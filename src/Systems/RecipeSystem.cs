// RecipeSystem.cs - Système complet de gestion des recettes
#nullable enable
using Raylib_cs;
using System.Text.Json;

namespace Soulfract
{
    public static class RecipeSystem
    {
        private static Dictionary<int, RecipeData> _recipeItems = new Dictionary<int, RecipeData>();
        private static HashSet<int> _unlockedRecipes = new HashSet<int>();
        private static HashSet<int> _unlockedCauldronRecipes = new HashSet<int>();
        private static Dictionary<string, int> _recipeNameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static string _savePath = Path.Combine("Saves", "unlocked_recipes.json");
        
        public class RecipeData
        {
            public int RecipeId { get; set; }
            public int ItemId { get; set; }
            public string DisplayName { get; set; } = "";
            public string Description { get; set; } = "";
            public int TargetRecipeId { get; set; }
            public string RequiresStation { get; set; } = "";
            public bool IsDiscovered { get; set; } = false;
            public DateTime? DiscoveredAt { get; set; }
            public Color Color { get; set; } = Color.Gold;
        }
        
        public static void Initialize()
        {
            LoadUnlockedRecipes();
            RegisterRecipeItems();
            MigrateUnlockedCauldronRecipes();
            BuildRecipeNameIndex();
        }
        
        private static void RegisterRecipeItems()
        {
            _recipeItems.Clear();
            
            int recipeScrollItemId = GameData.GetItemId("recipe_scroll");

            // Recettes du chaudron
            RegisterRecipe(recipeScrollItemId, 1001, "Recette de potion de soin", "Mélangez des fleurs rouges et des champignons.", 100, "cauldron");
            RegisterRecipe(recipeScrollItemId, 1002, "Recette de colorant mystérieux", "Associez des fleurs rouge, blanche et un champignon.", 60, "workbench");
            RegisterRecipe(recipeScrollItemId, 1003, "Recette de breuvage étrange", "Une combinaison secrète de plantes.", 186, "cauldron");
            RegisterRecipe(recipeScrollItemId, 1013, "Recette de la potion de force", "Champignons infusés avec un rubis broyé.", 103, "cauldron");
            RegisterRecipe(recipeScrollItemId, 1014, "Recette de la potion de résistance", "Pierre et onyx en poudre.", 104, "cauldron");
            RegisterRecipe(recipeScrollItemId, 1015, "Recette de la potion de lumière", "Fleurs blanches et éclat de topaze.", 105, "cauldron");
            RegisterRecipe(recipeScrollItemId, 1016, "Recette de la potion de résistance à la chaleur", "Rose des sables et gemme orange.", 106, "cauldron");
            RegisterRecipe(recipeScrollItemId, 1017, "Recette de la potion de résistance au froid", "Champignon glacé et saphir.", 107, "cauldron");
            RegisterRecipe(recipeScrollItemId, 1018, "Recette de la potion d'invisibilité", "Un secret que peu osent préparer.", 108, "cauldron");
            
            // Recettes d'établi
            RegisterRecipe(recipeScrollItemId, 1004, "Recette de l'épée en fer", "Forgez une épée avec des lingots de fer.", 18, "workbench");
            RegisterRecipe(recipeScrollItemId, 1005, "Recette de la machine à vapeur", "Assemblez des pièces métalliques complexes.", 401, "workbench");
            RegisterRecipe(recipeScrollItemId, 1006, "Recette de la dynamite", "Composition explosive.", 64, "workbench");
            
            // Recettes d'enclume
            RegisterRecipe(recipeScrollItemId, 1007, "Recette de la baguette magique", "Un savoir arcanique ancien.", 400, "anvil");
            RegisterRecipe(recipeScrollItemId, 1008, "Recette du casque de croisé", "Un secret de forge ancestral.", 134, "anvil");
            RegisterRecipe(recipeScrollItemId, 1009, "Recette du plastron de croisé", "Forge avancée.", 135, "anvil");
            RegisterRecipe(recipeScrollItemId, 1010, "Recette des jambières de croisé", "Protection des jambes.", 136, "anvil");
            
            // Recettes supplémentaires
            RegisterRecipe(recipeScrollItemId, 1011, "Recette de l'épée en pierre", "Arme tranchante.", 17, "workbench");
            RegisterRecipe(recipeScrollItemId, 1012, "Recette de l'épée en bois", "Arme basique.", 16, "workbench");
        }
        
        private static void RegisterRecipe(int itemId, int recipeId, string displayName, string description, int targetRecipeId, string requiresStation)
        {
            var recipeData = new RecipeData
            {
                RecipeId = recipeId,
                ItemId = itemId,
                DisplayName = displayName,
                Description = description,
                TargetRecipeId = targetRecipeId,
                RequiresStation = requiresStation,
                IsDiscovered = _unlockedRecipes.Contains(recipeId),
                Color = GetRecipeColor(requiresStation)
            };
            
            _recipeItems[recipeId] = recipeData;
        }

