using Raylib_cs;
using System.Collections.Generic;
using System.Numerics;

namespace Soulfract
{
    public static class ContainerUI
    {
        private static bool _isOpen = false;
        private static readonly Dictionary<int, bool> _isOpenByPlayer = new();
        private static readonly Dictionary<int, ContainerInventoryData?> _currentContainerByPlayer = new();
        private static readonly Dictionary<int, bool> _isCampfireByPlayer = new();
        private static readonly Dictionary<int, int> _hoveredSlotByPlayer = new();
        private static readonly Dictionary<int, float> _tooltipHoverTimeByPlayer = new();
        //  MULTIJOUEUR : coordonnées du conteneur actuellement ouvert (pour SyncContainer/
        // RequestContainerData). (-1,-1) = conteneur non networké (ex: coffre de bateau).
        private static readonly Dictionary<int, (int tileX, int tileY, bool underground)> _currentContainerPosByPlayer = new();
        private static DraggedItem _draggedItem = new DraggedItem();

        private static int SLOT_SIZE => UIManager.ItemSlotSize;
        private static int SLOT_STEP => UIManager.ItemSlotStep;
        private static int ITEM_SIZE => UIManager.ItemContentSize;
        private static int ITEM_OFFSET => (SLOT_SIZE - ITEM_SIZE) / 2;
        private static int HEADER_HEIGHT => UIManager.ScaleInt(45);
        private static int CLOSE_BTN_SIZE => UIManager.ScaleInt(28);
        private static int PANEL_TOP_MARGIN => UIManager.ScaleInt(25);
        
