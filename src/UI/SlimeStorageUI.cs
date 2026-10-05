#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class SlimeStorageUI
    {
        private static Entity? _slime;
        private static Vector2 _windowPos;
        private static bool _isOpen;
        private static bool _isDraggingWindow;
        private static Vector2 _dragOffset;
        private static InventorySlotSave? _pendingNetworkSlot;
        private static double _pendingNetworkChangeTime;

        private static int WINDOW_WIDTH => UIManager.ScaleInt(320);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(235);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(42);
        private static int SLOT_SIZE => UIManager.ItemSlotSize;

        private static readonly Color COLOR_BG = new(25, 28, 35, 245);
        private static readonly Color COLOR_HEADER = new(35, 38, 48, 255);
        private static readonly Color COLOR_BORDER = new(80, 70, 50, 220);
        private static readonly Color COLOR_ACCENT = new(210, 180, 100, 255);

        public static bool IsOpen => _isOpen;

        public static void Open(Entity slime)
        {
            if (slime == null || !slime.IsSlime || !slime.IsTamed) return;

            _slime = slime;
            _isOpen = true;
            _pendingNetworkSlot = null;
            _windowPos = new Vector2(
                (Raylib.GetScreenWidth() - WINDOW_WIDTH) / 2f,
                (Raylib.GetScreenHeight() - WINDOW_HEIGHT) / 2f);
            slime.GetSlimeStorageSlot();
        }

        public static bool IsMouseOverWindow(Vector2 mousePosition)
            => _isOpen && Raylib.CheckCollisionPointRec(mousePosition,
                new Rectangle(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, WINDOW_HEIGHT));

        public static void Update()
        {
            if (!_isOpen || _slime == null) return;
            RefreshFromNetworkSnapshot();

            Vector2 mousePosition = Raylib.GetMousePosition();
            Rectangle window = new(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, WINDOW_HEIGHT);
            Rectangle titleBar = new(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, HEADER_HEIGHT);
            Rectangle closeButton = new(_windowPos.X + WINDOW_WIDTH - UIManager.ScaleInt(34),
                _windowPos.Y + UIManager.ScaleInt(8), UIManager.ScaleInt(24), UIManager.ScaleInt(24));
            Rectangle storageSlot = GetStorageSlotRect();

            if (Raylib.IsMouseButtonPressed(MouseButton.Left)
                && Raylib.CheckCollisionPointRec(mousePosition, closeButton))
            {
                Close();
                return;
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Left)
                && Raylib.CheckCollisionPointRec(mousePosition, titleBar)
                && !Raylib.CheckCollisionPointRec(mousePosition, closeButton))
            {
                _isDraggingWindow = true;
                _dragOffset = mousePosition - _windowPos;
            }
            if (_isDraggingWindow && Raylib.IsMouseButtonDown(MouseButton.Left))
                _windowPos = mousePosition - _dragOffset;
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDraggingWindow = false;

            InventorySlot slot = _slime.GetSlimeStorageSlot();
            DraggedItem draggedItem = InventoryRenderer.DraggedItem;

            if (Raylib.IsMouseButtonPressed(MouseButton.Left)
                && Raylib.CheckCollisionPointRec(mousePosition, storageSlot)
                && !draggedItem.IsDragging
                && !slot.IsEmpty && slot.Item != null)
            {
                int previousCount = slot.Count;
                int remaining = Program.AddItemToInventory(slot.Item, previousCount);
                int transferred = previousCount - remaining;
                if (transferred > 0)
                {
                    if (remaining <= 0) slot.Clear();
                    else slot.Count = remaining;
                    if (slot.IsEmpty) _slime.SlimeIncubationSeconds = 0f;
                    SyncStorage();
                }
            }

            if (Raylib.IsMouseButtonReleased(MouseButton.Left)
                && Raylib.CheckCollisionPointRec(mousePosition, storageSlot)
                && draggedItem.IsDragging && draggedItem.Item != null)
            {
                StoreDraggedItem(slot, draggedItem);
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                Close();
        }

        private static void StoreDraggedItem(InventorySlot target, DraggedItem draggedItem)
        {
            Item dragged = draggedItem.Item!;
            int count = Math.Max(1, draggedItem.Count);

            if (target.IsEmpty || target.Item == null)
            {
                target.Item = dragged;
                target.Count = count;
                _slime!.SlimeIncubationSeconds = 0f;
                draggedItem.EndDrag();
                SyncStorage();
                return;
            }

            if (!Program.AreItemsStackable(target.Item, dragged))
            {
                Program.AddNotification(new Notification("Le slime ne peut stocker qu'un type d'objet à la fois.",
                    new Color(255, 200, 100, 255), 2f));
                return;
            }

            int itemId = Program.GetItemId(target.Item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)) return;
            int space = itemData.StackSize - target.Count;
            int added = Math.Min(space, count);
            if (added <= 0) return;

            Program.MergeSpoilMeta(target.Item, dragged, added);
            target.Count += added;
            draggedItem.Count -= added;
            if (draggedItem.Count <= 0) draggedItem.EndDrag();
            SyncStorage();
        }

        private static void SyncStorage()
        {
            if (_slime == null || !NetworkManager.IsClient) return;
            InventorySlot slot = _slime.GetSlimeStorageSlot();
            _pendingNetworkSlot = SaveSystem.CreateInventorySlotSave(slot);
            _pendingNetworkChangeTime = Raylib.GetTime();
            NetworkManager.RequestSlimeStorageUpdate(_slime.NetId, slot);
        }

        private static void RefreshFromNetworkSnapshot()
        {
            if (!NetworkManager.IsClient || _slime == null) return;

            EntityDto? snapshot = null;
            string netId = _slime.NetId.ToString();
            foreach (var entity in NetworkManager.GetRemoteEntities())
            {
                if (entity.NetId == netId)
                {
                    snapshot = entity;
                    break;
                }
            }
            if (snapshot == null) return;

            if (_pendingNetworkSlot != null
                && !StorageSlotsMatch(snapshot.SlimeStorageSlot, _pendingNetworkSlot)
                && Raylib.GetTime() - _pendingNetworkChangeTime < 2.0)
            {
                _slime.SlimeIncubationSeconds = snapshot.SlimeIncubationSeconds;
                return;
            }

            _pendingNetworkSlot = null;
            InventorySlot localSlot = _slime.GetSlimeStorageSlot();
            if (snapshot.SlimeStorageSlot == null)
                localSlot.Clear();
            else
                SaveSystem.RestoreInventorySlotFromSave(snapshot.SlimeStorageSlot, localSlot);
            _slime.SlimeIncubationSeconds = snapshot.SlimeIncubationSeconds;
        }

        private static bool StorageSlotsMatch(InventorySlotSave? a, InventorySlotSave? b)
        {
            if (a == null || a.IsEmpty) return b == null || b.IsEmpty;
            if (b == null || b.IsEmpty) return false;
            return string.Equals(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase)
                && a.Count == b.Count
                && string.Equals(a.Metadata, b.Metadata, StringComparison.Ordinal);
        }

        public static void Close()
        {
            _isOpen = false;
            _isDraggingWindow = false;
            _slime = null;
            _pendingNetworkSlot = null;
        }

        private static Rectangle GetStorageSlotRect()
            => new(_windowPos.X + (WINDOW_WIDTH - SLOT_SIZE) / 2f,
                _windowPos.Y + HEADER_HEIGHT + UIManager.ScaleInt(28), SLOT_SIZE, SLOT_SIZE);

        public static void Draw()
        {
            if (!_isOpen || _slime == null) return;

            Rectangle panel = new(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, WINDOW_HEIGHT);
            Rectangle shadow = new(_windowPos.X + 4, _windowPos.Y + 4, WINDOW_WIDTH, WINDOW_HEIGHT);
            Vector2 mousePosition = Raylib.GetMousePosition();
            Raylib.DrawRectangleRounded(shadow, 0.08f, 8, new Color(0, 0, 0, 110));
            if (UIManager.InventoryPanel != null && UIManager.InventoryPanel.IsValid)
                UIManager.InventoryPanel.Draw(panel, Color.White);
            else
            {
                Raylib.DrawRectangleRounded(panel, 0.08f, 8, COLOR_BG);
                Raylib.DrawRectangleRoundedLines(panel, 0.08f, 8, 2, COLOR_BORDER);
            }

            Rectangle titleBar = new(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, HEADER_HEIGHT);
            Raylib.DrawRectangleRounded(titleBar, 0.08f, 8, COLOR_HEADER);
            FontManager.DrawText("Stockage du slime", (int)_windowPos.X + UIManager.ScaleInt(14),
                (int)_windowPos.Y + UIManager.ScaleInt(12), 16, Color.White);

            Rectangle closeButton = new(_windowPos.X + WINDOW_WIDTH - UIManager.ScaleInt(34),
                _windowPos.Y + UIManager.ScaleInt(8), UIManager.ScaleInt(24), UIManager.ScaleInt(24));
            bool closeHovered = Raylib.CheckCollisionPointRec(mousePosition, closeButton);
            Raylib.DrawRectangleRounded(closeButton, 0.2f, 5,
                closeHovered ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 220));
            FontManager.DrawText("X", (int)closeButton.X + UIManager.ScaleInt(7),
                (int)closeButton.Y + UIManager.ScaleInt(4), 14, Color.White);

            Rectangle slotRect = GetStorageSlotRect();
            bool slotHovered = Raylib.CheckCollisionPointRec(mousePosition, slotRect);
            Raylib.DrawRectangleRounded(slotRect, 0.08f, 6,
                slotHovered ? new Color(70, 73, 80, 255) : new Color(45, 48, 55, 255));
            Raylib.DrawRectangleRoundedLines(slotRect, 0.08f, 6, 2, COLOR_ACCENT);

            InventorySlot slot = _slime.GetSlimeStorageSlot();
            if (!slot.IsEmpty && slot.Item != null)
            {
                ItemRenderer.DrawItemPadded(slot.Item, (int)slotRect.X + UIManager.ScaleInt(4),
                    (int)slotRect.Y + UIManager.ScaleInt(4), (int)slotRect.Width - UIManager.ScaleInt(8), 4);
                if (slot.Count > 1)
                    FontManager.DrawText(slot.Count.ToString(), (int)slotRect.X + (int)slotRect.Width - 22,
                        (int)slotRect.Y + (int)slotRect.Height - 20, 14, Color.White);
            }

            string hint = "Glissez un objet dans la case ou cliquez pour le reprendre.";
            int hintWidth = FontManager.MeasureText(hint, 11);
            FontManager.DrawText(hint, (int)_windowPos.X + (WINDOW_WIDTH - hintWidth) / 2,
                (int)slotRect.Y + SLOT_SIZE + UIManager.ScaleInt(10), 11, new Color(190, 180, 155, 255));

            if (!slot.IsEmpty && slot.Item != null && Entity.IsSlimeDuplicableGem(slot.Item))
            {
                float progress = Math.Clamp(_slime.SlimeIncubationSeconds / Entity.SlimeGemDuplicationSeconds, 0f, 1f);
                Rectangle progressTrack = new(_windowPos.X + UIManager.ScaleInt(30),
                    slotRect.Y + SLOT_SIZE + UIManager.ScaleInt(35), WINDOW_WIDTH - UIManager.ScaleInt(60), UIManager.ScaleInt(10));
                Raylib.DrawRectangleRounded(progressTrack, 0.25f, 4, new Color(40, 43, 50, 230));
                Raylib.DrawRectangleRounded(new Rectangle(progressTrack.X, progressTrack.Y,
                    progressTrack.Width * progress, progressTrack.Height), 0.25f, 4,
                    new Color(120, 210, 150, 255));
                float remaining = Math.Max(0f, Entity.SlimeGemDuplicationSeconds - _slime.SlimeIncubationSeconds);
                string timeText = $"Prochaine copie dans {TimeSpan.FromSeconds(remaining):mm\\:ss}";
                int timeWidth = FontManager.MeasureText(timeText, 12);
                FontManager.DrawText(timeText, (int)_windowPos.X + (WINDOW_WIDTH - timeWidth) / 2,
                    (int)progressTrack.Y + UIManager.ScaleInt(14), 12, Color.White);
            }
            else
            {
                string note = slot.IsEmpty ? "Le slime couve les gemmes stockées." : "Cet objet ne peut pas être couvé.";
                int noteWidth = FontManager.MeasureText(note, 12);
                FontManager.DrawText(note, (int)_windowPos.X + (WINDOW_WIDTH - noteWidth) / 2,
                    (int)slotRect.Y + SLOT_SIZE + UIManager.ScaleInt(38), 12, new Color(190, 180, 155, 255));
            }
        }
    }
}