// GuildUI.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static class GuildUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = UIManager.ScalePosition(260, 100);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isHoveringTitleBar = false;

        // Onglets : "members" (liste des membres) ou "emblem" (blason)
        private static string _activeTab = "members";
        // Dans l'onglet "emblem" : false = aperçu du blason, true = éditeur de peinture pixel-art
        private static bool _emblemEditMode = false;

        // Liste des membres : défilement
        private static float _memberListScroll = 0f;

        // Membre sélectionné pour la vue "équipement" (null = liste des membres)
        private static Guid? _selectedMemberId = null;

        // Définition des emplacements affichés dans la vue équipement d'un PNJ
        private static readonly (string label, EquipmentSlot? slot, BodyZone? zone)[] _memberSlotDefs = new (string, EquipmentSlot?, BodyZone?)[]
        {
            ("Tête", null, BodyZone.TopOfHead),
            ("Visage", null, BodyZone.Face),
            ("Oreilles", null, BodyZone.Ears),
            ("Cou", null, BodyZone.Neck),
            ("Torse", null, BodyZone.Torso),
            ("Taille", null, BodyZone.Waist),
            ("Dos", null, BodyZone.Back),
            ("Jambes", null, BodyZone.Legs),
            ("Pieds", null, BodyZone.Feet),
            ("Main droite", EquipmentSlot.MainHand, null),
            ("Main gauche", EquipmentSlot.OffHand, null),
        };
        private static Dictionary<(EquipmentSlot? slot, BodyZone? zone), Rectangle> _memberEquipSlotRects = new();

        // Tailles de canvas disponibles
        private static readonly int[] _canvasSizes = { 8, 16, 32, 64, 128, 256 };
        private static int _currentCanvasSizeIndex = 2; // Index de 32 dans le tableau
        private static int _canvasSize => _canvasSizes[_currentCanvasSizeIndex];

        // Logo editing
        private static Color[,] _logo = new Color[32, 32];
        private static Color _selectedColor = new Color(255, 0, 0, 255);
        private static Texture2D _logoTexture;
        private static bool _logoTextureDirty = true;

        // Guild name editing
        private static bool _isEditingName = false;
        private static string _editName = "";
        private static float _editBlinkTimer = 0f;

        // Export name editing
        private static bool _isEditingSaveName = false;
        private static string _saveEmblemName = "";

        // Emblem listing UI
        private static bool _showEmblemList = false;
        private static List<string> _emblemList = new();
        private static int _emblemListScroll = 0;

        // Color palette
        private static readonly Color[] _palette = new Color[]
        {
            Color.Black, Color.White, Color.Red, Color.Green, Color.Blue,
            Color.Yellow, Color.Orange, Color.Purple, Color.Gold, Color.Brown,
            Color.Gray, Color.Pink, Color.SkyBlue, Color.Lime, Color.Magenta, Color.Maroon
        };
        private static int _selectedPaletteIndex = 0;

        private static int WINDOW_WIDTH => UIManager.ScaleInt(640);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(620);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(45);
        private static int SUBHEADER_HEIGHT => UIManager.ScaleInt(68);
        private static int TAB_HEIGHT => UIManager.ScaleInt(36);
        private static int CONTENT_TOP => HEADER_HEIGHT + SUBHEADER_HEIGHT + TAB_HEIGHT;
        private static int LOGO_DISPLAY_SIZE => UIManager.ScaleInt(256); // 32*8 = 256 pixels

        // Colors
        private static readonly Color COLOR_BG = new Color(25, 28, 35, 240);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
        private static readonly Color COLOR_SUBHEADER = new Color(30, 32, 40, 255);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_CARD = new Color(38, 41, 50, 235);
        private static readonly Color COLOR_CARD_HOVER = new Color(48, 51, 62, 235);

        public static bool IsOpen => _isOpen;
        public static bool IsTextInputFocused => _isEditingName || _isEditingSaveName;
        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;

        public static void Open()
        {
            if (Program.PlayerGuild == null)
            {
                Program.AddNotification(new Notification("Vous devez créer une guilde avec la charte avant d'accéder au menu.", Color.Red, 2f));
                return;
            }

            _isOpen = true;
            var guild = Program.PlayerGuild;
            _editName = guild.Name;
            _logo = guild.Logo.Clone() as Color[,] ?? new Color[32, 32];
            // Déterminer l'index de la taille actuelle
            int size = _logo.GetLength(0);
            _currentCanvasSizeIndex = Array.IndexOf(_canvasSizes, size);
            if (_currentCanvasSizeIndex < 0) _currentCanvasSizeIndex = 2; // 32 par défaut
            _logoTextureDirty = true;
            _selectedColor = _palette[0];
            _selectedPaletteIndex = 0;
            _showEmblemList = false;
            _isEditingSaveName = false;
            _activeTab = "members";
            _emblemEditMode = false;
            _memberListScroll = 0f;
            _selectedMemberId = null;
        }

        public static void Close()
        {
            _isOpen = false;
            _isEditingName = false;
            _isEditingSaveName = false;
            _showEmblemList = false;
            _isDragging = false;
            _selectedMemberId = null;
        }

        // Résout le PNJ actuellement sélectionné pour la vue équipement (s'il est toujours vivant/membre)
        private static Entity? GetSelectedMember()
        {
            if (_selectedMemberId == null) return null;
            var members = GetLivingMembers();
            return members.FirstOrDefault(m => m.NetId == _selectedMemberId.Value);
        }

        private static void UpdateLogoTexture()
        {
            if (_logoTextureDirty)
            {
                if (_logoTexture.Id != 0)
                    Raylib.UnloadTexture(_logoTexture);

                int size = _logo.GetLength(0);
                Image img = Raylib.GenImageColor(size, size, Color.Blank);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        Raylib.ImageDrawPixel(ref img, x, y, _logo[x, y]);

                _logoTexture = Raylib.LoadTextureFromImage(img);
                Raylib.UnloadImage(img);
                _logoTextureDirty = false;
            }
        }

        private static void ResizeLogo(int newSize)
        {
            Color[,] newLogo = new Color[newSize, newSize];
            int oldSize = _logo.GetLength(0);

            // Copier le contenu existant (centré si agrandissement, tronqué si réduction)
            int offsetX = (newSize - oldSize) / 2;
            int offsetY = (newSize - oldSize) / 2;

            for (int x = 0; x < newSize; x++)
                for (int y = 0; y < newSize; y++)
                {
                    int oldX = x - offsetX;
                    int oldY = y - offsetY;
                    if (oldX >= 0 && oldX < oldSize && oldY >= 0 && oldY < oldSize)
                        newLogo[x, y] = _logo[oldX, oldY];
                    else
                        newLogo[x, y] = Color.Blank;
                }

            _logo = newLogo;
            _logoTextureDirty = true;

            // Mettre à jour le logo dans la guilde
            if (Program.PlayerGuild != null)
                Program.PlayerGuild.Logo = _logo;
        }

        // Résout la liste des PNJ membres de la guilde encore vivants, à partir de leurs NetId.
        private static List<Entity> GetLivingMembers()
        {
            var guild = Program.PlayerGuild;
            if (guild == null) return new List<Entity>();

            var all = Program.GetEntities();
            var result = new List<Entity>();
            foreach (var id in guild.MemberIds)
            {
                var npc = QuestManager.FindEntityByNetId(all, id);
                if (npc != null && npc.IsAlive)
                    result.Add(npc);
            }
            return result;
        }

        public static void Update()
        {
            if (!_isOpen) return;

            Vector2 mousePos = Raylib.GetMousePosition();
            Rectangle titleBar = new Rectangle(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, HEADER_HEIGHT);
            _isHoveringTitleBar = Raylib.CheckCollisionPointRec(mousePos, titleBar);

            if (_isHoveringTitleBar && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDragging = true;
                _dragOffset = mousePos - _windowPos;
            }
            if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                _windowPos = mousePos - _dragOffset;
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDragging = false;

            int visibleMargin = UIManager.ScaleInt(100);
            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + visibleMargin, Raylib.GetScreenWidth() - visibleMargin);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, Raylib.GetScreenHeight() - visibleMargin);

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;

            // ----- Fermeture -----
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 35, y + 10, 25, 25);
            if (Raylib.CheckCollisionPointRec(mousePos, closeBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Close();
                return;
            }

            // ----- Édition du nom du fichier d'export -----
            if (_isEditingSaveName)
            {
                _editBlinkTimer += Raylib.GetFrameTime();
                Rectangle exportInputRect = new Rectangle(x + 50, y + WINDOW_HEIGHT / 2 - 25, WINDOW_WIDTH - 100, 50);
                TextInput.Update(ref _saveEmblemName, "guild-export-name", exportInputRect, 30);
                if (Raylib.IsKeyPressed(KeyboardKey.Enter))
                {
                    if (!string.IsNullOrWhiteSpace(_saveEmblemName))
                    {
                        GuildStorage.SaveEmblem(_saveEmblemName.Trim(), _logo);
                        Program.AddNotification(new Notification($"Blason exporté : {_saveEmblemName.Trim()}", Color.Green, 2f));
                    }
                    _isEditingSaveName = false;
                    TextInput.Reset("guild-export-name");
                }
                else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    _isEditingSaveName = false;
                    TextInput.Reset("guild-export-name");
                }
                return; // Ne pas traiter les autres inputs pendant l'édition
            }

            // ----- Édition du nom de la guilde -----
            if (_isEditingName)
            {
                _editBlinkTimer += Raylib.GetFrameTime();
                Rectangle guildNameRect = new Rectangle(x + 80, y + HEADER_HEIGHT + 10, WINDOW_WIDTH - 130, 36);
                TextInput.Update(ref _editName, "guild-name", guildNameRect, 20);
                if (Raylib.IsKeyPressed(KeyboardKey.Enter))
                {
                    if (!string.IsNullOrWhiteSpace(_editName) && Program.PlayerGuild != null)
                        Program.PlayerGuild.Name = _editName.Trim();
                    _isEditingName = false;
                    TextInput.Reset("guild-name");
                }
                else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    _editName = Program.PlayerGuild?.Name ?? "";
                    _isEditingName = false;
                    TextInput.Reset("guild-name");
                }
                return; // Ne pas traiter les clics pendant l'édition du nom
            }

            // ----- Sous-en-tête : crayon d'édition du nom -----
            Rectangle editNameBtn = new Rectangle(x + WINDOW_WIDTH - 44, y + HEADER_HEIGHT + 12, 26, 26);
            if (Raylib.CheckCollisionPointRec(mousePos, editNameBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isEditingName = true;
                _editName = Program.PlayerGuild?.Name ?? "";
                return;
            }

            // ----- Barre d'onglets -----
            int tabY = y + HEADER_HEIGHT + SUBHEADER_HEIGHT;
            int tabW = WINDOW_WIDTH / 2;
            Rectangle membersTabRect = new Rectangle(x, tabY, tabW, TAB_HEIGHT);
            Rectangle emblemTabRect = new Rectangle(x + tabW, tabY, WINDOW_WIDTH - tabW, TAB_HEIGHT);
            if (Raylib.CheckCollisionPointRec(mousePos, membersTabRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _activeTab = "members";
                return;
            }
            if (Raylib.CheckCollisionPointRec(mousePos, emblemTabRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _activeTab = "emblem";
                return;
            }

            if (_activeTab == "members")
            {
                UpdateMembersTab(mousePos, x, y);
            }
            else
            {
                UpdateEmblemTab(mousePos, x, y);
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                if (_showEmblemList)
                    _showEmblemList = false;
                else if (_isEditingSaveName)
                    _isEditingSaveName = false;
                else if (_isEditingName)
                    _isEditingName = false;
                else if (_emblemEditMode)
                    _emblemEditMode = false;
                else if (_selectedMemberId != null)
                    _selectedMemberId = null;
                else
                    Close();   // fermeture directe
            }
        }

        private static void UpdateMembersTab(Vector2 mousePos, int x, int y)
        {
            var selected = GetSelectedMember();
            if (_selectedMemberId != null)
            {
                if (selected == null)
                {
                    // Le PNJ n'est plus disponible (mort, quitté la guilde...) : retour à la liste.
                    _selectedMemberId = null;
                }
                else
                {
                    UpdateMemberEquipmentPanel(selected, mousePos, x, y);
                    return;
                }
            }

            int listX = x + 20;
            int listY = y + CONTENT_TOP + 15;
            int listW = WINDOW_WIDTH - 40;
            int listH = WINDOW_HEIGHT - CONTENT_TOP - 30;

            var members = GetLivingMembers();
            const int cardH = 78;
            const int cardGap = 10;
            float contentHeight = members.Count * (cardH + cardGap);

            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0 && Raylib.CheckCollisionPointRec(mousePos, new Rectangle(listX, listY, listW, listH)))
            {
                _memberListScroll -= wheel * 40f;
                _memberListScroll = Math.Clamp(_memberListScroll, 0, Math.Max(0, contentHeight - listH));
            }

            // Clic sur une carte -> ouvrir la vue équipement de ce membre
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, new Rectangle(listX, listY, listW, listH)))
            {
                for (int i = 0; i < members.Count; i++)
                {
                    float cardY = listY + i * (cardH + cardGap) - _memberListScroll;
                    if (cardY + cardH < listY || cardY > listY + listH) continue;
                    Rectangle cardRect = new Rectangle(listX, cardY, listW, cardH);
                    if (Raylib.CheckCollisionPointRec(mousePos, cardRect))
                    {
                        _selectedMemberId = members[i].NetId;
                        break;
                    }
                }
            }
        }

        // ==================== ÉQUIPEMENT D'UN MEMBRE ====================

        private static void BuildMemberEquipSlotRects(int x, int y)
        {
            _memberEquipSlotRects.Clear();
            int gridTop = y + CONTENT_TOP + 130;
            int gridX = x + 40;
            const int slotSize = 56;
            const int cols = 4;
            const int cellW = 135;
            const int cellH = 100;

            for (int i = 0; i < _memberSlotDefs.Length; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int px = gridX + col * cellW;
                int py = gridTop + row * cellH;
                var def = _memberSlotDefs[i];
                _memberEquipSlotRects[(def.slot, def.zone)] = new Rectangle(px, py, slotSize, slotSize);
            }
        }

        private static void UpdateMemberEquipmentPanel(Entity npc, Vector2 mousePos, int x, int y)
        {
            Rectangle backBtn = new Rectangle(x + 20, y + CONTENT_TOP + 8, 90, 28);
            if (Raylib.CheckCollisionPointRec(mousePos, backBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _selectedMemberId = null;
                return;
            }

            var eq = npc.Equipment;
            if (eq == null) return;

            BuildMemberEquipSlotRects(x, y);

            if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

            foreach (var kv in _memberEquipSlotRects)
            {
                if (!Raylib.CheckCollisionPointRec(mousePos, kv.Value)) continue;

                var (slot, zone) = kv.Key;
                if (zone != null)
                    HandleMemberZoneClick(zone.Value, eq, mousePos);
                else if (slot != null)
                    HandleMemberEquipSlotClick(slot.Value, eq, mousePos);
                break;
            }
        }

        // Équiper/déséquiper un item couvrant une zone du corps sur l'équipement d'un PNJ
        private static void HandleMemberZoneClick(BodyZone zone, Equipment eq, Vector2 mp)
        {
            var dragged = InventoryRenderer.DraggedItem;
            if (dragged.IsDragging && dragged.Item != null)
            {
                int itemId = Program.GetItemId(dragged.Item.Name);
                if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData) || !itemData.CoveredZones.Contains(zone))
                    return;

                var draggedItem = dragged.Item;
                bool success = eq.EquipOnBody(draggedItem, out var unequipped);
                if (success)
                {
                    dragged.EndDrag();
                    foreach (var displaced in unequipped)
                        Program.AddItemToInventory(displaced, 1);
                    eq.LoadEquipmentTextures();
                    Program.PlayEquipSound();
                }
            }
            else
            {
                Item? equippedItem = eq.GetZoneItem(zone);
                if (equippedItem != null)
                {
                    eq.UnequipFromBody(equippedItem);
                    dragged.StartDrag(equippedItem, equippedItem.Count > 0 ? equippedItem.Count : 1, mp);
                    eq.LoadEquipmentTextures();
                }
            }
        }

        // Équiper/déséquiper un item d'un emplacement fonctionnel (main droite/gauche) sur l'équipement d'un PNJ
        private static void HandleMemberEquipSlotClick(EquipmentSlot slot, Equipment eq, Vector2 mp)
        {
            var dragged = InventoryRenderer.DraggedItem;
            if (dragged.IsDragging && dragged.Item != null)
            {
                int itemId = Program.GetItemId(dragged.Item.Name);
                if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData) || !itemData.HasEquipSlot || itemData.EquipSlot != slot)
                    return;

                Item? oldItem = eq.GetItemInSlot(slot, 0);
                if (oldItem != null)
                    eq.UnEquipItem(slot, 0);

                int currentCount = eq.GetEquipCount(slot);
                if (currentCount >= itemData.EquipMax)
                {
                    if (oldItem != null) eq.EquipItem(oldItem, slot, 0);
                    return;
                }

                bool success = eq.EquipItem(dragged.Item, slot, 0);
                if (success)
                {
                    if (oldItem != null)
                        Program.AddItemToInventory(oldItem, 1);
                    dragged.EndDrag();
                    eq.LoadEquipmentTextures();
                    Program.PlayEquipSound();
                }
                else if (oldItem != null)
                {
                    eq.EquipItem(oldItem, slot, 0);
                }
            }
            else
            {
                Item? equippedItem = eq.GetItemInSlot(slot, 0);
                if (equippedItem != null)
                {
                    eq.UnEquipItem(slot, 0);
                    dragged.StartDrag(equippedItem, equippedItem.Count > 0 ? equippedItem.Count : 1, mp);
                    eq.LoadEquipmentTextures();
                }
            }
        }

        private static void UpdateEmblemTab(Vector2 mousePos, int x, int y)
        {
            if (!_emblemEditMode)
            {
                // ----- Aperçu du blason -----
                Rectangle customizeBtn = new Rectangle(x + WINDOW_WIDTH / 2f - 140, y + WINDOW_HEIGHT - 80, 280, 44);
                if (Raylib.CheckCollisionPointRec(mousePos, customizeBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _emblemEditMode = true;
                }
                return;
            }

            // ----- Retour à l'aperçu -----
            Rectangle backBtn = new Rectangle(x + 20, y + CONTENT_TOP + 8, 90, 28);
            if (Raylib.CheckCollisionPointRec(mousePos, backBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _emblemEditMode = false;
                return;
            }

            int editorTop = y + CONTENT_TOP + 44;

            // Logo painting
            int logoDisplayX = x + 20;
            int logoDisplayY = editorTop;
            int cellSize = LOGO_DISPLAY_SIZE / _canvasSize;

            Rectangle logoRect = new Rectangle(logoDisplayX, logoDisplayY, LOGO_DISPLAY_SIZE, LOGO_DISPLAY_SIZE);
            if (Raylib.CheckCollisionPointRec(mousePos, logoRect))
            {
                int col = (int)((mousePos.X - logoDisplayX) / cellSize);
                int row = (int)((mousePos.Y - logoDisplayY) / cellSize);
                col = Math.Clamp(col, 0, _canvasSize - 1);
                row = Math.Clamp(row, 0, _canvasSize - 1);

                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    _logo[col, row] = _selectedColor;
                    _logoTextureDirty = true;
                }
                else if (Raylib.IsMouseButtonDown(MouseButton.Right))
                {
                    _logo[col, row] = Color.Blank;
                    _logoTextureDirty = true;
                }
            }

            // Color palette
            int paletteX = x + 20 + LOGO_DISPLAY_SIZE + 20;
            int paletteY = editorTop;
            int paletteCellSize = 24;
            for (int i = 0; i < _palette.Length; i++)
            {
                int row = i / 2;
                int col = i % 2;
                int px = paletteX + col * (paletteCellSize + 2);
                int py = paletteY + row * (paletteCellSize + 2);
                Rectangle btnRect = new Rectangle(px, py, paletteCellSize, paletteCellSize);
                if (Raylib.CheckCollisionPointRec(mousePos, btnRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _selectedColor = _palette[i];
                    _selectedPaletteIndex = i;
                }
            }

            // Canvas size buttons
            int sizeBtnX = paletteX;
            int sizeBtnY = paletteY + _palette.Length / 2 * (paletteCellSize + 2) + 10;
            int sizeBtnH = 22;

            Rectangle minusBtn = new Rectangle(sizeBtnX, sizeBtnY, sizeBtnH, sizeBtnH);
            if (Raylib.CheckCollisionPointRec(mousePos, minusBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _currentCanvasSizeIndex = Math.Max(0, _currentCanvasSizeIndex - 1);
                ResizeLogo(_canvasSize);
            }

            Rectangle plusBtn = new Rectangle(sizeBtnX + sizeBtnH + 8, sizeBtnY, sizeBtnH, sizeBtnH);
            if (Raylib.CheckCollisionPointRec(mousePos, plusBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _currentCanvasSizeIndex = Math.Min(_canvasSizes.Length - 1, _currentCanvasSizeIndex + 1);
                ResizeLogo(_canvasSize);
            }

            // Save button
            Rectangle saveBtn = new Rectangle(x + WINDOW_WIDTH - 140, y + WINDOW_HEIGHT - 130, 120, 35);
            if (Raylib.CheckCollisionPointRec(mousePos, saveBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if (Program.PlayerGuild != null)
                {
                    Program.PlayerGuild.Logo = _logo;
                    Program.PlayerGuild.Name = _editName;
                    Program.AddNotification(new Notification("Blason sauvegardé !", Color.Green, 2f));
                    _logoTextureDirty = true;
                }
            }

            // Export emblem button
            Rectangle exportBtn = new Rectangle(x + WINDOW_WIDTH - 140, y + WINDOW_HEIGHT - 90, 120, 35);
            if (Raylib.CheckCollisionPointRec(mousePos, exportBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _saveEmblemName = Program.PlayerGuild?.Name ?? "blason";
                _isEditingSaveName = true;
            }

            // Browse emblems button
            Rectangle browseBtn = new Rectangle(x + 20, y + WINDOW_HEIGHT - 90, 120, 35);
            if (Raylib.CheckCollisionPointRec(mousePos, browseBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _emblemList = GuildStorage.GetAvailableEmblems();
                _showEmblemList = !_showEmblemList;
                _emblemListScroll = 0;
            }

            // Emblem list popup interactions
            if (_showEmblemList)
            {
                int popupW = 250;
                int popupH = 300;
                int popupX = (int)_windowPos.X - popupW - 10;
                int popupY = (int)_windowPos.Y + HEADER_HEIGHT + 40;

                if (popupX < 10) popupX = (int)_windowPos.X + WINDOW_WIDTH + 10;
                popupX = Math.Clamp(popupX, 10, Raylib.GetScreenWidth() - popupW - 10);
                popupY = Math.Clamp(popupY, 10, Raylib.GetScreenHeight() - popupH - 10);

                Rectangle closePopBtn = new Rectangle(popupX + popupW - 25, popupY + 5, 20, 20);
                if (Raylib.CheckCollisionPointRec(mousePos, closePopBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _showEmblemList = false;
                }

                int listY = popupY + 30;
                int listH = popupH - 40;
                int itemH = 24;
                float wheel = Raylib.GetMouseWheelMove();
                if (wheel != 0 && Raylib.CheckCollisionPointRec(mousePos, new Rectangle(popupX, listY, popupW, listH)))
                {
                    _emblemListScroll -= (int)wheel * itemH;
                    _emblemListScroll = Math.Clamp(_emblemListScroll, 0, Math.Max(0, _emblemList.Count * itemH - listH));
                }

                int visibleItems = listH / itemH;
                int firstVisible = _emblemListScroll / itemH;
                int lastVisible = Math.Min(_emblemList.Count, firstVisible + visibleItems + 1);

                for (int i = firstVisible; i < lastVisible; i++)
                {
                    int itemY = listY + i * itemH - _emblemListScroll;
                    Rectangle itemRect = new Rectangle(popupX + 10, itemY, popupW - 20, itemH - 2);

                    if (Raylib.CheckCollisionPointRec(mousePos, itemRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        var loadedLogo = GuildStorage.LoadEmblem(_emblemList[i]);
                        if (loadedLogo != null)
                        {
                            _logo = loadedLogo;
                            _logoTextureDirty = true;
                            int size = _logo.GetLength(0);
                            _currentCanvasSizeIndex = Array.IndexOf(_canvasSizes, size);
                            if (_currentCanvasSizeIndex < 0) _currentCanvasSizeIndex = 2;
                            if (Program.PlayerGuild != null)
                                Program.PlayerGuild.Logo = _logo;
                            Program.AddNotification(new Notification($"Blason {_emblemList[i]} chargé !", Color.Green, 2f));
                            _showEmblemList = false;
                        }
                    }
                }
            }
        }

        public static void Draw()
        {
            if (!_isOpen) return;

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();

            UpdateLogoTexture();

            // ----- Panneau -----
            Raylib.DrawRectangleRounded(new Rectangle(x + 4, y + 4, WINDOW_WIDTH, WINDOW_HEIGHT), 0.08f, 12, new Color(0, 0, 0, 110));
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.08f, 12, COLOR_BG);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.08f, 12, 2, COLOR_BORDER);

            DrawHeader(x, y, mousePos);
            DrawSubheader(x, y, mousePos);
            DrawTabs(x, y, mousePos);

            // ----- Zone de contenu -----
            Rectangle contentClip = new Rectangle(x + 2, y + CONTENT_TOP, WINDOW_WIDTH - 4, WINDOW_HEIGHT - CONTENT_TOP - 2);
            Raylib.BeginScissorMode((int)contentClip.X, (int)contentClip.Y, (int)contentClip.Width, (int)contentClip.Height);
            if (_activeTab == "members")
                DrawMembersTab(x, y, mousePos);
            else
                DrawEmblemTab(x, y, mousePos);
            Raylib.EndScissorMode();

            // Popup de sélection de blason (par-dessus tout, en dehors du clip)
            if (_activeTab == "emblem" && _emblemEditMode && _showEmblemList)
            {
                DrawEmblemListPopup(mousePos);
            }

            // Dialogue d'export
            if (_isEditingSaveName)
            {
                Rectangle exportDlg = new Rectangle(x + 50, y + WINDOW_HEIGHT / 2 - 25, WINDOW_WIDTH - 100, 50);
                Raylib.DrawRectangleRounded(exportDlg, 0.2f, 8, new Color(20, 22, 28, 250));
                Raylib.DrawRectangleRoundedLines(exportDlg, 0.2f, 8, 1, COLOR_ACCENT);

                FontManager.DrawText(Localization.Get("guild.export.filename"), (int)exportDlg.X + 10, (int)exportDlg.Y + 8, 12, COLOR_ACCENT);
                Rectangle exportInputRect = new Rectangle(exportDlg.X + 10, exportDlg.Y + 22, exportDlg.Width - 20, 24);
                TextInput.DrawSingleLine(_saveEmblemName, "guild-export-name", exportInputRect, 16, Color.White,
                    new Color(90, 110, 150, 220), COLOR_ACCENT, 0);
            }
        }

        private static void DrawHeader(int x, int y, Vector2 mousePos)
        {
            Color titleColor = _isHoveringTitleBar ? new Color(80, 70, 50, 200) : COLOR_HEADER;
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, titleColor);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, 1, COLOR_ACCENT);
            FontManager.DrawText(Localization.Get("guild.title"), x + 15, y + 13, 18, Color.White);

            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 35, y + 10, 25, 25);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeBtn.X + 8, (int)closeBtn.Y + 5, 16, hoverClose ? Color.Red : new Color(200, 120, 120, 200));
        }

        private static void DrawSubheader(int x, int y, Vector2 mousePos)
        {
            var guild = Program.PlayerGuild;
            Rectangle subRect = new Rectangle(x, y + HEADER_HEIGHT, WINDOW_WIDTH, SUBHEADER_HEIGHT);
            Raylib.DrawRectangleRec(subRect, COLOR_SUBHEADER);
            Raylib.DrawLine(x, (int)(subRect.Y + subRect.Height), x + WINDOW_WIDTH, (int)(subRect.Y + subRect.Height), COLOR_BORDER);

            // Petit blason à gauche, dans un médaillon
            int thumbSize = 48;
            int thumbX = x + 16;
            int thumbY = (int)subRect.Y + (SUBHEADER_HEIGHT - thumbSize) / 2;
            Raylib.DrawRectangleRounded(new Rectangle(thumbX - 3, thumbY - 3, thumbSize + 6, thumbSize + 6), 0.25f, 8, new Color(15, 16, 20, 255));
            Raylib.DrawRectangleRoundedLines(new Rectangle(thumbX - 3, thumbY - 3, thumbSize + 6, thumbSize + 6), 0.25f, 8, 2, COLOR_ACCENT);
            if (_logoTexture.Id != 0)
            {
                Rectangle dest = new Rectangle(thumbX, thumbY, thumbSize, thumbSize);
                Raylib.DrawTexturePro(_logoTexture, new Rectangle(0, 0, _logoTexture.Width, _logoTexture.Height), dest, Vector2.Zero, 0, Color.White);
            }

            // Nom + effectif à droite du médaillon
            int textX = thumbX + thumbSize + 16;
            int textTop = (int)subRect.Y + 10;

            if (_isEditingName)
            {
                Rectangle nameInputRect = new Rectangle(textX, textTop - 4, WINDOW_WIDTH - (textX - x) - 60, 32);
                TextInput.DrawSingleLine(_editName, "guild-name", nameInputRect, 20, Color.White,
                    new Color(90, 110, 150, 220), COLOR_ACCENT, 0);
            }
            else
            {
                FontManager.DrawText(guild?.Name ?? Localization.Get("guild.default_name"), textX, textTop, 20, Color.White);

                Rectangle editRect = new Rectangle(x + WINDOW_WIDTH - 44, y + HEADER_HEIGHT + 12, 26, 26);
                bool hoverEdit = Raylib.CheckCollisionPointRec(mousePos, editRect);
                Raylib.DrawRectangleRounded(editRect, 0.2f, 6, hoverEdit ? new Color(80, 70, 50, 200) : new Color(50, 45, 35, 150));
                FontManager.DrawText("", (int)editRect.X + 5, (int)editRect.Y + 3, 16, Color.White);
            }

            int memberCount = guild?.MemberIds.Count ?? 0;
            string memberLabel = memberCount <= 1 ? Localization.Get("guild.member_count_singular", memberCount) : Localization.Get("guild.member_count_plural", memberCount);
            FontManager.DrawText(memberLabel, textX, textTop + 28, 13, new Color(180, 170, 150, 220));
        }

        private static void DrawTabs(int x, int y, Vector2 mousePos)
        {
            int tabY = y + HEADER_HEIGHT + SUBHEADER_HEIGHT;
            int tabW = WINDOW_WIDTH / 2;
            Rectangle membersTabRect = new Rectangle(x, tabY, tabW, TAB_HEIGHT);
            Rectangle emblemTabRect = new Rectangle(x + tabW, tabY, WINDOW_WIDTH - tabW, TAB_HEIGHT);

            DrawTabButton(membersTabRect, Localization.Get("guild.tabs.members"), _activeTab == "members", mousePos);
            DrawTabButton(emblemTabRect, Localization.Get("guild.tabs.emblem"), _activeTab == "emblem", mousePos);

            Raylib.DrawLine(x, tabY + TAB_HEIGHT, x + WINDOW_WIDTH, tabY + TAB_HEIGHT, COLOR_BORDER);
        }

        private static void DrawTabButton(Rectangle rect, string label, bool active, Vector2 mousePos)
        {
            bool hover = Raylib.CheckCollisionPointRec(mousePos, rect);
            Color bg = active ? new Color(45, 40, 30, 255) : (hover ? new Color(38, 40, 48, 255) : COLOR_SUBHEADER);
            Raylib.DrawRectangleRec(rect, bg);

            int textW = FontManager.MeasureText(label, 15);
            FontManager.DrawText(label, (int)(rect.X + (rect.Width - textW) / 2), (int)(rect.Y + 9), 15, active ? COLOR_ACCENT : new Color(190, 185, 175, 200));

            if (active)
                Raylib.DrawRectangle((int)rect.X, (int)(rect.Y + rect.Height - 3), (int)rect.Width, 3, COLOR_ACCENT);
        }

        // ==================== ONGLET MEMBRES ====================

        private static void DrawMembersTab(int x, int y, Vector2 mousePos)
        {
            var selectedMember = GetSelectedMember();
            if (_selectedMemberId != null && selectedMember != null)
            {
                DrawMemberEquipmentPanel(x, y, mousePos, selectedMember);
                return;
            }

            var members = GetLivingMembers();

            int listX = x + 20;
            int listY = y + CONTENT_TOP + 15;
            int listW = WINDOW_WIDTH - 40;
            int listH = WINDOW_HEIGHT - CONTENT_TOP - 30;

            if (members.Count == 0)
            {
                string msg = Localization.Get("guild.members.empty_title");
                string sub = Localization.Get("guild.members.empty_subtitle");
                string sub2 = Localization.Get("guild.members.empty_hint");
                int mw = FontManager.MeasureText(msg, 16);
                int sw = FontManager.MeasureText(sub, 13);
                int s2w = FontManager.MeasureText(sub2, 13);
                FontManager.DrawText(msg, x + (WINDOW_WIDTH - mw) / 2, listY + 60, 16, new Color(200, 190, 170, 220));
                FontManager.DrawText(sub, x + (WINDOW_WIDTH - sw) / 2, listY + 90, 13, new Color(150, 140, 120, 180));
                FontManager.DrawText(sub2, x + (WINDOW_WIDTH - s2w) / 2, listY + 108, 13, new Color(150, 140, 120, 180));
                return;
            }

            const int cardH = 78;
            const int cardGap = 10;
            float scrollY = _memberListScroll;

            Raylib.BeginScissorMode(listX, listY, listW, listH);
            for (int i = 0; i < members.Count; i++)
            {
                var npc = members[i];
                float cardY = listY + i * (cardH + cardGap) - scrollY;
                if (cardY + cardH < listY || cardY > listY + listH) continue; // hors-vue

                Rectangle cardRect = new Rectangle(listX, cardY, listW, cardH);
                bool hoverCard = Raylib.CheckCollisionPointRec(mousePos, cardRect);
                Raylib.DrawRectangleRounded(cardRect, 0.15f, 8, hoverCard ? COLOR_CARD_HOVER : COLOR_CARD);
                Raylib.DrawRectangleRoundedLines(cardRect, 0.15f, 8, 1, new Color(70, 62, 45, 160));

                // Portrait
                Rectangle portraitRect = new Rectangle(cardRect.X + 8, cardRect.Y + 7, cardH - 14, cardH - 14);
                Raylib.DrawRectangleRounded(portraitRect, 0.2f, 6, new Color(15, 16, 20, 255));
                QuestJournalUI.DrawSharedPortrait(npc, portraitRect);
                Raylib.DrawRectangleRoundedLines(portraitRect, 0.2f, 6, 1, new Color(90, 80, 60, 180));

                // Textes
                int textX = (int)(portraitRect.X + portraitRect.Width + 16);
                int textY = (int)cardRect.Y + 14;
                string name = npc.DisplayName ?? Localization.Get("guild.member.unnamed");
                FontManager.DrawText(name, textX, textY, 17, Color.White);

                string subtitle = npc.HasProfession ? npc.GetProfessionDisplayName() : Localization.Get("guild.member.default_role");
                FontManager.DrawText(subtitle, textX, textY + 24, 13, new Color(190, 180, 160, 200));

                // Badge "membre de la guilde"
                string badge = Localization.Get("guild.badge.member");
                int badgeW = FontManager.MeasureText(badge, 12);
                Rectangle badgeRect = new Rectangle(cardRect.X + cardRect.Width - badgeW - 26, cardRect.Y + cardRect.Height - 30, badgeW + 16, 22);
                Raylib.DrawRectangleRounded(badgeRect, 0.4f, 6, new Color(70, 55, 110, 180));
                FontManager.DrawText(badge, (int)badgeRect.X + 8, (int)badgeRect.Y + 4, 12, new Color(210, 200, 255, 255));

                if (hoverCard)
                {
                    string equipHint = "Cliquer pour gérer l'équipement";
                    int hintW = FontManager.MeasureText(equipHint, 11);
                    FontManager.DrawText(equipHint, (int)(cardRect.X + cardRect.Width - hintW - 14), (int)cardRect.Y + 8, 11, new Color(200, 190, 160, 200));
                }
            }
            Raylib.EndScissorMode();

            // Barre de défilement simplifiée
            float contentHeight = members.Count * (cardH + cardGap);
            if (contentHeight > listH)
            {
                float trackH = listH;
                float thumbH = Math.Max(30f, trackH * (listH / contentHeight));
                float thumbY = listY + (scrollY / Math.Max(1f, contentHeight - listH)) * (trackH - thumbH);
                Raylib.DrawRectangleRounded(new Rectangle(listX + listW - 6, listY, 4, trackH), 0.5f, 4, new Color(255, 255, 255, 20));
                Raylib.DrawRectangleRounded(new Rectangle(listX + listW - 6, thumbY, 4, thumbH), 0.5f, 4, COLOR_ACCENT);
            }
        }

        private static void DrawMemberEquipmentPanel(int x, int y, Vector2 mousePos, Entity npc)
        {
            // Bouton retour
            Rectangle backBtn = new Rectangle(x + 20, y + CONTENT_TOP + 8, 90, 28);
            bool hoverBack = Raylib.CheckCollisionPointRec(mousePos, backBtn);
            Raylib.DrawRectangleRounded(backBtn, 0.25f, 6, hoverBack ? new Color(80, 70, 50, 220) : new Color(55, 48, 38, 180));
            FontManager.DrawText(Localization.Get("guild.emblem.back"), (int)backBtn.X + 10, (int)backBtn.Y + 6, 13, Color.White);

            // En-tête : portrait + nom du PNJ
            int headerY = y + CONTENT_TOP + 44;
            Rectangle portraitRect = new Rectangle(x + 20, headerY, 56, 56);
            Raylib.DrawRectangleRounded(portraitRect, 0.2f, 6, new Color(15, 16, 20, 255));
            QuestJournalUI.DrawSharedPortrait(npc, portraitRect);
            Raylib.DrawRectangleRoundedLines(portraitRect, 0.2f, 6, 1, new Color(90, 80, 60, 180));

            string name = npc.DisplayName ?? Localization.Get("guild.member.unnamed");
            FontManager.DrawText(name, x + 88, headerY + 4, 18, Color.White);
            FontManager.DrawText("Faites glisser un objet depuis votre inventaire pour l'équiper.", x + 88, headerY + 30, 12, new Color(160, 150, 130, 200));

            var eq = npc.Equipment;
            if (eq == null)
            {
                FontManager.DrawText("Ce PNJ ne peut pas être équipé.", x + 20, headerY + 90, 14, new Color(180, 170, 150, 200));
                return;
            }

            BuildMemberEquipSlotRects(x, y);

            foreach (var def in _memberSlotDefs)
            {
                var key = (def.slot, def.zone);
                if (!_memberEquipSlotRects.TryGetValue(key, out Rectangle rect)) continue;
                bool hover = Raylib.CheckCollisionPointRec(mousePos, rect);

                Item? equippedItem = def.zone != null ? eq.GetZoneItem(def.zone.Value) : eq.GetItemInSlot(def.slot!.Value, 0);
                bool isEquipped = equippedItem != null;

                Color bg = isEquipped ? new Color(210, 180, 100, 80) : (hover ? new Color(70, 73, 80, 255) : new Color(50, 53, 60, 255));
                Raylib.DrawRectangleRounded(rect, 0.15f, 6, bg);
                Raylib.DrawRectangleRoundedLines(rect, 0.15f, 6, 1, hover ? COLOR_ACCENT : new Color(65, 68, 75, 255));

                if (isEquipped && equippedItem != null)
                {
                    ItemRenderer.DrawItemPadded(equippedItem, (int)rect.X, (int)rect.Y, (int)rect.Width, 6);
                }

                int labelW = FontManager.MeasureText(def.label, 11);
                FontManager.DrawText(def.label, (int)(rect.X + (rect.Width - labelW) / 2), (int)(rect.Y + rect.Height + 4), 11, new Color(170, 160, 140, 200));
            }

            // Filet de sécurité : afficher l'item en train d'être glissé même si l'inventaire du
            // joueur n'est pas ouvert à l'écran (sinon l'objet suivrait la souris invisiblement).
            var dragged = InventoryRenderer.DraggedItem;
            if (dragged.IsDragging && dragged.Item != null && !InventoryRenderer.IsInventoryOpen)
            {
                const int dragSize = 48;
                int drawX = (int)mousePos.X - dragSize / 2;
                int drawY = (int)mousePos.Y - dragSize / 2;
                ItemRenderer.DrawItemPadded(dragged.Item, drawX, drawY, dragSize, 4, new Color(255, 255, 255, 200));
                if (dragged.Count > 1)
                    FontManager.DrawText(dragged.Count.ToString(), (int)mousePos.X + 15, (int)mousePos.Y + 15, 16, Color.White);
            }
        }

        // ==================== ONGLET BLASON ====================

        private static void DrawEmblemTab(int x, int y, Vector2 mousePos)
        {
            if (!_emblemEditMode)
            {
                DrawEmblemPreview(x, y, mousePos);
            }
            else
            {
                DrawEmblemEditor(x, y, mousePos);
            }
        }

        private static void DrawEmblemPreview(int x, int y, Vector2 mousePos)
        {
            var guild = Program.PlayerGuild;

            int frameSize = 300;
            int frameX = x + (WINDOW_WIDTH - frameSize) / 2;
            int frameY = y + CONTENT_TOP + 25;

            // Cadre décoratif façon blason
            Raylib.DrawRectangleRounded(new Rectangle(frameX - 10, frameY - 10, frameSize + 20, frameSize + 20), 0.08f, 10, new Color(15, 16, 20, 255));
            Raylib.DrawRectangleRoundedLines(new Rectangle(frameX - 10, frameY - 10, frameSize + 20, frameSize + 20), 0.08f, 10, 3, COLOR_ACCENT);
            Raylib.DrawRectangleRoundedLines(new Rectangle(frameX - 4, frameY - 4, frameSize + 8, frameSize + 8), 0.08f, 10, 1, new Color(140, 120, 70, 160));

            if (_logoTexture.Id != 0)
            {
                Rectangle dest = new Rectangle(frameX, frameY, frameSize, frameSize);
                Raylib.DrawTexturePro(_logoTexture, new Rectangle(0, 0, _logoTexture.Width, _logoTexture.Height), dest, Vector2.Zero, 0, Color.White);
            }

            string name = guild?.Name ?? Localization.Get("guild.default_name");
            int nameW = FontManager.MeasureText(name, 22);
            FontManager.DrawText(name, x + (WINDOW_WIDTH - nameW) / 2, frameY + frameSize + 24, 22, COLOR_ACCENT);

            int memberCount = guild?.MemberIds.Count ?? 0;
            string memberLabel = memberCount <= 1 ? Localization.Get("guild.members.recruited_singular", memberCount) : Localization.Get("guild.members.recruited_plural", memberCount);
            int memberW = FontManager.MeasureText(memberLabel, 13);
            FontManager.DrawText(memberLabel, x + (WINDOW_WIDTH - memberW) / 2, frameY + frameSize + 52, 13, new Color(180, 170, 150, 200));

            // Bouton de personnalisation
            Rectangle customizeBtn = new Rectangle(x + WINDOW_WIDTH / 2f - 140, y + WINDOW_HEIGHT - 80, 280, 44);
            bool hoverCustomize = Raylib.CheckCollisionPointRec(mousePos, customizeBtn);
            Raylib.DrawRectangleRounded(customizeBtn, 0.25f, 8, hoverCustomize ? new Color(120, 95, 55, 255) : new Color(90, 70, 40, 230));
            Raylib.DrawRectangleRoundedLines(customizeBtn, 0.25f, 8, 2, COLOR_ACCENT);
            string btnLabel = Localization.Get("guild.emblem.customize");
            int btnLabelW = FontManager.MeasureText(btnLabel, 15);
            FontManager.DrawText(btnLabel, (int)(customizeBtn.X + (customizeBtn.Width - btnLabelW) / 2), (int)customizeBtn.Y + 14, 15, Color.White);
        }

        private static void DrawEmblemEditor(int x, int y, Vector2 mousePos)
        {
            // Bouton retour
            Rectangle backBtn = new Rectangle(x + 20, y + CONTENT_TOP + 8, 90, 28);
            bool hoverBack = Raylib.CheckCollisionPointRec(mousePos, backBtn);
            Raylib.DrawRectangleRounded(backBtn, 0.25f, 6, hoverBack ? new Color(80, 70, 50, 220) : new Color(55, 48, 38, 180));
            FontManager.DrawText(Localization.Get("guild.emblem.back"), (int)backBtn.X + 10, (int)backBtn.Y + 6, 13, Color.White);

            int editorTop = y + CONTENT_TOP + 44;

            // Logo display / painting
            int logoDisplayX = x + 20;
            int logoDisplayY = editorTop;
            int cellSize = LOGO_DISPLAY_SIZE / _canvasSize;

            if (_logoTexture.Id != 0)
            {
                Rectangle dest = new Rectangle(logoDisplayX, logoDisplayY, LOGO_DISPLAY_SIZE, LOGO_DISPLAY_SIZE);
                Raylib.DrawTexturePro(_logoTexture, new Rectangle(0, 0, _logoTexture.Width, _logoTexture.Height), dest, Vector2.Zero, 0, Color.White);
            }
            // Grid lines
            for (int i = 0; i <= _canvasSize; i++)
            {
                Raylib.DrawLine(logoDisplayX + i * cellSize, logoDisplayY, logoDisplayX + i * cellSize, logoDisplayY + LOGO_DISPLAY_SIZE, new Color(100, 100, 100, 60));
                Raylib.DrawLine(logoDisplayX, logoDisplayY + i * cellSize, logoDisplayX + LOGO_DISPLAY_SIZE, logoDisplayY + i * cellSize, new Color(100, 100, 100, 60));
            }

            // Palette
            int paletteX = x + 20 + LOGO_DISPLAY_SIZE + 20;
            int paletteY = editorTop;
            int paletteCellSize = 24;
            FontManager.DrawText(Localization.Get("guild.emblem.color"), paletteX, paletteY - 20, 14, COLOR_ACCENT);
            for (int i = 0; i < _palette.Length; i++)
            {
                int row = i / 2;
                int col = i % 2;
                int px = paletteX + col * (paletteCellSize + 2);
                int py = paletteY + row * (paletteCellSize + 2);
                Raylib.DrawRectangle(px, py, paletteCellSize, paletteCellSize, _palette[i]);
                if (_selectedPaletteIndex == i)
                    Raylib.DrawRectangleLines(px - 1, py - 1, paletteCellSize + 2, paletteCellSize + 2, Color.White);
                if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(px, py, paletteCellSize, paletteCellSize)))
                    Raylib.DrawRectangleLines(px - 1, py - 1, paletteCellSize + 2, paletteCellSize + 2, Color.Gold);
            }

            // Canvas size buttons
            int sizeBtnX = paletteX;
            int sizeBtnY = paletteY + _palette.Length / 2 * (paletteCellSize + 2) + 10;
            int sizeBtnH = 22;

            FontManager.DrawText(Localization.Get("guild.emblem.size", _canvasSize), sizeBtnX, sizeBtnY - 18, 11, COLOR_ACCENT);

            Rectangle minusBtn = new Rectangle(sizeBtnX, sizeBtnY, sizeBtnH, sizeBtnH);
            bool hoverMinus = Raylib.CheckCollisionPointRec(mousePos, minusBtn);
            Raylib.DrawRectangleRounded(minusBtn, 0.2f, 4, hoverMinus ? new Color(100, 70, 50, 255) : new Color(60, 40, 30, 200));
            FontManager.DrawText("-", (int)minusBtn.X + 7, (int)minusBtn.Y + 3, 14, Color.White);

            Rectangle plusBtn = new Rectangle(sizeBtnX + sizeBtnH + 8, sizeBtnY, sizeBtnH, sizeBtnH);
            bool hoverPlus = Raylib.CheckCollisionPointRec(mousePos, plusBtn);
            Raylib.DrawRectangleRounded(plusBtn, 0.2f, 4, hoverPlus ? new Color(100, 70, 50, 255) : new Color(60, 40, 30, 200));
            FontManager.DrawText("+", (int)plusBtn.X + 6, (int)plusBtn.Y + 3, 14, Color.White);

            // Save button
            Rectangle saveBtn = new Rectangle(x + WINDOW_WIDTH - 140, y + WINDOW_HEIGHT - 130, 120, 35);
            bool hoverSave = Raylib.CheckCollisionPointRec(mousePos, saveBtn);
            Raylib.DrawRectangleRounded(saveBtn, 0.2f, 6, hoverSave ? new Color(100, 200, 100, 255) : new Color(80, 140, 80, 255));
            FontManager.DrawText(Localization.Get("guild.emblem.save"), (int)saveBtn.X + 15, (int)saveBtn.Y + 10, 14, Color.White);

            // Export emblem button
            Rectangle exportBtn = new Rectangle(x + WINDOW_WIDTH - 140, y + WINDOW_HEIGHT - 90, 120, 35);
            bool hoverExport = Raylib.CheckCollisionPointRec(mousePos, exportBtn);
            Raylib.DrawRectangleRounded(exportBtn, 0.2f, 6, hoverExport ? new Color(100, 150, 200, 255) : new Color(70, 100, 150, 200));
            FontManager.DrawText(Localization.Get("guild.emblem.export_png"), (int)exportBtn.X + 12, (int)exportBtn.Y + 10, 12, Color.White);

            // Browse emblems button
            Rectangle browseBtn = new Rectangle(x + 20, y + WINDOW_HEIGHT - 90, 120, 35);
            bool hoverBrowse = Raylib.CheckCollisionPointRec(mousePos, browseBtn);
            Raylib.DrawRectangleRounded(browseBtn, 0.2f, 6, hoverBrowse ? new Color(100, 150, 200, 255) : new Color(70, 100, 150, 200));
            FontManager.DrawText(Localization.Get("guild.emblem.load"), (int)browseBtn.X + 22, (int)browseBtn.Y + 10, 12, Color.White);

            // Hint
            FontManager.DrawText(Localization.Get("guild.emblem.hint"), x + 20, y + WINDOW_HEIGHT - 25, 12, new Color(150, 140, 110, 180));
        }

        private static void DrawEmblemListPopup(Vector2 mousePos)
        {
            int popupW = 250;
            int popupH = 300;
            int popupX = (int)_windowPos.X - popupW - 10;
            int popupY = (int)_windowPos.Y + HEADER_HEIGHT + 40;

            // S'assurer que le popup reste dans l'écran
            if (popupX < 10) popupX = (int)_windowPos.X + WINDOW_WIDTH + 10;
            popupX = Math.Clamp(popupX, 10, Raylib.GetScreenWidth() - popupW - 10);
            popupY = Math.Clamp(popupY, 10, Raylib.GetScreenHeight() - popupH - 10);

            // Fond du popup
            Raylib.DrawRectangleRounded(new Rectangle(popupX, popupY, popupW, popupH), 0.1f, 8, COLOR_BG);
            Raylib.DrawRectangleRoundedLines(new Rectangle(popupX, popupY, popupW, popupH), 0.1f, 8, 2, COLOR_BORDER);

            FontManager.DrawText(Localization.Get("guild.emblem.popup_title"), popupX + 15, popupY + 10, 12, COLOR_ACCENT);

            // Bouton fermer
            Rectangle closePopBtn = new Rectangle(popupX + popupW - 25, popupY + 5, 20, 20);
            bool hoverClosePop = Raylib.CheckCollisionPointRec(mousePos, closePopBtn);
            Raylib.DrawRectangleRounded(closePopBtn, 0.2f, 4, hoverClosePop ? new Color(140, 60, 60, 255) : new Color(120, 50, 50, 200));
            FontManager.DrawText("X", (int)closePopBtn.X + 6, (int)closePopBtn.Y + 3, 12, Color.White);

            // Zone de la liste
            int listY = popupY + 30;
            int listH = popupH - 40;
            int itemH = 24;
            int visibleItems = listH / itemH;

            int firstVisible = _emblemListScroll / itemH;
            int lastVisible = Math.Min(_emblemList.Count, firstVisible + visibleItems + 1);

            Raylib.BeginScissorMode(popupX + 5, listY, popupW - 10, listH);

            for (int i = firstVisible; i < lastVisible; i++)
            {
                int itemY = listY + i * itemH - _emblemListScroll;
                Rectangle itemRect = new Rectangle(popupX + 10, itemY, popupW - 20, itemH - 2);
                bool hoverItem = Raylib.CheckCollisionPointRec(mousePos, itemRect);

                Color bgColor = hoverItem ? new Color(80, 70, 50, 255) : new Color(50, 45, 35, 200);
                Raylib.DrawRectangleRounded(itemRect, 0.2f, 4, bgColor);

                string emblemName = _emblemList[i];
                string displayName = emblemName.Length > 22 ? emblemName[..20] + ".." : emblemName;
                FontManager.DrawText(displayName, (int)itemRect.X + 8, (int)itemRect.Y + 4, 12, Color.White);
            }

            Raylib.EndScissorMode();

            if (_emblemList.Count == 0)
            {
                FontManager.DrawText(Localization.Get("guild.emblem.none_found"), popupX + 30, popupY + 80, 13, new Color(150, 140, 110, 200));
                FontManager.DrawText(Localization.Get("guild.emblem.none_found_hint"), popupX + 20, popupY + 105, 11, new Color(120, 110, 90, 160));
                FontManager.DrawText(Localization.Get("guild.emblem.none_found_hint2"), popupX + 50, popupY + 122, 11, new Color(120, 110, 90, 160));
            }
        }
    }
}
