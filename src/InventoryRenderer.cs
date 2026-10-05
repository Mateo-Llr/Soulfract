// InventoryRenderer.cs - Version avec système d'équipement dynamique (Emplacements multiples)
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;

namespace Soulfract
{
    public static class InventoryRenderer
    {
        private static int SLOT_SIZE => UIManager.ItemSlotSize;
        private static int SLOT_STEP => UIManager.ItemSlotStep;
        private static int ITEM_SIZE => UIManager.ItemContentSize;
        private static int ITEM_OFFSET => (SLOT_SIZE - ITEM_SIZE) / 2;
        private static int PANEL_PADDING => UIManager.ScaleInt(16);
        private const int SLOTS_PER_ROW = 5;
        private static int HEADER_HEIGHT => UIManager.ScaleInt(45);
        private static int MODULE_MARGIN_BOTTOM => UIManager.ScaleInt(20);
        private static int EQUIPMENT_SLOT_SIZE => UIManager.ScaleInt(48);

        public static int GetWindowX() => _mainPanelX;
        public static int GetWindowY() => _mainPanelY;
        public static int GetWindowWidth() => _mainPanelW;
        public static int GetWindowHeight() => _mainPanelH;

        public static bool IsOpen => IsOpenForPlayer(0);

        // Couleurs
        private static readonly Color COLOR_BG_DARK = new Color(25, 28, 35, 100);
        private static readonly Color COLOR_BG_MEDIUM = new Color(35, 38, 45, 255);
        private static readonly Color COLOR_SLOT_NORMAL = new Color(50, 53, 60, 255);
        private static readonly Color COLOR_SLOT_HOVER = new Color(70, 73, 80, 255);
        private static readonly Color COLOR_SLOT_SELECTED = new Color(100, 110, 80, 255);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_ACCENT_DARK = new Color(180, 150, 70, 255);
        private static readonly Color COLOR_BORDER = new Color(65, 68, 75, 255);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
        private static readonly Color COLOR_TOOLTIP_BG = new Color(20, 22, 28, 240);
        private static readonly Color COLOR_SLOT_EQUIPPED = new Color(210, 180, 100, 80);
        private static readonly Color COLOR_SLOT_FILLED = new Color(100, 180, 100, 60);

        // Textures 9-slice
        private static NineSliceTexture? _panelTexture;
        private static NineSliceTexture? _slotTexture;
        private static bool _useTextures = false;
        private static Texture2D _armorIconTexture = new();
        private static bool _armorIconLoaded = false;

        // État de l'inventaire
        public static List<InventorySlot> InventorySlots = new();
        public static Item? GetHotbarItem(int index)
        {
            if (index < 0 || index >= 10 || index >= InventorySlots.Count)
                return null;
            var slot = InventorySlots[index];
            return !slot.IsEmpty ? slot.Item : null;
        }

        public static bool TrySwapHoveredSlotWithHotbar(int hotbarIndex)
        {
            if (hotbarIndex < 0 || hotbarIndex >= 10 || hotbarIndex >= InventorySlots.Count || DraggedItem.IsDragging)
                return false;

            InventorySlot? hoveredSlot = null;
            if (HoveredSlot >= 0 && HoveredSlot < InventorySlots.Count)
            {
                hoveredSlot = InventorySlots[HoveredSlot];
            }
            else if (_hoveredContainerModule != null && _hoveredContainerSlotIndex >= 0)
            {
                hoveredSlot = _hoveredContainerModule.SlotGetter(_hoveredContainerSlotIndex);
            }

            if (hoveredSlot == null)
                return false;

            var hotbarSlot = InventorySlots[hotbarIndex];
            (hoveredSlot.Item, hotbarSlot.Item) = (hotbarSlot.Item, hoveredSlot.Item);
            (hoveredSlot.Count, hotbarSlot.Count) = (hotbarSlot.Count, hoveredSlot.Count);
            return true;
        }

        public static DraggedItem DraggedItem = new();
        public static int SelectedSlot = -1;
        public static int HoveredSlot = -1;
        public static bool IsInventoryOpen = false;

        // Variables pour le panneau déplaçable
        private static readonly Dictionary<int, Vector2> _windowPosByPlayer = new();
        private static readonly Dictionary<int, Vector2> _dragOffsetByPlayer = new();
        private static readonly Dictionary<int, bool> _isDraggingWindowByPlayer = new();
        private static readonly Dictionary<int, bool> _isHoveringTitleBarByPlayer = new();
        private static readonly Dictionary<int, bool> _isOpenByPlayer = new();
        private static readonly Dictionary<int, float> _scrollOffsetByPlayer = new();
        private static readonly Dictionary<int, float> _maxScrollByPlayer = new();

        // Variables pour l'infobulle
        private static InventorySlot? _tooltipSlot = null;
        private static float _tooltipHoverTimer = 0f;
        private const float TOOLTIP_DELAY = 0.4f;

        //  Stockage des rectangles d'équipement dynamiques (emplacements fonctionnels type
        // arme/bouclier uniquement : ceux-là gardent une petite zone ronde fixe, ça reste
        // adapté puisqu'il n'y a rien à distinguer visuellement à cet endroit).
        private static Dictionary<(EquipmentSlot? slot, BodyZone? zone), Rectangle> _equipSlotRects = new();
        private static Dictionary<(EquipmentSlot? slot, BodyZone? zone), Rectangle> _equipmentBarSlotRects = new();
        private static (EquipmentSlot? slot, BodyZone? zone)? _hoveredEquipSlot = null;

        //  Silhouettes réelles (une ou plusieurs pièces par zone, une par bone couvert) des
        // items ACTUELLEMENT ÉQUIPÉS, recalculées chaque frame à partir du sprite réellement
        // affiché sur le personnage - remplace les anciens ronds de survol approximatifs.
        private static Dictionary<BodyZone, List<EntityRenderer.ZonePieceRect>> _zonePieceRects = new();

        //  Silhouettes réelles de l'item EN COURS DE DRAG, calculées pour chacune des zones
        // qu'il couvre réellement (CoveredZones), avec sa propre texture. Sert à la fois de
        // zone de dépôt cliquable précise ET d'aperçu fantôme dessiné sur le personnage.
        private static Dictionary<BodyZone, List<EntityRenderer.ZonePieceRect>> _dragZonePieceRects = new();

        //  Même principe que _zonePieceRects/_dragZonePieceRects, mais pour les emplacements
        // FONCTIONNELS (MainHand/OffHand) : la vraie texture de l'objet actuellement équipé
        // (respectivement en cours de drag), pour un survol/aperçu fantôme cohérent avec le
        // reste de l'équipement au lieu d'un rond générique.
        private static Dictionary<EquipmentSlot, EntityRenderer.ZonePieceRect> _slotPieceRects = new();
        private static Dictionary<EquipmentSlot, EntityRenderer.ZonePieceRect> _dragSlotPieceRects = new();

        //  Ordre "du bas vers le haut" utilisé pour départager deux zones dont les silhouettes
        // se chevauchent sur un même pixel (ex: sac à dos et écharpe, tous deux ancrés sur le
        // bone "body") : on reprend EXACTEMENT l'ordre dans lequel DrawEntity dessine ces
        // pièces, pour que le clic et l'infobulle correspondent toujours à ce qui est
        // visuellement au premier plan.
        //  Ordre confirmé dans EntityRenderer.DrawEntity (bone "body") : Torse -> Sac à dos ->
        // variante buste des jambes -> Collier/écharpe -> Ceinture (chacun dessiné par-dessus
        // le précédent). Ordre confirmé (bone "head") : Casque/chapeau -> cheveux/yeux ->
        // Boucles d'oreilles -> Lunettes/masque. Les Chaussettes (bone jambe) sont dessinées
        // dans une passe séparée, avant la tête ; on les place donc tout en bas par défaut.
        private static readonly BodyZone[] _zoneHitPriority =
        {
            BodyZone.Feet,
            BodyZone.TopOfHead,
            BodyZone.Ears,
            BodyZone.Face,
            BodyZone.Torso,
            BodyZone.Back,
            BodyZone.Legs,
            BodyZone.Neck,
            BodyZone.Waist,
            BodyZone.Hands,
        };

        //  Test de collision précis (bbox + alpha du pixel réellement survolé) sur un jeu de
        // silhouettes par zone. Renvoie la zone la plus "au-dessus" dont un pixel opaque se
        // trouve sous la souris, ou false si la souris n'est sur aucune silhouette réelle -
        // c'est ce qui permet à un chapeau et des lunettes de rester distinctement cliquables
        // même si leurs zones de survol se chevauchent en boîte englobante.
        private static bool TryHitZonePieces(Dictionary<BodyZone, List<EntityRenderer.ZonePieceRect>> src, Vector2 mp, out BodyZone hitZone)
        {
            BodyZone? best = null;
            foreach (var zone in _zoneHitPriority)
            {
                if (!src.TryGetValue(zone, out var pieces)) continue;
                foreach (var piece in pieces)
                {
                    if (piece.Texture.Id == 0 || piece.Rect.Width <= 0 || piece.Rect.Height <= 0) continue;
                    if (!Raylib.CheckCollisionPointRec(mp, piece.Rect)) continue;

                    int localX = (int)((mp.X - piece.Rect.X) / piece.Rect.Width * piece.Texture.Width);
                    int localY = (int)((mp.Y - piece.Rect.Y) / piece.Rect.Height * piece.Texture.Height);
                    if (Equipment.IsPixelOpaque(piece.TexturePath, localX, localY))
                        best = zone; // on continue : une zone plus "haute" dans la priorité peut encore matcher
                }
            }
            hitZone = best ?? default;
            return best != null;
        }

        private static bool IsSlotPieceHit(EntityRenderer.ZonePieceRect piece, Vector2 mp)
        {
            if (piece.Texture.Id == 0 || !Raylib.CheckCollisionPointRec(mp, piece.Rect))
                return false;

            int localX = (int)((mp.X - piece.Rect.X) / piece.Rect.Width * piece.Texture.Width);
            int localY = (int)((mp.Y - piece.Rect.Y) / piece.Rect.Height * piece.Texture.Height);
            if (piece.FlipX)
                localX = piece.Texture.Width - 1 - localX;

            return Equipment.IsPixelOpaque(piece.TexturePath, localX, localY);
        }

        // Dimensions calculées
        private static int _screenWidth, _screenHeight;
        private static int _mainPanelX, _mainPanelY, _mainPanelW, _mainPanelH;
        private static int _equipmentAreaY;
        private static int _modulesStartY;

        // Stockage des informations de chaque module
        private class ModuleInfo
        {
            public string Title = string.Empty;
            public int SlotStartIndex;
            public int SlotCount;
            public int Rows;
            public Rectangle Bounds;
            public NineSliceTexture? CustomTexture;
            public Func<int, InventorySlot?> SlotGetter = _ => null!;
        }
        private static List<ModuleInfo> _modules = new();

        //  Nouveaux champs pour le survol des conteneurs
        private static ModuleInfo? _hoveredContainerModule = null;
        private static int _hoveredContainerSlotIndex = -1;

        public static void Initialize(int slotCount = 10)
        {
            InventorySlots.Clear();
            for (int i = 0; i < slotCount; i++)
                InventorySlots.Add(new InventorySlot());
            InitializeUI();
        }

        private static void InitializeUI()
        {
            _panelTexture = UIManager.InventoryPanel;
            _slotTexture = UIManager.SlotTexture;
            _useTextures = (_panelTexture != null && _panelTexture.IsValid);
            EnsureArmorIconTexture();
        }

        private static void EnsureArmorIconTexture()
        {
            if (_armorIconLoaded) return;
            _armorIconLoaded = true;
            _armorIconTexture = Raylib.LoadTexture("assets/gui/char_armor.png");
        }

        private static void EnsurePlayerState(int playerIndex)
        {
            if (!_windowPosByPlayer.ContainsKey(playerIndex)) _windowPosByPlayer[playerIndex] = new Vector2(100, 100);
            if (!_dragOffsetByPlayer.ContainsKey(playerIndex)) _dragOffsetByPlayer[playerIndex] = Vector2.Zero;
            if (!_isDraggingWindowByPlayer.ContainsKey(playerIndex)) _isDraggingWindowByPlayer[playerIndex] = false;
            if (!_isHoveringTitleBarByPlayer.ContainsKey(playerIndex)) _isHoveringTitleBarByPlayer[playerIndex] = false;
            if (!_isOpenByPlayer.ContainsKey(playerIndex)) _isOpenByPlayer[playerIndex] = false;
            if (!_scrollOffsetByPlayer.ContainsKey(playerIndex)) _scrollOffsetByPlayer[playerIndex] = 0f;
            if (!_maxScrollByPlayer.ContainsKey(playerIndex)) _maxScrollByPlayer[playerIndex] = 0f;
        }

        private static Vector2 GetWindowPos(int playerIndex)
        {
            EnsurePlayerState(playerIndex);
            return _windowPosByPlayer[playerIndex];
        }

        private static void SetWindowPos(int playerIndex, Vector2 pos)
        {
            EnsurePlayerState(playerIndex);
            _windowPosByPlayer[playerIndex] = pos;
        }

        public static bool IsOpenForPlayer(int playerIndex)
        {
            EnsurePlayerState(playerIndex);
            return _isOpenByPlayer[playerIndex];
        }

        public static void Open(int playerIndex = 0)
        {
            EnsurePlayerState(playerIndex);
            if (IsOpenForPlayer(playerIndex)) return;
            _isOpenByPlayer[playerIndex] = true;
            if (playerIndex == 0) IsInventoryOpen = true;
            UIManager.PushUI(() => Close(playerIndex), () => IsOpenForPlayer(playerIndex));
        }

        public static void Close(int playerIndex = 0)
        {
            EnsurePlayerState(playerIndex);
            _isOpenByPlayer[playerIndex] = false;
            if (playerIndex == 0) IsInventoryOpen = false;
            if (playerIndex == 0) SkillsTabRenderer.Close();
            _tooltipSlot = null;
            _tooltipHoverTimer = 0f;
            _isDraggingWindowByPlayer[playerIndex] = false;
            _dragOffsetByPlayer[playerIndex] = Vector2.Zero;
            SelectedSlot = -1;
            HoveredSlot = -1;
            _hoveredContainerModule = null;
            _hoveredContainerSlotIndex = -1;
            _hoveredEquipSlot = null;
        }

