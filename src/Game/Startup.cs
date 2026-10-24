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
		private static int _entityAiUpdateFrame;

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
#if DEBUG
			TryEnableDebugConsole();
#endif

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
				SpawnLevelUpParticles(GetPlayerVisualPosition(GetPlayerPosition()));
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
				if (_mainMenuFadePhase == MainMenuFadePhase.None && !debugPanelClickConsumed && !CharacterAnimationEditorUI.IsOpen && !_isSoulArrivalCinematic && !IsSoulMaterializing)
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
					UpdateSoulMaterialization(dt);
					UpdateSoulIntroBars(dt);
                    if (_mainMenuFadePhase != MainMenuFadePhase.None) UpdateMainMenuFade(dt);

                    if (_gameState == GameState.Loading) UpdateLoadingScreen(dt);
					else if (_gameState == GameState.MainMenu && !CharacterAnimationEditorUI.IsOpen && !_isSelectingSave && !_isMultiplayerMenu && !_isJoinCharacterCreation) UpdateMainMenu();
                    else if (_isMultiplayerMenu && !_isJoinCharacterCreation) UpdateMultiplayerMenu();
                    else if (_gameState == GameState.Options) UpdateOptionsMenu();
					else if (_isSoulArrivalCinematic) UpdateSoulArrivalCinematic(dt);
					else if (_gameState == GameState.Playing && _mainMenuFadePhase == MainMenuFadePhase.None && !_fadeOutOverlayActive && !debugPanelClickConsumed)
						UpdateGameplay(dt, ref _playerPos, ref facing, hitboxDim, ref craftSelected, ref camera);

					Weather.SetPaused(_gameState != GameState.Playing || _isSoulArrivalCinematic);
					AmbientAudio.SetPaused(_gameState != GameState.Playing || _isSoulArrivalCinematic);
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
				ProceduralNpcVoice.Unload();
				MusicPlayer.Shutdown(); // arrête le thread de rendu audio avant de fermer le périphérique
				Raylib.CloseAudioDevice();

				//  MULTIJOUEUR : se déconnecter proprement avant de quitter
				NetworkManager.Disconnect();
				
				try
				{
					SaveCurrentGameOnExit();
				}
				finally
				{
					try
					{
						SaveSystem.FlushChunkWrites();
					}
					catch (Exception ex)
					{
						Console.Error.WriteLine($"[Save] Échec lors de la vidange des sauvegardes de chunks : {ex}");
					}
				}
				SettingsManager.Save();
				TraderUI.Close();
				InventoryRenderer.CloseInventory();
				UnloadSoulArrivalSceneTextures();
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

    }
}
