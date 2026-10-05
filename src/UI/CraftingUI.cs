// CraftingUI.cs - Menu d'artisanat avec liste à gauche et détails à droite
#nullable enable
using Raylib_cs;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.IO;

namespace Soulfract
{
    public static class CraftingUI
    {
        private static bool _isOpen = false;
        private static readonly Dictionary<int, bool> _isOpenByPlayer = new();
        private static readonly Dictionary<int, Vector2> _windowPosByPlayer = new();
        private static readonly Dictionary<int, Vector2> _dragOffsetByPlayer = new();
        private static readonly Dictionary<int, bool> _isDraggingWindowByPlayer = new();
        
        private static int _selectedRecipe = -1;
        private static int _craftQuantity = 1;

        //  OPTIONS DE CRAFT DU MANTEAU (coat) : styles sélectionnés sur l'interface,
        // remises à zéro à chaque nouvelle sélection de recette. Traduites en métadonnées
        // (Item.Meta) sur l'item produit dans TryCraft(), lues ensuite par EntityRenderer
        // pour choisir la texture suffixée ("_open"/"_closed"/"_lace_up", "_short"/"_long").
        private static readonly Dictionary<string, string> _coatMeta = new(StringComparer.OrdinalIgnoreCase)
        {
            ["sleeves"] = "short",
            ["opening"] = "closed"
        };
        private static readonly string[] _coatStyles = { "closed", "open", "lace_up" };
        private static readonly string[] _floorStyles = { "zigzag", "tile", "square", "plank", "flat" };
        private static List<CraftRecipe> _filteredRecipes = new();
        private static string _activeStation = "";
        private static string _selectedFloorStyle = "zigzag";
        
        private static int _scrollOffset = 0;
        private static bool _isDraggingScrollbar = false;
        private static float _scrollbarDragStartY = 0f;
        private static int _scrollbarDragStartOffset = 0;
        
        private static string _selectedCategory = "Tout";
        private static readonly List<string> _allCategories = new() { "Tout", "basic", "tools", "weapons", "armor", "furniture", "smelting", "cooking" };
        private static List<string> _visibleCategories = new();
        
        private static int WINDOW_WIDTH => UIManager.ScaleInt(750);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(550);
        private static int LEFT_PANEL_WIDTH => UIManager.ScaleInt(280);
        private static int RECIPE_BUTTON_HEIGHT => UIManager.ScaleInt(76);
        private static int TAB_HEIGHT => UIManager.ScaleInt(42);
        private static int TAB_GAP => UIManager.ScaleInt(8);
        private static int TAB_TOP_OFFSET => UIManager.ScaleInt(4);
        private static int CLOSE_BTN_SIZE => UIManager.ScaleInt(28);
        private static int CLOSE_BTN_PADDING => UIManager.ScaleInt(10);
        private static int TITLE_HEIGHT => UIManager.ScaleInt(40);
        private static int CONTENT_OFFSET => TITLE_HEIGHT + UIManager.ScaleInt(15);
        private static int BOTTOM_PADDING => UIManager.ScaleInt(40);
        private static int SCROLLBAR_WIDTH => UIManager.ScaleInt(16);
        private static int SLIDER_WIDTH => UIManager.ScaleInt(30);
        private static int RECIPE_LIST_WIDTH => LEFT_PANEL_WIDTH - UIManager.ScaleInt(20) - SCROLLBAR_WIDTH - UIManager.ScaleInt(4);
        private static int RECIPE_PANEL_PADDING_X => UIManager.ScaleInt(24);
        private static int RECIPE_PANEL_PADDING_TOP => UIManager.ScaleInt(18);
        private static int RECIPE_PANEL_PADDING_BOTTOM => UIManager.ScaleInt(15);
        
        private static readonly Color COLOR_BG = new Color(20, 22, 28, 240);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_RECIPE_NORMAL = new Color(45, 48, 55, 200);
        private static readonly Color COLOR_RECIPE_HOVER = new Color(65, 68, 75, 200);
        private static readonly Color COLOR_RECIPE_SELECTED = new Color(210, 180, 100, 60);
        private static readonly Color COLOR_CRAFT_BTN = new Color(80, 140, 80, 255);
        private static readonly Color COLOR_CRAFT_BTN_HOVER = new Color(100, 180, 100, 255);
        private static readonly Color COLOR_CRAFT_BTN_DISABLED = new Color(80, 60, 60, 200);
        private static readonly Color COLOR_CATEGORY_NORMAL = new Color(50, 53, 60, 200);
        private static readonly Color COLOR_CATEGORY_HOVER = new Color(70, 73, 80, 255);
        private static readonly Color COLOR_CATEGORY_SELECTED = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_DETAILS_BG = new Color(25, 28, 35, 220);
        private static readonly Color COLOR_ACCENT_DARK = new Color(180, 150, 70, 255);
        
        private static NineSliceTexture? _panelTexture;
        private static NineSliceTexture? _slotTexture;
        private static NineSliceTexture? _recipePanelTexture;
        private static bool _useTextures = false;
        private static Texture2D _tabTexture;
        private static Texture2D _recipeLeft;
        private static Texture2D _recipeCenter;
        private static Texture2D _recipeRight;
        private static Texture2D _barFullLeft;
        private static Texture2D _barFullMid;
        private static Texture2D _barFullRight;
        private static Texture2D _barFullBorder;
        private static Texture2D _barEmptyLeft;
        private static Texture2D _barEmptyMid;
        private static Texture2D _barEmptyRight;
        private static Texture2D _barEmptyBorder;
        private static readonly Dictionary<string, Texture2D> _categoryTextures = new();
        
        private static Texture2D _slideBarTop;
        private static Texture2D _slideBarMid;
        private static Texture2D _slideBarBottom;
        private static Texture2D _sliderVertical;
        private static int _sliderHeight = 30;
        
        private static string GetCategoryTextureKey(string cat) => cat == "Tout" ? "tout" : cat;
        
        private static Texture2D TryLoadTexture(string path)
        {
            return File.Exists(path) ? Raylib.LoadTexture(path) : default;
        }
        
        public static bool IsOpen => _isOpen;
        public static bool IsOpenForPlayer(int playerIndex)
        {
            if (!_isOpenByPlayer.ContainsKey(playerIndex)) _isOpenByPlayer[playerIndex] = false;
            return _isOpenByPlayer[playerIndex];
        }
        public static string ActiveStation => _activeStation;
        
        public static void Initialize()
        {
            _panelTexture = UIManager.CraftPanelTexture;
            _slotTexture = UIManager.SlotTexture;
            _recipePanelTexture = UIManager.RecipePanelTexture;
            _useTextures = _panelTexture is { IsValid: true };
            
            _tabTexture = TryLoadTexture("assets/gui/craft_tab.png");
            _recipeLeft = TryLoadTexture("assets/gui/recipe_left.png");
            _recipeCenter = TryLoadTexture("assets/gui/recipe_center.png");
            _recipeRight = TryLoadTexture("assets/gui/recipe_right.png");
            _barFullLeft = TryLoadTexture("assets/gui/bar_full_left.png");
            _barFullMid = TryLoadTexture("assets/gui/bar_full_mid.png");
            _barFullRight = TryLoadTexture("assets/gui/bar_full_right.png");
            _barFullBorder = TryLoadTexture("assets/gui/bar_full_border.png");
            _barEmptyLeft = TryLoadTexture("assets/gui/bar_empty_left.png");
            _barEmptyMid = TryLoadTexture("assets/gui/bar_empty_mid.png");
            _barEmptyRight = TryLoadTexture("assets/gui/bar_empty_right.png");
            _barEmptyBorder = TryLoadTexture("assets/gui/bar_empty_border.png");
            
            _slideBarTop = TryLoadTexture("assets/gui/slide_bar_top.png");
            _slideBarMid = TryLoadTexture("assets/gui/slide_bar_mid.png");
            _slideBarBottom = TryLoadTexture("assets/gui/slide_bar_bottom.png");
            _sliderVertical = TryLoadTexture("assets/gui/slider_vertical.png");
            
            if (_sliderVertical.Id != 0 && _sliderVertical.Width > 0)
            {
                _sliderHeight = (int)(SLIDER_WIDTH * ((float)_sliderVertical.Height / _sliderVertical.Width));
                if (_sliderHeight < 20) _sliderHeight = 20;
            }
            else
                _sliderHeight = 30;
            
            foreach (var cat in _allCategories)
            {
                string key = GetCategoryTextureKey(cat);
                if (!_categoryTextures.ContainsKey(key))
                {
                    var tex = TryLoadTexture($"assets/gui/category_{key}.png");
                    if (tex.Id != 0)
                        _categoryTextures[key] = tex;
                }
            }
        }
        
