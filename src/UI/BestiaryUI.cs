// BestiaryUI.cs - Codex de bestiaire de Soulfract
#nullable enable

using Raylib_cs;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;

namespace Soulfract
{
    public static class BestiaryUI
    {
        private static bool _isOpen;
        private static Vector2 _windowPos = UIManager.ScalePosition(160, 70);
        private static Vector2 _dragOffset;
        private static bool _isDragging;
        private static bool _isDraggingScrollbar;
        private static int _selectedIndex = -1;
        private static int _scrollOffset;
        private static int _detailScrollOffset;
        private static int _categoryScrollOffset;
        private static string _searchFilter = "";
        private static string _selectedCategory = "Tout";
        private static bool _isSearching;

        private static readonly List<SpeciesEntry> _speciesEntries = new();
        private static readonly Dictionary<string, Texture2D> _uiTextures = new();

        // Mise en page
        private static int WINDOW_WIDTH => UIManager.ScaleInt(860);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(620);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(48);
        private const int GRID_COLUMNS = 4;
        private static int ITEM_WIDTH => UIManager.ScaleInt(82);
        private static int ITEM_HEIGHT => UIManager.ScaleInt(96);
        private static int ITEM_PADDING => UIManager.ScaleInt(8);
        private static int CLOSE_BUTTON_SIZE => UIManager.ScaleInt(26);
        private static int GRID_WIDTH => UIManager.ScaleInt(390);
        private static int GRID_TOP_OFFSET => UIManager.ScaleInt(88);

        // Palette du codex : charbon, ivoire et or vieilli.
        private static readonly Color COLOR_BG = new(20, 22, 29, 250);
        private static readonly Color COLOR_BORDER = new(111, 91, 59, 230);
        private static readonly Color COLOR_ACCENT = new(232, 197, 120, 255);
        private static readonly Color COLOR_ITEM_NORMAL = new(37, 40, 49, 255);
        private static readonly Color COLOR_ITEM_HOVER = new(61, 62, 72, 255);
        private static readonly Color COLOR_ITEM_SELECTED = new(110, 85, 43, 180);
        private static readonly Color COLOR_INFO_BG = new(29, 32, 41, 245);
        private static readonly Color COLOR_SEARCH_BG = new(30, 33, 42, 255);
        private static readonly Color COLOR_SCROLL_TRACK = new(24, 26, 32, 230);
        private static readonly Color COLOR_SCROLL_THUMB = new(184, 147, 79, 230);
        private static readonly Color COLOR_SCROLL_THUMB_HOVER = new(239, 205, 133, 255);
        private static readonly Color COLOR_TEXT = new(55, 43, 31, 255);
        private static readonly Color COLOR_TEXT_LIGHT = new(76, 61, 45, 255);
        private static readonly Color COLOR_MUTED = new(91, 73, 54, 255);
        private static readonly Color COLOR_PANEL_INNER = new(24, 27, 35, 230);
        private static readonly Color COLOR_DANGER = new(145, 61, 58, 255);
        // Encre sombre réservée au contenu posé directement sur la page du livre.
        private static readonly Color COLOR_INK = new(55, 43, 31, 255);
        private static readonly Color COLOR_INK_MUTED = new(91, 73, 54, 255);

        public static bool IsOpen => _isOpen;
        public static bool IsTextInputFocused => _isSearching;
        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;

        private sealed class SpeciesEntry
        {
            public string Key { get; set; } = "";
            public SpeciesData.SpeciesInfo Info { get; set; } = new();
            public Color Tint { get; set; } = Color.White;
            public Dictionary<string, int> FeatureVariants { get; } = new();
            public Dictionary<string, Color> FeatureColors { get; } = new();
            public string DisplayName => string.IsNullOrWhiteSpace(Info.Name) ? Key : Info.Name;
            public string Category => string.IsNullOrWhiteSpace(Info.Type) ? "Autre" : Info.Type;
        }

        public static void Open()
        {
            if (_isOpen) return;

            _isOpen = true;
            UIManager.PushUI(Close, () => IsOpen);
            BuildSpeciesList();

            _selectedIndex = -1;
            _scrollOffset = 0;
            _detailScrollOffset = 0;
            _categoryScrollOffset = 0;
            _searchFilter = "";
            _selectedCategory = "Tout";
            _isSearching = false;
            _isDragging = false;
            _isDraggingScrollbar = false;
            TextInput.Reset("bestiary-search");
        }

        public static void Close()
        {
            _isOpen = false;
            _isDragging = false;
            _isDraggingScrollbar = false;
            _isSearching = false;
            _detailScrollOffset = 0;
            TextInput.Reset("bestiary-search");
        }

        private static void BuildSpeciesList()
        {
            _speciesEntries.Clear();

            foreach (var kv in SpeciesData.Species)
            {
                var entry = new SpeciesEntry
                {
                    Key = kv.Key,
                    Info = kv.Value
                };
                if (!string.Equals(entry.Key, "human", StringComparison.OrdinalIgnoreCase))
                {
                    Entity.GenerateRandomAppearance(
                        entry.Key,
                        Program.GetPlayerPosition(),
                        out Color tint,
                        entry.FeatureVariants,
                        entry.FeatureColors);
                    entry.Tint = tint;
                }
                _speciesEntries.Add(entry);
            }

            _speciesEntries.Sort((a, b) =>
                string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        public static void Update()
        {
            if (!_isOpen) return;

            Vector2 mouse = Raylib.GetMousePosition();
            int screenW = Raylib.GetScreenWidth();
            int screenH = Raylib.GetScreenHeight();

            int visibleMargin = UIManager.ScaleInt(70);
            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + visibleMargin, screenW - visibleMargin);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, Math.Max(0, screenH - visibleMargin));

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            List<SpeciesEntry> filtered = GetFilteredEntries();

            Rectangle closeRect = GetCloseButtonRect(x, y);
            Rectangle searchRect = GetSearchRect(x, y);
            Rectangle leftCategoryArrow = GetCategoryArrowRect(x, y, false);
            Rectangle rightCategoryArrow = GetCategoryArrowRect(x, y, true);
            Rectangle categoryArea = GetCategoryAreaRect(x, y);
            Rectangle gridRect = GetGridRect(x, y);
            Rectangle detailRect = GetDetailPanelRect(x, y);

            bool clickOnInteractive = false;

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                Raylib.CheckCollisionPointRec(mouse, closeRect))
            {
                Close();
                return;
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                Raylib.CheckCollisionPointRec(mouse, searchRect))
            {
                _isSearching = true;
                clickOnInteractive = true;
            }
            else if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                     !Raylib.CheckCollisionPointRec(mouse, searchRect))
            {
                if (_isSearching)
                {
                    _isSearching = false;
                    TextInput.Reset("bestiary-search");
                }
            }

