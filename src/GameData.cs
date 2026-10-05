// GameData.cs - Version avec système d'équipement dynamique (EquipSlot / EquipMax)
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soulfract
{
    public enum ArmorCategory
    {
        None,
        Hat,    // Chapeau - cache les cheveux mais pas le visage
        Helmet, // Casque - cache cheveux ET visage (yeux/bouche)
        Mask    // Masque - ne cache rien (juste un accessoire)
    }

    //  Zones corporelles couvrables par les items équipables (armures, accessoires...).
    // Un item peut couvrir une ou plusieurs zones (ex: un casque d'armure couvre
    // TopOfHead + Ears + Face, alors qu'une écharpe ne couvre que Neck).
    public enum BodyZone
    {
        TopOfHead,  // Haut de la tête (cheveux)
        Ears,       // Oreilles
        Face,       // Visage (yeux/bouche)
        Neck,       // Cou
        Waist,      // Bassin / taille
        Torso,      // Buste
        Back,       // Dos
        Legs,       // Jambes
        Feet,       // Pieds
        Hands       // Mains
    }

    //  Emplacements "fonctionnels" restants : ne représentent pas une zone du corps
    // couverte visuellement, mais un rôle particulier (arme tenue en main, sac porté, etc.)
    public enum EquipmentSlot
    {
        MainHand,   // Main droite (arme, outil)
        OffHand,    // Main gauche (bouclier, panier, torche)
        Backpack,   // Sac à dos (stockage ; couvre aussi BodyZone.Back visuellement)
        Instrument, // Instrument de musique (max 1)
        Ammo,       // Munitions / Quiver (max 1)
        Pet,        // Animal de compagnie (max 1)
        Mount       // Monture (max 1)
    }

    public enum EquipmentCategory
    {
        Main,       // Emplacements principaux (MainHand, OffHand, Backpack...)
        Special     // Spéciaux (Instrument, Pet, Mount, etc.)
    }

    public static class GameData
    {
        // Bases de données
        public static Dictionary<int, ItemData> ItemDatabase = new();
        public static Dictionary<string, ItemData> ItemDatabaseByName = new(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, ItemData> ItemDatabaseByKey = new(StringComparer.OrdinalIgnoreCase);
        public static List<CraftRecipe> Recipes = new();

        public static List<(EquipmentSlot slot, string displayName, int defaultMax, EquipmentCategory category)> EquipmentSlotDefinitions = new()
        {
            // ===== EMPLACEMENTS PRINCIPAUX (au centre) =====
            (EquipmentSlot.MainHand, "Main droite", 1, EquipmentCategory.Main),
            (EquipmentSlot.OffHand, "Main gauche", 1, EquipmentCategory.Main),
            (EquipmentSlot.Backpack, "Sac à dos", 1, EquipmentCategory.Main),
            
            // ===== SPÉCIAUX (bas) =====
            (EquipmentSlot.Instrument, "Instrument", 1, EquipmentCategory.Special),
            (EquipmentSlot.Ammo, "Munitions", 1, EquipmentCategory.Special),
            (EquipmentSlot.Pet, "Animal de compagnie", 1, EquipmentCategory.Special),
            (EquipmentSlot.Mount, "Monture", 1, EquipmentCategory.Special)
        };

        //  Zones de corps couvertes par chaque catégorie d'armure de tête.
        // Utilisé pour dériver automatiquement CoveredZones des casques/chapeaux/masques.
        public static List<BodyZone> GetHeadZonesForCategory(ArmorCategory cat) => cat switch
        {
            ArmorCategory.Hat => new List<BodyZone> { BodyZone.TopOfHead },
            ArmorCategory.Helmet => new List<BodyZone> { BodyZone.TopOfHead, BodyZone.Ears, BodyZone.Face },
            ArmorCategory.Mask => new List<BodyZone> { BodyZone.Face },
            _ => new List<BodyZone> { BodyZone.TopOfHead, BodyZone.Ears }
        };


        public static void LoadFromFiles()
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                         CHARGEMENT DES DONNÉES                     ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            
            ItemDatabase.Clear();
            ItemDatabaseByName.Clear();
            ItemDatabaseByKey.Clear();
            Recipes.Clear();
            
            LoadItems();
            BuildItemNameIndex();
            LoadRecipes();
            
            Console.WriteLine();
            Console.WriteLine($"┌─ Résumé: {ItemDatabase.Count} items, {Recipes.Count} recettes ─┐");
            Console.WriteLine($"│  {EquipmentSlotDefinitions.Count} emplacements d'équipement disponibles");
            Console.WriteLine();
        }

        private static void LoadItems()
        {
            string path = "Data/items.json";
            if (!File.Exists(path))
            {
                Console.WriteLine($" Fichier manquant: {path}");
                Console.WriteLine("   Veuillez créer un fichier items.json valide");
                return;
            }

            string json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<ItemsRoot>(json);
            
            if (root?.items == null || root.items.Count == 0)
            {
                Console.WriteLine($" Aucun item trouvé dans {path}");
                return;
            }
            
            Console.WriteLine($" Chargement des items depuis {path}...");

            var pendingAmmoItems = new List<(int RuntimeId, ItemJson Item)>();
            
            for (int index = 0; index < root.items.Count; index++)
            {
                var item = root.items[index];
                int runtimeItemId = ResolveRuntimeItemId(item, index);
                string itemKey = ResolveItemKey(item);
                string localizedName = Localization.GetLocalizedItemName(itemKey, runtimeItemId);
                string localizedDescription = Localization.GetLocalizedItemDescription(itemKey, runtimeItemId);

                // L'ID de l'item (nom canonique, ex: "coat") est la base de texture fiable.
                // On garde un fallback legacy vers textureName seulement si l'item l'a explicitement
                // redéfini, mais sans le privilégier.
                string textureName = !string.IsNullOrWhiteSpace(itemKey)
                    ? itemKey
                    : (!string.IsNullOrWhiteSpace(item.textureName)
                        ? NormalizeItemKey(item.textureName)
                        : (string.IsNullOrWhiteSpace(item.name) ? "unknown" : NormalizeItemKey(item.name)));
                if (string.IsNullOrEmpty(textureName))
                {
                    textureName = localizedName.ToLowerInvariant()
                        .Replace("é", "e")
                        .Replace("è", "e")
                        .Replace("ê", "e")
                        .Replace("à", "a")
                        .Replace("ç", "c")
                        .Replace(" ", "_") ?? "unknown";
                }
                
                var itemData = new ItemData
                {
                    ID = runtimeItemId,
                    Key = itemKey,
                    Name = string.IsNullOrWhiteSpace(localizedName) ? GetItemFallbackName(itemKey, runtimeItemId) : localizedName,
                    Description = localizedDescription,
                    Metadata = item.metadata,
                    Icon = LoadTexture($"assets/items/{textureName}.png"),
                    Type = ParseItemType(item.type),
                    IsThrowable = item.isThrowable,
                    StackSize = item.stackSize > 0 ? item.stackSize : 99,
                    Value = item.value,
                    HealAmount = item.healAmount,
                    AttackBonus = item.attackBonus,
                    MiningPower = item.miningPower > 0 ? item.miningPower : 1f,
                    ToolKind = ParseToolType(item.toolType),
                    WaterCapacity = item.waterCapacity > 0 ? item.waterCapacity : 0,
                    IsPlaceable = item.type == "placeable",
                    PlaceableId = item.placeableId,
                    
                    //  Emplacement fonctionnel (arme, sac, monture...) - optionnel
                    EquipSlot = ParseEquipmentSlot(item.equipSlot),
                    HasEquipSlot = !string.IsNullOrEmpty(item.equipSlot),
                    EquipMax = item.equipMax > 0 ? item.equipMax : 1,
                    WeaponCategory = ParseWeaponCategory(item.weaponCategory),
                    MaxAmmoPerCharge = item.maxAmmoPerCharge > 0 ? item.maxAmmoPerCharge : 1,
                    AmmoIds = new List<int>(),
                    FireRate = item.fireRate > 0f ? item.fireRate : 1f,

                    //  Zones du corps couvertes (casque, écharpe, plastron...)
                    CoveredZones = ParseCoveredZones(item.coveredZones, item.armorCategory),

                    //  SYSTÈME DE TEMPÉRATURE
                    Warmth = item.warmth,
                    Cooling = item.cooling,

                    //  SYSTÈME DE RUNES
                    RuneSlots = item.runeSlots,
                    
                    ArmorSlot = item.armorSlot ?? "", // Gardé pour compatibilité (sera supprimé plus tard)
                    ArmorValue = item.armorValue > 0 ? item.armorValue : GetDefaultArmorValue(item),
                    IsGroundTile = item.isGroundTile,
                    ArmorCategory = ParseArmorCategory(item.armorCategory, textureName, localizedName),
                    TextureName = textureName,
                    IsSeed = item.isSeed,
                    CropId = item.cropId,
                    GrowthTime = item.growthTime,
                    GrowthStages = item.growthStages,
                    FertilizerBoost = item.fertilizerBoost,
                    PlantType = string.IsNullOrEmpty(item.plantType) ? "farmland" : item.plantType.ToLowerInvariant(),
                    Cookable = item.cookable,
                    CookedItemKey = item.cookedItem,
                    IsDyeable = item.dyeable,
                    DyeLayers = item.dyeLayers > 0 ? item.dyeLayers : 1,
                    BackpackSlots = item.backpackSlots,
                    BackpackRows = item.backpackRows,
                    IsLightSource = item.isLightSource,
                    LightRadius = item.lightRadius > 0 ? item.lightRadius : 130,
                    LightColor = (item.lightColor != null && item.lightColor.Length >= 3) 
                        ? new Color(item.lightColor[0], item.lightColor[1], item.lightColor[2], 255)
                        : new Color(255, 200, 100, 255),
                    Rarity = item.rarity ?? "common"
                };
                // Champs nutrition/hydratation : si absent, fallback compatible sur HealAmount
                itemData.HungerRestore = item.hungerRestore != 0f ? item.hungerRestore : item.healAmount * 2f;
                itemData.ThirstRestore = item.thirstRestore != 0f ? item.thirstRestore : item.healAmount * 0.5f;
                ItemDatabase[runtimeItemId] = itemData;
                if (!string.IsNullOrWhiteSpace(itemKey))
                    ItemDatabaseByKey[itemKey] = itemData;

                pendingAmmoItems.Add((runtimeItemId, item));
            }

            BuildItemNameIndex();
            foreach (var (runtimeItemId, item) in pendingAmmoItems)
            {
                if (!ItemDatabase.TryGetValue(runtimeItemId, out var itemData))
                    continue;

                itemData.AmmoIds = ResolveAmmoIds(item.ammoIds);
                ItemDatabase[runtimeItemId] = itemData;
            }
            
            int loadedCount = ItemDatabase.Count;
            int missingTextures = ItemDatabase.Values.Count(i => !HasItemTexture(i));
            Console.WriteLine($"   {loadedCount} items chargés ({missingTextures} textures manquantes)");
        }

        private static int ResolveRuntimeItemId(ItemJson item, int fallbackIndex)
        {
            if (TryReadInt(item.id, out int parsedId))
                return parsedId;

            return fallbackIndex + 1;
        }

        private static string ResolveItemKey(ItemJson item)
        {
            if (TryReadString(item.id, out var stringId) && !string.IsNullOrWhiteSpace(stringId))
                return stringId;

            if (!string.IsNullOrWhiteSpace(item.texture))
                return item.texture;

            return NormalizeItemKey(item.name);
        }

        public static string GetItemFallbackName(string? itemKey, int fallbackItemId)
        {
            if (!string.IsNullOrWhiteSpace(itemKey))
                return itemKey;

            if (fallbackItemId > 0)
                return fallbackItemId.ToString();

            return "unknown";
        }

        private static List<int> ResolveAmmoIds(JsonElement[]? ammoIds)
        {
            var resolved = new List<int>();
            if (ammoIds == null || ammoIds.Length == 0)
                return resolved;

            foreach (var element in ammoIds)
            {
                if (TryReadInt(element, out var parsedId) && parsedId > 0)
                {
                    resolved.Add(parsedId);
                    continue;
                }

                if (TryReadString(element, out var rawValue) && !string.IsNullOrWhiteSpace(rawValue))
                {
                    if (TryResolveItemId(rawValue, out var matchedId))
                        resolved.Add(matchedId);
                }
            }

            return resolved;
        }

        private static bool TryResolveItemId(string value, out int itemId)
        {
            itemId = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (int.TryParse(value, out itemId) && itemId > 0)
                return true;

            if (TryGetItemByKey(value, out var byKey))
            {
                itemId = byKey.ID;
                return true;
            }

            if (TryGetItemByName(value, out var byName))
            {
                itemId = byName.ID;
                return true;
            }

            var normalizedKey = NormalizeItemKey(value);
            if (!string.IsNullOrWhiteSpace(normalizedKey) && TryGetItemByKey(normalizedKey, out var byNormalizedKey))
            {
                itemId = byNormalizedKey.ID;
                return true;
            }

            return false;
        }

        private static bool TryReadInt(JsonElement element, out int value)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
                return true;

            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
                return true;

            value = 0;
            return false;
        }

        private static bool TryReadString(JsonElement element, out string value)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                value = element.GetString() ?? string.Empty;
                return true;
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                value = element.GetRawText();
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static string NormalizeItemKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().ToLowerInvariant()
                .Replace("é", "e")
                .Replace("è", "e")
                .Replace("ê", "e")
                .Replace("à", "a")
                .Replace("ç", "c")
                .Replace(" ", "_")
                .Replace("-", "_");
        }

        private static int GetDefaultArmorValue(ItemJson item)
        {
            if (item == null) return 0;
            if (item.armorValue > 0) return item.armorValue;

            int baseValue = item.value > 0 ? item.value : 0;
            if (baseValue <= 0) return 0;

            string[] zones = item.coveredZones ?? Array.Empty<string>();

            bool hasZone(string zone) => Array.Exists(zones, z => string.Equals(z, zone, StringComparison.OrdinalIgnoreCase));

            if (hasZone("Torso")) return Math.Max(1, baseValue / 12);
            if (hasZone("Legs")) return Math.Max(1, baseValue / 16);
            if (hasZone("Feet")) return Math.Max(1, baseValue / 20);
            if (hasZone("TopOfHead") || hasZone("Ears") || hasZone("Face")) return Math.Max(1, baseValue / 20);
            if (hasZone("Neck") || hasZone("Waist") || hasZone("Back")) return Math.Max(1, baseValue / 24);
            return Math.Max(1, baseValue / 25);
        }

        public static bool HasItemTexture(ItemData itemData)
        {
            if (string.IsNullOrEmpty(itemData.TextureName))
                return itemData.Icon.Id != 0;

            string textureName = itemData.TextureName;
            if (File.Exists($"assets/items/{textureName}.png"))
                return true;

            if (Directory.Exists("assets/items") && Directory.EnumerateFiles("assets/items", $"{textureName}_*.png").Any())
                return true;

            if (Directory.Exists("assets/equipements") && Directory.EnumerateFiles("assets/equipements", $"{textureName}_*.png").Any())
                return true;

            return itemData.Icon.Id != 0;
        }

        // Ne concerne plus que les emplacements fonctionnels (pas de couverture corporelle).
        // Retourne EquipmentSlot.MainHand par défaut ; utiliser HasEquipSlot pour savoir si
        // l'item a réellement un emplacement fonctionnel.
        private static EquipmentSlot ParseEquipmentSlot(string? slot)
        {
            if (string.IsNullOrEmpty(slot))
                return EquipmentSlot.MainHand;

            return slot.ToLowerInvariant() switch
            {
                "mainhand" or "weapon" or "main" => EquipmentSlot.MainHand,
                "offhand" or "shield" or "off" => EquipmentSlot.OffHand,
                "backpack" or "back" => EquipmentSlot.Backpack,
                "instrument" => EquipmentSlot.Instrument,
                "ammo" => EquipmentSlot.Ammo,
                "pet" => EquipmentSlot.Pet,
                "mount" => EquipmentSlot.Mount,
                _ => EquipmentSlot.MainHand
            };
        }

        //  Détermine les zones du corps couvertes par un item.
        // Priorité : liste explicite "coveredZones" dans le JSON.
        // Sinon : dérivée depuis l'ancien champ "armorCategory" (hat/helmet/mask) pour les têtes,
        // ou vide si l'item ne couvre aucune zone (arme, outil, ressource...).
        private static List<BodyZone> ParseCoveredZones(string[]? explicitZones, string? armorCategoryRaw)
        {
            if (explicitZones != null && explicitZones.Length > 0)
            {
                var zones = new List<BodyZone>();
                foreach (var z in explicitZones)
                {
                    if (Enum.TryParse<BodyZone>(z, true, out var parsed) && !zones.Contains(parsed))
                        zones.Add(parsed);
                }
                return zones;
            }

            // Compatibilité : dérive les zones de tête depuis armorCategory si présent
            if (!string.IsNullOrEmpty(armorCategoryRaw))
            {
                var cat = ParseArmorCategory(armorCategoryRaw, "", "");
                if (cat != ArmorCategory.None)
                    return GetHeadZonesForCategory(cat);
            }

            return new List<BodyZone>();
        }

        public static int GetEquipMaxForSlot(EquipmentSlot slot)
        {
            // D'abord, vérifier si un item a un maximum spécifique (via EquipMax)
            // Sinon, utiliser la valeur par défaut de la définition
            foreach (var def in EquipmentSlotDefinitions)
            {
                if (def.slot == slot)
                    return def.defaultMax;
            }
            return 1; // Par défaut, 1 item par emplacement
        }

        //  NOUVEAU : Obtenir le nom d'affichage d'un emplacement
        public static string GetSlotDisplayName(EquipmentSlot slot)
        {
            foreach (var def in EquipmentSlotDefinitions)
            {
                if (def.slot == slot)
                    return Localization.GetOrDefault($"equipment.slot.{slot.ToString().ToLowerInvariant()}", def.displayName);
            }
            return Localization.GetOrDefault($"equipment.slot.{slot.ToString().ToLowerInvariant()}", slot.ToString());
        }

        public static bool CanEquipItemInSlot(ItemData item, EquipmentSlot targetSlot)
        {
            // ItemData est une structure, elle ne peut pas être null
            // Vérifier si l'ID est 0 (signifie que l'item n'est pas valide)
            if (item.ID == 0) return false;
            return item.HasEquipSlot && item.EquipSlot == targetSlot;
        }

        //  Un item est "portable sur le corps" s'il couvre au moins une zone
        public static bool CanEquipOnBody(ItemData item) => item.ID != 0 && item.CoveredZones.Count > 0;


        private static void LoadRecipes()
        {
            string path = "Data/recipes.json";
            if (!File.Exists(path))
            {
                Console.WriteLine($" Fichier manquant: {path} - aucune recette chargée");
                return;
            }

            string json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<RecipesRoot>(json);
            
            if (root?.recipes == null || root.recipes.Count == 0)
            {
                Console.WriteLine($" Aucune recette trouvée dans {path}");
                return;
            }
            
            //  CRUCIAL : Vider la liste avant de la remplir pour éviter les doublons
            Recipes.Clear();
            
            Console.WriteLine($" Chargement des recettes depuis {path}...");
            
            foreach (var rec in root.recipes)
            {
                var ingredients = new List<(int id, int qty)>();
                if (rec.ingredients != null)
                {
                    foreach (var ing in rec.ingredients)
                    {
                        if (ing != null && ing.Length >= 2)
                        {
                            int ingredientId = 0;
                            if (ing[0].ValueKind == JsonValueKind.Number && ing[0].TryGetInt32(out var parsedIngredientId))
                            {
                                ingredientId = parsedIngredientId;
                            }
                            else if (ing[0].ValueKind == JsonValueKind.String)
                            {
                                var ingredientKey = ing[0].GetString() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(ingredientKey))
                                {
                                    if (GameData.TryGetItemByKey(ingredientKey, out var itemByKey))
                                        ingredientId = itemByKey.ID;
                                    else if (GameData.TryGetItemByName(ingredientKey, out var itemByName))
                                        ingredientId = itemByName.ID;
                                }
                            }
                            if (ingredientId != 0)
                                ingredients.Add((ingredientId, ing[1].GetInt32()));
                        }
                    }
                }
                
                int resultId = 0;
                if (rec.resultId.ValueKind == JsonValueKind.Number && rec.resultId.TryGetInt32(out var parsedResultId))
                {
                    resultId = parsedResultId;
                }
                else if (rec.resultId.ValueKind == JsonValueKind.String)
                {
                    var resultKey = rec.resultId.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(resultKey))
                    {
                        if (GameData.TryGetItemByKey(resultKey, out var resultByKey))
                            resultId = resultByKey.ID;
                        else if (GameData.TryGetItemByName(resultKey, out var resultByName))
                            resultId = resultByName.ID;
                    }
                }

                string resultName = string.IsNullOrEmpty(rec.resultName) ? 
                    (ItemDatabase.TryGetValue(resultId, out var item) ? item.Name : GetItemFallbackName(GetItemKey(resultId), resultId))
                    : rec.resultName;
                string station = string.IsNullOrEmpty(rec.station) ? "" : rec.station;
                string category = rec.category ?? "basic";
                string resultMetadata = string.IsNullOrWhiteSpace(rec.resultMetadata) ? "" : rec.resultMetadata;
                
                Color? resultColor = null;
                if (rec.resultColor != null && rec.resultColor.Length == 4)
                {
                    int r = rec.resultColor[0];
                    int g = rec.resultColor[1];
                    int b = rec.resultColor[2];
                    int a = rec.resultColor[3];

                    //  Une recette sans tag spécial doit être identique à un item droppé
                    // classique. Le blanc par défaut du JSON (255,255,255,255) ne doit pas
                    // être interprété comme une couleur personnalisée spéciale.
                    if (!(r == 255 && g == 255 && b == 255 && a == 255))
                        resultColor = new Color(r, g, b, a);
                }

                var craftRecipe = new CraftRecipe(
                    resultName, resultId, rec.resultCount,
                    ingredients, station, category, resultColor,
                    rec.ritual, rec.bloodEssenceCost, rec.isHidden, rec.unlockCondition
                );
                craftRecipe.ResultMetadata = resultMetadata;
                //  Id STABLE et UNIQUE de la recette (le "id" du recipes.json), à ne pas
                // confondre avec resultId (l'id de l'ITEM produit, partagé par toutes les
                // recettes de potions puisqu'elles produisent le même bocal). CauldronUI
                // s'appuie sur ce champ pour savoir quelles recettes sont apprises.
                craftRecipe.Id = rec.id;
                Recipes.Add(craftRecipe);
            }
            
            Console.WriteLine($"   {Recipes.Count} recettes chargées ({Recipes.Count(r => r.IsRitual)} rituelles)");

            //  Avertir si deux recettes partagent le même Id (romprait le suivi "recette apprise" dans CauldronUI)
            var duplicateIds = Recipes.GroupBy(r => r.Id).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var dupId in duplicateIds)
                Console.WriteLine($" ATTENTION: id de recette {dupId} utilisé plusieurs fois dans recipes.json");
        }

        private static Texture2D LoadTexture(string path)
        {
            if (File.Exists(path))
            {
                var tex = Raylib.LoadTexture(path);
                if (tex.Id != 0) return tex;
            }
            return new Texture2D();
        }

        private static ItemType ParseItemType(string type) => type?.ToLower() switch
        {
            "resource" => ItemType.Resource,
            "tool" => ItemType.Tool,
            "weapon" => ItemType.Weapon,
            "armor" => ItemType.Armor,
            "food" => ItemType.Food,
            "placeable" => ItemType.Placeable,
            "throwable" => ItemType.Throwable,
            "wallcovering" => ItemType.WallCovering,
            "backpack" => ItemType.Backpack,
            "instrument" => ItemType.Instrument,
            "rune" => ItemType.Rune,
            _ => ItemType.Resource
        };

        private static ToolType ParseToolType(string type) => type?.ToLower() switch
        {
            "axe" => ToolType.Axe,
            "pickaxe" => ToolType.Pickaxe,
            "shovel" => ToolType.Shovel,
            "hoe" => ToolType.Hoe,
            "scythe" => ToolType.Scythe,
            "hammer" => ToolType.Hammer,
            "scissors" => ToolType.Scissors,
            "wateringcan" => ToolType.WateringCan,
            "fishing" => ToolType.Fishing,
            "net" => ToolType.Net,
            _ => ToolType.None
        };

        private static WeaponCategory ParseWeaponCategory(string category) => category?.ToLower() switch
        {
            "melee" => WeaponCategory.Melee,
            "ranged" => WeaponCategory.Ranged,
            _ => WeaponCategory.None
        };

        private static ArmorCategory ParseArmorCategory(string category) => category?.ToLower() switch
        {
            "hat" => ArmorCategory.Hat,
            "helmet" => ArmorCategory.Helmet,
            "mask" => ArmorCategory.Mask,
            _ => ArmorCategory.None
        };

        private static ArmorCategory ParseArmorCategory(string category, string textureName, string itemName)
        {
            var parsed = ParseArmorCategory(category);
            if (parsed != ArmorCategory.None)
                return parsed;

            string combined = $"{textureName}|{itemName}".ToLowerInvariant();
            if (combined.Contains("mask") || combined.Contains("glasses"))
                return ArmorCategory.Mask;
            if (combined.Contains("helmet") || combined.Contains("helm") || combined.Contains("casque"))
                return ArmorCategory.Helmet;
            if (combined.Contains("hat") || combined.Contains("chapeau") || combined.Contains("cap") || combined.Contains("hood") || combined.Contains("bonnet"))
                return ArmorCategory.Hat;
            return ArmorCategory.None;
        }

        public static void BuildItemNameIndex()
        {
            ItemDatabaseByName.Clear();
            foreach (var kv in ItemDatabase)
            {
                var name = kv.Value.Name;
                if (!string.IsNullOrEmpty(name) && !ItemDatabaseByName.ContainsKey(name))
                    ItemDatabaseByName[name] = kv.Value;
            }
        }

        public static bool TryGetItemByName(string name, out ItemData itemData)
        {
            if (string.IsNullOrEmpty(name))
            {
                itemData = default;
                return false;
            }

            if (ItemDatabaseByName.TryGetValue(name, out itemData))
                return true;

            if (ItemDatabaseByKey.TryGetValue(name, out itemData))
                return true;

            return false;
        }

        public static bool TryGetItemByKey(string key, out ItemData itemData)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                itemData = default;
                return false;
            }

            return ItemDatabaseByKey.TryGetValue(key, out itemData);
        }

        public static string GetItemKey(int itemId)
        {
            if (ItemDatabase.TryGetValue(itemId, out var itemData))
                return itemData.Key;
            return string.Empty;
        }

        public static int GetItemId(string name)
        {
            if (TryGetItemByName(name, out var itemData))
                return itemData.ID;
            return 0;
        }

        // Méthodes utilitaires
        public static int GetAttack(string itemName)
        {
            if (TryGetItemByName(itemName, out var itemData))
                return itemData.AttackBonus;
            return 0;
        }

        public static ToolType GetToolType(string itemName)
        {
            if (TryGetItemByName(itemName, out var itemData))
                return itemData.ToolKind;
            return ToolType.None;
        }

        public static WeaponCategory GetWeaponCategory(string itemName)
        {
            if (TryGetItemByName(itemName, out var itemData))
                return itemData.WeaponCategory;
            return WeaponCategory.None;
        }

        public static bool IsRangedWeapon(string itemName)
        {
            return GetWeaponCategory(itemName) == WeaponCategory.Ranged;
        }

        public static List<int> GetAmmoIdsForWeapon(string itemName)
        {
            if (TryGetItemByName(itemName, out var itemData))
                return new List<int>(itemData.AmmoIds);
            return new List<int>();
        }

        public static float GetMiningPower(string itemName)
        {
            if (TryGetItemByName(itemName, out var itemData))
                return itemData.MiningPower > 0 ? itemData.MiningPower : 1f;
            return 1f;
        }

        public static int GetWaterCapacity(string itemName)
        {
            if (TryGetItemByName(itemName, out var itemData))
                return itemData.WaterCapacity;
            return 0;
        }

        public static bool CanContainWater(string itemName)
        {
            return GetWaterCapacity(itemName) > 0 || TryGetItemByName(itemName, out var itemData) && itemData.Key == "bucket";
        }
        
        public static ItemType GetItemType(string itemName)
        {
            if (TryGetItemByName(itemName, out var itemData))
                return itemData.Type;
            return ItemType.None;
        }

        //  Tire les drops d'une espèce à l'instant de l'appel (chance + quantité + métadonnées
        // par entrée). Voir SpeciesData.GetDrops pour le détail du tirage.
        public static List<(int id, int qty, Dictionary<string, string>? meta)> GetAnimalDrops(string species)
        {
            return SpeciesData.GetDrops(species);
        }

        public static ArmorCategory GetArmorCategory(string itemName)
        {
            if (TryGetItemByName(itemName, out var itemData))
                return itemData.ArmorCategory;
            return ArmorCategory.None;
        }

        //  NOUVEAU : Obtenir l'emplacement d'équipement d'un item
        public static EquipmentSlot GetEquipSlot(string itemName)
        {
            if (TryGetItemByName(itemName, out var itemData))
                return itemData.EquipSlot;
            return EquipmentSlot.MainHand; // Fallback
        }

        //  NOUVEAU : Obtenir le nombre maximal d'équipement pour un item
        public static int GetEquipMax(string itemName)
        {
            if (TryGetItemByName(itemName, out var itemData))
                return itemData.EquipMax;
            return 1;
        }

        public static Color GetRarityColor(string rarity)
        {
            return rarity?.ToLower() switch
            {
                "common" => new Color(180, 180, 180, 255),     // Gris
                "uncommon" => new Color(100, 200, 100, 255),   // Vert
                "rare" => new Color(80, 160, 255, 255),        // Bleu
                "epic" => new Color(200, 100, 255, 255),       // Violet
                "legendary" => new Color(255, 180, 50, 255),   // Or
                _ => new Color(200, 200, 200, 255)             // Gris par défaut
            };
        }

        // Classes pour la désérialisation JSON
        private class ItemsRoot 
        { 
            public List<ItemJson> items { get; set; } = new(); 
        }
        
        private class ItemJson
        {
            public JsonElement id { get; set; }
            public string name { get; set; } = "";
            public Dictionary<string, int[]> metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public string type { get; set; } = "";
            public bool isThrowable { get; set; }
            public int stackSize { get; set; } = 99;
            public int value { get; set; }
            public int healAmount { get; set; }
            public float hungerRestore { get; set; } = 0f;
            public float thirstRestore { get; set; } = 0f;
            public int attackBonus { get; set; }
            public float miningPower { get; set; } = 1f;
            public string toolType { get; set; } = "";
            public int waterCapacity { get; set; } = 0;
            public int placeableId { get; set; }
            public string armorSlot { get; set; } = "";
            public int armorValue { get; set; }
            public string texture { get; set; } = "";
            public string textureName { get; set; } = "";
            public string armorCategory { get; set; } = "";
            public bool isLightSource { get; set; }
            public int lightRadius { get; set; }
            public bool isGroundTile { get; set; }
            public int[] lightColor { get; set; } = new int[3] { 255, 220, 150 };
            public bool isSeed { get; set; }
            public int cropId { get; set; }
            public int growthTime { get; set; }
            public float fertilizerBoost { get; set; } = 0f;
            public bool dyeable { get; set; }
            public int dyeLayers { get; set; } = 1;
            public int growthStages { get; set; }
            public string plantType { get; set; } = "farmland";
            public bool cookable { get; set; }
            public string cookedItem { get; set; } = "";
            public int backpackSlots { get; set; }
            public int backpackRows { get; set; }
            public string rarity { get; set; } = "common";
            public string equipSlot { get; set; } = "";      // Emplacements fonctionnels uniquement (MainHand, OffHand, Backpack, Instrument, Ammo, Pet, Mount)
            public int equipMax { get; set; } = 1;
            public string weaponCategory { get; set; } = "";
            public int maxAmmoPerCharge { get; set; } = 1;
            public JsonElement[]? ammoIds { get; set; } = null;
            public float fireRate { get; set; } = 1f; // Débit de tir en projectiles/seconde pour les armes de distance
            public string[]? coveredZones { get; set; } = null; //  Zones du corps couvertes par cet item (TopOfHead, Ears, Face, Neck, Torso, Back, Legs, Feet)
            public int warmth { get; set; } = 0;
            public int cooling { get; set; } = 0;
            public int runeSlots { get; set; } = 0; //  Nombre d'emplacements de runes (armures uniquement)
        }

        private class RecipesRoot 
        { 
            public List<RecipeJson> recipes { get; set; } = new(); 
        }
        
        private class RecipeJson
        {
            public int id { get; set; }
            public JsonElement resultId { get; set; }
            public string resultName { get; set; } = "";
            public int resultCount { get; set; } = 1;
            public List<JsonElement[]> ingredients { get; set; } = new();
            public string station { get; set; } = "";
            public string category { get; set; } = "";
            public string resultMetadata { get; set; } = "";
            public int[]? resultColor { get; set; } = null;
            
            public bool ritual { get; set; } = false;
            public int bloodEssenceCost { get; set; } = 0;
            public bool isHidden { get; set; } = false;
            public string unlockCondition { get; set; } = "";
        }
    }

    public struct ItemData
    {
        public int ID;
        public string Name;
        public string Description;
        public string Rarity;
        public Dictionary<string, int[]> Metadata;
        // Compatibilite de source : la couleur native vit dans Metadata["color"].
        public Color GetColor()
        {
            if (Metadata != null && Metadata.TryGetValue("color", out var value) && value.Length >= 4)
                return new Color(value[0], value[1], value[2], value[3]);
            return Raylib_cs.Color.White;
        }

        public Color Color => GetColor();

        public Texture2D Icon;
        public ItemType Type;
        public bool IsThrowable;
        public int StackSize;
        public int Value;
        public int HealAmount;
        public float HungerRestore;
        public float ThirstRestore;
        public int AttackBonus;
        public float MiningPower;
        public ToolType ToolKind;
        public int WaterCapacity;
        public bool IsPlaceable;
        public int PlaceableId;
        public string ArmorSlot; //  À SUPPRIMER PROGRESSIVEMENT (remplacé par EquipSlot)
        public int Warmth;
        public int Cooling;
        public int ArmorValue;
        public ArmorCategory ArmorCategory;
        public string Key;
        public string TextureName;
        public bool IsGroundTile;
        public bool IsDyeable;
        public int DyeLayers;
        public int BackpackSlots;
        public int BackpackRows;
        public bool IsSeed;
        public int CropId;
        public int GrowthTime;
        public int GrowthStages;
        public float FertilizerBoost;
        public string PlantType;
        public bool Cookable;
        public string CookedItemKey;
        public bool IsLightSource;
        public int LightRadius;
        public Color LightColor;
        public bool IsInstrument => Type == ItemType.Instrument;
        public bool IsRangedWeapon => Type == ItemType.Weapon && WeaponCategory == WeaponCategory.Ranged;
        
        //  Emplacement fonctionnel (arme, sac, monture...) - optionnel, voir HasEquipSlot
        public EquipmentSlot EquipSlot;
        public bool HasEquipSlot;
        public int EquipMax; // Nombre maximal d'items de ce type qu'on peut équiper en même temps (emplacements fonctionnels)
        public WeaponCategory WeaponCategory;
        public int MaxAmmoPerCharge;
        public List<int> AmmoIds;
        public float FireRate;

        //  Zones du corps couvertes par cet item (système d'équipement unifié)
        public List<BodyZone> CoveredZones;

        //  SYSTÈME DE RUNES : nombre d'emplacements de runes que possède cette pièce d'armure
        public int RuneSlots;
        public bool IsRune => Type == ItemType.Rune;

        // Constructeur avec valeurs par défaut
        public ItemData(int dyeLayers = 1)
        {
            ID = 0;
            Name = "";
            Description = "";
            Rarity = "common";
            Metadata = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            Icon = new Texture2D();
            Type = ItemType.Resource;
            IsThrowable = false;
            StackSize = 99;
            Value = 0;
            HealAmount = 0;
            AttackBonus = 0;
            MiningPower = 1f;
            ToolKind = ToolType.None;
            WaterCapacity = 0;
            IsPlaceable = false;
            PlaceableId = 0;
            ArmorSlot = "";
            ArmorValue = 0;
            ArmorCategory = ArmorCategory.None;
            Key = string.Empty;
            TextureName = "";
            IsGroundTile = false;
            IsDyeable = false;
            DyeLayers = dyeLayers;
            BackpackSlots = 0;
            BackpackRows = 0;
            IsSeed = false;
            CropId = 0;
            GrowthTime = 0;
            GrowthStages = 0;
            FertilizerBoost = 0f;
            PlantType = "farmland";
            Cookable = false;
            CookedItemKey = "";
            IsLightSource = false;
            LightRadius = 130;
            LightColor = new Color(255, 200, 100, 255);
            
            //  NOUVEAUX CHAMPS
            EquipSlot = EquipmentSlot.MainHand;
            HasEquipSlot = false;
            EquipMax = 1;
            WeaponCategory = WeaponCategory.None;
            MaxAmmoPerCharge = 1;
            AmmoIds = new List<int>();
            FireRate = 1f;
            CoveredZones = new List<BodyZone>();
            Warmth = 0;
            HungerRestore = 0f;
            ThirstRestore = 0f;
            RuneSlots = 0;
        }
    }
}