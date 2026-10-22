using Raylib_cs;

namespace Soulfract
{
    public static class QuestSplashUI
    {
        private const float DISPLAY_DURATION = 4f;
        private const float ENTRANCE_DURATION = 0.55f;
        private const float EXIT_DURATION = 0.55f;
        private const int TITLE_SIZE = 34;
        private const int DETAIL_SIZE = 19;
        private const int REWARD_SIZE = 14;
        private const int OUTLINE_SIZE = 2;
        private const int TOP_OFFSET = 72;
        private const int ENTRANCE_OFFSET = 18;

        private static string _title = "";
        private static string _detail = "";
        private static string _reward = "";
        private static Color _titleColor = Color.White;
        private static Color _detailColor = Color.White;
        private static float _timer;
        private static float _elapsed;

        public static void Show(string title, string detail, string reward, Color titleColor, Color detailColor)
        {
            _title = title;
            _detail = detail;
            _reward = reward;
            _titleColor = titleColor;
            _detailColor = detailColor;
            _timer = DISPLAY_DURATION;
            _elapsed = 0f;
        }

        public static void Draw()
        {
            if (_timer <= 0f)
                return;

            float dt = Raylib.GetFrameTime();
            _timer = Math.Max(0f, _timer - dt);
            _elapsed += dt;
            float entrance = SmoothStep(Math.Clamp(_elapsed / ENTRANCE_DURATION, 0f, 1f));
            float exit = Math.Clamp(_timer / EXIT_DURATION, 0f, 1f);
            float fade = Math.Min(entrance, exit);
            byte alpha = (byte)(Math.Clamp(fade, 0f, 1f) * 255f);
            int screenWidth = Raylib.GetScreenWidth();
            int maxWidth = Math.Max(1, screenWidth - 48);
            int titleY = TOP_OFFSET + (int)((1f - entrance) * -ENTRANCE_OFFSET);
            int titleWidth = FontManager.MeasureText(_title, TITLE_SIZE);
            DrawOutlinedText(_title, (screenWidth - titleWidth) / 2, titleY, TITLE_SIZE, WithAlpha(_titleColor, alpha));

            int detailY = titleY + TITLE_SIZE + 10;
            foreach (string line in WrapText(_detail, maxWidth, DETAIL_SIZE))
            {
                int lineWidth = FontManager.MeasureText(line, DETAIL_SIZE);
                DrawOutlinedText(line, (screenWidth - lineWidth) / 2, detailY, DETAIL_SIZE, WithAlpha(_detailColor, alpha));
                detailY += DETAIL_SIZE + 6;
            }

            if (!string.IsNullOrWhiteSpace(_reward))
            {
                Color rewardColor = WithAlpha(new Color(175, 175, 175, 255), alpha);
                foreach (string line in WrapText(_reward, maxWidth, REWARD_SIZE))
                {
                    int lineWidth = FontManager.MeasureText(line, REWARD_SIZE);
                    DrawOutlinedText(line, (screenWidth - lineWidth) / 2, detailY + 2, REWARD_SIZE, rewardColor);
                    detailY += REWARD_SIZE + 4;
                }
            }
        }

        private static float SmoothStep(float value)
            => value * value * (3f - 2f * value);

        private static void DrawOutlinedText(string text, int x, int y, int size, Color color)
        {
            Color outline = new Color((byte)0, (byte)0, (byte)0, color.A);
            for (int offsetY = -OUTLINE_SIZE; offsetY <= OUTLINE_SIZE; offsetY++)
            {
                for (int offsetX = -OUTLINE_SIZE; offsetX <= OUTLINE_SIZE; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0)
                        continue;
                    FontManager.DrawText(text, x + offsetX, y + offsetY, size, outline);
                }
            }

            FontManager.DrawText(text, x, y, size, color);
        }

        private static List<string> WrapText(string text, int maxWidth, int size)
        {
            var lines = new List<string>();
            string currentLine = "";
            foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
                if (string.IsNullOrEmpty(currentLine) || FontManager.MeasureText(candidate, size) <= maxWidth)
                {
                    currentLine = candidate;
                }
                else
                {
                    lines.Add(currentLine);
                    currentLine = word;
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
                lines.Add(currentLine);
            return lines;
        }

        private static Color WithAlpha(Color color, byte alpha)
            => new Color(color.R, color.G, color.B, alpha);
    }
}
