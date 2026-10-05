// Program.Electricity.cs
#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Soulfract.WorldGeneration;
using DiscordRPC;

namespace Soulfract
{
    public static partial class Program
    {

		// 120 pixels
		
		private static (int x, int y, SteamEngineData engine)? _hoveredMachine = null;

		private static (int x, int y, RainCollectorData collector)? _hoveredRainCollector = null;

		private static (int x, int y, BatteryChargerData charger)? _hoveredBatteryCharger = null;

		// Mode de connexion des fils électriques
		private static bool _isConnectingWire = false;

		private static (int x, int y) _wireStartTile = (-1, -1);

		private static DynamoData? _wireStartDynamo = null;

		private static BatteryData? _wireStartBattery = null;

		private static LogicGateData? _wireStartGate = null;

		//  Cache pour l'équipement complet (toutes zones + teintes) des joueurs distants,
        // indexé par connectionId. La "signature" (JSON) sert à détecter un changement
        // sans reconstruire (et recharger les textures) à chaque frame.
        static Dictionary<int, (string signature, Equipment eq)> _networkFullEquipmentCache = new();

		//  ENDURANCE : se vide quand le joueur court (sprint), et une fois
        // complètement vidée elle doit intégralement se recharger avant de pouvoir
        // resprinter (verrouillage "staminaLocked").
        public static float playerStamina = 100f;

		//  MULTIJOUEUR : identifiant stable de CE joueur, généré une seule fois et
		// conservé sur sa machine (fichier "player_id.txt"), pour que le host puisse le
		// reconnaître d'une connexion à l'autre même s'il change de pseudo.
		private static string? _localPlayerGuid = null;

		//  CACHE DES APERÇUS DU CARROUSEL ───────────────────────────────────────
		// Évite de relire/parser le JSON de sauvegarde et de recharger les textures
		// d'équipement à CHAQUE FRAME (c'était la cause du lag du carrousel).
		// Le cache n'est reconstruit que lorsque la liste des mondes change
		// (ouverture du menu, création, suppression).
		class CarouselPreviewCache
		{
			public Equipment Equipment = new Equipment();
			public Color Skin;
			public Color HairColor;
			public Texture2D HairBase;
			public Texture2D HairOverlay;
			public int BeardStyle;
			public string Species = "human";
			public Color MorphTint = Color.White;
			public Dictionary<string, int> FeatureVariants = new();
			public Dictionary<string, Color> FeatureColors = new();
		}

		public static bool IsConnectingWire() => _isConnectingWire;

		public static (int x, int y) GetWireStartTile() => _wireStartTile;

