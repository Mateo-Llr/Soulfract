#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Soulfract
{
    public static class Localization
    {
        private const string SettingsPath = "Data/settings.json";
        private const string LanguagesDir = "Data/Languages";

        private static readonly Dictionary<string, string> _translations = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<LanguageInfo> _supportedLanguages = new();

        public static string CurrentLanguage { get; private set; } = "fr";
        public static IReadOnlyList<LanguageInfo> AvailableLanguages => _supportedLanguages;

        public static bool Initialize()
        {
            EnsureDirectoriesExist();
            RefreshAvailableLanguages();
            LoadSettings();
            return LoadLanguage(CurrentLanguage);
        }

        public static string Get(string key, params object[] args)
        {
            if (!_translations.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                value = key;
            if (args != null && args.Length > 0)
            {
                try
                {
                    value = string.Format(value, args);
                }
                catch
                {
                    // ignore formatting failures and return raw text
                }
            }
            return value;
        }

        public static string GetOrDefault(string key, string fallback, params object[] args)
        {
            if (!_translations.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                value = fallback;
            if (args != null && args.Length > 0)
            {
                try
                {
                    value = string.Format(value, args);
                }
                catch
                {
                    // ignore formatting failures and return raw text
                }
            }
            return value;
        }

        public static bool SetLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                return false;

            RefreshAvailableLanguages();
            if (!LoadLanguage(languageCode))
                return false;

            SaveSettings();
            return true;
        }

        public static bool LoadLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                return false;

            RefreshAvailableLanguages();
            var language = GetLanguageInfo(languageCode);
            if (language == null)
                return false;

            string path = Path.Combine(LanguagesDir, $"{language.Code}.json");
            if (!File.Exists(path))
                return false;

            try
            {
                string json = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
                var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dictionary == null)
                    return false;

                _translations.Clear();
                foreach (var kv in dictionary)
                {
                    if (kv.Key != null && kv.Value != null)
                        _translations[kv.Key] = kv.Value;
                }

                CurrentLanguage = language.Code;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetLocalizedItemName(int itemId)
        {
            if (itemId <= 0)
                return string.Empty;

            if (GameData.ItemDatabase.TryGetValue(itemId, out var data))
                return GetLocalizedItemName(data.Key);

            return string.Empty;
        }

        public static string GetLocalizedItemDescription(int itemId)
        {
            if (itemId <= 0)
                return string.Empty;

            if (GameData.ItemDatabase.TryGetValue(itemId, out var data))
                return GetLocalizedItemDescription(data.Key);

            return string.Empty;
        }

        public static string GetLocalizedItemName(string? itemKey, int fallbackItemId = 0)
        {
            if (string.IsNullOrWhiteSpace(itemKey))
                return string.Empty;

            string key = $"item.name.{itemKey}";
            string translated = Get(key);
            if (!string.IsNullOrWhiteSpace(translated) && !string.Equals(translated, key, StringComparison.OrdinalIgnoreCase))
                return translated;

            if (GameData.ItemDatabaseByKey.TryGetValue(itemKey, out var data) && !string.IsNullOrWhiteSpace(data.Name))
                return data.Name;

            return string.Empty;
        }

        public static string GetLocalizedItemDescription(string? itemKey, int fallbackItemId = 0)
        {
            if (string.IsNullOrWhiteSpace(itemKey))
                return string.Empty;

            string key = $"item.description.{itemKey}";
            string translated = Get(key);
            if (!string.IsNullOrWhiteSpace(translated) && !string.Equals(translated, key, StringComparison.OrdinalIgnoreCase))
                return translated;

            return string.Empty;
        }

        public static string GetLocalizedSpeciesName(string species)
        {
            if (string.IsNullOrWhiteSpace(species))
                return string.Empty;

            var info = SpeciesData.GetSpeciesInfo(species);
            string fallbackName = info?.Name ?? species;
            return GetOrDefault($"species.name.{species.ToLowerInvariant()}", fallbackName);
        }

        public static string GetLocalizedItemName(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return string.Empty;

            if (GameData.TryGetItemByName(itemName, out var data))
                return GetLocalizedItemName(data.Key, data.ID);

            return itemName;
        }

        public static string GetItemTypeName(ItemType itemType)
        {
            return GetOrDefault($"item.type.{itemType.ToString().ToLowerInvariant()}", itemType.ToString());
        }

        public static string GetToolTypeName(ToolType toolType)
        {
            return GetOrDefault($"tool.type.{toolType.ToString().ToLowerInvariant()}", toolType.ToString());
        }

        public static string GetArmorCategoryName(ArmorCategory armorCategory)
        {
            return GetOrDefault($"armor.category.{armorCategory.ToString().ToLowerInvariant()}", armorCategory.ToString());
        }

        public static string GetQuestStateName(QuestState questState)
        {
            return GetOrDefault($"quest.state.{questState.ToString().ToLowerInvariant()}", questState.ToString());
        }

        public static string GetEquipmentSlotName(EquipmentSlot slot)
        {
            return GetOrDefault($"equipment.slot.{slot.ToString().ToLowerInvariant()}", slot.ToString());
        }

        public static void LoadSettings()
        {
            RefreshAvailableLanguages();

            if (!File.Exists(SettingsPath))
            {
                CurrentLanguage = _supportedLanguages.Count > 0 ? _supportedLanguages[0].Code : "fr";
                SaveSettings();
                return;
            }

            try
            {
                string json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<LocalizationSettings>(json);
                if (settings != null && !string.IsNullOrWhiteSpace(settings.Language) && GetLanguageInfo(settings.Language) != null)
                {
                    CurrentLanguage = settings.Language!;
                }
                else
                {
                    CurrentLanguage = _supportedLanguages.Count > 0 ? _supportedLanguages[0].Code : "fr";
                    SaveSettings();
                }
            }
            catch
            {
                CurrentLanguage = _supportedLanguages.Count > 0 ? _supportedLanguages[0].Code : "fr";
                SaveSettings();
            }
        }

        public static void SaveSettings()
        {
            try
            {
                EnsureDirectoriesExist();
                var settings = new LocalizationSettings { Language = CurrentLanguage };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // ignore save failures
            }
        }

        public static LanguageInfo? GetLanguageInfo(string languageCode)
        {
            RefreshAvailableLanguages();
            foreach (var language in _supportedLanguages)
            {
                if (string.Equals(language.Code, languageCode, StringComparison.OrdinalIgnoreCase))
                    return language;
            }
            return null;
        }

        private static void RefreshAvailableLanguages()
        {
            EnsureDirectoriesExist();
            var languages = new List<LanguageInfo>();

            if (Directory.Exists(LanguagesDir))
            {
                foreach (var file in Directory.GetFiles(LanguagesDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrWhiteSpace(code) || string.Equals(code, "settings", StringComparison.OrdinalIgnoreCase))
                        continue;

                    languages.Add(new LanguageInfo(code, GetDisplayName(code)));
                }
            }

            languages.Sort((left, right) => string.Compare(left.Code, right.Code, StringComparison.OrdinalIgnoreCase));
            _supportedLanguages.Clear();
            _supportedLanguages.AddRange(languages);
        }

        private static string GetDisplayName(string code)
        {
            return code.ToLowerInvariant() switch
            {
                "fr" => "Français",
                "en" => "English",
                "es" => "Español",
                "de" => "Deutsch",
                "it" => "Italiano",
                "pt" => "Português",
                "ru" => "Русский",
                "ja" => "日本語",
                "ko" => "한국어",
                "zh" => "中文",
                _ => FormatDisplayName(code)
            };
        }

        private static string FormatDisplayName(string code)
        {
            string[] parts = code.Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length <= 1)
                    continue;
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
            }
            return string.Join(" ", parts);
        }

        private static void EnsureDirectoriesExist()
        {
            string settingsDirectory = Path.GetDirectoryName(SettingsPath) ?? "Data";
            if (!Directory.Exists(settingsDirectory))
                Directory.CreateDirectory(settingsDirectory);
            if (!Directory.Exists(LanguagesDir))
                Directory.CreateDirectory(LanguagesDir);
        }
    }

    public sealed record LanguageInfo(string Code, string DisplayName);
    public sealed record LocalizationSettings
    {
        public string Language { get; set; } = "fr";
    }
}
