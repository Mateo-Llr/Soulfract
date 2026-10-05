// Program.World.cs
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

		public const int TileSize = 40;

		private const float MAX_INTERACTION_DISTANCE = 3 * TileSize;
		private static float _tikiTotemScanTimer;

		private static bool TryFindTikiTotemUnderMouse(Vector2 mouseWorld, out int tileX, out int tileY)
		{
			var hit = FindTileUnderMouse(mouseWorld, TileSize);
			if (hit.objectId == 88)
			{
				tileX = hit.tileX;
				tileY = hit.tileY;
				return true;
			}

			int mouseTileX = (int)MathF.Floor(mouseWorld.X / TileSize);
			int mouseTileY = (int)MathF.Floor(mouseWorld.Y / TileSize);
			var tikiData = WorldTileRegistry.GetTile(88);
			int spriteWidth = TileSize * (tikiData?.Size?.Width ?? 1);
			int spriteHeight = TileSize * (tikiData?.Size?.Height ?? 2);
			for (int x = mouseTileX - 1; x <= mouseTileX + 1; x++)
			{
				for (int y = mouseTileY - 3; y <= mouseTileY + 3; y++)
				{
					if (World.GetObjectIdAt(x, y) != 88)
						continue;

					int elevation = World.GetHeightAt(x, y) * TileSize / 4;
					float drawX = x * TileSize + (tikiData?.DrawOffset?.X ?? 0);
					float drawY = y * TileSize - elevation - spriteHeight + TileSize;
					var spriteBounds = new Rectangle(drawX, drawY, spriteWidth, spriteHeight);
					if (!Raylib.CheckCollisionPointRec(mouseWorld, spriteBounds))
						continue;

					tileX = x;
					tileY = y;
					return true;
				}
			}

			tileX = -1;
			tileY = -1;
			return false;
		}

		private static bool TryOfferFoodToTikiTotem(Item offering, Vector2 mouseWorld)
		{
			int itemId = GetItemId(offering.Name);
			if (!GameData.ItemDatabase.TryGetValue(itemId, out var foodData)
				|| foodData.Type != ItemType.Food || foodData.HungerRestore <= 0f)
				return false;

			if (!TryFindTikiTotemUnderMouse(mouseWorld, out int tileX, out int tileY))
				return false;

			Vector2 totemPos = new(tileX * TileSize + TileSize / 2f, tileY * TileSize + TileSize / 2f);
			if (Vector2.Distance(_playerPos, totemPos) > MAX_INTERACTION_DISTANCE)
				return false;

			bool spiritPresent = NetworkManager.IsClient
				? NetworkManager.GetRemoteEntities().Any(entity => entity.IsSpiritAnimal && entity.HP > 0
					&& entity.SpiritTotemTileX == tileX && entity.SpiritTotemTileY == tileY)
				: entities.Any(entity => entity.IsSpiritAnimal && entity.IsAlive
					&& entity.SpiritTotemTileX == tileX && entity.SpiritTotemTileY == tileY);
			if (spiritPresent)
			{
				AddNotification(new Notification(" L'esprit de ce totem est déjà présent.", new Color(150, 220, 255, 255), 2f));
				return true;
			}

			if (World.GetTileMeta(tileX, tileY, "tiki_ritual_due") != "")
			{
				AddNotification(new Notification(" Le totem répond déjà à vos offrandes...", new Color(150, 220, 255, 255), 2f));
				return true;
			}

			int offerings = int.TryParse(World.GetTileMeta(tileX, tileY, "tiki_food_offerings"), out int savedOfferings)
				? savedOfferings
				: 0;
			RemoveItemFromInventory(offering.Name, 1);
			offerings++;

			if (offerings < 3)
			{
				World.SetTileMeta(tileX, tileY, "tiki_food_offerings", offerings.ToString(System.Globalization.CultureInfo.InvariantCulture));
				AddNotification(new Notification($" Offrande acceptée ({offerings}/3).", new Color(180, 220, 150, 255), 1.8f));
				return true;
			}

			World.RemoveTileMeta(tileX, tileY, "tiki_food_offerings");
			long summonAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 8000;
			World.SetTileMeta(tileX, tileY, "tiki_ritual_due", summonAt.ToString(System.Globalization.CultureInfo.InvariantCulture));
			AddNotification(new Notification(" Les offrandes ont éveillé le totem...", new Color(150, 220, 255, 255), 3f));
			return true;
		}

		private static void UpdateTikiTotems(float dt, Vector2 playerPos)
		{
			if (NetworkManager.IsClient || dt <= 0f)
				return;

			_tikiTotemScanTimer += dt;
			if (_tikiTotemScanTimer < 0.5f)
				return;
			_tikiTotemScanTimer = 0f;

			long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			int centerX = (int)MathF.Floor(playerPos.X / TileSize);
			int centerY = (int)MathF.Floor(playerPos.Y / TileSize);
			const int scanRadius = 8;
			for (int y = centerY - scanRadius; y <= centerY + scanRadius; y++)
			{
				for (int x = centerX - scanRadius; x <= centerX + scanRadius; x++)
				{
					if (World.GetObjectIdAt(x, y) != 88
						|| !long.TryParse(World.GetTileMeta(x, y, "tiki_ritual_due"), out long summonAt)
						|| now < summonAt)
						continue;

					if (entities.Any(entity => entity.IsSpiritAnimal && entity.IsAlive
						&& entity.SpiritTotemTileX == x && entity.SpiritTotemTileY == y))
					{
						World.RemoveTileMeta(x, y, "tiki_ritual_due");
						continue;
					}

					Vector2 spawnPos = new((x + 1.5f) * TileSize, (y + 0.5f) * TileSize);
					var spiritWolf = new Entity(spawnPos, "Wolf", false)
					{
						IsSpiritAnimal = true,
						SpiritTotemTileX = x,
						SpiritTotemTileY = y,
						Behavior = "passive",
						Tint = new Color(165, 225, 255, 210)
					};
					entities.Add(spiritWolf);
					World.RemoveTileMeta(x, y, "tiki_ritual_due");
					AddNotification(new Notification(" Un loup-esprit apparaît auprès du totem !", new Color(150, 220, 255, 255), 3f));
				}
			}
		}

		private static Dictionary<string, List<Sound>> _tileHitSounds = new(StringComparer.OrdinalIgnoreCase);

		private static Dictionary<string, List<Sound>> _tileBreakSounds = new(StringComparer.OrdinalIgnoreCase);

		private static bool _tileSoundsInitialized = false;

		static Vector2 _mapOffset = Vector2.Zero;

		static Vector2 _mapDragStart = Vector2.Zero;

		static bool _isDraggingMap = false;

		static float _mapZoom = 1.0f;

		// Abaissé (était 0.05f) pour permettre un dézoom beaucoup plus important.
		// Sûr même à cette valeur car DrawMap borne désormais le travail par
		// frame via MAP_TARGET_SAMPLES (le detailLevel grandit avec le dézoom
		// au lieu d'être plafonné) : voir Program_Core.cs / DrawMap().
		private const float MAP_ZOOM_MIN = 0.01f;

		private const float MAP_ZOOM_MAX = 2.5f;

		private const float MAP_ZOOM_STEP = 0.1f;

		const float MAP_TILE_SIZE_BASE = 8f;

		const int MAP_RENDER_WIDTH = 200;

		const int MAP_RENDER_HEIGHT = 150;

		private static bool _playerWalkSoundsLoaded = false;

		private static bool _punchSoundsLoaded = false;

		private static bool _hoverSoundLoaded = false;

		private static bool _buttonClickSoundLoaded = false;

		private static bool _equipSoundLoaded = false;

		private static bool _pistolShootSoundLoaded = false;

		private static bool _pistolDryFireSoundLoaded = false;

		private static bool _eatSoundsLoaded = false;

		private static bool _drinkSoundsLoaded = false;

		private static bool _serverPointerLoaded = false;

		private static bool _hungrySoundLoaded = false;

		private static bool _achievementSoundLoaded = false;

		private static bool _levelUpSoundLoaded = false;

		private static bool _fertilizerSoundLoaded = false;

		// durée de chaque fondu (secondes)
		const int LOADING_CHUNKS_PER_FRAME = 1;

		static float _loadingFadeAlpha = 0f;

		static float _loadingCharacterProgress = 0f;

		static bool _loadingGameStarted = false;

		static float _worldRevealAlpha = 1f;

		static float _loadingMenuExitProgress = 0f;
		static bool _loadingMenuCaptured = false;

		static string _pendingLoadSaveName = "";

		static GameSaveData? _loadingSaveData = null;

		static Queue<(int chunkX, int chunkY)>? _pendingChunkLoads = null;

		static int _loadingChunksTotal = 0;

		static string? _loadingError = null;

		public static Dictionary<string, WorldObjectHP> objectHPs = new();

		static Dictionary<int, Texture2D> tileTextures = new();

		//  Dernière position de mort du joueur, persistante (affichée sur la carte via skull_map.png)
        public static Vector2 LastDeathPosition = Vector2.Zero;

		static bool _deathSkullLoaded = false;

		private static Texture2D LoadDeathSkullTexture()
        {
            if (_deathSkullLoaded && _deathSkullTexture.Id != 0)
                return _deathSkullTexture;
            if (File.Exists("assets/gui/death_skull.png"))
                _deathSkullTexture = Raylib.LoadTexture("assets/gui/death_skull.png");
            _deathSkullLoaded = true;
            return _deathSkullTexture;
        }

		static int _caveTransitionTileX = 0;

		static int _caveTransitionTileY = 0;

		private static bool _eyeShieldTextureLoaded = false;

		static float _saveTimer = 0f;

		static float _saveInterval = 10f;

		public static int WorldSeed = 0;

		private static readonly HashSet<int> _previouslyOwnedItemIds = new();

		public static IReadOnlySet<int> PreviouslyOwnedItemIds => _previouslyOwnedItemIds;

		public static void LoadPreviouslyOwnedItemIds(IEnumerable<int>? itemIds)
		{
			_previouslyOwnedItemIds.Clear();
			if (itemIds != null)
				_previouslyOwnedItemIds.UnionWith(itemIds);
		}

		public static void MarkItemAsPreviouslyOwned(string itemName)
		{
			int itemId = GetItemId(itemName);
			if (itemId != 0)
				_previouslyOwnedItemIds.Add(itemId);
		}

		public static bool HasPreviouslyOwnedIngredient(CraftRecipe recipe)
		{
			if (Program.IsGodMode)
				return true;

			if (recipe.Ingredients == null || recipe.Ingredients.Count == 0)
				return true;

			return recipe.Ingredients.Any(ingredient =>
			{
				if (_previouslyOwnedItemIds.Contains(ingredient.id))
					return true;

				if (!GameData.ItemDatabase.TryGetValue(ingredient.id, out var itemData))
					return false;

				return GetTotalItemCount(itemData.Name) > 0;
			});
		}

		public static void MarkItemsInInventoryAsPreviouslyOwned()
		{
			foreach (var slot in InventoryRenderer.InventorySlots)
				MarkItemAndContentsAsPreviouslyOwned(slot.Item);

			foreach (var item in equipment.ZoneItems)
				MarkItemAndContentsAsPreviouslyOwned(item);
			foreach (var item in equipment.EquippedItems.Values)
				MarkItemAndContentsAsPreviouslyOwned(item);

			MarkItemAndContentsAsPreviouslyOwned(equipment.MainHand);
			MarkItemAndContentsAsPreviouslyOwned(equipment.OffHand);
			MarkItemAndContentsAsPreviouslyOwned(equipment.Backpack);
		}

		private static void MarkItemAndContentsAsPreviouslyOwned(Item? item)
		{
			if (item == null) return;

			MarkItemAsPreviouslyOwned(item.Name);
			if (item.Container != null)
				foreach (var slot in item.Container.Inventory.Slots)
					MarkItemAndContentsAsPreviouslyOwned(slot.Item);
			if (item.Backpack != null)
				foreach (var slot in item.Backpack.Inventory.Slots)
					MarkItemAndContentsAsPreviouslyOwned(slot.Item);
		}

		//  MULTIJOUEUR (côté host) : personnages sauvegardés des joueurs invités pour LE
		// monde actuellement hébergé, indexés par leur PlayerGuid. Rechargé depuis le
		// disque à chaque démarrage d'hébergement (voir LoadNetworkPlayerRegistry).
		private static Dictionary<string, GuestPlayerSaveData> _networkPlayerRegistry = new();

		private static string _networkPlayerRegistryWorldName = "";

		static bool _mpWaitingForWorld = false;

		// ========== NOUVEAU MENU DE CRÉATION DE MONDE ==========
		private static bool _isInWorldCreation = false;

		private static string _newWorldName = "";

		private static string _newWorldSeed = "";

		private static bool _seedFieldFocused = false;

		private static float _worldCreationRot = 0f;

		private static bool _worldCreationRotating = false;

		// Options de monde créées via l'écran de création (conservées dans la sauvegarde)
		public static bool WorldWeaponsBreak = true;

		public static bool WorldFoodSpoil = true;

		// assets/gui/gear.png (bouton paramètres, en bas à droite)
		static Texture2D _mainMenuCreateWorldIcon;
		static Texture2D _buttonToggleOn;
		static Texture2D _buttonToggleOff;
		static Texture2D _worldSettingsIconWeaponsBreak;
		static Texture2D _worldSettingsIconFoodSpoil;
		// assets/gui/create_world.png (dernier élément du carrousel)
		static Texture2D _mainMenuJoinWorldIcon;

		// assets/gui/join_world.png (élément "Rejoindre une partie" du carrousel)
		static Texture2D _carouselSaveBase;

		// assets/gui/save_base.png (socle sous chaque personnage du carrousel)
		static Texture2D _carouselArrowIcon;

		// assets/gui/arrow.png (flèches gauche/droite du carrousel, pointe vers la droite par défaut)
		static Texture2D _worldDeleteIcon;

		// assets/gui/achivements.png (bouton succès, menu pause, en bas à droite)
		static string? _worldPendingDeleteName = null;

		// 0 = accueil, 1 = carrousel (anim de transition)
		static int _worldCarouselIndex = 0;

		// index sélectionné dans (mondes + bouton "créer")
		static float _worldCarouselAnimIndex = 0f;

		// version lissée de _worldCarouselIndex pour l'anim
		static Vector2Int? _selectionCorner1 = null;

		static void SetWorldCarouselIndexToCurrentSave()
	{
		string currentSaveName = !string.IsNullOrEmpty(_currentSaveName) ? _currentSaveName : World.CurrentSaveName;
		if (string.IsNullOrEmpty(currentSaveName)) return;

		int saveIndex = _availableSaves.FindIndex(s => s.Name == currentSaveName);
		if (saveIndex >= 0)
		{
			_worldCarouselIndex = saveIndex + 2;
			_worldCarouselAnimIndex = _worldCarouselIndex;
		}
	}

		public static List<Texture2D> GetCarTextures() => carBodyTextures;

		private const float RANGED_RELOAD_DURATION = 1.0f;

		private struct RangedWeaponReloadState
		{
			public int CurrentAmmo;
			public bool IsReloading;
			public float ReloadTimer;
			public int ReloadAmmoId;
			public int ReloadAmount;
		}

		private static readonly Dictionary<int, RangedWeaponReloadState> _rangedWeaponReloadStates = new();

		static string _currentSaveName = "";

		static float _autoSaveTimer = 0f;

		static float _autoSaveInterval = 60f;

		static bool _isSelectingSave = false;

		static List<SaveInfo> _availableSaves = new();

		static int _selectedSaveIndex = 0;

		static string _newSaveName = "";

		static bool _isCreatingNewSave = false;

		// Sauvegarde
		private static Texture2D _savingIconTexture;

		static Vector2 _worldSpawnPos = Vector2.Zero;

		public static Vector2 ComputeGazeOffset(Vector2 eyeWorldPos, Vector2 targetWorldPos, float maxOffsetX, float maxOffsetY)
		{
			Vector2 delta = targetWorldPos - eyeWorldPos;
			float dist = delta.Length();
			Vector2 dir = dist > 0.0001f ? delta / dist : Vector2.Zero;
			float intensity = Math.Clamp(dist / PUPIL_MAX_DISTANCE, 0f, 1f);

			return new Vector2(
				Math.Clamp(dir.X * maxOffsetX * intensity, -maxOffsetX, maxOffsetX),
				Math.Clamp(dir.Y * maxOffsetY * intensity, -maxOffsetY, maxOffsetY)
			);
		}

		//  Dernière position monde du curseur LOCAL (mémorisée pour l'envoyer sur le
		// réseau - voir NetworkManager.BuildLocalTransform).
		private static Vector2 _lastCursorWorldPos = Vector2.Zero;

		public static Vector2 GetCursorWorldPos() => _lastCursorWorldPos;

		//  MULTIJOUEUR : calcule les décalages pupilles/sourcils (gauche/droite) pour
		// N'IMPORTE QUEL joueur (distant compris), avec la même formule que celle utilisée
		// pour le joueur local dans UpdatePupilLookAt - juste sans écrire dans les champs
		// globaux Pupil*/Eyebrow* (qui restent réservés au joueur local).
		public static (Vector2 pupilLeft, Vector2 pupilRight, Vector2 browLeft, Vector2 browRight) ComputeGazeOffsetsFor(
			Vector2 headWorldPos, Vector2 cursorWorldPos, float facing)
		{
			Vector2 leftEyeWorldPos = headWorldPos + new Vector2(-EYE_LOCAL_OFFSET_X * facing, EYE_LOCAL_OFFSET_Y);
			Vector2 rightEyeWorldPos = headWorldPos + new Vector2(EYE_LOCAL_OFFSET_X * facing, EYE_LOCAL_OFFSET_Y);

			Vector2 pupilLeft = ComputeGazeOffset(leftEyeWorldPos, cursorWorldPos, PUPIL_MAX_OFFSET_X, PUPIL_MAX_OFFSET_Y);
			Vector2 pupilRight = ComputeGazeOffset(rightEyeWorldPos, cursorWorldPos, PUPIL_MAX_OFFSET_X, PUPIL_MAX_OFFSET_Y);

			float facingSign = facing < 0 ? -1f : 1f;
			float baseOffsetX = -PUPIL_BASE_OFFSET_X * facingSign;
			pupilLeft.X += baseOffsetX;
			pupilRight.X += baseOffsetX;

			float browMaxY = PUPIL_MAX_OFFSET_Y * EYEBROW_Y_INTENSITY_FACTOR;
			Vector2 browLeft = new Vector2(0f, ComputeGazeOffset(leftEyeWorldPos, cursorWorldPos, 0f, browMaxY).Y);
			Vector2 browRight = new Vector2(0f, ComputeGazeOffset(rightEyeWorldPos, cursorWorldPos, 0f, browMaxY).Y);

			return (pupilLeft, pupilRight, browLeft, browRight);
		}

		// Calcule le décalage des pupilles ET des sourcils, en fonction de la position du curseur (monde),
		// de la position de la tête (monde) et du sens du regard (facing). Chaque œil a sa PROPRE position
		// approximative (gauche/droite), donc les deux pupilles ne bougent plus forcément "en rythme".
		public static void UpdatePupilLookAt(Vector2 headWorldPos, Vector2 cursorWorldPos, float facing = 1f)
		{
			_lastCursorWorldPos = cursorWorldPos;
			Vector2 leftEyeWorldPos = headWorldPos + new Vector2(-EYE_LOCAL_OFFSET_X * facing, EYE_LOCAL_OFFSET_Y);
			Vector2 rightEyeWorldPos = headWorldPos + new Vector2(EYE_LOCAL_OFFSET_X * facing, EYE_LOCAL_OFFSET_Y);

			PupilLeftOffset = ComputeGazeOffset(leftEyeWorldPos, cursorWorldPos, PUPIL_MAX_OFFSET_X, PUPIL_MAX_OFFSET_Y);
			PupilRightOffset = ComputeGazeOffset(rightEyeWorldPos, cursorWorldPos, PUPIL_MAX_OFFSET_X, PUPIL_MAX_OFFSET_Y);

			// Décalage de base (indépendant du curseur) pour éviter que la pupille soit cachée dans son coin d'œil
			float facingSign = facing < 0 ? -1f : 1f;
			float baseOffsetX = -PUPIL_BASE_OFFSET_X * facingSign;
			PupilLeftOffset.X += baseOffsetX;
			PupilRightOffset.X += baseOffsetX;

			// Sourcils : seulement l'axe Y, avec une amplitude réduite par rapport aux pupilles
			float browMaxY = PUPIL_MAX_OFFSET_Y * EYEBROW_Y_INTENSITY_FACTOR;
			EyebrowLeftOffset = new Vector2(0f, ComputeGazeOffset(leftEyeWorldPos, cursorWorldPos, 0f, browMaxY).Y);
			EyebrowRightOffset = new Vector2(0f, ComputeGazeOffset(rightEyeWorldPos, cursorWorldPos, 0f, browMaxY).Y);
		}

		public static List<EnergyConnection> GetEnergyConnections() => World.EnergyConnections;

		public static void MountAnimal(Entity animal)
		{
			if (animal == null || !animal.IsMountable) return;
			if (MountedAnimal != null) DismountAnimal();

			MountedAnimal = animal;
			animal.Mount(null); // Le joueur est le rider, on gère dans Program

			// Calculer la position assise sur l'animal (offset en fonction de l'espèce)
			Vector2 offset = GetMountOffset(animal);
			Vector2 sitPos = animal.WorldPos + offset;
			SetSitting(true, sitPos, 0f, "sit");
			_playerPos = sitPos; // Pour que la caméra suive

			AddNotification(new Notification($"Vous montez sur {animal.GetDisplayName()}", new Color(100, 200, 255, 255), 2f));
		}

		public static void DismountAnimal()
		{
			if (MountedAnimal == null) return;

			var animal = MountedAnimal;
			animal.Dismount();
			SetSitting(false);

			// Placer le joueur à côté de l'animal
			Vector2 dismountPos = animal.WorldPos + new Vector2(0, 30);
			_playerPos = dismountPos;

			MountedAnimal = null;
		}

		// Fonction pour flouter une texture existante
		private static void ApplyGaussianBlurToTexture(ref Texture2D texture, int radius)
		{
			Image img = Raylib.LoadImageFromTexture(texture);
			img = ApplyGaussianBlurToImage(img, radius);
			Raylib.UnloadTexture(texture);
			texture = Raylib.LoadTextureFromImage(img);
			Raylib.UnloadImage(img);
		}

		// Fonction de flou gaussien simple
		private static Image ApplyGaussianBlurToImage(Image img, int radius)
		{
			int width = img.Width;
			int height = img.Height;
			Image result = Raylib.GenImageColor(width, height, Color.Blank);
			
			// Noyau de flou gaussien simplifié (taille 3x3)
			float[,] kernel = {
				{ 1f/16f, 2f/16f, 1f/16f },
				{ 2f/16f, 4f/16f, 2f/16f },
				{ 1f/16f, 2f/16f, 1f/16f }
			};
			int kernelSize = 3;
			int offset = kernelSize / 2;
			
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					float rSum = 0, gSum = 0, bSum = 0, aSum = 0;
					float weightSum = 0;
					
					for (int ky = -offset; ky <= offset; ky++)
					{
						for (int kx = -offset; kx <= offset; kx++)
						{
							int px = x + kx;
							int py = y + ky;
							if (px >= 0 && px < width && py >= 0 && py < height)
							{
								Color c = Raylib.GetImageColor(img, px, py);
								float weight = kernel[ky + offset, kx + offset];
								rSum += c.R * weight;
								gSum += c.G * weight;
								bSum += c.B * weight;
								aSum += c.A * weight;
								weightSum += weight;
							}
						}
					}
					
					if (weightSum > 0)
					{
						Color newColor = new Color(
							(byte)(rSum / weightSum),
							(byte)(gSum / weightSum),
							(byte)(bSum / weightSum),
							(byte)(aSum / weightSum)
						);
						Raylib.ImageDrawPixel(ref result, x, y, newColor);
					}
				}
			}
			
			Raylib.UnloadImage(img);
			return result;
		}

		// Cache pour la texture du marqueur
		private static Texture2D _mapMarkerTexture = new Texture2D();

		private static bool _mapMarkerLoaded = false;

		// Cache pour la texture du marqueur du dernier lieu de mort du joueur
		private static Texture2D _skullMapTexture = new Texture2D();

		private static bool _skullMapLoaded = false;

		private static Texture2D LoadSkullMapTexture()
		{
			if (_skullMapLoaded && _skullMapTexture.Id != 0)
				return _skullMapTexture;

			if (File.Exists("assets/gui/skull_map.png"))
				_skullMapTexture = Raylib.LoadTexture("assets/gui/skull_map.png");

			_skullMapLoaded = true;
			return _skullMapTexture;
		}

		private static void DrawMapMarker(Texture2D marker, Vector2 pos, float size, float facing)
		{
			if (marker.Id == 0) return;
			
			// Calculer la rotation en fonction de la direction du joueur
			float rotation = 0f;
			if (facing < 0)
				rotation = 180f; // Retourner le marqueur si le joueur regarde à gauche
			
			Rectangle srcRect = new Rectangle(0, 0, marker.Width, marker.Height);
			Rectangle destRect = new Rectangle(pos.X, pos.Y, size, size);
			Vector2 origin = new Vector2(size / 2, size / 2);
			
			// Ajouter un effet de pulsation
			float pulse = 0.9f + 0.1f * MathF.Sin((float)Raylib.GetTime() * 5f);
			Color markerColor = new Color((byte)255, (byte)(150 + 50 * pulse), (byte)(100 + 50 * pulse), (byte)255);
			
			Raylib.DrawTexturePro(marker, srcRect, destRect, origin, rotation, markerColor);
			
			// Ajouter une ombre portée (optionnel)
			Rectangle shadowRect = new Rectangle(pos.X + 2, pos.Y + 2, size, size);
			Raylib.DrawTexturePro(marker, srcRect, shadowRect, origin, rotation, new Color(0, 0, 0, 100));
		}

		public static Vector2 GetPlayerVisualPosition(Vector2 playerPos)
		{
			int playerTileX = (int)(playerPos.X / TileSize);
			int playerTileY = (int)(playerPos.Y / TileSize);
			int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
			float playerYOffset = -playerHeight * TileSize / 4;
			return new Vector2(playerPos.X, playerPos.Y + playerYOffset);
		}

		public static Vector2 GetSittingVisualPosition()
		{
			// _sittingPosition est deja le centre de la hitbox du meuble.
			// Ne pas appliquer un second offset vertical : il decalerait le personnage
			// au-dessus du centre, surtout dans l'animation de sommeil.
			return _sittingPosition;
		}

		public static Vector2 GetFurnitureAnchorPosition(int tileX, int tileY, WorldTileData tileData)
		{
			var (hitboxX, hitboxY, hitboxW, hitboxH) = World.GetCollisionRect(tileX, tileY, tileData, TileSize);
			float anchorX = hitboxX + hitboxW * 0.5f;
			float anchorY = hitboxY + hitboxH * 0.5f;
			return new Vector2(anchorX, anchorY);
		}

		public static Vector2 GetFurnitureSitPosition(Vector2 anchorPosition, int widthTiles, int heightTiles, Vector2 requestedPosition)
		{
			float width = Math.Max(1, widthTiles) * TileSize;
			float height = Math.Max(1, heightTiles) * TileSize;
			float margin = TileSize * 0.3f;
			Vector2 sitPosition = anchorPosition;

			if (width > height)
				sitPosition.X = Math.Clamp(requestedPosition.X, anchorPosition.X - width * 0.5f + margin, anchorPosition.X + width * 0.5f - margin);
			else if (height > width)
				sitPosition.Y = Math.Clamp(requestedPosition.Y, anchorPosition.Y - height * 0.5f + margin, anchorPosition.Y + height * 0.5f - margin);

			return sitPosition;
		}

		public static bool IsSleepPositionOccupied(Vector2 sleepPosition, Entity? ignoredEntity = null)
		{
			const float SLEEP_OCCUPANCY_RADIUS = 18f;
			float radiusSq = SLEEP_OCCUPANCY_RADIUS * SLEEP_OCCUPANCY_RADIUS;

			if (IsSitting && IsSittingBehindFurniture
				&& Vector2.DistanceSquared(GetSittingVisualPosition(), sleepPosition) <= radiusSq)
				return true;

			return entities.Any(entity => entity != ignoredEntity
				&& entity.IsSleeping
				&& Vector2.DistanceSquared(entity.VisualWorldPos, sleepPosition) <= radiusSq);
		}

		public static void PrepareTentOccupation(Vector2 tentPosition, Entity? incomingNpc)
		{
			const float TENT_OCCUPANCY_RADIUS = 18f;
			float radiusSq = TENT_OCCUPANCY_RADIUS * TENT_OCCUPANCY_RADIUS;

			if (IsSitting && IsSittingBehindFurniture
				&& Vector2.DistanceSquared(GetSittingVisualPosition(), tentPosition) <= radiusSq)
			{
				SetSitting(false);
			}

			foreach (var entity in entities)
			{
				if (entity == incomingNpc || !entity.IsSleeping)
					continue;

				if (Vector2.DistanceSquared(entity.VisualWorldPos, tentPosition) <= radiusSq)
					entity.ForceWakeFromTent();
			}
		}

		// Remplacer la fonction GetTileColorForMap par celle-ci :
		static Color GetTileColorForMap(int tileX, int tileY)
		{
			if (World.IsUnderground)
			{
				int objectId = World.GetObjectIdAt(tileX, tileY);
				var objectTile = WorldTileRegistry.GetTile(objectId);
				if (objectTile != null && !objectTile.Walkable &&
					objectTile.Texture.Contains("wall", StringComparison.OrdinalIgnoreCase))
					return new Color(42, 44, 54, 255);
			}

			int groundId = World.GetGroundTileIdAt(tileX, tileY); // sol réel (avec overrides)
			
			// Mapping des IDs de sol vers des couleurs
			return groundId switch
			{
				1  => new Color(100, 180, 80, 255),   // Grass (Plaines)
				44 => new Color(50, 130, 50, 255),    // Forest Grass
				46 => new Color(180, 170, 100, 255),  // Savanna Grass
				47 => new Color(70, 110, 70, 255),    // Taiga Grass
				43 => new Color(220, 240, 255, 255),  // Snow
				11 => new Color(240, 230, 140, 255),  // Sand
				62 => new Color(235, 220, 180, 255),  // Beach sand
				26 => new Color(120, 120, 140, 255),  // Stone / Mountains
				10 => new Color(30, 60, 150, 255),    // Water
				53 => new Color(80, 100, 60, 255),    // Swamp mud
				27 => new Color(160, 130, 90, 255),   // Farmland
				61 => new Color(180, 160, 120, 255),  // Path
				83 => new Color(140, 130, 110, 255),  // Paving stone
				54 => new Color(150, 120, 80, 255),   // Wooden floors
				55 => new Color(150, 120, 80, 255),
				56 => new Color(150, 120, 80, 255),
				57 => new Color(150, 120, 80, 255),
				58 => new Color(150, 120, 80, 255),
				59 => new Color(100, 150, 180, 255),  // Bathroom tiles
				60 => new Color(120, 80, 150, 255),   // Purple carpet
				// Biomes souterrains
				100 => new Color(100, 100, 120, 255), // Cave stone
				101 => new Color(180, 80, 50, 255),   // Lava stone
				102 => new Color(100, 200, 220, 255), // Crystal stone
				103 => new Color(140, 100, 180, 255), // Mushroom soil
				104 => new Color(50, 40, 70, 255),    // Abyss stone
				_ => Color.Gray
			};
		}

		private static bool IsGrassTile(int groundId)
		{
			return groundId == 1   // Grass
				|| groundId == 44  // Forest Grass
				|| groundId == 46  // Savanna Grass
				|| groundId == 47  // Taiga Grass
				|| groundId == 53; // Swamp mud (optionnel)
		}

		public static void SetSitting(bool sitting, Vector2? position = null, float heightOffset = 0f, string pose = "sit", bool behindFurniture = false)
		{
			if (sitting && !_isSitting)
			{
				_preSittingPlayerPos = _playerPos;
				_hasPreSittingPlayerPos = true;
			}

			_isSitting = sitting;
			IsSittingBehindFurniture = sitting && behindFurniture;
			if (sitting && position.HasValue)
			{
				_sittingJustStarted = true;
				_sittingPosition = position.Value;
				_sittingHeightOffset = heightOffset;
				_sittingPose = pose;
				_sittingAnimTimer = 0f;
				_sittingAnimFrame = 0;
				_animState = pose;
				_animFrame = 0;
				_animProg = 0f;
				_playerPos = position.Value;
				
				// La position physique suit la position du meuble pendant l'assise.
			}
			else if (!sitting)
			{
				if (_hasPreSittingPlayerPos)
				{
					_playerPos = _preSittingPlayerPos;
					_hasPreSittingPlayerPos = false;
				}

				_sittingJustStarted = false;
				_sittingPose = "idle";
				_animState = "idle";
				_animFrame = 0;
				_animProg = 0f;
			}
		}

		private static Vector2 GetHandWorldPosition(Vector2 playerVisualPos, float facing, List<AnimalBodyPart> skeleton, string animState, int animFrame, float animProg)
		{
			var tempSkeleton = new List<AnimalBodyPart>();
			foreach (var part in skeleton)
			{
				var tempPart = new AnimalBodyPart(part.Name, part.BasePos.X, part.BasePos.Y, part.BaseRot, part.ParentName, part.Scale);
				tempPart.Texture = part.Texture;
				tempPart.Animations = part.Animations;
				tempSkeleton.Add(tempPart);
			}
			
			float speciesScale = 2.8f;
			
			foreach (var part in tempSkeleton)
			{
				Vector2 animOffset = Vector2.Zero;
				float animRot = 0f;
				
				if (part.Animations.TryGetValue(animState, out var frames) && frames.Count > 0)
				{
					int safeFrame = animFrame % frames.Count;
					int ni = (safeFrame + 1) % frames.Count;
					float t = animProg;
					
					animOffset = Vector2.Lerp(
						new Vector2(frames[safeFrame].X, frames[safeFrame].Y),
						new Vector2(frames[ni].X, frames[ni].Y),
						t) * speciesScale;
					animRot = Raymath.Lerp(frames[safeFrame].Z, frames[ni].Z, t);
				}
				
				Vector2 fb = new(part.BasePos.X * speciesScale, part.BasePos.Y * speciesScale);
				Vector2 fa = new(animOffset.X * facing, animOffset.Y);
				
				Vector2 finalOffset = fb + fa;
				if (facing < 0)
				{
					finalOffset = new Vector2(-finalOffset.X, finalOffset.Y);
				}
				
				if (!string.IsNullOrEmpty(part.ParentName))
				{
					var parent = tempSkeleton.Find(p => p.Name == part.ParentName);
					if (parent != null)
					{
						part.GlobalRot = parent.GlobalRot + (part.BaseRot + animRot) * facing;
						part.GlobalPos = parent.GlobalPos + RotateVec(finalOffset, parent.GlobalRot);
					}
				}
				else
				{
					part.GlobalPos = playerVisualPos + finalOffset;
					part.GlobalRot = (part.BaseRot + animRot) * facing;
				}
			}
			
			var rightArmBottom = tempSkeleton.FirstOrDefault(p => p.Name == "rarmbottom");
			if (rightArmBottom != null)
				return rightArmBottom.GlobalPos;
			
			return playerVisualPos;
		}

		private static void InitializeTileSounds()
		{
			if (_tileSoundsInitialized) return;
			_tileSoundsInitialized = true;

			string soundsDir = Path.Combine("assets", "sounds");
			if (!Directory.Exists(soundsDir)) return;

			foreach (string material in new[] { "wood", "stone", "dirt", "grass", "snow" })
			{
				_tileHitSounds[material] = LoadTileVariantSounds(soundsDir, material, "hit");
				_tileBreakSounds[material] = LoadTileVariantSounds(soundsDir, material, "break");
			}
		}

		private static List<Sound> LoadTileVariantSounds(string soundsDir, string material, string action)
		{
			var sounds = new List<Sound>();
			var candidates = new List<string>();
			string baseName = $"{material}{action}";
			candidates.Add(baseName);
			for (int i = 1; i <= 10; i++)
			{
				candidates.Add($"{baseName}{i}");
			}

			foreach (string candidate in candidates)
			{
				string? path = FindPreferredSoundPath(soundsDir, candidate);
				if (string.IsNullOrEmpty(path))
					continue;

				Sound sound = Raylib.LoadSound(path);
				if (Raylib.IsSoundReady(sound))
					sounds.Add(sound);
			}

			return sounds;
		}

		private static void PlayTileSound(WorldTileData? tileData, bool isBreak)
		{
			InitializeTileSounds();
			if (tileData == null) return;

			string material = (tileData.Material ?? string.Empty).Trim().ToLowerInvariant();
			if (string.IsNullOrWhiteSpace(material)) return;

			var sounds = isBreak ? _tileBreakSounds.GetValueOrDefault(material) : _tileHitSounds.GetValueOrDefault(material);
			if (sounds == null || sounds.Count == 0) return;

			int index = Random.Shared.Next(sounds.Count);
			Sound sound = sounds[index];
			if (!Raylib.IsSoundReady(sound)) return;

			float volume = 0.45f;
			Raylib.SetSoundVolume(sound, volume);
			Raylib.SetSoundPitch(sound, GetRandomSoundPitch());
			Raylib.PlaySound(sound);
		}

		private static void InitializePlayerWalkSounds()
		{
			if (_playerWalkSoundsLoaded) return;
			_playerWalkSoundsLoaded = true;

			string soundsDir = Path.Combine("assets", "sounds");
			if (!Directory.Exists(soundsDir)) return;

			for (int i = 1; i <= 10; i++)
			{
				string? path = FindPreferredSoundPath(soundsDir, $"walk{i}");
				if (string.IsNullOrEmpty(path)) continue;

				Sound sound = Raylib.LoadSound(path);
				if (Raylib.IsSoundReady(sound))
					_playerWalkSounds.Add(sound);
			}
		}

		private static void InitializePunchSounds()
		{
			if (_punchSoundsLoaded) return;
			_punchSoundsLoaded = true;

			string soundsDir = Path.Combine("assets", "sounds");
			if (!Directory.Exists(soundsDir)) return;

			// Fichier sans suffixe (punch.mp3 ou punch.wav) + variantes numérotées
			string? directPath = FindPreferredSoundPath(soundsDir, "punch");
			if (!string.IsNullOrEmpty(directPath))
			{
				Sound direct = Raylib.LoadSound(directPath);
				if (Raylib.IsSoundReady(direct))
					_punchSounds.Add(direct);
			}

			for (int i = 1; i <= 10; i++)
			{
				string? path = FindPreferredSoundPath(soundsDir, $"punch{i}");
				if (string.IsNullOrEmpty(path)) continue;

				Sound sound = Raylib.LoadSound(path);
				if (Raylib.IsSoundReady(sound))
					_punchSounds.Add(sound);
			}
		}

		private static Sound LoadUiSound(string fileName)
		{
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
			string soundsDir = Path.Combine("assets", "sounds");
			string? path = FindPreferredSoundPath(soundsDir, fileNameWithoutExtension);
			if (string.IsNullOrEmpty(path)) return default;
			Sound s = Raylib.LoadSound(path);
			return Raylib.IsSoundReady(s) ? s : default;
		}

		/// <summary>Joue le son de survol (hover.mp3). À appeler uniquement au moment où la souris
		/// commence à survoler un bouton (transition false → true), pas à chaque frame de survol.</summary>
		private static void PlayHoverSound()
		{
			if (!_hoverSoundLoaded)
			{
				_hoverSoundLoaded = true;
				_hoverSound = LoadUiSound("hover.mp3");
			}
			if (!Raylib.IsSoundReady(_hoverSound)) return;
			Raylib.SetSoundVolume(_hoverSound, 0.35f);
			Raylib.PlaySound(_hoverSound);
		}

		/// <summary>Joue le son de clic (button_click.mp3) lorsqu'un bouton est pressé.</summary>
		private static void PlayButtonClickSound()
		{
			if (!_buttonClickSoundLoaded)
			{
				_buttonClickSoundLoaded = true;
				_buttonClickSound = LoadUiSound("button_click.mp3");
			}
			if (!Raylib.IsSoundReady(_buttonClickSound)) return;
			Raylib.SetSoundVolume(_buttonClickSound, 0.4f);
			Raylib.SetSoundPitch(_buttonClickSound, GetRandomSoundPitch());
			Raylib.PlaySound(_buttonClickSound);
		}

		private static void PlayPistolShootSound()
		{
			if (!_pistolShootSoundLoaded)
			{
				_pistolShootSoundLoaded = true;
				_pistolShootSound = LoadUiSound("pistol_shoot.mp3");
			}
			if (!Raylib.IsSoundReady(_pistolShootSound)) return;
			Raylib.SetSoundVolume(_pistolShootSound, 0.6f);
			Raylib.SetSoundPitch(_pistolShootSound, GetRandomSoundPitch());
			Raylib.PlaySound(_pistolShootSound);
		}

		private static void PlayPistolDryFireSound()
		{
			if (!_pistolDryFireSoundLoaded)
			{
				_pistolDryFireSoundLoaded = true;
				_pistolDryFireSound = LoadUiSound("pistol_dry_fire.mp3");
			}
			if (!Raylib.IsSoundReady(_pistolDryFireSound)) return;
			Raylib.SetSoundVolume(_pistolDryFireSound, 0.45f);
			Raylib.SetSoundPitch(_pistolDryFireSound, 1f);
			Raylib.PlaySound(_pistolDryFireSound);
		}

		public static void PlayEquipSound()
		{
			if (!_equipSoundLoaded)
			{
				_equipSoundLoaded = true;
				_equipSound = LoadUiSound("equip.mp3");
			}
			if (!Raylib.IsSoundReady(_equipSound)) return;
			Raylib.SetSoundVolume(_equipSound, 0.5f);
			Raylib.SetSoundPitch(_equipSound, GetRandomSoundPitch());
			Raylib.PlaySound(_equipSound);
		}

		private static void InitializeEatSounds()
		{
			if (_eatSoundsLoaded) return;
			_eatSoundsLoaded = true;
			_eatSounds.Clear();

			for (int i = 1; i <= 3; i++)
			{
				string? path = FindPreferredSoundPath(Path.Combine("assets", "sounds"), $"eat{i}");
				if (string.IsNullOrEmpty(path)) continue;
				Sound sound = Raylib.LoadSound(path);
				if (Raylib.IsSoundReady(sound))
					_eatSounds.Add(sound);
			}
		}

		private static void InitializeDrinkSounds()
		{
			if (_drinkSoundsLoaded) return;
			_drinkSoundsLoaded = true;
			_drinkSounds.Clear();

			for (int i = 1; i <= 3; i++)
			{
				string? path = FindPreferredSoundPath(Path.Combine("assets", "sounds"), $"drink{i}");
				if (string.IsNullOrEmpty(path)) continue;
				Sound sound = Raylib.LoadSound(path);
				if (Raylib.IsSoundReady(sound))
					_drinkSounds.Add(sound);
			}
		}

		private static void EnsureServerPointerLoaded()
		{
			if (_serverPointerLoaded) return;
			_serverPointerLoaded = true;
			if (File.Exists("assets/gui/server_pointer.png"))
			{
				_serverPointerTex = Raylib.LoadTexture("assets/gui/server_pointer.png");
				Raylib.SetTextureFilter(_serverPointerTex, TextureFilter.Point);
			}
		}

		private static class PlayerPointerUI
		{
			private const float EDGE_MARGIN = 48f;
			private const float POINTER_SIZE = 40f;

			public static void Draw(Camera2D camera, List<LocalPlayer> players, Texture2D pointerTex)
			{
				int screenW = Raylib.GetScreenWidth();
				int screenH = Raylib.GetScreenHeight();
				Vector2 center = new Vector2(screenW / 2f, screenH / 2f);
				float halfW = screenW / 2f - EDGE_MARGIN;
				float halfH = screenH / 2f - EDGE_MARGIN;

				foreach (var p in players)
				{
					if (!p.Connected) continue;
					if (p.Underground != World.IsUnderground) continue;

					Vector2 screenPos = Raylib.GetWorldToScreen2D(p.Position, camera);
					bool onScreen = screenPos.X >= 0 && screenPos.X <= screenW && screenPos.Y >= 0 && screenPos.Y <= screenH;
					if (onScreen) continue; // only show offscreen

					Vector2 dir = screenPos - center;
					if (dir.X == 0 && dir.Y == 0) dir = new Vector2(0, 1);

					float tx = dir.X != 0 ? halfW / MathF.Abs(dir.X) : float.MaxValue;
					float ty = dir.Y != 0 ? halfH / MathF.Abs(dir.Y) : float.MaxValue;
					float t = MathF.Min(tx, ty);

					Vector2 drawPos = center + dir * t;

					// Draw pointer texture rotated toward player
					float angle = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);
					float rotation = angle - 90f;

					Rectangle src = new Rectangle(0, 0, pointerTex.Width, pointerTex.Height);
					Rectangle dst = new Rectangle(drawPos.X, drawPos.Y, POINTER_SIZE, POINTER_SIZE);
					Vector2 origin = new Vector2(POINTER_SIZE / 2f, POINTER_SIZE / 2f);
					Raylib.DrawTexturePro(pointerTex, src, dst, origin, rotation, Color.White);

					// Draw a simple circular head inside the pointer
					float headSize = POINTER_SIZE * 0.5f;
					Vector2 headPos = drawPos; // center
					Color skin = p.SkinColorOverride ?? new Color(255, 235, 200, 255);
					Color hair = p.HairColorOverride ?? new Color(60, 40, 30, 255);
					Raylib.DrawCircle((int)headPos.X, (int)headPos.Y, headSize / 2f, skin);
					Raylib.DrawCircleLines((int)headPos.X, (int)headPos.Y, headSize / 2f, Color.Black);
					// hair as arc - simplified: draw smaller circle on top
					Raylib.DrawCircle((int)(headPos.X), (int)(headPos.Y - headSize * 0.12f), (int)(headSize * 0.38f), hair);
				}
			}
		}

		private static void PlayLevelUpSound()
		{
			if (!_levelUpSoundLoaded)
			{
				_levelUpSoundLoaded = true;
				_levelUpSound = LoadUiSound("level_up.mp3");
			}
			if (!Raylib.IsSoundReady(_levelUpSound)) return;
			Raylib.SetSoundVolume(_levelUpSound, 0.7f);
			Raylib.PlaySound(_levelUpSound);
		}

		/// <summary>Petite explosion de traits blancs verticaux qui montent autour du joueur,
		/// jouée lors d'une montée de NIVEAU GLOBAL (pas les aptitudes individuelles).</summary>
		private static void SpawnLevelUpParticles(Vector2 worldPos)
		{
			int count = Random.Shared.Next(14, 20);
			for (int i = 0; i < count; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float radius = (float)(Random.Shared.NextDouble() * 26);
				Vector2 offset = new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * 0.5f);
				Vector2 pos = worldPos + offset - new Vector2(0, 10);

				float riseSpeed = Random.Shared.Next(40, 90);
				Vector2 velocity = new Vector2((float)(Random.Shared.NextDouble() - 0.5) * 10f, -riseSpeed);

				float width = 2f + (float)Random.Shared.NextDouble() * 1.5f;
				float height = 8f + (float)Random.Shared.NextDouble() * 10f;
				float lifetime = 0.5f + (float)Random.Shared.NextDouble() * 0.4f;

				_particles.Add(new LevelUpStreakParticle(pos, velocity, Color.White, width, height, lifetime));
			}
		}

		private static void SpawnWateringCanParticles(Vector2 worldPos)
		{
			int count = Random.Shared.Next(20, 35);
			for (int i = 0; i < count; i++)
			{
				float horizontalSpread = (float)(Random.Shared.NextDouble() * 12f - 6f);
				float verticalOffset = -(float)(Random.Shared.NextDouble() * 60f + 40f);
				Vector2 startPos = worldPos + new Vector2(horizontalSpread, verticalOffset);
				Vector2 targetPos = worldPos + new Vector2((float)(Random.Shared.NextDouble() * 10f - 5f), 2f + (float)Random.Shared.NextDouble() * 2f);
				Vector2 toTarget = targetPos - startPos;
				float distance = MathF.Max(1f, toTarget.Length());
				Vector2 direction = toTarget / distance;
				Vector2 velocity = new Vector2(direction.X * (60f + Random.Shared.Next(0, 20)), 120f + Random.Shared.Next(0, 25));
				float size = 1.2f + (float)Random.Shared.NextDouble() * 1.2f;
				float lifetime = 0.45f + (float)Random.Shared.NextDouble() * 0.1f;
				var particle = new Particle(startPos, velocity, new Color(130, 190, 255, 220), size, lifetime);
				particle.GravityY = 240f;
				particle.Drag = 0.985f;
				particle.TargetPosition = targetPos;
				particle.UseTargetLanding = true;
				_particles.Add(particle);
			}
		}

		private static void SpawnFertilizerParticles(Vector2 worldPos)
			{
				int count = Random.Shared.Next(12, 22);
				for (int i = 0; i < count; i++)
				{
					float ang = (float)(Random.Shared.NextDouble() * Math.PI * 2);
					float speed = (float)(Random.Shared.NextDouble() * 120f + 40f);
					Vector2 velocity = new Vector2(MathF.Cos(ang) * speed, MathF.Sin(ang) * speed * 0.5f - 30f);
					Vector2 startPos = worldPos + new Vector2((float)(Random.Shared.NextDouble() * 6f - 3f), (float)(Random.Shared.NextDouble() * 6f - 3f));
					float size = 1.6f + (float)Random.Shared.NextDouble() * 1.2f;
					float lifetime = 0.6f + (float)Random.Shared.NextDouble() * 0.4f;
					Color brown = new Color(120, 80, 40, 220);
					var particle = new Particle(startPos, velocity, brown, size, lifetime);
					particle.GravityY = 320f;
					particle.Drag = 0.96f;
					particle.GroundY = worldPos.Y + TileSize/2f;
					_particles.Add(particle);
				}
				// Petite gerbe centrale
				for (int i = 0; i < 8; i++)
				{
					float horizontal = (float)(Random.Shared.NextDouble() * 16f - 8f);
					float vertical = (float)(Random.Shared.NextDouble() * -10f - 6f);
					var p = new Particle(worldPos + new Vector2(horizontal, vertical), new Vector2((float)(Random.Shared.NextDouble() * 30f - 15f), -80f + (float)Random.Shared.NextDouble() * -30f), new Color(100, 60, 30, 230), 6f, 0.5f + (float)Random.Shared.NextDouble() * 0.3f);
					p.GravityY = 260f;
					p.Drag = 0.92f;
					_particles.Add(p);
				}
			}

		private static void PlayAchievementSound()
		{
			if (!_achievementSoundLoaded)
			{
				_achievementSoundLoaded = true;
				_achievementSound = LoadUiSound("achievement.mp3");
			}
			if (!Raylib.IsSoundReady(_achievementSound)) return;
			Raylib.SetSoundVolume(_achievementSound, 0.6f);
			Raylib.PlaySound(_achievementSound);
		}

		private static void PlayHungrySound()
		{
			if (!_hungrySoundLoaded)
			{
				_hungrySoundLoaded = true;
				_hungrySound = LoadUiSound("hungry.mp3");
			}
			if (!Raylib.IsSoundReady(_hungrySound)) return;
			Raylib.SetSoundVolume(_hungrySound, 0.5f);
			Raylib.PlaySound(_hungrySound);
		}

		private static void PlayFertilizerSound()
		{
			if (!_fertilizerSoundLoaded)
			{
				_fertilizerSoundLoaded = true;
				_fertilizerSound = LoadUiSound("fertilizer.mp3");
			}
			if (!Raylib.IsSoundReady(_fertilizerSound)) return;
			Raylib.SetSoundVolume(_fertilizerSound, 0.6f);
			Raylib.SetSoundPitch(_fertilizerSound, GetRandomSoundPitch());
			Raylib.PlaySound(_fertilizerSound);
		}

		private static Texture2D LoadEyeShieldTexture()
		{
			if (_eyeShieldTextureLoaded && _eyeShieldTexture.Id != 0)
				return _eyeShieldTexture;

			if (File.Exists("assets/gui/eye_shield.png"))
				_eyeShieldTexture = Raylib.LoadTexture("assets/gui/eye_shield.png");

			_eyeShieldTextureLoaded = true;
			return _eyeShieldTexture;
		}

		public static void SetPlayerPosition(Vector2 newPos)
		{
			if (float.IsNaN(newPos.X) || float.IsNaN(newPos.Y) ||
				float.IsInfinity(newPos.X) || float.IsInfinity(newPos.Y))
			{
				// Réinitialiser à une position connue (spawn du monde)
				newPos = _worldSpawnPos;
				Console.WriteLine(" Position invalide détectée – réinitialisation au spawn");
			}
			_playerPos = newPos;
		}

		//  Mémorise si le personnage restauré (welcome.Restore) a été appliqué, pour choisir
		// le bon message de bienvenue dans OnNetworkWorldSyncComplete (le WorldSync qui suit
		// ne transporte pas cette information).
		static bool _pendingWelcomeHadRestore = false;

		//  MULTIJOUEUR : reçu une seule fois côté client, juste après que NetworkManager a
		// appliqué le "WorldSync" complet (chunks/entités/objets/énergie) — ou après le délai
		// de secours si l'hôte n'en a pas envoyé. C'est seulement à ce moment que le client
		// bascule réellement en jeu, pour jouer sur un pied d'égalité avec l'hôte dès la
		// première frame au lieu de voir le monde se remplir progressivement sous ses yeux.
		public static void OnNetworkWorldSyncComplete()
		{
			_isMultiplayerMenu = false;
			_isHostSetup = false;
			_isJoinSetup = false;
			_mpWaitingForWorld = false;
			_gameState = GameState.Playing;

			AddNotification(_pendingWelcomeHadRestore
				? new Notification(" Connecté ! Vous retrouvez votre personnage.", new Color(120, 220, 140, 255), 3f)
				: new Notification(" Connecté ! Bienvenue dans la partie.", new Color(120, 220, 140, 255), 3f));
		}

		// ════════════════════════════════════════════════════════════════
		//  MULTIJOUEUR : persistance du personnage d'un joueur invité
		// ════════════════════════════════════════════════════════════════

		private static string NetworkPlayerRegistryPath(string worldName) =>
			Path.Combine("Saves", $"{worldName}_players.json");

		private static void SaveNetworkPlayerRegistryToDisk()
		{
			if (string.IsNullOrEmpty(_networkPlayerRegistryWorldName)) return;
			try
			{
				string json = JsonSerializer.Serialize(_networkPlayerRegistry);
				File.WriteAllText(NetworkPlayerRegistryPath(_networkPlayerRegistryWorldName), json);
			}
			catch (Exception ex)
			{
				Console.WriteLine($" Impossible d'enregistrer les personnages invités : {ex.Message}");
			}
		}

		//  Host : enregistre/actualise le personnage d'un invité et le persiste aussitôt
		// sur le disque (fréquence faible - toutes les ~8s par joueur - donc sans souci).
		public static void UpsertNetworkPlayerRecord(string guid, GuestPlayerSaveData data)
		{
			if (string.IsNullOrEmpty(guid)) return;
			data.LastSeenUtc = DateTime.UtcNow.ToString("o");
			_networkPlayerRegistry[guid] = data;
			SaveNetworkPlayerRegistryToDisk();
		}

		//  Host : renvoie le personnage sauvegardé d'un joueur, ou null s'il n'a jamais
		// visité ce monde (premier passage → comportement de bienvenue habituel).
		public static GuestPlayerSaveData? TryGetNetworkPlayerRecord(string guid)
		{
			if (string.IsNullOrEmpty(guid)) return null;
			return _networkPlayerRegistry.TryGetValue(guid, out var data) ? data : null;
		}

		static bool SaveExists(string saveName) => File.Exists(Path.Combine("Saves", $"{saveName}.json"));

		static bool TryStartNewGame(string saveName)
		{
			if (!SaveSystem.IsValidSaveName(saveName))
			{
				_creationError = "Nom invalide : caractères interdits ou espace/point final.";
				return false;
			}
			if (SaveExists(saveName))
			{
				_creationError = "Ce nom existe déjà !";
				return false;
			}
			_creationError = "";
			_currentSaveName = saveName;
			StartNewGame();
			return true;
		}

		// Contenu visuel commun (fond noir + logo/texte + spinner + barre de progression),
        // dessiné avec l'opacité `alpha` fournie par l'appelant selon la phase de fondu.
        static void DrawLoadingOverlayContent(byte alpha)
        {
            Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color((byte)10, (byte)12, (byte)18, alpha));

            string title = Localization.Get("loading.world");
            int titleSize = 28;
            int titleWidth = Raylib.MeasureText(title, titleSize);
            FontManager.DrawText(title, ScreenWidth / 2 - titleWidth / 2, ScreenHeight / 2 - 40, titleSize,
                new Color((byte)230, (byte)230, (byte)230, alpha));

            // Petit spinner circulaire animé.
            int spinnerRadius = 14;
            Vector2 spinnerCenter = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f + 20);
            float spinnerAngle = _loadingSpinnerTime * 320f;
            Raylib.DrawRing(spinnerCenter, spinnerRadius - 4, spinnerRadius, spinnerAngle, spinnerAngle + 260f, 24,
                new Color((byte)120, (byte)190, (byte)255, alpha));

            // Barre de progression basée sur les chunks restants (visible pendant le Working).
            if (_loadingChunksTotal > 0 && _pendingChunkLoads != null)
            {
                int remaining = _pendingChunkLoads.Count;
                float progress = 1f - (float)remaining / _loadingChunksTotal;
                int barWidth = 320, barHeight = 8;
                int barX = ScreenWidth / 2 - barWidth / 2;
                int barY = ScreenHeight / 2 + 60;
                Raylib.DrawRectangle(barX, barY, barWidth, barHeight, new Color((byte)40, (byte)40, (byte)50, alpha));
                Raylib.DrawRectangle(barX, barY, (int)(barWidth * progress), barHeight, new Color((byte)100, (byte)200, (byte)140, alpha));
            }

            if (!string.IsNullOrEmpty(_loadingError))
            {
                string err = string.Format(Localization.Get("loading.error"), _loadingError);
                int errWidth = Raylib.MeasureText(err, 14);
                FontManager.DrawText(err, ScreenWidth / 2 - errWidth / 2, ScreenHeight / 2 + 90, 14,
                    new Color((byte)220, (byte)90, (byte)90, alpha));
            }
        }

		private static void RegenerateAllRoofs()
        {
            Console.WriteLine("Vérification des toits...");

            var surfaceChunks = World.GetAllSurfaceChunks();
            var caveChunks = World.GetAllCaveChunks();
            int roofsFound = 0;

            foreach (var (chunkX, chunkY, chunkData) in surfaceChunks)
            {
                foreach (var (pos, overlayId) in chunkData.Overlays)
                {
                    if (overlayId == 82)
                    {
                        roofsFound++;
                    }
                }
            }

            foreach (var (chunkX, chunkY, chunkData) in caveChunks)
            {
                foreach (var (pos, overlayId) in chunkData.Overlays)
                {
                    if (overlayId == 82)
                    {
                        roofsFound++;
                    }
                }
            }

            Console.WriteLine($"   {roofsFound} toits trouvés dans les chunks chargés");

            World.ForceRebuildBuildingIds();
        }

		private static void SpawnTileBreakParticles(Vector2 worldPos, Color tileColor)
		{
			int particleCount = Random.Shared.Next(6, 12);
			for (int i = 0; i < particleCount; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float speed = Random.Shared.Next(50, 150);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				
				float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * 20f;
				float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * 15f;
				Vector2 pos = new Vector2(worldPos.X + offsetX, worldPos.Y + offsetY);
				
				float size = Random.Shared.Next(3, 7);
				float lifetime = Random.Shared.Next(400, 800) / 1000f;
				
				// Ajouter une légère variation de couleur
				Color particleColor = new Color(
					(byte)(tileColor.R * (0.7f + (float)Random.Shared.NextDouble() * 0.3f)),
					(byte)(tileColor.G * (0.7f + (float)Random.Shared.NextDouble() * 0.3f)),
					(byte)(tileColor.B * (0.7f + (float)Random.Shared.NextDouble() * 0.3f)),
					(byte)200
				);
				
				_particles.Add(new Particle(pos, velocity, particleColor, size, lifetime));
			}
			
			// Ajouter un peu de fumée/poussière
			int dustCount = Random.Shared.Next(3, 6);
			for (int i = 0; i < dustCount; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float speed = Random.Shared.Next(30, 80);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				
				float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * 25f;
				float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * 20f;
				Vector2 pos = new Vector2(worldPos.X + offsetX, worldPos.Y + offsetY);
				
				float size = Random.Shared.Next(5, 10);
				float lifetime = Random.Shared.Next(500, 900) / 1000f;
				Color dustColor = new Color(100, 90, 80, 150);
				
				_particles.Add(new Particle(pos, velocity, dustColor, size, lifetime));
			}
		}

		static bool ShouldSaveCurrentGameOnExit()
		{
			return (!string.IsNullOrEmpty(_currentSaveName) || !string.IsNullOrEmpty(World.CurrentSaveName)) && !NetworkManager.IsClient;
		}

		static void SaveCurrentGameOnExit()
		{
			if (!ShouldSaveCurrentGameOnExit()) return;
			SaveCurrentGame();
			ShowSavingIcon();
		}

		private static void SpawnRockBreakParticles(Vector2 playerPos)
		{
			int ts = Program.TileSize;
			int playerTileX = (int)(playerPos.X / ts);
			int playerTileY = (int)(playerPos.Y / ts);
			int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
			float playerYOffset = -playerHeight * ts / 4;
			Vector2 playerVisualPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset);

			int particleCount = 8;
			for (int i = 0; i < particleCount; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float speed = Random.Shared.Next(50, 150);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * 30f;
				float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * 20f - 10f;
				Vector2 pos = new Vector2(playerVisualPos.X + offsetX, playerVisualPos.Y + offsetY);
				float size = Random.Shared.Next(3, 7);
				float lifetime = Random.Shared.Next(300, 600) / 1000f;
				Color color = new Color(120, 100, 80, 200);
				_particles.Add(new Particle(pos, velocity, color, size, lifetime));
			}
		}

		static void LoadMenuAssets()
		{
			Console.WriteLine("┌─ MENU PRINCIPAL ──────────────────────────────────────────────────────┐");
			_menuClouds = TryLoad("assets/gui/menu_clouds.png");
			_menuBack = TryLoad("assets/gui/menu_back.png");
			_menuCliff = TryLoad("assets/gui/menu_cliff.png");
			_menuTraveler = TryLoad("assets/gui/menu_traveler.png");
			_menuTitle = TryLoad("assets/gui/title.png");
			_characterSelectionBackgrounds = new[]
			{
				TryLoad("assets/gui/character_selection_background1.png"),
				TryLoad("assets/gui/character_selection_background2.png"),
				TryLoad("assets/gui/character_selection_background3.png")
			};
			_characterSelectionBackground = _characterSelectionBackgrounds[0];
			_characterSelectionBox = TryLoad("assets/gui/character_selection_box.png");
			_mapMarkerTexture = TryLoad("assets/gui/map_marker.png");
			bool allLoaded = _menuClouds.Id != 0 && _menuBack.Id != 0 && _menuCliff.Id != 0 && _menuTraveler.Id != 0 && _menuTitle.Id != 0;
			if (allLoaded) Console.WriteLine("   Tous les assets du menu chargés avec succès !");
			else
			{
				if (_menuClouds.Id == 0) Console.WriteLine("   menu_clouds.png manquant");
				if (_menuBack.Id == 0) Console.WriteLine("   menu_back.png manquant");
				if (_menuCliff.Id == 0) Console.WriteLine("   menu_cliff.png manquant");
				if (_menuTraveler.Id == 0) Console.WriteLine("   menu_traveler.png manquant");
				if (_menuTitle.Id == 0) Console.WriteLine("   title.png manquant");
			}
			Console.WriteLine();
			_creationMenuBg = TryLoad("assets/gui/creation_menu.png");
			if (_creationMenuBg.Id == 0 || _creationMenuBg.Width == 0 || _creationMenuBg.Height == 0) Console.WriteLine("   creation_menu.png manquant ou invalide, fond uni utilisé");
			else Console.WriteLine($"   creation_menu.png chargé ({_creationMenuBg.Width}x{_creationMenuBg.Height})");

			// Barre d'onglets du créateur de personnage
			_creatorBarLeft = TryLoad("assets/gui/creator_bar_left.png");
			_creatorBarMid = TryLoad("assets/gui/creator_bar_mid.png");
			_creatorBarRight = TryLoad("assets/gui/creator_bar_right.png");
			_creatorIconHair = TryLoad("assets/gui/creator_icon_hair.png");
			_creatorIconBeard = TryLoad("assets/gui/creator_icon_beard.png");
			_creatorIconSkin = TryLoad("assets/gui/creator_icon_skin.png");
			_creatorIconEyes = TryLoad("assets/gui/creator_icon_eyes.png");
			_creatorIconName = TryLoad("assets/gui/creator_icon_name.png");
			_creatorRandomIcon = TryLoad("assets/gui/random.png");
		_buttonToggleOn = TryLoad("assets/gui/button_toggle_on.png");
		_buttonToggleOff = TryLoad("assets/gui/button_toggle_off.png");
		_worldSettingsIconWeaponsBreak = TryLoad("assets/gui/world_settings_weapons_break.png");
		_worldSettingsIconFoodSpoil = TryLoad("assets/gui/world_settings_food_spoil.png");

			_mainMenuGearIcon = TryLoad("assets/gui/gear.png");
			if (_mainMenuGearIcon.Id == 0) Console.WriteLine("   gear.png manquant (bouton paramètres du menu principal)");
			_mainMenuChangelogIcon = TryLoad("assets/gui/changelog.png");
			_mainMenuCreateWorldIcon = TryLoad("assets/gui/create_world.png");
			if (_mainMenuCreateWorldIcon.Id == 0) Console.WriteLine("   create_world.png manquant (bouton créer un monde)");
			_mainMenuLoadFileIcon = TryLoad("assets/gui/loadfile.png");
			_mainMenuSaveFileIcon = TryLoad("assets/gui/savefile.png");
			_mainMenuJoinWorldIcon = TryLoad("assets/gui/join_world.png");
			if (_mainMenuJoinWorldIcon.Id == 0) Console.WriteLine("   join_world.png manquant (élément rejoindre une partie du carrousel)");
			_carouselSaveBase = TryLoad("assets/gui/save_base.png");
			if (_carouselSaveBase.Id == 0) Console.WriteLine("   save_base.png manquant (socle sous les personnages du carrousel)");
			_carouselArrowIcon = TryLoad("assets/gui/arrow.png");
			if (_carouselArrowIcon.Id == 0) Console.WriteLine("   arrow.png manquant (flèches du carrousel)");
			_worldDeleteIcon = TryLoad("assets/gui/bin.png");
			_achievementsMenuIcon = TryLoad("assets/gui/achivements.png");
			if (_worldDeleteIcon.Id == 0) Console.WriteLine("   bin.png manquant (bouton supprimer un monde)");
		}

		//  MULTIJOUEUR : côté client, on ne possède pas d'objet Entity (Program.entities est
		// vide) - on ne peut donc pas stocker la créature portée comme le fait le host avec
		// _carriedCreature. On garde juste son identifiant réseau et son espèce (utile pour la
		// vérification de collision locale au moment de la dépose) ; le host reste seul maître
		// de la position réelle de l'entité pendant qu'elle est portée.
		private static string? _clientCarriedNetId = null;

		private static void MarkExploredAreaAroundPlayer(Vector2 playerPos)
		{
			int ts = Program.TileSize;
			int centerX = (int)(playerPos.X / ts);
			int centerY = (int)(playerPos.Y / ts);
			int radius = 20; // rayons de tuiles explorées autour du joueur

			for (int dx = -radius; dx <= radius; dx++)
			{
				for (int dy = -radius; dy <= radius; dy++)
				{
					int x = centerX + dx;
					int y = centerY + dy;
					World.MarkTileExplored(x, y);
				}
			}
		}

		private static void LoadBiomeBorderTextures()
		{

			// Dictionnaire : ID du sol → (nom du biome, priorité)
			var biomes = new Dictionary<int, (string name, int priority)>
			{
				{ 1, ("grass", 4) },        // Plaines
				{ 10, ("water", 50) },       // Eau
				{ 27, ("farmland", 14) },    // Terre labourée
				{ 29, ("farmland_wet", 15) },
				{ 44, ("forest", 2) },      // Forêt
				{ 43, ("snow", 9) },        // Neige
				{ 62, ("beach_sand", 8) },        // Sable de plage
				{ 47, ("taiga", 7) },       // Taïga
				{ 46, ("savanna", 6) },     // Savane
				{ 11, ("sand", 5) },        // Sable
				{ 26, ("stone", 3) },       // Pierre / Montagne
				{ 100, ("cave", 1) },       // Grotte
				{ 101, ("lava", 1) },
				{ 102, ("crystal", 1) },
				{ 103, ("mushroom", 1) },
				{ 104, ("abyss", 1) }
			};
			
			int loadedCount = 0;
			foreach (var biome in biomes)
			{
				string borderPath = $"assets/tiles/{biome.Value.name}_border.png";
                bool loaded = World.LoadBiomeBorderTexture(biome.Key, biome.Value.name, biome.Value.priority);
                if (loaded)
                {
                    loadedCount++;
                }
			}
		}

		private static Vector2 GetPlayerCameraTarget(Vector2 playerPos)
		{
			int playerTileX = (int)(playerPos.X / TileSize);
			int playerTileY = (int)(playerPos.Y / TileSize);
			int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
			float playerYOffset = -playerHeight * TileSize / 4;
			float underwaterVisualOffset = World.IsUnderwater ? -_underwaterElevation : 0f;
			return new Vector2(playerPos.X, playerPos.Y + playerYOffset + underwaterVisualOffset);
		}

		private static void SpawnCrabCompanion()
		{
			Vector2 spawnPos = _playerPos + new Vector2(-55f, 0f);
			Entity.SpawnKhamsinSmoke(spawnPos, 0.5f); // petit nuage de fumée à l'apparition
			var crab = new Entity(spawnPos, "Crab", false)
			{
				IsTamed = true,
				TamedBehavior = TamedAnimalMode.Follow,
				OwnerName = _currentSaveName,
				IsPetCompanion = true,
				Behavior = "tamed",
				FollowOrder = -1 // sera réattribué par UpdateTamedAnimalTargets
			};
			entities.Add(crab);
			_crabCompanion = crab;
		}

		private static Entity? FindTamedAnimalUnderMouse(Camera2D camera)
		{
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			if (NetworkManager.IsClient)
			{
				EntityDto? closestSlime = null;
				float bestSlimeDistance = 40f;
				foreach (var dto in NetworkManager.GetRemoteEntities())
				{
					if (!dto.IsTamed || dto.IsHostilePet || dto.IsCarried
						|| !string.Equals(dto.Species, "slime", StringComparison.OrdinalIgnoreCase)
						|| !string.Equals(dto.OwnerName, LocalPlayerName, StringComparison.OrdinalIgnoreCase)) continue;
					int remoteTileX = (int)(dto.PosX / TileSize);
					int remoteTileY = (int)(dto.PosY / TileSize);
					float visualY = dto.PosY - World.GetHeightAt(remoteTileX, remoteTileY) * TileSize / 4f;
					float distance = Vector2.Distance(mouseWorld, new Vector2(dto.PosX, visualY));
					if (distance >= bestSlimeDistance) continue;
					bestSlimeDistance = distance;
					closestSlime = dto;
				}
				return closestSlime != null ? CreateSlimeProxyFromSnapshot(closestSlime) : null;
			}

			float bestDist = 40f; // rayon de détection
			Entity? closest = null;

			foreach (var entity in entities)
			{
				if (!entity.IsAlive) continue;
				if (!entity.IsTamed) continue;           // ← ne garder que les animaux apprivoisés
				//  Idem : exclure les animaux offerts à un PNJ, ce ne sont plus ceux du joueur.
				if (!string.Equals(entity.OwnerName, _currentSaveName, StringComparison.Ordinal)) continue;
				if (entity.Species == "human") continue; // ignorer les humains

				// Position visuelle de l'entité (avec hauteur)
				int tileX = (int)(entity.WorldPos.X / TileSize);
				int tileY = (int)(entity.WorldPos.Y / TileSize);
				int height = World.GetHeightAt(tileX, tileY);
				float yOffset = -height * TileSize / 4;
				Vector2 visualPos = new Vector2(entity.WorldPos.X, entity.WorldPos.Y + yOffset);

				float dist = Vector2.Distance(mouseWorld, visualPos);
				if (dist < bestDist)
				{
					bestDist = dist;
					closest = entity;
				}
			}
			return closest;
		}

		private static Entity CreateSlimeProxyFromSnapshot(EntityDto dto)
		{
			var proxy = new Entity(new Vector2(dto.PosX, dto.PosY), dto.Species, false)
			{
				NetId = Guid.TryParse(dto.NetId, out var netId) ? netId : Guid.Empty,
				IsTamed = dto.IsTamed,
				IsHostilePet = dto.IsHostilePet,
				OwnerName = dto.OwnerName,
				Scale = dto.Scale,
				SlimeIncubationSeconds = dto.SlimeIncubationSeconds
			};
			if (dto.SlimeStorageSlot != null)
				SaveSystem.RestoreInventorySlotFromSave(dto.SlimeStorageSlot, proxy.GetSlimeStorageSlot());
			return proxy;
		}

		private static Vector2 FindWaterSurface(Vector2 worldPos)
		{
			int ts = TileSize;
			int startX = (int)(worldPos.X / ts);
			int startY = (int)(worldPos.Y / ts);
			for (int y = startY - 5; y <= startY + 5; y++)
			{
				for (int x = startX - 5; x <= startX + 5; x++)
				{
					int groundId = World.GetGroundTileIdAt(x, y);
					if (groundId == 10 || groundId == 11) // eau
					{
						int height = World.GetHeightAt(x, y);
						float yOffset = -height * ts / 4;
						float waterSurfaceY = y * ts + yOffset + ts / 2f;
						return new Vector2(x * ts + ts / 2f, waterSurfaceY);
					}
				}
			}
			return Vector2.Zero;
		}

		private static void SpawnPlayerFootstepParticle()
		{
			int tileX = (int)(_playerPos.X / TileSize);
			int tileY = (int)(_playerPos.Y / TileSize);
			int groundId = World.GetGroundTileIdAt(tileX, tileY);
			Color particleColor = World.GetParticleColorForGround(groundId);
			
			int height = World.GetHeightAt(tileX, tileY);
			float yOffset = -height * TileSize / 4;
			Vector2 footPos = new Vector2(_playerPos.X, _playerPos.Y + yOffset + FeetOffsetY);
			
			Random rand = new Random();
			int count = rand.Next(1, 3);
			for (int i = 0; i < count; i++)
			{
				Vector2 vel = new Vector2(
					(float)(rand.NextDouble() - 0.5) * 40f,
					-(float)(rand.NextDouble() * 30f + 10f)
				);
				float size = rand.Next(2, 5);
				float lifetime = rand.Next(300, 600) / 1000f;
				_particles.Add(new Particle(footPos, vel, particleColor, size, lifetime));
			}
		}

		public static float GetDarknessAlpha()
		{
			if (World.IsUnderground)
				return 1.0f;
			
			float dayProgress = (_gameTime % DAY_CYCLE_DURATION) / DAY_CYCLE_DURATION;
			float factor = 0.5f + 0.5f * MathF.Cos(2f * MathF.PI * dayProgress);
			return MIN_DARKNESS + (MAX_DARKNESS - MIN_DARKNESS) * factor;
		}

		//  FILTRE COLORIMÉTRIQUE JOUR/NUIT ────────────────────────────────────────────
		// Léger voile de couleur plaqué sur tout l'écran, qui évolue avec l'heure en jeu
		// (_gameTime / DAY_CYCLE_DURATION, exactement le même référentiel que
		// GetDarknessAlpha) : bleu-violet la nuit, rose saumon plus soutenu au petit
		// matin (la "rosée"), très léger orange au coucher du soleil, et rien en plein
		// jour. Purement cosmétique, indépendant du masque de lumière (DrawLighting,
		// qui assombrit/éclaire) : ceci ne fait qu'ajouter une teinte par-dessus.
		//
		// Chaque point-clé est un couple (dayProgress, Color) où dayProgress suit la
		// même convention que GetDarknessAlpha : 0 = minuit, 0.25 = ~6h du matin,
		// 0.5 = midi, 0.75 = ~18h, 1 = minuit suivant. La couleur est interpolée
		// linéairement entre les deux points-clés qui encadrent l'heure actuelle, ce
		// qui donne une transition parfaitement fluide (frame par frame, jamais de
		// saut) au fil de la journée en jeu.
		private static readonly (float t, Color color)[] _dayNightFilterKeyframes = new (float, Color)[]
		{
			// Coeur de nuit : bleu-violet
			(0.00f, new Color((byte)65,  (byte)60,  (byte)120, (byte)24)),
			// Fin de nuit, juste avant l'aube : encore bleu-violet
			(0.20f, new Color((byte)65,  (byte)60,  (byte)120, (byte)24)),
			// Petit matin / rosée : rose saumon, plus foncé (pic d'intensité de cette teinte)
			(0.27f, new Color((byte)205, (byte)95,  (byte)108, (byte)34)),
			// Matin bien avancé : la teinte rosée se dissipe
			(0.38f, new Color((byte)255, (byte)205, (byte)190, (byte)10)),
			// Plein jour : aucun filtre
			(0.50f, new Color((byte)255, (byte)255, (byte)255, (byte)0)),
			(0.62f, new Color((byte)255, (byte)255, (byte)255, (byte)0)),
			// Fin d'après-midi : l'orange du couchant apparaît doucement
			(0.70f, new Color((byte)255, (byte)205, (byte)150, (byte)9)),
			// Coucher de soleil : orange léger
			(0.78f, new Color((byte)255, (byte)178, (byte)112, (byte)18)),
			// Crépuscule : transition vers le bleu-violet de la nuit
			(0.88f, new Color((byte)110, (byte)75,  (byte)130, (byte)22)),
			// Retour à minuit (boucle avec le premier point-clé)
			(1.00f, new Color((byte)65,  (byte)60,  (byte)120, (byte)24)),
		};

		private static Color GetDayNightFilterColor(float dayProgress)
		{
			var kf = _dayNightFilterKeyframes;
			for (int i = 0; i < kf.Length - 1; i++)
			{
				if (dayProgress >= kf[i].t && dayProgress <= kf[i + 1].t)
				{
					float span = kf[i + 1].t - kf[i].t;
					float localT = span > 0.0001f ? (dayProgress - kf[i].t) / span : 0f;
					return LerpColor(kf[i].color, kf[i + 1].color, localT);
				}
			}
			// Repli (ne devrait pas arriver vu que le premier/dernier point-clé couvrent 0..1)
			return kf[0].color;
		}

		private static Color LerpColor(Color a, Color b, float t)
		{
			t = Math.Clamp(t, 0f, 1f);
			return new Color(
				(byte)(a.R + (b.R - a.R) * t),
				(byte)(a.G + (b.G - a.G) * t),
				(byte)(a.B + (b.B - a.B) * t),
				(byte)(a.A + (b.A - a.A) * t));
		}

		// Dessine le voile colorimétrique par-dessus toute la scène (coordonnées écran,
		// donc à appeler APRÈS EndMode2D, comme DrawLighting). Ne s'applique pas en
		// sous-sol/intérieur (World.IsUnderground) : un filtre calé sur l'heure du ciel
		// n'a pas de sens là où on ne voit pas le ciel.
		public static void DrawDayNightColorFilter()
		{
			if (World.IsUnderground) return;

			float dayProgress = (_gameTime % DAY_CYCLE_DURATION) / DAY_CYCLE_DURATION;
			Color tint = GetDayNightFilterColor(dayProgress);
			if (tint.A <= 0) return;

			int sw = Raylib.GetRenderWidth();
			int sh = Raylib.GetRenderHeight();
			Raylib.DrawRectangle(0, 0, sw, sh, tint);
		}

		static void InitLightMask(int width, int height)
		{
			if (_lightMask.Id != 0) Raylib.UnloadRenderTexture(_lightMask);
			_lightMask = Raylib.LoadRenderTexture(width, height);
		}

		// Force la reconstruction du masque de lumière à la bonne taille au prochain DrawLighting
		// (à appeler après tout changement de résolution / bascule plein écran).
		static void InvalidateLightMask()
		{
			if (_lightMask.Id != 0)
			{
				Raylib.UnloadRenderTexture(_lightMask);
				_lightMask = default;
			}
		}

		private static bool _lightGridPreparedForWorldDraw;
		private static (int startX, int startY, int endX, int endY) _preparedLightGridBounds;

		private static (int startX, int startY, int endX, int endY) GetLightGridBounds(Camera2D camera, int width, int height)
		{
			Vector2 topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera);
			Vector2 bottomRight = Raylib.GetScreenToWorld2D(new Vector2(width, height), camera);
			int startX = (int)Math.Floor(topLeft.X / TileSize) - 2;
			int startY = (int)Math.Floor(topLeft.Y / TileSize) - 2;
			int endX = (int)Math.Ceiling(bottomRight.X / TileSize) + 2;
			int endY = (int)Math.Ceiling(bottomRight.Y / TileSize) + 2;
			return (startX, startY, endX, endY);
		}

		private static void PrepareLightGridForWorldDraw(Camera2D camera, Vector2? playerPosition = null)
		{
			_lightGridPreparedForWorldDraw = false;
			if (!World.IsUnderground)
				return;

			int sw = Raylib.GetRenderWidth();
			int sh = Raylib.GetRenderHeight();
			_preparedLightGridBounds = GetLightGridBounds(camera, sw, sh);
			_lightSources.Clear();
			CollectLightSources(camera, _lightSources, playerPosition);
			ComputeLightGrid(_lightSources,
				_preparedLightGridBounds.startX, _preparedLightGridBounds.startY,
				_preparedLightGridBounds.endX, _preparedLightGridBounds.endY);
			_lightGridPreparedForWorldDraw = true;
		}

		static void DrawLighting(Camera2D camera, Vector2? playerPosition = null)
		{
			float darknessAlpha = GetDarknessAlpha();
			if (darknessAlpha <= 0.01f) return;
			//  On utilise la taille réelle du framebuffer (GetRenderWidth/Height) et non
			// GetScreenWidth/Height : en plein écran (notamment avec la mise à l'échelle
			// DPI de Windows), ces deux valeurs peuvent différer, ce qui laissait une bande
			// en bas de l'écran non recouverte par le masque de lumière.
			int sw = Raylib.GetRenderWidth();
			int sh = Raylib.GetRenderHeight();
			if (_lightMask.Id == 0 || _lightMask.Texture.Width != sw || _lightMask.Texture.Height != sh) InitLightMask(sw, sh);
			float ambientLight = 1.0f - darknessAlpha;
			byte ambientByte = (byte)(ambientLight * 255);

			//  NOUVEAU SYSTÈME : la lumière est calculée en coordonnées de GRILLE pure (une
			// tuile = une seule valeur, jamais de décalage Y visuel appliqué ici — ça, c'est
			// uniquement une histoire de RENDU des sprites, pas de la propagation logique).
			// On construit une petite texture (1 texel = 1 tuile), qu'on étire ensuite en un
			// seul DrawTexturePro avec filtrage bilinéaire : le GPU interpole nativement entre
			// tuiles voisines, donc plus aucune coupure/quadrillage visible.
			int ts = TileSize;
			var lightBounds = GetLightGridBounds(camera, sw, sh);
			int gridStartX = lightBounds.startX;
			int gridStartY = lightBounds.startY;
			int gridEndX = lightBounds.endX;
			int gridEndY = lightBounds.endY;

			Dictionary<(int x, int y), TileLight> lightGrid;
			if (_lightGridPreparedForWorldDraw && World.IsUnderground && lightBounds.Equals(_preparedLightGridBounds))
			{
				_lightGridPreparedForWorldDraw = false;
				lightGrid = _lightGridCache;
			}
			else
			{
				_lightGridPreparedForWorldDraw = false;
				_lightSources.Clear();
				CollectLightSources(camera, _lightSources, playerPosition);
				lightGrid = ComputeLightGrid(_lightSources, gridStartX, gridStartY, gridEndX, gridEndY);
			}

			int gridW = gridEndX - gridStartX + 1;
			int gridH = gridEndY - gridStartY + 1;
			var (texW, texH) = UpdateLightmapTexture(lightGrid, gridStartX, gridStartY, gridW, gridH, ambientByte);

			Raylib.BeginTextureMode(_lightMask);
			Raylib.ClearBackground(new Color((byte)ambientByte, (byte)ambientByte, (byte)ambientByte, (byte)255));
			// La texture basse résolution (grille logique, éventuellement regroupée par blocs de
			// "step" tuiles — voir UpdateLightmapTexture) est étirée sur exactement la zone monde
			// qu'elle couvre, puis projetée à l'écran via la caméra — le filtrage bilinéaire de la
			// texture fait le lissage, pas nous.
			Vector2 worldOrigin = new Vector2(gridStartX * ts, (gridStartY - 1) * ts);
			Vector2 worldSize = new Vector2(gridW * ts, gridH * ts);
			Vector2 screenOrigin = Raylib.GetWorldToScreen2D(worldOrigin, camera);
			Vector2 screenBottomRight = Raylib.GetWorldToScreen2D(worldOrigin + worldSize, camera);
			Rectangle destRect = new Rectangle(screenOrigin.X, screenOrigin.Y,
				screenBottomRight.X - screenOrigin.X, screenBottomRight.Y - screenOrigin.Y);
			Raylib.DrawTexturePro(_lightmapTexture,
				new Rectangle(0, 0, texW, texH), destRect, Vector2.Zero, 0f, Color.White);
			Raylib.EndTextureMode();

			Raylib.BeginBlendMode(BlendMode.Multiplied);
			Raylib.DrawTextureRec(_lightMask.Texture, new Rectangle(0, 0, sw, -sh), Vector2.Zero, Color.White);
			Raylib.EndBlendMode();
		}

		//  PERF : au lieu de laisser la texture de lumière grandir avec la zone visible (donc
		// avec le carré de 1/zoom), on plafonne sa résolution : au-delà de ce nombre de texels
		// par côté, plusieurs tuiles sont regroupées derrière un seul texel (voir "step" dans
		// UpdateLightmapTexture). Avant ce plafond, un dézoom complet pouvait faire grimper le
		// nombre de tuiles calculées/dessinées de quelques centaines à plusieurs dizaines de
		// milliers, ET la texture GPU était entièrement RECRÉÉE (Load/Unload) à chaque frame :
		// les deux effets se cumulaient et faisaient chuter le framerate à 10-20 FPS en vue
		// large. Le filtrage bilinéaire masque très bien le fait que plusieurs tuiles partagent
		// un texel, donc la perte de finesse est invisible à l'écran alors que le coût CPU/GPU
		// redevient constant, quel que soit le niveau de zoom.
		private const int MAX_LIGHTMAP_DIM = 128;

		static Texture2D _lightmapTexture;
		static readonly List<(Vector2 pos, int radius, Color color)> _lightSources = new();

		/// <summary>
		/// Reconstruit la texture de lumière à partir de la grille calculée par ComputeLightGrid,
		/// et l'uploade au GPU. Résolution plafonnée à MAX_LIGHTMAP_DIM texels par côté : si la
		/// zone visible (gridW x gridH tuiles) dépasse ce plafond, plusieurs tuiles sont
		/// regroupées derrière un seul texel au lieu du "1 texel = 1 tuile" d'avant. Ceci garde
		/// un coût constant (image plafonnée à MAX_LIGHTMAP_DIM x MAX_LIGHTMAP_DIM, recréée une
		/// fois par frame comme avant mais toujours à la même petite taille) au lieu de croître
		/// avec le carré de la zone visible — c'était ça qui faisait chuter le framerate en vue
		/// dézoomée : la texture (et le nombre de pixels dessinés dedans) grandissait avec
		/// l'inverse du carré du zoom.
		/// Les tuiles absentes de lightGrid (hors de portée de toute source) restent à la couleur
		/// ambiante (nuit/jour/sous-sol), exactement comme le fond du masque avant.
		/// Retourne (texW, texH) : la portion utile de la texture à dessiner.
		/// </summary>
		static (int texW, int texH) UpdateLightmapTexture(Dictionary<(int x, int y), TileLight> lightGrid,
			int gridStartX, int gridStartY, int gridW, int gridH, byte ambientByte)
		{
			if (gridW <= 0 || gridH <= 0) return (0, 0);

			int step = Math.Max(1, (int)Math.Ceiling(Math.Max(gridW, gridH) / (float)MAX_LIGHTMAP_DIM));
			int texW = (gridW + step - 1) / step;
			int texH = (gridH + step - 1) / step;

			Image img = Raylib.GenImageColor(texW, texH, new Color(ambientByte, ambientByte, ambientByte, (byte)255));

			if (step == 1)
			{
				// Cas courant (zoom normal/rapproché) : correspondance directe tuile <-> texel,
				// comme avant — on ne parcourt que les tuiles réellement éclairées.
				foreach (var kv in lightGrid)
				{
					int px = kv.Key.x - gridStartX;
					int py = kv.Key.y - gridStartY;
					if (px < 0 || px >= texW || py < 0 || py >= texH) continue;
					DrawLightTexel(ref img, px, py, kv.Value, ambientByte);
				}
			}
			else
			{
				// Dézoom important : un texel regroupe "step x step" tuiles. On échantillonne
				// au centre du bloc (le flou bilinéaire de la texture masque très bien le fait
				// que plusieurs tuiles partagent un texel), ce qui garde le coût de cette boucle
				// borné par MAX_LIGHTMAP_DIM², jamais par la taille réelle de la zone visible.
				for (int ty = 0; ty < texH; ty++)
				{
					for (int tx = 0; tx < texW; tx++)
					{
						int sampleX = gridStartX + tx * step + step / 2;
						int sampleY = gridStartY + ty * step + step / 2;
						if (lightGrid.TryGetValue((sampleX, sampleY), out var light))
							DrawLightTexel(ref img, tx, ty, light, ambientByte);
					}
				}
			}

			if (_lightmapTexture.Id != 0) Raylib.UnloadTexture(_lightmapTexture);
			_lightmapTexture = Raylib.LoadTextureFromImage(img);
			Raylib.SetTextureFilter(_lightmapTexture, TextureFilter.Bilinear);
			Raylib.UnloadImage(img);

			return (texW, texH);
		}

		static void DrawLightTexel(ref Image img, int px, int py, TileLight light, byte ambientByte)
		{
			// On COMBINE additivement avec l'ambiant (comme le faisait le blend additif
			// précédent) plutôt que de l'écraser, pour garder le comportement "la lumière
			// s'ajoute à la pénombre ambiante" au lieu de l'y substituer brutalement.
			byte r = (byte)Math.Clamp(ambientByte + light.Color.R * light.Intensity, 0f, 255f);
			byte g = (byte)Math.Clamp(ambientByte + light.Color.G * light.Intensity, 0f, 255f);
			byte b = (byte)Math.Clamp(ambientByte + light.Color.B * light.Intensity, 0f, 255f);
			Raylib.ImageDrawPixel(ref img, px, py, new Color(r, g, b, (byte)255));
		}

		/// <summary>
		/// Lumière (intensité 0..1 + teinte) de la tuile sur laquelle se trouve une entité/le
		/// joueur — à utiliser pour teinter DIRECTEMENT le sprite au lieu de compter sur le
		/// masque écran. Contrairement au masque (qui applique un dégradé par PIXEL d'écran,
		/// donc incohérent sur un sprite qui dépasse en hauteur sur plusieurs "cases" visuelles),
		/// ceci renvoie UNE seule valeur pour toute l'entité, basée sur sa tuile d'ORIGINE (les
		/// pieds), exactement comme demandé : une tuile, même haute, = une seule source/teinte.
		/// </summary>
		public static Color GetEntityLightTint(Vector2 worldFootPosition)
		{
			int ts = TileSize;
			int tileX = (int)Math.Floor(worldFootPosition.X / ts);
			int tileY = (int)Math.Floor(worldFootPosition.Y / ts);

			float darknessAlpha = GetDarknessAlpha();
			float ambientLight = 1f - darknessAlpha;
			byte ambientByte = (byte)Math.Clamp(ambientLight * 255f, 0f, 255f);

			if (_lightGridCache.TryGetValue((tileX, tileY), out var tileLight))
			{
				byte r = (byte)Math.Clamp(ambientByte + tileLight.Color.R * tileLight.Intensity, 0f, 255f);
				byte g = (byte)Math.Clamp(ambientByte + tileLight.Color.G * tileLight.Intensity, 0f, 255f);
				byte b = (byte)Math.Clamp(ambientByte + tileLight.Color.B * tileLight.Intensity, 0f, 255f);
				return new Color(r, g, b, (byte)255);
			}
			return new Color(ambientByte, ambientByte, ambientByte, (byte)255);
		}

		static List<Rectangle> GetOpaqueWallsInRadius(Vector2 lightPos, float radius)
		{
			int ts = TileSize;
			int minX = (int)((lightPos.X - radius) / ts) - 1;
			int minY = (int)((lightPos.Y - radius) / ts) - 1;
			int maxX = (int)((lightPos.X + radius) / ts) + 1;
			int maxY = (int)((lightPos.Y + radius) / ts) + 1;
			var walls = new List<Rectangle>();

			for (int x = minX; x <= maxX; x++)
			{
				for (int y = minY; y <= maxY; y++)
				{
					int objId = World.GetObjectIdAt(x, y);
					// Les murs sont les IDs 8 (mur de pierre) et 84 (mur de bois)
					if (objId == 8 || objId == 84)
					{
						int height = World.GetHeightAt(x, y);
						float yOffset = -height * ts / 4;
						float drawY = y * ts + yOffset;
						// Le rectangle du mur (collision approximative)
						Rectangle wallRect = new Rectangle(x * ts, drawY, ts, ts);
						walls.Add(wallRect);
					}
				}
			}
			return walls;
		}

		static Texture2D CreateHaloTexture(int size)
		{
			Image img = Raylib.GenImageColor(size, size, Color.Blank);
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
				{
					float dx = x - size/2f;
					float dy = y - size/2f;
					float dist = MathF.Sqrt(dx*dx + dy*dy) / (size/2f);
					byte alpha = (byte)(Math.Clamp(1f - dist, 0f, 1f) * 255);
					if (alpha > 0) Raylib.ImageDrawPixel(ref img, x, y, new Color((byte)255, (byte)255, (byte)255, (byte)alpha));
				}
			return Raylib.LoadTextureFromImage(img);
		}

		static void SpawnHealParticles(Vector2 playerPos)
		{
			int ts = Program.TileSize;
			int playerTileX = (int)(playerPos.X / ts);
			int playerTileY = (int)(playerPos.Y / ts);
			int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
			float playerYOffset = -playerHeight * ts / 4;
			Vector2 playerVisualPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset);
			
			int particleCount = 8;
			for (int i = 0; i < particleCount; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float speed = Random.Shared.Next(30, 100);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				
				float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * 30f;
				float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * 20f - 20f;
				Vector2 pos = new Vector2(playerVisualPos.X + offsetX, playerVisualPos.Y + offsetY);
				
				float size = Random.Shared.Next(3, 7);
				float lifetime = Random.Shared.Next(300, 600) / 1000f;
				
				Color color = new Color(100, 255, 100, 200);
				_particles.Add(new Particle(pos, velocity, color, size, lifetime));
			}
		}

		// Fond d'écran défilant commun aux deux écrans (accueil + carrousel de mondes),
		// identique au fond utilisé par l'ancien menu de sélection de sauvegarde.
		static void DrawScrollingMenuBackground(int sw, int sh, float dt)
		{
			const float SCROLL_SPEED_X = 30f;
			const float SCROLL_SPEED_Y = 15f;
			_creationBgOffsetX -= SCROLL_SPEED_X * dt;
			_creationBgOffsetY += SCROLL_SPEED_Y * dt;
			if (_creationMenuBg.Id != 0 && _creationMenuBg.Width > 0 && _creationMenuBg.Height > 0)
			{
				int tw = _creationMenuBg.Width;
				int th = _creationMenuBg.Height;
				float offsetX = _creationBgOffsetX % tw;
				if (offsetX < 0) offsetX += tw;
				float offsetY = _creationBgOffsetY % th;
				if (offsetY < 0) offsetY += th;
				for (int x = -tw; x < sw + tw; x += tw)
					for (int y = -th; y < sh + th; y += th)
						SafeDrawTexture(_creationMenuBg, new Vector2(x + (int)offsetX, y + (int)offsetY), 0, 1f, Color.White);
			}
			else Raylib.ClearBackground(new Color(20, 20, 35, 255));
		}

		//  MULTIJOUEUR : historique des serveurs déjà rejoints (IP/port), pour ne pas avoir
		// à ressaisir l'adresse à chaque fois. Stocké dans Data/servers.json, même principe
		// que KeyBindings ci-dessous pour Data/keybindings.json.
		public class SavedServerEntry
		{
			public string Name { get; set; } = "";
			public string Ip { get; set; } = "";
			public int Port { get; set; } = 7777;
			public long LastJoinedTicks { get; set; } = DateTime.UtcNow.Ticks;
		}

		private static Texture2D GetUIBarButtonTexture(string id)
		{
			if (_uiBarButtonTextures.TryGetValue(id, out var tex) && tex.Id != 0)
				return tex;

			string path = $"assets/gui/ui_bar_button_{id}.png";
			if (File.Exists(path))
			{
				tex = Raylib.LoadTexture(path);
				if (tex.Id != 0)
				{
					_uiBarButtonTextures[id] = tex;
					return tex;
				}
			}
			// Fallback : texture carrée blanche avec texte
			Image img = Raylib.GenImageColor(32, 32, Color.Blank);
			// Vous pouvez dessiner du texte ou utiliser une texture par défaut
			tex = Raylib.LoadTextureFromImage(img);
			Raylib.UnloadImage(img);
			_uiBarButtonTextures[id] = tex;
			return tex;
		}

		static void LoadAllGameData()
        {
            GameData.LoadFromFiles();
            SpeciesData.LoadFromFile();
            WorldTileRegistry.LoadFromFile();
			StructureManager.LoadStructures();
			AchievementManager.Initialize();
			RecipeSystem.Initialize();
			KeyBindings.Load();
			SavedServers.Load();
        }

		private static Texture2D TryLoadWithStatus(string path, string displayName, List<string> missingTextures, LoadStats stats)
		{
			Console.Write($"  * {displayName,-35} ");
			var texture = TryLoad(path);
			
			if (texture.Id == 0)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($" MANQUANT");
				Console.ResetColor();
				missingTextures.Add(path);
				stats.Warnings++;
			}
			else
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine($" OK");
				Console.ResetColor();
				stats.Success++;
			}
			
			return texture;
		}

		private static void LoadGuiTextures(List<string> missingTextures, LoadStats stats)
		{
			string[] selectFiles = { "select1.png", "select2.png", "select3.png", "select2.png" };
			int loaded = 0;
			
			foreach (string file in selectFiles)
			{
				var tex = TryLoad($"assets/gui/{file}");
				_selectTextures.Add(tex);
				if (tex.Id != 0) loaded++;
				else missingTextures.Add($"assets/gui/{file}");
			}
			
			LoadLanguageFlagTextures(missingTextures, stats);
			PrintInfo($"Curseurs de selection : {loaded}/{selectFiles.Length}");
		}

		private class LoadStats
		{
			public int Success { get; set; } = 0;
			public int Warnings { get; set; } = 0;
		}

		private static Entity? FindAnimalUnderMouse(Camera2D camera)
		{
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			float bestDist = 40f;
			Entity? closest = null;

			foreach (var entity in entities)
			{
				if (!entity.IsAlive) continue;
				if (entity.IsSpiritAnimal) continue;
				if (entity.Species == "human") continue;

				int tileX = (int)(entity.WorldPos.X / TileSize);
				int tileY = (int)(entity.WorldPos.Y / TileSize);
				int height = World.GetHeightAt(tileX, tileY);
				float yOffset = -height * TileSize / 4;
				Vector2 visualPos = new Vector2(entity.WorldPos.X, entity.WorldPos.Y + yOffset);

				float dist = Vector2.Distance(mouseWorld, visualPos);
				if (dist < bestDist)
				{
					bestDist = dist;
					closest = entity;
				}
			}
			return closest;
		}

		//  MULTIJOUEUR : équivalent de FindAnimalUnderMouse pour un client distant, qui ne
		// possède pas d'objets Entity simulés localement (Program.entities est vide côté
		// client) mais reçoit des snapshots EntityDto du host.
		private static EntityDto? FindAnimalUnderMouseClient(Camera2D camera)
		{
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			float bestDist = 40f;
			EntityDto? closest = null;

			foreach (var dto in NetworkManager.GetRemoteEntities())
			{
				if (dto.HP <= 0) continue;
				if (dto.IsSpiritAnimal) continue;
				if (dto.Species == "human") continue;

				int tileX = (int)(dto.PosX / TileSize);
				int tileY = (int)(dto.PosY / TileSize);
				int height = World.GetHeightAt(tileX, tileY);
				float yOffset = -height * TileSize / 4;
				Vector2 visualPos = new Vector2(dto.PosX, dto.PosY + yOffset);

				float dist = Vector2.Distance(mouseWorld, visualPos);
				if (dist < bestDist)
				{
					bestDist = dist;
					closest = dto;
				}
			}
			return closest;
		}

		static void SpawnHairParticles(Vector2 playerPos)
		{
			int ts = Program.TileSize;
			int playerTileX = (int)(playerPos.X / ts);
			int playerTileY = (int)(playerPos.Y / ts);
			int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
			float playerYOffset = -playerHeight * ts / 4;
			Vector2 playerVisualPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset);
			
			int particleCount = Random.Shared.Next(15, 30);
			for (int i = 0; i < particleCount; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float speed = Random.Shared.Next(30, 100);
				Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
				
				float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * 40f;
				float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * 30f - 20f;
				Vector2 pos = new Vector2(playerVisualPos.X + offsetX, playerVisualPos.Y + offsetY);
				
				float size = Random.Shared.Next(2, 5);
				float lifetime = Random.Shared.Next(400, 800) / 1000f;
				
				Color hairColor = new Color(60 + Random.Shared.Next(0, 40), 40 + Random.Shared.Next(0, 30), 20 + Random.Shared.Next(0, 20), 220);
				
				_particles.Add(new Particle(pos, velocity, hairColor, size, lifetime));
			}
		}

		static void LoadCarTextures()
		{
			carBodyTextures.Clear();
			wheelTextures.Clear();
			int loadedBody = 0;
			for (int i = 0; i <= 22; i++)
			{
				string path = $"assets/car/car_body{i}.png";
				var tex = TryLoad(path);
				carBodyTextures.Add(tex);
				if (tex.Id != 0) loadedBody++;
				else Console.WriteLine($"  | Texture voiture manquante: {path}");
			}
			Console.WriteLine($"  │ Voiture : {loadedBody}/22 calques chargés");
			int loadedWheels = 0;
			for (int i = 0; i <= 5; i++)
			{
				string path = $"assets/car/tyre_{i}.png";
				var tex = TryLoad(path);
				wheelTextures.Add(tex);
				if (tex.Id != 0) loadedWheels++;
				else Console.WriteLine($"   Texture roue manquante: {path}");
			}
			Console.WriteLine($"  │ Roues : {loadedWheels}/5 calques chargés");
		}

		static Texture2D TryLoad(string path) => File.Exists(path) ? Raylib.LoadTexture(path) : (missingTexture.Id != 0 ? missingTexture : new Texture2D());

		// Plus grande taille (en tuiles) parmi tous les objets du registre. Calculée une
		// seule fois et mise en cache : sert à élargir la fenêtre de recherche de
		// FindTileUnderMouse pour que les gros objets (ex. arbre 4x8) restent cliquables
		// même quand le curseur vise leur partie haute ou droite, loin de la tuile d'ancrage.
		private static int _maxObjectTileWidth = -1;

		private static int _maxObjectTileHeight = -1;

		private static void EnsureMaxObjectSizeCached()
		{
			if (_maxObjectTileWidth >= 0) return;
			_maxObjectTileWidth = 1;
			_maxObjectTileHeight = 1;
			foreach (var tileData in WorldTileRegistry.Tiles.Values)
			{
				// La fenêtre de recherche doit couvrir la HITBOX réelle (CollisionSize si
				// définie, sinon Size), pas le sprite visuel — c'est elle qui sert désormais
				// au test de clic dans FindTileUnderMouse.
				var extent = tileData.CollisionSize ?? tileData.Size;
				if (extent == null) continue;
				if (extent.Width > _maxObjectTileWidth) _maxObjectTileWidth = extent.Width;
				if (extent.Height > _maxObjectTileHeight) _maxObjectTileHeight = extent.Height;
			}
		}

		static string FindNearStation(Vector2 playerPos)
		{
			Vector2 visualPlayerPos = World.GetVisualPosition(playerPos);
			int ts = Program.TileSize;
			int centerX = (int)(playerPos.X / ts);
			int centerY = (int)(playerPos.Y / ts);
			for (int dx = -3; dx <= 3; dx++)
				for (int dy = -3; dy <= 3; dy++)
				{
					int x = centerX + dx, y = centerY + dy;
					int objectId = World.GetObjectIdAt(x, y);
					if (objectId == 30) return "etabli";
					if (objectId == 31) return "fourneau";
				}
			return "";
		}

		static void ReloadMenuTextures()
		{
		if (_menuClouds.Id != 0) { Raylib.UnloadTexture(_menuClouds); _menuClouds = new Texture2D(); }
		if (_menuBack.Id != 0) { Raylib.UnloadTexture(_menuBack); _menuBack = new Texture2D(); }
		if (_menuCliff.Id != 0) { Raylib.UnloadTexture(_menuCliff); _menuCliff = new Texture2D(); }
		if (_menuTraveler.Id != 0) { Raylib.UnloadTexture(_menuTraveler); _menuTraveler = new Texture2D(); }
		if (_menuTitle.Id != 0) { Raylib.UnloadTexture(_menuTitle); _menuTitle = new Texture2D(); }
		if (_creationMenuBg.Id != 0) { Raylib.UnloadTexture(_creationMenuBg); _creationMenuBg = new Texture2D(); }
			_menuCliff = TryLoad("assets/gui/menu_cliff.png");
			_menuTraveler = TryLoad("assets/gui/menu_traveler.png");
			_menuTitle = TryLoad("assets/gui/title.png");
			_creationMenuBg = TryLoad("assets/gui/creation_menu.png");
		}

		static Vector2 GetShootDirection(Vector2 playerPos, Camera2D camera)
		{
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			Vector2 aimOrigin = GetPlayerAimOrigin(playerPos);
			Vector2 dir = mouseWorld - aimOrigin;
			if (dir.Length() < 0.01f) dir = new Vector2(1, 0);
			return Vector2.Normalize(dir);
		}

		static void SaveAllChunksEntities()
        {
            foreach (var chunk in World.GetAllChunks()) World.SaveChunkEntities(chunk.chunkX, chunk.chunkY, entities);
        }
    }
}