        private static readonly Color COLOR_BG = new Color(25, 28, 35, 240);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
        private static readonly Color COLOR_BORDER = new Color(100, 100, 120, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_SLOT_NORMAL = new Color(50, 53, 60, 255);
        private static readonly Color COLOR_SLOT_HOVER = new Color(70, 73, 80, 255);
        
        // Textures 9-slice
        private static NineSliceTexture? _panelTexture;
        private static NineSliceTexture? _slotTexture;
        private static bool _useTextures = false;
        
        // Variables pour le panneau déplaçable
        private static readonly Dictionary<int, Vector2> _windowPosByPlayer = new();
        private static readonly Dictionary<int, Vector2> _dragOffsetByPlayer = new();
        private static readonly Dictionary<int, bool> _isDraggingWindowByPlayer = new();
        private static readonly Dictionary<int, bool> _isHoveringTitleBarByPlayer = new();
        
        // Dimensions calculées dynamiquement
        private static int _panelWidth = 0;
        private static int _panelHeight = 0;
        
        //  NOUVEAU : Référence au DraggedItem partagé
        private static DraggedItem? _sharedDraggedItem;
        
        public static bool IsOpen => _isOpen;
        public static bool IsOpenForPlayer(int playerIndex)
        {
            if (!_isOpenByPlayer.ContainsKey(playerIndex)) _isOpenByPlayer[playerIndex] = false;
            return _isOpenByPlayer[playerIndex];
        }
        
        public static void Initialize(DraggedItem sharedDraggedItem)
        {
            _sharedDraggedItem = sharedDraggedItem;
            _panelTexture = UIManager.WoodPanel;
            _slotTexture = UIManager.SlotTexture;
            _useTextures = (_panelTexture != null && _panelTexture.IsValid);
        }
        
        public static void Open(ContainerInventoryData container, int playerIndex = 0, int tileX = -1, int tileY = -1, bool underground = false)
        {
            if (!_windowPosByPlayer.ContainsKey(playerIndex)) _windowPosByPlayer[playerIndex] = UIManager.ScalePosition(200, 150);
            if (!_dragOffsetByPlayer.ContainsKey(playerIndex)) _dragOffsetByPlayer[playerIndex] = Vector2.Zero;
            if (!_isDraggingWindowByPlayer.ContainsKey(playerIndex)) _isDraggingWindowByPlayer[playerIndex] = false;
            if (!_isHoveringTitleBarByPlayer.ContainsKey(playerIndex)) _isHoveringTitleBarByPlayer[playerIndex] = false;
            _currentContainerByPlayer[playerIndex] = container;
            _isCampfireByPlayer[playerIndex] = false;
            _currentContainerPosByPlayer[playerIndex] = (tileX, tileY, underground);
            _isOpenByPlayer[playerIndex] = true;
            _isOpen = true;
            _sharedDraggedItem?.EndDrag();
            
            // Ouvrir l'inventaire si ce n'est pas déjà fait
            if (!InventoryRenderer.IsInventoryOpen)
                InventoryRenderer.Open(playerIndex);

            //  MULTIJOUEUR : si on est client et que ce conteneur est networké (tileX/Y
            // valides), on demande son contenu à jour au host — notre copie locale peut
            // être périmée (un autre joueur a pu le modifier depuis notre dernière visite).
            if (NetworkManager.IsClient && tileX != -1)
                NetworkManager.RequestContainerData(tileX, tileY, underground);
            
            // Calculer les dimensions du panneau en fonction du nombre de slots
            int cols = container.Columns;
            int rows = container.Rows;
            int gridWidth = cols * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
            int gridHeight = rows * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
            
            _panelWidth = gridWidth + UIManager.ScaleInt(100); // Marges latérales
            _panelHeight = HEADER_HEIGHT + gridHeight + UIManager.ScaleInt(60); // Header + marge haut + marge bas
        }

        public static void OpenCampfire(ContainerInventoryData container, int playerIndex = 0, int tileX = -1, int tileY = -1, bool underground = false)
        {
            Open(container, playerIndex, tileX, tileY, underground);
            _isCampfireByPlayer[playerIndex] = true;

            container.Columns = Math.Min(4, Math.Max(1, container.Slots.Count));
            container.Rows = Math.Max(1, (int)Math.Ceiling((double)container.Slots.Count / container.Columns));
            int gridWidth = container.Columns * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
            int gridHeight = container.Rows * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
            _panelWidth = gridWidth + UIManager.ScaleInt(40);
            _panelHeight = HEADER_HEIGHT + gridHeight + UIManager.ScaleInt(96);
        }

        public static void Close(int playerIndex = 0)
		{
			_isOpenByPlayer[playerIndex] = false;
			_currentContainerByPlayer[playerIndex] = null;
            _isCampfireByPlayer[playerIndex] = false;
			_isOpen = false;
			_sharedDraggedItem?.EndDrag();
			_isDraggingWindowByPlayer[playerIndex] = false;
			_dragOffsetByPlayer[playerIndex] = Vector2.Zero;
		}

        public static void Update(int playerIndex = 0, Rectangle? viewport = null)
        {
            if (!IsOpenForPlayer(playerIndex)) return;
            var currentContainer = _currentContainerByPlayer.TryGetValue(playerIndex, out var container) ? container : null;
            if (currentContainer == null) return;

            Vector2 mousePos = Raylib.GetMousePosition();
            if (viewport.HasValue)
                mousePos -= new Vector2(viewport.Value.X, viewport.Value.Y);
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            
            var windowPos = _windowPosByPlayer.ContainsKey(playerIndex) ? _windowPosByPlayer[playerIndex] : UIManager.ScalePosition(200, 150);
            int visibleMargin = UIManager.ScaleInt(100);
            windowPos.X = Math.Clamp(windowPos.X, -_panelWidth + visibleMargin, sw - visibleMargin);
            windowPos.Y = Math.Clamp(windowPos.Y, 0, sh - visibleMargin);
            _windowPosByPlayer[playerIndex] = windowPos;
            
            int panelX = (int)windowPos.X;
            int panelY = (int)windowPos.Y;
            
            Rectangle titleBar = new Rectangle(panelX, panelY, _panelWidth, HEADER_HEIGHT);
            _isHoveringTitleBarByPlayer[playerIndex] = Raylib.CheckCollisionPointRec(mousePos, titleBar);
            
            if (_isHoveringTitleBarByPlayer[playerIndex] && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDraggingWindowByPlayer[playerIndex] = true;
                _dragOffsetByPlayer[playerIndex] = mousePos - windowPos;
            }
            if (_isDraggingWindowByPlayer[playerIndex] && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                windowPos = mousePos - _dragOffsetByPlayer[playerIndex];
                _windowPosByPlayer[playerIndex] = windowPos;
            }
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDraggingWindowByPlayer[playerIndex] = false;
            
            //  CALCUL DES POSITIONS DES SLOTS DU CONTENEUR (pour les interactions)
            int columns = currentContainer.Columns;
            int rows = currentContainer.Rows;
            int totalWidth = columns * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
            int startX = panelX + (_panelWidth - totalWidth) / 2;
            int startY = panelY + HEADER_HEIGHT + PANEL_TOP_MARGIN;
            
			if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !_sharedDraggedItem.IsDragging)
			{
				// Trouver le slot survolé
				int hoveredIndex = -1;
                for (int i = currentContainer.Slots.Count - 1; i >= 0; i--)
				{
					int row = i / columns;
					int col = i % columns;
                    int slotX = startX + col * SLOT_STEP;
                    int slotY = startY + row * SLOT_STEP;
					if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE)))
					{
						hoveredIndex = i;
						break;
					}
				}
				