        private static void MigrateUnlockedCauldronRecipes()
        {
            foreach (var recipeData in _recipeItems.Values)
            {
                if (recipeData.RequiresStation != "cauldron" || !recipeData.IsDiscovered)
                    continue;

                if (GameData.Recipes.Any(recipe =>
                    recipe.Id == recipeData.TargetRecipeId && recipe.RequiresStation == "cauldron"))
                    _unlockedCauldronRecipes.Add(recipeData.TargetRecipeId);
            }

            foreach (var recipe in GameData.Recipes.Where(recipe =>
                recipe.RequiresStation == "cauldron" && _unlockedCauldronRecipes.Contains(recipe.Id)))
            {
                var matchingRecipeData = _recipeItems.Values.FirstOrDefault(data =>
                    data.RequiresStation == "cauldron" && data.TargetRecipeId == recipe.Id);
                if (matchingRecipeData == null) continue;

                matchingRecipeData.IsDiscovered = true;
                _unlockedRecipes.Add(matchingRecipeData.RecipeId);
            }
        }

        public static bool IsCauldronRecipeLearned(int recipeId) =>
            _unlockedCauldronRecipes.Contains(recipeId);

        public static void LearnCauldronRecipe(int recipeId)
        {
            if (!GameData.Recipes.Any(recipe =>
                recipe.Id == recipeId && recipe.RequiresStation == "cauldron"))
                return;

            if (!_unlockedCauldronRecipes.Add(recipeId))
                return;

            foreach (var recipeData in _recipeItems.Values.Where(data =>
                data.RequiresStation == "cauldron" && data.TargetRecipeId == recipeId))
            {
                recipeData.IsDiscovered = true;
                recipeData.DiscoveredAt ??= DateTime.Now;
                _unlockedRecipes.Add(recipeData.RecipeId);
            }

            SaveUnlockedRecipes();
        }
        
        private static void BuildRecipeNameIndex()
        {
            _recipeNameToId.Clear();
            foreach (var kv in _recipeItems)
            {
                var data = kv.Value;
                string key = data.DisplayName.ToLowerInvariant();
                _recipeNameToId[key] = data.RecipeId;
                
                string shortKey = data.DisplayName
                    .Replace("Recette de ", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Recette ", "", StringComparison.OrdinalIgnoreCase)
                    .ToLowerInvariant();
                if (!_recipeNameToId.ContainsKey(shortKey) && !string.IsNullOrWhiteSpace(shortKey))
                    _recipeNameToId[shortKey] = data.RecipeId;
            }
        }
        
        private static Color GetRecipeColor(string station)
        {
            return station switch
            {
                "cauldron" => new Color(100, 200, 150, 255),
                "workbench" => new Color(200, 180, 100, 255),
                "anvil" => new Color(200, 150, 100, 255),
                "furnace" => new Color(255, 150, 80, 255),
                _ => Color.Gold
            };
        }
        
        public static bool IsRecipeItem(int itemId)
        {
            return _recipeItems.Values.Any(recipe => recipe.ItemId == itemId);
        }
        
        public static RecipeData? GetRecipeData(int itemId)
        {
            return _recipeItems.Values.FirstOrDefault(recipe => recipe.ItemId == itemId);
        }

        public static RecipeData? GetRecipeData(Item item)
        {
            if (item == null)
                return null;

            if (item.Meta.TryGetValue("recipeId", out var recipeIdText) &&
                int.TryParse(recipeIdText, out int recipeId) &&
                _recipeItems.TryGetValue(recipeId, out var recipe))
            {
                return recipe;
            }

            return GetRecipeData(GameData.GetItemId(item.Name));
        }
        
        public static int? GetRecipeIdByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            
            string key = name.ToLowerInvariant();
            
            if (_recipeNameToId.TryGetValue(key, out int id))
                return id;
            
            foreach (var kv in _recipeNameToId)
            {
                if (kv.Key.Contains(key))
                    return kv.Value;
            }
            
            return null;
        }
        
        public static RecipeData? CreateRecipeItemFor(int recipeId)
        {
            var recipe = _recipeItems.Values.FirstOrDefault(r => r.RecipeId == recipeId);
            if (recipe == null) return null;
            
            return new RecipeData
            {
                RecipeId = recipe.RecipeId,
                ItemId = recipe.ItemId,
                DisplayName = recipe.DisplayName,
                Description = recipe.Description,
                TargetRecipeId = recipe.TargetRecipeId,
                RequiresStation = recipe.RequiresStation,
                IsDiscovered = false,
                Color = recipe.Color
            };
        }
        
        public static List<string> GetAvailableRecipeNames()
        {
            return _recipeItems.Values
                .Select(r => r.DisplayName)
                .OrderBy(n => n)
                .ToList();
        }
        
        public static List<RecipeData> GetAllRecipes()
        {
            return _recipeItems.Values.ToList();
        }
        
