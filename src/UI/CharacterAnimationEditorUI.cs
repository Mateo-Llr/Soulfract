using System.Numerics;
using System.Text.Json;
using Raylib_cs;

namespace Soulfract
{
    public static class CharacterAnimationEditorUI
    {
        private const string SavePath = "Data/character_animations.json";
        private const float PreviewScale = 1.8f;
        private static readonly Color Background = new(13, 19, 27, 255);
        private static readonly Color Panel = new(22, 31, 42, 255);
        private static readonly Color PanelRaised = new(31, 43, 56, 255);
        private static readonly Color Accent = new(226, 184, 99, 255);
        private static readonly Color Muted = new(143, 160, 175, 255);
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly List<string> AnimationNames = new();
        private static readonly List<string> DeletedNames = new();
        private static bool _initialized;
        private static bool _isOpen;
        private static bool _isPlaying;
        private static bool _isDraggingPart;
        private static bool _isDraggingRotation;
        private static string _selectedAnimation = "idle";
        private static string _selectedPart = "body";
        private static int _selectedFrame;
        private static float _playbackTimer;
        private static float _animationScroll;
        private static float _timelineScroll;
        private static Vector2 _lastMouse;
        private static float _previewZoom = 1f;
        private static bool _showNameDialog;
        private static bool _renameDialog;
        private static string _nameInput = "";
        private static Rectangle _previewRect;
        private static Rectangle _timelineRect;

        private sealed class SavedFile
        {
            public Dictionary<string, Dictionary<string, List<SavedFrame>>> Animations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> DeletedAnimations { get; set; } = new();
        }

        private sealed class SavedFrame
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Rotation { get; set; }
        }

        public static bool IsOpen => _isOpen;

        public static void LoadSavedAnimations()
        {
            if (!SpeciesData.Skeletons.TryGetValue("human", out var skeleton)) return;
            if (!File.Exists(SavePath)) return;

            try
            {
                var saved = JsonSerializer.Deserialize<SavedFile>(File.ReadAllText(SavePath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (saved == null) return;

                DeletedNames.Clear();
                DeletedNames.AddRange(saved.DeletedAnimations.Where(name => !string.IsNullOrWhiteSpace(name)));
                foreach (string deleted in DeletedNames)
                    foreach (var part in skeleton)
                        part.Animations.Remove(deleted);

                foreach (var animation in saved.Animations)
                {
                    foreach (var part in skeleton)
                    {
                        if (!animation.Value.TryGetValue(part.Name, out var savedFrames) || savedFrames.Count == 0)
                            continue;
                        part.Animations[animation.Key] = savedFrames
                            .Select(frame => new Vector3(frame.X, -frame.Y, frame.Rotation))
                            .ToList();
                    }
                    DeletedNames.RemoveAll(name => string.Equals(name, animation.Key, StringComparison.OrdinalIgnoreCase));
                }

                EntityRenderer.InvalidateAnimationCache("human");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[Animations] Impossible de charger {SavePath}: {exception.Message}");
            }
        }

        public static void Open()
        {
            if (!_initialized) InitializeFromSkeleton();
            _isOpen = true;
            _isPlaying = false;
            _playbackTimer = 0f;
            _showNameDialog = false;
        }

        public static void Draw()
        {
            if (!_isOpen) return;
            if (!SpeciesData.Skeletons.TryGetValue("human", out var skeleton) || skeleton.Count == 0)
            {
                Raylib.ClearBackground(Background);
                FontManager.DrawText("Squelette humain indisponible", 32, 32, 24, Color.White);
                DrawButton(new Rectangle(32, 76, 120, 42), "RETOUR", true);
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), new Rectangle(32, 76, 120, 42)))
                    _isOpen = false;
                return;
            }

