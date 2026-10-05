// Achievement.cs
using System.Numerics;
using System.Linq;
using Raylib_cs;

namespace Soulfract
{
    public enum AchievementType
    {
        // Exploration
        EnterCave,
        FindVillage,
        TravelDistance,
        
        // Combat
        KillEnemies,
        KillBoss,
        
        // Collection
        CraftItem,
        ObtainItem,
        GatherResources,
        
        // Élevage
        TameAnimal,
        
        // Construction
        BuildHouse,
        PlaceFurniture,
        
        // Autres
        FirstDeath,
        FullArmor,
        MaxHealth,
        Fishing,
        DyeItem,
        ThrowExplosive
    }
    
    public class Achievement
    {
        public AchievementType Type { get; set; }
        public string TitleKey { get; set; } = "";
        public string DescriptionKey { get; set; } = "";
        public string CategoryKey { get; set; } = "achievement.category.other";
        public string Title => Localization.Get(TitleKey);
        public string Description => Localization.Get(DescriptionKey);
        public string Category => Localization.Get(CategoryKey);
        public int TargetValue { get; set; } = 1;
        public Color IconColor { get; set; } = Color.Gold;
        public string IconPath { get; set; } = "";
        public bool IsHidden { get; set; } = false;
        
        public int CurrentValue { get; set; } = 0;
        public bool IsUnlocked { get; set; } = false;
        public float UnlockTime { get; set; } = 0f;
        
        public Achievement(AchievementType type, string titleKey, string descriptionKey, int target = 1, Color? color = null, string iconPath = "", bool hidden = false, string categoryKey = "achievement.category.other")
        {
            Type = type;
            TitleKey = titleKey;
            DescriptionKey = descriptionKey;
            TargetValue = target;
            IconColor = color ?? Color.Gold;
            IconPath = iconPath;
            IsHidden = hidden;
            CategoryKey = categoryKey;
        }
        
        public float GetProgressPercent()
        {
            if (TargetValue <= 0) return IsUnlocked ? 1f : 0f;
            return Math.Clamp((float)CurrentValue / TargetValue, 0f, 1f);
        }
        
        public string GetProgressText()
        {
            if (TargetValue <= 1) return "";
            return $"{CurrentValue}/{TargetValue}";
        }
    }
    
    public class AchievementNotification
    {
        public Achievement Achievement { get; set; }
        public float DisplayTimer { get; set; }
        public const float DISPLAY_DURATION = 4.5f;
        public float AnimationProgress { get; set; } = 0f;
        
        public AchievementNotification(Achievement achievement)
        {
            Achievement = achievement;
            DisplayTimer = DISPLAY_DURATION;
            AnimationProgress = 0f;
        }
        
        public void Update(float dt)
        {
            DisplayTimer -= dt;
            if (DisplayTimer <= 0)
                AnimationProgress = Math.Min(2f, AnimationProgress + dt * 3f);
            else if (DisplayTimer > DISPLAY_DURATION - 0.3f)
                AnimationProgress = Math.Min(1f, AnimationProgress + dt * 15f);
            else if (DisplayTimer < 0.5f)
                AnimationProgress = Math.Min(2f, AnimationProgress + dt * 8f);
            else
                AnimationProgress = 1f;
        }
        
        public bool IsAlive => DisplayTimer > 0 || AnimationProgress < 1.9f;
    }
    
    public static class AchievementManager
    {
        private static Dictionary<AchievementType, Achievement> _achievements = new();
        private static List<AchievementNotification> _notificationQueue = new();
        private static Texture2D _defaultIcon;
        private static Texture2D _achievementBg;
        private static bool _texturesLoaded = false;
        
        public static event Action<Achievement>? OnAchievementUnlocked;
        
        public static void Initialize()
        {
            LoadTextures();
            RegisterAllAchievements();
        }
        
        private static void LoadTextures()
        {
            _defaultIcon = LoadTextureOrDefault("assets/gui/achievement_default.png");
            _achievementBg = LoadTextureOrDefault("assets/gui/achievement_bg.png");
            _texturesLoaded = true;
        }
        
        private static Texture2D LoadTextureOrDefault(string path)
        {
            if (File.Exists(path))
                return Raylib.LoadTexture(path);
            return new Texture2D();
        }
        
