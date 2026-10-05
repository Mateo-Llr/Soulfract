// InstrumentUI.cs - Interface pour les instruments de musique avec visualiseur
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static class InstrumentUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = new Vector2(400, 200);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isHoveringTitleBar = false;

        private static List<string> _musicNames = new();
        private static int _selectedIndex = -1;
        private static int _scrollOffset = 0;
        private static int _maxScroll = 0;
        private static int ITEM_HEIGHT => UIManager.ScaleInt(30);
        private const int LIST_VISIBLE_ROWS = 12;

        private static string _currentPlayingMusic = "";
        private static float _doubleClickTimer = 0f;
        private static int _doubleClickIndex = -1;
        private const float DOUBLE_CLICK_DELAY = 0.3f;

        // Dimensions de la fenêtre
        private static int WINDOW_WIDTH => UIManager.ScaleInt(750);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(550);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(45);
        private static int BUTTON_HEIGHT => UIManager.ScaleInt(36);
        private static int LIST_WIDTH => UIManager.ScaleInt(220);

        // Couleurs
        private static readonly Color COLOR_BG = new Color(25, 28, 35, 240);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_ITEM_NORMAL = new Color(45, 48, 55, 200);
        private static readonly Color COLOR_ITEM_HOVER = new Color(65, 68, 75, 200);
        private static readonly Color COLOR_ITEM_SELECTED = new Color(210, 180, 100, 80);
        private static readonly Color COLOR_PLAYING = new Color(100, 200, 100, 255);
        private static readonly Color COLOR_BUTTON = new Color(80, 140, 80, 255);
        private static readonly Color COLOR_BUTTON_HOVER = new Color(100, 180, 100, 255);
        private static readonly Color COLOR_STOP_BUTTON = new Color(140, 80, 80, 255);
        private static readonly Color COLOR_STOP_BUTTON_HOVER = new Color(180, 100, 100, 255);

        private static Texture2D? _textureNoteLeft;
        private static Texture2D? _textureNoteRight;
        private static Texture2D? _textureNoteMid;
        private static bool _texturesLoaded = false;

        // Couleurs des instruments pour le visualiseur
        private static readonly Color[] _instrumentColors = new Color[]
        {
            new Color(255, 200, 100, 255), // or
            new Color(100, 200, 255, 255), // cyan
            new Color(255, 100, 100, 255), // rouge
            new Color(100, 255, 100, 255), // vert
            new Color(200, 100, 255, 255), // violet
            new Color(255, 150, 200, 255), // rose
            new Color(150, 200, 255, 255),
            new Color(255, 255, 100, 255),
            new Color(100, 255, 255, 255),
            new Color(255, 200, 200, 255),
            new Color(200, 200, 255, 255),
            new Color(200, 255, 200, 255),
        };

        // Variables du visualiseur
        private static List<NoteDisplayInfo> _displayNotes = new();
        private static float _visibleDuration = 8.0f; // secondes visibles
        private static float _pixelsPerSecond = 0f;
        private static float _timeAxisX = 0f; // position x du temps actuel
        private const float TIME_AXIS_RATIO = 0.2f;
        private static int _minPitch = 36;
        private static int _maxPitch = 84;
        private static float _pixelsPerPitch = 0f;

        private static void LoadNoteTextures()
        {
            if (_texturesLoaded) return;
            _textureNoteLeft = Raylib.LoadTexture("assets/gui/note_left.png");
            _textureNoteRight = Raylib.LoadTexture("assets/gui/note_right.png");
            _textureNoteMid = Raylib.LoadTexture("assets/gui/note_mid.png");
            _texturesLoaded = true;
        }

        private static void DrawNoteWithTextures(float x, float y, float width, float height, Color tint)
        {
            if (_textureNoteLeft == null || _textureNoteRight == null || _textureNoteMid == null)
                return;

            Texture2D textureLeft = _textureNoteLeft.Value;
            Texture2D textureRight = _textureNoteRight.Value;
            Texture2D textureMid = _textureNoteMid.Value;

            float leftW = textureLeft.Width;
            float rightW = textureRight.Width;

            if (width < leftW + rightW)
            {
                float midW = Math.Min(width, textureMid.Width);
                float centerX = x + (width - midW) / 2;
                Rectangle dest = new Rectangle(centerX, y, midW, height);
                Rectangle src = new Rectangle(0, 0, textureMid.Width, textureMid.Height);
                Raylib.DrawTexturePro(textureMid, src, dest, Vector2.Zero, 0f, tint);
                return;
            }

            Rectangle destLeft = new Rectangle(x, y, leftW, height);
            Rectangle srcLeft = new Rectangle(0, 0, textureLeft.Width, textureLeft.Height);
            Raylib.DrawTexturePro(textureLeft, srcLeft, destLeft, Vector2.Zero, 0f, tint);

            Rectangle destRight = new Rectangle(x + width - rightW, y, rightW, height);
            Rectangle srcRight = new Rectangle(0, 0, textureRight.Width, textureRight.Height);
            Raylib.DrawTexturePro(textureRight, srcRight, destRight, Vector2.Zero, 0f, tint);

            float midX = x + leftW;
            float midWidth = width - leftW - rightW;
            if (midWidth > 0)
            {
                Rectangle destMid = new Rectangle(midX, y, midWidth, height);
                Rectangle srcMid = new Rectangle(0, 0, textureMid.Width, textureMid.Height);
                Raylib.DrawTexturePro(textureMid, srcMid, destMid, Vector2.Zero, 0f, tint);
            }
        }

        public static bool IsOpen => _isOpen;
        public static int GetWindowX() => (int)_windowPos.X;
        public static int GetWindowY() => (int)_windowPos.Y;
        public static int GetWindowWidth() => WINDOW_WIDTH;
        public static int GetWindowHeight() => WINDOW_HEIGHT;

        public static void Open()
        {
            if (_isOpen) return;
            RefreshMusicList();
            _isOpen = true;
            _selectedIndex = -1;
            _scrollOffset = 0;
            _isDragging = false;
            _displayNotes.Clear();
            UIManager.PushUI(Close, () => _isOpen);
        }

        public static void Close()
        {
            _isOpen = false;
            _isDragging = false;
            _displayNotes.Clear();
        }

        private static void RefreshMusicList()
        {
            _musicNames = MusicPlayer.GetAvailableMusicNames();
            _musicNames.Sort(StringComparer.OrdinalIgnoreCase);
            int listHeight = WINDOW_HEIGHT - HEADER_HEIGHT - BUTTON_HEIGHT - 50;
            _maxScroll = Math.Max(0, _musicNames.Count * ITEM_HEIGHT - listHeight);
            if (_selectedIndex >= _musicNames.Count) _selectedIndex = -1;
        }

        public static void Update()
        {
            if (!_isOpen) return;

            Vector2 mousePos = Raylib.GetMousePosition();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            _windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + 100, sw - 100);
            _windowPos.Y = Math.Clamp(_windowPos.Y, 0, sh - 100);

            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;

            // Drag de la fenêtre
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

            // Zone de la liste
            int listX = x + 10;
            int listY = y + HEADER_HEIGHT + 10;
            int listW = LIST_WIDTH - 20;
            int listH = WINDOW_HEIGHT - HEADER_HEIGHT - BUTTON_HEIGHT - 50;

            // Molette sur la liste
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0 && Raylib.CheckCollisionPointRec(mousePos, new Rectangle(listX, listY, listW, listH)))
            {
                _scrollOffset -= (int)wheel * ITEM_HEIGHT;
                _scrollOffset = Math.Clamp(_scrollOffset, 0, _maxScroll);
            }

            // Sélection par clic dans la liste
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, new Rectangle(listX, listY, listW, listH)))
            {
                int firstVisible = _scrollOffset / ITEM_HEIGHT;
                int lastVisible = Math.Min(_musicNames.Count, firstVisible + LIST_VISIBLE_ROWS + 1);
                for (int i = firstVisible; i < lastVisible; i++)
                {
                    int itemY = listY + i * ITEM_HEIGHT - _scrollOffset;
                    if (itemY + ITEM_HEIGHT < listY || itemY > listY + listH) continue;
                    Rectangle itemRect = new Rectangle(listX, itemY, listW, ITEM_HEIGHT - 2);
                    if (Raylib.CheckCollisionPointRec(mousePos, itemRect))
                    {
                        if (_doubleClickIndex == i && Raylib.GetTime() - _doubleClickTimer < DOUBLE_CLICK_DELAY)
                        {
                            PlaySelectedMusic(i);
                            _doubleClickIndex = -1;
                        }
                        else
                        {
                            _selectedIndex = i;
                            _doubleClickIndex = i;
                            _doubleClickTimer = (float)Raylib.GetTime();
                        }
                        break;
                    }
                }
            }

            // Boutons (sous la liste)
            int btnY = listY + listH + 10;
            int btnW = (listW - 10) / 3;
            int btnX1 = listX;
            int btnX2 = listX + btnW + 5;
            int btnX3 = listX + 2 * (btnW + 5);

            Rectangle playBtn = new Rectangle(btnX1, btnY, btnW, BUTTON_HEIGHT);
            Rectangle stopBtn = new Rectangle(btnX2, btnY, btnW, BUTTON_HEIGHT);
            Rectangle refreshBtn = new Rectangle(btnX3, btnY, btnW, BUTTON_HEIGHT);

            if (Raylib.CheckCollisionPointRec(mousePos, playBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if (_selectedIndex >= 0 && _selectedIndex < _musicNames.Count)
                    PlaySelectedMusic(_selectedIndex);
            }

            if (Raylib.CheckCollisionPointRec(mousePos, stopBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                MusicPlayer.Stop();
                _currentPlayingMusic = "";
            }

            if (Raylib.CheckCollisionPointRec(mousePos, refreshBtn) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                RefreshMusicList();
            }

            // Molette sur le visualiseur pour zoom
            int vizX = x + LIST_WIDTH + 15;
            int vizY = y + HEADER_HEIGHT + 10;
            int vizW = WINDOW_WIDTH - LIST_WIDTH - 30;
            int vizH = WINDOW_HEIGHT - HEADER_HEIGHT - 20;
            Rectangle vizRect = new Rectangle(vizX, vizY, vizW, vizH);
            if (Raylib.CheckCollisionPointRec(mousePos, vizRect))
            {
                float wheelViz = Raylib.GetMouseWheelMove();
                if (wheelViz != 0)
                {
                    _visibleDuration = Math.Clamp(_visibleDuration - wheelViz * 0.5f, 2.0f, 30.0f);
                    _pixelsPerSecond = (vizW - 20) / _visibleDuration; // recalcul dans Draw aussi
                }
            }

            // Fermeture si l'instrument n'est plus en main
            Item? held = Program.equipment.MainHand;
            if (held == null || !IsInstrumentItem(held))
            {
                Close();
                UIManager.PopUI();
            }
        }

        private static void PlaySelectedMusic(int index)
        {
            if (index < 0 || index >= _musicNames.Count) return;
            string name = _musicNames[index];
            string path = $"assets/musics/{name}.mid";
            if (!File.Exists(path)) path = $"assets/musics/{name}.midi";
            if (MusicPlayer.LoadMusic(path))
            {
                MusicPlayer.Play();
                _currentPlayingMusic = name;
                // Récupérer les notes pour le visualiseur
                _displayNotes = MusicPlayer.GetNotesForDisplay();
                Program.AddNotification(new Notification($" Lecture : {name}", new Color(100, 200, 255, 255), 2f));
            }
            else
            {
                Program.AddNotification(new Notification($" Impossible de charger {name}", Color.Red, 2f));
            }
        }

        private static bool IsInstrumentItem(Item item)
        {
            if (item == null) return false;
            int itemId = Program.GetItemId(item.Name);
            return GameData.ItemDatabase.TryGetValue(itemId, out var data) && data.IsInstrument;
        }

        public static void Draw()
        {
            if (!_isOpen) return;

            // Charger les textures une fois
            LoadNoteTextures();

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
            FontManager.DrawText(" INSTRUMENT DE MUSIQUE", x + 15, y + 13, 18, Color.White);

            // Bouton fermer
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 35, y + 10, 25, 25);
            bool hoverClose = Raylib.CheckCollisionPointRec(mousePos, closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            FontManager.DrawText("X", (int)closeBtn.X + 8, (int)closeBtn.Y + 5, 16, hoverClose ? Color.Red : new Color(200, 120, 120, 200));
            if (hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Close();
                UIManager.PopUI();
            }

            // --- Colonne de gauche : liste des musiques ---
            int listX = x + 10;
            int listY = y + HEADER_HEIGHT + 10;
            int listW = LIST_WIDTH - 20;
            int listH = WINDOW_HEIGHT - HEADER_HEIGHT - BUTTON_HEIGHT - 50;

            // Fond de la liste
            Raylib.DrawRectangleRounded(new Rectangle(listX, listY, listW, listH), 0.1f, 6, new Color(30, 33, 40, 200));
            Raylib.DrawRectangleRoundedLines(new Rectangle(listX, listY, listW, listH), 0.1f, 6, 1, COLOR_BORDER);

            // Statut de lecture en haut de la liste
            string status = MusicPlayer.IsPlaying ? $"▶ {_currentPlayingMusic}" :
                            (MusicPlayer.IsPaused ? "⏸ Pause" : "⏹ Arrêté");
            FontManager.DrawText(status, listX + 8, listY + 4, 12,
                MusicPlayer.IsPlaying ? COLOR_PLAYING : new Color(180, 180, 160, 200));

            // Contenu de la liste (scissor)
            int contentY = listY + 22;
            int contentH = listH - 22;
            Raylib.BeginScissorMode(listX, contentY, listW, contentH);

            int firstVisible = _scrollOffset / ITEM_HEIGHT;
            int lastVisible = Math.Min(_musicNames.Count, firstVisible + LIST_VISIBLE_ROWS + 1);

            for (int i = firstVisible; i < lastVisible; i++)
            {
                int itemY = contentY + i * ITEM_HEIGHT - _scrollOffset;
                if (itemY + ITEM_HEIGHT < contentY || itemY > contentY + contentH) continue;

                Rectangle itemRect = new Rectangle(listX, itemY, listW, ITEM_HEIGHT - 2);
                bool isHovered = Raylib.CheckCollisionPointRec(mousePos, itemRect);
                bool isSelected = (i == _selectedIndex);
                bool isPlaying = (_currentPlayingMusic == _musicNames[i]);

                Color bgColor;
                if (isPlaying) bgColor = new Color(100, 200, 100, 60);
                else if (isSelected) bgColor = COLOR_ITEM_SELECTED;
                else if (isHovered) bgColor = COLOR_ITEM_HOVER;
                else bgColor = COLOR_ITEM_NORMAL;

                Raylib.DrawRectangleRounded(itemRect, 0.1f, 6, bgColor);
                Raylib.DrawRectangleRoundedLines(itemRect, 0.1f, 6, 1, isPlaying ? COLOR_PLAYING : COLOR_BORDER);

                string displayName = _musicNames[i];
                if (isPlaying) displayName = "▶ " + displayName;
                FontManager.DrawText(displayName, (int)itemRect.X + 8, (int)itemRect.Y + 6, 13,
                    isPlaying ? COLOR_PLAYING : Color.White);
            }

            Raylib.EndScissorMode();

            // Barre de défilement de la liste
            if (_maxScroll > 0)
            {
                int scrollbarX = listX + listW - 6;
                int scrollbarHeight = (int)((float)contentH / (_musicNames.Count * ITEM_HEIGHT) * contentH);
                scrollbarHeight = Math.Max(20, scrollbarHeight);
                float scrollPercent = (float)_scrollOffset / _maxScroll;
                int scrollbarY = contentY + (int)(scrollPercent * (contentH - scrollbarHeight));

                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, contentY, 4, contentH), 0.2f, 6, new Color(40, 43, 48, 200));
                Raylib.DrawRectangleRounded(new Rectangle(scrollbarX, scrollbarY, 4, scrollbarHeight), 0.2f, 6, COLOR_ACCENT);
            }

            // Boutons sous la liste
            int btnY = listY + listH + 10;
            int btnW = (listW - 10) / 3;
            int btnX1 = listX;
            int btnX2 = listX + btnW + 5;
            int btnX3 = listX + 2 * (btnW + 5);

            // Jouer
            Rectangle playBtn = new Rectangle(btnX1, btnY, btnW, BUTTON_HEIGHT);
            bool hoverPlay = Raylib.CheckCollisionPointRec(mousePos, playBtn);
            Color playColor = (_selectedIndex >= 0) ? (hoverPlay ? COLOR_BUTTON_HOVER : COLOR_BUTTON) : new Color(60, 70, 60, 200);
            Raylib.DrawRectangleRounded(playBtn, 0.2f, 6, playColor);
            Raylib.DrawRectangleRoundedLines(playBtn, 0.2f, 6, 1, new Color(100, 200, 100, 150));
            string playText = Localization.Get("ui.instrument_play");
            int playW = FontManager.MeasureText(playText, 13);
            FontManager.DrawText(playText, (int)(btnX1 + (btnW - playW) / 2), (int)(btnY + 10), 13, Color.White);

            // Arrêter
            Rectangle stopBtn = new Rectangle(btnX2, btnY, btnW, BUTTON_HEIGHT);
            bool hoverStop = Raylib.CheckCollisionPointRec(mousePos, stopBtn);
            Color stopColor = hoverStop ? COLOR_STOP_BUTTON_HOVER : COLOR_STOP_BUTTON;
            Raylib.DrawRectangleRounded(stopBtn, 0.2f, 6, stopColor);
            Raylib.DrawRectangleRoundedLines(stopBtn, 0.2f, 6, 1, new Color(200, 100, 100, 150));
            string stopText = "⏹ Arrêter";
            int stopW = FontManager.MeasureText(stopText, 13);
            FontManager.DrawText(stopText, (int)(btnX2 + (btnW - stopW) / 2), (int)(btnY + 10), 13, Color.White);

            // Rafraîchir
            Rectangle refreshBtn = new Rectangle(btnX3, btnY, btnW, BUTTON_HEIGHT);
            bool hoverRefresh = Raylib.CheckCollisionPointRec(mousePos, refreshBtn);
            Color refreshColor = hoverRefresh ? new Color(100, 100, 180, 255) : new Color(70, 70, 120, 200);
            Raylib.DrawRectangleRounded(refreshBtn, 0.2f, 6, refreshColor);
            Raylib.DrawRectangleRoundedLines(refreshBtn, 0.2f, 6, 1, new Color(150, 150, 200, 150));
            string refreshText = "⟳";
            int refreshW = FontManager.MeasureText(refreshText, 16);
            FontManager.DrawText(refreshText, (int)(btnX3 + (btnW - refreshW) / 2), (int)(btnY + 8), 16, Color.White);

            // --- Visualiseur (piano roll) ---
            int vizX = x + LIST_WIDTH + 15;
            int vizY = y + HEADER_HEIGHT + 10;
            int vizW = WINDOW_WIDTH - LIST_WIDTH - 30;
            int vizH = WINDOW_HEIGHT - HEADER_HEIGHT - 20;

            // Fond du visualiseur
            Raylib.DrawRectangleRounded(new Rectangle(vizX, vizY, vizW, vizH), 0.1f, 6, new Color(20, 22, 28, 230));
            Raylib.DrawRectangleRoundedLines(new Rectangle(vizX, vizY, vizW, vizH), 0.1f, 6, 1, COLOR_BORDER);

            // Titre du visualiseur
            string vizTitle = _currentPlayingMusic != "" ? $"Visualiseur : {_currentPlayingMusic}" : "Choisissez une musique";
            FontManager.DrawText(vizTitle, vizX + 10, vizY + 6, 14, new Color(210, 210, 200, 200));

            // Calculs de conversion
            float margin = 10f;
            float vizInnerW = vizW - 2 * margin;
            float vizInnerH = vizH - 2 * margin - 20; // on réserve de la place pour les infos en haut
            float vizInnerY = vizY + margin + 20;

            Raylib.BeginScissorMode(vizX, vizY, vizW, vizH);
            if (_displayNotes.Count > 0)
            {
                // Déterminer la plage de hauteur effective (min/max) ou utiliser la plage fixe
                int minPitch = _displayNotes.Min(n => n.Pitch);
                int maxPitch = _displayNotes.Max(n => n.Pitch);
                minPitch = Math.Min(minPitch, _minPitch);
                maxPitch = Math.Max(maxPitch, _maxPitch);
                // Ajouter une marge
                minPitch = Math.Max(21, minPitch - 2);
                maxPitch = Math.Min(108, maxPitch + 2);
                int pitchRange = maxPitch - minPitch;
                if (pitchRange < 12) pitchRange = 12; // au moins une octave

                _pixelsPerPitch = (vizInnerH - 10) / pitchRange;
                _pixelsPerSecond = vizInnerW / _visibleDuration;

                // Position du temps actuel
                float timeAxisXRel = TIME_AXIS_RATIO * vizInnerW;
                _timeAxisX = vizX + margin + timeAxisXRel;

                LoadNoteTextures();
                // Dessiner les notes
                float currentTime = MusicPlayer.CurrentPlayTime;

                // Trier les notes par temps (elles le sont déjà)
                foreach (var note in _displayNotes)
                {
                    // Calcul de la position X relative au temps actuel
                    float dx = (note.StartTime - currentTime) * _pixelsPerSecond;
                    float xNote = _timeAxisX + dx;
                    // Si la note est complètement en dehors de la zone visible, on skip
                    if (xNote + note.Duration * _pixelsPerSecond < vizX + margin || xNote > vizX + margin + vizInnerW)
                        continue;

                    // Position Y : le pitch le plus bas est en bas, donc y = vizInnerY + vizInnerH - (note.Pitch - minPitch) * pixelsPerPitch - hauteurNote
                    float yNote = vizInnerY + vizInnerH - (note.Pitch - minPitch) * _pixelsPerPitch - 4;
                    float wNote = Math.Max(note.Duration * _pixelsPerSecond, 2f);
                    float hNote = _pixelsPerPitch - 1;
                    if (hNote < 2) hNote = 2;

                    // Couleur par instrument
                    int idx = note.InstrumentIndex % _instrumentColors.Length;
                    Color baseColor = _instrumentColors[idx];
                    // Opacité selon si la note est passée ou future
                    float alpha = 0.5f;
                    if (note.StartTime <= currentTime && currentTime < note.StartTime + note.Duration)
                        alpha = 1.0f; // note en cours
                    else if (note.StartTime > currentTime)
                        alpha = 0.8f; // future
                    else
                        alpha = 0.3f; // passée

                    Color color = new Color(baseColor.R, baseColor.G, baseColor.B, (byte)(alpha * 255));

                    DrawNoteWithTextures(xNote, yNote, wNote, hNote, color);
                }

                // Ligne de temps actuelle
                Raylib.DrawLine((int)_timeAxisX, vizY + 25, (int)_timeAxisX, vizY + vizH - 5, new Color(255, 255, 255, 180));
                // Petite flèche ou marqueur
                FontManager.DrawText("▼", (int)_timeAxisX - 5, vizY + 12, 12, new Color(255, 255, 255, 180));

                // Affichage du temps
                string timeStr = $"T: {currentTime:F1}s / {MusicPlayer.TotalDuration:F1}s";
                FontManager.DrawText(timeStr, vizX + vizW - FontManager.MeasureText(timeStr, 12) - 10, vizY + 8, 12, new Color(200, 200, 200, 200));

                // Graduations de temps (tous les 2 secondes)
                float step = 2.0f;
                for (float t = 0; t <= MusicPlayer.TotalDuration + 2; t += step)
                {
                    float dx = (t - currentTime) * _pixelsPerSecond;
                    float xTick = _timeAxisX + dx;
                    if (xTick < vizX + margin || xTick > vizX + margin + vizInnerW) continue;
                    Raylib.DrawLine((int)xTick, vizY + vizH - 12, (int)xTick, vizY + vizH - 4, new Color(150, 150, 150, 120));
                    if (t % 4 < 0.1f)
                        FontManager.DrawText($"{t:F0}s", (int)xTick - 8, vizY + vizH - 20, 10, new Color(150, 150, 150, 100));
                }

                // Graduations de hauteur (pitch)
                int stepPitch = 12; // une octave
                for (int p = minPitch - (minPitch % 12); p <= maxPitch; p += stepPitch)
                {
                    if (p < minPitch || p > maxPitch) continue;
                    float yPitch = vizInnerY + vizInnerH - (p - minPitch) * _pixelsPerPitch;
                    if (yPitch < vizInnerY || yPitch > vizInnerY + vizInnerH) continue;
                    Raylib.DrawLine(vizX + (int)margin + 2, (int)yPitch, vizX + (int)margin + 12, (int)yPitch, new Color(150, 150, 150, 80));
                    string noteName = GetNoteName(p);
                    FontManager.DrawText(noteName, vizX + (int)margin + 14, (int)yPitch - 6, 10, new Color(200, 200, 200, 120));
                }
            }
            else
            {
                // Aucune note chargée
                string msg = "Aucune note à afficher. Chargez une musique.";
                int msgW = FontManager.MeasureText(msg, 16);
                FontManager.DrawText(msg, vizX + (vizW - msgW) / 2, vizY + (vizH / 2) - 8, 16, new Color(150, 150, 150, 150));
            }

            // Raccourci en bas de la fenêtre
            string hint = "Double-clic pour jouer | Molette sur visualiseur pour zoom | ESC: Fermer";
            int hintW = FontManager.MeasureText(hint, 11);
            FontManager.DrawText(hint, x + (WINDOW_WIDTH - hintW) / 2, y + WINDOW_HEIGHT - 5, 11, new Color(120, 110, 90, 180));
            Raylib.EndScissorMode();
        }

        private static string GetNoteName(int midiPitch)
        {
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            int octave = (midiPitch / 12) - 1;
            int noteIndex = midiPitch % 12;
            return $"{names[noteIndex]}{octave}";
        }
    }
}