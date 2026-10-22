// PerfStats.cs
//  Compteurs/chronos très légers pour le panneau de debug F3, afin de voir en jeu
// où part le temps de frame (IA vs rendu) et combien d'entités sont réellement
// traitées/dessinées, plutôt que de deviner à la lecture du code.
//
// Usage : PerfStats.BeginFrame() en tout début de frame, puis
// PerfStats.Time(PerfStats.Section.Update, () => { ... }) (ou StartSection/EndSection)
// autour des blocs à mesurer. Les valeurs affichées sont lissées sur quelques frames
// pour rester lisibles (sinon ça clignote trop vite pour être lu).
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Soulfract
{
    public static class PerfStats
    {
        public enum Section
        {
            EntityUpdate,   // IA/logique de toutes les entités (Entity.Update)
            FrameDraw,      // Draw complet, BeginDrawing -> EndDrawing inclus
            WorldPreDraw,   // Dessins monde faits avant la préparation du monde
            LightGridPrepare, // Pré-calcul de l'éclairage avant World.DrawWorld
            WorldDraw,      // World.DrawWorld (tuiles, objets, tri, entités)
            WorldDrawSetup, // Préparation des bornes et buffers avant la grille de tuiles
            TileGridFill,   // World.DrawWorld : collecte des données par tuile (heights/ground/objets/...)
                            // avant tout dessin — voir la boucle juste après EnsureRenderTileBuffers
            TerrainDraw,        // Dessin direct du terrain, des falaises et éléments avant la liste de rendu
            WorldRenderPrepare, // Construction/préparation des éléments de rendu du monde
            RenderItemSort,     // Tri Y des éléments de rendu du monde
            RenderItemDraw,     // Exécution des éléments de rendu triés
            WorldDrawTail,      // Reste de World.DrawWorld après le dessin trié
            WorldDebugOverlay,  // Hitboxes, chemins et diagnostics dessinés avec F3
            InteractionPrompts, // Recherche et dessin des invites d'interaction
            EntityRender,       // Dessin des squelettes d'entités, hors appels imbriqués
            WorldEffects,       // Effets dessinés en coordonnées monde après World.DrawWorld
            ScreenLighting,     // Masque de lumière et filtre jour/nuit
            ScreenOverlays,     // HUD, menus et autres interfaces après la caméra monde
            ChunkStreaming, // World.UpdateActiveChunks (chargement/génération des chunks proches du joueur)
            ChunkDiskLoad, // Lecture/désérialisation d'un chunk sauvegardé
            ChunkGeneration, // Génération procédurale d'un chunk
            ChunkEntityMerge, // Transfert des entités d'un chunk vers la liste active
            ChunkUnloadWork, // Préparation, sauvegarde et retrait des chunks hors zone
            GameplayUpdateTotal, // Toute la fonction UpdateGameplay (englobe EntityUpdate + ChunkStreaming +
                                 // tout le reste : crops, boats, campfires, UI, quêtes...). Sert seulement
                                 // à calculer par soustraction le temps "non catégorisé" de la frame — voir
                                 // PerfStats.GetOtherUpdateMs().
        }

        //  Un chrono PAR section (et non un seul chrono partagé) : GameplayUpdateTotal englobe
        // EntityUpdate et ChunkStreaming, donc StartSection(EntityUpdate) est appelé PENDANT que
        // GameplayUpdateTotal tourne déjà. Avec un seul Stopwatch partagé, ce StartSection interne
        // aurait fait un Restart() et effacé le temps déjà accumulé par la section englobante.
        private static readonly Dictionary<Section, Stopwatch> _stopwatches = new();
        private static readonly Dictionary<Section, int> _sectionDepths = new();

        private static Stopwatch GetOrCreateStopwatch(Section section)
        {
            if (!_stopwatches.TryGetValue(section, out var sw))
            {
                sw = new Stopwatch();
                _stopwatches[section] = sw;
            }
            return sw;
        }

        // Temps (ms) mesuré sur la dernière frame, par section.
        private static readonly Dictionary<Section, float> _lastFrameMs = new();
        // Moyenne lissée (EMA) affichée dans le panneau debug, moins nerveuse que la valeur brute.
        private static readonly Dictionary<Section, float> _smoothedMs = new();
        private const float SmoothingFactor = 0.15f; // plus petit = plus lisse/lent à réagir

        // Compteurs remis à zéro à chaque frame (incrémentés depuis les endroits mesurés).
        public static int EntitiesTotal { get; private set; }
        public static int EntitiesFullAiUpdated { get; private set; }
        public static int EntitiesDrawnThisFrame { get; private set; }
        public static int RenderItemsThisFrame { get; private set; }
        public static int TopLevelEntityRenderCallsThisFrame { get; private set; }

        private static int _entitiesFullAiUpdatedAccum;
        private static int _entityRenderDepth;
        private static long _entityRenderTicksAccum;
        private static int _topLevelEntityRenderCallsAccum;

        public static void BeginFrame(int entitiesTotal)
        {
            EntitiesTotal = entitiesTotal;
            EntitiesFullAiUpdated = _entitiesFullAiUpdatedAccum;
            EntitiesDrawnThisFrame = 0;
            RenderItemsThisFrame = 0;
            _entitiesFullAiUpdatedAccum = 0;
        }

        public static void NotifyFullAiUpdate() => _entitiesFullAiUpdatedAccum++;
        public static void NotifyEntityDrawn() => EntitiesDrawnThisFrame++;
        public static void NotifyRenderItems(int count) => RenderItemsThisFrame = count;

        public static bool BeginEntityRender() => _entityRenderDepth++ == 0;

        public static void EndEntityRender(bool measure, long startTimestamp)
        {
            if (_entityRenderDepth > 0)
                _entityRenderDepth--;
            if (measure)
            {
                _entityRenderTicksAccum += Stopwatch.GetTimestamp() - startTimestamp;
                _topLevelEntityRenderCallsAccum++;
            }
        }

        public static void EndEntityRenderFrame()
        {
            RecordMs(Section.EntityRender,
                (float)(_entityRenderTicksAccum * 1000d / Stopwatch.Frequency));
            TopLevelEntityRenderCallsThisFrame = _topLevelEntityRenderCallsAccum;
            _entityRenderTicksAccum = 0;
            _topLevelEntityRenderCallsAccum = 0;
        }

        public static void StartSection(Section section)
        {
            if (_sectionDepths.TryGetValue(section, out int depth) && depth > 0)
            {
                _sectionDepths[section] = depth + 1;
                return;
            }

            _sectionDepths[section] = 1;
            GetOrCreateStopwatch(section).Restart();
        }

        public static void EndSection(Section section)
        {
            if (_sectionDepths.TryGetValue(section, out int depth) && depth > 1)
            {
                _sectionDepths[section] = depth - 1;
                return;
            }

            _sectionDepths.Remove(section);
            var sw = GetOrCreateStopwatch(section);
            sw.Stop();
            RecordMs(section, (float)sw.Elapsed.TotalMilliseconds);
        }

        public static long GetTimestamp() => Stopwatch.GetTimestamp();

        private static void RecordMs(Section section, float ms)
        {
            _lastFrameMs[section] = ms;
            if (_smoothedMs.TryGetValue(section, out float prev))
                _smoothedMs[section] = prev + (ms - prev) * SmoothingFactor;
            else
                _smoothedMs[section] = ms;
        }

        public static float GetSmoothedMs(Section section) =>
            _smoothedMs.TryGetValue(section, out float ms) ? ms : 0f;

        public static float GetLastFrameMs(Section section) =>
            _lastFrameMs.TryGetValue(section, out float ms) ? ms : 0f;

        //  PERF : GameplayUpdateTotal englobe TOUTE la fonction UpdateGameplay, y compris
        // EntityUpdate et ChunkStreaming qui sont déjà mesurés à part. On soustrait donc ces
        // deux-là pour obtenir le temps passé dans tout ce qui n'est PAS encore instrumenté
        // individuellement (crops, boats, campfires, UI, quêtes, particules...). Un chiffre élevé
        // ici veut dire "il faut découper GameplayUpdateTotal en sections plus précises",
        // pas "le jeu est intrinsèquement lent à cet endroit".
        public static float GetOtherUpdateMs(bool smoothed = true)
        {
            float total = smoothed ? GetSmoothedMs(Section.GameplayUpdateTotal) : GetLastFrameMs(Section.GameplayUpdateTotal);
            float entity = smoothed ? GetSmoothedMs(Section.EntityUpdate) : GetLastFrameMs(Section.EntityUpdate);
            float chunks = smoothed ? GetSmoothedMs(Section.ChunkStreaming) : GetLastFrameMs(Section.ChunkStreaming);
            return MathF.Max(0f, total - entity - chunks);
        }
    }
}
