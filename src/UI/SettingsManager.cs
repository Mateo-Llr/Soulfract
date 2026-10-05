// SettingsManager.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soulfract
{
    public class GameSettings
    {
        public int MasterVolume { get; set; } = 100;
        public string VoiceChatMode { get; set; } = "PushToTalk";
        public bool Fullscreen { get; set; } = false;
        public string Language { get; set; } = "en";

        // Boutons UI activés dans la barre
        public bool ShowInventoryButton { get; set; } = true;
        public bool ShowCraftButton { get; set; } = true;
        public bool ShowGuildButton { get; set; } = true;
        public bool ShowQuestButton { get; set; } = true;
        public bool ShowMapButton { get; set; } = true;
        public bool ShowBestiaryButton { get; set; } = true;
        public bool ShowItemsButton { get; set; } = true;
        public string HotbarStyle { get; set; } = "radial";
        public string InventoryEquipmentStyle { get; set; } = "character";
    }

    public static class SettingsManager
    {
        private const string SETTINGS_PATH = "Data/settings.json";

        public static GameSettings Settings { get; private set; } = new GameSettings();

        public static void Load()
        {
            if (!File.Exists(SETTINGS_PATH))
            {
                Save();
                return;
            }

            try
            {
                string json = File.ReadAllText(SETTINGS_PATH);
                var loaded = JsonSerializer.Deserialize<GameSettings>(json);
                if (loaded != null)
                    Settings = loaded;
                else
                    Save();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur chargement settings: {ex.Message}");
                Save();
            }
        }

        public static void Save()
        {
            try
            {
                string? dir = Path.GetDirectoryName(SETTINGS_PATH);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SETTINGS_PATH, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur sauvegarde settings: {ex.Message}");
            }
        }
    }
}