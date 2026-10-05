using Raylib_cs;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Soulfract
{
    public static class TamedAnimalInteraction
    {
        private enum RadialAction
        {
            Mount,
            Dismount,
            Follow,
            Roam,
            Stay,
            Attack,
            SlimeStorage
        }

        private struct RadialMenuOption
        {
            public string Name;
            public Color Color;
            public RadialAction Action;
            public string IconFile;
        }

        private static bool _isRadialMenuOpen = false;
        private static Entity _currentAnimal = null!;
        private static float _holdTime = 0f;
        private static bool _isHoldingE = false;
        private static Vector2 _radialCenter = Vector2.Zero;
        private static float _radialRadius = 150f; // rayon extérieur du menu (donut)
        private static float _radialInnerRadius = 54f; // rayon intérieur (trou du donut)
        private static float _sliceGapDegrees = 4f; // espace entre chaque secteur
        private static int _selectedOption = -1;
        private static bool _isCenterHovered = false;

        private const float HOLD_REQUIRED_TIME = 0.6f;

        //  Liste construite dynamiquement à chaque ouverture : ne contient QUE les options
        // réellement disponibles, donc plus jamais de secteur vide/désactivé dans le menu.
        private static List<RadialMenuOption> _menuOptions = new();

        // Cache des textures d'icônes (chargées une seule fois, réutilisées ensuite)
        private static Dictionary<string, Texture2D> _iconCache = new();
        private const string ICON_DIR = "assets/gui/";

        public static bool IsOpen => _isRadialMenuOpen;

        public static void Update(Entity? animal, bool isKeyPressed, bool isKeyDown, bool isKeyReleased, Vector2 mouseScreenPos, Camera2D camera)
        {
            if (_isRadialMenuOpen)
            {
                UpdateRadialMenuSelection(mouseScreenPos);

                if (Raylib.IsKeyReleased(KeyboardKey.E))
                {
                    if (_isCenterHovered)
                        OpenAnimalInfo(_currentAnimal);
                    else if (_selectedOption >= 0)
                        ApplyOption(_currentAnimal, _selectedOption);
                    Close();
                }
                else if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsMouseButtonPressed(MouseButton.Right))
                {
                    Close();
                }
                else if (Raylib.IsMouseButtonPressed(MouseButton.Left) && _isCenterHovered)
                {
                    OpenAnimalInfo(_currentAnimal);
                    Close();
                }
                else if (Raylib.IsMouseButtonPressed(MouseButton.Left) && _selectedOption >= 0)
                {
                    ApplyOption(_currentAnimal, _selectedOption);
                    Close();
                }
            }
        }

        public static void OpenRadial(Entity animal, Camera2D camera)
        {
            _currentAnimal = animal;
            _isRadialMenuOpen = true;
            _isHoldingE = false;
            _holdTime = 0f;
            _selectedOption = -1;
            _isCenterHovered = false;

            //  Construction dynamique des options : seules les actions disponibles sont
            // ajoutées, donc le menu se redimensionne intelligemment (pas de secteur vide).
            _menuOptions.Clear();

            if (animal.IsMounted)
            {
                _menuOptions.Add(new RadialMenuOption
                {
                    Name = "Descendre",
                    Color = new Color(255, 150, 100, 220),
                    Action = RadialAction.Dismount,
                    IconFile = "action_dismount.png"
                });
            }
            else if (animal.IsMountable)
            {
                _menuOptions.Add(new RadialMenuOption
                {
                    Name = "Monter",
                    Color = new Color(100, 200, 255, 220),
                    Action = RadialAction.Mount,
                    IconFile = "action_mount.png"
                });
            }
            // Si l'animal n'est ni monté ni montable, on n'ajoute rien : pas de secteur "---".

            _menuOptions.Add(new RadialMenuOption
            {
                Name = "Suivre",
                Color = new Color(50, 150, 200, 220),
                Action = RadialAction.Follow,
                IconFile = "behaviour_follow.png"
            });
            _menuOptions.Add(new RadialMenuOption
            {
                Name = "Se promener",
                Color = new Color(100, 200, 100, 220),
                Action = RadialAction.Roam,
                IconFile = "behaviour_roam.png"
            });
            _menuOptions.Add(new RadialMenuOption
            {
                Name = "Attendre",
                Color = new Color(200, 150, 50, 220),
                Action = RadialAction.Stay,
                IconFile = "behaviour_stay.png"
            });
            _menuOptions.Add(new RadialMenuOption
            {
                Name = "Attaquer",
                Color = new Color(200, 80, 80, 220),
                Action = RadialAction.Attack,
                IconFile = "behaviour_attack.png"
            });

            if (animal.IsSlime)
            {
                _menuOptions.Add(new RadialMenuOption
                {
                    Name = "Stockage",
                    Color = new Color(120, 210, 150, 220),
                    Action = RadialAction.SlimeStorage,
                    IconFile = "slime_storage.png"
                });
            }

            // Calcul de la position à l'écran de l'animal
            int tileX = (int)(animal.WorldPos.X / Program.TileSize);
            int tileY = (int)(animal.WorldPos.Y / Program.TileSize);
            int height = World.GetHeightAt(tileX, tileY);
            float yOffset = -height * Program.TileSize / 4;
            Vector2 animalVisualWorldPos = new Vector2(animal.WorldPos.X, animal.WorldPos.Y + yOffset);
            _radialCenter = Raylib.GetWorldToScreen2D(animalVisualWorldPos, camera);
        }

        private static void UpdateRadialMenuSelection(Vector2 mousePos)
        {
            if (!_isRadialMenuOpen) return;

            Vector2 dir = mousePos - _radialCenter;
            float dist = dir.Length();

            // Rond central : sert à ouvrir les infos de l'animal
            if (dist <= _radialInnerRadius)
            {
                _isCenterHovered = true;
                _selectedOption = -1;
                return;
            }
            _isCenterHovered = false;

            if (dist > _radialRadius + 15f)
            {
                _selectedOption = -1;
                return;
            }

            float angle = MathF.Atan2(dir.Y, dir.X);
            float degrees = (angle * 180f / MathF.PI + 360) % 360;
            degrees = (degrees + 90f) % 360;

            int optionCount = _menuOptions.Count;
            if (optionCount == 0)
            {
                _selectedOption = -1;
                return;
            }
            float sliceAngle = 360f / optionCount;

            _selectedOption = -1;
            for (int i = 0; i < optionCount; i++)
            {
                float startAngle = i * sliceAngle;
                float endAngle = (i + 1) * sliceAngle;
                if (degrees >= startAngle && degrees < endAngle)
                {
                    _selectedOption = i;
                    break;
                }
            }
        }

        public static void Close()
        {
            _isRadialMenuOpen = false;
            _isHoldingE = false;
            _holdTime = 0f;
            _currentAnimal = null!;
            _selectedOption = -1;
            _isCenterHovered = false;
        }

        /// <summary>Ouvre le panneau d'informations classique de l'animal (via le rond central du menu radial).</summary>
        private static void OpenAnimalInfo(Entity animal)
        {
            if (animal == null) return;
            TamedAnimalUI.Open(animal);
        }

        private static void ApplyOption(Entity animal, int optionIndex)
        {
            if (animal == null) return;
            if (optionIndex < 0 || optionIndex >= _menuOptions.Count) return;

            var option = _menuOptions[optionIndex];

            switch (option.Action)
            {
                case RadialAction.Mount:
                    if (animal.IsMountable)
                        Program.MountAnimal(animal);
                    break;
                case RadialAction.Dismount:
                    Program.DismountAnimal();
                    break;
                case RadialAction.Follow:
                    animal.ApplyTamedOrder(TamedAnimalMode.Follow);
                    Program.AddNotification(new Notification($"{animal.Species} vous suit maintenant !", new Color(100, 200, 255, 255), 2f));
                    break;
                case RadialAction.Roam:
                    animal.ApplyTamedOrder(TamedAnimalMode.Roam);
                    Program.AddNotification(new Notification($"{animal.Species} se repose tranquillement", new Color(100, 255, 100, 255), 2f));
                    break;
                case RadialAction.Stay:
                    animal.ApplyTamedOrder(TamedAnimalMode.Stay);
                    Program.AddNotification(new Notification($"{animal.Species} attend sur place", new Color(255, 200, 100, 255), 2f));
                    break;
                case RadialAction.Attack:
                    animal.ApplyTamedOrder(TamedAnimalMode.Attack);
                    Program.AddNotification(new Notification($"{animal.Species} est prêt à attaquer !", new Color(255, 100, 100, 255), 2f));
                    break;
                case RadialAction.SlimeStorage:
                    if (animal.IsSlime)
                        SlimeStorageUI.Open(animal);
                    break;
            }
        }

        private static Texture2D GetIcon(string fileName)
        {
            if (_iconCache.TryGetValue(fileName, out var cached))
                return cached;

            string path = ICON_DIR + fileName;
            Texture2D tex = File.Exists(path) ? Raylib.LoadTexture(path) : new Texture2D();
            _iconCache[fileName] = tex;
            return tex;
        }

        public static void Draw(Camera2D camera)
        {
            // Cercle de progression (appui long E)
            if (_isHoldingE && !_isRadialMenuOpen && _currentAnimal != null)
            {
                Vector2 animalScreenPos = _radialCenter;
                float progress = Math.Clamp(_holdTime / HOLD_REQUIRED_TIME, 0f, 1f);

                float holdRadius = 34f;
                float holdThickness = 6f;

                // Fond du cercle (piste)
                Raylib.DrawRing(animalScreenPos, holdRadius - holdThickness, holdRadius, 0, 360, 40, new Color(20, 20, 25, 140));
                Raylib.DrawCircleLines((int)animalScreenPos.X, (int)animalScreenPos.Y, (int)holdRadius, new Color(255, 255, 255, 90));

                // Progression (part de -90° = midi, dans le sens horaire)
                if (progress > 0f)
                {
                    Color progressColor = progress >= 1f
                        ? new Color(255, 230, 120, 255)
                        : new Color(255, 200, 100, 220);
                    Raylib.DrawRing(animalScreenPos, holdRadius - holdThickness, holdRadius, -90f, -90f + 360f * progress, 40, progressColor);
                }

                string holdText = progress >= 1f ? "Relâchez !" : "Maintenez E...";
                int fontSize = 13;
                int textWidth = Raylib.MeasureText(holdText, fontSize);
                DrawShadowedText(holdText, (int)(animalScreenPos.X - textWidth / 2), (int)(animalScreenPos.Y + holdRadius + 12), fontSize, new Color(255, 220, 160, 230));
            }

            // Menu radial ouvert
            if (!_isRadialMenuOpen || _currentAnimal == null || _menuOptions.Count == 0) return;

            int optionCount = _menuOptions.Count;
            float sliceAngle = 360f / optionCount;
            float pulse = 1f + MathF.Sin((float)Raylib.GetTime() * 6f) * 0.02f;

            // Voile sombre autour du menu pour le détacher du décor et améliorer la lisibilité
            Raylib.DrawCircleGradient((int)_radialCenter.X, (int)_radialCenter.Y, _radialRadius + 46f,
                new Color(0, 0, 0, 90), new Color(0, 0, 0, 0));

            // Ombre portée du donut (légèrement décalée) pour donner du relief
            Raylib.DrawRing(_radialCenter + new Vector2(0, 4), _radialInnerRadius - 4f, _radialRadius + 4f, 0, 360, 90, new Color(0, 0, 0, 70));

            // Dessiner chaque secteur avec un petit espace entre eux
            for (int i = 0; i < optionCount; i++)
            {
                bool isSelected = _selectedOption == i;

                float startAngle = i * sliceAngle - 90f + _sliceGapDegrees / 2f;
                float endAngle = (i + 1) * sliceAngle - 90f - _sliceGapDegrees / 2f;

                float outerRadius = _radialRadius * (isSelected ? pulse * 1.06f : 1f);
                Color baseColor = _menuOptions[i].Color;
                Color sliceColor = isSelected
                    ? new Color((byte)Math.Min(255, baseColor.R + 45), (byte)Math.Min(255, baseColor.G + 45), (byte)Math.Min(255, baseColor.B + 45), (byte)255)
                    : new Color(baseColor.R, baseColor.G, baseColor.B, (byte)210);

                Raylib.DrawRing(_radialCenter, _radialInnerRadius, outerRadius, startAngle, endAngle, 24, sliceColor);

                // Halo lumineux supplémentaire sur le secteur survolé
                if (isSelected)
                {
                    Raylib.DrawRing(_radialCenter, outerRadius, outerRadius + 6f, startAngle, endAngle, 24, new Color(255, 255, 255, 130));
                }

                // Bordures fines pour bien séparer visuellement chaque secteur
                for (int edge = 0; edge < 2; edge++)
                {
                    float edgeAngle = (edge == 0 ? startAngle : endAngle) * MathF.PI / 180f;
                    Vector2 inner = _radialCenter + new Vector2(MathF.Cos(edgeAngle), MathF.Sin(edgeAngle)) * (_radialInnerRadius - 2f);
                    Vector2 outer = _radialCenter + new Vector2(MathF.Cos(edgeAngle), MathF.Sin(edgeAngle)) * (outerRadius + 2f);
                    Raylib.DrawLineEx(inner, outer, 1.5f, new Color(255, 255, 255, 60));
                }
            }

            // Contours extérieur / intérieur du donut
            Raylib.DrawCircleLines((int)_radialCenter.X, (int)_radialCenter.Y, (int)_radialRadius, new Color(255, 255, 255, 210));
            Raylib.DrawCircleLines((int)_radialCenter.X, (int)_radialCenter.Y, (int)_radialInnerRadius, new Color(255, 255, 255, 160));

            // Centre du cercle avec le nom de l'animal (rond central = ouvrir les infos)
            Color centerFillColor = _isCenterHovered ? new Color(55, 55, 68, 245) : new Color(30, 30, 38, 235);
            Color centerLineColor = _isCenterHovered ? new Color(255, 230, 150, 220) : new Color(255, 255, 255, 100);
            float centerPulse = _isCenterHovered ? 1f + MathF.Sin((float)Raylib.GetTime() * 6f) * 0.03f : 1f;
            Raylib.DrawCircle((int)_radialCenter.X, (int)_radialCenter.Y, (_radialInnerRadius - 4f) * centerPulse, centerFillColor);
            Raylib.DrawCircleLines((int)_radialCenter.X, (int)_radialCenter.Y, (int)((_radialInnerRadius - 4f) * centerPulse), centerLineColor);

            string centerLabel;
            if (_isCenterHovered)
                centerLabel = "Infos";
            else if (_selectedOption >= 0)
                centerLabel = _menuOptions[_selectedOption].Name;
            else
                centerLabel = _currentAnimal.Species;

            Color centerColor = _isCenterHovered ? new Color(255, 230, 150, 255) : (_selectedOption >= 0 ? Color.Gold : new Color(220, 220, 225, 255));
            int centerFontSize = (_isCenterHovered || _selectedOption >= 0) ? 16 : 13;
            int centerTextWidth = Raylib.MeasureText(centerLabel, centerFontSize);
            // On rétrécit le texte s'il dépasse le cercle central
            while (centerTextWidth > (_radialInnerRadius - 4f) * 1.7f && centerFontSize > 8)
            {
                centerFontSize--;
                centerTextWidth = Raylib.MeasureText(centerLabel, centerFontSize);
            }
            DrawShadowedText(centerLabel, (int)(_radialCenter.X - centerTextWidth / 2f), (int)(_radialCenter.Y - centerFontSize / 2f), centerFontSize, centerColor);

            // Icône (texture) + texte dans chaque secteur
            for (int i = 0; i < optionCount; i++)
            {
                bool isSelected = _selectedOption == i;

                float midAngleDeg = i * sliceAngle + sliceAngle / 2f - 90f;
                float midAngleRad = midAngleDeg * MathF.PI / 180f;
                Vector2 dirVec = new Vector2(MathF.Cos(midAngleRad), MathF.Sin(midAngleRad));

                float iconRadius = _radialInnerRadius + (_radialRadius - _radialInnerRadius) * 0.36f;
                float textRadius = _radialInnerRadius + (_radialRadius - _radialInnerRadius) * 0.78f;

                // Icône de l'action (texture), agrandie si survolée
                float iconSize = isSelected ? 30f : 24f;
                Vector2 iconPos = _radialCenter + dirVec * iconRadius;
                Texture2D iconTex = GetIcon(_menuOptions[i].IconFile);
                if (iconTex.Id != 0)
                {
                    var srcRect = new Rectangle(0, 0, iconTex.Width, iconTex.Height);
                    var destRect = new Rectangle(iconPos.X - iconSize / 2f, iconPos.Y - iconSize / 2f, iconSize, iconSize);
                    Color tint = isSelected ? Color.White : new Color(235, 235, 235, 235);
                    Raylib.DrawTexturePro(iconTex, srcRect, destRect, Vector2.Zero, 0f, tint);
                }
                else
                {
                    // Filet de sécurité si la texture n'existe pas encore : petit disque neutre
                    Raylib.DrawCircle((int)iconPos.X, (int)iconPos.Y, iconSize / 2f, new Color(255, 255, 255, 60));
                }

                // Libellé de l'action
                string optionText = _menuOptions[i].Name;
                if (optionText == "Se promener") optionText = "Promener";
                if (optionText == "Attaquer") optionText = "Attaque";

                int fontSize = isSelected ? 16 : 14;
                Vector2 textPos = _radialCenter + dirVec * textRadius;
                int textWidth = Raylib.MeasureText(optionText, fontSize);
                Color textColor = isSelected ? Color.White : new Color(240, 240, 240, 235);
                DrawShadowedText(optionText, (int)(textPos.X - textWidth / 2f), (int)(textPos.Y - fontSize / 2f), fontSize, textColor);
            }

            // Indicateur de validation
            string hint = "Relâchez E pour valider  •  Centre = Infos  •  Échap pour annuler";
            int hintFontSize = 12;
            int hintWidth = Raylib.MeasureText(hint, hintFontSize);
            DrawShadowedText(hint, (int)(_radialCenter.X - hintWidth / 2f), (int)(_radialCenter.Y + _radialRadius + 18), hintFontSize, new Color(210, 210, 215, 200));
        }

        private static void DrawShadowedText(string text, int x, int y, int fontSize, Color color)
        {
            Raylib.DrawText(text, x + 1, y + 2, fontSize, new Color(0, 0, 0, 180));
            Raylib.DrawText(text, x, y, fontSize, color);
        }
    }
}
