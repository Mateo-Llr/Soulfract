#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public partial class Entity
    {
        // Tamed animal commands and armor companion behavior
        // ==================== ORDRES DES ANIMAUX APPRIVOISÉS ====================
        private TamedAnimalMode? _lastAppliedTamedMode;
        private float _petTargetScanTimer;
        private const float PET_TARGET_RANGE = 450f;        // rayon de détection autour du pet
        private const float PET_OWNER_LEASH = 700f;         // cible valide seulement si proche du maître
        private const float PET_FOLLOW_RESUME_DISTANCE = 100f; // repart au-delà (rayon d'arrivée = 65 px)

        //  Sélectionne (avec verrouillage) l'ennemi le plus proche d'un animal du joueur en mode
        // Attack : monstre hostile, ni apprivoisé, ni villageois, proche du pet ET du maître.
        private Entity? FindPlayerPetTarget(Vector2 ownerPos, float dt)
        {
            static bool Valid(Entity t) => t.IsAlive && !t.IsPlayer && !t.IsVillager && !t.IsVillageGuard
                && !t.IsInvulnerable && t.Behavior == "hostile" && (!t.IsTamed || t.IsHostilePet);

            if (_hostileTargetEntity != null)
            {
                var locked = _hostileTargetEntity;
                if (Valid(locked)
                    && Vector2.DistanceSquared(WorldPos, locked.WorldPos) <= (PET_TARGET_RANGE * 1.5f) * (PET_TARGET_RANGE * 1.5f)
                    && Vector2.DistanceSquared(ownerPos, locked.WorldPos) <= PET_OWNER_LEASH * PET_OWNER_LEASH)
                    return locked;
                _hostileTargetEntity = null;
            }

            _petTargetScanTimer -= dt;
            if (_petTargetScanTimer > 0f) return null;
            _petTargetScanTimer = 0.4f; // pas de scan complet de la liste à chaque frame

            Entity? best = null;
            float bestSq = PET_TARGET_RANGE * PET_TARGET_RANGE;
            foreach (var c in Program.GetEntities())
            {
                if (ReferenceEquals(c, this) || !Valid(c)) continue;
                if (Vector2.DistanceSquared(ownerPos, c.WorldPos) > PET_OWNER_LEASH * PET_OWNER_LEASH) continue;
                float d = Vector2.DistanceSquared(WorldPos, c.WorldPos);
                if (d >= bestSq) continue;
                bestSq = d;
                best = c;
            }
            _hostileTargetEntity = best;
            return best;
        }

        //  Point UNIQUE d'application des ordres du propriétaire (Follow/Stay/Roam/Attack). Les
        // ordres reprennent la main de façon INCONDITIONNELLE (ForceGoal) au lieu de passer par
        // TryCommitGoal, que n'importe quelle priorité résiduelle (Survie, Combat...) faisait échouer.
        //  Appelée par le menu radial (TamedAnimalInteraction) : applique un ordre PROPREMENT.
        // Avant, le menu posait TamedBehavior à la main et, pour Attack, laissait Behavior =
        // "hostile" à vie (même après un ordre Suivre/Attendre), ce qui gardait l'animal dans la
        // logique des monstres (riposte, priorité Combat, cible des gardes...).
        public void ApplyTamedOrder(TamedAnimalMode mode)
        {
            TamedBehavior = mode;
            Behavior = "tamed";
            FollowOrder = -1; // force la réattribution propre de la file de suivi
            _hostileTargetEntity = null;
            _retaliationTargetEntity = null;
            _retaliateAgainstPlayer = false;
            _retaliationTimer = 0f;
            _hasFearTarget = false;
            _fearTargetEntity = null;
            _fearTargetIsPlayer = false;
            _fearTargetConnectionId = -1;
            IsAlerted = false;
            ShowAlertIcon = false;
            _attackPhase = AttackPhase.None;
            ReleaseCurrentGoalLock();
            AiState = NpcAiState.Idle;
            AiTarget = WorldPos;
            AiTimer = 0f;
            _currentPath.Clear();
            _pathRecalcTimer = 0f;
            Speed = BaseSpeed;
            AnimState = "idle";
        }

        private void UpdateTamedOrders(Vector2 ownerPos)
        {
            // Anciennes sauvegardes : un animal resté "hostile" après un ordre Attaque.
            if (Behavior != "tamed") Behavior = "tamed";

            bool modeChanged = _lastAppliedTamedMode != TamedBehavior;
            _lastAppliedTamedMode = TamedBehavior;

            bool attackMode = TamedBehavior == TamedAnimalMode.Attack;
            bool hasCombatTarget = _combatTargetEntity != null;
            bool mustLeaveCombat = AiState == NpcAiState.Chase
                && (!attackMode || (!hasCombatTarget && !IsHostilePet));
            bool leftoverPanic = !attackMode
                && (AiState == NpcAiState.Flee || AiState == NpcAiState.LookAtPlayer);

            if (mustLeaveCombat || leftoverPanic)
            {
                _attackPhase = AttackPhase.None;
                _hostileTargetEntity = null;
                IsAlerted = false;
                ShowAlertIcon = false;
                ReleaseCurrentGoalLock();
                AiState = NpcAiState.Idle;
                AiTarget = WorldPos;
                _currentPath.Clear();
                _pathRecalcTimer = 0f;
                Speed = BaseSpeed;
                AnimState = "idle";
            }
            else if (modeChanged)
            {
                _pathRecalcTimer = 0f;
                _currentPath.Clear();
            }

            // Un apprivoisé (hors mode Attack) ne "s'alerte" plus : sinon AlertFreeze le gèle sur place.
            if (!attackMode) { IsAlerted = false; ShowAlertIcon = false; }

            switch (TamedBehavior)
            {
                case TamedAnimalMode.Stay:
                    ForceGoal(NpcPriority.OwnerCommand);
                    if (AiState != NpcAiState.Idle)
                    {
                        AiState = NpcAiState.Idle;
                        AiTarget = WorldPos;
                        _currentPath.Clear();
                    }
                    Speed = 0f;
                    AnimState = "idle";
                    AiTimer = Math.Max(AiTimer, 1f); // empêche Decide() de le faire déambuler
                    break;

                case TamedAnimalMode.Follow:
                    FollowOwnerOrder(ownerPos);
                    break;

                case TamedAnimalMode.Roam:
                    if (modeChanged || AiState == NpcAiState.Follow)
                    {
                        // Sort de Follow/Stay : Decide() choisira une déambulation libre.
                        ReleaseCurrentGoalLock();
                        AiState = NpcAiState.Idle;
                        AiTarget = WorldPos;
                        AiTimer = 0f;
                    }
                    break;

                case TamedAnimalMode.Attack:
                    // En attente d'un ennemi (pas de cible), un animal du joueur reste près de lui.
                    if (!hasCombatTarget && !IsHostilePet)
                        FollowOwnerOrder(ownerPos);
                    break;
            }
        }

        //  Suit le propriétaire avec une marge (hystérésis) : une fois arrivé, le pet reste Idle
        // (et non plus Follow/Idle en alternance à chaque frame, ce qui relançait un A* par frame)
        // et ne repart que lorsque le maître s'éloigne.
        private void FollowOwnerOrder(Vector2 ownerPos)
        {
            ForceGoal(NpcPriority.OwnerCommand);
            if (AiState == NpcAiState.Follow) return;

            //  On compare à AiTarget (position de formation calculée par
            // Program.UpdateTamedAnimalTargets : le joueur pour le 1er, le pet précédent pour les
            // suivants) et non au joueur, sinon les derniers de la file, naturellement loin du joueur,
            // repasseraient en Follow à chaque frame.
            bool ownerFar = Vector2.DistanceSquared(WorldPos, AiTarget)
                > PET_FOLLOW_RESUME_DISTANCE * PET_FOLLOW_RESUME_DISTANCE;
            if (AiState != NpcAiState.Idle || ownerFar)
            {
                AiState = NpcAiState.Follow;
                _pathRecalcTimer = 0f;
                if (Speed <= 0f) Speed = BaseSpeed;
            }
            else
            {
                AiTimer = Math.Max(AiTimer, 0.5f); // reste près du maître, pas de balade via Decide()
            }
        }

        private void UpdateFollowSpeed()
        {
            if (IsTamed && (TamedBehavior == TamedAnimalMode.Follow || TamedBehavior == TamedAnimalMode.Attack)
                && AiState == NpcAiState.Follow)
            {
                float distToTarget = Vector2.Distance(WorldPos, AiTarget);
                Speed = distToTarget > 250 ? RunSpeed : BaseSpeed;
            }
        }

        // ==================== COMPAGNON D'ARMURE (ex : petit crabe) ====================
        private const float PET_ATTACK_RANGE = 34f;          // distance de mêlée pour infliger des dégâts
        private const float PET_AGGRO_GIVE_UP_RANGE = 500f;  // cible trop loin du joueur -> abandon
        private const float PET_ATTACK_COOLDOWN = 0.9f;
        private const int PET_ATTACK_DAMAGE = 3;
        private const float PET_FOLLOW_STOP_DISTANCE = 55f;  // ne vient pas coller aux pieds du joueur

        /// <summary>
        /// IA du compagnon (voir IsPetCompanion) : suit le joueur (en réutilisant la position de
        /// formation calculée par Program.UpdateTamedAnimalTargets, via AiTarget) tant qu'aucune
        /// cible n'est assignée, et se rue sur PetCombatTarget dès que
        /// Program.NotifyPlayerAttackedByEntity en assigne une (le joueur vient d'être attaqué).
        /// </summary>
        private void UpdatePetCompanionAI(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            UpdateWaterStatus();

            if (_petAttackTimer > 0f) _petAttackTimer -= dt;

            // Abandonne la cible si elle est morte ou si elle s'est trop éloignée du joueur.
            if (PetCombatTarget != null)
            {
                if (!PetCombatTarget.IsAlive || Vector2.DistanceSquared(PetCombatTarget.WorldPos, playerPos) > (PET_AGGRO_GIVE_UP_RANGE) * (PET_AGGRO_GIVE_UP_RANGE))
                    PetCombatTarget = null;
            }

            Vector2 desiredTarget;
            float desiredSpeed;

            if (PetCombatTarget != null)
            {
                var target = PetCombatTarget;
                float distToTarget = Vector2.Distance(
                    World.GetEntityGroundPosition(WorldPos),
                    World.GetEntityGroundPosition(target.WorldPos));
                if (distToTarget > 0.01f)
                    Facing = (target.WorldPos.X - WorldPos.X) >= 0 ? 1f : -1f;

                if (distToTarget > PET_ATTACK_RANGE)
                {
                    desiredTarget = target.WorldPos;
                    desiredSpeed = RunSpeed > 0f ? RunSpeed : BaseSpeed * 1.6f;
                    AnimState = "walk";
                }
                else
                {
                    desiredTarget = WorldPos; // reste en place pour frapper
                    desiredSpeed = 0f;
                    AnimState = "attack";

                    if (_petAttackTimer <= 0f)
                    {
                        _petAttackTimer = PET_ATTACK_COOLDOWN;
                        target.CurrentHP -= PET_ATTACK_DAMAGE;
                        target.OnHit(WorldPos, this, attackerIsPlayer: false);

                        Vector2 kb = target.WorldPos - WorldPos;
                        kb = kb.LengthSquared() > 0.01f ? Vector2.Normalize(kb) : new Vector2(Facing, 0);
                        target.ApplyKnockback(kb, 90f);

                        int ts = Program.TileSize;
                        int th = World.GetHeightAt((int)(target.WorldPos.X / ts), (int)(target.WorldPos.Y / ts));
                        Vector2 targetVisualPos = new Vector2(target.WorldPos.X, target.WorldPos.Y - th * ts / 4f);
                        Program.AddFloatingDamage(targetVisualPos, PET_ATTACK_DAMAGE, false, isEnemy: true);

                        if (!target.IsAlive)
                        {
                            PetCombatTarget = null;
                            var speciesInfo = SpeciesData.GetSpeciesInfo(target.Species);
                            if (speciesInfo == null || !speciesInfo.CanBeCaught)
                            {
                                foreach (var (id, qty, _) in GameData.GetAnimalDrops(target.Species))
                                    Program.GiveItemToPlayer(id, qty, target.WorldPos);
                            }
                        }
                    }
                }
            }
            else
            {
                // Pas de cible : suit le joueur (formation gérée par Program.UpdateTamedAnimalTargets),
                // mais s'arrête à une distance confortable au lieu de venir se coller aux pieds du joueur.
                desiredTarget = AiTarget;
                float distToFollow = Vector2.Distance(WorldPos, desiredTarget);
                if (distToFollow > PET_FOLLOW_STOP_DISTANCE)
                {
                    desiredSpeed = distToFollow > 250f ? RunSpeed : BaseSpeed;
                    AnimState = "walk";
                    Facing = (desiredTarget.X - WorldPos.X) >= 0 ? 1f : -1f;
                }
                else
                {
                    desiredSpeed = 0f;
                    AnimState = "idle";
                }
            }

            Speed = desiredSpeed;

            if (desiredSpeed > 0f)
            {
                Vector2 dir = desiredTarget - WorldPos;
                float dist = dir.Length();
                if (dist > 4f)
                {
                    dir /= dist;
                    Vector2 move = dir * desiredSpeed * dt;
                    if (move.Length() > dist) move = dir * dist;

                    Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                    if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                        WorldPos.X += move.X;
                    Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                    if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                        WorldPos.Y += move.Y;
                }
            }

            // Animation (retombe sur "idle" si l'espèce n'a pas l'animation demandée).
            float animSpeed = AnimState == "walk" ? 8f : (AnimState == "attack" ? 10f : 2f);
            AnimProgress += dt * animSpeed;
            if (AnimProgress >= 1f)
            {
                AnimProgress = 0f;
                int frameCount = 8;
                if (SpeciesData.Skeletons.TryGetValue(Species, out var skeleton))
                {
                    var firstPart = skeleton.FirstOrDefault();
                    string animKey = (firstPart != null && firstPart.Animations.ContainsKey(AnimState)) ? AnimState : "idle";
                    if (firstPart != null && firstPart.Animations.TryGetValue(animKey, out var frames) && frames.Count > 0)
                        frameCount = frames.Count;
                }
                if (frameCount == 0) frameCount = 1;
                CurrentFrame = (CurrentFrame + 1) % frameCount;
            }
        }

        //  Couleur de peau/fourrure aléatoire par entité, tirée dans la plage [ColorMin, ColorMax]
        // définie dans species.json (colorMin/colorMax), si l'espèce est marquée "tintable".
        // Si un préset est fourni et définit sa propre plage de couleur, celle-ci est prioritaire.
        // Sinon, retombe sur la couleur fixe historique (GetColorForSpecies).
        public static void GenerateRandomAppearance(
            string species,
            Vector2 position,
            out Color tint,
            Dictionary<string, int> randomFeatureVariant,
            Dictionary<string, Color> randomFeatureColor)
        {
            string renderSpecies = string.Equals(species, "Genie", StringComparison.OrdinalIgnoreCase) ? "human" : species;
            SpeciesData.ColorPreset? colorPreset = null;

            if (string.Equals(renderSpecies, "human", StringComparison.OrdinalIgnoreCase))
            {
                tint = Program.SkinColorPalette[Random.Shared.Next(Program.SkinColorPalette.Length)];
            }
            else
            {
                Biome? spawnBiome = null;
                if (Program.TileSize > 0)
                {
                    spawnBiome = World.GetBiomeAt(position.X / Program.TileSize, position.Y / Program.TileSize);
                }
                colorPreset = SpeciesData.PickColorPreset(renderSpecies, spawnBiome);
                tint = GetRandomSkinColor(renderSpecies, colorPreset);
            }

            if (string.Equals(species, "Genie", StringComparison.OrdinalIgnoreCase))
                tint = GenieBlueTint;

            AssignRandomFeatures(colorPreset, randomFeatureVariant, randomFeatureColor, renderSpecies);
        }

        private static Color GetRandomSkinColor(string species, SpeciesData.ColorPreset? preset = null)
        {
            if (preset != null && preset.ColorMin.HasValue && preset.ColorMax.HasValue)
                return RandomColorInRange(preset.ColorMin.Value, preset.ColorMax.Value);

            var info = SpeciesData.GetSpeciesInfo(species);
            if (info != null && info.Tintable)
                return RandomColorInRange(info.ColorMin, info.ColorMax);

            return GetColorForSpecies(species);
        }

        private static Color RandomColorInRange(Color min, Color max)
        {
            int rMin = Math.Min(min.R, max.R);
            int rMax = Math.Max(min.R, max.R);
            int gMin = Math.Min(min.G, max.G);
            int gMax = Math.Max(min.G, max.G);
            int bMin = Math.Min(min.B, max.B);
            int bMax = Math.Max(min.B, max.B);

            return new Color(
                Random.Shared.Next(rMin, rMax + 1),
                Random.Shared.Next(gMin, gMax + 1),
                Random.Shared.Next(bMin, bMax + 1),
                255);
        }

        private static Color GetColorForSpecies(string species)
        {
            return species switch
            {
                "pig" => new Color(255, 200, 200, 255),
                "sheep" => new Color(240, 240, 240, 255),
                "cow" => new Color(210, 170, 100, 255),
                "chicken" => new Color(255, 240, 180, 255),
                "duck" => new Color(220, 200, 120, 255),
                "wolf" => new Color(180, 180, 200, 255),
                "bear" => new Color(160, 120, 80, 255),
                "goat" => new Color(200, 180, 150, 255),
                "horse" => new Color(180, 140, 100, 255),
                "bat" => new Color(80, 80, 100, 255),
                "skeleton" => new Color(200, 200, 210, 255),
                _ => Color.White
            };
        }

        //  CARACTÈRES ALÉATOIRES : tire au sort, pour chaque feature déclarée dans species.json
        // pour cette espèce, si l'entité la possède et quelle variante elle affiche.
        // Si un préset est fourni, il peut forcer la présence/absence, la variante et la couleur
        // de chaque feature ; sinon le tirage 100% aléatoire habituel s'applique.
        private static void AssignRandomFeatures(
            SpeciesData.ColorPreset? preset,
            Dictionary<string, int> randomFeatureVariant,
            Dictionary<string, Color> randomFeatureColor,
            string? renderSpeciesOverride = null)
        {
            randomFeatureVariant.Clear();
            randomFeatureColor.Clear();

            string speciesKey = (renderSpeciesOverride ?? "").ToLowerInvariant();
            if (!SpeciesData.Species.TryGetValue(speciesKey, out var info) || info.RandomFeatures == null)
                return;

            foreach (var feature in info.RandomFeatures)
            {
                if (string.IsNullOrWhiteSpace(feature.name)) continue;

                int variantCount = SpeciesData.GetFeatureVariantCount(speciesKey, feature.name);

                SpeciesData.PresetFeature? presetFeature = null;
                preset?.Features.TryGetValue(feature.name, out presetFeature);

                bool hasFeature;
                if (presetFeature?.Enabled == true)
                    hasFeature = variantCount > 0;
                else if (presetFeature?.Enabled == false)
                    hasFeature = false;
                else
                    hasFeature = variantCount > 0 && Random.Shared.NextDouble() < feature.chance;

                int chosenVariant = -1;
                if (hasFeature)
                {
                    chosenVariant = (presetFeature != null && presetFeature.ForceVariant && presetFeature.Variant < variantCount)
                        ? presetFeature.Variant
                        : Random.Shared.Next(0, variantCount);
                }

                randomFeatureVariant[feature.name] = chosenVariant;

                if (hasFeature && feature.colorable)
                {
                    Color cMin = presetFeature?.ColorMin ?? new Color(40, 40, 40, 255);
                    Color cMax = presetFeature?.ColorMax ?? new Color(240, 240, 240, 255);
                    randomFeatureColor[feature.name] = RandomColorInRange(cMin, cMax);
                }
            }

            //  Couleur d'yeux customisée : priorité au préset, sinon plage de l'espèce, sinon pas de teinte (blanc).
            // Stockée sous une clé réservée dans RandomFeatureColor pour être transmise à EntityRenderer
            // sans changer la signature de DrawEntity.
            if (info.EyeColors.Count > 0)
            {
                randomFeatureColor["__eyeColor"] = info.EyeColors[Random.Shared.Next(info.EyeColors.Count)];
            }
            else
            {
                Color? eyeMin = preset?.EyeColorMin ?? info.EyeColorMin;
                Color? eyeMax = preset?.EyeColorMax ?? info.EyeColorMax;
                if (eyeMin.HasValue && eyeMax.HasValue)
                randomFeatureColor["__eyeColor"] = RandomColorInRange(eyeMin.Value, eyeMax.Value);
            }
        }

        private void SpawnFootstepParticle()
        {
            int tileX = (int)(WorldPos.X / Program.TileSize);
            int tileY = (int)(WorldPos.Y / Program.TileSize);
            int groundId = World.GetGroundTileIdAt(tileX, tileY);
            Color particleColor = World.GetParticleColorForGround(groundId);

            int height = World.GetHeightAt(tileX, tileY);
            float yOffset = -height * Program.TileSize / 4;
            Vector2 footPos = new Vector2(WorldPos.X, WorldPos.Y + yOffset + Program.FeetOffsetY);

            Random rand = new Random();
            int count = rand.Next(1, 3);
            for (int i = 0; i < count; i++)
            {
                Vector2 vel = new Vector2(
                    (float)(rand.NextDouble() - 0.5) * 40f,
                    -(float)(rand.NextDouble() * 30f + 10f)
                );
                float size = rand.Next(2, 5);
                float lifetime = rand.Next(300, 600) / 1000f;
                Program.GetParticleList().Add(new Particle(footPos, vel, particleColor, size, lifetime));
            }
        }

        private void SpawnZzzParticle()
        {
            if (!_isSleeping) return;

            float headOffsetX = (float)(Random.Shared.NextDouble() - 0.5) * 15f;
            float headOffsetY = -35f - (float)(Random.Shared.NextDouble() * 10f);
            Vector2 visualPos = World.GetEntityGroundPosition(WorldPos);
            Vector2 zzzPos = new Vector2(visualPos.X + headOffsetX, visualPos.Y + headOffsetY);

            Vector2 velocity = new Vector2(
                (float)(Random.Shared.NextDouble() - 0.5) * 15f,
                -(float)(Random.Shared.NextDouble() * 20f + 15f)
            );

            float size = Random.Shared.Next(12, 20);
            float lifetime = Random.Shared.Next(800, 1400) / 1000f;

            Color zzzColor = new Color((byte)220, (byte)220, (byte)180, (byte)Random.Shared.Next(180, 255));

            Program.GetParticleList().Add(new ZzzParticle(zzzPos, velocity, zzzColor, size, lifetime));

            _zzzCurrentInterval = (float)(Random.Shared.NextDouble() * 1.0f + 0.5f);
        }

        private void UpdateSleepState(float darkness, float dt)
        {
            if (!IsInPlayerDialogue)
                _sleepWakeCooldown = Math.Max(0f, _sleepWakeCooldown - dt);

            bool isTentResident = IsCaveTentResident;
            if (HomeBuildingId == TentHomeBuildingId && _tentRestCooldown > 0f)
                _tentRestCooldown = Math.Max(0f, _tentRestCooldown - dt);
            bool isNight = isTentResident ? _tentRestCooldown <= 0f : darkness > 0.6f;
            if (isNight)
                _farmerSupplyPreparedToday = false;

            //  Un oiseau ne peut pas dormir en plein vol : il doit forcément se poser au sol
            // d'abord. S'il fait nuit alors qu'il est encore en l'air, on force un atterrissage
            // anticipé (au lieu d'attendre la fin normale du vol) pour qu'il puisse ensuite
            // s'endormir comme n'importe quelle autre créature au sol.
            if (IsBird && IsBirdFlying)
            {
                if (isNight && _birdFlightState == BirdFlightState.Flying)
                {
                    _birdFlightState = BirdFlightState.Landing;
                    _birdLandingTimer = _birdLandingDuration;
                }
                return;
            }

            // Un item peut être donné à un PNJ déjà endormi. Dans ce cas il doit se réveiller
            // immédiatement pour relancer la recherche du coffre, au lieu de rester bloqué par
            // _hasDepositedTonight jusqu'au matin.
            if (_isSleeping && HomeBuildingId >= 0 && World.HasAnyDepositableItem(this))
            {
                _isSleeping = false;
                _groundResting = false;
                _hasDepositedTonight = false;
                _sleepTimer = 0f;
                _zzzTimer = 0f;
                ReleaseCurrentGoalLock();
                AnimState = "idle";
                Speed = BaseSpeed;
                //  Libérer la chaise réservée pour la nuit (voir GetSleepVisualPosition), sinon
                // elle resterait marquée occupée indéfiniment alors que le PNJ n'y est plus assis.
                if (_restingInChair)
                {
                    World.ReleaseBench(_reservedBench);
                    _reservedBench = null;
                    _restingInChair = false;
                }
            }

            //  Un PNJ ne doit s'endormir que s'il est effectivement arrivé chez lui
            // (ou s'il n'a pas de maison, auquel cas il s'endort sur place faute de mieux).
            // Pour une maison, l'arrivée est l'ensemble des tuiles intérieures : HomePosition
            // n'est qu'une case attribuée au PNJ, pas le centre obligatoire de la maison.
            bool hasHome = HomePosition != Vector2.Zero;
            //  Les tentes restent évaluées par distance, car elles n'ont pas de liste de tuiles
            //  intérieures comparable à celle d'une maison.
            const float HOME_ARRIVED_DISTANCE = 25f;
            //  Le coffre peut très bien se trouver À L'INTÉRIEUR du même rayon de 25
            // unités que HomePosition (petite maison) : sans cette exclusion, le PNJ
            // reste "considéré chez lui" pendant tout son aller-retour vers le coffre,
            // le minuteur de sommeil continue de tourner en parallèle, et peut se
            // terminer AVANT que le trajet ne soit fini. Le dépôt se faisait alors via
            // le filet de sécurité au moment de l'endormissement plutôt que via le
            // vrai passage au coffre. Tant que ce trajet est en cours, le PNJ n'est
            // pas "arrivé" pour les besoins du sommeil, quelle que soit sa position.
            bool isRunningChestErrand = AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToChestWithdraw;
            bool isInsideHome = HomeBuildingId == TentHomeBuildingId
                ? Vector2.DistanceSquared(WorldPos, HomePosition) < HOME_ARRIVED_DISTANCE * HOME_ARRIVED_DISTANCE
                : IsInsideAssignedHouse();
            bool isHomeOrHomeless = !isRunningChestErrand
                && (!hasHome || isInsideHome);

            bool canRestInTent = HomeBuildingId != TentHomeBuildingId || _tentRestCooldown <= 0f;
            if (isNight && !IsPlayer && !IsTamed && Behavior != "hostile" && isHomeOrHomeless
                && canRestInTent && _sleepWakeCooldown <= 0f)
            {
                if (!_isSleeping)
                {
                    //  CORRECTIF : Raylib.GetFrameTime() renvoie le delta-temps RÉEL, non affecté
                    // par un éventuel ralenti/pause/multiplicateur de vitesse de jeu, alors que
                    // tout le reste de cette fonction (et de l'IA en général) utilise le
                    // paramètre `dt` reçu en argument. Un `dt` mis à l'échelle (pause, time-scale)
                    // désynchronisait alors ce minuteur de tous les autres (rentrer chez soi,
                    // marche jusqu'au coffre, etc.) : le PNJ pouvait par exemple sembler "figé"
                    // en Idle près de son lit bien plus longtemps que SLEEP_TRANSITION_DELAY ne
                    // le laisse penser (ou au contraire s'endormir plus vite que prévu), le
                    // temps affiché/observé ne correspondant plus au reste de la simulation.
                    _sleepTimer += dt;
                    if (_sleepTimer >= SLEEP_TRANSITION_DELAY)
                    {
                        //  BUGFIX : PrepareHatForNightDeposit() n'était appelée QUE comme effet de
                        // bord du trajet "aller déposer les autres objets au coffre" (juste en
                        // dessous). Un villageois qui n'avait rien d'autre à ranger ce soir-là
                        // (World.HasAnyDepositableItem == false) sautait directement à
                        // l'endormissement sans jamais passer par cette fonction : il s'endormait
                        // chapeau compris. Le retrait du chapeau doit être une étape à part entière
                        // du coucher, indépendante du fait qu'il y ait ou non d'autres objets à
                        // déposer — on l'appelle donc ici, une seule fois, dès qu'on s'apprête
                        // vraiment à basculer en sommeil (elle est de toute façon sans effet si le
                        // chapeau est déjà rangé, voir sa propre garde interne).
                        PrepareHatForNightDeposit();

                        //  Le dépôt doit rester un trajet visible, même si la routine n'a pas
                        //  obtenu la main avant l'expiration du délai de sommeil. Le dépôt direct
                        //  reste uniquement le repli si aucune case d'approche praticable n'est
                        //  disponible, afin de ne pas perdre les objets du PNJ.
                        if (!_hasDepositedTonight && HomeBuildingId >= 0 && World.HasAnyDepositableItem(this)
                            && _chestDepositRetryCooldown <= 0f)
                        {
                            var chestApproachPos = World.GetHouseChestApproachPosition(HomeBuildingId, this);
                            if (chestApproachPos.HasValue)
                            {
                                _activeRoutineTask = RoutineTaskKind.DepositItems;
                                AiState = NpcAiState.GoToChestDeposit;
                                AiTarget = chestApproachPos.Value;
                                _currentPath.Clear();
                                _pathRecalcTimer = 0f;
                                _chestArrivalLogged = false;
                                _chestBlockedLogged = false;
                                _chestDepositFullRetryCount = 0;
                                _chestDepositStuckRetryCount = 0;
                                AnimState = "walk";
                                Speed = BaseSpeed;
                                _sleepTimer = 0f;
                                _chestApproachRetryCount = 0;
                                Console.WriteLine($"[NPC-CHEST] SLEEP_FALLBACK npc={NetId} home={HomeBuildingId} pos={WorldPos} target={AiTarget} inventory={GetChestInventoryLog()}");
                                return;
                            }

                            //  FILET DE SÉCURITÉ : si aucun point d'approche praticable n'est
                            // trouvable après plusieurs tentatives (maison sans case libre,
                            // entièrement meublée, etc.), on ne doit PAS laisser le PNJ figé
                            // indéfiniment à réessayer chaque frame sans jamais dormir — c'est
                            // exactement le bug "le PNJ attend juste sur sa case de sommeil".
                            // Au bout d'un nombre de tentatives raisonnable, on renonce pour ce
                            // soir (il gardera ses objets, rien n'est perdu) et on le laisse
                            // s'endormir normalement ; _hasDepositedTonight reste false pour
                            // qu'il retente dès le lendemain soir.
                            _chestApproachRetryCount++;
                            if (_chestApproachRetryCount >= CHEST_APPROACH_MAX_RETRIES)
                            {
                                _chestApproachRetryCount = 0;
                                _isSleeping = true;
                                _sleepVisualPosition = GetSleepVisualPosition();
                                ReleaseCurrentGoalLock();
                                AnimState = _restingInChair ? "sit" : "sleep";
                                _sleepTimer = 0f;
                                return;
                            }

                            _sleepTimer = 0f;
                            return;
                        }

                        _isSleeping = true;
                        _sleepVisualPosition = GetSleepVisualPosition();
                        ReleaseCurrentGoalLock();
                        AnimState = _restingInChair ? "sit" : "sleep";
                        _sleepTimer = 0f;
                    }
                }
                else
                {
                    if (isTentResident)
                    {
                        _tentSleepTimer += dt;
                        if (_tentSleepTimer >= CAVE_TENT_SLEEP_DURATION)
                        {
                            _tentSleepTimer = 0f;
                            _tentRestCooldown = 45f + (float)(Random.Shared.NextDouble() * 120f);
                            ForceWakeFromTent();
                            RestoreHatAfterNight();
                            Decide();
                            return;
                        }
                    }

                    AiState = NpcAiState.Idle;
                    AiTarget = WorldPos;
                    Speed = 0f;
                    AnimState = _restingInChair ? "sit" : "sleep";
                }
            }
            else
            {
                if (_isSleeping)
                {
                    _isSleeping = false;
                    _tentSleepTimer = 0f;
                    _groundResting = false;
                    _sleepVisualPosition = Vector2.Zero;
                    _hasDepositedTonight = false;
                    _morningDeparturePending = true;
                    _sleepTimer = 0f;
                    ReleaseCurrentGoalLock();
                    AnimState = "idle";
                    //  Libérer la chaise réservée pour la nuit (voir GetSleepVisualPosition) : sans
                    // cela, elle resterait marquée occupée pour toujours et ne redeviendrait jamais
                    // disponible pour un autre villageois (de jour comme la nuit suivante).
                    if (_restingInChair)
                    {
                        World.ReleaseBench(_reservedBench);
                        _reservedBench = null;
                        _restingInChair = false;
                    }
                    var speciesInfo = SpeciesData.GetSpeciesInfo(Species);
                    if (speciesInfo != null)
                        Speed = speciesInfo.Speed;
                    if (HomeBuildingId == TentHomeBuildingId)
                        _tentRestCooldown = 90f + (float)(Random.Shared.NextDouble() * 150f);
                    RestoreHatAfterNight();
                    Decide();
                }
                else
                {
                    _sleepTimer = 0f;
                }
            }
        }

        private Vector2 GetSleepVisualPosition()
        {
            _restingInChair = false;
            _groundResting = false;

            if (TentPosition != Vector2.Zero && !Program.IsSleepPositionOccupied(TentPosition, this))
            {
                Program.PrepareTentOccupation(TentPosition, this);
                return TentPosition;
            }

            //  Chercher un lit LIBRE en priorité (comportement d'origine).
            Vector2? sleepPosition = World.FindNearestAvailableSleepPosition(HomeBuildingId, WorldPos, this);

            //  Un lit existe dans la maison mais ils sont tous occupés : mieux vaut prendre le
            // lit "occupé" le plus proche (superposition visuelle mineure, déjà le comportement
            // d'origine via FindNearestSleepPosition) plutôt que de passer directement à la
            // recherche de chaise, réservée au cas où la maison n'a carrément AUCUN lit.
            bool houseHasNoBedAtAll = !sleepPosition.HasValue
                && HomeBuildingId >= 0 && HomeBuildingId != TentHomeBuildingId
                && World.FindNearestSleepPosition(HomeBuildingId, WorldPos) is null;

            if (!sleepPosition.HasValue && !houseHasNoBedAtAll)
            {
                sleepPosition = World.FindNearestSleepPosition(HomeBuildingId, WorldPos);
            }

            if (sleepPosition.HasValue)
            {
                Program.PrepareTentOccupation(sleepPosition.Value, this);
                return sleepPosition.Value;
            }

            //  Pas de lit du tout chez lui : plutôt que de "dormir" debout sur place (ce qui, sans
            // animation dédiée, ressemble à une pause figée), on cherche d'abord une chaise/banc libre
            // de la maison. S'il n'y en a pas non plus, le PNJ peut simplement s'asseoir au sol ou
            // se coucher directement sur le sol sans rester planté au milieu de la pièce.
            if (houseHasNoBedAtAll)
            {
                var seat = World.FindNearestFreeHouseSeat(HomeBuildingId, WorldPos, 800f);
                if (seat.HasValue)
                {
                    _restingInChair = true;
                    _reservedBench = seat.Value.key;
                    return seat.Value.seatPos;
                }
            }

            _groundResting = true;
            return WorldPos;
        }
    }

    //  Diffuse le signal d'alerte aux entités hostiles proches : quand
    // une entité programme l'émission de son signal (voir Entity.TriggerAlertSignal /
    // ReceiveAlertSignal), cette classe se charge, une fois le délai écoulé, de retrouver les
    // entités hostiles proches de la même espèce (rayon Entity.ALERT_SIGNAL_RANGE_TILES).
    public static class AlertSignalManager
    {
        public static void Emit(Entity source, Vector2 targetPos, int targetConnectionId)
        {
            if (!source.IsAlive || source.IsSleeping || source.IsKnockedOut)
                return;

            float rangePixels = Entity.ALERT_SIGNAL_RANGE_TILES * Program.TileSize;
            float rangeSq = rangePixels * rangePixels;

            foreach (var other in Program.GetEntities())
            {
                if (other == source || !other.IsAlive || other.IsSleeping || other.IsKnockedOut) continue;
                if ((other.Behavior != "hostile" && other.Behavior != "neutral") || other.IsTamed) continue;
                if (!string.Equals(other.Species, source.Species, StringComparison.OrdinalIgnoreCase)) continue;

                float distSq = Vector2.DistanceSquared(other.WorldPos, source.WorldPos);
                if (distSq > rangeSq) continue;

                other.ReceiveAlertSignal(targetPos, targetConnectionId);
            }
        }
    }

    //  Zone de danger au sol utilisée par le Génie de la Lampe (phase 2 : tourbillons de sable,
    // phase 3 : flaques de lave). Le cycle est toujours Telegraph (visuel d'avertissement,
    // aucun dégât) -> Active (dégâts au joueur s'il reste dedans) -> expirée (retirée de la liste).
    public class GenieHazardZone
    {
        public Vector2 Pos;
        public float Radius;
        public float TelegraphTimer;   // temps restant avant que la zone devienne active
        public float ActiveTimer;      // temps restant pendant lequel la zone est active
        public bool IsSand;            // true = sable (phase 2, ralentit), false = lave (phase 3, brûle)
        public float DamageTickTimer;  // pour infliger les dégâts périodiquement plutôt qu'en continu

        public bool IsTelegraph => TelegraphTimer > 0f;
        public bool IsExpired => TelegraphTimer <= 0f && ActiveTimer <= 0f;
    }
}
