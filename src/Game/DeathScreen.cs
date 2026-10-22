#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static partial class Program
    {
        private static Texture2D _deathSkullTexture = new();
        private static bool _deathSkullLoaded;

        private static Texture2D LoadDeathSkullTexture()
        {
            if (_deathSkullLoaded && _deathSkullTexture.Id != 0)
                return _deathSkullTexture;
            if (File.Exists("assets/gui/death_skull.png"))
                _deathSkullTexture = Raylib.LoadTexture("assets/gui/death_skull.png");
            _deathSkullLoaded = true;
            return _deathSkullTexture;
        }

        // Avance la séquence de l'écran de mort pendant que le joueur est au sol.
        static void UpdateDeathScreen(float dt, bool skipRequested)
        {
            if (skipRequested && _deathScreenPhase != DeathScreenPhase.ButtonReady)
            {
                _deathScreenPhase = DeathScreenPhase.ButtonReady;
                _deathMessageCharsShown = _deathMessage.Length;
                return;
            }

            switch (_deathScreenPhase)
            {
                case DeathScreenPhase.Vignette:
                    _deathScreenPhaseTimer += dt;
                    if (_deathScreenPhaseTimer >= VIGNETTE_DURATION)
                    {
                        _deathScreenPhase = DeathScreenPhase.SkullPop;
                        _deathScreenPhaseTimer = 0f;
                    }
                    break;
                case DeathScreenPhase.SkullPop:
                    _deathScreenPhaseTimer += dt;
                    if (_deathScreenPhaseTimer >= SKULL_POP_DURATION)
                    {
                        _deathScreenPhase = DeathScreenPhase.TypingMessage;
                        _deathScreenPhaseTimer = 0f;
                    }
                    break;
                case DeathScreenPhase.TypingMessage:
                    _deathMessageCharTimer += dt;
                    while (_deathMessageCharTimer >= CHAR_REVEAL_INTERVAL && _deathMessageCharsShown < _deathMessage.Length)
                    {
                        _deathMessageCharTimer -= CHAR_REVEAL_INTERVAL;
                        _deathMessageCharsShown++;
                    }
                    if (_deathMessageCharsShown >= _deathMessage.Length)
                    {
                        _deathScreenPhase = DeathScreenPhase.WaitingForButton;
                        _deathScreenPhaseTimer = 0f;
                    }
                    break;
                case DeathScreenPhase.WaitingForButton:
                    _deathScreenPhaseTimer += dt;
                    if (_deathScreenPhaseTimer >= DELAY_BEFORE_BUTTON)
                        _deathScreenPhase = DeathScreenPhase.ButtonReady;
                    break;
                case DeathScreenPhase.ButtonReady:
                    break;
            }
        }

        static void DrawDeathScreen()
        {
            float vignetteT = _deathScreenPhase == DeathScreenPhase.Vignette
                ? Math.Clamp(_deathScreenPhaseTimer / VIGNETTE_DURATION, 0f, 1f)
                : 1f;
            if (vignetteT > 0f)
            {
                int maxAlpha = (int)(180 * vignetteT);
                int steps = 10;
                for (int i = 0; i < steps; i++)
                {
                    float f = (float)i / steps;
                    int alpha = (int)(maxAlpha * (1f - f));
                    int thickness = (int)(ScreenWidth * 0.12f * (1f - f) / steps) + 2;
                    Color c = new Color(0, 0, 0, alpha);
                    Raylib.DrawRectangle(0, i * thickness, ScreenWidth, thickness, c);
                    Raylib.DrawRectangle(0, ScreenHeight - (i + 1) * thickness, ScreenWidth, thickness, c);
                    Raylib.DrawRectangle(i * thickness, 0, thickness, ScreenHeight, c);
                    Raylib.DrawRectangle(ScreenWidth - (i + 1) * thickness, 0, thickness, ScreenHeight, c);
                }
            }

            if (_deathScreenPhase == DeathScreenPhase.Vignette)
                return;

            int centerX = ScreenWidth / 2;
            int centerY = ScreenHeight / 2 - 40;
            float skullScale = 1f;
            if (_deathScreenPhase == DeathScreenPhase.SkullPop)
            {
                float t = Math.Clamp(_deathScreenPhaseTimer / SKULL_POP_DURATION, 0f, 1f);
                float bounce = t < 0.6f
                    ? MathHelper.Lerp(0f, 1.25f, t / 0.6f)
                    : MathHelper.Lerp(1.25f, 1f, (t - 0.6f) / 0.4f);
                skullScale = bounce;
            }

            Texture2D skull = LoadDeathSkullTexture();
            int baseSize = 168;
            int size = (int)(baseSize * skullScale);
            if (skull.Id != 0)
            {
                Rectangle src = new Rectangle(0, 0, skull.Width, skull.Height);
                Rectangle dst = new Rectangle(centerX - size / 2f, centerY - size / 2f, size, size);
                Raylib.DrawTexturePro(skull, src, dst, Vector2.Zero, 0f, Color.White);
            }
            else
            {
                int fallbackFontSize = size;
                string glyph = "";
                int gw = Raylib.MeasureText(glyph, fallbackFontSize);
                FontManager.DrawText(glyph, centerX - gw / 2, centerY - fallbackFontSize / 2, fallbackFontSize, Color.White);
            }

            if (_deathScreenPhase == DeathScreenPhase.SkullPop)
                return;

            string visibleText = _deathMessage.Substring(0, Math.Min(_deathMessageCharsShown, _deathMessage.Length));
            int msgFontSize = 32;
            int maxWidth = (int)(ScreenWidth * 0.65f);
            var wrapped = WrapTextToWidth(visibleText, msgFontSize, maxWidth);
            int lineHeight = msgFontSize + 10;
            int textStartY = centerY + baseSize / 2 + 32;
            for (int i = 0; i < wrapped.Count; i++)
            {
                int lw = Raylib.MeasureText(wrapped[i], msgFontSize);
                FontManager.DrawText(wrapped[i], centerX - lw / 2, textStartY + i * lineHeight, msgFontSize, Color.Red);
            }

            if (_deathScreenPhase == DeathScreenPhase.WaitingForButton || _deathScreenPhase == DeathScreenPhase.TypingMessage)
                return;

            if (_deathScreenPhase == DeathScreenPhase.ButtonReady)
            {
                string label = "Réapparaître";
                int btnFontSize = 26;
                int textW = Raylib.MeasureText(label, btnFontSize);
                int btnW = textW + 70;
                int btnH = 56;
                int btnX = centerX - btnW / 2;
                int btnY = ScreenHeight - 110;
                _respawnButtonRect = new Rectangle(btnX, btnY, btnW, btnH);

                Vector2 mouse = Raylib.GetMousePosition();
                bool hovered = Raylib.CheckCollisionPointRec(mouse, _respawnButtonRect);
                Color bg = hovered ? new Color(150, 30, 30, 230) : new Color(110, 20, 20, 220);
                Raylib.DrawRectangleRec(_respawnButtonRect, bg);
                Raylib.DrawRectangleLinesEx(_respawnButtonRect, 2, Color.Red);
                FontManager.DrawText(label, btnX + 30, btnY + (btnH - btnFontSize) / 2, btnFontSize, Color.White);
            }
        }
    }
}
