// Program.LightPropagation.cs
// ─────────────────────────────────────────────────────────────────────────
//  NOUVEAU SYSTÈME DE PROPAGATION DE LUMIÈRE (façon Minecraft/Terraria)
// ─────────────────────────────────────────────────────────────────────────
//  Remplace les anciens "cercles" de lumière (rayon en pixels, alpha dégradé
//  radial) par une vraie propagation tuile par tuile : chaque source injecte
//  un niveau de lumière (0..MAX_LIGHT_LEVEL) dans sa tuile, puis ce niveau se
//  propage aux tuiles voisines en perdant de l'intensité à chaque pas — et
//  perd BEAUCOUP plus d'intensité en traversant une tuile non praticable
//  (mur, rocher... tout ce qui a une hitbox), ce qui empêche la lumière de
//  traverser les murs comme elle le faisait avec les cercles.
//
//  Ce fichier ne fait QUE le calcul (BFS multi-source). Le rendu (comment on
//  compose ça sur _lightMask / le shader existant) reste dans le fichier qui
//  contient DrawLighting — cette partie n'a pas encore été fournie, donc le
//  point de branchement exact (ComputeLightGrid → composition finale) est à
//  finaliser une fois ce fichier localisé.
// ─────────────────────────────────────────────────────────────────────────
#nullable enable
using System.Numerics;
using Raylib_cs;

namespace Soulfract
{
    public static partial class Program
    {
        //  Nombre de paliers de lumière (0 = noir total, MAX = pleine luminosité).
        // 16 paliers (comme Minecraft) donne un dégradé assez doux sans coûter cher en BFS.
        public const int MAX_LIGHT_LEVEL = 16;

        private const int LIGHT_LEVEL_SCALE = 10;

        private static readonly (int dx, int dy, bool diagonal)[] _lightNeighbors = new (int, int, bool)[]
        {
            (1, 0, false), (-1, 0, false), (0, 1, false), (0, -1, false),
            (1, 1, true), (1, -1, true), (-1, 1, true), (-1, -1, true)
        };

        //  Coûts en unités sous-tuile : un pas diagonal vaut environ sqrt(2) pas droits.
        private const int LIGHT_FALLOFF_OPEN = LIGHT_LEVEL_SCALE;
        private const int LIGHT_FALLOFF_DIAGONAL = 14;

        //  Résultat de la propagation pour une tuile : intensité normalisée [0..1] et couleur
        // moyenne pondérée des sources qui l'éclairent (utile pour les halos colorés : torche
        // orange, ampoule chaude, projectile magique, etc.)
        public struct TileLight
        {
            public float Intensity;
            public Color Color;
        }

        //  Cache de la dernière grille calculée + zone couverte, pour éviter de relancer un BFS
        // complet à chaque frame si rien n'a changé (voir InvalidateLightGrid()).
        private static Dictionary<(int x, int y), TileLight> _lightGridCache = new();
        private static bool _lightGridDirty = true;
        private static (int startX, int startY, int endX, int endY) _lightGridCachedBounds;
        private static int _lightGridCachedSourceSignature;

        private static bool IsVisibleLightSample(int tileX, int tileY)
        {
            if (!_lightGridCache.TryGetValue((tileX, tileY), out var light))
                return false;

            return (byte)(light.Color.R * light.Intensity) > 0
                || (byte)(light.Color.G * light.Intensity) > 0
                || (byte)(light.Color.B * light.Intensity) > 0;
        }

