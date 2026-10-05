// BestiaryUI.cs - Menu de bestiaire (encyclopédie des espèces)
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;

namespace Soulfract
{
    public static class BestiaryUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = UIManager.ScalePosition(200, 100);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static int _selectedIndex = -1;
        private static int _scrollOffset = 0;
        private static string _searchFilter = "";
        private static string _selectedCategory = "Tout";
        private static bool _isSearching = false;
        private static float _searchBlinkTimer = 0f;
        private static int _hoveredItemIndex = -1;

        // Données
        private static List<SpeciesEntry> _speciesEntries = new();

        // Constantes de mise en page
        private static int WINDOW_WIDTH => UIManager.ScaleInt(820);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(600);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(45);
        private const int GRID_COLUMNS = 4;       // 4 espèces par ligne
        private static int ITEM_WIDTH => UIManager.ScaleInt(80);
        private static int ITEM_HEIGHT => UIManager.ScaleInt(80);
        private static int ITEM_PADDING => UIManager.ScaleInt(8);
        private static int CLOSE_BUTTON_SIZE => UIManager.ScaleInt(25);
        private static int GRID_WIDTH => UIManager.ScaleInt(380);
        private static int GRID_TOP_OFFSET => UIManager.ScaleInt(82);

        // Couleurs et textures (réutilisées de ItemMenuUI)
        private static readonly Color COLOR_BG = new Color(25, 28, 35, 240);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
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
            _hoveredItemIndex = -1;
        }

        public static void Close()
        {
            _isOpen = false;
            _isDragging = false;
            _isSearching = false;
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
            bool mouseInWindow = Raylib.CheckCollisionPointRec(mousePos, new Rectangle(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, WINDOW_HEIGHT));
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

            // Catégories (onglets latéraux)
            var categoryOptions = GetCategoryOptions();
            for (int i = 0; i < categoryOptions.Count; i++)
            {
                string category = categoryOptions[i];
                Rectangle categoryRect = GetCategoryTabRect(i, (int)_windowPos.X, (int)_windowPos.Y);
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, categoryRect))
                {
                    _selectedCategory = category;
                    _scrollOffset = 0;
                    _selectedIndex = -1;
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
                            // On sélectionne l'élément
                            _selectedIndex = index;
                            clickOnInteractive = true;
                            break;
                        }
                    }
                    if (clickOnInteractive) break;
                }
                // Si clic dans la grille mais hors item, désélectionner
                if (!clickOnInteractive)
                    _selectedIndex = -1;
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
                    _isDragging = true;
                    clickOnInteractive = true;
                }
                if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    float relativeY = (mousePos.Y - scrollbarY) / (scrollbarHeight - thumbHeight);
                    relativeY = Math.Clamp(relativeY, 0f, 1f);
                    _scrollOffset = (int)(relativeY * maxScroll);
                }
                if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                    _isDragging = false;
            }

            // Drag de la fenêtre depuis la barre du haut uniquement
            Rectangle headerRect = new Rectangle(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, HEADER_HEIGHT);
            bool isOnHeader = Raylib.CheckCollisionPointRec(mousePos, headerRect);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && isOnHeader && !clickOnInteractive && !_isDragging)
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
                _searchBlinkTimer += Raylib.GetFrameTime();
                TextInput.Update(ref _searchFilter, "bestiary-search", searchRect, 30);
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

            // Panneau principal
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

            // Bouton fermer
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - UIManager.ScaleInt(35), y + UIManager.ScaleInt(10), CLOSE_BUTTON_SIZE, CLOSE_BUTTON_SIZE);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeBtn.X + UIManager.ScaleInt(8), (int)closeBtn.Y + UIManager.ScaleInt(5), UIManager.ScaleInt(16), new Color(173, 119, 87, 255));
            if (hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left)) Close();

            // Titre
            FontManager.DrawText("BESTIAIRE", x + 24, y + 12, 22, COLOR_ACCENT);

            // Barre de recherche
            Rectangle searchRect = new Rectangle(x + UIManager.ScaleInt(24), y + HEADER_HEIGHT + UIManager.ScaleInt(10), UIManager.ScaleInt(200), UIManager.ScaleInt(28));
            DrawSearchBar(searchRect);
            TextInput.DrawSingleLine(_searchFilter, "bestiary-search", searchRect, 13, COLOR_TEXT,
                new Color(90, 110, 150, 220), COLOR_ACCENT, 8, placeholder: "Rechercher...");

            // Catégories (lateral)
            var categoryOptions = GetCategoryOptions();
            for (int i = 0; i < categoryOptions.Count; i++)
            {
                string category = categoryOptions[i];
                Rectangle categoryRect = GetCategoryTabRect(i, x, y);
                bool hover = Raylib.CheckCollisionPointRec(mousePos, categoryRect);
                Color bg = category == _selectedCategory
                    ? new Color(210, 180, 100, 200)
                    : (hover ? new Color(70, 73, 80, 200) : new Color(50, 53, 60, 200));
                Raylib.DrawRectangleRounded(categoryRect, 0.2f, 6, bg);
                Raylib.DrawRectangleRoundedLines(categoryRect, 0.2f, 6, 1, COLOR_BORDER);
                // Texte court
                string label = GetCategoryShortLabel(category);
                int tw = FontManager.MeasureText(label, 12);
                FontManager.DrawText(label, (int)(categoryRect.X + (categoryRect.Width - tw) / 2f), (int)(categoryRect.Y + 22), 12,
                    category == _selectedCategory ? Color.White : COLOR_TEXT_LIGHT);
            }

            // Grille des espèces
            var filtered = GetFilteredEntries();
            int gridX = x + UIManager.ScaleInt(12);
            int gridY = y + HEADER_HEIGHT + GRID_TOP_OFFSET;
            int gridW = GRID_WIDTH;
            int gridH = WINDOW_HEIGHT - HEADER_HEIGHT - UIManager.ScaleInt(120);

            int itemCount = filtered.Count;
            int rows = (int)Math.Ceiling((double)itemCount / GRID_COLUMNS);
            int totalHeight = rows * (ITEM_HEIGHT + ITEM_PADDING) + ITEM_PADDING;

            // Scissor pour la grille
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

                    // Fond de l'emplacement
                    DrawItemSlot(itemRect, isSelected, isHovered);

                    // Miniature : on dessine une petite représentation de l'entité
                    // L'échelle dépend de la taille réelle de l'espèce : les grands animaux
                    // sont rendus plus petits pour rentrer dans la case, et les petits plus grands.
                    Vector2 miniPos = new Vector2(itemX + ITEM_WIDTH / 2f, itemY + ITEM_HEIGHT / 2f + 4f);
                    float speciesScale = entry.Info.Scale > 0f ? entry.Info.Scale : 1f;
                    float miniScale = Math.Clamp(0.42f / Math.Max(0.7f, speciesScale), 0.40f, 1.00f);
                    // On crée une Entity temporaire pour le rendu
                    var tempEntity = new Entity(miniPos, entry.Key, false)
                    {
                        Tint = Color.White,
                        HairColor = Color.Black,
                        Scale = 1f
                    };
                    // Forcer l'animation idle
                    tempEntity.AnimState = "idle";
                    // On dessine avec un squelette
                    if (SpeciesData.Skeletons.TryGetValue(entry.Key, out var skeleton))
                    {
                        EntityRenderer.DrawEntity(
                            entry.Key,
                            "idle", 0, 0f,
                            1f, miniPos,
                            Color.White,
                            new Texture2D(), new Texture2D(), Color.Black,
                            SpeciesData.Skeletons,
                            eyes: new Texture2D(),
                            mouth: new Texture2D(),
                            customScale: miniScale,
                            equipment: null,
                            isCarrying: false,
                            inWater: false,
                            attackSwingProgress: 0f
                        );
                    }
                    else
                    {
                        // Fallback : cercle coloré
                        Raylib.DrawCircle((int)miniPos.X, (int)miniPos.Y, 18, new Color(100, 150, 200, 200));
                        Raylib.DrawCircleLines((int)miniPos.X, (int)miniPos.Y, 18, Color.White);
                    }

                    // Nom (tronqué)
                    string name = entry.DisplayName;
                    int maxChars = 12;
                    if (name.Length > maxChars) name = name.Substring(0, maxChars) + "..";
                    int tw = FontManager.MeasureText(name, 11);
                    FontManager.DrawText(name, (int)(itemX + (ITEM_WIDTH - tw) / 2f), (int)(itemY + ITEM_HEIGHT - 16), 11, COLOR_TEXT_LIGHT);
                }
            }

            Raylib.EndScissorMode();

            // Barre de défilement
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
                Color thumbColor = (thumbHovered || _isDragging) ? COLOR_SCROLL_THUMB_HOVER : COLOR_SCROLL_THUMB;
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, thumbY, 8, thumbHeight), 0.25f, 4, thumbColor);
            }

            // Panneau de détails (si un élément est sélectionné)
            if (_selectedIndex >= 0 && _selectedIndex < filtered.Count)
            {
                var entry = filtered[_selectedIndex];
                int detailX = x + gridW + 16;
                int detailY = y + HEADER_HEIGHT + 10;
                int detailW = WINDOW_WIDTH - gridW - 36;
                int detailH = WINDOW_HEIGHT - HEADER_HEIGHT - 80;

                DrawDescriptionPanel(new Rectangle(detailX, detailY, detailW, detailH));

                // Grande miniature
                int iconSize = 80;
                int iconX = detailX + 20;
                int iconY = detailY + 20;
                Raylib.DrawRectangleRounded(new Rectangle(iconX, iconY, iconSize, iconSize), 0.1f, 6, new Color(50, 53, 60, 255));
                // Dessin de l'entité en grand
                Vector2 bigPos = new Vector2(iconX + iconSize / 2f, iconY + iconSize / 2f + 4f);
                if (SpeciesData.Skeletons.TryGetValue(entry.Key, out _))
                {
                    EntityRenderer.DrawEntity(
                        entry.Key,
                        "idle", 0, 0f,
                        1f, bigPos,
                        Color.White,
                        new Texture2D(), new Texture2D(), Color.Black,
                        SpeciesData.Skeletons,
                        eyes: new Texture2D(),
                        mouth: new Texture2D(),
                        customScale: 1.4f,
                        equipment: null,
                        isCarrying: false,
                        inWater: false,
                        attackSwingProgress: 0f
                    );
                }
                else
                {
                    Raylib.DrawCircle((int)bigPos.X, (int)bigPos.Y, 30, new Color(100, 150, 200, 200));
                    Raylib.DrawCircleLines((int)bigPos.X, (int)bigPos.Y, 30, Color.White);
                }

                // Nom et informations
                int nameX = detailX + iconSize + 40;
                FontManager.DrawText(entry.DisplayName, nameX, detailY + 20, 24, COLOR_ACCENT);

                // Catégorie
                FontManager.DrawText($"Type: {entry.Info.Type ?? "Inconnu"}", nameX, detailY + 50, 14, COLOR_TEXT_LIGHT);

                // Stats
                int statsY = detailY + 90;
                DrawInfoLine(detailX + 20, ref statsY, "PV max", entry.Info.MaxHp.ToString(), COLOR_TEXT_LIGHT);
                DrawInfoLine(detailX + 20, ref statsY, "Vitesse", $"{entry.Info.Speed:F0}", COLOR_TEXT_LIGHT);
                DrawInfoLine(detailX + 20, ref statsY, "Vitesse de course", $"{entry.Info.RunSpeed:F0}", COLOR_TEXT_LIGHT);
                DrawInfoLine(detailX + 20, ref statsY, "Attaque", entry.Info.Attack.ToString(), COLOR_TEXT_LIGHT);
                DrawInfoLine(detailX + 20, ref statsY, "Comportement", entry.Info.Behavior ?? "Inconnu", COLOR_TEXT_LIGHT);
                DrawInfoLine(detailX + 20, ref statsY, "Portée de vision", $"{entry.Info.VisionRange} tuiles", COLOR_TEXT_LIGHT);
                DrawInfoLine(detailX + 20, ref statsY, "Montable", entry.Info.Mountable ? "Oui" : "Non", COLOR_TEXT_LIGHT);
                DrawInfoLine(detailX + 20, ref statsY, "Apprivoisable", entry.Info.PreferredFoodId != 0 ? "Oui" : "Non", COLOR_TEXT_LIGHT);

                // Butins
                if (entry.Info.Drops.Count > 0)
                {
                    statsY += 6;
                    FontManager.DrawText("Butins:", detailX + 20, statsY, 14, COLOR_ACCENT);
                    statsY += 20;
                    foreach (var drop in entry.Info.Drops)
                    {
                        if (int.TryParse(drop.Key, out int itemId) && GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
                        {
                            string qtyText = drop.Value.MinQty == drop.Value.MaxQty ? drop.Value.MinQty.ToString() : $"{drop.Value.MinQty}-{drop.Value.MaxQty}";
                            string dropText = $"{itemData.Name} x{qtyText}";
                            FontManager.DrawText(dropText, detailX + 30, statsY, 13, COLOR_TEXT_LIGHT);
                            statsY += 18;
                        }
                        else
                        {
                            string qtyText = drop.Value.MinQty == drop.Value.MaxQty ? drop.Value.MinQty.ToString() : $"{drop.Value.MinQty}-{drop.Value.MaxQty}";
                            FontManager.DrawText($"ID {drop.Key} x{qtyText}", detailX + 30, statsY, 13, COLOR_TEXT_LIGHT);
                            statsY += 18;
                        }
                    }
                }
                else
                {
                    statsY += 6;
                    FontManager.DrawText("Aucun butin", detailX + 20, statsY, 14, new Color(160, 160, 160, 200));
                }
            }
            else
            {
                // Aucune sélection
                int detailX = x + gridW + 16;
                int detailY = y + HEADER_HEIGHT + 10;
                int detailW = WINDOW_WIDTH - gridW - 36;
                int detailH = WINDOW_HEIGHT - HEADER_HEIGHT - 80;
                FontManager.DrawText("Sélectionnez une espèce", detailX + 20, detailY + detailH / 2 - 10, 16, COLOR_TEXT);
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
            return entry.DisplayName.ToLower().Contains(_searchFilter.ToLower());
        }

        private static List<string> GetCategoryOptions()
        {
            var categories = new HashSet<string> { "Tout" };
            foreach (var entry in _speciesEntries)
            {
                if (!string.IsNullOrEmpty(entry.Category))
                    categories.Add(entry.Category);
            }
            return categories.ToList();
        }

        private static string GetCategoryShortLabel(string category)
        {
            return category switch
            {
                "biped" => "Bipède",
                "quadruped" => "Quadrupède",
                "flying" => "Volant",
                "spider" => "Arachnide",
                "worm" => "Ver",
                _ => category.Length > 4 ? category.Substring(0, 4) : category
            };
        }

        private static Rectangle GetCategoryTabRect(int index, int windowX, int windowY)
        {
            const int tabWidth = 58;
            const int tabHeight = 58;
            const int tabSpacing = 1;
            int tabX = windowX + WINDOW_WIDTH - 16;
            int tabY = windowY + 42;
            return new Rectangle(tabX, tabY + index * (tabHeight + tabSpacing), tabWidth, tabHeight);
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

        private static void DrawInfoLine(int x, ref int y, string label, string value, Color valueColor)
        {
            FontManager.DrawText($"{label}:", x, y, 13, COLOR_ACCENT);
            int labelW = FontManager.MeasureText($"{label}:", 13);
            FontManager.DrawText(value, x + labelW + 8, y, 13, valueColor);
            y += 20;
        }
    }
}