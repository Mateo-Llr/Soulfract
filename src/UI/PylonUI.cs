// PylonUI.cs - Menu de sélection des pylônes pour téléportation
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static class PylonUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = new Vector2(400, 200);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isHoveringTitleBar = false;
        
        private static List<PylonInfo> _pylons = new();
        private static int _scrollOffset = 0;
        private static int _selectedIndex = -1;
        private static bool _isDraggingScrollbar = false;
        private static float _scrollbarDragStartY = 0f;
        private static int _scrollbarDragStartOffset = 0;
        
        private static int WINDOW_WIDTH => UIManager.ScaleInt(380);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(450);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(45);
        private static int ENTRY_HEIGHT => UIManager.ScaleInt(55);
        private static int SCROLLBAR_WIDTH => UIManager.ScaleInt(8);
        
        private static readonly Color COLOR_BG = new Color(25, 28, 35, 240);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_ENTRY_NORMAL = new Color(45, 48, 55, 200);
        private static readonly Color COLOR_ENTRY_HOVER = new Color(65, 68, 75, 200);
        private static readonly Color COLOR_ENTRY_SELECTED = new Color(210, 180, 100, 80);
        
        // Textures
        private static Texture2D _closeIcon;
        private static Texture2D _pylonIcon;
        private static bool _texturesLoaded = false;
        
        public static bool IsOpen => _isOpen;
        
        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;
        
        private static void LoadTextures()
        {
            if (_texturesLoaded) return;
            _closeIcon = LoadTextureOrDefault("assets/gui/close.png");
            _pylonIcon = LoadTextureOrDefault("assets/gui/pylon_icon.png");
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
            if (_pylonIcon.Id != 0) Raylib.UnloadTexture(_pylonIcon);
            _texturesLoaded = false;
        }
        
        public static void Open()
        {
            LoadTextures();
            RefreshPylonList();
            _isOpen = true;
            _scrollOffset = 0;
            _selectedIndex = -1;
        }
        
        public static void Close()
        {
            _isOpen = false;
            UnloadTextures();
            _isDragging = false;
            _isDraggingScrollbar = false;
        }
        
        /// <summary>
        /// Parcourt tous les chunks chargés pour trouver tous les pylônes (ID 105)
        /// </summary>
        private static void RefreshPylonList()
        {
            _pylons.Clear();
            
            // Parcourir les chunks de surface
            foreach (var (chunkX, chunkY, chunkData) in World.GetAllSurfaceChunks())
            {
                AddPylonsFromChunk(chunkData);
            }
            
            // Parcourir les chunks des grottes
            foreach (var (chunkX, chunkY, chunkData) in World.GetAllCaveChunks())
            {
                AddPylonsFromChunk(chunkData);
            }
            
            // Trier par distance au joueur (optionnel)
            Vector2 playerPos = Program.GetPlayerPosition();
            _pylons = _pylons.OrderBy(p => Vector2.DistanceSquared(p.WorldPosition, playerPos)).ToList();
        }
        
        private static void AddPylonsFromChunk(World.ChunkData chunk)
        {
            foreach (var ((x, y), objectId) in chunk.Objects)
            {
                if (objectId == 105) // ID du pylône
                {
                    int ts = Program.TileSize;
                    int height = World.GetHeightAt(x, y);
                    float yOffset = -height * ts / 4;
                    Vector2 worldPos = new Vector2(x * ts + ts / 2f, y * ts + yOffset + ts / 2f);
                    
                    // Trouver un nom (optionnel : utiliser la position ou un nom personnalisé plus tard)
                    string name = $"Pylône ({x}, {y})";
                    
                    _pylons.Add(new PylonInfo
                    {
                        TileX = x,
                        TileY = y,
                        WorldPosition = worldPos,
                        DisplayName = name
                    });
                }
            }
        }
        
        public static void Update()
        {
            if (!_isOpen) return;
            
            Vector2 mousePos = Raylib.GetMousePosition();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            
            // Clamping de la fenêtre
            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + 100, sw - 100);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, sh - 100);
            
            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            
            // Barre de titre pour le drag
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
            
            // Gestion du scroll
            int listStartY = y + HEADER_HEIGHT + 15;
            int listHeight = WINDOW_HEIGHT - HEADER_HEIGHT - 70;
            int totalHeight = _pylons.Count * ENTRY_HEIGHT;
            
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0 && Raylib.CheckCollisionPointRec(mousePos, new Rectangle(x + 10, listStartY, WINDOW_WIDTH - 30, listHeight)))
            {
                _scrollOffset -= (int)wheel * ENTRY_HEIGHT;
                _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, totalHeight - listHeight));
            }
            
            // Gestion de la barre de défilement
            if (totalHeight > listHeight)
            {
                int scrollbarX = x + WINDOW_WIDTH - SCROLLBAR_WIDTH - 15;
                int scrollbarHeight = (int)((float)listHeight / totalHeight * listHeight);
                scrollbarHeight = Math.Max(30, scrollbarHeight);
                float scrollPercent = (float)_scrollOffset / (totalHeight - listHeight);
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
                    float deltaPercent = deltaY / (listHeight - scrollbarHeight);
                    _scrollOffset = _scrollbarDragStartOffset + (int)(deltaPercent * (totalHeight - listHeight));
                    _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, totalHeight - listHeight));
                }
                
                if (_isDraggingScrollbar && Raylib.IsMouseButtonReleased(MouseButton.Left))
                    _isDraggingScrollbar = false;
            }
            
            // Sélection des pylônes
            int firstVisible = _scrollOffset / ENTRY_HEIGHT;
            int lastVisible = Math.Min(_pylons.Count, firstVisible + (listHeight / ENTRY_HEIGHT) + 1);
            
            for (int i = firstVisible; i < lastVisible; i++)
            {
                int entryY = listStartY + i * ENTRY_HEIGHT - _scrollOffset;
                if (entryY + ENTRY_HEIGHT < listStartY || entryY > listStartY + listHeight) continue;
                
                Rectangle entryRect = new Rectangle(x + 15, entryY, WINDOW_WIDTH - 45, ENTRY_HEIGHT - 5);
                bool isHovered = Raylib.CheckCollisionPointRec(mousePos, entryRect);
                bool isSelected = (i == _selectedIndex);
                
                Color bgColor = isSelected ? COLOR_ENTRY_SELECTED : (isHovered ? COLOR_ENTRY_HOVER : COLOR_ENTRY_NORMAL);
                Raylib.DrawRectangleRounded(entryRect, 0.1f, 8, bgColor);
                Raylib.DrawRectangleRoundedLines(entryRect, 0.1f, 8, 1, COLOR_BORDER);
                
                // Icône
                if (_pylonIcon.Id != 0)
                {
                    float iconSize = 32;
                    Raylib.DrawTexturePro(_pylonIcon,
                        new Rectangle(0, 0, _pylonIcon.Width, _pylonIcon.Height),
                        new Rectangle(entryRect.X + 8, entryRect.Y + (ENTRY_HEIGHT - iconSize) / 2, iconSize, iconSize),
                        Vector2.Zero, 0, Color.White);
                }
                
                // Nom et coordonnées
                var pylon = _pylons[i];
                string displayName = pylon.DisplayName;
                FontManager.DrawText(displayName, (int)entryRect.X + 50, (int)entryRect.Y + 12, 14, Color.White);
                FontManager.DrawText($"({pylon.TileX}, {pylon.TileY})", (int)entryRect.X + 50, (int)entryRect.Y + 32, 11, new Color(150, 150, 140, 200));
                
                // Téléportation au clic gauche
                if (isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _selectedIndex = i;
                    TeleportToPylon(pylon);
                    Close();
                    break;
                }
            }
            
            // Fermeture
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                Close();
        }
        
        private static void TeleportToPylon(PylonInfo pylon)
        {
            Vector2 targetPos = pylon.WorldPosition;
            
            // Ajuster la position pour éviter d'être dans le mur
            int ts = Program.TileSize;
            int tileX = (int)(targetPos.X / ts);
            int tileY = (int)(targetPos.Y / ts);
            int height = World.GetHeightAt(tileX, tileY);
            float yOffset = -height * ts / 4;
            
            // Placer le joueur légèrement au-dessus du sol
            float playerY = tileY * ts + yOffset + ts / 2f;
            targetPos = new Vector2(tileX * ts + ts / 2f, playerY - Program.FeetOffsetY);
            
            Program.SetPlayerPosition(targetPos);
            Program.AddNotification(new Notification($" Téléporté vers {pylon.DisplayName}", new Color(100, 200, 255, 255), 2.5f));
            Program.ApplyScreenShake(4f, 0.2f);
        }
        
        public static void Draw()
        {
            if (!_isOpen) return;
            
            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            Vector2 mousePos = Raylib.GetMousePosition();
            
            // Panneau principal
            Raylib.DrawRectangleRounded(new Rectangle(x + 4, y + 4, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, new Color(0, 0, 0, 100));
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, COLOR_BG);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, 2, COLOR_BORDER);
            
            // Barre de titre
            Color titleColor = _isHoveringTitleBar ? new Color(80, 70, 50, 200) : COLOR_HEADER;
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, titleColor);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 8, 1, COLOR_ACCENT);
            
            // Titre
            string title = " TÉLÉPORTATION ";
            int titleWidth = FontManager.MeasureText(title, 18);
            FontManager.DrawText(title, x + (WINDOW_WIDTH - titleWidth) / 2, y + 13, 18, COLOR_ACCENT);
            
            // Bouton fermer
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 35, y + 10, 25, 25);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            if (_closeIcon.Id != 0)
                Raylib.DrawTexturePro(_closeIcon, new Rectangle(0, 0, _closeIcon.Width, _closeIcon.Height),
                    new Rectangle(closeBtn.X + 4, closeBtn.Y + 4, 17, 17), Vector2.Zero, 0, hoverClose ? Color.Red : new Color(200, 120, 120, 200));
            if (hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
                Close();
            
            // Zone de liste
            int listStartY = y + HEADER_HEIGHT + 15;
            int listHeight = WINDOW_HEIGHT - HEADER_HEIGHT - 70;
            
            // Découpage pour le scrolling (scissor)
            Raylib.BeginScissorMode(x + 10, listStartY, WINDOW_WIDTH - 30, listHeight);
            
            int firstVisible = _scrollOffset / ENTRY_HEIGHT;
            int lastVisible = Math.Min(_pylons.Count, firstVisible + (listHeight / ENTRY_HEIGHT) + 1);
            
            for (int i = firstVisible; i < lastVisible; i++)
            {
                int entryY = listStartY + i * ENTRY_HEIGHT - _scrollOffset;
                if (entryY + ENTRY_HEIGHT < listStartY || entryY > listStartY + listHeight) continue;
                
                Rectangle entryRect = new Rectangle(x + 15, entryY, WINDOW_WIDTH - 45, ENTRY_HEIGHT - 5);
                bool isHovered = Raylib.CheckCollisionPointRec(mousePos, entryRect);
                bool isSelected = (i == _selectedIndex);
                
                Color bgColor = isSelected ? COLOR_ENTRY_SELECTED : (isHovered ? COLOR_ENTRY_HOVER : COLOR_ENTRY_NORMAL);
                Raylib.DrawRectangleRounded(entryRect, 0.1f, 8, bgColor);
                Raylib.DrawRectangleRoundedLines(entryRect, 0.1f, 8, 1, COLOR_BORDER);
                
                // Icône
                if (_pylonIcon.Id != 0)
                {
                    float iconSize = 32;
                    Raylib.DrawTexturePro(_pylonIcon,
                        new Rectangle(0, 0, _pylonIcon.Width, _pylonIcon.Height),
                        new Rectangle(entryRect.X + 8, entryRect.Y + (ENTRY_HEIGHT - iconSize) / 2, iconSize, iconSize),
                        Vector2.Zero, 0, Color.White);
                }
                
                // Nom et coordonnées
                var pylon = _pylons[i];
                FontManager.DrawText(pylon.DisplayName, (int)entryRect.X + 50, (int)entryRect.Y + 12, 14, Color.White);
                FontManager.DrawText($"({pylon.TileX}, {pylon.TileY})", (int)entryRect.X + 50, (int)entryRect.Y + 32, 11, new Color(150, 150, 140, 200));
            }
            
            Raylib.EndScissorMode();
            
            // Barre de défilement (si nécessaire)
            int totalHeight = _pylons.Count * ENTRY_HEIGHT;
            if (totalHeight > listHeight)
            {
                int scrollbarX = x + WINDOW_WIDTH - SCROLLBAR_WIDTH - 15;
                int scrollbarHeight = (int)((float)listHeight / totalHeight * listHeight);
                scrollbarHeight = Math.Max(30, scrollbarHeight);
                float scrollPercent = (float)_scrollOffset / (totalHeight - listHeight);
                int scrollbarY = listStartY + (int)(scrollPercent * (listHeight - scrollbarHeight));
                
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, listStartY, SCROLLBAR_WIDTH, listHeight), 0.2f, 6, new Color(40, 43, 48, 200));
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, scrollbarY, SCROLLBAR_WIDTH, scrollbarHeight), 0.2f, 6, COLOR_ACCENT);
            }
            
            // Message si aucun pylône
            if (_pylons.Count == 0)
            {
                string msg = "Aucun pylône trouvé dans le monde.";
                int msgWidth = FontManager.MeasureText(msg, 14);
                FontManager.DrawText(msg, x + (WINDOW_WIDTH - msgWidth) / 2, y + HEADER_HEIGHT + 100, 14, new Color(150, 140, 110, 200));
                FontManager.DrawText("Placez-en avec l'item 'Pylone' (ID 187)", x + (WINDOW_WIDTH - 210) / 2, y + HEADER_HEIGHT + 130, 12, new Color(120, 110, 90, 160));
            }
            else
            {
                FontManager.DrawText("Cliquez sur un pylône pour vous y téléporter", x + 15, y + WINDOW_HEIGHT - 20, 11, new Color(150, 140, 110, 180));
            }
        }
        
        private class PylonInfo
        {
            public int TileX;
            public int TileY;
            public Vector2 WorldPosition;
            public string DisplayName;
        }
    }
}