		private static void SaveStructure(Vector2Int corner1, Vector2Int corner2)
		{
			int minX = Math.Min(corner1.X, corner2.X);
			int minY = Math.Min(corner1.Y, corner2.Y);
			int maxX = Math.Max(corner1.X, corner2.X);
			int maxY = Math.Max(corner1.Y, corner2.Y);
			
			int width = maxX - minX + 1;
			int height = maxY - minY + 1;
			if (width > 64 || height > 64)
			{
				AddNotification(new Notification($" Zone trop grande ({width}x{height}). Max 64x64.", Color.Red, 3f));
				return;
			}
			
			string structureName = $"Structure_{DateTime.Now:yyyyMMdd_HHmmss}";
			// Option : demander un nom via chat (implémentation plus complexe)
			// On peut aussi utiliser ChatSystem.AddInputPrompt mais pour simplifier on génère un nom.
			
			var structure = new StructureData
			{
				Name = structureName,
				CreatedAt = DateTime.Now,
				MinX = minX,
				MinY = minY,
				MaxX = maxX,
				MaxY = maxY,
				Tiles = new List<StructureTileData>()
			};
			
			for (int x = minX; x <= maxX; x++)
			{
				for (int y = minY; y <= maxY; y++)
				{
					var tileData = new StructureTileData
					{
						X = x - minX,  // coordonnées relatives
						Y = y - minY,
						GroundTileId = World.GetGroundTileIdAt(x, y),
						PlacedTileId = World.GetObjectIdAt(x, y),
						OverlayId = World.GetOverlayAt(x, y),
						DecorationIndex = -1,
						VariationIndex = -1,
						Height = World.GetHeightAt(x, y)
					};
					
					// Récupérer la décoration s'il y en a
					var chunk = World.GetChunkAt(x, y);
					if (chunk != null)
					{
						if (chunk.Decorations.TryGetValue((x, y), out int decoIndex))
							tileData.DecorationIndex = decoIndex;
						if (chunk.Variations.TryGetValue((x, y), out int varIndex))
							tileData.VariationIndex = varIndex;
						
						// Conteneur
						var container = chunk.GetContainerAt(x, y);
						if (container != null)
						{
							var containerSave = new ContainerInventorySave();
							foreach (var slot in container.Slots)
							{
								containerSave.Slots.Add(new InventorySlotSave
								{
									ItemName = slot.Item?.Name ?? "",
									Count = slot.Count,
									IsEmpty = slot.IsEmpty
								});
							}
							tileData.ContainerData = containerSave;
						}
						
						// Porte-armure
						var armorStand = chunk.GetArmorStandAt(x, y);
						if (armorStand != null)
						{
							tileData.ArmorStandData = new ArmorStandSaveData
							{
								Head = armorStand.Head?.Name,
								Body = armorStand.Body?.Name,
								Legs = armorStand.Legs?.Name,
								MainHand = armorStand.MainHand?.Name,
								OffHand = armorStand.OffHand?.Name,
								CurrentPose = armorStand.CurrentPose
							};
						}
						
						// Culture
						var crop = chunk.GetCropAt(x, y);
						if (crop != null)
						{
							tileData.CropData = new CropSaveData
							{
								CropTileId = crop.CropTileId,
								PlantTime = crop.PlantTime
							};
						}
					}
					
					structure.Tiles.Add(tileData);
				}
			}
			
			// Charger les structures existantes
			string path = "Data/structures.json";
			List<StructureData> structures = new();
			if (File.Exists(path))
			{
				string json = File.ReadAllText(path);
				var root = JsonSerializer.Deserialize<StructuresRoot>(json);
				if (root?.Structures != null)
					structures = root.Structures;
			}
			structures.Add(structure);
			
			var newRoot = new StructuresRoot { Structures = structures };
			string outputJson = JsonSerializer.Serialize(newRoot, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(path, outputJson);
			
			AddNotification(new Notification($" Structure sauvegardée : {structureName} ({width}x{height})", new Color(100, 255, 100, 255), 4f));
		}

		private static Texture2D LoadMapMarkerTexture()
		{
			if (_mapMarkerLoaded && _mapMarkerTexture.Id != 0)
				return _mapMarkerTexture;
			
			// Essayer de charger la texture
			_mapMarkerTexture = Raylib.LoadTexture("assets/gui/map_marker.png");
			if (_mapMarkerTexture.Id == 0)
			{
				// Fallback : créer une texture simple si le fichier n'existe pas
				Image img = Raylib.GenImageColor(32, 32, Color.Blank);
				// Dessiner un triangle pointant vers le haut
				for (int y = 0; y < 32; y++)
				{
					for (int x = 0; x < 32; x++)
					{
						float cx = 16f, cy = 16f;
						float dx = x - cx, dy = y - cy;
						float dist = MathF.Sqrt(dx*dx + dy*dy);
						if (dist < 14f && dy < 8f)
						{
							Raylib.ImageDrawPixel(ref img, x, y, Color.Red);
						}
						else if (dist < 10f && dy > -4f)
						{
							Raylib.ImageDrawPixel(ref img, x, y, Color.White);
						}
					}
				}
				_mapMarkerTexture = Raylib.LoadTextureFromImage(img);
				Raylib.UnloadImage(img);
			}
			
			_mapMarkerLoaded = true;
			return _mapMarkerTexture;
		}

		// Reçu une seule fois côté client, juste après la connexion : initialise une
		// partie "vierge" partagée (sans toucher aux sauvegardes locales). Le client reste
		// sur l'écran de chargement (_mpWaitingForWorld) tant que OnNetworkWorldSyncComplete
		// n'a pas été appelé : voir NetworkManager.HandleWorldSync.
		public static void OnNetworkWelcomeReceived(WelcomeMsg welcome)
		{
			//  Même raison que dans StartNewGame/BeginLoadGame : nettoyer les
			// personnages-vitrines du menu principal avant d'entrer en jeu. entities.Clear()
			// ci-dessous suffit à éviter qu'ils se retrouvent mélangés aux PNJ de l'hôte,
			// mais sans cet appel, _menuShowcaseEntities/_menuShowcaseNetIds gardent des
			// références obsolètes vers des entités qui viennent d'être vidées de la liste.
			WorldSeed = welcome.WorldSeed;
			PerlinNoise.SetSeed(WorldSeed);
			World.Reset();
			World.CurrentSaveName = "";
			_currentSaveName = ""; // un client ne sauvegarde jamais le monde partagé sur disque

			_gameTime = welcome.GameTime;
			_totalPlayTime = 0;

			//  MÉTÉO : on prend tout de suite le temps qu'il fait chez l'hôte au lieu
			// d'attendre la prochaine diffusion périodique ("Weather").
			ApplyNetworkWeather(welcome.WeatherType);

			destroyedObjects.Clear();
			objectHPs.Clear();
			entities.Clear();
			GroundItems.Clear();
			_particles.Clear();

			_pendingWelcomeHadRestore = welcome.Restore != null;

			if (welcome.Restore != null)
			{
				//  On a déjà visité ce monde : on retrouve son personnage tel qu'on l'a
				// laissé (position, stats, inventaire, équipement, apparence) au lieu de
				// repartir de zéro.
				ApplyGuestSaveData(welcome.Restore);
				EntityRenderer.ClearEquipmentCache();
			}
			else
			{
				// Premier passage sur ce monde : personnage flambant neuf.
				playerHP = playerMaxHP = 20;
				playerStamina = playerMaxStamina = 100f; staminaLocked = false;
				playerHunger = playerMaxHunger = 100f;
				playerThirst = playerMaxThirst = 100f;
				equipment = new Equipment();
				equipment.LoadEquipmentTextures();
				PlayerHairStyle = Random.Shared.Next(0, Math.Max(1, hairBaseTextures.Count));
				PlayerBeardStyle = Random.Shared.Next(0, Math.Max(1, beardBaseTextures.Count));
				EntityRenderer.ClearEquipmentCache();

				InventoryRenderer.Initialize(40);

				// Apparaître à proximité de l'hôte (toujours id 0 dans le roster reçu).
				var host = welcome.Roster.FirstOrDefault(p => p.Id == 0);
				_playerPos = host != null ? new Vector2(host.PosX + 60, host.PosY + 60) : Vector2.Zero;
			}

			_playerEntity = new Entity(_playerPos, "human", true);
			_playerEntity.IsPlayer = true;
			_playerEntity.WorldPos = _playerPos;
			_playerEntity.Facing = _playerFacing;

			// On NE passe PAS encore en jeu ici : on attend le paquet "WorldSync" qui suit
			// (chunks + entités + objets au sol + énergie envoyés d'un coup), pour ne pas
			// lâcher le joueur dans un monde encore vide pendant qu'il continue de charger
			// en arrière-plan. Voir OnNetworkWorldSyncComplete.
		}

		//  À appeler quand l'hébergement démarre : recharge depuis le disque les
		// personnages des invités qui ont déjà visité CE monde.
		public static void LoadNetworkPlayerRegistry(string worldName)
		{
			_networkPlayerRegistryWorldName = worldName;
			_networkPlayerRegistry = new Dictionary<string, GuestPlayerSaveData>();
			try
			{
				string path = NetworkPlayerRegistryPath(worldName);
				if (File.Exists(path))
				{
					string json = File.ReadAllText(path);
					var loaded = JsonSerializer.Deserialize<Dictionary<string, GuestPlayerSaveData>>(json);
					if (loaded != null) _networkPlayerRegistry = loaded;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($" Impossible de charger les personnages invités de \"{worldName}\" : {ex.Message}");
			}
		}

		private static void DrawEnergyConnections(Camera2D camera)
		{
			int ts = TileSize;
			foreach (var conn in World.EnergyConnections)
			{
				Vector2 p1 = new Vector2(conn.X1 * ts + ts / 2f, conn.Y1 * ts + ts / 2f);
				Vector2 p2 = new Vector2(conn.X2 * ts + ts / 2f, conn.Y2 * ts + ts / 2f);
				
				// Ajuster pour la hauteur du terrain
				p1.Y -= World.GetHeightAt(conn.X1, conn.Y1) * ts / 4f;
				p2.Y -= World.GetHeightAt(conn.X2, conn.Y2) * ts / 4f;
				
				// Effet de pulsation électrique
				float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 8f);
				Color wireColor = new Color((byte)255, (byte)(200 * pulse), (byte)(50 * pulse), (byte)220);
				
				Raylib.DrawLineEx(p1, p2, 3, wireColor);
				
				// Dessiner une petite "étincelle" au milieu
				Vector2 mid = (p1 + p2) / 2;
				Raylib.DrawCircle((int)mid.X, (int)mid.Y, 3, new Color(255, 200, 100, 180));
			}
			
			// Ligne de connexion en cours (mode placement)
			if (_isConnectingWire && _wireStartTile != (-1, -1))
			{
				Vector2 startPos = new Vector2(_wireStartTile.x * ts + ts / 2f, _wireStartTile.y * ts + ts / 2f);
				startPos.Y -= World.GetHeightAt(_wireStartTile.x, _wireStartTile.y) * ts / 4f;
				
				Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
				Vector2 endPos = mouseWorld;
				
				// Dessiner la ligne pointillée
				float dashLength = 15f;
				float totalLength = Vector2.Distance(startPos, endPos);
				Vector2 dir = (endPos - startPos) / totalLength;
				
				for (float d = 0; d < totalLength; d += dashLength)
				{
					Vector2 p = startPos + dir * d;
					Raylib.DrawCircle((int)p.X, (int)p.Y, 3, new Color(100, 200, 255, 200));
				}
				
				// Afficher un indicateur à la souris
				Raylib.DrawCircle((int)mouseWorld.X, (int)mouseWorld.Y, 10, new Color(100, 200, 255, 150));
				Raylib.DrawCircleLines((int)mouseWorld.X, (int)mouseWorld.Y, 10, new Color(100, 200, 255, 255));
			}
		}

		/// <summary>
		/// Met à jour tous les systèmes d'énergie (machine à vapeur, dynamo, batteries, ampoules)
		/// </summary>
		//  OPTIM : buffer réutilisé d'une frame à l'autre pour éviter d'allouer une nouvelle
		// List à chaque appel de UpdateEnergySystems() (remplace les 4x .ToList() précédents).
		private static readonly List<(int x, int y)> _energyRemovalBuffer = new();

		private const int BATTERY_ITEM_ID = 409;

		// "Batterie portable" (voir items.json)
		private const string BATTERY_CHARGE_PREFIX = "CHARGE:";

		//  Gère la touche E sur un socle de chargeur de batterie (405) :
		// - si le socle est vide et que le joueur tient une batterie portable en main -> on la pose.
		// - si le socle contient déjà une batterie -> on la retire et on la rend au joueur (charge conservée).
		private static void HandleBatteryChargerInteraction(int x, int y)
		{
			if (!World.BatteryChargers.TryGetValue((x, y), out var charger))
			{
				charger = new BatteryChargerData();
				World.BatteryChargers[(x, y)] = charger;
			}

			if (charger.InsertedBattery == null)
			{
				Item? held = equipment.MainHand;
				if (held == null || GetItemId(held.Name) != BATTERY_ITEM_ID)
				{
					AddNotification(new Notification(" Tenez une batterie portable en main pour la poser ici.", new Color(255, 200, 100, 255), 2f));
					return;
				}

				float storedEnergy = 0f;
				if (held.Metadata.StartsWith(BATTERY_CHARGE_PREFIX))
					float.TryParse(held.Metadata.Substring(BATTERY_CHARGE_PREFIX.Length), System.Globalization.CultureInfo.InvariantCulture, out storedEnergy);

				const float maxCapacity = 10000f; // doit correspondre à BatteryData.MaxCapacity par défaut

				if (!World.InsertBatteryInCharger(x, y, storedEnergy, maxCapacity)) return;

				//  Retire réellement l'item de l'inventaire : Item.Count n'est pas la quantité du
				// stack (c'est InventorySlot.Count), donc on doit décrémenter le bon slot, comme
				// pour la bannière plus haut, sinon la batterie "reste" dans l'inventaire.
				var heldSlot = InventoryRenderer.InventorySlots.FirstOrDefault(slot => slot.Item == held);
				if (heldSlot != null)
				{
					heldSlot.Count--;
					if (heldSlot.Count <= 0)
					{
						if (equipment.MainHand == heldSlot.Item) equipment.MainHand = null;
						heldSlot.Clear();
					}
				}
				else
				{
					RemoveItemFromInventoryById(BATTERY_ITEM_ID, 1);
				}

				AddNotification(new Notification(" Batterie posée sur le chargeur.", new Color(100, 255, 100, 255), 1.5f));
			}
			else
			{
				var battery = World.RemoveBatteryFromCharger(x, y);
				if (battery == null) return;

				if (GameData.ItemDatabase.TryGetValue(BATTERY_ITEM_ID, out var itemData))
				{
					var newItem = new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
					newItem.Metadata = BATTERY_CHARGE_PREFIX + battery.StoredEnergy.ToString(System.Globalization.CultureInfo.InvariantCulture);
					int remaining = AddItemToInventory(newItem, 1);
					if (remaining > 0)
					{
						// Pas de place : on remet la batterie sur le socle plutôt que de la perdre
						World.InsertBatteryInCharger(x, y, battery.StoredEnergy, battery.MaxCapacity);
						AddNotification(new Notification(" Inventaire plein !", new Color(255, 100, 100, 255), 2f));
						return;
					}
				}

				int percent = battery.MaxCapacity > 0 ? (int)(100f * battery.StoredEnergy / battery.MaxCapacity) : 0;
				AddNotification(new Notification($" Batterie retirée ({percent}% chargée).", new Color(100, 200, 255, 255), 2f));
			}
		}

		//  Capteur de plaques de pression, appelé À CHAQUE FRAME (contrairement à
		// UpdateEnergySystems, throttled à _energyUpdateInterval pour les performances). Rôle
		// unique : détecter qu'un poids est passé sur une plaque, même brièvement entre deux tics
		// électriques, et le mémoriser dans gate.PendingPress jusqu'à ce que le prochain tic le
		// consomme. Ne touche à AUCUN autre système électrique (pas de propagation, pas de calcul
		// de OutputState) : reste donc quasi gratuit.
		private static void UpdatePressurePlateSensors()
		{
			//  Sortie rapide : la quasi-totalité des mondes ne contiennent aucune plaque, ou n'en
			// contiennent qu'une poignée posées par le joueur. Pas la peine de construire l'ensemble
			// des tuiles occupées si rien n'écoute.
			bool anyPlate = false;
			foreach (var g in World.LogicGates.Values)
			{
				if (g.Type == GateType.PRESSURE_PLATE) { anyPlate = true; break; }
			}
			if (!anyPlate) return;

			int ts = TileSize;

			//  Ensemble des tuiles actuellement occupées par un poids (joueurs + entités vivantes),
			// construit UNE SEULE FOIS par frame : coût O(joueurs + entités), PAS O(nombre de
			// plaques) — c'est ce qui garde l'opération bon marché même avec beaucoup de plaques.
			var occupied = new HashSet<(int x, int y)>();
			foreach (var p in GetLocalPlayers())
				occupied.Add(((int)(p.Position.X / ts), (int)(p.Position.Y / ts)));
			foreach (var e in entities)
			{
				if (!e.IsAlive) continue;
				occupied.Add(((int)(e.WorldPos.X / ts), (int)(e.WorldPos.Y / ts)));
			}

			foreach (var kv in World.LogicGates)
			{
				var gate = kv.Value;
				if (gate.Type != GateType.PRESSURE_PLATE) continue;
				if (occupied.Contains(kv.Key)) gate.PendingPress = true;
			}
		}

		public static class SavedServers
		{
			private const string SavePath = "Data/servers.json";
			private const int MaxEntries = 10;
			public static List<SavedServerEntry> Entries = new();

			public static void Load()
			{
				Entries = new List<SavedServerEntry>();
				if (!File.Exists(SavePath)) return;
				try
				{
					string json = File.ReadAllText(SavePath);
					var loaded = JsonSerializer.Deserialize<List<SavedServerEntry>>(json);
					if (loaded != null) Entries = loaded;
				}
				catch (Exception ex)
				{
					Console.WriteLine($" Impossible de charger la liste des serveurs ({ex.Message}).");
					Entries = new List<SavedServerEntry>();
				}
			}

			public static void Save()
			{
				try
				{
					string? dir = Path.GetDirectoryName(SavePath);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
						Directory.CreateDirectory(dir);
					string json = JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true });
					File.WriteAllText(SavePath, json);
				}
				catch (Exception ex)
				{
					Console.WriteLine($" Impossible de sauvegarder la liste des serveurs : {ex.Message}");
				}
			}