            if (!_initialized) InitializeFromSkeleton();
            int screenWidth = Raylib.GetScreenWidth();
            int screenHeight = Raylib.GetScreenHeight();
            Vector2 mouse = Raylib.GetMousePosition();
            if (_showNameDialog) UpdateNameDialog();
            else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                _isOpen = false;
                _isPlaying = false;
                SaveAnimations();
                return;
            }

            if (_isPlaying && !_showNameDialog)
            {
                _playbackTimer += Raylib.GetFrameTime() * GetPlaybackFrameRate();
                int frameCount = GetFrameCount(skeleton, _selectedAnimation);
                if (_playbackTimer >= 1f)
                {
                    int steps = (int)_playbackTimer;
                    _playbackTimer -= steps;
                    _selectedFrame = (_selectedFrame + steps) % Math.Max(1, frameCount);
                }
            }

            Raylib.ClearBackground(Background);
            DrawHeader(screenWidth, mouse);
            DrawAnimationList(skeleton, screenHeight, mouse);
            DrawPreview(skeleton, screenWidth, screenHeight, mouse);
            DrawProperties(skeleton, screenWidth, screenHeight, mouse);
            DrawTimeline(skeleton, screenWidth, screenHeight, mouse);
            if (_showNameDialog) DrawNameDialog(screenWidth, screenHeight, mouse);
        }

        private static void InitializeFromSkeleton()
        {
            if (!SpeciesData.Skeletons.TryGetValue("human", out var skeleton)) return;
            AnimationNames.Clear();
            AnimationNames.AddRange(skeleton.SelectMany(part => part.Animations.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !DeletedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            if (AnimationNames.Count == 0)
            {
                AnimationNames.Add("idle");
                foreach (var part in skeleton) part.Animations["idle"] = new List<Vector3> { Vector3.Zero };
            }
            _selectedAnimation = AnimationNames.Contains("idle", StringComparer.OrdinalIgnoreCase)
                ? AnimationNames.First(name => string.Equals(name, "idle", StringComparison.OrdinalIgnoreCase))
                : AnimationNames[0];
            if (!skeleton.Any(part => part.Name == _selectedPart)) _selectedPart = skeleton[0].Name;
            _selectedFrame = 0;
            _initialized = true;
        }

        private static void DrawHeader(int sw, Vector2 mouse)
        {
            Raylib.DrawRectangle(0, 0, sw, 66, new Color(19, 28, 38, 255));
            FontManager.DrawText("ATELIER D'ANIMATION", 22, 15, 24, Color.White);
            FontManager.DrawText($"HUMAIN  /  {_selectedAnimation.ToUpperInvariant()}  /  FRAME {_selectedFrame + 1}", 276, 24, 14, Muted);

            int x = Math.Max(550, sw - 562);
            DrawHeaderButton(new Rectangle(x, 14, 82, 38), "NOUVELLE", mouse, () => BeginNameDialog(false));
            DrawHeaderButton(new Rectangle(x + 88, 14, 94, 38), "RENOMMER", mouse, () => BeginNameDialog(true));
            DrawHeaderButton(new Rectangle(x + 188, 14, 90, 38), "SUPPRIMER", mouse, DeleteAnimation);
            DrawHeaderButton(new Rectangle(x + 284, 14, 78, 38), _isPlaying ? "PAUSE" : "APERÇU", mouse, () => _isPlaying = !_isPlaying);
            DrawHeaderButton(new Rectangle(x + 368, 14, 74, 38), "SAUVER", mouse, SaveAnimations);
            DrawHeaderButton(new Rectangle(x + 448, 14, 92, 38), "RETOUR", mouse, () =>
            {
                SaveAnimations();
                _isOpen = false;
                _isPlaying = false;
            });
        }

        private static void DrawHeaderButton(Rectangle rect, string label, Vector2 mouse, Action action)
        {
            bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);
            DrawButton(rect, label, hovered);
            if (!_showNameDialog && hovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) action();
        }

        private static void DrawAnimationList(List<AnimalBodyPart> skeleton, int sh, Vector2 mouse)
        {
            Rectangle panel = new(16, 82, 220, Math.Max(190, sh - 465));
            Raylib.DrawRectangleRounded(panel, 0.025f, 4, Panel);
            DrawSectionTitle("ANIMATIONS", 30, 97);
            int rowHeight = 27;
            int listTop = 126;
            int visibleRows = Math.Max(1, (int)((panel.Height - 65) / rowHeight));
            int selectedIndex = Math.Max(0, AnimationNames.FindIndex(name => name == _selectedAnimation));
            if (selectedIndex < _animationScroll) _animationScroll = selectedIndex;
            if (selectedIndex >= _animationScroll + visibleRows) _animationScroll = selectedIndex - visibleRows + 1;
            _animationScroll = Math.Clamp(_animationScroll, 0, Math.Max(0, AnimationNames.Count - visibleRows));
            Raylib.BeginScissorMode((int)panel.X + 7, listTop, (int)panel.Width - 14, (int)panel.Height - 42);
            for (int index = (int)_animationScroll; index < Math.Min(AnimationNames.Count, _animationScroll + visibleRows); index++)
            {
                Rectangle row = new(panel.X + 8, listTop + (index - _animationScroll) * rowHeight, panel.Width - 16, rowHeight - 2);
                bool selected = AnimationNames[index] == _selectedAnimation;
                if (selected || Raylib.CheckCollisionPointRec(mouse, row))
                    Raylib.DrawRectangleRounded(row, 0.05f, 4, selected ? PanelRaised : new Color(35, 47, 60, 255));
                FontManager.DrawText(AnimationNames[index], (int)row.X + 9, (int)row.Y + 5, 14, selected ? Accent : Color.White);
                if (!_showNameDialog && Raylib.CheckCollisionPointRec(mouse, row) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _selectedAnimation = AnimationNames[index];
                    _selectedFrame = 0;
                    _playbackTimer = 0f;
                    _isPlaying = false;
                }
            }
            Raylib.EndScissorMode();
            if (AnimationNames.Count > visibleRows && Raylib.CheckCollisionPointRec(mouse, panel))
                _animationScroll = Math.Clamp(_animationScroll - Raylib.GetMouseWheelMove(), 0, AnimationNames.Count - visibleRows);

            float bonePanelY = panel.Y + panel.Height + 10;
            Rectangle bonePanel = new(16, bonePanelY, 220, Math.Max(100, sh - bonePanelY - 225));
            Raylib.DrawRectangleRounded(bonePanel, 0.025f, 4, Panel);
            DrawSectionTitle("SQUELETTE", 30, (int)bonePanel.Y + 14);
            int boneTop = (int)bonePanel.Y + 42;
            int boneHeight = Math.Max(18, Math.Min(23, (int)(bonePanel.Height - 48) / Math.Max(1, skeleton.Count)));
            for (int index = 0; index < skeleton.Count; index++)
            {
                AnimalBodyPart part = skeleton[index];
                Rectangle row = new(bonePanel.X + 8, boneTop + index * boneHeight, bonePanel.Width - 16, boneHeight - 1);
                bool selected = part.Name == _selectedPart;
                if (selected || Raylib.CheckCollisionPointRec(mouse, row))
                    Raylib.DrawRectangleRounded(row, 0.05f, 3, selected ? PanelRaised : new Color(35, 47, 60, 255));
                FontManager.DrawText(part.Name, (int)row.X + 8, (int)row.Y + 2, 13, selected ? Accent : Color.White);
                if (!_showNameDialog && Raylib.CheckCollisionPointRec(mouse, row) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    _selectedPart = part.Name;
            }
        }

        private static void DrawPreview(List<AnimalBodyPart> skeleton, int sw, int sh, Vector2 mouse)
        {
            int timelineTop = sh - 212;
            _previewRect = new Rectangle(250, 82, Math.Max(260, sw - 570), Math.Max(220, timelineTop - 100));
            Raylib.DrawRectangleRounded(_previewRect, 0.015f, 4, Panel);
            FontManager.DrawText("APERÇU  ·  GLISSER POUR DÉPLACER  ·  MOLETTE POUR ZOOM", (int)_previewRect.X + 16, (int)_previewRect.Y + 13, 13, Muted);
            Vector2 previewPosition = new(_previewRect.X + _previewRect.Width / 2f, _previewRect.Y + _previewRect.Height * 0.65f);
            float drawScale = PreviewScale * _previewZoom;
            Raylib.DrawLine((int)(_previewRect.X + 18), (int)(previewPosition.Y + 92 * drawScale), (int)(_previewRect.X + _previewRect.Width - 18), (int)(previewPosition.Y + 92 * drawScale), new Color(51, 65, 76, 255));

            Raylib.BeginScissorMode((int)_previewRect.X + 2, (int)_previewRect.Y + 38,
                (int)_previewRect.Width - 4, (int)_previewRect.Height - 42);
            EntityRenderer.DrawEntity(
                speciesName: "human", anim: _selectedAnimation, frame: _selectedFrame, prog: _playbackTimer,
                facing: 1f, pos: previewPosition, tint: Color.White,
                hBase: default, hOverlay: default, hColor: Color.White,
                skeletons: SpeciesData.Skeletons, customScale: drawScale, equipment: new Equipment());

            var selectedPart = skeleton.FirstOrDefault(part => part.Name == _selectedPart);
            if (selectedPart != null)
            {
                var (partPosition, partRotation) = EntityRenderer.GetGlobalPartTransform(
                    "human", _selectedAnimation, _selectedFrame, 0f, 1f, previewPosition,
                    SpeciesData.Skeletons, selectedPart.Name, customScale: drawScale);
                float partScale = drawScale * (SpeciesData.Species.GetValueOrDefault("human")?.Scale ?? 1f);
                float halfWidth = Math.Max(9f, selectedPart.Texture.Width * partScale * 0.58f);
                float halfHeight = Math.Max(9f, selectedPart.Texture.Height * partScale * 0.58f);
                Rectangle outline = new(partPosition.X - halfWidth, partPosition.Y - halfHeight, halfWidth * 2, halfHeight * 2);
                Raylib.DrawRectangleLinesEx(outline, 2f, Accent);
                Raylib.DrawCircle((int)partPosition.X, (int)partPosition.Y, 4f, Accent);

                if (!_showNameDialog && Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mouse, _previewRect))
                {
                    var hitPart = FindPartAt(skeleton, mouse, previewPosition, drawScale);
                    if (hitPart != null)
                    {
                        _selectedPart = hitPart.Name;
                        selectedPart = hitPart;
                        _isDraggingPart = true;
                        _lastMouse = mouse;
                    }
                }
                if (Raylib.IsMouseButtonReleased(MouseButton.Left) && _isDraggingPart)
                {
                    _isDraggingPart = false;
                    SaveAnimations();
                }
                if (_isDraggingPart && Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    Vector2 delta = mouse - _lastMouse;
                    if (delta.LengthSquared() > 0f)
                    {
                        var frame = GetKeyframe(selectedPart, _selectedAnimation, _selectedFrame);
                        frame.X += delta.X / drawScale;
                        frame.Y -= delta.Y / drawScale;
                        _lastMouse = mouse;
                    }
                }
            }
            Raylib.EndScissorMode();
            if (Raylib.CheckCollisionPointRec(mouse, _previewRect) && Raylib.GetMouseWheelMove() != 0f)
                _previewZoom = Math.Clamp(_previewZoom + Raylib.GetMouseWheelMove() * 0.08f, 0.6f, 2.2f);
        }

        private static AnimalBodyPart? FindPartAt(List<AnimalBodyPart> skeleton, Vector2 mouse, Vector2 previewPosition, float scale)
        {
            for (int index = skeleton.Count - 1; index >= 0; index--)
            {
                AnimalBodyPart part = skeleton[index];
                if (part.Texture.Id == 0) continue;
                var (position, _) = EntityRenderer.GetGlobalPartTransform("human", _selectedAnimation, _selectedFrame,
                    0f, 1f, previewPosition, SpeciesData.Skeletons, part.Name, customScale: scale);
                float width = Math.Max(12f, part.Texture.Width * scale * 0.5f);
                float height = Math.Max(12f, part.Texture.Height * scale * 0.5f);
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(position.X - width, position.Y - height, width * 2, height * 2)))
                    return part;
            }
            return null;
        }

        private static void DrawProperties(List<AnimalBodyPart> skeleton, int sw, int sh, Vector2 mouse)
        {
            Rectangle panel = new(sw - 300, 82, 284, Math.Max(260, sh - 310));
            Raylib.DrawRectangleRounded(panel, 0.025f, 4, Panel);
            DrawSectionTitle("TRANSFORMER", (int)panel.X + 16, (int)panel.Y + 16);
            FontManager.DrawText(_selectedPart.ToUpperInvariant(), (int)panel.X + 16, (int)panel.Y + 48, 19, Accent);

            AnimalBodyPart? part = skeleton.FirstOrDefault(item => item.Name == _selectedPart);
            if (part == null) return;
            Vector3 pose = GetKeyframe(part, _selectedAnimation, _selectedFrame);
            int propertyY = (int)panel.Y + 96;
            DrawStepper(panel, "POSITION X", pose.X, propertyY, mouse,
                value => SetComponent(part, 0, value), -1f, 1f);
            DrawStepper(panel, "POSITION Y", -pose.Y, propertyY + 70, mouse,
                value => SetComponent(part, 1, -value), -1f, 1f);
            DrawStepper(panel, "ROTATION", pose.Z, propertyY + 140, mouse,
                value => SetComponent(part, 2, value), -5f, 5f);

            int sliderY = propertyY + 218;
            FontManager.DrawText($"ANGLE  {pose.Z:0}°", (int)panel.X + 16, sliderY - 22, 14, Color.White);
            Rectangle slider = new(panel.X + 16, sliderY, panel.Width - 32, 16);
            Raylib.DrawRectangleRounded(slider, 0.5f, 6, new Color(47, 59, 71, 255));
            float normalized = Math.Clamp((pose.Z + 180f) / 360f, 0f, 1f);
            Raylib.DrawRectangleRounded(new Rectangle(slider.X, slider.Y, slider.Width * normalized, slider.Height), 0.5f, 6, Accent);
            Raylib.DrawCircle((int)(slider.X + slider.Width * normalized), (int)(slider.Y + slider.Height / 2), 7, Color.White);
            if (!_showNameDialog && Raylib.CheckCollisionPointRec(mouse, slider) && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                float angle = (mouse.X - slider.X) / slider.Width * 360f - 180f;
                SetComponent(part, 2, MathF.Round(angle), persist: false);
                _isDraggingRotation = true;
            }
            if (Raylib.IsMouseButtonReleased(MouseButton.Left) && _isDraggingRotation)
            {
                _isDraggingRotation = false;
                SaveAnimations();
            }

            FontManager.DrawText("PARENT", (int)panel.X + 16, sliderY + 38, 12, Muted);
            FontManager.DrawText(string.IsNullOrEmpty(part.ParentName) ? "racine" : part.ParentName,
                (int)panel.X + 16, sliderY + 57, 14, Color.White);
        }

        private static void DrawStepper(Rectangle panel, string label, float value, int y, Vector2 mouse,
            Action<float> setValue, float decrease, float increase)
        {
            FontManager.DrawText(label, (int)panel.X + 16, y, 13, Muted);
            int valueTextWidth = FontManager.MeasureText(value.ToString("0.0"), 18);
            FontManager.DrawText(value.ToString("0.0"), (int)(panel.X + panel.Width - 60 - valueTextWidth / 2f), y + 23, 18, Color.White);
            Rectangle minus = new(panel.X + 16, y + 19, 42, 34);
            Rectangle plus = new(panel.X + panel.Width - 58, y + 19, 42, 34);
            DrawButton(minus, "-", Raylib.CheckCollisionPointRec(mouse, minus));
            DrawButton(plus, "+", Raylib.CheckCollisionPointRec(mouse, plus));
            if (!_showNameDialog && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if (Raylib.CheckCollisionPointRec(mouse, minus)) setValue(value + decrease);
                else if (Raylib.CheckCollisionPointRec(mouse, plus)) setValue(value + increase);
            }
        }

        private static void DrawTimeline(List<AnimalBodyPart> skeleton, int sw, int sh, Vector2 mouse)
        {
            _timelineRect = new Rectangle(16, sh - 212, sw - 32, 196);
            Raylib.DrawRectangleRounded(_timelineRect, 0.02f, 4, Panel);
            FontManager.DrawText("PISTE DE KEYFRAMES", (int)_timelineRect.X + 14, (int)_timelineRect.Y + 9, 14, Color.White);

            int controlX = (int)_timelineRect.X + 220;
            DrawTimelineButton(new Rectangle(controlX, _timelineRect.Y + 5, 86, 25), "+ FRAME", mouse, AddFrame);
            DrawTimelineButton(new Rectangle(controlX + 92, _timelineRect.Y + 5, 102, 25), "DUPLIQUER", mouse, DuplicateFrame);
            DrawTimelineButton(new Rectangle(controlX + 200, _timelineRect.Y + 5, 84, 25), "SUPPRIMER", mouse, DeleteFrame);
            int frameCount = GetFrameCount(skeleton, _selectedAnimation);
            FontManager.DrawText($"{_selectedFrame + 1} / {frameCount}", (int)(_timelineRect.X + _timelineRect.Width - 95), (int)_timelineRect.Y + 11, 13, Accent);

            float labelWidth = 128f;
            float cellWidth = 36f;
            float lanesTop = _timelineRect.Y + 36;
            float laneHeight = Math.Min(14f, (_timelineRect.Height - 43) / Math.Max(1, skeleton.Count));
            float trackWidth = Math.Max(0f, _timelineRect.Width - labelWidth - 12);
            float maxScroll = Math.Max(0f, frameCount * cellWidth - trackWidth);
            if (Raylib.CheckCollisionPointRec(mouse, _timelineRect) && Raylib.GetMouseWheelMove() != 0f)
                _timelineScroll = Math.Clamp(_timelineScroll - Raylib.GetMouseWheelMove() * cellWidth, 0f, maxScroll);
            _timelineScroll = Math.Clamp(_timelineScroll, 0f, maxScroll);

            Raylib.BeginScissorMode((int)(_timelineRect.X + labelWidth), (int)lanesTop,
                (int)trackWidth, (int)(_timelineRect.Height - 39));
            for (int partIndex = 0; partIndex < skeleton.Count; partIndex++)
            {
                AnimalBodyPart part = skeleton[partIndex];
                float y = lanesTop + partIndex * laneHeight;
                bool selectedPart = part.Name == _selectedPart;
                if (selectedPart)
                    Raylib.DrawRectangle((int)_timelineRect.X + 6, (int)y, (int)labelWidth - 8, Math.Max(1, (int)laneHeight), new Color(44, 57, 69, 255));
                FontManager.DrawText(part.Name, (int)_timelineRect.X + 12, (int)y + 1, 11, selectedPart ? Accent : Muted);
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    float x = _timelineRect.X + labelWidth + frameIndex * cellWidth - _timelineScroll;
                    Rectangle cell = new(x, y, cellWidth - 2, Math.Max(1, laneHeight - 1));
                    bool selectedCell = frameIndex == _selectedFrame && selectedPart;
                    if (selectedCell)
                        Raylib.DrawRectangle((int)cell.X, (int)cell.Y, (int)cell.Width, Math.Max(1, (int)cell.Height), new Color(61, 75, 86, 255));
                    Raylib.DrawCircle((int)(cell.X + cell.Width / 2), (int)(cell.Y + cell.Height / 2), selectedCell ? 4f : 2.5f,
                        selectedCell ? Accent : new Color(120, 137, 148, 255));
                    if (!_showNameDialog && Raylib.CheckCollisionPointRec(mouse, cell) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        _selectedPart = part.Name;
                        _selectedFrame = frameIndex;
                        _playbackTimer = 0f;
                        _isPlaying = false;
                    }
                }
            }
            if (_isPlaying && frameCount > 1)
            {
                float playheadX = _timelineRect.X + labelWidth
                    + (_selectedFrame + _playbackTimer) * cellWidth - _timelineScroll;
                Raylib.DrawLine((int)playheadX, (int)lanesTop,
                    (int)playheadX, (int)(lanesTop + skeleton.Count * laneHeight), Accent);
            }
            Raylib.EndScissorMode();
        }

        private static void DrawTimelineButton(Rectangle rect, string label, Vector2 mouse, Action action)
        {
            bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);
            DrawButton(rect, label, hovered);
            if (!_showNameDialog && hovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) action();
        }

        private static void DrawNameDialog(int sw, int sh, Vector2 mouse)
        {
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
            Rectangle dialog = new(sw / 2f - 210, sh / 2f - 92, 420, 184);
            Raylib.DrawRectangleRounded(dialog, 0.025f, 4, PanelRaised);
            FontManager.DrawText(_renameDialog ? "RENOMMER L'ANIMATION" : "NOUVELLE ANIMATION", (int)dialog.X + 22, (int)dialog.Y + 20, 18, Color.White);
            Rectangle input = new(dialog.X + 22, dialog.Y + 58, dialog.Width - 44, 38);
            Raylib.DrawRectangleRounded(input, 0.03f, 4, Background);
            FontManager.DrawText(_nameInput + "_", (int)input.X + 10, (int)input.Y + 10, 16, Accent);
            DrawButton(new Rectangle(dialog.X + 22, dialog.Y + 120, 116, 38), "ANNULER", Raylib.CheckCollisionPointRec(mouse, new Rectangle(dialog.X + 22, dialog.Y + 120, 116, 38)));
            DrawButton(new Rectangle(dialog.X + dialog.Width - 138, dialog.Y + 120, 116, 38), "VALIDER", Raylib.CheckCollisionPointRec(mouse, new Rectangle(dialog.X + dialog.Width - 138, dialog.Y + 120, 116, 38)));
        }

        private static void UpdateNameDialog()
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                _showNameDialog = false;
                return;
            }
            int pressed = Raylib.GetCharPressed();
            while (pressed > 0)
            {
                char character = (char)pressed;
                if (!char.IsControl(character) && _nameInput.Length < 24) _nameInput += character;
                pressed = Raylib.GetCharPressed();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _nameInput.Length > 0)
                _nameInput = _nameInput[..^1];

            if (Raylib.IsKeyPressed(KeyboardKey.Enter)) ConfirmNameDialog();
            Vector2 mouse = Raylib.GetMousePosition();
            float dialogX = Raylib.GetScreenWidth() / 2f - 210;
            float dialogY = Raylib.GetScreenHeight() / 2f - 92;
            Rectangle cancel = new(dialogX + 22, dialogY + 120, 116, 38);
            Rectangle confirm = new(dialogX + 420 - 138, dialogY + 120, 116, 38);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if (Raylib.CheckCollisionPointRec(mouse, cancel)) _showNameDialog = false;
                else if (Raylib.CheckCollisionPointRec(mouse, confirm)) ConfirmNameDialog();
            }
        }

        private static void BeginNameDialog(bool rename)
        {
            _renameDialog = rename;
            _nameInput = rename ? _selectedAnimation : "";
            _showNameDialog = true;
        }

        private static void ConfirmNameDialog()
        {
            string name = _nameInput.Trim();
            if (name.Length == 0 || !SpeciesData.Skeletons.TryGetValue("human", out var skeleton)) return;
            if (AnimationNames.Any(existing => !string.Equals(existing, _selectedAnimation, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))) return;

            if (_renameDialog)
            {
                string oldName = _selectedAnimation;
                foreach (var part in skeleton)
                {
                    if (part.Animations.TryGetValue(oldName, out var frames))
                    {
                        part.Animations[name] = new List<Vector3>(frames);
                        part.Animations.Remove(oldName);
                    }
                }
                DeletedNames.Add(oldName);
                AnimationNames.Remove(oldName);
            }
            else
            {
                foreach (var part in skeleton)
                {
                    int frameCount = GetFrameCount(skeleton, _selectedAnimation);
                    if (part.Animations.TryGetValue(_selectedAnimation, out var source) && source.Count > 0)
                        part.Animations[name] = Enumerable.Range(0, frameCount).Select(index => source[Math.Min(index, source.Count - 1)]).ToList();
                    else
                        part.Animations[name] = Enumerable.Repeat(Vector3.Zero, frameCount).ToList();
                }
            }
            DeletedNames.RemoveAll(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
            AnimationNames.Add(name);
            AnimationNames.Sort(StringComparer.OrdinalIgnoreCase);
            _selectedAnimation = name;
            _selectedFrame = 0;
            _isPlaying = false;
            _showNameDialog = false;
            EntityRenderer.InvalidateAnimationCache("human");
            SaveAnimations();
        }

        private static void DeleteAnimation()
        {
            if (!SpeciesData.Skeletons.TryGetValue("human", out var skeleton) || AnimationNames.Count <= 1) return;
            string deleted = _selectedAnimation;
            foreach (var part in skeleton) part.Animations.Remove(deleted);
            DeletedNames.Add(deleted);
            AnimationNames.Remove(deleted);
            _selectedAnimation = AnimationNames[0];
            _selectedFrame = 0;
            _isPlaying = false;
            EntityRenderer.InvalidateAnimationCache("human");
            SaveAnimations();
        }

        private static void AddFrame()
        {
            if (!SpeciesData.Skeletons.TryGetValue("human", out var skeleton)) return;
            int frameCount = GetFrameCount(skeleton, _selectedAnimation);
            foreach (var part in skeleton)
            {
                var frames = EnsureFrames(part, _selectedAnimation, frameCount);
                frames.Insert(_selectedFrame + 1, frames[_selectedFrame]);
            }
            _selectedFrame++;
            SaveAnimations();
        }

        private static void DuplicateFrame()
        {
            AddFrame();
        }

        private static void DeleteFrame()
        {
            if (!SpeciesData.Skeletons.TryGetValue("human", out var skeleton)) return;
            int frameCount = GetFrameCount(skeleton, _selectedAnimation);
            if (frameCount <= 1) return;
            foreach (var part in skeleton)
            {
                var frames = EnsureFrames(part, _selectedAnimation, frameCount);
                frames.RemoveAt(_selectedFrame);
            }
            _selectedFrame = Math.Min(_selectedFrame, GetFrameCount(skeleton, _selectedAnimation) - 1);
            SaveAnimations();
        }

        private static int GetFrameCount(List<AnimalBodyPart> skeleton, string animation)
        {
            return Math.Max(1, skeleton.Where(part => part.Animations.ContainsKey(animation))
                .Select(part => part.Animations[animation].Count).DefaultIfEmpty(1).Max());
        }

        private static float GetPlaybackFrameRate()
        {
            if (string.Equals(_selectedAnimation, "walk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_selectedAnimation, "run", StringComparison.OrdinalIgnoreCase))
                return 10f;
            if (string.Equals(_selectedAnimation, "sleep", StringComparison.OrdinalIgnoreCase)
                || _selectedAnimation.StartsWith("sit", StringComparison.OrdinalIgnoreCase))
                return 3f;
            return 4f;
        }

        private static List<Vector3> EnsureFrames(AnimalBodyPart part, string animation, int count)
        {
            if (!part.Animations.TryGetValue(animation, out var frames))
            {
                frames = new List<Vector3>();
                part.Animations[animation] = frames;
            }
            while (frames.Count < count) frames.Add(frames.Count > 0 ? frames[^1] : Vector3.Zero);
            return frames;
        }

        private static Vector3 GetKeyframe(AnimalBodyPart part, string animation, int frame)
        {
            var frames = EnsureFrames(part, animation, Math.Max(frame + 1,
                SpeciesData.Skeletons.TryGetValue("human", out var skeleton) ? GetFrameCount(skeleton, animation) : frame + 1));
            return frames[Math.Clamp(frame, 0, frames.Count - 1)];
        }

        private static void SetComponent(AnimalBodyPart part, int component, float value, bool persist = true)
        {
            var frames = GetPartFrames(part);
            Vector3 pose = frames[_selectedFrame];
            if (component == 0) pose.X = value;
            else if (component == 1) pose.Y = value;
            else pose.Z = value;
            frames[_selectedFrame] = pose;
            if (persist) SaveAnimations();
        }

        private static List<Vector3> GetPartFrames(AnimalBodyPart part)
        {
            int count = SpeciesData.Skeletons.TryGetValue("human", out var skeleton) ? GetFrameCount(skeleton, _selectedAnimation) : _selectedFrame + 1;
            return EnsureFrames(part, _selectedAnimation, Math.Max(count, _selectedFrame + 1));
        }

        private static void SaveAnimations()
        {
            if (!SpeciesData.Skeletons.TryGetValue("human", out var skeleton)) return;
            var file = new SavedFile { DeletedAnimations = DeletedNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
            foreach (string animation in AnimationNames)
            {
                var byPart = new Dictionary<string, List<SavedFrame>>(StringComparer.OrdinalIgnoreCase);
                int frameCount = GetFrameCount(skeleton, animation);
                foreach (var part in skeleton)
                {
                    if (!part.Animations.TryGetValue(animation, out var frames) || frames.Count == 0) continue;
                    byPart[part.Name] = Enumerable.Range(0, frameCount).Select(index =>
                    {
                        Vector3 value = frames[Math.Min(index, frames.Count - 1)];
                        return new SavedFrame { X = value.X, Y = -value.Y, Rotation = value.Z };
                    }).ToList();
                }
                file.Animations[animation] = byPart;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                File.WriteAllText(SavePath, JsonSerializer.Serialize(file, JsonOptions));
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[Animations] Impossible de sauvegarder {SavePath}: {exception.Message}");
            }
        }

        private static void DrawSectionTitle(string label, int x, int y)
        {
            FontManager.DrawText(label, x, y, 13, Muted);
        }

        private static void DrawButton(Rectangle rect, string label, bool hovered)
        {
            Raylib.DrawRectangleRounded(rect, 0.04f, 4, hovered ? new Color(57, 74, 89, 255) : PanelRaised);
            Raylib.DrawRectangleRoundedLines(rect, 0.04f, 4, 1f, hovered ? Accent : new Color(58, 72, 84, 255));
            int fontSize = rect.Height < 30 ? 11 : 13;
            int width = FontManager.MeasureText(label, fontSize);
            FontManager.DrawText(label, (int)(rect.X + (rect.Width - width) / 2f),
                (int)(rect.Y + (rect.Height - fontSize) / 2f), fontSize, Color.White);
        }
    }
}