using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class TamedAnimalUI
    {
        private static bool _isOpen = false;
        private static Entity _currentAnimal = null!;
        private static Vector2 _windowPos = new Vector2(100, 100);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isEditingName = false;
        private static string _editingName = "";
        private static float _editBlinkTimer = 0f;
        private static float _animationTimer = 0f;
        private static int _animationFrame = 0;
		
		// Position de la fenêtre
		public static int GetWindowX() => (int)_windowPos.X;
		public static int GetWindowY() => (int)_windowPos.Y;
		public static int GetWindowWidth() => WINDOW_WIDTH;
		public static int GetWindowHeight() => WINDOW_HEIGHT;

        private static int WINDOW_WIDTH => UIManager.ScaleInt(320);
        private static int WINDOW_HEIGHT => UIManager.ScaleInt(420);
        private static int HEADER_HEIGHT => UIManager.ScaleInt(50);
        private static int PREVIEW_SIZE => UIManager.ScaleInt(120);
        
        private static readonly Color COLOR_BG = new Color(20, 22, 28, 240);
        private static readonly Color COLOR_HEADER = new Color(35, 38, 48, 255);
        private static readonly Color COLOR_BORDER = new Color(80, 70, 50, 200);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color COLOR_STATS_BG = new Color(30, 33, 40, 255);
        private static readonly Color COLOR_HP_BAR = new Color(80, 200, 80, 255);
        private static readonly Color COLOR_HP_BG = new Color(60, 30, 30, 255);
        
        // Textures pour l'interface (non-nullables avec un flag de validité)
        private static Texture2D _heartIcon;
        private static Texture2D _attackIcon;
        private static Texture2D _speedIcon;
        private static Texture2D _companionIcon;
        private static Texture2D _tipIcon;
        private static Texture2D _closeIcon;
        private static Texture2D _editIcon;
        private static Texture2D _followIcon;
        private static Texture2D _roamIcon;
        private static Texture2D _stayIcon;
        private static Texture2D _attackModeIcon;
        private static Texture2D _favFoodIcon;
        private static Texture2D _foodIcon;
        
        // Textures pour les icônes d'espèces
        private static Texture2D _wolfIcon;
        private static Texture2D _bearIcon;
        private static Texture2D _pigIcon;
        private static Texture2D _sheepIcon;
        private static Texture2D _cowIcon;
        private static Texture2D _chickenIcon;
        private static Texture2D _duckIcon;
        private static Texture2D _goatIcon;
        private static Texture2D _horseIcon;
        private static Texture2D _foxIcon;
        private static Texture2D _batIcon;
        private static Texture2D _ogreIcon;
        private static Texture2D _humanIcon;
        private static Texture2D _defaultIcon;
        
        // Textures pour l'édition (touches)
        private static Texture2D _enterIcon;
        private static Texture2D _escIcon;
        
        // Flags pour vérifier si les textures sont chargées
        private static bool _texturesLoaded = false;
        private static bool _heartValid = false;
        private static bool _attackValid = false;
        private static bool _speedValid = false;
        private static bool _companionValid = false;
        private static bool _tipValid = false;
        private static bool _closeValid = false;
        private static bool _editValid = false;
        private static bool _followValid = false;
        private static bool _roamValid = false;
        private static bool _stayValid = false;
        private static bool _attackModeValid = false;
        private static bool _favFoodValid = false;
        private static bool _foodValid = false;
        private static bool _enterValid = false;
        private static bool _escValid = false;
        private static bool _defaultValid = false;
		
		public static bool IsEditingName => _isEditingName;
        
        public static bool IsOpen => _isOpen;

        private static void LoadTextures()
        {
            if (_texturesLoaded) return;
            
            _heartIcon = Raylib.LoadTexture("assets/gui/heart.png");
            _heartValid = _heartIcon.Id != 0;
            
            _attackIcon = Raylib.LoadTexture("assets/gui/attack.png");
            _attackValid = _attackIcon.Id != 0;
            
            _speedIcon = Raylib.LoadTexture("assets/gui/speed.png");
            _speedValid = _speedIcon.Id != 0;
            
            _companionIcon = Raylib.LoadTexture("assets/gui/companion.png");
            _companionValid = _companionIcon.Id != 0;
            
            _tipIcon = Raylib.LoadTexture("assets/gui/tip.png");
            _tipValid = _tipIcon.Id != 0;
            
            _closeIcon = Raylib.LoadTexture("assets/gui/close.png");
            _closeValid = _closeIcon.Id != 0;
            
            _editIcon = Raylib.LoadTexture("assets/gui/edit.png");
            _editValid = _editIcon.Id != 0;
            
            _followIcon = Raylib.LoadTexture("assets/gui/follow.png");
            _followValid = _followIcon.Id != 0;
            
            _roamIcon = Raylib.LoadTexture("assets/gui/roam.png");
            _roamValid = _roamIcon.Id != 0;
            
            _stayIcon = Raylib.LoadTexture("assets/gui/stay.png");
            _stayValid = _stayIcon.Id != 0;
            
            _attackModeIcon = Raylib.LoadTexture("assets/gui/attack_mode.png");
            _attackModeValid = _attackModeIcon.Id != 0;
            
            _favFoodIcon = Raylib.LoadTexture("assets/gui/fav_food.png");
            _favFoodValid = _favFoodIcon.Id != 0;
            
            _foodIcon = Raylib.LoadTexture("assets/gui/food.png");
            _foodValid = _foodIcon.Id != 0;
            
            _enterIcon = Raylib.LoadTexture("assets/gui/enter_key.png");
            _enterValid = _enterIcon.Id != 0;
            
            _escIcon = Raylib.LoadTexture("assets/gui/esc_key.png");
            _escValid = _escIcon.Id != 0;
            
            // Chargement des icônes d'espèces
            _wolfIcon = Raylib.LoadTexture("assets/gui/wolf_icon.png");
            _bearIcon = Raylib.LoadTexture("assets/gui/bear_icon.png");
            _pigIcon = Raylib.LoadTexture("assets/gui/pig_icon.png");
            _sheepIcon = Raylib.LoadTexture("assets/gui/sheep_icon.png");
            _cowIcon = Raylib.LoadTexture("assets/gui/cow_icon.png");
            _chickenIcon = Raylib.LoadTexture("assets/gui/chicken_icon.png");
            _duckIcon = Raylib.LoadTexture("assets/gui/duck_icon.png");
            _goatIcon = Raylib.LoadTexture("assets/gui/goat_icon.png");
            _horseIcon = Raylib.LoadTexture("assets/gui/horse_icon.png");
            _foxIcon = Raylib.LoadTexture("assets/gui/fox_icon.png");
            _batIcon = Raylib.LoadTexture("assets/gui/bat_icon.png");
            _ogreIcon = Raylib.LoadTexture("assets/gui/ogre_icon.png");
            _humanIcon = Raylib.LoadTexture("assets/gui/human_icon.png");
            _defaultIcon = Raylib.LoadTexture("assets/gui/default_icon.png");
            _defaultValid = _defaultIcon.Id != 0;
            
            _texturesLoaded = true;
        }
        
        private static void UnloadTextures()
        {
            if (!_texturesLoaded) return;
            
            if (_heartValid) Raylib.UnloadTexture(_heartIcon);
            if (_attackValid) Raylib.UnloadTexture(_attackIcon);
            if (_speedValid) Raylib.UnloadTexture(_speedIcon);
            if (_companionValid) Raylib.UnloadTexture(_companionIcon);
            if (_tipValid) Raylib.UnloadTexture(_tipIcon);
            if (_closeValid) Raylib.UnloadTexture(_closeIcon);
            if (_editValid) Raylib.UnloadTexture(_editIcon);
            if (_followValid) Raylib.UnloadTexture(_followIcon);
            if (_roamValid) Raylib.UnloadTexture(_roamIcon);
            if (_stayValid) Raylib.UnloadTexture(_stayIcon);
            if (_attackModeValid) Raylib.UnloadTexture(_attackModeIcon);
            if (_favFoodValid) Raylib.UnloadTexture(_favFoodIcon);
            if (_foodValid) Raylib.UnloadTexture(_foodIcon);
            if (_enterValid) Raylib.UnloadTexture(_enterIcon);
            if (_escValid) Raylib.UnloadTexture(_escIcon);
            
            if (_wolfIcon.Id != 0) Raylib.UnloadTexture(_wolfIcon);
            if (_bearIcon.Id != 0) Raylib.UnloadTexture(_bearIcon);
            if (_pigIcon.Id != 0) Raylib.UnloadTexture(_pigIcon);
            if (_sheepIcon.Id != 0) Raylib.UnloadTexture(_sheepIcon);
            if (_cowIcon.Id != 0) Raylib.UnloadTexture(_cowIcon);
            if (_chickenIcon.Id != 0) Raylib.UnloadTexture(_chickenIcon);
            if (_duckIcon.Id != 0) Raylib.UnloadTexture(_duckIcon);
            if (_goatIcon.Id != 0) Raylib.UnloadTexture(_goatIcon);
            if (_horseIcon.Id != 0) Raylib.UnloadTexture(_horseIcon);
            if (_foxIcon.Id != 0) Raylib.UnloadTexture(_foxIcon);
            if (_batIcon.Id != 0) Raylib.UnloadTexture(_batIcon);
            if (_ogreIcon.Id != 0) Raylib.UnloadTexture(_ogreIcon);
            if (_humanIcon.Id != 0) Raylib.UnloadTexture(_humanIcon);
            if (_defaultValid) Raylib.UnloadTexture(_defaultIcon);
            
            _texturesLoaded = false;
        }

        public static void Open(Entity animal)
        {
            LoadTextures();
            _currentAnimal = animal;
            _isOpen = true;
            _isEditingName = false;
            _isDragging = false;
            _editingName = !string.IsNullOrEmpty(animal.CustomName) ? animal.CustomName : animal.Species;
            _animationTimer = 0f;
            _animationFrame = 0;
        }

        public static void Close()
		{
			_isOpen = false;
			UnloadTextures();
			_isDragging = false;
			_dragOffset = Vector2.Zero;
		}

        public static void Update()
		{
			if (!_isOpen || _currentAnimal == null) return;

			float dt = Raylib.GetFrameTime();
			
			_animationTimer += dt * 4f;
			if (_animationTimer >= 1f)
			{
				_animationTimer = 0f;
				_animationFrame = (_animationFrame + 1) % 8;
			}
			
			Vector2 mousePos = Raylib.GetMousePosition();
			Rectangle headerRect = new Rectangle(_windowPos.X, _windowPos.Y, WINDOW_WIDTH, HEADER_HEIGHT);
			
			if (!_isEditingName && Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mousePos, headerRect))
			{
				_isDragging = true;
				_dragOffset = mousePos - _windowPos;
			}
			
			if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
			{
				_windowPos = mousePos - _dragOffset;
				_windowPos.X = Math.Clamp(_windowPos.X, -WINDOW_WIDTH + 50, Raylib.GetScreenWidth() - 50);
				_windowPos.Y = Math.Clamp(_windowPos.Y, 0, Raylib.GetScreenHeight() - HEADER_HEIGHT);
			}
			
			if (Raylib.IsMouseButtonReleased(MouseButton.Left))
				_isDragging = false;
			
			if (_isDragging && !Raylib.IsMouseButtonDown(MouseButton.Left))
				_isDragging = false;
			
			if (_isEditingName)
			{
				_editBlinkTimer += dt;
				if (_editBlinkTimer >= 0.5f) _editBlinkTimer -= 0.5f;
                Rectangle nameInputRect = new Rectangle(_windowPos.X + 48, _windowPos.Y + 8, 180, 34);
                TextInput.Update(ref _editingName, "animal-name", nameInputRect, 20);
				
				if (Raylib.IsKeyPressed(KeyboardKey.Enter))
				{
					string newName = _editingName.Trim();
					if (!string.IsNullOrEmpty(newName))
						_currentAnimal.CustomName = newName;
					else
						_currentAnimal.CustomName = null;
					_isEditingName = false;
				}
				else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
				{
					_isEditingName = false;
                    TextInput.Reset("animal-name");
					_editingName = !string.IsNullOrEmpty(_currentAnimal.CustomName) ? _currentAnimal.CustomName : _currentAnimal.Species;
				}
			}
			
			if (Raylib.IsKeyPressed(KeyboardKey.Escape) && !_isEditingName)
				Close();
		}

        public static void Draw()
        {
            if (!_isOpen || _currentAnimal == null) return;
            
            DrawWindow();
            
            Raylib.BeginScissorMode(
                (int)_windowPos.X + 10, 
                (int)_windowPos.Y + HEADER_HEIGHT + 10, 
                WINDOW_WIDTH - 20, 
                WINDOW_HEIGHT - HEADER_HEIGHT - 20
            );
            
            DrawAnimalPreview();
            DrawStats();
            DrawInfo();
            
            Raylib.EndScissorMode();
        }
        
        private static void DrawWindow()
        {
            int x = (int)_windowPos.X;
            int y = (int)_windowPos.Y;
            
            Raylib.DrawRectangleRounded(new Rectangle(x + 4, y + 4, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, new Color(0, 0, 0, 100));
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, COLOR_BG);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, WINDOW_HEIGHT), 0.1f, 12, 2, COLOR_BORDER);
            
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 12, COLOR_HEADER);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH, HEADER_HEIGHT), 0.1f, 12, 1, COLOR_ACCENT);
            
            Texture2D speciesIcon = GetSpeciesIcon(_currentAnimal.Species);
            Vector2 iconPos = new Vector2(x + 12, y + 12);
            float iconSize = 28f;
            if (speciesIcon.Id != 0)
            {
                float iconScale = iconSize / speciesIcon.Width;
                Raylib.DrawTextureEx(speciesIcon, iconPos, 0f, iconScale, Color.White);
            }
            else if (_defaultValid)
            {
                float iconScale = iconSize / _defaultIcon.Width;
                Raylib.DrawTextureEx(_defaultIcon, iconPos, 0f, iconScale, Color.White);
            }
            
            string displayName = _currentAnimal.GetDisplayName();
            Rectangle nameRect = new Rectangle(x + 48, y + 8, 180, 34);
            
            if (_isEditingName)
            {
                Raylib.DrawRectangleRounded(nameRect, 0.2f, 6, COLOR_BG);
                Raylib.DrawRectangleRoundedLines(nameRect, 0.2f, 6, 1, COLOR_ACCENT);
                
                TextInput.DrawSingleLine(_editingName, "animal-name", nameRect, 16, Color.White,
                    new Color(90, 110, 150, 220), COLOR_ACCENT, 8);
                
                // Icône Entrée
                if (_enterValid)
                {
                    float enterScale = 14f / _enterIcon.Width;
                    Raylib.DrawTextureEx(_enterIcon, new Vector2(nameRect.X + nameRect.Width - 22, nameRect.Y + 8), 0f, enterScale, new Color(150, 140, 110, 180));
                }
                // Icône ESC
                if (_escValid)
                {
                    float escScale = 14f / _escIcon.Width;
                    Raylib.DrawTextureEx(_escIcon, new Vector2(nameRect.X + nameRect.Width - 42, nameRect.Y + 8), 0f, escScale, new Color(150, 140, 110, 180));
                }
            }
            else
            {
                string truncatedName = displayName;
                int nameWidth = Raylib.MeasureText(truncatedName, 16);
                if (nameWidth > 160)
                {
                    while (Raylib.MeasureText(truncatedName + "...", 16) > 160 && truncatedName.Length > 0)
                        truncatedName = truncatedName[..^1];
                    truncatedName += "...";
                }
                Raylib.DrawText(truncatedName, (int)nameRect.X + 8, (int)nameRect.Y + 8, 16, Color.White);
                
                Rectangle editBtn = new Rectangle(x + WINDOW_WIDTH - 55, y + 8, 30, 30);
                bool hoverEdit = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), editBtn);
                
                Raylib.DrawRectangleRounded(editBtn, 0.2f, 6, hoverEdit ? new Color(80, 70, 50, 200) : new Color(50, 45, 35, 150));
                if (_editValid)
                {
                    float iconEditScale = 18f / _editIcon.Width;
                    Raylib.DrawTextureEx(_editIcon, new Vector2(editBtn.X + 6, editBtn.Y + 6), 0f, iconEditScale, hoverEdit ? COLOR_ACCENT : new Color(150, 140, 110, 200));
                }
                
                if (hoverEdit && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _isEditingName = true;
                    _editingName = !string.IsNullOrEmpty(_currentAnimal.CustomName) ? _currentAnimal.CustomName : _currentAnimal.Species;
                }
            }
            
            Rectangle closeBtn = new Rectangle(x + WINDOW_WIDTH - 32, y + 8, 24, 24);
            bool hoverClose = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), closeBtn);
            Raylib.DrawRectangleRounded(closeBtn, 0.2f, 6, hoverClose ? new Color(140, 60, 60, 255) : new Color(80, 40, 40, 200));
            if (_closeValid)
            {
                float iconCloseScale = 16f / _closeIcon.Width;
                Raylib.DrawTextureEx(_closeIcon, new Vector2(closeBtn.X + 4, closeBtn.Y + 4), 0f, iconCloseScale, hoverClose ? Color.Red : new Color(200, 120, 120, 200));
            }
            
            if (hoverClose && Raylib.IsMouseButtonPressed(MouseButton.Left))
                Close();
        }
        
        private static void DrawAnimalPreview()
        {
            int centerX = (int)_windowPos.X + WINDOW_WIDTH / 2;
            int previewY = (int)_windowPos.Y + HEADER_HEIGHT + 20;
            
            Raylib.DrawCircle(centerX, previewY + PREVIEW_SIZE / 2 - 10, PREVIEW_SIZE / 2 + 10, new Color(25, 28, 35, 200));
            Raylib.DrawCircleLines(centerX, previewY + PREVIEW_SIZE / 2 - 10, PREVIEW_SIZE / 2 + 10, new Color(80, 70, 50, 150));
            
            Vector2 previewPos = new Vector2(centerX, previewY + PREVIEW_SIZE / 2 - 10);
            
            Texture2D hairBase = new Texture2D();
            Texture2D hairOverlay = new Texture2D();
            Color hairColor = Color.Black;
            
            if (_currentAnimal.Species == "human" && Program.hairBaseTextures.Count > 0)
            {
                hairBase = Program.hairBaseTextures[0];
                if (Program.hairOverlayTextures.Count > 0)
                    hairOverlay = Program.hairOverlayTextures[0];
                hairColor = _currentAnimal.HairColor;
            }
            
            Equipment tempEquip = new Equipment();
            
            EntityRenderer.DrawEntity(
                _currentAnimal.Species,
                "idle", _animationFrame, _animationTimer,
                1f,
                previewPos,
                _currentAnimal.Tint,
                hairBase, hairOverlay, hairColor,
                SpeciesData.Skeletons,
                Program.EyesTexture,
                Program.MouthTexture,
                customScale: 1.2f,
                equipment: tempEquip,
                isCarrying: false,
                inWater: false,
                attackSwingProgress: 0f,
                heldItemTexture: default,
                headAngle: 0f
            );
            
            if (_companionValid)
            {
                float companionScale = 12f / _companionIcon.Width;
                Raylib.DrawTextureEx(_companionIcon, new Vector2(centerX - 15, previewY + PREVIEW_SIZE - 5), 0f, companionScale, new Color(150, 140, 110, 200));
            }
            Raylib.DrawText("Votre compagnon", centerX - 35, previewY + PREVIEW_SIZE - 4, 11, new Color(150, 140, 110, 200));
        }
        
        private static void DrawStats()
        {
            int x = (int)_windowPos.X + 15;
            int y = (int)_windowPos.Y + HEADER_HEIGHT + PREVIEW_SIZE + 15;
            
            Raylib.DrawRectangleRounded(new Rectangle(x, y, WINDOW_WIDTH - 30, 95), 0.1f, 8, COLOR_STATS_BG);
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, WINDOW_WIDTH - 30, 95), 0.1f, 8, 1, new Color(60, 55, 45, 150));
            
            int barWidth = WINDOW_WIDTH - 60;
            int barX = x + 20;
            int barY = y + 12;
            
            if (_heartValid)
            {
                float heartScale = 14f / _heartIcon.Width;
                Raylib.DrawTextureEx(_heartIcon, new Vector2(x + 12, barY - 12), 0f, heartScale, new Color(200, 200, 150, 255));
            }
            Raylib.DrawText("Santé", x + 30, barY - 11, 12, new Color(200, 200, 150, 255));
            
            float hpPercent = (float)_currentAnimal.CurrentHP / _currentAnimal.MaxHP;
            int fillWidth = (int)(barWidth * hpPercent);
            
            Raylib.DrawRectangleRounded(new Rectangle(barX, barY, barWidth, 12), 0.3f, 6, COLOR_HP_BG);
            Raylib.DrawRectangleRounded(new Rectangle(barX, barY, fillWidth, 12), 0.3f, 6, COLOR_HP_BAR);
            
            string hpText = $"{_currentAnimal.CurrentHP} / {_currentAnimal.MaxHP} PV";
            int hpTextWidth = Raylib.MeasureText(hpText, 11);
            Raylib.DrawText(hpText, barX + barWidth - hpTextWidth - 5, barY - 10, 11, new Color(200, 200, 150, 180));
            
            int statsY = barY + 22;
            
            if (_attackValid)
            {
                float attackScale = 14f / _attackIcon.Width;
                Raylib.DrawTextureEx(_attackIcon, new Vector2(x + 10, statsY), 0f, attackScale, new Color(200, 160, 100, 255));
            }
            Raylib.DrawText($"Attaque: {_currentAnimal.Attack}", x + 35, statsY + 1, 12, Color.White);
            
            if (_speedValid)
            {
                float speedScale = 14f / _speedIcon.Width;
                Raylib.DrawTextureEx(_speedIcon, new Vector2(x + 10, statsY + 20), 0f, speedScale, new Color(100, 180, 200, 255));
            }
            Raylib.DrawText($"Vitesse: {_currentAnimal.Speed:F0}", x + 35, statsY + 21, 12, Color.White);
            
            Texture2D modeIcon = GetModeIconTexture(_currentAnimal.TamedBehavior);
            string modeText = GetModeText(_currentAnimal.TamedBehavior);
            if (modeIcon.Id != 0)
            {
                float modeScale = 14f / modeIcon.Width;
                Raylib.DrawTextureEx(modeIcon, new Vector2(x + 160, statsY), 0f, modeScale, COLOR_ACCENT);
            }
            Raylib.DrawText(modeText, x + 183, statsY + 1, 12, Color.White);
        }
        
        private static void DrawInfo()
        {
            int x = (int)_windowPos.X + 15;
            int y = (int)_windowPos.Y + HEADER_HEIGHT + PREVIEW_SIZE + 115;
            
            Raylib.DrawLine(x + 10, y - 5, x + WINDOW_WIDTH - 40, y - 5, new Color(80, 70, 50, 100));
            
            int prefFoodId = GetPreferredFoodId(_currentAnimal.Species);
            string foodName = "Inconnue";
            Color foodColor = new Color(150, 140, 110, 255);
            
            if (GameData.ItemDatabase.TryGetValue(prefFoodId, out var foodData))
            {
                foodName = foodData.Name;
                foodColor = foodData.Color;
            }
            
            if (_favFoodValid)
            {
                float favFoodScale = 14f / _favFoodIcon.Width;
                Raylib.DrawTextureEx(_favFoodIcon, new Vector2(x + 10, y + 5), 0f, favFoodScale, new Color(200, 200, 150, 200));
            }
            else if (_foodValid)
            {
                float foodScale = 14f / _foodIcon.Width;
                Raylib.DrawTextureEx(_foodIcon, new Vector2(x + 10, y + 5), 0f, foodScale, new Color(200, 200, 150, 200));
            }
            Raylib.DrawText("Nourriture prf.", x + 30, y + 6, 11, new Color(200, 200, 150, 200));
            Raylib.DrawText(foodName, x + 15, y + 22, 13, foodColor);
            
            int tipsY = y + 55;
            Raylib.DrawLine(x + 10, tipsY - 8, x + WINDOW_WIDTH - 40, tipsY - 8, new Color(80, 70, 50, 80));
            
            if (_tipValid)
            {
                float tipScale = 12f / _tipIcon.Width;
                Raylib.DrawTextureEx(_tipIcon, new Vector2(x + 12, tipsY), 0f, tipScale, new Color(120, 110, 90, 180));
            }
            Raylib.DrawText("Maintenez E pour ouvrir", x + 30, tipsY, 10, new Color(120, 110, 90, 180));
            Raylib.DrawText("le menu d'actions", x + 30, tipsY + 14, 10, new Color(120, 110, 90, 180));
        }
        
        private static Texture2D GetSpeciesIcon(string species)
        {
            if (!_texturesLoaded) return _defaultValid ? _defaultIcon : new Texture2D();
            
            Texture2D result = species?.ToLower() switch
            {
                "wolf" => _wolfIcon,
                "bear" => _bearIcon,
                "pig" => _pigIcon,
                "sheep" => _sheepIcon,
                "cow" => _cowIcon,
                "chicken" => _chickenIcon,
                "duck" => _duckIcon,
                "goat" => _goatIcon,
                "horse" => _horseIcon,
                "fox" => _foxIcon,
                "bat" => _batIcon,
                "ogre" => _ogreIcon,
                "human" => _humanIcon,
                _ => _defaultIcon
            };
            
            return result.Id != 0 ? result : (_defaultValid ? _defaultIcon : new Texture2D());
        }
        
        private static Texture2D GetModeIconTexture(TamedAnimalMode mode)
        {
            if (!_texturesLoaded) return new Texture2D();
            
            return mode switch
            {
                TamedAnimalMode.Follow => _followValid ? _followIcon : new Texture2D(),
                TamedAnimalMode.Roam => _roamValid ? _roamIcon : new Texture2D(),
                TamedAnimalMode.Stay => _stayValid ? _stayIcon : new Texture2D(),
                TamedAnimalMode.Attack => _attackModeValid ? _attackModeIcon : new Texture2D(),
                _ => new Texture2D()
            };
        }
        
        private static string GetModeText(TamedAnimalMode mode)
        {
            return mode switch
            {
                TamedAnimalMode.Follow => "Mode: Suivi",
                TamedAnimalMode.Roam => "Mode: Repos",
                TamedAnimalMode.Stay => "Mode: Attente",
                TamedAnimalMode.Attack => "Mode: Attaque",
                _ => "Mode: Inconnu"
            };
        }
        
        private static int GetPreferredFoodId(string species)
        {
            var info = SpeciesData.GetSpeciesInfo(species);
            return info?.PreferredFoodId ?? 0;
        }
    }
}