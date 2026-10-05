using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class ArmorStandUI
    {
        private const int HeaderHeight = 42;
        private const int CloseButtonSize = 26;
        private const int PanelWidth = 420;
        private const int PanelHeight = 500;
        private const float PreviewScale = 1.65f;
        private const string PreviewAnimation = "idle";

        private static readonly Dictionary<int, bool> OpenByPlayer = new();
        private static readonly Dictionary<int, ArmorStandData?> StandByPlayer = new();
        private static readonly Dictionary<int, Vector2> WindowPositionByPlayer = new();
        private static readonly Dictionary<int, Vector2> DragOffsetByPlayer = new();
        private static readonly Dictionary<int, bool> DraggingWindowByPlayer = new();
        private static readonly Dictionary<int, (EquipmentSlot? slot, BodyZone? zone)?> HoveredByPlayer = new();
        private static readonly Dictionary<(EquipmentSlot? slot, BodyZone? zone), Rectangle> HitRects = new();

        private static DraggedItem SharedDraggedItem = null!;
        private static NineSliceTexture? PanelTexture;
        private static bool UsePanelTexture;

        private static readonly BodyZone[] Zones =
        {
            BodyZone.TopOfHead, BodyZone.Face, BodyZone.Ears, BodyZone.Neck,
            BodyZone.Torso, BodyZone.Waist, BodyZone.Legs, BodyZone.Feet, BodyZone.Back
        };

        private static readonly BodyZone[] HitPriority =
        {
            BodyZone.Feet, BodyZone.TopOfHead, BodyZone.Ears, BodyZone.Face,
            BodyZone.Torso, BodyZone.Back, BodyZone.Legs, BodyZone.Neck, BodyZone.Waist
        };

        public static bool IsOpen => OpenByPlayer.Values.Any(value => value);

        public static bool IsOpenForPlayer(int playerIndex)
        {
            return OpenByPlayer.TryGetValue(playerIndex, out var open) && open;
        }

        public static void Initialize(DraggedItem sharedDraggedItem)
        {
            SharedDraggedItem = sharedDraggedItem;
            PanelTexture = UIManager.InventoryPanel;
            UsePanelTexture = PanelTexture != null && PanelTexture.IsValid;
        }

        public static void Open(ArmorStandData stand, int playerIndex = 0)
        {
            if (!WindowPositionByPlayer.ContainsKey(playerIndex))
                WindowPositionByPlayer[playerIndex] = new Vector2(220, 80);

            StandByPlayer[playerIndex] = stand;
            OpenByPlayer[playerIndex] = true;
            SharedDraggedItem?.EndDrag();
        }

        public static void Close(int playerIndex = 0)
        {
            OpenByPlayer[playerIndex] = false;
            StandByPlayer[playerIndex] = null;
            DraggingWindowByPlayer[playerIndex] = false;
            HoveredByPlayer[playerIndex] = null;
            SharedDraggedItem?.EndDrag();
        }

        public static int GetWindowX(int playerIndex = 0) => (int)GetWindowPosition(playerIndex).X;
        public static int GetWindowY(int playerIndex = 0) => (int)GetWindowPosition(playerIndex).Y;
        public static int GetWindowWidth() => PanelWidth;
        public static int GetWindowHeight() => PanelHeight;

        private static Vector2 GetWindowPosition(int playerIndex)
        {
            return WindowPositionByPlayer.TryGetValue(playerIndex, out var position)
                ? position
                : new Vector2(220, 80);
        }

        public static void Update(int playerIndex = 0, Rectangle? viewport = null)
        {
            if (!IsOpenForPlayer(playerIndex) || !StandByPlayer.TryGetValue(playerIndex, out var stand) || stand == null)
                return;

            Vector2 mouse = Raylib.GetMousePosition();
            if (viewport.HasValue) mouse -= new Vector2(viewport.Value.X, viewport.Value.Y);

            Vector2 position = GetWindowPosition(playerIndex);
            int screenWidth = viewport.HasValue ? (int)viewport.Value.Width : Raylib.GetScreenWidth();
            int screenHeight = viewport.HasValue ? (int)viewport.Value.Height : Raylib.GetScreenHeight();
            position.X = Math.Clamp(position.X, -PanelWidth + 100, screenWidth - 100);
            position.Y = Math.Clamp(position.Y, 0, screenHeight - 100);

            Rectangle titleBar = new(position.X, position.Y, PanelWidth, HeaderHeight);
            if (Raylib.CheckCollisionPointRec(mouse, titleBar) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                DraggingWindowByPlayer[playerIndex] = true;
                DragOffsetByPlayer[playerIndex] = mouse - position;
            }
            if (DraggingWindowByPlayer.TryGetValue(playerIndex, out var dragging) && dragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                position = mouse - DragOffsetByPlayer[playerIndex];
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                DraggingWindowByPlayer[playerIndex] = false;
            WindowPositionByPlayer[playerIndex] = position;

            BuildHitRects((int)position.X, (int)position.Y, stand.Equipment, stand.CurrentPose);
            var hovered = FindHoveredTarget(mouse, (int)position.X, (int)position.Y);
            HoveredByPlayer[playerIndex] = hovered;

            Rectangle closeButton = new((int)position.X + PanelWidth - CloseButtonSize - 10,
                (int)position.Y + (HeaderHeight - CloseButtonSize) / 2, CloseButtonSize, CloseButtonSize);
            if (Raylib.CheckCollisionPointRec(mouse, closeButton) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Close(playerIndex);
                return;
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && SharedDraggedItem.IsDragging
                && TryPlaceDraggedItem(stand.Equipment, mouse, (int)position.X, (int)position.Y))
            {
                hovered = null;
            }
            else if (hovered.HasValue && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                var target = hovered.Value;
                if (target.zone.HasValue)
                    HandleZoneClick(target.zone.Value, stand.Equipment, mouse);
                else if (target.slot.HasValue)
                    HandleFunctionalSlotClick(target.slot.Value, stand.Equipment, mouse);
            }
            else if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                // Pas de case précise sous la souris : on autorise quand même à déposer
                // l'objet directement sur le mannequin (aperçu), comme sur un vrai porte-armure.
                TryPlaceDraggedItem(stand.Equipment, mouse, (int)position.X, (int)position.Y);
            }

            if (SharedDraggedItem.IsDragging && Raylib.IsMouseButtonPressed(MouseButton.Right)
                && !Raylib.CheckCollisionPointRec(mouse, new Rectangle(position.X, position.Y, PanelWidth, PanelHeight)))
            {
                if (SharedDraggedItem.Item != null)
                    Program.ThrowDraggedItem(SharedDraggedItem.Item, SharedDraggedItem.Count);
                SharedDraggedItem.EndDrag();
            }

            stand.Equipment.LoadEquipmentTextures();
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) Close(playerIndex);
        }

        private static bool TryPlaceDraggedItem(Equipment equipment, Vector2 mouse, int panelX, int panelY)
        {
            if (!SharedDraggedItem.IsDragging || SharedDraggedItem.Item == null) return false;

            Rectangle previewRect = GetPreviewRect(panelX, panelY);
            if (!Raylib.CheckCollisionPointRec(mouse, previewRect)) return false;

            int itemId = Program.GetItemId(SharedDraggedItem.Item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var data)) return false;

            if (data.CoveredZones.Count > 0)
            {
                Item draggedItem = SharedDraggedItem.Item;
                if (!equipment.EquipOnBody(draggedItem, out var displaced)) return true;

                SharedDraggedItem.EndDrag();
                foreach (var item in displaced) Program.AddItemToInventory(item, 1);
                equipment.LoadEquipmentTextures();
                EntityRenderer.ClearEquipmentCache();
                Program.PlayEquipSound();
                return true;
            }

            if (data.HasEquipSlot)
            {
                HandleFunctionalSlotClick(data.EquipSlot, equipment, mouse);
                return true;
            }

            return false;
        }

        private static void BuildHitRects(int panelX, int panelY, Equipment equipment, string pose)
        {
            HitRects.Clear();
            Vector2 previewPos = GetPreviewPosition(panelX, panelY);
            for (int index = 0; index < Zones.Length; index++)
            {
                BodyZone zone = Zones[index];
                Item? equipped = equipment.GetZoneItem(zone);
                if (equipped != null && TryGetZonePieces(zone, equipped, previewPos, pose, out var pieces) && pieces.Count > 0)
                {
                    Rectangle bounds = pieces[0].Rect;
                    foreach (var piece in pieces.Skip(1)) bounds = Combine(bounds, piece.Rect);
                    HitRects[(null, zone)] = bounds;
                }
                else
                {
                    HitRects[(null, zone)] = GetZoneHitRect(zone, previewPos, pose);
                }
            }

            var (rightHand, _) = EntityRenderer.GetGlobalPartTransform("armorstand", pose, 0, 0f, 1f,
                previewPos, SpeciesData.Skeletons, "rarmbottom", customScale: PreviewScale);
            var (leftHand, _) = EntityRenderer.GetGlobalPartTransform("armorstand", pose, 0, 0f, 1f,
                previewPos, SpeciesData.Skeletons, "larmbottom", customScale: PreviewScale);
            HitRects[(EquipmentSlot.MainHand, null)] = new Rectangle(rightHand.X - 42, rightHand.Y - 55, 84, 110);
            HitRects[(EquipmentSlot.OffHand, null)] = new Rectangle(leftHand.X - 42, leftHand.Y - 55, 84, 110);
        }

        private static Rectangle GetZoneHitRect(BodyZone zone, Vector2 previewPos, string pose)
        {
            return zone switch
            {
                BodyZone.TopOfHead => GetPartHitRect(previewPos, pose, "head", 52, 54),
                BodyZone.Face => GetPartHitRect(previewPos, pose, "head", 58, 42),
                BodyZone.Ears => GetPartHitRect(previewPos, pose, "head", 74, 48),
                BodyZone.Neck => GetPartHitRect(previewPos, pose, "body", 48, 34, 0, -32),
                BodyZone.Torso => GetPartHitRect(previewPos, pose, "body", 76, 86),
                BodyZone.Back => GetPartHitRect(previewPos, pose, "body", 82, 92),
                BodyZone.Waist => GetPartHitRect(previewPos, pose, "body", 72, 38, 0, 48),
                BodyZone.Legs => Combine(
                    GetPartHitRect(previewPos, pose, "llegtop", 38, 72),
                    GetPartHitRect(previewPos, pose, "rlegtop", 38, 72)),
                BodyZone.Feet => Combine(
                    GetPartHitRect(previewPos, pose, "llegbottom", 42, 38),
                    GetPartHitRect(previewPos, pose, "rlegbottom", 42, 38)),
                _ => GetPartHitRect(previewPos, pose, "body", 76, 86)
            };
        }

        private static Rectangle GetPartHitRect(Vector2 previewPos, string pose, string partName,
            float halfWidth, float halfHeight, float offsetX = 0f, float offsetY = 0f)
        {
            var (partPosition, _) = EntityRenderer.GetGlobalPartTransform("armorstand", pose, 0, 0f, 1f,
                previewPos, SpeciesData.Skeletons, partName, customScale: PreviewScale);
            return new Rectangle(partPosition.X + offsetX * PreviewScale - halfWidth * PreviewScale,
                partPosition.Y + offsetY * PreviewScale - halfHeight * PreviewScale,
                halfWidth * 2f * PreviewScale, halfHeight * 2f * PreviewScale);
        }

        private static (EquipmentSlot? slot, BodyZone? zone)? FindHoveredTarget(Vector2 mouse, int panelX, int panelY)
        {
            if (SharedDraggedItem.IsDragging)
            {
                foreach (var hit in HitRects)
                    if (Raylib.CheckCollisionPointRec(mouse, hit.Value)) return hit.Key;
            }

            foreach (var zone in HitPriority)
            {
                var key = ((EquipmentSlot?)null, (BodyZone?)zone);
                if (HitRects.TryGetValue(key, out var rect) && Raylib.CheckCollisionPointRec(mouse, rect))
                    return key;
            }

            foreach (var slot in new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand })
            {
                var key = ((EquipmentSlot?)slot, (BodyZone?)null);
                if (HitRects.TryGetValue(key, out var rect) && Raylib.CheckCollisionPointRec(mouse, rect))
                    return key;
            }
            return null;
        }

        private static void AddHitRect((EquipmentSlot? slot, BodyZone? zone) key, Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            if (HitRects.TryGetValue(key, out var previous))
                HitRects[key] = Combine(previous, rect);
            else
                HitRects[key] = rect;
        }

        private static Rectangle Combine(Rectangle first, Rectangle second)
        {
            float left = Math.Min(first.X, second.X);
            float top = Math.Min(first.Y, second.Y);
            float right = Math.Max(first.X + first.Width, second.X + second.Width);
            float bottom = Math.Max(first.Y + first.Height, second.Y + second.Height);
            return new Rectangle(left, top, right - left, bottom - top);
        }

        private static Vector2 GetPreviewPosition(int panelX, int panelY)
        {
            return new Vector2(panelX + PanelWidth / 2f, panelY + 315f);
        }

        private static Rectangle GetPreviewRect(int panelX, int panelY)
        {
            return new Rectangle(panelX + 20, panelY + HeaderHeight + 16,
                PanelWidth - 40, PanelHeight - HeaderHeight - 36);
        }

        private static bool TryGetZonePieces(BodyZone zone, Item item, Vector2 previewPos, string pose, out List<EntityRenderer.ZonePieceRect> pieces)
        {
            pieces = new List<EntityRenderer.ZonePieceRect>();
            int itemId = Program.GetItemId(item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var data)) return false;
            pieces = EntityRenderer.GetZonePieceRects("armorstand", pose, 0, 1f,
                previewPos, SpeciesData.Skeletons, PreviewScale, zone, data, item);
            return pieces.Count > 0;
        }

        public static void Draw(int playerIndex = 0, Rectangle? viewport = null)
        {
            if (!IsOpenForPlayer(playerIndex) || !StandByPlayer.TryGetValue(playerIndex, out var stand) || stand == null)
                return;

            Vector2 position = GetWindowPosition(playerIndex);
            int panelX = (int)position.X;
            int panelY = (int)position.Y;
            Vector2 mouse = Raylib.GetMousePosition();
            if (viewport.HasValue) mouse -= new Vector2(viewport.Value.X, viewport.Value.Y);

            if (UsePanelTexture && PanelTexture != null)
                PanelTexture.Draw(new Rectangle(panelX, panelY, PanelWidth, PanelHeight), Color.White);
            else
            {
                Raylib.DrawRectangleRounded(new Rectangle(panelX + 4, panelY + 4, PanelWidth, PanelHeight), 0.08f, 10, new Color(0, 0, 0, 100));
                Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, PanelWidth, PanelHeight), 0.08f, 10, new Color(25, 28, 35, 245));
                Raylib.DrawRectangleRoundedLines(new Rectangle(panelX, panelY, PanelWidth, PanelHeight), 0.08f, 10, 2, new Color(100, 100, 120, 200));
            }

            Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, PanelWidth, HeaderHeight), 0.08f, 8, new Color(35, 38, 48, 255));
            Raylib.DrawRectangleRoundedLines(new Rectangle(panelX, panelY, PanelWidth, HeaderHeight), 0.08f, 8, 1, new Color(210, 180, 100, 255));
            FontManager.DrawText("PORTE-ARMURE", panelX + 16, panelY + 12, 18, Color.White);

            Rectangle closeButton = new(panelX + PanelWidth - CloseButtonSize - 10, panelY + 8, CloseButtonSize, CloseButtonSize);
            bool closeHovered = Raylib.CheckCollisionPointRec(mouse, closeButton);
            Raylib.DrawRectangleRounded(closeButton, 0.2f, 6, closeHovered ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeButton.X + 8, (int)closeButton.Y + 4, 16, closeHovered ? Color.Red : new Color(220, 150, 150, 255));

            Vector2 previewPos = GetPreviewPosition(panelX, panelY);
            Rectangle previewRect = new(panelX + 20, panelY + 58, PanelWidth - 40, PanelHeight - 78);
            Raylib.DrawRectangleRounded(previewRect, 0.08f, 10, new Color(18, 21, 27, 220));
            Raylib.DrawRectangleRoundedLines(previewRect, 0.08f, 10, 1, new Color(80, 70, 50, 160));
            stand.Equipment.LoadEquipmentTextures();
            EntityRenderer.DrawEntity("armorstand", stand.CurrentPose, 0, 0f, 1f, previewPos, Color.White,
                Program.hairBaseTextures.Count > 0 ? Program.hairBaseTextures[0] : new Texture2D(),
                Program.hairOverlayTextures.Count > 0 ? Program.hairOverlayTextures[0] : new Texture2D(),
                Color.Black, SpeciesData.Skeletons, Program.EyesTexture, Program.MouthTexture,
                customScale: PreviewScale, equipment: stand.Equipment, isCarrying: false, inWater: false,
                attackSwingProgress: 0f, heldItemTexture: default, headAngle: 0f);

            DrawFallbackEquippedItems(stand.Equipment, previewPos, stand.CurrentPose);
            DrawDragPreview(previewPos);

            if (SharedDraggedItem.IsDragging && SharedDraggedItem.Item != null)
            {
                ItemRenderer.DrawItemPadded(SharedDraggedItem.Item, (int)mouse.X - 24, (int)mouse.Y - 24, 48, 4, new Color(255, 255, 255, 200));
                if (SharedDraggedItem.Count > 1)
                    FontManager.DrawText(SharedDraggedItem.Count.ToString(), (int)mouse.X + 15, (int)mouse.Y + 15, 16, Color.White);
            }
        }

        private static void DrawFallbackEquippedItems(Equipment equipment, Vector2 previewPos, string pose)
        {
            foreach (var item in equipment.ZoneItems)
            {
                int itemId = Program.GetItemId(item.Name);
                if (!GameData.ItemDatabase.TryGetValue(itemId, out var data)) continue;

                foreach (var zone in data.CoveredZones)
                {
                    if (TryGetZonePieces(zone, item, previewPos, pose, out var pieces) && pieces.Count > 0)
                        continue;

                    Rectangle fallback = GetZoneHitRect(zone, previewPos, pose);
                    ItemRenderer.DrawItemPadded(item, (int)(fallback.X + fallback.Width / 2f - 24),
                        (int)(fallback.Y + fallback.Height / 2f - 24), 48, 4, new Color(255, 255, 255, 235));
                    break;
                }
            }

            foreach (var slot in new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand })
            {
                Item? item = equipment.GetItemInSlot(slot, 0);
                if (item == null) continue;

                var piece = EntityRenderer.GetSlotItemPieceRect("armorstand", PreviewAnimation, 0, 1f,
                    previewPos, SpeciesData.Skeletons, PreviewScale, slot, item);
                if (piece.HasValue) continue;

                string bone = slot == EquipmentSlot.MainHand ? "rarmbottom" : "larmbottom";
                var (handPosition, _) = EntityRenderer.GetGlobalPartTransform("armorstand", PreviewAnimation, 0, 0f, 1f,
                    previewPos, SpeciesData.Skeletons, bone, customScale: PreviewScale);
                ItemRenderer.DrawItemPadded(item, (int)handPosition.X - 24, (int)handPosition.Y - 24, 48, 4, new Color(255, 255, 255, 235));
            }
        }

        private static void DrawDragPreview(Vector2 previewPos)
        {
            if (!SharedDraggedItem.IsDragging || SharedDraggedItem.Item == null) return;
            int itemId = Program.GetItemId(SharedDraggedItem.Item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var data)) return;

            var drawn = new HashSet<string>();
            foreach (var zone in data.CoveredZones)
            {
                var pieces = EntityRenderer.GetZonePieceRects("armorstand", PreviewAnimation, 0, 1f,
                    previewPos, SpeciesData.Skeletons, PreviewScale, zone, data, SharedDraggedItem.Item);
                foreach (var piece in pieces)
                {
                    if (piece.Texture.Id == 0 || !drawn.Add($"{piece.PartName}:{piece.Layer}")) continue;
                    Color tint = piece.Tint;
                    tint.A = 120;
                    Raylib.DrawTexturePro(piece.Texture, new Rectangle(0, 0, piece.Texture.Width, piece.Texture.Height), piece.Rect, Vector2.Zero, 0f, tint);
                }
            }
        }

        private static void HandleZoneClick(BodyZone zone, Equipment equipment, Vector2 mouse)
        {
            if (SharedDraggedItem.IsDragging && SharedDraggedItem.Item != null)
            {
                int itemId = Program.GetItemId(SharedDraggedItem.Item.Name);
                if (!GameData.ItemDatabase.TryGetValue(itemId, out var data) || !data.CoveredZones.Contains(zone)) return;
                if (equipment.EquipOnBody(SharedDraggedItem.Item, out var displaced))
                {
                    SharedDraggedItem.EndDrag();
                    foreach (var item in displaced) Program.AddItemToInventory(item, 1);
                    equipment.LoadEquipmentTextures();
                    EntityRenderer.ClearEquipmentCache();
                    Program.PlayEquipSound();
                }
                return;
            }

            Item? equipped = equipment.GetZoneItem(zone);
            if (equipped == null) return;
            equipment.UnequipFromBody(equipped);
            SharedDraggedItem.StartDrag(equipped, Math.Max(1, equipped.Count), mouse, -1, null, -1, zone);
            equipment.LoadEquipmentTextures();
        }

        private static void HandleFunctionalSlotClick(EquipmentSlot slot, Equipment equipment, Vector2 mouse)
        {
            if (SharedDraggedItem.IsDragging && SharedDraggedItem.Item != null)
            {
                int itemId = Program.GetItemId(SharedDraggedItem.Item.Name);
                if (!GameData.ItemDatabase.TryGetValue(itemId, out var data) || !data.HasEquipSlot || data.EquipSlot != slot) return;
                Item? old = equipment.GetItemInSlot(slot, 0);
                if (old != null) equipment.UnEquipItem(slot, 0);
                if (equipment.EquipItem(SharedDraggedItem.Item, slot, 0))
                {
                    if (old != null) Program.AddItemToInventory(old, 1);
                    SharedDraggedItem.EndDrag();
                    equipment.LoadEquipmentTextures();
                    Program.PlayEquipSound();
                }
                else if (old != null) equipment.EquipItem(old, slot, 0);
                return;
            }

            Item? equipped = equipment.GetItemInSlot(slot, 0);
            if (equipped == null) return;
            equipment.UnEquipItem(slot, 0);
            SharedDraggedItem.StartDrag(equipped, Math.Max(1, equipped.Count), mouse, -1, slot, 0);
            equipment.LoadEquipmentTextures();
            EntityRenderer.ClearEquipmentCache();
        }
    }
}
