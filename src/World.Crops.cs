// World.Crops.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {

		private static Dictionary<(int x, int y), float> _farmlandMoisture = new();

		private const float FARMLAND_DRYING_TIME = 300f;

		// 5 minutes en secondes
		private static Dictionary<(int x, int y), int> _lastCropStage = new();

		private const float REGROWTH_INTERVAL = 30f;

		// Intervalle entre deux tentatives de repousse (secondes)
		private const int REGROWTH_ATTEMPTS_PER_CHUNK = 2;

		/// <summary>
        /// Rebascule sur le plan de chunks d'un donjon déjà généré, SANS téléporter le joueur ni
        /// recalculer de position d'apparition. À utiliser uniquement au chargement d'une partie
        /// dont le joueur était dans un donjon au moment de la sauvegarde (voir SaveData.IsInDungeon).
        /// </summary>
        public static bool RestoreDungeonPlane(int instanceId)
        {
            if (!_dungeonInstances.TryGetValue(instanceId, out var inst))
                return false;

            if (!_dungeonChunksByInstance.TryGetValue(instanceId, out var instanceChunks))
            {
                instanceChunks = new Dictionary<(int chunkX, int chunkY), ChunkData>();
                _dungeonChunksByInstance[instanceId] = instanceChunks;
            }

            _isInDungeon = true;
            _currentDungeonInstanceId = instanceId;
            _activeDungeonChunks = instanceChunks;

            if (!inst.Generated)
            {
                var rand = new Random(inst.Seed);
                EnsureDungeonRegionChunks(inst.OriginX, inst.OriginY, inst.Width, inst.Height);
                DungeonGenerator.GenerateDungeon(inst, rand);
                inst.Generated = true;
            }
			else if (inst.BossDefeated)
			{
				DungeonGenerator.EnsureExitApproach(inst);
			}
            return true;
        }

		public static bool IsFarmlandWet(int x, int y)
		{
			return _farmlandMoisture.TryGetValue((x, y), out float time) && time > 0f;
		}

		public static void WaterFarmland(int x, int y)
		{
			_farmlandMoisture[(x, y)] = FARMLAND_DRYING_TIME;
			//  S'assurer que le sol est en terre humide (ID 29)
			SetGroundTile(x, y, 29);
		}

		public static void SyncFarmlandMoistureWithGroundOverrides()
		{
			var chunksDict = GetChunksDict();
			foreach (var chunk in chunksDict.Values)
			{
				foreach (var kv in chunk.GroundOverrides)
				{
					var (x, y) = kv.Key;
					int groundId = kv.Value;
					if (groundId == 29 && !_farmlandMoisture.ContainsKey((x, y)))
					{
						_farmlandMoisture[(x, y)] = FARMLAND_DRYING_TIME;
					}
				}
			}
		}

		public static void RefreshAllCropStages(float currentTime)
		{
			// Le stade est désormais calculé à partir du temps de croissance RÉELLEMENT
			// ACCUMULÉ (cropData.AccumulatedGrowthTime), qui n'avance que pendant que le sol
			// est humide (voir UpdateFarmlandMoisture). Il n'y a donc plus besoin de distinguer
			// "sol humide" / "sol sec" ici : quand le sol est sec, AccumulatedGrowthTime n'a
			// simplement pas progressé depuis la dernière fois, donc le stade reste figé.

			// Parcourir les chunks de surface
			foreach (var chunk in _chunks.Values)
			{
				foreach (var kv in chunk.Crops)
				{
					var (x, y) = kv.Key;
					var cropData = kv.Value;
					var tileData = WorldTileRegistry.GetTile(cropData.CropTileId);
					if (tileData == null) continue;

					float elapsed = cropData.AccumulatedGrowthTime;
					float stageFloat = (elapsed / tileData.GrowthTime) * tileData.GrowthStages;
					int stage = Math.Clamp((int)MathF.Floor(stageFloat), 1, tileData.GrowthStages);
					_lastCropStage[(x, y)] = stage;
				}
			}

			// Faire de même pour les grottes
			foreach (var chunk in _caveChunks.Values)
			{
				foreach (var kv in chunk.Crops)
				{
					var (x, y) = kv.Key;
					var cropData = kv.Value;
					var tileData = WorldTileRegistry.GetTile(cropData.CropTileId);
					if (tileData == null) continue;

					float elapsed = cropData.AccumulatedGrowthTime;
					float stageFloat = (elapsed / tileData.GrowthTime) * tileData.GrowthStages;
					int stage = Math.Clamp((int)MathF.Floor(stageFloat), 1, tileData.GrowthStages);
					_lastCropStage[(x, y)] = stage;
				}
			}
		}

		public static void UpdateFarmlandMoisture(float dt)
		{
			var moistureKeys = _farmlandMoisture.Keys.ToList();
			
			foreach (var key in moistureKeys)
			{
				int x = key.x, y = key.y;
				bool waterConsumed = false;

				var cropChunk = GetChunkAt(x, y);
				if (cropChunk != null && cropChunk.Crops.TryGetValue((x, y), out var cropData))
				{
					var tileData = WorldTileRegistry.GetTile(cropData.CropTileId);
					if (tileData != null && tileData.IsCrop)
					{
						// La plante ne pousse que tant que le sol est humide : on accumule dt
						// uniquement pendant que la tuile est dans _farmlandMoisture (donc humide).
						cropData.AccumulatedGrowthTime += dt;

						float elapsed = cropData.AccumulatedGrowthTime;
						float stageFloat = (elapsed / tileData.GrowthTime) * tileData.GrowthStages;
						int currentStage = Math.Clamp((int)MathF.Floor(stageFloat), 1, tileData.GrowthStages);
						var cropKey = (x, y);

						if (tileData.RegrowsAfterHarvest && elapsed >= tileData.GrowthTime &&
							GetTileMeta(x, y, "crop_state") == "harvested")
						{
							RemoveTileMeta(x, y, "crop_state");
						}

						// Récupérer le dernier stade connu
						if (!_lastCropStage.TryGetValue(cropKey, out int lastStage))
						{
							_lastCropStage[cropKey] = currentStage;
							lastStage = currentStage;
						}

						// Drainer uniquement lors du passage à un nouveau stade, y compris le stade mature.
						bool shouldConsume = currentStage > lastStage;
						if (shouldConsume && !waterConsumed)
						{
							waterConsumed = true;
							_farmlandMoisture.Remove(key);
							EnsureTileLoaded(x, y);
							SetGroundTile(x, y, 27);
							_lastCropStage[cropKey] = currentStage;
						}
						else
						{
							// Mettre à jour le dernier stade même si l'eau n'est pas consommée
							// pour que le prochain changement soit détecté
							if (currentStage != lastStage)
								_lastCropStage[cropKey] = currentStage;
						}
					}
				}

				// Séchage naturel
				if (!waterConsumed && _farmlandMoisture.TryGetValue(key, out float moisture))
				{
					moisture -= dt;
					if (moisture <= 0f)
					{
						_farmlandMoisture.Remove(key);
						EnsureTileLoaded(x, y);
						SetGroundTile(x, y, 27);
					}
					else
					{
						_farmlandMoisture[key] = moisture;
					}
				}
			}
		}

		//  Pousse des arbres plantés (jeunes arbres, plantType "classic" côté items.json,
		// ex : le gland). Contrairement aux cultures, elle ne dépend d'aucune humidité de
		// farmland : le temps s'accumule à chaque tick, sans condition. Une fois GrowthTime
		// atteint, la tuile "jeune arbre" est remplacée par sa tuile adulte (AdultTileId,
		// ex : Tree id=4), qui se comporte alors comme n'importe quel arbre existant.
		public static void UpdateSaplingGrowth(float dt)
		{
			ProcessSaplingGrowthForChunks(_chunks.Values, dt, isUnderground: false);
			ProcessSaplingGrowthForChunks(_caveChunks.Values, dt, isUnderground: true);
		}

		private static void ProcessSaplingGrowthForChunks(IEnumerable<ChunkData> chunks, float dt, bool isUnderground)
		{
			foreach (var chunk in chunks)
			{
				if (chunk.Crops.Count == 0) continue;

				List<(int x, int y)>? readyToGrow = null;

				foreach (var kv in chunk.Crops)
				{
					var (x, y) = kv.Key;
					var cropData = kv.Value;
					var tileData = WorldTileRegistry.GetTile(cropData.CropTileId);
					if (tileData == null || !tileData.IsSapling) continue;

					// Pas d'arrosage requis : la croissance avance à chaque tick.
					cropData.AccumulatedGrowthTime += dt;

					float elapsed = cropData.AccumulatedGrowthTime;
					float stageFloat = (elapsed / MathF.Max(tileData.GrowthTime, 0.01f)) * tileData.GrowthStages;
					int stage = Math.Clamp((int)MathF.Floor(stageFloat), 1, tileData.GrowthStages);
					_lastCropStage[(x, y)] = stage;

					if (elapsed >= tileData.GrowthTime && tileData.AdultTileId > 0)
						(readyToGrow ??= new List<(int, int)>()).Add((x, y));
				}

				if (readyToGrow == null) continue;

				foreach (var (x, y) in readyToGrow)
				{
					if (!chunk.Crops.TryGetValue((x, y), out var cropData)) continue;
					var tileData = WorldTileRegistry.GetTile(cropData.CropTileId);
					if (tileData == null || !tileData.IsSapling || tileData.AdultTileId <= 0) continue;

					chunk.Crops.Remove((x, y));
					_lastCropStage.Remove((x, y));

					AddPlacedObject_Apply(x, y, tileData.AdultTileId);
					if (NetworkManager.IsHost)
						NetworkManager.BroadcastWorldAction("AddPlacedObject", x, y, tileData.AdultTileId, isUnderground);
				}
			}
		}

		public static Dictionary<string, float> GetFarmlandMoistureData()
		{
			var result = new Dictionary<string, float>();
			foreach (var kv in _farmlandMoisture)
			{
				string key = $"{kv.Key.x}_{kv.Key.y}";
				result[key] = kv.Value;
			}
			return result;
		}

		public static void RestoreFarmlandMoisture(Dictionary<string, float> data)
		{
			_farmlandMoisture.Clear();
			if (data == null) return;
			foreach (var kv in data)
			{
				var parts = kv.Key.Split('_');
				if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
				{
					_farmlandMoisture[(x, y)] = kv.Value;
				}
			}
		}

		private static UnderwaterChunkData GenerateUnderwaterChunk(int chunkX, int chunkY)
		{
			var chunk = new UnderwaterChunkData();
			Random rand = new Random(Program.WorldSeed ^ (chunkX * 73856093) ^ (chunkY * 19349663));

			for (int lx = 0; lx < CHUNK_SIZE; lx++)
			for (int ly = 0; ly < CHUNK_SIZE; ly++)
			{
				int worldX = chunkX * CHUNK_SIZE + lx;
				int worldY = chunkY * CHUNK_SIZE + ly;

				float depthNoise = PerlinNoise.Noise(worldX * 0.05f, worldY * 0.05f);
				int floorDepth = 45 + (int)((depthNoise + 1) * 45f);  // 45 à 90 unités
				_seaFloorHeights[(worldX, worldY)] = floorDepth;

				// Sol : ID 60 (sable) ou 61 (roche) selon la profondeur
				int groundId;
				if (floorDepth > 45) groundId = 61;      // Roche profonde
				else if (floorDepth > 30) groundId = 60; // Sable
				else groundId = 11;                       // Sable clair (peu profond)
				chunk.SeaFloor[(worldX, worldY)] = groundId;
				
				// Décors (coraux, herbiers) avec une chance
				if (rand.NextDouble() < 0.15)
					chunk.Objects[(worldX, worldY)] = rand.Next(200, 210); // IDs réservés aux coraux/herbiers
			}

			chunk.IsGenerated = true;
			return chunk;
		}

		public class ChunkData
		{
			public Dictionary<(int x, int y), int> Objects = new();
			public Dictionary<(int x, int y), int> Overlays = new();
			public Dictionary<(int x, int y), int> Heights = new();
			public Dictionary<(int x, int y), int> Decorations = new();
			public Dictionary<(int x, int y), int> Variations = new();
			public Dictionary<(int x, int y), int> RoofHeights = new();
			public Dictionary<(int x, int y), RoofTileInfo> RoofTileInfo = new();
			public Dictionary<(int x, int y), ContainerInventoryData> Containers = new();
			public Dictionary<(int x, int y), ArmorStandData> ArmorStands = new();
			public Dictionary<(int x, int y), int> GroundOverrides = new();
			public Dictionary<(int x, int y), CropData> Crops = new();
			public Dictionary<(int x, int y), int> WallCoverings { get; set; } = new();
			public Dictionary<(int x, int y), BannerHolderData> BannerHolders { get; set; } = new();

			//  SYSTÈME UNIFIÉ DE MÉTADONNÉES DE TUILE (extensible, additif) — pendant de
			// Item.Meta côté monde. Sac extensible de paires clé/valeur par tuile pour toute
			// donnée occasionnelle (ruche sur un arbre, minerai dans de la roche, câblage
			// électrique, etc.) sans avoir à ajouter un nouveau Dictionary<(x,y), T> à
			// ChunkData, un nouveau champ à ChunkSaveData, ni un nouveau message réseau à
			// chaque fois. Clés conventionnelles (insensibles à la casse), définies au fil
			// des besoins, par ex. "beehive" → "1", "ore" → "iron:40", "wired" → "1".
			// Une tuile sans métadonnée n'a pas d'entrée ici (pas de clé (x,y) vide à traîner).
			public Dictionary<(int x, int y), Dictionary<string, string>> TileMeta = new();

			public List<Entity> Entities = new();
			public bool IsGenerated = false;
			public List<(int x, int y)> NewWallPositions = new();
			public float LastAccessTime = 0;
			public float LastRegrowthAttempt { get; set; } = 0f;
			
			public ChunkData()
			{
				// Initialisation explicite de tous les dictionnaires
				Objects ??= new Dictionary<(int x, int y), int>();
				Overlays ??= new Dictionary<(int x, int y), int>();
				Heights ??= new Dictionary<(int x, int y), int>();
				Decorations ??= new Dictionary<(int x, int y), int>();
				Variations ??= new Dictionary<(int x, int y), int>();
				RoofHeights ??= new Dictionary<(int x, int y), int>();
				RoofTileInfo ??= new Dictionary<(int x, int y), RoofTileInfo>();
				Containers ??= new Dictionary<(int x, int y), ContainerInventoryData>();
				ArmorStands ??= new Dictionary<(int x, int y), ArmorStandData>();
				GroundOverrides ??= new Dictionary<(int x, int y), int>();
				Crops ??= new Dictionary<(int x, int y), CropData>();
				WallCoverings ??= new Dictionary<(int x, int y), int>();
				BannerHolders ??= new Dictionary<(int x, int y), BannerHolderData>();
				TileMeta ??= new Dictionary<(int x, int y), Dictionary<string, string>>();
				Entities ??= new List<Entity>();
			}
			
			public ContainerInventoryData? GetContainerAt(int x, int y)
			{
				Containers.TryGetValue((x, y), out var data);
				return data;
			}
			
			public void SetContainerAt(int x, int y, ContainerInventoryData? data)
			{
				if (data != null)
					Containers[(x, y)] = data;
				else
					Containers.Remove((x, y));
			}
			
			public ArmorStandData? GetArmorStandAt(int x, int y)
			{
				ArmorStands.TryGetValue((x, y), out var data);
				return data;
			}
			
			public void SetArmorStandAt(int x, int y, ArmorStandData? data)
			{
				if (data != null)
					ArmorStands[(x, y)] = data;
				else
					ArmorStands.Remove((x, y));
			}
			
			public CropData? GetCropAt(int x, int y)
			{
				Crops.TryGetValue((x, y), out var data);
				return data;
			}
			
			public void SetCropAt(int x, int y, CropData? data)
			{
				if (data != null)
					Crops[(x, y)] = data;
				else
					Crops.Remove((x, y));
			}
			
			// Méthodes d'accès pour les revêtements muraux
			public int GetWallCoveringAt(int x, int y)
			{
				return WallCoverings.GetValueOrDefault((x, y), 0);
			}

			public void SetWallCoveringAt(int x, int y, int coveringId)
			{
				if (coveringId == 0)
					WallCoverings.Remove((x, y));
				else
					WallCoverings[(x, y)] = coveringId;
			}

			public BannerHolderData? GetBannerHolderAt(int x, int y)
			{
				BannerHolders.TryGetValue((x, y), out var data);
				return data;
			}

			public void SetBannerHolderAt(int x, int y, BannerHolderData? data)
			{
				if (data != null) BannerHolders[(x, y)] = data;
				else              BannerHolders.Remove((x, y));
			}

			// ---- Métadonnées de tuile (générique) ----

			public bool HasTileMeta(int x, int y, string key)
			{
				return TileMeta.TryGetValue((x, y), out var bag) && bag.ContainsKey(key);
			}

			public string GetTileMeta(int x, int y, string key, string defaultValue = "")
			{
				if (TileMeta.TryGetValue((x, y), out var bag) && bag.TryGetValue(key, out var v))
					return v;
				return defaultValue;
			}

			/// <summary>Retourne le sac de métadonnées complet d'une tuile, ou null si aucune.</summary>
			public Dictionary<string, string>? GetTileMetaBag(int x, int y)
			{
				TileMeta.TryGetValue((x, y), out var bag);
				return bag;
			}

			/// <summary>Définit une clé de métadonnée pour une tuile ; une valeur nulle/vide supprime
			/// la clé (et le sac s'il devient vide), pour ne pas accumuler d'entrées inutiles à
			/// sérialiser/synchroniser.</summary>
			public void SetTileMeta(int x, int y, string key, string? value)
			{
				if (string.IsNullOrWhiteSpace(value))
				{
					if (TileMeta.TryGetValue((x, y), out var bag))
					{
						bag.Remove(key);
						if (bag.Count == 0) TileMeta.Remove((x, y));
					}
					return;
				}

				if (!TileMeta.TryGetValue((x, y), out var existing))
				{
					existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					TileMeta[(x, y)] = existing;
				}
				existing[key] = value;
			}

			public void RemoveTileMeta(int x, int y, string key) => SetTileMeta(x, y, key, null);

			/// <summary>Supprime toutes les métadonnées d'une tuile (ex : destruction de l'objet qui les porte).</summary>
			public void ClearTileMeta(int x, int y) => TileMeta.Remove((x, y));
		}

		public static Dictionary<(int x, int y), int> GetLastCropStages()
		{
			return new Dictionary<(int x, int y), int>(_lastCropStage);
		}

		public static void RestoreLastCropStages(Dictionary<(int x, int y), int> stages)
		{
			_lastCropStage.Clear();
			foreach (var kv in stages)
			{
				_lastCropStage[kv.Key] = kv.Value;
			}
		}

		public static void PlantCrop(int x, int y, int cropTileId, float currentGameTime)
		{
			if (NetworkManager.IsClient)
			{
				NetworkManager.RequestWorldAction("PlantCrop", x, y, cropTileId, _isUnderground);
				return;
			}
			PlantCrop_Apply(x, y, cropTileId, currentGameTime);
			if (NetworkManager.IsHost)
				NetworkManager.BroadcastWorldAction("PlantCrop", x, y, cropTileId, _isUnderground);
		}

		public static void PlantCrop_Apply(int x, int y, int cropTileId, float currentGameTime)
		{
			if (GetObjectIdAt(x, y) != 0) return;

			var cropTileData = WorldTileRegistry.GetTile(cropTileId);

			//  Arbre (plantType "classic", ex : gland) : se plante comme un objet plaçable,
			// n'importe où sur un sol praticable, sans passer par les farmlands ni l'arrosage.
			if (cropTileData != null && cropTileData.IsSapling)
			{
				int groundIdTree = GetGroundTileIdAt(x, y);
				var groundTileTree = WorldTileRegistry.GetTile(groundIdTree);
				if (groundTileTree == null || !groundTileTree.Walkable) return;

				AddPlacedObject_Apply(x, y, cropTileId);

				var treeChunk = GetChunkAt(x, y);
				if (treeChunk != null)
				{
					treeChunk.Crops[(x, y)] = new CropData
					{
						CropTileId = cropTileId,
						PlantTime = currentGameTime
					};
					_lastCropStage[(x, y)] = 0;
				}
				return;
			}

			// Culture classique : uniquement sur une farmland (27 = sèche, 29 = humide)
			int groundId = GetGroundTileIdAt(x, y);
			if (groundId != 27 && groundId != 29) return;

			//  Si la terre est sèche (27), on la rend humide automatiquement avant de planter
			// pour que la graine puisse pousser
			if (groundId == 27)
			{
				// Rendre la terre humide automatiquement
				SetGroundTile_Apply(x, y, 29);
				_farmlandMoisture[(x, y)] = FARMLAND_DRYING_TIME;
				// On ne notifie pas le joueur ici car le message est déjà envoyé par Program.cs
			}
			
			AddPlacedObject_Apply(x, y, cropTileId);
			
			var chunk = GetChunkAt(x, y);
			if (chunk != null)
			{
				chunk.Crops[(x, y)] = new CropData
				{
					CropTileId = cropTileId,
					PlantTime = currentGameTime
				};
				_lastCropStage[(x, y)] = 0;
			}
		}

		public static CropData? GetCropDataAt(int x, int y)
		{
			var chunk = GetChunkAt(x, y);
			if (chunk != null && chunk.Crops.TryGetValue((x, y), out var data))
			{
				return data;
			}
			return null;
		}

		public static void ResetCropStage(int x, int y)
		{
			_lastCropStage.Remove((x, y));
		}

		public static void BeginCropRegrowth(int x, int y, WorldTileData tileData, CropData cropData)
		{
			float phaseGrowthTime = tileData.GrowthTime / MathF.Max(1, tileData.GrowthStages);
			cropData.AccumulatedGrowthTime = MathF.Max(0f, tileData.GrowthTime - phaseGrowthTime);
			SetTileMeta(x, y, "crop_state", "harvested");
			ResetCropStage(x, y);
		}

		//  Stade de croissance actuel (1..GrowthStages) d'une culture OU d'un jeune arbre planté
		// à (x, y), calculé à partir du temps réellement accumulé. Retourne 0 si la tuile n'a
		// pas de données de croissance (pas encore plantée / pas une tuile de croissance).
		public static int GetGrowthStageAt(int x, int y, WorldTileData tileData)
		{
			if (tileData == null || !tileData.HasGrowthStages) return 0;
			var cropData = GetCropDataAt(x, y);
			if (cropData == null) return 0;

			float elapsed = cropData.AccumulatedGrowthTime;
			float stageFloat = (elapsed / MathF.Max(tileData.GrowthTime, 0.01f)) * tileData.GrowthStages;
			return Math.Clamp((int)MathF.Floor(stageFloat), 1, tileData.GrowthStages);
		}

		public class CropData
		{
			public int CropTileId { get; set; }   // ID de la tuile de culture
			public float PlantTime { get; set; }  // temps de jeu (Program._gameTime) au moment de la plantation
			// Temps de croissance réellement accumulé : n'avance que lorsque le sol est humide.
			// C'est cette valeur (et non PlantTime) qui doit servir à calculer le stade/la maturité.
			public float AccumulatedGrowthTime { get; set; } = 0f;
		}

		// ---- Métadonnées de tuile (API statique, monde courant) ----

		/// <summary>Lecture locale (pas d'aller-retour réseau) : marche identiquement client/host/solo.</summary>
		public static string GetTileMeta(int x, int y, string key, string defaultValue = "")
		{
			var chunk = GetChunkAt(x, y);
			return chunk?.GetTileMeta(x, y, key, defaultValue) ?? defaultValue;
		}

		public static bool HasTileMeta(int x, int y, string key)
		{
			var chunk = GetChunkAt(x, y);
			return chunk != null && chunk.HasTileMeta(x, y, key);
		}

		/// <summary>Écriture : sur un client, délègue au host (comme PlantCrop/AddPlacedObject) ;
		/// sur le host (ou en solo), applique directement puis redistribue aux clients.</summary>
		public static void SetTileMeta(int x, int y, string key, string? value)
		{
			if (NetworkManager.IsClient)
			{
				NetworkManager.RequestTileMetaAction(x, y, key, value ?? "", _isUnderground);
				return;
			}
			SetTileMeta_Apply(x, y, key, value);
			if (NetworkManager.IsHost)
				NetworkManager.BroadcastTileMetaAction(x, y, key, value ?? "", _isUnderground);
		}

		public static void RemoveTileMeta(int x, int y, string key) => SetTileMeta(x, y, key, null);

		public static string GetOreKeyFromTileMeta(int x, int y)
		{
			string oreMeta = GetTileMeta(x, y, "ore");
			if (string.IsNullOrWhiteSpace(oreMeta))
				return string.Empty;

			int separator = oreMeta.IndexOf(':');
			string oreKey = separator >= 0 ? oreMeta.Substring(0, separator) : oreMeta;
			return oreKey.Trim().ToLowerInvariant();
		}

		public static int GetOreItemIdFromTileMeta(int x, int y)
		{
			switch (GetOreKeyFromTileMeta(x, y))
			{
				case "iron": return GameData.GetItemId("iron_ore");
				case "copper": return GameData.GetItemId("copper_ore");
				case "coal": return GameData.GetItemId("coal");
				case "garnet": return GameData.GetItemId("garnet");
				case "amber": return GameData.GetItemId("amber");
				case "topaze": return GameData.GetItemId("topaze");
				case "onyx": return GameData.GetItemId("onyx");
				case "opale": return GameData.GetItemId("opale");
				case "obsidian": return GameData.GetItemId("obsidian");
				default: return 0;
			}
		}

		public static Texture2D GetOreOverlayTexture(int x, int y)
		{
			string oreKey = GetOreKeyFromTileMeta(x, y);
			if (string.IsNullOrEmpty(oreKey))
				return new Texture2D();
			return WorldTileRegistry.GetTextureByName($"{oreKey}_ore");
		}

		public static string GetOreKeyForLegacyOverlayId(int overlayId)
		{
			switch (overlayId)
			{
				case 1001: return "iron";
				case 1002: return "copper";
				case 1003: return "coal";
				case 1004: return "garnet";
				case 1005: return "amber";
				case 1006: return "topaze";
				case 1007: return "onyx";
				case 1008: return "opale";
				case 1009: return "obsidian";
				default: return string.Empty;
			}
		}

		public static void SetTileMeta_Apply(int x, int y, string key, string? value)
		{
			var chunk = GetChunkAt(x, y);
			chunk?.SetTileMeta(x, y, key, value);
		}

		public static void ApplyTileMetaActionFromNetwork(TileMetaActionMsg action)
		{
			bool savedUnderground = _isUnderground;
			_isUnderground = action.Underground;
			try
			{
				if (NetworkManager.IsHost && GetChunkAt(action.X, action.Y) == null)
				{
					int chunkX = action.X / CHUNK_SIZE;
					int chunkY = action.Y / CHUNK_SIZE;
					if (action.X < 0) chunkX = (action.X - CHUNK_SIZE + 1) / CHUNK_SIZE;
					if (action.Y < 0) chunkY = (action.Y - CHUNK_SIZE + 1) / CHUNK_SIZE;
					LoadOrGenerateChunk(chunkX, chunkY, Program.GetGameTime(), Program.GetEntities());
				}
				SetTileMeta_Apply(action.X, action.Y, action.Key, action.Value);
			}
			finally
			{
				_isUnderground = savedUnderground;
			}
		}

		public static void ApplyWorldActionFromNetwork(WorldActionMsg action)
		{
			bool savedUnderground = _isUnderground;
			_isUnderground = action.Underground;
			try
			{
				if (NetworkManager.IsHost)
				{
					int chunkX = action.X / CHUNK_SIZE;
					int chunkY = action.Y / CHUNK_SIZE;
					if (action.X < 0) chunkX = (action.X - CHUNK_SIZE + 1) / CHUNK_SIZE;
					if (action.Y < 0) chunkY = (action.Y - CHUNK_SIZE + 1) / CHUNK_SIZE;
					if (GetChunkAt(action.X, action.Y) == null)
					{
						LoadOrGenerateChunk(chunkX, chunkY, Program.GetGameTime(), Program.GetEntities());
					}
				}
				switch (action.Action)
				{
					case "AddPlacedObject":
						AddPlacedObject_Apply(action.X, action.Y, action.Value);
						break;
					case "RemovePlacedObject":
						RemovePlacedObject_Apply(action.X, action.Y);
						break;
					case "AddOverlay":
						AddOverlay_Apply(action.X, action.Y, action.Value);
						break;
					case "RemoveOverlay":
						RemoveOverlay(action.X, action.Y);
						break;
					case "SetGroundTile":
						SetGroundTile_Apply(action.X, action.Y, action.Value);
						break;
					case "PlantCrop":
						PlantCrop_Apply(action.X, action.Y, action.Value, Program.GetGameTime());
						break;
				}
			}
			finally
			{
				_isUnderground = savedUnderground;
			}
		}

		private static Color GetTreeLeafTintForObjectId(int objectId, int worldX, int worldY)
		{
			long seed = ((long)worldX * 73856093L) ^ ((long)worldY * 19349663L) ^ 0x9E3779B9L;
			var rand = new Random(Math.Abs((int)seed));
			if (rand.NextDouble() < 0.03)
				return new Color((byte)220, (byte)140, (byte)20, (byte)255);
			if (rand.NextDouble() < 0.03)
				return new Color((byte)205, (byte)110, (byte)75, (byte)255);

			switch (objectId)
			{
				case 45:
					int spruceVariant = rand.Next(3);
					return spruceVariant switch
					{
						0 => new Color((byte)42, (byte)84, (byte)48, (byte)255),
						1 => new Color((byte)50, (byte)105, (byte)58, (byte)255),
						_ => new Color((byte)68, (byte)118, (byte)64, (byte)255)
					};
				case 48:
					if (rand.NextDouble() < 0.05)
						return new Color((byte)255, (byte)220, (byte)210, (byte)255);
					return new Color((byte)170, (byte)200, (byte)150, (byte)255);
				case 49:
					if (rand.NextDouble() < 0.08)
						return new Color((byte)220, (byte)170, (byte)120, (byte)255);
					return new Color((byte)120, (byte)180, (byte)90, (byte)255);
				case 4:
				default:
					if (rand.NextDouble() < 0.02)
						return new Color((byte)255, (byte)185, (byte)200, (byte)255);

					int r = 120 + rand.Next(-18, 19);
					int g = 180 + rand.Next(-18, 19);
					int b = 80 + rand.Next(-16, 17);

					return new Color(
						(byte)Math.Clamp(r, 0, 255),
						(byte)Math.Clamp(g, 0, 255),
						(byte)Math.Clamp(b, 0, 255),
						(byte)255);
			}
		}

		public static int GetGroundTileIdAt(int x, int y)
		{
			// Vérifier les coordonnées extrêmes
			if (x == int.MinValue || x == int.MaxValue || y == int.MinValue || y == int.MaxValue)
				return 0;

			// Vérifier les overrides
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				if (chunk.GroundOverrides.TryGetValue((x, y), out int overrideId))
				{
					// Si c'est une farmland (ID 27) et qu'elle est humide, retourner 29
					if (overrideId == 27 && IsFarmlandWet(x, y))
						return 29; // farmland_wet
					return overrideId;
				}
			}

			// Génération procédurale basée sur température/humidité
			if (_isUnderground)
				return GetCaveGroundTileIdAt(x, y);
			
			var climate = GetClimateAt(x, y);
			
			Biome biome = WorldGeneration.GetBiome(climate.temperature, climate.humidity, climate.elevation);
			int groundId = WorldGeneration.GetGroundTileForBiome(biome);
			
			// Si c'est une farmland (ID 27) et qu'elle est humide, retourner 29
			if (groundId == 27 && IsFarmlandWet(x, y))
				return 29;
			
			return groundId;
		}

		/// <summary>
        /// Réserve une nouvelle instance de donjon associée à l'entrée (entryX, entryY) située
        /// dans les grottes. N'effectue AUCUNE génération — juste l'allocation de l'instance et
        /// l'enregistrement du portail d'entrée. La génération réelle est faite paresseusement
        /// à la première visite (TryEnterDungeon), pour ne payer son coût que si le joueur y va.
        /// Le donjon utilise TOUJOURS des coordonnées locales à partir de (0,0) : il vit dans
        /// son propre dictionnaire de chunks, jamais dans celui des grottes/de la surface, donc
        /// aucune collision possible et aucun risque de dépassement arithmétique.
        /// </summary>
        public static DungeonInstance AllocateDungeonInstance(int entryX, int entryY, int maxWidth, int maxHeight)
        {
            int id = _nextDungeonInstanceId++;
            var inst = new DungeonInstance
            {
                Id = id,
                OriginX = 0,
                OriginY = 0,
                Width = maxWidth,
                Height = maxHeight,
                EntryPos = (entryX, entryY),
                Generated = false,
                BossDefeated = false,
                Seed = HashCode.Combine(Program.WorldSeed, entryX, entryY, 918273)
            };
            _dungeonInstances[id] = inst;
            _dungeonPortals[(entryX, entryY)] = new DungeonPortalInfo { Kind = DungeonPortalKind.Entrance, InstanceId = id };
            return inst;
        }

		/// <summary>
        /// Le joueur interagit avec une entrée de donjon. Génère le donjon au besoin (première
        /// visite) puis bascule dans son plan de chunks dédié et renvoie la position d'apparition
        /// dans la salle de repos.
        /// </summary>
        public static bool TryEnterDungeon(int entryX, int entryY, out Vector2 spawnPos)
        {
            spawnPos = Vector2.Zero;
            if (!_dungeonPortals.TryGetValue((entryX, entryY), out var portal) || portal.Kind != DungeonPortalKind.Entrance)
                return false;
            if (!_dungeonInstances.TryGetValue(portal.InstanceId, out var inst))
                return false;

            if (!_dungeonChunksByInstance.TryGetValue(inst.Id, out var instanceChunks))
            {
                instanceChunks = new Dictionary<(int chunkX, int chunkY), ChunkData>();
                _dungeonChunksByInstance[inst.Id] = instanceChunks;
            }

            // Bascule vers le plan de ce donjon AVANT toute génération/lecture de tuiles,
            // pour que GetChunksDict() (et donc SetGroundTile/AddPlacedObject/...) écrivent
            // au bon endroit.
            _isInDungeon = true;
            _currentDungeonInstanceId = inst.Id;
            _activeDungeonChunks = instanceChunks;

            if (!inst.Generated)
            {
                var rand = new Random(inst.Seed);
                EnsureDungeonRegionChunks(inst.OriginX, inst.OriginY, inst.Width, inst.Height);
                DungeonGenerator.GenerateDungeon(inst, rand);
                inst.Generated = true;
                Console.WriteLine($" Donjon #{inst.Id} généré ({inst.Width}x{inst.Height}) à l'entrée ({entryX},{entryY})");
            }
			else if (inst.BossDefeated)
			{
				DungeonGenerator.EnsureExitApproach(inst);
			}

            int ts = Program.TileSize;
            spawnPos = new Vector2(inst.RestSpawnTile.x * ts + ts / 2f, inst.RestSpawnTile.y * ts + ts / 2f);
            return true;
        }

		public static void UpdateRegrowth(float currentTime)
		{
			// Traiter les chunks de surface
			foreach (var kv in _chunks)
			{
				var chunk = kv.Value;
				if (currentTime - chunk.LastRegrowthAttempt > REGROWTH_INTERVAL)
				{
					chunk.LastRegrowthAttempt = currentTime;
					AttemptRegrowthForChunk(kv.Key.chunkX, kv.Key.chunkY, chunk, false, currentTime);
				}
			}

			// Traiter les chunks de grottes
			foreach (var kv in _caveChunks)
			{
				var chunk = kv.Value;
				if (currentTime - chunk.LastRegrowthAttempt > REGROWTH_INTERVAL)
				{
					chunk.LastRegrowthAttempt = currentTime;
					AttemptRegrowthForChunk(kv.Key.chunkX, kv.Key.chunkY, chunk, true, currentTime);
				}
			}
		}

		private static void AttemptRegrowthForChunk(int chunkX, int chunkY, ChunkData chunk, bool isUnderground, float currentTime)
		{
			var rand = new Random(); // Non déterministe, acceptable pour la repousse aléatoire
			int chunkSize = CHUNK_SIZE;
			int baseX = chunkX * chunkSize;
			int baseY = chunkY * chunkSize;
			int ts = Program.TileSize;

			for (int attempt = 0; attempt < REGROWTH_ATTEMPTS_PER_CHUNK; attempt++)
			{
				// Choix d'une position aléatoire dans le chunk
				int localX = rand.Next(chunkSize);
				int localY = rand.Next(chunkSize);
				int worldX = baseX + localX;
				int worldY = baseY + localY;

				// Ne pas faire repousser s'il y a déjà un objet
				if (GetObjectIdAt(worldX, worldY) != 0)
					continue;

				// Récupération du sol et du biome
				int groundId = GetGroundTileIdAt(worldX, worldY);
				var groundTile = WorldTileRegistry.GetTile(groundId);
				if (groundTile == null || !groundTile.Diggable) // Sol inconstructible
					continue;

				Biome biome = isUnderground ? GetCaveBiomeAt(worldX, worldY) : GetBiomeAt(worldX, worldY);
				string biomeName = biome.ToString();

				// Filtrer les objets spawnables compatibles avec ce biome et ce sol
				var possibleObjects = _spawnableObjects.Where(obj =>
					obj.Biomes.Contains(biomeName) &&
					obj.RequiredGroundId == groundId &&
					(isUnderground ? obj.IsCaveOnly : !obj.IsCaveOnly)).ToList();

				if (possibleObjects.Count == 0)
					continue;

				// Sélection pondérée
				float totalWeight = possibleObjects.Sum(obj => obj.Weight);
				float randVal = (float)rand.NextDouble() * totalWeight;
				float cumul = 0;
				SpawnableObject selected = null;
				foreach (var obj in possibleObjects)
				{
					cumul += obj.Weight;
					if (randVal <= cumul)
					{
						selected = obj;
						break;
					}
				}

				if (selected == null)
					continue;

				var tileDef = WorldTileRegistry.GetTile(selected.Id);
				if (tileDef == null || !tileDef.Collectable || tileDef.IsCrop)
					continue;

				// Placement de l'objet
				AddPlacedObject(worldX, worldY, selected.Id);
				if (tileDef.Variation > 1)
				{
					int variation = rand.Next(tileDef.Variation);
					chunk.Variations[(worldX, worldY)] = variation;
				}
				// Optionnel : notification discrète pour debug
				// Console.WriteLine($" Repousse de {tileDef.Name} à ({worldX},{worldY})");
			}
		}

		//  Un jeune arbre planté (IsSapling, ex : id 4005) a lui aussi du feuillage dès qu'il a
		// des stades de croissance visibles : il fait donc tomber des feuilles comme un arbre
		// adulte, avec la même teinte (voir GetTreeLeafTintForObjectId).
		public static bool IsLeafBearingTree(int objectId)
		{
			if (_leafBearingTreeIds.Contains(objectId)) return true;
			var tileData = WorldTileRegistry.GetTile(objectId);
			return tileData != null && tileData.IsSapling;
		}

		//  Calcule la zone de dessin (rectangle monde) d'un arbre planté en (tileX, tileY),
		// ainsi que le niveau du sol sur lequel les feuilles doivent se poser, et la liste
		// des points de feuillage (pixels opaques dans la partie haute de la texture) où une
		// feuille peut légitimement apparaître.
		private static bool TryGetTreeLeafSpawnPoints(int tileX, int tileY, out List<Vector2> spawnPointsWorld, out float groundY)
		{
			spawnPointsWorld = null;
			groundY = 0f;

			int objectId = GetObjectIdAt(tileX, tileY);
			var tileData = WorldTileRegistry.GetTile(objectId);
			if (tileData?.Size == null) return false;

			//  Utiliser la texture du feuillage réel quand elle existe (couche "bush" de
			// l'arbre adulte, ou couche "leaves" du stade actuel pour un jeune arbre), pour
			// que les points de chute de feuilles correspondent à la forme du feuillage
			// effectivement dessiné plutôt qu'au tronc ou à un sprite générique.
			Texture2D tex = default;
			bool isPureLeavesTexture = false;
			if (objectId == 4 || objectId == 45)
			{
				string treeTexturePrefix = objectId == 45 ? "spruce_tree" : "oak_tree";
				tex = WorldTileRegistry.GetTextureByName($"{treeTexturePrefix}_bush");
				isPureLeavesTexture = tex.Id != 0;
			}
			else if (tileData.IsSapling &&
				WorldTileRegistry.SaplingLeavesStageTextures.TryGetValue(objectId, out var leavesStages) &&
				leavesStages.Count > 0)
			{
				int stage = GetGrowthStageAt(tileX, tileY, tileData);
				int stageIdx = Math.Clamp(stage - 1, 0, leavesStages.Count - 1);
				tex = leavesStages[stageIdx];
				isPureLeavesTexture = tex.Id != 0;
			}
			if (tex.Id == 0)
				tex = _tileTextures.GetValueOrDefault(objectId);
			if (tex.Id == 0) return false;

			int ts = Program.TileSize;
			int height = GetHeightAt(tileX, tileY);
			float yOffset = -height * ts / 4f;

			float baseWx = tileX * ts;
			float baseWy = tileY * ts + yOffset;

			int objWidth = ts * tileData.Size.Width;
			int objHeight = ts * tileData.Size.Height;

			float drawX = baseWx + (tileData.DrawOffset?.X ?? 0);
			groundY = baseWy + ts; // bas de la tuile d'ancrage = sol sous l'arbre
			float drawY = groundY - objHeight; // haut du sprite

			var opaquePixels = GetOrGenerateOpaquePixels(tex);
			if (opaquePixels.Count == 0) return false;

			//  Texture déjà "feuillage pur" (couche bush/leaves séparée du tronc) : pas besoin
			// de filtrer par hauteur, tous les pixels opaques sont légitimement du feuillage.
			if (isPureLeavesTexture)
			{
				var pureCanopyPoints = new List<Vector2>(opaquePixels.Count);
				foreach (var p in opaquePixels)
					pureCanopyPoints.Add(new Vector2(drawX + p.X * objWidth, drawY + p.Y * objHeight));
				spawnPointsWorld = pureCanopyPoints;
				return true;
			}

			//  Ne garder que le feuillage (partie haute de la texture, sans le tronc)
			const float canopyMaxV = 0.65f;
			var canopyPoints = new List<Vector2>(opaquePixels.Count);
			foreach (var p in opaquePixels)
			{
				if (p.Y <= canopyMaxV)
					canopyPoints.Add(new Vector2(drawX + p.X * objWidth, drawY + p.Y * objHeight));
			}
			//  Repli : si l'arbre n'a pas de feuillage détecté dans la zone haute (texture
			// atypique), on utilise tous les pixels opaques plutôt que de ne rien afficher.
			if (canopyPoints.Count == 0)
			{
				foreach (var p in opaquePixels)
					canopyPoints.Add(new Vector2(drawX + p.X * objWidth, drawY + p.Y * objHeight));
			}

			spawnPointsWorld = canopyPoints;
			return true;
		}

		public static bool TryGetTreeBirdSpawnPosition(int tileX, int tileY, out Vector2 spawnPosition)
		{
			spawnPosition = Vector2.Zero;
			if (!TryGetTreeLeafSpawnPoints(tileX, tileY, out var spawnPoints, out _)
				|| spawnPoints.Count == 0)
				return false;

			spawnPosition = spawnPoints[Random.Shared.Next(spawnPoints.Count)];
			return true;
		}

		public static int GetPottedFlowerTileId(int itemId)
		{
			if (GameData.ItemDatabaseByKey.TryGetValue("rose", out var rose) && rose.ID == itemId)
				return 13;
			if (GameData.ItemDatabaseByKey.TryGetValue("white_flower", out var whiteFlower) && whiteFlower.ID == itemId)
				return 14;
			if (GameData.ItemDatabaseByKey.TryGetValue("sunflower", out var sunflower) && sunflower.ID == itemId)
				return 5009;
			return 0;
		}

		public static bool TryGetPottedFlowerDestination(int itemId, Rectangle potRect, out int tileId, out Rectangle destination)
		{
			tileId = GetPottedFlowerTileId(itemId);
			destination = default;
			if (tileId == 0) return false;

			var tileData = WorldTileRegistry.GetTile(tileId);
			if (tileData == null) return false;

			int flowerWidth = Program.TileSize * (tileData.Size?.Width ?? 1);
			int flowerHeight = Program.TileSize * (tileData.Size?.Height ?? 1);
			int flowerX = (int)(potRect.X + (potRect.Width - flowerWidth) / 2f);
			int flowerY = (int)(potRect.Y - flowerHeight + Program.TileSize + 16);
			if (tileData.DrawOffset != null)
			{
				flowerX += tileData.DrawOffset.X;
				flowerY -= tileData.DrawOffset.Y;
			}

			destination = new Rectangle(flowerX, flowerY, flowerWidth, flowerHeight);
			return true;
		}

		private static Texture2D GetTileTexture(int x, int y, int tileId, float currentTime)
		{
			if (tileId == 123) return GetRailwayTexture(x, y);

			var tileData = WorldTileRegistry.GetTile(tileId);
			if (tileData == null) return default;

			// 1. Animation (priorité absolue)
			if (WorldTileRegistry.IsAnimated(tileId))
			{
				var frames = WorldTileRegistry.GetAnimationFrames(tileId);
				if (frames != null && frames.Count > 0)
				{
					var animKey = (x, y);
					if (!_tileAnimationStartCache.ContainsKey(animKey))
					{
						int hash = (x * 73856093) ^ (y * 19349663);
						int startAnimFrame = Math.Abs(hash) % frames.Count;
						_tileAnimationStartCache[animKey] = startAnimFrame;
					}
					int frameIndex = (_tileAnimationStartCache[animKey] + (int)(currentTime / 0.15f)) % frames.Count;
					return frames[frameIndex];
				}
			}

			if (!tileData.HasGrowthStages && !WorldTileRegistry.IsAnimated(tileId))
			{
				var staticVariations = WorldTileRegistry.GetVariations(tileId);
				if ((staticVariations == null || staticVariations.Count <= 1) && _tileTextures.TryGetValue(tileId, out var staticTexture))
					return staticTexture;
			}

			bool hasSavedVariation = false;

			// 2. Variations (avec cache simple)
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();

			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				if (chunk.Variations.TryGetValue((x, y), out int savedVariation))
				{
					hasSavedVariation = true;
					var variations = WorldTileRegistry.GetVariations(tileId);
					if (variations != null && savedVariation >= 0 && savedVariation < variations.Count)
						return variations[savedVariation];
				}
			}

			if (!hasSavedVariation && !tileData.HasGrowthStages && !WorldTileRegistry.IsAnimated(tileId) &&
				_tileTextures.TryGetValue(tileId, out var baseTexture))
				return baseTexture;
			
			//  Culture / jeune arbre - afficher le stade actuel (même si la terre est sèche
			// pour une culture ; pour un arbre, la pousse ne dépend d'aucun arrosage)
			if (tileData != null && tileData.HasGrowthStages)
			{
				if (tileData.IsCrop && tileData.RegrowsAfterHarvest &&
					GetTileMeta(x, y, "crop_state") == "harvested")
				{
					var harvestedTexture = WorldTileRegistry.GetTextureByName(tileData.HarvestedTexture);
					if (harvestedTexture.Id != 0)
						return harvestedTexture;
				}

				var cropData = GetCropDataAt(x, y);
				if (cropData != null)
				{
					var cropStageTextures = WorldTileRegistry.CropStageTextures.GetValueOrDefault(tileId);
					if (cropStageTextures == null || cropStageTextures.Count == 0)
						return default;

					// Le stade est basé sur le temps de croissance réellement accumulé
					// (n'avance que quand le sol est humide), donc il reste automatiquement
					// figé quand la terre est sèche.
					float elapsed = cropData.AccumulatedGrowthTime;
					float stageFloat = (elapsed / tileData.GrowthTime) * tileData.GrowthStages;
					int stage = Math.Clamp((int)MathF.Floor(stageFloat), 1, tileData.GrowthStages);

					//  SÉCURISATION : l'index doit être compris entre 0 et cropStageTextures.Count-1
					int textureIndex = Math.Clamp(stage - 1, 0, cropStageTextures.Count - 1);
					if (cropStageTextures[textureIndex].Id != 0)
						return cropStageTextures[textureIndex];
				}
			}

			// 3. Sinon texture unique (simple retour)
			return WorldTileRegistry.GetFirstTexture(tileId);
		}
    }
}
