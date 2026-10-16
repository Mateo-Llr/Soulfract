// BestiaryUI.cs - Menu de bestiaire (encyclopédie des espèces)
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace Soulfract
{
    public static class BestiaryUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = UIManager.ScalePosition(200, 100);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isDraggingScrollbar = false;
        private static int _selectedIndex = -1;
        private static int _scrollOffset = 0;
        private static string _searchFilter = "";
        private static string _selectedCategory = "Tout";
        private static bool _isSearching = false;
        private static int _detailScrollOffset = 0;

        // Données
        private static List<SpeciesEntry> _speciesEntries = new();

        // Constantes de mise en page
        private static int WINDOW_WIDTH => UIManager.ScaleInt(780);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(580);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(45);
        private const int GRID_COLUMNS = 5;
        private static int ITEM_WIDTH => UIManager.ScaleInt(64);
        private static int ITEM_HEIGHT => UIManager.ScaleInt(80);
        private static int ITEM_PADDING => UIManager.ScaleInt(6);
        private static int CLOSE_BUTTON_SIZE => UIManager.ScaleInt(25);
        private static int GRID_WIDTH => UIManager.ScaleInt(380);
        private static int GRID_TOP_OFFSET => UIManager.ScaleInt(82);

        // Couleurs et textures (réutilisées de ItemMenuUI)
        private static readonly Color COLOR_BG = new Color(25, 28, 35, 240);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_ITEM_NORMAL = new Color(45, 48, 55, 200);
        private static readonly Color COLOR_ITEM_HOVER = new Color(65, 68, 75, 200);
        private static readonly Color COLOR_ITEM_SELECTED = new Color(210, 180, 100, 80);
        private static readonly Color COLOR_INFO_BG = new Color(30, 33, 40, 220);
        private static readonly Color COLOR_SEARCH_BG = new Color(40, 43, 50, 200);
        private static readonly Color COLOR_SCROLL_TRACK = new Color(40, 43, 48, 200);
        private static readonly Color COLOR_SCROLL_THUMB = new Color(210, 180, 100, 200);
        private static readonly Color COLOR_SCROLL_THUMB_HOVER = new Color(230, 200, 120, 255);
        private static readonly Color COLOR_TEXT = new Color(173, 119, 87, 255);
        private static readonly Color COLOR_TEXT_LIGHT = new Color(173, 119, 87, 220);

        // Cache de textures (pour les fonds d'emplacement)
        private static readonly Dictionary<string, Texture2D> _uiTextures = new();

        public static bool IsOpen => _isOpen;
        public static bool IsTextInputFocused => _isSearching;
        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;

        // Structure interne pour l'affichage
        private class SpeciesEntry
        {
            public string Key { get; set; } = "";
            public SpeciesData.SpeciesInfo Info { get; set; } = new();
            public string DisplayName => Info.Name ?? Key;
            public string Category => Info.Type ?? "Autre";
        }

        public static void Open()
        {
            if (IsOpen) return;
            _isOpen = true;
            UIManager.PushUI(Close, () => IsOpen);
            BuildSpeciesList();
            _selectedIndex = -1;
            _scrollOffset = 0;
            _searchFilter = "";
            _selectedCategory = "Tout";
            _isSearching = false;
            _detailScrollOffset = 0;
        }

        public static void Close()
        {
            _isOpen = false;
            _isDragging = false;
            _isDraggingScrollbar = false;
            _isSearching = false;
            _detailScrollOffset = 0;
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
                _speciesEntries.Add(entry);
            }
            // Trier par nom
            _speciesEntries.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));
        }

        public static void Update()
        {
            if (!_isOpen) return;

            Vector2 mousePos = Raylib.GetMousePosition();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            // Clamp de la fenêtre
            int visibleMargin = UIManager.ScaleInt(100);
            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + visibleMargin, sw - visibleMargin);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, sh - visibleMargin);

            var filtered = GetFilteredEntries();
            bool clickOnInteractive = false;

            // Bouton fermer
            Rectangle closeBtn = new Rectangle(_windowPos.X + WINDOW_WIDTH - UIManager.ScaleInt(35), _windowPos.Y + UIManager.ScaleInt(10), CLOSE_BUTTON_SIZE, CLOSE_BUTTON_SIZE);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, closeBtn))
            {
                clickOnInteractive = true;
                Close();
            }

            // Barre de recherche
            Rectangle searchRect = new Rectangle(_windowPos.X + UIManager.ScaleInt(24), _windowPos.Y + HEADER_HEIGHT + UIManager.ScaleInt(10), UIManager.ScaleInt(200), UIManager.ScaleInt(28));
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, searchRect))
            {
                _isSearching = true;
                clickOnInteractive = true;
            }
            else if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !Raylib.CheckCollisionPointRec(mousePos, searchRect))
            {
                _isSearching = false;
                TextInput.Reset("bestiary-search");
            }

            // Filtres de catégorie
            var categoryOptions = GetCategoryOptions();
            for (int i = 0; i < categoryOptions.Count; i++)
            {
                string category = categoryOptions[i];
                Rectangle categoryRect = GetCategoryTabRect(i, categoryOptions.Count, (int)_windowPos.X, (int)_windowPos.Y);
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, categoryRect))
                {
                    _selectedCategory = category;
                    _scrollOffset = 0;
                    _selectedIndex = -1;
                    _detailScrollOffset = 0;
                    clickOnInteractive = true;
                }
            }

            // Zone de la grille
            int gridX = (int)_windowPos.X + UIManager.ScaleInt(12);
            int gridY = (int)_windowPos.Y + HEADER_HEIGHT + GRID_TOP_OFFSET;
            int gridW = GRID_WIDTH;
            int gridH = WINDOW_HEIGHT - HEADER_HEIGHT - UIManager.ScaleInt(120);

            int itemCount = filtered.Count;
            int rows = (int)Math.Ceiling((double)itemCount / GRID_COLUMNS);
            int totalHeight = rows * (ITEM_HEIGHT + ITEM_PADDING) + ITEM_PADDING;

            // Sélection d'un élément
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                Raylib.CheckCollisionPointRec(mousePos, new Rectangle(gridX, gridY, gridW, gridH)))
            {
                int firstVisibleRow = _scrollOffset / (ITEM_HEIGHT + ITEM_PADDING);
                int maxVisibleRows = (int)Math.Ceiling((float)gridH / (ITEM_HEIGHT + ITEM_PADDING)) + 1;
                for (int row = firstVisibleRow; row < Math.Min(rows, firstVisibleRow + maxVisibleRows); row++)
                {
                    for (int col = 0; col < GRID_COLUMNS; col++)
                    {
                        int index = row * GRID_COLUMNS + col;
                        if (index >= itemCount) continue;

                        int itemX = gridX + ITEM_PADDING + col * (ITEM_WIDTH + ITEM_PADDING);
                        int itemY = gridY + ITEM_PADDING + row * (ITEM_HEIGHT + ITEM_PADDING) - _scrollOffset;
                        Rectangle itemRect = new Rectangle(itemX, itemY, ITEM_WIDTH, ITEM_HEIGHT);
                        if (Raylib.CheckCollisionPointRec(mousePos, itemRect))
                        {
                            if (_selectedIndex != index)
                                _detailScrollOffset = 0;
                            _selectedIndex = index;
                            clickOnInteractive = true;
                            break;
                        }
                    }
                    if (clickOnInteractive) break;
                }
                // Si clic dans la grille mais hors item, désélectionner
                if (!clickOnInteractive)
                {
                    _selectedIndex = -1;
                    _detailScrollOffset = 0;
                }
            }

            // Molette de défilement
            if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(gridX, gridY, gridW, gridH)))
            {
                float wheel = Raylib.GetMouseWheelMove();
                if (wheel != 0)
                {
                    int step = (int)(wheel * (ITEM_HEIGHT + ITEM_PADDING) * 2);
                    _scrollOffset -= step;
                    _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, totalHeight - gridH));
                }
            }

            if (_selectedIndex >= 0 && _selectedIndex < filtered.Count)
            {
                Rectangle detailRect = GetDetailPanelRect((int)_windowPos.X, (int)_windowPos.Y);
                if (Raylib.CheckCollisionPointRec(mousePos, detailRect))
                {
                    float wheel = Raylib.GetMouseWheelMove();
                    if (wheel != 0)
                    {
                        int detailHeight = GetDetailContentHeight(filtered[_selectedIndex]);
                        int detailViewportHeight = (int)detailRect.Height - UIManager.ScaleInt(24);
                        _detailScrollOffset -= (int)(wheel * UIManager.ScaleInt(36));
                        _detailScrollOffset = Math.Clamp(_detailScrollOffset, 0, Math.Max(0, detailHeight - detailViewportHeight));
                    }
                }
            }

            // Barre de défilement (si nécessaire)
            if (totalHeight > gridH)
            {
                int scrollbarX = gridX + gridW - 12;
                int scrollbarY = gridY;
                int scrollbarHeight = gridH;
                float thumbRatio = (float)gridH / totalHeight;
                float thumbHeight = Math.Max(20, thumbRatio * scrollbarHeight);
                float maxScroll = totalHeight - gridH;
                float thumbY = scrollbarY + (_scrollOffset / maxScroll) * (scrollbarHeight - thumbHeight);
                Rectangle thumbRect = new Rectangle(scrollbarX, thumbY, 8, thumbHeight);

                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, thumbRect))
                {
                    _isDraggingScrollbar = true;
                    clickOnInteractive = true;
                }
                if (_isDraggingScrollbar && Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    float relativeY = (mousePos.Y - scrollbarY) / (scrollbarHeight - thumbHeight);
                    relativeY = Math.Clamp(relativeY, 0f, 1f);
                    _scrollOffset = (int)(relativeY * maxScroll);
                }
                if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                    _isDraggingScrollbar = false;
            }

            // Drag de la fenêtre depuis la barre du haut uniquement
            Rectangle headerRect = new Rectangle(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, HEADER_HEIGHT);
            bool isOnHeader = Raylib.CheckCollisionPointRec(mousePos, headerRect);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && isOnHeader && !clickOnInteractive && !_isDragging && !_isDraggingScrollbar)
            {
                _isDragging = true;
                _dragOffset = mousePos - _windowPos;
            }
            if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                _windowPos = mousePos - _dragOffset;
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDragging = false;

            // Gestion de la recherche
            if (_isSearching)
            {
                string previousSearch = _searchFilter;
                TextInput.Update(ref _searchFilter, "bestiary-search", searchRect, 30);
                if (!string.Equals(previousSearch, _searchFilter, System.StringComparison.Ordinal))
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

            // Touche Échap pour fermer
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                Close();
        }

        public static void Draw()
        {
            if (!_isOpen) return;

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();

            Rectangle panelRect = new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT);
            Rectangle shadowRect = new Rectangle(x + 4, y + 4, WINDOW_WIDTH, WINDOW_HEIGHT);
            Raylib.DrawRectangleRounded(shadowRect, 0.1f, 12, new Color(0, 0, 0, 100));
            if (UIManager.BookPanelTexture != null && UIManager.BookPanelTexture.IsValid)
                UIManager.BookPanelTexture.DrawSplit(panelRect, Color.White);
            else if (UIManager.InventoryPanel != null && UIManager.InventoryPanel.IsValid)
                UIManager.InventoryPanel.Draw(panelRect, Color.White);
            else
            {
                Raylib.DrawRectangleRounded(panelRect, 0.1f, 12, COLOR_BG);
                Raylib.DrawRectangleRoundedLines(panelRect, 0.1f, 12, 2, COLOR_BORDER);
            }

            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - UIManager.ScaleInt(35), y + UIManager.ScaleInt(10), CLOSE_BUTTON_SIZE, CLOSE_BUTTON_SIZE);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeBtn.X + UIManager.ScaleInt(8), (int)closeBtn.Y + UIManager.ScaleInt(5), UIManager.ScaleInt(16), new Color(173, 119, 87, 255));
            FontManager.DrawText("BESTIAIRE", x + UIManager.ScaleInt(24), y + UIManager.ScaleInt(12), UIManager.ScaleInt(22), COLOR_ACCENT);

            Rectangle searchRect = new Rectangle(x + UIManager.ScaleInt(24), y + HEADER_HEIGHT + UIManager.ScaleInt(10), UIManager.ScaleInt(200), UIManager.ScaleInt(28));
            DrawSearchBar(searchRect);
            TextInput.DrawSingleLine(_searchFilter, "bestiary-search", searchRect, UIManager.ScaleInt(13), COLOR_TEXT,
                new Color(90, 110, 150, 220), COLOR_ACCENT, 8, placeholder: "Rechercher...");

            var categoryOptions = GetCategoryOptions();
            for (int i = 0; i < categoryOptions.Count; i++)
            {
                string category = categoryOptions[i];
                Rectangle categoryRect = GetCategoryTabRect(i, categoryOptions.Count, x, y);
                bool hover = Raylib.CheckCollisionPointRec(mousePos, categoryRect);
                Color bg = category == _selectedCategory
                    ? new Color(210, 180, 100, 255)
                    : (hover ? new Color(70, 73, 80, 255) : new Color(50, 53, 60, 200));
                Raylib.DrawRectangleRounded(categoryRect, 0.2f, 6, bg);
                Raylib.DrawRectangleRoundedLines(categoryRect, 0.2f, 6, 1, COLOR_BORDER);
                string label = GetCategoryShortLabel(category);
                int fontSize = UIManager.ScaleInt(10);
                int textWidth = FontManager.MeasureText(label, fontSize);
                FontManager.DrawText(label, (int)(categoryRect.X + (categoryRect.Width - textWidth) / 2f),
                    (int)(categoryRect.Y + (categoryRect.Height - fontSize) / 2f), fontSize,
                    category == _selectedCategory ? Color.White : COLOR_TEXT_LIGHT);
            }

            var filtered = GetFilteredEntries();
            int gridX = x + UIManager.ScaleInt(12);
            int gridY = y + HEADER_HEIGHT + GRID_TOP_OFFSET;
            int gridW = GRID_WIDTH;
            int gridH = WINDOW_HEIGHT - HEADER_HEIGHT - UIManager.ScaleInt(120);

            int itemCount = filtered.Count;
            int rows = (int)Math.Ceiling((double)itemCount / GRID_COLUMNS);
            int totalHeight = rows * (ITEM_HEIGHT + ITEM_PADDING) + ITEM_PADDING;

            Raylib.BeginScissorMode(gridX, gridY, gridW, gridH);

            int firstVisibleRow = Math.Max(0, _scrollOffset / (ITEM_HEIGHT + ITEM_PADDING));
            int maxVisibleRows = (int)Math.Ceiling((float)gridH / (ITEM_HEIGHT + ITEM_PADDING)) + 2;

            for (int row = firstVisibleRow; row < Math.Min(rows, firstVisibleRow + maxVisibleRows); row++)
            {
                for (int col = 0; col < GRID_COLUMNS; col++)
                {
                    int index = row * GRID_COLUMNS + col;
                    if (index >= itemCount) continue;

                    var entry = filtered[index];
                    int itemX = gridX + ITEM_PADDING + col * (ITEM_WIDTH + ITEM_PADDING);
                    int itemY = gridY + ITEM_PADDING + row * (ITEM_HEIGHT + ITEM_PADDING) - _scrollOffset;

                    if (itemY + ITEM_HEIGHT < gridY || itemY > gridY + gridH) continue;

                    Rectangle itemRect = new Rectangle(itemX, itemY, ITEM_WIDTH, ITEM_HEIGHT);
                    bool isHovered = Raylib.CheckCollisionPointRec(mousePos, itemRect);
                    bool isSelected = (index == _selectedIndex);

                    Rectangle slotRect = new Rectangle(itemX, itemY, ITEM_WIDTH, ITEM_WIDTH);
                    DrawItemSlot(slotRect, isSelected, isHovered);
                    DrawSpeciesPreview(entry, slotRect, 0.48f);

                    string name = entry.DisplayName;
                    int fontSize = UIManager.ScaleInt(10);
                    int nameWidth = FontManager.MeasureText(name, fontSize);
                    while (nameWidth > ITEM_WIDTH - UIManager.ScaleInt(2) && name.Length > 3)
                    {
                        name = name.Substring(0, name.Length - 2);
                        nameWidth = FontManager.MeasureText(name + "...", fontSize);
                    }
                    if (name != entry.DisplayName) name += "...";
                    nameWidth = FontManager.MeasureText(name, fontSize);
                    FontManager.DrawText(name, (int)(itemX + (ITEM_WIDTH - nameWidth) / 2f),
                        itemY + ITEM_WIDTH + UIManager.ScaleInt(2), fontSize,
                        isSelected ? COLOR_ACCENT : COLOR_TEXT_LIGHT);
                }
            }

            Raylib.EndScissorMode();

            if (totalHeight > gridH)
            {
                int scrollbarX = gridX + gridW - 12;
                int scrollbarY = gridY;
                int scrollbarHeight = gridH;
                float thumbRatio = (float)gridH / totalHeight;
                float thumbHeight = Math.Max(20, thumbRatio * scrollbarHeight);
                float maxScroll = totalHeight - gridH;
                float thumbY = scrollbarY + (_scrollOffset / maxScroll) * (scrollbarHeight - thumbHeight);
                Rectangle thumbRect = new Rectangle(scrollbarX, thumbY, 8, thumbHeight);
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, scrollbarY, 8, scrollbarHeight), 0.25f, 4, COLOR_SCROLL_TRACK);
                bool thumbHovered = Raylib.CheckCollisionPointRec(mousePos, thumbRect);
                Color thumbColor = (thumbHovered || _isDraggingScrollbar) ? COLOR_SCROLL_THUMB_HOVER : COLOR_SCROLL_THUMB;
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, thumbY, 8, thumbHeight), 0.25f, 4, thumbColor);
            }

            if (_selectedIndex >= 0 && _selectedIndex < filtered.Count)
            {
                var entry = filtered[_selectedIndex];
                Rectangle detailRect = GetDetailPanelRect(x, y);
                DrawDescriptionPanel(detailRect);
                DrawSelectedSpeciesDetails(entry, detailRect);
            }
            else
            {
                Rectangle detailRect = GetDetailPanelRect(x, y);
                DrawDescriptionPanel(detailRect);
                string emptyText = filtered.Count == 0 ? "Aucune espèce trouvée" : "Sélectionnez une espèce";
                int fontSize = UIManager.ScaleInt(14);
                int textWidth = FontManager.MeasureText(emptyText, fontSize);
                FontManager.DrawText(emptyText,
                    (int)(detailRect.X + (detailRect.Width - textWidth) / 2f),
                    (int)(detailRect.Y + (detailRect.Height - fontSize) / 2f),
                    fontSize, COLOR_TEXT_LIGHT);
            }
        }

        private static List<SpeciesEntry> GetFilteredEntries()
        {
            var filtered = _speciesEntries
                .Where(e => MatchesCategory(e) && MatchesSearch(e))
                .ToList();

            // Si l'index sélectionné n'est plus valide, on le réinitialise
            if (_selectedIndex >= filtered.Count)
                _selectedIndex = -1;

            return filtered;
        }

        private static bool MatchesCategory(SpeciesEntry entry)
        {
            if (_selectedCategory == "Tout") return true;
            return string.Equals(entry.Category, _selectedCategory, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesSearch(SpeciesEntry entry)
        {
            if (string.IsNullOrEmpty(_searchFilter)) return true;
            return entry.DisplayName.Contains(_searchFilter, System.StringComparison.OrdinalIgnoreCase) ||
                   entry.Key.Contains(_searchFilter, System.StringComparison.OrdinalIgnoreCase) ||
                   entry.Category.Contains(_searchFilter, System.StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> GetCategoryOptions()
        {
            return new[] { "Tout" }
                .Concat(_speciesEntries
                    .Select(entry => entry.Category)
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .OrderBy(category => category, System.StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        private static string GetCategoryShortLabel(string category)
        {
            return category switch
            {
                "Tout" => "Tout",
                "biped" => "Bipèdes",
                "quadruped" => "Quadrupèdes",
                "flying" => "Volants",
                "spider" => "Arachnides",
                "worm" => "Vers",
                _ => category
            };
        }

        private static Rectangle GetCategoryTabRect(int index, int count, int windowX, int windowY)
        {
            int tabSpacing = UIManager.ScaleInt(4);
            int firstTabX = windowX + UIManager.ScaleInt(24 + 200 + 12);
            int availableWidth = WINDOW_WIDTH - UIManager.ScaleInt(24 + 200 + 12 + 48);
            int tabWidth = Math.Min(UIManager.ScaleInt(68),
                (availableWidth - tabSpacing * Math.Max(0, count - 1)) / Math.Max(1, count));
            int tabHeight = UIManager.ScaleInt(28);
            int tabY = windowY + HEADER_HEIGHT + UIManager.ScaleInt(10);
            return new Rectangle(firstTabX + index * (tabWidth + tabSpacing), tabY, tabWidth, tabHeight);
        }

        private static Rectangle GetDetailPanelRect(int windowX, int windowY)
        {
            int detailX = windowX + GRID_WIDTH + UIManager.ScaleInt(16);
            int detailY = windowY + HEADER_HEIGHT + UIManager.ScaleInt(10);
            int detailWidth = WINDOW_WIDTH - GRID_WIDTH - UIManager.ScaleInt(36);
            int detailHeight = WINDOW_HEIGHT - HEADER_HEIGHT - UIManager.ScaleInt(80);
            return new Rectangle(detailX, detailY, detailWidth, detailHeight);
        }

        private static int GetDetailContentHeight(SpeciesEntry entry)
        {
            int rows = Math.Max(1, entry.Info.Drops.Count);
            return UIManager.ScaleInt(90 + 8 + 178 + 8 + 38 + 8 + 20 + rows * 20);
        }

        private static void DrawSelectedSpeciesDetails(SpeciesEntry entry, Rectangle detailRect)
        {
            int padding = UIManager.ScaleInt(12);
            int viewportHeight = (int)detailRect.Height - padding * 2;
            int contentHeight = GetDetailContentHeight(entry);
            int maxScroll = Math.Max(0, contentHeight - viewportHeight);
            _detailScrollOffset = Math.Clamp(_detailScrollOffset, 0, maxScroll);

            int contentX = (int)detailRect.X + padding;
            int contentY = (int)detailRect.Y + padding - _detailScrollOffset;
            int contentWidth = (int)detailRect.Width - padding * 2;

            Raylib.BeginScissorMode(contentX, (int)detailRect.Y + padding, contentWidth, viewportHeight);

            int portraitSize = UIManager.ScaleInt(88);
            Rectangle portraitRect = new Rectangle(contentX, contentY, portraitSize, portraitSize);
            DrawItemSlot(portraitRect, false, false);
            DrawSpeciesPreview(entry, portraitRect, 1.05f);

            int headingX = contentX + portraitSize + UIManager.ScaleInt(12);
            int headingWidth = contentWidth - portraitSize - UIManager.ScaleInt(12);
            string name = FitText(entry.DisplayName, headingWidth, UIManager.ScaleInt(18));
            FontManager.DrawText(name, headingX, contentY + UIManager.ScaleInt(12), UIManager.ScaleInt(18), COLOR_ACCENT);
            FontManager.DrawText(GetCategoryShortLabel(entry.Category), headingX, contentY + UIManager.ScaleInt(42),
                UIManager.ScaleInt(12), COLOR_TEXT_LIGHT);

            int statsY = contentY + portraitSize + UIManager.ScaleInt(10);
            int columnGap = UIManager.ScaleInt(6);
            int columnWidth = (contentWidth - columnGap) / 2;
            int cardHeight = UIManager.ScaleInt(40);
            int rowGap = UIManager.ScaleInt(4);
            DrawInfoCard(new Rectangle(contentX, statsY, columnWidth, cardHeight), "PV max", entry.Info.MaxHp.ToString());
            DrawInfoCard(new Rectangle(contentX + columnWidth + columnGap, statsY, columnWidth, cardHeight), "Attaque", entry.Info.Attack.ToString());
            statsY += cardHeight + rowGap;
            DrawInfoCard(new Rectangle(contentX, statsY, columnWidth, cardHeight), "Vitesse", $"{entry.Info.Speed:F0}");
            DrawInfoCard(new Rectangle(contentX + columnWidth + columnGap, statsY, columnWidth, cardHeight), "Course", $"{entry.Info.RunSpeed:F0}");
            statsY += cardHeight + rowGap;
            DrawInfoCard(new Rectangle(contentX, statsY, columnWidth, cardHeight), "Vision", $"{entry.Info.VisionRange} tuiles");
            DrawInfoCard(new Rectangle(contentX + columnWidth + columnGap, statsY, columnWidth, cardHeight), "Montable", entry.Info.Mountable ? "Oui" : "Non");
            statsY += cardHeight + rowGap;
            DrawInfoCard(new Rectangle(contentX, statsY, columnWidth, cardHeight), "Apprivoisable", entry.Info.PreferredFoodId != 0 ? "Oui" : "Non");
            float speciesSize = entry.Info.Scale > 0f ? entry.Info.Scale : 1f;
            DrawInfoCard(new Rectangle(contentX + columnWidth + columnGap, statsY, columnWidth, cardHeight), "Taille", $"{speciesSize:F1}x");

            statsY += cardHeight + UIManager.ScaleInt(8);
            Rectangle behaviorRect = new Rectangle(contentX, statsY, contentWidth, UIManager.ScaleInt(38));
            Raylib.DrawRectangleRounded(behaviorRect, 0.12f, 6, new Color(40, 43, 50, 180));
            FontManager.DrawText("Comportement", (int)behaviorRect.X + UIManager.ScaleInt(8),
                (int)behaviorRect.Y + UIManager.ScaleInt(4), UIManager.ScaleInt(10), COLOR_ACCENT);
            FontManager.DrawText(FitText(entry.Info.Behavior ?? "Inconnu", contentWidth - UIManager.ScaleInt(16), UIManager.ScaleInt(12)),
                (int)behaviorRect.X + UIManager.ScaleInt(8), (int)behaviorRect.Y + UIManager.ScaleInt(19),
                UIManager.ScaleInt(12), COLOR_TEXT_LIGHT);

            statsY += (int)behaviorRect.Height + UIManager.ScaleInt(8);
            FontManager.DrawText("Butins", contentX, statsY, UIManager.ScaleInt(13), COLOR_ACCENT);
            statsY += UIManager.ScaleInt(20);
            if (entry.Info.Drops.Count == 0)
            {
                FontManager.DrawText("Aucun butin", contentX, statsY, UIManager.ScaleInt(11), COLOR_TEXT_LIGHT);
            }
            else
            {
                foreach (var drop in entry.Info.Drops)
                {
                    string quantity = drop.Value.MinQty == drop.Value.MaxQty
                        ? drop.Value.MinQty.ToString()
                        : $"{drop.Value.MinQty}-{drop.Value.MaxQty}";
                    string itemName = int.TryParse(drop.Key, out int itemId) &&
                                      GameData.ItemDatabase.TryGetValue(itemId, out var itemData)
                        ? itemData.Name
                        : $"ID {drop.Key}";
                    FontManager.DrawText(FitText($"{itemName}  x{quantity}", contentWidth, UIManager.ScaleInt(11)),
                        contentX + UIManager.ScaleInt(4), statsY, UIManager.ScaleInt(11), COLOR_TEXT_LIGHT);
                    statsY += UIManager.ScaleInt(20);
                }
            }

            Raylib.EndScissorMode();

            if (contentHeight > viewportHeight)
            {
                int trackWidth = UIManager.ScaleInt(6);
                int trackX = (int)detailRect.X + (int)detailRect.Width - padding - trackWidth;
                int trackY = (int)detailRect.Y + padding;
                float thumbHeight = Math.Max(UIManager.ScaleInt(18),
                    (float)viewportHeight * viewportHeight / contentHeight);
                float thumbY = trackY + (_detailScrollOffset / (float)maxScroll) * (viewportHeight - thumbHeight);
                Raylib.DrawRectangleRounded(new Rectangle(trackX, trackY, trackWidth, viewportHeight), 0.25f, 4, COLOR_SCROLL_TRACK);
                Raylib.DrawRectangleRounded(new Rectangle(trackX, thumbY, trackWidth, thumbHeight), 0.25f, 4, COLOR_SCROLL_THUMB);
            }
        }

        private static void DrawSpeciesPreview(SpeciesEntry entry, Rectangle bounds, float previewScale)
        {
            Vector2 position = new(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f + UIManager.ScaleInt(2));
            float speciesScale = entry.Info.Scale > 0f ? entry.Info.Scale : 1f;
            float customScale = Math.Clamp(previewScale / Math.Max(0.7f, speciesScale), 0.25f, 1.5f);
            EntityRenderer.DrawEntity(
                entry.Key,
                "idle", 0, 0f,
                1f, position,
                Color.White,
                new Texture2D(), new Texture2D(), Color.Black,
                SpeciesData.Skeletons,
                eyes: new Texture2D(),
                mouth: new Texture2D(),
                customScale: customScale,
                equipment: null,
                isCarrying: false,
                inWater: false,
                attackSwingProgress: 0f
            );
        }

        private static void DrawInfoCard(Rectangle rect, string label, string value)
        {
            Raylib.DrawRectangleRounded(rect, 0.12f, 6, new Color(40, 43, 50, 180));
            int fontSize = UIManager.ScaleInt(10);
            int valueFontSize = UIManager.ScaleInt(13);
            int labelWidth = FontManager.MeasureText(label, fontSize);
            int valueWidth = FontManager.MeasureText(value, valueFontSize);
            FontManager.DrawText(label, (int)(rect.X + (rect.Width - labelWidth) / 2f),
                (int)rect.Y + UIManager.ScaleInt(4), fontSize, COLOR_TEXT_LIGHT);
            FontManager.DrawText(value, (int)(rect.X + (rect.Width - valueWidth) / 2f),
                (int)rect.Y + UIManager.ScaleInt(19), valueFontSize, COLOR_ACCENT);
        }

        private static string FitText(string text, int maxWidth, int fontSize)
        {
            if (FontManager.MeasureText(text, fontSize) <= maxWidth)
                return text;

            string ellipsis = "...";
            while (text.Length > 0 && FontManager.MeasureText(text + ellipsis, fontSize) > maxWidth)
                text = text.Substring(0, text.Length - 1);
            return text + ellipsis;
        }

        private static void DrawItemSlot(Rectangle rect, bool isSelected, bool isHovered)
        {
            var slotTexture = GetItemSlotTexture();
            if (slotTexture.Id != 0)
            {
                float texW = slotTexture.Width;
                float texH = slotTexture.Height;
                float scale = Math.Min(rect.Width / texW, rect.Height / texH);
                float drawW = texW * scale;
                float drawH = texH * scale;
                float drawX = rect.X + (rect.Width - drawW) / 2f;
                float drawY = rect.Y + (rect.Height - drawH) / 2f;

                Color tint = isSelected
                    ? new Color(245, 220, 140, 255)
                    : (isHovered ? new Color(230, 230, 230, 255) : Color.White);
                Raylib.DrawTexturePro(slotTexture,
                    new Rectangle(0, 0, texW, texH),
                    new Rectangle(drawX, drawY, drawW, drawH),
                    Vector2.Zero, 0, tint);
            }
            else
            {
                Color bgColor = isSelected ? COLOR_ITEM_SELECTED : (isHovered ? COLOR_ITEM_HOVER : COLOR_ITEM_NORMAL);
                Raylib.DrawRectangleRounded(rect, 0.1f, 8, bgColor);
                Raylib.DrawRectangleRoundedLines(rect, 0.1f, 8, 1, COLOR_BORDER);
            }
        }

        private static Texture2D GetItemSlotTexture()
        {
            const string key = "default";
            if (_uiTextures.TryGetValue(key, out var existing) && existing.Id != 0)
                return existing;

            string[] candidatePaths =
            {
                "assets/gui/item_ui_case.png",
                "assets/gui/skill_case.png"
            };

            foreach (var path in candidatePaths)
            {
                if (!File.Exists(path))
                    continue;

                var texture = Raylib.LoadTexture(path);
                Raylib.SetTextureFilter(texture, TextureFilter.Point);
                _uiTextures[key] = texture;
                return texture;
            }

            return new Texture2D();
        }

        private static void DrawSearchBar(Rectangle rect)
        {
            var texture = GetUiTexture("item_ui_search_bar");
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
                Raylib.DrawRectangleRounded(rect, 0.2f, 6, COLOR_SEARCH_BG);
                Raylib.DrawRectangleRoundedLines(rect, 0.2f, 6, 1, COLOR_BORDER);
            }
        }

        private static void DrawDescriptionPanel(Rectangle rect)
        {
            var texture = GetUiTexture("item_ui_back");
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
                Raylib.DrawRectangleRounded(rect, 0.1f, 8, COLOR_INFO_BG);
                Raylib.DrawRectangleRoundedLines(rect, 0.1f, 8, 1, COLOR_BORDER);
            }
        }

        private static Texture2D GetUiTexture(string textureName)
        {
            if (_uiTextures.TryGetValue(textureName, out var existing) && existing.Id != 0)
                return existing;

            string[] candidatePaths =
            {
                $"assets/gui/{textureName}.png",
                $"assets/gui/{textureName}.jpg"
            };

            foreach (var path in candidatePaths)
            {
                if (!File.Exists(path))
                    continue;

                var texture = Raylib.LoadTexture(path);
                Raylib.SetTextureFilter(texture, TextureFilter.Point);
                _uiTextures[textureName] = texture;
                return texture;
            }

            return new Texture2D();
        }

    }
}