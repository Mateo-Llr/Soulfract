// CauldronUI.cs - Chaudron : glisser-déposer direct, rendu moderne, recettes apprises
#nullable enable
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public static class CauldronUI
    {
        // =====================================================================
        //  ÉTAT
        // =====================================================================
        private static readonly Dictionary<int, bool> _isOpenByPlayer = new();
        private static readonly Dictionary<int, Vector2> _windowPosByPlayer = new();
        private static readonly Dictionary<int, Vector2> _dragOffsetByPlayer = new();
        private static readonly Dictionary<int, bool> _isDraggingWindowByPlayer = new();
        private static readonly Dictionary<int, bool> _isHoveringTitleBarByPlayer = new();

        private static DraggedItem _sharedDraggedItem = null!;
        private static readonly Dictionary<Item, int> _mixture = new Dictionary<Item, int>();
        private static Item? _fillContainer;

        // Recettes apprises : clé = CraftRecipe.Id (id du recipes.json), PAS ResultId
        // (partagé par toutes les potions puisqu'elles produisent le même item bocal).
        private static readonly HashSet<int> _learnedRecipes = new HashSet<int>();
        private static List<CraftRecipe> _cauldronRecipes = new List<CraftRecipe>();
        private static int _selectedRecipeIndex = -1;
        private static float _recipeListScroll = 0f;
        private static float _recipeScrollTarget = 0f;

        // Notification interne (bandeau "recette apprise")
        private static string _unlockMessage = "";
        private static float _unlockMessageTimer = 0f;
        private static float _unlockMessageDuration = 4f;
        private static int _unlockFlashRecipeId = -1;
        private static float _unlockFlashTimer = 0f;

        // Animation
        private static float _globalTime = 0f;
        private static float _flashTimer = 0f;
        private static Color _flashColor = Color.White;
        private static Vector3 _liquidRgb = new Vector3(40, 66, 78);
        private static float _bubbleTimer = 0f;
        private static float _steamTimer = 0f;

        // =====================================================================
        //  DIMENSIONS / COULEURS
        // =====================================================================
        // Toutes les tailles passent par S() / SF() / FS() : plus aucune valeur "en dur"
        // qui ignore l'échelle de l'UI.
        // NOTE : si FontManager applique déjà l'échelle en interne, remplacer FS par `size`.
        private static int S(int v) => UIManager.ScaleInt(v);
        private static float SF(float v) => UIManager.Scale(v);
        private static int FS(int size) => UIManager.ScaleInt(size);

        private static int WINDOW_WIDTH => S(760);
        private static int WINDOW_HEIGHT => S(600);
        private static int HEADER_HEIGHT => S(54);
        private static int CAULDRON_SIZE => S(300);
        private static int RECIPE_LIST_WIDTH => S(270);
        private static int RECIPE_ITEM_HEIGHT => S(58);

        private static Color C(int r, int g, int b, int a = 255) =>
            new Color((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255),
                      (byte)Math.Clamp(b, 0, 255), (byte)Math.Clamp(a, 0, 255));

        private static readonly Color COLOR_BG = new Color((byte)22, (byte)20, (byte)32, (byte)250);
        private static readonly Color COLOR_HEADER = new Color((byte)38, (byte)32, (byte)56, (byte)255);
        private static readonly Color COLOR_HEADER_HOVER = new Color((byte)48, (byte)41, (byte)68, (byte)255);
        private static readonly Color COLOR_BORDER = new Color((byte)96, (byte)80, (byte)56, (byte)230);
        private static readonly Color COLOR_BORDER_INNER = new Color((byte)255, (byte)255, (byte)255, (byte)10);
        private static readonly Color COLOR_PANEL = new Color((byte)28, (byte)25, (byte)40, (byte)255);
        private static readonly Color COLOR_PANEL_BORDER = new Color((byte)70, (byte)62, (byte)92, (byte)200);
        private static readonly Color COLOR_ACCENT = new Color((byte)226, (byte)190, (byte)106, (byte)255);
        private static readonly Color COLOR_ACCENT_DIM = new Color((byte)170, (byte)148, (byte)92, (byte)200);
        private static readonly Color COLOR_TEXT = new Color((byte)236, (byte)232, (byte)224, (byte)255);
        private static readonly Color COLOR_TEXT_DIM = new Color((byte)150, (byte)145, (byte)160, (byte)200);
        private static readonly Color COLOR_GOOD = new Color((byte)110, (byte)214, (byte)130, (byte)255);
        private static readonly Color COLOR_WARN = new Color((byte)224, (byte)160, (byte)96, (byte)255);
        private static readonly Color COLOR_SLOT_HOVER = new Color((byte)44, (byte)40, (byte)62, (byte)255);
        private static readonly Color COLOR_BUTTON = new Color((byte)70, (byte)138, (byte)92, (byte)255);
        private static readonly Color COLOR_BUTTON_HOVER = new Color((byte)92, (byte)170, (byte)116, (byte)255);
        private static readonly Color COLOR_BUTTON_DISABLED = new Color((byte)44, (byte)46, (byte)54, (byte)230);
        private static readonly Color COLOR_EMPTY_LIQUID = new Color((byte)40, (byte)66, (byte)78, (byte)255);

        // =====================================================================
        //  TEXTURES (toutes optionnelles : un rendu procédural prend le relais)
        // =====================================================================
        private static Texture2D _cauldronBack = default;
        private static Texture2D _cauldronFront = default;
        private static Item _cauldronItem = null!;
        private static ItemData _potionBottleData;
        private static Texture2D _recipeBookIcon = default;
        private static Texture2D _lockIcon = default;
        private static Texture2D _checkIcon = default;
        private static Texture2D _unknownIcon = default;
        private static bool _texturesLoaded = false;

        // Géométrie du chaudron procédural (fractions de sa taille)
        private const float POT_RIM_Y = 0.38f;
        private const float POT_R = 0.33f;
        private const float POT_BELLY_Y = 0.55f;
        private const float POT_LIP_RY = 0.125f;
        private const float POT_INNER_RY = 0.105f;

        // =====================================================================
        //  OBJETS VISUELS : ingrédients flottants + particules
        // =====================================================================
        private const int MAX_FLOATING = 24;
        private const float APPEAR_TIME = 0.38f;

        private static readonly List<FloatingIngredient> _floatingItems = new List<FloatingIngredient>();
        private static readonly List<Particle> _particles = new List<Particle>();

        private class FloatingIngredient
        {
            public Item Item = null!;
            public Vector2 Rest;          // position de repos, dans le disque unité de la surface
            public Vector2 SpawnOffset;   // point de chute (pixels, relatif au centre du liquide)
            public float Appear;          // 0 -> 1
            public float PhaseX, PhaseY, SpeedX, SpeedY;
            public float RotPhase, RotSpeed, RotAmp;
        }

        private enum ParticleKind { Bubble, Steam, Droplet, Ripple, Spark }

        private class Particle
        {
            public ParticleKind Kind;
            public Vector2 Pos;   // pixels, relatif au centre du liquide
            public Vector2 Vel;
            public float Age;     // négatif = délai avant apparition
            public float Life;
            public float Size;
            public Color Tint;
        }

        private struct UILayout
        {
            public Rectangle Window, Header, CloseBtn;
            public Rectangle CauldronArea, DropTarget, ContainerSlot, IngredientPanel;
            public Rectangle MixBtn, ClearBtn;
            public Rectangle RecipePanel, RecipeListInner, DetailPanel;
            public Vector2 LiquidCenter;
            public float LiquidRx, LiquidRy;
        }

        private struct Chip
        {
            public Rectangle Rect;
            public Item? Key;
            public Texture2D Icon;
            public Color Color;
            public string Label;
            public int Qty;
        }

        // =====================================================================
        //  API PUBLIQUE
        // =====================================================================
        public static bool IsOpen => _isOpenByPlayer.Values.Any(v => v);

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
            foreach (var recipe in GameData.Recipes.Where(recipe =>
                recipe.RequiresStation == "cauldron" && RecipeSystem.IsCauldronRecipeLearned(recipe.Id)))
                _learnedRecipes.Add(recipe.Id);
        }

        private static void LoadTextures()
        {
            if (_texturesLoaded) return;
            _cauldronBack = LoadTextureOrDefault("assets/gui/cauldron_back.png");
            _cauldronFront = LoadTextureOrDefault("assets/gui/cauldron_front.png");
            _cauldronItem = LoadVisualItem("cauldron");
            if (!GameData.ItemDatabaseByKey.TryGetValue("glassbottle_small", out _potionBottleData))
                throw new InvalidOperationException("Missing item texture source 'glassbottle_small' for the cauldron menu.");
            _recipeBookIcon = LoadTextureOrDefault("assets/gui/recipe_book.png");
            _lockIcon = LoadTextureOrDefault("assets/gui/lock.png");
            _checkIcon = LoadTextureOrDefault("assets/gui/check.png");
            _unknownIcon = LoadTextureOrDefault("assets/gui/unknown.png");
            _texturesLoaded = true;
        }

        private static Item LoadVisualItem(string key)
        {
            if (!GameData.ItemDatabaseByKey.TryGetValue(key, out var itemData))
                throw new InvalidOperationException($"Missing item texture source '{key}' for the cauldron menu.");

            return new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
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
            if (_recipeBookIcon.Id != 0) Raylib.UnloadTexture(_recipeBookIcon);
            if (_lockIcon.Id != 0) Raylib.UnloadTexture(_lockIcon);
            if (_checkIcon.Id != 0) Raylib.UnloadTexture(_checkIcon);
            if (_unknownIcon.Id != 0) Raylib.UnloadTexture(_unknownIcon);
            _cauldronBack = default;
            _cauldronFront = default;
            _cauldronItem = null!;
            _potionBottleData = default;
            _recipeBookIcon = default;
            _lockIcon = default;
            _checkIcon = default;
            _unknownIcon = default;
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

            _mixture.Clear();
            _fillContainer = null;
            _floatingItems.Clear();
            _particles.Clear();
            _selectedRecipeIndex = -1;
            _recipeListScroll = 0f;
            _recipeScrollTarget = 0f;
            _unlockMessage = "";
            _unlockMessageTimer = 0f;
            _unlockFlashTimer = 0f;
            _flashTimer = 0f;
            _liquidRgb = new Vector3(COLOR_EMPTY_LIQUID.R, COLOR_EMPTY_LIQUID.G, COLOR_EMPTY_LIQUID.B);
            _sharedDraggedItem?.EndDrag();
            _globalTime = 0f;
            LoadLearnedRecipes();
            UIManager.PushUI(() => Close(playerIndex), () => IsOpenForPlayer(playerIndex));
        }

        public static void Close(int playerIndex = 0)
        {
            _isOpenByPlayer[playerIndex] = false;

            // Tout ce qui a été consommé à l'ouverture est rendu au joueur (avant : perdu).
            ReturnMixtureToPlayer();
            if (_fillContainer != null)
            {
                GiveItemToPlayer(_fillContainer, 1);
                _fillContainer = null;
            }

            _floatingItems.Clear();
            _particles.Clear();
            _sharedDraggedItem?.EndDrag();
            _isDraggingWindowByPlayer[playerIndex] = false;
            _dragOffsetByPlayer[playerIndex] = Vector2.Zero;
            if (!IsOpen) UnloadTextures();
        }

        // =====================================================================
        //  LAYOUT (unique source de vérité, partagée par Update et Draw)
        // =====================================================================
        private static Vector2 GetWindowPos(int playerIndex) =>
            _windowPosByPlayer.TryGetValue(playerIndex, out var p) ? p : new Vector2(200, 150);

        private static UILayout ComputeLayout(int x, int y)
        {
            var L = new UILayout();
            int W = WINDOW_WIDTH, H = WINDOW_HEIGHT, HH = HEADER_HEIGHT;
            int pad = S(18), gap = S(14);

            L.Window = new Rectangle(x, y, W, H);
            L.Header = new Rectangle(x, y, W, HH);
            L.CloseBtn = new Rectangle(x + W - S(42), y + (HH - S(28)) / 2, S(28), S(28));

            int top = y + HH + S(14);
            int bottom = y + H - S(34);
            int rightW = RECIPE_LIST_WIDTH;
            int leftX = x + pad;
            int leftW = W - pad * 2 - gap - rightW;
            int rightX = leftX + leftW + gap;

            // Colonne gauche, construite depuis le bas
            int btnH = S(48);
            int btnY = bottom - btnH;
            int rowH = S(104);
            int rowY = btnY - S(12) - rowH;
            int clearW = S(112);
            L.ClearBtn = new Rectangle(leftX, btnY, clearW, btnH);
            L.MixBtn = new Rectangle(leftX + clearW + S(10), btnY, leftW - clearW - S(10), btnH);
            L.ContainerSlot = new Rectangle(leftX, rowY, rowH, rowH);
            L.IngredientPanel = new Rectangle(leftX + rowH + S(10), rowY, leftW - rowH - S(10), rowH);

            int areaH = rowY - S(10) - top;
            int cs = Math.Min(CAULDRON_SIZE, Math.Min(areaH, leftW));
            L.CauldronArea = new Rectangle(leftX + (leftW - cs) / 2, top + (areaH - cs) / 2, cs, cs);
            L.DropTarget = new Rectangle(leftX, top, leftW, areaH);

            // Surface du liquide
            float mx = L.CauldronArea.X + cs / 2f;
            float cy = L.CauldronArea.Y;
            if (_cauldronBack.Id != 0)
            {
                L.LiquidCenter = new Vector2(mx, cy + cs * 0.20f);
                L.LiquidRx = cs * 0.36f;
                L.LiquidRy = cs * 0.12f;
            }
            else
            {
                L.LiquidCenter = new Vector2(mx, cy + cs * (POT_RIM_Y + 0.006f));
                L.LiquidRx = cs * (POT_R - 0.012f - 0.02f);
                L.LiquidRy = cs * (POT_INNER_RY - 0.02f);
            }

            // Colonne droite
            int detailH = S(118);
            L.DetailPanel = new Rectangle(rightX, bottom - detailH, rightW, detailH);
            float listH = L.DetailPanel.Y - S(10) - top;
            L.RecipePanel = new Rectangle(rightX, top, rightW, listH);
            L.RecipeListInner = new Rectangle(rightX + S(6), top + S(40), rightW - S(12), listH - S(40) - S(6));
            return L;
        }

        private static Rectangle RecipeRowRect(UILayout L, int index)
        {
            float y = L.RecipeListInner.Y + index * RECIPE_ITEM_HEIGHT - _recipeListScroll;
            return new Rectangle(L.RecipeListInner.X + S(2), y, L.RecipeListInner.Width - S(14), RECIPE_ITEM_HEIGHT - S(4));
        }

        private static float MaxRecipeScroll(UILayout L) =>
            Math.Max(0f, _cauldronRecipes.Count * RECIPE_ITEM_HEIGHT - L.RecipeListInner.Height);

        private static void ScrollToRecipe(UILayout L, int index)
        {
            float rowTop = index * RECIPE_ITEM_HEIGHT;
            float rowBottom = rowTop + RECIPE_ITEM_HEIGHT;
            if (rowTop < _recipeScrollTarget) _recipeScrollTarget = rowTop;
            else if (rowBottom > _recipeScrollTarget + L.RecipeListInner.Height)
                _recipeScrollTarget = rowBottom - L.RecipeListInner.Height;
            _recipeScrollTarget = Math.Clamp(_recipeScrollTarget, 0f, MaxRecipeScroll(L));
        }

        private static Rectangle ChipArea(UILayout L) =>
            new Rectangle(L.IngredientPanel.X + S(8), L.IngredientPanel.Y + S(30),
                          L.IngredientPanel.Width - S(16), L.IngredientPanel.Height - S(38));

        // =====================================================================
        //  UPDATE
        // =====================================================================
        public static void Update(int playerIndex = 0, Rectangle? viewport = null)
        {
            if (!IsOpenForPlayer(playerIndex)) return;

            Vector2 mousePos = Raylib.GetMousePosition();
            if (viewport.HasValue)
                mousePos -= new Vector2(viewport.Value.X, viewport.Value.Y);
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);

            bool pressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
            bool released = Raylib.IsMouseButtonReleased(MouseButton.Left);
            bool itemDragging = _sharedDraggedItem != null && _sharedDraggedItem.IsDragging;

            // ----- Position / déplacement de la fenêtre -----
            var windowPos = GetWindowPos(playerIndex);
            windowPos.X = Math.Clamp(windowPos.X, (float)(-WINDOW_WIDTH + 100), (float)(sw - 100));
            windowPos.Y = Math.Clamp(windowPos.Y, 0f, (float)(sh - 100));
            _windowPosByPlayer[playerIndex] = windowPos;

            var L = ComputeLayout((int)windowPos.X, (int)windowPos.Y);

            bool overClose = Raylib.CheckCollisionPointRec(mousePos, L.CloseBtn);
            bool overTitle = Raylib.CheckCollisionPointRec(mousePos, L.Header) && !overClose;
            _isHoveringTitleBarByPlayer[playerIndex] = overTitle;

            if (overTitle && pressed && !itemDragging)
            {
                _isDraggingWindowByPlayer[playerIndex] = true;
                _dragOffsetByPlayer[playerIndex] = mousePos - windowPos;
            }
            bool draggingWindow = _isDraggingWindowByPlayer.TryGetValue(playerIndex, out var dw) && dw;
            if (draggingWindow && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                windowPos = mousePos - _dragOffsetByPlayer[playerIndex];
                windowPos.X = Math.Clamp(windowPos.X, (float)(-WINDOW_WIDTH + 100), (float)(sw - 100));
                windowPos.Y = Math.Clamp(windowPos.Y, 0f, (float)(sh - 100));
                _windowPosByPlayer[playerIndex] = windowPos;
                L = ComputeLayout((int)windowPos.X, (int)windowPos.Y); // pas de décalage d'une frame
            }
            if (released)
                _isDraggingWindowByPlayer[playerIndex] = false;

            // ----- Fermeture -----
            if (overClose && pressed)
            {
                Close(playerIndex);
                return;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                Close(playerIndex);
                return;
            }

            // ----- Dépôt d'un objet glissé -----
            if (itemDragging && released && _sharedDraggedItem!.Item != null)
            {
                var dragged = _sharedDraggedItem.Item;
                bool onSlot = Raylib.CheckCollisionPointRec(mousePos, L.ContainerSlot);
                bool onCauldron = Raylib.CheckCollisionPointRec(mousePos, L.DropTarget);

                // Un récipient vide lâché sur le chaudron va dans l'emplacement dédié
                bool containerIntoSlot = onSlot ||
                    (onCauldron && _fillContainer == null && Program.IsEmptyLiquidContainer(dragged));

                if (containerIntoSlot)
                {
                    AddFillContainer(dragged);
                    if (_fillContainer != null)
                    {
                        _sharedDraggedItem.Count--;
                        if (_sharedDraggedItem.Count <= 0)
                            _sharedDraggedItem.EndDrag();
                    }
                }
                else if (onCauldron)
                {
                    AddIngredient(dragged, 1, mousePos - L.LiquidCenter, L);
                    _sharedDraggedItem.Count--;
                    if (_sharedDraggedItem.Count <= 0)
                        _sharedDraggedItem.EndDrag();
                }
            }

            // ----- Clics sur l'interface (hors glisser-déposer) -----
            if (pressed && !itemDragging)
            {
                // Reprendre le récipient
                if (_fillContainer != null && Raylib.CheckCollisionPointRec(mousePos, L.ContainerSlot))
                {
                    GiveItemToPlayer(_fillContainer, 1);
                    _fillContainer = null;
                }

                // Retirer un ingrédient en cliquant dessus
                var chips = LayoutChips(BuildMixtureChips(), ChipArea(L), out _);
                foreach (var chip in chips)
                {
                    if (chip.Key != null && Raylib.CheckCollisionPointRec(mousePos, chip.Rect))
                    {
                        RemoveOneIngredient(chip.Key, L);
                        break;
                    }
                }

                // Vider
                if (_mixture.Count > 0 && Raylib.CheckCollisionPointRec(mousePos, L.ClearBtn))
                {
                    SpawnSplash(Vector2.Zero, 6, 0f);
                    ReturnMixtureToPlayer();
                }

                // Mélanger (même condition que l'affichage du bouton)
                bool canMix = _mixture.Count > 0 && _fillContainer != null;
                if (canMix && Raylib.CheckCollisionPointRec(mousePos, L.MixBtn))
                    TryCraft();

                // Sélection de recette
                if (Raylib.CheckCollisionPointRec(mousePos, L.RecipeListInner))
                {
                    for (int i = 0; i < _cauldronRecipes.Count; i++)
                    {
                        if (Raylib.CheckCollisionPointRec(mousePos, RecipeRowRect(L, i)))
                        {
                            _selectedRecipeIndex = i;
                            break;
                        }
                    }
                }
            }

            // ----- Défilement de la liste (fluide) -----
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0 && Raylib.CheckCollisionPointRec(mousePos, L.RecipePanel))
                _recipeScrollTarget -= wheel * RECIPE_ITEM_HEIGHT * 1.2f;
            _recipeScrollTarget = Math.Clamp(_recipeScrollTarget, 0f, MaxRecipeScroll(L));
            _recipeListScroll += (_recipeScrollTarget - _recipeListScroll) * MathF.Min(1f, dt * 14f);
            if (MathF.Abs(_recipeScrollTarget - _recipeListScroll) < 0.1f) _recipeListScroll = _recipeScrollTarget;

            // ----- Animations -----
            _globalTime += dt;
            if (_unlockMessageTimer > 0) _unlockMessageTimer -= dt;
            if (_unlockFlashTimer > 0) _unlockFlashTimer -= dt;
            if (_flashTimer > 0) _flashTimer -= dt;

            foreach (var fi in _floatingItems)
                fi.Appear = MathF.Min(1f, fi.Appear + dt / APPEAR_TIME);

            // Couleur du liquide : moyenne des ingrédients, ou couleur du résultat pendant l'éclat
            Vector3 target = _flashTimer > 0
                ? new Vector3(_flashColor.R, _flashColor.G, _flashColor.B)
                : GetMixtureRgb();
            _liquidRgb = Vector3.Lerp(_liquidRgb, target, MathF.Min(1f, dt * 4f));

            UpdateParticles(L, dt);
        }

        // =====================================================================
        //  PARTICULES
        // =====================================================================
        private static float Rnd() => Random.Shared.NextSingle();
        private static float RndRange(float a, float b) => a + (b - a) * Rnd();

        private static void AddParticle(ParticleKind kind, Vector2 pos, Vector2 vel, float life, float size, Color tint, float delay = 0f)
        {
            if (_particles.Count > 220) return;
            _particles.Add(new Particle { Kind = kind, Pos = pos, Vel = vel, Life = life, Size = size, Tint = tint, Age = -delay });
        }

        private static void SpawnSplash(Vector2 offset, int droplets, float delay)
        {
            Color tint = Lighten(C((int)_liquidRgb.X, (int)_liquidRgb.Y, (int)_liquidRgb.Z), 0.35f);
            AddParticle(ParticleKind.Ripple, offset, Vector2.Zero, 0.9f, SF(26f), tint, delay);
            AddParticle(ParticleKind.Ripple, offset, Vector2.Zero, 1.1f, SF(40f), tint, delay + 0.12f);
            for (int i = 0; i < droplets; i++)
                AddParticle(ParticleKind.Droplet, offset,
                    new Vector2(RndRange(-70f, 70f), RndRange(-190f, -110f)) * (UIManager.Scale(1f)),
                    RndRange(0.45f, 0.75f), SF(RndRange(1.6f, 3.2f)), tint, delay);
        }

        private static void UpdateParticles(UILayout L, float dt)
        {
            bool hasMix = _mixture.Count > 0;
            int total = _mixture.Values.Sum();
            float heat = Math.Clamp(total / 10f, 0f, 1f) + (_flashTimer > 0 ? 1f : 0f);
            Color liquid = C((int)_liquidRgb.X, (int)_liquidRgb.Y, (int)_liquidRgb.Z);

            _bubbleTimer -= dt;
            if (_bubbleTimer <= 0f)
            {
                _bubbleTimer = (0.75f + (0.12f - 0.75f) * MathF.Min(1f, heat)) * RndRange(0.6f, 1.2f);
                float ang = Rnd() * MathF.PI * 2f;
                float r = MathF.Sqrt(Rnd()) * 0.8f;
                AddParticle(ParticleKind.Bubble,
                    new Vector2(MathF.Cos(ang) * r * L.LiquidRx, MathF.Sin(ang) * r * L.LiquidRy),
                    Vector2.Zero, RndRange(0.7f, 1.4f), SF(RndRange(2.5f, 5.5f)), Lighten(liquid, 0.4f));
            }

            _steamTimer -= dt;
            if (_steamTimer <= 0f && (hasMix || _flashTimer > 0))
            {
                _steamTimer = RndRange(0.25f, 0.5f) / (0.6f + heat);
                AddParticle(ParticleKind.Steam,
                    new Vector2(RndRange(-0.6f, 0.6f) * L.LiquidRx, -L.LiquidRy * 0.3f),
                    new Vector2(RndRange(-7f, 7f), RndRange(-30f, -18f)) * UIManager.Scale(1f),
                    RndRange(1.6f, 2.4f), SF(RndRange(8f, 15f)), Lighten(liquid, 0.55f));
            }

            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Age += dt;
                if (p.Age < 0f) continue;
                if (p.Kind == ParticleKind.Droplet) p.Vel.Y += SF(420f) * dt;
                p.Pos += p.Vel * dt;
                if (p.Age >= p.Life) _particles.RemoveAt(i);
            }
        }

        private static void DrawParticles(Vector2 lc, bool surfaceLayer)
        {
            foreach (var p in _particles)
            {
                if (p.Age < 0f) continue;
                bool isSurface = p.Kind == ParticleKind.Bubble || p.Kind == ParticleKind.Ripple;
                if (isSurface != surfaceLayer) continue;

                float t = Math.Clamp(p.Age / p.Life, 0f, 1f);
                Vector2 pos = lc + p.Pos;

                switch (p.Kind)
                {
                    case ParticleKind.Bubble:
                    {
                        float r = p.Size * (0.5f + t * 0.9f);
                        Raylib.DrawCircleV(pos, r, Fade(p.Tint, 0.12f));
                        Raylib.DrawCircleLines((int)pos.X, (int)pos.Y, r, Fade(p.Tint, (1f - t * t) * 0.75f));
                        break;
                    }
                    case ParticleKind.Ripple:
                    {
                        float rx = p.Size * (0.3f + t * 1.6f);
                        Raylib.DrawEllipseLines((int)pos.X, (int)pos.Y, rx, rx * 0.35f, Fade(p.Tint, (1f - t) * 0.65f));
                        break;
                    }
                    case ParticleKind.Steam:
                    {
                        float r = p.Size * (0.8f + t * 1.4f);
                        Raylib.DrawCircleV(pos, r, Fade(p.Tint, MathF.Sin(t * MathF.PI) * 0.17f));
                        break;
                    }
                    case ParticleKind.Droplet:
                        Raylib.DrawCircleV(pos, p.Size * (1f - t * 0.4f), Fade(p.Tint, 1f - t));
                        break;
                    case ParticleKind.Spark:
                    {
                        float r = p.Size * (1f - t * 0.7f);
                        Raylib.DrawCircleV(pos, r * 2.4f, Fade(p.Tint, (1f - t) * 0.18f));
                        Raylib.DrawCircleV(pos, r, Fade(Lighten(p.Tint, 0.6f), 1f - t));
                        break;
                    }
                }
            }
        }

        // =====================================================================
        //  LOGIQUE : ingrédients, récipient, craft
        // =====================================================================
        private static bool SameIngredient(Item a, Item b)
        {
            if (a.Name != b.Name) return false;
            if (a.CustomColor == null && b.CustomColor == null) return true;
            return a.CustomColor != null && b.CustomColor != null && a.CustomColor.Value.Equals(b.CustomColor.Value);
        }

        private static void AddIngredient(Item item, int quantity, Vector2 dropOffset, UILayout L)
        {
            if (quantity <= 0) return;

            Item? key = null;
            foreach (var kv in _mixture)
            {
                if (SameIngredient(kv.Key, item)) { key = kv.Key; break; }
            }

            if (key != null)
            {
                _mixture[key] += quantity;
            }
            else
            {
                key = CloneItem(item); // conserve métadonnées / couleurs pour pouvoir les rendre intactes
                key.Count = quantity;
                _mixture[key] = quantity;
            }

            for (int i = 0; i < quantity && _floatingItems.Count < MAX_FLOATING; i++)
            {
                int n = _floatingItems.Count;
                float ang = n * 2.3999632f + RndRange(-0.3f, 0.3f);          // angle d'or : répartition régulière
                float rad = 0.22f + 0.55f * MathF.Sqrt((n * 0.618f) % 1f);
                _floatingItems.Add(new FloatingIngredient
                {
                    Item = key,
                    Rest = new Vector2(MathF.Cos(ang) * rad, MathF.Sin(ang) * rad),
                    SpawnOffset = dropOffset,
                    Appear = 0f,
                    PhaseX = Rnd() * MathF.PI * 2f,
                    PhaseY = Rnd() * MathF.PI * 2f,
                    SpeedX = RndRange(0.6f, 1.4f),
                    SpeedY = RndRange(0.9f, 1.8f),
                    RotPhase = Rnd() * MathF.PI * 2f,
                    RotSpeed = RndRange(0.6f, 1.6f),
                    RotAmp = RndRange(8f, 20f)
                });
            }

            // Éclaboussure à l'endroit où le dernier ingrédient atterrit
            Vector2 land = Vector2.Zero;
            if (_floatingItems.Count > 0 && ReferenceEquals(_floatingItems[^1].Item, key))
            {
                var last = _floatingItems[^1];
                land = new Vector2(last.Rest.X * L.LiquidRx * 0.78f, last.Rest.Y * L.LiquidRy * 0.7f - SF(8f));
            }
            SpawnSplash(land, 6, APPEAR_TIME - 0.04f);

            Program.ConsumeItemFromInventory(item, quantity);
        }

        private static void RemoveOneIngredient(Item key, UILayout L)
        {
            if (!_mixture.TryGetValue(key, out int qty)) return;

            var back = CloneItem(key);
            back.Count = 1;
            GiveItemToPlayer(back, 1);

            if (qty <= 1) _mixture.Remove(key);
            else _mixture[key] = qty - 1;

            int idx = _floatingItems.FindLastIndex(f => ReferenceEquals(f.Item, key));
            if (idx >= 0) _floatingItems.RemoveAt(idx);

            SpawnSplash(Vector2.Zero, 3, 0f);
        }

        private static void ReturnMixtureToPlayer()
        {
            foreach (var kv in _mixture.ToList())
            {
                var back = CloneItem(kv.Key);
                back.Count = kv.Value;
                GiveItemToPlayer(back, kv.Value);
            }
            _mixture.Clear();
            _floatingItems.Clear();
        }

        private static void GiveItemToPlayer(Item item, int count)
        {
            int remaining = Program.AddItemToInventory(item, count);
            if (remaining > 0)
            {
                Program.DropCustomItemOnGroundWithColors(
                    Program.GetPlayerPosition(),
                    GetItemId(item.Name),
                    remaining,
                    item.CustomColors,
                    item.Metadata,
                    item.Meta);
            }
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

            // Quantités par id d'item (deux entrées de même nom mais de couleurs différentes s'additionnent)
            var mixtureIds = new Dictionary<int, int>();
            bool unknownIngredient = false;
            foreach (var kv in _mixture)
            {
                int id = GetItemId(kv.Key.Name);
                if (id == 0) { unknownIngredient = true; continue; }
                mixtureIds[id] = mixtureIds.GetValueOrDefault(id) + kv.Value;
            }

            CraftRecipe? matchingRecipe = null;
            if (!unknownIngredient)
            {
                foreach (var recipe in _cauldronRecipes)
                {
                    if (mixtureIds.Count != recipe.Ingredients.Count) continue;

                    bool match = true;
                    foreach (var (ingId, ingQty) in recipe.Ingredients)
                    {
                        if (!mixtureIds.TryGetValue(ingId, out int have) || have != ingQty)
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) { matchingRecipe = recipe; break; }
                }
            }

            if (matchingRecipe != null)
            {
                SoundEffects.PlayCraft();
                var resultItem = CreateFilledContainerItem(_fillContainer, matchingRecipe);
                _fillContainer = null;
                GiveItemToPlayer(resultItem, 1);

                // Couleur propre à LA RECETTE (resultColor), sinon couleur générique de l'item.
                Color resultColor = matchingRecipe.ResultColor
                    ?? (GameData.ItemDatabase.TryGetValue(matchingRecipe.ResultId, out var resultData) ? resultData.Color : Color.White);

                Program.AddItemNotification(resultItem, 1, resultColor);
                StartCraftFx(resultColor, true);

                string craftedName = GetRecipeName(matchingRecipe);
                if (!_learnedRecipes.Contains(matchingRecipe.Id))
                {
                    _learnedRecipes.Add(matchingRecipe.Id);
                    OnRecipeLearned(matchingRecipe, $"Nouvelle recette apprise : {craftedName} !");
                    Program.AddNotification(new Notification($"Recette apprise : {craftedName} !", new Color((byte)200, (byte)180, (byte)100, (byte)255), 3f));
                    SaveLearnedRecipes();
                }

                Program.AddNotification(new Notification($"Mélange réussi ! Votre récipient contient maintenant {craftedName.ToLowerInvariant()}.", new Color((byte)100, (byte)255, (byte)100, (byte)255), 2.5f));
                AchievementManager.Progress(AchievementType.CraftItem, 1);
            }
            else
            {
                // Mélange raté : le récipient se remplit d'un liquide sans effet (cohérent avec le
                // cas réussi). Avant : une bouteille neuve était créée EN PLUS du récipient rendu.
                var failItem = CreateFilledContainerItem(_fillContainer, null);
                _fillContainer = null;
                GiveItemToPlayer(failItem, 1);
                Program.AddItemNotification(failItem, 1, Color.White);
                StartCraftFx(new Color((byte)150, (byte)130, (byte)110, (byte)255), false);
                Program.AddNotification(new Notification("Mélange étrange... Votre récipient contient un liquide sans effet.", new Color((byte)255, (byte)180, (byte)100, (byte)255), 2.5f));
            }

            _mixture.Clear();
            _floatingItems.Clear();
        }

        private static void StartCraftFx(Color color, bool success)
        {
            _flashColor = color;
            _flashTimer = success ? 1.3f : 0.7f;
            int count = success ? 30 : 14;
            for (int i = 0; i < count; i++)
            {
                float ang = Rnd() * MathF.PI * 2f;
                float spd = RndRange(40f, 150f);
                var vel = new Vector2(MathF.Cos(ang) * spd, -MathF.Abs(MathF.Sin(ang)) * spd - 40f) * UIManager.Scale(1f);
                AddParticle(i % 3 == 0 ? ParticleKind.Droplet : ParticleKind.Spark,
                    new Vector2(RndRange(-30f, 30f), 0f) * UIManager.Scale(1f), vel,
                    RndRange(0.6f, 1.2f), SF(RndRange(2f, 4.5f)), color);
            }
            AddParticle(ParticleKind.Ripple, Vector2.Zero, Vector2.Zero, 1.0f, SF(60f), color);
        }

        // =====================================================================
        //  DONNÉES DE RECETTES / ITEMS
        // =====================================================================
        // Nom unique, utilisé partout (liste, détail, notifications) : avant, deux fonctions
        // différentes pouvaient donner des noms différents pour la même recette.
        private static string GetRecipeName(CraftRecipe? recipe)
        {
            if (recipe == null) return "liquide sans effet";

            if (!string.IsNullOrWhiteSpace(recipe.ResultName))
                return recipe.ResultName;

            if (GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var resultData))
                return Localization.GetLocalizedItemName(resultData.ID);

            return Localization.GetLocalizedItemName(recipe.ResultId);
        }

        private static Item CreateFilledContainerItem(Item container, CraftRecipe? recipe)
        {
            Item filled = CloneItem(container);
            filled.Count = 1;
            filled.Metadata = BuildContainerPotionMetadata(recipe, container);
            return filled;
        }

        private static string BuildContainerPotionMetadata(CraftRecipe? recipe, Item container)
        {
            string metadata = BuildBottleMetadata(recipe);
            int capacity = Program.GetWaterCapacityForItem(container);
            if (capacity <= 0)
                capacity = 1;
            return $"{metadata};doses={capacity}";
        }

        private static string BuildBottleMetadata(CraftRecipe? recipe)
        {
            // Basé sur les données (recipes.json) : chaque recette porte sa métadonnée d'effet.
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.ResultMetadata))
                return "effect:none";

            return ItemRenderer.NormalizePotionMetadata(recipe.ResultMetadata);
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

        private static void SaveLearnedRecipes()
        {
            foreach (var recipeId in _learnedRecipes)
                RecipeSystem.LearnCauldronRecipe(recipeId);
        }

        private static int GetItemId(string name) => GameData.GetItemId(name);

        private static bool IsRecipeLearned(CraftRecipe recipe) => _learnedRecipes.Contains(recipe.Id);

        private static void OnRecipeLearned(CraftRecipe recipe, string message)
        {
            _unlockMessage = message;
            _unlockMessageDuration = 4f;
            _unlockMessageTimer = 4f;
            _unlockFlashRecipeId = recipe.Id;
            _unlockFlashTimer = 2f;
            _selectedRecipeIndex = _cauldronRecipes.FindIndex(r => r.Id == recipe.Id);
        }

        // --- Méthodes publiques pour le système de recettes ---

        public static void LearnRecipe(int recipeId)
        {
            if (_learnedRecipes.Contains(recipeId)) return;
            _learnedRecipes.Add(recipeId);

            var recipe = _cauldronRecipes.FirstOrDefault(r => r.Id == recipeId);
            if (recipe != null)
            {
                string name = GetRecipeName(recipe);
                OnRecipeLearned(recipe, $"Recette débloquée : {name}");
                Program.AddNotification(new Notification($"Nouvelle recette : {name}", new Color((byte)100, (byte)255, (byte)200, (byte)255), 2.5f));
            }
            SaveLearnedRecipes();
        }

        public static void ResetLearnedRecipes()
        {
            _learnedRecipes.Clear();
            SaveLearnedRecipes();
            Program.AddNotification(new Notification("Toutes les recettes du chaudron ont été réinitialisées", new Color((byte)200, (byte)200, (byte)100, (byte)255), 2f));
        }

        public static void UnlockAllCauldronRecipes()
        {
            foreach (var recipe in _cauldronRecipes)
                _learnedRecipes.Add(recipe.Id);
            SaveLearnedRecipes();
            Program.AddNotification(new Notification($"{_cauldronRecipes.Count} recettes du chaudron débloquées !", new Color((byte)100, (byte)255, (byte)200, (byte)255), 3f));
        }

        // =====================================================================
        //  HELPERS DE DESSIN
        // =====================================================================
        private static Color Fade(Color c, float m) => C(c.R, c.G, c.B, (int)(c.A * m));
        private static Color WithAlpha(Color c, float a) => C(c.R, c.G, c.B, (int)(a * 255f));
        private static Color Lighten(Color c, float t) =>
            C((int)(c.R + (255 - c.R) * t), (int)(c.G + (255 - c.G) * t), (int)(c.B + (255 - c.B) * t), c.A);
        private static Color Darken(Color c, float t) =>
            C((int)(c.R * (1f - t)), (int)(c.G * (1f - t)), (int)(c.B * (1f - t)), c.A);

        private static float EaseOutCubic(float x) => 1f - MathF.Pow(1f - Math.Clamp(x, 0f, 1f), 3f);

        private static Vector3 GetMixtureRgb()
        {
            if (_mixture.Count == 0)
                return new Vector3(COLOR_EMPTY_LIQUID.R, COLOR_EMPTY_LIQUID.G, COLOR_EMPTY_LIQUID.B);

            float r = 0, g = 0, b = 0; int n = 0;
            foreach (var kv in _mixture)
            {
                var c = kv.Key.DisplayColor;
                r += c.R * kv.Value; g += c.G * kv.Value; b += c.B * kv.Value; n += kv.Value;
            }
            if (n == 0) return new Vector3(COLOR_EMPTY_LIQUID.R, COLOR_EMPTY_LIQUID.G, COLOR_EMPTY_LIQUID.B);
            return new Vector3(r / n, g / n, b / n);
        }

        private static float Roundness(Rectangle r, float radiusPx) =>
            Math.Clamp(radiusPx * 2f / MathF.Max(1f, MathF.Min(r.Width, r.Height)), 0f, 1f);

        private static void FillRound(Rectangle r, float radiusPx, Color c) =>
            Raylib.DrawRectangleRounded(r, Roundness(r, radiusPx), 10, c);

        private static void LineRound(Rectangle r, float radiusPx, float thick, Color c) =>
            Raylib.DrawRectangleRoundedLines(r, Roundness(r, radiusPx), 10, thick, c);

        private static void DrawInset(Rectangle r)
        {
            FillRound(r, S(10), COLOR_PANEL);
            LineRound(r, S(10), 1, COLOR_PANEL_BORDER);
        }

        private static int TextW(string s, int size) => FontManager.MeasureText(s, FS(size));

        private static void Text(string s, float x, float y, int size, Color col) =>
            FontManager.DrawText(s, (int)x, (int)y, FS(size), col);

        private static void TextShadow(string s, float x, float y, int size, Color col)
        {
            Text(s, x + 1, y + 1, size, C(0, 0, 0, 140));
            Text(s, x, y, size, col);
        }

        private static void TextCentered(string s, Rectangle r, int size, Color col)
        {
            int w = TextW(s, size);
            Text(s, r.X + (r.Width - w) / 2f, r.Y + (r.Height - FS(size)) / 2f - 1, size, col);
        }

        private static string Fit(string s, int size, float maxW)
        {
            if (TextW(s, size) <= maxW) return s;
            while (s.Length > 1 && TextW(s + "..", size) > maxW) s = s[..^1];
            return s + "..";
        }

        private static float DrawWrapped(string text, float x, float y, float maxW, int size, Color col)
        {
            var words = text.Split(' ');
            string line = "";
            float cy = y;
            foreach (var w in words)
            {
                string test = line.Length == 0 ? w : line + " " + w;
                if (TextW(test, size) > maxW && line.Length > 0)
                {
                    Text(line, x, cy, size, col);
                    cy += FS(size) + S(3);
                    line = w;
                }
                else line = test;
            }
            if (line.Length > 0)
            {
                Text(line, x, cy, size, col);
                cy += FS(size) + S(3);
            }
            return cy;
        }

        private static void DrawIconBox(Texture2D icon, Color fallback, Rectangle dest, float alpha = 1f)
        {
            if (icon.Id != 0)
                Raylib.DrawTexturePro(icon, new Rectangle(0, 0, icon.Width, icon.Height), dest, Vector2.Zero, 0f,
                    C(255, 255, 255, (int)(255 * alpha)));
            else
                FillRound(dest, S(4), Fade(fallback, alpha));
        }

        private static void DrawPotionBottle(Vector2 center, float size, string metadata = "", Color? tint = null)
        {
            ItemRenderer.DrawItemRotated(_potionBottleData, center, size, 0f, tint, metadata);
        }

        // =====================================================================
        //  DRAW
        // =====================================================================
        public static void Draw(int playerIndex = 0, Rectangle? viewport = null)
        {
            if (!IsOpenForPlayer(playerIndex)) return;

            var windowPos = GetWindowPos(playerIndex);
            Vector2 mousePos = Raylib.GetMousePosition();
            if (viewport.HasValue)
                mousePos -= new Vector2(viewport.Value.X, viewport.Value.Y);

            var L = ComputeLayout((int)windowPos.X, (int)windowPos.Y);
            bool dragging = _sharedDraggedItem != null && _sharedDraggedItem.IsDragging;
            bool hoverTitle = _isHoveringTitleBarByPlayer.TryGetValue(playerIndex, out var ht) && ht;

            DrawWindowFrame(L, hoverTitle);
            DrawHeaderContent(L, mousePos);
            DrawCauldronScene(L, mousePos, dragging);
            DrawContainerSlot(L, mousePos, dragging);
            DrawIngredientPanel(L, mousePos);
            DrawButtons(L, mousePos);
            DrawRecipePanel(L, mousePos);
            DrawDetailPanel(L);
            DrawToast(L);

            string hint = "Glissez un objet dans le chaudron  |  Clic sur un ingrédient pour le retirer  |  Échap : fermer";
            Text(hint, L.Window.X + (L.Window.Width - TextW(hint, 11)) / 2f, L.Window.Y + L.Window.Height - S(24), 11, C(130, 122, 150, 190));
        }

        // ----- Cadre + barre de titre -----
        private static void DrawWindowFrame(UILayout L, bool hoverTitle)
        {
            var w = L.Window;
            float rad = S(16);

            FillRound(new Rectangle(w.X + S(5), w.Y + S(7), w.Width, w.Height), rad, C(0, 0, 0, 110));
            FillRound(w, rad, COLOR_BG);

            // Léger éclairage sous l'en-tête
            Raylib.DrawRectangleGradientV((int)w.X + S(3), (int)(w.Y + L.Header.Height), (int)w.Width - S(6), S(140),
                C(120, 90, 200, 20), C(120, 90, 200, 0));

            // En-tête : coins du haut arrondis, bas droit
            Color hc = hoverTitle ? COLOR_HEADER_HOVER : COLOR_HEADER;
            FillRound(L.Header, rad, hc);
            Raylib.DrawRectangle((int)L.Header.X, (int)(L.Header.Y + L.Header.Height - rad), (int)L.Header.Width, (int)rad, hc);
            Raylib.DrawRectangle((int)L.Header.X + S(2), (int)(L.Header.Y + L.Header.Height - 1), (int)L.Header.Width - S(4), 1, Fade(COLOR_ACCENT, 0.45f));

            LineRound(w, rad, 2, COLOR_BORDER);
            LineRound(new Rectangle(w.X + S(5), w.Y + S(5), w.Width - S(10), w.Height - S(10)), rad - S(4), 1, COLOR_BORDER_INNER);
        }

        private static void DrawHeaderContent(UILayout L, Vector2 mouse)
        {
            var h = L.Header;

            // Petit emblème de chaudron
            float ex = h.X + S(32), ey = h.Y + h.Height / 2f;
            float emblemSize = S(24);
            ItemRenderer.DrawItem(_cauldronItem,
                new Rectangle(ex - emblemSize / 2f, ey - emblemSize / 2f, emblemSize, emblemSize));

            TextShadow("CHAUDRON", h.X + S(54), h.Y + (h.Height - FS(18)) / 2f - 1, 18, COLOR_TEXT);

            // Pastille de progression
            int learned = _cauldronRecipes.Count(IsRecipeLearned);
            int totalR = _cauldronRecipes.Count;
            float pw = S(150);
            var pill = new Rectangle(L.CloseBtn.X - S(14) - pw, h.Y + (h.Height - S(24)) / 2f, pw, S(24));
            FillRound(pill, S(12), C(20, 18, 30, 255));
            if (totalR > 0 && learned > 0)
            {
                float fill = pill.Width * learned / totalR;
                Raylib.BeginScissorMode((int)pill.X, (int)pill.Y, (int)fill, (int)pill.Height);
                FillRound(pill, S(12), Fade(COLOR_ACCENT, 0.32f));
                Raylib.EndScissorMode();
            }
            LineRound(pill, S(12), 1, Fade(COLOR_ACCENT, 0.5f));
            TextCentered($"Recettes  {learned}/{totalR}", pill, 11, COLOR_ACCENT);

            // Bouton fermer
            bool hoverClose = Raylib.CheckCollisionPointRec(mouse, L.CloseBtn);
            FillRound(L.CloseBtn, S(8), hoverClose ? C(150, 62, 62) : C(78, 42, 48, 220));
            float cxm = L.CloseBtn.X + L.CloseBtn.Width / 2f, cym = L.CloseBtn.Y + L.CloseBtn.Height / 2f, d = S(5);
            Color xc = hoverClose ? Color.White : C(214, 140, 140);
            Raylib.DrawLineEx(new Vector2(cxm - d, cym - d), new Vector2(cxm + d, cym + d), 2f, xc);
            Raylib.DrawLineEx(new Vector2(cxm - d, cym + d), new Vector2(cxm + d, cym - d), 2f, xc);
        }

        // ----- Scène du chaudron -----
        private static void DrawCauldronScene(UILayout L, Vector2 mouse, bool dragging)
        {
            var area = L.CauldronArea;
            float cs = area.Width, cx = area.X, cy = area.Y, mx = cx + cs / 2f;
            Vector2 lc = L.LiquidCenter;
            float rx = L.LiquidRx, ry = L.LiquidRy;
            bool art = _cauldronBack.Id != 0;
            Color liquid = C((int)_liquidRgb.X, (int)_liquidRgb.Y, (int)_liquidRgb.Z);
            float pulse = 0.5f + 0.5f * MathF.Sin(_globalTime * 4f);
            bool hasMix = _mixture.Count > 0;
            bool overDrop = dragging && Raylib.CheckCollisionPointRec(mouse, L.DropTarget);

            // Zone de dépôt : discrète au repos, visible dès qu'on glisse un objet
            FillRound(L.DropTarget, S(14), C(16, 14, 26, 150));
            if (dragging)
                LineRound(L.DropTarget, S(14), overDrop ? 2f : 1f, Fade(COLOR_ACCENT, overDrop ? 0.5f + 0.4f * pulse : 0.25f + 0.15f * pulse));
            else
                LineRound(L.DropTarget, S(14), 1, C(70, 62, 92, 90));

            // Halo magique derrière le chaudron
            Color aura = (hasMix || _flashTimer > 0) ? liquid : C(120, 90, 200);
            float auraA = (hasMix ? 0.10f : 0.035f) + (_flashTimer > 0 ? MathF.Min(1f, _flashTimer) * 0.22f : 0f) + (overDrop ? 0.05f * pulse : 0f);
            for (int k = 0; k < 4; k++)
                Raylib.DrawCircleV(new Vector2(mx, cy + cs * 0.55f), cs * (0.52f - k * 0.08f), WithAlpha(aura, auraA * 0.5f));

            // --- Chaudron (fond) ---
            if (art)
                Raylib.DrawTexturePro(_cauldronBack, new Rectangle(0, 0, _cauldronBack.Width, _cauldronBack.Height),
                    area, Vector2.Zero, 0f, Color.White);
            else
                DrawPotBack(area, liquid, hasMix || _flashTimer > 0);

            // Bulles / ondulations à la surface
            DrawParticles(lc, true);

            // --- Ingrédients flottants ---
            float lift = SF(8f);
            foreach (var fi in _floatingItems)
            {
                float e = EaseOutCubic(fi.Appear);
                float t = _globalTime;
                Vector2 rest = new Vector2(
                    lc.X + fi.Rest.X * rx * 0.78f + MathF.Sin(t * fi.SpeedX + fi.PhaseX) * SF(5f),
                    lc.Y + fi.Rest.Y * ry * 0.7f - lift + MathF.Sin(t * fi.SpeedY + fi.PhaseY) * SF(3.5f));
                Vector2 pos = Vector2.Lerp(lc + fi.SpawnOffset, rest, e);
                pos.Y -= MathF.Sin(e * MathF.PI) * SF(30f) * (fi.Appear < 1f ? 1f : 0f);

                float size = SF(30f) * (0.55f + 0.45f * e);
                float alpha = 0.3f + 0.7f * e;
                float rot = MathF.Sin(t * fi.RotSpeed + fi.RotPhase) * fi.RotAmp + (1f - e) * 160f;

                // ombre portée sur le liquide
                Raylib.DrawEllipse((int)pos.X, (int)(pos.Y + size * 0.42f), size * 0.38f, size * 0.10f, C(0, 0, 0, (int)(60 * e)));

                var icon = fi.Item.Icon;
                if (icon.Id != 0)
                {
                    // rotation autour du CENTRE (avant : autour du coin haut-gauche, d'où un tremblement)
                    Raylib.DrawTexturePro(icon, new Rectangle(0, 0, icon.Width, icon.Height),
                        new Rectangle(pos.X, pos.Y, size, size), new Vector2(size / 2f, size / 2f), rot,
                        C(255, 255, 255, (int)(255 * alpha)));
                }
                else
                {
                    Raylib.DrawRectanglePro(new Rectangle(pos.X, pos.Y, size * 0.8f, size * 0.8f),
                        new Vector2(size * 0.4f, size * 0.4f), rot, Fade(fi.Item.DisplayColor, alpha));
                }

                // Partie immergée : léger voile de la couleur du liquide
                Raylib.DrawEllipse((int)pos.X, (int)(pos.Y + size * 0.36f), size * 0.5f, size * 0.15f, WithAlpha(liquid, 0.5f * e));
            }

            // --- Chaudron (avant) ---
            if (art)
            {
                if (_cauldronFront.Id != 0)
                    Raylib.DrawTexturePro(_cauldronFront, new Rectangle(0, 0, _cauldronFront.Width, _cauldronFront.Height),
                        area, Vector2.Zero, 0f, Color.White);
            }
            else
                DrawPotFront(area);

            DrawParticles(lc, false);

            // Indice quand le chaudron est vide
            if (!hasMix && !dragging && _flashTimer <= 0)
            {
                string tip = "Glissez vos ingrédients ici";
                int tw = TextW(tip, 11);
                Text(tip, lc.X - tw / 2f, lc.Y - FS(11) / 2f, 11, C(190, 185, 210, (int)(90 + 50 * pulse)));
            }

            // Surbrillance de la cible quand l'objet est au-dessus
            if (overDrop)
            {
                for (int k = 0; k < 3; k++)
                    Raylib.DrawEllipseLines((int)lc.X, (int)lc.Y,
                        rx + SF(8f) + k * SF(3f) + pulse * SF(2f), ry + SF(5f) + k * SF(2f) + pulse * SF(1.5f),
                        Fade(COLOR_ACCENT, (0.8f - k * 0.25f) * (0.6f + 0.4f * pulse)));

                string name = _sharedDraggedItem?.Item != null ? _sharedDraggedItem.Item.Name : "";
                string label = Fit(name.Length > 0 ? $"Ajouter : {name}" : "Relâchez pour ajouter", 12, L.DropTarget.Width - S(40));
                int lw = TextW(label, 12);
                var pillR = new Rectangle(mx - lw / 2f - S(12), lc.Y - ry - SF(52f), lw + S(24), S(26));
                FillRound(pillR, S(13), C(14, 12, 22, 225));
                LineRound(pillR, S(13), 1, Fade(COLOR_ACCENT, 0.8f));
                TextCentered(label, pillR, 12, COLOR_ACCENT);
            }
        }

        private static void DrawPotBack(Rectangle area, Color liquid, bool active)
        {
            float cs = area.Width, cx = area.X, cy = area.Y, mx = cx + cs / 2f;
            float t = _globalTime;
            float rimY = cy + cs * POT_RIM_Y;
            float R = cs * POT_R;
            float bellyY = cy + cs * POT_BELLY_Y;
            float lipRx = R + cs * 0.025f, lipRy = cs * POT_LIP_RY;
            float innerRx = R - cs * 0.012f, innerRy = cs * POT_INNER_RY;

            Color iron = C(46, 43, 58), ironLight = C(78, 74, 94), ironDark = C(30, 28, 40);

            // Ombre au sol + lueur du feu
            Raylib.DrawEllipse((int)mx, (int)(cy + cs * 0.965f), cs * 0.36f, cs * 0.035f, C(0, 0, 0, 90));
            Raylib.DrawEllipse((int)mx, (int)(cy + cs * 0.95f), cs * 0.30f, cs * 0.05f, C(255, 140, 60, 38));

            // Flammes
            float by = cy + cs * 0.975f;
            for (int i = 0; i < 5; i++)
            {
                float fx = mx + (i - 2) * cs * 0.085f;
                float flick = MathF.Sin(t * 7f + i * 1.7f) * 0.5f + 0.5f;
                float fh = cs * (0.07f + 0.05f * flick) * (i == 2 ? 1.35f : 1f);
                float sway = MathF.Sin(t * 5f + i) * cs * 0.01f;
                Raylib.DrawTriangle(new Vector2(fx + sway, by - fh), new Vector2(fx - cs * 0.04f, by), new Vector2(fx + cs * 0.04f, by), C(240, 110, 40, 215));
                Raylib.DrawTriangle(new Vector2(fx + sway, by - fh * 0.62f), new Vector2(fx - cs * 0.022f, by), new Vector2(fx + cs * 0.022f, by), C(255, 205, 90, 235));
            }
            // Bûches
            Raylib.DrawRectanglePro(new Rectangle(mx, by + cs * 0.005f, cs * 0.38f, cs * 0.036f), new Vector2(cs * 0.19f, cs * 0.018f), 7f, C(86, 56, 36));
            Raylib.DrawRectanglePro(new Rectangle(mx, by + cs * 0.005f, cs * 0.38f, cs * 0.036f), new Vector2(cs * 0.19f, cs * 0.018f), -7f, C(104, 68, 42));

            // Pieds
            for (int s = -1; s <= 1; s += 2)
                FillRound(new Rectangle(mx + s * cs * 0.22f - cs * 0.035f, cy + cs * 0.83f, cs * 0.07f, cs * 0.10f), cs * 0.02f, ironDark);

            // Anses
            for (int s = -1; s <= 1; s += 2)
            {
                int hx = (int)(mx + s * (R + cs * 0.03f)), hy = (int)(rimY + cs * 0.12f);
                for (int k = 0; k < 3; k++)
                    Raylib.DrawCircleLines(hx, hy, cs * 0.045f - k, ironLight);
            }

            // Corps : rectangle + demi-cercle bas (le cercle complet dépassait au-dessus de la bordure)
            Raylib.DrawRectangle((int)(mx - R), (int)rimY, (int)(R * 2f), (int)(bellyY - rimY), iron);
            Raylib.DrawCircleSector(new Vector2(mx, bellyY), R, 0, 180, 48, iron);
            Raylib.DrawEllipse((int)(mx - R * 0.5f), (int)(rimY + cs * 0.22f), cs * 0.05f, cs * 0.17f, C(255, 255, 255, 26));
            Raylib.DrawEllipse((int)(mx + R * 0.55f), (int)(rimY + cs * 0.22f), cs * 0.07f, cs * 0.19f, C(0, 0, 0, 45));

            // Rivets
            for (int i = -2; i <= 2; i++)
                Raylib.DrawCircleV(new Vector2(mx + i * cs * 0.13f, rimY + cs * 0.165f), cs * 0.008f, ironLight);

            // Sigle alchimique : s'illumine quand le chaudron travaille
            float sg = active ? 0.55f + 0.35f * MathF.Sin(t * 3f) : 0.20f;
            Color sc = active ? Lighten(liquid, 0.4f) : COLOR_ACCENT_DIM;
            Vector2 sp = new Vector2(mx, cy + cs * 0.69f);
            float sr = cs * 0.085f;
            Raylib.DrawCircleLines((int)sp.X, (int)sp.Y, sr, Fade(sc, sg));
            Raylib.DrawCircleLines((int)sp.X, (int)sp.Y, sr * 0.55f, Fade(sc, sg * 0.7f));
            for (int i = 0; i < 3; i++)
            {
                float a0 = -MathF.PI / 2f + i * MathF.PI * 2f / 3f + t * (active ? 0.6f : 0f);
                float a1 = a0 + MathF.PI * 2f / 3f;
                Raylib.DrawLineEx(sp + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * sr, sp + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * sr, 1.2f, Fade(sc, sg));
            }

            // Bordure supérieure + intérieur
            Raylib.DrawEllipse((int)mx, (int)rimY, lipRx, lipRy, ironLight);
            Raylib.DrawEllipse((int)mx, (int)rimY, innerRx, innerRy, C(14, 12, 20));

            // Liquide
            Vector2 lc = new Vector2(mx, rimY + cs * 0.006f);
            float rx = cs * (POT_R - 0.012f - 0.02f), ry = cs * (POT_INNER_RY - 0.02f);
            Raylib.DrawEllipse((int)lc.X, (int)lc.Y, rx, ry, Darken(liquid, 0.35f));
            Raylib.DrawEllipse((int)lc.X, (int)(lc.Y + ry * 0.06f), rx * 0.94f, ry * 0.88f, liquid);
            Raylib.DrawEllipse((int)(lc.X - rx * 0.1f), (int)(lc.Y - ry * 0.15f), rx * 0.7f, ry * 0.55f, WithAlpha(Lighten(liquid, 0.35f), 0.22f));
            for (int k = 0; k < 3; k++)
            {
                float ph = (t * 0.5f + k / 3f) % 1f;
                Raylib.DrawEllipseLines((int)lc.X, (int)lc.Y, rx * (0.3f + 0.65f * ph), ry * (0.3f + 0.65f * ph),
                    WithAlpha(Lighten(liquid, 0.5f), (1f - ph) * 0.35f));
            }
        }

        private static void DrawPotFront(Rectangle area)
        {
            float cs = area.Width, mx = area.X + cs / 2f;
            float rimY = area.Y + cs * POT_RIM_Y;
            float R = cs * POT_R;
            float lipRx = R + cs * 0.025f, lipRy = cs * POT_LIP_RY;
            float innerRx = R - cs * 0.012f, innerRy = cs * POT_INNER_RY;

            // Lèvre avant : recouvre le bas des ingrédients qui trempent dans le liquide
            Vector2 c = new Vector2(mx, rimY);
            DrawEllipseArc(c, (lipRx + innerRx) / 2f, (lipRy + innerRy) / 2f, 0f, MathF.PI, cs * 0.03f, C(78, 74, 94));
            Raylib.DrawEllipseLines((int)mx, (int)rimY, lipRx, lipRy, C(150, 144, 172, 170));
            DrawEllipseArc(c, lipRx - 1f, lipRy - 1f, 0.15f, MathF.PI - 0.15f, 1.5f, C(255, 255, 255, 40));
        }

        private static void DrawEllipseArc(Vector2 c, float rx, float ry, float a0, float a1, float thick, Color col)
        {
            const int seg = 36;
            Vector2 prev = c + new Vector2(MathF.Cos(a0) * rx, MathF.Sin(a0) * ry);
            for (int i = 1; i <= seg; i++)
            {
                float a = a0 + (a1 - a0) * i / seg;
                Vector2 next = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
                Raylib.DrawLineEx(prev, next, thick, col);
                prev = next;
            }
        }

        // ----- Emplacement du récipient -----
        private static void DrawContainerSlot(UILayout L, Vector2 mouse, bool dragging)
        {
            var r = L.ContainerSlot;
            float pulse = 0.5f + 0.5f * MathF.Sin(_globalTime * 4f);
            bool hover = Raylib.CheckCollisionPointRec(mouse, r);
            bool validDrop = dragging && _fillContainer == null && _sharedDraggedItem?.Item != null
                             && Program.IsEmptyLiquidContainer(_sharedDraggedItem.Item);

            FillRound(r, S(10), hover ? COLOR_SLOT_HOVER : COLOR_PANEL);
            LineRound(r, S(10), validDrop ? 2f : 1f,
                validDrop ? Fade(COLOR_ACCENT, 0.5f + 0.5f * pulse) : COLOR_PANEL_BORDER);

            TextCentered("RÉCIPIENT", new Rectangle(r.X, r.Y + S(6), r.Width, FS(10) + S(4)), 10, COLOR_ACCENT);

            var inner = new Rectangle(r.X + (r.Width - S(60)) / 2f, r.Y + S(26), S(60), S(60));
            if (_fillContainer != null)
            {
                ItemRenderer.DrawItem(_fillContainer, inner);
                if (hover && !dragging)
                    TextCentered("Reprendre", new Rectangle(r.X, r.Y + r.Height - S(18), r.Width, S(14)), 10, COLOR_TEXT_DIM);
            }
            else
            {
                byte alpha = (byte)(validDrop ? 230 : 115);
                DrawPotionBottle(new Vector2(inner.X + inner.Width / 2f, inner.Y + inner.Height / 2f),
                    S(52), tint: new Color((byte)255, (byte)255, (byte)255, alpha));
                TextCentered(validDrop ? "Lâchez ici" : "vide", new Rectangle(r.X, r.Y + r.Height - S(18), r.Width, S(14)), 10,
                    validDrop ? COLOR_ACCENT : C(150, 145, 160, 160));
            }
        }

        // ----- Ingrédients (pastilles cliquables) -----
        private static List<Chip> BuildMixtureChips()
        {
            var list = new List<Chip>();
            foreach (var kv in _mixture.ToList())
            {
                int id = GetItemId(kv.Key.Name);
                string label = id != 0 ? Localization.GetLocalizedItemName(id) : kv.Key.Name;
                list.Add(new Chip { Key = kv.Key, Icon = kv.Key.Icon, Color = kv.Key.DisplayColor, Label = label, Qty = kv.Value });
            }
            return list;
        }

        private static List<Chip> LayoutChips(List<Chip> chips, Rectangle area, out int overflow)
        {
            var result = new List<Chip>();
            overflow = 0;
            int h = S(26), icon = S(18), pad = S(6), gap = S(6);
            float maxChipW = Math.Min(S(150), area.Width);
            float x = area.X, y = area.Y;

            for (int i = 0; i < chips.Count; i++)
            {
                var chip = chips[i];
                string qtyText = $"x{chip.Qty}";
                int qtyW = TextW(qtyText, 11);
                float nameMax = maxChipW - (pad * 2 + icon + S(5) + S(6) + qtyW);
                chip.Label = Fit(chip.Label, 11, Math.Max(S(20), nameMax));
                float w = pad * 2 + icon + S(5) + TextW(chip.Label, 11) + S(6) + qtyW;

                if (x + w > area.X + area.Width && x > area.X)
                {
                    x = area.X;
                    y += h + gap;
                }
                if (y + h > area.Y + area.Height + 0.5f)
                {
                    overflow = chips.Count - i;
                    break;
                }
                chip.Rect = new Rectangle(x, y, w, h);
                result.Add(chip);
                x += w + gap;
            }
            return result;
        }

        private static void DrawChip(Chip chip, bool hoverRemovable)
        {
            var r = chip.Rect;
            FillRound(r, S(8), hoverRemovable ? C(96, 46, 54, 240) : C(46, 42, 66, 255));
            LineRound(r, S(8), 1, hoverRemovable ? C(210, 110, 110, 220) : C(88, 80, 118, 200));

            int icon = S(18), pad = S(6);
            DrawIconBox(chip.Icon, chip.Color, new Rectangle(r.X + pad, r.Y + (r.Height - icon) / 2f, icon, icon));
            Text(chip.Label, r.X + pad + icon + S(5), r.Y + (r.Height - FS(11)) / 2f - 1, 11, COLOR_TEXT);
            string q = $"x{chip.Qty}";
            Text(q, r.X + r.Width - pad - TextW(q, 11), r.Y + (r.Height - FS(11)) / 2f - 1, 11, COLOR_ACCENT);
        }

        private static void DrawIngredientPanel(UILayout L, Vector2 mouse)
        {
            var p = L.IngredientPanel;
            DrawInset(p);
            Text("INGRÉDIENTS", p.X + S(12), p.Y + S(9), 11, COLOR_ACCENT);

            int total = _mixture.Values.Sum();
            string count = total > 0 ? $"{total} objet{(total > 1 ? "s" : "")}" : "";
            if (count.Length > 0)
                Text(count, p.X + p.Width - S(12) - TextW(count, 10), p.Y + S(10), 10, COLOR_ACCENT_DIM);

            var area = ChipArea(L);
            if (_mixture.Count == 0)
            {
                TextCentered("Le chaudron est vide", area, 11, C(150, 145, 160, 170));
                return;
            }

            var chips = LayoutChips(BuildMixtureChips(), area, out int overflow);
            bool dragging = _sharedDraggedItem != null && _sharedDraggedItem.IsDragging;
            foreach (var chip in chips)
                DrawChip(chip, !dragging && Raylib.CheckCollisionPointRec(mouse, chip.Rect));

            if (overflow > 0)
                Text($"+{overflow}", p.X + p.Width - S(12) - TextW($"+{overflow}", 11), p.Y + p.Height - S(18), 11, COLOR_ACCENT_DIM);
        }

        // ----- Boutons -----
        private static void DrawButtons(UILayout L, Vector2 mouse)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin(_globalTime * 3f);

            // Vider
            bool canClear = _mixture.Count > 0;
            bool hoverClear = canClear && Raylib.CheckCollisionPointRec(mouse, L.ClearBtn);
            FillRound(L.ClearBtn, S(10), canClear ? (hoverClear ? C(130, 62, 62) : C(88, 46, 52)) : COLOR_BUTTON_DISABLED);
            LineRound(L.ClearBtn, S(10), 1, canClear ? C(200, 110, 110, 160) : C(80, 80, 90, 100));
            TextCentered("Vider", L.ClearBtn, 14, canClear ? COLOR_TEXT : C(130, 130, 140, 160));

            // Mélanger
            bool canMix = _mixture.Count > 0 && _fillContainer != null;
            bool hoverMix = canMix && Raylib.CheckCollisionPointRec(mouse, L.MixBtn);
            bool down = hoverMix && Raylib.IsMouseButtonDown(MouseButton.Left);
            var btn = L.MixBtn;
            if (down) btn.Y += 1;

            if (canMix)
            {
                // halo pulsant
                LineRound(new Rectangle(btn.X - 2, btn.Y - 2, btn.Width + 4, btn.Height + 4), S(12), 2, Fade(COLOR_GOOD, hoverMix ? 0.6f : 0.15f + 0.25f * pulse));
            }
            FillRound(btn, S(10), canMix ? (hoverMix ? COLOR_BUTTON_HOVER : COLOR_BUTTON) : COLOR_BUTTON_DISABLED);
            LineRound(btn, S(10), 1, canMix ? C(150, 230, 170, 190) : C(80, 80, 90, 100));

            string text = canMix ? "MÉLANGER"
                        : _fillContainer == null ? "Placez un récipient vide"
                        : "Ajoutez des ingrédients";
            TextCentered(text, btn, canMix ? 16 : 13, canMix ? Color.White : C(150, 150, 160, 190));
        }

        // ----- Liste des recettes -----
        private static void DrawRecipePanel(UILayout L, Vector2 mouse)
        {
            var p = L.RecipePanel;
            DrawInset(p);

            // En-tête
            float ix = p.X + S(12), iy = p.Y + S(11);
            if (_recipeBookIcon.Id != 0)
                Raylib.DrawTexturePro(_recipeBookIcon, new Rectangle(0, 0, _recipeBookIcon.Width, _recipeBookIcon.Height),
                    new Rectangle(ix, iy - S(1), S(16), S(16)), Vector2.Zero, 0f, Color.White);
            else
            {
                FillRound(new Rectangle(ix, iy, S(14), S(16)), S(3), C(150, 104, 62));
                Raylib.DrawRectangle((int)ix + S(2), (int)iy + S(3), S(10), 1, C(240, 225, 190, 200));
                Raylib.DrawRectangle((int)ix + S(2), (int)iy + S(7), S(10), 1, C(240, 225, 190, 200));
                Raylib.DrawRectangle((int)ix + S(2), (int)iy + S(11), S(7), 1, C(240, 225, 190, 200));
            }
            Text("RECETTES", ix + S(22), p.Y + S(12), 12, COLOR_ACCENT);
            int learned = _cauldronRecipes.Count(IsRecipeLearned);
            string cnt = $"{learned}/{_cauldronRecipes.Count}";
            Text(cnt, p.X + p.Width - S(12) - TextW(cnt, 11), p.Y + S(13), 11, COLOR_ACCENT_DIM);
            Raylib.DrawRectangle((int)p.X + S(10), (int)p.Y + S(34), (int)p.Width - S(20), 1, C(96, 84, 128, 110));

            var inner = L.RecipeListInner;
            if (_cauldronRecipes.Count == 0)
            {
                TextCentered("Aucune recette", inner, 12, COLOR_TEXT_DIM);
                return;
            }

            bool overList = Raylib.CheckCollisionPointRec(mouse, inner);
            Raylib.BeginScissorMode((int)inner.X, (int)inner.Y, (int)inner.Width, (int)inner.Height);
            for (int i = 0; i < _cauldronRecipes.Count; i++)
            {
                var row = RecipeRowRect(L, i);
                if (row.Y + row.Height < inner.Y || row.Y > inner.Y + inner.Height) continue;
                DrawRecipeRow(_cauldronRecipes[i], row, i == _selectedRecipeIndex, overList && Raylib.CheckCollisionPointRec(mouse, row));
            }
            Raylib.EndScissorMode();

            // Barre de défilement
            float totalH = _cauldronRecipes.Count * RECIPE_ITEM_HEIGHT;
            if (totalH > inner.Height)
            {
                float barX = inner.X + inner.Width - S(5);
                float barH = MathF.Max(S(26), inner.Height * inner.Height / totalH);
                float maxScroll = totalH - inner.Height;
                float barY = inner.Y + (inner.Height - barH) * (maxScroll <= 0 ? 0 : _recipeListScroll / maxScroll);
                FillRound(new Rectangle(barX, inner.Y, S(4), inner.Height), S(2), C(40, 36, 56, 200));
                FillRound(new Rectangle(barX, barY, S(4), barH), S(2), COLOR_ACCENT);
            }
        }

        private static void DrawRecipeRow(CraftRecipe recipe, Rectangle row, bool selected, bool hovered)
        {
            bool learned = IsRecipeLearned(recipe);

            Color bg = selected ? C(226, 190, 106, 70)
                     : hovered ? C(62, 56, 88, 235)
                     : learned ? C(40, 70, 56, 150)
                     : C(40, 34, 50, 150);
            FillRound(row, S(8), bg);
            LineRound(row, S(8), 1, selected ? COLOR_ACCENT : C(70, 62, 92, 120));

            // Éclat quand la recette vient d'être apprise
            if (_unlockFlashTimer > 0 && recipe.Id == _unlockFlashRecipeId)
            {
                float a = MathF.Abs(MathF.Sin(_unlockFlashTimer * 6f)) * MathF.Min(1f, _unlockFlashTimer);
                FillRound(row, S(8), Fade(COLOR_ACCENT, a * 0.45f));
            }

            // Fiole de la potion (grisée et masquée tant que la recette est inconnue)
            var fc = new Vector2(row.X + S(22), row.Y + row.Height / 2f);
            if (learned)
            {
                string metadata = $"{BuildBottleMetadata(recipe)};doses={Math.Max(1, _potionBottleData.WaterCapacity)}";
                DrawPotionBottle(fc, S(34), metadata);
            }
            else if (_unknownIcon.Id != 0)
                Raylib.DrawTexturePro(_unknownIcon, new Rectangle(0, 0, _unknownIcon.Width, _unknownIcon.Height),
                    new Rectangle(fc.X - S(14), fc.Y - S(14), S(28), S(28)), Vector2.Zero, 0f, Color.White);
            else
            {
                DrawPotionBottle(fc, S(34), tint: C(70, 66, 84, 204));
                Text("?", fc.X - TextW("?", 12) / 2f, fc.Y - FS(12) / 2f + S(3), 12, C(200, 195, 215, 220));
            }

            float tx = row.X + S(46);
            float statusW = S(26);
            string name = Fit(GetRecipeName(recipe), 13, row.Width - (tx - row.X) - statusW);
            Text(name, tx, row.Y + S(9), 13, learned ? COLOR_TEXT : C(150, 145, 165, 200));

            // Ingrédients visibles uniquement si la recette est connue
            // (avant : ils s'affichaient aussi pour les recettes inconnues, ce qui contredisait "à découvrir")
            float lx = tx, ly = row.Y + S(31);
            float maxX = row.X + row.Width - statusW;
            if (learned)
            {
                int shown = 0;
                foreach (var ing in recipe.Ingredients)
                {
                    string q = $"x{ing.qty}";
                    float w = S(16) + S(2) + TextW(q, 10) + S(8);
                    if (lx + w > maxX && shown < recipe.Ingredients.Count)
                    {
                        Text("...", lx, ly, 10, COLOR_TEXT_DIM);
                        break;
                    }
                    if (GameData.ItemDatabase.TryGetValue(ing.id, out var d))
                        DrawIconBox(d.Icon, d.Color, new Rectangle(lx, ly - S(2), S(16), S(16)));
                    else
                        FillRound(new Rectangle(lx, ly - S(2), S(16), S(16)), S(4), C(70, 66, 84));
                    Text(q, lx + S(18), ly + S(1), 10, COLOR_ACCENT_DIM);
                    lx += w;
                    shown++;
                }
            }
            else
            {
                int n = recipe.Ingredients.Count;
                for (int k = 0; k < n; k++)
                {
                    float w = S(16) + S(4);
                    if (lx + w > maxX) { Text("...", lx, ly, 10, COLOR_TEXT_DIM); break; }
                    var box = new Rectangle(lx, ly - S(2), S(16), S(16));
                    FillRound(box, S(4), C(30, 26, 40, 220));
                    LineRound(box, S(4), 1, C(90, 82, 110, 160));
                    TextCentered("?", box, 10, C(150, 145, 165, 200));
                    lx += w;
                }
            }

            // Statut (coche / cadenas)
            var sc = new Vector2(row.X + row.Width - S(15), row.Y + row.Height / 2f);
            Texture2D si = learned ? _checkIcon : _lockIcon;
            if (si.Id != 0)
            {
                float s = S(16) / (float)si.Width;
                Raylib.DrawTextureEx(si, new Vector2(sc.X - S(8), sc.Y - S(8)), 0f, s, learned ? COLOR_GOOD : C(200, 150, 100, 180));
            }
            else if (learned)
            {
                Raylib.DrawCircleV(sc, S(8), C(60, 140, 86, 230));
                Raylib.DrawLineEx(new Vector2(sc.X - S(4), sc.Y), new Vector2(sc.X - S(1), sc.Y + S(3)), 2f, Color.White);
                Raylib.DrawLineEx(new Vector2(sc.X - S(1), sc.Y + S(3)), new Vector2(sc.X + S(4), sc.Y - S(3)), 2f, Color.White);
            }
            else
            {
                Color lockC = C(200, 150, 100, 190);
                Raylib.DrawCircleLines((int)sc.X, (int)(sc.Y - S(3)), S(4), lockC);
                FillRound(new Rectangle(sc.X - S(6), sc.Y - S(1), S(12), S(8)), S(2), lockC);
            }
        }

        // ----- Détail de la recette sélectionnée -----
        private static void DrawDetailPanel(UILayout L)
        {
            var p = L.DetailPanel;
            DrawInset(p);

            if (_selectedRecipeIndex < 0 || _selectedRecipeIndex >= _cauldronRecipes.Count)
            {
                TextCentered("Sélectionnez une recette", p, 12, COLOR_TEXT_DIM);
                return;
            }

            var recipe = _cauldronRecipes[_selectedRecipeIndex];
            bool learned = IsRecipeLearned(recipe);

            // Badge d'état
            string badge = learned ? "CONNUE" : "INCONNUE";
            Color badgeC = learned ? COLOR_GOOD : COLOR_WARN;
            int bw = TextW(badge, 10) + S(16);
            var badgeR = new Rectangle(p.X + p.Width - S(10) - bw, p.Y + S(10), bw, S(18));
            FillRound(badgeR, S(9), Fade(badgeC, 0.18f));
            LineRound(badgeR, S(9), 1, Fade(badgeC, 0.7f));
            TextCentered(badge, badgeR, 10, badgeC);

            string title = Fit(GetRecipeName(recipe), 14, p.Width - bw - S(34));
            Text(title, p.X + S(12), p.Y + S(11), 14, learned ? COLOR_TEXT : C(190, 185, 205, 230));

            if (learned)
            {
                var chips = new List<Chip>();
                foreach (var ing in recipe.Ingredients)
                {
                    if (GameData.ItemDatabase.TryGetValue(ing.id, out var d))
                        chips.Add(new Chip { Icon = d.Icon, Color = d.Color, Label = Localization.GetLocalizedItemName(d.ID), Qty = ing.qty });
                    else
                        chips.Add(new Chip { Color = C(70, 66, 84), Label = "?", Qty = ing.qty });
                }
                var area = new Rectangle(p.X + S(8), p.Y + S(36), p.Width - S(16), p.Height - S(42));
                var laid = LayoutChips(chips, area, out int overflow);
                foreach (var chip in laid) DrawChip(chip, false);
                if (overflow > 0)
                    Text($"+{overflow}", p.X + p.Width - S(12) - TextW($"+{overflow}", 11), p.Y + p.Height - S(18), 11, COLOR_ACCENT_DIM);
            }
            else
            {
                float y = DrawWrapped("Mélangez les bons ingrédients pour la débloquer.", p.X + S(12), p.Y + S(38), p.Width - S(24), 11, COLOR_TEXT_DIM);
                int n = recipe.Ingredients.Count;
                Text($"{n} ingrédient{(n > 1 ? "s" : "")} différent{(n > 1 ? "s" : "")}", p.X + S(12), y + S(2), 11, COLOR_ACCENT_DIM);
            }
        }

        // ----- Bandeau "recette apprise" -----
        private static void DrawToast(UILayout L)
        {
            if (_unlockMessageTimer <= 0f || _unlockMessage.Length == 0) return;

            float fadeOut = MathF.Min(1f, _unlockMessageTimer / 0.6f);
            float slideIn = Math.Clamp((_unlockMessageDuration - _unlockMessageTimer) / 0.25f, 0f, 1f);
            int tw = TextW(_unlockMessage, 14);
            var r = new Rectangle(
                L.DropTarget.X + (L.DropTarget.Width - tw - S(52)) / 2f,
                L.DropTarget.Y + S(8) - (1f - EaseOutCubic(slideIn)) * S(14),
                tw + S(52), S(32));

            FillRound(r, S(16), C(16, 14, 24, (int)(235 * fadeOut)));
            LineRound(r, S(16), 1.5f, Fade(COLOR_ACCENT, fadeOut));

            // petite étoile
            Vector2 sc = new Vector2(r.X + S(18), r.Y + r.Height / 2f);
            float sz = S(5);
            Color star = Fade(COLOR_ACCENT, fadeOut);
            Raylib.DrawTriangle(new Vector2(sc.X, sc.Y - sz), new Vector2(sc.X - sz * 0.45f, sc.Y), new Vector2(sc.X + sz * 0.45f, sc.Y), star);
            Raylib.DrawTriangle(new Vector2(sc.X, sc.Y + sz), new Vector2(sc.X + sz * 0.45f, sc.Y), new Vector2(sc.X - sz * 0.45f, sc.Y), star);

            Text(_unlockMessage, r.X + S(32), r.Y + (r.Height - FS(14)) / 2f - 1, 14, Fade(C(255, 224, 120), fadeOut));
        }
    }
}