        public static bool IsTileLit(int tileX, int tileY)
        {
            if (!World.IsUnderground)
                return true;

            var (startX, startY, endX, endY) = _lightGridCachedBounds;
            int gridWidth = endX - startX + 1;
            int gridHeight = endY - startY + 1;
            if (gridWidth <= 0 || gridHeight <= 0)
                return false;

            int step = Math.Max(1, (int)Math.Ceiling(Math.Max(gridWidth, gridHeight) / (float)MAX_LIGHTMAP_DIM));
            int texWidth = (gridWidth + step - 1) / step;
            int texHeight = (gridHeight + step - 1) / step;

            // DrawLighting places the lightmap one tile higher and bilinear filtering can
            // carry light across texel edges, especially when several tiles share one texel.
            int mappedY = tileY + 1;
            if (tileX < startX || tileX > endX || mappedY < startY || mappedY > endY)
                return false;

            int centerTexX = (tileX - startX) / step;
            int centerTexY = (mappedY - startY) / step;
            for (int texY = Math.Max(0, centerTexY - 1); texY <= Math.Min(texHeight - 1, centerTexY + 1); texY++)
            {
                int sampleY = startY + texY * step + step / 2;
                for (int texX = Math.Max(0, centerTexX - 1); texX <= Math.Min(texWidth - 1, centerTexX + 1); texX++)
                {
                    int sampleX = startX + texX * step + step / 2;
                    if (IsVisibleLightSample(sampleX, sampleY))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Force le recalcul de la grille de lumière à la prochaine frame (à appeler quand une
        /// source de lumière apparaît/disparaît/bouge de façon discrète, ou qu'un mur est posé/
        /// détruit). Le joueur qui se déplace en continu n'a PAS besoin d'invalider : la fenêtre
        /// visible est recalculée de toute façon à chaque frame tant que la caméra bouge.
        /// </summary>
        public static void InvalidateLightGrid()
        {
            _lightGridDirty = true;
        }

        /// <summary>
        /// Calcule (ou renvoie depuis le cache) la grille de lumière propagée pour la zone de
        /// tuiles [startX..endX] x [startY..endY]. Multi-source : chaque entrée de `lights`
        /// (position monde, rayon en pixels, couleur) lance son propre flood-fill borné par son
        /// rayon converti en nombre de tuiles. Les intensités s'additionnent avec saturation et
        /// les couleurs sont moyennées selon la contribution de chaque source.
        /// </summary>
        public static Dictionary<(int x, int y), TileLight> ComputeLightGrid(
            List<(Vector2 worldPos, int radiusPixels, Color color)> lights,
            int startX, int startY, int endX, int endY)
        {
            var bounds = (startX, startY, endX, endY);
            int ts = Program.TileSize;
            var sourceSignatureBuilder = new HashCode();
            foreach (var (worldPos, radiusPixels, color) in lights)
            {
                sourceSignatureBuilder.Add((int)MathF.Floor(worldPos.X / ts));
                sourceSignatureBuilder.Add((int)MathF.Floor(worldPos.Y / ts));
                sourceSignatureBuilder.Add(Math.Max(1, radiusPixels / ts));
                sourceSignatureBuilder.Add(color.R);
                sourceSignatureBuilder.Add(color.G);
                sourceSignatureBuilder.Add(color.B);
                sourceSignatureBuilder.Add(color.A);
            }
            int sourceSignature = sourceSignatureBuilder.ToHashCode();
            if (!_lightGridDirty && bounds.Equals(_lightGridCachedBounds)
                && sourceSignature == _lightGridCachedSourceSignature)
                return _lightGridCache;

            //  Accumulateurs par tuile : somme saturée des intensités, et couleurs pondérées
            // par la contribution de chaque source.
            var levelByTile = new Dictionary<(int, int), int>();
            var colorAccum = new Dictionary<(int, int), (float r, float g, float b, float wsum)>();

            //  Petite marge autour de la zone visible pour que la lumière des sources juste
            // hors-écran continue de propager correctement jusqu'au bord.
            int margin = MAX_LIGHT_LEVEL + 1;
            int boundedStartX = startX - margin;
            int boundedStartY = startY - margin;
            int boundedEndX = endX + margin;
            int boundedEndY = endY + margin;

            var queue = new Queue<(int x, int y, int level, Color color)>();

            foreach (var (worldPos, radiusPixels, color) in lights)
            {
                int srcX = (int)Math.Floor(worldPos.X / ts);
                int srcY = (int)Math.Floor(worldPos.Y / ts);

                int radiusTiles = Math.Max(1, radiusPixels / ts);
                int startLevel = Math.Clamp(radiusTiles * LIGHT_LEVEL_SCALE, LIGHT_LEVEL_SCALE, MAX_LIGHT_LEVEL * LIGHT_LEVEL_SCALE);

                //  BFS dédié à cette source (visited local pour ne pas mélanger les niveaux de
                // deux sources différentes pendant la traversée : seul le résultat final cumule).
                var visited = new Dictionary<(int, int), int>();
                queue.Clear();
                queue.Enqueue((srcX, srcY, startLevel, color));
                visited[(srcX, srcY)] = startLevel;

                while (queue.Count > 0)
                {
                    var (cx, cy, level, col) = queue.Dequeue();
                    if (level <= 0) continue;
                    if (!visited.TryGetValue((cx, cy), out int bestLevel) || bestLevel != level) continue;
                    if (cx < boundedStartX || cx > boundedEndX || cy < boundedStartY || cy > boundedEndY) continue;

                    int currentObjectId = World.GetObjectIdAt(cx, cy);
                    var currentTileData = WorldTileRegistry.GetTile(currentObjectId);
                    if ((cx != srcX || cy != srcY) && currentTileData != null && !currentTileData.Walkable)
                        continue;

                    foreach (var (dx, dy, diagonal) in _lightNeighbors)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;

                        if (diagonal)
                        {
                            int sideXObjectId = World.GetObjectIdAt(cx + dx, cy);
                            int sideYObjectId = World.GetObjectIdAt(cx, cy + dy);
                            var sideXTile = WorldTileRegistry.GetTile(sideXObjectId);
                            var sideYTile = WorldTileRegistry.GetTile(sideYObjectId);
                            bool sideXBlocks = sideXTile != null && !sideXTile.Walkable;
                            bool sideYBlocks = sideYTile != null && !sideYTile.Walkable;
                            if (sideXBlocks && sideYBlocks)
                                continue;
                        }

                        int falloff = diagonal ? LIGHT_FALLOFF_DIAGONAL : LIGHT_FALLOFF_OPEN;
                        int newLevel = level - falloff;
                        if (newLevel <= 0) continue;

                        if (!visited.TryGetValue((nx, ny), out int existingLevel) || newLevel > existingLevel)
                        {
                            visited[(nx, ny)] = newLevel;
                            queue.Enqueue((nx, ny, newLevel, col));
                        }
                    }
                }

                // Chaque tuile ne contribue qu'une fois par source, avec le meilleur niveau
                // atteint après propagation autour des obstacles.
                foreach (var (tile, level) in visited)
                {
                    if (tile.Item1 < boundedStartX || tile.Item1 > boundedEndX
                        || tile.Item2 < boundedStartY || tile.Item2 > boundedEndY)
                        continue;

                    ApplyContribution(tile, level, color, levelByTile, colorAccum);
                }
            }

            //  Finalise : convertit (somme saturée, somme couleur pondérée) en TileLight normalisé.
            var result = new Dictionary<(int, int), TileLight>(levelByTile.Count);
            foreach (var kv in levelByTile)
            {
                float intensity = Math.Clamp((float)kv.Value / (MAX_LIGHT_LEVEL * LIGHT_LEVEL_SCALE), 0f, 1f);
                Color finalColor = new Color((byte)255, (byte)255, (byte)255, (byte)255);
                if (colorAccum.TryGetValue(kv.Key, out var acc) && acc.wsum > 0f)
                {
                    finalColor = new Color(
                        (byte)Math.Clamp(acc.r / acc.wsum, 0f, 255f),
                        (byte)Math.Clamp(acc.g / acc.wsum, 0f, 255f),
                        (byte)Math.Clamp(acc.b / acc.wsum, 0f, 255f),
                        (byte)255);
                }
                result[kv.Key] = new TileLight { Intensity = intensity, Color = finalColor };
            }

            _lightGridCache = result;
            _lightGridCachedBounds = bounds;
            _lightGridCachedSourceSignature = sourceSignature;
            _lightGridDirty = false;
            return result;
        }

        /// <summary>Ajoute la contribution d'une source à une tuile, sans dépasser la luminosité
        /// maximale, et pondère la teinte par l'intensité réellement reçue.</summary>
        private static void ApplyContribution(
            (int x, int y) tile, int level, Color color,
            Dictionary<(int, int), int> levelByTile,
            Dictionary<(int, int), (float r, float g, float b, float wsum)> colorAccum)
        {
            levelByTile.TryGetValue(tile, out int existing);
            int contribution = Math.Min(level, MAX_LIGHT_LEVEL * LIGHT_LEVEL_SCALE - existing);
            if (contribution <= 0) return;
            levelByTile[tile] = existing + contribution;

            float weight = contribution;
            if (colorAccum.TryGetValue(tile, out var acc))
                colorAccum[tile] = (acc.r + color.R * weight, acc.g + color.G * weight, acc.b + color.B * weight, acc.wsum + weight);
            else
                colorAccum[tile] = (color.R * weight, color.G * weight, color.B * weight, weight);
        }
    }
}
