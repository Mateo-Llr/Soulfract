// DyeingUI.cs - Version améliorée avec nine‑slice, grand slot, code hexa éditable
using Raylib_cs;
using System.Numerics;
using System.Text;

namespace Soulfract
{
    public static class DyeingUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = new Vector2(400, 150);
        private static Vector2 _dragOffset;
        private static bool _isDraggingWindow = false;
        private static bool _isHoveringTitleBar = false;

        private static InventorySlot _dyeSlot = new InventorySlot();
        private static Color _selectedColor = Color.White;
        private static int _selectedLayer = 0;
        private static DraggedItem? _sharedDraggedItem;

        // Hex input
        private static string _hexInput = "FFFFFF";
        private static bool _isEditingHex = false;
        private static bool _hexHasFocus = false;
        private static string _hexTemp = "";

        // RGB inputs
        private static int _editingChannel = -1;
        private static string _channelTemp = "";

        // Textures / nine‑slices (chargées une seule fois)
        private static Texture2D _slotTexture;
        private static NineSliceTexture? _woodPanel;
        private static NineSliceTexture? _buttonPanel;

        private static int WINDOW_WIDTH => UIManager.ScaleInt(460);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(540);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(40);
        private static int SLOT_SIZE => UIManager.ScaleInt(80);

        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_BORDER = new Color(100, 100, 120, 200);
        private static readonly Color COLOR_LAYER_BUTTON_ACTIVE = new Color(140, 120, 70, 255);
        private static readonly Color COLOR_LAYER_BUTTON_HOVER = new Color(90, 90, 110, 255);

        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;

        public static bool IsOpen => _isOpen;

        public static bool IsTextInputFocused => _isEditingHex || _hexHasFocus || _editingChannel >= 0;

        public static void Initialize(DraggedItem sharedDraggedItem)
        {
            _sharedDraggedItem = sharedDraggedItem;
            LoadTextures();
        }

        private static void LoadTextures()
        {
            // Slot
            _slotTexture = TryLoadTexture("assets/gui/item_ui_case.png");
            if (_slotTexture.Id == 0)
            {
                // Fallback : créer une texture simple
                Image img = Raylib.GenImageColor(64, 64, new Color(50, 53, 60, 255));
                _slotTexture = Raylib.LoadTextureFromImage(img);
                Raylib.UnloadImage(img);
            }

            // Nine‑slices via UIManager (déjà initialisé)
            _woodPanel = UIManager.ScrollPanelTexture;
            _buttonPanel = UIManager.ButtonTexture;
        }

        private static Texture2D TryLoadTexture(string path)
        {
            if (File.Exists(path))
            {
                var tex = Raylib.LoadTexture(path);
                if (tex.Id != 0)
                    return tex;
            }
            return new Texture2D();
        }

        public static void Open()
        {
            _isOpen = true;
            _dyeSlot.Clear();
            _selectedColor = Color.White;
            _selectedLayer = 0;
            _hexInput = "FFFFFF";
            _isEditingHex = false;
            _editingChannel = -1;
            _channelTemp = "";
            _sharedDraggedItem?.EndDrag();
        }

        public static void Close()
        {
            _isOpen = false;
            _dyeSlot.Clear();
            _sharedDraggedItem?.EndDrag();
            _isDraggingWindow = false;
            _isEditingHex = false;
            _editingChannel = -1;
            _channelTemp = "";
        }