        // ===== ICÔNES PAR SUCCÈS (assets/gui/achievements/{Type}.png, avec repli sur l'icône par défaut) =====
        private static Dictionary<AchievementType, Texture2D> _iconCache = new();
        
        private static Texture2D GetAchievementIcon(Achievement a)
        {
            if (_iconCache.TryGetValue(a.Type, out var cached))
                return cached;
            
            string path = string.IsNullOrEmpty(a.IconPath) ? $"assets/gui/achievements/{a.Type}.png" : a.IconPath;
            Texture2D tex = LoadTextureOrDefault(path);
            _iconCache[a.Type] = tex;
            return tex;
        }
        
        // ===== REGROUPEMENT PAR CATÉGORIE (pour le menu) =====
        private static List<(string Category, List<Achievement> Items)> GetGroupedAchievements()
        {
            var result = new List<(string, List<Achievement>)>();
            foreach (var cat in GetCategoryOrder())
            {
                var items = _achievements.Values.Where(a => a.Category == cat).OrderBy(a => a.Title).ToList();
                if (items.Count > 0)
                    result.Add((cat, items));
            }
            return result;
        }
        
        private static string[] GetCategoryOrder() => new[]
        {
            Localization.Get("achievement.category.exploration"),
            Localization.Get("achievement.category.combat"),
            Localization.Get("achievement.category.collection"),
            Localization.Get("achievement.category.breeding"),
            Localization.Get("achievement.category.construction"),
            Localization.Get("achievement.category.other")
        };
        
        private static void RegisterAllAchievements()
        {
            // Exploration
            AddAchievement(AchievementType.EnterCave, "achievement.title.enter_cave", "achievement.description.enter_cave", 1, new Color((byte)150, (byte)100, (byte)200, (byte)255), "achievement.category.exploration");
            AddAchievement(AchievementType.FindVillage, "achievement.title.find_village", "achievement.description.find_village", 1, new Color((byte)100, (byte)200, (byte)100, (byte)255), "achievement.category.exploration");
            AddAchievement(AchievementType.TravelDistance, "achievement.title.travel_distance", "achievement.description.travel_distance", 10000, new Color((byte)100, (byte)180, (byte)255, (byte)255), "achievement.category.exploration");
            
            // Combat
            AddAchievement(AchievementType.KillEnemies, "achievement.title.kill_enemies", "achievement.description.kill_enemies", 50, new Color((byte)255, (byte)80, (byte)80, (byte)255), "achievement.category.combat", true);
            AddAchievement(AchievementType.KillBoss, "achievement.title.kill_boss", "achievement.description.kill_boss", 1, new Color((byte)255, (byte)100, (byte)0, (byte)255), "achievement.category.combat");
            
            // Collection / Craft
            AddAchievement(AchievementType.CraftItem, "achievement.title.craft_item", "achievement.description.craft_item", 1, new Color((byte)100, (byte)200, (byte)200, (byte)255), "achievement.category.collection");
            AddAchievement(AchievementType.ObtainItem, "achievement.title.obtain_item", "achievement.description.obtain_item", 10, new Color((byte)255, (byte)200, (byte)100, (byte)255), "achievement.category.collection");
            AddAchievement(AchievementType.GatherResources, "achievement.title.gather_resources", "achievement.description.gather_resources", 100, new Color((byte)150, (byte)150, (byte)150, (byte)255), "achievement.category.collection");
            
            // Élevage
            AddAchievement(AchievementType.TameAnimal, "achievement.title.tame_animal", "achievement.description.tame_animal", 1, new Color((byte)100, (byte)200, (byte)150, (byte)255), "achievement.category.breeding");
            
            // Construction
            AddAchievement(AchievementType.BuildHouse, "achievement.title.build_house", "achievement.description.build_house", 1, new Color((byte)200, (byte)150, (byte)100, (byte)255), "achievement.category.construction");
            AddAchievement(AchievementType.PlaceFurniture, "achievement.title.place_furniture", "achievement.description.place_furniture", 10, new Color((byte)180, (byte)140, (byte)100, (byte)255), "achievement.category.construction");
            
            // Autres
            AddAchievement(AchievementType.FirstDeath, "achievement.title.first_death", "achievement.description.first_death", 1, new Color((byte)150, (byte)150, (byte)150, (byte)255), "achievement.category.other", true);
            AddAchievement(AchievementType.FullArmor, "achievement.title.full_armor", "achievement.description.full_armor", 1, new Color((byte)200, (byte)200, (byte)255, (byte)255), "achievement.category.other");
            AddAchievement(AchievementType.MaxHealth, "achievement.title.max_health", "achievement.description.max_health", 1, new Color((byte)255, (byte)100, (byte)100, (byte)255), "achievement.category.other");
            AddAchievement(AchievementType.Fishing, "achievement.title.fishing", "achievement.description.fishing", 1, new Color((byte)100, (byte)200, (byte)255, (byte)255), "achievement.category.other");
            AddAchievement(AchievementType.DyeItem, "achievement.title.dye_item", "achievement.description.dye_item", 1, new Color((byte)200, (byte)100, (byte)200, (byte)255), "achievement.category.other");
            AddAchievement(AchievementType.ThrowExplosive, "achievement.title.throw_explosive", "achievement.description.throw_explosive", 1, new Color((byte)255, (byte)100, (byte)50, (byte)255), "achievement.category.other");
        }
        
