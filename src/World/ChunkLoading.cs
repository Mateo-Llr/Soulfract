// ChunkLoading.cs
// Fichier extrait de World.Core.cs — classe partielle World.
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {
		public static void LoadChunkFromSave(ChunkSaveData chunkData)
		{
			var chunksDict = GetChunksDict();
			var chunkKey = (chunkData.ChunkX, chunkData.ChunkY);
			var chunk = new ChunkData
			{
				Objects = new Dictionary<(int x, int y), int>(),
				Overlays = new Dictionary<(int x, int y), int>(),
				Heights = new Dictionary<(int x, int y), int>(),
				Decorations = new Dictionary<(int x, int y), int>(),
				Variations = new Dictionary<(int x, int y), int>(),
				TileMeta = new Dictionary<(int x, int y), Dictionary<string, string>>(),
				IsGenerated = true,
				LastAccessTime = (float)Raylib.GetTime()
			};

			foreach (var kv in chunkData.Objects)
			{
				var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
				chunk.Objects[(x, y)] = kv.Value;
			}

			if (chunkData.Overlays != null)
			{
				foreach (var kv in chunkData.Overlays)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.Overlays[(x, y)] = kv.Value;
				}
			}

			if (chunkData.BuildingIds != null)
			{
				foreach (var kv in chunkData.BuildingIds)
				{
					var (worldX, worldY) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					BuildingIds[(worldX, worldY)] = kv.Value;
				}
			}

			// Heights
			foreach (var kv in chunkData.Heights)
			{
				var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
				chunk.Heights[(x, y)] = kv.Value;
			}

			// Decorations
			foreach (var kv in chunkData.Decorations)
			{
				var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
				chunk.Decorations[(x, y)] = kv.Value;
			}

			// Variations
			foreach (var kv in chunkData.Variations)
			{
				var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
				chunk.Variations[(x, y)] = kv.Value;
			}

			// Roof heights & info
			if (chunkData.RoofHeights != null)
			{
				foreach (var kv in chunkData.RoofHeights)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.RoofHeights[(x, y)] = kv.Value;
				}
			}

			if (chunkData.RoofTileInfo != null)
			{
				foreach (var kv in chunkData.RoofTileInfo)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					if (Enum.TryParse<World.RoofTexKind>(kv.Value.TexKind, true, out var texKind))
					{
						chunk.RoofTileInfo[(x, y)] = new World.RoofTileInfo(
							kv.Value.Height,
							kv.Value.IsHorizontalZone,
							kv.Value.WallStackCount,
							texKind,
							kv.Value.IsColumnAnchor);
					}
				}
			}

			// Wall coverings
			if (chunkData.WallCoverings != null)
			{
				foreach (var kv in chunkData.WallCoverings)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.WallCoverings[(x, y)] = kv.Value;
				}
			}

			// Containers
			if (chunkData.Containers != null)
			{
				foreach (var cont in chunkData.Containers)
				{
					var (x, y) = SaveSystem.UnpackTileKey(cont.Key, chunkData.ChunkX, chunkData.ChunkY);

					int objectId = 0;
					if (chunk.Objects.TryGetValue((x, y), out int foundId))
						objectId = foundId;

					int columns = 8;
					if (objectId != 0)
					{
						var tileData = WorldTileRegistry.GetTile(objectId);
						if (tileData?.IsContainer == true)
							columns = tileData.ContainerColumns > 0 ? tileData.ContainerColumns : 8;
					}

					int slotCount = cont.Value.Slots.Count;
					var containerData = new ContainerInventoryData(slotCount, columns);

					for (int i = 0; i < cont.Value.Slots.Count && i < containerData.Slots.Count; i++)
					{
						var slotSave = cont.Value.Slots[i];
						var slot = containerData.Slots[i];

						if (!slotSave.IsEmpty && !string.IsNullOrEmpty(slotSave.ItemName))
						{
							if (GameData.TryGetItemByName(slotSave.ItemName, out var itemDef))
							{
								var restoredItem = new Item(itemDef.Name, slotSave.Count, itemDef.Color, itemDef.Icon);

								if (slotSave.CustomColors != null && slotSave.CustomColors.Count > 0)
								{
									var colors = new List<Color?>();
									foreach (var colorSave in slotSave.CustomColors)
									{
										if (colorSave != null)
											colors.Add(new Color(colorSave.R, colorSave.G, colorSave.B, colorSave.A));
										else
											colors.Add(null);
									}
									restoredItem.CustomColors = colors;
								}

								slot.Item = restoredItem;
								slot.Count = slotSave.Count;
							}
						}
					}

					chunk.Containers[(x, y)] = containerData;
				}
			}

			// Armor stands
			if (chunkData.ArmorStands != null)
			{
				foreach (var stand in chunkData.ArmorStands)
				{
					var (x, y) = SaveSystem.UnpackTileKey(stand.Key, chunkData.ChunkX, chunkData.ChunkY);
					var standData = new ArmorStandData();
					standData.CurrentPose = stand.Value.CurrentPose ?? "idle";
					standData.Head = RestoreArmorStandItem(stand.Value.Head);
					standData.Body = RestoreArmorStandItem(stand.Value.Body);
					standData.Legs = RestoreArmorStandItem(stand.Value.Legs);
					standData.MainHand = RestoreArmorStandItem(stand.Value.MainHand);
					standData.OffHand = RestoreArmorStandItem(stand.Value.OffHand);
					standData.Face = RestoreArmorStandItem(stand.Value.Face);
					standData.Ears = RestoreArmorStandItem(stand.Value.Ears);
					standData.Neck = RestoreArmorStandItem(stand.Value.Neck);
					standData.Waist = RestoreArmorStandItem(stand.Value.Waist);
					standData.Feet = RestoreArmorStandItem(stand.Value.Feet);
					standData.Back = RestoreArmorStandItem(stand.Value.Back);
					chunk.ArmorStands[(x, y)] = standData;
				}
			}

			// Banner holders
			if (chunkData.BannerHolders != null)
			{
				foreach (var kv in chunkData.BannerHolders)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.BannerHolders[(x, y)] = SaveSystem.RestoreBannerHolder(kv.Value);
				}
			}

			// Ground overrides
			if (chunkData.GroundOverrides != null)
			{
				foreach (var kv in chunkData.GroundOverrides)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.GroundOverrides[(x, y)] = kv.Value;
				}
			}

			// Crops
			if (chunkData.Crops != null)
			{
				foreach (var crop in chunkData.Crops)
				{
					var (x, y) = SaveSystem.UnpackTileKey(crop.Key, chunkData.ChunkX, chunkData.ChunkY);
					chunk.Crops[(x, y)] = new World.CropData
					{
						CropTileId = crop.Value.CropTileId,
						PlantTime = crop.Value.PlantTime
					};
				}
			}

			// Métadonnées de tuile (générique)
			if (chunkData.TileMeta != null)
			{
				foreach (var kv in chunkData.TileMeta)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					if (kv.Value != null && kv.Value.Count > 0)
						chunk.TileMeta[(x, y)] = new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase);
				}
			}

			//  MIGRATION : anciennes ruches sauvegardées comme overlay 6000 (avant le passage
			// à TileMeta) → reconverties en métadonnée "beehive" à leur premier chargement, et
			// retirées d'Overlays pour ne pas être redessinées deux fois / interférer avec le
			// filtre générique d'overlays. Sans effet sur les sauvegardes déjà migrées.
			foreach (var pos in chunk.Overlays.Where(kv => kv.Value == 6000).Select(kv => kv.Key).ToList())
			{
				chunk.SetTileMeta(pos.x, pos.y, "beehive", "1");
				chunk.Overlays.Remove(pos);
			}

			// Aquariums
			if (chunkData.Aquariums != null)
			{
				foreach (var kv in chunkData.Aquariums)
				{
					var (x, y) = SaveSystem.UnpackTileKey(kv.Key, chunkData.ChunkX, chunkData.ChunkY);
					var aquariumData = new AquariumData();
					foreach (var fishSave in kv.Value)
					{
						aquariumData.FishStates[fishSave.SlotIndex] = new AquariumFishState
						{
							SlotIndex = fishSave.SlotIndex,
							LocalPosition = new Vector2(fishSave.LocalPosX, fishSave.LocalPosY),
							TargetPosition = new Vector2(fishSave.TargetPosX, fishSave.TargetPosY),
							MoveSpeed = fishSave.MoveSpeed,
							IsMoving = fishSave.IsMoving,
							IdleTimer = fishSave.IdleTimer,
							BobPhase = fishSave.BobPhase,
							BobSpeed = fishSave.BobSpeed,
							MoveStartTime = fishSave.MoveStartTime,
							MoveDuration = fishSave.MoveDuration,
							BaseY = fishSave.BaseY
						};
					}
					World.SetAquariumData(x, y, aquariumData);
				}
			}

			// Entities
			if (chunkData.Entities != null)
			{
				foreach (var entityData in chunkData.Entities)
				{
					var entity = new Entity(new Vector2(entityData.WorldPosX, entityData.WorldPosY), entityData.Species, false, skipHomeAutoAssign: true);
					entity.Tint = entityData.Tint == null
						? entity.Tint
						: new Color(entityData.Tint.R, entityData.Tint.G, entityData.Tint.B, entityData.Tint.A);
					entity.HairColor = entityData.HairColor == null
						? entity.HairColor
						: new Color(entityData.HairColor.R, entityData.HairColor.G, entityData.HairColor.B, entityData.HairColor.A);
					entity.HairStyle = entityData.HairStyle;
					entity.BeardStyle = entityData.BeardStyle;
					SaveSystem.RestoreRandomFeatures(entity, entityData);
					entity.EnsureHumanEyeColor();
					entity.SetHealthSilently(entityData.CurrentHP, entityData.MaxHP);
					entity.IsBoss = entityData.IsBoss || string.Equals(entityData.Species, "ogre", StringComparison.OrdinalIgnoreCase) || string.Equals(entityData.Species, "khamsin", StringComparison.OrdinalIgnoreCase) || (string.Equals(entityData.Species, "genie", StringComparison.OrdinalIgnoreCase) && !string.Equals(entityData.CustomName, "Illusion du Génie", StringComparison.OrdinalIgnoreCase));
					entity.IsSpiritAnimal = entityData.IsSpiritAnimal;
					entity.SpiritTotemTileX = entityData.SpiritTotemTileX;
					entity.SpiritTotemTileY = entityData.SpiritTotemTileY;
					entity.Behavior = string.IsNullOrWhiteSpace(entityData.Behavior) ? (entity.IsBoss ? "hostile" : "passive") : entityData.Behavior;
					if (entityData.VisionRange.HasValue)
						entity.VisionRange = entityData.VisionRange.Value;
					entity.AlertDuration = entityData.AlertDuration > 0f ? entityData.AlertDuration : (entity.IsBoss ? 2f : 0f);
					entity.IsAlerted = entityData.IsAlerted || entity.IsBoss;
					entity.AlertTimer = entityData.AlertTimer > 0f ? entityData.AlertTimer : (entity.IsBoss ? entity.AlertDuration : 0f);
					entity.IsTamed = entityData.IsTamed && !entity.IsSpiritAnimal;
					entity.IsGuildMember = entityData.IsGuildMember;
					entity.WantsToJoinGuild = entityData.WantsToJoinGuild;
					entity.OwnerName = string.IsNullOrEmpty(entityData.OwnerName) ? null : entityData.OwnerName;
					entity.CustomName = string.IsNullOrEmpty(entityData.CustomName) ? null : entityData.CustomName;
					entity.EnsureGenieTint();
					entity.HomePosition = new Vector2(entityData.HomePosX, entityData.HomePosY);
					//  CORRECTIF : HomeBuildingId n'était jamais restauré à la sauvegarde/chargement
					// (seule HomePosition l'était), alors qu'il reste à sa valeur par défaut -1 posée
					// par le constructeur Entity. Or TOUT le système de dépôt au coffre du soir
					// (routine + filet de sécurité au moment de s'endormir, voir Entity.UpdateSleepState
					// et le bloc "Comportement des villageois") est verrouillé par HomeBuildingId >= 0.
					// Le PNJ rentrait donc bien chez lui après rechargement (GoHome/GetHomeEntryTarget
					// utilisent HomePosition en repli, indépendamment de HomeBuildingId), mais restait
					// ensuite bloqué à attendre indéfiniment sans jamais vider son inventaire dans le
					// coffre, faute de HomeBuildingId valide. On le recalcule ici à partir de la tuile
					// de HomePosition, exactement comme le fait l'assignation normale d'une maison
					// (voir World.FindNearestFreeHouseSpot, qui renvoie toujours les deux ensemble).
					if (entity.HomePosition != Vector2.Zero)
					{
						int homeTileX = (int)(entity.HomePosition.X / Program.TileSize);
						int homeTileY = (int)(entity.HomePosition.Y / Program.TileSize);
						int restoredBuildingId = GetBuildingIdAt(homeTileX, homeTileY);
						entity.HomeBuildingId = restoredBuildingId != 0 ? restoredBuildingId : -1;
					}
					entity.IsTrader = entityData.IsTrader;
					entity.Profession = entityData.Profession >= 0 ? (ProfessionType)entityData.Profession : ProfessionType.None;
					if (entity.Profession == ProfessionType.Guard)
						entity.ConfigureVillageGuardStats();
					else if (entity.IsVillager)
						entity.Attack = SpeciesData.GetSpeciesInfo(entity.Species)?.Attack ?? entity.Attack;
					entity.TraderItems = entityData.TraderItemIds?.ToList() ?? new List<int>();
					entity.IsBaby = entityData.IsBaby;
					entity.Age = entityData.Age;
					entity.GrowthTime = entityData.GrowthTime;
					entity.GestationTimer = entityData.GestationTimer;
					entity.Scale = entityData.Scale;
					entity.IsInterestedInFood = entityData.IsInterestedInFood;
					entity.PreferredFoodId = entityData.PreferredFoodId;
					entity.FollowOrder = entityData.FollowOrder;
					entity.IsTalking = entityData.IsTalking;
					entity.TalkTimer = entityData.TalkTimer;
					entity.TalkCooldown = entityData.TalkCooldown;
					entity.TalkText = string.IsNullOrEmpty(entityData.TalkText) ? null : entityData.TalkText;
				
					if (entityData.Equipment != null)
					{
						entity.Equipment ??= new Equipment();
					}
					if (entity.Profession == ProfessionType.Guard)
						entity.EquipVillageGuard();
				
					if (entityData.InventorySlots != null && entityData.InventorySlots.Count > 0)
					{
						entity.Inventory = new ContainerInventoryData(entityData.InventorySlots.Count, 4);
						for (int i = 0; i < entityData.InventorySlots.Count && i < entity.Inventory.Slots.Count; i++)
							SaveSystem.RestoreInventorySlotFromSave(entityData.InventorySlots[i], entity.Inventory.Slots[i]);
					}
				
					if (entity.Profession == ProfessionType.Guard)
					{
						entity.IsTrader = false;
						entity.TraderItems.Clear();
					}
					else if (entity.Profession != ProfessionType.None || entityData.IsTrader || entity.TraderItems.Count > 0)
					{
						entity.IsTrader = true;
						if (entity.Profession == ProfessionType.None)
							entity.AssignProfessionAndTrade();
						else
							entity.InitTraderItems();
					}
				
					if (entityData.TamedBehavior >= 0 && entityData.TamedBehavior <= 3)
						entity.TamedBehavior = (TamedAnimalMode)entityData.TamedBehavior;
				
					if (entity.IsTamed)
						entity.Behavior = "tamed";
				
					chunk.Entities.Add(entity);
				}
			}

			// Finally, register the chunk in the world dictionary
			chunksDict[chunkKey] = chunk;

			//  BUG CORRIGÉ (toits mal affichés en multijoueur / après rechargement d'une
			// sauvegarde) : chunk.RoofTileInfo ci-dessus ne contient QUE la copie locale au
			// chunk, désérialisée depuis le réseau ou le fichier de sauvegarde. Mais le
			// rendu (TryGetRoofTileInfo, dans World_Core.DrawWorld) lit un dictionnaire
			// GLOBAL statique (_roofTileInfo dans World_Houses.cs), rempli normalement par
			// ApplyRoofToHouse au moment de la construction. Ce dictionnaire global n'était
			// jamais synchronisé avec les chunks chargés depuis le réseau ou une sauvegarde :
			// TryGetRoofTileInfo échouait donc systématiquement pour toute maison que le
			// joueur/client n'avait pas construite lui-même dans la session en cours, et le
			// rendu retombait sur RoofTexKind.Default (toit plat) au lieu de la vraie forme
			// en pente. On restaure donc explicitement l'info dans le dictionnaire global
			// juste après l'enregistrement du chunk.
			RestoreRoofTileInfoForChunk(chunk);
        }

		private static Item? RestoreArmorStandItem(string? itemName)
		{
			if (string.IsNullOrEmpty(itemName) || !GameData.TryGetItemByName(itemName, out var itemData))
				return null;

			return new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
		}

    }
}
