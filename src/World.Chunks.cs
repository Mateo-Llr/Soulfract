// World.Chunks.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {

		public const int CHUNK_SIZE = 16;

		public const int EXTRA_CHUNKS_AROUND_PLAYER = 2;

		// Nombre de tentatives par chunk à chaque intervalle
		
		private static int _currentBuildingId = 0;

		private static Dictionary<(int chunkX, int chunkY), ChunkData> _chunks = new();

		private static HashSet<(int chunkX, int chunkY)> _chunksBeingGenerated = new();

		private static List<(int chunkX, int chunkY)> _activeChunks = new();

		private const int ACTIVE_CHUNKS_PER_FRAME = 1;
		private static Queue<(int chunkX, int chunkY)> _pendingActiveChunkLoads = new();
		private static HashSet<(int chunkX, int chunkY)> _pendingActiveChunkLoadKeys = new();

		private static Dictionary<(int chunkX, int chunkY), ChunkData> _caveChunks = new();

		// ═══════════════════════════════════════════════════════════════════
        // DONJONS — chaque donjon vit dans son PROPRE dictionnaire de chunks
        // (comme un mini plan souterrain rien qu'à lui), toujours construit à
        // partir des coordonnées locales (0,0). Comme ces coordonnées ne sont
        // jamais partagées avec les grottes, la surface, ou un autre donjon
        // (chaque instance a son propre dictionnaire), aucune collision n'est
        // possible, quelle que soit la taille du monde déjà exploré — et on
        // évite tout dépassement arithmétique lié à de trop grandes coordonnées.
        // Un seul donjon est "actif" (chargé en mémoire) à la fois.
        // ═══════════════════════════════════════════════════════════════════
        private const float DUNGEON_ENTRY_SPAWN_CHANCE = 0.035f;

		private static Dictionary<(int chunkX, int chunkY), ChunkData> _activeDungeonChunks = new();

		private static Dictionary<int, Dictionary<(int chunkX, int chunkY), ChunkData>> _dungeonChunksByInstance = new();

		// ─── PORTAILS DE TÉLÉPORTATION (registre persistant, indépendant des chunks chargés) ───
        private static Dictionary<(int x, int y, bool isCave), PortalInfo> _knownPortals = new();

		/// <summary>Le dictionnaire de chunks actuellement pertinent : donjon actif, sinon grotte, sinon surface.</summary>
        private static Dictionary<(int chunkX, int chunkY), ChunkData> GetChunksDict()
        {
            if (_isInDungeon) return _activeDungeonChunks;
            return _isUnderground ? _caveChunks : _chunks;
        }

		public static bool IsUnderground 
        { 
            get => _isUnderground;
            set 
            {
                if (_isUnderground != value)
                {
                    _isUnderground = value;
                    ClearCurrentChunks();
                }
            }
        }

		/// <summary>
		/// Dictionnaire des "buildingId" (zones de maison) actuellement pertinent, séparé par
		/// plan (surface / grottes / donjon) exactement comme GetChunksDict() pour les tuiles.
		/// Évite qu'une maison enregistrée en surface soit encore considérée comme une maison
		/// dans une grotte ou un donjon situé aux mêmes coordonnées (x,y) de tuile.
		/// </summary>
		private static Dictionary<(int x, int y), int> BuildingIds
		{
			get
			{
				if (_isInDungeon) return _buildingIdsDungeon;
				return _isUnderground ? _buildingIdsCave : _buildingIdsSurface;
			}
		}

		public static void AddOverlay(int x, int y, int itemId)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				chunk.Overlays[(x, y)] = itemId;
			}
		}

		public static void AddOverlayNetworked(int x, int y, int itemId)
		{
			if (NetworkManager.IsClient)
			{
				NetworkManager.RequestWorldAction("AddOverlay", x, y, itemId, _isUnderground);
				return;
			}

			AddOverlay(x, y, itemId);
			if (NetworkManager.IsHost)
				NetworkManager.BroadcastWorldAction("AddOverlay", x, y, itemId, _isUnderground);
		}

		public static void AddOverlay_Apply(int x, int y, int itemId) => AddOverlay(x, y, itemId);

		public static void RemoveOverlay(int x, int y)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				chunk.Overlays.Remove((x, y));
			}
		}

		public static void RemoveOverlayNetworked(int x, int y)
		{
			if (NetworkManager.IsClient)
			{
				NetworkManager.RequestWorldAction("RemoveOverlay", x, y, 0, _isUnderground);
				return;
			}

			RemoveOverlay(x, y);
			if (NetworkManager.IsHost)
				NetworkManager.BroadcastWorldAction("RemoveOverlay", x, y, 0, _isUnderground);
		}

		public static int GetOverlayAt(int x, int y)
		{
			// Sécurité : coordonnées extrêmes
			if (x == int.MinValue || x == int.MaxValue || y == int.MinValue || y == int.MaxValue)
				return 0;

			var chunksDict = GetChunksDict();
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk) && chunk.Overlays.TryGetValue((x, y), out int overlayId))
				return overlayId;
			return 0;
		}

		public static void SetGroundTile_Apply(int x, int y, int groundTileId)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				chunk.GroundOverrides[(x, y)] = groundTileId;
				InvalidateTerrainChunkRenderCache(x, y);
			}
		}

		public static void RemoveGroundOverride(int x, int y)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				chunk.GroundOverrides.Remove((x, y));
				InvalidateTerrainChunkRenderCache(x, y);
			}
		}

		//  MULTIJOUEUR : point d'entrée unique pour appliquer une action reçue du réseau
		// (qu'on soit le host qui valide une demande de client, ou un client qui reçoit
		// la confirmation du host). N'appelle QUE les variantes "_Apply" (jamais les
		// versions publiques qui répliqueraient à nouveau l'action en boucle).
		//  MULTIJOUEUR : insère un chunk reçu du host (réponse à RequestChunk). Réutilise
		// directement LoadChunkFromSave, qui sait déjà reconstruire un ChunkData complet
		// (objets, sol, cultures, PNJ...) à partir d'un ChunkSaveData — exactement la même
		// structure que celle utilisée pour les sauvegardes sur disque.
		public static void ReceiveChunkFromNetwork(int chunkX, int chunkY, ChunkSaveData data, bool underground)
		{
			bool saved = _isUnderground;
			_isUnderground = underground;
			try
			{
				LoadChunkFromSave(data);
				Program.InvalidateLightGrid();
			}
			finally
			{
				_isUnderground = saved;
			}
		}

		// Récupérer un chunk à partir des coordonnées d'une tuile
		public static ChunkData? GetChunkAt(int tileX, int tileY)
		{
			// Sécurité : rejeter des coordonnées extrêmes
			if (tileX == int.MinValue || tileX == int.MaxValue || tileY == int.MinValue || tileY == int.MaxValue)
				return null;

			int chunkX = tileX / CHUNK_SIZE;
			int chunkY = tileY / CHUNK_SIZE;
			if (tileX < 0) chunkX = (tileX - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (tileY < 0) chunkY = (tileY - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			chunksDict.TryGetValue((chunkX, chunkY), out var chunk);
			return chunk;
		}

		private static int GetCaveHeightAt(int tileX, int tileY)
        {
            int chunkX = tileX / CHUNK_SIZE;
            int chunkY = tileY / CHUNK_SIZE;
            if (tileX < 0) chunkX = (tileX - CHUNK_SIZE + 1) / CHUNK_SIZE;
            if (tileY < 0) chunkY = (tileY - CHUNK_SIZE + 1) / CHUNK_SIZE;
            
            if (_caveChunks.TryGetValue((chunkX, chunkY), out var chunk))
                if (chunk.Heights.TryGetValue((tileX, tileY), out int h))
                    return h;
            
            if (!_caveHeightCache.TryGetValue((tileX, tileY), out int height))
            {
                float n = GetCaveNoise(tileX, tileY);
                height = (int)Math.Floor(n * 8f);
                if (height > 7) height = 7;
                _caveHeightCache[(tileX, tileY)] = height;
            }
            return height;
        }

		// Toutes les tuiles (avec marge de sécurité) occupées par une structure de village,
		// tous villages confondus, pour toute la durée de la session. Sert à empêcher deux
		// villages générés séparément (chunks différents) de se chevaucher et de se
		// "manger" mutuellement leurs maisons via forceOverwrite.
		private static readonly HashSet<(int x, int y)> _villageClaimedTiles = new();

		/// <summary>
		/// Reconstruit le registre des places assises en scannant tous les chunks chargés
		/// à la recherche de bancs (id 114). Nécessaire car les bancs déjà présents dans une
		/// sauvegarde, ou dans un chunk généré avant que ce registre n'existe/soit rechargé,
		/// ne passent jamais par RegisterBenchSeat autrement.
		/// </summary>
		public static void RefreshAllBenches()
		{
			Benches.Clear();
			var chunksDict = GetChunksDict();
			foreach (var chunk in chunksDict.Values)
			{
				foreach (var kv in chunk.Objects)
				{
					if (kv.Value != 114) continue;
					var (anchorX, anchorY) = kv.Key;
					var tileDef = WorldTileRegistry.GetTile(114);
					int benchWidth = tileDef?.Size?.Width ?? 2;
					for (int i = 0; i < benchWidth; i++)
					{
						// Chaque colonne du banc est une place avec son propre index
						RegisterBenchSeat((anchorX + i, anchorY), (anchorX, anchorY), i);
					}
				}
			}
		}

		private const int MAX_OBJECTS_PER_CHUNK = 8;

		private static void TryAddMineralOverlay(int x, int y, Random rand, ChunkData chunk)
		{
			float noise = PerlinNoise.Noise(x * 0.05f, y * 0.05f);
			int height = GetCaveHeightAt(x, y);
			float depthBonus = height / 7f;
			
			// Seuils plus bas
			float threshold = 0.35f - depthBonus * 0.25f;  // ← abaissé
			
			if (noise > threshold && rand.NextDouble() < 0.7)  // ← chance augmentée
			{
				int overlayId = 0;
				int objectId = chunk.Objects.TryGetValue((x, y), out var objId) ? objId : 0;
				bool isSandstoneWall = false;
				if (objectId != 0 && WorldTileRegistry.TileNameToId.TryGetValue("sandstone wall", out var sandstoneWallId) && objectId == sandstoneWallId)
				{
					isSandstoneWall = true;
				}

				if (isSandstoneWall)
				{
					if (height >= 6)
						overlayId = 1009; // obsidienne
					else if (height >= 5)
						overlayId = 1004; // grenat
					else if (height >= 4)
						overlayId = 1006; // topaze
					else if (height >= 3)
						overlayId = 1007; // onyx
					else if (height >= 2)
						overlayId = 1008; // opale
					else if (height >= 0)
						overlayId = 1005; // ambre
				}
				else
				{
					if (height >= 5)
						overlayId = 1001; // fer
					else if (height >= 3)
						overlayId = 1002; // cuivre
					else if (height >= 0)
						overlayId = 1003; // charbon
				}
				
				if (overlayId != 0)
				{
					string oreKey = GetOreKeyForLegacyOverlayId(overlayId);
					if (!string.IsNullOrWhiteSpace(oreKey) && !chunk.HasTileMeta(x, y, "ore"))
					{
						chunk.SetTileMeta(x, y, "ore", oreKey);
					}
				}
			}
		}

		// Vérifie si le motif de bougies est présent dans le rectangle 7x5 commençant à (originX, originY)
		private static bool IsRitualPatternPresent(int originX, int originY)
		{
			// Charger tous les chunks du rectangle 7x5
			for (int dx = 0; dx < 7; dx++)
				for (int dy = 0; dy < 5; dy++)
					EnsureTileLoaded(originX + dx, originY + dy);

			// Vérifier les 5 emplacements exacts des bougies
			foreach (var (dx, dy) in RitualCandlePositions)
			{
				if (GetObjectIdAt(originX + dx, originY + dy) != 70)
					return false;
			}

			// Vérifier qu'il n'y a aucune autre bougie dans tout le rectangle
			for (int dx = 0; dx < 7; dx++)
			{
				for (int dy = 0; dy < 5; dy++)
				{
					int x = originX + dx;
					int y = originY + dy;
					int objId = GetObjectIdAt(x, y);
					if (objId == 70 && !IsRitualCandlePosition(dx, dy))
						return false;
				}
			}

			// Vérifier que la zone centrale (5x3) ne contient aucun autre objet (sauf cercle rituel)
			for (int dx = 1; dx <= 5; dx++)
			{
				for (int dy = 1; dy <= 3; dy++)
				{
					int x = originX + dx;
					int y = originY + dy;
					int objId = GetObjectIdAt(x, y);
					if (objId != 0 && objId != 4000)
						return false;
				}
			}

			return true;
		}

		private static void UpdateRitualCircle(int originX, int originY, bool place)
		{
			const int circleId = 4000;
			
			//  NOUVEAU : Le cercle ne doit apparaître qu'à une seule position relative
			// Position relative du cercle dans le motif (au centre du rectangle 5x3)
			const int circleRelativeX = 3;  // entre 1 et 5
			const int circleRelativeY = 4;  // entre 1 et 3
			
			int x = originX + circleRelativeX;
			int y = originY + circleRelativeY;
			
			// Force le chargement du chunk
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			if (!chunksDict.ContainsKey((chunkX, chunkY)))
				LoadOrGenerateChunk(chunkX, chunkY, (float)Raylib.GetTime(), Program.GetEntities());

			if (place)
			{
				if (GetObjectIdAt(x, y) != circleId)
					AddPlacedObject(x, y, circleId);
			}
			else
			{
				if (GetObjectIdAt(x, y) == circleId)
					RemovePlacedObject(x, y);
			}
		}

		private static void EnsureTileLoaded(int x, int y)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			// Force le chargement/génération du chunk
			var chunksDict = GetChunksDict();
			if (!chunksDict.ContainsKey((chunkX, chunkY)))
				LoadOrGenerateChunk(chunkX, chunkY, (float)Raylib.GetTime(), Program.GetEntities());
		}

		private static bool PlacePathTile(int x, int y, int pathWidth)
		{
			bool anyPlaced = false;
			int half = pathWidth / 2;
			for (int ox = -half; ox <= half; ox++)
			{
				for (int oy = -half; oy <= half; oy++)
				{
					int px = x + ox;
					int py = y + oy;
					bool isCenterTile = (ox == 0 && oy == 0);

					//  L'élargissement en largeur ne doit jamais peindre une route à
					// l'intérieur d'un autre bâtiment. Le point central du chemin a déjà
					// été validé (mur/porte/bâtiment) par IsPathTileValid ; mais avant ce
					// correctif, les tuiles VOISINES ajoutées uniquement pour la largeur
					// n'étaient vérifiées que pour un objet posé dessus (chunk.Objects),
					// pas pour le sol intérieur d'une maison sans objet (buildingId sans
					// Object), ce qui pouvait faire apparaître un bout de chemin sous un
					// toit voisin. Seul le point central peut légitimement toucher une
					// porte/un bâtiment.
					if (!isCenterTile && GetBuildingIdAt(px, py) != 0)
						continue;

					EnsureTileLoaded(px, py);
					var chunk = GetChunkAt(px, py);
					if (chunk == null)
						continue;

					if (chunk.Objects.TryGetValue((px, py), out int existing))
					{
						if (existing == 84)
						{
							anyPlaced = true;
							continue;
						}
						continue;
					}

					chunk.GroundOverrides[(px, py)] = 83;
					anyPlaced = true;
				}
			}
			return anyPlaced;
		}

		private static void CreateCaveExitForEntry(int entryX, int entryY, int chunkX, int chunkY, Random chunkRandom, ChunkData chunkData)
		{
			if (!_caveEntryPairs.ContainsKey((entryX, entryY)))
				return;

			var existing = _caveEntryPairs[(entryX, entryY)];
			if (existing.caveX != -1 && existing.caveY != -1)
				return; // déjà créée

			// On force la sortie à la même position que l'entrée
			int exitX = entryX;
			int exitY = entryY;

			// Dégager une petite zone ronde autour de la sortie pour éviter que le joueur soit coincé
			ClearCaveExitZone(exitX, exitY, 2);

			// On pose la sortie de grotte (ID 52)
			chunkData.Objects[(exitX, exitY)] = 52;

			// Mise à jour de la paire
			_caveEntryPairs[(entryX, entryY)] = (exitX, exitY);
		}

		private static void ClearCaveExitZone(int centerX, int centerY, int radius)
		{
			int radiusSq = radius * radius;
			for (int dx = -radius; dx <= radius; dx++)
			{
				for (int dy = -radius; dy <= radius; dy++)
				{
					if (dx * dx + dy * dy > radiusSq)
						continue;

					int x = centerX + dx;
					int y = centerY + dy;
					var chunk = GetChunkAt(x, y);
					if (chunk == null)
						continue;

					if (chunk.Objects.ContainsKey((x, y)))
						chunk.Objects.Remove((x, y));
					chunk.GroundOverrides[(x, y)] = 100; // sol de grotte
				}
			}
		}

		//  Probabilité, par chunk généré (hors sous-sol/donjon/village), qu'une horde de
        // gobelins hostiles y apparaisse. Réduite fortement pour éviter un afflux trop
        // important de troupes autour du joueur.
        private const float GOBLIN_HORDE_SPAWN_CHANCE = 0.004f;
        //  Probabilité, PAR GOBELIN d'une horde, qu'il possède un loup de compagnie chevauchable.
        //  Diminuée pour ne pas multiplier les unités de manière excessive.
        private const float GOBLIN_MOUNT_WOLF_CHANCE = 0.12f;

		private const int MAX_CAVE_ENTRIES_PER_CHUNK = 1;

		/// <summary>
        /// Crée (si besoin) tous les chunks vides d'une zone rectangulaire dans le dictionnaire
        /// de chunks ACTUELLEMENT ACTIF (voir GetChunksDict), AVANT que la génération du donjon
        /// ne peigne quoi que ce soit dessus. Comme le dictionnaire actif d'un donjon lui est
        /// entièrement propre, la zone est nécessairement vierge : rien ne peut être écrasé.
        /// </summary>
        private static void EnsureDungeonRegionChunks(int originX, int originY, int width, int height)
        {
            var dict = GetChunksDict();
            int pad = CHUNK_SIZE * 2;
            int minChunkX = FloorDiv(originX - pad, CHUNK_SIZE);
            int maxChunkX = FloorDiv(originX + width + pad, CHUNK_SIZE);
            int minChunkY = FloorDiv(originY - pad, CHUNK_SIZE);
            int maxChunkY = FloorDiv(originY + height + pad, CHUNK_SIZE);

            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            for (int cy = minChunkY; cy <= maxChunkY; cy++)
            {
                var key = (cx, cy);
                if (!dict.ContainsKey(key))
                    dict[key] = new ChunkData { IsGenerated = true };
            }
        }

		/// <summary>Le joueur emprunte le portail de retour : quitte le plan du donjon et renvoie la position près de l'entrée d'origine, dans les grottes.</summary>
        public static bool TryExitDungeon(int portalTileX, int portalTileY, out Vector2 spawnPos)
        {
            spawnPos = Vector2.Zero;
            if (!_dungeonPortals.TryGetValue((portalTileX, portalTileY), out var portal) || portal.Kind != DungeonPortalKind.ReturnExit)
                return false;
            if (!_dungeonInstances.TryGetValue(portal.InstanceId, out var inst))
                return false;

            // On quitte le plan du donjon AVANT de calculer la position d'arrivée, pour que
            // FindSpawnNearExit lise bien les tuiles des grottes (GetChunksDict()).
            _isInDungeon = false;
            _currentDungeonInstanceId = -1;
            _activeDungeonChunks = new Dictionary<(int chunkX, int chunkY), ChunkData>();

            spawnPos = FindSpawnNearExit(inst.EntryPos.x, inst.EntryPos.y);
            return true;
        }

		/// <summary>
        /// Force la sortie du plan de donjon actif, sans passer par un portail (utilisé par ex.
        /// lors d'une réapparition après la mort, pour ne jamais laisser le joueur "coincé" sur
        /// le plan d'un donjon avec une position de surface).
        /// </summary>
        public static void ForceExitDungeon()
        {
            _isInDungeon = false;
            _currentDungeonInstanceId = -1;
            _activeDungeonChunks = new Dictionary<(int chunkX, int chunkY), ChunkData>();
        }

		/// <summary>
        /// À appeler régulièrement (voir UpdateActiveChunks). Détecte la mort d'un boss de
        /// donjon et débloque alors la porte de sortie de la salle finale.
        /// </summary>
        public static void CheckDungeonBossDeaths()
        {
            if (_dungeonBosses.Count == 0) return;
            List<Entity>? dead = null;
            foreach (var kv in _dungeonBosses)
            {
                if (!kv.Key.IsAlive)
                {
                    (dead ??= new List<Entity>()).Add(kv.Key);
                }
            }
            if (dead == null) return;
            foreach (var boss in dead)
            {
                int instanceId = _dungeonBosses[boss];
                _dungeonBosses.Remove(boss);

                if (string.Equals(boss.Species, "ogre", StringComparison.OrdinalIgnoreCase) &&
                    !AchievementManager.IsUnlocked(AchievementType.KillBoss))
                {
                    AchievementManager.UnlockAchievement(AchievementType.KillBoss);
                }

                UnlockDungeonExit(instanceId);
            }
        }

		private static void UnlockDungeonExit(int instanceId)
        {
            if (!_dungeonInstances.TryGetValue(instanceId, out var inst)) return;
            if (inst.BossDefeated) return;
            inst.BossDefeated = true;

            if (!_dungeonChunksByInstance.TryGetValue(instanceId, out var instanceChunks))
                return; // ne devrait jamais arriver (le donjon a forcément été généré pour avoir un boss)

            // On écrit la porte de sortie dans le plan de CE donjon précisément, qu'il soit ou
            // non actuellement affiché à l'écran (le joueur est presque toujours dedans à ce
            // moment, mais on ne suppose rien).
            bool savedIsInDungeon = _isInDungeon;
            int savedInstanceId = _currentDungeonInstanceId;
            var savedActiveChunks = _activeDungeonChunks;
            _isInDungeon = true;
            _currentDungeonInstanceId = instanceId;
            _activeDungeonChunks = instanceChunks;
            try
            {
                int x = inst.ExitDoorTile.x;
                int y = inst.ExitDoorTile.y;
                World.RemovePlacedObject(x, y);
                World.SetGroundTile(x, y, 3000); // DUNGEON_FLOOR_ID
                World.AddPlacedObject(x, y, 50); // même sprite que les entrées de grottes
                _dungeonPortals[(x, y)] = new DungeonPortalInfo { Kind = DungeonPortalKind.ReturnExit, InstanceId = instanceId };
				DungeonGenerator.EnsureExitApproach(inst);
            }
            finally
            {
                _isInDungeon = savedIsInDungeon;
                _currentDungeonInstanceId = savedInstanceId;
                _activeDungeonChunks = savedActiveChunks;
            }

            Console.WriteLine($" Boss du donjon #{instanceId} vaincu, sortie débloquée !");
        }

		public static void SaveAllActiveChunks()
        {
            if (string.IsNullOrEmpty(CurrentSaveName)) return;
            var chunksDict = GetChunksDict();
            if (_isInDungeon && _currentDungeonInstanceId >= 0)
            {
				SaveSystem.SaveDungeonChunks(CurrentSaveName, _currentDungeonInstanceId, chunksDict);
                return;
            }
            foreach (var chunk in chunksDict)
                SaveSystem.SaveChunk(CurrentSaveName, chunk.Key.chunkX, chunk.Key.chunkY, chunk.Value, _isUnderground);
        }

		public static List<(int chunkX, int chunkY, ChunkData data)> GetAllSurfaceChunks()
		{
			var result = new List<(int, int, ChunkData)>();
			foreach (var chunk in _chunks)
				result.Add((chunk.Key.chunkX, chunk.Key.chunkY, chunk.Value));
			return result;
		}

		public static List<(int chunkX, int chunkY, ChunkData data)> GetAllCaveChunks()
		{
			var result = new List<(int, int, ChunkData)>();
			foreach (var chunk in _caveChunks)
				result.Add((chunk.Key.chunkX, chunk.Key.chunkY, chunk.Value));
			return result;
		}

		// ─────────────────────────────────────────────────────────────────────────
        //  GESTION DES CHUNKS
        // ─────────────────────────────────────────────────────────────────────────
        public static List<(int chunkX, int chunkY, ChunkData data)> GetAllChunks()
        {
            var chunksDict = GetChunksDict();
            var result = new List<(int, int, ChunkData)>();
            foreach (var chunk in chunksDict) result.Add((chunk.Key.Item1, chunk.Key.Item2, chunk.Value));
            return result;
        }

		public static void UpdateCampfires(float dt)
		{
			if (dt <= 0f) return;
			foreach (var (_, _, chunk) in GetAllChunks())
			{
				foreach (var entry in chunk.Objects.ToList())
				{
					if (entry.Value != 18 && entry.Value != 32) continue;
					if (!chunk.Containers.TryGetValue(entry.Key, out var fire)) continue;

					foreach (var slot in fire.Slots)
					{
						if (slot.IsEmpty || slot.Item == null) continue;
						var item = slot.Item;
						if (!GameData.TryGetItemByName(item.Name, out var data)) continue;
						if (item.HasMeta("cookProgress")) item.RemoveMeta("cookProgress");
						if (item.HasMeta("burnProgress")) item.RemoveMeta("burnProgress");
						string stage = item.GetMetadataValue("cookStage");
						if (data.Cookable && string.IsNullOrEmpty(stage))
						{
							item.CampfireCookSeconds += dt;
							if (item.CampfireCookSeconds < 60f) continue;

							if (!string.IsNullOrWhiteSpace(data.CookedItemKey) && GameData.TryGetItemByKey(data.CookedItemKey, out var cookedData))
							{
								var cooked = new Item(cookedData.Name, item.Count, cookedData.Color, cookedData.Icon);
								cooked.CustomColors = new List<Color?>(item.CustomColors);
								cooked.Metadata = item.Metadata ?? "";
								cooked.Meta = item.Meta != null
									? new Dictionary<string, string>(item.Meta, StringComparer.OrdinalIgnoreCase)
									: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
								cooked.SetMetadataValue("cookStage", "bleu");
								slot.Item = cooked;
							}
						}
						else if (!string.IsNullOrEmpty(stage) && stage != "burned" && !item.HasMeta("burned"))
						{
							item.CampfireBurnSeconds += dt;
							int stageIndex = stage switch
							{
								"bleu" => 1,
								"saignant" => 2,
								"a_point" => 3,
								"bien_cuit" => 4,
								_ => 1
							};
							int nextStage = Math.Min(4, 1 + (int)(item.CampfireBurnSeconds / 15f));
							if (nextStage > stageIndex)
								item.SetMetadataValue("cookStage", nextStage switch
								{
									2 => "saignant",
									3 => "a_point",
									4 => "bien_cuit",
									_ => "bleu"
								});
							if (item.CampfireBurnSeconds >= 60f)
							{
								item.SetMetadataValue("cookStage", "burned");
								item.SetMetadataValue("burned", "1");
							}
						}
					}
				}
			}
		}

		//  MULTIJOUEUR : accès direct à un chunk en précisant explicitement surface/grotte,
        // sans dépendre (ni modifier) l'état global _isUnderground. Utilisé par le host
        // pour répondre aux demandes de chunk des clients.
        //  MULTIJOUEUR : permet au host de s'assurer qu'un chunk précis est chargé
        // (génération si besoin) pour répondre à la demande d'un client, sans perturber
        // l'état _isUnderground du joueur qui héberge la partie.
        public static void EnsureChunkLoadedForNetwork(int chunkX, int chunkY, bool underground, float currentTime, List<Entity> globalEntities)
        {
            bool saved = _isUnderground;
            _isUnderground = underground;
            try
            {
                LoadOrGenerateChunk(chunkX, chunkY, currentTime, globalEntities);
            }
            finally
            {
                _isUnderground = saved;
            }
        }

		public static ChunkData? GetChunkRaw(int chunkX, int chunkY, bool underground)
        {
            var dict = underground ? _caveChunks : _chunks;
            return dict.TryGetValue((chunkX, chunkY), out var c) ? c : null;
        }

		public static bool IsChunkLoaded(int chunkX, int chunkY)
        {
            var chunksDict = GetChunksDict();
            return chunksDict.ContainsKey((chunkX, chunkY));
        }

		// ─────────────────────────────────────────────────────────────────────────
        //  ACCÈS AUX TILES ET OBJETS
        // ─────────────────────────────────────────────────────────────────────────
		public static int GetObjectIdAt(int x, int y)
		{
			// Sécurité : coordonnées extrêmes
			if (x == int.MinValue || x == int.MaxValue || y == int.MinValue || y == int.MaxValue)
				return 0;

			var chunksDict = GetChunksDict();
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				if (chunk.Objects.TryGetValue((x, y), out int objectId) && objectId != 0)
					return objectId;
				if (chunk.Overlays.TryGetValue((x, y), out int overlayId) && overlayId == 123)
					return overlayId;
			}
			return 0;
		}

		public static ArmorStandData? GetArmorStandAt(int x, int y)
		{
			var chunk = GetChunkAt(x, y);
			return chunk?.GetArmorStandAt(x, y);
		}

		public static void ScanAllRitualCircles()
		{
			_activeRitualOrigins.Clear();
			// Parcours de tous les chunks chargés (surface et grottes)
			var allChunksDict = GetChunksDict();
			foreach (var chunk in allChunksDict.Values)
			{
				foreach (var (x, y) in chunk.Objects.Keys)
				{
					if (chunk.Objects[(x, y)] == 70) // bougie
					{
						UpdateAllRitualCirclesAround(x, y);
					}
				}
			}
		}

		public static void ClearAllEntities(List<Entity> globalEntities)
        {
            //  Dédoublonnage par NetId, cf. correction de duplication dans LoadOrGenerateChunk /
            // UpdateActiveChunks / SaveChunkEntities.
            var chunksDict = GetChunksDict();
            foreach (var chunk in chunksDict.Values)
            {
                foreach (var entity in chunk.Entities)
                {
                    globalEntities.RemoveAll(e => e.NetId == entity.NetId);
                }
                chunk.Entities.Clear();
            }
        }
    }
}