        private static void AddAchievement(AchievementType type, string title, string description, int target, Color color, string category = "Autres", bool hidden = false)
        {
            _achievements[type] = new Achievement(type, title, description, target, color, "", hidden, category);
        }
        
        public static void Progress(AchievementType type, int amount = 1)
        {
            if (!_achievements.TryGetValue(type, out var achievement))
                return;
                
            if (achievement.IsUnlocked)
                return;
                
            achievement.CurrentValue += amount;
            
            if (achievement.CurrentValue >= achievement.TargetValue && !achievement.IsUnlocked)
            {
                UnlockAchievement(achievement);
            }
        }
        
        public static void UnlockAchievement(AchievementType type)
        {
            if (!_achievements.TryGetValue(type, out var achievement))
                return;
                
            if (achievement.IsUnlocked)
                return;
                
            achievement.CurrentValue = achievement.TargetValue;
            UnlockAchievement(achievement);
        }
        
        private static void UnlockAchievement(Achievement achievement)
        {
            if (achievement.IsUnlocked) return;
            
            achievement.IsUnlocked = true;
            achievement.UnlockTime = (float)Raylib.GetTime();
            
            _notificationQueue.Add(new AchievementNotification(achievement));
            
            Console.WriteLine($" SUCCÈS DÉBLOQUÉ : {achievement.Title} - {achievement.Description}");
            
            OnAchievementUnlocked?.Invoke(achievement);
        }
        
        public static bool IsUnlocked(AchievementType type)
        {
            return _achievements.TryGetValue(type, out var a) && a.IsUnlocked;
        }
        
        public static int GetProgress(AchievementType type)
        {
            return _achievements.TryGetValue(type, out var a) ? a.CurrentValue : 0;
        }
        
        public static void Update(float dt)
        {
            for (int i = _notificationQueue.Count - 1; i >= 0; i--)
            {
                _notificationQueue[i].Update(dt);
                if (!_notificationQueue[i].IsAlive)
                    _notificationQueue.RemoveAt(i);
            }
        }
        