        public static void Open(string stationType = "", int playerIndex = 0)
        {
            if (!_windowPosByPlayer.ContainsKey(playerIndex)) _windowPosByPlayer[playerIndex] = new Vector2(300, 150);
            if (!_dragOffsetByPlayer.ContainsKey(playerIndex)) _dragOffsetByPlayer[playerIndex] = Vector2.Zero;
            if (!_isDraggingWindowByPlayer.ContainsKey(playerIndex)) _isDraggingWindowByPlayer[playerIndex] = false;
            
            _isOpenByPlayer[playerIndex] = true;
            _activeStation = stationType;
            _isOpen = true;
            _selectedRecipe = -1;
            _craftQuantity = 1;
            _selectedFloorStyle = "zigzag";
            _scrollOffset = 0;
            _selectedCategory = "Tout";
            
            RefreshRecipeList();
            
            UIManager.PushUI(() => Close(playerIndex), () => IsOpenForPlayer(playerIndex));
        }

        public static void RefreshRecipeList()
        {
            UpdateVisibleCategories();
            UpdateFilteredRecipes();
        }

        public static void Close(int playerIndex = 0)
        {
            _isOpenByPlayer[playerIndex] = false;
            _isOpen = false;
            _selectedRecipe = -1;
            _activeStation = "";
            _isDraggingWindowByPlayer[playerIndex] = false;
            _dragOffsetByPlayer[playerIndex] = Vector2.Zero;
        }
        
        public static void Toggle(string stationType = "", int playerIndex = 0)
        {
            if (_isOpen && _activeStation == stationType)
                Close(playerIndex);
            else
                Open(stationType, playerIndex);
        }
        
        private static void UpdateVisibleCategories()
        {
            _visibleCategories = new List<string>();
            bool anyRecipe = false;
            foreach (var recipe in GameData.Recipes)
            {
                if (!recipe.IsRitual && IsRecipeAvailableForStation(recipe))
                {
                    anyRecipe = true;
                    break;
                }
            }
            if (anyRecipe)
                _visibleCategories.Add("Tout");
            
            foreach (var cat in _allCategories)
            {
                if (cat == "Tout") continue;
                bool hasRecipe = GameData.Recipes.Any(r => !r.IsRitual && r.Category == cat && IsRecipeAvailableForStation(r));
                if (hasRecipe)
                    _visibleCategories.Add(cat);
            }
        }
        
        private static void UpdateFilteredRecipes()
        {
            var allRecipes = GameData.Recipes.Where(r => !r.IsRitual).ToList();
            
            if (_selectedCategory == "Tout")
                _filteredRecipes = allRecipes.Where(r => IsRecipeAvailableForStation(r)).ToList();
            else
                _filteredRecipes = allRecipes.Where(r => r.Category == _selectedCategory && IsRecipeAvailableForStation(r)).ToList();
            
            if (_selectedRecipe >= _filteredRecipes.Count)
            {
                _selectedRecipe = _filteredRecipes.Count > 0 ? 0 : -1;
                ResetCoatMeta();
            }
            
            if (_selectedRecipe >= 0 && _selectedRecipe < _filteredRecipes.Count)
            {
                int maxCraft = GetMaxCraftable(_filteredRecipes[_selectedRecipe]);
                _craftQuantity = maxCraft > 0 ? Math.Clamp(_craftQuantity, 1, maxCraft) : 0;
            }
            
            UpdateVisibleCategories();
        }

        private static bool IsRecipeAvailableForStation(CraftRecipe recipe)
        {
            if (recipe.Ingredients == null || recipe.Ingredients.Count == 0)
                return true;

            // L'affichage de la recette se base sur l'historique des objets déjà possédés,
            // sans bloquer l'apparition si l'ingrédient est présent dans l'inventaire actuel.
            if (!Program.HasPreviouslyOwnedIngredient(recipe))
                return false;

            if (!string.IsNullOrEmpty(recipe.RequiresStation))
                return recipe.RequiresStation == _activeStation;
            return string.IsNullOrEmpty(_activeStation) || _activeStation == "workbench";
        }
        
        private static bool CanCraft(CraftRecipe recipe)
        {
            if (Program.IsGodMode)
                return true;

            foreach (var (id, qty) in recipe.Ingredients)
            {
                if (!GameData.ItemDatabase.TryGetValue(id, out var itemData))
                    return false;
                if (Program.GetTotalItemCount(itemData.Name) < qty)
                    return false;
            }
            return true;
        }

        private static int GetMaxCraftable(CraftRecipe recipe)
        {
            if (recipe.Ingredients == null || recipe.Ingredients.Count == 0)
                return 1;

            if (Program.IsGodMode)
                return int.MaxValue;
                
            int max = int.MaxValue;
            foreach (var (id, qty) in recipe.Ingredients)
            {
                if (!GameData.ItemDatabase.TryGetValue(id, out var itemData))
                    return 0;
                int have = Program.GetTotalItemCount(itemData.Name);
                int possible = have / qty;
                if (possible == 0)
                    return 0;
                max = Math.Min(max, possible);
            }
            return max == int.MaxValue ? 1 : max;
        }

        //  Recette produisant un manteau (coat) : identifié via la texture de base de l'item
        // ("coat" dans items.json), pas via le nom localisé, qui peut varier ("Manteau",
        // "Veste"...) selon la recette.
        private static bool IsCoatRecipe(CraftRecipe recipe) =>
            GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var d) &&
            string.Equals(d.TextureName, "coat", StringComparison.OrdinalIgnoreCase);

