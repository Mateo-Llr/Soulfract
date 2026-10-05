// PortableContainerUI.cs - Interface pour les conteneurs portables (panier en main gauche, etc.)
#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class PortableContainerUI
    {
        private static bool _isOpen = false;
        private static PortableContainerData? _currentContainer = null;
        private static Vector2 _windowPos = new Vector2(300, 200);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isHoveringTitleBar = false;
        
        private static int WINDOW_WIDTH => UIManager.ScaleInt(350);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(350);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(40);
        private static int SLOT_SIZE => UIManager.ItemSlotSize;
        private static int SLOT_STEP => UIManager.ItemSlotStep;
        private static int ITEM_SIZE => UIManager.ItemContentSize;
        private static int ITEM_OFFSET => (SLOT_SIZE - ITEM_SIZE) / 2;
        
        private static readonly Color COLOR_BG = new Color(25, 28, 35, 240);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_SLOT_NORMAL = new Color(50, 53, 60, 255);
        private static readonly Color COLOR_SLOT_HOVER = new Color(70, 73, 80, 255);
        
        // Textures 9-slice
        private static NineSliceTexture? _panelTexture;
        private static NineSliceTexture? _slotTexture;
        private static bool _useTextures = false;
        
        // Référence au DraggedItem partagé
        private static DraggedItem? _sharedDraggedItem;
        
        public static bool IsOpen => _isOpen;
        
        public static void Initialize(DraggedItem sharedDraggedItem)
        {
            _sharedDraggedItem = sharedDraggedItem;
            _panelTexture = UIManager.InventoryPanel;
            _slotTexture = UIManager.SlotTexture;
            _useTextures = (_panelTexture != null && _panelTexture.IsValid);
        }
        
        public static void Open(PortableContainerData container)
        {
            if (container == null) return;
            
            _currentContainer = container;
            _isOpen = true;
            _sharedDraggedItem?.EndDrag();
            
            // Ouvrir l'inventaire si ce n'est pas déjà fait
            if (!InventoryRenderer.IsInventoryOpen)
                InventoryRenderer.IsInventoryOpen = true;
            
            // Enregistrer dans la pile UI
            UIManager.PushUI(Close, () => _isOpen);
        }
        
        public static void Close()
        {
            _isOpen = false;
            _currentContainer = null;
            _sharedDraggedItem?.EndDrag();
            _isDragging = false;
            _dragOffset = Vector2.Zero;
        }
        
        public static void Update()
        {
            if (!_isOpen || _currentContainer == null) return;
            
            Vector2 mousePos = Raylib.GetMousePosition();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            
            // Ajuster la position de la fenêtre
            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + 100, sw - 100);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, sh - 100);
            
            int panelX = (int)_windowPos.X;
            int panelY = (int)_windowPos.Y;
            
            // Barre de titre pour le drag & drop
            Rectangle titleBar = new Rectangle(panelX, panelY, WINDOW_WIDTH, HEADER_HEIGHT);
            _isHoveringTitleBar = Raylib.CheckCollisionPointRec(mousePos, titleBar);
            
            if (_isHoveringTitleBar && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDragging = true;
                _dragOffset = mousePos - _windowPos;
            }
            if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                _windowPos = mousePos - _dragOffset;
            }
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDragging = false;
            
            // Calcul des positions des slots
            int columns = _currentContainer.Inventory.Columns;
            int rows = _currentContainer.Inventory.Rows;
            int totalWidth = columns * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
            int startX = panelX + (WINDOW_WIDTH - totalWidth) / 2;
            int startY = panelY + HEADER_HEIGHT + 20;
            
            // Gestion du clic gauche pour prendre un item
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !_sharedDraggedItem.IsDragging)
            {
                for (int i = _currentContainer.Inventory.Slots.Count - 1; i >= 0; i--)
                {
                    int row = i / columns;
                    int col = i % columns;
                    if (row >= rows) break;
                    
                    int slotX = startX + col * SLOT_STEP;
                    int slotY = startY + row * SLOT_STEP;
                    
                    if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE)))
                    {
                        var slot = _currentContainer.Inventory.Slots[i];
                        if (!slot.IsEmpty && slot.Item != null)
                        {
                            // Si Shift est enfoncé, transférer vers l'inventaire
                            if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))
                            {
                                TransferToInventory(i);
                            }
                            else
                            {
                                _sharedDraggedItem.StartDrag(slot.Item, slot.Count, mousePos);
                                slot.Clear();
                            }
                        }
                        break;
                    }
                }
            }
            
            // Dépôt d'un item dragué
            if (_sharedDraggedItem.IsDragging && Raylib.IsMouseButtonReleased(MouseButton.Left))
            {
                for (int i = _currentContainer.Inventory.Slots.Count - 1; i >= 0; i--)
                {
                    int row = i / columns;
                    int col = i % columns;
                    if (row >= rows) break;
                    
                    int slotX = startX + col * SLOT_STEP;
                    int slotY = startY + row * SLOT_STEP;
                    
                    if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE)))
                    {
                        TryDropOnContainerSlot(i);
                        break;
                    }
                }
            }
            
            // Fermeture avec ESC
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                Close();
        }
        
        private static void TransferToInventory(int slotIndex)
        {
            if (_currentContainer == null) return;
            
            var slot = _currentContainer.Inventory.Slots[slotIndex];
            if (slot.IsEmpty || slot.Item == null) return;
            
            int remaining = Program.AddItemToInventory(slot.Item, slot.Count);
            if (remaining == 0)
            {
                slot.Clear();
            }
            else if (remaining < slot.Count)
            {
                slot.Count = remaining;
            }
        }
        
        private static void TryDropOnContainerSlot(int targetSlot)
        {
            if (_currentContainer == null || _sharedDraggedItem?.Item == null) return;
            
            var target = _currentContainer.Inventory.Slots[targetSlot];
            int maxStack = GameData.ItemDatabase[GetItemId(_sharedDraggedItem.Item.Name)].StackSize;
            
            if (target.IsEmpty)
            {
                target.Item = _sharedDraggedItem.Item;
                target.Count = _sharedDraggedItem.Count;
                _sharedDraggedItem.EndDrag();
            }
            else if (target.Item != null && AreItemsCompatible(target.Item, _sharedDraggedItem.Item))
            {
                int space = maxStack - target.Count;
                if (space > 0)
                {
                    int add = Math.Min(space, _sharedDraggedItem.Count);
                    Program.MergeSpoilMeta(target.Item, _sharedDraggedItem.Item, add);
                    target.Count += add;
                    _sharedDraggedItem.Count -= add;
                    if (_sharedDraggedItem.Count <= 0)
                        _sharedDraggedItem.EndDrag();
                }
            }
            else
            {
                // Échanger les items
                var oldItem = target.Item;
                var oldCount = target.Count;
                
                target.Item = _sharedDraggedItem.Item;
                target.Count = _sharedDraggedItem.Count;
                
                _sharedDraggedItem.Item = oldItem;
                _sharedDraggedItem.Count = oldCount;
            }
        }
        
        private static bool AreItemsCompatible(Item a, Item b)
        {
            return Program.AreItemsStackable(a, b);
        }
        
        private static int GetItemId(string name)
        {
            return GameData.GetItemId(name);
        }
        
        public static void Draw()
        {
            if (!_isOpen || _currentContainer == null) return;
            
            int panelX = (int)_windowPos.X;
            int panelY = (int)_windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();
            
            // Dessiner le panneau principal
            if (_useTextures && _panelTexture != null && _panelTexture.IsValid)
            {
                _panelTexture.Draw(new Rectangle(panelX, panelY, WINDOW_WIDTH, WINDOW_HEIGHT), Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(new Rectangle(panelX + 4, panelY + 4, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, new Color(0, 0, 0, 100));
                Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, COLOR_BG);
                Raylib.DrawRectangleRoundedLines(new Rectangle(panelX, panelY, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, 2, COLOR_BORDER);
            }
            
            // Barre de titre
            Color titleColor = _isHoveringTitleBar ? new Color(80, 70, 50, 200) : COLOR_HEADER;
            Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, titleColor);
            Raylib.DrawRectangleRoundedLines(new Rectangle(panelX, panelY, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, 1, COLOR_ACCENT);
            
            // Titre
            string title = " PANIER EN OSIER";
            int titleWidth = FontManager.MeasureText(title, 16);
            FontManager.DrawText(title, panelX + (WINDOW_WIDTH - titleWidth) / 2, panelY + 12, 16, Color.White);
            
            // Bouton fermer
            Rectangle closeBtn = new Rectangle(panelX + WINDOW_WIDTH - 32, panelY + 8, 24, 24);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeBtn.X + 7, (int)closeBtn.Y + 4, 14, hoverClose ? Color.Red : new Color(200, 120, 120, 200));
            
            if (hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Close();
                return;
            }
            
            // Dessiner les slots du conteneur
            int columns = _currentContainer.Inventory.Columns;
            int rows = _currentContainer.Inventory.Rows;
            int totalWidth = columns * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
            int startX = panelX + (WINDOW_WIDTH - totalWidth) / 2;
            int startY = panelY + HEADER_HEIGHT + 20;

            int hoveredIndex = -1;
            for (int i = _currentContainer.Inventory.Slots.Count - 1; i >= 0; i--)
            {
                int row = i / columns;
                int col = i % columns;
                if (row >= rows) continue;

                int slotX = startX + col * SLOT_STEP;
                int slotY = startY + row * SLOT_STEP;
                if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE)))
                {
                    hoveredIndex = i;
                    break;
                }
            }
            
            for (int drawPass = 0; drawPass < 2; drawPass++)
            {
                for (int i = 0; i < _currentContainer.Inventory.Slots.Count; i++)
                {
                    int row = i / columns;
                    int col = i % columns;
                    if (row >= rows) break;
                    
                    int slotX = startX + col * SLOT_STEP;
                    int slotY = startY + row * SLOT_STEP;
                    
                    bool isHovered = i == hoveredIndex;
                    if (isHovered != (drawPass == 1))
                        continue;

                    DrawSlot(slotX, slotY, _currentContainer.Inventory.Slots[i], isHovered,
                        i % columns == 0,
                        i + 1 >= _currentContainer.Inventory.Slots.Count || i % columns == columns - 1,
                        i < columns,
                        i + columns >= _currentContainer.Inventory.Slots.Count);
                }
            }
            
            // Indicateur
            string hint = "Glissez les items entre l'inventaire et le panier";
            int hintWidth = FontManager.MeasureText(hint, 11);
            FontManager.DrawText(hint, panelX + (WINDOW_WIDTH - hintWidth) / 2, panelY + WINDOW_HEIGHT - 22, 11, new Color(150, 140, 110, 180));
            
            // Dessiner l'item dragué
            if (_sharedDraggedItem != null && _sharedDraggedItem.IsDragging && _sharedDraggedItem.Item != null)
            {
                Vector2 mouse = Raylib.GetMousePosition();
                Rectangle dest = new Rectangle(mouse.X - 24, mouse.Y - 24, 48, 48);
                if (_sharedDraggedItem.Item.Icon.Id != 0)
                    Raylib.DrawTexturePro(_sharedDraggedItem.Item.Icon, new Rectangle(0, 0, _sharedDraggedItem.Item.Icon.Width, _sharedDraggedItem.Item.Icon.Height), dest, Vector2.Zero, 0, new Color(255, 255, 255, 200));
                else
                    Raylib.DrawRectangleRounded(dest, 0.1f, 6, _sharedDraggedItem.Item.DisplayColor);
                if (_sharedDraggedItem.Count > 1)
                    FontManager.DrawText(_sharedDraggedItem.Count.ToString(), (int)mouse.X + 15, (int)mouse.Y + 15, 16, Color.White);
            }
        }
        
        private static void DrawSlot(int x, int y, InventorySlot slot, bool isHovered, bool borderLeft, bool borderRight, bool borderUp, bool borderDown)
        {
            bool pressed = isHovered && Raylib.IsMouseButtonDown(MouseButton.Left);
            Color fallbackColor = isHovered ? COLOR_SLOT_HOVER : COLOR_SLOT_NORMAL;
            UIManager.DrawItemSlotBackground(new Rectangle(x, y, SLOT_SIZE, SLOT_SIZE), isHovered, pressed, fallbackColor, borderUp, borderDown, borderLeft, borderRight);
            
            if (!slot.IsEmpty && slot.Item != null)
            {
                ItemRenderer.DrawItemPadded(slot.Item, x + ITEM_OFFSET, y + ITEM_OFFSET, ITEM_SIZE, 6);
                if (slot.Count > 1)
                    FontManager.DrawText(slot.Count.ToString(), x + ITEM_OFFSET + ITEM_SIZE - 18, y + ITEM_OFFSET + ITEM_SIZE - 18, 14, Color.White);
            }
        }
        
        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;
    }
}