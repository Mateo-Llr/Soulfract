// Skills.cs - Système de niveaux global + aptitudes (Bûcheron, Artisan, Pêcheur, Mineur, Chasseur, Dresseur)
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Soulfract
{
    public enum SkillType
    {
        Bucheron,
        Artisan,
        Pecheur,
        Mineur,
        Chasseur,
        Dresseur
    }

    public class SkillProgress
    {
        public int Level = 0;
        public float Xp = 0f; // points accumulés dans le niveau courant
    }

    /// <summary>
    /// Système de compétences du joueur.
    /// - Chaque aptitude commence niveau 0, il faut 25 points pour passer au niveau suivant,
    ///   le coût est multiplié par 1.5 à chaque niveau (25, 37.5, 56.25, ...).
    /// - Le niveau global additionne TOUS les points gagnés par toutes les aptitudes.
    ///   Il faut 100 points de base pour monter, multiplié par 1.5 à chaque niveau.
    /// </summary>
    public static class SkillSystem
    {
        private const float SKILL_BASE_XP = 25f;
        private const float SKILL_XP_MULT = 1.5f;
        private const float GLOBAL_BASE_XP = 100f;
        private const float GLOBAL_XP_MULT = 1.5f;

        public static readonly Dictionary<SkillType, SkillProgress> Skills = new()
        {
            { SkillType.Bucheron, new SkillProgress() },
            { SkillType.Artisan,  new SkillProgress() },
            { SkillType.Pecheur,  new SkillProgress() },
            { SkillType.Mineur,   new SkillProgress() },
            { SkillType.Chasseur, new SkillProgress() },
            { SkillType.Dresseur, new SkillProgress() },
        };

        public static int GlobalLevel = 0;
        public static float GlobalXp = 0f;

        /// <summary>Événement levé quand une aptitude (ou le niveau global) monte de niveau, pour notifications/particules.</summary>
        public static Action<SkillType, int>? OnSkillLevelUp;
        public static Action<int>? OnGlobalLevelUp;

        public static float XpRequiredForSkillLevel(int level)
            => MathF.Round(SKILL_BASE_XP * MathF.Pow(SKILL_XP_MULT, level));

        public static float XpRequiredForGlobalLevel(int level)
            => MathF.Round(GLOBAL_BASE_XP * MathF.Pow(GLOBAL_XP_MULT, level));

        public static SkillProgress Get(SkillType type) => Skills[type];

        public static int GetLevel(SkillType type) => Skills[type].Level;
        public static float GetXp(SkillType type) => Skills[type].Xp;
        public static float GetXpToNext(SkillType type) => XpRequiredForSkillLevel(Skills[type].Level);
        public static float GetSkillProgressPercent(SkillType type)
        {
            float needed = GetXpToNext(type);
            return needed <= 0f ? 0f : Math.Clamp(Skills[type].Xp / needed, 0f, 1f);
        }

        public static float GetGlobalXpToNext() => XpRequiredForGlobalLevel(GlobalLevel);
        public static float GetGlobalProgressPercent()
        {
            float needed = GetGlobalXpToNext();
            return needed <= 0f ? 0f : Math.Clamp(GlobalXp / needed, 0f, 1f);
        }

        /// <summary>
        /// Ajoute de l'expérience à une aptitude. Chaque point ajouté alimente aussi
        /// la barre de niveau global. Gère les montées de niveau en cascade
        /// (si assez de points pour sauter plusieurs niveaux d'un coup).
        /// </summary>
        public static void AddXP(SkillType type, float amount)
        {
            if (amount <= 0f) return;

            var progress = Skills[type];
            progress.Xp += amount;
            while (progress.Xp >= XpRequiredForSkillLevel(progress.Level))
            {
                progress.Xp -= XpRequiredForSkillLevel(progress.Level);
                progress.Level++;
                OnSkillLevelUp?.Invoke(type, progress.Level);
            }

            GlobalXp += amount;
            while (GlobalXp >= XpRequiredForGlobalLevel(GlobalLevel))
            {
                GlobalXp -= XpRequiredForGlobalLevel(GlobalLevel);
                GlobalLevel++;
                OnGlobalLevelUp?.Invoke(GlobalLevel);
            }
        }

        public static string GetSkillDisplayName(SkillType type) => type switch
        {
            SkillType.Bucheron => "Bûcheron",
            SkillType.Artisan  => "Artisan",
            SkillType.Pecheur  => "Pêcheur",
            SkillType.Mineur   => "Mineur",
            SkillType.Chasseur => "Chasseur",
            SkillType.Dresseur => "Dresseur",
            _ => type.ToString()
        };

        // ==================== SAUVEGARDE ====================
        public static SkillsSaveData ToSaveData()
        {
            var data = new SkillsSaveData
            {
                GlobalLevel = GlobalLevel,
                GlobalXp = GlobalXp
            };
            foreach (var kv in Skills)
                data.Skills[kv.Key.ToString()] = new SkillSaveEntry { Level = kv.Value.Level, Xp = kv.Value.Xp };
            return data;
        }

        public static void LoadFromSaveData(SkillsSaveData? data)
        {
            // Reset
            foreach (var kv in Skills) { kv.Value.Level = 0; kv.Value.Xp = 0f; }
            GlobalLevel = 0;
            GlobalXp = 0f;

            if (data == null) return;

            GlobalLevel = data.GlobalLevel;
            GlobalXp = data.GlobalXp;
            foreach (var kv in data.Skills)
            {
                if (Enum.TryParse<SkillType>(kv.Key, out var type) && Skills.TryGetValue(type, out var progress))
                {
                    progress.Level = kv.Value.Level;
                    progress.Xp = kv.Value.Xp;
                }
            }
        }
    }

    // ==================== CLASSES DE SAUVEGARDE ====================
    // À ajouter dans SaveSystem.cs (ou laisser ici si SaveSystem.cs importe le namespace Soulfract).
    public class SkillsSaveData
    {
        public int GlobalLevel { get; set; } = 0;
        public float GlobalXp { get; set; } = 0f;
        public Dictionary<string, SkillSaveEntry> Skills { get; set; } = new();
    }

    public class SkillSaveEntry
    {
        public int Level { get; set; } = 0;
        public float Xp { get; set; } = 0f;
    }
}
