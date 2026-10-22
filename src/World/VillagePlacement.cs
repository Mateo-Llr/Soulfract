// VillagePlacement.cs
// Fichier extrait de World.Core.cs — classe partielle World.
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
				if (IsSpawnProtected(x, y, SPAWN_STRUCTURE_BUFFER)) continue;
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
					if (IsSpawnProtectedRect(posX, posY, fieldW, fieldH, SPAWN_STRUCTURE_BUFFER)) { rejectedOverlap++; continue; }

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
					if (IsSpawnProtected(x, y, SPAWN_STRUCTURE_BUFFER)) continue;
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


    }
}
