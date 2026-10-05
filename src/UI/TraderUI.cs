using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;

namespace Soulfract
{
    public static class TraderUI
    {
        private static bool _isOpen = false;
        private static Entity _currentTrader = null!;
        private static Vector2 _windowPos = new Vector2(100, 100);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static NineSliceTexture? _tableTexture;
        private static int _hoveredItemIndex = -1;
        public static Entity? GetCurrentTrader() => _currentTrader;

        // Constantes – fenêtre basse, icônes en haut
        private static int WINDOW_WIDTH => UIManager.ScaleInt(420);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(350);
        private static int TOP_AREA_HEIGHT => UIManager.ScaleInt(200);
        private static int ITEM_ICON_SIZE => UIManager.ScaleInt(40);
        private static int ITEM_SPACING => UIManager.ScaleInt(8);
        private static int ITEMS_ROW_Y_OFFSET => -UIManager.ScaleInt(4);
        private const int MIN_TRADE_COST_COINS = 1;

        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;

        // Fond totalement opaque
        private static readonly Color COLOR_BG = new Color(20, 22, 28, 255);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_SLOT_BG = new Color(50, 53, 60, 255);
        private static readonly Color COLOR_SLOT_HOVER = new Color(70, 73, 80, 255);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);

        private static Texture2D _closeIcon;

        public static bool IsOpen => _isOpen;

        public static void Open(Entity trader)
        {
            try
            {
                if (trader == null) return;
                _currentTrader = trader;
                _isOpen = true;
                _hoveredItemIndex = -1;
                _tableTexture = UIManager.TablePanelTexture;
                LoadTextures();

                if (_currentTrader.Equipment != null)
                    _currentTrader.Equipment.LoadEquipmentTextures();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur dans TraderUI.Open : {ex}");
                Close();
            }
        }

        public static void Close()
        {
            _isOpen = false;
            _currentTrader = null;
            _hoveredItemIndex = -1;
            _tableTexture = null;
            UnloadTextures();
            _isDragging = false;
            _dragOffset = Vector2.Zero;
        }

        private static void LoadTextures()
        {
            _closeIcon = Raylib.LoadTexture("assets/gui/close.png");
        }

        private static void UnloadTextures()
        {
            if (_closeIcon.Id != 0) Raylib.UnloadTexture(_closeIcon);
        }