				if (hoveredIndex != -1)
				{
					// Si Shift est enfoncé, transfert direct vers l'inventaire
					if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))
					{
						TransferToInventory(hoveredIndex);
						return;
					}
					else
					{
						HandleContainerSlotClick(hoveredIndex);
						return;
					}
				}
			}
			
			if (_isDraggingWindowByPlayer[playerIndex] && !Raylib.IsMouseButtonDown(MouseButton.Left))
				_isDraggingWindowByPlayer[playerIndex] = false;
            
            //  DÉPÔT D'UN ITEM DRAGUÉ DANS LE CONTENEUR
            if (_sharedDraggedItem.IsDragging && Raylib.IsMouseButtonReleased(MouseButton.Left))
            {
                for (int i = currentContainer.Slots.Count - 1; i >= 0; i--)
                {
                    int row = i / columns;
                    int col = i % columns;
                    int slotX = startX + col * SLOT_STEP;
                    int slotY = startY + row * SLOT_STEP;
                    if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE)))
                    {
                        TryDropOnContainerSlot(i);
                        break;
                    }
                }
            }
        }
        
        private static void HandleContainerSlotClick(int idx)
        {
            var currentContainer = _currentContainerByPlayer.TryGetValue(0, out var container) ? container : null;
            if (currentContainer == null) return;
            
            var slot = currentContainer.Slots[idx];
            
            if (!slot.IsEmpty && slot.Item != null)
            {
                _sharedDraggedItem.StartDrag(slot.Item, slot.Count, Raylib.GetMousePosition());
                slot.Clear();
                SyncCurrentContainer();
            }
			
			if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))
			{
				TransferToInventory(idx);
				return;
			}
        }
		