        public static void Draw()
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            
            for (int i = _notificationQueue.Count - 1; i >= 0; i--)
            {
                var notif = _notificationQueue[i];
                float anim = notif.AnimationProgress;
                
                if (anim >= 2f) continue;
                
                float slideX = 0f;
                if (anim < 1f)
                    slideX = (1f - anim) * 350f;
                
                int notifWidth = 320;
                int notifHeight = 80;
                int notifX = sw - notifWidth - 20 + (int)slideX;
                int notifY = sh - 100 - i * (notifHeight + 10);
                
                byte alpha = 255;
                if (anim > 1f)
                {
                    float fadeOut = 1f - (anim - 1f);
                    alpha = (byte)(Math.Clamp(fadeOut, 0f, 1f) * 255);
                }
                
                Color bgColor = new Color((byte)25, (byte)28, (byte)35, alpha);
                Color borderColor = new Color((byte)210, (byte)180, (byte)100, alpha);
                
                if (_achievementBg.Id != 0 && _texturesLoaded)
                {
                    Raylib.DrawTexturePro(_achievementBg,
                        new Rectangle(0, 0, _achievementBg.Width, _achievementBg.Height),
                        new Rectangle(notifX, notifY, notifWidth, notifHeight),
                        Vector2.Zero, 0, new Color((byte)255, (byte)255, (byte)255, alpha));
                }
                else
                {
                    Raylib.DrawRectangleRounded(new Rectangle(notifX, notifY, notifWidth, notifHeight), 0.1f, 8, bgColor);
                    Raylib.DrawRectangleRoundedLines(new Rectangle(notifX, notifY, notifWidth, notifHeight), 0.1f, 8, 2, borderColor);
                }
                
                int iconSize = 48;
                int iconX = notifX + 12;
                int iconY = notifY + (notifHeight - iconSize) / 2;
                
                Color glowColor = new Color(notif.Achievement.IconColor.R, notif.Achievement.IconColor.G, notif.Achievement.IconColor.B, alpha);
                
                if (_defaultIcon.Id != 0)
                {
                    Raylib.DrawTexturePro(_defaultIcon,
                        new Rectangle(0, 0, _defaultIcon.Width, _defaultIcon.Height),
                        new Rectangle(iconX, iconY, iconSize, iconSize),
                        Vector2.Zero, 0, glowColor);
                }
                else
                {
                    Raylib.DrawRectangleRounded(new Rectangle(iconX, iconY, iconSize, iconSize), 0.2f, 6, glowColor);
                }
                
                Raylib.DrawText(Localization.Get("achievement.notification.unlocked"), notifX + 70, notifY + 12, 12, new Color((byte)210, (byte)180, (byte)100, alpha));
                Raylib.DrawText(notif.Achievement.Title, notifX + 70, notifY + 32, 16, new Color((byte)255, (byte)255, (byte)255, alpha));
                
                string desc = notif.Achievement.Description;
                if (desc.Length > 28) desc = desc[..25] + "...";
                Raylib.DrawText(desc, notifX + 70, notifY + 54, 10, new Color((byte)180, (byte)180, (byte)160, alpha));
            }
        }
        
        // ===== MENU DES SUCCÈS (liste scrollable, 4 succès max par rangée) =====
        private static float _menuScrollY = 0f;
        private static float _menuMaxScroll = 0f;
        
        public static void OpenMenu()
        {
            _menuScrollY = 0f;
        }
        
        private static List<string> WrapText(string text, int fontSize, int maxWidth)
        {
            var lines = new List<string>();
            var words = text.Split(' ');
            string current = "";
            foreach (var w in words)
            {
                string test = current.Length == 0 ? w : current + " " + w;
                if (Raylib.MeasureText(test, fontSize) > maxWidth && current.Length > 0)
                {
                    lines.Add(current);
                    current = w;
                }
                else
                {
                    current = test;
                }
            }
            if (current.Length > 0) lines.Add(current);
            return lines;
        }
        
        /// <summary>
        /// Nombre de succès débloqués / total (pour affichage dans l'en-tête du menu appelant).
        /// </summary>
        public static (int Unlocked, int Total) GetCounts()
        {
            return (_achievements.Values.Count(a => a.IsUnlocked), _achievements.Values.Count);
        }
        
        /// <summary>
        /// Dessine uniquement la grille scrollable des succès (catégories + cartes, 4 par rangée)
        /// à l'intérieur du rectangle donné. Ne dessine ni fond, ni panneau, ni titre : à appeler
        /// depuis un menu qui gère déjà son propre habillage (comme le menu Options).
        /// </summary>
        public static void DrawGrid(int contentX, int contentY, int contentW, int contentH)
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            Rectangle scissorRect = new Rectangle(contentX, contentY, contentW, contentH);
            
            const int columns = 4;
            int spacing = 16;
            int cardW = (contentW - spacing * (columns - 1)) / columns;
            int cardH = 160;
            int categorySpacing = 44;
            int iconSize = 56;
            
            var grouped = GetGroupedAchievements();
            
            // Pré-calcule la hauteur totale du contenu (pour le clamp du scroll)
            float totalHeight = 0f;
            foreach (var (cat, items) in grouped)
            {
                int rows = (int)Math.Ceiling(items.Count / (float)columns);
                totalHeight += categorySpacing + rows * (cardH + spacing);
            }
            _menuMaxScroll = Math.Max(0f, totalHeight - contentH);
            
