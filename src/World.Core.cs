// World.Core.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {
		private static bool TryPlaceVillageSpiritTotem(int villageX, int villageY, Random random)
		{
			for (int attempt = 0; attempt < 48; attempt++)
			{
				int x = villageX + random.Next(-10, 11);
				int y = villageY + random.Next(-10, 11);
				int distanceSquared = (x - villageX) * (x - villageX) + (y - villageY) * (y - villageY);
				if (distanceSquared < 16 || distanceSquared > 100
					|| _villageClaimedTiles.Contains((x, y)) || _villageClaimedTiles.Contains((x + 1, y)))
					continue;

				EnsureTileLoaded(x, y);
				EnsureTileLoaded(x + 1, y);
				var totemChunk = GetChunkAt(x, y);
				var spiritChunk = GetChunkAt(x + 1, y);
				if (totemChunk == null || spiritChunk == null
					|| GetObjectIdAt(x, y) != 0 || GetObjectIdAt(x + 1, y) != 0)
					continue;

				var groundTile = WorldTileRegistry.GetTile(GetGroundTileIdAt(x, y));
				if (groundTile == null || !groundTile.Diggable)
					continue;

				totemChunk.Objects[(x, y)] = 88;
				var spirit = new Entity(new Vector2((x + 1.5f) * Program.TileSize, (y + 0.5f) * Program.TileSize), "Wolf", false)
				{
					IsSpiritAnimal = true,
					SpiritTotemTileX = x,
					SpiritTotemTileY = y,
					Behavior = "passive",
					Tint = new Color(165, 225, 255, 210)
				};
				spiritChunk.Entities.Add(spirit);
				var globalEntities = Program.GetEntities();
				if (!globalEntities.Any(entity => entity.NetId == spirit.NetId))
					globalEntities.Add(spirit);
				return true;
			}

			return false;
		}

		//  PERF : table précalculée (biomeName, groundId, isUnderground) → objets possibles,
		// pour éviter de refaire un scan LINQ .Where(...).ToList() de toute la liste
		// _spawnableObjects à CHAQUE tuile candidate lors de la génération d'un chunk
		// (jusqu'à 20 fois par chunk en forêt dense). Construite une seule fois, au
		// premier accès, puis simplement consultée.
		private static Dictionary<(string biome, int groundId, bool underground), List<SpawnableObject>>? _spawnableObjectsCache;

		private static List<SpawnableObject> GetPossibleSpawnableObjects(string biomeName, int groundId, bool underground)
		{
			if (_spawnableObjectsCache == null)
			{
				var cache = new Dictionary<(string, int, bool), List<SpawnableObject>>();
				foreach (var obj in _spawnableObjects)
				{
					bool caveEligible = obj.IsCaveOnly || obj.Id >= 200;
					foreach (var b in obj.Biomes)
					{
						// underground = true → seulement les objets éligibles grotte
						// underground = false → seulement les objets non réservés aux grottes
						foreach (bool u in new[] { true, false })
						{
							bool matchesUnderground = u ? caveEligible : !obj.IsCaveOnly;
							if (!matchesUnderground) continue;
							var key = (b, obj.RequiredGroundId, u);
							if (!cache.TryGetValue(key, out var list))
								cache[key] = list = new List<SpawnableObject>();
							list.Add(obj);
						}
					}
				}
				_spawnableObjectsCache = cache;
			}
			return _spawnableObjectsCache.TryGetValue((biomeName, groundId, underground), out var possible)
				? possible
				: EmptySpawnableObjects;
		}

		private static readonly List<SpawnableObject> EmptySpawnableObjects = new();

		private static List<SpawnableObject> _spawnableObjects = new()
		{
			// Désert (ID 11)
			new SpawnableObject { Id = 38, Weight = 30, Biomes = new[] { "Desert" }, RequiredGroundId = 11 },
			new SpawnableObject { Id = 19, Weight = 25, Biomes = new[] { "Desert" }, RequiredGroundId = 11 },
			new SpawnableObject { Id = 2, Weight = 15, Biomes = new[] { "Desert" }, RequiredGroundId = 11 },
			new SpawnableObject { Id = 49, Weight = 10, Biomes = new[] { "Desert" }, RequiredGroundId = 11 },
			new SpawnableObject { Id = 15, Weight = 12, Biomes = new[] { "Desert" }, RequiredGroundId = 11 },
			
			// Plage (ID 62)
			new SpawnableObject { Id = 38, Weight = 30, Biomes = new[] { "Beach" }, RequiredGroundId = 62 },
			new SpawnableObject { Id = 19, Weight = 25, Biomes = new[] { "Beach" }, RequiredGroundId = 62 },
			new SpawnableObject { Id = 2, Weight = 15, Biomes = new[] { "Beach" }, RequiredGroundId = 62 },
			new SpawnableObject { Id = 49, Weight = 10, Biomes = new[] { "Beach" }, RequiredGroundId = 62 },
			new SpawnableObject { Id = 15, Weight = 12, Biomes = new[] { "Beach" }, RequiredGroundId = 62 },
			
			// Savane (ID 46)
			new SpawnableObject { Id = 4, Weight = 25, Biomes = new[] { "Savanna" }, RequiredGroundId = 46 },
			new SpawnableObject { Id = 38, Weight = 25, Biomes = new[] { "Savanna" }, RequiredGroundId = 46 },
			new SpawnableObject { Id = 19, Weight = 20, Biomes = new[] { "Savanna" }, RequiredGroundId = 46 },
			new SpawnableObject { Id = 2, Weight = 15, Biomes = new[] { "Savanna" }, RequiredGroundId = 46 },
			
			// Jungle (ID 44)
			new SpawnableObject { Id = 4, Weight = 35, Biomes = new[] { "Jungle" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 48, Weight = 30, Biomes = new[] { "Jungle" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 13, Weight = 15, Biomes = new[] { "Jungle" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 14, Weight = 15, Biomes = new[] { "Jungle" }, RequiredGroundId = 44 },
			
			// Herbe (Tall Grass) – apparaît sur les différents sols herbeux
			new SpawnableObject { Id = 16, Weight = 30, Biomes = new[] { "Plains", "Forest", "BirchForest", "DarkForest" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 16, Weight = 25, Biomes = new[] { "Forest", "DarkForest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 16, Weight = 20, Biomes = new[] { "Savanna" }, RequiredGroundId = 46 },
			new SpawnableObject { Id = 16, Weight = 20, Biomes = new[] { "Taiga" }, RequiredGroundId = 47 },
			new SpawnableObject { Id = 16, Weight = 15, Biomes = new[] { "Swamp" }, RequiredGroundId = 53 },
			
			// Cailloux (Pebble) – apparaissent sur les sols herbeux, sable, etc.
			new SpawnableObject { Id = 17, Weight = 20, Biomes = new[] { "Plains", "Forest", "BirchForest", "DarkForest", "Savanna" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 17, Weight = 15, Biomes = new[] { "Forest", "DarkForest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 17, Weight = 15, Biomes = new[] { "Savanna" }, RequiredGroundId = 46 },
			new SpawnableObject { Id = 17, Weight = 20, Biomes = new[] { "Desert" }, RequiredGroundId = 11 },
			new SpawnableObject { Id = 17, Weight = 20, Biomes = new[] { "Beach" }, RequiredGroundId = 62 },
			new SpawnableObject { Id = 17, Weight = 10, Biomes = new[] { "Taiga" }, RequiredGroundId = 47 },
			new SpawnableObject { Id = 17, Weight = 10, Biomes = new[] { "Swamp" }, RequiredGroundId = 53 },
			new SpawnableObject { Id = 17, Weight = 15, Biomes = new[] { "Mountains", "Peak" }, RequiredGroundId = 26 },
			
			// Neige / Toundra / IceSpikes (ID 43)
			new SpawnableObject { Id = 45, Weight = 40, Biomes = new[] { "Snow", "Tundra", "IceSpikes", "SnowyForest" }, RequiredGroundId = 43 },  // Sapin
			new SpawnableObject { Id = 19, Weight = 25, Biomes = new[] { "Snow", "Tundra", "IceSpikes", "SnowyForest" }, RequiredGroundId = 43 },  // Rocher
			new SpawnableObject { Id = 12, Weight = 2, Biomes = new[] { "Snow", "Tundra", "IceSpikes", "SnowyForest" }, RequiredGroundId = 43 },   // Statue moai (rare)
			new SpawnableObject { Id = 15, Weight = 15, Biomes = new[] { "Snow", "Tundra", "IceSpikes", "SnowyForest" }, RequiredGroundId = 43 },  // Champignon
			new SpawnableObject { Id = 30, Weight = 12, Biomes = new[] { "Snow", "Tundra", "IceSpikes", "SnowyForest" }, RequiredGroundId = 43 },  // Citrouille
			new SpawnableObject { Id = 4, Weight = 8, Biomes = new[] { "Snow", "Tundra", "IceSpikes", "SnowyForest" }, RequiredGroundId = 43 },   // Arbre (rare)
			
			// Forêt (ID 44)
			new SpawnableObject { Id = 45, Weight = 55, Biomes = new[] { "Forest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 4, Weight = 25, Biomes = new[] { "Forest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 48, Weight = 20, Biomes = new[] { "Forest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 2, Weight = 12, Biomes = new[] { "Forest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 13, Weight = 10, Biomes = new[] { "Forest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 45, Weight = 55, Biomes = new[] { "Forest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 4, Weight = 25, Biomes = new[] { "Forest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 48, Weight = 20, Biomes = new[] { "Forest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 2, Weight = 12, Biomes = new[] { "Forest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 15, Weight = 18, Biomes = new[] { "Forest", "DarkForest" }, RequiredGroundId = 44 },  // Champignon
			
			// Forêt de bouleau (ID 1)
			new SpawnableObject { Id = 4, Weight = 30, Biomes = new[] { "BirchForest" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 48, Weight = 25, Biomes = new[] { "BirchForest" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 2, Weight = 20, Biomes = new[] { "BirchForest" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 13, Weight = 15, Biomes = new[] { "BirchForest" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 15, Weight = 16, Biomes = new[] { "BirchForest" }, RequiredGroundId = 1 },  // Champignon
			
			// Forêt sombre (ID 44)
			new SpawnableObject { Id = 45, Weight = 55, Biomes = new[] { "DarkForest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 4, Weight = 25, Biomes = new[] { "DarkForest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 48, Weight = 20, Biomes = new[] { "DarkForest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 2, Weight = 12, Biomes = new[] { "DarkForest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 15, Weight = 10, Biomes = new[] { "DarkForest" }, RequiredGroundId = 44 },  // Champignon
			new SpawnableObject { Id = 45, Weight = 55, Biomes = new[] { "DarkForest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 4, Weight = 25, Biomes = new[] { "DarkForest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 48, Weight = 20, Biomes = new[] { "DarkForest" }, RequiredGroundId = 44 },
			new SpawnableObject { Id = 2, Weight = 12, Biomes = new[] { "DarkForest" }, RequiredGroundId = 44 },
			
			// Plaines (ID 1)
			new SpawnableObject { Id = 2, Weight = 20, Biomes = new[] { "Plains" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 4, Weight = 10, Biomes = new[] { "Plains" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 5, Weight = 15, Biomes = new[] { "Plains" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 13, Weight = 15, Biomes = new[] { "Plains" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 14, Weight = 15, Biomes = new[] { "Plains" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 19, Weight = 10, Biomes = new[] { "Plains" }, RequiredGroundId = 1 },
			new SpawnableObject { Id = 5009, Weight = 10, Biomes = new[] { "Plains" }, RequiredGroundId = 1 },
			
			// Taïga (ID 47)
			new SpawnableObject { Id = 45, Weight = 35, Biomes = new[] { "Taiga" }, RequiredGroundId = 47 },
			new SpawnableObject { Id = 4, Weight = 20, Biomes = new[] { "Taiga" }, RequiredGroundId = 47 },
			new SpawnableObject { Id = 15, Weight = 15, Biomes = new[] { "Taiga" }, RequiredGroundId = 47 },
			
			// Toundra (ID 43)
			new SpawnableObject { Id = 4, Weight = 25, Biomes = new[] { "Tundra" }, RequiredGroundId = 43 },
			new SpawnableObject { Id = 15, Weight = 20, Biomes = new[] { "Tundra" }, RequiredGroundId = 43 },
			
			// Marais (ID 44)
			new SpawnableObject { Id = 19, Weight = 25, Biomes = new[] { "Swamp" }, RequiredGroundId = 53 },
			new SpawnableObject { Id = 2, Weight = 20, Biomes = new[] { "Swamp" }, RequiredGroundId = 53 },
			new SpawnableObject { Id = 20, Weight = 20, Biomes = new[] { "Swamp" }, RequiredGroundId = 53 },
			
			// Montagnes (ID 26)
			new SpawnableObject { Id = 19, Weight = 30, Biomes = new[] { "Mountains" }, RequiredGroundId = 26 },
			new SpawnableObject { Id = 24, Weight = 25, Biomes = new[] { "Mountains" }, RequiredGroundId = 26 },
			new SpawnableObject { Id = 2, Weight = 15, Biomes = new[] { "Mountains" }, RequiredGroundId = 26 },
			new SpawnableObject { Id = 4, Weight = 10, Biomes = new[] { "Mountains" }, RequiredGroundId = 26 },
		};

		public static bool TryCreateFieldNear(Vector2 homePosition)
		{
			int homeTileX = (int)MathF.Floor(homePosition.X / Program.TileSize);
			int homeTileY = (int)MathF.Floor(homePosition.Y / Program.TileSize);
			var homeRect = (homeTileX - 3, homeTileY - 3, 6, 6);
			return TryPlaceFieldZone(homeTileX, homeTileY, Random.Shared, new List<(int x, int y, int w, int h)> { homeRect }, tillGround: false);
		}

		private static bool TryPlaceFieldZone(int centralX, int centralY, Random rand, List<(int x, int y, int w, int h)> reservedRects, bool tillGround = true)
		{
			const int fieldW = 10, fieldH = 8;

			//  Un village peut désormais s'étendre sur plusieurs centaines de tuiles
			// (10 à 50 habitants, voir GenerateNewChunk). Chercher le champ dans un
			// anneau fixe de 90 à 350 tuiles autour du CENTRE ne fonctionne que pour
			// les petits villages : pour les plus grands, cette zone est presque
			// toujours entièrement couverte par des maisons (reservedRects), donc
			// les 80 tentatives échouent systématiquement et AUCUN champ n'est créé.
			// On part donc de la bordure réelle du bâti déjà placé.
			double builtRadius = 90;
			foreach (var r in reservedRects)
			{
				double rcx = r.x + r.w / 2.0;
				double rcy = r.y + r.h / 2.0;
				double d = Math.Sqrt((rcx - centralX) * (rcx - centralX) + (rcy - centralY) * (rcy - centralY)) + Math.Max(r.w, r.h);
				if (d > builtRadius) builtRadius = d;
			}

			int rejectedOverlap = 0, rejectedTerrain = 0;
			int minX = reservedRects.Min(r => r.x);
			int maxX = reservedRects.Max(r => r.x + r.w);
			int minY = reservedRects.Min(r => r.y);
			int maxY = reservedRects.Max(r => r.y + r.h);
			int villageSpan = Math.Max(maxX - minX, maxY - minY);
			int fieldGapMin = 10;
			int fieldGapMax = Math.Max(12, Math.Min(28, villageSpan / 8));
			int lateralSpread = Math.Max(14, Math.Min(40, villageSpan / 4));

			// Les champs étaient placés à +12..+70 tuiles au-delà du bord du village,
			// ce qui produit de gros espaces vides pour les villages étendus. On les garde
			// juste à la périphérie du bâti, proportionnellement à la taille du village.
			var sideOrder = new[]
			{
				"south",
				"east",
				"west",
				"north"
			};

			foreach (var side in sideOrder)
			{
				for (int attempt = 0; attempt < 60; attempt++)
				{
					int posX = centralX;
					int posY = centralY;

					switch (side)
					{
						case "south":
							posX = Math.Clamp(centralX + rand.Next(-lateralSpread, lateralSpread + 1) - fieldW / 2, minX - 20, maxX + 20);
							posY = maxY + fieldGapMin + rand.Next(0, Math.Max(4, fieldGapMax - fieldGapMin + 1));
							break;
						case "north":
							posX = Math.Clamp(centralX + rand.Next(-lateralSpread, lateralSpread + 1) - fieldW / 2, minX - 20, maxX + 20);
							posY = minY - fieldGapMin - rand.Next(0, Math.Max(4, fieldGapMax - fieldGapMin + 1));
							break;
					case "east":
							posX = maxX + fieldGapMin + rand.Next(0, Math.Max(4, fieldGapMax - fieldGapMin + 1));
							posY = Math.Clamp(centralY + rand.Next(-lateralSpread, lateralSpread + 1) - fieldH / 2, minY - 20, maxY + 20);
							break;
					case "west":
							posX = minX - fieldGapMin - fieldW - rand.Next(0, Math.Max(4, fieldGapMax - fieldGapMin + 1));
							posY = Math.Clamp(centralY + rand.Next(-lateralSpread, lateralSpread + 1) - fieldH / 2, minY - 20, maxY + 20);
							break;
					}
					bool overlap = reservedRects.Any(r =>
						!(posX + fieldW < r.x || posX > r.x + r.w || posY + fieldH < r.y || posY > r.y + r.h));
					if (overlap) { rejectedOverlap++; continue; }

					bool ok = true;
					bool sawGoodGround = false;
					int availableTiles = 0;
					for (int dx = 0; dx < fieldW && ok; dx++)
						for (int dy = 0; dy < fieldH && ok; dy++)
						{
							int groundId = GetGroundTileIdAt(posX + dx, posY + dy);
							var groundTile = WorldTileRegistry.GetTile(groundId);
							if (groundTile == null || !groundTile.Diggable)
							{
								if (tillGround) ok = false;
								continue;
							}
							if (GetObjectIdAt(posX + dx, posY + dy) != 0)
							{
								if (tillGround) ok = false;
								continue;
							}
							availableTiles++;
							if (GetBiomeAt(posX + dx, posY + dy) == Biome.Plains)
								sawGoodGround = true;
						}

					if (!ok || availableTiles == 0 || (tillGround && !sawGoodGround && !IsAreaMostlyDiggable(posX, posY, fieldW, fieldH)))
					{
						rejectedTerrain++;
						continue;
					}

					if (tillGround)
					{
						for (int dx = 0; dx < fieldW; dx++)
							for (int dy = 0; dy < fieldH; dy++)
							{
								int wx = posX + dx;
								int wy = posY + dy;
								EnsureTileLoaded(wx, wy);
								SetGroundTile(wx, wy, 27);
								if ((dx + dy) % 3 == 0 && (dx % 2 == 0 || dy % 2 == 0))
									SetGroundTile(wx, wy, 29);
							}
					}

					var zone = new VillageWorkZone
					{
						Id = _nextWorkZoneId++,
						Kind = WorkZoneKind.Field,
						AnchorPos = new Vector2((posX + fieldW / 2f) * Program.TileSize, (posY + fieldH / 2f) * Program.TileSize),
						Radius = fieldW * Program.TileSize * 0.5f,
						MaxWorkers = 4,
					};
					PopulateWorkSlots(zone, rand);
					WorkZones[zone.Id] = zone;
					reservedRects.Add((posX, posY, fieldW, fieldH));
					Console.WriteLine($"[Farming] {(tillGround ? "Champ labouré" : "Zone de champ réservée")} en ({posX},{posY}), zone id={zone.Id}, {zone.Slots.Count} emplacements, côté={side}, après {attempt + 1} essai(s).");
					return true;
				}
			}

			Console.WriteLine($"[Farming] ÉCHEC placement du champ en ({centralX},{centralY}) : {rejectedOverlap} rejets pour chevauchement, {rejectedTerrain} rejets pour terrain non labourable, builtRadius={builtRadius:F0}. Aucune VillageWorkZone de type Field créée.");
			return false;
		}

		private static bool IsAreaMostlyDiggable(int x, int y, int w, int h)
		{
			int valid = 0;
			int checkedTiles = 0;
			for (int dx = 0; dx < w; dx++)
			{
				for (int dy = 0; dy < h; dy++)
				{
					checkedTiles++;
					int groundId = GetGroundTileIdAt(x + dx, y + dy);
					var groundTile = WorldTileRegistry.GetTile(groundId);
					if (groundTile != null && groundTile.Diggable)
						valid++;
				}
			}
			return checkedTiles > 0 && (float)valid / checkedTiles >= 0.7f;
		}

		private static bool IsCampTileFree(ChunkData chunkData, int x, int y)
		{
			if (chunkData.Objects.ContainsKey((x, y))) return false;
			int groundId = GetCaveGroundTileIdAt(x, y);
			var groundTile = WorldTileRegistry.GetTile(groundId);
			return groundTile != null && groundTile.Walkable;
		}

		private static bool TryPlaceCaveTentCamp(ChunkData chunkData, int chunkX, int chunkY, Random rand)
		{
			int minX = chunkX * CHUNK_SIZE + 2;
			int maxX = (chunkX + 1) * CHUNK_SIZE - 3;
			int minY = chunkY * CHUNK_SIZE + 2;
			int maxY = (chunkY + 1) * CHUNK_SIZE - 3;
			int maxAttempts = 40;

			for (int attempt = 0; attempt < maxAttempts; attempt++)
			{
				int centerX = rand.Next(minX + 3, maxX - 2);
				int centerY = rand.Next(minY + 3, maxY - 2);
				int campRadius = rand.Next(7, 13);
				int tentCount = rand.Next(3, 7);
				HashSet<(int x, int y)> tentPositions = new();

				for (int i = 0; i < tentCount; i++)
				{
					int tries = 0;
					bool placed = false;
					while (tries < 50 && !placed)
					{
						int dx = rand.Next(-campRadius, campRadius + 1);
						int dy = rand.Next(-campRadius, campRadius + 1);
						if (dx * dx + dy * dy < 9 || dx * dx + dy * dy > campRadius * campRadius)
						{
							tries++;
							continue;
						}

						int tx = centerX + dx;
						int ty = centerY + dy;
						if (tx < minX || tx > maxX || ty < minY || ty > maxY)
						{
							tries++;
							continue;
						}
						if (!IsCampTileFree(chunkData, tx, ty))
						{
							tries++;
							continue;
						}

						tentPositions.Add((tx, ty));
						placed = true;
					}
					if (!placed)
						return false;
				}

				if (tentPositions.Count == 0)
					continue;

				foreach (var (tx, ty) in tentPositions)
					chunkData.Objects[(tx, ty)] = 6001;
				HashSet<(int x, int y)> claimedNpcTiles = new();

				int fireX = centerX + rand.Next(-2, 3);
				int fireY = centerY + rand.Next(-2, 3);
				if (fireX >= minX && fireX <= maxX && fireY >= minY && fireY <= maxY && IsCampTileFree(chunkData, fireX, fireY))
					chunkData.Objects[(fireX, fireY)] = 32;

				for (int i = 0; i < 2 + rand.Next(3); i++)
				{
					int lx = centerX + rand.Next(-campRadius - 1, campRadius + 2);
					int ly = centerY + rand.Next(-campRadius - 1, campRadius + 2);
					if (lx < minX || lx > maxX || ly < minY || ly > maxY)
						continue;
					if (chunkData.Objects.ContainsKey((lx, ly)))
						continue;
					int groundId = GetCaveGroundTileIdAt(lx, ly);
					var groundTile = WorldTileRegistry.GetTile(groundId);
					if (groundTile == null || !groundTile.Walkable)
						continue;
					chunkData.Objects[(lx, ly)] = rand.NextDouble() < 0.6 ? 33 : 32;
				}

				foreach (var (tentX, tentY) in tentPositions)
				{
					int npcX = tentX;
					int npcY = tentY;
					bool foundSpot = false;
					for (int search = 0; search < 12 && !foundSpot; search++)
					{
						int dx = rand.Next(-2, 3);
						int dy = rand.Next(-2, 3);
						int candidateX = tentX + dx;
						int candidateY = tentY + dy;
						if (candidateX < minX || candidateX > maxX || candidateY < minY || candidateY > maxY)
							continue;
						if (chunkData.Objects.ContainsKey((candidateX, candidateY)))
							continue;
						if (claimedNpcTiles.Contains((candidateX, candidateY)))
							continue;
						int groundId = GetCaveGroundTileIdAt(candidateX, candidateY);
						var groundTile = WorldTileRegistry.GetTile(groundId);
						if (groundTile == null || !groundTile.Walkable)
							continue;
						npcX = candidateX;
						npcY = candidateY;
						foundSpot = true;
					}

					if (!foundSpot)
					{
						int fallbackX = tentX + 1;
						int fallbackY = tentY + 1;
						if (fallbackX < minX || fallbackX > maxX || fallbackY < minY || fallbackY > maxY)
						{
							fallbackX = centerX;
							fallbackY = centerY;
						}
						if (fallbackX >= minX && fallbackX <= maxX && fallbackY >= minY && fallbackY <= maxY)
						{
							int groundId = GetCaveGroundTileIdAt(fallbackX, fallbackY);
							var groundTile = WorldTileRegistry.GetTile(groundId);
							if (groundTile != null && groundTile.Walkable && !chunkData.Objects.ContainsKey((fallbackX, fallbackY)))
							{
								npcX = fallbackX;
								npcY = fallbackY;
								foundSpot = true;
							}
						}
					}

					if (!foundSpot)
						continue;

					int homeX = npcX;
					int homeY = npcY;
					float bestHomeDistanceSq = float.MaxValue;
					for (int offsetX = -3; offsetX <= 3; offsetX++)
					{
						for (int offsetY = -3; offsetY <= 3; offsetY++)
						{
							int candidateX = tentX + offsetX;
							int candidateY = tentY + offsetY;
							if (candidateX < minX || candidateX > maxX || candidateY < minY || candidateY > maxY)
								continue;
							if ((candidateX == npcX && candidateY == npcY)
								|| chunkData.Objects.ContainsKey((candidateX, candidateY))
								|| claimedNpcTiles.Contains((candidateX, candidateY)))
								continue;

							int groundId = GetCaveGroundTileIdAt(candidateX, candidateY);
							var groundTile = WorldTileRegistry.GetTile(groundId);
							if (groundTile == null || !groundTile.Walkable)
								continue;

							float distanceSq = offsetX * offsetX + offsetY * offsetY;
							if (distanceSq >= bestHomeDistanceSq)
								continue;
							bestHomeDistanceSq = distanceSq;
							homeX = candidateX;
							homeY = candidateY;
						}
					}
					claimedNpcTiles.Add((npcX, npcY));
					claimedNpcTiles.Add((homeX, homeY));

					float worldX = npcX * Program.TileSize + 8f;
					float worldY = npcY * Program.TileSize + 8f;
					var tentNpc = new Entity(new Vector2(worldX, worldY), "human", false);
					tentNpc.HomePosition = new Vector2(homeX * Program.TileSize + Program.TileSize / 2f, homeY * Program.TileSize + Program.TileSize / 2f);
					tentNpc.TentPosition = new Vector2(tentX * Program.TileSize + Program.TileSize / 2f, tentY * Program.TileSize + Program.TileSize / 2f);
					tentNpc.HomeBuildingId = Entity.TentHomeBuildingId;
					chunkData.Entities.Add(tentNpc);
				}

				return true;
			}

			return false;
		}

		private static void TryPlaceBiomeWorkZone(WorkZoneKind kind, Biome targetBiome, int centralX, int centralY, Random rand, int markerObjectId)
		{
			double radius = 60.0;
			const double step = 30.0;
			const double maxRadius = 500.0;

			while (radius <= maxRadius)
			{
				int samples = 16;
				for (int s = 0; s < samples; s++)
				{
					double angle = rand.NextDouble() * Math.PI * 2;
					int x = centralX + (int)(Math.Cos(angle) * radius);
					int y = centralY + (int)(Math.Sin(angle) * radius);

					if (GetBiomeAt(x, y) != targetBiome) continue;
					if (GetObjectIdAt(x, y) != 0) continue;
					int groundId = GetGroundTileIdAt(x, y);
					var groundTile = WorldTileRegistry.GetTile(groundId);
					if (groundTile == null || !groundTile.Diggable) continue;

					EnsureTileLoaded(x, y);
					var chunk = GetChunkAt(x, y);
					if (chunk == null) continue;
					chunk.Objects[(x, y)] = markerObjectId;

					var zone = new VillageWorkZone
					{
						Id = _nextWorkZoneId++,
						Kind = kind,
						AnchorPos = new Vector2(x * Program.TileSize, y * Program.TileSize),
						Radius = 220f,
						MaxWorkers = kind == WorkZoneKind.HuntingGround ? 2 : 3,
					};
					PopulateWorkSlots(zone, rand);
					WorkZones[zone.Id] = zone;
					return;
				}
				radius += step;
			}
		}

		private static ChunkData GenerateNewChunk(int chunkX, int chunkY, ChunkData? chunkData = null)
		{
			chunkData ??= new ChunkData();
			int combinedSeed = Program.WorldSeed ^ (chunkX * 73856093) ^ (chunkY * 19349663);
			var chunkRandom = new Random(combinedSeed);

			// 1. Altitudes (Hauteurs)
			for (int localX = 0; localX < CHUNK_SIZE; localX++)
			{
				for (int localY = 0; localY < CHUNK_SIZE; localY++)
				{
					int worldX = chunkX * CHUNK_SIZE + localX;
					int worldY = chunkY * CHUNK_SIZE + localY;
					int height = _isUnderground && !_isInDungeon
						? 0
						: (int)(PerlinNoise.Noise(worldX * 0.02f, worldY * 0.02f) * 3f + 4f);
					height = Math.Clamp(height, 0, 7);
					chunkData.Heights[(worldX, worldY)] = height;
				}
			}

			// 2. GÉNÉRATION DES MURS DE GROTTE (Uniquement en sous-sol)
			if (_isUnderground)
			{
				for (int localX = 0; localX < CHUNK_SIZE; localX++)
				{
					for (int localY = 0; localY < CHUNK_SIZE; localY++)
					{
						int worldX = chunkX * CHUNK_SIZE + localX;
						int worldY = chunkY * CHUNK_SIZE + localY;

						float density = (PerlinNoise.Noise(worldX * 0.03f, worldY * 0.03f) + 1f) / 2f;
						if (density > 0.45f)
						{
							// Par défaut, bloc rocheux. Si la surface au-dessus est désertique, utiliser
							// une version en grès (sandstone_wall) si elle est définie dans le registre.
							int objId = 23; // rock block
							var climate = GetClimateAt(worldX, worldY);
							Biome surfaceBiome = WorldGeneration.GetBiome(climate.temperature, climate.humidity, climate.elevation);
							if (surfaceBiome == Biome.Desert)
							{
								int sandWallId = 0;
								// Try common name variants first (lowercased keys are stored from tile.Name)
								if (!WorldTileRegistry.TileNameToId.TryGetValue("sandstone wall", out sandWallId) &&
									!WorldTileRegistry.TileNameToId.TryGetValue("sandstone_wall", out sandWallId))
								{
									// Fallback: search by texture field in loaded tiles
									foreach (var kv in WorldTileRegistry.Tiles)
									{
										if (kv.Value != null && string.Equals(kv.Value.Texture, "sandstone_wall", StringComparison.OrdinalIgnoreCase))
										{
											sandWallId = kv.Key;
											break;
										}
									}
								}
								if (sandWallId != 0)
									objId = sandWallId;
							}
							chunkData.Objects[(worldX, worldY)] = objId;
						}
					}
				}
			}

			// 3. Génération des minerais (uniquement en sous-sol)
			if (_isUnderground)
			{
				for (int localX = 0; localX < CHUNK_SIZE; localX++)
				{
					for (int localY = 0; localY < CHUNK_SIZE; localY++)
					{
						int worldX = chunkX * CHUNK_SIZE + localX;
						int worldY = chunkY * CHUNK_SIZE + localY;
						if (chunkData.Objects.ContainsKey((worldX, worldY)) && chunkData.Objects[(worldX, worldY)] == 23)
						{
							TryAddMineralOverlay(worldX, worldY, chunkRandom, chunkData);
						}
					}
				}
			}

			// 3.b Création des sorties de grotte pour les entrées situées dans ce chunk (uniquement en sous-sol)
			if (_isUnderground)
			{
				for (int localX = 0; localX < CHUNK_SIZE; localX++)
				{
					for (int localY = 0; localY < CHUNK_SIZE; localY++)
					{
						int wx = chunkX * CHUNK_SIZE + localX;
						int wy = chunkY * CHUNK_SIZE + localY;
						// Vérifier si une entrée de grotte existe à ces coordonnées (en surface)
						if (_caveEntryPairs.TryGetValue((wx, wy), out var pair))
						{
							if (pair.caveX == -1 && pair.caveY == -1)
							{
								CreateCaveExitForEntry(wx, wy, chunkX, chunkY, chunkRandom, chunkData);
							}
						}
					}
				}
			}

			// 3.a Petits campements troglodytes occasionnels : quelques tentes avec torches / feux et PNJ.
			if (_isUnderground && chunkRandom.NextDouble() < 0.06)
			{
				TryPlaceCaveTentCamp(chunkData, chunkX, chunkY, chunkRandom);
			}

			if (!_isUnderground)
			{
				var oasisRandom = new Random(combinedSeed ^ 0x4F415349);
				TryPlaceDesertOasis(chunkX, chunkY, oasisRandom, chunkData);
			}

			// 3. Génération des objets de décoration (arbres, buissons, stalactites...)
			var availablePositions = new List<(int x, int y)>();
			for (int localX = 0; localX < CHUNK_SIZE; localX++)
				for (int localY = 0; localY < CHUNK_SIZE; localY++)
					availablePositions.Add((chunkX * CHUNK_SIZE + localX, chunkY * CHUNK_SIZE + localY));

			for (int i = availablePositions.Count - 1; i > 0; i--)
			{
				int j = chunkRandom.Next(i + 1);
				var temp = availablePositions[i];
				availablePositions[i] = availablePositions[j];
				availablePositions[j] = temp;
			}

			Biome biomeAtChunkCenter = _isUnderground
				? GetCaveBiomeAt(chunkX * CHUNK_SIZE + CHUNK_SIZE / 2, chunkY * CHUNK_SIZE + CHUNK_SIZE / 2)
				: GetBiomeAt(chunkX * CHUNK_SIZE + CHUNK_SIZE / 2, chunkY * CHUNK_SIZE + CHUNK_SIZE / 2);
			bool denseForest = !_isUnderground &&
				(biomeAtChunkCenter == Biome.Forest || biomeAtChunkCenter == Biome.DarkForest);
			int maxObjectsPerChunk = denseForest ? 20 : MAX_OBJECTS_PER_CHUNK;
			float spawnChance = denseForest ? 0.88f : GLOBAL_SPAWN_CHANCE;
			int objectsToPlace = 0;
			for (int i = 0; i < maxObjectsPerChunk && i < availablePositions.Count; i++)
				if (chunkRandom.NextDouble() < spawnChance) objectsToPlace++;

			for (int i = 0; i < objectsToPlace && i < availablePositions.Count; i++)
			{
				var (worldX, worldY) = availablePositions[i];

				if (chunkData.Objects.ContainsKey((worldX, worldY))) continue;
				if (chunkData.GroundOverrides.TryGetValue((worldX, worldY), out int groundOverride) && groundOverride == 10) continue;

				Biome biome;
				int groundId;
				if (_isUnderground)
				{
					biome = GetCaveBiomeAt(worldX, worldY);
					groundId = GetCaveGroundTileIdAt(worldX, worldY);
				}
				else
				{
					biome = GetBiomeAt(worldX, worldY);
					groundId = GetGroundTileIdAt(worldX, worldY);
				}
				string biomeName = biome.ToString();
				var possibleObjects = GetPossibleSpawnableObjects(biomeName, groundId, _isUnderground);

				if (possibleObjects.Count == 0) continue;

				float totalWeight = 0f;
				foreach (var obj in possibleObjects) totalWeight += obj.Weight;
				float randomValue = (float)chunkRandom.NextDouble() * totalWeight;
				float cumulativeWeight = 0;
				SpawnableObject selectedObject = null!;
				foreach (var obj in possibleObjects)
				{
					cumulativeWeight += obj.Weight;
					if (randomValue <= cumulativeWeight) { selectedObject = obj; break; }
				}

				if (selectedObject != null)
				{
					chunkData.Objects[(worldX, worldY)] = selectedObject.Id;
					var tileDef = WorldTileRegistry.GetTile(selectedObject.Id);
					if (tileDef != null && tileDef.Variation > 1)
					{
						int variationIndex = chunkRandom.Next(tileDef.Variation);
						chunkData.Variations[(worldX, worldY)] = variationIndex;
					}
					if (selectedObject.Id == 4 || selectedObject.Id == 45 ||
						selectedObject.Id == 48 || selectedObject.Id == 49)
					{
						if (chunkRandom.NextDouble() < 0.10)
						{
							//  Ruche : donnée occasionnelle sur la tuile-arbre, via le système
							// générique de métadonnées de tuile plutôt qu'un overlay dédié
							// (voir World.ChunkData.TileMeta).
							chunkData.SetTileMeta(worldX, worldY, "beehive", "1");
						}
					}
				}
			}

			// Génération de villages dans les plaines
			if (!_isUnderground && (chunkX % 5 == 0 && chunkY % 5 == 0))
			{
				int hash = Program.WorldSeed ^ (chunkX * 73856093) ^ (chunkY * 19349663);
				Random villageRand = new Random(hash);
				if (villageRand.NextDouble() < 0.18f)
				{
					int centralX = chunkX * CHUNK_SIZE + CHUNK_SIZE / 2;
					int centralY = chunkY * CHUNK_SIZE + CHUNK_SIZE / 2;
					Biome biomeAtCenter = GetBiomeAt(centralX, centralY);
					if (biomeAtCenter == Biome.Plains)
					{
						var structures = StructureManager.GetStructures()
							.Where(s => s != null && s.Tiles != null && s.Tiles.Count > 0)
							.ToList();
						if (structures.Count > 0)
						{
							//  NOUVEL ALGORITHME DE GÉNÉRATION DE VILLAGE
							// ─────────────────────────────────────────────────────────────
							// 1) On décide d'abord de la POPULATION du village (10 à 50
							//    villageois), pas d'un nombre de maisons fixe : plusieurs
							//    villageois peuvent cohabiter sous le même toit, donc le
							//    nombre de maisons réellement construites en découle.
							// 2) Le centre du village (le puits) est le seul point qui doit
							//    être en plaine — le village peut ensuite librement déborder
							//    sur d'autres biomes en s'étendant.
							// 3) Pour placer chaque maison, on cherche en anneaux concentriques
							//    de plus en plus larges autour du centre : on teste plusieurs
							//    positions réparties sur le pourtour du cercle courant, et si
							//    aucune ne convient (chevauchement, terrain non praticable...),
							//    on élargit le cercle et on recommence. Résultat : le village
							//    peut désormais s'étendre sur des centaines de tuiles si sa
							//    population le demande, au lieu d'être plafonné à ~22 tuiles
							//    de rayon comme avant.
							int targetVillagerCount = villageRand.Next(10, 51); // 10 à 50 habitants
							int villagersHoused = 0;
							int placed = 0;
							int attempts = 0;
							int maxAttempts = 1500; // garde-fou global (perf) — réduit pour éviter les pics de lag au chargement de chunk (voir maxVillageRadius plus bas)
							List<(int x, int y)> doorPositions = new();
							List<int> villageBuildingIds = new();
							List<(int x, int y, int w, int h)> reservedRects = new();
							int spacing = Math.Max(2, villageRand.Next(2, 4));
							(int x, int y)? wellAnchor = null;
							bool infirmaryPlaced = false;

							EnsureTileLoaded(centralX, centralY);
							var villageCenterChunk = GetChunkAt(centralX, centralY);
							if (villageCenterChunk != null)
							{
								bool wellPlaced = StructureManager.TryPlaceWellNear(centralX, centralY, villageCenterChunk, villageRand, 7, out var wellPos);
								if (wellPlaced)
								{
									wellAnchor = wellPos;
								}
							}
							CreateVillageCenter(centralX, centralY, wellAnchor);

							// Recherche en anneaux concentriques : rayon de départ modeste, on
							// l'élargit tant qu'on n'a pas assez logé de villageois.
							double ringRadius = 6.0;
							const double ringStep = 5.0;          // élargissement du cercle à chaque passe infructueuse
							//  PERF : un rayon de 300 tuiles pouvait forcer la génération complète
							// (bruit, objets, minerais...) de plus de 1000 chunks en une seule frame
							// dès qu'un village se mettait à chercher de la place loin de son centre
							// (EnsureTileLoaded ci-dessous génère chaque chunk sondé, même rejeté).
							// 110 tuiles reste largement suffisant pour loger 50 villageois tout en
							// bornant le pire cas de cascade de chargement à un rayon raisonnable.
							const double maxVillageRadius = 110.0;
							int consecutiveEmptyRings = 0;

							while (villagersHoused < targetVillagerCount && attempts < maxAttempts && ringRadius <= maxVillageRadius)
							{
								// Plus le cercle est grand, plus on répartit de points d'essai
								// dessus pour continuer à bien le "remplir" avant de s'éloigner encore.
								int samplesThisRing = Math.Max(10, (int)(ringRadius * 0.8));
								bool placedOnThisRing = false;
								double angleJitterBase = villageRand.NextDouble() * Math.PI * 2.0;

								for (int s = 0; s < samplesThisRing && villagersHoused < targetVillagerCount && attempts < maxAttempts; s++)
								{
									attempts++;

									double angle = angleJitterBase + (Math.PI * 2.0 * s / samplesThisRing) + (villageRand.NextDouble() - 0.5) * 0.35;
									int centerX = centralX + (int)Math.Round(Math.Cos(angle) * ringRadius);
									int centerY = centralY + (int)Math.Round(Math.Sin(angle) * ringRadius * 0.75);

									var structure = PickVillageStructure(structures, villageRand);
									if (structure == null)
										continue;
									int structW = structure.MaxX - structure.MinX + 1;
									int structH = structure.MaxY - structure.MinY + 1;

									int posX = centerX - structW / 2;
									int posY = centerY - structH / 2;

									//  Le village ne se soucie plus du biome à l'emplacement de
									// chaque maison : seul le centre (déjà vérifié en plaine plus
									// haut) compte. Une maison peut donc apparaître en bordure de
									// forêt, de désert, etc. si le village s'étend jusque-là.

									int FloorDivChunk(int v) => v >= 0 ? v / CHUNK_SIZE : (v - CHUNK_SIZE + 1) / CHUNK_SIZE;
									int chunkStartX = FloorDivChunk(posX);
									int chunkStartY = FloorDivChunk(posY);
									int chunkEndX = FloorDivChunk(posX + structW - 1);
									int chunkEndY = FloorDivChunk(posY + structH - 1);
									if (chunkStartX != chunkEndX || chunkStartY != chunkEndY)
										continue;

									int rectLeft = posX - spacing;
									int rectTop = posY - spacing;
									int rectRight = posX + structW - 1 + spacing;
									int rectBottom = posY + structH - 1 + spacing;
									bool overlap = false;
									foreach (var r in reservedRects)
									{
										int rLeft = r.x;
										int rTop = r.y;
										int rRight = r.x + r.w - 1;
										int rBottom = r.y + r.h - 1;
										if (!(rectRight < rLeft || rectLeft > rRight || rectBottom < rTop || rectTop > rBottom))
										{
											overlap = true;
											break;
										}
									}
									if (overlap)
										continue;

									bool claimedByOtherVillage = false;
									for (int cx = rectLeft; cx <= rectRight && !claimedByOtherVillage; cx++)
										for (int cy = rectTop; cy <= rectBottom; cy++)
											if (_villageClaimedTiles.Contains((cx, cy)))
											{
												claimedByOtherVillage = true;
												break;
											}
									if (claimedByOtherVillage)
										continue;

									//  PERF : ne sonder/générer que des chunks pas trop loin de la zone
									// déjà chargée autour du joueur. Avant ce garde-fou, une tentative
									// de placement pouvait forcer EnsureTileLoaded → génération complète
									// (bruit, objets, minerais...) d'un chunk jamais visité, y compris
									// pour des tentatives finalement rejetées — jusqu'à des centaines de
									// chunks générés d'un coup sur une seule frame.
									const int MAX_VILLAGE_PROBE_CHUNK_DIST = 12; // ~192 tuiles autour du chunk qui a déclenché le village
									int probeChunkX = FloorDivChunk(posX);
									int probeChunkY = FloorDivChunk(posY);
									if (Math.Abs(probeChunkX - chunkX) > MAX_VILLAGE_PROBE_CHUNK_DIST ||
										Math.Abs(probeChunkY - chunkY) > MAX_VILLAGE_PROBE_CHUNK_DIST)
										continue;

									EnsureTileLoaded(posX, posY);
									var targetChunk = GetChunkAt(posX, posY);
									if (targetChunk == null)
										continue;

									// Le terrain doit rester praticable (creusable/constructible),
									// quel que soit le biome : ce contrôle était calculé avant mais
									// jamais réellement appliqué (bug), ce qui laissait des maisons
									// se poser sur des tuiles invalides.
									bool terrainOk = true;
									for (int dx = 0; dx < structW && terrainOk; dx++)
									{
										for (int dy = 0; dy < structH && terrainOk; dy++)
										{
											int wx = posX + dx;
											int wy = posY + dy;
											int groundId = GetGroundTileIdAt(wx, wy);
											var groundTile = WorldTileRegistry.GetTile(groundId);
											if (groundTile == null || !groundTile.Diggable)
												terrainOk = false;
										}
									}
									if (!terrainOk)
										continue;

									bool placedNow = StructureManager.TryPlaceStructure(structure, posX, posY, targetChunk, villageRand, (float)Program.GetGameTime(), out var doorPos, out var interiorPos, true, false);
									if (placedNow)
									{
										placed++;
										placedOnThisRing = true;
										doorPositions.Add(doorPos);
										if (villageRand.NextDouble() < 0.65)
											StructureManager.TryPlaceBenchNear(doorPos.x, doorPos.y, targetChunk, villageRand);
										for (int cx = rectLeft; cx <= rectRight; cx++)
											for (int cy = rectTop; cy <= rectBottom; cy++)
												_villageClaimedTiles.Add((cx, cy));
										reservedRects.Add((rectLeft, rectTop, rectRight - rectLeft + 1, rectBottom - rectTop + 1));
										int buildingId = RegisterHouse(doorPos.x, doorPos.y, interiorPos);
										villageBuildingIds.Add(buildingId);

										var candidateTiles = new List<(int x, int y)>();
										for (int cx = posX; cx < posX + structW; cx++)
											for (int cy = posY; cy < posY + structH; cy++)
												candidateTiles.Add((cx, cy));
										RegisterHouseInteriorSpots(buildingId, candidateTiles, doorPos.x, doorPos.y);
										// Les chaises de village sont placées au moment de la génération de la
										// structure, pas par les PNJ lorsqu'ils cherchent une maison.
										TryPlaceVillageChair(buildingId);
										if (!infirmaryPlaced)
											infirmaryPlaced = TryPlaceVillageInfirmaryToilet(buildingId);

										if (Houses.TryGetValue(buildingId, out var house))
										{
											//  PLUSIEURS VILLAGEOIS PAR MAISON : on loge entre 1 et
											// (nombre de cases intérieures libres, plafonné à 4) habitants
											// dans cette maison, sans jamais dépasser l'objectif de
											// population du village.
											int freeSpotsInHouse = HouseInteriorSpots.TryGetValue(buildingId, out var houseSpots) ? houseSpots.Count(sp => !sp.Occupied) : 1;
											int maxOccupantsHere = Math.Max(1, Math.Min(freeSpotsInHouse, 4));
											int remainingNeeded = targetVillagerCount - villagersHoused;
											int occupantsToSpawn = Math.Max(1, Math.Min(maxOccupantsHere, villageRand.Next(1, maxOccupantsHere + 1)));
											occupantsToSpawn = Math.Min(occupantsToSpawn, remainingNeeded);

											for (int occ = 0; occ < occupantsToSpawn; occ++)
											{
Vector2 assignedSpot = house.InteriorPosition;
									if (HouseInteriorSpots.TryGetValue(buildingId, out var initialSpots) && initialSpots.Count > 0)
									{
										var freeSpot = initialSpots.FirstOrDefault(s => !s.Occupied) ?? initialSpots[0];
										freeSpot.Occupied = true;
										assignedSpot = freeSpot.WorldPos;
									}

									var villager = new Entity(assignedSpot, "human", false);
									villager.HomePosition = assignedSpot;
									villager.HomeBuildingId = buildingId;
													if (occ == 0 && villageRand.NextDouble() < 0.2)
													{
														var cat = new Entity(assignedSpot, "Cat", false);
														villager.AddPet(cat);
														targetChunk.Entities.Add(cat);
														var villageEntities = Program.GetEntities();
														if (!villageEntities.Any(e => e.NetId == cat.NetId))
															villageEntities.Add(cat);
													}
									// Les villageois doivent apparaître à l'intérieur, jamais au seuil de la porte.
									float darkness = Program.GetDarknessAlpha();
									if (darkness > 0.6f)
									{
										villager.WorldPos = assignedSpot;
										villager.AiState = NpcAiState.Idle;
										villager.AiTarget = villager.WorldPos;
									}
									else
									{
										villager.WorldPos = assignedSpot;
													villager.AiState = NpcAiState.GoHome;
													villager.AiTarget = assignedSpot;
												}
												targetChunk.Entities.Add(villager);

												var globalEntities = Program.GetEntities();
												if (!globalEntities.Any(e => e.NetId == villager.NetId))
													globalEntities.Add(villager);

												villagersHoused++;
											}
											house.IsOccupied = HouseInteriorSpots.TryGetValue(buildingId, out var spotsCheck) && spotsCheck.All(s => s.Occupied);
										}
										ApplyRoofToPlacedStructure(structure, posX, posY, buildingId, doorPos);
									}
								}

								if (!placedOnThisRing)
									consecutiveEmptyRings++;
								else
									consecutiveEmptyRings = 0;

								ringRadius += ringStep;

								//  Si plusieurs anneaux d'affilée n'ont permis de placer AUCUNE
								// maison (terrain hostile persistant, océan, etc.), on arrête de
								// s'éloigner indéfiniment plutôt que de gaspiller le budget
								// d'essais pour rien.
								if (consecutiveEmptyRings >= 8)
									break;
							}

							if (placed >= 2)
							{
								if (villageRand.NextDouble() < 0.25)
									TryPlaceVillageSpiritTotem(centralX, centralY, villageRand);

								// Relier les portes au puits central (ou entre elles si le puits
								// n'a pas pu être placé) une fois toutes les maisons connues.
								DrawPathsBetweenDoors(doorPositions, 2, wellAnchor);

								if (!infirmaryPlaced)
								{
									foreach (int buildingId in villageBuildingIds)
									{
										if (TryPlaceVillageInfirmaryToilet(buildingId))
										{
											infirmaryPlaced = true;
											break;
										}
									}
								}
								Console.WriteLine($"[Farming] Village généré en ({centralX},{centralY}) : {placed} maison(s), {villagersHoused} villageois logé(s), infirmerie={(infirmaryPlaced ? "oui" : "non")}.");
																																									TryPlaceBiomeWorkZone(WorkZoneKind.LumberCamp, Biome.Forest, centralX, centralY, villageRand, 7011);
																																									TryPlaceBiomeWorkZone(WorkZoneKind.MineEntrance, Biome.Mountains, centralX, centralY, villageRand, 7012);
																																									TryPlaceBiomeWorkZone(WorkZoneKind.HuntingGround, Biome.Forest, centralX, centralY, villageRand, 7013);

								string[] bannerPatterns = { "anchor", "bow", "cross", "crown", "deer", "fist",
															"fox", "hammer", "helmet", "key", "lys", "moon", "star", "sword" };
								string[] bannerShapes   = { "angled", "forked", "long_pointed", "pointed",
															"scalloped", "shield_shaped", "square", "tatterned" };
								Color[] bannerPalette   =
								{
									new Color(180, 20, 20, 255),
									new Color(20, 60, 180, 255),
									new Color(20, 130, 20, 255),
									new Color(160, 130, 0, 255),
									new Color(100, 0, 140, 255),
									new Color(0, 130, 130, 255),
									new Color(200, 80, 0, 255),
									new Color(30, 30, 30, 255),
									new Color(220, 220, 220, 255),
									new Color(140, 80, 30, 255),
								};

								string chosenPattern = bannerPatterns[villageRand.Next(bannerPatterns.Length)];
								string chosenShape   = bannerShapes[villageRand.Next(bannerShapes.Length)];

								int bgIdx  = villageRand.Next(bannerPalette.Length);
								int patIdx = (bgIdx + 1 + villageRand.Next(bannerPalette.Length - 1)) % bannerPalette.Length;
								Color bgColor  = bannerPalette[bgIdx];
								Color patColor = bannerPalette[patIdx];

								int wantedBanners  = villageRand.Next(1, Math.Min(4, doorPositions.Count + 1));
								int bannersPlaced  = 0;
								var shuffledDoors  = doorPositions.OrderBy(_ => villageRand.Next()).ToList();

								foreach (var (doorX, doorY) in shuffledDoors)
								{
									if (bannersPlaced >= wantedBanners) break;
									for (int attempt = 0; attempt < 30; attempt++)
									{
										int bx = doorX + villageRand.Next(-5, 6);
										int by = doorY + villageRand.Next(-5, 6);

										if (GetObjectIdAt(bx, by) != 0) continue;
										if (GetObjectIdAt(bx + 1, by) != 0) continue;
										if (GetObjectIdAt(bx, by - 1) != 0) continue;
										if (GetObjectIdAt(bx, by - 2) != 0) continue;

										EnsureTileLoaded(bx, by);
										var bannerChunk = GetChunkAt(bx, by);
										if (bannerChunk == null) continue;

										int groundId = GetGroundTileIdAt(bx, by);
										var groundTile = WorldTileRegistry.GetTile(groundId);
										if (groundTile == null || !groundTile.Diggable) continue;

										bannerChunk.Objects[(bx, by)] = 7000;
										bannerChunk.BannerHolders[(bx, by)] = new BannerHolderData
										{
											HasBanner = true,
											PatternName = chosenPattern,
											ShapeName = chosenShape,
											BgR = bgColor.R,
											BgG = bgColor.G,
											BgB = bgColor.B,
											PatR = patColor.R,
											PatG = patColor.G,
											PatB = patColor.B,
										};
										bannersPlaced++;
										break;
									}
								}
							}
						}
					}
				}
			}

			// 4. Décorations de sol (petites herbes, cailloux...)
			for (int localX = 0; localX < CHUNK_SIZE; localX++)
				for (int localY = 0; localY < CHUNK_SIZE; localY++)
				{
					int worldX = chunkX * CHUNK_SIZE + localX;
					int worldY = chunkY * CHUNK_SIZE + localY;

					if (chunkData.Objects.ContainsKey((worldX, worldY))) continue;

					int groundId = _isUnderground ? GetCaveGroundTileIdAt(worldX, worldY) : GetGroundTileIdAt(worldX, worldY);
					if (WorldTileRegistry.TileDecorations.TryGetValue(groundId, out var deco))
					{
						if (chunkRandom.NextDouble() < deco.SpawnChance)
						{
							if (WorldTileRegistry.DecorationTextures.TryGetValue(groundId, out var textures) && textures.Count > 0)
							{
								int texIndex = chunkRandom.Next(textures.Count);
								chunkData.Decorations[(worldX, worldY)] = texIndex;
							}
						}
					}
				}

			// 5. Entités (Animaux/Monstres)
			//  Apparaissent en petits troupeaux (une seule espèce par troupeau, comme dans la
			// nature) plutôt qu'individuellement dispersés : plus cohérent visuellement, et le
			// chunk entier a une chance UNIQUE (WILDLIFE_HERD_SPAWN_CHANCE, un peu plus rare
			// que l'ancien taux d'apparition individuel) de faire apparaître tout un troupeau
			// d'un coup, sur le modèle des hordes de gobelins ci-dessous (5.5).
			string[] possibleSpecies = GetSpeciesForBiome(_isUnderground ? GetCaveBiomeAt(chunkX * CHUNK_SIZE, chunkY * CHUNK_SIZE) : GetBiomeAt(chunkX * CHUNK_SIZE, chunkY * CHUNK_SIZE));

			// Génération d'insectes volants
			int insectCount = chunkRandom.Next(0, 3);
            string[] flyingInsects = GetFlyingInsectsForBiome(_isUnderground ? GetCaveBiomeAt(chunkX * CHUNK_SIZE, chunkY * CHUNK_SIZE) : GetBiomeAt(chunkX * CHUNK_SIZE, chunkY * CHUNK_SIZE));
            if (flyingInsects.Length > 0)
            {
                for (int i = 0; i < insectCount; i++)
                {
                    string insect = flyingInsects[chunkRandom.Next(flyingInsects.Length)];

                    int localX, localY;
                    int attempts = 0;
                    do
                    {
                        localX = chunkRandom.Next(0, CHUNK_SIZE);
                        localY = chunkRandom.Next(0, CHUNK_SIZE);
                        attempts++;
                        if (attempts > 100) break;
                    } while (chunkData.Objects.ContainsKey((chunkX * CHUNK_SIZE + localX, chunkY * CHUNK_SIZE + localY)));

                    float worldX = (chunkX * CHUNK_SIZE + localX) * Program.TileSize + chunkRandom.Next(0, Program.TileSize);
                    float worldY = (chunkY * CHUNK_SIZE + localY) * Program.TileSize + chunkRandom.Next(0, Program.TileSize);

                    int tileX = (int)(worldX / Program.TileSize);
                    int tileY = (int)(worldY / Program.TileSize);
                    int groundId = _isUnderground ? GetCaveGroundTileIdAt(tileX, tileY) : GetGroundTileIdAt(tileX, tileY);
                    // 10 = water tiles — ne pas faire spawn d'entités dans l'eau
                    if (groundId == 10) continue;

                    var entity = new Entity(new Vector2(worldX, worldY), insect, false);
                    chunkData.Entities.Add(entity);
                }
            }

            //  Troupeau de faune/monstres du biome : une seule espèce tirée au sort pour tout
            // le troupeau (comme des gobelins hordes de 3-6), regroupé autour d'un centre
            // aléatoire du chunk plutôt que dispersé sur toute sa surface.
            if (possibleSpecies.Length > 0 && chunkRandom.NextDouble() < WILDLIFE_HERD_SPAWN_CHANCE)
            {
                string herdSpecies = possibleSpecies[chunkRandom.Next(possibleSpecies.Length)];
                int herdSize = chunkRandom.Next(WILDLIFE_HERD_MIN_SIZE, WILDLIFE_HERD_MAX_SIZE + 1);
                int centerLocalX = chunkRandom.Next(2, CHUNK_SIZE - 2);
                int centerLocalY = chunkRandom.Next(2, CHUNK_SIZE - 2);

                var usedHerdPositions = new HashSet<(int, int)>();

                for (int i = 0; i < herdSize; i++)
                {
                    int localX, localY;
                    int attempts = 0;
                    do
                    {
                        localX = Math.Clamp(centerLocalX + chunkRandom.Next(-3, 4), 0, CHUNK_SIZE - 1);
                        localY = Math.Clamp(centerLocalY + chunkRandom.Next(-3, 4), 0, CHUNK_SIZE - 1);
                        attempts++;
                        if (attempts > 100) break;
                    } while (usedHerdPositions.Contains((localX, localY)));
                    usedHerdPositions.Add((localX, localY));

                    int tileX = chunkX * CHUNK_SIZE + localX;
                    int tileY = chunkY * CHUNK_SIZE + localY;

                    if (chunkData.Objects.ContainsKey((tileX, tileY))) continue;

                    int groundIdMain = _isUnderground ? GetCaveGroundTileIdAt(tileX, tileY) : GetGroundTileIdAt(tileX, tileY);
                    if (groundIdMain == 10) continue; // Empêche le spawn dans l'eau

                    float worldX = tileX * Program.TileSize + chunkRandom.Next(0, Program.TileSize);
                    float worldY = tileY * Program.TileSize + chunkRandom.Next(0, Program.TileSize);

                    var entity = new Entity(new Vector2(worldX, worldY), herdSpecies, false);
                    chunkData.Entities.Add(entity);
                }
            }
			// 5.5 Hordes de gobelins
			if (!_isUnderground && chunkData.Entities.All(e => e.Species != "human")
				&& chunkRandom.NextDouble() < GOBLIN_HORDE_SPAWN_CHANCE)
			{
				int hordeSize = chunkRandom.Next(2, 5);
				int centerLocalX = chunkRandom.Next(2, CHUNK_SIZE - 2);
				int centerLocalY = chunkRandom.Next(2, CHUNK_SIZE - 2);

				for (int i = 0; i < hordeSize; i++)
				{
					int localX = Math.Clamp(centerLocalX + chunkRandom.Next(-3, 4), 0, CHUNK_SIZE - 1);
					int localY = Math.Clamp(centerLocalY + chunkRandom.Next(-3, 4), 0, CHUNK_SIZE - 1);

					int gTileX = chunkX * CHUNK_SIZE + localX;
					int gTileY = chunkY * CHUNK_SIZE + localY;

					if (chunkData.Objects.ContainsKey((gTileX, gTileY))) continue;

					int gGroundId = GetGroundTileIdAt(gTileX, gTileY);
					var gGroundTile = WorldTileRegistry.GetTile(gGroundId);
					if (gGroundTile == null || !gGroundTile.Walkable) continue;

					float gWorldX = gTileX * Program.TileSize + chunkRandom.Next(0, Program.TileSize);
					float gWorldY = gTileY * Program.TileSize + chunkRandom.Next(0, Program.TileSize);

					var goblin = new Entity(new Vector2(gWorldX, gWorldY), "goblin", false);
					chunkData.Entities.Add(goblin);

					//  Loup de compagnie chevauchable : spawné juste à côté de son gobelin, sur
					// une case praticable proche. Si aucune case libre n'est trouvée à proximité,
					// on renonce simplement (le gobelin reste sans monture) plutôt que de forcer
					// un spawn hors-chemin.
					// En savane ou désert, le gobelin monte une hyèna au lieu d'un loup.
					if (chunkRandom.NextDouble() < GOBLIN_MOUNT_WOLF_CHANCE)
					{
						int wolfLocalX = Math.Clamp(localX + chunkRandom.Next(-1, 2), 0, CHUNK_SIZE - 1);
						int wolfLocalY = Math.Clamp(localY + chunkRandom.Next(-1, 2), 0, CHUNK_SIZE - 1);
						int wTileX = chunkX * CHUNK_SIZE + wolfLocalX;
						int wTileY = chunkY * CHUNK_SIZE + wolfLocalY;

						if (!chunkData.Objects.ContainsKey((wTileX, wTileY)))
						{
							int wGroundId = GetGroundTileIdAt(wTileX, wTileY);
							var wGroundTile = WorldTileRegistry.GetTile(wGroundId);
							if (wGroundTile != null && wGroundTile.Walkable)
							{
								float wWorldX = wTileX * Program.TileSize + chunkRandom.Next(0, Program.TileSize);
								float wWorldY = wTileY * Program.TileSize + chunkRandom.Next(0, Program.TileSize);

								// Choisir l'espèce de monture selon le biome
								var goblinBiome = GetBiomeAt(gWorldX, gWorldY);
								string mountSpecies = (goblinBiome == Biome.Savanna || goblinBiome == Biome.Desert) ? "hyena" : "wolf";
								
								var mount = new Entity(new Vector2(wWorldX, wWorldY), mountSpecies, false);
								chunkData.Entities.Add(mount);
								goblin.AddPet(mount, asMount: true);
							}
						}
					}
				}
			}

			// 6. Entrées de grottes (Surface seulement)
			if (!_isUnderground)
			{
				if (chunkRandom.NextDouble() < CAVE_ENTRY_SPAWN_CHANCE)
				{
					List<(int x, int y)> validPositions = new();
					for (int localX = 0; localX < CHUNK_SIZE; localX++)
					{
						for (int localY = 0; localY < CHUNK_SIZE; localY++)
						{
							int worldX = chunkX * CHUNK_SIZE + localX;
							int worldY = chunkY * CHUNK_SIZE + localY;
							if (!chunkData.Objects.ContainsKey((worldX, worldY)))
							{
								int groundId = GetGroundTileIdAt(worldX, worldY);
								if (groundId == 1 || groundId == 44 || groundId == 46 || groundId == 47 || groundId == 11 || groundId == 26)
								{
									validPositions.Add((worldX, worldY));
								}
							}
						}
					}
					if (validPositions.Count > 0)
					{
						var pos = validPositions[chunkRandom.Next(validPositions.Count)];
						chunkData.Objects[pos] = 50;
						_caveEntryPairs[(pos.x, pos.y)] = (-1, -1);
					}
				}
			}

			// 7. Entrées de donjon (uniquement dans les grottes)
			if (_isUnderground)
			{
				if (chunkRandom.NextDouble() < DUNGEON_ENTRY_SPAWN_CHANCE)
				{
					List<(int x, int y)> validPositions = new();
					for (int localX = 0; localX < CHUNK_SIZE; localX++)
					{
						for (int localY = 0; localY < CHUNK_SIZE; localY++)
						{
							int worldX = chunkX * CHUNK_SIZE + localX;
							int worldY = chunkY * CHUNK_SIZE + localY;
							if (!chunkData.Objects.ContainsKey((worldX, worldY)))
							{
								int groundId = GetCaveGroundTileIdAt(worldX, worldY);
								if (groundId == 100)
								{
									validPositions.Add((worldX, worldY));
								}
							}
						}
					}
					if (validPositions.Count > 0)
					{
						var pos = validPositions[chunkRandom.Next(validPositions.Count)];
						chunkData.Objects[pos] = 50;
						AllocateDungeonInstance(pos.x, pos.y, maxWidth: 200, maxHeight: 200);
					}
				}
			}

			// 8. Trésors souterrains (uniquement dans les grottes) : un coffre en bois (ID 42, déjà
			// utilisé pour les coffres fabriqués par le joueur) apparaît occasionnellement, rempli
			// de loot aléatoire.
			if (_isUnderground)
			{
				if (chunkRandom.NextDouble() < UNDERGROUND_TREASURE_SPAWN_CHANCE)
				{
					List<(int x, int y)> validPositions = new();
					for (int localX = 0; localX < CHUNK_SIZE; localX++)
					{
						for (int localY = 0; localY < CHUNK_SIZE; localY++)
						{
							int worldX = chunkX * CHUNK_SIZE + localX;
							int worldY = chunkY * CHUNK_SIZE + localY;
							if (chunkData.Objects.ContainsKey((worldX, worldY))) continue;
							int groundId = GetCaveGroundTileIdAt(worldX, worldY);
							var tileDefAtGround = WorldTileRegistry.GetTile(groundId);
							// Praticable uniquement (pas d'eau souterraine, pas de vide) : même critère
							// que celui utilisé ailleurs pour valider une case de spawn au sol.
							if (tileDefAtGround != null && tileDefAtGround.Walkable)
								validPositions.Add((worldX, worldY));
						}
					}
					if (validPositions.Count > 0)
					{
						var pos = validPositions[chunkRandom.Next(validPositions.Count)];
						chunkData.Objects[pos] = TREASURE_CHEST_OBJECT_ID;
						chunkData.Containers[pos] = GenerateUndergroundTreasureLoot(chunkRandom);
						Console.WriteLine($" Trésor souterrain placé en ({pos.x},{pos.y}) [chunk ({chunkX},{chunkY})]");
					}
				}
			}

			chunkData.IsGenerated = true;
			return chunkData;
		}

		//  Fréquence d'apparition d'un trésor souterrain par chunk de grotte (~2%).
		private const double UNDERGROUND_TREASURE_SPAWN_CHANCE = 0.02;

		//  Troupeaux de faune/monstres du biome (section 5, ci-dessus) : un seul jet de dé par
		// chunk pour décider si un troupeau apparaît (au lieu de l'ancien Next(0,2) qui tentait
		// de faire spawn un animal isolé à ~50% de chance à chaque chunk). Légèrement plus rare
		// qu'avant, mais chaque apparition amène 3 à 6 individus de la même espèce d'un coup.
		private const double WILDLIFE_HERD_SPAWN_CHANCE = 0.35;
		private const int WILDLIFE_HERD_MIN_SIZE = 3;
		private const int WILDLIFE_HERD_MAX_SIZE = 6;

		//  ID d'objet réutilisé pour le coffre de trésor souterrain : c'est le même coffre en bois
		// (placeable ID 42, IsContainer = true) que celui fabriqué par le joueur, ce qui lui donne
		// gratuitement tout son comportement (ouverture, rendu, sauvegarde) sans rien dupliquer.
		private const int TREASURE_CHEST_OBJECT_ID = 42;

		//  Table de loot pondérée pour les trésors souterrains : (itemKey, poids, quantité min, quantité max).
		// Poids élevé = commun, poids faible = rare. Utiliser les clés d'items actuelles plutôt que
		// des IDs numériques hérités.
		private static readonly (string key, float weight, int min, int max)[] _undergroundTreasureLootTable = new[]
		{
			("coin", 25f, 5, 20),         // Pièce (monnaie)
			("ruby", 8f,  1, 3),          // Rubis
			("iron_ingot", 10f, 2, 5),    // Lingot de fer
			("runic_stone", 5f,  1, 1),   // Pierre runique
		};

		/// <summary>
		/// Génère le contenu (aléatoire) d'un coffre de trésor souterrain : quelques emplacements
		/// remplis d'items tirés dans la table de loot pondérée ci-dessus, le reste vide.
		/// </summary>
		private static ContainerInventoryData GenerateUndergroundTreasureLoot(Random rand)
		{
			var container = new ContainerInventoryData(9, 3);

			var table = _undergroundTreasureLootTable.Where(e => e.weight > 0f).ToArray();
			float totalWeight = table.Sum(e => e.weight);
			if (totalWeight <= 0f || table.Length == 0) return container;

			int rollCount = rand.Next(2, 5); // 2 à 4 items par trésor
			int slotIndex = 0;
			for (int i = 0; i < rollCount && slotIndex < container.Slots.Count; i++)
			{
				float roll = (float)rand.NextDouble() * totalWeight;
				float cumulative = 0f;
				foreach (var entry in table)
				{
					cumulative += entry.weight;
					if (roll <= cumulative)
					{
						if (GameData.TryGetItemByKey(entry.key, out var itemData))
						{
							int count = entry.min == entry.max ? entry.min : rand.Next(entry.min, entry.max + 1);
							var item = new Item(itemData.Name, count, itemData.Color, itemData.Icon);
							container.Slots[slotIndex].Item = item;
							container.Slots[slotIndex].Count = count;
							slotIndex++;
						}
						break;
					}
				}
			}
			return container;
		}

		private static void TryPlaceDesertOasis(int chunkX, int chunkY, Random chunkRandom, ChunkData chunkData)
		{
			int desertTiles = 0;
			for (int localX = 0; localX < CHUNK_SIZE; localX++)
			{
				for (int localY = 0; localY < CHUNK_SIZE; localY++)
				{
					int worldX = chunkX * CHUNK_SIZE + localX;
					int worldY = chunkY * CHUNK_SIZE + localY;
					if (GetBiomeAt(worldX, worldY) == Biome.Desert)
						desertTiles++;
				}
			}

			if (desertTiles < CHUNK_SIZE * CHUNK_SIZE / 3)
				return;

			const double oasisChance = 0.08;
			if (chunkRandom.NextDouble() >= oasisChance)
				return;

			int localCenterX = chunkRandom.Next(4, CHUNK_SIZE - 4);
			int localCenterY = chunkRandom.Next(4, CHUNK_SIZE - 4);
			int centerX = chunkX * CHUNK_SIZE + localCenterX;
			int centerY = chunkY * CHUNK_SIZE + localCenterY;
			int radius = chunkRandom.Next(2, 4);

			var waterPositions = new List<(int x, int y)>();
			for (int localX = localCenterX - radius; localX <= localCenterX + radius; localX++)
			{
				for (int localY = localCenterY - radius; localY <= localCenterY + radius; localY++)
				{
					if (localX < 0 || localX >= CHUNK_SIZE || localY < 0 || localY >= CHUNK_SIZE)
						continue;

					int dx = localX - localCenterX;
					int dy = localY - localCenterY;
					int distSq = dx * dx + dy * dy;
					int maxSq = radius * radius;
					if (distSq > maxSq)
						continue;

					int worldX = chunkX * CHUNK_SIZE + localX;
					int worldY = chunkY * CHUNK_SIZE + localY;
					if (chunkData.Objects.ContainsKey((worldX, worldY)))
						continue;

					int groundId = GetGroundTileIdAt(worldX, worldY);
					if (groundId != 11)
						continue;

					if (chunkRandom.NextDouble() < 0.75 || distSq <= (radius - 1) * (radius - 1))
					{
						waterPositions.Add((worldX, worldY));
					}
				}
			}

			if (waterPositions.Count == 0)
				return;

			foreach (var pos in waterPositions)
			{
				chunkData.GroundOverrides[(pos.x, pos.y)] = 10;
			}

			var palmCandidates = new List<(int x, int y)>();
			for (int localX = localCenterX - radius - 1; localX <= localCenterX + radius + 1; localX++)
			{
				for (int localY = localCenterY - radius - 1; localY <= localCenterY + radius + 1; localY++)
				{
					if (localX < 0 || localX >= CHUNK_SIZE || localY < 0 || localY >= CHUNK_SIZE)
						continue;

					int worldX = chunkX * CHUNK_SIZE + localX;
					int worldY = chunkY * CHUNK_SIZE + localY;
					if (chunkData.Objects.ContainsKey((worldX, worldY)))
						continue;
					if (waterPositions.Contains((worldX, worldY)))
						continue;

					int groundId = GetGroundTileIdAt(worldX, worldY);
					if (groundId != 11)
						continue;

					bool adjacentToWater = false;
					for (int nx = worldX - 1; nx <= worldX + 1 && !adjacentToWater; nx++)
					{
						for (int ny = worldY - 1; ny <= worldY + 1; ny++)
						{
							if (nx == worldX && ny == worldY) continue;
							if (waterPositions.Contains((nx, ny)))
							{
								adjacentToWater = true;
								break;
							}
						}
					}

					if (adjacentToWater)
						palmCandidates.Add((worldX, worldY));
				}
			}

			int palmsToPlace = Math.Min(3, Math.Max(1, chunkRandom.Next(1, 4)));
			for (int i = 0; i < palmsToPlace && palmCandidates.Count > 0; i++)
			{
				int index = chunkRandom.Next(palmCandidates.Count);
				var palmPos = palmCandidates[index];
				palmCandidates.RemoveAt(index);
				chunkData.Objects[palmPos] = 49;
				palmCandidates.RemoveAll(p => p == palmPos);
			}
		}

		public static void LoadChunkFromSave(ChunkSaveData chunkData)
		{
			var chunksDict = GetChunksDict();
			var chunkKey = (chunkData.ChunkX, chunkData.ChunkY);
			var chunk = new ChunkData
			{
				Objects = new Dictionary<(int x, int y), int>(),
				Overlays = new Dictionary<(int x, int y), int>(),
				Heights = new Dictionary<(int x, int y), int>(),
				Decorations = new Dictionary<(int x, int y), int>(),
				Variations = new Dictionary<(int x, int y), int>(),
				TileMeta = new Dictionary<(int x, int y), Dictionary<string, string>>(),
				IsGenerated = true,
				LastAccessTime = (float)Raylib.GetTime()
			};

			foreach (var kv in chunkData.Objects)
			{
				var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
				chunk.Objects[(x, y)] = kv.Value;
			}

			if (chunkData.Overlays != null)
			{
				foreach (var kv in chunkData.Overlays)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.Overlays[(x, y)] = kv.Value;
				}
			}

			if (chunkData.BuildingIds != null)
			{
				foreach (var kv in chunkData.BuildingIds)
				{
					var (worldX, worldY) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					BuildingIds[(worldX, worldY)] = kv.Value;
				}
			}

			// Heights
			foreach (var kv in chunkData.Heights)
			{
				var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
				chunk.Heights[(x, y)] = kv.Value;
			}

			// Decorations
			foreach (var kv in chunkData.Decorations)
			{
				var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
				chunk.Decorations[(x, y)] = kv.Value;
			}

			// Variations
			foreach (var kv in chunkData.Variations)
			{
				var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
				chunk.Variations[(x, y)] = kv.Value;
			}

			// Roof heights & info
			if (chunkData.RoofHeights != null)
			{
				foreach (var kv in chunkData.RoofHeights)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.RoofHeights[(x, y)] = kv.Value;
				}
			}

			if (chunkData.RoofTileInfo != null)
			{
				foreach (var kv in chunkData.RoofTileInfo)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					if (Enum.TryParse<World.RoofTexKind>(kv.Value.TexKind, true, out var texKind))
					{
						chunk.RoofTileInfo[(x, y)] = new World.RoofTileInfo(
							kv.Value.Height,
							kv.Value.IsHorizontalZone,
							kv.Value.WallStackCount,
							texKind,
							kv.Value.IsColumnAnchor);
					}
				}
			}

			// Wall coverings
			if (chunkData.WallCoverings != null)
			{
				foreach (var kv in chunkData.WallCoverings)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.WallCoverings[(x, y)] = kv.Value;
				}
			}

			// Containers
			if (chunkData.Containers != null)
			{
				foreach (var cont in chunkData.Containers)
				{
					var (x, y) = SaveSystem.UnpackTileKey(cont.Key, chunkData.ChunkX, chunkData.ChunkY);

					int objectId = 0;
					if (chunk.Objects.TryGetValue((x, y), out int foundId))
						objectId = foundId;

					int columns = 8;
					if (objectId != 0)
					{
						var tileData = WorldTileRegistry.GetTile(objectId);
						if (tileData?.IsContainer == true)
							columns = tileData.ContainerColumns > 0 ? tileData.ContainerColumns : 8;
					}

					int slotCount = cont.Value.Slots.Count;
					var containerData = new ContainerInventoryData(slotCount, columns);

					for (int i = 0; i < cont.Value.Slots.Count && i < containerData.Slots.Count; i++)
					{
						var slotSave = cont.Value.Slots[i];
						var slot = containerData.Slots[i];

						if (!slotSave.IsEmpty && !string.IsNullOrEmpty(slotSave.ItemName))
						{
							var itemDef = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == slotSave.ItemName);
							if (itemDef.ID != 0)
							{
var restoredItem = new Item(itemDef.Name, slotSave.Count, itemDef.Color, itemDef.Icon);

								if (slotSave.CustomColors != null && slotSave.CustomColors.Count > 0)
								{
									var colors = new List<Color?>();
									foreach (var colorSave in slotSave.CustomColors)
									{
										if (colorSave != null)
											colors.Add(new Color(colorSave.R, colorSave.G, colorSave.B, colorSave.A));
										else
											colors.Add(null);
									}
									restoredItem.CustomColors = colors;
								}

								slot.Item = restoredItem;
								slot.Count = slotSave.Count;
							}
						}
					}

					chunk.Containers[(x, y)] = containerData;
				}
			}

			// Armor stands
			if (chunkData.ArmorStands != null)
			{
				foreach (var stand in chunkData.ArmorStands)
				{
					var (x, y) = SaveSystem.UnpackTileKey(stand.Key, chunkData.ChunkX, chunkData.ChunkY);
					var standData = new ArmorStandData();
					standData.CurrentPose = stand.Value.CurrentPose ?? "idle";
					if (!string.IsNullOrEmpty(stand.Value.Head))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.Head);
						if (d.ID != 0) standData.Head = new Item(d.Name, 1, d.Color, d.Icon);
					}
					if (!string.IsNullOrEmpty(stand.Value.Body))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.Body);
						if (d.ID != 0) standData.Body = new Item(d.Name, 1, d.Color, d.Icon);
					}
					if (!string.IsNullOrEmpty(stand.Value.Legs))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.Legs);
						if (d.ID != 0) standData.Legs = new Item(d.Name, 1, d.Color, d.Icon);
					}
					if (!string.IsNullOrEmpty(stand.Value.MainHand))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.MainHand);
						if (d.ID != 0) standData.MainHand = new Item(d.Name, 1, d.Color, d.Icon);
					}
					if (!string.IsNullOrEmpty(stand.Value.OffHand))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.OffHand);
						if (d.ID != 0) standData.OffHand = new Item(d.Name, 1, d.Color, d.Icon);
					}
					if (!string.IsNullOrEmpty(stand.Value.Face))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.Face);
						if (d.ID != 0) standData.Face = new Item(d.Name, 1, d.Color, d.Icon);
					}
					if (!string.IsNullOrEmpty(stand.Value.Ears))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.Ears);
						if (d.ID != 0) standData.Ears = new Item(d.Name, 1, d.Color, d.Icon);
					}
					if (!string.IsNullOrEmpty(stand.Value.Neck))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.Neck);
						if (d.ID != 0) standData.Neck = new Item(d.Name, 1, d.Color, d.Icon);
					}
					if (!string.IsNullOrEmpty(stand.Value.Waist))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.Waist);
						if (d.ID != 0) standData.Waist = new Item(d.Name, 1, d.Color, d.Icon);
					}
					if (!string.IsNullOrEmpty(stand.Value.Feet))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.Feet);
						if (d.ID != 0) standData.Feet = new Item(d.Name, 1, d.Color, d.Icon);
					}
					if (!string.IsNullOrEmpty(stand.Value.Back))
					{
						var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == stand.Value.Back);
						if (d.ID != 0) standData.Back = new Item(d.Name, 1, d.Color, d.Icon);
					}
					chunk.ArmorStands[(x, y)] = standData;
				}
			}

			// Banner holders
			if (chunkData.BannerHolders != null)
			{
				foreach (var kv in chunkData.BannerHolders)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.BannerHolders[(x, y)] = new BannerHolderData
					{
						HasBanner = kv.Value.HasBanner,
						PatternName = kv.Value.PatternName,
						ShapeName = kv.Value.ShapeName,
						BgR = kv.Value.BgR, BgG = kv.Value.BgG, BgB = kv.Value.BgB,
						PatR = kv.Value.PatR, PatG = kv.Value.PatG, PatB = kv.Value.PatB,
					};
				}
			}

			// Ground overrides
			if (chunkData.GroundOverrides != null)
			{
				foreach (var kv in chunkData.GroundOverrides)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.GroundOverrides[(x, y)] = kv.Value;
				}
			}

			// Crops
			if (chunkData.Crops != null)
			{
				foreach (var crop in chunkData.Crops)
				{
					var (x, y) = SaveSystem.UnpackTileKey(crop.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.Crops[(x, y)] = new World.CropData
					{
						CropTileId = crop.Value.CropTileId,
						PlantTime = crop.Value.PlantTime
					};
				}
			}

			// Métadonnées de tuile (générique)
			if (chunkData.TileMeta != null)
			{
				foreach (var kv in chunkData.TileMeta)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					if (kv.Value != null && kv.Value.Count > 0)
						chunk.TileMeta[(x, y)] = new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase);
				}
			}

			//  MIGRATION : anciennes ruches sauvegardées comme overlay 6000 (avant le passage
			// à TileMeta) → reconverties en métadonnée "beehive" à leur premier chargement, et
			// retirées d'Overlays pour ne pas être redessinées deux fois / interférer avec le
			// filtre générique d'overlays. Sans effet sur les sauvegardes déjà migrées.
			foreach (var pos in chunk.Overlays.Where(kv => kv.Value == 6000).Select(kv => kv.Key).ToList())
			{
				chunk.SetTileMeta(pos.x, pos.y, "beehive", "1");
				chunk.Overlays.Remove(pos);
			}

			// Aquariums
			if (chunkData.Aquariums != null)
			{
				foreach (var kv in chunkData.Aquariums)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					var aquariumData = new AquariumData();
					foreach (var fishSave in kv.Value)
					{
						aquariumData.FishStates[fishSave.SlotIndex] = new AquariumFishState
						{
							SlotIndex = fishSave.SlotIndex,
							LocalPosition = new Vector2(fishSave.LocalPosX, fishSave.LocalPosY),
							TargetPosition = new Vector2(fishSave.TargetPosX, fishSave.TargetPosY),
							MoveSpeed = fishSave.MoveSpeed,
							IsMoving = fishSave.IsMoving,
							IdleTimer = fishSave.IdleTimer,
							BobPhase = fishSave.BobPhase,
							BobSpeed = fishSave.BobSpeed,
							MoveStartTime = fishSave.MoveStartTime,
							MoveDuration = fishSave.MoveDuration,
							BaseY = fishSave.BaseY
						};
					}
					World.SetAquariumData(x, y, aquariumData);
				}
			}

			// Entities
			if (chunkData.Entities != null)
			{
				foreach (var entityData in chunkData.Entities)
				{
					var entity = new Entity(new Vector2(entityData.WorldPosX, entityData.WorldPosY), entityData.Species, false, skipHomeAutoAssign: true);
					entity.Tint = entityData.Tint == null
						? entity.Tint
						: new Color(entityData.Tint.R, entityData.Tint.G, entityData.Tint.B, entityData.Tint.A);
					entity.HairColor = entityData.HairColor == null
						? entity.HairColor
						: new Color(entityData.HairColor.R, entityData.HairColor.G, entityData.HairColor.B, entityData.HairColor.A);
					entity.HairStyle = entityData.HairStyle;
					entity.BeardStyle = entityData.BeardStyle;
					SaveSystem.RestoreRandomFeatures(entity, entityData);
					entity.EnsureHumanEyeColor();
					entity.SetHealthSilently(entityData.CurrentHP, entityData.MaxHP);
					entity.IsBoss = entityData.IsBoss || string.Equals(entityData.Species, "ogre", StringComparison.OrdinalIgnoreCase) || string.Equals(entityData.Species, "khamsin", StringComparison.OrdinalIgnoreCase) || (string.Equals(entityData.Species, "genie", StringComparison.OrdinalIgnoreCase) && !string.Equals(entityData.CustomName, "Illusion du Génie", StringComparison.OrdinalIgnoreCase));
					entity.IsSpiritAnimal = entityData.IsSpiritAnimal;
					entity.SpiritTotemTileX = entityData.SpiritTotemTileX;
					entity.SpiritTotemTileY = entityData.SpiritTotemTileY;
					entity.Behavior = string.IsNullOrWhiteSpace(entityData.Behavior) ? (entity.IsBoss ? "hostile" : "passive") : entityData.Behavior;
					if (entityData.VisionRange.HasValue)
						entity.VisionRange = entityData.VisionRange.Value;
					entity.AlertDuration = entityData.AlertDuration > 0f ? entityData.AlertDuration : (entity.IsBoss ? 2f : 0f);
					entity.IsAlerted = entityData.IsAlerted || entity.IsBoss;
					entity.AlertTimer = entityData.AlertTimer > 0f ? entityData.AlertTimer : (entity.IsBoss ? entity.AlertDuration : 0f);
					entity.IsTamed = entityData.IsTamed && !entity.IsSpiritAnimal;
					entity.IsGuildMember = entityData.IsGuildMember;
					entity.WantsToJoinGuild = entityData.WantsToJoinGuild;
					entity.OwnerName = string.IsNullOrEmpty(entityData.OwnerName) ? null : entityData.OwnerName;
					entity.CustomName = string.IsNullOrEmpty(entityData.CustomName) ? null : entityData.CustomName;
					entity.HomePosition = new Vector2(entityData.HomePosX, entityData.HomePosY);
					//  CORRECTIF : HomeBuildingId n'était jamais restauré à la sauvegarde/chargement
					// (seule HomePosition l'était), alors qu'il reste à sa valeur par défaut -1 posée
					// par le constructeur Entity. Or TOUT le système de dépôt au coffre du soir
					// (routine + filet de sécurité au moment de s'endormir, voir Entity.UpdateSleepState
					// et le bloc "Comportement des villageois") est verrouillé par HomeBuildingId >= 0.
					// Le PNJ rentrait donc bien chez lui après rechargement (GoHome/GetHomeEntryTarget
					// utilisent HomePosition en repli, indépendamment de HomeBuildingId), mais restait
					// ensuite bloqué à attendre indéfiniment sans jamais vider son inventaire dans le
					// coffre, faute de HomeBuildingId valide. On le recalcule ici à partir de la tuile
					// de HomePosition, exactement comme le fait l'assignation normale d'une maison
					// (voir World.FindNearestFreeHouseSpot, qui renvoie toujours les deux ensemble).
					if (entity.HomePosition != Vector2.Zero)
					{
						int homeTileX = (int)(entity.HomePosition.X / Program.TileSize);
						int homeTileY = (int)(entity.HomePosition.Y / Program.TileSize);
						int restoredBuildingId = GetBuildingIdAt(homeTileX, homeTileY);
						entity.HomeBuildingId = restoredBuildingId != 0 ? restoredBuildingId : -1;
					}
					entity.IsTrader = entityData.IsTrader;
					entity.Profession = entityData.Profession >= 0 ? (ProfessionType)entityData.Profession : ProfessionType.None;
					if (entity.Profession == ProfessionType.Guard)
						entity.ConfigureVillageGuardStats();
					else if (entity.IsVillager)
						entity.Attack = SpeciesData.GetSpeciesInfo(entity.Species)?.Attack ?? entity.Attack;
					entity.TraderItems = entityData.TraderItemIds?.ToList() ?? new List<int>();
					entity.IsBaby = entityData.IsBaby;
					entity.Age = entityData.Age;
					entity.GrowthTime = entityData.GrowthTime;
					entity.GestationTimer = entityData.GestationTimer;
					entity.Scale = entityData.Scale;
					entity.IsInterestedInFood = entityData.IsInterestedInFood;
					entity.PreferredFoodId = entityData.PreferredFoodId;
					entity.FollowOrder = entityData.FollowOrder;
					entity.IsTalking = entityData.IsTalking;
					entity.TalkTimer = entityData.TalkTimer;
					entity.TalkCooldown = entityData.TalkCooldown;
					entity.TalkText = string.IsNullOrEmpty(entityData.TalkText) ? null : entityData.TalkText;
				
					if (entityData.Equipment != null)
					{
						entity.Equipment ??= new Equipment();
					}
					if (entity.Profession == ProfessionType.Guard)
						entity.EquipVillageGuard();
				
					if (entityData.InventorySlots != null && entityData.InventorySlots.Count > 0)
					{
						entity.Inventory = new ContainerInventoryData(entityData.InventorySlots.Count, 4);
						for (int i = 0; i < entityData.InventorySlots.Count && i < entity.Inventory.Slots.Count; i++)
							SaveSystem.RestoreInventorySlotFromSave(entityData.InventorySlots[i], entity.Inventory.Slots[i]);
					}
				
					if (entity.Profession == ProfessionType.Guard)
					{
						entity.IsTrader = false;
						entity.TraderItems.Clear();
					}
					else if (entity.Profession != ProfessionType.None || entityData.IsTrader || entity.TraderItems.Count > 0)
					{
						entity.IsTrader = true;
						if (entity.Profession == ProfessionType.None)
							entity.AssignProfessionAndTrade();
						else
							entity.InitTraderItems();
					}
				
					if (entityData.TamedBehavior >= 0 && entityData.TamedBehavior <= 3)
						entity.TamedBehavior = (TamedAnimalMode)entityData.TamedBehavior;
				
					if (entity.IsTamed)
						entity.Behavior = "tamed";
				
					chunk.Entities.Add(entity);
				}
			}

			// Finally, register the chunk in the world dictionary
			chunksDict[chunkKey] = chunk;

			//  BUG CORRIGÉ (toits mal affichés en multijoueur / après rechargement d'une
			// sauvegarde) : chunk.RoofTileInfo ci-dessus ne contient QUE la copie locale au
			// chunk, désérialisée depuis le réseau ou le fichier de sauvegarde. Mais le
			// rendu (TryGetRoofTileInfo, dans World_Core.DrawWorld) lit un dictionnaire
			// GLOBAL statique (_roofTileInfo dans World_Houses.cs), rempli normalement par
			// ApplyRoofToHouse au moment de la construction. Ce dictionnaire global n'était
			// jamais synchronisé avec les chunks chargés depuis le réseau ou une sauvegarde :
			// TryGetRoofTileInfo échouait donc systématiquement pour toute maison que le
			// joueur/client n'avait pas construite lui-même dans la session en cours, et le
			// rendu retombait sur RoofTexKind.Default (toit plat) au lieu de la vraie forme
			// en pente. On restaure donc explicitement l'info dans le dictionnaire global
			// juste après l'enregistrement du chunk.
			RestoreRoofTileInfoForChunk(chunk);
        }

		public static void UpdateActiveChunks(Vector2 playerPos, float currentTime, List<Entity> globalEntities)
		{
			CheckDungeonBossDeaths();

			if (IsUnderwater)
			{
				UpdateUnderwaterChunks(playerPos, currentTime);
				return;
			}
			
			int ts = Program.TileSize;
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			
			// Obtenir la caméra actuelle
			Camera2D camera = Program.GetCurrentCamera();
			
			// Calculer les limites de l'écran dans le monde
			Vector2 topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera);
			Vector2 bottomRight = Raylib.GetScreenToWorld2D(new Vector2(sw, sh), camera);
			
			// Étendre légèrement pour éviter les clignotements
			int extraPixels = ts * EXTRA_CHUNKS_AROUND_PLAYER;
			topLeft.X -= extraPixels;
			topLeft.Y -= extraPixels;
			bottomRight.X += extraPixels;
			bottomRight.Y += extraPixels;
			
			// Convertir en coordonnées de chunks
			int minChunkX = (int)Math.Floor(topLeft.X / (CHUNK_SIZE * ts));
			int maxChunkX = (int)Math.Floor(bottomRight.X / (CHUNK_SIZE * ts));
			int minChunkY = (int)Math.Floor(topLeft.Y / (CHUNK_SIZE * ts));
			int maxChunkY = (int)Math.Floor(bottomRight.Y / (CHUNK_SIZE * ts));
			
			// Ajouter une marge de sécurité
			minChunkX -= EXTRA_CHUNKS_AROUND_PLAYER;
			minChunkY -= EXTRA_CHUNKS_AROUND_PLAYER;
			maxChunkX += EXTRA_CHUNKS_AROUND_PLAYER;
			maxChunkY += EXTRA_CHUNKS_AROUND_PLAYER;

			//  PERF (dézoom) : sans plafond, cette fenêtre grandit avec le dézoom (l'écran couvre
			// une zone du monde plus grande) et charge/simule bien plus de chunks — donc bien plus
			// de PNJ (villages entiers en plus) — que ce que DrawWorld dessine réellement (sa
			// fenêtre de tuiles est déjà plafonnée à MAX_RENDER_TILES_X/Y, indépendamment du
			// zoom). Résultat : à fort dézoom, on simulait ET dessinait des PNJ de villages
			// entiers en plus, ce qu'aucune réduction de détail par PNJ ne peut compenser
			// puisque c'est le NOMBRE de PNJ qui explose, pas leur coût individuel. On applique
			// donc le même principe de plafond ici, pour que la zone de chunks actifs reste
			// bornée quel que soit le niveau de zoom (le joueur dézoomé voit alors le monde déjà
			// exploré au loin, mais sans re-simuler/re-dessiner des villages hors de la fenêtre
			// réellement rendue).
			const int MAX_ACTIVE_CHUNKS_X = 12;
			const int MAX_ACTIVE_CHUNKS_Y = 9;
			int chunkRangeX = maxChunkX - minChunkX;
			if (chunkRangeX > MAX_ACTIVE_CHUNKS_X)
			{
				int excess = chunkRangeX - MAX_ACTIVE_CHUNKS_X;
				minChunkX += excess / 2;
				maxChunkX -= excess / 2;
			}
			int chunkRangeY = maxChunkY - minChunkY;
			if (chunkRangeY > MAX_ACTIVE_CHUNKS_Y)
			{
				int excess = chunkRangeY - MAX_ACTIVE_CHUNKS_Y;
				minChunkY += excess / 2;
				maxChunkY -= excess / 2;
			}
			
			var chunksDict = GetChunksDict();
			
			// Créer un HashSet des chunks qui devraient être chargés
			var shouldBeLoaded = new HashSet<(int x, int y)>();
			
			for (int x = minChunkX; x <= maxChunkX; x++)
			{
				for (int y = minChunkY; y <= maxChunkY; y++)
				{
					shouldBeLoaded.Add((x, y));
					
					if (!chunksDict.ContainsKey((x, y)))
					{
						var chunkKey = (x, y);
						if (_pendingActiveChunkLoadKeys.Add(chunkKey))
							_pendingActiveChunkLoads.Enqueue(chunkKey);
					}
					else if (chunksDict.TryGetValue((x, y), out var chunk))
					{
						chunk.LastAccessTime = currentTime;
					}
				}
			}

			for (int i = 0; i < ACTIVE_CHUNKS_PER_FRAME && _pendingActiveChunkLoads.Count > 0; i++)
			{
				var chunkKey = _pendingActiveChunkLoads.Dequeue();
				_pendingActiveChunkLoadKeys.Remove(chunkKey);
				if (shouldBeLoaded.Contains(chunkKey) && !chunksDict.ContainsKey(chunkKey))
					LoadOrGenerateChunk(chunkKey.chunkX, chunkKey.chunkY, currentTime, globalEntities);
			}
			
			// Décharger les chunks qui ne sont pas dans la zone visible.
			// EXCEPTION : un donjon reste intégralement chargé pendant toute la visite. Sa zone
			// est petite (quelques centaines de chunks au pire) mais s'étend largement au-delà du
			// rayon de rendu autour du joueur (le boss et les salles éloignées sont volontairement
			// loin de l'entrée) — les décharger comme le monde normal supprimerait immédiatement
			// leurs entités (boss compris) de la liste globale dès la génération.
			if (_isInDungeon)
				return;

			var toUnload = new List<(int x, int y)>();
			foreach (var chunk in chunksDict)
			{
				// CORRECTION : utiliser chunk.Key.Item1 et chunk.Key.Item2
				if (!shouldBeLoaded.Contains((chunk.Key.Item1, chunk.Key.Item2)))
				{
					//  CORRECTION DUPLICATION : chunk.Entities n'est JAMAIS tenu à jour quand
					// une entité se déplace d'un chunk à l'autre en cours de jeu (rien ne la
					// retire du chunk qu'elle quitte, rien ne l'ajoute à celui qu'elle rejoint).
					// Si on sauvegardait chunk.Entities tel quel, on pouvait donc écrire sur
					// disque une entité qui a déjà quitté ce chunk depuis longtemps (avec sa
					// position actuelle, potentiellement dans un tout autre chunk), tandis que
					// le chunk qu'elle occupe réellement ne la connaît pas. Au rechargement,
					// cette entité "fantôme" ressortait comme une copie en plus de l'originale.
					// On reconstruit donc la liste juste avant sauvegarde à partir de la
					// position RÉELLE des entités dans globalEntities : chaque entité n'est
					// ainsi jamais persistée que dans le fichier du chunk où elle se trouve
					// vraiment au moment de la sauvegarde.
					var entitiesReallyInThisChunk = new List<Entity>();
					for (int i = globalEntities.Count - 1; i >= 0; i--)
					{
						var entity = globalEntities[i];
						//  Les compagnons d'armure (crabe, etc.) ne sont jamais liés à un chunk : ils
						// sont entièrement dérivés de l'équipement porté et respawnés à chaque
						// chargement (voir Program.UpdateArmorCompanions / SaveChunkEntities). Les
						// laisser passer ici les faisait disparaître silencieusement dès que leur
						// chunk du moment se déchargeait (ex : juste après le chargement d'une
						// partie, avant que la caméra ne soit recentrée sur le joueur) : le nuage de
						// fumée d'apparition se déclenchait, mais le crabe était retiré juste après.
						if (entity.IsPetCompanion) continue;
						int entityChunkX = (int)Math.Floor(entity.WorldPos.X / (CHUNK_SIZE * Program.TileSize));
						int entityChunkY = (int)Math.Floor(entity.WorldPos.Y / (CHUNK_SIZE * Program.TileSize));
						if (entityChunkX == chunk.Key.Item1 && entityChunkY == chunk.Key.Item2)
						{
							entitiesReallyInThisChunk.Add(entity);
							globalEntities.RemoveAt(i);
						}
					}
					chunk.Value.Entities = entitiesReallyInThisChunk;

					// Sauvegarder avant de décharger
					if (!string.IsNullOrEmpty(CurrentSaveName))
					{
						if (_isInDungeon && _currentDungeonInstanceId >= 0)
							SaveSystem.SaveDungeonChunk(CurrentSaveName, _currentDungeonInstanceId, chunk.Key.Item1, chunk.Key.Item2, chunk.Value);
						else
							SaveSystem.SaveChunk(CurrentSaveName, chunk.Key.Item1, chunk.Key.Item2, chunk.Value, _isUnderground);
					}
					
					toUnload.Add((chunk.Key.Item1, chunk.Key.Item2));
				}
			}
			
			foreach (var key in toUnload)
			{
				chunksDict.Remove((key.x, key.y));
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  DESSIN DU MONDE (UNIFIÉ) - VERSION OPTIMISÉE AVEC CACHE LOCAL
		// ─────────────────────────────────────────────────────────────────────────
		private static void DrawGrandfatherClockHands(float drawX, float drawY, int drawW, int drawH, float gameTime)
		{
			float dayProgress = (gameTime % 600f + 600f) % 600f / 600f;
			float gameHours = dayProgress * 24f;
			float hour = gameHours % 12f;
			float minute = (gameHours - MathF.Floor(gameHours)) * 60f;
			Vector2 center = new(drawX + drawW * 0.5f, drawY + drawH * 0.32f - 8f);
			float radius = drawW * 0.2f;

			float hourAngle = (hour / 12f) * MathF.Tau - MathF.PI / 2f;
			float minuteAngle = (minute / 60f) * MathF.Tau - MathF.PI / 2f;
			Vector2 hourEnd = center + new Vector2(MathF.Cos(hourAngle), MathF.Sin(hourAngle)) * radius * 0.62f;
			Vector2 minuteEnd = center + new Vector2(MathF.Cos(minuteAngle), MathF.Sin(minuteAngle)) * radius;

			Raylib.DrawLineEx(center, hourEnd, MathF.Max(1.5f, drawW * 0.045f), new Color(54, 28, 18, 255));
			Raylib.DrawLineEx(center, minuteEnd, MathF.Max(1f, drawW * 0.03f), new Color(54, 28, 18, 255));
			Raylib.DrawCircleV(center, MathF.Max(1.5f, drawW * 0.045f), new Color(54, 28, 18, 255));
		}

		private static int[,] _renderHeights = new int[0, 0];
		private static int[,] _renderGroundIds = new int[0, 0];
		private static int[,] _renderObjectIds = new int[0, 0];
		private static int[,] _renderOverlayIds = new int[0, 0];
		private static int[,] _renderBuildingIds = new int[0, 0];
		private static int[,] _renderRoofHeights = new int[0, 0];
		private static int[,] _renderDecorationIndex = new int[0, 0];
		private static int[,] _renderWallCoveringIds = new int[0, 0];
		private static bool[,] _renderIsWallToHide = new bool[0, 0];

		private static void EnsureRenderTileBuffers(int width, int height)
		{
			if (_renderHeights.GetLength(0) >= width && _renderHeights.GetLength(1) >= height)
				return;

			_renderHeights = new int[width, height];
			_renderGroundIds = new int[width, height];
			_renderObjectIds = new int[width, height];
			_renderOverlayIds = new int[width, height];
			_renderBuildingIds = new int[width, height];
			_renderRoofHeights = new int[width, height];
			_renderDecorationIndex = new int[width, height];
			_renderWallCoveringIds = new int[width, height];
			_renderIsWallToHide = new bool[width, height];
		}

		private static void AddHouseResidentDoorIcons(List<RenderItem> renderItems, List<Entity> entities)
		{
			foreach (var house in Houses.Values)
			{
				if (house.BuildingId <= 0) continue;
				var residents = entities
					.Where(e => e.IsAlive && e.IsVillager && e.HomeBuildingId == house.BuildingId)
					.OrderBy(e => e.NetId)
					.ToList();
				if (residents.Count == 0) continue;

				int extraCount = Math.Max(0, residents.Count - 3);
				var visibleResidents = residents.Take(Math.Min(3, residents.Count)).ToList();
				float iconSize = 22f;
				float gap = 18f;
				float totalWidth = Math.Max(0f, (visibleResidents.Count - 1) * gap);
				Vector2 basePos = house.DoorWorldPos + new Vector2(0f, -28f);
				float startX = basePos.X - totalWidth / 2f;

				for (int i = 0; i < visibleResidents.Count; i++)
				{
					var resident = visibleResidents[i];
					var hairBaseTex = (resident.Species == "human" && Program.hairBaseTextures.Count > 0)
						? Program.hairBaseTextures[Math.Clamp(resident.HairStyle, 0, Program.hairBaseTextures.Count - 1)]
						: new Texture2D();
					var hairOverlayTex = (resident.Species == "human" && Program.hairOverlayTextures.Count > 0)
						? Program.hairOverlayTextures[Math.Clamp(resident.HairStyle, 0, Program.hairOverlayTextures.Count - 1)]
						: new Texture2D();
					var eyeBaseTex = (resident.Species == "human" && Program.EyeBaseTextures.Count > 0)
						? Program.EyeBaseTextures[Math.Clamp(Program.EyeStyle, 0, Program.EyeBaseTextures.Count - 1)]
						: new Texture2D();
					var eyeOverlayTex = (resident.Species == "human" && Program.EyeOverlayTextures.Count > 0)
						? Program.EyeOverlayTextures[Math.Clamp(Program.EyeStyle, 0, Program.EyeOverlayTextures.Count - 1)]
						: new Texture2D();
					Color tint = resident.Tint.R != 0 || resident.Tint.G != 0 || resident.Tint.B != 0 || resident.Tint.A != 0 ? resident.Tint : Color.White;
					Color hairColor = resident.HairColor.R != 0 || resident.HairColor.G != 0 || resident.HairColor.B != 0 || resident.HairColor.A != 0 ? resident.HairColor : Color.White;
					Vector2 iconPos = new Vector2(startX + i * gap, basePos.Y);

					renderItems.Add(new RenderItem
					{
						Y = float.MaxValue - 1000f,
						DrawAction = () =>
						{
							Rectangle bgRect = new Rectangle(iconPos.X - iconSize / 2f - 3f, iconPos.Y - iconSize / 2f - 3f, iconSize + 6f, iconSize + 6f);
							Raylib.DrawRectangleRounded(bgRect, 0.35f, 10, new Color(18, 18, 20, 200));
							Raylib.DrawRectangleRoundedLines(bgRect, 0.35f, 10, 2, new Color(215, 180, 110, 180));
							EntityRenderer.DrawEntity(
								resident.Species,
								"idle",
								0,
								0f,
								1f,
								new Vector2(iconPos.X, iconPos.Y + 8f),
								tint,
								hairBaseTex,
								hairOverlayTex,
								hairColor,
								SpeciesData.Skeletons,
								eyeBaseTex,
								eyeOverlayTex,
								customScale: 0.42f,
								equipment: resident.Equipment,
								isCarrying: false,
								inWater: false,
								attackSwingProgress: 0f,
								heldItemTexture: default,
								headAngle: 0f,
								keepItemHorizontal: false,
								isBow: false,
								underwearTexture: default,
								beardStyle: resident.BeardStyle,
								prevAnim: "",
								prevFrame: 0,
								prevProg: 0f,
								transitionWeight: 0f,
								flashWhite: false);
						}
					});
				}

				if (extraCount > 0)
				{
					Vector2 extraPos = basePos + new Vector2((visibleResidents.Count > 0 ? (visibleResidents.Count - 1) * gap : 0f) / 2f + 12f, 6f);
					renderItems.Add(new RenderItem
					{
						Y = float.MaxValue - 999f,
						DrawAction = () =>
						{
							Rectangle badgeRect = new Rectangle(extraPos.X - 8f, extraPos.Y - 8f, 16f, 16f);
							Raylib.DrawRectangleRounded(badgeRect, 0.5f, 8, new Color(72, 52, 30, 220));
							Raylib.DrawRectangleRoundedLines(badgeRect, 0.5f, 8, 1, new Color(240, 220, 160, 220));
							FontManager.DrawText($"+{extraCount}", (int)extraPos.X - 5, (int)extraPos.Y - 6, 10, new Color(255, 245, 220, 255));
						}
					});
				}
			}
		}

		public static void DrawWorld(
			Vector2 playerPos,
			Camera2D camera,
			string anim, int frame, float prog,
			float facing, Vector2 pDim, Color pSkin,
			Texture2D missingTex,
			HashSet<string> destroyed,
			Dictionary<string, WorldObjectHP> objectHP,
			List<Entity> entities,
			Dictionary<int, Texture2D> tileTextures,
			List<Texture2D> hairBase,
			List<Texture2D> hairOverlay,
			Vector2 mouseWorldPos,
			int hoverTileX,
			int hoverTileY,
			bool showDebug,
			bool isCarrying,
			bool isPlayerInWater,
			float headAngle,
			Vector2? sittingPosition = null,
			float playerDepth = 0f,
			IReadOnlyList<Program.LocalPlayer>? playersToRender = null,
			List<SpellProjectile>? spellProjectiles = null,
			bool drawPlayer = true)
		{
			_tileTextures = tileTextures;
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			int ts = Program.TileSize;
			float currentTime = Program.GetGameTime();

			//  PERF (dézoom) : informe EntityRenderer du niveau de zoom courant, pour son LOD
			// interne (voir _currentZoomLevel dans EntityRenderer.cs). Une seule fois par frame,
			// avant tout DrawEntity de cette frame.
			EntityRenderer.SetCurrentZoomLevel(camera.Zoom);

			Vector2 topLeft = Raylib.GetScreenToWorld2D(new Vector2(0, 0), camera);
			Vector2 bottomRight = Raylib.GetScreenToWorld2D(new Vector2(sw, sh), camera);

			const int MAX_RENDER_TILES_X = 120;
			const int MAX_RENDER_TILES_Y = 80;

			//  MARGE POUR ÉLÉMENTS "HAUTS" (arbres, toits...) : ces objets sont dessinés
			// depuis leur tuile d'ancrage (souvent la base) avec un décalage vertical/horizontal
			// qui peut représenter plusieurs tuiles (yOffset = -roofHeightTiles * ts pour un
			// toit, DrawOffset pour un arbre etc.). Avec une marge d'1 seule tuile, dès que la
			// tuile d'ancrage sort de la fenêtre visible (typiquement en bas de l'écran), l'objet
			// entier disparaît instantanément — alors qu'une partie de son sprite (le haut de
			// l'arbre, le bas du toit) devrait encore être visible à l'écran. On élargit donc la
			// zone de tuiles VALIDÉES (celle qui alimente les tableaux heights/objectIds/...)
			// bien au-delà du strict cadrage caméra, pour que ces tuiles d'ancrage restent prises
			// en compte et leurs objets dessinés jusqu'à ce qu'ils quittent réellement l'écran.
			// Coût : quelques dizaines de tuiles de plus à traiter par frame, négligeable face au
			// gain visuel (pas de pop-out brutal), et toujours borné par MAX_RENDER_TILES_X/Y.
			const int TALL_FEATURE_MARGIN_X = 3;
			const int TALL_FEATURE_MARGIN_Y = 8;

			int startX = (int)Math.Floor(topLeft.X / ts) - 1 - TALL_FEATURE_MARGIN_X;
			int endX = (int)Math.Ceiling(bottomRight.X / ts) + 1 + TALL_FEATURE_MARGIN_X;
			int startY = (int)Math.Floor(topLeft.Y / ts) - 1 - TALL_FEATURE_MARGIN_Y;
			int endY = (int)Math.Ceiling(bottomRight.Y / ts) + 1 + TALL_FEATURE_MARGIN_Y;

			float entityCullMargin = ts * 2f;
			bool IsEntityNearScreen(Vector2 pos)
			{
				return pos.X >= topLeft.X - entityCullMargin && pos.X <= bottomRight.X + entityCullMargin &&
					pos.Y >= topLeft.Y - entityCullMargin && pos.Y <= bottomRight.Y + entityCullMargin;
			}

			bool IsSegmentNearScreen(Vector2 start, Vector2 end)
			{
				return MathF.Max(start.X, end.X) >= topLeft.X - entityCullMargin &&
					MathF.Min(start.X, end.X) <= bottomRight.X + entityCullMargin &&
					MathF.Max(start.Y, end.Y) >= topLeft.Y - entityCullMargin &&
					MathF.Min(start.Y, end.Y) <= bottomRight.Y + entityCullMargin;
			}

			int renderWidth = endX - startX;
			int renderHeight = endY - startY;

			if (renderWidth > MAX_RENDER_TILES_X)
			{
				int excess = renderWidth - MAX_RENDER_TILES_X;
				startX += excess / 2;
				endX -= excess / 2;
			}
			if (renderHeight > MAX_RENDER_TILES_Y)
			{
				int excess = renderHeight - MAX_RENDER_TILES_Y;
				startY += excess / 2;
				endY -= excess / 2;
			}

			//  BUG CORRIGÉ ("Arithmetic operation resulted in an overflow" en cas de téléportation
			// lointaine) : ce clamp raisonnait en coordonnées ABSOLUES (± 10000 tuiles autour de
			// l'origine du monde), pas en distance relative à la caméra. Dès que le joueur se
			// téléporte loin (ex: tuile 125000), startX ET endX valent tous les deux ~125000
			// (déjà bornés à une petite fenêtre par MAX_RENDER_TILES_X/Y ci-dessus) : mais
			// Math.Min(endX, 10000) écrasait alors endX à 10000 tout seul, sans toucher startX
			// (toujours ~124995, car > -10000). endX se retrouvait < startX, donc
			// `width = endX - startX + 1` devenait négatif, et `new int[width, heightTiles]`
			// avec une dimension négative lève justement OverflowException. Comme
			// MAX_RENDER_TILES_X/Y borne déjà la fenêtre de rendu (~120x80 tuiles) quelle que
			// soit la position du joueur dans le monde, ce clamp absolu est à la fois inutile et
			// dangereux : on le retire, et on ne garde qu'un garde-fou final qui ne peut jamais
			// inverser start/end (protège seulement contre un topLeft/bottomRight invalide,
			// NaN/Infini, qui donnerait un cast (int) aberrant).
			int width = endX - startX + 1;
			int heightTiles = endY - startY + 1;

			const int MAX_SAFE_DIM = MAX_RENDER_TILES_X + MAX_RENDER_TILES_Y + 4; // marge large, jamais atteint en usage normal
			if (width <= 0 || width > MAX_SAFE_DIM)
			{
				width = Math.Clamp(width, 1, MAX_SAFE_DIM);
				endX = startX + width - 1;
			}
			if (heightTiles <= 0 || heightTiles > MAX_SAFE_DIM)
			{
				heightTiles = Math.Clamp(heightTiles, 1, MAX_SAFE_DIM);
				endY = startY + heightTiles - 1;
			}

			// Buffers réutilisés : la grille est entièrement réécrite ci-dessous à chaque frame.
			EnsureRenderTileBuffers(width, heightTiles);
			int[,] heights = _renderHeights;
			int[,] groundIds = _renderGroundIds;
			int[,] objectIds = _renderObjectIds;
			int[,] overlayIds = _renderOverlayIds;
			int[,] buildingIds = _renderBuildingIds;
			int[,] roofHeights = _renderRoofHeights;
			int[,] decorationIndex = _renderDecorationIndex;
			int[,] wallCoveringIds = _renderWallCoveringIds;
			bool[,] isWallToHide = _renderIsWallToHide;

			// Remplir les tableaux en une seule passe
			//
			//  PERF (dézoom) : ce bloc tournait auparavant sur un chunk (GetChunkAt) DIFFÉRENT
			// par appel — un pour la hauteur, un pour le sol, un pour l'objet, un pour l'overlay,
			// un pour le revêtement mural, un pour la décoration — soit jusqu'à 8 lookups de
			// Dictionary<(int,int), ChunkData> pour LA MÊME tuile, chacun recalculant en plus
			// chunkX/chunkY. En dézoom, MAX_RENDER_TILES_X/Y (120x80) peut porter ce nombre de
			// tuiles à ~9600/frame, donc jusqu'à ~77 000 lookups de chunk rien que pour cette
			// boucle. On récupère maintenant le chunk UNE SEULE FOIS par tuile et on lit ses
			// dictionnaires internes directement ; les fonctions globales (GetHeightAt,
			// GetGroundTileIdAt, GetBuildingIdAt, GetRoofHeight) ne sont rappelées qu'en repli,
			// pour les tuiles qui n'ont pas encore d'entrée explicite dans le chunk (génération
			// procédurale / cache climat), ce qui reste correct au sous-sol/en extérieur du
			// chunk chargé.
			PerfStats.StartSection(PerfStats.Section.TileGridFill);
			int visibleObjectCount = 0;
			for (int x = startX; x <= endX; x++)
			{
				int ix = Math.Clamp(x - startX, 0, width - 1);
				for (int y = startY; y <= endY; y++)
				{
					int iy = Math.Clamp(y - startY, 0, heightTiles - 1);
					if (!Program.IsTileLit(x, y))
					{
						heights[ix, iy] = 0;
						groundIds[ix, iy] = 0;
						objectIds[ix, iy] = 0;
						overlayIds[ix, iy] = 0;
						buildingIds[ix, iy] = 0;
						roofHeights[ix, iy] = 0;
						decorationIndex[ix, iy] = -1;
						wallCoveringIds[ix, iy] = 0;
						isWallToHide[ix, iy] = false;
						continue;
					}
					
					try
					{
						var chunk = GetChunkAt(x, y); // UN SEUL lookup de chunk pour toute la tuile

						int h = _isUnderground && !_isInDungeon
							? 0
							: (chunk != null && chunk.Heights.TryGetValue((x, y), out int hv))
								? hv : GetHeightAt(x, y);
						heights[ix, iy] = h;

						int gid = (chunk != null && chunk.GroundOverrides.TryGetValue((x, y), out int gv))
							? gv : GetGroundTileIdAt(x, y);
						groundIds[ix, iy] = gid;

						int oid = chunk?.Objects.GetValueOrDefault((x, y), 0) ?? 0;
						objectIds[ix, iy] = oid;
						if (oid != 0) visibleObjectCount++;

						int ovid = chunk?.Overlays.GetValueOrDefault((x, y), 0) ?? 0;
						overlayIds[ix, iy] = ovid;

						// Les buildingIds vivent dans un registre séparé (BuildingIds), pas dans
						// ChunkData : pas de lookup redondant à éliminer ici.
						int bid = GetBuildingIdAt(x, y);
						buildingIds[ix, iy] = bid;

						int rh = (chunk != null && chunk.RoofHeights.TryGetValue((x, y), out int rhv))
							? rhv : GetRoofHeight(x, y);
						roofHeights[ix, iy] = rh;

						isWallToHide[ix, iy] = IsWallToHide(oid);
						wallCoveringIds[ix, iy] = chunk?.GetWallCoveringAt(x, y) ?? 0;

						// Décoration (même chunk déjà en main, plus besoin de le redemander)
						decorationIndex[ix, iy] = (chunk != null && chunk.Decorations.TryGetValue((x, y), out int deco))
							? deco : -1;
					}
					catch (Exception ex)
					{
						// Log l'erreur mais continue pour éviter le crash
						Console.WriteLine($" Erreur à ({x},{y}) : {ex.Message}");
					}
				}
			}
			PerfStats.EndSection(PerfStats.Section.TileGridFill);

			Texture2D cliffDownTex = GetCliffDownTexture(missingTex);
			Texture2D cliffBorderTex = GetCliffBorderTexture(missingTex);

			if (_isUnderground)
				Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 100));
			if (_isPlayerIndoors)
				Raylib.ClearBackground(Color.Black);
			
			if (false)
			{
				// ========== RENDU SOUS-MARIN ==========
				int playerTileX = (int)(playerPos.X / ts);
				int playerTileY = (int)(playerPos.Y / ts);
				
				// Effacer l'écran avec la couleur de l'eau
				Raylib.ClearBackground(new Color(10, 30, 60, 255));
				
				// Fond d'eau sombre
				Raylib.DrawRectangle(0, 0, sw, sh, new Color(10, 30, 60, 255));
				
				// Dessiner le fond marin
				for (int x = startX; x <= endX; x++)
				{
					for (int y = startY; y <= endY; y++)
					{
						int floorDepth = GetSeaFloorDepth(x, y);
						float distToFloor = Math.Max(0f, floorDepth - playerDepth);
						float visibility = Math.Clamp(1f - (distToFloor / 32f), 0f, 1f);
						visibility = (float)Math.Pow(visibility, 0.8f);
						
						if (visibility > 0.02f)
						{
							int seaFloorId = GetUnderwaterChunkSeaFloor(x, y);
							Texture2D tex = GetTileTextureForSeaFloor(x, y, seaFloorId, currentTime);
							if (tex.Id == 0 && missingTex.Id != 0) tex = missingTex;
							
							if (tex.Id != 0)
							{
								int drawY = (int)(y * ts - (playerDepth * 3f));
								
								float depthFactor = Math.Clamp(playerDepth / 40f, 0f, 1f);
								Color tint = new Color(
									(byte)(60 + (40 * (1f - depthFactor))),
									(byte)(80 + (60 * (1f - depthFactor))),
									(byte)(150 + (105 * depthFactor)),
									(byte)(255 * visibility)
								);
								
								Raylib.DrawTexturePro(tex,
									new Rectangle(0, 0, tex.Width, tex.Height),
									new Rectangle(x * ts, drawY, ts, ts),
									Vector2.Zero, 0, tint);
							}
						}
					}
				}
				
				// ========== ENTITÉS SOUS-MARINES ==========
				if (_renderItemsPoolInUse)
					_renderItemsPool.Clear();
				else
					_renderItemsPoolInUse = true;
				var underwaterRenderItems = _renderItemsPool;

				// Ajouter les entités visibles sous l'eau
				foreach (var entity in entities)
				{
					if (!entity.IsAlive) continue;
					if (Program.MountedAnimal == entity) continue; // dessinée avec le joueur (voir plus bas), pour l'ordre de superposition
					if (entity.IsMounted && entity.Rider != null) continue; // monture PNJ dessinée via le cavalier
					if (!IsEntityNearScreen(entity.WorldPos))
						continue;
					float entityYOffset = -playerDepth * 3f;
				
					var e = entity;
					underwaterRenderItems.Add(new RenderItem
					{
						Y = e.FeetPos.Y + entityYOffset,
						DrawAction = () =>
						{
								Vector2 drawPos = new Vector2(e.WorldPos.X, e.WorldPos.Y + entityYOffset - e.VisualOffsetY);
							Texture2D hairBaseTex = hairBase.Count > e.HairStyle ? hairBase[e.HairStyle] : new Texture2D();
					Texture2D hairOverlayTex = hairOverlay.Count > e.HairStyle ? hairOverlay[e.HairStyle] : new Texture2D();
					Entity? mount = e.IsRidingMount && e.OwnedMount != null && e.OwnedMount.IsMounted && e.OwnedMount.Rider == e ? e.OwnedMount : null;
					EntityRenderer.DrawEntity(
						e.Species, e.AnimState, e.CurrentFrame, e.AnimProgress,
						e.Facing, drawPos, e.Tint,
						hairBaseTex, hairOverlayTex, e.HairColor,
						SpeciesData.Skeletons,
						eyes: EntityRenderer.GetEyesTextureForSpecies(e.Species), mouth: e.Species == "human" ? Program.MouthTexture : default, forceBlinking: e.IsBlinking,
						customScale: e.Scale,
						equipment: null,
						isCarrying: false, inWater: true,
						attackSwingProgress: e.GetAttackSwingProgress(),
						flashWhite: e.DamageFlash > 0f,
						randomFeatureVariant: e.RandomFeatureVariant,
						randomFeatureColor: e.RandomFeatureColor,
						slimeStoredItem: e.IsSlime ? e.GetSlimeStorageSlot().Item : null,
						beardStyle: e.BeardStyle,
						shadowGroundOffset: e.VisualOffsetY,
						drawMount: mount != null ? () =>
						{
							var m = mount;
							if (m == null || !m.IsAlive) return;
							Texture2D mHairBase = hairBase.Count > m.HairStyle ? hairBase[m.HairStyle] : new Texture2D();
							Texture2D mHairOverlay = hairOverlay.Count > m.HairStyle ? hairOverlay[m.HairStyle] : new Texture2D();
							EntityRenderer.DrawEntity(
								m.Species, m.AnimState, m.CurrentFrame, m.AnimProgress,
								m.Facing, m.WorldPos, m.Tint,
								mHairBase, mHairOverlay, m.HairColor,
								SpeciesData.Skeletons,
								eyes: EntityRenderer.GetEyesTextureForSpecies(m.Species),
								mouth: Program.GetMouthTextureFor(m),
								forceBlinking: m.IsBlinking,
								customScale: m.Scale,
								equipment: m.Equipment,
								isCarrying: false, inWater: m.IsInWater,
								attackSwingProgress: m.GetAttackSwingProgress(),
								flashWhite: m.DamageFlash > 0f,
								pupilLeftOffset: m.PupilLeftOffset,
								pupilRightOffset: m.PupilRightOffset,
								eyebrowLeftOffset: m.EyebrowLeftOffset,
								eyebrowRightOffset: m.EyebrowRightOffset,
								randomFeatureVariant: m.RandomFeatureVariant,
								randomFeatureColor: m.RandomFeatureColor,
								beardStyle: m.BeardStyle);
						} : null);
						}
					});
				}

				// Joueurs locaux supplémentaires (sous l'eau)
				if (playersToRender != null)
				{
					foreach (var extraPlayer in playersToRender)
					{
						if (!extraPlayer.Connected) continue;
						if (!IsEntityNearScreen(extraPlayer.Position)) continue;
						float extraYOffset = -playerDepth * 3f;
						Vector2 extraDrawPos = new Vector2(extraPlayer.Position.X, extraPlayer.Position.Y + extraYOffset);
						float extraSortY = extraDrawPos.Y + Program.FeetOffsetY;
						var capturedExtraPlayer = extraPlayer;
						var capturedExtraDrawPos = extraDrawPos;
						var capturedExtraSkin = extraPlayer.SkinColorOverride ?? pSkin;
						var capturedExtraHairColor = extraPlayer.HairColorOverride ?? Program.HairColor;
						var capturedExtraEyes = Program.EyesTexture;
						var capturedExtraMouth = Program.MouthTexture;
						var capturedExtraEquipment = extraPlayer.IsNetworkPlayer
						? Program.GetNetworkPlayerEquipmentFull(extraPlayer.Id, extraPlayer.Equip)
						: Program.equipment;
						var capturedExtraUnderwear = Program.LeafUnderpantsTexture;
						var capturedHairBase = hairBase;
						var capturedHairOverlay = hairOverlay;
						//  Rotation de tête et regard propres à CE joueur distant (sinon tous les
						// joueurs distants copiaient la tête/les yeux du joueur qui regarde).
						float capturedExtraHeadAngle = extraPlayer.IsNetworkPlayer ? extraPlayer.HeadAngle : headAngle;
						var capturedExtraGaze = extraPlayer.IsNetworkPlayer
							? Program.ComputeGazeOffsetsFor(capturedExtraDrawPos, extraPlayer.LookWorldPos, capturedExtraPlayer.Facing)
							: (Program.PupilLeftOffset, Program.PupilRightOffset, Program.EyebrowLeftOffset, Program.EyebrowRightOffset);
						underwaterRenderItems.Add(new RenderItem
						{
							Y = extraSortY,
							DrawAction = () =>
							{
								int hairIndex = capturedExtraPlayer.HairStyleOverride ?? Program.PlayerHairStyle;
								if (hairIndex < 0 || hairIndex >= capturedHairBase.Count) hairIndex = 0;
								Texture2D hairBaseTex = capturedHairBase.Count > 0 ? capturedHairBase[hairIndex] : new Texture2D();
								Texture2D hairOverlayTex = capturedHairOverlay.Count > 0 ? capturedHairOverlay[hairIndex] : new Texture2D();
								Texture2D extraHeldItemTex = EntityRenderer.GetHeldItemTextureById(capturedExtraPlayer.HeldItemId);
								EntityRenderer.DrawEntity(
									capturedExtraPlayer.SpeciesOverride ?? "human",
									capturedExtraPlayer.AnimState,
									capturedExtraPlayer.AnimFrame,
									capturedExtraPlayer.AnimProg,
									capturedExtraPlayer.Facing,
									capturedExtraDrawPos,
									capturedExtraSkin,
									hairBaseTex,
									hairOverlayTex,
									capturedExtraHairColor,
									SpeciesData.Skeletons,
									capturedExtraEyes,
									capturedExtraMouth,
									customScale: 1.0f,
									equipment: capturedExtraEquipment,
									isCarrying: false,
									inWater: true,
									attackSwingProgress: capturedExtraPlayer.AttackSwingProgress,
									heldItemTexture: extraHeldItemTex,
									headAngle: capturedExtraHeadAngle,
									keepItemHorizontal: false,
									isBow: false,
									underwearTexture: capturedExtraUnderwear,
									beardStyle: capturedExtraPlayer.BeardStyleOverride ?? 0,
									eyeStyleOverride: capturedExtraPlayer.EyeStyleOverride,
									eyeColorOverride: capturedExtraPlayer.EyeColorOverride,
									pupilLeftOffset: capturedExtraGaze.Item1,
									pupilRightOffset: capturedExtraGaze.Item2,
									eyebrowLeftOffset: capturedExtraGaze.Item3,
									eyebrowRightOffset: capturedExtraGaze.Item4);
							}
						});
					}
				}

				// Ajouter le joueur
				if (!Program.isCarMode)
				{
					float playerYOffset = -playerDepth * 3f;
					
					Item? heldItem = null;
					var hotbarSlots = Program.GetHotbarSlotsCached(); //  OPTIM : cache par frame
					if (Program.hotbarSlot < hotbarSlots.Count && !hotbarSlots[Program.hotbarSlot].IsEmpty)
						heldItem = hotbarSlots[Program.hotbarSlot].Item;
					
					int hairIndex = Program.PlayerHairStyle;
					if (hairIndex < 0 || hairIndex >= hairBase.Count) hairIndex = 0;
					var playerHairBaseTex = hairBase.Count > 0 ? hairBase[hairIndex] : new Texture2D();
					var playerHairOverlayTex = hairOverlay.Count > 0 ? hairOverlay[hairIndex] : new Texture2D();
					
					float underwaterElevationOffset = IsUnderwater ? -Program.GetUnderwaterElevation() : 0f;
					Vector2 actualDrawPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset + underwaterElevationOffset);
					float playerSortY = actualDrawPos.Y + Program.FeetOffsetY;
					
					Texture2D heldItemTex = default;
					string heldItemAnchor = "rarmbottom";
					if (heldItem != null)
					{
						var heldItemInfo = EntityRenderer.GetHeldItemTextureInfo(heldItem);
						heldItemTex = heldItemInfo.Texture;
						heldItemAnchor = heldItemInfo.AnchorPart;
					}

					var mountedAnimal = Program.MountedAnimal;
					// Le sens du cavalier suit toujours celui de la monture.
					float underwaterPlayerFacing = mountedAnimal != null ? mountedAnimal.Facing : facing;

					underwaterRenderItems.Add(new RenderItem
					{
						Y = playerSortY,
						DrawAction = () =>
						{
							// Ombre sur le fond
							int currentFloorDepth = GetSeaFloorDepth(playerTileX, playerTileY);
							float distToFloor = currentFloorDepth - playerDepth;
							if (distToFloor < 8f)
							{
								Raylib.DrawEllipse((int)actualDrawPos.X, (int)(actualDrawPos.Y + Program.FeetOffsetY), 20, 6, new Color(0,0,0,60));
							}
							
							EntityRenderer.DrawEntity(
								"human", anim, frame, prog, underwaterPlayerFacing,
								actualDrawPos, pSkin,
								playerHairBaseTex, playerHairOverlayTex, Program.HairColor,
								SpeciesData.Skeletons, Program.EyesTexture, Program.MouthTexture,
								customScale: 1.0f, equipment: Program.equipment,
								isCarrying: false, inWater: true,
								attackSwingProgress: Program.GetAttackSwingProgress(),
								heldItemTexture: heldItemTex,
								headAngle: headAngle,
								keepItemHorizontal: false,
								isBow: false,
								underwearTexture: Program.LeafUnderpantsTexture,
								drawMount: mountedAnimal != null ? () =>
								{
									var m = mountedAnimal;
									if (!m.IsAlive) return;
									Texture2D mHairBase = hairBase.Count > m.HairStyle ? hairBase[m.HairStyle] : new Texture2D();
									Texture2D mHairOverlay = hairOverlay.Count > m.HairStyle ? hairOverlay[m.HairStyle] : new Texture2D();
									EntityRenderer.DrawEntity(
										m.Species, m.AnimState, m.CurrentFrame, m.AnimProgress,
										m.Facing, m.WorldPos, m.Tint,
										mHairBase, mHairOverlay, m.HairColor,
										SpeciesData.Skeletons,
										eyes: EntityRenderer.GetEyesTextureForSpecies(m.Species),
										mouth: Program.GetMouthTextureFor(m), forceBlinking: m.IsBlinking,
										customScale: m.Scale,
										equipment: m.Equipment,
										isCarrying: false, inWater: m.IsInWater,
										attackSwingProgress: 0f,
										flashWhite: m.DamageFlash > 0f,
										pupilLeftOffset: m.PupilLeftOffset,
										pupilRightOffset: m.PupilRightOffset,
										eyebrowLeftOffset: m.EyebrowLeftOffset,
										eyebrowRightOffset: m.EyebrowRightOffset,
										randomFeatureVariant: m.RandomFeatureVariant,
										randomFeatureColor: m.RandomFeatureColor,
										beardStyle: m.BeardStyle);
								} : null);
						}
					});
				}

				// Tri et dessin des entités
				//  Même correctif que pour le rendu de surface : départage stable des égalités de Y
				// par index d'insertion, pour éviter le clignotement de sprites à Y identique.
				for (int ri = 0; ri < underwaterRenderItems.Count; ri++)
				{
					var tmp = underwaterRenderItems[ri];
					tmp.SortIndex = ri;
					underwaterRenderItems[ri] = tmp;
				}
				underwaterRenderItems.Sort((a, b) =>
				{
					int c = a.Y.CompareTo(b.Y);
					if (c != 0) return c;
					return a.SortIndex.CompareTo(b.SortIndex);
				});
				foreach (var item in underwaterRenderItems) item.DrawAction();
				_renderItemsPool = underwaterRenderItems;

				// Voile d'eau par-dessus les entités
				byte waterAlpha = (byte)(Math.Clamp(playerDepth * 3, 20, 220));
				Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)20, (byte)60, (byte)100, waterAlpha));
				
				// Obscurité supplémentaire en grande profondeur
				if (playerDepth > 30f)
				{
					byte deepAlpha = (byte)(Math.Clamp((playerDepth - 30f) * 4, 0, 100));
					Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)0, (byte)0, (byte)10, deepAlpha));
				}
				
				// Particules sous-marines
				float particleChance = playerDepth * 0.02f;
				if (Random.Shared.NextDouble() < particleChance)
				{
					int px = Random.Shared.Next(0, sw);
					int py = Random.Shared.Next(0, sh);
					int pSize = Random.Shared.Next(1, 3);
					byte pAlpha = (byte)Random.Shared.Next(30, 100);
					Raylib.DrawCircle(px, py, pSize, new Color((byte)200, (byte)220, (byte)255, pAlpha));
				}
				
				// Interface sous-marine
				string depthText = $"Profondeur: {playerDepth:F1}m";
				FontManager.DrawText(depthText, 10, 10, 18, new Color(100, 200, 255, 255));
				
				string helpText = "ESPACE: Nager vers le haut | WASD: Se deplacer";
				FontManager.DrawText(helpText, 10, 35, 14, new Color(150, 200, 255, 200));
				
				// Indicateur de proximite du fond
				int currentFloorDepth = GetSeaFloorDepth(playerTileX, playerTileY);
				float distToCurrentFloor = currentFloorDepth - playerDepth;
				if (distToCurrentFloor < 10f && distToCurrentFloor > 0f)
				{
					string floorText = $"Fond: {distToCurrentFloor:F1}m";
					FontManager.DrawText(floorText, 10, 60, 14, new Color(200, 200, 150, 200));
				}

				return;
			}
			// ========== FIN DU RENDU SOUS-MARIN ==========

			// Shader d'eau (caustiques procédurales) : horloge mise à jour une fois par
			// frame, avant la boucle de tuiles qui va s'en servir pour chaque tuile d'eau.
			UpdateWaterShaderTime(currentTime);

			// =================================================================================
			// 1. SOL, DÉCORATIONS, BORDURES, FALAISES (tout en une seule boucle)
			// =================================================================================
			for (int ix = 0; ix < width; ix++)
			{
				int worldX = startX + ix;
				for (int iy = 0; iy < heightTiles; iy++)
				{
					int worldY = startY + iy;
					if (!Program.IsTileLit(worldX, worldY))
					{
						EnsureWaterShaderInactive();
						continue;
					}
					int height = heights[ix, iy];
					int drawY = worldY * ts - (height * ts / 4);
					int groundId = groundIds[ix, iy];
					int objectId = objectIds[ix, iy];

					// Filtre intérieur
					if (_isPlayerIndoors)
					{
						int tileBld = buildingIds[ix, iy];
						if (!_currentIndoorGroupIds.Contains(tileBld)) continue;
					}

					bool hideGround = false;
					if (isWallToHide[ix, iy] && _isPlayerIndoors)
					{
						int tileAboveY = worldY - 1;
						if (tileAboveY >= startY && tileAboveY <= endY)
						{
							int aboveOverlay = GetOverlayAt(worldX, tileAboveY);
							if (aboveOverlay == 82) hideGround = true;
						}
					}

					if (hideGround) EnsureWaterShaderInactive();

					if (!hideGround)
					{
						// Sol
						if (groundId == 10)
						{
							// Ancien rendu : une texture d'eau statique par tuile, teintée
							// uniquement par l'alpha (GetWaterColor). Remplacé par un shader
							// procédural (voir World.Water.cs / shaders/water.fs) : deux
							// couches de bruit qui défilent forment les caustiques, un
							// troisième bruit très lent les fait "flotter" doucement — plus
							// aucune texture d'eau n'est nécessaire.
							byte waterAlphaTile = GetWaterColor(worldX, worldY, currentTime).A;
							EnsureWaterShaderActive();
							DrawWaterTileShader(worldX, worldY, drawY, ts, waterAlphaTile);
						}
						else
						{
							Texture2D tex = GetTileTexture(worldX, worldY, groundId, currentTime);
							if (tex.Id == 0) tex = tileTextures.GetValueOrDefault(groundId);

							// On quitte le mode shader d'eau avant de dessiner une texture
							// normale, sinon le fragment shader de l'eau s'appliquerait
							// aussi à cette texture.
							EnsureWaterShaderInactive();

							if (tex.Id == 0 && missingTex.Id != 0) tex = missingTex;
							if (tex.Id != 0)
							{
								bool isLegacyRail = groundId == 123;
								Raylib.DrawTexturePro(tex,
									new Rectangle(0, 0, tex.Width, tex.Height),
									isLegacyRail
										? new Rectangle(worldX * ts + ts / 2f, drawY + ts / 2f, ts, ts)
										: new Rectangle(worldX * ts, drawY, ts, ts),
									isLegacyRail ? new Vector2(ts / 2f, ts / 2f) : Vector2.Zero,
									isLegacyRail ? GetRailwayRotation(worldX, worldY) : 0, Color.White);
							}
							else
							{
								Raylib.DrawRectangle(worldX * ts, drawY, ts, ts, new Color(255, 0, 220, 200));
							}
						}

						// Zones de maisons visibles avec le mode debug (F3).
						if (showDebug && buildingIds[ix, iy] != 0)
							Raylib.DrawRectangle(worldX * ts, drawY, ts, ts, new Color(170, 70, 230, 85));

						// Décoration
						int decoIdx = decorationIndex[ix, iy];
						if (decoIdx >= 0 && WorldTileRegistry.DecorationTextures.TryGetValue(groundId, out var decoTexList) && decoIdx < decoTexList.Count)
						{
							Texture2D decoTex = decoTexList[decoIdx];
							if (decoTex.Id != 0)
							{
								Raylib.DrawTexturePro(decoTex,
									new Rectangle(0, 0, decoTex.Width, decoTex.Height),
									new Rectangle(worldX * ts, drawY, ts, ts),
									Vector2.Zero, 0, Color.White);
							}
						}
					}

					// Falaises
					if (!_isUnderground && !_isPlayerIndoors)
					{
						// Bas
						if (iy + 1 < heightTiles)
						{
							int downHeight = heights[ix, iy + 1];
							int diff = height - downHeight;
							if (diff > 0)
								DrawCliffDown(worldX, worldY, diff, ts, cliffDownTex);
						}
						// Gauche
						if (ix - 1 >= 0)
						{
							int leftHeight = heights[ix - 1, iy];
							if (height > leftHeight)
								DrawCliffBorder(worldX, worldY, -1, 0, ts, cliffBorderTex);
						}
						// Haut
						if (iy - 1 >= 0)
						{
							int upHeight = heights[ix, iy - 1];
							if (height > upHeight)
								DrawCliffBorder(worldX, worldY, 0, -1, ts, cliffBorderTex);
						}
						// Droite
						if (ix + 1 < width)
						{
							int rightHeight = heights[ix + 1, iy];
							if (height > rightHeight)
								DrawCliffBorder(worldX, worldY, 1, 0, ts, cliffBorderTex);
						}
					}
				}
			}

			// Sécurité : au cas où la dernière tuile dessinée était de l'eau,
			// on referme le mode shader avant de continuer le rendu (bordures,
			// décorations, entités...), sinon tout le reste serait dessiné avec
			// le fragment shader de l'eau.
			EnsureWaterShaderInactive();

			// =================================================================================
			// BORDURES DE BIOMES (dessinées après toutes les tuiles)
			// =================================================================================
			if (!_isUnderground && !_isPlayerIndoors)
			{
				for (int bx = 0; bx < width; bx++)
				{
					int wx = startX + bx;
					for (int by = 0; by < heightTiles; by++)
					{
						int wy = startY + by;
						int currentGround = groundIds[bx, by];
						
						// Droite
						if (bx + 1 < width)
						{
							int rightGround = groundIds[bx + 1, by];
							if (rightGround != currentGround && HasBiomeBorder(currentGround))
							{
								int currentPrio = GetBiomePriority(currentGround);
								int rightPrio = GetBiomePriority(rightGround);
								if (currentPrio > rightPrio)
								{
									Texture2D borderTex = GetBiomeBorderTexture(currentGround, wx, wy, currentTime);
									if (borderTex.Id != 0)
									{
										int neighborHeight = heights[bx + 1, by];
										int neighborDrawY = (wy) * ts - (neighborHeight * ts / 4);
										// PAS de flip (texture orientée pour la droite)
										Raylib.DrawTexturePro(borderTex,
											new Rectangle(0, 0, borderTex.Width, borderTex.Height),
											new Rectangle((wx + 1) * ts, neighborDrawY, ts, ts),
											Vector2.Zero, 0, Color.White);
									}
								}
							}
						}
						
						// Gauche
						if (bx - 1 >= 0)
						{
							int leftGround = groundIds[bx - 1, by];
							if (leftGround != currentGround && HasBiomeBorder(currentGround))
							{
								int currentPrio = GetBiomePriority(currentGround);
								int leftPrio = GetBiomePriority(leftGround);
								if (currentPrio > leftPrio)
								{
									Texture2D borderTex = GetBiomeBorderTexture(currentGround, wx, wy, currentTime);
									if (borderTex.Id != 0)
									{
										int neighborHeight = heights[bx - 1, by];
										int neighborDrawY = (wy) * ts - (neighborHeight * ts / 4);
										// Flip horizontal
										Raylib.DrawTexturePro(borderTex,
											new Rectangle(borderTex.Width, 0, -borderTex.Width, borderTex.Height),
											new Rectangle((wx - 1) * ts, neighborDrawY, ts, ts),
											Vector2.Zero, 0, Color.White);
									}
								}
							}
						}
						
						// Bas
						if (by + 1 < heightTiles)
						{
							int bottomGround = groundIds[bx, by + 1];
							if (bottomGround != currentGround && HasBiomeBorder(currentGround))
							{
								int currentPrio = GetBiomePriority(currentGround);
								int bottomPrio = GetBiomePriority(bottomGround);
								if (currentPrio > bottomPrio)
								{
									Texture2D borderTex = GetBiomeBorderTexture(currentGround, wx, wy, currentTime);
									if (borderTex.Id != 0)
									{
										int neighborHeight = heights[bx, by + 1];
										int neighborDrawY = (wy + 1) * ts - (neighborHeight * ts / 4);
										
										// Rotation de 90° autour du centre
										float centerX = wx * ts + ts / 2f;
										float centerY = neighborDrawY + ts / 2f;
										
										Raylib.DrawTexturePro(borderTex,
											new Rectangle(0, 0, borderTex.Width, borderTex.Height),
											new Rectangle(centerX, centerY, ts, ts),
											new Vector2(ts / 2f, ts / 2f), 90f, Color.White);
									}
								}
							}
						}
						
						// Haut
						if (by - 1 >= 0)
						{
							int topGround = groundIds[bx, by - 1];
							if (topGround != currentGround && HasBiomeBorder(currentGround))
							{
								int currentPrio = GetBiomePriority(currentGround);
								int topPrio = GetBiomePriority(topGround);
								if (currentPrio > topPrio)
								{
									Texture2D borderTex = GetBiomeBorderTexture(currentGround, wx, wy, currentTime);
									if (borderTex.Id != 0)
									{
										int neighborHeight = heights[bx, by - 1];
										int neighborDrawY = (wy - 1) * ts - (neighborHeight * ts / 4);
										float centerX = wx * ts + ts / 2f;
										float centerY = neighborDrawY + ts / 2f;
										
										// Essayer 90° au lieu de -90°
										Raylib.DrawTexturePro(borderTex,
											new Rectangle(0, 0, borderTex.Width, borderTex.Height),
											new Rectangle(centerX, centerY, ts, ts),
											new Vector2(ts / 2f, ts / 2f), -90f, Color.White);
									}
								}
							}
						}
					}
				}
			}

			// =================================================================================
			// 2. BATEAUX (tuiles + voile) - Version unique et optimisée
			// =================================================================================
			foreach (var boat in World.GetAllBoats().Values)
			{
				if (boat == null) continue;
				
				if (_isPlayerIndoors)
				{
					int tileOverlay = GetBuildingIdAt((int)(boat.Position.X / ts), (int)(boat.Position.Y / ts));
					if (!_currentIndoorGroupIds.Contains(tileOverlay))
						continue;
				}
				
				// 2. Dessiner toutes les tuiles du bateau (radeau)
				foreach (var (relX, relY) in boat.Tiles.Keys)
				{
					Vector2 boatWorldPos = boat.GetTileWorldPos(relX, relY);
					int boatTileX = (int)(boatWorldPos.X / ts);
					int boatTileY = (int)(boatWorldPos.Y / ts);
					if (!Program.IsTileLit(boatTileX, boatTileY)) continue;
					int boatHeight = GetHeightAt(boatTileX, boatTileY);
					float boatYOffset = -boatHeight * ts / 4;
					float boatDrawX = boatWorldPos.X - ts/2f;
					float boatDrawY = boatWorldPos.Y - ts/2f + boatYOffset;
					
					Texture2D boatTex = WorldTileRegistry.GetFirstTexture(500);
					if (boatTex.Id != 0)
					{
						Raylib.DrawTexturePro(boatTex,
							new Rectangle(0, 0, boatTex.Width, boatTex.Height),
							new Rectangle(boatDrawX, boatDrawY, ts, ts),
							Vector2.Zero, 0, Color.White);
					}
					
					// Connexions du radeau
					if (WorldTileRegistry.IsConnectable(500))
					{
						if (boat.Tiles.ContainsKey((relX - 1, relY)) && Program.IsTileLit(boatTileX - 1, boatTileY))
						{
							Texture2D connTex = WorldTileRegistry.GetConnectionTexture(500, "left");
							if (connTex.Id != 0)
							{
								Raylib.DrawTexturePro(connTex,
									new Rectangle(0, 0, connTex.Width, connTex.Height),
									new Rectangle(boatDrawX, boatDrawY, ts, ts),
									Vector2.Zero, 0, Color.White);
							}
						}
						if (boat.Tiles.ContainsKey((relX + 1, relY)) && Program.IsTileLit(boatTileX + 1, boatTileY))
						{
							Texture2D connTex = WorldTileRegistry.GetConnectionTexture(500, "right");
							if (connTex.Id != 0)
							{
								Raylib.DrawTexturePro(connTex,
									new Rectangle(0, 0, connTex.Width, connTex.Height),
									new Rectangle(boatDrawX, boatDrawY, ts, ts),
									Vector2.Zero, 0, Color.White);
							}
						}
						if (boat.Tiles.ContainsKey((relX, relY - 1)) && Program.IsTileLit(boatTileX, boatTileY - 1))
						{
							Texture2D connTex = WorldTileRegistry.GetConnectionTexture(500, "up");
							if (connTex.Id != 0)
							{
								Raylib.DrawTexturePro(connTex,
									new Rectangle(0, 0, connTex.Width, connTex.Height),
									new Rectangle(boatDrawX, boatDrawY, ts, ts),
									Vector2.Zero, 0, Color.White);
							}
						}
						if (boat.Tiles.ContainsKey((relX, relY + 1)) && Program.IsTileLit(boatTileX, boatTileY + 1))
						{
							Texture2D connTex = WorldTileRegistry.GetConnectionTexture(500, "down");
							if (connTex.Id != 0)
							{
								Raylib.DrawTexturePro(connTex,
									new Rectangle(0, 0, connTex.Width, connTex.Height),
									new Rectangle(boatDrawX, boatDrawY, ts, ts),
									Vector2.Zero, 0, Color.White);
							}
						}
					}
				}
			}

			// =================================================================================
			// 3. INITIALISATION DE LA LISTE DE RENDU (RenderItems)
			// =================================================================================
			if (_renderItemsPoolInUse)
				_renderItemsPool.Clear();
			else
				_renderItemsPoolInUse = true;
			var renderItems = _renderItemsPool;

			// =================================================================================
			// 4. ICÔNES DES PROPRIÉTAIRES DE MAISON SUR LES PORTES
			// =================================================================================
			AddHouseResidentDoorIcons(renderItems, entities);

			// =================================================================================
			// 5. ITEMS AU SOL
			// =================================================================================
			foreach (var gi in Program.GroundItems)
			{
				if (!IsEntityNearScreen(gi.Position))
					continue;
					if (!Program.IsTileLit((int)(gi.Position.X / ts), (int)(gi.Position.Y / ts)))
						continue;
				if (!GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData))
					continue;
				const float itemSize = 28f;
				Color itemColor = gi.CustomColor.HasValue ? gi.CustomColor.Value : itemData.Color;
				bool isLootbag = gi.ItemId == Program.GetItemId("Sac en osier") || !string.IsNullOrWhiteSpace(gi.LootbagPayload);
				float shadowWidth = isLootbag && Program.LootbagTexture.Id != 0
					? ItemRenderer.GetTextureAlphaWidth(Program.LootbagTexture, "lootbag") * itemSize
					: ItemRenderer.GetItemAlphaWidth(itemData) * itemSize;
				float alphaBottom = isLootbag && Program.LootbagTexture.Id != 0
					? ItemRenderer.GetTextureAlphaBottom(Program.LootbagTexture, "lootbag")
					: ItemRenderer.GetItemAlphaBottom(itemData);
				float shadowY = gi.Position.Y + 5f;
				Vector2 drawPos = new Vector2(gi.Position.X, shadowY - gi.Height + itemSize / 2f - alphaBottom * itemSize);
				
				renderItems.Add(new RenderItem
				{
					Y = gi.Position.Y,
					Kind = RenderItemKind.GroundItem,
					Tag = new GroundItemRenderTag(gi, itemData, itemColor, isLootbag, shadowWidth, alphaBottom, gi.GroundVelocity.X * 0.5f, drawPos, itemSize, gi.Metadata, gi.CustomColor)
				});
			}

			//  MULTIJOUEUR : côté client, Program.GroundItems reste vide (le host est
			// seul propriétaire de la liste) ; on affiche à la place le dernier instantané
			// reçu du réseau.
			if (NetworkManager.IsClient)
			{
				foreach (var ngi in NetworkManager.GetRemoteGroundItems())
				{
					if (!IsEntityNearScreen(new Vector2(ngi.PosX, ngi.PosY)))
						continue;
					if (!Program.IsTileLit((int)(ngi.PosX / ts), (int)(ngi.PosY / ts)))
						continue;
					if (!GameData.ItemDatabase.TryGetValue(ngi.ItemId, out var netItemData)) continue;
					Color? netCustomColor = (ngi.Colors != null && ngi.Colors.Count > 0 && ngi.Colors[0] != null)
						? new Color(ngi.Colors[0]!.R, ngi.Colors[0]!.G, ngi.Colors[0]!.B, ngi.Colors[0]!.A)
						: (Color?)null;
					Color netItemColor = netCustomColor ?? netItemData.Color;
					const float itemSize = 28f;
					bool isLootbag = ngi.ItemId == Program.GetItemId("Sac en osier");
					float shadowWidth = isLootbag && Program.LootbagTexture.Id != 0
						? ItemRenderer.GetTextureAlphaWidth(Program.LootbagTexture, "lootbag") * itemSize
						: ItemRenderer.GetItemAlphaWidth(netItemData) * itemSize;
					float alphaBottom = isLootbag && Program.LootbagTexture.Id != 0
						? ItemRenderer.GetTextureAlphaBottom(Program.LootbagTexture, "lootbag")
						: ItemRenderer.GetItemAlphaBottom(netItemData);
					float shadowY = ngi.PosY + 5f;
					Vector2 drawPos = new Vector2(ngi.PosX, shadowY + itemSize / 2f - alphaBottom * itemSize);

					renderItems.Add(new RenderItem
					{
						Y = ngi.PosY,
						DrawAction = () =>
						{
							float size = itemSize;
							Raylib.DrawEllipse((int)ngi.PosX, (int)shadowY, shadowWidth / 2f, 4, new Color(0, 0, 0, 80));

							if (isLootbag && Program.LootbagTexture.Id != 0)
							{
								Rectangle srcRect = new Rectangle(0, 0, Program.LootbagTexture.Width, Program.LootbagTexture.Height);
								Rectangle destRect = new Rectangle(drawPos.X, drawPos.Y, size, size);
								Vector2 origin = new Vector2(size / 2, size / 2);
								Raylib.DrawTexturePro(Program.LootbagTexture, srcRect, destRect, origin, 0, netItemColor);
							}
							else
							{
								//  Dessine TOUTES les couches de l'item, pas seulement la première.
								// Teinte uniquement si l'item a réellement été teint (netCustomColor).
								ItemRenderer.DrawItemRotated(netItemData, drawPos, size, 0, netCustomColor, ngi.Metadata);
							}

						}
					});
				}
			}

			// =================================================================================
			// 6. OBJETS POSÉS SUR LES BATEAUX (coffres, porte-armures, etc.)
			// =================================================================================
			foreach (var boat in World.GetAllBoats().Values)
			{
				foreach (var (relX, relY) in boat.Tiles.Keys)
				{
					int boatObjectId = boat.GetObjectIdAt(relX, relY);
					if (boatObjectId == 0) continue;
					
					var boatTileData = WorldTileRegistry.GetTile(boatObjectId);
					if (boatTileData == null) continue;
					
					if (_isPlayerIndoors)
					{
						int tileOverlay = GetBuildingIdAt(
							(int)(boat.GetTileWorldPos(relX, relY).X / ts),
							(int)(boat.GetTileWorldPos(relX, relY).Y / ts)
						);
						if (!_currentIndoorGroupIds.Contains(tileOverlay))
							continue;
					}
					
					Vector2 tileWorldPos = boat.GetTileWorldPos(relX, relY);
					int tileX = (int)(tileWorldPos.X / ts);
					int tileY = (int)(tileWorldPos.Y / ts);
					if (!Program.IsTileLit(tileX, tileY)) continue;
					int height = GetHeightAt(tileX, tileY);
					float yOffset = -height * ts / 4;
					
					float drawX = tileWorldPos.X - ts/2f;
					float drawY = tileWorldPos.Y - ts/2f + yOffset;
					int drawW = ts, drawH = ts;
					int offX = 0, offY = 0;
					
					if (boatTileData.Size != null)
					{
						drawW = ts * boatTileData.Size.Width;
						drawH = ts * boatTileData.Size.Height;
						if (boatTileData.DrawOffset != null)
						{
							offX = boatTileData.DrawOffset.X;
							offY = boatTileData.DrawOffset.Y;
						}
						drawY = drawY - drawH + ts;
					}
					
					float sortY = tileWorldPos.Y + yOffset;
					
					var capturedBoat = boat;
					var capturedRelX = relX;
					var capturedRelY = relY;
					var capturedObjectId = boatObjectId;
					var capturedTileData = boatTileData;
					var capturedDrawX = drawX;
					var capturedDrawY = drawY;
					var capturedDrawW = drawW;
					var capturedDrawH = drawH;
					var capturedOffX = offX;
					var capturedOffY = offY;
					
					renderItems.Add(new RenderItem
					{
						Y = sortY,
						SortLayer = capturedTileData.OnFloor ? -1 : 0,
						DrawAction = () =>
						{
							Texture2D objTex = GetTileTexture(
								(int)(capturedBoat.GetTileWorldPos(capturedRelX, capturedRelY).X / ts),
								(int)(capturedBoat.GetTileWorldPos(capturedRelX, capturedRelY).Y / ts),
								capturedObjectId, currentTime);
							
							if (objTex.Id != 0)
							{
								Raylib.DrawTexturePro(objTex,
									new Rectangle(0, 0, objTex.Width, objTex.Height),
									new Rectangle(capturedDrawX + capturedOffX, capturedDrawY + capturedOffY, 
												 capturedDrawW, capturedDrawH),
									Vector2.Zero, 0, Color.White);
							}
							
							// Connexions pour les objets connectables sur le bateau
							if (WorldTileRegistry.IsConnectable(capturedObjectId))
							{
								if (capturedBoat.Tiles.ContainsKey((capturedRelX - 1, capturedRelY)) && 
									capturedBoat.GetObjectIdAt(capturedRelX - 1, capturedRelY) == capturedObjectId)
								{
									Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "left");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex,
											new Rectangle(0, 0, connTex.Width, connTex.Height),
											new Rectangle(capturedDrawX + capturedOffX, capturedDrawY + capturedOffY, 
														 capturedDrawW, capturedDrawH),
											Vector2.Zero, 0, Color.White);
								}
								if (capturedBoat.Tiles.ContainsKey((capturedRelX + 1, capturedRelY)) && 
									capturedBoat.GetObjectIdAt(capturedRelX + 1, capturedRelY) == capturedObjectId)
								{
									Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "right");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex,
											new Rectangle(0, 0, connTex.Width, connTex.Height),
											new Rectangle(capturedDrawX + capturedOffX, capturedDrawY + capturedOffY, 
														 capturedDrawW, capturedDrawH),
											Vector2.Zero, 0, Color.White);
								}
								if (capturedBoat.Tiles.ContainsKey((capturedRelX, capturedRelY - 1)) && 
									capturedBoat.GetObjectIdAt(capturedRelX, capturedRelY - 1) == capturedObjectId)
								{
									Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "up");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex,
											new Rectangle(0, 0, connTex.Width, connTex.Height),
											new Rectangle(capturedDrawX + capturedOffX, capturedDrawY + capturedOffY, 
														 capturedDrawW, capturedDrawH),
											Vector2.Zero, 0, Color.White);
								}
								if (capturedBoat.Tiles.ContainsKey((capturedRelX, capturedRelY + 1)) && 
									capturedBoat.GetObjectIdAt(capturedRelX, capturedRelY + 1) == capturedObjectId)
								{
									Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "down");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex,
											new Rectangle(0, 0, connTex.Width, connTex.Height),
											new Rectangle(capturedDrawX + capturedOffX, capturedDrawY + capturedOffY, 
														 capturedDrawW, capturedDrawH),
											Vector2.Zero, 0, Color.White);
								}
							}
						}
					});
				}
			}

			// =================================================================================
			// 7. OBJETS DU MONDE (arbres, rochers, maisons, etc.)
			// =================================================================================
			for (int ix = 0; ix < width; ix++)
			{
				int worldX = startX + ix;
				for (int iy = 0; iy < heightTiles; iy++)
				{
					int worldY = startY + iy;
					int objectId = objectIds[ix, iy];
					if (objectId == 0) continue;
					if (!Program.IsTileLit(worldX, worldY)) continue;

					// Cercle rituel (traitement spécial)
					if (objectId == 4000)
					{
						int ritualHeight = heights[ix, iy];
						float ritualWx = worldX * ts;
						float ritualWy = worldY * ts - (ritualHeight * ts / 4);
						var ritualTileData = WorldTileRegistry.GetTile(4000);
						if (ritualTileData != null)
						{
							int drawW = ts, drawH = ts;
							int drawX = (int)ritualWx, drawY = (int)ritualWy;
							if (ritualTileData.Size != null)
							{
								drawW = ts * ritualTileData.Size.Width;
								drawH = ts * ritualTileData.Size.Height;
								if (ritualTileData.DrawOffset != null)
								{
									drawX += ritualTileData.DrawOffset.X;
									drawY -= ritualTileData.DrawOffset.Y;
								}
								drawY = (int)(ritualWy - drawH + ts);
							}
							Texture2D tex = GetTileTexture(worldX, worldY, 4000, currentTime);
							if (tex.Id != 0)
							{
								Raylib.DrawTexturePro(tex,
									new Rectangle(0, 0, tex.Width, tex.Height),
									new Rectangle(drawX, drawY, drawW, drawH),
									Vector2.Zero, 0, Color.White);
							}
						}
						continue;
					}

					var tileData = WorldTileRegistry.GetTile(objectId);
					if (tileData == null) continue;

					string key = $"{worldX}_{worldY}";
					if (destroyed.Contains(key)) continue;

					if (_isPlayerIndoors)
					{
						int tileBld = buildingIds[ix, iy];
						if (!_currentIndoorGroupIds.Contains(tileBld)) continue;
					}

					int height = heights[ix, iy];
					float wx = worldX * ts, wy = worldY * ts - (height * ts / 4);
					objectHP.TryGetValue(key, out var hp);
					//  On utilise l'index LOCAL "ix" (petit, borné à la largeur de la fenêtre visible)
					// plutôt que "worldX" (coordonnée monde potentiellement grande) pour ce micro-décalage
					// de départage : ajouté à un "wy" de grande magnitude, un décalage basé sur worldX
					// pouvait être totalement "avalé" par l'imprécision du float (perte de précision),
					// ce qui faisait redevenir deux sprites voisins strictement égaux en Y. Le départage
					// final et déterministe reste de toute façon assuré par SortIndex au moment du tri.
					float sortY = wy + ts + ix * 0.001f;

					//  CRITIQUE : Créer des COPIES LOCALES pour la lambda 
					int capturedX = worldX;
					int capturedY = worldY;
					float capturedWx = wx;
					float capturedWy = wy;
					var capturedTileData = tileData;
					var capturedHp = hp;
					int capturedObjectId = objectId;
					int capturedWallCovering = wallCoveringIds[ix, iy];
					int capturedIx = ix;
					int capturedIy = iy;

					bool isArmorStand = capturedTileData.IsArmorStand == true;
					ArmorStandData? standData = null;
					if (isArmorStand)
					{
						var chunk = GetChunkAt(capturedX, capturedY);
						standData = chunk?.GetArmorStandAt(capturedX, capturedY);
						if (standData == null) standData = new ArmorStandData();
					}

					bool isBannerHolder = (capturedObjectId == 7000);
					BannerHolderData? capturedBannerData = null;
					if (isBannerHolder)
					{
						var bChunk = GetChunkAt(capturedX, capturedY);
						capturedBannerData = bChunk?.GetBannerHolderAt(capturedX, capturedY);
					}

					renderItems.Add(new RenderItem
					{
						Y = sortY,
						DrawAction = () =>
						{
							if (capturedObjectId == 123)
							{
								Texture2D railTex = GetTileTexture(capturedX, capturedY, capturedObjectId, currentTime);
								if (railTex.Id != 0)
								{
									Raylib.DrawTexturePro(railTex, new Rectangle(0, 0, railTex.Width, railTex.Height),
										new Rectangle(capturedWx + ts / 2f, capturedWy + ts / 2f, ts, ts),
										new Vector2(ts / 2f, ts / 2f), GetRailwayRotation(capturedX, capturedY), Color.White);
								}
							}
							else if (capturedObjectId == 124)
							{
								if (!Program.IsWagonMountedAt(capturedX, capturedY))
									Program.DrawWagon(new Vector2(capturedWx + ts / 2f, capturedWy + ts / 2f)
										+ Program.GetWagonDrawOffset(capturedX, capturedY), ts,
										Program.IsWagonDrawnVertical(capturedX, capturedY));
							}
							else if (isArmorStand)
							{
								Vector2 standPos = new Vector2(capturedWx + ts/2f, capturedWy + ts/2f);
								var tempEquipment = EntityRenderer.GetCachedEquipmentForArmorStand(standData);
								Texture2D emptyTex = new Texture2D();
								string poseToUse = standData.CurrentPose ?? "idle";
								if (SpeciesData.Skeletons.TryGetValue("armorstand", out var skeleton))
								{
									var firstPart = skeleton.FirstOrDefault();
									if (firstPart != null && !firstPart.Animations.ContainsKey(poseToUse))
										poseToUse = "idle";
								}
								EntityRenderer.DrawEntity(
									"armorstand", poseToUse, 0, 0f, 1f, standPos, Color.White,
									emptyTex, emptyTex, Color.Black, SpeciesData.Skeletons,
									Program.EyesTexture, Program.MouthTexture,
									customScale: 1.0f, equipment: tempEquipment,
									isCarrying: false, inWater: false, attackSwingProgress: 0f,
									heldItemTexture: default, headAngle: 0f);
								if (capturedHp != null && capturedHp.Current < capturedHp.Max)
								{
									float ratio = Math.Clamp((float)capturedHp.Current / Math.Max(1, capturedHp.Max), 0f, 1f);
									float barWidth = ts;
									float barHeight = 7f;
									float barX = capturedWx;
									float barY = capturedWy - ts * 2 - 8;
									Rectangle barRect = new Rectangle(barX, barY, barWidth, barHeight);
									Rectangle fillRect = new Rectangle(barX + 1.5f, barY + 1.5f, Math.Max(0f, (barWidth - 3f) * ratio), barHeight - 3f);
									Raylib.DrawRectangleRounded(new Rectangle(barX - 2f, barY - 2f, barWidth + 4f, barHeight + 4f), 0.45f, 8, new Color(0, 0, 0, 105));
									Raylib.DrawRectangleRounded(barRect, 0.45f, 8, new Color(32, 20, 24, 225));
									if (fillRect.Width > 0f)
									{
										Color fillColor = ratio < 0.3f
											? new Color(225, 65, 58, 255)
											: ratio < 0.6f
												? new Color(238, 165, 58, 255)
												: new Color(90, 210, 105, 255);
										Raylib.DrawRectangleRounded(fillRect, 0.35f, 6, fillColor);
										Raylib.DrawRectangleRounded(
											new Rectangle(fillRect.X + 1f, fillRect.Y + 0.5f, Math.Max(0f, fillRect.Width - 2f), 1.5f),
											0.5f, 6, new Color(255, 255, 255, 85));
									}
									Raylib.DrawRectangleRoundedLines(barRect, 0.45f, 8, 1, new Color(255, 220, 220, 150));
								}
							}
							else if (isBannerHolder) // ── PORTE-BANNIÈRE ──────────────────────────────
							{
								Texture2D tex = GetTileTexture(capturedX, capturedY, capturedObjectId, currentTime);
								if (tex.Id == 0) tex = tileTextures.GetValueOrDefault(capturedObjectId);
								if (tex.Id == 0 && missingTex.Id != 0) tex = missingTex;

								int drawW = ts, drawH = ts;
								int drawX = (int)capturedWx, drawY = (int)capturedWy;
								if (capturedTileData.Size != null)
								{
									drawW = ts * capturedTileData.Size.Width;
									drawH = ts * capturedTileData.Size.Height;
									if (capturedTileData.DrawOffset != null)
									{
										drawX += capturedTileData.DrawOffset.X;
										drawY -= capturedTileData.DrawOffset.Y;
									}
									drawY = (int)(capturedWy - drawH + ts);
								}

								bool isHovered = mouseWorldPos.X >= drawX && mouseWorldPos.X <= drawX + drawW &&
												   mouseWorldPos.Y >= drawY && mouseWorldPos.Y <= drawY + drawH;
								if (tex.Id != 0)
									Raylib.DrawTexturePro(tex,
										new Rectangle(0, 0, tex.Width, tex.Height),
										new Rectangle(drawX, drawY, drawW, drawH),
										Vector2.Zero, 0, Color.White);

								if (capturedBannerData != null && capturedBannerData.HasBanner)
								{
									if (_bannerShapeTextures.TryGetValue(capturedBannerData.ShapeName, out var shapeTex) && shapeTex.Id != 0)
										Raylib.DrawTexturePro(shapeTex,
											new Rectangle(0, 0, shapeTex.Width, shapeTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH),
											Vector2.Zero, 0, capturedBannerData.BackgroundColor);

									if (_bannerPatternTextures.TryGetValue(capturedBannerData.PatternName, out var patTex) && patTex.Id != 0)
										Raylib.DrawTexturePro(patTex,
											new Rectangle(0, 0, patTex.Width, patTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH),
											Vector2.Zero, 0, capturedBannerData.PatternColor);
								}

								if (isHovered)
								{
									string hint = (capturedBannerData?.HasBanner == true)
										? "[E] Décrocher la bannière"
										: "[E] Accrocher la bannière";
									Raylib.DrawText(hint, drawX, drawY - 14, 10, new Color(255, 255, 200, 220));
								}
							}
							else
							{
								Color tint = Color.White;
								if (capturedHp != null && capturedHp.DamageFlash > 0f)
									tint = new Color(255,80,80,255);

								int drawW = ts, drawH = ts;
								int drawX = (int)capturedWx, drawY = (int)capturedWy;
								if (capturedTileData.Size != null)
								{
									drawW = ts * capturedTileData.Size.Width;
									drawH = ts * capturedTileData.Size.Height;
									if (capturedTileData.DrawOffset != null)
									{
										drawX += capturedTileData.DrawOffset.X;
										drawY -= capturedTileData.DrawOffset.Y;
									}
									drawY = (int)(capturedWy - drawH + ts);
								}

								// Récupération de l'overlay une fois pour toute la lambda
								int currentOverlay = overlayIds[capturedIx, capturedIy];

								bool isHovered = mouseWorldPos.X >= drawX && mouseWorldPos.X <= drawX + drawW &&
												 mouseWorldPos.Y >= drawY && mouseWorldPos.Y <= drawY + drawH;
								Texture2D tex = GetTileTexture(capturedX, capturedY, capturedObjectId, currentTime);
								if (tex.Id == 0) tex = tileTextures.GetValueOrDefault(capturedObjectId);
								if (tex.Id == 0 && missingTex.Id != 0) tex = missingTex;

								// ========== AMPOULE ÉLECTRIQUE ==========
								if (capturedObjectId == 408)
								{
									bool isLit = false;
									if (Ampoules.TryGetValue((capturedX, capturedY), out var ampoule))
									{
										isLit = ampoule.IsOn;
									}

									Color bulbTint = isLit ? Color.White : new Color(80, 80, 80, 255);

									if (tex.Id != 0)
									{
										Raylib.DrawTexturePro(tex,
											new Rectangle(0, 0, tex.Width, tex.Height),
											new Rectangle(drawX, drawY, drawW, drawH),
											Vector2.Zero, 0, bulbTint);
									}

									if (isLit)
									{
										Raylib.BeginBlendMode(BlendMode.Additive);
										float pulse = 0.7f + 0.3f * MathF.Sin((float)Raylib.GetTime() * 5f);
										int glowRadius = (int)(drawW * 1.5f * pulse);
										Raylib.DrawCircleGradient((int)(drawX + drawW/2), (int)(drawY + drawH/2), glowRadius,
											new Color(255, 220, 150, 80),
											new Color(255, 220, 150, 0));
										Raylib.EndBlendMode();
									}
								}
								else
								{
									// Dessin normal pour les autres objets
									bool isCollectable = capturedTileData.Collectable;
									if (capturedTileData.IsCrop)
									{
										var cropData = World.GetCropDataAt(capturedX, capturedY);
										if (cropData != null)
										{
											float elapsed = cropData.AccumulatedGrowthTime;
											if (elapsed >= capturedTileData.GrowthTime)
												isCollectable = true;
										}
									}

									if (isHovered && tex.Id != 0 && isCollectable)
									{
										Rectangle destRect = new Rectangle(drawX, drawY, drawW, drawH);
										DrawCachedOutline(tex, destRect, new Color(255,255,255,180), 2);
									}

									bool isWall = IsWallToHide(capturedObjectId);
														bool hasVisibleInteriorTile = HasVisibleInteriorTile(capturedX, capturedY);
									int originalDrawH = drawH;
									int originalDrawY = drawY;
									if (isWall && _isPlayerIndoors && capturedTileData.Size != null)
									{
										int tileAboveY = capturedY - 1;
										if (tileAboveY >= startY && tileAboveY <= endY)
										{
											int aboveOverlay = GetOverlayAt(capturedX, tileAboveY);
											if (aboveOverlay == 82) drawH = 0;
										}
									}

														if (!hasVisibleInteriorTile)
															drawH = 0;

														if (drawH > 0)
								{
									//  Balancement dans le vent (flore uniquement) : on fait pivoter le(s)
									// sprite(s) de la tuile autour du bas de leur hitbox, pas autour du centre.
									// La hitbox est positionnée au sol, donc le pivot doit rester centré
									// horizontalement et aligné sur le bord inférieur de la case.
									Vector2 windOrigin = Vector2.Zero;
									float windAngle = 0f;
									Rectangle windDestRect = new Rectangle(drawX, drawY, drawW, drawH);
									bool isTree = IsLeafBearingTree(capturedObjectId);
									if (capturedTileData.SwaysInWind || isTree)
									{
										if (capturedTileData.SwaysInWind)
										{
											int tileWidth = capturedTileData.Size?.Width ?? 1;
											int tileHeight = capturedTileData.Size?.Height ?? 1;
											windAngle = GetWindSwayAngle(capturedX, capturedY, currentTime, tileWidth, tileHeight);
										}
										windOrigin = new Vector2(drawW / 2f, drawH);
										windDestRect = new Rectangle(drawX + windOrigin.X, drawY + windOrigin.Y, drawW, drawH);
									}
									if (isTree)
										windAngle += GetTreeHitSwayAngle(capturedX, capturedY, currentTime);

									if (capturedObjectId == 4 || capturedObjectId == 45)
									{
										string treeTexturePrefix = capturedObjectId == 45 ? "spruce_tree" : "oak_tree";
										Texture2D trunkTex = WorldTileRegistry.GetTextureByName($"{treeTexturePrefix}_trunk");
										Texture2D bushTex = WorldTileRegistry.GetTextureByName($"{treeTexturePrefix}_bush");
										Color treeTint = GetTreeLeafTintForObjectId(capturedObjectId, capturedX, capturedY);

										if (trunkTex.Id != 0)
										{
											Raylib.DrawTexturePro(trunkTex,
												new Rectangle(0, 0, trunkTex.Width, trunkTex.Height),
												windDestRect,
												windOrigin, windAngle, Color.White);
										}

										if (bushTex.Id != 0)
										{
											Raylib.DrawTexturePro(bushTex,
												new Rectangle(0, 0, bushTex.Width, bushTex.Height),
												windDestRect,
												windOrigin, windAngle, treeTint);
										}
										else if (tex.Id != 0)
										{
											Raylib.DrawTexturePro(tex,
												new Rectangle(0, 0, tex.Width, tex.Height),
												windDestRect,
												windOrigin, windAngle, treeTint);
										}
									}
									else if (capturedTileData.IsSapling &&
										WorldTileRegistry.SaplingTrunkStageTextures.TryGetValue(capturedObjectId, out var saplingTrunkStages) &&
										WorldTileRegistry.SaplingLeavesStageTextures.TryGetValue(capturedObjectId, out var saplingLeavesStages) &&
										saplingTrunkStages.Count > 0 && saplingLeavesStages.Count > 0)
									{
										//  Jeune arbre planté : mêmes deux couches (tronc + feuillage teinté)
										// que l'arbre adulte ci-dessus, mais avec une paire de textures par
										// stade de croissance actuel.
										int saplingStage = GetGrowthStageAt(capturedX, capturedY, capturedTileData);
										int saplingStageIdx = Math.Clamp(saplingStage - 1, 0, saplingTrunkStages.Count - 1);
										Texture2D saplingTrunkTex = saplingTrunkStages[saplingStageIdx];
										Texture2D saplingLeavesTex = saplingLeavesStages[saplingStageIdx];
										Color saplingTint = GetTreeLeafTintForObjectId(capturedObjectId, capturedX, capturedY);

										if (saplingTrunkTex.Id != 0)
										{
											Raylib.DrawTexturePro(saplingTrunkTex,
												new Rectangle(0, 0, saplingTrunkTex.Width, saplingTrunkTex.Height),
												windDestRect,
												windOrigin, windAngle, Color.White);
										}

										if (saplingLeavesTex.Id != 0)
										{
											Raylib.DrawTexturePro(saplingLeavesTex,
												new Rectangle(0, 0, saplingLeavesTex.Width, saplingLeavesTex.Height),
												windDestRect,
												windOrigin, windAngle, saplingTint);
										}
										else if (tex.Id != 0)
										{
											Raylib.DrawTexturePro(tex,
												new Rectangle(0, 0, tex.Width, tex.Height),
												windDestRect,
												windOrigin, windAngle, saplingTint);
										}
									}
									else if (tex.Id != 0)
									{
										Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
											windDestRect, windOrigin, windAngle, tint);
									}
									else if (missingTex.Id != 0)
									{
										Raylib.DrawTexturePro(missingTex, new Rectangle(0,0,missingTex.Width,missingTex.Height),
											windDestRect, windOrigin, windAngle, tint);
									}
									}

									string wallTorchMeta = World.GetTileMeta(capturedX, capturedY, "wall_torch");
									if (int.TryParse(wallTorchMeta, out int wallTorchId))
									{
										var wallTorchData = WorldTileRegistry.GetTile(wallTorchId);
										if (wallTorchData?.WallMountable == true)
										{
											Texture2D wallTorchTexture = GetTileTexture(capturedX, capturedY, wallTorchId, currentTime);
											if (wallTorchTexture.Id == 0)
												wallTorchTexture = tileTextures.GetValueOrDefault(wallTorchId);
											if (wallTorchTexture.Id != 0)
											{
												int wallTorchWidth = ts * (wallTorchData.Size?.Width ?? 1);
												int wallTorchHeight = ts * (wallTorchData.Size?.Height ?? 1);
												float wallTorchX = capturedWx + (drawW - wallTorchWidth) / 2f;
												float wallTorchY = originalDrawY + (originalDrawH - wallTorchHeight) / 2f;
												Raylib.DrawTexturePro(wallTorchTexture,
													new Rectangle(0, 0, wallTorchTexture.Width, wallTorchTexture.Height),
													new Rectangle(wallTorchX, wallTorchY, wallTorchWidth, wallTorchHeight),
													Vector2.Zero, 0, Color.White);
											}
										}
									}

									if (capturedObjectId == 122)
										DrawGrandfatherClockHands(drawX, drawY, drawW, drawH, currentTime);

									if (capturedObjectId == 86)
									{
										string flowerId = World.GetTileMeta(capturedX, capturedY, "potted_flower");
										if (int.TryParse(flowerId, out int flowerItemId))
										{
											Rectangle potRect = new Rectangle(drawX, drawY, drawW, drawH);
											if (World.TryGetPottedFlowerDestination(flowerItemId, potRect, out int flowerTileId, out Rectangle flowerDestination))
											{
												Texture2D flowerTex = GetTileTexture(capturedX, capturedY, flowerTileId, currentTime);
												if (flowerTex.Id != 0)
													Raylib.DrawTexturePro(flowerTex,
														new Rectangle(0, 0, flowerTex.Width, flowerTex.Height),
														flowerDestination,
														Vector2.Zero, 0, Color.White);
											}
										}
									}

									drawH = originalDrawH;
									drawY = originalDrawY;

if (hasVisibleInteriorTile && capturedWallCovering != 0)
																{
																	var coveringTileData = WorldTileRegistry.GetTile(capturedWallCovering);
																	if (coveringTileData != null)
																	{
																		Texture2D coveringTex = GetTileTexture(capturedX, capturedY, capturedWallCovering, currentTime);
																		if (coveringTex.Id != 0)
																		{
																			Raylib.DrawTexturePro(coveringTex,
																				new Rectangle(0, 0, coveringTex.Width, coveringTex.Height),
																				new Rectangle(drawX, drawY, drawW, originalDrawH), Vector2.Zero, 0, Color.White);
																		}
																	}
																}

																if (hasVisibleInteriorTile && originalDrawH > 0 && WorldTileRegistry.IsConnectable(capturedObjectId))
																{
																	bool IsVisibleNeighbor(int nx, int ny, bool isVertical = false)
																	{
																		if (!IsConnectableSameType(nx, ny, capturedObjectId)) return false;
																	if (!Program.IsTileLit(nx, ny)) return false;
																		if (_isPlayerIndoors)
																		{
																			if (!HasVisibleInteriorTile(nx, ny)) return false;
												// Vérifier le toit seulement pour les connexions verticales
												if (isVertical && GetOverlayAt(nx, ny - 1) == 82) return false;
											}
											return true;
										}

										if (IsVisibleNeighbor(capturedX - 1, capturedY, false))
										{
											Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "left");
											if (connTex.Id != 0)
												Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
													new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
										}
										if (IsVisibleNeighbor(capturedX + 1, capturedY, false))
										{
											Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "right");
											if (connTex.Id != 0)
												Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
													new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
										}
										if (IsVisibleNeighbor(capturedX, capturedY - 1, true))
										{
											Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "up");
											if (connTex.Id != 0)
												Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
													new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
										}
										if (IsVisibleNeighbor(capturedX, capturedY + 1, true))
										{
											Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "down");
											if (connTex.Id != 0)
												Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
													new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
										}
									}

									// ==========  DESSIN DE LA RUCHE (métadonnée de tuile "beehive") ==========
											if (World.HasTileMeta(capturedX, capturedY, "ore"))
											{
												Texture2D oreTex = World.GetOreOverlayTexture(capturedX, capturedY);
												if (oreTex.Id != 0)
												{
													Raylib.DrawTexturePro(oreTex,
														new Rectangle(0, 0, oreTex.Width, oreTex.Height),
														new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
												}
											}
											if (World.HasTileMeta(capturedX, capturedY, "beehive"))
									{
										Texture2D beehiveTex = WorldTileRegistry.GetFirstTexture(6000);
										if (beehiveTex.Id != 0)
										{
											var beehiveTileData = WorldTileRegistry.GetTile(6000);
											int offX = beehiveTileData?.DrawOffset?.X ?? 0;
											int offY = beehiveTileData?.DrawOffset?.Y ?? 0;
											Rectangle beeDest = new Rectangle(
												drawX + offX,
												drawY + offY,
												drawW,
												drawH
											);
											Raylib.DrawTexturePro(beehiveTex,
												new Rectangle(0, 0, beehiveTex.Width, beehiveTex.Height),
												beeDest, Vector2.Zero, 0, Color.White);
										}
									}

									// Affichage spécial pour les étagères (ID 81)
									if (capturedObjectId == 81 && capturedTileData.IsContainer == true)
									{
										var chunkAtPos = GetChunkAt(capturedX, capturedY);
										var container = chunkAtPos?.GetContainerAt(capturedX, capturedY);
										if (container != null)
										{
											int shelfW = drawW;
											int itemSize = 14;
											int spacingX = itemSize + 1;
											int spacingY = itemSize + 8;
											int cols = container.Columns;
											int startX = drawX + (shelfW - (cols * itemSize + (cols - 1) * 4)) / 2;
											int startY = drawY + 15;
											for (int i = 0; i < container.Slots.Count; i++)
											{
												int col = i % cols;
												int row = i / cols;
												var slot = container.Slots[i];
												if (!slot.IsEmpty && slot.Item != null)
												{
													int iconX = startX + col * spacingX;
													int iconY = startY + row * spacingY;
													//  Toujours passer par ItemRenderer (gère lui-même le repli
													// couches teintées / icône / couleur) plutôt que de dessiner
													// slot.Item.Icon à la main : sinon les items sans texture de
													// base (chapka, fedora...) restent invisibles sur l'étagère.
													ItemRenderer.DrawItemCompact(slot.Item, iconX, iconY, itemSize);
												}
											}
										}
									}
									// Aquarium (ID 99)
									else if (capturedObjectId == 99 && capturedTileData.IsContainer == true)
									{
										var chunkAtPos = GetChunkAt(capturedX, capturedY);
										var container = chunkAtPos?.GetContainerAt(capturedX, capturedY);
										var aquarium = GetAquariumData(capturedX, capturedY);
										if (aquarium != null && container != null)
										{
											foreach (var fish in aquarium.FishStates.Values)
											{
												if (fish.SlotIndex < 0 || fish.SlotIndex >= container.Slots.Count) continue;
												var slot = container.Slots[fish.SlotIndex];
												if (slot?.Item == null) continue;

												float baseWorldY = aquarium.SwimArea.Y + fish.BaseY * aquarium.SwimArea.Height;
												float bob = MathF.Sin(currentTime * fish.BobSpeed + fish.BobPhase) * 3f;
												Vector2 worldPos = new Vector2(
													aquarium.SwimArea.X + fish.LocalPosition.X * aquarium.SwimArea.Width,
													baseWorldY + bob
												);

												float size = 14f;
												Rectangle destRect = new Rectangle(worldPos.X - size/2, worldPos.Y - size/2, size, size);
												//  Toujours passer par ItemRenderer (gère lui-même le repli
												// couches teintées / icône / couleur) plutôt que de dessiner
												// slot.Item.Icon à la main.
												ItemRenderer.DrawItem(slot.Item, destRect, forceSingleLayer: true);
											}
										}
									}
								}

								// Overlays (minerais, etc.)
								if (currentOverlay != 0 && currentOverlay != 82 && currentOverlay != 123 && currentOverlay != 6000 && capturedObjectId != 408)
								{
									Texture2D overlayTex = GetTileTexture(capturedX, capturedY, currentOverlay, currentTime);
									if (overlayTex.Id != 0)
									{
										Raylib.DrawTexturePro(overlayTex,
											new Rectangle(0, 0, overlayTex.Width, overlayTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH),
											Vector2.Zero, 0, Color.White);
									}
								}

								if (capturedHp != null && capturedHp.Current < capturedHp.Max)
								{
									float ratio = Math.Clamp((float)capturedHp.Current / Math.Max(1, capturedHp.Max), 0f, 1f);
									float barWidth = ts;
									float barHeight = 7f;
									float barX = capturedWx;
									float barY = capturedWy - ts * 2 - 8;
									Rectangle barRect = new Rectangle(barX, barY, barWidth, barHeight);
									Rectangle fillRect = new Rectangle(barX + 1.5f, barY + 1.5f, Math.Max(0f, (barWidth - 3f) * ratio), barHeight - 3f);
									Raylib.DrawRectangleRounded(new Rectangle(barX - 2f, barY - 2f, barWidth + 4f, barHeight + 4f), 0.45f, 8, new Color(0, 0, 0, 105));
									Raylib.DrawRectangleRounded(barRect, 0.45f, 8, new Color(32, 20, 24, 225));
									if (fillRect.Width > 0f)
									{
										Color fillColor = ratio < 0.3f
											? new Color(225, 65, 58, 255)
											: ratio < 0.6f
												? new Color(238, 165, 58, 255)
												: new Color(90, 210, 105, 255);
										Raylib.DrawRectangleRounded(fillRect, 0.35f, 6, fillColor);
										Raylib.DrawRectangleRounded(
											new Rectangle(fillRect.X + 1f, fillRect.Y + 0.5f, Math.Max(0f, fillRect.Width - 2f), 1.5f),
											0.5f, 6, new Color(255, 255, 255, 85));
									}
									Raylib.DrawRectangleRoundedLines(barRect, 0.45f, 8, 1, new Color(255, 220, 220, 150));
								}
							}
						}
					});
				}
			}

			// Overlays (toits) si joueur pas à l'intérieur
			{
				for (int ix = 0; ix < width; ix++)
				{
					int worldX = startX + ix;
					for (int iy = 0; iy < heightTiles; iy++)
					{
						int worldY = startY + iy;
						int localIx = ix;   // COPIE LOCALE
						int localIy = iy;   // COPIE LOCALE
						int overlayId = overlayIds[localIx, localIy];
						if (overlayId == 0 || (_isPlayerIndoors && overlayId != 123)) continue;
						var overlayTileData = WorldTileRegistry.GetTile(overlayId);
						if (overlayTileData == null) continue;
						int height = heights[ix, iy];
						float wx = worldX * ts;
						float wy = worldY * ts - (height * ts / 4);
						int roofHeightTiles = roofHeights[ix, iy];
						// Décalage vertical proportionnel à la hauteur DE CETTE TUILE (plafonnée au
						// faîtage) : c'est ce qui donne la perspective en pente au lieu d'un toit plat.
						// Les pans horizontaux restent à hauteur pleine (comme avant, positions
						// inchangées) ; les pans verticaux montent depuis les bords vers le milieu.
						float yOffset = -(roofHeightTiles) * ts;
						float adjustedWy = wy + yOffset;
						// Le tri en Y doit se baser sur la position AU SOL (non décalée), pas sur la
						// position de dessin déjà remontée par la hauteur : sinon une colonne haute
						// (empilement de roof_wall) se retrouve triée "avant" la rangée du dessous,
						// qui se dessine alors PAR-DESSUS et cache le bas de l'empilement. Le
						// décalage de hauteur ne doit affecter que le DESSIN, jamais l'ordre.
						//
						//  CORRECTIF : cette ligne ajoutait auparavant +100000f, ce qui plaçait le
						// toit systématiquement TOUT EN HAUT du tri de profondeur, quelle que soit sa
						// position réelle. Résultat : un arbre (ou tout autre sprite) situé plus bas
						// à l'écran — donc censé se dessiner PAR-DESSUS le toit, comme pour n'importe
						// quel autre objet (les arbres utilisent exactement la même formule de base,
						// `wy + ts`, voir plus haut dans DrawWorld) — se faisait quand même recouvrir
						// par le toit, qui l'emportait toujours artificiellement. Le toit doit donc
						// concourir avec la même valeur de base que tout le reste ; seuls de très
						// petits écarts (bien inférieurs à la hauteur d'une tuile) sont ajoutés
						// ci-dessous pour départager les COUCHES INTERNES d'un même toit entre elles
						// (pente verticale/horizontale, raccords, empilement de roof_wall), sans
						// jamais pouvoir l'emporter sur un objet d'une autre rangée.
						// Les murs utilisent le même départage horizontal basé sur ix. Le toit doit
						// rester juste au-dessus du mur de la même tuile, sinon le mur le recouvre
						// dès que la tuile n'est plus à l'extrême gauche de la fenêtre.
						float sortY = overlayId == 123 ? wy + ts - 0.01f : wy + ts + (ix + 1) * 0.001f;

						bool hasSlopeInfo = TryGetRoofTileInfo(worldX, worldY, out RoofTileInfo slopeInfo);
						// Les pans "horizontaux" (plats, à hauteur classique) sont dessinés en
						// premier (en arrière-plan) ; les pans "verticaux" (surélevés par
						// l'empilement de roof_wall) passent par-dessus. Écart minime : ça ne
						// concerne que l'ordre ENTRE pans du même toit, jamais vis-à-vis d'un objet
						// extérieur à cette rangée.
						bool isVerticalZone = hasSlopeInfo && !slopeInfo.IsHorizontalZone;
						if (isVerticalZone)
							sortY += 0.05f;

						int capturedX = worldX, capturedY = worldY;
						float capturedWx = wx, capturedWy = overlayId == 123 ? wy : adjustedWy;
						float capturedWyBase = wy;
						int capturedSortLayer = overlayTileData.OnFloor ? -1 : 0;
						int capturedHeight = roofHeightTiles;
						int capturedOverlayId = overlayId;
						bool capturedIsColumnAnchor = hasSlopeInfo && slopeInfo.IsColumnAnchor;
						int capturedWallStackCount = hasSlopeInfo ? slopeInfo.WallStackCount : 0;
						RoofTexKind capturedTexKind = hasSlopeInfo ? slopeInfo.TexKind : RoofTexKind.Default;

						// L'empilement de roof_wall part dans SON PROPRE élément de rendu, avec une
						// priorité de tri légèrement supérieure (voir note plus haut : l'écart reste
						// minime, il ne sert qu'à départager les couches internes du même toit) :
						// sinon, sur la rangée du bas, une tuile voisine (sol, mur, autre bâtiment)
						// au sortY quasi identique pouvait se dessiner juste après et cacher le bas
						// de la pile.
						if (capturedIsColumnAnchor && capturedWallStackCount > 0)
						{
							renderItems.Add(new RenderItem
							{
								SortLayer = capturedSortLayer,
								// Doit rester au-dessus du pan de toit et de ses raccords (ci-dessous),
								// mais seulement de quelques centièmes : voir la note sur `sortY` plus haut.
								Y = sortY + 0.02f,
								DrawAction = () =>
								{
									Texture2D wallTex = GetRoofWallTexture(missingTex);
									int baseHeightTiles = capturedHeight - capturedWallStackCount; // hauteur classique (sans pente)
									float baseY = capturedWyBase - baseHeightTiles * ts;
									for (int lvl = 1; lvl <= capturedWallStackCount; lvl++)
									{
										// Les roof_wall doivent commencer juste sous la base du toit, pas au-dessus.
float stackY = baseY - (lvl - 1) * ts;
										Raylib.DrawTexturePro(wallTex, new Rectangle(0, 0, wallTex.Width, wallTex.Height),
											new Rectangle((int)capturedWx, (int)stackY, ts, ts), Vector2.Zero, 0, Color.White);
									}
								}
							});
						}

						renderItems.Add(new RenderItem
						{
							Y = sortY,
							SortLayer = capturedSortLayer,
							DrawAction = () =>
							{
								Texture2D tex = capturedTexKind switch
								{
									RoofTexKind.Up => GetRoofUpTexture(missingTex),
									RoofTexKind.Down => GetRoofDownTexture(missingTex),
									_ => GetTileTexture(capturedX, capturedY, capturedOverlayId, currentTime),
								};
								if (tex.Id == 0) tex = missingTex;
								int drawW = ts, drawH = ts;
								int drawX = (int)capturedWx, drawY = (int)capturedWy;
								var tileDef = WorldTileRegistry.GetTile(capturedOverlayId);
								if (tileDef?.Size != null)
								{
									drawW = ts * tileDef.Size.Width;
									drawH = ts * tileDef.Size.Height;
									if (tileDef.DrawOffset != null)
									{
										drawX += tileDef.DrawOffset.X;
										drawY -= tileDef.DrawOffset.Y;
									}
									drawY = (int)(capturedWy - drawH + ts);
								}
								if (tex.Id != 0)
								{
									if (capturedOverlayId == 123)
									{
										Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
											new Rectangle(drawX + drawW / 2f, drawY + drawH / 2f, drawW, drawH),
											new Vector2(drawW / 2f, drawH / 2f), GetRailwayRotation(capturedX, capturedY), Color.White);
									}
									else
									{
										Raylib.DrawTexturePro(tex, new Rectangle(0,0,tex.Width,tex.Height),
											new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
									}
								}
							}
						});
							renderItems.Add(new RenderItem
						{
							SortLayer = capturedSortLayer,
							// Raccords dessinés par-dessus la texture de base du pan (voir note sur
						// `sortY` plus haut), mais toujours en dessous de l'empilement de roof_wall.
						Y = sortY + 0.01f,
							DrawAction = () =>
							{
								int drawW = ts, drawH = ts;
								int drawX = (int)capturedWx, drawY = (int)capturedWy;
								var tileDef = WorldTileRegistry.GetTile(capturedOverlayId);
								if (tileDef?.Size != null)
								{
									drawW = ts * tileDef.Size.Width;
									drawH = ts * tileDef.Size.Height;
									if (tileDef.DrawOffset != null)
									{
										drawX += tileDef.DrawOffset.X;
										drawY -= tileDef.DrawOffset.Y;
									}
									drawY = (int)(capturedWy - drawH + ts);
								}
								Texture2D GetConnectionTextureForKind(string direction)
								{
									return capturedTexKind switch
									{
										RoofTexKind.Up => GetRoofVariantConnectionTexture(missingTex, "roof_up", direction),
										RoofTexKind.Down => GetRoofVariantConnectionTexture(missingTex, "roof_down", direction),
										_ => WorldTileRegistry.GetConnectionTexture(capturedOverlayId, direction),
									};
								}
								if (Program.IsTileLit(capturedX - 1, capturedY) && GetOverlayAt(capturedX - 1, capturedY) == capturedOverlayId)
								{
									Texture2D connTex = GetConnectionTextureForKind("left");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
								}
								if (Program.IsTileLit(capturedX + 1, capturedY) && GetOverlayAt(capturedX + 1, capturedY) == capturedOverlayId)
								{
									Texture2D connTex = GetConnectionTextureForKind("right");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
								}
								if (Program.IsTileLit(capturedX, capturedY - 1) && GetOverlayAt(capturedX, capturedY - 1) == capturedOverlayId)
								{
									Texture2D connTex = GetConnectionTextureForKind("up");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
								}
								if (Program.IsTileLit(capturedX, capturedY + 1) && GetOverlayAt(capturedX, capturedY + 1) == capturedOverlayId)
								{
									Texture2D connTex = GetConnectionTextureForKind("down");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
								}
							}
						});
					}
				}
			}
			
			foreach (var car in Program.Cars)
			{
				if (!IsEntityNearScreen(car.Position))
					continue;
				float sortY = car.Position.Y + Program.FeetOffsetY;
				renderItems.Add(new RenderItem
				{
					Y = sortY,
					Kind = RenderItemKind.Car,
					Tag = new CarRenderTag(car)
				});
			}

			if (Program.HasWagonLinkVisuals())
			{
				// Barres d'attelage : triées tout en bas pour passer sous les sprites des wagons.
				renderItems.Add(new RenderItem
				{
					Y = float.MinValue,
					DrawAction = () => Program.DrawWagonLinks()
				});
			}

			if (Program.PlayerCurrentWagonTile.HasValue)
			{
				Vector2 wagonPos = Program.GetMountedWagonDrawPosition();
				if (IsEntityNearScreen(wagonPos))
				{
					renderItems.Add(new RenderItem
					{
						Y = wagonPos.Y + Program.FeetOffsetY - 0.5f,
						DrawAction = () => Program.DrawWagon(
							Program.GetMountedWagonDrawPosition(),
							(int)Program.TileSize,
							Program.IsMountedWagonVertical())
					});
				}
			}

			//  MULTIJOUEUR : côté client, les entités viennent du snapshot du host.
			// IMPORTANT : ce bloc doit être indépendant de la boucle "foreach (var entity in entities)"
			// ci-dessous, car côté client la liste Program.entities est toujours vide (le client ne
			// simule aucune entité) — placé À L'INTÉRIEUR de cette boucle comme avant, ce code ne
			// s'exécutait donc JAMAIS, ce qui rendait les entités totalement invisibles pour les clients.
			if (NetworkManager.IsClient)
			{
				foreach (var dto in NetworkManager.GetRemoteEntities())
				{
					var dtoPos = new Vector2(dto.PosX, dto.PosY);
					if (!Program.IsTileLit((int)(dtoPos.X / ts), (int)(dtoPos.Y / ts)))
						continue;
					if (!ShouldRenderEntity(dtoPos))
						continue;
					if (!IsEntityNearScreen(dtoPos))
						continue;

					var capturedDto = dto;
					var capturedHairBase = hairBase;
					var capturedHairOverlay = hairOverlay;
					int hairIdx = capturedDto.HairStyle < capturedHairBase.Count ? capturedDto.HairStyle : 0;
					Color dtoTint = new Color(capturedDto.Tint.R, capturedDto.Tint.G, capturedDto.Tint.B, capturedDto.Tint.A);
					Color dtoHair = new Color(capturedDto.HairColor.R, capturedDto.HairColor.G, capturedDto.HairColor.B, capturedDto.HairColor.A);
					var capturedDtoEquipment = Program.GetNetworkEntityEquipment(capturedDto.NetId, capturedDto.Equip);
					Item? capturedSlimeStoredItem = null;
					if (capturedDto.SlimeStorageSlot != null)
					{
						var storedSlot = new InventorySlot();
						SaveSystem.RestoreInventorySlotFromSave(capturedDto.SlimeStorageSlot, storedSlot);
						capturedSlimeStoredItem = storedSlot.Item;
					}
					renderItems.Add(new RenderItem
					{
						Y = capturedDto.PosY,
						DrawAction = () =>
						{
							Texture2D hBase = capturedHairBase.Count > hairIdx ? capturedHairBase[hairIdx] : new Texture2D();
							Texture2D hOver = capturedHairOverlay.Count > hairIdx ? capturedHairOverlay[hairIdx] : new Texture2D();
							EntityRenderer.DrawEntity(
								capturedDto.Species, capturedDto.AnimState, capturedDto.AnimFrame, capturedDto.AnimProg,
								capturedDto.Facing, new Vector2(capturedDto.PosX, capturedDto.PosY),
								dtoTint, hBase, hOver, dtoHair,
								SpeciesData.Skeletons,
								customScale: capturedDto.Scale,
                                eyes: EntityRenderer.GetEyesTextureForSpecies(capturedDto.Species),
                                equipment: capturedDtoEquipment, isCarrying: false, inWater: false,
								flashWhite: capturedDto.DamageFlash > 0f,
								ghostly: capturedDto.IsSpiritAnimal,
								ghostTrailKey: capturedDto.NetId,
								slimeStoredItem: capturedSlimeStoredItem);
							float w = Program.TileSize * 1.2f;
							float ratio = 1f;
							int bx = (int)(capturedDto.PosX - w / 2f);
							int by = (int)(capturedDto.PosY - Program.TileSize * 0.8f);
							Raylib.DrawRectangle(bx, by, (int)w, 5, new Color(60, 0, 0, 180));
							Raylib.DrawRectangle(bx, by, (int)(w * ratio), 5, new Color(80, 200, 80, 220));
							// Nom personnalisé
							if (!string.IsNullOrEmpty(capturedDto.CustomName))
							{
								int nw = FontManager.MeasureText(capturedDto.CustomName, 13);
								FontManager.DrawText(capturedDto.CustomName,
									(int)capturedDto.PosX - nw / 2, (int)capturedDto.PosY - Program.TileSize - 10,
									13, new Color(220, 220, 100, 220));
							}
						}
					});

					//  MULTIJOUEUR : icônes de commerce/quête/guilde au-dessus de la tête du PNJ,
					// miroir du rendu côté hôte, calculées à partir des données désormais incluses
					// dans le snapshot réseau (EntityDto) plutôt que depuis Program.entities (vide
					// côté client). Sans ceci, un client ne voyait jamais l'icône signalant qu'un
					// PNJ a quelque chose à proposer. Comme côté hôte, c'est un RenderItem SÉPARÉ
					// avec Y = float.MaxValue - 1000 pour être dessiné en dernier (au-dessus de
					// tout le reste) : le mettre dans le même DrawAction que le sprite (Y =
					// PosY, trié avec le décor) le faisait recouvrir par des éléments dessinés
					// après lui, donc jamais visible à l'écran.
					if (capturedDto.Species == "human")
					{
						bool cShowTrader = (capturedDto.IsTrader || capturedDto.Profession != 0) && _traderIcon.Id != 0;
						bool cShowStart = capturedDto.QuestState == (int)QuestState.Offered;
						bool cShowFinish = capturedDto.QuestState == (int)QuestState.Delivered
							|| NetworkManager.GetRemoteEntities().Any(o => o.QuestState == (int)QuestState.Accepted && o.QuestTargetNetId == capturedDto.NetId);
						bool cShowQuest = (cShowStart && _questStartIcon.Id != 0) || (cShowFinish && _questFinishIcon.Id != 0);
						bool cShowGuild = (capturedDto.WantsToJoinGuild || capturedDto.Friendship >= 1f) && !capturedDto.IsTamed && !capturedDto.IsGuildMember
							&& Program.PlayerGuild != null && _guildJoinIcon.Id != 0;
						bool cShowTracker = capturedDto.QuestState == (int)QuestState.Accepted || capturedDto.QuestState == (int)QuestState.Delivered;

						var cActiveIcons = new List<Texture2D>();
						if (cShowTrader) cActiveIcons.Add(_traderIcon);
						if (cShowQuest) cActiveIcons.Add(cShowStart ? _questStartIcon : _questFinishIcon);
						if (cShowGuild) cActiveIcons.Add(_guildJoinIcon);
						if (cShowTracker) { var tracker = QuestTrackerUI.GetTrackerTexture(); if (tracker.Id != 0) cActiveIcons.Add(tracker); }

						if (cActiveIcons.Count > 0)
						{
							Vector2 iconBasePos = new Vector2(capturedDto.PosX, capturedDto.PosY);
							renderItems.Add(new RenderItem
							{
								Y = float.MaxValue - 1000,
								DrawAction = () =>
								{
									float iconSize = cActiveIcons.Count > 1 ? 26f : 28f;
									float yOffsetIcon = -90f;
									float step = 34f;
									int count = cActiveIcons.Count;
									for (int i = 0; i < count; i++)
									{
										Texture2D tex = cActiveIcons[i];
										float xOffset = (i - (count - 1) / 2f) * step;
										Vector2 pos = new Vector2(iconBasePos.X + xOffset, iconBasePos.Y);
										Raylib.DrawTexturePro(tex,
											new Rectangle(0, 0, tex.Width, tex.Height),
											new Rectangle(pos.X - iconSize / 2, pos.Y + yOffsetIcon - iconSize / 2, iconSize, iconSize),
											Vector2.Zero, 0, Color.White);
									}
								}
							});
						}
					}
				}
				// Ne pas dessiner les entités locales côté client (elles sont vides de toute façon)
			}
			else
			// Entités (host / solo) : dessinées depuis la liste Program.entities habituelle.
			foreach (var entity in entities)
			{
				if (!entity.IsAlive) continue;
				if (Program.MountedAnimal == entity) continue; // dessinée avec le joueur (voir plus bas), pour l'ordre de superposition
				if (entity.IsMounted && entity.Rider != null) continue; // monture PNJ dessinée via le cavalier
				if (!ShouldRenderEntity(entity.WorldPos)) continue;
				if (!IsEntityNearScreen(entity.WorldPos))
					continue;
				{
				Vector2 entityVisualPos = entity.VisualWorldPos;
				int tileX = (int)(entityVisualPos.X / ts);
				int tileY = (int)(entityVisualPos.Y / ts);
				if (!Program.IsTileLit(tileX, tileY)) continue;
				int height = GetHeightAt(tileX, tileY);
				float yOffset = -height * ts / 4;
				var e = entity;
				bool isTrader = e.IsTrader;
				Vector2 traderIconPos = new Vector2(entityVisualPos.X, entityVisualPos.Y + yOffset - e.VisualOffsetY);
				renderItems.Add(new RenderItem
				{
					Y = entityVisualPos.Y + Program.FeetOffsetY + yOffset,
					DrawAction = () =>
					{
						//  BOSS VER DE TERRE : rendu segmenté dédié (trail), pas le squelette classique.
						// Invisible tant qu'il est sous terre (Underground/Telegraph/Recover) : IsAlive
						// reste true tout du long, seul WormHeadVisible pilote l'affichage.
						if (e.IsWorm)
						{
							if (e.WormHeadVisible)
								EntityRenderer.DrawWormBoss(e, e.Scale);
							return;
						}

						Texture2D hairBaseTex = hairBase.Count > e.HairStyle ? hairBase[e.HairStyle] : new Texture2D();
						Texture2D hairOverlayTex = hairOverlay.Count > e.HairStyle ? hairOverlay[e.HairStyle] : new Texture2D();
						Entity? mount = e.IsRidingMount && e.OwnedMount != null && e.OwnedMount.IsMounted && e.OwnedMount.Rider == e ? e.OwnedMount : null;
						EntityRenderer.DrawEntity(
							e.Species, e.AnimState, e.CurrentFrame, e.AnimProgress,
							e.Facing, new Vector2(entityVisualPos.X, entityVisualPos.Y + yOffset - e.VisualOffsetY), e.Tint,
							hairBaseTex, hairOverlayTex, e.HairColor,
							SpeciesData.Skeletons,
							eyes: EntityRenderer.GetEyesTextureForSpecies(e.Species), mouth: Program.GetMouthTextureFor(e), forceBlinking: e.IsBlinking,
							customScale: e.Scale,  // ← utilisation du scale individuel
							equipment: e.Equipment,
							underwearTexture: Program.LeafUnderpantsTexture,
							isCarrying: false, inWater: e.IsInWater,
							attackSwingProgress: e.GetAttackSwingProgress(),
							flashWhite: e.DamageFlash > 0f,
							pupilLeftOffset: e.PupilLeftOffset,
							pupilRightOffset: e.PupilRightOffset,
							eyebrowLeftOffset: e.EyebrowLeftOffset,
							eyebrowRightOffset: e.EyebrowRightOffset,
							randomFeatureVariant: e.RandomFeatureVariant,
							randomFeatureColor: e.RandomFeatureColor,
							beardStyle: e.BeardStyle,
							shadowGroundOffset: e.VisualOffsetY,
							ghostly: e.IsSpiritAnimal,
							ghostTrailKey: e.NetId.ToString(),
							slimeStoredItem: e.IsSlime ? e.GetSlimeStorageSlot().Item : null,
							drawMount: mount != null ? () =>
							{
								var m = mount;
								if (m == null || !m.IsAlive) return;
								Texture2D mHairBase = hairBase.Count > m.HairStyle ? hairBase[m.HairStyle] : new Texture2D();
								Texture2D mHairOverlay = hairOverlay.Count > m.HairStyle ? hairOverlay[m.HairStyle] : new Texture2D();
								EntityRenderer.DrawEntity(
									m.Species, m.AnimState, m.CurrentFrame, m.AnimProgress,
									m.Facing, m.WorldPos, m.Tint,
									mHairBase, mHairOverlay, m.HairColor,
									SpeciesData.Skeletons,
									eyes: EntityRenderer.GetEyesTextureForSpecies(m.Species),
									mouth: Program.GetMouthTextureFor(m),
									forceBlinking: m.IsBlinking,
									customScale: m.Scale,
									equipment: m.Equipment,
									isCarrying: false, inWater: m.IsInWater,
											attackSwingProgress: m.GetAttackSwingProgress(),
									flashWhite: m.DamageFlash > 0f,
									pupilLeftOffset: m.PupilLeftOffset,
									pupilRightOffset: m.PupilRightOffset,
									eyebrowLeftOffset: m.EyebrowLeftOffset,
									eyebrowRightOffset: m.EyebrowRightOffset,
									randomFeatureVariant: m.RandomFeatureVariant,
									randomFeatureColor: m.RandomFeatureColor,
									beardStyle: m.BeardStyle);
							} : null);
						//  Plus de bulle de dialogue au-dessus de la tête : le PNJ s'arrête,
						// s'oriente vers son interlocuteur (Entity.Update) et "articule" via
						// l'alternance Humanmouth / Humanmouth_open ci-dessus (Program.GetMouthTextureFor).
						if (e.Species != "human" && e.CurrentHP < e.MaxHP)
						{
							float ratio = Math.Clamp((float)e.CurrentHP / Math.Max(1, e.MaxHP), 0f, 1f);
							Vector2 entityDrawPos = new Vector2(e.WorldPos.X, e.WorldPos.Y + yOffset - e.VisualOffsetY);
							float topY = EntityRenderer.GetVisualTopY(
								e.Species, e.AnimState, e.CurrentFrame, e.AnimProgress,
								e.Facing, entityDrawPos, e.Scale);
							float w = Program.TileSize * 1.35f;
							float h = 7f;
							float bx = e.WorldPos.X - w / 2f;
							float by = topY - 13f;
							Rectangle barRect = new Rectangle(bx, by, w, h);
							Rectangle fillRect = new Rectangle(bx + 1.5f, by + 1.5f, Math.Max(0f, (w - 3f) * ratio), h - 3f);
							Raylib.DrawRectangleRounded(new Rectangle(bx - 2f, by - 2f, w + 4f, h + 4f), 0.45f, 8, new Color(0, 0, 0, 105));
							Raylib.DrawRectangleRounded(barRect, 0.45f, 8, new Color(32, 20, 24, 225));
							if (fillRect.Width > 0f)
							{
								Color fillColor = ratio < 0.3f
									? new Color(225, 65, 58, 255)
									: ratio < 0.6f
										? new Color(238, 165, 58, 255)
										: new Color(90, 210, 105, 255);
								Raylib.DrawRectangleRounded(fillRect, 0.35f, 6, fillColor);
								Raylib.DrawRectangleRounded(
									new Rectangle(fillRect.X + 1f, fillRect.Y + 0.5f, Math.Max(0f, fillRect.Width - 2f), 1.5f),
									0.5f, 6, new Color(255, 255, 255, 85));
							}
							Raylib.DrawRectangleRoundedLines(barRect, 0.45f, 8, 1, new Color(255, 220, 220, 150));
						}
					}
				});
				//  Icônes au-dessus de la tête du PNJ (commerce ET quête) : on calcule d'abord
				// lesquelles sont actives afin de les positionner côte à côte plutôt que superposées.
				bool showTraderIcon = (isTrader || e.HasProfession) && _traderIcon.Id != 0;

				bool showStart = false, showFinish = false;
				if (e.IsVillager)
				{
					// 1) Ce PNJ propose une quête (pas encore acceptée) -> quest_start.png
					showStart = e.ActiveQuest != null && e.ActiveQuest.State == QuestState.Offered;

					// 2) Ce PNJ est la cible d'une quête acceptée par le joueur -> quest_finish.png
					var deliveryQuest = QuestManager.GetQuestToDeliverFor(e, entities);
					showFinish = deliveryQuest != null;

					// 3) Ce PNJ est le donneur d'une quête livrée -> aussi quest_finish.png pour indiquer la validation
					var rewardQuest = QuestManager.GetIncomingQuestFor(e, entities);
					showFinish = showFinish || rewardQuest != null;
				}
				bool showQuestIcon = (showStart && _questStartIcon.Id != 0) || (showFinish && _questFinishIcon.Id != 0);

				// 4) Ce PNJ propose de rejoindre la guilde du joueur (amitié maximale) -> guild_joinable.png.
				// Traité exactement comme les icônes de commerce/quête : même hauteur, même style
				// d'affichage côte à côte, et acceptation via la bulle de dialogue (pas de clic droit direct).
				bool showGuildIcon = e.IsVillager && GuildRecruitManager.CanAccept(e)
					&& Program.PlayerGuild != null && _guildJoinIcon.Id != 0;
				bool showTrackerIcon = false;
				foreach (var quest in QuestManager.GetPersistedQuestsSnapshot())
				{
					if (quest.State == QuestState.Accepted && quest.TargetId == e.NetId)
					{
						showTrackerIcon = true;
						break;
					}
					if (quest.State == QuestState.Delivered && quest.GiverId == e.NetId)
					{
						showTrackerIcon = true;
						break;
					}
				}

				//  On regroupe toutes les icônes actives (commerce / quête / guilde / traqueur) et on les
				// positionne côte à côte au-dessus de la tête, plutôt que de les superposer.
				var activeIcons = new List<Texture2D>();
				if (showTraderIcon) activeIcons.Add(_traderIcon);
				if (showQuestIcon) activeIcons.Add(showStart ? _questStartIcon : _questFinishIcon);
				if (showGuildIcon) activeIcons.Add(_guildJoinIcon);
				if (showTrackerIcon) { var trackerTex = QuestTrackerUI.GetTrackerTexture(); if (trackerTex.Id != 0) activeIcons.Add(trackerTex); }

				if (activeIcons.Count > 0)
				{
					float iconSize = activeIcons.Count > 1 ? 26f : 28f;
					float yOffsetIcon = -90f;
					float step = 34f; // distance entre les centres de deux icônes voisines
					Vector2 basePos = traderIconPos;
					int count = activeIcons.Count;

					renderItems.Add(new RenderItem
					{
						Y = float.MaxValue - 1000,
						DrawAction = () =>
						{
							for (int i = 0; i < count; i++)
							{
								Texture2D tex = activeIcons[i];
								float xOffset = (i - (count - 1) / 2f) * step;
								Vector2 pos = new Vector2(basePos.X + xOffset, basePos.Y);
								Raylib.DrawTexturePro(tex,
									new Rectangle(0, 0, tex.Width, tex.Height),
									new Rectangle(pos.X - iconSize/2, pos.Y + yOffsetIcon - iconSize/2, iconSize, iconSize),
									Vector2.Zero, 0, Color.White);
							}
						}
					});
				}
				if (e.ShowAlertIcon && _alertIcon.Id != 0)
				{
					Vector2 iconWorldPos = new Vector2(e.WorldPos.X, e.WorldPos.Y + yOffset - e.VisualOffsetY);
					renderItems.Add(new RenderItem
					{
						Y = float.MaxValue - 900,
						DrawAction = () =>
						{
							float iconSize = 34f;
							float yOffsetIcon = -110f;
							Rectangle srcRect = new Rectangle(0, 0, _alertIcon.Width, _alertIcon.Height);
							Rectangle destRect = new Rectangle(
								(int)(iconWorldPos.X - iconSize / 2),
								(int)(iconWorldPos.Y + yOffsetIcon - iconSize / 2),
								(int)iconSize,
								(int)iconSize
							);
							Raylib.DrawTexturePro(_alertIcon, srcRect, destRect, Vector2.Zero, 0, Color.White);
						}
					});
				}
				if (entity.IsInterestedInFood && !entity.IsTamed)
				{
					int entityTileX = (int)(entity.WorldPos.X / ts);
					int entityTileY = (int)(entity.WorldPos.Y / ts);
					int entityHeight = World.GetHeightAt(entityTileX, entityTileY);
					float entityYOffset = -entityHeight * ts / 4;
					Vector2 iconWorldPos = new Vector2(entity.WorldPos.X, entity.WorldPos.Y + entityYOffset - entity.VisualOffsetY);
					renderItems.Add(new RenderItem
					{
						Y = float.MaxValue - 1000,
						DrawAction = () =>
						{
							Texture2D questionTex = Program._questionIcon;
							if (questionTex.Id == 0) return;
							float iconSize = 32f;
							float pulse = 0.8f + 0.2f * MathF.Sin((float)Raylib.GetTime() * 5f);
							float yOffsetIcon = -90f * pulse;
							Rectangle srcRect = new Rectangle(0, 0, questionTex.Width, questionTex.Height);
							Rectangle destRect = new Rectangle(
								(int)(iconWorldPos.X - iconSize / 2),
								(int)(iconWorldPos.Y + yOffsetIcon - iconSize / 2),
								(int)iconSize,
								(int)iconSize
							);
							Raylib.DrawTexturePro(questionTex, srcRect, destRect, Vector2.Zero, 0, Color.White);
						}
					});
				}
			} // fin du foreach entity (guard IsAlive/frustum)
			} // fin du else { (rendu des entités côté host/solo)
			
			// ========== CONNEXIONS ÉLECTRIQUES (fils) ==========
			foreach (var conn in Program.GetEnergyConnections())
			{
				if (!Program.IsTileLit(conn.X1, conn.Y1) || !Program.IsTileLit(conn.X2, conn.Y2))
					continue;
				Vector2 p1 = new Vector2(conn.X1 * ts + ts / 2f, conn.Y1 * ts + ts / 2f);
				Vector2 p2 = new Vector2(conn.X2 * ts + ts / 2f, conn.Y2 * ts + ts / 2f);
				if (!IsSegmentNearScreen(p1, p2))
					continue;
				
				// Ajuster pour la hauteur du terrain
				int h1 = GetHeightAt(conn.X1, conn.Y1);
				int h2 = GetHeightAt(conn.X2, conn.Y2);
				p1.Y -= h1 * ts / 4f;
				p2.Y -= h2 * ts / 4f;
				
				float sortY = Math.Min(p1.Y, p2.Y);
				
				renderItems.Add(new RenderItem
				{
					Y = sortY,
					Kind = RenderItemKind.Wire,
					Tag = new WireRenderTag(p1, p2, (p1 + p2) / 2f, IsConnectionPowered(conn), 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 8f))
				});
			}
			
			// Ligne de connexion en cours (mode placement)
			if (Program.IsConnectingWire() && Program.GetWireStartTile() != (-1, -1)
				&& Program.IsTileLit(Program.GetWireStartTile().x, Program.GetWireStartTile().y))
			{
				var startTile = Program.GetWireStartTile();
				Vector2 startPos = new Vector2(startTile.x * ts + ts / 2f, startTile.y * ts + ts / 2f);
				startPos.Y -= GetHeightAt(startTile.x, startTile.y) * ts / 4f;
				
				Vector2 endPos = mouseWorldPos;
				float sortY = Math.Min(startPos.Y, endPos.Y);
				
				renderItems.Add(new RenderItem
				{
					Y = sortY,
					Kind = RenderItemKind.Wire,
					Tag = new WireRenderTag(startPos, endPos, (startPos + endPos) / 2f, false, 0f)
				});
			}

			// Joueurs locaux supplémentaires (visibles dans toutes les vues)
			if (playersToRender != null)
			{
				foreach (var extraPlayer in playersToRender)
				{
					if (!extraPlayer.Connected) continue;
					if (!IsEntityNearScreen(extraPlayer.Position)) continue;
					int playerTileX = (int)(extraPlayer.Position.X / ts);
					int playerTileY = (int)(extraPlayer.Position.Y / ts);
					int playerHeight = GetHeightAt(playerTileX, playerTileY);
					float playerYOffset = -playerHeight * ts / 4;
					Vector2 actualDrawPos = new Vector2(extraPlayer.Position.X, extraPlayer.Position.Y + playerYOffset);
					float playerSortY = actualDrawPos.Y + Program.FeetOffsetY;
					int hairIndex = extraPlayer.HairStyleOverride ?? Program.PlayerHairStyle;
					if (hairIndex < 0 || hairIndex >= hairBase.Count) hairIndex = 0;
					var playerHairBaseTex = hairBase.Count > 0 ? hairBase[hairIndex] : new Texture2D();
					var playerHairOverlayTex = hairOverlay.Count > 0 ? hairOverlay[hairIndex] : new Texture2D();
					var capturedExtraPlayer = extraPlayer;
					var capturedExtraDrawPos = actualDrawPos;
					var capturedExtraSkin = extraPlayer.SkinColorOverride ?? pSkin;
					var capturedExtraTint = extraPlayer.MorphTintOverride ?? capturedExtraSkin;
					var capturedExtraFeatureVariants = extraPlayer.MorphFeatureVariantsOverride;
					var capturedExtraFeatureColors = extraPlayer.MorphFeatureColorsOverride;
					var capturedExtraEquipment = extraPlayer.IsNetworkPlayer
						? Program.GetNetworkPlayerEquipmentFull(extraPlayer.Id, extraPlayer.Equip)
						: Program.equipment;
					var capturedExtraEyes = Program.EyesTexture;
					var capturedExtraMouth = Program.MouthTexture;
					var capturedExtraHairColor = extraPlayer.HairColorOverride ?? Program.HairColor;
					var capturedExtraUnderwear = Program.LeafUnderpantsTexture;
					//  Rotation de tête et regard propres à CE joueur distant
					float capturedExtraHeadAngle = extraPlayer.IsNetworkPlayer ? extraPlayer.HeadAngle : headAngle;
					var capturedExtraGaze = extraPlayer.IsNetworkPlayer
						? Program.ComputeGazeOffsetsFor(capturedExtraDrawPos, extraPlayer.LookWorldPos, capturedExtraPlayer.Facing)
						: (Program.PupilLeftOffset, Program.PupilRightOffset, Program.EyebrowLeftOffset, Program.EyebrowRightOffset);
					//  Item tenu en main : même rendu attaché au bras (texture "_rarmbottom") que
					// pour le joueur local, au lieu de l'icône statique flottante précédente.
					Texture2D capturedExtraHeldItemTex = EntityRenderer.GetHeldItemTextureById(capturedExtraPlayer.HeldItemId);
					renderItems.Add(new RenderItem
					{
						Y = playerSortY,
						DrawAction = () =>
						{
							EntityRenderer.DrawEntity(
								capturedExtraPlayer.SpeciesOverride ?? "human",
								capturedExtraPlayer.AnimState,
								capturedExtraPlayer.AnimFrame,
								capturedExtraPlayer.AnimProg,
								capturedExtraPlayer.Facing,
								capturedExtraDrawPos,
								(string.Equals(capturedExtraPlayer.SpeciesOverride, "human", StringComparison.OrdinalIgnoreCase)
									? capturedExtraSkin : capturedExtraTint),
								playerHairBaseTex,
								playerHairOverlayTex,
								capturedExtraHairColor,
								SpeciesData.Skeletons,
								capturedExtraEyes,
								capturedExtraMouth,
								customScale: 1.0f,
								equipment: capturedExtraEquipment,
								isCarrying: false,
								inWater: isPlayerInWater,
									attackSwingProgress: capturedExtraPlayer.AttackSwingProgress,
								heldItemTexture: capturedExtraHeldItemTex,
								headAngle: capturedExtraHeadAngle,
								keepItemHorizontal: false,
								isBow: false,
underwearTexture: capturedExtraUnderwear,
									beardStyle: capturedExtraPlayer.BeardStyleOverride ?? 0,
									eyeStyleOverride: capturedExtraPlayer.EyeStyleOverride,
									eyeColorOverride: capturedExtraPlayer.EyeColorOverride,
									pupilLeftOffset: capturedExtraGaze.Item1,
									pupilRightOffset: capturedExtraGaze.Item2,
									eyebrowLeftOffset: capturedExtraGaze.Item3,
									eyebrowRightOffset: capturedExtraGaze.Item4,
									randomFeatureVariant: capturedExtraFeatureVariants,
									randomFeatureColor: capturedExtraFeatureColors);

							//  MULTIJOUEUR : pseudo + barre de vie
							// PVP : si ce joueur a activé le PVP, on ne l'affiche pas pour ne pas
							// pouvoir le localiser (ni pseudo, ni barre de vie).
							if (capturedExtraPlayer.IsNetworkPlayer && !capturedExtraPlayer.PvpEnabled)
							{
								string displayedSpecies = capturedExtraPlayer.SpeciesOverride ?? "human";
								float visualTopY = EntityRenderer.GetVisualTopY(
									displayedSpecies,
									capturedExtraPlayer.AnimState,
									capturedExtraPlayer.AnimFrame,
									capturedExtraPlayer.AnimProg,
									capturedExtraPlayer.Facing,
									capturedExtraDrawPos);

								float barWidth = 48f;
								float barHeight = 7f;
								float barY = visualTopY - 13f;
								float barX = capturedExtraDrawPos.X - barWidth / 2f;

								// Pseudo — remonté au-dessus de la tête (au lieu de -78) pour ne plus
								// être en superposition avec les cheveux/le crâne du joueur, avec une
								// bordure noire pour rester lisible sur tous les fonds.
								if (!string.IsNullOrEmpty(capturedExtraPlayer.DisplayName))
								{
									int nameWidth = FontManager.MeasureText(capturedExtraPlayer.DisplayName, 14);
									int nameX = (int)capturedExtraDrawPos.X - nameWidth / 2;
									int nameY = (int)barY - 19;
									// Bordure noire : le texte est redessiné en noir tout autour (4 ou 8
									// directions) avant la passe finale en blanc, par-dessus.
									foreach (var (ox, oy) in new (int, int)[] { (-1, -1), (1, -1), (-1, 1), (1, 1), (-1, 0), (1, 0), (0, -1), (0, 1) })
									{
										FontManager.DrawText(capturedExtraPlayer.DisplayName,
											nameX + ox, nameY + oy,
											14, new Color(0, 0, 0, 230));
									}
									FontManager.DrawText(capturedExtraPlayer.DisplayName,
										nameX, nameY,
										14, new Color(255, 255, 255, 230));
								}
								// Barre de vie ancrée au sommet réel du squelette, comme pour les entités.
								if (capturedExtraPlayer.MaxHP > 0)
								{
									float ratio = Math.Clamp((float)capturedExtraPlayer.HP / capturedExtraPlayer.MaxHP, 0f, 1f);
									Rectangle barRect = new Rectangle(barX, barY, barWidth, barHeight);
								Rectangle fillRect = new Rectangle(barX + 1.5f, barY + 1.5f, Math.Max(0f, (barWidth - 3f) * ratio), barHeight - 3f);
								Raylib.DrawRectangleRounded(new Rectangle(barX - 2f, barY - 2f, barWidth + 4f, barHeight + 4f), 0.45f, 8, new Color(0, 0, 0, 105));
								Raylib.DrawRectangleRounded(barRect, 0.45f, 8, new Color(32, 20, 24, 225));
								if (fillRect.Width > 0f)
								{
									Color fillColor = ratio < 0.3f
										? new Color(225, 65, 58, 255)
										: ratio < 0.6f
											? new Color(238, 165, 58, 255)
											: new Color(90, 210, 105, 255);
									Raylib.DrawRectangleRounded(fillRect, 0.35f, 6, fillColor);
									Raylib.DrawRectangleRounded(
										new Rectangle(fillRect.X + 1f, fillRect.Y + 0.5f, Math.Max(0f, fillRect.Width - 2f), 1.5f),
										0.5f, 6, new Color(255, 255, 255, 85));
								}
								Raylib.DrawRectangleRoundedLines(barRect, 0.45f, 8, 1, new Color(255, 220, 220, 150));
								}
							}
						}
					});
				}
			}

			// Joueur ou voiture
			//  RAGDOLL : pendant le ragdoll de mort, le joueur n'est plus dessiné avec l'animation
			// normale (le squelette physique est dessiné séparément par PlayerRagdoll.Draw).
			if (drawPlayer && Program.PlayerCurrentCar == null && !Program.IsPlayerRagdolled)
			{
				int playerTileX_h = (int)(playerPos.X / ts);
				int playerTileY_h = (int)(playerPos.Y / ts);
				int playerHeight = GetHeightAt(playerTileX_h, playerTileY_h);
				float playerYOffset = -playerHeight * ts / 4;
				Item? heldItem = null;
				var hotbarSlots = Program.GetHotbarSlotsCached(); //  OPTIM : cache par frame
				if (Program.hotbarSlot < hotbarSlots.Count && !hotbarSlots[Program.hotbarSlot].IsEmpty)
					heldItem = hotbarSlots[Program.hotbarSlot].Item;
				int hairIndex = Program.PlayerHairStyle;
				if (hairIndex < 0 || hairIndex >= hairBase.Count) hairIndex = 0;
				var playerHairBaseTex = hairBase.Count > 0 ? hairBase[hairIndex] : new Texture2D();
				var playerHairOverlayTex = hairOverlay.Count > 0 ? hairOverlay[hairIndex] : new Texture2D();
				var playerPosForDraw = playerPos;
				var playerYOffsetForDraw = playerYOffset;
				var mountedAnimal = Program.MountedAnimal;
				// Le sens du cavalier suit toujours celui de la monture, pour ne jamais les voir
				// tournés dans des directions différentes.
				var playerFacing = mountedAnimal != null ? mountedAnimal.Facing : facing;
				var playerAnim = anim;
				var playerFrame = frame;
				var playerProg = prog;
				var playerSkin = pSkin;
				var playerSkeletons = SpeciesData.Skeletons;
				var playerEyes = Program.EyesTexture;
				var playerMouth = Program.MouthTexture;
				var playerEquipment = Program.equipment;
				Texture2D heldItemTex = default;
				string heldItemAnchor = "rarmbottom";
				var carried = Program.CurrentCarriedFurniture;
				if (carried != null)
				{
					var tileDataCarry = WorldTileRegistry.GetTile(carried.PlaceableId);
					if (tileDataCarry != null) heldItemTex = WorldTileRegistry.GetFirstTexture(carried.PlaceableId);
				}
				else if (heldItem != null)
				{
					var heldItemInfo = EntityRenderer.GetHeldItemTextureInfo(heldItem);
					heldItemTex = heldItemInfo.Texture;
					heldItemAnchor = heldItemInfo.AnchorPart;
				}
				bool isFurniture = carried != null;
				var carriedCreatureRef = Program.CurrentCarriedCreature;
				bool isCarryingCreature = carriedCreatureRef != null;
				Vector2 actualDrawPos;
				if (sittingPosition.HasValue) actualDrawPos = Program.GetSittingVisualPosition();
				else actualDrawPos = new Vector2(playerPosForDraw.X, playerPosForDraw.Y + playerYOffsetForDraw);
						float tempIntensity = TemperatureSystem.CurrentIntensity;
						if (TemperatureSystem.IsCold)
						{
							float shake = Math.Clamp(Math.Abs(tempIntensity) * 3f, 0f, 5f);
							float phase = (float)Raylib.GetTime() * 24f;
							actualDrawPos += new Vector2(
								MathF.Sin(phase) * shake * 0.25f,
								MathF.Cos(phase * 1.2f) * shake * 0.08f
							);
						}
				float playerSortY = actualDrawPos.Y + Program.FeetOffsetY;
				if (Program.IsSitting)
					playerSortY = actualDrawPos.Y + (Program.IsSittingBehindFurniture ? 0f : 80f);
				bool isBow = false;
				if (heldItem != null)
				{
					isBow = GameData.IsRangedWeapon(heldItem.Name);
				}

				// --- DÉBUT MORPHOSE ---
				string speciesToDraw = Program.PlayerMorphSpecies; // "human" par défaut

				Texture2D morphHairBase = new Texture2D();
				Texture2D morphHairOverlay = new Texture2D();
				Color morphHairColor = Color.Black;
				Texture2D morphEyes = new Texture2D();
				Texture2D morphMouth = new Texture2D();
				Equipment? morphEquipment = null;
				Color morphTint = playerSkin; // par défaut (pour human)
				Texture2D underwearTex = Program.LeafUnderpantsTexture; // par défaut

				if (speciesToDraw == "human")
				{
					// Mode humain : on conserve tous les éléments personnalisés
					morphHairBase = playerHairBaseTex;
					morphHairOverlay = playerHairOverlayTex;
					morphHairColor = Program.HairColor;
					morphEyes = playerEyes;
					morphMouth = playerMouth;
					morphEquipment = playerEquipment;
					morphTint = playerSkin;
					underwearTex = Program.LeafUnderpantsTexture; // on garde le pagne
				}
				else
				{
					// Les espèces non humaines gardent leurs traits propres; les humanoïdes
					// conservent toutefois l'équipement du joueur.
					morphTint = Program.PlayerMorphTint;
					morphEquipment = SpeciesData.IsHumanoid(speciesToDraw) ? playerEquipment : null;
					underwearTex = new Texture2D(); // pas de pagne
				}
				// --- FIN MORPHOSE ---

				renderItems.Add(new RenderItem
				{
					Y = playerSortY,
					DrawAction = () =>
					{
						EntityRenderer.DrawEntity(
							speciesToDraw,                // ← espèce morphée (human ou autre)
							playerAnim, playerFrame, playerProg, playerFacing,
							actualDrawPos, morphTint,     // ← teinte adaptée
							morphHairBase, morphHairOverlay, morphHairColor,
							playerSkeletons, morphEyes, morphMouth,
							customScale: 1.0f, equipment: morphEquipment,
							isCarrying: isCarrying || isCarryingCreature, inWater: isPlayerInWater,
							isCarryingCreature: isCarryingCreature,
							attackSwingProgress: Program.GetAttackSwingProgress(),
							heldItemTexture: heldItemTex,
							heldItemInstance: heldItem,
							headAngle: headAngle,
							keepItemHorizontal: isFurniture,
							isBow: isBow,
							underwearTexture: underwearTex, // ← texture adaptée
							beardStyle: Program.PlayerBeardStyle,
							randomFeatureVariant: Program.PlayerMorphFeatureVariants,
							randomFeatureColor: Program.PlayerMorphFeatureColors,
							drawCarriedEntity: isCarryingCreature ? () =>
							{
								string carriedSpeciesKey = carriedCreatureRef!.Species.ToLower();
								if (!SpeciesData.Skeletons.ContainsKey(carriedSpeciesKey)) return;

								Vector2 carriedPos = EntityRenderer.GetCarryAnchorWorldPos(
													speciesToDraw, playerFacing, actualDrawPos, playerSkeletons,
									anim: playerAnim, frame: playerFrame, prog: playerProg);
								// GetCarryAnchorWorldPos renvoie le PIVOT de rarmbottom (coude/poignet),
								// pas la main. On pousse donc vers l'avant/le bas pour amener la créature
								// à la position tenue, bien au bout du bras.
								carriedPos += new Vector2(24f * playerFacing, 4f);

								EntityRenderer.DrawEntity(
									carriedSpeciesKey,
									"hold", 0, 0f, playerFacing,
									carriedPos, carriedCreatureRef.Tint,
									default, default, Color.White,
									playerSkeletons,
									eyes: EntityRenderer.GetEyesTextureForSpecies(carriedSpeciesKey),
									customScale: 1.0f,
									randomFeatureVariant: carriedCreatureRef.RandomFeatureVariant,
									randomFeatureColor: carriedCreatureRef.RandomFeatureColor
								);
							} : null,
							drawMount: mountedAnimal != null ? () =>
							{
								var m = mountedAnimal;
								if (!m.IsAlive) return;
								Texture2D mHairBase = hairBase.Count > m.HairStyle ? hairBase[m.HairStyle] : new Texture2D();
								Texture2D mHairOverlay = hairOverlay.Count > m.HairStyle ? hairOverlay[m.HairStyle] : new Texture2D();
								EntityRenderer.DrawEntity(
									m.Species, m.AnimState, m.CurrentFrame, m.AnimProgress,
									// La monture garde son propre Facing (synchronisé avec celui du joueur
									// au moment du déplacement), afin que le cavalier et l'animal soient
									// toujours orientés dans le même sens.
									m.Facing, m.WorldPos, m.Tint,
									mHairBase, mHairOverlay, m.HairColor,
									playerSkeletons,
									eyes: EntityRenderer.GetEyesTextureForSpecies(m.Species),
									mouth: Program.GetMouthTextureFor(m), forceBlinking: m.IsBlinking,
									customScale: m.Scale,
									equipment: m.Equipment,
									isCarrying: false, inWater: m.IsInWater,
									attackSwingProgress: 0f,
									flashWhite: m.DamageFlash > 0f,
									pupilLeftOffset: m.PupilLeftOffset,
									pupilRightOffset: m.PupilRightOffset,
									eyebrowLeftOffset: m.EyebrowLeftOffset,
									eyebrowRightOffset: m.EyebrowRightOffset,
									randomFeatureVariant: m.RandomFeatureVariant,
									randomFeatureColor: m.RandomFeatureColor,
									beardStyle: m.BeardStyle);
							} : null
						);
					}
				});
			}

			if (Program.PlayerCurrentWagonTile.HasValue)
			{
				Vector2 wagonPos = Program.GetMountedWagonDrawPosition();
				if (IsEntityNearScreen(wagonPos))
				{
					renderItems.Add(new RenderItem
					{
						Y = wagonPos.Y + Program.FeetOffsetY + 0.5f,
						DrawAction = () => Program.DrawMountedWagonFront(
							Program.GetMountedWagonDrawPosition(),
							(int)Program.TileSize,
							Program.IsMountedWagonVertical())
					});
				}
			}

			// Projectiles magiques : intégrés au tri par Y pour réagir correctement aux
			// hitbox des tuiles/entités (passer devant/derrière selon leur profondeur),
			// au lieu d'être dessinés dans une passe séparée toujours au-dessus de tout.
			if (spellProjectiles != null)
			{
				foreach (var sp in spellProjectiles)
				{
					var spRef = sp;
					if (!Program.IsTileLit((int)(spRef.Position.X / ts), (int)(spRef.Position.Y / ts)))
						continue;
					renderItems.Add(new RenderItem
					{
						Y = spRef.Position.Y,
						Kind = RenderItemKind.Projectile,
						Tag = new ProjectileRenderTag(spRef, spRef.Position, spRef.Size, spRef.VisualRotation, spRef.VisualTexture, spRef.Color, spRef.VisualTexture.Id != 0)
					});
				}
			}

			// Éléments externes (offrandes du RitualCircleUI, etc.) : injectés ici pour être triés
			// par Y avec le reste du monde, puis la liste externe est vidée pour la frame suivante.
			if (_externalRenderItems.Count > 0)
			{
				renderItems.AddRange(_externalRenderItems);
				_externalRenderItems.Clear();
			}

			// Tri et dessin
			//  Fix flicker "gros sprites" (ex : deux arbres voisins) : on assigne d'abord un index
			// d'insertion stable à chaque élément, puis on l'utilise comme départage lorsque deux
			// éléments ont un Y de tri identique (ou quasi identique). Sans ce départage explicite,
			// List<T>.Sort() (non stable) pouvait inverser aléatoirement l'ordre de deux sprites à
			// Y égal d'une frame à l'autre, ce qui les faisait clignoter/se repasser devant l'un
			// l'autre. Avec ce départage, l'ordre devient un ordre total déterministe : à
			// configuration égale, deux sprites à Y égal se dessinent toujours dans le même ordre.
			for (int ri = 0; ri < renderItems.Count; ri++)
			{
				var tmp = renderItems[ri];
				tmp.SortIndex = ri;
				renderItems[ri] = tmp;
			}
			renderItems.Sort((a, b) =>
			{
				int layerComparison = a.SortLayer.CompareTo(b.SortLayer);
				if (layerComparison != 0) return layerComparison;
				int c = a.Y.CompareTo(b.Y);
				if (c != 0) return c;
				return a.SortIndex.CompareTo(b.SortIndex);
			});
			foreach (var item in renderItems) item.Draw();
			_renderItemsPool = renderItems; // On garde la liste pour reuse
			
			// Hitboxes debug
			if (showDebug)
			{
				foreach (var entity in entities)
				{
					if (!entity.IsAlive) continue;
					Vector2 entityDebugPos = entity.VisualWorldPos;
					if (!ShouldRenderEntity(entityDebugPos)) continue;
					Rectangle hitboxRect = GetEntityCollisionHitbox(entityDebugPos, entity.Species, ts);
					Rectangle damageHitboxRect = GetEntityDamageHitbox(entity, ts);
					Raylib.DrawRectangleLinesEx(hitboxRect, 2, Color.Red);
					Raylib.DrawRectangleLinesEx(damageHitboxRect, 2, Color.Blue);
					string speciesName = entity.Species;
					int textWidth = Raylib.MeasureText(speciesName, 10);
					Raylib.DrawText(speciesName,
						(int)(hitboxRect.X + hitboxRect.Width / 2 - textWidth / 2),
						(int)(hitboxRect.Y - 12), 10, Color.White);

					//  Trajectoire IA (voir Entity.DrawPath) : dessinée ici, au même endroit et de
					// la même façon que la hitbox ci-dessus (coordonnées MONDE directes, on est
					// toujours dans le BeginMode2D de la caméra). Avant, ce dessin était fait dans
					// Program.cs via GetWorldToScreen2D alors qu'on était déjà dans un bloc
					// BeginMode2D : ça double-transformait les coordonnées (positions décalées et
					// fausses dès qu'on zoomait/dézoomait). Le placer ici, après le rendu du sol/
					// des objets, corrige aussi le fait que les lignes étaient recouvertes par le
					// sol dessiné ensuite.
					entity.DrawPath();

					//  Pile de priorités IA (du moins important en haut au plus important en
					// bas, juste au-dessus de la tête) : permet de voir en un coup d'œil ce que
					// le PNJ a choisi de faire et où ça se situe dans l'échelle des priorités,
					// notamment quand il vient d'abandonner un objectif inaccessible (voir
					// Entity.AbandonCurrentGoal).
					entity.DrawPriorityDebugLabels();
				}

				if (drawPlayer)
				{
					Rectangle playerCollision = GetEntityCollisionHitbox(playerPos, Program.GetPlayerCollisionDimensions(), ts);
					Rectangle playerDamage = GetEntityDamageHitbox(playerPos, Program.PlayerMorphSpecies, ts);
					Raylib.DrawRectangleLinesEx(playerCollision, 2, Color.Red);
					Raylib.DrawRectangleLinesEx(playerDamage, 2, Color.Blue);
					Raylib.DrawText("joueur local", (int)playerDamage.X, (int)(playerDamage.Y - 12), 10, Color.White);
				}

				if (playersToRender != null)
				{
					foreach (var remotePlayer in playersToRender)
					{
						string species = remotePlayer.SpeciesOverride ?? "human";
						Rectangle playerCollision = GetEntityCollisionHitbox(remotePlayer.Position, Program.GetPlayerCollisionDimensions(), ts);
						Rectangle playerDamage = GetEntityDamageHitbox(remotePlayer.Position, species, ts);
						Raylib.DrawRectangleLinesEx(playerCollision, 2, Color.Red);
						Raylib.DrawRectangleLinesEx(playerDamage, 2, Color.Blue);
						string label = string.IsNullOrWhiteSpace(remotePlayer.DisplayName) ? "joueur réseau" : remotePlayer.DisplayName;
						Raylib.DrawText(label, (int)playerDamage.X, (int)(playerDamage.Y - 12), 10, Color.White);
					}
				}

				//  Hitboxes de TOUS les objets du monde visibles (meubles, arbres, murs, etc.),
				// via la même géométrie que GetCollisionRect (celle réellement utilisée pour les
				// collisions et pour la détection de clic). Avant, seules les entités et les
				// quelques tuiles voisines débordées par une hitbox (contour orange ci-dessous)
				// étaient visibles en debug — l'objet lui-même n'affichait jamais sa hitbox.
				for (int dhx = startX; dhx <= endX; dhx++)
				{
					for (int dhy = startY; dhy <= endY; dhy++)
					{
						int dhObjectId = GetObjectIdAt(dhx, dhy);
						if (dhObjectId == 0) continue;
						var dhTileData = WorldTileRegistry.GetTile(dhObjectId);
						if (dhTileData == null) continue;
						var (dhX, dhY, dhW, dhH) = GetCollisionRect(dhx, dhy, dhTileData, ts);
						Raylib.DrawRectangleLinesEx(new Rectangle(dhX, dhY, dhW, dhH), 2, Color.Lime);
					}
				}

				//  DIAGNOSTIC : contour orange sur chaque tuile visible que le pathfinding
				// considère bloquée alors qu'elle n'a AUCUN objectId propre (donc pas détectée
				// par le check direct "objectId != 0 && !Walkable" — uniquement via le
				// débordement de hitbox d'un objet voisin, voir GetHitboxBlockedTiles). Sert à
				// vérifier visuellement si une tuile sur laquelle un PNJ fonce est vue comme
				// libre ou bloquée par le pathfinding, et par quel mécanisme.
				if (visibleObjectCount > 0)
				{
					var pfBlocked = GetHitboxBlockedTiles(startX, endX, startY, endY, destroyed);
					for (int dbx = startX; dbx <= endX; dbx++)
					{
						for (int dby = startY; dby <= endY; dby++)
						{
							if (!pfBlocked.Contains((dbx, dby))) continue;
							if (GetObjectIdAt(dbx, dby) != 0) continue; // déjà visible via l'objet lui-même
							int dbHeight = GetHeightAt(dbx, dby);
							int dbBaseY = dby * ts - (dbHeight * ts / 4);
							Raylib.DrawRectangleLinesEx(new Rectangle(dbx * ts, dbBaseY, ts, ts), 2, Color.Orange);
						}
					}
				}
			}

			// Interactions UI
			var interactionsByTile = new Dictionary<(int x, int y), List<(string actionKey, string actionType, Texture2D icon)>>();
			for (int ix = 0; ix < width; ix++)
			{
				int worldX = startX + ix;
				for (int iy = 0; iy < heightTiles; iy++)
				{
					int worldY = startY + iy;
					int localIx = ix;   // ← COPIE LOCALE
					int localIy = iy;   // ← COPIE LOCALE
					int objectId = objectIds[ix, iy];
					if (objectId == 0) continue;
					var tileData = WorldTileRegistry.GetTile(objectId);
					if (tileData == null) continue;
					bool isCaveEntryExit = (objectId == 50 || objectId == 52);
					bool isStation = tileData.IsStation == true;
					bool isContainer = tileData.IsContainer == true;
					bool isArmorStand = tileData.IsArmorStand == true;
					bool isFurniture = tileData.IsFurniture;
					bool isCrop = tileData.IsCrop;
					if (!isCaveEntryExit && !isStation && !isContainer && !isArmorStand && !isFurniture && !isCrop) continue;
					var key = (worldX, worldY);
					if (!interactionsByTile.ContainsKey(key))
						interactionsByTile[key] = new List<(string actionKey, string actionType, Texture2D icon)>();
					if (isCaveEntryExit)
					{
						string actionType = (objectId == 50) ? "enterCave" : "exitCave";
						interactionsByTile[key].Add(("E", actionType, GetInteractionIcon(objectId, actionType)));
					}
					if (isStation) interactionsByTile[key].Add(("E", "craft", GetInteractionIcon(objectId, "craft")));
					if (isContainer) interactionsByTile[key].Add(("E", "E", GetInteractionIcon(objectId, "E")));
					if (isArmorStand) interactionsByTile[key].Add(("E", "E", GetInteractionIcon(objectId, "E")));
					if (objectId == 86) interactionsByTile[key].Add(("E", "E", GetInteractionIcon(objectId, "E")));
					if (isCrop)
					{
						var cropData = GetCropDataAt(worldX, worldY);
						bool cropIsReady = cropData != null && cropData.AccumulatedGrowthTime >= tileData.GrowthTime;
						if (cropIsReady)
							interactionsByTile[key].Add(("E", "E", GetInteractionIcon(objectId, "E")));
					}
					if (isFurniture && !isCarrying) interactionsByTile[key].Add(("F", "F", GetInteractionIcon(objectId, "F")));
				}
			}
			foreach (var tile in interactionsByTile)
			{
				int x = tile.Key.x, y = tile.Key.y;
				var interactions = tile.Value;
				int objectId = GetObjectIdAt(x, y);
				if (objectId == 0) continue;
				var tileData = WorldTileRegistry.GetTile(objectId);
				if (tileData == null) continue;
				var (hitboxX, hitboxY, hitboxW, hitboxH) = GetCollisionRect(x, y, tileData, ts);
				Rectangle objRect = new Rectangle(hitboxX, hitboxY, hitboxW, hitboxH);
				Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), Program.GetCurrentCamera());
				if (Raylib.CheckCollisionPointRec(mouseWorld, objRect))
				{
					float objectX = x * ts + (tileData.DrawOffset?.X ?? 0);
					float objectY = y * ts - (GetHeightAt(x, y) * ts / 4) + ts;
					float objectWidth = ts;
					float objectHeight = ts;
					if (tileData.Size != null)
					{
						objectWidth = ts * tileData.Size.Width;
						objectHeight = ts * tileData.Size.Height;
						objectY -= objectHeight;
						objectY -= tileData.DrawOffset?.Y ?? 0;
					}
					else
					{
						objectY -= ts;
					}
					float centerX = objectX + objectWidth / 2f;
					float baseIconY = objectY - ts / 2f - 10f;
					for (int i = 0; i < interactions.Count; i++)
					{
						var (actionKey, actionType, iconTex) = interactions[i];
						Color textColor = actionType switch
						{
							"craft" => new Color(100, 200, 100, 255),
							"enterCave" => new Color(200, 150, 50, 255),
							"exitCave" => new Color(100, 150, 200, 255),
							"F" => new Color(100, 200, 255, 255),
							_ => Color.Yellow
						};
						float iconSize = 20f;
						float buttonSize = 28f;
						float actionGroupWidth = iconSize + 4f + buttonSize;
						float iconX = centerX - actionGroupWidth / 2f;
						float yOffsetList = i * (iconSize + 2f);
						float currentY = baseIconY - yOffsetList;
						if (iconTex.Id != 0)
						{
							Raylib.DrawTexturePro(iconTex,
								new Rectangle(0, 0, iconTex.Width, iconTex.Height),
								new Rectangle(iconX, currentY, iconSize, iconSize),
								Vector2.Zero, 0, Color.White);
						}
						float buttonX = iconX + iconSize + 4f;
						float buttonY = currentY + (iconSize - buttonSize) / 2;
						if (_inputButtonTexture.Id != 0)
						{
							Raylib.DrawTexturePro(_inputButtonTexture,
								new Rectangle(0, 0, _inputButtonTexture.Width, _inputButtonTexture.Height),
								new Rectangle(buttonX, buttonY, buttonSize, buttonSize),
								Vector2.Zero, 0, Color.White);
							int fontSize = 12;
							int textWidth = Raylib.MeasureText(actionKey, fontSize);
							float textX = buttonX + (buttonSize - textWidth) / 2;
							float textY = buttonY + (buttonSize - fontSize) / 2;
							Raylib.DrawText(actionKey, (int)textX, (int)textY, fontSize, Color.White);
						}
						else
						{
							Raylib.DrawRectangleRounded(new Rectangle(buttonX, buttonY, buttonSize, buttonSize), 0.2f, 6, new Color(40, 40, 50, 200));
							Raylib.DrawRectangleRoundedLines(new Rectangle(buttonX, buttonY, buttonSize, buttonSize), 0.2f, 6, 1, new Color(150, 150, 170, 200));
							int fontSize = 12;
							int textWidth = Raylib.MeasureText(actionKey, fontSize);
							float textX = buttonX + (buttonSize - textWidth) / 2;
							float textY = buttonY + (buttonSize - fontSize) / 2;
							Raylib.DrawText(actionKey, (int)textX, (int)textY, fontSize, textColor);
						}
					}
				}
			}

			if (_isUnderground)
			{
				bool nearExit = false;
				int playerTileX_und = (int)(playerPos.X / ts);
				int playerTileY_und = (int)(playerPos.Y / ts);
				for (int dx = -3; dx <= 3; dx++)
				{
					for (int dy = -3; dy <= 3; dy++)
					{
						if (GetObjectIdAt(playerTileX_und + dx, playerTileY_und + dy) == 52)
						{
							nearExit = true;
							break;
						}
					}
					if (nearExit) break;
				}
				string caveText = nearExit ? " GROTTE - Cherchez la sortie lumineuse" : " GROTTE";
				int tw = Raylib.MeasureText(caveText, 16);
				Raylib.DrawText(caveText, (sw - tw) / 2, sh - 40, 16, new Color(200, 150, 50, 200));
			}
		}
    }
}
