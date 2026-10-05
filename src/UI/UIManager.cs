// UIManager.cs - Version complète avec textures de ceinture et sac à dos
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class UIManager
    {
        // Panneaux principaux
        public static NineSliceTexture? InventoryPanel { get; private set; }
        public static NineSliceTexture? BookPanelTexture { get; private set; }
        public static NineSliceTexture? WoodPanel { get; private set; }
        public static NineSliceTexture? SlotTexture { get; private set; }
        public static Texture2D ItemSlotTexture { get; private set; }
        public static Texture2D ItemSlotHoverTexture { get; private set; }
        public static Texture2D ItemSlotPressedTexture { get; private set; }
        private static Texture2D ItemSlotBorderUpTexture { get; set; }
        private static Texture2D ItemSlotBorderDownTexture { get; set; }
        private static Texture2D ItemSlotBorderLeftTexture { get; set; }
        private static Texture2D ItemSlotBorderRightTexture { get; set; }
        public static int ItemSlotSize => (int)MathF.Round(22f * UIScale);
        public static int ItemSlotStep => ItemSlotSize - (int)MathF.Round(3f * UIScale);
        public static int ItemContentSize => (int)MathF.Round(16f * UIScale);
        public static NineSliceTexture? ButtonTexture { get; private set; }
        public static NineSliceTexture? CraftPanelTexture { get; private set; }
        public static NineSliceTexture? RecipePanelTexture { get; private set; }
        public static NineSliceTexture? TablePanelTexture { get; private set; }
		
		public static Texture2D HeadSlotIcon { get; private set; }
		public static Texture2D BodySlotIcon { get; private set; }
		public static Texture2D LegsSlotIcon { get; private set; }
		public static Texture2D MainHandSlotIcon { get; private set; }
		public static Texture2D OffHandSlotIcon { get; private set; }
		public static Texture2D BackpackSlotIcon { get; private set; }
        
        // Nouveaux panneaux pour l'inventaire modulaire
        public static NineSliceTexture? BeltPanelTexture { get; private set; }
        public static NineSliceTexture? BackpackPanelTexture { get; private set; }
        // Nine-slice "parchemin" utilisé par le menu pause
        public static NineSliceTexture? ScrollPanelTexture { get; private set; }
        // Nine-slice pour bulle de dialogue
        public static NineSliceTexture? TalkBubblePanelTexture { get; private set; }
        // Nine-slice pour la case du prénom dans la bulle de dialogue (skin healthbar)
        public static NineSliceTexture? HealthBarPanelTexture { get; private set; }
        
        // Facteur d'échelle global pour l'UI (pixel art)
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;
        public const float ReferenceScale = 4f;
        public static float UIScale = ReferenceScale;
        public static float LayoutScale { get; private set; } = 1f;
        
        private static bool _initialized = false;
        
        // Gestionnaire de pile UI (fenêtres modales)
        private static Stack<Action> _uiCloseStack = new Stack<Action>();
        private static Stack<Func<bool>> _uiIsOpenStack = new Stack<Func<bool>>();
        
        public static void Initialize(float scale = 4f)
        {
            if (_initialized) return;
            
            UIScale = scale;
            
            InventoryPanel = new NineSliceTexture("assets/gui/inventory", UIScale);
            BookPanelTexture = new NineSliceTexture("assets/gui/book", UIScale);
            WoodPanel = new NineSliceTexture("assets/gui/wood", UIScale);
            SlotTexture = new NineSliceTexture("assets/gui/slot", UIScale);
            ItemSlotTexture = LoadSlotIcon("assets/gui/item_slot.png");
            ItemSlotHoverTexture = LoadSlotIcon("assets/gui/item_slot_hover.png");
            ItemSlotPressedTexture = LoadSlotIcon("assets/gui/item_slot_pressed.png");
            ItemSlotBorderUpTexture = LoadSlotIcon("assets/gui/item_slot_border_up.png");
            ItemSlotBorderDownTexture = LoadSlotIcon("assets/gui/item_slot_border_down.png");
            ItemSlotBorderLeftTexture = LoadSlotIcon("assets/gui/item_slot_border_left.png");
            ItemSlotBorderRightTexture = LoadSlotIcon("assets/gui/item_slot_border_right.png");
            ButtonTexture = new NineSliceTexture("assets/gui/classic_button", UIScale);
            CraftPanelTexture = new NineSliceTexture("assets/gui/craft", UIScale);
            RecipePanelTexture = new NineSliceTexture("assets/gui/recipe", UIScale);
            TablePanelTexture = new NineSliceTexture("assets/gui/table", UIScale);
            BeltPanelTexture = new NineSliceTexture("assets/gui/belt", UIScale);
            BackpackPanelTexture = new NineSliceTexture("assets/gui/backpack", UIScale);
            ScrollPanelTexture = new NineSliceTexture("assets/gui/scroll", UIScale);
            TalkBubblePanelTexture = new NineSliceTexture("assets/gui/talkbubble", UIScale);
            HealthBarPanelTexture = new NineSliceTexture("assets/gui/healthbar", UIScale);
            
            HeadSlotIcon = LoadSlotIcon("assets/gui/slot_head.png");
            BodySlotIcon = LoadSlotIcon("assets/gui/slot_body.png");
            LegsSlotIcon = LoadSlotIcon("assets/gui/slot_legs.png");
            MainHandSlotIcon = LoadSlotIcon("assets/gui/slot_mainhand.png");
            OffHandSlotIcon = LoadSlotIcon("assets/gui/slot_offhand.png");
            BackpackSlotIcon = LoadSlotIcon("assets/gui/slot_backpack.png");
            
            _initialized = true;
            Console.WriteLine();
        }

        public static float Scale(float value) => value * LayoutScale;

        public static int ScaleInt(float value) => Math.Max(1, (int)MathF.Round(Scale(value)));

        public static Vector2 ScalePosition(float x, float y) => new(Scale(x), Scale(y));

        public static Rectangle ScaleRectangle(float x, float y, float width, float height) =>
            new(Scale(x), Scale(y), Scale(width), Scale(height));

        public static void UpdateForViewport(int width, int height)
        {
            if (width <= 0 || height <= 0) return;

            float newLayoutScale = MathF.Min(width / ReferenceWidth, height / ReferenceHeight);
            if (MathF.Abs(newLayoutScale - LayoutScale) < 0.001f)
                return;

            LayoutScale = newLayoutScale;
            UIScale = ReferenceScale * LayoutScale;

            if (!_initialized) return;

            if (InventoryPanel != null) InventoryPanel.Scale = UIScale;
            if (BookPanelTexture != null) BookPanelTexture.Scale = UIScale;
            if (WoodPanel != null) WoodPanel.Scale = UIScale;
            if (SlotTexture != null) SlotTexture.Scale = UIScale;
            if (ButtonTexture != null) ButtonTexture.Scale = UIScale;
            if (CraftPanelTexture != null) CraftPanelTexture.Scale = UIScale;
            if (RecipePanelTexture != null) RecipePanelTexture.Scale = UIScale;
            if (TablePanelTexture != null) TablePanelTexture.Scale = UIScale;
            if (BeltPanelTexture != null) BeltPanelTexture.Scale = UIScale;
            if (BackpackPanelTexture != null) BackpackPanelTexture.Scale = UIScale;
            if (ScrollPanelTexture != null) ScrollPanelTexture.Scale = UIScale;
            if (TalkBubblePanelTexture != null) TalkBubblePanelTexture.Scale = UIScale;
            if (HealthBarPanelTexture != null) HealthBarPanelTexture.Scale = UIScale;
        }
		
		private static Texture2D LoadSlotIcon(string path)
		{
			if (File.Exists(path))
				return Raylib.LoadTexture(path);
			return new Texture2D();
		}
        
        public static void Unload()
        {
            if (InventoryPanel != null) InventoryPanel.Unload();
            if (BookPanelTexture != null) BookPanelTexture.Unload();
            if (WoodPanel != null) WoodPanel.Unload();
            if (SlotTexture != null) SlotTexture.Unload();
            ItemSlotTexture = UnloadTexture(ItemSlotTexture);
            ItemSlotHoverTexture = UnloadTexture(ItemSlotHoverTexture);
            ItemSlotPressedTexture = UnloadTexture(ItemSlotPressedTexture);
            ItemSlotBorderUpTexture = UnloadTexture(ItemSlotBorderUpTexture);
            ItemSlotBorderDownTexture = UnloadTexture(ItemSlotBorderDownTexture);
            ItemSlotBorderLeftTexture = UnloadTexture(ItemSlotBorderLeftTexture);
            ItemSlotBorderRightTexture = UnloadTexture(ItemSlotBorderRightTexture);
            if (ButtonTexture != null) ButtonTexture.Unload();
            if (CraftPanelTexture != null) CraftPanelTexture.Unload();
            if (RecipePanelTexture != null) RecipePanelTexture.Unload();
            if (TablePanelTexture != null) TablePanelTexture.Unload();
            if (BeltPanelTexture != null) BeltPanelTexture.Unload();
            if (BackpackPanelTexture != null) BackpackPanelTexture.Unload();
            if (ScrollPanelTexture != null) ScrollPanelTexture.Unload();
            if (TalkBubblePanelTexture != null) TalkBubblePanelTexture.Unload();
            if (HealthBarPanelTexture != null) HealthBarPanelTexture.Unload();
            _initialized = false;
        }

        public static void DrawItemSlotBackground(
            Rectangle rect,
            bool hovered,
            bool pressed,
            Color fallbackColor,
            bool borderUp = false,
            bool borderDown = false,
            bool borderLeft = false,
            bool borderRight = false)
        {
            Texture2D texture = pressed && ItemSlotPressedTexture.Id != 0
                ? ItemSlotPressedTexture
                : hovered && ItemSlotHoverTexture.Id != 0
                    ? ItemSlotHoverTexture
                    : ItemSlotTexture;

            if (texture.Id != 0)
            {
                Raylib.DrawTexturePro(
                    texture,
                    new Rectangle(0, 0, texture.Width, texture.Height),
                    rect,
                    Vector2.Zero,
                    0f,
                    Color.White);
                DrawSlotBorder(ItemSlotBorderUpTexture, rect, borderUp);
                DrawSlotBorder(ItemSlotBorderDownTexture, rect, borderDown);
                DrawSlotBorder(ItemSlotBorderLeftTexture, rect, borderLeft);
                DrawSlotBorder(ItemSlotBorderRightTexture, rect, borderRight);
                return;
            }

            Raylib.DrawRectangleRounded(rect, 0.1f, 6, fallbackColor);
            Raylib.DrawRectangleRoundedLines(rect, 0.1f, 6, 1, new Color(65, 68, 75, 255));
        }

        private static void DrawSlotBorder(Texture2D texture, Rectangle rect, bool visible)
        {
            if (!visible || texture.Id == 0) return;

            Raylib.DrawTexturePro(
                texture,
                new Rectangle(0, 0, texture.Width, texture.Height),
                rect,
                Vector2.Zero,
                0f,
                Color.White);
        }

        private static Texture2D UnloadTexture(Texture2D texture)
        {
            if (texture.Id != 0)
                Raylib.UnloadTexture(texture);
            return new Texture2D();
        }

        /// <summary>
        /// Dessine un bouton standard basé sur le nine-slice classic_button.
        /// Le tint est optionnel : laissez Color.White pour garder la texture blanche,
        /// ou passez une couleur personnalisée pour le recolorer.
        /// </summary>
        public static void DrawButton(Rectangle rect, Color? tint = null, bool hovered = false, bool enabled = true)
        {
            if (ButtonTexture != null && ButtonTexture.IsValid)
            {
                Color color = tint ?? Color.White;
                if (!enabled)
                {
                    color = new Color(
                        (byte)Math.Clamp(color.R * 0.65f, 0, 255),
                        (byte)Math.Clamp(color.G * 0.65f, 0, 255),
                        (byte)Math.Clamp(color.B * 0.65f, 0, 255),
                        (byte)Math.Clamp(color.A * 0.75f, 0, 255));
                }
                else if (hovered)
                {
                    color = new Color(
                        (byte)Math.Clamp(color.R + 20, 0, 255),
                        (byte)Math.Clamp(color.G + 20, 0, 255),
                        (byte)Math.Clamp(color.B + 20, 0, 255),
                        color.A);
                }

                ButtonTexture.Draw(rect, color);
                return;
            }

            Color fallbackColor = enabled ? (hovered ? new Color(110, 96, 72, 255) : new Color(85, 72, 54, 255)) : new Color(70, 65, 55, 220);
            Raylib.DrawRectangleRounded(rect, 0.18f, 8, fallbackColor);
            Raylib.DrawRectangleRoundedLines(rect, 0.18f, 8, 1, new Color(200, 170, 100, 220));
        }
        
        // ═══════════════════════════════════════════════════════════════════════════
        //  GESTIONNAIRE DE PILE UI (Fenêtres modales)
        // ═══════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Enregistre une UI dans la pile. La dernière fermée sera la première à se fermer.
        /// </summary>
        /// <param name="closeAction">Action à appeler pour fermer l'UI</param>
        /// <param name="isOpenCheck">Fonction retournant true si l'UI est encore ouverte</param>
        public static void PushUI(Action closeAction, Func<bool> isOpenCheck)
        {
            CleanupDeadUIs();
            _uiCloseStack.Push(closeAction);
            _uiIsOpenStack.Push(isOpenCheck);
        }
        
        /// <summary>
        /// Ferme la dernière UI ouverte (celle au sommet de la pile)
        /// </summary>
        /// <returns>True si une UI a été fermée, False sinon</returns>
        public static bool PopUI()
        {
            CleanupDeadUIs();
            
            if (_uiCloseStack.Count > 0 && _uiIsOpenStack.Peek().Invoke())
            {
                Action closeAction = _uiCloseStack.Pop();
                _uiIsOpenStack.Pop();
                closeAction?.Invoke();
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Ferme TOUTES les UI ouvertes immédiatement (sans animation)
        /// </summary>
        public static void CloseAllUIs()
        {
            while (_uiCloseStack.Count > 0)
            {
                if (_uiIsOpenStack.Peek().Invoke())
                    _uiCloseStack.Peek()?.Invoke();
                _uiCloseStack.Pop();
                _uiIsOpenStack.Pop();
            }
            
            _uiCloseStack.Clear();
            _uiIsOpenStack.Clear();
            Console.WriteLine(" Toutes les UI ont été fermées");
        }
        
        /// <summary>
        /// Vérifie si une UI est ouverte (peu importe laquelle).
        /// La table Pouilleux est un overlay de jeu de carte spécial : elle
        /// doit être exclue du blocage modal global, pour que le clic du monde
        /// continue jusqu’à TryInteractWithQuestNpc et TryInviteNpc.
        /// </summary>
        public static bool IsAnyUIOpen()
        {
            CleanupDeadUIs();
            if (CartesGameUI.IsOpen)
                return false;
            return _uiCloseStack.Count > 0;
        }
        
        /// <summary>
        /// Obtient le nombre d'UI actuellement ouvertes
        /// </summary>
        public static int GetUIOpenCount()
        {
            CleanupDeadUIs();
            return _uiCloseStack.Count;
        }
        
        /// <summary>
        /// Réinitialise la pile (au chargement de partie par exemple)
        /// </summary>
        public static void ResetUIStack()
        {
            _uiCloseStack.Clear();
            _uiIsOpenStack.Clear();
            Console.WriteLine(" Pile UI réinitialisée");
        }
        
        /// <summary>
        /// Nettoie la pile en supprimant les références aux UI déjà fermées
        /// </summary>
        private static void CleanupDeadUIs()
        {
            while (_uiCloseStack.Count > 0 && !_uiIsOpenStack.Peek().Invoke())
            {
                _uiCloseStack.Pop();
                _uiIsOpenStack.Pop();
            }
        }
        
        /// <summary>
        /// Obtient le nom de la dernière UI ouverte (pour debug)
        /// </summary>
        public static string GetTopUIName()
        {
            if (_uiCloseStack.Count == 0)
                return "Aucune";
            return $"UI #{_uiCloseStack.Count}";
        }
    }
}