            // Scroll molette (uniquement si la souris est dans la zone de contenu)
            if (Raylib.CheckCollisionPointRec(mousePos, scissorRect))
            {
                float wheel = Raylib.GetMouseWheelMove();
                if (wheel != 0f) _menuScrollY -= wheel * 45f;
            }
            _menuScrollY = Math.Clamp(_menuScrollY, 0f, _menuMaxScroll);
            
            Raylib.BeginScissorMode((int)scissorRect.X, (int)scissorRect.Y, (int)scissorRect.Width, (int)scissorRect.Height);
            
            float cursorY = contentY - _menuScrollY;
            foreach (var (cat, items) in grouped)
            {
                // Titre de catégorie
                if (cursorY + categorySpacing > contentY - 40 && cursorY < contentY + contentH + 40)
                {
                    Raylib.DrawText(cat, contentX, (int)cursorY + 8, 22, new Color((byte)220, (byte)190, (byte)130, (byte)255));
                    Raylib.DrawLine(contentX, (int)cursorY + 34, contentX + contentW, (int)cursorY + 34, new Color((byte)90, (byte)80, (byte)60, (byte)180));
                }
                cursorY += categorySpacing;
                
                for (int i = 0; i < items.Count; i++)
                {
                    int col = i % columns;
                    int row = i / columns;
                    float cardX = contentX + col * (cardW + spacing);
                    float cardY = cursorY + row * (cardH + spacing);
                    
                    // Ne dessine que les cartes visibles (perf)
                    if (cardY + cardH < contentY - 20 || cardY > contentY + contentH + 20)
                        continue;
                    
                    DrawAchievementCard(items[i], cardX, cardY, cardW, cardH, iconSize);
                }
                
                int rowCount = (int)Math.Ceiling(items.Count / (float)columns);
                cursorY += rowCount * (cardH + spacing);
            }
            
            Raylib.EndScissorMode();
            
