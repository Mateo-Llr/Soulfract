using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class FontManager
    {
        public static Font CustomFont { get; private set; }
        private static bool _loaded = false;

        public static void LoadFont(string path, int defaultFontSize = 16)
        {
            HashSet<int> codepoints = new HashSet<int>();

            // Caractères ASCII et ponctuation courants.
            AddCodepointRange(codepoints, 32, 126);

            // Latin-1 Supplement : accents français, allemands, espagnols, etc.
            AddCodepointRange(codepoints, 160, 255);

            // Latin Extended A/B + Latin Extended Additional.
            AddCodepointRange(codepoints, 0x0100, 0x024F);
            AddCodepointRange(codepoints, 0x1E00, 0x1EFF);

            // Alphabet grec et cyrillique standard.
            AddCodepointRange(codepoints, 0x0370, 0x03FF);
            AddCodepointRange(codepoints, 0x0400, 0x052F);

            // Symboles et ponctuations utiles dans les interfaces.
            AddCodepointRange(codepoints, 0x2010, 0x2027);
            AddCodepointRange(codepoints, 0x2030, 0x20FF);
            AddCodepointRange(codepoints, 0x2200, 0x22FF);

            // Caractères supplémentaires fréquents.
            codepoints.Add(0x0152); // Œ
            codepoints.Add(0x0153); // œ
            codepoints.Add(0x0178); // Ÿ
            codepoints.Add(0x00A3); // £
            codepoints.Add(0x20AC); // €
            codepoints.Add(0x00B0); // °
            codepoints.Add(0x2026); // …
            codepoints.Add(0x00D7); // ×
            codepoints.Add(0x00F7); // ÷

            int[] cps = codepoints.ToArray();
            CustomFont = Raylib.LoadFontEx(path, defaultFontSize, cps, cps.Length);

            if (CustomFont.BaseSize == 0)
            {
                Console.WriteLine($" Impossible de charger la police {path}, utilisation de la police par défaut.");
                CustomFont = Raylib.GetFontDefault();
            }
            else
            {
                Raylib.SetTextureFilter(CustomFont.Texture, TextureFilter.Bilinear);
                _loaded = true;
                Console.WriteLine($" Police personnalisée chargée avec succès ({cps.Length} glyphes inclus, support cyrillique/accents activé).");
            }
        }

        private static void AddCodepointRange(HashSet<int> codepoints, int start, int end)
        {
            for (int value = start; value <= end; value++)
            {
                codepoints.Add(value);
            }
        }

        public static void UnloadFont()
        {
            if (_loaded && CustomFont.Texture.Id != 0 && CustomFont.BaseSize != 0)
            {
                Raylib.UnloadFont(CustomFont);
                _loaded = false;
            }
        }

        public static void DrawText(string text, int x, int y, int fontSize, Color color)
        {
            if (!_loaded || CustomFont.Texture.Id == 0)
            {
                Raylib.DrawText(text, x, y, fontSize, color);
                return;
            }
            Raylib.DrawTextEx(CustomFont, text, new Vector2(x, y), fontSize, 1, color);
        }

        public static int MeasureText(string text, int fontSize)
        {
            if (!_loaded || CustomFont.Texture.Id == 0)
            {
                return Raylib.MeasureText(text, fontSize);
            }
            Vector2 size = Raylib.MeasureTextEx(CustomFont, text, fontSize, 1);
            return (int)size.X;
        }
    }
}