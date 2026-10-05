// Program.Misc.cs
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

		public static int ScreenWidth = 1280;

		public static int ScreenHeight = 720;

		private const float CAMERA_REFERENCE_WIDTH = 1280f;

		private const float CAMERA_REFERENCE_HEIGHT = 720f;

		private static float _cameraViewportScale = 1f;

		private static float GetCameraViewportScale(float viewportWidth, float viewportHeight)
		{
			return MathF.Min(viewportWidth / CAMERA_REFERENCE_WIDTH, viewportHeight / CAMERA_REFERENCE_HEIGHT);
		}

		private static void AdaptCameraToViewport(ref Camera2D camera, int viewportWidth, int viewportHeight)
		{
			float newScale = GetCameraViewportScale(viewportWidth, viewportHeight);
			if (_cameraViewportScale > 0f)
				camera.Zoom *= newScale / _cameraViewportScale;
			_cameraViewportScale = newScale;
		}

		public const float CharacterScale = 0.8f;

		public const float FeetOffsetY = 32f;

		public static List<Car> Cars = new List<Car>();

		// Cycle jour/nuit
		const float DAY_CYCLE_DURATION = 600f;

		const float MIN_DARKNESS = 0f;

		const float MAX_DARKNESS = 0.82f;

		static float _gameTime = 0f;

		const float MAX_TIME_ADVANCE_PER_FRAME = 3f;

		public static void SetGameTime(float gameTime)
		{
			_gameTime = ((gameTime % DAY_CYCLE_DURATION) + DAY_CYCLE_DURATION) % DAY_CYCLE_DURATION;
		}

		// secondes max appliquées par frame lors d'un skip
		
		private static float _energyUpdateTimer = 0f;

		private static float _energyUpdateInterval = 0.5f;

		public static string ActiveStation { get; set; } = "";

		// Caméra lissée (smoothing)
		private static Vector2 _smoothedCameraTarget = Vector2.Zero;

		private static double _preciseCameraTargetX;

		private static double _preciseCameraTargetY;

		private static bool _preciseCameraTargetInitialized;

		private static Vector2 _aimCameraOffset = Vector2.Zero;

		private static float _cameraSmoothingSpeed = 8f;

		private static Vector2 SmoothCameraTarget(Vector2 target, float amount)
		{
			amount = Math.Clamp(amount, 0f, 1f);
			if (!_preciseCameraTargetInitialized ||
				Math.Abs(_preciseCameraTargetX - _smoothedCameraTarget.X) > 0.01 ||
				Math.Abs(_preciseCameraTargetY - _smoothedCameraTarget.Y) > 0.01)
			{
				_preciseCameraTargetX = _smoothedCameraTarget.X;
				_preciseCameraTargetY = _smoothedCameraTarget.Y;
				_preciseCameraTargetInitialized = true;
			}

			_preciseCameraTargetX += ((double)target.X - _preciseCameraTargetX) * amount;
			_preciseCameraTargetY += ((double)target.Y - _preciseCameraTargetY) * amount;
			_smoothedCameraTarget = new Vector2(
				(float)_preciseCameraTargetX,
				(float)_preciseCameraTargetY);
			return _smoothedCameraTarget;
		}

		private static void ApplyPreciseCameraTransform(ref Camera2D camera)
		{
			double expectedAnchorX = _preciseCameraTargetInitialized ? Math.Round(_preciseCameraTargetX) : double.NaN;
			double expectedAnchorY = _preciseCameraTargetInitialized ? Math.Round(_preciseCameraTargetY) : double.NaN;
			if (!_preciseCameraTargetInitialized ||
				Math.Abs(camera.Target.X - expectedAnchorX) > 0.01 ||
				Math.Abs(camera.Target.Y - expectedAnchorY) > 0.01)
			{
				_preciseCameraTargetX = camera.Target.X;
				_preciseCameraTargetY = camera.Target.Y;
				_preciseCameraTargetInitialized = true;
			}

			double anchorX = Math.Round(_preciseCameraTargetX);
			double anchorY = Math.Round(_preciseCameraTargetY);
			camera.Target = new Vector2((float)anchorX, (float)anchorY);
			camera.Offset = new Vector2(
				ScreenWidth / 2f - (float)((_preciseCameraTargetX - anchorX) * camera.Zoom),
				ScreenHeight / 2f - (float)((_preciseCameraTargetY - anchorY) * camera.Zoom));
		}

		// Plus la valeur est basse, plus c'est fluide (5-12)
		private static bool _cameraNeedsImmediateCenter = true;

		private static List<Sound> _punchSounds = new();

		private static Sound _equipSound = default;

		private static Sound _pistolShootSound = default;

		private static Sound _pistolDryFireSound = default;

		private static List<Sound> _eatSounds = new();

		private static List<Sound> _drinkSounds = new();

		private static Sound _hungrySound = default;

		private static Sound _achievementSound = default;

		private static Sound _levelUpSound = default;

		private static Sound _fertilizerSound = default;

		private const float HUNGER_WARNING_THRESHOLD = 25f;

		// seuil (en %) sous lequel le son "faim" se joue
		private static bool _hungerWarningPlayed = false;

		private static GameState _pausePendingState = GameState.Playing;

		private static Action _pausePendingAction = null;

		// Effet de secousse d'écran
		static float _shakeIntensity = 0f;

		static float _shakeDuration = 0f;

		static Vector2 _shakeOffset = Vector2.Zero;

		static HashSet<string> destroyedObjects = new();

		static List<Notification> activeNotifications = new();

		public static List<Entity> entities = new();

		// Mettre à false pour suspendre la reproduction animale temporairement
		public static bool AnimalBreedingEnabled = false;

		public static bool staminaLocked = false;

		// true tant que la barre n'est pas remontée à 100%
        private const float STAMINA_DRAIN_PER_SEC = 12f;

		// vitesse de vidage en sprintant
        private const float STAMINA_REGEN_DELAY = 1f;

		// délai avant de recommencer à remonter après le dernier sprint
        private const float STAMINA_REGEN_PER_SEC = 20f;

		// vitesse de remontée (quelques secondes pour se remplir)
        private static float _staminaRegenTimer = 0f;

		private const float HUNGER_DRAIN_PER_SEC = 100f / (60f * 30f);

		// vide en ~12 minutes de jeu
        private const float THIRST_DRAIN_PER_SEC = 100f / (60f * 20f);

		private static float _speedPotionTimer = 0f;

		private const float SPEED_POTION_SPEED_MULTIPLIER = 1.35f;

		static float _ragdollMinDelay = 0f;

		public static bool HasDiedAtLeastOnce = false;

		//  Message dédié pour la faim/soif (pas d'"agresseur" à proprement parler).
        private const string _starvationDeathMessage = "{0} s'est éteint, épuisé par la faim et la soif, loin de tout secours.";

		public static string BuildDeathMessage(string victimName, string killerName, DeathCause cause)
        {
            if (cause == DeathCause.Starvation)
                return string.Format(_starvationDeathMessage, victimName);

            var matches = _deathMessageTemplates.Where(t => t.causes.Contains(cause)).ToList();
            var pool = matches.Count > 0 ? matches : _deathMessageTemplates.ToList();
            var chosen = pool[Random.Shared.Next(pool.Count)];
            return string.Format(chosen.template, victimName, killerName);
        }

		static string _deathVictimName = "Vous";

		static string _deathMessage = "";

		static DeathScreenPhase _deathScreenPhase = DeathScreenPhase.Vignette;

		static float _deathScreenPhaseTimer = 0f;

		static int _deathMessageCharsShown = 0;

		static float _deathMessageCharTimer = 0f;

		const float VIGNETTE_DURATION = 1.6f;

		// temps pour que les bords s'assombrissent
        const float SKULL_POP_DURATION = 0.45f;

		// durée de l'impulsion du crâne
        const float CHAR_REVEAL_INTERVAL = 0.028f;

		static bool _fullscreen = false;

		static int _masterVolume = 100;

		static int _selectedOption = 0;

		static int _languageSelectionIndex = 0;

		static float _animProg = 0f;

		static int _animFrame = 0;

		static string _animState = "idle";

		static float _animRawProg = 0f;

		static bool _animReversed = false;

		enum CaveTransitionPhase { None, MoveToCenter, CloseBars, Closed, OpenBars }

		static CaveTransitionPhase _caveTransitionPhase = CaveTransitionPhase.None;

		static float _caveTransitionTimer = 0f;

		static Vector2 _caveTransitionStartPos = Vector2.Zero;

		static Vector2 _caveTransitionCenterPos = Vector2.Zero;

		static Vector2 _caveTransitionSpawnPos = Vector2.Zero;

		static bool _caveTransitionEnterUnderground = false;

		static bool _caveTransitionWasLeavingCave = false;

		static float _caveTransitionBarProgress = 0f;

		static bool _caveTransitionHasSwitchedZone = false;

		private const float CAVE_TRANSITION_MOVE_DURATION = 0.80f;

		private const float CAVE_TRANSITION_CLOSE_DURATION = 0.50f;

		private const float CAVE_TRANSITION_HOLD_DURATION = 0.20f;

		private const float CAVE_TRANSITION_OPEN_DURATION = 0.50f;

		private static int _bloodEssence = 0;

		// Pendings pour la flûte (suppression différée après lecture du son)
		private static List<float> _pendingFluteRemovals = new List<float>();

		// écran de configuration de l'hébergement
		static bool _isJoinSetup = false;

		//  MULTIJOUEUR : écran "Choisir un personnage", affiché après validation d'IP/pseudo/
		// port sur l'écran "Rejoindre" et avant la connexion effective (voir DrawMultiplayerJoin
		// et le handler de clic associé dans Program_Core.cs).
		static bool _isCharacterSelect = false;

		static string _pendingJoinIp = "";

		static int _pendingJoinPort = 7777;

		static bool _isWorldLookupPending = false;

		static Task<WorldInfoMsg?>? _joinWorldLookupTask = null;

		static string _pendingJoinWorldName = "";

		static List<CharacterProfile> _joinCharacterOptions = new();

		//  Rectangles des lignes cliquables de l'écran de choix du personnage, recalculés
		// à chaque frame de dessin, lus par le gestionnaire de clic de la même frame.
		static readonly List<Rectangle> _characterSelectRowRects = new();

		//  MULTIJOUEUR : quand le joueur choisit "+ Nouveau personnage" sur l'écran de
		// choix du personnage, on ne rejoint plus directement le serveur : on rappelle
		// l'écran de personnalisation (celui normalement utilisé pour créer un monde,
		// voir DrawCreateWorldMenu) pour lui laisser customiser son personnage AVANT de
		// rejoindre. Le personnage n'est créé/enregistré (LocalCharacters.CreateNew) et
		// la connexion lancée (NetworkManager.JoinHostAsync) qu'à la confirmation sur cet
		// écran ; _pendingJoinIp/_pendingJoinPort restent valides entre-temps.
		static bool _isJoinCharacterCreation = false;

		static int _mpFocusedField = 0;

		// champ de texte actif (0=pseudo,1=ip,2=port)
		static string _mpStatusMessage = "";

		private static string _creationError = "";

		// Personnalisation du personnage (copie depuis CharacterCustomizationUI.cs)
		private static Color _tempSkinColor = new Color(255, 235, 200, 255);

		public static readonly Color[] SkinColorPalette = BuildSkinColorPalette();

		private static Color[] BuildSkinColorPalette()
		{
			Color[] rowStart =
			{
				new Color(251, 229, 220, 255), new Color(251, 211, 196, 255),
				new Color(250, 190, 170, 255), new Color(250, 166, 143, 255),
				new Color(241, 143, 117, 255), new Color(229, 117, 88, 255),
				new Color(216, 107, 76, 255), new Color(194, 85, 49, 255),
				new Color(181, 70, 35, 255), new Color(167, 58, 24, 255),
				new Color(153, 49, 17, 255), new Color(135, 43, 12, 255),
				new Color(116, 39, 10, 255), new Color(99, 35, 17, 255),
				new Color(81, 31, 17, 255), new Color(62, 27, 17, 255)
			};
			Color[] rowEnd =
			{
				new Color(242, 229, 225, 255), new Color(242, 213, 203, 255),
				new Color(239, 193, 181, 255), new Color(233, 169, 159, 255),
				new Color(219, 145, 132, 255), new Color(201, 119, 108, 255),
				new Color(191, 107, 98, 255), new Color(174, 89, 82, 255),
				new Color(157, 74, 68, 255), new Color(139, 61, 54, 255),
				new Color(121, 52, 45, 255), new Color(103, 45, 39, 255),
				new Color(87, 40, 34, 255), new Color(71, 35, 30, 255),
				new Color(60, 32, 28, 255), new Color(48, 27, 25, 255)
			};
			Color[] palette = new Color[rowStart.Length * 8];
			for (int row = 0; row < rowStart.Length; row++)
			{
				for (int column = 0; column < 8; column++)
				{
					float amount = column / 7f;
					palette[row * 8 + column] = new Color(
						(byte)(rowStart[row].R + (rowEnd[row].R - rowStart[row].R) * amount),
						(byte)(rowStart[row].G + (rowEnd[row].G - rowStart[row].G) * amount),
						(byte)(rowStart[row].B + (rowEnd[row].B - rowStart[row].B) * amount), (byte)255);
				}
			}
			return palette;
		}

		private static Color _tempHairColor = new Color(60, 40, 30, 255);

		private static Color _tempEyeColor = new Color(100, 150, 200, 255);

		private static int _tempHairStyle = 0;

		private static int _tempBeardStyle = 0;

		private static int _tempEyeStyle = 0;

		// Sliders pour les couleurs
		private static float _hue = 0.0f;

		private static float _saturation = 0.8f;

		private static float _value = 0.8f;

		static Vector2Int? _selectionCorner2 = null;

		static bool _carouselCacheDirty = true;

		public static int GetBloodEssence() => _bloodEssence;

		public static void AddBloodEssence(int amount) => _bloodEssence = Math.Max(0, _bloodEssence + amount);

		public static bool SpendBloodEssence(int amount)
		{
			if (_bloodEssence < amount) return false;
			_bloodEssence -= amount;
			return true;
		}

		static float _shootCooldown = 0f;

		static float _shootDelay = 0.35f;

		public static bool isCarMode = false;

		public static float carAngle = 0f;

		static float carMaxSpeed = 900f;

		static float carTurnSpeed = 180f;

		static float carWidth = 48f;

		static float carHeight = 96f;

		// Nouveau système de physique pour la voiture
		private static float carVelX = 0f;

		private static float carVelY = 0f;

		private const float CAR_ACCEL = 2500f;

		// Accélération (pixels/s²)
		private const float CAR_FRICTION = 0.93f;

		// Coefficient de frottement (multiplicateur par frame)
		private const float CAR_MAX_SPEED = 900f;

		// Vitesse maximale (pixels/s)
		private const float CAR_TURN_SPEED = 360f;

		// Vitesse de rotation (degrés/s)
		
		// Inertie angulaire pour la voiture
		private static float _carAngularVelocity = 0f;

		private const float CAR_ANGULAR_ACCEL = 800f;

		// Accélération angulaire (degrés/s²)
		private const float CAR_ANGULAR_FRICTION = 0.92f;

		// Friction (multiplicateur par frame)
		private const float CAR_MAX_ANGULAR_SPEED = 360f;

		// Vitesse angulaire max (degrés/s)
		
		private static float _explorationTimer = 0f;

		private const float EXPLORATION_INTERVAL = 0.5f;

		public static List<Entity> GetEntities() => entities;

		static bool _isChargingThrow = false;

		static float _throwCooldown = 0f;

		const float THROW_MAX_SPEED = 800f;

		static Vector2? _predictedThrowLanding = null;

		public static HashSet<string> GetDestroyedObjects() => destroyedObjects;

		public static CarriedFurniture? CurrentCarriedFurniture => _carriedFurniture;

		static Camera2D _currentCamera;

		static float _headAngle = 0f;

		public static float GetHeadAngle() => _headAngle;

		public static float GetGameTime() => _gameTime;

		public static double GetPlayTime() => _totalPlayTime;

		static float _aimCooldown = 0f;

		static double _totalPlayTime = 0f;

		static float _depthVelocity = 0f;

		static Vector2 _lastSurfacePos = Vector2.Zero;

		static bool _isOnSeaFloor = false;

		static float _underwaterElevation = 0f;

		static float _underwaterElevationVelocity = 0f;

		const float UNDERWATER_ELEVATION_PUSH = 95f;

		const float UNDERWATER_ELEVATION_GRAVITY = 180f;

		const float UNDERWATER_ELEVATION_MAX = 95f;

		const float UNDERWATER_ELEVATION_MIN = 0f;

		const float UNDERWATER_DEPTH_PUSH = 12f;

		const float UNDERWATER_DEPTH_GRAVITY = 4f;

		const float UNDERWATER_DEPTH_MAX = 60f;

		const float UNDERWATER_DEPTH_MIN = 0f;

		// seuil minimum pour déclencher un tir
		
		public static Color SkinColor = new Color(255, 235, 200, 255);

		public static Color HairColor = new Color(60, 40, 30, 255);

		public static Color EyeColor = new Color(100, 150, 200, 255);

		public static int EyeStyle = 0;

		private static float _savingDisplayTimer = 0f;

		private const float SAVING_DISPLAY_DURATION = 1.5f;

		static bool _isChargingFishing = false;

		static float _fishingCooldown = 0f;

		const float FISHING_CAST_SPEED = 800f;

		static Vector2? _predictedFishingLanding = null;

		static bool _wasMovingLastFrame = false;

		// Emote wheel (sur B)
		static bool _isEmoteWheelOpen = false;

		static int _emoteWheelHoverIndex = -1;

		static readonly string[] _emoteWheelLabels = new string[] { "Danse", "Dormir", "S'asseoir", "Danse hawaïenne" };

		static readonly Color[] _emoteWheelColors = new Color[] { new Color(235, 190, 60, 220), new Color(90, 130, 255, 220), new Color(170, 210, 120, 220), new Color(255, 140, 180, 220) };

		static string? _currentEmote = null;

		static bool _showDebug = false;

		static Vector2 _lastTravelDistancePos = Vector2.Zero;

		static float _travelDistanceProgress = 0f;

		static bool _noClipEnabled = false;

		public static bool NoClipEnabled => _noClipEnabled;

		private static float _eHoldTime = 0f;

		private static bool _eWasPressed = false;

		private static bool _eInteractionHandled = false;

		private const float E_LONG_PRESS_THRESHOLD = 0.4f;

		static bool _isSitting = false;
		static Vector2 _preSittingPlayerPos = Vector2.Zero;
		static bool _hasPreSittingPlayerPos = false;

		static bool _sittingJustStarted = false;

		static Vector2 _sittingPosition = Vector2.Zero;

		static float _sittingHeightOffset = 0f;

		public static bool IsSittingBehindFurniture { get; private set; }

		static float _sittingAnimTimer = 0f;

		static int _sittingAnimFrame = 0;

		static string _sittingPose = "idle";

		public static bool IsSitting => _isSitting;

		public static Entity? MountedAnimal = null;

		// ===== CLIGNEMENT DES YEUX (aléatoire, toutes les 6 à 15s) =====
		public static bool IsBlinking = false;

		public const float BLINK_DURATION = 0.15f;

		// durée d'un clignement, en secondes
		private static float _blinkTimer = 0f;

		private static float _nextBlinkDelay = 0f;

		private static Random _blinkRandom = new Random();

		private static void ResetBlinkTimer()
		{
			_nextBlinkDelay = 6f + (float)_blinkRandom.NextDouble() * 9f; // entre 6 et 15 secondes
		}

		public static void UpdateBlink(float dt)
		{
			if (_nextBlinkDelay <= 0f && !IsBlinking)
				ResetBlinkTimer();

			if (IsBlinking)
			{
				_blinkTimer -= dt;
				if (_blinkTimer <= 0f)
				{
					IsBlinking = false;
					ResetBlinkTimer();
				}
			}
			else
			{
				_nextBlinkDelay -= dt;
				if (_nextBlinkDelay <= 0f)
				{
					IsBlinking = true;
					_blinkTimer = BLINK_DURATION;
				}
			}
		}

		// Décalage courant des pupilles (calculé chaque frame en fonction du curseur), en pixels logiques
		public static Vector2 PupilLeftOffset = Vector2.Zero;

		public static Vector2 PupilRightOffset = Vector2.Zero;

		// Décalage vertical des sourcils (même logique que les pupilles, mais atténuée)
		public static Vector2 EyebrowLeftOffset = Vector2.Zero;

		public static Vector2 EyebrowRightOffset = Vector2.Zero;

		// Les pupilles (1px de large) doivent rester dans le blanc de l'œil (2px de large) :
		// déplacement max en X = (largeur_oeil - largeur_pupille) / 2 = (2 - 1) / 2 = 0.5px de chaque côté.
		public const float PUPIL_MAX_OFFSET_X = 0.5f;

		public const float PUPIL_MAX_OFFSET_Y = 2f;

		// Les sourcils bougent moins que les pupilles (60% de l'amplitude Y max des pupilles)
		public const float EYEBROW_Y_INTENSITY_FACTOR = 0.6f;

		// Décalage local approximatif (avant échelle/rotation) de chaque œil par rapport au centre de la tête,
		// utilisé uniquement pour calculer la direction/distance du curseur INDÉPENDAMMENT pour chaque œil.
		public const float EYE_LOCAL_OFFSET_X = 5f;

		public const float EYE_LOCAL_OFFSET_Y = -3f;

		// Distance (en unités monde) à partir de laquelle le décalage des pupilles atteint son maximum.
		// En dessous, le décalage est proportionnellement plus faible (le regard "part" progressivement).
		public const float PUPIL_MAX_DISTANCE = 150f;

		static Vector2? _predictedLandingPos = null;

		static float _swingTimer = 0f;

		static float _swingAngle = 0f;

		static Vector2 _swingDirection = Vector2.Zero;

		static float _creationBgOffsetX = 0f;

		static float _creationBgOffsetY = 0f;

		static float _selectAnimTimer = 0f;

		static int _selectAnimFrame = 0;

		static float _selectAnimSpeed = 0.4f;

		public static float wheelRotation = 0f;

		public static float wheelSpin = 0f;

		static float lastCarSpeed = 0f;

		private static bool _godmode = false;

		public static bool IsGodMode => _godmode;

		public const string CrashLogFileName = "log.txt";

		private static void LogCrash(Exception? exception, string? context = null)
		{
			try
			{
				string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
				string header = $"[{timestamp}] {(context ?? "Crash")}";
				string details = exception?.ToString() ?? "Exception object was null.";
				string separator = new string('-', 80);
				File.AppendAllText(CrashLogFileName, header + Environment.NewLine + details + Environment.NewLine + separator + Environment.NewLine);
			}
			catch
			{
				// Ne pas lever d'exception depuis le journal de crash.
			}
		}

		static Vector2? PredictThrowLanding(Vector2 startPos, Vector2 direction, float power)
		{
			float distance = MathHelper.Lerp(50f, 300f, power);
			Vector2 targetPos = startPos + direction * distance;
			return targetPos;
		}

		private static Vector2 GetMountOffset(Entity animal)
		{
			// Ajuster selon l'espèce
			return animal.Species switch
			{
				"horse" => new Vector2(0, -55),
				"wolf" => new Vector2(0, -35),
				"bear" => new Vector2(0, -45),
				_ => new Vector2(0, -40)
			};
		}

		public struct Vector2Int
		{
			public int X, Y;
			public Vector2Int(int x, int y) { X = x; Y = y; }
		}

		public static float GetUnderwaterElevation() => _underwaterElevation;

		private static Vector2 GetIntersectionWithRectEdge(Vector2 from, Vector2 direction, float rectX, float rectY, float rectW, float rectH)
		{
			// Calculer les intersections avec les 4 bords
			float leftX = rectX;
			float rightX = rectX + rectW;
			float topY = rectY;
			float bottomY = rectY + rectH;
			
			// Éviter la division par zéro
			if (Math.Abs(direction.X) < 0.0001f) direction.X = 0.0001f;
			if (Math.Abs(direction.Y) < 0.0001f) direction.Y = 0.0001f;
			
			// Intersection avec le bord gauche
			float tLeft = (leftX - from.X) / direction.X;
			Vector2 pointLeft = new Vector2(leftX, from.Y + tLeft * direction.Y);
			
			// Intersection avec le bord droit
			float tRight = (rightX - from.X) / direction.X;
			Vector2 pointRight = new Vector2(rightX, from.Y + tRight * direction.Y);
			
			// Intersection avec le bord haut
			float tTop = (topY - from.Y) / direction.Y;
			Vector2 pointTop = new Vector2(from.X + tTop * direction.X, topY);
			
			// Intersection avec le bord bas
			float tBottom = (bottomY - from.Y) / direction.Y;
			Vector2 pointBottom = new Vector2(from.X + tBottom * direction.X, bottomY);
			
			// Trouver la première intersection positive dans la direction
			Vector2 bestPoint = from;
			float bestT = float.MaxValue;
			
			if (tLeft > 0 && pointLeft.Y >= topY && pointLeft.Y <= bottomY)
			{
				if (tLeft < bestT) { bestT = tLeft; bestPoint = pointLeft; }
			}
			if (tRight > 0 && pointRight.Y >= topY && pointRight.Y <= bottomY)
			{
				if (tRight < bestT) { bestT = tRight; bestPoint = pointRight; }
			}
			if (tTop > 0 && pointTop.X >= leftX && pointTop.X <= rightX)
			{
				if (tTop < bestT) { bestT = tTop; bestPoint = pointTop; }
			}
			if (tBottom > 0 && pointBottom.X >= leftX && pointBottom.X <= rightX)
			{
				if (tBottom < bestT) { bestT = tBottom; bestPoint = pointBottom; }
			}
			
			return bestPoint;
		}

		public static void ApplyScreenShake(float intensity, float duration)
		{
			_shakeIntensity = Math.Max(_shakeIntensity, intensity);
			_shakeDuration = Math.Max(_shakeDuration, duration);
		}

		private static Vector2 RotateVec(Vector2 v, float angle)
		{
			float r = angle * MathF.PI / 180f;
			return new Vector2(
				v.X * MathF.Cos(r) - v.Y * MathF.Sin(r),
				v.X * MathF.Sin(r) + v.Y * MathF.Cos(r)
			);
		}

		private static string? FindPreferredSoundPath(string soundsDir, string fileNameWithoutExtension)
		{
			foreach (string extension in new[] { ".mp3", ".wav" })
			{
				string path = Path.Combine(soundsDir, $"{fileNameWithoutExtension}{extension}");
				if (File.Exists(path))
					return path;
			}
			return null;
		}

		private static void PlayEatSound()
		{
			InitializeEatSounds();
			if (_eatSounds.Count == 0) return;

			int index = Random.Shared.Next(_eatSounds.Count);
			Sound sound = _eatSounds[index];
			if (!Raylib.IsSoundReady(sound)) return;
			Raylib.SetSoundVolume(sound, 0.5f);
			Raylib.SetSoundPitch(sound, GetRandomSoundPitch());
			Raylib.PlaySound(sound);
		}

		private static void PlayDrinkSound()
		{
			InitializeDrinkSounds();
			if (_drinkSounds.Count == 0)
			{
				// fallback sur eat si pas de sons de boisson fournis
				PlayEatSound();
				return;
			}

			int index = Random.Shared.Next(_drinkSounds.Count);
			Sound sound = _drinkSounds[index];
			if (!Raylib.IsSoundReady(sound)) return;
			Raylib.SetSoundVolume(sound, 0.5f);
			Raylib.SetSoundPitch(sound, GetRandomSoundPitch());
			Raylib.PlaySound(sound);
		}

		private static float GetRandomSoundPitch()
		{
			float randomPitch = 1f + (float)(Random.Shared.NextDouble() * 0.12f - 0.06f);
			return Math.Clamp(randomPitch, 0.9f, 1.1f);
		}

		internal static bool ShouldTriggerFootstepFrame(string animState, int animFrame, int frameCount)
		{
			if (frameCount <= 1) return false;
			if (!string.Equals(animState, "walk", StringComparison.OrdinalIgnoreCase) &&
			    !string.Equals(animState, "run", StringComparison.OrdinalIgnoreCase))
				return false;

			int secondStepFrame = Math.Max(1, frameCount / 2);
			return animFrame == 0 || animFrame == secondStepFrame;
		}

		internal static int GetPreferredFoodId(string species)
        {
            var info = SpeciesData.GetSpeciesInfo(species);
            return info?.PreferredFoodId ?? 0;
        }

		private static void UpdateCaveTransition(float dt)
	{
		if (_caveTransitionPhase == CaveTransitionPhase.None)
			return;

		_caveTransitionTimer += dt;

		switch (_caveTransitionPhase)
		{
			case CaveTransitionPhase.MoveToCenter:
			{
				if (_caveTransitionTimer >= CAVE_TRANSITION_MOVE_DURATION)
				{
					_caveTransitionPhase = CaveTransitionPhase.CloseBars;
					_caveTransitionTimer = 0f;
					_caveTransitionBarProgress = 0f;
				}
				break;

			}
			case CaveTransitionPhase.CloseBars:
			{
				_caveTransitionBarProgress = Math.Clamp(_caveTransitionTimer / CAVE_TRANSITION_CLOSE_DURATION, 0f, 1f);
				if (_caveTransitionTimer >= CAVE_TRANSITION_CLOSE_DURATION)
				{
					_caveTransitionPhase = CaveTransitionPhase.Closed;
					_caveTransitionTimer = 0f;
					_caveTransitionBarProgress = 1f;
					PerformCaveZoneSwitch();
				}
				break;
			}
			case CaveTransitionPhase.Closed:
			{
				_caveTransitionBarProgress = 1f;
				if (_caveTransitionTimer >= CAVE_TRANSITION_HOLD_DURATION)
				{
					_caveTransitionPhase = CaveTransitionPhase.OpenBars;
					_caveTransitionTimer = 0f;
				}
				break;
			}
			case CaveTransitionPhase.OpenBars:
			{
				float progress = Math.Clamp(_caveTransitionTimer / CAVE_TRANSITION_OPEN_DURATION, 0f, 1f);
				_caveTransitionBarProgress = 1f - progress;
				if (_caveTransitionTimer >= CAVE_TRANSITION_OPEN_DURATION)
				{
					_caveTransitionPhase = CaveTransitionPhase.None;
					_caveTransitionTimer = 0f;
					_caveTransitionBarProgress = 0f;
				}
				break;
			}
		}
	}

		static int _hoveredMpSubOption = -1;

		static int _pendingFishId = 0;

		private static CarriedFurniture? _carriedFurniture = null;

		private static Entity? _carriedCreature = null;

		public static Entity? CurrentCarriedCreature => _carriedCreature;

		private static float _speedPotionTotalDuration = 0f;

		private static readonly HashSet<int> _followOrderSeenBuffer = new();

		// Chapeau crabulesque, Torse de crabe, Jambes de crabe
		private static Entity? _crabCompanion = null;

		private static float _crabRespawnCooldown = 0f;

		private static bool IsPointInUIRect(Vector2 point, int x, int y, int w, int h)
		{
			return point.X >= x && point.X <= x + w && point.Y >= y && point.Y <= y + h;
		}

		// Constantes pour la reproduction
		private const float BREEDING_CHECK_INTERVAL = 5f;

		// vérifier toutes les 2 secondes
		private const float BREEDING_DISTANCE = 80f;

		// distance maximale pour se reproduire
		private const float GESTATION_DURATION = 120f;

		// 2 minutes de gestation (modifiable)

		private static float _breedingCooldown = 0f;

		// ===================================================
		// FONCTIONS DE DESSIN POUR LA PERSONNALISATION
		// (Adaptées depuis CharacterCustomizationUI.cs)
		// ===================================================

		// Convertit HSV (0..1) en Color
		private static Color HsvToColor(float h, float s, float v)
		{
			float r = 0, g = 0, b = 0;
			int i = (int)(h * 6);
			float f = h * 6 - i;
			float p = v * (1 - s);
			float q = v * (1 - f * s);
			float t = v * (1 - (1 - f) * s);

			switch (i % 6)
			{
				case 0: r = v; g = t; b = p; break;
				case 1: r = q; g = v; b = p; break;
				case 2: r = p; g = v; b = t; break;
				case 3: r = p; g = q; b = v; break;
				case 4: r = t; g = p; b = v; break;
				case 5: r = v; g = p; b = q; break;
			}
			return new Color((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), (byte)(255));
		}

		// Convertit Color en HSV (hue 0..1, saturation 0..1, value 0..1)
		private static (float h, float s, float v) ColorToHsv(Color color)
		{
			float r = color.R / 255f;
			float g = color.G / 255f;
			float b = color.B / 255f;
			float max = Math.Max(r, Math.Max(g, b));
			float min = Math.Min(r, Math.Min(g, b));
			float delta = max - min;
			float h = 0, s = 0, v = max;

			if (delta > 0.0001f)
			{
				s = delta / max;
				if (Math.Abs(max - r) < 0.0001f)
					h = (g - b) / delta;
				else if (Math.Abs(max - g) < 0.0001f)
					h = 2 + (b - r) / delta;
				else
					h = 4 + (r - g) / delta;
				h *= 60;
				if (h < 0) h += 360;
				h /= 360;
			}
			return (h, s, v);
		}

		// Synchronise les sliders à partir d'une couleur donnée
		private static void SyncSlidersFromColor(Color color)
		{
			var (h, s, v) = ColorToHsv(color);
			_hue = h;
			_saturation = s;
			_value = v;
		}

		// Dégradé pour la teinte : spectre complet
		private static Color HueGradient(float t) => HsvToColor(t, 1.0f, 1.0f);

		// Dégradé pour la saturation : blanc -> couleur actuelle
		private static Color SaturationGradient(float t) => HsvToColor(_hue, t, _value);

		// Dégradé pour la valeur : noir -> couleur actuelle
		private static Color ValueGradient(float t) => HsvToColor(_hue, _saturation, t);

		public static Camera2D GetCurrentCamera() => _currentCamera;

		private static void PrintBanner(string title, ConsoleColor color)
		{
			try
			{
				Console.Clear();
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════╗");
				Console.ForegroundColor = color;
				Console.WriteLine($"║{title,-79}║");
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════╝");
				Console.ResetColor();
				Console.WriteLine();
			}
			catch
			{
				// Ignorer les erreurs de console lorsque l'application est exécutée sans terminal.
			}
		}

		private static void PrintWarning(string message)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"  [!]  {message}");
			Console.ResetColor();
		}

		private static void PrintError(string message)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"  [X]  {message}");
			Console.ResetColor();
		}

		private static void PrintInfo(string message)
		{
			Console.ForegroundColor = ConsoleColor.Gray;
			Console.WriteLine($"  [i]  {message}");
			Console.ResetColor();
		}

		private static void PrintInlineProgress(bool isError = false)
		{
			if (isError)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write("X");
			}
			else
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write(".");
			}
			Console.ResetColor();
		}

		static ToolType ParseToolType(string t) => t?.ToLower() switch
        {
            "axe"=>ToolType.Axe, "pickaxe"=>ToolType.Pickaxe, "shovel"=>ToolType.Shovel,
            "hoe"=>ToolType.Hoe, "scythe"=>ToolType.Scythe, "hammer"=>ToolType.Hammer, _=>ToolType.None
        };

		private static bool AreColorsEqual(List<Color?>? a, List<Color?>? b)
		{
			if (a == null && b == null) return true;
			if (a == null || b == null) return false;
			if (a.Count != b.Count) return false;
			for (int i = 0; i < a.Count; i++)
			{
				if (a[i] == null && b[i] == null) continue;
				if (a[i] == null || b[i] == null) return false;
				if (!a[i].Value.Equals(b[i].Value)) return false;
			}
			return true;
		}

		private static bool AreMetadataEqual(string? a, string? b)
        {
            a = a?.Trim() ?? string.Empty;
            b = b?.Trim() ?? string.Empty;
            return a.Equals(b, StringComparison.OrdinalIgnoreCase);
        }

		private static string FormatMetaPair(float cur, float max)
        {
            return $"{cur.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{max.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
    }
}
