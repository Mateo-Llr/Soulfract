// ChunkGeneration.cs
// Fichier extrait de World.Core.cs — classe partielle World.
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {
		// Partagé entre les arbres générés : évite de recréer le tableau des espèces à chaque arbre.
		private static readonly string[] NestingBirdSpeciesCandidates = { "Sparrow", "Pigeon", "Crow" };

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
					int height;
					if (_isUnderground)
					{
						height = _isInDungeon
							? (int)(PerlinNoise.Noise(worldX * 0.02f, worldY * 0.02f) * 3f + 4f)
							: 0;
						height = Math.Clamp(height, 0, 7);
					}
					else
					{
						height = GetHeightAt(worldX, worldY);
					}
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
				// Zone dégagée autour du socle d'âmes : ni arbre, ni buisson, ni rocher.
				if (!_isUnderground && IsSpawnProtected(worldX, worldY, SPAWN_CLEAR_RADIUS)) continue;
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
						var availableNestingBirds = NestingBirdSpeciesCandidates
							.Where(name => SpeciesData.Species.ContainsKey(name.ToLowerInvariant()))
							.ToArray();
						if (!_isUnderground && availableNestingBirds.Length > 0 && chunkRandom.NextDouble() < 0.10)
						{
							string nestingBird = availableNestingBirds[chunkRandom.Next(availableNestingBirds.Length)];
							chunkData.SetTileMeta(worldX, worldY, "tree_birds", nestingBird);
						}

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
					// Pas de village dont le centre (puits) serait trop proche du socle d'âmes.
					if (biomeAtCenter == Biome.Plains && !IsSpawnProtected(centralX, centralY, SPAWN_VILLAGE_MIN_DISTANCE))
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
							var villageEntityNetIds = Program.GetEntities()
								.Select(entity => entity.NetId)
								.ToHashSet();
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

									// Aucune maison (ni son espacement) dans la zone tampon autour du socle d'âmes.
									if (IsSpawnProtectedRect(rectLeft, rectTop, rectRight - rectLeft + 1, rectBottom - rectTop + 1, SPAWN_STRUCTURE_BUFFER))
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
														if (villageEntityNetIds.Add(cat.NetId))
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
												if (villageEntityNetIds.Add(villager.NetId))
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
										bannerChunk.BannerHolders[(bx, by)] = BannerHolderData.WithBanner(
											BannerHolderData.CreateBannerItem(chosenPattern, chosenShape, bgColor, patColor));
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

    }
}
