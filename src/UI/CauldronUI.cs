// CauldronUI.cs - Version améliorée avec recettes apprises et rendu moderne
#nullable enable
using Raylib_cs;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static class CauldronUI
    {
        private static bool _isOpen = false;
        private static readonly Dictionary<int, bool> _isOpenByPlayer = new();
        private static readonly Dictionary<int, Vector2> _windowPosByPlayer = new();
        private static readonly Dictionary<int, Vector2> _dragOffsetByPlayer = new();
        private static readonly Dictionary<int, bool> _isDraggingWindowByPlayer = new();
        private static readonly Dictionary<int, bool> _isHoveringTitleBarByPlayer = new();
        
        private static DraggedItem _sharedDraggedItem = null!;
        private static Dictionary<Item, int> _mixture = new Dictionary<Item, int>();
        private static Item? _fillContainer;
        
        // Recettes apprises — clé = CraftRecipe.Id (l'id du recipes.json), PAS ResultId
        // (qui est partagé par toutes les potions puisqu'elles produisent le même item bocal).
        private static HashSet<int> _learnedRecipes = new HashSet<int>();
        private static List<CraftRecipe> _cauldronRecipes = new List<CraftRecipe>();
        private static int _selectedRecipeIndex = -1;
        private static int _recipeListScroll = 0;
        private static float _recipeUnlockAnimation = 0f;
        private static string _unlockMessage = "";
        private static float _unlockMessageTimer = 0f;
        
        // UI dimensions
        private static int WINDOW_WIDTH => UIManager.ScaleInt(720);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(560);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(50);
        private static int CAULDRON_SIZE => UIManager.ScaleInt(280);
        private static int RECIPE_LIST_WIDTH => UIManager.ScaleInt(260);
        private static int RECIPE_ITEM_HEIGHT => UIManager.ScaleInt(52);
        
        // Couleurs
        private static readonly Color COLOR_BG = new Color(20, 22, 30, 245);
        private static readonly Color COLOR_HEADER = new Color(30, 33, 45, 255);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_ACCENT_DIM = new Color(160, 140, 80, 180);
        private static readonly Color COLOR_SLOT_NORMAL = new Color(50, 53, 60, 255);
        private static readonly Color COLOR_SLOT_HOVER = new Color(70, 73, 80, 255);
        private static readonly Color COLOR_BUTTON = new Color(80, 140, 80, 255);
        private static readonly Color COLOR_BUTTON_HOVER = new Color(100, 180, 100, 255);
        private static readonly Color COLOR_BUTTON_DISABLED = new Color(60, 70, 60, 200);
        private static readonly Color COLOR_RECIPE_BG = new Color(40, 43, 52, 220);
        private static readonly Color COLOR_RECIPE_HOVER = new Color(60, 63, 72, 220);
        private static readonly Color COLOR_RECIPE_SELECTED = new Color(210, 180, 100, 80);
        private static readonly Color COLOR_RECIPE_LEARNED = new Color(100, 200, 100, 60);
        private static readonly Color COLOR_RECIPE_UNKNOWN = new Color(60, 50, 50, 120);
        private static readonly Color COLOR_RECIPE_LOCKED = new Color(40, 40, 50, 100);
        
        // Textures
        private static Texture2D _cauldronBack = default;
        private static Texture2D _cauldronFront = default;
        private static Texture2D _dropZoneIcon = default;
        private static Texture2D _recipeBookIcon = default;
        private static Texture2D _lockIcon = default;
        private static Texture2D _checkIcon = default;
        private static Texture2D _unknownIcon = default;
        private static bool _texturesLoaded = false;
        
        // Drop zone rectangle
        private static Rectangle _dropZoneRect;
        private static Rectangle _fillContainerRect;
        private static Rectangle _floatArea;
        
        // Objets flottants
        private static List<FloatingIngredient> _floatingItems = new List<FloatingIngredient>();
        private static float _globalTime = 0f;
        
        private class FloatingIngredient
        {
            public Item Item = null!;
            public Vector2 Position;
            public float AngleX;
            public float AngleY;
            public float RotAngle;
            public float RotSpeed;
            public float RotAmplitude;
            public float OffsetX;
            public float OffsetY;
            public float SpeedX;
            public float SpeedY;
            public float RadiusX;
            public float RadiusY;
            public float Rotation;
        }
        
        public static bool IsOpen => _isOpen;
        public static bool IsOpenForPlayer(int playerIndex)
        {
            if (!_isOpenByPlayer.ContainsKey(playerIndex)) _isOpenByPlayer[playerIndex] = false;
            return _isOpenByPlayer[playerIndex];
        }
        
        public static int GetWindowX(int playerIndex = 0) => (int)(_windowPosByPlayer.TryGetValue(playerIndex, out var pos) ? pos.X : 200);
        public static int GetWindowY(int playerIndex = 0) => (int)(_windowPosByPlayer.TryGetValue(playerIndex, out var pos) ? pos.Y : 150);
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;
        
        public static void Initialize(DraggedItem sharedDraggedItem)
        {
            _sharedDraggedItem = sharedDraggedItem;
            LoadTextures();
            LoadCauldronRecipes();
            LoadLearnedRecipes();
        }
        
        private static void LoadCauldronRecipes()
        {
            _cauldronRecipes = GameData.Recipes
                .Where(r => r.RequiresStation == "cauldron")
                .ToList();
        }
        
        private static void LoadLearnedRecipes()
        {
            _learnedRecipes.Clear();
            
            //  CHARGER DEPUIS LE SYSTÈME DE RECETTES
            var discovered = RecipeSystem.GetDiscoveredRecipes("cauldron");
            foreach (var recipe in discovered)
            {
                _learnedRecipes.Add(recipe.TargetRecipeId); // TargetRecipeId == CraftRecipe.Id du recipes.json
            }
        }
        
        private static void LoadTextures()
        {
            if (_texturesLoaded) return;
            _cauldronBack = LoadTextureOrDefault("assets/gui/cauldron_back.png");
            _cauldronFront = LoadTextureOrDefault("assets/gui/cauldron_front.png");
            _dropZoneIcon = LoadTextureOrDefault("assets/gui/drop_zone.png");
            _recipeBookIcon = LoadTextureOrDefault("assets/gui/recipe_book.png");
            _lockIcon = LoadTextureOrDefault("assets/gui/lock.png");
            _checkIcon = LoadTextureOrDefault("assets/gui/check.png");
            _unknownIcon = LoadTextureOrDefault("assets/gui/unknown.png");
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
            if (_cauldronBack.Id != 0) Raylib.UnloadTexture(_cauldronBack);
            if (_cauldronFront.Id != 0) Raylib.UnloadTexture(_cauldronFront);
            if (_dropZoneIcon.Id != 0) Raylib.UnloadTexture(_dropZoneIcon);
            if (_recipeBookIcon.Id != 0) Raylib.UnloadTexture(_recipeBookIcon);
            if (_lockIcon.Id != 0) Raylib.UnloadTexture(_lockIcon);
            if (_checkIcon.Id != 0) Raylib.UnloadTexture(_checkIcon);
            if (_unknownIcon.Id != 0) Raylib.UnloadTexture(_unknownIcon);
            _texturesLoaded = false;
        }
        
        public static void Open(int playerIndex = 0)
        {
            if (!_texturesLoaded) LoadTextures();
            if (!_windowPosByPlayer.ContainsKey(playerIndex)) _windowPosByPlayer[playerIndex] = new Vector2(200, 150);
            if (!_dragOffsetByPlayer.ContainsKey(playerIndex)) _dragOffsetByPlayer[playerIndex] = Vector2.Zero;
            if (!_isDraggingWindowByPlayer.ContainsKey(playerIndex)) _isDraggingWindowByPlayer[playerIndex] = false;
            if (!_isHoveringTitleBarByPlayer.ContainsKey(playerIndex)) _isHoveringTitleBarByPlayer[playerIndex] = false;
            _isOpenByPlayer[playerIndex] = true;
            _isOpen = true;
            _mixture.Clear();
            _fillContainer = null;
            _floatingItems.Clear();
            _selectedRecipeIndex = -1;
            _recipeListScroll = 0;
            _unlockMessage = "";
            _unlockMessageTimer = 0f;
            _sharedDraggedItem?.EndDrag();
            _globalTime = 0f;
            LoadLearnedRecipes();
            UIManager.PushUI(() => Close(playerIndex), () => IsOpenForPlayer(playerIndex));
        }
        
        public static void Close(int playerIndex = 0)
        {
            _isOpenByPlayer[playerIndex] = false;
            _isOpen = false;
            _mixture.Clear();
            if (_fillContainer != null)
            {
                Program.AddItemToInventory(_fillContainer, 1);
                _fillContainer = null;
            }
            _floatingItems.Clear();
            _sharedDraggedItem?.EndDrag();
            _isDraggingWindowByPlayer[playerIndex] = false;
            _dragOffsetByPlayer[playerIndex] = Vector2.Zero;
            UnloadTextures();
        }
        
        public static void Update(int playerIndex = 0, Rectangle? viewport = null)
        {
            if (!IsOpenForPlayer(playerIndex)) return;
            
            Vector2 mousePos = Raylib.GetMousePosition();
            if (viewport.HasValue)
                mousePos -= new Vector2(viewport.Value.X, viewport.Value.Y);
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            
            var windowPos = _windowPosByPlayer.ContainsKey(playerIndex) ? _windowPosByPlayer[playerIndex] : new Vector2(200, 150);
            windowPos.X = Math.Clamp(windowPos.X, -WINDOW_WIDTH + 100, sw - 100);
            windowPos.Y = Math.Clamp(windowPos.Y, 0, sh - 100);
            _windowPosByPlayer[playerIndex] = windowPos;
            
            int x = (int)windowPos.X;
            int y = (int)windowPos.Y;
            
            Rectangle titleBar = new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT);
            _isHoveringTitleBarByPlayer[playerIndex] = Raylib.CheckCollisionPointRec(mousePos, titleBar);
            if (_isHoveringTitleBarByPlayer[playerIndex] && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDraggingWindowByPlayer[playerIndex] = true;
                _dragOffsetByPlayer[playerIndex] = mousePos - windowPos;
            }
            if (_isDraggingWindowByPlayer[playerIndex] && Raylib.IsMouseButtonDown(MouseButton.Left))
                _windowPosByPlayer[playerIndex] = mousePos - _dragOffsetByPlayer[playerIndex];
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDraggingWindowByPlayer[playerIndex] = false;
            
            // Calcul des zones
            int cauldronX = x + 20;
            int cauldronY = y + HEADER_HEIGHT + 15;
            
            _floatArea = new Rectangle(cauldronX + 20, cauldronY + 15, CAULDRON_SIZE - 40, 80);
            
            int dropWidth = 120;
            int dropHeight = 50;
            int dropX = cauldronX + (CAULDRON_SIZE - dropWidth) / 2;
            int dropY = cauldronY + CAULDRON_SIZE + 15;
            _dropZoneRect = new Rectangle(dropX, dropY, dropWidth, dropHeight);
            _fillContainerRect = new Rectangle(cauldronX + CAULDRON_SIZE + 20, dropY, 100, dropHeight);
            
            // Ajouter un ingrédient depuis un item dragué
            if (_sharedDraggedItem.IsDragging && Raylib.IsMouseButtonReleased(MouseButton.Left))
            {
                if (Raylib.CheckCollisionPointRec(mousePos, _dropZoneRect) && _sharedDraggedItem.Item != null)
                {
                    AddIngredient(_sharedDraggedItem.Item, 1);
                    _sharedDraggedItem.Count--;
                    if (_sharedDraggedItem.Count <= 0)
                        _sharedDraggedItem.EndDrag();
                    return;
                }

                if (Raylib.CheckCollisionPointRec(mousePos, _fillContainerRect) && _sharedDraggedItem.Item != null)
                {
                    AddFillContainer(_sharedDraggedItem.Item);
                    if (_fillContainer != null)
                    {
                        _sharedDraggedItem.Count--;
                        if (_sharedDraggedItem.Count <= 0)
                            _sharedDraggedItem.EndDrag();
                    }
                    return;
                }
            }
            
            // Mise à jour des flottants
            float dt = Raylib.GetFrameTime();
            _globalTime += dt;
            
            if (_unlockMessageTimer > 0)
                _unlockMessageTimer -= dt;
            if (_recipeUnlockAnimation > 0)
                _recipeUnlockAnimation += dt * 3f;
            
            foreach (var fi in _floatingItems)
            {
                fi.AngleX += fi.SpeedX * dt;
                if (fi.AngleX > MathF.PI * 2) fi.AngleX -= MathF.PI * 2;
                fi.AngleY += fi.SpeedY * dt;
                if (fi.AngleY > MathF.PI * 2) fi.AngleY -= MathF.PI * 2;
                fi.RotAngle += fi.RotSpeed * dt;
                if (fi.RotAngle > MathF.PI * 2) fi.RotAngle -= MathF.PI * 2;
                fi.Rotation = MathF.Sin(fi.RotAngle) * fi.RotAmplitude;
            }
            
            // ========== LISTE DES RECETTES ==========
            int listX = x + WINDOW_WIDTH - RECIPE_LIST_WIDTH - 15;
            int listY = y + HEADER_HEIGHT + 15;
            int listW = RECIPE_LIST_WIDTH;
            int listH = WINDOW_HEIGHT - HEADER_HEIGHT - 100;
            
            // Scroll
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0 && Raylib.CheckCollisionPointRec(mousePos, new Rectangle(listX, listY, listW, listH)))
            {
                _recipeListScroll -= (int)(wheel * RECIPE_ITEM_HEIGHT);
                int maxScroll = Math.Max(0, _cauldronRecipes.Count * RECIPE_ITEM_HEIGHT - listH);
                _recipeListScroll = Math.Clamp(_recipeListScroll, 0, maxScroll);
            }
            
            // Sélection de recette
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, new Rectangle(listX, listY, listW, listH)))
            {
                int firstVisible = _recipeListScroll / RECIPE_ITEM_HEIGHT;
                for (int i = firstVisible; i < _cauldronRecipes.Count; i++)
                {
                    int itemY = listY + i * RECIPE_ITEM_HEIGHT - _recipeListScroll;
                    if (itemY + RECIPE_ITEM_HEIGHT < listY || itemY > listY + listH) continue;
                    Rectangle itemRect = new Rectangle(listX + 5, itemY, listW - 10, RECIPE_ITEM_HEIGHT - 2);
                    if (Raylib.CheckCollisionPointRec(mousePos, itemRect))
                    {
                        _selectedRecipeIndex = i;
                        break;
                    }
                }
            }
            
            // Bouton Mélanger
            int btnY = y + WINDOW_HEIGHT - 55;
            int btnW = 160;
            int btnX = x + (WINDOW_WIDTH - btnW) / 2;
            Rectangle validateBtn = new Rectangle(btnX, btnY, btnW, 40);
            bool hoverValidate = Raylib.CheckCollisionPointRec(mousePos, validateBtn);
            bool canMix = _mixture.Count > 0 && _fillContainer != null;
            
            if (canMix && hoverValidate && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                TryCraft();
            }
            
            // Fermeture avec Escape
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                UIManager.PopUI();
        }
        
        private static void AddIngredient(Item item, int quantity)
        {
            if (quantity <= 0) return;
            
            Item? existing = null;
            foreach (var kv in _mixture)
            {
                if (kv.Key.Name == item.Name &&
                    ((kv.Key.CustomColor == null && item.CustomColor == null) ||
                     (kv.Key.CustomColor != null && item.CustomColor != null &&
                      kv.Key.CustomColor.Value.Equals(item.CustomColor.Value))))
                {
                    existing = kv.Key;
                    break;
                }
            }
            
            if (existing != null)
            {
                _mixture[existing] += quantity;
            }
            else
            {
                Item newItem = new Item(item.Name, quantity, item.BaseColor, item.Icon, item.CustomColor);
                _mixture[newItem] = quantity;
            }
            
            for (int i = 0; i < quantity; i++)
            {
                var fi = new FloatingIngredient
                {
                    Item = (existing != null) ? existing : new Item(item.Name, 1, item.BaseColor, item.Icon, item.CustomColor),
                    Position = new Vector2((float)Random.Shared.NextDouble(), (float)Random.Shared.NextDouble()),
                    AngleX = (float)(Random.Shared.NextDouble() * Math.PI * 2),
                    AngleY = (float)(Random.Shared.NextDouble() * Math.PI * 2),
                    RotAngle = (float)(Random.Shared.NextDouble() * Math.PI * 2),
                    RotSpeed = (float)(Random.Shared.NextDouble() * 1.5f + 0.5f),
                    RotAmplitude = (float)(Random.Shared.NextDouble() * 20f + 10f),
                    OffsetX = (float)Random.Shared.NextDouble(),
                    OffsetY = (float)Random.Shared.NextDouble(),
                    SpeedX = (float)(Random.Shared.NextDouble() * 1.2f + 0.4f),
                    SpeedY = (float)(Random.Shared.NextDouble() * 1.2f + 0.4f),
                    RadiusX = (float)(Random.Shared.NextDouble() * 20f + 10f),
                    RadiusY = (float)(Random.Shared.NextDouble() * 8f + 4f),
                    Rotation = 0f
                };
                _floatingItems.Add(fi);
            }
            
            Program.ConsumeItemFromInventory(item, quantity);
        }

        private static void AddFillContainer(Item item)
        {
            if (_fillContainer != null)
            {
                Program.AddNotification(new Notification("Un seul récipient peut être rempli à la fois.", Color.Red, 1.5f));
                return;
            }

            if (!Program.IsEmptyLiquidContainer(item))
            {
                Program.AddNotification(new Notification("Le récipient doit être vide.", Color.Red, 1.5f));
                return;
            }

            _fillContainer = CloneItem(item);
            _fillContainer.Count = 1;
            Program.ConsumeItemFromInventory(item, 1);
        }
        
        private static void TryCraft()
        {
            if (_mixture.Count == 0)
            {
                Program.AddNotification(new Notification("Le chaudron est vide !", Color.Red, 1.5f));
                return;
            }

            if (_fillContainer == null)
            {
                Program.AddNotification(new Notification("Placez un récipient vide dans l'emplacement prévu.", Color.Red, 1.5f));
                return;
            }
            
            Dictionary<int, int> mixtureIds = new Dictionary<int, int>();
            foreach (var kv in _mixture)
            {
                int id = GetItemId(kv.Key.Name);
                if (id != 0)
                    mixtureIds[id] = kv.Value;
            }
            
            CraftRecipe? matchingRecipe = null;
            foreach (var recipe in _cauldronRecipes)
            {
                bool match = true;
                if (mixtureIds.Count != recipe.Ingredients.Count)
                {
                    match = false;
                    continue;
                }
                foreach (var (ingId, ingQty) in recipe.Ingredients)
                {
                    if (!mixtureIds.ContainsKey(ingId) || mixtureIds[ingId] != ingQty)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    matchingRecipe = recipe;
                    break;
                }
            }
            
            if (matchingRecipe != null)
            {
                SoundEffects.PlayCraft();
                var resultItem = CreateFilledContainerItem(_fillContainer, matchingRecipe);
                _fillContainer = null;
                int remaining = Program.AddItemToInventory(resultItem, 1);
                if (remaining > 0)
                {
                    Program.DropCustomItemOnGroundWithColors(
                        Program.GetPlayerPosition(),
                        GetItemId(resultItem.Name),
                        remaining,
                        resultItem.CustomColors,
                        resultItem.Metadata,
                        resultItem.Meta);
                }

                //  Couleur de notif : priorité à la couleur propre à LA RECETTE (chaque
                // potion a la sienne dans resultColor), sinon fallback sur l'item générique.
                Color notificationColor = matchingRecipe.ResultColor
                    ?? (GameData.ItemDatabase.TryGetValue(matchingRecipe.ResultId, out var resultData) ? resultData.Color : Color.White);

                Program.AddItemNotification(resultItem, 1, notificationColor);

                //  APPRENDRE LA RECETTE SI CE N'EST PAS DÉJÀ LE CAS (clé = Id de recette, pas ResultId)
                if (!_learnedRecipes.Contains(matchingRecipe.Id))
                {
                    _learnedRecipes.Add(matchingRecipe.Id);
                    string resultName = GetBottleResultName(matchingRecipe);
                    _unlockMessage = $" Nouvelle recette apprise : {resultName} !";
                    _unlockMessageTimer = 4f;
                    _recipeUnlockAnimation = 0.01f;
                    Program.AddNotification(new Notification($" Recette apprise : {resultName} !", new Color(200, 180, 100, 255), 3f));
                    SaveLearnedRecipes();
                }

                string craftedName = GetBottleResultName(matchingRecipe);
                Program.AddNotification(new Notification($" Mélange réussi ! Votre récipient contient maintenant {craftedName.ToLowerInvariant()}.", new Color(100, 255, 100, 255), 2.5f));
                AchievementManager.Progress(AchievementType.CraftItem, 1);
            }
            else
            {
                var failItem = CreateBottleResultItem(null);
                Program.AddItemToInventory(failItem, 1);
                Program.AddItemNotification(failItem, 1, Color.White);
                Program.AddItemToInventory(_fillContainer, 1);
                _fillContainer = null;
                Program.AddNotification(new Notification(" Mélange étrange... Vous obtenez une bouteille remplie sans effet.", new Color(255, 180, 100, 255), 2.5f));
            }
            
            _mixture.Clear();
            _floatingItems.Clear();
        }
        
        private static string GetRecipeDisplayName(CraftRecipe recipe)
        {
            if (!string.IsNullOrWhiteSpace(recipe.ResultName))
                return recipe.ResultName;

            if (GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var resultData))
                return Localization.GetLocalizedItemName(resultData.ID);

            return Localization.GetLocalizedItemName(recipe.ResultId);
        }

        private static Item CreateBottleResultItem(CraftRecipe? recipe)
        {
            int bottleItemId = Program.GetItemId("Petite bouteille");
            //  La couleur visuelle du bocal doit refléter LA POTION (resultColor de la
            // recette), sinon toutes les potions ont la couleur générique du bocal vide.
            Color bottleColor = recipe?.ResultColor ?? Color.White;
            if (GameData.ItemDatabase.TryGetValue(bottleItemId, out var bottleData))
            {
                bottleColor = recipe?.ResultColor ?? bottleData.Color;
                var bottleItem = new Item(bottleData.Name, 1, bottleColor, bottleData.Icon);
                bottleItem.Metadata = BuildBottleMetadata(recipe);
                return bottleItem;
            }

            var fallbackBottle = new Item("Petite bouteille", 1, bottleColor, new Texture2D());
            fallbackBottle.Metadata = BuildBottleMetadata(recipe);
            return fallbackBottle;
        }

        private static Item CreateFilledContainerItem(Item container, CraftRecipe recipe)
        {
            Item filled = CloneItem(container);
            filled.Count = 1;
            filled.Metadata = BuildContainerPotionMetadata(recipe, container);
            return filled;
        }

        private static string BuildContainerPotionMetadata(CraftRecipe recipe, Item container)
        {
            string metadata = BuildBottleMetadata(recipe);
            int capacity = Program.GetWaterCapacityForItem(container);
            if (capacity <= 0)
                capacity = 1;
            return $"{metadata};doses={capacity}";
        }

        private static Item CloneItem(Item source)
        {
            var clone = new Item(source.Name, source.Count, source.BaseColor, source.Icon, source.CustomColor)
            {
                Metadata = source.Metadata,
                Container = source.Container,
                Backpack = source.Backpack,
                Meta = new Dictionary<string, string>(source.Meta, StringComparer.OrdinalIgnoreCase),
                CustomColors = new List<Color?>(source.CustomColors)
            };
            return clone;
        }

        private static string GetBottleResultName(CraftRecipe? recipe)
        {
            if (recipe == null)
                return "bouteille inconnue";

            //  Priorité au nom PROPRE À LA RECETTE (chaque potion a le sien dans
            // resultName). Le fallback sur ItemDatabase renverrait toujours le même nom
            // générique de bocal puisque toutes les potions partagent le même ResultId.
            if (!string.IsNullOrWhiteSpace(recipe.ResultName))
                return recipe.ResultName;

            if (GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var resultData))
                return resultData.Name;

            return "bouteille";
        }

        private static string BuildBottleMetadata(CraftRecipe? recipe)
        {
            //  Système à jour, basé sur les données (recipes.json) plutôt que sur des ID
            // ou des noms codés en dur : chaque recette porte déjà sa propre métadonnée
            // d'effet ("heal:20", "effect:speed:30", "effect:strength:60", ...).
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.ResultMetadata))
                return "effect:none";

            return ItemRenderer.NormalizePotionMetadata(recipe.ResultMetadata);
        }

        private static void SaveLearnedRecipes()
        {
            // Sauvegarder dans le système de recettes
            foreach (var recipeId in _learnedRecipes)
            {
                var recipeData = RecipeSystem.GetAllRecipes().FirstOrDefault(r => r.TargetRecipeId == recipeId && r.RequiresStation == "cauldron");
                if (recipeData != null && !recipeData.IsDiscovered)
                {
                    recipeData.IsDiscovered = true;
                    recipeData.DiscoveredAt = DateTime.Now;
                }
            }
            RecipeSystem.SaveUnlockedRecipes();
        }
        
        private static int GetItemId(string name)
        {
            return GameData.GetItemId(name);
        }
        
        private static bool IsRecipeLearned(CraftRecipe recipe)
        {
            return _learnedRecipes.Contains(recipe.Id);
        }
        
        //  MÉTHODES PUBLIQUES POUR LE SYSTÈME DE RECETTES
        
        public static void LearnRecipe(int recipeId)
        {
            if (!_learnedRecipes.Contains(recipeId))
            {
                _learnedRecipes.Add(recipeId);
                
                var recipe = _cauldronRecipes.FirstOrDefault(r => r.Id == recipeId);
                if (recipe != null)
                {
                    _unlockMessage = $" {recipe.ResultName} débloquée !";
                    _unlockMessageTimer = 3f;
                    _recipeUnlockAnimation = 0.01f;
                    SaveLearnedRecipes();
                    Program.AddNotification(new Notification($" Nouvelle recette : {recipe.ResultName}", new Color(100, 255, 200, 255), 2.5f));
                }
            }
        }
        
        public static void ResetLearnedRecipes()
        {
            _learnedRecipes.Clear();
            SaveLearnedRecipes();
            Program.AddNotification(new Notification(" Toutes les recettes du chaudron ont été réinitialisées", new Color(200, 200, 100, 255), 2f));
        }
        
        public static void UnlockAllCauldronRecipes()
        {
            foreach (var recipe in _cauldronRecipes)
            {
                if (!_learnedRecipes.Contains(recipe.Id))
                {
                    _learnedRecipes.Add(recipe.Id);
                }
            }
            SaveLearnedRecipes();
            Program.AddNotification(new Notification($" {_learnedRecipes.Count} recettes du chaudron débloquées !", new Color(100, 255, 200, 255), 3f));
        }
        
        public static void Draw(int playerIndex = 0, Rectangle? viewport = null)
        {
            if (!IsOpenForPlayer(playerIndex)) return;
            
            var windowPos = _windowPosByPlayer.ContainsKey(playerIndex) ? _windowPosByPlayer[playerIndex] : new Vector2(200, 150);
            int x = (int)windowPos.X;
            int y = (int)windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();
            if (viewport.HasValue)
                mousePos -= new Vector2(viewport.Value.X, viewport.Value.Y);
            
            // ===== PANEAU PRINCIPAL =====
            Raylib.DrawRectangleRounded(new Rectangle(x + 4, y + 4, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, new Color(0, 0, 0, 120));
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, COLOR_BG);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, 2, COLOR_BORDER);
            
            // ===== BARRE DE TITRE =====
            Color titleColor = _isHoveringTitleBarByPlayer[playerIndex] ? new Color(80, 70, 50, 200) : COLOR_HEADER;
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, titleColor);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, 1, COLOR_ACCENT);
            
            FontManager.DrawText(" CHAUDRON", x + 15, y + 14, 18, Color.White);
            FontManager.DrawText($"Recettes: {_learnedRecipes.Count}/{_cauldronRecipes.Count}", x + 200, y + 16, 13, COLOR_ACCENT_DIM);
            
            // Bouton fermer
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 35, y + 10, 25, 25);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeBtn.X + 8, (int)closeBtn.Y + 5, 16, hoverClose ? Color.Red : new Color(200, 120, 120, 200));
            if (hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
                UIManager.PopUI();
            
            // ===== MESSAGE DE DÉVERROUILLAGE =====
            if (_unlockMessageTimer > 0)
            {
                float alpha = Math.Min(1f, _unlockMessageTimer);
                int alphaByte = (int)(alpha * 255);
                int msgX = x + WINDOW_WIDTH / 2 - FontManager.MeasureText(_unlockMessage, 16) / 2;
                int msgY = y + HEADER_HEIGHT + 8;
                Raylib.DrawRectangleRounded(new Rectangle(msgX - 15, msgY - 6, FontManager.MeasureText(_unlockMessage, 16) + 30, 32), 0.2f, 6, new Color(0, 0, 0, (int)(alpha * 180)));
                FontManager.DrawText(_unlockMessage, msgX, msgY + 4, 16, new Color(255, 220, 100, alphaByte));
            }
            
            // ===== ZONE DU CHAUDRON =====
            int cauldronX = x + 20;
            int cauldronY = y + HEADER_HEIGHT + 15;
            
            if (_cauldronBack.Id != 0)
                Raylib.DrawTexturePro(_cauldronBack, new Rectangle(0, 0, _cauldronBack.Width, _cauldronBack.Height),
                    new Rectangle(cauldronX, cauldronY, CAULDRON_SIZE, CAULDRON_SIZE), Vector2.Zero, 0, Color.White);
            else
                Raylib.DrawRectangleRounded(new Rectangle(cauldronX, cauldronY, CAULDRON_SIZE, CAULDRON_SIZE), 0.1f, 8, new Color(60, 40, 30, 255));
            
            // ===== ITEMS FLOTTANTS =====
            float centerX = _floatArea.X + _floatArea.Width / 2f;
            float centerY = _floatArea.Y + _floatArea.Height / 2f;
            
            foreach (var fi in _floatingItems)
            {
                float offsetX = MathF.Sin(fi.AngleX) * fi.RadiusX;
                float offsetY = MathF.Sin(fi.AngleY) * fi.RadiusY;
                
                float drawX = Math.Clamp(centerX + offsetX, _floatArea.X + 8, _floatArea.X + _floatArea.Width - 8);
                float drawY = Math.Clamp(centerY + offsetY, _floatArea.Y + 8, _floatArea.Y + _floatArea.Height - 8);
                
                float size = UIManager.Scale(28f);
                Rectangle dest = new Rectangle(drawX - size/2, drawY - size/2, size, size);
                if (fi.Item.Icon.Id != 0)
                    Raylib.DrawTexturePro(fi.Item.Icon, new Rectangle(0, 0, fi.Item.Icon.Width, fi.Item.Icon.Height),
                        dest, Vector2.Zero, fi.Rotation, Color.White);
                else
                    Raylib.DrawRectangleRounded(dest, 0.1f, 6, fi.Item.DisplayColor);
            }
            
            if (_cauldronFront.Id != 0)
                Raylib.DrawTexturePro(_cauldronFront, new Rectangle(0, 0, _cauldronFront.Width, _cauldronFront.Height),
                    new Rectangle(cauldronX, cauldronY, CAULDRON_SIZE, CAULDRON_SIZE), Vector2.Zero, 0, Color.White);
            
            // ===== ZONE DE DÉPÔT =====
            bool hoverDrop = Raylib.CheckCollisionPointRec(mousePos, _dropZoneRect);
            Raylib.DrawRectangleRounded(_dropZoneRect, 0.2f, 6, hoverDrop ? COLOR_SLOT_HOVER : COLOR_SLOT_NORMAL);
            Raylib.DrawRectangleRoundedLines(_dropZoneRect, 0.2f, 6, 1, COLOR_BORDER);
            
            if (_dropZoneIcon.Id != 0)
                Raylib.DrawTexturePro(_dropZoneIcon, new Rectangle(0, 0, _dropZoneIcon.Width, _dropZoneIcon.Height),
                    new Rectangle(_dropZoneRect.X + 20, _dropZoneRect.Y + 10, 80, 30), Vector2.Zero, 0, Color.White);
            else
                FontManager.DrawText(" Déposer", (int)_dropZoneRect.X + 25, (int)_dropZoneRect.Y + 15, 12, Color.White);

            // ===== RÉCIPIENT À REMPLIR =====
            bool hoverContainer = Raylib.CheckCollisionPointRec(mousePos, _fillContainerRect);
            Raylib.DrawRectangleRounded(_fillContainerRect, 0.2f, 6, hoverContainer ? COLOR_SLOT_HOVER : COLOR_SLOT_NORMAL);
            Raylib.DrawRectangleRoundedLines(_fillContainerRect, 0.2f, 6, 1, COLOR_BORDER);
            FontManager.DrawText("Récipient", (int)_fillContainerRect.X + 8, (int)_fillContainerRect.Y - 17, 11, COLOR_ACCENT);
            if (_fillContainer != null)
            {
                ItemRenderer.DrawItem(_fillContainer, new Rectangle(
                    _fillContainerRect.X + 25, _fillContainerRect.Y + 5,
                    _fillContainerRect.Width - 50, _fillContainerRect.Height - 10));
            }
            else
            {
                FontManager.DrawText("vide", (int)_fillContainerRect.X + 33, (int)_fillContainerRect.Y + 18, 11, new Color(150, 150, 140, 180));
            }
            
            // ===== INGRÉDIENTS DANS LE CHAUDRON =====
            int ingX = cauldronX + 20;
            int ingY = cauldronY + CAULDRON_SIZE + 70;
            FontManager.DrawText(" Ingrédients:", ingX, ingY, 13, COLOR_ACCENT);
            
            int lineY = ingY + 25;
            if (_mixture.Count == 0)
            {
                FontManager.DrawText("(vide)", ingX + 10, lineY, 12, new Color(150, 150, 140, 180));
            }
            else
            {
                foreach (var kv in _mixture)
                {
                    if (lineY > cauldronY + CAULDRON_SIZE + 130) break;
                    int iconSize = 22;
                    if (kv.Key.Icon.Id != 0)
                        Raylib.DrawTexturePro(kv.Key.Icon, new Rectangle(0, 0, kv.Key.Icon.Width, kv.Key.Icon.Height),
                            new Rectangle(ingX, lineY, iconSize, iconSize), Vector2.Zero, 0, Color.White);
                    else
                        Raylib.DrawRectangleRounded(new Rectangle(ingX, lineY, iconSize, iconSize), 0.1f, 6, kv.Key.DisplayColor);
                    
                    string name = kv.Key.Name.Length > 12 ? kv.Key.Name[..10] + "..." : kv.Key.Name;
                    FontManager.DrawText($"{name} x{kv.Value}", ingX + iconSize + 8, lineY + 4, 11, Color.White);
                    lineY += 24;
                }
            }
            
            // ===== BOUTON MÉLANGER =====
            int btnY = y + WINDOW_HEIGHT - 55;
            int btnW = 160;
            int btnX = x + (WINDOW_WIDTH - btnW) / 2;
            Rectangle validateBtn = new Rectangle(btnX, btnY, btnW, 40);
            bool hoverValidate = Raylib.CheckCollisionPointRec(mousePos, validateBtn);
            bool canMix = _mixture.Count > 0;
            
            Color btnColor = canMix ? (hoverValidate ? COLOR_BUTTON_HOVER : COLOR_BUTTON) : COLOR_BUTTON_DISABLED;
            Raylib.DrawRectangleRounded(validateBtn, 0.2f, 6, btnColor);
            Raylib.DrawRectangleRoundedLines(validateBtn, 0.2f, 6, 1, canMix ? new Color(100, 200, 100, 150) : new Color(80, 80, 80, 100));
            
            string btnText = canMix ? " MÉLANGER" : (_fillContainer == null ? " Ajoutez un récipient" : " Ajoutez des ingrédients");
            int btnTextW = FontManager.MeasureText(btnText, 15);
            FontManager.DrawText(btnText, btnX + (btnW - btnTextW) / 2, btnY + 12, 15, canMix ? Color.White : new Color(150, 150, 150, 180));
            
            // ===== LISTE DES RECETTES =====
            int listX = x + WINDOW_WIDTH - RECIPE_LIST_WIDTH - 15;
            int listY = y + HEADER_HEIGHT + 15;
            int listW = RECIPE_LIST_WIDTH;
            int listH = WINDOW_HEIGHT - HEADER_HEIGHT - 100;
            
            Raylib.DrawRectangleRounded(new Rectangle(listX, listY, listW, listH), 0.1f, 8, new Color(25, 28, 35, 220));
            Raylib.DrawRectangleRoundedLines(new Rectangle(listX, listY, listW, listH), 0.1f, 8, 1, COLOR_BORDER);
            
            FontManager.DrawText(" RECETTES", listX + 12, listY + 8, 13, COLOR_ACCENT);
            FontManager.DrawText($"{_learnedRecipes.Count}/{_cauldronRecipes.Count}", listX + listW - 55, listY + 10, 11, COLOR_ACCENT_DIM);
            
            Raylib.DrawLine(listX + 10, listY + 30, listX + listW - 10, listY + 30, new Color(80, 70, 50, 100));
            
            Raylib.BeginScissorMode(listX + 5, listY + 35, listW - 10, listH - 45);
            
            int firstVisible = _recipeListScroll / RECIPE_ITEM_HEIGHT;
            int lastVisible = Math.Min(_cauldronRecipes.Count, firstVisible + (listH / RECIPE_ITEM_HEIGHT) + 1);
            
            for (int i = firstVisible; i < lastVisible; i++)
            {
                var recipe = _cauldronRecipes[i];
                int itemY = listY + 35 + i * RECIPE_ITEM_HEIGHT - _recipeListScroll;
                if (itemY + RECIPE_ITEM_HEIGHT < listY + 35 || itemY > listY + listH) continue;
                
                bool isLearned = IsRecipeLearned(recipe);
                bool isSelected = (i == _selectedRecipeIndex);
                bool isHovered = Raylib.CheckCollisionPointRec(mousePos, new Rectangle(listX + 8, itemY, listW - 16, RECIPE_ITEM_HEIGHT - 4));
                
                Color bgColor;
                if (isSelected) bgColor = COLOR_RECIPE_SELECTED;
                else if (isHovered) bgColor = COLOR_RECIPE_HOVER;
                else if (isLearned) bgColor = COLOR_RECIPE_LEARNED;
                else bgColor = COLOR_RECIPE_UNKNOWN;
                
                Raylib.DrawRectangleRounded(new Rectangle(listX + 8, itemY, listW - 16, RECIPE_ITEM_HEIGHT - 4), 0.1f, 6, bgColor);
                Raylib.DrawRectangleRoundedLines(new Rectangle(listX + 8, itemY, listW - 16, RECIPE_ITEM_HEIGHT - 4), 0.1f, 6, 1, 
                    isSelected ? COLOR_ACCENT : new Color(60, 55, 45, 100));
                
                Texture2D statusIcon;
                Color statusColor;
                if (isLearned)
                {
                    statusIcon = _checkIcon;
                    statusColor = new Color(100, 255, 100, 255);
                }
                else
                {
                    statusIcon = _lockIcon;
                    statusColor = new Color(200, 150, 100, 180);
                }
                
                if (statusIcon.Id != 0)
                {
                    float iconScale = 16f / statusIcon.Width;
                    Raylib.DrawTextureEx(statusIcon, new Vector2(listX + 14, itemY + 18), 0, iconScale, statusColor);
                }
                else
                {
                    FontManager.DrawText(isLearned ? "" : "", listX + 14, itemY + 15, 14, statusColor);
                }
                
                string displayName = GetRecipeDisplayName(recipe);
                if (displayName.Length > 18) displayName = displayName[..16] + "..";
                FontManager.DrawText(displayName, listX + 40, itemY + 10, 12, isLearned ? Color.White : new Color(150, 150, 150, 180));
                
                string ingPreview = string.Join(" ", recipe.Ingredients.Take(3).Select(ing => 
                    GameData.ItemDatabase.TryGetValue(ing.id, out var d) ? $"•{Localization.GetLocalizedItemName(d.ID)[..Math.Min(4, Localization.GetLocalizedItemName(d.ID).Length)]}" : "?"));
                if (recipe.Ingredients.Count > 3) ingPreview += "…";
                FontManager.DrawText(ingPreview, listX + 40, itemY + 28, 9, isLearned ? new Color(180, 180, 160, 200) : new Color(120, 120, 110, 150));
            }
            
            Raylib.EndScissorMode();
            
            int totalHeight = _cauldronRecipes.Count * RECIPE_ITEM_HEIGHT;
            if (totalHeight > listH - 45)
            {
                int scrollbarX = listX + listW - 12;
                int scrollbarHeight = (int)((float)(listH - 45) / totalHeight * (listH - 45));
                scrollbarHeight = Math.Max(25, scrollbarHeight);
                float scrollPercent = (float)_recipeListScroll / (totalHeight - (listH - 45));
                int scrollbarY = listY + 35 + (int)(scrollPercent * ((listH - 45) - scrollbarHeight));
                
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, listY + 35, 5, listH - 45), 0.2f, 6, new Color(40, 43, 48, 200));
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, scrollbarY, 5, scrollbarHeight), 0.2f, 6, COLOR_ACCENT);
            }
            
            // ===== INFOS DE LA RECETTE SÉLECTIONNÉE =====
            if (_selectedRecipeIndex >= 0 && _selectedRecipeIndex < _cauldronRecipes.Count)
            {
                var recipe = _cauldronRecipes[_selectedRecipeIndex];
                bool isLearned = IsRecipeLearned(recipe);
                
                int detailX = x + 20;
                int detailY = y + HEADER_HEIGHT + CAULDRON_SIZE + 120;
                
                if (isLearned)
                {
                    FontManager.DrawText(" Recette connue", detailX, detailY, 12, new Color(100, 255, 100, 255));

                    string recipeLabel = GetRecipeDisplayName(recipe);
                    string ingList = string.Join(", ", recipe.Ingredients.Select(ing =>
                        GameData.ItemDatabase.TryGetValue(ing.id, out var d) ? $"{Localization.GetLocalizedItemName(d.ID)} x{ing.qty}" : "?"));
                    FontManager.DrawText($"{recipeLabel} → {ingList}", detailX, detailY + 20, 11, new Color(180, 180, 160, 200));
                }
                else
                {
                    FontManager.DrawText(" Recette inconnue", detailX, detailY, 12, new Color(200, 150, 100, 255));
                    FontManager.DrawText("Mélangez les bons ingrédients", detailX, detailY + 20, 11, new Color(150, 150, 140, 180));
                    FontManager.DrawText("pour la débloquer !", detailX, detailY + 36, 11, new Color(150, 150, 140, 180));
                }
            }
            
            string hint = "ESC: Fermer | Déposez des items pour mélanger";
            int hintW = FontManager.MeasureText(hint, 11);
            FontManager.DrawText(hint, x + (WINDOW_WIDTH - hintW) / 2, y + WINDOW_HEIGHT - 14, 11, new Color(120, 110, 90, 180));
        }
    }
}