            // ── Barre de scroll (indicateur simple) ──
            if (_menuMaxScroll > 0f)
            {
                int trackX = contentX + contentW - 6;
                int trackY = contentY;
                int trackH = contentH;
                Raylib.DrawRectangle(trackX, trackY, 4, trackH, new Color((byte)60, (byte)60, (byte)70, (byte)150));
                float thumbH = Math.Max(30f, trackH * (contentH / totalHeight));
                float thumbY = trackY + (trackH - thumbH) * (_menuScrollY / _menuMaxScroll);
                Raylib.DrawRectangle(trackX, (int)thumbY, 4, (int)thumbH, new Color((byte)200, (byte)170, (byte)100, (byte)220));
            }
        }
        
        private static void DrawAchievementCard(Achievement a, float x, float y, int w, int h, int iconSize)
        {
            Rectangle cardRect = new Rectangle(x, y, w, h);
            bool locked = !a.IsUnlocked;
            bool hiddenLocked = locked && a.IsHidden;
            
            Color bg = a.IsUnlocked ? new Color((byte)32, (byte)30, (byte)22, (byte)230) : new Color((byte)24, (byte)24, (byte)28, (byte)220);
            Color border = a.IsUnlocked ? new Color((byte)210, (byte)180, (byte)100, (byte)255) : new Color((byte)70, (byte)70, (byte)80, (byte)200);
            
            Raylib.DrawRectangleRounded(cardRect, 0.08f, 8, bg);
            Raylib.DrawRectangleRoundedLines(cardRect, 0.08f, 8, 2, border);
            
            // ── Icône ──
            float iconX = x + w / 2f - iconSize / 2f;
            float iconY = y + 12;
            Texture2D icon = GetAchievementIcon(a);
            Color iconTint = a.IsUnlocked ? Color.White : new Color((byte)90, (byte)90, (byte)95, (byte)255);
            
            if (icon.Id != 0)
            {
                Raylib.DrawTexturePro(icon,
                    new Rectangle(0, 0, icon.Width, icon.Height),
                    new Rectangle(iconX, iconY, iconSize, iconSize),
                    Vector2.Zero, 0, iconTint);
            }
            else
            {
                Color fallbackColor = a.IsUnlocked ? a.IconColor : new Color((byte)60, (byte)60, (byte)65, (byte)255);
                Raylib.DrawRectangleRounded(new Rectangle(iconX, iconY, iconSize, iconSize), 0.2f, 6, fallbackColor);
            }
            
            if (locked)
            {
                // petit cadenas dessiné à la main dans le coin de l'icône
                Raylib.DrawCircle((int)(iconX + iconSize - 10), (int)(iconY + iconSize - 10), 9, new Color((byte)15, (byte)15, (byte)15, (byte)230));
                Raylib.DrawText("", (int)(iconX + iconSize - 17), (int)(iconY + iconSize - 17), 14, Color.White);
            }
            
            // ── Textes ──
            string displayTitle = hiddenLocked ? Localization.Get("achievement.hidden_title") : a.Title;
            string displayDesc = hiddenLocked ? Localization.Get("achievement.secret") : a.Description;
            
            int textX = (int)(x + 10);
            int textW = w - 20;
            float textY = iconY + iconSize + 10;
            
            // Titre / petite description en italique (Raylib n'a pas d'italique natif : on simule avec une teinte plus claire)
            int titleFontSize = 13;
            var titleLines = WrapText(displayTitle, titleFontSize, textW);
            Color titleColor = a.IsUnlocked ? new Color((byte)235, (byte)225, (byte)190, (byte)255) : new Color((byte)150, (byte)150, (byte)150, (byte)255);
            foreach (var line in titleLines.Take(2))
            {
                int lw = Raylib.MeasureText(line, titleFontSize);
                Raylib.DrawText(line, (int)(x + w / 2 - lw / 2), (int)textY, titleFontSize, titleColor);
                textY += titleFontSize + 3;
            }
            
            textY += 4;
            
            // Manière de l'obtenir : plus petit, plus foncé
            int descFontSize = 10;
            var descLines = WrapText(displayDesc, descFontSize, textW);
            Color descColor = a.IsUnlocked ? new Color((byte)150, (byte)145, (byte)130, (byte)255) : new Color((byte)95, (byte)95, (byte)100, (byte)255);
            foreach (var line in descLines.Take(3))
            {
                int lw = Raylib.MeasureText(line, descFontSize);
                Raylib.DrawText(line, (int)(x + w / 2 - lw / 2), (int)textY, descFontSize, descColor);
                textY += descFontSize + 3;
            }
            
            // ── Barre de progression (si objectif > 1) ──
            if (a.TargetValue > 1 && !hiddenLocked)
            {
                int barW = w - 24;
                int barH = 6;
                float barX = x + 12;
                float barY = y + h - 18;
                Raylib.DrawRectangleRounded(new Rectangle(barX, barY, barW, barH), 0.5f, 4, new Color((byte)40, (byte)40, (byte)45, (byte)255));
                float pct = a.GetProgressPercent();
                if (pct > 0f)
                {
                    Raylib.DrawRectangleRounded(new Rectangle(barX, barY, barW * pct, barH), 0.5f, 4, a.IsUnlocked ? new Color((byte)120, (byte)220, (byte)120, (byte)255) : a.IconColor);
                }
                string progText = a.GetProgressText();
                if (!string.IsNullOrEmpty(progText))
                {
                    int pfs = 9;
                    int pw = Raylib.MeasureText(progText, pfs);
                    Raylib.DrawText(progText, (int)(barX + barW / 2 - pw / 2), (int)(barY - 12), pfs, new Color((byte)170, (byte)170, (byte)160, (byte)255));
                }
            }
        }
        
        public static void Save(GameSaveData data)
        {
            data.Achievements.Clear();
            foreach (var a in _achievements.Values)
            {
                if (a.IsUnlocked)
                {
                    data.Achievements.Add(new AchievementSaveData
                    {
                        Type = (int)a.Type,
                        CurrentValue = a.CurrentValue,
                        IsUnlocked = a.IsUnlocked
                    });
                }
            }
        }

        public static void Load(GameSaveData data)
        {
            foreach (var saved in data.Achievements)
            {
                var type = (AchievementType)saved.Type;
                if (_achievements.TryGetValue(type, out var achievement))
                {
                    achievement.CurrentValue = saved.CurrentValue;
                    achievement.IsUnlocked = saved.IsUnlocked;
                }
            }
        }
        
        public static void Reset()
        {
            foreach (var achievement in _achievements.Values)
            {
                achievement.CurrentValue = 0;
                achievement.IsUnlocked = false;
            }
            _notificationQueue.Clear();
        }
    }
    
    public class AchievementSaveData
    {
        public int Type { get; set; }
        public int CurrentValue { get; set; }
        public bool IsUnlocked { get; set; }
    }
}