using System.Text.Json;
using Raylib_cs;
using System.Linq;

namespace Soulfract
{
    public static class SpeciesData
    {
        public static Dictionary<string, SpeciesInfo> Species = new();
        public static Dictionary<string, List<AnimalBodyPart>> Skeletons = new();
        
        public static Dictionary<string, float> LegHeights = new();
        public static Dictionary<string, HashSet<string>> LegPartNames = new();

        //  CARACTÈRES ALÉATOIRES : [speciesKey][featureName][partName] -> variantes disponibles
        public static Dictionary<string, Dictionary<string, Dictionary<string, List<Texture2D>>>> FeatureTextures
            = new(StringComparer.OrdinalIgnoreCase);

        private static HashSet<(string speciesKey, string featureName, string partName)> _sharedFeatureTextureParts = new();
        
        public static void LoadFromFile()
        {
            string path = "Data/species.json";
            if (!File.Exists(path))
            {
                CreateDefaultSpecies();
                return;
            }
            
            string json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<SpeciesRoot>(json);
            
            try { Console.Clear(); } catch { }
            Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                         CHARGEMENT DES ESPÈCES                      ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            
            if (root?.species != null)
            {
                foreach (var sp in root.species)
                {
                    string speciesKey = sp.name.ToLower();
                    string folderName = sp.name;
                    
                    // Le format JSON des drops (dictionnaire clé=id d'item -> LootDrop) correspond
                    // désormais directement à SpeciesInfo.Drops, plus besoin de conversion manuelle.
                    var drops = sp.drops ?? new Dictionary<string, LootDrop>();
                    
                    // Déterminer la vitesse de course (runSpeed) avec fallback
                    float runSpeed = sp.runSpeed;
                    if (runSpeed <= 0)
                    {
                        // Fallback : si runSpeed absent, on utilise speed * 1.5
                        runSpeed = sp.speed * 1.5f;
                    }
                    
                    Species[speciesKey] = new SpeciesInfo
					{
						Name = sp.name,
						Type = sp.type,
                        IsHumanoid = sp.isHumanoid,
						Scale = sp.scale,
						Speed = sp.speed,
						RunSpeed = runSpeed,
						MaxHp = sp.maxHp,
						Attack = sp.attack,
						Behavior = sp.behavior,
                        VisionRange = sp.visionRange,
						AlertDuration = sp.alertDuration,
						Drops = drops,
						PreferredFoodId = ResolvePreferredFoodId(sp.preferredFoodId, sp.name),
						AttackCooldown = sp.attackCooldown > 0 ? sp.attackCooldown : 1.0f,
						GrowthTime = sp.growthTime,
						CanBeCaught = sp.canBeCaught,
						IsLightSource = sp.isLightSource,
						LightRadius = sp.lightRadius > 0 ? sp.lightRadius : 120,
						LightColor = (sp.lightColor != null && sp.lightColor.Length >= 3) 
							? new Color(sp.lightColor[0], sp.lightColor[1], sp.lightColor[2], 255)
							: new Color(255, 200, 100, 255),
						Mountable = sp.mountable,   // ← AJOUTER CETTE LIGNE
						RandomFeatures = sp.randomFeatures ?? new(),
						ColorMin = (sp.colorMin != null && sp.colorMin.Length >= 3)
							? new Color(sp.colorMin[0], sp.colorMin[1], sp.colorMin[2], 255) : Color.White,
						ColorMax = (sp.colorMax != null && sp.colorMax.Length >= 3)
							? new Color(sp.colorMax[0], sp.colorMax[1], sp.colorMax[2], 255) : Color.White,
						Tintable = sp.tintable,
						EyeColorMin = ToColor(sp.eyeColorMin),
						EyeColorMax = ToColor(sp.eyeColorMax),
						Portable = sp.portable,
						PresetChance = Math.Clamp(sp.presetChance, 0f, 1f),
						ColorPresets = (sp.colorPresets ?? new()).Select(ConvertPreset).ToList()
					};
                    

				// Résoudre les clés de drops : la JSON peut fournir des IDs numériques
				// sous forme de string, des keys (texture/key) ou des noms localisés.
				// Convertir en une table dont les clés sont des IDs numériques sous forme
				// de string (conforme aux attentes du reste du code).
				if (Species[speciesKey].Drops != null && Species[speciesKey].Drops.Count > 0)
				{
					var original = Species[speciesKey].Drops;
					var resolved = new Dictionary<string, LootDrop>();
					foreach (var kv in original)
					{
						int resolvedId = 0;
						var key = kv.Key ?? string.Empty;
						if (int.TryParse(key, out var nid))
						{
							resolvedId = nid;
						}
						else
						{
							// Essayer par key (ItemDatabaseByKey)
							if (GameData.TryGetItemByKey(key, out var byKey))
								resolvedId = byKey.ID;
							// Essayer par name (ItemDatabaseByName)
							else if (GameData.TryGetItemByName(key, out var byName))
								resolvedId = byName.ID;
							else
							{
								// Recherche par nom localisé
								var match = GameData.ItemDatabase.Values.FirstOrDefault(i => string.Equals(i.Name, key, StringComparison.OrdinalIgnoreCase));
								if (match.ID != 0) resolvedId = match.ID;
							}
						}
						if (resolvedId != 0)
							resolved[resolvedId.ToString()] = kv.Value;
						else
							Console.WriteLine($"⚠️ Impossible de résoudre le drop '{kv.Key}' pour l'espèce {sp.name}");
					}
					Species[speciesKey].Drops = resolved;
				}

                    // Construire le squelette
                    var skeleton = new List<AnimalBodyPart>();
                    if (sp.skeleton != null)
                    {
                        foreach (var part in sp.skeleton)
                        {
                            var bodyPart = new AnimalBodyPart(part.name, part.baseX, part.baseY, part.baseRot, part.parent, sp.scale);
                            
                            string texPath = $"assets/animals/{folderName}/{folderName}{part.name}.png";
                            if (File.Exists(texPath))
                                bodyPart.Texture = Raylib.LoadTexture(texPath);
                            else
                            {
                                string fallbackPath = $"assets/animals/{folderName}/{part.name}.png";
                                if (File.Exists(fallbackPath))
                                    bodyPart.Texture = Raylib.LoadTexture(fallbackPath);
                            }
                            
                            skeleton.Add(bodyPart);
                        }
                    }
                    
                    // Déterminer le type d'animation
                    string animType = sp.type == "biped" ? "biped" : (sp.type == "spider" ? "spider" : "quadruped");

                    // Charger les animations
                    if (root.animations != null)
                    {
                        foreach (var kvp in root.animations)
                        {
                            string animKey = kvp.Key;
                            List<string> animDataList = kvp.Value;

                            if (animKey.EndsWith($"_{animType}"))
                            {
                                int lastUnderscore = animKey.LastIndexOf('_');
                                string animName = animKey.Substring(0, lastUnderscore);

                                foreach (var part in skeleton)
                                {
                                    var animData = animDataList.FirstOrDefault(a => a.StartsWith(part.Name + "#"));
                                    if (!string.IsNullOrEmpty(animData))
                                        part.AddAnimation(animName, animData);
                                }
                            }
                        }
                    }
                    
                    Skeletons[speciesKey] = skeleton;

                    //  Chargement des textures de "caractères aléatoires" (paws, face, back, beak, etc.)
                    LoadFeatureTextures(speciesKey, folderName, sp.randomFeatures);

                    // Calcul des parties "jambes" et de la hauteur pour cette espèce
                    var legParts = new HashSet<string>();
                    float minY = 0f;
                    foreach (var part in skeleton)
                    {
                        if (part.Name.Contains("leg", StringComparison.OrdinalIgnoreCase))
                        {
                            legParts.Add(part.Name);
                            if (part.BasePos.Y < minY) minY = part.BasePos.Y;
                        }
                    }
                    LegPartNames[speciesKey] = legParts;
                    LegHeights[speciesKey] = -minY;
                    
                    // Forcer la hauteur des jambes pour l'humain si elle est nulle
                    if (speciesKey == "human" && LegHeights["human"] <= 0.1f)
                    {
                        LegHeights["human"] = 24f;
                        if (LegPartNames["human"].Count == 0)
                            LegPartNames["human"] = new HashSet<string> { "llegtop", "llegbottom", "rlegtop", "rlegbottom" };
                    }
                    
                    // Affichage compact
                    int loadedCount = skeleton.Count(p => p.Texture.Id != 0);
                    string status = loadedCount == skeleton.Count ? "" : "";
                    Console.ForegroundColor = loadedCount == skeleton.Count ? ConsoleColor.Green : ConsoleColor.Yellow;
                    
                    // Afficher le comportement
                    string behaviorIcon = sp.behavior switch
                    {
                        "passive" => "",
                        "neutral" => "",
                        "hostile" => "",
                        _ => ""
                    };
                    
                    if (loadedCount < skeleton.Count)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        var missing = skeleton.Where(p => p.Texture.Id == 0).Select(p => p.Name);
                    }
                }
                
                // Initialisation par défaut pour les espèces sans squelette
                foreach (var species in Species.Keys)
                {
                    if (!LegPartNames.ContainsKey(species))
                        LegPartNames[species] = new HashSet<string>();
                    if (!LegHeights.ContainsKey(species))
                        LegHeights[species] = 0f;
                }

                // Le porte-armure ("armorstand") n'est pas une créature définie dans species.json :
                // c'est un meuble qui réutilise le rendu humanoïde pour pouvoir afficher les objets
                // équipés (armure, arme...) dans son aperçu. Sans cette entrée, EntityRenderer ne
                // trouve aucun squelette pour "armorstand" et abandonne le rendu détaillé avant même
                // d'atteindre le code qui dessine l'équipement - les objets restent invisibles sur
                // le mannequin même une fois placés dans les cases.
                if (!Skeletons.ContainsKey("armorstand") && Skeletons.TryGetValue("human", out var humanSkeleton))
                {
                    Skeletons["armorstand"] = humanSkeleton;
                    Species["armorstand"] = Species["human"];
                    LegHeights["armorstand"] = LegHeights.GetValueOrDefault("human", 24f);
                    LegPartNames["armorstand"] = LegPartNames.TryGetValue("human", out var humanLegParts)
                        ? humanLegParts
                        : new HashSet<string> { "llegtop", "llegbottom", "rlegtop", "rlegbottom" };
                }

                    CharacterAnimationEditorUI.LoadSavedAnimations();
            }
            
