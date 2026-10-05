// SaveSystem.cs
#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Raylib_cs;
using System.Numerics;
using System.Linq;
using System.IO.Compression;

namespace Soulfract
{
    public static class SaveSystem
    {
        private const string SAVE_DIR = "Saves";
        private const string SAVES_INDEX = "saves_index.json";
        private const int CHUNK_STORE_MAGIC = 0x53464348;
        private const int CHUNK_STORE_VERSION = 1;
        private const int CHUNK_STORE_HEADER_SIZE = 8;
        private const int CHUNK_RECORD_HEADER_SIZE = 12;
        private const int MAX_CHUNK_RECORD_SIZE = 256 * 1024 * 1024;
        private static readonly object ChunkStoreLock = new();
        private static readonly Dictionary<string, ChunkStore> ChunkStores = new(StringComparer.OrdinalIgnoreCase);

        private sealed class ChunkStore
        {
            public Dictionary<(int x, int y), ChunkRecord> Chunks { get; } = new();
            public long Length { get; set; }
            public long LiveBytes { get; set; }
        }

        private readonly record struct ChunkRecord(long PayloadOffset, int PayloadLength);

        //  OPTIMISATION POIDS DES SAUVEGARDES : une seule instance d'options partagée,
        // au lieu d'en recréer une par appel. Pas d'indentation (les JSON de sauvegarde ne
        // sont jamais lus à la main, l'indentation ne fait que gonfler le fichier).
        //  IMPORTANT : PAS de DefaultIgnoreCondition ici. Beaucoup de classes de ce fichier
        // ont des propriétés dont la valeur par défaut "logique" n'est PAS le défaut CLR
        // (ex : InventorySlotSave.IsEmpty = true, BannerHolderSaveData.HasBanner = true,
        // WeaponsBreak/FoodSpoils = true). Omettre les valeurs "par défaut CLR" (false/0/null)
        // aurait fait disparaître ces propriétés du JSON dès qu'elles valent false/0 — qui est
        // pourtant un état parfaitement valide (objet dans un slot, WeaponsBreak désactivé...)
        // — et elles seraient revenues à leur valeur initiale (true) au rechargement. C'est
        // exactement ce qui a fait disparaître l'inventaire : un slot occupé (IsEmpty=false)
        // n'était plus écrit, et rechargeait IsEmpty=true (= slot vide). Ne jamais réactiver
        // cette option sans auditer un par un chaque type sérialisé par SaveSystem.
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static bool IsValidSaveName(string? saveName)
        {
            if (string.IsNullOrWhiteSpace(saveName) || saveName is "." or ".."
                || saveName.EndsWith(' ') || saveName.EndsWith('.'))
                return false;

            return !saveName.Any(Path.GetInvalidFileNameChars().Contains);
        }

        //  Les dictionnaires de tuiles de World.ChunkData (Objects, Overlays, Heights, ...) sont
        // indexés par des coordonnées MONDE (voir World.AddOverlay, etc.), pas par des coordonnées
        // locales au chunk. On les convertit en local (0 <= local < CHUNK_SIZE) avant de les
        // empaqueter en un seul entier, pour rester compact ET sans collision entre chunks.
        // Publiques : réutilisées par World.Core.cs (chargement réseau des chunks côté client).
        public static int PackTileKey(int worldX, int worldY, int chunkX, int chunkY)
        {
            int localX = worldX - chunkX * World.CHUNK_SIZE;
            int localY = worldY - chunkY * World.CHUNK_SIZE;
            return localX * World.CHUNK_SIZE + localY;
        }

        public static (int x, int y) UnpackTileKey(int key, int chunkX, int chunkY)
        {
            int localX = key / World.CHUNK_SIZE;
            int localY = key % World.CHUNK_SIZE;
            return (chunkX * World.CHUNK_SIZE + localX, chunkY * World.CHUNK_SIZE + localY);
        }

        private static ColorSave SaveColor(Color color)
        {
            return new ColorSave { R = color.R, G = color.G, B = color.B, A = color.A };
        }

        private static Color RestoreColor(ColorSave? color, Color fallback)
        {
            return color == null ? fallback : new Color(color.R, color.G, color.B, color.A);
        }

        //  SYSTÈME DE QUÊTES : reconstruit NetId, prénom et quête active d'un PNJ depuis la sauvegarde.
        private static void RestoreQuestData(Entity entity, EntitySaveData entityData)
        {
            if (!string.IsNullOrEmpty(entityData.NetId) && Guid.TryParse(entityData.NetId, out var netId))
                entity.NetId = netId;

            entity.FirstName = string.IsNullOrEmpty(entityData.FirstName) ? null : entityData.FirstName;

            if (entityData.HasActiveQuest && !string.IsNullOrEmpty(entityData.QuestTargetNetId)
                && Guid.TryParse(entityData.QuestTargetNetId, out var targetId))
            {
                var restoredQuest = new Quest
                {
                    Type = entityData.QuestType == 1 ? QuestType.TameAndBring : QuestType.DeliverItem,
                    GiverId = entity.NetId,
                    TargetId = targetId,
                    ItemId = entityData.QuestItemId,
                    ItemName = entityData.QuestItemName,
                    ItemQty = entityData.QuestItemQty,
                    TargetSpecies = entityData.QuestTargetSpecies ?? "",
                    RewardItemId = entityData.QuestRewardItemId,
                    RewardItemName = entityData.QuestRewardItemName,
                    RewardItemQty = entityData.QuestRewardItemQty,
                    LastKnownTargetPosX = entityData.QuestTargetPosX,
                    LastKnownTargetPosY = entityData.QuestTargetPosY,
                    HasLastKnownTargetPosition = entityData.QuestHasTargetPos,
                    State = entityData.QuestState switch
                    {
                        2 => QuestState.Delivered,
                        1 => QuestState.Accepted,
                        _ => QuestState.Offered
                    }
                };
                QuestManager.NormalizeQuestItemData(restoredQuest);
                entity.ActiveQuest = restoredQuest;
                QuestManager.RegisterQuest(restoredQuest);
            }
        }


        public static void RestoreRandomFeatures(Entity entity, EntitySaveData entityData)
        {
            if (entityData.RandomFeatureVariants != null)
            {
                foreach (var feature in entityData.RandomFeatureVariants)
                    entity.RandomFeatureVariant[feature.Key] = feature.Value;
            }

            if (entityData.RandomFeatureColors != null)
            {
                foreach (var feature in entityData.RandomFeatureColors)
                    entity.RandomFeatureColor[feature.Key] = RestoreColor(feature.Value, Color.White);
            }

            var speciesInfo = SpeciesData.GetSpeciesInfo(entity.Species);
            var matchingPreset = speciesInfo?.ColorPresets.FirstOrDefault(preset =>
                MatchesExactPresetColor(entity.Tint, preset.ColorMin, preset.ColorMax) &&
                preset.Features.Any(feature =>
                    entity.RandomFeatureColor.TryGetValue(feature.Key, out var color) &&
                    MatchesExactPresetColor(color, feature.Value.ColorMin, feature.Value.ColorMax)));
            if (matchingPreset == null) return;

            foreach (var feature in matchingPreset.Features)
            {
                if (feature.Value.ForceVariant && feature.Value.Variant >= 0)
                    entity.RandomFeatureVariant[feature.Key] = feature.Value.Variant;
            }
        }

        private static bool MatchesExactPresetColor(Color actual, Color? min, Color? max)
        {
            return min.HasValue && max.HasValue &&
                min.Value.R == max.Value.R && min.Value.G == max.Value.G && min.Value.B == max.Value.B &&
                actual.R == min.Value.R && actual.G == min.Value.G && actual.B == min.Value.B;
        }

        private static Entity RestoreEntityFromSave(EntitySaveData entityData, List<Entity> globalEntities)
        {
            var entity = new Entity(new Vector2(entityData.WorldPosX, entityData.WorldPosY), entityData.Species, false, skipHomeAutoAssign: true);
            entity.Tint = RestoreColor(entityData.Tint, entity.Tint);
            entity.HairColor = RestoreColor(entityData.HairColor, entity.HairColor);
            entity.HairStyle = entityData.HairStyle;
            entity.BeardStyle = entityData.BeardStyle;
            RestoreRandomFeatures(entity, entityData);
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
            entity.OwnerNpcId = (!string.IsNullOrEmpty(entityData.OwnerNpcId) && Guid.TryParse(entityData.OwnerNpcId, out var ownerNpcId))
                ? ownerNpcId
                : (Guid?)null;
            //  OwnedMount lui-même (la référence Entity) est résolu plus tard, une fois toutes
            // les entités chargées, via Entity.ResolveOwnedMountReference (même logique que la
            // résolution existante d'OwnerNpcId côté Program.cs).
            entity.OwnedMountNetId = (!string.IsNullOrEmpty(entityData.OwnedMountNetId) && Guid.TryParse(entityData.OwnedMountNetId, out var ownedMountNetId))
                ? ownedMountNetId
                : (Guid?)null;
            entity.IsRidingMount = entityData.IsRidingMount;
            entity.CustomName = string.IsNullOrEmpty(entityData.CustomName) ? null : entityData.CustomName;
            entity.HomePosition = new Vector2(entityData.HomePosX, entityData.HomePosY);
            entity.HomeBuildingId = entityData.HomeBuildingId;
            //  BUGFIX (rechargement de sauvegarde) : à la construction ci-dessus, HomePosition
            // valait encore Vector2.Zero, donc Decide() a pu poser un AiState/AiTarget "par
            // défaut" (wander/idle) qui n'a plus de sens maintenant que le VRAI foyer vient d'être
            // restauré. On force un état neutre : la logique normale (Update -> isAwayFromHome ->
            // GoHome) reprendra la main dès la prochaine frame avec les bonnes informations, sans
            // qu'aucun trajet périmé ne subsiste.
            entity.AiState = NpcAiState.Idle;
            entity.AiTarget = entity.WorldPos;
            entity.Hunger = entityData.Hunger;
            entity.WorkplacePosition = entityData.WorkplacePosX.HasValue && entityData.WorkplacePosY.HasValue
                ? new Vector2(entityData.WorkplacePosX.Value, entityData.WorkplacePosY.Value)
                : null;
            entity.WorkZoneId = entityData.WorkZoneId;
            entity.IsTrader = entityData.IsTrader;
            entity.Profession = entityData.Profession >= 0 ? (ProfessionType)entityData.Profession : ProfessionType.None;
            if (entity.Profession == ProfessionType.Guard)
                entity.ConfigureVillageGuardStats();
            else if (entity.IsVillager)
                entity.Attack = SpeciesData.GetSpeciesInfo(entity.Species)?.Attack ?? entity.Attack;
            entity.TraderItems = entityData.TraderItemIds?.ToList() ?? new List<int>();
            entity.Friendship = entityData.Friendship;
            entity.IsBaby = entityData.IsBaby;
            entity.Age = entityData.Age;
            entity.GrowthTime = entityData.GrowthTime;
            entity.GestationTimer = entityData.GestationTimer;
            entity.Scale = entityData.Scale;
            entity.SlimeIncubationSeconds = entityData.SlimeIncubationSeconds;
            entity.IsInterestedInFood = entityData.IsInterestedInFood;
            entity.PreferredFoodId = entityData.PreferredFoodId;
            entity.FollowOrder = entityData.FollowOrder;
            entity.IsTalking = entityData.IsTalking;
            entity.TalkTimer = entityData.TalkTimer;
            entity.TalkCooldown = entityData.TalkCooldown;
            entity.TalkText = string.IsNullOrEmpty(entityData.TalkText) ? null : entityData.TalkText;

            RestoreQuestData(entity, entityData);

            if (entityData.Equipment != null)
            {
                entity.Equipment ??= new Equipment();
                RestoreEquipmentFromSave(entity.Equipment, entityData.Equipment);
            }
            else if (entity.Profession == ProfessionType.Guard)
            {
                entity.EquipVillageGuard();
            }

            if (entityData.InventorySlots != null && entityData.InventorySlots.Count > 0)
            {
                entity.Inventory = new ContainerInventoryData(entityData.InventorySlots.Count, 4);
                for (int i = 0; i < entityData.InventorySlots.Count && i < entity.Inventory.Slots.Count; i++)
                    RestoreInventorySlotFromSave(entityData.InventorySlots[i], entity.Inventory.Slots[i]);
            }

            if (entity.Profession == ProfessionType.Guard)
            {
                entity.IsTrader = false;
                entity.TraderItems.Clear();
            }
            else if (entity.Profession != ProfessionType.None || entityData.IsTrader || entity.TraderItems.Count > 0)
            {
                entity.IsTrader = true;
                if (entity.TraderItems.Count == 0)
                    entity.AssignProfessionAndTrade();
            }

            if (entityData.TamedBehavior >= 0 && entityData.TamedBehavior <= 3)
                entity.TamedBehavior = (TamedAnimalMode)entityData.TamedBehavior;

            if (entity.IsTamed)
                entity.Behavior = "tamed";

            globalEntities.Add(entity);

            int tileX = (int)(entity.WorldPos.X / Program.TileSize);
            int tileY = (int)(entity.WorldPos.Y / Program.TileSize);
            var ownerChunk = World.GetChunkAt(tileX, tileY);
            if (ownerChunk != null)
            {
                bool alreadyInChunk = ownerChunk.Entities.Any(e => e.NetId == entity.NetId);
                if (!alreadyInChunk)
                    ownerChunk.Entities.Add(entity);
            }

            if (entityData.Pets != null && entityData.Pets.Count > 0)
            {
                foreach (var petData in entityData.Pets)
                {
                    var pet = RestoreEntityFromSave(petData, globalEntities);
                    entity.Pets.Add(pet);
                }
            }

            return entity;
        }

        public static void SyncItemRuneMetadata(Item? item)
        {
            if (item == null) return;
            if (item.SocketedRunes.Count > 0 || (!string.IsNullOrEmpty(item.Metadata) && item.Metadata.StartsWith("RUNES:", StringComparison.Ordinal)))
                item.SyncRunesToMetadata();
        }

        private static List<RuneSaveData?> SaveRunes(Item? item)
        {
            if (item == null) return new List<RuneSaveData?>();
            SyncItemRuneMetadata(item);
            return item.SocketedRunes.Select(r => r == null ? null : new RuneSaveData { Type = r.Type, Value = r.Value }).ToList();
        }

        public static void RestoreRunes(Item item, int runeSlotCount, List<RuneSaveData?>? runes, string? metadata)
        {
            item.EnsureRuneSlots(runeSlotCount);

            if (runes != null && runes.Count > 0)
            {
                item.SocketedRunes = runes.Select(r => r == null ? (RuneData?)null : new RuneData { Type = r.Type, Value = r.Value }).ToList();
                item.EnsureRuneSlots(Math.Max(runeSlotCount, item.SocketedRunes.Count));
                item.SyncRunesToMetadata();
                return;
            }

            if (!string.IsNullOrEmpty(metadata) && metadata.StartsWith("RUNES:", StringComparison.Ordinal))
            {
                item.LoadRunesFromMetadata();
                return;
            }

            item.SocketedRunes.Clear();
            item.EnsureRuneSlots(runeSlotCount);
        }

        public static InventorySlotSave CreateInventorySlotSave(InventorySlot? slot)
        {
            if (slot?.Item != null)
                SyncItemRuneMetadata(slot.Item);

            return new InventorySlotSave
            {
                ItemName = slot?.Item?.Name ?? "",
                Metadata = slot?.Item != null ? slot.Item.SerializeMetadataForSave() : "",
                Runes = SaveRunes(slot?.Item),
                Count = slot?.Count ?? 0,
                IsEmpty = slot == null || slot.IsEmpty,
                CustomColors = SaveCustomColors(slot?.Item?.CustomColors)
            };
        }

