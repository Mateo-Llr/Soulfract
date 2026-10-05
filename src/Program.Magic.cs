// Program.Magic.cs
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

		public static void AddSpellProjectile(SpellProjectile p) => _spellProjectiles.Add(p);

		public static List<SpellProjectile> GetSpellProjectiles() => _spellProjectiles;

		public static void SpawnMagicExplosionEffect(Vector2 position, Color tint, float scale = 1f)
		{
			if (MagicExplosionTextures.Count == 0)
				return;
			_particles.Add(new MagicExplosionEffect(position, tint, scale));
		}

		//  NUAGES DE SPORES DU bolet ────────────────────────────────────────────
		// pour le détail de leur utilisation (spawn / présence figée / despawn).
		public static List<Texture2D> PoisonCloudTextures = new();

		private static bool _poisonCloudTexturesLoaded = false;

		private static void EnsurePoisonCloudTexturesLoaded()
		{
			if (_poisonCloudTexturesLoaded) return;
			_poisonCloudTexturesLoaded = true;
			PoisonCloudTextures.Clear();
			for (int i = 1; i <= 5; i++)
			{
				var tex = TryLoad($"assets/extras/cloudpop_{i}.png");
				if (tex.Id != 0) PoisonCloudTextures.Add(tex);
			}
		}

		/// <summary>
		/// Attaque du bolet : au lieu d'un coup direct, relâche une petite salve de nuages
		/// de spores empoisonnées autour de lui. Les nuages eux-mêmes empoisonnent (voir
		/// PoisonCloudParticle) toute entité (dont le joueur) qui s'y trouve pendant leur
		/// phase "présente". targetConnectionId identifie le joueur visé par la source
		/// (-1 = joueur local), comme pour DamagePlayerTarget.
		/// </summary>
		public static void SpawnPoisonCloudBurst(Vector2 center, Entity? source, int targetConnectionId)
		{
			EnsurePoisonCloudTexturesLoaded();
			if (PoisonCloudTextures.Count == 0) return;

			//  WorldPos est ancré au sol (les pieds) : on relève le centre de la salve pour
			// que les nuages apparaissent autour du corps/torse du bolet plutôt qu'à ses
			// pieds. La créature étant petite (scale 0.6), un décalage modeste suffit.
			Vector2 burstCenter = new Vector2(center.X, center.Y - 16f);

			int cloudCount = Random.Shared.Next(8, 12); // "une petite dizaine"
			for (int i = 0; i < cloudCount; i++)
			{
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);
				float dist = 10f + (float)Random.Shared.NextDouble() * 34f;
				Vector2 offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
				Vector2 pos = burstCenter + offset; // Position finale du nuage

				float radius = 18f + (float)Random.Shared.NextDouble() * 8f;
				float lifetime = 2.5f + (float)Random.Shared.NextDouble() * 1.5f; // "quelques secondes"
				float startDelay = (float)Random.Shared.NextDouble() * 0.6f; // pop échelonné sur "quelques instants"

				_particles.Add(new PoisonCloudParticle(pos, lifetime, radius, startDelay, source, targetConnectionId, burstCenter));
			}
		}

		private static List<SpellProjectile> _spellProjectiles = new();

		public static List<Texture2D> MagicExplosionTextures = new();

		// Textures pour les effets de slash (frappe)
		public static List<Texture2D> SlashTextures = new();

		public static void SpawnSlashEffect(Vector2 position, Vector2 direction)
		{
			if (SlashTextures == null || SlashTextures.Count == 0) return;
			// Choisir une texture aléatoire parmi les disponibles
			GetParticleList().Add(new SlashEffect(position, direction));
		}

		// Sorts assignés aux clics
		private static SpellData _leftClickSpell = new SpellData
		{
			Element = SpellElement.Fire,
			Type = SpellType.Projectile,
			Power = PowerLevel.Medium
		};

		private static SpellData _rightClickSpell = new SpellData
		{
			Element = SpellElement.Fire,
			Type = SpellType.Projectile,
			Power = PowerLevel.Medium
		};

		// Variables de charge pour les deux clics
		private static bool _isChargingLeftClick = false;

		private static float _leftClickCharge = 0f;

		private static float _rightClickCharge = 0f;

		const float MAX_CHARGE_TIME = 1.5f;

		// temps max pour charge complète
		const float MIN_CHARGE_THRESHOLD = 0.2f;

		//  ÉCRAN DE CHARGEMENT AVEC FONDU ────────────────────────────────────
        // Le chargement d'une sauvegarde est étalé sur plusieurs frames (génération
        // des chunks morceau par morceau) pendant qu'un écran de chargement plein
        // écran, avec fondu d'entrée puis de sortie, masque le travail effectué.
		const float LOADING_FADE_DURATION = 0.8f;
		const float LOADING_CHARACTER_DURATION = 1.8f;
		const float LOADING_FADE_OUT_DELAY = 0.15f;

		// nb de chunks générés par frame pendant le chargement
        static LoadingPhase _loadingPhase = LoadingPhase.FadeIn;

		// 0 = écran de chargement invisible, 1 = totalement opaque
        static float _loadingSpinnerTime = 0f;

		// Une fois le chargement terminé, on attend brièvement sous l'écran opaque avant de
		// passer en Playing, puis on fait disparaître doucement l'écran par-dessus le monde.
        static bool _fadeOutOverlayActive = false;
		static float _loadingFadeOutDelay = 0f;

		private static Texture2D _defaultBossCursorTexture;

		public static SpellData GetLeftClickSpell() => _leftClickSpell;

		public static SpellData GetRightClickSpell() => _rightClickSpell;

		public static void SetLeftClickSpell(SpellData spell) => _leftClickSpell = spell;

		public static void SetRightClickSpell(SpellData spell) => _rightClickSpell = spell;

		// true => à reconstruire au prochain Draw

		// Recharge la liste des sauvegardes disponibles ET invalide le cache d'aperçus.
		// À utiliser à la place de "SaveSystem.GetSaves()" partout où la liste peut changer
		// (activation du menu, création/suppression d'un monde) — jamais à chaque frame.
		static void RefreshAvailableSaves()
		{
			_availableSaves = SaveSystem.GetSaves();
			_saveBackgroundStyles.Clear();
			_carouselCacheDirty = true;
		}

		static Texture2D GetCharacterSelectionBackground(string saveName)
		{
			if (_characterSelectionBackgrounds.Length == 0) return _characterSelectionBackground;
			int style = _saveBackgroundStyles.TryGetValue(saveName, out var savedStyle) ? savedStyle : 0;
			return _characterSelectionBackgrounds[Math.Clamp(style, 0, _characterSelectionBackgrounds.Length - 1)];
		}

		static void ChangeSaveBackgroundStyle(string saveName)
		{
			if (_characterSelectionBackgrounds.Length == 0 || string.IsNullOrWhiteSpace(saveName)) return;
			int currentStyle = _saveBackgroundStyles.TryGetValue(saveName, out var savedStyle) ? savedStyle : 0;
			int nextStyle = (currentStyle + 1) % _characterSelectionBackgrounds.Length;
			try
			{
				GameSaveData data = SaveSystem.LoadGame(saveName);
				data.CharacterSelectionBackgroundStyle = nextStyle;
				SaveSystem.SaveGame(saveName, data);
				_saveBackgroundStyles[saveName] = nextStyle;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Impossible de changer le fond de sélection : {ex.Message}");
			}
		}

		static void ExportSelectedSave(string saveName)
		{
			string? destinationPath = NativeFileDialog.SaveFile("Exporter la sauvegarde", saveName + ".soulfract");
			if (string.IsNullOrEmpty(destinationPath)) return;

			try
			{
				SaveSystem.ExportSave(saveName, destinationPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Impossible d’exporter la sauvegarde : {ex.Message}");
			}
		}

		static void ImportSaveFromFile()
		{
			string? sourcePath = NativeFileDialog.OpenFile("Charger une sauvegarde");
			if (string.IsNullOrEmpty(sourcePath)) return;

			try
			{
				string importedName = SaveSystem.ImportSave(sourcePath);
				RefreshAvailableSaves();
				_classicMenuSelectedCardIndex = _availableSaves.FindIndex(save => save.Name == importedName) + 1;
				if (_classicMenuSelectedCardIndex <= 0) _classicMenuSelectedCardIndex = 1;
				PlayButtonClickSound();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Impossible de charger la sauvegarde : {ex.Message}");
			}
		}

		static void DrawMenuFileButton(Rectangle rect, Texture2D icon, bool hovered)
		{
			UIManager.DrawButton(rect, hovered ? new Color(88, 124, 165, 255) : new Color(50, 72, 102, 255), hovered, true);
			if (icon.Id != 0 && icon.Width > 0 && icon.Height > 0)
			{
				float pad = rect.Width * 0.2f;
				Raylib.DrawTexturePro(icon, new Rectangle(0, 0, icon.Width, icon.Height),
					new Rectangle(rect.X + pad, rect.Y + pad, rect.Width - pad * 2, rect.Height - pad * 2), Vector2.Zero, 0f, Color.White);
			}
		}

		static List<ArrowProjectile> _arrows = new List<ArrowProjectile>();

		static List<ArrowProjectile> _bullets = new List<ArrowProjectile>();

		private static RangedWeaponReloadState GetOrCreateRangedWeaponState(int weaponId, int maxAmmoPerCharge)
		{
			if (!_rangedWeaponReloadStates.TryGetValue(weaponId, out var state))
			{
				state = new RangedWeaponReloadState
				{
					CurrentAmmo = Math.Max(1, maxAmmoPerCharge),
					ReloadAmmoId = 0,
					ReloadAmount = 0,
				};
				_rangedWeaponReloadStates[weaponId] = state;
			}
			return state;
		}

		private static void StartRangedReload(int weaponId, int ammoId, int ammoAmountToReload, int maxAmmoPerCharge)
		{
			if (ammoId <= 0 || ammoAmountToReload <= 0) return;
			if (!_rangedWeaponReloadStates.TryGetValue(weaponId, out var state))
			{
				state = new RangedWeaponReloadState
				{
					CurrentAmmo = Math.Max(1, maxAmmoPerCharge),
					ReloadAmmoId = 0,
					ReloadAmount = 0,
				};
			}

			state.IsReloading = true;
			state.ReloadTimer = RANGED_RELOAD_DURATION;
			state.ReloadAmmoId = ammoId;
			state.ReloadAmount = Math.Min(Math.Max(1, maxAmmoPerCharge), ammoAmountToReload);
			_rangedWeaponReloadStates[weaponId] = state;
		}

		static List<ThrowableProjectile> _throwables = new List<ThrowableProjectile>();

		static float _throwCharge = 0f;

		const float MAX_THROW_CHARGE = 1f;

		const float MIN_THROW_CHARGE = 0.2f;

		// temps max de charge en secondes
		const float MIN_LEFT_CLICK_CHARGE = 0.2f;

		// Pêche
		static FishingProjectile? _currentFishingProjectile = null;

		static float _fishingCharge = 0f;

		const float MAX_FISHING_CHARGE = 1f;

		const float MIN_FISHING_CHARGE = 0.2f;

		public static IReadOnlyList<LocalPlayer> GetLocalPlayers()
		{
			// Recalcule seulement si on a changé de frame depuis le dernier appel :
			// évite de refiltrer/retrier _localPlayers plusieurs fois par frame (Draw + Update...).
			if (_localPlayersCacheFrame != _currentFrame)
			{
				_localPlayersCache.Clear();
				for (int i = 0; i < _localPlayers.Count; i++)
					if (_localPlayers[i].Connected)
						_localPlayersCache.Add(_localPlayers[i]);
				_localPlayersCache.Sort((a, b) => a.Id.CompareTo(b.Id));
				_localPlayersCacheFrame = _currentFrame;
			}
			return _localPlayersCache;
		}

		//  Le seau (id 63) a désormais 10 charges de liquide, comme un petit réservoir : il
		// peut contenir de l'eau OU du lait (jamais les deux à la fois), avec un niveau qui
		// se vide/se remplit progressivement. Le format canonique est "type;doses=N"
		// (ex: "water;doses=7", "milk;doses=10"), comme pour les potions. Les anciens
		// formats "type:N" et "type=N" restent acceptés pour les sauvegardes existantes.
		public const int BUCKET_MAX_CHARGES = 10;

		public static void ParseBucketContent(string? metadata, out string bucketType, out int charges)
		{
			bucketType = "empty";
			charges = 0;

			string trimmed = (metadata ?? "").Trim();
			if (string.IsNullOrEmpty(trimmed) || trimmed.Equals("empty", StringComparison.OrdinalIgnoreCase))
				return;

			string[] metadataParts = trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			string typeAndAmount = metadataParts[0];
			string type = typeAndAmount;
			int separatorIndex = typeAndAmount.IndexOfAny(new[] { ':', '=' });
			if (separatorIndex > 0)
			{
				type = typeAndAmount[..separatorIndex].Trim();
				if (int.TryParse(typeAndAmount[(separatorIndex + 1)..].Trim(), out int legacyCharges))
					charges = legacyCharges;
			}
			else if (metadataParts.Length > 1)
			{
				string dosePart = metadataParts.FirstOrDefault(part => part.StartsWith("doses=", StringComparison.OrdinalIgnoreCase)) ?? "";
				if (dosePart.Length > 6 && int.TryParse(dosePart[6..], out int canonicalCharges))
					charges = canonicalCharges;
			}
			charges = Math.Clamp(charges, 0, BUCKET_MAX_CHARGES);

			bucketType = charges > 0 ? type : "empty";
		}

		public static string FormatBucketContent(string bucketType, int charges)
		{
			charges = Math.Clamp(charges, 0, BUCKET_MAX_CHARGES);
			if (charges <= 0 || string.IsNullOrEmpty(bucketType) || bucketType.Equals("empty", StringComparison.OrdinalIgnoreCase))
				return "empty";
			return $"{bucketType.ToLowerInvariant()};doses={charges}";
		}

		/// <summary>
		/// Téléporte le joueur à une tuile donnée, en gérant le changement de plan
		/// (surface / grotte) si nécessaire : charge le chunk cible, calcule une position
		/// de sol valide, et recentre immédiatement la caméra. Utilisé par PortalUI.
		/// </summary>
		public static void TeleportPlayerTo(int tileX, int tileY, bool toCave)
		{
			int ts = TileSize;

			if (World.IsUnderground != toCave)
				World.IsUnderground = toCave;

			int chunkX = tileX / World.CHUNK_SIZE;
			int chunkY = tileY / World.CHUNK_SIZE;
			if (tileX < 0) chunkX = (tileX - World.CHUNK_SIZE + 1) / World.CHUNK_SIZE;
			if (tileY < 0) chunkY = (tileY - World.CHUNK_SIZE + 1) / World.CHUNK_SIZE;
			World.LoadOrGenerateChunk(chunkX, chunkY, (float)Raylib.GetTime(), entities);

			// La tuile d'ancrage d'un objet multi-cases est son coin BAS-GAUCHE (voir
			// FindTileUnderMouse). Pour apparaître pile au CENTRE du portail (et non décalé
			// vers son coin), on récupère sa taille réelle dans le registre et on centre
			// l'arrivée sur toute l'emprise de l'objet plutôt que sur la seule tuile d'ancrage.
			var targetTileData = WorldTileRegistry.GetTile(World.GetObjectIdAt(tileX, tileY));
			int objWidth = targetTileData?.Size?.Width ?? 1;
			int objHeight = targetTileData?.Size?.Height ?? 1;

			int height = World.GetHeightAt(tileX, tileY);
			float yOffset = -height * ts / 4;
			float targetX = tileX * ts + (objWidth * ts) / 2f;
			float targetY = tileY * ts + yOffset + ts - (objHeight * ts) / 2f;
			Vector2 targetPos = new Vector2(targetX, targetY);

			SetPlayerPosition(targetPos);
			RequestImmediateCameraCenter();

			float currentTime = (float)Raylib.GetTime();
			World.UpdateActiveChunks(targetPos, currentTime, entities);
		}

		private static void PerformCaveZoneSwitch()
	{
		if (_caveTransitionHasSwitchedZone)
			return;
		_caveTransitionHasSwitchedZone = true;

		int ts = TileSize;
		if (_caveTransitionEnterUnderground)
		{
			var exitPos = World.GetCaveExitForEntry(_caveTransitionTileX, _caveTransitionTileY);
			World.IsUnderground = true;
			if (exitPos.x != -1)
			{
				int chunkX = exitPos.x / World.CHUNK_SIZE;
				int chunkY = exitPos.y / World.CHUNK_SIZE;
				if (exitPos.x < 0) chunkX = (exitPos.x - World.CHUNK_SIZE + 1) / World.CHUNK_SIZE;
				if (exitPos.y < 0) chunkY = (exitPos.y - World.CHUNK_SIZE + 1) / World.CHUNK_SIZE;
				World.LoadOrGenerateChunk(chunkX, chunkY, (float)Raylib.GetTime(), Program.GetEntities());

				Vector2 spawnPos = World.FindSpawnNearExit(exitPos.x, exitPos.y);
				if (float.IsNaN(spawnPos.X) || float.IsNaN(spawnPos.Y) || float.IsInfinity(spawnPos.X) || float.IsInfinity(spawnPos.Y))
				{
					spawnPos = new Vector2(_caveTransitionTileX * ts + ts / 2f, _caveTransitionTileY * ts + ts / 2f);
					Console.WriteLine($"Fallback : position de spawn invalide, utilisation de l'entrée ({_caveTransitionTileX},{_caveTransitionTileY})");
				}
				_playerPos = spawnPos;
			}
			else
			{
				Vector2 fallbackPos = new Vector2(_caveTransitionTileX * ts + ts / 2f, _caveTransitionTileY * ts + ts / 2f);
				_playerPos = fallbackPos;
				Console.WriteLine($" Entrée de grotte sans sortie enregistrée – fallback à ({_caveTransitionTileX}, {_caveTransitionTileY})");
			}
			_playerFacing = 1f;
			AchievementManager.Progress(AchievementType.EnterCave, 1);
		}
		else
		{
			var entryPos = World.GetSurfaceEntryForExit(_caveTransitionTileX, _caveTransitionTileY);
			World.IsUnderground = false;
			if (entryPos.x != -1)
			{
				int chunkX = entryPos.x / World.CHUNK_SIZE;
				int chunkY = entryPos.y / World.CHUNK_SIZE;
				if (entryPos.x < 0) chunkX = (entryPos.x - World.CHUNK_SIZE + 1) / World.CHUNK_SIZE;
				if (entryPos.y < 0) chunkY = (entryPos.y - World.CHUNK_SIZE + 1) / World.CHUNK_SIZE;
				World.LoadOrGenerateChunk(chunkX, chunkY, (float)Raylib.GetTime(), Program.GetEntities());

				Vector2 spawnPos = World.FindSpawnNearExit(entryPos.x, entryPos.y);
				if (float.IsNaN(spawnPos.X) || float.IsNaN(spawnPos.Y) || float.IsInfinity(spawnPos.X) || float.IsInfinity(spawnPos.Y))
				{
					spawnPos = new Vector2(_caveTransitionTileX * ts + ts / 2f, _caveTransitionTileY * ts + ts / 2f);
					Console.WriteLine($"Fallback : position de spawn invalide, utilisation de la sortie ({_caveTransitionTileX},{_caveTransitionTileY})");
				}
				_playerPos = spawnPos;
			}
			else
			{
				Vector2 fallbackPos = new Vector2(_caveTransitionTileX * ts + ts / 2f, _caveTransitionTileY * ts + ts / 2f);
				_playerPos = fallbackPos;
				Console.WriteLine($" Sortie de grotte sans entrée associée – fallback à ({_caveTransitionTileX}, {_caveTransitionTileY})");
			}
			_playerFacing = 1f;
		}

		_cameraNeedsImmediateCenter = true;
		float currentTime = (float)Raylib.GetTime();
		World.UpdateActiveChunks(_playerPos, currentTime, entities);
	}

		private static void StartCaveTransition(int tileX, int tileY, bool enterUnderground)
	{
		_caveTransitionPhase = CaveTransitionPhase.CloseBars;
		_caveTransitionTimer = 0f;
		_caveTransitionBarProgress = 0f;
		_caveTransitionTileX = tileX;
		_caveTransitionTileY = tileY;
		_caveTransitionEnterUnderground = enterUnderground;
		_caveTransitionHasSwitchedZone = false;
		_caveTransitionWasLeavingCave = !enterUnderground;

		if (enterUnderground)
		{
			AddNotification(new Notification(Localization.Get("cave.enter_message"), new Color(200, 200, 200, 255), 2f));
		}
		else
		{
			AddNotification(new Notification(Localization.Get("cave.exit_message"), new Color(200, 200, 200, 255), 2f));
		}
	}

		//  Traite jusqu'à maxChunks chunks en attente (génération/chargement). Renvoie true
        // quand il n'en reste plus aucun, à appeler chaque frame pendant le chargement.
        static bool StepLoadGameChunks(int maxChunks)
        {
            if (_pendingChunkLoads == null) return true;

            float currentTime = (float)Raylib.GetTime();
            for (int i = 0; i < maxChunks && _pendingChunkLoads.Count > 0; i++)
            {
                var (chunkX, chunkY) = _pendingChunkLoads.Dequeue();
                World.LoadOrGenerateChunk(chunkX, chunkY, currentTime, entities);
            }

            return _pendingChunkLoads.Count == 0;
        }

		//  Point d'entrée : lance le fondu vers l'écran de chargement. Le chargement
        // proprement dit ne démarre qu'une fois le fondu d'entrée terminé (voir
        // UpdateLoadingScreen), pour ne jamais geler une frame déjà visible à l'écran.
        static void StartLoadingGame(string saveName)
        {
            _pendingLoadSaveName = saveName;
            _loadingPhase = LoadingPhase.FadeIn;
            _loadingFadeAlpha = 0f;
            _loadingCharacterProgress = 0f;
			_loadingGameStarted = false;
			_worldRevealAlpha = 0f;
			_loadingMenuExitProgress = 0f;
			_loadingMenuCaptured = false;
            _loadingSpinnerTime = 0f;
            _loadingError = null;
			_loadingFadeOutDelay = 0f;
            _fadeOutOverlayActive = false;
            _mainMenuActivated = false;
            _gameState = GameState.Loading;
        }

        static void UpdateLoadingScreen(float dt)
        {
            _loadingSpinnerTime += dt;

            switch (_loadingPhase)
            {
                case LoadingPhase.FadeIn:
					if (!_loadingGameStarted)
					{
						try
						{
							BeginLoadGame(_pendingLoadSaveName);
							_loadingGameStarted = true;
							_animState = "idle";
							_animFrame = 0;
							_animProg = 0f;
						}
						catch (Exception ex)
						{
							Console.WriteLine($" Erreur au chargement de la sauvegarde : {ex.Message}");
							_loadingError = ex.Message;
							_loadingPhase = LoadingPhase.FadeOut;
							break;
						}
					}

					bool chunksFinished = StepLoadGameChunks(LOADING_CHUNKS_PER_FRAME);
					_loadingMenuExitProgress = Math.Clamp(_loadingMenuExitProgress + dt / 0.55f, 0f, 1f);
					if (_animState != "idle") _animState = "idle";
					int idleFrameCount = 8;
					if (SpeciesData.Skeletons.TryGetValue("human", out var humanSkeleton))
					{
						var firstPart = humanSkeleton.FirstOrDefault();
						if (firstPart != null && firstPart.Animations.TryGetValue("idle", out var idleFrames))
							idleFrameCount = Math.Max(1, idleFrames.Count);
					}
					_animProg += dt * 4f;
					while (_animProg >= 1f)
					{
						_animProg -= 1f;
						_animFrame = (_animFrame + 1) % idleFrameCount;
					}
					_loadingCharacterProgress += dt / LOADING_CHARACTER_DURATION;
					if (_loadingCharacterProgress >= 1f && chunksFinished)
                    {
                        _loadingCharacterProgress = 1f;
						_loadingFadeAlpha = 1f;
                        try
                        {
							FinishLoadGame();
							_gameState = GameState.Playing;
							_fadeOutOverlayActive = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($" Erreur au chargement de la sauvegarde : {ex.Message}");
                            _loadingError = ex.Message;
                            _loadingPhase = LoadingPhase.FadeOut;
                        }
                    }
                    break;

                case LoadingPhase.Working:
                    bool finished = _loadingError != null || StepLoadGameChunks(LOADING_CHUNKS_PER_FRAME);
                    if (finished)
                    {
						if (_loadingError == null)
						{
                            FinishLoadGame();
							_loadingFadeOutDelay = LOADING_FADE_OUT_DELAY;
							_loadingPhase = LoadingPhase.FadeOutDelay;
						}
						else
						{
							_gameState = GameState.MainMenu;
							_loadingPhase = LoadingPhase.FadeOut;
						}
                    }
                    break;

				case LoadingPhase.FadeOutDelay:
					// Laisser passer quelques frames couvertes avant le premier rendu du jeu.
					_loadingFadeOutDelay -= dt;
					if (_loadingFadeOutDelay <= 0f)
					{
						_gameState = GameState.Playing;
						_fadeOutOverlayActive = true;
					}
					break;

                case LoadingPhase.FadeOut:
                    // Utilisé uniquement en cas d'erreur de chargement (retour au menu) :
                    // on fait juste disparaître l'écran de chargement.
                    _loadingFadeAlpha -= dt / LOADING_FADE_DURATION;
                    if (_loadingFadeAlpha <= 0f)
                    {
                        _loadingFadeAlpha = 0f;
                        _loadingPhase = LoadingPhase.FadeIn;
                    }
                    break;
            }
        }


		// Fondu de sortie déclenché après un chargement réussi : le jeu est déjà en
        // GameState.Playing et s'affiche normalement ; on ne fait ici que réduire
        // l'opacité de l'écran de chargement qui le recouvre encore.
        static void UpdateLoadingFadeOutOverlay(float dt)
        {
            _loadingSpinnerTime += dt;
			_animState = "idle";
			int idleFrameCount = 8;
			if (SpeciesData.Skeletons.TryGetValue("human", out var humanSkeleton))
			{
				var firstPart = humanSkeleton.FirstOrDefault();
				if (firstPart != null && firstPart.Animations.TryGetValue("idle", out var idleFrames))
					idleFrameCount = Math.Max(1, idleFrames.Count);
			}
			_animProg += dt * 4f;
			while (_animProg >= 1f)
			{
				_animProg -= 1f;
				_animFrame = (_animFrame + 1) % idleFrameCount;
			}
			_worldRevealAlpha += dt / LOADING_FADE_DURATION;
			if (_worldRevealAlpha >= 1f)
            {
				_worldRevealAlpha = 1f;
                _fadeOutOverlayActive = false;
            }
        }

		// Dessine l'overlay de fondu de sortie par-dessus le jeu déjà rendu ce frame.
        static void DrawLoadingFadeOutOverlay()
        {
			// Le monde est révélé dans Draw(), via la texture transparente capturée pendant
			// le rendu 2D. Aucun voile noir n'est nécessaire ici.
        }

		//  Permet à une entité (ex : gobelin archer, voir Entity.RangedCombatBehavior) de tirer
		// une flèche hostile sur le joueur. Réutilise le même système visuel/physique que les
		// flèches du joueur (_arrows), avec IsHostile=true pour que la flèche ne touche que le
		// joueur (voir ArrowProjectile.CheckPlayerHitAlongPath).
		public static void SpawnHostileArrow(Vector2 originPos, Vector2 direction, float speed, float damage, Entity? targetEntity = null, Entity? sourceEntity = null, int targetConnectionId = -1)
		{
			if (direction.Length() < 0.01f) return;
			var arrow = new ArrowProjectile(originPos, Vector2.Normalize(direction), speed, damage)
			{
				IsHostile = true,
				TargetEntity = targetEntity,
				SourceEntity = sourceEntity,
				TargetConnectionId = targetConnectionId
			};
			_arrows.Add(arrow);
		}

		// afin de remplacer les Where/Select/Distinct/OrderBy (allocations + tris à chaque frame,
        // même quand rien n'a changé) par des boucles manuelles + Sort en place.
        private static readonly List<Entity> _tamedFollowersBuffer = new();

		private static void UpdateTamedAnimalTargets(Vector2 playerPos)
		{
			// Récupérer les animaux apprivoisés vivants en mode Follow (sans allocation LINQ)
			_tamedFollowersBuffer.Clear();
			for (int i = 0; i < entities.Count; i++)
			{
				var e = entities[i];
				//  CORRECTIF : le mode Attack doit aussi recevoir une position de formation. Sans ça,
				// un animal en Attack sans ennemi à portée suivait un AiTarget périmé (et ne
				// bougeait plus). Quand il a une cible, Entity.Update écrase AiTarget lui-même.
				if (e.IsAlive && e.IsTamed
					&& (e.TamedBehavior == TamedAnimalMode.Follow || e.TamedBehavior == TamedAnimalMode.Attack)
					&& string.Equals(e.OwnerName, _currentSaveName, StringComparison.Ordinal))
					_tamedFollowersBuffer.Add(e);
			}

			if (_tamedFollowersBuffer.Count == 0)
				return;

			// Vérifier si un ordre doit être réattribué :
			// - un animal sans ordre (FollowOrder == -1)
			// - ou le nombre d'ordres distincts ne correspond pas au nombre d'animaux (incohérence)
			_followOrderSeenBuffer.Clear();
			bool needsReorder = false;
			for (int i = 0; i < _tamedFollowersBuffer.Count; i++)
			{
				int order = _tamedFollowersBuffer[i].FollowOrder;
				if (order < 0 || !_followOrderSeenBuffer.Add(order))
				{
					needsReorder = true;
					break;
				}
			}

			if (needsReorder)
			{
				// Réinitialiser tous les ordres
				for (int i = 0; i < _tamedFollowersBuffer.Count; i++)
					_tamedFollowersBuffer[i].FollowOrder = -1;

				// Trier par distance au joueur pour définir l'ordre initial (tri en place, pas d'allocation)
				_tamedFollowersBuffer.Sort((a, b) =>
					Vector2.DistanceSquared(a.WorldPos, playerPos).CompareTo(Vector2.DistanceSquared(b.WorldPos, playerPos)));

				for (int i = 0; i < _tamedFollowersBuffer.Count; i++)
					_tamedFollowersBuffer[i].FollowOrder = i;
			}
			else
			{
				// Trier les animaux par leur ordre fixe (tri en place, pas d'allocation)
				_tamedFollowersBuffer.Sort((a, b) => a.FollowOrder.CompareTo(b.FollowOrder));
			}

			Entity previous = null;
			for (int i = 0; i < _tamedFollowersBuffer.Count; i++)
			{
				Entity current = _tamedFollowersBuffer[i];
				if (i == 0)
				{
					// Le premier suit le joueur
					current.AiTarget = playerPos;
				}
				else
				{
					// Les suivants suivent le précédent avec un décalage
					Vector2 followPos = previous.WorldPos;
					Vector2 dirToTarget = previous.AiTarget - previous.WorldPos;
					if (dirToTarget.LengthSquared() > 0.01f)
					{
						dirToTarget = Vector2.Normalize(dirToTarget);
						followPos -= dirToTarget * 55f; // décalage de 55 pixels vers l'arrière
					}
					current.AiTarget = followPos;
				}
				previous = current;
			}
		}

		//  Symétrique de UpdateTamedAnimalTargets ci-dessus, mais pour les animaux apprivoisés
		// donnés à un PNJ (quête TameAndBring : QuestManager.TryGiftTamedAnimal / Entity.AddPet).
		// Sans ça, ces animaux gardent IsTamed=true et TamedBehavior=Follow mais leur AiTarget
		// n'est jamais mis à jour (UpdateTamedAnimalTargets ne suit que OwnerName == joueur
		// courant), donc ils restent figés sur place. On les regroupe par PNJ propriétaire
		// (OwnerNpcId) au cas où plusieurs animaux appartiendraient au même PNJ.
		private static void UpdateNpcOwnedPetTargets()
		{
			foreach (var list in _npcPetGroupBuffer.Values) list.Clear();

			for (int i = 0; i < entities.Count; i++)
			{
				var e = entities[i];
				if (e.IsAlive && e.IsTamed && e.TamedBehavior == TamedAnimalMode.Follow && e.OwnerNpcId.HasValue)
				{
					if (!_npcPetGroupBuffer.TryGetValue(e.OwnerNpcId.Value, out var list))
					{
						list = new List<Entity>();
						_npcPetGroupBuffer[e.OwnerNpcId.Value] = list;
					}
					list.Add(e);
				}
			}

			foreach (var kvp in _npcPetGroupBuffer)
			{
				if (kvp.Value.Count == 0) continue;

				var owner = entities.FirstOrDefault(e => e.NetId == kvp.Key && e.IsAlive);
				if (owner == null) continue; // PNJ propriétaire mort ou chunk non chargé

				var pets = kvp.Value;
				pets.Sort((a, b) => a.FollowOrder.CompareTo(b.FollowOrder));

				Entity previous = owner;
				bool isFirst = true;
				foreach (var pet in pets)
				{
					if (isFirst)
					{
						pet.AiTarget = owner.WorldPos;
					}
					else
					{
						Vector2 followPos = previous.WorldPos;
						Vector2 dirToTarget = previous.AiTarget - previous.WorldPos;
						if (dirToTarget.LengthSquared() > 0.01f)
						{
							dirToTarget = Vector2.Normalize(dirToTarget);
							followPos -= dirToTarget * 55f;
						}
						pet.AiTarget = followPos;
					}
					previous = pet;
					isFirst = false;
				}
			}
		}

		static List<Vector2> ComputeShadowPolygon(Vector2 lightPos, Rectangle wall, float maxRadius)
		{
			// Calcul des deux extrémités du mur les plus éloignées de la source
			Vector2[] corners = new Vector2[]
			{
				new Vector2(wall.X, wall.Y),
				new Vector2(wall.X + wall.Width, wall.Y),
				new Vector2(wall.X + wall.Width, wall.Y + wall.Height),
				new Vector2(wall.X, wall.Y + wall.Height)
			};

			// Trouver les deux points qui "font face" à la lumière (avec le plus grand angle)
			// Approche simplifiée : on prend les deux coins les plus proches de la lumière
			var sorted = corners.OrderBy(c => Vector2.Distance(lightPos, c)).ToList();
			Vector2 leftCorner = sorted[0];
			Vector2 rightCorner = sorted[1];

			// Déterminer l'ordre des angles
			Vector2 dirLeft = leftCorner - lightPos;
			Vector2 dirRight = rightCorner - lightPos;
			float angleLeft = MathF.Atan2(dirLeft.Y, dirLeft.X);
			float angleRight = MathF.Atan2(dirRight.Y, dirRight.X);

			// Normaliser les angles
			if (angleLeft > angleRight) (angleLeft, angleRight) = (angleRight, angleLeft);

			// Projeter les points loin derrière le mur (à maxRadius)
			Vector2 farLeft = lightPos + dirLeft / dirLeft.Length() * maxRadius;
			Vector2 farRight = lightPos + dirRight / dirRight.Length() * maxRadius;

			// Le polygone d'ombre : leftCorner -> rightCorner -> farRight -> farLeft
			return new List<Vector2> { leftCorner, rightCorner, farRight, farLeft };
		}

		static void DrawMainMenu()
		{
			if (CharacterAnimationEditorUI.IsOpen)
			{
				CharacterAnimationEditorUI.Draw();
				return;
			}
			DrawClassicMainMenu();
			return;
			/*
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			Vector2 mousePos = Raylib.GetMousePosition();
			float dt = Raylib.GetFrameTime();

			// Panorama du monde généré (remplace l'ancien fond parallaxe UNIQUEMENT ici :
			// DrawScrollingMenuBackground reste utilisé partout ailleurs, notamment pour
			// les Options ouvertes depuis une partie déjà lancée).
			DrawMenuWorldPreview(sw, sh, dt);

			// Anim de transition accueil <-> carrousel (0 = accueil, 1 = carrousel)
			float animTarget = _mainMenuActivated ? 1f : 0f;
			_mainMenuAnim += Math.Sign(animTarget - _mainMenuAnim) * dt * 3.5f;
			if (Math.Abs(_mainMenuAnim - animTarget) < 0.02f) _mainMenuAnim = animTarget;
			_mainMenuAnim = Math.Clamp(_mainMenuAnim, 0f, 1f);
			float easeAnim = _mainMenuAnim * _mainMenuAnim * (3f - 2f * _mainMenuAnim); // smoothstep

			// ── Titre, centré en haut, assez gros. S'élève légèrement une fois activé. ──
			float titleRise = easeAnim * (sh * 0.06f);
			if (_menuTitle.Id != 0 && _menuTitle.Width > 0 && _menuTitle.Height > 0)
			{
				float titleScale = Math.Min((sw * 0.5f) / _menuTitle.Width, (sh * 0.22f) / _menuTitle.Height);
				int titleWidth = (int)(_menuTitle.Width * titleScale);
				int titleX = sw / 2 - titleWidth / 2;
				float titleBob = (float)Math.Sin(Raylib.GetTime() * 1.5f) * 4f;
				int titleY = (int)(sh * 0.08f - titleRise + titleBob);
				SafeDrawTexture(_menuTitle, new Vector2(titleX, titleY), 0, titleScale, Color.White);
			}
			else
			{
				string titleText = Localization.Get("menu.title");
				int fontSize = (int)(sh * 0.09f);
				int textWidth = FontManager.MeasureText(titleText, fontSize);
				int titleX = sw / 2 - textWidth / 2;
				int titleY = (int)(sh * 0.08f - titleRise);
				FontManager.DrawText(titleText, titleX, titleY, fontSize, new Color(230, 200, 100, 255));
			}

			// ── "Pressez n'importe quelle touche pour démarrer", en bas, rougeâtre. ──
			// Glisse et sort de l'écran par le bas lorsque le menu est activé.
			{
				string prompt = Localization.Get("menu.press_any_key");
				int fontSize = Math.Max(18, (int)(sh * 0.03f));
				int textWidth = FontManager.MeasureText(prompt, fontSize);
				int promptX = sw / 2 - textWidth / 2;
				int baseY = (int)(sh * 0.88f);
				float slideOut = easeAnim * (sh * 0.25f);
				int promptY = (int)(baseY + slideOut);
				byte alpha = (byte)(255 * (1f - easeAnim));
				if (alpha > 0)
				{
					float pulse = 0.65f + (float)(Math.Sin(Raylib.GetTime() * 2.2f) * 0.35f);
					Color promptColor = new Color((byte)210, (byte)(40 * pulse + 20), (byte)(40 * pulse + 20), alpha);
					FontManager.DrawText(prompt, promptX, promptY, fontSize, promptColor);
				}
			}

			// ── Engrenage des paramètres, en bas à gauche, à moitié hors écran. ──
			_mainMenuGearHovered = false;
			if (_mainMenuGearIcon.Id != 0 && _mainMenuGearIcon.Width > 0 && _mainMenuGearIcon.Height > 0)
			{
				// Forcer l'échelle de l'engrenage : 5x -> 15x (triplé)
				float gearScale = 15f;
				float gearW = _mainMenuGearIcon.Width * gearScale;
				float gearH = _mainMenuGearIcon.Height * gearScale;
				// placer l'engrenage à moitié hors-écran sur la gauche
				float gearX = -gearW * 0.5f;
				float gearY = sh - gearH * 0.5f;
				Rectangle gearRect = new Rectangle(gearX, gearY, gearW, gearH);
				_mainMenuGearHovered = Raylib.CheckCollisionPointRec(mousePos, gearRect);
				UpdateButtonHoverSound(_mainMenuGearHovered, ref _prevMainMenuGearHovered);
				float gearSpin = _mainMenuGearHovered ? (float)Raylib.GetTime() * 60f : (float)Raylib.GetTime() * 10f;
				Color gearTint = _mainMenuGearHovered ? Color.White : new Color(210, 210, 210, 255);
				Rectangle src = new Rectangle(0, 0, _mainMenuGearIcon.Width, _mainMenuGearIcon.Height);
				Rectangle dst = new Rectangle(gearX + gearW / 2f, gearY + gearH / 2f, gearW, gearH);
				Vector2 origin = new Vector2(gearW / 2f, gearH / 2f);
				Raylib.DrawTexturePro(_mainMenuGearIcon, src, dst, origin, gearSpin, gearTint);
				if (_mainMenuGearHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					PlayButtonClickSound();
					// Options ouvertes depuis le menu principal : le retour doit ramener au menu principal
					OpenOptions(GameState.MainMenu);
					return;
				}
			}

			// ── Boutons principaux du menu : rejoindre/créer en haut à droite,
			// et Jouer sous le joueur actuellement sélectionné. ──
			_mainMenuCenterHovered = false;
			_mainMenuPlayHovered = false;
			_mainMenuCreateHovered = false;

			bool joinClicked = false;
			bool createClicked = false;
			bool playClicked = false;

			int buttonFontSize = Math.Max(18, (int)(sh * 0.028f));
			string joinText = Localization.Get("menu.join_game");
			string createText = Localization.Get("menu.create_world");
			int joinTextWidth = FontManager.MeasureText(joinText, buttonFontSize);
			int createTextWidth = FontManager.MeasureText(createText, buttonFontSize);
			int buttonW = Math.Max(joinTextWidth, createTextWidth) + 40;
			int buttonH = Math.Max(38, (int)(sh * 0.065f));
			float buttonX = sw - 20f - buttonW;
			float joinY = 20f;
			float createY = joinY + buttonH + 12f;

			Rectangle joinRect = new Rectangle(buttonX, joinY, buttonW, buttonH);
			Rectangle createRect = new Rectangle(buttonX, createY, buttonW, buttonH);

			_mainMenuJoinHovered = Raylib.CheckCollisionPointRec(mousePos, joinRect);
			_mainMenuCreateHovered = Raylib.CheckCollisionPointRec(mousePos, createRect);
			UpdateButtonHoverSound(_mainMenuJoinHovered, ref _prevMainMenuJoinHovered);
			UpdateButtonHoverSound(_mainMenuCreateHovered, ref _prevMainMenuCreateHovered);

			Color joinBg = _mainMenuJoinHovered ? new Color(90, 120, 170, 220) : new Color(40, 45, 65, 200);
			Color createBg = _mainMenuCreateHovered ? new Color(90, 170, 110, 220) : new Color(45, 65, 35, 200);

			Raylib.DrawRectangleRounded(joinRect, 0.16f, 8, joinBg);
			Raylib.DrawRectangleRoundedLines(joinRect, 0.16f, 8, 2, new Color(180, 180, 190, 170));
			Raylib.DrawRectangleRounded(createRect, 0.16f, 8, createBg);
			Raylib.DrawRectangleRoundedLines(createRect, 0.16f, 8, 2, new Color(180, 180, 190, 170));

			int joinTextX = (int)(joinRect.X + (buttonW - joinTextWidth) / 2f);
			int joinTextY = (int)(joinY + (buttonH - buttonFontSize) / 2f);
			int createTextX = (int)(createRect.X + (buttonW - createTextWidth) / 2f);
			int createTextY = (int)(createY + (buttonH - buttonFontSize) / 2f);
			FontManager.DrawText(joinText, joinTextX, joinTextY, buttonFontSize, Color.White);
			FontManager.DrawText(createText, createTextX, createTextY, buttonFontSize, Color.White);

			if (Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				joinClicked = _mainMenuJoinHovered;
				createClicked = _mainMenuCreateHovered;
			}

			int selectedSaveIndex = -1;
			if (_availableSaves.Count > 0)
			{
				selectedSaveIndex = _worldCarouselIndex - 2;
				if (selectedSaveIndex < 0) selectedSaveIndex = 0;
				if (selectedSaveIndex >= _availableSaves.Count) selectedSaveIndex = _availableSaves.Count - 1;
			}

			if (_mainMenuActivated && selectedSaveIndex >= 0 && selectedSaveIndex < _availableSaves.Count)
			{
				UpdateMenuShowcaseCamera(selectedSaveIndex, Raylib.GetFrameTime());
			}

			if (selectedSaveIndex >= 0 && selectedSaveIndex < _availableSaves.Count)
			{
				var selectedEntity = GetSelectedShowcaseEntity(selectedSaveIndex);
				if (selectedEntity != null)
				{
					Vector2 entityScreen = Raylib.GetWorldToScreen2D(selectedEntity.WorldPos, _menuPreviewCamera);
					string worldName = _availableSaves[selectedSaveIndex].Name;
					int worldNameFontSize = Math.Max(18, (int)(sh * 0.028f));
					int nameWidth = FontManager.MeasureText(worldName, worldNameFontSize);
					float nameX = entityScreen.X - nameWidth / 2f;
					// Remonter davantage le label et le bouton Jouer pour qu'ils ne collent pas aux pieds
					float overlayRaise = Math.Max(24f, sh * 0.035f);
					float nameY = entityScreen.Y - sh * 0.08f - overlayRaise;
					nameX = Math.Clamp(nameX, 10f, sw - nameWidth - 10f);
					nameY = Math.Max(nameY, 10f);
					Color outlineColor = Color.Black;
					FontManager.DrawText(worldName, (int)(nameX - 1f), (int)nameY, worldNameFontSize, outlineColor);
					FontManager.DrawText(worldName, (int)(nameX + 1f), (int)nameY, worldNameFontSize, outlineColor);
					FontManager.DrawText(worldName, (int)nameX, (int)(nameY - 1f), worldNameFontSize, outlineColor);
					FontManager.DrawText(worldName, (int)nameX, (int)(nameY + 1f), worldNameFontSize, outlineColor);
					FontManager.DrawText(worldName, (int)nameX, (int)nameY, worldNameFontSize, Color.White);

					// Réduire légèrement la taille du bouton Jouer
					float playW = Math.Max(90f, sw * 0.10f);
					float playH = Math.Max(30f, sh * 0.045f);
					float playX = entityScreen.X - playW / 2f;
					// lever le bouton du même offset que le label
					float playY = entityScreen.Y + sh * 0.08f - overlayRaise;
					playX = Math.Clamp(playX, 20f, sw - playW - 20f);
					playY = Math.Clamp(playY, 20f, sh - playH - 20f);

					Rectangle playRect = new Rectangle(playX, playY, playW, playH);
					_mainMenuPlayHovered = Raylib.CheckCollisionPointRec(mousePos, playRect);
					UpdateButtonHoverSound(_mainMenuPlayHovered, ref _prevMainMenuPlayHovered);

					Color playBg = _mainMenuPlayHovered ? new Color(140, 90, 40, 220) : new Color(90, 60, 30, 200);
					Raylib.DrawRectangleRounded(playRect, 0.2f, 8, playBg);
					Raylib.DrawRectangleRoundedLines(playRect, 0.2f, 8, 2, new Color(220, 180, 120, 200));

					string playText = "Jouer";
					int playTextWidth = FontManager.MeasureText(playText, buttonFontSize);
					FontManager.DrawText(playText, (int)(playRect.X + (playW - playTextWidth) / 2f), (int)(playRect.Y + (playH - buttonFontSize) / 2f), buttonFontSize, Color.White);
					if (Raylib.IsMouseButtonPressed(MouseButton.Left) && _mainMenuPlayHovered)
					{
						playClicked = true;
					}

					float joinButtonSize = Math.Max(28f, playH);
					float joinButtonX = playRect.X + playRect.Width + 10f;
					float joinButtonY = playY + (playH - joinButtonSize) * 0.5f;
					if (joinButtonX + joinButtonSize > sw - 20f)
						joinButtonX = playRect.X - joinButtonSize - 10f;
					joinButtonX = Math.Clamp(joinButtonX, 20f, sw - joinButtonSize - 20f);
					joinButtonY = Math.Clamp(joinButtonY, 20f, sh - joinButtonSize - 20f);
					Rectangle joinRect = new Rectangle(joinButtonX, joinButtonY, joinButtonSize, joinButtonSize);
					bool joinHovered = Raylib.CheckCollisionPointRec(mousePos, joinRect);
					Color joinBg = joinHovered ? new Color(90, 120, 170, 220) : new Color(30, 48, 74, 200);
					Raylib.DrawRectangleRounded(joinRect, 0.25f, 8, joinBg);
					Raylib.DrawRectangleRoundedLines(joinRect, 0.25f, 8, 2, new Color(180, 180, 190, 170));
					if (_mainMenuJoinWorldIcon.Id != 0 && _mainMenuJoinWorldIcon.Width > 0 && _mainMenuJoinWorldIcon.Height > 0)
					{
						Rectangle joinSrc = new Rectangle(0, 0, _mainMenuJoinWorldIcon.Width, _mainMenuJoinWorldIcon.Height);
						Rectangle joinDst = new Rectangle(joinRect.X + 4f, joinRect.Y + 4f, joinRect.Width - 8f, joinRect.Height - 8f);
						Raylib.DrawTexturePro(_mainMenuJoinWorldIcon, joinSrc, joinDst, Vector2.Zero, 0f, Color.White);
					}
					else
					{
						FontManager.DrawText("Join", (int)(joinRect.X + 5), (int)(joinRect.Y + (joinRect.Height - 14) / 2f), 14, Color.White);
					}
					if (Raylib.IsMouseButtonPressed(MouseButton.Left) && joinHovered)
					{
						_worldCarouselIndex = 0;
						PlayButtonClickSound();
						ConfirmWorldCarouselSelection();
					}

					if (_worldDeleteIcon.Id != 0 && _worldDeleteIcon.Width > 0 && _worldDeleteIcon.Height > 0 && _worldPendingDeleteName == null)
					{
						float binSize = Math.Max(26f, buttonFontSize * 1.4f);
						// Positionner la poubelle directement à droite du bouton Jouer si possible,
						// sinon à gauche du bouton.
						float preferredBinX = playX + playW + 8f;
						float altBinX = playX - binSize - 8f;
						float binX = preferredBinX;
						if (binX > sw - binSize - 20f) binX = altBinX;
						binX = Math.Clamp(binX, 20f, sw - binSize - 20f);
						float binY = Math.Clamp(playY + (playH - binSize) / 2f, 20f, sh - binSize - 20f);
						Rectangle binRect = new Rectangle(binX, binY, binSize, binSize);
						bool binHovered = Raylib.CheckCollisionPointRec(mousePos, binRect);

						Color binBg = binHovered ? new Color(90, 40, 40, 220) : new Color(40, 25, 25, 180);
						Raylib.DrawRectangleRounded(binRect, 0.25f, 6, binBg);
						Raylib.DrawRectangleRoundedLines(binRect, 0.25f, 6, 1, new Color(150, 90, 90, 200));

						float pad = binSize * 0.18f;
						Rectangle binSrc = new Rectangle(0, 0, _worldDeleteIcon.Width, _worldDeleteIcon.Height);
						Rectangle binDst = new Rectangle(binRect.X + pad, binRect.Y + pad, binRect.Width - pad * 2, binRect.Height - pad * 2);
						Raylib.DrawTexturePro(_worldDeleteIcon, binSrc, binDst, Vector2.Zero, 0, Color.White);

						if (binHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
						{
							_worldPendingDeleteName = worldName;
						}
					}
				}
			}

			if (!_mainMenuActivated && (joinClicked || createClicked || playClicked))
			{
				_mainMenuActivated = true;
				RefreshAvailableSaves();
				int defaultIndex = Math.Min(2, Math.Max(0, _availableSaves.Count + 1));
				_worldCarouselIndex = defaultIndex;
				_worldCarouselAnimIndex = _worldCarouselIndex;
			}

			if (createClicked)
			{
				_worldCarouselIndex = 1;
				PlayButtonClickSound();
				ConfirmWorldCarouselSelection();
			}
			else if (joinClicked)
			{
				_worldCarouselIndex = 0;
				PlayButtonClickSound();
				ConfirmWorldCarouselSelection();
			}
			else if (playClicked)
			{
				_worldCarouselIndex = selectedSaveIndex + 2;
				PlayButtonClickSound();
				ConfirmWorldCarouselSelection();
			}

			// Popup de confirmation de suppression (par-dessus le carrousel).
			if (_worldPendingDeleteName != null)
			{
				DrawWorldDeleteConfirmPopup(sw, sh, mousePos);
			}

			// Idle : un clic n'importe où (hors engrenage) démarre aussi le carrousel.
			if (!_mainMenuActivated && _worldPendingDeleteName == null && Raylib.IsMouseButtonPressed(MouseButton.Left) && !_mainMenuGearHovered)
			{
				_mainMenuActivated = true;
				RefreshAvailableSaves();
				_worldCarouselIndex = Math.Min(2, Math.Max(0, _availableSaves.Count + 1));
			}
			*/
		}

		static void DrawClassicMainMenu()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			Vector2 mousePos = Raylib.GetMousePosition();
			float dt = Raylib.GetFrameTime();

			DrawClassicMenuBackground(sw, sh);

			// Anim de transition accueil <-> carrousel de sauvegardes (0 = accueil, 1 = carrousel),
			// glisse vers sa cible dans les deux sens à vitesse constante.
			float animTarget = _mainMenuActivated ? 1f : 0f;
			float animDelta = animTarget - _mainMenuAnim;
			if (Math.Abs(animDelta) > 0.0001f)
				_mainMenuAnim += Math.Sign(animDelta) * dt * MAIN_MENU_TRANSITION_SPEED;
			if (Math.Abs(animTarget - _mainMenuAnim) < 0.01f) _mainMenuAnim = animTarget;
			_mainMenuAnim = Math.Clamp(_mainMenuAnim, 0f, 1f);

			_mainMenuTitleFade = Math.Clamp(_mainMenuTitleFade + dt * 1.8f, 0f, 1f);
			if (_mainMenuAnim > 0f)
				_mainMenuTitleFade = 1f;

			if (_mainMenuAnim <= 0f)
			{
				DrawMainMenuIntro(sw, sh, _mainMenuTitleFade);
				return;
			}
			if (_mainMenuAnim >= 1f)
			{
				DrawClassicMenuCarousel(sw, sh, mousePos, interactive: true);
				return;
			}

			// Transition en cours : on rend l'accueil et le carrousel chacun dans leur propre
			// texture (l'un des deux ne doit pas réagir aux clics tant qu'il n'est pas "actif"),
			// puis on les fond l'un dans l'autre avec un léger glissement vertical.
			EnsureMenuTransitionTargets(sw, sh);
			float easeAnim = _mainMenuAnim * _mainMenuAnim * (3f - 2f * _mainMenuAnim); // smoothstep

			Raylib.BeginTextureMode(_menuTransitionIntroRT);
			Raylib.ClearBackground(new Color(0, 0, 0, 0));
			DrawMainMenuIntro(sw, sh);
			Raylib.EndTextureMode();

			Raylib.BeginTextureMode(_menuTransitionCarouselRT);
			Raylib.ClearBackground(new Color(0, 0, 0, 0));
			DrawClassicMenuCarousel(sw, sh, mousePos, interactive: false);
			Raylib.EndTextureMode();

			float slide = sh * 0.05f;
			DrawMenuTransitionLayer(_menuTransitionIntroRT, sw, sh, -easeAnim * slide, 1f - easeAnim);
			DrawMenuTransitionLayer(_menuTransitionCarouselRT, sw, sh, (1f - easeAnim) * slide, easeAnim);
		}

		// Crée (ou recrée si la résolution a changé) les deux textures de rendu utilisées
		// pour le fondu accueil <-> carrousel.
		static void EnsureMenuTransitionTargets(int sw, int sh)
		{
			if (_menuTransitionRTReady && _menuTransitionRTWidth == sw && _menuTransitionRTHeight == sh) return;
			if (_menuTransitionRTReady)
			{
				Raylib.UnloadRenderTexture(_menuTransitionIntroRT);
				Raylib.UnloadRenderTexture(_menuTransitionCarouselRT);
			}
			_menuTransitionIntroRT = Raylib.LoadRenderTexture(sw, sh);
			_menuTransitionCarouselRT = Raylib.LoadRenderTexture(sw, sh);
			_menuTransitionRTReady = true;
			_menuTransitionRTWidth = sw;
			_menuTransitionRTHeight = sh;
		}

		// Dessine une texture de transition à l'écran avec un décalage vertical et une opacité donnés.
		static void DrawMenuTransitionLayer(RenderTexture2D rt, int sw, int sh, float offsetY, float alpha)
		{
			if (alpha <= 0.001f) return;
			Rectangle src = new Rectangle(0, 0, rt.Texture.Width, -rt.Texture.Height); // RT verticalement inversée
			Rectangle dst = new Rectangle(0, offsetY, sw, sh);
			Color tint = new Color((byte)255, (byte)255, (byte)255, (byte)Math.Clamp(alpha * 255f, 0f, 255f));
			Raylib.DrawTexturePro(rt.Texture, src, dst, Vector2.Zero, 0f, tint);
		}

		// Carrousel des sauvegardes (menu "activé"). interactive=false pendant la
		// transition : on l'affiche sans traiter ses clics/survols (déjà gérés par
		// la version au premier plan une fois la transition terminée).
		static void DrawClassicMenuCarousel(int sw, int sh, Vector2 mousePos, bool interactive, bool drawCharacterPreviews = true, bool drawDetailPreview = true)
		{
			if (_carouselCacheDirty) RebuildCarouselPreviewCache();
			_classicMenuSelectedCardIndex = Math.Clamp(_classicMenuSelectedCardIndex, 0, _availableSaves.Count);
			_classicMenuSelectedSaveIndex = _classicMenuSelectedCardIndex - 1;

			int margin = Math.Max(24, sw / 28);
			int headerY = Math.Max(26, sh / 18);
			int titleSize = Math.Max(30, Math.Min(60, sh / 11));
			string title = Localization.Get("menu.title");
			DrawMainMenuTitle(new Vector2(margin, headerY), titleSize, title);
			FontManager.DrawText("SAUVEGARDES", margin, headerY + titleSize + 8, 15, new Color(135, 151, 180, 255));

			Rectangle changelogButton = new Rectangle(sw - margin - 54, headerY - 4, 50, 50);
			bool changelogHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, changelogButton);
			DrawMenuFileButton(changelogButton, _mainMenuChangelogIcon, changelogHovered);
			if (interactive && changelogHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				PlayButtonClickSound();
				_showChangelog = true;
				_changelogScroll = 0f;
				_changelogScrollTarget = 0f;
			}
			Rectangle animationEditorButton = new Rectangle(sw - margin - 176, headerY + 1, 108, 40);
			bool animationEditorHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, animationEditorButton);
			UIManager.DrawButton(animationEditorButton, new Color(39, 53, 72, 255), animationEditorHovered, true);
			const string animationEditorLabel = "ANIMATIONS";
			int animationLabelWidth = FontManager.MeasureText(animationEditorLabel, 13);
			FontManager.DrawText(animationEditorLabel,
				(int)(animationEditorButton.X + (animationEditorButton.Width - animationLabelWidth) / 2f),
				(int)(animationEditorButton.Y + (animationEditorButton.Height - 13) / 2f), 13,
				new Color(233, 218, 175, 255));
			if (interactive && animationEditorHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				PlayButtonClickSound();
				CharacterAnimationEditorUI.Open();
				return;
			}
			if (_showChangelog)
			{
				DrawChangelogPopup(sw, sh, mousePos, interactive);
				return;
			}

			if (_availableSaves.Count == 0)
			{
				_classicMenuSelectedCardIndex = 0;
				_classicMenuSelectedSaveIndex = -1;
				Rectangle centerPanel = new Rectangle(sw * 0.22f, sh * 0.2f, sw * 0.56f, sh * 0.5f);
				bool hovered = interactive && Raylib.CheckCollisionPointRec(mousePos, centerPanel);
				Color panelColor = hovered ? new Color(32, 51, 77, 255) : new Color(22, 34, 54, 255);
				Color borderColor = hovered ? new Color(245, 206, 107, 255) : new Color(102, 120, 150, 255);
				Raylib.DrawRectangleRounded(centerPanel, 0.08f, 18, panelColor);
				Raylib.DrawRectangleRoundedLines(centerPanel, 0.08f, 18, 2f, borderColor);

				float iconSize = Math.Min(centerPanel.Width * 0.28f, centerPanel.Height * 0.32f);
				Vector2 iconCenter = new Vector2(centerPanel.X + centerPanel.Width / 2f, centerPanel.Y + centerPanel.Height * 0.42f);
				Rectangle loadRect = new Rectangle(centerPanel.X + 18, centerPanel.Y + 18, 40, 40);
				bool loadHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, loadRect);
				DrawMenuFileButton(loadRect, _mainMenuLoadFileIcon, loadHovered);
				if (interactive && loadHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					PlayButtonClickSound();
					ImportSaveFromFile();
				}
				if (_mainMenuCreateWorldIcon.Id != 0 && _mainMenuCreateWorldIcon.Width > 0 && _mainMenuCreateWorldIcon.Height > 0)
				{
					Rectangle source = new Rectangle(0, 0, _mainMenuCreateWorldIcon.Width, _mainMenuCreateWorldIcon.Height);
					Rectangle destination = new Rectangle(iconCenter.X - iconSize / 2f, iconCenter.Y - iconSize / 2f, iconSize, iconSize);
					Raylib.DrawTexturePro(_mainMenuCreateWorldIcon, source, destination, Vector2.Zero, 0f, Color.White);
				}
				else
				{
					Raylib.DrawCircle((int)iconCenter.X, (int)iconCenter.Y, iconSize * 0.38f, new Color(245, 206, 107, 255));
				}

				float joinIconSize = Math.Min(42f, centerPanel.Width * 0.12f);
				Rectangle joinRect = new Rectangle(centerPanel.X + centerPanel.Width - 18f - joinIconSize, centerPanel.Y + centerPanel.Height - 18f - joinIconSize, joinIconSize, joinIconSize);
				bool joinHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, joinRect);
				DrawMenuFileButton(joinRect, _mainMenuJoinWorldIcon, joinHovered);
				if (interactive && joinHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					_worldCarouselIndex = 0;
					PlayButtonClickSound();
					ConfirmWorldCarouselSelection();
				}

				string label = Localization.Get("menu.create_world");
				int labelSize = Math.Max(26, Math.Min(42, (int)(centerPanel.Height * 0.12f)));
				int labelWidth = FontManager.MeasureText(label, labelSize);
				FontManager.DrawText(label, (int)(centerPanel.X + centerPanel.Width / 2f - labelWidth / 2f), (int)(centerPanel.Y + centerPanel.Height * 0.7f), labelSize, Color.White);

				string hint = Localization.Get("menu.create_world_title");
				if (string.IsNullOrWhiteSpace(hint) || hint == "menu.create_world_title") hint = "Créer un nouveau monde";
				int hintSize = Math.Max(14, Math.Min(20, (int)(centerPanel.Height * 0.06f)));
				int hintWidth = FontManager.MeasureText(hint, hintSize);
				FontManager.DrawText(hint, (int)(centerPanel.X + centerPanel.Width / 2f - hintWidth / 2f), (int)(centerPanel.Y + centerPanel.Height * 0.82f), hintSize, new Color(180, 188, 207, 255));

				if (interactive && hovered && Raylib.IsMouseButtonPressed(MouseButton.Left) && !loadHovered && !joinHovered)
				{
					_worldCarouselIndex = 1;
					PlayButtonClickSound();
					ConfirmWorldCarouselSelection();
				}

				return;
			}

			int gap = Math.Max(18, sw / 45);
			int leftWidth = (int)(sw * 0.52f);
			int rightX = leftWidth + gap;
			int rightWidth = sw - rightX;
			int panelTop = headerY + titleSize + 48;
			int panelBottom = sh - margin;
			int panelHeight = panelBottom - panelTop;
			Rectangle leftPanel = new Rectangle(0, panelTop, leftWidth, panelHeight);
			Rectangle rightPanel = new Rectangle(rightX, panelTop, rightWidth, panelHeight);
			Raylib.DrawRectangleRounded(leftPanel, 0.01f, 4, new Color(21, 27, 42, 255));
			Raylib.DrawRectangleRounded(rightPanel, 0.01f, 4, new Color(18, 23, 36, 255));

			int inner = 16;
			int gridX = (int)leftPanel.X + inner;
			int gridY = (int)leftPanel.Y + inner;
			int gridWidth = (int)leftPanel.Width - inner * 2;
			int gridHeight = (int)leftPanel.Height - inner * 2;
			int columns = Math.Min(4, Math.Max(1, gridWidth / 145));
			int cardGap = 10;
			int cardSlotWidth = (gridWidth - cardGap * (columns - 1)) / columns;
			int cardWidth = Math.Max(90, (int)(cardSlotWidth * 0.86f));
			int cardHeight = Math.Max(125, Math.Min(190, (int)(cardWidth * 1.12f)));
			int totalCards = _availableSaves.Count + 1;
			int rows = (int)Math.Ceiling(totalCards / (double)columns);
			float contentHeight = rows * cardHeight + Math.Max(0, rows - 1) * cardGap;
			float maxScroll = Math.Max(0f, contentHeight - gridHeight);
			Rectangle gridRect = new Rectangle(gridX, gridY, gridWidth, gridHeight);
			if (interactive && Raylib.CheckCollisionPointRec(mousePos, gridRect))
				_classicMenuSaveScroll = Math.Clamp(_classicMenuSaveScroll - Raylib.GetMouseWheelMove() * (cardHeight + cardGap) * 0.65f, 0f, maxScroll);
			_classicMenuSaveScroll = Math.Clamp(_classicMenuSaveScroll, 0f, maxScroll);

			Raylib.BeginScissorMode(gridX, gridY, gridWidth, gridHeight);
			for (int i = 0; i < totalCards; i++)
			{
				int column = i % columns;
				int row = i / columns;
				Rectangle card = new Rectangle(gridX + column * (cardSlotWidth + cardGap) + (cardSlotWidth - cardWidth) / 2f, gridY + row * (cardHeight + cardGap) - _classicMenuSaveScroll, cardWidth, cardHeight);
				bool hovered = interactive && Raylib.CheckCollisionPointRec(mousePos, card) && Raylib.CheckCollisionPointRec(mousePos, gridRect);
				bool selected = i == _classicMenuSelectedCardIndex;
				Color cardColor = selected ? new Color(65, 78, 112, 255) : hovered ? new Color(44, 56, 82, 255) : new Color(29, 36, 55, 255);
				if (i == 0)
				{
					// Aucun fond ici, même discret : juste l'icône, pour ne pas donner
					// l'impression d'une case.
				}
				else if (_availableSaves[i - 1].Name.Length > 0 && GetCharacterSelectionBackground(_availableSaves[i - 1].Name).Id != 0)
				{
					Texture2D cardBackground = GetCharacterSelectionBackground(_availableSaves[i - 1].Name);
					Rectangle backgroundSource = new Rectangle(0, 0, cardBackground.Width, cardBackground.Height);
					Raylib.DrawTexturePro(cardBackground, backgroundSource, card, Vector2.Zero, 0f, Color.White);
				}
				else
				{
					Raylib.DrawRectangleRounded(card, 0.05f, 6, cardColor);
				}
				if (hovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					if (i == 0)
					{
						Rectangle loadRect = new Rectangle(card.X + 8, card.Y + 8, Math.Min(36f, card.Width * 0.24f), Math.Min(36f, card.Width * 0.24f));
						if (Raylib.CheckCollisionPointRec(mousePos, loadRect))
						{
							PlayButtonClickSound();
							ImportSaveFromFile();
							continue;
						}
						float joinIconSize = Math.Min(36f, card.Width * 0.22f);
						Rectangle joinRect = new Rectangle(card.X + card.Width - 8f - joinIconSize, card.Y + 8f, joinIconSize, joinIconSize);
						if (Raylib.CheckCollisionPointRec(mousePos, joinRect))
						{
							_worldCarouselIndex = 0;
							PlayButtonClickSound();
							ConfirmWorldCarouselSelection();
							continue;
						}
					}
					double clickTime = Raylib.GetTime();
					bool isDoubleClick = i > 0
						&& _classicMenuLastClickedCardIndex == i
						&& clickTime - _classicMenuLastCardClickTime <= 0.35d;
					_classicMenuLastClickedCardIndex = i;
					_classicMenuLastCardClickTime = clickTime;
					_classicMenuSelectedCardIndex = i;
					_classicMenuSelectedSaveIndex = i - 1;
					_worldCarouselIndex = i == 0 ? 1 : i + 1;
					PlayButtonClickSound();
					if (i == 0)
						ConfirmWorldCarouselSelection();
					else if (isDoubleClick)
						ConfirmWorldCarouselSelection();
				}
			}
			Raylib.EndScissorMode();
			for (int i = 0; i < totalCards; i++)
			{
				int column = i % columns;
				int row = i / columns;
				Rectangle card = new Rectangle(gridX + column * (cardSlotWidth + cardGap) + (cardSlotWidth - cardWidth) / 2f, gridY + row * (cardHeight + cardGap) - _classicMenuSaveScroll, cardWidth, cardHeight);
				if (card.Y + card.Height < gridY || card.Y > gridY + gridHeight) continue;
				bool selected = i == _classicMenuSelectedCardIndex;

				// Le haut du portrait reste visible ; seule la bordure inferieure
				// de la case coupe le corps et les jambes.
				int portraitClipTop = 0;
				int portraitClipBottom = (int)(card.Y + card.Height);
				Raylib.BeginScissorMode((int)card.X, portraitClipTop, (int)card.Width, Math.Max(1, portraitClipBottom - portraitClipTop));
				if (i == 0)
				{
					// Pas de cadre/bordure décoratif ici : juste l'icône, en plus grand.
					if (_mainMenuCreateWorldIcon.Id != 0 && _mainMenuCreateWorldIcon.Width > 0 && _mainMenuCreateWorldIcon.Height > 0)
					{
						float iconSize = Math.Min(card.Width * 0.8f, card.Height * 0.72f);
						Rectangle source = new Rectangle(0, 0, _mainMenuCreateWorldIcon.Width, _mainMenuCreateWorldIcon.Height);
						Rectangle destination = new Rectangle(card.X + (card.Width - iconSize) / 2f, card.Y + card.Height * 0.5f - iconSize / 2f, iconSize, iconSize);
						Raylib.DrawTexturePro(_mainMenuCreateWorldIcon, source, destination, Vector2.Zero, 0f, Color.White);
					}
					Rectangle loadRect = new Rectangle(card.X + 8, card.Y + 8, Math.Min(36f, card.Width * 0.24f), Math.Min(36f, card.Width * 0.24f));
					bool loadHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, loadRect);
					DrawMenuFileButton(loadRect, _mainMenuLoadFileIcon, loadHovered);
					if (interactive && loadHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						PlayButtonClickSound();
						ImportSaveFromFile();
					}
					float joinIconSize = Math.Min(36f, card.Width * 0.22f);
					Rectangle joinRect = new Rectangle(card.X + card.Width - 8f - joinIconSize, card.Y + 8f, joinIconSize, joinIconSize);
					bool joinHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, joinRect);
					DrawMenuFileButton(joinRect, _mainMenuJoinWorldIcon, joinHovered);
					if (interactive && joinHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						_worldCarouselIndex = 0;
						PlayButtonClickSound();
						ConfirmWorldCarouselSelection();
					}
					Raylib.EndScissorMode();
					continue;
				}

				string previewSaveName = _availableSaves[i - 1].Name;
				float portraitScale = Math.Clamp(card.Width / 68f, 1.8f, 5.2f);
				float previewAreaTop = card.Y + card.Height * 0.04f;
				float previewGroundLine = card.Y + card.Height * 0.96f;
				bool drawPreviewOverForeground = false;
				if (drawCharacterPreviews)
				{
					if (_carouselPreviewCache.TryGetValue(previewSaveName, out var preview))
					{
						var previewBounds = GetClassicPreviewVerticalBounds(preview.Species, portraitScale);
						float humanScale = SpeciesData.GetSpeciesInfo("human")?.Scale ?? 1f;
						float speciesScale = SpeciesData.GetSpeciesInfo(preview.Species)?.Scale ?? 1f;
						drawPreviewOverForeground = previewBounds.bottom - previewBounds.top <= previewGroundLine - previewAreaTop
							|| speciesScale <= humanScale * 0.85f;
					}
					if (!drawPreviewOverForeground)
						DrawClassicSavePreview(previewSaveName, new Vector2(card.X + card.Width / 2f, card.Y + card.Height * 1.08f), portraitScale, card.Height * 0.8f, areaTop: previewAreaTop, groundLine: previewGroundLine);
				}
				if (_characterSelectionBox.Id != 0 && _characterSelectionBox.Width > 0 && _characterSelectionBox.Height > 0)
				{
					const float foregroundStart = 0.64f;
					Rectangle foregroundSource = new Rectangle(
						0,
						_characterSelectionBox.Height * foregroundStart,
						_characterSelectionBox.Width,
						_characterSelectionBox.Height * (1f - foregroundStart));
					Rectangle foregroundDestination = new Rectangle(
						card.X,
						card.Y + card.Height * foregroundStart,
						card.Width,
						card.Height * (1f - foregroundStart));
					Raylib.DrawTexturePro(_characterSelectionBox, foregroundSource, foregroundDestination, Vector2.Zero, 0f, selected ? new Color(255, 238, 185, 255) : Color.White);
				}
				if (drawCharacterPreviews && drawPreviewOverForeground)
					DrawClassicSavePreview(previewSaveName, new Vector2(card.X + card.Width / 2f, card.Y + card.Height * 1.08f), portraitScale, card.Height * 0.8f, areaTop: previewAreaTop, groundLine: previewGroundLine);
				Raylib.EndScissorMode();
			}
			if (maxScroll > 0)
			{
				float thumbHeight = Math.Max(24f, gridHeight * gridHeight / contentHeight);
				float thumbY = gridY + (_classicMenuSaveScroll / maxScroll) * (gridHeight - thumbHeight);
				Raylib.DrawRectangle((int)leftPanel.X + (int)leftPanel.Width - 7, (int)thumbY, 3, (int)thumbHeight, new Color(190, 166, 104, 220));
			}

			int buttonHeight = Math.Max(48, Math.Min(64, sh / 11));
			DrawClassicSaveDetails(rightPanel, _classicMenuSelectedSaveIndex, mousePos, interactive, buttonHeight, inner, drawDetailPreview);

			Rectangle playRect = new Rectangle(rightPanel.X + inner, rightPanel.Y + rightPanel.Height - inner - buttonHeight, rightPanel.Width - inner * 2, buttonHeight);
			_mainMenuPlayHovered = interactive && _classicMenuSelectedCardIndex > 0 && Raylib.CheckCollisionPointRec(mousePos, playRect);
			_mainMenuCreateHovered = false;
			_mainMenuJoinHovered = false;
			if (interactive) UpdateButtonHoverSound(_mainMenuPlayHovered, ref _prevMainMenuPlayHovered);
			if (_classicMenuSelectedCardIndex > 0)
			{
				UIManager.DrawButton(playRect, new Color(75, 165, 90, 255), _mainMenuPlayHovered, true);
				string playText = "Jouer";
				int playFontSize = Math.Max(16, (int)(buttonHeight * 0.34f));
				int playTextWidth = FontManager.MeasureText(playText, playFontSize);
				FontManager.DrawText(playText, (int)(playRect.X + (playRect.Width - playTextWidth) / 2f), (int)(playRect.Y + (playRect.Height - playFontSize) / 2f), playFontSize, Color.White);
			}

			if (interactive && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				if (_mainMenuPlayHovered)
				{
					_worldCarouselIndex = _classicMenuSelectedCardIndex == 0 ? 1 : _classicMenuSelectedCardIndex + 1;
					PlayButtonClickSound();
					ConfirmWorldCarouselSelection();
				}
			}

			if (interactive && _worldPendingDeleteName != null)
				DrawWorldDeleteConfirmPopup(sw, sh, mousePos);
		}

		static void EnsureChangelogLoaded()
		{
			if (_changelogLoaded) return;
			_changelogLoaded = true;
			if (!File.Exists("Data/changelog.md")) return;

			string month = string.Empty;
			string date = string.Empty;
			foreach (string rawLine in File.ReadLines("Data/changelog.md"))
			{
				string line = rawLine.Trim();
				if (line.StartsWith("## ", StringComparison.Ordinal) && !line.StartsWith("### ", StringComparison.Ordinal))
				{
					month = line[3..].Trim();
					continue;
				}
				if (line.StartsWith("### ", StringComparison.Ordinal))
				{
					date = line[4..].Trim();
					continue;
				}
				if (!line.StartsWith("- [", StringComparison.Ordinal)) continue;

				int closingBracket = line.IndexOf(']', 3);
				if (closingBracket < 0 || string.IsNullOrWhiteSpace(date)) continue;
				string type = line[3..closingBracket].Trim().ToUpperInvariant();
				string text = line[(closingBracket + 1)..].Trim();
				if (text.Length == 0) continue;
				_changelogItems.Add(new ChangelogItem { Month = month, Date = date, Type = type, Text = text });
			}
		}

		static List<string> WrapChangelogText(string text, int fontSize, int maxWidth)
		{
			var lines = new List<string>();
			string current = string.Empty;
			foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				string candidate = current.Length == 0 ? word : current + " " + word;
				if (current.Length > 0 && FontManager.MeasureText(candidate, fontSize) > maxWidth)
				{
					lines.Add(current);
					current = word;
				}
				else current = candidate;
			}
			if (current.Length > 0) lines.Add(current);
			return lines;
		}

		static Color GetChangelogTypeColor(string type) => type switch
		{
			"ADDED" => new Color(91, 211, 135, 255),
			"CHANGED" => new Color(245, 195, 82, 255),
			"FIXED" => new Color(105, 181, 245, 255),
			"REMOVED" => new Color(235, 112, 112, 255),
			"PERF" => new Color(193, 139, 238, 255),
			_ => new Color(190, 198, 215, 255)
		};

		static List<(string Type, int Count)> GetChangelogDateSummary(string month, string date)
		{
			string[] preferredOrder = { "ADDED", "CHANGED", "FIXED", "REMOVED", "PERF" };
			var counts = _changelogItems
				.Where(item => item.Month == month && item.Date == date)
				.GroupBy(item => item.Type)
				.ToDictionary(group => group.Key, group => group.Count());
			var summary = new List<(string Type, int Count)>();
			foreach (string type in preferredOrder)
				if (counts.TryGetValue(type, out int count)) summary.Add((type, count));
			foreach (var entry in counts.Where(entry => !preferredOrder.Contains(entry.Key)).OrderBy(entry => entry.Key))
				summary.Add((entry.Key, entry.Value));
			return summary;
		}

		static void DrawChangelogPopup(int sw, int sh, Vector2 mousePos, bool interactive)
		{
			EnsureChangelogLoaded();
			Raylib.DrawRectangle(0, 0, sw, sh, new Color(3, 7, 16, 170));

			float panelWidth = Math.Min(sw * 0.86f, 1080f);
			float panelHeight = Math.Min(sh * 0.82f, 760f);
			Rectangle panel = new Rectangle((sw - panelWidth) / 2f, (sh - panelHeight) / 2f, panelWidth, panelHeight);
			Raylib.DrawRectangleRounded(panel, 0.025f, 8, new Color(11, 12, 15, 252));
			Raylib.DrawRectangleRoundedLines(panel, 0.025f, 8, 2, new Color(78, 82, 91, 255));

			int padding = Math.Max(20, (int)(panel.Width * 0.035f));
			int titleSize = Math.Max(24, Math.Min(38, (int)(panel.Height * 0.065f)));
			string changelogTitle = "CHANGELOG";
			int titleWidth = FontManager.MeasureText(changelogTitle, titleSize);
			FontManager.DrawText(changelogTitle, (int)(panel.X + (panel.Width - titleWidth) / 2f), (int)panel.Y + padding, titleSize, Color.White);
			string changelogSubtitle = "Les dernières évolutions de Soulfract";
			int subtitleWidth = FontManager.MeasureText(changelogSubtitle, 15);
			FontManager.DrawText(changelogSubtitle, (int)(panel.X + (panel.Width - subtitleWidth) / 2f), (int)panel.Y + padding + titleSize + 4, 15, new Color(161, 165, 174, 255));

			Rectangle closeRect = new Rectangle(panel.X + panel.Width - padding - 36, panel.Y + padding - 4, 36, 36);
			bool closeHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, closeRect);
			UIManager.DrawButton(closeRect, closeHovered ? new Color(125, 61, 65, 255) : new Color(65, 49, 61, 255), closeHovered, true);
			FontManager.DrawText("X", (int)closeRect.X + 11, (int)closeRect.Y + 7, 18, Color.White);
			if (closeHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				_showChangelog = false;
				PlayButtonClickSound();
				return;
			}

			Rectangle content = new Rectangle(panel.X + padding, panel.Y + padding + titleSize + 35, panel.Width - padding * 2, panel.Height - padding * 2 - titleSize - 35);

			int dateSize = Math.Max(16, Math.Min(22, (int)(panel.Height * 0.035f)));
			int bodySize = Math.Max(13, Math.Min(18, (int)(panel.Height * 0.027f)));
			int lineHeight = bodySize + 7;
			float contentHeight = 0f;
			string previousDate = string.Empty;
			string previousMonth = string.Empty;
			for (int index = _changelogItems.Count - 1; index >= 0; index--)
			{
				ChangelogItem item = _changelogItems[index];
				if (item.Month != previousMonth) { contentHeight += dateSize + 10; previousMonth = item.Month; previousDate = string.Empty; }
				if (item.Date != previousDate) { contentHeight += dateSize + 8; previousDate = item.Date; }
				contentHeight += lineHeight * WrapChangelogText(item.Text, bodySize, (int)content.Width - 125).Count + 9;
			}
			_changelogScroll = Math.Clamp(_changelogScroll, 0f, Math.Max(0f, contentHeight - content.Height));
			float maxScroll = Math.Max(0f, contentHeight - content.Height);
			_changelogScrollTarget = Math.Clamp(_changelogScrollTarget, 0f, maxScroll);
			if (interactive && Raylib.CheckCollisionPointRec(mousePos, content))
				_changelogScrollTarget = Math.Clamp(_changelogScrollTarget - Raylib.GetMouseWheelMove() * 42f, 0f, maxScroll);
			if (contentHeight > content.Height)
			{
				float thumbHeight = Math.Max(28f, content.Height * content.Height / contentHeight);
				float thumbTravel = content.Height - thumbHeight;
				float thumbY = content.Y + (_changelogScroll / maxScroll) * thumbTravel;
				Rectangle scrollbarTrack = new Rectangle(panel.X + panel.Width - padding * 0.45f, content.Y, 10, content.Height);
				Rectangle scrollbarThumb = new Rectangle(scrollbarTrack.X, thumbY, scrollbarTrack.Width, thumbHeight);
				if (interactive && Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, scrollbarThumb))
				{
					_changelogScrollbarDragging = true;
					_changelogScrollbarDragOffset = mousePos.Y - thumbY;
				}
				if (Raylib.IsMouseButtonReleased(MouseButton.Left))
					_changelogScrollbarDragging = false;
				if (interactive && _changelogScrollbarDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
				{
					float draggedThumbY = Math.Clamp(mousePos.Y - _changelogScrollbarDragOffset, content.Y, content.Y + thumbTravel);
					_changelogScroll = ((draggedThumbY - content.Y) / Math.Max(1f, thumbTravel)) * maxScroll;
					_changelogScrollTarget = _changelogScroll;
				}
				else
				{
					float scrollSmoothing = 1f - MathF.Exp(-14f * Raylib.GetFrameTime());
					_changelogScroll += (_changelogScrollTarget - _changelogScroll) * scrollSmoothing;
				}
			}
			else
			{
				_changelogScrollbarDragging = false;
				_changelogScroll = 0f;
				_changelogScrollTarget = 0f;
			}

			Raylib.BeginScissorMode((int)content.X, (int)content.Y, (int)content.Width, (int)content.Height);
			float y = content.Y - _changelogScroll;
			previousDate = string.Empty;
			previousMonth = string.Empty;
			for (int index = _changelogItems.Count - 1; index >= 0; index--)
			{
				ChangelogItem item = _changelogItems[index];
				if (item.Month != previousMonth)
				{
					y += dateSize + 10;
					FontManager.DrawText(item.Month.ToUpperInvariant(), (int)content.X, (int)y - dateSize, dateSize, new Color(129, 177, 224, 255));
					previousMonth = item.Month;
					previousDate = string.Empty;
				}
				if (item.Date != previousDate)
				{
					y += dateSize + 8;
					int dateY = (int)y - dateSize;
					FontManager.DrawText(item.Date, (int)content.X, dateY, dateSize, Color.White);
					int summaryX = (int)content.X + FontManager.MeasureText(item.Date, dateSize) + 18;
					int summarySize = Math.Max(11, dateSize - 5);
					foreach ((string type, int count) in GetChangelogDateSummary(item.Month, item.Date))
					{
						string summaryText = $"{count} [{type}]";
						FontManager.DrawText(summaryText, summaryX, dateY + 3, summarySize, GetChangelogTypeColor(type));
						summaryX += FontManager.MeasureText(summaryText, summarySize) + 14;
					}
					previousDate = item.Date;
				}
				Color typeColor = GetChangelogTypeColor(item.Type);
				int badgeWidth = Math.Max(68, FontManager.MeasureText(item.Type, bodySize - 1) + 18);
				Rectangle badge = new Rectangle(content.X, y, badgeWidth, bodySize + 5);
				Raylib.DrawRectangleRounded(badge, 0.25f, 5, new Color(typeColor.R, typeColor.G, typeColor.B, (byte)45));
				FontManager.DrawText(item.Type, (int)badge.X + 9, (int)badge.Y + 2, bodySize - 1, typeColor);
				float textX = badge.X + badge.Width + 12;
				foreach (string wrappedLine in WrapChangelogText(item.Text, bodySize, (int)(content.Width - badge.Width - 12)))
				{
					FontManager.DrawText(wrappedLine, (int)textX, (int)y + 2, bodySize, new Color(218, 225, 237, 255));
					y += lineHeight;
				}
				y += 9;
			}
			Raylib.EndScissorMode();

			if (contentHeight > content.Height)
			{
				float thumbHeight = Math.Max(28f, content.Height * content.Height / contentHeight);
				float thumbY = content.Y + (_changelogScroll / (contentHeight - content.Height)) * (content.Height - thumbHeight);
				Raylib.DrawRectangle((int)(panel.X + panel.Width - padding * 0.45f), (int)content.Y, 10, (int)content.Height, new Color(29, 30, 34, 255));
				Raylib.DrawRectangleRounded(new Rectangle(panel.X + panel.Width - padding * 0.45f, thumbY, 10, thumbHeight), 0.4f, 6,
					_changelogScrollbarDragging ? new Color(224, 224, 228, 255) : new Color(154, 158, 168, 255));
			}
		}

		static void DrawClassicMenuBackground(int sw, int sh)
		{
			Raylib.ClearBackground(new Color(4, 12, 31, 255));
			if (_characterSelectionBackground.Id != 0 && _characterSelectionBackground.Width > 0 && _characterSelectionBackground.Height > 0)
			{
				DrawTiledMenuTexture(_characterSelectionBackground, new Rectangle(0, 0, sw, sh), Color.White);
			}

			// Degrade bleu profond, plus lumineux derriere les panneaux du menu.
			for (int y = 0; y < sh; y += 4)
			{
				float vertical = y / (float)Math.Max(1, sh);
				byte red = (byte)Math.Clamp(5 + (int)(vertical * 7f), 0, 255);
				byte green = (byte)Math.Clamp(18 + (int)(vertical * 18f), 0, 255);
				byte blue = (byte)Math.Clamp(48 + (int)(vertical * 55f), 0, 255);
				Raylib.DrawRectangle(0, y, sw, 4, new Color(red, green, blue, (byte)255));
			}

			Raylib.DrawCircleGradient((int)(sw * 0.53f), (int)(sh * 0.42f), sh * 0.62f,
				new Color(35, 107, 170, 75), new Color(8, 18, 50, 0));
			Raylib.DrawCircleGradient((int)(sw * 0.16f), (int)(sh * 0.78f), sh * 0.34f,
				new Color(15, 86, 150, 45), new Color(7, 18, 45, 0));

			// Petits fragments colores, disperses sans motif lineaire et animes
			// par une legere derive.
			float time = (float)Raylib.GetTime();
			for (int index = 0; index < 72; index++)
			{
				float baseX = MenuBackgroundHash(index * 2 + 11);
				float baseY = MenuBackgroundHash(index * 2 + 29);
				float driftX = MathF.Sin(time * 0.16f + index * 1.7f) * 5f;
				float driftY = MathF.Cos(time * 0.13f + index * 0.9f) * 4f;
				float size = index % 7 == 0 ? 3f : 2f;
				Color[] fragmentColors =
				{
					new Color(21, 164, 226, 145),
					new Color(238, 205, 51, 170),
					new Color(226, 63, 48, 150),
					new Color(57, 207, 112, 150)
				};
				Color fragmentColor = fragmentColors[index % fragmentColors.Length];
				Raylib.DrawRectangle((int)(baseX * sw + driftX), (int)(baseY * sh + driftY), (int)size, (int)size, fragmentColor);
			}
		}

		static float MenuBackgroundHash(int value)
		{
			float hashed = MathF.Sin(value * 12.9898f + 78.233f) * 43758.5453f;
			return hashed - MathF.Floor(hashed);
		}

		static void DrawTiledMenuTexture(Texture2D texture, Rectangle destination, Color tint)
		{
			int tileWidth = Math.Max(1, texture.Width);
			int tileHeight = Math.Max(1, texture.Height);
			for (int y = (int)destination.Y; y < destination.Y + destination.Height; y += tileHeight)
			{
				for (int x = (int)destination.X; x < destination.X + destination.Width; x += tileWidth)
				{
					int drawWidth = Math.Min(tileWidth, (int)destination.X + (int)destination.Width - x);
					int drawHeight = Math.Min(tileHeight, (int)destination.Y + (int)destination.Height - y);
					Rectangle source = new Rectangle(0, 0, drawWidth, drawHeight);
					Rectangle target = new Rectangle(x, y, drawWidth, drawHeight);
					Raylib.DrawTexturePro(texture, source, target, Vector2.Zero, 0f, tint);
				}
			}
		}

		static void DrawMainMenuIntro(int sw, int sh, float titleAlpha = 1f)
		{
			string title = Localization.Get("menu.title");
			int titleSize = Math.Max(140, Math.Min(340, (int)(sh * 0.5f)));
			DrawMainMenuTitle(new Vector2(sw / 2f, sh / 2f - titleSize / 2f), titleSize, title, true, titleAlpha);
			string prompt = Localization.Get("menu.press_any_key");
			int promptSize = Math.Max(16, Math.Min(24, sh / 32));
			int promptWidth = FontManager.MeasureText(prompt, promptSize);
			byte promptAlpha = (byte)Math.Clamp(titleAlpha * 255f, 0f, 255f);
			FontManager.DrawText(prompt, sw / 2 - promptWidth / 2, sh / 2 + titleSize / 2 + 30, promptSize, new Color((byte)180, (byte)188, (byte)207, promptAlpha));
		}

		static void DrawMainMenuTitle(Vector2 position, int maxFontSize, string fallback, bool centered = false, float alpha = 1f)
		{
			byte alphaByte = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
			if (_menuTitle.Id != 0 && _menuTitle.Width > 0 && _menuTitle.Height > 0)
			{
				float scale = Math.Min(10f, maxFontSize / (float)_menuTitle.Height);
				float width = _menuTitle.Width * scale;
				Vector2 drawPosition = centered ? new Vector2(position.X - width / 2f, position.Y) : position;
				SafeDrawTexture(_menuTitle, drawPosition, 0f, scale, new Color((byte)255, (byte)255, (byte)255, alphaByte));
				return;
			}

			int textWidth = FontManager.MeasureText(fallback, maxFontSize);
			int x = centered ? (int)position.X - textWidth / 2 : (int)position.X;
			FontManager.DrawText(fallback, x, (int)position.Y, maxFontSize, new Color((byte)238, (byte)211, (byte)148, alphaByte));
		}

		static readonly Dictionary<long, (float minX, float minY, float maxX, float maxY)> _classicPreviewTextureAlphaBoundsCache = new();
		static readonly Dictionary<(string species, float scale), (float top, float bottom)> _classicPreviewVerticalBoundsCache = new();

		static (float minX, float minY, float maxX, float maxY) GetClassicPreviewTextureAlphaBounds(Texture2D texture)
		{
			long textureId = texture.Id;
			if (_classicPreviewTextureAlphaBoundsCache.TryGetValue(textureId, out var cachedBounds))
				return cachedBounds;

			Image image = Raylib.LoadImageFromTexture(texture);
			int minX = image.Width;
			int minY = image.Height;
			int maxX = -1;
			int maxY = -1;
			for (int y = 0; y < image.Height; y++)
			{
				for (int x = 0; x < image.Width; x++)
				{
					if (Raylib.GetImageColor(image, x, y).A <= 8) continue;
					minX = Math.Min(minX, x);
					minY = Math.Min(minY, y);
					maxX = Math.Max(maxX, x);
					maxY = Math.Max(maxY, y);
				}
			}
			Raylib.UnloadImage(image);

			var bounds = maxX >= minX ? (minX, minY, maxX + 1f, maxY + 1f) : (0f, 0f, 0f, 0f);
			_classicPreviewTextureAlphaBoundsCache[textureId] = bounds;
			return bounds;
		}

		static float GetClassicPreviewVerticalOffset(string speciesName, float customScale, float positionY, float areaTop, float groundLine)
		{
			string speciesKey = speciesName.ToLowerInvariant();
			var speciesBounds = GetClassicPreviewVerticalBounds(speciesKey, customScale);
			float speciesHeight = speciesBounds.bottom - speciesBounds.top;
			float availableHeight = groundLine - areaTop;
			float humanScale = SpeciesData.GetSpeciesInfo("human")?.Scale ?? 1f;
			float speciesScale = SpeciesData.GetSpeciesInfo(speciesKey)?.Scale ?? 1f;
			if (speciesHeight <= availableHeight || speciesScale <= humanScale * 0.85f)
				return groundLine - positionY - speciesBounds.bottom;

			var humanBounds = GetClassicPreviewVerticalBounds("human", customScale);
			return humanBounds.top - speciesBounds.top;
		}

		static (float top, float bottom) GetClassicPreviewVerticalBounds(string speciesKey, float customScale)
		{
			var cacheKey = (speciesKey, customScale);
			if (_classicPreviewVerticalBoundsCache.TryGetValue(cacheKey, out var cachedBounds))
				return cachedBounds;
			if (!SpeciesData.Skeletons.TryGetValue(speciesKey, out var skeleton) || skeleton.Count == 0)
				return (0f, 0f);

			float renderScale = 2.8f * (SpeciesData.GetSpeciesInfo(speciesKey)?.Scale ?? 1f) * customScale;
			float top = float.PositiveInfinity;
			float bottom = float.NegativeInfinity;
			foreach (var part in skeleton)
			{
				if (part.Texture.Width <= 0 || part.Texture.Height <= 0) continue;
				var alphaBounds = GetClassicPreviewTextureAlphaBounds(part.Texture);
				if (alphaBounds.maxX <= alphaBounds.minX || alphaBounds.maxY <= alphaBounds.minY) continue;
				var (center, rotation) = EntityRenderer.GetGlobalPartTransform(
					speciesKey, "idle", 0, 0f, 1f, Vector2.Zero, SpeciesData.Skeletons, part.Name,
					customScale: customScale);
				float radians = rotation * MathF.PI / 180f;
				float sin = MathF.Sin(radians);
				float cos = MathF.Cos(radians);
				float left = (alphaBounds.minX - part.Texture.Width * 0.5f) * renderScale;
				float right = (alphaBounds.maxX - part.Texture.Width * 0.5f) * renderScale;
				float upper = (alphaBounds.minY - part.Texture.Height * 0.5f) * renderScale;
				float lower = (alphaBounds.maxY - part.Texture.Height * 0.5f) * renderScale;
				for (int xEdge = 0; xEdge < 2; xEdge++)
				{
					float localX = xEdge == 0 ? left : right;
					for (int yEdge = 0; yEdge < 2; yEdge++)
					{
						float localY = yEdge == 0 ? upper : lower;
						float worldY = center.Y + sin * localX + cos * localY;
						top = Math.Min(top, worldY);
						bottom = Math.Max(bottom, worldY);
					}
				}
			}

			var bounds = float.IsFinite(top) && float.IsFinite(bottom) ? (top, bottom) : (0f, 0f);
			_classicPreviewVerticalBoundsCache[cacheKey] = bounds;
			return bounds;
		}

		static void DrawClassicSavePreview(string saveName, Vector2 position, float scale, float height, float facing = 1f, int animFrame = 0, float animProg = 0f, float? areaTop = null, float? groundLine = null)
		{
			if (!_carouselPreviewCache.TryGetValue(saveName, out var preview)) return;
			if (areaTop.HasValue && groundLine.HasValue)
				position.Y += GetClassicPreviewVerticalOffset(preview.Species, scale, position.Y, areaTop.Value, groundLine.Value);
			// "position" est le point d'ancrage aux pieds du personnage (c'est ce que DrawEntity
			// attend) : la tête est nettement plus haut, donc on décale le point utilisé pour le
			// calcul du regard vers le haut, sinon le personnage a l'air de regarder trop bas.
			Vector2 headPos = position - new Vector2(0f, height * 0.85f);
			var gaze = ComputeGazeOffsetsFor(headPos, Raylib.GetMousePosition(), facing);
			EntityRenderer.DrawEntity(preview.Species, "idle", animFrame, animProg, facing, position,
				string.Equals(preview.Species, "human", StringComparison.OrdinalIgnoreCase) ? preview.Skin : preview.MorphTint,
				preview.HairBase, preview.HairOverlay, preview.HairColor, SpeciesData.Skeletons, eyes: EyesTexture, mouth: MouthTexture, customScale: scale, equipment: preview.Equipment, beardStyle: preview.BeardStyle, underwearTexture: LeafUnderpantsTexture,
				randomFeatureVariant: preview.FeatureVariants, randomFeatureColor: preview.FeatureColors,
				pupilLeftOffset: gaze.pupilLeft, pupilRightOffset: gaze.pupilRight, eyebrowLeftOffset: gaze.browLeft, eyebrowRightOffset: gaze.browRight);
		}

		// panel : zone complète du panneau de droite. reservedBottomHeight/innerPad : espace
		// laissé libre en bas pour le bouton "Jouer" (dessiné séparément par l'appelant).
		static void DrawClassicSaveDetails(Rectangle panel, int saveIndex, Vector2 mousePos, bool interactive, float reservedBottomHeight, int innerPad, bool drawCharacterPreviews = true)
		{
			int x = (int)panel.X + 24;
			int rightEdge = (int)(panel.X + panel.Width) - 24;
			float contentBottom = panel.Y + panel.Height - reservedBottomHeight - innerPad - 14;

			if (_classicMenuSelectedCardIndex == 0)
			{
				FontManager.DrawText(Localization.Get("menu.create_world"), x, (int)panel.Y + 30, 30, Color.White);
				FontManager.DrawText("Créez une nouvelle aventure", x, (int)panel.Y + 78, 18, new Color(190, 198, 214, 255));
				return;
			}
			if (saveIndex < 0 || saveIndex >= _availableSaves.Count)
			{
				FontManager.DrawText("Sélectionnez une sauvegarde", x, (int)panel.Y + 32, 21, new Color(166, 175, 198, 255));
				return;
			}
			SaveInfo save = _availableSaves[saveIndex];

			// ── En-tête : nom de la sauvegarde centré dans le panneau, bouton supprimer à droite. ──
			int headerFontSize = 28;
			int nameMaxWidth = (int)panel.Width - 48 - 80;
			string displayName = save.Name;
			while (FontManager.MeasureText(displayName, headerFontSize) > nameMaxWidth && displayName.Length > 1)
				displayName = displayName.Substring(0, displayName.Length - 1);
			if (displayName.Length < save.Name.Length) displayName = displayName.TrimEnd() + "…";
			int nameWidth = FontManager.MeasureText(displayName, headerFontSize);
			int nameX = (int)(panel.X + panel.Width / 2f - nameWidth / 2f);
			int nameY = (int)panel.Y + 26;
			Color outlineColor = new Color(0, 0, 0, 255);
			FontManager.DrawText(displayName, nameX - 1, nameY, headerFontSize, outlineColor);
			FontManager.DrawText(displayName, nameX + 1, nameY, headerFontSize, outlineColor);
			FontManager.DrawText(displayName, nameX, nameY - 1, headerFontSize, outlineColor);
			FontManager.DrawText(displayName, nameX, nameY + 1, headerFontSize, outlineColor);
			FontManager.DrawText(displayName, nameX, nameY, headerFontSize, Color.White);

			float deleteSize = 34f;
			Rectangle deleteRect = new Rectangle(rightEdge - deleteSize, panel.Y + 24, deleteSize, deleteSize);
			_mainMenuDeleteSaveHovered = interactive && _worldPendingDeleteName == null && Raylib.CheckCollisionPointRec(mousePos, deleteRect);
			if (interactive) UpdateButtonHoverSound(_mainMenuDeleteSaveHovered, ref _prevMainMenuDeleteSaveHovered);
			Color deleteBg = _mainMenuDeleteSaveHovered ? new Color(150, 55, 55, 235) : new Color(50, 32, 32, 200);
			Raylib.DrawRectangleRounded(deleteRect, 0.28f, 6, deleteBg);
			Raylib.DrawRectangleRoundedLines(deleteRect, 0.28f, 6, 1f, _mainMenuDeleteSaveHovered ? new Color(230, 120, 120, 230) : new Color(140, 95, 95, 180));
			if (_worldDeleteIcon.Id != 0 && _worldDeleteIcon.Width > 0 && _worldDeleteIcon.Height > 0)
			{
				float pad = deleteSize * 0.24f;
				Rectangle iconSrc = new Rectangle(0, 0, _worldDeleteIcon.Width, _worldDeleteIcon.Height);
				Rectangle iconDst = new Rectangle(deleteRect.X + pad, deleteRect.Y + pad, deleteRect.Width - pad * 2, deleteRect.Height - pad * 2);
				Raylib.DrawTexturePro(_worldDeleteIcon, iconSrc, iconDst, Vector2.Zero, 0f, Color.White);
			}
			else
			{
				// Repli si l'icône n'est pas chargée : une simple croix.
				Color crossColor = _mainMenuDeleteSaveHovered ? Color.White : new Color(210, 170, 170, 220);
				float pad = deleteSize * 0.28f;
				Raylib.DrawLineEx(new Vector2(deleteRect.X + pad, deleteRect.Y + pad), new Vector2(deleteRect.X + deleteRect.Width - pad, deleteRect.Y + deleteRect.Height - pad), 2.5f, crossColor);
				Raylib.DrawLineEx(new Vector2(deleteRect.X + deleteRect.Width - pad, deleteRect.Y + pad), new Vector2(deleteRect.X + pad, deleteRect.Y + deleteRect.Height - pad), 2.5f, crossColor);
			}
			if (interactive && _mainMenuDeleteSaveHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				PlayButtonClickSound();
				_worldPendingDeleteName = save.Name;
			}

			float styleSize = 34f;
			Rectangle styleRect = new Rectangle(panel.X + 24, panel.Y + 24, styleSize, styleSize);
			bool styleHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, styleRect);
			if (interactive) UpdateButtonHoverSound(styleHovered, ref _mainMenuStyleButtonHovered);
			UIManager.DrawButton(styleRect, new Color(75, 105, 145, 255), styleHovered, interactive);
			int currentStyle = _saveBackgroundStyles.TryGetValue(save.Name, out var savedStyle) ? savedStyle : 0;
			string styleLabel = (currentStyle + 1).ToString();
			int styleFontSize = 18;
			int styleTextWidth = FontManager.MeasureText(styleLabel, styleFontSize);
			FontManager.DrawText(styleLabel, (int)(styleRect.X + (styleRect.Width - styleTextWidth) / 2f),
				(int)(styleRect.Y + (styleRect.Height - styleFontSize) / 2f), styleFontSize, Color.White);
			if (styleHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				ChangeSaveBackgroundStyle(save.Name);
				PlayButtonClickSound();
			}

			Rectangle saveFileRect = new Rectangle(styleRect.X + styleSize + 8, styleRect.Y, styleSize, styleSize);
			bool saveFileHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, saveFileRect);
			DrawMenuFileButton(saveFileRect, _mainMenuSaveFileIcon, saveFileHovered);
			if (interactive && saveFileHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				PlayButtonClickSound();
				ExportSelectedSave(save.Name);
			}

			// ── Bloc d'informations, en bas du panneau, juste au-dessus du bouton Jouer. ──
			int infoRowHeight = 30;
			int infoRows = 3;
			int infoPaddingY = 16;
			float infoHeight = infoRows * infoRowHeight + infoPaddingY * 2 + 10;
			Rectangle infoPanel = new Rectangle(panel.X + innerPad, contentBottom - infoHeight, panel.Width - innerPad * 2, infoHeight);
			Raylib.DrawRectangleRounded(infoPanel, 0.06f, 8, new Color(14, 18, 29, 220));
			Raylib.DrawRectangleRoundedLines(infoPanel, 0.06f, 8, 1f, new Color(60, 72, 100, 200));
			int infoLabelX = (int)infoPanel.X + 18;
			int infoValueRightX = (int)(infoPanel.X + infoPanel.Width) - 18;
			int infoTitleY = (int)infoPanel.Y + 10;
			FontManager.DrawText("INFORMATIONS DE LA PARTIE", infoLabelX, infoTitleY, 13, new Color(190, 164, 103, 255));

			(string label, string value)[] rows =
			{
				("Dernière partie", save.LastPlayed.ToString("dd/MM/yyyy HH:mm")),
				("Temps joué", TimeSpan.FromSeconds(save.PlayTime).ToString(@"hh\:mm\:ss")),
				("Position", $"{save.PlayerX:0}, {save.PlayerY:0}"),
			};
			float rowStartY = infoTitleY + 26;
			for (int i = 0; i < rows.Length; i++)
			{
				float rowY = rowStartY + i * infoRowHeight;
				if (i > 0)
					Raylib.DrawLine(infoLabelX, (int)rowY - 4, infoValueRightX, (int)rowY - 4, new Color(45, 54, 76, 180));
				FontManager.DrawText(rows[i].label, infoLabelX, (int)rowY, 16, new Color(150, 160, 182, 255));
				int valueWidth = FontManager.MeasureText(rows[i].value, 16);
				FontManager.DrawText(rows[i].value, infoValueRightX - valueWidth, (int)rowY, 16, new Color(225, 230, 240, 255));
			}

			// ── Personnage, centré verticalement dans l'espace restant entre l'en-tête et le bloc d'infos. ──
			float previewTop = panel.Y + 78;
			float previewBottom = infoPanel.Y - 12;
			float previewCenterY = previewTop + (previewBottom - previewTop) / 2f;
			float previewAvailable = Math.Max(40f, previewBottom - previewTop);
			float detailScale = Math.Clamp(Math.Min(panel.Width * 0.7f, previewAvailable) / 95f, 1.6f, 3.6f);
			if (drawCharacterPreviews)
				DrawClassicSavePreview(save.Name, new Vector2(panel.X + panel.Width / 2f, previewCenterY + previewAvailable * 0.28f), detailScale, previewAvailable, facing: -1f, areaTop: previewTop, groundLine: previewBottom);
		}

		static void DrawClassicMenuButton(Rectangle rect, string text, bool hovered, bool enabled)
		{
			Color background = !enabled ? new Color(35, 38, 45, 255) : hovered ? new Color(83, 105, 137, 255) : new Color(48, 61, 82, 255);
			Raylib.DrawRectangleRounded(rect, 0.08f, 6, background);
			Raylib.DrawRectangleRoundedLines(rect, 0.08f, 6, 1f, new Color(120, 135, 158, 255));
			int fontSize = Math.Max(16, (int)(rect.Height * 0.34f));
			int textWidth = FontManager.MeasureText(text, fontSize);
			FontManager.DrawText(text, (int)(rect.X + (rect.Width - textWidth) / 2), (int)(rect.Y + (rect.Height - fontSize) / 2), fontSize, enabled ? Color.White : new Color(125, 130, 140, 255));
		}

		private static void LoadLanguageFlagTextures(List<string> missingTextures, LoadStats stats)
		{
			const string flagsDir = "assets/gui";
			if (!Directory.Exists(flagsDir))
				return;

			var flagFiles = Directory.GetFiles(flagsDir, "flag_*.png");
			int loaded = 0;
			foreach (var file in flagFiles)
			{
				string fileName = Path.GetFileName(file);
				string code = Path.GetFileNameWithoutExtension(fileName).Substring("flag_".Length).ToLowerInvariant();
				var tex = TryLoad($"{flagsDir}/{fileName}");
				_languageFlagTextures[code] = tex;
				if (tex.Id != 0)
				{
					loaded++;
					stats.Success++;
				}
				else
				{
					missingTextures.Add(file);
					stats.Warnings++;
				}
			}
			PrintInfo($"Drapeaux de langue charges : {loaded}/{flagFiles.Length}");
		}

		private static void LoadAnimals(List<string> missingTextures, LoadStats stats)
		{
			int totalLoaded = 0, totalMissing = 0;
			
			foreach (var species in SpeciesData.Skeletons)
			{
				string folderName = char.ToUpper(species.Key[0]) + species.Key.Substring(1);
				int loaded = 0, missing = 0;
				
				foreach (var part in species.Value)
				{
					string path1 = $"assets/animals/{folderName}/{folderName}{part.Name}.png";
					string path2 = $"assets/animals/{folderName}/{part.Name}.png";
					
					if (File.Exists(path1) || File.Exists(path2))
						loaded++;
					else
					{
						missing++;
						missingTextures.Add(path1);
					}
				}
				
				totalLoaded += loaded;
				totalMissing += missing;
				
				if (missing != 0)
					PrintWarning($"{species.Key,-12} -> {loaded} chargées, {missing} manquantes");
			}
			
			stats.Success += totalLoaded;
			stats.Warnings += totalMissing;
		}

		private static void PrintSummary(List<string> missingTextures, LoadStats stats)
		{
			Console.WriteLine();
			PrintBanner("RESUME DU CHARGEMENT", ConsoleColor.Cyan);
			
			int totalMissing = missingTextures.Distinct().Count();
			
			Console.WriteLine();
			if (totalMissing == 0)
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine($"  [OK]  {"TOUTES LES RESSOURCES SONT PRESENTES !"}");
				Console.ResetColor();
				PrintInfo($"{stats.Success} assets charges avec succes");
			}
			else
			{
				PrintWarning($"{totalMissing} FICHIER(S) MANQUANT(S) DETECTE(S)");
				Console.WriteLine();
				PrintInfo("Textures manquantes :");
				
				foreach (var tex in missingTextures.Distinct().OrderBy(x => x))
					PrintError($"   {tex}");
			}
			
			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════╗");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine($"║ {Localization.Get("ui.loading_complete"),60} ║");
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════╝");
			Console.ResetColor();
			Console.WriteLine();
		}
    }
}