			//  Enregistre (ou remonte en tête de liste si déjà connu) un serveur qu'on vient
			// d'essayer de rejoindre, pour le retrouver directement la prochaine fois.
			public static void AddOrUpdate(string ip, int port, string name = "")
			{
				if (string.IsNullOrWhiteSpace(ip)) return;
				Entries.RemoveAll(e => e.Ip.Equals(ip, StringComparison.OrdinalIgnoreCase) && e.Port == port);
				Entries.Insert(0, new SavedServerEntry { Ip = ip, Port = port, Name = name, LastJoinedTicks = DateTime.UtcNow.Ticks });
				if (Entries.Count > MaxEntries) Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
				Save();
			}

			public static void Remove(SavedServerEntry entry)
			{
				Entries.RemoveAll(e => e.Ip == entry.Ip && e.Port == entry.Port);
				Save();
			}
		}

		private static void LoadGameUiAssets(List<string> missingTextures)
		{
			UIManager.Initialize();
			CraftingUI.Initialize();
			FontManager.LoadFont("assets/pixel_font.ttf", 32);
			World.LoadInteractionIcons();
			LoadMenuAssets();
			
			_arrowTexture = TryLoad("assets/extras/arrow.png");
			if (_arrowTexture.Id == 0)
				PrintWarning("Fleche d'interaction absente -> affichage rectangle");

			KhamsinBulletTexture = TryLoad("assets/extras/khamsin_bullet.png");
			if (KhamsinBulletTexture.Id == 0)
				PrintWarning("assets/extras/khamsin_bullet.png manquant");

			LootbagTexture = TryLoad("assets/extras/lootbag.png");
			if (LootbagTexture.Id == 0)
				PrintWarning("assets/extras/lootbag.png manquant");

			TreeLeafTexture = TryLoad("assets/extras/tree_leaf.png");
			if (TreeLeafTexture.Id == 0)
				PrintWarning("assets/extras/tree_leaf.png manquant");

			MagicExplosionTextures.Clear();
			for (int i = 1; i <= 5; i++)
			{
				Texture2D explosionTex = TryLoad($"assets/extras/explode_{i}.png");
				MagicExplosionTextures.Add(explosionTex);
				if (explosionTex.Id == 0)
					PrintWarning($"assets/extras/explode_{i}.png manquant");
			}

			SlashTextures.Clear();
			for (int i = 1; i <= 5; i++)
			{
				Texture2D slashTex = TryLoad($"assets/extras/slash_{i}.png");
				SlashTextures.Add(slashTex);
				if (slashTex.Id == 0)
					PrintWarning($"assets/extras/slash_{i}.png manquant");
			}
			
			_questionIcon = TryLoad("assets/gui/interrogation.png");
			if (_questionIcon.Id == 0)
				Console.WriteLine("   assets/gui/interrogation.png manquant");

			_lmouseButtonTex = TryLoad("assets/gui/lmouse_button.png");
			_rmouseButtonTex = TryLoad("assets/gui/rmouse_button.png");
			_wmouseButtonTex = TryLoad("assets/gui/wmouse_button.png");
			_gamepadButtonTextures.Clear();
			_gamepadButtonTextures[GamepadButton.LeftFaceUp] = TryLoad("assets/gui/input_controller_up.png");
			_gamepadButtonTextures[GamepadButton.LeftFaceDown] = TryLoad("assets/gui/input_controller_down.png");
			_gamepadButtonTextures[GamepadButton.LeftFaceLeft] = TryLoad("assets/gui/input_controller_left.png");
			_gamepadButtonTextures[GamepadButton.LeftFaceRight] = TryLoad("assets/gui/input_controller_right.png");
			_gamepadButtonTextures[GamepadButton.RightFaceUp] = TryLoad("assets/gui/input_controller_triangle.png");
			_gamepadButtonTextures[GamepadButton.RightFaceRight] = TryLoad("assets/gui/input_controller_circle.png");
			_gamepadButtonTextures[GamepadButton.RightFaceDown] = TryLoad("assets/gui/input_controller_cross.png");
			_gamepadButtonTextures[GamepadButton.RightFaceLeft] = TryLoad("assets/gui/input_controller_square.png");
			_gamepadButtonTextures[GamepadButton.LeftThumb] = TryLoad("assets/gui/input_controller_l_joystick.png");
			_gamepadButtonTextures[GamepadButton.RightThumb] = TryLoad("assets/gui/input_controller_r_joystick.png");
			_gamepadButtonTextures[GamepadButton.LeftTrigger1] = TryLoad("assets/gui/input_controller_l1.png");
			_gamepadButtonTextures[GamepadButton.LeftTrigger2] = TryLoad("assets/gui/input_controller_l2.png");
			_gamepadButtonTextures[GamepadButton.RightTrigger1] = TryLoad("assets/gui/input_controller_r1.png");
			_gamepadButtonTextures[GamepadButton.RightTrigger2] = TryLoad("assets/gui/input_controller_r2.png");
			_gamepadJoystickTextures.Clear();
			_gamepadJoystickTextures["l_up"] = TryLoad("assets/gui/input_controller_l_joystick_up.png");
			_gamepadJoystickTextures["l_down"] = TryLoad("assets/gui/input_controller_l_joystick_down.png");
			_gamepadJoystickTextures["l_left"] = TryLoad("assets/gui/input_controller_l_joystick_left.png");
			_gamepadJoystickTextures["l_right"] = TryLoad("assets/gui/input_controller_l_joystick_right.png");
			_gamepadJoystickTextures["r_up"] = TryLoad("assets/gui/input_controller_r_joystick_up.png");
			_gamepadJoystickTextures["r_down"] = TryLoad("assets/gui/input_controller_r_joystick_down.png");
			_gamepadJoystickTextures["r_left"] = TryLoad("assets/gui/input_controller_r_joystick_left.png");
			_gamepadJoystickTextures["r_right"] = TryLoad("assets/gui/input_controller_r_joystick_right.png");
			
			_gearTexture = TryLoad("assets/gui/gear.png");
			if (_gearTexture.Id == 0)
				PrintWarning("Engrenage manquant pour dynamo");
			_tooltipWaterIcon = TryLoad("assets/gui/tooltip_icon_water.png");
			_tooltipFireIcon = TryLoad("assets/gui/tooltip_icon_fire.png");

			_speechBubbleTexture = TryLoad("assets/gui/bubble_speech.png");
			if (_speechBubbleTexture.Id == 0)
				PrintWarning("assets/gui/bubble_speech.png manquant");
			
			_savingIconTexture = TryLoad("assets/gui/saving.png");
			if (_savingIconTexture.Id == 0)
				PrintWarning("assets/gui/saving.png manquant - affichage textuel");
			
			_pedalTexture = TryLoad("assets/gui/pedal.png");
			_pedalPushTexture = TryLoad("assets/gui/pedal_push.png");

			_serverOwnerIcon = TryLoad("assets/gui/server_owner.png");
			_serverClientIcon = TryLoad("assets/gui/server_client.png");
			_serverTeleportIcon = TryLoad("assets/gui/server_teleport.png");
			_serverKickIcon = TryLoad("assets/gui/server_kick.png");
			_serverPvpOnIcon = TryLoad("assets/gui/server_pvp_on.png");
			_serverPvpOffIcon = TryLoad("assets/gui/server_pvp_off.png");
			_uiBarSlotTex = TryLoad("assets/gui/ui_bar_slot.png");
			_uiBarSlotOpenTex = TryLoad("assets/gui/ui_bar_slot_open.png");
			_uiBarConnectionTex = TryLoad("assets/gui/ui_bar_connection.png");
			RadialSlotTexture = TryLoad("assets/gui/hotbar_slot.png");
			RadialSlotOnTexture = TryLoad("assets/gui/hotbar_slot_on.png");
			RadialSelectorTexture = TryLoad("assets/gui/hotbar_selector.png");
			RadialSelectorOnTexture = TryLoad("assets/gui/hotbar_selector_on.png");
			if (_serverKickIcon.Id == 0)
				PrintWarning("assets/gui/server_kick.png manquant");

			_healthBarWingTexture = TryLoad("assets/gui/health_bar_wing.png");
			_healthBarWingColorTexture = TryLoad("assets/gui/health_bar_wing_color.png");
			_defaultBossCursorTexture = TryLoad("assets/gui/boss_cursor.png");
			
			if (_pedalTexture.Id == 0)
				PrintWarning("assets/gui/pedal.png manquant");
			
			if (_pedalPushTexture.Id == 0)
				PrintWarning("assets/gui/pedal_push.png manquant");
			
			_dashboardTexture = TryLoad("assets/gui/car_dashboard.png");
			_arrowTextureCar = TryLoad("assets/gui/car_arrow.png");
			
			if (_dashboardTexture.Id == 0)
				PrintWarning("assets/gui/car_dashboard.png manquant");
			
			if (_arrowTextureCar.Id == 0)
				PrintWarning("assets/gui/car_arrow.png manquant");
		}
    }
}