            Console.WriteLine();
            Console.WriteLine($"┌─ Total: {Species.Count} espèces chargées ─────────────────────────────────────────┐");
            Console.WriteLine();
            
            if (Species.Count == 0) CreateDefaultSpecies();
        }
        
        //  CARACTÈRES ALÉATOIRES : scan des variantes disponibles sur disque pour une espèce
        // Convention : assets/animals/{Espece}/{feature}/{Espece}{feature}_{partie}N.png
        // Fallback (si un seul "attachTo") : assets/animals/{Espece}/{feature}/{Espece}{feature}N.png
        private static void LoadFeatureTextures(string speciesKey, string folderName, List<RandomFeatureJson>? features)
        {
            if (features == null || features.Count == 0) return;

            if (!FeatureTextures.TryGetValue(speciesKey, out var byFeature))
            {
                byFeature = new Dictionary<string, Dictionary<string, List<Texture2D>>>(StringComparer.OrdinalIgnoreCase);
                FeatureTextures[speciesKey] = byFeature;
            }

            foreach (var feature in features)
            {
                if (string.IsNullOrWhiteSpace(feature.name) || feature.attachTo == null || feature.attachTo.Count == 0)
                    continue;

                string normalizedSpeciesKey = speciesKey.ToLowerInvariant();
                string normalizedFeatureName = feature.name.ToLowerInvariant();
                _sharedFeatureTextureParts.RemoveWhere(key =>
                    key.speciesKey == normalizedSpeciesKey && key.featureName == normalizedFeatureName);

                string featureDir = $"assets/animals/{folderName}/{feature.name}/";
                var perPart = new Dictionary<string, List<Texture2D>>(StringComparer.OrdinalIgnoreCase);

                foreach (var partName in feature.attachTo)
                {
                    var variants = new List<Texture2D>();
                    if (Directory.Exists(featureDir))
                    {
                        var availableFiles = Directory.GetFiles(featureDir, "*.png");
                        string partPrefix = $"{folderName}{feature.name}_{partName}";
                        string variantPrefix = partPrefix;
                        var files = availableFiles
                            .Where(f => Path.GetFileNameWithoutExtension(f).StartsWith(partPrefix, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        // Fallback : un seul fichier partagé par variante, sans suffixe de partie
                        // (pratique quand la feature n'a qu'une seule partie attachée, ex: "face" sur "head")
                        if (files.Count == 0 && feature.attachTo.Count == 1)
                        {
                            string featurePrefix = $"{folderName}{feature.name}";
                            variantPrefix = featurePrefix;
                            files = availableFiles
                                .Where(f => Path.GetFileNameWithoutExtension(f).StartsWith(featurePrefix, StringComparison.OrdinalIgnoreCase))
                                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                .ToList();
                        }

                        foreach (var file in files)
                        {
                            var tex = Raylib.LoadTexture(file);
                            if (tex.Id != 0)
                            {
                                Raylib.SetTextureFilter(tex, TextureFilter.Point);
                                string fileName = Path.GetFileNameWithoutExtension(file);
                                string suffix = fileName.StartsWith(variantPrefix, StringComparison.OrdinalIgnoreCase)
                                    ? fileName[variantPrefix.Length..]
                                    : string.Empty;
                                if (int.TryParse(suffix, out int numberedVariant) && numberedVariant > 0)
                                {
                                    while (variants.Count < numberedVariant)
                                        variants.Add(default);
                                    variants[numberedVariant - 1] = tex;
                                }
                                else
                                {
                                    variants.Add(tex);
                                    if (string.IsNullOrEmpty(suffix))
                                    {
                                        _sharedFeatureTextureParts.Add((
                                            normalizedSpeciesKey,
                                            normalizedFeatureName,
                                            partName.ToLowerInvariant()));
                                    }
                                }
                            }
                        }
                    }
                    perPart[partName] = variants;
                }

                byFeature[feature.name] = perPart;
            }
        }

        // Nombre de variantes disponibles pour une feature donnée (max toutes parties confondues)
        public static int GetFeatureVariantCount(string speciesKey, string featureName)
        {
            speciesKey = speciesKey.ToLower();
            if (FeatureTextures.TryGetValue(speciesKey, out var byFeature) &&
                byFeature.TryGetValue(featureName, out var byPart))
            {
                return byPart.Values.Select(l => l.Count).DefaultIfEmpty(0).Max();
            }
            return 0;
        }

        // Texture d'une variante précise pour une feature, sur une partie donnée
        public static Texture2D GetFeatureTexture(string speciesKey, string featureName, string partName, int variantIndex)
        {
            if (variantIndex < 0) return default;
            speciesKey = speciesKey.ToLower();
            if (FeatureTextures.TryGetValue(speciesKey, out var byFeature) &&
                byFeature.TryGetValue(featureName, out var byPart) &&
                byPart.TryGetValue(partName, out var list) &&
                variantIndex < list.Count)
            {
                return list[variantIndex];
            }
            if (_sharedFeatureTextureParts.Contains((
                    speciesKey,
                    featureName.ToLowerInvariant(),
                    partName.ToLowerInvariant())) &&
                FeatureTextures.TryGetValue(speciesKey, out byFeature) &&
                byFeature.TryGetValue(featureName, out byPart) &&
                byPart.TryGetValue(partName, out var singleVariant) &&
                singleVariant.Count == 1)
            {
                return singleVariant[0];
            }
            return default;
        }

        //  PRÉSETS D'APPARENCE : conversion JSON -> runtime
        private static Color? ToColor(int[]? arr)
        {
            if (arr == null || arr.Length < 3) return null;
            return new Color(arr[0], arr[1], arr[2], 255);
        }

        private static ColorPreset ConvertPreset(ColorPresetJson pj)
        {
            var preset = new ColorPreset
            {
                Name = pj.name,
                Weight = pj.weight > 0 ? pj.weight : 1f,
                ColorMin = ToColor(pj.colorMin),
                ColorMax = ToColor(pj.colorMax),
                EyeColorMin = ToColor(pj.eyeColorMin),
                EyeColorMax = ToColor(pj.eyeColorMax)
            };

            if (pj.features != null)
            {
                foreach (var kvp in pj.features)
                {
                    var fj = kvp.Value;
                    preset.Features[kvp.Key] = new PresetFeature
                    {
                        Enabled = fj.enabled,
                        Variant = fj.variant,
                        ForceVariant = fj.variant >= 0,
                        ColorMin = ToColor(fj.colorMin),
                        ColorMax = ToColor(fj.colorMax)
                    };
                }
            }

            if (pj.biomes != null)
            {
                preset.Biomes = pj.biomes
                    .Where(b => !string.IsNullOrWhiteSpace(b))
                    .Select(b => b.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return preset;
        }

        private static int ResolvePreferredFoodId(JsonElement preferredFoodIdElement, string speciesName)
        {
            if (preferredFoodIdElement.ValueKind == JsonValueKind.Number)
            {
                if (preferredFoodIdElement.TryGetInt32(out var id))
                    return id;
            }
            else if (preferredFoodIdElement.ValueKind == JsonValueKind.String)
            {
                var rawValue = preferredFoodIdElement.GetString()?.Trim() ?? string.Empty;
                if (int.TryParse(rawValue, out var id))
                    return id;

                if (GameData.TryGetItemByKey(rawValue, out var itemByKey))
                    return itemByKey.ID;

                if (GameData.TryGetItemByName(rawValue, out var itemByName))
                    return itemByName.ID;

                var localizedMatch = GameData.ItemDatabase.Values
                    .FirstOrDefault(i => string.Equals(i.Name, rawValue, StringComparison.OrdinalIgnoreCase));
                if (localizedMatch.ID != 0)
                    return localizedMatch.ID;

                Console.WriteLine($"⚠️ Impossible de résoudre preferredFoodId '{rawValue}' pour l'espèce {speciesName}");
            }
            else if (preferredFoodIdElement.ValueKind != JsonValueKind.Undefined && preferredFoodIdElement.ValueKind != JsonValueKind.Null)
            {
                Console.WriteLine($"⚠️ preferredFoodId inattendu pour l'espèce {speciesName}: {preferredFoodIdElement.ValueKind}");
            }

            return 0;
        }

        // Tire un préset d'apparence pour une espèce donnée, en respectant PresetChance et les poids.
        // Retourne null si l'espèce n'a pas de présets, ou si le tirage "PresetChance" échoue
        // (dans ce cas l'entité doit utiliser l'apparence 100% aléatoire habituelle).
        public static ColorPreset? PickColorPreset(string species, Biome? biome = null)
        {
            var info = GetSpeciesInfo(species);
            if (info == null || info.ColorPresets.Count == 0) return null;
            if (Random.Shared.NextDouble() >= info.PresetChance) return null;

            var candidates = info.ColorPresets;
            if (biome.HasValue)
            {
                string environmentName = World.IsUnderground ? "cave" : biome.Value.ToString().ToLowerInvariant();
                var filtered = info.ColorPresets
                    .Where(p => p.Biomes.Count == 0 || p.Biomes.Contains(environmentName))
                    .ToList();
                if (filtered.Count > 0)
                    candidates = filtered;
            }

            float totalWeight = candidates.Sum(p => Math.Max(0f, p.Weight));
            if (totalWeight <= 0f) return candidates[Random.Shared.Next(candidates.Count)];

            float roll = (float)(Random.Shared.NextDouble() * totalWeight);
            float cumulative = 0f;
            foreach (var preset in candidates)
            {
                cumulative += Math.Max(0f, preset.Weight);
                if (roll <= cumulative) return preset;
            }
            return candidates[^1];
        }

        public static bool IsPortable(string species)
        {
            return GetSpeciesInfo(species)?.Portable ?? false;
        }

        public static bool IsHumanoid(string species)
        {
            return string.Equals(species, "armorstand", StringComparison.OrdinalIgnoreCase)
                || GetSpeciesInfo(species)?.IsHumanoid == true;
        }

        public static float GetAttackCooldown(string species)
        {
            return Species.TryGetValue(species.ToLower(), out var info) ? info.AttackCooldown : 1.0f;
        }
        
        private static void CreateDefaultSpecies()
        {
            // Nettoyer les dictionnaires existants
            Species.Clear();
            Skeletons.Clear();
            LegHeights.Clear();
            LegPartNames.Clear();
            
            var humanInfo = new SpeciesInfo 
            { 
                Name = "Human", Type = "biped", IsHumanoid = true, Scale = 1.8f, Speed = 65, RunSpeed = 100, MaxHp = 20, Attack = 3,
                Behavior = "passive", VisionRange = 0, AlertDuration = 0,
                Drops = new Dictionary<string, LootDrop>()
            };
            Species["human"] = humanInfo;
            
            var pigInfo = new SpeciesInfo 
            { 
                Name = "Pig", Type = "quadruped", Scale = 1.2f, Speed = 45, RunSpeed = 80, MaxHp = 12, Attack = 2,
                Behavior = "passive", VisionRange = 2, AlertDuration = 1.2f,
                Drops = new Dictionary<string, LootDrop>
                {
                    ["32"] = new LootDrop { Chance = 1.0, MinQty = 2, MaxQty = 2 }
                }
            };
            Species["pig"] = pigInfo;
            
            var chickenInfo = new SpeciesInfo 
            { 
                Name = "Chicken", Type = "bird", Scale = 1.0f, Speed = 55, RunSpeed = 100, MaxHp = 8, Attack = 1,
                Behavior = "passive", VisionRange = 2, AlertDuration = 1.0f,
                Drops = new Dictionary<string, LootDrop>
                {
                    ["35"] = new LootDrop { Chance = 1.0, MinQty = 1, MaxQty = 1 },
                    ["37"] = new LootDrop { Chance = 1.0, MinQty = 2, MaxQty = 2 },
                    ["32"] = new LootDrop { Chance = 1.0, MinQty = 1, MaxQty = 1 }
                }
            };
            Species["chicken"] = chickenInfo;
            
            var wolfInfo = new SpeciesInfo 
            { 
                Name = "Wolf", Type = "quadruped", Scale = 0.85f, Speed = 60, RunSpeed = 130, MaxHp = 18, Attack = 5,
                Behavior = "hostile", VisionRange = 4, AlertDuration = 0,
                Drops = new Dictionary<string, LootDrop>
                {
                    ["28"] = new LootDrop { Chance = 1.0, MinQty = 1, MaxQty = 1 },
                    ["32"] = new LootDrop { Chance = 1.0, MinQty = 1, MaxQty = 1 }
                },
                AttackCooldown = 1.0f
            };
            Species["wolf"] = wolfInfo;
            
            var bearInfo = new SpeciesInfo 
            { 
                Name = "Bear", Type = "quadruped", Scale = 1.2f, Speed = 40, RunSpeed = 110, MaxHp = 40, Attack = 8,
                Behavior = "neutral", VisionRange = 3, AlertDuration = 1.5f,
                Drops = new Dictionary<string, LootDrop>
                {
                    // Exemple de métadonnées ajoutées directement au moment du drop :
                    // la fourrure d'ours a une petite chance d'être marquée "quality":"rare".
                    ["28"] = new LootDrop { Chance = 1.0, MinQty = 3, MaxQty = 3 },
                    ["32"] = new LootDrop { Chance = 1.0, MinQty = 3, MaxQty = 3, Metadata = new() { ["quality"] = "rare" } }
                },
                AttackCooldown = 1.2f
            };
            Species["bear"] = bearInfo;
            
            var spiderInfo = new SpeciesInfo 
            { 
                Name = "Spider", Type = "spider", Scale = 0.7f, Speed = 100, RunSpeed = 150, MaxHp = 12, Attack = 3,
                Behavior = "hostile", VisionRange = 3, AlertDuration = 0,
                Drops = new Dictionary<string, LootDrop>
                {
                    ["28"] = new LootDrop { Chance = 1.0, MinQty = 1, MaxQty = 1 }
                },
                AttackCooldown = 1.2f
            };
            Species["spider"] = spiderInfo;
            
            // Initialiser les squelettes vides et les hauteurs de jambes
            foreach (var species in Species.Keys)
            {
                Skeletons[species] = new List<AnimalBodyPart>();
                LegPartNames[species] = new HashSet<string>();
                LegHeights[species] = 0f;
            }
            
            // Définir une hauteur de jambe par défaut pour l'humain
            LegHeights["human"] = 24f;
            LegPartNames["human"] = new HashSet<string> { "llegtop", "llegbottom", "rlegtop", "rlegbottom" };

            // Même alias que dans LoadFromFile() : le porte-armure réutilise le squelette humain.
            Species["armorstand"] = Species["human"];
            Skeletons["armorstand"] = Skeletons["human"];
            LegHeights["armorstand"] = LegHeights["human"];
            LegPartNames["armorstand"] = LegPartNames["human"];
        }
        
        public static SpeciesInfo? GetSpeciesInfo(string species)
        {
            if (string.IsNullOrWhiteSpace(species))
                return null;

            string normalized = species.Trim().ToLowerInvariant();
            if (Species.TryGetValue(normalized, out var info))
                return info;

            // Fallback robustesse : certains chemins peuvent fournir des clés avec des variantes
            // de casse, des espaces superflus ou des noms légèrement différents au runtime.
            foreach (var kv in Species)
            {
                if (string.Equals(kv.Value.Name, species, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kv.Key, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }

            return null;
        }
        
        public static int GetMaxHp(string species)
        {
            return Species.TryGetValue(species.ToLower(), out var info) ? info.MaxHp : 10;
        }
        
        public static float GetSpeed(string species)
        {
            return Species.TryGetValue(species.ToLower(), out var info) ? info.Speed : 45f;
        }
        
        public static string GetBehavior(string species)
        {
            return Species.TryGetValue(species.ToLower(), out var info) ? info.Behavior : "passive";
        }
        
        public static int GetVisionRange(string species)
        {
            return Species.TryGetValue(species.ToLower(), out var info) ? info.VisionRange : 0;
        }
        
        public static float GetAlertDuration(string species)
        {
            return Species.TryGetValue(species.ToLower(), out var info) ? info.AlertDuration : 0f;
        }
        
        // Tire les drops d'une espèce : pour chaque entrée du dictionnaire de loot, on teste
        // sa Chance, on tire une quantité entre MinQty et MaxQty, puis on renvoie ses éventuelles
        // Metadata telles quelles pour qu'elles soient appliquées à l'objet créé au moment du drop.
        public static List<(int id, int qty, Dictionary<string, string>? meta)> GetDrops(string species)
        {
            var result = new List<(int id, int qty, Dictionary<string, string>? meta)>();
            var info = GetSpeciesInfo(species);
            if (info?.Drops == null) return result;

            foreach (var kv in info.Drops)
            {
                if (!int.TryParse(kv.Key, out int itemId)) continue;
                var loot = kv.Value;
                if (loot == null) continue;

                if (Random.Shared.NextDouble() > loot.Chance) continue;

                int qty = Random.Shared.Next(loot.MinQty, loot.MaxQty + 1);
                if (qty <= 0) continue;

                result.Add((itemId, qty, loot.Metadata));
            }
            return result;
        }
        
        // Classes pour la désérialisation
        public class SpeciesRoot
        {
            public List<SpeciesJson> species { get; set; } = new();
            public Dictionary<string, List<string>> animations { get; set; } = new();
        }
        
        public class SpeciesInfo
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public bool IsHumanoid { get; set; }
            public float Scale { get; set; } = 1f;
            public float Speed { get; set; } = 45f;
            public float RunSpeed { get; set; } = 0f;      // vitesse de course (pour fuite ou poursuite)
            public int MaxHp { get; set; } = 10;
            public int Attack { get; set; } = 1;
            public string Behavior { get; set; } = "passive";
			public bool Mountable { get; set; } = false;
            public int VisionRange { get; set; } = 0;
            public float AlertDuration { get; set; } = 0f;
            //  Table de loot : clé = ID d'item (string), valeur = règle de drop (chance, qty, métadonnées).
            public Dictionary<string, LootDrop> Drops { get; set; } = new();
            public int PreferredFoodId { get; set; } = 0;
            public float AttackCooldown { get; set; } = 1.0f;
            public float GrowthTime { get; set; } = 600f;
            public bool CanBeCaught { get; set; } = false;
            public bool IsLightSource { get; set; } = false;
            public int LightRadius { get; set; } = 120;
            public Color LightColor { get; set; } = new Color(255, 200, 100, 255);
			public string AttackType { get; set; } = "melee";      // "melee" ou "charge"
			public float ChargeDuration { get; set; } = 0.8f;      // Durée de la charge (secondes)
			public float ChargeSpeed { get; set; } = 400f;         // Vitesse de la charge (pixels/s)
			public float ChargeCooldown { get; set; } = 2.0f;      // Temps de recharge avant une nouvelle charge
			public int ChargeDamage { get; set; } = 5;             // Dégâts infligés en cas de collision

            //  CARACTÈRES ALÉATOIRES (paws, face, back, beak, etc.)
            public List<RandomFeatureJson> RandomFeatures { get; set; } = new();

            //  Couleur de peau/fourrure aléatoire par entité
            public Color ColorMin { get; set; } = Color.White;
            public Color ColorMax { get; set; } = Color.White;
            public bool Tintable { get; set; } = false;

            //  Couleur d'yeux aléatoire par défaut pour l'espèce (null = pas de teinte, yeux tels quels)
            public Color? EyeColorMin { get; set; }
            public Color? EyeColorMax { get; set; }

            //  Si vrai, cette créature peut être portée (comme un meuble) via la touche F, puis reposée
            public bool Portable { get; set; } = false;

            //  PRÉSETS D'APPARENCE : combinaisons figées (couleur + features) parmi lesquelles piocher
            // PresetChance = probabilité qu'une entité qui spawn utilise UN des présets ci-dessous
            // plutôt qu'une apparence 100% aléatoire (1.0 = toujours un préset).
            public float PresetChance { get; set; } = 0f;
            public List<ColorPreset> ColorPresets { get; set; } = new();
        }

        // Préset d'apparence figé (ex: "Tabby", "Chat noir", "Chat blanc"...)
        public class ColorPreset
        {
            public string Name = "";
            // Poids relatif de tirage parmi les présets de l'espèce (pas besoin que la somme fasse 1)
            public float Weight = 1f;
            // Liste des environnements/bioomes où ce préset peut apparaître.
            // Si vide, le préset est disponible partout.
            public List<string> Biomes = new List<string>();
            // Plage de couleur du corps pour ce préset (null = utilise la plage globale de l'espèce)
            public Color? ColorMin;
            public Color? ColorMax;
            // Plage de couleur des yeux pour ce préset (null = utilise la plage globale de l'espèce)
            public Color? EyeColorMin;
            public Color? EyeColorMax;
            // Réglages par feature (paws, face, stripes, ...), indexés par nom de feature
            public Dictionary<string, PresetFeature> Features = new(StringComparer.OrdinalIgnoreCase);
        }

        // Réglage d'une feature précise à l'intérieur d'un préset
        public class PresetFeature
        {
            // true = force la présence de la feature, false = force son absence,
            // null = laisse la "chance" normale de species.json décider
            public bool? Enabled;
            // Index de variante forcé (-1/absent = variante aléatoire parmi celles disponibles)
            public int Variant = -1;
            public bool ForceVariant = false;
            // Plage de couleur spécifique à ce préset pour cette feature (si colorable),
            // sinon on retombe sur la plage par défaut (40-240)
            public Color? ColorMin;
            public Color? ColorMax;
        }

        // Définition d'une "feature" aléatoire (cf. species.json -> randomFeatures)
        public class RandomFeatureJson
        {
            public string name { get; set; } = "";
            // Parties du squelette concernées par cette feature (ex: ["head"], ou plusieurs pour un élément symétrique)
            public List<string> attachTo { get; set; } = new();
            // "overlay" : se superpose par-dessus la texture de la partie
            // "replace" : remplace entièrement la texture de la partie (ex: "face" change la tête)
            public string mode { get; set; } = "overlay";
            // Si vrai, une teinte aléatoire est générée pour cette feature (comme les cheveux)
            public bool colorable { get; set; } = false;
            // Probabilité (0-1) qu'une entité donnée possède cette feature
            public float chance { get; set; } = 1.0f;
            public int? fixedVariant { get; set; } = null;
        }

        public class SpeciesJson
        {
            public string name { get; set; } = "";
            public string type { get; set; } = "";
            public bool isHumanoid { get; set; }
            public float scale { get; set; } = 1f;
            public float speed { get; set; } = 45f;
            public float runSpeed { get; set; } = 0f;      // nouvelle clé unique pour la course
            public int maxHp { get; set; } = 10;
            public int attack { get; set; } = 1;
            public string behavior { get; set; } = "passive";
			public bool mountable { get; set; } = false;
            public int visionRange { get; set; } = 0;
            public float alertDuration { get; set; } = 0f;
            //  Format JSON : { "238": { "chance": 1.0, "min": 1, "max": 2, "metadata": {"quality":"rare"} } }
            public Dictionary<string, LootDrop>? drops { get; set; } = new();
            public List<PartJson> skeleton { get; set; } = new();
            public JsonElement preferredFoodId { get; set; }
            public float attackCooldown { get; set; } = 1.0f;
            public float growthTime { get; set; } = 600f;
            public bool canBeCaught { get; set; } = false;
            public bool isLightSource { get; set; } = false;
            public int lightRadius { get; set; } = 120;
            public int[] lightColor { get; set; } = new int[] { 255, 200, 100, 255 };
            public List<RandomFeatureJson> randomFeatures { get; set; } = new();
            //  Couleur de peau/fourrure aléatoire (plage min/max), appliquée seulement si tintable = true
            public int[]? colorMin { get; set; }
            public int[]? colorMax { get; set; }
            public bool tintable { get; set; } = false;
            //  Couleur d'yeux aléatoire par défaut (plage min/max) pour l'espèce
            public int[]? eyeColorMin { get; set; }
            public int[]? eyeColorMax { get; set; }
            //  Si vrai, cette créature peut être portée (comme un meuble)
            public bool portable { get; set; } = false;
            //  Présets d'apparence figés + probabilité de piocher dedans plutôt qu'être 100% aléatoire
            public float presetChance { get; set; } = 0f;
            public List<ColorPresetJson> colorPresets { get; set; } = new();
        }

        // Présets d'apparence (cf. species.json -> colorPresets)
        public class ColorPresetJson
        {
            public string name { get; set; } = "";
            public float weight { get; set; } = 1f;
            public List<string> biomes { get; set; } = new();
            public int[]? colorMin { get; set; }
            public int[]? colorMax { get; set; }
            //  Couleur d'yeux spécifique à ce préset (sinon retombe sur eyeColorMin/eyeColorMax de l'espèce)
            public int[]? eyeColorMin { get; set; }
            public int[]? eyeColorMax { get; set; }
            public Dictionary<string, PresetFeatureJson> features { get; set; } = new();
        }

        public class PresetFeatureJson
        {
            public bool? enabled { get; set; }
            public int variant { get; set; } = -1;
            public int[]? colorMin { get; set; }
            public int[]? colorMax { get; set; }
        }
        
        public class PartJson
        {
            public string name { get; set; } = "";
            public float baseX { get; set; } = 0;
            public float baseY { get; set; } = 0;
            public float baseRot { get; set; } = 0;
            public string parent { get; set; } = "";
        }
    }
}