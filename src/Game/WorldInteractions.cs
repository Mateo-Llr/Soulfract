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
		private static bool IsMouseOnAnyOpenUIAtPosition(Vector2 mousePos)
		{
			if (UIManager.IsPointOverRegisteredUI(mousePos))
				return true;

			if (QuestDialogUI.IsOpen)
				return true;

			if (BestiaryUI.IsOpen)
			{
				Rectangle bestiaryRect = new Rectangle(
					BestiaryUI.GetWindowX(),
					BestiaryUI.GetWindowY(),
					BestiaryUI.GetWindowWidth(),
					BestiaryUI.GetWindowHeight()
				);
				if (Raylib.CheckCollisionPointRec(mousePos, bestiaryRect))
					return true;
			}
			
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

						float drawX = x * ts;
						float drawY = y * ts;
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
					if (objId == 408) continue; // Les ampoules électriques sont dessinées selon leur état alimenté ci-dessous.
					if (World.HasInputPort(objId) && !World.IsPoweredOrUnwiredLight(x, y))
						continue;
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
			Vector2 playerWorld = _isSitting
				? GetSittingVisualPosition()
				: GetPlayerVisualPosition(resolvedPlayerPosition);
			
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
					Vector2 drawPosition = new(visualPos.X, visualPos.Y - entity.VisualOffsetY);
					Vector2 eyePosition = EntityRenderer.GetSpiritEyeLightPosition(
						entity.Species, entity.AnimState, entity.CurrentFrame, entity.AnimProgress,
						entity.Facing, drawPosition, entity.IsInWater, entity.Scale, blendKey: entity);
					lights.Add((eyePosition, 160, new Color(130, 190, 255, 255)));
				}

				var info = SpeciesData.GetSpeciesInfo(entity.Species);
				if (info != null && info.IsLightSource)
				{
					Vector2 pos = entity.VisualWorldPos;
					Vector2 visualPos = new Vector2(pos.X, pos.Y - entity.VisualOffsetY);
					
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

					Vector2 drawPosition = World.GetEntityGroundPosition(new Vector2(spirit.PosX, spirit.PosY));
					object spiritPoseBlendKey = string.IsNullOrEmpty(spirit.NetId)
						? spirit
						: EntityRenderer.GetStablePoseBlendKey($"entity:{spirit.NetId}");
					Vector2 eyePosition = EntityRenderer.GetSpiritEyeLightPosition(
						spirit.Species, spirit.AnimState, spirit.AnimFrame, spirit.AnimProg,
						spirit.Facing, drawPosition, false, spirit.Scale, blendKey: spiritPoseBlendKey);
					lights.Add((eyePosition, 160, new Color(130, 190, 255, 255)));
				}
			}
			
			// ========== LUMIÈRES DES PROJECTILES MAGIQUES ==========
			foreach (var sp in _spellProjectiles)
			{
				if (!sp.IsAlive) continue;
				Vector2 pos = sp.Position;
				// La position du point d'impact est déjà en coordonnées monde.
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

					// Seau : contenant à 10 charges (eau ou lait, jamais les deux à la fois),
					// distinct de la gourde/arrosoir qui a son propre compteur de charges.
					if (heldWaterItem != null && GetItemId(heldWaterItem.Name) == GameData.GetItemId("bucket"))
					{
						ParseBucketContent(heldWaterItem.Metadata, out string oceanBucketType, out int oceanBucketCharges);
						float distToWaterBucket = Vector2.Distance(GetPlayerFeetPosition(playerPos), new Vector2(waterTileX * ts + ts / 2f, waterTileY * ts + ts / 2f));
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
						float distToWater = Vector2.Distance(GetPlayerFeetPosition(playerPos), new Vector2(waterTileX * ts + ts / 2f, waterTileY * ts + ts / 2f));
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
					float drawX = tileWorldPos.X - ts/2f;
					float drawY = tileWorldPos.Y - ts/2f;
					
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
						return true;
					}
					
					// Porte-armure sur le bateau
					if (bestTileData.IsArmorStand == true && nearbyBoat.ArmorStands.TryGetValue((bestRelX, bestRelY), out var boatStand))
					{
						ArmorStandUI.Open(boatStand, playerIndex);
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
						}
						else if (stationType == "furnace")
						{
							Program.ActiveStation = "furnace";
							CraftingUI.Toggle("furnace", playerIndex);
						}
						else if (stationType == "anvil")
						{
							Program.ActiveStation = "anvil";
							CraftingUI.Toggle("anvil", playerIndex);
						}
						else if (stationType == "loom")
						{
							Program.ActiveStation = "loom";
							CraftingUI.Toggle("loom", playerIndex);
						}
						else if (stationType == "carpentry")
						{
							Program.ActiveStation = "carpentry";
							CraftingUI.Toggle("carpentry", playerIndex);
						}
						else if (stationType == "cauldron")
						{
							CauldronUI.Open(playerIndex);
						}
						else if (stationType == "mirror")
						{
							CharacterCustomizationUI.Open();
						}
						return true;
					}
					
					// Meuble assoyable sur le bateau
					if (bestTileData.IsSittable)
					{
						Vector2 tileWorldPos = nearbyBoat.GetTileWorldPos(bestRelX, bestRelY);
						float sitWorldX = tileWorldPos.X;
						float sitWorldY = tileWorldPos.Y;
						
						float sitHeightOffset = ts * 0.6f;
						if (bestTileData.Size != null && bestTileData.Size.Height > 1)
							sitHeightOffset = ts * (bestTileData.Size.Height - 0.4f);
						Vector2 boatSitPosition = new Vector2(sitWorldX, sitWorldY);
						if (bestTileData.CollisionSize != null)
							boatSitPosition = GetFurnitureSitPosition(boatSitPosition, bestTileData.CollisionSize.Width, bestTileData.CollisionSize.Height, mouseWorld);
						
						SetSitting(true, boatSitPosition, sitHeightOffset, "sit");
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
				float potDist = Vector2.Distance(GetPlayerFeetPosition(playerPos), new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f));
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
                        float plantDist = Vector2.Distance(GetPlayerFeetPosition(playerPos), new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f));
                        if (plantDist <= MAX_INTERACTION_DISTANCE)
                        {
                            int cropId = heldData.CropId;
                            if (cropId != 0)
                            {
                                World.PlantCrop(tileX, tileY, cropId, GetGameTime());
								SoundEffects.PlayPlant();
                                RemoveItemFromInventory(heldItem.Name, 1);
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
				float cropDist = Vector2.Distance(GetPlayerFeetPosition(playerPos), new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f));
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
                if (heldItemLocal != null && GetItemId(heldItemLocal.Name) == GameData.GetItemId("bucket"))
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
				float dist = Vector2.Distance(GetPlayerFeetPosition(playerPos), new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f));
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
				float portalDist = Vector2.Distance(GetPlayerFeetPosition(playerPos), new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f));
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
				if (NetworkManager.IsClient)
				{
					NetworkManager.RequestDungeonTransition(tileX, tileY);
					return true;
				}

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
					SpawnTileBreakParticles(GetVisualTileCenter(tileX, tileY), new Color(120, 112, 104, 255));

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
				if (moved && NetworkManager.IsHost)
					NetworkManager.BroadcastDungeonTransition();
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

                if (heldWellItem != null && GetItemId(heldWellItem.Name) == GameData.GetItemId("bucket"))
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
    }
}
