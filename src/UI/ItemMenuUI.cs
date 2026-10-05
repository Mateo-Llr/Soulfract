// ItemMenuUI.cs - Version avec grille 5 colonnes, barre de défilement attrapable
#nullable enable
using Raylib_cs;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static class ItemMenuUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = UIManager.ScalePosition(200, 100);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isHoveringTitleBar = false;
        private static int _selectedIndex = -1;
        private static int _scrollOffset = 0;
        private static int _quantity = 1;
        private static string _quantityText = "1";
        private static RuneType _selectedRuneType = RuneType.Resistance;
        private static float _selectedRuneValue = 1f;
        private static string _selectedCoatSleeves = "short";
        private static string _selectedCoatOpening = "closed";
        private static List<ItemData> _items = new();
        private static string _searchFilter = "";
        private static string _selectedCategory = "Tout";
        private static bool _isSearching = false;
        private static float _searchBlinkTimer = 0f;
        private static int _hoveredItemIndex = -1;
        private static float _tooltipHoverTimer = 0f;
        private const float TOOLTIP_DELAY = 0.4f;
        private const int MAX_GIVE_QUANTITY = 99;

        // Nouveaux champs pour le défilement attrapable
        private static bool _isDraggingScrollbar = false;
        private static readonly Dictionary<string, Texture2D> _categoryTabTextures = new();
        private static readonly Dictionary<string, Texture2D> _itemSlotTextures = new();

        private static int WINDOW_WIDTH => UIManager.ScaleInt(780);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(580);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(45);
        private const int GRID_COLUMNS = 5;          // 5 items par rangée
        private static int ITEM_WIDTH => UIManager.ScaleInt(64);          // largeur d'un item dans la grille
        private static int ITEM_HEIGHT => UIManager.ScaleInt(64);          // hauteur d'un item
        private static int ITEM_PADDING => UIManager.ScaleInt(6);          // espacement entre items
        private static int GRID_WIDTH => UIManager.ScaleInt(380);
        private static int GRID_TOP_OFFSET => UIManager.ScaleInt(82);

        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;

        private static readonly Color COLOR_BG = new Color(25, 28, 35, 240);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_ITEM_NORMAL = new Color(45, 48, 55, 200);
        private static readonly Color COLOR_ITEM_HOVER = new Color(65, 68, 75, 200);
        private static readonly Color COLOR_ITEM_SELECTED = new Color(210, 180, 100, 80);
        private static readonly Color COLOR_BUTTON = new Color(80, 140, 80, 255);
        private static readonly Color COLOR_BUTTON_HOVER = new Color(100, 180, 100, 255);
        private static readonly Color COLOR_INFO_BG = new Color(30, 33, 40, 220);
        private static readonly Color COLOR_SEARCH_BG = new Color(40, 43, 50, 200);
        private static readonly Color COLOR_SCROLL_TRACK = new Color(40, 43, 48, 200);
        private static readonly Dictionary<string, Texture2D> _uiTextures = new();
        private static readonly Color COLOR_SCROLL_THUMB = new Color(210, 180, 100, 200);
        private static readonly Color COLOR_SCROLL_THUMB_HOVER = new Color(230, 200, 120, 255);

        public static bool IsOpen => _isOpen;

        public static bool IsTextInputFocused => _isSearching || TextInput.IsFocused("item-quantity");

        public static void Open()
        {
            if (IsOpen) return;
            _isOpen = true;
            UIManager.PushUI(Close, () => IsOpen);
            _items = GameData.ItemDatabase.Values
                .OrderBy(i => i.ID)
                .ToList();
            _selectedIndex = -1;
            _scrollOffset = 0;
            _quantity = 1;
            _quantityText = "1";
            TextInput.Reset("item-quantity");
            _selectedRuneType = RuneType.Resistance;
            _selectedRuneValue = 1f;
            _selectedCoatSleeves = "short";
            _selectedCoatOpening = "closed";
            _searchFilter = "";
            _selectedCategory = "Tout";
            _isSearching = false;
            _isDraggingScrollbar = false;
            _hoveredItemIndex = -1;
            _tooltipHoverTimer = 0f;
            TextInput.Reset("item-quantity");
        }

        public static void Close()
        {
            _isOpen = false;
            _isDragging = false;
            _isSearching = false;
            _isDraggingScrollbar = false;
            _hoveredItemIndex = -1;
            _tooltipHoverTimer = 0f;
        }

        public static void Update()
		{
			if (!_isOpen) return;

			Vector2 mousePos = Raylib.GetMousePosition();
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();

            int visibleMargin = UIManager.ScaleInt(100);
            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + visibleMargin, sw - visibleMargin);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, sh - visibleMargin);

			var filteredItems = GetFilteredItems();
			bool mouseInWindow = Raylib.CheckCollisionPointRec(mousePos, new Rectangle(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, WINDOW_HEIGHT));
			bool clickOnInteractive = false;

            Rectangle closeBtn = new Rectangle(_windowPos.X + WINDOW_WIDTH - UIManager.ScaleInt(35), _windowPos.Y + UIManager.ScaleInt(10), UIManager.ScaleInt(25), UIManager.ScaleInt(25));
			if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, closeBtn))
			{
				clickOnInteractive = true;
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
                TextInput.Reset("item-search");
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

			// Zone de la grille et éléments
            int gridX = (int)_windowPos.X + UIManager.ScaleInt(12);
            int gridY = (int)_windowPos.Y + HEADER_HEIGHT + GRID_TOP_OFFSET;
            int gridW = GRID_WIDTH;
            int gridH = WINDOW_HEIGHT - HEADER_HEIGHT - UIManager.ScaleInt(120);
			int itemCount = filteredItems.Count;
			int rows = (int)Math.Ceiling((double)itemCount / GRID_COLUMNS);
			int totalHeight = rows * (ITEM_HEIGHT + ITEM_PADDING) + ITEM_PADDING;

			if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, new Rectangle(gridX, gridY, gridW, gridH)))
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
							clickOnInteractive = true;
							break;
						}
					}
					if (clickOnInteractive) break;
				}
			}

			// Boutons du panneau de détail
			if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
			{
				int detailX = (int)_windowPos.X + gridW + 16;
				int detailY = (int)_windowPos.Y + HEADER_HEIGHT + 10;
				int detailW = WINDOW_WIDTH - gridW - 36;
				int detailH = WINDOW_HEIGHT - HEADER_HEIGHT - 80;

                int qtyY = detailY + detailH - 80;
                Rectangle minusBtn = GetQuantityMinusButton(detailX, qtyY);
                Rectangle quantityInput = GetQuantityInputRect(detailX, qtyY);
                Rectangle plusBtn = GetQuantityPlusButton(detailX, qtyY);
				Rectangle takeBtn = new Rectangle(detailX + detailW - 140, detailY + detailH - 85, 120, 40);
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && (Raylib.CheckCollisionPointRec(mousePos, minusBtn) || Raylib.CheckCollisionPointRec(mousePos, quantityInput) || Raylib.CheckCollisionPointRec(mousePos, plusBtn) || Raylib.CheckCollisionPointRec(mousePos, takeBtn)))
				{
					clickOnInteractive = true;
				}
			}

			// Détection du début du drag de la barre de défilement avant le drag de la fenêtre
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
			}

			// Drag de la fenêtre
			if (Raylib.IsMouseButtonPressed(MouseButton.Left) && mouseInWindow && !clickOnInteractive && !_isDraggingScrollbar)
			{
				_isDragging = true;
				_dragOffset = mousePos - _windowPos;
			}
			if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
				_windowPos = mousePos - _dragOffset;
			if (Raylib.IsMouseButtonReleased(MouseButton.Left))
			{
				_isDragging = false;
				_isDraggingScrollbar = false;
			}

			if (_isSearching)
			{
				_searchBlinkTimer += Raylib.GetFrameTime();
                TextInput.Update(ref _searchFilter, "item-search", searchRect, 30);
                if (Raylib.IsKeyPressed(KeyboardKey.Enter))
                {
                    _isSearching = false;
                    TextInput.Reset("item-search");
                }
			}

			if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
			{
				var selectedItem = _items[_selectedIndex];
				if (!filteredItems.Any(i => i.ID == selectedItem.ID))
					_selectedIndex = -1;
			}

			// Gestion du défilement (molette)
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

			// Gestion du défilement par la barre (drag)
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
				{
					_isDraggingScrollbar = false;
				}
			}

			// Survol des items pour afficher la tooltip
			int hoveredItemIndex = -1;
			bool hoveringGrid = Raylib.CheckCollisionPointRec(mousePos, new Rectangle(gridX, gridY, gridW, gridH));
			if (hoveringGrid)
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
							hoveredItemIndex = _items.IndexOf(filteredItems[index]);
							if (Raylib.IsMouseButtonPressed(MouseButton.Left))
								clickOnInteractive = true;
							break;
						}
					}
					if (hoveredItemIndex >= 0) break;
				}
			}

			if (hoveredItemIndex != _hoveredItemIndex)
			{
				_hoveredItemIndex = hoveredItemIndex;
				_tooltipHoverTimer = 0f;
			}
			else if (_hoveredItemIndex >= 0 && hoveringGrid)
			{
				_tooltipHoverTimer += Raylib.GetFrameTime();
			}
			else
			{
				_tooltipHoverTimer = 0f;
			}

			//  SECTION CORRIGÉE : Sélection d'un item 
			if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
				Raylib.CheckCollisionPointRec(mousePos, new Rectangle(gridX, gridY, gridW, gridH)))
			{
				int firstVisibleRow = _scrollOffset / (ITEM_HEIGHT + ITEM_PADDING);
				int maxVisibleRows = (int)Math.Ceiling((float)gridH / (ITEM_HEIGHT + ITEM_PADDING)) + 1;

				bool found = false;
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
							// Si on clique sur le même item, on le désélectionne
							if (_selectedIndex == _items.IndexOf(filteredItems[index]))
							{
								_selectedIndex = -1;
							}
							else
							{
								_selectedIndex = _items.IndexOf(filteredItems[index]);
							}
							found = true;
							break;
						}
					}
					if (found) break;
				}

				// Si on a cliqué dans la grille mais sur aucun item, désélectionner
				if (!found)
				{
					_selectedIndex = -1;
				}
			}

			// Gestion du curseur de quantité et du bouton "PRENDRE"
			if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
			{
                int maxQuantity = MAX_GIVE_QUANTITY;
				int detailX = (int)_windowPos.X + gridW + 16;
				int detailY = (int)_windowPos.Y + HEADER_HEIGHT + 10;
				int detailW = WINDOW_WIDTH - gridW - 36;
				int detailH = WINDOW_HEIGHT - HEADER_HEIGHT - 80;

                int qtyY = detailY + detailH - 80;
                Rectangle minusBtn = GetQuantityMinusButton(detailX, qtyY);
                Rectangle quantityInput = GetQuantityInputRect(detailX, qtyY);
                Rectangle plusBtn = GetQuantityPlusButton(detailX, qtyY);

                TextInput.Update(ref _quantityText, "item-quantity", quantityInput,
                    MAX_GIVE_QUANTITY.ToString().Length, char.IsDigit);
                if (int.TryParse(_quantityText, out int typedQuantity))
                    _quantity = Math.Clamp(typedQuantity, 1, maxQuantity);
                if (!TextInput.IsFocused("item-quantity"))
                    _quantityText = _quantity.ToString();

				if (Raylib.CheckCollisionPointRec(mousePos, minusBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
                    SetQuantity(_quantity - 1, maxQuantity);
					clickOnInteractive = true;
				}
				if (Raylib.CheckCollisionPointRec(mousePos, plusBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
                    SetQuantity(_quantity + 1, maxQuantity);
					clickOnInteractive = true;
				}

				Rectangle takeBtn = new Rectangle(
					detailX + detailW - 140,
					detailY + detailH - 85,
					120, 40
				);
				if (Raylib.CheckCollisionPointRec(mousePos, takeBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					TakeItem();
					clickOnInteractive = true;
				}

                UpdateMetadataOptions(_items[_selectedIndex], mousePos, detailX, detailY, detailW, detailH, ref clickOnInteractive);
			}

            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                if (TextInput.IsFocused("item-quantity")) TextInput.Reset("item-quantity");
                else Close();
            }
		}

        private static void TakeItem()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _items.Count) return;
            var itemData = _items[_selectedIndex];
            var item = new Item(itemData.Name, _quantity, itemData.Color, itemData.Icon);

            if (itemData.IsRune)
            {
                item.Metadata = new RuneData { Type = _selectedRuneType, Value = _selectedRuneValue }.ToMetadata();
            }

            if (IsCoat(itemData))
            {
                item.SetMetadataValue("sleeves", _selectedCoatSleeves);
                item.SetMetadataValue("opening", _selectedCoatOpening);
            }

            int remaining = Program.AddItemToInventory(item, _quantity);
            if (remaining > 0)
                Program.AddNotification(new Notification($" Inventaire plein ! {remaining} non ajoutés.", Color.Red, 1.5f));
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
            {
                UIManager.BookPanelTexture.DrawSplit(panelRect, Color.White);
            }
            else if (UIManager.InventoryPanel != null && UIManager.InventoryPanel.IsValid)
            {
                UIManager.InventoryPanel.Draw(panelRect, Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(panelRect, 0.1f, 12, COLOR_BG);
                Raylib.DrawRectangleRoundedLines(panelRect, 0.1f, 12, 2, COLOR_BORDER);
            }

            // Bouton fermer
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 35, y + 10, 25, 25);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeBtn.X + 8, (int)closeBtn.Y + 5, 16, new Color(173, 119, 87, 255));
            if (hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left)) Close();

            // Barre de recherche
            Rectangle searchRect = new Rectangle(x + 24, y + HEADER_HEIGHT + 10, 200, 28);
            DrawSearchBar(searchRect);
            Color searchColor = new Color(173, 119, 87, 255);
            TextInput.DrawSingleLine(_searchFilter, "item-search", searchRect, 13, searchColor,
                new Color(90, 110, 150, 220), COLOR_ACCENT, 8, placeholder: "Rechercher...");

            // Items filtrés avec catégories
            var filteredItems = GetFilteredItems();

            var categoryOptions = GetCategoryOptions();
            for (int i = 0; i < categoryOptions.Count; i++)
            {
                string category = categoryOptions[i];
                Rectangle categoryRect = GetCategoryTabRect(i, x, y);
                bool categoryHovered = Raylib.CheckCollisionPointRec(mousePos, categoryRect);
                Color categoryColor = category == _selectedCategory
                    ? new Color(210, 180, 100, 255)
                    : (categoryHovered ? new Color(70, 73, 80, 255) : new Color(50, 53, 60, 200));

                var tabTexture = GetCategoryTabTexture(category);
                if (tabTexture.Id != 0)
                {
                    float texW = tabTexture.Width;
                    float texH = tabTexture.Height;
                    float scale = Math.Min(categoryRect.Width / texW, categoryRect.Height / texH);
                    float drawW = texW * scale;
                    float drawH = texH * scale;
                    float drawX = categoryRect.X + (categoryRect.Width - drawW) / 2f;
                    float drawY = categoryRect.Y + (categoryRect.Height - drawH) / 2f;

                    Raylib.DrawTexturePro(tabTexture,
                        new Rectangle(0, 0, texW, texH),
                        new Rectangle(drawX, drawY, drawW, drawH),
                        Vector2.Zero, 0, category == _selectedCategory ? Color.White : new Color(220, 220, 220, 220));
                }
                else
                {
                    Raylib.DrawRectangleRounded(categoryRect, 0.2f, 6, categoryColor);
                    Raylib.DrawRectangleRoundedLines(categoryRect, 0.2f, 6, 1, COLOR_BORDER);
                }

            }

            int gridX = x + UIManager.ScaleInt(12);
            int gridY = y + HEADER_HEIGHT + GRID_TOP_OFFSET;
            int gridW = GRID_WIDTH;
            int gridH = WINDOW_HEIGHT - HEADER_HEIGHT - UIManager.ScaleInt(120);

            // Afficher la grille avec défilement
            int itemCount = filteredItems.Count;
            int rows = (int)Math.Ceiling((double)itemCount / GRID_COLUMNS);
            int totalHeight = rows * (ITEM_HEIGHT + ITEM_PADDING) + ITEM_PADDING;

            // Découpage de la grille
            Raylib.BeginScissorMode(gridX, gridY, gridW, gridH);

            int firstVisibleRow = Math.Max(0, _scrollOffset / (ITEM_HEIGHT + ITEM_PADDING));
            int maxVisibleRows = (int)Math.Ceiling((float)gridH / (ITEM_HEIGHT + ITEM_PADDING)) + 2;

            for (int row = firstVisibleRow; row < Math.Min(rows, firstVisibleRow + maxVisibleRows); row++)
            {
                for (int col = 0; col < GRID_COLUMNS; col++)
                {
                    int index = row * GRID_COLUMNS + col;
                    if (index >= itemCount) continue;

                    var item = filteredItems[index];
                    int itemX = gridX + ITEM_PADDING + col * (ITEM_WIDTH + ITEM_PADDING);
                    int itemY = gridY + ITEM_PADDING + row * (ITEM_HEIGHT + ITEM_PADDING) - _scrollOffset;

                    // Ne dessiner que si visible
                    if (itemY + ITEM_HEIGHT < gridY || itemY > gridY + gridH) continue;

                    Rectangle itemRect = new Rectangle(itemX, itemY, ITEM_WIDTH, ITEM_HEIGHT);
                    bool isHovered = Raylib.CheckCollisionPointRec(mousePos, itemRect);
                    bool isSelected = (_items.IndexOf(item) == _selectedIndex);

                    DrawItemSlot(itemRect, isSelected, isHovered);

                    // Icône : on passe toujours par ItemRenderer, qui gère lui-même le repli
                    // (couches teintées _1/_2/..., puis icône, puis couleur unie) — le vérifier
                    // via item.Icon.Id ici court-circuitait le rendu par couches pour les items
                    // sans texture de base (ex: chapka, fedora) et affichait un carré uni à la place.
                    float iconSize = UIManager.Scale(38);
                    float iconX = itemX + (ITEM_WIDTH - iconSize) / 2f;
                    float iconY = itemY + (ITEM_HEIGHT - iconSize) / 2f - 2f;
                    var tempItem = new Item(item.Name, 1, item.GetColor(), item.Icon);
                    ItemRenderer.DrawItemCompact(tempItem, (int)iconX, (int)iconY, (int)iconSize);
                }
            }

            Raylib.EndScissorMode();

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

                // Piste
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, scrollbarY, 8, scrollbarHeight), 0.25f, 4, COLOR_SCROLL_TRACK);

                // Curseur
                bool thumbHovered = Raylib.CheckCollisionPointRec(mousePos, new Rectangle(scrollbarX, thumbY, 8, thumbHeight));
                Color thumbColor = (thumbHovered || _isDraggingScrollbar) ? COLOR_SCROLL_THUMB_HOVER : COLOR_SCROLL_THUMB;
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, thumbY, 8, thumbHeight), 0.25f, 4, thumbColor);
            }

            // ========== PANEL D'INFORMATIONS ==========
            if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
            {
                var selected = _items[_selectedIndex];
                int detailX = x + gridW + 16;  // Position de la description
                int detailY = y + HEADER_HEIGHT + 10;
                int detailW = WINDOW_WIDTH - gridW - 36;  // Largeur de la description
                int detailH = WINDOW_HEIGHT - HEADER_HEIGHT - 80;

                DrawDescriptionPanel(new Rectangle(detailX, detailY, detailW, detailH));

                // Grande icône
                int iconSize = UIManager.ScaleInt(64);
                int iconX = detailX + 20;
                int iconY = detailY + 20;
                Raylib.DrawRectangleRounded(new Rectangle(iconX, iconY, iconSize, iconSize), 0.1f, 6, new Color(50, 53, 60, 255));
                var tempItem = new Item(selected.Name, 1, selected.GetColor(), selected.Icon);
                ItemRenderer.DrawItem(tempItem, iconX + 4, iconY + 4, iconSize - 8);

                // Nom et ID
                string displayName = ItemRenderer.GetDisplayName(tempItem, selected.ID, Localization.GetLocalizedItemName(selected.ID));
                FontManager.DrawText(displayName, detailX + iconSize + 30, detailY + 20, 20, new Color(173, 119, 87, 255));
                FontManager.DrawText($"ID: {selected.ID}", detailX + iconSize + 30, detailY + 48, 14, new Color(173, 119, 87, 255));

                // Informations complètes
                int infoY = iconY + iconSize + 20;
                DrawInfoLine(detailX + 20, ref infoY, " Type", GetItemTypeDisplay(selected.Type), new Color(173, 119, 87, 255));
                DrawInfoLine(detailX + 20, ref infoY, " Stack", $"{selected.StackSize}", new Color(173, 119, 87, 255));
                DrawInfoLine(detailX + 20, ref infoY, " Valeur", $"{selected.Value}", new Color(173, 119, 87, 255));

                if (selected.Type == ItemType.Armor)
                {
                    string armorSlot = GetArmorSlotDisplay(selected.ArmorSlot);
                    string armorCat = GetArmorCategoryDisplay(selected.ArmorCategory);
                    DrawInfoLine(detailX + 20, ref infoY, " Emplacement", $"{armorSlot} ({armorCat})", new Color(173, 119, 87, 255));
                    DrawInfoLine(detailX + 20, ref infoY, " Armure", $"+{selected.ArmorValue}", new Color(173, 119, 87, 255));
                }
                if (selected.Type == ItemType.Weapon)
                {
                    DrawInfoLine(detailX + 20, ref infoY, " Dégâts", $"+{selected.AttackBonus}", new Color(173, 119, 87, 255));
                }
                if (selected.Type == ItemType.Tool)
                {
                    DrawInfoLine(detailX + 20, ref infoY, " Outil", GetToolTypeDisplay(selected.ToolKind), new Color(173, 119, 87, 255));
                    DrawInfoLine(detailX + 20, ref infoY, " Puissance", $"{selected.MiningPower:F1}", new Color(173, 119, 87, 255));
                }
                if (selected.Type == ItemType.Food && selected.HealAmount > 0)
                {
                    DrawInfoLine(detailX + 20, ref infoY, " Soin", $"+{selected.HealAmount} PV", new Color(173, 119, 87, 255));
                }
                if (selected.IsPlaceable)
                {
                    string placeableName = selected.PlaceableId != 0 ? $"ID {selected.PlaceableId}" : "Oui";
                    DrawInfoLine(detailX + 20, ref infoY, " Posable", placeableName, new Color(173, 119, 87, 255));
                    if (selected.IsGroundTile)
                        DrawInfoLine(detailX + 20, ref infoY, " Sol", "Modifie le terrain", new Color(173, 119, 87, 255));
                }
                if (selected.Type == ItemType.WallCovering)
                {
                    DrawInfoLine(detailX + 20, ref infoY, " Revêtement", $"ID {selected.PlaceableId}", new Color(173, 119, 87, 255));
                }
                if (selected.Type == ItemType.Backpack)
                {
                    DrawInfoLine(detailX + 20, ref infoY, " Slots", $"{selected.BackpackSlots} ({selected.BackpackRows} rangées)", new Color(173, 119, 87, 255));
                }
                if (selected.IsLightSource)
                {
                    DrawInfoLine(detailX + 20, ref infoY, " Lumière", $"Rayon {selected.LightRadius}px", new Color(173, 119, 87, 255));
                }
                if (selected.IsSeed)
                {
                    DrawInfoLine(detailX + 20, ref infoY, " Graine", $"Culture ID {selected.CropId} ({selected.GrowthTime}s)", new Color(173, 119, 87, 255));
                }
                if (selected.IsDyeable)
                {
                    DrawInfoLine(detailX + 20, ref infoY, " Teignable", $"{selected.DyeLayers} couche(s)", new Color(173, 119, 87, 255));
                }

                // Description
                if (!string.IsNullOrEmpty(selected.Description))
                {
                    infoY += 8;
                    FontManager.DrawText(" Description:", detailX + 20, infoY, 12, new Color(173, 119, 87, 255));
                    infoY += 20;
                    string desc = selected.Description;
                    int maxWidth = detailW - 40;
                    int fontSize = 12;
                    string[] words = desc.Split(' ');
                    string line = "";
                    foreach (string word in words)
                    {
                        string testLine = line + word + " ";
                        int tw = FontManager.MeasureText(testLine, fontSize);
                        if (tw > maxWidth && line.Length > 0)
                        {
                            FontManager.DrawText(line, detailX + 20, infoY, fontSize, new Color(173, 119, 87, 220));
                            infoY += 16;
                            line = word + " ";
                        }
                        else
                        {
                            line = testLine;
                        }
                    }
                    if (line.Length > 0)
                        FontManager.DrawText(line, detailX + 20, infoY, fontSize, new Color(173, 119, 87, 220));
                }

                DrawMetadataOptions(selected, detailX, detailY, detailW, detailH, mousePos);

                // Sélecteur de quantité
                int qtyY = detailY + detailH - 80;
                Rectangle minusBtn = GetQuantityMinusButton(detailX, qtyY);
                Rectangle quantityInput = GetQuantityInputRect(detailX, qtyY);
                Rectangle plusBtn = GetQuantityPlusButton(detailX, qtyY);
                Raylib.DrawRectangleRounded(minusBtn, 0.2f, 6, new Color(60, 40, 40, 200));
                Raylib.DrawRectangleRounded(plusBtn, 0.2f, 6, new Color(60, 40, 40, 200));
                FontManager.DrawText("-", (int)minusBtn.X + 8, (int)minusBtn.Y + 4, 16, Color.White);
                FontManager.DrawText("+", (int)plusBtn.X + 8, (int)plusBtn.Y + 4, 16, Color.White);

                Raylib.DrawRectangleRounded(quantityInput, 0.15f, 4, new Color(40, 43, 50, 210));
                Raylib.DrawRectangleRoundedLines(quantityInput, 0.15f, 4, 1, COLOR_BORDER);
                TextInput.DrawSingleLine(_quantityText, "item-quantity", quantityInput, 16, Color.White,
                    new Color(90, 110, 150, 220), COLOR_ACCENT, 6, placeholder: "1");
                FontManager.DrawText($"/ {MAX_GIVE_QUANTITY}", detailX + 163, qtyY + 8, 12, new Color(173, 119, 87, 200));

                // Bouton PRENDRE
                Rectangle takeBtn = new Rectangle(detailX + detailW - 140, qtyY - 10, 120, 40);
                bool hoverTake = Raylib.CheckCollisionPointRec(mousePos, takeBtn);
                Raylib.DrawRectangleRounded(takeBtn, 0.2f, 6, hoverTake ? COLOR_BUTTON_HOVER : COLOR_BUTTON);
                Raylib.DrawRectangleRoundedLines(takeBtn, 0.2f, 6, 1, new Color(100, 200, 100, 150));
                FontManager.DrawText("PRENDRE", (int)takeBtn.X + 20, (int)takeBtn.Y + 12, 16, new Color(173, 119, 87, 255));

                // La vérification du clic se fait dans Update(), pas ici.
            }
            else
            {
                int detailX = x + gridW + 16;
                int detailY = y + HEADER_HEIGHT + 10;
                int detailW = WINDOW_WIDTH - gridW - 36;
                int detailH = WINDOW_HEIGHT - HEADER_HEIGHT - 80;
                FontManager.DrawText("Sélectionnez un item", detailX + 20, detailY + detailH / 2 - 10, 16, new Color(173, 119, 87, 200));
            }
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
            if (_itemSlotTextures.TryGetValue(key, out var existing) && existing.Id != 0)
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
                _itemSlotTextures[key] = texture;
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

        private static Texture2D GetCategoryTabTexture(string category)
        {
            string key = GetCategoryTabTextureKey(category);
            if (_categoryTabTextures.TryGetValue(key, out var existing) && existing.Id != 0)
                return existing;

            string[] candidatePaths =
            {
                $"assets/gui/item_tab_{key}.png",
                "assets/gui/item_tab.png"
            };

            foreach (var path in candidatePaths)
            {
                if (!File.Exists(path))
                    continue;

                var texture = Raylib.LoadTexture(path);
                Raylib.SetTextureFilter(texture, TextureFilter.Point);
                _categoryTabTextures[key] = texture;
                return texture;
            }

            return new Texture2D();
        }

        private static string GetCategoryTabTextureKey(string category)
        {
            return category switch
            {
                "Tout" => "all",
                "Outils" => "tool",
                "Armes" => "weapon",
                "Armures" => "armor",
                "Nourriture" => "food",
                "Ressources" => "resource",
                "Posables" => "placeable",
                _ => "other"
            };
        }

        private static string GetCategoryTabLabel(string category)
        {
            return category switch
            {
                "Tout" => "T",
                "Outils" => "O",
                "Armes" => "A",
                "Armures" => "R",
                "Nourriture" => "N",
                "Ressources" => "R",
                "Posables" => "P",
                _ => "?"
            };
        }

        private static List<ItemData> GetFilteredItems()
        {
            var filtered = _items
                .Where(item => MatchesCategory(item) && MatchesSearch(item))
                .ToList();

            if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
            {
                var selectedItem = _items[_selectedIndex];
                if (!filtered.Any(i => i.ID == selectedItem.ID))
                    _selectedIndex = -1;
            }

            return filtered;
        }

        private static bool MatchesCategory(ItemData item)
        {
            if (string.IsNullOrEmpty(_selectedCategory) || _selectedCategory == "Tout")
                return true;

            return GetItemCategory(item) == _selectedCategory;
        }

        private static bool MatchesSearch(ItemData item)
        {
            if (string.IsNullOrEmpty(_searchFilter))
                return true;

            string search = _searchFilter.ToLower();
            return item.Name.ToLower().Contains(search) ||
                   item.ID.ToString().Contains(search);
        }

        private static List<string> GetCategoryOptions()
        {
            return new List<string>
            {
                "Tout",
                "Outils",
                "Armes",
                "Armures",
                "Nourriture",
                "Ressources",
                "Posables",
                "Autres"
            };
        }

        private static string GetItemCategory(ItemData item)
        {
            return item.Type switch
            {
                ItemType.Tool => "Outils",
                ItemType.Weapon => "Armes",
                ItemType.Armor => "Armures",
                ItemType.Food => "Nourriture",
                ItemType.Resource => "Ressources",
                ItemType.Placeable or ItemType.WallCovering => "Posables",
                ItemType.Throwable => "Autres",
                ItemType.Backpack => "Autres",
                _ => "Autres"
            };
        }



        private static void DrawInfoLine(int x, ref int y, string label, string value, Color valueColor)
        {
            FontManager.DrawText($"{label}:", x, y, 12, new Color(173, 119, 87, 255));
            int labelW = FontManager.MeasureText($"{label}:", 12);
            FontManager.DrawText(value, x + labelW + 6, y, 12, valueColor);
            y += 18;
        }

        private static Rectangle GetQuantityMinusButton(int detailX, int qtyY)
            => new Rectangle(detailX + 20, qtyY, 25, 25);

        private static Rectangle GetQuantityInputRect(int detailX, int qtyY)
            => new Rectangle(detailX + 53, qtyY, 72, 25);

        private static Rectangle GetQuantityPlusButton(int detailX, int qtyY)
            => new Rectangle(detailX + 132, qtyY, 25, 25);

        private static void SetQuantity(int quantity, int maxQuantity)
        {
            _quantity = Math.Clamp(quantity, 1, Math.Max(1, maxQuantity));
            _quantityText = _quantity.ToString();
        }

        private static bool IsCoat(ItemData item)
            => string.Equals(item.Key, "coat", StringComparison.OrdinalIgnoreCase);

        private static void UpdateMetadataOptions(ItemData item, Vector2 mousePos, int detailX, int detailY, int detailW, int detailH, ref bool clickOnInteractive)
        {
            if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

            int optionsY = detailY + detailH - 150;
            Rectangle firstOption = new Rectangle(detailX + 20, optionsY + 20, detailW - 40, 20);
            Rectangle secondOption = new Rectangle(detailX + 20, optionsY + 44, detailW - 40, 20);

            if (IsCoat(item))
            {
                if (Raylib.CheckCollisionPointRec(mousePos, firstOption))
                {
                    _selectedCoatSleeves = _selectedCoatSleeves switch
                    {
                        "short" => "long",
                        "long" => "none",
                        _ => "short"
                    };
                    clickOnInteractive = true;
                }
                else if (Raylib.CheckCollisionPointRec(mousePos, secondOption))
                {
                    _selectedCoatOpening = _selectedCoatOpening switch
                    {
                        "closed" => "open",
                        "open" => "lace_up",
                        _ => "closed"
                    };
                    clickOnInteractive = true;
                }
            }
            else if (item.IsRune)
            {
                if (Raylib.CheckCollisionPointRec(mousePos, firstOption))
                {
                    RuneType[] runeTypes = Enum.GetValues<RuneType>();
                    int index = Array.IndexOf(runeTypes, _selectedRuneType);
                    _selectedRuneType = runeTypes[(index + 1 + runeTypes.Length) % runeTypes.Length];
                    clickOnInteractive = true;
                }
                else
                {
                    Rectangle minusButton = new Rectangle(detailX + 100, optionsY + 44, 25, 20);
                    Rectangle plusButton = new Rectangle(detailX + 240, optionsY + 44, 25, 20);
                    if (Raylib.CheckCollisionPointRec(mousePos, minusButton))
                    {
                        _selectedRuneValue = Math.Max(1f, _selectedRuneValue - 1f);
                        clickOnInteractive = true;
                    }
                    else if (Raylib.CheckCollisionPointRec(mousePos, plusButton))
                    {
                        _selectedRuneValue = Math.Min(100f, _selectedRuneValue + 1f);
                        clickOnInteractive = true;
                    }
                }
            }
        }

        private static void DrawMetadataOptions(ItemData item, int detailX, int detailY, int detailW, int detailH, Vector2 mousePos)
        {
            if (!IsCoat(item) && !item.IsRune) return;

            int optionsY = detailY + detailH - 150;
            FontManager.DrawText(IsCoat(item) ? "Style du manteau" : "Pouvoir de la rune", detailX + 20, optionsY, 12, new Color(173, 119, 87, 255));
            Rectangle firstOption = new Rectangle(detailX + 20, optionsY + 20, detailW - 40, 20);
            Rectangle secondOption = new Rectangle(detailX + 20, optionsY + 44, detailW - 40, 20);

            if (IsCoat(item))
            {
                DrawMetadataButton(firstOption, $"< Manches : {GetSleeveLabel(_selectedCoatSleeves)} >", mousePos);
                DrawMetadataButton(secondOption, $"< Ouverture : {GetOpeningLabel(_selectedCoatOpening)} >", mousePos);
            }
            else
            {
                DrawMetadataButton(firstOption, $"< {GetRuneTypeLabel(_selectedRuneType)} >", mousePos);
                Rectangle minusButton = new Rectangle(detailX + 100, optionsY + 44, 25, 20);
                Rectangle plusButton = new Rectangle(detailX + 240, optionsY + 44, 25, 20);
                DrawMetadataButton(minusButton, "-", mousePos);
                DrawMetadataButton(plusButton, "+", mousePos);
                FontManager.DrawText($"Valeur : {_selectedRuneValue:0.#}", detailX + 132, optionsY + 47, 12, Color.White);
            }
        }

        private static void DrawMetadataButton(Rectangle rect, string label, Vector2 mousePos)
        {
            bool hovered = Raylib.CheckCollisionPointRec(mousePos, rect);
            Raylib.DrawRectangleRounded(rect, 0.15f, 4, hovered ? COLOR_ITEM_HOVER : COLOR_ITEM_NORMAL);
            Raylib.DrawRectangleRoundedLines(rect, 0.15f, 4, 1, COLOR_BORDER);
            FontManager.DrawText(label, (int)rect.X + 6, (int)rect.Y + 3, 12, Color.White);
        }

        private static string GetSleeveLabel(string value) => value switch
        {
            "long" => "longues",
            "none" => "aucune",
            _ => "courtes"
        };

        private static string GetOpeningLabel(string value) => value switch
        {
            "open" => "ouvert",
            "lace_up" => "lacé",
            _ => "fermé"
        };

        private static string GetRuneTypeLabel(RuneType type) => type switch
        {
            RuneType.Resistance => "Résistance",
            RuneType.MaxHealth => "PV max",
            RuneType.Speed => "Vitesse",
            RuneType.Solar => "Puissance solaire",
            RuneType.Frost => "Puissance givrée",
            _ => type.ToString()
        };

        private static string GetItemTypeDisplay(ItemType type)
        {
            return type switch
            {
                ItemType.None => "Aucun",
                ItemType.Tool => "Outil",
                ItemType.Weapon => "Arme",
                ItemType.Armor => "Armure",
                ItemType.Food => "Nourriture",
                ItemType.Resource => "Ressource",
                ItemType.Placeable => "Posable",
                ItemType.Throwable => "Lanceable",
                ItemType.WallCovering => "Revêtement",
                ItemType.Backpack => "Sac à dos",
                _ => type.ToString()
            };
        }

        private static string GetArmorSlotDisplay(string slot)
        {
            return slot?.ToLower() switch
            {
                "head" => "Tête",
                "body" => "Corps",
                "legs" => "Jambes",
                "mainhand" or "weapon" => "Main droite",
                "offhand" or "shield" => "Main gauche",
                "back" => "Dos",
                _ => slot ?? "Inconnu"
            };
        }

        private static string GetArmorCategoryDisplay(ArmorCategory category)
        {
            return category switch
            {
                ArmorCategory.Hat => "Chapeau",
                ArmorCategory.Helmet => "Casque",
                ArmorCategory.Mask => "Masque",
                ArmorCategory.None => "Aucune",
                _ => category.ToString()
            };
        }

        private static string GetToolTypeDisplay(ToolType tool)
        {
            return tool switch
            {
                ToolType.Axe => "Hache",
                ToolType.Pickaxe => "Pioche",
                ToolType.Shovel => "Pelle",
                ToolType.Hoe => "Houe",
                ToolType.Scythe => "Faux",
                ToolType.Hammer => "Marteau",
                ToolType.Scissors => "Ciseaux",
                ToolType.Fishing => "Canne à pêche",
                ToolType.Net => "Filet",
                ToolType.None => "Aucun",
                _ => tool.ToString()
            };
        }
    }
}