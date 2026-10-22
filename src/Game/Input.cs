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
				if (KeyBindings.IsPressed(GameAction.GuildMenu) || Raylib.IsKeyPressed(KeyboardKey.Escape))
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
				if (KeyBindings.IsPressed(GameAction.SelectStructureCorner1))
				{
					Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), inputCamera);
					int tileX = (int)(mouseWorld.X / TileSize);
					int tileY = (int)(mouseWorld.Y / TileSize);
					_selectionCorner1 = new Vector2Int(tileX, tileY);
					AddNotification(new Notification($"Premier coin sélectionné : ({tileX}, {tileY})", Color.Yellow, 2f));
					_selectionCorner2 = null;
				}
				else if (KeyBindings.IsPressed(GameAction.SelectStructureCorner2))
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
		if (KeyBindings.IsDown(GameAction.TimeSkip))
		{
			_timeSkipRemaining += dt * 60f;  // Le temps avance 60x plus vite tant que F6 est pressé
		}
		
		// Rechargement des données (F4)
		if (KeyBindings.IsPressed(GameAction.ReloadData) && _gameState == GameState.Playing && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen && !QuestDialogUI.IsOpen)
			{
				ReloadData();
			}
			
			// Carte (M)
			if (KeyBindings.IsPressed(GameAction.Map) && (_gameState == GameState.Playing || _gameState == GameState.Map))
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
			if (KeyBindings.IsPressed(GameAction.Bestiary) && _gameState == GameState.Playing && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen)
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
			if (KeyBindings.IsPressed(GameAction.ToggleDebug) && _gameState == GameState.Playing)
			{
				CycleDebugMode();
			}
			
			// Fullscreen (F11)
			if (KeyBindings.IsPressed(GameAction.Fullscreen))
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
			if (KeyBindings.IsPressed(GameAction.MagicMenu) && !ChatSystem.IsOpen && PlayerCurrentCar == null)
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
			if (KeyBindings.IsPressed(GameAction.QuickSave) && _gameState == GameState.Playing && !string.IsNullOrEmpty(_currentSaveName))
			{
				SaveCurrentGame();
				ShowSavingIcon();
			}
			
			// Touche R sur un wagon (sous la souris) : lier / délier des wagons
			if (KeyBindings.IsPressed(GameAction.WagonLink) && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen
				&& !QuestDialogUI.IsOpen && PlayerCurrentCar == null)
				TryHandleWagonLinkClick(inputCamera, inputViewport);

			// Dans HandleInput, après la gestion des interactions de station, etc.
			if (KeyBindings.IsPressed(GameAction.Interact) && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen && !QuestDialogUI.IsOpen)
			{
				if (TryInteractWithMusicBiped(inputCamera)) return;
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
						// Comparaison au carré : évite la racine carrée pour chaque voiture testée.
						Vector2 carDelta = _playerPos - car.Position;
						if (carDelta.LengthSquared() < 80f * 80f)
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
			if (IsGodMode && KeyBindings.IsPressed(GameAction.ItemsMenu) && _gameState == GameState.Playing)
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
				if (KeyBindings.IsPressed(GameAction.GuildMenu) && _gameState == GameState.Playing)
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
			bool pauseActionPressed = KeyBindings.IsPressed(GameAction.PauseMenu);
			if (Raylib.IsKeyPressed(KeyboardKey.Escape) || pauseActionPressed)
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
					CancelStoryGuideDialog(); // Échap abandonne aussi la conversation en pages du Veilleur
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
					if (!pauseActionPressed)
						return;

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

			// L'invocation doit précéder les interactions génériques au clic droit,
			// qui peuvent consommer l'action avant que la lampe ne soit traitée.
			if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !_rightClickConsumed
				&& PlayerCurrentCar == null && !ChatSystem.IsOpen && !InventoryRenderer.IsInventoryOpen
				&& !QuestDialogUI.IsOpen)
			{
				Item? heldLamp = equipment.MainHand;
				if (heldLamp != null && GetItemId(heldLamp.Name) == GameData.GetItemId("genie_lamp"))
				{
					if (CurrentBoss != null && CurrentBoss.IsAlive)
					{
						AddNotification(new Notification(" Un danger rôde déjà... Ce n'est pas le moment.", new Color(220, 100, 100, 255), 2f));
					}
					else
					{
						Vector2 spawnPos = _playerPos + new Vector2(0f, -140f);
						int spawnTileX = (int)(spawnPos.X / TileSize);
						int spawnTileY = (int)(spawnPos.Y / TileSize);
						int spawnGroundId = World.GetGroundTileIdAt(spawnTileX, spawnTileY);
						var spawnGround = WorldTileRegistry.GetTile(spawnGroundId);
						if (spawnGround == null || !spawnGround.Walkable || World.GetObjectIdAt(spawnTileX, spawnTileY) != 0)
							spawnPos = _playerPos + new Vector2(160f, 0f);

						spawnTileX = (int)(spawnPos.X / TileSize);
						spawnTileY = (int)(spawnPos.Y / TileSize);
						RemoveItemFromInventory(heldLamp.Name, 1);
						Entity.SpawnGenieSmoke(spawnPos, 1.6f);
						Program.ApplyScreenShake(5f, 0.3f);

						var genie = new Entity(spawnPos, "Genie", false);
						genie.CustomName = "Génie de la Lampe";
						GetEntities().Add(genie);
						var spawnChunk = World.GetChunkAt(spawnTileX, spawnTileY);
						if (spawnChunk != null) spawnChunk.Entities.Add(genie);
						CurrentBoss = genie;

						AddNotification(new Notification(" \"QUI OSE ME RÉVEILLER ?!\" Le Génie de la Lampe apparaît !", new Color(255, 200, 80, 255), 3.5f));
					}
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
						float dist = Vector2.Distance(GetPlayerFeetPosition(_playerPos), new Vector2(fTileX * TileSize + TileSize/2f, fTileY * TileSize + TileSize/2f));
						if (dist < MAX_INTERACTION_DISTANCE)
						{
							var tileData = WorldTileRegistry.GetTile(fObjectId);
							if (tileData != null
								&& (tileData.IsSapling || tileData.IsCrop || tileData.HasGrowthStages)
								&& (!tileData.IsCrop || World.IsFarmlandWet(fTileX, fTileY)))
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
									SpawnFertilizerParticles(World.GetEntityGroundPosition(fertilPos));
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

			if (Raylib.IsKeyPressed(KeyboardKey.M) && !InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen && PlayerCurrentCar == null && !InstrumentUI.IsOpen && InstrumentUI.GetEquippedInstrument() != null)
			{
				InstrumentUI.Open();
			}

			bool instrumentOpenRequested = Raylib.IsMouseButtonPressed(MouseButton.Left) && !InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen && PlayerCurrentCar == null && !_rightClickConsumed;
			if (instrumentOpenRequested)
			{
				Item? heldInstrument = InstrumentUI.GetEquippedInstrument();
				if (heldInstrument != null && GameData.ItemDatabase.TryGetValue(GetItemId(heldInstrument.Name), out var data) && data.IsInstrument)
				{
					if (GetItemId(heldInstrument.Name) == GameData.GetItemId("flute"))
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
						if (animalDto != null && !animalDto.IsTamed && !animalDto.IsSpiritAnimal
							&& animalDto.CanBeTamedByPlayer && Guid.TryParse(animalDto.NetId, out var animalNetId))
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
						if (animal != null && !animal.IsTamed && !animal.IsSpiritAnimal && animal.CanBeTamedByPlayer)
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
								animal.WakeFromTaming();
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
					if (heldItemForMilk != null && GetItemId(heldItemForMilk.Name) == GameData.GetItemId("bucket"))
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
					if (heldItem != null && GetItemId(heldItem.Name) == GameData.GetItemId("rock"))
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
							float dist = Vector2.Distance(GetPlayerFeetPosition(_playerPos), new Vector2(tileX * TileSize + TileSize/2f, tileY * TileSize + TileSize/2f));
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
									SpawnWateringCanParticles(GetVisualTileCenter(tileX, tileY));
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
										SpawnFertilizerParticles(World.GetEntityGroundPosition(fertilPos));
										_rightClickConsumed = true;
									}
								}
							}
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
			if (KeyBindings.IsPressed(GameAction.EmoteWheel) && !ChatSystem.IsOpen && PlayerCurrentCar == null)
			{
				_isEmoteWheelOpen = true;
				_emoteWheelHoverIndex = -1;
			}

			if (_isEmoteWheelOpen)
			{
				UpdateEmoteWheel(Raylib.GetMousePosition());
				if (KeyBindings.IsReleased(GameAction.EmoteWheel))
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
			if (KeyBindings.IsPressed(GameAction.ArmorStandPose) && !ChatSystem.IsOpen && PlayerCurrentCar == null)
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
						int playerHeight = GetPlayerGroundHeight(_playerPos);
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

				for (int slot = 0; slot < 10; slot++)
					if (KeyBindings.IsHotbarSlotPressed(slot)) hotbarSlot = slot;
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
    }
}