        public static void Update()
        {
            if (!_isOpen) return;

            Vector2 mousePos = Raylib.GetMousePosition();
            Rectangle titleBar = new Rectangle(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, HEADER_HEIGHT);
            _isHoveringTitleBar = Raylib.CheckCollisionPointRec(mousePos, titleBar);

            // Drag de la fenêtre
            if (_isHoveringTitleBar && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDraggingWindow = true;
                _dragOffset = mousePos - _windowPos;
            }
            if (_isDraggingWindow && Raylib.IsMouseButtonDown(MouseButton.Left))
                _windowPos = mousePos - _dragOffset;
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDraggingWindow = false;

            // Clamp position
            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + 50, Raylib.GetScreenWidth() - 50);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, Raylib.GetScreenHeight() - 50);

            // ========== SLOT ==========
            int slotX = (int)_windowPos.X + (WINDOW_WIDTH - SLOT_SIZE) / 2;
            int slotY = (int)_windowPos.Y + HEADER_HEIGHT + 15;
            Rectangle slotRect = new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE);
            bool hoverSlot = Raylib.CheckCollisionPointRec(mousePos, slotRect);

            // Gestion du drag & drop
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !_sharedDraggedItem.IsDragging && hoverSlot)
            {
                if (!_dyeSlot.IsEmpty && _dyeSlot.Item != null)
                {
                    _sharedDraggedItem.StartDrag(_dyeSlot.Item, 1, mousePos);
                    _dyeSlot.Clear();
                    _selectedColor = Color.White;
                    _hexInput = "FFFFFF";
                    _selectedLayer = 0;
                }
            }

            if (_sharedDraggedItem.IsDragging && Raylib.IsMouseButtonReleased(MouseButton.Left) && hoverSlot)
            {
                if (_dyeSlot.IsEmpty && _sharedDraggedItem.Item != null)
                {
                    _dyeSlot.Item = _sharedDraggedItem.Item;
                    _dyeSlot.Count = 1;
                    _sharedDraggedItem.EndDrag();

                    _selectedLayer = 0;
                    _selectedColor = _dyeSlot.Item.GetLayerColor(0);
                    UpdateHexFromColor();
                }
            }

            // ========== SÉLECTEUR DE COUCHE ==========
            int layers = GetItemLayers(_dyeSlot.Item);
            if (layers > 1 && !_dyeSlot.IsEmpty && _dyeSlot.Item != null)
            {
                int layerPanelY = (int)_windowPos.Y + HEADER_HEIGHT + SLOT_SIZE + 25;
                int layerButtonW = 55;
                int layerButtonH = 55;
                int layerSpacing = 12;
                int totalWidth = layers * (layerButtonW + layerSpacing) - layerSpacing;
                int layerStartX = (int)_windowPos.X + (WINDOW_WIDTH - totalWidth) / 2;

                for (int i = 0; i < layers; i++)
                {
                    int btnX = layerStartX + i * (layerButtonW + layerSpacing);
                    int layerBtnY = layerPanelY;
                    Rectangle btnRect = new Rectangle(btnX, layerBtnY, layerButtonW, layerButtonH);
                    bool isHover = Raylib.CheckCollisionPointRec(mousePos, btnRect);
                    bool isActive = (i == _selectedLayer);

                    Color btnColor = isActive ? COLOR_LAYER_BUTTON_ACTIVE :
                                     isHover ? COLOR_LAYER_BUTTON_HOVER :
                                     new Color(60, 60, 80, 255);

                    Raylib.DrawRectangleRounded(btnRect, 0.15f, 8, btnColor);
                    Raylib.DrawRectangleRoundedLines(btnRect, 0.15f, 8, 1, COLOR_BORDER);

                    Color layerColor = GetCurrentLayerColor(_dyeSlot.Item, i);
                    int colorRectSize = 34;
                    int colorRectX = btnX + (layerButtonW - colorRectSize) / 2;
                    int colorRectY = layerBtnY + (layerButtonH - colorRectSize) / 2;
                    Raylib.DrawRectangleRounded(new Rectangle(colorRectX, colorRectY, colorRectSize, colorRectSize), 0.1f, 6, layerColor);

                    string layerNum = (i + 1).ToString();
                    int fontSize = 12;
                    int textWidth = Raylib.MeasureText(layerNum, fontSize);
                    Raylib.DrawText(layerNum, btnX + (layerButtonW - textWidth) / 2, layerBtnY + layerButtonH - 16, fontSize, new Color(200, 200, 200, 180));

                    if (isHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        _selectedLayer = i;
                        Color newColor = GetCurrentLayerColor(_dyeSlot.Item, i);
                        SetSelectedColor(newColor);
                    }
                }
            }

            // ========== CURSEURS RGB ==========
            int sliderX = (int)_windowPos.X + 30;
            int sliderWidth = WINDOW_WIDTH - 140;
            int sliderStartY = (int)_windowPos.Y + HEADER_HEIGHT + SLOT_SIZE + 110;
            if (layers > 1 && !_dyeSlot.IsEmpty)
                sliderStartY += 75;

            const int SLIDER_HEIGHT = 14;
            const int CHANNEL_INPUT_WIDTH = 55;
            const int CHANNEL_INPUT_HEIGHT = 24;
            int channelInputX = sliderX + sliderWidth + 10;
            int channelInputY = sliderStartY - 23;

            // Rouge
            Rectangle redInput = new Rectangle(channelInputX, channelInputY, CHANNEL_INPUT_WIDTH, CHANNEL_INPUT_HEIGHT);
            Rectangle redBg = new Rectangle(sliderX, sliderStartY, sliderWidth, SLIDER_HEIGHT);
            if (Raylib.CheckCollisionPointRec(mousePos, redBg) && !Raylib.CheckCollisionPointRec(mousePos, redInput) && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                float percent = (mousePos.X - sliderX) / sliderWidth;
                _selectedColor.R = (byte)(Math.Clamp(percent, 0, 1) * 255);
                UpdateHexFromColor();
            }
            // Vert
            Rectangle greenInput = new Rectangle(channelInputX, channelInputY + 30, CHANNEL_INPUT_WIDTH, CHANNEL_INPUT_HEIGHT);
            Rectangle greenBg = new Rectangle(sliderX, sliderStartY + 30, sliderWidth, SLIDER_HEIGHT);
            if (Raylib.CheckCollisionPointRec(mousePos, greenBg) && !Raylib.CheckCollisionPointRec(mousePos, greenInput) && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                float percent = (mousePos.X - sliderX) / sliderWidth;
                _selectedColor.G = (byte)(Math.Clamp(percent, 0, 1) * 255);
                UpdateHexFromColor();
            }
            // Bleu
            Rectangle blueInput = new Rectangle(channelInputX, channelInputY + 60, CHANNEL_INPUT_WIDTH, CHANNEL_INPUT_HEIGHT);
            Rectangle blueBg = new Rectangle(sliderX, sliderStartY + 60, sliderWidth, SLIDER_HEIGHT);
            if (Raylib.CheckCollisionPointRec(mousePos, blueBg) && !Raylib.CheckCollisionPointRec(mousePos, blueInput) && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                float percent = (mousePos.X - sliderX) / sliderWidth;
                _selectedColor.B = (byte)(Math.Clamp(percent, 0, 1) * 255);
                UpdateHexFromColor();
            }

            Rectangle[] channelInputs = { redInput, greenInput, blueInput };
            bool channelInputClosed = false;
            for (int channel = 0; channel < channelInputs.Length; channel++)
            {
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, channelInputs[channel]))
                {
                    _editingChannel = channel;
                    _channelTemp = GetSelectedChannelValue(channel).ToString();
                }
            }

            if (_editingChannel >= 0)
            {
                TextInput.Update(ref _channelTemp, $"dye-channel-{_editingChannel}", channelInputs[_editingChannel], 3, char.IsDigit);
                if (int.TryParse(_channelTemp, out int channelValue) && channelValue <= 255)
                {
                    SetSelectedChannelValue(_editingChannel, channelValue);
                    UpdateHexFromColor();
                }

                if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    channelInputClosed = Raylib.IsKeyPressed(KeyboardKey.Escape);
                    _editingChannel = -1;
                    _channelTemp = "";
                    TextInput.Reset();
                    UpdateHexFromColor();
                }
            }

            // ========== CODE HEXADÉCIMAL ==========
            int hexY = sliderStartY + 90;
            Rectangle hexRect = new Rectangle(sliderX + 10, hexY, 150, 30);
            bool hexHover = Raylib.CheckCollisionPointRec(mousePos, hexRect);

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && hexHover)
            {
                _isEditingHex = true;
                _hexTemp = _hexInput;
                _hexHasFocus = true;
            }

            if (_isEditingHex)
            {
                TextInput.Update(ref _hexTemp, "dye-hex", hexRect, 6, IsHexDigit);
                _hexTemp = _hexTemp.ToUpperInvariant();

                // Validation (Entrée) ou annulation (Echap)
                if (Raylib.IsKeyPressed(KeyboardKey.Enter))
                {
                    if (_hexTemp.Length == 6)
                    {
                        _hexInput = _hexTemp;
                        if (TryParseHex(_hexInput, out Color parsed))
                            _selectedColor = parsed;
                    }
                    _isEditingHex = false;
                    _hexHasFocus = false;
                    TextInput.Reset("dye-hex");
                }
                else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    _isEditingHex = false;
                    _hexHasFocus = false;
                    TextInput.Reset("dye-hex");
                }

                // Mise à jour de la couleur pendant l'édition (si valide)
                if (_hexTemp.Length == 6 && TryParseHex(_hexTemp, out Color tempColor))
                    _selectedColor = tempColor;
            }

            // ========== APERÇU DE LA COULEUR (clic pour remettre la teinte de la couche) ==========
            int previewX = (int)(hexRect.X + hexRect.Width + 20);
            int previewY = hexY;
            int previewSize = 30;
            Rectangle previewRect = new Rectangle(previewX, previewY, previewSize, previewSize);

            if (!_dyeSlot.IsEmpty && _dyeSlot.Item != null)
            {
                bool previewHover = Raylib.CheckCollisionPointRec(mousePos, previewRect);
                if (previewHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    // Remettre la couleur de la couche actuelle
                    Color currentLayerColor = GetCurrentLayerColor(_dyeSlot.Item, _selectedLayer);
                    SetSelectedColor(currentLayerColor);
                }
            }

            // ========== BOUTON TEINDRE ==========
            int btnY = (int)_windowPos.Y + WINDOW_HEIGHT - 60;
            Rectangle dyeBtn = new Rectangle((int)_windowPos.X + 50, btnY, WINDOW_WIDTH - 100, 42);
            bool hoverDye = Raylib.CheckCollisionPointRec(mousePos, dyeBtn);
            if (hoverDye && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if (_dyeSlot.IsEmpty || _dyeSlot.Item == null)
                {
                    Program.AddNotification(new Notification("Placez d'abord un item dans le slot", Color.Red, 1.5f));
                }
                else
                {
                    int itemId = GetItemId(_dyeSlot.Item.Name);
                    if (GameData.ItemDatabase.TryGetValue(itemId, out var data) && data.IsDyeable)
                    {
                        int itemLayers = data.DyeLayers;
                        _dyeSlot.Item.SetLayerColor(_selectedLayer, _selectedColor);
                        string layerText = itemLayers > 1 ? $" (couche {_selectedLayer + 1})" : "";
                        Program.AddNotification(new Notification($"Teinture appliquée à {_dyeSlot.Item.Name}{layerText}", new Color(100, 255, 100, 255), 1.5f));
                    }
                    else
                    {
                        Program.AddNotification(new Notification($"{_dyeSlot.Item.Name} n'est pas teignable", Color.Red, 1.5f));
                    }
                }
            }

            // Fermeture
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) && !_isEditingHex && _editingChannel < 0 && !channelInputClosed)
                Close();
            else if (Raylib.IsKeyPressed(KeyboardKey.Escape) && _isEditingHex)
            {
                _isEditingHex = false;
                _hexHasFocus = false;
            }
        }

        private static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static bool TryParseHex(string hex, out Color color)
        {
            color = Color.White;
            if (hex.Length != 6) return false;
            if (!byte.TryParse(hex[0..2], System.Globalization.NumberStyles.HexNumber, null, out byte r)) return false;
            if (!byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g)) return false;
            if (!byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte b)) return false;
            color = new Color(r, g, b, (byte)255);
            return true;
        }

        private static void UpdateHexFromColor()
        {
            _hexInput = $"{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";
            if (!_isEditingHex)
                _hexTemp = _hexInput;
        }

        private static void SetSelectedColor(Color color)
        {
            _selectedColor = color;
            UpdateHexFromColor();
        }

        private static int GetSelectedChannelValue(int channel) => channel switch
        {
            0 => _selectedColor.R,
            1 => _selectedColor.G,
            _ => _selectedColor.B
        };

        private static void SetSelectedChannelValue(int channel, int value)
        {
            byte channelValue = (byte)Math.Clamp(value, 0, 255);
            switch (channel)
            {
                case 0:
                    _selectedColor.R = channelValue;
                    break;
                case 1:
                    _selectedColor.G = channelValue;
                    break;
                default:
                    _selectedColor.B = channelValue;
                    break;
            }
        }

        private static int GetItemLayers(Item item)
        {
            if (item == null) return 1;
            int itemId = GetItemId(item.Name);
            if (GameData.ItemDatabase.TryGetValue(itemId, out var data))
                return data.DyeLayers;
            return 1;
        }

        private static Color GetCurrentLayerColor(Item item, int layer)
        {
            if (item == null) return Color.White;
            return item.GetLayerColor(layer);
        }

        private static int GetItemId(string name) => GameData.GetItemId(name);

        public static void Draw()
        {
            if (!_isOpen) return;

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();

            // ========== FENÊTRE EN BOIS (nine‑slice) ==========
            if (_woodPanel != null && _woodPanel.IsValid)
            {
                _woodPanel.Draw(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), Color.White);
            }
            else
            {
                // Fallback
                Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.08f, 12, new Color(25, 28, 35, 240));
                Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.08f, 12, 2, COLOR_BORDER);
            }

            // ========== BARRE DE TITRE ==========
            // On laisse le fond du wood, on ajoute simplement un trait et le texte
            Color titleBg = _isHoveringTitleBar ? new Color(80, 70, 50, 200) : new Color(35, 38, 48, 200);
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.08f, 8, titleBg);
            Raylib.DrawLine((int)x, (int)(y + HEADER_HEIGHT), (int)(x + WINDOW_WIDTH), (int)(y + HEADER_HEIGHT), COLOR_ACCENT);
            FontManager.DrawText("BAC À TEINTURE", x + 15, y + 11, 18, Color.White);

            // Bouton fermer
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 34, y + 6, 28, 28);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            Raylib.DrawText("X", (int)closeBtn.X + 9, (int)closeBtn.Y + 5, 16, hoverClose ? Color.Red : new Color(200, 120, 120, 200));
            if (hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
                Close();

            // ========== SLOT DE L'ITEM (grand, avec texture item_ui_case) ==========
            int slotX = x + (WINDOW_WIDTH - SLOT_SIZE) / 2;
            int slotY = y + HEADER_HEIGHT + 15;
            Rectangle slotRect = new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE);
            bool hoverSlot = Raylib.CheckCollisionPointRec(mousePos, slotRect);

            // Fond du slot
            if (_slotTexture.Id != 0)
            {
                // On adapte la texture à la taille du slot
                float scaleX = (float)SLOT_SIZE / _slotTexture.Width;
                float scaleY = (float)SLOT_SIZE / _slotTexture.Height;
                float scale = Math.Max(scaleX, scaleY);
                float drawW = _slotTexture.Width * scale;
                float drawH = _slotTexture.Height * scale;
                float drawX = slotRect.X + (SLOT_SIZE - drawW) / 2;
                float drawY = slotRect.Y + (SLOT_SIZE - drawH) / 2;
                Raylib.DrawTexturePro(_slotTexture,
                    new Rectangle(0, 0, _slotTexture.Width, _slotTexture.Height),
                    new Rectangle(drawX, drawY, drawW, drawH),
                    Vector2.Zero, 0, Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(slotRect, 0.1f, 6, new Color(50, 53, 60, 255));
                Raylib.DrawRectangleRoundedLines(slotRect, 0.1f, 6, 1, COLOR_BORDER);
            }

            // Si un item est présent, on le dessine par‑dessus
            if (!_dyeSlot.IsEmpty && _dyeSlot.Item != null)
            {
                ItemRenderer.DrawItemPadded(_dyeSlot.Item, slotX, slotY, SLOT_SIZE, 4);
                if (_dyeSlot.Count > 1)
                    Raylib.DrawText(_dyeSlot.Count.ToString(), slotX + SLOT_SIZE - 20, slotY + SLOT_SIZE - 20, 16, Color.White);
            }
            else
            {
                // Texte d'invite
                string hint = "Glissez un item";
                int hintW = FontManager.MeasureText(hint, 11);
                FontManager.DrawText(hint, slotX + (SLOT_SIZE - hintW) / 2, slotY + SLOT_SIZE / 2 + 4, 11, new Color(150, 140, 120, 180));
            }

            // ========== SÉLECTEUR DE COUCHE ==========
            int layers = GetItemLayers(_dyeSlot.Item);
            if (layers > 1 && !_dyeSlot.IsEmpty && _dyeSlot.Item != null)
            {
                int layerPanelY = y + HEADER_HEIGHT + SLOT_SIZE + 25;
                int layerButtonW = 55;
                int layerButtonH = 55;
                int layerSpacing = 12;
                int totalWidth = layers * (layerButtonW + layerSpacing) - layerSpacing;
                int layerStartX = x + (WINDOW_WIDTH - totalWidth) / 2;

                FontManager.DrawText("COUCHES", layerStartX, layerPanelY - 20, 11, new Color(180, 160, 100, 200));

                for (int i = 0; i < layers; i++)
                {
                    int btnX = layerStartX + i * (layerButtonW + layerSpacing);
                    int layerBtnY = layerPanelY;
                    Rectangle btnRect = new Rectangle(btnX, layerBtnY, layerButtonW, layerButtonH);
                    bool isHover = Raylib.CheckCollisionPointRec(mousePos, btnRect);
                    bool isActive = (i == _selectedLayer);

                    Color btnColor = isActive ? COLOR_LAYER_BUTTON_ACTIVE :
                                     isHover ? COLOR_LAYER_BUTTON_HOVER :
                                     new Color(60, 60, 80, 255);

                    Raylib.DrawRectangleRounded(btnRect, 0.15f, 8, btnColor);
                    Raylib.DrawRectangleRoundedLines(btnRect, 0.15f, 8, 1, COLOR_BORDER);

                    Color layerColor = GetCurrentLayerColor(_dyeSlot.Item, i);
                    int colorRectSize = 34;
                    int colorRectX = btnX + (layerButtonW - colorRectSize) / 2;
                    int colorRectY = layerBtnY + (layerButtonH - colorRectSize) / 2;
                    Raylib.DrawRectangleRounded(new Rectangle(colorRectX, colorRectY, colorRectSize, colorRectSize), 0.1f, 6, layerColor);

                    string layerNum = (i + 1).ToString();
                    int fontSize = 12;
                    int textWidth = Raylib.MeasureText(layerNum, fontSize);
                    Raylib.DrawText(layerNum, btnX + (layerButtonW - textWidth) / 2, layerBtnY + layerButtonH - 16, fontSize, new Color(200, 200, 200, 180));
                }
            }

            // ========== CURSEURS RGB ==========
            int sliderX = x + 30;
            int sliderWidth = WINDOW_WIDTH - 140;
            int sliderStartY = y + HEADER_HEIGHT + SLOT_SIZE + 110;
            if (layers > 1 && !_dyeSlot.IsEmpty)
                sliderStartY += 75;

            const int SLIDER_HEIGHT = 14;

            // Afficher les valeurs
            FontManager.DrawText("R", sliderX, sliderStartY - 18, 12, Color.Red);
            DrawSlider(sliderX, sliderStartY, sliderWidth, SLIDER_HEIGHT, _selectedColor.R, Color.Red);

            FontManager.DrawText("G", sliderX, sliderStartY + 30 - 18, 12, Color.Green);
            DrawSlider(sliderX, sliderStartY + 30, sliderWidth, SLIDER_HEIGHT, _selectedColor.G, Color.Green);

            FontManager.DrawText("B", sliderX, sliderStartY + 60 - 18, 12, Color.Blue);
            DrawSlider(sliderX, sliderStartY + 60, sliderWidth, SLIDER_HEIGHT, _selectedColor.B, Color.Blue);

            Rectangle[] channelInputs =
            {
                new Rectangle(sliderX + sliderWidth + 10, sliderStartY - 23, 55, 24),
                new Rectangle(sliderX + sliderWidth + 10, sliderStartY + 7, 55, 24),
                new Rectangle(sliderX + sliderWidth + 10, sliderStartY + 37, 55, 24)
            };
            for (int channel = 0; channel < channelInputs.Length; channel++)
            {
                Rectangle inputRect = channelInputs[channel];
                bool isEditing = _editingChannel == channel;
                bool isHover = Raylib.CheckCollisionPointRec(mousePos, inputRect);
                Color inputBg = isEditing || isHover ? new Color(60, 60, 70, 220) : new Color(40, 40, 50, 200);
                Raylib.DrawRectangleRounded(inputRect, 0.15f, 5, inputBg);
                Raylib.DrawRectangleRoundedLines(inputRect, 0.15f, 5, 1, isEditing ? new Color(255, 220, 120, 255) : COLOR_BORDER);

                string valueText = isEditing ? _channelTemp : GetSelectedChannelValue(channel).ToString();
                TextInput.DrawSingleLine(valueText, $"dye-channel-{channel}", inputRect, 15, Color.White,
                    new Color(90, 110, 150, 220), COLOR_ACCENT, 4);
            }

            // ========== CODE HEXADÉCIMAL ==========
            int hexY = sliderStartY + 90;
            Rectangle hexRect = new Rectangle(sliderX + 10, hexY, 150, 30);
            bool hexHover = Raylib.CheckCollisionPointRec(mousePos, hexRect);
            Color hexBg = (_isEditingHex || hexHover) ? new Color(60, 60, 70, 220) : new Color(40, 40, 50, 200);
            Raylib.DrawRectangleRounded(hexRect, 0.15f, 6, hexBg);
            Raylib.DrawRectangleRoundedLines(hexRect, 0.15f, 6, 1, _isEditingHex ? new Color(255, 220, 120, 255) : COLOR_BORDER);

            int hexFontSize = 18;
            TextInput.DrawSingleLine(_isEditingHex ? _hexTemp : _hexInput, "dye-hex", hexRect, hexFontSize,
                Color.White, new Color(90, 110, 150, 220), COLOR_ACCENT, 10);

            // ========== APERÇU DE LA COULEUR ==========
            int previewX = (int)(hexRect.X + hexRect.Width + 20);
            int previewY = hexY;
            int previewSize = 30;
            Rectangle previewRect = new Rectangle(previewX, previewY, previewSize, previewSize);
            Raylib.DrawRectangleRounded(previewRect, 0.1f, 6, _selectedColor);
            Raylib.DrawRectangleRoundedLines(previewRect, 0.1f, 6, 1, Color.White);

            // ========== BOUTON TEINDRE (nine‑slice) ==========
            int btnY = y + WINDOW_HEIGHT - 60;
            Rectangle dyeBtn = new Rectangle(x + 50, btnY, WINDOW_WIDTH - 100, 42);
            bool hoverDye = Raylib.CheckCollisionPointRec(mousePos, dyeBtn);

            if (_buttonPanel != null && _buttonPanel.IsValid)
            {
                Color tint = hoverDye ? new Color(255, 235, 200, 255) : Color.White;
                _buttonPanel.Draw(dyeBtn, tint);
            }
            else
            {
                // Fallback
                Color btnColor = hoverDye ? new Color(100, 180, 100, 255) : new Color(80, 140, 80, 255);
                Raylib.DrawRectangleRounded(dyeBtn, 0.2f, 6, btnColor);
                Raylib.DrawRectangleRoundedLines(dyeBtn, 0.2f, 6, 1, new Color(100, 200, 100, 150));
            }

            string btnText = (layers > 1 && !_dyeSlot.IsEmpty) ? $"TEINDRE COUCHE {_selectedLayer + 1}" : "TEINDRE";
            int btnTextW = FontManager.MeasureText(btnText, 18);
            FontManager.DrawText(btnText, (int)(dyeBtn.X + dyeBtn.Width / 2 - btnTextW / 2), (int)(dyeBtn.Y + 12), 18, Color.White);

            // ========== INFO ==========
            if (!_dyeSlot.IsEmpty && _dyeSlot.Item != null)
            {
                string info = layers > 1 ? "Cliquez sur une couche pour la sélectionner" : "Glissez un item teignable";
                int infoW = FontManager.MeasureText(info, 11);
                FontManager.DrawText(info, x + (WINDOW_WIDTH - infoW) / 2, btnY - 20, 11, new Color(150, 140, 110, 180));
            }
        }

        private static void DrawSlider(int x, int y, int width, int height, byte value, Color color)
        {
            Rectangle bg = new Rectangle(x, y, width, height);
            Raylib.DrawRectangleRounded(bg, 0.5f, 6, new Color(40, 40, 45, 255));
            float fill = value / 255f;
            if (fill > 0)
            {
                Rectangle fillRect = new Rectangle(x, y, width * fill, height);
                Raylib.DrawRectangleRounded(fillRect, 0.5f, 6, color);
            }
            // Curseur
            int knobX = x + (int)(width * fill);
            Raylib.DrawCircle(knobX, y + height / 2, 8, Color.White);
            Raylib.DrawCircle(knobX, y + height / 2, 6, color);
        }
    }
}