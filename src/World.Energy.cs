// World.Energy.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {

		public static Dictionary<(int x, int y), SteamEngineData> SteamEngines = new();

		public static Dictionary<(int x, int y), DynamoData> Dynamos = new();

		public static Dictionary<(int x, int y), BatteryData> Batteries = new();

		public static Dictionary<(int x, int y), BatteryChargerData> BatteryChargers = new();

		public static Dictionary<(int x, int y), AmpouleData> Ampoules = new();

		// NOUVEAU
		public static Dictionary<(int x, int y), RainCollectorData> RainCollectors = new();

		public static Dictionary<(int x, int y), LogicGateData> LogicGates = new();

		// NOUVEAU : portes logiques
		public static List<EnergyConnection> EnergyConnections = new();

		// toutes les connexions

		// IDs des tuiles "porte logique" (voir worldobjects.json)
		public static readonly HashSet<int> LogicGateObjectIds = new() { 410, 411, 412, 413, 414, 415, 416, 417, 418 };

		public static GateType? GetGateTypeForObjectId(int id) => id switch
		{
			410 => GateType.AND,
			411 => GateType.OR,
			412 => GateType.NAND,
			413 => GateType.NOR,
			414 => GateType.XOR,
			415 => GateType.NOT,
			416 => GateType.SWITCH,
			417 => GateType.PRESSURE_PLATE,
			418 => GateType.DELAY,
			_ => null
		};

		//  Bascule manuellement le délai d'un bloc de délai (GateType.DELAY) au préréglage suivant
		// (Maj+E). Retourne la nouvelle valeur en secondes, ou -1 si (x,y) n'est pas un bloc de délai.
		public static float CycleDelayBlock(int x, int y)
		{
			if (GetGateTypeForObjectId(GetObjectIdAt(x, y)) != GateType.DELAY) return -1f;
			if (!LogicGates.ContainsKey((x, y))) LogicGates[(x, y)] = new LogicGateData { Type = GateType.DELAY };
			var gate = LogicGates[(x, y)];
			int idx = Array.IndexOf(DelayPresets, gate.DelaySeconds);
			idx = (idx + 1) % DelayPresets.Length; // valeur inconnue (idx == -1) : repart proprement du 1er préréglage
			gate.DelaySeconds = DelayPresets[idx];
			return gate.DelaySeconds;
		}

		//  MULTIJOUEUR : logique de câblage électrique centralisée. Utilisée à la fois par
		// l'input local (host / solo) et par le handler réseau côté host quand un client en
		// fait la demande — évite d'avoir deux implémentations qui divergent.
		public static string ConnectEnergyWire(int x1, int y1, int x2, int y2)
		{
			int sourceId = GetObjectIdAt(x1, y1);
			int targetId = GetObjectIdAt(x2, y2);

			//  RÈGLE GÉNÉRALE : on ne peut tirer un câble QUE depuis une tuile ayant un port
			// "output" disponible, et le connecter QUE sur une tuile ayant un port "input"
			// disponible — peu importe son type. Ça évite qu'une tuile sans le bon port
			// (ex : une dynamo, qui n'a pas d'entrée) accepte quand même un câble par erreur.
			if (!HasOutputPort(sourceId)) return "invalid_source";
			if (!HasInputPort(targetId)) return "invalid_target";

			List<EnergyConnection>? connections = null;
			if (sourceId == 403) // Dynamo
			{
				if (!Dynamos.ContainsKey((x1, y1))) Dynamos[(x1, y1)] = new DynamoData();
				connections = Dynamos[(x1, y1)].Connections;
			}
			else if (LogicGateObjectIds.Contains(sourceId)) // Porte logique / interrupteur (sortie)
			{
				if (!LogicGates.ContainsKey((x1, y1)))
					LogicGates[(x1, y1)] = new LogicGateData { Type = GetGateTypeForObjectId(sourceId) ?? GateType.AND };
				connections = LogicGates[(x1, y1)].Connections;
			}
			else return "unsupported_source"; // a un port output dans le JSON mais pas de type de données associé côté code

			if (connections!.Any(c => c.X2 == x2 && c.Y2 == y2)) return "already_exists";

			var connection = new EnergyConnection { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
			connections.Add(connection);
			EnergyConnections.Add(connection);

			if (targetId == 408 && !Ampoules.ContainsKey((x2, y2))) Ampoules[(x2, y2)] = new AmpouleData();
			if (targetId == 405 && !BatteryChargers.ContainsKey((x2, y2))) BatteryChargers[(x2, y2)] = new BatteryChargerData();
			if (LogicGateObjectIds.Contains(targetId) && !LogicGates.ContainsKey((x2, y2)))
				LogicGates[(x2, y2)] = new LogicGateData { Type = GetGateTypeForObjectId(targetId) ?? GateType.AND };

			return "ok";
		}

		//  Pose la batterie portable actuellement tenue par le joueur sur le socle (x,y).
		// Retourne false si ce n'est pas un socle (405) ou s'il contient déjà une batterie.
		public static bool InsertBatteryInCharger(int x, int y, float storedEnergy, float maxCapacity)
		{
			if (GetObjectIdAt(x, y) != 405) return false;
			if (!BatteryChargers.ContainsKey((x, y))) BatteryChargers[(x, y)] = new BatteryChargerData();
			var charger = BatteryChargers[(x, y)];
			if (charger.InsertedBattery != null) return false;
			charger.InsertedBattery = new BatteryData { StoredEnergy = storedEnergy, MaxCapacity = maxCapacity };
			return true;
		}

		//  Retire la batterie posée sur le socle (x,y) et la retourne (pour la remettre dans
		// l'inventaire du joueur, avec sa charge conservée). Retourne null si le socle est vide.
		public static BatteryData? RemoveBatteryFromCharger(int x, int y)
		{
			if (!BatteryChargers.TryGetValue((x, y), out var charger)) return null;
			var battery = charger.InsertedBattery;
			charger.InsertedBattery = null;
			return battery;
		}

		//  Indique si un câble transporte réellement de l'énergie en ce moment (pour la couleur du
		// rendu) : vrai si sa source (dynamo qui produit, ou batterie qui a encore de la charge)
		// est active.
		public static bool IsConnectionPowered(EnergyConnection conn)
		{
			int sourceId = GetObjectIdAt(conn.X1, conn.Y1);
			if (sourceId == 403 && Dynamos.TryGetValue((conn.X1, conn.Y1), out var dynamo))
				return dynamo.ElectricityOutput > 0.01f;
			if (LogicGateObjectIds.Contains(sourceId) && LogicGates.TryGetValue((conn.X1, conn.Y1), out var gate))
				return gate.OutputState;
			return false;
		}

		//  Indique si la tuile (x,y) reçoit actuellement du courant, tous types de sources
		// confondus (dynamo, batterie, ou sortie d'une autre porte logique). Utilisé par les
		// portes logiques pour évaluer leurs entrées, et par l'interrupteur (source manuelle).
		public static bool IsPositionPowered(int x, int y)
		{
			foreach (var d in Dynamos.Values)
				if (d.ElectricityOutput > 0.01f && d.Connections.Any(c => c.X2 == x && c.Y2 == y)) return true;
			foreach (var b in Batteries.Values)
				if (b.StoredEnergy > 0.01f && b.Connections.Any(c => c.X2 == x && c.Y2 == y)) return true;
			foreach (var g in LogicGates.Values)
				if (g.OutputState && g.Connections.Any(c => c.X2 == x && c.Y2 == y)) return true;
			return false;
		}

		//  Compte, pour la tuile (x,y) d'une porte logique, combien de fils entrants elle a
		// (connectés = toute connexion dont X2,Y2 pointe vers cette tuile) et combien d'entre
		// eux sont actuellement alimentés.
		public static (int connected, int active) CountIncomingSignals(int x, int y)
		{
			int connected = 0, active = 0;
			foreach (var d in Dynamos.Values)
				foreach (var c in d.Connections)
					if (c.X2 == x && c.Y2 == y) { connected++; if (d.ElectricityOutput > 0.01f) active++; }
			foreach (var b in Batteries.Values)
				foreach (var c in b.Connections)
					if (c.X2 == x && c.Y2 == y) { connected++; if (b.StoredEnergy > 0.01f) active++; }
			foreach (var g in LogicGates.Values)
				foreach (var c in g.Connections)
					if (c.X2 == x && c.Y2 == y) { connected++; if (g.OutputState) active++; }
			return (connected, active);
		}

		//  Bascule manuellement un interrupteur (GateType.SWITCH). Retourne le nouvel état.
		public static bool ToggleLogicSwitch(int x, int y)
		{
			if (GetGateTypeForObjectId(GetObjectIdAt(x, y)) != GateType.SWITCH) return false;
			if (!LogicGates.ContainsKey((x, y))) LogicGates[(x, y)] = new LogicGateData { Type = GateType.SWITCH };
			var sw = LogicGates[(x, y)];
			sw.ManualState = !sw.ManualState;
			return sw.ManualState;
		}

		public static bool ToggleSteamEngine(int x, int y)
		{
			if (GetObjectIdAt(x, y) != 401) return false;
			if (!SteamEngines.ContainsKey((x, y))) SteamEngines[(x, y)] = new SteamEngineData();
			var engine = SteamEngines[(x, y)];
			engine.IsOn = !engine.IsOn;
			if (!engine.IsOn)
			{
				engine.SteamPressure = 0f;
				engine.ProductionRate = 0f;
			}
			else if (engine.Water > 0.01f && engine.Coal > 0.01f)
			{
				engine.SteamPressure = Math.Max(engine.SteamPressure, 20f);
			}
			return engine.IsOn;
		}

		public static void AddSteamEngineFuel(int x, int y, float water, float coal)
		{
			if (GetObjectIdAt(x, y) != 401) return;
			if (!SteamEngines.ContainsKey((x, y))) SteamEngines[(x, y)] = new SteamEngineData();
			var engine = SteamEngines[(x, y)];
			if (water > 0) engine.Water = Math.Min(SteamEngineData.MaxWater, engine.Water + water);
			if (coal > 0) engine.Coal = Math.Min(SteamEngineData.MaxCoal, engine.Coal + coal);
		}

		public static void Reset()
        {
			ClearTerrainChunkRenderCaches();
            _chunks.Clear();
            _caveChunks.Clear();
            _activeChunks.Clear();
            _tileVariationCache.Clear();
            _tileAnimationStartCache.Clear();
            _heightCache.Clear();
				_climateCache.Clear();
            _caveHeightCache.Clear();
            _caveEntryPairs.Clear();
            _isUnderground = false;
			_activeRitualOrigins.Clear();
            _farmlandMoisture.Clear();
            _lastCropStage.Clear();
            _buildingIdsSurface.Clear();
            _buildingIdsCave.Clear();
            _buildingIdsDungeon.Clear();
            Houses.Clear();
            _currentIndoorGroupIds.Clear();
            _exploredTiles.Clear();
            _boats.Clear();
            SteamEngines.Clear();
            Dynamos.Clear();
            Batteries.Clear();
            BatteryChargers.Clear();
            Ampoules.Clear();
            RainCollectors.Clear();
            LogicGates.Clear();
            EnergyConnections.Clear();
            _dungeonPortals.Clear();
            _knownPortals.Clear();
            _dungeonInstances.Clear();
            _dungeonBosses.Clear();
            _dungeonChunksByInstance.Clear();
            _activeDungeonChunks.Clear();
            _currentDungeonInstanceId = -1;
            _isInDungeon = false;
            _nextDungeonInstanceId = 0;
            _currentBuildingId = 0;
        }

		private static void EnsureRainCollectorEntriesForChunk(ChunkData chunk, float currentTime)
        {
            foreach (var kv in chunk.Objects)
            {
                if (kv.Value == 116 && !RainCollectors.ContainsKey(kv.Key))
                {
                    RainCollectors[kv.Key] = new RainCollectorData
                    {
                        CurrentWater = 0f,
						Capacity = RainCollectorData.MaxCapacity,
                        LastUpdateTime = currentTime
                    };
                }
            }
        }

		// ─────────────────────────────────────────────────────────────────────────
        //  GÉNÉRATION DE CHUNK
        // ─────────────────────────────────────────────────────────────────────────
        public static void LoadOrGenerateChunk(int chunkX, int chunkY, float currentTime, List<Entity> globalEntities)
		{
			var chunksDict = GetChunksDict();
			var chunkKey = (chunkX, chunkY);
			
			if (chunksDict.ContainsKey(chunkKey))
				return;

			// Donjon : jamais de réseau, jamais de sauvegarde générique, jamais de génération de
			// grotte procédurale — un chunk de donjon manquant (en dehors de la zone déjà peinte
			// par le générateur) est simplement un chunk de roche vide, tout comme ses voisins.
			if (_isInDungeon)
			{
				ChunkData? dungeonChunk = !string.IsNullOrEmpty(CurrentSaveName) && _currentDungeonInstanceId >= 0
					? SaveSystem.LoadDungeonChunk(CurrentSaveName, _currentDungeonInstanceId, chunkX, chunkY)
					: null;
				dungeonChunk ??= new ChunkData { IsGenerated = true };
				dungeonChunk.LastAccessTime = currentTime;
				chunksDict[chunkKey] = dungeonChunk;
				foreach (var savedEntity in dungeonChunk.Entities)
				{
					var loadedEntity = globalEntities.FirstOrDefault(entity => entity.NetId == savedEntity.NetId);
					if (loadedEntity == null)
					{
						loadedEntity = savedEntity;
						globalEntities.Add(loadedEntity);
					}

					if (string.Equals(loadedEntity.Species, "ogre", StringComparison.OrdinalIgnoreCase))
						RegisterDungeonBoss(loadedEntity, _currentDungeonInstanceId);
				}
				Program.InvalidateLightGrid();
				return;
			}

			//  MULTIJOUEUR : un client ne génère JAMAIS le monde lui-même (cela diverge
			// inévitablement des objets/cultures/PNJ déjà modifiés côté host). Il demande
			// le chunk authoritaire au host et attend la réponse (voir ReceiveChunkFromNetwork).
			if (NetworkManager.IsClient)
			{
				NetworkManager.RequestChunk(chunkX, chunkY, _isUnderground);
				return;
			}
			
			ChunkData? chunk = null;
			
			if (!string.IsNullOrEmpty(CurrentSaveName))
			{
				chunk = SaveSystem.LoadChunk(CurrentSaveName, chunkX, chunkY, _isUnderground);
				if (chunk != null)
				{
					chunk.LastAccessTime = currentTime;
					chunksDict[chunkKey] = chunk;
					Program.InvalidateLightGrid();
					EnsureRainCollectorEntriesForChunk(chunk, currentTime);
					
					// Mettre à jour les toits pour les murs/portes des villages
					if (chunk.NewWallPositions != null)
					{
						foreach (var (wx, wy) in chunk.NewWallPositions)
						{
							UpdateRoofsAt(wx, wy);
						}
					}
					
					//  TRANSFÉRER LES ENTITÉS DU CHUNK VERS globalEntities (sans doublons)
					//  Dédoublonnage par NetId (identifiant unique et stable), et non plus
					// par "espèce + distance approximative" : ce dernier check confondait des
					// entités distinctes de même espèce proches l'une de l'autre (troupeaux,
					// familles) et ratait les entités ayant bougé de plus de 50px depuis leur
					// dernière sauvegarde — ce qui provoquait des duplications à chaque
					// rechargement du chunk.
					foreach (var entity in chunk.Entities)
					{
						bool exists = globalEntities.Any(e => e.NetId == entity.NetId);
						
						if (!exists)
						{
							globalEntities.Add(entity);
						}
					}
					return;
				}
			}

            if (_chunksBeingGenerated.Contains(chunkKey))
                return;

            _chunksBeingGenerated.Add(chunkKey);
            try
            {
                // Placer un chunk temporaire pour éviter que GenerateNewChunk ne relance la génération
                chunksDict[chunkKey] = new ChunkData();
				chunk = GenerateNewChunk(chunkX, chunkY, chunksDict[chunkKey]);
                if (chunk == null)
                {
                    chunksDict.Remove(chunkKey);
                    return;
                }

                chunk.LastAccessTime = currentTime;
                chunksDict[chunkKey] = chunk;
				Program.InvalidateLightGrid();
                EnsureRainCollectorEntriesForChunk(chunk, currentTime);

                //  CORRECTIF : un chunk fraîchement généré (pas rechargé depuis une sauvegarde)
                // n'était jusqu'ici jamais transféré vers globalEntities — contrairement à la
                // branche SaveSystem.LoadChunk ci-dessus. Résultat : aucune entité créée pendant
                // GenerateNewChunk (PNJ, animaux, insectes, hordes de gobelins...) n'était mise à
                // jour ni dessinée, alors qu'elle existait bien dans chunk.Entities. Même
                // dédoublonnage par NetId que pour la branche "chargé depuis le disque".
                foreach (var entity in chunk.Entities)
                {
                    bool exists = globalEntities.Any(e => e.NetId == entity.NetId);
                    if (!exists)
                        globalEntities.Add(entity);
                }
            }
            finally
            {
                _chunksBeingGenerated.Remove(chunkKey);
            }
        }

		public static void RebuildEnergySystems()
		{
			// Vider les dictionnaires existants
			SteamEngines.Clear();
			Dynamos.Clear();
			Batteries.Clear();
			BatteryChargers.Clear();
			Ampoules.Clear();
			LogicGates.Clear();

			// Parcourir les chunks de surface
			foreach (var (chunkX, chunkY, chunk) in GetAllSurfaceChunks())
			{
				ScanChunkForEnergyObjects(chunk);
			}

			// Parcourir les chunks de grottes
			foreach (var (chunkX, chunkY, chunk) in GetAllCaveChunks())
			{
				ScanChunkForEnergyObjects(chunk);
			}

			Console.WriteLine($" Énergie : {SteamEngines.Count} machines, {Dynamos.Count} dynamos, {Batteries.Count} batteries, {Ampoules.Count} ampoules, {LogicGates.Count} portes logiques");
		}

		private static void ScanChunkForEnergyObjects(ChunkData chunk)
		{
			foreach (var ((x, y), objectId) in chunk.Objects)
			{
				switch (objectId)
				{
					case 401: // Machine à vapeur
						if (!SteamEngines.ContainsKey((x, y)))
							SteamEngines[(x, y)] = new SteamEngineData();
						break;
					case 403: // Dynamo
						if (!Dynamos.ContainsKey((x, y)))
							Dynamos[(x, y)] = new DynamoData();
						break;
					case 405: // Chargeur de batterie
						if (!BatteryChargers.ContainsKey((x, y)))
							BatteryChargers[(x, y)] = new BatteryChargerData();
						break;
					case 408: // Ampoule
						if (!Ampoules.ContainsKey((x, y)))
							Ampoules[(x, y)] = new AmpouleData();
						break;
					case 410: case 411: case 412: case 413: case 414: case 415: case 416: case 417: case 418: // Portes logiques / interrupteur / plaque de pression / délai
						if (!LogicGates.ContainsKey((x, y)))
							LogicGates[(x, y)] = new LogicGateData { Type = GetGateTypeForObjectId(objectId) ?? GateType.AND };
						break;
				}
			}
		}

		public static void RebuildEnergyConnections()
		{
			EnergyConnections.Clear();
			foreach (var dynamo in Dynamos.Values)
			{
				foreach (var conn in dynamo.Connections)
				{
					if (!EnergyConnections.Contains(conn))
						EnergyConnections.Add(conn);
				}
			}
			foreach (var battery in Batteries.Values)
			{
				foreach (var conn in battery.Connections)
				{
					if (!EnergyConnections.Contains(conn))
						EnergyConnections.Add(conn);
				}
			}
			foreach (var gate in LogicGates.Values)
			{
				foreach (var conn in gate.Connections)
				{
					if (!EnergyConnections.Contains(conn))
						EnergyConnections.Add(conn);
				}
			}
		}

		//  Comme IsChunkLoaded, mais à partir de coordonnées de TUILE (mêmes calculs que
        // GetObjectIdAt). Sert à éviter de purger à tort les appareils électriques (dynamo,
        // batterie, chargeur, ampoule) dont le chunk n'est simplement pas encore chargé en
        // mémoire — sans ça, GetObjectIdAt renvoie 0 pour un chunk non chargé, et les boucles
        // de nettoyage d'UpdateEnergySystems supprimaient définitivement leurs données (charge,
        // câbles...) avant même que le joueur ait eu le temps de s'approcher après un chargement.
        public static bool IsTileChunkLoaded(int x, int y)
        {
            int chunkX = x / CHUNK_SIZE;
            int chunkY = y / CHUNK_SIZE;
            if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
            if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
            return IsChunkLoaded(chunkX, chunkY);
        }

		// Application réelle de la mutation (utilisée par AddPlacedObject ci-dessus, par
        // les autres primitives qui placent un objet en interne, et par la réception
        // réseau d'une action confirmée). Ne JAMAIS appeler AddPlacedObject (la version
        // publique) depuis l'intérieur du moteur réseau pour éviter une double réplication.
        public static void AddPlacedObject_Apply(int x, int y, int itemId)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				chunk.Objects[(x, y)] = itemId;
				
				// Gestion de la variation persistante
				var tileDef = WorldTileRegistry.GetTile(itemId);
				if (tileDef != null && tileDef.Variation > 1)
				{
					int variationIndex = new Random((x * 73856093) ^ (y * 19349663) ^ Program.WorldSeed)
										 .Next(tileDef.Variation);
					chunk.Variations[(x, y)] = variationIndex;
				}
				
				// NOUVEAU : Initialiser les données d’aquarium pour l’ID 99
				if (itemId == 99)
				{
					var aquariumData = GetAquariumData(x, y);
					if (aquariumData == null)
					{
						SetAquariumData(x, y, new AquariumData());
					}
				}
				
				// NOUVEAU : Initialiser les données d'ampoule pour l'ID 408
				if (itemId == 408)
				{
					if (!Ampoules.ContainsKey((x, y)))
					{
						Ampoules[(x, y)] = new AmpouleData();
					}
				}

				// NOUVEAU : Initialiser les données de porte logique / interrupteur
				var gateType = GetGateTypeForObjectId(itemId);
				if (gateType != null && !LogicGates.ContainsKey((x, y)))
				{
					LogicGates[(x, y)] = new LogicGateData { Type = gateType.Value };
				}

				// Initialiser les données du collecteur de pluie (ID 116)
				if (itemId == 116)
				{
					if (!RainCollectors.ContainsKey((x, y)))
					{
						RainCollectors[(x, y)] = new RainCollectorData();
					}
				}

				if (itemId == 401 && !SteamEngines.ContainsKey((x, y)))
					SteamEngines[(x, y)] = new SteamEngineData();
				
				// Mise à jour des cercles rituels
				if (itemId == 70)
				{
					UpdateAllRitualCirclesAround(x, y);
				}

				// Un banc vient d'être posé : enregistrer ses places assises (largeur = nombre de places)
				if (itemId == 114)
				{
					int benchWidth = tileDef?.Size?.Width ?? 2;
					for (int i = 0; i < benchWidth; i++)
						RegisterBenchSeat((x + i, y), (x, y), i);
				}

				// Un mur/porte vient d'être posé : recalculer les toits, la maison peut
				// venir de se fermer complètement (ou une autre maison adjacente peut
				// avoir changé de forme).
				if (IsWall(itemId))
				{
					UpdateRoofsAt(x, y);
				}

				//  ÉCLAIRAGE : un objet vient d'apparaître (mur, source de lumière...) — la
				// grille de propagation doit être recalculée, sinon l'effet n'apparaît qu'au
				// prochain déplacement de caméra.
				Program.InvalidateLightGrid();
			}
			else
			{
				Console.WriteLine($"Warning: Tentative d'ajout d'objet dans un chunk non chargé ({chunkX},{chunkY})");
			}
		}

		public static void RemovePlacedObject_Apply(int x, int y)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				// Récupérer l'ID avant suppression
				int oldItemId = 0;
				if (chunk.Objects.TryGetValue((x, y), out int id))
					oldItemId = id;
				
				if (oldItemId != 0)
				{
					var tileData = WorldTileRegistry.GetTile(oldItemId);
					if (tileData != null && tileData.IsCrop)
					{
						_farmlandMoisture.Remove((x, y));
						SetGroundTile_Apply(x, y, 27); // remet le sol en terre sèche (sans réplication)
					}
					if (oldItemId == 114)
					{
						UnregisterBenchSeatsAt((x, y));
					}
				}	

				// Un mur/porte va être retiré : la maison ne peut plus être fermée à cet
				// endroit, donc son toit (s'il y en avait un) doit disparaître. On capture
				// le buildingId associé à cette tuile AVANT suppression pour pouvoir nettoyer
				// son toit explicitement (utile si le mur retiré était isolé, sans voisin).
				int roofBuildingId = BuildingIds.GetValueOrDefault((x, y), 0);
				bool wasWall = IsWall(oldItemId);

				// Voisins muraux encore en place : ils appartiennent potentiellement à la
				// même maison et doivent être recalculés une fois le mur courant retiré.
				List<(int x, int y)>? wallNeighbors = null;
				if (wasWall)
				{
					wallNeighbors = new List<(int x, int y)>();
					for (int dx = -1; dx <= 1; dx++)
					{
						for (int dy = -1; dy <= 1; dy++)
						{
							if (dx == 0 && dy == 0) continue;
							int nx = x + dx;
							int ny = y + dy;
							if (IsWall(GetObjectIdAt(nx, ny)))
								wallNeighbors.Add((nx, ny));
						}
					}
				}

				chunk.Objects.Remove((x, y));
				
				//  AMÉLIORATION : Mettre à jour les cercles rituels si c'était une bougie (ID 70)
				if (oldItemId == 70)
				{
					// Re-vérifier toutes les origines possibles autour de cette position
					UpdateAllRitualCirclesAround(x, y);
				}

				if (wasWall)
				{
					// Nettoyer directement le toit qui occupait cette tuile (couvre le cas
					// d'un mur isolé sans voisin, que UpdateRoofsAt ne pourrait pas retrouver
					// puisque la tuile elle-même n'est plus un mur).
					if (roofBuildingId != 0)
						ClearRoofForBuilding(roofBuildingId);

					// Recalculer les toits depuis les murs voisins encore présents : soit la
					// maison est toujours close ailleurs (autre pièce), soit elle est
					// désormais ouverte et son toit doit être retiré.
					if (wallNeighbors != null)
					{
						foreach (var (nx, ny) in wallNeighbors)
							UpdateRoofsAt(nx, ny);
					}
				}
				
				if (chunk.ArmorStands.ContainsKey((x, y)))
					chunk.ArmorStands.Remove((x, y));
				if (chunk.Crops.ContainsKey((x, y)))
					chunk.Crops.Remove((x, y));
					_lastCropStage.Remove((x, y));
				
				// NOUVEAU : Nettoyer les données d'ampoule
				if (oldItemId == 408)
				{
					Ampoules.Remove((x, y));
				}

				// NOUVEAU : Nettoyer les données de porte logique / interrupteur, et tous les
				// fils qui partent de cette tuile ou y arrivent (sinon on garde des connexions
				// fantômes pointant vers une tuile qui n'existe plus).
				//  Auparavant limité à `GetGateTypeForObjectId(oldItemId) != null`, donc SEULES
				// les portes logiques/l'interrupteur étaient nettoyées : casser/porter une
				// dynamo (403), un chargeur de batterie (405)
				// ou une ampoule (408) laissait leurs câbles connectés en place ("fantômes").
				// On se base maintenant sur la présence d'un port câble (input OU output) dans
				// worldobjects.json, qui couvre TOUTES les tuiles connectables, pas seulement
				// les portes logiques.
				bool wasEnergyNode = HasOutputPort(oldItemId) || HasInputPort(oldItemId);
				if (wasEnergyNode)
				{
					LogicGates.Remove((x, y));
					Dynamos.Remove((x, y));
					Batteries.Remove((x, y));
					BatteryChargers.Remove((x, y));
					EnergyConnections.RemoveAll(c => (c.X1 == x && c.Y1 == y) || (c.X2 == x && c.Y2 == y));
					foreach (var d in Dynamos.Values) d.Connections.RemoveAll(c => (c.X1 == x && c.Y1 == y) || (c.X2 == x && c.Y2 == y));
					foreach (var b in Batteries.Values) b.Connections.RemoveAll(c => (c.X1 == x && c.Y1 == y) || (c.X2 == x && c.Y2 == y));
					foreach (var g in LogicGates.Values) g.Connections.RemoveAll(c => (c.X1 == x && c.Y1 == y) || (c.X2 == x && c.Y2 == y));
				}

				// Nettoyer les données du collecteur de pluie
				if (oldItemId == 116)
				{
					RainCollectors.Remove((x, y));
				}
			}
		}
    }
}