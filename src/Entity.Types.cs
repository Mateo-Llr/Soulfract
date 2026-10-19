// Entity.cs - Début de la classe avec NetId
#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public enum NpcAiState { Idle, WalkToTarget, Wander, Flee, Chase, LookAtPlayer, Follow, GoHome, Sitting,
        GoToChestWithdraw, GoToChestDeposit, GoToMerchant, GoToGroundItem, GoToVillageCenter,
        GoToInfirmary, HealingAtInfirmary }

    public enum ProfessionType
    {
        None,
        Farmer,
        Blacksmith,
        Lumberjack,
        Hunter,
        Alchemist,
        Tailor,
        Miner,
        Cook,
        Guard
    }

    public static class ProfessionData
    {
        // Les stations de métier non liées à un village sont gérées par la recherche de
        // tuiles de station et des réservations dans World. On garde ici la table de
        // correspondance pour les professions qui utilisent des objets spécifiques.
        public static readonly Dictionary<ProfessionType, int[]> StationObjectIds = new()
        {
            // IDs à compléter lors de la définition exacte des stations de fabrication.
        };

        // Catalogue de vente: chaque métier vend des objets cohérents avec sa fonction,
        // sans dépendre d'un filtrage fragile sur les chaînes de caractères du nom.
        public static readonly Dictionary<ProfessionType, HashSet<string>> SaleItemKeys = new()
        {
            [ProfessionType.Farmer] = new(StringComparer.OrdinalIgnoreCase)
            {
                "apple", "bread", "carrot", "wheat", "flour", "mushroom", "berry",
                "wooden_hoe", "scythe", "watering_can", "seeds", "fertilizer"
            },
            [ProfessionType.Blacksmith] = new(StringComparer.OrdinalIgnoreCase)
            {
                "iron_ore", "iron_ingot", "coal", "copper_ore", "bronze", "hammer",
                "wooden_axe", "wooden_pickaxe", "stone_sword", "iron_sword", "iron_axe", "iron_pickaxe"
            },
            [ProfessionType.Lumberjack] = new(StringComparer.OrdinalIgnoreCase)
            {
                "log", "plank", "stick", "rope", "wooden_axe", "wooden_shovel", "scythe", "wooden_pickaxe"
            },
            [ProfessionType.Hunter] = new(StringComparer.OrdinalIgnoreCase)
            {
                "arrow", "bow", "stone_sword", "wooden_sword", "meat", "raw_meat", "hide",
                "trap", "leather"
            },
            [ProfessionType.Alchemist] = new(StringComparer.OrdinalIgnoreCase)
            {
                "potion", "herb", "mushroom", "honey", "glass", "bottle", "glassbottle_small", "milk"
            },
            [ProfessionType.Tailor] = new(StringComparer.OrdinalIgnoreCase)
            {
                "cotton", "cloth", "thread", "leather", "bandage", "fabric", "robe", "hat", "boots"
            },
            [ProfessionType.Miner] = new(StringComparer.OrdinalIgnoreCase)
            {
                "iron_ore", "coal", "copper_ore", "gold_ore", "stone", "rock", "wooden_pickaxe", "hammer", "iron_pickaxe"
            },
            [ProfessionType.Cook] = new(StringComparer.OrdinalIgnoreCase)
            {
                "apple", "bread", "carrot", "meat", "fish", "mushroom", "milk", "butter", "flour", "water_bucket"
            }
        };
    }

    public enum AttackPhase
    {
        None,
        Telegraph,   // Armement (durée : CombatProfile.Windup)
        Charge,      // Frappe : élan et/ou instant d'impact (CombatProfile.Strike)
        Recovery     // Récupération, pieds plantés (CombatProfile.Recovery)
    }

    public enum TamedAnimalMode { Follow, Stay, Roam, Attack }

}