        public static bool DiscoverRecipe(int itemId)
        {
            var recipeData = GetRecipeData(itemId);
            if (recipeData == null)
                return false;
            
            if (recipeData.IsDiscovered)
            {
                Program.AddNotification(new Notification($" Vous connaissez déjà cette recette : {recipeData.DisplayName}", new Color(200, 200, 100, 255), 2f));
                return false;
            }
            
            recipeData.IsDiscovered = true;
            recipeData.DiscoveredAt = DateTime.Now;
            _unlockedRecipes.Add(recipeData.RecipeId);
            
            //  SYNCHRONISER AVEC LE CHAUDRON
            if (recipeData.RequiresStation == "cauldron")
            {
                CauldronUI.LearnRecipe(recipeData.TargetRecipeId);
            }
            
            SaveUnlockedRecipes();
            
            Program.AddNotification(new Notification($" Recette débloquée : {recipeData.DisplayName} !", new Color(100, 255, 200, 255), 3f));
            return true;
        }

        public static bool DiscoverRecipe(Item item)
        {
            var recipeData = GetRecipeData(item);
            if (recipeData == null)
                return false;

            return DiscoverRecipe(recipeData.RecipeId);
        }
        
        public static List<RecipeData> GetDiscoveredRecipes(string station = "")
        {
            if (string.IsNullOrEmpty(station))
            {
                return _recipeItems.Values
                    .Where(r => r.IsDiscovered)
                    .OrderBy(r => r.DisplayName)
                    .ToList();
            }
            
            return _recipeItems.Values
                .Where(r => r.IsDiscovered && r.RequiresStation == station)
                .OrderBy(r => r.DisplayName)
                .ToList();
        }
        
        public static List<RecipeData> GetUndiscoveredRecipes(string station = "")
        {
            if (string.IsNullOrEmpty(station))
            {
                return _recipeItems.Values
                    .Where(r => !r.IsDiscovered)
                    .OrderBy(r => r.DisplayName)
                    .ToList();
            }
            
            return _recipeItems.Values
                .Where(r => !r.IsDiscovered && r.RequiresStation == station)
                .OrderBy(r => r.DisplayName)
                .ToList();
        }
        
        public static int GetRecipeCount()
        {
            return _recipeItems.Count;
        }
        
        public static int GetDiscoveredCount()
        {
            return _unlockedRecipes.Count;
        }
        
        private static void LoadUnlockedRecipes()
        {
            _unlockedRecipes.Clear();
            _unlockedCauldronRecipes.Clear();
            
            if (!File.Exists(_savePath))
                return;
            
            try
            {
                string json = File.ReadAllText(_savePath);
                var data = JsonSerializer.Deserialize<UnlockedRecipesData>(json);
                if (data != null)
                {
                    foreach (var id in data.RecipeIds)
                        _unlockedRecipes.Add(id);
                    foreach (var id in data.CauldronRecipeIds)
                        _unlockedCauldronRecipes.Add(id);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erreur lors du chargement des recettes : {ex.Message}");
            }
        }
        
        public static void SaveUnlockedRecipes()
        {
            try
            {
                var data = new UnlockedRecipesData
                {
                    RecipeIds = _unlockedRecipes.ToList(),
                    CauldronRecipeIds = _unlockedCauldronRecipes.ToList(),
                    LastSave = DateTime.Now
                };
                
                Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_savePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erreur lors de la sauvegarde des recettes : {ex.Message}");
            }
        }
        
        public static void ResetAllRecipes()
        {
            _unlockedRecipes.Clear();
            _unlockedCauldronRecipes.Clear();
            foreach (var recipe in _recipeItems.Values)
            {
                recipe.IsDiscovered = false;
                recipe.DiscoveredAt = null;
            }
            
            CauldronUI.ResetLearnedRecipes();
            
            SaveUnlockedRecipes();
            Program.AddNotification(new Notification(" Toutes les recettes ont été réinitialisées", new Color(200, 200, 100, 255), 2f));
        }
        
        public static void UnlockAllRecipes()
        {
            foreach (var recipe in _recipeItems.Values)
            {
                if (!recipe.IsDiscovered)
                {
                    recipe.IsDiscovered = true;
                    recipe.DiscoveredAt = DateTime.Now;
                    _unlockedRecipes.Add(recipe.RecipeId);
                    
                    if (recipe.RequiresStation == "cauldron")
                    {
                        CauldronUI.LearnRecipe(recipe.TargetRecipeId);
                    }
                }
            }
            
            SaveUnlockedRecipes();
            Program.AddNotification(new Notification($" {GetDiscoveredCount()} recettes débloquées !", new Color(100, 255, 200, 255), 3f));
        }
        
        private class UnlockedRecipesData
        {
            public List<int> RecipeIds { get; set; } = new();
            public List<int> CauldronRecipeIds { get; set; } = new();
            public DateTime LastSave { get; set; }
        }
    }
}