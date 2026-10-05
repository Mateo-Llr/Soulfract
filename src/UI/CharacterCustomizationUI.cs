// CharacterCustomizationUI.cs - Menu de personnalisation via le miroir
#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class CharacterCustomizationUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = new Vector2(300, 150);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isHoveringTitleBar = false;

        private static int WINDOW_WIDTH => UIManager.ScaleInt(620);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(580);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(45);
        private static int PREVIEW_SIZE => UIManager.ScaleInt(200);
        private static int CONTROL_CONTENT_WIDTH => UIManager.ScaleInt(300);

        // Scroll state for mirror controls
        private static int _controlScrollOffset = 0;
        private static int _controlScrollMax = 0;
        private static bool _isDraggingControlScrollbar = false;
        private static float _controlScrollbarDragStartY = 0f;
        private static int _controlScrollbarDragStartOffset = 0;

        // Sliders state for color picking
        private static float _hue = 0.0f;          // 0..1
        private static float _saturation = 0.8f;   // 0..1
        private static float _value = 0.8f;        // 0..1

        // Temporary colors
        private static Color _tempSkinColor;
        private static Color _tempHairColor;
        private static Color _tempEyeColor;
        private static int _tempHairStyle;
        private static int _tempBeardStyle;
        private static int _tempEyeStyle;

        // Original values for cancel
        private static Color _originalSkinColor;
        private static Color _originalHairColor;
        private static Color _originalEyeColor;
        private static int _originalHairStyle;
        private static int _originalBeardStyle;
        private static int _originalEyeStyle;

        // UI state
        private static int _activeTab = 0; // 0=skin,1=hair,2=eyes
        private static string[] _tabs = { "PEAU", "CHEVEUX", "YEUX" };

        // Colors for UI
        private static readonly Color COLOR_BG = new Color(25, 28, 35, 240);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_SLOT_NORMAL = new Color(50, 53, 60, 255);
        private static readonly Color COLOR_BUTTON = new Color(80, 140, 80, 255);
        private static readonly Color COLOR_BUTTON_HOVER = new Color(100, 180, 100, 255);
        private static readonly Color COLOR_RESET = new Color(140, 80, 80, 255);
        private static readonly Color COLOR_RESET_HOVER = new Color(180, 100, 100, 255);
        private static readonly Color COLOR_SLIDER_BG = new Color(40, 43, 48, 200);

        private static Texture2D _closeIcon;
        private static Texture2D _resetIcon;
        private static bool _texturesLoaded = false;

        public static bool IsOpen => _isOpen;

        public static void Open()
        {
            if (!_texturesLoaded) LoadTextures();

            _originalSkinColor = Program.SkinColor;
            _originalHairColor = Program.HairColor;
            _originalEyeColor = Program.EyeColor;
            _originalHairStyle = Program.PlayerHairStyle;
            _originalBeardStyle = Program.PlayerBeardStyle;
            _originalEyeStyle = Program.EyeStyle;

            _tempSkinColor = _originalSkinColor;
            _tempHairColor = _originalHairColor;
            _tempEyeColor = _originalEyeColor;
            _tempHairStyle = _originalHairStyle;
            _tempBeardStyle = _originalBeardStyle;
            _tempEyeStyle = _originalEyeStyle;

            _isOpen = true;
            _activeTab = 0;
        }

        public static void Close()
        {
            _isOpen = false;
            _isDragging = false;
        }

        private static void LoadTextures()
        {
            _closeIcon = LoadTextureOrDefault("assets/gui/close.png");
            _resetIcon = LoadTextureOrDefault("assets/gui/reset.png");
            _texturesLoaded = true;
        }

        private static Texture2D LoadTextureOrDefault(string path)
        {
            if (File.Exists(path))
                return Raylib.LoadTexture(path);
            return new Texture2D();
        }

        private static void UnloadTextures()
        {
            if (_closeIcon.Id != 0) Raylib.UnloadTexture(_closeIcon);
            if (_resetIcon.Id != 0) Raylib.UnloadTexture(_resetIcon);
            _texturesLoaded = false;
        }

        // Convert HSV (0..1) to Color
        private static Color HsvToColor(float h, float s, float v)
        {
            float r = 0, g = 0, b = 0;
            int i = (int)(h * 6);
            float f = h * 6 - i;
            float p = v * (1 - s);
            float q = v * (1 - f * s);
            float t = v * (1 - (1 - f) * s);

            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                case 5: r = v; g = p; b = q; break;
            }
            return new Color((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), (byte)255);
        }

        // Convert Color to HSV (hue 0..1, saturation 0..1, value 0..1)
        private static (float h, float s, float v) ColorToHsv(Color color)
        {
            float r = color.R / 255f;
            float g = color.G / 255f;
            float b = color.B / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;
            float h = 0, s = 0, v = max;

            if (delta > 0.0001f)
            {
                s = delta / max;
                if (Math.Abs(max - r) < 0.0001f)
                    h = (g - b) / delta;
                else if (Math.Abs(max - g) < 0.0001f)
                    h = 2 + (b - r) / delta;
                else
                    h = 4 + (r - g) / delta;
                h *= 60;
                if (h < 0) h += 360;
                h /= 360;
            }
            return (h, s, v);
        }

        // Update the current temp color based on HSV sliders
        private static void UpdateTempColorFromSliders(ref Color target)
        {
            target = HsvToColor(_hue, _saturation, _value);
        }

        // Synchronize sliders from a given color
        private static void SyncSlidersFromColor(Color color)
        {
            var (h, s, v) = ColorToHsv(color);
            _hue = h;
            _saturation = s;
            _value = v;
        }

        // Draw a horizontal slider with a gradient background
        // Returns true if the value changed this frame
        private static bool DrawSlider(int x, int y, int width, int height, ref float value, Func<float, Color> gradientFunc)
        {
            Rectangle sliderRect = new Rectangle(x, y, width, height);
            Vector2 mousePos = Raylib.GetMousePosition();

            // Draw gradient background
            for (int i = 0; i <= width; i++)
            {
                float t = i / (float)width;
                Color col = gradientFunc(t);
                Raylib.DrawRectangle(x + i, y, 1, height, col);
            }
            Raylib.DrawRectangleLines(x, y, width, height, COLOR_BORDER);

            // Draw knob
            int knobX = x + (int)(value * width);
            Raylib.DrawCircle(knobX, y + height / 2, 6, Color.White);
            Raylib.DrawCircle(knobX, y + height / 2, 5, Color.Black);

            bool changed = false;
            if (Raylib.CheckCollisionPointRec(mousePos, sliderRect) && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                float newValue = (mousePos.X - x) / width;
                newValue = Math.Clamp(newValue, 0, 1);
                if (Math.Abs(value - newValue) > 0.001f)
                {
                    value = newValue;
                    changed = true;
                }
            }
            return changed;
        }

        // Gradient for hue: full spectrum
        private static Color HueGradient(float t)
        {
            return HsvToColor(t, 1.0f, 1.0f);
        }

        // Gradient for saturation: white -> current hue+value color
        private static Color SaturationGradient(float t)
        {
            return HsvToColor(_hue, t, _value);
        }

        // Gradient for value: black -> current hue+saturation color
        private static Color ValueGradient(float t)
        {
            return HsvToColor(_hue, _saturation, t);
        }

        // New simple color picker with preview square and three sliders
        private static int DrawColorPicker(int x, int y, string label, ref Color color)
        {
            // Synchronize sliders with the current color when first opening or when color changes externally
            // We'll sync before drawing if the color doesn't match the current HSV
            var (currentH, currentS, currentV) = ColorToHsv(color);
            if (Math.Abs(currentH - _hue) > 0.01f || Math.Abs(currentS - _saturation) > 0.01f || Math.Abs(currentV - _value) > 0.01f)
            {
                _hue = currentH;
                _saturation = currentS;
                _value = currentV;
            }

            FontManager.DrawText(label, x, y, 14, COLOR_ACCENT);

            int previewSize = 60;
            int previewX = x;
            int previewY = y + 25;
            // Draw preview square
            Raylib.DrawRectangle(previewX, previewY, previewSize, previewSize, color);
            Raylib.DrawRectangleLines(previewX, previewY, previewSize, previewSize, COLOR_BORDER);

            int sliderWidth = 200;
            int sliderHeight = 8;
            int sliderX = previewX + previewSize + 15;
            int sliderStartY = previewY;

            // Hue slider
            FontManager.DrawText("Teinte", sliderX, sliderStartY - 8, 12, Color.White);
            bool hueChanged = DrawSlider(sliderX, sliderStartY, sliderWidth, sliderHeight, ref _hue, HueGradient);

            // Saturation slider
            FontManager.DrawText("Saturation", sliderX, sliderStartY + 25 - 8, 12, Color.White);
            bool satChanged = DrawSlider(sliderX, sliderStartY + 25, sliderWidth, sliderHeight, ref _saturation, SaturationGradient);

            // Value slider
            FontManager.DrawText("Luminosité", sliderX, sliderStartY + 50 - 8, 12, Color.White);
            bool valChanged = DrawSlider(sliderX, sliderStartY + 50, sliderWidth, sliderHeight, ref _value, ValueGradient);

            if (hueChanged || satChanged || valChanged)
            {
                UpdateTempColorFromSliders(ref color);
            }

            return 95;
        }

        private static int DrawHairStylePicker(int x, int y)
        {
            FontManager.DrawText("Style de cheveux", x, y, 14, COLOR_ACCENT);
            int startX = x;
            int startY = y + 25;
            int itemSize = 40;
            int spacing = -Math.Max(1, (int)MathF.Round(itemSize * 3f / 22f));
            int cols = 5;
            int rows = (Program.hairBaseTextures.Count + cols - 1) / cols;
            int totalHeight = 25 + rows * itemSize + Math.Max(0, rows - 1) * spacing;

            for (int i = 0; i < Program.hairBaseTextures.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int btnX = startX + col * (itemSize + spacing);
                int btnY = startY + row * (itemSize + spacing);
                Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
                bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
                Color bg = (i == _tempHairStyle) ? COLOR_ACCENT : (hover ? new Color(100, 110, 80, 200) : COLOR_SLOT_NORMAL);
                bool pressed = hover && Raylib.IsMouseButtonDown(MouseButton.Left);
                int lastRow = (Program.hairBaseTextures.Count - 1) / cols;
                UIManager.DrawItemSlotBackground(rect, hover, pressed, bg,
                    row == 0, row == lastRow, col == 0, col == cols - 1 || i == Program.hairBaseTextures.Count - 1);

                Texture2D hairTex = i == 0 ? Program.NoneStyleIcon : Program.hairBaseTextures[i];
                if (hairTex.Id != 0)
                {
                    float scale = i == 0
                        ? itemSize * 0.65f / Math.Max(hairTex.Width, hairTex.Height)
                        : itemSize / (float)hairTex.Width;
                    Vector2 iconPos = i == 0
                        ? new Vector2(btnX + (itemSize - hairTex.Width * scale) / 2f, btnY + (itemSize - hairTex.Height * scale) / 2f)
                        : new Vector2(btnX, btnY);
                    if (i > 0 && i < Program.hairBackTextures.Count && Program.hairBackTextures[i].Id != 0)
                        Raylib.DrawTextureEx(Program.hairBackTextures[i], iconPos, 0, scale, Color.White);
                    Raylib.DrawTextureEx(hairTex, iconPos, 0, scale, Color.White);
                }
                if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    _tempHairStyle = i;
            }
            return totalHeight;
        }

        private static int DrawBeardStylePicker(int x, int y)
        {
            FontManager.DrawText("Style de barbe", x, y, 14, COLOR_ACCENT);
            int startX = x;
            int startY = y + 25;
            int itemSize = 40;
            int spacing = -Math.Max(1, (int)MathF.Round(itemSize * 3f / 22f));
            int cols = 5;
            int rows = (Program.beardBaseTextures.Count + cols - 1) / cols;
            int totalHeight = 25 + rows * itemSize + Math.Max(0, rows - 1) * spacing;

            for (int i = 0; i < Program.beardBaseTextures.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int btnX = startX + col * (itemSize + spacing);
                int btnY = startY + row * (itemSize + spacing);
                Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
                bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
                Color bg = (i == _tempBeardStyle) ? COLOR_ACCENT : (hover ? new Color(100, 110, 80, 200) : COLOR_SLOT_NORMAL);
                bool pressed = hover && Raylib.IsMouseButtonDown(MouseButton.Left);
                int lastRow = (Program.beardBaseTextures.Count - 1) / cols;
                UIManager.DrawItemSlotBackground(rect, hover, pressed, bg,
                    row == 0, row == lastRow, col == 0, col == cols - 1 || i == Program.beardBaseTextures.Count - 1);

                Texture2D beardTex = i == 0 ? Program.NoneStyleIcon : Program.beardBaseTextures[i];
                if (beardTex.Id != 0)
                {
                    float scale = itemSize * (i == 0 ? 0.65f : 1f) / Math.Max(beardTex.Width, beardTex.Height);
                    Vector2 iconPos = i == 0
                        ? new Vector2(btnX + (itemSize - beardTex.Width * scale) / 2f, btnY + (itemSize - beardTex.Height * scale) / 2f)
                        : new Vector2(btnX, btnY);
                    Raylib.DrawTextureEx(beardTex, iconPos, 0, scale, Color.White);
                }
                if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    _tempBeardStyle = i;
            }
            return totalHeight;
        }

        private static void DrawEyeStylePicker(int x, int y)
        {
            FontManager.DrawText("Style d'yeux", x, y, 14, COLOR_ACCENT);
            int startX = x;
            int startY = y + 25;
            int itemSize = 40;
            int spacing = -Math.Max(1, (int)MathF.Round(itemSize * 3f / 22f));
            int cols = 5;

            for (int i = 0; i < Program.EyeBaseTextures.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int btnX = startX + col * (itemSize + spacing);
                int btnY = startY + row * (itemSize + spacing);
                Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
                bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
                Color bg = (i == _tempEyeStyle) ? COLOR_ACCENT : (hover ? new Color(100, 110, 80, 200) : COLOR_SLOT_NORMAL);
                bool pressed = hover && Raylib.IsMouseButtonDown(MouseButton.Left);
                int lastRow = (Program.EyeBaseTextures.Count - 1) / cols;
                UIManager.DrawItemSlotBackground(rect, hover, pressed, bg,
                    row == 0, row == lastRow, col == 0, col == cols - 1 || i == Program.EyeBaseTextures.Count - 1);

                Texture2D eyeTex = Program.EyeBaseTextures[i];
                if (eyeTex.Id != 0)
                {
                    float scale = itemSize / (float)eyeTex.Width;
                    Raylib.DrawTextureEx(eyeTex, new Vector2(btnX, btnY), 0, scale, Color.White);
                }
                if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    _tempEyeStyle = i;
            }
        }

        // Draw the entire UI - handles input and rendering
        public static void Draw()
        {
            if (!_isOpen) return;

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();

            // Window dragging
            Rectangle titleBar = new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT);
            _isHoveringTitleBar = Raylib.CheckCollisionPointRec(mousePos, titleBar);
            if (_isHoveringTitleBar && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDragging = true;
                _dragOffset = mousePos - _windowPos;
            }
            if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                _windowPos = mousePos - _dragOffset;
            else if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDragging = false;

            // Clamp window position
            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + 100, Raylib.GetScreenWidth() - 100);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, Raylib.GetScreenHeight() - 100);
            x = (int)_windowPos.X;
            y = (int)_windowPos.Y;

            // Main panel
            Raylib.DrawRectangleRounded(new Rectangle(x + 4, y + 4, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, new Color(0, 0, 0, 100));
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, COLOR_BG);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, 2, COLOR_BORDER);

            // Title bar
            Color titleColor = _isHoveringTitleBar ? new Color(80, 70, 50, 200) : COLOR_HEADER;
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, titleColor);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, 1, COLOR_ACCENT);
            FontManager.DrawText("PERSONNALISATION", x + 15, y + 13, 18, Color.White);

            // Close button
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 35, y + 10, 25, 25);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            if (_closeIcon.Id != 0)
                Raylib.DrawTexturePro(_closeIcon, new Rectangle(0, 0, _closeIcon.Width, _closeIcon.Height),
                    new Rectangle(closeBtn.X + 4, closeBtn.Y + 4, 17, 17), Vector2.Zero, 0, hoverClose ? Color.Red : new Color(200, 120, 120, 200));
            else
                FontManager.DrawText("X", (int)closeBtn.X + 8, (int)closeBtn.Y + 5, 16, Color.White);
            if (hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
                Close();

            // Tabs
            int tabStartX = x + 20;
            int tabY = y + HEADER_HEIGHT + 10;
            int tabWidth = 80;
            int tabHeight = 30;
            for (int i = 0; i < _tabs.Length; i++)
            {
                Rectangle tabRect = new Rectangle(tabStartX + i * (tabWidth + 10), tabY, tabWidth, tabHeight);
                bool hover = Raylib.CheckCollisionPointRec(mousePos, tabRect);
                Color tabColor = (i == _activeTab) ? COLOR_ACCENT : (hover ? new Color(80, 70, 50, 200) : new Color(50, 45, 35, 180));
                Raylib.DrawRectangleRounded(tabRect, 0.2f, 6, tabColor);
                Raylib.DrawRectangleRoundedLines(tabRect, 0.2f, 6, 1, COLOR_BORDER);
                int textW = FontManager.MeasureText(_tabs[i], 14);
                FontManager.DrawText(_tabs[i], (int)(tabRect.X + (tabWidth - textW) / 2), (int)(tabRect.Y + 8), 14, Color.White);
                if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    _activeTab = i;
            }

            // Preview area
            int previewX = x + WINDOW_WIDTH - PREVIEW_SIZE - 30;
            int previewY = y + HEADER_HEIGHT + 60;
            Vector2 previewPos = new Vector2(previewX + PREVIEW_SIZE / 2, previewY + PREVIEW_SIZE / 2);
            DrawCharacterPreview(previewPos, _tempSkinColor, _tempHairStyle, _tempBeardStyle, _tempHairColor, _tempEyeStyle, _tempEyeColor);

            // Controls area (left side)
            int controlsX = x + 30;
            int controlsY = y + HEADER_HEIGHT + 60;
            int btnY = y + WINDOW_HEIGHT - 55;
            int scrollAreaHeight = btnY - controlsY - 15;

            switch (_activeTab)
            {
                case 0: // Skin color
                    DrawColorPicker(controlsX, controlsY, "Couleur de peau", ref _tempSkinColor);
                    break;
                case 1: // Hair
                    int hairPickerHeight;
                    int beardPickerHeight;
                    int colorPickerHeight;
                    int totalContentHeight;
                    Rectangle scrollViewport = new Rectangle(controlsX, controlsY, CONTROL_CONTENT_WIDTH, scrollAreaHeight);
                    float wheel = Raylib.GetMouseWheelMove();
                    if (wheel != 0 && Raylib.CheckCollisionPointRec(mousePos, scrollViewport))
                    {
                        _controlScrollOffset -= (int)(wheel * 30f);
                        _controlScrollOffset = Math.Clamp(_controlScrollOffset, 0, _controlScrollMax);
                    }

                    Raylib.BeginScissorMode((int)scrollViewport.X, (int)scrollViewport.Y, (int)scrollViewport.Width, (int)scrollViewport.Height);
                    int drawY = controlsY - _controlScrollOffset;
                    hairPickerHeight = DrawHairStylePicker(controlsX, drawY);
                    int beardPickerY = drawY + hairPickerHeight + 20;
                    beardPickerHeight = DrawBeardStylePicker(controlsX, beardPickerY);
                    int colorY = beardPickerY + beardPickerHeight + 20;
                    colorPickerHeight = DrawColorPicker(controlsX, colorY, "Couleur des cheveux", ref _tempHairColor);
                    Raylib.EndScissorMode();

                    totalContentHeight = hairPickerHeight + 20 + beardPickerHeight + 20 + colorPickerHeight;
                    _controlScrollMax = Math.Max(0, totalContentHeight - scrollAreaHeight);
                    _controlScrollOffset = Math.Clamp(_controlScrollOffset, 0, _controlScrollMax);

                    if (_controlScrollMax > 0)
                    {
                        int scrollbarX = controlsX + CONTROL_CONTENT_WIDTH + 8;
                        int scrollbarWidth = 8;
                        int thumbHeight = Math.Max(30, (int)((float)scrollAreaHeight / totalContentHeight * scrollAreaHeight));
                        float scrollPercent = totalContentHeight > scrollAreaHeight ? (float)_controlScrollOffset / (totalContentHeight - scrollAreaHeight) : 0f;
                        int thumbY = controlsY + (int)(scrollPercent * (scrollAreaHeight - thumbHeight));
                        Rectangle trackRect = new Rectangle(scrollbarX, controlsY, scrollbarWidth, scrollAreaHeight);
                        Rectangle thumbRect = new Rectangle(scrollbarX, thumbY, scrollbarWidth, thumbHeight);

                        Raylib.DrawRectangleRounded(trackRect, 0.5f, 4, new Color(40, 43, 48, 200));
                        Raylib.DrawRectangleRounded(thumbRect, 0.5f, 4, new Color(210, 180, 100, 200));

                        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, thumbRect))
                        {
                            _isDraggingControlScrollbar = true;
                            _controlScrollbarDragStartY = mousePos.Y;
                            _controlScrollbarDragStartOffset = _controlScrollOffset;
                        }
                        if (_isDraggingControlScrollbar && Raylib.IsMouseButtonDown(MouseButton.Left))
                        {
                            float deltaY = mousePos.Y - _controlScrollbarDragStartY;
                            float percent = deltaY / Math.Max(1, scrollAreaHeight - thumbHeight);
                            _controlScrollOffset = _controlScrollbarDragStartOffset + (int)(percent * (totalContentHeight - scrollAreaHeight));
                            _controlScrollOffset = Math.Clamp(_controlScrollOffset, 0, _controlScrollMax);
                        }
                        if (_isDraggingControlScrollbar && Raylib.IsMouseButtonReleased(MouseButton.Left))
                            _isDraggingControlScrollbar = false;
                    }
                    else
                    {
                        _controlScrollOffset = 0;
                    }
                    break;
                case 2: // Eyes
                    DrawEyeStylePicker(controlsX, controlsY);
                    DrawColorPicker(controlsX, controlsY + 160, "Couleur des yeux", ref _tempEyeColor);
                    break;
            }

            // Buttons: Cancel, Reset, Apply
            int cancelBtn = controlsX;
            int resetBtn = controlsX + 110;
            int applyBtn = controlsX + 220;

            // Cancel button
            Rectangle cancelRect = new Rectangle(cancelBtn, btnY, 90, 35);
            bool hoverCancel = Raylib.CheckCollisionPointRec(mousePos, cancelRect);
            Raylib.DrawRectangleRounded(cancelRect, 0.2f, 6, hoverCancel ? new Color(140, 80, 80, 255) : new Color(100, 60, 60, 200));
            FontManager.DrawText("Annuler", (int)cancelRect.X + 18, (int)cancelRect.Y + 10, 14, Color.White);
            if (hoverCancel && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                ApplyChanges(_originalSkinColor, _originalHairStyle, _originalBeardStyle, _originalHairColor, _originalEyeStyle, _originalEyeColor);
                Close();
            }

            // Reset button
            Rectangle resetRect = new Rectangle(resetBtn, btnY, 90, 35);
            bool hoverReset = Raylib.CheckCollisionPointRec(mousePos, resetRect);
            Raylib.DrawRectangleRounded(resetRect, 0.2f, 6, hoverReset ? COLOR_RESET_HOVER : COLOR_RESET);
            if (_resetIcon.Id != 0)
                Raylib.DrawTexturePro(_resetIcon, new Rectangle(0, 0, _resetIcon.Width, _resetIcon.Height),
                    new Rectangle(resetRect.X + 8, resetRect.Y + 8, 20, 20), Vector2.Zero, 0, Color.White);
            FontManager.DrawText("Défaut", (int)resetRect.X + 32, (int)resetRect.Y + 10, 14, Color.White);
            if (hoverReset && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _tempSkinColor = new Color(255, 235, 200, 255);
                _tempHairColor = new Color(60, 40, 30, 255);
                _tempEyeColor = new Color(100, 150, 200, 255);
                _tempHairStyle = 0;
                _tempBeardStyle = 0;
                _tempEyeStyle = 0;
                // Sync sliders for active tab
                if (_activeTab == 0) SyncSlidersFromColor(_tempSkinColor);
                else if (_activeTab == 1) SyncSlidersFromColor(_tempHairColor);
                else if (_activeTab == 2) SyncSlidersFromColor(_tempEyeColor);
            }

            // Apply button
            Rectangle applyRect = new Rectangle(applyBtn, btnY, 90, 35);
            bool hoverApply = Raylib.CheckCollisionPointRec(mousePos, applyRect);
            Raylib.DrawRectangleRounded(applyRect, 0.2f, 6, hoverApply ? COLOR_BUTTON_HOVER : COLOR_BUTTON);
            FontManager.DrawText("Appliquer", (int)applyRect.X + 12, (int)applyRect.Y + 10, 14, Color.White);
            if (hoverApply && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                ApplyChanges(_tempSkinColor, _tempHairStyle, _tempBeardStyle, _tempHairColor, _tempEyeStyle, _tempEyeColor);
                Close();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                Close();
        }

        private static void DrawCharacterPreview(Vector2 pos, Color skin, int hairStyle, int beardStyle, Color hairColor, int eyeStyle, Color eyeColor)
        {
            Texture2D hairBase = (hairStyle >= 0 && hairStyle < Program.hairBaseTextures.Count)
                ? Program.hairBaseTextures[hairStyle]
                : new Texture2D();
            Texture2D hairOverlay = (hairStyle >= 0 && hairStyle < Program.hairOverlayTextures.Count)
                ? Program.hairOverlayTextures[hairStyle]
                : new Texture2D();

            Texture2D eyeBase = (eyeStyle >= 0 && eyeStyle < Program.EyeBaseTextures.Count)
                ? Program.EyeBaseTextures[eyeStyle]
                : new Texture2D();
            Texture2D eyeOverlay = (eyeStyle >= 0 && eyeStyle < Program.EyeOverlayTextures.Count)
                ? Program.EyeOverlayTextures[eyeStyle]
                : new Texture2D();

            Equipment dummyEquipment = new Equipment();

            var oldEyeBaseList = Program.EyeBaseTextures;
            var oldEyeOverlayList = Program.EyeOverlayTextures;
            var oldEyeStyle = Program.EyeStyle;
            var oldEyeColor = Program.EyeColor;

            Program.EyeBaseTextures = new List<Texture2D> { eyeBase };
            Program.EyeOverlayTextures = new List<Texture2D> { eyeOverlay };
            Program.EyeStyle = 0;
            Program.EyeColor = eyeColor;

            EntityRenderer.DrawEntity(
                speciesName: "human",
                anim: "idle",
                frame: 0,
                prog: 0f,
                facing: 1f,
                pos: pos,
                tint: skin,
                hBase: hairBase,
                hOverlay: hairOverlay,
                hColor: hairColor,
                skeletons: SpeciesData.Skeletons,
                eyes: eyeBase,
                mouth: Program.MouthTexture,
                customScale: 1.2f,
                equipment: dummyEquipment,
                isCarrying: false,
                inWater: false,
                attackSwingProgress: 0f,
                heldItemTexture: default,
                headAngle: 0f,
                keepItemHorizontal: false,
                isBow: false,
                underwearTexture: Program.LeafUnderpantsTexture,
                beardStyle: beardStyle
            );

            Program.EyeBaseTextures = oldEyeBaseList;
            Program.EyeOverlayTextures = oldEyeOverlayList;
            Program.EyeStyle = oldEyeStyle;
            Program.EyeColor = oldEyeColor;
        }

        private static void ApplyChanges(Color skin, int hairStyle, int beardStyle, Color hairColor, int eyeStyle, Color eyeColor)
        {
            Program.SkinColor = skin;
            Program.PlayerHairStyle = hairStyle;
            Program.PlayerBeardStyle = beardStyle;
            Program.HairColor = hairColor;
            Program.EyeStyle = eyeStyle;
            Program.EyeColor = eyeColor;
        }

        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;
    }
}