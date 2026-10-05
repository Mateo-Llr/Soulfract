// Program.Core.cs
#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Soulfract.WorldGeneration;
using DiscordRPC;

namespace Soulfract
{
    public static partial class Program
    {
		private static long _lastScreenshotScanAt;

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool AllocConsole();

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool AttachConsole(int dwProcessId);

		private static RenderTexture2D _mapRenderTexture = new RenderTexture2D();

		private static RenderTexture2D _worldRevealTexture = new RenderTexture2D();

		private static RenderTexture2D _menuRevealTexture = new RenderTexture2D();

		private static RenderTexture2D _loadingMenuTexture = new RenderTexture2D();
		private static Shader _worldRevealShader = new Shader();
		private static bool _worldRevealShaderLoaded = false;
		private static int _worldRevealProgressLocation = -1;

		private const string WorldRevealFragmentShader = @"
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;

uniform sampler2D texture0;
uniform float revealProgress;

void main()
{
	vec4 texel = texture(texture0, fragTexCoord) * fragColor;
	vec2 cells = floor(fragTexCoord * vec2(48.0, 32.0));
	vec2 cellCenter = (cells + vec2(0.5)) / vec2(48.0, 32.0);
	float distanceFromCenter = distance(cellCenter, vec2(0.5));
	float delay = clamp(distanceFromCenter / 0.7071, 0.0, 1.0) * 0.78;
	float localProgress = revealProgress - delay;
	float reveal = smoothstep(0.0, 0.18, localProgress);
	if (texel.a <= 0.001)
		discard;
	finalColor = vec4(texel.rgb, reveal);
}
";
		private static bool _mapRenderTextureInitialized = false;
		private static int _mapRenderTextureWidth = -1;
		private static int _mapRenderTextureHeight = -1;
		private static float _mapRenderTextureOriginTileX = float.MinValue;
		private static float _mapRenderTextureOriginTileY = float.MinValue;
		private static int _mapRenderTextureVisibleTilesX = -1;
		private static int _mapRenderTextureVisibleTilesY = -1;
		private static float _mapRenderTextureTileSize = -1f;
		private static bool _mapRenderTextureGodMode = false;
		private static bool _mapRenderTextureUnderground = false;
		private static bool _mapRenderTextureDungeon = false;
		private static int _mapCacheFloorTileX = int.MinValue;
		private static int _mapCacheFloorTileY = int.MinValue;

		// Nombre cible de tuiles échantillonnées par axe pour le rendu de la
		// carte, QUEL QUE SOIT le niveau de zoom. Le detailLevel (voir DrawMap)
		// est calculé pour que visibleTilesX/detailLevel et visibleTilesY/
		// detailLevel restent proches de cette valeur : la quantité de travail
		// par frame reste donc constante, et c'est la PRÉCISION (une tuile
		// monde = une case affichée, ou une case affichée pour plusieurs
		// tuiles monde) qui diminue quand on dézoome, jamais l'inverse.
		private const int MAP_TARGET_SAMPLES = 200;

		private static bool TryEnableDebugConsole()
		{
			try
			{
				if (Console.IsInputRedirected || Console.IsOutputRedirected)
				{
					return false;
				}

				try
				{
					if (AttachConsole(-1))
					{
						Console.Title = "Soulfract - Debug";
						return true;
					}
				}
				catch
				{
				}

				if (AllocConsole())
				{
					Console.Title = "Soulfract - Debug";
					Console.Clear();
					return true;
				}
			}
			catch
			{
			}

			return false;
		}

		static void DrawDeathScreen()
        {
            //  Vignette : opacité des bords qui augmente progressivement pendant la phase
            // Vignette, puis reste au maximum pour toutes les phases suivantes.
            float vignetteT = _deathScreenPhase == DeathScreenPhase.Vignette
                ? Math.Clamp(_deathScreenPhaseTimer / VIGNETTE_DURATION, 0f, 1f)
                : 1f;
            if (vignetteT > 0f)
            {
                int maxAlpha = (int)(180 * vignetteT);
                int steps = 10;
                for (int i = 0; i < steps; i++)
                {
                    float f = (float)i / steps;
                    int alpha = (int)(maxAlpha * (1f - f));
                    int thickness = (int)(ScreenWidth * 0.12f * (1f - f) / steps) + 2;
                    Color c = new Color(0, 0, 0, alpha);
                    Raylib.DrawRectangle(0, i * thickness, ScreenWidth, thickness, c);
                    Raylib.DrawRectangle(0, ScreenHeight - (i + 1) * thickness, ScreenWidth, thickness, c);
                    Raylib.DrawRectangle(i * thickness, 0, thickness, ScreenHeight, c);
                    Raylib.DrawRectangle(ScreenWidth - (i + 1) * thickness, 0, thickness, ScreenHeight, c);
                }
            }

            if (_deathScreenPhase == DeathScreenPhase.Vignette)
                return;

            int centerX = ScreenWidth / 2;
            int centerY = ScreenHeight / 2 - 40;

            //  Crâne : léger effet d'impulsion (pop) à l'apparition -> dépasse légèrement sa
            // taille finale puis revient, plutôt qu'un simple fade-in statique.
            float skullScale = 1f;
            if (_deathScreenPhase == DeathScreenPhase.SkullPop)
            {
                float t = Math.Clamp(_deathScreenPhaseTimer / SKULL_POP_DURATION, 0f, 1f);
                // Montée rapide avec léger dépassement (pop) puis retour à la taille finale.
                float bounce = t < 0.6f
                    ? MathHelper.Lerp(0f, 1.25f, t / 0.6f)
                    : MathHelper.Lerp(1.25f, 1f, (t - 0.6f) / 0.4f);
                skullScale = bounce;
            }

            Texture2D skull = LoadDeathSkullTexture();
            int baseSize = 168;
            int size = (int)(baseSize * skullScale);
            if (skull.Id != 0)
            {
                Rectangle src = new Rectangle(0, 0, skull.Width, skull.Height);
                Rectangle dst = new Rectangle(centerX - size / 2f, centerY - size / 2f, size, size);
                Raylib.DrawTexturePro(skull, src, dst, Vector2.Zero, 0f, Color.White);
            }
            else
            {
                // Repli si l'asset n'est pas présent : simple représentation textuelle.
                int fallbackFontSize = size;
                string glyph = "";
                int gw = Raylib.MeasureText(glyph, fallbackFontSize);
                FontManager.DrawText(glyph, centerX - gw / 2, centerY - fallbackFontSize / 2, fallbackFontSize, Color.White);
            }

            if (_deathScreenPhase == DeathScreenPhase.SkullPop)
                return;

            //  Message qui s'écrit progressivement, en rouge, sous le crâne.
            string visibleText = _deathMessage.Substring(0, Math.Min(_deathMessageCharsShown, _deathMessage.Length));
            int msgFontSize = 32;
            int maxWidth = (int)(ScreenWidth * 0.65f);
            var wrapped = WrapTextToWidth(visibleText, msgFontSize, maxWidth);
            int lineHeight = msgFontSize + 10;
            int textStartY = centerY + baseSize / 2 + 32;
            for (int i = 0; i < wrapped.Count; i++)
            {
                int lw = Raylib.MeasureText(wrapped[i], msgFontSize);
                FontManager.DrawText(wrapped[i], centerX - lw / 2, textStartY + i * lineHeight, msgFontSize, Color.Red);
            }

            if (_deathScreenPhase == DeathScreenPhase.WaitingForButton || _deathScreenPhase == DeathScreenPhase.TypingMessage)
                return;

            //  Bouton "Réapparaître", uniquement une fois le message affiché en entier et
            // le petit délai post-texte écoulé.
            if (_deathScreenPhase == DeathScreenPhase.ButtonReady)
            {
                string label = "Réapparaître";
                int btnFontSize = 26;
                int textW = Raylib.MeasureText(label, btnFontSize);
                int btnW = textW + 70;
                int btnH = 56;
                int btnX = centerX - btnW / 2;
                int btnY = ScreenHeight - 110;
                _respawnButtonRect = new Rectangle(btnX, btnY, btnW, btnH);

                Vector2 mouse = Raylib.GetMousePosition();
                bool hovered = Raylib.CheckCollisionPointRec(mouse, _respawnButtonRect);
                Color bg = hovered ? new Color(150, 30, 30, 230) : new Color(110, 20, 20, 220);
                Raylib.DrawRectangleRec(_respawnButtonRect, bg);
                Raylib.DrawRectangleLinesEx(_respawnButtonRect, 2, Color.Red);
                FontManager.DrawText(label, btnX + 30, btnY + (btnH - btnFontSize) / 2, btnFontSize, Color.White);
            }
        }

		// Rempli si le jeu a été relancé automatiquement avec --autohost-port=XXXX
		// (après une élévation UAC déclenchée depuis StartHostingGame). Consommé
		// une fois l'écran principal prêt pour lancer l'hébergement sans repasser
		// par le menu.
		private static int? _autoHostPortOnStartup = null;

		private enum MainMenuFadePhase { None, FadeOut, FadeIn }
		private static MainMenuFadePhase _mainMenuFadePhase = MainMenuFadePhase.None;
		private static float _mainMenuFadeAlpha = 0f;
		private const float MAIN_MENU_FADE_DURATION = 0.45f;
		private static bool _pendingReturnToMainMenuCleanup = false;

		// Écran de démarrage (icône + barre de progression) affiché pendant le
		// chargement des assets, des données de jeu et de l'audio, pour éviter
		// l'écran blanc "ne répond pas" pendant InitWindow -> premier rendu.

		private static void RunStartupSplashSequence()
		{
			Texture2D splashLogo = default;
			bool hasSplashLogo = false;
			if (File.Exists("assets/icon.png"))
			{
				Image splashImg = Raylib.LoadImage("assets/icon.png");
				splashLogo = Raylib.LoadTextureFromImage(splashImg);
				Raylib.UnloadImage(splashImg);
				hasSplashLogo = true;
			}

			float splashProgress = 0f;
			string splashLabel = "Démarrage...";

			void DrawSplashFrame()
			{
				// Pompe les événements Windows pour éviter le "ne répond pas"
				// tant qu'on n'est pas encore rentré dans la boucle principale.
				Raylib.WindowShouldClose();

				int sw = Raylib.GetScreenWidth();
				int sh = Raylib.GetScreenHeight();

				Raylib.BeginDrawing();
				Raylib.ClearBackground(new Color(20, 20, 24, 255));

				string title = "Soulfract";
				int titleSize = 26;
				int titleW = Raylib.MeasureText(title, titleSize);
				Raylib.DrawText(title, sw / 2 - titleW / 2, 20, titleSize, Color.White);

				if (hasSplashLogo)
				{
					int logoSize = 96;
					Rectangle src = new Rectangle(0, 0, splashLogo.Width, splashLogo.Height);
					Rectangle dst = new Rectangle(sw / 2f - logoSize / 2f, 60, logoSize, logoSize);
					Raylib.DrawTexturePro(splashLogo, src, dst, Vector2.Zero, 0f, Color.White);
				}

				int barW = Math.Max(220, sw - 80);
				int barH = 18;
				int barX = sw / 2 - barW / 2;
				int barY = sh - 60;
				const int segmentCount = 14;
				const int segmentGap = 4;
				int segmentW = (barW - segmentGap * (segmentCount - 1)) / segmentCount;
				float clampedProgress = Math.Clamp(splashProgress, 0f, 1f);

				Raylib.DrawRectangle(barX, barY, barW, barH, new Color(34, 34, 39, 255));
				for (int i = 0; i < segmentCount; i++)
				{
					int x = barX + i * (segmentW + segmentGap);
					int segmentFill = (int)Math.Floor((clampedProgress * segmentCount) - i);
					if (segmentFill > 0)
					{
						int fillWidth = Math.Min(segmentW, segmentW * Math.Clamp(segmentFill, 0, 1));
						if (fillWidth > 0)
						{
							Raylib.DrawRectangle(x + 1, barY + 2, fillWidth - 2, barH - 4, new Color(232, 190, 76, 255));
							Raylib.DrawRectangle(x + 1, barY + 2, fillWidth - 2, 6, new Color(255, 226, 167, 130));
						}
					}
					Raylib.DrawRectangleLines(x, barY, segmentW, barH, new Color(90, 90, 97, 255));
				}
				Raylib.DrawRectangleLines(barX, barY, barW, barH, new Color(120, 120, 130, 255));

				int labelSize = 18;
				int labelW = Raylib.MeasureText(splashLabel, labelSize);
				Raylib.DrawText(splashLabel, sw / 2 - labelW / 2, barY + barH + 16, labelSize, new Color(200, 200, 208, 255));

				Raylib.EndDrawing();
			}

			void SetSplashStep(string label, float progress)
			{
				splashLabel = label;
				splashProgress = progress;
				DrawSplashFrame();
			}

			// Dessine une première frame immédiatement pour ne jamais laisser
			// l'écran blanc, avant même de commencer à charger quoi que ce soit.
			SetSplashStep("Démarrage...", 0f);

			SetSplashStep("Initialisation audio...", 0.03f);
			Raylib.InitAudioDevice();
			MusicPlayer.Init(); // charge assets/soundfonts/default.sf2

			SetSplashStep("Chargement des paramètres...", 0.08f);
			Localization.Initialize();
			SettingsManager.Load();
			Program._masterVolume = SettingsManager.Settings.MasterVolume;
			Program._fullscreen = SettingsManager.Settings.Fullscreen;
			Localization.SetLanguage(SettingsManager.Settings.Language);

			SetSplashStep("Chargement des données du jeu...", 0.15f);
			LoadAllGameData();

			// LoadAssets rapporte sa propre progression en 8 étapes ; on la
			// mappe sur la portion [0.20 ; 0.92] de la barre globale.
			LoadAssets((label, fraction) => SetSplashStep(label, 0.20f + fraction * 0.72f));

			SetSplashStep("Finalisation...", 0.96f);
			InitializeDiscordRichPresence();

			SetSplashStep("Prêt !", 1f);

			if (hasSplashLogo)
				Raylib.UnloadTexture(splashLogo);
		}

		static void Main(string[] args)
		{
			TryEnableDebugConsole();

			if (TryConsumeAutoHostArg(args, out int autoHostPort))
			{
				_autoHostPortOnStartup = autoHostPort;
			}

			AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
			{
				try
				{
					LogCrash(e.ExceptionObject as Exception, "UnhandledException");
				}
				catch
				{
				}
			};

			TaskScheduler.UnobservedTaskException += (sender, e) =>
			{
				e.SetObserved();
			};

			Raylib.SetTraceLogLevel(TraceLogLevel.None);
		try { Console.Clear(); } catch { } // Ignore console clear errors in some terminals
			Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
			
			// Fenêtre de démarrage : petite, non redimensionnable, centrée.
			// On ne met PAS ConfigFlags.ResizableWindow ici pour garder une
			// taille fixe pendant l'écran de chargement.
			const int SplashWidth = 420;
			const int SplashHeight = 260;
			Raylib.InitWindow(SplashWidth, SplashHeight, "Soulfract");
			Raylib.SetWindowState(ConfigFlags.UndecoratedWindow);

			int monitor = Raylib.GetCurrentMonitor();
			int monW = Raylib.GetMonitorWidth(monitor);
			int monH = Raylib.GetMonitorHeight(monitor);
			Raylib.SetWindowPosition((monW - SplashWidth) / 2, (monH - SplashHeight) / 2);
			
			if (File.Exists("assets/icon.png"))
			{
				Image icon = Raylib.LoadImage("assets/icon.png");
				Raylib.SetWindowIcon(icon);
				Raylib.UnloadImage(icon);
				Console.WriteLine("   Icône de fenêtre chargée");
			}
			else
			{
				Console.WriteLine("   assets/icon.png manquant - icône par défaut utilisée");
			}
			
			RunStartupSplashSequence();

			// Passage de la petite fenêtre de démarrage à la vraie fenêtre du
			// jeu, redimensionnable et centrée.
			Raylib.ClearWindowState(ConfigFlags.UndecoratedWindow);
			Raylib.SetWindowState(ConfigFlags.ResizableWindow);
			Raylib.SetWindowSize(ScreenWidth, ScreenHeight);
			Raylib.SetWindowPosition((monW - ScreenWidth) / 2, (monH - ScreenHeight) / 2);
			Raylib.SetWindowTitle("Soulfract");

			// Force un premier rendu noir après le redimensionnement pour effacer le
			// vestige visuel de la petite fenêtre de démarrage (splash), qui peut
			// rester visible en coin haut gauche pendant quelques frames.
			Raylib.BeginDrawing();
			Raylib.ClearBackground(Color.Black);
			Raylib.EndDrawing();

			var selectedLanguage = Localization.AvailableLanguages.ToList().FindIndex(l => l.Code == Localization.CurrentLanguage);
			_languageSelectionIndex = selectedLanguage >= 0 ? selectedLanguage : 0;

			SkillSystem.OnGlobalLevelUp += (newLevel) =>
			{
				PlayLevelUpSound();
				SpawnLevelUpParticles(GetPlayerPosition());
			};
			
			Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
			Console.WriteLine($"║ {Localization.Get("ui.loading_complete"),64} ║");
			Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
			Console.WriteLine();

			float facing = 1f;
			Vector2 hitboxDim = new(TileSize * 0.9f, TileSize * 0.4f);

			_inventoryBuffer = new List<Item>
			{
				MakeItem("log", 5), MakeItem("apple", 3), MakeItem("wooden_axe", 1), MakeItem("rock", 10), MakeItem("plank", 20),
			};

			InventoryRenderer.Initialize(40);
			ContainerUI.Initialize(InventoryRenderer.DraggedItem);
			CauldronUI.Initialize(InventoryRenderer.DraggedItem);
			PortableContainerUI.Initialize(InventoryRenderer.DraggedItem);
			DyeingUI.Initialize(InventoryRenderer.DraggedItem);
			ArmorStandUI.Initialize(InventoryRenderer.DraggedItem);
			InventoryRenderer.ConvertToNewInventory(_inventoryBuffer);
			RitualCircleUI.Initialize(InventoryRenderer.DraggedItem);
			InventoryRenderer.IsInventoryOpen = false;

			int craftSelected = 0;

			EnsureLocalPlayersInitialized();

			//  Si on vient d'être relancé en admin pour héberger (voir StartHostingGame /
			// FirewallHelper.RelaunchAsAdministrator), on démarre directement le serveur
			// sur le port demandé, sans repasser par le menu Héberger/Rejoindre.
			if (_autoHostPortOnStartup.HasValue)
			{
				int autoPort = _autoHostPortOnStartup.Value;
				_autoHostPortOnStartup = null;
				StartHostingGame(autoPort);
			}

			var camera = new Camera2D { Offset = new Vector2(ScreenWidth/2f, ScreenHeight/2f), Zoom = GetCameraViewportScale(ScreenWidth, ScreenHeight) };
			_cameraViewportScale = camera.Zoom;
			_smoothedCameraTarget = GetPlayerCameraTarget(_playerPos);
			camera.Target = _smoothedCameraTarget;
			ApplyPreciseCameraTransform(ref camera);
			_cameraNeedsImmediateCenter = true;

			try
			{
				Raylib.SetExitKey(KeyboardKey.Null);
				
				while (!Raylib.WindowShouldClose())
				{
					_currentFrame++; //  OPTIM : sert à invalider les caches "1 calcul par frame" (GetLocalPlayers, etc.)
					float dt = Raylib.GetFrameTime();
					if (dt > 0.1f) dt = 0.1f; // Limiter à 100 ms pour éviter les bonds
					ScreenWidth = Raylib.GetScreenWidth();
					ScreenHeight = Raylib.GetScreenHeight();
					UIManager.UpdateForViewport(ScreenWidth, ScreenHeight);
					camera.Offset = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f);
					AdaptCameraToViewport(ref camera, ScreenWidth, ScreenHeight);
					if (_gameState == GameState.Playing && _cameraNeedsImmediateCenter)
					{
						Vector2 immediateTarget = _isSitting ? _sittingPosition : GetPlayerCameraTarget(_playerPos);
						_smoothedCameraTarget = immediateTarget;
						camera.Target = immediateTarget;
						ApplyPreciseCameraTransform(ref camera);
						_cameraNeedsImmediateCenter = false;
					}
					ApplyPreciseCameraTransform(ref camera);
				bool debugPanelClickConsumed = _showDebug && Renderer.HandleDebugMenuInput();
				if (_mainMenuFadePhase == MainMenuFadePhase.None && !debugPanelClickConsumed && !CharacterAnimationEditorUI.IsOpen)
					HandleInput(camera, dt);
                    
                    ChatSystem.Update();
                    NetworkManager.Update(); //  MULTIJOUEUR : traite les messages réseau reçus chaque frame
					bool voiceSessionActive = NetworkManager.IsOnline && _gameState == GameState.Playing && !ChatSystem.IsOpen;
					bool transmitVoice = SettingsManager.Settings.VoiceChatMode switch
					{
						"Disabled" => false,
						"AlwaysOn" => NetworkManager.IsOnline,
						_ => voiceSessionActive && KeyBindings.IsDown(GameAction.PushToTalk)
					};
					VoiceChat.Update(transmitVoice);
					UpdatePouilleuxInvitation(dt);

                        TryConnectLocalPlayers();
                    UpdateAdditionalLocalPlayers(dt, hitboxDim, camera);
                    if (_fadeOutOverlayActive) UpdateLoadingFadeOutOverlay(dt);
                    if (_mainMenuFadePhase != MainMenuFadePhase.None) UpdateMainMenuFade(dt);

                    if (_gameState == GameState.Loading) UpdateLoadingScreen(dt);
					else if (_gameState == GameState.MainMenu && !CharacterAnimationEditorUI.IsOpen && !_isSelectingSave && !_isMultiplayerMenu && !_isJoinCharacterCreation) UpdateMainMenu();
                    else if (_isMultiplayerMenu && !_isJoinCharacterCreation) UpdateMultiplayerMenu();
                    else if (_gameState == GameState.Options) UpdateOptionsMenu();
					else if (_gameState == GameState.Playing && _mainMenuFadePhase == MainMenuFadePhase.None && !_fadeOutOverlayActive && !debugPanelClickConsumed)
						UpdateGameplay(dt, ref _playerPos, ref facing, hitboxDim, ref craftSelected, ref camera);

					Weather.SetPaused(_gameState != GameState.Playing);
					AmbientAudio.SetPaused(_gameState != GameState.Playing);
					Draw(_playerPos, facing, hitboxDim, camera, craftSelected, _showDebug);
					OrganizeScreenshots();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"\n ERREUR: {ex.Message}");
				Console.WriteLine($"\n STACK TRACE:\n{ex.StackTrace}");
				if (!Console.IsInputRedirected)
				{
					Console.WriteLine("\nAppuyez sur une touche pour fermer...");
					Console.ReadKey();
				}
			}
			finally
			{
				FontManager.UnloadFont();
				Raylib.CloseAudioDevice();

				//  MULTIJOUEUR : se déconnecter proprement avant de quitter
				NetworkManager.Disconnect();
				
				SaveCurrentGameOnExit();
				SettingsManager.Save();
				TraderUI.Close();
				InventoryRenderer.CloseInventory();
				ContainerUI.Close();
				ArmorStandUI.Close();
				TamedAnimalUI.Close();
				TamedAnimalInteraction.Close();
				_discordClient?.Dispose();
				World.UnloadWaterShader();
				Raylib.CloseWindow();
			}
		}

		private static void OrganizeScreenshots()
		{
			long now = Environment.TickCount64;
			if (now - _lastScreenshotScanAt < 500)
				return;
			_lastScreenshotScanAt = now;

			try
			{
				string workingDirectory = Environment.CurrentDirectory;
				string[] screenshots = Directory.GetFiles(workingDirectory, "screenshot*.png", SearchOption.TopDirectoryOnly);
				if (screenshots.Length == 0)
					return;

				string screenshotDirectory = Path.Combine(workingDirectory, "Screenshots");
				Directory.CreateDirectory(screenshotDirectory);
				foreach (string screenshot in screenshots)
				{
					string fileName = Path.GetFileName(screenshot);
					string destination = Path.Combine(screenshotDirectory, fileName);
					string extension = Path.GetExtension(fileName);
					string name = Path.GetFileNameWithoutExtension(fileName);
					for (int suffix = 1; File.Exists(destination); suffix++)
						destination = Path.Combine(screenshotDirectory, $"{name}-{suffix}{extension}");

					File.Move(screenshot, destination);
				}
			}
			catch (IOException exception)
			{
				Console.Error.WriteLine($"Impossible de déplacer une capture vers le dossier Screenshots : {exception.Message}");
			}
			catch (UnauthorizedAccessException exception)
			{
				Console.Error.WriteLine($"Accès refusé au dossier Screenshots : {exception.Message}");
			}
		}

		private struct MapLayout
		{
			public float TileSize;
			public int VisibleTilesX;
			public int VisibleTilesY;
			public int MapX;
			public int MapY;
			public int MapAreaW;
			public int MapAreaH;
		}

		private static MapLayout ComputeMapLayout(int sw, int sh, float zoom)
		{
			float tileSize = MAP_TILE_SIZE_BASE * zoom;
			float mapAreaWidth = sw - 80;
			float mapAreaHeight = sh - 100;

			int visibleTilesX = Math.Max(30, (int)(mapAreaWidth / tileSize));
			int visibleTilesY = Math.Max(20, (int)(mapAreaHeight / tileSize));

			return new MapLayout
			{
				TileSize = tileSize,
				VisibleTilesX = visibleTilesX,
				VisibleTilesY = visibleTilesY,
				MapX = 40,
				MapY = 50,
				MapAreaW = Math.Max(1, (int)Math.Ceiling(visibleTilesX * tileSize)),
				MapAreaH = Math.Max(1, (int)Math.Ceiling(visibleTilesY * tileSize)),
			};
		}

		static void DrawMap()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			int ts = Program.TileSize;

			Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 220));

			MapLayout layout = ComputeMapLayout(sw, sh, _mapZoom);
			float currentTileSize = layout.TileSize;
			int visibleTilesX = layout.VisibleTilesX;
			int visibleTilesY = layout.VisibleTilesY;
			int mapX = layout.MapX;
			int mapY = layout.MapY;
			int mapAreaW = layout.MapAreaW;
			int mapAreaH = layout.MapAreaH;

			Vector2 playerTilePos = new Vector2(_playerPos.X / ts, _playerPos.Y / ts);
			float halfWidth = visibleTilesX / 2f;
			float halfHeight = visibleTilesY / 2f;
			float startTileX = playerTilePos.X - halfWidth + _mapOffset.X;
			float startTileY = playerTilePos.Y - halfHeight + _mapOffset.Y;

			int floorStartTileX = (int)Math.Floor(startTileX);
			int floorStartTileY = (int)Math.Floor(startTileY);
			float fracX = startTileX - floorStartTileX;
			float fracY = startTileY - floorStartTileY;

			int cacheTilesX = visibleTilesX + 2;
			int cacheTilesY = visibleTilesY + 2;
			int cacheAreaW = Math.Max(1, (int)Math.Ceiling(cacheTilesX * currentTileSize));
			int cacheAreaH = Math.Max(1, (int)Math.Ceiling(cacheTilesY * currentTileSize));

			bool needsMapTextureRefresh =
				!_mapRenderTextureInitialized ||
				_mapRenderTextureWidth != cacheAreaW ||
				_mapRenderTextureHeight != cacheAreaH ||
				_mapCacheFloorTileX != floorStartTileX ||
				_mapCacheFloorTileY != floorStartTileY ||
				_mapRenderTextureVisibleTilesX != visibleTilesX ||
				_mapRenderTextureVisibleTilesY != visibleTilesY ||
				Math.Abs(_mapRenderTextureTileSize - currentTileSize) > 0.001f ||
				_mapRenderTextureGodMode != Program.IsGodMode ||
				_mapRenderTextureUnderground != World.IsUnderground ||
				_mapRenderTextureDungeon != World.IsInDungeon;

			if (needsMapTextureRefresh)
			{
				if (_mapRenderTextureInitialized && _mapRenderTexture.Id != 0)
					Raylib.UnloadRenderTexture(_mapRenderTexture);

				_mapRenderTexture = Raylib.LoadRenderTexture(cacheAreaW, cacheAreaH);
				_mapRenderTextureInitialized = true;
				_mapRenderTextureWidth = cacheAreaW;
				_mapRenderTextureHeight = cacheAreaH;
				_mapCacheFloorTileX = floorStartTileX;
				_mapCacheFloorTileY = floorStartTileY;
				_mapRenderTextureVisibleTilesX = visibleTilesX;
				_mapRenderTextureVisibleTilesY = visibleTilesY;
				_mapRenderTextureTileSize = currentTileSize;
				_mapRenderTextureGodMode = Program.IsGodMode;
				_mapRenderTextureUnderground = World.IsUnderground;
				_mapRenderTextureDungeon = World.IsInDungeon;

				Raylib.BeginTextureMode(_mapRenderTexture);
				Raylib.ClearBackground(new Color(0, 0, 0, 0));

				// PERF : le detailLevel doit continuer à grandir avec le dézoom, pas
				// être plafonné à une valeur fixe (avant : max 4x, donc à un dézoom
				// extrême avec des milliers de tuiles visibles, la boucle traitait
				// quand même des milliers d'itérations -> gros lag). Ici on calcule
				// le detailLevel nécessaire pour que le nombre de tuiles RÉELLEMENT
				// traitées reste borné par MAP_TARGET_SAMPLES dans chaque axe, quel
				// que soit le niveau de zoom : plus on dézoome, moins la carte est
				// précise (on saute plus de tuiles), mais la quantité de travail par
				// frame reste à peu près constante.
				int detailLevel = Math.Max(1, (int)Math.Ceiling(Math.Max(visibleTilesX, visibleTilesY) / (float)MAP_TARGET_SAMPLES));

				float renderTileSize = currentTileSize * detailLevel;

				for (int screenX = -1; screenX < visibleTilesX + 1; screenX += detailLevel)
				{
					int worldTileX = floorStartTileX + screenX;
					float drawX = (screenX + 1) * currentTileSize;

					for (int screenY = -1; screenY < visibleTilesY + 1; screenY += detailLevel)
					{
						int worldTileY = floorStartTileY + screenY;

						Color tileColor;
						bool shouldRenderTile = Program.IsGodMode || World.IsUnderground || World.IsTileExplored(worldTileX, worldTileY);
						tileColor = shouldRenderTile
							? GetTileColorForMap(worldTileX, worldTileY)
							: new Color(30, 30, 40, 255);

						float drawY = (screenY + 1) * currentTileSize;
						Raylib.DrawRectangleRec(new Rectangle(drawX, drawY, renderTileSize + 1f, renderTileSize + 1f), tileColor);
					}
				}

				// Espacement de la grille proportionnel au detailLevel (sinon, très
				// dézoomé, on recalculerait une grille tous les 10 tuiles alors que
				// chaque "pixel" de la carte représente déjà des dizaines de tuiles).
				Color gridColor = new Color(255, 255, 255, 18);
				int gridSpacing = Math.Max(10, detailLevel * 10);
				for (int screenX = -1; screenX < visibleTilesX + 1; screenX += detailLevel)
				{
					int worldTileX = floorStartTileX + screenX;
					if (worldTileX % gridSpacing != 0) continue;
					float lineX = (screenX + 1) * currentTileSize;
					Raylib.DrawLineEx(new Vector2(lineX, 0), new Vector2(lineX, cacheAreaH), 1f, gridColor);
				}
				for (int screenY = -1; screenY < visibleTilesY + 1; screenY += detailLevel)
				{
					int worldTileY = floorStartTileY + screenY;
					if (worldTileY % gridSpacing != 0) continue;
					float lineY = (screenY + 1) * currentTileSize;
					Raylib.DrawLineEx(new Vector2(0, lineY), new Vector2(cacheAreaW, lineY), 1f, gridColor);
				}

				Raylib.EndTextureMode();
			}

			Rectangle panelRect = new Rectangle(mapX - 6, mapY - 6, mapAreaW + 12, mapAreaH + 12);
			Rectangle shadowRect = new Rectangle(panelRect.X + 6, panelRect.Y + 8, panelRect.Width, panelRect.Height);
			Raylib.DrawRectangleRounded(shadowRect, 0.04f, 8, new Color(0, 0, 0, 120));
			Raylib.DrawRectangleRounded(panelRect, 0.04f, 8, new Color(18, 18, 26, 235));

			Raylib.BeginScissorMode(mapX, mapY, mapAreaW, mapAreaH);

			float offsetX = mapX - currentTileSize - fracX * currentTileSize;
			float offsetY = mapY - currentTileSize - fracY * currentTileSize;

			Rectangle src = new Rectangle(0, 0, _mapRenderTexture.Texture.Width, -_mapRenderTexture.Texture.Height);
			Rectangle dst = new Rectangle(offsetX, offsetY, _mapRenderTexture.Texture.Width, _mapRenderTexture.Texture.Height);
			Raylib.DrawTexturePro(_mapRenderTexture.Texture, src, dst, Vector2.Zero, 0f, Color.White);

			Raylib.EndScissorMode();

			Raylib.DrawRectangleRoundedLines(panelRect, 0.04f, 8, 2, new Color(110, 110, 135, 220));

			string title = Localization.Get("map.title");
			if (string.IsNullOrEmpty(title)) title = "Carte";
			FontManager.DrawText(title, mapX, mapY - 30, 20, new Color(230, 230, 240, 255));

			int compassCx = mapX + mapAreaW - 26;
			int compassCy = mapY + 26;
			Raylib.DrawCircle(compassCx, compassCy, 20, new Color(0, 0, 0, 140));
			Raylib.DrawCircleLines(compassCx, compassCy, 20, new Color(150, 150, 170, 200));
			FontManager.DrawText("N", compassCx - 4, compassCy - 18, 14, Color.White);
			FontManager.DrawText("S", compassCx - 4, compassCy + 4, 14, new Color(180, 180, 190, 255));
			FontManager.DrawText("E", compassCx + 8, compassCy - 6, 14, new Color(180, 180, 190, 255));
			FontManager.DrawText("O", compassCx - 18, compassCy - 6, 14, new Color(180, 180, 190, 255));

			Texture2D mapMarker = LoadMapMarkerTexture();
			float markerSize = 24f;

			float markerMapX = mapX + (playerTilePos.X - startTileX) * currentTileSize;
			float markerMapY = mapY + (playerTilePos.Y - startTileY) * currentTileSize;

			bool isMarkerInside = markerMapX >= mapX && markerMapX <= mapX + mapAreaW &&
						markerMapY >= mapY && markerMapY <= mapY + mapAreaH;

			if (isMarkerInside)
			{
				Raylib.DrawEllipse((int)markerMapX, (int)(markerMapY + markerSize * 0.32f), markerSize * 0.35f, markerSize * 0.14f, new Color(0, 0, 0, 90));
				Vector2 markerPos = new Vector2(markerMapX - markerSize / 2, markerMapY - markerSize / 2);
				DrawMapMarker(mapMarker, markerPos, markerSize, _playerFacing);
			}
			else
			{
				Vector2 centerMap = new Vector2(mapX + mapAreaW / 2f, mapY + mapAreaH / 2f);
				Vector2 direction = new Vector2(markerMapX - centerMap.X, markerMapY - centerMap.Y);
				direction = direction.Length() > 0.01f ? Vector2.Normalize(direction) : new Vector2(1, 0);

				Vector2 edgePoint = GetIntersectionWithRectEdge(centerMap, direction, mapX, mapY, mapAreaW, mapAreaH);
				Vector2 edgeMarkerPos = new Vector2(edgePoint.X - markerSize / 2, edgePoint.Y - markerSize / 2);
				DrawMapMarker(mapMarker, edgeMarkerPos, markerSize, _playerFacing);
				Raylib.DrawLineEx(centerMap, edgePoint, 2, new Color(255, 255, 255, 120));
			}

			if (HasDiedAtLeastOnce)
			{
				Texture2D skullMarker = LoadSkullMapTexture();
				if (skullMarker.Id != 0)
				{
					Vector2 deathTilePos = new Vector2(LastDeathPosition.X / ts, LastDeathPosition.Y / ts);
					float skullMapX = mapX + (deathTilePos.X - startTileX) * currentTileSize;
					float skullMapY = mapY + (deathTilePos.Y - startTileY) * currentTileSize;
					bool isSkullInside = skullMapX >= mapX && skullMapX <= mapX + mapAreaW &&
								  skullMapY >= mapY && skullMapY <= mapY + mapAreaH;
					if (isSkullInside)
					{
						float skullSize = 20f;
						Rectangle skullSrc = new Rectangle(0, 0, skullMarker.Width, skullMarker.Height);
						Rectangle skullDest = new Rectangle(skullMapX - skullSize / 2f, skullMapY - skullSize / 2f, skullSize, skullSize);
						Raylib.DrawTexturePro(skullMarker, skullSrc, skullDest, Vector2.Zero, 0f, Color.White);
					}
				}
			}

			Vector2 mousePos = Raylib.GetMousePosition();
			if (Raylib.IsMouseButtonPressed(MouseButton.Right) &&
				mousePos.X >= mapX && mousePos.X <= mapX + mapAreaW &&
				mousePos.Y >= mapY && mousePos.Y <= mapY + mapAreaH)
			{
				if (Program.IsGodMode)
				{
					float targetTileX = startTileX + (mousePos.X - mapX) / currentTileSize;
					float targetTileY = startTileY + (mousePos.Y - mapY) / currentTileSize;
					Vector2 targetWorld = new Vector2(
						targetTileX * ts + ts / 2f,
						targetTileY * ts + ts / 2f);
					SetPlayerPosition(targetWorld);
					_smoothedCameraTarget = targetWorld;
					_isDraggingMap = false;
					_gameState = GameState.Playing;
				}
			}
			if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
				mousePos.X >= mapX && mousePos.X <= mapX + mapAreaW &&
				mousePos.Y >= mapY && mousePos.Y <= mapY + mapAreaH)
			{
				_isDraggingMap = true;
				_mapDragStart = mousePos;
			}
			if (_isDraggingMap && Raylib.IsMouseButtonDown(MouseButton.Left))
			{
				Vector2 delta = mousePos - _mapDragStart;
				_mapOffset.X += delta.X / currentTileSize;
				_mapOffset.Y += delta.Y / currentTileSize;
				_mapDragStart = mousePos;
			}
			if (Raylib.IsMouseButtonReleased(MouseButton.Left)) _isDraggingMap = false;

			if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(mapX, mapY, mapAreaW, mapAreaH)))
			{
				float wheel = Raylib.GetMouseWheelMove();
				if (wheel != 0)
				{
					Vector2 mouseMapPos = new Vector2(
						(mousePos.X - mapX) / currentTileSize,
						(mousePos.Y - mapY) / currentTileSize
					);

					_mapZoom = Math.Clamp(_mapZoom + wheel * MAP_ZOOM_STEP, MAP_ZOOM_MIN, MAP_ZOOM_MAX);
					MapLayout newLayout = ComputeMapLayout(sw, sh, _mapZoom);


					Vector2 newMouseMapPos = new Vector2(
						(mousePos.X - newLayout.MapX) / newLayout.TileSize,
						(mousePos.Y - newLayout.MapY) / newLayout.TileSize
					);

					float oldHalfWidth = visibleTilesX / 2f;
					float oldHalfHeight = visibleTilesY / 2f;
					float newHalfHeight = newLayout.VisibleTilesY / 2f;

					_mapOffset += (mouseMapPos - newMouseMapPos);
				}
			}

			string zoomText = $"Zoom : {_mapZoom * 100:F0}% | Zone : {visibleTilesX}x{visibleTilesY} tuiles";
			string wheelHint = Localization.Get("map.wheel_hint");
			int infoY = mapY + mapAreaH + 14;
			int infoH = 46;
			Raylib.DrawRectangleRounded(new Rectangle(mapX - 4, infoY - 4, mapAreaW + 8, infoH), 0.15f, 6, new Color(0, 0, 0, 130));
			FontManager.DrawText(zoomText, mapX + 4, infoY, 14, new Color(210, 210, 220, 230));
			FontManager.DrawText(wheelHint, mapX + 4, infoY + 22, 12, new Color(160, 160, 170, 200));

			string helpText = Localization.Get("map.help_text");
			int tw = FontManager.MeasureText(helpText, 16);
			FontManager.DrawText(helpText, (sw - tw) / 2, sh - 30, 16, Color.Gray);
		}

		static void HandleInput(Camera2D camera, float dt)
	{
		// ========================================================
		if (TamedAnimalUI.IsOpen && TamedAnimalUI.IsEditingName)
			return;

		Vector2 inputPosition = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
		Rectangle? inputViewport = null;
		int inputPlayerIndex = 0;
		Camera2D inputCamera = camera;
			
			if (ChatSystem.IsOpen) 
			{
				// Les touches pour chat sont gérées dans ChatSystem.Update()
				return;
			}

			if (QuestJournalUI.IsOpen)
			{
				if (Raylib.IsKeyPressed(KeyboardKey.G) || Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					QuestJournalUI.Close();
					return;
				}
				QuestJournalUI.Update(entities);
				return;
			}
			
			if (!IsAnyTextInputFocused())
			{
				// ========================================================
				// 2. RACCOURCIS CLAVIER UNIVERSELS (fonctionnent TOUJOURS)
				// ========================================================
			
			// TELECHARGEMENT DE STRUCTURE (F7/F8) - fonctionne même si souris sur UI
			if (!InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen)
			{
				if (Raylib.IsKeyPressed(KeyboardKey.F7))
				{
					Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), inputCamera);
					int tileX = (int)(mouseWorld.X / TileSize);
					int tileY = (int)(mouseWorld.Y / TileSize);
					_selectionCorner1 = new Vector2Int(tileX, tileY);
					AddNotification(new Notification($"Premier coin sélectionné : ({tileX}, {tileY})", Color.Yellow, 2f));
					_selectionCorner2 = null;
				}
				else if (Raylib.IsKeyPressed(KeyboardKey.F8))
				{
					if (_selectionCorner1 == null)
					{
						AddNotification(new Notification(Localization.Get("map.no_first_corner"), Color.Red, 2f));
						return;
					}
					Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
					int tileX = (int)(mouseWorld.X / TileSize);
					int tileY = (int)(mouseWorld.Y / TileSize);
					_selectionCorner2 = new Vector2Int(tileX, tileY);
					AddNotification(new Notification($" Deuxième coin sélectionné : ({tileX}, {tileY})", Color.Yellow, 2f));
					SaveStructure(_selectionCorner1.Value, _selectionCorner2.Value);
					_selectionCorner1 = null;
					_selectionCorner2 = null;
				}
			}
		}
		
		// Avance rapide du temps (F6) - tant que la touche est tenue
		if (Raylib.IsKeyDown(KeyboardKey.F6))
		{
			_timeSkipRemaining += dt * 60f;  // Le temps avance 60x plus vite tant que F6 est pressé
		}
		
		// Rechargement des données (F4)
		if (Raylib.IsKeyPressed(KeyboardKey.F4) && _gameState == GameState.Playing && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen && !QuestDialogUI.IsOpen)
			{
				ReloadData();
			}
			
			// Carte (M)
			if (Raylib.IsKeyPressed(KeyboardKey.M) && (_gameState == GameState.Playing || _gameState == GameState.Map))
			{
				if (_gameState == GameState.Map)
				{
					_gameState = GameState.Playing;
				}
				else
				{
					_gameState = GameState.Map;
					_mapOffset = Vector2.Zero;
					_isDraggingMap = false;
				}
			}

			// Invitation Pouilleux distante : elle doit prendre la priorité sur les raccourcis
			// ordinaires tant qu'une proposition est en attente.
			if (HasPendingPouilleuxInvitation)
			{
				if (Raylib.IsKeyPressed(KeyboardKey.Y))
				{
					RespondToPouilleuxInvitation(true);
					return;
				}
				if (Raylib.IsKeyPressed(KeyboardKey.N) || Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					RespondToPouilleuxInvitation(false);
					return;
				}
			}

			// Bestiaire (Y)
			if (Raylib.IsKeyPressed(KeyboardKey.Y) && _gameState == GameState.Playing && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen)
			{
				if (BestiaryUI.IsOpen)
					UIManager.PopUI();
				else
					BestiaryUI.Open();
			}
			
			// Retour de la carte (ESC) - géré plus bas
			if (Raylib.IsKeyPressed(KeyboardKey.Escape) && _gameState == GameState.Map)
			{
				_gameState = GameState.Playing;
			}
			
			// Debug (F3)
			if (Raylib.IsKeyPressed(KeyboardKey.F3) && _gameState == GameState.Playing)
			{
				_showDebug = !_showDebug;
			}
			
			// Fullscreen (F11)
			if (Raylib.IsKeyPressed(KeyboardKey.F11))
			{
				_fullscreen = !_fullscreen;
				if (_fullscreen)
				{
					ScreenWidth = Raylib.GetMonitorWidth(0);
					ScreenHeight = Raylib.GetMonitorHeight(0);
					Raylib.SetWindowSize(ScreenWidth, ScreenHeight);
					Raylib.ToggleFullscreen();
				}
				else
				{
					Raylib.ToggleFullscreen();
					ScreenWidth = 1280;
					ScreenHeight = 720;
					Raylib.SetWindowSize(ScreenWidth, ScreenHeight);
				}
				InvalidateLightMask();
			}
			
			// Touche J : ouvrir/fermer l'interface de création de sort (si baguette en main)
			if (Raylib.IsKeyPressed(KeyboardKey.J) && !ChatSystem.IsOpen && PlayerCurrentCar == null)
			{
				Item? heldItem = InventoryRenderer.GetHotbarItem(hotbarSlot);
				if (heldItem != null && Program.GetItemId(heldItem.Name) == GameData.GetItemId("magic_wand"))
				{
					if (SpellCraftingUI.IsOpen)
						UIManager.PopUI(); // ou SpellCraftingUI.Close()
					else
						SpellCraftingUI.Open();
				}
			}
			
			// Chat
			if (KeyBindings.IsPressed(GameAction.ChatToggle) && _gameState == GameState.Playing && !IsAnyTextInputFocused())
			{
				ChatSystem.Toggle();
			}
			
			// Sauvegarde rapide (F5)
			if (Raylib.IsKeyPressed(KeyboardKey.F5) && _gameState == GameState.Playing && !string.IsNullOrEmpty(_currentSaveName))
			{
				SaveCurrentGame();
				ShowSavingIcon();
			}
			
			// Touche R sur un wagon (sous la souris) : lier / délier des wagons
			if (Raylib.IsKeyPressed(KeyboardKey.R) && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen
				&& !QuestDialogUI.IsOpen && PlayerCurrentCar == null)
				TryHandleWagonLinkClick(inputCamera, inputViewport);

			// Dans HandleInput, après la gestion des interactions de station, etc.
			if (KeyBindings.IsPressed(GameAction.Interact) && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen && !QuestDialogUI.IsOpen)
			{
				if (TryHandleWagonInteraction()) return;

				// Cas 1 : on est dans une voiture → descendre
				if (PlayerCurrentCar != null)
				{
					// Descendre
					PlayerCurrentCar.Driver = null;
					// Placer le joueur à côté
					_playerPos = PlayerCurrentCar.Position + new Vector2(50, 0);
					PlayerCurrentCar = null;
					return;
				}
				// Cas 2 : on cherche une voiture proche pour monter
				else
				{
					foreach (var car in Cars)
					{
						if (car.Driver != null) continue;
						float dist = Vector2.Distance(_playerPos, car.Position);
						if (dist < 80f)
						{
							car.Driver = GetPlayerEntity();
							PlayerCurrentCar = car;
							AddNotification(new Notification(" Vous montez dans la voiture.", new Color(100, 200, 255, 255), 2f));
							return;
						}
					}
				}
			}
			
			HandlePrimaryPlayerGamepadInput(camera);
			
			// ========================================================
			// 3. RACCOURCIS UI (I, C, L, etc.)
			// ========================================================
		
			// Si le jeu est en mode écriture, on ignore tous les raccourcis UI
			if (!IsAnyTextInputFocused())
			{
				// INVENTAIRE (I)
				if (KeyBindings.IsPressed(GameAction.Inventory) && _gameState == GameState.Playing)
			{
				if (InventoryRenderer.IsInventoryOpen)
					UIManager.PopUI();
				else
					InventoryRenderer.Open(inputPlayerIndex);
				return;
			}

			// ITEMS (K)
			if (IsGodMode && Raylib.IsKeyPressed(KeyboardKey.K) && _gameState == GameState.Playing)
			{
				if (ItemMenuUI.IsOpen)
					UIManager.PopUI();
				else
					ItemMenuUI.Open();
				return;
			}
			
			// ARTISANAT (C)
			if (KeyBindings.IsPressed(GameAction.Character) && _gameState == GameState.Playing)
			{
				if (CraftingUI.IsOpen)
					UIManager.PopUI();
				else if (!UIManager.IsAnyUIOpen())
					CraftingUI.Open("", inputPlayerIndex);  // ← Ouvre toujours l'artisanat de base
				return;
			}
			
// GUILDE (G)
				if (Raylib.IsKeyPressed(KeyboardKey.G) && _gameState == GameState.Playing)
				{
					if (GuildUI.IsOpen)
						UIManager.PopUI();
					else if (!UIManager.IsAnyUIOpen())
					{
						if (Program.PlayerGuild != null)
							GuildUI.Open();
						else
							AddNotification(new Notification("Vous devez créer une guilde avec la charte avant d'accéder au menu.", Color.Red, 2f));
					}
					return;
				}
			}

			// ========================================================
			// 4. GESTION DE L'ESCAPE / PAUSE (fermeture des UI + MENU PAUSE)
			// ========================================================
			if (Raylib.IsKeyPressed(KeyboardKey.Escape) || KeyBindings.IsPressed(GameAction.PauseMenu))
			{
				if (QuestJournalUI.IsOpen)
				{
					QuestJournalUI.Close();
					return;
				}
				if (DyeingUI.IsOpen)
				{
					DyeingUI.Close();
					return;
				}
				if (GuildUI.IsOpen)
				{
					GuildUI.Close();
					return;
				}
				if (TraderUI.IsOpen)
				{
					TraderUI.Close();
					return;
				}
				if (QuestDialogUI.IsOpen)
				{
					QuestDialogUI.Close();
					return;
				}
				if (PortalUI.IsOpen)
				{
					PortalUI.Close();
					return;
				}
				if (_gameState == GameState.Playing)
				{
					if (UIManager.IsAnyUIOpen())
					{
						UIManager.PopUI();
						return;
					}
					// Si aucune UI n'est ouverte, on met en pause
					_gameState = GameState.Paused;
					_pauseMenuClosing = false; // le menu part de _pauseMenuSlide (généralement 0) et glisse vers le bas à l'ouverture
					return;
				}
				
				// Si on est déjà dans le menu pause, on le ferme (avec l'animation de fermeture)
				if (_gameState == GameState.Paused)
				{
					RequestClosePauseMenu(GameState.Playing);
					return;
				}
				
				// Pour les autres états (Options) : on revient à l'endroit d'où les Options ont été ouvertes
				if (_gameState == GameState.Options)
				{
					_gameState = _optionsReturnState;
					return;
				}
				
				// Menu des succès : retour à l'endroit d'où il a été ouvert (pause en général)
				if (_gameState == GameState.Achievements)
				{
					_gameState = _optionsReturnState;
					return;
				}
			}
			
			// ========================================================
			// 5. BLOQUER LES ACTIONS PHYSIQUES SI UNE UI EST OUVERTE
			// ========================================================
			if (UIManager.IsAnyUIOpen())
				return;

			// ========================================================
			// 6. MENU RADIAL (MOUSSE MOYENNE)
			// ========================================================
			// Ouvrir le menu radial lorsque le bouton du milieu (ou action rebinding) est enfoncé
			if (SettingsManager.Settings.HotbarStyle == "radial" && KeyBindings.IsDown(GameAction.RadialMenu) && _gameState == GameState.Playing && !ChatSystem.IsOpen && !_radialMenu.IsOpen && PlayerCurrentCar == null)
			{
				var slots = GetHotbarSlotsCached(); //  OPTIM : cache par frame
				_radialMenu.Open(slots, hotbarSlot);
			}
			
			// Mettre à jour le menu radial si ouvert
			if (_radialMenu.IsOpen)
			{
				_radialMenu.Update(Raylib.GetMousePosition());
				
				// Si le bouton du milieu / action radial menu est relâché, valider la sélection
				if (KeyBindings.IsReleased(GameAction.RadialMenu))
				{
					int selected = _radialMenu.SelectedSlot;
					if (selected >= 0 && selected < 10)
					{
						hotbarSlot = selected;
					}
					_radialMenu.Close();
					return; // Sortir pour ne pas exécuter les autres actions
				}
				
				// Si on appuie sur ESC pendant le menu, le fermer sans sélection
				if (Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					_radialMenu.Close();
					return;
				}
				
				// Bloquer les autres interactions pendant que le menu est ouvert
				return;
			}

			// Pendant la sélection d'une table Pouilleux, un clic droit sur un joueur
			// distant envoie une invitation ciblée au lieu de déclencher un outil du monde.
			if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !_rightClickConsumed
				&& PlayerCurrentCar == null && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen
				&& !QuestDialogUI.IsOpen && CartesGameUI.IsOpen
				&& TryInviteRemotePlayerUnderMouse(camera))
			{
				_rightClickConsumed = true;
				return;
			}

			// Fertiliser une pousse avec un item compatible (clic droit) — priorité haute
			if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !_rightClickConsumed
				&& PlayerCurrentCar == null && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen
				&& !QuestDialogUI.IsOpen && !CartesGameUI.IsOpen)
			{
				Item? heldCardDeck = equipment.MainHand;
				if (heldCardDeck != null && GetItemId(heldCardDeck.Name) == GameData.GetItemId("card_deck"))
				{
					CartesGameUI.OpenStandaloneCards();
					_rightClickConsumed = true;
					return;
				}
			}

			// Fertiliser une pousse avec du Caca (clic droit) — priorité haute
			if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !_rightClickConsumed && PlayerCurrentCar == null && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen && !QuestDialogUI.IsOpen)
			{
				Item? heldItemForFertil = equipment.MainHand;
				if (heldItemForFertil != null
					&& GameData.ItemDatabase.TryGetValue(GetItemId(heldItemForFertil.Name), out var fertilizerData)
					&& fertilizerData.FertilizerBoost > 0f)
				{
					Vector2 localMouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
					var tileInfo = FindTileUnderMouse(localMouseWorld, TileSize);
					int fTileX = tileInfo.tileX;
					int fTileY = tileInfo.tileY;
					int fObjectId = tileInfo.objectId;
					if (fTileX != -1 && fTileY != -1)
					{
						float dist = Vector2.Distance(_playerPos, new Vector2(fTileX * TileSize + TileSize/2f, fTileY * TileSize + TileSize/2f));
						if (dist < MAX_INTERACTION_DISTANCE)
						{
							var tileData = WorldTileRegistry.GetTile(fObjectId);
							if (tileData != null && (tileData.IsSapling || tileData.IsCrop || tileData.HasGrowthStages))
							{
								var cropData = World.GetCropDataAt(fTileX, fTileY);
								if (cropData != null)
								{
									float add = 0f;
									if (tileData.GrowthStages > 0)
										add = tileData.GrowthTime / Math.Max(1, tileData.GrowthStages);
									else
										add = tileData.GrowthTime * 0.25f;
									add *= fertilizerData.FertilizerBoost;

									cropData.AccumulatedGrowthTime += add;
									if (cropData.AccumulatedGrowthTime >= tileData.GrowthTime && tileData.AdultTileId > 0)
									{
										World.AddPlacedObject(fTileX, fTileY, tileData.AdultTileId);
									}
									RemoveItemFromInventory(heldItemForFertil.Name, 1);
									AddNotification(new Notification(" Pousse fertilisée !", new Color(170, 220, 120, 255), 1.5f));
									Vector2 fertilPos = new Vector2(fTileX * TileSize + TileSize / 2f, fTileY * TileSize + TileSize / 2f);
									PlayFertilizerSound();
									SpawnFertilizerParticles(fertilPos);
									_rightClickConsumed = true;
								}
							}
						}
					}
				}
			}
			
			// ========================================================
			// 7. ACTIONS PHYSIQUES BLOQUÉES PAR L'UI (mais menu fermé)
			// ========================================================

			bool instrumentOpenRequested = Raylib.IsMouseButtonPressed(MouseButton.Left) && !InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen && PlayerCurrentCar == null && !_rightClickConsumed;
			if (instrumentOpenRequested)
			{
				Item? heldInstrument = equipment.MainHand;
				if (heldInstrument != null && GameData.ItemDatabase.TryGetValue(GetItemId(heldInstrument.Name), out var data) && data.IsInstrument)
				{
					if (GetItemId(heldInstrument.Name) == 9000)
					{
						UseSandlandFlute(heldInstrument);
						_rightClickConsumed = true;
						return;
					}
					else if (!InstrumentUI.IsOpen)
					{
						InstrumentUI.Open();
					}
					_rightClickConsumed = true;
					return;
				}
			}

			if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen && PlayerCurrentCar == null && !_rightClickConsumed)
			{
				Item? heldPotion = equipment.MainHand;
				if (heldPotion != null && TryDrinkTaggedPotion(heldPotion))
				{
					_rightClickConsumed = true;
					return;
				}

				// 1. Recette (code existant)
				Item? heldItemForRecipe = equipment.MainHand;
				if (heldItemForRecipe != null && RecipeSystem.IsRecipeItem(GetItemId(heldItemForRecipe.Name)))
				{
					if (TryUseRecipeItem(heldItemForRecipe, inputPosition, inputCamera))
					{
						_rightClickConsumed = true;
						return;
					}
				}
			}
			
			// Apprivoisement (clic droit)
			if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen && PlayerCurrentCar == null && !_rightClickConsumed)
			{
				Item? heldItemForTiki = equipment.MainHand;
				if (heldItemForTiki != null && TryOfferFoodToTikiTotem(
					heldItemForTiki,
					Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera)))
				{
					_rightClickConsumed = true;
					return;
				}

				// 1. D'abord vérifier si on utilise une recette
				Item? heldItemForRecipe = equipment.MainHand;
				if (heldItemForRecipe != null && RecipeSystem.IsRecipeItem(GetItemId(heldItemForRecipe.Name)))
				{
					if (TryUseRecipeItem(heldItemForRecipe, inputPosition, inputCamera))
					{
						_rightClickConsumed = true;
						return; // Important : sortir pour ne pas exécuter les autres actions
					}
				}
				
				// 2. Ensuite vérifier l'apprivoisement
				Item? heldItemForTame = equipment.MainHand;
				bool tamed = false;
				if (heldItemForTame != null)
				{
					int heldId = GetItemId(heldItemForTame.Name);

					if (NetworkManager.IsClient)
					{
						//  MULTIJOUEUR : un client ne possède pas d'entités simulées localement
						// (elles vivent côté host). On repère l'animal parmi les snapshots reçus,
						// puis on envoie une demande au host, seul autoritaire pour valider et
						// appliquer le tamage. L'item n'est retiré qu'à la confirmation du host
						// (voir ApplyNetworkTameResult), pour ne pas le perdre en cas de refus.
						var animalDto = FindAnimalUnderMouseClient(camera);
						if (animalDto != null && !animalDto.IsTamed && !animalDto.IsSpiritAnimal && Guid.TryParse(animalDto.NetId, out var animalNetId))
						{
							int prefFoodId = GetPreferredFoodId(animalDto.Species);
							if (heldId == prefFoodId)
							{
								NetworkManager.RequestTameAnimal(animalNetId, heldId);
								_rightClickConsumed = true;
								tamed = true;
							}
						}
					}
					else
					{
						var animal = FindAnimalUnderMouse(camera);
						if (animal != null && !animal.IsTamed && !animal.IsSpiritAnimal)
						{
							int prefFoodId = GetPreferredFoodId(animal.Species);
							if (heldId == prefFoodId)
							{
								RemoveItemFromInventory(heldItemForTame.Name, 1);
								animal.IsTamed = true;
								SkillSystem.AddXP(SkillType.Dresseur, 15);
								AchievementManager.Progress(AchievementType.TameAnimal, 1);
								animal.OwnerName = _currentSaveName;
								AddNotification(new Notification($" {animal.Species} apprivoisé(e) !", new Color(100, 255, 100, 255), 2.5f));
								animal.Behavior = "tamed";
								_rightClickConsumed = true;
								tamed = true;
							}
						}
					}
				}
				
				// 2.5 Sinon, traire une vache avec un seau vide en main
				bool milked = false;
				if (!tamed && !NetworkManager.IsClient)
				{
					Item? heldItemForMilk = equipment.MainHand;
					if (heldItemForMilk != null && GetItemId(heldItemForMilk.Name) == 63)
					{
						ParseBucketContent(heldItemForMilk.Metadata, out string milkBucketType, out int milkBucketCharges);
						bool bucketCanTakeMilk = milkBucketType == "empty" || (milkBucketType == "milk" && milkBucketCharges < BUCKET_MAX_CHARGES);
						if (bucketCanTakeMilk)
						{
							var milkAnimal = FindAnimalUnderMouse(camera);
							if (milkAnimal != null && string.Equals(milkAnimal.Species, "cow", StringComparison.OrdinalIgnoreCase))
							{
								heldItemForMilk.Metadata = FormatBucketContent("milk", BUCKET_MAX_CHARGES);
								AddNotification(new Notification(" Vous remplissez le seau de lait !", new Color(240, 240, 220, 255), 2f));
								_rightClickConsumed = true;
								milked = true;
							}
						}
					}
				}
				
				// 3. Sinon, casser un rocher si on tient un amas de pierre
				if (!tamed && !milked)
				{
					Item? heldItem = equipment.MainHand;
					if (heldItem != null && GetItemId(heldItem.Name) == 163)
					{
						BreakRock(heldItem, inputPosition);
						_rightClickConsumed = true;
					}
					// 4. Sinon, équiper l'item (jamais pour une arme : elle est déjà tenue en main)
					else if (heldItem != null && IsEquippableItem(heldItem) && GetItemId(heldItem.Name) is int heldItemId2 && GameData.ItemDatabase.TryGetValue(heldItemId2, out var heldItemData2) && heldItemData2.Type != ItemType.Weapon)
					{
						var hotbarSlotRef = InventoryRenderer.InventorySlots[hotbarSlot];
						EquipItemFromSlot(hotbarSlotRef, "");
						_rightClickConsumed = true;
					}
					// 5. Sinon, interagir avec le monde (stations, coffres, etc.)
					else
					{
						bool interacted = TryInteractWithStation(inputPosition, inputCamera, inputViewport, inputPlayerIndex);
						if (interacted) _rightClickConsumed = true;
					}
				}
			}
			
			// Arroser les plantes (clic droit) : système unifié, fonctionne avec n'importe quel
			// contenant de liquide (gourde, arrosoir, seau...) tant qu'il contient de l'eau.
			if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !_rightClickConsumed && PlayerCurrentCar == null && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen && !QuestDialogUI.IsOpen)
			{
				Item? heldItemForWater = equipment.MainHand;
				if (heldItemForWater != null && TryGetLiquidContainer(heldItemForWater, out string waterLiquidType, out int currentWater, out int maxWaterCharges))
				{
					Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
					(int tileX, int tileY, float _, int objectId) = FindTileUnderMouse(mouseWorld, TileSize);
					if (tileX != -1 && tileY != -1)
					{
						int groundId = World.GetGroundTileIdAt(tileX, tileY);
						if (groundId == 27) // farmland sec
						{
							// Vérifier la distance
							float dist = Vector2.Distance(_playerPos, new Vector2(tileX * TileSize + TileSize/2f, tileY * TileSize + TileSize/2f));
							if (dist < MAX_INTERACTION_DISTANCE)
							{
								if (waterLiquidType == "milk")
								{
									AddNotification(new Notification(" Vous ne pouvez pas arroser avec du lait !", new Color(200, 100, 100, 255), 1.5f));
								}
								else if (currentWater <= 0)
								{
									AddNotification(new Notification(" Le contenant est vide !", new Color(200, 100, 100, 255), 1.5f));
								}
								else
								{
									World.WaterFarmland(tileX, tileY);
									currentWater--;
									SetLiquidContainerCharges(heldItemForWater, "water", currentWater);
									SpawnWateringCanParticles(new Vector2(tileX * TileSize + TileSize / 2f, tileY * TileSize + TileSize / 2f));
									AddNotification(new Notification($" Terre arrosée ! ({currentWater}/{maxWaterCharges})", new Color(100, 200, 255, 255), 1.5f));
								}
								_rightClickConsumed = true;
							}
						}
					}

                    
				}
			}
			
			// Créer une guilde avec la charte (clic droit)
			if (Raylib.IsMouseButtonPressed(MouseButton.Right) && PlayerCurrentCar == null && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen && !QuestDialogUI.IsOpen)
			{
				Item? heldItem = equipment.MainHand;
				if (heldItem != null && GameData.TryGetItemByName(heldItem.Name, out var heldItemData) && string.Equals(heldItemData.Key, "guild_charter", StringComparison.OrdinalIgnoreCase))
				{
					if (PlayerGuild == null)
					{
						PlayerGuild = new Guild();
						RemoveItemFromInventory(heldItem.Name, 1);
						AddNotification(new Notification(" Guilde créée !", new Color(200, 180, 100, 255), 2.5f));
					}
					else
					{
						AddNotification(new Notification(" Vous avez déjà une guilde !", Color.Red, 1.5f));
					}
					_rightClickConsumed = true;
					return;
				}
			}

					// Fertiliser une pousse avec du Caca (clic droit)
					if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !_rightClickConsumed && PlayerCurrentCar == null && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen && !QuestDialogUI.IsOpen)
					{
						Item? heldItemForFertil = equipment.MainHand;
						if (heldItemForFertil != null
							&& GameData.ItemDatabase.TryGetValue(GetItemId(heldItemForFertil.Name), out var fertilizerData)
							&& fertilizerData.FertilizerBoost > 0f)
						{
							Vector2 localMouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
							var tileInfo = FindTileUnderMouse(localMouseWorld, TileSize);
							int fTileX = tileInfo.tileX;
							int fTileY = tileInfo.tileY;
							int fObjectId = tileInfo.objectId;
							if (fTileX != -1 && fTileY != -1)
							{
								var tileData = WorldTileRegistry.GetTile(fObjectId);
								if (tileData != null && tileData.IsSapling)
								{
									var cropData = World.GetCropDataAt(fTileX, fTileY);
									if (cropData != null)
									{
										float add = 0f;
										if (tileData.GrowthStages > 0)
											add = tileData.GrowthTime / Math.Max(1, tileData.GrowthStages); // avance d'un stade
										else
											add = tileData.GrowthTime * 0.25f; // fallback : 25% du temps total
										add *= fertilizerData.FertilizerBoost;

										cropData.AccumulatedGrowthTime += add;
										if (cropData.AccumulatedGrowthTime >= tileData.GrowthTime && tileData.AdultTileId > 0)
										{
											World.AddPlacedObject(fTileX, fTileY, tileData.AdultTileId);
										}
										RemoveItemFromInventory(heldItemForFertil.Name, 1);
										AddNotification(new Notification(" Pousse fertilisée !", new Color(170, 220, 120, 255), 1.5f));
										// Effets sonores et particules
										Vector2 fertilPos = new Vector2(fTileX * TileSize + TileSize / 2f, fTileY * TileSize + TileSize / 2f);
										PlayFertilizerSound();
										SpawnFertilizerParticles(fertilPos);
										_rightClickConsumed = true;
									}
								}
							}
						}
					}
			
			//  Invocation du boss "Génie de la Lampe" (clic droit avec la Lampe merveilleuse, item 272)
			if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !_rightClickConsumed && PlayerCurrentCar == null && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen && !QuestDialogUI.IsOpen)
			{
				Item? heldLamp = equipment.MainHand;
				if (heldLamp != null && GetItemId(heldLamp.Name) == 272)
				{
					if (CurrentBoss != null && CurrentBoss.IsAlive)
					{
						AddNotification(new Notification(" Un danger rôde déjà... Ce n'est pas le moment.", new Color(220, 100, 100, 255), 2f));
					}
					else
					{
						Vector2 spawnPos = _playerPos + new Vector2(0f, -140f);
						int spTileX = (int)(spawnPos.X / TileSize);
						int spTileY = (int)(spawnPos.Y / TileSize);
						int spGround = World.GetGroundTileIdAt(spTileX, spTileY);
						var spGroundTile = WorldTileRegistry.GetTile(spGround);
						if (spGroundTile == null || !spGroundTile.Walkable || World.GetObjectIdAt(spTileX, spTileY) != 0)
							spawnPos = _playerPos + new Vector2(160f, 0f); // secours si la case au-dessus est bloquée

						RemoveItemFromInventory(heldLamp.Name, 1);
						Entity.SpawnGenieSmoke(spawnPos, 1.6f);
						Program.ApplyScreenShake(5f, 0.3f);

						var genie = new Entity(spawnPos, "Genie", false);
						genie.CustomName = "Génie de la Lampe";
						GetEntities().Add(genie);
						var spawnChunk = World.GetChunkAt(spTileX, spTileY);
						if (spawnChunk != null) spawnChunk.Entities.Add(genie);

						AddNotification(new Notification(" \"QUI OSE ME RÉVEILLER ?!\" Le Génie de la Lampe apparaît !", new Color(255, 200, 80, 255), 3.5f));
					}
					_rightClickConsumed = true;
				}
			}

			if (MountedAnimal != null)
			{
				if (KeyBindings.IsPressed(GameAction.Interact))
				{
					DismountAnimal();
					return; // Bloquer les autres interactions
				}
				// Bloquer les autres touches de mouvement? On les gérera dans UpdateGameplay
			}

			// Emote wheel (B)
			if (Raylib.IsKeyPressed(KeyboardKey.B) && !ChatSystem.IsOpen && PlayerCurrentCar == null)
			{
				_isEmoteWheelOpen = true;
				_emoteWheelHoverIndex = -1;
			}

			if (_isEmoteWheelOpen)
			{
				UpdateEmoteWheel(Raylib.GetMousePosition());
				if (Raylib.IsKeyReleased(KeyboardKey.B))
				{
					if (_emoteWheelHoverIndex >= 0 && _emoteWheelHoverIndex < _emoteWheelOptions.Length)
					{
						ApplyEmote(_emoteWheelOptions[_emoteWheelHoverIndex]);
					}
					_isEmoteWheelOpen = false;
					_emoteWheelHoverIndex = -1;
					return;
				}
				if (Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					_isEmoteWheelOpen = false;
					_emoteWheelHoverIndex = -1;
					return;
				}
				return;
			}
			
			// Changement de pose du porte-armure (R)
			if (Raylib.IsKeyPressed(KeyboardKey.R) && !ChatSystem.IsOpen && PlayerCurrentCar == null)
			{
				Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
				int ts = TileSize;
				(int tileX, int tileY, float dist, int objectId) = FindTileUnderMouse(mouseWorldPos, ts);
				if (tileX != -1 && tileY != -1)
				{
					var tileData = WorldTileRegistry.GetTile(objectId);
					if (tileData != null && tileData.IsArmorStand == true)
					{
						int height = World.GetHeightAt(tileX, tileY);
						float yOffset = -height * ts / 4;
						float objX = tileX * ts + ts/2f;
						float objY = tileY * ts + yOffset + ts/2f;
						int playerTileX = (int)(_playerPos.X / ts);
						int playerTileY = (int)(_playerPos.Y / ts);
						int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
						float playerYOffset = -playerHeight * ts / 4;
						Vector2 playerVisualPos = new Vector2(_playerPos.X, _playerPos.Y + playerYOffset);
						if (Vector2.DistanceSquared(playerVisualPos, new Vector2(objX, objY)) < (ts * 2f) * (ts * 2f))
						{
							var chunk = World.GetChunkAt(tileX, tileY);
							var standData = chunk?.GetArmorStandAt(tileX, tileY);
							if (standData != null)
							{
								standData.NextPose();
								AddNotification(new Notification($" Porte-armure - Pose: {standData.CurrentPose}", new Color(200, 180, 100, 255), 1.5f));
							}
						}
					}
				}
			}
			
			// Ramasser un meuble ou une créature portable (F)
			if (KeyBindings.IsPressed(GameAction.Drop) && !ChatSystem.IsOpen && PlayerCurrentCar == null)
			{
				TryPickupFurnitureOrCreature(inputPosition, inputCamera, inputViewport, inputPlayerIndex);
			}
			
			// Touches de hotbar (1-9, 0) - uniquement si menu radial fermé
			if (!_radialMenu.IsOpen)
			{
				if (SettingsManager.Settings.HotbarStyle == "classic"
					&& Raylib.IsMouseButtonPressed(MouseButton.Left)
					&& !ChatSystem.IsOpen
					&& !IsMouseOverAnyUI()
					&& HudRenderer.TryGetClassicHotbarSlot(Raylib.GetMousePosition(), out int clickedHotbarSlot))
				{
					hotbarSlot = clickedHotbarSlot;
					UpdateMainHandFromHotbar();
					return;
				}

				for (int k = 1; k <= 9; k++)
					if (Raylib.IsKeyPressed(KeyboardKey.One + (k - 1))) hotbarSlot = k - 1;
				if (Raylib.IsKeyPressed(KeyboardKey.Zero)) hotbarSlot = 9;
				UpdateMainHandFromHotbar();
			}
			
			// Molette - uniquement si menu radial fermé
			// NOTE : le changement de slot hotbar via la molette est géré dans UpdateGameplay()
			// pour éviter un double traitement du même événement de molette (qui causait un saut de 3 slots).
			if (!_radialMenu.IsOpen)
			{
				float wheel = Raylib.GetMouseWheelMove();
				if (wheel != 0 && !IsMouseOverAnyUI())
				{
					if (KeyBindings.IsDown(GameAction.Sprint)) 
						camera.Zoom = Math.Clamp(camera.Zoom + wheel * 0.05f, 0.5f * _cameraViewportScale, 4f * _cameraViewportScale);
				}
			}
			
			// À la toute fin, réinitialiser le flag pour la prochaine frame
			_rightClickConsumed = false;
		}

		//  MULTIJOUEUR EN LIGNE : sous-menu Héberger / Rejoindre ────────────────
		static void UpdateMultiplayerMenu()
		{
			// Écran de connexion en cours (client) : on surveille juste l'état du réseau.
			if (_mpWaitingForWorld)
			{
				if (NetworkManager.IsConnecting)
				{
					// La tentative de connexion tourne encore sur son thread : on affiche juste
					// son statut, sans jamais la considérer comme échouée avant qu'elle soit finie
					// (sinon, sur une connexion un peu lente - hors LAN typiquement - on affichait
					// "Connexion interrompue" avant même que la tentative ait eu le temps d'aboutir).
					_mpStatusMessage = NetworkManager.ConnectionStatus;
				}
				else if (!NetworkManager.IsClient)
				{
					// La tentative est terminée et a échoué.
					_mpWaitingForWorld = false;
					_mpStatusMessage = string.IsNullOrEmpty(NetworkManager.LastError) ? "Connexion interrompue." : NetworkManager.LastError;
				}
				else if (NetworkManager.ConnectionStatus.StartsWith("Monde reçu") == false)
				{
					_mpStatusMessage = NetworkManager.ConnectionStatus;
				}
				if (Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					NetworkManager.Disconnect();
					_mpWaitingForWorld = false;
					// Revenir directement au menu principal
					_isMultiplayerMenu = false;
					_isJoinSetup = false;
					_isCharacterSelect = false;
					_isHostSetup = false;
				}
				return;
			}

			if (_isWorldLookupPending)
			{
				if (Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					_isWorldLookupPending = false;
					_joinWorldLookupTask = null;
					_isJoinSetup = true;
					return;
				}

				if (_joinWorldLookupTask?.IsCompleted == true)
				{
					WorldInfoMsg? worldInfo = null;
					try { worldInfo = _joinWorldLookupTask.GetAwaiter().GetResult(); }
					catch { }
					_isWorldLookupPending = false;
					_joinWorldLookupTask = null;
					if (worldInfo == null)
					{
						_mpStatusMessage = "Impossible de récupérer le monde de l'hôte.";
						_isJoinSetup = true;
					}
					else
					{
						_pendingJoinWorldName = worldInfo.WorldName;
						string hostKey = $"{_pendingJoinIp}:{_pendingJoinPort}";
						LocalCharacters.ImportLegacyForWorld(hostKey, _pendingJoinWorldName, worldInfo.ExistingCharacterGuids);
						_joinCharacterOptions = LocalCharacters.GetForWorld(hostKey, _pendingJoinWorldName);
						_isCharacterSelect = true;
					}
				}
				return;
			}

			if (_isHostSetup)
			{
				UpdateTextFieldInput(ref _mpPseudoInput, 0);
				UpdateTextFieldInput(ref _mpPortInput, 1, digitsOnly: true);
				if (Raylib.IsKeyPressed(KeyboardKey.Tab)) _mpFocusedField = (_mpFocusedField + 1) % 2;

				if (Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					_isHostSetup = false;
					_mpStatusMessage = "";
					// Revenir directement au menu principal
					_isMultiplayerMenu = false;
					if (_isHostingFromPause) { _isHostingFromPause = false; _isMultiplayerMenu = false; }
				}
				if (Raylib.IsMouseButtonPressed(MouseButton.Left) && IsMouseOverMpButton("confirm"))
				{
					LocalPlayerName = string.IsNullOrWhiteSpace(_mpPseudoInput) ? "Hôte" : _mpPseudoInput;
					int.TryParse(_mpPortInput, out int port);
					int actualPort = port > 0 ? port : 7777;
					
					if (_isHostingFromPause)
					{
						LoadNetworkPlayerRegistry(World.CurrentSaveName);
						if (!StartHostingGame(actualPort))
							return;
						// La partie est déjà chargée (on vient du menu pause) : on héberge directement
						// le monde en cours, sans passer par l'écran de sélection de sauvegarde.
						_isHostingFromPause = false;
						_isMultiplayerMenu = false;
						_isHostSetup = false;
						_gameState = GameState.Paused;
						AddNotification(new Notification(" Partie hébergée ! Les autres joueurs peuvent maintenant vous rejoindre.", new Color(120, 220, 140, 255), 3f));
					}
					else
					{
						_isSelectingSave = true; // réutilise l'écran existant de sélection/création de sauvegarde
						RefreshAvailableSaves();
						if (_selectedSaveIndex >= _availableSaves.Count) _selectedSaveIndex = 0;
						_isCreatingNewSave = false;
						_creationError = "";
						_pendingHostPort = actualPort;
						_pendingHostMode = true;
						_isMultiplayerMenu = false;
						_isHostSetup = false;
					}
				}
				return;
			}

			if (_isJoinSetup)
			{
				UpdateTextFieldInput(ref _mpPseudoInput, 0);
				UpdateTextFieldInput(ref _mpHostIpInput, 1);
				UpdateTextFieldInput(ref _mpPortInput, 2, digitsOnly: true);
				if (Raylib.IsKeyPressed(KeyboardKey.Tab)) _mpFocusedField = (_mpFocusedField + 1) % 3;

				if (Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					_isJoinSetup = false;
					_mpStatusMessage = "";
					// Revenir directement au menu principal
					_isMultiplayerMenu = false;
				}
				if (Raylib.IsMouseButtonPressed(MouseButton.Left) && IsMouseOverMpButton("confirm"))
				{
					LocalPlayerName = string.IsNullOrWhiteSpace(_mpPseudoInput) ? "Joueur" : _mpPseudoInput;
					int.TryParse(_mpPortInput, out int port);
					int actualPort = port > 0 ? port : 7777;
					string ip = _mpHostIpInput.Trim();
					//  On ne se connecte pas tout de suite : on passe par l'écran de choix du
					// personnage (voir _isCharacterSelect), pour permettre de reprendre un
					// personnage précédemment créé sur ce monde/hôte, ou d'en créer un nouveau,
					// au lieu de deviner via une simple case à cocher.
					_pendingJoinIp = ip;
					_pendingJoinPort = actualPort;
					string hostKey = $"{ip}:{actualPort}";
					_joinWorldLookupTask = NetworkManager.QueryHostWorldInfoAsync(ip, actualPort, LocalCharacters.GetLegacyGuids(hostKey));
					_isWorldLookupPending = true;
					_isJoinSetup = false;
					_isCharacterSelect = false;
					_mpStatusMessage = "";
				}
				return;
			}

			if (_isCharacterSelect)
			{
				if (Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					// Retour à l'écran précédent, IP/pseudo/port conservés.
					_isCharacterSelect = false;
					_isJoinSetup = true;
				}

				var mouseNow = Raylib.GetMousePosition();
				if (Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					for (int i = 0; i < _characterSelectRowRects.Count; i++)
					{
						if (!Raylib.CheckCollisionPointRec(mouseNow, _characterSelectRowRects[i])) continue;

						string hostKey = $"{_pendingJoinIp}:{_pendingJoinPort}";
						if (i < _joinCharacterOptions.Count)
						{
							// Personnage existant choisi (le premier de la liste, "Personnage
							// principal", correspond à l'identité par défaut de l'installation :
							// dans ce cas on n'active PAS de session override, on garde le
							// comportement normal basé sur player_id.txt).
						var chosen = _joinCharacterOptions[i];
							if (chosen.Guid != DefaultLocalPlayerGuidReadOnly)
								UseCharacterGuidForNextJoin(chosen.Guid);
							LocalCharacters.MarkUsed(hostKey, _pendingJoinWorldName, chosen.Guid);

							LocalPlayerName = string.IsNullOrWhiteSpace(_mpPseudoInput) ? "Joueur" : _mpPseudoInput;
							NetworkManager.JoinHostAsync(_pendingJoinIp, _pendingJoinPort, LocalPlayerName);
							SavedServers.AddOrUpdate(_pendingJoinIp, _pendingJoinPort);
							_mpWaitingForWorld = true;
							_mpStatusMessage = "Connexion en cours...";
							_isCharacterSelect = false;
						}
						else
						{
							// "+ Nouveau personnage" : au lieu de créer le personnage et de
							// rejoindre tout de suite, on rappelle l'écran de personnalisation
							// (le même que pour la création d'un monde) pour laisser le joueur
							// customiser son apparence AVANT de rejoindre. La création réelle
							// du personnage (LocalCharacters.CreateNew) et la connexion
							// (NetworkManager.JoinHostAsync) n'ont lieu qu'à la confirmation
							// sur cet écran — voir DrawCreateWorldMenu / _isJoinCharacterCreation.
							_tempSkinColor = SkinColor;
							_tempHairStyle = PlayerHairStyle;
							_tempBeardStyle = PlayerBeardStyle;
							_tempHairColor = HairColor;
							_tempEyeStyle = EyeStyle;
							_tempEyeColor = EyeColor;
							_newWorldName = "";
							_creationError = "";
							_creationTab = 0;
							_isCharacterSelect = false;
							_isJoinCharacterCreation = true;
						}
						break;
					}
				}
				return;
			}

			// Menu Héberger / Rejoindre / Retour
			if (Raylib.IsKeyPressed(KeyboardKey.Up)) _multiplayerSubOption = (_multiplayerSubOption - 1 + 3) % 3;
			if (Raylib.IsKeyPressed(KeyboardKey.Down)) _multiplayerSubOption = (_multiplayerSubOption + 1) % 3;
			if (Raylib.IsKeyPressed(KeyboardKey.Escape))
			{
				_isMultiplayerMenu = false;
				_isJoinSetup = false;
				_isCharacterSelect = false;
				_isHostSetup = false;
				return;
			}

			bool confirm = Raylib.IsKeyPressed(KeyboardKey.Enter) ||
				(Raylib.IsMouseButtonPressed(MouseButton.Left) && _hoveredMpSubOption != -1);
			if (Raylib.IsMouseButtonPressed(MouseButton.Left) && _hoveredMpSubOption != -1) _multiplayerSubOption = _hoveredMpSubOption;

			if (confirm)
			{
				if (_multiplayerSubOption == 0) { _isHostSetup = true; _mpFocusedField = 0; }
				else if (_multiplayerSubOption == 1) { _isJoinSetup = true; _mpFocusedField = 0; }
				else
				{
					_isMultiplayerMenu = false;
					_isJoinSetup = false;
					_isCharacterSelect = false;
					_isHostSetup = false;
				}
			}
		}

		static void StartNewGame()
		{
			Weather.ResetForWorld();
			ClearWagonLinks();
			if (!string.IsNullOrEmpty(_newWorldSeed) && int.TryParse(_newWorldSeed, out int parsedSeed) && parsedSeed != 0)
			{
				WorldSeed = parsedSeed;
			}
			else
			{
				WorldSeed = (int)DateTime.Now.Ticks & 0x7FFFFFFF;
				if (WorldSeed < 1) WorldSeed = 1;
			}
			PerlinNoise.SetSeed(WorldSeed);
			
			QuestManager.ClearPersistedQuests();
			World.CurrentSaveName = _currentSaveName;
			LoadPreviouslyOwnedItemIds(null);
			
			_totalPlayTime = 0;
			
			_gameTime = DAY_CYCLE_DURATION * 0.5f;
			
			playerHP = playerMaxHP = 20;
			playerStamina = playerMaxStamina = 100f; staminaLocked = false;
			playerHunger = playerMaxHunger = 100f;
			playerThirst = playerMaxThirst = 100f;
			equipment = new Equipment();
			equipment.LoadEquipmentTextures();
			destroyedObjects.Clear();
			objectHPs.Clear();
			entities.Clear();
			_particles.Clear();
			Cars.Clear();
			EntityRenderer.ClearEquipmentCache();
			
			InventoryRenderer.Initialize(40);
			InventoryRenderer.ConvertToNewInventory(_inventoryBuffer);
			
			Random rand = new Random(WorldSeed);
			bool validSpawn = false;
			int searchRadius = 3000;
			int step = 100;
			float startX = 0, startY = 0;
			Biome spawnBiome = Biome.Plains;

			Console.WriteLine(" Recherche d'une zone de spawn dans les plaines...");
			Console.WriteLine($"   Rayon de recherche: {searchRadius} tuiles, pas de {step}");

			// Fonction pour forcer la génération complète d'une zone
			void ForceChunkGenerationAt(float x, float y)
			{
				int centerChunkX = (int)Math.Floor(x / (World.CHUNK_SIZE * Program.TileSize));
				int centerChunkY = (int)Math.Floor(y / (World.CHUNK_SIZE * Program.TileSize));
				
				// Générer les chunks autour (rayon 2 pour être sûr)
				for (int dx = -2; dx <= 2; dx++)
				{
					for (int dy = -2; dy <= 2; dy++)
					{
						int chunkX = centerChunkX + dx;
						int chunkY = centerChunkY + dy;
						World.LoadOrGenerateChunk(chunkX, chunkY, (float)Raylib.GetTime(), new List<Entity>());
					}
				}
			}

			// Recherche exhaustive en spirale autour de l'origine
			for (int r = 0; r <= searchRadius; r += step)
			{
				if (validSpawn) break;
				
				// Parcourir le périmètre du carré actuel
				for (int dx = -r; dx <= r; dx += step)
				{
					for (int dy = -r; dy <= r; dy += step)
					{
						// Ne vérifier que les bords extérieurs pour éviter les doublons
						if (Math.Abs(dx) != r && Math.Abs(dy) != r && r > 0) continue;
						
						float testX = dx;
						float testY = dy;
						
						// Ne pas re-vérifier l'origine inutilement
						if (testX == 0 && testY == 0 && r == 0) continue;
						
						// FORCER la génération des chunks autour de la position testée
						ForceChunkGenerationAt(testX, testY);
						
						Biome testBiome = World.GetBiomeAt(testX, testY);
						
						if (r % 500 == 0 && (dx == r || dy == r))
							Console.WriteLine($"   Recherche à distance {r}... Biome actuel: {WorldGeneration.GetBiomeDisplayName(testBiome)}");
						
						if (testBiome == Biome.Plains)
						{
							startX = testX;
							startY = testY;
							spawnBiome = testBiome;
							validSpawn = true;
							Console.WriteLine($" Plaine trouvée à ({startX:F0}, {startY:F0})");
							break;
						}
					}
					if (validSpawn) break;
				}
			}

			if (!validSpawn)
			{
				// Dernier recours : chercher dans les chunks déjà chargés autour de l'origine
				Console.WriteLine("   Recherche approfondie autour de l'origine...");
				ForceChunkGenerationAt(0, 0);
				
				for (int x = -100; x <= 100; x++)
				{
					for (int y = -100; y <= 100; y++)
					{
						Biome testBiome = World.GetBiomeAt(x * Program.TileSize, y * Program.TileSize);
						if (testBiome == Biome.Plains)
						{
							startX = x * Program.TileSize;
							startY = y * Program.TileSize;
							validSpawn = true;
							Console.WriteLine($" Plaine trouvée à ({startX:F0}, {startY:F0})");
							break;
						}
					}
					if (validSpawn) break;
				}
			}

			if (!validSpawn)
			{
				startX = 0;
				startY = 0;
				Console.WriteLine(" AUCUNE PLAINE TROUVÉE - SPAWN À L'ORIGINE");
				ForceChunkGenerationAt(0, 0);
			}

			_playerPos = new Vector2(startX, startY);
			_playerFacing = 1f;
			
			_playerEntity = new Entity(_playerPos, "human", true);
			_playerEntity.IsPlayer = true;
			RebuildInventoryFromEquipment();
			
			_smoothedCameraTarget = GetPlayerCameraTarget(_playerPos);
			_cameraNeedsImmediateCenter = true;

			Biome finalBiome = World.GetBiomeAt(_playerPos.X, _playerPos.Y);
			Console.WriteLine($" Position de spawn finale: ({_playerPos.X:F0}, {_playerPos.Y:F0}) - {WorldGeneration.GetBiomeDisplayName(finalBiome)}");
			_worldSpawnPos = _playerPos;

			var saveData = new GameSaveData
			{
				WorldSeed = WorldSeed,
				WorldSpawnX = _worldSpawnPos.X,
				WorldSpawnY = _worldSpawnPos.Y,
				HasDied = HasDiedAtLeastOnce,
				DeathPosX = LastDeathPosition.X,
				DeathPosY = LastDeathPosition.Y,
				PlayerPosX = _playerPos.X,
				PlayerPosY = _playerPos.Y,
				PlayerFacing = _playerFacing,
				IsGodMode = _godmode,
				TravelDistance = _travelDistanceProgress,
				PlayerHP = playerHP,
				PlayerMaxHP = playerMaxHP,
				PlayerStamina = playerStamina,
				PlayerMaxStamina = playerMaxStamina,
				PlayerHunger = playerHunger,
				PlayerMaxHunger = playerMaxHunger,
				PlayerThirst = playerThirst,
				PlayerMaxThirst = playerMaxThirst,
				PlayerHairStyle = PlayerHairStyle,
				PlayerBeardStyle = PlayerBeardStyle,
				PlayerSkinColorR = SkinColor.R,
				PlayerSkinColorG = SkinColor.G,
				PlayerSkinColorB = SkinColor.B,
				PlayerSkinColorA = SkinColor.A,
				PlayerHairColorR = HairColor.R,
				PlayerHairColorG = HairColor.G,
				PlayerHairColorB = HairColor.B,
				PlayerHairColorA = HairColor.A,
				PlayerEyeStyle = EyeStyle,
				PlayerEyeColorR = EyeColor.R,
				PlayerEyeColorG = EyeColor.G,
				PlayerEyeColorB = EyeColor.B,
				PlayerEyeColorA = EyeColor.A,
				Skills = SkillSystem.ToSaveData(),
				PreviouslyOwnedItemIds = PreviouslyOwnedItemIds.ToList(),
				HotbarSlot = 0,
				CameraZoom = 1f,
				PlayTime = 0,
				GameTime = _gameTime,
				InventorySlots = new List<InventorySlotSave>(),
				Equipment = new EquipmentSave(),
				SurfacePlacedObjects = new List<PlacedObjectSave>(),
				CavePlacedObjects = new List<PlacedObjectSave>(),
				DestroyedObjects = new HashSet<string>(),
				ObjectHPs = new Dictionary<string, WorldObjectHPSave>()
						,
						WeaponsBreak = WorldWeaponsBreak,
						FoodSpoils = WorldFoodSpoil
					};
			SaveSystem.SaveGame(_currentSaveName, saveData);
		}

		static void BeginLoadGame(string saveName)
		{
			//  IMPORTANT : comme dans StartNewGame, il faut retirer les personnages-
			// vitrines du menu principal (carrousel de mondes) de la liste globale
			// `entities` AVANT de charger la sauvegarde. Sans ça, SaveSystem.LoadChunks
			// (via RestoreEntityFromSave) se contente d'AJOUTER les PNJ de la
			// sauvegarde à `entities` sans jamais la vider : les vitrines du menu
			// restaient donc mélangées aux vrais PNJ chargés, se baladaient dans la
			// partie, et finissaient même par être réenregistrées dans la sauvegarde
			// au prochain Save.
			World.CurrentSaveName = saveName;
			_currentSaveName = saveName;
			ClearWagonLinks();
			WorldSeed = 0;
			PerlinNoise.SetSeed(WorldSeed);
			World.Reset();
			destroyedObjects.Clear();
			objectHPs.Clear();
			entities.Clear();
			_particles.Clear();

			var saveData = SaveSystem.LoadGame(saveName);
			//  BUG CORRIGÉ : WorldSeed restait à 0 (ou à la seed aléatoire du monde-vitrine
			// du menu principal) après le chargement d'une partie, au lieu de reprendre la
			// seed réellement utilisée à la création du monde. Les chunks déjà sauvegardés sur
			// disque étaient rechargés tels quels donc ça ne se voyait pas immédiatement, mais
			// toute zone jamais explorée (donc jamais sauvegardée) se regénérait alors avec la
			// mauvaise seed = terrain incohérent/différent d'une session à l'autre en bordure
			// des zones déjà visitées.
			WorldSeed = saveData.WorldSeed;
			PerlinNoise.SetSeed(WorldSeed);
			// Console.WriteLine($" Chargement de la partie - Seed du monde : {WorldSeed}");
			// Restaurer les options de monde (compatibles avec les nouvelles sauvegardes)
			WorldWeaponsBreak = saveData.WeaponsBreak;
			WorldFoodSpoil = saveData.FoodSpoils;
			PerlinNoise.SetSeed(WorldSeed);
			_crabCompanion = null;
			_crabRespawnCooldown = 0f;
			
			bool wasUnderground = saveData.IsUnderground;
			// Restaurer le plan avant de charger les chunks autour du joueur : sans cela,
			// la file de chargement utilise les chunks de surface par defaut et la position
			// sauvegardee sous terre finit par etre invalidee ou ramenee au spawn.
			World.IsUnderground = wasUnderground;
			HasDiedAtLeastOnce = saveData.HasDied;
			LastDeathPosition = new Vector2(saveData.DeathPosX, saveData.DeathPosY);
			
			World.LoadCavePairs();
			World.LoadDungeons();
			World.LoadPortals();

			//  Le joueur était dans un donjon au moment de la sauvegarde : il faut rebasculer
			// sur le plan de chunks de CE donjon avant de placer le joueur, sinon sa position
			// (en coordonnées locales au donjon) tombe sur des chunks vides des grottes/surface
			// -> écran noir, puis téléportation au spawn dès qu'un système de sécurité (chute
			// dans le vide, ragdoll...) se déclenche.
			if (saveData.IsInDungeon && saveData.DungeonInstanceId >= 0)
			{
				bool restored = World.RestoreDungeonPlane(saveData.DungeonInstanceId);
				if (!restored)
					Console.WriteLine($" Impossible de restaurer le donjon #{saveData.DungeonInstanceId} au chargement, le joueur risque d'apparaître dans le vide.");
			}

			_particles.Clear();
			_playerPos = new Vector2(saveData.PlayerPosX, saveData.PlayerPosY);
			_playerFacing = saveData.PlayerFacing;
			_godmode = saveData.IsGodMode;
			_travelDistanceProgress = saveData.TravelDistance;
			_lastTravelDistancePos = _playerPos;
			_playerEntity = new Entity(_playerPos, "human", true);
			_playerEntity.IsPlayer = true;
			_smoothedCameraTarget = GetPlayerCameraTarget(_playerPos);
			_cameraNeedsImmediateCenter = true;
			SkillSystem.LoadFromSaveData(saveData.Skills);
			playerHP = saveData.PlayerHP;
			playerMaxHP = saveData.PlayerMaxHP;
			playerStamina = saveData.PlayerMaxStamina > 0 ? saveData.PlayerStamina : 100f;
			playerMaxStamina = saveData.PlayerMaxStamina > 0 ? saveData.PlayerMaxStamina : 100f;
			staminaLocked = false;
			playerHunger = saveData.PlayerMaxHunger > 0 ? saveData.PlayerHunger : 100f;
			playerMaxHunger = saveData.PlayerMaxHunger > 0 ? saveData.PlayerMaxHunger : 100f;
			playerThirst = saveData.PlayerMaxThirst > 0 ? saveData.PlayerThirst : 100f;
			playerMaxThirst = saveData.PlayerMaxThirst > 0 ? saveData.PlayerMaxThirst : 100f;
			_totalPlayTime = saveData.PlayTime;
			_gameTime = saveData.GameTime;
			Weather.ResetForWorld(saveData.WeatherType, saveData.WeatherTimeRemaining);
			PlayerHairStyle = saveData.PlayerHairStyle;
			PlayerBeardStyle = saveData.PlayerBeardStyle;
			SkinColor = new Color((byte)saveData.PlayerSkinColorR, (byte)saveData.PlayerSkinColorG, (byte)saveData.PlayerSkinColorB, (byte)saveData.PlayerSkinColorA);
			HairColor = new Color((byte)saveData.PlayerHairColorR, (byte)saveData.PlayerHairColorG, (byte)saveData.PlayerHairColorB, (byte)saveData.PlayerHairColorA);
			EyeStyle = saveData.PlayerEyeStyle;
			EyeColor = new Color((byte)saveData.PlayerEyeColorR, (byte)saveData.PlayerEyeColorG, (byte)saveData.PlayerEyeColorB, (byte)saveData.PlayerEyeColorA);
			SetPlayerMorphSpecies(string.IsNullOrEmpty(saveData.PlayerMorphSpecies) ? "human" : saveData.PlayerMorphSpecies);
			if (saveData.PlayerMorphTint != null)
				PlayerMorphTint = new Color(saveData.PlayerMorphTint.R, saveData.PlayerMorphTint.G, saveData.PlayerMorphTint.B, saveData.PlayerMorphTint.A);
			if (saveData.PlayerMorphFeatureVariants != null)
			{
				PlayerMorphFeatureVariants.Clear();
				foreach (var feature in saveData.PlayerMorphFeatureVariants)
					PlayerMorphFeatureVariants[feature.Key] = feature.Value;
			}
			if (saveData.PlayerMorphFeatureColors != null)
			{
				PlayerMorphFeatureColors.Clear();
				foreach (var feature in saveData.PlayerMorphFeatureColors)
					PlayerMorphFeatureColors[feature.Key] = new Color(feature.Value.R, feature.Value.G, feature.Value.B, feature.Value.A);
			}
			World.ScanAllRitualCircles();

			//  Restaurer la guilde (nom, logo, membres recrutés) si elle existait.
			if (saveData.GuildData != null)
			{
                int logoSize = saveData.GuildData.LogoSize > 0 ? saveData.GuildData.LogoSize : 32;
                var restoredGuild = new Guild { Name = saveData.GuildData.Name };
                if (restoredGuild.Logo.GetLength(0) != logoSize)
                    restoredGuild.Logo = new Color[logoSize, logoSize];

                int i = 0;
                for (int x = 0; x < logoSize; x++)
                    for (int y = 0; y < logoSize; y++)
                    {
                        if (i < saveData.GuildData.LogoPixels.Count)
                        {
                            var c = saveData.GuildData.LogoPixels[i];
                            restoredGuild.Logo[x, y] = new Color(c.R, c.G, c.B, c.A);
                        }
                        i++;
                    }
                foreach (var idStr in saveData.GuildData.MemberNetIds)
                {
                    if (Guid.TryParse(idStr, out var guid))
                        restoredGuild.MemberIds.Add(guid);
                }
                PlayerGuild = restoredGuild;
            }
            else
            {
                PlayerGuild = null;
            }

            // Chargement de l'équipement avec couleurs personnalisées
            equipment = new Equipment();
			
			//  Items équipés couvrant une ou plusieurs zones du corps (casque, plastron, jambières,
			// écharpe, lunettes, sac à dos...) - système unifié
			foreach (var zoneSave in saveData.Equipment.ZoneItems)
			{
				if (string.IsNullOrEmpty(zoneSave.ItemName)) continue;
				var itemDef = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == zoneSave.ItemName);
				if (itemDef.ID == 0) continue;

				var zoneItem = new Item(itemDef.Name, 1, itemDef.Color, itemDef.Icon);
				var loadedColors = SaveSystem.LoadCustomColors(zoneSave.CustomColors);
				if (loadedColors != null && loadedColors.Count > 0)
					zoneItem.CustomColors = loadedColors;
				zoneItem.RestoreMetadataFromSave(zoneSave.Metadata ?? "");
				if (zoneSave.Meta != null && zoneSave.Meta.Count > 0)
					foreach (var kv in zoneSave.Meta)
						zoneItem.Meta[kv.Key] = kv.Value;
				SaveSystem.RestoreCoatVariantMeta(zoneItem, itemDef);
				if (itemDef.RuneSlots > 0)
				{
					SaveSystem.RestoreRunes(zoneItem, itemDef.RuneSlots, zoneSave.Runes, zoneSave.Metadata);
				}

				equipment.EquipOnBody(zoneItem, out _);
			}

						// Main principale
			if (!string.IsNullOrEmpty(saveData.Equipment.MainHand))
			{
				var item = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == saveData.Equipment.MainHand);
				if (item.ID != 0)
				{
					equipment.MainHand = new Item(item.Name, 1, item.GetColor(), item.Icon);
					var loadedColors = SaveSystem.LoadCustomColors(saveData.Equipment.MainHandCustomColors);
					if (loadedColors != null && loadedColors.Count > 0)
						equipment.MainHand.CustomColors = loadedColors;
				}
			}
			if (!string.IsNullOrEmpty(saveData.Equipment.OffHand))
			{
				var item = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == saveData.Equipment.OffHand);
				if (item.ID != 0)
				{
					equipment.OffHand = new Item(item.Name, 1, item.GetColor(), item.Icon);
					var loadedColors = SaveSystem.LoadCustomColors(saveData.Equipment.OffHandCustomColors);
					if (loadedColors != null && loadedColors.Count > 0)
						equipment.OffHand.CustomColors = loadedColors;
				}
			}
			// Restaurer le sac à dos équipé
			if (saveData.Equipment.BackpackContainer != null)
			{
				int slotCount = saveData.Equipment.BackpackContainer.Slots.Count;
				int columns = saveData.Equipment.BackpackContainer.Columns > 0 ? saveData.Equipment.BackpackContainer.Columns : 5;
				var backpack = new BackpackData(slotCount, columns);
				
				for (int i = 0; i < saveData.Equipment.BackpackContainer.Slots.Count && i < backpack.Inventory.Slots.Count; i++)
				{
					SaveSystem.RestoreInventorySlotFromSave(saveData.Equipment.BackpackContainer.Slots[i], backpack.Inventory.Slots[i]);
				}
				
				equipment.BackpackContainer = backpack;
				
				//  Lier le conteneur à l'item équipé (sac à dos)
				if (equipment.Backpack != null)
					equipment.Backpack.Backpack = backpack;
			}

            // Restaurer les slots d'équipement sauvegardés
            if (saveData.Equipment.EquippedItems != null && saveData.Equipment.EquippedItems.Count > 0)
            {
                foreach (var equippedSave in saveData.Equipment.EquippedItems)
                {
                    if (string.IsNullOrEmpty(equippedSave.ItemName))
                        continue;

                    var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == equippedSave.ItemName);
                    if (itemData.ID == 0)
                        continue;

					var item = new Item(itemData.Name, equippedSave.Count, itemData.Color, itemData.Icon);
					var loadedColors = SaveSystem.LoadCustomColors(equippedSave.CustomColors);
					if (loadedColors != null && loadedColors.Count > 0)
						item.CustomColors = loadedColors;
                    item.RestoreMetadataFromSave(equippedSave.Metadata ?? "");
                    if (equippedSave.Meta != null && equippedSave.Meta.Count > 0)
                    {
                        foreach (var kv in equippedSave.Meta)
                            item.Meta[kv.Key] = kv.Value;
                    }
                    if (itemData.RuneSlots > 0)
                    {
                        SaveSystem.RestoreRunes(item, itemData.RuneSlots, equippedSave.Runes, equippedSave.Metadata);
                    }
                    SaveSystem.RestoreCoatVariantMeta(item, itemData);

                    equipment.EquipItem(item, equippedSave.Slot, equippedSave.Index);
                }
            }

            //  RESTAURATION DU CONTENU DU CONTENEUR PORTABLE ÉQUIPÉ (panier) 
            // Doit impérativement se faire APRÈS la boucle EquippedItems ci-dessus :
            // EquipItem(..., EquipmentSlot.OffHand, ...) réassigne equipment.OffHand avec un
            // item tout neuf et appelle EnsureOffHandContainer(), qui créerait sinon un
            // conteneur vide et écraserait celui qu'on restaure ici, faisant disparaître
            // le contenu du panier à chaque rechargement de partie.
            if (saveData.Equipment.OffHandContainer != null)
            {
                int slotCount = saveData.Equipment.OffHandContainer.Slots.Count;
                int columns = saveData.Equipment.OffHandContainer.Columns > 0 ? saveData.Equipment.OffHandContainer.Columns : 3;
                var container = new PortableContainerData(slotCount, columns);

                for (int i = 0; i < saveData.Equipment.OffHandContainer.Slots.Count && i < container.Inventory.Slots.Count; i++)
                {
                    var savedSlot = saveData.Equipment.OffHandContainer.Slots[i];
                    if (!savedSlot.IsEmpty && !string.IsNullOrEmpty(savedSlot.ItemName))
                    {
                        var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == savedSlot.ItemName);
                        if (itemData.ID != 0)
                        {
                            var contItem = new Item(itemData.Name, savedSlot.Count, itemData.Color, itemData.Icon);
                            contItem.RestoreMetadataFromSave(savedSlot.Metadata ?? "");
                            if (itemData.RuneSlots > 0)
                            {
								SaveSystem.RestoreRunes(contItem, itemData.RuneSlots, savedSlot.Runes, savedSlot.Metadata);
                            }

                            var loadedColors = SaveSystem.LoadCustomColors(savedSlot.CustomColors);
                            if (loadedColors != null && loadedColors.Count > 0)
                                contItem.CustomColors = loadedColors;

                            container.Inventory.Slots[i].Item = contItem;
                            container.Inventory.Slots[i].Count = savedSlot.Count;
                        }
                    }
                }

                equipment.OffHandContainer = container;

                //  Lier le conteneur à l'item équipé (panier), quelle que soit l'instance
                // d'Item finalement présente dans OffHand après la boucle EquippedItems.
                if (equipment.OffHand != null)
                {
                    equipment.OffHand.Container = container;
                }
            }

            equipment.LoadEquipmentTextures();
            EntityRenderer.ClearEquipmentCache();
            RebuildInventoryFromEquipment();
			MarkItemsInInventoryAsPreviouslyOwned();
            //  NE PAS appeler UpdateArmorCompanions ici : aucun chunk n'est encore chargé à ce
            // stade (StepLoadGameChunks() ne s'exécute qu'après, frame par frame, pendant l'écran
            // de chargement). Faire apparaître le crabe compagnon maintenant le placerait sur un
            // terrain pas encore généré : le nuage de fumée d'apparition se déclenchait bien, mais
            // le crabe lui-même était ensuite perdu/invalide faute de chunk réel sous ses pattes.
            // L'appel a été déplacé dans FinishLoadGame(), une fois tous les chunks chargés.

            //  On ne génère plus tous les chunks d'un coup ici : on empile juste les
            // coordonnées à charger, et StepLoadGameChunks() en traitera un petit lot par
            // frame pendant que l'écran de chargement est affiché (voir plus bas).
            int centerChunkX = (int)Math.Floor(_playerPos.X / (World.CHUNK_SIZE * Program.TileSize));
            int centerChunkY = (int)Math.Floor(_playerPos.Y / (World.CHUNK_SIZE * Program.TileSize));

            _pendingChunkLoads = new Queue<(int chunkX, int chunkY)>();
            for (int dx = -World.RENDER_DISTANCE; dx <= World.RENDER_DISTANCE; dx++)
            {
                for (int dy = -World.RENDER_DISTANCE; dy <= World.RENDER_DISTANCE; dy++)
                {
                    _pendingChunkLoads.Enqueue((centerChunkX + dx, centerChunkY + dy));
                }
            }
            _loadingChunksTotal = _pendingChunkLoads.Count;

            _loadingSaveData = saveData;
        }

		static void SaveCurrentGame()
		{
			string saveName = !string.IsNullOrEmpty(_currentSaveName) ? _currentSaveName : World.CurrentSaveName;
			if (string.IsNullOrEmpty(saveName)) return;
			//  MULTIJOUEUR : un client ne sauvegarde jamais — le monde appartient au host.
			if (NetworkManager.IsClient) return;
			_currentSaveName = saveName;
			World.CurrentSaveName = saveName;
			SaveAllChunksEntities();
			World.SaveAllActiveChunks();
			World.SaveCavePairs();
			World.SaveDungeons();
			World.SavePortals();
			var saveData = new GameSaveData
			{
				WorldSeed = WorldSeed,
				IsUnderground = World.IsUnderground,
				IsInDungeon = World.IsInDungeon,
				DungeonInstanceId = World.CurrentDungeonInstanceId,
				WorldSpawnX = _worldSpawnPos.X,
				WorldSpawnY = _worldSpawnPos.Y,
				HasDied = HasDiedAtLeastOnce,
				DeathPosX = LastDeathPosition.X,
				DeathPosY = LastDeathPosition.Y,
				PlayerPosX = _playerPos.X,
				PlayerPosY = _playerPos.Y,
				PlayerFacing = _playerFacing,
				IsGodMode = _godmode,
				TravelDistance = _travelDistanceProgress,
				WagonLinks = CaptureWagonLinks(),
				PlayerHP = playerHP,
				PlayerHairStyle = PlayerHairStyle,
				PlayerBeardStyle = PlayerBeardStyle,
				PlayerMorphSpecies = PlayerMorphSpecies,
				PlayerMorphTint = new ColorSave { R = PlayerMorphTint.R, G = PlayerMorphTint.G, B = PlayerMorphTint.B, A = PlayerMorphTint.A },
				PlayerMorphFeatureVariants = PlayerMorphFeatureVariants.ToDictionary(feature => feature.Key, feature => feature.Value),
				PlayerMorphFeatureColors = PlayerMorphFeatureColors.ToDictionary(
					feature => feature.Key,
					feature => new ColorSave { R = feature.Value.R, G = feature.Value.G, B = feature.Value.B, A = feature.Value.A }),
				PlayerSkinColorR = SkinColor.R,
				PlayerSkinColorG = SkinColor.G,
				PlayerSkinColorB = SkinColor.B,
				PlayerSkinColorA = SkinColor.A,
				PlayerHairColorR = HairColor.R,
				PlayerHairColorG = HairColor.G,
				PlayerHairColorB = HairColor.B,
				PlayerHairColorA = HairColor.A,
				PlayerEyeStyle = EyeStyle,
				PlayerEyeColorR = EyeColor.R,
				PlayerEyeColorG = EyeColor.G,
				PlayerEyeColorB = EyeColor.B,
				PlayerEyeColorA = EyeColor.A,
				PlayerMaxHP = playerMaxHP,
				PlayerStamina = playerStamina,
				PlayerMaxStamina = playerMaxStamina,
				PlayerHunger = playerHunger,
				PlayerMaxHunger = playerMaxHunger,
				PlayerThirst = playerThirst,
				PlayerMaxThirst = playerMaxThirst,
				Skills = SkillSystem.ToSaveData(),
				PreviouslyOwnedItemIds = PreviouslyOwnedItemIds.ToList(),
				HotbarSlot = 0,
				CameraZoom = 1f,
				Quests = QuestManager.GetPersistedQuestsSnapshot()
					.Select(q => new QuestSaveData
					{
						Id = q.Id,
						Type = q.Type == QuestType.TameAndBring ? 1 : 0,
						GiverId = q.GiverId,
						TargetId = q.TargetId,
						ItemId = q.ItemId,
						ItemName = q.ItemName,
						ItemQty = q.ItemQty,
						TargetSpecies = q.TargetSpecies,
						OfferedAnimalId = q.OfferedAnimalId,
						RewardItemId = q.RewardItemId,
						RewardItemName = q.RewardItemName,
						RewardItemQty = q.RewardItemQty,
						LastKnownTargetPosX = q.LastKnownTargetPosX,
						LastKnownTargetPosY = q.LastKnownTargetPosY,
						HasLastKnownTargetPosition = q.HasLastKnownTargetPosition,
						LastKnownGiverPosX = q.LastKnownGiverPosX,
						LastKnownGiverPosY = q.LastKnownGiverPosY,
						HasLastKnownGiverPosition = q.HasLastKnownGiverPosition,
						State = (int)q.State
					}).ToList(),
				PlayTime = _totalPlayTime,
				InventorySlots = new List<InventorySlotSave>(),
				Equipment = new EquipmentSave(),
				SurfacePlacedObjects = new List<PlacedObjectSave>(),
				CavePlacedObjects = new List<PlacedObjectSave>(),
				DestroyedObjects = destroyedObjects,
				ObjectHPs = new Dictionary<string, WorldObjectHPSave>()
			};
			
			// Sauvegarde de l'inventaire
			foreach (var slot in InventoryRenderer.InventorySlots)
			{
				SaveSystem.SyncItemRuneMetadata(slot.Item);
				var slotSave = new InventorySlotSave
				{
					ItemName = slot.Item?.Name ?? "",
					Metadata = slot.Item?.Metadata ?? "",
					Meta = (slot.Item?.Meta != null && slot.Item.Meta.Count > 0) ? new Dictionary<string, string>(slot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null,
					Count = slot.Count,
					IsEmpty = slot.IsEmpty,
					CustomColors = SaveSystem.SaveCustomColors(slot.Item?.CustomColors)
				};

				//  Conteneur portable transporté dans l'inventaire (ex: panier)
				if (slot.Item?.Container != null)
				{
					var container = slot.Item.Container;
					slotSave.PortableContainer = new PortableContainerSaveData
					{
						Columns = container.Inventory.Columns,
						Rows = container.Inventory.Rows,
						Slots = new List<InventorySlotSave>()
					};
					foreach (var contSlot in container.Inventory.Slots)
					{
						SaveSystem.SyncItemRuneMetadata(contSlot.Item);
						slotSave.PortableContainer.Slots.Add(new InventorySlotSave
						{
							ItemName = contSlot.Item?.Name ?? "",
							Metadata = contSlot.Item?.Metadata ?? "",
							Meta = (contSlot.Item?.Meta != null && contSlot.Item.Meta.Count > 0) ? new Dictionary<string, string>(contSlot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null,
							Count = contSlot.Count,
							IsEmpty = contSlot.IsEmpty,
							CustomColors = SaveSystem.SaveCustomColors(contSlot.Item?.CustomColors)
						});
					}
				}

				//  Sac à dos transporté dans l'inventaire
				if (slot.Item?.Backpack != null)
				{
					var backpack = slot.Item.Backpack;
					slotSave.Backpack = new BackpackSaveData
					{
						Columns = backpack.Inventory.Columns,
						Rows = backpack.Inventory.Rows,
						Slots = new List<InventorySlotSave>()
					};
					foreach (var backSlot in backpack.Inventory.Slots)
					{
						SaveSystem.SyncItemRuneMetadata(backSlot.Item);
						slotSave.Backpack.Slots.Add(new InventorySlotSave
						{
							ItemName = backSlot.Item?.Name ?? "",
							Metadata = backSlot.Item?.Metadata ?? "",
							Meta = (backSlot.Item?.Meta != null && backSlot.Item.Meta.Count > 0) ? new Dictionary<string, string>(backSlot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null,
							Count = backSlot.Count,
							IsEmpty = backSlot.IsEmpty,
							CustomColors = SaveSystem.SaveCustomColors(backSlot.Item?.CustomColors)
						});
					}
				}

				saveData.InventorySlots.Add(slotSave);
			}
			
			// Sauvegarde de l'équipement avec couleurs personnalisées
			//  Items couvrant une zone du corps (casque, plastron, jambières, écharpe, lunettes, sac à dos...)
			saveData.Equipment.ZoneItems.Clear();
			foreach (var zoneItem in equipment.ZoneItems)
			{
				SaveSystem.SyncItemRuneMetadata(zoneItem);
				var zoneSave = new ZoneItemSave
				{
					ItemName = zoneItem.Name,
					Metadata = zoneItem.Metadata ?? "",
					Meta = (zoneItem.Meta != null && zoneItem.Meta.Count > 0) ? new Dictionary<string, string>(zoneItem.Meta, StringComparer.OrdinalIgnoreCase) : null
				};
				if (zoneItem.CustomColors != null && zoneItem.CustomColors.Count > 0)
					zoneSave.CustomColors = SaveSystem.SaveCustomColors(zoneItem.CustomColors);
				saveData.Equipment.ZoneItems.Add(zoneSave);
			}
			
			saveData.Equipment.MainHand = equipment.MainHand?.Name ?? "";
			saveData.Equipment.MainHandMetadata = equipment.MainHand?.Metadata ?? "";
			saveData.Equipment.MainHandMeta = equipment.MainHand?.Meta != null && equipment.MainHand.Meta.Count > 0
				? new Dictionary<string, string>(equipment.MainHand.Meta, StringComparer.OrdinalIgnoreCase) : null;
			saveData.Equipment.MainHandCustomColors = SaveSystem.SaveCustomColors(equipment.MainHand?.CustomColors);
			
			saveData.Equipment.OffHand = equipment.OffHand?.Name ?? "";
			saveData.Equipment.OffHandMetadata = equipment.OffHand?.Metadata ?? "";
			saveData.Equipment.OffHandMeta = equipment.OffHand?.Meta != null && equipment.OffHand.Meta.Count > 0
				? new Dictionary<string, string>(equipment.OffHand.Meta, StringComparer.OrdinalIgnoreCase) : null;
			saveData.Equipment.OffHandCustomColors = SaveSystem.SaveCustomColors(equipment.OffHand?.CustomColors);
			
			saveData.Equipment.Backpack = equipment.Backpack?.Name ?? "";
			saveData.Equipment.BackpackMetadata = equipment.Backpack?.Metadata ?? "";
			saveData.Equipment.BackpackMeta = equipment.Backpack?.Meta != null && equipment.Backpack.Meta.Count > 0
				? new Dictionary<string, string>(equipment.Backpack.Meta, StringComparer.OrdinalIgnoreCase) : null;
			saveData.Equipment.BackpackCustomColors = SaveSystem.SaveCustomColors(equipment.Backpack?.CustomColors);

			//  Items équipés dans les slots d'armure/accessoires (dictionnaire)
			saveData.Equipment.EquippedItems = equipment.EquippedItems.Select(kv =>
			{
				SaveSystem.SyncItemRuneMetadata(kv.Value);
				return new EquippedItemSave
				{
					Slot = kv.Key.slot,
					Index = kv.Key.index,
					ItemName = kv.Value.Name,
					Count = kv.Value.Count,
					CustomColors = SaveSystem.SaveCustomColors(kv.Value.CustomColors),
					Metadata = kv.Value.Metadata ?? "",
					Meta = (kv.Value.Meta != null && kv.Value.Meta.Count > 0) ? new Dictionary<string, string>(kv.Value.Meta, StringComparer.OrdinalIgnoreCase) : null
				};
			}).ToList();
			//  Conteneur portable équipé en main (ex: panier en offhand) — c'est ce qui manquait
			if (equipment.OffHandContainer != null)
			{
				saveData.Equipment.OffHandContainer = new PortableContainerSaveData
				{
					Columns = equipment.OffHandContainer.Inventory.Columns,
					Rows = equipment.OffHandContainer.Inventory.Rows,
					Slots = new List<InventorySlotSave>()
				};
				foreach (var slot in equipment.OffHandContainer.Inventory.Slots)
				{
					SaveSystem.SyncItemRuneMetadata(slot.Item);
					saveData.Equipment.OffHandContainer.Slots.Add(new InventorySlotSave
					{
						ItemName = slot.Item?.Name ?? "",
						Metadata = slot.Item?.Metadata ?? "",
								Meta = (slot.Item?.Meta != null && slot.Item.Meta.Count > 0) ? new Dictionary<string, string>(slot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null,
					});
				}
			}

			//  Sac à dos équipé
			if (equipment.BackpackContainer != null)
			{
				saveData.Equipment.BackpackContainer = new BackpackSaveData
				{
					Columns = equipment.BackpackContainer.Inventory.Columns,
					Rows = equipment.BackpackContainer.Inventory.Rows,
					Slots = new List<InventorySlotSave>()
				};
				foreach (var slot in equipment.BackpackContainer.Inventory.Slots)
				{
					SaveSystem.SyncItemRuneMetadata(slot.Item);
					saveData.Equipment.BackpackContainer.Slots.Add(new InventorySlotSave
					{
						ItemName = slot.Item?.Name ?? "",
						Metadata = slot.Item?.Metadata ?? "",
						Meta = (slot.Item?.Meta != null && slot.Item.Meta.Count > 0) ? new Dictionary<string, string>(slot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null,
						Count = slot.Count,
						IsEmpty = slot.IsEmpty,
						CustomColors = SaveSystem.SaveCustomColors(slot.Item?.CustomColors)
					});
				}
			}

			foreach (var hp in objectHPs)
			{
				saveData.ObjectHPs[hp.Key] = new WorldObjectHPSave
				{
					Current = hp.Value.Current,
					Max = hp.Value.Max
				};
			}
			
			SaveSystem.SaveGame(saveName, saveData);
		}

		static void UpdateGameplay(float dt, ref Vector2 playerPos, ref float facing, Vector2 hitboxDim,
           ref int craftSelected, ref Camera2D camera)
		{
			//  DEBUG F3 : englobe TOUTE cette fonction (crops, boats, campfires, quêtes, UI...),
			// y compris EntityUpdate et ChunkStreaming mesurés séparément plus bas. Sert de base
			// pour calculer par soustraction (PerfStats.GetOtherUpdateMs) tout ce qui n'est pas
			// encore instrumenté finement dans la mise à jour du jeu. Voir PerfStats.cs.
			PerfStats.StartSection(PerfStats.Section.GameplayUpdateTotal);
			Item? heldItem = null;

			//  MULTIJOUEUR : si le joueur a choisi "Héberger une partie" dans le menu,
			// on démarre le serveur réseau au moment où la partie démarre réellement
			// (une fois la sauvegarde chargée/créée), quel que soit le chemin utilisé
			// pour y arriver (charger, créer un nouveau monde, etc.).
			if (_pendingHostMode)
			{
				_pendingHostMode = false;
				LoadNetworkPlayerRegistry(World.CurrentSaveName);
				StartHostingGame(_pendingHostPort);
			}

			ScreenWidth = Raylib.GetScreenWidth();
			ScreenHeight = Raylib.GetScreenHeight();
			camera.Offset = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f);

			bool leftClickOnOpenUIThisFrame = Raylib.IsMouseButtonPressed(MouseButton.Left)
				&& IsMouseOnAnyOpenUIAtPosition(Raylib.GetMousePosition());

			if (CartesGameUI.IsOpen)
			{
				CartesGameUI.Update(dt);
			}

			if (_gameState == GameState.Playing && !IsPlayerRagdolled && !AchievementManager.IsUnlocked(AchievementType.TravelDistance))
			{
				if (_lastTravelDistancePos == Vector2.Zero && playerPos != Vector2.Zero)
				{
					_lastTravelDistancePos = playerPos;
				}
				else if (_lastTravelDistancePos != Vector2.Zero)
				{
					float moved = Vector2.Distance(_lastTravelDistancePos, playerPos);
					_lastTravelDistancePos = playerPos;
					_travelDistanceProgress += moved;
					if (_travelDistanceProgress >= 20f)
					{
						int progressAmount = (int)MathF.Floor(_travelDistanceProgress / 20f);
						if (progressAmount > 0)
						{
							_travelDistanceProgress -= progressAmount * 20f;
							AchievementManager.Progress(AchievementType.TravelDistance, progressAmount);
						}
					}
				}
			}

			//  Clignement des yeux aléatoire (toutes les 6 à 15 secondes)
			UpdateBlink(dt);

			//  PNJ en dialogue/commerce avec le joueur : le PNJ regarde le joueur et se tourne vers lui
			UpdateNpcPlayerDialogGaze(playerPos);

			//  Regard du joueur vers le curseur (pupilles + sourcils)
			{
				Vector2 mouseWorldForEyes = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
				Vector2 playerVisualPosForEyes = GetPlayerVisualPosition(playerPos);
				UpdatePupilLookAt(playerVisualPosForEyes, mouseWorldForEyes, facing);
			}

			//  MORT : la séquence de disparition se joue sans bloquer le monde ; le joueur ne
			// réapparaît plus automatiquement après un délai, il clique sur le bouton
			// "Réapparaître" affiché une fois l'écran de mort (vignette -> crâne -> message)
			// entièrement joué (voir UpdateDeathScreen / DrawDeathScreen).
			if (IsPlayerRagdolled)
		{
			if (_ragdollMinDelay > 0f) _ragdollMinDelay -= dt;
			UpdatePlayerDeathParticles(dt);

			Vector2 deathTarget = _playerDeathPosition;
			_smoothedCameraTarget = Vector2.Lerp(_smoothedCameraTarget, deathTarget, Math.Min(1f, dt * 3f));
			camera.Target = _smoothedCameraTarget;

			//  Un clic pendant l'animation (crâne/texte) la fait sauter directement à l'état
			// final (tout affiché, bouton prêt) plutôt que de déclencher une action - il faut
			// un second clic, sur le bouton lui-même, pour réapparaître.
			bool clicked = Raylib.IsMouseButtonPressed(MouseButton.Left);
			bool skipRequested = clicked && _deathScreenPhase != DeathScreenPhase.ButtonReady;
			UpdateDeathScreen(dt, skipRequested);

			//  Clic sur le bouton "Réapparaître" (rect calculé au dessin de la frame
			// précédente - même principe que le survol des autres boutons du jeu).
			if (clicked && _deathScreenPhase == DeathScreenPhase.ButtonReady && !skipRequested)
			{
				Vector2 mouse = Raylib.GetMousePosition();
				if (Raylib.CheckCollisionPointRec(mouse, _respawnButtonRect))
				{
					RespawnPlayer();
				}
			}

			if (IsPlayerRagdolled)
			{
				goto SkipMovementAndInteraction;
			}
		}

		if (_caveTransitionPhase != CaveTransitionPhase.None)
		{
			UpdateCaveTransition(dt);
			goto SkipMovementAndInteraction;
		}

			//  SYSTÈME DE TEMPÉRATURE : mis à jour chaque frame selon la position et l'équipement du joueur
			TemperatureSystem.Update(dt, playerPos, equipment);
			_playerEntity.WorldPos = playerPos;
			_playerEntity.Facing = facing;
			_playerEntity.UpdateBloodDrips(dt, playerHP, GetEffectivePlayerMaxHP(), PlayerMorphSpecies,
				visualPosition: GetPlayerVisualPosition(playerPos), inWater: _isPlayerInWater);
			AmbientAudio.Update(dt, playerPos);

			EnsureLocalPlayersInitialized();
			TryConnectLocalPlayers();
			UpdateAdditionalLocalPlayers(dt, hitboxDim, camera);
			
			bool isRenamingAnimal = TamedAnimalUI.IsOpen && TamedAnimalUI.IsEditingName;
			if (isRenamingAnimal)
			{
				TamedAnimalUI.Update();
				// Sortie anticipée : fermer quand même GameplayUpdateTotal, sinon son Stopwatch
				// reste en marche et fausse la mesure de la frame suivante (StartSection fait un
				// Restart, donc pas de crash, mais mieux vaut rester rigoureux).
				PerfStats.EndSection(PerfStats.Section.GameplayUpdateTotal);
				return;
			}
			
			if (TamedAnimalUI.IsOpen) TamedAnimalUI.Update();
			
			// Mise à jour de l'effet de secousse d'écran
			if (_shakeDuration > 0f)
			{
				_shakeDuration -= dt;
				Random rand = new Random();
				_shakeOffset = new Vector2(
					(float)(rand.NextDouble() - 0.5) * _shakeIntensity,
					(float)(rand.NextDouble() - 0.5) * _shakeIntensity
				);
				if (_shakeDuration <= 0f) { _shakeIntensity = 0f; _shakeOffset = Vector2.Zero; }
			}
			else _shakeOffset = Vector2.Zero;
			
			// Mise à jour des différentes UI
			if (!InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen && PlayerCurrentCar == null)
			{
				Entity? nearbyTamedAnimal = FindNearbyTamedAnimal();
				bool hasNearbyTamedAnimal = nearbyTamedAnimal != null && nearbyTamedAnimal.IsTamed;
				
				// ========== RÉGLAGE DE LA VOILE ==========
				if (_isAdjustingSail && _adjustedBoat != null)
				{
					// Si la touche E est relâchée, on termine
					if (!KeyBindings.IsDown(GameAction.Interact))
					{
						_adjustedBoat.SailAngle = _currentSailAngle;
						_adjustedBoat.IsSailAdjusted = Math.Abs(_currentSailAngle) > 0.01f;
						_isAdjustingSail = false;
						_adjustedBoat = null;
					}
					else
					{
						// Ajustement de l'angle par déplacement HORIZONTAL de la souris
						Vector2 currentMouse = Raylib.GetMousePosition();
						float deltaX = currentMouse.X - _sailAdjustStartMouseScreen.X;
						float angleStep = deltaX * 1.5f;
						_currentSailAngle = _initialSailAngle + angleStep;
						_currentSailAngle = ((_currentSailAngle % 360) + 360) % 360;
						if (_currentSailAngle > 180) _currentSailAngle -= 360;
						
						//  NOUVEAU : Appliquer l'angle en temps réel au bateau
						_adjustedBoat.SailAngle = _currentSailAngle;
						_adjustedBoat.IsSailAdjusted = Math.Abs(_currentSailAngle) > 0.01f;
					}
				}
				
				// Gestion de la touche E pour les animaux apprivoisés
				if (hasNearbyTamedAnimal && !TamedAnimalInteraction.IsOpen && !SlimeStorageUI.IsOpen)
				{
					bool ePressed = KeyBindings.IsPressed(GameAction.Interact);
					bool eDown = KeyBindings.IsDown(GameAction.Interact);
					bool eReleased = KeyBindings.IsReleased(GameAction.Interact);
					
					if (ePressed)
					{
						_eHoldTime = 0f;
						_eWasPressed = true;
						_eInteractionHandled = false;
					}
					
					if (_eWasPressed && eDown && !_eInteractionHandled)
					{
						_eHoldTime += dt;
						
						// Si on dépasse le seuil du long press, ouvrir le menu radial
						if (_eHoldTime >= E_LONG_PRESS_THRESHOLD && !_eInteractionHandled)
						{
							_eInteractionHandled = true;
							TamedAnimalInteraction.OpenRadial(nearbyTamedAnimal, camera);
						}
					}
					
					if (eReleased && _eWasPressed)
					{
						//  L'appui court n'ouvre plus l'UI d'infos : on y accède désormais
						// via le rond central du menu radial (appui long sur E).
						_eWasPressed = false;
						_eInteractionHandled = false;
					}
				}
				else if (TamedAnimalInteraction.IsOpen)
				{
					TamedAnimalInteraction.Update(null, false, false, false, Raylib.GetMousePosition(), camera);
				}
			}
			
			ContainerUI.Update();
			ArmorStandUI.Update();
			RitualCircleUI.Update();
			TraderUI.Update();

			//  SYSTÈME DE QUÊTES : gestion des clics sur la fenêtre de dialogue de quête
			if (QuestDialogUI.IsOpen)
			{
				string? questAction = QuestDialogUI.Update(Raylib.GetMousePosition());
				if (questAction == "accept")
				{
					var giver = QuestDialogUI.GetCurrentNpc();
					if (giver != null)
					{
						var quest = giver.ActiveQuest;
						if (NetworkManager.IsClient)
						{
							//  L'hôte est seul autoritaire pour faire passer la quête à "Accepted" ;
							// l'objet à livrer est déposé au sol par l'hôte à la position du donneur
							// (GiveItemToPlayer est déjà "network-aware"), le client le ramasse ensuite
							// normalement en s'approchant.
							NetworkManager.RequestQuestAction(giver.NetId, "Accept");
						}
						else
						{
							QuestManager.AcceptQuest(giver, (itemId, qty) => GiveItemToPlayer(itemId, qty, giver.WorldPos));
						}
						if (quest != null)
						{
							string questText = $" Quête acceptée : apporte {quest.ItemQty}x {quest.ItemName}";
							AddNotification(new Notification(questText, new Color(220, 200, 100, 255), 3.2f, giver, true));
						}
					}
				}
				else if (questAction == "accept_offer")
				{
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc != null && npc.ActiveQuest != null)
					{
						var target = QuestManager.FindEntityByNetId(GetQuestNpcs(), npc.ActiveQuest.TargetId);
						QuestDialogUI.OpenOffer(npc, target);
					}
				}
				else if (questAction == "validate_quest")
				{
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc != null) TryValidateQuestWithNpc(npc);
				}
				else if (questAction == "deliver_quest")
				{
					// Le joueur a choisi explicitement de livrer l'objet, distinctement de la
					// propre quête que ce PNJ pourrait par ailleurs proposer.
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc != null) TryDeliverQuestWithNpc(npc);
				}
				else if (questAction == "decline")
				{
					var giver = QuestDialogUI.GetCurrentNpc();
					if (giver != null)
					{
						if (NetworkManager.IsClient)
							NetworkManager.RequestQuestAction(giver.NetId, "Decline");
						else
							QuestManager.DeclineQuest(giver);
					}
				}
				else if (questAction == "gift_accept")
				{
					//  Le joueur confirme le don de l'animal apprivoisé au PNJ cible d'une
					// quête TameAndBring. _pendingGiftAnimal a été posé par TryDeliverQuestWithNpc
					// juste avant l'ouverture de la bulle de confirmation.
					var targetNpc = QuestDialogUI.GetCurrentNpc();
					if (targetNpc != null && _pendingGiftAnimal != null)
					{
						if (NetworkManager.IsClient)
						{
							//  MULTIJOUEUR : l'hôte est seul autoritaire sur Program.entities, donc seul
							// capable d'appliquer QuestManager.TryGiftTamedAnimal (AddPet/OwnerNpcId...).
							// On transmet le NetId de l'animal choisi ; l'hôte retrouve la vraie Entity
							// et applique le don, puis renvoie un accusé (QuestActionResult) pour un
							// retour visuel immédiat côté client.
							NetworkManager.RequestQuestAction(targetNpc.NetId, "GiftAnimal", _pendingGiftAnimal.NetId);
						}
						else
						{
							bool gifted = QuestManager.TryGiftTamedAnimal(targetNpc, _pendingGiftAnimal, GetQuestNpcs(), out var deliveredQuest);
							if (gifted && deliveredQuest != null)
							{
								AddNotification(new Notification(
									$" {_pendingGiftAnimal.Species} offert à {targetNpc.DisplayName ?? "ce PNJ"} ! Retourne voir {QuestManager.FindEntityByNetId(GetQuestNpcs(), deliveredQuest.GiverId)?.DisplayName ?? "le donneur"} pour ta récompense.",
									new Color(220, 200, 100, 255), 3.2f));
							}
						}
					}
					_pendingGiftAnimal = null;
				}
				else if (questAction == "gift_decline")
				{
					_pendingGiftAnimal = null;
				}
				else if (questAction == "discuss_quest")
				{
					// Le joueur a choisi de discuter de la quête dans la bulle de choix :
					// on enchaîne sur la logique de quête habituelle (offre / livraison / validation).
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc != null) HandleQuestNpcInteraction(npc);
				}
				else if (questAction == "social_chat")
				{
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc != null)
					{
						QuestDialogUI.OpenSocialChat(npc);
					}
				}
				else if (questAction == "social_continue")
				{
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc != null)
					{
						QuestDialogUI.OpenSocialChat(npc);
					}
				}
				else if (questAction == "social_back")
				{
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc != null)
					{
						TryOpenVillagerDialog(npc);
					}
				}
				else if (!string.IsNullOrEmpty(questAction) && questAction.StartsWith("social_"))
				{
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc == null)
					{
						QuestDialogUI.Close();
					}
					else
					{
						npc.RememberRandomSocialContact();
						string topic = questAction.Substring("social_".Length);
						string response = topic switch
						{
							"village" => npc.GetSocialReply("village"),
							"self" => npc.GetSocialReply("self"),
							"others" => npc.GetSocialReply("others"),
							"wish" => npc.GetSocialReply("wish"),
							_ => npc.GetSocialReply("leave")
						};
						npc.Friendship = Math.Clamp(npc.Friendship + npc.GetSocialFriendshipGain(topic), 0f, 1f);
						QuestDialogUI.OpenSocialFollowup(npc, response);
					}
				}
				else if (questAction == "trade")
				{
					// Le joueur a choisi de commercer dans la bulle de choix.
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc != null) TraderUI.Open(npc);
				}
				else if (questAction == "join_guild")
				{
					// Le joueur a choisi de recruter ce PNJ dans sa guilde depuis la bulle de choix.
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc != null)
					{
						if (NetworkManager.IsClient)
						{
							//  L'hôte est seul autoritaire (Program.PlayerGuild n'existe que chez lui).
							NetworkManager.RequestQuestAction(npc.NetId, "JoinGuild");
							AddNotification(new Notification($" {npc.DisplayName ?? "Le PNJ"} a rejoint votre guilde !", new Color(180, 160, 255, 255), 2.5f, npc, true));
						}
						else if (GuildRecruitManager.TryAccept(npc, _currentSaveName))
						{
							AddNotification(new Notification($" {npc.DisplayName ?? "Le PNJ"} a rejoint votre guilde !", new Color(180, 160, 255, 255), 2.5f, npc, true));
						}
					}
				}
				else if (questAction == "play_cards")
				{
					var npc = QuestDialogUI.GetCurrentNpc();
					if (npc != null) StartPouilleux(npc);
				}
			}
			CraftingUI.Update();
			GuildUI.Update();
			InstrumentUI.Update();
			PortalUI.Update();
			ItemMenuUI.Update();
			BestiaryUI.Update();
			MusicPlayer.Update();
			AchievementManager.Update(dt);
			UpdateAnimalBreeding(dt, (float)Raylib.GetTime());
			if (SpellCraftingUI.IsOpen)
				SpellCraftingUI.Update();
			
			World.UpdateRegrowth((float)Raylib.GetTime());
			World.UpdateCampfires(dt);
			World.UpdateFarmlandMoisture(dt);
			World.UpdateSaplingGrowth(dt);
			World.UpdateAquariums(dt, (float)Raylib.GetTime());
			
			// Succès de l'armure entière
			if (equipment.Head != null && equipment.Body != null && equipment.Legs != null)
			{
				if (!AchievementManager.IsUnlocked(AchievementType.FullArmor))
				{
					AchievementManager.UnlockAchievement(AchievementType.FullArmor);
					PlayAchievementSound();
				}
			}
			
			// La plongée sous-marine est désactivée.
			World.IsUnderwater = false;
			_playerDepth = 0f;
			_depthVelocity = 0f;
			_underwaterElevation = 0f;
			_underwaterElevationVelocity = 0f;
			_isOnSeaFloor = false;
			
			bool mouseOverUI = CartesGameUI.IsOpen || IsMouseOverAnyUI()
				|| (SettingsManager.Settings.HotbarStyle == "classic"
					&& Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), HudRenderer.GetClassicHotbarBounds()));
			
			// Récupération de l'item tenu en main (hotbar)
			heldItem = InventoryRenderer.GetHotbarItem(hotbarSlot);
			if (heldItem == null)
			{
				heldItem = equipment.MainHand;
			}
			
			// DÉFINIR mouseWorld ICI (AVANT DE L'UTILISER)
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			
			// Gestion de la torche (particules)
			if (heldItem != null)
			{
				int heldId = GetItemId(heldItem.Name);
				if (heldId == 7)
				{
					if (SpeciesData.Skeletons.TryGetValue("human", out var skeleton))
					{
						var rightArmBottom = skeleton.FirstOrDefault(p => p.Name == "rarmbottom");
						if (rightArmBottom != null)
						{
							int playerTileX = (int)(playerPos.X / TileSize);
							int playerTileY = (int)(playerPos.Y / TileSize);
							int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
							float playerYOffset = -playerHeight * TileSize / 4;
							Vector2 playerVisualPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset);
							Vector2 handPos = GetHandWorldPosition(playerVisualPos, facing, skeleton, _animState, _animFrame, _animProg);
							if (Random.Shared.NextDouble() < dt * 10f)
							{
								float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * 12f;
								float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * 8f;
								float worldX = handPos.X + offsetX;
								float worldY = handPos.Y + offsetY;
								if (Random.Shared.NextDouble() < 0.3)
									World.SpawnEmberParticle(worldX, worldY, _particles);
								else
									World.SpawnSmokeParticle(worldX, worldY, _particles);
							}
						}
					}
				}
			}
			
			if (!_isSitting && PlayerCurrentCar == null)
			{
				_explorationTimer += dt;
				if (_explorationTimer >= EXPLORATION_INTERVAL)
				{
					_explorationTimer = 0f;
					MarkExploredAreaAroundPlayer(playerPos);
				}
			}
			
			// Touche R pour changer la pose du porte-armure (plus bloqué par l'inventaire)
			if (Raylib.IsKeyPressed(KeyboardKey.R) && !ChatSystem.IsOpen && PlayerCurrentCar == null)
			{
				Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
				int ts = TileSize;
				(int tileX, int tileY, float dist, int objectId) = FindTileUnderMouse(mouseWorldPos, ts);
				if (tileX != -1 && tileY != -1)
				{
					var tileData = WorldTileRegistry.GetTile(objectId);
					if (tileData != null && tileData.IsArmorStand == true)
					{
						int height = World.GetHeightAt(tileX, tileY);
						float yOffset = -height * ts / 4;
						float objX = tileX * ts + ts/2f;
						float objY = tileY * ts + yOffset + ts/2f;
						int playerTileX = (int)(_playerPos.X / ts);
						int playerTileY = (int)(_playerPos.Y / ts);
						int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
						float playerYOffset = -playerHeight * ts / 4;
						Vector2 playerVisualPos = new Vector2(_playerPos.X, _playerPos.Y + playerYOffset);
						if (Vector2.DistanceSquared(playerVisualPos, new Vector2(objX, objY)) < (ts * 2f) * (ts * 2f))
						{
							var chunk = World.GetChunkAt(tileX, tileY);
							var standData = chunk?.GetArmorStandAt(tileX, tileY);
							if (standData != null)
							{
								standData.NextPose();
								AddNotification(new Notification($" Porte-armure - Pose: {standData.CurrentPose}", new Color(200, 180, 100, 255), 1.5f));
							}
						}
					}
				}
			}
			
			// Touche G : rame uniquement sur bateau
			if (!ChatSystem.IsOpen && PlayerCurrentCar == null && Raylib.IsKeyPressed(KeyboardKey.G))
			{
				Boat? currentBoat = World.GetBoatAtPosition(_playerPos, TileSize);
				if (currentBoat != null && currentBoat.RowingCooldown <= 0f)
				{
					Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
					
					Vector2 rowDir = mouseWorldPos - currentBoat.Position;
					if (rowDir.Length() > 0.01f) rowDir = Vector2.Normalize(rowDir);
					else rowDir = new Vector2(1, 0);
					
					float rowForce = 180f;
					currentBoat.Velocity += rowDir * rowForce;
					
					currentBoat.RowingCooldown = 0.5f;
					currentBoat.Angle = MathF.Atan2(rowDir.Y, rowDir.X) * 180f / MathF.PI;
					attackAnim = 0.2f;
					isAttacking = true;
					AddNotification(new Notification(" Coup de rame !", new Color(100, 200, 255, 255), 0.8f));
				}
			}
			
			// Touche F pour ramasser un meuble ou une créature portable (plus bloqué par l'inventaire)
			if (KeyBindings.IsPressed(GameAction.Drop) && !ChatSystem.IsOpen && PlayerCurrentCar == null)
				TryPickupFurnitureOrCreature(playerPos, camera);
			
			// Mise à jour de l'item tenu en main (équipement)
			equipment.MainHand = heldItem;
			_heldItem.Item = heldItem;
			_heldItem.Update(dt);
			
			// Lancer d'un objet : clic droit pour viser, clic gauche pour lancer.
			if (PlayerCurrentCar == null && !ChatSystem.IsOpen)
			{
				Item? heldThrowable = InventoryRenderer.GetHotbarItem(hotbarSlot);
				
				bool holdingThrowable = heldThrowable != null && 
					GameData.ItemDatabase.TryGetValue(GetItemId(heldThrowable.Name), out var throwData) &&
					throwData.IsThrowable;
				
				if (holdingThrowable && _throwCooldown <= 0f && !mouseOverUI)
				{
					if (Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						Vector2 playerAimOrigin = GetPlayerAimOrigin(playerPos);
						Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
						Vector2 dir = mouseWorldPos - playerAimOrigin;
						if (dir.LengthSquared() > 0.01f)
						{
							dir = Vector2.Normalize(dir);
							_throwables.Add(new ThrowableProjectile(playerAimOrigin, mouseWorldPos, 1f, GetItemId(heldThrowable.Name)));
							AchievementManager.Progress(AchievementType.ThrowExplosive, 1);
							RemoveItemFromInventory(heldThrowable.Name, 1);
							_throwCooldown = 0.5f;
						}
						_isChargingThrow = false;
						_throwCharge = 0f;
						_predictedThrowLanding = null;
					}
				}
				if (_throwCooldown > 0f) _throwCooldown -= dt;
			}
			
			// Gestion de la baguette magique - Nouveau système de sorts assignables
			if (PlayerCurrentCar == null && !ChatSystem.IsOpen)
			{
				Item? heldItemWeapon = InventoryRenderer.GetHotbarItem(hotbarSlot);
				bool hasMagicWand = heldItemWeapon != null && GetItemId(heldItemWeapon.Name) == GameData.GetItemId("magic_wand");

				if (hasMagicWand)
				{
					// ========== CLIC GAUCHE (charge) ==========
					if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !_isChargingRightClick)
					{
						_isChargingLeftClick = true;
						_leftClickCharge = 0f;
						_leftClickStartTime = (float)Raylib.GetTime();
					}
					else if (_isChargingLeftClick)
					{
						if (Raylib.IsMouseButtonDown(MouseButton.Left))
						{
							float holdTime = (float)Raylib.GetTime() - _leftClickStartTime;
							_leftClickCharge = Math.Min(1f, holdTime / MAX_CHARGE_TIME);
						}
						else if (Raylib.IsMouseButtonReleased(MouseButton.Left))
						{
							if (_leftClickCharge >= MIN_CHARGE_THRESHOLD)
							{
								// Lancer le sort assigné au clic gauche
								Vector2 playerVisualPos = GetPlayerVisualPosition(_playerPos);
								Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
								Vector2 direction = mouseWorldPos - playerVisualPos;
								if (direction.Length() < 0.01f) direction = new Vector2(1, 0);
								direction = Vector2.Normalize(direction);
								
								SpellCaster.CastSpell(_leftClickSpell, playerVisualPos, direction, mouseWorldPos, _leftClickCharge);
							}
							_isChargingLeftClick = false;
							_leftClickCharge = 0f;
						}
					}

					// ========== CLIC DROIT (charge) ==========
					if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !_isChargingLeftClick)
					{
						_isChargingRightClick = true;
						_rightClickCharge = 0f;
						_rightClickStartTime = (float)Raylib.GetTime();
					}
					else if (_isChargingRightClick)
					{
						if (Raylib.IsMouseButtonDown(MouseButton.Right))
						{
							float holdTime = (float)Raylib.GetTime() - _rightClickStartTime;
							_rightClickCharge = Math.Min(1f, holdTime / MAX_CHARGE_TIME);
						}
						else if (Raylib.IsMouseButtonReleased(MouseButton.Right))
						{
							if (_rightClickCharge >= MIN_CHARGE_THRESHOLD)
							{
								Vector2 playerVisualPos = GetPlayerVisualPosition(_playerPos);
								Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
								Vector2 direction = mouseWorldPos - playerVisualPos;
								if (direction.Length() < 0.01f) direction = new Vector2(1, 0);
								direction = Vector2.Normalize(direction);
								
								SpellCaster.CastSpell(_rightClickSpell, playerVisualPos, direction, mouseWorldPos, _rightClickCharge);
							}
							_isChargingRightClick = false;
							_rightClickCharge = 0f;
						}
					}
				}
				else
				{
					// Si on n'a plus la baguette, annuler les charges en cours
					if (_isChargingLeftClick) { _isChargingLeftClick = false; _leftClickCharge = 0f; }
					if (_isChargingRightClick) { _isChargingRightClick = false; _rightClickCharge = 0f; }
				}
			}

			// Gestion de la pêche (comme les explosifs)
			if (PlayerCurrentCar == null && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen)
			{
				bool holdingFishingRod = heldItem != null && GameData.GetToolType(heldItem.Name) == ToolType.Fishing;
				if (holdingFishingRod)
				{
					int playerTileX = (int)(playerPos.X / TileSize);
					int playerTileY = (int)(playerPos.Y / TileSize);
					int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
					float playerYOffset = -playerHeight * TileSize / 4;
					Vector2 playerVisualPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset);

					if (_currentFishingProjectile == null && _fishingCooldown <= 0f)
					{
						if (Raylib.IsMouseButtonDown(MouseButton.Left))
						{
							if (!_isChargingFishing) { _isChargingFishing = true; _fishingCharge = 0f; }
							_fishingCharge += dt;
							if (_fishingCharge > MAX_FISHING_CHARGE) _fishingCharge = MAX_FISHING_CHARGE;

							// Prédiction : afficher le cercle à la position de la souris
							Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
							_predictedFishingLanding = mouseWorldPos;
						}
						else if (_isChargingFishing)
						{
							if (_fishingCharge >= MIN_FISHING_CHARGE)
							{
								// Lancer directement à la position de la souris
								Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
								float power = MathHelper.Lerp(0.3f, 1f, _fishingCharge / MAX_FISHING_CHARGE);
								int stars = PickPendingFishAndGetStars();

								_currentFishingProjectile = new FishingProjectile(playerVisualPos, mouseWorldPos, power, stars);
								attackAnim = 0.2f;
								_fishingCooldown = 0.5f;
							}
							_isChargingFishing = false;
							_fishingCharge = 0f;
							_predictedFishingLanding = null;
						}
					}

					// Gestion du clic gauche pour ferrer (un clic par étoile à valider)
					if (_currentFishingProjectile != null && _currentFishingProjectile.HookStarted && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						bool fullyCaught = _currentFishingProjectile.TryCatch();
						if (fullyCaught)
						{
							GiveRandomFish();
							Program.AddNotification(new Notification(" Poisson attrapé !", new Color(100, 200, 255, 255), 2f));
							_currentFishingProjectile = null;
							_fishingCooldown = 1f;
						}
						else if (_currentFishingProjectile.Fled)
						{
							_pendingFishId = 0;
							Program.AddNotification(new Notification(" Le poisson s'est échappé...", new Color(200, 200, 200, 255), 2f));
							_currentFishingProjectile = null;
							_fishingCooldown = 0.75f;
						}
					}
				}

				if (_fishingCooldown > 0f) _fishingCooldown -= dt;

				if (_currentFishingProjectile != null)
				{
					_currentFishingProjectile.Update(dt);
					if (!_currentFishingProjectile.IsAlive && !_currentFishingProjectile.Caught)
					{
						if (_currentFishingProjectile.Fled)
						{
							_pendingFishId = 0;
							Program.AddNotification(new Notification(" Le poisson s'est échappé...", new Color(200, 200, 200, 255), 2f));
						}
						_currentFishingProjectile = null;
					}
				}
			}
			
			// Tir à l'arc / au mousquet - Clic droit pour viser, clic gauche pour tirer
			if (!ChatSystem.IsOpen && PlayerCurrentCar == null && !QuestDialogUI.IsOpen)
			{
				Item? heldWeapon = InventoryRenderer.GetHotbarItem(hotbarSlot);
				int heldWeaponId = heldWeapon != null ? GetItemId(heldWeapon.Name) : 0;
				bool hasRangedWeapon = false;
				List<int> rangedAmmoIds = new List<int>();
				ItemData weaponData = default;
				if (heldWeapon != null && GameData.ItemDatabase.TryGetValue(heldWeaponId, out var rangedWeaponData))
				{
					weaponData = rangedWeaponData;
					hasRangedWeapon = weaponData.IsRangedWeapon;
					rangedAmmoIds = weaponData.AmmoIds;
				}
				
				if (hasRangedWeapon)
				{
					int ammoId = rangedAmmoIds.Count > 0 ? rangedAmmoIds[0] : 0;
					int maxAmmoPerCharge = Math.Max(1, weaponData.MaxAmmoPerCharge);
					var weaponState = GetOrCreateRangedWeaponState(heldWeaponId, maxAmmoPerCharge);

					if (weaponState.IsReloading)
					{
						if ((Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)) && !mouseOverUI)
						{
							PlayPistolDryFireSound();
						}
						weaponState.ReloadTimer -= dt;
						if (weaponState.ReloadTimer <= 0f)
						{
							int loadedAmmo = Math.Min(maxAmmoPerCharge, weaponState.ReloadAmount);
							weaponState.CurrentAmmo = loadedAmmo;
							if (weaponState.ReloadAmmoId > 0 && loadedAmmo > 0)
								RemoveItemFromInventoryById(weaponState.ReloadAmmoId, loadedAmmo);
							weaponState.IsReloading = false;
							weaponState.ReloadTimer = 0f;
							weaponState.ReloadAmmoId = 0;
							weaponState.ReloadAmount = 0;
						}
						_rangedWeaponReloadStates[heldWeaponId] = weaponState;
					}
					else if (weaponState.CurrentAmmo <= 0 && !mouseOverUI && ammoId > 0 && (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)))
					{
						int availableAmmoCount = GetItemCountInInventory(ammoId);
						if (availableAmmoCount > 0)
						{
							StartRangedReload(heldWeaponId, ammoId, availableAmmoCount, maxAmmoPerCharge);
						}
						else
						{
							PlayPistolDryFireSound();
							string ammoMessage = "Pas de munitions !";
							AddNotification(new Notification(ammoMessage, new Color(255, 100, 100, 255), 1f));
						}
					}
					if (!weaponState.IsReloading && Raylib.IsMouseButtonDown(MouseButton.Right))
					{
						Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
						Vector2 playerAimOrigin = GetPlayerAimOrigin(_playerPos);
						Vector2 dirToMouse = mouseWorldPos - playerAimOrigin;
						if (dirToMouse.Length() > 0.01f)
						{
							float targetFacing = Math.Sign(dirToMouse.X);
							if (targetFacing != 0 && targetFacing != facing) facing = targetFacing;
							float currentFacingAngle = (facing > 0) ? 0f : 180f;
							float targetAngle = MathF.Atan2(dirToMouse.Y, dirToMouse.X) * 180f / MathF.PI;
							float angleDiff = targetAngle - currentFacingAngle;
							if (angleDiff > 180) angleDiff -= 360;
							if (angleDiff < -180) angleDiff += 360;
							_headAngle = Math.Clamp(angleDiff, -45f, 45f);
						}
					}
					else if (_headAngle != 0f && _aimCooldown <= 0f)
					{
						_headAngle = MathHelper.Lerp(_headAngle, 0f, 10f * dt);
						if (Math.Abs(_headAngle) < 1f) _headAngle = 0f;
					}
					
					// Tir (clic gauche)
					if (!mouseOverUI && !weaponState.IsReloading && weaponState.CurrentAmmo > 0 && Raylib.IsMouseButtonPressed(MouseButton.Left) && _shootCooldown <= 0f)
					{
						if (ammoId > 0)
						{
							weaponState.CurrentAmmo--;
							_rangedWeaponReloadStates[heldWeaponId] = weaponState;
							Vector2 shootDir = GetShootDirection(playerPos, camera);
							Vector2 spawnPos = GetPlayerAimOrigin(playerPos) + shootDir * 45f;
							// Le type de projectile (balle ou flèche) doit dépendre de la munition
							// réellement utilisée par l'arme (ammoId), et non d'une liste de noms
							// d'armes codée en dur : sinon toute nouvelle arme à feu (ou arme
							// renommée) utilisant "pistol_ammo" retombe silencieusement sur une
							// flèche au lieu d'une balle.
							bool useBulletProjectile = ammoId > 0 && ammoId == GetItemId("pistol_ammo");
							if (useBulletProjectile)
							{
								_bullets.Add(new ArrowProjectile(spawnPos, shootDir, 1800f, Math.Max(1f, equipment.TotalAttack + 4f)) { IsBullet = true });
								PlayPistolShootSound();
							}
							else
							{
								_arrows.Add(new ArrowProjectile(spawnPos, shootDir, 1200f, equipment.TotalAttack));
							}
							float weaponShootDelay = Math.Max(0.05f, 1f / Math.Max(0.1f, weaponData.FireRate));
							_shootCooldown = useBulletProjectile ? weaponShootDelay : Math.Max(weaponShootDelay, _shootDelay);
							_attackSwingProgress = 0.01f;
							_aimCooldown = 0.3f;
						}
						else
						{
							string ammoMessage = "Pas de munitions !";
							AddNotification(new Notification(ammoMessage, new Color(255, 100, 100, 255), 1f));
						}
					}
				}
				else if (_headAngle != 0f)
				{
					_headAngle = 0f;
				}
			}

			if (_shootCooldown > 0f) _shootCooldown -= dt;
			
			// Touches de raccourci
		for (int k = 1; k <= 9; k++)
			if (Raylib.IsKeyPressed(KeyboardKey.One + (k - 1))) hotbarSlot = k - 1;
		if (Raylib.IsKeyPressed(KeyboardKey.Zero)) hotbarSlot = 9;
		
		float wheel = Raylib.GetMouseWheelMove();
		if (wheel != 0 && !IsMouseOverAnyUI() && !_radialMenu.IsOpen)
		{
			if (KeyBindings.IsDown(GameAction.Sprint)) 
				camera.Zoom = Math.Clamp(camera.Zoom + wheel * 0.05f, 0.5f * _cameraViewportScale, 4f * _cameraViewportScale);
			else 
				hotbarSlot = (hotbarSlot - Math.Sign(wheel) + 10) % 10;
		}
		
		// Mettre à jour equipment.MainHand
		UpdateMainHandFromHotbar();
		
		// Détection de l'eau
			int waterTileX = (int)(playerPos.X / TileSize);
			int waterTileY = (int)(playerPos.Y / TileSize);
			int groundIdWater = World.GetGroundTileIdAt(waterTileX, waterTileY);
			bool onBoat = World.GetBoatAtPosition(_playerPos, TileSize) != null;
			_isPlayerInWater = (groundIdWater == 10) && !onBoat;
			
			// Gestion du clic dans l'inventaire (toujours si l'inventaire est ouvert, même si ContainerUI est ouvert)
			// NB : le clic droit est aussi écouté ici (division de pile, et sertissage de runes en drag).
			if (InventoryRenderer.IsInventoryOpen && (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)))
			{
				Vector2 mousePos = Raylib.GetMousePosition();
				bool overOtherUI = false;

				// Vérifier les autres UI ouvertes (ajoutez celles qui peuvent coexister)
				if (ContainerUI.IsOpen && IsPointInUIRect(mousePos, ContainerUI.GetWindowX(), ContainerUI.GetWindowY(), ContainerUI.GetWindowWidth(), ContainerUI.GetWindowHeight()))
					overOtherUI = true;
				else if (PortableContainerUI.IsOpen && IsPointInUIRect(mousePos, PortableContainerUI.GetWindowX(), PortableContainerUI.GetWindowY(), PortableContainerUI.GetWindowWidth(), PortableContainerUI.GetWindowHeight()))
					overOtherUI = true;
				else if (ArmorStandUI.IsOpen && IsPointInUIRect(mousePos, ArmorStandUI.GetWindowX(), ArmorStandUI.GetWindowY(), ArmorStandUI.GetWindowWidth(), ArmorStandUI.GetWindowHeight()))
					overOtherUI = true;
				else if (CauldronUI.IsOpen && IsPointInUIRect(mousePos, CauldronUI.GetWindowX(), CauldronUI.GetWindowY(), CauldronUI.GetWindowWidth(), CauldronUI.GetWindowHeight()))
					overOtherUI = true;
				else if (DyeingUI.IsOpen && IsPointInUIRect(mousePos, DyeingUI.GetWindowX(), DyeingUI.GetWindowY(), DyeingUI.GetWindowWidth(), DyeingUI.GetWindowHeight()))
					overOtherUI = true;
				else if (TraderUI.IsOpen && IsPointInUIRect(mousePos, TraderUI.GetWindowX(), TraderUI.GetWindowY(), TraderUI.GetWindowWidth(), TraderUI.GetWindowHeight()))
					overOtherUI = true;
				else if (TamedAnimalUI.IsOpen && IsPointInUIRect(mousePos, TamedAnimalUI.GetWindowX(), TamedAnimalUI.GetWindowY(), TamedAnimalUI.GetWindowWidth(), TamedAnimalUI.GetWindowHeight()))
					overOtherUI = true;
				else if (RitualCircleUI.IsMouseOverButton(mousePos))
					overOtherUI = true;
				else if (PortalUI.IsOpen && IsPointInUIRect(mousePos, PortalUI.GetWindowX(), PortalUI.GetWindowY(), PortalUI.GetWindowWidth(), PortalUI.GetWindowHeight()))
					overOtherUI = true;
				else if (CraftingUI.IsOpen && IsPointInUIRect(mousePos, CraftingUI.GetWindowX(), CraftingUI.GetWindowY(), CraftingUI.GetWindowWidth(), CraftingUI.GetWindowHeight()))
					overOtherUI = true;
				else if (GuildUI.IsOpen && IsPointInUIRect(mousePos, GuildUI.GetWindowX(), GuildUI.GetWindowY(), GuildUI.GetWindowWidth(), GuildUI.GetWindowHeight()))
					overOtherUI = true;
				else if (CharacterCustomizationUI.IsOpen && IsPointInUIRect(mousePos, CharacterCustomizationUI.GetWindowX(), CharacterCustomizationUI.GetWindowY(), CharacterCustomizationUI.GetWindowWidth(), CharacterCustomizationUI.GetWindowHeight()))
					overOtherUI = true;
				else if (ItemMenuUI.IsOpen && IsPointInUIRect(mousePos, ItemMenuUI.GetWindowX(), ItemMenuUI.GetWindowY(), ItemMenuUI.GetWindowWidth(), ItemMenuUI.GetWindowHeight()))
					overOtherUI = true;
				// Ajoutez ici d’autres UI si nécessaire

				if (!overOtherUI)
					InventoryRenderer.HandleSlotClick(mousePos, equipment);
			}
			
			// Gestion de la touche E (interaction) avec priorité corrigée
			if (!ChatSystem.IsOpen && !mouseOverUI)
			{
				if (PlayerCurrentCar == null && KeyBindings.IsPressed(GameAction.Interact) && !_isAdjustingSail)
				{
					// UNIQUEMENT le gouvernail (ID 109) permet de régler les voiles
					var (wheelBoat, _, _, wheelId) = GetInteractiveBoatPartUnderMouse(camera, Boat.WheelObjectId);
					if (wheelBoat != null && wheelId == Boat.WheelObjectId)
					{
						// Mode réglage de voile via le gouvernail
						_isAdjustingSail = true;
						_adjustedBoat = wheelBoat;
						_currentSailAngle = wheelBoat.SailAngle;
						_initialSailAngle = wheelBoat.SailAngle;
						_sailAdjustStartMouseScreen = Raylib.GetMousePosition();
					}
					else
					{
						//  MODE CONNEXION POUR DYNAMO/BATTERIE 
						// Vérifier si on clique sur une dynamo ou une batterie pour connecter un fil
						(int tileX, int tileY, _, int objectId) = FindTileUnderMouse(mouseWorld, TileSize);
						
						if (objectId == 403) // Dynamo
						{
							if (!_isConnectingWire)
							{
								// Commencer la connexion
								_isConnectingWire = true;
								_wireStartTile = (tileX, tileY);
								
								if (!World.Dynamos.ContainsKey((tileX, tileY)))
									World.Dynamos[(tileX, tileY)] = new DynamoData();
								_wireStartDynamo = World.Dynamos[(tileX, tileY)];
								_wireStartBattery = null;
								
								AddNotification(new Notification(" Mode connexion activé. Cliquez sur une batterie pour connecter.", new Color(100, 200, 255, 255), 2.5f));
							}
							else
							{
								// Annuler le mode connexion
								_isConnectingWire = false;
								_wireStartTile = (-1, -1);
								_wireStartDynamo = null;
								_wireStartBattery = null;
								AddNotification(new Notification(" Connexion annulée.", new Color(255, 200, 100, 255), 1.5f));
							}
						}
						else if (objectId == 408 && !_isConnectingWire) // Ampoule électrique - démarrer la connexion
						{
							// Commencer la connexion
							_isConnectingWire = true;
							_wireStartTile = (tileX, tileY);
							_wireStartDynamo = null;
							_wireStartBattery = null;
							
							AddNotification(new Notification(" Mode connexion activé. Cliquez sur une batterie pour connecter l'ampoule.", new Color(100, 200, 255, 255), 2.5f));
						}
						else if (World.GetGateTypeForObjectId(objectId) is GateType gateTypeHere) // Porte logique / interrupteur / plaque / délai
						{
							if (gateTypeHere == GateType.SWITCH && Raylib.IsKeyDown(KeyboardKey.LeftShift))
							{
								//  MAJ+E sur un interrupteur : bascule ON/OFF au lieu de démarrer un câblage
								if (NetworkManager.IsClient)
								{
									NetworkManager.RequestEnergyAction("ToggleSwitch", tileX, tileY);
									AddNotification(new Notification(" Interrupteur : commutation en cours...", new Color(100, 200, 255, 255), 1.5f));
								}
								else
								{
									bool nowOn = World.ToggleLogicSwitch(tileX, tileY);
									if (NetworkManager.IsHost) NetworkManager.BroadcastEnergySnapshot();
									AddNotification(new Notification(nowOn ? " Interrupteur activé" : " Interrupteur désactivé",
										new Color(100, 200, 255, 255), 1.5f));
								}
							}
							else if (gateTypeHere == GateType.DELAY && Raylib.IsKeyDown(KeyboardKey.LeftShift))
							{
								//  MAJ+E sur un bloc de délai : fait défiler les préréglages de durée
								// (0.5s à 30s) au lieu de démarrer un câblage.
								if (NetworkManager.IsClient)
								{
									NetworkManager.RequestEnergyAction("CycleDelay", tileX, tileY);
									AddNotification(new Notification(" Bloc de délai : réglage en cours...", new Color(100, 200, 255, 255), 1.5f));
								}
								else
								{
									float newDelay = World.CycleDelayBlock(tileX, tileY);
									if (NetworkManager.IsHost) NetworkManager.BroadcastEnergySnapshot();
									AddNotification(new Notification($" Délai réglé : {newDelay:0.#}s", new Color(100, 200, 255, 255), 1.5f));
								}
							}
							else if (!_isConnectingWire)
							{
								// Commencer la connexion depuis la sortie de cette porte / interrupteur
								_isConnectingWire = true;
								_wireStartTile = (tileX, tileY);
								if (!World.LogicGates.ContainsKey((tileX, tileY)))
									World.LogicGates[(tileX, tileY)] = new LogicGateData { Type = gateTypeHere };
								_wireStartGate = World.LogicGates[(tileX, tileY)];
								_wireStartDynamo = null;
								_wireStartBattery = null;
								AddNotification(new Notification($" Mode connexion activé ({gateTypeHere}). Cliquez sur une entrée à relier.", new Color(100, 200, 255, 255), 2.5f));
							}
							else if (_wireStartTile != (tileX, tileY))
							{
								// Cette porte est la CIBLE du câble (une de ses entrées)
								if (NetworkManager.IsClient)
								{
									NetworkManager.RequestEnergyAction("ConnectWire", _wireStartTile.x, _wireStartTile.y, tileX, tileY);
									AddNotification(new Notification(" Connexion en cours...", new Color(100, 200, 255, 255), 1.5f));
								}
								else
								{
									string result = World.ConnectEnergyWire(_wireStartTile.x, _wireStartTile.y, tileX, tileY);
									if (result == "ok")
									{
										AddNotification(new Notification($" Porte logique connectée !", new Color(100, 255, 100, 255), 2f));
										if (NetworkManager.IsHost) NetworkManager.BroadcastEnergySnapshot();
									}
									else if (result == "already_exists")
										AddNotification(new Notification(" Connexion déjà existante !", new Color(255, 200, 100, 255), 1.5f));
									else
										AddNotification(new Notification(" Connexion invalide !", new Color(255, 100, 100, 255), 1.5f));
								}

								_isConnectingWire = false;
								_wireStartTile = (-1, -1);
								_wireStartDynamo = null;
								_wireStartBattery = null;
								_wireStartGate = null;
							}
							else
							{
								// Re-clic sur la même tuile : annuler le mode connexion
								_isConnectingWire = false;
								_wireStartTile = (-1, -1);
								_wireStartDynamo = null;
								_wireStartBattery = null;
								_wireStartGate = null;
								AddNotification(new Notification(" Connexion annulée.", new Color(255, 200, 100, 255), 1.5f));
							}
						}
						else if (objectId == 408 && _isConnectingWire && _wireStartTile != (-1, -1)) // Ampoule électrique - CIBLAGE
						{
							//  MULTIJOUEUR : même logique unifiée que pour une batterie cible.
							if (NetworkManager.IsClient)
							{
								NetworkManager.RequestEnergyAction("ConnectWire", _wireStartTile.x, _wireStartTile.y, tileX, tileY);
								AddNotification(new Notification(" Connexion en cours...", new Color(100, 200, 255, 255), 1.5f));
							}
							else
							{
								string result = World.ConnectEnergyWire(_wireStartTile.x, _wireStartTile.y, tileX, tileY);
								if (result == "ok")
								{
									AddNotification(new Notification($" Ampoule connectée !", new Color(100, 255, 100, 255), 2f));
									if (NetworkManager.IsHost) NetworkManager.BroadcastEnergySnapshot();
								}
								else if (result == "already_exists")
									AddNotification(new Notification(" Connexion déjà existante !", new Color(255, 200, 100, 255), 1.5f));
								else
									AddNotification(new Notification(" Connexion impossible : port incompatible.", new Color(255, 100, 100, 255), 1.5f));
							}
							
							_isConnectingWire = false;
							_wireStartTile = (-1, -1);
							_wireStartDynamo = null;
							_wireStartBattery = null;
						}
						else if (objectId == 405 && _isConnectingWire && _wireStartTile != (-1, -1)) // Chargeur de batterie - CIBLAGE (entrée de câble)
						{
							if (NetworkManager.IsClient)
							{
								NetworkManager.RequestEnergyAction("ConnectWire", _wireStartTile.x, _wireStartTile.y, tileX, tileY);
								AddNotification(new Notification(" Connexion en cours...", new Color(100, 200, 255, 255), 1.5f));
							}
							else
							{
								string result = World.ConnectEnergyWire(_wireStartTile.x, _wireStartTile.y, tileX, tileY);
								if (result == "ok")
								{
									AddNotification(new Notification($" Chargeur connecté !", new Color(100, 255, 100, 255), 2f));
									if (NetworkManager.IsHost) NetworkManager.BroadcastEnergySnapshot();
								}
								else if (result == "already_exists")
									AddNotification(new Notification(" Connexion déjà existante !", new Color(255, 200, 100, 255), 1.5f));
								else
									AddNotification(new Notification(" Connexion impossible : port incompatible.", new Color(255, 100, 100, 255), 1.5f));
							}

							_isConnectingWire = false;
							_wireStartTile = (-1, -1);
							_wireStartDynamo = null;
							_wireStartBattery = null;
						}
						else if (objectId == 405 && !_isConnectingWire) // Chargeur de batterie - poser/retirer la batterie portable
						{
							HandleBatteryChargerInteraction(tileX, tileY);
						}
						else
						{
							// PRIORITÉ 1 : Interaction avec les tuiles (stations, coffres, etc.)
							bool interactedWithTile = TryInteractWithStation(playerPos, camera);
							
							// ========== Gestion de la touche E pour les animaux apprivoisés ==========
							if (!InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && PlayerCurrentCar == null)
							{
								Entity? tamedAnimalUnderMouse = FindTamedAnimalUnderMouse(camera);
								bool hasTamedAnimalUnderMouse = tamedAnimalUnderMouse != null && tamedAnimalUnderMouse.IsTamed;

								if (hasTamedAnimalUnderMouse && !TamedAnimalInteraction.IsOpen && !SlimeStorageUI.IsOpen)
								{
									bool ePressed = KeyBindings.IsPressed(GameAction.Interact);
									bool eDown = KeyBindings.IsDown(GameAction.Interact);
									bool eReleased = KeyBindings.IsReleased(GameAction.Interact);

									if (ePressed)
									{
										_eHoldTime = 0f;
										_eWasPressed = true;
										_eInteractionHandled = false;
									}

									if (_eWasPressed && eDown && !_eInteractionHandled)
									{
										_eHoldTime += dt;

										// Long press → menu radial
										if (_eHoldTime >= E_LONG_PRESS_THRESHOLD && !_eInteractionHandled)
										{
											_eInteractionHandled = true;
											TamedAnimalInteraction.OpenRadial(tamedAnimalUnderMouse, camera);
										}
									}

									if (eReleased && _eWasPressed)
									{
										//  L'appui court n'ouvre plus l'UI d'infos : on y accède désormais
										// via le rond central du menu radial (appui long sur E).
										_eWasPressed = false;
										_eInteractionHandled = false;
									}
								}
								else if (TamedAnimalInteraction.IsOpen)
								{
									TamedAnimalInteraction.Update(null, false, false, false, Raylib.GetMousePosition(), camera);
								}
							}
						}
					}
				}
				
				// Gestion de l'attaque / mine (plus bloqué par l'inventaire)
				if (PlayerCurrentCar == null)
				{
					if (attackCooldown > 0f) attackCooldown -= dt;
					if (attackAnim > 0f) attackAnim -= dt;
					else isAttacking = false;
				}
				
				// Mode voiture (touche R)
				if (Raylib.IsKeyPressed(KeyboardKey.R))
				{
					isCarMode = PlayerCurrentCar == null;
					if (isCarMode)
					{
						carAngle = _playerFacing > 0 ? 90f : -90f;
						carVelX = 0f;
						carVelY = 0f;
						_carAngularVelocity = 0f;
						wheelRotation = 0f;
						wheelSpin = 0f;
					}
					else
					{
						carVelX = 0f;
						carVelY = 0f;
						_carAngularVelocity = 0f;
						wheelRotation = 0f;
						wheelSpin = 0f;
					}
				}
			}
			
			if (_aimCooldown > 0f)
			{
				_aimCooldown -= dt;
				if (_aimCooldown <= 0f && _headAngle != 0f)
				{
					_headAngle = MathHelper.Lerp(_headAngle, 0f, 10f * dt);
					if (Math.Abs(_headAngle) < 1f) _headAngle = 0f;
				}
			}
			
			// Gestion de la position assise (sauf si monté)
			if (_isSitting && MountedAnimal == null)
			{
				if (_sittingJustStarted)
				{
					_sittingJustStarted = false;
				}
				else if (IsMovementInputPressed())
				{
					SetSitting(false);
				}
				else
				{
					if (_animState != _sittingPose)
					{
						_animState = _sittingPose;
						_animFrame = 0; _animProg = 0f;
					}
					int frameCount = 8;
					if (SpeciesData.Skeletons.ContainsKey("human"))
					{
						var humanSkeleton = SpeciesData.Skeletons["human"];
						var firstPart = humanSkeleton.FirstOrDefault();
						if (firstPart != null && firstPart.Animations.TryGetValue(_sittingPose, out var frames))
							frameCount = frames.Count;
					}
					if (_sittingPose == "sleep")
					{
						// Le sommeil est autorisé le jour comme la nuit, mais la progression du temps
						// ne s'accélère que la nuit. La journée ne fait donc qu'un "repos" sans effet
						// sur le cycle du monde.
						if (GetDarknessAlpha() > 0.6f)
						{
							_gameTime += dt * 59f;
						}
					}
					_animProg += dt * 3f;
					if (_animProg >= 1f) { _animProg = 0f; _animFrame = (_animFrame + 1) % frameCount; }
					if (_isSitting) goto SkipMovementAndInteraction;
				}
			}
			if (CartesGameUI.ShouldBlockPlayerMovement())
			{
				goto SkipMovementAndInteraction;
			}

			if (ChatSystem.IsOpen && !_isSitting && PlayerCurrentCar == null && MountedAnimal == null)
			{
				if (_animState != "idle")
				{
					_animState = "idle";
					_animFrame = 0;
					_animProg = 0f;
					_animRawFrame = 0;
					_animRawProg = 0f;
				}

				int idleFrameCount = 8;
				if (SpeciesData.Skeletons.ContainsKey("human"))
				{
					var humanSkeleton = SpeciesData.Skeletons["human"];
					var firstPart = humanSkeleton.FirstOrDefault();
					if (firstPart != null && firstPart.Animations.TryGetValue("idle", out var frames))
						idleFrameCount = frames.Count;
				}

				_animRawProg += dt * 4f;
				if (_animRawProg >= 1f)
				{
					_animRawProg -= 1f;
					_animRawFrame = (_animRawFrame + 1) % idleFrameCount;
				}
				_animFrame = _animRawFrame;
				_animProg = _animRawProg;
			}
			if (!ChatSystem.IsOpen)
			{
				UpdateLooseWagons(dt);
				if (!PlayerCurrentWagonTile.HasValue) UpdateWagonLinks();
				if (PlayerCurrentWagonTile.HasValue)
				{
					UpdateMountedWagon(dt);
					UpdateWagonLinks(); // avant la position du joueur : le wagon monté peut lui-même être tiré
					_playerPos = GetMountedWagonPosition();
					if (_animState != "sit_wagon")
					{
						_animRawFrame = 0;
						_animRawProg = 0f;
					}
					_animState = "sit_wagon";
					if (_wagonHeading.x != 0)
						facing = _playerFacing = Math.Sign(_wagonHeading.x);

					int wagonFrameCount = 1;
					if (SpeciesData.Skeletons.TryGetValue(PlayerMorphSpecies, out var wagonSkeleton))
				{
						foreach (var part in wagonSkeleton)
						{
							if (!part.Animations.TryGetValue("sit_wagon", out var frames)) continue;
							wagonFrameCount = frames.Count;
							break;
						}
					}

					if (_wagonSpeed > 0.1f && wagonFrameCount > 1)
					{
						_animRawProg += dt * 6f;
						if (_animRawProg >= 1f)
						{
							_animRawProg -= 1f;
							_animRawFrame = (_animRawFrame + 1) % wagonFrameCount;
						}
					}
					_animFrame = _animRawFrame % wagonFrameCount;
					_animProg = _wagonSpeed > 0.1f ? _animRawProg : 0f;
					goto SkipMovementAndInteraction;
				}
				else if (PlayerCurrentCar != null)
				{
					PlayerCurrentCar.Update(dt, destroyedObjects);
					_playerPos = PlayerCurrentCar.Position;
					//  Forcer l'animation assise
					_animState = "sit";
					_animFrame = 0;
					_animProg = 0f;
					goto SkipMovementAndInteraction;
				}
				else if (MountedAnimal != null)
				{
					Vector2 moveDir = Vector2.Zero;
					if (KeyBindings.IsDown(GameAction.MoveUp)) moveDir.Y -= 1;
					if (KeyBindings.IsDown(GameAction.MoveDown)) moveDir.Y += 1;
					if (KeyBindings.IsDown(GameAction.MoveLeft)) moveDir.X -= 1;
					if (KeyBindings.IsDown(GameAction.MoveRight)) moveDir.X += 1;

					if (moveDir.LengthSquared() > 0.01f)
						moveDir = Vector2.Normalize(moveDir);

					bool isSprinting = KeyBindings.IsDown(GameAction.Sprint);
					float speed = isSprinting ? MountedAnimal.RunSpeed : MountedAnimal.BaseSpeed;
					// Utiliser Speed comme alternative si RunSpeed n'est pas défini
					if (speed <= 0) speed = MountedAnimal.Speed;

					Vector2 move = moveDir * speed * dt;
					// Valider le vecteur de mouvement
					if (float.IsNaN(move.X) || float.IsNaN(move.Y) || float.IsInfinity(move.X) || float.IsInfinity(move.Y))
					{
						move = Vector2.Zero;
					}
					bool isMoveIntended = move.LengthSquared() > 0.01f;

					if (isMoveIntended)
					{
						Vector2 newPos = MountedAnimal.WorldPos + move;
						if (!World.IsCollidingEntity(newPos, destroyedObjects, MountedAnimal.Species))
							MountedAnimal.WorldPos = newPos;

						MountedAnimal.Facing = Math.Sign(move.X) != 0 ? Math.Sign(move.X) : MountedAnimal.Facing;
						// Animation : run si sprint, walk sinon — reflète l'intention du joueur,
						// même si l'animal est momentanément bloqué par une collision.
						MountedAnimal.AnimState = isSprinting ? "run" : "walk";
					}
					else
					{
						MountedAnimal.AnimState = "idle";
					}

					// Mettre à jour la position du joueur (assise sur l'animal)
					Vector2 offset = GetMountOffset(MountedAnimal);
					_playerPos = MountedAnimal.WorldPos + offset;
					SetSitting(true, _playerPos, 0f, "sit");

					// Caméra
					_smoothedCameraTarget = _playerPos;
				}
				else
				{
					bool isMoving = false;
					bool isCarrying = false;
					if (heldItem != null)
					{
						int id = GetItemId(heldItem.Name);
						if (GameData.ItemDatabase.TryGetValue(id, out var data) && data.Type == ItemType.Placeable) isCarrying = true;
					}
					
					bool isHoldingThrowable = heldItem != null &&
					GameData.ItemDatabase.TryGetValue(GetItemId(heldItem.Name), out var heldThrowableData) &&
					heldThrowableData.IsThrowable;
					bool isAimingWithRangedWeapon = Raylib.IsMouseButtonDown(MouseButton.Right) &&
					!mouseOverUI && PlayerCurrentCar == null &&
					((heldItem != null && GameData.IsRangedWeapon(heldItem.Name)) || isHoldingThrowable);

					// --- Mouvement : clavier uniquement pour le joueur principal ---
					float speed = GetPlayerBaseSpeed();
					bool wantsSprint = KeyBindings.IsDown(GameAction.Sprint);
					//  On ne peut sprinter que s'il reste de l'endurance ET que la barre
					// n'est pas verrouillée (vidée complètement, en attente de recharge totale).
					bool isSprinting = wantsSprint && playerStamina > 0f && !staminaLocked;
					if (isSprinting) speed = GetPlayerRunSpeed();

					Vector2 moveDir = Vector2.Zero;
					float moveMagnitude = 0f;

					bool keyboardInputDetected = false;
					if (KeyBindings.IsDown(GameAction.MoveUp)) { moveDir.Y -= 1; keyboardInputDetected = true; }
					if (KeyBindings.IsDown(GameAction.MoveDown)) { moveDir.Y += 1; keyboardInputDetected = true; }
					if (KeyBindings.IsDown(GameAction.MoveLeft)) { moveDir.X -= 1; keyboardInputDetected = true; }
					if (KeyBindings.IsDown(GameAction.MoveRight)) { moveDir.X += 1; keyboardInputDetected = true; }

					//  Gauche + droite enfoncées en même temps : moveDir.X s'annule à 0 (le
					// perso s'arrête), mais le regard ne doit PAS basculer vers la touche
					// enfoncée en second — il doit rester celui d'avant (ex : on allait à
					// gauche, on appuie aussi sur droite -> arrêt, toujours tourné à gauche).
					// On ne met donc à jour `facing` que si un mouvement horizontal net est
					// réellement demandé, sinon on conserve la valeur actuelle.
					if (moveDir.X > 0f) facing = 1f;
					else if (moveDir.X < 0f) facing = -1f;

					bool keyPressed = keyboardInputDetected && moveDir.LengthSquared() > 0.01f;
					if (!keyboardInputDetected && TryGetPrimaryGamepadIndex(out int gamepadIndex))
					{
						float axisX = Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.LeftX);
						float axisY = Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.LeftY);
						const float DEADZONE = 0.15f;
						float rawMagnitude = MathF.Sqrt(axisX * axisX + axisY * axisY);
						if (rawMagnitude > DEADZONE)
						{
							moveDir = new Vector2(axisX, axisY) / rawMagnitude;
							moveMagnitude = 1f;
							if (MathF.Abs(moveDir.X) > 0.01f) facing = moveDir.X > 0 ? 1f : -1f;
							keyPressed = true;
						}
					}

					if (keyPressed)
					{
						moveDir = Vector2.Normalize(moveDir);
						moveMagnitude = 1f;
					}

					if (isAimingWithRangedWeapon)
					{
						Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
						Vector2 playerVisualPos = GetPlayerVisualPosition(playerPos);
						Vector2 dirToMouse = mouseWorldPos - playerVisualPos;
						if (dirToMouse.Length() > 0.01f)
						{
							float targetFacing = Math.Sign(dirToMouse.X);
							if (targetFacing != 0f) facing = targetFacing;
						}
					}

					// --- Appliquer le mouvement ---
					if (moveMagnitude > 0.01f)
					{
						if (_isDancing)
						{
							_isDancing = false;
							_animState = "walk";
							_animFrame = 0; _animProg = 0f;
						}
						if (_isEmoteWheelOpen)
						{
							_isEmoteWheelOpen = false;
							_emoteWheelHoverIndex = -1;
						}
						if (!string.IsNullOrEmpty(_currentEmote))
						{
							_currentEmote = null;
						}
						Vector2 move = moveDir * speed * moveMagnitude * dt;
						// Valider le vecteur de mouvement
						if (float.IsNaN(move.X) || float.IsNaN(move.Y) || float.IsInfinity(move.X) || float.IsInfinity(move.Y))
						{
							move = Vector2.Zero;
						}
						Vector2 movementStart = playerPos;
						int currentTileX = (int)(playerPos.X / TileSize);
						int currentTileY = (int)(playerPos.Y / TileSize);
						int currentHeight = World.GetHeightAt(currentTileX, currentTileY);
						int newTileX = (int)((playerPos.X + move.X) / TileSize);
						int newHeightX = World.GetHeightAt(newTileX, currentTileY);
						if (Math.Abs(currentHeight - newHeightX) <= 1)
							if (_noClipEnabled || !World.IsColliding(playerPos + new Vector2(move.X, 0), hitboxDim, destroyedObjects))
								playerPos.X += move.X;
						
						// Mouvement vertical
						int newTileY = (int)((playerPos.Y + move.Y) / TileSize);
						int newHeightY = World.GetHeightAt(currentTileX, newTileY);
						if (Math.Abs(currentHeight - newHeightY) <= 1)
							if (_noClipEnabled || !World.IsColliding(playerPos + new Vector2(0, move.Y), hitboxDim, destroyedObjects))
								playerPos.Y += move.Y;

						World.ApplyPlayerMovementPush(movementStart, playerPos - movementStart, dt);

						// Valider la position après l'application du mouvement
						if (float.IsNaN(playerPos.X) || float.IsNaN(playerPos.Y) ||
							float.IsInfinity(playerPos.X) || float.IsInfinity(playerPos.Y))
						{
							playerPos = _worldSpawnPos;
							Console.WriteLine($"ATTENTION : Position invalide détectée, réinitialisée au spawn ({_worldSpawnPos})");
							_cameraNeedsImmediateCenter = true;
						}
						
						isMoving = true;
					}
					else isMoving = false;

					//  Marche/course à reculons : si le déplacement horizontal va à l'opposé
					// de la direction regardée (facing), on jouera l'animation à l'envers.
					bool isWalkingBackward = isMoving && facing != 0f && MathF.Abs(moveDir.X) > 0.01f
						&& Math.Sign(moveDir.X) != Math.Sign(facing);

					string nextAnim;
					if (!string.IsNullOrEmpty(_currentEmote))
					{
						if (_currentEmote == "sleep") nextAnim = "sleep";
						else if (_currentEmote == "sit_floor") nextAnim = "sit_floor";
						else if (_currentEmote == "hula_dance") nextAnim = "hula_dance";
						else nextAnim = "wavedance";
					}
					else if (_isDancing) nextAnim = "wavedance";
					else if (isAttacking) nextAnim = "attack";
					else if (isMoving)
					{
						// Récupérer la vitesse actuelle (sprint = BaseSpeed * SprintMult)
						float currentSpeed = isSprinting ? GetPlayerRunSpeed() : GetPlayerBaseSpeed();
						if (isSprinting && currentSpeed > GetPlayerBaseSpeed() * 1.2f)
							nextAnim = "run";
						else nextAnim = "walk";
					}
					else nextAnim = "idle";
					
					if (SpeciesData.Skeletons.ContainsKey("human"))
					{
						var humanSkeleton = SpeciesData.Skeletons["human"];
						var firstPart = humanSkeleton.FirstOrDefault();
						if (firstPart != null && !firstPart.Animations.ContainsKey(nextAnim))
							nextAnim = isMoving ? "walk" : "idle";
					}
					if (nextAnim != _animState)
					{
						_animState = nextAnim;
						_animFrame = 0;
						_animProg = 0f;
						_animRawFrame = 0;
						_animRawProg = 0f;
					}
					int fc = 8;
					if (SpeciesData.Skeletons.ContainsKey("human"))
					{
						var humanSkeleton = SpeciesData.Skeletons["human"];
						var firstPart = humanSkeleton.FirstOrDefault();
						if (firstPart != null && firstPart.Animations.TryGetValue(_animState, out var frames))
							fc = frames.Count;
					}
					// On n'inverse que pour les états de déplacement (walk/run), pas idle/attack/etc.
					_animReversed = isWalkingBackward && (_animState == "walk" || _animState == "run");
					_animRawProg += dt * (isMoving ? 10f : 4f);
					if (_animRawProg >= 1f)
					{
						_animRawProg -= 1f;
						// Le sens dans lequel on parcourt la séquence de poses dépend de _animReversed :
						// en avant on incrémente normalement, à reculons on décrémente pour rejouer
						// exactement la même séquence de poses mais dans l'ordre inverse.
						_animRawFrame = _animReversed
							? (_animRawFrame - 1 + fc) % fc
							: (_animRawFrame + 1) % fc;
					}
					if (_animReversed)
					{
						// Interpole "à l'envers" entre la frame courante et la précédente,
						// en réutilisant tel quel le pipeline de rendu existant (frame -> frame+1).
						_animFrame = (_animRawFrame - 1 + fc) % fc;
						_animProg = 1f - _animRawProg;
					}
					else
					{
						_animFrame = _animRawFrame;
						_animProg = _animRawProg;
					}
					if (isMoving && ShouldTriggerFootstepFrame(_animState, _animFrame, fc))
						TriggerPlayerFootstepEffects();
				}
				
				if (!mouseOverUI)
				{
					//  Clic gauche : PLACER (si item plaçable) sinon ATTAQUER/CASSER
					if (PlayerCurrentCar == null && Raylib.IsMouseButtonPressed(MouseButton.Left) && !leftClickOnOpenUIThisFrame)
					{
						if (TryCatchInsectWithNet(playerPos, camera))
						{
							// Capture effectuée, ne pas continuer avec les autres actions
						}
						//  MULTIJOUEUR : dépose (côté client) d'une créature portée via le réseau -
						// _carriedCreature n'existe pas chez un client, donc on passe par le chemin dédié.
						else if (NetworkManager.IsClient && IsClientCarryingCreature)
						{
							Vector2 dropMouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
							(int dropTileX, int dropTileY, float dropDist, _) = FindTileUnderMouse(dropMouseWorld, TileSize);
							if (dropTileX != -1 && dropTileY != -1)
							{
								float distanceToDropTile = Vector2.Distance(
									new Vector2((int)(playerPos.X / TileSize) * TileSize + TileSize / 2f, (int)(playerPos.Y / TileSize) * TileSize + TileSize / 2f),
									new Vector2(dropTileX * TileSize + TileSize / 2f, dropTileY * TileSize + TileSize / 2f)
								);
								if (distanceToDropTile <= MAX_INTERACTION_DISTANCE)
								{
									Vector2 dropTargetPos = new Vector2(dropTileX * TileSize + TileSize / 2f, dropTileY * TileSize + TileSize / 2f);
									TryDropCreatureClient(dropTargetPos);
								}
							}
						}
						// PRIORITÉ 1 : Si on porte un meuble ou une créature, la/le poser
						else if (_carriedFurniture != null || _carriedCreature != null)
						{
							HandlePlace(Raylib.GetMousePosition(), camera, null);
						}
						// PRIORITÉ 2 : Si on tient un item dans la main (hotbar)
						else if (heldItem != null)
						{
							var data = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == heldItem.Name);
							if (data.ID != 0)
							{
								// Les graines utilisent la même logique que le clic droit : plantation directe
								if (data.IsSeed)
								{
									HandleInteract(Raylib.GetMousePosition(), camera, dt, playerPos, facing);
								}
								// Si c'est un item plaçable, le poser
								else if (data.Type == ItemType.Placeable)
								{
									HandlePlace(Raylib.GetMousePosition(), camera, heldItem);
								}
								// Si c'est un revêtement mural
								else if (data.Type == ItemType.WallCovering)
								{
									HandleWallCoveringPlace(mouseWorld, heldItem, data);
								}
								// Sinon (outil, arme, nourriture, etc.) → ATTAQUER/CASSER
								else
								{
									HandleInteract(Raylib.GetMousePosition(), camera, dt, playerPos, facing);
								}
							}
							else
							{
								HandleInteract(Raylib.GetMousePosition(), camera, dt, playerPos, facing);
							}
						}
						// PRIORITÉ 3 : Rien en main → ATTAQUER/CASSER
						else
						{
							HandleInteract(Raylib.GetMousePosition(), camera, dt, playerPos, facing);
						}
					}
					
				}
				
				// Visée avec les armes/outils (non bloqué par l'inventaire)
				bool isWeaponOrTool = equipment.MainHand != null &&
					(GameData.GetItemType(equipment.MainHand.Name) == ItemType.Weapon ||
					 GameData.GetItemType(equipment.MainHand.Name) == ItemType.Tool);
				bool isRangedWeapon = heldItem != null && GameData.IsRangedWeapon(heldItem.Name);
				if (PlayerCurrentCar == null && isWeaponOrTool && !isRangedWeapon)
				{
					Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
					Vector2 playerAimOrigin = GetPlayerAimOrigin(playerPos);
					Vector2 dirToMouse = mouseWorldPos - playerAimOrigin;
					if (dirToMouse.Length() > 0.01f)
					{
						float targetFacing = Math.Sign(dirToMouse.X);
						if (targetFacing != 0 && targetFacing != facing) facing = targetFacing;
						float currentFacingAngle = (facing > 0) ? 0f : 180f;
						float targetAngle = MathF.Atan2(dirToMouse.Y, dirToMouse.X) * 180f / MathF.PI;
						float angleDiff = targetAngle - currentFacingAngle;
						if (angleDiff > 180) angleDiff -= 360;
						if (angleDiff < -180) angleDiff += 360;
						_headAngle = Math.Clamp(angleDiff, -45f, 45f);
					}
				}
				else if (!isRangedWeapon)
				{
					_headAngle = 0f;
				}
				
				if (_isSitting)
				{
					camera.Target = _sittingPosition;
				}
				else
				{
					int playerTileX = (int)(playerPos.X / TileSize);
					int playerTileY = (int)(playerPos.Y / TileSize);
					int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
					float playerYOffset = -playerHeight * TileSize / 4;
					float visualYOffset = World.IsUnderwater ? -_underwaterElevation : 0f;
					camera.Target = new Vector2(playerPos.X, playerPos.Y + playerYOffset + visualYOffset);
				}
			}
			
		SkipMovementAndInteraction:

			// Position visuelle : si assis, utiliser _sittingPosition, sinon utiliser playerPos + offset
			Vector2 playerVisualPosForCamera;
			Vector2 playerDrawPosForCamera;
			if (_isSitting)
			{
				playerVisualPosForCamera = GetSittingVisualPosition();
				playerDrawPosForCamera = playerVisualPosForCamera;
				
				if (_cameraNeedsImmediateCenter)
				{
					_smoothedCameraTarget = playerVisualPosForCamera;
					camera.Target = _smoothedCameraTarget;
					_cameraNeedsImmediateCenter = false;
				}
				else
				{
					_smoothedCameraTarget = Vector2.Lerp(_smoothedCameraTarget, playerVisualPosForCamera, dt * _cameraSmoothingSpeed);
					camera.Target = _smoothedCameraTarget;
				}
			}
			else
			{
				playerVisualPosForCamera = GetPlayerCameraTarget(playerPos);
				playerDrawPosForCamera = playerVisualPosForCamera;
				
				if (_cameraNeedsImmediateCenter)
				{
					_smoothedCameraTarget = playerVisualPosForCamera;
					camera.Target = _smoothedCameraTarget;
					_cameraNeedsImmediateCenter = false;
				}
				else
				{
					Vector2 targetCameraPos = playerVisualPosForCamera;
					Vector2 desiredAimOffset = Vector2.Zero;
					bool isHoldingThrowable = heldItem != null &&
						GameData.ItemDatabase.TryGetValue(GetItemId(heldItem.Name), out var heldThrowableData) &&
						heldThrowableData.IsThrowable;
					bool isAiming = Raylib.IsMouseButtonDown(MouseButton.Right) &&
						PlayerCurrentCar == null &&
						((heldItem != null && GameData.IsRangedWeapon(heldItem.Name)) || isHoldingThrowable);
					if (isAiming)
					{
						Vector2 screenCenter = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f);
						Vector2 mouseScreenPos = Raylib.GetMousePosition();
						Vector2 centerWorldPos = Raylib.GetScreenToWorld2D(screenCenter, camera);
						Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(mouseScreenPos, camera);
						Vector2 mouseDeltaWorld = mouseWorldPos - centerWorldPos;
						if (mouseDeltaWorld.Length() > 0.01f)
						{
							float offsetStrength = Math.Clamp(mouseDeltaWorld.Length() / 220f, 0f, 1f);
							float zoomFactor = Math.Clamp(camera.Zoom, 0.7f, 2.2f);
							float maxOffset = 36f + zoomFactor * 24f;
							desiredAimOffset = Vector2.Normalize(mouseDeltaWorld) * (offsetStrength * maxOffset);
						}
					}
					
					float smoothingSpeed = isAiming ? 10f : 14f;
					_aimCameraOffset = Vector2.Lerp(_aimCameraOffset, desiredAimOffset, dt * smoothingSpeed);
					targetCameraPos += _aimCameraOffset;

					bool isFarFromWorldOrigin = MathF.Max(
						MathF.Abs(targetCameraPos.X), MathF.Abs(targetCameraPos.Y)) > 100000f;
					if (isFarFromWorldOrigin)
					{
						targetCameraPos = new Vector2(
							MathF.Round(targetCameraPos.X * 16f) / 16f,
							MathF.Round(targetCameraPos.Y * 16f) / 16f);
						_smoothedCameraTarget = targetCameraPos;
					}
					else
					{
						// Si la caméra est trop loin du joueur, sauter directement au lieu de lisser
						// pour éviter de charger/rendre tout le terrain intermédiaire
						float cameraDistance = Vector2.Distance(_smoothedCameraTarget, targetCameraPos);
						if (cameraDistance > 400f)
						{
							_smoothedCameraTarget = targetCameraPos;
						}
						else
						{
							_smoothedCameraTarget = Vector2.Lerp(_smoothedCameraTarget, targetCameraPos, dt * _cameraSmoothingSpeed);
						}
					}
					
					Vector2 playerDelta = playerPos - _playerPos;
					if (playerDelta.Length() > 100f)
					{
						_cameraSmoothingSpeed = 15f;
					}
					else
					{
						_cameraSmoothingSpeed = 8f;
					}
					
					camera.Target = _smoothedCameraTarget;
				}
			}
			ApplyPreciseCameraTransform(ref camera);
			
			_localPlayers[0].Position = playerPos;
			_localPlayers[0].Facing = facing;
			_localPlayers[0].AnimState = _animState;
			_localPlayers[0].AnimFrame = _animFrame;
			_localPlayers[0].AnimProg = _animProg;
			
			float currentTime = (float)Raylib.GetTime();
			//  DEBUG F3 : chargement/génération des chunks proches du joueur. Suspect n°1 pour
			// les pics de frame en dézoom ou en déplacement rapide (LoadOrGenerateChunk peut
			// tourner en plein milieu d'une frame). Voir PerfStats.cs.
			PerfStats.StartSection(PerfStats.Section.ChunkStreaming);
			World.UpdateActiveChunks(playerPos, currentTime, entities);
			PerfStats.EndSection(PerfStats.Section.ChunkStreaming);
			int heldItemId = (heldItem != null) ? GetItemId(heldItem.Name) : 0;
			
			UpdateArmorCompanions(dt);
			UpdateTamedAnimalTargets(playerPos);
			UpdateNpcOwnedPetTargets();
			UpdateOwnedPetHostility();
			
			// Mise à jour des entités
			//  DEBUG F3 : mesure le temps passé dans la boucle IA de toutes les entités,
			// pour distinguer un ralentissement dû à l'IA de celui dû au rendu (voir aussi
			// World.DrawWorld ci-dessous). Voir PerfStats.cs.
			PerfStats.BeginFrame(entities.Count);
			PerfStats.StartSection(PerfStats.Section.EntityUpdate);
			List<(int ConnectionId, Vector2 Pos)>? connectedPlayerPositions =
				NetworkManager.IsHost ? NetworkManager.GetConnectedPlayerPositions() : null;
			for (int i = entities.Count - 1; i >= 0; i--)
			{
				var e = entities[i];
				if (!e.IsAlive)
				{
					if (e.IsBoss && string.Equals(e.Species, "Khamsin", StringComparison.OrdinalIgnoreCase))
					{
						GiveItemToPlayer(193, 1, e.WorldPos);
						AddNotification(new Notification(" Coeur de Khamsin récupéré !", new Color(255, 120, 120, 255), 3f));
					}
					if (e.IsBoss && string.Equals(e.Species, "Genie", StringComparison.OrdinalIgnoreCase))
					{
						GiveItemToPlayer(273, 3, e.WorldPos);
						//  Loot garanti : une pierre runique Solaire OU Givrée (tirage aléatoire),
						// les deux runes de puissance du Génie de la Lampe.
						var genieRuneType = Random.Shared.Next(2) == 0 ? RuneType.Solar : RuneType.Frost;
						var genieRune = new RuneData { Type = genieRuneType, Value = 15f };
						DropCustomItemOnGround(e.WorldPos, 250, 1, null, null, 0f, genieRune.ToMetadata());
						Entity.SpawnGenieSmoke(e.WorldPos, 2f);
						Program.ApplyScreenShake(6f, 0.3f);
						string genieRuneLabel = genieRuneType == RuneType.Solar ? "Solaire" : "Givrée";
						AddNotification(new Notification($" Le Génie de la Lampe se dissout dans un nuage doré... 3x Coeur du Génie + 1x Pierre runique {genieRuneLabel} récupérés !", new Color(255, 210, 90, 255), 4f));
					}
					int tileX = (int)(e.WorldPos.X / TileSize);
					int tileY = (int)(e.WorldPos.Y / TileSize);
					int height = World.GetHeightAt(tileX, tileY);
					float yOffset = -height * TileSize / 4;
					Vector2 entityVisualPos = new Vector2(e.WorldPos.X, e.WorldPos.Y + yOffset);
					SpawnDeathParticles(entityVisualPos, e.Tint);
					if (TraderUI.IsOpen && TraderUI.GetCurrentTrader() == e) TraderUI.Close();
					//  MULTIJOUEUR : informer les clients de la mort de cette entité
					NetworkManager.BroadcastEntityDeath(e.NetId);
					entities.RemoveAt(i);
					continue;
				}
				int chunkX = (int)Math.Floor(e.WorldPos.X / (World.CHUNK_SIZE * TileSize));
				int chunkY = (int)Math.Floor(e.WorldPos.Y / (World.CHUNK_SIZE * TileSize));
				//  MULTIJOUEUR : les clients ne simulent pas les entités (le host est seul
				// authoritative). Côté client, les entités sont rendues depuis le snapshot reçu.
				if (!NetworkManager.IsClient && World.IsChunkLoaded(chunkX, chunkY))
				{
					e.UpdateNaturalHealing(dt);

					//  MULTIJOUEUR : créature portée par un client - elle suit sa position au
					// lieu de subir sa simulation IA habituelle (pas d'attaque, pas de fuite, on
					// se contente de la faire suivre le porteur, comme _carriedCreature côté host).
					if (e.CarriedByConnectionId != -1)
					{
						foreach (var (connId, pos) in connectedPlayerPositions ?? NetworkManager.GetConnectedPlayerPositions())
						{
							if (connId == e.CarriedByConnectionId)
							{
								e.WorldPos = pos;
								break;
							}
						}
						continue;
					}

					//  MULTIJOUEUR : une entité doit pouvoir cibler n'importe quel joueur présent
					// (le host ou un client connecté), pas uniquement le joueur local du host -
					// sinon les entités ignorent complètement les clients (jamais agressées,
					// jamais fuies, jamais combattues).
					Vector2 targetPos = _playerPos;
					int targetConnectionId = -1;
					int targetHeldItemId = heldItemId;
					float bestDistSq = Vector2.DistanceSquared(e.WorldPos, _playerPos);
					if (NetworkManager.IsHost)
					{
						foreach (var (connId, pos) in connectedPlayerPositions!)
						{
							float distSq = Vector2.DistanceSquared(e.WorldPos, pos);
							if (distSq < bestDistSq)
							{
								bestDistSq = distSq;
								targetPos = pos;
								targetConnectionId = connId;
								targetHeldItemId = NetworkManager.GetConnectedPlayerHeldItemId(connId);
							}
						}
					}
					e.AiTargetConnectionId = targetConnectionId;
					e.UpdateBloodDrips(dt, e.CurrentHP, e.MaxHP);
					e.UpdateSlimeGemIncubation(dt);

					//  Limite volontaire du coût IA : on met à jour tout le moteur de pathfinding/
					// gaze/choix d'objectif seulement pour les PNJ proches du joueur ou déjà en
					// danger. Les PNJ lointains et calmes sont mis à jour moins souvent, ce qui coupe
					// drastiquement les pics de lag dès qu'il y a des dizaines d'habitants en village.
					if (!e.ShouldRunFullAiUpdate(targetPos, i))
						continue;

					PerfStats.NotifyFullAiUpdate();
					e.Update(dt, destroyedObjects, targetPos, targetHeldItemId);
				}
			}
			PerfStats.EndSection(PerfStats.Section.EntityUpdate);
			if (!NetworkManager.IsClient)
				World.ResolveEntityPushes(dt);
			//  SYSTÈME DE QUÊTES : génération aléatoire d'idées de quêtes chez les PNJ
			if (!NetworkManager.IsClient)
			{
				QuestManager.Update(dt, entities);
				GuildRecruitManager.Update(dt, entities);
			}
			CurrentBoss = entities.FirstOrDefault(e => e.IsBoss && e.IsAlive);

			// Mise à jour du combat de boss de l'oeil (intro, vagues, victoire)
			UpdateEyeBossFight(dt);

			// Gérer suppression différée de la flûte après la lecture du son
			if (_pendingFluteRemovals.Count > 0)
			{
				float now = (float)Raylib.GetTime();
				for (int i = _pendingFluteRemovals.Count - 1; i >= 0; i--)
				{
					if (now >= _pendingFluteRemovals[i])
					{
						// Retirer 1 flute de l'inventaire si présente
						if (RemoveItemFromInventoryById(9000, 1))
							AddNotification(new Notification("La flûte s'est brisée.", new Color(200, 150, 100, 255), 2f));
						_pendingFluteRemovals.RemoveAt(i);
					}
				}
			}
			
			foreach (var car in Cars)
			{
				car.Update(dt, destroyedObjects);
			}
			
			// Mise à jour des effets divers
			foreach (var hp in objectHPs.Values) if (hp.DamageFlash > 0) hp.DamageFlash -= dt;
			for (int i = _floatingDamages.Count - 1; i >= 0; i--)
			{
				_floatingDamages[i].Update(dt);
				if (!_floatingDamages[i].IsAlive) _floatingDamages.RemoveAt(i);
			}
			for (int i = _particles.Count - 1; i >= 0; i--)
			{
				_particles[i].Update(dt);
				if (!_particles[i].IsAlive) _particles.RemoveAt(i);
			}
			
			// Dans UpdateGameplay, la partie qui ramasse les GroundItems
			if (NetworkManager.IsClient)
			{
				if (IsPlayerRagdolled)
				{
					_pendingNetworkPickups.Clear();
				}
				else
				{
					//  MULTIJOUEUR : en tant que client, les objets au sol affichés viennent
					// du host (snapshot périodique). Le ramassage se fait via une requête
					// réseau ; c'est le host qui retire l'objet et nous attribue l'item.
					var visibleIds = new HashSet<string>();
					foreach (var ngi in NetworkManager.GetRemoteGroundItems())
					{
						visibleIds.Add(ngi.NetId);
						float dist = Vector2.Distance(_playerPos, new Vector2(ngi.PosX, ngi.PosY));
						if (dist < 40f && !_pendingNetworkPickups.Contains(ngi.NetId))
						{
							_pendingNetworkPickups.Add(ngi.NetId);
							NetworkManager.RequestPickup(ngi.NetId);
						}
					}
					_pendingNetworkPickups.IntersectWith(visibleIds);
				}
			}
			else
			{
				for (int i = GroundItems.Count - 1; i >= 0; i--)
				{
					var gi = GroundItems[i];
					if (IsPlayerRagdolled)
					{
						gi.Update(dt, _playerPos, false);
						continue;
					}
					//  L'item n'est attiré que s'il peut effectivement trouver une place
					// (stack existant ou slot vide) dans l'un des stockages du joueur
					// (ceinture, sac à dos, panier équipé...).
					float relevantRadius = MathF.Max(gi.PickupRadius, 12f);
					float reachableRadius = relevantRadius + MathF.Abs(dt) * gi.GroundVelocity.Length();
					bool canAttract = gi.TimeSinceDrop + dt > gi.PickupCooldown
						&& Vector2.DistanceSquared(gi.Position, _playerPos) < reachableRadius * reachableRadius
						&& HasSpaceForItem(gi.ItemId);
					gi.Update(dt, _playerPos, canAttract);
					if (canAttract && gi.CanPickup(_playerPos))
					{
						if (!string.IsNullOrWhiteSpace(gi.LootbagPayload))
						{
							RestoreLootbagPayload(gi.LootbagPayload);
							GroundItems.RemoveAt(i);
							SpawnPickupParticles(gi.Position);
						}
						else if (GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData))
						{
							//  Créer l'item avec TOUTES les couleurs personnalisées
							var item = new Item(itemData.Name, gi.Count, itemData.Color, itemData.Icon);
							
							// Restaurer les couleurs multiples
							if (gi.CustomColors != null && gi.CustomColors.Count > 0)
							{
								item.CustomColors = new List<Color?>(gi.CustomColors);
							}

							//  Restaurer le contenu d'instance préservé par GroundItem (seau
							// rempli, gourde chargée, runes serties encodées, etc.) — sans ça
							// l'item redevenait "neuf" après avoir touché le sol.
									item.RestoreMetadataFromSave(gi.Metadata ?? "");
									if (gi.Meta != null && gi.Meta.Count > 0)
										foreach (var kv in gi.Meta)
											item.Meta[kv.Key] = kv.Value;
							item.Container = gi.Container;
							item.Backpack = gi.Backpack;

							int remaining = AddItemToInventory(item, item.Count);
							if (remaining <= 0)
							{
								GroundItems.RemoveAt(i);
								SpawnPickupParticles(gi.Position);
							}
							else
							{
								gi.Position += new Vector2(25, 0);
								gi.TimeSinceDrop = 0f;
							}
						}
					}
					else if (!gi.IsAlive)
					{
						GroundItems.RemoveAt(i);
					}
				}
			}
            
			//  PLAQUES DE PRESSION : contrairement au reste de la grille électrique (throttled à
			// _energyUpdateInterval = 0.5s pour les performances), on échantillonne leur présence
			// À CHAQUE FRAME. Sans ça, un passage rapide entre deux tics de simulation pouvait être
			// totalement raté (le joueur a le temps de monter et redescendre de la plaque entre deux
			// évaluations). Le coût ajouté est négligeable : O(joueurs + entités) pour construire
			// l'ensemble des tuiles occupées, puis un simple lookup O(1) par plaque existante — donc
			// aucun rapport avec le coût, bien plus lourd, de la simulation électrique elle-même.
			if (!NetworkManager.IsClient)
			{
				UpdatePressurePlateSensors();
			}

            //  MISE À JOUR DES SYSTÈMES D'ÉNERGIE (machine à vapeur, dynamo, batteries)
            //  MULTIJOUEUR : seul le host (ou le mode solo) simule la grille électrique.
            // Un client qui la simulerait aussi de son côté finirait par diverger (pression,
            // charge des batteries...) ; il reçoit à la place un snapshot périodique du host
            // (NetworkManager.ApplyEnergySnapshot) qui écrase son état local.
            if (!NetworkManager.IsClient)
            {
                _energyUpdateTimer += dt;
                if (_energyUpdateTimer >= _energyUpdateInterval)
                {
                    _energyUpdateTimer = 0f;
                    UpdateEnergySystems();
                }
            }
			// Mise à jour des projectiles
			for (int i = _arrows.Count - 1; i >= 0; i--)
			{
				_arrows[i].Update(dt);
				if (!_arrows[i].IsAlive)
				{
					_arrows.RemoveAt(i);
				}
			}
			for (int i = _bullets.Count - 1; i >= 0; i--)
			{
				_bullets[i].Update(dt);
				if (!_bullets[i].IsAlive)
				{
					_bullets.RemoveAt(i);
				}
			}
			
			// Mise à jour des projectiles magiques
			for (int i = _spellProjectiles.Count - 1; i >= 0; i--)
			{
				var sp = _spellProjectiles[i];
				sp.Update(dt, entities);
				if (!sp.IsAlive)
					_spellProjectiles.RemoveAt(i);
			}
			
			for (int i = _throwables.Count - 1; i >= 0; i--)
			{
				var throwable = _throwables[i];
				throwable.Update(dt);
				if (!throwable.IsAlive)
				{
					_throwables.RemoveAt(i);
				}
			}
			
			// Particules autour du joueur
			World.UpdateTileParticles(playerPos, _particles, dt);
			World.UpdatePlayerIndoorStatus(playerPos);
			World.UpdateBoats(dt, destroyedObjects, entities, ref _playerPos);
			
			// Gestion des cooldowns d'attaque
			if (attackCooldown > 0f) attackCooldown -= dt;
			if (attackAnim > 0f) attackAnim -= dt;
			else isAttacking = false;
			if (_attackSwingProgress > 0f && _attackSwingProgress < 1f)
			{
				_attackSwingProgress += dt * _attackSwingSpeed;
				if (_attackSwingProgress >= 1f) _attackSwingProgress = 0f;
			}
			else if (_attackSwingProgress >= 1f) _attackSwingProgress = 0f;
			if (_swingTimer > 0) _swingTimer -= dt;
			
			// Sauvegarde automatique
			_autoSaveTimer += dt;
			if (_autoSaveTimer >= _autoSaveInterval)
			{
				_autoSaveTimer = 0f;
				SaveCurrentGame();
				ShowSavingIcon(); //  Au lieu de la notification
			}

			// Mise à jour du timer d'affichage de l'icône de sauvegarde
			if (_savingDisplayTimer > 0f)
			{
				_savingDisplayTimer -= dt;
			}
			_totalPlayTime += dt;

			// Appliquer une avance temporelle progressive si demandée (évite gel)
			if (_timeSkipRemaining > 0f)
			{
				float advance = Math.Min(_timeSkipRemaining, MAX_TIME_ADVANCE_PER_FRAME);
				_timeSkipRemaining -= advance;
				_gameTime += advance;
			}

			_gameTime += dt;
			UpdateTikiTotems(dt, playerPos);
			_saveTimer += dt;
			if (_saveTimer >= _saveInterval) { _saveTimer = 0f; SaveAllChunksEntities(); }
			
			UpdateSurvivalStats(dt);
			PerfStats.EndSection(PerfStats.Section.GameplayUpdateTotal);
		}

		private static void UpdateEnergySystems()
		{
			float dt = _energyUpdateInterval; // Utiliser l'intervalle comme delta pour la cohérence
			
			// ========== 1. MACHINES À VAPEUR ==========
			//  OPTIM : on itère directement le dictionnaire (pas de .ToList()) et on ne
			// supprime les entrées qu'APRÈS la boucle, dans _energyRemovalBuffer, pour éviter
			// l'exception "collection modified" tout en évitant une allocation par frame.
			_energyRemovalBuffer.Clear();
			foreach (var kv in World.SteamEngines)
			{
				var (x, y) = kv.Key;
				var engine = kv.Value;
				
				// Si le chunk n'est pas chargé, on ne touche à rien (évite de perdre les données
				// d'une machine simplement hors de portée) ; sinon on nettoie si elle a disparu.
				if (!World.IsTileChunkLoaded(x, y)) continue;
				if (World.GetObjectIdAt(x, y) != 401)
				{
					_energyRemovalBuffer.Add((x, y));
					continue;
				}
				engine.Water = Math.Clamp(engine.Water, 0f, SteamEngineData.MaxWater);
				engine.Coal = Math.Clamp(engine.Coal, 0f, SteamEngineData.MaxCoal);
				
				// MISE À JOUR CONTINUE : Consommation et production
				if (engine.IsOn && engine.Water > 0.01f && engine.Coal > 0.01f)
				{
					float waterConsumption = Math.Min(SteamEngineData.WaterConsumptionPerSecond * dt, engine.Water);
					float coalConsumption = Math.Min(SteamEngineData.CoalConsumptionPerSecond * dt, engine.Coal);
					
					engine.Water -= waterConsumption;
					engine.Coal -= coalConsumption;
					
					// Augmenter la pression (plus rapide)
					engine.SteamPressure = Math.Min(100f, engine.SteamPressure + 25f * dt);
					engine.ProductionRate = 50f * (engine.SteamPressure / 100f);
				}
				else
				{
					// Redescendre la pression (plus lent pour éviter les oscillations)
					engine.SteamPressure = Math.Max(0f, engine.SteamPressure - 5f * dt);
					engine.ProductionRate = 0f;
				}
				
				engine.LastUpdateTime = (float)Raylib.GetTime();
			}
			foreach (var key in _energyRemovalBuffer) World.SteamEngines.Remove(key);

			// ========== 2. DYNAMOS (convertissent la vapeur en électricité) ==========
			_energyRemovalBuffer.Clear();
			foreach (var kv in World.Dynamos)
			{
				var (x, y) = kv.Key;
				var dynamo = kv.Value;
				
				if (!World.IsTileChunkLoaded(x, y)) continue;
				if (World.GetObjectIdAt(x, y) != 403)
				{
					_energyRemovalBuffer.Add((x, y));
					continue;
				}
				
				//  RECHERCHE DIRECTE DANS LE CHUNK (sans dépendre du dictionnaire)
				float steamPower = 0f;
				
				foreach (var (dx, dy) in new[] { (1,0), (-1,0), (0,1), (0,-1) })
				{
					int nx = x + dx, ny = y + dy;
					int objectId = World.GetObjectIdAt(nx, ny);
					
					if (objectId == 401) // Machine à vapeur
					{
						// S'assurer que les données existent
						if (!World.SteamEngines.ContainsKey((nx, ny)))
							World.SteamEngines[(nx, ny)] = new SteamEngineData();
						
						var engine = World.SteamEngines[(nx, ny)];

						if (engine.IsOn && engine.SteamPressure > 5f)
						{
							// Calculer la puissance directement à partir de la pression
							steamPower = 50f * (engine.SteamPressure / 100f);
							break;
						}
					}
				}
				
				dynamo.SteamInput = steamPower;
				dynamo.ElectricityOutput = steamPower * 0.9f;
				dynamo.LastUpdateTime = (float)Raylib.GetTime();
				
				// Distribuer l'électricité aux ampoules connectées directement
				//  OPTIM : plus de .ToList() ici, Connections n'est pas modifiée pendant cette boucle
				foreach (var conn in dynamo.Connections)
				{
					//  CHARGER LES AMPOULES CONNECTÉES
					if (World.Ampoules.TryGetValue((conn.X2, conn.Y2), out var ampoule))
					{
						ampoule.IsOn = dynamo.ElectricityOutput > 0.01f;
						if (ampoule.IsOn && !ampoule.WasLitLastFrame)
						{
							ampoule.WasLitLastFrame = true;
						}
					}
					
					// Également charger les batteries
					if (World.Batteries.TryGetValue((conn.X2, conn.Y2), out var battery))
					{
						float charge = Math.Min(dynamo.ElectricityOutput * dt, battery.MaxCapacity - battery.StoredEnergy);
						if (charge > 0.01f)
						{
							battery.StoredEnergy += charge;
						}
					}
				}
			}
			foreach (var key in _energyRemovalBuffer) World.Dynamos.Remove(key);

			// ========== 3. CHARGEURS DE BATTERIE ==========
			_energyRemovalBuffer.Clear();
			foreach (var kv in World.BatteryChargers)
			{
				int x = kv.Key.x;
				int y = kv.Key.y;
				var charger = kv.Value;
				
				if (!World.IsTileChunkLoaded(x, y)) continue;
				if (World.GetObjectIdAt(x, y) != 405)
				{
					_energyRemovalBuffer.Add((x, y));
					continue;
				}

				//  Le socle ne charge la batterie que s'il reçoit réellement du courant par câble
				// (une dynamo qui produit, ou une batterie chargée).
				bool chargerHasPower = false;

				foreach (var dynamoKv in World.Dynamos)
				{
					var dynamo = dynamoKv.Value;
					if (dynamo.Connections.Any(c => c.X2 == x && c.Y2 == y) && dynamo.ElectricityOutput > 0.01f)
					{
						chargerHasPower = true;
						break;
					}
				}

				if (!chargerHasPower)
				{
					foreach (var batteryKv in World.Batteries)
					{
						var sourceBattery = batteryKv.Value;
						if (sourceBattery.Connections.Any(c => c.X2 == x && c.Y2 == y) && sourceBattery.StoredEnergy > 0.01f)
						{
							float consumption = Math.Min(1f * dt, sourceBattery.StoredEnergy);
							sourceBattery.StoredEnergy -= consumption;
							chargerHasPower = consumption > 0.01f;
							break;
						}
					}
				}

				if (!chargerHasPower)
				{
					foreach (var gateKv in World.LogicGates)
					{
						var sourceGate = gateKv.Value;
						if (sourceGate.OutputState && sourceGate.Connections.Any(c => c.X2 == x && c.Y2 == y))
						{
							chargerHasPower = true;
							break;
						}
					}
				}

				charger.HasPower = chargerHasPower;

				// Recharger la batterie insérée uniquement si le socle est alimenté
				if (charger.InsertedBattery != null && charger.IsOn && chargerHasPower)
				{
					float charge = Math.Min(50f * dt, charger.InsertedBattery.MaxCapacity - charger.InsertedBattery.StoredEnergy);
					charger.InsertedBattery.StoredEnergy += charge;
				}
				
				charger.LastUpdateTime = (float)Raylib.GetTime();
			}
			foreach (var key in _energyRemovalBuffer) World.BatteryChargers.Remove(key);
			
			// ========== 4. AMPOULES ÉLECTRIQUES (reçoivent l'énergie des dynamos ou batteries) ==========
			_energyRemovalBuffer.Clear();
			foreach (var kv in World.Ampoules)
			{
				int x = kv.Key.x;
				int y = kv.Key.y;
				var ampoule = kv.Value;
				
				if (!World.IsTileChunkLoaded(x, y)) continue;
				if (World.GetObjectIdAt(x, y) != 408)
				{
					_energyRemovalBuffer.Add((x, y));
					continue;
				}
				
				bool hasPower = false;
				
				// 1. Vérifier si une dynamo est connectée directement à cette ampoule
				foreach (var dynamoKv in World.Dynamos)
				{
					var dynamo = dynamoKv.Value;
					foreach (var conn in dynamo.Connections)
					{
						if ((conn.X2 == x && conn.Y2 == y) || (conn.X1 == x && conn.Y1 == y))
						{
							if (dynamo.ElectricityOutput > 0.01f)
							{
								hasPower = true;
								break;
							}
						}
					}
					if (hasPower) break;
				}
				
				// 2. Sinon, vérifier si une batterie connectée a de l'énergie
				if (!hasPower)
				{
					foreach (var batteryKv in World.Batteries)
					{
						var battery = batteryKv.Value;
						foreach (var conn in battery.Connections)
						{
							if ((conn.X2 == x && conn.Y2 == y) || (conn.X1 == x && conn.Y1 == y))
							{
								if (battery.StoredEnergy > 0.01f)
								{
									// Consommer un peu d'énergie de la batterie
									float consumption = Math.Min(1f * dt, battery.StoredEnergy);
									battery.StoredEnergy -= consumption;
									hasPower = consumption > 0.01f;
									break;
								}
							}
						}
						if (hasPower) break;
					}
				}
				
				// 3. Sinon, vérifier si une porte logique connectée est active
				if (!hasPower)
				{
					foreach (var gateKv in World.LogicGates)
					{
						var sourceGate = gateKv.Value;
						if (sourceGate.OutputState && sourceGate.Connections.Any(c => c.X2 == x && c.Y2 == y))
						{
							hasPower = true;
							break;
						}
					}
				}

				// Mettre à jour l'état de l'ampoule
				bool wasLit = ampoule.IsOn;
				ampoule.IsOn = hasPower;
				
				if (ampoule.IsOn && !wasLit)
				{
					Program.ApplyScreenShake(0.5f, 0.1f);
				}
				else if (!ampoule.IsOn && wasLit)
				{
				}
				
				ampoule.LastUpdateTime = (float)Raylib.GetTime();
			}
			foreach (var key in _energyRemovalBuffer) World.Ampoules.Remove(key);

			// ========== 5. PORTES LOGIQUES ET INTERRUPTEURS ==========
			//  Le câblage du jeu relie des tuiles entre elles (pas de "port A / port B" distincts),
			// donc chaque porte évalue simplement combien de fils elle a en entrée et combien
			// d'entre eux transportent actuellement du courant (World.CountIncomingSignals).
			_energyRemovalBuffer.Clear();
			foreach (var kv in World.LogicGates)
			{
				int x = kv.Key.x;
				int y = kv.Key.y;
				var gate = kv.Value;

				if (!World.IsTileChunkLoaded(x, y)) continue;
				int currentObjectId = World.GetObjectIdAt(x, y);
				if (World.GetGateTypeForObjectId(currentObjectId) == null)
				{
					_energyRemovalBuffer.Add((x, y));
					continue;
				}

				var (connected, active) = World.CountIncomingSignals(x, y);
				gate.ConnectedInputs = connected;
				gate.ActiveInputs = active;

				bool newOutput;
				if (gate.Type != GateType.SWITCH && connected == 0)
				{
					//  CORRECTIF : une porte sans AUCUN fil entrant ne doit jamais pouvoir sortir
					// "true". Sans ce garde-fou, NOT/NAND/NOR sortent "true" par défaut quand
					// rien n'est branché (leur formule logique retombe sur vrai en l'absence
					// d'entrée), ce qui permet de créer de l'énergie à partir de rien en posant
					// juste une porte reliée à une lampe. Un interrupteur (SWITCH) est exempté
					// car c'est une source manuelle assumée, comme une batterie déjà chargée.
					// La plaque de pression et le bloc de délai NE SONT PAS exemptés : ce sont
					// de simples RELAIS (ils laissent passer ou retardent le courant d'une
					// alimentation câblée en entrée) — sans câble d'entrée, il n'y a rien à
					// transmettre, peu importe qu'on marche sur la plaque ou combien de temps on attend.
					newOutput = false;
				}
				else
				{
					switch (gate.Type)
					{
						case GateType.SWITCH:
							// Source manuelle : sa sortie dépend uniquement de son état ON/OFF (bascule avec E)
							newOutput = gate.ManualState;
							break;
						case GateType.PRESSURE_PLATE:
							//  RÉALISME (pas de comportement "Minecraft") : la plaque ne génère pas de
							// courant, elle se contente de fermer/ouvrir mécaniquement le circuit entre
							// son entrée (câblée à une batterie/alimentation) et sa sortie. Le courant ne
							// passe QUE si (a) au moins une entrée câblée est actuellement alimentée ET
							// (b) un poids a pressé physiquement la plaque à un moment ou un autre depuis
							// le dernier tic (gate.PendingPress, rempli à CHAQUE FRAME par
							// UpdatePressurePlateSensors — voir plus haut — pour ne rater aucun passage
							// plus rapide que l'intervalle de simulation électrique) OU la presse encore
							// à l'instant présent. Aucune mémoire au-delà de ce tic : dès que plus
							// personne n'est dessus, le circuit se rouvre au tic suivant, comme un vrai
							// interrupteur à pression (pas de bascule ON/OFF persistante).
							bool pressedSinceLastTick = gate.PendingPress || World.IsWeightOnTile(x, y);
							newOutput = active >= 1 && pressedSinceLastTick;
							gate.ManualState = pressedSinceLastTick; // mémoire d'affichage (plaque enfoncée)
							gate.PendingPress = false; // consommé : prêt pour la fenêtre suivante
							break;
						case GateType.DELAY:
						{
							//  Bloc de délai : retransmet l'état de son entrée en sortie après
							// gate.DelaySeconds (réglable avec Maj+E). File d'attente FIFO
							// d'horodatages pour ne perdre AUCUN front, même plus court que le délai
							// lui-même : chaque changement d'état brut de l'entrée est empilé avec
							// l'instant où il devra être appliqué, puis dépilé (dans l'ordre) dès que
							// cet instant est atteint.
							bool rawInput = active >= 1;
							if (rawInput != gate.DelayLastRawInput)
							{
								gate.DelayLastRawInput = rawInput;
								gate.DelayQueue.Add(((float)Raylib.GetTime() + gate.DelaySeconds, rawInput));
							}
							float now = (float)Raylib.GetTime();
							while (gate.DelayQueue.Count > 0 && gate.DelayQueue[0].ReadyAt <= now)
							{
								gate.ManualState = gate.DelayQueue[0].State; // mémoire = dernière valeur retardée appliquée
								gate.DelayQueue.RemoveAt(0);
							}
							newOutput = gate.ManualState;
							break;
						}
						case GateType.NOT:
							// Inverseur : nécessite une entrée réellement câblée (garantie ci-dessus).
							// Sortie = inverse de l'état de cette entrée.
							newOutput = active == 0;
							break;
						case GateType.AND:
							newOutput = connected >= 2 && active == connected;
							break;
						case GateType.OR:
							newOutput = active >= 1;
							break;
						case GateType.NAND:
							newOutput = !(connected >= 2 && active == connected);
							break;
						case GateType.NOR:
							newOutput = !(active >= 1);
							break;
						case GateType.XOR:
							newOutput = (active % 2) == 1;
							break;
						default:
							newOutput = false;
							break;
					}
				}

				gate.OutputState = newOutput;
				gate.LastUpdateTime = (float)Raylib.GetTime();

				// Distribuer la sortie aux tuiles connectées (ampoules, batteries, chargeurs, autres portes)
				foreach (var conn in gate.Connections)
				{
					if (World.Ampoules.TryGetValue((conn.X2, conn.Y2), out var ampoule))
					{
						// L'état final de l'ampoule est de toute façon recalculé section 4 au tick
						// suivant ; ceci ne fait qu'accélérer l'allumage visuel dès ce tick-ci.
						ampoule.IsOn = ampoule.IsOn || gate.OutputState;
					}
					if (gate.OutputState && World.Batteries.TryGetValue((conn.X2, conn.Y2), out var battery))
					{
						float charge = Math.Min(10f * dt, battery.MaxCapacity - battery.StoredEnergy);
						if (charge > 0.01f) battery.StoredEnergy += charge;
					}
				}
			}
			foreach (var key in _energyRemovalBuffer) World.LogicGates.Remove(key);

			// ========== COLLECTEURS DE PLUIE ==========
			_energyRemovalBuffer.Clear();
			foreach (var kv in World.RainCollectors)
			{
				int x = kv.Key.x;
				int y = kv.Key.y;
				var rc = kv.Value;

				// Si la tuile n'est plus un collecteur, on nettoie (uniquement si le chunk est chargé)
				if (!World.IsTileChunkLoaded(x, y)) continue;
				if (World.GetObjectIdAt(x, y) != 116)
				{
					_energyRemovalBuffer.Add((x, y));
					continue;
				}

				// Remplissage si pluie et pas de toit
				if (Weather.Current is WeatherType.Rain or WeatherType.Storm && World.GetOverlayAt(x, y) == 0)
				{
					rc.CurrentWater = Math.Min(rc.Capacity, rc.CurrentWater + rc.FillRate * dt);
				}

				rc.LastUpdateTime = (float)Raylib.GetTime();
			}
			foreach (var key in _energyRemovalBuffer) World.RainCollectors.Remove(key);
		}

		private static bool IsMouseOnAnyOpenUIAtPosition(Vector2 mousePos)
		{
			if (QuestDialogUI.IsOpen)
				return true;
			
			// Vérifier le conteneur (coffre) - PRIORITÉ 1
			if (ContainerUI.IsOpen)
			{
				Rectangle containerRect = new Rectangle(
					ContainerUI.GetWindowX(),
					ContainerUI.GetWindowY(),
					ContainerUI.GetWindowWidth(),
					ContainerUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, containerRect))
					return true;
			}
			
			// Vérifier l'interface du chaudron
			if (CauldronUI.IsOpen)
			{
				Rectangle cauldronRect = new Rectangle(
					CauldronUI.GetWindowX(),
					CauldronUI.GetWindowY(),
					CauldronUI.GetWindowWidth(),
					CauldronUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, cauldronRect))
					return true;
			}
			
			// Vérifier l'inventaire
			if (InventoryRenderer.IsInventoryOpen)
			{
				Rectangle invRect = new Rectangle(
					InventoryRenderer.GetWindowX(), 
					InventoryRenderer.GetWindowY(), 
					InventoryRenderer.GetWindowWidth(), 
					InventoryRenderer.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, invRect))
					return true;
			}
			
			// Vérifier l'interface du menu des items (Godmode)
			if (ItemMenuUI.IsOpen)
			{
				Rectangle itemMenuRect = new Rectangle(
					ItemMenuUI.GetWindowX(),
					ItemMenuUI.GetWindowY(),
					ItemMenuUI.GetWindowWidth(),
					ItemMenuUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, itemMenuRect))
					return true;
			}
			
			// Vérifier l'interface d'artisanat
			if (CraftingUI.IsOpen)
			{
				Rectangle craftRect = new Rectangle(
					CraftingUI.GetWindowX(),
					CraftingUI.GetWindowY(),
					CraftingUI.GetWindowWidth(),
					CraftingUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, craftRect))
					return true;
			}
			
			if (RitualCircleUI.IsMouseOverButton(mousePos))
				return true;
			
			if (PortalUI.IsOpen)
			{
				Rectangle ritualRect = new Rectangle(
					PortalUI.GetWindowX(),
					PortalUI.GetWindowY(),
					PortalUI.GetWindowWidth(),
					PortalUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, ritualRect))
					return true;
			}
			
			// Vérifier l'interface du porte-armure
			if (ArmorStandUI.IsOpen)
			{
				Rectangle armorRect = new Rectangle(
					ArmorStandUI.GetWindowX(),
					ArmorStandUI.GetWindowY(),
					ArmorStandUI.GetWindowWidth(),
					ArmorStandUI.GetWindowHeight());
				if (Raylib.CheckCollisionPointRec(mousePos, armorRect))
					return true;
			}
			
			// Vérifier l'interface de teinte
			if (DyeingUI.IsOpen)
			{
				Rectangle dyeRect = new Rectangle(DyeingUI.GetWindowX(), DyeingUI.GetWindowY(), DyeingUI.GetWindowWidth(), DyeingUI.GetWindowHeight());
				if (Raylib.CheckCollisionPointRec(mousePos, dyeRect)) return true;
			}
			
			// Vérifier l'interface du commerçant
			if (TraderUI.IsOpen)
			{
				Rectangle traderRect = new Rectangle(
					TraderUI.GetWindowX(),
					TraderUI.GetWindowY(),
					TraderUI.GetWindowWidth(),
					TraderUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, traderRect))
					return true;
			}
			
			// Vérifier l'interface du miroir (Personnalisation)
			if (CharacterCustomizationUI.IsOpen)
			{
				Rectangle mirrorRect = new Rectangle(
					CharacterCustomizationUI.GetWindowX(),
					CharacterCustomizationUI.GetWindowY(),
					CharacterCustomizationUI.GetWindowWidth(),
					CharacterCustomizationUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, mirrorRect))
					return true;
			}

			// Vérifier l'interface du menu des instruments
			if (InstrumentUI.IsOpen)
			{
				Rectangle instrumentRect = new Rectangle(
					InstrumentUI.GetWindowX(),
					InstrumentUI.GetWindowY(),
					InstrumentUI.GetWindowWidth(),
					InstrumentUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, instrumentRect))
					return true;
			}
			
			// Vérifier l'interface de l'animal apprivoisé
			if (TamedAnimalUI.IsOpen)
			{
				Rectangle animalRect = new Rectangle(
					TamedAnimalUI.GetWindowX(),
					TamedAnimalUI.GetWindowY(),
					TamedAnimalUI.GetWindowWidth(),
					TamedAnimalUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, animalRect))
					return true;
			}
			
			// Vérifier l'interface de l'animal apprivoisé
			if (QuestJournalUI.IsOpen)
			{
				Rectangle animalRect = new Rectangle(
					QuestJournalUI.GetWindowX(),
					QuestJournalUI.GetWindowY(),
					QuestJournalUI.GetWindowWidth(),
					QuestJournalUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, animalRect))
					return true;
			}
			
			// Vérifier le menu radial de l'animal (si ouvert)
			if (TamedAnimalInteraction.IsOpen)
			{
				// Le menu radial est centré sur l'écran, on vérifie un rayon
				int sw = Raylib.GetScreenWidth();
				int sh = Raylib.GetScreenHeight();
				float centerX = sw / 2f;
				float centerY = sh / 2f;
				float radius = 130f; // Rayon du menu radial + marge
				if (Vector2.DistanceSquared(mousePos, new Vector2(centerX, centerY)) < (radius) * (radius))
					return true;
			}
			
			// Vérifier le système de chat (quand il est ouvert, toute la zone de saisie compte)
			if (ChatSystem.IsOpen)
			{
				int inputWidth = 600;
				int inputX = 10;
				int inputY = Raylib.GetScreenHeight() - 40;
				Rectangle chatRect = new Rectangle(inputX, inputY, inputWidth, 30);
				if (Raylib.CheckCollisionPointRec(mousePos, chatRect))
					return true;
			}
			
			return false;
		}

		private static bool IsMouseOverAnyUI()
		{
			return IsMouseOnAnyOpenUIAtPosition(Raylib.GetMousePosition());
		}

		static void CollectLightSources(Camera2D camera, List<(Vector2, int, Color)> lights, Vector2? playerPosition = null)
		{
			int ts = Program.TileSize;
			Vector2 topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera);
			Vector2 bottomRight = Raylib.GetScreenToWorld2D(new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), camera);
			// Inclure les sources juste hors écran : la propagation peut encore atteindre
			// la zone visible depuis ces tuiles.
			int sourceScanMargin = MAX_LIGHT_LEVEL + 1;
			int startX = (int)Math.Floor(topLeft.X / ts) - sourceScanMargin;
			int startY = (int)Math.Floor(topLeft.Y / ts) - sourceScanMargin;
			int endX = (int)Math.Ceiling(bottomRight.X / ts) + sourceScanMargin;
			int endY = (int)Math.Ceiling(bottomRight.Y / ts) + sourceScanMargin;

			// Si on est sous terre, considérer les sorties de grotte comme des sources
			// de lumière fournissant la luminosité ambiante extérieure (soleil).
			if (World.IsUnderground)
			{
				// Calculer la luminosité ambiante à la surface (indépendante du fait
				// qu'on soit sous terre) à partir du cycle jour/nuit.
				float dayProgress = (_gameTime % DAY_CYCLE_DURATION) / DAY_CYCLE_DURATION;
				float factor = 0.5f + 0.5f * MathF.Cos(2f * MathF.PI * dayProgress);
				float surfaceDarknessAlpha = MIN_DARKNESS + (MAX_DARKNESS - MIN_DARKNESS) * factor;
				float surfaceAmbient = 1f - surfaceDarknessAlpha;

				for (int x = startX; x <= endX; x++)
				{
					for (int y = startY; y <= endY; y++)
					{
						int objId = World.GetObjectIdAt(x, y);
						if (objId != 52) continue; // 52 = sortie de grotte

						int height = World.GetHeightAt(x, y);
						float yOffset = -height * ts / 4;
						float drawX = x * ts;
						float drawY = y * ts + yOffset;
						Vector2 worldPos = new Vector2(drawX + ts / 2, drawY + ts / 2);

						// Rayon et intensité liés à la luminosité extérieure
						int radius = (int)(200 + surfaceAmbient * 1000f); // 200..1200
						byte alpha = (byte) Math.Clamp(120 + surfaceAmbient * 135f, 0f, 255f);
						Color sunLike = new Color((byte)255, (byte)235, (byte)190, alpha);
						lights.Add((worldPos, radius, sunLike));
					}
				}
			}

			// ========== LUMIÈRES SUR LES BATEAUX ==========
			foreach (var boat in World.GetAllBoats().Values)
			{
				foreach (var (relX, relY) in boat.Tiles.Keys)
				{
					int boatObjectId = boat.GetObjectIdAt(relX, relY);
					if (boatObjectId == 0) continue;
					
					var tileData = WorldTileRegistry.GetTile(boatObjectId);
					if (tileData?.IsLightSource != true) continue;
					
					Vector2 tileCenter = boat.GetTileWorldPos(relX, relY);
					Vector2 worldPos = tileCenter;
					
					int radius = tileData.LightRadius > 0 ? tileData.LightRadius : 160;
					Color lightColor;
					if (tileData.LightColor != null && tileData.LightColor.Length >= 3)
						lightColor = new Color(tileData.LightColor[0], tileData.LightColor[1], tileData.LightColor[2], 200);
					else
						lightColor = new Color(255, 200, 100, 200);
					
					lights.Add((worldPos, radius, lightColor));
				}
			}

			// ========== LUMIÈRES SUR LE TERRAIN NORMAL ==========
			for (int x = startX; x <= endX; x++)
			{
				for (int y = startY; y <= endY; y++)
				{
					int objId = World.GetObjectIdAt(x, y);
					if (objId == 0) continue;
					var tileData = WorldTileRegistry.GetTile(objId);
					string wallTorchMeta = World.GetTileMeta(x, y, "wall_torch");
					bool hasWallTorch = int.TryParse(wallTorchMeta, out int wallTorchId)
						&& WorldTileRegistry.GetTile(wallTorchId)?.WallMountable == true;
					if (hasWallTorch) tileData = WorldTileRegistry.GetTile(wallTorchId);
					if (tileData?.IsLightSource != true) continue;
					float lightY = y * ts + ts / 2f;
					if (hasWallTorch)
					{
						var wallData = WorldTileRegistry.GetTile(objId);
						lightY -= ((wallData?.Size?.Height ?? 1) - 1) * ts / 2f;
					}
					Vector2 worldPos = new Vector2(x * ts + ts / 2f, lightY);
					
					int radius = tileData.LightRadius > 0 ? tileData.LightRadius : 160;
					Color lightColor;
					if (tileData.LightColor != null && tileData.LightColor.Length >= 3)
						lightColor = new Color(tileData.LightColor[0], tileData.LightColor[1], tileData.LightColor[2], 200);
					else
						lightColor = new Color(255, 200, 100, 200);
					
					lights.Add((worldPos, radius, lightColor));
				}
			}
			
			// ========== AMPOULES ÉLECTRIQUES ==========
			for (int x = startX; x <= endX; x++)
			{
				for (int y = startY; y <= endY; y++)
				{
					int objId = World.GetObjectIdAt(x, y);
					if (objId == 408) // Ampoule électrique
					{
						// Vérifier si l'ampoule est allumée
						bool isLit = false;
						if (World.Ampoules.TryGetValue((x, y), out var ampoule))
							isLit = ampoule.IsOn;
						
						if (isLit)
						{
							var tileData = WorldTileRegistry.GetTile(408);
							if (tileData != null)
							{
								Vector2 worldPos = new Vector2(x * ts + ts / 2f, y * ts + ts / 2f);
								
								int radius = 350; // Ajustez selon vos besoins
								Color lightColor = new Color(255, 220, 150, 200);
								
								lights.Add((worldPos, radius, lightColor));
							}
						}
					}
				}
			}

			// ========== LUMIÈRE DE LA TORCHE EN MAIN ==========
			Vector2 resolvedPlayerPosition = playerPosition ?? _playerPos;
			Vector2 playerWorld = resolvedPlayerPosition;
			
			Item? held = equipment.MainHand;
			if (held != null)
			{
				int heldId = GetItemId(held.Name);
				if (GameData.ItemDatabase.TryGetValue(heldId, out var itemData) && itemData.IsLightSource)
				{
					int radius = itemData.LightRadius > 0 ? itemData.LightRadius : 160;
					Color color = itemData.LightColor;
					lights.Add((playerWorld, radius, color));
				}
			}

			// ========== LUMIÈRES DE L'ÉQUIPEMENT ==========
			Item?[] equipmentItems = { equipment.Head, equipment.Body, equipment.Legs, equipment.OffHand };
			foreach (var eqItem in equipmentItems)
			{
				if (eqItem == null) continue;
				int eqItemId = GetItemId(eqItem.Name);
				if (GameData.ItemDatabase.TryGetValue(eqItemId, out var eqData) && eqData.IsLightSource)
				{
					int radius = eqData.LightRadius > 0 ? eqData.LightRadius : 100;
					Color color = eqData.LightColor;
					lights.Add((playerWorld, radius, color));
				}
			}
			
			// Lumières des feufollets (Will-o'-Wisps)
			foreach (var entity in GetEntities())
			{
				if (!entity.IsAlive) continue;
				if (entity.IsSpiritAnimal)
				{
					Vector2 visualPos = entity.VisualWorldPos;
					int tileX = (int)(visualPos.X / ts);
					int tileY = (int)(visualPos.Y / ts);
					float yOffset = -World.GetHeightAt(tileX, tileY) * ts / 4f;
					Vector2 drawPosition = new(visualPos.X, visualPos.Y + yOffset - entity.VisualOffsetY);
					Vector2 eyePosition = EntityRenderer.GetSpiritEyeLightPosition(
						entity.Species, entity.AnimState, entity.CurrentFrame, entity.AnimProgress,
						entity.Facing, drawPosition, entity.IsInWater, entity.Scale);
					lights.Add((eyePosition, 160, new Color(130, 190, 255, 255)));
				}

				var info = SpeciesData.GetSpeciesInfo(entity.Species);
				if (info != null && info.IsLightSource)
				{
					Vector2 pos = entity.WorldPos;
					int height = World.GetHeightAt((int)(pos.X / ts), (int)(pos.Y / ts));
					float yOffset = -height * ts / 4;
					Vector2 visualPos = new Vector2(pos.X, pos.Y + yOffset);
					
					int radius = info.LightRadius;
					Color lightColor = info.LightColor;
					lights.Add((visualPos, radius, lightColor));
				}
			}

			if (NetworkManager.IsClient)
			{
				foreach (var spirit in NetworkManager.GetRemoteEntities())
				{
					if (!spirit.IsSpiritAnimal || spirit.HP <= 0)
						continue;

					Vector2 drawPosition = new(spirit.PosX, spirit.PosY);
					Vector2 eyePosition = EntityRenderer.GetSpiritEyeLightPosition(
						spirit.Species, spirit.AnimState, spirit.AnimFrame, spirit.AnimProg,
						spirit.Facing, drawPosition, false, spirit.Scale);
					lights.Add((eyePosition, 160, new Color(130, 190, 255, 255)));
				}
			}
			
			// ========== LUMIÈRES DES PROJECTILES MAGIQUES ==========
			foreach (var sp in _spellProjectiles)
			{
				if (!sp.IsAlive) continue;
				Vector2 pos = sp.Position;
				// Ajuster la hauteur du terrain si besoin, mais la position est déjà monde
				int radius = (int)(sp.Size * 6); // ou 100 par défaut
				Color lightColor = sp.Color;
				// Augmenter l'alpha pour la lumière
				lightColor.A = 200;
				lights.Add((pos, radius, lightColor));
			}
		}

		private static bool IsPottableFlower(int itemId)
		{
			return (GameData.ItemDatabaseByKey.TryGetValue("rose", out var rose) && rose.ID == itemId)
				|| (GameData.ItemDatabaseByKey.TryGetValue("white_flower", out var whiteFlower) && whiteFlower.ID == itemId)
				|| (GameData.ItemDatabaseByKey.TryGetValue("cactus_flower", out var cactusFlower) && cactusFlower.ID == itemId)
				|| (GameData.ItemDatabaseByKey.TryGetValue("sunflower", out var sunflower) && sunflower.ID == itemId);
		}

		private static bool TryInteractWithStation(Vector2 playerPos, Camera2D camera, Rectangle? viewport = null, int playerIndex = 0)
		{
			if (IsMouseOverAnyUI())
				return false;
			
			Vector2 mouseWorld = viewport.HasValue
				? GetViewportMouseWorldPosition(camera, viewport.Value)
				: Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			int ts = Program.TileSize;
			
			(int waterTileX, int waterTileY, float _, int _) = FindTileUnderMouse(mouseWorld, ts);
			if (waterTileX != -1 && waterTileY != -1)
			{
				int groundIdAtTile = World.GetGroundTileIdAt(waterTileX, waterTileY);
				if (groundIdAtTile == 10 && World.GetBoatAtPosition(playerPos, ts) == null)
				{
					Item? heldWaterItem = equipment.MainHand;
					bool isWaterContainer = heldWaterItem != null && GetWaterCapacityForItem(heldWaterItem) > 0;

					//  SEAU (id 63) : contenant à 10 charges (eau OU lait, jamais les deux à la
					// fois), distinct de la gourde/arrosoir qui a son propre compteur de charges.
					if (heldWaterItem != null && GetItemId(heldWaterItem.Name) == 63)
					{
						ParseBucketContent(heldWaterItem.Metadata, out string oceanBucketType, out int oceanBucketCharges);
						float distToWaterBucket = Vector2.Distance(playerPos, new Vector2(waterTileX * ts + ts / 2f, waterTileY * ts + ts / 2f));
						if (distToWaterBucket <= MAX_INTERACTION_DISTANCE)
						{
							if (oceanBucketType == "milk")
							{
								AddNotification(new Notification(" Le seau contient déjà du lait !", new Color(255, 200, 100, 255), 1.5f));
							}
							else if (oceanBucketType == "water" && oceanBucketCharges >= BUCKET_MAX_CHARGES)
							{
								AddNotification(new Notification(" Le seau est déjà plein d'eau !", new Color(255, 200, 100, 255), 1.5f));
							}
							else
							{
								heldWaterItem.Metadata = FormatBucketContent("water", BUCKET_MAX_CHARGES);
								AddNotification(new Notification(" Seau rempli d'eau !", new Color(100, 200, 255, 255), 1.5f));
							}
							return true;
						}
					}

					if (isWaterContainer)
					{
						float distToWater = Vector2.Distance(playerPos, new Vector2(waterTileX * ts + ts / 2f, waterTileY * ts + ts / 2f));
						if (distToWater <= MAX_INTERACTION_DISTANCE)
						{
							int maxWater = GetWaterCapacityForItem(heldWaterItem);
							int currentWater = 0;
							if (!string.IsNullOrEmpty(heldWaterItem.Metadata))
								int.TryParse(heldWaterItem.Metadata, out currentWater);
							
							if (currentWater < maxWater)
							{
								SetLiquidContainerCharges(heldWaterItem, "water", maxWater);
								AddNotification(new Notification(" Contenant rempli !", new Color(100, 200, 255, 255), 1.5f));
								return true;
							}
							else
							{
								AddNotification(new Notification(" Déjà plein(e) !", new Color(255, 200, 100, 255), 1.5f));
								return true;
							}
						}
					}
				}
			}
			
			// ========== INTERACTION AVEC LES BATEAUX (PRIORITÉ ABSOLUE) ==========
			Boat? nearbyBoat = World.GetBoatAtPosition(playerPos, ts);
			if (nearbyBoat != null)
			{   
				int bestRelX = -1, bestRelY = -1;
				int bestBoatObjectId = 0;
				var bestTileData = null as WorldTileData;
				
				foreach (var (relX, relY) in nearbyBoat.Tiles.Keys)
				{
					Vector2 tileWorldPos = nearbyBoat.GetTileWorldPos(relX, relY);
					int boatTileX = (int)(tileWorldPos.X / ts);
					int boatTileY = (int)(tileWorldPos.Y / ts);
					int boatHeight = World.GetHeightAt(boatTileX, boatTileY);
					float boatYOffset = -boatHeight * ts / 4;
					float drawX = tileWorldPos.X - ts/2f;
					float drawY = tileWorldPos.Y - ts/2f + boatYOffset;
					
					int boatObjectId = nearbyBoat.GetObjectIdAt(relX, relY);
					var currentBoatTileData = boatObjectId != 0 ? WorldTileRegistry.GetTile(boatObjectId) : null;
					
					int w = ts, h = ts;
					int offX = 0, offY = 0;
					
					if (currentBoatTileData != null && currentBoatTileData.Size != null)
					{
						w = ts * currentBoatTileData.Size.Width;
						h = ts * currentBoatTileData.Size.Height;
						if (currentBoatTileData.DrawOffset != null)
						{
							offX = currentBoatTileData.DrawOffset.X;
							offY = currentBoatTileData.DrawOffset.Y;
						}
						drawY = drawY - h + ts;
					}
					
					Rectangle tileRect = new Rectangle(drawX + offX, drawY + offY, w, h);
					
					if (Raylib.CheckCollisionPointRec(mouseWorld, tileRect))
					{
						bestRelX = relX;
						bestRelY = relY;
						bestBoatObjectId = boatObjectId;
						bestTileData = currentBoatTileData;
						break;
					}
				}
				
				if (bestRelX != -1 && bestTileData != null)
				{
					// Conteneur sur le bateau
					if (bestTileData.IsContainer == true && nearbyBoat.Containers.TryGetValue((bestRelX, bestRelY), out var boatContainer))
					{
						//  MULTIJOUEUR : coffre de bateau pas encore synchronisé (pas de tileX/tileY
						// fixes puisque le bateau se déplace ; nécessiterait un message réseau dédié
						// identifiant le bateau par son id plutôt que par une tuile).
						ContainerUI.Open(boatContainer, playerIndex);
						AddNotification(new Notification(" Coffre ouvert (bateau)", new Color(100, 200, 255, 255), 1.5f));
						return true;
					}
					
					// Porte-armure sur le bateau
					if (bestTileData.IsArmorStand == true && nearbyBoat.ArmorStands.TryGetValue((bestRelX, bestRelY), out var boatStand))
					{
						ArmorStandUI.Open(boatStand, playerIndex);
						AddNotification(new Notification(" Porte-armure ouvert (bateau)", new Color(200, 180, 100, 255), 1.5f));
						return true;
					}
					
					// Station sur le bateau
					if (bestTileData.IsStation == true)
					{
						string stationType = bestTileData.StationType ?? "";
						if (stationType == "workbench")
						{
							Program.ActiveStation = "workbench";
							CraftingUI.Toggle("workbench", playerIndex);
							AddNotification(new Notification(" Établi ouvert (bateau) !", new Color(100, 200, 255, 255), 1.5f));
						}
						else if (stationType == "furnace")
						{
							Program.ActiveStation = "furnace";
							CraftingUI.Toggle("furnace", playerIndex);
							AddNotification(new Notification(" Fourneau ouvert (bateau) !", new Color(100, 200, 255, 255), 1.5f));
						}
						else if (stationType == "anvil")
						{
							Program.ActiveStation = "anvil";
							CraftingUI.Toggle("anvil", playerIndex);
							AddNotification(new Notification(" Enclume ouvert (bateau) !", new Color(200, 180, 100, 255), 1.5f));
						}
						else if (stationType == "loom")
						{
							Program.ActiveStation = "loom";
							CraftingUI.Toggle("loom", playerIndex);
							AddNotification(new Notification(" Métier à tisser ouvert (bateau) !", new Color(200, 180, 150, 255), 1.5f));
						}
						else if (stationType == "carpentry")
						{
							Program.ActiveStation = "carpentry";
							CraftingUI.Toggle("carpentry", playerIndex);
							AddNotification(new Notification(" Établi de menuisier ouvert (bateau) !", new Color(200, 180, 100, 255), 1.5f));
						}
						else if (stationType == "cauldron")
						{
							CauldronUI.Open(playerIndex);
							AddNotification(new Notification(" Chaudron ouvert (bateau) !", new Color(100, 200, 255, 255), 1.5f));
						}
						else if (stationType == "mirror")
						{
							CharacterCustomizationUI.Open();
							AddNotification(new Notification(" Miroir ouvert (bateau) !", new Color(100, 200, 255, 255), 1.5f));
						}
						return true;
					}
					
					// Meuble assoyable sur le bateau
					if (bestTileData.IsSittable)
					{
						Vector2 tileWorldPos = nearbyBoat.GetTileWorldPos(bestRelX, bestRelY);
						int sitTileX = (int)(tileWorldPos.X / ts);
						int sitTileY = (int)(tileWorldPos.Y / ts);
						int sitHeight = World.GetHeightAt(sitTileX, sitTileY);
						float sitYOffset = -sitHeight * ts / 4;
						float sitWorldX = tileWorldPos.X;
						float sitWorldY = tileWorldPos.Y + sitYOffset;
						
						float sitHeightOffset = ts * 0.6f;
						if (bestTileData.Size != null && bestTileData.Size.Height > 1)
							sitHeightOffset = ts * (bestTileData.Size.Height - 0.4f);
						Vector2 boatSitPosition = new Vector2(sitWorldX, sitWorldY);
						if (bestTileData.CollisionSize != null)
							boatSitPosition = GetFurnitureSitPosition(boatSitPosition, bestTileData.CollisionSize.Width, bestTileData.CollisionSize.Height, mouseWorld);
						
						SetSitting(true, boatSitPosition, sitHeightOffset, "sit");
						AddNotification(new Notification($" Vous vous asseyez sur {bestTileData.Name} (bateau)", new Color(150, 200, 255, 255), 2f));
						return true;
					}
				}
			}

			// ========== VÉRIFIER SI ON VISE UN PNJ AVEC UNE QUÊTE (proposition ou livraison) ==========
			if (TryInteractWithQuestNpc(camera))
			{
				return true;
			}

			// ========== VÉRIFIER SI ON VISE UN VILLAGEOIS AVEC MÉTIER / COMMERCE ==========
			Entity? trader = FindTraderUnderMouse(camera);
			if (trader != null)
			{
				if ((trader.IsTrader || trader.HasProfession) && (!trader.IsTamed || trader.IsGuildMember))
				{
					TraderUI.Open(trader);
					return true;
				}
			}
			
			// ========== UTILISER FindTileUnderMouse POUR TOUT LE RESTE ==========
			(int tileX, int tileY, float _, int objectId) = FindTileUnderMouse(mouseWorld, ts);
			if (tileX == -1 || objectId == 0)
				return false;
			
			// ---- DÉCLARATION UNIQUE DE heldItem (utilisée dans plusieurs blocs) ----
			Item? heldItem = equipment.MainHand;

			// ---- FLEURS DANS LES POTS ----
			if (objectId == 86)
			{
				const string flowerMetaKey = "potted_flower";
				string plantedFlowerId = World.GetTileMeta(tileX, tileY, flowerMetaKey);
				float potDist = Vector2.Distance(playerPos, new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f));
				if (potDist <= MAX_INTERACTION_DISTANCE)
				{
					if (!string.IsNullOrEmpty(plantedFlowerId))
					{
						if (int.TryParse(plantedFlowerId, out int flowerItemId))
						{
							Program.GiveItemToPlayer(flowerItemId, 1,
								new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f));
						}
						World.RemoveTileMeta(tileX, tileY, flowerMetaKey);
						return true;
					}

					int heldFlowerId = heldItem != null ? GetItemId(heldItem.Name) : 0;
					if (heldItem != null && IsPottableFlower(heldFlowerId))
					{
						World.SetTileMeta(tileX, tileY, flowerMetaKey, heldFlowerId.ToString());
						RemoveItemFromInventory(heldItem.Name, 1);
						AddNotification(new Notification($" {heldItem.Name} plantée dans le pot !", new Color(100, 200, 100, 255), 1.5f));
						return true;
					}
				}
				return true;
			}
			
			// ---- PLANTATION DE GRAINES ----
			if (heldItem != null)
			{
				int heldId = GetItemId(heldItem.Name);
				if (GameData.ItemDatabase.TryGetValue(heldId, out var heldData) && heldData.IsSeed)
				{
					int groundId = World.GetGroundTileIdAt(tileX, tileY);
                    var groundTile = WorldTileRegistry.GetTile(groundId);
                    bool canPlant = false;

                    if (groundTile != null && World.GetObjectIdAt(tileX, tileY) == 0)
                    {
                        if (heldData.PlantType == "grass")
                        {
                            canPlant = groundTile.Material == "grass";
                        }
                        else if (heldData.PlantType == "classic")
                        {
                            //  Arbre (ex : gland) : se plante n'importe où sur un sol
                            // praticable, comme un objet plaçable — pas besoin de farmland.
                            canPlant = groundTile.Walkable;
                        }
                        else
                        {
                            canPlant = groundId == 27 || groundId == 29;
                        }
                    }

                    if (canPlant)
                    {
                        //  Vérifier la distance
                        float plantDist = Vector2.Distance(playerPos, new Vector2(tileX * ts + ts/2f, tileY * ts + ts/2f));
                        if (plantDist <= MAX_INTERACTION_DISTANCE)
                        {
                            int cropId = heldData.CropId;
                            if (cropId != 0)
                            {
                                World.PlantCrop(tileX, tileY, cropId, GetGameTime());
								SoundEffects.PlayPlant();
                                RemoveItemFromInventory(heldItem.Name, 1);
                                AddNotification(new Notification($" {heldItem.Name} plantée !", new Color(100, 200, 100, 255), 1.5f));
                                return true;
                            }
                        }
                    }
                }
            }

            var tileData = WorldTileRegistry.GetTile(objectId);
            if (tileData == null)
                return false;

			// Les cultures sont interactives comme les autres tuiles : E récolte une
			// culture mûre, tandis que le clic gauche reste le geste de casse/récolte.
			if (tileData.IsCrop)
			{
				float cropDist = Vector2.Distance(playerPos, new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f));
				if (cropDist > MAX_INTERACTION_DISTANCE)
					return false;

				var cropData = World.GetCropDataAt(tileX, tileY);
				if (cropData == null)
					return true;

				if (cropData.AccumulatedGrowthTime < tileData.GrowthTime)
				{
					AddNotification(new Notification(" La culture n'est pas encore prête", new Color(200, 200, 100, 255), 1f));
					return true;
				}

				int qty = Random.Shared.Next(tileData.HarvestMinQty, tileData.HarvestMaxQty + 1);
				int harvestItemId = GameData.GetItemId(tileData.HarvestItemId);
				if (harvestItemId <= 0)
				{
					AddNotification(new Notification($" Item de récolte introuvable : {tileData.HarvestItemId}", new Color(220, 100, 100, 255), 2f));
					return true;
				}

				Vector2 cropWorldPos = new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f);
				GiveItemToPlayer(harvestItemId, qty, cropWorldPos);

				if (tileData.RegrowsAfterHarvest)
					World.BeginCropRegrowth(tileX, tileY, tileData, cropData);
				else
					World.RemovePlacedObject(tileX, tileY);

				return true;
			}

            if (objectId == 116)
            {
                Item? heldItemLocal = equipment.MainHand;
                if (heldItemLocal != null && GetItemId(heldItemLocal.Name) == 63)
                {
                    ParseBucketContent(heldItemLocal.Metadata, out string rainBucketType, out int rainBucketCharges);
                    if (rainBucketType == "milk")
                    {
                        AddNotification(new Notification(" Le seau contient déjà du lait !", new Color(255, 200, 100, 255), 1.5f));
                        return true;
                    }
                    if (rainBucketType == "water" && rainBucketCharges >= BUCKET_MAX_CHARGES)
                    {
                        AddNotification(new Notification(" Le seau est déjà plein d'eau !", new Color(255, 200, 100, 255), 1.5f));
                        return true;
                    }

                    if (!World.RainCollectors.ContainsKey((tileX, tileY))) World.RainCollectors[(tileX, tileY)] = new RainCollectorData();
                    var bucketCollector = World.RainCollectors[(tileX, tileY)];
                    int bucketChargesNeeded = BUCKET_MAX_CHARGES - rainBucketCharges;
                    int bucketChargesAvailable = (int)Math.Floor(bucketCollector.CurrentWater);
                    int bucketChargesGained = Math.Min(bucketChargesNeeded, bucketChargesAvailable);
                    if (bucketChargesGained > 0)
                    {
                        bucketCollector.CurrentWater = Math.Max(0f, bucketCollector.CurrentWater - bucketChargesGained);
                        heldItemLocal.Metadata = FormatBucketContent("water", rainBucketCharges + bucketChargesGained);
                        AddNotification(new Notification(" Seau rempli d'eau !", new Color(100, 200, 255, 255), 1.5f));
                    }
                    else
                    {
                        AddNotification(new Notification(" Aucune eau collectée pour l'instant.", new Color(200, 180, 120, 255), 1.5f));
                    }
                    return true;
                }

                bool isWaterContainerLocal = heldItemLocal != null && GetWaterCapacityForItem(heldItemLocal) > 0;
                if (isWaterContainerLocal)
                {
                    int maxWater = GetWaterCapacityForItem(heldItemLocal);
                    int currentWater = GetWaterAmountFromMetadata(heldItemLocal);
                    if (currentWater < maxWater)
                    {
                        if (!World.RainCollectors.ContainsKey((tileX, tileY))) World.RainCollectors[(tileX, tileY)] = new RainCollectorData();
                        var collector = World.RainCollectors[(tileX, tileY)];
                        int available = (int)Math.Floor(collector.CurrentWater);
                        int amount = Math.Min(maxWater - currentWater, available);
                        if (amount > 0)
                        {
                            collector.CurrentWater = Math.Max(0f, collector.CurrentWater - amount);
                            currentWater += amount;
                            SetLiquidContainerCharges(heldItemLocal, "water", currentWater);
                            AddNotification(new Notification(" Contenant rempli !", new Color(100, 200, 255, 255), 1.5f));
                        }
                        else
                        {
                            AddNotification(new Notification(" Aucune eau collectée pour l'instant.", new Color(200, 180, 120, 255), 1.5f));
                        }
                        return true;
                    }
                    else
                    {
                        AddNotification(new Notification(" Déjà plein(e) !", new Color(255, 200, 100, 255), 1.5f));
                        return true;
                    }
                }

                // sinon, pas d'autre interaction
                return false;
            }

            // Interaction spécifique : Socle de gemme-oeil (ID 117) + Gemme-oeil en main
			// => le socle se transforme en socle avec l'oeil animé (ID 118), la gemme est consommée.
			if (objectId == 117)
			{
				Item? heldGemItem = equipment.MainHand;
				if (heldGemItem != null && GetItemId(heldGemItem.Name) == GetItemId("gem_eyeball"))
				{
					World.AddPlacedObject(tileX, tileY, 118);
					RemoveItemFromInventory(heldGemItem.Name, 1);
					AddNotification(new Notification(" La gemme-oeil s'installe sur son socle...", new Color(180, 100, 220, 255), 2f));
					return true;
				}
				// sinon, pas d'autre interaction
				return false;
			}
			
			int overlayId = World.GetOverlayAt(tileX, tileY);
			if (overlayId == 6000)
			{
				// Vérifier la distance
				float dist = Vector2.Distance(playerPos, new Vector2(tileX * ts + ts/2f, tileY * ts + ts/2f));
				if (dist <= MAX_INTERACTION_DISTANCE)
				{
					// Récolter le miel
					int mielId = 57; // ID de l'item Miel
					int qty = Random.Shared.Next(1, 4);
					Program.GiveItemToPlayer(mielId, qty, new Vector2(tileX * ts + ts/2f, tileY * ts + ts/2f));
					
					// Supprimer la ruche (ou la mettre en cooldown)
					World.RemoveOverlay(tileX, tileY);
					
					// Effet visuel (particules)
					// (optionnel)
					return true; // Interaction traitée
				}
			}
			
			// ========== CONTENEURS ==========
			if (objectId == 18 || objectId == 32)
			{
				var fireChunk = World.GetChunkAt(tileX, tileY);
				if (fireChunk != null)
				{
					var fireData = fireChunk.GetContainerAt(tileX, tileY);
					if (fireData == null)
					{
						fireData = new ContainerInventoryData(4, 4);
						fireChunk.SetContainerAt(tileX, tileY, fireData);
					}
					ContainerUI.OpenCampfire(fireData, 0, tileX, tileY, World.IsUnderground);
					return true;
				}
			}

			if (tileData.IsContainer == true)
			{
				var chunk = World.GetChunkAt(tileX, tileY);
				var containerData = chunk?.GetContainerAt(tileX, tileY);
				if (containerData == null && chunk != null)
				{
					containerData = new ContainerInventoryData(
						tileData.ContainerSlots ?? 20,
						tileData.ContainerColumns > 0 ? tileData.ContainerColumns : 8
					);
					chunk.SetContainerAt(tileX, tileY, containerData);
				}
				if (containerData != null)
				{
					ContainerUI.Open(containerData, 0, tileX, tileY, World.IsUnderground);
					return true;
				}
			}
			
			// ========== PORTE-ARMURES ==========
			if (tileData.IsArmorStand == true)
			{
				var chunk = World.GetChunkAt(tileX, tileY);
				var standData = chunk?.GetArmorStandAt(tileX, tileY);
				if (standData == null && chunk != null)
				{
					standData = new ArmorStandData();
					chunk.SetArmorStandAt(tileX, tileY, standData);
				}
				if (standData != null)
				{
					ArmorStandUI.Open(standData);
					return true;
				}
			}

			// ========== PORTE-BANNIÈRES ==========
			if (objectId == 7000)
			{
				var chunk = World.GetChunkAt(tileX, tileY);
				if (chunk == null) return false;

				var bannerData = chunk.GetBannerHolderAt(tileX, tileY);
				if (bannerData == null)
				{
					bannerData = new BannerHolderData { HasBanner = false };
					chunk.SetBannerHolderAt(tileX, tileY, bannerData);
				}

				if (bannerData.HasBanner)
				{
					int bannerId = GetItemId("Bannière");
					if (bannerId == 0) return false;
					if (!GameData.ItemDatabase.TryGetValue(bannerId, out var bannerItemData)) return false;

					var bannerItem = new Item(bannerItemData.Name, 1, bannerItemData.Color, bannerItemData.Icon);
					bannerItem.Metadata = bannerData.ToMetadata();
					bannerItem.CustomColors = new List<Color?>
					{
						bannerData.BackgroundColor,
						bannerData.PatternColor,
					};

					int remaining = AddItemToInventory(bannerItem, 1);
					if (remaining == 0)
					{
						bannerData.HasBanner = false;
					}
					else
					{
						AddNotification(new Notification(" Inventaire plein !", new Color(220, 80, 80, 255), 1.5f));
					}
					return true;
				}
				else
				{
					Item? foundBannerItem = null;
					InventorySlot? foundSlot = null;

                    var heldBannerItem = InventoryRenderer.GetHotbarItem(hotbarSlot);
                    if (heldBannerItem != null && heldBannerItem.Name == "Bannière" && !string.IsNullOrEmpty(heldBannerItem.Metadata))
                    {
                        foundBannerItem = heldBannerItem;
                        foundSlot = InventoryRenderer.InventorySlots.FirstOrDefault(slot => slot.Item == heldBannerItem);
                    }
                    else if (equipment.MainHand != null && equipment.MainHand.Name == "Bannière" && !string.IsNullOrEmpty(equipment.MainHand.Metadata))
                    {
                        foundBannerItem = equipment.MainHand;
                        foundSlot = InventoryRenderer.InventorySlots.FirstOrDefault(slot => slot.Item == equipment.MainHand);
                    }

                    if (foundBannerItem == null)
                    {
                        foreach (var slot in InventoryRenderer.InventorySlots)
                        {
                            if (!slot.IsEmpty && slot.Item?.Name == "Bannière" && !string.IsNullOrEmpty(slot.Item.Metadata))
                            {
                                foundBannerItem = slot.Item;
                                foundSlot = slot;
                                break;
                            }
                        }
                    }

                    if (foundBannerItem == null)
                    {
                        return true;
                    }

                    var newData = BannerHolderData.FromMetadata(foundBannerItem.Metadata, foundBannerItem.CustomColors);
                    newData.HasBanner = true;
                    chunk.SetBannerHolderAt(tileX, tileY, newData);

                    if (foundSlot != null)
                    {
                        foundSlot.Count--;
                        if (foundSlot.Count <= 0) foundSlot.Clear();
                    }
                    else
                    {
                        RemoveItemFromInventory(foundBannerItem.Name, 1);

                        if (equipment.MainHand == foundBannerItem)
                        {
                            equipment.MainHand = null;
                        }
                    }
                    return true;
                }
            }

            // ========== STATIONS ==========
            if (tileData.IsStation == true)
			{
				string stationType = tileData.StationType ?? "";
				
				if (stationType == "workbench")
				{
					Program.ActiveStation = "workbench";
					CraftingUI.Toggle("workbench");
				}
				else if (stationType == "furnace")
				{
					Program.ActiveStation = "furnace";
					CraftingUI.Toggle("furnace");
				}
				else if (stationType == "anvil")
				{
					Program.ActiveStation = "anvil";
					CraftingUI.Toggle("anvil");
				}
				else if (stationType == "loom")
				{
					Program.ActiveStation = "loom";
					CraftingUI.Toggle("loom");
				}
				else if (stationType == "carpentry")
				{
					Program.ActiveStation = "carpentry";
					CraftingUI.Toggle("carpentry");
				}
				else if (stationType == "dye" || stationType == "dye_tray")
				{
					DyeingUI.Open();
				}
				else if (stationType == "cauldron")
				{
					CauldronUI.Open();
				}
				else if (stationType == "mirror")
				{
					CharacterCustomizationUI.Open();
				}
				else
				{
					AddNotification(new Notification($"Station inconnue: {stationType}", new Color(255, 200, 100, 255), 1.5f));
				}
				return true;
			}
			
			// ========== PORTAILS DE TÉLÉPORTATION ==========
			if (objectId == 119)
			{
				float portalDist = Vector2.Distance(playerPos, new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f));
				if (portalDist > MAX_INTERACTION_DISTANCE)
					return false;

				bool isCave = World.IsUnderground;
				bool alreadyKnown = World.IsPortalDiscovered(tileX, tileY, isCave);
				var portal = World.DiscoverPortal(tileX, tileY, isCave, GetGameTime());

				if (!alreadyKnown)
				{
					World.SavePortals();
					AddNotification(new Notification($" Nouveau portail découvert : {portal.DisplayName}", new Color(180, 140, 255, 255), 2.5f));
					ApplyScreenShake(3f, 0.15f);
				}

				PortalUI.Open(tileX, tileY, isCave);
				return true;
			}

			// ========== ENTRÉES/SORTIES DE DONJONS ==========
			// Utilisent le même sprite (id 50) que les entrées de grottes, mais sont
			// distinguées via les portails enregistrés dans World — donjon d'abord.
			if (objectId == 50 && (World.IsDungeonEntrance(tileX, tileY) || World.IsDungeonReturnPortal(tileX, tileY)))
			{
				World.SaveAllActiveChunks();
				SaveCurrentGame();
				World.ClearAllEntities(entities);
				entities.Clear();

				bool moved;
				if (World.IsDungeonEntrance(tileX, tileY))
				{
					moved = World.TryEnterDungeon(tileX, tileY, out Vector2 spawnPos);
					if (moved)
					{
						// Validation de sécurité de la position retournée
						if (float.IsNaN(spawnPos.X) || float.IsNaN(spawnPos.Y) || float.IsInfinity(spawnPos.X) || float.IsInfinity(spawnPos.Y))
						{
							spawnPos = new Vector2(tileX * TileSize + TileSize / 2f, tileY * TileSize + TileSize / 2f);
							Console.WriteLine($"Fallback : position de spawn invalide depuis donjon, utilisation de la porte ({tileX},{tileY})");
						}
						SetPlayerPosition(spawnPos);
						_playerFacing = 1f;
						AddNotification(new Notification(Localization.Get("dungeon.enter_message"), new Color(200, 160, 255, 255), 2f));
					}
				}
				else
				{
					// La porte de sortie "explose" en un nuage de gravats au moment où on la franchit.
					Vector2 doorWorldPos = new Vector2(tileX * TileSize + TileSize / 2f, tileY * TileSize + TileSize / 2f);
					SpawnTileBreakParticles(doorWorldPos, new Color(120, 112, 104, 255));

					moved = World.TryExitDungeon(tileX, tileY, out Vector2 spawnPos);
					if (moved)
					{
						if (float.IsNaN(spawnPos.X) || float.IsNaN(spawnPos.Y) || float.IsInfinity(spawnPos.X) || float.IsInfinity(spawnPos.Y))
						{
							spawnPos = new Vector2(tileX * TileSize + TileSize / 2f, tileY * TileSize + TileSize / 2f);
							Console.WriteLine($"Fallback : position de spawn invalide depuis sortie de donjon, utilisation de la porte ({tileX},{tileY})");
						}
						SetPlayerPosition(spawnPos);
						_playerFacing = 1f;
						AddNotification(new Notification("Vous quittez le donjon.", new Color(200, 200, 200, 255), 2f));
					}
				}

				float dungeonCurrentTime = (float)Raylib.GetTime();
				World.UpdateActiveChunks(_playerPos, dungeonCurrentTime, entities);
				return true;
			}

			// ========== ENTRÉES/SORTIES DE GROTTES ==========
			// (objectId == 50 en sous-terrain sans être un portail de donjon enregistré = donnée
			// périmée d'une session précédente ; on l'ignore proprement plutôt que de déclencher
			// la logique "entrer dans une grotte depuis la surface", qui ne fait rien de cohérent.)
			if (objectId == 50 && World.IsUnderground)
			{
				AddNotification(new Notification("Cette entrée semble s'être refermée...", new Color(200, 200, 200, 255), 2f));
				return true;
			}
			if (objectId == 50 || objectId == 52)
			{
				World.SaveAllActiveChunks();
				SaveCurrentGame();
				World.ClearAllEntities(entities);
				entities.Clear();
				StartCaveTransition(tileX, tileY, objectId == 50);
				return true;
			}

			// ========== LIT (DORMIR) ==========
			if (tileData.IsSleepable)
			{
				Vector2 sleepPos = GetFurnitureAnchorPosition(tileX, tileY, tileData);
				PrepareTentOccupation(sleepPos, null);
				SetSitting(true, sleepPos, 0f, "sleep", behindFurniture: tileData.Name.Equals("Tent", StringComparison.OrdinalIgnoreCase));
				AddNotification(new Notification($" Vous vous allongez sur {tileData.Name}.", new Color(150, 200, 255, 255), 2f));
				return true;
			}

			// ========== MEUBLES ASSOYABLES ==========
			if (tileData.IsSittable)
			{
				Vector2 sitPos = GetFurnitureAnchorPosition(tileX, tileY, tileData);
				if (tileData.CollisionSize != null)
					sitPos = GetFurnitureSitPosition(sitPos, tileData.CollisionSize.Width, tileData.CollisionSize.Height, mouseWorld);
				SetSitting(true, sitPos, 0f, "sit");
				return true;
			}

			// ========== PUITS (BOIRE OU REMPLIR ARROSOIR) ==========
			if (tileData.IsDrinkable)
            {
                Item? heldWellItem = equipment.MainHand;

                if (heldWellItem != null && GetItemId(heldWellItem.Name) == 63)
                {
                    ParseBucketContent(heldWellItem.Metadata, out string wellBucketType, out int wellBucketCharges);
                    if (wellBucketType == "milk")
                    {
                        AddNotification(new Notification(" Le seau contient déjà du lait !", new Color(255, 200, 100, 255), 1.5f));
                    }
                    else if (wellBucketType == "water" && wellBucketCharges >= BUCKET_MAX_CHARGES)
                    {
                        AddNotification(new Notification(" Le seau est déjà plein d'eau !", new Color(255, 200, 100, 255), 1.5f));
                    }
                    else
                    {
                        heldWellItem.Metadata = FormatBucketContent("water", BUCKET_MAX_CHARGES);
                        AddNotification(new Notification(" Seau rempli d'eau !", new Color(100, 200, 255, 255), 1.5f));
                    }
                    return true;
                }

                bool isWaterContainer = heldWellItem != null && GetWaterCapacityForItem(heldWellItem) > 0;

                if (isWaterContainer)
                {
                    int maxWater = GetWaterCapacityForItem(heldWellItem);
                    int currentWater = GetWaterAmountFromMetadata(heldWellItem);

                    if (currentWater < maxWater)
                    {
                        SetLiquidContainerCharges(heldWellItem, "water", maxWater);
                        AddNotification(new Notification(" Contenant rempli !", new Color(100, 200, 255, 255), 1.5f));
                        return true;
                    }
                    else
                    {
                        AddNotification(new Notification(" Déjà plein(e) !", new Color(255, 200, 100, 255), 1.5f));
                        return true;
                    }
                }
				if (playerThirst >= playerMaxThirst)
				{
					AddNotification(new Notification(" Vous n'avez plus soif", new Color(150, 200, 255, 255), 1.5f));
					return true;
				}
				
				const float WELL_THIRST_RESTORE = 45f;
				playerThirst = Math.Min(playerMaxThirst, playerThirst + WELL_THIRST_RESTORE);
				AddNotification(new Notification(" Vous buvez au puits", new Color(120, 190, 255, 255), 1.5f));
				return true;
			}
			
			// ========== MACHINES À VAPEUR ==========
			if (objectId == 401)
			{
				if (!World.SteamEngines.ContainsKey((tileX, tileY)))
					World.SteamEngines[(tileX, tileY)] = new SteamEngineData();
				
				var engine = World.SteamEngines[(tileX, tileY)];

				if (ContainerUI.GetCurrentContainer() != null && TryLoadSteamEngineFuelFromContainer(tileX, tileY, out float containerWater, out float containerCoal))
				{
					if (NetworkManager.IsClient)
						NetworkManager.RequestEnergyAction("AddFuel", tileX, tileY, water: containerWater, coal: containerCoal);
					else
					{
						World.AddSteamEngineFuel(tileX, tileY, containerWater, containerCoal);
						if (NetworkManager.IsHost) NetworkManager.BroadcastEnergySnapshot();
					}
					AddNotification(new Notification($" Combustible transféré : {containerCoal:0} charbon, {containerWater:0}L d'eau", new Color(100, 200, 255, 255), 1.5f));
					return true;
				}
				
				if (heldItem != null && TryLoadSteamEngineFuelFromHeldItem(tileX, tileY, heldItem, out float heldWater, out float heldCoal))
				{
					if (NetworkManager.IsClient)
						NetworkManager.RequestEnergyAction("AddFuel", tileX, tileY, water: heldWater, coal: heldCoal);
					else
					{
						World.AddSteamEngineFuel(tileX, tileY, heldWater, heldCoal);
						if (NetworkManager.IsHost) NetworkManager.BroadcastEnergySnapshot();
					}
					string fuelMessage = heldWater > 0f
						? $" Ajout de {heldWater:0}L d'eau"
						: $" Ajout de charbon (+{heldCoal:0})";
					AddNotification(new Notification(fuelMessage, new Color(200, 180, 100, 255), 1.5f));
					return true;
				}
				
				//  MULTIJOUEUR : marche/arrêt délégué au host côté client, sinon la machine
				// ne s'allume que localement et jamais pour les autres joueurs.
				if (NetworkManager.IsClient)
				{
					NetworkManager.RequestEnergyAction("ToggleEngine", tileX, tileY);
					AddNotification(new Notification(" Machine à vapeur : commutation en cours...", new Color(200, 180, 100, 255), 1.5f));
				}
				else
				{
					bool nowOn = World.ToggleSteamEngine(tileX, tileY);
					if (NetworkManager.IsHost) NetworkManager.BroadcastEnergySnapshot();
					AddNotification(new Notification(nowOn ? " Machine à vapeur démarrée" : " Machine à vapeur arrêtée",
						new Color(200, 180, 100, 255), 1.5f));
				}
				return true;
			}
			
			// ========== DYNAMOS ==========
			if (objectId == 403)
			{
				if (!World.Dynamos.ContainsKey((tileX, tileY)))
					World.Dynamos[(tileX, tileY)] = new DynamoData();
				
				var dynamo = World.Dynamos[(tileX, tileY)];
				
				AddNotification(new Notification($" Dynamo - Production: {dynamo.ElectricityOutput:F0} kW | Connexions: {dynamo.Connections.Count}", 
					new Color(100, 200, 255, 255), 1.5f));
				
				if (!_isConnectingWire)
				{
					// Démarrer une connexion depuis la sortie de cette dynamo
					_isConnectingWire = true;
					_wireStartTile = (tileX, tileY);
					_wireStartDynamo = dynamo;
					_wireStartBattery = null;
					_wireStartGate = null;
					AddNotification(new Notification(" Mode connexion: sélectionnez une cible (batterie, ampoule, chargeur ou porte logique)", new Color(100, 200, 255, 255), 1.5f));
				}
				else if (_wireStartTile != (-1, -1) && _wireStartTile != (tileX, tileY))
				{
					//  CORRECTIF : on ne modifie plus jamais dynamo.Connections directement ici.
					// Une dynamo n'a pas de port d'entrée : si la cible cliquée est une autre
					// dynamo (ou tout autre objet sans entrée), World.ConnectEnergyWire la
					// refusera ("invalid_target") au lieu de créer silencieusement le câble.
					if (NetworkManager.IsClient)
					{
						NetworkManager.RequestEnergyAction("ConnectWire", _wireStartTile.x, _wireStartTile.y, tileX, tileY);
						AddNotification(new Notification(" Connexion en cours...", new Color(100, 200, 255, 255), 1.5f));
					}
					else
					{
						string result = World.ConnectEnergyWire(_wireStartTile.x, _wireStartTile.y, tileX, tileY);
						if (result == "ok")
						{
							AddNotification(new Notification($" Fil connecté !", new Color(100, 255, 100, 255), 1.5f));
							if (NetworkManager.IsHost) NetworkManager.BroadcastEnergySnapshot();
						}
						else if (result == "already_exists")
							AddNotification(new Notification(" Connexion déjà existante !", new Color(255, 200, 100, 255), 1.5f));
						else if (result == "invalid_target")
							AddNotification(new Notification(" Cible invalide : cet objet n'a pas d'entrée.", new Color(255, 100, 100, 255), 1.5f));
						else if (result == "invalid_source" || result == "unsupported_source")
							AddNotification(new Notification(" Source invalide : cet objet n'a pas de sortie.", new Color(255, 100, 100, 255), 1.5f));
					}

					_isConnectingWire = false;
					_wireStartTile = (-1, -1);
					_wireStartDynamo = null;
					_wireStartBattery = null;
					_wireStartGate = null;
				}
				else
				{
					// Re-clic sur la même dynamo : annuler le mode connexion
					_isConnectingWire = false;
					_wireStartTile = (-1, -1);
					_wireStartDynamo = null;
					_wireStartBattery = null;
					_wireStartGate = null;
					AddNotification(new Notification(" Connexion annulée.", new Color(255, 200, 100, 255), 1.5f));
				}
				return true;
			}
			
			return false;
		}

		// Ce panneau ne sert plus qu'à saisir le nom d'un NOUVEAU monde : il est ouvert
		// en sélectionnant le bouton "Créer un monde" du carrousel du menu principal
		// (voir ConfirmWorldCarouselSelection). La liste/chargement des sauvegardes se
		// fait maintenant directement depuis le carrousel (DrawWorldCarousel).
		static void DrawCreationText(string text, int x, int y, int fontSize, Color color = default)
		{
			Color fill = color.A == 0 ? Color.White : color;
			for (int offsetX = -1; offsetX <= 1; offsetX++)
			{
				for (int offsetY = -1; offsetY <= 1; offsetY++)
				{
					if (offsetX == 0 && offsetY == 0) continue;
					FontManager.DrawText(text, x + offsetX, y + offsetY, fontSize, Color.Black);
				}
			}
			FontManager.DrawText(text, x, y, fontSize, fill);
		}

		static void DrawCreateWorldMenu()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			Vector2 mousePos = Raylib.GetMousePosition();
			float dt = Raylib.GetFrameTime();
			float creationScale = Math.Clamp(MathF.Min(sw / 1280f, sh / 900f), 0.65f, 1f);
			int Scale(float value) => Math.Max(1, (int)MathF.Round(value * creationScale));

			// Fond défilant (repris de l'ancien menu) — le menu n'est plus dans une boîte,
			// il est directement ouvert sur le fond.
			DrawScrollingMenuBackground(sw, sh, dt);

			int centerX = sw / 2;

			// Titre (adapté selon qu'on personnalise un personnage pour rejoindre un
			// serveur, ou qu'on crée un nouveau monde en solo/hébergement)
			string screenTitle = _isJoinCharacterCreation ? Localization.Get("menu.new_character") : Localization.Get("menu.create_world_title");
			int titleFontSize = Scale(34);
			int tw = FontManager.MeasureText(screenTitle, titleFontSize);
			DrawCreationText(screenTitle, centerX - tw / 2, (int)(sh * 0.05f), titleFontSize, new Color(230, 200, 100, 255));

			// ===================================================
			// 1. APERÇU DU PERSONNAGE (centré, légèrement en haut, avec rotation)
			// ===================================================
			int previewSize = Scale(220);
			int previewX = centerX - previewSize / 2;
			int previewY = (int)(sh * 0.14f);
			Vector2 previewPos = new Vector2(previewX + previewSize / 2, previewY + previewSize / 2);

			// Mise à jour de la rotation
			if (_worldCreationRotating)
			{
				_worldCreationRot += dt * 90f; // Rotation lente
			}
			if (_worldCreationRot > 360f) _worldCreationRot -= 360f;

			// Rotation auto si la souris survole le personnage (plus de flèches cliquables)
			Rectangle previewRect = new Rectangle(previewX, previewY, previewSize, previewSize);
			_worldCreationRotating = Raylib.CheckCollisionPointRec(mousePos, previewRect);

			// Dessin du personnage avec la rotation
			float facing = MathF.Cos(_worldCreationRot * MathF.PI / 180f) >= 0 ? 1f : -1f;
			DrawCharacterPreviewWithRotation(previewPos, _tempSkinColor, _tempHairStyle, _tempBeardStyle, _tempHairColor, _tempEyeStyle, _tempEyeColor, facing);

			// ===================================================
			// 2. BARRE D'ONGLETS (creator_bar_left / mid / right + icônes de catégorie)
			// ===================================================
			//  En mode "rejoindre" on personnalise seulement le personnage : l'onglet
			// "OPTIONS" (règles du monde : armes cassables, nourriture qui pourrit...) n'a
			// pas de sens ici, ces réglages appartiennent à l'hôte.
			int tabCount = _isJoinCharacterCreation ? _creationTabs.Length - 1 : _creationTabs.Length;
			int segW = Scale(76);
			int barH = Scale(58);
			int capW = Scale(_creatorBarLeft.Id != 0 ? Math.Max(16, _creatorBarLeft.Width) : 20);
			int barY = previewY + previewSize + Scale(18);
			int midW = segW * tabCount;
			int barTotalW = capW * 2 + midW;
			int barX = centerX - barTotalW / 2;

			// Capuchon gauche
			if (_creatorBarLeft.Id != 0)
				Raylib.DrawTexturePro(_creatorBarLeft, new Rectangle(0, 0, _creatorBarLeft.Width, _creatorBarLeft.Height),
					new Rectangle(barX, barY, capW, barH), Vector2.Zero, 0, Color.White);
			else
				Raylib.DrawRectangle(barX, barY, capW, barH, new Color(40, 35, 25, 220));

			// Section centrale (étirée pour couvrir tous les onglets)
			if (_creatorBarMid.Id != 0)
				Raylib.DrawTexturePro(_creatorBarMid, new Rectangle(0, 0, _creatorBarMid.Width, _creatorBarMid.Height),
					new Rectangle(barX + capW, barY, midW, barH), Vector2.Zero, 0, Color.White);
			else
				Raylib.DrawRectangle(barX + capW, barY, midW, barH, new Color(40, 35, 25, 220));

			// Capuchon droit
			if (_creatorBarRight.Id != 0)
				Raylib.DrawTexturePro(_creatorBarRight, new Rectangle(0, 0, _creatorBarRight.Width, _creatorBarRight.Height),
					new Rectangle(barX + capW + midW, barY, capW, barH), Vector2.Zero, 0, Color.White);
			else
				Raylib.DrawRectangle(barX + capW + midW, barY, capW, barH, new Color(40, 35, 25, 220));

			Raylib.DrawRectangleLines(barX, barY, barTotalW, barH, new Color(80, 70, 50, 180));

			Texture2D[] tabIcons = { _creatorIconHair, _creatorIconBeard, _creatorIconSkin, _creatorIconEyes, _creatorIconName, new Texture2D() };

			for (int i = 0; i < tabCount; i++)
			{
				Rectangle segRect = new Rectangle(barX + capW + i * segW, barY, segW, barH);
				bool hover = Raylib.CheckCollisionPointRec(mousePos, segRect);
				bool selected = (i == _creationTab);

				// Séparateur entre segments
				if (i > 0)
					Raylib.DrawLine((int)segRect.X, barY + 4, (int)segRect.X, barY + barH - 4, new Color(80, 70, 50, 150));

				// Surbrillance du segment actif / survolé
				if (selected)
					Raylib.DrawRectangle((int)segRect.X, (int)segRect.Y, (int)segRect.Width, (int)segRect.Height, new Color(210, 180, 100, 70));
				else if (hover)
					Raylib.DrawRectangle((int)segRect.X, (int)segRect.Y, (int)segRect.Width, (int)segRect.Height, new Color(210, 180, 100, 30));
				if (selected)
					Raylib.DrawRectangleLinesEx(segRect, 2, new Color(230, 200, 100, 255));

				// Icône de la catégorie
				Texture2D icon = tabIcons[i];
				if (icon.Id != 0)
				{
					int iconSize = Scale(30);
					float scale = iconSize / (float)Math.Max(icon.Width, icon.Height);
					Vector2 iconPos = new Vector2(segRect.X + segRect.Width / 2 - (icon.Width * scale) / 2, segRect.Y + 6);
					SafeDrawTexture(icon, iconPos, 0, scale, Color.White);
				}

				// Libellé de la catégorie
				int labelFontSize = Scale(11);
				int labelW = FontManager.MeasureText(_creationTabs[i], labelFontSize);
				DrawCreationText(_creationTabs[i], (int)(segRect.X + segRect.Width / 2 - labelW / 2), (int)(segRect.Y + barH - Scale(18)), labelFontSize,
					selected ? new Color(230, 210, 150, 255) : Color.White);

				if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					_creationTab = i;
					// Synchroniser les sliders avec la couleur de la nouvelle catégorie
					if (i == 0 || i == 1) SyncSlidersFromColor(_tempHairColor);
					else if (i == 2) SyncSlidersFromColor(_tempSkinColor);
					else if (i == 3) SyncSlidersFromColor(_tempEyeColor);
				}
			}

			// Bouton "aléatoire" (random.png), juste à droite de la barre d'onglets :
			// randomise tous les éléments (styles) et toutes les couleurs du personnage.
			int randomButtonSize = Scale(44);
			Rectangle randomBtnRect = new Rectangle(barX + barTotalW + Scale(14), barY + (barH - randomButtonSize) / 2f, randomButtonSize, randomButtonSize);
			bool randomHover = Raylib.CheckCollisionPointRec(mousePos, randomBtnRect);
			Raylib.DrawRectangleRounded(randomBtnRect, 0.25f, 8, randomHover ? new Color(70, 60, 40, 220) : new Color(40, 35, 25, 180));
			Raylib.DrawRectangleRoundedLines(randomBtnRect, 0.25f, 8, 2, new Color(120, 100, 70, 200));
			if (_creatorRandomIcon.Id != 0)
			{
				float iconPad = Scale(8);
				float iconSize = randomBtnRect.Width - iconPad * 2;
				float scale = iconSize / Math.Max(_creatorRandomIcon.Width, _creatorRandomIcon.Height);
				Vector2 iconPos = new Vector2(randomBtnRect.X + iconPad, randomBtnRect.Y + iconPad);
				SafeDrawTexture(_creatorRandomIcon, iconPos, 0, scale, Color.White);
			}
			else
			{
				int qFontSize = Scale(22);
				int qW = FontManager.MeasureText("?", qFontSize);
				DrawCreationText("?", (int)(randomBtnRect.X + randomBtnRect.Width / 2 - qW / 2), (int)(randomBtnRect.Y + Scale(10)), qFontSize);
			}
			if (randomHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				RandomizeCharacterCustomization();
				SyncSlidersFromColor(_creationTab == 2 ? _tempSkinColor : (_creationTab == 3 ? _tempEyeColor : _tempHairColor));
			}

			// ===================================================
			// 3. ZONE DE SÉLECTION (styles à gauche, sélecteur RGB à droite si besoin)
			// ===================================================
			int selectionY = barY + barH + Scale(24);
			int pickerSlotSize = Math.Clamp(Scale(60), 44, 76);
			int pickerSpacing = -Math.Max(1, (int)MathF.Round(pickerSlotSize * 3f / 22f));
			int pickerCols = 5;
			int pickerWidth = pickerCols * pickerSlotSize + (pickerCols - 1) * pickerSpacing;
			int leftColumnX = Math.Max(Scale(20), centerX - pickerWidth - Scale(36));
			int rightColumnX = Math.Min(sw - Scale(260), leftColumnX + pickerWidth + Scale(48));

			switch (_creationTab)
			{
				case 0: // Cheveux
					DrawHairStylePicker(leftColumnX, selectionY, pickerSlotSize, pickerSpacing, pickerCols);
					DrawColorPicker(rightColumnX, selectionY, "Couleur des cheveux", ref _tempHairColor, creationScale);
					break;
				case 1: // Barbe
					DrawBeardStylePicker(leftColumnX, selectionY, pickerSlotSize, pickerSpacing, pickerCols);
					DrawColorPicker(rightColumnX, selectionY, "Couleur des cheveux/barbe", ref _tempHairColor, creationScale);
					break;
				case 2: // Peau
					DrawColorPicker(leftColumnX, selectionY, "Couleur de peau", ref _tempSkinColor, creationScale);
					break;
				case 3: // Yeux
					DrawEyeStylePicker(leftColumnX, selectionY, pickerSlotSize, pickerSpacing, pickerCols);
					DrawColorPicker(rightColumnX, selectionY, "Couleur des yeux", ref _tempEyeColor, creationScale);
					break;
				case 4: // Prénom
					{
						int inputW = Scale(340);
						int inputX = centerX - inputW / 2;
						int inputY = selectionY + Scale(10);
						string fieldLabel = _isJoinCharacterCreation ? "Nom du personnage" : "Prénom / nom du monde";
						DrawCreationText(fieldLabel, inputX, inputY - Scale(24), Scale(14));
						int inputH = Scale(42);
						Raylib.DrawRectangleRounded(new Rectangle(inputX, inputY, inputW, inputH), 0.1f, 8, new Color(30, 30, 40, 255));
						Raylib.DrawRectangleRoundedLines(new Rectangle(inputX, inputY, inputW, inputH), 0.1f, 8, 2, new Color(120, 100, 70, 255));
						TextInput.DrawSingleLine(_newWorldName, "world-name", new Rectangle(inputX, inputY, inputW, inputH), Scale(18),
							Color.White, new Color(90, 110, 150, 220), new Color(230, 200, 100, 255), 14);

						if (!string.IsNullOrEmpty(_creationError))
						{
							int errW = FontManager.MeasureText(_creationError, 14);
							DrawCreationText(_creationError, centerX - errW / 2, inputY + Scale(56), Scale(14), Color.Red);
						}

						// Edition du champ de texte (actif uniquement sur cet onglet)
						if (!TextInput.IsFocused("world-name"))
							TextInput.Focus("world-name", _newWorldName.Length);
						TextInput.Update(ref _newWorldName, "world-name", new Rectangle(inputX, inputY, inputW, inputH), Math.Max(12, Scale(30)));
						if (Raylib.IsKeyPressed(KeyboardKey.Enter) && (_isJoinCharacterCreation || _newWorldName.Length > 0))
						{
							if (_isJoinCharacterCreation)
							{
								ConfirmJoinCharacterCreation();
								return;
							}
							ApplyCharacterCustomization(_tempSkinColor, _tempHairStyle, _tempBeardStyle, _tempHairColor, _tempEyeStyle, _tempEyeColor);
							if (TryStartNewGame(_newWorldName))
							{
								_isInWorldCreation = false;
								_currentSaveName = _newWorldName;
								_gameState = GameState.Playing;
								return;
							}
						}
					}
					break;
				case 5: // Options
					{
						int optionW = Scale(380);
						int optionX = centerX - optionW / 2;
						int optionY = selectionY + Scale(10);

						DrawCreationText("Options du monde", optionX, optionY, Scale(18));

						int rowH = Scale(70);
						Rectangle weaponsRect = new Rectangle(optionX, optionY + Scale(40), optionW, rowH - Scale(10));
						Rectangle foodRect = new Rectangle(optionX, optionY + Scale(40) + rowH, optionW, rowH - Scale(10));

						Texture2D weaponsIcon = _worldSettingsIconWeaponsBreak;
						Texture2D foodIcon = _worldSettingsIconFoodSpoil;

						Rectangle weaponsIconRect = new Rectangle(optionX + Scale(12), optionY + Scale(48), Scale(44), Scale(44));
						Rectangle foodIconRect = new Rectangle(optionX + Scale(12), optionY + Scale(48) + rowH, Scale(44), Scale(44));
						Rectangle weaponsToggleRect = new Rectangle(optionX + Scale(70), optionY + Scale(54), Scale(64), Scale(28));
						Rectangle foodToggleRect = new Rectangle(optionX + Scale(70), optionY + Scale(54) + rowH, Scale(64), Scale(28));
						int textX = optionX + Scale(150);

						Color optionBorder = new Color(120, 120, 130, 180);
						Color weaponsBorder = Raylib.CheckCollisionPointRec(mousePos, weaponsRect) ? new Color(180, 180, 200, 220) : optionBorder;
						Color foodBorder = Raylib.CheckCollisionPointRec(mousePos, foodRect) ? new Color(180, 180, 200, 220) : optionBorder;

						Raylib.DrawRectangleRoundedLines(weaponsRect, 0.15f, 8, 2, weaponsBorder);
						Raylib.DrawRectangleRoundedLines(foodRect, 0.15f, 8, 2, foodBorder);

						if (weaponsIcon.Id != 0)
						{
							float iconScale = Math.Min(weaponsIconRect.Width / (float)weaponsIcon.Width, weaponsIconRect.Height / (float)weaponsIcon.Height);
							SafeDrawTexture(weaponsIcon, new Vector2(weaponsIconRect.X + (weaponsIconRect.Width - weaponsIcon.Width * iconScale) / 2, weaponsIconRect.Y + (weaponsIconRect.Height - weaponsIcon.Height * iconScale) / 2), 0, iconScale, Color.White);
						}
						else
						{
							Raylib.DrawRectangleRounded(weaponsIconRect, 0.15f, 6, new Color(50, 50, 50, 200));
							Raylib.DrawRectangleRoundedLines(weaponsIconRect, 0.15f, 6, 1, new Color(120, 110, 100, 180));
						}

						if (foodIcon.Id != 0)
						{
							float iconScale = Math.Min(foodIconRect.Width / (float)foodIcon.Width, foodIconRect.Height / (float)foodIcon.Height);
							SafeDrawTexture(foodIcon, new Vector2(foodIconRect.X + (foodIconRect.Width - foodIcon.Width * iconScale) / 2, foodIconRect.Y + (foodIconRect.Height - foodIcon.Height * iconScale) / 2), 0, iconScale, Color.White);
						}
						else
						{
							Raylib.DrawRectangleRounded(foodIconRect, 0.15f, 6, new Color(50, 50, 50, 200));
							Raylib.DrawRectangleRoundedLines(foodIconRect, 0.15f, 6, 1, new Color(120, 110, 100, 180));
						}

						Texture2D weaponsToggleTexture = WorldWeaponsBreak ? _buttonToggleOn : _buttonToggleOff;
						Texture2D foodToggleTexture = WorldFoodSpoil ? _buttonToggleOn : _buttonToggleOff;
						bool weaponsToggleHover = Raylib.CheckCollisionPointRec(mousePos, weaponsToggleRect);
						bool foodToggleHover = Raylib.CheckCollisionPointRec(mousePos, foodToggleRect);
						Color weaponsToggleTint = weaponsToggleHover ? new Color(255, 255, 230, 220) : Color.White;
						Color foodToggleTint = foodToggleHover ? new Color(255, 255, 230, 220) : Color.White;
						Color weaponsToggleBg = weaponsToggleHover ? new Color(255, 255, 255, 40) : new Color(0, 0, 0, 0);
						Color foodToggleBg = foodToggleHover ? new Color(255, 255, 255, 40) : new Color(0, 0, 0, 0);

						Raylib.DrawRectangleRounded(weaponsToggleRect, 0.2f, 6, weaponsToggleBg);
						Raylib.DrawRectangleRounded(foodToggleRect, 0.2f, 6, foodToggleBg);

						if (weaponsToggleTexture.Id != 0)
						{
							float toggleScale = Math.Min(weaponsToggleRect.Width / (float)weaponsToggleTexture.Width, weaponsToggleRect.Height / (float)weaponsToggleTexture.Height);
							SafeDrawTexture(weaponsToggleTexture, new Vector2(weaponsToggleRect.X + (weaponsToggleRect.Width - weaponsToggleTexture.Width * toggleScale) / 2, weaponsToggleRect.Y + (weaponsToggleRect.Height - weaponsToggleTexture.Height * toggleScale) / 2), 0, toggleScale, weaponsToggleTint);
						}
						else
						{
							Raylib.DrawRectangleRounded(weaponsToggleRect, 0.2f, 6, WorldWeaponsBreak ? new Color(90, 170, 90, 255) : new Color(80, 80, 80, 255));
							Raylib.DrawRectangleRoundedLines(weaponsToggleRect, 0.2f, 6, 1, new Color(140, 140, 140, 200));
						}

						if (foodToggleTexture.Id != 0)
						{
							float toggleScale = Math.Min(foodToggleRect.Width / (float)foodToggleTexture.Width, foodToggleRect.Height / (float)foodToggleTexture.Height);
							SafeDrawTexture(foodToggleTexture, new Vector2(foodToggleRect.X + (foodToggleRect.Width - foodToggleTexture.Width * toggleScale) / 2, foodToggleRect.Y + (foodToggleRect.Height - foodToggleTexture.Height * toggleScale) / 2), 0, toggleScale, foodToggleTint);
						}
						else
						{
							Raylib.DrawRectangleRounded(foodToggleRect, 0.2f, 6, WorldFoodSpoil ? new Color(90, 170, 90, 255) : new Color(80, 80, 80, 255));
							Raylib.DrawRectangleRoundedLines(foodToggleRect, 0.2f, 6, 1, foodToggleHover ? new Color(220, 220, 180, 255) : new Color(140, 140, 140, 200));
						}

						DrawCreationText("Armes se cassent", textX, optionY + Scale(52), Scale(18));
						DrawCreationText("Les armes peuvent se briser pendant le combat.", textX, optionY + Scale(72), Scale(12));
						DrawCreationText("La nourriture pourrit", textX, optionY + Scale(52) + rowH, Scale(18));
						DrawCreationText("Les aliments périssables se dégradent avec le temps.", textX, optionY + Scale(72) + rowH, Scale(12));

						if (Raylib.CheckCollisionPointRec(mousePos, weaponsToggleRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
							WorldWeaponsBreak = !WorldWeaponsBreak;
						if (Raylib.CheckCollisionPointRec(mousePos, foodToggleRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
							WorldFoodSpoil = !WorldFoodSpoil;
						int seedRowY = optionY + Scale(40) + rowH * 2;
						Rectangle seedFieldRect = new Rectangle(optionX + Scale(14), seedRowY + Scale(30), optionW - Scale(28), Scale(42));
bool seedFieldHover = Raylib.CheckCollisionPointRec(mousePos, seedFieldRect);
string seedLabel = string.IsNullOrEmpty(_newWorldSeed) ? "Al?atoire" : _newWorldSeed;
DrawCreationText("Seed du monde", optionX + Scale(14), seedRowY, Scale(16));
Raylib.DrawRectangleRounded(seedFieldRect, 0.1f, 8, new Color(30, 30, 40, 255));
Raylib.DrawRectangleRoundedLines(seedFieldRect, 0.1f, 8, 2, seedFieldHover || _seedFieldFocused ? new Color(230, 200, 100, 255) : new Color(120, 100, 70, 255));
TextInput.DrawSingleLine(_newWorldSeed, "world-seed", seedFieldRect, 16, Color.White,
	new Color(90, 110, 150, 220), new Color(230, 200, 100, 255), 12, placeholder: "Aléatoire");

if (Raylib.IsMouseButtonPressed(MouseButton.Left))
{
if (seedFieldHover)
{
_seedFieldFocused = true;
}
else if (!Raylib.CheckCollisionPointRec(mousePos, weaponsToggleRect) && !Raylib.CheckCollisionPointRec(mousePos, foodToggleRect))
{
_seedFieldFocused = false;
}
}

if (_seedFieldFocused)
{
if (!TextInput.IsFocused("world-seed"))
TextInput.Focus("world-seed", _newWorldSeed.Length);
TextInput.Update(ref _newWorldSeed, "world-seed", seedFieldRect, 11, c => c == '-' || char.IsDigit(c));
}
					}
					break;
			}

			// ===================================================
			// 4. BOUTON "CRÉER ET JOUER"
			// ===================================================
			int btnW = Scale(240), btnH = Scale(52);
			int btnX = centerX - btnW / 2;
			int btnY = sh - Scale(90);
			Rectangle createBtn = new Rectangle(btnX, btnY, btnW, btnH);
			bool createHover = Raylib.CheckCollisionPointRec(mousePos, createBtn);
			//  En mode "rejoindre", le nom du personnage est optionnel (LocalCharacters lui
			// donnera un nom par défaut) : le bouton reste donc toujours activé.
			bool canCreate = _isJoinCharacterCreation || _newWorldName.Length > 0;

			Color createColor = canCreate ? (createHover ? new Color(100, 180, 100, 255) : new Color(80, 140, 80, 255)) : new Color(50, 70, 50, 255);
			Raylib.DrawRectangleRounded(createBtn, 0.25f, 8, createColor);
			Raylib.DrawRectangleRoundedLines(createBtn, 0.25f, 8, 2, canCreate ? new Color(100, 200, 100, 200) : new Color(80, 100, 80, 100));

			string btnText = canCreate ? (_isJoinCharacterCreation ? Localization.Get("menu.rejoin") : Localization.Get("menu.create_and_play")) : "ENTREZ UN PRÉNOM";
			int btnTextW = FontManager.MeasureText(btnText, 20);
			DrawCreationText(btnText, (int)(createBtn.X + btnW / 2 - btnTextW / 2), (int)(createBtn.Y + Scale(16)), Scale(20));

			if (canCreate && createHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				if (_isJoinCharacterCreation)
				{
					ConfirmJoinCharacterCreation();
				}
				else
				{
					// Appliquer les changements de personnalisation
					ApplyCharacterCustomization(_tempSkinColor, _tempHairStyle, _tempBeardStyle, _tempHairColor, _tempEyeStyle, _tempEyeColor);
					if (TryStartNewGame(_newWorldName))
					{
						_isInWorldCreation = false;
						_currentSaveName = _newWorldName;
						_gameState = GameState.Playing;
					}
				}
			}

			// ===================================================
			// 5. TOUCHE ECHAP POUR REVENIR EN ARRIÈRE
			// ===================================================
			if (Raylib.IsKeyPressed(KeyboardKey.Escape))
			{
				if (_isJoinCharacterCreation)
				{
					// Retour à l'écran de choix du personnage plutôt qu'au menu principal :
					// IP/pseudo/port et la liste des personnages connus restent valides.
					_isJoinCharacterCreation = false;
					_creationError = "";
					_newWorldName = "";
					_isCharacterSelect = true;
					_isMultiplayerMenu = true;
				}
				else
				{
					_isInWorldCreation = false;
					_gameState = GameState.MainMenu;
					_creationError = "";
					_newWorldName = "";
					_newWorldSeed = "";
				}
			}
		}

		//  MULTIJOUEUR : appelé depuis l'écran de personnalisation (DrawCreateWorldMenu, en
		// mode _isJoinCharacterCreation) une fois que le joueur a validé l'apparence de son
		// nouveau personnage. Applique la customisation, crée et enregistre localement le
		// personnage pour cet hôte, puis lance réellement la connexion — jusqu'ici on
		// n'avait fait qu'ouvrir l'écran de personnalisation (voir le handler de clic
		// "+ Nouveau personnage" plus haut dans UpdateMultiplayerMenu).
		static void ConfirmJoinCharacterCreation()
		{
			ApplyCharacterCustomization(_tempSkinColor, _tempHairStyle, _tempBeardStyle, _tempHairColor, _tempEyeStyle, _tempEyeColor);

			string hostKey = $"{_pendingJoinIp}:{_pendingJoinPort}";
			string label = string.IsNullOrWhiteSpace(_newWorldName) ? $"Personnage {_joinCharacterOptions.Count + 1}" : _newWorldName.Trim();
			var created = LocalCharacters.CreateNew(hostKey, _pendingJoinWorldName, label);
			UseCharacterGuidForNextJoin(created.Guid);

			LocalPlayerName = string.IsNullOrWhiteSpace(_mpPseudoInput) ? "Joueur" : _mpPseudoInput;
			NetworkManager.JoinHostAsync(_pendingJoinIp, _pendingJoinPort, LocalPlayerName);
			SavedServers.AddOrUpdate(_pendingJoinIp, _pendingJoinPort);

			_isJoinCharacterCreation = false;
			_creationError = "";
			_newWorldName = "";
			_isMultiplayerMenu = true;
			_mpWaitingForWorld = true;
			_mpStatusMessage = "Connexion en cours...";
		}

		static void EnsureWorldRevealShader()
		{
			if (_worldRevealShaderLoaded) return;
			_worldRevealShaderLoaded = true;
			_worldRevealShader = Raylib.LoadShaderFromMemory(null, WorldRevealFragmentShader);
			if (_worldRevealShader.Id == 0)
			{
				Console.WriteLine("[Loading] Impossible de charger le shader de révélation du monde.");
				return;
			}
			_worldRevealProgressLocation = Raylib.GetShaderLocation(_worldRevealShader, "revealProgress");
		}

		static void Draw(Vector2 playerPos, float facing, Vector2 hitboxDim, Camera2D camera,
                 int craftSelected, bool showDebug)
		{
			Raylib.BeginDrawing();
			if (_gameState == GameState.Loading)
			{
				DrawLoadingScreen(playerPos, facing, hitboxDim, camera, craftSelected, showDebug);
			}
			else if (_isSelectingSave) DrawCreateWorldMenu();
			else if (_isInWorldCreation) DrawCreateWorldMenu();
			else if (_isJoinCharacterCreation) DrawCreateWorldMenu();
			else if (_isMultiplayerMenu)
			{
				Raylib.ClearBackground(Color.Black);
				DrawMultiplayerMenu();
			}
			else if (_gameState == GameState.MainMenu || _gameState == GameState.Options || _gameState == GameState.Achievements)
			{
				Raylib.ClearBackground(Color.Black);
				if (_gameState == GameState.MainMenu) DrawMainMenu();
				else if (_gameState == GameState.Options) DrawOptionsMenu();
				else if (_gameState == GameState.Achievements) DrawAchievementsMenu();
			}
			else if (_gameState == GameState.Map) DrawMap();
			else
			{
				Raylib.ClearBackground(new Color(135, 206, 235, 255));
				bool captureWorldForReveal = _fadeOutOverlayActive && _worldRevealAlpha < 1f;
				if (captureWorldForReveal)
				{
					if (_menuRevealTexture.Id != 0)
					{
						Rectangle menuSource = new Rectangle(0, 0, ScreenWidth, -ScreenHeight);
						Rectangle menuDestination = new Rectangle(0, 0, ScreenWidth, ScreenHeight);
						byte menuAlpha = (byte)Math.Clamp((1f - _worldRevealAlpha) * 255f, 0f, 255f);
						Raylib.DrawTexturePro(_menuRevealTexture.Texture, menuSource, menuDestination, Vector2.Zero, 0f,
							new Color((byte)255, (byte)255, (byte)255, menuAlpha));
					}
					if (_worldRevealTexture.Id == 0 || _worldRevealTexture.Texture.Width != ScreenWidth || _worldRevealTexture.Texture.Height != ScreenHeight)
					{
						if (_worldRevealTexture.Id != 0) Raylib.UnloadRenderTexture(_worldRevealTexture);
						_worldRevealTexture = Raylib.LoadRenderTexture(ScreenWidth, ScreenHeight);
					}
					Raylib.BeginTextureMode(_worldRevealTexture);
					Raylib.ClearBackground(new Color((byte)0, (byte)0, (byte)0, (byte)0));
				}
					ApplyPreciseCameraTransform(ref camera);
					Camera2D shakenCamera = camera;
				shakenCamera.Target += _shakeOffset;
				Raylib.BeginMode2D(shakenCamera);
				_currentCamera = camera;
				Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
				(int hoverTileX, int hoverTileY, _, _) = FindTileUnderMouse(mouseWorldPos, TileSize);
				
				if (_isChargingThrow && _predictedThrowLanding.HasValue)
				{
					Vector2 pos = _predictedThrowLanding.Value;
					float pulse = 0.7f + 0.3f * MathF.Sin((float)Raylib.GetTime() * 8f);
					float chargePercent = _throwCharge / MAX_THROW_CHARGE;
					Color indicatorColor = new Color(255, 100 + (int)(100 * chargePercent), 50, (int)(180 * pulse));
					
					Raylib.DrawCircle((int)pos.X, (int)pos.Y, 12, indicatorColor);
					Raylib.DrawCircleLines((int)pos.X, (int)pos.Y, 12, Color.White);
					
					Raylib.DrawLine((int)pos.X - 8, (int)pos.Y, (int)pos.X + 8, (int)pos.Y, Color.White);
					Raylib.DrawLine((int)pos.X, (int)pos.Y - 8, (int)pos.X, (int)pos.Y + 8, Color.White);
					
					int chargeX = (int)pos.X - 20;
					int chargeY = (int)pos.Y - 25;
					Raylib.DrawRectangle(chargeX, chargeY, 40, 6, new Color(50, 50, 50, 180));
					Raylib.DrawRectangle(chargeX, chargeY, (int)(40 * chargePercent), 6, new Color(255, 100, 50, 220));
				}
				
				if (_isChargingFishing && _predictedFishingLanding.HasValue)
				{
					Vector2 pos = _predictedFishingLanding.Value;
					float pulse = 0.7f + 0.3f * MathF.Sin((float)Raylib.GetTime() * 8f);
					float chargePercent = _fishingCharge / MAX_FISHING_CHARGE;
					Color indicatorColor = new Color(100, 200, 255, (int)(180 * pulse));
					Raylib.DrawCircle((int)pos.X, (int)pos.Y, 12, indicatorColor);
					Raylib.DrawCircleLines((int)pos.X, (int)pos.Y, 12, Color.White);
					Raylib.DrawLine((int)pos.X - 8, (int)pos.Y, (int)pos.X + 8, (int)pos.Y, Color.White);
					Raylib.DrawLine((int)pos.X, (int)pos.Y - 8, (int)pos.X, (int)pos.Y + 8, Color.White);
					int chargeX = (int)pos.X - 20;
					int chargeY = (int)pos.Y - 25;
					Raylib.DrawRectangle(chargeX, chargeY, 40, 6, new Color(50, 50, 50, 180));
					Raylib.DrawRectangle(chargeX, chargeY, (int)(40 * chargePercent), 6, new Color(100, 200, 255, 220));
				}
				
				Item? heldItem = null;
				bool isCarryingFurniture = _carriedFurniture != null;
				int? carriedPlaceableId = _carriedFurniture?.PlaceableId;

				if (_carriedFurniture != null)
				{
					if (GameData.ItemDatabase.TryGetValue(_carriedFurniture.ItemId, out var itemData))
					{
						heldItem = new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
					}
				}
				else
				{
					var hotbarSlots = GetHotbarSlotsCached(); //  OPTIM : cache par frame
					if (hotbarSlot < hotbarSlots.Count && !hotbarSlots[hotbarSlot].IsEmpty)
						heldItem = hotbarSlots[hotbarSlot].Item;
				}
				
				if (showDebug)
				{
					foreach (var car in Program.Cars)
					{
						car.DrawDebugHitbox();
					}
				}
				
				bool isCarrying = false;
				if (heldItem != null)
				{
					int id = GetItemId(heldItem.Name);
					if (GameData.ItemDatabase.TryGetValue(id, out var data) && data.Type == ItemType.Placeable) isCarrying = true;
				}
				
				List<LocalPlayer> remotePlayersToRender = null;
				if (NetworkManager.IsOnline)
				{
					remotePlayersToRender = new List<LocalPlayer>();
					foreach (var rp in NetworkManager.GetRemotePlayersAsLocalPlayers())
					{
						bool isDeadNow = rp.HP <= 0;
						bool wasDeadBefore = _remoteWasDead.TryGetValue(rp.Id, out var wd) && wd;
						if (isDeadNow && !wasDeadBefore)
						{
							//  Même explosion de particules bleues que pour le joueur local :
							// on la voit désormais nous aussi, au lieu de le voir rester debout.
							SpawnPlayerDeathEffect(GetPlayerVisualPosition(rp.Position));
						}
						_remoteWasDead[rp.Id] = isDeadNow;
						if (isDeadNow) continue; //  masqué tant qu'il n'est pas ressuscité (HP redevenu > 0)
						if (rp.Underground != World.IsUnderground) continue;
						remotePlayersToRender.Add(rp);
					}
				}

				// Offrandes du cercle de rituel : injectées dans le tri par Y du monde pour apparaître
				// comme de vrais objets posés au sol (devant/derrière selon la profondeur), sans "case".
				RitualCircleUI.QueueGroundItems();

				//  DEBUG F3 : mesure le temps passé dans tout le rendu du monde (tuiles, tri par
				// Y, dessin des entités...), à comparer au temps IA mesuré plus haut. Voir PerfStats.cs.
				PrepareLightGridForWorldDraw(camera);
				PerfStats.StartSection(PerfStats.Section.WorldDraw);
				World.DrawWorld(playerPos, camera, _animState, _animFrame, _animProg, facing, hitboxDim, SkinColor,
					missingTexture, destroyedObjects, objectHPs, entities, tileTextures,
					hairBaseTextures, hairOverlayTextures, mouseWorldPos, hoverTileX, hoverTileY, _showDebug,
					isCarrying, _isPlayerInWater, _headAngle, _isSitting ? _sittingPosition : null, _playerDepth,
					playersToRender: remotePlayersToRender, spellProjectiles: _spellProjectiles,
					drawPlayer: !captureWorldForReveal || _worldRevealAlpha >= 0.82f);
				PerfStats.EndSection(PerfStats.Section.WorldDraw);
				//  PORTAGE DE CRÉATURES : dessinée désormais à l'intérieur même de World.cs / EntityRenderer,
				// entre le bras gauche et le bras droit du joueur, pour un rendu correctement superposé.

				// ==================== APERÇU DE SÉLECTION DE STRUCTURE (F7/F8) ====================
				// Affiche un point au centre de CHAQUE tuile qui sera effectivement incluse dans la
				// sauvegarde (SaveStructure), pour repérer immédiatement un décalage d'1 tuile comme
				// celui qui a fait "manger" la colonne de mur gauche de certaines structures : le point
				// est dessiné avec le même calcul de hauteur/offset que le rendu du monde, donc s'il
				// manque un point sur une tuile de mur, ou si le rectangle ne colle pas au mur, c'est
				// visible avant même d'appuyer sur F8.
				if (_selectionCorner1.HasValue)
				{
					int c1x = _selectionCorner1.Value.X;
					int c1y = _selectionCorner1.Value.Y;
					// Tant que le second coin n'est pas posé (F8), on prévisualise avec la tuile
					// survolée par la souris : le rectangle affiché est EXACTEMENT celui qui serait
					// sauvegardé si on appuyait sur F8 maintenant.
					int c2x = _selectionCorner2?.X ?? hoverTileX;
					int c2y = _selectionCorner2?.Y ?? hoverTileY;

					int selMinX = Math.Min(c1x, c2x);
					int selMaxX = Math.Max(c1x, c2x);
					int selMinY = Math.Min(c1y, c2y);
					int selMaxY = Math.Max(c1y, c2y);

					for (int sx = selMinX; sx <= selMaxX; sx++)
					{
						for (int sy = selMinY; sy <= selMaxY; sy++)
						{
							int h = World.GetHeightAt(sx, sy);
							float yOff = -h * TileSize / 4f;
							float centerX = sx * TileSize + TileSize / 2f;
							float centerY = sy * TileSize + yOff + TileSize / 2f;

							bool isFirstCorner = (sx == c1x && sy == c1y);
							bool isPerimeter = (sx == selMinX || sx == selMaxX || sy == selMinY || sy == selMaxY);

							Color dotColor = isFirstCorner
								? Color.Lime
								: (isPerimeter ? new Color(255, 210, 0, 230) : new Color(255, 255, 0, 130));
							int radius = isFirstCorner ? 5 : (isPerimeter ? 4 : 2);

							Raylib.DrawCircle((int)centerX, (int)centerY, radius, dotColor);
						}
					}

					// Contour du rectangle exact (en coordonnées tuiles -> monde), pour visualiser
					// sans ambiguïté les bords min/max réellement utilisés par SaveStructure.
					float rectX = selMinX * TileSize;
					float rectY = selMinY * TileSize;
					float rectW = (selMaxX - selMinX + 1) * TileSize;
					float rectH = (selMaxY - selMinY + 1) * TileSize;
					Raylib.DrawRectangleLinesEx(new Rectangle(rectX, rectY, rectW, rectH), 2f, new Color(255, 255, 0, 200));
				}

				//  OMBRE AU SOL DU GULPER (boss volant) : reste ancrée au sol pendant tout le vol
				// (contrairement au sprite, qui monte/descend avec _guHeight), et se resserre /
				// s'assombrit à mesure qu'il se rapproche du sol, pour bien lire sa trajectoire de chute.
				foreach (var gulperEnt in entities)
				{
					if (!gulperEnt.IsAlive || !gulperEnt.IsGulper) continue;
					if (!World.ShouldRenderEntity(gulperEnt.WorldPos)) continue;
					Vector2 groundPos = new Vector2(gulperEnt.WorldPos.X, gulperEnt.WorldPos.Y + gulperEnt.GulperHeight);
					float heightRatio = Math.Clamp(gulperEnt.GulperHeight / 85f, 0f, 1f); // 85 = GU_HOVER_HEIGHT
					float shadowW = MathHelper.Lerp(46f, 30f, heightRatio);
					float shadowH = shadowW * 0.35f;
					byte shadowAlpha = (byte)MathHelper.Lerp(150f, 60f, heightRatio);
					Raylib.DrawEllipse((int)groundPos.X, (int)groundPos.Y, shadowW / 2f, shadowH / 2f, new Color((byte)0, (byte)0, (byte)0, shadowAlpha));
				}
				
				// ==================== DÉGÂTS FLOTTANTS (CORRIGÉ AVEC CONVERSION ÉCRAN) ====================
				for (int i = _floatingDamages.Count - 1; i >= 0; i--)
				{
					var dmg = _floatingDamages[i];
					
					// Vérifier validité
					if (float.IsNaN(dmg.Position.X) || float.IsNaN(dmg.Position.Y) ||
						float.IsInfinity(dmg.Position.X) || float.IsInfinity(dmg.Position.Y))
					{
						_floatingDamages.RemoveAt(i);
						continue;
					}
					
					// On est encore DANS BeginMode2D ici : on dessine directement en coordonnées MONDE,
					// il ne faut pas convertir en écran (sinon la caméra transforme deux fois la position).
					float alpha = Math.Clamp(dmg.Timer / dmg.MaxTimer, 0f, 1f);
					byte alphaByte = (byte)(alpha * 255);
					Color drawColor = new Color(dmg.Color.R, dmg.Color.G, dmg.Color.B, alphaByte);
					float yOffset = Math.Clamp(dmg.FloatOffset, -80f, 0f);
					int fontSize = Math.Clamp(20 + (dmg.Damage / 5), 18, 28);
					
					bool isHeal = (dmg.Color.G > 150 && dmg.Color.R < 150);
					string displayText = isHeal ? $"+{dmg.Damage}" : $"-{dmg.Damage}";
					
					int textX = (int)dmg.Position.X - 15;
					int textY = (int)dmg.Position.Y - 30 + (int)yOffset;
					
					if (dmg.OutlineColor.HasValue)
					{
						//  Contour : on dessine le texte dans la couleur de contour décalé dans les 8 directions, puis la couleur principale par-dessus
						Color oc = dmg.OutlineColor.Value;
						Color outlineColor = new Color(oc.R, oc.G, oc.B, alphaByte);
						for (int ox = -1; ox <= 1; ox++)
						{
							for (int oy = -1; oy <= 1; oy++)
							{
								if (ox == 0 && oy == 0) continue;
								FontManager.DrawText(displayText, textX + ox, textY + oy, fontSize, outlineColor);
							}
						}
					}
					
					// Dessiner le texte avec les coordonnées monde (cohérent avec le reste du bloc BeginMode2D)
					FontManager.DrawText(displayText, textX, textY, fontSize, drawColor);
				}
				// ==================== FIN DÉGÂTS FLOTTANTS ====================
				
				foreach (var particle in _particles)
				{
					if (particle is MagicExplosionEffect explosion)
					{
						explosion.Draw();
					}
					else if (particle is SlashEffect slash)
					{
						slash.Draw();
					}
					else if (particle is PoisonCloudParticle poisonCloud)
					{
						poisonCloud.Draw();
					}
					else if (particle is ZzzParticle zzz)
					{
						zzz.Draw();
					}
					else if (particle is LevelUpStreakParticle streak)
					{
						streak.Draw();
					}
					else if (particle is LeafParticle leaf)
					{
						leaf.Draw(TreeLeafTexture);
					}
					else
					{
						byte alpha = (byte)(particle.GetAlpha() * 255);
						Color drawColor = new Color(particle.Color.R, particle.Color.G, particle.Color.B, alpha);
						
						// Toutes les particules utilisent des rectangles
						Raylib.DrawRectangle((int)(particle.Position.X - particle.Size / 2), 
											(int)(particle.Position.Y - particle.Size / 2), 
											(int)particle.Size, (int)particle.Size, drawColor);
					}
				}

				//  Boucliers rotatifs autour de l'oeil (remplace l'ancienne barre de vie du boss).
				DrawEyeBossShields();

				//  Les projectiles magiques sont désormais dessinés à l'intérieur de World.DrawWorld,
				// intégrés au tri par Y avec les tuiles et les entités (voir spellProjectiles: ... plus haut).

				if (PlayerCurrentCar == null)
				{
					Item? held = null;
					var slots = GetHotbarSlotsCached(); //  OPTIM : cache par frame
					if (hotbarSlot < slots.Count && !slots[hotbarSlot].IsEmpty) held = slots[hotbarSlot].Item;
					if (held != null)
					{
						var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == held.Name);
						itemData.PlaceableId = ResolvePlaceableId(held, itemData);
						if (itemData.ID != 0 && (itemData.Type == ItemType.Placeable || itemData.IsSeed))
						{
							DrawPlacementGhost(camera, held, itemData);
						}
					}
				}
				
				foreach (var arrow in _arrows) arrow.Draw();
				foreach (var bullet in _bullets) bullet.Draw();
				foreach (var throwable in _throwables) throwable.Draw();

				if (_currentFishingProjectile != null) _currentFishingProjectile.Draw();
				
	
				//  MÉTÉO : mise à jour puis rendu de la pluie, DANS BeginMode2D pour qu'elle
				// suive le monde (zoom/scroll) et soit dessinée par-dessus tout ce qui précède,
				// toits inclus (World.DrawWorld a déjà dessiné les toits juste au-dessus).
				Weather.Update(Raylib.GetFrameTime(), camera);
				Weather.Draw(camera);
				
				Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
				int ts = Program.TileSize;

				foreach (var kv in World.SteamEngines)
				{
					var (x, y) = kv.Key;
					var engine = kv.Value;
					
					int height = World.GetHeightAt(x, y);
					float yOffset = -height * ts / 4;
					float drawX = x * ts;
					float drawY = y * ts + yOffset;
					
					var tileData = WorldTileRegistry.GetTile(401);
					int machineTilesW = tileData?.Size?.Width ?? 1;
					int machineTilesH = tileData?.Size?.Height ?? 1;
					if (tileData?.Size != null)
					{
						drawX += tileData.DrawOffset?.X ?? 0;
						drawY += tileData.DrawOffset?.Y ?? 0;
						drawY = drawY - (ts * tileData.Size.Height) + ts;
					}
					
					//  FIX : la zone de survol se basait sur une taille fixe (ts*2) au lieu de la
					// taille réelle de la tuile (worldobjects.json -> "size"), ce qui la désynchronisait
					// dès que la machine n'était plus 2x2 (ou si sa taille venait à changer).
					Rectangle machineRect = new Rectangle(drawX, drawY, ts * machineTilesW, ts * machineTilesH);
					
					if (Raylib.CheckCollisionPointRec(mouseWorld, machineRect))
					{
						_hoveredMachine = (x, y, engine);
					}
				}
				
				foreach (var kv in World.Dynamos)
				{
					int x = kv.Key.x;
					int y = kv.Key.y;
					var dynamo = kv.Value;
					
					int height = World.GetHeightAt(x, y);
					float yOffset = -height * ts / 4;
					float drawX = x * ts;
					float drawY = y * ts + yOffset;
					
					var tileData = WorldTileRegistry.GetTile(403);
					if (tileData?.Size != null)
					{
						drawX += tileData.DrawOffset?.X ?? 0;
						drawY += tileData.DrawOffset?.Y ?? 0;
						drawY = drawY - (ts * tileData.Size.Height) + ts;
					}
					
					if (dynamo.ElectricityOutput > 0 && _gearTexture.Id != 0)
					{
						float gearX = drawX + ts;
						float gearY = drawY - ts / 2f;
						float rotation = (float)Raylib.GetTime() * 180f;
						float scale = 0.7f;
						
						Rectangle srcRect = new Rectangle(0, 0, _gearTexture.Width, _gearTexture.Height);
						Rectangle destRect = new Rectangle(gearX - (ts * scale) / 2, gearY - (ts * scale) / 2, ts * scale, ts * scale);
						Vector2 origin = new Vector2(destRect.Width / 2, destRect.Height / 2);
						
						Raylib.DrawTexturePro(_gearTexture, srcRect, destRect, origin, rotation, Color.White);
					}
					
					Rectangle dynamoRect = new Rectangle(drawX, drawY, ts * 2, ts * 2);
					
					if (Raylib.CheckCollisionPointRec(mouseWorld, dynamoRect))
					{
						int tooltipW = 200, tooltipH = 75;
						Vector2 mouseScreen = Raylib.GetMousePosition();
						int tooltipX = (int)mouseScreen.X + 15;
						int tooltipY = (int)mouseScreen.Y + 15;
						
						if (tooltipX + tooltipW > Raylib.GetScreenWidth())
							tooltipX = (int)mouseScreen.X - tooltipW - 15;
						if (tooltipY + tooltipH > Raylib.GetScreenHeight())
							tooltipY = (int)mouseScreen.Y - tooltipH - 15;
						
						Raylib.DrawRectangleRounded(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, new Color(20, 20, 30, 220));
						Raylib.DrawRectangleRoundedLines(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, 1, new Color(210, 180, 100, 150));
						
						FontManager.DrawText(Localization.Get("tooltip.dynamo"), tooltipX + 8, tooltipY + 6, 12, new Color(210, 180, 100, 255));
						FontManager.DrawText($"Vapeur reçue: {dynamo.SteamInput:F0} kW", tooltipX + 8, tooltipY + 24, 11, Color.White);
						FontManager.DrawText($"Production: {dynamo.ElectricityOutput:F0} kW", tooltipX + 8, tooltipY + 40, 11, 
							dynamo.ElectricityOutput > 0 ? new Color(100, 255, 100, 255) : new Color(200, 200, 100, 255));
						FontManager.DrawText($" Connexions: {dynamo.Connections.Count}", tooltipX + 8, tooltipY + 56, 10, new Color(150, 150, 150, 255));
						
						if (!_isConnectingWire)
						{
							FontManager.DrawText(Localization.Get("tooltip.press_e_to_connect"), tooltipX + 8, tooltipY + 68, 9, new Color(100, 200, 255, 200));
						}
						else if (_wireStartTile == (-1, -1))
						{
							FontManager.DrawText(Localization.Get("tooltip.click_battery"), tooltipX + 8, tooltipY + 68, 9, new Color(255, 200, 100, 200));
						}
					}
				}

				// ====== CHARGEURS DE BATTERIE : détection du survol (infobulle dessinée après EndMode2D) ======
				foreach (var kv in World.BatteryChargers)
				{
					int x = kv.Key.x;
					int y = kv.Key.y;
					var charger = kv.Value;

					if (World.GetObjectIdAt(x, y) != 405) continue;

					int height = World.GetHeightAt(x, y);
					float yOffset = -height * ts / 4;
					float drawX = x * ts;
					float drawY = y * ts + yOffset;

					var tileData = WorldTileRegistry.GetTile(405);
					if (tileData?.Size != null)
					{
						drawX += tileData.DrawOffset?.X ?? 0;
						drawY += tileData.DrawOffset?.Y ?? 0;
						drawY = drawY - (ts * tileData.Size.Height) + ts;
					}

					Rectangle chargerRect = new Rectangle(drawX, drawY, ts * (tileData?.Size?.Width ?? 2), ts * (tileData?.Size?.Height ?? 1));

					if (Raylib.CheckCollisionPointRec(mouseWorld, chargerRect))
					{
						_hoveredBatteryCharger = (x, y, charger);
					}
				}

				// ====== COLLECTEURS DE PLUIE : survol + jauge au-dessus de la tuile ======
				foreach (var kv in World.RainCollectors)
				{
					int x = kv.Key.x;
					int y = kv.Key.y;
					var rc = kv.Value;

					// Si la tuile n'est plus un collecteur, on l'ignore (sera nettoyé ailleurs)
					if (World.GetObjectIdAt(x, y) != 116) continue;

					int height = World.GetHeightAt(x, y);
					float yOffset = -height * ts / 4;
					float drawX = x * ts;
					float drawY = y * ts + yOffset;

					var tileData = WorldTileRegistry.GetTile(116);
					if (tileData?.Size != null)
					{
						drawX += tileData.DrawOffset?.X ?? 0;
						drawY += tileData.DrawOffset?.Y ?? 0;
						drawY = drawY - (ts * tileData.Size.Height) + ts;
					}

					Rectangle rect = new Rectangle(drawX, drawY, ts * (tileData?.Size?.Width ?? 1), ts * (tileData?.Size?.Height ?? 1));

					if (Raylib.CheckCollisionPointRec(mouseWorld, rect))
					{
						_hoveredRainCollector = (x, y, rc);

						// Dessiner une petite jauge au-dessus de la tuile (coordonnées MONDE)
						float barW = Math.Min(48, ts);
						float barH = 6f;
						float barX = drawX + rect.Width / 2f - barW / 2f;
						float barY = drawY - 12f - barH;
						Raylib.DrawRectangle((int)barX, (int)barY, (int)barW, (int)barH, new Color(40, 40, 50, 200));
						float frac = rc.Capacity > 0 ? Math.Min(1f, rc.CurrentWater / rc.Capacity) : 0f;
						Raylib.DrawRectangle((int)barX, (int)barY, (int)(barW * frac), (int)barH, new Color(50, 150, 255, 220));
						FontManager.DrawText($"{(int)rc.CurrentWater}L / {(int)rc.Capacity}L", (int)barX, (int)(barY - 14f), 10, Color.White);
					}
				}

				//  CERCLE DE RITUEL : offrandes + animation d'invocation, dessinées ici en
				// coordonnées MONDE (comme les jauges ci-dessus) pour que ça fasse partie du
				// rendu du jeu et non une interface plaquée par-dessus.
				RitualCircleUI.Draw();

				Raylib.EndMode2D();
				if (captureWorldForReveal)
				{
					Raylib.EndTextureMode();
					Rectangle source = new Rectangle(0, 0, ScreenWidth, -ScreenHeight);
					Rectangle destination = new Rectangle(0, 0, ScreenWidth, ScreenHeight);
					EnsureWorldRevealShader();
					if (_worldRevealShader.Id != 0 && _worldRevealProgressLocation >= 0)
					{
						Raylib.SetShaderValue(_worldRevealShader, _worldRevealProgressLocation, _worldRevealAlpha, ShaderUniformDataType.Float);
						Raylib.BeginShaderMode(_worldRevealShader);
					}
					Raylib.DrawTexturePro(_worldRevealTexture.Texture, source, destination, Vector2.Zero, 0f, Color.White);
					if (_worldRevealShader.Id != 0 && _worldRevealProgressLocation >= 0)
						Raylib.EndShaderMode();

					if (!string.IsNullOrEmpty(_pendingLoadSaveName) && _carouselPreviewCache.ContainsKey(_pendingLoadSaveName))
					{
						Camera2D transitionCamera = camera;
						transitionCamera.Offset = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f);
						transitionCamera.Target = GetPlayerCameraTarget(_playerPos);
						Vector2 characterPosition = Raylib.GetWorldToScreen2D(GetPlayerCameraTarget(_playerPos), transitionCamera);
						float characterScale = Math.Max(0.01f, camera.Zoom);
						if (_worldRevealAlpha < 0.82f)
						{
							DrawClassicSavePreview(_pendingLoadSaveName, characterPosition, characterScale,
								Math.Max(80f, ScreenHeight * 0.18f), 1f, _animFrame, _animProg);
						}
					}
				}

				//  ÉCRAN DE MORT : vignette -> pop du crâne -> message qui s'écrit -> bouton
				// "Réapparaître" (voir DrawDeathScreen / UpdateDeathScreen). Retiré d'ici et
				// dessiné tout en bas, juste avant EndDrawing, pour être garanti au-dessus de
				// TOUT le reste (lumière jour/nuit, température, HUD, notifications...).
				
				if (_gameState == GameState.Playing || _gameState == GameState.Paused)
				{
					Weather.DrawScreenTint();
				}

				if (_gameState == GameState.Playing || _gameState == GameState.Paused)
				{
					DrawLighting(camera);
					//  Léger voile de couleur (bleu-violet la nuit, rose saumon à l'aube,
					// orange au coucher de soleil), dessiné après le masque de lumière comme
					// une couche cosmétique en plus, pas un remplacement.
					DrawDayNightColorFilter();
				}

				// Jauge de charge pour le clic gauche
				if (_isChargingLeftClick && PlayerCurrentCar == null && !InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen)
				{
					Vector2 playerScreen = Raylib.GetWorldToScreen2D(GetPlayerVisualPosition(_playerPos), camera);
					float radius = 20f + _leftClickCharge * 40f;
					Color spellColor = _leftClickSpell.GetElementColor();
					Raylib.DrawCircleLines((int)playerScreen.X, (int)playerScreen.Y, radius, spellColor);
					int barWidth = 100;
					int barX = (int)playerScreen.X - barWidth / 2;
					int barY = (int)playerScreen.Y - 60;
					Raylib.DrawRectangle(barX, barY, barWidth, 8, new Color(50, 50, 50, 200));
					Raylib.DrawRectangle(barX, barY, (int)(barWidth * _leftClickCharge), 8, spellColor);
				}
				// Jauge de charge pour le clic droit
				if (_isChargingRightClick && PlayerCurrentCar == null && !InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen)
				{
					Vector2 playerScreen = Raylib.GetWorldToScreen2D(GetPlayerVisualPosition(_playerPos), camera);
					float radius = 20f + _rightClickCharge * 40f;
					Color spellColor = _rightClickSpell.GetElementColor();
					Raylib.DrawCircleLines((int)playerScreen.X, (int)playerScreen.Y, radius, spellColor);
					int barWidth = 100;
					int barX = (int)playerScreen.X - barWidth / 2;
					int barY = (int)playerScreen.Y - 80; // décalé pour éviter superposition
					Raylib.DrawRectangle(barX, barY, barWidth, 8, new Color(50, 50, 50, 200));
					Raylib.DrawRectangle(barX, barY, (int)(barWidth * _rightClickCharge), 8, spellColor);
				}
				
				if (_hoveredMachine.HasValue)
				{
					var (x, y, engine) = _hoveredMachine.Value;
					
					int screenWidth = Raylib.GetScreenWidth();
					int screenHeight = Raylib.GetScreenHeight();
					Vector2 mouseScreen = Raylib.GetMousePosition();
					
					int tooltipW = 205, tooltipH = 190;
					int tooltipX = (int)mouseScreen.X + 15;
					int tooltipY = (int)mouseScreen.Y + 15;
					
					if (tooltipX + tooltipW > screenWidth)
						tooltipX = (int)mouseScreen.X - tooltipW - 15;
					if (tooltipY + tooltipH > screenHeight)
						tooltipY = (int)mouseScreen.Y - tooltipH - 15;
					
					Raylib.DrawRectangleRounded(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, new Color(20,20,30,220));
					Raylib.DrawRectangleRoundedLines(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, 1, new Color(210,180,100,150));
					
					FontManager.DrawText(Localization.Get("tooltip.steam_machine"), tooltipX + 10, tooltipY + 8, 14, new Color(235, 200, 110, 255));
					string status = engine.IsOn ? "EN MARCHE" : "ARRETEE";
					FontManager.DrawText(status, tooltipX + 10, tooltipY + 29, 10,
						engine.IsOn ? new Color(100,255,100,255) : new Color(255,100,100,255));

					if (_tooltipWaterIcon.Id != 0)
						Raylib.DrawTexturePro(_tooltipWaterIcon, new Rectangle(0, 0, _tooltipWaterIcon.Width, _tooltipWaterIcon.Height),
							new Rectangle(tooltipX + 10, tooltipY + 49, 28, 28), Vector2.Zero, 0, Color.White);
					FontManager.DrawText($"Eau: {(int)(engine.Water * 100f)} cL / {(int)(SteamEngineData.MaxWater * 100f)} cL", tooltipX + 46, tooltipY + 51, 11, Color.White);
					Raylib.DrawRectangle(tooltipX + 46, tooltipY + 68, 145, 7, new Color(40,40,50,255));
					Raylib.DrawRectangle(tooltipX + 46, tooltipY + 68, (int)(145 * engine.Water / SteamEngineData.MaxWater), 7, new Color(50,150,255,255));

					if (_tooltipFireIcon.Id != 0)
						Raylib.DrawTexturePro(_tooltipFireIcon, new Rectangle(0, 0, _tooltipFireIcon.Width, _tooltipFireIcon.Height),
							new Rectangle(tooltipX + 10, tooltipY + 86, 28, 28), Vector2.Zero, 0, Color.White);
					FontManager.DrawText($"Charbon: {(int)engine.Coal} / {(int)SteamEngineData.MaxCoal}", tooltipX + 46, tooltipY + 88, 11, Color.White);
					Raylib.DrawRectangle(tooltipX + 46, tooltipY + 105, 145, 7, new Color(40,40,50,255));
					Raylib.DrawRectangle(tooltipX + 46, tooltipY + 105, (int)(145 * engine.Coal / SteamEngineData.MaxCoal), 7, new Color(210,120,55,255));

					FontManager.DrawText($"Pression: {engine.SteamPressure:F0}%", tooltipX + 10, tooltipY + 128, 11, Color.White);
					Raylib.DrawRectangle(tooltipX + 10, tooltipY + 144, 181, 7, new Color(40,40,50,255));
					Raylib.DrawRectangle(tooltipX + 10, tooltipY + 144, (int)(181 * engine.SteamPressure / 100f), 7,
						engine.SteamPressure > 80 ? new Color(255,100,100,255) : new Color(200,200,100,255));
					FontManager.DrawText($"Production: {engine.ProductionRate:F0} kW", tooltipX + 10, tooltipY + 164, 11,
						engine.IsOn ? new Color(100,255,100,255) : new Color(200,200,100,255));
					
					_hoveredMachine = null;
				}

				if (_hoveredBatteryCharger.HasValue)
				{
					var (x, y, charger) = _hoveredBatteryCharger.Value;

					int screenWidth = Raylib.GetScreenWidth();
					int screenHeight = Raylib.GetScreenHeight();
					Vector2 mouseScreen = Raylib.GetMousePosition();

					int tooltipW = 220, tooltipH = charger.InsertedBattery != null ? 96 : 70;
					int tooltipX = (int)mouseScreen.X + 15;
					int tooltipY = (int)mouseScreen.Y + 15;

					if (tooltipX + tooltipW > screenWidth)
						tooltipX = (int)mouseScreen.X - tooltipW - 15;
					if (tooltipY + tooltipH > screenHeight)
						tooltipY = (int)mouseScreen.Y - tooltipH - 15;

					Raylib.DrawRectangleRounded(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, new Color(20, 20, 30, 220));
					Raylib.DrawRectangleRoundedLines(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, 1, new Color(210, 180, 100, 150));

					FontManager.DrawText("Chargeur de batterie", tooltipX + 8, tooltipY + 6, 12, new Color(210, 180, 100, 255));

					string powerStatus = charger.HasPower ? " Alimenté" : " Pas de courant";
					FontManager.DrawText(powerStatus, tooltipX + 8, tooltipY + 24, 11,
						charger.HasPower ? new Color(100, 255, 100, 255) : new Color(200, 100, 100, 255));

					if (charger.InsertedBattery != null)
					{
						var battery = charger.InsertedBattery;
						float frac = battery.MaxCapacity > 0 ? Math.Min(1f, battery.StoredEnergy / battery.MaxCapacity) : 0f;

						FontManager.DrawText($" Batterie: {(int)battery.StoredEnergy} / {(int)battery.MaxCapacity} Wh", tooltipX + 8, tooltipY + 40, 11, Color.White);
						Raylib.DrawRectangle(tooltipX + 8, tooltipY + 52, 150, 6, new Color(40, 40, 50, 255));
						Raylib.DrawRectangle(tooltipX + 8, tooltipY + 52, (int)(150 * frac), 6,
							frac >= 0.999f ? new Color(100, 255, 100, 255) : new Color(255, 220, 100, 255));

						FontManager.DrawText(" E pour retirer", tooltipX + 8, tooltipY + 64, 9, new Color(200, 200, 200, 200));
					}
					else
					{
						FontManager.DrawText(" Aucune batterie posée", tooltipX + 8, tooltipY + 40, 11, new Color(200, 200, 200, 255));
						FontManager.DrawText(" E pour poser la batterie tenue en main", tooltipX + 8, tooltipY + 54, 9, new Color(150, 200, 255, 200));
					}

					_hoveredBatteryCharger = null;
				}

				if (_hoveredRainCollector.HasValue)
				{
					var (x, y, collector) = _hoveredRainCollector.Value;
					int screenWidth = Raylib.GetScreenWidth();
					int screenHeight = Raylib.GetScreenHeight();
					Vector2 mouseScreen = Raylib.GetMousePosition();

					int tooltipW = 220, tooltipH = 70;
					int tooltipX = (int)mouseScreen.X + 15;
					int tooltipY = (int)mouseScreen.Y + 15;

					if (tooltipX + tooltipW > screenWidth) tooltipX = (int)mouseScreen.X - tooltipW - 15;
					if (tooltipY + tooltipH > screenHeight) tooltipY = (int)mouseScreen.Y - tooltipH - 15;

					Raylib.DrawRectangleRounded(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, new Color(20,20,30,220));
					Raylib.DrawRectangleRoundedLines(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, 1, new Color(100,180,255,150));

					FontManager.DrawText("Collecteur de pluie", tooltipX+8, tooltipY+6, 12, new Color(150,200,255,255));
					FontManager.DrawText($"Eau: {(int)collector.CurrentWater}L / {(int)collector.Capacity}L", tooltipX+8, tooltipY+26, 11, Color.White);
					Raylib.DrawRectangle(tooltipX+8, tooltipY+44, 180, 8, new Color(40,40,50,255));
					float frac = collector.Capacity > 0 ? Math.Min(1f, collector.CurrentWater / collector.Capacity) : 0f;
					Raylib.DrawRectangle(tooltipX+8, tooltipY+44, (int)(180 * frac), 8, new Color(50,150,255,255));
					FontManager.DrawText("Clic droit pour remplir gourde/arrosoir", tooltipX+8, tooltipY+56, 10, new Color(160,160,200,220));

					_hoveredRainCollector = null;
				}
				
				if (showDebug)
				{
					int playerTileX = (int)(playerPos.X / TileSize);
					int playerTileY = (int)(playerPos.Y / TileSize);
					int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
					float playerYOffset = -playerHeight * TileSize / 4;
					float playerBaseY = playerPos.Y + playerYOffset + FeetOffsetY;
					Rectangle playerHitboxWorld = new Rectangle(playerPos.X - hitboxDim.X / 2f, playerBaseY - hitboxDim.Y, hitboxDim.X, hitboxDim.Y);
					Vector2 topLeft = Raylib.GetWorldToScreen2D(new Vector2(playerHitboxWorld.X, playerHitboxWorld.Y), camera);
					Vector2 bottomRight = Raylib.GetWorldToScreen2D(new Vector2(playerHitboxWorld.X + playerHitboxWorld.Width, playerHitboxWorld.Y + playerHitboxWorld.Height), camera);
					Rectangle playerHitbox = new Rectangle(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
					Raylib.DrawRectangleLinesEx(playerHitbox, 2, Color.Lime);
				}
				
				if (_gameState == GameState.Paused) DrawPauseMenu();
				{
					if (_isEmoteWheelOpen)
					{
						DrawEmoteWheel();
					}
					else if (_radialMenu.IsOpen)
					{
						_radialMenu.Draw();
					}
					else
					{
						// plus d'indicateur bas droite ; on affiche l'item dans la bulle.
					}
					Item? hotbarHeldItem = InventoryRenderer.GetHotbarItem(hotbarSlot);
					HudRenderer.Draw(playerHP, GetEffectivePlayerMaxHP(), playerStamina, playerMaxStamina, staminaLocked, playerHunger, playerMaxHunger, playerThirst, playerMaxThirst, hotbarHeldItem);
					if (SettingsManager.Settings.HotbarStyle == "classic")
						HudRenderer.DrawClassicHotbar(hotbarSlot);
					Renderer.UpdateAndDrawNotifications(activeNotifications, Raylib.GetFrameTime());
					DrawPouilleuxInvitation();
					
					if (CurrentBoss != null && CurrentBoss.IsAlive)
					{
						int sw = Raylib.GetScreenWidth();
						int barWidth = 420;
						int barHeight = 28;
						int x = (sw - barWidth) / 2;
						int y = 16;
						float healthPercent = Math.Clamp((float)CurrentBoss.CurrentHP / Math.Max(1, CurrentBoss.MaxHP), 0f, 1f);

						// Fond et bordure (léger cadre pour lisibilité)
						Raylib.DrawRectangleRounded(new Rectangle(x - 2, y - 2, barWidth + 4, barHeight + 4), 0.25f, 8, new Color(0, 0, 0, 180));
						Raylib.DrawRectangleRounded(new Rectangle(x, y, barWidth, barHeight), 0.25f, 8, new Color(30, 20, 20, 240));

						// Calculs pour les ailes et le curseur
						int centerX = sw / 2;
						int cursorSize = (int)(barHeight * 2.0f);
						int wingMaxWidth = (barWidth - cursorSize) / 2;

						// Charger (et cacher) le curseur spécifique au boss si disponible
						string speciesLower = CurrentBoss.Species?.ToLower() ?? "";
						Texture2D cursorTex;
						if (!_bossCursorCache.TryGetValue(speciesLower, out cursorTex))
						{
							var loaded = TryLoad($"assets/gui/{speciesLower}_cursor.png");
							cursorTex = loaded.Id != 0 ? loaded : _defaultBossCursorTexture;
							_bossCursorCache[speciesLower] = cursorTex;
						}

						// Ailes : texture classique (base) puis overlay color qui sera rogné selon la vie
						Texture2D wingBase = _healthBarWingTexture;
						Texture2D wingColor = _healthBarWingColorTexture;

						float leftScale = wingBase.Id != 0 ? (float)wingMaxWidth / wingBase.Width : 1f;
						float wingHeight = wingBase.Id != 0 ? wingBase.Height * leftScale : barHeight;
						// Légère descente pour mieux aligner avec la barre
						float destY = y + barHeight/2f - wingHeight/2f + 4;

						// Draw left base (flipped)
						if (wingBase.Id != 0)
						{
							Rectangle src = new Rectangle(wingBase.Width, 0, -wingBase.Width, wingBase.Height);
							Rectangle dest = new Rectangle(centerX - cursorSize/2 - wingMaxWidth, (int)destY, wingMaxWidth, (int)wingHeight);
							Raylib.DrawTexturePro(wingBase, src, dest, Vector2.Zero, 0, Color.White);
						}

						// Draw right base (normal)
						if (wingBase.Id != 0)
						{
							Rectangle srcR = new Rectangle(0, 0, wingBase.Width, wingBase.Height);
							Rectangle destR = new Rectangle(centerX + cursorSize/2, (int)destY, wingMaxWidth, (int)wingHeight);
							Raylib.DrawTexturePro(wingBase, srcR, destR, Vector2.Zero, 0, Color.White);
						}

						// Overlay color: only draw a portion depending on healthPercent
						Color GetBossFrameColor(string species)
						{
							if (string.Equals(species, "Khamsin", StringComparison.OrdinalIgnoreCase))
								return new Color(240, 200, 80, 255);
							if (string.Equals(species, "Ogre", StringComparison.OrdinalIgnoreCase))
								return new Color(200, 150, 120, 255);
							if (string.Equals(species, "Genie", StringComparison.OrdinalIgnoreCase))
								return new Color(255, 195, 60, 255);
							return Color.White;
						}
						Color frameTint = GetBossFrameColor(CurrentBoss.Species);
						if (wingColor.Id != 0 && healthPercent > 0f)
						{
							int colorPx = (int)(wingColor.Width * healthPercent);
							int destColorW = (int)(wingMaxWidth * healthPercent);

							// Right color (grows to the right from cursor)
							if (destColorW > 0)
							{
								Rectangle srcColorR = new Rectangle(0, 0, colorPx, wingColor.Height);
								Rectangle destColorR = new Rectangle(centerX + cursorSize/2, (int)destY, destColorW, (int)wingHeight);
								Raylib.DrawTexturePro(wingColor, srcColorR, destColorR, Vector2.Zero, 0, frameTint);
							}

							// Left color (grows to the left from cursor) - take rightmost slice of source
							if (destColorW > 0)
							{
								Rectangle srcColorL = new Rectangle(wingColor.Width - colorPx, 0, colorPx, wingColor.Height);
								Rectangle destColorL = new Rectangle(centerX - cursorSize/2 - destColorW, (int)destY, destColorW, (int)wingHeight);
								// Draw flipped horizontally by using negative src width
								Rectangle srcColorLFlipped = new Rectangle(wingColor.Width - colorPx + colorPx, 0, -colorPx, wingColor.Height);
								Raylib.DrawTexturePro(wingColor, srcColorLFlipped, destColorL, Vector2.Zero, 0, frameTint);
							}
						}

						// Draw cursor at center
						if (cursorTex.Id != 0)
						{
							float cursorScale = (float)cursorSize / cursorTex.Width;
							Rectangle srcC = new Rectangle(0, 0, cursorTex.Width, cursorTex.Height);
							float centerY = y + barHeight / 2f;
							Rectangle destC = new Rectangle(centerX - cursorSize/2, (int)(centerY - (cursorTex.Height * cursorScale)/2f), cursorSize, (int)(cursorTex.Height * cursorScale));
							Raylib.DrawTexturePro(cursorTex, srcC, destC, Vector2.Zero, 0, Color.White);
						}

						// Label
						string bossName = CurrentBoss.Species == "Khamsin" ? "Khamsin" : (CurrentBoss.Species == "Ogre" ? "Ogre" : (CurrentBoss.Species == "Genie" ? "Génie de la Lampe" : CurrentBoss.Species));
						string bossLabel = $"{bossName}  •  {CurrentBoss.CurrentHP}/{CurrentBoss.MaxHP} HP";
						int textWidth = FontManager.MeasureText(bossLabel, 18);
						FontManager.DrawText(bossLabel, centerX - textWidth/2, y + 5, 18, Color.White);
					}

					// ---- Indicateur du combat du boss de l'oeil (nom + vague, sans barre de vie : ----
					// ---- l'état est désormais visible via les boucliers tournant autour de l'oeil, voir DrawEyeBossShields) ----
					if (_eyeBossActive)
					{
						int sw2 = Raylib.GetScreenWidth();
						string eyeBossLabel = _eyeBossIntroPlaying
							? "L'Oeil s'éveille..."
							: (_eyeBossWaitingNextWave
								? "L'Oeil  •  Vague suivante..."
								: $"L'Oeil  •  Vague {_eyeBossWaveIndex + 1}/{_eyeBossWaves.Length}");
						int eyeTextWidth = FontManager.MeasureText(eyeBossLabel, 18);
						FontManager.DrawText(eyeBossLabel, sw2 / 2 - eyeTextWidth / 2, 16, 18, Color.White);
					}
					
					SlimeStorageUI.Update();
					if (InventoryRenderer.IsInventoryOpen)
					{
						InventoryRenderer.Update();
						InventoryRenderer.Draw(equipment);
					}
					
					//  MULTIJOUEUR : HUD réseau (coin supérieur droit en jeu)
					if (NetworkManager.IsOnline) DrawNetworkHUD();
					
					ContainerUI.Update();
					DyeingUI.Update();
					CauldronUI.Update();
					
					if (PlayerCurrentCar != null)
					{
						DrawCarDashboard(PlayerCurrentCar);
						DrawCarPedals(PlayerCurrentCar);
					}
					else
					{
						Boat? currentBoat = World.GetBoatAtPosition(_playerPos, TileSize);
						if (currentBoat != null)
						{
							int speedKmph = (int)(currentBoat.Velocity.Length() * 3.6f);
							int sailCount = currentBoat.GetSailCount();
							string boatInfo = $" Voiles: {sailCount} | Vitesse: {speedKmph} km/h";
							if (currentBoat.IsSailAdjusted)
								boatInfo += $" | Cap: {currentBoat.SailAngle:F0}°";
							FontManager.DrawText(boatInfo, 10, 70, 14, new Color(100, 200, 255, 255));
						}
					}
					
					if (showDebug) 
						Renderer.DrawDebugMenu(playerPos, camera.Zoom, entities.Count(e=>e.Species=="human"), entities.Count(e=>e.Species!="human"), Raylib.GetFrameTime(), camera, InventoryRenderer.GetHotbarItem(hotbarSlot));
				}
				
				DrawUIBar();

				// Draw remote player pointers overlay (for players offscreen)
				// remotePlayersToRender may be null if offline
				if (remotePlayersToRender != null)
				{
					EnsureServerPointerLoaded();
					PlayerPointerUI.Draw(_currentCamera, remotePlayersToRender, _serverPointerTex);
				}
			}
			ArmorStandUI.Draw();
			TamedAnimalUI.Draw();
			SlimeStorageUI.Draw();
			TraderUI.Draw();
			TamedAnimalInteraction.Draw(camera);
			QuestDialogUI.Draw();
				CartesGameUI.Draw();
			QuestJournalUI.Draw(entities);
			QuestTrackerUI.Draw(camera, GetQuestNpcs());
			CraftingUI.Draw();
			BestiaryUI.Draw();
			ChatSystem.Draw();
				VoiceChat.Draw();
			GuildUI.Draw();
			ContainerUI.Draw();
			CharacterCustomizationUI.Draw();
			CauldronUI.Draw();
			DyeingUI.Draw();
			InstrumentUI.Draw();
				if (_gameState == GameState.Playing || _gameState == GameState.Paused)
					TemperatureSystem.DrawHUD();
			PortalUI.Draw();
			ItemMenuUI.Draw();
			AchievementManager.Draw();
			SpellCraftingUI.Draw();
			DrawSailAdjustmentIndicator(camera);
			
			// ========== DESSIN DE L'ICÔNE DE SAUVEGARDE ==========
			if (_savingDisplayTimer > 0f && _savingIconTexture.Id != 0)
			{
				int sw = Raylib.GetScreenWidth();
				int sh = Raylib.GetScreenHeight();
				
				// Calcul de l'alpha pour l'effet de fondu
				float alpha = 1f;
				if (_savingDisplayTimer < 0.3f)
					alpha = _savingDisplayTimer / 0.3f; // Fondu de sortie
				else if (_savingDisplayTimer > SAVING_DISPLAY_DURATION - 0.3f)
					alpha = (SAVING_DISPLAY_DURATION - _savingDisplayTimer) / 0.3f; // Fondu d'entrée
				
				byte alphaByte = (byte)(alpha * 255);
				Color tint = new Color((byte)255, (byte)255, (byte)255, alphaByte);
				
				// Position : en bas à droite, avec une marge
				float iconSize = 64f;
				float margin = 20f;
				float posX = sw - iconSize - margin;
				float posY = sh - iconSize - margin;
				
				Rectangle srcRect = new Rectangle(0, 0, _savingIconTexture.Width, _savingIconTexture.Height);
				Rectangle destRect = new Rectangle(posX, posY, iconSize, iconSize);
				
				// Ombre portée
				Raylib.DrawRectangleRounded(new Rectangle(posX + 2, posY + 2, iconSize, iconSize), 0.1f, 6, new Color((byte)0, (byte)0, (byte)0, (byte)(80 * alpha)));
				
				// Icône
				Raylib.DrawTexturePro(_savingIconTexture, srcRect, destRect, Vector2.Zero, 0, tint);
				
				// Petit effet de pulsation
				float pulse = 1f + 0.05f * MathF.Sin((float)Raylib.GetTime() * 8f);
				// On ne modifie pas la taille mais on peut ajouter une légère lueur
				if (alpha > 0.8f)
				{
					Raylib.DrawCircleGradient((int)(posX + iconSize/2), (int)(posY + iconSize/2), (int)(iconSize * 0.6f),
						new Color((byte)255, (byte)255, (byte)255, (byte)(20 * alpha)),
						new Color(255, 255, 255, 0));
				}
			}
			
			
			if (_caveTransitionPhase != CaveTransitionPhase.None)
			{
				DrawCaveTransitionBars();
			}

			if (_mainMenuFadePhase != MainMenuFadePhase.None)
			{
				DrawMainMenuTransitionOverlay();
			}
			else if (_fadeOutOverlayActive)
			{
				DrawLoadingFadeOutOverlay();
			}

			//  ÉCRAN DE MORT : dessiné en tout dernier, par-dessus absolument tout le reste
			// (lumière jour/nuit, température, HUD, notifications, fade...) puisque c'est une
			// interface qui doit rester lisible quel que soit l'éclairage de la scène.
			if (IsPlayerRagdolled)
			{
				DrawDeathScreen();
			}

			Raylib.EndDrawing();
		}

	private static void UpdateMainMenuFade(float dt)
	{
		if (_mainMenuFadePhase == MainMenuFadePhase.None) return;

		if (_mainMenuFadePhase == MainMenuFadePhase.FadeOut)
		{
			_mainMenuFadeAlpha += dt / MAIN_MENU_FADE_DURATION;
			if (_mainMenuFadeAlpha >= 1f)
			{
				_mainMenuFadeAlpha = 1f;
				PerformReturnToMainMenuCleanup();
				_gameState = GameState.MainMenu;
				_mainMenuFadePhase = MainMenuFadePhase.FadeIn;
			}
		}
		else if (_mainMenuFadePhase == MainMenuFadePhase.FadeIn)
		{
			_mainMenuFadeAlpha -= dt / MAIN_MENU_FADE_DURATION;
			if (_mainMenuFadeAlpha <= 0f)
			{
				_mainMenuFadeAlpha = 0f;
				_mainMenuFadePhase = MainMenuFadePhase.None;
			}
		}
	}

	private static void DrawMainMenuTransitionOverlay()
	{
		byte alpha = (byte)Math.Clamp(_mainMenuFadeAlpha * 255f, 0, 255);
		Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color((byte)0, (byte)0, (byte)0, alpha));
	}

	private static void StartReturnToMainMenuFade()
	{
		if (_mainMenuFadePhase != MainMenuFadePhase.None) return;
		_mainMenuFadePhase = MainMenuFadePhase.FadeOut;
		_mainMenuFadeAlpha = 0f;
		_pendingReturnToMainMenuCleanup = true;
	}

	private static void PerformReturnToMainMenuCleanup()
	{
		if (!_pendingReturnToMainMenuCleanup) return;
		_pendingReturnToMainMenuCleanup = false;

		if (NetworkManager.IsOnline)
			NetworkManager.Disconnect();

		SaveCurrentGameOnExit();
		if (ShouldSaveCurrentGameOnExit())
			InventoryRenderer.CloseInventory();
		World.Reset();
		//  CRITIQUE : sans ceci, World.CurrentSaveName garde le nom de la dernière
		// partie jouée. Comme le monde-vitrine du menu principal (Program_MenuPreview.cs)
		// génère et décharge des chunks comme en jeu (UpdateActiveChunks), tout chunk
		// du panorama aléatoire du menu qui se décharge se retrouvait alors SAUVEGARDÉ
		// dans le dossier de la vraie sauvegarde (World_Chunks.cs / World_Core.cs ne
		// vérifient que "CurrentSaveName non vide" avant d'écrire sur disque) : le menu
		// polluait donc silencieusement la partie du joueur avec du terrain/contenu qui
		// n'a rien à voir. Le menu principal doit être un monde totalement à part, qui
		// ne doit plus jamais avoir de nom de sauvegarde associé une fois qu'on le quitte.
		World.CurrentSaveName = "";
		_currentSaveName = "";
		entities.Clear();
		destroyedObjects.Clear();
		objectHPs.Clear();
		PerlinNoise.SetSeed(0);
		Program.ActiveStation = "";
		_isMultiplayerMenu = false;
		_isJoinSetup = false;
		_isCharacterSelect = false;
		_isHostSetup = false;
		_isHostingFromPause = false;
		// Repartir directement sur l'écran d'accueil de base : sans ça, _mainMenuActivated
		// restait à true (hérité de la partie qu'on vient de quitter) et on retombait un
		// court instant sur le carrousel des sauvegardes avant de revenir à l'accueil.
		_mainMenuActivated = false;
		_mainMenuAnim = 0f;
		RefreshAvailableSaves();
	}

	private static int ResolvePlaceableId(Item item, ItemData itemData)
	{
		if (!string.Equals(itemData.Key, "wooden_floor", StringComparison.OrdinalIgnoreCase))
			return itemData.PlaceableId;

		string floorStyle = item.GetMetadataValue("floor_style", "");

		return (string.IsNullOrWhiteSpace(floorStyle) ? "zigzag" : floorStyle).ToLowerInvariant() switch
		{
			"zigzag" => 54,
			"tile" => 55,
			"square" => 56,
			"plank" => 57,
			"flat" => 58,
			_ => itemData.PlaceableId
		};
	}

	static void DrawPlacementGhost(Camera2D camera, Item heldItem, ItemData itemData)
	{
		itemData.PlaceableId = ResolvePlaceableId(heldItem, itemData);
		int ts = TileSize;
		Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
		
		// ─────────────────────────────────────────────────────────
		// CAS DES GRAINES
		// ─────────────────────────────────────────────────────────
		if (itemData.IsSeed)
		{
				int cropId = itemData.CropId;
				if (cropId == 0) return;
				var cropTileData = WorldTileRegistry.GetTile(cropId);
				if (cropTileData == null) return;
				
				(int seedTileX, int seedTileY, float seedDist, int _) = FindTileUnderMouse(mouseWorld, ts);
				if (seedTileX == -1 || seedTileY == -1) return;
				
				int groundId = World.GetGroundTileIdAt(seedTileX, seedTileY);
				var seedGroundTile = WorldTileRegistry.GetTile(groundId);
				bool canPlantGhost;
				if (itemData.PlantType == "classic")
				{
					//  Arbre (ex : gland) : se plante n'importe où sur un sol praticable,
					// comme un objet plaçable — pas besoin de farmland/arrosage.
					canPlantGhost = seedGroundTile != null && seedGroundTile.Walkable;
				}
				else if (itemData.PlantType == "grass")
				{
					canPlantGhost = seedGroundTile != null && seedGroundTile.Material == "grass";
				}
				else
				{
					//  Culture classique : accepter la terre sèche (27) ET humide (29), comme
					// dans PlantCrop_Apply (qui humidifie automatiquement la terre sèche).
					canPlantGhost = groundId == 27 || groundId == 29;
				}
				if (!canPlantGhost) return;
				if (World.GetObjectIdAt(seedTileX, seedTileY) != 0) return;
				
				float distanceToTile = Vector2.Distance(
					new Vector2((int)(_playerPos.X / ts) * ts + ts / 2f, (int)(_playerPos.Y / ts) * ts + ts / 2f),
					new Vector2(seedTileX * ts + ts / 2f, seedTileY * ts + ts / 2f)
				);
				if (distanceToTile > MAX_INTERACTION_DISTANCE) return;
				
				// Récupérer la texture du stade final (adulte pour une culture ; pour un arbre
				// planté, on préfère montrer directement l'arbre adulte final si possible).
				Texture2D seedTex = new Texture2D();
				if (cropTileData.IsSapling && cropTileData.AdultTileId > 0)
				{
					seedTex = WorldTileRegistry.GetFirstTexture(cropTileData.AdultTileId);
				}
				if (seedTex.Id == 0 && WorldTileRegistry.CropStageTextures.TryGetValue(cropId, out var stages) && stages.Count > 0)
				{
					seedTex = stages[stages.Count - 1];
				}
				if (seedTex.Id == 0) seedTex = missingTexture;
				if (seedTex.Id == 0) return;

				var previewTreeData = cropTileData.IsSapling && cropTileData.AdultTileId > 0
					? WorldTileRegistry.GetTile(cropTileData.AdultTileId)
					: cropTileData;
				int seedDrawW = ts;
				int seedDrawH = ts;
				if (previewTreeData?.Size != null)
				{
					seedDrawW = ts * previewTreeData.Size.Width;
					seedDrawH = ts * previewTreeData.Size.Height;
				}
				
				int seedHeight = World.GetHeightAt(seedTileX, seedTileY);
				int seedDrawX = seedTileX * ts + (previewTreeData?.DrawOffset?.X ?? 0);
				int seedDrawY = seedTileY * ts - (seedHeight * ts / 4) - seedDrawH + ts;
				
				Color seedGhostColor = new Color(255, 255, 255, 180);
				
				Raylib.DrawTexturePro(seedTex,
					new Rectangle(0, 0, seedTex.Width, seedTex.Height),
					new Rectangle(seedDrawX, seedDrawY, seedDrawW, seedDrawH),
					Vector2.Zero, 0, seedGhostColor);
				
				UpdateSelectAnimation(Raylib.GetFrameTime());
				if (_selectTextures.Count > 0 && _selectTextures[_selectAnimFrame].Id != 0)
				{
					Texture2D selectTex = _selectTextures[_selectAnimFrame];
					int selectPadding = 16;
					int selectW = seedDrawW + selectPadding * 2;
					int selectH = seedDrawH + selectPadding * 2;
					int selectX = seedDrawX - selectPadding;
					int selectY = seedDrawY - selectPadding;
					Raylib.DrawTexturePro(selectTex,
						new Rectangle(0, 0, selectTex.Width, selectTex.Height),
						new Rectangle(selectX, selectY, selectW, selectH),
						Vector2.Zero, 0, new Color(255, 255, 255, 200));
				}
				return;
			}
			
			// ─────────────────────────────────────────────────────────
			// CAS DES OBJETS PLACABLES (code existant)
			// ─────────────────────────────────────────────────────────
			int placeableId = itemData.PlaceableId;
			if (placeableId == 0) return;
			var tileData = WorldTileRegistry.GetTile(placeableId);
			if (tileData == null) return;
			
			//  CORRECTION : récupérer la première frame d'animation si la tuile est animée
			Texture2D tex;
			if (WorldTileRegistry.IsAnimated(placeableId))
			{
				var frames = WorldTileRegistry.GetAnimationFrames(placeableId);
				tex = (frames != null && frames.Count > 0) ? frames[0] : WorldTileRegistry.GetFirstTexture(placeableId);
			}
			else
			{
				tex = WorldTileRegistry.GetFirstTexture(placeableId);
			}
			
			if (tex.Id == 0) tex = missingTexture;
			if (tex.Id == 0) return;

			// ─────────────────────────────────────────────────────────
			// CAS PARTICULIER : Radeau (ID 500) → gestion bateau
			// ─────────────────────────────────────────────────────────
			Boat? previewBoat = World.GetBoatAtPosition(_playerPos, ts);

			if (placeableId == 500)
			{
				if (previewBoat != null)
				{
					// ── On est sur un bateau existant ─────────────────
					Vector2 mouseLocal = mouseWorld - previewBoat.Position;
					int cursorRelX = (int)Math.Floor((mouseLocal.X + ts / 2f) / ts);
					int cursorRelY = (int)Math.Floor((mouseLocal.Y + ts / 2f) / ts);

					bool isAdjacent = false;
					foreach (var (dx, dy) in new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
					{
						if (previewBoat.Tiles.ContainsKey((cursorRelX + dx, cursorRelY + dy)))
						{
							isAdjacent = true;
							break;
						}
					}

					if (!previewBoat.Tiles.ContainsKey((cursorRelX, cursorRelY)) && isAdjacent)
					{
						Vector2 candidateWorld = previewBoat.GetTileWorldPos(cursorRelX, cursorRelY);
						int worldTileX = (int)(candidateWorld.X / ts);
						int worldTileY = (int)(candidateWorld.Y / ts);

						if ((World.GetGroundTileIdAt(worldTileX, worldTileY) == 10 ||
							 World.GetGroundTileIdAt(worldTileX, worldTileY) == 11) &&
							World.GetObjectIdAt(worldTileX, worldTileY) == 0)
						{
							// Position réelle et continue de la case candidate (pas re-alignée sur
							// la grille MONDE) + décalage de hauteur, pour rester exact même quand
							// le bateau a dérivé et n'est plus pile sur une frontière de tuile.
							int candidateHeight = World.GetHeightAt(worldTileX, worldTileY);
							float candYOffset = -candidateHeight * ts / 4f;
							float boatDrawX = candidateWorld.X - ts / 2f;
							float boatDrawY = candidateWorld.Y - ts / 2f + candYOffset;

							UpdateSelectAnimation(Raylib.GetFrameTime());
							if (_selectTextures.Count > 0 && _selectTextures[_selectAnimFrame].Id != 0)
							{
								Texture2D selectTex = _selectTextures[_selectAnimFrame];
								int selectPadding = 16;
								int selectW = ts + selectPadding * 2;
								int selectH = ts + selectPadding * 2;
								float selectX = boatDrawX - selectPadding;
								float selectY = boatDrawY - selectPadding;
								Raylib.DrawTexturePro(selectTex,
									new Rectangle(0, 0, selectTex.Width, selectTex.Height),
									new Rectangle(selectX, selectY, selectW, selectH),
									Vector2.Zero, 0, new Color(255, 255, 255, 200));
							}

							Color boatGhostColor = new Color(255, 255, 255, 180);
							Raylib.DrawTexturePro(tex,
								new Rectangle(0, 0, tex.Width, tex.Height),
								new Rectangle(boatDrawX, boatDrawY, ts, ts),
								Vector2.Zero, 0, boatGhostColor);
						}
					}
					return;
				}
				// else: on continue vers le cas général (créer un nouveau bateau sur l'eau libre)
			}
			else if (previewBoat != null)
			{
				// ─────────────────────────────────────────────────────────
				// NOUVEAU : tout objet plaçable AUTRE que le radeau, alors que le
				// joueur est sur un bateau → doit obligatoirement viser une case de
				// LA GRILLE DU BATEAU (et non la grille du monde en dessous). Avant
				// ce correctif, seul le radeau (ID 500) avait cet aperçu spécifique ;
				// tous les autres objets (meubles, voiles, gouvernails...) utilisaient
				// FindTileUnderMouse (grille MONDE), ce qui désynchronisait totalement
				// le curseur select1-3 de la vraie case ciblée par HandlePlace (qui,
				// lui, utilise bien la grille du bateau) dès que le bateau avait bougé.
				Vector2 mouseLocal = mouseWorld - previewBoat.Position;
				int relX = (int)Math.Floor((mouseLocal.X + ts / 2f) / ts);
				int relY = (int)Math.Floor((mouseLocal.Y + ts / 2f) / ts);

				if (!previewBoat.Tiles.ContainsKey((relX, relY)))
					return; // hors de la grille du bateau : aucun aperçu, placement impossible

				if (previewBoat.GetObjectIdAt(relX, relY) != 0)
					return; // case déjà occupée

				Vector2 tileWorldPos = previewBoat.GetTileWorldPos(relX, relY);
				int furnTileX = (int)(tileWorldPos.X / ts);
				int furnTileY = (int)(tileWorldPos.Y / ts);
				int height = World.GetHeightAt(furnTileX, furnTileY);
				float yOffset = -height * ts / 4f;

				float furnDrawX = tileWorldPos.X - ts / 2f;
				float furnDrawY = tileWorldPos.Y - ts / 2f + yOffset;
				float furnW = ts, furnH = ts;

				if (tileData.Size != null)
				{
					furnW = ts * tileData.Size.Width;
					furnH = ts * tileData.Size.Height;
					if (tileData.DrawOffset != null)
					{
						furnDrawX += tileData.DrawOffset.X;
						furnDrawY -= tileData.DrawOffset.Y;
					}
					furnDrawY = furnDrawY - furnH + ts;
				}

				UpdateSelectAnimation(Raylib.GetFrameTime());
				if (_selectTextures.Count > 0 && _selectTextures[_selectAnimFrame].Id != 0)
				{
					Texture2D selectTex = _selectTextures[_selectAnimFrame];
					int selectPadding = 16;
					float selectW = furnW + selectPadding * 2;
					float selectH = furnH + selectPadding * 2;
					float selectX = furnDrawX - selectPadding;
					float selectY = furnDrawY - selectPadding;
					Raylib.DrawTexturePro(selectTex,
						new Rectangle(0, 0, selectTex.Width, selectTex.Height),
						new Rectangle(selectX, selectY, selectW, selectH),
						Vector2.Zero, 0, new Color(255, 255, 255, 200));
				}

				Color furnitureGhostColor = new Color(255, 255, 255, 180);
				Raylib.DrawTexturePro(tex,
					new Rectangle(0, 0, tex.Width, tex.Height),
					new Rectangle(furnDrawX, furnDrawY, furnW, furnH),
					Vector2.Zero, 0, furnitureGhostColor);

				return; // sur un bateau : jamais de repli vers la grille du monde
			}

			// ─────────────────────────────────────────────────────────
			// CAS GÉNÉRAL (hors bateau)
			// ─────────────────────────────────────────────────────────
			(int tileX, int tileY, float dist, int _) = FindTileUnderMouse(mouseWorld, ts);
			if (tileX == -1 || tileY == -1) return;

			if (World.GetObjectIdAt(tileX, tileY) != 0) return;

			if (itemData.IsGroundTile)
			{
				int currentGroundId = World.GetGroundTileIdAt(tileX, tileY);
				var currentTileData = WorldTileRegistry.GetTile(currentGroundId);
				if (currentTileData != null && !currentTileData.Diggable) return;
			}

			int heightAtTarget = World.GetHeightAt(tileX, tileY);

			int drawX = tileX * ts;
			int drawY = tileY * ts - (heightAtTarget * ts / 4);

			int w = ts, h = ts;

			if (tileData.Size != null)
			{
				w = ts * tileData.Size.Width;
				h = ts * tileData.Size.Height;

				if (tileData.DrawOffset != null)
				{
					drawX += tileData.DrawOffset.X;
					drawY -= tileData.DrawOffset.Y;
				}

				drawY = drawY - h + ts;
			}

			UpdateSelectAnimation(Raylib.GetFrameTime());
			if (_selectTextures.Count > 0 && _selectTextures[_selectAnimFrame].Id != 0)
			{
				Texture2D selectTex = _selectTextures[_selectAnimFrame];
				int selectPadding = 16;
				int selectW = w + selectPadding * 2;
				int selectH = h + selectPadding * 2;
				int selectX = drawX - selectPadding;
				int selectY = drawY - selectPadding;

				Raylib.DrawTexturePro(selectTex,
					new Rectangle(0, 0, selectTex.Width, selectTex.Height),
					new Rectangle(selectX, selectY, selectW, selectH),
					Vector2.Zero, 0, new Color(255, 255, 255, 200));
			}

			Color ghostColor = new Color(255, 255, 255, 180);
			Raylib.DrawTexturePro(tex,
				new Rectangle(0, 0, tex.Width, tex.Height),
				new Rectangle(drawX, drawY, w, h),
				Vector2.Zero, 0, ghostColor);

			if (_showDebug && tileData.CollisionSize != null)
			{
				int colW = ts * tileData.CollisionSize.Width;
				int colH = ts * tileData.CollisionSize.Height;
				int colX = drawX + (w - colW) / 2;
				int colY = drawY + (h - colH);

				if (tileData.CollisionOffset != null)
				{
					colX += tileData.CollisionOffset.X;
					colY -= tileData.CollisionOffset.Y;
				}

				Raylib.DrawRectangleLines(colX, colY, colW, colH, Color.Red);
			}
		}

		static void DrawPauseMenu()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();

			// ── Animation d'ouverture / fermeture ──
			const float SLIDE_SPEED = 5f;
			float dt = Raylib.GetFrameTime();
			if (_pauseMenuClosing)
			{
				_pauseMenuSlide -= dt * SLIDE_SPEED;
				if (_pauseMenuSlide <= 0f)
				{
					_pauseMenuSlide = 0f;
					_pauseMenuClosing = false;
					var target = _pausePendingState;
					var action = _pausePendingAction;
					_pausePendingAction = null;
					action?.Invoke();
					_gameState = target;
					return;
				}
			}
			else if (_pauseMenuSlide < 1f)
			{
				_pauseMenuSlide += dt * SLIDE_SPEED;
				if (_pauseMenuSlide > 1f) _pauseMenuSlide = 1f;
			}
			float eased = 1f - MathF.Pow(1f - _pauseMenuSlide, 3f);

			Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, (int)(120 * eased)));

			// ── Panneau nine-slice "scroll" ──
			int panelWidth = 460;
			bool canHost = !NetworkManager.IsOnline;
			var pauseMenuItemsList = new List<(string Key, string Label)>
			{
				("pause.button.resume", Localization.Get("pause.button.resume"))
			};
			if (canHost) pauseMenuItemsList.Add(("pause.button.host", Localization.Get("pause.button.host")));
			pauseMenuItemsList.Add(("pause.button.options", Localization.Get("pause.button.options")));
			pauseMenuItemsList.Add(("pause.button.quit", Localization.Get("pause.button.quit")));
			var pauseMenuItems = pauseMenuItemsList.ToArray();

			int titleAreaHeight = 140; // Réduit pour laisser plus de place au titre
			int buttonWidth = 260;
			int buttonHeight = 50;
			int buttonSpacing = 18;
			int panelPadding = 30;
			int buttonsHeight = pauseMenuItems.Length * buttonHeight + (pauseMenuItems.Length - 1) * buttonSpacing;
			int panelHeight = titleAreaHeight + buttonsHeight + panelPadding * 2;

			int panelX = (sw - panelWidth) / 2;
			int restingPanelY = (sh - panelHeight) / 2;
			int hiddenPanelY = -panelHeight - 20;
			int panelY = (int)MathHelper.Lerp(hiddenPanelY, restingPanelY, eased);

			Rectangle panelRect = new Rectangle(panelX, panelY, panelWidth, panelHeight);
			if (UIManager.ScrollPanelTexture != null && UIManager.ScrollPanelTexture.IsValid)
			{
				UIManager.ScrollPanelTexture.Draw(panelRect, Color.White);
			}
			else
			{
				Raylib.DrawRectangleRounded(panelRect, 0.05f, 8, new Color(45, 35, 25, 235));
				Raylib.DrawRectangleRoundedLines(panelRect, 0.05f, 8, 2, new Color(200, 170, 100, 200));
			}

			// ── Titre en TRÈS GROS, centré en haut du parchemin ──
			Texture2D titleTex = _menuTitle;
			if (titleTex.Id != 0)
			{
				//  TITRE PLUS GROS : 2.4x au lieu de 1.6x (ou plus selon vos préférences)
				float titleScale = Math.Min(2.4f, (float)(panelWidth - 40) / titleTex.Width);
				int titleWidth = (int)(titleTex.Width * titleScale);
				int titleX = panelX + (panelWidth - titleWidth) / 2;
				int titleY = panelY + 12; // Décalé vers le haut pour rester dans le panneau
				SafeDrawTexture(titleTex, new Vector2(titleX, titleY), 0, titleScale, Color.White);
			}
			else
			{
				// Fallback textuel si la texture est manquante : police plus grande
				string title = Localization.Get("pause.title");
				int titleFontSize = 60; // Augmenté de 72 à 60 (ajusté pour ne pas déborder)
				int titleWidth = FontManager.MeasureText(title, titleFontSize);
				FontManager.DrawText(title, panelX + (panelWidth - titleWidth) / 2, panelY + 16, titleFontSize, new Color(230, 200, 100, 255));
			}

			// ── Boutons, en nine-slice "inventory" ──
			int buttonX = panelX + (panelWidth - buttonWidth) / 2;
			int startButtonY = panelY + titleAreaHeight + panelPadding - 10; // Ajusté
			Vector2 mousePos = Raylib.GetMousePosition();
			for (int i = 0; i < pauseMenuItems.Length; i++)
			{
				int currentButtonY = startButtonY + i * (buttonHeight + buttonSpacing);
				Rectangle buttonRect = new Rectangle(buttonX, currentButtonY, buttonWidth, buttonHeight);
				bool interactive = !_pauseMenuClosing && _pauseMenuSlide >= 0.999f && !_isMultiplayerMenu;
				bool isHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, buttonRect);
				if (!interactive)
				{
					_prevPauseButtonsHovered.Clear();
				}
				else
				{
					if (isHovered && !_prevPauseButtonsHovered.Contains(i)) PlayHoverSound();
					if (isHovered) _prevPauseButtonsHovered.Add(i); else _prevPauseButtonsHovered.Remove(i);
				}

				if (UIManager.InventoryPanel != null && UIManager.InventoryPanel.IsValid)
				{
					Color tint = isHovered ? new Color(255, 235, 190, 255) : Color.White;
					UIManager.InventoryPanel.Draw(buttonRect, tint);
				}
				else
				{
					Color buttonBg = isHovered ? new Color(60, 50, 35, 220) : new Color(25, 25, 35, 180);
					Raylib.DrawRectangleRounded(buttonRect, 0.2f, 8, buttonBg);
					Color borderColor = isHovered ? new Color(200, 170, 100, 255) : new Color(100, 90, 70, 180);
					Raylib.DrawRectangleRoundedLines(buttonRect, 0.2f, 8, 2, borderColor);
				}

				int fontSize = 24;
				int textWidth = FontManager.MeasureText(pauseMenuItems[i].Label, fontSize);
				Color textColor = isHovered ? Color.White : new Color(60, 45, 30, 255);
				FontManager.DrawText(pauseMenuItems[i].Label, (int)(buttonX + buttonWidth / 2 - textWidth / 2), (int)(currentButtonY + buttonHeight / 2 - fontSize / 2), fontSize, textColor);

				if (isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					PlayButtonClickSound();
					string action = pauseMenuItems[i].Key;
					if (action == "pause.button.resume")
					{
						RequestClosePauseMenu(GameState.Playing);
					}
					else if (action == "pause.button.host")
					{
						RequestClosePauseMenu(GameState.Playing, () =>
						{
							_mpPseudoInput = string.IsNullOrWhiteSpace(LocalPlayerName) ? Localization.Get("multiplayer.default_host_name") : LocalPlayerName;
							_mpFocusedField = 0;
							_mpStatusMessage = "";
							_isHostingFromPause = true;
							_isHostSetup = true;
							_isMultiplayerMenu = true;
						});
					}
					else if (action == "pause.button.options")
					{
						RequestClosePauseMenu(GameState.Options, () => OpenOptions(GameState.Paused));
					}
					else if (action == "pause.button.quit")
					{
						RequestClosePauseMenu(GameState.Playing, () =>
						{
							StartReturnToMainMenuFade();
						});
					}
				}
			}

			// ── Bouton "Succès" ──
			if (_achievementsMenuIcon.Id != 0 && _achievementsMenuIcon.Width > 0)
			{
				int achSize = 48;
				int achX = panelX + panelWidth - achSize - 24;
				int achY = panelY + panelHeight - achSize - 20;
				Rectangle achRect = new Rectangle(achX, achY, achSize, achSize);
				bool achInteractive = !_pauseMenuClosing && _pauseMenuSlide >= 0.999f && !_isMultiplayerMenu;
				bool achHovered = achInteractive && Raylib.CheckCollisionPointRec(mousePos, achRect);
				if (!achInteractive) _prevAchievementsIconHovered = false;
				UpdateButtonHoverSound(achHovered, ref _prevAchievementsIconHovered);
				Color achBg = achHovered ? new Color(60, 50, 35, 220) : new Color(25, 25, 35, 180);
				Raylib.DrawRectangleRounded(achRect, 0.25f, 8, achBg);
				Raylib.DrawRectangleRoundedLines(achRect, 0.25f, 8, 2, achHovered ? new Color(200, 170, 100, 255) : new Color(100, 90, 70, 180));
				Rectangle achSrc = new Rectangle(0, 0, _achievementsMenuIcon.Width, _achievementsMenuIcon.Height);
				Rectangle achDst = new Rectangle(achX + 6, achY + 6, achSize - 12, achSize - 12);
				Raylib.DrawTexturePro(_achievementsMenuIcon, achSrc, achDst, Vector2.Zero, 0, Color.White);
				if (achHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					PlayButtonClickSound();
					RequestClosePauseMenu(GameState.Achievements, () =>
					{
						_optionsReturnState = GameState.Paused;
						AchievementManager.OpenMenu();
					});
				}
			}

			string resumeHint = Localization.Get("pause.hint.resume");
			int hintWidth = FontManager.MeasureText(resumeHint, 12);
			FontManager.DrawText(resumeHint, panelX + panelWidth - hintWidth - 20, panelY + panelHeight - 25, 12, new Color(200, 190, 160, (int)(180 * eased)));
		}

		// Carrousel interactif des mondes disponibles + bouton "Créer un monde" tout à droite.
		// Jusqu'à 5 éléments affichés : le centre (plus gros, c'est lui qu'on lance),
		// puis 2 de chaque côté, de plus en plus petits et sombres en s'éloignant du centre.
		// Reconstruit le cache des aperçus (équipement + apparence) pour tous les mondes
		// actuellement dans _availableSaves. Fait le travail lourd (lecture disque, parse JSON,
		// reconstruction de l'Equipment, chargement des textures) UNE SEULE FOIS par changement
		// de liste, au lieu de le refaire à chaque frame comme avant.
		static void RebuildCarouselPreviewCache()
		{
			_carouselPreviewCache.Clear();

			foreach (var save in _availableSaves)
			{
				string savePath = Path.Combine("Saves", save.Name + ".json");
				GameSaveData? preview = null;
				if (File.Exists(savePath))
				{
					try
					{
						string json = File.ReadAllText(savePath);
						var opts = new JsonSerializerOptions
						{
							NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
							PropertyNameCaseInsensitive = true
						};
						preview = JsonSerializer.Deserialize<GameSaveData>(json, opts);
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Erreur de désérialisation pour {save.Name} : {ex.Message}");
						preview = null;
					}
				}

				var tempEquipment = new Equipment();

				// --- Restauration de l'équipement (zone items) ---
				if (preview != null)
				{
					foreach (var zoneSave in preview.Equipment.ZoneItems)
					{
						if (string.IsNullOrEmpty(zoneSave.ItemName)) continue;
						var itemDef = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == zoneSave.ItemName);
						if (itemDef.ID == 0) continue;
						var zoneItem = new Item(itemDef.Name, 1, itemDef.Color, itemDef.Icon);
						var loadedColors = SaveSystem.LoadCustomColors(zoneSave.CustomColors);
						if (loadedColors != null && loadedColors.Count > 0)
							zoneItem.CustomColors = loadedColors;
						zoneItem.RestoreMetadataFromSave(zoneSave.Metadata ?? "");
						if (zoneSave.Meta != null && zoneSave.Meta.Count > 0)
							foreach (var kv in zoneSave.Meta)
								zoneItem.Meta[kv.Key] = kv.Value;
						if (itemDef.RuneSlots > 0)
							SaveSystem.RestoreRunes(zoneItem, itemDef.RuneSlots, zoneSave.Runes, zoneSave.Metadata);
						SaveSystem.RestoreCoatVariantMeta(zoneItem, itemDef);
						tempEquipment.EquipOnBody(zoneItem, out _);
					}

					if (!string.IsNullOrEmpty(preview.Equipment.MainHand))
					{
						var item = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == preview.Equipment.MainHand);
						if (item.ID != 0)
						{
							tempEquipment.MainHand = new Item(item.Name, 1, item.GetColor(), item.Icon);
							var lc = SaveSystem.LoadCustomColors(preview.Equipment.MainHandCustomColors);
							if (lc != null && lc.Count > 0)
								tempEquipment.MainHand.CustomColors = lc;
						}
					}

					if (!string.IsNullOrEmpty(preview.Equipment.OffHand))
					{
						var item = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == preview.Equipment.OffHand);
						if (item.ID != 0)
						{
							tempEquipment.OffHand = new Item(item.Name, 1, item.GetColor(), item.Icon);
							var lc = SaveSystem.LoadCustomColors(preview.Equipment.OffHandCustomColors);
							if (lc != null && lc.Count > 0)
								tempEquipment.OffHand.CustomColors = lc;
						}
					}

					// Conteneur portable (panier)
					if (preview.Equipment.OffHandContainer != null)
					{
						int slotCount = preview.Equipment.OffHandContainer.Slots.Count;
						int columns = preview.Equipment.OffHandContainer.Columns > 0 ? preview.Equipment.OffHandContainer.Columns : 3;
						var container = new PortableContainerData(slotCount, columns);
						for (int i = 0; i < preview.Equipment.OffHandContainer.Slots.Count && i < container.Inventory.Slots.Count; i++)
						{
							var savedSlot = preview.Equipment.OffHandContainer.Slots[i];
							if (!savedSlot.IsEmpty && !string.IsNullOrEmpty(savedSlot.ItemName))
							{
								var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == savedSlot.ItemName);
								if (itemData.ID != 0)
								{
									var contItem = new Item(itemData.Name, savedSlot.Count, itemData.Color, itemData.Icon);
									var lc = SaveSystem.LoadCustomColors(savedSlot.CustomColors);
									if (lc != null && lc.Count > 0)
										contItem.CustomColors = lc;
									container.Inventory.Slots[i].Item = contItem;
									container.Inventory.Slots[i].Count = savedSlot.Count;
								}
							}
						}
						tempEquipment.OffHandContainer = container;
						if (tempEquipment.OffHand != null)
							tempEquipment.OffHand.Container = container;
					}

					// Sac à dos
					if (preview.Equipment.BackpackContainer != null)
					{
						int slotCount = preview.Equipment.BackpackContainer.Slots.Count;
						int columns = preview.Equipment.BackpackContainer.Columns > 0 ? preview.Equipment.BackpackContainer.Columns : 5;
						var backpack = new BackpackData(slotCount, columns);
						for (int i = 0; i < preview.Equipment.BackpackContainer.Slots.Count && i < backpack.Inventory.Slots.Count; i++)
						{
							var savedSlot = preview.Equipment.BackpackContainer.Slots[i];
							if (!savedSlot.IsEmpty && !string.IsNullOrEmpty(savedSlot.ItemName))
							{
								var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == savedSlot.ItemName);
								if (itemData.ID != 0)
								{
									var backItem = new Item(itemData.Name, savedSlot.Count, itemData.Color, itemData.Icon);
									var lc = SaveSystem.LoadCustomColors(savedSlot.CustomColors);
									if (lc != null && lc.Count > 0)
										backItem.CustomColors = lc;
									backpack.Inventory.Slots[i].Item = backItem;
									backpack.Inventory.Slots[i].Count = savedSlot.Count;
								}
							}
						}
						tempEquipment.BackpackContainer = backpack;
						if (tempEquipment.Backpack != null)
							tempEquipment.Backpack.Backpack = backpack;
					}

					// EquippedItems (armures/accessoires dans les slots)
					if (preview.Equipment.EquippedItems != null && preview.Equipment.EquippedItems.Count > 0)
					{
						foreach (var equippedSave in preview.Equipment.EquippedItems)
						{
							if (string.IsNullOrEmpty(equippedSave.ItemName)) continue;
							var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == equippedSave.ItemName);
							if (itemData.ID == 0) continue;
							var item = new Item(itemData.Name, equippedSave.Count, itemData.Color, itemData.Icon);
							var lc = SaveSystem.LoadCustomColors(equippedSave.CustomColors);
							if (lc != null && lc.Count > 0)
								item.CustomColors = lc;
									item.RestoreMetadataFromSave(equippedSave.Metadata ?? "");
									if (equippedSave.Meta != null && equippedSave.Meta.Count > 0)
										foreach (var kv in equippedSave.Meta)
											item.Meta[kv.Key] = kv.Value;
							if (itemData.RuneSlots > 0)
								SaveSystem.RestoreRunes(item, itemData.RuneSlots, equippedSave.Runes, equippedSave.Metadata);
							SaveSystem.RestoreCoatVariantMeta(item, itemData);
							tempEquipment.EquipItem(item, equippedSave.Slot, equippedSave.Index);
						}
					}
				}

				tempEquipment.LoadEquipmentTextures();

				// --- Couleurs ---
				// Couleur de peau : si preview est null ou que les composantes sont toutes à 0, on utilise une valeur par défaut fixe
				Color defaultSkin = new Color(255, 235, 200, 255);
				Color skin;
				if (preview != null && (preview.PlayerSkinColorR != 0 || preview.PlayerSkinColorG != 0 || preview.PlayerSkinColorB != 0))
				{
					skin = new Color(
						(byte)preview.PlayerSkinColorR,
						(byte)preview.PlayerSkinColorG,
						(byte)preview.PlayerSkinColorB,
						(byte)preview.PlayerSkinColorA
					);
				}
				else
				{
					skin = defaultSkin;
				}

				// Couleur des cheveux : fallback sur une couleur brune par défaut si absente
				Color hairColor = preview != null
					? new Color((byte)preview.PlayerHairColorR, (byte)preview.PlayerHairColorG, (byte)preview.PlayerHairColorB, (byte)preview.PlayerHairColorA)
					: new Color(60, 40, 30, 255);

				// Styles
				int hairIdx = preview != null ? Math.Clamp(preview.PlayerHairStyle, 0, Program.hairBaseTextures.Count - 1) : 0;
				Texture2D hBase = Program.hairBaseTextures.Count > 0 ? Program.hairBaseTextures[hairIdx] : new Texture2D();
				Texture2D hOverlay = Program.hairOverlayTextures.Count > 0 ? Program.hairOverlayTextures[hairIdx] : new Texture2D();

				int beardStyle = preview != null ? preview.PlayerBeardStyle : 0;

				string species = preview != null && !string.IsNullOrWhiteSpace(preview.PlayerMorphSpecies)
					? preview.PlayerMorphSpecies
					: "human";
				Color morphTint = preview?.PlayerMorphTint == null
					? Color.White
					: new Color(preview.PlayerMorphTint.R, preview.PlayerMorphTint.G, preview.PlayerMorphTint.B, preview.PlayerMorphTint.A);
				var featureVariants = preview?.PlayerMorphFeatureVariants?.ToDictionary(feature => feature.Key, feature => feature.Value)
					?? new Dictionary<string, int>();
				var featureColors = preview?.PlayerMorphFeatureColors?.ToDictionary(
					feature => feature.Key,
					feature => new Color(feature.Value.R, feature.Value.G, feature.Value.B, feature.Value.A))
					?? new Dictionary<string, Color>();
				if (!string.Equals(species, "human", StringComparison.OrdinalIgnoreCase)
					&& preview?.PlayerMorphFeatureVariants == null && preview?.PlayerMorphFeatureColors == null)
				{
					Entity.GenerateRandomAppearance(species, Vector2.Zero, out morphTint, featureVariants, featureColors);
				}

				_saveBackgroundStyles[save.Name] = preview?.CharacterSelectionBackgroundStyle ?? 0;
				_carouselPreviewCache[save.Name] = new CarouselPreviewCache
				{
					Equipment = tempEquipment,
					Skin = skin,
					HairColor = hairColor,
					HairBase = hBase,
					HairOverlay = hOverlay,
					BeardStyle = beardStyle,
					Species = species,
					MorphTint = morphTint,
					FeatureVariants = featureVariants,
					FeatureColors = featureColors
				};
			}

			EntityRenderer.ClearEquipmentCache();
			_carouselCacheDirty = false;
		}

		static void DrawWorldCarousel(int sw, int sh, float visibility, Vector2 mousePos)
		{
			// Plus de SaveSystem.GetSaves() ici : ça scannait le disque à CHAQUE FRAME.
			// _availableSaves est déjà à jour (rafraîchi via RefreshAvailableSaves()).
			if (_carouselCacheDirty)
			{
				RebuildCarouselPreviewCache();
			}
			int worldCount = _availableSaves.Count;
			int totalItems = worldCount + 2; // +2 = bouton "Rejoindre une partie" + bouton "Créer un monde"
			_worldCarouselIndex = Math.Clamp(_worldCarouselIndex, 0, totalItems - 1);

			// Le carrousel est ouvert : la caméra-vitrine du menu (Program_MenuPreview)
			// suit le personnage correspondant à la sauvegarde actuellement au centre,
			// au lieu du panorama automatique. Slots 0/1 = "Rejoindre"/"Créer", donc
			// les sauvegardes commencent à l'index 2 (voir label plus bas).
			// Lissage de l'index sélectionné pour une animation de glissement fluide.
			float diff = _worldCarouselIndex - _worldCarouselAnimIndex;
			_worldCarouselAnimIndex += diff * Math.Min(1f, Raylib.GetFrameTime() * 10f);
			if (Math.Abs(diff) < 0.01f) _worldCarouselAnimIndex = _worldCarouselIndex;

			int centerY = (int)(sh * 0.56f);
			float slotSpacing = sw * 0.19f;
			float centerScaleMax = Math.Min(sw, sh) * 0.00042f; // facteur d'échelle pour le personnage central

			// On affiche au plus 2 voisins de chaque côté du centre (5 éléments visibles au total).
				for (int offset = -2; offset <= 2; offset++)
				{
					int itemIndex = _worldCarouselIndex + offset;
				if (itemIndex < 0 || itemIndex >= totalItems) continue;

				float slideOffset = itemIndex - _worldCarouselAnimIndex; // position réelle lissée (peut être fractionnaire)

				float absOffset = Math.Abs(slideOffset);
				float distanceFactor = Math.Clamp(absOffset, 0f, 2f) / 2f; // 0 = centre, 1 = bord

				float x = sw / 2f + slideOffset * slotSpacing;
				float scale = (1f - distanceFactor * 0.62f) * centerScaleMax * visibility;
				byte darken = (byte)(255 - distanceFactor * 150);
				Color tint = new Color(darken, darken, darken, (byte)(255 * visibility));

				bool isJoinSlot = itemIndex == 0;
				bool isCreateSlot = itemIndex == 1;
				// Déterminer la taille en interpolant en continu selon la distance lissée (fluidité)
				float normalized = Math.Clamp(absOffset / 2f, 0f, 1f); // 0 = centre, 1 = bord
				float sizeMultiplier = MathHelper.Lerp(4f, 2f, normalized); // 4x au centre → 2x au bord
				float baseCharHeight = Math.Min(sw, sh) * 0.06f; // hauteur de base pour multiplier

				float entryDelay = Math.Clamp(Math.Abs(slideOffset) * 0.08f, 0f, 0.55f);
				float entryProgress = Math.Clamp((visibility - entryDelay) / Math.Max(0.001f, 1f - entryDelay), 0f, 1f);
				float entryEase = 1f - (float)Math.Pow(1f - entryProgress, 3f);
				float impulseScale = 1f + (float)Math.Sin(entryEase * MathF.PI) * 0.08f;
				float appearScale = MathHelper.Lerp(0.12f, 1f, entryEase);
				float appearAlpha = MathHelper.Lerp(0f, 1f, entryEase);
				float bounceOffset = (1f - entryEase) * 14f;

				float texH = baseCharHeight * sizeMultiplier * appearScale * visibility;
				float texW = texH * 0.6f;
				float baseY = centerY - texH * 0.5f - bounceOffset;

				// Socle d'ombre sous le personnage (pas sur le bouton "Créer un monde").
				if (!isCreateSlot && !isJoinSlot)
				{
					float shadowW = texW * 0.7f;
					float shadowH = shadowW * 0.28f;
					Raylib.DrawEllipse((int)x, (int)(baseY + texH * 0.92f), shadowW / 2f, shadowH / 2f, new Color((byte)0, (byte)0, (byte)0, (byte)(140 * appearAlpha * visibility * (1f - distanceFactor * 0.3f))));
				}

				// Socle décoratif (assets/gui/save_base.png), proportionnel à la taille du personnage.
				if (_carouselSaveBase.Id != 0 && _carouselSaveBase.Width > 0 && _carouselSaveBase.Height > 0)
				{
					float baseTexW = texW * 1.95f;
					float baseTexH = baseTexW * ((float)_carouselSaveBase.Height / _carouselSaveBase.Width);
					Rectangle baseSrc = new Rectangle(0, 0, _carouselSaveBase.Width, _carouselSaveBase.Height);
					Rectangle baseDst = new Rectangle(x, baseY + texH * 0.98f, baseTexW, baseTexH);
					Vector2 baseOrigin = new Vector2(baseTexW / 2f, baseTexH / 2f);
					Raylib.DrawTexturePro(_carouselSaveBase, baseSrc, baseDst, baseOrigin, 0, new Color(tint.R, tint.G, tint.B, (byte)(tint.A * appearAlpha)));
				}

				// Ne pas dessiner seulement si l'aperçu est complètement hors-écran
				float halfW = texW / 2f;
				if (x + halfW < 0f - halfW || x - halfW > sw + halfW) continue;

				if (isJoinSlot)
				{
					Texture2D charTex = _mainMenuJoinWorldIcon;
					if (charTex.Id != 0 && charTex.Width != 0)
					{
						float iconW = texW * 1.02f;
						float iconH = iconW * ((float)charTex.Height / charTex.Width);
						if (iconH > texH * 1.02f)
						{
							iconH = texH * 1.02f;
							iconW = iconH * ((float)charTex.Width / charTex.Height);
						}
						Rectangle src = new Rectangle(0, 0, charTex.Width, charTex.Height);
						Rectangle dst = new Rectangle(x, baseY + texH / 2f, iconW, iconH);
						Vector2 origin = new Vector2(iconW / 2f, iconH / 2f);
						Raylib.DrawTexturePro(charTex, src, dst, origin, 0, new Color(tint.R, tint.G, tint.B, (byte)(tint.A * appearAlpha)));
					}
				}
				else if (isCreateSlot)
				{
					Texture2D charTex = _mainMenuCreateWorldIcon;
					if (charTex.Id != 0 && charTex.Width != 0)
					{
						float iconW = texW * 1.02f;
						float iconH = iconW * ((float)charTex.Height / charTex.Width);
						if (iconH > texH * 1.02f)
						{
							iconH = texH * 1.02f;
							iconW = iconH * ((float)charTex.Width / charTex.Height);
						}
						Rectangle src = new Rectangle(0, 0, charTex.Width, charTex.Height);
						Rectangle dst = new Rectangle(x, baseY + texH / 2f, iconW, iconH);
						Vector2 origin = new Vector2(iconW / 2f, iconH / 2f);
						Raylib.DrawTexturePro(charTex, src, dst, origin, 0, new Color(tint.R, tint.G, tint.B, (byte)(tint.A * appearAlpha)));
					}
				}
				else
				{
					//  Aperçu lu depuis le cache (plus de lecture/parse JSON ni de rechargement
					// de textures d'équipement à chaque frame — voir RebuildCarouselPreviewCache).
					int previewSaveIndex = itemIndex - 2;
					string saveName = _availableSaves[previewSaveIndex].Name;
					if (!_carouselPreviewCache.TryGetValue(saveName, out var cached))
					{
						// Filet de sécurité : ne devrait pas arriver, mais on reconstruit tout le
						// cache si une entrée manque (ex: sauvegarde ajoutée hors cycle normal).
						RebuildCarouselPreviewCache();
						_carouselPreviewCache.TryGetValue(saveName, out cached);
					}

					if (cached != null)
					{
						Vector2 previewPos = new Vector2(x, baseY + texH * 0.6f);
						float previewScale = sizeMultiplier * appearScale * impulseScale; // appliquer le multiplicateur directement avec l'effet d'impulsion

						// Calculer le regard local pour que l'aperçu du carrousel suive la souris
						var gaze = ComputeGazeOffsetsFor(previewPos, Raylib.GetMousePosition(), 1f);

						EntityRenderer.DrawEntity(
							speciesName: cached.Species,
							anim: "idle",
							frame: 0,
							prog: 0f,
							facing: 1f,
							pos: previewPos,
								tint: string.Equals(cached.Species, "human", StringComparison.OrdinalIgnoreCase) ? cached.Skin : cached.MorphTint,
							hBase: cached.HairBase,
							hOverlay: cached.HairOverlay,
							hColor: cached.HairColor,
							skeletons: SpeciesData.Skeletons,
							eyes: Program.EyesTexture,
							mouth: Program.MouthTexture,
							customScale: previewScale,
							equipment: cached.Equipment,
							isCarrying: false,
							inWater: false,
							attackSwingProgress: 0f,
							heldItemTexture: default,
							headAngle: 0f,
							keepItemHorizontal: false,
							isBow: false,
							underwearTexture: Program.LeafUnderpantsTexture,
							beardStyle: cached.BeardStyle,
								randomFeatureVariant: cached.FeatureVariants,
								randomFeatureColor: cached.FeatureColors,
							pupilLeftOffset: gaze.pupilLeft,
							pupilRightOffset: gaze.pupilRight,
							eyebrowLeftOffset: gaze.browLeft,
							eyebrowRightOffset: gaze.browRight
						);
					}
				}

				Rectangle hitRect = new Rectangle(x - texW / 2f, baseY, texW, texH);
				bool isCenter = itemIndex == _worldCarouselIndex && absOffset < 0.5f;
				bool hovered = Raylib.CheckCollisionPointRec(mousePos, hitRect);

				if (isCenter)
				{
					_mainMenuCenterHovered = hovered;
					string label = isJoinSlot ? Localization.Get("menu.join_game") : isCreateSlot ? Localization.Get("menu.create_world") : _availableSaves[itemIndex - 2].Name;
					int labelFontSize = Math.Max(16, (int)(sh * 0.022f));
					int labelWidth = FontManager.MeasureText(label, labelFontSize);
					// Relever légèrement l'interface (elle était calée sur les pieds du personnage)
				float overlayRaise = Math.Max(42f, texH * 0.38f);
				int labelX = (int)(x - labelWidth / 2f);
				int labelY = (int)(baseY + texH + 12 - overlayRaise);
				
				// Affichage du prénom avec bordure noire (comme en multijoueur)
				// Bordure noire : le texte est redessiné en noir tout autour avant la passe finale en blanc
				foreach (var (ox, oy) in new (int, int)[] { (-1, -1), (1, -1), (-1, 1), (1, 1), (-1, 0), (1, 0), (0, -1), (0, 1) })
				{
					FontManager.DrawText(label, labelX + ox, labelY + oy, labelFontSize, new Color((byte)0, (byte)0, (byte)0, (byte)(230 * visibility)));
				}
				FontManager.DrawText(label, labelX, labelY, labelFontSize, new Color((byte)255, (byte)255, (byte)255, (byte)(255 * visibility)));
			}

			// Bouton "supprimer" (assets/gui/bin.png), sous le rendu de chaque monde existant
			// (pas sur le bouton "Créer un monde" ou "Rejoindre une partie"). Sa taille suit celle du personnage affiché.
			bool binHovered = false;
			Rectangle binRect = default;
			int saveIndex = itemIndex - 2;
			if (!isCreateSlot && !isJoinSlot && saveIndex >= 0 && saveIndex < _availableSaves.Count && _worldDeleteIcon.Id != 0 && _worldDeleteIcon.Width > 0 && _worldDeleteIcon.Height > 0)
			{
				float binSize = Math.Max(22f, texW * 0.32f);
				// Réhausser la poubelle lorsque l'élément est au centre pour qu'elle suive l'overlay relevé
				float binY = (isCenter ? baseY + texH + 24 + Math.Max(18, (int)(sh * 0.026f)) + 16 : baseY + texH + 22) - Math.Max(42f, texH * 0.38f);
				binRect = new Rectangle(x - binSize / 2f, binY, binSize, binSize);
				binHovered = Raylib.CheckCollisionPointRec(mousePos, binRect) && visibility > 0.99f;

				Color binBg = binHovered ? new Color((byte)90, (byte)40, (byte)40, (byte)(220 * visibility)) : new Color((byte)40, (byte)25, (byte)25, (byte)(180 * visibility));
				Raylib.DrawRectangleRounded(binRect, 0.25f, 6, binBg);
				Raylib.DrawRectangleRoundedLines(binRect, 0.25f, 6, 1, new Color((byte)150, (byte)90, (byte)90, (byte)(200 * visibility)));

				float pad = binSize * 0.18f;
				Rectangle binSrc = new Rectangle(0, 0, _worldDeleteIcon.Width, _worldDeleteIcon.Height);
				Rectangle binDst = new Rectangle(binRect.X + pad, binRect.Y + pad, binRect.Width - pad * 2, binRect.Height - pad * 2);
				Raylib.DrawTexturePro(_worldDeleteIcon, binSrc, binDst, Vector2.Zero, 0, new Color((byte)255, (byte)255, (byte)255, (byte)(255 * visibility)));

				if (binHovered && _worldPendingDeleteName == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					_worldPendingDeleteName = _availableSaves[saveIndex].Name;
				}
			}

			if (hovered && !binHovered && _worldPendingDeleteName == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				if (isCenter)
				{
					ConfirmWorldCarouselSelection();
				}
				else
				{
					_worldCarouselIndex = itemIndex; // clic sur un côté : on le fait glisser au centre
				}
			}
		}

		// Flèches gauche/droite (indicateurs visuels, la navigation clavier fonctionne aussi).
		byte arrowAlpha = (byte)(200 * visibility);
			Color arrowTint = new Color((byte)220, (byte)200, (byte)160, arrowAlpha);
			if (_carouselArrowIcon.Id != 0 && _carouselArrowIcon.Width > 0 && _carouselArrowIcon.Height > 0)
			{
				float arrowH = Math.Max(28f, sh * 0.05f);
				float arrowW = arrowH * ((float)_carouselArrowIcon.Width / _carouselArrowIcon.Height);
				Rectangle arrowSrcRight = new Rectangle(0, 0, _carouselArrowIcon.Width, _carouselArrowIcon.Height);
				Rectangle arrowSrcLeft = new Rectangle(_carouselArrowIcon.Width, 0, -_carouselArrowIcon.Width, _carouselArrowIcon.Height);
				Vector2 arrowOrigin = new Vector2(arrowW / 2f, arrowH / 2f);

				if (_worldCarouselIndex > 0)
				{
					// La texture pointe vers la droite par défaut : rectangle source inversé pour pointer vers la gauche.
					Rectangle dst = new Rectangle(sw * 0.06f + arrowW / 2f, centerY, arrowW, arrowH);
					Raylib.DrawTexturePro(_carouselArrowIcon, arrowSrcLeft, dst, arrowOrigin, 0, arrowTint);
				}
				if (_worldCarouselIndex < totalItems - 1)
				{
					Rectangle dst = new Rectangle(sw * 0.92f + arrowW / 2f, centerY, arrowW, arrowH);
					Raylib.DrawTexturePro(_carouselArrowIcon, arrowSrcRight, dst, arrowOrigin, 0, arrowTint);
				}
			}
			else
			{
				// Repli si la texture est manquante : anciens indicateurs texte.
				int arrowFontSize = Math.Max(24, (int)(sh * 0.04f));
				if (_worldCarouselIndex > 0)
					FontManager.DrawText("<", (int)(sw * 0.06f), centerY - arrowFontSize / 2, arrowFontSize, arrowTint);
				if (_worldCarouselIndex < totalItems - 1)
					FontManager.DrawText(">", (int)(sw * 0.92f), centerY - arrowFontSize / 2, arrowFontSize, arrowTint);
			}
		}

		//  MULTIJOUEUR EN LIGNE : HUD réseau affiché en coin supérieur droit en jeu ───
		static void DrawNetworkHUD()
		{
			int sw = Raylib.GetScreenWidth();
			var players = NetworkManager.GetConnectedPlayersInfo();
			int lineH = 24;
			int extraLines = 0;
			bool showInternetInfo = NetworkManager.IsHost; // on garde pour les infos IP
			if (showInternetInfo) { extraLines = 1; if (NetworkManager.UpnpInProgress || !NetworkManager.UpnpMappingActive) extraLines++; }
			int panelH = 28 + players.Count * lineH + extraLines * lineH + (showInternetInfo ? 6 : 0);
			int panelW = 320;
			int px = sw - panelW - 10;
			int py = 10;

			Raylib.DrawRectangleRounded(new Rectangle(px, py, panelW, panelH), 0.15f, 6, new Color(10, 10, 20, 180));
			Raylib.DrawRectangleRoundedLines(new Rectangle(px, py, panelW, panelH), 0.15f, 6, 1,
				NetworkManager.AmIHost ? new Color(100, 220, 150, 200) : new Color(100, 160, 255, 200));

			//  Utilisation de AmIHost au lieu de IsHost
			string role = NetworkManager.AmIHost ? "Hôte" : "Client";
			FontManager.DrawText(role, px + 8, py + 5, 13, NetworkManager.AmIHost ? new Color(120, 255, 160, 255) : new Color(140, 200, 255, 255));

			int cursorY = py + 22;

			foreach (var (id, name, pvp) in players)
			{
				bool isSelf = (id == NetworkManager.LocalId);
				bool isHostPlayer = (id == 0); //  l'hôte a toujours l'ID 0

				Rectangle entryRect = new Rectangle(px + 4, cursorY, panelW - 8, lineH);
				bool hovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), entryRect);

				Texture2D statusIcon = isHostPlayer ? _serverOwnerIcon : _serverClientIcon;
				if (statusIcon.Id != 0)
				{
					float iconSize = 16;
					Vector2 pos = new Vector2(entryRect.X + 4, entryRect.Y + (lineH - iconSize) / 2);
					Raylib.DrawTextureEx(statusIcon, pos, 0, iconSize / statusIcon.Width, Color.White);
				}
				else
				{
					string bullet = isHostPlayer ? "" : "•";
					FontManager.DrawText(bullet, (int)entryRect.X + 4, cursorY, 13, isHostPlayer ? new Color(255, 215, 0, 255) : Color.White);
				}

				string displayName = isSelf ? name + " (vous)" : name;
				float nameX = entryRect.X + 26;
				FontManager.DrawText(displayName, (int)nameX, cursorY, 13, isSelf ? new Color(255, 230, 120, 255) : Color.White);

				//  PVP : icône à côté du pseudo (activable seulement pour soi-même,
				// visible en lecture seule pour les autres joueurs de la liste).
				{
					int nameWidth = FontManager.MeasureText(displayName, 13);
					float pvpIconSize = 16f;
					float pvpX = nameX + nameWidth + 6f;
					Texture2D pvpIcon = pvp ? _serverPvpOnIcon : _serverPvpOffIcon;
					Rectangle pvpRect = new Rectangle(pvpX, entryRect.Y + (lineH - pvpIconSize) / 2, pvpIconSize, pvpIconSize);
					bool pvpHover = isSelf && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), pvpRect);

					if (pvpHover)
						Raylib.DrawRectangleRounded(pvpRect, 0.2f, 6, new Color(255, 255, 255, 40));

					if (pvpIcon.Id != 0)
					{
						Color pvpTint = pvp ? new Color(255, 90, 90, 235) : new Color(200, 200, 200, isSelf ? 210 : 150);
						Raylib.DrawTextureEx(pvpIcon, new Vector2(pvpRect.X, pvpRect.Y), 0, pvpIconSize / pvpIcon.Width, pvpTint);
					}
					else
					{
						// Repli si les textures sont manquantes : pastille colorée + texte.
						Raylib.DrawCircle((int)(pvpRect.X + pvpIconSize / 2), (int)(pvpRect.Y + pvpIconSize / 2), pvpIconSize / 2,
							pvp ? new Color(220, 60, 60, 220) : new Color(90, 90, 90, 180));
						FontManager.DrawText("P", (int)(pvpRect.X + 4), (int)(pvpRect.Y + 1), 11, Color.White);
					}

					if (isSelf && pvpHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						Program.ToggleLocalPvp();
					}
				}

// Icônes d'action (téléportation et kick) – uniquement pour l'hôte
			if (NetworkManager.AmIHost && !isSelf)
			{
				float iconSize = 18f;
				float spacing = 4f;

				// Position de base : juste à droite du nom
				float baseX = entryRect.X + entryRect.Width - iconSize - 4f;

				// Icône de kick (à gauche)
				if (_serverKickIcon.Id != 0)
				{
					float kickX = baseX - (iconSize + spacing);
					Rectangle kickRect = new Rectangle(kickX, entryRect.Y + (lineH - iconSize) / 2, iconSize, iconSize);
					bool kickHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), kickRect);

					if (kickHover)
						Raylib.DrawRectangleRounded(kickRect, 0.2f, 6, new Color(255, 80, 80, 40));

					Raylib.DrawTextureEx(_serverKickIcon, new Vector2(kickRect.X, kickRect.Y), 0, iconSize / _serverKickIcon.Width,
						kickHover ? Color.White : new Color(200, 200, 200, 180));

					if (kickHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						NetworkManager.KickPlayer(id);
						Program.AddNotification(new Notification($"Joueur {name} expulsé.", new Color(255, 100, 100, 255), 2f));
					}
				}

				// Icône de téléportation (à droite)
				if (_serverTeleportIcon.Id != 0)
				{
					float teleX = baseX;
					Rectangle teleRect = new Rectangle(teleX, entryRect.Y + (lineH - iconSize) / 2, iconSize, iconSize);
					bool teleHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), teleRect);

					if (teleHover)
						Raylib.DrawRectangleRounded(teleRect, 0.2f, 6, new Color(255, 255, 255, 40));

					Raylib.DrawTextureEx(_serverTeleportIcon, new Vector2(teleRect.X, teleRect.Y), 0, iconSize / _serverTeleportIcon.Width,
						teleHover ? Color.White : new Color(200, 200, 200, 180));

					if (teleHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						Vector2 hostPos = Program.GetPlayerPosition();
						NetworkManager.SendTeleport(id, hostPos);
						Program.AddNotification(new Notification($"Téléportation de {name} vers l'hôte", new Color(100, 255, 200, 255), 2f));
					}
					}
				}

				cursorY += lineH;
			}

			// IP publique + statut UPnP (uniquement côté hôte)
			if (showInternetInfo)
			{
				cursorY += 4;
				if (NetworkManager.UpnpInProgress)
				{
					FontManager.DrawText(Localization.Get("network.searching_public_ip"), px + 10, cursorY, 12, new Color(200, 190, 160, 220));
					cursorY += lineH;
				}
				else if (!string.IsNullOrEmpty(NetworkManager.PublicIp))
				{
					FontManager.DrawText(string.Format(Localization.Get("network.public_ip"), NetworkManager.PublicIp), px + 10, cursorY, 12, new Color(180, 255, 200, 230));
					cursorY += lineH;
					if (!NetworkManager.UpnpMappingActive)
					{
						FontManager.DrawText(Localization.Get("network.port_not_forwarded"), px + 10, cursorY, 11, new Color(255, 190, 120, 220));
						cursorY += lineH;
					}
				}
				else
				{
					FontManager.DrawText(Localization.Get("network.public_ip_unavailable"), px + 10, cursorY, 12, new Color(230, 160, 160, 220));
					cursorY += lineH;
				}
			}
		}

		//  MULTIJOUEUR EN LIGNE : affichage du sous-menu Héberger / Rejoindre ──────
		static void DrawMultiplayerMenu()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			Vector2 mousePos = Raylib.GetMousePosition();

			int panelW = 560;
			//  Le panneau s'agrandit pour l'écran "Rejoindre" s'il y a des serveurs récents à afficher,
			// et pour l'écran de choix du personnage selon le nombre de personnages déjà créés.
			int savedServersShown = _isJoinSetup ? Math.Min(SavedServers.Entries.Count, 5) : 0;
			int characterRowsShown = _isCharacterSelect ? _joinCharacterOptions.Count + 1 : 0; // +1 = "Nouveau personnage"
			int panelH = _isCharacterSelect
				? 200 + characterRowsShown * 58
				: 420 + (savedServersShown > 0 ? 40 + savedServersShown * 34 : 0);
			int px = sw / 2 - panelW / 2, py = sh / 2 - panelH / 2;
			Rectangle panel = new Rectangle(px, py, panelW, panelH);
			Raylib.DrawRectangleRounded(panel, 0.06f, 8, new Color(18, 18, 26, 235));
			Raylib.DrawRectangleRoundedLines(panel, 0.06f, 8, 2, new Color(200, 170, 100, 200));

			if (_isWorldLookupPending)
			{
				FontManager.DrawText("Recherche du monde...", px + 40, py + 40, 24, new Color(230, 200, 100, 255));
				FontManager.DrawText("Récupération de l'identité du monde hébergé.", px + 40, py + 100, 16, Color.White);
				FontManager.DrawText(Localization.Get("multiplayer.cancel_hint"), px + 40, py + panelH - 40, 12, new Color(150, 140, 120, 200));
				return;
			}

			if (_mpWaitingForWorld)
			{
				FontManager.DrawText(Localization.Get("multiplayer.connecting"), px + 40, py + 40, 26, new Color(230, 200, 100, 255));
				FontManager.DrawText(_mpStatusMessage, px + 40, py + 100, 16, Color.White);
				FontManager.DrawText(Localization.Get("multiplayer.cancel_hint"), px + 40, py + panelH - 40, 12, new Color(150, 140, 120, 200));
				return;
			}

			if (_isHostSetup)
			{
				FontManager.DrawText(Localization.Get("multiplayer.host_game_title"), px + 40, py + 30, 24, new Color(230, 200, 100, 255));
				DrawMpTextField(Localization.Get("multiplayer.player_name"), _mpPseudoInput, px + 40, py + 90, panelW - 80, 0);
				DrawMpTextField(Localization.Get("multiplayer.port_default"), _mpPortInput, px + 40, py + 170, panelW - 80, 1);
				if (_isHostingFromPause)
				{
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_current_game"), px + 40, py + 230, 13, new Color(160, 150, 130, 200));
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_upnp"), px + 40, py + 248, 13, new Color(160, 150, 130, 200));
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_network"), px + 40, py + 266, 13, new Color(160, 150, 130, 200));
				}
				else
				{
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_upnp_friends"), px + 40, py + 230, 13, new Color(160, 150, 130, 200));
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_outside_network"), px + 40, py + 248, 13, new Color(160, 150, 130, 200));
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_manual_port"), px + 40, py + 266, 13, new Color(160, 150, 130, 200));
				}

				_mpConfirmButtonRect = new Rectangle(px + 40, py + panelH - 80, panelW - 80, 50);
				bool hov = Raylib.CheckCollisionPointRec(mousePos, _mpConfirmButtonRect);
				Raylib.DrawRectangleRounded(_mpConfirmButtonRect, 0.2f, 8, hov ? new Color(80, 70, 40, 230) : new Color(50, 45, 30, 200));
				Raylib.DrawRectangleRoundedLines(_mpConfirmButtonRect, 0.2f, 8, 2, new Color(200, 170, 100, 220));
				string confirmLabel = _isHostingFromPause ? Localization.Get("multiplayer.host_button") : Localization.Get("multiplayer.select_save_button");
				FontManager.DrawText(confirmLabel, px + 70, py + panelH - 65, 18, Color.White);
				FontManager.DrawText(Localization.Get("multiplayer.return_hint"), px + 40, py + panelH - 22, 12, new Color(150, 140, 120, 200));
				return;
			}

			if (_isJoinSetup)
			{
				FontManager.DrawText(Localization.Get("multiplayer.join_game_title"), px + 40, py + 30, 24, new Color(230, 200, 100, 255));
				DrawMpTextField(Localization.Get("multiplayer.player_name"), _mpPseudoInput, px + 40, py + 90, panelW - 80, 0);
				DrawMpTextField(Localization.Get("multiplayer.host_ip"), _mpHostIpInput, px + 40, py + 170, panelW - 80, 1);
				DrawMpTextField(Localization.Get("multiplayer.port"), _mpPortInput, px + 40, py + 250, panelW - 80, 2);

				//  Liste des serveurs déjà rejoints (Data/servers.json) : cliquer une ligne
				// remplit IP/port automatiquement, la croix la retire de la liste.
				if (savedServersShown > 0)
				{
					FontManager.DrawText("Serveurs récents :", px + 40, py + 316, 13, new Color(160, 150, 130, 220));
					int rowY = py + 336;
					for (int i = 0; i < savedServersShown; i++)
					{
						var entry = SavedServers.Entries[i];
						Rectangle rowRect = new Rectangle(px + 40, rowY, panelW - 80 - 36, 28);
						Rectangle delRect = new Rectangle(px + 40 + panelW - 80 - 30, rowY, 28, 28);
						bool rowHover = Raylib.CheckCollisionPointRec(mousePos, rowRect);
						Raylib.DrawRectangleRounded(rowRect, 0.2f, 6, rowHover ? new Color(60, 50, 35, 220) : new Color(28, 28, 38, 190));
						Raylib.DrawRectangleRoundedLines(rowRect, 0.2f, 6, 1, new Color(90, 80, 65, 160));
						string label = string.IsNullOrEmpty(entry.Name) ? $"{entry.Ip}:{entry.Port}" : $"{entry.Name}  ({entry.Ip}:{entry.Port})";
						FontManager.DrawText(label, (int)rowRect.X + 10, (int)rowRect.Y + 6, 14, Color.White);
						if (rowHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
						{
							_mpHostIpInput = entry.Ip;
							_mpPortInput = entry.Port.ToString();
						}

						bool delHover = Raylib.CheckCollisionPointRec(mousePos, delRect);
						Raylib.DrawRectangleRounded(delRect, 0.3f, 6, delHover ? new Color(140, 40, 40, 220) : new Color(60, 30, 30, 180));
						FontManager.DrawText("x", (int)delRect.X + 9, (int)delRect.Y + 5, 16, Color.White);
						if (delHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
						{
							SavedServers.Remove(entry);
						}
						rowY += 34;
					}
				}

				_mpConfirmButtonRect = new Rectangle(px + 40, py + panelH - 80, panelW - 80, 50);
				bool hov = Raylib.CheckCollisionPointRec(mousePos, _mpConfirmButtonRect);
				Raylib.DrawRectangleRounded(_mpConfirmButtonRect, 0.2f, 8, hov ? new Color(80, 70, 40, 230) : new Color(50, 45, 30, 200));
				Raylib.DrawRectangleRoundedLines(_mpConfirmButtonRect, 0.2f, 8, 2, new Color(200, 170, 100, 220));
				FontManager.DrawText(Localization.Get("multiplayer.connect_button"), px + 70, py + panelH - 65, 18, Color.White);
				if (!string.IsNullOrEmpty(_mpStatusMessage))
					FontManager.DrawText(_mpStatusMessage, px + 40, py + panelH - 110, 14, new Color(255, 140, 120, 230));
				FontManager.DrawText(Localization.Get("multiplayer.return_hint"), px + 40, py + panelH - 22, 12, new Color(150, 140, 120, 200));
				return;
			}

			if (_isCharacterSelect)
			{
				FontManager.DrawText("Choisir un personnage", px + 40, py + 30, 24, new Color(230, 200, 100, 255));
				FontManager.DrawText($"Sur {_pendingJoinIp}:{_pendingJoinPort}", px + 40, py + 62, 13, new Color(160, 150, 130, 200));

				_characterSelectRowRects.Clear();
				int rowY2 = py + 100;
				const int rowH = 50;

				//  Personnages déjà créés sur cet hôte (le premier, s'il correspond à l'identité
				// par défaut de l'installation, est affiché "Personnage principal").
				for (int i = 0; i < _joinCharacterOptions.Count; i++)
				{
					var profile = _joinCharacterOptions[i];
					Rectangle rowRect = new Rectangle(px + 40, rowY2, panelW - 80, rowH - 8);
					_characterSelectRowRects.Add(rowRect);
					bool rowHover = Raylib.CheckCollisionPointRec(mousePos, rowRect);
					Raylib.DrawRectangleRounded(rowRect, 0.15f, 6, rowHover ? new Color(70, 60, 40, 230) : new Color(32, 32, 42, 200));
					Raylib.DrawRectangleRoundedLines(rowRect, 0.15f, 6, 1.5f, new Color((byte)200, (byte)170, (byte)100, rowHover ? (byte)220 : (byte)140));
					string label = profile.Guid == DefaultLocalPlayerGuidReadOnly ? $"{profile.Label} (par défaut)" : profile.Label;
					FontManager.DrawText(label, (int)rowRect.X + 16, (int)rowRect.Y + 14, 16, Color.White);
					rowY2 += rowH;
				}

				// "+ Nouveau personnage" — toujours en dernière position.
				{
					Rectangle newRowRect = new Rectangle(px + 40, rowY2, panelW - 80, rowH - 8);
					_characterSelectRowRects.Add(newRowRect);
					bool rowHover = Raylib.CheckCollisionPointRec(mousePos, newRowRect);
					Raylib.DrawRectangleRounded(newRowRect, 0.15f, 6, rowHover ? new Color(60, 80, 60, 230) : new Color(28, 38, 30, 200));
					Raylib.DrawRectangleRoundedLines(newRowRect, 0.15f, 6, 1.5f, new Color((byte)140, (byte)220, (byte)140, rowHover ? (byte)230 : (byte)150));
					FontManager.DrawText("+ Nouveau personnage", (int)newRowRect.X + 16, (int)newRowRect.Y + 14, 16, new Color(200, 240, 200, 255));
				}

				FontManager.DrawText("Échap : retour", px + 40, py + panelH - 22, 12, new Color(150, 140, 120, 200));
				return;
			}

			// Menu Héberger / Rejoindre / Retour
			FontManager.DrawText(Localization.Get("multiplayer.title"), px + 40, py + 30, 28, new Color(230, 200, 100, 255));
			string[] options = { Localization.Get("multiplayer.host_game"), Localization.Get("multiplayer.join_game"), Localization.Get("button.return") };
			_hoveredMpSubOption = -1;
			for (int i = 0; i < options.Length; i++)
			{
				Rectangle r = new Rectangle(px + 40, py + 110 + i * 70, panelW - 80, 55);
				bool hov = Raylib.CheckCollisionPointRec(mousePos, r);
				if (hov) _hoveredMpSubOption = i;
				bool sel = i == _multiplayerSubOption;
				Raylib.DrawRectangleRounded(r, 0.2f, 8, sel || hov ? new Color(60, 50, 35, 220) : new Color(28, 28, 38, 190));
				Raylib.DrawRectangleRoundedLines(r, 0.2f, 8, 2, sel || hov ? new Color(200, 170, 100, 255) : new Color(90, 80, 65, 160));
				FontManager.DrawText(options[i], (int)r.X + 25, (int)r.Y + 16, 20, sel || hov ? new Color(255, 220, 100, 255) : Color.White);
			}
			FontManager.DrawText(Localization.Get("multiplayer.return_to_menu_hint"), px + 40, py + panelH - 30, 12, new Color(150, 140, 120, 200));
		}

		public static class KeyBindings
		{
			private static readonly Dictionary<GameAction, List<InputBinding>> _defaults = new()
			{
				{ GameAction.MoveUp, new() { new(InputType.Keyboard, (int)KeyboardKey.W) } },
				{ GameAction.MoveDown, new() { new(InputType.Keyboard, (int)KeyboardKey.S) } },
				{ GameAction.MoveLeft, new() { new(InputType.Keyboard, (int)KeyboardKey.A) } },
				{ GameAction.MoveRight, new() { new(InputType.Keyboard, (int)KeyboardKey.D) } },
				{ GameAction.Sprint, new() { new(InputType.Keyboard, (int)KeyboardKey.LeftShift) } },
				{ GameAction.Interact, new() { new(InputType.Keyboard, (int)KeyboardKey.E) } },
				{ GameAction.Inventory, new() { new(InputType.Keyboard, (int)KeyboardKey.I) } },
				{ GameAction.Character, new() { new(InputType.Keyboard, (int)KeyboardKey.C) } },
				{ GameAction.Drop, new() { new(InputType.Keyboard, (int)KeyboardKey.F) } },
				{ GameAction.RadialMenu, new() { new(InputType.Mouse, (int)MouseButton.Middle) } },
				{ GameAction.ChatToggle, new() { new(InputType.Keyboard, (int)KeyboardKey.T) } },
				{ GameAction.PushToTalk, new() { new(InputType.Keyboard, (int)KeyboardKey.V) } },
				{ GameAction.PauseMenu, new() { new(InputType.Keyboard, (int)KeyboardKey.Escape) } },
			};

			public static Dictionary<GameAction, List<InputBinding>> Current = CloneBindings(_defaults);

			public static Dictionary<GameAction, List<InputBinding>> CloneCurrent() => CloneBindings(Current);

			public static void Apply(Dictionary<GameAction, List<InputBinding>> bindings)
			{
				Current = CloneBindings(bindings);
				Save();
			}

			public static void Restore(Dictionary<GameAction, List<InputBinding>> bindings) => Current = CloneBindings(bindings);

			public static bool HasChanges(Dictionary<GameAction, List<InputBinding>> original)
			{
				if (original.Count != Current.Count) return true;
				return Current.Any(pair => !original.TryGetValue(pair.Key, out var bindings) || !bindings.SequenceEqual(pair.Value));
			}

			private const string SavePath = "Data/keybindings.json";
			private const float GamepadAxisDeadzone = 0.15f;
			private const float GamepadAxisCaptureThreshold = 0.5f;
			private static readonly Dictionary<int, bool> _gamepadAxisPressedStates = new();
			private static readonly Dictionary<int, bool> _gamepadAxisReleasedStates = new();

			public static readonly GamepadButton[] CapturableGamepadButtons =
			{
				GamepadButton.LeftFaceUp, GamepadButton.LeftFaceDown,
				GamepadButton.LeftFaceLeft, GamepadButton.LeftFaceRight,
				GamepadButton.RightFaceUp, GamepadButton.RightFaceDown,
				GamepadButton.RightFaceLeft, GamepadButton.RightFaceRight,
				GamepadButton.LeftTrigger1, GamepadButton.LeftTrigger2,
				GamepadButton.RightTrigger1, GamepadButton.RightTrigger2,
				GamepadButton.MiddleLeft, GamepadButton.Middle,
				GamepadButton.MiddleRight, GamepadButton.LeftThumb,
				GamepadButton.RightThumb
			};

			public static bool IsDown(GameAction action)
			{
				return Current[action].Any(IsDown);
			}

			public static bool IsPressed(GameAction action)
			{
				return Current[action].Any(IsPressed);
			}

			public static bool IsReleased(GameAction action)
			{
				return Current[action].Any(IsReleased);
			}

			private static bool IsDown(InputBinding binding)
			{
				if (binding.Type == InputType.Keyboard)
					return Raylib.IsKeyDown((KeyboardKey)binding.Code);
				if (binding.Type == InputType.Mouse)
					return Raylib.IsMouseButtonDown((MouseButton)binding.Code);
				if (binding.Type == InputType.GamepadAxis)
					return TryGetPrimaryGamepadIndex(out int axisIndex) && IsGamepadAxisDirectionDown(axisIndex, (GamepadAxisDirection)binding.Code, GamepadAxisDeadzone);
				return TryGetPrimaryGamepadIndex(out int index) && Raylib.IsGamepadButtonDown(index, (GamepadButton)binding.Code);
			}

			private static bool IsPressed(InputBinding binding)
			{
				if (binding.Type == InputType.Keyboard)
					return Raylib.IsKeyPressed((KeyboardKey)binding.Code);
				if (binding.Type == InputType.Mouse)
					return Raylib.IsMouseButtonPressed((MouseButton)binding.Code);
				if (binding.Type == InputType.GamepadAxis)
				{
					bool isDown = TryGetAvailableGamepadIndex(out int axisIndex) && IsGamepadAxisDirectionDown(axisIndex, (GamepadAxisDirection)binding.Code, GamepadAxisDeadzone);
					bool wasDown = _gamepadAxisPressedStates.TryGetValue(binding.Code, out bool previous) && previous;
					_gamepadAxisPressedStates[binding.Code] = isDown;
					return isDown && !wasDown;
				}
				return TryGetPrimaryGamepadIndex(out int index) && Raylib.IsGamepadButtonPressed(index, (GamepadButton)binding.Code);
			}

			private static bool IsReleased(InputBinding binding)
			{
				if (binding.Type == InputType.Keyboard)
					return Raylib.IsKeyReleased((KeyboardKey)binding.Code);
				if (binding.Type == InputType.Mouse)
					return Raylib.IsMouseButtonReleased((MouseButton)binding.Code);
				if (binding.Type == InputType.GamepadAxis)
				{
					bool isDown = TryGetAvailableGamepadIndex(out int axisIndex) && IsGamepadAxisDirectionDown(axisIndex, (GamepadAxisDirection)binding.Code, GamepadAxisDeadzone);
					bool wasDown = _gamepadAxisReleasedStates.TryGetValue(binding.Code, out bool previous) && previous;
					_gamepadAxisReleasedStates[binding.Code] = isDown;
					return wasDown && !isDown;
				}
				return TryGetPrimaryGamepadIndex(out int index) && Raylib.IsGamepadButtonReleased(index, (GamepadButton)binding.Code);
			}

			private static bool IsGamepadAxisDirectionDown(int gamepadIndex, GamepadAxisDirection direction, float threshold)
			{
				float movement = direction switch
				{
					GamepadAxisDirection.LeftXNegative or GamepadAxisDirection.LeftXPositive => Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.LeftX),
					GamepadAxisDirection.LeftYNegative or GamepadAxisDirection.LeftYPositive => Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.LeftY),
					GamepadAxisDirection.RightXNegative or GamepadAxisDirection.RightXPositive => Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.RightX),
					_ => Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.RightY)
				};
				return direction switch
				{
					GamepadAxisDirection.LeftXNegative or GamepadAxisDirection.LeftYNegative or GamepadAxisDirection.RightXNegative or GamepadAxisDirection.RightYNegative => movement < -threshold,
					_ => movement > threshold
				};
			}

			private static bool TryGetAvailableGamepadIndex(out int gamepadIndex)
			{
				for (int index = 0; index < 4; index++)
				{
					if (Raylib.IsGamepadAvailable(index))
					{
						gamepadIndex = index;
						return true;
					}
				}
				gamepadIndex = -1;
				return false;
			}

			public static bool TryGetGamepadAxisDirection(int gamepadIndex, out GamepadAxisDirection direction)
			{
				direction = default;
				float axisX = Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.LeftX);
				float axisY = Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.LeftY);
				float rightAxisX = Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.RightX);
				float rightAxisY = Raylib.GetGamepadAxisMovement(gamepadIndex, GamepadAxis.RightY);
				float strongestAxis = MathF.Max(MathF.Max(MathF.Abs(axisX), MathF.Abs(axisY)), MathF.Max(MathF.Abs(rightAxisX), MathF.Abs(rightAxisY)));
				if (strongestAxis < GamepadAxisCaptureThreshold)
					return false;
				if (strongestAxis == MathF.Abs(axisX))
				{
					direction = axisX < 0 ? GamepadAxisDirection.LeftXNegative : GamepadAxisDirection.LeftXPositive;
					return true;
				}
				if (strongestAxis == MathF.Abs(axisY))
				{
					direction = axisY < 0 ? GamepadAxisDirection.LeftYNegative : GamepadAxisDirection.LeftYPositive;
					return true;
				}
				if (strongestAxis == MathF.Abs(rightAxisX))
				{
					direction = rightAxisX < 0 ? GamepadAxisDirection.RightXNegative : GamepadAxisDirection.RightXPositive;
					return true;
				}
				direction = rightAxisY < 0 ? GamepadAxisDirection.RightYNegative : GamepadAxisDirection.RightYPositive;
				return true;
			}

			private static Dictionary<GameAction, List<InputBinding>> CloneBindings(Dictionary<GameAction, List<InputBinding>> source) =>
				source.ToDictionary(kv => kv.Key, kv => new List<InputBinding>(kv.Value));

			public static IReadOnlyList<InputBinding> GetBindings(GameAction action) => Current[action];

			public static string DisplayName(GameAction action) => action switch
			{
				GameAction.MoveUp => Localization.GetOrDefault("controls.move_up", "Avancer"),
				GameAction.MoveDown => Localization.GetOrDefault("controls.move_down", "Reculer"),
				GameAction.MoveLeft => Localization.GetOrDefault("controls.move_left", "Aller à gauche"),
				GameAction.MoveRight => Localization.GetOrDefault("controls.move_right", "Aller à droite"),
				GameAction.Sprint => Localization.GetOrDefault("controls.sprint", "Sprinter"),
				GameAction.Interact => Localization.GetOrDefault("controls.interact", "Interagir"),
				GameAction.Inventory => Localization.GetOrDefault("controls.inventory", "Inventaire"),
				GameAction.Character => Localization.GetOrDefault("controls.character", "Fiche de personnage"),
				GameAction.Drop => Localization.GetOrDefault("controls.drop", "Ramasser un meuble"),
				GameAction.RadialMenu => Localization.GetOrDefault("controls.radial_menu", "Menu radial"),
				GameAction.ChatToggle => Localization.GetOrDefault("controls.chat_toggle", "Chat"),
				GameAction.PushToTalk => Localization.GetOrDefault("controls.push_to_talk", "Push to talk"),
				GameAction.PauseMenu => Localization.GetOrDefault("controls.pause_menu", "Pause / Retour"),
				_ => action.ToString()
			};

			public static string BindingDisplayName(InputBinding binding)
			{
				if (binding.Type == InputType.Keyboard)
				{
					var key = (KeyboardKey)binding.Code;
					return key switch
					{
						KeyboardKey.LeftShift => "MAJ (gauche)",
						KeyboardKey.RightShift => "MAJ (droite)",
						KeyboardKey.LeftControl => "CTRL (gauche)",
						KeyboardKey.RightControl => "CTRL (droite)",
						KeyboardKey.Space => "ESPACE",
						KeyboardKey.Escape => "ECHAP",
						KeyboardKey.Enter => "ENTRÉE",
						KeyboardKey.Tab => "TAB",
						_ => key.ToString().ToUpperInvariant()
					};
				}
				else if (binding.Type == InputType.Mouse)
				{
					var btn = (MouseButton)binding.Code;
					return btn switch
					{
						MouseButton.Left => "Clic gauche",
						MouseButton.Right => "Clic droit",
						MouseButton.Middle => "Clic molette",
						_ => "Souris"
					};
				}
				else if (binding.Type == InputType.GamepadButton)
				{
					return (GamepadButton)binding.Code switch
					{
						GamepadButton.LeftFaceUp => "Manette haut",
						GamepadButton.LeftFaceDown => "Manette bas",
						GamepadButton.LeftFaceLeft => "Manette gauche",
						GamepadButton.LeftFaceRight => "Manette droite",
						GamepadButton.RightFaceUp => "Triangle",
						GamepadButton.RightFaceRight => "Cercle",
						GamepadButton.RightFaceDown => "Croix",
						GamepadButton.RightFaceLeft => "Carre",
						GamepadButton.LeftTrigger1 => "Gachette gauche",
						GamepadButton.LeftTrigger2 => "Gachette gauche 2",
						GamepadButton.RightTrigger1 => "Gachette droite",
						GamepadButton.RightTrigger2 => "Gachette droite 2",
						GamepadButton.LeftThumb => "Joystick gauche",
						GamepadButton.RightThumb => "Joystick droit",
						GamepadButton.MiddleLeft => "Bouton central gauche",
						GamepadButton.Middle => "Bouton central",
						GamepadButton.MiddleRight => "Bouton central droit",
						_ => "Manette"
					};
				}
				else
				{
					return (GamepadAxisDirection)binding.Code switch
					{
						GamepadAxisDirection.LeftXNegative => "Joystick gauche gauche",
						GamepadAxisDirection.LeftXPositive => "Joystick gauche droite",
						GamepadAxisDirection.LeftYNegative => "Joystick gauche haut",
						GamepadAxisDirection.LeftYPositive => "Joystick gauche bas",
						GamepadAxisDirection.RightXNegative => "Joystick droit gauche",
						GamepadAxisDirection.RightXPositive => "Joystick droit droite",
						GamepadAxisDirection.RightYNegative => "Joystick droit haut",
						GamepadAxisDirection.RightYPositive => "Joystick droit bas",
						_ => "Direction manette"
					};
				}
			}

			public static void Rebind(GameAction action, int index, InputBinding newBinding)
			{
				var bindings = Current[action];
				if (index < 0 || index >= bindings.Count || bindings[index].Equals(newBinding))
					return;
				bindings[index] = newBinding;
			}

			public static int AddBinding(GameAction action)
			{
				Current[action].Add(new InputBinding(InputType.Keyboard, (int)KeyboardKey.Null));
				return Current[action].Count - 1;
			}

			public static void RemoveBinding(GameAction action, int index)
			{
				if (index <= 0 || index >= Current[action].Count) return;
				Current[action].RemoveAt(index);
			}

			public static void ResetToDefaults()
			{
				Current = CloneBindings(_defaults);
			}

			public static void Save()
			{
				try
				{
					var asStrings = Current.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Count == 1
						? (object)kv.Value[0].ToString() : kv.Value.Select(binding => binding.ToString()).ToList());
					string json = JsonSerializer.Serialize(asStrings, new JsonSerializerOptions { WriteIndented = true });
					string? dir = Path.GetDirectoryName(SavePath);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
						Directory.CreateDirectory(dir);
					File.WriteAllText(SavePath, json);
				}
				catch (Exception ex)
				{
					Console.WriteLine($" Impossible de sauvegarder les touches assignées : {ex.Message}");
				}
			}

			public static void Load()
			{
				Current = new(_defaults);
				if (!File.Exists(SavePath))
					return;

				try
				{
					string json = File.ReadAllText(SavePath);
					var asStrings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
					if (asStrings == null)
						return;

					foreach (var kv in asStrings)
					{
						if (!Enum.TryParse(kv.Key, out GameAction action))
							continue;
						var values = kv.Value.ValueKind == JsonValueKind.Array
							? kv.Value.EnumerateArray().Select(value => value.GetString()).ToList()
							: new List<string?> { kv.Value.GetString() };
						var bindings = values.Where(value => value != null).Select(ParseBinding).Where(binding => binding.HasValue).Select(binding => binding!.Value).ToList();
						if (bindings.Count > 0) Current[action] = bindings;
					}
					Console.WriteLine(" Touches assignées chargées depuis keybindings.json");
				}
				catch (Exception ex)
				{
					Console.WriteLine($" Impossible de charger les touches assignées ({ex.Message}), valeurs par défaut utilisées.");
					Current = CloneBindings(_defaults);
				}
			}

			private static InputBinding? ParseBinding(string? value)
			{
				if (string.IsNullOrEmpty(value)) return null;
				var parts = value.Split(':');
				if (parts.Length != 2 || !Enum.TryParse(parts[0], out InputType type) || !int.TryParse(parts[1], out int code)) return null;
				return new InputBinding(type, code);
			}
		}

		static void DrawOptionsMenu()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			Vector2 mousePos = Raylib.GetMousePosition();
			float dt = Raylib.GetFrameTime();

			// ── Fond ──
			DrawScrollingMenuBackground(sw, sh, dt);

			// ── Ecran complet ──
			int panelW = sw;
			int panelH = sh;
			int px = 0;
			int py = 0;

			// ── Titre ──
			int titleFontSize = 48;
			int tw = FontManager.MeasureText(Localization.Get("menu.options"), titleFontSize);
			FontManager.DrawText(Localization.Get("menu.options"), px + panelW / 2 - tw / 2, py + 20, titleFontSize, new Color(230, 200, 100, 255));

			// ── Onglets ──
			int tabSpacing = 16;
			int tabW = Math.Min(260, Math.Max(120, (sw - 100 - tabSpacing * 2) / 3));
			int tabH = 54;
			string[] tabNames = { Localization.Get("menu.general"), Localization.Get("menu.controls"), Localization.Get("menu.interface") };
			int tabsTotalW = tabNames.Length * tabW + (tabNames.Length - 1) * tabSpacing;
			int tabStartX = px + panelW / 2 - tabsTotalW / 2;
			int tabY = py + 96;
			for (int i = 0; i < tabNames.Length; i++)
			{
				Rectangle tabRect = new Rectangle(tabStartX + i * (tabW + tabSpacing), tabY, tabW, tabH);
				bool hovered = Raylib.CheckCollisionPointRec(mousePos, tabRect);
				bool active = _optionsTab == i;
				Color tint = active ? new Color(220, 190, 100, 255) : (hovered ? new Color(205, 195, 165, 255) : new Color(180, 175, 165, 220));
				UIManager.DrawButton(tabRect, tint, hovered, true);
				int tabFontSize = tabW < 170 ? 18 : 24;
				int nw = FontManager.MeasureText(tabNames[i], tabFontSize);
				Color tcol = active ? new Color(255, 235, 170, 255) : new Color(245, 240, 230, 240);
				FontManager.DrawText(tabNames[i], (int)(tabRect.X + tabRect.Width / 2 - nw / 2), (int)(tabRect.Y + tabRect.Height / 2 - tabFontSize / 2), tabFontSize, tcol);
				if (hovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
					_optionsTab = i;
			}

			// ── Zone de contenu ──
			int contentY = tabY + tabH + 32;
			int contentHeight = panelH - (contentY - py) - 90; // Réserve pour le bouton retour
			int contentX = px + 60;
			int contentWidth = panelW - 120;

			// ── CONTENU DE L'ONGLET ──
			if (_optionsTab == 0)
			{
				// ── Volume ──
				string volLabel = string.Format(Localization.Get("options.volume"), _masterVolume);
				FontManager.DrawText(volLabel, contentX, contentY, 28, new Color(220, 210, 190, 230));

				int sliderX = contentX, sliderY = contentY + 46, sliderW = contentWidth - 20, sliderH = 20;
				Rectangle sliderRect = new Rectangle(sliderX, sliderY, sliderW, sliderH);
				Raylib.DrawRectangleRounded(sliderRect, 0.5f, 8, new Color(40, 38, 45, 220));
				Raylib.DrawRectangleRoundedLines(sliderRect, 0.5f, 8, 1, new Color(100, 90, 70, 180));
				float fillRatio = _masterVolume / 100f;
				if (fillRatio > 0f)
				{
					Rectangle fillRect = new Rectangle(sliderX, sliderY, sliderW * fillRatio, sliderH);
					Raylib.DrawRectangleRounded(fillRect, 0.5f, 8, new Color(210, 170, 90, 240));
				}
				float knobX = sliderX + sliderW * fillRatio;
				Raylib.DrawCircle((int)knobX, sliderY + sliderH / 2, 15, new Color(255, 225, 150, 255));

				Rectangle sliderHitbox = new Rectangle(sliderX - 10, sliderY - 10, sliderW + 20, sliderH + 20);
				bool sliderHovered = Raylib.CheckCollisionPointRec(mousePos, sliderHitbox);
				if (sliderHovered && (Raylib.IsMouseButtonDown(MouseButton.Left)))
				{
					float ratio = Math.Clamp((mousePos.X - sliderX) / sliderW, 0f, 1f);
					_masterVolume = (int)Math.Round(ratio * 100f);
					Raylib.SetMasterVolume(_masterVolume / 100f);
				}

				// ── Plein écran ──
				int fsY = sliderY + 70;
				string fsLabel = Localization.Get("options.fullscreen");
				FontManager.DrawText(fsLabel, contentX, fsY + 10, 28, new Color(220, 210, 190, 230));

				Rectangle fsToggleRect = new Rectangle(contentX + contentWidth - 190, fsY, 160, 52);
				bool fsHovered = Raylib.CheckCollisionPointRec(mousePos, fsToggleRect);
				Color fsBg = _fullscreen ? new Color(90, 140, 90, 220) : new Color(60, 45, 45, 220);
				if (fsHovered) fsBg = new Color((byte)Math.Min(255, fsBg.R + 20), (byte)Math.Min(255, fsBg.G + 20), (byte)Math.Min(255, fsBg.B + 20), fsBg.A);
				Raylib.DrawRectangleRounded(fsToggleRect, 0.3f, 8, fsBg);
				Raylib.DrawRectangleRoundedLines(fsToggleRect, 0.3f, 8, 2, new Color(200, 190, 160, 200));
				string fsText = _fullscreen ? Localization.Get("options.enabled") : Localization.Get("options.disabled");
				int fsw = FontManager.MeasureText(fsText, 22);
				FontManager.DrawText(fsText, (int)(fsToggleRect.X + fsToggleRect.Width / 2 - fsw / 2), (int)(fsToggleRect.Y + fsToggleRect.Height / 2 - 11), 22, Color.White);
				if (fsHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
					ApplyFullscreenToggle();

				// ── Mode du micro ──
				int voiceY = fsY + 70;
				FontManager.DrawText(Localization.GetOrDefault("options.voice_mode", "Microphone mode"), contentX, voiceY + 10, 24, new Color(220, 210, 190, 230));
				var voiceModes = new[]
				{
					("Disabled", "options.voice.disabled", "Microphone off"),
					("PushToTalk", "options.voice.push_to_talk", "Push to talk"),
					("AlwaysOn", "options.voice.always_on", "Always on")
				};
				float voiceButtonGap = 8f;
				float voiceButtonWidth = Math.Clamp((contentWidth - 220f) / voiceModes.Length, 130f, 180f);
				float voiceButtonStartX = contentX + contentWidth - voiceModes.Length * voiceButtonWidth - (voiceModes.Length - 1) * voiceButtonGap;
				for (int i = 0; i < voiceModes.Length; i++)
				{
					var option = voiceModes[i];
					Rectangle modeRect = new Rectangle(voiceButtonStartX + i * (voiceButtonWidth + voiceButtonGap), voiceY, voiceButtonWidth, 48);
					bool modeHovered = Raylib.CheckCollisionPointRec(mousePos, modeRect);
					bool modeSelected = SettingsManager.Settings.VoiceChatMode == option.Item1;
					Color modeColor = modeSelected ? new Color(100, 80, 45, 240) : new Color(35, 35, 42, 220);
					if (modeHovered)
						modeColor = new Color((byte)Math.Min(255, modeColor.R + 18), (byte)Math.Min(255, modeColor.G + 18), (byte)Math.Min(255, modeColor.B + 18), modeColor.A);
					Raylib.DrawRectangleRounded(modeRect, 0.16f, 6, modeColor);
					Raylib.DrawRectangleRoundedLines(modeRect, 0.16f, 6, 1, modeSelected ? new Color(235, 195, 105, 240) : new Color(120, 110, 90, 180));
					string modeLabel = Localization.GetOrDefault(option.Item2, option.Item3);
					int modeFontSize = 16;
					int modeLabelWidth = FontManager.MeasureText(modeLabel, modeFontSize);
					FontManager.DrawText(modeLabel, (int)(modeRect.X + (modeRect.Width - modeLabelWidth) / 2), (int)(modeRect.Y + 16), modeFontSize, Color.White);
					if (modeHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						SettingsManager.Settings.VoiceChatMode = option.Item1;
						SettingsManager.Save();
					}
				}

				// ── Langues ──
				int languageY = voiceY + 82;
				string languageLabel = Localization.Get("options.language");
				FontManager.DrawText(languageLabel, contentX, languageY + 10, 28, new Color(220, 210, 190, 230));

				int langCount = Localization.AvailableLanguages.Count;
				float flagSize = 112f;
				float colSpacing = 28f;
				float rowSpacing = 28f;
				int cols = Math.Min(5, langCount);
				int rows = (langCount + cols - 1) / cols;

				float gridX = contentX;
				float gridY = languageY + 44;
				float availableHeight = contentHeight - (gridY - contentY) - 20;
				float gridHeight = rows * flagSize + (rows - 1) * rowSpacing;
				_optionsLanguageMaxScroll = gridHeight > availableHeight ? gridHeight - availableHeight : 0f;
				_optionsLanguageScrollOffset = Math.Clamp(_optionsLanguageScrollOffset, 0f, _optionsLanguageMaxScroll);

				if (gridHeight > availableHeight)
				{
					Raylib.BeginScissorMode((int)gridX, (int)gridY, (int)(contentWidth), (int)availableHeight);
				}

				for (int i = 0; i < langCount; i++)
				{
					int row = i / cols;
					int col = i % cols;
					float x = gridX + col * (flagSize + colSpacing);
					float y = gridY + row * (flagSize + rowSpacing) - _optionsLanguageScrollOffset;

					var languageEntry = Localization.AvailableLanguages[i];
					Rectangle flagRect = new Rectangle(x, y, flagSize, flagSize);
					bool hovered = Raylib.CheckCollisionPointRec(mousePos, flagRect);
					bool selected = i == _languageSelectionIndex;

					if (_languageFlagTextures.TryGetValue(languageEntry.Code, out var flagTexture) && flagTexture.Id != 0)
					{
						Rectangle src = new Rectangle(0, 0, flagTexture.Width, flagTexture.Height);
						float aspect = (float)flagTexture.Width / flagTexture.Height;
						float drawWidth = flagSize;
						float drawHeight = flagSize;
						if (aspect > 1f)
							drawHeight = flagSize / aspect;
						else if (aspect < 1f)
							drawWidth = flagSize * aspect;
						float drawX = flagRect.X + (flagSize - drawWidth) / 2;
						float drawY = flagRect.Y + (flagSize - drawHeight) / 2;
						Rectangle dest = new Rectangle(drawX, drawY, drawWidth, drawHeight);
						Raylib.DrawTexturePro(flagTexture, src, dest, Vector2.Zero, 0f, Color.White);

						if (selected)
						{
							Raylib.DrawRectangleLinesEx(new Rectangle(drawX - 3, drawY - 3, drawWidth + 6, drawHeight + 6), 3, new Color(255, 255, 255, 220));
						}
					}
					else
					{
						string fallback = languageEntry.Code.ToUpperInvariant();
						int fallbackWidth = FontManager.MeasureText(fallback, 18);
						FontManager.DrawText(fallback, (int)(flagRect.X + flagRect.Width / 2 - fallbackWidth / 2), (int)(flagRect.Y + flagRect.Height / 2 - 9), 18, Color.White);
					}

					if (hovered && Raylib.IsMouseButtonPressed(MouseButton.Left) && y + flagSize > gridY && y < gridY + availableHeight)
					{
						if (_languageSelectionIndex != i)
						{
							_languageSelectionIndex = i;
							var newLanguage = Localization.AvailableLanguages[i];
							Localization.SetLanguage(newLanguage.Code);
						}
					}
				}

				if (gridHeight > availableHeight)
				{
					Raylib.EndScissorMode();
					float scrollbarHeight = (availableHeight / gridHeight) * availableHeight;
					float scrollbarY = gridY + (availableHeight - scrollbarHeight) * (_optionsLanguageScrollOffset / _optionsLanguageMaxScroll);
					Rectangle scrollbar = new Rectangle(contentX + contentWidth - 18, scrollbarY, 10, scrollbarHeight);
					Raylib.DrawRectangleRounded(scrollbar, 0.5f, 4, new Color(200, 200, 200, 180));
				}
			}
			else if (_optionsTab == 1) // Controls tab
			{
				// ── Contrôles avec défilement ──
				int scrollableHeight = contentHeight - 80;
				int controlsWidth = Math.Max(420, contentWidth / 2);
				int totalControlsHeight = 0;
				
				var rebindableActions = new[]
				{
					GameAction.MoveUp, GameAction.MoveDown, GameAction.MoveLeft, GameAction.MoveRight,
					GameAction.Sprint, GameAction.Interact, GameAction.Inventory, GameAction.Character,
					GameAction.Drop, GameAction.RadialMenu, GameAction.ChatToggle, GameAction.PushToTalk, GameAction.PauseMenu
				};

				int rowH = 88;
				int keyboardKeySize = 76;
				int keyGap = 12;
				int addButtonWidth = 46;
				int totalHeight = rebindableActions.Length * rowH;
				totalHeight += 50;
				totalHeight += 120;

				if (!_controlsScrollInitialized)
				{
					_controlsScrollOffset = 0f;
					_controlsScrollInitialized = true;
				}

				Rectangle scrollArea = new Rectangle(contentX, contentY, controlsWidth, scrollableHeight);
				if (Raylib.CheckCollisionPointRec(mousePos, scrollArea))
				{
					float wheel = Raylib.GetMouseWheelMove();
					if (wheel != 0f)
					{
						_controlsScrollOffset -= wheel * 25f;
						_controlsScrollOffset = Math.Clamp(_controlsScrollOffset, 0f, Math.Max(0, totalHeight - scrollableHeight));
					}
				}

				if (totalHeight > scrollableHeight)
				{
					float scrollRatio = Math.Clamp(_controlsScrollOffset / Math.Max(1, totalHeight - scrollableHeight), 0f, 1f);
					float scrollbarHeight = (scrollableHeight / totalHeight) * scrollableHeight;
					float scrollbarY = contentY + (scrollableHeight - scrollbarHeight) * scrollRatio;
					Rectangle scrollbar = new Rectangle(contentX + controlsWidth - 18, scrollbarY, 10, scrollbarHeight);
					Raylib.DrawRectangleRounded(scrollbar, 0.5f, 4, new Color(200, 200, 200, 180));
					Raylib.BeginScissorMode((int)contentX, (int)contentY, controlsWidth - 20, (int)scrollableHeight);
				}
				else
				{
					Raylib.BeginScissorMode((int)contentX, (int)contentY, controlsWidth, (int)scrollableHeight);
				}

				int lineY = contentY - (int)_controlsScrollOffset;

				foreach (var action in rebindableActions)
				{
					Rectangle rowRect = new Rectangle(contentX + 4, lineY + 2, controlsWidth - 30, rowH - 5);
					bool rowHovered = Raylib.CheckCollisionPointRec(mousePos, rowRect);
					Raylib.DrawRectangleRounded(rowRect, 0.12f, 6, rowHovered ? new Color(35, 34, 42, 210) : new Color(22, 22, 30, 165));
					Raylib.DrawRectangleRoundedLines(rowRect, 0.12f, 6, 1, rowHovered ? new Color(125, 105, 70, 170) : new Color(65, 62, 58, 130));

					FontManager.DrawText(KeyBindings.DisplayName(action), contentX + 16, lineY + 30, 26, new Color(225, 215, 195, 235));
					var bindings = KeyBindings.GetBindings(action);
					int bindingsWidth = bindings.Count * (keyboardKeySize + keyGap) - keyGap;
					int bindingStartX = contentX + controlsWidth - (bindingsWidth + addButtonWidth + 18);
					int bindingX = bindingStartX;
					for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
					{
						var binding = bindings[bindingIndex];
						bool isKeyboardBinding = binding.Type == InputType.Keyboard;
						int currentKeyWidth = keyboardKeySize;
						int currentKeyHeight = keyboardKeySize;
						Rectangle keyRect = new Rectangle(bindingX, lineY + (rowH - currentKeyHeight) / 2, currentKeyWidth, currentKeyHeight);
						bool isRebinding = _rebindingAction?.action == action && _rebindingAction?.index == bindingIndex;
						bool keyHovered = Raylib.CheckCollisionPointRec(mousePos, keyRect);
						Texture2D buttonTex = binding.Type == InputType.Keyboard ? World.GetInputButtonTexture() : binding.Type == InputType.GamepadButton ? GetGamepadButtonTexture((GamepadButton)binding.Code) : binding.Type == InputType.GamepadAxis ? GetGamepadJoystickTexture((GamepadAxisDirection)binding.Code) : binding.Type == InputType.Mouse ? (MouseButton)binding.Code switch
						{
							MouseButton.Left => _lmouseButtonTex,
							MouseButton.Right => _rmouseButtonTex,
							MouseButton.Middle => _wmouseButtonTex,
							_ => new Texture2D()
						} : new Texture2D();

						Color keyColor = isRebinding ? new Color(255, 225, 150, 255) : keyHovered ? new Color(255, 235, 190, 255) : Color.White;
						if (buttonTex.Id != 0 && buttonTex.Width > 0 && buttonTex.Height > 0)
						{
							Raylib.DrawTexturePro(buttonTex, new Rectangle(0, 0, buttonTex.Width, buttonTex.Height),
								keyRect, Vector2.Zero, 0, keyColor);
						}
						string label = isKeyboardBinding ? ((KeyboardKey)binding.Code switch
						{
							KeyboardKey.LeftShift or KeyboardKey.RightShift => "MAJ",
							KeyboardKey.LeftControl or KeyboardKey.RightControl => "CTRL",
							KeyboardKey.Space => "ESPACE",
							KeyboardKey.Escape => "ECHAP",
							KeyboardKey.Enter => "ENTREE",
							KeyboardKey.Tab => "TAB",
							_ => ((KeyboardKey)binding.Code).ToString().ToUpperInvariant()
						}) : binding.Type == InputType.GamepadAxis ? KeyBindings.BindingDisplayName(binding) : string.Empty;
						if (isKeyboardBinding)
						{
							int labelFontSize = label.Length > 5 ? 15 : 22;
							int labelWidth = FontManager.MeasureText(label, labelFontSize);
							int labelY = (int)(keyRect.Y + (keyRect.Height - labelFontSize) / 2f);
							FontManager.DrawText(label, (int)(keyRect.X + (keyRect.Width - labelWidth) / 2), labelY, labelFontSize, Color.White);
						}

						if (bindingIndex > 0)
						{
							Rectangle removeRect = new Rectangle(keyRect.X + keyRect.Width - 7, keyRect.Y - 7, 16, 16);
							bool removeHovered = Raylib.CheckCollisionPointRec(mousePos, removeRect);
							Raylib.DrawCircle((int)(removeRect.X + 8), (int)(removeRect.Y + 8), 8, removeHovered ? new Color(235, 95, 75, 255) : new Color(150, 65, 55, 245));
							FontManager.DrawText("x", (int)removeRect.X + 4, (int)removeRect.Y + 1, 11, Color.White);
							if (removeHovered && Raylib.IsMouseButtonPressed(MouseButton.Left) && _rebindingAction == null)
								KeyBindings.RemoveBinding(action, bindingIndex);
						}

						if (keyHovered && Raylib.IsMouseButtonPressed(MouseButton.Left) && _rebindingAction == null)
							_rebindingAction = (action, bindingIndex);

						bindingX += currentKeyWidth + keyGap;
					}

					Rectangle addRect = new Rectangle(bindingX, lineY + 21, addButtonWidth, 46);
					bool addHovered = Raylib.CheckCollisionPointRec(mousePos, addRect);
					Raylib.DrawRectangleRounded(addRect, 0.18f, 6, addHovered ? new Color(80, 120, 70, 245) : new Color(42, 72, 48, 230));
					Raylib.DrawRectangleRoundedLines(addRect, 0.18f, 6, 1, new Color(135, 170, 115, 190));
					FontManager.DrawText("+", (int)addRect.X + 13, (int)addRect.Y + 9, 24, new Color(215, 245, 195, 255));
					if (addHovered && Raylib.IsMouseButtonPressed(MouseButton.Left) && _rebindingAction == null)
						_rebindingAction = (action, KeyBindings.AddBinding(action));

					lineY += rowH;
				}

				Raylib.EndScissorMode();

				// ── Message d'indication lors du rebind ──
				if (_rebindingAction != null)
				{
					string hint = "Appuyez sur une touche, un bouton ou une direction de manette (Echap pour annuler)";
					int hintW = FontManager.MeasureText(hint, 22);
					FontManager.DrawText(hint, contentX + controlsWidth / 2 - hintW / 2, contentY + scrollableHeight + 6, 22, new Color(255, 220, 120, 220));
				}

				// ── Bouton Réinitialiser ──
				int resetY = contentY + scrollableHeight + 38;
				Rectangle resetRect = new Rectangle(contentX + 10, resetY, 250, 44);
				bool resetHovered = Raylib.CheckCollisionPointRec(mousePos, resetRect);
				Raylib.DrawRectangleRounded(resetRect, 0.3f, 6, resetHovered ? new Color(70, 45, 40, 220) : new Color(35, 33, 40, 200));
				Raylib.DrawRectangleRoundedLines(resetRect, 0.3f, 6, 1, new Color(100, 90, 70, 160));
				string resetLabel = "Réinitialiser les touches";
				int rlw = FontManager.MeasureText(resetLabel, 18);
				FontManager.DrawText(resetLabel, (int)(resetRect.X + resetRect.Width / 2 - rlw / 2), (int)(resetRect.Y + resetRect.Height / 2 - 10), 18, new Color(220, 200, 190, 220));
				if (resetHovered && Raylib.IsMouseButtonPressed(MouseButton.Left) && _rebindingAction == null)
					KeyBindings.ResetToDefaults();

				// ── Informations sur les touches non réassignables ──
				int infoY = resetY + 50;
				var fixedControls = new (string action, string touche)[]
				{
					("Hotbar (1-0)", "1 - 0"),
					("Molette (zoom)", "Molette + MAJ"),
					("Menu principal (F5)", "F5"),
					("Debug (F3)", "F3"),
				};

				foreach (var (action, touche) in fixedControls)
				{
					FontManager.DrawText(action, contentX + 10, infoY, 17, new Color(190, 185, 170, 190));
					int kw = FontManager.MeasureText(touche, 17);
					FontManager.DrawText(touche, contentX + contentWidth - 20 - kw, infoY, 17, new Color(190, 165, 110, 200));
					infoY += 32;
				}
			}
			else if (_optionsTab == 2)
			{
				int y = contentY;
				FontManager.DrawText("Affichage de l'équipement :", contentX, y, 24, new Color(220, 210, 190, 230));
				y += 44;

				var equipmentStyles = new[]
				{
					("Personnage", "character"),
					("Personnage + cases", "character_with_slots"),
					("Équipements seuls", "equipment_only")
				};
				int equipmentStyleGap = 16;
				int equipmentStyleWidth = (contentWidth - equipmentStyleGap * 2) / equipmentStyles.Length;
				for (int styleIndex = 0; styleIndex < equipmentStyles.Length; styleIndex++)
				{
					var style = equipmentStyles[styleIndex];
					Rectangle styleRect = new Rectangle(contentX + styleIndex * (equipmentStyleWidth + equipmentStyleGap), y, equipmentStyleWidth, 46);
					bool hovered = Raylib.CheckCollisionPointRec(mousePos, styleRect);
					bool selected = SettingsManager.Settings.InventoryEquipmentStyle == style.Item2;
					Color styleColor = selected ? new Color(100, 80, 45, 240) : new Color(35, 35, 42, 220);
					if (hovered) styleColor = new Color((byte)Math.Min(255, styleColor.R + 18), (byte)Math.Min(255, styleColor.G + 18), (byte)Math.Min(255, styleColor.B + 18), styleColor.A);
					Raylib.DrawRectangleRounded(styleRect, 0.16f, 6, styleColor);
					Raylib.DrawRectangleRoundedLines(styleRect, 0.16f, 6, 1, selected ? new Color(235, 195, 105, 240) : new Color(120, 110, 90, 180));
					int styleWidth = FontManager.MeasureText(style.Item1, 20);
					FontManager.DrawText(style.Item1, (int)(styleRect.X + (styleRect.Width - styleWidth) / 2), (int)(styleRect.Y + 12), 20, Color.White);
					if (hovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						SettingsManager.Settings.InventoryEquipmentStyle = style.Item2;
						SettingsManager.Save();
					}
				}
				y += 66;

				FontManager.DrawText("Style de la hotbar :", contentX, y, 24, new Color(220, 210, 190, 230));
				y += 44;

				var hotbarStyles = new[] { ("Circulaire", "radial"), ("Classique", "classic") };
				for (int styleIndex = 0; styleIndex < hotbarStyles.Length; styleIndex++)
				{
					var style = hotbarStyles[styleIndex];
					Rectangle styleRect = new Rectangle(contentX + styleIndex * 220, y, 200, 46);
					bool hovered = Raylib.CheckCollisionPointRec(mousePos, styleRect);
					bool selected = SettingsManager.Settings.HotbarStyle == style.Item2;
					Color styleColor = selected ? new Color(100, 80, 45, 240) : new Color(35, 35, 42, 220);
					if (hovered) styleColor = new Color((byte)Math.Min(255, styleColor.R + 18), (byte)Math.Min(255, styleColor.G + 18), (byte)Math.Min(255, styleColor.B + 18), styleColor.A);
					Raylib.DrawRectangleRounded(styleRect, 0.16f, 6, styleColor);
					Raylib.DrawRectangleRoundedLines(styleRect, 0.16f, 6, 1, selected ? new Color(235, 195, 105, 240) : new Color(120, 110, 90, 180));
					int styleWidth = FontManager.MeasureText(style.Item1, 20);
					FontManager.DrawText(style.Item1, (int)(styleRect.X + (styleRect.Width - styleWidth) / 2), (int)(styleRect.Y + 12), 20, Color.White);
					if (hovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						SettingsManager.Settings.HotbarStyle = style.Item2;
						SettingsManager.Save();
					}
				}
				y += 66;
				FontManager.DrawText("Afficher les boutons d'interface rapide :", contentX, y, 24, new Color(220, 210, 190, 230));
				y += 52;

				var uiOptions = new (string label, bool current, Action<bool> setter)[]
				{
					("Inventaire", SettingsManager.Settings.ShowInventoryButton, v => SettingsManager.Settings.ShowInventoryButton = v),
					("Artisanat", SettingsManager.Settings.ShowCraftButton, v => SettingsManager.Settings.ShowCraftButton = v),
					("Guilde", SettingsManager.Settings.ShowGuildButton, v => SettingsManager.Settings.ShowGuildButton = v),
					("Journal de quêtes", SettingsManager.Settings.ShowQuestButton, v => SettingsManager.Settings.ShowQuestButton = v),
					("Carte", SettingsManager.Settings.ShowMapButton, v => SettingsManager.Settings.ShowMapButton = v),
					("Bestiaire", SettingsManager.Settings.ShowBestiaryButton, v => SettingsManager.Settings.ShowBestiaryButton = v),
					("Items (godmode)", SettingsManager.Settings.ShowItemsButton, v => SettingsManager.Settings.ShowItemsButton = v),
				};

				foreach (var opt in uiOptions)
				{
					bool isChecked = opt.current;
					Rectangle toggleRect = new Rectangle(contentX + 20, y, 34, 34);
					bool hovered = Raylib.CheckCollisionPointRec(mousePos, toggleRect);
					// Dessiner la case à cocher
					Raylib.DrawRectangleRounded(toggleRect, 0.15f, 6, hovered ? new Color(60, 50, 40, 220) : new Color(30, 30, 40, 200));
					Raylib.DrawRectangleRoundedLines(toggleRect, 0.15f, 6, 1, new Color(120, 110, 90, 200));
					string checkTextureName = isChecked ? "check_yes" : "check_no";
					string[] candidatePaths = { $"assets/gui/{checkTextureName}.png", $"assets/gui/{checkTextureName}.jpg" };
					Texture2D checkTexture = new Texture2D();
					foreach (var path in candidatePaths)
					{
						if (File.Exists(path))
						{
							checkTexture = Raylib.LoadTexture(path);
							Raylib.SetTextureFilter(checkTexture, TextureFilter.Point);
							break;
						}
					}
					if (checkTexture.Id != 0)
					{
						float texW = checkTexture.Width;
						float texH = checkTexture.Height;
						float scale = Math.Min(toggleRect.Width / texW, toggleRect.Height / texH);
						float drawW = texW * scale;
						float drawH = texH * scale;
						float drawX = toggleRect.X + (toggleRect.Width - drawW) / 2f;
						float drawY = toggleRect.Y + (toggleRect.Height - drawH) / 2f;
						Raylib.DrawTexturePro(checkTexture, new Rectangle(0, 0, texW, texH), new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
					}
					FontManager.DrawText(opt.label, (int)toggleRect.X + 48, y + 6, 20, Color.White);
					if (hovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						opt.setter(!isChecked);
						SettingsManager.Save();
					}
					y += 44;
				}
			}

			// ── ACTIONS DU MENU ──
			int backY = py + panelH - 80;
			int actionGap = 16;
			int actionWidth = 260;
			int actionsWidth = actionWidth * 2 + actionGap;
			Rectangle backRect = new Rectangle(px + panelW / 2 - actionsWidth / 2, backY, actionWidth, 60);
			Rectangle acceptRect = new Rectangle(backRect.X + actionWidth + actionGap, backY, actionWidth, 60);
			bool backHovered = Raylib.CheckCollisionPointRec(mousePos, backRect);
			bool acceptHovered = Raylib.CheckCollisionPointRec(mousePos, acceptRect);
			UIManager.DrawButton(backRect, new Color(185, 75, 65, 255), backHovered, true);
			UIManager.DrawButton(acceptRect, new Color(75, 165, 90, 255), acceptHovered, true);
			string backText = Localization.Get("options.back");
			string acceptText = "VALIDER";
			int btw = FontManager.MeasureText(backText, 25);
			int atw = FontManager.MeasureText(acceptText, 25);
			FontManager.DrawText(backText, (int)(backRect.X + backRect.Width / 2 - btw / 2), (int)(backRect.Y + backRect.Height / 2 - 13), 25, Color.White);
			FontManager.DrawText(acceptText, (int)(acceptRect.X + acceptRect.Width / 2 - atw / 2), (int)(acceptRect.Y + acceptRect.Height / 2 - 13), 25, Color.White);
			if (backHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				RequestOptionsBack();
			}
			if (acceptHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				AcceptOptionsChanges();
			}

			if (_showOptionsDiscardConfirmation)
			{
				Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
				int dialogW = Math.Min(560, sw - 40);
				int dialogH = 190;
				Rectangle dialogRect = new Rectangle(sw / 2 - dialogW / 2, sh / 2 - dialogH / 2, dialogW, dialogH);
				Raylib.DrawRectangleRounded(dialogRect, 0.08f, 8, new Color(24, 22, 28, 250));
				Raylib.DrawRectangleRoundedLines(dialogRect, 0.08f, 8, 2, new Color(190, 75, 65, 240));
				string warning = "Des modifications n'ont pas ete validees.";
				string question = "Quitter sans les enregistrer ?";
				int warningW = FontManager.MeasureText(warning, 18);
				int questionW = FontManager.MeasureText(question, 16);
				FontManager.DrawText(warning, sw / 2 - warningW / 2, (int)dialogRect.Y + 28, 18, new Color(255, 220, 150, 255));
				FontManager.DrawText(question, sw / 2 - questionW / 2, (int)dialogRect.Y + 58, 16, Color.White);
				Rectangle cancelRect = new Rectangle(dialogRect.X + 30, dialogRect.Y + 112, dialogW / 2 - 45, 42);
				Rectangle discardRect = new Rectangle(dialogRect.X + dialogW / 2 + 15, dialogRect.Y + 112, dialogW / 2 - 45, 42);
				bool cancelHovered = Raylib.CheckCollisionPointRec(mousePos, cancelRect);
				bool discardHovered = Raylib.CheckCollisionPointRec(mousePos, discardRect);
				UIManager.DrawButton(cancelRect, new Color(75, 165, 90, 255), cancelHovered, true);
				UIManager.DrawButton(discardRect, new Color(185, 75, 65, 255), discardHovered, true);
				string cancelText = "ANNULER";
				string discardText = "QUITTER";
				int cancelW = FontManager.MeasureText(cancelText, 16);
				int discardW = FontManager.MeasureText(discardText, 16);
				FontManager.DrawText(cancelText, (int)(cancelRect.X + cancelRect.Width / 2 - cancelW / 2), (int)cancelRect.Y + 12, 16, Color.White);
				FontManager.DrawText(discardText, (int)(discardRect.X + discardRect.Width / 2 - discardW / 2), (int)discardRect.Y + 12, 16, Color.White);
				if (cancelHovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) _showOptionsDiscardConfirmation = false;
				if (discardHovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) DiscardOptionsChanges();
			}
		}

		private static void DrawUIBar()
		{
			if (_gameState != GameState.Playing) return;

			var settings = SettingsManager.Settings;
			var uiItems = new List<(string id, bool enabled, Func<bool> isOpen, Action toggle)>
			{
				("inventory", settings.ShowInventoryButton, () => InventoryRenderer.IsInventoryOpen, () =>
				{
					if (InventoryRenderer.IsInventoryOpen) UIManager.PopUI();
					else InventoryRenderer.Open(0);
				}),
				("craft", settings.ShowCraftButton, () => CraftingUI.IsOpen, () =>
				{
					if (CraftingUI.IsOpen) UIManager.PopUI();
					else CraftingUI.Open("", 0);
				}),
				("guild", settings.ShowGuildButton, () => GuildUI.IsOpen, () =>
				{
					if (GuildUI.IsOpen)
						GuildUI.Close();
					else if (Program.PlayerGuild != null)
						GuildUI.Open();
					else
						AddNotification(new Notification("Vous devez créer une guilde avec la charte avant d'accéder au menu.", Color.Red, 2f));
				}),
				("quest", settings.ShowQuestButton, () => QuestJournalUI.IsOpen, () => QuestJournalUI.Toggle()),
				("map", settings.ShowMapButton, () => _gameState == GameState.Map, () =>
				{
					if (_gameState == GameState.Map) _gameState = GameState.Playing;
					else _gameState = GameState.Map;
				}),
				("bestiary", settings.ShowBestiaryButton, () => BestiaryUI.IsOpen, () =>
				{
					if (BestiaryUI.IsOpen)
						UIManager.PopUI();
					else
						BestiaryUI.Open();
				}),
			};

			// Ajout du bouton Items (Godmode uniquement)
			if (Program.IsGodMode && settings.ShowItemsButton)
    		{
				uiItems.Add(("items", true, () => ItemMenuUI.IsOpen, () =>
				{
					if (ItemMenuUI.IsOpen)
						UIManager.PopUI();
					else
						ItemMenuUI.Open();
				}));
			}

			var activeButtons = uiItems.Where(x => x.enabled).ToList();
			if (activeButtons.Count == 0) return;

			int buttonSize = 40;
			int spacing = 8;
			int totalWidth = activeButtons.Count * (buttonSize + spacing) - spacing;
			int startX = 20;
			int startY = ScreenHeight - buttonSize - 20;

			// ==== CONNEXIONS (en arrière‑plan) ====
			if (_uiBarConnectionTex.Id != 0 && activeButtons.Count > 1)
			{
				int inset = buttonSize / 8;
				for (int i = 0; i < activeButtons.Count - 1; i++)
				{
					int x1 = startX + i * (buttonSize + spacing) + buttonSize - inset;
					int x2 = startX + (i + 1) * (buttonSize + spacing) + inset;
					int connWidth = x2 - x1;
					if (connWidth > 0)
					{
						float connHeight = buttonSize * 0.6f;
						float connY = startY + (buttonSize - connHeight) / 2;
						Rectangle srcRect = new Rectangle(0, 0, _uiBarConnectionTex.Width, _uiBarConnectionTex.Height);
						Rectangle destRect = new Rectangle(x1, connY, connWidth, connHeight);
						Raylib.DrawTexturePro(_uiBarConnectionTex, srcRect, destRect, Vector2.Zero, 0, Color.White);
					}
				}
			}

			// ==== BOUTONS ====
			for (int i = 0; i < activeButtons.Count; i++)
			{
				var (id, _, isOpen, toggle) = activeButtons[i];
				int x = startX + i * (buttonSize + spacing);
				int y = startY;

				Rectangle destRect = new Rectangle(x, y, buttonSize, buttonSize);

				// 1. Fond (uniquement si la texture de slot est absente ou si on veut un fallback)
				Texture2D slotTex = isOpen() ? _uiBarSlotOpenTex : _uiBarSlotTex;
				if (slotTex.Id == 0)
				{
					// Fallback : rectangle coloré en fond
					Raylib.DrawRectangle(x, y, buttonSize, buttonSize, isOpen() ? new Color(60, 80, 60, 200) : new Color(40, 40, 50, 200));
				}

				// 2. Icône (dessinée en premier, donc derrière le slot)
				Texture2D iconTex = GetUIBarButtonTexture(id);
				if (iconTex.Id != 0)
				{
					float scaleX = destRect.Width / iconTex.Width;
					float scaleY = destRect.Height / iconTex.Height;
					float scale = Math.Max(scaleX, scaleY);
					float drawW = iconTex.Width * scale;
					float drawH = iconTex.Height * scale;
					float drawX = destRect.X + (destRect.Width - drawW) / 2;
					float drawY = destRect.Y + (destRect.Height - drawH) / 2;
					Rectangle src = new Rectangle(0, 0, iconTex.Width, iconTex.Height);
					Rectangle dest = new Rectangle(drawX, drawY, drawW, drawH);
					Raylib.DrawTexturePro(iconTex, src, dest, Vector2.Zero, 0, Color.White);
				}

				// 3. Slot (par‑dessus l’icône)
				if (slotTex.Id != 0)
				{
					float scaleX = destRect.Width / slotTex.Width;
					float scaleY = destRect.Height / slotTex.Height;
					float scale = Math.Max(scaleX, scaleY);
					float drawW = slotTex.Width * scale;
					float drawH = slotTex.Height * scale;
					float drawX = destRect.X + (destRect.Width - drawW) / 2;
					float drawY = destRect.Y + (destRect.Height - drawH) / 2;
					Rectangle src = new Rectangle(0, 0, slotTex.Width, slotTex.Height);
					Rectangle dest = new Rectangle(drawX, drawY, drawW, drawH);
					Raylib.DrawTexturePro(slotTex, src, dest, Vector2.Zero, 0, Color.White);
				}
				else
				{
					// Si pas de slot, on dessine une bordure pour délimiter le bouton
					Raylib.DrawRectangleLines(x, y, buttonSize, buttonSize, isOpen() ? Color.Green : Color.Gray);
				}

				// Détection de clic
				Rectangle btnRect = new Rectangle(x, y, buttonSize, buttonSize);
				Vector2 mousePos = Raylib.GetMousePosition();
				if (Raylib.CheckCollisionPointRec(mousePos, btnRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					toggle();
				}
			}
		}

		private static void LoadCharacterTextures(List<string> missingTextures, LoadStats stats)
		{
			// ========== CHEVEUX ==========
			
			string hairsDir = "assets/animals/Human/hairs/";
			hairBaseTextures.Clear();
			hairBackTextures.Clear();
			hairOverlayTextures.Clear();
			NoneStyleIcon = File.Exists("assets/gui/icon_none.png")
				? Raylib.LoadTexture("assets/gui/icon_none.png")
				: new Texture2D();

			hairBaseTextures.Add(new Texture2D());
			hairBackTextures.Add(new Texture2D());
			hairOverlayTextures.Add(new Texture2D());
			
			int loaded = 0, missing = 0;
			
			if (Directory.Exists(hairsDir))
			{
				var hairFiles = Directory.GetFiles(hairsDir, "Humanhair*.png")
					.Where(f => !f.Contains("R") && !Path.GetFileNameWithoutExtension(f).EndsWith("_back", StringComparison.OrdinalIgnoreCase))
					.Select(Path.GetFileNameWithoutExtension)
					.Distinct()
					.OrderBy(f => 
					{
						string numStr = f.Replace("Humanhair", "");
						if (int.TryParse(numStr, out int num))
							return num;
						return int.MaxValue;
					})
					.ToList();
				
				foreach (var baseName in hairFiles)
				{
					string path = $"{hairsDir}{baseName}.png";
					var tex = TryLoad(path);
					hairBaseTextures.Add(tex);
					string backPath = $"{hairsDir}{baseName}_back.png";
					hairBackTextures.Add(File.Exists(backPath) ? Raylib.LoadTexture(backPath) : new Texture2D());
					hairOverlayTextures.Add(new Texture2D());
					
					if (tex.Id == 0)
					{
						missing++;
						missingTextures.Add(path);
					}
					else
						loaded++;
				}
			}
			else
			{
				for (int i = 1; i <= 12; i++)
				{
					var tex = TryLoad($"{hairsDir}Humanhair{i}.png");
					hairBaseTextures.Add(tex);
					string backPath = $"{hairsDir}Humanhair{i}_back.png";
					hairBackTextures.Add(File.Exists(backPath) ? Raylib.LoadTexture(backPath) : new Texture2D());
					hairOverlayTextures.Add(new Texture2D());
					if (tex.Id != 0) loaded++;
					else missing++;
				}
			}
			
			if (hairBaseTextures.Count == 0)
			{
				hairBaseTextures.Add(missingTexture);
				hairBackTextures.Add(new Texture2D());
				hairOverlayTextures.Add(new Texture2D());
			}
			
			// ========== BARBES ==========
			beardBaseTextures.Clear();
			beardOverlayTextures.Clear();
			beardBaseTextures.Add(new Texture2D());
			beardOverlayTextures.Add(new Texture2D());
			loaded = 0;
			missing = 0;
			
			if (Directory.Exists(hairsDir))
			{
				var beardFiles = Directory.GetFiles(hairsDir, "Humanbeard*.png")
					.Where(f => !f.Contains("R"))
					.Select(Path.GetFileNameWithoutExtension)
					.Distinct()
					.OrderBy(f =>
					{
						string numStr = f.Replace("Humanbeard", "");
						if (int.TryParse(numStr, out int num))
							return num;
						return int.MaxValue;
					})
					.ToList();
				
				foreach (var baseName in beardFiles)
				{
					string path = $"{hairsDir}{baseName}.png";
					var tex = TryLoad(path);
					beardBaseTextures.Add(tex);
					beardOverlayTextures.Add(new Texture2D());
					
					if (tex.Id == 0)
					{
						missing++;
						missingTextures.Add(path);
					}
					else
						loaded++;
				}
			}
			
			if (beardBaseTextures.Count == 0)
			{
				beardBaseTextures.Add(new Texture2D());
				beardOverlayTextures.Add(new Texture2D());
			}
			
			Console.WriteLine();
			stats.Success += loaded;
			stats.Warnings += missing;
			
			// ========== YEUX ==========
			
			EyeWhiteTextures.Clear();
			PupilLeftTextures.Clear();
			PupilRightTextures.Clear();
			EyebrowLeftTextures.Clear();
			EyebrowRightTextures.Clear();
			EyeClosedTextures.Clear();
			EyeBaseTextures.Clear();
			EyeOverlayTextures.Clear();
			
			string eyesDir = "assets/animals/Human/";
			loaded = 0;
			missing = 0;
			
			if (Directory.Exists(eyesDir))
			{
				var eyeFiles = Directory.GetFiles(eyesDir, "Humaneyes*.png")
					.Select(Path.GetFileNameWithoutExtension)
					.Distinct()
					.OrderBy(f =>
					{
						string numStr = f.Replace("Humaneyes", "");
						if (int.TryParse(numStr, out int num))
							return num;
						return int.MaxValue;
					})
					.ToList();
				
				foreach (var baseName in eyeFiles)
				{
					string styleSuffix = baseName.Replace("Humaneyes", "");

					string whitePath = $"{eyesDir}Humaneyes{styleSuffix}.png";
					string lpupilPath = $"{eyesDir}Humanlpupil{styleSuffix}.png";
					string rpupilPath = $"{eyesDir}Humanrpupil{styleSuffix}.png";
					string leyebrowPath = $"{eyesDir}Humanleyebrow{styleSuffix}.png";
					string reyebrowPath = $"{eyesDir}Humanreyebrow{styleSuffix}.png";
					string closedPath = $"{eyesDir}Humaneyes{styleSuffix}closed.png";

					var whiteTex = TryLoad(whitePath);
					var lpupilTex = TryLoad(lpupilPath);
					var rpupilTex = TryLoad(rpupilPath);
					var leyebrowTex = TryLoad(leyebrowPath);
					var reyebrowTex = TryLoad(reyebrowPath);
					var closedTex = TryLoad(closedPath);

					EyeWhiteTextures.Add(whiteTex);
					PupilLeftTextures.Add(lpupilTex);
					PupilRightTextures.Add(rpupilTex);
					EyebrowLeftTextures.Add(leyebrowTex);
					EyebrowRightTextures.Add(reyebrowTex);
					EyeClosedTextures.Add(closedTex);

					bool allOk = whiteTex.Id != 0 && lpupilTex.Id != 0 && rpupilTex.Id != 0 && leyebrowTex.Id != 0 && reyebrowTex.Id != 0;
					if (allOk)
						loaded++;
					else
					{
						missing++;
						if (whiteTex.Id == 0) missingTextures.Add(whitePath);
						if (lpupilTex.Id == 0) missingTextures.Add(lpupilPath);
						if (rpupilTex.Id == 0) missingTextures.Add(rpupilPath);
						if (leyebrowTex.Id == 0) missingTextures.Add(leyebrowPath);
						if (reyebrowTex.Id == 0) missingTextures.Add(reyebrowPath);
					}
				}
			}
			
			if (EyeWhiteTextures.Count == 0)
			{
				EyeWhiteTextures.Add(missingTexture);
				PupilLeftTextures.Add(missingTexture);
				PupilRightTextures.Add(missingTexture);
				EyebrowLeftTextures.Add(missingTexture);
				EyebrowRightTextures.Add(missingTexture);
			}
			stats.Success += loaded;
			stats.Warnings += missing;
			
			// ========== BOUCHE ==========
			MouthTexture = TryLoad("assets/animals/Human/Humanmouth.png");
			
			if (MouthTexture.Id == 0)
			{ 
				missingTextures.Add("assets/animals/Human/Humanmouth.png"); 
				stats.Warnings++; 
			}

			MouthOpenTexture = TryLoad("assets/animals/Human/Humanmouth_open.png");
			if (MouthOpenTexture.Id == 0)
			{
				missingTextures.Add("assets/animals/Human/Humanmouth_open.png");
				stats.Warnings++;
			}
		}

		private static bool TryDrinkTaggedPotion(Item item)
		{
			string metadata = item.Metadata?.Trim() ?? string.Empty;
			metadata = ItemRenderer.NormalizePotionMetadata(metadata);
			if (!ItemRenderer.IsPotionMetadata(metadata))
				return false;

			var bottleData = new ItemData { TextureName = "glassbottle_small" };
			if (!ItemRenderer.TryGetBottleContentInfo(item, bottleData, out string contentType, out int amount))
				return false;

			int duration;
			switch (contentType.ToLowerInvariant())
			{
				case "speed":
					ConsumeOnePotionAndCreateEmptyBottle(item);
					_speedPotionTimer = _speedPotionTotalDuration = amount > 0 ? amount : 30;
					AddNotification(new Notification($" Vous buvez une potion de vitesse ! Vitesse augmentée pendant {_speedPotionTimer} secondes.", new Color(100, 160, 255, 255), 2f));
					return true;
				case "strength":
				case "power":
				case "pouvoir":
					ConsumeOnePotionAndCreateEmptyBottle(item);
					_strengthPotionTimer = _strengthPotionTimerMax = amount > 0 ? amount : 60;
					return true;
				case "resistance":
					ConsumeOnePotionAndCreateEmptyBottle(item);
					_resistancePotionTimer = _resistancePotionTimerMax = amount > 0 ? amount : 60;
					return true;
				case "light":
					ConsumeOnePotionAndCreateEmptyBottle(item);
					_lightPotionTimer = _lightPotionTimerMax = amount > 0 ? amount : 120;
					return true;
				case "heatresist":
					duration = amount > 0 ? amount : 90;
					ConsumeOnePotionAndCreateEmptyBottle(item);
					_heatResistPotionTimer = _heatResistPotionTimerMax = duration;
					return true;
				case "coldresist":
					duration = amount > 0 ? amount : 90;
					ConsumeOnePotionAndCreateEmptyBottle(item);
					_coldResistPotionTimer = _coldResistPotionTimerMax = duration;
					return true;
				case "invisibility":
					ConsumeOnePotionAndCreateEmptyBottle(item);
					_invisibilityPotionTimer = _invisibilityPotionTimerMax = amount > 0 ? amount : 20;
					return true;
				case "heal":
					if (playerHP >= GetEffectivePlayerMaxHP())
					{
						AddNotification(new Notification(" Vous êtes déjà en pleine forme.", new Color(255, 200, 100, 255), 1.5f));
						return true;
					}
					playerHP = Math.Min(GetEffectivePlayerMaxHP(), playerHP + (amount > 0 ? amount : 10));
					ConsumeOnePotionAndCreateEmptyBottle(item);
					return true;
			}

			return false;
		}

		private static bool TryLoadSteamEngineFuelFromContainer(int tileX, int tileY, out float water, out float coal)
		{
			water = 0f;
			coal = 0f;
			var container = ContainerUI.GetCurrentContainer();
			if (container == null || !World.SteamEngines.TryGetValue((tileX, tileY), out var engine)) return false;

			float coalCapacity = Math.Max(0f, SteamEngineData.MaxCoal - engine.Coal);
			float waterCapacity = Math.Max(0f, SteamEngineData.MaxWater - engine.Water);
			int coalId = GetItemId("coal");
			int waterBottleId = GetItemId("Petite bouteille");

			foreach (var slot in container.Slots)
			{
				if (slot.IsEmpty || slot.Item == null || slot.Count <= 0) continue;

				int itemId = GetItemId(slot.Item.Name);
				if (itemId == coalId && coalCapacity >= 1f)
				{
					int units = Math.Min(slot.Count, (int)MathF.Floor(coalCapacity));
					coal += units;
					coalCapacity -= units;
					slot.Count -= units;
					if (slot.Count <= 0) slot.Clear();
					continue;
				}

				if (waterCapacity <= 0f) continue;
				int waterAmount = 0;
				bool isWaterContainer = TryGetLiquidContainer(slot.Item, out string liquidType, out int charges, out _)
					&& liquidType == "water" && charges > 0;
				if (isWaterContainer)
					waterAmount = charges;
				else if (itemId == waterBottleId)
					waterAmount = 10;
				if (waterAmount <= 0) continue;
				if (!isWaterContainer && waterCapacity < waterAmount) continue;

				int transferred = Math.Min(waterAmount, (int)MathF.Floor(waterCapacity));
				if (transferred <= 0) continue;
				water += transferred;
				waterCapacity -= transferred;
				if (isWaterContainer)
					SetLiquidContainerCharges(slot.Item, "water", charges - transferred);
				else
					slot.Count--;
				if (slot.Count <= 0) slot.Clear();
			}

			if (water <= 0f && coal <= 0f) return false;
			ContainerUI.NotifyCurrentContainerChanged();
			return true;
		}

		private static bool TryLoadSteamEngineFuelFromHeldItem(int tileX, int tileY, Item heldItem, out float water, out float coal)
		{
			water = 0f;
			coal = 0f;
			if (!World.SteamEngines.TryGetValue((tileX, tileY), out var engine)) return false;

			int heldId = GetItemId(heldItem.Name);
			if (heldId == GetItemId("coal"))
			{
				if (SteamEngineData.MaxCoal - engine.Coal < 1f) return false;
				coal = 1f;
				RemoveItemFromInventory(heldItem.Name, 1);
				return true;
			}

			bool isWaterContainer = TryGetLiquidContainer(heldItem, out string liquidType, out int charges, out _)
				&& liquidType == "water" && charges > 0;
			bool isSmallWaterBottle = heldId == GetItemId("Petite bouteille");
			if (!isWaterContainer && !isSmallWaterBottle) return false;

			int availableWater = isWaterContainer ? charges : 10;
			int acceptedWater = Math.Min(availableWater, (int)MathF.Floor(Math.Max(0f, SteamEngineData.MaxWater - engine.Water)));
			if (acceptedWater <= 0 || (!isWaterContainer && acceptedWater < availableWater)) return false;

			water = acceptedWater;
			if (isWaterContainer)
				SetLiquidContainerCharges(heldItem, "water", charges - acceptedWater);
			else
				RemoveItemFromInventory(heldItem.Name, 1);
			return true;
		}

		private static bool ConsumeOnePotionAndCreateEmptyBottle(Item consumedItem)
		{
			int remainingDoses = ItemRenderer.GetPotionDoses(consumedItem);
			if (remainingDoses > 1)
			{
				ItemRenderer.SetPotionDoses(consumedItem, remainingDoses - 1);
				return true;
			}

			InventorySlot? sourceSlot = GetAllInventorySlots()
				.FirstOrDefault(slot => !slot.IsEmpty && ReferenceEquals(slot.Item, consumedItem));
			if (sourceSlot == null)
			{
				EmptyBottleItem(consumedItem);
				return true;
			}

			sourceSlot.Count--;
			if (sourceSlot.Count <= 0)
				sourceSlot.Clear();

			var emptyContainer = new Item(consumedItem.Name, 1, consumedItem.BaseColor, consumedItem.Icon, consumedItem.CustomColor)
			{
				Metadata = string.Empty,
				Container = consumedItem.Container,
				Backpack = consumedItem.Backpack,
				Meta = new Dictionary<string, string>(consumedItem.Meta, StringComparer.OrdinalIgnoreCase),
				CustomColors = new List<Color?>(consumedItem.CustomColors)
			};
			int remaining = AddItemToInventory(emptyContainer, 1);
			if (remaining > 0)
			{
				DropCustomItemOnGroundWithColors(
					GetPlayerPosition(), GetItemId(emptyContainer.Name), remaining,
					emptyContainer.CustomColors, emptyContainer.Metadata, emptyContainer.Meta);
			}

			return true;
		}

		static void HandleInteract(Vector2 mousePos, Camera2D cam, float dt, Vector2 playerPos, float facing)
		{
			if (IsMouseOverAnyUI())
				return;

			if (_caveTransitionPhase != CaveTransitionPhase.None)
				return;

			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mousePos, cam);
			int ts = Program.TileSize;
			(int bestTileX, int bestTileY, float bestDist, int bestObjectId) = FindTileUnderMouse(mouseWorld, ts);

			// DÉCLARATION UNIQUE DE heldItem (avant toute utilisation)
			Item? heldItem = equipment.MainHand;

			if (bestObjectId == 401 && TryInteractWithStation(playerPos, cam))
				return;

			if (heldItem != null && TryDrinkTaggedPotion(heldItem))
				return;

			// ========== 0. PLANTATION DE GRAINES (PRIORITÉ ABSOLUE) ==========
			if (heldItem != null && bestTileX != -1 && bestTileY != -1)
			{
				int heldId = GetItemId(heldItem.Name);
				if (GameData.ItemDatabase.TryGetValue(heldId, out var heldData) && heldData.IsSeed)
				{
					int groundId = World.GetGroundTileIdAt(bestTileX, bestTileY);
					var groundTileForSeed = WorldTileRegistry.GetTile(groundId);
					bool canPlantHere;
					if (heldData.PlantType == "classic")
					{
						//  Arbre (ex : gland) : se plante n'importe où sur un sol praticable,
						// comme un objet plaçable — pas besoin de farmland.
						canPlantHere = groundTileForSeed != null && groundTileForSeed.Walkable
							&& World.GetObjectIdAt(bestTileX, bestTileY) == 0;
					}
					else if (heldData.PlantType == "grass")
					{
						canPlantHere = groundTileForSeed != null && groundTileForSeed.Material == "grass"
							&& World.GetObjectIdAt(bestTileX, bestTileY) == 0;
					}
					else
					{
						//  Accepter la terre sèche (27) ET humide (29)
						canPlantHere = (groundId == 27 || groundId == 29) && World.GetObjectIdAt(bestTileX, bestTileY) == 0;
					}

					if (canPlantHere)
					{
						// Vérifier la distance
						float plantDist = Vector2.Distance(playerPos, new Vector2(bestTileX * ts + ts/2f, bestTileY * ts + ts/2f));
						if (plantDist <= MAX_INTERACTION_DISTANCE)
						{
							int cropId = heldData.CropId;
							if (cropId != 0)
							{
								World.PlantCrop(bestTileX, bestTileY, cropId, (float)_gameTime);
								SoundEffects.PlayPlant();
								RemoveItemFromInventory(heldItem.Name, 1);
								AddNotification(new Notification($" {heldItem.Name} plantée !", new Color(100, 200, 100, 255), 1.5f));
								attackAnim = 0.2f;
								isAttacking = true;
								return; //  SORTIE : on a planté
							}
						}
					}
				}
			}

			// ========== 1. ATTAQUE DES ENNEMIS (pas de limite de distance) ==========
			if (attackCooldown > 0f)
			{
				return;
			}
			if (heldItem != null)
			{
				int heldId = GetItemId(heldItem.Name);
				if (heldId == 5000) // Filet à papillons : pas de zone d'attaque, capture directe
				{
					return;
				}
			}

			int playerTileX = (int)(playerPos.X / ts);
			int playerTileY = (int)(playerPos.Y / ts);
			int playerHeight = World.GetHeightAt(playerTileX, playerTileY);
			float playerYOffset = -playerHeight * ts / 4;
			Vector2 playerVisualPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset);
			Vector2 attackDir = mouseWorld - playerVisualPos;
			if (attackDir.Length() < 0.01f) attackDir = new Vector2(facing, 0);
			attackDir = Vector2.Normalize(attackDir);
			_swingDirection = attackDir;
			_swingAngle = MathF.Atan2(attackDir.Y, attackDir.X) * 180f / MathF.PI;
			_swingTimer = 0.25f;
			_attackSwingProgress = 0.01f;
			isAttacking = true;
			attackAnim = 0.35f;
			attackCooldown = 0.5f;

			// ========== PVP : joueurs adverses touchés par le coup (consentement mutuel) ==========
			var pvpTargets = GetPvpTargetsInAttackCone(playerPos, attackDir, ATTACK_RADIUS, ATTACK_ANGLE);
			if (pvpTargets.Count > 0)
			{
				PlayPunchSound();
				int pvpDamage = equipment.TotalAttack;
				foreach (var target in pvpTargets)
				{
					int targetConnId = target.Id - 1000; // voir NetworkManager.GetRemotePlayersAsLocalPlayers
					if (NetworkManager.IsHost)
					{
						DamagePlayerTarget(pvpDamage, targetConnId);
					}
					else
					{
						NetworkManager.SendPvpHitRequest(targetConnId, pvpDamage);
					}
					Vector2 targetVisualPos = new Vector2(target.Position.X,
						target.Position.Y + (World.GetHeightAt((int)(target.Position.X / ts), (int)(target.Position.Y / ts)) * -ts / 4));
					if (!float.IsNaN(targetVisualPos.X) && !float.IsNaN(targetVisualPos.Y) &&
						!float.IsInfinity(targetVisualPos.X) && !float.IsInfinity(targetVisualPos.Y))
					{
						AddFloatingDamage(targetVisualPos, pvpDamage, pvpDamage >= 8, isEnemy: true);
						SpawnSlashEffect(targetVisualPos, attackDir);
					}
				}
				return; // ← SORTIE : on ne touche pas aux blocs/entités si on a frappé un joueur
			}

			//  MULTIJOUEUR : côté client, Program.entities est toujours vide (le client ne simule
			// aucune entité, il ne fait qu'afficher les snapshots du host). On détecte donc le
			// coup sur les EntityDto reçus par le réseau, et on envoie une demande de dégâts au
			// host au lieu de modifier l'entité localement (le host reste seul autoritaire).
			if (NetworkManager.IsClient)
			{
				var hitDtos = GetRemoteEntitiesInAttackCone(playerPos, attackDir, ATTACK_RADIUS, ATTACK_ANGLE);
				if (hitDtos.Count > 0)
				{
					PlayPunchSound();
					int clientDamage = equipment.TotalAttack;
					foreach (var dto in hitDtos)
					{
						if (!Guid.TryParse(dto.NetId, out var dtoGuid)) continue;
						NetworkManager.RequestEntityHit(dtoGuid, clientDamage);
						Vector2 dtoVisualPos = new Vector2(dto.PosX, dto.PosY + (World.GetHeightAt((int)(dto.PosX / ts), (int)(dto.PosY / ts)) * -ts / 4));
						if (!float.IsNaN(dtoVisualPos.X) && !float.IsNaN(dtoVisualPos.Y) &&
							!float.IsInfinity(dtoVisualPos.X) && !float.IsInfinity(dtoVisualPos.Y))
						{
							AddFloatingDamage(dtoVisualPos, clientDamage, clientDamage >= 8, isEnemy: true);
							SpawnSlashEffect(dtoVisualPos, attackDir);
						}
					}
					return; // ← SORTIE : on ne touche pas aux blocs si on a frappé des ennemis
				}
				// Aucune entité touchée côté client : on continue vers les interactions sur les blocs.
			}
			else
			{
			var hitEntities = GetEntitiesInAttackCone(playerPos, attackDir, ATTACK_RADIUS, ATTACK_ANGLE);
			//  Une créature en l'air (oiseau en vol, boss volant non posé...) ne peut pas être
			// touchée par une simple attaque de corps à corps : elle est hors de portée tant
			// qu'elle n'a pas atterri (voir Entity.IsAirborne / IsInvulnerable).
			if (hitEntities.Count > 0)
				hitEntities.RemoveAll(e => e.IsInvulnerable);
			if (hitEntities.Count > 0)
			{
				PlayPunchSound(); //  Son de coup une seule fois par attaque qui touche, pas par entité
				int damage = equipment.TotalAttack;
				foreach (var entity in hitEntities)
				{
					entity.CurrentHP -= damage;
					entity.OnHit(playerPos);
					Vector2 knockbackDir = entity.WorldPos - playerPos;
					if (knockbackDir.Length() > 0.01f) knockbackDir = Vector2.Normalize(knockbackDir);
					else knockbackDir = new Vector2(1, 0);
					entity.ApplyKnockback(knockbackDir, ATTACK_KNOCKBACK);
					Vector2 entityVisualPos = new Vector2(entity.WorldPos.X, entity.WorldPos.Y + (World.GetHeightAt((int)(entity.WorldPos.X / ts), (int)(entity.WorldPos.Y / ts)) * -ts / 4));
					if (!float.IsNaN(entityVisualPos.X) && !float.IsNaN(entityVisualPos.Y) &&
						!float.IsInfinity(entityVisualPos.X) && !float.IsInfinity(entityVisualPos.Y))
					{
						AddFloatingDamage(entityVisualPos, damage, damage >= 8, isEnemy: true);
							SpawnSlashEffect(entityVisualPos, attackDir);
					}
					else
					{
						AddFloatingDamage(playerPos, damage, damage >= 8, isEnemy: true);
					}
					if (!entity.IsAlive)
					{
						SkillSystem.AddXP(SkillType.Chasseur, 5);
						entityVisualPos = new Vector2(entity.WorldPos.X, entity.WorldPos.Y + (World.GetHeightAt((int)(entity.WorldPos.X / ts), (int)(entity.WorldPos.Y / ts)) * -ts / 4));
						SpawnDeathParticles(entityVisualPos, entity.Tint);

						// Boss de l'oeil : chaque monstre de vague tué inflige des dégâts au boss.
						if (_eyeBossActive && entity.EyeBossWaveTag)
						{
							_eyeBossHP = Math.Max(0, _eyeBossHP - GetEyeBossMonsterDamage(entity.Species));
						}

						var speciesInfo = SpeciesData.GetSpeciesInfo(entity.Species);
						if (speciesInfo == null || !speciesInfo.CanBeCaught)
						{
							foreach (var (id, qty, _) in GameData.GetAnimalDrops(entity.Species))
							{
								Program.GiveItemToPlayer(id, qty, entity.WorldPos);
							}
						}
					}
				}
				// Appliquer usure de la main si les armes/outils peuvent se casser
				if (!NetworkManager.IsClient && WorldWeaponsBreak)
				{
					ApplyDurabilityToMainHand(1);
				}
				return; // ← SORTIE : on ne touche pas aux blocs si on a frappé des ennemis
			}
			} // fin du else (host)

			// ========== 2. VÉRIFICATION DE LA DISTANCE POUR LES BLOCS ==========
			if (bestTileX != -1 && bestTileY != -1)
			{
				float distanceToTile = Vector2.Distance(
					new Vector2(playerTileX * ts + ts / 2f, playerTileY * ts + ts / 2f),
					new Vector2(bestTileX * ts + ts / 2f, bestTileY * ts + ts / 2f)
				);
				if (distanceToTile > MAX_INTERACTION_DISTANCE)
				{
					return;
				}
			}

			// ========== 3. INTERACTIONS SUR LES BLOCS (casser, planter, etc.) ==========
			// TRANSFORMATION HERBE -> CHEMIN AVEC PELLE (TERRAIN)
			if (heldItem != null && GameData.GetToolType(heldItem.Name) == ToolType.Shovel)
			{
				// Vérifier d'abord si on clique sur une tuile de bateau
				Boat? playerBoatForShovel = World.GetBoatAtPosition(_playerPos, ts);
				if (playerBoatForShovel != null)
				{
					Vector2 localMouseForShovel = mouseWorld - playerBoatForShovel.Position;
					int relX = (int)Math.Floor((localMouseForShovel.X + ts/2f) / ts);
					int relY = (int)Math.Floor((localMouseForShovel.Y + ts/2f) / ts);
					
					if (playerBoatForShovel.Tiles.ContainsKey((relX, relY)))
					{
						// Vérifier qu'il n'y a pas d'objet sur cette tuile du bateau
						if (playerBoatForShovel.GetObjectIdAt(relX, relY) == 0)
						{
							// Supprimer la tuile du bateau
							if (playerBoatForShovel.Tiles.Count <= 1)
							{
								// Détruire complètement le bateau
								World.GetAllBoats().Remove(playerBoatForShovel.Id);
								AddNotification(new Notification(" Bateau détruit !", new Color(200, 180, 100, 255), 1.5f));
							}
							else
							{
								playerBoatForShovel.Tiles.Remove((relX, relY));
								AddNotification(new Notification(" Tuile de bateau cassée !", new Color(200, 180, 100, 255), 1.5f));
							}
							
							attackAnim = 0.2f;
							isAttacking = true;
							
							// Ajouter des particules
							Vector2 boatWorldPos = playerBoatForShovel.GetTileWorldPos(relX, relY);
							SpawnRockBreakParticles(boatWorldPos);
							SpawnSlashEffect(boatWorldPos, attackDir);
							
							return;
						}
					}
				}
				
				// Comportement normal : transformer herbe en chemin
				int groundId = World.GetGroundTileIdAt(bestTileX, bestTileY);
				if (IsGrassTile(groundId) && World.GetObjectIdAt(bestTileX, bestTileY) == 0)
				{
					World.SetGroundTile(bestTileX, bestTileY, 61);
					attackAnim = 0.2f;
					isAttacking = true;
					if (WorldWeaponsBreak)
						ApplyDurabilityToMainHand(1);
					return;
				}
			}
			
			int heightAtTarget = World.GetHeightAt(bestTileX, bestTileY);
			float drawY = bestTileY * ts - (heightAtTarget * ts / 4);
			
			//  BOIRE : interagir face à une case d'eau (mêmes IDs que la détection
			// de flottaison des bateaux) restaure la soif, à main vide ou avec un
			// objet non consommable en main.
			if (heldItem == null || GameData.ItemDatabase.TryGetValue(GetItemId(heldItem.Name), out var maybeItemData) == false || maybeItemData.Type != ItemType.Food)
			{
				Boat? currentBoat = World.GetBoatAtPosition(_playerPos, ts);
				bool isBoatTile = false;
				if (currentBoat != null)
				{
					Vector2 localMouse = mouseWorld - currentBoat.Position;
					int relX = (int)Math.Floor((localMouse.X + ts/2f) / ts);
					int relY = (int)Math.Floor((localMouse.Y + ts/2f) / ts);
					isBoatTile = currentBoat.Tiles.ContainsKey((relX, relY));
				}
				if (!isBoatTile)
				{
					int drinkTileX = (int)Math.Floor(mouseWorld.X / ts);
					int drinkTileY = (int)Math.Floor(mouseWorld.Y / ts);
					int drinkGroundId = World.GetGroundTileIdAt(drinkTileX, drinkTileY);
					bool isWaterTile = (drinkGroundId == 10);
					if (isWaterTile && playerThirst < playerMaxThirst)
					{
						playerThirst = Math.Min(playerMaxThirst, playerThirst + 40f);
						AddNotification(new Notification("Vous buvez de l'eau.", new Color(70, 160, 230, 255), 1.5f));
						attackAnim = 0.2f;
						isAttacking = true;
						return;
					}
				}
			}
			
			//  BOIRE (contenant de liquide) : système unifié, fonctionne avec n'importe quel
			// contenant de liquide (gourde, arrosoir, seau...) pour tous les liquides buvables
			// (eau, lait...).
			if (heldItem != null && !ItemRenderer.IsPotionMetadata(heldItem.Metadata)
				&& TryGetLiquidContainer(heldItem, out string drinkLiquidType, out int drinkCharges, out _))
			{
				if (drinkCharges > 0)
				{
					SetLiquidContainerCharges(heldItem, drinkLiquidType, drinkCharges - 1);
					if (drinkLiquidType == "milk")
					{
						playerThirst = Math.Min(playerMaxThirst, playerThirst + 10);
						playerHunger = Math.Min(playerMaxHunger, playerHunger + 8);
						AddNotification(new Notification(" Vous buvez une gorgée de lait.", new Color(240, 240, 220, 255), 1.5f));
					}
					else
					{
						playerThirst = Math.Min(playerMaxThirst, playerThirst + 20);
						AddNotification(new Notification(" Vous buvez une gorgée d'eau.", new Color(70, 160, 230, 255), 1.5f));
					}
					PlayDrinkSound();
					attackAnim = 0.2f;
					isAttacking = true;
					return;
				}
				else
				{
					AddNotification(new Notification(" Le contenant est vide.", new Color(255, 200, 100, 255), 1.5f));
					return;
				}
			}
			
			// BOUTEILLE / POTION GÉNÉRIQUE
			if (heldItem != null)
			{
				int heldId = GetItemId(heldItem.Name);
				bool hasPotionMetadata = ItemRenderer.IsPotionMetadata(heldItem.Metadata);
				bool hasItemData = GameData.ItemDatabase.TryGetValue(heldId, out var bottleItemData);
				if (hasPotionMetadata || (hasItemData &&
					(bottleItemData.ID == GetItemId("Petite bouteille")
						|| bottleItemData.TextureName.Contains("glassbottle", StringComparison.OrdinalIgnoreCase))))
				{
					if (ItemRenderer.TryGetBottleContentInfo(heldItem, bottleItemData, out string contentType, out int amount))
					{
						if (contentType.Equals("heal", StringComparison.OrdinalIgnoreCase))
						{
							bool canHeal = playerHP < GetEffectivePlayerMaxHP();
							if (canHeal)
							{
								playerHP = Math.Min(GetEffectivePlayerMaxHP(), playerHP + amount);
								EmptyBottleItem(heldItem);
								AddFloatingHeal(GetPlayerVisualPosition(playerPos), amount);
								SpawnHealParticles(playerPos);
								AddNotification(new Notification($" Vous buvez une potion de soin (+{amount} PV).", new Color(240, 80, 80, 255), 1.8f));
							}
							else
							{
								AddNotification(new Notification(" Vous êtes déjà en pleine forme !", new Color(255, 200, 100, 255), 1.5f));
							}
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
						else if (contentType.Equals("speed", StringComparison.OrdinalIgnoreCase))
						{
							EmptyBottleItem(heldItem);
							_speedPotionTimer = _speedPotionTotalDuration = amount > 0 ? amount : 30;
							AddNotification(new Notification($" Vous buvez une potion de vitesse ! Vitesse augmentée pendant {_speedPotionTimer} secondes.", new Color(100, 160, 255, 255), 2f));
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
						else if (contentType.Equals("strength", StringComparison.OrdinalIgnoreCase))
						{
							EmptyBottleItem(heldItem);
							_strengthPotionTimer = _strengthPotionTimerMax = amount > 0 ? amount : 60;
							AddNotification(new Notification($" Vous buvez une potion de force ! Dégâts augmentés pendant {_strengthPotionTimer} secondes.", new Color(200, 40, 30, 255), 2f));
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
						else if (contentType.Equals("resistance", StringComparison.OrdinalIgnoreCase))
						{
							EmptyBottleItem(heldItem);
							_resistancePotionTimer = _resistancePotionTimerMax = amount > 0 ? amount : 60;
							AddNotification(new Notification($" Vous buvez une potion de résistance ! Dégâts subis réduits pendant {_resistancePotionTimer} secondes.", new Color(120, 110, 100, 255), 2f));
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
						else if (contentType.Equals("light", StringComparison.OrdinalIgnoreCase))
						{
							EmptyBottleItem(heldItem);
							_lightPotionTimer = _lightPotionTimerMax = amount > 0 ? amount : 120;
							AddNotification(new Notification($" Vous buvez une potion de lumière ! Vous émettez de la lumière pendant {_lightPotionTimer} secondes.", new Color(255, 240, 150, 255), 2f));
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
						else if (contentType.Equals("heatresist", StringComparison.OrdinalIgnoreCase))
						{
							EmptyBottleItem(heldItem);
							_heatResistPotionTimer = _heatResistPotionTimerMax = amount > 0 ? amount : 90;
							AddNotification(new Notification($" Vous buvez une potion de résistance à la chaleur pendant {_heatResistPotionTimer} secondes.", new Color(255, 130, 40, 255), 2f));
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
						else if (contentType.Equals("coldresist", StringComparison.OrdinalIgnoreCase))
						{
							EmptyBottleItem(heldItem);
							_coldResistPotionTimer = _coldResistPotionTimerMax = amount > 0 ? amount : 90;
							AddNotification(new Notification($" Vous buvez une potion de résistance au froid pendant {_coldResistPotionTimer} secondes.", new Color(130, 200, 255, 255), 2f));
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
						else if (contentType.Equals("invisibility", StringComparison.OrdinalIgnoreCase))
						{
							EmptyBottleItem(heldItem);
							_invisibilityPotionTimer = _invisibilityPotionTimerMax = amount > 0 ? amount : 20;
							AddNotification(new Notification($" Vous buvez une potion d'invisibilité ! Invisible pendant {_invisibilityPotionTimer} secondes.", new Color(220, 220, 235, 255), 2f));
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
						else if (contentType.Equals("empty", StringComparison.OrdinalIgnoreCase))
						{
							AddNotification(new Notification(" La bouteille est vide.", new Color(180, 180, 180, 255), 1.5f));
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
						else if (contentType.Equals("none", StringComparison.OrdinalIgnoreCase) || contentType.Equals("custom", StringComparison.OrdinalIgnoreCase))
						{
							EmptyBottleItem(heldItem);
							AddNotification(new Notification(" Le breuvage est sans effet.", new Color(220, 180, 100, 255), 1.5f));
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
					}
				}
			}
			
			// NOURRITURE
			if (heldItem != null)
			{
				int heldId = GetItemId(heldItem.Name);
				if (GameData.ItemDatabase.TryGetValue(heldId, out var itemData) && 
					itemData.Type == ItemType.Food && 
					(itemData.HealAmount > 0 || itemData.HungerRestore != 0f || itemData.ThirstRestore != 0f))
				{
					if (heldItem.HasMeta("burned"))
					{
						AddNotification(new Notification("Cet aliment est brûlé et inmangeable.", new Color(220, 80, 60, 255), 1.8f));
						return;
					}

					//  Manger restaure la vie ET la faim (et un peu la soif). On ne
					// consomme l'item que si au moins l'une des trois jauges n'est
					// pas déjà pleine, pour ne pas gâcher de la nourriture pour rien.
					bool canHeal = playerHP < GetEffectivePlayerMaxHP();
					bool canFeed = playerHunger < playerMaxHunger;
					bool canHydrate = playerThirst < playerMaxThirst;
					
					if (canHeal || canFeed || canHydrate)
					{
						int healAmount = itemData.HealAmount;
						float hungerRestore = itemData.HungerRestore != 0f ? itemData.HungerRestore : healAmount * 2f;   // priorité au champ item
						float thirstRestore = itemData.ThirstRestore != 0f ? itemData.ThirstRestore : healAmount * 0.5f; // fallback rétrocompatible

						playerHP = Math.Min(GetEffectivePlayerMaxHP(), playerHP + healAmount);
						playerHunger = Math.Min(playerMaxHunger, playerHunger + hungerRestore);
						playerThirst = Math.Min(playerMaxThirst, playerThirst + thirstRestore);
						
						//  RASSASIÉ : si la faim est désormais au maximum, la barre ne
						// redescend plus pendant un moment (voir IsPlayerSated / _satietyTimer),
						// et l'icône "info_satiety_boost" apparaît dans le HUD.
						if (playerHunger >= playerMaxHunger)
						{
							bool wasAlreadySated = _satietyTimer > 0f;
							_satietyTimer = SATIETY_DURATION;
							if (!wasAlreadySated)
								AddNotification(new Notification(" Vous êtes rassasié ! Votre faim ne baissera plus pendant un moment.", new Color(255, 200, 100, 255), 2.5f));
						}
						
						RemoveItemFromInventory(heldItem.Name, 1);
						// Jouer un son d'"eat" ou de "drink" selon ce qui est le plus restauré
						if (thirstRestore > hungerRestore)
							PlayDrinkSound();
						else
							PlayEatSound();
						
						//  AJOUTER LES PARTICULES DE SOIN
						Vector2 healPos = GetPlayerVisualPosition(playerPos);
						if (canHeal) AddFloatingHeal(healPos, healAmount);
						
						attackAnim = 0.2f;
						isAttacking = true;
						
						SpawnHealParticles(playerPos);
					}
					else
					{
						AddNotification(new Notification(" Vous êtes déjà en pleine forme !", new Color(255, 200, 100, 255), 1.5f));
					}
					return;
				}
			}

			
			// CISEAUX (changement de coiffure)
			if (heldItem != null && GameData.GetToolType(heldItem.Name) == ToolType.Scissors)
			{
				int newHairStyle = Random.Shared.Next(0, hairBaseTextures.Count);
				PlayerHairStyle = newHairStyle;
				if (beardBaseTextures.Count > 0)
				{
					int newBeardStyle = Random.Shared.Next(0, beardBaseTextures.Count);
					PlayerBeardStyle = newBeardStyle;
				}
				
				attackAnim = 0.2f;
				isAttacking = true;
				
				SpawnHairParticles(playerPos);
				
				return;
			}

			// ========== 1. INTERACTION SUR BATEAU ==========
			Boat? playerBoat = World.GetBoatAtPosition(_playerPos, ts);
			if (playerBoat != null)
			{
				Vector2 localMouse = mouseWorld - playerBoat.Position;
				int relX = (int)Math.Floor((localMouse.X + ts/2f) / ts);
				int relY = (int)Math.Floor((localMouse.Y + ts/2f) / ts);
				if (playerBoat.Tiles.ContainsKey((relX, relY)))
				{
					int boatObjectId = playerBoat.GetObjectIdAt(relX, relY);
					if (boatObjectId != 0)
					{
						var boatTileData = WorldTileRegistry.GetTile(boatObjectId);
						if (boatTileData != null && boatTileData.Diggable)
						{
							// Appliquer les dégâts (même logique que pour le monde normal)
							string boatKey = $"{_playerPos.X}_{_playerPos.Y}_boat_{playerBoat.Id}"; // clé unique pour ce bateau
							if (!objectHPs.ContainsKey(boatKey))
								objectHPs[boatKey] = new WorldObjectHP(boatTileData.Hardness);
							var boatHp = objectHPs[boatKey];
							float boatPower = (heldItem != null) ? GameData.GetMiningPower(heldItem.Name) : 1f;
							int boatDamage = Math.Max(1, (int)boatPower);
							boatHp.Current -= boatDamage;
							boatHp.DamageFlash = 0.2f;
							attackCooldown = 0.35f;
							isAttacking = true;
							attackAnim = 0.25f;
							
							// Position visuelle de l'objet pour les dégâts flottants
							Vector2 boatObjectWorldPos = playerBoat.GetTileWorldPos(relX, relY);
							int boatObjTileX = (int)(boatObjectWorldPos.X / ts);
							int boatObjTileY = (int)(boatObjectWorldPos.Y / ts);
							int boatObjHeight = World.GetHeightAt(boatObjTileX, boatObjTileY);
							float boatObjDrawY = boatObjTileY * ts - (boatObjHeight * ts / 4);
							AddFloatingDamage(new Vector2(boatObjectWorldPos.X, boatObjDrawY + ts/2f), boatDamage, isEnemy: true);
							
							if (boatHp.Current <= 0)
							{
								// Gérer les conteneurs sur le bateau
								if (boatTileData.IsContainer == true && playerBoat.Containers.TryGetValue((relX, relY), out var boatContainer))
								{
									foreach (var slot in boatContainer.Slots)
									{
										if (!slot.IsEmpty && slot.Item != null)
										{
											int itemId = GetItemId(slot.Item.Name);
											if (itemId > 0 && GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
											{
												Program.GiveItemToPlayer(itemId, slot.Count, boatObjectWorldPos, pickupCooldown: 0f);
											}
										}
									}
									playerBoat.Containers.Remove((relX, relY));
								}
								
								// Gérer les porte-armures sur le bateau
								if (boatTileData.IsArmorStand == true && playerBoat.ArmorStands.TryGetValue((relX, relY), out var boatStandData))
								{
									if (boatStandData.Head != null)
									{
										int itemId = GetItemId(boatStandData.Head.Name);
										Program.GiveItemToPlayer(itemId, 1, boatObjectWorldPos, pickupCooldown: 0f);
									}
									if (boatStandData.Body != null)
									{
										int itemId = GetItemId(boatStandData.Body.Name);
										Program.GiveItemToPlayer(itemId, 1, boatObjectWorldPos, pickupCooldown: 0f);
									}
									if (boatStandData.Legs != null)
									{
										int itemId = GetItemId(boatStandData.Legs.Name);
										Program.GiveItemToPlayer(itemId, 1, boatObjectWorldPos, pickupCooldown: 0f);
									}
									if (boatStandData.MainHand != null)
									{
										int itemId = GetItemId(boatStandData.MainHand.Name);
										Program.GiveItemToPlayer(itemId, 1, boatObjectWorldPos, pickupCooldown: 0f);
									}
									if (boatStandData.OffHand != null)
									{
										int itemId = GetItemId(boatStandData.OffHand.Name);
										Program.GiveItemToPlayer(itemId, 1, boatObjectWorldPos, pickupCooldown: 0f);
									}
									boatStandData.Clear();
									playerBoat.ArmorStands.Remove((relX, relY));
								}
								
								// Drops normaux de l'objet
								foreach (var (id, qty) in WorldTileRegistry.GetDrops(boatObjectId))
								{
									Program.GiveItemToPlayer(id, qty, boatObjectWorldPos, pickupCooldown: 0f);
								}
								
								//  PARTICULES DE DESTRUCTION DE L'OBJET SUR LE BATEAU
								Color boatTileColor = boatTileData != null ? 
									new Color(150, 100, 60, 200) : new Color(150, 100, 60, 200);
								SpawnTileBreakParticles(boatObjectWorldPos, boatTileColor);
								
								// Supprimer l'objet du bateau
								playerBoat.SetObjectAt(relX, relY, 0);
								objectHPs.Remove(boatKey);
								
								// Mettre à jour les toits si c'était un mur (optionnel, rarement sur bateau)
								if (boatObjectId == 8 || boatObjectId == 84)
								{
									World.UpdateRoofsAt(boatObjTileX, boatObjTileY);
								}
								
								activeNotifications.Add(new Notification($"Objet détruit sur le bateau", new Color(200, 180, 100, 255), 1.5f));
							}
							return; // Sortir car l'interaction a été traitée
						}
					}
				}
			}

			// ========== 2. INTERACTION SUR MONDE NORMAL (inchangé) ==========
			int objId = World.GetObjectIdAt(bestTileX, bestTileY);

			// ---- Socle de gemme-oeil avec l'oeil (118) frappé avec le Doigt de Biggie ----
			if (objId == 118)
			{
				if (heldItem != null && GetItemId(heldItem.Name) == GetItemId("biggie_finger"))
				{
					if (!_eyeBossActive)
					{
						StartEyeBossFight(bestTileX, bestTileY);
						attackAnim = 0.3f;
						isAttacking = true;
					}
					else
					{
						AddNotification(new Notification(" Le combat est déjà en cours !", new Color(220, 150, 100, 255), 1.5f));
					}
				}
				//  Le socle avec l'oeil ne peut pas être cassé normalement : il faut vaincre le
				// boss (voir EndEyeBossFight) pour qu'il redevienne un socle vide cassable.
				return;
			}
			{
				var tileData = WorldTileRegistry.GetTile(objId);
				if (tileData != null && tileData.IsCrop)
				{
					var cropData = World.GetCropDataAt(bestTileX, bestTileY);
					if (cropData != null)
					{
						// La maturité se base sur le temps de croissance réellement accumulé
						// (n'avance que quand le sol est humide), pas sur le temps écoulé brut.
						float elapsed = cropData.AccumulatedGrowthTime;
						if (elapsed >= tileData.GrowthTime)
						{
							// RÉCOLTE
							int qty = Random.Shared.Next(tileData.HarvestMinQty, tileData.HarvestMaxQty + 1);
							int harvestItemId = GameData.GetItemId(tileData.HarvestItemId);
							if (harvestItemId <= 0)
							{
								AddNotification(new Notification($" Item de récolte introuvable : {tileData.HarvestItemId}", new Color(220, 100, 100, 255), 2f));
								return;
							}
							Program.GiveItemToPlayer(harvestItemId, qty, new Vector2(bestTileX * ts + ts/2f, drawY + ts/2f));

							if (tileData.RegrowsAfterHarvest)
							{
								World.BeginCropRegrowth(bestTileX, bestTileY, tileData, cropData);
							}
							else
							{
								World.RemovePlacedObject(bestTileX, bestTileY);
							}
							attackAnim = 0.2f;
							isAttacking = true;
							return;
						}
					}
				}
			}
			
			if (heldItem != null && GameData.GetToolType(heldItem.Name) == ToolType.Hoe)
			{
				int groundId = World.GetGroundTileIdAt(bestTileX, bestTileY);
				bool isTillable = (groundId == 1 || groundId == 44 || groundId == 46 || 
								   groundId == 47 || groundId == 43 || groundId == 26);
				
				if (isTillable && World.GetObjectIdAt(bestTileX, bestTileY) == 0)
				{
					World.SetGroundTile(bestTileX, bestTileY, 27);
					SoundEffects.PlayHoe();
					attackAnim = 0.2f;
					isAttacking = true;
					return;
				}
			}
			
			if (bestTileX != -1 && bestTileY != -1)
			{
				int objIdAtPos = World.GetObjectIdAt(bestTileX, bestTileY);
				var objTileData = WorldTileRegistry.GetTile(objIdAtPos);
			}

			// ========== CAS PAR DÉFAUT : CASSER UN BLOC ==========
			int tileId = World.GetObjectIdAt(bestTileX, bestTileY);
			if (tileId == 0) tileId = World.GetGroundTileIdAt(bestTileX, bestTileY);
			var tileDataObj = WorldTileRegistry.GetTile(tileId);
			if (tileDataObj == null) return;
			if (tileDataObj.Collectable)
			{
				string collectKey = $"{bestTileX}_{bestTileY}";
				if (!destroyedObjects.Contains(collectKey))
				{
					foreach (var (id, qty) in WorldTileRegistry.GetDrops(tileId))
					{
						Program.GiveItemToPlayer(id, qty, new Vector2(bestTileX * ts + ts/2f, drawY + ts/2f), pickupCooldown: 0f);
					}
					World.RemovePlacedObject(bestTileX, bestTileY);
					attackCooldown = 0.1f;
					isAttacking = true;
					attackAnim = 0.15f;
				}
				return;
			}
			if (!tileDataObj.Diggable) return;
			// Refinement: Only prevent mining if we are looking at a ground tile AND have no tool equipped or the tool isn't suitable for ANY digging activity.
			bool isGroundTile = (World.GetObjectIdAt(bestTileX, bestTileY) == 0);
			
			//  SEULS LES OUTILS APPROPRIÉS PEUVENT CREUSER LE SOL
			if (isGroundTile)
			{
				if (heldItem == null) return;
				
				ToolType toolType = GameData.GetToolType(heldItem.Name);
				// Seule la pelle, la houe et l'arrosoir peuvent interagir avec le sol
				if (toolType != ToolType.Shovel && 
					toolType != ToolType.Hoe && 
					toolType != ToolType.WateringCan &&
					toolType != ToolType.Scythe)  // La faux pour récolter les cultures
				{
					return;  //  Les autres outils ne creusent pas le sol
				}
			}
			
			string objKey = $"{bestTileX}_{bestTileY}";
			if (destroyedObjects.Contains(objKey)) return;
			if (!objectHPs.ContainsKey(objKey)) objectHPs[objKey] = new WorldObjectHP(tileDataObj.Hardness);
			var hp = objectHPs[objKey];
			float power = (heldItem != null) ? GameData.GetMiningPower(heldItem.Name) : 1f;
			int damageAmount = Math.Max(1, (int)power);
			hp.Current -= damageAmount;
			hp.DamageFlash = 0.2f;
			if (World.IsLeafBearingTree(tileId))
				World.TriggerTreeHitSway(bestTileX, bestTileY, _playerPos, (float)GetGameTime(), ts);
			if (tileDataObj.MiningTool == "axe") SkillSystem.AddXP(SkillType.Bucheron, 1);
			else if (tileDataObj.MiningTool == "pickaxe") SkillSystem.AddXP(SkillType.Mineur, 1);
			PlayTileSound(tileDataObj, false);
			//  Quelques feuilles se détachent à chaque coup de hache dans un arbre
			if (World.IsLeafBearingTree(tileId))
			{
				World.SpawnLeafBurst(bestTileX, bestTileY, _particles, Random.Shared.Next(2, 5));
			}
			attackCooldown = 0.35f;
			isAttacking = true;
			attackAnim = 0.25f;
			float finalWorldY = bestTileY * ts - (heightAtTarget * ts / 4) + ts; // bas de la tuile
			Vector2 objectWorldPos = new Vector2(bestTileX * ts + ts/2f, finalWorldY - 15f); // légèrement au-dessus
			if (World.IsLeafBearingTree(tileId))
				TrySpawnBirdFromTreeHit(bestTileX, bestTileY);
			AddFloatingDamage(objectWorldPos, damageAmount, isEnemy: true);
			if (hp.Current <= 0)
			{
				if (tileId == 86)
				{
					const string flowerMetaKey = "potted_flower";
					string plantedFlowerId = World.GetTileMeta(bestTileX, bestTileY, flowerMetaKey);
								if (int.TryParse(plantedFlowerId, out int flowerItemId))
					{
						Program.GiveItemToPlayer(flowerItemId, 1,
							new Vector2(bestTileX * ts + ts / 2f, drawY + ts / 2f), pickupCooldown: 0f);
					}
					World.RemoveTileMeta(bestTileX, bestTileY, flowerMetaKey);
				}

				if (tileDataObj.IsContainer == true)
				{
					var chunk = World.GetChunkAt(bestTileX, bestTileY);
					if (chunk != null)
					{
						var containerData = chunk.GetContainerAt(bestTileX, bestTileY);
						if (containerData != null)
						{
							foreach (var slot in containerData.Slots)
							{
								if (!slot.IsEmpty && slot.Item != null)
								{
									int itemId = GetItemId(slot.Item.Name);
									if (itemId > 0 && GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
									{
										Program.GiveItemToPlayer(itemId, slot.Count, new Vector2(bestTileX * ts + ts/2f, drawY + ts/2f), pickupCooldown: 0f);
									}
								}
							}
							chunk.Containers.Remove((bestTileX, bestTileY));
						}
					}
				}

				if (tileDataObj.IsArmorStand == true)
				{
					var chunk = World.GetChunkAt(bestTileX, bestTileY);
					if (chunk != null)
					{
						var standData = chunk.GetArmorStandAt(bestTileX, bestTileY);
						if (standData != null)
						{
							if (standData.Head != null)
							{
								int itemId = GetItemId(standData.Head.Name);
								Program.GiveItemToPlayer(itemId, 1, new Vector2(bestTileX * ts + ts/2f, drawY + ts/2f), pickupCooldown: 0f);
							}
							if (standData.Body != null)
							{
								int itemId = GetItemId(standData.Body.Name);
								Program.GiveItemToPlayer(itemId, 1, new Vector2(bestTileX * ts + ts/2f, drawY + ts/2f), pickupCooldown: 0f);
							}
							if (standData.Legs != null)
							{
								int itemId = GetItemId(standData.Legs.Name);
								Program.GiveItemToPlayer(itemId, 1, new Vector2(bestTileX * ts + ts/2f, drawY + ts/2f), pickupCooldown: 0f);
							}
							if (standData.MainHand != null)
							{
								int itemId = GetItemId(standData.MainHand.Name);
								Program.GiveItemToPlayer(itemId, 1, new Vector2(bestTileX * ts + ts/2f, drawY + ts/2f), pickupCooldown: 0f);
							}
							if (standData.OffHand != null)
							{
								int itemId = GetItemId(standData.OffHand.Name);
								Program.GiveItemToPlayer(itemId, 1, new Vector2(bestTileX * ts + ts/2f, drawY + ts/2f), pickupCooldown: 0f);
							}
							standData.Clear();
							chunk.SetArmorStandAt(bestTileX, bestTileY, standData);
						}
					}
				}
				
				int mineralOverlayId = World.GetOverlayAt(bestTileX, bestTileY);
				if (mineralOverlayId != 0 && mineralOverlayId != 82)
				{
					int mineralItemId = GetMineralItemId(mineralOverlayId);
					if (mineralItemId != 0)
					{
						int qty = Random.Shared.Next(1, 3);
						Program.GiveItemToPlayer(mineralItemId, qty, new Vector2(bestTileX * ts + ts/2f, drawY + ts/2f), pickupCooldown: 0f);
					}
					World.RemoveOverlay(bestTileX, bestTileY);
				}
                else if (World.HasTileMeta(bestTileX, bestTileY, "ore"))
                {
                    // Ore metadata no longer forces a mineral item drop; preserve the block's normal drops.
                    World.RemoveTileMeta(bestTileX, bestTileY, "ore");
                }

                //  PARTICULES DE DESTRUCTION DE LA TUILE (avant suppression)
				Color tileColor = tileDataObj != null ? 
					new Color(150, 100, 60, 200) : new Color(150, 100, 60, 200);
				SpawnTileBreakParticles(objectWorldPos, tileColor);
				PlayTileSound(tileDataObj, true);

				//  Un arbre abattu perd une grosse quantité de feuilles d'un coup
				if (World.IsLeafBearingTree(tileId))
					World.SpawnLeafBurst(bestTileX, bestTileY, _particles, Random.Shared.Next(14, 22));

				if (World.HasTileMeta(bestTileX, bestTileY, "beehive"))
				{
					int beeWaxId = GameData.GetItemId("bee_wax");
					if (beeWaxId > 0)
						Program.GiveItemToPlayer(beeWaxId, 1, new Vector2(bestTileX * ts + ts / 2f, drawY + ts / 2f), pickupCooldown: 0f);
					World.RemoveTileMeta(bestTileX, bestTileY, "beehive");
				}

				World.RemovePlacedObject(bestTileX, bestTileY);
				if (tileId == 8 || tileId == 84)
				{
					World.UpdateRoofsAt(bestTileX, bestTileY);
				}
				objectHPs.Remove(objKey);
				foreach (var (id, qty) in WorldTileRegistry.GetDrops(tileId))
				{
					Program.GiveItemToPlayer(id, qty, new Vector2(bestTileX * ts + ts/2f, drawY + ts/2f), pickupCooldown: 0f);
				}
			}
		}

		private static void TrySpawnBirdFromTreeHit(int treeTileX, int treeTileY)
		{
			const double escapeChance = 0.18;
			const float nearbyRadius = 110f;
			const int maxNearbyBirds = 3;

			if (NetworkManager.IsClient || Random.Shared.NextDouble() >= escapeChance)
				return;
			if (!World.TryGetTreeBirdSpawnPosition(treeTileX, treeTileY, out Vector2 treeWorldPos))
				return;

			int nearbyBirdCount = 0;
			foreach (var entity in entities)
			{
				if (entity.IsAlive && entity.IsBird && Vector2.DistanceSquared(entity.WorldPos, treeWorldPos) <= nearbyRadius * nearbyRadius)
					nearbyBirdCount++;
			}
			if (nearbyBirdCount >= maxNearbyBirds)
				return;

			var availableSpecies = new List<string>();
			if (SpeciesData.Species.ContainsKey("sparrow")) availableSpecies.Add("Sparrow");
			if (SpeciesData.Species.ContainsKey("pigeon")) availableSpecies.Add("Pigeon");
			if (availableSpecies.Count == 0)
				return;

			string species = availableSpecies[Random.Shared.Next(availableSpecies.Count)];
			Vector2 spawnPos = treeWorldPos + new Vector2(
				(float)(Random.Shared.NextDouble() * 30f - 15f),
				(float)(Random.Shared.NextDouble() * 24f - 12f));
			var bird = new Entity(spawnPos, species, false);
			entities.Add(bird);
			bird.EscapeFromTreeHit(treeWorldPos);
		}

		static (int tileX, int tileY, float dist, int objectId) FindTileUnderMouse(Vector2 mouseWorld, int ts)
		{
			EnsureMaxObjectSizeCached();

			// Stabilité numérique : quand le joueur/la caméra est proche de l'origine du monde
			// (ce qui est très fréquent, puisque c'est là que la partie démarre), les calculs de
			// caméra (Raylib.GetScreenToWorld2D, multiplications/divisions en float) peuvent
			// produire des valeurs infimement négatives (ex: -0.0000004) là où la valeur "logique"
			// est exactement 0.0. Comme Math.Floor bascule sur un changement de signe, ce bruit
			// flottant suffit à faire calculer la tuile -1 au lieu de 0 : c'est précisément sur les
			// lignes X=0 et Y=0 que cet artefact devient visible, puisque c'est le SEUL endroit où
			// la valeur "logique" tombe exactement sur une frontière de tuile ET sur un changement
			// de signe (aux autres frontières, ex. x=40, une imprécision de 1e-6 ne fait pas
			// basculer le signe du résultat de la division). On absorbe ce bruit avant de floorer.
			const float epsilon = 0.001f;
			float snappedX = mouseWorld.X;
			float snappedY = mouseWorld.Y;
			float remX = snappedX / ts - MathF.Round(snappedX / ts);
			float remY = snappedY / ts - MathF.Round(snappedY / ts);
			if (MathF.Abs(remX) < epsilon) snappedX = MathF.Round(snappedX / ts) * ts;
			if (MathF.Abs(remY) < epsilon) snappedY = MathF.Round(snappedY / ts) * ts;

			int mouseTileX = (int)Math.Floor(snappedX / ts);
			int mouseTileY = (int)Math.Floor(snappedY / ts);

			// La tuile d'ancrage d'un objet est son coin BAS-GAUCHE (voir World.GetCollisionRect /
			// DrawWorld). Un objet dessiné sous le curseur peut donc avoir son ancrage jusqu'à
			// (largeur-1) tuiles à GAUCHE et (hauteur-1) tuiles en DESSOUS (y plus grand) du
			// curseur. On élargit la fenêtre de recherche en conséquence, plus une petite marge
			// pour le sol/les tuiles simples autour du curseur.
			const int groundPad = 2;
			int heightPad = (World.MAX_HEIGHT_LEVELS + 3) / 4;
			int startX = mouseTileX - Math.Max(groundPad, _maxObjectTileWidth - 1);
			int endX = mouseTileX + groundPad;
			int startY = mouseTileY - groundPad;
			int endY = mouseTileY + Math.Max(groundPad, _maxObjectTileHeight - 1) + heightPad;

			// IMPORTANT : on garde deux résultats séparés. Un objet touché QUELQUE PART sur son
			// sprite doit toujours l'emporter sur la tuile de sol la plus proche du curseur —
			// sinon, dès qu'on ne clique pas pile sur la tuile d'ancrage (bas-gauche) d'un objet
			// de plusieurs tuiles, c'est systématiquement le sol qui "gagnait" (il est presque
			// toujours plus proche du curseur que l'ancrage d'un gros objet), et l'objet devenait
			// impossible à casser/porter/utiliser ailleurs que sur cette seule tuile.
			int objTileX = -1, objTileY = -1, objId = 0;
			float bestObjDist = float.MaxValue;

			int groundTileX = -1, groundTileY = -1, groundId2 = 0;
			float bestGroundDist = float.MaxValue;

			for (int x = startX; x <= endX; x++)
			{
				for (int y = startY; y <= endY; y++)
				{
					int height = World.GetHeightAt(x, y);
					float visualY = y * ts - (height * ts / 4);
					float visualX = x * ts;
					float centerX = visualX + ts / 2f;
					float centerY = visualY + ts / 2f;
					float dist = Vector2.Distance(mouseWorld, new Vector2(centerX, centerY));

					int objectId = World.GetObjectIdAt(x, y);

					if (objectId != 0)
					{
						var tileData = WorldTileRegistry.GetTile(objectId);
						if (tileData != null)
						{
							// On teste le clic contre la HITBOX réelle de l'objet (même géométrie
							// que les collisions physiques), pas contre tout son sprite visuel :
							// un meuble de 2 tuiles de haut dont la hitbox ne couvre que la tuile
							// du bas ne doit pas capter les clics sur sa partie haute, sinon il
							// masque/bloque l'interaction avec un meuble placé derrière lui.
							var (hbX, hbY, hbW, hbH) = World.GetCollisionRect(x, y, tileData, ts);
							Rectangle rect = new Rectangle(hbX, hbY, hbW, hbH);
							if (Raylib.CheckCollisionPointRec(mouseWorld, rect))
							{
								// On garde l'objet dont la tuile d'ancrage est la plus proche du
								// curseur, au cas où deux sprites se chevaucheraient visuellement.
								if (dist < bestObjDist) { bestObjDist = dist; objTileX = x; objTileY = y; objId = objectId; }
							}
						}
					}

					// Vérifier le sol (seulement utilisé si aucun objet n'a été touché)
					if (Math.Abs(mouseWorld.X - centerX) < ts * 1.2f && Math.Abs(mouseWorld.Y - centerY) < ts * 1.2f)
					{
						int gId = World.GetGroundTileIdAt(x, y);
						if (dist < bestGroundDist) { bestGroundDist = dist; groundTileX = x; groundTileY = y; groundId2 = gId; }
					}
				}
			}

			// Filet de sécurité pour les OBJETS : si la boucle ci-dessus n'a rien trouvé (par
			// exemple parce que la fenêtre de recherche ou le test de distance a raté de peu,
			// ce qui arrive typiquement sur les lignes X=0 / Y=0, cf. commentaire plus haut sur
			// le bruit flottant), on vérifie directement s'il y a un objet dont la hitbox
			// contient réellement le curseur, sans dépendre de la fenêtre ni des tolérances.
			// Avant, seul le sol bénéficiait de ce filet ; un objet raté ici (coffre, arbre,
			// porte...) devenait tout simplement impossible à cliquer sur ces lignes.
			if (objTileX == -1)
			{
				for (int x = mouseTileX - Math.Max(1, _maxObjectTileWidth - 1); x <= mouseTileX + 1; x++)
				{
					for (int y = mouseTileY - 1; y <= mouseTileY + Math.Max(1, _maxObjectTileHeight - 1); y++)
					{
						int objectId = World.GetObjectIdAt(x, y);
						if (objectId == 0) continue;
						var tileData = WorldTileRegistry.GetTile(objectId);
						if (tileData == null) continue;
						var (hbX, hbY, hbW, hbH) = World.GetCollisionRect(x, y, tileData, ts);
						Rectangle rect = new Rectangle(hbX, hbY, hbW, hbH);
						if (Raylib.CheckCollisionPointRec(mouseWorld, rect))
						{
							float dist = Vector2.Distance(mouseWorld, new Vector2(hbX + hbW / 2f, hbY + hbH / 2f));
							if (dist < bestObjDist) { bestObjDist = dist; objTileX = x; objTileY = y; objId = objectId; }
						}
					}
				}
			}

			// Un objet touché (n'importe où sur son sprite) l'emporte toujours sur le sol.
			if (objTileX != -1)
				return (objTileX, objTileY, bestObjDist, objId);

			// Si la recherche du sol n'a rien retourné (cas limite aux bords),
			// on renvoie la tuile directement sous la souris telle que calculée
			// par `mouseTileX/mouseTileY`. Cela évite que les tuiles en X/Y=0
			// soient parfois introuvables à cause des tolérances de distance.
			if (groundTileX == -1)
			{
				groundTileX = mouseTileX;
				groundTileY = mouseTileY;
				groundId2 = World.GetGroundTileIdAt(groundTileX, groundTileY);
				float centerX = groundTileX * ts + ts / 2f;
				float centerY = groundTileY * ts - (World.GetHeightAt(groundTileX, groundTileY) * ts / 4) + ts / 2f;
				bestGroundDist = Vector2.Distance(mouseWorld, new Vector2(centerX, centerY));
			}

			return (groundTileX, groundTileY, bestGroundDist, groundId2);
		}

		static void HandlePlace(Vector2 mousePos, Camera2D cam, Item? item)
		{
			int ts = Program.TileSize;
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mousePos, cam);
			
			// ========== 1. PLACEMENT SUR UN BATEAU ==========
			Boat? playerBoat = World.GetBoatAtPosition(Program.GetPlayerPosition(), ts);
			if (playerBoat != null)
			{
				Vector2 localMouse = mouseWorld - playerBoat.Position;
				int relX = (int)Math.Floor((localMouse.X + ts/2f) / ts);
				int relY = (int)Math.Floor((localMouse.Y + ts/2f) / ts);
				
				if (playerBoat.Tiles.ContainsKey((relX, relY)))
				{
					// Cas : porter un meuble
					if (_carriedFurniture != null)
					{
						if (playerBoat.GetObjectIdAt(relX, relY) != 0)
						{
							activeNotifications.Add(new Notification($"Impossible de placer ici !", new Color(255, 100, 100, 255), 1f));
							return;
						}
						playerBoat.SetObjectAt(relX, relY, _carriedFurniture.PlaceableId);
						
						// Restaurer container / armor stand si besoin
						if (_carriedFurniture.ContainerData != null)
							playerBoat.Containers[(relX, relY)] = _carriedFurniture.ContainerData;
						if (_carriedFurniture.ArmorStandData != null)
							playerBoat.ArmorStands[(relX, relY)] = _carriedFurniture.ArmorStandData;
						
						_carriedFurniture = null;

					}
					
					// Cas : item normal (placeable)
					if (item != null)
					{
						var boatItemInfo = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == item.Name);
						if (boatItemInfo.ID != 0 && boatItemInfo.PlaceableId != 0 && boatItemInfo.Type == ItemType.Placeable)
						{
							if (playerBoat.GetObjectIdAt(relX, relY) != 0)
							{
								activeNotifications.Add(new Notification($"Impossible de placer ici !", new Color(255, 100, 100, 255), 1f));
								return;
							}
							
							// Vérifier si c'est un meuble avec container/armorstand
							var placedTileData = WorldTileRegistry.GetTile(boatItemInfo.PlaceableId);
							if (placedTileData != null)
							{
								playerBoat.SetObjectAt(relX, relY, boatItemInfo.PlaceableId);
								
								// Initialiser le conteneur si nécessaire
								if (placedTileData.IsContainer == true)
								{
									var newContainer = new ContainerInventoryData(
										placedTileData.ContainerSlots ?? 20,
										placedTileData.ContainerColumns > 0 ? placedTileData.ContainerColumns : 8
									);
									playerBoat.Containers[(relX, relY)] = newContainer;
								}
								
								// Initialiser le porte-armure si nécessaire
								if (placedTileData.IsArmorStand == true)
								{
									playerBoat.ArmorStands[(relX, relY)] = new ArmorStandData();
								}
							}
							
							RemoveItemFromInventory(item.Name, 1);
								return;
						}
					}
				}
			}
			
			// ========== 2. PLACEMENT SUR LE TERRAIN NORMAL (inchangé) ==========
			(int tileX, int tileY, float dist, int groundId) = FindTileUnderMouse(mouseWorld, ts);
			
			// Vérification de la distance (limite 3 blocs)
			Vector2 playerPos = Program.GetPlayerPosition();
			float distanceToTile = Vector2.Distance(
				new Vector2((int)(playerPos.X / ts) * ts + ts / 2f, (int)(playerPos.Y / ts) * ts + ts / 2f),
				new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f)
			);
			if (distanceToTile > MAX_INTERACTION_DISTANCE)
			{
				return;
			}
			
			// Cas 0 : On porte une créature
			if (_carriedCreature != null)
			{
				// Le sol doit être praticable et libre de tout objet/créature
				int groundIdAtTile = World.GetGroundTileIdAt(tileX, tileY);
				var groundTileData = WorldTileRegistry.GetTile(groundIdAtTile);
				if (groundTileData == null || !groundTileData.Walkable || World.GetObjectIdAt(tileX, tileY) != 0)
				{
					activeNotifications.Add(new Notification($"Impossible de reposer la créature ici !", new Color(255, 100, 100, 255), 1.5f));
					return;
				}

				Vector2 targetPos = new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f);
				if (World.IsCollidingEntity(targetPos, destroyedObjects, _carriedCreature.Species))
				{
					activeNotifications.Add(new Notification($"Impossible de reposer la créature ici !", new Color(255, 100, 100, 255), 1.5f));
					return;
				}

				_carriedCreature.WorldPos = targetPos;
				_carriedCreature.AnimState = "idle";
				entities.Add(_carriedCreature);
				activeNotifications.Add(new Notification($"Créature reposée", new Color(0, 255, 200, 255), 1.5f));
				_carriedCreature = null;
				return;
			}

			// Cas 1 : On porte un meuble
			if (_carriedFurniture != null)
			{
				if (!NetworkManager.IsClient && World.GetObjectIdAt(tileX, tileY) != 0)
				{
					activeNotifications.Add(new Notification($"Impossible de placer ici !", new Color(255, 100, 100, 255), 1f));
					return;
				}

				var carriedTileData = WorldTileRegistry.GetTile(_carriedFurniture.PlaceableId);
				if (carriedTileData != null && !NetworkManager.IsClient)
				{
					int currentGroundId = World.GetGroundTileIdAt(tileX, tileY);
					var currentTileData = WorldTileRegistry.GetTile(currentGroundId);
					if (currentTileData == null || !currentTileData.Walkable)
					{
						activeNotifications.Add(new Notification($"Impossible de placer un meuble ici !", new Color(255, 100, 100, 255), 1.5f));
						return;
					}
				}

				World.AddPlacedObject(tileX, tileY, _carriedFurniture.PlaceableId);

				var tileDef = WorldTileRegistry.GetTile(_carriedFurniture.PlaceableId);
				if (tileDef != null && tileDef.Variation > 1)
				{
					var chunkForVar = World.GetChunkAt(tileX, tileY);
					if (chunkForVar != null)
					{
						int variationIndex = new Random((tileX * 73856093) ^ (tileY * 19349663) ^ WorldSeed).Next(tileDef.Variation);
						chunkForVar.Variations[(tileX, tileY)] = variationIndex;
					}
				}

				if (_carriedFurniture.PlaceableId == 8 || _carriedFurniture.PlaceableId == 84)
				{
					World.UpdateRoofsAt(tileX, tileY);
				}

				var targetChunk = World.GetChunkAt(tileX, tileY);
				if (_carriedFurniture.PottedFlowerItemId != null && targetChunk != null)
					World.SetTileMeta(tileX, tileY, "potted_flower", _carriedFurniture.PottedFlowerItemId);
				if (_carriedFurniture.ContainerData != null && targetChunk != null)
				{
					targetChunk.SetContainerAt(tileX, tileY, _carriedFurniture.ContainerData);
				}
				if (_carriedFurniture.ArmorStandData != null && targetChunk != null)
				{
					targetChunk.SetArmorStandAt(tileX, tileY, _carriedFurniture.ArmorStandData);
				}

				_carriedFurniture = null;
				return;
			}
			
			// Cas 2 : Item normal
			if (item == null) return;
			
			var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == item.Name);
			if (itemData.ID == 0 || itemData.Type != ItemType.Placeable) return;
			itemData.PlaceableId = ResolvePlaceableId(item, itemData);
			
			// Sol
			if (itemData.IsGroundTile)
			{
				if (!NetworkManager.IsClient)
				{
					int currentGroundId = World.GetGroundTileIdAt(tileX, tileY);
					var currentTileData = WorldTileRegistry.GetTile(currentGroundId);
					
					if (currentTileData != null && !currentTileData.Diggable)
					{
						activeNotifications.Add(new Notification($"Impossible de modifier ce terrain !", new Color(255, 100, 100, 255), 1.5f));
						return;
					}
				}
				
				World.SetGroundTile(tileX, tileY, itemData.PlaceableId);
				RemoveItemFromInventory(item.Name, 1);
				activeNotifications.Add(new Notification($"Sol modifié : {item.Name}", new Color(0, 255, 200, 255), 1.5f));
				return;
			}
			
			// Radeau (PlaceableId == 500)
			if (itemData.PlaceableId == 500)
			{
				if (World.AddBoatTile(tileX, tileY, out var boat))
				{
					RemoveItemFromInventory(item.Name, 1);
					string msg = boat != null ? "Radeau agrandi !" : "Radeau posé sur l'eau";
					activeNotifications.Add(new Notification(msg, new Color(0, 255, 200, 255), 1.5f));
				}
				else
				{
					activeNotifications.Add(new Notification("Impossible de poser le radeau ici", new Color(255, 100, 100, 255), 1f));
				}
				return;
			}

			if (itemData.PlaceableId == 124)
			{
				int existingObjectId = World.GetObjectIdAt(tileX, tileY);
				if (!World.IsRailwayAt(tileX, tileY) || (existingObjectId != 0 && existingObjectId != 123))
				{
					activeNotifications.Add(new Notification("Le wagon doit être posé sur un rail libre.", new Color(255, 100, 100, 255), 1.5f));
					return;
				}

				World.AddPlacedObject(tileX, tileY, 124);
				RemoveItemFromInventory(item.Name, 1);
				return;
			}

			if (itemData.PlaceableId == 123)
			{
				var ground = WorldTileRegistry.GetTile(World.GetGroundTileIdAt(tileX, tileY));
				if (World.GetOverlayAt(tileX, tileY) != 0 || World.GetObjectIdAt(tileX, tileY) != 0 || ground?.Walkable != true)
				{
					activeNotifications.Add(new Notification("Impossible de poser un rail ici.", new Color(255, 100, 100, 255), 1.5f));
					return;
				}

				World.AddOverlayNetworked(tileX, tileY, 123);
				RemoveItemFromInventory(item.Name, 1);
				return;
			}
			
			if (itemData.PlaceableId == 215) // ou 601 selon votre items.json
			{
				int height = World.GetHeightAt(tileX, tileY);
				float yOffset = -height * ts / 4;
				Vector2 carPos = new Vector2(tileX * ts + ts / 2f, tileY * ts + yOffset + ts / 2f);
				var car = new Car(carPos);
				Program.Cars.Add(car);
				RemoveItemFromInventory(item.Name, 1);
				activeNotifications.Add(new Notification(" Voiture posée !", new Color(0, 255, 255, 255), 1.5f));
				Console.WriteLine($"Voiture ajoutée à la position ({carPos.X}, {carPos.Y})");
				return;
			}
			
			// Vérifier si la tuile est libre
			if (!NetworkManager.IsClient && World.GetObjectIdAt(tileX, tileY) != 0)
			{
				activeNotifications.Add(new Notification($"Impossible de placer ici !", new Color(255, 100, 100, 255), 1f));
				return;
			}
			
			// Nettoyer les destroyed objects si nécessaire
			string key = $"{tileX}_{tileY}";
			if (Program.destroyedObjects.Contains(key))
				Program.destroyedObjects.Remove(key);
			
			// Placer l'objet
			World.AddPlacedObject(tileX, tileY, itemData.PlaceableId);
			
			// Gérer les variations
			var tileDefPlaced = WorldTileRegistry.GetTile(itemData.PlaceableId);
			if (tileDefPlaced != null && tileDefPlaced.Variation > 1)
			{
				var chunk = World.GetChunkAt(tileX, tileY);
				if (chunk != null)
				{
					int variationIndex = new Random((tileX * 73856093) ^ (tileY * 19349663) ^ WorldSeed).Next(tileDefPlaced.Variation);
					chunk.Variations[(tileX, tileY)] = variationIndex;
				}
			}
			
			// Mise à jour des toits si on place un mur ou une porte
			if (itemData.PlaceableId == 8 || itemData.PlaceableId == 84)
			{
				World.UpdateRoofsAt(tileX, tileY);
				World.RefreshAllHouses();
			}
			
			// Initialisation des conteneurs
			if (tileDefPlaced != null && tileDefPlaced.IsContainer == true)
			{
				var chunk = World.GetChunkAt(tileX, tileY);
				if (chunk != null)
				{
					var containerData = chunk.GetContainerAt(tileX, tileY);
					if (containerData == null)
					{
						containerData = new ContainerInventoryData(
							tileDefPlaced.ContainerSlots ?? 20,
							tileDefPlaced.ContainerColumns > 0 ? tileDefPlaced.ContainerColumns : 8
						);
						chunk.SetContainerAt(tileX, tileY, containerData);
					}
				}
			}
			
			// Initialisation des porte-armures
			if (tileDefPlaced != null && tileDefPlaced.IsArmorStand == true)
			{
				var chunk = World.GetChunkAt(tileX, tileY);
				if (chunk != null)
				{
					var standData = chunk.GetArmorStandAt(tileX, tileY);
					if (standData == null)
					{
						standData = new ArmorStandData();
						chunk.SetArmorStandAt(tileX, tileY, standData);
					}
				}
			}
			
			// Retirer l'item de l'inventaire
			RemoveItemFromInventory(item.Name, 1);
		}

		public static int AddItemToInventory(Item item, int count, bool silent = false)
        {
            if (item == null) return count;
			item.Metadata = ItemRenderer.NormalizePotionMetadata(item.Metadata);
            int remaining = count;
            int maxStack = GameData.ItemDatabase[GetItemId(item.Name)].StackSize;

            // ========== 1. FUSION DANS LA CEINTURE ==========
            foreach (var slot in InventoryRenderer.InventorySlots)
            {
                if (remaining <= 0) break;
                if (!slot.IsEmpty && slot.Item != null && AreItemsStackable(slot.Item, item))
                {
                    int space = maxStack - slot.Count;
                    if (space > 0)
                    {
                        int add = Math.Min(space, remaining);
                        MergeSpoilMeta(slot.Item, item, add);
                        slot.Count += add;
                        remaining -= add;
                    }
                }
            }

            // ========== 2. FUSION DANS LE SAC À DOS ÉQUIPÉ ==========
            if (remaining > 0 && equipment.BackpackContainer != null)
            {
                foreach (var slot in equipment.BackpackContainer.Inventory.Slots)
                {
                    if (remaining <= 0) break;
                    if (!slot.IsEmpty && slot.Item != null && AreItemsStackable(slot.Item, item))
                    {
                        int space = maxStack - slot.Count;
                        if (space > 0)
                        {
                            int add = Math.Min(space, remaining);
                            MergeSpoilMeta(slot.Item, item, add);
                            slot.Count += add;
                            remaining -= add;
                        }
                    }
                }
            }

			// ========== 3. FUSION DANS LES PANIER(S) ÉQUIPÉS (main gauche / main droite) ==========
			if (remaining > 0)
			{
				if (equipment.OffHandContainer != null && IsPortableContainer(equipment.OffHand))
				{
					foreach (var slot in equipment.OffHandContainer.Inventory.Slots)
					{
						if (remaining <= 0) break;
						if (!slot.IsEmpty && slot.Item != null && AreItemsStackable(slot.Item, item))
						{
							int space = maxStack - slot.Count;
							if (space > 0)
							{
								int add = Math.Min(space, remaining);
								MergeSpoilMeta(slot.Item, item, add);
								slot.Count += add;
								remaining -= add;
							}
						}
					}
				}

				if (remaining > 0 && equipment.MainHandContainer != null && IsPortableContainer(equipment.MainHand))
				{
					foreach (var slot in equipment.MainHandContainer.Inventory.Slots)
					{
						if (remaining <= 0) break;
						if (!slot.IsEmpty && slot.Item != null && AreItemsStackable(slot.Item, item))
						{
							int space = maxStack - slot.Count;
							if (space > 0)
							{
								int add = Math.Min(space, remaining);
								MergeSpoilMeta(slot.Item, item, add);
								slot.Count += add;
								remaining -= add;
							}
						}
					}
				}
			}

            // ========== 4. SLOTS VIDES DE LA CEINTURE (PRÉSERVER LES COULEURS) ==========
            while (remaining > 0)
            {
                bool added = false;
                foreach (var slot in InventoryRenderer.InventorySlots)
                {
                    if (slot.IsEmpty)
                    {
						var newItem = new Item(item.Name, 0, item.BaseColor, item.Icon);
                        if (item.CustomColors != null && item.CustomColors.Count > 0)
                        {
                            newItem.CustomColors = new List<Color?>(item.CustomColors);
                        }
                        newItem.Container = item.Container;
                        newItem.Backpack = item.Backpack;
                        newItem.Metadata = item.Metadata;
                        newItem.Meta = new Dictionary<string, string>(item.Meta, StringComparer.OrdinalIgnoreCase);
                        slot.Item = newItem;
                        slot.Count = Math.Min(remaining, maxStack);
                        remaining -= slot.Count;
                        added = true;
                        break;
                    }
                }
                if (!added) break;
            }

            // ========== 5. SLOTS VIDES DU SAC À DOS (PRÉSERVER LES COULEURS) ==========
            while (remaining > 0 && equipment.BackpackContainer != null)
            {
                bool added = false;
                foreach (var slot in equipment.BackpackContainer.Inventory.Slots)
                {
                    if (slot.IsEmpty)
                    {
						var newItem = new Item(item.Name, 0, item.BaseColor, item.Icon);
                        if (item.CustomColors != null && item.CustomColors.Count > 0)
                        {
                            newItem.CustomColors = new List<Color?>(item.CustomColors);
                        }
                        newItem.Container = item.Container;
                        newItem.Backpack = item.Backpack;
                        newItem.Metadata = item.Metadata;
                        newItem.Meta = new Dictionary<string, string>(item.Meta, StringComparer.OrdinalIgnoreCase);
                        slot.Item = newItem;
                        slot.Count = Math.Min(remaining, maxStack);
                        remaining -= slot.Count;
                        added = true;
                        break;
                    }
                }
                if (!added) break;
            }

            // ========== 6. SLOTS VIDES DU PANIER (PRÉSERVER LES COULEURS) ==========
			while (remaining > 0 && ((equipment.OffHandContainer != null && IsPortableContainer(equipment.OffHand)) || (equipment.MainHandContainer != null && IsPortableContainer(equipment.MainHand))))
			{
				bool added = false;
				if (equipment.OffHandContainer != null && IsPortableContainer(equipment.OffHand))
				{
					foreach (var slot in equipment.OffHandContainer.Inventory.Slots)
					{
						if (slot.IsEmpty)
						{
							var newItem = new Item(item.Name, 0, item.BaseColor, item.Icon);
							if (item.CustomColors != null && item.CustomColors.Count > 0)
							{
								newItem.CustomColors = new List<Color?>(item.CustomColors);
							}
							newItem.Container = item.Container;
							newItem.Backpack = item.Backpack;
							newItem.Metadata = item.Metadata;
							newItem.Meta = new Dictionary<string, string>(item.Meta, StringComparer.OrdinalIgnoreCase);
							slot.Item = newItem;
							slot.Count = Math.Min(remaining, maxStack);
							remaining -= slot.Count;
							added = true;
							break;
						}
					}
				}

				if (!added && equipment.MainHandContainer != null && IsPortableContainer(equipment.MainHand))
				{
					foreach (var slot in equipment.MainHandContainer.Inventory.Slots)
					{
						if (slot.IsEmpty)
						{
							var newItem = new Item(item.Name, 0, item.BaseColor, item.Icon);
							if (item.CustomColors != null && item.CustomColors.Count > 0)
							{
								newItem.CustomColors = new List<Color?>(item.CustomColors);
							}
							newItem.Container = item.Container;
							newItem.Backpack = item.Backpack;
							newItem.Metadata = item.Metadata;
							newItem.Meta = new Dictionary<string, string>(item.Meta, StringComparer.OrdinalIgnoreCase);
							slot.Item = newItem;
							slot.Count = Math.Min(remaining, maxStack);
							remaining -= slot.Count;
							added = true;
							break;
						}
					}
				}

				if (!added) break;
			}

            int addedCount = count - remaining;
            if (addedCount > 0)
            {
                MarkItemAsPreviouslyOwned(item.Name);
                if (CraftingUI.IsOpen)
                    CraftingUI.RefreshRecipeList();
                if (!silent)
                {
                    PlayItemPickupSound();
                    AddItemNotification(item, addedCount, item.DisplayColor);
                }
            }

            return remaining;
        }
    }
}
