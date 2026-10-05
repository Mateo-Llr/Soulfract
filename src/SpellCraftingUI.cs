// SpellCraftingUI.cs - Version corrigée avec assignation des sorts
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static class SpellCraftingUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = new Vector2(300, 200);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isHoveringTitleBar = false;

        private static SpellData _currentSpell = new SpellData
        {
            Element = SpellElement.Fire,
            Type = SpellType.Projectile,
            Power = PowerLevel.Medium
        };

        // Sélection temporaire pour l'affichage
        private static int _selectedElementIndex = 0;
        private static int _selectedTypeIndex = 0;
        private static int _selectedPowerIndex = 1; // Medium par défaut

        private static readonly SpellElement[] _elements = Enum.GetValues<SpellElement>();
        private static readonly SpellType[] _types = Enum.GetValues<SpellType>();
        private static readonly PowerLevel[] _powers = Enum.GetValues<PowerLevel>();

        private const int WINDOW_WIDTH = 480;
        private const int WINDOW_HEIGHT = 480; // Augmenté pour l'assignation
        private const int HEADER_HEIGHT = 45;

        private static readonly Color COLOR_BG = new Color(20, 22, 28, 240);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_BUTTON = new Color(80, 140, 80, 255);
        private static readonly Color COLOR_BUTTON_HOVER = new Color(100, 180, 100, 255);
        private static readonly Color COLOR_SELECTED = new Color(210, 180, 100, 200);
        private static readonly Color COLOR_ITEM_NORMAL = new Color(50, 53, 60, 200);

        public static bool IsOpen => _isOpen;

        public static void Open()
        {
            _isOpen = true;
            _isDragging = false;
            UIManager.PushUI(Close, () => IsOpen);
        }

        public static void Close()
        {
            _isOpen = false;
            _isDragging = false;
        }

        public static void Update()
        {
            if (!_isOpen) return;

            Vector2 mousePos = Raylib.GetMousePosition();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + 100, sw - 100);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, sh - 100);

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;

            // Drag
            Rectangle titleBar = new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT);
            _isHoveringTitleBar = Raylib.CheckCollisionPointRec(mousePos, titleBar);
            if (_isHoveringTitleBar && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDragging = true;
                _dragOffset = mousePos - _windowPos;
            }
            if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                _windowPos = mousePos - _dragOffset;
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDragging = false;

            // Gestion des clics sur les sélecteurs
            int startX = x + 20;
            int startY = y + HEADER_HEIGHT + 30;
            int spacing = 60;
            int itemSize = 36;

            // Éléments (ligne 1)
            for (int i = 0; i < _elements.Length; i++)
            {
                int btnX = startX + i * (itemSize + 8);
                int btnY = startY;
                Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
                if (Raylib.CheckCollisionPointRec(mousePos, rect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _selectedElementIndex = i;
                    _currentSpell.Element = _elements[i];
                }
            }

            // Types (ligne 2)
            int typeStartY = startY + itemSize + 20;
            for (int i = 0; i < _types.Length; i++)
            {
                int btnX = startX + i * (itemSize + 8);
                int btnY = typeStartY;
                Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
                if (Raylib.CheckCollisionPointRec(mousePos, rect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _selectedTypeIndex = i;
                    _currentSpell.Type = _types[i];
                }
            }

            // Puissances (ligne 3)
            int powerStartY = typeStartY + itemSize + 20;
            for (int i = 0; i < _powers.Length; i++)
            {
                int btnX = startX + i * (itemSize + 8);
                int btnY = powerStartY;
                Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
                if (Raylib.CheckCollisionPointRec(mousePos, rect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _selectedPowerIndex = i;
                    _currentSpell.Power = _powers[i];
                }
            }

            // Fermeture
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                Close();
        }

        public static void Draw()
        {
            if (!_isOpen) return;

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();

            // Panneau principal
            Raylib.DrawRectangleRounded(new Rectangle(x + 4, y + 4, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, new Color(0, 0, 0, 100));
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, COLOR_BG);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, 2, COLOR_BORDER);

            // Barre de titre
            Color titleColor = _isHoveringTitleBar ? new Color(80, 70, 50, 200) : COLOR_HEADER;
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, titleColor);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, 1, COLOR_ACCENT);
            FontManager.DrawText(" CRÉATION DE SORT", x + 15, y + 13, 18, Color.White);

            // Bouton fermer
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 35, y + 10, 25, 25);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeBtn.X + 8, (int)closeBtn.Y + 5, 16, hoverClose ? Color.Red : new Color(200, 120, 120, 200));
            if (hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
                Close();

            // Afficher les sélecteurs
            int startX = x + 20;
            int startY = y + HEADER_HEIGHT + 30;
            int spacing = 60;
            int itemSize = 36;

            // Titres des sélecteurs
            FontManager.DrawText("ÉLÉMENT", startX, startY - 20, 12, COLOR_ACCENT);
            FontManager.DrawText("TYPE", startX, startY + itemSize + 20 - 20, 12, COLOR_ACCENT);
            FontManager.DrawText("PUISSANCE", startX, startY + 2 * (itemSize + 20) - 20, 12, COLOR_ACCENT);

            // Éléments
            for (int i = 0; i < _elements.Length; i++)
            {
                int btnX = startX + i * (itemSize + 8);
                int btnY = startY;
                Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
                bool hover = Raylib.CheckCollisionPointRec(mousePos, rect);
                bool selected = (i == _selectedElementIndex);

                Color bgColor = selected ? COLOR_SELECTED : (hover ? COLOR_BUTTON_HOVER : COLOR_ITEM_NORMAL);
                Raylib.DrawRectangleRounded(rect, 0.2f, 6, bgColor);
                Raylib.DrawRectangleRoundedLines(rect, 0.2f, 6, 1, COLOR_BORDER);

                Color elemColor = GetElementColor(_elements[i]);
                Raylib.DrawCircle(btnX + itemSize / 2, btnY + itemSize / 2, itemSize / 3, elemColor);
            }

            // Types
            for (int i = 0; i < _types.Length; i++)
            {
                int btnX = startX + i * (itemSize + 8);
                int btnY = startY + itemSize + 20;
                Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
                bool hover = Raylib.CheckCollisionPointRec(mousePos, rect);
                bool selected = (i == _selectedTypeIndex);

                Color bgColor = selected ? COLOR_SELECTED : (hover ? COLOR_BUTTON_HOVER : COLOR_ITEM_NORMAL);
                Raylib.DrawRectangleRounded(rect, 0.2f, 6, bgColor);
                Raylib.DrawRectangleRoundedLines(rect, 0.2f, 6, 1, COLOR_BORDER);

                string symbol = GetTypeSymbol(_types[i]);
                FontManager.DrawText(symbol, btnX + 8, btnY + 6, 18, Color.White);
            }

            // Puissances
            for (int i = 0; i < _powers.Length; i++)
            {
                int btnX = startX + i * (itemSize + 8);
                int btnY = startY + 2 * (itemSize + 20);
                Rectangle rect = new Rectangle(btnX, btnY, itemSize, itemSize);
                bool hover = Raylib.CheckCollisionPointRec(mousePos, rect);
                bool selected = (i == _selectedPowerIndex);

                Color bgColor = selected ? COLOR_SELECTED : (hover ? COLOR_BUTTON_HOVER : COLOR_ITEM_NORMAL);
                Raylib.DrawRectangleRounded(rect, 0.2f, 6, bgColor);
                Raylib.DrawRectangleRoundedLines(rect, 0.2f, 6, 1, COLOR_BORDER);

                string label = _powers[i].ToString()[0].ToString();
                FontManager.DrawText(label, btnX + 14, btnY + 8, 16, Color.White);
            }

            // ---- SECTION ASSIGNATION (placée en dessous des sélecteurs) ----
            int assignY = startY + 3 * (itemSize + 20) + 20;
            FontManager.DrawText("ASSIGNATION", startX, assignY, 12, COLOR_ACCENT);

            int leftX = startX;
            int rightX = startX + 220;
            int previewSize = 60;
            int previewY = assignY + 20;

            // Sort gauche
            Raylib.DrawRectangleRounded(new Rectangle(leftX, previewY, previewSize, previewSize), 0.1f, 6, new Color(50, 53, 60, 200));
            Raylib.DrawRectangleRoundedLines(new Rectangle(leftX, previewY, previewSize, previewSize), 0.1f, 6, 1, COLOR_BORDER);
            FontManager.DrawText("G", leftX + previewSize/2 - 8, previewY + 10, 14, Color.White);
            FontManager.DrawText("Clic gauche", leftX + previewSize/2 - 30, previewY + previewSize - 12, 10, new Color(150, 150, 140, 180));

            Color leftColor = Program.GetLeftClickSpell().GetElementColor();
            Raylib.DrawCircle(leftX + previewSize/2, previewY + previewSize/2 - 5, 12, leftColor);
            string leftName = Program.GetLeftClickSpell().GetDisplayName();
            int leftNameW = FontManager.MeasureText(leftName, 10);
            FontManager.DrawText(leftName, leftX + (previewSize - leftNameW)/2, previewY + previewSize/2 + 8, 10, Color.White);

            Rectangle assignLeftBtn = new Rectangle(leftX, previewY + previewSize + 5, previewSize, 20);
            bool hoverAssignLeft = Raylib.CheckCollisionPointRec(mousePos, assignLeftBtn);
            Raylib.DrawRectangleRounded(assignLeftBtn, 0.2f, 6, hoverAssignLeft ? new Color(80, 140, 80, 255) : new Color(60, 120, 60, 200));
            FontManager.DrawText("← Assigner", (int)assignLeftBtn.X + 6, (int)assignLeftBtn.Y + 4, 10, Color.White);
            if (hoverAssignLeft && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Program.SetLeftClickSpell(_currentSpell);
                Program.AddNotification(new Notification($"Sort assigné au clic gauche : {_currentSpell.GetDisplayName()}", _currentSpell.GetElementColor(), 2f));
            }

            // Sort droit
            Raylib.DrawRectangleRounded(new Rectangle(rightX, previewY, previewSize, previewSize), 0.1f, 6, new Color(50, 53, 60, 200));
            Raylib.DrawRectangleRoundedLines(new Rectangle(rightX, previewY, previewSize, previewSize), 0.1f, 6, 1, COLOR_BORDER);
            FontManager.DrawText("D", rightX + previewSize/2 - 8, previewY + 10, 14, Color.White);
            FontManager.DrawText("Clic droit", rightX + previewSize/2 - 30, previewY + previewSize - 12, 10, new Color(150, 150, 140, 180));

            Color rightColor = Program.GetRightClickSpell().GetElementColor();
            Raylib.DrawCircle(rightX + previewSize/2, previewY + previewSize/2 - 5, 12, rightColor);
            string rightName = Program.GetRightClickSpell().GetDisplayName();
            int rightNameW = FontManager.MeasureText(rightName, 10);
            FontManager.DrawText(rightName, rightX + (previewSize - rightNameW)/2, previewY + previewSize/2 + 8, 10, Color.White);

            Rectangle assignRightBtn = new Rectangle(rightX, previewY + previewSize + 5, previewSize, 20);
            bool hoverAssignRight = Raylib.CheckCollisionPointRec(mousePos, assignRightBtn);
            Raylib.DrawRectangleRounded(assignRightBtn, 0.2f, 6, hoverAssignRight ? new Color(80, 140, 80, 255) : new Color(60, 120, 60, 200));
            FontManager.DrawText("Assigner →", (int)assignRightBtn.X + 6, (int)assignRightBtn.Y + 4, 10, Color.White);
            if (hoverAssignRight && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Program.SetRightClickSpell(_currentSpell);
                Program.AddNotification(new Notification($"Sort assigné au clic droit : {_currentSpell.GetDisplayName()}", _currentSpell.GetElementColor(), 2f));
            }

            // Aperçu du sort en cours (en dessous de l'assignation ou à côté)
            int previewNameY = assignY + previewSize + 40;
            string spellName = _currentSpell.GetDisplayName();
            int nameWidth = FontManager.MeasureText(spellName, 16);
            FontManager.DrawText(spellName, x + (WINDOW_WIDTH - nameWidth) / 2, previewNameY, 16, _currentSpell.GetElementColor());

            string hint = "ESC : Fermer | J : Ouvrir/fermer";
            int hintW = FontManager.MeasureText(hint, 11);
            FontManager.DrawText(hint, x + (WINDOW_WIDTH - hintW) / 2, y + WINDOW_HEIGHT - 20, 11, new Color(120, 110, 90, 180));
        }

        private static Color GetElementColor(SpellElement elem)
        {
            return elem switch
            {
                SpellElement.Fire => new Color(255, 120, 20, 255),
                SpellElement.Water => new Color(60, 120, 255, 255),
                SpellElement.Earth => new Color(140, 100, 60, 255),
                SpellElement.Air => new Color(200, 230, 255, 255),
                SpellElement.Ice => new Color(180, 240, 255, 255),
                SpellElement.Lightning => new Color(255, 220, 50, 255),
                SpellElement.Poison => new Color(100, 200, 80, 255),
                SpellElement.Light => new Color(255, 255, 200, 255),
                SpellElement.Dark => new Color(100, 80, 180, 255),
                SpellElement.Metal => new Color(180, 180, 200, 255),
                _ => Color.White
            };
        }

        private static string GetTypeSymbol(SpellType type)
        {
            return type switch
            {
                SpellType.Projectile => "",
                SpellType.Homing => "",
                SpellType.Area => "",
                SpellType.Rain => "",
                SpellType.Lightning => "",
                SpellType.Shield => "",
                SpellType.Summon => "",
                SpellType.Burst => "",
                _ => "?"
            };
        }
    }
}