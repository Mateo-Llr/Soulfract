#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public partial class Entity
    {
        // Constructor, equipment slots and public methods
        // ==================== CONSTRUCTEUR ====================
        //  `skipHomeAutoAssign` : à utiliser quand l'appelant s'apprête à réassigner lui-même
        // HomeBuildingId/HomePosition juste après la construction (cas du chargement d'une
        // sauvegarde, voir SaveSystem.RestoreEntityFromSave). Sans ce paramètre, CHAQUE PNJ humain
        // rechargé déclenchait ici une recherche + RÉSERVATION d'une case de lit dans la maison la
        // plus proche de sa position (HomePosition valant Vector2.Zero par défaut à la construction,
        // avant que le vrai HomeBuildingId/HomePosition sauvegardés ne soient réappliqués par
        // l'appelant) : s'il faisait nuit, le PNJ partait même immédiatement en GoHome vers cette
        // maison temporaire. Le vrai HomeBuildingId/HomePosition écrasait bien ces valeurs juste
        // après, MAIS ni AiState/AiTarget (qui gardaient l'ordre de marche vers la mauvaise maison)
        // ni la case fraîchement réservée (jamais libérée, puisque plus personne n'y fait
        // référence) n'étaient nettoyés. Au chargement d'une sauvegarde faite de nuit, ça se
        // traduisait par : tous les villageois humains partent d'abord vers la maison la plus
        // proche du point où ils ont été recréés (souvent la même, si plusieurs PNJ sont proches
        // les uns des autres), puis, une fois Decide()/le système de priorité repris la main avec
        // le VRAI HomeBuildingId, repartent vers leur vraie maison — en plus de "geler" une case de
        // lit à chaque rechargement, jamais relâchée.
        public Entity(Vector2 pos, string species, bool isPlayer = false, bool skipHomeAutoAssign = false)
        {
            WorldPos = pos;
            Species = species;
            AiTarget = pos;
            AiTimer = 0f;
            IsPlayer = isPlayer;

            var speciesInfo = SpeciesData.GetSpeciesInfo(species);
            if (speciesInfo != null)
            {
                PreferredFoodId = speciesInfo.PreferredFoodId;
                MaxHP = speciesInfo.MaxHp;
                Speed = speciesInfo.Speed;
                BaseSpeed = speciesInfo.Speed;
                RunSpeed = speciesInfo.RunSpeed > 0 ? speciesInfo.RunSpeed : BaseSpeed * 1.5f;
                Attack = speciesInfo.Attack;
                Behavior = speciesInfo.Behavior;
                VisionRange = speciesInfo.VisionRange;
                AlertDuration = speciesInfo.AlertDuration;
                AttackHitRange = 25f;
                if (string.Equals(species, "Goblin", StringComparison.OrdinalIgnoreCase))
                {
                    AttackHitRange = GOBLIN_ATTACK_REACH;
                }
                GrowthTime = speciesInfo.GrowthTime;
            }
            else
            {
                PreferredFoodId = 0;
                MaxHP = 10;
                Speed = 45f;
                BaseSpeed = 45f;
                RunSpeed = 70f;
                Attack = 1;
                Behavior = "passive";
                VisionRange = 0;
                AlertDuration = 0f;
                AttackHitRange = 25;
                GrowthTime = 600f;
            }

            _legHeight = SpeciesData.LegHeights.GetValueOrDefault(species, 0f);

            SpeciesData.ColorPreset? colorPreset = null;
            string renderSpecies = RenderSpecies;

            if (string.Equals(renderSpecies, "human", StringComparison.OrdinalIgnoreCase))
            {
                Tint = Program.SkinColorPalette[Random.Shared.Next(Program.SkinColorPalette.Length)];
                HairColor = new Color(Random.Shared.Next(30, 200), Random.Shared.Next(20, 160), Random.Shared.Next(0, 80), 255);
                HairStyle = Random.Shared.Next(0, Math.Max(1, Program.hairBaseTextures.Count));
                BeardStyle = Random.Shared.Next(0, Math.Max(1, Program.beardBaseTextures.Count));
                EnsureHumanEyeColor();
            }
            else
            {
                //  Tire un préset d'apparence (couleur + features figées) selon PresetChance, sinon reste null
                // et l'apparence sera 100% aléatoire comme avant.
                Biome? spawnBiome = null;
                if (Program.TileSize > 0)
                {
                    float tileX = WorldPos.X / Program.TileSize;
                    float tileY = WorldPos.Y / Program.TileSize;
                    spawnBiome = World.GetBiomeAt(tileX, tileY);
                }
                colorPreset = SpeciesData.PickColorPreset(renderSpecies, spawnBiome);
                Tint = GetRandomSkinColor(renderSpecies, colorPreset);
                HairStyle = 0;
                BeardStyle = 0;
                HairColor = Color.Black;
            }

            EnsureGenieTint();
            AssignRandomFeatures(colorPreset, RandomFeatureVariant, RandomFeatureColor, renderSpecies);

            _idleSoundSlug = BuildIdleSoundSlug(colorPreset);
            InitializeIdleSound();

            CurrentHP = MaxHP;

            if (species.ToLower() == "ogre" && !isPlayer)
            {
                IsBoss = true;
                MaxHP = 120;
                CurrentHP = MaxHP;
                Attack = 12;
            }
            else if (species.ToLower() == "worm" && !isPlayer)
            {
                IsBoss = true;
                MaxHP = 400;
                CurrentHP = MaxHP;
                Attack = 18;
                _wmState = WormBossState.IntroEmerging;
            }
            else if (species.ToLower() == "gulper" && !isPlayer)
            {
                // Stats (MaxHP, Attack, etc.) déjà définies dans species.json : on active juste
                // le comportement de boss et on initialise sa machine à états.
                IsBoss = true;
                _guState = GulperBossState.Rising;
                _guGroundPos = WorldPos;
                _guHeight = 0f;
            }
            else if (species.ToLower() == "genie" && !isPlayer)
            {
                MaxHP = 850;
                CurrentHP = MaxHP;
                Attack = 14;
                Behavior = "hostile";
                VisionRange = Math.Max(VisionRange, 3);
                AlertDuration = 1.5f;
                AttackHitRange = 25f;
                Speed = 150f;
                BaseSpeed = 150f;
                RunSpeed = 320f;
                Scale = 1.15f;
                IsBoss = true;
                _geState = GenieBossState.Idle;
                _geAttackCooldown = 1.5f; // petit délai avant la première attaque, le temps de l'intro
            }
            else
            {
                IsBoss = false;
            }

            if (IsTamed)
            {
                Behavior = "tamed";
            }

            if (species.ToLower() == "human" && !isPlayer)
            {
                AssignProfessionAndTrade(allowGuard: !skipHomeAutoAssign);
                ApplyNpcEquipmentAndInventory();
                InitializeSocialProfile();
                if (Profession == ProfessionType.Guard)
                {
                    ConfigureVillageGuardStats();
                    CurrentHP = MaxHP;
                    EquipVillageGuard();
                }
                if (World.IsUnderground)
                    _tentRestCooldown = 45f + (float)(Random.Shared.NextDouble() * 120f);
                if (Profession == ProfessionType.Farmer)
                    GiveStarterSeedsToFarmer();
            }

            //  Les gobelins générés (voir World.LoadOrGenerateChunk, hordes rares) sont
            // toujours hostiles et reçoivent une arme au hasard (épée ou arc), qui détermine
            // ensuite leur style de combat dans ChaseBehavior (mêlée vs. tir à distance).
            if (species.ToLower() == "goblin" && !isPlayer)
            {
                Behavior = "hostile";
                if (VisionRange <= 0)
                    VisionRange = 7;
                EquipGoblinWeapon();
            }

            Decide();

            if (!skipHomeAutoAssign && species.ToLower() == "human" && !isPlayer && !World.IsUnderground
                && HomePosition == Vector2.Zero && !IsTamed)
            {
                var foundHome = World.FindNearestFreeHouseSpot(pos, 2400f);
                if (foundHome.HasValue)
                {
                    HomePosition = foundHome.Value.spotPos;
                    HomeBuildingId = foundHome.Value.buildingId;
                    float darkness = Program.GetDarknessAlpha();
                    if (darkness > 0.6f)
                    {
                        AiState = NpcAiState.GoHome;
                        AiTarget = GetHomeEntryTarget();
                        AnimState = "walk";
                        Speed = BaseSpeed;
                        // Pas de HashSet "destroyed" disponible ici (construction) : le trajet
                        // A* réel sera calculé au prochain tick d'Update, _currentPath étant
                        // encore vide (voir la branche GoHome dans Update).
                    }
                }
            }

        }

        public void EnsureHumanEyeColor()
        {
            if (!string.Equals(RenderSpecies, "human", StringComparison.OrdinalIgnoreCase) || RandomFeatureColor.ContainsKey("__eyeColor"))
                return;

            RandomFeatureColor["__eyeColor"] = new[]
            {
                new Color(45, 35, 25, 255),
                new Color(90, 60, 35, 255),
                new Color(70, 120, 155, 255),
                new Color(95, 145, 95, 255),
                new Color(125, 165, 185, 255)
            }[Random.Shared.Next(5)];
        }

        private string BuildIdleSoundSlug(SpeciesData.ColorPreset? colorPreset)
        {
            string baseName = !string.IsNullOrWhiteSpace(colorPreset?.Name)
                ? colorPreset.Name
                : Species;

            return NormalizeSoundSlug(baseName);
        }

        private string NormalizeSoundSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string normalized = value.Trim().ToLowerInvariant();
            normalized = normalized.Replace(" ", "_");
            normalized = normalized.Replace("-", "_");
            normalized = normalized.Replace("'", string.Empty);
            normalized = normalized.Replace("(", string.Empty).Replace(")", string.Empty);
            normalized = normalized.Replace("/", "_");

            return normalized;
        }

        private void InitializeIdleSound()
        {
            if (IsPlayer || Species == "human" || _idleSoundLoaded || string.IsNullOrWhiteSpace(_idleSoundSlug))
                return;

            foreach (string candidate in GetIdleSoundCandidates())
            {
                var soundPaths = FindIdleSoundFiles(candidate);
                if (soundPaths.Count == 0)
                    continue;

                foreach (string path in soundPaths)
                {
                    Sound sound = Raylib.LoadSound(path);
                    if (Raylib.IsSoundReady(sound))
                        _idleSounds.Add(sound);
                }

                if (_idleSounds.Count > 0)
                {
                    _idleSoundLoaded = true;
                    _idleSoundCooldown = 10f + (float)(Random.Shared.NextDouble() * 12f);
                    return;
                }
            }
        }

        private List<string> FindIdleSoundFiles(string candidate)
        {
            var result = new List<string>();
            string baseName = $"{candidate}_idle";
            string soundsDir = Path.Combine("assets", "sounds");

            if (!Directory.Exists(soundsDir))
                return result;

            foreach (string extension in new[] { ".mp3", ".wav" })
            {
                string directPath = Path.Combine(soundsDir, $"{baseName}{extension}");
                if (File.Exists(directPath) && !result.Any(path => string.Equals(Path.GetFileNameWithoutExtension(path), baseName, StringComparison.OrdinalIgnoreCase)))
                    result.Add(directPath);

                var files = Directory.GetFiles(soundsDir, $"{baseName}*{extension}", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (string path in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    if (string.Equals(fileName, baseName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (fileName.Length > baseName.Length &&
                        fileName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) &&
                        fileName.Substring(baseName.Length).All(char.IsDigit))
                    {
                        string stem = Path.GetFileNameWithoutExtension(path);
                        if (!result.Any(existing => string.Equals(Path.GetFileNameWithoutExtension(existing), stem, StringComparison.OrdinalIgnoreCase)))
                            result.Add(path);
                    }
                }
            }

            return result;
        }

        private List<string> GetIdleSoundCandidates()
        {
            var candidates = new List<string>();
            string presetSlug = NormalizeSoundSlug(_idleSoundSlug);
            string speciesSlug = NormalizeSoundSlug(Species);

            if (!string.IsNullOrWhiteSpace(presetSlug))
                candidates.Add(presetSlug);

            if (!string.IsNullOrWhiteSpace(speciesSlug) && !string.Equals(presetSlug, speciesSlug, StringComparison.OrdinalIgnoreCase))
                candidates.Add(speciesSlug);

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void TryPlayIdleSound(Vector2 playerPos)
        {
            if (IsPlayer || Species == "human" || !_idleSoundLoaded || _idleSounds.Count == 0)
                return;

            int index = Random.Shared.Next(_idleSounds.Count);
            Sound sound = _idleSounds[index];
            if (!Raylib.IsSoundReady(sound))
                return;

            float distance = Vector2.Distance(WorldPos, playerPos);
            float maxDistance = 900f;
            float volume = Math.Clamp(1f - (distance / maxDistance), 0.05f, 1f);

            Raylib.SetSoundVolume(sound, volume);
            Raylib.SetSoundPitch(sound, 0.95f + (float)(Random.Shared.NextDouble() * 0.1f));
            Raylib.PlaySound(sound);
            _idleSoundCooldown = 16f + (float)(Random.Shared.NextDouble() * 20f);
        }

        /// <summary>Cherche les fichiers assets/sounds/{candidate}{suffix}.mp3 et {candidate}{suffix}N.mp3
        /// (variantes numérotées) - version générique utilisée par les sons idle et hurt.</summary>
        private List<string> FindSuffixedSoundFiles(string candidate, string suffix)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(candidate)) return result;

            string baseName = $"{candidate}{suffix}";
            string soundsDir = Path.Combine("assets", "sounds");

            if (!Directory.Exists(soundsDir))
                return result;

            foreach (string extension in new[] { ".mp3", ".wav" })
            {
                string directPath = Path.Combine(soundsDir, $"{baseName}{extension}");
                if (File.Exists(directPath) && !result.Any(path => string.Equals(Path.GetFileNameWithoutExtension(path), baseName, StringComparison.OrdinalIgnoreCase)))
                    result.Add(directPath);

                var files = Directory.GetFiles(soundsDir, $"{baseName}*{extension}", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (string path in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    if (string.Equals(fileName, baseName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (fileName.Length > baseName.Length &&
                        fileName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) &&
                        fileName.Substring(baseName.Length).All(char.IsDigit))
                    {
                        string stem = Path.GetFileNameWithoutExtension(path);
                        if (!result.Any(existing => string.Equals(Path.GetFileNameWithoutExtension(existing), stem, StringComparison.OrdinalIgnoreCase)))
                            result.Add(path);
                    }
                }
            }

            return result;
        }

        /// <summary>Charge les sons de blessure ({couleur/preset}_hurtN.mp3 puis, à défaut,
        /// {espèce}_hurtN.mp3), une seule fois par entité, la première fois qu'elle est touchée.</summary>
        private void InitializeHurtSound()
        {
            if (IsPlayer || _hurtSoundLoaded)
                return;
            _hurtSoundLoaded = true; // on ne retente pas le scan du dossier à chaque coup, même si rien trouvé

            foreach (string candidate in GetIdleSoundCandidates())
            {
                var soundPaths = FindSuffixedSoundFiles(candidate, "_hurt");
                if (soundPaths.Count == 0)
                    continue;

                foreach (string path in soundPaths)
                {
                    Sound sound = Raylib.LoadSound(path);
                    if (Raylib.IsSoundReady(sound))
                        _hurtSounds.Add(sound);
                }

                if (_hurtSounds.Count > 0)
                    return;
            }
        }

        private void InitializeAttackSound()
        {
            if (IsPlayer || _attackSoundLoaded)
                return;
            _attackSoundLoaded = true;

            foreach (string candidate in GetIdleSoundCandidates())
            {
                var soundPaths = FindSuffixedSoundFiles(candidate, "_attack");
                if (soundPaths.Count == 0)
                    continue;

                foreach (string path in soundPaths)
                {
                    Sound sound = Raylib.LoadSound(path);
                    if (Raylib.IsSoundReady(sound))
                        _attackSounds.Add(sound);
                }

                if (_attackSounds.Count > 0)
                    return;
            }
        }

        private void PlayAttackSound()
        {
            InitializeAttackSound();
            if (_attackSounds.Count == 0)
                return;

            int index = Random.Shared.Next(_attackSounds.Count);
            Sound sound = _attackSounds[index];
            if (!Raylib.IsSoundReady(sound))
                return;

            float distance = Vector2.Distance(WorldPos, Program.GetPlayerPosition());
            float maxDistance = 900f;
            float volume = Math.Clamp(1f - (distance / maxDistance), 0.05f, 1f);

            Raylib.SetSoundVolume(sound, volume);
            Raylib.SetSoundPitch(sound, 0.95f + (float)(Random.Shared.NextDouble() * 0.1f));
            Raylib.PlaySound(sound);
        }

        /// <summary>Joue un son de blessure aléatoire ({espèce}_hurtN.mp3) lorsque l'entité est touchée.
        /// Le volume diminue avec la distance au joueur, comme pour le son idle.</summary>
        private void PlayHurtSound()
        {
            double currentTime = Raylib.GetTime();
            if (currentTime < _nextHurtSoundTime)
                return;

            InitializeHurtSound();
            if (_hurtSounds.Count == 0)
                return;

            int index = Random.Shared.Next(_hurtSounds.Count);
            Sound sound = _hurtSounds[index];
            if (!Raylib.IsSoundReady(sound))
                return;

            float distance = Vector2.Distance(WorldPos, Program.GetPlayerPosition());
            float maxDistance = 900f;
            float volume = Math.Clamp(1f - (distance / maxDistance), 0.05f, 1f);

            Raylib.SetSoundVolume(sound, volume);
            Raylib.SetSoundPitch(sound, 0.92f + (float)(Random.Shared.NextDouble() * 0.16f));
            Raylib.PlaySound(sound);
            _nextHurtSoundTime = currentTime + HURT_SOUND_COOLDOWN;
        }

        //  Donne à un gobelin une arme au hasard : épée en bois (item 16) pour un combattant
        // de mêlée, ou une arme à distance (arc ou mousquet) pour un tireur. C'est cette arme,
        // une fois en main, qui pilote le choix du profil de combat (voir GetCombatProfile : mêlée / distance).
        private void EquipGoblinWeapon()
        {
            Equipment ??= new Equipment();
            int[] rangedWeaponIds = { 66, 257 };
            int weaponId = Random.Shared.NextDouble() < 0.5 ? rangedWeaponIds[Random.Shared.Next(rangedWeaponIds.Length)] : 16;
            if (GameData.ItemDatabase.TryGetValue(weaponId, out var data))
            {
                var item = new Item(data.Name, 1, data.Color, data.Icon);
                Equipment.EquipItem(item, EquipmentSlot.MainHand);
                Equipment.LoadEquipmentTextures();
            }
        }

        public void ConfigureVillageGuardStats()
        {
            MaxHP = 100;
            Attack = 8;
            VisionRange = 18;
            AttackHitRange = 65f;
        }

        internal void EquipVillageGuard()
        {
            Equipment ??= new Equipment();
            Equipment.ClearAllEquipment();

            foreach (string itemKey in new[] { "crusader_helmet", "crusader_chestplate", "crusader_leggings" })
            {
                if (!GameData.ItemDatabaseByKey.TryGetValue(itemKey, out var armorData))
                    continue;

                var armorItem = new Item(armorData.Name, 1, armorData.Color, armorData.Icon);
                Equipment.EquipOnBody(armorItem, out _);
            }

            if (GameData.ItemDatabaseByKey.TryGetValue("wooden_sword", out var weaponData))
            {
                var weapon = new Item(weaponData.Name, 1, weaponData.Color, weaponData.Icon);
                Equipment.EquipItem(weapon, EquipmentSlot.MainHand);
            }

            Equipment.LoadEquipmentTextures();
        }

        //  Vrai si l'arme actuellement en main est une arme de tir.
        private bool HasRangedWeapon => Equipment?.MainHand != null && GameData.IsRangedWeapon(Equipment.MainHand.Name);

        private void ApplyNpcEquipmentAndInventory()
        {
            Equipment ??= new Equipment();
            Equipment.ClearAllEquipment();

            var eligibleItems = GameData.ItemDatabase.Values
                .Where(d => d.Type != ItemType.None && d.Type != ItemType.Resource && d.Type != ItemType.Food &&
                            d.Type != ItemType.Placeable && d.Type != ItemType.Throwable && d.Type != ItemType.WallCovering)
                .ToList();

            var hazmat = eligibleItems.FirstOrDefault(d => string.Equals(d.Key, "hazmat_suit", StringComparison.OrdinalIgnoreCase));
            if (hazmat.ID != 0 && Random.Shared.NextDouble() < 0.05)
            {
                var hazmatItem = new Item(hazmat.Name, 1, hazmat.Color, hazmat.Icon);
                ApplyRandomDyeColors(hazmatItem, hazmat);
                Equipment.EquipOnBody(hazmatItem, out _);
            }
            else
            {
                var coat = eligibleItems.FirstOrDefault(d => string.Equals(d.Key, "coat", StringComparison.OrdinalIgnoreCase));
                var jeans = eligibleItems.FirstOrDefault(d => string.Equals(d.Key, "jean", StringComparison.OrdinalIgnoreCase));

                if (coat.ID != 0)
                {
                    var coatItem = new Item(coat.Name, 1, coat.Color, coat.Icon);
                    coatItem.SetMetadataValue("sleeves", new[] { "short", "long", "none" }[Random.Shared.Next(3)]);
                    coatItem.SetMetadataValue("opening", new[] { "open", "closed", "lace_up" }[Random.Shared.Next(3)]);
                    ApplyRandomDyeColors(coatItem, coat);
                    Equipment.EquipOnBody(coatItem, out _);
                }

                if (jeans.ID != 0)
                {
                    var jeansItem = new Item(jeans.Name, 1, jeans.Color, jeans.Icon);
                    ApplyRandomDyeColors(jeansItem, jeans);
                    Equipment.EquipOnBody(jeansItem, out _);
                }

                var accessoryZones = new[]
                {
                    BodyZone.TopOfHead, BodyZone.Back, BodyZone.Face, BodyZone.Neck,
                    BodyZone.Waist, BodyZone.Ears, BodyZone.Feet
                };

                foreach (var zone in accessoryZones)
                {
                    if (Equipment.GetZoneItem(zone) != null || Random.Shared.NextDouble() >= 0.2) continue;

                    var chosen = eligibleItems
                        .Where(d => d.CoveredZones.Contains(zone)
                            && !string.Equals(d.Key, "hazmat_suit", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(_ => Random.Shared.Next())
                        .FirstOrDefault();
                    if (chosen.ID == 0) continue;

                    var item = new Item(chosen.Name, 1, chosen.Color, chosen.Icon);
                    ApplyRandomDyeColors(item, chosen);
                    Equipment.EquipOnBody(item, out _);
                }
            }

            EnsureNpcClothing();

            // ===== ÉQUIPEMENT DES EMPLACEMENTS FONCTIONNELS (main, sac à dos...) =====
            var functionalSlots = new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Backpack };
            foreach (var slot in functionalSlots)
            {
                var candidates = eligibleItems
                    .Where(d => d.HasEquipSlot && d.EquipSlot == slot)
                    .OrderBy(_ => Random.Shared.Next())
                    .ToList();

                if (candidates.Count == 0) continue;

                var chosen = candidates.First();
                var item = new Item(chosen.Name, 1, chosen.Color, chosen.Icon);
                ApplyRandomDyeColors(item, chosen);
                Equipment.EquipItem(item, slot);
            }

            // Charger les textures d'équipement
            Equipment.LoadEquipmentTextures();

            // L'inventaire commence vide. Les objets doivent provenir d'une action réelle
            // (ramassage, production, échange ou cadeau), jamais d'un tirage aléatoire dans
            // toute la base d'items.
            Inventory = new ContainerInventoryData(8, 4);
        }

        private void EnsureNpcClothing()
        {
            if (IsPlayer || !string.Equals(Species, "human", StringComparison.OrdinalIgnoreCase))
                return;

            Equipment ??= new Equipment();
            EnsureNpcClothingZone(BodyZone.Torso, "coat", "leaf_shirt");
            EnsureNpcClothingZone(BodyZone.Legs, "jean");
        }

        private void EnsureNpcClothingZone(BodyZone zone, params string[] itemKeys)
        {
            if (Equipment!.GetZoneItem(zone) != null)
                return;

            foreach (string itemKey in itemKeys)
            {
                if (!GameData.ItemDatabaseByKey.TryGetValue(itemKey, out var itemData))
                    continue;

                var clothing = new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
                if (Equipment.EquipOnBody(clothing, out _))
                    return;
            }
        }

        /// <summary>
        /// Si l'item est teignable, lui génère des couleurs personnalisées aléatoires (une par
        /// couche teignable) afin que les PNJ n'apparaissent pas tous avec les couleurs par
        /// défaut de leurs habits. Appelé une seule fois, à l'équipement de l'item au spawn.
        /// </summary>
        private static void ApplyRandomDyeColors(Item item, ItemData itemData)
        {
            if (!itemData.IsDyeable) return;

            // Nombre de couches réellement teignables (voir ItemRenderer.DrawItem) : la couche 1
            // n'est jamais teintée directement, seules les couches 2..DyeLayers le sont. Pour les
            // items "dye simple" (DyeLayers == 1), on garde tout de même 1 couleur : c'est elle qui
            // déclenche la couche de teinte supplémentaire (voir la logique _1.png).
            int dyeSlots = Math.Max(itemData.DyeLayers - 1, 1);

            for (int i = 0; i < dyeSlots; i++)
                item.SetLayerColor(i, RandomDyeColor());
        }

        /// <summary>Génère une couleur aléatoire agréable (évite les teintes trop sombres ou trop délavées).</summary>
        private static Color RandomDyeColor()
        {
            return new Color(
                (byte)Random.Shared.Next(40, 256),
                (byte)Random.Shared.Next(40, 256),
                (byte)Random.Shared.Next(40, 256),
                (byte)255);
        }

        // ==================== MÉTHODES PUBLIQUES ====================
        public string GetDisplayName() => !string.IsNullOrEmpty(FirstName) ? FirstName : !string.IsNullOrEmpty(CustomName) ? CustomName : Species;

        public string GetProfessionDisplayName() => Profession switch
        {
            ProfessionType.Farmer => Localization.Get("profession.farmer"),
            ProfessionType.Blacksmith => Localization.Get("profession.blacksmith"),
            ProfessionType.Lumberjack => Localization.Get("profession.lumberjack"),
            ProfessionType.Hunter => Localization.Get("profession.hunter"),
            ProfessionType.Alchemist => Localization.Get("profession.alchemist"),
            ProfessionType.Tailor => Localization.Get("profession.tailor"),
            ProfessionType.Miner => Localization.Get("profession.miner"),
            ProfessionType.Cook => Localization.Get("profession.cook"),
            ProfessionType.Guard => Localization.GetOrDefault("profession.guard", "Garde"),
            _ => Localization.Get("profession.trader")
        };

        public void AssignProfessionAndTrade(ProfessionType? forcedProfession = null, bool allowGuard = true)
        {
            if (Species != "human" || IsPlayer || IsTamed)
            {
                Profession = ProfessionType.None;
                TraderItems = new List<int>();
                IsTrader = false;
                return;
            }

            Profession = forcedProfession ?? (allowGuard && Random.Shared.NextDouble() < 0.1
                ? ProfessionType.Guard
                : Random.Shared.NextDouble() < 0.7 ? PickRandomProfession() : ProfessionType.None);
            if (Profession == ProfessionType.None)
            {
                TraderItems = new List<int>();
                IsTrader = false;
                return;
            }

            if (Profession == ProfessionType.Guard)
            {
                TraderItems = new List<int>();
                IsTrader = false;
                return;
            }

            InitTraderItems();
            IsTrader = TraderItems.Count > 0;
        }

        /// <summary>
        /// Donne 1 à 2 stacks de graines à un PNJ fermier fraîchement créé, sans quoi
        /// TryPlantSeedInFieldZone ne trouve jamais rien à planter (l'inventaire des
        /// PNJ est rempli d'objets aléatoires, sans lien avec la profession) et les
        /// champs générés restent vides pour toujours.
        /// </summary>
        private void GiveStarterSeedsToFarmer()
        {
            if (GameData.ItemDatabase == null || GameData.ItemDatabase.Count == 0)
                return;

            var seedDatas = GameData.ItemDatabase.Values
                .Where(d => d.IsSeed && d.CropId != 0)
                .OrderBy(_ => Random.Shared.Next())
                .Take(2)
                .ToList();

            foreach (var seedData in seedDatas)
            {
                var emptySlot = Inventory.Slots.FirstOrDefault(s => s.IsEmpty);
                if (emptySlot == null)
                    break;

                int qty = Math.Max(1, Math.Min(seedData.StackSize, 5));
                emptySlot.Item = new Item(seedData.Name, qty, seedData.Color, seedData.Icon);
                emptySlot.Count = qty;
            }

        }

        public void AssignWorkplace()
        {
            if (Profession == ProfessionType.None) return;

            _workApproachPos = null; // le point d'approche en cache correspondait à l'ancien poste
            _farmerWorkTarget = null;
            _farmerApproachPos = null;

            if (Profession == ProfessionType.Farmer
                && WorkZoneId >= 0
                && (!World.WorkZones.TryGetValue(WorkZoneId, out var assignedZone)
                    || assignedZone.Kind != WorkZoneKind.Field))
            {
                if (WorkplacePosition.HasValue)
                    World.ReleaseWorkSlot(WorkZoneId, WorkplacePosition.Value); // sinon l'emplacement restait "occupé" à vie
                WorkplacePosition = null;
                WorkZoneId = -1;
            }

            if (ProfessionData.StationObjectIds.TryGetValue(Profession, out var stationIds) && stationIds.Length > 0)
            {
                if (HomeBuildingId >= 0 && World.Houses.TryGetValue(HomeBuildingId, out var ownHouse))
                {
                    var found = World.FindStationTileInBounds(ownHouse.Bounds, stationIds);
                    if (found.HasValue)
                    {
                        WorkplacePosition = found.Value;
                        WorkZoneId = -1;
                        return;
                    }
                }

                var nearest = World.FindNearestUnclaimedStation(HomePosition, stationIds, 1500f, NetId);
                if (nearest.HasValue)
                {
                    WorkplacePosition = nearest.Value;
                    WorkZoneId = -1;
                    return;
                }

                return;
            }

            if (World.ProfessionZoneKind.TryGetValue(Profession, out var zoneKind))
            {
                //  Avec des villages pouvant s'étendre sur plusieurs centaines de
                // tuiles, la distance par défaut de FindNearestFreeWorkSlot (2000
                // unités ≈ 60 tuiles) est souvent trop courte pour relier une maison
                // à la zone de champ, qui peut être placée loin du centre : le
                // fermier ne trouvait alors jamais de poste, silencieusement.
                var slot = World.FindNearestFreeWorkSlot(zoneKind, HomePosition, 8000f);
                if (slot.HasValue)
                {
                    WorkplacePosition = slot.Value.slotPos;
                    WorkZoneId = slot.Value.zoneId;
                    if (Profession == ProfessionType.Farmer)
                        Console.WriteLine($"[Farming] PNJ {NetId} : poste de travail assigné, zone id={slot.Value.zoneId}, position={slot.Value.slotPos}.");
                }
                else if (Profession == ProfessionType.Farmer)
                {
                    if (!_assigningWorkplace && HomePosition != Vector2.Zero && World.TryCreateFieldNear(HomePosition))
                    {
                        // Garde anti-récursion : si le nouveau champ n'offre aucun emplacement, on ne
                        // rappelle pas AssignWorkplace() indéfiniment (StackOverflow).
                        _assigningWorkplace = true;
                        try { AssignWorkplace(); }
                        finally { _assigningWorkplace = false; }
                        return;
                    }

                    Console.WriteLine($"[Farming] PNJ {NetId} : AUCUN emplacement de champ libre trouvé près de HomePosition={HomePosition} (zones Field existantes : {World.WorkZones.Values.Count(z => z.Kind == WorkZoneKind.Field)}).");
                }
            }
        }

        private ProfessionType PickRandomProfession()
        {
            var values = Enum.GetValues<ProfessionType>()
                .Where(v => v != ProfessionType.None && v != ProfessionType.Guard)
                .ToArray();
            return values.Length == 0 ? ProfessionType.None : values[Random.Shared.Next(values.Length)];
        }

        public void InitTraderItems()
        {
            try
            {
                TraderItems = new List<int>();
                var professionItems = BuildProfessionItemIds();
                foreach (var itemId in professionItems)
                {
                    if (!TraderItems.Contains(itemId))
                        TraderItems.Add(itemId);
                    if (TraderItems.Count >= 8)
                        break;
                }

                // Les PNJ de métier ne doivent pas se retrouver avec un stock "générique" en plus
                // de leurs objets de métier. La liste de fallback ne doit servir que si le métier
                // ne fournit aucun objet cohérent (ou si l'on est dans le cas d'un marchand non
                // spécialisé), sinon on conserve strictement le pool de profession.
                if (TraderItems.Count == 0)
                {
                    var fallbackIds = GameData.ItemDatabase?.Keys
                        .Where(id => id > 0)
                        .OrderBy(_ => Random.Shared.Next())
                        .Take(12)
                        .ToList() ?? new List<int>();

                    foreach (var itemId in fallbackIds)
                    {
                        if (!TraderItems.Contains(itemId))
                            TraderItems.Add(itemId);
                        if (TraderItems.Count >= 8)
                            break;
                    }
                }

                IsTrader = TraderItems.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erreur dans InitTraderItems : {ex}");
                TraderItems = new List<int> { 1, 2, 3 };
                IsTrader = TraderItems.Count > 0;
            }
        }

        public List<int> GetProfessionItemIds() => BuildProfessionItemIds();

        private List<int> BuildProfessionItemIds()
        {
            var result = new List<int>();
            if (GameData.ItemDatabase == null || GameData.ItemDatabase.Count == 0)
                return result;

            var candidates = GameData.ItemDatabase.Values
                .Where(d => d.ID != 0 && d.Type != ItemType.None && d.Type != ItemType.Placeable && d.Type != ItemType.WallCovering)
                .ToList();

            if (candidates.Count == 0)
                return result;

            var professionKeys = ProfessionData.SaleItemKeys.TryGetValue(Profession, out var keys)
                ? keys
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matched = candidates
                .Where(item => MatchesProfessionSale(item, professionKeys, Profession))
                .OrderBy(_ => Random.Shared.Next())
                .ToList();

            foreach (var item in matched)
            {
                if (!result.Contains(item.ID))
                    result.Add(item.ID);
                if (result.Count >= 8)
                    break;
            }

            return result;
        }

        private static bool MatchesProfessionSale(ItemData item, HashSet<string> professionKeys, ProfessionType profession)
        {
            if (item.ID == 0)
                return false;

            if (professionKeys.Count > 0 && !string.IsNullOrWhiteSpace(item.Key) && professionKeys.Contains(item.Key))
                return true;

            return profession switch
            {
                ProfessionType.Farmer => item.Type == ItemType.Food || item.Type == ItemType.Resource || item.ToolKind == ToolType.Hoe || item.ToolKind == ToolType.Scythe || item.ToolKind == ToolType.WateringCan,
                ProfessionType.Blacksmith => item.Type == ItemType.Tool || item.Type == ItemType.Weapon || item.Type == ItemType.Armor || item.ToolKind == ToolType.Hammer || item.ToolKind == ToolType.Pickaxe,
                ProfessionType.Lumberjack => item.Type == ItemType.Tool || item.Type == ItemType.Resource || item.ToolKind == ToolType.Axe || item.ToolKind == ToolType.Shovel,
                ProfessionType.Hunter => item.Type == ItemType.Weapon || item.Type == ItemType.Resource || item.ToolKind == ToolType.Fishing || item.ToolKind == ToolType.Net,
                ProfessionType.Alchemist => item.Type == ItemType.Food || item.Type == ItemType.Resource || item.IsLightSource || item.Type == ItemType.Rune,
                ProfessionType.Tailor => item.Type == ItemType.Armor || item.Type == ItemType.Resource || item.ToolKind == ToolType.Scissors,
                ProfessionType.Miner => item.Type == ItemType.Tool || item.Type == ItemType.Resource || item.ToolKind == ToolType.Pickaxe || item.ToolKind == ToolType.Hammer,
                ProfessionType.Cook => item.Type == ItemType.Food || item.Type == ItemType.Resource,
                _ => true
            };
        }

        public void ApplyKnockback(Vector2 direction, float force)
        {
            if (_isSleeping)
                WakeFromSleep(8f);

            KnockbackVelocity = direction * force;
            IsKnockedBack = true;
        }

        public void ApplyPushImpulse(Vector2 impulse)
        {
            if (_isSleeping)
                WakeFromSleep(8f);

            KnockbackVelocity += impulse;
            IsKnockedBack = true;
        }

        public void WakeFromInteraction()
        {
            if (_isSleeping)
                WakeFromSleep(8f);
        }

        public void WakeFromTaming()
        {
            Concussion = 0f;
            IsKnockedOut = false;
            ClearAlertSignal();
            _retaliationTargetEntity = null;
            _retaliateAgainstPlayer = false;
            _retaliationTimer = 0f;
            _attackPhase = AttackPhase.None;

            if (_isSleeping)
                WakeFromSleep(8f);
            else
            {
                ReleaseCurrentGoalLock();
                _currentPath.Clear();
                _pathRecalcTimer = 0f;
                AiState = NpcAiState.Idle;
                AiTarget = WorldPos;
                Speed = BaseSpeed;
                AnimState = "idle";
            }
        }

        private void WakeFromSleep(float awakeDuration)
        {
            _isSleeping = false;
            _sleepVisualPosition = Vector2.Zero;
            _groundResting = false;
            _sleepTimer = 0f;
            _zzzTimer = 0f;
            _sleepWakeCooldown = Math.Max(_sleepWakeCooldown, awakeDuration);
            ReleaseCurrentGoalLock();
            AnimState = "idle";

            if (_restingInChair)
            {
                World.ReleaseBench(_reservedBench);
                _reservedBench = null;
                _restingInChair = false;
            }

            var speciesInfo = SpeciesData.GetSpeciesInfo(Species);
            Speed = speciesInfo?.Speed ?? BaseSpeed;
            Decide();
        }

        //  Décompte de l'icône d'alerte au-dessus de la tête, et émission différée du signal
        // d'écho une fois le petit délai aléatoire écoulé.
        private void UpdateAlertSignal(float dt, Vector2 playerPos)
        {
            if (_isSleeping || IsKnockedOut)
            {
                ClearAlertSignal();
                return;
            }

            if (ShowAlertIcon)
            {
                AlertIconTimer -= dt;
                if (AlertIconTimer <= 0f)
                    ShowAlertIcon = false;
            }

            if (_pendingSignalDelay >= 0f)
            {
                _pendingSignalDelay -= dt;
                if (_pendingSignalDelay <= 0f)
                {
                    _pendingSignalDelay = -1f;
                    AlertSignalManager.Emit(this, _pendingSignalTarget, _pendingSignalTargetConnectionId);
                }
            }
        }

        private void ClearAlertSignal()
        {
            IsAlerted = false;
            AlertTimer = 0f;
            ShowAlertIcon = false;
            AlertIconTimer = 0f;
            _pendingSignalDelay = -1f;
            _pendingSignalTarget = Vector2.Zero;
            _pendingSignalTargetConnectionId = -1;
            HasPropagatedSignal = false;
        }

        //  Appelée quand l'entité vient tout juste de repérer sa cible elle-même (pas via écho) :
        // affiche l'icône exclamation.png et programme, après un court délai aléatoire, l'envoi
        // du signal aux entités hostiles proches.
        public void TriggerAlertSignal(Vector2 targetPos, int targetConnectionId)
        {
            if (!IsAlive || _isSleeping || IsKnockedOut || IsTamed)
                return;

            // L'appel de détection intervient après le passage à Chase : ce n'est donc pas
            // un motif pour supprimer le signal. Éviter uniquement de programmer deux relais.
            if (_pendingSignalDelay >= 0f || HasPropagatedSignal)
                return;

            HasPropagatedSignal = true;
            ShowAlertIcon = true;
            AlertIconTimer = ALERT_ICON_DURATION;
            SoundEffects.PlayEntitySurprise();

            if (_pendingSignalDelay < 0f)
            {
                _pendingSignalDelay = (float)(Random.Shared.NextDouble() * 0.6 + 0.2); // 0.2s à 0.8s
                _pendingSignalTarget = targetPos;
                _pendingSignalTargetConnectionId = targetConnectionId;
            }
        }

        //  Appelée quand l'entité reçoit le signal d'une voisine : elle réagit une fois, sans
        // retransmettre le signal.
        public void ReceiveAlertSignal(Vector2 targetPos, int targetConnectionId)
        {
            if (!IsAlive || _isSleeping || IsKnockedOut || IsTamed
                || (Behavior != "hostile" && Behavior != "neutral")) return;
            if (_pendingSignalDelay >= 0f)
            {
                _pendingSignalDelay = -1f;
                HasPropagatedSignal = true;
            }
            if (AiState == NpcAiState.Flee || _currentGoalPriority >= NpcPriority.Survival)
                return;
            if (AiState == NpcAiState.Chase)
                return; // déjà en poursuite active : ne pas re-warn pendant que le combat est engagé
            if (HasPropagatedSignal) return; // déjà alertée et déjà relayée

            ResetPathfinding();
            ForceGoal(NpcPriority.Combat, 1.2f);
            AiTarget = targetPos;
            AiTargetConnectionId = targetConnectionId;
            Speed = RunSpeed;
            AiTimer = 2f;
            IsAlerted = true;
            AlertTimer = AlertDuration;

            //  Alerté par relais (écho) plutôt que par détection directe : la cible peut être
            // loin, donc même logique de monte que la détection directe (voir plus haut).
            TryMountOwnedPet(targetPos);

            ShowAlertIcon = true;
            AlertIconTimer = ALERT_ICON_DURATION;
            SoundEffects.PlayEntitySurprise();
            HasPropagatedSignal = true;
        }

        public void OnHit(Vector2? attackerPos = null, Entity? attackerEntity = null, bool attackerIsPlayer = true, int attackerConnectionId = -1)
        {
            if (IsKnockedOut)
            {
                DamageFlash = 0.2f;
                PlayHurtSound();
                return;
            }

            bool wasSleeping = _isSleeping;
            Vector2 hitPos = attackerPos ?? Program.GetPlayerPosition();
            _fearTargetEntity = attackerEntity;
            _fearTargetIsPlayer = attackerEntity == null && attackerIsPlayer;
            _fearTargetConnectionId = _fearTargetIsPlayer ? attackerConnectionId : -1;
            _fearTargetPosition = attackerEntity?.WorldPos ?? hitPos;
            _hasFearTarget = true;
            bool canRetaliate = IsVillageGuard || IsSpiritAnimal || Behavior == "hostile" || Behavior == "neutral"
                || (IsTamed && TamedBehavior == TamedAnimalMode.Attack);
            //  CORRECTIF : un animal apprivoisé par le joueur ne riposte jamais contre son maître
            // (seuls les pets "hostiles" d'un PNJ hostile, IsHostilePet, gardent ce réflexe).
            bool retaliateAgainstPlayerAllowed = !IsTamed || IsHostilePet;
            if (!canRetaliate)
            {
                _retaliationTargetEntity = null;
                _retaliateAgainstPlayer = false;
                _retaliationTimer = 0f;
            }
            else if (attackerEntity != null)
            {
                _retaliationTargetEntity = attackerEntity;
                _retaliateAgainstPlayer = false;
                _retaliationTimer = RETALIATION_DURATION;
            }
            else if (attackerIsPlayer && retaliateAgainstPlayerAllowed)
            {
                _retaliationTargetEntity = null;
                _retaliateAgainstPlayer = true;
                _retaliationTimer = RETALIATION_DURATION;
            }
            DamageFlash = 0.2f;
            PlayHurtSound();

            if (_isSleeping)
                WakeFromSleep(8f);

            if (IsWorm)
            {
                if (_wmState == WormBossState.SurfaceRecover)
                {
                    _wmState = WormBossState.HitStagger;
                    _wmTimer = 0f;
                    AddWormGroundMark(WormGroundMarkType.Telegraph, _wmSurfacePoint, 54f, 0.55f);
                    SpawnWormDebris(_wmSurfacePoint, 0.75f);
                    Program.ApplyScreenShake(3f, 0.14f);
                }
                return;
            }

            //  Une entité qui vient de se prendre un coup considère toujours sa vie en danger :
            // ceci prend le pas sur n'importe quel autre objectif en cours (routine, nourriture,
            // discussion...), et verrouille ce nouvel objectif un court instant pour éviter qu'il
            // ne soit aussitôt écrasé par la logique de routine de la même frame.
            bool isVillageGuard = IsVillageGuard;
            //  CORRECTIF : un animal apprivoisé n'a JAMAIS le réflexe de fuir (priorité Survie à
            // 100 qui écrasait OwnerCommand=60 et le laissait ignorer les ordres du joueur).
            bool aboutToFlee = !isVillageGuard && !IsSpiritAnimal && Behavior != "hostile" && Behavior != "neutral" && !IsTamed;
            bool aboutToTurnHostile = Behavior == "neutral" && !IsTamed;
            if (aboutToFlee)
                ForceGoal(NpcPriority.Survival, 3f);
            else if (aboutToTurnHostile || Behavior == "hostile")
                ForceGoal(NpcPriority.Combat, 1.5f);

            if (Behavior == "neutral" && !IsTamed)
            {
                Behavior = "hostile";
                ResetPathfinding();
                AiState = NpcAiState.Chase;
                AiTarget = attackerEntity?.WorldPos ?? _fearTargetPosition;
                AiTargetConnectionId = attackerEntity == null ? attackerConnectionId : -1;
                Speed = RunSpeed;
                _chaseStartPos = WorldPos;
                _chaseElapsedTime = 0f;
                IsAlerted = true;
                AlertTimer = AlertDuration;
                if (IsVillager && TryStartInfirmaryCare())
                    return;
                if (attackerEntity == null && attackerIsPlayer && !wasSleeping)
                    TriggerAlertSignal(AiTarget, AiTargetConnectionId);
                return;
            }

            if (IsVillager && TryStartInfirmaryCare())
                return;

            if (Behavior != "hostile" && !isVillageGuard && !IsSpiritAnimal && !IsTamed)
            {
                ResetPathfinding();
                IsAlerted = true;
                AlertTimer = AlertDuration;
                AlertOrigin = WorldPos;
                _continuousFearTimer = 0f;

                Vector2 fleeTargetPos = GetFearTargetPosition();
                Vector2 fleeDir = WorldPos - fleeTargetPos;
                if (fleeDir.Length() > 0.01f) fleeDir = Vector2.Normalize(fleeDir);
                else fleeDir = new Vector2(1, 0);
                Vector2 fleeTarget = WorldPos + fleeDir * 300f;
                AiTarget = fleeTarget;
                AiState = NpcAiState.Flee;
                _attackPhase = AttackPhase.None;
                AiTimer = 8f;
                Speed = RunSpeed;
            }
        }

        private bool TryStartInfirmaryCare()
        {
            if (!IsVillager || IsTamed || !IsAlive || CurrentHP >= MaxHP)
                return false;
            if (_infirmaryBuildingId >= 0)
                return true;

            if (!World.TryReserveNearestInfirmary(this, out int buildingId, out Vector2 approachPos))
            {
                _infirmaryRetryTimer = 5f;
                return false;
            }

            _infirmaryBuildingId = buildingId;
            _infirmaryHealAccumulator = 0f;
            _infirmaryRetryTimer = 0f;
            _retaliationTargetEntity = null;
            _retaliateAgainstPlayer = false;
            _retaliationTimer = 0f;
            _attackPhase = AttackPhase.None;
            _currentPath.Clear();
            _pathRecalcTimer = 0f;
            _pathFailStreak = 0;
            AiTarget = approachPos;
            AiState = NpcAiState.GoToInfirmary;
            Speed = RunSpeed > 0f ? RunSpeed : BaseSpeed;
            AnimState = "walk";
            ForceGoal(NpcPriority.Medical, 3f);
            return true;
        }

        public void UpdateWaterStatus()
        {
            int tileX = (int)(WorldPos.X / Program.TileSize);
            int tileY = (int)(WorldPos.Y / Program.TileSize);
            int groundId = World.GetGroundTileIdAt(tileX, tileY);
            IsInWater = (groundId == 10);
        }

        public Vector2 FeetPos => new(WorldPos.X, WorldPos.Y + Program.FeetOffsetY);
        public bool IsSleeping => _isSleeping;
        public Vector2 VisualWorldPos => _isSleeping && _sleepVisualPosition != Vector2.Zero
            ? _sleepVisualPosition
            : WorldPos;
        public bool IsAlive => CurrentHP > 0;

        private float GetFleeRadius()
        {
            return VisionRange * Program.TileSize;
        }

        private float GetFleeTriggerRadius()
        {
            return GetFleeRadius() * 0.5f;
        }

        private float GetFleeSafeRadius()
        {
            return GetFleeRadius() * 1.15f;
        }

        public void ForceWakeFromTent()
        {
            if (!_isSleeping || HomeBuildingId != TentHomeBuildingId)
                return;

            _isSleeping = false;
            _sleepVisualPosition = Vector2.Zero;
            _sleepTimer = 0f;
            _tentSleepTimer = 0f;
            _zzzTimer = 0f;
            ReleaseCurrentGoalLock();
            AnimState = "idle";
            Speed = BaseSpeed;
            AiState = NpcAiState.Idle;
            AiTarget = WorldPos;
            _currentPath.Clear();
            _morningDeparturePending = true;
        }

        public void WakeFromCarry()
        {
            if (!_isSleeping)
                return;

            _isSleeping = false;
            _groundResting = false;
            _sleepVisualPosition = Vector2.Zero;
            _sleepTimer = 0f;
            _zzzTimer = 0f;
            ReleaseCurrentGoalLock();
            AnimState = "idle";
            if (_restingInChair)
            {
                World.ReleaseBench(_reservedBench);
                _reservedBench = null;
                _restingInChair = false;
            }

            var speciesInfo = SpeciesData.GetSpeciesInfo(Species);
            if (speciesInfo != null)
                Speed = speciesInfo.Speed;
            Decide();
        }

    }
}
