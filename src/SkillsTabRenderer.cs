// SkillsTabRenderer.cs - Languette + panneau coulissant des aptitudes, accrochée sur le BORD DROIT
// du panneau équipement. La languette est le "prolongement" du panneau : quand on l'ouvre, elle se
// déplace avec le bord du tiroir (continuité). L'ensemble est dessiné AVANT le panneau équipement
// pour rester visuellement derrière lui (pas de pop/disparition au niveau de la couture).
#nullable enable
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Soulfract
{
    public static class SkillsTabRenderer
    {
        // ==================== CONFIG ====================
        private const int TAB_FALLBACK_WIDTH = 40;
        private const int TAB_FALLBACK_HEIGHT = 110;
        private const int PANEL_WIDTH = 260;
        private const int ROW_HEIGHT = 64;
        private const int ICON_SIZE = 40;
        private const int ICON_BAR_GAP = 8;
        private const int BAR_WIDTH = 210;
        private const int BAR_HEIGHT = 18;
        private const float SLIDE_SPEED = 9f; // vitesse d'ouverture/fermeture
        private const float TAB_SCALE = 2.6f; // facteur d'agrandissement appliqué à la texture native de la languette
        private const int TAB_RAISE_OFFSET = 18; // remonte la languette au-dessus du panneau

        // Chemins des assets.  Le nom du fichier de la languette est "skill_tab.png"
        // (singulier), pas "skills_tab.png" — c'était le bug précédent.
        private const string TAB_TEXTURE_PATH = "assets/gui/skill_tab.png";
        // Base du nine-slice du panneau : attend skills_top1.png, skills_top2.png, skills_top3.png,
        // skills_mid1.png, skills_mid2.png, skills_mid3.png, skills_bottom1/2/3.png.
        // Si tes fichiers ont un autre préfixe, change juste cette constante.
        private const string PANEL_NINESLICE_BASE = "assets/gui/skills";

        private static readonly Color COLOR_BG_DARK = new Color(25, 28, 35, 235);
        private static readonly Color COLOR_BORDER = new Color(65, 68, 75, 255);
        private static readonly Color COLOR_ACCENT = new Color(210, 180, 100, 255);
        private static readonly Color FILL_SKILL = new Color(90, 170, 230, 255);  // bleu : XP d'aptitude
        private static readonly Color FILL_GLOBAL = new Color(230, 190, 90, 255); // doré : XP globale

        // ==================== ÉTAT ====================
        private static bool _isOpen = false;
        private static float _openAmount = 0f; // 0 = fermé, 1 = complètement ouvert
        private static float _scrollOffset = 0f;
        private static float _maxScroll = 0f;
        private const int SCROLLBAR_WIDTH = 8;

        // ==================== RESSOURCES ====================
        private static Texture2D _tabTexture;
        private static Texture2D _barTexture;
        private static Shader _barShader;
        private static NineSliceTexture? _panelNineSlice;
        private static bool _resourcesLoaded = false;
        private static int _tabTexW = TAB_FALLBACK_WIDTH;
        private static int _tabTexH = TAB_FALLBACK_HEIGHT;
        private static readonly Dictionary<string, Texture2D> _skillIcons = new();

        private static int _locBarX, _locBarWidth, _locHealthPercent, _locPreviousPercent, _locFillColor;

        private static void EnsureResourcesLoaded()
        {
            if (_resourcesLoaded) return;

            if (File.Exists(TAB_TEXTURE_PATH))
            {
                _tabTexture = Raylib.LoadTexture(TAB_TEXTURE_PATH);
                if (_tabTexture.Id != 0)
                {
                    _tabTexW = (int)(_tabTexture.Width * TAB_SCALE);
                    _tabTexH = (int)(_tabTexture.Height * TAB_SCALE);
                }
            }
            else
            {
                Console.WriteLine($"   {TAB_TEXTURE_PATH} introuvable — languette de secours (rectangle) utilisée");
            }

            if (File.Exists("assets/gui/skills_bar.png"))
                _barTexture = Raylib.LoadTexture("assets/gui/skills_bar.png");

            // Nine-slice du fond du panneau (même système que le panneau d'inventaire/équipement).
            _panelNineSlice = new NineSliceTexture(PANEL_NINESLICE_BASE, 4f);
            if (!_panelNineSlice.IsValid)
                Console.WriteLine($"   Nine-slice {PANEL_NINESLICE_BASE}_*.png introuvable — fond de secours (rectangle) utilisé");

            // Réutilise le shader de la healthbar (mêmes uniformes : barX, barWidth,
            // healthPercent, previousPercent, fillColor). Ajuste le chemin si besoin.
            if (File.Exists("assets/shaders/healthbar.fs"))
            {
                _barShader = Raylib.LoadShader(null, "assets/shaders/healthbar.fs");
                _locBarX = Raylib.GetShaderLocation(_barShader, "barX");
                _locBarWidth = Raylib.GetShaderLocation(_barShader, "barWidth");
                _locHealthPercent = Raylib.GetShaderLocation(_barShader, "healthPercent");
                _locPreviousPercent = Raylib.GetShaderLocation(_barShader, "previousPercent");
                _locFillColor = Raylib.GetShaderLocation(_barShader, "fillColor");
            }

            _resourcesLoaded = true;
        }

        public static void Close() => _isOpen = false;
        public static void Toggle() => _isOpen = !_isOpen;

        private static string GetSkillIconPath(SkillType? type)
        {
            string suffix = type switch
            {
                SkillType.Bucheron => "bucheron",
                SkillType.Artisan => "artisan",
                SkillType.Pecheur => "pecheur",
                SkillType.Mineur => "mineur",
                SkillType.Chasseur => "chasseur",
                SkillType.Dresseur => "dresseur",
                _ => "global"
            };
            return $"assets/gui/skill_{suffix}.png";
        }

        private static Texture2D GetSkillIcon(SkillType? type)
        {
            string path = GetSkillIconPath(type);
            if (_skillIcons.TryGetValue(path, out var cached)) return cached;

            Texture2D tex = default;
            if (File.Exists(path))
            {
                tex = Raylib.LoadTexture(path);
            }
            else
            {
                Console.WriteLine($"   {path} introuvable — icône d'aptitude manquante (rendu sans icône)");
            }
            _skillIcons[path] = tex;
            return tex;
        }

        // ==================== GÉOMÉTRIE MISE EN CACHE (calculée dans Update, utilisée dans Draw) ====================
        private static Rectangle _lastTabRect;
        private static bool _lastHoverTab;
        private static int _lastEquipRight, _lastEquipPanelY, _lastEquipPanelHeight;

        /// <summary>Empêche les clics de traverser vers le monde tant que le panneau est visible.</summary>
        public static bool IsBlockingInput => _openAmount > 0.01f || _lastHoverTab;

        /// <summary>
        /// À appeler une fois par frame quand le panneau équipement est ouvert, AVANT Draw().
        /// equipPanelX/Y/Width/Height = rectangle exact du panneau équipement dessiné par
        /// InventoryRenderer (mêmes coordonnées écran, offset de viewport déjà inclus).
        /// </summary>
        public static void Update(float dt, int equipPanelX, int equipPanelY, int equipPanelWidth, int equipPanelHeight)
        {
            EnsureResourcesLoaded();

            float target = _isOpen ? 1f : 0f;
            _openAmount = MathHelper.Lerp(_openAmount, target, Math.Clamp(dt * SLIDE_SPEED, 0f, 1f));
            if (MathF.Abs(_openAmount - target) < 0.002f) _openAmount = target;

            int equipRight = equipPanelX + equipPanelWidth;
            int revealedWidth = (int)(PANEL_WIDTH * _openAmount);

            // La languette est le bord "avant" du tiroir : quand fermé elle colle au bord droit
            // du panneau équipement ; quand ouvert elle a été poussée avec le panneau jusqu'à
            // equipRight + PANEL_WIDTH. Continuité visuelle garantie.
            int tabX = equipRight + revealedWidth - 6; // léger chevauchement pour paraître "vissée" au tiroir
            int tabY = equipPanelY + equipPanelHeight / 2 - _tabTexH / 2 - TAB_RAISE_OFFSET;
            Rectangle tabRect = new Rectangle(tabX, tabY, _tabTexW, _tabTexH);

            Vector2 mousePos = Raylib.GetMousePosition();
            bool hoverTab = Raylib.CheckCollisionPointRec(mousePos, tabRect);
            if (hoverTab && Raylib.IsMouseButtonPressed(MouseButton.Left))
                _isOpen = !_isOpen;

            _lastTabRect = tabRect;
            _lastHoverTab = hoverTab;
            _lastEquipRight = equipRight;
            _lastEquipPanelY = equipPanelY;
            _lastEquipPanelHeight = equipPanelHeight;

            int contentLines = 1 + Enum.GetValues<SkillType>().Length;
            int totalContentHeight = 40 + ROW_HEIGHT * contentLines;
            int availableHeight = Math.Max(160, equipPanelHeight - 28);
            _maxScroll = Math.Max(0f, totalContentHeight - availableHeight);
            _scrollOffset = Math.Clamp(_scrollOffset, 0f, _maxScroll);

            if (_openAmount > 0.01f && hoverTab == false)
            {
                Rectangle panelRect = new Rectangle(equipRight, equipPanelY, PANEL_WIDTH, equipPanelHeight);
                Vector2 panelMouse = Raylib.GetMousePosition();
                bool mouseInPanel = Raylib.CheckCollisionPointRec(panelMouse, panelRect);
                if (mouseInPanel)
                {
                    float wheel = Raylib.GetMouseWheelMove();
                    if (wheel != 0f)
                    {
                        _scrollOffset -= wheel * 48f;
                        _scrollOffset = Math.Clamp(_scrollOffset, 0f, _maxScroll);
                    }
                }
            }
        }

        public static void Draw()
        {
            if (!_resourcesLoaded) return;

            int revealedWidth = (int)(PANEL_WIDTH * _openAmount);

            // ---- Panneau coulissant (dessiné en premier : reste visuellement DERRIÈRE
            // le panneau équipement/inventaire, dessiné juste après par InventoryRenderer) ----
            if (revealedWidth > 0)
            {
                Rectangle clipRect = new Rectangle(_lastEquipRight, _lastEquipPanelY, revealedWidth, _lastEquipPanelHeight);
                Raylib.BeginScissorMode((int)clipRect.X, (int)clipRect.Y, (int)clipRect.Width, (int)clipRect.Height);

                // Le panneau garde toujours sa largeur pleine dans le clip, seul le scissor
                // limite ce qui est visible pendant l'animation -> le contenu ne se redimensionne pas.
                Rectangle fullPanelRect = new Rectangle(_lastEquipRight, _lastEquipPanelY, PANEL_WIDTH, _lastEquipPanelHeight);

                if (_panelNineSlice != null && _panelNineSlice.IsValid)
                {
                    _panelNineSlice.Draw(fullPanelRect, Color.White);
                }
                else
                {
                    Raylib.DrawRectangleRounded(fullPanelRect, 0.08f, 10, COLOR_BG_DARK);
                    Raylib.DrawRectangleRoundedLines(fullPanelRect, 0.08f, 10, 2, COLOR_BORDER);
                }

                int panelX = _lastEquipRight;
                int contentTop = _lastEquipPanelY + 12;
                int cursorY = contentTop + 18 - (int)_scrollOffset;
                FontManager.DrawText("APTITUDES", panelX + 14, contentTop, 16, COLOR_ACCENT);
                cursorY += 20;

                DrawSkillRow(panelX + 14, cursorY, null, "Global", SkillSystem.GlobalLevel,
                    SkillSystem.GetGlobalProgressPercent(), FILL_GLOBAL);
                cursorY += ROW_HEIGHT;

                Raylib.DrawLine(panelX + 14, cursorY - 8, panelX + PANEL_WIDTH - 14, cursorY - 8, COLOR_BORDER);

                foreach (SkillType type in Enum.GetValues<SkillType>())
                {
                    DrawSkillRow(panelX + 14, cursorY, type, SkillSystem.GetSkillDisplayName(type), SkillSystem.GetLevel(type),
                        SkillSystem.GetSkillProgressPercent(type), FILL_SKILL);
                    cursorY += ROW_HEIGHT;
                }

                if (_maxScroll > 0f)
                {
                    int trackHeight = Math.Max(80, _lastEquipPanelHeight - 28);
                    int trackY = _lastEquipPanelY + 14;
                    int thumbHeight = Math.Max(28, (int)((trackHeight / Math.Max(1f, _maxScroll + trackHeight)) * trackHeight));
                    int thumbY = trackY + (int)((_scrollOffset / _maxScroll) * Math.Max(1, trackHeight - thumbHeight));

                    Rectangle trackRect = new Rectangle(panelX + PANEL_WIDTH - 16, trackY, SCROLLBAR_WIDTH, trackHeight);
                    Raylib.DrawRectangleRounded(trackRect, 0.5f, 6, new Color(20, 22, 28, 180));
                    Rectangle thumbRect = new Rectangle(panelX + PANEL_WIDTH - 16, thumbY, SCROLLBAR_WIDTH, thumbHeight);
                    Raylib.DrawRectangleRounded(thumbRect, 0.5f, 6, new Color(210, 180, 100, 220));
                }

                Raylib.EndScissorMode();
            }

            // ---- Languette (dessinée par-dessus le panneau, mais toujours derrière ce que
            // InventoryRenderer dessine ensuite) ----
            if (_tabTexture.Id != 0)
            {
                Raylib.DrawTexturePro(_tabTexture,
                    new Rectangle(0, 0, _tabTexture.Width, _tabTexture.Height),
                    _lastTabRect, Vector2.Zero, 0f,
                    _lastHoverTab ? new Color(255, 255, 255, 255) : new Color(230, 230, 230, 255));
            }
            else
            {
                Raylib.DrawRectangleRounded(_lastTabRect, 0.3f, 8, _lastHoverTab ? new Color(70, 73, 85, 255) : COLOR_BG_DARK);
                Raylib.DrawRectangleRoundedLines(_lastTabRect, 0.3f, 8, 2, COLOR_ACCENT);
                string chevron = _isOpen ? "<" : ">";
                FontManager.DrawText(chevron, (int)_lastTabRect.X + _tabTexW / 2 - 4, (int)_lastTabRect.Y + _tabTexH / 2 - 8, 16, COLOR_ACCENT);
            }
        }

        /// <summary>
        /// Dessine une ligne d'aptitude : icône à gauche (skill_(aptitude).png ou skill_global.png),
        /// puis à droite deux rangées : "LVL X - Nom" au-dessus, la barre de progression en dessous.
        /// </summary>
        private static void DrawSkillRow(int x, int y, SkillType? type, string displayName, int level, float percent, Color fillColor)
        {
            Texture2D icon = GetSkillIcon(type);
            if (icon.Id != 0)
            {
                Raylib.DrawTexturePro(icon,
                    new Rectangle(0, 0, icon.Width, icon.Height),
                    new Rectangle(x, y, ICON_SIZE, ICON_SIZE),
                    Vector2.Zero, 0f, Color.White);
            }
            else
            {
                Raylib.DrawRectangleRounded(new Rectangle(x, y, ICON_SIZE, ICON_SIZE), 0.2f, 6, COLOR_BG_DARK);
                Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, ICON_SIZE, ICON_SIZE), 0.2f, 6, 1, COLOR_BORDER);
            }

            int contentX = x + ICON_SIZE + ICON_BAR_GAP;
            int barWidth = Math.Max(20, BAR_WIDTH - ICON_SIZE - ICON_BAR_GAP);

            FontManager.DrawText($"LVL {level} - {displayName}", contentX, y, 14, Color.White);
            DrawXpBar(contentX, y + ICON_SIZE - BAR_HEIGHT, barWidth, BAR_HEIGHT, percent, fillColor);
        }

        /// <summary>
        /// Dessine une barre d'XP en réutilisant le shader de la healthbar : remplissage de
        /// gauche à droite selon `percent`, sur la texture skills_bar.png.
        /// </summary>
        private static void DrawXpBar(int x, int y, int width, int height, float percent, Color fillColor)
        {
            if (_barTexture.Id == 0)
            {
                Raylib.DrawRectangle(x, y, width, height, new Color(30, 30, 34, 255));
                Raylib.DrawRectangle(x, y, (int)(width * percent), height, fillColor);
                Raylib.DrawRectangleLines(x, y, width, height, COLOR_BORDER);
                return;
            }

            Rectangle dest = new Rectangle(x, y, width, height);

            if (_barShader.Id != 0)
            {
                float renderScale = (float)Raylib.GetRenderWidth() / Raylib.GetScreenWidth();
                float screenBarX = x * renderScale;
                float screenBarWidth = width * renderScale;

                float fr = fillColor.R / 255f;
                float fg = fillColor.G / 255f;
                float fb = fillColor.B / 255f;

                Raylib.BeginShaderMode(_barShader);
                unsafe
                {
                    Raylib.SetShaderValue(_barShader, _locBarX, &screenBarX, ShaderUniformDataType.Float);
                    Raylib.SetShaderValue(_barShader, _locBarWidth, &screenBarWidth, ShaderUniformDataType.Float);
                    Raylib.SetShaderValue(_barShader, _locHealthPercent, &percent, ShaderUniformDataType.Float);
                    Raylib.SetShaderValue(_barShader, _locPreviousPercent, &percent, ShaderUniformDataType.Float);
                    float[] fillArr = { fr, fg, fb };
                    fixed (float* p = fillArr)
                        Raylib.SetShaderValue(_barShader, _locFillColor, p, ShaderUniformDataType.Vec3);
                }

                Raylib.DrawTexturePro(_barTexture,
                    new Rectangle(0, 0, _barTexture.Width, _barTexture.Height),
                    dest, Vector2.Zero, 0f, Color.White);

                Raylib.EndShaderMode();
            }
            else
            {
                Raylib.DrawTexturePro(_barTexture,
                    new Rectangle(0, 0, _barTexture.Width, _barTexture.Height),
                    dest, Vector2.Zero, 0f, Color.White);
            }
        }
    }
}
