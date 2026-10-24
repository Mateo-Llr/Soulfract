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

		//  CORPS MORTS : physique des ragdolls (chute, glissement, poussée) et
		//  dépose après 20 secondes.
		//  ATTENTION : cet appel est placé AVANT les "goto SkipMovementAndInteraction"
		//  (mort du joueur, transition de grotte, voiture, chat, matérialisation...).
		//  Ces gotos sautent jusqu'au label, donc une mise à jour placée après le label
		//  n'était jamais exécutée dans ces situations : les corps restaient figés en
		//  l'air (et le corps du joueur ne tombait jamais).
		CorpseSystem.Update(dt);

		//  MORT : la séquence de disparition se joue sans bloquer le monde ; le joueur ne
		//  réapparaît plus automatiquement après un délai, il clique sur le bouton
		//  "Réapparaître" affiché une fois l'écran de mort (vignette -> crâne -> message)
		//  entièrement joué (voir UpdateDeathScreen / DrawDeathScreen).
		if (IsPlayerRagdolled)
		{
			if (_ragdollMinDelay > 0f) _ragdollMinDelay -= dt;
			UpdatePlayerDeathParticles(dt);

			//  La caméra suit le corps du joueur (qui tombe puis glisse au sol).
			Vector2 deathTarget = _ragdollMinDelay > 0f
				? _playerDeathPosition
				: CorpseSystem.GetPlayerCorpsePosition(_playerDeathPosition);
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

			// Conversation en pages du Veilleur : nettoie son état si elle est interrompue.
			UpdateStoryGuideDialog();

			//  SYSTÈME DE QUÊTES : gestion des clics sur la fenêtre de dialogue de quête
			if (QuestDialogUI.IsOpen)
			{
				string? questAction = QuestDialogUI.Update(Raylib.GetMousePosition());
				if (questAction == "story_complete")
				{
					CompleteStoryGuideDialog();
				}
				else if (questAction == "accept")
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
							QuestManager.AcceptQuest(giver, (itemId, qty) => GiveQuestItemToPlayer(itemId, qty, giver.WorldPos));
						}
						if (quest != null)
						{
							var target = QuestManager.FindEntityByNetId(GetQuestNpcs(), quest.TargetId);
							string targetName = quest.Type == QuestType.TameAndBring
								? giver.DisplayName ?? giver.FirstName ?? Localization.Get("quest.unknown_target", "???")
								: target?.DisplayName ?? target?.FirstName ?? Localization.Get("quest.unknown_target", "???");
							QuestSplashUI.Show(
								Localization.Get("quest.splash.new", "Nouvelle quête"),
								quest.GetReminderLine(targetName),
								string.Format(Localization.Get("quest.offer_reward", "Récompense : {0}"), quest.GetRewardLine()),
								new Color(255, 215, 100, 255),
								new Color(255, 255, 255, 255));
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
						OpenVillagerChoiceDialog(npc); // retour à la bulle de choix (sans relancer la conversation du Veilleur)
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
						string topic = questAction.Substring("social_".Length);
						if (topic == "village" && !npc.HasStableVillageHome)
						{
							QuestDialogUI.OpenSocialChat(npc);
							return;
						}

						npc.RememberRandomSocialContact();
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
			if (GetEffectivePlayerMaxHP() >= 20 && !AchievementManager.IsUnlocked(AchievementType.MaxHealth))
				AchievementManager.UnlockAchievement(AchievementType.MaxHealth);
			
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
				if (heldId == GameData.GetItemId("torch"))
				{
					if (SpeciesData.Skeletons.TryGetValue("human", out var skeleton))
					{
						var rightArmBottom = skeleton.FirstOrDefault(p => p.Name == "rarmbottom");
						if (rightArmBottom != null)
						{
							Vector2 playerVisualPos = GetPlayerVisualPosition(playerPos);
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
						float objX = tileX * ts + ts/2f;
						float objY = tileY * ts + ts/2f;
						Vector2 playerVisualPos = GetPlayerVisualPosition(_playerPos);
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
			if (!ChatSystem.IsOpen && PlayerCurrentCar == null && KeyBindings.IsPressed(GameAction.BoatRow))
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
				ItemData throwData = default;
				bool holdingThrowable = heldThrowable != null
					&& GameData.ItemDatabase.TryGetValue(GetItemId(heldThrowable.Name), out throwData)
					&& throwData.IsThrowable;
				
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
							if (string.Equals(throwData.Key, "dynamite", StringComparison.OrdinalIgnoreCase))
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

				if (hasMagicWand && !mouseOverUI && !SpellCraftingUI.IsOpen)
				{
					float now = (float)Raylib.GetTime();

					// Léger : tir immédiat puis répétition cadencée tant que le clic est maintenu.
					if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !_isChargingRightClick)
					{
						_isChargingLeftClick = true;
						_leftClickCharge = 0f;
						_leftClickStartTime = now;
						if (_leftClickSpell.Power == PowerLevel.Weak)
						{
							CastAssignedSpell(_leftClickSpell, 0f, camera);
							_leftSpellNextCastTime = now + LIGHT_SPELL_REPEAT_INTERVAL;
						}
					}
					else if (_isChargingLeftClick)
					{
						if (Raylib.IsMouseButtonDown(MouseButton.Left))
						{
							if (_leftClickSpell.Power == PowerLevel.Weak)
							{
								if (now >= _leftSpellNextCastTime)
								{
									CastAssignedSpell(_leftClickSpell, 0f, camera);
									_leftSpellNextCastTime = now + LIGHT_SPELL_REPEAT_INTERVAL;
								}
							}
							else
							{
								float holdTime = now - _leftClickStartTime;
								_leftClickCharge = Math.Min(1f, holdTime / MAX_CHARGE_TIME);
							}
						}
						else if (Raylib.IsMouseButtonReleased(MouseButton.Left))
						{
							float holdTime = now - _leftClickStartTime;
							if (_leftClickSpell.Power == PowerLevel.Medium
								|| (_leftClickSpell.Power == PowerLevel.Strong && holdTime >= MIN_CHARGE_THRESHOLD))
							{
								float charge = Math.Min(1f, holdTime / MAX_CHARGE_TIME);
								CastAssignedSpell(_leftClickSpell, charge, camera);
							}
							_isChargingLeftClick = false;
							_leftClickCharge = 0f;
						}
					}

					// Même cadence de tir pour le clic droit.
					if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !_isChargingLeftClick)
					{
						_isChargingRightClick = true;
						_rightClickCharge = 0f;
						_rightClickStartTime = now;
						if (_rightClickSpell.Power == PowerLevel.Weak)
						{
							CastAssignedSpell(_rightClickSpell, 0f, camera);
							_rightSpellNextCastTime = now + LIGHT_SPELL_REPEAT_INTERVAL;
						}
					}
					else if (_isChargingRightClick)
					{
						if (Raylib.IsMouseButtonDown(MouseButton.Right))
						{
							if (_rightClickSpell.Power == PowerLevel.Weak)
							{
								if (now >= _rightSpellNextCastTime)
								{
									CastAssignedSpell(_rightClickSpell, 0f, camera);
									_rightSpellNextCastTime = now + LIGHT_SPELL_REPEAT_INTERVAL;
								}
							}
							else
							{
								float holdTime = now - _rightClickStartTime;
								_rightClickCharge = Math.Min(1f, holdTime / MAX_CHARGE_TIME);
							}
						}
						else if (Raylib.IsMouseButtonReleased(MouseButton.Right))
						{
							float holdTime = now - _rightClickStartTime;
							if (_rightClickSpell.Power == PowerLevel.Medium
								|| (_rightClickSpell.Power == PowerLevel.Strong && holdTime >= MIN_CHARGE_THRESHOLD))
							{
								float charge = Math.Min(1f, holdTime / MAX_CHARGE_TIME);
								CastAssignedSpell(_rightClickSpell, charge, camera);
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
					Vector2 playerVisualPos = GetPlayerVisualPosition(playerPos);

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
					var weaponState = GetOrCreateRangedWeaponState(heldWeapon!, maxAmmoPerCharge);

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
						SaveRangedWeaponState(heldWeapon!, weaponState);
					}
					else if (weaponState.CurrentAmmo <= 0 && !mouseOverUI && ammoId > 0 && (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)))
					{
						int availableAmmoCount = GetItemCountInInventory(ammoId);
						if (availableAmmoCount > 0)
						{
							StartRangedReload(heldWeapon!, ammoId, availableAmmoCount, maxAmmoPerCharge);
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
							SaveRangedWeaponState(heldWeapon!, weaponState);
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
								_bullets.Add(new ArrowProjectile(spawnPos, shootDir, 1800f, Math.Max(1f, equipment.TotalAttack + 4f)) { IsBullet = true, IsPlayerWeaponProjectile = true });
								PlayPistolShootSound();
							}
							else
							{
								_arrows.Add(new ArrowProjectile(spawnPos, shootDir, 1200f, equipment.TotalAttack) { IsPlayerWeaponProjectile = true });
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
		for (int slot = 0; slot < 10; slot++)
			if (KeyBindings.IsHotbarSlotPressed(slot)) hotbarSlot = slot;
		
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
				else if (SlimeStorageUI.IsMouseOverWindow(mousePos))
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
				if (PlayerCurrentCar == null && KeyBindings.IsPressed(GameAction.Interact) && !_isAdjustingSail
					&& !_skateboardInteractConsumedThisFrame)
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
						else if (_isConnectingWire && _wireStartTile != (-1, -1) && World.HasInputPort(objectId))
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
									AddNotification(new Notification(" Appareil connecté !", new Color(100, 255, 100, 255), 2f));
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
							_wireStartGate = null;
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
				if (KeyBindings.IsPressed(GameAction.CarMode))
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
			if (IsSoulMaterializing)
			{
				_animState = "idle";
				_animFrame = 0;
				_animProg = 0f;
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
				else if (PlayerSkateboard != null)
				{
					UpdateSkateboardRide(dt, ref facing, hitboxDim);
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
						if (_noClipEnabled || !World.IsColliding(playerPos + new Vector2(move.X, 0), hitboxDim, destroyedObjects))
							playerPos.X += move.X;
						
						// Mouvement vertical
						if (_noClipEnabled || !World.IsColliding(playerPos + new Vector2(0, move.Y), hitboxDim, destroyedObjects))
							playerPos.Y += move.Y;

					World.ApplyPlayerMovementPush(movementStart, playerPos - movementStart, dt);

					//  (Les corps morts sont poussés par le joueur dans
					//  World.ApplyPlayerMovementPush : voir PushCorpsWithPlayer.)


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
									new Vector2((int)(GetPlayerFeetPosition(playerPos).X / TileSize) * TileSize + TileSize / 2f, (int)(GetPlayerFeetPosition(playerPos).Y / TileSize) * TileSize + TileSize / 2f),
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
					float visualYOffset = World.IsUnderwater ? -_underwaterElevation : 0f;
					camera.Target = new Vector2(playerPos.X, playerPos.Y + visualYOffset);
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
			Vector2 villageDiscoveryPosition = playerPos;
			if (!AchievementManager.IsUnlocked(AchievementType.FindVillage)
				&& World.VillageCenters.Any(center =>
					Vector2.DistanceSquared(villageDiscoveryPosition, World.GetVillageCenterWorldPosition(center)) <= 600f * 600f))
			{
				AchievementManager.UnlockAchievement(AchievementType.FindVillage);
			}
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
			int entityAiFramePhase = unchecked(++_entityAiUpdateFrame) & 3;
			for (int i = entities.Count - 1; i >= 0; i--)
			{
				var e = entities[i];
				if (!e.IsAlive)
				{
					if (e.WasKilledByPlayerWeapon)
						SoundEffects.PlaySwordKillOrganic();
					else if (!e.IsTamed && (e.IsBoss
						|| string.Equals(e.Behavior, "hostile", StringComparison.OrdinalIgnoreCase)))
						SoundEffects.PlayEnemyDeathPuff();

					if (!e.IsBoss && !e.IsTamed
						&& string.Equals(e.Behavior, "hostile", StringComparison.OrdinalIgnoreCase))
					{
						AchievementManager.Progress(AchievementType.KillEnemies);
					}
					if (e.IsBoss && string.Equals(e.Species, "Khamsin", StringComparison.OrdinalIgnoreCase))
					{
						GiveItemToPlayer(GameData.GetItemId("khamsin_heart"), 1, e.WorldPos);
					}
					if (e.IsBoss && string.Equals(e.Species, "Genie", StringComparison.OrdinalIgnoreCase))
					{
						GiveItemToPlayer(GameData.GetItemId("genie_heart"), 3, e.WorldPos);
						//  Loot garanti : une pierre runique Solaire OU Givrée (tirage aléatoire),
						// les deux runes de puissance du Génie de la Lampe.
						var genieRuneType = Random.Shared.Next(2) == 0 ? RuneType.Solar : RuneType.Frost;
						var genieRune = new RuneData { Type = genieRuneType, Value = 15f };
						DropCustomItemOnGround(e.WorldPos, GameData.GetItemId("runic_stone"), 1, null, null, 0f, genieRune.ToMetadata());
						Entity.SpawnGenieSmoke(e.WorldPos, 2f);
						Program.ApplyScreenShake(6f, 0.3f);
						string genieRuneLabel = genieRuneType == RuneType.Solar ? "Solaire" : "Givrée";
					}
					Vector2 entityVisualPos = e.WorldPos;
					SpawnDeathParticles(entityVisualPos, e.Tint);
					//  CORPS MORT (ragdoll) : l'entité laisse un corps physique au sol, qui
					//  reste 20 secondes, peut être poussé, et est rendu par le même moteur
					//  de rendu que les entités vivantes. La position d'ancrage est celle
					//  utilisée par le rendu des entités (y compris le décalage visuel des
					//  espèces volantes / gluantes) pour que le corps soit exactement à la
					//  place où l'entité était affichée.
					Vector2 corpseDrawPos = new Vector2(e.WorldPos.X, e.WorldPos.Y - e.VisualOffsetY);
					//  Le sol du corps est la ligne du terrain (là où l'ombre de la créature
					//  est tracée) : une créature volante laisse donc un corps qui TOMBE
					//  d'abord jusqu'au sol avant de basculer.
					float corpseGroundY = entityVisualPos.Y + (FeetOffsetY - 6f);
					CorpseSystem.SpawnFromEntity(e, corpseDrawPos, corpseGroundY);
					if (TraderUI.IsOpen && TraderUI.GetCurrentTrader() == e) TraderUI.Close();
					//  MULTIJOUEUR : informer les clients de la mort de cette entité
					NetworkManager.BroadcastEntityDeath(e.NetId, e.WasKilledByPlayerWeapon);
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
					if (!e.ShouldRunFullAiUpdate(targetPos, i, entityAiFramePhase))
						continue;

					PerfStats.NotifyFullAiUpdate();
					e.Update(dt, destroyedObjects, targetPos, targetHeldItemId);
					if (e.TryGetRequestedBirdRoost(out int roostX, out int roostY)
						&& World.TryStoreBirdInTree(roostX, roostY, e.Species))
					{
						entities.RemoveAt(i);
					}
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
						if (RemoveItemFromInventoryById(GameData.GetItemId("flute"), 1))
						_pendingFluteRemovals.RemoveAt(i);
					}
				}
			}
			
			foreach (var car in Cars)
			{
				if (car == PlayerCurrentCar) continue;
				car.Update(dt, destroyedObjects);
			}
			UpdateLooseSkateboards(dt);
			
			// Mise à jour des effets divers
			foreach (var hp in objectHPs.Values) if (hp.DamageFlash > 0) hp.DamageFlash -= dt;
			for (int i = _floatingDamages.Count - 1; i >= 0; i--)
			{
				_floatingDamages[i].Update(dt);
				if (!_floatingDamages[i].IsAlive) _floatingDamages.RemoveAt(i);
			}
			World.UpdateTreeFalls(dt, _particles);
			World.UpdateTreeBirds(dt, _playerPos);
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
						float dist = Vector2.Distance(GetPlayerFeetPosition(_playerPos), new Vector2(ngi.PosX, ngi.PosY));
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
						&& Vector2.DistanceSquared(gi.Position, GetPlayerFeetPosition(_playerPos)) < reachableRadius * reachableRadius
						&& HasSpaceForItem(gi.ItemId);
					gi.Update(dt, GetPlayerFeetPosition(_playerPos), canAttract);
					if (canAttract && gi.CanPickup(GetPlayerFeetPosition(_playerPos)))
					{
						if (!string.IsNullOrWhiteSpace(gi.LootbagPayload))
						{
							RestoreLootbagPayload(gi.LootbagPayload);
							GroundItems.RemoveAt(i);
							SpawnPickupParticles(World.GetEntityGroundPosition(gi.Position));
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
								SpawnPickupParticles(World.GetEntityGroundPosition(gi.Position));
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

    }
}