        public static void RestoreCoatVariantMeta(Item item, ItemData itemData)
        {
            if (item == null) return;

            bool isCoat = string.Equals(itemData.Key, "coat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemData.TextureName, "coat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemData.Name, "coat", StringComparison.OrdinalIgnoreCase);
            if (!isCoat) return;

            // Normaliser les métadonnées de variante : ne FORCER une valeur par défaut
            // que si aucune n'est fournie. Le défaut canonique du manteau est "short"
            // pour rester cohérent avec le sélecteur d'artisanat et les assets associés.
            if (item.Meta.TryGetValue("sleeves", out var legacySleeves))
            {
                item.SetMetadataValue("sleeves", legacySleeves);
                item.Meta.Remove("sleeves");
            }
            if (item.Meta.TryGetValue("opening", out var legacyOpening))
            {
                item.SetMetadataValue("opening", legacyOpening);
                item.Meta.Remove("opening");
            }

            string sleeveVal = item.GetMetadataValue("sleeves");
            if (!string.Equals(sleeveVal, "short", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(sleeveVal, "long", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(sleeveVal, "none", StringComparison.OrdinalIgnoreCase))
                item.SetMetadataValue("sleeves", "short");

            string openingVal = item.GetMetadataValue("opening");
            if (!string.Equals(openingVal, "open", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(openingVal, "closed", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(openingVal, "lace_up", StringComparison.OrdinalIgnoreCase))
                item.SetMetadataValue("opening", "closed");
        }

        public static void RestoreInventorySlotFromSave(InventorySlotSave save, InventorySlot target)
        {
            if (string.IsNullOrEmpty(save.ItemName) || save.IsEmpty)
            {
                target.Clear();
                return;
            }

            var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == save.ItemName);
            if (itemData.ID == 0)
            {
                target.Clear();
                return;
            }

            var item = new Item(itemData.Name, save.Count, itemData.Color, itemData.Icon);
            item.RestoreMetadataFromSave(save.Metadata ?? "");
            if (save.Meta != null && save.Meta.Count > 0)
                foreach (var kv in save.Meta)
                    item.Meta[kv.Key] = kv.Value;
            RestoreCoatVariantMeta(item, itemData);
            if (itemData.RuneSlots > 0)
            {
                RestoreRunes(item, itemData.RuneSlots, save.Runes, save.Metadata);
            }

            var loadedColors = LoadCustomColors(save.CustomColors);
            if (loadedColors != null && loadedColors.Count > 0)
                item.CustomColors = loadedColors;

            target.Item = item;
            target.Count = save.Count;
        }

        public static EquipmentSave CreateEquipmentSave(Equipment? equipment)
        {
            var save = new EquipmentSave();
            if (equipment == null) return save;

            save.ZoneItems = equipment.ZoneItems
                .Where(item => item != null)
                .Select(item =>
                {
                    SyncItemRuneMetadata(item);
                    return new ZoneItemSave
                    {
                        ItemName = item!.Name,
                        CustomColors = SaveCustomColors(item.CustomColors),
                        Metadata = item.SerializeMetadataForSave(),
                        Runes = SaveRunes(item)
                    };
                }).ToList();

            if (equipment.MainHand != null)
            {
                SyncItemRuneMetadata(equipment.MainHand);
                save.MainHand = equipment.MainHand.Name;
                save.MainHandMetadata = equipment.MainHand.SerializeMetadataForSave();
                save.MainHandRunes = SaveRunes(equipment.MainHand);
            }
            else
            {
                save.MainHand = "";
            }

            if (equipment.OffHand != null)
            {
                SyncItemRuneMetadata(equipment.OffHand);
                save.OffHand = equipment.OffHand.Name;
                save.OffHandMetadata = equipment.OffHand.SerializeMetadataForSave();
                save.OffHandRunes = SaveRunes(equipment.OffHand);
            }
            else
            {
                save.OffHand = "";
            }

            if (equipment.Backpack != null)
            {
                SyncItemRuneMetadata(equipment.Backpack);
                save.Backpack = equipment.Backpack.Name;
                save.BackpackMetadata = equipment.Backpack.SerializeMetadataForSave();
                save.BackpackRunes = SaveRunes(equipment.Backpack);
            }
            else
            {
                save.Backpack = "";
            }

            save.MainHandCustomColors = SaveCustomColors(equipment.MainHand?.CustomColors);
            save.OffHandCustomColors = SaveCustomColors(equipment.OffHand?.CustomColors);
            save.BackpackCustomColors = SaveCustomColors(equipment.Backpack?.CustomColors);

            foreach (var kv in equipment.EquippedItems)
            {
                if (kv.Value == null) continue;
                SyncItemRuneMetadata(kv.Value);
                save.EquippedItems.Add(new EquippedItemSave
                {
                    Slot = kv.Key.slot,
                    Index = kv.Key.index,
                    ItemName = kv.Value.Name,
                    Count = 1,
                    CustomColors = SaveCustomColors(kv.Value.CustomColors),
                    Metadata = kv.Value.SerializeMetadataForSave(),
                    Runes = SaveRunes(kv.Value)
                });
            }

            if (equipment.OffHandContainer != null)
            {
                save.OffHandContainer = new PortableContainerSaveData
                {
                    Columns = equipment.OffHandContainer.Inventory.Columns,
                    Rows = equipment.OffHandContainer.Inventory.Rows,
                    Slots = equipment.OffHandContainer.Inventory.Slots.Select(CreateInventorySlotSave).ToList()
                };
            }

            if (equipment.BackpackContainer != null)
            {
                save.BackpackContainer = new BackpackSaveData
                {
                    Columns = equipment.BackpackContainer.Inventory.Columns,
                    Rows = equipment.BackpackContainer.Inventory.Rows,
                    Slots = equipment.BackpackContainer.Inventory.Slots.Select(CreateInventorySlotSave).ToList()
                };
            }

            return save;
        }

        public static void RestoreEquipmentFromSave(Equipment equipment, EquipmentSave saveData)
        {
            equipment.ClearAllEquipment();

            foreach (var zoneSave in saveData.ZoneItems ?? new List<ZoneItemSave>())
            {
                if (string.IsNullOrEmpty(zoneSave.ItemName)) continue;
                var itemDef = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == zoneSave.ItemName);
                if (itemDef.ID == 0) continue;

                var zoneItem = new Item(itemDef.Name, 1, itemDef.Color, itemDef.Icon);
                var loadedColors = LoadCustomColors(zoneSave.CustomColors);
                if (loadedColors != null && loadedColors.Count > 0)
                    zoneItem.CustomColors = loadedColors;
                zoneItem.RestoreMetadataFromSave(zoneSave.Metadata ?? "");
                if (zoneSave.Meta != null && zoneSave.Meta.Count > 0)
                    foreach (var kv in zoneSave.Meta)
                        zoneItem.Meta[kv.Key] = kv.Value;
                RestoreCoatVariantMeta(zoneItem, itemDef);
                if (itemDef.RuneSlots > 0)
                {
                    RestoreRunes(zoneItem, itemDef.RuneSlots, zoneSave.Runes, zoneSave.Metadata);
                }

                equipment.EquipOnBody(zoneItem, out _);
            }

            if (!string.IsNullOrEmpty(saveData.MainHand))
            {
                var itemDef = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == saveData.MainHand);
                if (itemDef.ID != 0)
                {
                    var item = new Item(itemDef.Name, 1, itemDef.Color, itemDef.Icon);
                    item.RestoreMetadataFromSave(saveData.MainHandMetadata ?? "");
                    var loadedColors = LoadCustomColors(saveData.MainHandCustomColors);
                    if (loadedColors != null && loadedColors.Count > 0)
                        item.CustomColors = loadedColors;
                    if (saveData.MainHandMeta != null && saveData.MainHandMeta.Count > 0)
                        foreach (var kv in saveData.MainHandMeta)
                            item.Meta[kv.Key] = kv.Value;
                    RestoreCoatVariantMeta(item, itemDef);
                    if (itemDef.RuneSlots > 0)
                        RestoreRunes(item, itemDef.RuneSlots, saveData.MainHandRunes, saveData.MainHandMetadata);
                    equipment.EquipItem(item, EquipmentSlot.MainHand);
                }
            }

            if (!string.IsNullOrEmpty(saveData.OffHand))
            {
                var itemDef = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == saveData.OffHand);
                if (itemDef.ID != 0)
                {
                    var item = new Item(itemDef.Name, 1, itemDef.Color, itemDef.Icon);
                    item.RestoreMetadataFromSave(saveData.OffHandMetadata ?? "");
                    var loadedColors = LoadCustomColors(saveData.OffHandCustomColors);
                    if (loadedColors != null && loadedColors.Count > 0)
                        item.CustomColors = loadedColors;
                    if (saveData.OffHandMeta != null && saveData.OffHandMeta.Count > 0)
                        foreach (var kv in saveData.OffHandMeta)
                            item.Meta[kv.Key] = kv.Value;
                    RestoreCoatVariantMeta(item, itemDef);
                    if (itemDef.RuneSlots > 0)
                        RestoreRunes(item, itemDef.RuneSlots, saveData.OffHandRunes, saveData.OffHandMetadata);
                    equipment.EquipItem(item, EquipmentSlot.OffHand);
                }
            }

            if (!string.IsNullOrEmpty(saveData.Backpack))
            {
                var itemDef = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == saveData.Backpack);
                if (itemDef.ID != 0)
                {
                    var item = new Item(itemDef.Name, 1, itemDef.Color, itemDef.Icon);
                    item.RestoreMetadataFromSave(saveData.BackpackMetadata ?? "");
                    var loadedColors = LoadCustomColors(saveData.BackpackCustomColors);
                    if (loadedColors != null && loadedColors.Count > 0)
                        item.CustomColors = loadedColors;
                    if (saveData.BackpackMeta != null && saveData.BackpackMeta.Count > 0)
                        foreach (var kv in saveData.BackpackMeta)
                            item.Meta[kv.Key] = kv.Value;
                    RestoreCoatVariantMeta(item, itemDef);
                    if (itemDef.RuneSlots > 0)
                        RestoreRunes(item, itemDef.RuneSlots, saveData.BackpackRunes, saveData.BackpackMetadata);
                    equipment.EquipItem(item, EquipmentSlot.Backpack);
                }
            }

            if (saveData.BackpackContainer != null)
            {
                int slotCount = saveData.BackpackContainer.Slots.Count;
                int columns = saveData.BackpackContainer.Columns > 0 ? saveData.BackpackContainer.Columns : 5;
                var backpack = new BackpackData(slotCount, columns);
                for (int i = 0; i < saveData.BackpackContainer.Slots.Count && i < backpack.Inventory.Slots.Count; i++)
                {
                    RestoreInventorySlotFromSave(saveData.BackpackContainer.Slots[i], backpack.Inventory.Slots[i]);
                }
                equipment.BackpackContainer = backpack;
                if (equipment.Backpack != null)
                    equipment.Backpack.Backpack = backpack;
            }

            foreach (var equippedSave in saveData.EquippedItems ?? new List<EquippedItemSave>())
            {
                if (string.IsNullOrEmpty(equippedSave.ItemName)) continue;
                var itemDef = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == equippedSave.ItemName);
                if (itemDef.ID == 0) continue;

                var item = new Item(itemDef.Name, equippedSave.Count, itemDef.Color, itemDef.Icon);
                var loadedColors = LoadCustomColors(equippedSave.CustomColors);
                if (loadedColors != null && loadedColors.Count > 0)
                    item.CustomColors = loadedColors;
                item.RestoreMetadataFromSave(equippedSave.Metadata ?? "");
                if (equippedSave.Meta != null && equippedSave.Meta.Count > 0)
                    foreach (var kv in equippedSave.Meta)
                        item.Meta[kv.Key] = kv.Value;
                RestoreCoatVariantMeta(item, itemDef);
                if (itemDef.RuneSlots > 0)
                {
                    RestoreRunes(item, itemDef.RuneSlots, equippedSave.Runes, equippedSave.Metadata);
                }

                equipment.EquipItem(item, equippedSave.Slot, equippedSave.Index);
            }

            //  Doit se faire APRÈS la boucle EquippedItems ci-dessus : EquipItem(...,
            // EquipmentSlot.OffHand, ...) réassigne equipment.OffHand avec un item neuf et
            // appelle EnsureOffHandContainer(), qui écraserait sinon le conteneur restauré
            // par un conteneur vide (contenu du panier perdu au rechargement).
            if (saveData.OffHandContainer != null)
            {
                int slotCount = saveData.OffHandContainer.Slots.Count;
                int columns = saveData.OffHandContainer.Columns > 0 ? saveData.OffHandContainer.Columns : 3;
                var container = new PortableContainerData(slotCount, columns);
                for (int i = 0; i < saveData.OffHandContainer.Slots.Count && i < container.Inventory.Slots.Count; i++)
                {
                    RestoreInventorySlotFromSave(saveData.OffHandContainer.Slots[i], container.Inventory.Slots[i]);
                }
                equipment.OffHandContainer = container;
                if (equipment.OffHand != null)
                    equipment.OffHand.Container = container;
            }

