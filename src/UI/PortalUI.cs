// PortalUI.cs - Menu de sélection des portails pour téléportation
//
// Remplace l'ancien PylonUI. Différence fondamentale : la liste ne dépend plus
// des chunks actuellement chargés en mémoire (World.GetAllSurfaceChunks / GetAllCaveChunks),
// mais du registre persistant World._knownPortals, alimenté à la découverte de chaque
// portail (World.DiscoverPortal). Un portail découvert reste donc accessible même à
// 3000 blocs de distance, chunk déchargé ou non.
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static class PortalUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = new Vector2(380, 140);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isHoveringTitleBar = false;

        private static List<PortalInfo> _portals = new();
        private static int _scrollOffset = 0;
        private static bool _isDraggingScrollbar = false;
        private static float _scrollbarDragStartY = 0f;
        private static int _scrollbarDragStartOffset = 0;

        // Portail depuis lequel le menu a été ouvert (mis en évidence dans la liste)
        private static int _originTileX = int.MinValue;
        private static int _originTileY = int.MinValue;
        private static bool _originIsCave = false;

        // Recherche
        private static string _searchText = "";
        private static bool _searchFocused = false;

        // Tri
        private enum SortMode { Distance, Name, Recent }
        private static SortMode _sortMode = SortMode.Distance;

        // Renommage en cours (clé du portail concerné)
        private static (int x, int y, bool isCave)? _renamingKey = null;
        private static string _renameBuffer = "";

        private static int WINDOW_WIDTH => UIManager.ScaleInt(420);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(520);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(48);
        private static int SEARCH_HEIGHT => UIManager.ScaleInt(34);
        private static int SORTBAR_HEIGHT => UIManager.ScaleInt(28);
        private static int ENTRY_HEIGHT => UIManager.ScaleInt(64);
        private static int SCROLLBAR_WIDTH => UIManager.ScaleInt(8);
        private static int LIST_TOP_MARGIN => HEADER_HEIGHT + SEARCH_HEIGHT + SORTBAR_HEIGHT + UIManager.ScaleInt(16);
        private static int FOOTER_HEIGHT => UIManager.ScaleInt(26);

        private static readonly Color COLOR_BG = new Color(22, 24, 31, 245);
        private static readonly Color COLOR_HEADER = new Color(33, 30, 46, 255);
        private static readonly Color COLOR_BORDER = new Color(120, 90, 200, 200);
        private static readonly Color COLOR_ACCENT = new Color(180, 140, 255, 255);
        private static readonly Color COLOR_ACCENT_DIM = new Color(140, 110, 200, 160);
        private static readonly Color COLOR_ENTRY_NORMAL = new Color(40, 38, 52, 200);
        private static readonly Color COLOR_ENTRY_HOVER = new Color(58, 54, 76, 210);
        private static readonly Color COLOR_ENTRY_SELECTED = new Color(180, 140, 255, 55);
        private static readonly Color COLOR_ENTRY_ORIGIN = new Color(120, 220, 180, 45);
        private static readonly Color COLOR_TEXT_DIM = new Color(160, 155, 175, 200);
        private static readonly Color COLOR_CAVE_TAG = new Color(200, 150, 90, 255);
        private static readonly Color COLOR_SURFACE_TAG = new Color(120, 190, 255, 255);
        private static readonly Color COLOR_FAVORITE = new Color(255, 205, 90, 255);

        private static Texture2D _closeIcon;
        private static bool _texturesLoaded = false;

        public static bool IsOpen => _isOpen;

        public static bool IsTextInputFocused => _searchFocused || _renamingKey != null;

        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;

        private static void LoadTextures()
        {
            if (_texturesLoaded) return;
            _closeIcon = LoadTextureOrDefault("assets/gui/close.png");
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
            if (_closeIcon.Id != 0) Raylib.UnloadTexture(_closeIcon);
            _texturesLoaded = false;
        }

        /// <summary>Ouvre le menu, en mettant en évidence le portail depuis lequel on l'a ouvert (optionnel).</summary>
        public static void Open(int originTileX = int.MinValue, int originTileY = int.MinValue, bool originIsCave = false)
        {
            LoadTextures();
            _originTileX = originTileX;
            _originTileY = originTileY;
            _originIsCave = originIsCave;
            RefreshPortalList();
            _isOpen = true;
            _scrollOffset = 0;
            _searchText = "";
            _searchFocused = false;
            _renamingKey = null;
        }

        public static void Close()
        {
            _isOpen = false;
            UnloadTextures();
            _isDragging = false;
            _isDraggingScrollbar = false;
            _renamingKey = null;
            _searchFocused = false;
        }

        /// <summary>Récupère les portails connus depuis le registre persistant de World (aucun scan de chunk).</summary>
        private static void RefreshPortalList()
        {
            _portals = World.GetKnownPortals();
        }

        private static List<PortalInfo> GetFilteredSortedPortals()
        {
            Vector2 playerPos = Program.GetPlayerPosition();
            int ts = Program.TileSize;
            int playerTileX = (int)(playerPos.X / ts);
            int playerTileY = (int)(playerPos.Y / ts);

            IEnumerable<PortalInfo> query = _portals;

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string needle = _searchText.Trim();
                query = query.Where(p => p.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            float DistOf(PortalInfo p)
            {
                float dx = p.TileX - playerTileX;
                float dy = p.TileY - playerTileY;
                return dx * dx + dy * dy;
            }

            IOrderedEnumerable<PortalInfo> sorted = _sortMode switch
            {
                SortMode.Name => query.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase),
                SortMode.Recent => query.OrderByDescending(p => p.DiscoveredAt),
                _ => query.OrderBy(p => DistOf(p)),
            };

            // Les favoris remontent toujours en tête, en conservant l'ordre de tri choisi à l'intérieur de chaque groupe.
            return sorted.OrderByDescending(p => p.IsFavorite).ToList();
        }

        public static void Update()
        {
            if (!_isOpen) return;

            // Le registre peut évoluer pendant que le menu est ouvert (découverte d'un nouveau portail
            // ailleurs en multijoueur, par exemple) : on garde la liste synchronisée.
            RefreshPortalList();

            Vector2 mousePos = Raylib.GetMousePosition();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + 100, sw - 100);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, sh - 100);

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;

            // ---- Barre de titre (drag) ----
            Rectangle titleBar = new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT);
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

            // ---- Bouton fermer ----
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 35, y + 10, 25, 25);
            if (Raylib.CheckCollisionPointRec(mousePos, closeBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Close();
                return;
            }

            // ---- Barre de recherche ----
            Rectangle searchRect = new Rectangle(x + 15, y + HEADER_HEIGHT + 6, WINDOW_WIDTH - 30, SEARCH_HEIGHT);
            bool hoverSearch = Raylib.CheckCollisionPointRec(mousePos, searchRect);
            if (hoverSearch && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _searchFocused = true;
                _renamingKey = null;
            }
            else if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !hoverSearch)
            {
                _searchFocused = false;
            }
            if (_searchFocused)
            {
                TextInput.Update(ref _searchText, "portal-search", searchRect, 30);
                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    _searchFocused = false;
                    TextInput.Reset("portal-search");
                }
            }

            // ---- Onglets de tri ----
            int sortBarY = y + HEADER_HEIGHT + SEARCH_HEIGHT + 10;
            int sortTabWidth = (WINDOW_WIDTH - 30) / 3;
            string[] sortLabels = { "Distance", "Nom", "Récents" };
            for (int i = 0; i < 3; i++)
            {
                Rectangle tabRect = new Rectangle(x + 15 + i * sortTabWidth, sortBarY, sortTabWidth - 4, SORTBAR_HEIGHT);
                if (Raylib.CheckCollisionPointRec(mousePos, tabRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _sortMode = (SortMode)i;
                }
            }

            var portals = GetFilteredSortedPortals();

            int listStartY = y + LIST_TOP_MARGIN;
            int listHeight = WINDOW_HEIGHT - LIST_TOP_MARGIN - FOOTER_HEIGHT - (y - y);
            listHeight = WINDOW_HEIGHT - (LIST_TOP_MARGIN + FOOTER_HEIGHT);
            int totalHeight = portals.Count * ENTRY_HEIGHT;

            Rectangle listRect = new Rectangle(x + 10, listStartY, WINDOW_WIDTH - 30, listHeight);
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0 && Raylib.CheckCollisionPointRec(mousePos, listRect))
            {
                _scrollOffset -= (int)wheel * ENTRY_HEIGHT;
                _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, totalHeight - listHeight));
            }

            // ---- Barre de défilement ----
            if (totalHeight > listHeight)
            {
                int scrollbarX = x + WINDOW_WIDTH - SCROLLBAR_WIDTH - 15;
                int scrollbarHeight = Math.Max(30, (int)((float)listHeight / totalHeight * listHeight));
                float scrollPercent = (float)_scrollOffset / Math.Max(1, totalHeight - listHeight);
                int scrollbarY = listStartY + (int)(scrollPercent * (listHeight - scrollbarHeight));
                Rectangle scrollbarRect = new Rectangle(scrollbarX, scrollbarY, SCROLLBAR_WIDTH, scrollbarHeight);

                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, scrollbarRect))
                {
                    _isDraggingScrollbar = true;
                    _scrollbarDragStartY = mousePos.Y;
                    _scrollbarDragStartOffset = _scrollOffset;
                }
                if (_isDraggingScrollbar && Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    float deltaY = mousePos.Y - _scrollbarDragStartY;
                    float deltaPercent = deltaY / Math.Max(1, listHeight - scrollbarHeight);
                    _scrollOffset = _scrollbarDragStartOffset + (int)(deltaPercent * (totalHeight - listHeight));
                    _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, totalHeight - listHeight));
                }
                if (_isDraggingScrollbar && Raylib.IsMouseButtonReleased(MouseButton.Left))
                    _isDraggingScrollbar = false;
            }
            else
            {
                _scrollOffset = 0;
            }

            // ---- Entrées : interactions (favori / renommer / téléporter) ----
            int firstVisible = Math.Max(0, _scrollOffset / ENTRY_HEIGHT);
            int lastVisible = Math.Min(portals.Count, firstVisible + (listHeight / ENTRY_HEIGHT) + 2);

            for (int i = firstVisible; i < lastVisible; i++)
            {
                var portal = portals[i];
                int entryY = listStartY + i * ENTRY_HEIGHT - _scrollOffset;
                if (entryY + ENTRY_HEIGHT < listStartY || entryY > listStartY + listHeight) continue;

                Rectangle entryRect = new Rectangle(x + 15, entryY, WINDOW_WIDTH - 45, ENTRY_HEIGHT - 6);
                var key = (portal.TileX, portal.TileY, portal.IsCave);
                bool isRenaming = _renamingKey.HasValue && _renamingKey.Value == key;

                Rectangle favRect = new Rectangle(entryRect.X + 8, entryRect.Y + (ENTRY_HEIGHT - 6 - 20) / 2f, 20, 20);
                Rectangle editRect = new Rectangle(entryRect.X + entryRect.Width - 32, entryRect.Y + 8, 22, 22);
                Rectangle goRect = new Rectangle(entryRect.X + entryRect.Width - 32, entryRect.Y + entryRect.Height - 30, 22, 22);

                bool hoverFav = Raylib.CheckCollisionPointRec(mousePos, favRect);
                bool hoverEdit = Raylib.CheckCollisionPointRec(mousePos, editRect);
                bool hoverGo = !isRenaming && Raylib.CheckCollisionPointRec(mousePos, goRect);
                bool hoverEntry = !isRenaming && Raylib.CheckCollisionPointRec(mousePos, entryRect) && !hoverFav && !hoverEdit && !hoverGo;

                if (hoverFav && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    World.SetPortalFavorite(portal.TileX, portal.TileY, portal.IsCave, !portal.IsFavorite);
                }
                else if (hoverEdit && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _renamingKey = key;
                    _renameBuffer = portal.DisplayName;
                    _searchFocused = false;
                }
                else if (isRenaming)
                {
                    TextInput.Update(ref _renameBuffer, $"portal-rename-{key.TileX}-{key.TileY}-{key.IsCave}", entryRect, 26);
                    Rectangle confirmRect = new Rectangle(entryRect.X + entryRect.Width - 56, entryRect.Y + entryRect.Height / 2f - 11, 22, 22);
                    Rectangle cancelRect = new Rectangle(entryRect.X + entryRect.Width - 30, entryRect.Y + entryRect.Height / 2f - 11, 22, 22);
                    bool confirmClicked = Raylib.CheckCollisionPointRec(mousePos, confirmRect) && Raylib.IsMouseButtonPressed(MouseButton.Left);
                    bool cancelClicked = Raylib.CheckCollisionPointRec(mousePos, cancelRect) && Raylib.IsMouseButtonPressed(MouseButton.Left);
                    if (confirmClicked || Raylib.IsKeyPressed(KeyboardKey.Enter))
                    {
                        World.RenamePortal(portal.TileX, portal.TileY, portal.IsCave, _renameBuffer);
                        _renamingKey = null;
                        TextInput.Reset();
                    }
                    else if (cancelClicked || Raylib.IsKeyPressed(KeyboardKey.Escape))
                    {
                        _renamingKey = null;
                        TextInput.Reset();
                    }
                    else if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                             !Raylib.CheckCollisionPointRec(mousePos, entryRect))
                    {
                        // Clic ailleurs : on valide le renommage en cours plutôt que de le perdre.
                        World.RenamePortal(portal.TileX, portal.TileY, portal.IsCave, _renameBuffer);
                        _renamingKey = null;
                        TextInput.Reset();
                    }
                }
                else if ((hoverEntry || hoverGo) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    Program.TeleportPlayerTo(portal.TileX, portal.TileY, portal.IsCave);
                    Program.AddNotification(new Notification($"Téléporté vers {portal.DisplayName}", new Color(180, 140, 255, 255), 2.2f));
                    Program.ApplyScreenShake(4f, 0.2f);
                    Close();
                    return;
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape) && !_searchFocused && _renamingKey == null)
                Close();
        }

        public static void Draw()
        {
            if (!_isOpen) return;

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();

            // ---- Panneau ----
            Raylib.DrawRectangleRounded(new Rectangle(x + 4, y + 4, WINDOW_WIDTH, WINDOW_HEIGHT), 0.08f, 12, new Color(0, 0, 0, 110));
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.08f, 12, COLOR_BG);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.08f, 12, 2, COLOR_BORDER);

            // ---- Titre ----
            Color titleColor = _isHoveringTitleBar ? new Color(60, 50, 90, 220) : COLOR_HEADER;
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.08f, 8, titleColor);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.08f, 8, 1, COLOR_ACCENT);

            string title = "PORTAILS";
            int titleWidth = FontManager.MeasureText(title, 19);
            DrawPortalGlyph(new Rectangle(x + 16, y + 9, 30, 30), COLOR_ACCENT);
            FontManager.DrawText(title, x + 56, y + 15, 19, COLOR_ACCENT);

            int knownCount = _portals.Count;
            string countLabel = knownCount == 0 ? "aucun portail découvert" : $"{knownCount} portail{(knownCount > 1 ? "s" : "")} découvert{(knownCount > 1 ? "s" : "")}";
            FontManager.DrawText(countLabel, x + 56, y + 32, 10, COLOR_TEXT_DIM);

            // ---- Bouton fermer ----
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 35, y + 10, 25, 25);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(150, 60, 60, 255) : new Color(80, 40, 40, 200));
            if (_closeIcon.Id != 0)
                Raylib.DrawTexturePro(_closeIcon, new Rectangle(0, 0, _closeIcon.Width, _closeIcon.Height),
                    new Rectangle(closeBtn.X + 4, closeBtn.Y + 4, 17, 17), Vector2.Zero, 0, hoverClose ? Color.Red : new Color(200, 120, 120, 200));
            else
            {
                Raylib.DrawLineEx(new Vector2(closeBtn.X + 6, closeBtn.Y + 6), new Vector2(closeBtn.X + 19, closeBtn.Y + 19), 2.5f, Color.White);
                Raylib.DrawLineEx(new Vector2(closeBtn.X + 19, closeBtn.Y + 6), new Vector2(closeBtn.X + 6, closeBtn.Y + 19), 2.5f, Color.White);
            }

            // ---- Barre de recherche ----
            Rectangle searchRect = new Rectangle(x + 15, y + HEADER_HEIGHT + 6, WINDOW_WIDTH - 30, SEARCH_HEIGHT);
            Raylib.DrawRectangleRounded(searchRect, 0.3f, 8, new Color(15, 16, 22, 220));
            Raylib.DrawRectangleRoundedLines(searchRect, 0.3f, 8, 1, _searchFocused ? COLOR_ACCENT : COLOR_ACCENT_DIM);
            // Loupe stylisée
            Vector2 magCenter = new Vector2(searchRect.X + 16, searchRect.Y + SEARCH_HEIGHT / 2f);
            Raylib.DrawCircleLines((int)magCenter.X, (int)magCenter.Y, 5, COLOR_TEXT_DIM);
            Raylib.DrawLineEx(magCenter + new Vector2(3.5f, 3.5f), magCenter + new Vector2(8f, 8f), 1.5f, COLOR_TEXT_DIM);

            Rectangle searchTextRect = new Rectangle(searchRect.X + 20, searchRect.Y, searchRect.Width - 20, searchRect.Height);
            TextInput.DrawSingleLine(_searchText, "portal-search", searchTextRect, 13, Color.White,
                new Color(90, 110, 150, 220), COLOR_ACCENT, 8, placeholder: "Rechercher un portail...");

            // ---- Onglets de tri ----
            int sortBarY = y + HEADER_HEIGHT + SEARCH_HEIGHT + 10;
            int sortTabWidth = (WINDOW_WIDTH - 30) / 3;
            string[] sortLabels = { "Distance", "Nom", "Récents" };
            for (int i = 0; i < 3; i++)
            {
                Rectangle tabRect = new Rectangle(x + 15 + i * sortTabWidth, sortBarY, sortTabWidth - 4, SORTBAR_HEIGHT);
                bool active = (int)_sortMode == i;
                bool hover = Raylib.CheckCollisionPointRec(mousePos, tabRect);
                Raylib.DrawRectangleRounded(tabRect, 0.3f, 6, active ? new Color(180, 140, 255, 60) : (hover ? new Color(60, 56, 78, 180) : new Color(35, 33, 44, 150)));
                if (active) Raylib.DrawRectangleRoundedLines(tabRect, 0.3f, 6, 1, COLOR_ACCENT);
                int lw = FontManager.MeasureText(sortLabels[i], 11);
                FontManager.DrawText(sortLabels[i], (int)(tabRect.X + (tabRect.Width - lw) / 2), (int)tabRect.Y + 8, 11, active ? COLOR_ACCENT : COLOR_TEXT_DIM);
            }

            var portals = GetFilteredSortedPortals();

            int listStartY = y + LIST_TOP_MARGIN;
            int listHeight = WINDOW_HEIGHT - (LIST_TOP_MARGIN + FOOTER_HEIGHT);

            Vector2 playerPos = Program.GetPlayerPosition();
            int ts = Program.TileSize;
            int playerTileX = (int)(playerPos.X / ts);
            int playerTileY = (int)(playerPos.Y / ts);

            Raylib.BeginScissorMode(x + 10, listStartY, WINDOW_WIDTH - 30, listHeight);

            int firstVisible = Math.Max(0, _scrollOffset / ENTRY_HEIGHT);
            int lastVisible = Math.Min(portals.Count, firstVisible + (listHeight / ENTRY_HEIGHT) + 2);

            for (int i = firstVisible; i < lastVisible; i++)
            {
                var portal = portals[i];
                int entryY = listStartY + i * ENTRY_HEIGHT - _scrollOffset;
                if (entryY + ENTRY_HEIGHT < listStartY || entryY > listStartY + listHeight) continue;

                Rectangle entryRect = new Rectangle(x + 15, entryY, WINDOW_WIDTH - 45, ENTRY_HEIGHT - 6);
                var key = (portal.TileX, portal.TileY, portal.IsCave);
                bool isRenaming = _renamingKey.HasValue && _renamingKey.Value == key;
                bool isOrigin = portal.TileX == _originTileX && portal.TileY == _originTileY && portal.IsCave == _originIsCave;

                Rectangle favRect = new Rectangle(entryRect.X + 8, entryRect.Y + (ENTRY_HEIGHT - 6 - 20) / 2f, 20, 20);
                Rectangle editRect = new Rectangle(entryRect.X + entryRect.Width - 32, entryRect.Y + 8, 22, 22);
                Rectangle goRect = new Rectangle(entryRect.X + entryRect.Width - 32, entryRect.Y + entryRect.Height - 30, 22, 22);

                bool hoverEntry = Raylib.CheckCollisionPointRec(mousePos, entryRect);
                bool hoverGo = Raylib.CheckCollisionPointRec(mousePos, goRect);

                Color bgColor = isOrigin ? COLOR_ENTRY_ORIGIN : (hoverEntry ? COLOR_ENTRY_HOVER : COLOR_ENTRY_NORMAL);
                Raylib.DrawRectangleRounded(entryRect, 0.15f, 8, bgColor);
                Raylib.DrawRectangleRoundedLines(entryRect, 0.15f, 8, 1, isOrigin ? new Color(120, 220, 180, 160) : COLOR_BORDER);

                // ---- Icône plan (portail stylisé, teinté selon surface/grotte) ----
                Rectangle iconRect = new Rectangle(entryRect.X + 34, entryRect.Y + 8, ENTRY_HEIGHT - 22, ENTRY_HEIGHT - 22);
                Color planColor = portal.IsCave ? COLOR_CAVE_TAG : COLOR_SURFACE_TAG;
                DrawPortalGlyph(iconRect, planColor);

                // ---- Favori ----
                bool hoverFav = Raylib.CheckCollisionPointRec(mousePos, favRect);
                DrawFavoriteMarker(favRect, portal.IsFavorite, hoverFav);

                float textX = iconRect.X + iconRect.Width + 10;

                if (isRenaming)
                {
                    Rectangle inputRect = new Rectangle(textX, entryRect.Y + 8, entryRect.Width - (textX - entryRect.X) - 62, 22);
                    Raylib.DrawRectangleRounded(inputRect, 0.3f, 6, new Color(15, 16, 22, 230));
                    Raylib.DrawRectangleRoundedLines(inputRect, 0.3f, 6, 1, COLOR_ACCENT);
                    TextInput.DrawSingleLine(_renameBuffer, $"portal-rename-{key.TileX}-{key.TileY}-{key.IsCave}", inputRect,
                        13, Color.White, new Color(90, 110, 150, 220), COLOR_ACCENT, 6);

                    Rectangle confirmRect = new Rectangle(entryRect.X + entryRect.Width - 56, entryRect.Y + entryRect.Height / 2f - 11, 22, 22);
                    Rectangle cancelRect = new Rectangle(entryRect.X + entryRect.Width - 30, entryRect.Y + entryRect.Height / 2f - 11, 22, 22);
                    bool hoverConfirm = Raylib.CheckCollisionPointRec(mousePos, confirmRect);
                    bool hoverCancel = Raylib.CheckCollisionPointRec(mousePos, cancelRect);
                    Raylib.DrawRectangleRounded(confirmRect, 0.3f, 6, hoverConfirm ? new Color(80, 180, 100, 255) : new Color(50, 110, 65, 200));
                    Raylib.DrawLineEx(new Vector2(confirmRect.X + 5, confirmRect.Y + 11), new Vector2(confirmRect.X + 9, confirmRect.Y + 16), 2f, Color.White);
                    Raylib.DrawLineEx(new Vector2(confirmRect.X + 9, confirmRect.Y + 16), new Vector2(confirmRect.X + 17, confirmRect.Y + 6), 2f, Color.White);
                    Raylib.DrawRectangleRounded(cancelRect, 0.3f, 6, hoverCancel ? new Color(150, 60, 60, 255) : new Color(90, 45, 45, 200));
                    Raylib.DrawLineEx(new Vector2(cancelRect.X + 6, cancelRect.Y + 6), new Vector2(cancelRect.X + 16, cancelRect.Y + 16), 2f, Color.White);
                    Raylib.DrawLineEx(new Vector2(cancelRect.X + 16, cancelRect.Y + 6), new Vector2(cancelRect.X + 6, cancelRect.Y + 16), 2f, Color.White);
                }
                else
                {
                    FontManager.DrawText(portal.DisplayName, (int)textX, (int)entryRect.Y + 9, 14, Color.White);

                    string planLabel = portal.IsCave ? "Grotte" : "Surface";
                    FontManager.DrawText(planLabel, (int)textX, (int)entryRect.Y + 28, 10, planColor);

                    int planLabelWidth = FontManager.MeasureText(planLabel, 10);
                    string coordLabel = $"({portal.TileX}, {portal.TileY})";
                    FontManager.DrawText(coordLabel, (int)textX + planLabelWidth + 8, (int)entryRect.Y + 28, 10, COLOR_TEXT_DIM);

                    if (isOrigin)
                    {
                        FontManager.DrawText("Vous êtes ici", (int)textX, (int)entryRect.Y + 44, 10, new Color(120, 220, 180, 255));
                    }
                    else
                    {
                        float dx = portal.TileX - playerTileX;
                        float dy = portal.TileY - playerTileY;
                        float distTiles = MathF.Sqrt(dx * dx + dy * dy);
                        string distLabel = distTiles < 1f ? "à proximité" : $"{(int)distTiles} blocs";
                        bool samePlane = portal.IsCave == World.IsUnderground;
                        string planeNote = samePlane ? "" : "  •  autre plan";
                        FontManager.DrawText(distLabel + planeNote, (int)textX, (int)entryRect.Y + 44, 10, COLOR_TEXT_DIM);
                    }

                    // ---- Bouton renommer (crayon) ----
                    bool hoverEdit = Raylib.CheckCollisionPointRec(mousePos, editRect);
                    Raylib.DrawRectangleRounded(editRect, 0.3f, 6, hoverEdit ? new Color(70, 65, 95, 220) : new Color(45, 42, 58, 160));
                    Raylib.DrawLineEx(new Vector2(editRect.X + 5, editRect.Y + 17), new Vector2(editRect.X + 15, editRect.Y + 7), 2f, COLOR_TEXT_DIM);
                    Raylib.DrawLineEx(new Vector2(editRect.X + 15, editRect.Y + 7), new Vector2(editRect.X + 18, editRect.Y + 10), 2f, COLOR_TEXT_DIM);
                    Raylib.DrawLineEx(new Vector2(editRect.X + 18, editRect.Y + 10), new Vector2(editRect.X + 8, editRect.Y + 20), 2f, COLOR_TEXT_DIM);

                    // ---- Bouton téléporter (flèche) ----
                    Raylib.DrawRectangleRounded(goRect, 0.3f, 6, hoverGo ? COLOR_ACCENT : new Color(70, 55, 110, 180));
                    Vector2 c = new Vector2(goRect.X + goRect.Width / 2f, goRect.Y + goRect.Height / 2f);
                    Raylib.DrawLineEx(c + new Vector2(-6, 0), c + new Vector2(5, 0), 2.2f, Color.White);
                    Raylib.DrawLineEx(c + new Vector2(1, -5), c + new Vector2(6, 0), 2.2f, Color.White);
                    Raylib.DrawLineEx(c + new Vector2(1, 5), c + new Vector2(6, 0), 2.2f, Color.White);
                }
            }

            Raylib.EndScissorMode();

            int totalHeight = portals.Count * ENTRY_HEIGHT;
            if (totalHeight > listHeight)
            {
                int scrollbarX = x + WINDOW_WIDTH - SCROLLBAR_WIDTH - 15;
                int scrollbarHeight = Math.Max(30, (int)((float)listHeight / totalHeight * listHeight));
                float scrollPercent = (float)_scrollOffset / Math.Max(1, totalHeight - listHeight);
                int scrollbarY = listStartY + (int)(scrollPercent * (listHeight - scrollbarHeight));

                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, listStartY, SCROLLBAR_WIDTH, listHeight), 0.3f, 6, new Color(35, 33, 44, 200));
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, scrollbarY, SCROLLBAR_WIDTH, scrollbarHeight), 0.3f, 6, COLOR_ACCENT);
            }

            // ---- Message vide / pied de fenêtre ----
            if (_portals.Count == 0)
            {
                string msg = "Aucun portail découvert pour l'instant.";
                int msgWidth = FontManager.MeasureText(msg, 14);
                FontManager.DrawText(msg, x + (WINDOW_WIDTH - msgWidth) / 2, y + WINDOW_HEIGHT / 2 - 10, 14, COLOR_TEXT_DIM);
                string hint = "Placez un Portail et cliquez dessus pour le découvrir.";
                int hintWidth = FontManager.MeasureText(hint, 12);
                FontManager.DrawText(hint, x + (WINDOW_WIDTH - hintWidth) / 2, y + WINDOW_HEIGHT / 2 + 14, 12, new Color(120, 110, 140, 170));
            }
            else if (portals.Count == 0)
            {
                string msg = "Aucun résultat pour cette recherche.";
                int msgWidth = FontManager.MeasureText(msg, 13);
                FontManager.DrawText(msg, x + (WINDOW_WIDTH - msgWidth) / 2, y + WINDOW_HEIGHT / 2, 13, COLOR_TEXT_DIM);
            }
            else
            {
                FontManager.DrawText("Cliquez sur un portail pour vous y téléporter", x + 15, y + WINDOW_HEIGHT - 20, 11, COLOR_TEXT_DIM);
            }
        }

        /// <summary>Icône vectorielle de portail (arche + voile), pour ne dépendre d'aucune texture externe.</summary>
        private static void DrawPortalGlyph(Rectangle rect, Color color)
        {
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float rOuter = rect.Width / 2f;
            float rInner = rOuter * 0.62f;

            Raylib.DrawEllipseLines((int)cx, (int)cy, rOuter, rOuter, new Color(color.R, color.G, color.B, (byte)180));
            Raylib.DrawEllipse((int)cx, (int)cy, rInner, rInner, new Color(color.R, color.G, color.B, (byte)90));
            Raylib.DrawEllipseLines((int)cx, (int)cy, rInner, rInner, color);
        }

        private static void DrawFavoriteMarker(Rectangle rect, bool isFavorite, bool hovered)
        {
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float r = rect.Width / 2f;
            Color fill = isFavorite ? COLOR_FAVORITE : (hovered ? new Color(120, 115, 140, 200) : new Color(70, 66, 84, 160));
            if (isFavorite)
                Raylib.DrawCircle((int)cx, (int)cy, r, new Color(fill.R, fill.G, fill.B, (byte)70));
            Raylib.DrawCircleLines((int)cx, (int)cy, r, fill);
            Raylib.DrawCircle((int)cx, (int)cy, isFavorite ? r * 0.45f : r * 0.28f, fill);
        }
    }
}