        public static void Update()
        {
            if (!_isOpen || _currentTrader == null || _currentTrader.TraderItems == null) return;

            Vector2 mousePos = Raylib.GetMousePosition();
            Rectangle windowRect = new Rectangle(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, WINDOW_HEIGHT);

            bool isOverItem = false;
            int itemsCount = _currentTrader.TraderItems.Count;
            int startX = (int)_windowPos.X + 20;
            int startY = (int)_windowPos.Y + ITEMS_ROW_Y_OFFSET;
            int totalWidth = itemsCount * (ITEM_ICON_SIZE + ITEM_SPACING) - ITEM_SPACING;
            int offsetX = (WINDOW_WIDTH - 40 - totalWidth) / 2;
            if (offsetX < 0) offsetX = 0;

            for (int i = 0; i < itemsCount; i++)
            {
                int iconX = startX + offsetX + i * (ITEM_ICON_SIZE + ITEM_SPACING);
                int iconY = startY;
                Rectangle iconRect = new Rectangle(iconX, iconY, ITEM_ICON_SIZE, ITEM_ICON_SIZE);
                if (Raylib.CheckCollisionPointRec(mousePos, iconRect))
                {
                    isOverItem = true;
                    break;
                }
            }

            Rectangle closeButton = new Rectangle(_windowPos.X + WINDOW_WIDTH - 32, _windowPos.Y + 8, 24, 24);
            bool isOverClose = Raylib.CheckCollisionPointRec(mousePos, closeButton);

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, windowRect) && !isOverItem && !isOverClose)
            {
                _isDragging = true;
                _dragOffset = mousePos - _windowPos;
            }
            if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                _windowPos = mousePos - _dragOffset;
                _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + 50, Raylib.GetScreenWidth() - 50);
                _windowPos.Y = Math.Clamp(_windowPos.Y, 0, Raylib.GetScreenHeight() - 50);
            }
            if (Raylib.IsMouseButtonReleased(MouseButton.Left)) _isDragging = false;
            if (_isDragging && !Raylib.IsMouseButtonDown(MouseButton.Left))
                _isDragging = false;

            _hoveredItemIndex = -1;
            for (int i = 0; i < itemsCount; i++)
            {
                int iconX = startX + offsetX + i * (ITEM_ICON_SIZE + ITEM_SPACING);
                int iconY = startY;
                Rectangle iconRect = new Rectangle(iconX, iconY, ITEM_ICON_SIZE, ITEM_ICON_SIZE);

                if (Raylib.CheckCollisionPointRec(mousePos, iconRect))
                {
                    _hoveredItemIndex = i;
                    if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        int itemId = _currentTrader.TraderItems[i];
                        if (GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
                        {
                            int tradeCost = GetTradeCost(itemData);
                            int coinItemId = Program.GetItemId("coin");
                            if (coinItemId <= 0)
                            {
                                Program.AddNotification(new Notification("Le marchand ne peut pas accepter de paiement pour le moment.", Color.Orange, 2f));
                                Console.WriteLine(" Coin item unavailable for trader purchases");
                            }
                            else if (Program.GetItemCountInInventoryById(coinItemId) < tradeCost)
                            {
                                Program.AddNotification(new Notification($"Vous n'avez pas assez de pièces pour acheter {itemData.Name}.", Color.Orange, 2f));
                            }
                            else
                            {
                                var tempItem = new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
                                int remaining = Program.AddItemToInventory(tempItem, 1);
                                if (remaining == 0)
                                {
                                    if (Program.ConsumeItemFromInventoryById(coinItemId, tradeCost))
                                    {
                                        Program.AddItemNotification(itemData.Name, 1, itemData.Color);
                                        Program.AddNotification(new Notification($"{itemData.Name} acheté contre {tradeCost} pièce{(tradeCost > 1 ? "s" : string.Empty)}.", Color.Gold, 1.8f));
                                        Console.WriteLine($" Acheté: {itemData.Name} pour {tradeCost} pièce(s)");
                                        if (_currentTrader != null)
                                        {
                                            float friendshipGain = Math.Clamp(tradeCost * 0.002f, 0f, 1f);
                                            if (NetworkManager.IsClient)
                                                NetworkManager.RequestTraderPurchase(_currentTrader.NetId, itemId);
                                            else
                                                _currentTrader.Friendship = Math.Clamp(_currentTrader.Friendship + friendshipGain, 0f, 1f);
                                        }
                                    }
                                    else
                                    {
                                        Program.AddNotification(new Notification($"Impossible de débiter les pièces pour {itemData.Name}.", Color.Red, 2f));
                                    }
                                }
                                else
                                {
                                    Program.AddNotification(new Notification($"Inventaire plein ! Impossible d'acheter {itemData.Name}", Color.Red, 2f));
                                    Console.WriteLine($" Inventaire plein pour: {itemData.Name}");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($" Item ID {itemId} non trouvé dans la base de données");
                        }
                    }
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                Close();
        }

        private static int GetTradeCost(ItemData itemData)
        {
            return itemData.Value > 0 ? itemData.Value : MIN_TRADE_COST_COINS;
        }

        private static Color GetRarityColor(string rarity)
        {
            if (string.IsNullOrEmpty(rarity)) return new Color(200, 200, 160, 255);
            return rarity.ToLower() switch
            {
                "rare" => new Color(120, 180, 255, 255),
                "epic" => new Color(200, 120, 255, 255),
                "legendary" => new Color(255, 180, 80, 255),
                _ => new Color(200, 200, 160, 255)
            };
        }

        //  Le PNJ passé à Open() peut être soit une vraie entité simulée par l'hôte (dans
        // Program.entities), soit un "fantôme" reconstruit côté client à partir du dernier
        // snapshot réseau (voir NetworkManager.GetVillagerProxies) - dans ce cas
        // Program.entities est TOUJOURS vide et une vérification par référence ne
        // correspondra donc jamais, même si le PNJ est parfaitement valide. On vérifie donc
        // sa présence par NetId dans le dernier snapshot reçu quand on est client.
        private static bool IsCurrentTraderStillValid()
        {
            if (_currentTrader == null || !_currentTrader.IsAlive) return false;

            if (NetworkManager.IsClient)
            {
                foreach (var dto in NetworkManager.GetRemoteEntities())
                {
                    if (Guid.TryParse(dto.NetId, out var id) && id == _currentTrader.NetId)
                        return true;
                }
                return false;
            }

            return Program.GetEntities().Contains(_currentTrader);
        }

        public static void Draw()
        {
            if (!_isOpen) return;
            if (!IsCurrentTraderStillValid())
            {
                Close();
                return;
            }
            if (_currentTrader.TraderItems == null)
            {
                Close();
                return;
            }

            int windowX = (int)_windowPos.X;
            int windowY = (int)_windowPos.Y;

            // ========== 1. DESSIN DU PNJ ==========
            int previewSize = (int)(WINDOW_WIDTH * 0.66f);
            int previewX = windowX + (WINDOW_WIDTH - previewSize) / 2;
            int previewY = windowY - 60;

            Vector2 vendorPos = new Vector2(
                previewX + previewSize / 2f,
                previewY + previewSize * 0.12f
            );

            Texture2D hairBase = Program.hairBaseTextures.Count > 0
                ? Program.hairBaseTextures[Math.Clamp(_currentTrader.HairStyle, 0, Program.hairBaseTextures.Count - 1)]
                : new Texture2D();
            Texture2D hairOverlay = Program.hairOverlayTextures.Count > 0
                ? Program.hairOverlayTextures[Math.Clamp(_currentTrader.HairStyle, 0, Program.hairOverlayTextures.Count - 1)]
                : new Texture2D();

            string speciesKey = _currentTrader.Species?.ToLowerInvariant() ?? "";
            if (SpeciesData.Skeletons.ContainsKey(speciesKey))
            {
                float customScale = 2.1f;
                EntityRenderer.DrawEntity(
                    speciesName: _currentTrader.Species,
                    anim: "idle",
                    frame: 0,
                    prog: 0f,
                    facing: 1f,
                    pos: vendorPos,
                    tint: _currentTrader.Tint,
                    hBase: hairBase,
                    hOverlay: hairOverlay,
                    hColor: _currentTrader.HairColor,
                    skeletons: SpeciesData.Skeletons,
                    eyes: Program.EyesTexture,
                    mouth: Program.MouthTexture,
                    customScale: customScale,
                    equipment: _currentTrader.Equipment,
                    isCarrying: false,
                    inWater: false,
                    attackSwingProgress: 0f,
                    heldItemTexture: default,
                    headAngle: 0f,
                    keepItemHorizontal: false,
                    isBow: false,
                    underwearTexture: Program.LeafUnderpantsTexture,
                    beardStyle: _currentTrader.BeardStyle,
                    flashWhite: false
                );
            }
            else
            {
                Raylib.DrawRectangle(previewX + 10, previewY + 10, previewSize - 20, previewSize - 20, Color.Gray);
                FontManager.DrawText("PNJ", previewX + previewSize / 2 - 20, previewY + previewSize / 2 - 10, 20, Color.White);
            }

            // ========== 2. TABLE (fond opaque) ==========
            if (_tableTexture != null && _tableTexture.IsValid)
            {
                _tableTexture.Draw(new Rectangle(windowX, windowY, WINDOW_WIDTH, WINDOW_HEIGHT), new Color(255, 255, 255, 255));
            }
            else
            {
                Raylib.DrawRectangleRounded(new Rectangle(windowX, windowY, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, new Color(20, 22, 28, 255));
                Raylib.DrawRectangleRoundedLines(new Rectangle(windowX, windowY, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, 2, COLOR_BORDER);
            }

            // ========== 3. BOUTON FERMER ==========
            Rectangle closeButton = new Rectangle(windowX + WINDOW_WIDTH - 32, windowY + 8, 24, 24);
            bool isHoverClose = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), closeButton);
            Raylib.DrawRectangleRounded(closeButton, 0.2f, 6, isHoverClose ? new Color(140,60,60,255) : new Color(80,40,40,200));
            if (_closeIcon.Id != 0)
                Raylib.DrawTexturePro(_closeIcon, new Rectangle(0,0,_closeIcon.Width,_closeIcon.Height),
                    new Rectangle(closeButton.X+4, closeButton.Y+4, 16, 16), Vector2.Zero, 0, isHoverClose ? Color.Red : new Color(200,120,120,200));
            if (isHoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Close();
                return;
            }

            // ========== 4. ITEMS ==========
            if (_currentTrader.TraderItems == null || _currentTrader.TraderItems.Count == 0)
            {
                Raylib.DrawText(Localization.Get("trader.empty"), windowX + 50, windowY + 120, 14, Color.LightGray);
                return;
            }

            int startX = windowX + 20;
            int startY = windowY + ITEMS_ROW_Y_OFFSET;
            int itemsCount = _currentTrader.TraderItems.Count;
            int totalWidth = itemsCount * (ITEM_ICON_SIZE + ITEM_SPACING) - ITEM_SPACING;
            int offsetX = (WINDOW_WIDTH - 40 - totalWidth) / 2;
            if (offsetX < 0) offsetX = 0;

            for (int i = 0; i < itemsCount; i++)
            {
                int iconX = startX + offsetX + i * (ITEM_ICON_SIZE + ITEM_SPACING);
                int iconY = startY;
                bool hover = i == _hoveredItemIndex;

                int itemId = _currentTrader.TraderItems[i];
                if (GameData.ItemDatabase.TryGetValue(itemId, out var itemData) && itemData.Icon.Id != 0)
                {
                    Rectangle srcRect = new Rectangle(0, 0, itemData.Icon.Width, itemData.Icon.Height);
                    Rectangle destRect = new Rectangle(iconX, iconY, ITEM_ICON_SIZE, ITEM_ICON_SIZE);

                    if (hover)
                    {
                        Color outlineColor = GetRarityColor(itemData.Rarity);
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            for (int dy = -2; dy <= 2; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                Rectangle destOutline = new Rectangle(iconX + dx, iconY + dy, ITEM_ICON_SIZE, ITEM_ICON_SIZE);
                                Raylib.DrawTexturePro(itemData.Icon, srcRect, destOutline, Vector2.Zero, 0, outlineColor);
                            }
                        }
                    }

                    Raylib.DrawTexturePro(itemData.Icon, srcRect, destRect, Vector2.Zero, 0, Color.White);
                }
                else
                {
                    Raylib.DrawText($"{itemId}", iconX + 4, iconY + 8, 10, Color.White);
                }
            }

            // ========== 5. ZONE D'INFORMATION (centrée, rapprochée du haut) ==========
            // Padding latéral réduit pour que la boîte soit plus large, et décalage vers le haut
            int infoPadding = 80;
            Rectangle infoArea = new Rectangle(
                windowX + infoPadding,
                windowY + TOP_AREA_HEIGHT - 80,
                WINDOW_WIDTH - 2 * infoPadding,
                WINDOW_HEIGHT - TOP_AREA_HEIGHT + 30
            );

            if (_hoveredItemIndex >= 0 && _hoveredItemIndex < _currentTrader.TraderItems.Count)
            {
                int hoveredItemId = _currentTrader.TraderItems[_hoveredItemIndex];
                if (GameData.ItemDatabase.TryGetValue(hoveredItemId, out var hoveredData))
                {
                    var hoveredItem = new Item(hoveredData.Name, 1, hoveredData.Color, hoveredData.Icon);
                    InventoryRenderer.BuildItemTooltipData(hoveredItem, 1, out var displayName, out var detailLines, out var descriptionLines, out var rarityText, out var rarityColor, out var detailIconKeys);
                    Rectangle tooltipRect = new Rectangle(infoArea.X + 8, infoArea.Y + 8, infoArea.Width - 16, infoArea.Height - 16);
                    InventoryRenderer.DrawSharedItemTooltip(tooltipRect, displayName, hoveredItem, detailLines, descriptionLines, 1, rarityText, rarityColor, detailIconKeys);

                    int coinItemId = Program.GetItemId("coin");
                    int tradeCost = GetTradeCost(hoveredData);
                    if (coinItemId > 0 && GameData.ItemDatabase.TryGetValue(coinItemId, out var coinData) && coinData.Icon.Id != 0)
                    {
                        Rectangle coinSrc = new Rectangle(0, 0, coinData.Icon.Width, coinData.Icon.Height);
                        Rectangle coinDest = new Rectangle(tooltipRect.X + tooltipRect.Width - 54, tooltipRect.Y + 8, 16, 16);
                        Raylib.DrawTexturePro(coinData.Icon, coinSrc, coinDest, Vector2.Zero, 0, Color.White);
                        FontManager.DrawText($"{tradeCost}", (int)(coinDest.X + 20), (int)(coinDest.Y - 1), 14, Color.Gold);
                    }
                }
            }
            else
            {
                string hint = Localization.Get("trader.hover_for_details", "Passez la souris sur un objet pour voir ses informations.");
                Raylib.DrawText(hint, (int)(infoArea.X + 12), (int)(infoArea.Y + 18), 14, new Color(200, 200, 200, 220));
            }

            int balanceCoinItemId = Program.GetItemId("coin");
            int playerCoins = balanceCoinItemId > 0 ? Program.GetItemCountInInventoryById(balanceCoinItemId) : 0;
            if (balanceCoinItemId > 0 && GameData.ItemDatabase.TryGetValue(balanceCoinItemId, out var balanceCoinData) && balanceCoinData.Icon.Id != 0)
            {
                Rectangle coinSrc = new Rectangle(0, 0, balanceCoinData.Icon.Width, balanceCoinData.Icon.Height);
                Rectangle coinDest = new Rectangle(windowX + WINDOW_WIDTH - 72, windowY + WINDOW_HEIGHT - 26, 16, 16);
                Raylib.DrawTexturePro(balanceCoinData.Icon, coinSrc, coinDest, Vector2.Zero, 0, Color.White);
                FontManager.DrawText($"{playerCoins}", (int)(coinDest.X + 20), (int)(coinDest.Y - 1), 14, Color.Gold);
            }
        }

    }
}