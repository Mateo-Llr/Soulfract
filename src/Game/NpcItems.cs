#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public static partial class Program
    {
        /// <summary>
        /// Attire vers le PNJ les objets au sol proches qu'il peut effectivement ranger.
        /// </summary>
        public static void TryNpcAttractNearby(Entity npc, float radius)
        {
            float radiusSq = radius * radius;
            foreach (var gi in GroundItems)
            {
                if (!gi.IsAlive || !gi.IsOnGround) continue;
                if (gi.TimeSinceDrop <= gi.PickupCooldown) continue;
                if (Vector2.DistanceSquared(npc.WorldPos, gi.Position) > radiusSq) continue;
                if (!GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData)) continue;
                if (!World.ContainerHasSpaceForItem(npc.Inventory, itemData.Name)) continue;

                Vector2 direction = npc.WorldPos - gi.Position;
                float distance = direction.Length();
                if (distance > 0.01f)
                    direction /= distance;
                gi.GroundVelocity = direction * MathF.Min(gi.AttractSpeed, distance / 0.25f);
            }
        }

        /// <summary>
        /// Ramasse dans l'inventaire du PNJ les objets au sol proches qu'il peut ranger.
        /// </summary>
        public static void TryNpcPassivePickupNearby(Entity npc, float radius)
        {
            float radiusSq = radius * radius;
            for (int i = GroundItems.Count - 1; i >= 0; i--)
            {
                var gi = GroundItems[i];
                if (!gi.IsAlive) continue;
                if (gi.TimeSinceDrop <= gi.PickupCooldown) continue;
                if (Vector2.DistanceSquared(npc.WorldPos, gi.Position) > radiusSq) continue;
                if (!GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData)) continue;
                if (!CanNpcPickUpItem(npc, itemData)) continue;

                var item = new Item(itemData.Name, gi.Count, itemData.Color, itemData.Icon);
                if (gi.CustomColors != null && gi.CustomColors.Count > 0)
                    item.CustomColors = new List<Color?>(gi.CustomColors);
                item.RestoreMetadataFromSave(gi.Metadata ?? "");
                if (gi.Meta != null && gi.Meta.Count > 0)
                    foreach (var kv in gi.Meta)
                        item.Meta[kv.Key] = kv.Value;

                if (TryEquipNpcPickup(npc, item, itemData))
                {
                    Console.WriteLine($"[NPC-CHEST] PICKUP npc={npc.NetId} item={itemData.Name} count=1 mode=passive equipped=true");
                    npc.NotifyItemPickedUp();
                    if (gi.Count <= 1)
                        GroundItems.RemoveAt(i);
                    else
                        gi.Count--;
                    continue;
                }

                int deposited = World.StackItemIntoContainer(npc.Inventory, item, gi.Count);
                if (deposited >= gi.Count)
                {
                    Console.WriteLine($"[NPC-CHEST] PICKUP npc={npc.NetId} item={itemData.Name} count={deposited} mode=passive");
                    npc.NotifyItemPickedUp();
                    GroundItems.RemoveAt(i);
                }
                else if (deposited > 0)
                {
                    gi.Count -= deposited;
                }
            }
        }

        /// <summary>
        /// Cherche l'objet au sol le plus proche que ce PNJ peut ranger.
        /// </summary>
        public static GroundItem? FindNearestGroundItemForNpc(Vector2 worldPos, float maxDistance, Entity npc)
        {
            GroundItem? closest = null;
            float bestDistSq = maxDistance * maxDistance;

            foreach (var gi in GroundItems)
            {
                if (!gi.IsAlive) continue;
                if (gi.TimeSinceDrop <= gi.PickupCooldown) continue;
                if (!GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData)) continue;
                if (!CanNpcPickUpItem(npc, itemData)) continue;

                float distSq = Vector2.DistanceSquared(worldPos, gi.Position);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    closest = gi;
                }
            }
            return closest;
        }

        /// <summary>
        /// Ramasse un objet au sol dans l'inventaire ou l'équipement du PNJ.
        /// </summary>
        public static void TryNpcPickupGroundItem(Entity npc, Guid targetNetId)
        {
            if (targetNetId == Guid.Empty) return;
            var gi = GroundItems.FirstOrDefault(g => g.NetId == targetNetId && g.IsAlive);
            if (gi == null) return;
            if (!GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData)) return;
            if (!CanNpcPickUpItem(npc, itemData)) return;

            var item = new Item(itemData.Name, gi.Count, itemData.Color, itemData.Icon);
            if (gi.CustomColors != null && gi.CustomColors.Count > 0)
                item.CustomColors = new List<Color?>(gi.CustomColors);
            item.RestoreMetadataFromSave(gi.Metadata ?? "");
            if (gi.Meta != null && gi.Meta.Count > 0)
                foreach (var kv in gi.Meta)
                    item.Meta[kv.Key] = kv.Value;

            if (TryEquipNpcPickup(npc, item, itemData))
            {
                Console.WriteLine($"[NPC-CHEST] PICKUP npc={npc.NetId} item={itemData.Name} count=1 mode=active equipped=true");
                npc.NotifyItemPickedUp();
                if (gi.Count <= 1)
                    GroundItems.Remove(gi);
                else
                    gi.Count--;
                return;
            }

            int deposited = World.StackItemIntoContainer(npc.Inventory, item, gi.Count);
            if (deposited >= gi.Count)
            {
                Console.WriteLine($"[NPC-CHEST] PICKUP npc={npc.NetId} item={itemData.Name} count={deposited} mode=active");
                npc.NotifyItemPickedUp();
                GroundItems.Remove(gi);
            }
            else if (deposited > 0)
            {
                gi.Count -= deposited;
            }
        }

        private static bool CanNpcPickUpItem(Entity npc, ItemData itemData)
        {
            return World.ContainerHasSpaceForItem(npc.Inventory, itemData.Name)
                || HasFreeNpcEquipmentSlot(npc, itemData);
        }

        private static bool HasFreeNpcEquipmentSlot(Entity npc, ItemData itemData)
        {
            if (itemData.CoveredZones.Count > 0)
                return itemData.CoveredZones.All(zone => npc.Equipment?.GetZoneItem(zone) == null);

            if (!itemData.HasEquipSlot || npc.Equipment == null) return false;
            return npc.Equipment.GetEquipCount(itemData.EquipSlot) < itemData.EquipMax
                && Enumerable.Range(0, itemData.EquipMax)
                    .Any(index => npc.Equipment.GetItemInSlot(itemData.EquipSlot, index) == null);
        }

        private static bool TryEquipNpcPickup(Entity npc, Item item, ItemData itemData)
        {
            if (!HasFreeNpcEquipmentSlot(npc, itemData)) return false;
            npc.Equipment ??= new Equipment();

            if (itemData.CoveredZones.Count > 0)
            {
                bool equipped = npc.Equipment.EquipOnBody(item, out _);
                if (equipped) npc.Equipment.LoadEquipmentTextures();
                return equipped;
            }

            for (int index = 0; index < itemData.EquipMax; index++)
            {
                if (npc.Equipment.GetItemInSlot(itemData.EquipSlot, index) == null)
                {
                    bool equipped = npc.Equipment.EquipItem(item, itemData.EquipSlot, index);
                    if (equipped) npc.Equipment.LoadEquipmentTextures();
                    return equipped;
                }
            }

            return false;
        }

        /// <summary>
        /// Fait acheter une ration de nourriture par un PNJ affamé auprès d'un marchand.
        /// </summary>
        public static void TryBuyFoodFromMerchant(Entity buyer, Guid merchantNetId)
        {
            var merchant = entities.FirstOrDefault(e => e.NetId == merchantNetId && e.IsAlive);
            if (merchant == null || !(merchant.IsTrader || merchant.HasProfession)) return;

            var foodSlot = merchant.Inventory.Slots.FirstOrDefault(s => !s.IsEmpty && s.Item != null
                && GameData.ItemDatabase.TryGetValue(GameData.GetItemId(s.Item.Name), out var d) && d.Type == ItemType.Food);

            Item? foodTemplate = foodSlot?.Item;
            if (foodTemplate == null)
            {
                var fallback = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Type == ItemType.Food);
                if (fallback.ID == 0) return;
                foodTemplate = new Item(fallback.Name, 1, fallback.Color, fallback.Icon);
            }

            buyer.Hunger = 0f;
            World.StackItemIntoContainer(buyer.Inventory, foodTemplate, 1);
        }
    }
}
