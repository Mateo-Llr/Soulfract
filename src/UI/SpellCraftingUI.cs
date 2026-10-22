// SpellCraftingUI.cs - Menu de création de sort (touche J) : sélection élément / forme / puissance,
// aperçu animé du sort, assignation aux clics gauche et droit.
#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class SpellCraftingUI
    {
        private static bool _isOpen = false;
        private static Vector2 _windowPos = new Vector2(300, 60);
        private static Vector2 _dragOffset = Vector2.Zero;
        private static bool _isDragging = false;
        private static bool _isHoveringTitleBar = false;

        private static SpellData _currentSpell = new SpellData
        {
            Element = SpellElement.Fire,
            Type = SpellType.Projectile,
            Power = PowerLevel.Medium
        };

        private static int _selectedElementIndex = 0;
        private static int _selectedTypeIndex = 0;
        private static int _selectedPowerIndex = 1;

        private static readonly SpellElement[] _elements = Enum.GetValues<SpellElement>();
        private static readonly SpellType[] _types = Enum.GetValues<SpellType>();
        private static readonly PowerLevel[] _powers =
        {
            PowerLevel.Weak,
            PowerLevel.Medium,
            PowerLevel.Strong
        };

        private static readonly SpellPreview _preview = new SpellPreview();
        private static string _hoverText = "";

        // ── Disposition (en unités de conception, multipliées par S) ──
        private const float DESIGN_W = 560f;
        private const float DESIGN_H = 664f;
        private const float HEADER_H = 44f;
        private const float PAD = 16f;

        private static float S => Math.Clamp(
            Math.Min(UIManager.LayoutScale * 1.35f,
                Math.Min((Raylib.GetScreenWidth() - 24f) / DESIGN_W, (Raylib.GetScreenHeight() - 24f) / DESIGN_H)),
            0.65f, 2.2f);
        private static int WinW => (int)(DESIGN_W * S);
        private static int WinH => (int)(DESIGN_H * S);

        private static Rectangle _titleBar, _closeBtn, _previewRect, _assignLeft, _assignRight, _cardLeft, _cardRight;
        private static Rectangle[] _elementRects = Array.Empty<Rectangle>();
        private static Rectangle[] _typeRects = Array.Empty<Rectangle>();
        private static Rectangle[] _powerRects = Array.Empty<Rectangle>();
        private static float _secElementY, _secTypeY, _secPowerY, _secAssignY, _descY;

        private static readonly Color BG_TOP = new Color(26, 29, 38, 250);
        private static readonly Color BG_BOTTOM = new Color(15, 17, 23, 250);
        private static readonly Color HEADER = new Color(33, 36, 47, 255);
        private static readonly Color BTN = new Color(40, 44, 56, 255);
        private static readonly Color BTN_HOVER = new Color(58, 63, 80, 255);
        private static readonly Color GOLD = new Color(210, 180, 100, 255);
        private static readonly Color MUTED = new Color(150, 150, 160, 255);

        public static bool IsOpen => _isOpen;

        public static void Open()
        {
            _isOpen = true;
            _isDragging = false;
            _preview.Reset();
            UIManager.PushUI(Close, () => IsOpen, IsPointInside);
        }

        public static void Close()
        {
            _isOpen = false;
            _isDragging = false;
        }

        private static bool IsPointInside(Vector2 position)
        {
            ComputeLayout();
            return Raylib.CheckCollisionPointRec(position,
                new Rectangle(_windowPos.X, _windowPos.Y, WinW, WinH));
        }

        // ───────────────────────── disposition ─────────────────────────
        private static void ComputeLayout()
        {
            float s = S;
            float x = _windowPos.X, y = _windowPos.Y;
            float w = WinW;
            float inner = w - PAD * 2f * s;
            float left = x + PAD * s;

            _titleBar = new Rectangle(x, y, w, HEADER_H * s);
            float cb = 26f * s;
            _closeBtn = new Rectangle(x + w - cb - 10f * s, y + (HEADER_H * s - cb) * 0.5f, cb, cb);

            float cy = y + (HEADER_H + 12f) * s;
            _previewRect = new Rectangle(left, cy, inner, 150f * s);
            cy += 150f * s + 14f * s;

            // Éléments : 5 colonnes x 2 lignes
            _secElementY = cy;
            cy += 16f * s;
            int cols = 5;
            float gap = 8f * s;
            float bw = (inner - gap * (cols - 1)) / cols, bh = 42f * s;
            if (_elementRects.Length != _elements.Length) _elementRects = new Rectangle[_elements.Length];
            for (int i = 0; i < _elements.Length; i++)
                _elementRects[i] = new Rectangle(left + (i % cols) * (bw + gap), cy + (i / cols) * (bh + gap), bw, bh);
            int elemRows = (_elements.Length + cols - 1) / cols;
            cy += elemRows * (bh + gap) + 6f * s;

            // Formes : 4 colonnes x 2 lignes
            _secTypeY = cy;
            cy += 16f * s;
            cols = 4;
            bw = (inner - gap * (cols - 1)) / cols; bh = 38f * s;
            if (_typeRects.Length != _types.Length) _typeRects = new Rectangle[_types.Length];
            for (int i = 0; i < _types.Length; i++)
                _typeRects[i] = new Rectangle(left + (i % cols) * (bw + gap), cy + (i / cols) * (bh + gap), bw, bh);
            int typeRows = (_types.Length + cols - 1) / cols;
            cy += typeRows * (bh + gap) + 6f * s;

            // Puissance : 1 ligne
            _secPowerY = cy;
            cy += 16f * s;
            int pc = Math.Max(1, _powers.Length);
            bw = (inner - gap * (pc - 1)) / pc; bh = 34f * s;
            if (_powerRects.Length != _powers.Length) _powerRects = new Rectangle[_powers.Length];
            for (int i = 0; i < _powers.Length; i++)
                _powerRects[i] = new Rectangle(left + i * (bw + gap), cy, bw, bh);
            cy += bh + 8f * s;

            // Ligne de description
            _descY = cy;
            cy += 22f * s;

            // Assignation : deux cartes
            _secAssignY = cy;
            cy += 16f * s;
            float cw = (inner - gap) * 0.5f, ch = 74f * s;
            _cardLeft = new Rectangle(left, cy, cw, ch);
            _cardRight = new Rectangle(left + cw + gap, cy, cw, ch);
            float abw = 82f * s, abh = 24f * s;
            _assignLeft = new Rectangle(_cardLeft.X + _cardLeft.Width - abw - 10f * s, _cardLeft.Y + 7f * s, abw, abh);
            _assignRight = new Rectangle(_cardRight.X + _cardRight.Width - abw - 10f * s, _cardRight.Y + 7f * s, abw, abh);
        }

        private static SpellData CopyCurrent() => new SpellData
        {
            Element = _currentSpell.Element,
            Type = _currentSpell.Type,
            Power = _currentSpell.Power
        };

        private static bool SameSpell(SpellData a, SpellData b) => a.Element == b.Element && a.Type == b.Type && a.Power == b.Power;

        // ───────────────────────── mise à jour ─────────────────────────
        public static void Update()
        {
            if (!_isOpen) return;

            Vector2 mouse = Raylib.GetMousePosition();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            _windowPos.X = Math.Clamp(_windowPos.X, 8f, Math.Max(8f, sw - WinW - 8f));
            _windowPos.Y = Math.Clamp(_windowPos.Y, 8f, Math.Max(8f, sh - WinH - 8f));

            ComputeLayout();

            // Déplacement de la fenêtre
            _isHoveringTitleBar = Raylib.CheckCollisionPointRec(mouse, _titleBar) && !Raylib.CheckCollisionPointRec(mouse, _closeBtn);
            if (_isHoveringTitleBar && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDragging = true;
                _dragOffset = mouse - _windowPos;
            }
            if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                _windowPos = mouse - _dragOffset;
                ComputeLayout();
            }
            if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                _isDragging = false;

            bool click = Raylib.IsMouseButtonPressed(MouseButton.Left) && !_isDragging;
            _hoverText = "";

            for (int i = 0; i < _elements.Length; i++)
            {
                if (!Raylib.CheckCollisionPointRec(mouse, _elementRects[i])) continue;
                _hoverText = Localization.Get("magic.hover.element", SpellVfx.GetElementName(_elements[i]), SpellVfx.GetElementDescription(_elements[i]));
                if (click) { _selectedElementIndex = i; _currentSpell.Element = _elements[i]; }
            }
            for (int i = 0; i < _types.Length; i++)
            {
                if (!Raylib.CheckCollisionPointRec(mouse, _typeRects[i])) continue;
                _hoverText = Localization.Get("magic.hover.type", SpellVfx.GetTypeName(_types[i]), SpellVfx.GetTypeDescription(_types[i]));
                if (click) { _selectedTypeIndex = i; _currentSpell.Type = _types[i]; }
            }
            for (int i = 0; i < _powers.Length; i++)
            {
                if (!Raylib.CheckCollisionPointRec(mouse, _powerRects[i])) continue;
                _hoverText = Localization.Get("magic.hover.power", SpellVfx.GetPowerName(_powers[i]));
                if (click) { _selectedPowerIndex = i; _currentSpell.Power = _powers[i]; }
            }

            if (click && Raylib.CheckCollisionPointRec(mouse, _assignLeft))
            {
                Program.SetLeftClickSpell(CopyCurrent());
                Program.AddNotification(new Notification(Localization.Get("magic.notification.left", SpellVfx.GetSpellName(_currentSpell)), SpellVfx.Style(_currentSpell.Element).Mid, 2f));
            }
            if (click && Raylib.CheckCollisionPointRec(mouse, _assignRight))
            {
                Program.SetRightClickSpell(CopyCurrent());
                Program.AddNotification(new Notification(Localization.Get("magic.notification.right", SpellVfx.GetSpellName(_currentSpell)), SpellVfx.Style(_currentSpell.Element).Mid, 2f));
            }
            if (click && Raylib.CheckCollisionPointRec(mouse, _closeBtn))
                Close();

            _preview.Update(Raylib.GetFrameTime(), _previewRect, _currentSpell.Element, _currentSpell.Type, _currentSpell.Power);

            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                Close();
        }

        // ───────────────────────── dessin ─────────────────────────
        private static int Fs(float size) => Math.Max(8, (int)MathF.Round(size * S));

        private static void Text(string text, float x, float y, float size, Color color)
            => FontManager.DrawText(text, (int)x, (int)y, Fs(size), color);

        private static void TextShadow(string text, float x, float y, float size, Color color)
        {
            FontManager.DrawText(text, (int)x + 1, (int)y + 1, Fs(size), new Color(0, 0, 0, 200));
            FontManager.DrawText(text, (int)x, (int)y, Fs(size), color);
        }

        private static void TextCentered(string text, Rectangle r, float size, Color color, float dy = 0f)
        {
            int w = FontManager.MeasureText(text, Fs(size));
            FontManager.DrawText(text, (int)(r.X + (r.Width - w) * 0.5f), (int)(r.Y + dy), Fs(size), color);
        }

        private static void SectionLabel(string label, float y, Color accent)
        {
            float s = S;
            float left = _windowPos.X + PAD * s;
            float right = _windowPos.X + WinW - PAD * s;
            Text(label, left, y, 11f, accent);
            int w = FontManager.MeasureText(label, Fs(11f));
            Raylib.DrawLine((int)(left + w + 8 * s), (int)(y + 7 * s), (int)right, (int)(y + 7 * s), new Color(accent.R, accent.G, accent.B, (byte)50));
        }

        private static void DrawButtonBg(Rectangle r, bool hover, bool selected, Color accent, Color dark)
        {
            Color bg = selected ? new Color(dark.R / 2 + 22, dark.G / 2 + 22, dark.B / 2 + 26, 255) : (hover ? BTN_HOVER : BTN);
            Raylib.DrawRectangleRounded(r, 0.18f, 6, bg);
            if (selected)
            {
                Raylib.DrawRectangleRoundedLines(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), 0.18f, 6, 2, accent);
            }
            else
            {
                Raylib.DrawRectangleRoundedLines(r, 0.18f, 6, 1, hover ? new Color(accent.R, accent.G, accent.B, (byte)150) : new Color(70, 74, 90, 255));
            }
        }

        public static void Draw()
        {
            if (!_isOpen) return;

            float s = S;
            ComputeLayout();
            Vector2 mouse = Raylib.GetMousePosition();
            float time = (float)Raylib.GetTime();
            var st = SpellVfx.Style(_currentSpell.Element);
            Color accent = st.Mid;

            Rectangle win = new Rectangle(_windowPos.X, _windowPos.Y, WinW, WinH);

            // Panneau
            Raylib.DrawRectangleRounded(new Rectangle(win.X + 5, win.Y + 6, win.Width, win.Height), 0.04f, 12, new Color(0, 0, 0, 110));
            Raylib.DrawRectangleRounded(win, 0.04f, 12, BG_BOTTOM);
            Raylib.DrawRectangleGradientV((int)win.X + 3, (int)win.Y + 3, (int)win.Width - 6, (int)(win.Height * 0.5f), BG_TOP, BG_BOTTOM);
            Raylib.DrawRectangleRoundedLines(win, 0.04f, 12, 2, new Color(accent.R, accent.G, accent.B, (byte)170));

            // En-tête
            Raylib.DrawRectangleRounded(_titleBar, 0.3f, 8, _isHoveringTitleBar ? new Color(48, 52, 66, 255) : HEADER);
            Raylib.DrawRectangle((int)_titleBar.X + 6, (int)(_titleBar.Y + _titleBar.Height - 2), (int)_titleBar.Width - 12, 2, new Color(accent.R, accent.G, accent.B, (byte)200));
            TextShadow(Localization.Get("magic.title"), _titleBar.X + 16 * s, _titleBar.Y + 13 * s, 17f, Color.White);

            bool hoverClose = Raylib.CheckCollisionPointRec(mouse, _closeBtn);
            Raylib.DrawRectangleRounded(_closeBtn, 0.25f, 6, hoverClose ? new Color(150, 60, 60, 255) : new Color(80, 40, 44, 230));
            TextCentered("X", _closeBtn, 14f, hoverClose ? Color.White : new Color(220, 150, 150, 255), 5f * s);

            // Aperçu
            Raylib.DrawRectangleRounded(new Rectangle(_previewRect.X - 2, _previewRect.Y - 2, _previewRect.Width + 4, _previewRect.Height + 4), 0.05f, 8, new Color(accent.R, accent.G, accent.B, (byte)90));
            _preview.Draw(_previewRect, _currentSpell.Element, _currentSpell.Type);
            Raylib.DrawRectangleLinesEx(_previewRect, 1, new Color(0, 0, 0, 120));

            string spellName = SpellVfx.GetSpellName(_currentSpell);
            TextShadow(spellName, _previewRect.X + 12 * s, _previewRect.Y + 10 * s, 20f, st.Core);
            string sub = $"{SpellVfx.GetTypeName(_currentSpell.Type)}  -  {SpellVfx.GetPowerName(_currentSpell.Power)}";
            TextShadow(sub, _previewRect.X + 12 * s, _previewRect.Y + 36 * s, 12f, MUTED);

            // Éléments
            SectionLabel(Localization.Get("magic.section.element"), _secElementY, GOLD);
            for (int i = 0; i < _elements.Length; i++)
            {
                Rectangle r = _elementRects[i];
                var es = SpellVfx.Style(_elements[i]);
                bool hover = Raylib.CheckCollisionPointRec(mouse, r);
                bool sel = i == _selectedElementIndex;
                DrawButtonBg(r, hover, sel, es.Mid, es.Dark);
                float iconSize = r.Height * 0.9f;
                SpellVfx.DrawElementIcon(_elements[i], new Vector2(r.X + iconSize * 0.62f, r.Y + r.Height * 0.5f), iconSize, time);
                string n = SpellVfx.GetElementName(_elements[i]);
                int tw = FontManager.MeasureText(n, Fs(11f));
                float tx = r.X + iconSize + (r.Width - iconSize - tw) * 0.5f;
                Text(n, Math.Max(r.X + iconSize, tx), r.Y + (r.Height - 11f * s) * 0.5f - 1, 11f, sel ? Color.White : new Color(205, 208, 218, 255));
            }

            // Formes
            SectionLabel(Localization.Get("magic.section.type"), _secTypeY, GOLD);
            for (int i = 0; i < _types.Length; i++)
            {
                Rectangle r = _typeRects[i];
                bool hover = Raylib.CheckCollisionPointRec(mouse, r);
                bool sel = i == _selectedTypeIndex;
                DrawButtonBg(r, hover, sel, accent, st.Dark);
                float iconS = r.Height * 0.3f;
                Color ic = sel ? st.Core : new Color(190, 194, 206, 255);
                SpellVfx.DrawTypeIcon(_types[i], new Vector2(r.X + r.Height * 0.55f, r.Y + r.Height * 0.5f), iconS, ic);
                string n = SpellVfx.GetTypeName(_types[i]);
                int tw = FontManager.MeasureText(n, Fs(11f));
                float region = r.Width - r.Height * 0.95f;
                Text(n, r.X + r.Height * 0.95f + (region - tw) * 0.5f, r.Y + (r.Height - 11f * s) * 0.5f - 1, 11f, sel ? Color.White : new Color(205, 208, 218, 255));
            }

            // Puissance
            SectionLabel(Localization.Get("magic.section.power"), _secPowerY, GOLD);
            for (int i = 0; i < _powers.Length; i++)
            {
                Rectangle r = _powerRects[i];
                bool hover = Raylib.CheckCollisionPointRec(mouse, r);
                bool sel = i == _selectedPowerIndex;
                DrawButtonBg(r, hover, sel, accent, st.Dark);
                // pastilles (1 à 4)
                float pip = 6f * s, pg = 3f * s;
                float px = r.X + 10 * s, py = r.Y + (r.Height - pip) * 0.5f;
                for (int p = 0; p < _powers.Length; p++)
                {
                    Color pc = p <= i ? (sel ? st.Core : accent) : new Color(70, 74, 90, 255);
                    Raylib.DrawRectangle((int)(px + p * (pip + pg)), (int)py, (int)pip, (int)pip, pc);
                }
                string n = SpellVfx.GetPowerName(_powers[i]);
                int tw = FontManager.MeasureText(n, Fs(11f));
                float start = px + _powers.Length * (pip + pg) + 4 * s;
                Text(n, start + (r.X + r.Width - start - tw) * 0.5f, r.Y + (r.Height - 11f * s) * 0.5f - 1, 11f, sel ? Color.White : new Color(205, 208, 218, 255));
            }

            // Description
            string desc = _hoverText.Length > 0 ? _hoverText : SpellVfx.GetTypeDescription(_currentSpell.Type);
            Text(desc, _windowPos.X + PAD * s, _descY + 2 * s, 11f, _hoverText.Length > 0 ? new Color(235, 220, 170, 255) : MUTED);

            // Assignation
            SectionLabel(Localization.Get("magic.section.assignment"), _secAssignY, GOLD);
            DrawAssignCard(_cardLeft, _assignLeft, Localization.Get("magic.click.left"), Program.GetLeftClickSpell(), mouse, time);
            DrawAssignCard(_cardRight, _assignRight, Localization.Get("magic.click.right"), Program.GetRightClickSpell(), mouse, time);

            string hint = Localization.Get("magic.close_hint");
            int hw = FontManager.MeasureText(hint, Fs(10f));
            Text(hint, win.X + (win.Width - hw) * 0.5f, win.Y + win.Height - 18 * s, 10f, new Color(120, 118, 110, 200));
        }

        private static void DrawAssignCard(Rectangle card, Rectangle btn, string label, SpellData assigned, Vector2 mouse, float time)
        {
            float s = S;
            var es = SpellVfx.Style(assigned.Element);
            bool active = SameSpell(assigned, _currentSpell);

            Raylib.DrawRectangleRounded(card, 0.14f, 6, new Color(30, 33, 43, 255));
            Raylib.DrawRectangleRoundedLines(card, 0.14f, 6, 1, active ? new Color(es.Mid.R, es.Mid.G, es.Mid.B, (byte)200) : new Color(66, 70, 86, 255));

            float iconSize = 30f * s;
            SpellVfx.DrawElementIcon(assigned.Element, new Vector2(card.X + 25f * s, card.Y + 53f * s), iconSize, time);
            Text(label.ToUpperInvariant(), card.X + 10f * s, card.Y + 12f * s, 9f, MUTED);
            float tx = card.X + 46f * s;
            Text(SpellVfx.GetSpellName(assigned), tx, card.Y + 37f * s, 12f, Color.White);
            Text($"{SpellVfx.GetTypeName(assigned.Type)} - {SpellVfx.GetPowerName(assigned.Power)}", tx, card.Y + 54f * s, 10f, MUTED);

            // bouton
            bool hover = Raylib.CheckCollisionPointRec(mouse, btn);
            Color bcol = active ? new Color(55, 60, 74, 255) : (hover ? new Color(88, 160, 96, 255) : new Color(66, 126, 74, 255));
            Raylib.DrawRectangleRounded(btn, 0.3f, 6, bcol);
            Raylib.DrawRectangleRoundedLines(btn, 0.3f, 6, 1, active ? new Color(90, 94, 110, 255) : new Color(150, 220, 160, 255));
            TextCentered(Localization.Get(active ? "magic.assigned" : "magic.assign"), btn, 11f, active ? MUTED : Color.White, (btn.Height - 11f * s) * 0.5f - 1);
        }
    }
}