            equipment.LoadEquipmentTextures();
        }

        public static void MergeContainerStacksAfterRestore(ContainerInventoryData container)
        {
            //  Fusion des piles d'items de même nom et même couleur dans un conteneur.
            // Après la restauration, des items du même type peuvent être séparés en
            // plusieurs slots, ce qui ne doit pas se produire.
            for (int i = 0; i < container.Slots.Count; i++)
            {
                var slot = container.Slots[i];
                if (slot.IsEmpty || slot.Item == null) continue;

                //  Chercher les autres piles du même item
                for (int j = i + 1; j < container.Slots.Count; j++)
                {
                    var otherSlot = container.Slots[j];
                    if (otherSlot.IsEmpty || otherSlot.Item == null) continue;
                    
                    //  Même nom et même couleur ?
                    if (Program.AreItemsStackable(slot.Item, otherSlot.Item))
                    {
                        //  Fusionner
                        slot.Count += otherSlot.Count;
                        otherSlot.Clear();
                    }
                }
            }

            //  Compacter les slots vides
            var compacted = container.Slots.Where(s => !s.IsEmpty).ToList();
            while (compacted.Count < container.Slots.Count)
                compacted.Add(new InventorySlot());
            container.Slots = compacted;
        }

        private static bool AreColorsCompatible(Item a, Item b)
        {
            //  Deux items sont "compatibles de couleur" s'ils ont exactement la même liste
            // de CustomColors. Cela inclut le cas où les deux n'ont aucune couleur perso.
            if (a.CustomColors == null && b.CustomColors == null) return true;
            if (a.CustomColors == null || b.CustomColors == null) return false;
            if (a.CustomColors.Count != b.CustomColors.Count) return false;
            
            for (int i = 0; i < a.CustomColors.Count; i++)
            {
                Color? colA = a.CustomColors[i];
                Color? colB = b.CustomColors[i];
                if (colA == null && colB == null) continue;
                if (colA == null || colB == null) return false;
                if (!colA.Value.Equals(colB.Value)) return false;
            }
            return true;
        }
        
        public static List<SaveInfo> GetSaves()
        {
            if (!Directory.Exists(SAVE_DIR))
                Directory.CreateDirectory(SAVE_DIR);
            
            string indexPath = Path.Combine(SAVE_DIR, SAVES_INDEX);
            if (!File.Exists(indexPath))
                return new List<SaveInfo>();
            
            string json = File.ReadAllText(indexPath);
            var saves = JsonSerializer.Deserialize<List<SaveInfo>>(json, ReadOptions) ?? new List<SaveInfo>();
            return saves
                .OrderByDescending(s => s.LastPlayed)
                .ThenByDescending(s => s.CreatedAt)
                .ToList();
        }
        
        public static void SaveGame(string saveName, GameSaveData data)
        {
            if (!IsValidSaveName(saveName))
                throw new ArgumentException("Nom de sauvegarde invalide.", nameof(saveName));

            if (!Directory.Exists(SAVE_DIR))
                Directory.CreateDirectory(SAVE_DIR);
            
            string savePath = Path.Combine(SAVE_DIR, $"{saveName}.json");
            
            // Nettoyer les données avant sérialisation
            CleanGameData(data);
            
            AchievementManager.Save(data);
            var boats = World.GetAllBoats();
            data.Boats.Clear();
            foreach (var boat in boats.Values)
            {
                var boatSave = new BoatSaveData
                {
                    Id = boat.Id.ToString(),
                    PosX = boat.Position.X,
                    PosY = boat.Position.Y,
                    VelX = boat.Velocity.X,
                    VelY = boat.Velocity.Y,
                    Angle = boat.Angle,
                    RowingCooldown = boat.RowingCooldown,
                    GroundTileId = boat.GroundTileId,
                    SailAngle = boat.SailAngle,
                    IsSailAdjusted = boat.IsSailAdjusted,
                    RelX = new List<int>(),
                    RelY = new List<int>(),
                    PlacedRelX = new List<int>(),
                    PlacedRelY = new List<int>(),
                    PlacedObjectIds = new List<int>(),
                    ContainerRelX = new List<int>(),
                    ContainerRelY = new List<int>(),
                    ContainerData = new List<ContainerInventorySave>(),
                    ArmorStandRelX = new List<int>(),
                    ArmorStandRelY = new List<int>(),
                    ArmorStandData = new List<ArmorStandSaveData>()
                };
                
                foreach (var (relX, relY) in boat.Tiles.Keys)
                {
                    boatSave.RelX.Add(relX);
                    boatSave.RelY.Add(relY);
                }
                
                foreach (var ((relX, relY), objId) in boat.PlacedObjects)
                {
                    boatSave.PlacedRelX.Add(relX);
                    boatSave.PlacedRelY.Add(relY);
                    boatSave.PlacedObjectIds.Add(objId);
                }
                
                foreach (var ((relX, relY), container) in boat.Containers)
                {
                    boatSave.ContainerRelX.Add(relX);
                    boatSave.ContainerRelY.Add(relY);
                    var containerSave = new ContainerInventorySave();
                    foreach (var slot in container.Slots)
                    {
                        containerSave.Slots.Add(new InventorySlotSave
                        {
                            ItemName = slot.Item?.Name ?? "",
                            Metadata = slot.Item?.Metadata ?? "",
                            Count = slot.Count,
                            IsEmpty = slot.IsEmpty,
                            CustomColors = SaveCustomColors(slot.Item?.CustomColors)
                        });
                    }
                    boatSave.ContainerData.Add(containerSave);
                }
                
                foreach (var ((relX, relY), stand) in boat.ArmorStands)
                {
                    boatSave.ArmorStandRelX.Add(relX);
                    boatSave.ArmorStandRelY.Add(relY);
                    boatSave.ArmorStandData.Add(new ArmorStandSaveData
                    {
                        Head = stand.Head?.Name,
                        Body = stand.Body?.Name,
                        Legs = stand.Legs?.Name,
                        MainHand = stand.MainHand?.Name,
                        OffHand = stand.OffHand?.Name,
                        Face = stand.Face?.Name,
                        Ears = stand.Ears?.Name,
                        Neck = stand.Neck?.Name,
                        Waist = stand.Waist?.Name,
                        Feet = stand.Feet?.Name,
                        Back = stand.Back?.Name,
                        CurrentPose = stand.CurrentPose
                    });
                }
                
                data.Boats.Add(boatSave);
            }
            
            // Sauvegarde des voitures
            data.Cars.Clear();
            foreach (var car in Program.Cars)
            {
                var carData = car.GetSaveData();
                carData.PosX = CleanFloat(carData.PosX);
                carData.PosY = CleanFloat(carData.PosY);
                carData.Angle = CleanFloat(carData.Angle);
                carData.VelocityX = CleanFloat(carData.VelocityX);
                carData.VelocityY = CleanFloat(carData.VelocityY);
                carData.AngularVelocity = CleanFloat(carData.AngularVelocity);
                carData.WheelRotation = CleanFloat(carData.WheelRotation);
                carData.WheelSpin = CleanFloat(carData.WheelSpin);
                data.Cars.Add(carData);
            }
            
            data.ExploredTiles = World.GetExploredTiles();
            
            if (Program.PlayerGuild != null)
            {
                int logoSize = Program.PlayerGuild.Logo.GetLength(0);
                data.GuildData = new GuildSaveData
                {
                    Name = Program.PlayerGuild.Name,
                    LogoSize = logoSize,
                    LogoPixels = new List<ColorSave>(),
                    MemberNetIds = Program.PlayerGuild.MemberIds.Select(id => id.ToString()).ToList()
                };
                for (int x = 0; x < logoSize; x++)
                    for (int y = 0; y < logoSize; y++)
                    {
                        var c = Program.PlayerGuild.Logo[x, y];
                        data.GuildData.LogoPixels.Add(new ColorSave { R = c.R, G = c.G, B = c.B, A = c.A });
                    }
            }
            else
            {
                data.GuildData = null;
            }
            
            // Sauvegarde complète de l'inventaire
            data.InventorySlots.Clear();
            for (int i = 0; i < InventoryRenderer.InventorySlots.Count; i++)
            {
                var slot = InventoryRenderer.InventorySlots[i];
                var slotSave = new InventorySlotSave
                {
                    SlotIndex = i,
                    ItemName = slot.Item?.Name ?? "",
                    Metadata = slot.Item?.Metadata ?? "",
                    Meta = (slot.Item?.Meta != null && slot.Item.Meta.Count > 0) ? new Dictionary<string, string>(slot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null,
                    Runes = SaveRunes(slot.Item),
                    Count = slot.Count,
                    IsEmpty = slot.IsEmpty,
                    CustomColors = SaveCustomColors(slot.Item?.CustomColors)
                };
                
                // Conteneur portable
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
                        var contSlotSave = new InventorySlotSave
                        {
                            ItemName = contSlot.Item?.Name ?? "",
                            Metadata = contSlot.Item?.Metadata ?? "",
                            Meta = (contSlot.Item?.Meta != null && contSlot.Item.Meta.Count > 0) ? new Dictionary<string, string>(contSlot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null,
                            Runes = SaveRunes(contSlot.Item),
                            Count = contSlot.Count,
                            IsEmpty = contSlot.IsEmpty,
                            CustomColors = SaveCustomColors(contSlot.Item?.CustomColors)
                        };
                        slotSave.PortableContainer.Slots.Add(contSlotSave);
                    }
                }
                
                // Sac à dos
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
                        var backSlotSave = new InventorySlotSave
                        {
                            ItemName = backSlot.Item?.Name ?? "",
                            Metadata = backSlot.Item?.Metadata ?? "",
                            Meta = (backSlot.Item?.Meta != null && backSlot.Item.Meta.Count > 0) ? new Dictionary<string, string>(backSlot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null,
                            Runes = SaveRunes(backSlot.Item),
                            Count = backSlot.Count,
                            IsEmpty = backSlot.IsEmpty,
                            CustomColors = SaveCustomColors(backSlot.Item?.CustomColors)
                        };
                        slotSave.Backpack.Slots.Add(backSlotSave);
                    }
                }
                
                data.InventorySlots.Add(slotSave);
            }
            
            // Sauvegarde de l'équipement
            data.Equipment = new EquipmentSave
            {
                MainHand = Program.equipment.MainHand?.Name ?? "",
                OffHand = Program.equipment.OffHand?.Name ?? "",
                Backpack = Program.equipment.Backpack?.Name ?? "",
                MainHandCustomColors = SaveCustomColors(Program.equipment.MainHand?.CustomColors),
                OffHandCustomColors = SaveCustomColors(Program.equipment.OffHand?.CustomColors),
                BackpackCustomColors = SaveCustomColors(Program.equipment.Backpack?.CustomColors),
                MainHandMeta = (Program.equipment.MainHand?.Meta != null && Program.equipment.MainHand.Meta.Count > 0) ? new Dictionary<string, string>(Program.equipment.MainHand.Meta, StringComparer.OrdinalIgnoreCase) : null,
                OffHandMeta = (Program.equipment.OffHand?.Meta != null && Program.equipment.OffHand.Meta.Count > 0) ? new Dictionary<string, string>(Program.equipment.OffHand.Meta, StringComparer.OrdinalIgnoreCase) : null,
                BackpackMeta = (Program.equipment.Backpack?.Meta != null && Program.equipment.Backpack.Meta.Count > 0) ? new Dictionary<string, string>(Program.equipment.Backpack.Meta, StringComparer.OrdinalIgnoreCase) : null,
                EquippedItems = Program.equipment.EquippedItems.Select(kv => new EquippedItemSave
                {
                    Slot = kv.Key.slot,
                    Index = kv.Key.index,
                    ItemName = kv.Value.Name,
                    Count = kv.Value.Count,
                    CustomColors = SaveCustomColors(kv.Value.CustomColors),
                    Metadata = kv.Value.Metadata ?? "",
                    Meta = (kv.Value.Meta != null && kv.Value.Meta.Count > 0) ? new Dictionary<string, string>(kv.Value.Meta, StringComparer.OrdinalIgnoreCase) : null,
                    Runes = SaveRunes(kv.Value)
                }).ToList(),
                //  Items couvrant une zone du corps (casque, plastron, jambières, écharpe, lunettes, sac à dos...)
                ZoneItems = Program.equipment.ZoneItems.Select(item => new ZoneItemSave
                {
                    ItemName = item.Name,
                    CustomColors = SaveCustomColors(item.CustomColors),
                    Metadata = item.Metadata ?? "",
                    Meta = (item.Meta != null && item.Meta.Count > 0) ? new Dictionary<string, string>(item.Meta, StringComparer.OrdinalIgnoreCase) : null,
                    Runes = SaveRunes(item)
                }).ToList()
            };

                        // Conteneur portable équipé
            if (Program.equipment.OffHandContainer != null)
            {
                data.Equipment.OffHandContainer = new PortableContainerSaveData
                {
                    Columns = Program.equipment.OffHandContainer.Inventory.Columns,
                    Rows = Program.equipment.OffHandContainer.Inventory.Rows,
                    Slots = new List<InventorySlotSave>()
                };
                foreach (var slot in Program.equipment.OffHandContainer.Inventory.Slots)
                {
                    var slotSave = new InventorySlotSave
                    {
                        ItemName = slot.Item?.Name ?? "",
                        Metadata = slot.Item?.Metadata ?? "",
                        Meta = (slot.Item?.Meta != null && slot.Item.Meta.Count > 0) ? new Dictionary<string, string>(slot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null,
                        Runes = SaveRunes(slot.Item),
                        Count = slot.Count,
                        IsEmpty = slot.IsEmpty,
                        CustomColors = SaveCustomColors(slot.Item?.CustomColors)
                    };
                    data.Equipment.OffHandContainer.Slots.Add(slotSave);
                }
            }
            
            // Sac à dos équipé
            if (Program.equipment.BackpackContainer != null)
            {
                data.Equipment.BackpackContainer = new BackpackSaveData
                {
                    Columns = Program.equipment.BackpackContainer.Inventory.Columns,
                    Rows = Program.equipment.BackpackContainer.Inventory.Rows,
                    Slots = new List<InventorySlotSave>()
                };
                foreach (var slot in Program.equipment.BackpackContainer.Inventory.Slots)
                {
                    var slotSave = new InventorySlotSave
                    {
                        ItemName = slot.Item?.Name ?? "",
                        Metadata = slot.Item?.Metadata ?? "",
                        Meta = (slot.Item?.Meta != null && slot.Item.Meta.Count > 0) ? new Dictionary<string, string>(slot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null,
                        Runes = SaveRunes(slot.Item),
                        Count = slot.Count,
                        IsEmpty = slot.IsEmpty,
                        CustomColors = SaveCustomColors(slot.Item?.CustomColors)
                    };
                    data.Equipment.BackpackContainer.Slots.Add(slotSave);
                }
            }
            
            data.GameTime = Program.GetGameTime();
            data.WeatherType = Weather.Current switch
            {
                WeatherType.Rain => "rain",
                WeatherType.Storm => "storm",
                _ => "clear"
            };
            data.WeatherTimeRemaining = Weather.TimeUntilNextChange;
            
            // Systèmes énergétiques
            data.SteamEngines.Clear();
            foreach (var kv in World.SteamEngines)
            {
                data.SteamEngines.Add(new SteamEngineSave
                {
                    X = kv.Key.x,
                    Y = kv.Key.y,
                    Water = CleanFloat(kv.Value.Water),
                    Coal = CleanFloat(kv.Value.Coal),
                    SteamPressure = CleanFloat(kv.Value.SteamPressure),
                    ProductionRate = CleanFloat(kv.Value.ProductionRate),
                    LastUpdateTime = CleanFloat(kv.Value.LastUpdateTime),
                    IsOn = kv.Value.IsOn
                });
            }
            data.RainCollectors.Clear();
            foreach (var kv in World.RainCollectors)
            {
                data.RainCollectors.Add(new RainCollectorSave
                {
                    X = kv.Key.x,
                    Y = kv.Key.y,
                    CurrentWater = CleanFloat(kv.Value.CurrentWater),
                    Capacity = CleanFloat(kv.Value.Capacity),
                    LastUpdateTime = CleanFloat(kv.Value.LastUpdateTime)
                });
            }
            
            data.LastCropStages.Clear();
            var lastStages = World.GetLastCropStages();
            foreach (var kv in lastStages)
            {
                string key = $"{kv.Key.x}_{kv.Key.y}";
                data.LastCropStages[key] = kv.Value;
            }
            
            data.FarmlandMoisture = World.GetFarmlandMoistureData();
            
            data.Dynamos.Clear();
            foreach (var kv in World.Dynamos)
            {
                var dynamoSave = new DynamoSave
                {
                    X = kv.Key.x,
                    Y = kv.Key.y,
                    SteamInput = CleanFloat(kv.Value.SteamInput),
                    ElectricityOutput = CleanFloat(kv.Value.ElectricityOutput),
                    LastUpdateTime = CleanFloat(kv.Value.LastUpdateTime),
                    Connections = new List<EnergyConnectionSave>()
                };
                foreach (var conn in kv.Value.Connections)
                {
                    dynamoSave.Connections.Add(new EnergyConnectionSave
                    {
                        X1 = conn.X1,
                        Y1 = conn.Y1,
                        X2 = conn.X2,
                        Y2 = conn.Y2,
                        Id = conn.Id.ToString()
                    });
                }
                data.Dynamos.Add(dynamoSave);
            }
            
            data.Batteries.Clear();
            foreach (var kv in World.Batteries)
            {
                var batterySave = new BatterySave
                {
                    X = kv.Key.x,
                    Y = kv.Key.y,
                    StoredEnergy = CleanFloat(kv.Value.StoredEnergy),
                    MaxCapacity = CleanFloat(kv.Value.MaxCapacity),
                    Connections = new List<EnergyConnectionSave>()
                };
                foreach (var conn in kv.Value.Connections)
                {
                    batterySave.Connections.Add(new EnergyConnectionSave
                    {
                        X1 = conn.X1,
                        Y1 = conn.Y1,
                        X2 = conn.X2,
                        Y2 = conn.Y2,
                        Id = conn.Id.ToString()
                    });
                }
                data.Batteries.Add(batterySave);
            }
            
            data.BatteryChargers.Clear();
            foreach (var kv in World.BatteryChargers)
            {
                data.BatteryChargers.Add(new BatteryChargerSave
                {
                    X = kv.Key.x,
                    Y = kv.Key.y,
                    IsOn = kv.Value.IsOn,
                    HasPower = kv.Value.HasPower,
                    LastUpdateTime = CleanFloat(kv.Value.LastUpdateTime),
                    InsertedBattery = kv.Value.InsertedBattery != null ? new BatterySave
                    {
                        StoredEnergy = CleanFloat(kv.Value.InsertedBattery.StoredEnergy),
                        MaxCapacity = CleanFloat(kv.Value.InsertedBattery.MaxCapacity),
                        Connections = new List<EnergyConnectionSave>()
                    } : null
                });
            }
            
            data.Ampoules.Clear();
            foreach (var kv in World.Ampoules)
            {
                data.Ampoules.Add(new AmpouleSave
                {
                    X = kv.Key.x,
                    Y = kv.Key.y,
                    IsOn = kv.Value.IsOn,
                    LastUpdateTime = CleanFloat(kv.Value.LastUpdateTime),
                    WasLitLastFrame = kv.Value.WasLitLastFrame
                });
            }

            data.LogicGates.Clear();
            foreach (var kv in World.LogicGates)
            {
                data.LogicGates.Add(new LogicGateSave
                {
                    X = kv.Key.x,
                    Y = kv.Key.y,
                    Type = kv.Value.Type,
                    OutputState = kv.Value.OutputState,
                    ConnectedInputs = kv.Value.ConnectedInputs,
                    ActiveInputs = kv.Value.ActiveInputs,
                    ManualState = kv.Value.ManualState,
                    PendingPress = kv.Value.PendingPress,
                    DelaySeconds = CleanFloat(kv.Value.DelaySeconds),
                    DelayLastRawInput = kv.Value.DelayLastRawInput,
                    DelayQueue = kv.Value.DelayQueue.Select(entry => new LogicGateDelayEntrySave
                    {
                        ReadyAt = CleanFloat(entry.ReadyAt),
                        State = entry.State
                    }).ToList(),
                    LastUpdateTime = CleanFloat(kv.Value.LastUpdateTime),
                    Connections = kv.Value.Connections.Select(conn => new EnergyConnectionSave
                    {
                        X1 = conn.X1,
                        Y1 = conn.Y1,
                        X2 = conn.X2,
                        Y2 = conn.Y2,
                        Id = conn.Id.ToString()
                    }).ToList()
                });
            }
            
            data.EnergyConnections.Clear();
            foreach (var conn in World.EnergyConnections)
            {
                data.EnergyConnections.Add(new EnergyConnectionSave
                {
                    X1 = conn.X1,
                    Y1 = conn.Y1,
                    X2 = conn.X2,
                    Y2 = conn.Y2,
                    Id = conn.Id.ToString()
                });
            }
            
            data.ObjectHPs = new Dictionary<string, WorldObjectHPSave>();
            foreach (var hp in Program.objectHPs)
            {
                data.ObjectHPs[hp.Key] = new WorldObjectHPSave
                {
                    Current = hp.Value.Current,
                    Max = hp.Value.Max
                };
            }
            
            string json = JsonSerializer.Serialize(data, WriteOptions);
            File.WriteAllText(savePath, json);
            
            UpdateSaveIndex(saveName, data);
        }

        public static void ExportSave(string saveName, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(saveName))
                throw new ArgumentException("Nom de sauvegarde invalide.", nameof(saveName));

            string savePath = Path.Combine(SAVE_DIR, $"{saveName}.json");
            if (!File.Exists(savePath))
                throw new FileNotFoundException("Sauvegarde introuvable.", savePath);

            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
            foreach (string path in Directory.EnumerateFiles(SAVE_DIR, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(SAVE_DIR, path);
                string topLevelName = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                if (!string.Equals(topLevelName, $"{saveName}.json", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(topLevelName, $"{saveName}_chunks", StringComparison.OrdinalIgnoreCase)
                    && !topLevelName.StartsWith($"{saveName}_", StringComparison.OrdinalIgnoreCase))
                    continue;
                archive.CreateEntryFromFile(path, relativePath, CompressionLevel.Fastest);
            }
        }

        public static string ImportSave(string sourcePath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Fichier de sauvegarde introuvable.", sourcePath);

            string importedName = Path.GetFileNameWithoutExtension(sourcePath);
            foreach (char invalid in Path.GetInvalidFileNameChars())
                importedName = importedName.Replace(invalid, '_');
            if (string.IsNullOrWhiteSpace(importedName)) importedName = "Sauvegarde_importee";

            using var archive = ZipFile.OpenRead(sourcePath);
            ZipArchiveEntry? mainEntry = archive.GetEntry($"{importedName}.json")
                ?? archive.Entries.FirstOrDefault(entry => Path.GetFileName(entry.FullName).EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            if (mainEntry == null)
                throw new InvalidDataException("Cette archive ne contient pas de sauvegarde valide.");

            var data = JsonSerializer.Deserialize<GameSaveData>(ReadEntryText(mainEntry), ReadOptions)
                ?? throw new InvalidDataException("Le fichier de sauvegarde est illisible.");
            string originalName = Path.GetFileNameWithoutExtension(mainEntry.Name);
            string targetName = importedName;
            int suffix = 2;
            while (File.Exists(Path.Combine(SAVE_DIR, $"{targetName}.json")))
                targetName = $"{importedName}_{suffix++}";

            Directory.CreateDirectory(SAVE_DIR);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                if (!relativePath.StartsWith(originalName, StringComparison.OrdinalIgnoreCase)) continue;
                relativePath = targetName + relativePath.Substring(originalName.Length);
                string destination = Path.Combine(SAVE_DIR, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            UpdateSaveIndex(targetName, data);
            return targetName;
        }

        private static string ReadEntryText(ZipArchiveEntry entry)
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
        
        // Nettoyage des valeurs flottantes
        private static float CleanFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            return value;
        }
        
        private static void CleanGameData(GameSaveData data)
        {
            data.PlayerPosX = CleanFloat(data.PlayerPosX);
            data.PlayerPosY = CleanFloat(data.PlayerPosY);
            data.PlayerFacing = CleanFloat(data.PlayerFacing);
            data.CameraZoom = CleanFloat(data.CameraZoom);
            data.GameTime = CleanFloat(data.GameTime);
            
            foreach (var boat in data.Boats)
            {
                boat.PosX = CleanFloat(boat.PosX);
                boat.PosY = CleanFloat(boat.PosY);
                boat.VelX = CleanFloat(boat.VelX);
                boat.VelY = CleanFloat(boat.VelY);
                boat.Angle = CleanFloat(boat.Angle);
                boat.RowingCooldown = CleanFloat(boat.RowingCooldown);
                boat.SailAngle = CleanFloat(boat.SailAngle);
            }
            
            foreach (var car in data.Cars)
            {
                car.PosX = CleanFloat(car.PosX);
                car.PosY = CleanFloat(car.PosY);
                car.Angle = CleanFloat(car.Angle);
                car.VelocityX = CleanFloat(car.VelocityX);
                car.VelocityY = CleanFloat(car.VelocityY);
                car.AngularVelocity = CleanFloat(car.AngularVelocity);
                car.WheelRotation = CleanFloat(car.WheelRotation);
                car.WheelSpin = CleanFloat(car.WheelSpin);
            }
            
            foreach (var engine in data.SteamEngines)
            {
                engine.Water = CleanFloat(engine.Water);
                engine.Coal = CleanFloat(engine.Coal);
                engine.SteamPressure = CleanFloat(engine.SteamPressure);
                engine.ProductionRate = CleanFloat(engine.ProductionRate);
                engine.LastUpdateTime = CleanFloat(engine.LastUpdateTime);
            }

            foreach (var rc in data.RainCollectors)
            {
                rc.CurrentWater = CleanFloat(rc.CurrentWater);
                rc.Capacity = CleanFloat(rc.Capacity);
                rc.LastUpdateTime = CleanFloat(rc.LastUpdateTime);
            }
            
            foreach (var dynamo in data.Dynamos)
            {
                dynamo.SteamInput = CleanFloat(dynamo.SteamInput);
                dynamo.ElectricityOutput = CleanFloat(dynamo.ElectricityOutput);
                dynamo.LastUpdateTime = CleanFloat(dynamo.LastUpdateTime);
            }
            
            foreach (var battery in data.Batteries)
            {
                battery.StoredEnergy = CleanFloat(battery.StoredEnergy);
                battery.MaxCapacity = CleanFloat(battery.MaxCapacity);
            }
            
            foreach (var charger in data.BatteryChargers)
            {
                charger.LastUpdateTime = CleanFloat(charger.LastUpdateTime);
                if (charger.InsertedBattery != null)
                {
                    charger.InsertedBattery.StoredEnergy = CleanFloat(charger.InsertedBattery.StoredEnergy);
                    charger.InsertedBattery.MaxCapacity = CleanFloat(charger.InsertedBattery.MaxCapacity);
                }
            }
            
            foreach (var ampoule in data.Ampoules)
            {
                ampoule.LastUpdateTime = CleanFloat(ampoule.LastUpdateTime);
            }

            foreach (var gate in data.LogicGates)
            {
                gate.DelaySeconds = CleanFloat(gate.DelaySeconds);
                gate.LastUpdateTime = CleanFloat(gate.LastUpdateTime);
                foreach (var entry in gate.DelayQueue)
                {
                    entry.ReadyAt = CleanFloat(entry.ReadyAt);
                }
            }
            
            var keysToClean = data.FarmlandMoisture.Keys.ToList();
            foreach (var key in keysToClean)
            {
                data.FarmlandMoisture[key] = CleanFloat(data.FarmlandMoisture[key]);
            }
        }
        
        public static List<ColorSave?> SaveCustomColors(List<Color?>? customColors)
        {
            if (customColors == null || customColors.Count == 0)
                return new List<ColorSave?>();
            
            var result = new List<ColorSave?>();
            foreach (var color in customColors)
            {
                if (color.HasValue)
                {
                    result.Add(new ColorSave { R = color.Value.R, G = color.Value.G, B = color.Value.B, A = color.Value.A });
                }
                else
                {
                    result.Add(null);
                }
            }
            return result;
        }
        
        public static List<Color?>? LoadCustomColors(List<ColorSave?>? savedColors)
        {
            if (savedColors == null || savedColors.Count == 0)
                return null;
            
            var result = new List<Color?>();
            foreach (var colorSave in savedColors)
            {
                if (colorSave != null)
                {
                    result.Add(new Color(colorSave.R, colorSave.G, colorSave.B, colorSave.A));
                }
                else
                {
                    result.Add(null);
                }
            }
            return result;
        }
        
        public static GameSaveData LoadGame(string saveName)
        {
            string savePath = Path.Combine(SAVE_DIR, $"{saveName}.json");
            if (!File.Exists(savePath))
                throw new FileNotFoundException($"Sauvegarde introuvable : {saveName}");
            
            string json = File.ReadAllText(savePath);
            var data = JsonSerializer.Deserialize<GameSaveData>(json, ReadOptions) ?? new GameSaveData();
            
            AchievementManager.Load(data);
            World.LoadExploredTiles(data.ExploredTiles ?? new HashSet<string>());
            QuestManager.RestorePersistedQuests((data.Quests ?? new List<QuestSaveData>())
                .Select(q =>
                {
                    var restoredQuest = new Quest
                    {
                        Id = q.Id,
                        Type = q.Type == 1 ? QuestType.TameAndBring : QuestType.DeliverItem,
                        GiverId = q.GiverId,
                        TargetId = q.TargetId,
                        ItemId = q.ItemId,
                        ItemName = q.ItemName,
                        ItemQty = q.ItemQty,
                        TargetSpecies = q.TargetSpecies ?? "",
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
                        State = (QuestState)q.State
                    };
                    QuestManager.NormalizeQuestItemData(restoredQuest);
                    return restoredQuest;
                }));
			Program.LoadPreviouslyOwnedItemIds(data.PreviouslyOwnedItemIds);
            
            // Restaurer l'inventaire
            if (data.InventorySlots != null && data.InventorySlots.Count > 0)
            {
                InventoryRenderer.RestoreFromSave(data.InventorySlots);
				foreach (var slot in InventoryRenderer.InventorySlots)
					if (!slot.IsEmpty && slot.Item != null)
						Program.MarkItemAsPreviouslyOwned(slot.Item.Name);
            }
            else
            {
                InventoryRenderer.Initialize(40);
            }
            
            // Restaurer les systèmes énergétiques
            World.SteamEngines.Clear();
            foreach (var s in data.SteamEngines)
            {
                World.SteamEngines[(s.X, s.Y)] = new SteamEngineData
                {
                    Water = s.Water,
                    Coal = s.Coal,
                    SteamPressure = s.SteamPressure,
                    ProductionRate = s.ProductionRate,
                    LastUpdateTime = s.LastUpdateTime,
                    IsOn = s.IsOn
                };
            }

            World.RainCollectors.Clear();
            foreach (var r in data.RainCollectors)
            {
                float capacity = RainCollectorData.MaxCapacity;
                World.RainCollectors[(r.X, r.Y)] = new RainCollectorData
                {
                    CurrentWater = Math.Max(0f, Math.Min(r.CurrentWater, capacity)),
                    Capacity = capacity,
                    LastUpdateTime = r.LastUpdateTime
                };
            }
            
            World.Dynamos.Clear();
            foreach (var d in data.Dynamos)
            {
                var dynamo = new DynamoData
                {
                    SteamInput = d.SteamInput,
                    ElectricityOutput = d.ElectricityOutput,
                    LastUpdateTime = d.LastUpdateTime
                };
                foreach (var connSave in d.Connections)
                {
                    dynamo.Connections.Add(new EnergyConnection
                    {
                        X1 = connSave.X1,
                        Y1 = connSave.Y1,
                        X2 = connSave.X2,
                        Y2 = connSave.Y2,
                        Id = Guid.TryParse(connSave.Id, out var guid) ? guid : Guid.NewGuid()
                    });
                }
                World.Dynamos[(d.X, d.Y)] = dynamo;
            }
            
            World.Batteries.Clear();
            foreach (var b in data.Batteries)
            {
                var battery = new BatteryData
                {
                    StoredEnergy = b.StoredEnergy,
                    MaxCapacity = b.MaxCapacity
                };
                foreach (var connSave in b.Connections)
                {
                    battery.Connections.Add(new EnergyConnection
                    {
                        X1 = connSave.X1,
                        Y1 = connSave.Y1,
                        X2 = connSave.X2,
                        Y2 = connSave.Y2,
                        Id = Guid.TryParse(connSave.Id, out var guid) ? guid : Guid.NewGuid()
                    });
                }
                World.Batteries[(b.X, b.Y)] = battery;
            }
            
            World.BatteryChargers.Clear();
            foreach (var c in data.BatteryChargers)
            {
                var charger = new BatteryChargerData
                {
                    IsOn = c.IsOn,
                    HasPower = c.HasPower,
                    LastUpdateTime = c.LastUpdateTime
                };
                if (c.InsertedBattery != null)
                {
                    charger.InsertedBattery = new BatteryData
                    {
                        StoredEnergy = c.InsertedBattery.StoredEnergy,
                        MaxCapacity = c.InsertedBattery.MaxCapacity
                    };
                }
                World.BatteryChargers[(c.X, c.Y)] = charger;
            }
            
            World.Ampoules.Clear();
            foreach (var a in data.Ampoules)
            {
                World.Ampoules[(a.X, a.Y)] = new AmpouleData
                {
                    IsOn = a.IsOn,
                    LastUpdateTime = a.LastUpdateTime,
                    WasLitLastFrame = a.WasLitLastFrame
                };
            }

            World.LogicGates.Clear();
            foreach (var g in data.LogicGates)
            {
                var gate = new LogicGateData
                {
                    Type = g.Type,
                    OutputState = g.OutputState,
                    ConnectedInputs = g.ConnectedInputs,
                    ActiveInputs = g.ActiveInputs,
                    ManualState = g.ManualState,
                    PendingPress = g.PendingPress,
                    DelaySeconds = g.DelaySeconds,
                    DelayLastRawInput = g.DelayLastRawInput,
                    LastUpdateTime = g.LastUpdateTime
                };
                gate.DelayQueue = g.DelayQueue.Select(entry => (entry.ReadyAt, entry.State)).ToList();
                foreach (var connSave in g.Connections)
                {
                    gate.Connections.Add(new EnergyConnection
                    {
                        X1 = connSave.X1,
                        Y1 = connSave.Y1,
                        X2 = connSave.X2,
                        Y2 = connSave.Y2,
                        Id = Guid.TryParse(connSave.Id, out var guid) ? guid : Guid.NewGuid()
                    });
                }
                World.LogicGates[(g.X, g.Y)] = gate;
            }
            
            World.EnergyConnections.Clear();
            foreach (var connSave in data.EnergyConnections)
            {
                World.EnergyConnections.Add(new EnergyConnection
                {
                    X1 = connSave.X1,
                    Y1 = connSave.Y1,
                    X2 = connSave.X2,
                    Y2 = connSave.Y2,
                    Id = Guid.TryParse(connSave.Id, out var guid) ? guid : Guid.NewGuid()
                });
            }
            
            // Restaurer les HPs des objets
            Program.objectHPs.Clear();
            foreach (var hp in data.ObjectHPs)
            {
                var hpObj = new WorldObjectHP(hp.Value.Max);
                hpObj.Current = hp.Value.Current;
                Program.objectHPs[hp.Key] = hpObj;
            }
            
            // Restaurer les stades des cultures
            if (data.LastCropStages != null)
            {
                var restoredStages = new Dictionary<(int x, int y), int>();
                foreach (var kv in data.LastCropStages)
                {
                    var parts = kv.Key.Split('_');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                    {
                        restoredStages[(x, y)] = kv.Value;
                    }
                }
                World.RestoreLastCropStages(restoredStages);
            }
            
            // Restaurer l'humidité
            if (data.FarmlandMoisture != null)
            {
                World.RestoreFarmlandMoisture(data.FarmlandMoisture);
            }
            
            // Restaurer les voitures
            Program.Cars.Clear();
            foreach (var carData in data.Cars)
            {
                var car = new Car(Vector2.Zero);
                car.LoadSaveData(carData);
                Program.Cars.Add(car);
            }
            
            return data;
        }
        
        public static void DeleteSave(string saveName)
        {
            string savePath = Path.Combine(SAVE_DIR, $"{saveName}.json");
            if (File.Exists(savePath))
                File.Delete(savePath);

            string chunkPrefix = $"{saveName}_";
            lock (ChunkStoreLock)
            {
                foreach (string cachedPath in ChunkStores.Keys
                    .Where(path => Path.GetFileName(path).StartsWith(chunkPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList())
                {
                    ChunkStores.Remove(cachedPath);
                }
            }

            string prefix = $"{saveName}_";
            if (Directory.Exists(SAVE_DIR))
            {
                foreach (var file in Directory.GetFiles(SAVE_DIR))
                {
                    if (Path.GetFileName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        File.Delete(file);
                }

                foreach (var dir in Directory.GetDirectories(SAVE_DIR))
                {
                    if (Path.GetFileName(dir).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(dir, true);
                }
            }

            UpdateSaveIndexAfterDelete(saveName);
            Console.WriteLine($" Partie supprimée : {saveName}");
        }
        
        //  MULTIJOUEUR : construit un ChunkSaveData à partir d'un chunk vivant, sans
        // toucher au disque. Extrait de SaveChunk pour être réutilisé à la fois par la
        // sauvegarde et par l'envoi d'un chunk à un client via le réseau.
        public static ChunkSaveData BuildChunkSaveData(int chunkX, int chunkY, World.ChunkData chunk)
        {
            var objectsDict = new Dictionary<int, int>();
            if (chunk.Objects != null)
            {
                foreach (var kv in chunk.Objects)
                    objectsDict[PackTileKey(kv.Key.x, kv.Key.y, chunkX, chunkY)] = kv.Value;
            }
            
            var overlaysDict = new Dictionary<int, int>();
            if (chunk.Overlays != null)
            {
                foreach (var kv in chunk.Overlays)
                    overlaysDict[PackTileKey(kv.Key.x, kv.Key.y, chunkX, chunkY)] = kv.Value;
            }
            
            var heightsDict = new Dictionary<int, int>();
            if (chunk.Heights != null)
            {
                foreach (var kv in chunk.Heights)
                    heightsDict[PackTileKey(kv.Key.x, kv.Key.y, chunkX, chunkY)] = kv.Value;
            }
            
            var decorationsDict = new Dictionary<int, int>();
            if (chunk.Decorations != null)
            {
                foreach (var kv in chunk.Decorations)
                    decorationsDict[PackTileKey(kv.Key.x, kv.Key.y, chunkX, chunkY)] = kv.Value;
            }
                
            var variationsDict = new Dictionary<int, int>();
            if (chunk.Variations != null)
            {
                foreach (var kv in chunk.Variations)
                    variationsDict[PackTileKey(kv.Key.x, kv.Key.y, chunkX, chunkY)] = kv.Value;
            }
            
            var containersDict = new Dictionary<int, ContainerInventorySave>();
            if (chunk.Containers != null)
            {
                foreach (var cont in chunk.Containers)
                {
                    int key = PackTileKey(cont.Key.x, cont.Key.y, chunkX, chunkY);
                    var save = new ContainerInventorySave();
                    foreach (var slot in cont.Value.Slots)
                    {
                        save.Slots.Add(new InventorySlotSave
                        {
                            ItemName = slot.Item?.Name ?? "",
                            Count = slot.Count,
                            IsEmpty = slot.IsEmpty,
                            CustomColors = SaveCustomColors(slot.Item?.CustomColors),
                            Metadata = slot.Item?.Metadata ?? "",
                            Meta = (slot.Item?.Meta != null && slot.Item.Meta.Count > 0) ? new Dictionary<string, string>(slot.Item.Meta, StringComparer.OrdinalIgnoreCase) : null
                        });
                    }
                    containersDict[key] = save;
                }
            }
            
            var armorStandsDict = new Dictionary<int, ArmorStandSaveData>();
            if (chunk.ArmorStands != null)
            {
                foreach (var stand in chunk.ArmorStands)
                {
                    int key = PackTileKey(stand.Key.x, stand.Key.y, chunkX, chunkY);
                    var save = new ArmorStandSaveData
                    {
                        Head = stand.Value.Head?.Name,
                        Body = stand.Value.Body?.Name,
                        Legs = stand.Value.Legs?.Name,
                        MainHand = stand.Value.MainHand?.Name,
                        OffHand = stand.Value.OffHand?.Name,
                        Face = stand.Value.Face?.Name,
                        Ears = stand.Value.Ears?.Name,
                        Neck = stand.Value.Neck?.Name,
                        Waist = stand.Value.Waist?.Name,
                        Feet = stand.Value.Feet?.Name,
                        Back = stand.Value.Back?.Name,
                        CurrentPose = stand.Value.CurrentPose
                    };
                    armorStandsDict[key] = save;
                }
            }
            
            var groundOverridesDict = new Dictionary<int, int>();
            if (chunk.GroundOverrides != null)
            {
                foreach (var kv in chunk.GroundOverrides)
                    groundOverridesDict[PackTileKey(kv.Key.x, kv.Key.y, chunkX, chunkY)] = kv.Value;
            }
            
            var cropsDict = new Dictionary<int, CropSaveData>();
            if (chunk.Crops != null)
            {
                foreach (var crop in chunk.Crops)
                {
                    int key = PackTileKey(crop.Key.x, crop.Key.y, chunkX, chunkY);
                    cropsDict[key] = new CropSaveData
                    {
                        CropTileId = crop.Value.CropTileId,
                        PlantTime = crop.Value.PlantTime,
                        AccumulatedGrowthTime = crop.Value.AccumulatedGrowthTime
                    };
                }
            }
            
            var roofHeightsDict = new Dictionary<int, int>();
            if (chunk.RoofHeights != null)
            {
                foreach (var kv in chunk.RoofHeights)
                    roofHeightsDict[PackTileKey(kv.Key.x, kv.Key.y, chunkX, chunkY)] = kv.Value;
            }

            var roofTileInfoDict = new Dictionary<int, RoofTileInfoSaveData>();
            if (chunk.RoofHeights != null)
            {
                foreach (var kv in chunk.RoofHeights)
                {
                    var tilePos = kv.Key;
                    if (World.TryGetRoofTileInfo(tilePos.x, tilePos.y, out var info))
                    {
                        roofTileInfoDict[PackTileKey(tilePos.x, tilePos.y, chunkX, chunkY)] = new RoofTileInfoSaveData
                        {
                            Height = info.Height,
                            IsHorizontalZone = info.IsHorizontalZone,
                            WallStackCount = info.WallStackCount,
                            TexKind = info.TexKind.ToString(),
                            IsColumnAnchor = info.IsColumnAnchor
                        };
                    }
                }
            }
            
            var wallCoveringsDict = new Dictionary<int, int>();
            if (chunk.WallCoverings != null)
            {
                foreach (var kv in chunk.WallCoverings)
                    wallCoveringsDict[PackTileKey(kv.Key.x, kv.Key.y, chunkX, chunkY)] = kv.Value;
            }

            var bannerHoldersDict = new Dictionary<int, BannerHolderSaveData>();
            if (chunk.BannerHolders != null)
            {
                foreach (var kv in chunk.BannerHolders)
                {
                    int key = PackTileKey(kv.Key.x, kv.Key.y, chunkX, chunkY);
                    bannerHoldersDict[key] = new BannerHolderSaveData
                    {
                        HasBanner = kv.Value.HasBanner,
                        PatternName = kv.Value.PatternName,
                        ShapeName = kv.Value.ShapeName,
                        BgR = kv.Value.BgR, BgG = kv.Value.BgG, BgB = kv.Value.BgB,
                        PatR = kv.Value.PatR, PatG = kv.Value.PatG, PatB = kv.Value.PatB,
                    };
                }
            }

            //  BuildingIds/Aquariums sont déjà itérés en coordonnées locales ci-dessous (boucle
            // localX/localY), donc empaquetés directement sans passer par PackTileKey (qui
            // attend des coordonnées MONDE en entrée).
            var buildingIdsDict = new Dictionary<int, int>();
            for (int localX = 0; localX < World.CHUNK_SIZE; localX++)
            {
                for (int localY = 0; localY < World.CHUNK_SIZE; localY++)
                {
                    int worldX = chunkX * World.CHUNK_SIZE + localX;
                    int worldY = chunkY * World.CHUNK_SIZE + localY;
                    int buildingId = World.GetBuildingIdAt(worldX, worldY);
                    if (buildingId != 0)
                        buildingIdsDict[localX * World.CHUNK_SIZE + localY] = buildingId;
                }
            }
            
            var aquariumsDict = new Dictionary<int, List<AquariumFishSaveData>>();
            for (int localX = 0; localX < World.CHUNK_SIZE; localX++)
            {
                for (int localY = 0; localY < World.CHUNK_SIZE; localY++)
                {
                    int worldX = chunkX * World.CHUNK_SIZE + localX;
                    int worldY = chunkY * World.CHUNK_SIZE + localY;
                    var aquariumData = World.GetAquariumData(worldX, worldY);
                    if (aquariumData != null && aquariumData.FishStates.Count > 0)
                    {
                        int key = localX * World.CHUNK_SIZE + localY;
                        var fishList = new List<AquariumFishSaveData>();
                        foreach (var fish in aquariumData.FishStates.Values)
                        {
                            fishList.Add(new AquariumFishSaveData
                            {
                                SlotIndex = fish.SlotIndex,
                                LocalPosX = fish.LocalPosition.X,
                                LocalPosY = fish.LocalPosition.Y,
                                TargetPosX = fish.TargetPosition.X,
                                TargetPosY = fish.TargetPosition.Y,
                                MoveSpeed = fish.MoveSpeed,
                                IsMoving = fish.IsMoving,
                                IdleTimer = fish.IdleTimer,
                                BobPhase = fish.BobPhase,
                                BobSpeed = fish.BobSpeed,
                                MoveStartTime = fish.MoveStartTime,
                                MoveDuration = fish.MoveDuration,
                                BaseY = fish.BaseY
                            });
                        }
                        aquariumsDict[key] = fishList;
                    }
                }
            }
            
            var tileMetaDict = new Dictionary<int, Dictionary<string, string>>();
            if (chunk.TileMeta != null)
            {
                foreach (var kv in chunk.TileMeta)
                {
                    if (kv.Value == null || kv.Value.Count == 0) continue;
                    tileMetaDict[PackTileKey(kv.Key.x, kv.Key.y, chunkX, chunkY)] = new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase);
                }
            }

            var entitiesList = new List<EntitySaveData>();
            if (chunk.Entities != null)
            {
                foreach (var e in chunk.Entities)
                {
                    if (e.IsPetCompanion)
                        continue;

                    entitiesList.Add(new EntitySaveData
                    {
                        WorldPosX = e.WorldPos.X,
                        WorldPosY = e.WorldPos.Y,
                        Species = e.Species,
                        Tint = SaveColor(e.Tint),
                        HairColor = SaveColor(e.HairColor),
                        HairStyle = e.HairStyle,
                        BeardStyle = e.BeardStyle,
                        RandomFeatureVariants = e.RandomFeatureVariant?.ToDictionary(entry => entry.Key, entry => entry.Value)
                            ?? new Dictionary<string, int>(),
                        RandomFeatureColors = e.RandomFeatureColor?.ToDictionary(
                            entry => entry.Key,
                            entry => SaveColor(entry.Value))
                            ?? new Dictionary<string, ColorSave>(),
                        CurrentHP = e.CurrentHP,
                        MaxHP = e.MaxHP,
                        IsBoss = e.IsBoss,
                        IsSpiritAnimal = e.IsSpiritAnimal,
                        SpiritTotemTileX = e.SpiritTotemTileX,
                        SpiritTotemTileY = e.SpiritTotemTileY,
                        Behavior = e.Behavior ?? "passive",
                        VisionRange = e.VisionRange,
                        AlertDuration = e.AlertDuration,
                        IsAlerted = e.IsAlerted,
                        AlertTimer = e.AlertTimer,
                        IsTamed = e.IsTamed,
                        IsGuildMember = e.IsGuildMember,
                        WantsToJoinGuild = e.WantsToJoinGuild,
                        OwnerName = e.OwnerName ?? "",
                        OwnerNpcId = e.OwnerNpcId?.ToString() ?? "",
                        //  On sauvegarde le NetId même si OwnedMount vient de OwnedMountNetId
                        // (pas encore résolu, ex : sauvegarde juste après un chargement) : le
                        // lien survit ainsi à des sauvegardes/chargements en chaîne.
                        OwnedMountNetId = (e.OwnedMount?.NetId ?? e.OwnedMountNetId)?.ToString() ?? "",
                        IsRidingMount = e.IsRidingMount,
                        CustomName = e.CustomName ?? "",
                        TamedBehavior = (int)e.TamedBehavior,
                        HomePosX = e.HomePosition.X,
                        HomePosY = e.HomePosition.Y,
                        HomeBuildingId = e.HomeBuildingId,
                        Hunger = e.Hunger,
                        WorkplacePosX = e.WorkplacePosition?.X,
                        WorkplacePosY = e.WorkplacePosition?.Y,
                        WorkZoneId = e.WorkZoneId,
                        IsTrader = e.IsTrader,
                        Profession = (int)e.Profession,
                        TraderItemIds = e.TraderItems?.ToList() ?? new List<int>(),
                        Friendship = e.Friendship,
                        IsBaby = e.IsBaby,
                        Age = e.Age,
                        GrowthTime = e.GrowthTime,
                        GestationTimer = e.GestationTimer,
                        Scale = e.Scale,
                        SlimeIncubationSeconds = e.SlimeIncubationSeconds,
                        NetId = e.NetId.ToString(),
                        FirstName = e.FirstName ?? "",
                        HasActiveQuest = e.ActiveQuest != null,
                        QuestType = e.ActiveQuest?.Type == QuestType.TameAndBring ? 1 : 0,
                        QuestTargetNetId = e.ActiveQuest?.TargetId.ToString() ?? "",
                        QuestItemId = e.ActiveQuest?.ItemId ?? 0,
                        QuestItemName = e.ActiveQuest?.ItemName ?? "",
                        QuestItemQty = e.ActiveQuest?.ItemQty ?? 0,
                        QuestTargetSpecies = e.ActiveQuest?.TargetSpecies ?? "",
                        QuestRewardItemId = e.ActiveQuest?.RewardItemId ?? 0,
                        QuestRewardItemName = e.ActiveQuest?.RewardItemName ?? "",
                        QuestRewardItemQty = e.ActiveQuest?.RewardItemQty ?? 0,
                        QuestTargetPosX = e.ActiveQuest?.LastKnownTargetPosX ?? 0f,
                        QuestTargetPosY = e.ActiveQuest?.LastKnownTargetPosY ?? 0f,
                        QuestHasTargetPos = e.ActiveQuest?.HasLastKnownTargetPosition ?? false,
                        QuestState = e.ActiveQuest != null ? (int)e.ActiveQuest.State : 0,
                        InventorySlots = e.Inventory?.Slots.Select(CreateInventorySlotSave).ToList() ?? new List<InventorySlotSave>(),
                        Equipment = CreateEquipmentSave(e.Equipment),
                        IsInterestedInFood = e.IsInterestedInFood,
                        PreferredFoodId = e.PreferredFoodId,
                        FollowOrder = e.FollowOrder,
                        IsTalking = e.IsTalking,
                        TalkTimer = e.TalkTimer,
                        TalkCooldown = e.TalkCooldown,
                        TalkText = e.TalkText ?? "",
                        Pets = e.Pets?.Select(pet => new EntitySaveData
                        {
                            WorldPosX = pet.WorldPos.X,
                            WorldPosY = pet.WorldPos.Y,
                            Species = pet.Species,
                            Tint = SaveColor(pet.Tint),
                            HairColor = SaveColor(pet.HairColor),
                            HairStyle = pet.HairStyle,
                            BeardStyle = pet.BeardStyle,
                            RandomFeatureVariants = pet.RandomFeatureVariant?.ToDictionary(entry => entry.Key, entry => entry.Value)
                                ?? new Dictionary<string, int>(),
                            RandomFeatureColors = pet.RandomFeatureColor?.ToDictionary(
                                entry => entry.Key,
                                entry => SaveColor(entry.Value))
                                ?? new Dictionary<string, ColorSave>(),
                            CurrentHP = pet.CurrentHP,
                            MaxHP = pet.MaxHP,
                            IsBoss = pet.IsBoss,
                            Behavior = pet.Behavior ?? "tamed",
                            VisionRange = pet.VisionRange,
                            AlertDuration = pet.AlertDuration,
                            IsAlerted = pet.IsAlerted,
                            AlertTimer = pet.AlertTimer,
                            IsTamed = pet.IsTamed,
                            IsGuildMember = pet.IsGuildMember,
                            WantsToJoinGuild = pet.WantsToJoinGuild,
                            OwnerName = pet.OwnerName ?? "",
                            CustomName = pet.CustomName ?? "",
                            TamedBehavior = (int)pet.TamedBehavior,
                            HomePosX = pet.HomePosition.X,
                            HomePosY = pet.HomePosition.Y,
                            HomeBuildingId = pet.HomeBuildingId,
                            Hunger = pet.Hunger,
                            WorkplacePosX = pet.WorkplacePosition?.X,
                            WorkplacePosY = pet.WorkplacePosition?.Y,
                            WorkZoneId = pet.WorkZoneId,
                            IsTrader = pet.IsTrader,
                            Profession = (int)pet.Profession,
                            TraderItemIds = pet.TraderItems?.ToList() ?? new List<int>(),
                            Friendship = pet.Friendship,
                            IsBaby = pet.IsBaby,
                            Age = pet.Age,
                            GrowthTime = pet.GrowthTime,
                            GestationTimer = pet.GestationTimer,
                            Scale = pet.Scale,
                            SlimeIncubationSeconds = pet.SlimeIncubationSeconds,
                            NetId = pet.NetId.ToString(),
                            FirstName = pet.FirstName ?? "",
                            HasActiveQuest = pet.ActiveQuest != null,
                            QuestType = pet.ActiveQuest?.Type == QuestType.TameAndBring ? 1 : 0,
                            QuestTargetNetId = pet.ActiveQuest?.TargetId.ToString() ?? "",
                            QuestItemId = pet.ActiveQuest?.ItemId ?? 0,
                            QuestItemName = pet.ActiveQuest?.ItemName ?? "",
                            QuestItemQty = pet.ActiveQuest?.ItemQty ?? 0,
                            QuestTargetSpecies = pet.ActiveQuest?.TargetSpecies ?? "",
                            QuestRewardItemId = pet.ActiveQuest?.RewardItemId ?? 0,
                            QuestRewardItemName = pet.ActiveQuest?.RewardItemName ?? "",
                            QuestRewardItemQty = pet.ActiveQuest?.RewardItemQty ?? 0,
                            QuestTargetPosX = pet.ActiveQuest?.LastKnownTargetPosX ?? 0f,
                            QuestTargetPosY = pet.ActiveQuest?.LastKnownTargetPosY ?? 0f,
                            QuestHasTargetPos = pet.ActiveQuest?.HasLastKnownTargetPosition ?? false,
                            QuestState = pet.ActiveQuest != null ? (int)pet.ActiveQuest.State : 0,
                            InventorySlots = pet.Inventory?.Slots.Select(CreateInventorySlotSave).ToList() ?? new List<InventorySlotSave>(),
                            Equipment = CreateEquipmentSave(pet.Equipment),
                            IsInterestedInFood = pet.IsInterestedInFood,
                            PreferredFoodId = pet.PreferredFoodId,
                            FollowOrder = pet.FollowOrder,
                            IsTalking = pet.IsTalking,
                            TalkTimer = pet.TalkTimer,
                            TalkCooldown = pet.TalkCooldown,
                            TalkText = pet.TalkText ?? ""
                        }).ToList() ?? new List<EntitySaveData>()
                    });
                }
            }
            
            return new ChunkSaveData
            {
                ChunkX = chunkX,
                ChunkY = chunkY,
                Objects = objectsDict,
                Overlays = overlaysDict,
                Heights = heightsDict,
                Decorations = decorationsDict,
                Variations = variationsDict,
                Containers = containersDict,
                ArmorStands = armorStandsDict,
                GroundOverrides = groundOverridesDict,
                Crops = cropsDict,
                RoofHeights = roofHeightsDict,
                RoofTileInfo = roofTileInfoDict,
                WallCoverings = wallCoveringsDict,
                BannerHolders = bannerHoldersDict,
                BuildingIds = buildingIdsDict,
                Aquariums = aquariumsDict,
                TileMeta = tileMetaDict,
                Entities = entitiesList
            };
        }

        //  MULTIJOUEUR : variante réseau — récupère le chunk vivant (host) et le convertit,
        // sans toucher au disque. Retourne null si le chunk n'est pas (encore) chargé.
        public static ChunkSaveData? BuildChunkSaveDataForNetwork(int chunkX, int chunkY, bool isUnderground)
        {
            var chunk = World.GetChunkRaw(chunkX, chunkY, isUnderground);
            if (chunk == null) return null;
            return BuildChunkSaveData(chunkX, chunkY, chunk);
        }

        public static void SaveChunk(string saveName, int chunkX, int chunkY, World.ChunkData chunk, bool isUnderground)
        {
            string subDir = isUnderground ? $"{saveName}_cavechunks" : $"{saveName}_chunks";
            SaveChunkToDir(subDir, chunkX, chunkY, chunk);
        }

        /// <summary>Sauvegarde un chunk appartenant à un donjon précis, dans un dossier dédié à
        /// cette instance — jamais mélangé avec les chunks de grotte ou de surface.</summary>
        public static void SaveDungeonChunk(string saveName, int instanceId, int chunkX, int chunkY, World.ChunkData chunk)
        {
            SaveChunkToDir($"{saveName}_dungeon{instanceId}_chunks", chunkX, chunkY, chunk);
        }

        public static void SaveDungeonChunks(string saveName, int instanceId,
            Dictionary<(int chunkX, int chunkY), World.ChunkData> chunks)
        {
            if (chunks.Count == 0) return;

            string subDir = $"{saveName}_dungeon{instanceId}_chunks";
            string storePath = GetChunkStorePath(subDir);
            lock (ChunkStoreLock)
            {
                var store = GetChunkStore(storePath);
                Directory.CreateDirectory(SAVE_DIR);

                string legacyChunkDir = Path.Combine(SAVE_DIR, subDir);
                if (Directory.Exists(legacyChunkDir))
                    Directory.Delete(legacyChunkDir, recursive: true);

                using (var stream = new FileStream(storePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                {
                    if (stream.Length == 0)
                    {
                        using var header = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                        header.Write(CHUNK_STORE_MAGIC);
                        header.Write(CHUNK_STORE_VERSION);
                        header.Flush();
                        store.Length = CHUNK_STORE_HEADER_SIZE;
                    }

                    stream.Position = store.Length;
                    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                    {
                        foreach (var (coords, chunk) in chunks)
                        {
                            var chunkData = BuildChunkSaveData(coords.chunkX, coords.chunkY, chunk);
                            byte[] payload = CompressChunkData(chunkData);
                            writer.Write(coords.chunkX);
                            writer.Write(coords.chunkY);
                            writer.Write(payload.Length);
                            long payloadOffset = stream.Position;
                            writer.Write(payload);

                            if (store.Chunks.TryGetValue(coords, out var previous))
                                store.LiveBytes -= CHUNK_RECORD_HEADER_SIZE + previous.PayloadLength;

                            store.Chunks[coords] = new ChunkRecord(payloadOffset, payload.Length);
                            store.LiveBytes += CHUNK_RECORD_HEADER_SIZE + payload.Length;
                        }

                        writer.Flush();
                        stream.Flush();
                        store.Length = stream.Position;
                    }
                }

                if (store.Length > Math.Max(4L * 1024 * 1024, store.LiveBytes * 2))
                    CompactChunkStore(storePath, store);
            }
        }

        private static void SaveChunkToDir(string subDir, int chunkX, int chunkY, World.ChunkData chunk)
        {
            var chunkData = BuildChunkSaveData(chunkX, chunkY, chunk);
            byte[] payload = CompressChunkData(chunkData);
            string storePath = GetChunkStorePath(subDir);

            lock (ChunkStoreLock)
            {
                var store = GetChunkStore(storePath);
                Directory.CreateDirectory(SAVE_DIR);

                string legacyChunkDir = Path.Combine(SAVE_DIR, subDir);
                if (Directory.Exists(legacyChunkDir))
                    Directory.Delete(legacyChunkDir, recursive: true);

                using (var stream = new FileStream(storePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                {
                    if (stream.Length == 0)
                    {
                        using var header = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                        header.Write(CHUNK_STORE_MAGIC);
                        header.Write(CHUNK_STORE_VERSION);
                        header.Flush();
                        store.Length = CHUNK_STORE_HEADER_SIZE;
                    }

                    stream.Position = store.Length;
                    using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                    writer.Write(chunkX);
                    writer.Write(chunkY);
                    writer.Write(payload.Length);
                    long payloadOffset = stream.Position;
                    writer.Write(payload);
                    writer.Flush();
                    stream.Flush();

                    if (store.Chunks.TryGetValue((chunkX, chunkY), out var previous))
                        store.LiveBytes -= CHUNK_RECORD_HEADER_SIZE + previous.PayloadLength;

                    store.Chunks[(chunkX, chunkY)] = new ChunkRecord(payloadOffset, payload.Length);
                    store.LiveBytes += CHUNK_RECORD_HEADER_SIZE + payload.Length;
                    store.Length = stream.Position;
                }

                if (store.Length > Math.Max(4L * 1024 * 1024, store.LiveBytes * 2))
                    CompactChunkStore(storePath, store);
            }
        }

        private static string GetChunkStorePath(string subDir)
        {
            return Path.GetFullPath(Path.Combine(SAVE_DIR, $"{subDir}.bin"));
        }

        private static byte[] CompressChunkData(ChunkSaveData chunkData)
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(chunkData, WriteOptions);
            using var output = new MemoryStream();
            using (var compressor = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
                compressor.Write(json);

            byte[] payload = output.ToArray();
            if (payload.Length > MAX_CHUNK_RECORD_SIZE)
                throw new InvalidDataException("Compressed chunk exceeds the supported record size.");
            return payload;
        }

        private static ChunkStore GetChunkStore(string storePath)
        {
            if (ChunkStores.TryGetValue(storePath, out var cachedStore))
                return cachedStore;

            var store = new ChunkStore();
            if (File.Exists(storePath))
            {
                using var stream = new FileStream(storePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                if (stream.Length < CHUNK_STORE_HEADER_SIZE)
                    throw new InvalidDataException($"Invalid chunk store header: {storePath}");

                using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                if (reader.ReadInt32() != CHUNK_STORE_MAGIC || reader.ReadInt32() != CHUNK_STORE_VERSION)
                    throw new InvalidDataException($"Unsupported chunk store format: {storePath}");

                long validLength = CHUNK_STORE_HEADER_SIZE;
                while (stream.Length - stream.Position >= CHUNK_RECORD_HEADER_SIZE)
                {
                    long recordStart = stream.Position;
                    int chunkX = reader.ReadInt32();
                    int chunkY = reader.ReadInt32();
                    int payloadLength = reader.ReadInt32();
                    if (payloadLength <= 0 || payloadLength > MAX_CHUNK_RECORD_SIZE
                        || payloadLength > stream.Length - stream.Position)
                    {
                        stream.Position = recordStart;
                        break;
                    }

                    long payloadOffset = stream.Position;
                    stream.Position += payloadLength;
                    if (store.Chunks.TryGetValue((chunkX, chunkY), out var previous))
                        store.LiveBytes -= CHUNK_RECORD_HEADER_SIZE + previous.PayloadLength;
                    store.Chunks[(chunkX, chunkY)] = new ChunkRecord(payloadOffset, payloadLength);
                    store.LiveBytes += CHUNK_RECORD_HEADER_SIZE + payloadLength;
                    validLength = stream.Position;
                }

                if (stream.Length != validLength)
                {
                    stream.SetLength(validLength);
                    stream.Flush();
                }
                store.Length = validLength;
            }

            ChunkStores[storePath] = store;
            return store;
        }

        private static ChunkSaveData? ReadChunkData(string storePath, ChunkRecord record)
        {
            byte[] payload = new byte[record.PayloadLength];
            using (var stream = new FileStream(storePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Position = record.PayloadOffset;
                stream.ReadExactly(payload);
            }

            using var input = new MemoryStream(payload, writable: false);
            using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<ChunkSaveData>(decompressor, ReadOptions);
        }

        private static ChunkSaveData? LoadChunkData(string subDir, int chunkX, int chunkY)
        {
            string storePath = GetChunkStorePath(subDir);
            lock (ChunkStoreLock)
            {
                var store = GetChunkStore(storePath);
                return store.Chunks.TryGetValue((chunkX, chunkY), out var record)
                    ? ReadChunkData(storePath, record)
                    : null;
            }
        }

        private static IEnumerable<ChunkSaveData> EnumerateChunkData(string subDir)
        {
            string storePath = GetChunkStorePath(subDir);
            List<(int x, int y)> coordinates;
            lock (ChunkStoreLock)
            {
                var store = GetChunkStore(storePath);
                coordinates = store.Chunks.Keys.OrderBy(key => key.x).ThenBy(key => key.y).ToList();
            }

            foreach (var (chunkX, chunkY) in coordinates)
            {
                var chunkData = LoadChunkData(subDir, chunkX, chunkY);
                if (chunkData != null)
                    yield return chunkData;
            }
        }

        private static void CompactChunkStore(string storePath, ChunkStore store)
        {
            string tempPath = $"{storePath}.tmp";
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var compactedChunks = new Dictionary<(int x, int y), ChunkRecord>();
            long compactedLiveBytes = 0;
            using (var source = new FileStream(storePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(destination, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(CHUNK_STORE_MAGIC);
                writer.Write(CHUNK_STORE_VERSION);
                foreach (var entry in store.Chunks.OrderBy(entry => entry.Key.x).ThenBy(entry => entry.Key.y))
                {
                    source.Position = entry.Value.PayloadOffset;
                    byte[] payload = new byte[entry.Value.PayloadLength];
                    source.ReadExactly(payload);

                    writer.Write(entry.Key.x);
                    writer.Write(entry.Key.y);
                    writer.Write(payload.Length);
                    long payloadOffset = destination.Position;
                    writer.Write(payload);
                    compactedChunks[entry.Key] = new ChunkRecord(payloadOffset, payload.Length);
                    compactedLiveBytes += CHUNK_RECORD_HEADER_SIZE + payload.Length;
                }
                writer.Flush();
                destination.Flush();
            }

            File.Move(tempPath, storePath, overwrite: true);
            store.Chunks.Clear();
            foreach (var entry in compactedChunks)
                store.Chunks[entry.Key] = entry.Value;
            store.LiveBytes = compactedLiveBytes;
            store.Length = CHUNK_STORE_HEADER_SIZE + compactedLiveBytes;
        }

        private static long GetChunkStoreFileSize(string subDir)
        {
            string storePath = GetChunkStorePath(subDir);
            return File.Exists(storePath) ? new FileInfo(storePath).Length : 0;
        }

        private static int GetChunkStoreCount(string subDir)
        {
            string storePath = GetChunkStorePath(subDir);
            lock (ChunkStoreLock)
                return GetChunkStore(storePath).Chunks.Count;
        }
        
        public static World.ChunkData? LoadChunk(string saveName, int chunkX, int chunkY, bool isUnderground)
        {
            string subDir = isUnderground ? $"{saveName}_cavechunks" : $"{saveName}_chunks";
            return LoadChunkFromDir(subDir, chunkX, chunkY);
        }

        /// <summary>Charge un chunk appartenant à un donjon précis (voir SaveDungeonChunk).</summary>
        public static World.ChunkData? LoadDungeonChunk(string saveName, int instanceId, int chunkX, int chunkY)
        {
            return LoadChunkFromDir($"{saveName}_dungeon{instanceId}_chunks", chunkX, chunkY);
        }

        private static World.ChunkData? LoadChunkFromDir(string subDir, int chunkX, int chunkY)
        {
            var chunkData = LoadChunkData(subDir, chunkX, chunkY);
            if (chunkData == null)
                return null;
            
            var chunk = new World.ChunkData
            {
                Objects = new Dictionary<(int x, int y), int>(),
                Overlays = new Dictionary<(int x, int y), int>(),
                Heights = new Dictionary<(int x, int y), int>(),
                Decorations = new Dictionary<(int x, int y), int>(),
                Variations = new Dictionary<(int x, int y), int>(),
                Containers = new Dictionary<(int x, int y), ContainerInventoryData>(),
                ArmorStands = new Dictionary<(int x, int y), ArmorStandData>(),
                GroundOverrides = new Dictionary<(int x, int y), int>(),
                Crops = new Dictionary<(int x, int y), World.CropData>(),
                RoofHeights = new Dictionary<(int x, int y), int>(),
                RoofTileInfo = new Dictionary<(int x, int y), World.RoofTileInfo>(),
                WallCoverings = new Dictionary<(int x, int y), int>(),
                BannerHolders = new Dictionary<(int x, int y), BannerHolderData>(),
                TileMeta = new Dictionary<(int x, int y), Dictionary<string, string>>(),
                IsGenerated = true,
                LastAccessTime = (float)Raylib.GetTime()
            };
            
            foreach (var kv in chunkData.Objects)
            {
                var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
                chunk.Objects[(x, y)] = kv.Value;
            }
            
            if (chunkData.Overlays != null)
            {
                foreach (var kv in chunkData.Overlays)
                {
                    var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
                    chunk.Overlays[(x, y)] = kv.Value;
                }
            }

            if (chunkData.BuildingIds != null)
            {
                foreach (var kv in chunkData.BuildingIds)
                {
                    var (worldX, worldY) = UnpackTileKey(kv.Key, chunkX, chunkY);
                    World.SetBuildingIdForTile(worldX, worldY, kv.Value);
                }
            }
            
            foreach (var kv in chunkData.Heights)
            {
                var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
                chunk.Heights[(x, y)] = kv.Value;
            }
            
            foreach (var kv in chunkData.Decorations)
            {
                var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
                chunk.Decorations[(x, y)] = kv.Value;
            }
            
            foreach (var kv in chunkData.Variations)
            {
                var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
                chunk.Variations[(x, y)] = kv.Value;
            }
            
            if (chunkData.RoofHeights != null)
            {
                foreach (var kv in chunkData.RoofHeights)
                {
                    var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
                    chunk.RoofHeights[(x, y)] = kv.Value;
                }
            }

            if (chunkData.RoofTileInfo != null)
            {
                foreach (var kv in chunkData.RoofTileInfo)
                {
                    var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
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

            World.RestoreRoofTileInfoForChunk(chunk);
            
            if (chunkData.WallCoverings != null)
            {
                foreach (var kv in chunkData.WallCoverings)
                {
                    var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
                    chunk.WallCoverings[(x, y)] = kv.Value;
                }
            }
            
            if (chunkData.Containers != null)
            {
                foreach (var cont in chunkData.Containers)
                {
                    var (x, y) = UnpackTileKey(cont.Key, chunkX, chunkY);
                    
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
                            var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == slotSave.ItemName);
                            if (itemData.ID != 0)
                            {
                                var restoredItem = new Item(itemData.Name, slotSave.Count, itemData.Color, itemData.Icon);
                                restoredItem.RestoreMetadataFromSave(slotSave.Metadata ?? "");
                                if (itemData.RuneSlots > 0)
                                {
                                    restoredItem.EnsureRuneSlots(itemData.RuneSlots);
                                    restoredItem.LoadRunesFromMetadata();
                                }
                                
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
            
            if (chunkData.ArmorStands != null)
            {
                foreach (var stand in chunkData.ArmorStands)
                {
                    var (x, y) = UnpackTileKey(stand.Key, chunkX, chunkY);
                    var standData = new ArmorStandData();
                    standData.CurrentPose = stand.Value.CurrentPose ?? "idle"; 
                    if (!string.IsNullOrEmpty(stand.Value.Head))
                        standData.Head = CreateItemFromName(stand.Value.Head);
                    if (!string.IsNullOrEmpty(stand.Value.Body))
                        standData.Body = CreateItemFromName(stand.Value.Body);
                    if (!string.IsNullOrEmpty(stand.Value.Legs))
                        standData.Legs = CreateItemFromName(stand.Value.Legs);
                    if (!string.IsNullOrEmpty(stand.Value.MainHand))
                        standData.MainHand = CreateItemFromName(stand.Value.MainHand);
                    if (!string.IsNullOrEmpty(stand.Value.OffHand))
                        standData.OffHand = CreateItemFromName(stand.Value.OffHand);
                    if (!string.IsNullOrEmpty(stand.Value.Face))
                        standData.Face = CreateItemFromName(stand.Value.Face);
                    if (!string.IsNullOrEmpty(stand.Value.Ears))
                        standData.Ears = CreateItemFromName(stand.Value.Ears);
                    if (!string.IsNullOrEmpty(stand.Value.Neck))
                        standData.Neck = CreateItemFromName(stand.Value.Neck);
                    if (!string.IsNullOrEmpty(stand.Value.Waist))
                        standData.Waist = CreateItemFromName(stand.Value.Waist);
                    if (!string.IsNullOrEmpty(stand.Value.Feet))
                        standData.Feet = CreateItemFromName(stand.Value.Feet);
                    if (!string.IsNullOrEmpty(stand.Value.Back))
                        standData.Back = CreateItemFromName(stand.Value.Back);
                    chunk.ArmorStands[(x, y)] = standData;
                }
            }

            if (chunkData.BannerHolders != null)
            {
                foreach (var kv in chunkData.BannerHolders)
                {
                    var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
                    chunk.BannerHolders[(x, y)] = new BannerHolderData
                    {
                        HasBanner = kv.Value.HasBanner,
                        PatternName = kv.Value.PatternName,
                        ShapeName = kv.Value.ShapeName,
                        BgR = kv.Value.BgR, BgG = kv.Value.BgG, BgB = kv.Value.BgB,
                        PatR = kv.Value.PatR, PatG = kv.Value.PatG, PatB = kv.Value.PatB,
                    };
                }
            }
            
            if (chunkData.GroundOverrides != null)
            {
                foreach (var kv in chunkData.GroundOverrides)
                {
                    var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
                    chunk.GroundOverrides[(x, y)] = kv.Value;
                }
            }
            
            if (chunkData.Crops != null)
            {
                foreach (var crop in chunkData.Crops)
                {
                    var (x, y) = UnpackTileKey(crop.Key, chunkX, chunkY);
                    chunk.Crops[(x, y)] = new World.CropData
                    {
                        CropTileId = crop.Value.CropTileId,
                        PlantTime = crop.Value.PlantTime,
                        AccumulatedGrowthTime = crop.Value.AccumulatedGrowthTime
                    };
                }
            }
            
            if (chunkData.Aquariums != null)
            {
                foreach (var kv in chunkData.Aquariums)
                {
                    var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
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
            
            if (chunkData.TileMeta != null)
            {
                foreach (var kv in chunkData.TileMeta)
                {
                    var (x, y) = UnpackTileKey(kv.Key, chunkX, chunkY);
                    if (kv.Value != null && kv.Value.Count > 0)
                        chunk.TileMeta[(x, y)] = new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase);
                }
            }

            //  MIGRATION : anciennes ruches sauvegardées comme overlay 6000 (avant le passage
            // à TileMeta) → reconverties en métadonnée "beehive" à leur premier chargement.
            foreach (var pos in chunk.Overlays.Where(kv => kv.Value == 6000).Select(kv => kv.Key).ToList())
            {
                chunk.SetTileMeta(pos.x, pos.y, "beehive", "1");
                chunk.Overlays.Remove(pos);
            }

            //  MIGRATION : anciennes veines de minerai sauvegardées comme overlay 1001..1009
            // doivent passer en métadonnée de tuile "ore".
            foreach (var pos in chunk.Overlays.Where(kv => kv.Value >= 1001 && kv.Value <= 1009).Select(kv => kv.Key).ToList())
            {
                string oreKey = World.GetOreKeyForLegacyOverlayId(chunk.Overlays[pos]);
                if (!string.IsNullOrWhiteSpace(oreKey))
                {
                    chunk.SetTileMeta(pos.x, pos.y, "ore", oreKey);
                }
                chunk.Overlays.Remove(pos);
            }

            if (chunkData.Entities != null)
            {
                foreach (var entityData in chunkData.Entities)
                {
                    var entity = new Entity(new Vector2(entityData.WorldPosX, entityData.WorldPosY), entityData.Species, false, skipHomeAutoAssign: true);
                    entity.Tint = RestoreColor(entityData.Tint, entity.Tint);
                    entity.HairColor = RestoreColor(entityData.HairColor, entity.HairColor);
                    entity.HairStyle = entityData.HairStyle;
                    entity.BeardStyle = entityData.BeardStyle;
                    RestoreRandomFeatures(entity, entityData);
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
                    entity.OwnerNpcId = (!string.IsNullOrEmpty(entityData.OwnerNpcId) && Guid.TryParse(entityData.OwnerNpcId, out var entityOwnerNpcId))
                        ? entityOwnerNpcId
                        : (Guid?)null;
                    entity.OwnedMountNetId = (!string.IsNullOrEmpty(entityData.OwnedMountNetId) && Guid.TryParse(entityData.OwnedMountNetId, out var entityOwnedMountNetId))
                        ? entityOwnedMountNetId
                        : (Guid?)null;
                    entity.IsRidingMount = entityData.IsRidingMount;
                    entity.CustomName = string.IsNullOrEmpty(entityData.CustomName) ? null : entityData.CustomName;
                    entity.HomePosition = new Vector2(entityData.HomePosX, entityData.HomePosY);
                    entity.HomeBuildingId = entityData.HomeBuildingId;
                    //  BUGFIX (rechargement de sauvegarde) : voir le commentaire jumeau dans
                    // RestoreEntityFromSave — Decide() a pu poser un AiState/AiTarget "par défaut"
                    // avant que le vrai foyer ne soit connu ; on repart d'un état neutre.
                    entity.AiState = NpcAiState.Idle;
                    entity.AiTarget = entity.WorldPos;
                    entity.Hunger = entityData.Hunger;
                    entity.WorkplacePosition = entityData.WorkplacePosX.HasValue && entityData.WorkplacePosY.HasValue
                        ? new Vector2(entityData.WorkplacePosX.Value, entityData.WorkplacePosY.Value)
                        : null;
                    entity.WorkZoneId = entityData.WorkZoneId;
                    entity.IsTrader = entityData.IsTrader;
                    entity.Profession = entityData.Profession >= 0 ? (ProfessionType)entityData.Profession : ProfessionType.None;
                    if (entity.Profession == ProfessionType.Guard)
                        entity.ConfigureVillageGuardStats();
                    else if (entity.IsVillager)
                        entity.Attack = SpeciesData.GetSpeciesInfo(entity.Species)?.Attack ?? entity.Attack;
                    entity.TraderItems = entityData.TraderItemIds?.ToList() ?? new List<int>();
                    entity.Friendship = entityData.Friendship;
                    entity.IsBaby = entityData.IsBaby;
                    entity.Age = entityData.Age;
                    entity.GrowthTime = entityData.GrowthTime;
                    entity.GestationTimer = entityData.GestationTimer;
                    entity.Scale = entityData.Scale;
                    entity.SlimeIncubationSeconds = entityData.SlimeIncubationSeconds;
                    entity.IsInterestedInFood = entityData.IsInterestedInFood;
                    entity.PreferredFoodId = entityData.PreferredFoodId;
                    entity.FollowOrder = entityData.FollowOrder;
                    entity.IsTalking = entityData.IsTalking;
                    entity.TalkTimer = entityData.TalkTimer;
                    entity.TalkCooldown = entityData.TalkCooldown;
                    entity.TalkText = string.IsNullOrEmpty(entityData.TalkText) ? null : entityData.TalkText;
                    RestoreQuestData(entity, entityData);

                    if (entityData.Equipment != null)
                    {
                        entity.Equipment ??= new Equipment();
                        RestoreEquipmentFromSave(entity.Equipment, entityData.Equipment);
                    }
                    else if (entity.Profession == ProfessionType.Guard)
                    {
                        entity.EquipVillageGuard();
                    }

                    if (entityData.InventorySlots != null && entityData.InventorySlots.Count > 0)
                    {
                        entity.Inventory = new ContainerInventoryData(entityData.InventorySlots.Count, 4);
                        for (int i = 0; i < entityData.InventorySlots.Count && i < entity.Inventory.Slots.Count; i++)
                            RestoreInventorySlotFromSave(entityData.InventorySlots[i], entity.Inventory.Slots[i]);
                    }
                    
                    if (entity.Profession == ProfessionType.Guard)
                    {
                        entity.IsTrader = false;
                        entity.TraderItems.Clear();
                    }
                    else if (entity.Profession != ProfessionType.None || entityData.IsTrader || entity.TraderItems.Count > 0)
                    {
                        entity.IsTrader = true;
                        if (entity.TraderItems.Count == 0)
                            entity.AssignProfessionAndTrade();
                    }
                    
                    if (entityData.TamedBehavior >= 0 && entityData.TamedBehavior <= 3)
                        entity.TamedBehavior = (TamedAnimalMode)entityData.TamedBehavior;
                    
                    if (entity.IsTamed)
                        entity.Behavior = "tamed";
                    
                    chunk.Entities.Add(entity);
                }
            }
            
            return chunk;
        }
        
        private static Item? CreateItemFromName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == name);
            if (itemData.ID != 0)
            {
                var item = new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
                return item;
            }
            return null;
        }
        
        private static void UpdateSaveIndex(string saveName, GameSaveData data)
        {
            var saves = GetSaves();
            var existing = saves.FirstOrDefault(s => s.Name == saveName);
            
            if (existing != null)
            {
                existing.LastPlayed = DateTime.Now;
                existing.PlayerX = data.PlayerPosX;
                existing.PlayerY = data.PlayerPosY;
                existing.PlayTime = data.PlayTime;
            }
            else
            {
                saves.Add(new SaveInfo
                {
                    Name = saveName,
                    CreatedAt = DateTime.Now,
                    LastPlayed = DateTime.Now,
                    PlayerX = data.PlayerPosX,
                    PlayerY = data.PlayerPosY,
                    PlayTime = data.PlayTime
                });
            }

            saves = saves
                .OrderByDescending(s => s.LastPlayed)
                .ThenByDescending(s => s.CreatedAt)
                .ToList();
            
            string indexPath = Path.Combine(SAVE_DIR, SAVES_INDEX);
            string json = JsonSerializer.Serialize(saves, WriteOptions);
            File.WriteAllText(indexPath, json);
        }
        
        private static void UpdateSaveIndexAfterDelete(string saveName)
        {
            var saves = GetSaves();
            saves.RemoveAll(s => s.Name == saveName);
            
            string indexPath = Path.Combine(SAVE_DIR, SAVES_INDEX);
            string json = JsonSerializer.Serialize(saves, WriteOptions);
            File.WriteAllText(indexPath, json);
        }
        
        public static void SaveCavePairs(string saveName, Dictionary<(int surfaceX, int surfaceY), (int caveX, int caveY)> pairs)
        {
            string savePath = Path.Combine(SAVE_DIR, $"{saveName}_cavepairs.json");
            
            var savePairs = new List<CavePairSave>();
            foreach (var pair in pairs)
            {
                savePairs.Add(new CavePairSave
                {
                    EntryX = pair.Key.surfaceX,
                    EntryY = pair.Key.surfaceY,
                    ExitX = pair.Value.caveX,
                    ExitY = pair.Value.caveY
                });
            }
            
            string json = JsonSerializer.Serialize(savePairs, WriteOptions);
            File.WriteAllText(savePath, json);
        }

        public static Dictionary<(int, int), (int, int)> LoadCavePairs(string saveName)
        {
            string savePath = Path.Combine(SAVE_DIR, $"{saveName}_cavepairs.json");
            if (!File.Exists(savePath))
                return new Dictionary<(int, int), (int, int)>();
            
            string json = File.ReadAllText(savePath);
            var savePairs = JsonSerializer.Deserialize<List<CavePairSave>>(json, ReadOptions) ?? new List<CavePairSave>();
            
            var result = new Dictionary<(int, int), (int, int)>();
            foreach (var pair in savePairs)
            {
                result[(pair.EntryX, pair.EntryY)] = (pair.ExitX, pair.ExitY);
            }
            return result;
        }

        public static void SaveDungeons(string saveName, Dictionary<int, DungeonInstance> instances,
            Dictionary<(int x, int y), DungeonPortalInfo> portals, int nextInstanceId)
        {
            string savePath = Path.Combine(SAVE_DIR, $"{saveName}_dungeons.json");

            var file = new DungeonsSaveFile { NextInstanceId = nextInstanceId };
            foreach (var inst in instances.Values)
            {
                file.Instances.Add(new DungeonInstanceSave
                {
                    Id = inst.Id,
                    OriginX = inst.OriginX,
                    OriginY = inst.OriginY,
                    Width = inst.Width,
                    Height = inst.Height,
                    EntryX = inst.EntryPos.x,
                    EntryY = inst.EntryPos.y,
                    RestSpawnX = inst.RestSpawnTile.x,
                    RestSpawnY = inst.RestSpawnTile.y,
                    ExitDoorX = inst.ExitDoorTile.x,
                    ExitDoorY = inst.ExitDoorTile.y,
                    Generated = inst.Generated,
                    BossDefeated = inst.BossDefeated,
                    Seed = inst.Seed
                });
            }
            foreach (var kv in portals)
            {
                file.Portals.Add(new DungeonPortalSave
                {
                    X = kv.Key.x,
                    Y = kv.Key.y,
                    Kind = (int)kv.Value.Kind,
                    InstanceId = kv.Value.InstanceId
                });
            }

            string json = JsonSerializer.Serialize(file, WriteOptions);
            File.WriteAllText(savePath, json);
        }

        public static (Dictionary<int, DungeonInstance> instances, Dictionary<(int x, int y), DungeonPortalInfo> portals, int nextInstanceId) LoadDungeons(string saveName)
        {
            string savePath = Path.Combine(SAVE_DIR, $"{saveName}_dungeons.json");
            var instances = new Dictionary<int, DungeonInstance>();
            var portals = new Dictionary<(int x, int y), DungeonPortalInfo>();
            if (!File.Exists(savePath))
                return (instances, portals, 0);

            string json = File.ReadAllText(savePath);
            var file = JsonSerializer.Deserialize<DungeonsSaveFile>(json, ReadOptions) ?? new DungeonsSaveFile();

            foreach (var s in file.Instances)
            {
                instances[s.Id] = new DungeonInstance
                {
                    Id = s.Id,
                    OriginX = s.OriginX,
                    OriginY = s.OriginY,
                    Width = s.Width,
                    Height = s.Height,
                    EntryPos = (s.EntryX, s.EntryY),
                    RestSpawnTile = (s.RestSpawnX, s.RestSpawnY),
                    ExitDoorTile = (s.ExitDoorX, s.ExitDoorY),
                    Generated = s.Generated,
                    BossDefeated = s.BossDefeated,
                    Seed = s.Seed
                };
            }
            foreach (var p in file.Portals)
            {
                portals[(p.X, p.Y)] = new DungeonPortalInfo { Kind = (DungeonPortalKind)p.Kind, InstanceId = p.InstanceId };
            }

            return (instances, portals, file.NextInstanceId);
        }

        public static void SavePortals(string saveName, Dictionary<(int x, int y, bool isCave), PortalInfo> portals)
        {
            string savePath = Path.Combine(SAVE_DIR, $"{saveName}_portals.json");

            var file = new PortalsSaveFile();
            foreach (var info in portals.Values)
            {
                file.Portals.Add(new PortalSave
                {
                    X = info.TileX,
                    Y = info.TileY,
                    IsCave = info.IsCave,
                    Name = info.Name,
                    DiscoveredAt = info.DiscoveredAt,
                    IsFavorite = info.IsFavorite
                });
            }

            string json = JsonSerializer.Serialize(file, WriteOptions);
            File.WriteAllText(savePath, json);
        }

        public static Dictionary<(int x, int y, bool isCave), PortalInfo> LoadPortals(string saveName)
        {
            string savePath = Path.Combine(SAVE_DIR, $"{saveName}_portals.json");
            var result = new Dictionary<(int x, int y, bool isCave), PortalInfo>();
            if (!File.Exists(savePath))
                return result;

            string json = File.ReadAllText(savePath);
            var file = JsonSerializer.Deserialize<PortalsSaveFile>(json, ReadOptions) ?? new PortalsSaveFile();

            foreach (var p in file.Portals)
            {
                var info = new PortalInfo
                {
                    TileX = p.X,
                    TileY = p.Y,
                    IsCave = p.IsCave,
                    Name = p.Name ?? "",
                    DiscoveredAt = p.DiscoveredAt,
                    IsFavorite = p.IsFavorite
                };
                result[(p.X, p.Y, p.IsCave)] = info;
            }

            return result;
        }

        public static void LoadChunks(string saveName, List<Entity> globalEntities)
        {
            bool wasUnderground = World.IsUnderground;
            World.IsUnderground = false;
            try
            {
                foreach (var chunkData in EnumerateChunkData($"{saveName}_chunks"))
                {
                    World.LoadChunkFromSave(chunkData);

                    foreach (var entityData in chunkData.Entities)
                    {
                        RestoreEntityFromSave(entityData, globalEntities);
                    }
                }

                World.IsUnderground = true;
                foreach (var chunkData in EnumerateChunkData($"{saveName}_cavechunks"))
                {
                    World.LoadChunkFromSave(chunkData);

                    foreach (var entityData in chunkData.Entities)
                    {
                        RestoreEntityFromSave(entityData, globalEntities);
                    }
                }
            }
            finally
            {
                World.IsUnderground = wasUnderground;
            }
        }
        
        public static void ResolveOwnedMountReferences(IEnumerable<Entity> allEntities)
        {
            if (allEntities == null)
                return;

            foreach (var entity in allEntities)
            {
                entity.ResolveOwnedMountReference(allEntities);
            }

            foreach (var entity in allEntities)
            {
                if (!entity.IsRidingMount || entity.OwnedMount == null || !entity.OwnedMount.IsAlive)
                    continue;

                if (entity.OwnedMount.IsMounted && entity.OwnedMount.Rider == entity)
                    continue;

                entity.OwnedMount.Mount(entity);
            }
        }

        public static long GetSaveSize(string saveName)
        {
            long totalSize = 0;
            string savePath = Path.Combine(SAVE_DIR, $"{saveName}.json");
            if (File.Exists(savePath))
                totalSize += new FileInfo(savePath).Length;
            
            totalSize += GetChunkStoreFileSize($"{saveName}_chunks");
            totalSize += GetChunkStoreFileSize($"{saveName}_cavechunks");
            foreach (string dungeonStore in Directory.Exists(SAVE_DIR)
                ? Directory.GetFiles(SAVE_DIR, $"{saveName}_dungeon*_chunks.bin")
                : Array.Empty<string>())
                totalSize += new FileInfo(dungeonStore).Length;
            
            return totalSize;
        }
        
        public static (int surfaceChunks, int caveChunks) GetChunkCount(string saveName)
        {
            int surfaceChunks = 0;
            int caveChunks = 0;
            
            surfaceChunks = GetChunkStoreCount($"{saveName}_chunks");
            caveChunks = GetChunkStoreCount($"{saveName}_cavechunks");
            
            return (surfaceChunks, caveChunks);
        }
    }
    
    // ===== CLASSES DE DONNÉES =====
    
    public class SaveInfo
    {
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastPlayed { get; set; }
        public float PlayerX { get; set; }
        public float PlayerY { get; set; }
        public double PlayTime { get; set; }
    }
    
    //  MULTIJOUEUR : sauvegarde persistante d'un joueur invité sur un monde hébergé, pour
    // qu'il retrouve son personnage (position, stats, inventaire, équipement, apparence)
    // au lieu de repartir de zéro à chaque connexion. Le host la stocke dans
    // Saves/{worldName}_players.json, indexée par un identifiant stable généré une fois
    // et conservé sur la machine du joueur (voir Program.LocalPlayerGuid).
    public class GuestPlayerSaveData
    {
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float Facing { get; set; } = 1f;
        public bool Underground { get; set; }
        public int HP { get; set; } = 20;
        public int MaxHP { get; set; } = 20;
        public float Stamina { get; set; } = 100f;
        public float MaxStamina { get; set; } = 100f;
        public float Hunger { get; set; } = 100f;
        public float MaxHunger { get; set; } = 100f;
        public float Thirst { get; set; } = 100f;
        public float MaxThirst { get; set; } = 100f;
        public int HairStyle { get; set; }
        public int BeardStyle { get; set; }
        public int EyeStyle { get; set; }
        public string Species { get; set; } = "human";
        public ColorSave Skin { get; set; } = new ColorSave { R = 255, G = 235, B = 200, A = 255 };
        public ColorSave Hair { get; set; } = new ColorSave { R = 60, G = 40, B = 30, A = 255 };
        public ColorSave Eye { get; set; } = new ColorSave { R = 100, G = 150, B = 200, A = 255 };
        public List<InventorySlotSave> InventorySlots { get; set; } = new();
        public EquipmentSave Equipment { get; set; } = new();
        public string LastSeenUtc { get; set; } = "";
    }

    public class GameSaveData
    {
        public int WorldSeed { get; set; }
        public bool IsUnderground { get; set; }
        public bool IsInDungeon { get; set; }
        public int DungeonInstanceId { get; set; } = -1;
        public float WorldSpawnX { get; set; }
        public float WorldSpawnY { get; set; }
        public bool HasDied { get; set; } = false;
        public float DeathPosX { get; set; }
        public float DeathPosY { get; set; }
        public float PlayerPosX { get; set; }
        public float PlayerPosY { get; set; }
        public float PlayerFacing { get; set; }
        public bool IsGodMode { get; set; } = false;
        public int PlayerHP { get; set; }
        public int PlayerMaxHP { get; set; }
        public float PlayerStamina { get; set; } = 100f;
        public float PlayerMaxStamina { get; set; } = 100f;
        public float PlayerHunger { get; set; } = 100f;
        public float PlayerMaxHunger { get; set; } = 100f;
        public float PlayerThirst { get; set; } = 100f;
        public float PlayerMaxThirst { get; set; } = 100f;
        public int HotbarSlot { get; set; }
        public float CameraZoom { get; set; }
		public int CharacterSelectionBackgroundStyle { get; set; } = 0;
        public double PlayTime { get; set; }
        public float GameTime { get; set; }
        public float TravelDistance { get; set; }
        public string WeatherType { get; set; } = "clear";
        public float WeatherTimeRemaining { get; set; }
        public int PlayerHairStyle { get; set; } = 0;
        public int PlayerBeardStyle { get; set; } = 0;
        public string PlayerMorphSpecies { get; set; } = "human";
        public ColorSave? PlayerMorphTint { get; set; }
        public Dictionary<string, int>? PlayerMorphFeatureVariants { get; set; }
        public Dictionary<string, ColorSave>? PlayerMorphFeatureColors { get; set; }
        public List<AchievementSaveData> Achievements { get; set; } = new();
		public List<int> PreviouslyOwnedItemIds { get; set; } = new();
        public Dictionary<string, int> LastCropStages { get; set; } = new();
        public Dictionary<string, float> FarmlandMoisture { get; set; } = new();
        
        public int PlayerSkinColorR { get; set; } = 255;
        public int PlayerSkinColorG { get; set; } = 235;
        public int PlayerSkinColorB { get; set; } = 200;
        public int PlayerSkinColorA { get; set; } = 255;
        public int PlayerHairColorR { get; set; } = 60;
        public int PlayerHairColorG { get; set; } = 40;
        public int PlayerHairColorB { get; set; } = 30;
        public int PlayerHairColorA { get; set; } = 255;
        public int PlayerEyeStyle { get; set; } = 0;
        public int PlayerEyeColorR { get; set; } = 100;
        public int PlayerEyeColorG { get; set; } = 150;
        public int PlayerEyeColorB { get; set; } = 200;
        public int PlayerEyeColorA { get; set; } = 255;
        
        public List<InventorySlotSave> InventorySlots { get; set; } = new();
        public EquipmentSave Equipment { get; set; } = new();
        public List<PlacedObjectSave> SurfacePlacedObjects { get; set; } = new();
        public List<PlacedObjectSave> CavePlacedObjects { get; set; } = new();
        public HashSet<string> DestroyedObjects { get; set; } = new();
        public Dictionary<string, WorldObjectHPSave> ObjectHPs { get; set; } = new();
        
        public List<BoatSaveData> Boats { get; set; } = new();
        public List<WagonLinkSaveData> WagonLinks { get; set; } = new();
        public HashSet<string> ExploredTiles { get; set; } = new();
        public GuildSaveData? GuildData { get; set; }
        public List<CarSaveData> Cars { get; set; } = new();
        
        public List<SteamEngineSave> SteamEngines { get; set; } = new();
        public List<DynamoSave> Dynamos { get; set; } = new();
        public List<BatterySave> Batteries { get; set; } = new();
        public List<QuestSaveData> Quests { get; set; } = new();
        public List<BatteryChargerSave> BatteryChargers { get; set; } = new();
        public List<AmpouleSave> Ampoules { get; set; } = new();
        public List<LogicGateSave> LogicGates { get; set; } = new();
        public List<RainCollectorSave> RainCollectors { get; set; } = new();
        public List<EnergyConnectionSave> EnergyConnections { get; set; } = new();
        public SkillsSaveData? Skills { get; set; } = new();
        // Options de monde (activables/désactivables à la création)
        public bool WeaponsBreak { get; set; } = true;
        public bool FoodSpoils { get; set; } = true;
    }

    public class WagonLinkSaveData
    {
        public int AX { get; set; }
        public int AY { get; set; }
        public int BX { get; set; }
        public int BY { get; set; }
        public float Length { get; set; }
        public List<WagonLinkPointSaveData> Path { get; set; } = new();
    }

    public class WagonLinkPointSaveData
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
    
    // Classes de sauvegarde pour les systèmes énergétiques
    public class SteamEngineSave
    {
        public int X { get; set; }
        public int Y { get; set; }
        public float Water { get; set; }
        public float Coal { get; set; }
        public float SteamPressure { get; set; }
        public float ProductionRate { get; set; }
        public float LastUpdateTime { get; set; }
        public bool IsOn { get; set; }
    }

    public class RainCollectorSave
    {
        public int X { get; set; }
        public int Y { get; set; }
        public float CurrentWater { get; set; }
        public float Capacity { get; set; }
        public float LastUpdateTime { get; set; }
    }
    
    public class DynamoSave
    {
        public int X { get; set; }
        public int Y { get; set; }
        public float SteamInput { get; set; }
        public float ElectricityOutput { get; set; }
        public float LastUpdateTime { get; set; }
        public List<EnergyConnectionSave> Connections { get; set; } = new();
    }
    
    public class BatterySave
    {
        public int X { get; set; }
        public int Y { get; set; }
        public float StoredEnergy { get; set; }
        public float MaxCapacity { get; set; }
        public List<EnergyConnectionSave> Connections { get; set; } = new();
    }
    
    public class BatteryChargerSave
    {
        public int X { get; set; }
        public int Y { get; set; }
        public bool IsOn { get; set; }
        public bool HasPower { get; set; }
        public float LastUpdateTime { get; set; }
        public BatterySave? InsertedBattery { get; set; }
    }
    
    public class AmpouleSave
    {
        public int X { get; set; }
        public int Y { get; set; }
        public bool IsOn { get; set; }
        public float LastUpdateTime { get; set; }
        public bool WasLitLastFrame { get; set; }
    }

    public class LogicGateSave
    {
        public int X { get; set; }
        public int Y { get; set; }
        public GateType Type { get; set; } = GateType.AND;
        public bool OutputState { get; set; }
        public int ConnectedInputs { get; set; }
        public int ActiveInputs { get; set; }
        public bool ManualState { get; set; }
        public bool PendingPress { get; set; }
        public float DelaySeconds { get; set; } = 1f;
        public bool DelayLastRawInput { get; set; }
        public List<LogicGateDelayEntrySave> DelayQueue { get; set; } = new();
        public List<EnergyConnectionSave> Connections { get; set; } = new();
        public float LastUpdateTime { get; set; }
    }

    public class LogicGateDelayEntrySave
    {
        public float ReadyAt { get; set; }
        public bool State { get; set; }
    }
    
    public class EnergyConnectionSave
    {
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
        public string Id { get; set; } = Guid.NewGuid().ToString();
    }
    
    public class RuneSaveData
    {
        public RuneType Type { get; set; } = RuneType.Resistance;
        public float Value { get; set; } = 1f;
    }

    public class InventorySlotSave
    {
        public int SlotIndex { get; set; } = -1;
        public string ItemName { get; set; } = "";
        public string? Metadata { get; set; }
        public Dictionary<string, string>? Meta { get; set; }
        public List<RuneSaveData?> Runes { get; set; } = new();
        public int Count { get; set; }
        public bool IsEmpty { get; set; } = true;
        public List<ColorSave?> CustomColors { get; set; } = new();
        public PortableContainerSaveData? PortableContainer { get; set; }
        public BackpackSaveData? Backpack { get; set; }
    }
    
    public class PortableContainerSaveData
    {
        public List<InventorySlotSave> Slots { get; set; } = new();
        public int Columns { get; set; } = 3;
        public int Rows { get; set; } = 3;
    }
    
    public class BackpackSaveData
    {
        public List<InventorySlotSave> Slots { get; set; } = new();
        public int Columns { get; set; } = 5;
        public int Rows { get; set; } = 3;
    }
    
    public class EquipmentSave
    {
        public string MainHand { get; set; } = "";
        public string OffHand { get; set; } = "";
        public string Backpack { get; set; } = "";

        public List<ColorSave?> MainHandCustomColors { get; set; } = new();
        public string MainHandMetadata { get; set; } = "";
        public Dictionary<string, string>? MainHandMeta { get; set; }
        public List<RuneSaveData?> MainHandRunes { get; set; } = new();

        public List<ColorSave?> OffHandCustomColors { get; set; } = new();
        public string OffHandMetadata { get; set; } = "";
        public Dictionary<string, string>? OffHandMeta { get; set; }
        public List<RuneSaveData?> OffHandRunes { get; set; } = new();

        public List<ColorSave?> BackpackCustomColors { get; set; } = new();
        public string BackpackMetadata { get; set; } = "";
        public Dictionary<string, string>? BackpackMeta { get; set; }
        public List<RuneSaveData?> BackpackRunes { get; set; } = new();

        public List<EquippedItemSave> EquippedItems { get; set; } = new();

        //  Items équipés couvrant une ou plusieurs zones du corps (système unifié)
        public List<ZoneItemSave> ZoneItems { get; set; } = new();

        public PortableContainerSaveData? OffHandContainer { get; set; }
        public BackpackSaveData? BackpackContainer { get; set; }
    }

    //  Item équipé couvrant une/plusieurs BodyZone (casque, plastron, écharpe, lunettes...)
    public class ZoneItemSave
    {
        public string ItemName { get; set; } = "";
        public List<ColorSave?> CustomColors { get; set; } = new();
        public string Metadata { get; set; } = ""; //  Encode notamment les runes serties (voir Item.SyncRunesToMetadata)
        public Dictionary<string, string>? Meta { get; set; }
        public List<RuneSaveData?> Runes { get; set; } = new();
    }
    
    
    public class EquippedItemSave
    {
        public EquipmentSlot Slot { get; set; }
        public int Index { get; set; }
        public string ItemName { get; set; } = "";
        public int Count { get; set; }
        public List<RuneSaveData?> Runes { get; set; } = new();
        public List<ColorSave?> CustomColors { get; set; } = new();
        public string Metadata { get; set; } = ""; //  Encode notamment les runes serties, si applicable
        public Dictionary<string, string>? Meta { get; set; }
    }

    public class PlacedObjectSave
    {
        public string Key { get; set; } = "";
        public int ItemId { get; set; }
        public float WorldPosX { get; set; }
        public float WorldPosY { get; set; }
    }
    
    public class WorldObjectHPSave
    {
        public int Current { get; set; }
        public int Max { get; set; }
    }
    
    public class ChunkSaveData
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        //  OPTIMISATION POIDS : clé de tuile compactée en un seul int au lieu d'une chaîne "x_y".
        // Toutes les positions du monde (Objects, Overlays, BuildingIds, Aquariums, ...) sont
        // exprimées en coordonnées MONDE dans World.ChunkData ; on les convertit en local au
        // chunk (0 <= local < CHUNK_SIZE) avant de les empaqueter, puis on reconvertit en monde
        // au chargement. Voir SaveSystem.PackTileKey / UnpackTileKey.
        public Dictionary<int, int> Objects { get; set; } = new();
        public Dictionary<int, int> Overlays { get; set; } = new();
        public Dictionary<int, int> Heights { get; set; } = new();
        public Dictionary<int, int> Decorations { get; set; } = new();
        public Dictionary<int, int> Variations { get; set; } = new();
        public Dictionary<int, ContainerInventorySave> Containers { get; set; } = new();
        public Dictionary<int, ArmorStandSaveData> ArmorStands { get; set; } = new();
        public Dictionary<int, int> GroundOverrides { get; set; } = new();
        public Dictionary<int, CropSaveData> Crops { get; set; } = new();
        public Dictionary<int, int> RoofHeights { get; set; } = new();
        public Dictionary<int, RoofTileInfoSaveData> RoofTileInfo { get; set; } = new();
        public Dictionary<int, int> WallCoverings { get; set; } = new();
        public Dictionary<int, BannerHolderSaveData> BannerHolders { get; set; } = new();
        public Dictionary<int, int> BuildingIds { get; set; } = new();
        public Dictionary<int, List<AquariumFishSaveData>> Aquariums { get; set; } = new();
        //  Métadonnées de tuile génériques (voir World.ChunkData.TileMeta) : un seul champ,
        // extensible par convention de clé, au lieu d'un nouveau Dictionary<string, T> ici à
        // chaque nouvelle fonctionnalité de tuile (ruche, minerai, câblage électrique, etc.).
        public Dictionary<int, Dictionary<string, string>> TileMeta { get; set; } = new();
        public List<EntitySaveData> Entities { get; set; } = new();
    }
    
    public class ContainerInventorySave
    {
        public List<InventorySlotSave> Slots { get; set; } = new();
    }
    
    public class ArmorStandSaveData
    {
        public string? Head { get; set; }
        public string? Body { get; set; }
        public string? Legs { get; set; }
        public string? MainHand { get; set; }
        public string? OffHand { get; set; }
        //  Zones supplémentaires, alignées sur le menu d'équipement du joueur.
        public string? Face { get; set; }
        public string? Ears { get; set; }
        public string? Neck { get; set; }
        public string? Waist { get; set; }
        public string? Feet { get; set; }
        public string? Back { get; set; }
        public string CurrentPose { get; set; } = "idle";
    }

    public class BannerHolderSaveData
    {
        public bool HasBanner { get; set; } = true;
        public string PatternName { get; set; } = "";
        public string ShapeName { get; set; } = "";
        public byte BgR { get; set; }
        public byte BgG { get; set; }
        public byte BgB { get; set; }
        public byte PatR { get; set; }
        public byte PatG { get; set; }
        public byte PatB { get; set; }
    }
    
    public class CropSaveData
    {
        public int CropTileId { get; set; }
        public float PlantTime { get; set; }
        public float AccumulatedGrowthTime { get; set; }
    }

    public class RoofTileInfoSaveData
    {
        public int Height { get; set; }
        public bool IsHorizontalZone { get; set; }
        public int WallStackCount { get; set; }
        public string TexKind { get; set; } = "Default";
        public bool IsColumnAnchor { get; set; }
    }
    
    public class GuildSaveData
    {
        public string Name { get; set; } = "";
        public int LogoSize { get; set; } = 32;
        public List<ColorSave> LogoPixels { get; set; } = new();
        public List<string> MemberNetIds { get; set; } = new();
    }

    public class ColorSave
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; }
    }
    
    public class AquariumFishSaveData
    {
        public int SlotIndex { get; set; }
        public float LocalPosX { get; set; }
        public float LocalPosY { get; set; }
        public float TargetPosX { get; set; }
        public float TargetPosY { get; set; }
        public float MoveSpeed { get; set; }
        public bool IsMoving { get; set; }
        public float IdleTimer { get; set; }
        public float BobPhase { get; set; }
        public float BobSpeed { get; set; }
        public float MoveStartTime { get; set; }
        public float MoveDuration { get; set; }
        public float BaseY { get; set; }
    }
    
    public class QuestSaveData
    {
        public Guid Id { get; set; } = Guid.Empty;
        public int Type { get; set; } = 0; // 0 = DeliverItem, 1 = TameAndBring
        public Guid GiverId { get; set; } = Guid.Empty;
        public Guid TargetId { get; set; } = Guid.Empty;
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public int ItemQty { get; set; } = 1;
        public string TargetSpecies { get; set; } = "";
        public Guid OfferedAnimalId { get; set; } = Guid.Empty;
        public int RewardItemId { get; set; } = 0;
        public string RewardItemName { get; set; } = "";
        public int RewardItemQty { get; set; } = 0;
        public float LastKnownTargetPosX { get; set; }
        public float LastKnownTargetPosY { get; set; }
        public bool HasLastKnownTargetPosition { get; set; } = false;
        public float LastKnownGiverPosX { get; set; }
        public float LastKnownGiverPosY { get; set; }
        public bool HasLastKnownGiverPosition { get; set; } = false;
        public int State { get; set; } = 0;
    }

    public class EntitySaveData
    {
        public float WorldPosX { get; set; }
        public float WorldPosY { get; set; }
        public string Species { get; set; } = "";
        public ColorSave? Tint { get; set; }
        public ColorSave? HairColor { get; set; }
        public int HairStyle { get; set; }
        public int BeardStyle { get; set; }
        public Dictionary<string, int> RandomFeatureVariants { get; set; } = new();
        public Dictionary<string, ColorSave> RandomFeatureColors { get; set; } = new();
        public int CurrentHP { get; set; }
        public int MaxHP { get; set; }
        public bool IsBoss { get; set; } = false;
        public bool IsSpiritAnimal { get; set; }
        public int SpiritTotemTileX { get; set; } = int.MinValue;
        public int SpiritTotemTileY { get; set; } = int.MinValue;
        public string Behavior { get; set; } = "passive";
        public int? VisionRange { get; set; }
        public float AlertDuration { get; set; } = 0f;
        public bool IsAlerted { get; set; } = false;
        public float AlertTimer { get; set; } = 0f;
        public int HerdId { get; set; } = -1;
        public bool IsTamed { get; set; } = false;
        public bool IsGuildMember { get; set; } = false;
        public bool WantsToJoinGuild { get; set; } = false;
        public string OwnerName { get; set; } = "";
        //  NetId (en string, comme NetId ci-dessous) du PNJ propriétaire quand ce tamed
        // appartient à un PNJ plutôt qu'au joueur (don via quête TameAndBring). Vide si
        // apprivoisé par le joueur ou non apprivoisé. Voir Entity.OwnerNpcId.
        public string OwnerNpcId { get; set; } = "";
        //  NetId (en string) de la monture chevauchable de ce PNJ, si elle en a une (ex : loup
        // de compagnie d'un gobelin). Voir Entity.OwnedMount / OwnedMountNetId.
        public string OwnedMountNetId { get; set; } = "";
        //  Vrai si ce PNJ était en train de chevaucher sa monture au moment de la sauvegarde.
        public bool IsRidingMount { get; set; } = false;
        public string CustomName { get; set; } = "";
        public int TamedBehavior { get; set; } = 0;
        public float HomePosX { get; set; }
        public float HomePosY { get; set; }
        public int HomeBuildingId { get; set; } = -1;
        public float Hunger { get; set; } = 0f;
        public float? WorkplacePosX { get; set; }
        public float? WorkplacePosY { get; set; }
        public int WorkZoneId { get; set; } = -1;
        public bool IsTrader { get; set; } = false;
        public int Profession { get; set; } = 0;
        public List<int> TraderItemIds { get; set; } = new();
        public float Friendship { get; set; } = 0f;
        public bool IsBaby { get; set; }
        public float Age { get; set; }
        public float GrowthTime { get; set; }
        public float? GestationTimer { get; set; }
        public float Scale { get; set; }
        public float SlimeIncubationSeconds { get; set; }
        public List<InventorySlotSave> InventorySlots { get; set; } = new();
        public EquipmentSave Equipment { get; set; } = new();
        public bool IsInterestedInFood { get; set; }
        public int PreferredFoodId { get; set; }
        public int FollowOrder { get; set; } = -1;
        public bool IsTalking { get; set; }
        public float TalkTimer { get; set; }
        public float TalkCooldown { get; set; }
        public string TalkText { get; set; } = "";

        public List<EntitySaveData> Pets { get; set; } = new();

        //  SYSTÈME DE QUÊTES : persistance de l'identité et de la quête en cours
        public string NetId { get; set; } = "";
        public string FirstName { get; set; } = "";
        public bool HasActiveQuest { get; set; } = false;
        public int QuestType { get; set; } = 0; // 0 = DeliverItem, 1 = TameAndBring
        public string QuestTargetNetId { get; set; } = "";
        public int QuestItemId { get; set; }
        public string QuestItemName { get; set; } = "";
        public int QuestItemQty { get; set; }
        public string QuestTargetSpecies { get; set; } = "";
        public int QuestRewardItemId { get; set; }
        public string QuestRewardItemName { get; set; } = "";
        public int QuestRewardItemQty { get; set; }
        public float QuestTargetPosX { get; set; }
        public float QuestTargetPosY { get; set; }
        public bool QuestHasTargetPos { get; set; }
        public int QuestState { get; set; } // 0=Offered, 1=Accepted, 2=Delivered
    }
    
    public class BoatSaveData
    {
        public string Id { get; set; } = "";
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float VelX { get; set; }
        public float VelY { get; set; }
        public float Angle { get; set; }
        public float RowingCooldown { get; set; }
        public int GroundTileId { get; set; } = 500;
        public float SailAngle { get; set; }
        public bool IsSailAdjusted { get; set; }
        public List<int> RelX { get; set; } = new();
        public List<int> RelY { get; set; } = new();
        public List<int> PlacedRelX { get; set; } = new();
        public List<int> PlacedRelY { get; set; } = new();
        public List<int> PlacedObjectIds { get; set; } = new();
        public List<int> ContainerRelX { get; set; } = new();
        public List<int> ContainerRelY { get; set; } = new();
        public List<ContainerInventorySave> ContainerData { get; set; } = new();
        public List<int> ArmorStandRelX { get; set; } = new();
        public List<int> ArmorStandRelY { get; set; } = new();
        public List<ArmorStandSaveData> ArmorStandData { get; set; } = new();
    }
    
    public class CavePairSave
    {
        public int EntryX { get; set; }
        public int EntryY { get; set; }
        public int ExitX { get; set; }
        public int ExitY { get; set; }
    }

    public class DungeonInstanceSave
    {
        public int Id { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int EntryX { get; set; }
        public int EntryY { get; set; }
        public int RestSpawnX { get; set; }
        public int RestSpawnY { get; set; }
        public int ExitDoorX { get; set; }
        public int ExitDoorY { get; set; }
        public bool Generated { get; set; }
        public bool BossDefeated { get; set; }
        public int Seed { get; set; }
    }

    public class DungeonPortalSave
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Kind { get; set; } // 0 = Entrance, 1 = ReturnExit
        public int InstanceId { get; set; }
    }

    public class DungeonsSaveFile
    {
        public int NextInstanceId { get; set; }
        public List<DungeonInstanceSave> Instances { get; set; } = new();
        public List<DungeonPortalSave> Portals { get; set; } = new();
    }

    public class PortalSave
    {
        public int X { get; set; }
        public int Y { get; set; }
        public bool IsCave { get; set; }
        public string Name { get; set; } = "";
        public double DiscoveredAt { get; set; }
        public bool IsFavorite { get; set; }
    }

    public class PortalsSaveFile
    {
        public List<PortalSave> Portals { get; set; } = new();
    }
}