            List<string> categories = GetCategoryOptions();
            int visibleCategoryCount = GetVisibleCategoryCount();
            int maxCategoryStart = Math.Max(0, categories.Count - visibleCategoryCount);
            _categoryScrollOffset = Math.Clamp(_categoryScrollOffset, 0, maxCategoryStart);

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                Raylib.CheckCollisionPointRec(mouse, leftCategoryArrow))
            {
                _categoryScrollOffset = Math.Max(0, _categoryScrollOffset - 1);
                clickOnInteractive = true;
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                Raylib.CheckCollisionPointRec(mouse, rightCategoryArrow))
            {
                _categoryScrollOffset = Math.Min(maxCategoryStart, _categoryScrollOffset + 1);
                clickOnInteractive = true;
            }

            for (int i = 0; i < visibleCategoryCount; i++)
            {
                int categoryIndex = _categoryScrollOffset + i;
                if (categoryIndex >= categories.Count) break;

                Rectangle categoryRect = GetCategoryTabRect(i, x, y);
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                    Raylib.CheckCollisionPointRec(mouse, categoryRect))
                {
                    _selectedCategory = categories[categoryIndex];
                    _scrollOffset = 0;
                    _selectedIndex = -1;
                    _detailScrollOffset = 0;
                    clickOnInteractive = true;
                }
            }

            // Hauteur du contenu de la galerie et bornes de défilement.
            int rows = (int)Math.Ceiling(filtered.Count / (double)GRID_COLUMNS);
            int totalGridHeight = rows * (ITEM_HEIGHT + ITEM_PADDING) + ITEM_PADDING;
            int maxGridScroll = Math.Max(0, totalGridHeight - (int)gridRect.Height);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxGridScroll);

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                Raylib.CheckCollisionPointRec(mouse, gridRect))
            {
                bool foundItem = false;
                for (int index = 0; index < filtered.Count; index++)
                {
                    int row = index / GRID_COLUMNS;
                    int col = index % GRID_COLUMNS;
                    int itemX = (int)gridRect.X + ITEM_PADDING + col * (ITEM_WIDTH + ITEM_PADDING);
                    int itemY = (int)gridRect.Y + ITEM_PADDING + row * (ITEM_HEIGHT + ITEM_PADDING) - _scrollOffset;
                    Rectangle itemRect = new(itemX, itemY, ITEM_WIDTH, ITEM_HEIGHT);

                    // Les cases partiellement visibles ne sont ni dessinées ni cliquables.
                    if (itemY < gridRect.Y || itemY + ITEM_HEIGHT > gridRect.Y + gridRect.Height)
                        continue;

                    if (Raylib.CheckCollisionPointRec(mouse, itemRect))
                    {
                        if (_selectedIndex != index) _detailScrollOffset = 0;
                        _selectedIndex = index;
                        foundItem = true;
                        clickOnInteractive = true;
                        break;
                    }
                }

                if (!foundItem)
                {
                    _selectedIndex = -1;
                    _detailScrollOffset = 0;
                }
            }

            // Une seule lecture de la molette par frame, appliquée à la zone survolée.
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0)
            {
                if (Raylib.CheckCollisionPointRec(mouse, gridRect))
                {
                    _scrollOffset -= (int)(wheel * (ITEM_HEIGHT + ITEM_PADDING) * 2);
                    _scrollOffset = Math.Clamp(_scrollOffset, 0, maxGridScroll);
                }
                else if (_selectedIndex >= 0 && _selectedIndex < filtered.Count &&
                         Raylib.CheckCollisionPointRec(mouse, detailRect))
                {
                    int contentHeight = GetDetailContentHeight(filtered[_selectedIndex], (int)detailRect.Width);
                    int viewportHeight = Math.Max(0, (int)detailRect.Height - UIManager.ScaleInt(24));
                    _detailScrollOffset -= (int)(wheel * UIManager.ScaleInt(38));
                    _detailScrollOffset = Math.Clamp(
                        _detailScrollOffset, 0, Math.Max(0, contentHeight - viewportHeight));
                }
            }

            // Barre de défilement de la galerie.
            Rectangle scrollbarTrack;
            Rectangle scrollbarThumb;
            GetGridScrollbarRects(gridRect, totalGridHeight, _scrollOffset,
                out scrollbarTrack, out scrollbarThumb);

            if (totalGridHeight > gridRect.Height &&
                Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                Raylib.CheckCollisionPointRec(mouse, scrollbarThumb))
            {
                _isDraggingScrollbar = true;
                clickOnInteractive = true;
            }

            if (_isDraggingScrollbar && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                float range = scrollbarTrack.Height - scrollbarThumb.Height;
                if (range > 0)
                {
                    float relativeY = (mouse.Y - scrollbarTrack.Y - scrollbarThumb.Height / 2f) / range;
                    relativeY = Math.Clamp(relativeY, 0f, 1f);
                    _scrollOffset = (int)(relativeY * maxGridScroll);
                }
            }

            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDraggingScrollbar = false;

            // La fenêtre peut être déplacée depuis toute sa surface, sauf les contrôles.
            Rectangle windowRect = new(x, y, WINDOW_WIDTH, WINDOW_HEIGHT);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                Raylib.CheckCollisionPointRec(mouse, windowRect) &&
                !clickOnInteractive && !_isDragging && !_isDraggingScrollbar)
            {
                _isDragging = true;
                _dragOffset = mouse - _windowPos;
            }

            if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                _windowPos = mouse - _dragOffset;

            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDragging = false;

            // Saisie de recherche.
            if (_isSearching)
            {
                string previousSearch = _searchFilter;
                TextInput.Update(ref _searchFilter, "bestiary-search", searchRect, 60);

                if (!string.Equals(previousSearch, _searchFilter, StringComparison.Ordinal))
                {
                    _selectedIndex = -1;
                    _scrollOffset = 0;
                    _detailScrollOffset = 0;
                }

                if (Raylib.IsKeyPressed(KeyboardKey.Enter))
                {
                    _isSearching = false;
                    TextInput.Reset("bestiary-search");
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                if (_isSearching)
                {
                    _isSearching = false;
                    TextInput.Reset("bestiary-search");
                }
                else
                {
                    Close();
                }
            }
        }

        public static void Draw()
        {
            if (!_isOpen) return;

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            Vector2 mouse = Raylib.GetMousePosition();

            Rectangle panelRect = new(x, y, WINDOW_WIDTH, WINDOW_HEIGHT);
            Rectangle shadowRect = new(x + UIManager.ScaleInt(5), y + UIManager.ScaleInt(6),
                WINDOW_WIDTH, WINDOW_HEIGHT);

            Raylib.DrawRectangleRounded(shadowRect, 0.055f, 12, new Color(0, 0, 0, 125));

            if (UIManager.BookPanelTexture != null && UIManager.BookPanelTexture.IsValid)
                UIManager.BookPanelTexture.DrawSplit(panelRect, Color.White);
            else if (UIManager.InventoryPanel != null && UIManager.InventoryPanel.IsValid)
                UIManager.InventoryPanel.Draw(panelRect, Color.White);
            else
            {
                Raylib.DrawRectangleRounded(panelRect, 0.055f, 12, COLOR_BG);
                Raylib.DrawRectangleRoundedLines(panelRect, 0.055f, 12, 2, COLOR_BORDER);
            }

            DrawCloseButton(x, y, mouse);
            DrawSearchAndCategories(x, y, mouse);

            List<SpeciesEntry> filtered = GetFilteredEntries();
            Rectangle gridRect = GetGridRect(x, y);
            DrawSpeciesGrid(filtered, gridRect, mouse);

            if (_selectedIndex >= 0 && _selectedIndex < filtered.Count)
            {
                Rectangle detailRect = GetDetailPanelRect(x, y);
                DrawSelectedSpeciesDetails(filtered[_selectedIndex], detailRect);
            }
            else
            {
                Rectangle detailRect = GetDetailPanelRect(x, y);
                DrawEmptyDetails(detailRect, filtered.Count == 0);
            }
        }

        private static void DrawCloseButton(int x, int y, Vector2 mouse)
        {
            Rectangle closeRect = GetCloseButtonRect(x, y);
            bool hover = Raylib.CheckCollisionPointRec(mouse, closeRect);
            Raylib.DrawRectangleRounded(closeRect, 0.2f, 6,
                hover ? COLOR_DANGER : new Color(74, 42, 43, 235));
            Raylib.DrawRectangleRoundedLines(closeRect, 0.2f, 6, 1, COLOR_BORDER);
            int closeFont = UIManager.ScaleInt(15);
            int closeTextWidth = FontManager.MeasureText("X", closeFont);
            FontManager.DrawText("X",
                (int)(closeRect.X + (closeRect.Width - closeTextWidth) / 2f),
                (int)(closeRect.Y + (closeRect.Height - closeFont) / 2f),
                closeFont, COLOR_TEXT);
        }

        private static void DrawSearchAndCategories(int x, int y, Vector2 mouse)
        {
            Rectangle searchRect = GetSearchRect(x, y);
            DrawSearchBar(searchRect);

            TextInput.DrawSingleLine(
                _searchFilter, "bestiary-search", searchRect,
                UIManager.ScaleInt(13), COLOR_TEXT,
                new Color(90, 110, 150, 220), COLOR_ACCENT, 8,
                placeholder: "Rechercher une espèce...");

            int labelSize = UIManager.ScaleInt(9);
            FontManager.DrawText("FAMILLES",
                (int)searchRect.X, (int)searchRect.Y + (int)searchRect.Height + UIManager.ScaleInt(5),
                labelSize, COLOR_MUTED);

            Rectangle leftArrow = GetCategoryArrowRect(x, y, false);
            Rectangle rightArrow = GetCategoryArrowRect(x, y, true);
            DrawCategoryArrow(leftArrow, "<", mouse, _categoryScrollOffset > 0);

            List<string> categories = GetCategoryOptions();
            int visibleCount = GetVisibleCategoryCount();
            int maxStart = Math.Max(0, categories.Count - visibleCount);
            _categoryScrollOffset = Math.Clamp(_categoryScrollOffset, 0, maxStart);

            for (int i = 0; i < visibleCount; i++)
            {
                int categoryIndex = _categoryScrollOffset + i;
                if (categoryIndex >= categories.Count) break;

                string category = categories[categoryIndex];
                Rectangle rect = GetCategoryTabRect(i, x, y);
                bool selected = string.Equals(category, _selectedCategory, StringComparison.OrdinalIgnoreCase);
                bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);

                Color bg = selected ? new Color(126, 96, 48, 255)
                    : hovered ? COLOR_ITEM_HOVER : new Color(37, 40, 49, 235);

                Raylib.DrawRectangleRounded(rect, 0.18f, 6, bg);
                Raylib.DrawRectangleRoundedLines(rect, 0.18f, 6, selected ? 2 : 1,
                    selected ? COLOR_ACCENT : COLOR_BORDER);

                string label = GetCategoryShortLabel(category);
                int fontSize = UIManager.ScaleInt(10);
                label = FitText(label, (int)rect.Width - UIManager.ScaleInt(8), fontSize);
                int textWidth = FontManager.MeasureText(label, fontSize);
                FontManager.DrawText(label,
                    (int)(rect.X + (rect.Width - textWidth) / 2f),
                    (int)(rect.Y + (rect.Height - fontSize) / 2f),
                    fontSize, selected ? COLOR_TEXT : COLOR_TEXT_LIGHT);
            }

            DrawCategoryArrow(rightArrow, ">", mouse, _categoryScrollOffset < maxStart);
        }

        private static void DrawCategoryArrow(Rectangle rect, string label, Vector2 mouse, bool enabled)
        {
            bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);
            Color bg = !enabled ? new Color(29, 31, 38, 150)
                : hovered ? COLOR_ITEM_HOVER : COLOR_ITEM_NORMAL;

            Raylib.DrawRectangleRounded(rect, 0.18f, 5, bg);
            Raylib.DrawRectangleRoundedLines(rect, 0.18f, 5, 1, COLOR_BORDER);

            int fontSize = UIManager.ScaleInt(12);
            int width = FontManager.MeasureText(label, fontSize);
            FontManager.DrawText(label,
                (int)(rect.X + (rect.Width - width) / 2f),
                (int)(rect.Y + (rect.Height - fontSize) / 2f),
                fontSize, enabled ? COLOR_ACCENT : COLOR_MUTED);
        }

        private static void DrawSpeciesGrid(List<SpeciesEntry> filtered, Rectangle gridRect, Vector2 mouse)
        {
            int rows = (int)Math.Ceiling(filtered.Count / (double)GRID_COLUMNS);
            int totalHeight = rows * (ITEM_HEIGHT + ITEM_PADDING) + ITEM_PADDING;
            int rowStep = ITEM_HEIGHT + ITEM_PADDING;
            int maxScroll = Math.Max(0, totalHeight - (int)gridRect.Height);
            // Défilement par rangées entières : aucune rangée ne reste coupée.
            int maxRowScroll = (int)Math.Ceiling(maxScroll / (double)rowStep) * rowStep;
            _scrollOffset = Math.Clamp((int)Math.Round(_scrollOffset / (double)rowStep) * rowStep, 0, maxRowScroll);

            int fontSize = UIManager.ScaleInt(10);
            string countLabel = $"{filtered.Count} résultat{(filtered.Count == 1 ? "" : "s")}";
            FontManager.DrawText(countLabel,
                (int)gridRect.X + UIManager.ScaleInt(7),
                (int)gridRect.Y - UIManager.ScaleInt(16),
                fontSize, COLOR_MUTED);

            Raylib.BeginScissorMode((int)gridRect.X, (int)gridRect.Y,
                (int)gridRect.Width, (int)gridRect.Height);

            int firstRow = Math.Max(0, _scrollOffset / (ITEM_HEIGHT + ITEM_PADDING));
            int visibleRows = (int)Math.Ceiling(gridRect.Height / (ITEM_HEIGHT + ITEM_PADDING)) + 2;

            for (int row = firstRow; row < Math.Min(rows, firstRow + visibleRows); row++)
            {
                for (int col = 0; col < GRID_COLUMNS; col++)
                {
                    int index = row * GRID_COLUMNS + col;
                    if (index >= filtered.Count) break;

                    SpeciesEntry entry = filtered[index];
                    int itemX = (int)gridRect.X + ITEM_PADDING + col * (ITEM_WIDTH + ITEM_PADDING);
                    int itemY = (int)gridRect.Y + ITEM_PADDING + row * (ITEM_HEIGHT + ITEM_PADDING) - _scrollOffset;
                    Rectangle itemRect = new(itemX, itemY, ITEM_WIDTH, ITEM_HEIGHT);

                    // Masquer toute case qui dépasse en haut ou en bas de la galerie.
                    if (itemY < gridRect.Y || itemY + ITEM_HEIGHT > gridRect.Y + gridRect.Height)
                        continue;

                    bool hovered = Raylib.CheckCollisionPointRec(mouse, itemRect);
                    bool selected = index == _selectedIndex;

                    // Une case par créature : cadre de slot, sans nom ni ombre portée.
                    // Le scissor garde strictement le portrait à l'intérieur de sa case.
                    DrawItemSlot(itemRect, selected, hovered);
                    int inset = UIManager.ScaleInt(7);
                    Rectangle portraitRect = new(
                        itemX + inset,
                        itemY + inset,
                        ITEM_WIDTH - inset * 2,
                        ITEM_HEIGHT - inset * 2);

                    Raylib.BeginScissorMode(
                        (int)portraitRect.X, (int)portraitRect.Y,
                        (int)portraitRect.Width, (int)portraitRect.Height);
                    DrawSpeciesHeadPreview(entry, portraitRect);
                    Raylib.EndScissorMode();

                }
            }

            Raylib.EndScissorMode();

            if (totalHeight > gridRect.Height)
            {
                Rectangle track, thumb;
                GetGridScrollbarRects(gridRect, totalHeight, _scrollOffset, out track, out thumb);
                Raylib.DrawRectangleRounded(track, 0.4f, 5, COLOR_SCROLL_TRACK);

                bool hovered = Raylib.CheckCollisionPointRec(mouse, thumb);
                Color thumbColor = hovered || _isDraggingScrollbar
                    ? COLOR_SCROLL_THUMB_HOVER : COLOR_SCROLL_THUMB;
                Raylib.DrawRectangleRounded(thumb, 0.4f, 5, thumbColor);
            }
        }

        private static void DrawEmptyDetails(Rectangle rect, bool noResults)
        {
            int centerX = (int)(rect.X + rect.Width / 2f);
            int centerY = (int)(rect.Y + rect.Height / 2f);

            string title = noResults ? "Aucune créature trouvée" : "Choisissez une créature";
            string subtitle = noResults
                ? "Essayez un autre nom ou une autre famille."
                : "Sélectionnez une fiche dans la galerie pour consulter ses données.";

            int titleSize = UIManager.ScaleInt(17);
            int subtitleSize = UIManager.ScaleInt(11);
            int titleWidth = FontManager.MeasureText(title, titleSize);
            int subtitleWidth = FontManager.MeasureText(subtitle, subtitleSize);

            Raylib.DrawCircle(centerX, centerY - UIManager.ScaleInt(38),
                UIManager.ScaleInt(25), new Color(45, 42, 35, 150));
            Raylib.DrawCircleLines(centerX, centerY - UIManager.ScaleInt(38),
                UIManager.ScaleInt(25), COLOR_BORDER);

            FontManager.DrawText(noResults ? "?" : "✦",
                centerX - UIManager.ScaleInt(7), centerY - UIManager.ScaleInt(49),
                UIManager.ScaleInt(20), COLOR_ACCENT);

            FontManager.DrawText(title, centerX - titleWidth / 2,
                centerY + UIManager.ScaleInt(2), titleSize, COLOR_ACCENT);

            FontManager.DrawText(FitText(subtitle, (int)rect.Width - UIManager.ScaleInt(36), subtitleSize),
                centerX - Math.Min(subtitleWidth, (int)rect.Width - UIManager.ScaleInt(36)) / 2,
                centerY + UIManager.ScaleInt(28), subtitleSize, COLOR_INK_MUTED);
        }

        private static void DrawSelectedSpeciesDetails(SpeciesEntry entry, Rectangle detailRect)
        {
            int padding = UIManager.ScaleInt(18);
            int viewportHeight = Math.Max(0, (int)detailRect.Height - padding * 2);
            int contentWidth = Math.Max(1, (int)detailRect.Width - padding * 2);
            int contentHeight = GetDetailContentHeight(entry, (int)detailRect.Width);
            int maxScroll = Math.Max(0, contentHeight - viewportHeight);
            _detailScrollOffset = Math.Clamp(_detailScrollOffset, 0, maxScroll);

            int contentX = (int)detailRect.X + padding;
            int contentY = (int)detailRect.Y + padding - _detailScrollOffset;
            Raylib.BeginScissorMode(contentX, (int)detailRect.Y + padding, contentWidth, viewportHeight);

            // Illustration seule, sans cadre ni case : elle occupe le haut de la page.
            int portraitHeight = UIManager.ScaleInt(176);
            Rectangle portraitRect = new(contentX, contentY, contentWidth, portraitHeight);
            DrawSpeciesPreview(entry, portraitRect, 1.35f);

            int nameSize = UIManager.ScaleInt(22);
            string name = FitText(entry.DisplayName, contentWidth, nameSize);
            int nameWidth = FontManager.MeasureText(name, nameSize);
            int nameY = contentY + portraitHeight + UIManager.ScaleInt(1);
            FontManager.DrawText(name, contentX + (contentWidth - nameWidth) / 2, nameY,
                nameSize, COLOR_INK);

            int categorySize = UIManager.ScaleInt(11);
            string category = GetCategoryShortLabel(entry.Category).ToUpperInvariant();
            int categoryWidth = FontManager.MeasureText(category, categorySize);
            int categoryY = nameY + UIManager.ScaleInt(27);
            FontManager.DrawText(category, contentX + (contentWidth - categoryWidth) / 2,
                categoryY, categorySize, COLOR_INK_MUTED);

            int statsY = categoryY + UIManager.ScaleInt(24);
            Raylib.DrawLine(contentX + UIManager.ScaleInt(8), statsY,
                contentX + contentWidth - UIManager.ScaleInt(8), statsY,
                new Color(COLOR_BORDER.R, COLOR_BORDER.G, COLOR_BORDER.B, (byte)170));
            statsY += UIManager.ScaleInt(12);

            int half = contentWidth / 2;
            DrawPlainStat(contentX, statsY, half, "POINTS DE VIE", entry.Info.MaxHp.ToString());
            DrawPlainStat(contentX + half, statsY, contentWidth - half, "ATTAQUE", entry.Info.Attack.ToString());
            statsY += UIManager.ScaleInt(34);
            DrawPlainStat(contentX, statsY, half, "VITESSE", $"{entry.Info.Speed:F1}");
            DrawPlainStat(contentX + half, statsY, contentWidth - half, "COURSE", $"{entry.Info.RunSpeed:F1}");
            statsY += UIManager.ScaleInt(34);
            DrawPlainStat(contentX, statsY, half, "VISION", $"{entry.Info.VisionRange} tuiles");
            DrawPlainStat(contentX + half, statsY, contentWidth - half, "TAILLE", $"{(entry.Info.Scale > 0f ? entry.Info.Scale : 1f):F1}x");
            statsY += UIManager.ScaleInt(34);
            DrawPlainStat(contentX, statsY, half, "MONTABLE", entry.Info.Mountable ? "Oui" : "Non");
            DrawPlainStat(contentX + half, statsY, contentWidth - half, "APPRIVOISABLE", entry.Info.PreferredFoodId != 0 ? "Oui" : "Non");
            statsY += UIManager.ScaleInt(38);

            string behavior = string.IsNullOrWhiteSpace(entry.Info.Behavior)
                ? "Comportement inconnu" : entry.Info.Behavior!;
            DrawSectionHeading("COMPORTEMENT", contentX, statsY);
            statsY += UIManager.ScaleInt(21);
            int behaviorSize = UIManager.ScaleInt(12);
            int behaviorLines = CountWrappedLines(behavior, contentWidth, behaviorSize, 5);
            DrawWrappedText(behavior, contentX, statsY, contentWidth, behaviorSize, COLOR_INK_MUTED, 5);
            statsY += behaviorLines * UIManager.ScaleInt(16) + UIManager.ScaleInt(12);

            DrawSectionHeading("BUTINS", contentX, statsY);
            statsY += UIManager.ScaleInt(22);
            if (entry.Info.Drops.Count == 0)
            {
                FontManager.DrawText("Aucun butin connu", contentX, statsY,
                    UIManager.ScaleInt(11), COLOR_INK_MUTED);
            }
            else
            {
                int dropIndex = 0;
                foreach (var drop in entry.Info.Drops)
                {
                    string quantity = drop.Value.MinQty == drop.Value.MaxQty
                        ? drop.Value.MinQty.ToString()
                        : $"{drop.Value.MinQty}–{drop.Value.MaxQty}";
                    string itemName = int.TryParse(drop.Key, out int itemId) &&
                                      GameData.ItemDatabase.TryGetValue(itemId, out var itemData)
                        ? itemData.Name : $"Objet {drop.Key}";
                    int textSize = UIManager.ScaleInt(11);
                    int quantityWidth = FontManager.MeasureText("x" + quantity, textSize);
                    FontManager.DrawText(FitText(itemName, contentWidth - quantityWidth - UIManager.ScaleInt(12), textSize),
                        contentX, statsY, textSize, COLOR_INK_MUTED);
                    FontManager.DrawText("x" + quantity,
                        contentX + contentWidth - quantityWidth, statsY, textSize, COLOR_INK);
                    statsY += UIManager.ScaleInt(21);
                    dropIndex++;
                }
            }
            Raylib.EndScissorMode();

            if (contentHeight > viewportHeight && viewportHeight > 0)
            {
                int trackWidth = UIManager.ScaleInt(4);
                int trackX = (int)detailRect.X + (int)detailRect.Width - UIManager.ScaleInt(6) - trackWidth;
                int trackY = (int)detailRect.Y + padding;
                float thumbHeight = Math.Max(UIManager.ScaleInt(22), (float)viewportHeight * viewportHeight / contentHeight);
                thumbHeight = Math.Min(thumbHeight, viewportHeight);
                float thumbY = trackY + (_detailScrollOffset / (float)Math.Max(1, maxScroll)) * (viewportHeight - thumbHeight);
                Raylib.DrawRectangleRounded(new Rectangle(trackX, trackY, trackWidth, viewportHeight), 0.4f, 4, COLOR_SCROLL_TRACK);
                Raylib.DrawRectangleRounded(new Rectangle(trackX, thumbY, trackWidth, thumbHeight), 0.4f, 4, COLOR_SCROLL_THUMB);
            }
        }

        private static void DrawPlainStat(int x, int y, int width, string label, string value)
        {
            int labelSize = UIManager.ScaleInt(9);
            int valueSize = UIManager.ScaleInt(14);
            FontManager.DrawText(label, x + UIManager.ScaleInt(4), y,
                labelSize, COLOR_INK_MUTED);
            FontManager.DrawText(FitText(value, width - UIManager.ScaleInt(8), valueSize),
                x + UIManager.ScaleInt(4), y + UIManager.ScaleInt(11),
                valueSize, COLOR_INK);
        }

        // Simple typographic heading that preserves the book-page aesthetic without
        // introducing another panel or boxed UI element.
        private static void DrawSectionHeading(string text, int x, int y)
        {
            int fontSize = UIManager.ScaleInt(11);
            FontManager.DrawText(text, x, y, fontSize, COLOR_INK);
            int textWidth = FontManager.MeasureText(text, fontSize);
            int lineY = y + UIManager.ScaleInt(15);
            int lineEnd = x + Math.Max(textWidth, UIManager.ScaleInt(36));
            Raylib.DrawLine(x, lineY, lineEnd, lineY,
                new Color(COLOR_BORDER.R, COLOR_BORDER.G, COLOR_BORDER.B, (byte)120));
        }

        private static int GetDetailContentHeight(SpeciesEntry entry, int panelWidth)
        {
            int padding = UIManager.ScaleInt(18);
            int contentWidth = Math.Max(1, panelWidth - padding * 2);
            string behavior = string.IsNullOrWhiteSpace(entry.Info.Behavior)
                ? "Comportement inconnu" : entry.Info.Behavior!;
            int behaviorLines = CountWrappedLines(behavior, contentWidth, UIManager.ScaleInt(12), 5);
            int height = UIManager.ScaleInt(176 + 1 + 27 + 24 + 12 + 34 * 4 + 38 + 21);
            height += behaviorLines * UIManager.ScaleInt(16) + UIManager.ScaleInt(12 + 22);
            height += entry.Info.Drops.Count == 0
                ? UIManager.ScaleInt(22)
                : entry.Info.Drops.Count * UIManager.ScaleInt(21);
            return height + padding * 2;
        }

        private static void DrawSpeciesPreview(SpeciesEntry entry, Rectangle bounds, float previewScale)
        {
            Vector2 position = new(
                bounds.X + bounds.Width / 2f,
                bounds.Y + bounds.Height / 2f + UIManager.ScaleInt(2));

            float speciesScale = entry.Info.Scale > 0f ? entry.Info.Scale : 1f;
            float customScale = Math.Clamp(previewScale / Math.Max(0.7f, speciesScale), 0.25f, 1.5f);

            EntityRenderer.DrawEntity(
                entry.Key,
                "idle", 0, 0f,
                1f, position,
                entry.Tint,
                new Texture2D(), new Texture2D(), Color.Black,
                SpeciesData.Skeletons,
                eyes: new Texture2D(),
                mouth: new Texture2D(),
                customScale: customScale,
                equipment: null,
                isCarrying: false,
                inWater: false,
                attackSwingProgress: 0f,
                randomFeatureVariant: entry.FeatureVariants,
                randomFeatureColor: entry.FeatureColors
            );
        }


        private static void DrawSpeciesHeadPreview(SpeciesEntry entry, Rectangle bounds)
        {
            float speciesScale = entry.Info.Scale > 0f ? entry.Info.Scale : 1f;
            float customScale = Math.Clamp(1.4f / Math.Max(0.7f, speciesScale), 0.4f, 2.2f);
            float renderScale = EntityRenderer.BASE_ENTITY_SCALE * speciesScale * customScale;
            Vector2 headOffset = GetSpeciesHeadOffset(entry);
            Vector2 position = new Vector2(
                bounds.X + bounds.Width / 2f,
                bounds.Y + bounds.Height / 2f) - headOffset * renderScale;

            EntityRenderer.DrawEntity(
                entry.Key,
                "idle", 0, 0f,
                1f, position,
                entry.Tint,
                new Texture2D(), new Texture2D(), Color.Black,
                SpeciesData.Skeletons,
                eyes: new Texture2D(),
                mouth: new Texture2D(),
                customScale: customScale,
                equipment: null,
                isCarrying: false,
                inWater: false,
                attackSwingProgress: 0f,
                randomFeatureVariant: entry.FeatureVariants,
                randomFeatureColor: entry.FeatureColors,
                drawShadow: false
            );
        }

        private static Vector2 GetSpeciesHeadOffset(SpeciesEntry entry)
        {
            if (!SpeciesData.Skeletons.TryGetValue(entry.Key.ToLowerInvariant(), out var skeleton))
                return Vector2.Zero;

            AnimalBodyPart? head = skeleton.FirstOrDefault(part => part.Name == "head")
                ?? skeleton.FirstOrDefault(part => part.Name == "body");
            if (head == null)
                return Vector2.Zero;

            var hierarchy = new List<AnimalBodyPart>();
            AnimalBodyPart? current = head;
            while (current != null && hierarchy.Count < skeleton.Count)
            {
                hierarchy.Add(current);
                current = string.IsNullOrEmpty(current.ParentName)
                    ? null
                    : skeleton.FirstOrDefault(part => part.Name == current.ParentName);
            }

            hierarchy.Reverse();
            Vector2 offset = Vector2.Zero;
            float parentRotation = 0f;
            bool hasParent = false;
            foreach (AnimalBodyPart part in hierarchy)
            {
                Vector2 localOffset = part.BasePos;
                if (hasParent)
                {
                    float rotation = parentRotation * MathF.PI / 180f;
                    localOffset = new Vector2(
                        localOffset.X * MathF.Cos(rotation) - localOffset.Y * MathF.Sin(rotation),
                        localOffset.X * MathF.Sin(rotation) + localOffset.Y * MathF.Cos(rotation));
                    offset += localOffset;
                }
                else
                {
                    offset = localOffset;
                }

                parentRotation += part.BaseRot;
                hasParent = true;
            }

            return offset;
        }

        private static void DrawItemSlot(Rectangle rect, bool isSelected, bool isHovered)
        {
            Texture2D texture = GetItemSlotTexture();

            if (texture.Id != 0)
            {
                float texW = texture.Width;
                float texH = texture.Height;
                float scale = Math.Min(rect.Width / texW, rect.Height / texH);
                float drawW = texW * scale;
                float drawH = texH * scale;
                float drawX = rect.X + (rect.Width - drawW) / 2f;
                float drawY = rect.Y + (rect.Height - drawH) / 2f;

                Color tint = isSelected ? new Color(255, 235, 180, 255)
                    : isHovered ? new Color(240, 240, 240, 255) : Color.White;

                Raylib.DrawTexturePro(texture,
                    new Rectangle(0, 0, texW, texH),
                    new Rectangle(drawX, drawY, drawW, drawH),
                    Vector2.Zero, 0, tint);
            }
            else
            {
                Color bg = isSelected ? COLOR_ITEM_SELECTED
                    : isHovered ? COLOR_ITEM_HOVER : COLOR_ITEM_NORMAL;

                Raylib.DrawRectangleRounded(rect, 0.08f, 7, bg);
                Raylib.DrawRectangleRoundedLines(rect, 0.08f, 7, 1, COLOR_BORDER);
            }
        }

        private static void DrawSearchBar(Rectangle rect)
        {
            Texture2D texture = GetUiTexture("item_ui_search_bar");
            if (texture.Id != 0)
            {
                float texW = texture.Width;
                float texH = texture.Height;
                float scale = Math.Min(rect.Width / texW, rect.Height / texH);
                float drawW = texW * scale;
                float drawH = texH * scale;
                float drawX = rect.X + (rect.Width - drawW) / 2f;
                float drawY = rect.Y + (rect.Height - drawH) / 2f;

                Raylib.DrawTexturePro(texture,
                    new Rectangle(0, 0, texW, texH),
                    new Rectangle(drawX, drawY, drawW, drawH),
                    Vector2.Zero, 0, Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(rect, 0.18f, 6,
                    _isSearching ? new Color(39, 43, 54, 255) : COLOR_SEARCH_BG);
                Raylib.DrawRectangleRoundedLines(rect, 0.18f, 6, 1,
                    _isSearching ? COLOR_ACCENT : COLOR_BORDER);
            }
        }

        private static void DrawDescriptionPanel(Rectangle rect)
        {
            Texture2D texture = GetUiTexture("item_ui_back");
            if (texture.Id != 0)
            {
                // Étirer le fond sur tout le panneau afin qu'il ne laisse pas de bandes vides.
                Raylib.DrawTexturePro(texture,
                    new Rectangle(0, 0, texture.Width, texture.Height),
                    rect, Vector2.Zero, 0, new Color(225, 225, 225, 245));
            }
            else
            {
                Raylib.DrawRectangleRounded(rect, 0.055f, 8, COLOR_INFO_BG);
                Raylib.DrawRectangleRoundedLines(rect, 0.055f, 8, 1, COLOR_BORDER);
            }
        }

        private static Texture2D GetItemSlotTexture()
        {
            const string key = "default";
            if (_uiTextures.TryGetValue(key, out Texture2D existing) && existing.Id != 0)
                return existing;

            string[] paths =
            {
                "assets/gui/item_ui_case.png",
                "assets/gui/skill_case.png"
            };

            foreach (string path in paths)
            {
                if (!File.Exists(path)) continue;

                Texture2D texture = Raylib.LoadTexture(path);
                Raylib.SetTextureFilter(texture, TextureFilter.Point);
                _uiTextures[key] = texture;
                return texture;
            }

            return new Texture2D();
        }

        private static Texture2D GetUiTexture(string textureName)
        {
            if (_uiTextures.TryGetValue(textureName, out Texture2D existing) && existing.Id != 0)
                return existing;

            string[] paths =
            {
                $"assets/gui/{textureName}.png",
                $"assets/gui/{textureName}.jpg"
            };

            foreach (string path in paths)
            {
                if (!File.Exists(path)) continue;

                Texture2D texture = Raylib.LoadTexture(path);
                Raylib.SetTextureFilter(texture, TextureFilter.Point);
                _uiTextures[textureName] = texture;
                return texture;
            }

            return new Texture2D();
        }

        private static List<SpeciesEntry> GetFilteredEntries()
        {
            List<SpeciesEntry> filtered = _speciesEntries
                .Where(e => MatchesCategory(e) && MatchesSearch(e))
                .ToList();

            if (_selectedIndex >= filtered.Count)
                _selectedIndex = -1;

            return filtered;
        }

        private static bool MatchesCategory(SpeciesEntry entry)
        {
            return _selectedCategory == "Tout" ||
                string.Equals(entry.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesSearch(SpeciesEntry entry)
        {
            if (string.IsNullOrWhiteSpace(_searchFilter)) return true;

            return entry.DisplayName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                   entry.Key.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                   entry.Category.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> GetCategoryOptions()
        {
            return new[] { "Tout" }
                .Concat(_speciesEntries
                    .Select(entry => entry.Category)
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(category => category, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        private static string GetCategoryShortLabel(string category)
        {
            return category.ToLowerInvariant() switch
            {
                "tout" => "Tout",
                "biped" => "Bipèdes",
                "quadruped" => "Quadrupèdes",
                "flying" => "Volants",
                "spider" => "Arachnides",
                "worm" => "Vers",
                "other" => "Autres",
                _ => category
            };
        }

        private static Rectangle GetCloseButtonRect(int x, int y)
        {
            return new Rectangle(
                x + WINDOW_WIDTH - UIManager.ScaleInt(38),
                y + UIManager.ScaleInt(10),
                CLOSE_BUTTON_SIZE, CLOSE_BUTTON_SIZE);
        }

        private static Rectangle GetSearchRect(int x, int y)
        {
            return new Rectangle(
                x + UIManager.ScaleInt(14),
                y + HEADER_HEIGHT + UIManager.ScaleInt(10),
                GRID_WIDTH - UIManager.ScaleInt(8),
                UIManager.ScaleInt(29));
        }

        private static Rectangle GetCategoryAreaRect(int x, int y)
        {
            return new Rectangle(
                x + UIManager.ScaleInt(12),
                y + HEADER_HEIGHT + UIManager.ScaleInt(48),
                GRID_WIDTH - UIManager.ScaleInt(8),
                UIManager.ScaleInt(27));
        }

        private static Rectangle GetCategoryArrowRect(int x, int y, bool right)
        {
            Rectangle area = GetCategoryAreaRect(x, y);
            int size = UIManager.ScaleInt(22);
            int arrowX = right
                ? (int)(area.X + area.Width - size)
                : (int)area.X;

            return new Rectangle(arrowX, area.Y, size, area.Height);
        }

        private static int GetVisibleCategoryCount()
        {
            int areaWidth = GRID_WIDTH - UIManager.ScaleInt(8);
            int arrowSize = UIManager.ScaleInt(22);
            int spacing = UIManager.ScaleInt(4);
            int tabWidth = UIManager.ScaleInt(76);
            int available = areaWidth - arrowSize * 2 - UIManager.ScaleInt(8);
            return Math.Max(1, (available + spacing) / (tabWidth + spacing));
        }

        private static Rectangle GetCategoryTabRect(int visibleIndex, int x, int y)
        {
            Rectangle area = GetCategoryAreaRect(x, y);
            int arrowSize = UIManager.ScaleInt(22);
            int spacing = UIManager.ScaleInt(4);
            int tabWidth = UIManager.ScaleInt(76);
            int tabHeight = UIManager.ScaleInt(27);
            int startX = (int)area.X + arrowSize + UIManager.ScaleInt(4);

            return new Rectangle(
                startX + visibleIndex * (tabWidth + spacing),
                area.Y, tabWidth, tabHeight);
        }

        private static Rectangle GetGridRect(int x, int y)
        {
            int gridX = x + UIManager.ScaleInt(12);
            int gridY = y + HEADER_HEIGHT + GRID_TOP_OFFSET;
            int gridW = GRID_WIDTH - UIManager.ScaleInt(8);
            int gridH = WINDOW_HEIGHT - HEADER_HEIGHT - GRID_TOP_OFFSET - UIManager.ScaleInt(14);

            return new Rectangle(gridX, gridY, gridW, gridH);
        }

        private static Rectangle GetDetailPanelRect(int x, int y)
        {
            int gap = UIManager.ScaleInt(14);
            int rightMargin = UIManager.ScaleInt(14);
            int detailX = x + GRID_WIDTH + gap;
            int detailY = y + HEADER_HEIGHT + UIManager.ScaleInt(10);
            int detailWidth = WINDOW_WIDTH - GRID_WIDTH - gap - rightMargin;
            int detailHeight = WINDOW_HEIGHT - HEADER_HEIGHT - UIManager.ScaleInt(20);

            return new Rectangle(detailX, detailY, detailWidth, detailHeight);
        }

        private static void GetGridScrollbarRects(
            Rectangle gridRect, int totalHeight, int scrollOffset,
            out Rectangle track, out Rectangle thumb)
        {
            int trackWidth = UIManager.ScaleInt(6);
            int inset = UIManager.ScaleInt(5);
            float trackX = gridRect.X + gridRect.Width - trackWidth - inset;
            float trackY = gridRect.Y + inset;
            float trackHeight = Math.Max(1, gridRect.Height - inset * 2);

            track = new Rectangle(trackX, trackY, trackWidth, trackHeight);

            float ratio = totalHeight <= 0 ? 1f : Math.Min(1f, trackHeight / totalHeight);
            float thumbHeight = Math.Clamp(trackHeight * ratio, UIManager.ScaleInt(22), trackHeight);
            float maxScroll = Math.Max(1, totalHeight - gridRect.Height);
            float thumbY = trackY + (scrollOffset / maxScroll) * (trackHeight - thumbHeight);

            thumb = new Rectangle(trackX, thumbY, trackWidth, thumbHeight);
        }

        private static string FitText(string text, int maxWidth, int fontSize)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0) return "";
            if (FontManager.MeasureText(text, fontSize) <= maxWidth) return text;

            const string ellipsis = "...";
            string shortened = text;

            while (shortened.Length > 0 &&
                   FontManager.MeasureText(shortened + ellipsis, fontSize) > maxWidth)
            {
                shortened = shortened.Substring(0, shortened.Length - 1);
            }

            return shortened.Length == 0 ? ellipsis : shortened + ellipsis;
        }

        private static int CountWrappedLines(string text, int maxWidth, int fontSize, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0 || maxLines <= 0) return 1;

            string[] words = text.Split(new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            int lines = 1;
            string current = "";

            foreach (string word in words)
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (FontManager.MeasureText(candidate, fontSize) <= maxWidth)
                {
                    current = candidate;
                }
                else
                {
                    lines++;
                    current = word;

                    if (lines >= maxLines)
                        return maxLines;
                }
            }

            return Math.Max(1, Math.Min(lines, maxLines));
        }

        private static void DrawWrappedText(
            string text, int x, int y, int maxWidth, int fontSize,
            Color color, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0 || maxLines <= 0) return;

            string[] words = text.Split(new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            int line = 0;
            string current = "";

            foreach (string word in words)
            {
                string candidate = current.Length == 0 ? word : current + " " + word;

                if (FontManager.MeasureText(candidate, fontSize) <= maxWidth)
                {
                    current = candidate;
                    continue;
                }

                if (current.Length > 0)
                {
                    if (line == maxLines - 1)
                    {
                        FontManager.DrawText(FitText(current + " " + word, maxWidth, fontSize),
                            x, y + line * UIManager.ScaleInt(15), fontSize, color);
                        return;
                    }

                    FontManager.DrawText(current, x, y + line * UIManager.ScaleInt(15),
                        fontSize, color);
                    line++;
                }

                current = word;

                if (line >= maxLines)
                    return;
            }

            if (current.Length > 0 && line < maxLines)
                FontManager.DrawText(FitText(current, maxWidth, fontSize),
                    x, y + line * UIManager.ScaleInt(15), fontSize, color);
        }
    }
}
