// World.Biomes.cs
#nullable enable
using Raylib_cs;
using System;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {

		private static Dictionary<(int x, int y), (float temperature, float humidity, float elevation)> _climateCache = new();

		private static Dictionary<int, List<Vector2>> _outlineCache = new();

		private static Dictionary<int, Texture2D> _biomeBorderTextures = new();

		private static Dictionary<int, List<Texture2D>> _biomeBorderAnimationFrames = new();

		private static Dictionary<int, int> _biomePriority = new();

		private static Texture2D? _cliffBorderTexCache;

		private static Texture2D GetCliffBorderTexture(Texture2D missingTex)
		{
			if (_cliffBorderTexCache == null)
			{
				var tex = Raylib.LoadTexture("assets/tiles/cliff_border.png");
				_cliffBorderTexCache = tex.Id != 0 ? tex : missingTex;
			}
			return _cliffBorderTexCache.Value;
		}

		private static Color GetWaterColor(int tileX, int tileY, float currentTime)
		{
			// Récupérer l'élévation (profondeur) - plus l'élévation est basse, plus c'est profond
            float elevation = GetClimateAt(tileX, tileY).elevation;
			
			// Facteur de profondeur (0 = très profond, 1 = très peu profond)
			float depthFactor;
			if (elevation < 0.32f) // Océan profond
			{
				depthFactor = Math.Clamp(elevation / 0.32f, 0.15f, 1f);
				depthFactor = 0.2f + depthFactor * 0.8f; // Entre 0.2 et 1.0
			}
			else if (elevation < 0.38f) // Eaux peu profondes / plages
			{
				depthFactor = 0.7f + (elevation - 0.32f) / 0.06f * 0.3f;
			}
			else
			{
				depthFactor = 0.9f;
			}
			
			// Opacité légèrement variable selon la profondeur
			byte alpha = (byte)(200 + (1 - depthFactor) * 55);
			return new Color((byte)255, (byte)255, (byte)255, (byte)alpha);
		}

		// Charger les textures de bordure pour un biome
public static bool LoadBiomeBorderTexture(int biomeGroundId, string biomeName, int priority = 0)
        {
            _biomePriority[biomeGroundId] = priority;
            bool loaded = false;

            string borderPath = $"assets/tiles/{biomeName}_border.png";
            if (File.Exists(borderPath))
            {
                var tex = Raylib.LoadTexture(borderPath);
                if (tex.Id != 0)
                {
                    _biomeBorderTextures[biomeGroundId] = tex;
                    loaded = true;
                }
            }

            var tile = WorldTileRegistry.GetTile(biomeGroundId);
            if (tile?.Animation == true)
            {
                int frameCount = tile.Variation > 0 ? tile.Variation : 4;
                var frames = new List<Texture2D>();
                for (int i = 1; i <= frameCount; i++)
                {
                    string animatedBorderPath = $"assets/tiles/{biomeName}-{i}_border.png";
                    if (!File.Exists(animatedBorderPath)) break;
                    var frameTex = Raylib.LoadTexture(animatedBorderPath);
                    if (frameTex.Id == 0) break;
                    frames.Add(frameTex);
                }

                if (frames.Count > 0)
                {
                    _biomeBorderAnimationFrames[biomeGroundId] = frames;
                    loaded = true;
                }
            }

            return loaded;
        }

		// Obtenir la texture de bordure pour un biome
        public static Texture2D GetBiomeBorderTexture(int groundId)
		{
			return _biomeBorderTextures.GetValueOrDefault(groundId);
		}

		public static Texture2D GetBiomeBorderTexture(int groundId, int tileX, int tileY, float currentTime)
        {
            if (_biomeBorderAnimationFrames.TryGetValue(groundId, out var frames) && frames.Count > 0)
            {
                int frameIndex = GetAnimationFrameForTile(tileX, tileY, frames.Count, currentTime);
                return frames[frameIndex];
            }
            return GetBiomeBorderTexture(groundId);
        }

		// Vérifier si un biome a une texture de bordure
        public static bool HasBiomeBorder(int groundId)
        {
            return _biomeBorderTextures.ContainsKey(groundId) || _biomeBorderAnimationFrames.ContainsKey(groundId);
        }

		/// <summary>
        /// Calcule (et met en cache) température/humidité/élévation pour une tuile donnée.
        /// Avant cette méthode, GetHeightAt / GetBiomeAt / GetGroundTileIdAt recalculaient
        /// chacun ces valeurs indépendamment (et GetHumidity recalculait elle-même la
        /// température et l'élévation en interne) : jusqu'à 5-6 évaluations complètes du
        /// bruit de terrain pour une seule tuile. C'est la cause principale des ralentissements
        /// au chargement des chunks. Désormais chaque tuile n'est calculée qu'une fois.
        /// </summary>
        private static (float temperature, float humidity, float elevation) GetClimateAt(int x, int y)
        {
            EnsureTerrainCachesMatchSeed();

            if (_climateCache.TryGetValue((x, y), out var cached))
                return cached;

            // L'élévation est le bruit le plus coûteux (warp + 3 FBM + rivières) : on la calcule
            // UNE fois et on la passe à GetTemperature. Avant, GetTemperature(x, y) la recalculait
            // en interne, puis GetElevation(x, y) la recalculait encore juste après (2 fois / tuile).
            float elevation = WorldGeneration.GetElevation(x, y);
            float temperature = WorldGeneration.GetTemperature(x, y, elevation);
            float humidity = WorldGeneration.GetHumidity(x, y, temperature, elevation);

            // Borne mémoire : ces caches n'étaient jamais purgés et grossissaient à chaque tuile
            // explorée (plusieurs centaines de Mo en longue partie). Les valeurs sont déterministes
            // (même seed => même résultat) : vider le cache ne coûte qu'un recalcul.
            if (_climateCache.Count >= MAX_TERRAIN_CACHE_ENTRIES)
                _climateCache.Clear();

            var result = (temperature, humidity, elevation);
            _climateCache[(x, y)] = result;
            return result;
        }

        private const int MAX_TERRAIN_CACHE_ENTRIES = 1_000_000;
        private static int _terrainCacheSeed = int.MinValue;

        /// <summary>
        /// Les caches climat/hauteur ne dépendent que de la seed du bruit. Si elle change
        /// (nouveau monde, chargement d'une sauvegarde, retour au menu qui remet la seed à 0...),
        /// ils doivent être vidés : sinon les tuiles déjà visitées gardaient le terrain de
        /// l'ANCIEN monde et se mélangeaient aux nouvelles.
        /// </summary>
        private static void EnsureTerrainCachesMatchSeed()
        {
            int seed = PerlinNoise.Seed;
            if (seed == _terrainCacheSeed)
                return;

            _climateCache.Clear();
            _heightCache.Clear();
            _terrainCacheSeed = seed;
        }

		public static int GetHeightAt(int tileX, int tileY)
		{
			// Sécurité : coordonnées extrêmes
			if (tileX == int.MinValue || tileX == int.MaxValue || tileY == int.MinValue || tileY == int.MaxValue)
				return 0;

			if (_isUnderground)
				return 0;
			
			int chunkX = tileX / CHUNK_SIZE;
			int chunkY = tileY / CHUNK_SIZE;
			if (tileX < 0) chunkX = (tileX - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (tileY < 0) chunkY = (tileY - CHUNK_SIZE + 1) / CHUNK_SIZE;
			
			if (_chunks.TryGetValue((chunkX, chunkY), out var chunk))
				if (chunk.Heights.TryGetValue((tileX, tileY), out int h))
					return h;
			
			EnsureTerrainCachesMatchSeed();

			if (!_heightCache.TryGetValue((tileX, tileY), out int height))
			{
				var climate = GetClimateAt(tileX, tileY);
				// Étage des hauteurs (0-59)
				height = (int)(climate.elevation * MAX_HEIGHT_LEVELS);
				if (height > MAX_HEIGHT_LEVELS - 1) height = MAX_HEIGHT_LEVELS - 1;
				if (_heightCache.Count >= MAX_TERRAIN_CACHE_ENTRIES)
					_heightCache.Clear();
				_heightCache[(tileX, tileY)] = height;
			}
			return height;
		}

		// ─────────────────────────────────────────────────────────────────────────
        //  BIOMES ET SOL
        // ─────────────────────────────────────────────────────────────────────────
        public static Biome GetBiomeAt(float x, float y)
		{
			if (_isUnderground)
				return GetCaveBiomeAt(x, y);
			
			var climate = GetClimateAt((int)Math.Floor(x), (int)Math.Floor(y));
			
			return WorldGeneration.GetBiome(climate.temperature, climate.humidity, climate.elevation);
		}

		private static Biome GetCaveBiomeAt(float x, float y)
		{
            if (!_isInDungeon)
                return Biome.Plains;

			float caveNoise = GetCaveNoise(x, y);
			float cavityNoise = GetCavityNoise(x, y);
			int depth = (int)(caveNoise * 8f);
			
			if (caveNoise > 0.7f && cavityNoise > 0.6f)
				return Biome.Mesa; // Lave
			if (caveNoise > 0.5f && caveNoise < 0.65f && cavityNoise < 0.3f)
				return Biome.Beach; // Cristal
			if (cavityNoise > 0.6f && depth > 3)
				return Biome.Swamp; // Champignon
			if (depth > 6 && cavityNoise < 0.2f)
				return Biome.IceSpikes; // Abysse
			return Biome.Plains; // Cave normale
		}

		private static int GetBiomePriority(int groundId)
        {
            return _biomePriority.GetValueOrDefault(groundId, 0);
        }

		// ─────────────────────────────────────────────────────────────────────────
        //  SPAWN DES OBJETS
        // ─────────────────────────────────────────────────────────────────────────
        private class SpawnableObject
        {
            public int Id;
            public float Weight;
            public string[] Biomes = Array.Empty<string>();
            public int RequiredGroundId;
            public bool IsCaveOnly = false;
        }

		// Outline helpers
        private static List<Vector2> GetOrGenerateOutline(Texture2D tex)
        {
            int texId = (int)tex.Id;
            if (_outlineCache.TryGetValue(texId, out var cachedOutline))
                return cachedOutline;

            var outlinePoints = new List<Vector2>();
            Image img = Raylib.LoadImageFromTexture(tex);
            int width = img.Width;
            int height = img.Height;
            var occupiedPixels = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    occupiedPixels[y * width + x] = Raylib.GetImageColor(img, x, y).A > 8;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (occupiedPixels[y * width + x])
                        continue;

                    bool adjacentToOpaquePixel = false;
                    for (int offsetY = -1; offsetY <= 1 && !adjacentToOpaquePixel; offsetY++)
                    {
                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            if (offsetX == 0 && offsetY == 0)
                                continue;

                            int neighborX = x + offsetX;
                            int neighborY = y + offsetY;
                            if (neighborX >= 0 && neighborX < width
                                && neighborY >= 0 && neighborY < height
                                && occupiedPixels[neighborY * width + neighborX])
                            {
                                adjacentToOpaquePixel = true;
                                break;
                            }
                        }
                    }

                    if (adjacentToOpaquePixel)
                        outlinePoints.Add(new Vector2(x, y));
                }
            }

            Raylib.UnloadImage(img);
            _outlineCache[texId] = outlinePoints;
            return outlinePoints;
        }

		private static void DrawCachedOutline(Texture2D tex, Rectangle destRect, Vector2 origin, float rotation, Color outlineColor)
        {
            var outlinePoints = GetOrGenerateOutline(tex);
            if (outlinePoints.Count == 0) return;
            float scaleX = destRect.Width / tex.Width;
            float scaleY = destRect.Height / tex.Height;
            float pivotX = destRect.X;
            float pivotY = destRect.Y;
            foreach (var point in outlinePoints)
            {
                float px = destRect.X + point.X * scaleX;
                float py = destRect.Y + point.Y * scaleY;
                Raylib.DrawRectanglePro(
                    new Rectangle(pivotX, pivotY, scaleX, scaleY),
                    new Vector2(origin.X - point.X * scaleX, origin.Y - point.Y * scaleY),
                    rotation,
                    outlineColor);
            }
        }

        //  Utilisé par QuestSystem : ne renvoie que les espèces du biome ayant une nourriture
        // préférée configurée, comme l'exige le taming du joueur.
        public static string[] GetTamableSpeciesForPosition(float worldX, float worldY)
        {
            return GetSpeciesForBiome(GetBiomeAt(worldX, worldY))
                .Where(species => (SpeciesData.GetSpeciesInfo(species)?.PreferredFoodId ?? 0) > 0)
                .ToArray();
        }

		private static string[] GetSpeciesForBiome(Biome biome)
		{
			if (_isUnderground)
			{
				return biome switch
				{
					Biome.Mesa => new[] { "bat", "spider" },
					Biome.Beach => new[] { "bat", "spider" },
                    Biome.Swamp => new[] { "bat", "spider", "skeleton", "slime" },
                    Biome.IceSpikes => new[] { "bat", "skeleton", "slime" },
                    _ => new[] { "bat", "spider", "slime" }
                };
            }

            return biome switch
            {
                Biome.Desert => new[] { "chicken", "sheep", "hyena", "scorpio", "slime", "insect" },
                Biome.Savanna => new[] { "pig", "sheep", "chicken", "hyena" },
                Biome.Jungle => new[] { "pig", "wolf", "chicken", "duck" },
                Biome.Swamp => new[] { "pig", "chicken", "duck" },
                Biome.Plains => new[] { "pig", "sheep", "cow", "chicken", "slime", "insect" },
                Biome.Forest => new[] { "pig", "wolf", "bear", "chicken", "duck" },
                Biome.DarkForest => new[] { "wolf", "bear", "spider" },
                Biome.BirchForest => new[] { "pig", "wolf", "chicken" },
                Biome.Tundra => new[] { "penguin", "wolf", "sheep", "slime" },
                Biome.Steppe => new[] { "horse", "sheep" },
                Biome.Taiga => new[] { "wolf", "bear", "fox" },
                Biome.SnowyForest => new[] { "penguin", "wolf", "bear", "slime" },
                Biome.Snow => new[] { "penguin", "wolf", "slime" },
                Biome.IceSpikes => new[] { "penguin", "wolf", "slime" },
                Biome.Mountains => new[] { "goat", "wolf", "bear" },
                Biome.Peak => new[] { "goat" },
                Biome.Beach => new[] { "turtle", "crab", "chicken", "duck" },
                _ => new[] { "pig", "sheep", "chicken" }
            };
        }

        private static string[] GetFlyingInsectsForBiome(Biome biome)
        {
            if (_isUnderground)
                return Array.Empty<string>();

            return biome switch
            {
                Biome.Plains => new[] { "butterfly", "dragonfly" },
                Biome.Forest => new[] { "butterfly", "dragonfly" },
                Biome.BirchForest => new[] { "butterfly", "dragonfly" },
                Biome.DarkForest => new[] { "butterfly", "dragonfly", "willowisp" },
                Biome.Swamp => new[] { "dragonfly", "willowisp" },
                Biome.Beach => new[] { "dragonfly" },
                Biome.Savanna => new[] { "dragonfly" },
                Biome.Jungle => new[] { "dragonfly" },
                Biome.Steppe => new[] { "dragonfly" },
                _ => Array.Empty<string>()
            };
        }

        private static void DrawCliffBorder(int x, int y, int directionX, int directionY, int ts, Texture2D cliffBorderTex)
        {
            if (cliffBorderTex.Id == 0) return;
            int baseX = x * ts;
            int baseY = y * ts - (GetHeightAt(x, y) * ts / 4);
            Rectangle src = new Rectangle(0, 0, cliffBorderTex.Width, cliffBorderTex.Height);
            Rectangle dest;
            float rotation = 0;
            Vector2 origin = Vector2.Zero;
            if (directionX == -1 && directionY == 0)
            {
                dest = new Rectangle(baseX, baseY, ts / 4, ts);
                rotation = 0;
                origin = Vector2.Zero;
            }
            else if (directionX == 1 && directionY == 0)
            {
                dest = new Rectangle(baseX + ts - ts / 4, baseY, ts / 4, ts);
                rotation = 180;
                origin = new Vector2(dest.Width, dest.Height);
            }
            else if (directionX == 0 && directionY == -1)
            {
                dest = new Rectangle(baseX, baseY, ts / 4, ts);
                rotation = 90;
                origin = new Vector2(0, dest.Height);
            }
            else if (directionX == 0 && directionY == 1)
            {
                dest = new Rectangle(baseX, baseY + ts - ts / 4, ts / 4, ts);
                rotation = -90;
                origin = new Vector2(dest.Width, 0);
            }
            else
            {
                return;
            }
            Raylib.DrawTexturePro(cliffBorderTex, src, dest, origin, rotation, Color.White);
        }

    }
}