        private static bool IsFloorRecipe(CraftRecipe recipe) =>
            GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var d) &&
            string.Equals(d.Key, "wooden_floor", StringComparison.OrdinalIgnoreCase);

        private static void ResetCoatMeta()
        {
            _coatMeta["sleeves"] = "short";
            _coatMeta["opening"] = "closed";
            _selectedFloorStyle = "classic";
        }

        private static string GetRecipeResultName(CraftRecipe recipe)
        {
            if (!string.IsNullOrWhiteSpace(recipe.ResultName))
                return recipe.ResultName;

            if (GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var resultData))
                return resultData.Name;

            return GameData.GetItemFallbackName(GameData.GetItemKey(recipe.ResultId), recipe.ResultId);
        }

        private static void TryCraft()
        {
            if (_selectedRecipe < 0 || _selectedRecipe >= _filteredRecipes.Count) return;
            var recipe = _filteredRecipes[_selectedRecipe];
            int maxCraft = GetMaxCraftable(recipe);
            int qty = Math.Min(_craftQuantity, maxCraft);
            if (qty <= 0) return;

            Program.ConsumeIngredients(recipe, qty);
			SoundEffects.PlayCraft();

            int totalResultCount = recipe.ResultCount * qty;
            string resultName = GetRecipeResultName(recipe);
            Color resultColor = recipe.ResultColor ?? Color.White;
            if (!string.IsNullOrWhiteSpace(recipe.ResultMetadata) && GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var bottleData))
            {
                var newItem = new Item(bottleData.Name, totalResultCount, bottleData.Color, bottleData.Icon, recipe.ResultColor);
                newItem.Metadata = ItemRenderer.NormalizePotionMetadata(recipe.ResultMetadata);

                int remaining = Program.AddItemToInventory(newItem, totalResultCount);
                if (remaining > 0)
                {
                    Vector2 playerPos = Program.GetPlayerPosition();
                    Color? customColor = recipe.ResultColor;
                    List<Color?>? colors = customColor.HasValue ? new List<Color?> { customColor.Value } : null;
                    Program.DropCustomItemOnGroundWithColors(playerPos, recipe.ResultId, remaining, colors, newItem.Metadata, newItem.Meta);
                    Program.AddNotification(new Notification($" Inventaire plein ! {resultName} x{remaining} jeté au sol.", new Color(255, 200, 100, 255), 2.5f));
                }
                else
                {
                    Program.AddItemNotification(resultName, totalResultCount, resultColor);
                }
                AchievementManager.Progress(AchievementType.CraftItem, totalResultCount);
                return;
            }

            if (GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var resultData))
            {
                var newItem = new Item(resultData.Name, totalResultCount, resultData.Color, resultData.Icon, recipe.ResultColor);
                if (!string.IsNullOrWhiteSpace(recipe.ResultMetadata))
                    newItem.Metadata = ItemRenderer.NormalizePotionMetadata(recipe.ResultMetadata);

                //  Variantes spécialisées : on grave dans les métadonnées les choix faits
                // via les sélecteurs (manches, style). EntityRenderer s'en sert
                // ensuite pour choisir les textures suffixées à l'affichage sur l'entité.
                    if (IsCoatRecipe(recipe))
                {
                        newItem.Metadata = string.Join(";", _coatMeta.Select(kv => $"{kv.Key}={kv.Value}"));

                        // Debug : afficher les choix de craft pour le manteau
                        try
                        {
                            Console.WriteLine("[DEBUG] Crafting coat metadata=" + newItem.Metadata);
                        }
                        catch { }
                }
                else if (IsFloorRecipe(recipe))
                {
                    newItem.SetMeta("floor_style", _selectedFloorStyle);
                }

                int remaining = Program.AddItemToInventory(newItem, totalResultCount);

                if (remaining > 0)
                {
                    Vector2 playerPos = Program.GetPlayerPosition();
                        int itemId = recipe.ResultId;
                    Color? customColor = recipe.ResultColor;
                    List<Color?>? colors = customColor.HasValue ? new List<Color?> { customColor.Value } : null;
                    Program.DropCustomItemOnGroundWithColors(playerPos, itemId, remaining, colors, newItem.Metadata, newItem.Meta);
                    Program.AddNotification(new Notification($" Inventaire plein ! {resultData.Name} x{remaining} jeté au sol.", new Color(255, 200, 100, 255), 2.5f));
                }
                else
                {
                    Program.AddItemNotification(resultData.Name, totalResultCount, resultData.Color);
                }
                AchievementManager.Progress(AchievementType.CraftItem, totalResultCount);
            }
        }
        
        public static void Update(int playerIndex = 0, Rectangle? viewport = null)
        {
            if (!IsOpenForPlayer(playerIndex)) return;
            
            Vector2 mousePos = Raylib.GetMousePosition();
            if (viewport.HasValue)
                mousePos -= new Vector2(viewport.Value.X, viewport.Value.Y);
                
            var windowPos = _windowPosByPlayer.ContainsKey(playerIndex) ? _windowPosByPlayer[playerIndex] : new Vector2(300, 150);
            Rectangle windowRect = new Rectangle(windowPos.X, windowPos.Y, WINDOW_WIDTH, WINDOW_HEIGHT);
            
            int listWidth = RECIPE_LIST_WIDTH;
            int listStartY = (int)windowPos.Y + CONTENT_OFFSET;
            int listHeight = WINDOW_HEIGHT - CONTENT_OFFSET - BOTTOM_PADDING;
            int totalHeight = _filteredRecipes.Count * RECIPE_BUTTON_HEIGHT;
            int maxScroll = Math.Max(0, totalHeight - listHeight);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
            int scrollbarX = (int)windowPos.X + 8 + listWidth + 8;
            
            // Positions du slider et du rail
            float scrollPercent = totalHeight > listHeight ? (float)_scrollOffset / (totalHeight - listHeight) : 0f;
            int sliderY = listStartY + (int)(scrollPercent * (listHeight - _sliderHeight));
            int sliderX = scrollbarX + (SCROLLBAR_WIDTH - SLIDER_WIDTH) / 2;
            Rectangle sliderRect = new Rectangle(sliderX, sliderY, SLIDER_WIDTH, _sliderHeight);
            Rectangle railRect = new Rectangle(scrollbarX, listStartY, SCROLLBAR_WIDTH, listHeight);
            
            // Zone de clic pour la liste (exclut le rail)
            Rectangle listClickRect = new Rectangle(windowPos.X + 8, listStartY, listWidth, listHeight - 10);
            
            bool mouseInWindow = Raylib.CheckCollisionPointRec(mousePos, windowRect);
            bool clickOnInteractive = false;
            
            if (mouseInWindow && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                // Bouton de fermeture
                Rectangle closeBtn = new Rectangle(
                    windowPos.X + WINDOW_WIDTH - CLOSE_BTN_SIZE - CLOSE_BTN_PADDING,
                    windowPos.Y + (TITLE_HEIGHT - CLOSE_BTN_SIZE) / 2,
                    CLOSE_BTN_SIZE, CLOSE_BTN_SIZE);
                if (Raylib.CheckCollisionPointRec(mousePos, closeBtn))
                    clickOnInteractive = true;
                
                // Onglets
                if (!clickOnInteractive)
                {
                    int tabCount = _visibleCategories.Count;
                    if (tabCount > 0)
                    {
                        for (int i = 0; i < tabCount; i++)
                        {
                            Rectangle tabRect = GetCategoryTabRect(windowPos.X, windowPos.Y, i);
                            if (Raylib.CheckCollisionPointRec(mousePos, tabRect))
                            {
                                clickOnInteractive = true;
                                break;
                            }
                        }
                    }
                }
                
                // Slider (curseur) en premier
                if (!clickOnInteractive && totalHeight > listHeight && Raylib.CheckCollisionPointRec(mousePos, sliderRect))
                {
                    clickOnInteractive = true;
                    _isDraggingScrollbar = true;
                    _scrollbarDragStartY = mousePos.Y;
                    _scrollbarDragStartOffset = _scrollOffset;
                }
                // Rail (barre) : clic pour déplacer le slider
                else if (!clickOnInteractive && totalHeight > listHeight && Raylib.CheckCollisionPointRec(mousePos, railRect))
                {
                    clickOnInteractive = true;
                    // Calculer la nouvelle position : le clic correspond au centre du slider
                    float relativeY = mousePos.Y - listStartY;
                    float percent = Math.Clamp(relativeY / listHeight, 0f, 1f);
                    int newOffset = (int)(percent * (totalHeight - listHeight));
                    _scrollOffset = Math.Clamp(newOffset, 0, maxScroll);
                    // Démarrer le drag pour permettre le suivi
                    _isDraggingScrollbar = true;
                    _scrollbarDragStartY = mousePos.Y;
                    _scrollbarDragStartOffset = _scrollOffset;
                }
                // Liste des recettes
                else if (!clickOnInteractive && Raylib.CheckCollisionPointRec(mousePos, listClickRect))
                {
                    clickOnInteractive = true;
                }
                // Boutons du panneau de détails
                else if (!clickOnInteractive && _selectedRecipe >= 0 && _selectedRecipe < _filteredRecipes.Count)
                {
                    int rightPanelX = (int)windowPos.X + LEFT_PANEL_WIDTH + 10;
                    int rightPanelW = WINDOW_WIDTH - LEFT_PANEL_WIDTH - 25;
                    int rightPanelH = WINDOW_HEIGHT - CONTENT_OFFSET - BOTTOM_PADDING;
                    int rightPanelY = (int)windowPos.Y + CONTENT_OFFSET;
                    
                    int btnAreaY = rightPanelY + rightPanelH - 45;
                    int btnAreaW = rightPanelW - 30;
                    int btnAreaX = rightPanelX + 15;
                    int smallBtnW = 40;
                    int bigBtnW = btnAreaW - smallBtnW * 2 - 10;
                    
                    Rectangle minusBtn = new Rectangle(btnAreaX, btnAreaY, smallBtnW, 36);
                    Rectangle craftBtn = new Rectangle(btnAreaX + smallBtnW + 5, btnAreaY, bigBtnW, 36);
                    Rectangle plusBtn = new Rectangle(btnAreaX + smallBtnW + 5 + bigBtnW + 5, btnAreaY, smallBtnW, 36);
                    
                    if (Raylib.CheckCollisionPointRec(mousePos, minusBtn) ||
                        Raylib.CheckCollisionPointRec(mousePos, craftBtn) ||
                        Raylib.CheckCollisionPointRec(mousePos, plusBtn))
                        clickOnInteractive = true;
                }
                
                if (!clickOnInteractive && mouseInWindow)
                {
                    _isDraggingWindowByPlayer[playerIndex] = true;
                    _dragOffsetByPlayer[playerIndex] = mousePos - windowPos;
                }
            }
            
            if (_isDraggingWindowByPlayer[playerIndex] && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                windowPos = mousePos - _dragOffsetByPlayer[playerIndex];
                windowPos.X = Math.Clamp(windowPos.X, -WINDOW_WIDTH + 50, Raylib.GetScreenWidth() - 50);
                windowPos.Y = Math.Clamp(windowPos.Y, 0, Raylib.GetScreenHeight() - 50);
                _windowPosByPlayer[playerIndex] = windowPos;
            }
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDraggingWindowByPlayer[playerIndex] = false;
            
            // --- Drag du slider ---
            if (_isDraggingScrollbar && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                float deltaY = mousePos.Y - _scrollbarDragStartY;
                float deltaPercent = deltaY / (listHeight - _sliderHeight);
                _scrollOffset = _scrollbarDragStartOffset + (int)(deltaPercent * (totalHeight - listHeight));
                _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
            }
            if (_isDraggingScrollbar && Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDraggingScrollbar = false;
            
            // --- Scroll de la liste ---
            float wheel = Raylib.GetMouseWheelMove();
            Rectangle listArea = new Rectangle(windowPos.X, windowPos.Y + CONTENT_OFFSET, LEFT_PANEL_WIDTH, listHeight);
            if (wheel != 0 && Raylib.CheckCollisionPointRec(mousePos, listArea))
            {
                _scrollOffset -= (int)wheel * RECIPE_BUTTON_HEIGHT;
                _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
            }
            
            // --- Clic sur une recette ---
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, listClickRect))
            {
                int firstVisible = _scrollOffset / RECIPE_BUTTON_HEIGHT;
                int lastVisible = Math.Min(_filteredRecipes.Count, firstVisible + (listHeight / RECIPE_BUTTON_HEIGHT) + 2);
                for (int i = firstVisible; i < lastVisible; i++)
                {
                    if (i >= _filteredRecipes.Count) break;
                    int itemY = listStartY + i * RECIPE_BUTTON_HEIGHT - _scrollOffset;
                    if (itemY + RECIPE_BUTTON_HEIGHT < listStartY || itemY > listStartY + listHeight) continue;
                    Rectangle btnRect = new Rectangle(windowPos.X + 10, itemY, listWidth, RECIPE_BUTTON_HEIGHT - 5);
                    if (Raylib.CheckCollisionPointRec(mousePos, btnRect))
                    {
                        _selectedRecipe = i;
                        int maxCraft = GetMaxCraftable(_filteredRecipes[i]);
                        _craftQuantity = maxCraft > 0 ? Math.Clamp(_craftQuantity, 1, maxCraft) : 0;
                        ResetCoatMeta();
                        break;
                    }
                }
            }
            
            // --- Raccourcis clavier ---
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) && _selectedRecipe >= 0)
                TryCraft();
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                Close(playerIndex);
        }
        
        public static void Draw(int playerIndex = 0, Rectangle? viewport = null)
        {
            if (!IsOpenForPlayer(playerIndex)) return;
            
            var windowPos = _windowPosByPlayer.ContainsKey(playerIndex) ? _windowPosByPlayer[playerIndex] : new Vector2(300, 150);
            int x = (int)windowPos.X;
            int y = (int)windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();
            if (viewport.HasValue)
                mousePos -= new Vector2(viewport.Value.X, viewport.Value.Y);
            
            // --- Fond de la fenêtre ---
            if (_useTextures && _panelTexture != null && _panelTexture.IsValid)
            {
                _panelTexture.Draw(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(new Rectangle(x + 4, y + 4, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, new Color(0, 0, 0, 100));
                Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, COLOR_BG);
                Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, 2, COLOR_BORDER);
            }
            
            // --- Bouton de fermeture ---
            Rectangle closeBtn = new Rectangle(
                x + WINDOW_WIDTH - CLOSE_BTN_SIZE - CLOSE_BTN_PADDING,
                y + (TITLE_HEIGHT - CLOSE_BTN_SIZE) / 2,
                CLOSE_BTN_SIZE, CLOSE_BTN_SIZE);
            bool isHoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, isHoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeBtn.X + 8, (int)closeBtn.Y + 5, 16, isHoverClose ? Color.Red : new Color(200, 120, 120, 200));
            if (isHoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
                Close(playerIndex);
            
            // ========== CATÉGORIES ==========
            int tabCount = _visibleCategories.Count;
            if (tabCount > 0)
            {
                for (int i = 0; i < tabCount; i++)
                {
                    string cat = _visibleCategories[i];
                    bool isSelected = cat == _selectedCategory;
                    Rectangle tabRect = GetCategoryTabRect(x, y, i);
                    bool hover = Raylib.CheckCollisionPointRec(mousePos, tabRect);
                    
                    if (_tabTexture.Id != 0)
                    {
                        Color tint = isSelected ? Color.White : new Color(200, 200, 200, 180);
                        Raylib.DrawTexturePro(_tabTexture,
                            new Rectangle(0, 0, _tabTexture.Width, _tabTexture.Height),
                            tabRect,
                            Vector2.Zero,
                            0f,
                            tint);
                    }
                    else
                    {
                        Color tabColor = isSelected ? COLOR_CATEGORY_SELECTED : (hover ? COLOR_CATEGORY_HOVER : COLOR_CATEGORY_NORMAL);
                        Raylib.DrawRectangleRounded(tabRect, 0.25f, 8, tabColor);
                    }
                    
                    string key = GetCategoryTextureKey(cat);
                    int iconSizeTab = isSelected ? 30 : 26;
                    int iconXTab = (int)(tabRect.X + tabRect.Width / 2 - iconSizeTab / 2);
                    int iconYTab = (int)(tabRect.Y + tabRect.Height / 2 - iconSizeTab / 2);
                    
                    if (_categoryTextures.TryGetValue(key, out var catTex) && catTex.Id != 0)
                    {
                        Raylib.DrawTexturePro(catTex,
                            new Rectangle(0, 0, catTex.Width, catTex.Height),
                            new Rectangle(iconXTab, iconYTab, iconSizeTab, iconSizeTab),
                            Vector2.Zero,
                            0f,
                            isSelected ? Color.White : new Color(200, 200, 200, 220));
                    }
                    
                    if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        _selectedCategory = cat;
                        UpdateFilteredRecipes();
                        _scrollOffset = 0;
                    }
                }
            }
            
            // ========== LISTE DES RECETTES ==========
            int listStartY = y + CONTENT_OFFSET;
            int listHeight = WINDOW_HEIGHT - CONTENT_OFFSET - BOTTOM_PADDING;
            int listWidth = RECIPE_LIST_WIDTH;
            
            Raylib.BeginScissorMode(x + 8, listStartY, listWidth, listHeight - 10);
            
            int firstVisible = _scrollOffset / RECIPE_BUTTON_HEIGHT;
            int lastVisible = Math.Min(_filteredRecipes.Count, firstVisible + (listHeight / RECIPE_BUTTON_HEIGHT) + 2);
            
            for (int i = firstVisible; i < lastVisible; i++)
            {
                if (i >= _filteredRecipes.Count) break;
                
                var recipe = _filteredRecipes[i];
                int itemY = listStartY + i * RECIPE_BUTTON_HEIGHT - _scrollOffset;
                if (itemY + RECIPE_BUTTON_HEIGHT < listStartY || itemY > listStartY + listHeight) continue;
                
                Rectangle btnRect = new Rectangle(x + 10, itemY, listWidth, RECIPE_BUTTON_HEIGHT - 5);
                bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
                bool isSelected = i == _selectedRecipe;
                
                if (_recipeLeft.Id != 0 && _recipeCenter.Id != 0 && _recipeRight.Id != 0)
                {
                    Color tint = isSelected ? new Color(255, 235, 170, 255) : (isHovered ? new Color(220, 220, 220, 255) : Color.White);
                    DrawRecipeButtonBackground(btnRect, tint);
                }
                else if (_useTextures && _slotTexture != null && _slotTexture.IsValid)
                {
                    Color tint = isSelected ? new Color(210, 180, 100, 100) : (isHovered ? new Color(100, 110, 80, 150) : new Color(70, 73, 80, 150));
                    _slotTexture.Draw(btnRect, tint);
                }
                else
                {
                    Color btnColor = isSelected ? COLOR_RECIPE_SELECTED : (isHovered ? COLOR_RECIPE_HOVER : COLOR_RECIPE_NORMAL);
                    Raylib.DrawRectangleRounded(btnRect, 0.1f, 8, btnColor);
                    Raylib.DrawRectangleRoundedLines(btnRect, 0.1f, 8, 1, COLOR_BORDER);
                }
                
                if (GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var resultData))
                {
                    Color iconColor = recipe.ResultColor ?? resultData.Color;
                    var tempItem = new Item(resultData.Name, 1, resultData.Color, resultData.Icon, recipe.ResultColor);
                    
                    int iconSize = RECIPE_BUTTON_HEIGHT - 24;
                    int iconX = (int)btnRect.X + 6;
                    int iconY = (int)(btnRect.Y + (btnRect.Height - iconSize) / 2);
                    
                    ItemRenderer.DrawItemPadded(tempItem, iconX, iconY, iconSize, 0);
                    
                    string qtyText = $"x{recipe.ResultCount}";
                    int qtyTextW = FontManager.MeasureText(qtyText, 11);
                    int badgeX = iconX - 2;
                    int badgeY = iconY + iconSize - 12;
                    Raylib.DrawRectangleRounded(new Rectangle(badgeX, badgeY, qtyTextW + 6, 15), 0.3f, 6, new Color(0, 0, 0, 190));
                    FontManager.DrawText(qtyText, badgeX + 3, badgeY + 2, 11, Color.Gold);
                    
                    string localizedResultName = GetRecipeResultName(recipe);
                    string name = localizedResultName.Length > 26 ? localizedResultName[..23] + "..." : localizedResultName;
                    int nameY = (int)(btnRect.Y + (btnRect.Height - 16) / 2);
                    FontManager.DrawText(name, iconX + iconSize + 10, nameY, 16, Color.White);
                }
            }
            
            Raylib.EndScissorMode();
            
            // ========== BARRE DE DÉFILEMENT AVEC TEXTURES ==========
            int totalHeight = _filteredRecipes.Count * RECIPE_BUTTON_HEIGHT;
            if (totalHeight > listHeight)
            {
                int scrollbarX = x + 8 + listWidth + 8;
                int scrollbarY = listStartY;
                
                // Fond du rail
                Raylib.DrawRectangle(scrollbarX, scrollbarY, SCROLLBAR_WIDTH, listHeight, new Color(40, 43, 48, 200));
                
                int topHeight = _slideBarTop.Id != 0 ? _slideBarTop.Height : 8;
                int bottomHeight = _slideBarBottom.Id != 0 ? _slideBarBottom.Height : 8;
                int midHeight = listHeight - topHeight - bottomHeight;
                
                if (_slideBarTop.Id != 0)
                {
                    Rectangle srcTop = new Rectangle(0, 0, _slideBarTop.Width, _slideBarTop.Height);
                    Rectangle dstTop = new Rectangle(scrollbarX, scrollbarY, SCROLLBAR_WIDTH, topHeight);
                    Raylib.DrawTexturePro(_slideBarTop, srcTop, dstTop, Vector2.Zero, 0f, Color.White);
                }
                else
                    Raylib.DrawRectangle(scrollbarX, scrollbarY, SCROLLBAR_WIDTH, topHeight, new Color(70, 70, 80, 255));
                
                if (_slideBarMid.Id != 0 && midHeight > 0)
                {
                    Rectangle srcMid = new Rectangle(0, 0, _slideBarMid.Width, _slideBarMid.Height);
                    Rectangle dstMid = new Rectangle(scrollbarX, scrollbarY + topHeight, SCROLLBAR_WIDTH, midHeight);
                    Raylib.DrawTexturePro(_slideBarMid, srcMid, dstMid, Vector2.Zero, 0f, Color.White);
                }
                else
                    Raylib.DrawRectangle(scrollbarX, scrollbarY + topHeight, SCROLLBAR_WIDTH, midHeight, new Color(60, 60, 70, 255));
                
                if (_slideBarBottom.Id != 0)
                {
                    Rectangle srcBottom = new Rectangle(0, 0, _slideBarBottom.Width, _slideBarBottom.Height);
                    Rectangle dstBottom = new Rectangle(scrollbarX, scrollbarY + listHeight - bottomHeight, SCROLLBAR_WIDTH, bottomHeight);
                    Raylib.DrawTexturePro(_slideBarBottom, srcBottom, dstBottom, Vector2.Zero, 0f, Color.White);
                }
                else
                    Raylib.DrawRectangle(scrollbarX, scrollbarY + listHeight - bottomHeight, SCROLLBAR_WIDTH, bottomHeight, new Color(70, 70, 80, 255));
                
                // --- Curseur (slider) ---
                float scrollPercent = (float)_scrollOffset / (totalHeight - listHeight);
                int sliderY = listStartY + (int)(scrollPercent * (listHeight - _sliderHeight));
                int sliderX = scrollbarX + (SCROLLBAR_WIDTH - SLIDER_WIDTH) / 2;
                
                if (_sliderVertical.Id != 0)
                {
                    Rectangle srcSlider = new Rectangle(0, 0, _sliderVertical.Width, _sliderVertical.Height);
                    Rectangle dstSlider = new Rectangle(sliderX, sliderY, SLIDER_WIDTH, _sliderHeight);
                    Raylib.DrawTexturePro(_sliderVertical, srcSlider, dstSlider, Vector2.Zero, 0f, Color.White);
                }
                else
                {
                    Raylib.DrawRectangleRounded(new Rectangle(sliderX, sliderY, SLIDER_WIDTH, _sliderHeight), 0.2f, 6, COLOR_ACCENT);
                }
            }
            
            // ========== PARTIE DROITE : DÉTAILS ==========
            int rightPanelX = x + LEFT_PANEL_WIDTH + 10;
            int rightPanelW = WINDOW_WIDTH - LEFT_PANEL_WIDTH - 25;
            int rightPanelH = WINDOW_HEIGHT - CONTENT_OFFSET - BOTTOM_PADDING;
            int rightPanelY = y + CONTENT_OFFSET;
            
            if (_recipePanelTexture is { IsValid: true })
                _recipePanelTexture.Draw(new Rectangle(rightPanelX, rightPanelY, rightPanelW, rightPanelH), Color.White);
            else
            {
                Raylib.DrawRectangleRounded(new Rectangle(rightPanelX, rightPanelY, rightPanelW, rightPanelH), 0.1f, 8, COLOR_DETAILS_BG);
                Raylib.DrawRectangleRoundedLines(new Rectangle(rightPanelX, rightPanelY, rightPanelW, rightPanelH), 0.1f, 8, 1, COLOR_BORDER);
            }

            // Le bord du 9-slice représente le papier : le contenu commence dans sa zone utile.
            rightPanelX += RECIPE_PANEL_PADDING_X;
            rightPanelW -= RECIPE_PANEL_PADDING_X * 2;
            rightPanelY += RECIPE_PANEL_PADDING_TOP;
            rightPanelH -= RECIPE_PANEL_PADDING_TOP + RECIPE_PANEL_PADDING_BOTTOM;
            
            if (_selectedRecipe >= 0 && _selectedRecipe < _filteredRecipes.Count)
            {
                var selected = _filteredRecipes[_selectedRecipe];
                int maxCraft = GetMaxCraftable(selected);
                
                int bottleId = Program.GetItemId("Petite bouteille");
                ItemData? resultData = null;
                if (selected.ResultId == bottleId && !string.IsNullOrWhiteSpace(selected.ResultMetadata) && GameData.ItemDatabase.TryGetValue(bottleId, out var bottleData))
                    resultData = bottleData;
                else if (GameData.ItemDatabase.TryGetValue(selected.ResultId, out var resolvedResultData))
                    resultData = resolvedResultData;

                if (resultData.HasValue)
                {
                    // ===== ICÔNE =====
                    int iconSize = UIManager.ScaleInt(84);
                    int iconX = rightPanelX + (rightPanelW - iconSize) / 2;
                    int iconY = rightPanelY + 15;

                    var tempItem = new Item(resultData.Value.Name, 1, resultData.Value.Color, resultData.Value.Icon, selected.ResultColor);
                    if (IsCoatRecipe(selected))
                    {
                        tempItem.Metadata = string.Join(";", _coatMeta.Select(kv => $"{kv.Key}={kv.Value}"));
                    }
                    ItemRenderer.DrawItem(tempItem, new Rectangle(iconX, iconY, iconSize, iconSize));

                    string qtyBadge = $"x{selected.ResultCount}";
                    int badgeW = FontManager.MeasureText(qtyBadge, 18) + 12;
                    int badgeX = iconX + iconSize - badgeW - 5;
                    int badgeY = iconY + iconSize - 28;
                    Raylib.DrawRectangleRounded(new Rectangle(badgeX, badgeY, badgeW, 24), 0.2f, 6, new Color(0, 0, 0, 180));
                    FontManager.DrawText(qtyBadge, badgeX + 6, badgeY + 5, 16, Color.Gold);

                    // ===== NOM =====
                    string localizedSelectedName = GetRecipeResultName(selected);
                    int nameY = iconY + iconSize + 15;
                    int nameW = FontManager.MeasureText(localizedSelectedName, 20);
                    FontManager.DrawText(localizedSelectedName, rightPanelX + (rightPanelW - nameW) / 2, nameY, 20, COLOR_ACCENT);
                    
                    int separatorY = nameY + 28;
                    Raylib.DrawLine(rightPanelX + 15, separatorY, rightPanelX + rightPanelW - 15, separatorY, new Color(80, 70, 50, 120));
                    
                    // ===== INGRÉDIENTS =====
                    int ingredientsTitleY = separatorY + 10;
                    FontManager.DrawText(Localization.Get("craft.ingredients_required"), rightPanelX + 15, ingredientsTitleY, 15, new Color(180, 180, 160, 255));

                    int ingY = ingredientsTitleY + 29;
                    int effectiveQuantity = Math.Max(1, _craftQuantity);

                    if (selected.Ingredients != null && selected.Ingredients.Count > 0)
                    {
                        foreach (var (id, needQty) in selected.Ingredients)
                        {
                            if (GameData.ItemDatabase.TryGetValue(id, out var ingItem))
                            {
                                int have = Program.GetTotalItemCount(ingItem.Name);
                                int needed = needQty * effectiveQuantity;
                                bool enough = have >= needed;
                                Color qtyColor = enough ? new Color(100, 200, 100, 255) : new Color(255, 100, 100, 255);
                                
                                var ingredientSlot = InventoryRenderer.InventorySlots.FirstOrDefault(s => s.Item != null && s.Item.Name == ingItem.Name);
                                if (ingredientSlot != null && ingredientSlot.Item != null)
                                {
                                    ItemRenderer.DrawItemPadded(ingredientSlot.Item, rightPanelX + 15, ingY, 48, 0);
                                }
                                else
                                {
                                    var tempIngredient = new Item(ingItem.Name, 1, ingItem.GetColor(), ingItem.Icon);
                                    ItemRenderer.DrawItemPadded(tempIngredient, rightPanelX + 15, ingY, 48, 0);
                                }
                                
                                string ingText = Localization.GetLocalizedItemName(ingItem.ID);
                                int ingredientTextX = rightPanelX + 72;
                                FontManager.DrawText(ingText, ingredientTextX, ingY + 3, 15, Color.White);
                                
                                string qtyText = $"{have} / {needed}";
                                int qtyTextW = FontManager.MeasureText(qtyText, 15);
                                FontManager.DrawText(qtyText, rightPanelX + rightPanelW - qtyTextW - 15, ingY + 3, 15, qtyColor);
                                
                                float progress = Math.Clamp((float)have / Math.Max(1, needed), 0f, 1f);
                                int barWidth = rightPanelW - 102;
                                int barX = ingredientTextX;
                                int barY = ingY + 29;
                                DrawIngredientProgressBar(new Rectangle(barX, barY, barWidth, 8), progress);
                                
                                ingY += 66;
                            }
                        }
                    }
                    else
                    {
                        int msgY = separatorY + 20;
                        FontManager.DrawText(Localization.Get("craft.no_ingredients"), rightPanelX + 15, msgY, 13, new Color(100, 200, 100, 255));
                    }
                    
                    // ===== OPTIONS DE CRAFT (manteau ou motif du sol) =====
                    bool isCoat = IsCoatRecipe(selected);
                    bool isFloor = IsFloorRecipe(selected);
                    int optionsHeight = (isCoat || isFloor) ? 58 : 0;
                    if (isCoat)
                    {
                        int optY = rightPanelY + rightPanelH - 45 - optionsHeight - 8;
                        FontManager.DrawText(Localization.Get("craft.coat_options", "Options du manteau"), rightPanelX + 15, optY, 12, new Color(180, 180, 160, 255));

                        // Cycle: short -> long -> none
                        string[] sleeveLabels = { Localization.Get("craft.coat_sleeves_short", "Manches : courtes"), Localization.Get("craft.coat_sleeves_long", "Manches : longues"), Localization.Get("craft.coat_sleeves_none", "Manches : aucune") };
                        string sleeveValue = _coatMeta.TryGetValue("sleeves", out var selectedSleeves) ? selectedSleeves : "short";
                        int sleeveIndex = sleeveValue switch { "long" => 1, "none" => 2, _ => 0 };
                        string sleeveLabel = sleeveLabels[sleeveIndex];
                        Rectangle sleevesBox = new Rectangle(rightPanelX + 15, optY + 20, 220, 18);
                        Color boxColor = Raylib.CheckCollisionPointRec(mousePos, sleevesBox) ? COLOR_CATEGORY_HOVER : COLOR_CATEGORY_NORMAL;
                        Raylib.DrawRectangleRec(sleevesBox, boxColor);
                        FontManager.DrawText(sleeveLabel, (int)sleevesBox.X + 6, (int)sleevesBox.Y + 1, 12, Color.White);
                        if (Raylib.CheckCollisionPointRec(mousePos, sleevesBox) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                            _coatMeta["sleeves"] = sleeveIndex switch { 0 => "long", 1 => "none", _ => "short" };

                        string styleValue = _coatMeta.TryGetValue("opening", out var selectedStyle) ? selectedStyle : "closed";
                        int styleIndex = Array.IndexOf(_coatStyles, styleValue);
                        if (styleIndex < 0) styleIndex = 0;
                        string[] styleLabels =
                        {
                            Localization.Get("craft.coat_style_closed", "Style : fermé"),
                            Localization.Get("craft.coat_style_open", "Style : ouvert"),
                            Localization.Get("craft.coat_style_lace_up", "Style : lacé")
                        };
                        Rectangle styleBox = new Rectangle(rightPanelX + 15, optY + 42, 220, 18);
                        Color styleBoxColor = Raylib.CheckCollisionPointRec(mousePos, styleBox) ? COLOR_CATEGORY_HOVER : COLOR_CATEGORY_NORMAL;
                        Raylib.DrawRectangleRec(styleBox, styleBoxColor);
                        FontManager.DrawText(styleLabels[styleIndex], (int)styleBox.X + 6, (int)styleBox.Y + 1, 12, Color.White);
                        if (Raylib.CheckCollisionPointRec(mousePos, styleBox) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                            _coatMeta["opening"] = _coatStyles[(styleIndex + 1) % _coatStyles.Length];
                    }
                            else if (isFloor)
                            {
                            int optY = rightPanelY + rightPanelH - 45 - optionsHeight - 8;
                            FontManager.DrawText("Motif du sol", rightPanelX + 15, optY, 12, new Color(180, 180, 160, 255));

                            string[] floorLabels = { "Zigzag", "Tuiles", "Carres", "Planches", "Plat" };
                            int floorIndex = Array.IndexOf(_floorStyles, _selectedFloorStyle);
                            if (floorIndex < 0) floorIndex = 0;
                            Rectangle floorBox = new Rectangle(rightPanelX + 15, optY + 20, 220, 18);
                            Color floorBoxColor = Raylib.CheckCollisionPointRec(mousePos, floorBox) ? COLOR_CATEGORY_HOVER : COLOR_CATEGORY_NORMAL;
                            Raylib.DrawRectangleRec(floorBox, floorBoxColor);
                            FontManager.DrawText($"Motif : {floorLabels[floorIndex]}", (int)floorBox.X + 6, (int)floorBox.Y + 1, 12, Color.White);
                            if (Raylib.CheckCollisionPointRec(mousePos, floorBox) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                                _selectedFloorStyle = _floorStyles[(floorIndex + 1) % _floorStyles.Length];
                            }

                    // ===== BOUTONS =====
                    int btnAreaY = rightPanelY + rightPanelH - 45;
                    int btnAreaW = rightPanelW - 30;
                    int btnAreaX = rightPanelX + 15;
                    
                    int smallBtnW = 40;
                    int bigBtnW = btnAreaW - smallBtnW * 2 - 10;
                    
                    Rectangle minusBtn = new Rectangle(btnAreaX, btnAreaY, smallBtnW, 36);
                    bool hoverMinus = Raylib.CheckCollisionPointRec(mousePos, minusBtn);
                    bool canMinus = maxCraft > 1 && _craftQuantity > 1;
                    
                    Color minusColor = canMinus ? (hoverMinus ? new Color(70, 60, 50, 255) : new Color(50, 45, 40, 200)) : new Color(40, 40, 40, 120);
                    Raylib.DrawRectangleRounded(minusBtn, 0.2f, 6, minusColor);
                    Raylib.DrawRectangleRoundedLines(minusBtn, 0.2f, 6, 1, canMinus ? new Color(100, 90, 70, 150) : new Color(60, 60, 60, 100));
                    FontManager.DrawText("-", (int)minusBtn.X + 15, (int)minusBtn.Y + 8, 18, canMinus ? Color.White : new Color(100, 100, 100, 150));
                    
                    if (canMinus && hoverMinus && Raylib.IsMouseButtonPressed(MouseButton.Left))
                        _craftQuantity--;
                    
                    Rectangle craftBtn = new Rectangle(btnAreaX + smallBtnW + 5, btnAreaY, bigBtnW, 36);
                    bool canCraft = maxCraft >= _craftQuantity && _craftQuantity > 0;
                    bool hoverCraft = Raylib.CheckCollisionPointRec(mousePos, craftBtn);
                    
                    Color btnColor = canCraft ? (hoverCraft ? COLOR_CRAFT_BTN_HOVER : COLOR_CRAFT_BTN) : COLOR_CRAFT_BTN_DISABLED;
                    Raylib.DrawRectangleRounded(craftBtn, 0.2f, 6, btnColor);
                    Raylib.DrawRectangleRoundedLines(craftBtn, 0.2f, 6, 1, canCraft ? new Color(100, 200, 100, 150) : new Color(150, 80, 80, 100));
                    
                    string craftText = canCraft ? Localization.Get("craft.button.craft", _craftQuantity) : Localization.Get("craft.button.missing");
                    int tw = FontManager.MeasureText(craftText, 15);
                    FontManager.DrawText(craftText, (int)(craftBtn.X + craftBtn.Width / 2 - tw / 2), (int)(craftBtn.Y + 11), 15, canCraft ? Color.White : new Color(200, 200, 200, 180));
                    
                    if (canCraft && hoverCraft && Raylib.IsMouseButtonPressed(MouseButton.Left))
                        TryCraft();
                    
                    Rectangle plusBtn = new Rectangle(btnAreaX + smallBtnW + 5 + bigBtnW + 5, btnAreaY, smallBtnW, 36);
                    bool hoverPlus = Raylib.CheckCollisionPointRec(mousePos, plusBtn);
                    bool canPlus = maxCraft > 1 && _craftQuantity < maxCraft;
                    
                    Color plusColor = canPlus ? (hoverPlus ? new Color(70, 60, 50, 255) : new Color(50, 45, 40, 200)) : new Color(40, 40, 40, 120);
                    Raylib.DrawRectangleRounded(plusBtn, 0.2f, 6, plusColor);
                    Raylib.DrawRectangleRoundedLines(plusBtn, 0.2f, 6, 1, canPlus ? new Color(100, 90, 70, 150) : new Color(60, 60, 60, 100));
                    FontManager.DrawText("+", (int)plusBtn.X + 13, (int)plusBtn.Y + 8, 18, canPlus ? Color.White : new Color(100, 100, 100, 150));
                    
                    if (canPlus && hoverPlus && Raylib.IsMouseButtonPressed(MouseButton.Left))
                        _craftQuantity++;
                    
                    int qtyIndicatorY = btnAreaY - 22;
                    string qtyIndicator;
                    if (maxCraft <= 0)
                        qtyIndicator = Localization.Get("craft.insufficient_ingredients");
                    else
                        qtyIndicator = Localization.Get("craft.quantity", _craftQuantity, maxCraft);
                    
                    int qtyIndW = FontManager.MeasureText(qtyIndicator, 12);
                    FontManager.DrawText(qtyIndicator, rightPanelX + (rightPanelW - qtyIndW) / 2, qtyIndicatorY, 12, 
                        maxCraft <= 0 ? new Color(255, 100, 100, 255) : COLOR_ACCENT);
                }
            }
            else
            {
                int msgY = rightPanelY + rightPanelH / 2 - 20;
                FontManager.DrawText(Localization.Get("craft.select_recipe"), rightPanelX + rightPanelW / 2 - 90, msgY, 14, new Color(150, 150, 140, 200));
                FontManager.DrawText(Localization.Get("craft.select_recipe_hint"), rightPanelX + rightPanelW / 2 - 85, msgY + 25, 13, new Color(130, 130, 120, 180));
            }
            
            string shortcutText = Localization.Get("craft.shortcuts");
            int stw = FontManager.MeasureText(shortcutText, 11);
            FontManager.DrawText(shortcutText, x + WINDOW_WIDTH / 2 - stw / 2, y + WINDOW_HEIGHT - 18, 11, new Color(120, 110, 90, 180));
        }
        
        private static void DrawRecipeButtonBackground(Rectangle destination, Color tint)
        {
            // Les morceaux sont agrandis uniformément, puis le centre est étiré uniquement en largeur.
            float textureScale = Math.Min(3f, destination.Height / _recipeCenter.Height);
            float textureHeight = _recipeCenter.Height * textureScale;
            float textureY = destination.Y + (destination.Height - textureHeight) / 2f;
            float leftWidth = Math.Min(_recipeLeft.Width * textureScale, destination.Width / 2f);
            float rightWidth = Math.Min(_recipeRight.Width * textureScale, destination.Width / 2f);
            float centerWidth = Math.Max(0, destination.Width - leftWidth - rightWidth);

            Raylib.DrawTexturePro(
                _recipeLeft,
                new Rectangle(0, 0, _recipeLeft.Width, _recipeLeft.Height),
                new Rectangle(destination.X, textureY, leftWidth, textureHeight),
                Vector2.Zero, 0f, tint);
            Raylib.DrawTexturePro(
                _recipeCenter,
                new Rectangle(0, 0, _recipeCenter.Width, _recipeCenter.Height),
                new Rectangle(destination.X + leftWidth, textureY, centerWidth, textureHeight),
                Vector2.Zero, 0f, tint);
            Raylib.DrawTexturePro(
                _recipeRight,
                new Rectangle(0, 0, _recipeRight.Width, _recipeRight.Height),
                new Rectangle(destination.X + destination.Width - rightWidth, textureY, rightWidth, textureHeight),
                Vector2.Zero, 0f, tint);
        }

        private static void DrawIngredientProgressBar(Rectangle destination, float progress)
        {
            bool hasTextures = _barFullMid.Id != 0 && _barEmptyMid.Id != 0 && _barEmptyBorder.Id != 0;
            if (!hasTextures)
            {
                Raylib.DrawRectangle((int)destination.X, (int)destination.Y, (int)destination.Width, (int)destination.Height, new Color(40, 43, 48, 255));
                Raylib.DrawRectangle((int)destination.X, (int)destination.Y, (int)(destination.Width * progress), (int)destination.Height, new Color(100, 180, 100, 255));
                return;
            }

            bool isEmpty = progress <= 0f;
            bool isFull = progress >= 1f;
            Texture2D border = isFull ? _barFullBorder : _barEmptyBorder;
            Texture2D mid = isFull ? _barFullMid : _barEmptyMid;

            DrawBarTexture(border, destination, destination.X, 1);
            if (isEmpty || isFull)
            {
                DrawBarTexture(mid, destination, destination.X + 1, destination.Width - 2);
                return;
            }

            float innerX = destination.X + 1;
            float innerWidth = Math.Max(0, destination.Width - 2);
            DrawBarTexture(_barEmptyMid, destination, innerX, innerWidth);

            float filledWidthTotal = innerWidth * progress;
            float leftWidth = Math.Min(_barFullLeft.Width, filledWidthTotal);
            float filledWidth = Math.Max(0, filledWidthTotal - leftWidth);
            DrawBarTexture(_barFullLeft, destination, innerX, leftWidth);
            DrawBarTexture(_barFullMid, destination, innerX + leftWidth, filledWidth);

            float rightWidth = Math.Min(_barEmptyRight.Width, innerWidth - filledWidthTotal);
            DrawBarTexture(_barEmptyRight, destination, destination.X + destination.Width - rightWidth, rightWidth);
        }

        private static void DrawBarTexture(Texture2D texture, Rectangle destination, float x, float width)
        {
            if (texture.Id == 0 || width <= 0) return;
            Raylib.DrawTexturePro(
                texture,
                new Rectangle(0, 0, texture.Width, texture.Height),
                new Rectangle(x, destination.Y, width, destination.Height),
                Vector2.Zero,
                0f,
                Color.White);
        }

            private static Rectangle GetCategoryTabRect(float windowX, float windowY, int index)
            {
                return new Rectangle(
                windowX + 10 + index * (TAB_HEIGHT + TAB_GAP),
                windowY + TAB_TOP_OFFSET,
                TAB_HEIGHT,
                TAB_HEIGHT);
            }

        private static Rectangle DrawCraftCheckbox(float x, float y, bool isChecked, string label, Vector2 mousePos)
        {
            int boxSize = 14;
            int textWidth = FontManager.MeasureText(label, 11);
            Rectangle hitbox = new Rectangle(x, y, boxSize + 8 + textWidth, boxSize);
            bool hover = Raylib.CheckCollisionPointRec(mousePos, hitbox);

            Rectangle box = new Rectangle(x, y, boxSize, boxSize);
            Color boxColor = hover ? new Color(65, 72, 82, 255) : new Color(42, 46, 52, 230);
            Raylib.DrawRectangleRounded(box, 0.2f, 6, boxColor);
            Raylib.DrawRectangleRoundedLines(box, 0.2f, 6, 1, new Color(120, 110, 90, 180));

            if (isChecked)
            {
                Rectangle fill = new Rectangle(x + 3, y + 3, boxSize - 6, boxSize - 6);
                Raylib.DrawRectangleRounded(fill, 0.2f, 4, new Color(210, 180, 100, 255));
            }

            FontManager.DrawText(label, (int)x + boxSize + 8, (int)y - 1, 11, Color.White);
            return hitbox;
        }

        public static int GetWindowX() => (int)(_windowPosByPlayer.TryGetValue(0, out var pos) ? pos.X : 300);
        public static int GetWindowY() => (int)(_windowPosByPlayer.TryGetValue(0, out var pos) ? pos.Y : 150);
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;
    }
}