        private static void RestoreSavedItemData(Item item, InventorySlotSave save, ItemData itemData)
        {
            item.RestoreMetadataFromSave(save.Metadata ?? "");
            if (save.Meta != null && save.Meta.Count > 0)
            {
                foreach (var kv in save.Meta)
                    item.Meta[kv.Key] = kv.Value;
            }
            SaveSystem.RestoreCoatVariantMeta(item, itemData);
        }

        public static void RestoreFromSave(List<InventorySlotSave> savedSlots)
        {
            if (savedSlots == null || savedSlots.Count == 0)
            {
                Initialize(Equipment.BELT_SLOTS);
                return;
            }

            int maxSlots = savedSlots.Max(s => s.SlotIndex) + 1;
            //  GARDE-FOU : l'UI (module ceinture notamment) suppose toujours au moins
            // Equipment.BELT_SLOTS slots disponibles. Une sauvegarde incomplète/corrompue
            // (ex : SlotIndex jamais renseigné) ne doit jamais produire un inventaire plus
            // petit que ça, sous peine de IndexOutOfRange au premier rendu.
            if (maxSlots < Equipment.BELT_SLOTS) maxSlots = Equipment.BELT_SLOTS;
            var newSlots = new List<InventorySlot>();
            for (int i = 0; i < maxSlots; i++)
                newSlots.Add(new InventorySlot());

            foreach (var slotSave in savedSlots)
            {
                if (slotSave.SlotIndex >= 0 && slotSave.SlotIndex < newSlots.Count)
                {
                    var slot = newSlots[slotSave.SlotIndex];

                    if (!slotSave.IsEmpty && !string.IsNullOrEmpty(slotSave.ItemName) &&
                        GameData.TryGetItemByName(slotSave.ItemName, out var itemData))
                    {
                        slot.Item = new Item(itemData.Name, slotSave.Count, itemData.Color, itemData.Icon);
                        slot.Count = slotSave.Count;

                        if (slotSave.CustomColors != null && slotSave.CustomColors.Count > 0)
                        {
                            var colors = new List<Color?>();
                            foreach (var colorSave in slotSave.CustomColors)
                            {
                                if (colorSave != null)
                                    colors.Add(new Color(colorSave.R, colorSave.G, colorSave.B, colorSave.A));
                                else
                                    colors.Add(null);
                            }
                            slot.Item.CustomColors = colors;
                        }

                        RestoreSavedItemData(slot.Item, slotSave, itemData);

                        if (slotSave.PortableContainer != null)
                        {
                            var container = new PortableContainerData(slotSave.PortableContainer.Slots.Count, slotSave.PortableContainer.Columns);
                            for (int i = 0; i < slotSave.PortableContainer.Slots.Count && i < container.Inventory.Slots.Count; i++)
                            {
                                var savedSlot = slotSave.PortableContainer.Slots[i];
                                if (!savedSlot.IsEmpty && !string.IsNullOrEmpty(savedSlot.ItemName))
                                {
                                    var contItemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == savedSlot.ItemName);
                                    if (contItemData.ID != 0)
                                    {
                                        var contItem = new Item(contItemData.Name, savedSlot.Count, contItemData.Color, contItemData.Icon);
                                        RestoreSavedItemData(contItem, savedSlot, contItemData);
                                        if (savedSlot.CustomColors != null && savedSlot.CustomColors.Count > 0)
                                        {
                                            var colors = new List<Color?>();
                                            foreach (var colorSave in savedSlot.CustomColors)
                                            {
                                                if (colorSave != null)
                                                    colors.Add(new Color(colorSave.R, colorSave.G, colorSave.B, colorSave.A));
                                                else
                                                    colors.Add(null);
                                            }
                                            contItem.CustomColors = colors;
                                        }
                                        container.Inventory.Slots[i].Item = contItem;
                                        container.Inventory.Slots[i].Count = savedSlot.Count;
                                    }
                                }
                            }
                            slot.Item.Container = container;
                        }

                        if (slotSave.Backpack != null)
                        {
                            var backpack = new BackpackData(slotSave.Backpack.Slots.Count, slotSave.Backpack.Columns);
                            for (int i = 0; i < slotSave.Backpack.Slots.Count && i < backpack.Inventory.Slots.Count; i++)
                            {
                                var savedSlot = slotSave.Backpack.Slots[i];
                                if (!savedSlot.IsEmpty && !string.IsNullOrEmpty(savedSlot.ItemName) &&
                                    GameData.TryGetItemByName(savedSlot.ItemName, out var backItemData))
                                {
                                    Color? backCustomColor = null;
                                    var backItem = new Item(backItemData.Name, savedSlot.Count, backItemData.Color, backItemData.Icon);
                                    RestoreSavedItemData(backItem, savedSlot, backItemData);
                                    backpack.Inventory.Slots[i].Item = backItem;
                                    backpack.Inventory.Slots[i].Count = savedSlot.Count;
                                }
                            }
                            slot.Item.Backpack = backpack;
                        }
                    }
                }
            }

            InventorySlots = newSlots;
        }

        private static void MergeInventoryStacks()
        {
            //  Fusion des piles d'items de même nom et même couleur.
            // Après la restauration, des items du même type peuvent être séparés en
            // plusieurs slots, ce qui ne doit pas se produire (un seul slot par type+couleur).
            for (int i = InventorySlots.Count - 1; i >= 0; i--)
            {
                var slot = InventorySlots[i];
                if (slot.IsEmpty || slot.Item == null) continue;

                // Chercher les autres piles du même item
                for (int j = i + 1; j < InventorySlots.Count; j++)
                {
                    var otherSlot = InventorySlots[j];
                    if (otherSlot.IsEmpty || otherSlot.Item == null) continue;
                    
                    // Même nom et même couleur ?
                    if (Program.AreItemsStackable(slot.Item, otherSlot.Item))
                    {
                        // Fusionner
                        slot.Count += otherSlot.Count;
                        otherSlot.Clear();
                    }
                }
            }

            // Compacter les slots vides
            var compacted = InventorySlots.Where(s => !s.IsEmpty).ToList();
            while (compacted.Count < InventorySlots.Count)
                compacted.Add(new InventorySlot());
            InventorySlots = compacted;
        }

        private static bool AreColorsCompatible(Item a, Item b)
        {
            //  Deux items sont "compatibles de couleur" s'ils ont exactement la même liste
            // de CustomColors. Cela inclut le cas où les deux n'ont aucune couleur perso.
            if (a.CustomColors == null && b.CustomColors == null) return true;
            if (a.CustomColors == null || b.CustomColors == null) return false;
            if (a.CustomColors.Count != b.CustomColors.Count) return false;
            
            for (int i = 0; i < a.CustomColors.Count; i++)
            {
                Color? colA = a.CustomColors[i];
                Color? colB = b.CustomColors[i];
                if (colA == null && colB == null) continue;
                if (colA == null || colB == null) return false;
                if (!colA.Value.Equals(colB.Value)) return false;
            }
            return true;
        }

