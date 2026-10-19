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
			ResetAssignedSpells();
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
			World.ClearSpawnProtection(); // la zone protégée de l'ancien monde ne doit pas fuiter dans le nouveau
			
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

			// RECHERCHE STRICTE : on cherche d'abord un point de départ dont tout le voisinage est en
			// plaine, loin de tout village (déjà généré ou prévu par la graine). Le pré-filtre
			// n'utilise que le bruit (aucun chunk généré), seuls les candidats retenus sont vérifiés
			// pour de bon (sol praticable, aucune maison/champ dans la zone tampon).
			// Si rien ne convient, on retombe sur l'ancienne recherche (première plaine trouvée).
			{
				const int strictStepTiles = 4;
				const int strictMaxRing = 120; // 120 * 4 = 480 tuiles autour de l'origine
				int strictCandidates = 0;
				Console.WriteLine(" Recherche d'un spawn dégagé (plaine, loin des villages)...");

				bool TryStrictCandidate(int tileX, int tileY)
				{
					if (strictCandidates >= 40) return false; // garde-fou : chaque vérification génère des chunks
					if (!World.IsSpawnAreaNaturallySuitable(tileX, tileY)) return false;
					strictCandidates++;

					// La protection est posée AVANT de générer les chunks : arbres et maisons
					// ne seront donc jamais créés dans la zone.
					World.SetSpawnProtection(tileX, tileY);
					ForceChunkGenerationAt(tileX * TileSize, tileY * TileSize);
					if (World.IsSpawnAreaVerified(tileX, tileY))
					{
						startX = tileX * TileSize;
						startY = tileY * TileSize;
						spawnBiome = Biome.Plains;
						validSpawn = true;
						Console.WriteLine($" Spawn dégagé trouvé à ({startX:F0}, {startY:F0}) après {strictCandidates} candidat(s) vérifié(s)");
						return true;
					}
					World.ClearSpawnProtection();
					return false;
				}

				for (int ring = 0; ring <= strictMaxRing && !validSpawn; ring++)
				{
					int k = ring * strictStepTiles;
					if (ring == 0)
					{
						TryStrictCandidate(0, 0);
						continue;
					}
					for (int i = -ring; i <= ring && !validSpawn; i++)
					{
						int o = i * strictStepTiles;
						if (TryStrictCandidate(o, -k)) break;
						if (TryStrictCandidate(o, k)) break;
						if (i > -ring && i < ring)
						{
							if (TryStrictCandidate(-k, o)) break;
							if (TryStrictCandidate(k, o)) break;
						}
					}
				}

				if (!validSpawn)
					Console.WriteLine(" Aucun spawn dégagé trouvé, repli sur la première plaine disponible.");
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
						
						int candidateTileX = (int)MathF.Floor(testX / TileSize);
						int candidateTileY = (int)MathF.Floor(testY / TileSize);
						float snappedX = candidateTileX * TileSize;
						float snappedY = candidateTileY * TileSize;
						Biome testBiome = World.GetBiomeAt(candidateTileX, candidateTileY);
						
						if (r % 500 == 0 && (dx == r || dy == r))
							Console.WriteLine($"   Recherche à distance {r}... Biome actuel: {WorldGeneration.GetBiomeDisplayName(testBiome)}");
						
						if (testBiome == Biome.Plains)
						{
							startX = snappedX;
							startY = snappedY;
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
						Biome testBiome = World.GetBiomeAt(x, y);
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

			int spawnTileX = (int)MathF.Floor(startX / TileSize);
			int spawnTileY = (int)MathF.Floor(startY / TileSize);
			ForceChunkGenerationAt(spawnTileX * TileSize, spawnTileY * TileSize);

			// Zone protégée autour du socle : plus aucun arbre/maison/champ n'y sera généré, y compris
			// dans les chunks créés plus tard. On dégage aussi ce qui aurait déjà été généré (repli
			// sur l'ancienne recherche, ou chunks voisins déjà chargés pendant la recherche).
			World.SetSpawnProtection(spawnTileX, spawnTileY);
			int clearedObjects = World.ClearSpawnArea(spawnTileX, spawnTileY, World.SPAWN_CLEAR_RADIUS, SoulAnchorTileId);
			Console.WriteLine($" Zone du socle dégagée : {clearedObjects} objet(s) retiré(s) dans un rayon de {World.SPAWN_CLEAR_RADIUS} tuiles.");

			WorldTileData? soulAnchor = WorldTileRegistry.GetTile(SoulAnchorTileId);
			int anchorWidth = Math.Max(1, soulAnchor?.Size?.Width ?? 1);
			int anchorHeight = Math.Max(1, soulAnchor?.Size?.Height ?? 1);
			int anchorX = spawnTileX - anchorWidth / 2;
			int anchorY = spawnTileY + anchorHeight / 2;
			int anchorTopY = anchorY - anchorHeight + 1;
			for (int x = anchorX; x < anchorX + anchorWidth; x++)
			{
				for (int y = anchorTopY; y <= anchorY; y++)
				{
					int objectId = World.GetObjectIdAt(x, y);
					if (objectId != 0 && objectId != SoulAnchorTileId)
						World.RemovePlacedObject(x, y);
				}
			}
			World.AddPlacedObject(anchorX, anchorY, SoulAnchorTileId);

			// Sentier à moitié effacé qui part du socle et mène au campement du Veilleur (tente, feu,
			// caisses, PNJ qui explique la situation). Sans emplacement valide, le monde n'a juste pas de camp.
			World.TryBuildStoryCamp(spawnTileX, spawnTileY, SoulAnchorTileId);

			_playerPos = new Vector2(spawnTileX * TileSize, spawnTileY * TileSize);
			_playerFacing = 1f;
			
			_playerEntity = new Entity(_playerPos, "human", true);
			_playerEntity.IsPlayer = true;
			RebuildInventoryFromEquipment();
			
			_smoothedCameraTarget = GetPlayerCameraTarget(_playerPos);
			_cameraNeedsImmediateCenter = true;

			Biome finalBiome = World.GetBiomeAt(
				_playerPos.X / TileSize,
				_playerPos.Y / TileSize);
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
			RestoreAssignedSpells(saveData);
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
			_worldSpawnPos = new Vector2(saveData.WorldSpawnX, saveData.WorldSpawnY);
			if (!float.IsFinite(_worldSpawnPos.X) || !float.IsFinite(_worldSpawnPos.Y)
				|| (_worldSpawnPos == Vector2.Zero
					&& (saveData.PlayerPosX != 0f || saveData.PlayerPosY != 0f)))
			{
				_worldSpawnPos = new Vector2(saveData.PlayerPosX, saveData.PlayerPosY);
				Console.WriteLine($" Position de spawn absente ou invalide dans la sauvegarde, repli sur la position du joueur ({_worldSpawnPos})");
			}
			// La zone protégée n'est pas sauvegardée : on la recalcule depuis la position du socle
			// pour que les chunks pas encore générés respectent aussi la zone dégagée.
			World.SetSpawnProtection(
				(int)MathF.Floor(_worldSpawnPos.X / TileSize),
				(int)MathF.Floor(_worldSpawnPos.Y / TileSize));
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
				LeftClickSpellElement = _leftClickSpell.Element,
				LeftClickSpellType = _leftClickSpell.Type,
				LeftClickSpellPower = _leftClickSpell.Power,
				RightClickSpellElement = _rightClickSpell.Element,
				RightClickSpellType = _rightClickSpell.Type,
				RightClickSpellPower = _rightClickSpell.Power,
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

    }
}
