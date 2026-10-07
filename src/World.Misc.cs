// World.Misc.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {

		public const int RENDER_DISTANCE = 3;

		public const int UNLOAD_DISTANCE = 5;

		private static Random _random = new Random();

		// ─── VENT (oscillation de la flore : arbres, buissons, fleurs...) ─────────────
		//  Direction globale du vent, en degrés (0 = vers la droite). Peut évoluer
		// lentement dans le temps pour donner un vent qui "tourne" légèrement.
		public static float WindDirectionDegrees = 15f;

		//  Amplitude max de l'oscillation (en degrés) et vitesse d'oscillation.
		private const float WindSwayAmplitude = 6f;
		private const float WindSwaySpeed = 1.4f;

		//  Vitesse/amplitude des rafales (variation lente de l'intensité globale du vent).
		private const float WindGustSpeed = 0.35f;
		private const float WindGustAmplitude = 0.5f;
		private const float TreeHitSwayDuration = 0.28f;
		private const float TreeHitSwayAmplitude = 6f;
		private static readonly Dictionary<(int x, int y), (float startedAt, float direction)> _treeHitSways = new();

		//  Calcule l'angle d'oscillation (en degrés) à appliquer au sprite d'une tuile de
		// flore (arbre, buisson, fleur...) donnée, en fonction du temps de jeu et de sa
		// position (afin que les tuiles voisines ne se balancent pas exactement en phase,
		// pour un rendu plus naturel). Le sprite doit être tourné autour du bas de sa
		// hitbox (voir World_Core.cs, calcul de windOrigin dans la boucle de rendu).
		public static float GetWindSwayAngle(int worldX, int worldY, float currentTime, int tileWidth = 1, int tileHeight = 1)
		{
			//  Les grandes tuiles (arbres, grands buissons...) doivent se balancer moins
			// franchement que les plantes de 1x1, car leur longueur de levier est plus grande.
			float sizeScale = 1f / Math.Max(1, Math.Max(tileWidth, tileHeight));

			//  Déphasage pseudo-aléatoire mais stable par tuile, pour désynchroniser le
			// balancement des différentes plantes entre elles.
			float phase = (worldX * 12.9898f + worldY * 78.233f);
			phase = (phase - MathF.Floor(phase / MathF.Tau) * MathF.Tau);

			//  Rafale : une composante lente qui fait varier l'intensité du vent entre
			// ~50% et 100% de l'amplitude max.
			float gust = 1f - WindGustAmplitude + WindGustAmplitude * MathF.Sin(currentTime * WindGustSpeed + phase * 0.15f);

			float stormStrength = 1f + 1.6f * Weather.StormIntensity;
			float direction = WindDirectionDegrees + MathF.Sin(currentTime * 0.12f + phase * 0.08f) * 55f * Weather.StormIntensity;
			float sway = MathF.Sin(currentTime * WindSwaySpeed + phase) * WindSwayAmplitude * sizeScale * gust * stormStrength;

			//  L'oscillation est centrée sur la direction globale du vent (un vent qui
			// souffle vers la droite penchera légèrement plus les plantes de ce côté).
			return (direction * 0.15f) * sizeScale + sway;
		}

		public static void TriggerTreeHitSway(int treeX, int treeY, Vector2 sourceWorldPosition, float currentTime, float tileSize)
		{
			float treeWorldX = (treeX + 0.5f) * tileSize;
			float direction = MathF.Sign(treeWorldX - sourceWorldPosition.X);
			if (direction == 0f) direction = 1f;
			_treeHitSways[(treeX, treeY)] = (currentTime, direction);
		}

		public static float GetTreeHitSwayAngle(int worldX, int worldY, float currentTime)
		{
			if (!_treeHitSways.TryGetValue((worldX, worldY), out var sway)) return 0f;

			float elapsed = currentTime - sway.startedAt;
			if (elapsed >= TreeHitSwayDuration)
			{
				_treeHitSways.Remove((worldX, worldY));
				return 0f;
			}

			float progress = elapsed / TreeHitSwayDuration;
			//  Courbe en cloche (0 -> 1 -> 0) : l'arbre part doucement de son angle
			// initial, atteint son inclinaison maximale au milieu de l'effet, puis
			// revient tout aussi doucement à sa position d'origine (pas de saut brusque
			// au début ni de téléportation en fin d'animation).
			float envelope = MathF.Sin(progress * MathF.PI);
			return sway.direction * TreeHitSwayAmplitude * envelope;
		}

		private static Dictionary<(int x, int y), int> _tileVariationCache = new();

		private static Dictionary<(int x, int y), int> _tileAnimationStartCache = new();

		private static Dictionary<(int x, int y), int> _heightCache = new();

		//  Préréglages de délai (en secondes) proposés au bloc de délai via Maj+E. Le minimum
		// (0.5s) correspond à l'intervalle de simulation de la grille électrique
		// (_energyUpdateInterval côté Program.cs) : en-dessous, le délai n'aurait plus de sens
		// puisque la grille elle-même ne rafraîchit pas plus vite.
		public static readonly float[] DelayPresets = { 0.5f, 1f, 2f, 3f, 5f, 10f, 15f, 30f };

		//  Indique si la tuile (x,y) porte physiquement un poids : un joueur (local ou distant)
		// ou une entité vivante (animal, mob...) dont la position tombe sur cette case. Utilisé par
		// les plaques de pression pour fermer leur circuit tant qu'un poids reste dessus
		// (comportement réaliste : pas de bascule ON/OFF persistante contrairement à l'interrupteur).
		public static bool IsWeightOnTile(int x, int y)
		{
			int ts = Program.TileSize;

			foreach (var p in Program.GetLocalPlayers())
			{
				if ((int)(p.Position.X / ts) == x && (int)(p.Position.Y / ts) == y) return true;
			}

			foreach (var e in Program.GetEntities())
			{
				if (!e.IsAlive) continue;
				if ((int)(e.WorldPos.X / ts) == x && (int)(e.WorldPos.Y / ts) == y) return true;
			}

			return false;
		}

		//  Indique si la tuile d'objet `id` possède un port de sortie ("output") câblable,
		// d'après sa définition dans worldobjects.json (EnergyPorts). Source de vérité unique :
		// on ne se fie plus à des listes d'IDs codées en dur qui peuvent diverger du JSON.
		public static bool HasOutputPort(int id) =>
			WorldTileRegistry.GetTile(id)?.EnergyPorts.Any(p => p.Type == "output") == true;

		//  Idem pour un port d'entrée ("input").
		public static bool HasInputPort(int id) =>
			WorldTileRegistry.GetTile(id)?.EnergyPorts.Any(p => p.Type == "input") == true;

		public static int CurrentBuildingId => _currentBuildingId;

		private static bool _isUnderground = false;

		private static Dictionary<(int x, int y), int> _caveHeightCache = new();

		// Suivi des paires entrée/sortie de grottes
        private static Dictionary<(int surfaceX, int surfaceY), (int caveX, int caveY)> _caveEntryPairs = new();

		private static Random _cavePairRandom = new Random();

		private static bool _isInDungeon = false;

		private static int _currentDungeonInstanceId = -1;

		private static Dictionary<(int x, int y), DungeonPortalInfo> _dungeonPortals = new();

		private static Dictionary<int, DungeonInstance> _dungeonInstances = new();

		private static Dictionary<Entity, int> _dungeonBosses = new();

		private static int _nextDungeonInstanceId = 0;

		/// <summary>Le joueur est-il actuellement à l'intérieur d'un donjon ? (à sauvegarder avec la partie)</summary>
        public static bool IsInDungeon => _isInDungeon;

		/// <summary>Id de l'instance de donjon actuellement active, ou -1 si aucune.</summary>
        public static int CurrentDungeonInstanceId => _currentDungeonInstanceId;

		public static string CurrentSaveName = "";

		private static Texture2D _interactIcon;

		// hold_interact.png
		private static Texture2D _enterCaveIcon;

		// hold_down.png
		private static Texture2D _exitCaveIcon;

		// hold_up.png
		private static Texture2D _craftIcon;

		// hold_craft.png
		private static Texture2D _carryIcon;

		private static Texture2D _placeIcon;

		private static Texture2D _inputButtonTexture;

		// Texture du bouton d'entrée
		private static Texture2D _traderIcon;

		private static Texture2D _questStartIcon;

		private static Texture2D _questFinishIcon;

		private static Texture2D _guildJoinIcon;

		private static Texture2D _alertIcon;

		// PERF : stocké en (int,int) plutôt qu'en string ("x_y"). Avant, chaque
		// Mark/IsTileExplored allouait une chaîne + la hachait — négligeable à
		// l'unité, mais appelé des CENTAINES/MILLIERS de fois par frame (boucle
		// de 41x41 tuiles à chaque tick d'exploration, boucle sur toutes les
		// tuiles visibles à CHAQUE reconstruction du cache de DrawMap pendant
		// un drag/zoom de la carte plein écran) : c'était une source directe
		// de lag, surtout sur l'écran de carte. Un tuple (int,int) a une
		// égalité/hash générés par le compilateur, sans aucune allocation.
		private static HashSet<(int x, int y)> _exploredTiles = new();

		private static Dictionary<int, Texture2D> _tileTextures = new();

		private sealed class TerrainChunkRenderCache
		{
			public RenderTexture2D Texture = default;
			public int LastUsedFrame = 0;
		}

		private const int TerrainChunkCacheLimit = 32;
		private static readonly Dictionary<(int chunkX, int chunkY), TerrainChunkRenderCache> _terrainChunkRenderCaches = new();

		private static void ClearTerrainChunkRenderCaches()
		{
			foreach (var cache in _terrainChunkRenderCaches.Values)
			{
				if (cache.Texture.Id != 0)
					Raylib.UnloadRenderTexture(cache.Texture);
			}
			_terrainChunkRenderCaches.Clear();
		}

		private static void InvalidateTerrainChunkRenderCache(int x, int y)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (!_terrainChunkRenderCaches.Remove((chunkX, chunkY), out var cache)) return;
			if (cache.Texture.Id != 0)
				Raylib.UnloadRenderTexture(cache.Texture);
		}

		internal static Dictionary<string, Texture2D> _bannerPatternTextures = new();

		internal static Dictionary<string, Texture2D> _bannerShapeTextures = new();

		// Pool pour RenderItem (optimisation mémoire)
		private static List<RenderItem> _renderItemsPool = new List<RenderItem>();

		// Éléments externes (hors World) à intégrer au tri par Y du monde le temps d'une frame,
		// par ex. les offrandes du RitualCircleUI posées au sol. Alimenté via QueueExternalRenderItem
		// avant l'appel à DrawWorld(), consommé et vidé à chaque frame dans DrawWorld().
		private static List<RenderItem> _externalRenderItems = new List<RenderItem>();

		// Permet à un système externe (RitualCircleUI, etc.) d'ajouter un élément à dessiner au sol,
		// correctement trié par profondeur (Y) avec le reste du monde (joueur, entités, items...).
		// À appeler avant DrawWorld() pendant la frame courante.
		public static void QueueExternalRenderItem(float y, Action drawAction)
		{
			_externalRenderItems.Add(new RenderItem { Y = y, Kind = RenderItemKind.External, DrawAction = drawAction });
		}

		// Textures de falaise : chargées une seule fois et mises en cache (auparavant rechargées
		// depuis le disque à CHAQUE FRAME dans DrawWorld, ce qui causait un lag énorme et une
		// fuite de mémoire vidéo, une nouvelle texture GPU étant créée sans jamais libérer l'ancienne).
		private static Texture2D? _cliffDownTexCache;

		private static Texture2D GetCliffDownTexture(Texture2D missingTex)
		{
			if (_cliffDownTexCache == null)
			{
				var tex = Raylib.LoadTexture("assets/tiles/cliff_down.png");
				_cliffDownTexCache = tex.Id != 0 ? tex : missingTex;
			}
			return _cliffDownTexCache.Value;
		}

		private static bool _renderItemsPoolInUse = false;

		private static Dictionary<(int x, int y, int zoomLevel), Texture2D> _textureCache = new();

		public static void LoadBannerTextures()
        {
            string[] patterns = { "anchor", "bow", "cross", "crown", "deer", "fist",
                                   "fox", "hammer", "helmet", "key", "lys", "moon", "star", "sword" };
            string[] shapes   = { "angled", "forked", "long_pointed", "pointed",
                                   "scalloped", "shield_shaped", "square", "tatterned" };
            foreach (var p in patterns)
            {
                string path = $"assets/tiles/banner_pattern_{p}.png";
                if (File.Exists(path))
                    _bannerPatternTextures[p] = Raylib.LoadTexture(path);
            }
            foreach (var s in shapes)
            {
                string path = $"assets/tiles/banner_shape_{s}.png";
                if (File.Exists(path))
                    _bannerShapeTextures[s] = Raylib.LoadTexture(path);
            }
            Console.WriteLine($"   Bannières : {_bannerPatternTextures.Count} motifs, {_bannerShapeTextures.Count} formes chargés.");
        }

		public static void DrawGroundItemTag(GroundItemRenderTag tag)
		{
			float size = 28f;
			Raylib.DrawEllipse((int)tag.DrawPos.X, (int)(tag.DrawPos.Y + 5f), tag.ShadowWidth / 2f, 4, new Color(0, 0, 0, 80));

			if (tag.IsLootbag && Program.LootbagTexture.Id != 0)
			{
				Rectangle srcRect = new Rectangle(0, 0, Program.LootbagTexture.Width, Program.LootbagTexture.Height);
				Rectangle destRect = new Rectangle(tag.DrawPos.X, tag.DrawPos.Y, size, size);
				Vector2 origin = new Vector2(size / 2f, size / 2f);
				Raylib.DrawTexturePro(Program.LootbagTexture, srcRect, destRect, origin, tag.Angle, tag.ItemColor);
				return;
			}

			ItemRenderer.DrawItemRotated(tag.ItemData, tag.DrawPos, size, tag.Angle, tag.CustomColor, tag.Metadata);
		}

		public static void DrawCarRenderTag(CarRenderTag tag)
		{
			tag.Car.Draw();
		}

		public static void DrawWireRenderTag(WireRenderTag tag)
		{
			bool powered = tag.Powered;
			float pulse = tag.Pulse;
			Color wireColor = powered
				? new Color((byte)255, (byte)(200 * pulse + 55), (byte)(50 * pulse), (byte)220)
				: new Color((byte)45, (byte)10, (byte)10, (byte)200);

			Raylib.DrawLineEx(tag.P1, tag.P2, 3, wireColor);
			Vector2 mid = (tag.P1 + tag.P2) / 2f;
			Raylib.DrawCircle((int)mid.X, (int)mid.Y, 3, powered ? new Color(255, 200, 100, 180) : new Color(60, 15, 15, 180));
		}

		public static void DrawProjectileRenderTag(ProjectileRenderTag tag)
		{
			if (tag.VisualTexture.Id != 0 && tag.VisualTexture.Width > 0 && tag.VisualTexture.Height > 0)
			{
				float scale = tag.Size / Math.Max(8f, tag.VisualTexture.Width / 2f);
				Vector2 origin = new Vector2(tag.VisualTexture.Width / 2f, tag.VisualTexture.Height / 2f);
				Raylib.DrawTexturePro(tag.VisualTexture,
					new Rectangle(0, 0, tag.VisualTexture.Width, tag.VisualTexture.Height),
					new Rectangle(tag.Position.X, tag.Position.Y, tag.VisualTexture.Width * scale, tag.VisualTexture.Height * scale),
					origin,
					tag.Rotation * 180f / MathF.PI,
					tag.Color);
				return;
			}

			Raylib.DrawCircle((int)tag.Position.X, (int)tag.Position.Y, (int)tag.Size, tag.Color);
		}

		public static void DrawWorldEntityTag(WorldEntityRenderTag tag) { }
		public static void DrawPlayerRenderTag(PlayerRenderTag tag) { }
		public static void DrawWorldObjectRenderTag(WorldObjectRenderTag tag) { }
		public static void DrawIconRenderTag(IconRenderTag tag) { }

		private static Dictionary<(int x, int y), int> _seaFloorHeights = new();

		// Méthode à appeler après le chargement des assets
		public static void LoadInteractionIcons()
		{
			_interactIcon = LoadIcon("assets/gui/hold_interact.png");
			_enterCaveIcon = LoadIcon("assets/gui/hold_down.png");
			_exitCaveIcon = LoadIcon("assets/gui/hold_up.png");
			_craftIcon = LoadIcon("assets/gui/hold_craft.png");
			_carryIcon = LoadIcon("assets/gui/hold_carry.png");
			_placeIcon = LoadIcon("assets/gui/hold_down.png");
			_traderIcon = LoadIcon("assets/gui/icon_trade.png");
			_inputButtonTexture = LoadIcon("assets/gui/input_button.png");
			_questStartIcon = LoadIcon("assets/gui/quest_start.png");
			_questFinishIcon = LoadIcon("assets/gui/quest_finish.png");
			_guildJoinIcon = LoadIcon("assets/gui/guild_joinable.png");
			_alertIcon = LoadIcon("assets/gui/exclamation.png");
		}

		public static Texture2D GetInputButtonTexture() => _inputButtonTexture;

		private static Texture2D LoadIcon(string path)
		{
			if (File.Exists(path))
				return Raylib.LoadTexture(path);
			return new Texture2D();
		}

		// Initialisation / chargement
		// Format de sauvegarde inchangé (HashSet<string> "x_y") pour rester
		// compatible avec les saves existantes : la conversion ne se fait
		// qu'ici, au chargement (opération rare, pas un chemin chaud).
		public static void LoadExploredTiles(HashSet<string> tiles)
		{
			_exploredTiles = new HashSet<(int, int)>(tiles.Count);
			foreach (string key in tiles)
			{
				int sep = key.IndexOf('_');
				if (sep <= 0 || sep >= key.Length - 1) continue;
				if (int.TryParse(key.AsSpan(0, sep), out int x) &&
					int.TryParse(key.AsSpan(sep + 1), out int y))
				{
					_exploredTiles.Add((x, y));
				}
			}
		}

		// Sauvegarde (rare, pas un chemin chaud : la conversion en string ici est OK)
		public static HashSet<string> GetExploredTiles()
		{
			var result = new HashSet<string>(_exploredTiles.Count);
			foreach (var (x, y) in _exploredTiles)
				result.Add($"{x}_{y}");
			return result;
		}

		// Marquer une tuile comme explorée — zéro allocation
		public static void MarkTileExplored(int x, int y)
		{
			_exploredTiles.Add((x, y));
		}

		// Vérifier si une tuile a été explorée — zéro allocation, hot path
		// (appelé pour chaque tuile visible à chaque reconstruction du cache
		// de DrawMap : voir le commentaire sur le champ _exploredTiles).
		public static bool IsTileExplored(int x, int y)
		{
			return _exploredTiles.Contains((x, y));
		}

		private static Texture2D GetInteractionIcon(int objectId, string actionType)
		{
			return actionType switch
			{
				"E" => _interactIcon,
				"F" => _carryIcon,
				"craft" => _craftIcon,
				"enterCave" => _enterCaveIcon,
				"exitCave" => _exitCaveIcon,
				"place" => _placeIcon,
				_ => _interactIcon
			};
		}

		public static ArmorStandData BuildArmorStandFromSave(ArmorStandSaveData save)
		{
			var standData = new ArmorStandData();
			standData.CurrentPose = save.CurrentPose ?? "idle";

			Item? Lookup(string? name)
			{
				if (string.IsNullOrEmpty(name)) return null;
				var d = GameData.ItemDatabase.Values.FirstOrDefault(it => it.Name == name);
				return d.ID != 0 ? new Item(d.Name, 1, d.Color, d.Icon) : null;
			}

			standData.Head = Lookup(save.Head);
			standData.Body = Lookup(save.Body);
			standData.Legs = Lookup(save.Legs);
			standData.MainHand = Lookup(save.MainHand);
			standData.OffHand = Lookup(save.OffHand);
			standData.Face = Lookup(save.Face);
			standData.Ears = Lookup(save.Ears);
			standData.Neck = Lookup(save.Neck);
			standData.Waist = Lookup(save.Waist);
			standData.Feet = Lookup(save.Feet);
			standData.Back = Lookup(save.Back);

			return standData;
		}

		private static Dictionary<(int x, int y), int> _buildingIdsSurface = new();

		private static Dictionary<(int x, int y), int> _buildingIdsCave = new();

		private static Dictionary<(int x, int y), int> _buildingIdsDungeon = new();

		public static void SetGroundTileToDry(int x, int y)
		{
			int currentGround = GetGroundTileIdAt(x, y);
			if (currentGround == 27 || currentGround == 29)
				SetGroundTile(x, y, 27); // passe par la version réseau
		}

		public static void SetGroundTileToWet(int x, int y)
		{
			int currentGround = GetGroundTileIdAt(x, y);
			if (currentGround == 27 || currentGround == 29)
				SetGroundTile(x, y, 29); // passe par la version réseau
		}

		public static void SetGroundTile(int x, int y, int groundTileId)
		{
			if (NetworkManager.IsClient)
			{
				NetworkManager.RequestWorldAction("SetGroundTile", x, y, groundTileId, _isUnderground);
				return;
			}
			SetGroundTile_Apply(x, y, groundTileId);
			if (NetworkManager.IsHost)
				NetworkManager.BroadcastWorldAction("SetGroundTile", x, y, groundTileId, _isUnderground);
		}

		public static int GetBuildingIdAt(int x, int y)
		{
			return BuildingIds.GetValueOrDefault((x, y), 0);
		}

		private static int ComputeBuildingId(int doorX, int doorY)
		{
			return Math.Abs(doorX * 73856093 ^ doorY * 19349663) % 10000 + 1000;
		}

		// ─────────────────────────────────────────────────────────────────────────
        //  BRUIT D'ALTITUDE (60 niveaux)
        // ─────────────────────────────────────────────────────────────────────────
        public const int MAX_HEIGHT_LEVELS = 60;

		private const float MOUNTAIN_FACTOR = 2.5f;

		private static float GetHeightNoise(float x, float y)
		{
			float large = PerlinNoise.Noise(x * 0.0005f, y * 0.0005f);   // 0.0015 → 0.0005
			float medium = PerlinNoise.Noise(x * 0.002f, y * 0.002f);     // 0.006 → 0.002
			float small = PerlinNoise.Noise(x * 0.008f, y * 0.008f);      // 0.025 → 0.008
			float detail = PerlinNoise.Noise(x * 0.026f, y * 0.026f) * 0.25f; // 0.08 → 0.026
			float h = large * 0.55f + medium * 0.25f + small * 0.15f + detail * 0.05f;
			h = (h + 1f) / 2f;
			h = MathF.Pow(h, 1.5f);
			if (h > 0.55f) h = 0.55f + (h - 0.55f) * 3.0f;
			h = Math.Clamp(h, 0f, 1f);
			return h;
		}

		private static float GetCaveNoise(float x, float y)
        {
            float h = PerlinNoise.Noise(x * 0.012f, y * 0.012f);
            h += PerlinNoise.Noise(x * 0.035f, y * 0.035f) * 0.4f;
            h += PerlinNoise.Noise(x * 0.09f, y * 0.09f) * 0.2f;
            return (h + 1f) / 2f;
        }

		private static float GetCavityNoise(float x, float y)
        {
            float h = PerlinNoise.Noise(x * 0.025f, y * 0.025f);
            return (h + 1f) / 2f;
        }

		public static void ClearDoorTiles()
		{
			_doorTiles.Clear();
		}

		public static void RegisterDoorTile(int x, int y) => _doorTiles.Add((x, y));

		public static bool IsDoorTile(int x, int y) => _doorTiles.Contains((x, y));

		// Registre des places assises sur les bancs. Une place = une colonne du banc
		// (un banc de N tuiles de large offre N places). On stocke un point d'approche
		// praticable séparé de la position visuelle d'assise, pour éviter qu'un PNJ
		// essaie de marcher directement sur une tuile bloquée par la collision de l'objet.
		public static Dictionary<int, VillageWorkZone> WorkZones = new();
		private static int _nextWorkZoneId = 1;
		public static List<VillageCenter> VillageCenters { get; } = new();
		private static bool _villageCentersReconstructed;

		public static VillageCenter? FindNearestVillageCenter(Vector2 worldPos, float maxDistance = 2500f)
		{
			if (!_villageCentersReconstructed)
			{
				_villageCentersReconstructed = true;
				// CreateVillageCenter peut appeler EnsureTileLoaded, qui ajoute des chunks
				// au dictionnaire. La reconstruction doit donc parcourir un instantané stable.
				var chunksSnapshot = GetChunksDict().Values.ToList();
				foreach (var chunk in chunksSnapshot)
				{
					var objectsSnapshot = chunk.Objects.ToList();
					foreach (var objectTile in objectsSnapshot)
					{
						if (objectTile.Value == 136)
							CreateVillageCenter(0, 0, objectTile.Key);
					}
				}
			}

			VillageCenter? closest = null;
			float bestDistanceSquared = maxDistance * maxDistance;

			foreach (var center in VillageCenters)
			{
				Vector2 centerPos = GetVillageCenterWorldPosition(center);
				float distanceSquared = Vector2.DistanceSquared(worldPos, centerPos);
				if (distanceSquared < bestDistanceSquared)
				{
					bestDistanceSquared = distanceSquared;
					closest = center;
				}
			}

			return closest;
		}

		public static Vector2 GetVillageCenterWorldPosition(VillageCenter center) => new(
			(center.CenterTile.x + 0.5f) * Program.TileSize,
			(center.CenterTile.y + 0.5f) * Program.TileSize);

		public static void CreateVillageCenter(int centralX, int centralY, (int x, int y)? wellPos)
		{
			(int x, int y) centerTile = wellPos.HasValue
				? (wellPos.Value.x + 1, wellPos.Value.y + 3)
				: (centralX, centralY);

			if (VillageCenters.Any(c => c.CenterTile == centerTile))
				return;

			var center = new VillageCenter { CenterTile = centerTile };
			VillageCenters.Add(center);

			for (int x = centerTile.x - center.PlazaRadius; x <= centerTile.x + center.PlazaRadius; x++)
			{
				for (int y = centerTile.y - center.PlazaRadius; y <= centerTile.y + center.PlazaRadius; y++)
				{
					if (Math.Abs(x - centerTile.x) + Math.Abs(y - centerTile.y) > center.PlazaRadius + 2)
						continue;

					EnsureTileLoaded(x, y);
					var chunk = GetChunkAt(x, y);
					if (chunk == null || chunk.Objects.ContainsKey((x, y)))
						continue;

					chunk.GroundOverrides[(x, y)] = 83;
				}
			}
		}

		public static void ExchangeItemsAtVillageCenter(Entity npc, VillageCenter center)
		{
			if (Vector2.DistanceSquared(npc.WorldPos, GetVillageCenterWorldPosition(center)) > center.ExchangeRadius * center.ExchangeRadius)
				return;

			foreach (var partner in Program.entities)
			{
				if (ReferenceEquals(partner, npc) || !partner.IsAlive || !partner.IsVillager || partner.IsTamed)
					continue;
				if (Vector2.DistanceSquared(partner.WorldPos, npc.WorldPos) > 90f * 90f)
					continue;

				Entity? giver = null;
				Entity? receiver = null;
				if (HasFoodStack(npc) && !HasFoodStack(partner))
				{
					giver = npc;
					receiver = partner;
				}
				else if (HasFoodStack(partner) && !HasFoodStack(npc))
				{
					giver = partner;
					receiver = npc;
				}

				if (giver == null || receiver == null)
					continue;

				var foodSlot = giver.Inventory.Slots.FirstOrDefault(slot => slot.Count > 1 && slot.Item != null &&
					GameData.ItemDatabase.TryGetValue(GameData.GetItemId(slot.Item.Name), out var itemData) && itemData.Type == ItemType.Food);
				if (foodSlot?.Item == null || !ContainerHasSpaceForItem(receiver.Inventory, foodSlot.Item.Name))
					continue;

				StackItemIntoContainer(receiver.Inventory, foodSlot.Item, 1);
				foodSlot.Count--;
				receiver.Hunger = Math.Max(0f, receiver.Hunger - 25f);
				return;
			}
		}

		private static bool HasFoodStack(Entity npc) => npc.Inventory.Slots.Any(slot => slot.Count > 0 && slot.Item != null &&
			GameData.ItemDatabase.TryGetValue(GameData.GetItemId(slot.Item.Name), out var itemData) && itemData.Type == ItemType.Food);

		public static readonly Dictionary<ProfessionType, WorkZoneKind> ProfessionZoneKind = new()
		{
			{ ProfessionType.Farmer, WorkZoneKind.Field },
			{ ProfessionType.Lumberjack, WorkZoneKind.LumberCamp },
			{ ProfessionType.Miner, WorkZoneKind.MineEntrance },
			{ ProfessionType.Hunter, WorkZoneKind.HuntingGround },
		};

		public static (int zoneId, Vector2 slotPos)? FindNearestFreeWorkSlot(WorkZoneKind kind, Vector2 worldPos, float maxDistance = 2000f)
		{
			int bestZoneId = -1;
			int bestSlotIndex = -1;
			float bestDistSq = maxDistance * maxDistance;

			int fallbackZoneId = -1;
			int fallbackSlotIndex = -1;
			float fallbackDistSq = maxDistance * maxDistance;

			foreach (var kvp in WorkZones)
			{
				if (kvp.Value.Kind != kind) continue;
				var zone = kvp.Value;
				for (int i = 0; i < zone.Slots.Count; i++)
				{
					float distSq = Vector2.DistanceSquared(worldPos, zone.Slots[i].WorldPos);
					if (distSq > bestDistSq && distSq > fallbackDistSq)
						continue;

					if (!zone.Slots[i].Occupied)
					{
						if (distSq < bestDistSq)
						{
							bestDistSq = distSq;
							bestZoneId = kvp.Key;
							bestSlotIndex = i;
						}
					}
					else if (distSq < fallbackDistSq)
					{
						fallbackDistSq = distSq;
						fallbackZoneId = kvp.Key;
						fallbackSlotIndex = i;
					}
				}
			}

			if (bestZoneId != -1)
			{
				var slot = WorkZones[bestZoneId].Slots[bestSlotIndex];
				slot.Occupied = true;
				return (bestZoneId, slot.WorldPos);
			}

			if (fallbackZoneId != -1)
			{
				// Si tous les emplacements de la zone sont déjà pris, on réutilise le plus proche
				// plutôt que de laisser le fermier sans travail. Cela évite qu'un village avec
				// un unique champ saturé bloque forcément les fermiers suivants.
				var slot = WorkZones[fallbackZoneId].Slots[fallbackSlotIndex];
				return (fallbackZoneId, slot.WorldPos);
			}

			return null;
		}

		public static void ReleaseWorkSlot(int zoneId, Vector2 slotPos)
		{
			if (!WorkZones.TryGetValue(zoneId, out var zone)) return;
			var slot = zone.Slots.FirstOrDefault(s => s.WorldPos == slotPos);
			if (slot != null) slot.Occupied = false;
		}

		private static void PopulateWorkSlots(VillageWorkZone zone, Random rand)
		{
			for (int i = 0; i < zone.MaxWorkers; i++)
			{
				float angle = (float)(i * (Math.PI * 2 / zone.MaxWorkers) + rand.NextDouble() * 0.4);
				float dist = zone.Radius * (0.4f + (float)rand.NextDouble() * 0.5f);
				Vector2 pos = zone.AnchorPos + new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
				zone.Slots.Add(new WorkSlot { WorldPos = pos });
			}
		}

		private static readonly Dictionary<(int x, int y), Guid> _reservedStationTiles = new();

		public static Vector2? FindStationTileInBounds(Rectangle bounds, IEnumerable<int> stationIds)
		{
			int xMin = (int)bounds.X;
			int yMin = (int)bounds.Y;
			int xMax = (int)(bounds.X + bounds.Width);
			int yMax = (int)(bounds.Y + bounds.Height);
			var allowed = new HashSet<int>(stationIds);

			for (int x = xMin; x < xMax; x++)
			{
				for (int y = yMin; y < yMax; y++)
				{
					int objId = GetObjectIdAt(x, y);
					if (objId == 0 || !allowed.Contains(objId)) continue;
					return new Vector2(x * Program.TileSize + Program.TileSize / 2f, y * Program.TileSize + Program.TileSize / 2f);
				}
			}

			return null;
		}

		public static Vector2? FindNearestUnclaimedStation(Vector2 worldPos, IEnumerable<int> stationIds, float maxDistance, Guid thisNetId)
		{
			var allowed = new HashSet<int>(stationIds);
			Vector2? bestPos = null;
			float bestDistSq = maxDistance * maxDistance;
			int bestX = 0, bestY = 0;

			int minTileX = (int)Math.Floor((worldPos.X - maxDistance) / Program.TileSize);
			int maxTileX = (int)Math.Floor((worldPos.X + maxDistance) / Program.TileSize);
			int minTileY = (int)Math.Floor((worldPos.Y - maxDistance) / Program.TileSize);
			int maxTileY = (int)Math.Floor((worldPos.Y + maxDistance) / Program.TileSize);

			for (int x = minTileX; x <= maxTileX; x++)
			{
				for (int y = minTileY; y <= maxTileY; y++)
				{
					int objId = GetObjectIdAt(x, y);
					if (objId == 0 || !allowed.Contains(objId)) continue;
					if (_reservedStationTiles.TryGetValue((x, y), out var owner) && owner != thisNetId) continue;

					var candidate = new Vector2(x * Program.TileSize + Program.TileSize / 2f, y * Program.TileSize + Program.TileSize / 2f);
					float distSq = Vector2.DistanceSquared(worldPos, candidate);
					if (distSq < bestDistSq)
					{
						bestDistSq = distSq;
						bestPos = candidate;
						bestX = x;
						bestY = y;
					}
				}
			}

			if (bestPos.HasValue)
				_reservedStationTiles[(bestX, bestY)] = thisNetId;
			return bestPos;
		}

		public class BenchSeat
		{
			public (int x, int y) AnchorTile;
			public Vector2 SeatWorldPos;
			public Vector2 ApproachWorldPos;
			public bool Occupied;
			public int SeatIndex; // 0 = gauche, 1 = droite, etc.
		}

		public static Dictionary<(int x, int y, int index), BenchSeat> Benches = new();

		/// <summary>
		/// Enregistre une place assise sur un banc. seatTile est la colonne de la place
		/// (dans l'emprise du banc), anchorTile est la tuile qui porte réellement l'objet
		/// (utile pour vérifier que le banc n'a pas été détruit).
		/// </summary>
		public static void RegisterBenchSeat((int x, int y) seatTile, (int x, int y) anchorTile, int seatIndex)
		{
			int ts = Program.TileSize;
			Vector2 seatWorld = new Vector2(seatTile.x * ts + ts / 2f, seatTile.y * ts + ts / 2f);
			Vector2 approachWorld = FindWalkableApproachNear(seatTile, anchorTile) ?? seatWorld;

			// Clé unique : anchorTile + seatIndex pour éviter les conflits
			var key = (anchorTile.x, anchorTile.y, seatIndex);
			Benches[key] = new BenchSeat
			{
				AnchorTile = anchorTile,
				SeatWorldPos = seatWorld,
				ApproachWorldPos = approachWorld,
				Occupied = false,
				SeatIndex = seatIndex
			};
		}

		private static Vector2? FindWalkableApproachNear((int x, int y) seatTile, (int x, int y) anchorTile)
		{
			int ts = Program.TileSize;

			// Si la tuile de la place elle-même est libre (cas des places qui ne portent
			// pas l'ancre de l'objet), on peut s'y tenir directement.
			if (GetObjectIdAt(seatTile.x, seatTile.y) == 0)
				return new Vector2(seatTile.x * ts + ts / 2f, seatTile.y * ts + ts / 2f);

			// Sinon on cherche une tuile libre et praticable adjacente.
			foreach (var (dx, dy) in new (int, int)[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
			{
				int nx = seatTile.x + dx;
				int ny = seatTile.y + dy;
				if (GetObjectIdAt(nx, ny) != 0) continue;
				int groundId = GetGroundTileIdAt(nx, ny);
				var groundTile = WorldTileRegistry.GetTile(groundId);
				if (groundTile == null || !groundTile.Walkable) continue;
				return new Vector2(nx * ts + ts / 2f, ny * ts + ts / 2f);
			}

			return null;
		}

		public static void UnregisterBenchSeatsAt((int x, int y) anchorTile)
		{
			var keysToRemove = Benches.Keys
				.Where(k => k.x == anchorTile.x && k.y == anchorTile.y)
				.ToList();
			foreach (var key in keysToRemove)
				Benches.Remove(key);
		}

		/// <summary>
		/// Cherche la place assise libre la plus proche et la réserve (occupé = true).
		/// Retourne null si aucune place libre n'est trouvée dans le rayon donné.
		/// </summary>
		public static ((int x, int y, int index) key, Vector2 seatPos, Vector2 approachPos)? FindNearestFreeBench(Vector2 worldPos, float maxDistance = 800f)
		{
			if (Benches.Count == 0)
				RefreshAllBenches();

			(int x, int y, int index)? bestKey = null;
			BenchSeat? bestSeat = null;
			float bestDistSq = maxDistance * maxDistance;

			foreach (var kvp in Benches)
			{
				if (kvp.Value.Occupied) continue;
				if (GetObjectIdAt(kvp.Value.AnchorTile.x, kvp.Value.AnchorTile.y) != 114) continue;

				float distSq = Vector2.DistanceSquared(worldPos, kvp.Value.ApproachWorldPos);
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					bestKey = kvp.Key;
					bestSeat = kvp.Value;
				}
			}

			if (bestKey.HasValue && bestSeat != null)
			{
				bestSeat.Occupied = true;
				return (bestKey.Value, bestSeat.SeatWorldPos, bestSeat.ApproachWorldPos);
			}

			return null;
		}

		public static void ReleaseBench((int x, int y, int index)? seatKey)
		{
			if (seatKey.HasValue && Benches.TryGetValue(seatKey.Value, out var seat))
				seat.Occupied = false;
		}

		public static Vector2 GetVisualPosition(Vector2 worldPos)
        {
            int tileX = (int)Math.Floor(worldPos.X / Program.TileSize);
            int tileY = (int)Math.Floor(worldPos.Y / Program.TileSize);
            int height = GetHeightAt(tileX, tileY);
            float yOffset = -height * Program.TileSize / 4;
            return new Vector2(worldPos.X, worldPos.Y + yOffset);
        }

		public static int GetGroundTileIdAt(float wX, float wY)
		{
			int x = (int)Math.Floor(wX / Program.TileSize);
			int y = (int)Math.Floor(wY / Program.TileSize);
			return GetGroundTileIdAt(x, y);
		}

		private static int GetCaveGroundTileIdAt(float wX, float wY)
		{
			// wX/wY are tile coordinates (not pixel coords) in the cave plane.
			int x = (int)Math.Floor(wX);
			int y = (int)Math.Floor(wY);
			if (!_isInDungeon)
				return 100; // pierre de grotte

			// Déterminer le biome en surface à cette position afin d'adapter les grottes
			var climate = GetClimateAt(x, y);
			Biome surfaceBiome = WorldGeneration.GetBiome(climate.temperature, climate.humidity, climate.elevation);

			// Si la surface est désertique, préférer la tuile 'sandstone' si elle existe
			if (surfaceBiome == Biome.Desert)
			{
				if (WorldTileRegistry.TileNameToId.TryGetValue("sandstone", out int sandId))
					return sandId;
				// fallback sur le sable classique
				if (WorldTileRegistry.TileNameToId.TryGetValue("sand", out int sandFallback))
					return sandFallback;
				return 11; // sand
			}

			// Sinon, conserver la logique existante pour les biomes souterrains
			Biome caveBiome = GetCaveBiomeAt(x, y);
			switch (caveBiome)
			{
				case Biome.Mesa: return 101;   // Lava / mesa cave
				case Biome.Beach: return 102;  // Crystal cave
				case Biome.Swamp: return 103;  // Mushroom soil
				case Biome.IceSpikes: return 104; // Abyss
				default: return 100; // default cave stone
			}
		}

		private static void DrawTemporaryArrow(Vector2 from, Vector2 to, Color color, float thickness)
		{
			Raylib.DrawLineEx(from, to, thickness, color);
			
			Vector2 dir = to - from;
			if (dir.Length() < 0.01f) return;
			dir = Vector2.Normalize(dir);
			
			Vector2 perp = new Vector2(-dir.Y, dir.X);
			float arrowSize = 10f;
			
			Vector2 tip1 = to - dir * arrowSize + perp * (arrowSize / 2);
			Vector2 tip2 = to - dir * arrowSize - perp * (arrowSize / 2);
			
			Raylib.DrawLineEx(to, tip1, thickness, color);
			Raylib.DrawLineEx(to, tip2, thickness, color);
		}

		private static readonly (int dx, int dy)[] RitualCandlePositions = new (int, int)[]
		{
			(3,0), (0,1), (6,1), (1,4), (5,4)
		};

		private static HashSet<(int x, int y)> _activeRitualOrigins = new HashSet<(int, int)>();

		// Petite fonction utilitaire pour vérifier si (dx,dy) fait partie des positions des bougies
		private static bool IsRitualCandlePosition(int dx, int dy)
		{
			foreach (var (cdx, cdy) in RitualCandlePositions)
				if (cdx == dx && cdy == dy) return true;
			return false;
		}

		// Vérifie toutes les origines possibles autour d'une position et synchronise les cercles rituels
		private static void UpdateAllRitualCirclesAround(int x, int y)
		{
			var validOrigins = new HashSet<(int x, int y)>();
			int searchRadius = 20;

			for (int ox = x - searchRadius; ox <= x + searchRadius; ox++)
			{
				for (int oy = y - searchRadius; oy <= y + searchRadius; oy++)
				{
					if (IsRitualPatternPresent(ox, oy))
					{
						validOrigins.Add((ox, oy));
						Console.WriteLine($" Origine trouvée : ({ox}, {oy})");
					}
				}
			}

			//  NOUVEAU : Si plusieurs origines valides sont trouvées, ne garder qu'une seule
			if (validOrigins.Count > 1)
			{
				// Garder celle avec le hash le plus petit (ou n'importe quelle règle)
				var firstOrigin = validOrigins.OrderBy(o => o.x).ThenBy(o => o.y).First();
				Console.WriteLine($" {validOrigins.Count} cercles potentiels détectés. Un seul sera gardé à ({firstOrigin.x}, {firstOrigin.y})");
				validOrigins.Clear();
				validOrigins.Add(firstOrigin);
			}

			// Ajouter les nouveaux cercles (maintenant au maximum 1)
			foreach (var origin in validOrigins)
			{
				if (!_activeRitualOrigins.Contains(origin))
				{
					UpdateRitualCircle(origin.x, origin.y, true);
					_activeRitualOrigins.Add(origin);
				}
			}

			// Supprimer les cercles dont l'origine n'est plus valide
			var toRemove = _activeRitualOrigins.Where(o => !validOrigins.Contains(o)).ToList();
			foreach (var origin in toRemove)
			{
				UpdateRitualCircle(origin.x, origin.y, false);
				_activeRitualOrigins.Remove(origin);
			}
		}

		public static bool IsRitualCircleAt(int x, int y)
		{
			return GetObjectIdAt(x, y) == 4000;
		}

		//  Retourne la position (tuile) du cercle de rituel lui-même (objet 4000) pour chaque
		// origine active. Utilisé par RitualCircleUI pour savoir où dessiner/interagir en jeu.
		public static List<(int x, int y)> GetActiveRitualCircleTiles()
		{
			const int circleRelativeX = 3;
			const int circleRelativeY = 4;
			var result = new List<(int x, int y)>();
			foreach (var origin in _activeRitualOrigins)
				result.Add((origin.x + circleRelativeX, origin.y + circleRelativeY));
			return result;
		}

		// Retourne vrai si la tuile (x,y) fait partie de la zone centrale d'un cercle rituel actif
		public static bool IsTilePartOfActiveRitual(int x, int y)
		{
			// Pour chaque origine active, vérifier si (x,y) est dans le rectangle central 5x3
			foreach (var origin in _activeRitualOrigins)
			{
				int ox = origin.x;
				int oy = origin.y;
				for (int dx = 1; dx <= 5; dx++)
				{
					for (int dy = 1; dy <= 3; dy++)
					{
						if (x == ox + dx && y == oy + dy)
							return true;
					}
				}
			}
			return false;
		}

		private static StructureData? PickVillageStructure(List<StructureData> candidateStructures, Random rand)
		{
			var proceduralHouse = StructureManager.CreateProceduralVillageHouse(rand);

			var eligible = candidateStructures
				.Where(s => s.Tiles != null && s.Tiles.Count > 0 && s.Tiles.Any(t => t.PlacedTileId == 84))
				.ToList();

			if (eligible.Count == 0)
			{
				eligible = candidateStructures
					.Where(s => s.Tiles != null && s.Tiles.Count > 0)
					.ToList();
			}

			if (eligible.Count == 0)
				return proceduralHouse;

			// Les maisons de village sont désormais générées procéduralement par défaut
			// pour obtenir de plus petites pièces, des sols en parquet et des murs en briques.
			return proceduralHouse;
		}

		public static int GetSeaFloorDepth(int x, int y)
		{
			if (_seaFloorHeights.TryGetValue((x, y), out int depth))
				return depth;
			return 25; // Profondeur par défaut augmentée (était 5)
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  RÉSEAU ROUTIER DU VILLAGE
		// ─────────────────────────────────────────────────────────────────────────
		// Largeur de base d'une ruelle (menant à une simple maison) et bonus de largeur
		// pour une route "principale" (celle qui part directement de la place centrale) :
		// donne une hiérarchie visuelle place -> avenue -> ruelles, au lieu d'un réseau
		// uniforme. Facilement réglable ici sans toucher à l'algorithme.
		private const int MAIN_ROAD_WIDTH_BONUS = 1;

		// Distance (en tuiles) au-delà de laquelle on tente une reconnexion "de secours"
		// avec des contraintes assouplies (mouvements en diagonale, budget de recherche
		// nettement plus grand) plutôt que d'abandonner une maison isolée.
		private const int PATHFINDING_RESCUE_MAX_NODES = 60000;

		/// <summary>
		/// Relie chaque porte de maison (et, si fourni, le centre du village) par un
		/// réseau de chemins. Contrairement à l'ancienne version, un nœud n'est marqué
		/// "connecté" qu'une fois qu'un chemin a RÉELLEMENT été tracé jusqu'à lui : avant,
		/// l'arbre couvrant minimal (MST) choisissait l'arête la plus courte et marquait
		/// aussitôt la maison comme reliée, même si le pathfinding échouait ensuite
		/// (bâtiment qui bloque le seul passage direct, etc.) — la maison restait alors
		/// silencieusement orpheline, sans aucune route jusqu'à sa porte. Désormais, tant
		/// qu'il reste une maison non physiquement reliée, on retente une autre arête, puis,
		/// en dernier recours, une reconnexion assouplie (diagonales autorisées, recherche
		/// bien plus large) : toutes les maisons finissent connectées, sauf cas
		/// géométriquement impossible (alors journalisé clairement).
		private static void DrawPathsBetweenDoors(List<(int x, int y)> doors, int pathWidth = 1, (int x, int y)? villageCenter = null)
		{
			if (doors.Count < 1) return;

			var allNodes = new List<(int x, int y)>(doors);
			int rootIndex = 0;
			if (villageCenter.HasValue)
			{
				int existingIndex = allNodes.IndexOf(villageCenter.Value);
				if (existingIndex >= 0)
					rootIndex = existingIndex;
				else
				{
					allNodes.Insert(0, villageCenter.Value);
					rootIndex = 0;
				}
			}
			else if (allNodes.Count == 1)
			{
				return; // Une seule maison et pas de puits : rien à relier.
			}

			if (allNodes.Count < 2) return;

			var connected = new HashSet<int> { rootIndex };
			var pending = new HashSet<int>(Enumerable.Range(0, allNodes.Count));
			pending.Remove(rootIndex);
			var failedPairs = new HashSet<(int from, int to)>();

			// Profondeur (en arêtes) depuis la racine : la première arête qui sort de la
			// place centrale est traitée comme "route principale" (plus large), tout le
			// reste comme ruelle secondaire.
			var depthFromRoot = new Dictionary<int, int> { [rootIndex] = 0 };

			while (pending.Count > 0)
			{
				int bestFrom = -1, bestTo = -1, bestDist = int.MaxValue;
				foreach (int i in connected)
				{
					foreach (int j in pending)
					{
						if (failedPairs.Contains((i, j))) continue;
						int dist = Math.Abs(allNodes[i].x - allNodes[j].x) + Math.Abs(allNodes[i].y - allNodes[j].y);
						if (dist < bestDist)
						{
							bestDist = dist;
							bestFrom = i;
							bestTo = j;
						}
					}
				}

				if (bestFrom == -1)
				{
					// Toutes les arêtes candidates restantes ont déjà échoué au moins une
					// fois : on ne tourne pas en rond indéfiniment, on tente une dernière
					// reconnexion assouplie pour chaque maison encore isolée, puis on
					// referme la boucle (avec ou sans succès, journalisé si échec).
					foreach (int j in pending.ToList())
					{
						int nearestConnected = -1, nearestDist = int.MaxValue;
						foreach (int i in connected)
						{
							int dist = Math.Abs(allNodes[i].x - allNodes[j].x) + Math.Abs(allNodes[i].y - allNodes[j].y);
							if (dist < nearestDist) { nearestDist = dist; nearestConnected = i; }
						}
						if (nearestConnected == -1) { pending.Remove(j); continue; }

						var rescuePath = FindPathBetweenDoors(allNodes[nearestConnected], allNodes[j], PATHFINDING_RESCUE_MAX_NODES, allowDiagonals: true);
						if (rescuePath != null && rescuePath.Count > 0)
						{
							int depth = depthFromRoot.GetValueOrDefault(nearestConnected, 0) + 1;
							depthFromRoot[j] = depth;
							PlacePathAlong(rescuePath, GetRoadWidthForDepth(pathWidth, depth));
							connected.Add(j);
						}
						else
						{
							Console.WriteLine($"[Village] Impossible de relier une maison en ({allNodes[j].x},{allNodes[j].y}) au réseau routier (terrain géométriquement bloqué).");
						}
						pending.Remove(j);
					}
					continue;
				}

				var path = FindPathBetweenDoors(allNodes[bestFrom], allNodes[bestTo], 20000);
				if (path != null && path.Count > 0)
				{
					int depth = depthFromRoot.GetValueOrDefault(bestFrom, 0) + 1;
					depthFromRoot[bestTo] = depth;
					PlacePathAlong(path, GetRoadWidthForDepth(pathWidth, depth));
					connected.Add(bestTo);
					pending.Remove(bestTo);
				}
				else
				{
					// On ne marque PAS bestTo comme connecté : on retentera une autre
					// arête vers lui au prochain tour de boucle (c'est précisément le
					// correctif du bug de connexion silencieuse).
					failedPairs.Add((bestFrom, bestTo));
				}
			}
		}

		// Route principale (sort directement de la place centrale) plus large qu'une
		// simple ruelle desservant une maison en bout de réseau.
		private static int GetRoadWidthForDepth(int baseWidth, int depth)
		{
			return depth <= 1 ? baseWidth + MAIN_ROAD_WIDTH_BONUS : Math.Max(1, baseWidth);
		}

		private static List<(int x, int y)>? FindPathBetweenDoors((int x, int y) start, (int x, int y) goal, int maxNodes, bool allowDiagonals = false)
		{
			var open = new PriorityQueue<(int x, int y), int>();
			var cameFrom = new Dictionary<(int, int), (int, int)>();
			var gScore = new Dictionary<(int, int), int> { [start] = 0 };
			open.Enqueue(start, Heuristic(start, goal));
			var closed = new HashSet<(int, int)>();
			int nodes = 0;

			while (open.Count > 0 && nodes < maxNodes)
			{
				nodes++;
				var current = open.Dequeue();
				if (current == goal)
					return ReconstructPath(cameFrom, current);

				if (!closed.Add(current))
					continue; // déjà traité via une meilleure priorité entre-temps

				foreach (var (neighbor, stepCost) in GetNeighborsWithCost(current, allowDiagonals))
				{
					if (closed.Contains(neighbor))
						continue;
					if (!IsPathTileValid(neighbor, start, goal))
						continue;

					int tentativeG = gScore[current] + stepCost;
					if (!gScore.TryGetValue(neighbor, out var existingG) || tentativeG < existingG)
					{
						cameFrom[neighbor] = current;
						gScore[neighbor] = tentativeG;
						int tentativeF = tentativeG + Heuristic(neighbor, goal);
						open.Enqueue(neighbor, tentativeF);
					}
				}
			}

			// Si la recherche stricte (4 directions) échoue franchement, on retente une
			// fois en autorisant les diagonales : un simple décalage en biais entre deux
			// bâtiments suffit parfois à bloquer un trajet en croix alors qu'une route
			// existe clairement à l'œil. Évite de dépendre uniquement du rescue final.
			if (!allowDiagonals)
				return FindPathBetweenDoors(start, goal, maxNodes, allowDiagonals: true);

			return null;
		}

		private static int Heuristic((int x, int y) a, (int x, int y) b)
		{
			// Coût de base d'un pas = 10 (voir GetNeighborsWithCost) : l'heuristique
			// (distance de Manhattan à la même échelle) reste admissible même avec le
			// léger bruit et le rabais de coût sur route existante appliqués ensuite.
			return (Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y)) * 10;
		}

		private static List<(int x, int y)> ReconstructPath(Dictionary<(int, int), (int, int)> cameFrom, (int x, int y) current)
		{
			var path = new List<(int x, int y)> { current };
			while (cameFrom.TryGetValue(current, out var previous))
			{
				current = previous;
				path.Add(current);
			}
			path.Reverse();
			return path;
		}

		private static IEnumerable<((int x, int y) pos, int cost)> GetNeighborsWithCost((int x, int y) position, bool allowDiagonals)
		{
			yield return ((position.x + 1, position.y), StepCost(position.x + 1, position.y, 10));
			yield return ((position.x - 1, position.y), StepCost(position.x - 1, position.y, 10));
			yield return ((position.x, position.y + 1), StepCost(position.x, position.y + 1, 10));
			yield return ((position.x, position.y - 1), StepCost(position.x, position.y - 1, 10));

			if (!allowDiagonals) yield break;

			// Diagonales : uniquement utilisées en secours (voir FindPathBetweenDoors) pour
			// débloquer un tracé géométriquement coincé ; coût ~ racine(2)*10, arrondi.
			yield return ((position.x + 1, position.y + 1), StepCost(position.x + 1, position.y + 1, 14));
			yield return ((position.x - 1, position.y + 1), StepCost(position.x - 1, position.y + 1, 14));
			yield return ((position.x + 1, position.y - 1), StepCost(position.x + 1, position.y - 1, 14));
			yield return ((position.x - 1, position.y - 1), StepCost(position.x - 1, position.y - 1, 14));
		}

		// Coût d'un pas vers (x,y) : légèrement bruité (déterministe, basé sur la seed du
		// monde) pour casser l'aspect "tiré au cordeau" d'un simple A* en croix, MAIS moins
		// cher si la tuile est déjà une route/place existante — ce qui pousse naturellement
		// les nouvelles ruelles à converger vers le réseau déjà tracé plutôt que dessiner
		// plusieurs chemins parallèles quasi identiques à quelques tuiles d'écart.
		private static int StepCost(int x, int y, int baseCost)
		{
			if (GetGroundTileIdAt(x, y) == 83)
				return Math.Max(2, baseCost / 3);

			uint h = (uint)(Program.WorldSeed ^ (x * 374761393) ^ (y * 668265263));
			h = (h ^ (h >> 13)) * 1274126177u;
			int jitter = (int)(h % 5); // 0..4 : reste toujours positif, heuristique non affectée
			return baseCost + jitter;
		}

		private static bool IsPathTileValid((int x, int y) tile, (int x, int y) start, (int x, int y) goal)
		{
			int objectId = GetObjectIdAt(tile.x, tile.y);
			if (objectId != 0 && !(tile == start) && !(tile == goal) && objectId != 84)
				return false;

			int buildingId = GetBuildingIdAt(tile.x, tile.y);
			if (buildingId != 0 && tile != start && tile != goal)
				return false;

			return true;
		}

		private static void PlacePathAlong(List<(int x, int y)> path, int pathWidth)
		{
			foreach (var (x, y) in path)
			{
				PlacePathTile(x, y, pathWidth);
			}
		}

		public static (int x, int y) GetCaveExitForEntry(int entryX, int entryY)
        {
            if (_caveEntryPairs.TryGetValue((entryX, entryY), out var exit))
                return exit;
            return (-1, -1);
        }

		public static (int x, int y) GetSurfaceEntryForExit(int exitX, int exitY)
        {
            foreach (var pair in _caveEntryPairs)
            {
                if (pair.Value.caveX == exitX && pair.Value.caveY == exitY)
                    return pair.Key;
            }
            return (-1, -1);
        }

		public static bool HasCaveExit(int entryX, int entryY)
        {
            if (_caveEntryPairs.TryGetValue((entryX, entryY), out var exit))
                return exit.caveX != -1 && exit.caveY != -1;
            return false;
        }

		// Vérifie si une tuile est connectable ET du même type (même ID)
		private static bool IsConnectableSameType(int x, int y, int targetId)
		{
			int objId = GetObjectIdAt(x, y);
			return objId != 0 && objId == targetId && WorldTileRegistry.IsConnectable(objId);
		}

		public static void SaveCavePairs()
        {
            if (string.IsNullOrEmpty(CurrentSaveName)) return;
            SaveSystem.SaveCavePairs(CurrentSaveName, _caveEntryPairs);
        }

		public static void LoadCavePairs()
        {
            if (string.IsNullOrEmpty(CurrentSaveName)) return;
            var loadedPairs = SaveSystem.LoadCavePairs(CurrentSaveName);
            foreach (var pair in loadedPairs)
                _caveEntryPairs[pair.Key] = pair.Value;
        }

		private static Dictionary<(int x, int y), bool> _caveEntries = new();

		public static bool IsCaveEntry(int tileX, int tileY)
        {
            return GetObjectIdAt(tileX, tileY) == 50;
        }

		public static (int x, int y) FindNearestCaveEntry(Vector2 worldPos)
        {
            int centerX = (int)(worldPos.X / Program.TileSize);
            int centerY = (int)(worldPos.Y / Program.TileSize);
            int range = 20;
            (int bestX, int bestY) = (-1, -1);
            float bestDist = float.MaxValue;
            for (int dx = -range; dx <= range; dx++)
                for (int dy = -range; dy <= range; dy++)
                {
                    int x = centerX + dx;
                    int y = centerY + dy;
                    if (GetObjectIdAt(x, y) == 50)
                    {
                        float dist = Vector2.Distance(worldPos, new Vector2(x * Program.TileSize, y * Program.TileSize));
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestX = x;
                            bestY = y;
                        }
                    }
                }
            return (bestX, bestY);
        }

		// ═══════════════════════════════════════════════════════════════════
        // DONJONS — API publique
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Vrai si la tuile (dans les grottes) est une entrée de donjon.</summary>
        public static bool IsDungeonEntrance(int tileX, int tileY)
        {
            return _dungeonPortals.TryGetValue((tileX, tileY), out var info) && info.Kind == DungeonPortalKind.Entrance;
        }

		/// <summary>Vrai si la tuile est le portail de retour apparu après la mort du boss.</summary>
        public static bool IsDungeonReturnPortal(int tileX, int tileY)
        {
            return _dungeonPortals.TryGetValue((tileX, tileY), out var info) && info.Kind == DungeonPortalKind.ReturnExit;
        }

		private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

		/// <summary>Enregistre le boss d'un donjon pour que sa mort soit détectée automatiquement (voir CheckDungeonBossDeaths).</summary>
        public static void RegisterDungeonBoss(Entity boss, int instanceId)
        {
            _dungeonBosses[boss] = instanceId;
        }

		public static void SaveDungeons()
        {
            if (string.IsNullOrEmpty(CurrentSaveName)) return;
            SaveSystem.SaveDungeons(CurrentSaveName, _dungeonInstances, _dungeonPortals, _nextDungeonInstanceId);
        }

		public static void LoadDungeons()
        {
            if (string.IsNullOrEmpty(CurrentSaveName)) return;
            var (instances, portals, nextId) = SaveSystem.LoadDungeons(CurrentSaveName);
            _dungeonInstances = instances;
            _dungeonPortals = portals;
            _nextDungeonInstanceId = nextId;
        }

		// ─── PORTAILS DE TÉLÉPORTATION ───────────────────────────────────

        /// <summary>Un portail a-t-il déjà été découvert à cette position ?</summary>
        public static bool IsPortalDiscovered(int tileX, int tileY, bool isCave)
            => _knownPortals.ContainsKey((tileX, tileY, isCave));

		/// <summary>
        /// Découvre (ou redécouvre sans effet) un portail à cette position et l'ajoute au
        /// registre persistant. Retourne l'entrée nouvellement créée, ou l'existante.
        /// </summary>
        public static PortalInfo DiscoverPortal(int tileX, int tileY, bool isCave, double gameTime)
        {
            var key = (tileX, tileY, isCave);
            if (_knownPortals.TryGetValue(key, out var existing))
                return existing;

            var info = new PortalInfo
            {
                TileX = tileX,
                TileY = tileY,
                IsCave = isCave,
                DiscoveredAt = gameTime
            };
            _knownPortals[key] = info;
            return info;
        }

		public static List<PortalInfo> GetKnownPortals() => _knownPortals.Values.ToList();

		public static bool RenamePortal(int tileX, int tileY, bool isCave, string newName)
        {
            if (_knownPortals.TryGetValue((tileX, tileY, isCave), out var info))
            {
                info.Name = newName?.Trim() ?? "";
                return true;
            }
            return false;
        }

		public static bool SetPortalFavorite(int tileX, int tileY, bool isCave, bool favorite)
        {
            if (_knownPortals.TryGetValue((tileX, tileY, isCave), out var info))
            {
                info.IsFavorite = favorite;
                return true;
            }
            return false;
        }

		/// <summary>Oublie un portail (par ex. lorsqu'il est détruit). Sans effet s'il est inconnu.</summary>
        public static bool ForgetPortal(int tileX, int tileY, bool isCave)
            => _knownPortals.Remove((tileX, tileY, isCave));

		public static void SavePortals()
        {
            if (string.IsNullOrEmpty(CurrentSaveName)) return;
            SaveSystem.SavePortals(CurrentSaveName, _knownPortals);
        }

		public static void LoadPortals()
        {
            if (string.IsNullOrEmpty(CurrentSaveName)) return;
            _knownPortals = SaveSystem.LoadPortals(CurrentSaveName);
        }

		private static Color GetImageColorSafe(Image img, int x, int y)
        {
            if (x < 0 || x >= img.Width || y < 0 || y >= img.Height)
                return new Color(0, 0, 0, 0);
            return Raylib.GetImageColor(img, x, y);
        }

		public static Color GetParticleColorForGround(int groundId)
		{
			var tile = WorldTileRegistry.GetTile(groundId);
			if (tile == null) return new Color(120, 100, 80, 200);
			string name = tile.Name.ToLower();
			if (name.Contains("grass")) return new Color(80, 140, 60, 180);
			if (name.Contains("sand")) return new Color(220, 200, 120, 180);
			if (name.Contains("snow")) return new Color(240, 240, 255, 200);
			if (name.Contains("dirt")) return new Color(140, 100, 60, 180);
			if (name.Contains("stone") || name.Contains("rock")) return new Color(120, 110, 100, 180);
			if (name.Contains("mud")) return new Color(100, 80, 50, 180);
			return new Color(120, 100, 80, 180);
		}

		/// <summary>
		/// Retourne une case libre proche de la tente la plus proche dans le plan courant.
		/// Utilise uniquement les objets charges : les PNJ ne s'approprient donc pas une
		/// tente situee dans un autre plan ou dans une zone encore inconnue.
		/// </summary>
		public static Vector2? FindNearestTentHome(Vector2 worldPos, int maxDistanceTiles)
		{
			int centerX = (int)MathF.Floor(worldPos.X / Program.TileSize);
			int centerY = (int)MathF.Floor(worldPos.Y / Program.TileSize);
			float maxDistance = maxDistanceTiles * Program.TileSize;
			float bestDistanceSq = maxDistance * maxDistance;
			Vector2? bestPosition = null;

			for (int x = centerX - maxDistanceTiles; x <= centerX + maxDistanceTiles; x++)
			{
				for (int y = centerY - maxDistanceTiles; y <= centerY + maxDistanceTiles; y++)
				{
					if (GetObjectIdAt(x, y) != 6001)
						continue;

					var tentData = WorldTileRegistry.GetTile(6001);
					if (tentData == null)
						continue;

					Vector2 tentPosition = Program.GetFurnitureAnchorPosition(x, y, tentData);
					float distanceSq = Vector2.DistanceSquared(worldPos, tentPosition);
					if (distanceSq < bestDistanceSq)
					{
						Vector2? nearestRestPosition = null;
						float nearestRestDistanceSq = float.MaxValue;
						for (int offsetX = -3; offsetX <= 3; offsetX++)
						{
							for (int offsetY = -3; offsetY <= 3; offsetY++)
							{
								int restX = x + offsetX;
								int restY = y + offsetY;
								if (GetObjectIdAt(restX, restY) != 0)
									continue;

								var ground = WorldTileRegistry.GetTile(GetGroundTileIdAt(restX, restY));
								if (ground == null || !ground.Walkable)
									continue;

								Vector2 candidate = new Vector2(
									(restX + 0.5f) * Program.TileSize,
									(restY + 0.5f) * Program.TileSize);
								if (IsCollidingEntity(candidate, Program.GetDestroyedObjects(), "human"))
									continue;

								float restDistanceSq = Vector2.DistanceSquared(candidate, tentPosition);
								if (restDistanceSq < nearestRestDistanceSq)
								{
									nearestRestDistanceSq = restDistanceSq;
									nearestRestPosition = candidate;
								}
							}
						}

						if (nearestRestPosition.HasValue)
						{
							bestDistanceSq = distanceSq;
							bestPosition = nearestRestPosition.Value;
						}
					}
				}
			}

			return bestPosition;
		}

		public static Vector2? FindNearestTentSleepPosition(Vector2 worldPos, int maxDistanceTiles)
		{
			int centerX = (int)MathF.Floor(worldPos.X / Program.TileSize);
			int centerY = (int)MathF.Floor(worldPos.Y / Program.TileSize);
			float maxDistance = maxDistanceTiles * Program.TileSize;
			float bestDistanceSq = maxDistance * maxDistance;
			Vector2? bestPosition = null;

			for (int x = centerX - maxDistanceTiles; x <= centerX + maxDistanceTiles; x++)
			{
				for (int y = centerY - maxDistanceTiles; y <= centerY + maxDistanceTiles; y++)
				{
					if (GetObjectIdAt(x, y) != 6001)
						continue;

					var tentData = WorldTileRegistry.GetTile(6001);
					if (tentData == null)
						continue;

					Vector2 tentPosition = Program.GetFurnitureAnchorPosition(x, y, tentData);
					float distanceSq = Vector2.DistanceSquared(worldPos, tentPosition);
					if (distanceSq < bestDistanceSq)
					{
						bestDistanceSq = distanceSq;
						bestPosition = tentPosition;
					}
				}
			}

			return bestPosition;
		}

		public static Vector2? FindNearestAvailableTentSleepPosition(Vector2 worldPos, int maxDistanceTiles, Entity requester)
		{
			int centerX = (int)MathF.Floor(worldPos.X / Program.TileSize);
			int centerY = (int)MathF.Floor(worldPos.Y / Program.TileSize);
			float maxDistance = maxDistanceTiles * Program.TileSize;
			float bestDistanceSq = maxDistance * maxDistance;
			Vector2? bestPosition = null;

			for (int x = centerX - maxDistanceTiles; x <= centerX + maxDistanceTiles; x++)
			{
				for (int y = centerY - maxDistanceTiles; y <= centerY + maxDistanceTiles; y++)
				{
					if (GetObjectIdAt(x, y) != 6001)
						continue;

					var tentData = WorldTileRegistry.GetTile(6001);
					if (tentData == null)
						continue;

					Vector2 tentPosition = Program.GetFurnitureAnchorPosition(x, y, tentData);
					if (Program.IsSleepPositionOccupied(tentPosition, requester))
						continue;

					float distanceSq = Vector2.DistanceSquared(worldPos, tentPosition);
					if (distanceSq < bestDistanceSq)
					{
						bestDistanceSq = distanceSq;
						bestPosition = tentPosition;
					}
				}
			}

			return bestPosition;
		}

		//  Identifiants des objets "arbre" pouvant faire tomber des feuilles (chêne, sapin,
		// bouleau, palmier...). Utilisé à la fois pour la chute ambiante et pour les
		// rafales de feuilles déclenchées par un coup de hache.
		private static readonly HashSet<int> _leafBearingTreeIds = new HashSet<int> { 4, 45, 48, 49 };

		//  Cache des positions de pixels non-transparents (en coordonnées normalisées [0,1])
		// pour chaque texture d'arbre, afin de ne faire apparaître les feuilles que là où le
		// feuillage est réellement dessiné, et pas dans le vide autour (la texture ne remplit
		// pas toute sa bounding box).
		private static Dictionary<int, List<Vector2>> _treeOpaquePixelsCache = new();

		private static List<Vector2> GetOrGenerateOpaquePixels(Texture2D tex)
		{
			int texId = (int)tex.Id;
			if (_treeOpaquePixelsCache.TryGetValue(texId, out var cached))
				return cached;

			var points = new List<Vector2>();
			if (tex.Id != 0 && tex.Width > 0 && tex.Height > 0)
			{
				Image img = Raylib.LoadImageFromTexture(tex);
				int width = img.Width;
				int height = img.Height;
				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						Color pixel = Raylib.GetImageColor(img, x, y);
						//  On ignore les pixels quasi transparents (anti-aliasing des bords)
						if (pixel.A > 40)
							points.Add(new Vector2((x + 0.5f) / width, (y + 0.5f) / height));
					}
				}
				Raylib.UnloadImage(img);
			}
			_treeOpaquePixelsCache[texId] = points;
			return points;
		}

		public static int GetTileIdAt(float wX, float wY)
        {
            int x = (int)Math.Floor(wX);
            int y = (int)Math.Floor(wY);
            int objectId = GetObjectIdAt(x, y);
            return objectId != 0 ? objectId : GetGroundTileIdAt(wX, wY);
        }

		//  MULTIJOUEUR : version publique avec réplication réseau. Sur un client,
        // on délègue au host et on n'applique rien localement (on attend la confirmation,
        // qui revient via ApplyWorldActionFromNetwork). Sur le host (ou en solo), on
        // applique directement puis on diffuse l'action à tous les clients connectés.
        public static void AddPlacedObject(int x, int y, int itemId)
		{
			if (NetworkManager.IsClient)
			{
				NetworkManager.RequestWorldAction("AddPlacedObject", x, y, itemId, _isUnderground);
				return;
			}
			AddPlacedObject_Apply(x, y, itemId);
			if (NetworkManager.IsHost)
				NetworkManager.BroadcastWorldAction("AddPlacedObject", x, y, itemId, _isUnderground);
		}

		public static void RemovePlacedObject(int x, int y)
		{
			if (GetOverlayAt(x, y) == 123 && GetObjectIdAt(x, y) == 123)
			{
				RemoveOverlayNetworked(x, y);
				return;
			}

			if (NetworkManager.IsClient)
			{
				NetworkManager.RequestWorldAction("RemovePlacedObject", x, y, 0, _isUnderground);
				return;
			}
			RemovePlacedObject_Apply(x, y);
			if (NetworkManager.IsHost)
				NetworkManager.BroadcastWorldAction("RemovePlacedObject", x, y, 0, _isUnderground);
		}

		// ─────────────────────────────────────────────────────────────────────────
        //  COLLISIONS
        // ─────────────────────────────────────────────────────────────────────────
        public static bool IsColliding(Vector2 groundPos, Vector2 dim, HashSet<string> destroyed)
        {
            int tileX = (int)Math.Floor(groundPos.X / Program.TileSize);
            int tileY = (int)Math.Floor(groundPos.Y / Program.TileSize);
            int height = GetHeightAt(tileX, tileY);
            float yOffset = -height * Program.TileSize / 4;
            float playerBaseY = groundPos.Y + yOffset + Program.FeetOffsetY;
            Rectangle playerRect = new Rectangle(groundPos.X - dim.X / 2f, playerBaseY - dim.Y, dim.X, dim.Y);
            return CheckCollision(groundPos, playerRect, destroyed, Program.TileSize);
        }

		private const float MaxEntityPushAcceleration = 1200f;

		public static void ResolveEntityPushes(float dt)
		{
			if (NetworkManager.IsClient) return;

			List<Entity> entities = Program.GetEntities();
			for (int i = 0; i < entities.Count - 1; i++)
			{
				Entity first = entities[i];
				if (!first.IsAlive || first.CarriedByConnectionId != -1) continue;

				for (int j = i + 1; j < entities.Count; j++)
				{
					Entity second = entities[j];
					if (!second.IsAlive || second.CarriedByConnectionId != -1) continue;
					if (first.Rider == second || second.Rider == first) continue;
					ResolveEntityPush(first, second, dt);
				}
			}

			Program.ResolveWagonPushes(dt);

			Entity player = Program.GetPlayerEntity();
			if (player == null) return;
			player.WorldPos = Program.GetPlayerPosition();
			foreach (Entity entity in entities)
			{
				if (!entity.IsAlive || entity.CarriedByConnectionId != -1) continue;
				if (player.Rider == entity || entity.Rider == player) continue;
				ResolveEntityPush(player, entity, dt);
			}
		}

		public static void ApplyPlayerMovementPush(Vector2 playerPosition, Vector2 attemptedMove, float dt)
		{
			if (NetworkManager.IsClient || attemptedMove.LengthSquared() < 0.0001f) return;
			Program.ApplyPlayerWagonPush(playerPosition, attemptedMove, dt);

			Vector2 movementDirection = Vector2.Normalize(attemptedMove);
			float movementSpeed = attemptedMove.Length() / MathF.Max(dt, 0.0001f);
			Rectangle playerBox = GetEntityCollisionHitbox(
				playerPosition + attemptedMove,
				Program.GetPlayerCollisionDimensions(),
				Program.TileSize);

			foreach (Entity entity in Program.GetEntities())
			{
				if (!entity.IsAlive || entity.CarriedByConnectionId != -1) continue;
				if (entity.Rider == Program.GetPlayerEntity() || Program.GetPlayerEntity().Rider == entity) continue;

				Rectangle entityBox = GetPushHitbox(entity);
				if (!Raylib.CheckCollisionRecs(playerBox, entityBox)) continue;

				Vector2 playerCenter = new(playerBox.X + playerBox.Width / 2f, playerBox.Y + playerBox.Height / 2f);
				Vector2 entityCenter = new(entityBox.X + entityBox.Width / 2f, entityBox.Y + entityBox.Height / 2f);
				Vector2 awayFromPlayer = entityCenter - playerCenter;
				if (awayFromPlayer.LengthSquared() < 0.0001f) awayFromPlayer = movementDirection;
				else awayFromPlayer = Vector2.Normalize(awayFromPlayer);

				float incomingSpeed = Vector2.Dot(movementDirection, awayFromPlayer) * movementSpeed;
				if (incomingSpeed <= 0f) continue;

				float overlapX = MathF.Min(playerBox.X + playerBox.Width, entityBox.X + entityBox.Width)
					- MathF.Max(playerBox.X, entityBox.X);
				float overlapY = MathF.Min(playerBox.Y + playerBox.Height, entityBox.Y + entityBox.Height)
					- MathF.Max(playerBox.Y, entityBox.Y);
				float proximity = Math.Clamp(MathF.Min(overlapX, overlapY) / MathF.Min(playerBox.Width, playerBox.Height), 0f, 1f);
				float acceleration = MathF.Min(3000f, incomingSpeed * 5f + proximity * proximity * 1200f);

				entity.ApplyPushImpulse(awayFromPlayer * (acceleration * dt));
			}
		}

		private static void ResolveEntityPush(Entity first, Entity second, float dt)
		{
			Rectangle firstBox = GetPushHitbox(first);
			Rectangle secondBox = GetPushHitbox(second);
			if (!Raylib.CheckCollisionRecs(firstBox, secondBox)) return;

			float overlapX = MathF.Min(firstBox.X + firstBox.Width, secondBox.X + secondBox.Width)
				- MathF.Max(firstBox.X, secondBox.X);
			float overlapY = MathF.Min(firstBox.Y + firstBox.Height, secondBox.Y + secondBox.Height)
				- MathF.Max(firstBox.Y, secondBox.Y);
			if (overlapX <= 0f || overlapY <= 0f) return;

			Vector2 firstCenter = new(firstBox.X + firstBox.Width / 2f, firstBox.Y + firstBox.Height / 2f);
			Vector2 secondCenter = new(secondBox.X + secondBox.Width / 2f, secondBox.Y + secondBox.Height / 2f);
			Vector2 centerDelta = secondCenter - firstCenter;
			float centerDistance = centerDelta.Length();
			Vector2 pushDirection = centerDistance > 0.001f
				? -centerDelta / centerDistance
				: new Vector2(-1f, 0f);
			float supportDistance =
				MathF.Abs(pushDirection.X) * (firstBox.Width + secondBox.Width) / 2f
				+ MathF.Abs(pushDirection.Y) * (firstBox.Height + secondBox.Height) / 2f;
			float proximity = Math.Clamp(1f - centerDistance / MathF.Max(0.001f, supportDistance), 0f, 1f);
			float penetration = MathF.Min(overlapX, overlapY);

			Vector2 separation = pushDirection * MathF.Min(0.5f, penetration * 0.05f);
			ApplyEntitySeparation(first, separation);
			ApplyEntitySeparation(second, -separation);

			float acceleration = proximity * proximity * MaxEntityPushAcceleration;
			Vector2 push = pushDirection * (acceleration * dt * 0.5f);
			if (first.IsPlayer)
				Program.ApplyEntityPush(push);
			else
				first.ApplyPushImpulse(push);
			if (second.IsPlayer)
				Program.ApplyEntityPush(-push);
			else
				second.ApplyPushImpulse(-push);
			first.IsKnockedBack = true;
			second.IsKnockedBack = true;
		}

		private static Rectangle GetPushHitbox(Entity entity)
		{
			Vector2 collisionPosition = entity.VisualWorldPos;
			if (entity.IsPlayer)
				return GetEntityCollisionHitbox(collisionPosition, Program.GetPlayerCollisionDimensions(), Program.TileSize);
			return GetEntityCollisionHitbox(collisionPosition, entity.Species, Program.TileSize);
		}

		private static void ApplyEntitySeparation(Entity entity, Vector2 separation)
		{
			if (entity.IsPlayer)
			{
				Program.ApplyEntityPush(separation);
				entity.WorldPos = Program.GetPlayerPosition();
				return;
			}

			Vector2 xPosition = entity.WorldPos + new Vector2(separation.X, 0f);
			if (!IsCollidingEntity(xPosition, Program.GetDestroyedObjects(), entity.Species))
				entity.WorldPos.X = xPosition.X;

			Vector2 yPosition = entity.WorldPos + new Vector2(0f, separation.Y);
			if (!IsCollidingEntity(yPosition, Program.GetDestroyedObjects(), entity.Species))
				entity.WorldPos.Y = yPosition.Y;
		}

		public static Vector2 GetEntityDimensions(string species)
        {
            return species switch
            {
                "human" => new Vector2(Program.TileSize * 0.8f, Program.TileSize * 0.4f),
                "pig" => new Vector2(Program.TileSize * 1.0f, Program.TileSize * 0.5f),
                "sheep" => new Vector2(Program.TileSize * 0.9f, Program.TileSize * 0.5f),
                "cow" => new Vector2(Program.TileSize * 1.2f, Program.TileSize * 0.6f),
                "chicken" => new Vector2(Program.TileSize * 0.6f, Program.TileSize * 0.4f),
                "duck" => new Vector2(Program.TileSize * 0.6f, Program.TileSize * 0.4f),
                "wolf" => new Vector2(Program.TileSize * 0.8f, Program.TileSize * 0.5f),
                "bear" => new Vector2(Program.TileSize * 1.1f, Program.TileSize * 0.6f),
                "goat" => new Vector2(Program.TileSize * 0.8f, Program.TileSize * 0.5f),
                "horse" => new Vector2(Program.TileSize * 1.4f, Program.TileSize * 0.7f),
                "bat" => new Vector2(Program.TileSize * 0.7f, Program.TileSize * 0.4f),
                _ => new Vector2(Program.TileSize * 0.8f, Program.TileSize * 0.4f)
            };
        }

		public static (int x, int y, int w, int h) GetCollisionRect(int tileX, int tileY, WorldTileData tileData, int ts)
		{
			// Ancrage par défaut : la tuile (tileX, tileY) correspond au coin BAS-GAUCHE
			// de la texture/hitbox, exactement comme le rendu visuel (voir par ex. le
			// calcul de drawX/drawY dans DrawWorld : drawX = pos.x*ts, drawY = baseY+ts-drawH).
			// Sans ça, un objet de plusieurs tuiles de large/haut se retrouvait avec une
			// hitbox centrée sur une seule tuile, décalée par rapport à la texture — d'où
			// les CollisionOffset "bricolés" (+20, +40...) dans worldobjects.json pour
			// compenser. Avec cet ancrage commun, plus besoin de ces offsets par défaut ;
			// CollisionOffset ne sert plus qu'à un réglage fin optionnel.
			int height = GetHeightAt(tileX, tileY);
			int baseY = tileY * ts - (height * ts / 4);  // ← doit être int
			int w = ts, h = ts;
			int x = tileX * ts;
			int y = baseY;
			
			if (tileData.CollisionSize != null)
			{
				w = ts * tileData.CollisionSize.Width;
				h = ts * tileData.CollisionSize.Height;
				
				// Coin bas-gauche de la tuile d'ancrage = coin bas-gauche de la hitbox,
				// quelle que soit la largeur/hauteur (même convention que Size ci-dessous).
				x = tileX * ts;
				y = baseY + ts - h;
				
				int offsetX = tileData.CollisionOffset?.X ?? 0;
				int offsetY = tileData.CollisionOffset?.Y ?? 0;
				x += offsetX;
				y -= offsetY; // même convention de signe que DrawOffset (positif = vers le haut)
			}
			else if (tileData.Size != null)
			{
				w = ts * tileData.Size.Width;
				h = ts * tileData.Size.Height;
				// Coin bas-gauche de la tuile d'ancrage = coin bas-gauche de la texture.
				x = tileX * ts;
				y = baseY + ts - h;
				if (tileData.DrawOffset != null)
				{
					x += tileData.DrawOffset.X;
					y -= tileData.DrawOffset.Y;
				}
			}
			return (x, y, w, h);
		}

		public static bool IsCollidingCar(Vector2 pos, float angle, float width, float height, HashSet<string> destroyed)
        {
            int ts = Program.TileSize;
            float rad = angle * MathF.PI / 180f;
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);
            Vector2 half = new Vector2(width / 2, height / 2);
            Vector2[] corners = new Vector2[4];
            corners[0] = new Vector2(-half.X, -half.Y);
            corners[1] = new Vector2( half.X, -half.Y);
            corners[2] = new Vector2( half.X,  half.Y);
            corners[3] = new Vector2(-half.X,  half.Y);
            for (int i = 0; i < 4; i++)
            {
                float x = corners[i].X * cos - corners[i].Y * sin;
                float y = corners[i].X * sin + corners[i].Y * cos;
                corners[i] = new Vector2(pos.X + x, pos.Y + y);
            }
            float minX = corners.Min(c => c.X);
            float maxX = corners.Max(c => c.X);
            float minY = corners.Min(c => c.Y);
            float maxY = corners.Max(c => c.Y);
            int startX = (int)Math.Floor(minX / ts);
            int endX   = (int)Math.Ceiling(maxX / ts);
            int startY = (int)Math.Floor(minY / ts);
            int endY   = (int)Math.Ceiling(maxY / ts);
            for (int x = startX; x <= endX; x++)
            for (int y = startY; y <= endY; y++)
            {
                string key = $"{x}_{y}";
                if (destroyed.Contains(key)) continue;
                int tileId = GetTileIdAt(x, y);
                var tileData = WorldTileRegistry.GetTile(tileId);
                if (tileData == null || tileData.Walkable) continue;
                int heightTile = GetHeightAt(x, y);
                int drawY = y * ts - (heightTile * ts / 4);
                Rectangle tileRect = new Rectangle(x * ts, drawY, ts, ts);
                if (IntersectOrientedRectangleWithAABB(corners, tileRect))
                    return true;
            }
            return false;
        }

		public static void SetBuildingIdForTile(int x, int y, int buildingId)
		{
			BuildingIds[(x, y)] = buildingId;
		}

		private static bool IntersectOrientedRectangleWithAABB(Vector2[] corners, Rectangle aabb)
        {
            foreach (var c in corners)
                if (c.X >= aabb.X && c.X <= aabb.X + aabb.Width &&
                    c.Y >= aabb.Y && c.Y <= aabb.Y + aabb.Height)
                    return true;
            Vector2[] aabbCorners = new Vector2[]
            {
                new Vector2(aabb.X, aabb.Y),
                new Vector2(aabb.X + aabb.Width, aabb.Y),
                new Vector2(aabb.X + aabb.Width, aabb.Y + aabb.Height),
                new Vector2(aabb.X, aabb.Y + aabb.Height)
            };
            foreach (var c in aabbCorners)
                if (IsPointInOrientedRectangle(c, corners))
                    return true;
            float carMinX = corners.Min(c => c.X);
            float carMaxX = corners.Max(c => c.X);
            float carMinY = corners.Min(c => c.Y);
            float carMaxY = corners.Max(c => c.Y);
            if (carMaxX < aabb.X || carMinX > aabb.X + aabb.Width ||
                carMaxY < aabb.Y || carMinY > aabb.Y + aabb.Height)
                return false;
            return true;
        }

		private static bool IsPointInOrientedRectangle(Vector2 p, Vector2[] corners)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = corners[i];
                Vector2 b = corners[(i + 1) % 4];
                Vector2 ab = b - a;
                Vector2 ap = p - a;
                float cross = ab.X * ap.Y - ab.Y * ap.X;
                if (cross < 0) return false;
            }
            return true;
        }

		// ─────────────────────────────────────────────────────────────────────────
        //  TEXTURES (variations / animations)
        // ─────────────────────────────────────────────────────────────────────────
        private static int GetVariationForTile(int x, int y, int variationCount)
        {
            var key = (x, y);
            if (!_tileVariationCache.ContainsKey(key))
            {
                int hash = (x * 73856093) ^ (y * 19349663);
                int variation = Math.Abs(hash) % variationCount;
                _tileVariationCache[key] = variation;
            }
            return _tileVariationCache[key];
        }

		private static int GetAnimationFrameForTile(int x, int y, int frameCount, float currentTime)
        {
            var key = (x, y);
            if (!_tileAnimationStartCache.ContainsKey(key))
            {
                int hash = (x * 73856093) ^ (y * 19349663);
                int startAnimFrame = Math.Abs(hash) % frameCount;
                _tileAnimationStartCache[key] = startAnimFrame;
            }
            float speed = 0.15f;
            int cachedStartFrame = _tileAnimationStartCache[key];
            int currentFrame = (cachedStartFrame + (int)(currentTime / speed)) % frameCount;
            return currentFrame;
        }

		// ─────────────────────────────────────────────────────────────────────────
        //  DESSIN DES FALAISES
        // ─────────────────────────────────────────────────────────────────────────
        private static void DrawCliffDown(int x, int y, int heightDiff, int ts, Texture2D cliffDownTex)
        {
            if (heightDiff <= 0) return;
            if (cliffDownTex.Id == 0) return;
            int baseX = x * ts;
            int baseY = y * ts - (GetHeightAt(x, y) * ts / 4);
            int cliffHeight = heightDiff * (ts / 4);
            Rectangle src = new Rectangle(0, 0, cliffDownTex.Width, cliffDownTex.Height);
            Rectangle dest = new Rectangle(baseX, baseY + ts, ts, cliffHeight);
            Raylib.DrawTexturePro(cliffDownTex, src, dest, Vector2.Zero, 0, Color.White);
        }

		public static Rectangle GetEntityHitbox(Entity entity, int ts)
        {
			return GetEntityCollisionHitbox(entity.VisualWorldPos, entity.Species, ts);
        }

		// Hitbox de collision physique avec les tuiles et les obstacles : centrée sur les pieds.
        public static Rectangle GetEntityCollisionHitbox(Vector2 worldPos, string species, int ts)
        {
			return GetEntityCollisionHitbox(worldPos, GetEntityDimensions(species), ts);
		}

		public static Rectangle GetEntityCollisionHitbox(Vector2 worldPos, Vector2 dim, int ts)
		{
            int tileX = (int)(worldPos.X / ts);
            int tileY = (int)(worldPos.Y / ts);
            int height = GetHeightAt(tileX, tileY);
            float yOffset = -height * ts / 4;
            float entityBaseY = worldPos.Y + yOffset + Program.FeetOffsetY;
            return new Rectangle(
                worldPos.X - dim.X / 2f,
                entityBaseY - dim.Y,
                dim.X,
                dim.Y
            );
        }

		// Hitbox de dégâts : plus grande, couvrant le corps de l'entité pour les attaques et les impacts.
        public static Rectangle GetEntityDamageHitbox(Entity entity, int ts)
        {
			return GetEntityDamageHitbox(entity.VisualWorldPos, entity.Species, ts, entity.Scale);
        }

		//  MULTIJOUEUR : variante utilisable côté client, où l'on n'a que la position/espèce
        // reçue via un EntityDto (snapshot réseau) et pas d'objet Entity complet.
        public static Rectangle GetEntityDamageHitbox(Vector2 worldPos, string species, int ts)
		{
			return GetEntityDamageHitbox(worldPos, species, ts, 1f);
		}

		public static Rectangle GetEntityDamageHitbox(Vector2 worldPos, string species, int ts, float scale)
        {
            int tileX = (int)(worldPos.X / ts);
            int tileY = (int)(worldPos.Y / ts);
            int height = GetHeightAt(tileX, tileY);
            float yOffset = -height * ts / 4;
			Vector2 dim = GetEntityDimensions(species) * Math.Clamp(scale, 0.25f, 4f);
			Vector2 damageDim = new Vector2(dim.X * 2f, dim.Y * 5.5f);
            float entityBaseY = worldPos.Y + yOffset + Program.FeetOffsetY;
            return new Rectangle(
                worldPos.X - damageDim.X / 2f,
                entityBaseY - damageDim.Y,
                damageDim.X,
                damageDim.Y
            );
        }

		public static float GetDistanceToDamageHitbox(Vector2 point, Rectangle damageHitbox)
		{
			float closestX = Math.Clamp(point.X, damageHitbox.X, damageHitbox.X + damageHitbox.Width);
			float closestY = Math.Clamp(point.Y, damageHitbox.Y, damageHitbox.Y + damageHitbox.Height);
			return Vector2.Distance(point, new Vector2(closestX, closestY));
		}

		private static bool IsConnectableAt(int x, int y)
        {
            int objId = GetObjectIdAt(x, y);
            return objId != 0 && WorldTileRegistry.IsConnectable(objId);
        }
    }
}