public static ContainerInventoryData? GetCurrentContainer(int playerIndex = 0) => _currentContainerByPlayer.TryGetValue(playerIndex, out var container) ? container : null;

        public static void NotifyCurrentContainerChanged(int playerIndex = 0)
        {
            SyncCurrentContainer(playerIndex);
        }

		//  MULTIJOUEUR : à appeler après toute modification du contenu du conteneur ouvert
		// (dépose, retrait, fusion, échange...) pour que les autres joueurs voient le
		// changement. Sans coordonnées networkées (boat container) ou hors ligne : no-op.
		private static void SyncCurrentContainer(int playerIndex = 0)
		{
			if (!NetworkManager.IsOnline) return;
			if (!_currentContainerPosByPlayer.TryGetValue(playerIndex, out var pos) || pos.tileX == -1) return;
			var currentContainer = _currentContainerByPlayer.TryGetValue(playerIndex, out var container) ? container : null;
			if (currentContainer == null) return;

			var save = new ContainerInventorySave();
			foreach (var slot in currentContainer.Slots)
			{
				save.Slots.Add(new InventorySlotSave
				{
					ItemName = slot.Item?.Name ?? "",
					Count = slot.Count,
					IsEmpty = slot.IsEmpty,
					CustomColors = SaveSystem.SaveCustomColors(slot.Item?.CustomColors)
				});
			}
			NetworkManager.SyncContainer(pos.tileX, pos.tileY, pos.underground, save);
		}

		public static int AddItemToContainer(Item item, int count, int playerIndex = 0)
		{
			var currentContainer = _currentContainerByPlayer.TryGetValue(playerIndex, out var container) ? container : null;
			if (currentContainer == null) return count;
            if (_isCampfireByPlayer.GetValueOrDefault(playerIndex) && !CanPlaceOnCampfire(item))
                return count;
			int remaining = count;
			int maxStack = GameData.ItemDatabase[Program.GetItemId(item.Name)].StackSize;

			// 1. Fusion avec les items existants de même type et même couleur
			foreach (var slot in currentContainer.Slots)
			{
				if (remaining <= 0) break;
				if (!slot.IsEmpty && slot.Item != null && AreItemsCompatible(slot.Item, item))
				{
					int space = maxStack - slot.Count;
					if (space > 0)
					{
						int add = Math.Min(space, remaining);
						Program.MergeSpoilMeta(slot.Item, item, add);
						slot.Count += add;
						remaining -= add;
					}
				}
			}

			// 2. Remplissage dans les slots vides
			while (remaining > 0)
			{
				bool added = false;
				foreach (var slot in currentContainer.Slots)
				{
					if (slot.IsEmpty)
					{
						//  CRÉATION D'UNE COPIE COMPLÈTE DE L'ITEM
                        var newItem = _isCampfireByPlayer.GetValueOrDefault(playerIndex)
                            ? CloneItemInstance(item)
                            : new Item(item.Name, 0, item.BaseColor, item.Icon, null);
						if (_isCampfireByPlayer.GetValueOrDefault(playerIndex))
							InitializeCookedStage(newItem);
						// Copier les couleurs multiples si présentes
						if (item.CustomColors != null && item.CustomColors.Count > 0)
						{
							newItem.CustomColors = new List<Color?>(item.CustomColors);
						}
						else if (item.CustomColor.HasValue)
						{
							newItem.CustomColors = new List<Color?> { item.CustomColor.Value };
						}
						//  CRUCIAL : copier les conteneurs attachés
						newItem.Container = item.Container;   // panier
						newItem.Backpack = item.Backpack;     // sac à dos
                        newItem.Metadata = item.Metadata ?? "";
                        newItem.Meta = item.Meta != null
                            ? new Dictionary<string, string>(item.Meta, StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

						slot.Item = newItem;
						slot.Count = Math.Min(remaining, maxStack);
						remaining -= slot.Count;
						added = true;
						break;
					}
				}
				if (!added) break;
			}
			SyncCurrentContainer(playerIndex);
			return remaining;
		}

		private static void TransferToInventory(int idx)
		{
			var currentContainer = _currentContainerByPlayer.TryGetValue(0, out var container) ? container : null;
			if (currentContainer == null) return;
			var slot = currentContainer.Slots[idx];
			if (slot.IsEmpty || slot.Item == null) return;
			
			int remaining = Program.AddItemToInventory(slot.Item, slot.Count);
			if (remaining == 0)
			{
				slot.Clear();
				SyncCurrentContainer();
			}
			else if (remaining < slot.Count)
			{
				slot.Count = remaining;
				SyncCurrentContainer();
			}
		}
		
		private static bool AreItemsCompatible(Item a, Item b)
		{
            return Program.AreItemsStackable(a, b);
		}

        private static Item CloneItemInstance(Item item)
        {
            return new Item(item.Name, item.Count, item.BaseColor, item.Icon, null)
            {
                CustomColors = new List<Color?>(item.CustomColors),
                Container = item.Container,
                Backpack = item.Backpack,
                Metadata = item.Metadata ?? "",
                Meta = item.Meta != null
                    ? new Dictionary<string, string>(item.Meta, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                CampfireCookSeconds = item.CampfireCookSeconds,
                CampfireBurnSeconds = item.CampfireBurnSeconds
            };
        }

        private static bool CanPlaceOnCampfire(Item item)
        {
            if (!GameData.TryGetItemByName(item.Name, out var data)) return false;
            return !item.HasMeta("burned") &&
                (data.Cookable || item.HasMeta("cookStage") || data.Key.StartsWith("cooked_", StringComparison.OrdinalIgnoreCase));
        }

        private static void InitializeCookedStage(Item item)
        {
            if (!item.HasMeta("cookStage") && GameData.TryGetItemByName(item.Name, out var data) &&
                data.Key.StartsWith("cooked_", StringComparison.OrdinalIgnoreCase))
                item.SetMetadataValue("cookStage", "bleu");
        }
        
        private static void TryDropOnContainerSlot(int targetSlot)
		{
			var currentContainer = _currentContainerByPlayer.TryGetValue(0, out var container) ? container : null;
			if (currentContainer == null || _sharedDraggedItem.Item == null) return;
            if (_isCampfireByPlayer.GetValueOrDefault(0) && !CanPlaceOnCampfire(_sharedDraggedItem.Item))
            {
                Program.AddNotification(new Notification("Seules les viandes crues peuvent être cuites ici.", new Color(220, 150, 80, 255), 1.8f));
                return;
            }
			
			var target = currentContainer.Slots[targetSlot];
			
			if (target.IsEmpty)
			{
                target.Item = _isCampfireByPlayer.GetValueOrDefault(0)
                    ? CloneItemInstance(_sharedDraggedItem.Item)
                    : _sharedDraggedItem.Item;
				if (_isCampfireByPlayer.GetValueOrDefault(0))
					InitializeCookedStage(target.Item);
				target.Count = _sharedDraggedItem.Count;
				_sharedDraggedItem.EndDrag();
			}
			else if (target.Item != null && AreItemsCompatible(target.Item, _sharedDraggedItem.Item))
			{
				int maxStack = GameData.ItemDatabase[GetItemId(target.Item.Name)].StackSize;
				int space = maxStack - target.Count;
				if (space > 0)
				{
					int add = Math.Min(space, _sharedDraggedItem.Count);
					Program.MergeSpoilMeta(target.Item, _sharedDraggedItem.Item, add);
					target.Count += add;
					_sharedDraggedItem.Count -= add;
					if (_sharedDraggedItem.Count <= 0)
						_sharedDraggedItem.EndDrag();
				}
			}
			else
			{
				var oldItem = target.Item;
				var oldCount = target.Count;
				
                target.Item = _isCampfireByPlayer.GetValueOrDefault(0)
                    ? CloneItemInstance(_sharedDraggedItem.Item)
                    : _sharedDraggedItem.Item;
				if (_isCampfireByPlayer.GetValueOrDefault(0))
					InitializeCookedStage(target.Item);
				target.Count = _sharedDraggedItem.Count;
				
				// Utiliser l'item complet pour préserver sa couleur
				int remaining = Program.AddItemToInventory(oldItem, oldCount);
				
				if (remaining == 0)
				{
					// Succès : plus rien sous la souris
					_sharedDraggedItem.EndDrag();
				}
				else
				{
					// Échec : inventaire plein, on annule l'opération
					target.Item = oldItem;
					target.Count = oldCount;
					Program.AddNotification(new Notification("Inventaire plein, impossible d'échanger", Color.Red, 1.5f));
					// Le drag n'est pas terminé, l'item reste sous la souris
				}
			}

			SyncCurrentContainer();
		}
        
        private static int GetItemId(string name)
        {
            return GameData.GetItemId(name);
        }

        public static void Draw(int playerIndex = 0, Rectangle? viewport = null)
        {
            if (!IsOpenForPlayer(playerIndex)) return;
            var currentContainer = _currentContainerByPlayer.TryGetValue(playerIndex, out var container) ? container : null;
            if (currentContainer == null) return;
            
            var windowPos = _windowPosByPlayer.ContainsKey(playerIndex) ? _windowPosByPlayer[playerIndex] : UIManager.ScalePosition(200, 150);
            int panelX = (int)windowPos.X;
            int panelY = (int)windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();
            if (viewport.HasValue)
                mousePos -= new Vector2(viewport.Value.X, viewport.Value.Y);
            
            // Dessiner le panneau principal avec texture 9-slice
            if (_useTextures && _panelTexture != null && _panelTexture.IsValid)
            {
                _panelTexture.Draw(new Rectangle(panelX, panelY, _panelWidth, _panelHeight), Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(new Rectangle(panelX + 4, panelY + 4, _panelWidth, _panelHeight), 0.1f, 12, new Color(0, 0, 0, 100));
                Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, _panelWidth, _panelHeight), 0.1f, 12, COLOR_BG);
                Raylib.DrawRectangleRoundedLines(new Rectangle(panelX, panelY, _panelWidth, _panelHeight), 0.1f, 12, 2, COLOR_BORDER);
            }
            
            // Barre de titre
            Color titleColor = _isHoveringTitleBarByPlayer[playerIndex] ? new Color(80, 70, 50, 200) : COLOR_HEADER;
            Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, _panelWidth, HEADER_HEIGHT), 0.1f, 8, titleColor);
            Raylib.DrawRectangleRoundedLines(new Rectangle(panelX, panelY, _panelWidth, HEADER_HEIGHT), 0.1f, 8, 1, COLOR_ACCENT);
            
            // Titre
            bool isCampfire = _isCampfireByPlayer.GetValueOrDefault(playerIndex);
            string containerTitle = isCampfire ? "FEU DE CAMP" : (currentContainer.Rows <= 2 ? "PETIT COFFRE" : "GRAND COFFRE");
            Raylib.DrawText(containerTitle, panelX + UIManager.ScaleInt(15), panelY + UIManager.ScaleInt(13), UIManager.ScaleInt(18), Color.White);
            
            // Bouton fermer
            Rectangle closeBtn = new Rectangle(panelX + _panelWidth - CLOSE_BTN_SIZE - UIManager.ScaleInt(10), panelY + (HEADER_HEIGHT - CLOSE_BTN_SIZE) / 2, CLOSE_BTN_SIZE, CLOSE_BTN_SIZE);
            bool isHoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, isHoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            Raylib.DrawText("X", (int)closeBtn.X + UIManager.ScaleInt(8), (int)closeBtn.Y + UIManager.ScaleInt(6), UIManager.ScaleInt(16), isHoverClose ? Color.Red : new Color(200, 120, 120, 200));
            
            if (isHoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Close(playerIndex);
                return;
            }
            
            // DESSINER LES SLOTS DU CONTENEUR
            int columns = currentContainer.Columns;
            int rows = currentContainer.Rows;
            int totalWidth = columns * SLOT_STEP + (SLOT_SIZE - SLOT_STEP);
            int startX = panelX + (_panelWidth - totalWidth) / 2;
            int startY = panelY + HEADER_HEIGHT + 25;

            int hoveredIndex = -1;
            for (int i = currentContainer.Slots.Count - 1; i >= 0; i--)
            {
                int row = i / columns;
                int col = i % columns;
                if (row >= rows) continue;

                int slotX = startX + col * SLOT_STEP;
                int slotY = startY + row * SLOT_STEP;
                if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(slotX, slotY, SLOT_SIZE, SLOT_SIZE)))
                {
                    hoveredIndex = i;
                    break;
                }
            }

            if (_hoveredSlotByPlayer.GetValueOrDefault(playerIndex) == hoveredIndex)
                _tooltipHoverTimeByPlayer[playerIndex] = _tooltipHoverTimeByPlayer.GetValueOrDefault(playerIndex) + Raylib.GetFrameTime();
            else
            {
                _hoveredSlotByPlayer[playerIndex] = hoveredIndex;
                _tooltipHoverTimeByPlayer[playerIndex] = 0f;
            }
            
            for (int drawPass = 0; drawPass < 2; drawPass++)
            {
                for (int i = 0; i < currentContainer.Slots.Count; i++)
                {
                    int row = i / columns;
                    int col = i % columns;
                    if (row >= rows) break;
                    
                    int slotX = startX + col * SLOT_STEP;
                    int slotY = startY + row * SLOT_STEP;
                    
                    bool isHovered = i == hoveredIndex;
                    if (isHovered != (drawPass == 1))
                        continue;

                    DrawSlot(slotX, slotY, currentContainer.Slots[i], isHovered, isCampfire,
                        i % columns == 0,
                        i + 1 >= currentContainer.Slots.Count || i % columns == columns - 1,
                        i < columns,
                        i + columns >= currentContainer.Slots.Count);
                }
            }
            
            // Indicateur
            string hint = isCampfire ? "Cuisson 60 s  |  Brûlure 60 s" : "Glissez les items entre l'inventaire et le coffre";
            int hintWidth = Raylib.MeasureText(hint, 12);
            Raylib.DrawText(hint, panelX + (_panelWidth - hintWidth) / 2, panelY + _panelHeight - 22, 12, new Color(150, 140, 110, 180));

            if (isCampfire && hoveredIndex >= 0 && _tooltipHoverTimeByPlayer.GetValueOrDefault(playerIndex) >= 0.4f)
            {
                var hoveredSlot = currentContainer.Slots[hoveredIndex];
                if (!hoveredSlot.IsEmpty && hoveredSlot.Item != null)
                    InventoryRenderer.DrawItemTooltip(hoveredSlot.Item, hoveredSlot.Count);
            }
            
            // Raccourci clavier pour fermer
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                Close(playerIndex);
        }
        
        private static void DrawSlot(int x, int y, InventorySlot slot, bool isHovered, bool isCampfire, bool borderLeft, bool borderRight, bool borderUp, bool borderDown)
        {
            bool pressed = isHovered && Raylib.IsMouseButtonDown(MouseButton.Left);
            Color fallbackColor = isHovered ? COLOR_SLOT_HOVER : COLOR_SLOT_NORMAL;
            UIManager.DrawItemSlotBackground(new Rectangle(x, y, SLOT_SIZE, SLOT_SIZE), isHovered, pressed, fallbackColor, borderUp, borderDown, borderLeft, borderRight);
            
            if (!slot.IsEmpty && slot.Item != null)
			{
                ItemRenderer.DrawItemPadded(slot.Item, x + ITEM_OFFSET, y + ITEM_OFFSET, ITEM_SIZE, 6);
				if (slot.Count > 1)
                    Raylib.DrawText(slot.Count.ToString(), x + ITEM_OFFSET + ITEM_SIZE - 18, y + ITEM_OFFSET + ITEM_SIZE - 18, 14, Color.White);

                if (isCampfire && GameData.TryGetItemByName(slot.Item.Name, out var data) &&
                    (data.Cookable || slot.Item.HasMeta("cookStage")))
                {
                    string stage = slot.Item.GetMetadataValue("cookStage");
                    bool isCooking = string.IsNullOrEmpty(stage) && slot.Item.CampfireCookSeconds > 0f;
                    bool isCooked = !string.IsNullOrEmpty(stage);
                    if (isCooking)
                        DrawProgressBar(x - 2, y + SLOT_SIZE + 4, SLOT_SIZE + 4, slot.Item.CampfireCookSeconds / 60f, new Color(238, 157, 55, 255));
                    else if (isCooked)
                    {
                        float burn = stage == "burned" ? 1f : slot.Item.CampfireBurnSeconds / 60f;
                        DrawProgressBar(x - 2, y + SLOT_SIZE + 4, SLOT_SIZE + 4, burn, new Color(205, 65, 45, 255));
                    }
                }
			}
        }

        private static void DrawProgressBar(int x, int y, int width, float amount, Color color)
        {
            const int height = 6;
            Raylib.DrawRectangle(x - 1, y - 1, width + 2, height + 2, new Color(8, 10, 13, 245));
            Raylib.DrawRectangle(x, y, width, height, new Color(42, 45, 50, 255));
            int fillWidth = (int)(width * Math.Clamp(amount, 0f, 1f));
            if (fillWidth > 0)
            {
                Raylib.DrawRectangle(x, y, fillWidth, height, color);
                Raylib.DrawRectangle(x, y, fillWidth, 2, new Color(255, 235, 190, 170));
            }
        }
        
        public static int GetWindowX() => (int)(_windowPosByPlayer.TryGetValue(0, out var pos) ? pos.X : 200);
        public static int GetWindowY() => (int)(_windowPosByPlayer.TryGetValue(0, out var pos) ? pos.Y : 150);
        public static int GetWindowWidth() => _panelWidth;
        public static int GetWindowHeight() => _panelHeight;
    }
}