        public static void Update(int playerIndex = 0, Rectangle? viewport = null)
        {
            EnsurePlayerState(playerIndex);
            if (!IsOpenForPlayer(playerIndex)) return;
            if (playerIndex == 0 && SlimeStorageUI.IsMouseOverWindow(Raylib.GetMousePosition())) return;

            _screenWidth = viewport.HasValue ? (int)viewport.Value.Width : Raylib.GetScreenWidth();
            _screenHeight = viewport.HasValue ? (int)viewport.Value.Height : Raylib.GetScreenHeight();

            int totalWidth = SLOTS_PER_ROW * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
            _mainPanelW = totalWidth + PANEL_PADDING * 2;
            if (SettingsManager.Settings.InventoryEquipmentStyle == "character_with_slots")
                _mainPanelW = Math.Max(_mainPanelW + UIManager.ScaleInt(90), 220 + UIManager.ScaleInt(200));
            int verticalMargin = UIManager.ScaleInt(120);
            _mainPanelH = Math.Clamp(_screenHeight - verticalMargin, UIManager.ScaleInt(300), _screenHeight - verticalMargin);

            var windowPos = GetWindowPos(playerIndex);
            _mainPanelX = (int)windowPos.X;
            _mainPanelY = (int)windowPos.Y;
            _mainPanelX = Math.Clamp(_mainPanelX, -_mainPanelW + 100, _screenWidth - 100);
            _mainPanelY = Math.Clamp(_mainPanelY, 0, _screenHeight - 100);
            SetWindowPos(playerIndex, new Vector2(_mainPanelX, _mainPanelY));

            _equipmentAreaY = _mainPanelY + HEADER_HEIGHT + 10;
            _modulesStartY = _equipmentAreaY + CalculateEquipmentHeight() + 30;

            RebuildModules();

            HoveredSlot = -1;
            _hoveredContainerModule = null;
            _hoveredContainerSlotIndex = -1;
            _hoveredEquipSlot = null;
            Vector2 mousePos = Raylib.GetMousePosition();
            int scrollOffset = (int)_scrollOffsetByPlayer[playerIndex];

            // 1. Vérifier les slots de la ceinture
            for (int i = 0; i < InventorySlots.Count; i++)
            {
                ModuleInfo? owner = _modules.FirstOrDefault(m => i >= m.SlotStartIndex && i < m.SlotStartIndex + m.SlotCount);
                if (owner == null) continue;

                int col = (i - owner.SlotStartIndex) % SLOTS_PER_ROW;
                int row = (i - owner.SlotStartIndex) / SLOTS_PER_ROW;
                int startX = (int)owner.Bounds.X + PANEL_PADDING;
                int startY = (int)owner.Bounds.Y + PANEL_PADDING + (string.IsNullOrEmpty(owner.Title) ? 0 : 20) - scrollOffset;
                int slotX = startX + col * SLOT_STEP;
                int slotY = startY + row * SLOT_STEP;
                if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE)))
                {
                    HoveredSlot = i;
                    break;
                }
            }

            // 2. Vérifier les slots des conteneurs
            if (HoveredSlot == -1)
            {
                foreach (var module in _modules.Where(m => m.SlotStartIndex == -1))
                {
                    for (int i = module.SlotCount - 1; i >= 0; i--)
                    {
                        int col = i % SLOTS_PER_ROW;
                        int row = i / SLOTS_PER_ROW;
                        int startX = (int)module.Bounds.X + PANEL_PADDING;
                        int startY = (int)module.Bounds.Y + PANEL_PADDING + (string.IsNullOrEmpty(module.Title) ? 0 : 20) - scrollOffset;
                        int slotX = startX + col * SLOT_STEP;
                        int slotY = startY + row * SLOT_STEP;
                        if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE)))
                        {
                            _hoveredContainerModule = module;
                            _hoveredContainerSlotIndex = i;
                            HoveredSlot = -2;
                            break;
                        }
                    }
                    if (HoveredSlot != -1) break;
                }
            }

            // 3. Vérifier les slots d'équipement
            if (HoveredSlot == -1 && _hoveredContainerModule == null)
            {
                // Emplacements fonctionnels (arme/bouclier/offhand) EN PREMIER : même raison
                // qu'au clic (HandleSlotClick) - l'offhand partage le bone "larmbottom" avec
                // la manche d'un vêtement de torse, et doit rester prioritaire pour le survol
                // et l'infobulle, sans quoi elle affiche la manche au lieu de l'objet en main.
                bool hitSlot = false;
                foreach (var kv in _slotPieceRects)
                {
                    if (!IsSlotPieceHit(kv.Value, mousePos))
                        continue;

                    _hoveredEquipSlot = (kv.Key, null);
                    hitSlot = true;
                    break;
                }

                foreach (var kv in _equipSlotRects)
                {
                    if (hitSlot) break;
                    if (kv.Key.slot == null) continue;
                    if (_slotPieceRects.ContainsKey(kv.Key.slot.Value)) continue;
                    if (Raylib.CheckCollisionPointRec(mousePos, kv.Value))
                    {
                        _hoveredEquipSlot = kv.Key;
                        hitSlot = true;
                        break;
                    }
                }

                foreach (var kv in _equipmentBarSlotRects)
                {
                    if (hitSlot) break;
                    if (!Raylib.CheckCollisionPointRec(mousePos, kv.Value)) continue;
                    _hoveredEquipSlot = kv.Key;
                    hitSlot = true;
                }

                // Zones du corps (armure/accessoires) : test précis sur la silhouette réelle
                // de l'item équipé, pas sur une case ni un rond générique.
                if (!hitSlot && TryHitZonePieces(_zonePieceRects, mousePos, out var hitZone))
                {
                    _hoveredEquipSlot = (null, hitZone);
                }
            }

            int modulesViewportTop = _mainPanelY + HEADER_HEIGHT + 10 + CalculateEquipmentHeight() + 10;
            int modulesViewportHeight = Math.Max(120, _mainPanelH - (modulesViewportTop - _mainPanelY));
            Rectangle modulesViewportRect = new Rectangle(_mainPanelX, modulesViewportTop, _mainPanelW, modulesViewportHeight);
            float fullContentHeight = Math.Max(0f, _modulesStartY - modulesViewportTop + PANEL_PADDING + 20);
            _maxScrollByPlayer[playerIndex] = Math.Max(0f, fullContentHeight - modulesViewportHeight);
            _scrollOffsetByPlayer[playerIndex] = Math.Clamp(_scrollOffsetByPlayer[playerIndex], 0f, _maxScrollByPlayer[playerIndex]);

            Vector2 mousePosViewport = Raylib.GetMousePosition();
            if (Raylib.CheckCollisionPointRec(mousePosViewport, modulesViewportRect))
            {
                float wheel = Raylib.GetMouseWheelMove();
                if (wheel != 0f)
                {
                    _scrollOffsetByPlayer[playerIndex] -= wheel * 48f;
                    _scrollOffsetByPlayer[playerIndex] = Math.Clamp(_scrollOffsetByPlayer[playerIndex], 0f, _maxScrollByPlayer[playerIndex]);
                }
            }

            _tooltipSlot = null;
            if ((HoveredSlot != -1 || _hoveredContainerModule != null || _hoveredEquipSlot != null) && !DraggedItem.IsDragging)
            {
                if (_tooltipHoverTimer < TOOLTIP_DELAY)
                    _tooltipHoverTimer += Raylib.GetFrameTime();
                else
                {
                    _tooltipSlot = GetSlotAtHoveredPosition();
                }
            }
            else
            {
                _tooltipHoverTimer = 0f;
            }

            if (!DraggedItem.IsDragging)
            {
                for (int k = 1; k <= 9; k++)
                {
                    if (Raylib.IsKeyPressed(KeyboardKey.One + (k - 1)))
                        TrySwapHoveredSlotWithHotbar(k - 1);
                }
                if (Raylib.IsKeyPressed(KeyboardKey.Zero))
                    TrySwapHoveredSlotWithHotbar(9);
            }

            if (DraggedItem.IsDragging && Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                Vector2 currentMousePos = Raylib.GetMousePosition();
                bool mouseOverUI = IsMouseOverInventoryUI(currentMousePos);

                if (!mouseOverUI)
                {
                    Program.ThrowDraggedItem(DraggedItem.Item, DraggedItem.Count);
                    DraggedItem.EndDrag();
                }
            }

            Rectangle titleBarRect = new Rectangle(_mainPanelX, _mainPanelY, _mainPanelW, HEADER_HEIGHT);
            _isHoveringTitleBarByPlayer[playerIndex] = Raylib.CheckCollisionPointRec(mousePos, titleBarRect);
            if (_isHoveringTitleBarByPlayer[playerIndex] && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDraggingWindowByPlayer[playerIndex] = true;
                _dragOffsetByPlayer[playerIndex] = mousePos - GetWindowPos(playerIndex);
            }
            if (_isDraggingWindowByPlayer[playerIndex] && Raylib.IsMouseButtonDown(MouseButton.Left))
                SetWindowPos(playerIndex, mousePos - _dragOffsetByPlayer[playerIndex]);
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDraggingWindowByPlayer[playerIndex] = false;
        }

        private static int CalculateEquipmentHeight()
        {
            return 320;
        }

        private static InventorySlot? GetSlotAtHoveredPosition()
        {
            if (HoveredSlot >= 0 && HoveredSlot < InventorySlots.Count)
                return InventorySlots[HoveredSlot];

            if (_hoveredContainerModule != null && _hoveredContainerSlotIndex >= 0)
            {
                return _hoveredContainerModule.SlotGetter(_hoveredContainerSlotIndex);
            }

            if (_hoveredEquipSlot != null)
            {
                var (slot, zone) = _hoveredEquipSlot.Value;
                Item? item = zone != null
                    ? Program.equipment.GetZoneItem(zone.Value)
                    : (slot != null ? Program.equipment.GetItemInSlot(slot.Value, 0) : null);
                if (item != null)
                {
                    return new InventorySlot(item, 1);
                }
            }

            return null;
        }

        private static bool IsMouseOverInventoryUI(Vector2 mousePos)
        {
            if (!IsInventoryOpen) return false;

            Rectangle windowRect = new Rectangle(_mainPanelX, _mainPanelY, _mainPanelW, _mainPanelH);
            return Raylib.CheckCollisionPointRec(mousePos, windowRect);
        }

        private static void RebuildModules()
        {
            _modules.Clear();
            int currentSlotIndex = 0;

            // Module Belt
            int beltSlotCount = Equipment.BELT_SLOTS;
            int beltRows = (int)Math.Ceiling((double)beltSlotCount / SLOTS_PER_ROW);
            int beltHeight = PANEL_PADDING * 2 + beltRows * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
            Rectangle beltBounds = new Rectangle(_mainPanelX, _modulesStartY, _mainPanelW, beltHeight);
            _modules.Add(new ModuleInfo
            {
                Title = "",
                SlotStartIndex = currentSlotIndex,
                SlotCount = beltSlotCount,
                Rows = beltRows,
                Bounds = beltBounds,
                CustomTexture = UIManager.BeltPanelTexture,
                SlotGetter = (i) => InventorySlots[i]
            });
            currentSlotIndex += beltSlotCount;
            _modulesStartY += beltHeight + MODULE_MARGIN_BOTTOM;

            // Module conteneur portable
            if (Program.equipment.OffHandContainer != null && Program.equipment.OffHand != null && IsPortableContainer(Program.equipment.OffHand))
            {
                var container = Program.equipment.OffHandContainer;
                int containerSlotCount = container.Inventory.Slots.Count;
                int containerRows = (int)Math.Ceiling((double)containerSlotCount / SLOTS_PER_ROW);
                int containerHeight = PANEL_PADDING * 2 + containerRows * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
                Rectangle containerBounds = new Rectangle(_mainPanelX, _modulesStartY, _mainPanelW, containerHeight);
                _modules.Add(new ModuleInfo
                {
                    Title = "PANIER",
                    SlotStartIndex = -1,
                    SlotCount = containerSlotCount,
                    Rows = containerRows,
                    Bounds = containerBounds,
                    CustomTexture = UIManager.BackpackPanelTexture,
                    SlotGetter = (i) => container.Inventory.Slots[i]
                });
                _modulesStartY += containerHeight + MODULE_MARGIN_BOTTOM;
            }

            // Module conteneur portable en main droite (si présent)
            if (Program.equipment.MainHandContainer != null && Program.equipment.MainHand != null && IsPortableContainer(Program.equipment.MainHand))
            {
                var container = Program.equipment.MainHandContainer;
                int containerSlotCount = container.Inventory.Slots.Count;
                int containerRows = (int)Math.Ceiling((double)containerSlotCount / SLOTS_PER_ROW);
                int containerHeight = PANEL_PADDING * 2 + containerRows * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
                Rectangle containerBounds = new Rectangle(_mainPanelX, _modulesStartY, _mainPanelW, containerHeight);
                _modules.Add(new ModuleInfo
                {
                    Title = "PANIER (main)",
                    SlotStartIndex = -1,
                    SlotCount = containerSlotCount,
                    Rows = containerRows,
                    Bounds = containerBounds,
                    CustomTexture = UIManager.BackpackPanelTexture,
                    SlotGetter = (i) => container.Inventory.Slots[i]
                });
                _modulesStartY += containerHeight + MODULE_MARGIN_BOTTOM;
            }

            // Module Backpack
            if (Program.equipment.BackpackContainer != null)
            {
                int backpackSlotCount = Program.equipment.BackpackContainer.Inventory.Slots.Count;
                int backpackRows = (int)Math.Ceiling((double)backpackSlotCount / SLOTS_PER_ROW);
                int backpackHeight = PANEL_PADDING * 2 + backpackRows * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
                Rectangle backpackBounds = new Rectangle(_mainPanelX, _modulesStartY, _mainPanelW, backpackHeight);
                _modules.Add(new ModuleInfo
                {
                    Title = "",
                    SlotStartIndex = -1,
                    SlotCount = backpackSlotCount,
                    Rows = backpackRows,
                    Bounds = backpackBounds,
                    CustomTexture = UIManager.BackpackPanelTexture,
                    SlotGetter = (i) => Program.equipment.BackpackContainer.Inventory.Slots[i]
                });
                _modulesStartY += backpackHeight + MODULE_MARGIN_BOTTOM;
            }

            int fullContentHeight = _modulesStartY - _mainPanelY + PANEL_PADDING;
            int maxPanelHeight = Math.Max(300, _screenHeight - 120);
            _mainPanelH = Math.Clamp(fullContentHeight, 300, maxPanelHeight);
        }

        private static bool IsPortableContainer(Item item)
        {
            // Délègue à Program.IsPortableContainer, qui compare sur la clé stable de
            // l'item (items.json -> "id": "basket_wicker") plutôt que sur un ID
            // numérique en dur : cet ID dépendait de la position de l'item dans
            // items.json et ne correspondait plus à basket_wicker depuis que la liste
            // a été réordonnée/modifiée, ce qui empêchait le module "PANIER" de
            // s'afficher même quand le panier était bien équipé.
            return Program.IsPortableContainer(item);
        }

        public static void RebuildInventory(Equipment equipment)
        {
            var oldSlots = InventorySlots;

            if (equipment?.BackpackContainer != null && oldSlots.Count > Equipment.BELT_SLOTS)
            {
                var backpackSlots = equipment.BackpackContainer.Inventory.Slots;
                for (int i = Equipment.BELT_SLOTS; i < oldSlots.Count; i++)
                {
                    var oldSlot = oldSlots[i];
                    if (oldSlot.IsEmpty) continue;

                    int targetIndex = i - Equipment.BELT_SLOTS;
                    if (targetIndex < backpackSlots.Count && backpackSlots[targetIndex].IsEmpty)
                        backpackSlots[targetIndex] = oldSlot;
                }
            }

            InventorySlots = new List<InventorySlot>();
            for (int i = 0; i < Equipment.BELT_SLOTS; i++)
                InventorySlots.Add(new InventorySlot());

            for (int i = 0; i < Math.Min(oldSlots.Count, Equipment.BELT_SLOTS); i++)
                InventorySlots[i] = oldSlots[i];
        }

        public static void Draw(Equipment equipment, int playerIndex = 0, Rectangle? viewport = null)
        {
            EnsurePlayerState(playerIndex);
            if (!IsOpenForPlayer(playerIndex)) return;

            DrawTitleBarOnly(playerIndex, viewport);
            SkillsTabRenderer.Update(Raylib.GetFrameTime(), _mainPanelX, _equipmentAreaY - 5, _mainPanelW, CalculateEquipmentHeight() + 10);
            SkillsTabRenderer.Draw();
            DrawEquipmentArea(equipment, playerIndex, viewport);
            DrawModules(playerIndex);

            if (DraggedItem.IsDragging && DraggedItem.Item != null)
            {
                Vector2 mousePos = Raylib.GetMousePosition();
                int size = 48;
                int halfSize = size / 2;
                int drawX = (int)mousePos.X - halfSize;
                int drawY = (int)mousePos.Y - halfSize;
                
                //  Utiliser ItemRenderer avec transparence
                Color tint = new Color(255, 255, 255, 200);
                ItemRenderer.DrawItemPadded(DraggedItem.Item, drawX, drawY, size, 4, tint);
                
                // Afficher la quantité si > 1
                if (DraggedItem.Count > 1)
                    FontManager.DrawText(DraggedItem.Count.ToString(), (int)mousePos.X + 15, (int)mousePos.Y + 15, 16, Color.White);
            }

            if (_tooltipSlot != null && !_tooltipSlot.IsEmpty && _tooltipSlot.Item != null)
                DrawTooltip(_tooltipSlot);
        }

        private static void DrawTitleBarOnly(int playerIndex, Rectangle? viewport = null)
        {
            var windowPos = GetWindowPos(playerIndex);
            float offsetX = viewport?.X ?? 0f;
            float offsetY = viewport?.Y ?? 0f;
            int x = (int)(windowPos.X + offsetX);
            int y = (int)(windowPos.Y + offsetY);
            int w = _mainPanelW;

            Rectangle titleBar = new Rectangle(x, y, w, HEADER_HEIGHT);
            Color titleColor = _isHoveringTitleBarByPlayer[playerIndex] ? new Color(80, 70, 50, 200) : COLOR_HEADER;
            Raylib.DrawRectangleRounded(titleBar, 0.1f, 8, titleColor);
            Raylib.DrawRectangleRoundedLines(titleBar, 0.1f, 8, 1, COLOR_ACCENT);
            FontManager.DrawText(Localization.Get("inventory.title"), x + 15, y + 13, 18, Color.White);

            Rectangle closeBtn = new Rectangle(x + w - 35, y + 10, 25, 25);
            Vector2 mousePos = Raylib.GetMousePosition();
            bool isHoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, isHoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeBtn.X + 8, (int)closeBtn.Y + 5, 16, isHoverClose ? Color.Red : new Color(200, 120, 120, 200));
            if (isHoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
                Close(playerIndex);
        }

        private static readonly Dictionary<EquipmentSlot, string> _slotBoneMap = new()
        {
            { EquipmentSlot.MainHand, "rarmbottom" },
            { EquipmentSlot.OffHand, "larmbottom" },
        };

        private static void DrawEquipmentArea(Equipment equipment, int playerIndex, Rectangle? viewport = null)
        {
            var windowPos = GetWindowPos(playerIndex);
            float offsetX = viewport?.X ?? 0f;
            float offsetY = viewport?.Y ?? 0f;
            int x = (int)(windowPos.X + offsetX);
            int y = (int)(windowPos.Y + offsetY);

            int panelW = _mainPanelW;
            int panelH = CalculateEquipmentHeight();
            _equipmentBarSlotRects.Clear();

            if (_useTextures && _panelTexture != null && _panelTexture.IsValid)
            {
                _panelTexture.Draw(new Rectangle(x, _equipmentAreaY - 5, panelW, panelH + 10), Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(new Rectangle(x, _equipmentAreaY - 5, panelW, panelH + 10), 0.1f, 12, COLOR_BG_DARK);
                Raylib.DrawRectangleRoundedLines(new Rectangle(x, _equipmentAreaY - 5, panelW, panelH + 10), 0.1f, 12, 2, COLOR_BORDER);
            }

            FontManager.DrawText(Localization.Get("inventory.equipment"), x + 15, _equipmentAreaY - 2, 14, COLOR_ACCENT);

            int centerX = x + panelW / 2;
            string displayStyle = SettingsManager.Settings.InventoryEquipmentStyle;
            if (displayStyle == "character_with_slots")
            {
                int previewSize = 220;
                DrawCharacterPreview(centerX - previewSize / 2, _equipmentAreaY + 56, previewSize, equipment);
                DrawEquipmentSideBars(x, panelW, equipment);
            }
            else if (displayStyle == "equipment_only")
            {
                int previewSize = 220;
                DrawCharacterPreview(centerX - previewSize / 2, _equipmentAreaY + 56, previewSize, equipment, showCharacter: false, showFrame: false);
            }
            else
            {
                int previewSize = 220;
                int centerY = _equipmentAreaY + 158;
                DrawCharacterPreview(centerX - previewSize / 2, centerY - previewSize / 2 + 8, previewSize, equipment);
            }
        }

        private const float PREVIEW_CUSTOM_SCALE = 1.25f;
        private const string PREVIEW_ANIM = "idle";

        private static readonly (string label, BodyZone? zone, EquipmentSlot? slot)[] _leftEquipmentBarSlots =
        {
            ("Tête", BodyZone.TopOfHead, null),
            ("Visage", BodyZone.Face, null),
            ("Buste", BodyZone.Torso, null),
            ("Taille", BodyZone.Waist, null),
            ("Pieds", BodyZone.Feet, null),
            ("Main droite", null, EquipmentSlot.MainHand),
            ("Instrument", null, EquipmentSlot.Instrument),
            ("Compagnon", null, EquipmentSlot.Pet),
        };

        private static readonly (string label, BodyZone? zone, EquipmentSlot? slot)[] _rightEquipmentBarSlots =
        {
            ("Oreilles", BodyZone.Ears, null),
            ("Cou", BodyZone.Neck, null),
            ("Dos", BodyZone.Back, null),
            ("Jambes", BodyZone.Legs, null),
            ("Mains", BodyZone.Hands, null),
            ("Main gauche", null, EquipmentSlot.OffHand),
            ("Munitions", null, EquipmentSlot.Ammo),
            ("Monture", null, EquipmentSlot.Mount),
        };

        private static void DrawEquipmentSideBars(int x, int panelW, Equipment equipment)
        {
            int slotSize = 32;
            int rowStep = 34;
            int top = _equipmentAreaY + 22;
            int barHeight = rowStep * 8 - 2;
            int leftSlotX = x + 8;
            int rightSlotX = x + panelW - slotSize - 8;
            int labelFontSize = 10;

            Rectangle leftBar = new Rectangle(x + 3, top - 4, 92, barHeight + 8);
            Rectangle rightBar = new Rectangle(x + panelW - 95, top - 4, 92, barHeight + 8);
            Raylib.DrawRectangleRounded(leftBar, 0.08f, 6, new Color(20, 22, 28, 120));
            Raylib.DrawRectangleRoundedLines(leftBar, 0.08f, 6, 1, COLOR_BORDER);
            Raylib.DrawRectangleRounded(rightBar, 0.08f, 6, new Color(20, 22, 28, 120));
            Raylib.DrawRectangleRoundedLines(rightBar, 0.08f, 6, 1, COLOR_BORDER);

            DrawEquipmentBarColumn(_leftEquipmentBarSlots, leftSlotX, top, equipment, labelFontSize, true);
            DrawEquipmentBarColumn(_rightEquipmentBarSlots, rightSlotX, top, equipment, labelFontSize, false);
        }

        private static void DrawEquipmentBarColumn(
            (string label, BodyZone? zone, EquipmentSlot? slot)[] entries,
            int slotX,
            int top,
            Equipment equipment,
            int labelFontSize,
            bool labelOnRight)
        {
            const int slotSize = 32;
            const int rowStep = 34;
            Vector2 mousePos = Raylib.GetMousePosition();

            for (int index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                Rectangle slotRect = new Rectangle(slotX, top + index * rowStep, slotSize, slotSize);
                var key = (entry.slot, entry.zone);
                _equipmentBarSlotRects[key] = slotRect;

                bool hovered = _hoveredEquipSlot == key;
                Raylib.DrawRectangleRounded(slotRect, 0.12f, 5, hovered ? COLOR_SLOT_HOVER : COLOR_SLOT_NORMAL);
                Raylib.DrawRectangleRoundedLines(slotRect, 0.12f, 5, 1, hovered ? COLOR_ACCENT : COLOR_BORDER);

                Item? item = entry.zone != null
                    ? equipment.GetZoneItem(entry.zone.Value)
                    : (entry.slot != null ? equipment.GetItemInSlot(entry.slot.Value, 0) : null);
                if (item != null)
                    ItemRenderer.DrawItemPadded(item, (int)slotRect.X + 2, (int)slotRect.Y + 2, slotSize - 4, 2);

                int labelWidth = FontManager.MeasureText(entry.label, labelFontSize);
                int labelX = labelOnRight
                    ? (int)(slotRect.X + slotRect.Width) + 5
                    : (int)slotRect.X - labelWidth - 5;
                FontManager.DrawText(entry.label, labelX, (int)slotRect.Y + 10, labelFontSize,
                    hovered ? COLOR_ACCENT : (item != null ? Color.White : new Color(175, 175, 175, 210)));
            }
        }

        private static void DrawCharacterPreview(
            int x,
            int y,
            int size,
            Equipment equipment,
            bool showCharacter = true,
            bool showFrame = true,
            float customScale = PREVIEW_CUSTOM_SCALE)
        {
            Rectangle previewRect = new Rectangle(x, y, size, size);
            if (showFrame)
            {
                Raylib.DrawRectangleRounded(previewRect, 0.15f, 12, new Color(20, 22, 28, 180));
                Raylib.DrawRectangleRoundedLines(previewRect, 0.15f, 12, 1, new Color(80, 70, 50, 140));
            }

            Vector2 previewPos = new Vector2(x + size / 2f, y + size - size * (70f / 220f));

            // Calculer le regard vers la souris pour l'aperçu du menu
            var gaze = Program.ComputeGazeOffsetsFor(previewPos, Raylib.GetMousePosition(), 1f);

            if (showCharacter)
                EntityRenderer.DrawEntity(
                speciesName: "human",
                anim: PREVIEW_ANIM,
                frame: 0,
                prog: 0f,
                facing: 1f,
                pos: previewPos,
                tint: Program.SkinColor,
                hBase: Program.hairBaseTextures.Count > 0 ? Program.hairBaseTextures[Math.Clamp(Program.PlayerHairStyle, 0, Program.hairBaseTextures.Count - 1)] : new Texture2D(),
                hOverlay: Program.hairOverlayTextures.Count > 0 ? Program.hairOverlayTextures[Math.Clamp(Program.PlayerHairStyle, 0, Program.hairOverlayTextures.Count - 1)] : new Texture2D(),
                hColor: Program.HairColor,
                skeletons: SpeciesData.Skeletons,
                eyes: Program.EyesTexture,
                mouth: Program.MouthTexture,
                customScale: customScale,
                equipment: equipment,
                isCarrying: false,
                inWater: false,
                attackSwingProgress: 0f,
                heldItemTexture: default,
                headAngle: 0f,
                keepItemHorizontal: false,
                isBow: false,
                underwearTexture: Program.LeafUnderpantsTexture,
                beardStyle: Program.PlayerBeardStyle,
                pupilLeftOffset: gaze.pupilLeft,
                pupilRightOffset: gaze.pupilRight,
                eyebrowLeftOffset: gaze.browLeft,
                eyebrowRightOffset: gaze.browRight
            );

            //  Reconstruit les zones cliquables/survolables directement sur le rendu du
            // personnage (plus de cases séparées, plus de ronds approximatifs) : chaque zone
            // couverte par un item équipé obtient la ou les silhouette(s) réelle(s) de son
            // sprite, ancrée(s) sur le(s) bone(s) du squelette où elles sont vraiment dessinées.
            _equipSlotRects.Clear();
            foreach (var (slot, part) in _slotBoneMap)
            {
                var (partPos, _) = EntityRenderer.GetGlobalPartTransform(
                    "human", PREVIEW_ANIM, 0, 0f, 1f, previewPos, SpeciesData.Skeletons, part,
                    customScale: customScale);
                float r = 13f;
                _equipSlotRects[((EquipmentSlot?)slot, (BodyZone?)null)] =
                    new Rectangle(partPos.X - r, partPos.Y - r, r * 2, r * 2);
            }

            //  Silhouette réelle de l'item ACTUELLEMENT équipé sur les emplacements
            // fonctionnels (arme en MainHand, panier/bouclier en OffHand), pour un survol
            // cohérent avec le reste de l'équipement (voir _slotPieceRects).
            _slotPieceRects.Clear();
            foreach (var slot in new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand })
            {
                Item? equippedSlotItem = equipment.GetItemInSlot(slot, 0);
                var piece = EntityRenderer.GetSlotItemPieceRect(
                    "human", PREVIEW_ANIM, 0, 1f, previewPos, SpeciesData.Skeletons,
                    customScale, slot, equippedSlotItem);
                if (piece != null)
                    _slotPieceRects[slot] = piece.Value;
            }

            _zonePieceRects.Clear();
            foreach (var zone in _zoneCoverageZones)
            {
                Item? equipped = equipment.GetZoneItem(zone);
                if (equipped == null) continue;
                int equippedId = Program.GetItemId(equipped.Name);
                if (!GameData.ItemDatabase.TryGetValue(equippedId, out var equippedData)) continue;

                var pieces = EntityRenderer.GetZonePieceRects(
                    "human", PREVIEW_ANIM, 0, 1f, previewPos, SpeciesData.Skeletons,
                    customScale, zone, equippedData, equipped);
                if (pieces.Count > 0)
                    _zonePieceRects[zone] = pieces;
            }

            _dragZonePieceRects.Clear();
            if (DraggedItem.IsDragging && DraggedItem.Item != null)
            {
                int draggedId = Program.GetItemId(DraggedItem.Item.Name);
                if (GameData.ItemDatabase.TryGetValue(draggedId, out var draggedData))
                {
                    foreach (var zone in draggedData.CoveredZones)
                    {
                        var pieces = EntityRenderer.GetZonePieceRects(
                            "human", PREVIEW_ANIM, 0, 1f, previewPos, SpeciesData.Skeletons,
                            customScale, zone, draggedData, DraggedItem.Item);
                        if (pieces.Count > 0)
                            _dragZonePieceRects[zone] = pieces;
                    }
                }
            }

            //  Silhouette réelle de l'item EN COURS DE DRAG s'il vise un emplacement
            // fonctionnel (arme/panier/bouclier), pour un aperçu fantôme cohérent avec le
            // reste de l'équipement au lieu d'un rond générique.
            _dragSlotPieceRects.Clear();
            if (DraggedItem.IsDragging && DraggedItem.Item != null)
            {
                int draggedId = Program.GetItemId(DraggedItem.Item.Name);
                if (GameData.ItemDatabase.TryGetValue(draggedId, out var draggedData)
                    && draggedData.HasEquipSlot
                    && (draggedData.EquipSlot == EquipmentSlot.MainHand || draggedData.EquipSlot == EquipmentSlot.OffHand))
                {
                    var piece = EntityRenderer.GetSlotItemPieceRect(
                        "human", PREVIEW_ANIM, 0, 1f, previewPos, SpeciesData.Skeletons,
                        customScale, draggedData.EquipSlot, DraggedItem.Item);
                    if (piece != null)
                        _dragSlotPieceRects[draggedData.EquipSlot] = piece.Value;
                }
            }

            if (!showCharacter)
                DrawEquipmentOnly();
            DrawZoneHighlights(equipment);
            DrawArmorSummary(x, y, size, equipment);
        }

        private static void DrawEquipmentOnly()
        {
            var drawnParts = new HashSet<string>(StringComparer.Ordinal);
            for (int renderPass = 0; renderPass < 2; renderPass++)
            {
                bool drawBack = renderPass == 0;
                foreach (var zone in _zoneCoverageZones)
                {
                    if (!_zonePieceRects.TryGetValue(zone, out var pieces)) continue;
                    foreach (var piece in pieces)
                    {
                        if (piece.Texture.Id == 0 || piece.IsBack != drawBack
                            || !drawnParts.Add($"{piece.TexturePath}:{piece.PartName}:{piece.Layer}:{piece.IsBack}")) continue;
                        Raylib.DrawTexturePro(piece.Texture,
                            new Rectangle(0, 0, piece.Texture.Width, piece.Texture.Height),
                            piece.Rect, Vector2.Zero, 0f, piece.Tint);
                    }
                }
            }

            foreach (var piece in _slotPieceRects.Values)
            {
                if (piece.Texture.Id == 0) continue;
                Raylib.DrawTexturePro(piece.Texture,
                    new Rectangle(0, 0, piece.Texture.Width, piece.Texture.Height),
                    piece.Rect, Vector2.Zero, 0f, piece.Tint);
            }
        }

        //  Toutes les BodyZone gérées par le système d'équipement sur corps (utilisées pour
        // savoir quelles zones tester à chaque frame).
        private static readonly BodyZone[] _zoneCoverageZones =
        {
            BodyZone.TopOfHead, BodyZone.Face, BodyZone.Ears, BodyZone.Neck,
            BodyZone.Torso, BodyZone.Waist, BodyZone.Legs, BodyZone.Feet, BodyZone.Back,
            BodyZone.Hands,
        };

        private static byte GetPulsingAlpha(byte baseAlpha, float speed = 2f, float amplitude = 20f)
        {
            float time = (float)Raylib.GetTime();
            float pulse = (float)Math.Sin(time * speed) * amplitude;
            return (byte)Math.Clamp(baseAlpha + pulse, 0f, 255f);
        }

        //  Surbrillance des zones du corps.
        // Pendant un drag : dessine un véritable aperçu FANTÔME de l'item déplacé, à sa vraie
        // taille/forme, directement sur le personnage (plus un rond générique) - blanc
        // translucide si la zone visée peut recevoir l'item, rouge translucide sinon (ex: item
        // sans zone couverte, ou emplacement fonctionnel incompatible).
        // Hors drag : l'item actuellement survolé est légèrement éclairci sur sa silhouette
        // réelle pour indiquer qu'il est cliquable (retrait/échange).
        private static void DrawZoneHighlights(Equipment equipment)
        {
            if (DraggedItem.IsDragging && DraggedItem.Item != null)
            {
                int draggedId = Program.GetItemId(DraggedItem.Item.Name);
                bool draggedFound = GameData.ItemDatabase.TryGetValue(draggedId, out var draggedData);
                bool hasCoveredZones = draggedFound && draggedData.CoveredZones.Count > 0;

                if (hasCoveredZones)
                {
                    // Aperçu fantôme : la texture réelle de l'item, semi-transparente, à
                    // l'endroit exact où elle serait dessinée une fois équipée.
                    //  Un item qui couvre plusieurs BodyZone partageant le même bone (ex:
                    // TopOfHead+Face+Ears -> "head") renverrait la même pièce sous plusieurs
                    // clés de zone : on ne la dessine donc qu'une seule fois par bone, sinon la
                    // transparence s'empile visuellement et l'aperçu paraît opaque.
                    var drawnParts = new HashSet<string>(StringComparer.Ordinal);
                    byte dragPreviewAlpha = GetPulsingAlpha(150, speed: 2.4f, amplitude: 22f);
                    foreach (var piece in _dragZonePieceRects.Values.SelectMany(pieces => pieces).OrderBy(piece => piece.IsBack ? 0 : 1))
                    {
                        if (piece.Texture.Id == 0) continue;
                        if (!drawnParts.Add($"{piece.TexturePath}:{piece.PartName}:{piece.Layer}:{piece.IsBack}")) continue;
                        Rectangle src = new Rectangle(0, 0, piece.Texture.Width, piece.Texture.Height);
                        Color previewTint = piece.Tint;
                        previewTint.A = (byte)(previewTint.A * dragPreviewAlpha / 255);
                        Raylib.DrawTexturePro(piece.Texture, src, piece.Rect, Vector2.Zero, 0f, previewTint);
                    }
                }

                // Emplacements fonctionnels (arme/bouclier/panier) : même système que le reste
                // de l'équipement - la vraie texture de l'item en aperçu fantôme, plus de rond
                // générique.
                byte dragSlotPreviewAlpha = GetPulsingAlpha(150, speed: 2.4f, amplitude: 22f);
                foreach (var kv in _dragSlotPieceRects)
                {
                    bool candidate = draggedFound && draggedData.HasEquipSlot && draggedData.EquipSlot == kv.Key;
                    if (!candidate) continue;

                    var piece = kv.Value;
                    if (piece.Texture.Id == 0) continue;
                    Rectangle src = new Rectangle(0, 0, piece.Texture.Width, piece.Texture.Height);
                    Raylib.DrawTexturePro(piece.Texture, src, piece.Rect, Vector2.Zero, 0f, new Color((byte)255, (byte)255, (byte)255, dragSlotPreviewAlpha));
                }
            }
            else if (_hoveredEquipSlot != null)
            {
                var key = _hoveredEquipSlot.Value;

                if (key.zone != null && _zonePieceRects.TryGetValue(key.zone.Value, out var pieces))
                {
                    byte hoverAlpha = GetPulsingAlpha(55, speed: 1.8f, amplitude: 18f);
                    foreach (var piece in pieces)
                    {
                        if (piece.Texture.Id == 0) continue;
                        Rectangle src = new Rectangle(0, 0, piece.Texture.Width, piece.Texture.Height);
                        // Léger effet de surbrillance : on redessine la silhouette réelle en
                        // blanc translucide par-dessus, sans jamais dépasser sa vraie forme.
                        Raylib.DrawTexturePro(piece.Texture, src, piece.Rect, Vector2.Zero, 0f, new Color((byte)255, (byte)255, (byte)255, hoverAlpha));
                    }
                }
                else if (key.slot != null && equipment.GetItemInSlot(key.slot.Value, 0) != null && _slotPieceRects.TryGetValue(key.slot.Value, out var slotPiece))
                {
                    if (slotPiece.Texture.Id != 0)
                    {
                        Rectangle src = new Rectangle(0, 0, slotPiece.Texture.Width, slotPiece.Texture.Height);
                        Raylib.DrawTexturePro(slotPiece.Texture, src, slotPiece.Rect, Vector2.Zero, 0f, new Color(255, 255, 255, 55));
                    }
                }
            }
        }

        private static void DrawArmorSummary(int x, int y, int size, Equipment equipment)
        {
            EnsureArmorIconTexture();
            string armorText = equipment.TotalArmor.ToString();
            int fontSize = 18;
            int textWidth = FontManager.MeasureText(armorText, fontSize);
            int iconSize = 22;
            int contentWidth = iconSize + 8 + textWidth;
            int contentX = x + (size - contentWidth) / 2;
            int contentY = y + size + 8;

            if (_armorIconTexture.Id != 0)
            {
                Raylib.DrawTexturePro(
                    _armorIconTexture,
                    new Rectangle(0, 0, _armorIconTexture.Width, _armorIconTexture.Height),
                    new Rectangle(contentX, contentY, iconSize, iconSize),
                    Vector2.Zero, 0, Color.White);
            }

            FontManager.DrawText(armorText, contentX + iconSize + 8, contentY + 2, fontSize, new Color(220, 220, 180, 255));
        }

        // ===== MÉTHODES D'AIDE POUR L'AFFICHAGE =====
        // (Les anciennes cases d'équipement ont été retirées : l'équipement se voit et
        // s'interagit désormais directement sur le rendu du personnage, voir DrawCharacterPreview.)

        private static void DrawModules(int playerIndex)
        {
            int scrollOffset = (int)_scrollOffsetByPlayer[playerIndex];
            int viewportTop = _mainPanelY + HEADER_HEIGHT + 10 + CalculateEquipmentHeight() + 10;
            int viewportHeight = Math.Max(120, _mainPanelH - (viewportTop - _mainPanelY));
            Rectangle viewportRect = new Rectangle(_mainPanelX, viewportTop, _mainPanelW, viewportHeight);
            Raylib.BeginScissorMode((int)viewportRect.X, (int)viewportRect.Y, (int)viewportRect.Width, (int)viewportRect.Height);

            foreach (var module in _modules)
            {
                DrawModule(module, playerIndex, scrollOffset);
            }

            if (_maxScrollByPlayer[playerIndex] > 0f)
            {
                int trackHeight = Math.Max(70, viewportHeight - 24);
                int trackY = viewportTop + 12;
                int thumbHeight = Math.Max(24, (int)((trackHeight / Math.Max(1f, _maxScrollByPlayer[playerIndex] + trackHeight)) * trackHeight));
                int thumbY = trackY + (int)((_scrollOffsetByPlayer[playerIndex] / _maxScrollByPlayer[playerIndex]) * Math.Max(1, trackHeight - thumbHeight));

                Rectangle trackRect = new Rectangle(_mainPanelX + _mainPanelW - 14, trackY, 8, trackHeight);
                Raylib.DrawRectangleRounded(trackRect, 0.5f, 6, new Color(20, 22, 28, 180));
                Rectangle thumbRect = new Rectangle(_mainPanelX + _mainPanelW - 14, thumbY, 8, thumbHeight);
                Raylib.DrawRectangleRounded(thumbRect, 0.5f, 6, new Color(210, 180, 100, 220));
            }

            Raylib.EndScissorMode();
        }

        private static void DrawModule(ModuleInfo module, int playerIndex, int scrollOffset)
        {
            int x = (int)module.Bounds.X;
            int y = (int)module.Bounds.Y - scrollOffset;
            int w = (int)module.Bounds.Width;
            int h = (int)module.Bounds.Height;

            if (y + h < _mainPanelY + HEADER_HEIGHT + 10 + CalculateEquipmentHeight() + 10 - 20)
                return;
            if (y > _mainPanelY + _mainPanelH + 20)
                return;

            NineSliceTexture? tex = module.CustomTexture ?? _panelTexture;
            if (tex != null && tex.IsValid)
                tex.Draw(new Rectangle(x, y, w, h), Color.White);
            else
            {
                Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), 0.1f, 12, COLOR_BG_DARK);
                Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, w, h), 0.1f, 12, 2, COLOR_BORDER);
            }

            if (!string.IsNullOrEmpty(module.Title))
            {
                FontManager.DrawText(module.Title, x + 15, y + 5, 12, COLOR_ACCENT);
            }

            int startX = x + PANEL_PADDING;
            int startY = y + PANEL_PADDING + (string.IsNullOrEmpty(module.Title) ? 0 : 20);

            for (int drawPass = 0; drawPass < 2; drawPass++)
            {
                for (int i = 0; i < module.SlotCount; i++)
                {
                    int col = i % SLOTS_PER_ROW;
                    int row = i / SLOTS_PER_ROW;
                    int slotX = startX + col * SLOT_STEP;
                    int slotY = startY + row * SLOT_STEP;

                    bool isHovered = module.SlotStartIndex >= 0
                        ? HoveredSlot == module.SlotStartIndex + i
                        : _hoveredContainerModule == module && _hoveredContainerSlotIndex == i;
                    if (isHovered != (drawPass == 1))
                        continue;

                    bool isSelected = (SelectedSlot == module.SlotStartIndex + i && module.SlotStartIndex >= 0);

                    var slot = module.SlotGetter(i);
                    if (slot != null)
                    {
                        DrawSlot(slotX, slotY, slot, isHovered, isSelected,
                            i % SLOTS_PER_ROW == 0,
                            i + 1 >= module.SlotCount || i % SLOTS_PER_ROW == SLOTS_PER_ROW - 1,
                            i < SLOTS_PER_ROW,
                            i + SLOTS_PER_ROW >= module.SlotCount);
                    }
                }
            }
        }

        private static void DrawSlot(int x, int y, InventorySlot slot, bool hover, bool selected, bool borderLeft, bool borderRight, bool borderUp, bool borderDown)
        {
            bool pressed = hover && Raylib.IsMouseButtonDown(MouseButton.Left);
            Color fallbackColor = selected ? COLOR_SLOT_SELECTED : (hover ? COLOR_SLOT_HOVER : COLOR_SLOT_NORMAL);
            UIManager.DrawItemSlotBackground(new Rectangle(x, y, SLOT_SIZE, SLOT_SIZE), hover, pressed, fallbackColor, borderUp, borderDown, borderLeft, borderRight);

            if (!slot.IsEmpty && slot.Item != null)
            {
                Item itemToRender = slot.Item;
                string metadata = itemToRender.Metadata ?? string.Empty;
                bool isBottle = GameData.TryGetItemByName(itemToRender.Name, out var itemData)
                    && string.Equals(itemData.Key, "glassbottle_small", StringComparison.OrdinalIgnoreCase);
                if (isBottle && GameData.ItemDatabaseByKey.TryGetValue("glassbottle_small", out var bottleData))
                {
                    Color? customTint = itemToRender.CustomColor;
                    if (!customTint.HasValue && itemToRender.CustomColors != null && itemToRender.CustomColors.Count > 0)
                        customTint = itemToRender.CustomColors[0];

                    ItemRenderer.DrawItemRotated(
                        bottleData,
                        new Vector2(x + SLOT_SIZE / 2f, y + SLOT_SIZE / 2f),
                        ITEM_SIZE - 12,
                        0f,
                        customTint,
                        metadata);
                }
                else
                {
                    ItemRenderer.DrawItemPadded(itemToRender, x + ITEM_OFFSET, y + ITEM_OFFSET, ITEM_SIZE, 6);
                }

                if (slot.Count > 1)
                    FontManager.DrawText(slot.Count.ToString(), x + ITEM_OFFSET + ITEM_SIZE - 18, y + ITEM_OFFSET + ITEM_SIZE - 18, 14, Color.White);
            }
        }

        // ==================== TOOLTIP ====================
        // Bande de rareté en haut (avec le nom de l'item centré) + icône qui déborde
        // légèrement à gauche pour un petit effet de relief, puis description en
        // italique et liste des caractéristiques en dessous.

        private const int TOOLTIP_BAND_HEIGHT = 46;
        private const int TOOLTIP_ICON_SIZE = 56;
        private const int TOOLTIP_ICON_OVERFLOW = 12;
        private const int TOOLTIP_PADDING = 14;
        private const int DETAIL_LINE_HEIGHT = 16;
        private const int TOOLTIP_MIN_WIDTH = 300;
        private const int TOOLTIP_MAX_WIDTH = 560;

        private static List<string> WrapText(string text, int maxChars)
        {
            var words = text.Split(' ');
            var lines = new List<string>();
            var current = "";
            foreach (var word in words)
            {
                if (current.Length + word.Length + 1 > maxChars)
                {
                    if (!string.IsNullOrEmpty(current)) lines.Add(current.TrimEnd());
                    current = word + " ";
                }
                else
                {
                    current += word + " ";
                }
            }
            if (!string.IsNullOrEmpty(current)) lines.Add(current.TrimEnd());
            return lines;
        }

        public static void BuildItemTooltipData(Item item, int quantity, out string displayName, out List<string> detailLines, out List<string> descriptionLines, out string rarityText, out Color rarityColor, out List<string> detailIconKeys)
        {
            detailLines = new List<string>();
            descriptionLines = new List<string>();
            detailIconKeys = new List<string>();
            rarityText = string.Empty;
            rarityColor = new Color(200, 200, 160, 255);

            int itemId = Program.GetItemId(item.Name);
            displayName = ItemRenderer.GetDisplayName(item, itemId, Localization.GetLocalizedItemName(itemId));

            if (!GameData.ItemDatabase.TryGetValue(itemId, out var data))
                return;

            rarityText = data.Rarity?.ToUpperInvariant() ?? string.Empty;
            if (!string.IsNullOrEmpty(data.Rarity))
            {
                rarityColor = data.Rarity.ToLower() switch
                {
                    "rare" => new Color(120, 180, 255, 255),
                    "epic" => new Color(200, 120, 255, 255),
                    "legendary" => new Color(255, 180, 80, 255),
                    _ => new Color(200, 200, 160, 255)
                };
            }

            detailLines.Add(data.Type switch
            {
                ItemType.Weapon => string.Format(Localization.Get("item.tooltip.damage"), data.AttackBonus),
                ItemType.Armor => string.Format(Localization.Get("item.tooltip.armor"), data.ArmorValue),
                ItemType.Food => string.Format(Localization.Get("item.tooltip.heal"), data.HealAmount),
                ItemType.Tool => string.Format(Localization.Get("item.tooltip.tool"), Localization.GetToolTypeName(data.ToolKind)),
                ItemType.Throwable => Localization.Get("item.tooltip.throwable"),
                ItemType.Placeable => Localization.Get("item.tooltip.placeable"),
                ItemType.Backpack => Localization.Get("item.tooltip.backpack"),
                _ => Localization.GetItemTypeName(data.Type)
            });
            detailIconKeys.Add(data.Type switch
            {
                ItemType.Weapon => "damage",
                ItemType.Armor => "armor",
                ItemType.Food => "heal",
                ItemType.Tool => "tool",
                ItemType.Throwable => "throwable",
                ItemType.Placeable => "placeable",
                ItemType.Backpack => "backpack",
                _ => "type"
            });

            if (data.IsSeed)
            {
                detailLines.Add(string.Format(Localization.Get("item.tooltip.seed"), data.GrowthTime, data.GrowthStages));
                detailIconKeys.Add("seed");
            }

            if (data.RuneSlots > 0)
            {
                item.EnsureRuneSlots(data.RuneSlots);
                int filled = item.SocketedRunes.Count(r => r != null);
                detailLines.Add($"Runes : {filled}/{data.RuneSlots}");
                detailIconKeys.Add("rune");
                foreach (var rune in item.SocketedRunes)
                {
                    if (rune == null) continue;
                    detailLines.Add($"   {rune.DisplayName}");
                    detailIconKeys.Add("rune");
                }
            }
            else if (data.IsRune && !string.IsNullOrEmpty(item.Metadata))
            {
                var runeInfo = RuneData.FromMetadata(item.Metadata);
                detailLines.Add($"Pouvoir : {runeInfo.DisplayName}");
                detailIconKeys.Add("rune");
            }

            if (data.BackpackSlots > 0)
            {
                detailLines.Add(string.Format(Localization.Get("item.tooltip.backpack_size"), data.BackpackRows, data.BackpackSlots));
                detailIconKeys.Add("backpack_size");
            }

            if (data.HasEquipSlot)
            {
                string slotName = GameData.GetSlotDisplayName(data.EquipSlot);
                string maxText = data.EquipMax > 1 ? string.Format(Localization.Get("item.tooltip.max_count"), data.EquipMax) : string.Empty;
                detailLines.Add(string.Format(Localization.Get("item.tooltip.slot"), slotName, maxText));
                detailIconKeys.Add("slot");
            }

            if (data.CoveredZones.Count > 0)
            {
                string zonesLabel = string.Join(", ", data.CoveredZones.Select(z => Localization.GetOrDefault($"armor.category.{z.ToString().ToLowerInvariant()}", z.ToString())));
                detailLines.Add(string.Format(Localization.Get("item.tooltip.covers"), zonesLabel));
                detailIconKeys.Add("covers");
            }

            if (data.Value > 0)
            {
                detailLines.Add(string.Format(Localization.Get("item.tooltip.value"), data.Value));
                detailIconKeys.Add("value");
            }

            if (data.StackSize > 1)
            {
                detailLines.Add(string.Format(Localization.Get("item.tooltip.stackable"), data.StackSize));
                detailIconKeys.Add("stackable");
            }

            if (data.IsLightSource)
            {
                detailLines.Add(string.Format(Localization.Get("item.tooltip.light"), data.LightRadius));
                detailIconKeys.Add("light");
            }

            string localizedDescription = Localization.GetLocalizedItemDescription(itemId);
            if (!string.IsNullOrEmpty(localizedDescription))
            {
                string quoted = "\u00AB " + localizedDescription + " \u00BB";
                descriptionLines = WrapText(quoted, 45);
            }

            AddMetadataTooltipLines(item, detailLines, detailIconKeys);
        }

        private static void DrawTooltip(InventorySlot slot)
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            int tooltipW = 0;

            bool isContainer = (slot.Item?.Container != null);
            bool isBackpack = (slot.Item?.Backpack != null);
            bool hasInventoryPreview = isContainer || isBackpack;

            int previewRows = 0;
            int previewCols = 0;
            List<InventorySlot>? previewSlots = null;

            if (isContainer && slot.Item.Container != null)
            {
                previewSlots = slot.Item.Container.Inventory.Slots;
                previewCols = slot.Item.Container.Inventory.Columns;
                previewRows = slot.Item.Container.Inventory.Rows;
            }
            else if (isBackpack && slot.Item.Backpack != null)
            {
                previewSlots = slot.Item.Backpack.Inventory.Slots;
                previewCols = slot.Item.Backpack.Inventory.Columns;
                previewRows = slot.Item.Backpack.Inventory.Rows;
            }

            BuildItemTooltipData(slot.Item, slot.Count, out var displayName, out var detailLines, out var descriptionLines, out var rarityText, out var rarityColor, out var detailIconKeys);
            tooltipW = CalculateTooltipWidth(displayName, rarityText, detailLines, descriptionLines, hasInventoryPreview, slot.Count);

            int maxRowsToShow = 2;
            int itemsPerRow = 10;
            int extraBottomHeight = hasInventoryPreview ? 20 + maxRowsToShow * 20 : 0;
            int tooltipH = CalculateSharedTooltipHeight(detailLines, descriptionLines, extraBottomHeight, tooltipW);
            Rectangle tooltipRect = GetTooltipRect(mousePos, tooltipW, tooltipH);
            int tooltipX = (int)tooltipRect.X;
            int tooltipY = (int)tooltipRect.Y;
            DrawSharedItemTooltip(tooltipRect, displayName, slot.Item, detailLines, descriptionLines, slot.Count, rarityText, rarityColor, detailIconKeys);

            if (hasInventoryPreview && previewSlots != null)
            {
                int previewY = tooltipY + (int)tooltipRect.Height - (maxRowsToShow * 20) - 40;
                string previewTitle = isBackpack
                    ? Localization.Get("item.tooltip.backpack_contents", "Contenu du sac à dos :")
                    : Localization.Get("item.tooltip.container_contents", "Contenu du panier :");
                FontManager.DrawText(previewTitle, tooltipX + 10, previewY - 14, 10, new Color(200, 200, 160, 200));

                int startX = tooltipX + 8;
                int itemSize = 24;
                int spacing = 2;
                int rowHeight = itemSize + 2;

                for (int row = 0; row < maxRowsToShow; row++)
                {
                    for (int col = 0; col < itemsPerRow; col++)
                    {
                        int slotIndex = row * itemsPerRow + col;
                        if (slotIndex >= previewSlots.Count) break;

                        var previewSlot = previewSlots[slotIndex];
                        if (previewSlot == null || previewSlot.IsEmpty || previewSlot.Item == null) continue;

                        int itemX = startX + col * (itemSize + spacing);
                        int itemY = previewY + row * rowHeight;

                        Raylib.DrawRectangle(itemX + 1, itemY + 1, itemSize, itemSize, new Color(0, 0, 0, 80));
                        Raylib.DrawRectangleLines(itemX, itemY, itemSize, itemSize, new Color(80, 80, 80, 150));

                        ItemRenderer.DrawItemCompact(previewSlot.Item, itemX, itemY, itemSize);

                        if (previewSlot.Count > 1)
                        {
                            string countText = previewSlot.Count.ToString();
                            int fontSize = 9;
                            int textWidth = FontManager.MeasureText(countText, fontSize);
                            FontManager.DrawText(countText, itemX + itemSize - textWidth - 2, itemY + itemSize - fontSize - 1, fontSize, new Color(255, 255, 255, 220));
                        }
                    }
                }

                int totalItems = previewSlots.Count(s => s != null && !s.IsEmpty && s.Item != null);
                int displayedItems = maxRowsToShow * itemsPerRow;
                if (totalItems > displayedItems)
                {
                    string moreText = string.Format(Localization.Get("item.tooltip.more_items", "... et {0} autres"), totalItems - displayedItems);
                    FontManager.DrawText(moreText, startX, previewY + maxRowsToShow * rowHeight + 2, 9, new Color(150, 150, 150, 180));
                }
                else if (totalItems == 0)
                {
                    FontManager.DrawText(Localization.Get("item.tooltip.empty", "(vide)"), startX, previewY + 4, 10, new Color(120, 120, 120, 180));
                }
            }
        }

        public static void DrawItemTooltip(Item item, int count = 1)
        {
            if (item == null) return;
            DrawTooltip(new InventorySlot(item, count));
        }

        public static void DrawSharedItemTooltip(Vector2 mousePos, string displayName, Item? item, List<string> detailLines, List<string> descriptionLines, int quantity, int tooltipW = 360, int extraBottomHeight = 0, string rarityText = "", Color? rarityColor = null, List<string>? detailIconKeys = null)
        {
            tooltipW = CalculateTooltipWidth(displayName, rarityText, detailLines, descriptionLines, false, quantity);
            int tooltipH = CalculateSharedTooltipHeight(detailLines, descriptionLines, extraBottomHeight, tooltipW);
            Rectangle tooltipRect = GetTooltipRect(mousePos, tooltipW, tooltipH);
            DrawSharedItemTooltip(tooltipRect, displayName, item, detailLines, descriptionLines, quantity, rarityText, rarityColor, detailIconKeys);
        }

        public static void DrawSharedItemTooltip(Rectangle tooltipRect, string displayName, Item? item, List<string> detailLines, List<string> descriptionLines, int quantity, string rarityText = "", Color? rarityColor = null, List<string>? detailIconKeys = null)
        {
            int tooltipW = (int)tooltipRect.Width;
            int tooltipH = (int)tooltipRect.Height;
            int tooltipX = (int)tooltipRect.X;
            int tooltipY = (int)tooltipRect.Y;
            int contentX = tooltipX + TOOLTIP_PADDING;
            int contentWidth = tooltipW - TOOLTIP_PADDING * 2;
            Color band = rarityColor ?? new Color(200, 200, 160, 255);

            // --- Fond du panneau ---
            Raylib.DrawRectangleRounded(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.08f, 8, COLOR_TOOLTIP_BG);

            // --- Bande de rareté en haut, avec le nom de l'item centré ---
            Rectangle bandRect = new Rectangle(tooltipX, tooltipY, tooltipW, TOOLTIP_BAND_HEIGHT);
            Raylib.DrawRectangleRounded(bandRect, 0.22f, 8, band);
            // On aplatit le bas de la bande pour qu'elle se fonde proprement dans le panneau
            Raylib.DrawRectangle(tooltipX, tooltipY + TOOLTIP_BAND_HEIGHT - 8, tooltipW, 8, band);
            Raylib.DrawRectangleRoundedLines(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.08f, 8, 1, COLOR_BORDER);

            Color titleColor = Color.White;
            Color raritySubColor = new Color(40, 36, 32, 200);
            string titleText = $"{displayName}  x{quantity}";

            if (!string.IsNullOrEmpty(rarityText))
            {
                int titleW = FontManager.MeasureText(titleText, 16);
                DrawTooltipOutlinedText(titleText, tooltipX + (tooltipW - titleW) / 2, tooltipY + 6, 16, titleColor);
                int rarityW = FontManager.MeasureText(rarityText, 10);
                FontManager.DrawText(rarityText, tooltipX + (tooltipW - rarityW) / 2, tooltipY + 26, 10, raritySubColor);
            }
            else
            {
                int titleW = FontManager.MeasureText(titleText, 16);
                DrawTooltipOutlinedText(titleText, tooltipX + (tooltipW - titleW) / 2, tooltipY + (TOOLTIP_BAND_HEIGHT - 16) / 2, 16, titleColor);
            }

            // --- Icône de l'item, qui déborde légèrement sur la gauche de la bande (sans cadre) ---
            if (item != null)
            {
                int iconX = tooltipX - TOOLTIP_ICON_OVERFLOW;
                int iconY = tooltipY + (TOOLTIP_BAND_HEIGHT - TOOLTIP_ICON_SIZE) / 2;
                ItemRenderer.DrawItem(item, iconX, iconY, TOOLTIP_ICON_SIZE);
            }

            int textY = tooltipY + TOOLTIP_BAND_HEIGHT + 10;

            // --- Description entre guillemets ---
            if (descriptionLines.Count > 0)
            {
                foreach (var line in descriptionLines)
                {
                    foreach (var wrappedLine in WrapTooltipLines(line, contentWidth, 11))
                    {
                        FontManager.DrawText(wrappedLine, contentX, textY, 11, new Color(185, 182, 195, 220));
                        textY += 14;
                    }
                }
                textY += 8;
            }

            // --- Liste des caractéristiques, avec une icône dédiée à gauche de chaque ligne ---
            const int iconColSize = 16;
            const int iconTextGap = 6;
            int listIndent = iconColSize + iconTextGap;
            for (int lineIdx = 0; lineIdx < detailLines.Count; lineIdx++)
            {
                string line = detailLines[lineIdx];
                bool nested = line.StartsWith("   ");
                string trimmed = line.Trim();
                string? iconKey = (detailIconKeys != null && lineIdx < detailIconKeys.Count) ? detailIconKeys[lineIdx] : null;
                int extraIndent = nested ? 10 : 0;

                var wrapped = WrapTooltipLines(trimmed, contentWidth - listIndent - extraIndent, 12);
                int entryHeight = wrapped.Count * DETAIL_LINE_HEIGHT;

                // L'icône occupe exactement la hauteur utilisée par l'information (une ou plusieurs
                // lignes une fois le texte retourné à la ligne), pour rester alignée avec elle.
                Texture2D icon = GetTooltipDetailIcon(iconKey);
                if (icon.Id != 0)
                {
                    Raylib.DrawTexturePro(icon,
                        new Rectangle(0, 0, icon.Width, icon.Height),
                        new Rectangle(contentX + extraIndent, textY, iconColSize, entryHeight),
                        Vector2.Zero, 0f, Color.White);
                }

                for (int i = 0; i < wrapped.Count; i++)
                {
                    FontManager.DrawText(wrapped[i], contentX + extraIndent + listIndent, textY, 12, new Color(205, 202, 190, 255));
                    textY += DETAIL_LINE_HEIGHT;
                }
            }

        }

        private static void DrawTooltipOutlinedText(string text, int x, int y, int fontSize, Color color)
        {
            Color outline = new Color(0, 0, 0, 255);
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    if (offsetX == 0 && offsetY == 0) continue;
                    FontManager.DrawText(text, x + offsetX, y + offsetY, fontSize, outline);
                }
            }
            FontManager.DrawText(text, x, y, fontSize, color);
        }

        // Cache des icônes de caractéristiques de la tooltip (assets/gui/tooltip_icon_xxx.png)
        private static readonly Dictionary<string, Texture2D> _tooltipDetailIconCache = new Dictionary<string, Texture2D>();

        private static Texture2D GetTooltipDetailIcon(string? key)
        {
            string resolvedKey = string.IsNullOrEmpty(key) ? "info" : key;
            if (_tooltipDetailIconCache.TryGetValue(resolvedKey, out var cached))
                return cached;

            string path = $"assets/gui/tooltip_icon_{resolvedKey}.png";
            Texture2D tex = File.Exists(path) ? Raylib.LoadTexture(path) : new Texture2D();

            if (tex.Id == 0 && resolvedKey != "info")
            {
                // Repli sur l'icône générique si celle du type n'existe pas encore
                if (!_tooltipDetailIconCache.TryGetValue("info", out tex))
                {
                    string fallbackPath = "assets/gui/tooltip_icon_info.png";
                    tex = File.Exists(fallbackPath) ? Raylib.LoadTexture(fallbackPath) : new Texture2D();
                    _tooltipDetailIconCache["info"] = tex;
                }
            }

            _tooltipDetailIconCache[resolvedKey] = tex;
            return tex;
        }

        private static Rectangle GetTooltipRect(Vector2 mousePos, int tooltipW, int tooltipH)
        {
            const int offsetX = 20;
            const int offsetY = 20;
            int tooltipX = (int)mousePos.X + offsetX + TOOLTIP_ICON_OVERFLOW;
            int tooltipY = (int)mousePos.Y + offsetY;
            if (tooltipX + tooltipW > _screenWidth)
                tooltipX = (int)mousePos.X - tooltipW - offsetX - TOOLTIP_ICON_OVERFLOW;
            if (tooltipY + tooltipH > _screenHeight)
                tooltipY = (int)mousePos.Y - tooltipH - offsetY;
            if (tooltipX < TOOLTIP_ICON_OVERFLOW)
                tooltipX = TOOLTIP_ICON_OVERFLOW;
            return new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH);
        }

        private static int CalculateSharedTooltipHeight(List<string> detailLines, List<string> descriptionLines, int extraBottomHeight, int tooltipW)
        {
            int contentWidth = Math.Max(80, tooltipW - TOOLTIP_PADDING * 2);
            int descLineCount = descriptionLines.Sum(line => WrapTooltipLines(line, contentWidth, 11).Count);
            int detailLineCount = detailLines.Sum(line =>
            {
                bool nested = line.StartsWith("   ");
                int listIndent = 16 + 6 + (nested ? 10 : 0);
                return WrapTooltipLines(line.Trim(), Math.Max(80, contentWidth - listIndent), 12).Count;
            });
            int descBlock = descLineCount > 0 ? descLineCount * 14 + 8 : 0;
            int detailBlock = detailLineCount * DETAIL_LINE_HEIGHT;
            return TOOLTIP_BAND_HEIGHT + 10 + descBlock + detailBlock + 12 + extraBottomHeight;
        }

        private static int CalculateTooltipWidth(string displayName, string rarityText, List<string> detailLines, List<string> descriptionLines, bool hasInventoryPreview, int quantity = 0, int requestedWidth = 0)
        {
            int measuredWidth = Math.Max(
                FontManager.MeasureText($"{displayName}  x{quantity}", 16),
                FontManager.MeasureText(rarityText, 10));

            measuredWidth = Math.Max(measuredWidth, descriptionLines.Count == 0
                ? 0
                : descriptionLines.Max(line => FontManager.MeasureText(line, 11)));

            foreach (string line in detailLines)
                measuredWidth = Math.Max(measuredWidth, FontManager.MeasureText(line.Trim(), 12) + 16 + 6 + (line.StartsWith("   ") ? 10 : 0));

            if (hasInventoryPreview)
                measuredWidth = Math.Max(measuredWidth, 10 * 26 + 16);

            int width = Math.Max(TOOLTIP_MIN_WIDTH, measuredWidth + TOOLTIP_PADDING * 2 + 8);
            if (requestedWidth > 0)
                width = Math.Max(width, Math.Min(requestedWidth, TOOLTIP_MAX_WIDTH));

            int screenLimit = Math.Max(TOOLTIP_MIN_WIDTH, _screenWidth - TOOLTIP_ICON_OVERFLOW * 2 - 24);
            return Math.Min(width, Math.Min(TOOLTIP_MAX_WIDTH, screenLimit));
        }

        private static void AddMetadataTooltipLines(Item item, List<string> detailLines, List<string> detailIconKeys)
        {
            var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in detailLines)
            {
                int separatorIndex = line.IndexOf(':');
                if (separatorIndex > 0)
                    knownKeys.Add(line.Substring(0, separatorIndex).Trim());
            }

            string rawMetadata = ItemRenderer.NormalizePotionMetadata(item.Metadata);
            foreach (string part in rawMetadata.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith("RUNES:", StringComparison.OrdinalIgnoreCase))
                    continue;

                int separatorIndex = part.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    detailLines.Add($"Métadonnée : {part}");
                    string rawKey = part.Split(':', 2)[0];
                    detailIconKeys.Add(GetMetadataIconKey(rawKey));
                    continue;
                }

                string key = part.Substring(0, separatorIndex).Trim();
                if (!knownKeys.Add(key))
                    continue;

                string value = FormatMetadataValue(key, part.Substring(separatorIndex + 1).Trim());
                detailLines.Add($"{GetMetadataLabel(key)} : {value}");
                detailIconKeys.Add(GetMetadataIconKey(key));
            }

            foreach (var entry in item.Meta)
            {
                if (!knownKeys.Add(entry.Key))
                    continue;

                detailLines.Add($"{GetMetadataLabel(entry.Key)} : {FormatMetadataValue(entry.Key, entry.Value)}");
                detailIconKeys.Add(GetMetadataIconKey(entry.Key));
            }
        }

        private static string GetMetadataLabel(string key) => key.ToLowerInvariant() switch
        {
            "durability" => "Durabilité",
            "cookstage" => "Cuisson",
            "spoil" => "Fraîcheur",
            "spoiled" => "État",
            "water" => "Eau",
            "saturation" => "Satiété",
            "sleeves" => "Manches",
            "opening" => "Ouverture",
            "floor_style" => "Style",
            "recipeid" => "Recette",
            "quality" => "Qualité",
            _ => char.ToUpperInvariant(key[0]) + key.Substring(1)
        };

        private static string FormatMetadataValue(string key, string value)
        {
            if (!string.Equals(key, "spoil", StringComparison.OrdinalIgnoreCase))
                return value;

            string[] parts = value.Split('/', 2);
            if (parts.Length == 0 || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float current))
                return value;

            string formattedCurrent = ((int)current).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (parts.Length == 1)
                return formattedCurrent;

            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float maximum))
                return formattedCurrent + "/" + parts[1];

            return $"{formattedCurrent}/{((int)maximum).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        private static string GetMetadataIconKey(string key) => key.ToLowerInvariant() switch
        {
            "cookstage" => "fire",
            "spoil" or "spoiled" => "spoil",
            "effect" or "heal" => "heal",
            "water" => "water",
            "saturation" => "saturation",
            "stacksize" or "stackable" => "stackable",
            "quality" => "value",
            _ => "info"
        };


        private static List<string> WrapTooltipLines(string text, int maxWidth, int fontSize)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
                return lines;

            var words = text.Split(' ');
            string current = string.Empty;
            foreach (var word in words)
            {
                string candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
                if (FontManager.MeasureText(candidate, fontSize) > maxWidth && !string.IsNullOrEmpty(current))
                {
                    lines.Add(current.Trim());
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            if (!string.IsNullOrEmpty(current))
                lines.Add(current.Trim());

            return lines;
        }

        public static void HandleSlotClick(Vector2 mp, Equipment eq)
        {
            if (!IsInventoryOpen) return;

            bool draggingBodyItem = false;
            if (DraggedItem.IsDragging && DraggedItem.Item != null)
            {
                int draggedId = Program.GetItemId(DraggedItem.Item.Name);
                draggingBodyItem = GameData.ItemDatabase.TryGetValue(draggedId, out var draggedData)
                    && draggedData.CoveredZones.Count > 0;

                if (draggingBodyItem && TryHitZonePieces(_dragZonePieceRects, mp, out var draggedZone))
                {
                    HandleZoneInteraction(draggedZone, eq, mp);
                    return;
                }
            }

            // 1. Vérifier les emplacements fonctionnels (arme/bouclier ou offhand). Priorité
            // sur les zones du corps : l'offhand partage le même bone ("larmbottom") que les
            // manches d'un vêtement de torse, et DrawEntity dessine justement l'objet en main
            // gauche PAR-DESSUS la manche à cet endroit - sans cette priorité, un torse équipé
            // "mangeait" le clic destiné à l'offhand et rendait impossible son retrait.
            if (!draggingBodyItem)
            {
                foreach (var kv in _slotPieceRects)
                {
                    if (!IsSlotPieceHit(kv.Value, mp))
                        continue;

                    HandleEquipInteraction(kv.Key, 0, eq, mp);
                    return;
                }


                foreach (var kv in _equipSlotRects)
                {
                    if (kv.Key.slot == null) continue;
                    if (_slotPieceRects.ContainsKey(kv.Key.slot.Value)) continue;
                    if (Raylib.CheckCollisionPointRec(mp, kv.Value))
                    {
                        HandleEquipInteraction(kv.Key.slot.Value, 0, eq, mp);
                        return;
                    }
                }
            }

            foreach (var kv in _equipmentBarSlotRects)
            {
                if (!Raylib.CheckCollisionPointRec(mp, kv.Value)) continue;
                if (kv.Key.zone != null)
                    HandleZoneInteraction(kv.Key.zone.Value, eq, mp);
                else if (kv.Key.slot != null)
                    HandleEquipInteraction(kv.Key.slot.Value, 0, eq, mp);
                return;
            }

            // 2. Vérifier les zones du corps (armure/accessoires), sur la silhouette réelle :
            // pendant un drag, la cible est la silhouette de l'item déplacé (zone de dépôt) ;
            // sinon, celle de l'item actuellement équipé (retrait/échange).
            var zoneSource = DraggedItem.IsDragging ? _dragZonePieceRects : _zonePieceRects;
            if (TryHitZonePieces(zoneSource, mp, out var hitZone))
            {
                HandleZoneInteraction(hitZone, eq, mp);
                return;
            }

            // 3. Vérifier les slots de la belt
            for (int i = 0; i < InventorySlots.Count; i++)
            {
                ModuleInfo? owner = _modules.FirstOrDefault(m => i >= m.SlotStartIndex && i < m.SlotStartIndex + m.SlotCount);
                if (owner == null) continue;

                int col = (i - owner.SlotStartIndex) % SLOTS_PER_ROW;
                int row = (i - owner.SlotStartIndex) / SLOTS_PER_ROW;
                int startX = (int)owner.Bounds.X + PANEL_PADDING;
                int startY = (int)owner.Bounds.Y + PANEL_PADDING + (string.IsNullOrEmpty(owner.Title) ? 0 : 20) - (int)_scrollOffsetByPlayer[0];
                int slotX = startX + col * SLOT_STEP;
                int slotY = startY + row * SLOT_STEP;

                if (Raylib.CheckCollisionPointRec(mp, new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE)))
                {
                    HandleSlotInteraction(i, mp, eq);
                    return;
                }
            }

            // 4. Vérifier les slots des conteneurs
            foreach (var module in _modules.Where(m => m.SlotStartIndex == -1))
            {
                for (int i = 0; i < module.SlotCount; i++)
                {
                    int col = i % SLOTS_PER_ROW;
                    int row = i / SLOTS_PER_ROW;
                    int startX = (int)module.Bounds.X + PANEL_PADDING;
                    int startY = (int)module.Bounds.Y + PANEL_PADDING + (string.IsNullOrEmpty(module.Title) ? 0 : 20) - (int)_scrollOffsetByPlayer[0];
                    int slotX = startX + col * SLOT_STEP;
                    int slotY = startY + row * SLOT_STEP;

                    if (Raylib.CheckCollisionPointRec(mp, new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE)))
                    {
                        var slot = module.SlotGetter(i);
                        if (module.Title == "PANIER" && eq.OffHandContainer != null)
                        {
                            HandleContainerSlotClick(i, eq.OffHandContainer, module.SlotGetter);
                        }
                        else if (module.Title == "PANIER (main)" && eq.MainHandContainer != null)
                        {
                            HandleContainerSlotClick(i, eq.MainHandContainer, module.SlotGetter);
                        }
                        else if (module.CustomTexture == UIManager.BackpackPanelTexture && eq.BackpackContainer != null)
                        {
                            HandleContainerSlotClick(i, eq.BackpackContainer, module.SlotGetter);
                        }
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Tente de sertir la pierre runique actuellement en drag sur l'item cible (une armure avec
        /// un emplacement de rune libre). Retourne true si l'interaction a été traitée (que ce soit
        /// un succès, un échec "aucun slot libre", ou "pas une armure à runes") afin que l'appelant
        /// n'exécute pas en plus sa logique habituelle de clic droit.
        /// </summary>
        private static bool TrySocketDraggedRune(Item? targetItem)
        {
            if (!DraggedItem.IsDragging || DraggedItem.Item == null || targetItem == null) return false;

            int runeItemId = Program.GetItemId(DraggedItem.Item.Name);
            if (!GameData.ItemDatabase.TryGetValue(runeItemId, out var runeItemData) || !runeItemData.IsRune)
                return false; // L'item en drag n'est pas une pierre runique : laisser l'appelant gérer le clic droit normalement.

            int targetItemId = Program.GetItemId(targetItem.Name);
            if (!GameData.ItemDatabase.TryGetValue(targetItemId, out var targetItemData) || targetItemData.RuneSlots <= 0)
            {
                Program.AddNotification(new Notification(" Cet équipement ne possède aucun emplacement de rune.", new Color(255, 160, 120, 255), 1.6f));
                return true;
            }

            targetItem.EnsureRuneSlots(targetItemData.RuneSlots);
            int freeSlot = targetItem.FirstFreeRuneSlot();
            if (freeSlot < 0)
            {
                Program.AddNotification(new Notification(" Aucun emplacement de rune libre sur cet équipement.", new Color(255, 160, 120, 255), 1.6f));
                return true;
            }

            var rune = RuneData.FromMetadata(DraggedItem.Item.Metadata);
            targetItem.SocketedRunes[freeSlot] = rune;
            targetItem.SyncRunesToMetadata();

            DraggedItem.Count -= 1;
            if (DraggedItem.Count <= 0) DraggedItem.EndDrag();

            Program.AddNotification(new Notification($" Rune sertie : {rune.DisplayName}", rune.DisplayColor, 1.8f));
            Program.PlayEquipSound();
            return true;
        }

        //  Interaction (équiper/déséquiper) pour un item couvrant une zone du corps
        private static void HandleZoneInteraction(BodyZone zone, Equipment eq, Vector2 mp)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                // Clic droit : ne sert qu'à sertir une rune en cours de drag sur l'armure équipée ici.
                TrySocketDraggedRune(eq.GetZoneItem(zone));
                return;
            }

            if (DraggedItem.IsDragging && DraggedItem.Item != null)
            {
                int itemId = Program.GetItemId(DraggedItem.Item.Name);
                if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData) || !itemData.CoveredZones.Contains(zone))
                {
                    return;
                }

                var draggedItem = DraggedItem.Item;
                bool success = eq.EquipOnBody(draggedItem, out var unequipped);
                if (success)
                {
                    DraggedItem.EndDrag();
                    foreach (var displaced in unequipped)
                        Program.AddItemToInventory(displaced, 1);
                    eq.LoadEquipmentTextures();
                    Program.PlayEquipSound();
                }
            }
            else
            {
                // Déséquiper et prendre l'item en drag
                Item? equippedItem = eq.GetZoneItem(zone);
                if (equippedItem != null)
                {
                    eq.UnequipFromBody(equippedItem);
                    DraggedItem.StartDrag(equippedItem, equippedItem.Count > 0 ? equippedItem.Count : 1, mp, -1, null, -1, zone);
                    eq.LoadEquipmentTextures();
                }
            }
        }

        private static void HandleEquipInteraction(EquipmentSlot slot, int index, Equipment eq, Vector2 mp)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                // Clic droit : ne sert qu'à sertir une rune en cours de drag sur l'item équipé ici.
                TrySocketDraggedRune(eq.GetItemInSlot(slot, index));
                return;
            }

            if (DraggedItem.IsDragging && DraggedItem.Item != null)
            {
                int itemId = Program.GetItemId(DraggedItem.Item.Name);
                if (GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
                {
                    if (itemData.HasEquipSlot && itemData.EquipSlot == slot)
                    {
                        Item? oldItem = eq.GetItemInSlot(slot, index);
                        if (oldItem != null)
                        {
                            eq.UnEquipItem(slot, index);
                        }

                        int currentCount = eq.GetEquipCount(slot);
                        if (currentCount >= itemData.EquipMax)
                        {
                            if (oldItem != null)
                            {
                                eq.EquipItem(oldItem, slot, index);
                            }
                            return;
                        }

                        bool success = eq.EquipItem(DraggedItem.Item, slot, index);
                        
                        if (success)
                        {
                            if (oldItem != null)
                            {
                                Program.AddItemToInventory(oldItem, 1);
                            }
                            DraggedItem.EndDrag();
                            eq.LoadEquipmentTextures();
                            Program.PlayEquipSound();
                        }
                        else if (oldItem != null)
                        {
                            eq.EquipItem(oldItem, slot, index);
                        }
                    }
                    else
                    {
                    }
                }
            }
            else
            {
                // Déséquiper et prendre l'item en drag
                Item? equippedItem = eq.GetItemInSlot(slot, index);
                if (equippedItem != null)
                {
                    eq.UnEquipItem(slot, index);
                    DraggedItem.StartDrag(equippedItem, equippedItem.Count > 0 ? equippedItem.Count : 1, mp, -1, slot, index);
                    eq.LoadEquipmentTextures();
                }
            }
        }

        private static void HandleContainerSlotClick(int slotIndex, IContainerInventory container, Func<int, InventorySlot?> slotGetter)
        {
            var slot = slotGetter(slotIndex);
            if (slot == null) return;

            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if ((Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift)) && ContainerUI.IsOpen)
                {
                    if (!slot.IsEmpty && slot.Item != null)
                    {
                        int remaining = ContainerUI.AddItemToContainer(slot.Item, slot.Count);
                        if (remaining == 0)
                            slot.Clear();
                        else if (remaining < slot.Count)
                            slot.Count = remaining;
                    }
                    return;
                }

                if (DraggedItem.IsDragging)
                {
                    if (slot.IsEmpty)
                    {
                        int dropCount = Math.Max(1, DraggedItem.Count);
                        slot.Item = DraggedItem.Item;
                        slot.Count = dropCount;
                        DraggedItem.EndDrag();
                    }
                    else if (slot.Item != null && DraggedItem.Item != null && AreItemsCompatible(slot.Item, DraggedItem.Item))
                    {
                        int maxStack = GameData.ItemDatabase[Program.GetItemId(slot.Item.Name)].StackSize;
                        int space = maxStack - slot.Count;
                        if (space > 0)
                        {
                            int add = Math.Min(space, Math.Max(1, DraggedItem.Count));
                            slot.Count += add;
                            DraggedItem.Count -= add;
                            if (DraggedItem.Count <= 0) DraggedItem.EndDrag();
                        }
                    }
                    else if (slot.Item != null && DraggedItem.Item != null)
                    {
                        var tempItem = slot.Item;
                        var tempCount = slot.Count;
                        slot.Item = DraggedItem.Item;
                        slot.Count = Math.Max(1, DraggedItem.Count);
                        DraggedItem.Item = tempItem;
                        DraggedItem.Count = tempCount;
                    }
                }
                else
                {
                    if (!slot.IsEmpty && slot.Item != null)
                    {
                        DraggedItem.StartDrag(slot.Item, slot.Count, Raylib.GetMousePosition());
                        slot.Clear();
                    }
                }
            }
            else if (Raylib.IsMouseButtonPressed(MouseButton.Right) && DraggedItem.IsDragging)
            {
                TrySocketDraggedRune(slot.Item);
            }
            else if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !DraggedItem.IsDragging && !slot.IsEmpty && slot.Item != null)
            {
                int half = slot.Count / 2;
                if (half > 0)
                {
                    DraggedItem.StartDrag(slot.Item, half, Raylib.GetMousePosition());
                    slot.Count -= half;
                }
            }
        }

        private static void HandleSlotInteraction(int idx, Vector2 mp, Equipment eq)
        {
            var slot = InventorySlots[idx];

            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if ((Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift)) && ContainerUI.IsOpen)
                {
                    TransferToContainer(idx);
                    return;
                }

                if (DraggedItem.IsDragging)
                {
                    if (slot.IsEmpty)
                    {
                        int dropCount = Math.Max(1, DraggedItem.Count);
                        slot.Item = DraggedItem.Item;
                        slot.Count = dropCount;
                        DraggedItem.EndDrag();
                    }
                    else if (slot.Item != null && DraggedItem.Item != null)
                    {
                        if (AreItemsCompatible(slot.Item, DraggedItem.Item))
                        {
                            int maxStack = GameData.ItemDatabase[Program.GetItemId(slot.Item.Name)].StackSize;
                            int space = maxStack - slot.Count;
                            if (space > 0)
                            {
                                int add = Math.Min(space, Math.Max(1, DraggedItem.Count));
                                Program.MergeSpoilMeta(slot.Item, DraggedItem.Item, add);
                                slot.Count += add;
                                DraggedItem.Count -= add;
                                if (DraggedItem.Count <= 0) DraggedItem.EndDrag();
                            }
                        }
                        else
                        {
                            var tempItem = slot.Item;
                            var tempCount = slot.Count;
                            slot.Item = DraggedItem.Item;
                            slot.Count = Math.Max(1, DraggedItem.Count);
                            DraggedItem.Item = tempItem;
                            DraggedItem.Count = tempCount;
                        }
                    }
                }
                else
                {
                    if (!slot.IsEmpty && slot.Item != null)
                    {
                        SelectedSlot = idx;
                        DraggedItem.StartDrag(slot.Item, slot.Count, mp, idx);
                        slot.Clear();
                    }
                    else
                    {
                        SelectedSlot = idx;
                    }
                }
            }
            else if (Raylib.IsMouseButtonPressed(MouseButton.Right) && DraggedItem.IsDragging)
            {
                TrySocketDraggedRune(slot.Item);
            }
            else if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !DraggedItem.IsDragging && !slot.IsEmpty && slot.Item != null)
            {
                if (TryEquipItemFromInventorySlot(idx, eq))
                    return;

                int half = slot.Count / 2;
                if (half > 0)
                {
                    DraggedItem.StartDrag(slot.Item, half, mp, idx);
                    slot.Count -= half;
                }
            }
        }

        private static bool TryEquipItemFromInventorySlot(int idx, Equipment eq)
        {
            var slot = InventorySlots[idx];
            if (slot.IsEmpty || slot.Item == null) return false;

            int itemId = Program.GetItemId(slot.Item.Name);
            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData)) return false;

            //  Cas 1 : item couvrant une ou plusieurs zones du corps
            if (itemData.CoveredZones.Count > 0)
            {
                var item = slot.Item;
                bool success = eq.EquipOnBody(item, out var unequipped);
                if (!success) return false;

                slot.Clear();
                foreach (var displaced in unequipped)
                    Program.AddItemToInventory(displaced, 1);

                eq.LoadEquipmentTextures();
                Program.PlayEquipSound();
                return true;
            }

            //  Cas 2 : emplacement fonctionnel (arme, sac vide...)
            if (!itemData.HasEquipSlot) return false;

            EquipmentSlot targetSlot = itemData.EquipSlot;

            Item? currentEquipped = eq.GetItemInSlot(targetSlot, 0);
            if (currentEquipped != null)
            {
                Item? removed = eq.UnEquipItem(targetSlot, 0);
                if (removed != null)
                    Program.AddItemToInventory(removed, 1);
            }

            int slotIndex = -1;
            for (int i = 0; i < itemData.EquipMax; i++)
            {
                if (eq.GetItemInSlot(targetSlot, i) == null)
                {
                    slotIndex = i;
                    break;
                }
            }

            if (slotIndex == -1)
            {
                if (itemData.EquipMax == 1)
                {
                    var removed = eq.UnEquipItem(targetSlot, 0);
                    if (removed != null)
                        Program.AddItemToInventory(removed, 1);
                    slotIndex = 0;
                }
                else
                {
                    return false;
                }
            }

            if (!eq.EquipItem(slot.Item, targetSlot, slotIndex))
                return false;

            slot.Clear();
            eq.LoadEquipmentTextures();
            Program.PlayEquipSound();
            return true;
        }

        private static void TransferToContainer(int idx)
        {
            var slot = InventorySlots[idx];
            if (slot.IsEmpty || slot.Item == null) return;
            int remaining = ContainerUI.AddItemToContainer(slot.Item, slot.Count);
            if (remaining == 0)
                slot.Clear();
            else
                slot.Count = remaining;
        }

        private static bool AreItemsCompatible(Item a, Item b)
        {
            return Program.AreItemsStackable(a, b);
        }

        public static void ConvertToNewInventory(List<Item> old)
        {
            Initialize();
            foreach (var it in old)
            {
                int id = Program.GetItemId(it.Name);
                if (id > 0 && GameData.ItemDatabase.TryGetValue(id, out var data))
                    AddItemToInventory(data, it.Count);
            }
        }

        private static void AddItemToInventory(ItemData itemData, int count)
        {
            int remaining = count;
            foreach (var slot in InventorySlots)
            {
                if (remaining <= 0) break;
                if (!slot.IsEmpty && slot.Item != null && slot.Item.Name == itemData.Name)
                {
                    int space = itemData.StackSize - slot.Count;
                    if (space > 0)
                    {
                        int add = Math.Min(space, remaining);
                        slot.Count += add;
                        remaining -= add;
                    }
                }
            }
            while (remaining > 0)
            {
                bool added = false;
                foreach (var slot in InventorySlots)
                {
                    if (slot.IsEmpty)
                    {
                        slot.Item = new Item(itemData.Name, 0, itemData.Color, itemData.Icon, null);
                        slot.Count = Math.Min(remaining, itemData.StackSize);
                        remaining -= slot.Count;
                        added = true;
                        break;
                    }
                }
                if (!added) break;
            }
        }

        public static List<Item> ConvertToOldInventory()
        {
            var res = new List<Item>();
            foreach (var slot in InventorySlots)
                if (!slot.IsEmpty && slot.Item != null)
                    res.Add(new Item(slot.Item.Name, slot.Count, slot.Item.DisplayColor, slot.Item.Icon));
            return res;
        }

        public static void CloseInventory(int playerIndex = 0)
        {
            IsInventoryOpen = false;
            _tooltipSlot = null;
            _tooltipHoverTimer = 0f;
            if (playerIndex >= 0)
            {
                _isDraggingWindowByPlayer[playerIndex] = false;
                _dragOffsetByPlayer[playerIndex] = Vector2.Zero;
            }
            SelectedSlot = -1;
            _hoveredEquipSlot = null;
        }
    }
}