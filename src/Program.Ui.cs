// Program.Ui.cs
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

		private static float _controlsScrollOffset = 0f;

		private static bool _controlsScrollInitialized = false;

		private static Texture2D _serverOwnerIcon;

		private static Texture2D _serverClientIcon;

		private static Texture2D _serverTeleportIcon;

		private static Texture2D _serverKickIcon;

		static int _optionsTab = 0;

		private static Dictionary<string, Texture2D> _uiBarButtonTextures = new Dictionary<string, Texture2D>();

		private static Texture2D _lmouseButtonTex;

		private static Texture2D _rmouseButtonTex;

		private static Texture2D _wmouseButtonTex;

		private static readonly Dictionary<GamepadButton, Texture2D> _gamepadButtonTextures = new();

		private static readonly Dictionary<string, Texture2D> _gamepadJoystickTextures = new();

		private static Texture2D GetGamepadButtonTexture(GamepadButton button) =>
			_gamepadButtonTextures.TryGetValue(button, out var texture) ? texture : default;

		private static Texture2D GetGamepadJoystickTexture(GamepadAxisDirection direction)
		{
			string key = direction switch
			{
				GamepadAxisDirection.LeftXNegative => "l_left",
				GamepadAxisDirection.LeftXPositive => "l_right",
				GamepadAxisDirection.LeftYNegative => "l_up",
				GamepadAxisDirection.LeftYPositive => "l_down",
				GamepadAxisDirection.RightXNegative => "r_left",
				GamepadAxisDirection.RightXPositive => "r_right",
				GamepadAxisDirection.RightYNegative => "r_up",
				GamepadAxisDirection.RightYPositive => "r_down",
				_ => string.Empty
			};
			return _gamepadJoystickTextures.TryGetValue(key, out var texture) ? texture : default;
		}

		// --- Sons UI / statut (hover boutons, faim, succès) ---
		private static Sound _hoverSound = default;

		private static Sound _buttonClickSound = default;

		// évite de rejouer le son en boucle tant qu'on reste sous le seuil

		// États "survolé au frame précédent" pour quelques boutons représentatifs (voir UpdateButtonHoverSound).
		// Pour ajouter le son de survol à un autre bouton : déclarer un bool statique du même genre
		// et appeler UpdateButtonHoverSound(isHovered, ref monBoolPrecedent) juste après le calcul de isHovered.
		private static HashSet<int> _prevPauseButtonsHovered = new();

		private static bool _prevAchievementsIconHovered = false;

		private static bool _mainMenuJoinHovered = false;

		private static bool _mainMenuCreateHovered = false;

		private static bool _mainMenuPlayHovered = false;

		private static bool _prevMainMenuGearHovered = false;

		private static bool _prevMainMenuJoinHovered = false;

		private static bool _prevMainMenuCreateHovered = false;

		private static bool _prevMainMenuPlayHovered = false;

		// Bouton "supprimer la sauvegarde" en haut à droite du panneau de détails
		// du carrousel de sauvegardes (menu principal classique).
		private static bool _mainMenuDeleteSaveHovered = false;
		private static bool _prevMainMenuDeleteSaveHovered = false;

		//  Menu pause : animation d'ouverture/fermeture (glisse depuis/vers le haut de l'écran).
		// 0 = complètement caché au-dessus de l'écran, 1 = complètement affiché.
		private static float _pauseMenuSlide = 0f;

		private static bool _pauseMenuClosing = false;

		static GameState _gameState = GameState.MainMenu;

		// ─────────────────────────────────────────────────────────────────────────
        //  ÉCRAN DE MORT : vignette progressive -> pop du crâne -> message qui
        //  s'écrit -> bouton "Réapparaître". Voir DrawDeathScreen / UpdateDeathScreen.
        // ─────────────────────────────────────────────────────────────────────────
        public enum DeathCause
        {
            Fire, Cold, Lightning, Shadow, Physical, Poison, Crush, Holy,
            Ice, Earth, Water, ToxicBreath, Energy, Wind, Gaze, Curse,
            Celestial, SpectralFire, Disintegration, Starvation, Generic
        }

		// ---- Animation de l'écran de mort ----
        private enum DeathScreenPhase { Vignette, SkullPop, TypingMessage, WaitingForButton, ButtonReady }

		// vitesse d'écriture du message
        const float DELAY_BEFORE_BUTTON = 0.9f;

		// pause après la fin du texte avant le bouton
        static Texture2D _deathSkullTexture = new Texture2D();

		//  Dessine l'écran de mort en espace écran (appelé après EndMode2D). Vignette qui
        // s'assombrit sur les bords, crâne avec pop au centre, message en rouge qui
        // s'écrit dessous, et bouton "Réapparaître" une fois prêt.
        static Rectangle _respawnButtonRect = new Rectangle(0, 0, 0, 0);

		private static float _optionsLanguageScrollOffset = 0f;

		private static float _optionsLanguageMaxScroll = 0f;

		private static int _lastOptionsTab = -1;

		// Etat vers lequel on doit revenir en quittant les Options (MainMenu si ouvert depuis le menu
        // principal, Paused si ouvert depuis une partie en cours). Evite de renvoyer vers une "fausse"
        // partie en pause quand les Options ont été ouvertes depuis le menu principal.
        static GameState _optionsReturnState = GameState.MainMenu;

		private static Dictionary<GameAction, List<InputBinding>>? _optionsBindingsSnapshot;

		private static bool _showOptionsDiscardConfirmation = false;

		private static void OpenOptions(GameState returnState)
		{
			_optionsReturnState = returnState;
			_optionsBindingsSnapshot = KeyBindings.CloneCurrent();
			_showOptionsDiscardConfirmation = false;
			_gameState = GameState.Options;
		}

		private static bool OptionsHaveBindingChanges() =>
			_optionsBindingsSnapshot != null && KeyBindings.HasChanges(_optionsBindingsSnapshot);

		private static void AcceptOptionsChanges()
		{
			if (_optionsBindingsSnapshot != null)
				KeyBindings.Apply(KeyBindings.CloneCurrent());
			_optionsBindingsSnapshot = null;
			_showOptionsDiscardConfirmation = false;
			_gameState = _optionsReturnState;
		}

		private static void DiscardOptionsChanges()
		{
			if (_optionsBindingsSnapshot != null)
				KeyBindings.Restore(_optionsBindingsSnapshot);
			_optionsBindingsSnapshot = null;
			_showOptionsDiscardConfirmation = false;
			_gameState = _optionsReturnState;
		}

		private static void RequestOptionsBack()
		{
			if (OptionsHaveBindingChanges())
				_showOptionsDiscardConfirmation = true;
			else
				DiscardOptionsChanges();
		}

		static bool _isMultiplayerMenu = false;

		// sous-menu "Multijoueur" affiché
		static int _multiplayerSubOption = 0;

		// survol du bouton "Rejoindre une partie" du menu principal
		static readonly HashSet<string> _pendingNetworkPickups = new();

		// Onglet actif du menu de création (0=Apparence,1=Cheveux,2=Yeux)
		private static int _creationTab = 0;

		// Catégories du créateur de personnage : Cheveux, Barbe, Peau, Yeux, Prénom, Options
		private static string[] _creationTabs = { "CHEVEUX", "BARBE", "PEAU", "YEUX", "PRÉNOM", "OPTIONS" };

		static Texture2D _menuClouds;

		static Texture2D _menuBack;

		static Texture2D _menuCliff;

		static Texture2D _menuTraveler;

		static Texture2D _menuTitle;
		static Texture2D _characterSelectionBackground;
		static Texture2D[] _characterSelectionBackgrounds = Array.Empty<Texture2D>();
		static Dictionary<string, int> _saveBackgroundStyles = new(StringComparer.OrdinalIgnoreCase);
		static Texture2D _characterSelectionBox;

		//  NOUVEAU MENU PRINCIPAL (écran d'accueil + carrousel de mondes) ──────
		static Texture2D _mainMenuGearIcon;
		static Texture2D _mainMenuLoadFileIcon;
		static Texture2D _mainMenuSaveFileIcon;
		static Texture2D _mainMenuChangelogIcon;
		static bool _showChangelog = false;
		static float _changelogScroll = 0f;
		static float _changelogScrollTarget = 0f;
		static bool _changelogScrollbarDragging = false;
		static float _changelogScrollbarDragOffset = 0f;
		static bool _changelogLoaded = false;
		static readonly List<ChangelogItem> _changelogItems = new();

		private sealed class ChangelogItem
		{
			public string Month { get; init; } = string.Empty;
			public string Date { get; init; } = string.Empty;
			public string Type { get; init; } = string.Empty;
			public string Text { get; init; } = string.Empty;
		}

		// assets/gui/bin.png (bouton "supprimer" sous chaque monde du carrousel)
		static Texture2D _achievementsMenuIcon;

		// != null tant qu'une confirmation de suppression est affichée
		static bool _mainMenuActivated = false;

		// 0 = écran d'accueil, 1 = carrousel de mondes ; s'anime en douceur entre
		// les deux dans un sens comme dans l'autre (voir DrawClassicMainMenu).
		static float _mainMenuAnim = 0f;

		// Fondu doux du titre au démarrage du menu principal.
		static float _mainMenuTitleFade = 0f;

		// Vitesse (unités/s sur une échelle 0-1) du fondu accueil <-> sauvegardes.
		const float MAIN_MENU_TRANSITION_SPEED = 3.2f;

		// Textures de rendu utilisées uniquement pendant la transition, pour fondre
		// l'écran d'accueil et le carrousel l'un dans l'autre sans les mélanger niveau input.
		static RenderTexture2D _menuTransitionIntroRT;
		static RenderTexture2D _menuTransitionCarouselRT;
		static bool _menuTransitionRTReady = false;
		static int _menuTransitionRTWidth = 0;
		static int _menuTransitionRTHeight = 0;

		static bool _mainMenuGearHovered = false;
		static bool _mainMenuStyleButtonHovered = false;

		static bool _mainMenuCenterHovered = false;

		static int _classicMenuSelectedSaveIndex = 0;

		static int _classicMenuSelectedCardIndex = 0;

		static int _classicMenuLastClickedCardIndex = -1;

		static double _classicMenuLastCardClickTime = -1d;

		static float _classicMenuSaveScroll = 0f;

		static bool _mouseInMenuBar = false;

		static float _menuBarAnimProgress = 0f;

		private static RadialMenu _radialMenu = new RadialMenu();

		static readonly string[] _emoteWheelOptions = new string[] { "wavedance", "sleep", "sit_floor", "hula_dance" };

		static Texture2D _creationMenuBg;

		// Icônes des catégories de personnalisation (assets/gui/creator_icon_*.png)
		static Texture2D _creatorIconHair;

		static Texture2D _creatorIconBeard;

		static Texture2D _creatorIconSkin;

		static Texture2D _creatorIconEyes;

		static Texture2D _creatorIconName;

		// Bouton "aléatoire" affiché à droite de la barre d'onglets (assets/gui/random.png)
		static Texture2D _creatorRandomIcon;

		private static bool TryGetPrimaryGamepadIndex(out int gamepadIndex)
		{
			gamepadIndex = -1;
			for (int index = 0; index < 4; index++)
			{
				if (!Raylib.IsGamepadAvailable(index)) continue;
				if (MathF.Abs(Raylib.GetGamepadAxisMovement(index, GamepadAxis.LeftX)) > 0.15f ||
					MathF.Abs(Raylib.GetGamepadAxisMovement(index, GamepadAxis.LeftY)) > 0.15f ||
					Raylib.IsGamepadButtonPressed(index, GamepadButton.LeftFaceUp) ||
					Raylib.IsGamepadButtonPressed(index, GamepadButton.LeftFaceDown) ||
					Raylib.IsGamepadButtonPressed(index, GamepadButton.LeftFaceLeft) ||
					Raylib.IsGamepadButtonPressed(index, GamepadButton.LeftFaceRight) ||
					Raylib.IsGamepadButtonPressed(index, GamepadButton.RightFaceUp) ||
					Raylib.IsGamepadButtonPressed(index, GamepadButton.RightFaceDown) ||
					Raylib.IsGamepadButtonPressed(index, GamepadButton.RightFaceLeft) ||
					Raylib.IsGamepadButtonPressed(index, GamepadButton.RightFaceRight) ||
					KeyBindings.CapturableGamepadButtons.Any(button => Raylib.IsGamepadButtonDown(index, button)))
				{
					gamepadIndex = index;
					return true;
				}
			}
			return false;
		}

		private static bool IsGamepadButtonPressedForPrimaryPlayer(GamepadButton button)
		{
			if (!TryGetPrimaryGamepadIndex(out int gamepadIndex)) return false;
			return Raylib.IsGamepadAvailable(gamepadIndex) && Raylib.IsGamepadButtonPressed(gamepadIndex, button);
		}

		private static void UpdateEmoteWheel(Vector2 mousePos)
		{
			Vector2 center = new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f);
			Vector2 dir = mousePos - center;
			float distance = dir.Length();
			if (distance < 30f)
			{
				_emoteWheelHoverIndex = -1;
				return;
			}

			float angle = MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI;
			angle = (angle + 360f + 90f) % 360f;
			int optionIndex = (int)(angle / (360f / _emoteWheelOptions.Length));
			if (optionIndex >= 0 && optionIndex < _emoteWheelOptions.Length && distance <= 240f)
			{
				_emoteWheelHoverIndex = optionIndex;
			}
			else
			{
				_emoteWheelHoverIndex = -1;
			}
		}

		/// <summary>Aide générique pour déclencher le son de survol sur un bouton : ne joue le son
		/// que lors de la transition "pas survolé" → "survolé". `wasHovered` doit être une variable
		/// statique/persistante propre à CE bouton, mise à jour par cet appel.</summary>
		private static void UpdateButtonHoverSound(bool isHovered, ref bool wasHovered)
		{
			if (isHovered && !wasHovered)
				PlayHoverSound();
			wasHovered = isHovered;
		}

		static Rectangle _mpConfirmButtonRect;

		static bool IsMouseOverMpButton(string which)
		{
			return which == "confirm" && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _mpConfirmButtonRect);
		}

		// Dessine un slider horizontal avec un fond dégradé
		// Retourne true si la valeur a changé pendant cette frame
		private static bool DrawSlider(int x, int y, int width, int height, ref float value, Func<float, Color> gradientFunc)
		{
			Rectangle sliderRect = new Rectangle(x, y, width, height);
			Vector2 mousePos = Raylib.GetMousePosition();

			// Dessiner le fond dégradé
			for (int i = 0; i <= width; i++)
			{
				float t = i / (float)width;
				Color col = gradientFunc(t);
				Raylib.DrawRectangle(x + i, y, 1, height, col);
			}
			Raylib.DrawRectangleLines(x, y, width, height, new Color(80, 70, 50, 200));

			// Dessiner le curseur
			int knobX = x + (int)(value * width);
			Raylib.DrawCircle(knobX, y + height / 2, 6, Color.White);
			Raylib.DrawCircle(knobX, y + height / 2, 5, Color.Black);

			bool changed = false;
			if (Raylib.CheckCollisionPointRec(mousePos, sliderRect) && Raylib.IsMouseButtonDown(MouseButton.Left))
			{
				float newValue = (mousePos.X - x) / width;
				newValue = Math.Clamp(newValue, 0, 1);
				if (Math.Abs(value - newValue) > 0.001f)
				{
					value = newValue;
					changed = true;
				}
			}
			return changed;
		}

		// Randomise tous les éléments (styles) et toutes les couleurs du personnage
		// dans le menu de création (bouton random.png).
		private static void RandomizeCharacterCustomization()
		{
			Random rnd = Random.Shared;

			Color RandomSkinTone()
			{
				// Teintes de peau plausibles (pas de couleurs saturées type "peau bleue").
				float h = rnd.Next(0, 40) / 360f; // orangé/rosé
				float s = 0.25f + (float)rnd.NextDouble() * 0.45f;
				float v = 0.45f + (float)rnd.NextDouble() * 0.5f;
				return HsvToColor(h, s, v);
			}

			Color RandomVividColor()
			{
				float h = (float)rnd.NextDouble();
				float s = 0.4f + (float)rnd.NextDouble() * 0.6f;
				float v = 0.35f + (float)rnd.NextDouble() * 0.6f;
				return HsvToColor(h, s, v);
			}

			_tempSkinColor = RandomSkinTone();
			_tempHairColor = RandomVividColor();
			_tempEyeColor = RandomVividColor();
			_tempHairStyle = hairBaseTextures.Count > 0 ? rnd.Next(0, hairBaseTextures.Count) : 0;
			_tempBeardStyle = beardBaseTextures.Count > 0 ? rnd.Next(0, beardBaseTextures.Count) : 0;
			_tempEyeStyle = EyeBaseTextures.Count > 0 ? rnd.Next(0, EyeBaseTextures.Count) : 0;
		}

		static void DrawMpTextField(string label, string value, int x, int y, int width, int fieldIndex)
		{
			FontManager.DrawText(label, x, y, 13, new Color(160, 150, 130, 220));
			Rectangle box = new Rectangle(x, y + 18, width, 36);
			bool focused = _mpFocusedField == fieldIndex;
			Raylib.DrawRectangleRounded(box, 0.15f, 6, new Color(10, 10, 16, 200));
			Raylib.DrawRectangleRoundedLines(box, 0.15f, 6, 2, focused ? new Color(230, 200, 100, 255) : new Color(80, 75, 60, 180));
			if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), box))
			{
				_mpFocusedField = fieldIndex;
				TextInput.FocusAtMouse($"multiplayer-field-{fieldIndex}", value, box);
			}
			TextInput.DrawSingleLine(value, $"multiplayer-field-{fieldIndex}", box, 16,
				Color.White, new Color(90, 110, 150, 220), new Color(230, 200, 100, 255), 12);
		}

		static void DrawAchievementsMenu()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			Vector2 mousePos = Raylib.GetMousePosition();
			float dt = Raylib.GetFrameTime();

			// ── Fond : exactement le même que le menu principal / options, pour rester cohérent. ──
			DrawScrollingMenuBackground(sw, sh, dt);

			int panelW = Math.Min(1100, sw - 80);
			int panelH = sh - 120;
			int px = (sw - panelW) / 2;
			int py = (sh - panelH) / 2;
			Raylib.DrawRectangleRounded(new Rectangle(px, py, panelW, panelH), 0.05f, 12, new Color(10, 10, 15, 225));
			Raylib.DrawRectangleRoundedLines(new Rectangle(px, py, panelW, panelH), 0.05f, 12, 2, new Color(80, 70, 50, 200));

			int titleFontSize = 36;
			int tw2 = FontManager.MeasureText(Localization.Get("menu.achievements"), titleFontSize);
			FontManager.DrawText(Localization.Get("menu.achievements"), px + panelW / 2 - tw2 / 2, py + 24, titleFontSize, new Color(230, 200, 100, 255));

			var (unlocked, total) = AchievementManager.GetCounts();
			string countText = $"{unlocked} / {total} débloqués";
			int countFontSize = 16;
			int cw = FontManager.MeasureText(countText, countFontSize);
			FontManager.DrawText(countText, px + panelW - cw - 40, py + 34, countFontSize, new Color(200, 190, 170, 220));

			// ── Zone de contenu scrollable (grille des succès, gérée par AchievementManager) ──
			int contentPad = 40;
			int contentY = py + 90;
			int contentX = px + contentPad;
			int contentW = panelW - contentPad * 2;
			int contentH = panelH - 90 - 80; // laisse la place au bouton Retour en bas
			AchievementManager.DrawGrid(contentX, contentY, contentW, contentH);

			// ── Bouton Retour, même style que le menu Options. ──
			Rectangle backRect2 = new Rectangle(px + panelW / 2 - 100, py + panelH - 66, 200, 46);
			bool backHovered2 = Raylib.CheckCollisionPointRec(mousePos, backRect2);
			Color backBg2 = backHovered2 ? new Color(60, 50, 35, 230) : new Color(25, 25, 35, 190);
			Raylib.DrawRectangleRounded(backRect2, 0.25f, 8, backBg2);
			Raylib.DrawRectangleRoundedLines(backRect2, 0.25f, 8, 2, backHovered2 ? new Color(230, 200, 100, 255) : new Color(100, 90, 70, 180));
			string backText2 = "RETOUR";
			int btw2 = FontManager.MeasureText(backText2, 20);
			FontManager.DrawText(backText2, (int)(backRect2.X + backRect2.Width / 2 - btw2 / 2), (int)(backRect2.Y + backRect2.Height / 2 - 10), 20, backHovered2 ? new Color(255, 220, 100, 255) : new Color(200, 190, 160, 220));
			if (backHovered2 && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				_gameState = _optionsReturnState;
			}
		}

		private static void PrintSection(string title, string icon)
		{
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.WriteLine($"\n  {icon}  {title}");
			Console.WriteLine(new string('─', 60));
			Console.ResetColor();
		}

		public static void ShowSavingIcon()
		{
			_savingDisplayTimer = SAVING_DISPLAY_DURATION;
		}

		private static bool TryParseMetaPair(string meta, out float cur, out float max)
        {
            cur = 0f;
            max = 0f;
            if (string.IsNullOrWhiteSpace(meta)) return false;
            char[] seps = new char[] { '/', '|', ':', '=' };
            var parts = meta.Split(seps, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                {
                    cur = v;
                    max = 100f;
                    return true;
                }
                return false;
            }
            if (parts.Length >= 2)
            {
                bool ok1 = float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v1);
                bool ok2 = float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v2);
                if (ok1 && ok2)
                {
                    cur = v1;
                    max = v2;
                    return true;
                }
            }
            return false;
        }
    }
}
