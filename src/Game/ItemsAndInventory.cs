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
						float plantDist = GetPlacementDistanceFromPlayerFeet(playerPos, bestTileX, bestTileY);
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
				if (heldId == GameData.GetItemId("butterfly_net")) // Pas de zone d'attaque : capture directe
				{
					return;
				}
			}

			int playerTileX = (int)(GetPlayerFeetPosition(playerPos).X / ts);
			int playerTileY = (int)(GetPlayerFeetPosition(playerPos).Y / ts);
			Vector2 attackDir = GetPlayerAttackDirection(playerPos, mouseWorld, facing);
			float attackRadius = GetAttackRadius(heldItem);
			_swingDirection = attackDir;
			_swingAngle = MathF.Atan2(attackDir.Y, attackDir.X) * 180f / MathF.PI;
			_swingTimer = 0.25f;
			_attackSwingProgress = 0.01f;
			isAttacking = true;
			attackAnim = 0.35f;
			attackCooldown = 0.5f;
			if (heldItem != null
				&& GameData.GetItemType(heldItem.Name) is ItemType.Tool or ItemType.Weapon
				&& !GameData.IsRangedWeapon(heldItem.Name))
				SoundEffects.PlaySwing();

			if (heldItem != null && GameData.GetToolType(heldItem.Name) == ToolType.Scythe)
				HarvestScythePlants(GetPlayerAttackOrigin(playerPos), attackDir, attackRadius);

			// ========== PVP : joueurs adverses touchés par le coup (consentement mutuel) ==========
			var pvpTargets = GetPvpTargetsInAttackCone(playerPos, attackDir, attackRadius, ATTACK_ANGLE);
			if (pvpTargets.Count > 0)
			{
				PlayEntityImpactSound(heldItem);
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
					Vector2 targetVisualPos = target.Position;
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
				var hitDtos = GetRemoteEntitiesInAttackCone(playerPos, attackDir, attackRadius, ATTACK_ANGLE);
				if (hitDtos.Count > 0)
				{
					PlayEntityImpactSound(heldItem);
					int clientDamage = equipment.TotalAttack;
					foreach (var dto in hitDtos)
					{
						if (!Guid.TryParse(dto.NetId, out var dtoGuid)) continue;
						bool isPlayerWeapon = heldItem != null
							&& GameData.GetItemType(heldItem.Name) == ItemType.Weapon;
						int concussion = heldItem != null ? GameData.GetConcussion(heldItem.Name) : 0;
						NetworkManager.RequestEntityHit(dtoGuid, clientDamage, isPlayerWeapon, concussion);
						Vector2 dtoVisualPos = new Vector2(dto.PosX, dto.PosY);
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
			var hitEntities = GetEntitiesInAttackCone(playerPos, attackDir, attackRadius, ATTACK_ANGLE);
			//  Une créature en l'air (oiseau en vol, boss volant non posé...) ne peut pas être
			// touchée par une simple attaque de corps à corps : elle est hors de portée tant
			// qu'elle n'a pas atterri (voir Entity.IsAirborne / IsInvulnerable).
			if (hitEntities.Count > 0)
				hitEntities.RemoveAll(e => e.IsInvulnerable);
			if (hitEntities.Count > 0)
			{
				PlayEntityImpactSound(heldItem); // Un son par attaque qui touche, pas par entité.
				int damage = equipment.TotalAttack;
				foreach (var entity in hitEntities)
				{
					entity.CurrentHP -= damage;
					if (heldItem != null)
						entity.ApplyConcussion(GameData.GetConcussion(heldItem.Name));
					entity.OnHit(playerPos);
					Vector2 knockbackDir = entity.WorldPos - playerPos;
					if (knockbackDir.Length() > 0.01f) knockbackDir = Vector2.Normalize(knockbackDir);
					else knockbackDir = new Vector2(1, 0);
					entity.ApplyKnockback(knockbackDir, ATTACK_KNOCKBACK);
					Vector2 entityVisualPos = entity.WorldPos;
					if (!float.IsNaN(entityVisualPos.X) && !float.IsNaN(entityVisualPos.Y) &&
						!float.IsInfinity(entityVisualPos.X) && !float.IsInfinity(entityVisualPos.Y))
					{
						AddFloatingDamage(entityVisualPos, damage, damage >= 8, isEnemy: true);
							SpawnSlashEffect(entityVisualPos, attackDir);
					}
					else
					{
						AddFloatingDamage(GetPlayerVisualPosition(playerPos), damage, damage >= 8, isEnemy: true);
					}
					if (!entity.IsAlive)
					{
						if (heldItem != null
							&& GameData.GetItemType(heldItem.Name) == ItemType.Weapon)
							entity.WasKilledByPlayerWeapon = true;

						SkillSystem.AddXP(SkillType.Chasseur, 5);
						entityVisualPos = entity.WorldPos;
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
			
			float drawY = bestTileY * ts;
			Vector2 targetTileWorldPos = new(bestTileX * ts + ts / 2f, bestTileY * ts + ts / 2f);
			
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
							float boatObjDrawY = boatObjTileY * ts;
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
								SpawnTileBreakParticles(World.GetEntityGroundPosition(boatObjectWorldPos), boatTileColor);
								
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
					if (heldItem != null && GameData.GetToolType(heldItem.Name) == ToolType.Scythe)
						return;

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
							Program.GiveItemToPlayer(harvestItemId, qty, targetTileWorldPos);

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
				if (heldItem != null && GameData.GetToolType(heldItem.Name) == ToolType.Scythe)
					return;

				string collectKey = $"{bestTileX}_{bestTileY}";
				if (!destroyedObjects.Contains(collectKey))
				{
					AchievementManager.Progress(AchievementType.GatherResources);
					foreach (var (id, qty) in WorldTileRegistry.GetDrops(tileId))
					{
						Program.GiveItemToPlayer(id, qty, targetTileWorldPos, pickupCooldown: 0f);
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
					toolType != ToolType.WateringCan)
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
			float finalWorldY = bestTileY * ts + ts; // bas de la tuile
			Vector2 objectWorldPos = new Vector2(bestTileX * ts + ts/2f, finalWorldY - 15f); // légèrement au-dessus
			if (World.IsLeafBearingTree(tileId))
				TrySpawnBirdFromTreeHit(bestTileX, bestTileY);
			AddFloatingDamage(objectWorldPos, damageAmount, isEnemy: true);
			if (hp.Current <= 0)
			{
				AchievementManager.Progress(AchievementType.GatherResources);
				if (tileId == 86)
				{
					const string flowerMetaKey = "potted_flower";
					string plantedFlowerId = World.GetTileMeta(bestTileX, bestTileY, flowerMetaKey);
								if (int.TryParse(plantedFlowerId, out int flowerItemId))
					{
						Program.GiveItemToPlayer(flowerItemId, 1, targetTileWorldPos, pickupCooldown: 0f);
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
										Program.GiveItemToPlayer(itemId, slot.Count, targetTileWorldPos, pickupCooldown: 0f);
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
								Program.GiveItemToPlayer(itemId, 1, targetTileWorldPos, pickupCooldown: 0f);
							}
							if (standData.Body != null)
							{
								int itemId = GetItemId(standData.Body.Name);
								Program.GiveItemToPlayer(itemId, 1, targetTileWorldPos, pickupCooldown: 0f);
							}
							if (standData.Legs != null)
							{
								int itemId = GetItemId(standData.Legs.Name);
								Program.GiveItemToPlayer(itemId, 1, targetTileWorldPos, pickupCooldown: 0f);
							}
							if (standData.MainHand != null)
							{
								int itemId = GetItemId(standData.MainHand.Name);
								Program.GiveItemToPlayer(itemId, 1, targetTileWorldPos, pickupCooldown: 0f);
							}
							if (standData.OffHand != null)
							{
								int itemId = GetItemId(standData.OffHand.Name);
								Program.GiveItemToPlayer(itemId, 1, targetTileWorldPos, pickupCooldown: 0f);
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
						Program.GiveItemToPlayer(mineralItemId, qty, targetTileWorldPos, pickupCooldown: 0f);
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
				if (World.IsLeafBearingTree(tileId) && !World.IsMatureTree(tileId))
					World.SpawnLeafBurst(bestTileX, bestTileY, _particles, Random.Shared.Next(14, 22));

				if (World.HasTileMeta(bestTileX, bestTileY, "beehive"))
				{
					int beeWaxId = GameData.GetItemId("bee_wax");
					if (beeWaxId > 0)
						Program.GiveItemToPlayer(beeWaxId, 1, targetTileWorldPos, pickupCooldown: 0f);
					World.RemoveTileMeta(bestTileX, bestTileY, "beehive");
				}

				if (World.IsMatureTree(tileId))
					World.FellTree(bestTileX, bestTileY, World.GetTreeHitSwayDirection(bestTileX, bestTileY));
				else
					World.RemovePlacedObject(bestTileX, bestTileY);
				if (tileId == 8 || tileId == 84)
				{
					World.UpdateRoofsAt(bestTileX, bestTileY);
				}
				objectHPs.Remove(objKey);
				foreach (var (id, qty) in WorldTileRegistry.GetDrops(tileId))
				{
					Program.GiveItemToPlayer(id, qty, targetTileWorldPos, pickupCooldown: 0f);
				}
			}
		}

		private static void TrySpawnBirdFromTreeHit(int treeTileX, int treeTileY)
		{
			if (World.ReleaseBirdsFromTree(treeTileX, treeTileY) > 0)
				return;

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
					float visualX = x * ts;
					float centerX = visualX + ts / 2f;
					float centerY = y * ts + ts / 2f;
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
				float centerY = groundTileY * ts + ts / 2f;
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
			float distanceToTile = GetPlacementDistanceFromPlayerTile(playerPos, tileX, tileY);
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
				if (carriedTileData.IsFurniture)
					AchievementManager.Progress(AchievementType.PlaceFurniture);

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
				Vector2 carPos = new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f);
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
				var existingHouseIds = World.Houses.Keys.ToHashSet();
				World.UpdateRoofsAt(tileX, tileY);
				World.RefreshAllHouses();
				if (World.Houses.Keys.Any(buildingId => !existingHouseIds.Contains(buildingId)))
				{
					AchievementManager.UnlockAchievement(AchievementType.BuildHouse);
				}
			}

			if (tileDefPlaced?.IsFurniture == true)
				AchievementManager.Progress(AchievementType.PlaceFurniture);
			
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
                AchievementManager.RecordItemTypeObtained(item.Name);
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
