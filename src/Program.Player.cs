// Program.Player.cs
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

		public static Guild? PlayerGuild = null;

		public static Car? PlayerCurrentCar = null;

		public static (int x, int y)? PlayerCurrentWagonTile { get; private set; }

		public static string PlayerMorphSpecies = "human";
		public static Color PlayerMorphTint = Color.White;
		public static Dictionary<string, int> PlayerMorphFeatureVariants = new();
		public static Dictionary<string, Color> PlayerMorphFeatureColors = new();

		public static void SetPlayerMorphSpecies(string species)
		{
			PlayerMorphSpecies = string.IsNullOrWhiteSpace(species) ? "human" : species.ToLowerInvariant();
			PlayerMorphFeatureVariants.Clear();
			PlayerMorphFeatureColors.Clear();
			PlayerMorphTint = Color.White;

			if (!string.Equals(PlayerMorphSpecies, "human", StringComparison.OrdinalIgnoreCase))
			{
				Entity.GenerateRandomAppearance(PlayerMorphSpecies, _playerPos, out PlayerMorphTint,
					PlayerMorphFeatureVariants, PlayerMorphFeatureColors);
			}
		}

		static bool _isPlayerInWater = false;

		private static float _leftClickStartTime = 0f;

		private static bool _isChargingRightClick = false;

		private static float _rightClickStartTime = 0f;

		private static List<Sound> _playerWalkSounds = new();

		public static int playerHP = 20;

		public static int playerMaxHP = 20;

		public static float playerMaxStamina = 100f;

		public static float playerMaxHunger = 100f;

		public static float playerThirst = 100f;

		public static float playerMaxThirst = 100f;

		//  BOOSTS TEMPORAIRES (potions, rassasiement...) ─────────────────────────────
        // _speedPotionTimer / _speedPotionTotalDuration existent déjà ailleurs dans le
        // projet (Program.Rendering.cs s'en sert dans GetActiveBoosts) : on ne les
        // redéclare pas ici, on ajoute seulement les nouveaux effets.

		public static float _strengthPotionTimer = 0f;
		public static float _strengthPotionTimerMax = 0f;

		public static float _resistancePotionTimer = 0f;
		public static float _resistancePotionTimerMax = 0f;

		public static float _lightPotionTimer = 0f;
		public static float _lightPotionTimerMax = 0f;

		public static float _heatResistPotionTimer = 0f;
		public static float _heatResistPotionTimerMax = 0f;

		public static float _coldResistPotionTimer = 0f;
		public static float _coldResistPotionTimerMax = 0f;

		public static float _invisibilityPotionTimer = 0f;
		public static float _invisibilityPotionTimerMax = 0f;

        public static float _poisonTimer = 0f;
        public static float _poisonDuration = 0f;
        public const float POISON_TICK_INTERVAL = 1.5f;
        public static float _poisonTickAccumulator = 0f;

        // fil du temps (fichier non fourni ici) : if (!IsPlayerSated) playerHunger -= ...
        public const float SATIETY_DURATION = 180f; // 3 minutes

		public static float _satietyTimer = 0f;

		public static bool IsPlayerSated => _satietyTimer > 0f;

		public static bool IsPlayerInvisible => _invisibilityPotionTimer > 0f;

        public static bool IsPlayerPoisoned => _poisonTimer > 0f;

		public static bool HasColdResistance => _coldResistPotionTimer > 0f;

		public static bool IsGlowing => _lightPotionTimer > 0f;

		// Multiplicateurs à brancher là où les dégâts infligés / subis sont calculés
		public static float GetPlayerDamageMultiplier() => _strengthPotionTimer > 0f ? 1.5f : 1f;

		public static float GetPlayerDefenseMultiplier() => _resistancePotionTimer > 0f ? 0.5f : 1f; // dégâts subis x0.5

        public static float GetPoisonDamagePerTick() => 2f;

        public static void ApplyPlayerPoison(float duration)
        {
            if (duration <= 0f) return;
            if (_poisonTimer <= 0f || duration > _poisonTimer)
            {
                _poisonDuration = duration;
                _poisonTimer = Math.Max(_poisonTimer, duration);
                _poisonTickAccumulator = 0f;
                AddNotification(new Notification(" Vous êtes empoisonné !", new Color(180, 255, 100, 255), 2f));
            }
        }

		//  MORT : le joueur disparaît en particules bleues, laisse un lootbag au sol,
        // puis réapparaît sans bloquer le monde.
        public static bool IsPlayerRagdolled = false;

		static bool _playerDeathPendingRespawn = false;

		static Vector2 _playerDeathPosition = Vector2.Zero;

		static float _playerDeathTimer = 0f;

		//  MULTIJOUEUR : suivi (clé = LocalPlayer.Id, soit 1000+connexionId) de l'état
        // vivant/mort de chaque joueur distant, pour déclencher UNE FOIS l'explosion de
        // particules quand son HP passe à 0 (vu de notre point de vue), et le masquer tant
        // qu'il n'est pas ressuscité — au lieu de le voir "attendre" debout, mort.
        static readonly Dictionary<int, bool> _remoteWasDead = new();

		const float PLAYER_DEATH_DURATION = 2.2f;

		static bool _rightClickConsumed = false;

		//  MULTIJOUEUR EN LIGNE ─────────────────────────────────────────────
		public static string LocalPlayerName = "Joueur";

		//  MULTIJOUEUR : identifiant de session optionnel, utilisé À LA PLACE de celui
		// stocké dans player_id.txt quand le joueur a explicitement choisi un personnage
		// différent (existant ou nouveau) sur l'écran "Choisir un personnage" affiché après
		// avoir validé IP/pseudo/port. Généré/choisi en mémoire, jamais écrit dans
		// player_id.txt : cela permet de lancer plusieurs copies du jeu depuis le même PC
		// (donc partageant le même player_id.txt) et de rejoindre le même hôte/monde avec
		// des personnages distincts, sans écraser l'identité "par défaut" de l'installation.
		// Voir aussi Network.HandleHello (côté hôte), qui retrouve/persiste un personnage
		// par GUID et par monde — pas par IP, qui n'identifie pas de façon fiable un joueur
		// (plusieurs vrais joueurs derrière le même routeur/NAT partagent la même IP
		// publique, et plusieurs instances du jeu sur un même PC partagent la même IP locale).
		private static string? _sessionGuidOverride = null;

		// Lit l'identifiant permanent (player_id.txt) SANS jamais passer par l'override de
		// session, pour pouvoir comparer/afficher "est-ce le personnage par défaut ?" sur
		// l'écran de sélection, indépendamment du personnage actuellement choisi pour cette
		// connexion.
		public static string DefaultLocalPlayerGuidReadOnly
		{
			get
			{
				var saved = _sessionGuidOverride;
				_sessionGuidOverride = null;
				var value = LocalPlayerGuid;
				_sessionGuidOverride = saved;
				return value;
			}
		}

		//  Appelé depuis l'écran "Choisir un personnage" quand le joueur sélectionne un
		// personnage existant (autre que celui par défaut) ou vient d'en créer un nouveau.
		public static void UseCharacterGuidForNextJoin(string guid)
		{
			_sessionGuidOverride = guid;
		}

		public static string LocalPlayerGuid
		{
			get
			{
				if (_sessionGuidOverride != null) return _sessionGuidOverride;

				if (_localPlayerGuid == null)
				{
					const string path = "player_id.txt";
					try
					{
						if (File.Exists(path))
						{
							var existing = File.ReadAllText(path).Trim();
							if (!string.IsNullOrEmpty(existing)) _localPlayerGuid = existing;
						}
						if (_localPlayerGuid == null)
						{
							_localPlayerGuid = Guid.NewGuid().ToString();
							File.WriteAllText(path, _localPlayerGuid);
						}
					}
					catch
					{
						// Écriture impossible (dossier en lecture seule...) : on garde un id
						// en mémoire pour cette session, mieux que de planter.
						_localPlayerGuid = Guid.NewGuid().ToString();
					}
				}
				return _localPlayerGuid;
			}
		}

		// écran de configuration de la connexion
		static string _mpPseudoInput = "Joueur";

		static string _mpPortInput = "7777";

		public static int PlayerHairStyle = 0;

		public static int PlayerBeardStyle = 0;

		static float _playerDepth = 0f;

		static Vector2 _playerPos = Vector2.Zero;

		static float _playerFacing = 1f;

		private static readonly List<LocalPlayer> _localPlayers = new();

		//  OPTIM : cache de GetLocalPlayers(), recalculé au plus une fois par frame au lieu
		// d'à chaque appel (le tri + filtre LINQ étaient refaits plusieurs fois/frame).
		private static int _currentFrame = 0;

		private static List<LocalPlayer> _localPlayersCache = new();

		private static int _localPlayersCacheFrame = -1;

		static double _lastClickTime = 0;

		static int _lastClickedIndex = -1;

		private static Entity _playerEntity = null!;

		public static Vector2 GetPlayerPosition() => _playerPos;

		public static int GetLocalPlayerCount() => _localPlayers.Count(p => p.Connected);

		private static void EnsureLocalPlayersInitialized()
		{
			if (_localPlayers.Count == 0)
			{
				_localPlayers.Add(new LocalPlayer
				{
					Id = 0,
					Connected = true,
					Position = _playerPos,
					Facing = _playerFacing,
					DeviceName = "keyboard",
					DeviceIndex = -1
				});
				return;
			}
			
			if (_localPlayers[0].Id != 0)
			{
				_localPlayers.Insert(0, new LocalPlayer
				{
					Id = 0,
					Connected = true,
					Position = _playerPos,
					Facing = _playerFacing,
					DeviceName = "keyboard",
					DeviceIndex = -1
				});
			}
			
			for (int i = _localPlayers.Count - 1; i > 0; i--)
			{
				_localPlayers.RemoveAt(i);
			}
			
			_localPlayers[0].Position = _playerPos;
			_localPlayers[0].Facing = _playerFacing;
			_localPlayers[0].AnimState = _animState;
			_localPlayers[0].AnimFrame = _animFrame;
			_localPlayers[0].AnimProg = _animProg;
		}

		private static void UpdateAdditionalLocalPlayers(float dt, Vector2 hitboxDim, Camera2D camera)
		{
			// Le co-op local a été retiré : le jeu ne gère plus qu'un seul joueur contrôlable.
		}

		private static void TryConnectLocalPlayers()
		{
			EnsureLocalPlayersInitialized();
		}

		private static bool IsMovementInputPressed()
		{
			return KeyBindings.IsPressed(GameAction.MoveUp)
				|| KeyBindings.IsPressed(GameAction.MoveDown)
				|| KeyBindings.IsPressed(GameAction.MoveLeft)
				|| KeyBindings.IsPressed(GameAction.MoveRight);
		}

		private static void PlayPlayerWalkSound(float volume = 0.3f)
		{
			InitializePlayerWalkSounds();
			if (_playerWalkSounds.Count == 0) return;

			int index = Random.Shared.Next(_playerWalkSounds.Count);
			Sound sound = _playerWalkSounds[index];
			if (!Raylib.IsSoundReady(sound)) return;

			Raylib.SetSoundVolume(sound, volume);
			Raylib.SetSoundPitch(sound, GetRandomSoundPitch());
			Raylib.PlaySound(sound);
		}

		internal static void PlayEntityFootstepSound()
		{
			PlayPlayerWalkSound(0.12f);
		}

		static void UpdateTextFieldInput(ref string text, int fieldIndex, bool digitsOnly = false)
		{
			if (_mpFocusedField != fieldIndex) return;
			string id = $"multiplayer-field-{fieldIndex}";
			if (!TextInput.IsFocused(id))
				TextInput.Focus(id, text.Length);
			TextInput.Update(ref text, id, new Rectangle(-1, -1, 0, 0), 40, digitsOnly ? char.IsDigit : null);
		}

		private static float GetPlayerBaseSpeed()
        {
            var info = SpeciesData.GetSpeciesInfo(PlayerMorphSpecies);
            return (info?.Speed ?? 250f) * GetPlayerSpeedMultiplier();
        }

		private static float GetPlayerRunSpeed()
        {
            var info = SpeciesData.GetSpeciesInfo(PlayerMorphSpecies);
            float baseSpeed = info?.Speed ?? 250f;
            float runSpeed = (info != null && info.RunSpeed > 0)
                ? info.RunSpeed
                : baseSpeed * 1.5f;
            float speed = runSpeed * GetPlayerSpeedMultiplier();
            return IsGodMode ? speed * 2f : speed;
        }

		public static Entity GetPlayerEntity()
		{
			// Retourne une entité représentant le joueur (si on veut qu'il soit dans la liste des entités)
			// Pour l'instant, on peut créer une entité factice ou utiliser null pour le driver
			// On va simplement stocker une référence dans Program
			return _playerEntity; // à définir
		}

		public static Vector2 GetPlayerCollisionDimensions() => new(TileSize * 0.9f, TileSize * 0.4f);

		public static void ApplyEntityPush(Vector2 displacement)
		{
			Vector2 xPosition = _playerPos + new Vector2(displacement.X, 0f);
			if (_noClipEnabled || !World.IsColliding(xPosition, GetPlayerCollisionDimensions(), destroyedObjects))
				_playerPos.X = xPosition.X;

			Vector2 yPosition = _playerPos + new Vector2(0f, displacement.Y);
			if (_noClipEnabled || !World.IsColliding(yPosition, GetPlayerCollisionDimensions(), destroyedObjects))
				_playerPos.Y = yPosition.Y;

			_playerEntity.WorldPos = _playerPos;
		}

		// Applique les changements de personnalisation
		private static void ApplyCharacterCustomization(Color skin, int hairStyle, int beardStyle, Color hairColor, int eyeStyle, Color eyeColor)
		{
			Program.SkinColor = skin;
			Program.PlayerHairStyle = hairStyle;
			Program.PlayerBeardStyle = beardStyle;
			Program.HairColor = hairColor;
			Program.EyeStyle = eyeStyle;
			Program.EyeColor = eyeColor;
		}

		// Action actuellement en attente d'une nouvelle touche dans l'onglet Contrôles
		// (null si aucune réassignation n'est en cours).
		private static (GameAction action, int index)? _rebindingAction = null;

		private static bool AreMetaEqual(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
            {
                if (!b.TryGetValue(kv.Key, out var v) || !string.Equals(v, kv.Value, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

		private static bool IsSpoilKey(string key) => string.Equals(key, "spoil", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "spoiled", StringComparison.OrdinalIgnoreCase);

		private static Dictionary<string, string> ParseMetadataEntries(string? metadata)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(metadata)) return result;

            foreach (var part in metadata.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int separatorIndex = part.IndexOf('=');
                if (separatorIndex <= 0) continue;

                string key = part.Substring(0, separatorIndex).Trim();
                if (string.IsNullOrEmpty(key) || IsSpoilKey(key)) continue;

                result[key] = part.Substring(separatorIndex + 1);
            }

            return result;
        }

		private static bool AreMetadataEqualExceptSpoil(string? a, string? b)
        {
            return AreMetaEqualExceptSpoil(ParseMetadataEntries(a), ParseMetadataEntries(b));
        }

		private static bool AreMetaEqualExceptSpoil(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            var aKeys = a.Keys.Where(k => !IsSpoilKey(k)).ToList();
            var bKeys = b.Keys.Where(k => !IsSpoilKey(k)).ToList();
            if (aKeys.Count != bKeys.Count) return false;

            foreach (var key in aKeys)
            {
                if (!b.TryGetValue(key, out var value) || !string.Equals(value, a[key], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
    }
}
