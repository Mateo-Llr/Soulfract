// EntityRenderer.cs - Version avec support complet des accessoires et affichage du visage
using Raylib_cs;
using System.Buffers;
using System.IO;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static class EntityRenderer
    {
        //  OPTIM PERF : GetValueOrDefault(key, new HashSet<string>()) évalue son argument par
        // défaut À CHAQUE appel, même quand la clé existe déjà dans le dictionnaire — donc un
        // HashSet<string> vide était réalloué pour CHAQUE entité dessinée à CHAQUE frame. On
        // réutilise une instance vide unique et immuable (jamais mutée) à la place.
        private static readonly HashSet<string> _emptyLegPartsSet = new();
        private const float BASE_ENTITY_SCALE = 2.8f;

        //  PERF (dézoom) : niveau de zoom de la caméra courante, poussé une fois par frame par
        // World.DrawWorld via SetCurrentZoomLevel. Sert de base au LOD de DrawEntity ci-dessous
        // (voir "lowDetail") : à faible zoom, les détails fins (bijoux, traits aléatoires,
        // couches de teinture secondaires) sont invisibles à l'écran mais coûtaient quand même
        // un plein lot d'appels de dessin par PNJ — multiplié par beaucoup plus de PNJ visibles
        // simultanément puisque l'écran couvre une zone du monde bien plus grande en dézoomant.
        private static float _currentZoomLevel = 1f;
        private static readonly Dictionary<uint, List<Vector2>> _bloodOpaquePixelCache = new();
        private static Shader _ghostShader;
        private static bool _ghostShaderLoaded;
        private static bool _ghostShaderFailed;
        private static int _ghostTimeLocation = -1;
        private static int _ghostEyeGlowModeLocation = -1;
        private static int _ghostEyeGlowOpacityLocation = -1;
        //  Le motif du voile fantomatique doit être ancré au MONDE, pas à l'écran : sans ça,
        // le motif "glisse" visiblement quand la caméra bouge (panoramique/zoom), puisqu'un
        // même pixel écran ne correspond plus au même point du monde d'une frame à l'autre.
        // On transmet donc la transformation de la caméra courante (Camera2D de raylib :
        // screen = (world - target) * zoom + offset) au shader pour qu'il puisse inverser
        // gl_FragCoord vers une position monde stable, cohérente sur toute la silhouette de
        // l'entité ET fixe par rapport au monde quelle que soit la caméra.
        private static int _ghostCameraTargetLocation = -1;
        private static int _ghostCameraOffsetLocation = -1;
        private static int _ghostCameraZoomLocation = -1;
        private static int _ghostScreenHeightLocation = -1;

        //  Une entité fantôme est constituée de PLUSIEURS textures superposées (corps, tête,
        // équipement, cheveux...), chacune dessinée avec transparence via ghost.fs. Si on
        // applique le shader indépendamment à chaque calque, les zones de recouvrement (ex :
        // jointure haut/bas de jambe) se mélangent DEUX FOIS (une fois par calque), ce qui
        // produit un trait plus clair bien visible à ces jointures : c'est un problème de
        // double alpha-blending, pas seulement de motif de bruit incohérent.
        // La solution : dessiner toute l'entité normalement (opaque, SANS shader, exactement
        // comme en rendu non-fantôme) dans une texture hors-écran, puis n'appliquer le shader
        // fantôme qu'UNE SEULE FOIS sur cette silhouette déjà aplatie, en un seul DrawTexturePro.
        // Comme il n'y a alors plus qu'un seul pixel semi-transparent par endroit (au lieu de
        // plusieurs empilés), le recouvrement des calques redevient propre, exactement comme en
        // rendu normal.
        private static RenderTexture2D _ghostCompositeTexture;
        private static bool _ghostCompositeTextureLoaded;
        private static int _ghostCompositeAllocW;
        private static int _ghostCompositeAllocH;
        private static bool _ghostCompositeActive;
        private static Vector2 _ghostCompositeWorldOrigin;
        private static int _ghostCompositeW;
        private static int _ghostCompositeH;

        private static void EnsureGhostCompositeCapacity(int w, int h)
        {
            if (_ghostCompositeTextureLoaded && _ghostCompositeAllocW >= w && _ghostCompositeAllocH >= h)
                return;
            if (_ghostCompositeTextureLoaded)
                Raylib.UnloadRenderTexture(_ghostCompositeTexture);
            // On ne rétrécit jamais (on garde le plus grand format déjà alloué) pour éviter de
            // recréer la texture à chaque frame si la taille oscille légèrement d'une entité à
            // l'autre : un seul buffer est réutilisé séquentiellement pour toutes les entités
            // fantômes de la frame (le rendu est mono-thread, une composition se termine
            // toujours avant que la suivante ne démarre).
            _ghostCompositeAllocW = Math.Max(w, _ghostCompositeAllocW);
            _ghostCompositeAllocH = Math.Max(h, _ghostCompositeAllocH);
            _ghostCompositeTexture = Raylib.LoadRenderTexture(_ghostCompositeAllocW, _ghostCompositeAllocH);
            _ghostCompositeTextureLoaded = true;
        }

        //  Démarre la composition : tout ce qui est dessiné ENTRE cet appel et EndGhostComposite
        // (ou entre ResumeGhostComposite et le Suspend/End suivant) part vers la texture
        // hors-écran plutôt que vers l'écran, en repère "local" à l'entité (worldOrigin -> pixel
        // (0,0)). Le code de dessin existant (DrawTextureProWithFlash, etc.) n'a besoin d'AUCUNE
        // modification : il continue à utiliser des coordonnées MONDE identiques à celles du
        // rendu normal, c'est la caméra locale ci-dessous qui les reprojette dans la texture.
        private static bool BeginGhostComposite(Vector2 worldOrigin, int width, int height)
        {
            if (width <= 0 || height <= 0) return false;
            EnsureGhostShaderLoaded();
            if (_ghostShaderFailed) return false;

            EnsureGhostCompositeCapacity(width, height);
            _ghostCompositeWorldOrigin = worldOrigin;
            _ghostCompositeW = width;
            _ghostCompositeH = height;

            Raylib.BeginTextureMode(_ghostCompositeTexture);
            Raylib.ClearBackground(new Color(0, 0, 0, 0));
            Raylib.BeginMode2D(new Camera2D { Target = worldOrigin, Offset = Vector2.Zero, Zoom = 1f, Rotation = 0f });
            _ghostCompositeActive = true;
            return true;
        }

        //  À utiliser pour tout dessin qui ne fait PAS partie de la silhouette à aplatir (une
        // monture, une créature portée, le halo/traînée des yeux fantômes...) : on interrompt
        // temporairement la composition, on restaure la caméra "monde" normale pour que ce
        // dessin atterrisse correctement à l'écran, puis on reprend avec ResumeGhostComposite.
        private static void SuspendGhostComposite()
        {
            if (!_ghostCompositeActive) return;
            Raylib.EndMode2D();
            Raylib.EndTextureMode();
            _ghostCompositeActive = false;
            Raylib.BeginMode2D(Program.GetCurrentCamera());
        }

        private static void ResumeGhostComposite()
        {
            if (_ghostCompositeActive) return;
            if (_ghostCompositeW <= 0 || _ghostCompositeH <= 0) return;
            Raylib.EndMode2D(); // referme la caméra "monde" rétablie par SuspendGhostComposite
            Raylib.BeginTextureMode(_ghostCompositeTexture);
            Raylib.BeginMode2D(new Camera2D { Target = _ghostCompositeWorldOrigin, Offset = Vector2.Zero, Zoom = 1f, Rotation = 0f });
            _ghostCompositeActive = true;
        }

        //  Termine la composition : referme la texture hors-écran, restaure la caméra "monde",
        // puis dessine la silhouette aplatie en UN SEUL DrawTexturePro avec le shader fantôme
        // actif. Le voile/la transparence ne sont donc calculés qu'UNE FOIS par pixel final,
        // quel que soit le nombre de calques qui composaient l'entité à l'origine.
        private static void EndGhostComposite(Color tint)
        {
            if (_ghostCompositeW <= 0 || _ghostCompositeH <= 0)
            {
                _ghostCompositeActive = false;
                return;
            }
            if (_ghostCompositeActive)
            {
                Raylib.EndMode2D();
                Raylib.EndTextureMode();
                _ghostCompositeActive = false;
                Raylib.BeginMode2D(Program.GetCurrentCamera());
            }

            if (BeginGhostShader())
            {
                // RenderTexture2D est stocké verticalement inversé (convention OpenGL) : on
                // compense avec une hauteur source négative pour redessiner la silhouette à
                // l'endroit.
                Rectangle src = new(0, 0, _ghostCompositeW, -_ghostCompositeH);
                Rectangle dst = new(_ghostCompositeWorldOrigin.X, _ghostCompositeWorldOrigin.Y, _ghostCompositeW, _ghostCompositeH);
                Raylib.DrawTexturePro(_ghostCompositeTexture.Texture, src, dst, Vector2.Zero, 0f, tint);
                EndGhostShader();
            }

            _ghostCompositeW = 0;
            _ghostCompositeH = 0;
        }
        private static float _nextSpiritEyeTrailCleanupTime;

        private readonly record struct SpiritEyeTrailSample(Rectangle Source, Rectangle Destination, float Rotation, float CreatedAt);

        private sealed class SpiritEyeTrailState
        {
            public List<SpiritEyeTrailSample> Samples = new();
            public Rectangle PreviousSource;
            public Rectangle PreviousDestination;
            public float PreviousRotation;
            public float LastCaptureTime;
            public float LastSeenTime;
            public bool HasPrevious;
        }

        private static readonly Dictionary<string, SpiritEyeTrailState> _spiritEyeTrails = new(StringComparer.Ordinal);

        public static void SetCurrentZoomLevel(float zoom) => _currentZoomLevel = zoom;

        private static void EnsureGhostShaderLoaded()
        {
            if (_ghostShaderLoaded || _ghostShaderFailed) return;
            _ghostShaderLoaded = true;
            const string shaderPath = "assets/shaders/ghost.fs";
            if (!File.Exists(shaderPath))
            {
                _ghostShaderFailed = true;
                return;
            }

            _ghostShader = Raylib.LoadShader(null, shaderPath);
            if (_ghostShader.Id == 0)
            {
                _ghostShaderFailed = true;
                return;
            }

            _ghostTimeLocation = Raylib.GetShaderLocation(_ghostShader, "uTime");
            _ghostEyeGlowModeLocation = Raylib.GetShaderLocation(_ghostShader, "uEyeGlowMode");
            _ghostEyeGlowOpacityLocation = Raylib.GetShaderLocation(_ghostShader, "uEyeGlowOpacity");
            _ghostCameraTargetLocation = Raylib.GetShaderLocation(_ghostShader, "uCameraTarget");
            _ghostCameraOffsetLocation = Raylib.GetShaderLocation(_ghostShader, "uCameraOffset");
            _ghostCameraZoomLocation = Raylib.GetShaderLocation(_ghostShader, "uCameraZoom");
            _ghostScreenHeightLocation = Raylib.GetShaderLocation(_ghostShader, "uScreenHeight");
        }

        private static bool BeginGhostShader()
        {
            EnsureGhostShaderLoaded();
            if (_ghostShaderFailed) return false;

            if (_ghostTimeLocation >= 0)
                Raylib.SetShaderValue(_ghostShader, _ghostTimeLocation, (float)Raylib.GetTime(), ShaderUniformDataType.Float);

            // Caméra courante : nécessaire pour que le shader puisse reconvertir gl_FragCoord
            // (espace écran) en position MONDE, afin que le motif du voile reste fixe par
            // rapport au monde et ne "glisse" plus quand la caméra se déplace ou zoome.
            Camera2D camera = Program.GetCurrentCamera();
            if (_ghostCameraTargetLocation >= 0)
                Raylib.SetShaderValue(_ghostShader, _ghostCameraTargetLocation, camera.Target, ShaderUniformDataType.Vec2);
            if (_ghostCameraOffsetLocation >= 0)
                Raylib.SetShaderValue(_ghostShader, _ghostCameraOffsetLocation, camera.Offset, ShaderUniformDataType.Vec2);
            if (_ghostCameraZoomLocation >= 0)
                Raylib.SetShaderValue(_ghostShader, _ghostCameraZoomLocation, camera.Zoom, ShaderUniformDataType.Float);
            if (_ghostScreenHeightLocation >= 0)
                Raylib.SetShaderValue(_ghostShader, _ghostScreenHeightLocation, (float)Raylib.GetScreenHeight(), ShaderUniformDataType.Float);

            Raylib.BeginShaderMode(_ghostShader);
            if (_ghostEyeGlowModeLocation >= 0)
                Raylib.SetShaderValue(_ghostShader, _ghostEyeGlowModeLocation, 0f, ShaderUniformDataType.Float);
            if (_ghostEyeGlowOpacityLocation >= 0)
                Raylib.SetShaderValue(_ghostShader, _ghostEyeGlowOpacityLocation, 1f, ShaderUniformDataType.Float);
            return true;
        }

        private static void EndGhostShader() => Raylib.EndShaderMode();

        private static bool DrawSpiritEyeGlow(Texture2D eyeTexture, Rectangle source, Rectangle destination, Vector2 origin, float rotation, float opacity)
        {
            EnsureGhostShaderLoaded();
            if (_ghostShaderFailed)
                return false;

            Raylib.BeginShaderMode(_ghostShader);
            if (_ghostEyeGlowModeLocation >= 0)
                Raylib.SetShaderValue(_ghostShader, _ghostEyeGlowModeLocation, 1f, ShaderUniformDataType.Float);
            if (_ghostEyeGlowOpacityLocation >= 0)
                Raylib.SetShaderValue(_ghostShader, _ghostEyeGlowOpacityLocation, Math.Clamp(opacity, 0f, 1f), ShaderUniformDataType.Float);
            Raylib.DrawTexturePro(eyeTexture, source, destination, origin, rotation, Color.White);
            Raylib.EndShaderMode();
            return true;
        }

        private static void DrawSpiritEyeTrail(string? entityKey, Texture2D eyeTexture, Rectangle source, Rectangle destination, float rotation)
        {
            if (string.IsNullOrEmpty(entityKey))
                return;

            float now = (float)Raylib.GetTime();
            if (!_spiritEyeTrails.TryGetValue(entityKey, out var state))
            {
                state = new SpiritEyeTrailState();
                _spiritEyeTrails[entityKey] = state;
            }

            if (state.HasPrevious)
            {
                Vector2 previousCenter = new(state.PreviousDestination.X + state.PreviousDestination.Width * 0.5f,
                    state.PreviousDestination.Y + state.PreviousDestination.Height * 0.5f);
                Vector2 currentCenter = new(destination.X + destination.Width * 0.5f, destination.Y + destination.Height * 0.5f);
                bool turnedAround = Math.Sign(state.PreviousSource.Width) != Math.Sign(source.Width);
                if ((Vector2.DistanceSquared(previousCenter, currentCenter) >= 2.25f || turnedAround)
                    && now - state.LastCaptureTime >= 0.035f)
                {
                    state.Samples.Add(new SpiritEyeTrailSample(state.PreviousSource, state.PreviousDestination,
                        state.PreviousRotation, now));
                    state.LastCaptureTime = now;
                }
            }

            const float trailLifetime = 0.24f;
            for (int i = state.Samples.Count - 1; i >= 0; i--)
            {
                SpiritEyeTrailSample sample = state.Samples[i];
                float age = now - sample.CreatedAt;
                if (age >= trailLifetime)
                {
                    state.Samples.RemoveAt(i);
                    continue;
                }

                float opacity = 0.38f * (1f - age / trailLifetime);
                Vector2 sampleOrigin = new(sample.Destination.Width * 0.5f, sample.Destination.Height * 0.5f);
                DrawSpiritEyeGlow(eyeTexture, sample.Source, sample.Destination, sampleOrigin, sample.Rotation, opacity);
            }

            state.PreviousSource = source;
            state.PreviousDestination = destination;
            state.PreviousRotation = rotation;
            state.LastSeenTime = now;
            state.HasPrevious = true;

            if (now >= _nextSpiritEyeTrailCleanupTime)
            {
                foreach (string staleKey in _spiritEyeTrails
                    .Where(entry => now - entry.Value.LastSeenTime > 2f)
                    .Select(entry => entry.Key)
                    .ToArray())
                {
                    _spiritEyeTrails.Remove(staleKey);
                }
                _nextSpiritEyeTrailCleanupTime = now + 2f;
            }
        }

        public static void SpawnBloodDrips(Entity entity, float severity, string? renderSpecies = null,
            float customScale = 1f, Vector2? visualPosition = null, bool inWater = false)
        {
            if (Program.TileSize <= 0)
                return;

            string speciesKey = ResolveRenderSpecies(renderSpecies ?? entity.Species).ToLowerInvariant();
            if (!SpeciesData.Skeletons.TryGetValue(speciesKey, out var skeleton) || skeleton.Count == 0)
                return;

            AnimalBodyPart? bodyPart = skeleton.FirstOrDefault(part => part.Name == "body" && part.Texture.Id != 0)
                ?? skeleton.FirstOrDefault(part => part.Texture.Id != 0);
            if (bodyPart == null)
                return;

            List<Vector2> opaquePixels = GetBloodOpaquePixels(bodyPart.Texture);
            if (opaquePixels.Count == 0)
                return;

            severity = Math.Clamp(severity, 0f, 1f);
            int dropletCount = Math.Clamp(1 + (int)MathF.Ceiling(severity * 3f), 1, 4);
            float facing = entity.Facing < 0f ? -1f : 1f;
            float scale = BASE_ENTITY_SCALE * customScale * (SpeciesData.GetSpeciesInfo(speciesKey)?.Scale ?? 1f);
            Vector2 partOffset = GetStaticPartOffset(skeleton, bodyPart, scale, facing);
            float waterOffset = inWater ? SpeciesData.LegHeights.GetValueOrDefault(speciesKey, 0f) : 0f;
            Vector2 partCenter = (visualPosition ?? entity.WorldPos) + partOffset + new Vector2(0f, waterOffset);

            Vector2 groundPosition = visualPosition ?? entity.WorldPos;
            int tileX = (int)MathF.Floor(groundPosition.X / Program.TileSize);
            int tileY = (int)MathF.Floor(groundPosition.Y / Program.TileSize);
            float groundY = groundPosition.Y - World.GetHeightAt(tileX, tileY) * Program.TileSize / 4f + Program.FeetOffsetY;
            List<Particle> particles = Program.GetParticleList();

            for (int i = 0; i < dropletCount; i++)
            {
                Vector2 pixel = opaquePixels[Random.Shared.Next(opaquePixels.Count)];
                Vector2 pixelOffset = new(
                    (pixel.X - bodyPart.Texture.Width * 0.5f) * scale * facing,
                    (pixel.Y - bodyPart.Texture.Height * 0.5f) * scale);
                Vector2 spawnPosition = partCenter + pixelOffset;
                spawnPosition.Y = Math.Min(spawnPosition.Y, groundY - 14f);

                var droplet = new Particle(
                    spawnPosition,
                    new Vector2((float)(Random.Shared.NextDouble() * 20f - 10f), (float)Random.Shared.NextDouble() * 8f),
                    new Color((byte)Random.Shared.Next(135, 190), (byte)Random.Shared.Next(8, 25), (byte)Random.Shared.Next(12, 32), (byte)230),
                    3f + severity * 2f + (float)Random.Shared.NextDouble(),
                    2.5f);
                droplet.GravityY = 300f;
                droplet.Drag = 0.985f;
                droplet.GroundY = groundY;
                particles.Add(droplet);
            }
        }

        private static List<Vector2> GetBloodOpaquePixels(Texture2D texture)
        {
            if (_bloodOpaquePixelCache.TryGetValue(texture.Id, out var cachedPixels))
                return cachedPixels;

            var pixels = new List<Vector2>(96);
            Image image = Raylib.LoadImageFromTexture(texture);
            int opaqueCount = 0;
            const int MAX_SAMPLES = 96;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (Raylib.GetImageColor(image, x, y).A <= 8)
                        continue;

                    opaqueCount++;
                    var pixel = new Vector2(x + 0.5f, y + 0.5f);
                    if (pixels.Count < MAX_SAMPLES)
                    {
                        pixels.Add(pixel);
                    }
                    else
                    {
                        int replacementIndex = Random.Shared.Next(opaqueCount);
                        if (replacementIndex < MAX_SAMPLES)
                            pixels[replacementIndex] = pixel;
                    }
                }
            }
            Raylib.UnloadImage(image);
            _bloodOpaquePixelCache[texture.Id] = pixels;
            return pixels;
        }

        private static Vector2 GetStaticPartOffset(List<AnimalBodyPart> skeleton, AnimalBodyPart part, float scale, float facing)
        {
            var partsByName = skeleton.ToDictionary(candidate => candidate.Name, StringComparer.Ordinal);
            var hierarchy = new List<AnimalBodyPart>();
            var current = part;
            hierarchy.Add(current);
            while (!string.IsNullOrEmpty(current.ParentName) && partsByName.TryGetValue(current.ParentName, out var parent))
            {
                hierarchy.Add(parent);
                current = parent;
            }
            hierarchy.Reverse();

            Vector2 offset = Vector2.Zero;
            float parentRotation = 0f;
            bool hasParent = false;
            foreach (var currentPart in hierarchy)
            {
                Vector2 localOffset = currentPart.BasePos * scale;
                if (facing < 0f)
                    localOffset.X = -localOffset.X;

                offset = hasParent ? offset + RotateVec(localOffset, parentRotation) : localOffset;
                parentRotation += currentPart.BaseRot * facing;
                hasParent = true;
            }
            return offset;
        }
        
        private static Dictionary<string, Texture2D> _heldItemTextureCache = new();
        private static HashSet<uint> _heldItemFallbackTextureIds = new();
        private static Dictionary<string, Texture2D> _offHandItemTextureCache = new();
        private static Dictionary<string, Equipment> _equipmentCache = new();
        private static Dictionary<string, int> _itemNameToIdCache = new();
        private static Dictionary<string, EquipmentRenderState> _equipmentRenderStateCache = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Dictionary<string,int>> _partIndexCache = new();
        private static Dictionary<string, (float MaxBaseX, float MaxBaseY)> _skeletonExtentCache = new(StringComparer.OrdinalIgnoreCase);
        private sealed class SpeciesFeatureRenderCache
        {
            public Dictionary<string, SpeciesData.RandomFeatureJson> ReplaceByPart { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, List<SpeciesData.RandomFeatureJson>> OverlaysByPart { get; } = new(StringComparer.Ordinal);
        }
        private static Dictionary<string, SpeciesFeatureRenderCache> _speciesFeatureRenderCache = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Texture2D> _speciesEyeTextureCache = new(StringComparer.OrdinalIgnoreCase);
        //  Optimisation affichage villages : noms d'animations disponibles par espèce,
        // calculés une seule fois au lieu d'un .Any() LINQ répété à chaque PNJ/chaque frame.
        private static Dictionary<string, HashSet<string>> _availableAnimNamesCache = new();

        private sealed class EquipmentRenderState
        {
            public string CacheKey = "";
            public Item? BodyEquipItem;
            public Item? LegsEquipItem;
            public Item? HeadEquipItem;
            public Item? BackpackEquipItem;
            public Item? EarringItem;
            public Item? NecklaceItem;
            public Item? GlassesItem;
            public Item? SocksItem;
            public int BodyLayers;
            public int LegsLayers;
            public int HeadLayers;
            public int BackpackLayers;
            public int EarringLayers;
            public int NecklaceLayers;
            public int GlassesLayers;
            public int SocksLayers;
            public ArmorCategory HeadArmorCategory = ArmorCategory.None;
            public bool IsFullHelmet;
        }

        private static Dictionary<Guid, Entity.WormBossState> _lastWormState = new();
        private static Dictionary<Guid, Dictionary<int, bool>> _wormEntryParticlesSpawned = new();

        // ==================== WORM BOSS : rendu segmenté (trail-based) ====================
        // Le ver ne suit pas le système de squelette classique (parties fixes en hiérarchie
        // parent/enfant) : le nombre de segments visibles varie en continu à l'écran (sortie/
        // rentrée progressive). On calcule donc directement la position de chaque segment (arc décalé dans le temps), sans
        // passer par DrawEntity/SpeciesData.Skeletons.
        private static Dictionary<string, Texture2D> _wormTextureCache = new(StringComparer.OrdinalIgnoreCase);

        // Convention de fichiers : assets/bosses/worm/gem_wormhead.png / gem_wormbody.png / gem_wormtail.png
        // (fallback sur assets/animals/Worm/... si tu préfères ranger les textures avec les autres animaux)
        private static Texture2D GetWormTexture(string partSuffix)
        {
            string key = $"gem_worm{partSuffix}";
            if (_wormTextureCache.TryGetValue(key, out var cached) && cached.Id != 0)
                return cached;

            string[] candidates =
            {
                $"assets/bosses/worm/{key}.png",
                $"assets/animals/Worm/{key}.png",
            };

            Texture2D tex = default;
            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    tex = Raylib.LoadTexture(path);
                    if (tex.Id != 0)
                    {
                        Raylib.SetTextureFilter(tex, TextureFilter.Point);
                        break;
                    }
                }
            }
            _wormTextureCache[key] = tex;
            return tex;
        }

        // Position d'un point sur l'arc parabolique sortie -> entrée, pour un paramètre t in [0,1].
        private static Vector2 WormArcPos(Vector2 exit, Vector2 entry, float arcHeight, float t)
        {
            Vector2 flat = Vector2.Lerp(exit, entry, t);
            float arc = 4f * arcHeight * t * (1f - t);
            return new Vector2(flat.X, flat.Y - arc);
        }

        // Angle de la tangente locale à l'arc en t (par différence finie), pour orienter le sprite.
        private static float WormArcAngleDeg(Vector2 exit, Vector2 entry, float arcHeight, float t)
        {
            float t1 = Math.Clamp(t - 0.02f, 0f, 1f);
            float t2 = Math.Clamp(t + 0.02f, 0f, 1f);
            if (t2 - t1 < 0.0001f) return 0f;
            Vector2 a = WormArcPos(exit, entry, arcHeight, t1);
            Vector2 b = WormArcPos(exit, entry, arcHeight, t2);
            Vector2 dir = b - a;
            if (dir.LengthSquared() < 0.0001f) return 0f;
            return MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);
        }

        // Progression propre au segment i : démarre avec un retard de i * stagger secondes par
        // rapport à la tête, puis parcourt son propre arc en WM_HEAD_LEAP_DURATION secondes.
        // Renvoie une valeur dans ]0,1[ tant qu'il est visible, ou <=0 / >=1 s'il n'est pas encore
        // sorti / a déjà atteint le sol (les deux cas doivent être invisibles).
        private static float WormSegmentT(float elapsed, int index, float stagger, float headDuration)
        {
            float local = elapsed - index * stagger;
            return local / headDuration;
        }

        // Dessine le ver de terre géant (boss) : chaque segment suit le même arc que la tête,
        // décalé dans le temps -> il n'apparaît qu'à son tour et ne disparaît qu'en touchant le sol.
        // customScale suit SpeciesInfo.Scale comme les autres créatures (Worm dans species.json).
        public static void DrawWormBoss(Entity entity, float customScale = 1f)
        {
            if (entity == null || !entity.WormHeadVisible) return;

            Texture2D headTex = GetWormTexture("head");
            Texture2D bodyTex = GetWormTexture("body");
            Texture2D tailTex = GetWormTexture("tail");

            Vector2 exit = entity.WormExitPoint;
            Vector2 entry = entity.WormEntryPoint;
            float elapsed = entity.WormElapsedTime;
            float headDuration = Entity.WM_HEAD_LEAP_DURATION;
            float stagger = entity.WormSegmentStagger;
            float arcHeight = entity.WormArcHeight;
            int segmentCount = Entity.WM_SEGMENT_COUNT;

            const float WORM_SIZE_MULTIPLIER = 1.0f;
            float scale = BASE_ENTITY_SCALE * customScale * WORM_SIZE_MULTIPLIER;

            // -------------------------------------------------------------
            // Gestion des particules d'impact à l'entrée (par segment)
            // -------------------------------------------------------------
            Entity.WormBossState currentState = entity.WormState;
            Guid id = entity.NetId;

            if (currentState == Entity.WormBossState.Emerging)
            {
                if (!_lastWormState.TryGetValue(id, out var lastState) || lastState != currentState)
                {
                    _wormEntryParticlesSpawned[id] = new Dictionary<int, bool>();
                }
            }
            _lastWormState[id] = currentState;

            if (!_wormEntryParticlesSpawned.TryGetValue(id, out var spawnedDict))
            {
                spawnedDict = new Dictionary<int, bool>();
                _wormEntryParticlesSpawned[id] = spawnedDict;
            }

            // -------------------------------------------------------------
            // NOUVELLE GESTION DES OMBRES
            // -------------------------------------------------------------
            float exitSolY = GetGroundHeightAt(exit);
            float entrySolY = GetGroundHeightAt(entry);

            // Ombre portée au sol sous chaque segment visible
            for (int i = 0; i < segmentCount; i++)
            {
                float t = WormSegmentT(elapsed, i, stagger, headDuration);
                if (t <= 0f || t >= 1f) continue;

                Vector2 segPos = WormArcPos(exit, entry, arcHeight, t);

                // Hauteur du sol interpolée
                float solY = Raymath.Lerp(exitSolY, entrySolY, t);
                float heightAboveGround = segPos.Y - solY;
                float heightNorm = Math.Clamp(heightAboveGround / arcHeight, 0f, 1f);

                // Opacité : plus le segment est haut, plus l'ombre est transparente
                byte alpha = (byte)(180 * (1f - heightNorm * 0.85f)); // on ne descend pas en dessous de ~27
                // Taille de l'ombre augmentée pour qu'elles se touchent
                float shadowRadiusX = 16f + heightNorm * 18f; // 16 à 34
                float shadowRadiusY = 8f + heightNorm * 12f;  // 8 à 20

                Raylib.DrawEllipse(
                    (int)segPos.X,
                    (int)solY,
                    shadowRadiusX,
                    shadowRadiusY,
                    new Color((byte)0, (byte)0, (byte)0, alpha)
                );
            }

            // Direction du déplacement (1 = droite, -1 = gauche)
            float moveDir = MathF.Sign(entry.X - exit.X);

            // Dessine la queue -> corps -> tête, dans cet ordre.
            for (int i = segmentCount - 1; i >= 0; i--)
            {
                float t = WormSegmentT(elapsed, i, stagger, headDuration);
                if (t <= 0f || t >= 1f)
                {
                    // Le segment vient de finir sa course (t >= 1)
                    // Génère un nuage de particules si ce n'est pas déjà fait.
                    if (t >= 1f && !spawnedDict.ContainsKey(i))
                    {
                        float intensity = 0.6f + (i % 3) * 0.15f;
                        Vector2 smokePos = new Vector2(entry.X, entry.Y + 12f);
                        Entity.SpawnKhamsinSmoke(smokePos, intensity);
                        spawnedDict[i] = true;
                    }
                    continue;
                }

                Vector2 segPos = WormArcPos(exit, entry, arcHeight, t);
                float angleDeg = WormArcAngleDeg(exit, entry, arcHeight, t);

                bool isHead = i == 0;
                bool isTail = i == segmentCount - 1;
                Texture2D tex = isHead ? headTex : (isTail ? tailTex : bodyTex);
                if (tex.Id == 0) continue;

                float w = tex.Width * scale;
                float h = tex.Height * scale;

                Rectangle src;
                if (moveDir < 0)
                {
                    src = new Rectangle(tex.Width, 0, tex.Width, -tex.Height);
                }
                else
                {
                    src = new Rectangle(0, 0, tex.Width, tex.Height);
                }

                Rectangle dst = new Rectangle(segPos.X, segPos.Y, w, h);
                Vector2 origin = new Vector2(w / 2f, h / 2f);

                Raylib.DrawTexturePro(tex, src, dst, origin, angleDeg, entity.Tint);
            }
        }

        /// <summary>
        /// Retourne la hauteur Y du sol (en coordonnées monde) à une position donnée.
        /// </summary>
        private static float GetGroundHeightAt(Vector2 worldPos)
        {
            int tileX = (int)(worldPos.X / Program.TileSize);
            int tileY = (int)(worldPos.Y / Program.TileSize);
            int height = World.GetHeightAt(tileX, tileY);

            // La hauteur du sol en pixels : tileY * TileSize - height * TileSize/4
            // (correspond au décalage appliqué aux entités dans DrawEntity)
            return tileY * Program.TileSize - height * Program.TileSize / 4f;
        }

        private static SpeciesFeatureRenderCache GetSpeciesFeatureRenderCache(string speciesKey)
        {
            if (_speciesFeatureRenderCache.TryGetValue(speciesKey, out var cached))
                return cached;

            cached = new SpeciesFeatureRenderCache();
            if (SpeciesData.Species.TryGetValue(speciesKey, out var speciesInfo))
            {
                foreach (var feature in speciesInfo.RandomFeatures)
                {
                    foreach (var partName in feature.attachTo)
                    {
                        if (feature.mode == "replace")
                        {
                            if (!cached.ReplaceByPart.ContainsKey(partName))
                                cached.ReplaceByPart[partName] = feature;
                        }
                        else if (feature.mode == "overlay")
                        {
                            if (!cached.OverlaysByPart.TryGetValue(partName, out var overlays))
                            {
                                overlays = new List<SpeciesData.RandomFeatureJson>();
                                cached.OverlaysByPart[partName] = overlays;
                            }
                            overlays.Add(feature);
                        }
                    }
                }
            }

            _speciesFeatureRenderCache[speciesKey] = cached;
            return cached;
        }

        private static void DrawTextureProWithFlash(Texture2D texture, Rectangle src, Rectangle dest, Vector2 origin, float rotation, Color tint, bool flashWhite = false)
        {
            Raylib.DrawTexturePro(texture, src, dest, origin, rotation, tint);
            if (flashWhite && texture.Id != 0)
            {
                Raylib.BeginBlendMode(BlendMode.Additive);
                Raylib.DrawTexturePro(texture, src, dest, origin, rotation, new Color(255, 255, 255, 120));
                Raylib.EndBlendMode();
            }
        }
        private static string ResolveRenderSpecies(string? speciesName)
        {
            if (string.Equals(speciesName, "Genie", StringComparison.OrdinalIgnoreCase))
                return "human";
            return speciesName?.Trim() ?? "";
        }

        public static Texture2D GetEyesTextureForSpecies(string speciesName, bool isSleeping = false)
        {
            string resolvedSpecies = ResolveRenderSpecies(speciesName);
            if (string.IsNullOrWhiteSpace(resolvedSpecies)) return new Texture2D();

            string cacheKey = $"{resolvedSpecies}:{(isSleeping ? "sleep" : "normal")}";
            if (_speciesEyeTextureCache.TryGetValue(cacheKey, out var cached))
                return cached;

            Texture2D loaded = new Texture2D();
            string normalized = resolvedSpecies.Trim();
            string normalizedNoSpace = normalized.Replace(" ", "");
            string[] candidates = isSleeping
                ? new[]
                {
                    $"assets/animals/{normalized}/{normalizedNoSpace}eyes_sleep.png",
                    $"assets/animals/{normalized}/{normalized}eyes_sleep.png",
                    $"assets/animals/{normalized}/{normalizedNoSpace}eyes.png",
                    $"assets/animals/{normalized}/{normalized}eyes.png",
                    $"assets/animals/{normalized}/{normalizedNoSpace}eyes1.png",
                    $"assets/animals/{normalized}/{normalized}eyes1.png",
                    $"assets/animals/{normalized}/{normalized}eyes_default.png"
                }
                : new[]
                {
                    $"assets/animals/{normalized}/{normalizedNoSpace}eyes.png",
                    $"assets/animals/{normalized}/{normalized}eyes.png",
                    $"assets/animals/{normalized}/{normalizedNoSpace}eyes1.png",
                    $"assets/animals/{normalized}/{normalized}eyes1.png",
                    $"assets/animals/{normalized}/{normalizedNoSpace}eyes_sleep.png",
                    $"assets/animals/{normalized}/{normalized}eyes_sleep.png",
                    $"assets/animals/{normalized}/{normalized}eyes_default.png"
                };

            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                loaded = Raylib.LoadTexture(candidate);
                if (loaded.Id != 0)
                {
                    Raylib.SetTextureFilter(loaded, TextureFilter.Point);
                    break;
                }
            }

            if (loaded.Id == 0 && Program.EyesTexture.Id != 0 && !string.Equals(resolvedSpecies, "human", StringComparison.OrdinalIgnoreCase))
            {
                loaded = Program.EyesTexture;
            }

            _speciesEyeTextureCache[cacheKey] = loaded;
            return loaded;
        }

        // Cache des textures de sourcils par espèce non-humaine (une seule paire par espèce,
        // contrairement aux Humains qui ont plusieurs styles indexés par Program.EyeStyle).
        private static readonly Dictionary<string, Texture2D> _speciesEyebrowTextureCache = new();

        public static Texture2D GetEyebrowTextureForSpecies(string speciesName, bool isLeft)
        {
            string resolvedSpecies = ResolveRenderSpecies(speciesName);
            if (string.IsNullOrWhiteSpace(resolvedSpecies)) return new Texture2D();

            string cacheKey = $"{resolvedSpecies}:{(isLeft ? "l" : "r")}";
            if (_speciesEyebrowTextureCache.TryGetValue(cacheKey, out var cached))
                return cached;

            Texture2D loaded = new Texture2D();
            string normalized = resolvedSpecies.Trim();
            string normalizedNoSpace = normalized.Replace(" ", "");
            string side = isLeft ? "l" : "r";
            string[] candidates = new[]
            {
                $"assets/animals/{normalized}/{normalizedNoSpace}{side}eyebrow.png",
                $"assets/animals/{normalized}/{normalized}{side}eyebrow.png",
                $"assets/animals/{normalized}/{normalizedNoSpace}{side}eyebrow0.png",
                $"assets/animals/{normalized}/{normalized}{side}eyebrow0.png",
            };

            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                loaded = Raylib.LoadTexture(candidate);
                if (loaded.Id != 0)
                {
                    Raylib.SetTextureFilter(loaded, TextureFilter.Point);
                    break;
                }
            }

            _speciesEyebrowTextureCache[cacheKey] = loaded;
            return loaded;
        }

        // Cache des textures de pupilles par espèce non-humaine (une seule paire par espèce,
        // contrairement aux Humains qui ont plusieurs styles indexés par Program.EyeStyle).
        private static readonly Dictionary<string, Texture2D> _speciesPupilTextureCache = new();

        public static Texture2D GetPupilTextureForSpecies(string speciesName, bool isLeft)
        {
            string resolvedSpecies = ResolveRenderSpecies(speciesName);
            if (string.IsNullOrWhiteSpace(resolvedSpecies)) return new Texture2D();

            string cacheKey = $"{resolvedSpecies}:{(isLeft ? "l" : "r")}";
            if (_speciesPupilTextureCache.TryGetValue(cacheKey, out var cached))
                return cached;

            Texture2D loaded = new Texture2D();
            string normalized = resolvedSpecies.Trim();
            string normalizedNoSpace = normalized.Replace(" ", "");
            string side = isLeft ? "l" : "r";
            string[] candidates = new[]
            {
                $"assets/animals/{normalized}/{normalizedNoSpace}{side}pupil.png",
                $"assets/animals/{normalized}/{normalized}{side}pupil.png",
                $"assets/animals/{normalized}/{normalizedNoSpace}{side}pupil0.png",
                $"assets/animals/{normalized}/{normalized}{side}pupil0.png",
            };

            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                loaded = Raylib.LoadTexture(candidate);
                if (loaded.Id != 0)
                {
                    Raylib.SetTextureFilter(loaded, TextureFilter.Point);
                    break;
                }
            }

            _speciesPupilTextureCache[cacheKey] = loaded;
            return loaded;
        }

        public static void DrawEntity(
            string speciesName,
            string anim, int frame, float prog,
            float facing, Vector2 pos,
            Color tint,
            Texture2D hBase, Texture2D hOverlay, Color hColor,
            Dictionary<string, List<AnimalBodyPart>> skeletons,
            Texture2D eyes = default,
            Texture2D mouth = default,
            float customScale = 1.0f,
            Equipment? equipment = null,
            bool isCarrying = false,
            bool isCarryingCreature = false,
            bool inWater = false,
            float attackSwingProgress = 0f,
            Texture2D heldItemTexture = default,
            Item? heldItemInstance = null,
            float headAngle = 0f,
            bool keepItemHorizontal = false,
            bool isBow = false,
            Texture2D underwearTexture = default,
            string prevAnim = "",
            int prevFrame = 0,
            float prevProg = 0f,
            float transitionWeight = 0f,
            bool flashWhite = false,
            int beardStyle = 0,
            Vector2? pupilLeftOffset = null,
            Vector2? pupilRightOffset = null,
            Vector2? eyebrowLeftOffset = null,
            Vector2? eyebrowRightOffset = null,
            bool? forceBlinking = null,
            Dictionary<string, int>? randomFeatureVariant = null,
            Dictionary<string, Color>? randomFeatureColor = null,
            float shadowGroundOffset = 0f,
            Action? drawCarriedEntity = null,
            // Callback qui dessine la monture (animal chevauché) exactement à l'endroit du
            // squelette où se trouve la pièce "body" : tout ce qui est dessiné AVANT "body"
            // (donc "derrière" le corps dans l'ordre du squelette) passe ainsi derrière la
            // monture, et tout ce qui est dessiné à partir de "body" (corps, tête, bras avant,
            // etc.) passe devant elle. Ça évite par ex. de voir les deux jambes du cavalier du
            // même côté par-dessus la monture.
            Action? drawMount = null,
            bool ghostly = false,
            string? ghostTrailKey = null,
            Item? slimeStoredItem = null,
            int? eyeStyleOverride = null,
            Color? eyeColorOverride = null
        )
        {
            //  DEBUG F3 : compte les entités réellement soumises au rendu détaillé cette frame
            // (après le culling écran fait par l'appelant), pour le panneau de perf. Voir PerfStats.cs.
            PerfStats.NotifyEntityDrawn();

            //  PERF (dézoom) : à faible zoom, l'écran couvre une zone du monde bien plus grande,
            // donc beaucoup plus d'entités passent le culling écran (IsEntityNearScreen) et
            // reçoivent chacune un rendu détaillé complet (squelette + plusieurs couches de
            // teinture par pièce d'équipement + bijoux/accessoires). C'était la cause principale
            // des 35ms de rendu constatés en dézoomant : aucune réduction de détail ne
            // dépendait du zoom. En dessous de LOD_ZOOM_THRESHOLD, ces détails fins sont de
            // certains détails d'équipement deviennent minuscules : on les réduit à un seul
            // calque de teinture par pièce et on masque les bijoux. Les traits propres à
            // l'espèce restent dessinés pour préserver sa silhouette reconnaissable.
            const float LOD_ZOOM_THRESHOLD = 0.75f;
            bool lowDetail = _currentZoomLevel > 0f && _currentZoomLevel < LOD_ZOOM_THRESHOLD;

            bool isSleepingAnimation = string.Equals(anim, "sleep", StringComparison.OrdinalIgnoreCase);
            string speciesKey = ResolveRenderSpecies(speciesName).ToLowerInvariant();
            bool isHumanoid = SpeciesData.IsHumanoid(speciesKey);
            if (isHumanoid && equipment?.MainHand != null)
            {
                heldItemInstance ??= equipment.MainHand;
                if (heldItemTexture.Id == 0)
                    heldItemTexture = GetHeldItemTextureInfo(heldItemInstance).Texture;
                isBow |= GameData.IsRangedWeapon(heldItemInstance.Name);
            }
            if (!skeletons.ContainsKey(speciesKey))
            {
                DrawFallbackEntity(pos, facing, Color.White);
                return;
            }

            var skeleton = skeletons[speciesKey];
            int partCount = skeleton.Count;

            // Cache part index map
            if (!_partIndexCache.TryGetValue(speciesKey, out var partIndexMap))
            {
                partIndexMap = new Dictionary<string, int>(partCount);
                for (int i = 0; i < partCount; i++) partIndexMap[skeleton[i].Name] = i;
                _partIndexCache[speciesKey] = partIndexMap;
            }
            string headPartName = partIndexMap.ContainsKey("head") ? "head" : "body";

            // Determine animation name
            //  Optimisation : évite le .Any() (LINQ + allocation de delegate) recalculé à
            // chaque frame pour chaque PNJ. On mémorise une fois par espèce l'ensemble des noms
            // d'animation réellement disponibles sur son squelette.
            if (!_availableAnimNamesCache.TryGetValue(speciesKey, out var availableAnims))
            {
                availableAnims = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in skeleton)
                    if (p.Animations != null)
                        foreach (var animKey in p.Animations.Keys)
                            availableAnims.Add(animKey);
                _availableAnimNamesCache[speciesKey] = availableAnims;
            }
            string animName = anim;
            bool animationExists = !string.IsNullOrEmpty(anim) && availableAnims.Contains(anim);
            if (!animationExists)
            {
                animName = availableAnims.Contains("idle") ? "idle" : "";
            }

            float speciesScale = BASE_ENTITY_SCALE;
            if (SpeciesData.Species.TryGetValue(speciesKey, out var speciesInfo))
                speciesScale = BASE_ENTITY_SCALE * speciesInfo.Scale * customScale;

            float legHeight = SpeciesData.LegHeights.GetValueOrDefault(speciesKey, 0f);
            var legParts = SpeciesData.LegPartNames.GetValueOrDefault(speciesKey, _emptyLegPartsSet);
            Vector2 drawPos = pos;
            if (inWater)
                drawPos = new Vector2(pos.X, pos.Y + legHeight);

            // Determine dye layer counts for all equipment types
            int bodyLayers = 1, legsLayers = 1, headLayers = 1, backpackLayers = 1;
            int earringLayers = 1, necklaceLayers = 1;
            int glassesLayers = 1, socksLayers = 1;
            
            var equipmentState = GetEquipmentRenderState(equipment);
            var bodyEquipItem = equipmentState.BodyEquipItem;
            var legsEquipItem = equipmentState.LegsEquipItem;
            var headEquipItem = equipmentState.HeadEquipItem;
            var backpackEquipItem = equipmentState.BackpackEquipItem;
            bodyLayers = equipmentState.BodyLayers;
            legsLayers = equipmentState.LegsLayers;
            headLayers = equipmentState.HeadLayers;
            backpackLayers = equipmentState.BackpackLayers;
            
            var earringItem = equipmentState.EarringItem;
            earringLayers = equipmentState.EarringLayers;
            
            var necklaceItem = equipmentState.NecklaceItem;
            necklaceLayers = equipmentState.NecklaceLayers;
            
            var glassesItem = equipmentState.GlassesItem;
            glassesLayers = equipmentState.GlassesLayers;
            
            var socksItem = equipmentState.SocksItem;
            socksLayers = equipmentState.SocksLayers;

            socksLayers = equipmentState.SocksLayers;

            // --------------------- FIRST PASS : compute global positions (recursive) ---------------------
            //  Optimisation perf (villages avec beaucoup de PNJ à l'écran) : ces 3 tableaux
            // étaient auparavant réalloués à CHAQUE appel (donc à chaque PNJ, à chaque frame),
            // ce qui générait énormément de pression sur le GC. On les emprunte maintenant à un
            // ArrayPool partagé et on les rend à la fin (via try/finally) — sûr même en cas
            // d'appel imbriqué (monture / créature portée qui redessinent un autre DrawEntity
            // pendant qu'on est encore en train de lire ces tableaux), puisque chaque appel
            // reçoit son propre tableau loué, indépendant des autres.
            Vector2[] globalPositions = ArrayPool<Vector2>.Shared.Rent(partCount);
            float[] globalRotations = ArrayPool<float>.Shared.Rent(partCount);
            bool[] computed = ArrayPool<bool>.Shared.Rent(partCount);
            //  Note : ce booléen signifie maintenant "la composition hors-écran de l'entité
            // fantôme est active" (voir BeginGhostComposite plus bas), et non plus "le shader
            // ghost.fs est actif sur le framebuffer principal" — le shader n'est désormais
            // appliqué qu'une seule fois, à la toute fin, sur la silhouette déjà aplatie.
            bool ghostShaderActive = false;
            try
            {
            Array.Clear(computed, 0, partCount);

            // Local recursive function to compute a part (calls parent if needed)
            void ComputePart(int index)
            {
                if (computed[index]) return;
                var part = skeleton[index];

                // Compute animation offset/rotation for this part
                Vector2 animOffset = Vector2.Zero;
                float animRot = 0f;
                bool isHoldingBow = (isHumanoid && heldItemTexture.Id != 0 && isBow && headAngle != 0);
                bool isAimingPose = isHumanoid && headAngle != 0 && isBow;
                bool isArmPart = part.Name == "rarmtop" || part.Name == "rarmbottom" || part.Name == "larmtop" || part.Name == "larmbottom";

                if (part.Name == "rarmtop" && isHoldingBow)
                {
                    float baseAngle = -90f;
                    float aimAngle = headAngle * 1.2f;
                    aimAngle = Math.Clamp(aimAngle, -90f, 70f);
                    animRot = (facing < 0) ? baseAngle - aimAngle : baseAngle + aimAngle;
                }
                else if (isAimingPose && isArmPart)
                {
                    animOffset = Vector2.Zero;
                    animRot = 0f;
                }
                else
                {
                    bool isAttacking = attackSwingProgress > 0f && attackSwingProgress < 1f;
                    bool isRightArmTop = isAttacking && isHumanoid && part.Name == "rarmtop";
                    if (isRightArmTop)
                    {
                        animRot = GetSwingRotation(attackSwingProgress, true);
                        float forwardOffset = MathF.Sin(attackSwingProgress * MathF.PI) * 15f;
                        animOffset = new Vector2(forwardOffset * facing, -5f);
                    }
                    else if (isCarryingCreature && isHumanoid &&
                             (part.Name == "larmtop" || part.Name == "rarmtop" || part.Name == "rarmbottom" || part.Name == "larmbottom"))
                    {
                        // Portage d'une créature : bras repliés plus près du corps, moins tendus
                        // que pour un meuble, pour bien montrer qu'on la tient contre soi.
                        bool isTop = part.Name == "larmtop" || part.Name == "rarmtop";
                        animOffset = isTop ? new Vector2(2f, -3f) : new Vector2(1f, -2f);
                        animRot = isTop ? -60f : -20f;
                    }
                    else if (isCarrying && isHumanoid &&
                             (part.Name == "larmtop" || part.Name == "rarmtop" || part.Name == "rarmbottom" || part.Name == "larmbottom"))
                    {
                        animOffset = Vector2.Zero;
                        animRot = -35f;
                    }
                    else if (!string.IsNullOrEmpty(animName) && part.Animations != null && part.Animations.TryGetValue(animName, out var frames) && frames.Count > 0)
                    {
                        int safeFrame = frame % frames.Count;
                        int ni = (safeFrame + 1) % frames.Count;
                        float t = prog;
                        animOffset = Vector2.Lerp(
                            new Vector2(frames[safeFrame].X, frames[safeFrame].Y),
                            new Vector2(frames[ni].X, frames[ni].Y), t) * speciesScale;
                        animRot = Raymath.Lerp(frames[safeFrame].Z, frames[ni].Z, t);
                    }
                }

                float additionalHeadRot = (part.Name == headPartName) ? headAngle * facing : 0f;
                Vector2 fb = new(part.BasePos.X * speciesScale, part.BasePos.Y * speciesScale);
                Vector2 fa = new(animOffset.X * facing, animOffset.Y);
                Vector2 finalOffset = fb + fa;
                if (facing < 0)
                    finalOffset = new Vector2(-finalOffset.X, finalOffset.Y);

                // Compute global position using parent if any
                if (!string.IsNullOrEmpty(part.ParentName))
                {
                    if (partIndexMap.TryGetValue(part.ParentName, out int parentIdx))
                    {
                        ComputePart(parentIdx); // ensure parent is computed first
                        float parentRot = globalRotations[parentIdx];
                        Vector2 parentPos = globalPositions[parentIdx];
                        globalRotations[index] = parentRot + (part.BaseRot + animRot + additionalHeadRot) * facing;
                        globalPositions[index] = parentPos + RotateVec(finalOffset, parentRot);
                    }
                    else
                    {
                        // Parent not found -> fallback to root
                        globalPositions[index] = drawPos + finalOffset;
                        globalRotations[index] = (part.BaseRot + animRot + additionalHeadRot) * facing;
                    }
                }
                else
                {
                    globalPositions[index] = drawPos + finalOffset;
                    globalRotations[index] = (part.BaseRot + animRot + additionalHeadRot) * facing;
                }

                computed[index] = true;
            }

            // Compute every part
            for (int i = 0; i < partCount; i++)
            {
                ComputePart(i);
            }


            // --------------------- SHADOW / WATER RIPPLE ---------------------
            if (!_skeletonExtentCache.TryGetValue(speciesKey, out var skeletonExtents))
            {
                float maxBaseX = 0f;
                float maxBaseY = 0f;
                foreach (var part in skeleton)
                {
                    maxBaseX = Math.Max(maxBaseX, Math.Abs(part.BasePos.X));
                    maxBaseY = Math.Max(maxBaseY, Math.Abs(part.BasePos.Y));
                }
                skeletonExtents = (maxBaseX, maxBaseY);
                _skeletonExtentCache[speciesKey] = skeletonExtents;
            }
            float shadowRadiusX = Math.Max(16f, skeletonExtents.MaxBaseX * speciesScale);
            float shadowRadiusY = Math.Max(6f, skeletonExtents.MaxBaseY * speciesScale * 0.25f);
            float shadowY = pos.Y + (Program.FeetOffsetY - 6) * customScale + shadowGroundOffset;

            if (inWater)
            {
                float rippleW = shadowRadiusX * 1.6f;
                float rippleH = Math.Max(5f, shadowRadiusY * 0.9f);
                float rippleY = shadowY + 4f;
                Raylib.DrawEllipse((int)pos.X, (int)rippleY, rippleW, rippleH, new Color(255, 255, 255, 90));
                Raylib.DrawEllipse((int)pos.X, (int)rippleY + 1, rippleW * 0.72f, rippleH * 0.7f, new Color(200, 220, 255, 55));
            }
            else
            {
                Raylib.DrawEllipse((int)pos.X, (int)shadowY, shadowRadiusX, shadowRadiusY, new Color(0, 0, 0, 80));
            }

            bool speciesIsTintable = SpeciesData.GetSpeciesInfo(speciesKey)?.Tintable ?? false;
            Color bodyDefaultTint = (isHumanoid || speciesIsTintable) ? tint : Color.White;
            var featureRenderCache = GetSpeciesFeatureRenderCache(speciesKey);

            //  Démarrage de la composition hors-écran pour les entités fantômes (voir le
            // commentaire détaillé sur BeginGhostComposite plus haut) : tout ce qui suit
            // (cheveux + toutes les pièces du squelette) est dessiné normalement, opaque, sans
            // shader, mais redirigé vers une texture, avant d'être aplati puis passé au shader
            // fantôme EN UNE SEULE FOIS (voir EndGhostComposite en fin de fonction). L'ombre au
            // sol, elle, reste dessinée directement à l'écran juste au-dessus : elle ne fait pas
            // partie de la silhouette à voiler.
            //  Le rectangle englobant est volontairement généreux (marge fixe + facteur sur
            // l'étendue du squelette) pour couvrir les objets tenus en main, les bijoux/capuches
            // qui dépassent, et les mouvements d'animation, sans avoir à connaître précisément
            // chaque taille de texture ici. À ajuster si un accessoire inhabituellement grand
            // venait à être rogné visuellement.
            if (ghostly)
            {
                float halfW = Math.Max(64f, skeletonExtents.MaxBaseX * speciesScale * 1.4f + 40f);
                float halfH = Math.Max(90f, skeletonExtents.MaxBaseY * speciesScale * 1.4f + 60f);
                Vector2 compositeOrigin = new(drawPos.X - halfW, drawPos.Y - halfH);
                ghostShaderActive = BeginGhostComposite(compositeOrigin, (int)MathF.Ceiling(halfW * 2f), (int)MathF.Ceiling(halfH * 2f));
            }

            Texture2D hairBack = default;
            if (speciesKey == "human" && hBase.Id != 0)
            {
                for (int hairIndex = 0; hairIndex < Program.hairBaseTextures.Count; hairIndex++)
                {
                    if (Program.hairBaseTextures[hairIndex].Id == hBase.Id && hairIndex < Program.hairBackTextures.Count)
                    {
                        hairBack = Program.hairBackTextures[hairIndex];
                        break;
                    }
                }
            }

            if (hairBack.Id != 0 && !equipmentState.IsFullHelmet &&
                equipmentState.HeadArmorCategory != ArmorCategory.Hat &&
                equipmentState.HeadArmorCategory != ArmorCategory.Helmet &&
                partIndexMap.TryGetValue(headPartName, out int hairHeadIndex))
            {
                Vector2 hairBackPos = globalPositions[hairHeadIndex];
                float hairBackRot = globalRotations[hairHeadIndex];
                float tw = hairBack.Width;
                float th = hairBack.Height;
                Rectangle hs = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                Rectangle hd = new(hairBackPos.X, hairBackPos.Y, tw * speciesScale, th * speciesScale);
                Vector2 origin = new(hd.Width / 2f, hd.Height / 2f);
                DrawTextureProWithFlash(hairBack, hs, hd, origin, hairBackRot, hColor, flashWhite);
            }

            if (equipment != null && isHumanoid)
            {
                for (int i = 0; i < partCount; i++)
                {
                    DrawEquipmentBackVariants(equipment, skeleton[i].Name, globalPositions[i], globalRotations[i],
                        facing, speciesScale, flashWhite, lowDetail);
                }
            }

            // --------------------- SECOND PASS : draw in the SAME ORDER as defined in skeleton ---------------------
            bool carriedEntityDrawn = false;
            bool mountDrawn = false;
            for (int i = 0; i < partCount; i++)
            {
                var part = skeleton[i];

                // La monture doit être dessinée juste avant la pièce "body" : tout ce qui a été
                // dessiné avant (donc "derrière" le corps dans l'ordre du squelette) reste ainsi
                // derrière l'animal monté, et le reste (corps, tête, bras avant...) passe devant.
                if (!mountDrawn && drawMount != null && part.Name == "body")
                {
                    // La monture est une AUTRE entité, potentiellement plus grande que le
                    // rectangle englobant calculé pour CETTE entité : on sort temporairement de
                    // la composition pour qu'elle se dessine normalement à l'écran, sans être
                    // rognée par notre petite texture hors-écran.
                    bool wasCompositing = ghostShaderActive;
                    if (wasCompositing) SuspendGhostComposite();
                    drawMount();
                    if (wasCompositing) ResumeGhostComposite();
                    mountDrawn = true;
                }

                // La créature portée doit apparaître APRÈS le bras gauche mais AVANT le bras droit,
                // pour donner l'impression qu'elle est tenue entre les deux (le bras droit passe devant).
                if (!carriedEntityDrawn && drawCarriedEntity != null && part.Name.StartsWith("rarm"))
                {
                    bool wasCompositing2 = ghostShaderActive;
                    if (wasCompositing2) SuspendGhostComposite();
                    drawCarriedEntity();
                    if (wasCompositing2) ResumeGhostComposite();
                    carriedEntityDrawn = true;
                }

                if (inWater && legParts.Contains(part.Name))
                    continue;

                bool isSkinPart = isHumanoid &&
                    (part.Name == "head" || part.Name.Contains("arm"));
                Color partTint = isSkinPart ? tint : bodyDefaultTint;

                bool isFullHelmet = false;
                if (isHumanoid && part.Name == headPartName)
                {
                    var headEquipItem2 = equipmentState.HeadEquipItem;
                    if (headEquipItem2 != null)
                    {
                        isFullHelmet = equipmentState.IsFullHelmet;
                    }
                }

                Vector2 globalPos = globalPositions[i];
                float globalRot = globalRotations[i];

                // ===== BLANC DES YEUX + PUPILLES (tout au fond, sous la tête) =====
                if (part.Name == headPartName && !isFullHelmet)
                {
                    if (speciesKey == "human")
                    {
                        //  Clignement unifié : que ce soit le sommeil ou un clignement ponctuel, on
                        // bascule simplement vers la texture "yeux fermés" (eyes_sleep), sans logique
                        // séparée de sourcils qui s'abaissent.
                        bool blinking = forceBlinking ?? Program.IsBlinking;

                        if (isSleepingAnimation || blinking)
                        {
                            Texture2D sleepEyesTex = GetEyesTextureForSpecies(speciesName, isSleeping: true);
                            if (sleepEyesTex.Id != 0)
                            {
                                float ew = sleepEyesTex.Width;
                                float eh = sleepEyesTex.Height;
                                Rectangle eSrc = new(facing < 0 ? ew : 0, 0, ew * facing, eh);
                                Rectangle eDst = new(globalPos.X, globalPos.Y, ew * speciesScale, eh * speciesScale);
                                Vector2 eOrigin = new(eDst.Width / 2f, eDst.Height / 2f);
                                Color sleepEyeColor = tint;
                                DrawTextureProWithFlash(sleepEyesTex, eSrc, eDst, eOrigin, globalRot, sleepEyeColor, flashWhite);
                            }
                        }
                        else
                        {
                            int eyeStyleIdx = Math.Clamp(eyeStyleOverride ?? Program.EyeStyle, 0, Math.Max(0, Program.EyeWhiteTextures.Count - 1));
                            Texture2D eyeWhiteTex = Program.EyeWhiteTextures.Count > 0 ? Program.EyeWhiteTextures[eyeStyleIdx] : default;

                            if (eyeWhiteTex.Id != 0)
                            {
                                float ew = eyeWhiteTex.Width;
                                float eh = eyeWhiteTex.Height;
                                Rectangle eSrc = new(facing < 0 ? ew : 0, 0, ew * facing, eh);
                                Rectangle eDst = new(globalPos.X, globalPos.Y, ew * speciesScale, eh * speciesScale);
                                Vector2 eOrigin = new(eDst.Width / 2f, eDst.Height / 2f);
                                DrawTextureProWithFlash(eyeWhiteTex, eSrc, eDst, eOrigin, globalRot, Color.White, flashWhite);
                            }

                            Texture2D lPupilTex = Program.PupilLeftTextures.Count > 0 ? Program.PupilLeftTextures[eyeStyleIdx] : default;
                            if (lPupilTex.Id != 0)
                            {
                                float pw = lPupilTex.Width;
                                float ph = lPupilTex.Height;
                                Rectangle pSrc = new(facing < 0 ? pw : 0, 0, pw * facing, ph);
                                // Décalage de base fixe de 0.25px en X pour la pupille gauche (ajustement d'alignement),
                                // en plus du décalage dynamique lié au regard.
                                Vector2 pOffset = ((pupilLeftOffset ?? Program.PupilLeftOffset) + new Vector2(0.25f * facing, 0f)) * speciesScale;
                                Rectangle pDst = new(globalPos.X + pOffset.X, globalPos.Y + pOffset.Y, pw * speciesScale, ph * speciesScale);
                                Vector2 pOrigin = new(pDst.Width / 2f, pDst.Height / 2f);
                                Color humanEyeColor = (randomFeatureColor != null && randomFeatureColor.TryGetValue("__eyeColor", out var eyeCol))
                                    ? eyeCol
                                    : eyeColorOverride ?? Program.EyeColor;
                                DrawTextureProWithFlash(lPupilTex, pSrc, pDst, pOrigin, globalRot, humanEyeColor, flashWhite);
                            }

                            Texture2D rPupilTex = Program.PupilRightTextures.Count > 0 ? Program.PupilRightTextures[eyeStyleIdx] : default;
                            if (rPupilTex.Id != 0)
                            {
                                float pw = rPupilTex.Width;
                                float ph = rPupilTex.Height;
                                Rectangle pSrc = new(facing < 0 ? pw : 0, 0, pw * facing, ph);
                                Vector2 pOffset = (pupilRightOffset ?? Program.PupilRightOffset) * speciesScale;
                                Rectangle pDst = new(globalPos.X + pOffset.X, globalPos.Y + pOffset.Y, pw * speciesScale, ph * speciesScale);
                                Vector2 pOrigin = new(pDst.Width / 2f, pDst.Height / 2f);
                                Color humanEyeColor = (randomFeatureColor != null && randomFeatureColor.TryGetValue("__eyeColor", out var eyeCol))
                                    ? eyeCol
                                    : eyeColorOverride ?? Program.EyeColor;
                                DrawTextureProWithFlash(rPupilTex, pSrc, pDst, pOrigin, globalRot, humanEyeColor, flashWhite);
                            }
                        }
                    }
                    else if (speciesName == "goblin")
                    {
                        // Même ordre que les Humains : fond des yeux, puis pupilles (suivi du curseur en
                        // fallback pour le joueur, ou offset par-entité pour les PNJ), tout ceci sous la tête.
                        //  Clignement unifié : bascule vers la texture "yeux fermés" (eyes_sleep) que ce
                        // soit pour le sommeil ou un clignement ponctuel.
                        bool goblinBlinking = forceBlinking ?? Program.IsBlinking;

                        Texture2D goblinEyeBg = (isSleepingAnimation || goblinBlinking)
                            ? GetEyesTextureForSpecies(speciesName, isSleeping: true)
                            : (eyes.Id != 0 ? eyes : GetEyesTextureForSpecies(speciesName, isSleeping: false));

                        if (goblinEyeBg.Id != 0)
                        {
                            float ew = goblinEyeBg.Width;
                            float eh = goblinEyeBg.Height;
                            Rectangle eSrc = new(facing < 0 ? ew : 0, 0, ew * facing, eh);
                            Rectangle eDst = new(globalPos.X, globalPos.Y, ew * speciesScale, eh * speciesScale);
                            Vector2 eOrigin = new(eDst.Width / 2f, eDst.Height / 2f);
                            Color goblinEyeColor = (randomFeatureColor != null && randomFeatureColor.TryGetValue("__eyeColor", out var eyeCol))
                                ? eyeCol
                                : Color.White;
                            DrawTextureProWithFlash(goblinEyeBg, eSrc, eDst, eOrigin, globalRot, goblinEyeColor, flashWhite);
                        }

                        if (!isSleepingAnimation && !goblinBlinking)
                        {
                            Texture2D lPupilTex = GetPupilTextureForSpecies(speciesName, isLeft: true);
                            if (lPupilTex.Id != 0)
                            {
                                float pw = lPupilTex.Width;
                                float ph = lPupilTex.Height;
                                Rectangle pSrc = new(facing < 0 ? pw : 0, 0, pw * facing, ph);
                                Vector2 pOffset = (pupilLeftOffset ?? Program.PupilLeftOffset) * speciesScale;
                                Rectangle pDst = new(globalPos.X + pOffset.X, globalPos.Y + pOffset.Y, pw * speciesScale, ph * speciesScale);
                                Vector2 pOrigin = new(pDst.Width / 2f, pDst.Height / 2f);
                                DrawTextureProWithFlash(lPupilTex, pSrc, pDst, pOrigin, globalRot, Color.White, flashWhite);
                            }

                            Texture2D rPupilTex = GetPupilTextureForSpecies(speciesName, isLeft: false);
                            if (rPupilTex.Id != 0)
                            {
                                float pw = rPupilTex.Width;
                                float ph = rPupilTex.Height;
                                Rectangle pSrc = new(facing < 0 ? pw : 0, 0, pw * facing, ph);
                                Vector2 pOffset = (pupilRightOffset ?? Program.PupilRightOffset) * speciesScale;
                                Rectangle pDst = new(globalPos.X + pOffset.X, globalPos.Y + pOffset.Y, pw * speciesScale, ph * speciesScale);
                                Vector2 pOrigin = new(pDst.Width / 2f, pDst.Height / 2f);
                                DrawTextureProWithFlash(rPupilTex, pSrc, pDst, pOrigin, globalRot, Color.White, flashWhite);
                            }
                        }
                    }
                }

                //  CARACTÈRES ALÉATOIRES ("replace") : la feature remplace entièrement la texture de la partie
                Texture2D baseTextureToDraw = part.Texture;
                if (randomFeatureVariant != null && featureRenderCache.ReplaceByPart.TryGetValue(part.Name, out var replaceFeature))
                {
                    if (randomFeatureVariant.TryGetValue(replaceFeature.name, out int replaceIdx) &&
                        replaceIdx >= 0)
                    {
                        Texture2D replaceTex = SpeciesData.GetFeatureTexture(speciesKey, replaceFeature.name, part.Name, replaceIdx);
                        if (replaceTex.Id != 0) baseTextureToDraw = replaceTex;
                    }
                }

                // Draw base part texture (la tête vient recouvrir le blanc des yeux + pupilles ci-dessus)
                if (baseTextureToDraw.Id != 0 && !(part.Name == "head" && isFullHelmet))
                {
                    float tw = baseTextureToDraw.Width;
                    float th = baseTextureToDraw.Height;
                    Rectangle src = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                    Rectangle dest = new(globalPos.X, globalPos.Y, tw * speciesScale, th * speciesScale);
                    Vector2 origin = new(dest.Width / 2f, dest.Height / 2f);
                    DrawTextureProWithFlash(baseTextureToDraw, src, dest, origin, globalRot, partTint, flashWhite);

                    if (speciesKey == "slime" && part.Name == "body" && slimeStoredItem != null)
                    {
                        float gemSize = Math.Min(dest.Width, dest.Height) * 0.36f;
                        Vector2 gemCenter = new(
                            dest.X + dest.Width * 0.25f,
                            dest.Y + dest.Height * 0.22f);
                        Rectangle gemDest = new(gemCenter.X - gemSize / 2f, gemCenter.Y - gemSize / 2f, gemSize, gemSize);
                        Vector2 gemOrigin = new(gemSize / 2f, gemSize / 2f);
                        if (slimeStoredItem.Icon.Id != 0)
                        {
                            Texture2D gemTexture = slimeStoredItem.Icon;
                            Rectangle gemSource = new(0, 0, gemTexture.Width, gemTexture.Height);
                            Raylib.DrawTexturePro(gemTexture, gemSource, gemDest, gemOrigin, globalRot,
                                new Color(255, 255, 255, 220));
                        }
                        else
                        {
                            Raylib.DrawCircleV(gemCenter, gemSize / 2f, slimeStoredItem.DisplayColor);
                        }
                    }
                }

                //  CARACTÈRES ALÉATOIRES ("overlay") : traits propres à l'espèce, visibles à tout zoom.
                if (randomFeatureVariant != null && featureRenderCache.OverlaysByPart.TryGetValue(part.Name, out var overlayFeatures))
                {
                    foreach (var feature in overlayFeatures)
                    {
                        if (!randomFeatureVariant.TryGetValue(feature.name, out int variantIdx) || variantIdx < 0)
                            continue;

                        Texture2D featureTex = SpeciesData.GetFeatureTexture(speciesKey, feature.name, part.Name, variantIdx);
                        if (featureTex.Id == 0) continue;

                        Color featureColor = Color.White;
                        if (feature.colorable && randomFeatureColor != null && randomFeatureColor.TryGetValue(feature.name, out var col))
                            featureColor = col;

                        float fw = featureTex.Width;
                        float fh = featureTex.Height;
                        Rectangle fSrc = new(facing < 0 ? fw : 0, 0, fw * facing, fh);
                        Rectangle fDst = new(globalPos.X, globalPos.Y, fw * speciesScale, fh * speciesScale);
                        Vector2 fOrigin = new(fDst.Width / 2f, fDst.Height / 2f);
                        DrawTextureProWithFlash(featureTex, fSrc, fDst, fOrigin, globalRot, featureColor, flashWhite);
                    }
                }

                // ============================================================
                //  TÊTE - CORRECTION : CASQUE/MASQUE + ACCESSOIRES SIMULTANÉMENT
                // ============================================================
                if (part.Name == "head" && isHumanoid)
                {
                    ArmorCategory headArmorCategory = ArmorCategory.None;
                    
                    // Récupérer la catégorie du casque/masque
                    var headEquipItem3 = equipmentState.HeadEquipItem;
                    if (headEquipItem3 != null)
                    {
                        headArmorCategory = equipmentState.HeadArmorCategory;
                        isFullHelmet = equipmentState.IsFullHelmet;
                    }

                    bool isMask = headArmorCategory == ArmorCategory.Mask;
                    var headItem = equipmentState.HeadEquipItem;

                    // Les sourcils du gobelin sont des éléments du visage : le couvre-chef doit les recouvrir.
                    bool goblinBlinking = forceBlinking ?? Program.IsBlinking;
                    if (speciesKey == "goblin" && !isFullHelmet && !isSleepingAnimation && !goblinBlinking)
                    {
                        Texture2D leftBrow = GetEyebrowTextureForSpecies(speciesName, isLeft: true);
                        Texture2D rightBrow = GetEyebrowTextureForSpecies(speciesName, isLeft: false);
                        if (leftBrow.Id != 0)
                        {
                            float width = leftBrow.Width;
                            float height = leftBrow.Height;
                            Rectangle source = new(facing < 0 ? width : 0, 0, width * facing, height);
                            Vector2 offset = (eyebrowLeftOffset ?? Program.EyebrowLeftOffset) * speciesScale;
                            Rectangle destination = new(globalPos.X + offset.X, globalPos.Y + offset.Y, width * speciesScale, height * speciesScale);
                            Vector2 origin = new(destination.Width / 2f, destination.Height / 2f);
                            DrawTextureProWithFlash(leftBrow, source, destination, origin, globalRot, Color.White, flashWhite);
                        }

                        if (rightBrow.Id != 0)
                        {
                            float width = rightBrow.Width;
                            float height = rightBrow.Height;
                            Rectangle source = new(facing < 0 ? width : 0, 0, width * facing, height);
                            Vector2 offset = (eyebrowRightOffset ?? Program.EyebrowRightOffset) * speciesScale;
                            Rectangle destination = new(globalPos.X + offset.X, globalPos.Y + offset.Y, width * speciesScale, height * speciesScale);
                            Vector2 origin = new(destination.Width / 2f, destination.Height / 2f);
                            DrawTextureProWithFlash(rightBrow, source, destination, origin, globalRot, Color.White, flashWhite);
                        }
                    }

                    // ===== 1. DESSINER LE CASQUE (SI CE N'EST PAS UN MASQUE) =====
                    if (equipment != null && headItem != null && !isMask)
                    {
                        int actualLayers = lowDetail ? 1 : headLayers;

                        if (GameData.ItemDatabase.TryGetValue(GetItemId(headItem.Name), out var headItemData) &&
                            !string.IsNullOrEmpty(headItemData.TextureName) &&
                            equipment.EquipmentTextures.TryGetValue($"base:{headItemData.TextureName}_head", out var headBaseTex) &&
                            headBaseTex.Id != 0)
                        {
                            float baseWidth = headBaseTex.Width;
                            float baseHeight = headBaseTex.Height;
                            Rectangle baseSource = new(facing < 0 ? baseWidth : 0, 0, baseWidth * facing, baseHeight);
                            Rectangle baseDestination = new(globalPos.X, globalPos.Y, baseWidth * speciesScale, baseHeight * speciesScale);
                            Vector2 baseOrigin = new(baseDestination.Width / 2f, baseDestination.Height / 2f);
                            DrawTextureProWithFlash(headBaseTex, baseSource, baseDestination, baseOrigin, globalRot, Color.White, false);
                        }

                        for (int layer = 1; layer <= actualLayers; layer++)
                        {
                            string key = layer == 1 ? "head" : $"head_{layer}";

                            if (!equipment.EquipmentTextures.TryGetValue(key, out var headEquipTex) || headEquipTex.Id == 0)
                                continue;

                            Color headColor = headItem.GetLayerColor(layer - 1);

                            float twEquip = headEquipTex.Width;
                            float thEquip = headEquipTex.Height;
                            Rectangle srcEquip = new(facing < 0 ? twEquip : 0, 0, twEquip * facing, thEquip);
                            Rectangle destEquip = new(globalPos.X, globalPos.Y, twEquip * speciesScale, thEquip * speciesScale);
                            Vector2 originEquip = new(destEquip.Width / 2f, destEquip.Height / 2f);
                            DrawTextureProWithFlash(headEquipTex, srcEquip, destEquip, originEquip, globalRot, headColor, flashWhite);
                        }
                    }

                    // ===== 2. CHEVEUX, YEUX, BOUCHE (seulement si PAS casque intégral) =====
                    if (!isFullHelmet)
                    {
                        bool showHair = speciesKey == "human" && headArmorCategory != ArmorCategory.Hat && headArmorCategory != ArmorCategory.Helmet;
                        bool showBeard = speciesKey == "human" && headArmorCategory != ArmorCategory.Helmet && headArmorCategory != ArmorCategory.Mask;
                        bool showEyes = headArmorCategory != ArmorCategory.Helmet;
                        bool showMouth = headArmorCategory != ArmorCategory.Helmet;

                        // CHEVEUX
                        if (showHair && hBase.Id != 0)
                        {
                            float tw = hBase.Width;
                            float th = hBase.Height;
                            float hairScale = speciesScale;
                            Rectangle hs = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                            Rectangle hd = new(globalPos.X, globalPos.Y, tw * hairScale, th * hairScale);
                            Vector2 origin = new(hd.Width / 2f, hd.Height / 2f);

                            DrawTextureProWithFlash(hBase, hs, hd, origin, globalRot, hColor, flashWhite);
                            if (hOverlay.Id != 0)
                                DrawTextureProWithFlash(hOverlay, hs, hd, origin, globalRot, Color.White, flashWhite);
                        }

                        // BARBE
                        if (showBeard)
                        {
                            int beardStyleIndex = Math.Clamp(beardStyle, 0, Math.Max(0, Program.beardBaseTextures.Count - 1));
                            Texture2D beardBaseTex = beardStyleIndex >= 0 && beardStyleIndex < Program.beardBaseTextures.Count
                                ? Program.beardBaseTextures[beardStyleIndex]
                                : new Texture2D();
                            if (beardBaseTex.Id != 0)
                            {
                                float tw = beardBaseTex.Width;
                                float th = beardBaseTex.Height;
                                float beardScale = speciesScale;
                                Rectangle beardSrc = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                                Rectangle beardDst = new(globalPos.X, globalPos.Y, tw * beardScale, th * beardScale);
                                Vector2 beardOrigin = new(beardDst.Width / 2f, beardDst.Height / 2f);
                                DrawTextureProWithFlash(beardBaseTex, beardSrc, beardDst, beardOrigin, globalRot, hColor, flashWhite);
                            }
                        }

                        // SOURCILS (par-dessus la tête). Le blanc des yeux et les pupilles ont déjà été
                        // dessinés plus haut, AVANT la texture de tête, afin que la tête les recouvre.
                        // On les masque aussi pendant un clignement (comme pendant le sommeil) : sinon
                        // ils resteraient visibles au-dessus des yeux fermés, ce qui n'a pas de sens.
                        bool humanBlinking = forceBlinking ?? Program.IsBlinking;
                        if (showEyes && speciesKey == "human" && !isSleepingAnimation && !humanBlinking)
                        {
                            int eyeStyleIdx = Math.Clamp(eyeStyleOverride ?? Program.EyeStyle, 0, Math.Max(0, Program.EyeWhiteTextures.Count - 1));

                            Texture2D lBrowTex = Program.EyebrowLeftTextures.Count > 0 ? Program.EyebrowLeftTextures[eyeStyleIdx] : default;
                            if (lBrowTex.Id != 0)
                            {
                                float bw = lBrowTex.Width;
                                float bh = lBrowTex.Height;
                                Rectangle bSrc = new(facing < 0 ? bw : 0, 0, bw * facing, bh);
                                Vector2 lBrowOffset = (eyebrowLeftOffset ?? Program.EyebrowLeftOffset) * speciesScale;
                                Rectangle bDst = new(globalPos.X + lBrowOffset.X, globalPos.Y + lBrowOffset.Y, bw * speciesScale, bh * speciesScale);
                                Vector2 bOrigin = new(bDst.Width / 2f, bDst.Height / 2f);
                                DrawTextureProWithFlash(lBrowTex, bSrc, bDst, bOrigin, globalRot, hColor, flashWhite);
                            }

                            Texture2D rBrowTex = Program.EyebrowRightTextures.Count > 0 ? Program.EyebrowRightTextures[eyeStyleIdx] : default;
                            if (rBrowTex.Id != 0)
                            {
                                float bw = rBrowTex.Width;
                                float bh = rBrowTex.Height;
                                Rectangle bSrc = new(facing < 0 ? bw : 0, 0, bw * facing, bh);
                                Vector2 rBrowOffset = (eyebrowRightOffset ?? Program.EyebrowRightOffset) * speciesScale;
                                Rectangle bDst = new(globalPos.X + rBrowOffset.X, globalPos.Y + rBrowOffset.Y, bw * speciesScale, bh * speciesScale);
                                Vector2 bOrigin = new(bDst.Width / 2f, bDst.Height / 2f);
                                DrawTextureProWithFlash(rBrowTex, bSrc, bDst, bOrigin, globalRot, hColor, flashWhite);
                            }
                        }
                        else if (showEyes && speciesKey != "goblin" &&
                                 ((isSleepingAnimation || humanBlinking) || (!isSleepingAnimation && eyes.Id != 0)))
                        {
                            Texture2D activeEyes = (isSleepingAnimation || humanBlinking)
                                ? GetEyesTextureForSpecies(speciesName, isSleeping: true)
                                : eyes;
                            if (activeEyes.Id != 0)
                            {
                                float eyeW = activeEyes.Width;
                                float eyeH = activeEyes.Height;
                                Rectangle eyeSrc = new Rectangle(facing < 0 ? eyeW : 0, 0, eyeW * facing, eyeH);
                                Rectangle eyeDst = new Rectangle(globalPos.X, globalPos.Y, eyeW * speciesScale, eyeH * speciesScale);
                                Vector2 eyeOrigin = new(eyeDst.Width / 2f, eyeDst.Height / 2f);
                                // Les paupières fermées (sommeil ou clignement) utilisent la couleur de peau.
                                Color sleepEyeColor = (isSleepingAnimation || humanBlinking) ? tint : Color.White;
                                DrawTextureProWithFlash(activeEyes, eyeSrc, eyeDst, eyeOrigin, globalRot, sleepEyeColor, flashWhite);
                            }
                        }

                        // BOUCHE
                        if (showMouth && mouth.Id != 0)
                        {
                            float mouthW = mouth.Width;
                            float mouthH = mouth.Height;
                            Rectangle mouthSrc = new(facing < 0 ? mouthW : 0, 0, mouthW * facing, mouthH);
                            Rectangle mouthDst = new(globalPos.X, globalPos.Y, mouthW * speciesScale, mouthH * speciesScale);
                            Vector2 mouthOrigin = new(mouthDst.Width / 2f, mouthDst.Height / 2f);
                            DrawTextureProWithFlash(mouth, mouthSrc, mouthDst, mouthOrigin, globalRot, Color.White, flashWhite);
                        }
                    }

                    // Si c'est un masque, on le dessine après les cheveux, les yeux et la bouche.
                    if (equipment != null && headItem != null && isMask)
                    {
                        int actualLayers = headLayers;

                        if (GameData.ItemDatabase.TryGetValue(GetItemId(headItem.Name), out var maskItemData) &&
                            !string.IsNullOrEmpty(maskItemData.TextureName) &&
                            equipment.EquipmentTextures.TryGetValue($"base:{maskItemData.TextureName}_head", out var maskBaseTex) &&
                            maskBaseTex.Id != 0)
                        {
                            float baseWidth = maskBaseTex.Width;
                            float baseHeight = maskBaseTex.Height;
                            Rectangle baseSource = new(facing < 0 ? baseWidth : 0, 0, baseWidth * facing, baseHeight);
                            Rectangle baseDestination = new(globalPos.X, globalPos.Y, baseWidth * speciesScale, baseHeight * speciesScale);
                            Vector2 baseOrigin = new(baseDestination.Width / 2f, baseDestination.Height / 2f);
                            DrawTextureProWithFlash(maskBaseTex, baseSource, baseDestination, baseOrigin, globalRot, Color.White, false);
                        }

                        for (int layer = 1; layer <= actualLayers; layer++)
                        {
                            string key = layer == 1 ? "head" : $"head_{layer}";

                            if (!equipment.EquipmentTextures.TryGetValue(key, out var headEquipTex) || headEquipTex.Id == 0)
                                continue;

                            Color headColor = headItem.GetLayerColor(layer - 1);

                            float twEquip = headEquipTex.Width;
                            float thEquip = headEquipTex.Height;
                            Rectangle srcEquip = new(facing < 0 ? twEquip : 0, 0, twEquip * facing, thEquip);
                            Rectangle destEquip = new(globalPos.X, globalPos.Y, twEquip * speciesScale, thEquip * speciesScale);
                            Vector2 originEquip = new(destEquip.Width / 2f, destEquip.Height / 2f);
                            DrawTextureProWithFlash(headEquipTex, srcEquip, destEquip, originEquip, globalRot, headColor, flashWhite);
                        }
                    }

                    // ===== 3. ACCESSOIRES DE TÊTE (PAR-DESSUS TOUT) =====
                    if (equipment != null)
                    {
                        // Les petites boucles d'oreilles peuvent être omises au dézoom pour le LOD.
                        if (!lowDetail)
                            DrawAccessoryInSlot(equipment, BodyZone.Ears, "head", globalPos, globalRot, facing, speciesScale, earringLayers, flashWhite);
                    }
                }
                else if (part.Name == headPartName && speciesKey != "human" && !isFullHelmet)
                {
                    // ===== YEUX / BOUCHE DES ESPÈCES NON-HUMAINES (chats, etc.) =====
                    // Pendant l'animation de sommeil, les yeux restent fermés en permanence.
                    // Sinon, on clignote ponctuellement en basculant vers la texture "eyes_sleep"
                    // (les espèces non-humaines n'ont pas de sourcils pour animer le clignement autrement).
                    // NOTE : pour le Gobelin, le fond des yeux + les pupilles sont déjà dessinés plus haut
                    // (AVANT la tête, comme pour les Humains) ; on ne les redessine pas ici.
                    bool animalBlinking = forceBlinking ?? (speciesName == "goblin" ? Program.IsBlinking : false);

                    if (speciesName != "goblin")
                    {
                        Texture2D activeEyes = (isSleepingAnimation || animalBlinking)
                            ? GetEyesTextureForSpecies(speciesName, isSleeping: true)
                            : (eyes.Id != 0 ? eyes : GetEyesTextureForSpecies(speciesName, isSleeping: false));

                        if (activeEyes.Id != 0)
                        {
                            float eyeW = activeEyes.Width;
                            float eyeH = activeEyes.Height;
                            Rectangle eyeSrc = new Rectangle(facing < 0 ? eyeW : 0, 0, eyeW * facing, eyeH);
                            Rectangle eyeDst = new Rectangle(globalPos.X, globalPos.Y, eyeW * speciesScale, eyeH * speciesScale);
                            Vector2 eyeOrigin = new(eyeDst.Width / 2f, eyeDst.Height / 2f);
                            Color animalEyeColor = ghostly
                                ? new Color(30, 250, 255, 255)
                                : (randomFeatureColor != null && randomFeatureColor.TryGetValue("__eyeColor", out var eyeCol)
                                    ? eyeCol
                                    : Color.White);

                            // Le halo/traînée des yeux fantômes est un effet à part (lumineux,
                            // persistant sur plusieurs frames) : il doit se dessiner directement
                            // à l'écran, pas être aplati dans la texture de composition avec le
                            // reste du corps (sa traînée peut dépasser du rectangle englobant de
                            // l'entité au fil du mouvement).
                            bool resumeGhostShader = ghostly && ghostShaderActive;
                            if (resumeGhostShader)
                            {
                                SuspendGhostComposite();
                                DrawSpiritEyeTrail(ghostTrailKey, activeEyes, eyeSrc, eyeDst, globalRot);
                                if (!DrawSpiritEyeGlow(activeEyes, eyeSrc, eyeDst, eyeOrigin, globalRot, 1f))
                                    DrawTextureProWithFlash(activeEyes, eyeSrc, eyeDst, eyeOrigin, globalRot, animalEyeColor, flashWhite);
                                ResumeGhostComposite();
                            }
                            else
                                DrawTextureProWithFlash(activeEyes, eyeSrc, eyeDst, eyeOrigin, globalRot, animalEyeColor, flashWhite);
                        }
                    }

                    if (mouth.Id != 0)
                    {
                        float mouthW = mouth.Width;
                        float mouthH = mouth.Height;
                        Rectangle mouthSrc = new(facing < 0 ? mouthW : 0, 0, mouthW * facing, mouthH);
                        Rectangle mouthDst = new(globalPos.X, globalPos.Y, mouthW * speciesScale, mouthH * speciesScale);
                        Vector2 mouthOrigin = new(mouthDst.Width / 2f, mouthDst.Height / 2f);
                        DrawTextureProWithFlash(mouth, mouthSrc, mouthDst, mouthOrigin, globalRot, Color.White, flashWhite);
                    }
                }

                // ===== ÉQUIPEMENT PRINCIPAL (Body, Legs, Backpack) =====
                if (equipment != null && isHumanoid && part.Name != "head")
                {
                    int definedLayers = 1;
                    Item? equipItem = null;

                    if (part.Name.Contains("body") || part.Name.Contains("arm"))
                    {
                        equipItem = bodyEquipItem;
                        definedLayers = bodyLayers;
                    }
                    else if (part.Name.Contains("leg"))
                    {
                        equipItem = legsEquipItem;
                        definedLayers = legsLayers;
                    }

                    if (equipItem != null && definedLayers > 0)
                    {
                        int actualLayers = definedLayers;

                        //  VARIANTES DE CRAFT (ex: coat) : si l'item porte des métadonnées de
                        // variante (manches courtes/longues, ouvert/fermé...), on cherche une
                        // texture suffixée ("body_open", "larmtop_long", ...) chargée à la volée
                        // et mise en cache, avec repli sur la texture de base si le fichier
                        // suffixé n'existe pas (rétrocompatible avec les items sans métadonnées).
                        string? variantSuffix = GetCraftVariantSuffix(equipItem, part.Name);

                        if (string.Equals(variantSuffix, "none", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (GameData.ItemDatabase.TryGetValue(GetItemId(equipItem.Name), out var equipmentItemData) &&
                            !string.IsNullOrEmpty(equipmentItemData.TextureName))
                        {
                            string baseTextureKey = $"base:{equipmentItemData.TextureName}_{part.Name}";
                            if (variantSuffix != null)
                            {
                                string variantBaseKey = $"{baseTextureKey}_{variantSuffix}";
                                if (!equipment.EquipmentTextures.TryGetValue(variantBaseKey, out var variantBaseTex) || variantBaseTex.Id == 0)
                                    variantBaseKey = baseTextureKey;

                                if (equipment.EquipmentTextures.TryGetValue(variantBaseKey, out var baseTex) && baseTex.Id != 0)
                                {
                                    float baseWidth = baseTex.Width;
                                    float baseHeight = baseTex.Height;
                                    Rectangle baseSource = new(facing < 0 ? baseWidth : 0, 0, baseWidth * facing, baseHeight);
                                    Rectangle baseDestination = new(globalPos.X, globalPos.Y, baseWidth * speciesScale, baseHeight * speciesScale);
                                    Vector2 baseOrigin = new(baseDestination.Width / 2f, baseDestination.Height / 2f);
                                    DrawTextureProWithFlash(baseTex, baseSource, baseDestination, baseOrigin, globalRot, Color.White, false);
                                }
                            }
                            else if (equipment.EquipmentTextures.TryGetValue(baseTextureKey, out var baseTex) && baseTex.Id != 0)
                            {
                                float baseWidth = baseTex.Width;
                                float baseHeight = baseTex.Height;
                                Rectangle baseSource = new(facing < 0 ? baseWidth : 0, 0, baseWidth * facing, baseHeight);
                                Rectangle baseDestination = new(globalPos.X, globalPos.Y, baseWidth * speciesScale, baseHeight * speciesScale);
                                Vector2 baseOrigin = new(baseDestination.Width / 2f, baseDestination.Height / 2f);
                                DrawTextureProWithFlash(baseTex, baseSource, baseDestination, baseOrigin, globalRot, Color.White, false);
                            }
                        }

                        for (int layer = 1; layer <= actualLayers; layer++)
                        {
                            string baseKey = layer == 1 ? part.Name : $"{part.Name}_{layer}";
                            string key = baseKey;
                            Texture2D equipTex = default;
                            bool found = false;

                            if (variantSuffix != null)
                            {
                                // If variantSuffix is explicitly "none", skip drawing this part entirely.
                                string variantKey = layer == 1
                                    ? $"{part.Name}_{variantSuffix}"
                                    : $"{part.Name}_{variantSuffix}_{layer}";

                                if (equipment.EquipmentTextures.TryGetValue(variantKey, out equipTex) && equipTex.Id != 0)
                                {
                                    key = variantKey;
                                    found = true;
                                }
                                else if (!equipment.EquipmentTextures.ContainsKey(variantKey))
                                {
                                    int variantItemId = GetItemId(equipItem.Name);
                                    if (GameData.ItemDatabase.TryGetValue(variantItemId, out var variantItemData) && !string.IsNullOrEmpty(variantItemData.TextureName))
                                    {
                                        string[] variantCandidates = layer == 1
                                            ? new[]
                                            {
                                                $"assets/equipements/{variantItemData.TextureName}_{part.Name}_1_{variantSuffix}.png",
                                                $"assets/equipements/{variantItemData.TextureName}_{part.Name}_{variantSuffix}.png"
                                            }
                                            : new[]
                                            {
                                                $"assets/equipements/{variantItemData.TextureName}_{part.Name}_1_{variantSuffix}_{layer}.png",
                                                $"assets/equipements/{variantItemData.TextureName}_{part.Name}_{variantSuffix}_{layer}.png"
                                            };

                                        foreach (var variantPath in variantCandidates)
                                        {
                                            var loadedVariantTex = LoadAccessoryTexture(variantPath);
                                            if (loadedVariantTex.Id != 0)
                                            {
                                                equipment.EquipmentTextures[variantKey] = loadedVariantTex;
                                                equipTex = loadedVariantTex;
                                                key = variantKey;
                                                found = true;
                                                break;
                                            }
                                        }

                                        if (!found)
                                        {
                                            equipment.EquipmentTextures[variantKey] = default;
                                        }
                                    }
                                }
                            }

                            if (!found)
                            {
                                if (!equipment.EquipmentTextures.TryGetValue(baseKey, out equipTex) || equipTex.Id == 0)
                                {
                                    if (!equipment.EquipmentTextures.TryGetValue(part.Name, out equipTex) || equipTex.Id == 0)
                                        continue;
                                }
                                key = baseKey;
                            }

                            Color layerColor = equipItem.GetLayerColor(layer - 1);

                            float tw = equipTex.Width;
                            float th = equipTex.Height;
                            Rectangle src = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                            Rectangle dest = new(globalPos.X, globalPos.Y, tw * speciesScale, th * speciesScale);
                            Vector2 origin = new(dest.Width / 2f, dest.Height / 2f);
                            DrawTextureProWithFlash(equipTex, src, dest, origin, globalRot, layerColor, flashWhite);

                            bool isCoatItem = string.Equals(equipItem.Name, "coat", StringComparison.OrdinalIgnoreCase)
                                || (GameData.ItemDatabase.TryGetValue(GetItemId(equipItem.Name), out var equipItemData) &&
                                    (string.Equals(equipItemData.TextureName, "coat", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(equipItemData.Key, "coat", StringComparison.OrdinalIgnoreCase)));
                            if (layer == 1 && isCoatItem)
                            {
                                string overlayCategory = part.Name == "body" ? "body" :
                                    (part.Name == "larmtop" || part.Name == "larmbottom" ||
                                     part.Name == "rarmtop" || part.Name == "rarmbottom" ? "sleeves" : "");
                                if (!string.IsNullOrEmpty(overlayCategory))
                                {
                                    string overlayVariant = variantSuffix ?? (overlayCategory == "body" ? "closed" : "long");
                                    string overlayKey = $"variant_{overlayCategory}_{overlayVariant}";
                                    if (!equipment.EquipmentTextures.TryGetValue(overlayKey, out var overlayTex) || overlayTex.Id == 0)
                                    {
                                        overlayTex = LoadCoatVariantOverlayTexture(equipItem, overlayCategory, overlayVariant);
                                        equipment.EquipmentTextures[overlayKey] = overlayTex;
                                    }

                                    if (overlayTex.Id != 0)
                                    {
                                        float overlayW = overlayTex.Width;
                                        float overlayH = overlayTex.Height;
                                        Rectangle overlaySrc = new(facing < 0 ? overlayW : 0, 0, overlayW * facing, overlayH);
                                        Rectangle overlayDest = new(globalPos.X, globalPos.Y, overlayW * speciesScale, overlayH * speciesScale);
                                        Vector2 overlayOrigin = new(overlayDest.Width / 2f, overlayDest.Height / 2f);
                                        DrawTextureProWithFlash(overlayTex, overlaySrc, overlayDest, overlayOrigin, globalRot, layerColor, flashWhite);
                                    }
                                }
                            }
                        }
                    }
                }

                // ===== UNDERWEAR ===== (uniquement en forme humaine : un morph animal n'a pas de pagne)
                if (part.Name == "body" && speciesKey == "human" && (equipment == null || legsEquipItem == null) && underwearTexture.Id != 0)
                {
                    float tw = underwearTexture.Width;
                    float th = underwearTexture.Height;
                    Rectangle src = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                    Rectangle dest = new(globalPos.X, globalPos.Y, tw * speciesScale, th * speciesScale);
                    Vector2 origin = new(dest.Width / 2f, dest.Height / 2f);
                    DrawTextureProWithFlash(underwearTexture, src, dest, origin, globalRot, Color.White, flashWhite);
                }

                // ===== BACKPACK =====
                if (part.Name == "body" && equipment != null && backpackEquipItem != null)
                {
                    var backpackItem = backpackEquipItem;
                    if (backpackItem == null)
                        continue;

                    int definedLayers = backpackLayers;
                    int customColorCount = backpackItem.CustomColors?.Count ?? 0;
                    int actualLayers = lowDetail ? 1 : Math.Max(definedLayers, customColorCount + 1);

                    if (GameData.ItemDatabase.TryGetValue(GetItemId(backpackItem.Name), out var backpackItemData) &&
                        !string.IsNullOrEmpty(backpackItemData.TextureName) &&
                        equipment.EquipmentTextures.TryGetValue($"base:{backpackItemData.TextureName}_body", out var backpackBaseTex) &&
                        backpackBaseTex.Id != 0)
                    {
                        float baseWidth = backpackBaseTex.Width;
                        float baseHeight = backpackBaseTex.Height;
                        Rectangle baseSource = new(facing < 0 ? baseWidth : 0, 0, baseWidth * facing, baseHeight);
                        Rectangle baseDestination = new(globalPos.X, globalPos.Y, baseWidth * speciesScale, baseHeight * speciesScale);
                        Vector2 baseOrigin = new(baseDestination.Width / 2f, baseDestination.Height / 2f);
                        DrawTextureProWithFlash(backpackBaseTex, baseSource, baseDestination, baseOrigin, globalRot, Color.White, false);
                    }

                    for (int layer = 1; layer <= actualLayers; layer++)
                    {
                        string key;
                        if (layer == 1)
                            key = "backpack";
                        else
                            key = $"backpack_{layer}";

                        if (!equipment.EquipmentTextures.TryGetValue(key, out var backpackTex) || backpackTex.Id == 0)
                            continue;

                        Color backpackColor = backpackItem.GetLayerColor(layer - 1);

                        float tw = backpackTex.Width;
                        float th = backpackTex.Height;
                        Rectangle src = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                        Rectangle dest = new(globalPos.X, globalPos.Y, tw * speciesScale, th * speciesScale);
                        Vector2 origin = new(dest.Width / 2f, dest.Height / 2f);
                        DrawTextureProWithFlash(backpackTex, src, dest, origin, globalRot, backpackColor, flashWhite);
                    }
                }

                // ===== LEGS OVERLAY ON BODY =====
                if (equipment != null && part.Name == "body" && legsEquipItem != null)
                {
                    var legsItem = legsEquipItem;
                    if (legsItem == null)
                        continue;

                    int definedLayers = legsLayers;
                    int actualLayers = lowDetail ? 1 : definedLayers;

                    if (GameData.ItemDatabase.TryGetValue(GetItemId(legsItem.Name), out var legsItemDataForBase) &&
                        !string.IsNullOrEmpty(legsItemDataForBase.TextureName) &&
                        equipment.EquipmentTextures.TryGetValue($"base:{legsItemDataForBase.TextureName}_body", out var legsBaseTex) &&
                        legsBaseTex.Id != 0)
                    {
                        float baseWidth = legsBaseTex.Width;
                        float baseHeight = legsBaseTex.Height;
                        Rectangle baseSource = new(facing < 0 ? baseWidth : 0, 0, baseWidth * facing, baseHeight);
                        Rectangle baseDestination = new(globalPos.X, globalPos.Y, baseWidth * speciesScale, baseHeight * speciesScale);
                        Vector2 baseOrigin = new(baseDestination.Width / 2f, baseDestination.Height / 2f);
                        DrawTextureProWithFlash(legsBaseTex, baseSource, baseDestination, baseOrigin, globalRot, Color.White, false);
                    }

                    for (int layer = 1; layer <= actualLayers; layer++)
                    {
                        string textureName = "";
                        if (GameData.ItemDatabase.TryGetValue(GetItemId(legsItem.Name), out var legsItemData))
                            textureName = legsItemData.TextureName;

                        string key = layer == 1
                            ? $"{textureName}_body"
                            : $"{textureName}_body_{layer}";

                        if ((!equipment.EquipmentTextures.TryGetValue(key, out var legsBodyTex) || legsBodyTex.Id == 0) &&
                            !equipment.EquipmentTextures.TryGetValue(layer == 1 ? "body_legs" : $"body_legs_{layer}", out legsBodyTex))
                            continue;

                        Color legsColor = legsItem.GetLayerColor(layer - 1);

                        float tw = legsBodyTex.Width;
                        float th = legsBodyTex.Height;
                        Rectangle src = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                        Rectangle dest = new(globalPos.X, globalPos.Y, tw * speciesScale, th * speciesScale);
                        Vector2 origin = new(dest.Width / 2f, dest.Height / 2f);
                        DrawTextureProWithFlash(legsBodyTex, src, dest, origin, globalRot, legsColor, flashWhite);
                    }
                }

                // ===== ACCESSOIRES DU CORPS (Collier, Ceinture, Bracelets, Bagues, etc.) =====
                //  PERF LOD : sautés à faible zoom (voir lowDetail).
                if (!lowDetail && equipment != null && part.Name == "body" && isHumanoid)
                {
                    // Collier / Écharpe
                    if (necklaceItem != null)
                    {
                        DrawAccessoryInSlot(equipment, BodyZone.Neck, "body", globalPos, globalRot, facing, speciesScale, necklaceLayers, flashWhite, necklaceItem);
                    }
                    // Ceinture / accessoire de bassin
                    if (equipment.GetZoneItem(BodyZone.Waist) != null)
                    {
                        DrawAccessoryInSlot(equipment, BodyZone.Waist, "body", globalPos, globalRot, facing, speciesScale, necklaceLayers, flashWhite);
                    }
                }
                // Note : ceinture / bagues / bracelets / sous-vêtement ne font plus partie du système
                // d'équipement (unifié par zones du corps) ; aucun item de la base ne les utilise.

                // Chaussettes / chaussures (dessiner symétriquement sur les deux jambes)
                //  PERF LOD : sautées à faible zoom (voir lowDetail).
                if (!lowDetail && equipment != null && isHumanoid && socksItem != null)
                {
                    {
                        // Dessiner sur la jambe droite quand on arrive sur rlegbottom
                        if (part.Name == "rlegbottom")
                        {
                            DrawAccessoryInSlot(equipment, BodyZone.Feet, "rlegbottom", globalPos, globalRot, facing, speciesScale, socksLayers, flashWhite, socksItem);

                            // Trouver la position globale de la jambe gauche et dessiner symétriquement
                            if (partIndexMap.TryGetValue("llegbottom", out int llegIdx))
                            {
                                Vector2 leftPos = globalPositions[llegIdx];
                                float leftRot = globalRotations[llegIdx];
                                DrawAccessoryInSlot(equipment, BodyZone.Feet, "llegbottom", leftPos, leftRot, facing, speciesScale, socksLayers, flashWhite, socksItem);
                            }
                        }
                        // Si on boucle sur la jambe gauche, ne rien faire pour éviter double-draw
                    }
                }

                if (!lowDetail && equipment != null && isHumanoid
                    && (part.Name == "rarmbottom" || part.Name == "larmbottom"))
                {
                    Item? glovesItem = equipment.GetZoneItem(BodyZone.Hands);
                    if (glovesItem != null)
                    {
                        int glovesId = GetItemId(glovesItem.Name);
                        if (GameData.ItemDatabase.TryGetValue(glovesId, out var glovesData))
                        {
                            int glovesLayers = Math.Max(glovesData.DyeLayers, (glovesItem.CustomColors?.Count ?? 0) + 1);
                            DrawAccessoryInSlot(equipment, BodyZone.Hands, part.Name, globalPos, globalRot,
                                facing, speciesScale, Math.Max(1, glovesLayers), flashWhite, glovesItem);
                        }
                    }
                }

                // ===== HELD ITEM (sur la main droite) =====
                if (isHumanoid && part.Name == "rarmbottom" && heldItemTexture.Id != 0)
                {
                    float tw = heldItemTexture.Width;
                    float th = heldItemTexture.Height;
                    Rectangle src = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                    Vector2 basePos = globalPos;
                    Vector2 finalPos = basePos;
                    if (keepItemHorizontal)
                    {
                        finalPos = new Vector2(basePos.X + 35f * facing, basePos.Y + 15f);
                    }
                    Rectangle dest = new(finalPos.X, finalPos.Y, tw * speciesScale, th * speciesScale);
                    if (_heldItemFallbackTextureIds.Contains(heldItemTexture.Id))
                    {
                        dest.Width *= 0.5f;
                        dest.Height *= 0.5f;
                    }
                    Vector2 origin = new(dest.Width / 2f, dest.Height / 2f);
                    float finalRotation = keepItemHorizontal ? 0f : globalRot;
                    bool isTaggedPotion = heldItemInstance != null && ItemRenderer.IsPotionMetadata(heldItemInstance.Metadata);
                    isTaggedPotion = isTaggedPotion && heldItemInstance != null && !Program.IsLiquidContainer(heldItemInstance);
                    if (isTaggedPotion && heldItemInstance != null && GameData.TryGetItemByName(heldItemInstance.Name, out var heldItemData))
                    {
                        if (GameData.ItemDatabaseByKey.TryGetValue("glassbottle_small", out var bottleData))
                            heldItemData = bottleData;

                        ItemRenderer.DrawItemRotated(
                            heldItemData,
                            finalPos,
                            MathF.Min(dest.Width, dest.Height),
                            finalRotation,
                            heldItemInstance.CustomColor,
                            heldItemInstance.Metadata);
                    }
                    else
                    {
                        DrawTextureProWithFlash(heldItemTexture, src, dest, origin, finalRotation, Color.White, flashWhite);
                        if (heldItemInstance != null)
                        {
                            ItemRenderer.DrawLiquidContainerOverlay(
                                heldItemInstance, dest, origin, finalRotation, facing, held: true);
                        }
                    }

                    var carriedFurniture = Program.CurrentCarriedFurniture;
                    if (keepItemHorizontal && carriedFurniture?.PlaceableId == 86 &&
                        int.TryParse(carriedFurniture.PottedFlowerItemId, out int flowerItemId))
                    {
                        Rectangle potRect = new Rectangle(
                            finalPos.X - dest.Width / 2f,
                            finalPos.Y - dest.Height / 2f,
                            dest.Width,
                            dest.Height);
                        int flowerTileId = World.GetPottedFlowerTileId(flowerItemId);
                        Texture2D flowerTexture = flowerTileId != 0
                            ? WorldTileRegistry.GetFirstTexture(flowerTileId)
                            : default;
                        if (flowerTexture.Id != 0 && World.TryGetPottedFlowerDestination(flowerItemId, potRect, out _, out Rectangle flowerDestination))
                        {
                            float flowerSourceWidth = flowerTexture.Width * facing;
                            Raylib.DrawTexturePro(flowerTexture,
                                new Rectangle(facing < 0 ? flowerTexture.Width : 0, 0, flowerSourceWidth, flowerTexture.Height),
                                flowerDestination,
                                Vector2.Zero, 0, Color.White);
                        }
                    }
                }

                // ===== OFFHAND ITEM (sur la main gauche, ex: panier) =====
                if (isHumanoid && part.Name == "larmbottom" && equipment?.OffHand != null)
                {
                    Texture2D offHandTex = GetOffHandItemTexture(equipment.OffHand);
                    if (offHandTex.Id != 0)
                    {
                        float tw = offHandTex.Width;
                        float th = offHandTex.Height;
                        Rectangle src = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                        Rectangle dest = new(globalPos.X, globalPos.Y, tw * speciesScale, th * speciesScale);
                        Vector2 origin = new(dest.Width / 2f, dest.Height / 2f);
                        DrawTextureProWithFlash(offHandTex, src, dest, origin, globalRot, Color.White, flashWhite);
                    }
                }
            }

            // Le masque de visage doit rester devant les couches de tête de tenues intégrales
            // (par exemple la visière de la combinaison Hazmat), quelle que soit leur partie.
            if (equipment != null && isHumanoid && partIndexMap.TryGetValue(headPartName, out int faceHeadIndex))
            {
                DrawAccessoryInSlot(equipment, BodyZone.Face, "head", globalPositions[faceHeadIndex], globalRotations[faceHeadIndex],
                    facing, speciesScale, glassesLayers, flashWhite, glassesItem);
            }

            //  Toute la silhouette (cheveux + pièces du squelette) a été dessinée opaque dans la
            // texture hors-écran depuis BeginGhostComposite plus haut : on l'aplatit maintenant
            // en un seul DrawTexturePro passé au shader fantôme, ce qui élimine le double
            // alpha-blending aux jointures (ex : haut/bas de jambe) puisqu'il n'y a plus qu'UN
            // SEUL pixel semi-transparent par endroit, comme en rendu normal.
            if (ghostShaderActive)
                EndGhostComposite(Color.White);

            // La monture/créature portée "de secours" (si jamais aucune pièce "body"/"rarm*" ne
            // correspondait plus haut) se dessine directement à l'écran, APRÈS la silhouette déjà
            // aplatie ci-dessus : elle n'a jamais fait partie de la composition de cette entité.
            if (!carriedEntityDrawn && drawCarriedEntity != null)
                drawCarriedEntity();

            if (!mountDrawn && drawMount != null)
                drawMount();
            }
            finally
            {
                //  Filet de sécurité : si une exception a interrompu le dessin avant qu'on
                // n'atteigne EndGhostComposite ci-dessus, la texture hors-écran et/ou la caméra
                // "monde" peuvent être restées actives sur le mauvais framebuffer — on les
                // referme sans tenter de dessiner (on ne veut pas risquer une deuxième exception
                // dans un bloc finally).
                if (_ghostCompositeActive)
                {
                    Raylib.EndMode2D();
                    Raylib.EndTextureMode();
                    _ghostCompositeActive = false;
                    Raylib.BeginMode2D(Program.GetCurrentCamera());
                }
                ArrayPool<Vector2>.Shared.Return(globalPositions);
                ArrayPool<float>.Shared.Return(globalRotations);
                ArrayPool<bool>.Shared.Return(computed);
            }
        }

        public static float GetVisualTopY(
            string speciesName, string anim, int frame, float prog, float facing,
            Vector2 pos, float customScale = 1f)
        {
            string speciesKey = ResolveRenderSpecies(speciesName).ToLowerInvariant();
            if (!SpeciesData.Skeletons.TryGetValue(speciesKey, out var skeleton) || skeleton.Count == 0)
                return pos.Y - Program.TileSize * 0.8f;

            float speciesScale = BASE_ENTITY_SCALE * customScale;
            if (SpeciesData.Species.TryGetValue(speciesKey, out var speciesInfo))
                speciesScale *= speciesInfo.Scale;

            if (!_partIndexCache.TryGetValue(speciesKey, out var partIndex))
            {
                partIndex = new Dictionary<string, int>(skeleton.Count);
                for (int i = 0; i < skeleton.Count; i++) partIndex[skeleton[i].Name] = i;
                _partIndexCache[speciesKey] = partIndex;
            }
            var positions = ArrayPool<Vector2>.Shared.Rent(skeleton.Count);
            var rotations = ArrayPool<float>.Shared.Rent(skeleton.Count);
            var computed = ArrayPool<bool>.Shared.Rent(skeleton.Count);
            Array.Clear(computed, 0, skeleton.Count);
            float topY = float.PositiveInfinity;

            try
            {
            void ComputePart(int index)
            {
                if (computed[index]) return;
                AnimalBodyPart part = skeleton[index];
                Vector2 animOffset = Vector2.Zero;
                float animRotation = 0f;
                if (!string.IsNullOrEmpty(anim) && part.Animations != null && part.Animations.TryGetValue(anim, out var frames) && frames.Count > 0)
                {
                    int safeFrame = frame % frames.Count;
                    int nextFrame = (safeFrame + 1) % frames.Count;
                    animOffset = Vector2.Lerp(
                        new Vector2(frames[safeFrame].X, frames[safeFrame].Y),
                        new Vector2(frames[nextFrame].X, frames[nextFrame].Y), prog) * speciesScale;
                    animRotation = Raymath.Lerp(frames[safeFrame].Z, frames[nextFrame].Z, prog);
                }

                Vector2 local = new(part.BasePos.X * speciesScale, part.BasePos.Y * speciesScale);
                local += new Vector2(animOffset.X * facing, animOffset.Y);
                if (facing < 0) local.X = -local.X;
                float localRotation = (part.BaseRot + animRotation) * facing;

                if (!string.IsNullOrEmpty(part.ParentName) && partIndex.TryGetValue(part.ParentName, out int parentIndex))
                {
                    ComputePart(parentIndex);
                    rotations[index] = rotations[parentIndex] + localRotation;
                    positions[index] = positions[parentIndex] + RotateVec(local, rotations[parentIndex]);
                }
                else
                {
                    rotations[index] = localRotation;
                    positions[index] = pos + local;
                }
                computed[index] = true;

                if (part.Texture.Id != 0)
                {
                    float halfW = part.Texture.Width * speciesScale * 0.5f;
                    float halfH = part.Texture.Height * speciesScale * 0.5f;
                    float angle = rotations[index] * (MathF.PI / 180f);
                    float verticalExtent = MathF.Abs(MathF.Cos(angle)) * halfH + MathF.Abs(MathF.Sin(angle)) * halfW;
                    topY = MathF.Min(topY, positions[index].Y - verticalExtent);
                }
            }

            for (int i = 0; i < skeleton.Count; i++) ComputePart(i);
            return float.IsPositiveInfinity(topY) ? pos.Y - Program.TileSize * 0.8f : topY;
            }
            finally
            {
                ArrayPool<Vector2>.Shared.Return(positions);
                ArrayPool<float>.Shared.Return(rotations);
                ArrayPool<bool>.Shared.Return(computed);
            }
        }

        //  Pièces du squelette réellement dessinées pour chaque BodyZone, dans le même ordre
        // et avec les mêmes noms de bone que DrawEntity (voir DrawAccessoryInSlot et la boucle
        // "ÉQUIPEMENT PRINCIPAL"/socks plus haut). Sert de base commune au hit-test précis et
        // à l'aperçu fantôme dans l'inventaire, pour qu'ils correspondent pixel pour pixel au
        // rendu réel du personnage.
        private static readonly Dictionary<BodyZone, string[]> _zoneCoverageParts = new()
        {
            { BodyZone.TopOfHead, new[] { "head" } },
            { BodyZone.Face,      new[] { "head" } },
            { BodyZone.Ears,      new[] { "head" } },
            { BodyZone.Neck,      new[] { "body" } },
            { BodyZone.Waist,     new[] { "body" } },
            { BodyZone.Torso,     new[] { "body", "larmtop", "larmbottom", "rarmtop", "rarmbottom" } },
            { BodyZone.Legs,      new[] { "llegtop", "llegbottom", "rlegtop", "rlegbottom" } },
            { BodyZone.Feet,      new[] { "rlegbottom", "llegbottom" } },
            { BodyZone.Back,      new[] { "body" } },
            { BodyZone.Hands,    new[] { "rarmbottom", "larmbottom" } },
        };

        // Une pièce de silhouette individuelle (une texture, sur un bone donné) composant la
        // zone couverte par un item. Un item couvrant plusieurs bones (ex: une tenue hazmat
        // sur Torso -> body/larmtop/larmbottom/rarmtop/rarmbottom) renvoie plusieurs pièces.
        public struct ZonePieceRect
        {
            public Rectangle Rect;         // Rectangle écran (position + taille réelles du sprite, non tourné)
            public Texture2D Texture;
            public string TexturePath;     // Chemin du fichier, pour le test alpha pixel-perfect
            public bool FlipX;
            public bool IsBack;
            public string PartName;
            public int Layer;
            public Color Tint;
        }

        //  Calcule les silhouettes réelles (une par bone couvert) qu'un item occuperait à
        // l'écran pour une BodyZone donnée, sur l'aperçu du personnage. Utilise EXACTEMENT le
        // même calcul de position/échelle que le rendu réel (bone + texture centrée dessus,
        // taille = texture * échelle d'espèce), donc les rectangles renvoyés collent au sprite
        // affiché - fini les ronds génériques qui ne correspondent à rien de visuel.
        // Fonctionne pour un item déjà équipé comme pour un item juste survolé/en cours de
        // drag (la texture est chargée à la demande via Equipment.GetItemPartTexture).
        //  Limite connue : suppose une rotation de bone ~0 (vrai pour l'anim "idle" frame 0
        // utilisée dans l'aperçu de l'inventaire) ; un bone tourné donnerait un rectangle
        // légèrement désaxé par rapport au sprite tourné.
        public static List<ZonePieceRect> GetZonePieceRects(
            string speciesName, string anim, int frame,
            float facing, Vector2 pos,
            Dictionary<string, List<AnimalBodyPart>> skeletons,
            float customScale,
            BodyZone zone, ItemData itemData, Item? item = null)
        {
            var result = new List<ZonePieceRect>();
            if (string.IsNullOrEmpty(itemData.TextureName)) return result;
            if (!_zoneCoverageParts.TryGetValue(zone, out var parts)) return result;

            string speciesKey = ResolveRenderSpecies(speciesName).ToLowerInvariant();
            float speciesScale = BASE_ENTITY_SCALE;
            if (SpeciesData.Species.TryGetValue(speciesKey, out var speciesInfo))
                speciesScale = BASE_ENTITY_SCALE * speciesInfo.Scale * customScale;

            // Un même bone peut être partagé par plusieurs BodyZone (ex: TopOfHead/Face/Ears
            // pointent tous sur "head"). Pour un item qui couvre plusieurs de ces zones à la
            // fois, on ne veut renvoyer la pièce qu'une seule fois par bone, sinon l'appelant
            // (qui agrège par zone) finit par dessiner la même texture plusieurs fois l'une sur
            // l'autre - un aperçu semi-transparent empilé plusieurs fois redevient quasi opaque.
            var seenParts = new HashSet<string>(StringComparer.Ordinal);

            foreach (var part in parts)
            {
                if (!seenParts.Add(part)) continue;

                string? variantSuffix = item == null ? null : GetCraftVariantSuffix(item, part);
                if (variantSuffix == null && item != null && IsCoatItem(item, itemData))
                {
                    if (part == "body")
                        variantSuffix = "closed";
                    else if (part == "larmtop" || part == "larmbottom" || part == "rarmtop" || part == "rarmbottom")
                        variantSuffix = "short";
                }

                var (globalPos, _) = GetGlobalPartTransform(
                    speciesName, anim, frame, 0f, facing, pos, skeletons, part,
                    customScale: customScale);

                void AddPiece(string path, int layer, Color layerTint, bool isBack = false)
                {
                    Texture2D texture = Equipment.GetOrLoadEquipTextureByPath(path);
                    if (texture.Id == 0) return;

                    float textureWidth = texture.Width * speciesScale;
                    float textureHeight = texture.Height * speciesScale;
                    result.Add(new ZonePieceRect
                    {
                        Rect = new Rectangle(globalPos.X - textureWidth / 2f, globalPos.Y - textureHeight / 2f, textureWidth, textureHeight),
                        Texture = texture,
                        TexturePath = path,
                        FlipX = facing < 0,
                        IsBack = isBack,
                        PartName = part,
                        Layer = layer,
                        Tint = layerTint,
                    });
                }

                void AddFirstPiece(List<string> paths, int layer, Color layerTint)
                {
                    foreach (string path in paths)
                    {
                        Texture2D texture = Equipment.GetOrLoadEquipTextureByPath(path);
                        if (texture.Id == 0) continue;
                        AddPiece(path, layer, layerTint);
                        string backPath = GetBackTexturePath(path);
                        Texture2D backTexture = LoadBackVariantPath(backPath);
                        if (backTexture.Id != 0)
                            AddPiece(backPath, layer, layerTint, isBack: true);
                        return;
                    }
                }

                if (!string.Equals(variantSuffix, "none", StringComparison.OrdinalIgnoreCase))
                {
                    var basePaths = new List<string>();
                    if (!string.IsNullOrEmpty(variantSuffix))
                        basePaths.Add($"assets/equipements/{itemData.TextureName}_{part}_{variantSuffix}.png");
                    basePaths.Add($"assets/equipements/{itemData.TextureName}_{part}.png");
                    AddFirstPiece(basePaths, 0, Color.White);

                    int definedLayers = itemData.DyeLayers > 0 ? itemData.DyeLayers : 1;
                    int layers = item == null ? definedLayers : Math.Max(definedLayers, (item.CustomColors?.Count ?? 0) + 1);
                    for (int layer = 1; layer <= layers; layer++)
                    {
                        var layerPaths = new List<string>();
                        if (layer == 1 || layer > definedLayers)
                        {
                            if (!string.IsNullOrEmpty(variantSuffix))
                                layerPaths.Add($"assets/equipements/{itemData.TextureName}_{part}_1_{variantSuffix}.png");
                            layerPaths.Add($"assets/equipements/{itemData.TextureName}_{part}_1.png");
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(variantSuffix))
                                layerPaths.Add($"assets/equipements/{itemData.TextureName}_{part}_{variantSuffix}_{layer}.png");
                            layerPaths.Add($"assets/equipements/{itemData.TextureName}_{part}_{layer}.png");
                        }

                        AddFirstPiece(layerPaths, layer, item?.GetLayerColor(layer - 1) ?? Color.White);
                    }
                }

                if (item != null && IsCoatItem(item, itemData))
                {
                    string? overlayCategory = part == "body" ? "body" :
                        (part == "larmtop" || part == "larmbottom" || part == "rarmtop" || part == "rarmbottom" ? "sleeves" : null);
                    string? overlayVariant = overlayCategory == null ? null :
                        GetCraftVariantSuffix(item, part) ?? (overlayCategory == "body" ? "closed" : "long");
                    string? overlayPath = overlayCategory == null || overlayVariant == "none"
                        ? null
                        : GetCoatVariantOverlayPath(item, overlayCategory, overlayVariant);
                    if (overlayPath != null)
                    {
                        var overlayTex = Equipment.GetOrLoadEquipTextureByPath(overlayPath);
                        if (overlayTex.Id != 0)
                        {
                            float overlayW = overlayTex.Width * speciesScale;
                            float overlayH = overlayTex.Height * speciesScale;
                            result.Add(new ZonePieceRect
                            {
                                Rect = new Rectangle(globalPos.X - overlayW / 2f, globalPos.Y - overlayH / 2f, overlayW, overlayH),
                                Texture = overlayTex,
                                TexturePath = overlayPath,
                                FlipX = facing < 0,
                                PartName = $"{part}_coat_overlay",
                                Layer = -1,
                                Tint = Color.White,
                            });
                        }
                    }
                }
            }

            return result;
        }

        //  Équivalent de GetZonePieceRects mais pour les emplacements FONCTIONNELS
        // (MainHand/OffHand : arme, bouclier, panier...) plutôt que les zones du corps. Permet
        // à l'aperçu (survol/drag) d'afficher la vraie silhouette de l'item, exactement comme
        // pour les zones d'armure, au lieu d'un rond générique.
        public static ZonePieceRect? GetSlotItemPieceRect(
            string speciesName, string anim, int frame,
            float facing, Vector2 pos,
            Dictionary<string, List<AnimalBodyPart>> skeletons,
            float customScale,
            EquipmentSlot slot, Item? item)
        {
            if (item == null) return null;
            string part = slot == EquipmentSlot.MainHand ? "rarmbottom" : "larmbottom";

            Texture2D tex = slot == EquipmentSlot.MainHand
                ? GetHeldItemTexture(item)
                : GetOffHandItemTexture(item);
            if (tex.Id == 0) return null;

            string speciesKey = ResolveRenderSpecies(speciesName).ToLowerInvariant();
            float speciesScale = BASE_ENTITY_SCALE;
            if (SpeciesData.Species.TryGetValue(speciesKey, out var speciesInfo))
                speciesScale = BASE_ENTITY_SCALE * speciesInfo.Scale * customScale;

            var (globalPos, _) = GetGlobalPartTransform(
                speciesName, anim, frame, 0f, facing, pos, skeletons, part,
                customScale: customScale);

            float tw = tex.Width * speciesScale;
            float th = tex.Height * speciesScale;

            return new ZonePieceRect
            {
                Rect = new Rectangle(globalPos.X - tw / 2f, globalPos.Y - th / 2f, tw, th),
                Texture = tex,
                TexturePath = GetSlotItemTexturePath(slot, item),
                FlipX = facing < 0,
                PartName = part,
            };
        }

        public static string GetSlotItemTexturePath(EquipmentSlot slot, Item item)
        {
            int itemId = GetItemId(item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
                return string.Empty;

            string textureName = itemData.TextureName;
            if (string.IsNullOrEmpty(textureName))
                return string.Empty;

            if (slot == EquipmentSlot.OffHand)
            {
                string offHandPath = $"assets/equipements/{textureName}_larmbottom.png";
                if (File.Exists(offHandPath)) return offHandPath;
                return $"assets/equipements/{textureName}_rarmbottom.png";
            }

            string mainHandPath = $"assets/equipements/{textureName}_rarmbottom.png";
            if (File.Exists(mainHandPath)) return mainHandPath;
            return $"assets/items/{textureName}.png";
        }

        public static Vector2 GetSpiritEyeLightPosition(
            string speciesName,
            string anim, int frame, float prog,
            float facing, Vector2 drawPosition,
            bool inWater, float customScale)
        {
            var (headPosition, headRotation) = GetGlobalPartTransform(
                speciesName, anim, frame, prog, facing, drawPosition,
                SpeciesData.Skeletons, "head", inWater, customScale);
            float renderScale = BASE_ENTITY_SCALE
                * (SpeciesData.GetSpeciesInfo(speciesName)?.Scale ?? 1f)
                * customScale;
            float eyeX = facing < 0f ? 13.5f : 18.5f;
            Vector2 headCenter = headPosition + new Vector2(16f, 16f) * renderScale;
            Vector2 eyePosition = headPosition + new Vector2(eyeX, 13.5f) * renderScale;
            return headCenter + RotateVec(eyePosition - headCenter, headRotation);
        }

        public static (Vector2 globalPos, float rotation) GetGlobalPartTransform(
            string speciesName,
            string anim, int frame, float prog,
            float facing, Vector2 pos,
            Dictionary<string, List<AnimalBodyPart>> skeletons,
            string partName,
            bool inWater = false,
            float customScale = 1.0f,
            float headAngle = 0f)
        {
            string speciesKey = speciesName?.ToLowerInvariant() ?? "";
            if (!skeletons.ContainsKey(speciesKey))
                return (pos, 0f);

            var skeleton = skeletons[speciesKey];
            int partCount = skeleton.Count;

            if (!_partIndexCache.TryGetValue(speciesKey, out var partIndexMap))
            {
                partIndexMap = new Dictionary<string, int>(partCount);
                for (int i = 0; i < partCount; i++)
                    partIndexMap[skeleton[i].Name] = i;
                _partIndexCache[speciesKey] = partIndexMap;
            }

            //  Même optimisation que dans DrawEntity : on réutilise le cache par espèce
            // des noms d'animation disponibles au lieu de refaire un .Any() LINQ (parcours
            // complet du squelette + allocation de delegate) à chaque appel, pour chaque PNJ,
            // à chaque frame. C'était la cause principale du lag à l'affichage des PNJ.
            if (!_availableAnimNamesCache.TryGetValue(speciesKey, out var availableAnims))
            {
                availableAnims = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in skeleton)
                    if (p.Animations != null)
                        foreach (var animKey in p.Animations.Keys)
                            availableAnims.Add(animKey);
                _availableAnimNamesCache[speciesKey] = availableAnims;
            }
            string animName = anim;
            bool animationExists = !string.IsNullOrEmpty(anim) && availableAnims.Contains(anim);
            if (!animationExists)
            {
                animName = availableAnims.Contains("idle") ? "idle" : "";
            }

            float speciesScale = BASE_ENTITY_SCALE;
            if (SpeciesData.Species.TryGetValue(speciesKey, out var speciesInfo))
                speciesScale = BASE_ENTITY_SCALE * speciesInfo.Scale * customScale;

            float legHeight = SpeciesData.LegHeights.GetValueOrDefault(speciesKey, 0f);
            Vector2 drawPos = pos;
            if (inWater)
                drawPos = new Vector2(pos.X, pos.Y + legHeight);

            Vector2[] globalPositions = new Vector2[partCount];
            float[] globalRotations = new float[partCount];
            bool[] computed = new bool[partCount];

            void ComputePart(int index)
            {
                if (computed[index]) return;
                var part = skeleton[index];

                Vector2 animOffset = Vector2.Zero;
                float animRot = 0f;
                bool isHumanoid = SpeciesData.IsHumanoid(speciesKey);
                bool isHoldingBow = isHumanoid && headAngle != 0;
                bool isAimingPose = isHumanoid && headAngle != 0;
                bool isArmPart = part.Name == "rarmtop" || part.Name == "rarmbottom" || part.Name == "larmtop" || part.Name == "larmbottom";

                if (part.Name == "rarmtop" && isHoldingBow)
                {
                    float baseAngle = -90f;
                    float aimAngle = headAngle * 1.2f;
                    aimAngle = Math.Clamp(aimAngle, -90f, 70f);
                    animRot = (facing < 0) ? baseAngle - aimAngle : baseAngle + aimAngle;
                }
                else if (isAimingPose && isArmPart)
                {
                    animOffset = Vector2.Zero;
                    animRot = 0f;
                }
                else if (!string.IsNullOrEmpty(animName) && part.Animations != null && part.Animations.TryGetValue(animName, out var frames) && frames.Count > 0)
                {
                    int safeFrame = frame % frames.Count;
                    int ni = (safeFrame + 1) % frames.Count;
                    float t = prog;
                    animOffset = Vector2.Lerp(
                        new Vector2(frames[safeFrame].X, frames[safeFrame].Y),
                        new Vector2(frames[ni].X, frames[ni].Y), t) * speciesScale;
                    animRot = Raymath.Lerp(frames[safeFrame].Z, frames[ni].Z, t);
                }

                float additionalHeadRot = (part.Name == "head") ? headAngle * facing : 0f;
                Vector2 fb = new(part.BasePos.X * speciesScale, part.BasePos.Y * speciesScale);
                Vector2 fa = new(animOffset.X * facing, animOffset.Y);
                Vector2 finalOffset = fb + fa;
                if (facing < 0)
                    finalOffset = new Vector2(-finalOffset.X, finalOffset.Y);

                if (!string.IsNullOrEmpty(part.ParentName) && partIndexMap.TryGetValue(part.ParentName, out int parentIdx))
                {
                    ComputePart(parentIdx);
                    float parentRot = globalRotations[parentIdx];
                    Vector2 parentPos = globalPositions[parentIdx];
                    globalRotations[index] = parentRot + (part.BaseRot + animRot + additionalHeadRot) * facing;
                    globalPositions[index] = parentPos + RotateVec(finalOffset, parentRot);
                }
                else
                {
                    globalPositions[index] = drawPos + finalOffset;
                    globalRotations[index] = (part.BaseRot + animRot + additionalHeadRot) * facing;
                }

                computed[index] = true;
            }

            if (!partIndexMap.TryGetValue(partName, out int targetIndex))
                return (pos, 0f);

            ComputePart(targetIndex);
            return (globalPositions[targetIndex], globalRotations[targetIndex]);
        }
        
        private static readonly Dictionary<string, Texture2D> BackVariantTextureCache = new(StringComparer.OrdinalIgnoreCase);

        private static Texture2D LoadBackVariantPath(string texturePath)
        {
            if (BackVariantTextureCache.TryGetValue(texturePath, out var cachedTexture))
                return cachedTexture;

            Texture2D texture = Equipment.GetOrLoadEquipTextureByPath(texturePath);
            BackVariantTextureCache[texturePath] = texture;
            return texture;
        }

        private static string GetBackTexturePath(string texturePath)
        {
            int extensionIndex = texturePath.LastIndexOf('.');
            return extensionIndex < 0
                ? texturePath + "_back"
                : texturePath[..extensionIndex] + "_back" + texturePath[extensionIndex..];
        }

        private static Texture2D LoadBackVariant(string texturePathWithoutExtension) =>
            LoadBackVariantPath(texturePathWithoutExtension + "_back.png");

        private static bool EquipmentZoneUsesPart(BodyZone zone, string partName) => zone switch
        {
            BodyZone.TopOfHead or BodyZone.Face or BodyZone.Ears => partName == "head",
            BodyZone.Neck or BodyZone.Waist or BodyZone.Back => partName == "body",
            BodyZone.Torso => partName == "body" || partName.Contains("arm"),
            BodyZone.Legs => partName.Contains("leg") || partName == "body",
            BodyZone.Feet => partName == "rlegbottom" || partName == "llegbottom",
            BodyZone.Hands => partName == "rarmbottom" || partName == "larmbottom",
            _ => false
        };

        private static void DrawEquipmentBackVariants(
            Equipment equipment,
            string partName,
            Vector2 globalPos,
            float globalRot,
            float facing,
            float speciesScale,
            bool flashWhite,
            bool lowDetail)
        {
            foreach (var item in equipment.ZoneItems)
            {
                int itemId = GetItemId(item.Name);
                if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)
                    || string.IsNullOrEmpty(itemData.TextureName))
                    continue;

                bool usesPart = false;
                foreach (var zone in itemData.CoveredZones)
                {
                    if (lowDetail && (zone == BodyZone.Ears || zone == BodyZone.Neck || zone == BodyZone.Waist
                        || zone == BodyZone.Feet || zone == BodyZone.Hands))
                        continue;
                    if (!EquipmentZoneUsesPart(zone, partName)) continue;
                    usesPart = true;
                    break;
                }
                if (!usesPart) continue;

                string? variantSuffix = GetCraftVariantSuffix(item, partName);
                if (variantSuffix == null && IsCoatItem(item, itemData))
                {
                    if (partName == "body")
                        variantSuffix = "closed";
                    else if (partName == "larmtop" || partName == "rarmtop" || partName == "larmbottom" || partName == "rarmbottom")
                        variantSuffix = "short";
                }
                if (string.Equals(variantSuffix, "none", StringComparison.OrdinalIgnoreCase))
                    continue;

                string itemTexturePath = $"assets/equipements/{itemData.TextureName}_{partName}";
                Texture2D backBase = default;
                if (!string.IsNullOrEmpty(variantSuffix))
                    backBase = LoadBackVariant($"{itemTexturePath}_{variantSuffix}");
                if (backBase.Id == 0)
                    backBase = LoadBackVariant(itemTexturePath);
                DrawBackVariant(backBase, Color.White, globalPos, globalRot, facing, speciesScale, false);

                int definedLayers = itemData.DyeLayers > 0 ? itemData.DyeLayers : 1;
                int layers = Math.Max(definedLayers, (item.CustomColors?.Count ?? 0) + 1);
                for (int layer = 1; layer <= layers; layer++)
                {
                    Texture2D backLayer = default;
                    bool useLayerOneTexture = layer == 1 || layer > definedLayers;
                    if (!string.IsNullOrEmpty(variantSuffix))
                    {
                        if (useLayerOneTexture)
                            backLayer = LoadBackVariant($"{itemTexturePath}_1_{variantSuffix}");
                        else
                        {
                            backLayer = LoadBackVariant($"{itemTexturePath}_1_{variantSuffix}_{layer}");
                            if (backLayer.Id == 0)
                                backLayer = LoadBackVariant($"{itemTexturePath}_{variantSuffix}_{layer}");
                        }
                    }

                    if (backLayer.Id == 0)
                        backLayer = LoadBackVariant(useLayerOneTexture
                            ? $"{itemTexturePath}_1"
                            : $"{itemTexturePath}_{layer}");

                    DrawBackVariant(backLayer, item.GetLayerColor(layer - 1),
                        globalPos, globalRot, facing, speciesScale, flashWhite);
                }
            }
        }

        private static void DrawBackVariant(
            Texture2D texture,
            Color tint,
            Vector2 globalPos,
            float globalRot,
            float facing,
            float speciesScale,
            bool flashWhite)
        {
            if (texture.Id == 0) return;

            float width = texture.Width;
            float height = texture.Height;
            Rectangle source = new(facing < 0 ? width : 0, 0, width * facing, height);
            Rectangle destination = new(globalPos.X, globalPos.Y, width * speciesScale, height * speciesScale);
            Vector2 origin = new(destination.Width / 2f, destination.Height / 2f);
            DrawTextureProWithFlash(texture, source, destination, origin, globalRot, tint, flashWhite);
        }

        // ===== MÉTHODE D'AIDE POUR DESSINER UN ACCESSOIRE =====

        private static void DrawAccessoryInSlot(
            Equipment equipment, 
            BodyZone zone, 
            string partName,
            Vector2 globalPos, 
            float globalRot, 
            float facing, 
            float speciesScale,
            int definedLayers,
            bool flashWhite = false,
            Item? knownItem = null)
        {
            // knownItem permet au call site de fournir l'item déjà résolu (via EquipmentRenderState)
            // et d'éviter un GetZoneItem() (parcours de ZoneItems) redondant à chaque appel.
            Item? accessoryItem = knownItem ?? equipment.GetZoneItem(zone);
            if (accessoryItem == null) return;

            int itemId = GetItemId(accessoryItem.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)) return;
            string baseName = itemData.TextureName;
            if (string.IsNullOrEmpty(baseName)) return;

            int actualLayers = definedLayers;

            if (equipment.EquipmentTextures.TryGetValue($"base:{baseName}_{partName}", out var accessoryBaseTex) && accessoryBaseTex.Id != 0)
            {
                float baseWidth = accessoryBaseTex.Width;
                float baseHeight = accessoryBaseTex.Height;
                Rectangle baseSource = new(facing < 0 ? baseWidth : 0, 0, baseWidth * facing, baseHeight);
                Rectangle baseDestination = new(globalPos.X, globalPos.Y, baseWidth * speciesScale, baseHeight * speciesScale);
                Vector2 baseOrigin = new(baseDestination.Width / 2f, baseDestination.Height / 2f);
                DrawTextureProWithFlash(accessoryBaseTex, baseSource, baseDestination, baseOrigin, globalRot, Color.White, false);
            }

            for (int layer = 1; layer <= actualLayers; layer++)
            {
                string key;
                string? texPath = null;

                if (layer == 1)
                {
                    // Les accessoires teintables utilisent parfois _1 comme première couche.
                    string layeredKey = $"{baseName}_{partName}_1";
                    key = layeredKey;
                    Texture2D tex = default;
                    equipment.EquipmentTextures.TryGetValue(layeredKey, out tex);

                    if (tex.Id == 0)
                    {
                        texPath = $"assets/equipements/{baseName}_{partName}_1.png";
                        tex = LoadAccessoryTexture(texPath);
                        if (tex.Id != 0)
                        {
                            equipment.EquipmentTextures[key] = tex;
                        }
                        else
                        {
                            continue;
                        }
                    }
                }
                else
                {
                    // Couches supplémentaires : suffixe _{layer}
                    key = $"{baseName}_{partName}_{layer}";
                    if (!equipment.EquipmentTextures.TryGetValue(key, out var tex) || tex.Id == 0)
                    {
                        texPath = $"assets/equipements/{baseName}_{partName}_{layer}.png";
                        tex = LoadAccessoryTexture(texPath);
                        if (tex.Id != 0)
                        {
                            equipment.EquipmentTextures[key] = tex;
                        }
                        else
                        {
                            continue;
                        }
                    }
                }

                // Récupération de la texture chargée
                if (!equipment.EquipmentTextures.TryGetValue(key, out var accessoryTex) || accessoryTex.Id == 0)
                    continue;

                Color layerColor = accessoryItem.GetLayerColor(layer - 1);

                float tw = accessoryTex.Width;
                float th = accessoryTex.Height;
                Rectangle src = new(facing < 0 ? tw : 0, 0, tw * facing, th);
                Rectangle dest = new(globalPos.X, globalPos.Y, tw * speciesScale, th * speciesScale);
                Vector2 origin = new(dest.Width / 2f, dest.Height / 2f);
                DrawTextureProWithFlash(accessoryTex, src, dest, origin, globalRot, layerColor, flashWhite);
            }
        }
        
        //  VARIANTES DE CRAFT : retourne le suffixe de texture à utiliser pour une pièce
        // d'équipement donnée (ex: "open"/"closed" sur la partie "body", "short"/"long" sur
        // les manches "larmtop"/"rarmtop") en fonction des métadonnées choisies lors du craft
        // (Item.Metadata, rempli par CraftingUI). Retourne null si l'item n'a pas de métadonnées
        // de variante pertinentes pour cette partie (comportement inchangé : texture de base).
        private static string? GetCraftVariantSuffix(Item item, string partName)
        {
            bool isSleevePart = partName == "larmtop" || partName == "rarmtop" || partName == "larmbottom" || partName == "rarmbottom";
            string sleeveVal = item.GetMetadataValue("sleeves");
            if (isSleevePart && !string.IsNullOrEmpty(sleeveVal))
            {
                if (string.Equals(sleeveVal, "long", StringComparison.OrdinalIgnoreCase))
                    return "long";
                if (string.Equals(sleeveVal, "none", StringComparison.OrdinalIgnoreCase))
                    return "none";
                return "short";
            }

            string styleVal = item.GetMetadataValue("opening");
            if (partName == "body" && !string.IsNullOrEmpty(styleVal))
            {
                if (string.Equals(styleVal, "open", StringComparison.OrdinalIgnoreCase)) return "open";
                if (string.Equals(styleVal, "lace_up", StringComparison.OrdinalIgnoreCase)) return "lace_up";
                return "closed";
            }

            return null;
        }

        private static bool IsCoatItem(Item item, ItemData itemData) =>
            string.Equals(item.Name, "coat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(itemData.TextureName, "coat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(itemData.Key, "coat", StringComparison.OrdinalIgnoreCase);

        private static string? GetCoatVariantOverlayPath(Item item, string category, string variant)
        {
            int itemId = Program.GetItemId(item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData) || string.IsNullOrEmpty(itemData.TextureName))
                return null;

            string[] paths =
            {
                $"assets/equipements/{itemData.TextureName}_{category}_{variant}.png",
                $"assets/equipements/{itemData.TextureName}_{category}_1_{variant}.png",
                $"assets/equipements/{category}_{variant}.png",
                $"assets/equipements/{category}_1_{variant}.png",
                $"assets/items/{itemData.TextureName}_{category}_{variant}.png",
                $"assets/items/{itemData.TextureName}_{category}_1_{variant}.png",
                $"assets/items/{itemData.TextureName}_{category}_{variant}_1.png"
            };

            return paths.FirstOrDefault(File.Exists);
        }

        private static Texture2D LoadCoatVariantOverlayTexture(Item item, string category, string variant)
        {
            string? path = GetCoatVariantOverlayPath(item, category, variant);
            return path == null ? default : Equipment.GetOrLoadEquipTextureByPath(path);
        }

        private static Texture2D LoadAccessoryTexture(string path)
        {
            if (File.Exists(path))
            {
                var tex = Raylib.LoadTexture(path);
                if (tex.Id != 0) return tex;
            }
            return new Texture2D();
        }

        // ===== HELPERS =====
        
        private static Equipment GetOrCreateEquipmentForArmorStand(ArmorStandData standData)
        {
            // Le mannequin possède désormais son propre modèle Equipment complet. Le rendu monde
            // doit utiliser cette instance directement afin de conserver les nouveaux slots,
            // accessoires, couleurs et métadonnées au lieu de reconstruire une copie partielle.
            standData.Equipment ??= new Equipment();
            standData.Equipment.LoadEquipmentTextures();
            return standData.Equipment;

        }
        
        private static EquipmentRenderState GetEquipmentRenderState(Equipment? equipment)
        {
            if (equipment == null)
            {
                return new EquipmentRenderState();
            }

            string cacheKey = equipment.GetEquipmentTextureCacheKey();
            if (_equipmentRenderStateCache.TryGetValue(cacheKey, out var cachedState))
                return cachedState;

            var state = new EquipmentRenderState
            {
                CacheKey = cacheKey,
                BodyEquipItem = equipment.GetZoneItem(BodyZone.Torso),
                LegsEquipItem = equipment.GetZoneItem(BodyZone.Legs),
                HeadEquipItem = equipment.GetZoneItem(BodyZone.TopOfHead),
                BackpackEquipItem = equipment.GetZoneItem(BodyZone.Back),
                EarringItem = equipment.GetZoneItem(BodyZone.Ears),
                NecklaceItem = equipment.GetZoneItem(BodyZone.Neck),
                GlassesItem = equipment.GetZoneItem(BodyZone.Face),
                SocksItem = equipment.GetZoneItem(BodyZone.Feet)
            };

            void FillLayer(ref int target, Item? item)
            {
                if (item != null && GameData.ItemDatabase.TryGetValue(GetItemId(item.Name), out var itemData))
                    target = Math.Max(1, itemData.DyeLayers);
            }

            FillLayer(ref state.BodyLayers, state.BodyEquipItem);
            FillLayer(ref state.LegsLayers, state.LegsEquipItem);
            FillLayer(ref state.HeadLayers, state.HeadEquipItem);
            FillLayer(ref state.BackpackLayers, state.BackpackEquipItem);
            FillLayer(ref state.EarringLayers, state.EarringItem);
            FillLayer(ref state.NecklaceLayers, state.NecklaceItem);
            FillLayer(ref state.GlassesLayers, state.GlassesItem);
            FillLayer(ref state.SocksLayers, state.SocksItem);

            if (state.HeadEquipItem != null && GameData.ItemDatabase.TryGetValue(GetItemId(state.HeadEquipItem.Name), out var headData))
            {
                state.HeadArmorCategory = headData.ArmorCategory;
                state.IsFullHelmet = headData.ArmorCategory == ArmorCategory.Helmet;
            }

            _equipmentRenderStateCache[cacheKey] = state;
            return state;
        }

        public static void ClearEquipmentCache()
        {
            _equipmentCache.Clear();
            _equipmentRenderStateCache.Clear();
            _heldItemTextureCache.Clear();
            _offHandItemTextureCache.Clear();
            _partIndexCache.Clear();
            _skeletonExtentCache.Clear();
            _speciesFeatureRenderCache.Clear();
        }

        public static void InvalidateAnimationCache(string speciesName)
        {
            if (!string.IsNullOrWhiteSpace(speciesName))
                _availableAnimNamesCache.Remove(speciesName.ToLowerInvariant());
        }
        
        public static float GetArmRotationForAiming(float headAngle, float facing, string armPart)
        {
            if (armPart == "rarmtop")
            {
                float aimAngle = headAngle * 1.2f;
                aimAngle = Math.Clamp(aimAngle, -70f, 70f);
                return aimAngle;
            }
            return 0f;
        }
        
        public static Texture2D GetHeldItemTexture(Item? heldItem, bool isFurniture = false, int? placeableId = null)
        {
            if (heldItem == null && !isFurniture) return default;
            if (isFurniture && placeableId.HasValue)
            {
                var tileData = WorldTileRegistry.GetTile(placeableId.Value);
                if (tileData != null)
                {
                    Texture2D tex = WorldTileRegistry.GetFirstTexture(placeableId.Value);
                    if (tex.Id != 0) return tex;
                }
            }
            int itemId = GetItemId(heldItem.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
                return default;
            return LoadHeldItemTexture(itemData.TextureName);
        }

        public static (Texture2D Texture, string AnchorPart) GetHeldItemTextureInfo(Item? heldItem, bool isFurniture = false, int? placeableId = null)
        {
            return (GetHeldItemTexture(heldItem, isFurniture, placeableId), "rarmbottom");
        }

        //  MULTIJOUEUR : les joueurs distants ne transportent que l'id de l'item tenu
        // (PlayerTransform.HeldItemId), pas d'objet Item complet - on reproduit donc ici la
        // même logique que GetHeldItemTexture mais à partir de l'id directement, pour pouvoir
        // attacher correctement la texture "_rarmbottom" à leur bras comme pour le joueur local
        // (au lieu de l'icône statique flottante utilisée jusqu'ici).
        public static Texture2D GetHeldItemTextureById(int itemId)
        {
            if (itemId == 0) return default;
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
                return default;
            return LoadHeldItemTexture(itemData.TextureName);
        }

        private static Texture2D LoadHeldItemTexture(string? textureName)
        {
            if (string.IsNullOrEmpty(textureName)) return default;
            if (_heldItemTextureCache.TryGetValue(textureName, out var cached) && cached.Id != 0)
                return cached;

            string[] paths =
            {
                $"assets/equipements/{textureName}_rarmbottom.png",
                $"assets/items/{textureName}.png"
            };

            foreach (string path in paths)
            {
                var tex = Raylib.LoadTexture(path);
                if (tex.Id != 0)
                {
                    if (path.StartsWith("assets/items/", StringComparison.OrdinalIgnoreCase))
                        _heldItemFallbackTextureIds.Add(tex.Id);
                    _heldItemTextureCache[textureName] = tex;
                    return tex;
                }
            }

            _heldItemTextureCache[textureName] = default;
            return default;
        }

        public static Texture2D GetOffHandItemTexture(Item? offHandItem)
        {
            if (offHandItem == null) return default;
            int itemId = GetItemId(offHandItem.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
                return default;
            string texName = itemData.TextureName;
            if (string.IsNullOrEmpty(texName)) return default;
            if (_offHandItemTextureCache.TryGetValue(texName, out var cached) && cached.Id != 0)
                return cached;
            string path = $"assets/equipements/{texName}_larmbottom.png";
            if (File.Exists(path))
            {
                var tex = Raylib.LoadTexture(path);
                if (tex.Id != 0)
                {
                    _offHandItemTextureCache[texName] = tex;
                    return tex;
                }
            }
            // Fallback : réutiliser le sprite main droite si aucune variante gauche n'existe
            string fallbackPath = $"assets/equipements/{texName}_rarmbottom.png";
            if (File.Exists(fallbackPath))
            {
                var tex = Raylib.LoadTexture(fallbackPath);
                if (tex.Id != 0)
                {
                    _offHandItemTextureCache[texName] = tex;
                    return tex;
                }
            }
            _offHandItemTextureCache[texName] = default;
            return default;
        }

        private static float GetSwingRotation(float swingProgress, bool isRightArm)
        {
            if (swingProgress < 0.5f)
            {
                float t = swingProgress / 0.5f;
                return 0f - (120f * t);
            }
            else
            {
                float t = (swingProgress - 0.5f) / 0.5f;
                return -120f + (120f * t);
            }
        }
        
        private static int GetItemId(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (_itemNameToIdCache.TryGetValue(name, out var cachedId)) return cachedId;
            foreach (var kv in GameData.ItemDatabase)
            {
                if (kv.Value.Name == name)
                {
                    _itemNameToIdCache[name] = kv.Key;
                    return kv.Key;
                }
            }
            _itemNameToIdCache[name] = 0;
            return 0;
        }
        
        private static Vector2 RotateVec(Vector2 v, float angle)
        {
            float r = angle * MathF.PI / 180f;
            return new Vector2(
                v.X * MathF.Cos(r) - v.Y * MathF.Sin(r),
                v.X * MathF.Sin(r) + v.Y * MathF.Cos(r)
            );
        }

        /// <summary>
        /// Calcule la position monde d'un membre donné (par défaut "rarmbottom") en reproduisant
        /// exactement la pose "isCarryingCreature" utilisée dans DrawEntity, afin de pouvoir
        /// positionner une entité portée pile au bout du bras du porteur. Prend aussi en compte
        /// l'animation en cours (marche, idle, etc.) pour que l'ancrage bouge avec le corps du
        /// porteur, exactement comme le fait un meuble porté.
        /// </summary>
        public static Vector2 GetCarryAnchorWorldPos(
            string speciesName,
            float facing,
            Vector2 pos,
            Dictionary<string, List<AnimalBodyPart>> skeletons,
            string anim = "idle",
            int frame = 0,
            float prog = 0f,
            float customScale = 1.0f,
            string partName = "rarmbottom")
        {
            string speciesKey = speciesName?.ToLowerInvariant() ?? "";
            if (!skeletons.TryGetValue(speciesKey, out var skeleton)) return pos;

            var partIndexMap = new Dictionary<string, int>(skeleton.Count);
            for (int i = 0; i < skeleton.Count; i++) partIndexMap[skeleton[i].Name] = i;
            if (!partIndexMap.TryGetValue(partName, out int targetIdx)) return pos;

            // Résolution du nom d'anim, avec repli sur "idle" si l'anim demandée n'existe pas
            // pour ce squelette (même logique que dans DrawEntity, via le cache partagé
            // plutôt qu'un .Any() LINQ recalculé à chaque appel).
            if (!_availableAnimNamesCache.TryGetValue(speciesKey, out var availableAnims))
            {
                availableAnims = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in skeleton)
                    if (p.Animations != null)
                        foreach (var animKey in p.Animations.Keys)
                            availableAnims.Add(animKey);
                _availableAnimNamesCache[speciesKey] = availableAnims;
            }
            string animName = anim;
            bool animationExists = !string.IsNullOrEmpty(anim) && availableAnims.Contains(anim);
            if (!animationExists)
                animName = availableAnims.Contains("idle") ? "idle" : "";

            float speciesScale = BASE_ENTITY_SCALE;
            if (SpeciesData.Species.TryGetValue(speciesKey, out var speciesInfo))
                speciesScale = BASE_ENTITY_SCALE * speciesInfo.Scale * customScale;

            Vector2[] globalPositions = new Vector2[skeleton.Count];
            float[] globalRotations = new float[skeleton.Count];
            bool[] computed = new bool[skeleton.Count];

            void ComputePart(int index)
            {
                if (computed[index]) return;
                var part = skeleton[index];

                Vector2 animOffset = Vector2.Zero;
                float animRot = 0f;
                bool isCarryPoseArm = SpeciesData.IsHumanoid(speciesKey) &&
                    (part.Name == "larmtop" || part.Name == "rarmtop" || part.Name == "rarmbottom" || part.Name == "larmbottom");

                if (isCarryPoseArm)
                {
                    bool isTop = part.Name == "larmtop" || part.Name == "rarmtop";
                    animOffset = isTop ? new Vector2(2f, -3f) : new Vector2(1f, -2f);
                    animRot = isTop ? -60f : -20f;
                }
                else if (!string.IsNullOrEmpty(animName) && part.Animations != null && part.Animations.TryGetValue(animName, out var frames) && frames.Count > 0)
                {
                    // Reproduit le mouvement d'animation normal (marche, respiration idle, etc.)
                    // pour toutes les pièces qui ne sont pas repliées en pose de portage,
                    // notamment "body" — c'est ce qui fait bouger l'ancrage en Y avec le corps.
                    int safeFrame = frame % frames.Count;
                    int ni = (safeFrame + 1) % frames.Count;
                    float t = prog;
                    animOffset = Vector2.Lerp(
                        new Vector2(frames[safeFrame].X, frames[safeFrame].Y),
                        new Vector2(frames[ni].X, frames[ni].Y), t) * speciesScale;
                    animRot = Raymath.Lerp(frames[safeFrame].Z, frames[ni].Z, t);
                }

                Vector2 fb = new(part.BasePos.X * speciesScale, part.BasePos.Y * speciesScale);
                Vector2 fa = new(animOffset.X * facing, animOffset.Y);
                Vector2 finalOffset = fb + fa;
                if (facing < 0)
                    finalOffset = new Vector2(-finalOffset.X, finalOffset.Y);

                if (!string.IsNullOrEmpty(part.ParentName) && partIndexMap.TryGetValue(part.ParentName, out int parentIdx))
                {
                    ComputePart(parentIdx);
                    float parentRot = globalRotations[parentIdx];
                    Vector2 parentPos = globalPositions[parentIdx];
                    globalRotations[index] = parentRot + (part.BaseRot + animRot) * facing;
                    globalPositions[index] = parentPos + RotateVec(finalOffset, parentRot);
                }
                else
                {
                    globalPositions[index] = pos + finalOffset;
                    globalRotations[index] = (part.BaseRot + animRot) * facing;
                }

                computed[index] = true;
            }

            ComputePart(targetIdx);
            return globalPositions[targetIdx];
        }
        
        public static Equipment GetCachedEquipmentForArmorStand(ArmorStandData standData)
        {
            return GetOrCreateEquipmentForArmorStand(standData);
        }

        private static void DrawFallbackEntity(Vector2 pos, float facing, Color tint)
        {
            float size = Program.TileSize * 0.8f;
            Raylib.DrawEllipse((int)pos.X, (int)(pos.Y + Program.FeetOffsetY - 4), (int)(size/2), 6, new Color(0,0,0,80));
            Raylib.DrawRectangle((int)(pos.X - size/2), (int)(pos.Y - size), (int)size, (int)size, Color.White);
        }
    }
}