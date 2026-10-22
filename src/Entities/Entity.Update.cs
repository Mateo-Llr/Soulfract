#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public partial class Entity
    {
        // Main update loop and related update logic
        // ==================== UPDATE ====================
        /// <summary>
        /// Détermine où ce PNJ doit regarder (pupilles + sourcils), selon un ordre de priorité :
        /// 1. En train de parler à quelqu'un (joueur ou autre PNJ) -> regarde cet interlocuteur.
        /// 2. En train de se déplacer -> regarde dans la direction de son trajet.
        /// 3. Sinon (immobile, ne fait rien) -> regarde occasionnellement autour de lui,
        ///    éventuellement vers une entité qui passe à proximité.
        /// </summary>
        private void UpdateGaze(float dt, Vector2 playerPos)
        {
            //  OPTIMISATION PERF : le calcul fin du regard (pupilles/sourcils qui suivent une
            // cible) est purement cosmétique et coûte plusieurs trigonométries par oeil, par PNJ,
            // par frame. Un PNJ très loin du joueur (donc hors écran dans un grand village) n'a
            // pas besoin de cette précision : on garde le regard neutre et on sort tout de suite.
            const float GAZE_DETAIL_RANGE = 900f;
            if (Vector2.DistanceSquared(WorldPos, playerPos) > GAZE_DETAIL_RANGE * GAZE_DETAIL_RANGE)
            {
                PupilLeftOffset = Vector2.Zero;
                PupilRightOffset = Vector2.Zero;
                EyebrowLeftOffset = Vector2.Zero;
                EyebrowRightOffset = Vector2.Zero;
                return;
            }

            Vector2? gazeTarget = null;

            if (IsTalking)
            {
                if (CurrentTalkPartner != null)
                    gazeTarget = Program.GetPlayerVisualPosition(CurrentTalkPartner.WorldPos);
                else
                    gazeTarget = Program.GetPlayerVisualPosition(playerPos); // parle au joueur
            }
            else if (AnimState == "walk" && AiTarget != Vector2.Zero && AiTarget != WorldPos)
            {
                // Regarde un peu en avant sur son chemin
                Vector2 dir = AiTarget - WorldPos;
                if (dir.LengthSquared() > 0.01f)
                {
                    dir = Vector2.Normalize(dir);
                    gazeTarget = WorldPos + dir * 80f;
                }
            }
            else
            {
                // Immobile / inactif : glances aléatoires autour de lui, ou vers une entité qui passe
                _idleLookTimer -= dt;
                if (_idleLookTimer <= 0f)
                {
                    _idleLookTimer = 2.5f + (float)_gazeRandom.NextDouble() * 4.5f; // 2.5 à 7s entre chaque glance

                    //  OPTIMISATION PERF : le scan "qui passe à proximité ?" coûte O(nombre
                    // d'entités) et n'a d'intérêt visuel que pour les PNJ que le joueur regarde
                    // réellement. Loin de la caméra/joueur, on se contente d'un regard aléatoire
                    // (beaucoup moins cher) plutôt que de scanner toutes les entités du monde —
                    // utile dès qu'un village compte plusieurs dizaines d'habitants.
                    const float PASSERBY_SCAN_RANGE = 500f;
                    Entity? passerby = null;
                    if (Vector2.DistanceSquared(WorldPos, playerPos) < PASSERBY_SCAN_RANGE * PASSERBY_SCAN_RANGE)
                    {
                        float bestDistSq = 220f * 220f;
                        foreach (var other in Program.entities)
                        {
                            if (ReferenceEquals(other, this) || !other.IsAlive) continue;
                            float dSq = Vector2.DistanceSquared(WorldPos, other.WorldPos);
                            if (dSq < bestDistSq)
                            {
                                bestDistSq = dSq;
                                passerby = other;
                            }
                        }
                    }

                    if (passerby != null && _gazeRandom.NextDouble() < 0.6)
                    {
                        _idleLookTarget = passerby.WorldPos;
                    }
                    else
                    {
                        float angle = (float)(_gazeRandom.NextDouble() * Math.PI * 2.0);
                        _idleLookTarget = WorldPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 60f;
                    }
                }
                gazeTarget = _idleLookTarget;
            }

            if (!gazeTarget.HasValue)
            {
                PupilLeftOffset = Vector2.Zero;
                PupilRightOffset = Vector2.Zero;
                EyebrowLeftOffset = Vector2.Zero;
                EyebrowRightOffset = Vector2.Zero;
                return;
            }

            Vector2 headWorldPos = WorldPos;
            Vector2 leftEyeWorldPos = headWorldPos + new Vector2(-Program.EYE_LOCAL_OFFSET_X * Facing, Program.EYE_LOCAL_OFFSET_Y);
            Vector2 rightEyeWorldPos = headWorldPos + new Vector2(Program.EYE_LOCAL_OFFSET_X * Facing, Program.EYE_LOCAL_OFFSET_Y);

            PupilLeftOffset = Program.ComputeGazeOffset(leftEyeWorldPos, gazeTarget.Value, Program.PUPIL_MAX_OFFSET_X, Program.PUPIL_MAX_OFFSET_Y);
            PupilRightOffset = Program.ComputeGazeOffset(rightEyeWorldPos, gazeTarget.Value, Program.PUPIL_MAX_OFFSET_X, Program.PUPIL_MAX_OFFSET_Y);

            float browMaxY = Program.PUPIL_MAX_OFFSET_Y * Program.EYEBROW_Y_INTENSITY_FACTOR;
            EyebrowLeftOffset = new Vector2(0f, Program.ComputeGazeOffset(leftEyeWorldPos, gazeTarget.Value, 0f, browMaxY).Y);
            EyebrowRightOffset = new Vector2(0f, Program.ComputeGazeOffset(rightEyeWorldPos, gazeTarget.Value, 0f, browMaxY).Y);
        }

        public bool ShouldRunFullAiUpdate(Vector2 playerPos, int updateIndex, int framePhase)
        {
            if (IsPlayer || IsPetCompanion || IsBoss)
                return true;

            if (IsTalking || IsAlerted || ShowAlertIcon || CurrentTalkPartner != null)
                return true;

            if (AiState == NpcAiState.Chase || AiState == NpcAiState.Flee || AiState == NpcAiState.Follow ||
                AiState == NpcAiState.GoHome || AiState == NpcAiState.GoToInfirmary ||
                AiState == NpcAiState.HealingAtInfirmary || AiState == NpcAiState.GoToChestWithdraw ||
                AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToMerchant ||
                AiState == NpcAiState.GoToGroundItem || AiState == NpcAiState.GoToVillageCenter)
            {
                return true;
            }

            if (_currentGoalPriority >= NpcPriority.Combat)
                return true;

            float distSq = Vector2.DistanceSquared(WorldPos, playerPos);
            //  CORRECTIF : 700px (~17 tuiles) ne couvrait qu'une fraction de la zone réellement
            // affichée à l'écran (jusqu'à MAX_RENDER_TILES_X = 120 tuiles de large côté rendu,
            // voir World_Core.cs). Des PNJ parfaitement visibles, mais au-delà de 700px du
            // joueur, tombaient donc dans le mode dégradé (1 update sur 4) et paraissaient figés
            // - le simple fait de s'approcher les "débloquait" en les faisant repasser sous le
            // seuil. 2200px (~55 tuiles) couvre confortablement la zone visible dans les
            // résolutions/zooms courants tout en gardant l'optimisation utile pour les PNJ
            // vraiment hors champ (fond de village, zones adjacentes non regardées).
            const float NEARBY_NPC_AI_DISTANCE_SQ = 2200f * 2200f;
            if (distSq <= NEARBY_NPC_AI_DISTANCE_SQ)
                return true;

            //  Budget important : les PNJ lointains et inactifs ne recalculent pas tout leur AI
            // chaque frame. On laisse le moteur se concentrer sur les PNJ proches du joueur ou
            // déjà engagés dans une action urgente, ce qui coupe fortement les pics de coût quand
            // le village est rempli de dizaines d'habitants.
            return ((updateIndex + framePhase) & 3) == 0;
        }

        public void Update(float dt, HashSet<string> destroyed, Vector2 playerPos, int playerHeldItemId)
        {
            _debugPlannedPriorities.Clear();

            //  Filet de sécurité : si la monture est morte/a disparu pendant qu'on la chevauchait
            // (ex : tuée par le joueur sous son cavalier), on remet le PNJ à pied immédiatement
            // plutôt que de le laisser courir à la vitesse d'une monture qui n'existe plus.
            if (IsRidingMount && (OwnedMount == null || !OwnedMount.IsAlive))
                DismountOwnedMount();

            if (Species == "human" || Species == "goblin")
                UpdateGaze(dt, playerPos);

            //  Vol des oiseaux : indépendant de l'état d'IA courant (Flee, Wander, Chase...),
            // pour qu'un oiseau parti en l'air continue son vol/atterrissage même s'il change
            // d'état entre-temps (ex : il finit de fuir puis repart en Wander avant d'atterrir).
            if (IsBird)
                UpdateBirdFlight(dt, destroyed);

            if (DamageFlash > 0f)
            {
                DamageFlash -= dt;
                if (DamageFlash < 0f) DamageFlash = 0f;
            }

            //  POISON : tic régulier tant que l'entité est vivante et empoisonnée (voir
            // ApplyPoison, déclenché entre autres par les nuages de spores du bolet).
            if (_poisonTimer > 0f && IsAlive)
            {
                _poisonTimer = Math.Max(0f, _poisonTimer - dt);
                _poisonTickAccumulator += dt;
                if (_poisonTickAccumulator >= Program.POISON_TICK_INTERVAL)
                {
                    int ticks = (int)(_poisonTickAccumulator / Program.POISON_TICK_INTERVAL);
                    _poisonTickAccumulator -= ticks * Program.POISON_TICK_INTERVAL;
                    for (int i = 0; i < ticks && IsAlive; i++)
                    {
                        int poisonDamage = (int)Program.GetPoisonDamagePerTick();
                        CurrentHP -= poisonDamage;
                        OnHit();
                        Program.AddFloatingDamage(
                            World.GetEntityGroundPosition(WorldPos) - new Vector2(0f, 20f),
                            poisonDamage, false, isEnemy: true);
                    }
                }
                if (_poisonTimer <= 0f)
                {
                    _poisonTimer = 0f;
                    _poisonTickAccumulator = 0f;
                }
            }

            if (Concussion > 0f)
            {
                if (!IsKnockedOut && IsAlive && Concussion >= CurrentHP)
                    KnockOut();

                Concussion = Math.Max(0f, Concussion - dt * (IsKnockedOut ? 0.5f : 0.25f));
                if (IsKnockedOut)
                {
                    ResetMoveMomentum();
                    _currentPath.Clear();
                    AiState = NpcAiState.Idle;
                    AiTarget = WorldPos;
                    AnimState = "stunned";
                    if (Concussion <= 0f)
                    {
                        IsKnockedOut = false;
                        AnimState = "idle";
                    }
                    else
                    {
                        return;
                    }
                }
            }

            UpdateBlink(dt);
            UpdateAlertSignal(dt, playerPos);

            if (_infirmaryRetryTimer > 0f)
                _infirmaryRetryTimer = Math.Max(0f, _infirmaryRetryTimer - dt);
            if (IsVillager && !IsTamed && IsAlive && CurrentHP < MaxHP
                && _infirmaryBuildingId < 0 && _infirmaryRetryTimer <= 0f)
                TryStartInfirmaryCare();

            if (!IsPlayer && IsAlive)
            {
                if (string.Equals(Species, "Pig", StringComparison.OrdinalIgnoreCase))
                {
                    _poopDropTimer -= dt;
                    if (_poopDropTimer <= 0f)
                    {
                        _poopDropTimer = POOP_DROP_INTERVAL + (float)(Random.Shared.NextDouble() * 4f);
                        if (Random.Shared.NextDouble() < POOP_DROP_CHANCE)
                        {
                            Vector2 dropPos = new Vector2(WorldPos.X, WorldPos.Y + 12f);
                            Program.DropItemOnGround(dropPos, GameData.GetItemId("poo"), 1);
                        }
                    }
                }
                else if (string.Equals(Species, "Chicken", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Species, "Dodo", StringComparison.OrdinalIgnoreCase))
                {
                    _eggDropTimer -= dt;
                    if (_eggDropTimer <= 0f)
                    {
                        _eggDropTimer = EGG_DROP_INTERVAL + (float)(Random.Shared.NextDouble() * 4f);
                        if (Random.Shared.NextDouble() < EGG_DROP_CHANCE)
                        {
                            Vector2 dropPos = new Vector2(WorldPos.X, WorldPos.Y + 12f);
                            string eggId = string.Equals(Species, "Dodo", StringComparison.OrdinalIgnoreCase)
                                ? "dodo_egg"
                                : "egg";
                            Program.DropItemOnGround(dropPos, GameData.GetItemId(eggId), 1);
                        }
                    }
                }
            }

            //  Position du propriétaire AVANT que playerPos ne soit éventuellement remplacée par
            // celle d'une cible de combat (utilisée par les ordres des animaux apprivoisés).
            Vector2 petOwnerPos = playerPos;
            bool isPlayerOwnedPet = IsTamed && OwnerNpcId == null && !IsHostilePet && !IsPetCompanion;

            //  CORRECTIF : un apprivoisé n'a le droit de riposter/poursuivre QUE dans son mode
            // Attack. Sinon une riposte armée avant un ordre Follow/Stay/Roam le laissait
            // continuer la poursuite (ordre ignoré).
            if (IsTamed && TamedBehavior != TamedAnimalMode.Attack && _retaliationTimer > 0f)
            {
                _retaliationTimer = 0f;
                _retaliationTargetEntity = null;
                _retaliateAgainstPlayer = false;
                _fearTargetConnectionId = -1;
            }
            if (isPlayerOwnedPet && _retaliateAgainstPlayer)
            {
                _retaliationTimer = 0f;
                _retaliateAgainstPlayer = false;
                _fearTargetConnectionId = -1;
            }

            if (_retaliationTimer > 0f)
                _retaliationTimer = Math.Max(0f, _retaliationTimer - dt);

            bool isRetaliating = _retaliationTimer > 0f;
            _combatTargetEntity = null;
            if (isRetaliating && _retaliateAgainstPlayer)
            {
                playerPos = GetFearTargetPosition();
                AiTargetConnectionId = _fearTargetConnectionId;
                if (Vector2.DistanceSquared(WorldPos, playerPos) > RETALIATION_RANGE * RETALIATION_RANGE)
                {
                    _retaliationTimer = 0f;
                    _retaliateAgainstPlayer = false;
                    _fearTargetConnectionId = -1;
                    isRetaliating = false;
                }
                else
                {
                    _retaliationTimer = RETALIATION_DURATION;
                }
            }
            else if (isRetaliating && _retaliationTargetEntity != null && _retaliationTargetEntity.IsAlive
                && Vector2.DistanceSquared(WorldPos, _retaliationTargetEntity.WorldPos) <= RETALIATION_RANGE * RETALIATION_RANGE)
            {
                _combatTargetEntity = _retaliationTargetEntity;
                _retaliationTimer = RETALIATION_DURATION;
            }
            else if (isRetaliating)
            {
                _retaliationTimer = 0f;
                _retaliationTargetEntity = null;
                _retaliateAgainstPlayer = false;
                isRetaliating = false;
            }

            //  CORRECTIF : mode Attack d'un animal du joueur. Avant, RIEN ne lui choisissait de
            // cible : shouldChase devenait vrai dès que le joueur était dans sa vision, et comme
            // _combatTargetEntity restait null, ChaseBehavior/MeleeCombat visaient... le joueur.
            bool petAttackActive = isPlayerOwnedPet && TamedBehavior == TamedAnimalMode.Attack;
            if (petAttackActive && _combatTargetEntity == null)
            {
                _combatTargetEntity = FindPlayerPetTarget(petOwnerPos, dt);
                if (_combatTargetEntity != null)
                    playerPos = _combatTargetEntity.WorldPos;
            }

            bool isGoblin = string.Equals(Species, "goblin", StringComparison.OrdinalIgnoreCase) && !IsTamed;
            if (!isRetaliating && (IsVillageGuard || IsSpiritAnimal || isGoblin))
            {
                float targetRange = IsVillageGuard || IsSpiritAnimal ? 800f : 500f;
                float nearestTargetDistanceSq = targetRange * targetRange;
                Vector2 spiritTotemPosition = new((SpiritTotemTileX + 0.5f) * Program.TileSize, (SpiritTotemTileY + 0.5f) * Program.TileSize);
                bool IsVillageThreat(Entity target) => target.Behavior == "hostile"
                    && !string.Equals(target.Species, "human", StringComparison.OrdinalIgnoreCase)
                    && target.CombatTargetEntity?.IsVillager == true
                    && (!IsSpiritAnimal || (SpiritTotemTileX != int.MinValue && SpiritTotemTileY != int.MinValue
                        && Vector2.DistanceSquared(spiritTotemPosition, target.WorldPos) <= targetRange * targetRange));

                if (_hostileTargetEntity != null)
                {
                    Entity lockedTarget = _hostileTargetEntity;
                    bool isValidLockedTarget = lockedTarget.IsAlive && !lockedTarget.IsTamed && !lockedTarget.IsPlayer
                        && (IsVillageGuard || IsSpiritAnimal
                            ? IsVillageThreat(lockedTarget)
                            : lockedTarget.IsVillager);
                    float lockedTargetDistanceSq = Vector2.DistanceSquared(WorldPos, lockedTarget.WorldPos);

                    if (isValidLockedTarget && lockedTargetDistanceSq <= nearestTargetDistanceSq)
                    {
                        _combatTargetEntity = lockedTarget;
                        nearestTargetDistanceSq = lockedTargetDistanceSq;
                    }
                    else
                    {
                        _hostileTargetEntity = null;
                    }
                }

                if (_combatTargetEntity == null)
                {
                    foreach (var candidate in Program.GetEntities())
                    {
                        if (ReferenceEquals(candidate, this) || !candidate.IsAlive || candidate.IsTamed || candidate.IsPlayer)
                            continue;

                        bool isValidTarget = IsVillageGuard || IsSpiritAnimal
                            ? IsVillageThreat(candidate)
                            : candidate.IsVillager;
                        if (!isValidTarget) continue;

                        float candidateDistanceSq = Vector2.DistanceSquared(WorldPos, candidate.WorldPos);
                        if (candidateDistanceSq >= nearestTargetDistanceSq) continue;

                        nearestTargetDistanceSq = candidateDistanceSq;
                        _combatTargetEntity = candidate;
                        _hostileTargetEntity = candidate;
                    }
                }

                if (_combatTargetEntity != null)
                    playerPos = _combatTargetEntity.WorldPos;
            }

            float distToPlayerSq = Vector2.DistanceSquared(WorldPos, playerPos);

            if (IsBoss && !IsPlayer && !IsTamed)
            {
                Behavior = "hostile";
                if (VisionRange <= 0)
                    VisionRange = 3;
                if (AlertDuration <= 0f)
                    AlertDuration = 2f;
            }

            float darkness = Program.GetDarknessAlpha();
            UpdateSleepState(darkness, dt);

            // Gestion du sommeil
            if (_isSleeping)
            {
                ClearAlertSignal();
                ResetMoveMomentum();
                const float SLEEP_ANIM_SPEED = 0.5f;
                AnimProgress += dt * SLEEP_ANIM_SPEED;
                if (AnimProgress >= 1f)
                {
                    AnimProgress = 0f;
                    int frameCount = 8;
                    if (SpeciesData.Skeletons.TryGetValue(Species, out var skeleton))
                    {
                        var firstPart = skeleton.FirstOrDefault();
                        if (firstPart != null && firstPart.Animations.TryGetValue("sleep", out var frames) && frames.Count > 0)
                            frameCount = frames.Count;
                    }
                    CurrentFrame = (CurrentFrame + 1) % frameCount;
                }

                _zzzTimer += dt;
                if (_zzzTimer >= _zzzCurrentInterval)
                {
                    _zzzTimer = 0f;
                    SpawnZzzParticle();
                }
                return;
            }

            // Dans une grotte, un humain sans maison peut s'attribuer une tente proche.
            // Les anciens PNJ générés avant cette règle peuvent aussi être réparés ici.
            if (!IsPlayer && Species == "human" && !IsTamed && World.IsUnderground
                && (HomeBuildingId != TentHomeBuildingId || TentPosition == Vector2.Zero || HomePosition == Vector2.Zero)
                && (HomeBuildingId < 0 || !World.Houses.ContainsKey(HomeBuildingId)))
            {
                Vector2? tentHome = World.FindNearestTentHome(WorldPos, 12);
                Vector2? tentSleepPosition = World.FindNearestTentSleepPosition(WorldPos, 12);
                if (tentHome.HasValue)
                {
                    HomePosition = tentHome.Value;
                    TentPosition = tentSleepPosition ?? tentHome.Value;
                    HomeBuildingId = TentHomeBuildingId;
                    AiState = NpcAiState.Idle;
                    AiTarget = WorldPos;
                    _currentPath.Clear();
                    _pathRecalcTimer = 0f;
                }
                else
                {
                    // Une ancienne position de maison ne doit pas rendre le PNJ prisonnier
                    // d'un trajet impossible lorsqu'il n'existe aucun abri souterrain proche.
                    HomePosition = Vector2.Zero;
                }
            }

            bool hasHomeAssignment = HomeBuildingId >= 0
                && World.Houses.ContainsKey(HomeBuildingId)
                && World.HouseInteriorSpots.TryGetValue(HomeBuildingId, out var assignedSpots)
                && assignedSpots.Any(spot => Vector2.DistanceSquared(spot.WorldPos, HomePosition) < 16f);

            // Une maison pleine n'est pas une maison invalide pour le PNJ déjà logé dedans.
            // La saturation ne doit empêcher QUE l'attribution d'un nouveau PNJ, pas forcer
            // le relogement du PNJ actuel. On revalide seulement la case assignée / la maison
            // elle-même, pas la capacité globale de l'édifice.
            if (!hasHomeAssignment || HomePosition == Vector2.Zero)
            {
                if (HomeBuildingId >= 0 && World.Houses.ContainsKey(HomeBuildingId) && HomePosition != Vector2.Zero)
                {
                    HomePosition = Vector2.Zero;
                }
                else
                {
                    HomePosition = Vector2.Zero;
                    HomeBuildingId = -1;
                }
            }

            // Si un humain de surface sans foyer trouve une maison libre, il décide d'y habiter.
            if (!IsPlayer && Species == "human" && !IsTamed && !World.IsUnderground && HomePosition == Vector2.Zero)
            {
                _homeSearchTimer -= dt;
                if (_homeSearchTimer <= 0f)
                {
                    _homeSearchTimer = 4f + (float)(Random.Shared.NextDouble() * 6f);
                    var foundHome = World.FindNearestFreeHouseSpot(WorldPos, 2400f);
                    if (foundHome.HasValue)
                    {
                        HomePosition = foundHome.Value.spotPos;
                        HomeBuildingId = foundHome.Value.buildingId;
                        AiState = NpcAiState.GoHome;
                        AiTarget = GetHomeEntryTarget();
                        _currentPath.Clear();
                        _pathRecalcTimer = 0f;
                        AnimState = "walk";
                        Speed = BaseSpeed;
                    }
                }
            }

            // Knockback
            if (IsKnockedBack && KnockbackVelocity.LengthSquared() > 0.01f)
            {
                Vector2 move = KnockbackVelocity * dt;
                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                    WorldPos.X += move.X;
                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                    WorldPos.Y += move.Y;
                KnockbackVelocity *= MathF.Pow(0.92f, dt * 60f);
                if (KnockbackVelocity.LengthSquared() < 5f)
                {
                    IsKnockedBack = false;
                    KnockbackVelocity = Vector2.Zero;
                }

                float kbAnimSpeed = 8f;
                AnimProgress += dt * kbAnimSpeed;
                if (AnimProgress >= 1f)
                {
                    AnimProgress = 0f;
                    CurrentFrame = (CurrentFrame + 1) % 8;
                }
                // Allow the AI and other update logic to continue while knocked back.
                // Previously the method returned here which could permanently stun
                // an entity under rapid repeated hits. Removing the return lets
                // the entity resume behaviors (movement/attacks/targets) even
                // while knockback velocities are applied.
            }

            //  COMPAGNON D'ARMURE : IA totalement séparée (suit le joueur / attaque
            // l'agresseur signalé par Program.NotifyPlayerAttackedByEntity), on ne passe
            // donc jamais par le reste de la logique villageois/hostile/apprivoisé ci-dessous.
            if (IsPetCompanion)
            {
                UpdatePetCompanionAI(dt, destroyed, playerPos);
                return;
            }

            // Croissance des bébés
            if (IsBaby && !_isSleeping)
            {
                Age += dt;
                if (Age >= GrowthTime)
                {
                    IsBaby = false;
                    Scale = 1.0f;
                    var speciesInfo = SpeciesData.GetSpeciesInfo(Species);
                    if (speciesInfo != null)
                    {
                        MaxHP = speciesInfo.MaxHp;
                        CurrentHP = MaxHP;
                        Attack = speciesInfo.Attack;
                        Speed = speciesInfo.Speed;
                        BaseSpeed = speciesInfo.Speed;
                        RunSpeed = speciesInfo.RunSpeed;
                    }
                }
            }

            if (IsTalking)
            {
                TalkTimer -= dt;
                if (TalkTimer <= 0f)
                {
                    IsTalking = false;
                    TalkTimer = 0f;
                    TalkText = null;
                    if (CurrentTalkPartner != null && CurrentTalkPartner.CurrentTalkPartner == this)
                    {
                        CurrentTalkPartner.IsTalking = false;
                        CurrentTalkPartner.TalkTimer = 0f;
                        CurrentTalkPartner.TalkText = null;
                        CurrentTalkPartner.CurrentTalkPartner = null;
                    }
                    CurrentTalkPartner = null;
                }
            }
            else
            {
                TalkCooldown -= dt;
                //  IMPORTANT : le "TryCommitGoal(NpcPriority.Social)" ci-dessous est ce qui empêche
                // cette envie spontanée de discuter d'écraser un objectif plus important déjà en
                // cours (ex : un PNJ invité à jouer une partie de Pouilleux, qui détient la
                // priorité Play, largement au-dessus de Social) — avant ce correctif, ce bloc
                // modifiait AiState sans aucune vérification et pouvait donc faire décrocher un
                // PNJ en plein milieu d'une invitation, même juste après avoir cliqué dessus.
                if (TalkCooldown <= 0f && !IsTamed && Species == "human" && !IsPlayer && HomePosition != Vector2.Zero
                    && AiState != NpcAiState.GoToChestDeposit && AiState != NpcAiState.GoToChestWithdraw)
                {
                    const float talkDistance = 70f;
                    Entity? closestTalkTarget = null;
                    float closestDistSq = talkDistance * talkDistance;

                    foreach (var other in Program.entities)
                    {
                        if (!ReferenceEquals(other, this) && other.Species == "human" && !other.IsPlayer && !other.IsTamed &&
                            !other.IsTalking && other.HomePosition != Vector2.Zero &&
                            other.CurrentTalkPartner == null &&
                            other.CurrentGoalPriority < NpcPriority.Social) // ne pas déranger un PNJ déjà occupé par plus important (jouer, rentrer, etc.)
                        {
                            float distSq = Vector2.DistanceSquared(WorldPos, other.WorldPos);
                            if (distSq < closestDistSq)
                            {
                                closestDistSq = distSq;
                                closestTalkTarget = other;
                            }
                        }
                    }

                    if (closestTalkTarget != null)
                    {
                        if (TryCommitGoal(NpcPriority.Social))
                        {
                            float dist = Vector2.Distance(WorldPos, closestTalkTarget.WorldPos);
                            if (dist <= talkDistance)
                            {
                                StartTalking(closestTalkTarget);
                            }
                            else
                            {
                                AiState = NpcAiState.WalkToTarget;
                                AiTarget = closestTalkTarget.WorldPos;
                                _currentPath.Clear();
                                _pathRecalcTimer = 0f;
                                AnimState = "walk";
                                Speed = BaseSpeed;
                            }
                        }
                    }
                    else if (Random.Shared.NextDouble() < 0.002f && TryCommitGoal(NpcPriority.Social))
                    {
                        StartTalking();
                    }
                }
            }

            // Comportement des villageois
            if (!IsPlayer && Species == "human" && !IsTamed)
            {
                bool hasHome = HomePosition != Vector2.Zero;
                bool isNight = IsCaveTentResident ? _tentRestCooldown <= 0f : darkness > 0.6f;
                float distToHome = hasHome ? Vector2.Distance(WorldPos, HomePosition) : float.MaxValue;
                bool isTentHome = HomeBuildingId == TentHomeBuildingId;
                bool isAtHome = hasHome && (isTentHome
                    ? distToHome < 25f
                    : IsInsideAssignedHouse());

                // Un item peut être ajouté à l'inventaire après le passage initial au coffre
                // (par exemple quand le joueur donne des objets au PNJ pendant la journée).
                // Dans ce cas la tâche du soir doit redevenir active immédiatement.
                if (_hasDepositedTonight && World.HasAnyDepositableItem(this) && _chestDepositRetryCooldown <= 0f)
                    _hasDepositedTonight = false;

                //  Une tente sert de lieu de repos périodique, pas de prison nocturne : après
                // s'être reposé, le PNJ peut explorer la grotte et parler aux autres membres
                // du camp jusqu'à sa prochaine fenêtre de sommeil.
                if (isTentHome && isNight && _tentRestCooldown <= 0f && !isAtHome)
                {
                    if (AiState != NpcAiState.GoHome)
                    {
                        ForceGoal(NpcPriority.GoHome);
                        AiState = NpcAiState.GoHome;
                        _currentPath.Clear();
                        _pathRecalcTimer = 0f;
                        AnimState = "walk";
                        Speed = BaseSpeed;
                    }
                    AiTarget = GetHomeEntryTarget();
                }

                {
                    //  RAMASSAGE PASSIF : indépendant du système de priorités (voir commentaire sur
                    // _passivePickupScanTimer). Se déclenche systématiquement, peu importe l'AiState
                    // en cours (discussion, routine, trajet actif...), ce qui garantit qu'un PNJ ne
                    // reste jamais planté à côté d'un objet ramassable sans le prendre.
                    _passivePickupScanTimer -= dt;
                    if (_passivePickupScanTimer <= 0f)
                    {
                        _passivePickupScanTimer = 0.25f;
                        Program.TryNpcAttractNearby(this, GroundItem.DefaultPickupRadius);
                        Program.TryNpcPassivePickupNearby(this, 40f);
                    }

                    //  SYSTÈME DE FAIM : monte lentement avec le temps (~11 min pour atteindre le
                    // seuil avec les valeurs par défaut). Uniquement pour les villageois non apprivoisés
                    // (les compagnons du joueur ne gèrent pas leur propre faim ici).
                    if (IsStoryGuide)
                        Hunger = 0f; // il vit de ses provisions : pas de voyage jusqu'à un marchand lointain
                    else
                        Hunger = Math.Min(HUNGER_MAX, Hunger + dt * 0.15f);

                    if (IsStoryGuide)
                        UpdateStoryCampChest(dt);

                    //  Priorité Food (50) : passe devant la routine (40) mais cède face à une discussion
                    // en cours (Social, 70) ou tout ce qui est plus urgent (combat, fuite...).
                    if (Hunger >= HUNGER_THRESHOLD_SEEK_FOOD
                        && AiState != NpcAiState.GoToChestWithdraw && AiState != NpcAiState.GoToChestDeposit && AiState != NpcAiState.GoToMerchant
                        && TryCommitGoal(NpcPriority.Food))
                    {
                        bool foodGoalActive = false;
                        var carriedFood = Inventory.Slots.FirstOrDefault(s => !s.IsEmpty && s.Item != null
                            && GameData.ItemDatabase.TryGetValue(GameData.GetItemId(s.Item.Name), out var d) && d.Type == ItemType.Food);
                        if (carriedFood != null)
                        {
                            Console.WriteLine($"[NPC-CHEST] EAT npc={NetId} item={carriedFood.Item!.Name} countBefore={carriedFood.Count}");
                            Hunger = 0f;
                            carriedFood.Count -= 1;
                            if (carriedFood.Count <= 0) { carriedFood.Item = null; carriedFood.Count = 0; }
                        }
                        else if (HomeBuildingId >= 0 && World.HouseChestHasFood(HomeBuildingId))
                        {
                            var chestApproachPos = World.GetHouseChestApproachPosition(HomeBuildingId, this);
                            if (chestApproachPos.HasValue)
                            {
                                _farmerSupplyWithdrawPending = false;
                                AiState = NpcAiState.GoToChestWithdraw;
                                AiTarget = chestApproachPos.Value;
                                _currentPath.Clear();
                                AnimState = "walk";
                                Speed = BaseSpeed;
                                foodGoalActive = true;
                            }
                        }
                        else if (darkness <= 0.6f)
                        {
                            var merchant = World.FindNearestFoodMerchant(WorldPos, 2000f);
                            if (merchant != null)
                            {
                                AiState = NpcAiState.GoToMerchant;
                                AiTarget = merchant.WorldPos;
                                _targetMerchantNetId = merchant.NetId;
                                _currentPath.Clear();
                                AnimState = "walk";
                                Speed = BaseSpeed;
                                foodGoalActive = true;
                            }
                        }
                        if (!foodGoalActive)
                        {
                            _debugPlannedPriorities.Remove(NpcPriority.Food);
                            _currentGoalPriority = NpcPriority.Idle;
                            _goalLockTimer = 0f;
                        }
                    }

                    //  SYSTÈME DE RAMASSAGE : scan périodique des objets au sol à proximité.
                    _forageScanTimer -= dt;
                    if (_forageScanTimer <= 0f)
                    {
                        _forageScanTimer = 1.5f + (float)Random.Shared.NextDouble();
                        if (AiState != NpcAiState.GoToGroundItem && AiState != NpcAiState.GoToChestDeposit)
                        {
                            //  IMPORTANT : on cherche l'objet AVANT de revendiquer la priorité.
                            // Revendiquer Forage (qui doit rester au-dessus de Routine pour
                            // pouvoir l'interrompre quand il y a vraiment quelque chose à
                            // ramasser) alors qu'aucun objet n'est trouvé laissait
                            // _currentGoalPriority bloqué sur Forage sans aucun objectif actif :
                            // Routine ne pouvait plus jamais reprendre la main (40 < 45), et le
                            // PNJ restait Idle en permanence sans pouvoir aller au coffre.
                            var groundItem = Program.FindNearestGroundItemForNpc(WorldPos, 350f, this);
                            if (groundItem != null && TryCommitGoal(NpcPriority.Forage))
                            {
                                AiState = NpcAiState.GoToGroundItem;
                                AiTarget = groundItem.Position;
                                _targetGroundItemNetId = groundItem.NetId;
                                _currentPath.Clear();
                                AnimState = "walk";
                                Speed = BaseSpeed;
                            }
                        }
                    }

                    if (AiState != NpcAiState.GoToChestDeposit && AiState != NpcAiState.GoToChestWithdraw
                        && CurrentTalkPartner != null && CurrentTalkPartner.IsAlive && TryCommitGoal(NpcPriority.Social))
                    {
                        if (!IsTalking)
                        {
                            float distToTalkPartner = Vector2.Distance(WorldPos, CurrentTalkPartner.WorldPos);
                            if (distToTalkPartner > 55f)
                            {
                                if (AiState != NpcAiState.WalkToTarget)
                                {
                                    AiState = NpcAiState.WalkToTarget;
                                    _currentPath.Clear();
                                    _pathRecalcTimer = 0f;
                                    AnimState = "walk";
                                    Speed = BaseSpeed;
                                }
                                AiTarget = CurrentTalkPartner.WorldPos;
                            }
                            else
                            {
                                StartTalking(CurrentTalkPartner);
                            }
                        }
                    }
                    else if (hasHome && CurrentTalkPartner == null && TryCommitGoal(NpcPriority.Routine))
                    {
                        float distToHomeInRoutine = Vector2.Distance(WorldPos, HomePosition);
                        bool isRoutineAtHome = isTentHome
                            ? distToHomeInRoutine < 25f
                            : isAtHome;

                        if (isRoutineAtHome)
                        {
                            if (!_hasDepositedTonight && HomeBuildingId >= 0 && AiState != NpcAiState.GoToChestDeposit
                                && _chestDepositRetryCooldown <= 0f)
                            {
                                if (World.HasAnyDepositableItem(this))
                                {
                                    QueueRoutineTask(RoutineTaskKind.DepositItems);
                                    TryStartQueuedRoutineTask();
                                }
                                else
                                {
                                    _hasDepositedTonight = true;
                                }
                            }

                            if (AiState != NpcAiState.Idle && AiState != NpcAiState.GoHome
                                && AiState != NpcAiState.GoToGroundItem && AiState != NpcAiState.GoToChestWithdraw
                                && AiState != NpcAiState.GoToChestDeposit && AiState != NpcAiState.GoToMerchant
                                && AiState != NpcAiState.WalkToTarget && AiState != NpcAiState.Sitting)
                            {
                                AiState = NpcAiState.Idle;
                                AiTarget = WorldPos;
                                _currentPath.Clear();
                            }
                            else if (AiState == NpcAiState.Idle && AiTimer <= 0f && !_isSleeping)
                            {
                                if (!isNight)
                                {
                                    Decide();
                                    return;
                                }

                                AiTimer = (float)(Random.Shared.NextDouble() * 10f + 3f);

                                if (HomeBuildingId >= 0 && Random.Shared.NextDouble() < 0.35)
                                {
                                    var seat = World.FindNearestFreeHouseSeat(HomeBuildingId, WorldPos, 200f);
                                    if (seat.HasValue)
                                    {
                                        _reservedBench = seat.Value.key;
                                        _benchSeatVisualPos = seat.Value.seatPos;
                                        _walkingToBench = true;
                                        AiState = NpcAiState.WalkToTarget;
                                        AiTarget = seat.Value.approachPos;
                                        AiTimer = 20f;
                                        AnimState = "walk";
                                        Speed = BaseSpeed;
                                        _currentPath.Clear();
                                        _pathRecalcTimer = PATH_RECALC_INTERVAL;
                                        return;
                                    }
                                }

                                if (HomeBuildingId >= 0 && Random.Shared.NextDouble() < 0.7)
                                {
                                    var spot = GetHomeInteriorTarget(moveAwayFromCurrentPosition: true);
                                    if (spot != Vector2.Zero)
                                    {
                                        AiState = NpcAiState.WalkToTarget;
                                        AiTarget = spot;
                                        AnimState = "walk";
                                        Speed = BaseSpeed;
                                        _currentPath.Clear();
                                        _pathRecalcTimer = PATH_RECALC_INTERVAL;
                                        return;
                                    }
                                }
                            }
                        }
                    }

                    if (hasHome)
                    {
                        float distToHomeForCheck = Vector2.Distance(WorldPos, HomePosition);
                        bool isAwayFromHome = isTentHome
                            ? distToHomeForCheck >= 25f
                            : !isAtHome;

                        //  CORRECTIF : le jour peut se lever pendant qu'un villageois est encore
                        // en train de rentrer (AiState == GoHome) sans être arrivé. Ce cas n'était
                        // traité auparavant que dans un bloc mort, imbriqué sous
                        // TryCommitGoal(NpcPriority.Routine) : comme GoHome (75) est une priorité
                        // supérieure à Routine (40), ce TryCommitGoal échouait systématiquement
                        // tant que AiState == GoHome, donc ce code n'était en réalité JAMAIS
                        // exécuté. Un villageois déjà tout près de chez lui quand le jour se lève
                        // continuait donc bêtement jusqu'à sa porte avant de pouvoir reprendre une
                        // activité de jour, au lieu de rebrousser chemin tout de suite. On place
                        // ici le même contrôle, au bon endroit (hors de toute condition sur
                        // Routine), pour qu'il s'exécute réellement.
                        if (!isNight && AiState == NpcAiState.GoHome && distToHomeForCheck < 30f)
                        {
                            ReleaseCurrentGoalLock();
                            AiState = NpcAiState.Idle;
                            AiTarget = WorldPos;
                            AiTimer = 0f;
                            AnimState = "idle";
                            _currentPath.Clear();
                        }

                        if (isNight && isAwayFromHome && AiState != NpcAiState.Flee && AiState != NpcAiState.Chase
                            && AiState != NpcAiState.GoToChestDeposit && AiState != NpcAiState.GoToChestWithdraw
                            && AiState != NpcAiState.GoHome
                            && TryCommitGoal(NpcPriority.GoHome))
                        {
                            AiState = NpcAiState.GoHome;
                            AiTarget = GetHomeEntryTarget();
                            _currentPath.Clear();
                            _pathRecalcTimer = 0f;
                            AnimState = "walk";
                            Speed = BaseSpeed;
                        }
                    }
                }
            }

            // Particules de pas
            if (!IsPlayer && !_isSleeping)
            {
                float movedDist = Vector2.Distance(WorldPos, _lastPosForParticles);
                if (movedDist > 5f)
                {
                    _stepParticleCooldown -= dt;
                    if (_stepParticleCooldown <= 0f)
                    {
                        SpawnFootstepParticle();
                        _stepParticleCooldown = STEP_PARTICLE_INTERVAL;
                        _lastPosForParticles = WorldPos;
                    }
                }
            }

            if (!IsPlayer && Species != "human" && !_isSleeping && !IsInPlayerDialogue && !IsTalking &&
                (AiState == NpcAiState.Idle || AnimState == "idle") && !IsKnockedBack)
            {
                _idleSoundCooldown -= dt;
                if (_idleSoundCooldown <= 0f && Random.Shared.NextDouble() < 0.08)
                {
                    TryPlayIdleSound(playerPos);
                }
            }

            // Comportement apprivoisé
            //  Tant que l'animal est monté, c'est le joueur (via Program.cs) qui contrôle
            // directement sa position/son animation : on ne laisse plus l'IA (Follow/Roam/...)
            // décider d'un déplacement, sinon l'animal "tire" contre les commandes du joueur.
            if (IsTamed && !IsMounted)
            {
                UpdateTamedOrders(petOwnerPos);
                UpdateFollowSpeed();
            }

            UpdateWaterStatus();

            if (IsRidingMount && AiState == NpcAiState.Idle)
                DismountOwnedMount();

            if (IsBoss && string.Equals(Species, "Khamsin", StringComparison.OrdinalIgnoreCase))
            {
                UpdateKhamsinBoss(dt, destroyed, playerPos);
                return;
            }

            if (IsWorm)
            {
                UpdateWormBoss(dt, destroyed, playerPos);
                return;
            }

            if (IsGulper)
            {
                UpdateGulperBoss(dt, destroyed, playerPos);
                return;
            }

            if (IsGenie)
            {
                UpdateGenieBoss(dt, destroyed, playerPos);
                return;
            }

            //  PNJ en dialogue/commerce avec le joueur : il arrête simplement de se déplacer
            // (l'IA de mouvement/pathfinding est mise en pause) le temps de l'interaction. Tout
            // le reste (animation idle, clignement, dégâts, sommeil...) continue normalement,
            // et il reprend son comportement habituel dès la fin du dialogue.
            if (IsInPlayerDialogue)
            {
                Speed = 0f;
                AnimState = "idle";
            }

            // Animation
            //  Un animal monté n'a plus d'AiState de déplacement (il reste Idle, contrôlé par
            // le joueur) : on se base donc aussi sur AnimState ("walk"/"run", fixé directement
            // par Program.cs pendant la monte) pour que l'animation suive le mouvement imposé
            // par le joueur.
            float animSpeed;
            if (AnimState == "run")
                animSpeed = 12f;
            else if (AnimState == "walk" || AiState == NpcAiState.WalkToTarget || AiState == NpcAiState.Wander ||
                     AiState == NpcAiState.Flee || AiState == NpcAiState.Chase ||
                     AiState == NpcAiState.LookAtPlayer || AiState == NpcAiState.Follow ||
                     AiState == NpcAiState.GoHome || AiState == NpcAiState.GoToChestWithdraw ||
                     AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToMerchant ||
                     AiState == NpcAiState.GoToGroundItem || AiState == NpcAiState.GoToVillageCenter)
                animSpeed = 8f;
            else
                animSpeed = 2f;
            AnimProgress += dt * animSpeed;
            if (AnimProgress >= 1f)
            {
                AnimProgress = 0f;
                int frameCount = 8;
                if (SpeciesData.Skeletons.TryGetValue(Species, out var skeleton))
                {
                    var firstPart = skeleton.FirstOrDefault();
                    if (firstPart != null && firstPart.Animations.TryGetValue(AnimState, out var frames) && frames.Count > 0)
                        frameCount = frames.Count;
                }
                if (frameCount == 0) frameCount = 1;
                CurrentFrame = (CurrentFrame + 1) % frameCount;

                if (!IsMounted && Speed > 0.01f &&
                    Program.ShouldTriggerFootstepFrame(AnimState, CurrentFrame, frameCount))
                    Program.PlayEntityFootstepSound();
            }

            if (IsPlayer) return;

            //  L'animal est monté par le JOUEUR : sa position/orientation/animation sont
            // entièrement pilotées par Program.cs à partir des touches du joueur (voir
            // MountedAnimal). On s'arrête ici pour qu'aucune IA (pathfinding, Decide(),
            // poursuite...) ne vienne déplacer l'animal de son propre chef pendant qu'il
            // est monté (Rider == null ⇒ convention "monté par le joueur", voir Mount()).
            if (IsMounted && Rider == null) return;

            //  MONTURE PILOTÉE PAR UN PNJ (Rider != null, ex : loup de compagnie d'un gobelin) :
            // contrairement au joueur, il n'y a pas de code dédié dans Program.cs pour ça — le
            // cavalier garde entièrement sa propre IA/pathfinding (Chase, GoHome...), et c'est la
            // monture qui se contente de recopier sa position/orientation chaque frame, avec un
            // léger décalage de selle. Si le cavalier a disparu ou a mis pied à terre entre-temps
            // (cas limite : mort du cavalier, sauvegarde partielle...), on descend par sécurité.
            if (IsMounted)
            {
                if (Rider == null || !Rider.IsAlive || !Rider.IsRidingMount)
                {
                    Dismount();
                    return;
                }

                Vector2 seatOffset = new Vector2(0f, -6f); // la monture se cale juste sous son cavalier
                WorldPos = Rider.WorldPos + seatOffset;
                Facing = Rider.Facing;
                AnimState = Rider.Speed > BaseSpeed * 1.2f ? "run" : (Rider.Speed > 0.01f ? "walk" : "idle");

                // Si cette monture est chevauchée par un PNJ qui poursuit une cible, elle peut
                // également participer au combat en attaquant la même cible que son maître.
                if (Rider.AiState == NpcAiState.Chase && Rider.OwnedMount == this)
                {
                    AiTarget = Rider.AiTarget;
                    AiTargetConnectionId = Rider.AiTargetConnectionId;

                    float distToTarget = Vector2.Distance(WorldPos, AiTarget);
                    if (_attackPhase != AttackPhase.None || distToTarget <= Math.Max(AttackHitRange * 2f, 250f))
                    {
                        CombatBehavior(dt, destroyed, AiTarget);
                    }
                }
                return;
            }

            if (IsInPlayerDialogue)
            {
                Speed = 0f;
                ResetMoveMomentum();
                return;
            }

            // ==================== IA ====================
            if (_goalLockTimer > 0f)
            {
                _goalLockTimer -= dt;
                if (_goalLockTimer < 0f) _goalLockTimer = 0f;
            }
            //  PRIORITÉ ABSOLUE : vie en danger. Si les PV sont critiques et qu'une menace
            // (le joueur) se trouve à proximité, l'entité fuit quel que soit ce qu'elle était
            // en train de faire (combat, routine, nourriture, discussion...). Ce contrôle est
            // fait avant tout le reste de la logique de décision pour éviter qu'une entité ne
            // reste "coincée" à hésiter entre attaquer/rentrer chez elle et fuir pour sa survie.
            if (!IsVillageGuard && !IsBoss && !IsTamed && IsAlive && MaxHP > 0)
            {
                float hpPercent = (float)CurrentHP / MaxHP;
                float fleeTriggerRadius = GetFleeTriggerRadius();
                Vector2 fearTargetPos = GetFearTargetPosition();
                float distanceToThreatSq = Vector2.DistanceSquared(WorldPos, fearTargetPos);
                bool threatNearby = _hasFearTarget && distanceToThreatSq < fleeTriggerRadius * fleeTriggerRadius;
                if (hpPercent <= 0.25f && threatNearby && TryCommitGoal(NpcPriority.Survival, 2.5f))
                {
                    if (AiState != NpcAiState.Flee)
                    {
                        Vector2 fleeDir = WorldPos - fearTargetPos;
                        fleeDir = fleeDir.LengthSquared() > 0.01f ? Vector2.Normalize(fleeDir) : new Vector2(1, 0);
                        AiState = NpcAiState.Flee;
                        _attackPhase = AttackPhase.None;
                        AiTarget = fearTargetPos + fleeDir * GetFleeSafeRadius();
                        AiTimer = 6f;
                        Speed = RunSpeed;
                        ShowAlertIcon = false;
                        ResetPathfinding();
                    }
                }
            }

            if (!IsPlayer && Program.IsPlayerRagdolled
                && AiState != NpcAiState.GoToInfirmary && AiState != NpcAiState.HealingAtInfirmary)
            {
                Speed = 0f;
                AnimState = "idle";
                AiState = NpcAiState.Idle;
                AiTarget = WorldPos;
                _currentPath.Clear();
                ResetMoveMomentum();
                return;
            }

            //  Décompte de la période de grâce après un abandon de poursuite (voir plus bas).
            if (_chaseGiveUpCooldown > 0f)
            {
                _chaseGiveUpCooldown -= dt;
                if (_chaseGiveUpCooldown < 0f) _chaseGiveUpCooldown = 0f;
            }

            bool shouldChase = false;
            if (_chaseGiveUpCooldown <= 0f)
            {
                if (Behavior == "hostile" && !IsTamed)
                {
                    float visionDistance = VisionRange * Program.TileSize;
                    shouldChase = (VisionRange > 0 && distToPlayerSq < visionDistance * visionDistance);
                }
                else if (Behavior == "neutral" && IsAlerted && !IsTamed)
                {
                    float visionDistance = VisionRange * Program.TileSize;
                    shouldChase = (VisionRange > 0 && distToPlayerSq < visionDistance * visionDistance);
                }
                else if (IsTamed && IsHostilePet && TamedBehavior == TamedAnimalMode.Attack)
                {
                    //  Uniquement les pets d'un PNJ hostile chargent le joueur. Pour un animal du
                    // joueur, seule _combatTargetEntity (voir FindPlayerPetTarget) déclenche la poursuite.
                    float visionDistance = VisionRange * Program.TileSize;
                    shouldChase = (VisionRange > 0 && distToPlayerSq < visionDistance * visionDistance);
                }
            }

            if (isRetaliating || _combatTargetEntity != null)
                shouldChase = true;

            // Une fuite de survie ne peut pas être interrompue par une détection visuelle,
            // une riposte mémorisée ou une alerte reçue d'une autre entité.
            bool isFleeingForSurvival = AiState == NpcAiState.Flee
                || _currentGoalPriority >= NpcPriority.Survival;
            if (isFleeingForSurvival)
            {
                shouldChase = false;
                ShowAlertIcon = false;
            }

            if ((IsVillageGuard || IsSpiritAnimal) && _combatTargetEntity == null && !isRetaliating && AiState == NpcAiState.Chase)
            {
                AiState = NpcAiState.Idle;
                AiTarget = WorldPos;
                _attackPhase = AttackPhase.None;
                _currentGoalPriority = NpcPriority.Idle;
                _goalLockTimer = 0f;
                _currentPath.Clear();
                Speed = 0f;
            }

            //  ABANDON DE POURSUITE : évite qu'une entité pourchasse le joueur à l'infini.
            // Deux cas distincts, vérifiés uniquement pendant une poursuite en cours :
            //   1) "Semée" : le joueur s'est suffisamment éloigné (au-delà du double de la
            //      distance à laquelle l'entité le repère normalement) pour qu'on considère
            //      qu'elle l'a perdu de vue - abandon immédiat, quelle que soit la durée.
            //   2) Poursuite trop longue ET trop éloignée du point de départ : le joueur reste
            //      à portée mais la course-poursuite dure depuis un moment ET a entraîné
            //      l'entité loin de là où elle se trouvait avant d'attaquer - elle renonce et
            //      rentre plutôt que de suivre indéfiniment (utile si le joueur recule sans
            //      jamais sortir complètement de portée).
            // Dans les deux cas : retour au point de départ de la poursuite (_chaseStartPos),
            // pas au domicile (HomePosition), qui peut être vide ou très éloigné pour un monstre.
            if (AiState == NpcAiState.Chase)
            {
                _chaseElapsedTime += dt;

                float visionDistance = VisionRange * Program.TileSize;
                bool petOwnTarget = (petAttackActive || IsSpiritAnimal) && _combatTargetEntity != null;
                bool lostSight = !isRetaliating && !petOwnTarget && visionDistance > 0f
                    && distToPlayerSq > (visionDistance * 2f) * (visionDistance * 2f);
                bool strayedTooFar = !isRetaliating && !petOwnTarget && _chaseElapsedTime > CHASE_LEASH_TIME
                    && Vector2.DistanceSquared(WorldPos, _chaseStartPos) > (CHASE_LEASH_RANGE) * (CHASE_LEASH_RANGE);

                if (lostSight || strayedTooFar)
                {
                    shouldChase = false;
                    _chaseGiveUpCooldown = CHASE_GIVEUP_COOLDOWN_DURATION;
                    IsAlerted = false; // sinon une entité "neutral" repartirait en Chase dès la fin du cooldown
                    HasPropagatedSignal = false;
                    _attackPhase = AttackPhase.None;

                    AiState = NpcAiState.WalkToTarget;
                    AiTarget = _chaseStartPos != Vector2.Zero ? _chaseStartPos : WorldPos;
                    AiTimer = 20f; // large marge ; Decide() reprend la main normalement une fois arrivée
                    Speed = BaseSpeed;
                    _currentPath.Clear();
                    _pathRecalcTimer = 0f;
                    _currentGoalPriority = NpcPriority.Idle;
                    _goalLockTimer = 0f;
                    ShowAlertIcon = false;
                }
            }

            if (ShowAlertIcon && !IsSlime && _currentGoalPriority <= NpcPriority.AlertFreeze)
            {
                // Tant que l'entité est alertée visuellement, elle ne se déplace pas.
                // (Priorité "AlertFreeze" : pas besoin de TryCommitGoal ici, ce court arrêt
                // ne fait que geler le mouvement sans changer l'objectif de fond.)
                if (_currentGoalPriority <= NpcPriority.AlertFreeze)
                {
                    Speed = 0f;
                    AnimState = "idle";
                    AiTarget = WorldPos;
                    _currentPath.Clear();
                }
            }
            else if (shouldChase && TryCommitGoal(NpcPriority.Combat, 1.2f))
            {
                if (AiState != NpcAiState.Chase)
                {
                    AiState = NpcAiState.Chase;
                    AiTarget = playerPos;
                    Speed = RunSpeed;

                    //  Nouvelle poursuite : on mémorise le point de départ et on repart de
                    // zéro sur la durée, pour l'abandon "leash" ci-dessus.
                    _chaseStartPos = WorldPos;
                    _chaseElapsedTime = 0f;

                    //  Le joueur est loin : si ce PNJ possède une monture (ex : loup de
                    // compagnie d'un gobelin), il grimpe dessus pour le rattraper plus vite
                    // plutôt que de courir à pied (voir TryMountOwnedPet/OwnedMount).
                    TryMountOwnedPet(playerPos);

                    //  Détection directe (pas via écho) : affiche l'icône d'alerte et programme
                    // l'émission du signal qui va réveiller les entités hostiles proches.
                    if (Behavior == "hostile" && !IsTamed)
                    {
                        TriggerAlertSignal(playerPos, AiTargetConnectionId);
                    }
                }
                AiTarget = playerPos;
                AiTimer = 2f;
            }
            else if (AiState == NpcAiState.Chase)
            {
                // Tant que le PNJ est en poursuite, retenter la montée si la cible
                // s'éloigne suffisamment après un départ initial trop proche.
                TryMountOwnedPet(AiTarget);
            }
            else if (AiState != NpcAiState.Chase && HasPropagatedSignal)
            {
                // La poursuite est bien terminée (plus en Chase) : on autorise l'entité à relayer
                // à nouveau un futur signal d'alerte.
                HasPropagatedSignal = false;
            }

            // Attirance par la nourriture
            const float FOOD_ATTRACTION_RANGE = 180f;
            if (PreferredFoodId != 0 && AiState != NpcAiState.Chase && AiState != NpcAiState.Flee && !IsTamed)
            {
                bool hasPreferredFood = (playerHeldItemId == PreferredFoodId);
                IsInterestedInFood = hasPreferredFood;

                if (hasPreferredFood && distToPlayerSq < FOOD_ATTRACTION_RANGE * FOOD_ATTRACTION_RANGE && TryCommitGoal(NpcPriority.Food))
                {
                    if (AiState != NpcAiState.Follow)
                    {
                        AiState = NpcAiState.Follow;
                        AiTarget = playerPos;
                    }
                    else
                    {
                        AiTarget = playerPos;
                    }
                }
                else if (!hasPreferredFood && AiState == NpcAiState.Follow && _currentGoalPriority <= NpcPriority.Food)
                {
                    IsInterestedInFood = false;
                    _currentGoalPriority = NpcPriority.Idle;
                    _goalLockTimer = 0f;
                    Decide();
                }
            }
            else
            {
                IsInterestedInFood = false;
                if (AiState == NpcAiState.Follow && _currentGoalPriority <= NpcPriority.Food)
                {
                    _currentGoalPriority = NpcPriority.Idle;
                    _goalLockTimer = 0f;
                    Decide();
                }
            }

            AiTimer -= dt;

            if (_villageCenterVisitCooldown > 0f)
                _villageCenterVisitCooldown = Math.Max(0f, _villageCenterVisitCooldown - dt);

            if (_workBreakCooldown > 0f)
                _workBreakCooldown = Math.Max(0f, _workBreakCooldown - dt);
            else if (_workShiftTimer > 0f)
                _workShiftTimer -= dt;

            if (_workplaceRetryCooldown > 0f)
                _workplaceRetryCooldown = Math.Max(0f, _workplaceRetryCooldown - dt);

            _farmClock += dt;
            if (_farmerActionCooldown > 0f)
                _farmerActionCooldown = Math.Max(0f, _farmerActionCooldown - dt);
            if (_farmerNoWorkCooldown > 0f)
                _farmerNoWorkCooldown = Math.Max(0f, _farmerNoWorkCooldown - dt);
            if (_workGoalBackoffTimer > 0f)
                _workGoalBackoffTimer = Math.Max(0f, _workGoalBackoffTimer - dt);

            if (_chestDepositRetryCooldown > 0f)
                _chestDepositRetryCooldown = Math.Max(0f, _chestDepositRetryCooldown - dt);

            if (_abandonedGoalCooldown > 0f)
            {
                _abandonedGoalCooldown -= dt;
                if (_abandonedGoalCooldown < 0f)
                    _abandonedGoalCooldown = 0f;
            }

            if (_walkingToBench && AiState == NpcAiState.WalkToTarget)
            {
                if (Vector2.DistanceSquared(WorldPos, AiTarget) < (70f) * (70f))
                {
                    StartSitting();
                }
                else if (AiTimer <= 0f)
                {
                    World.ReleaseBench(_reservedBench);
                    _reservedBench = null;
                    _walkingToBench = false;
                    AiState = NpcAiState.Idle;
                    AnimState = "idle";
                    AiTimer = (float)(Random.Shared.NextDouble() * 2f + 1f);
                }
            }

            //  IMPORTANT : tout état dont le déplacement passe par MoveTowardTarget() doit
            // figurer ici, sinon _currentPath reste vide pour toujours (il n'est jamais
            // rempli qu'ici, ailleurs on ne fait que le vider avec _currentPath.Clear() en
            // changeant d'état) et MoveTowardTarget renvoie immédiatement sans bouger le PNJ
            // (voir son "if (_currentPath.Count == 0) { AnimState = "idle"; return; }").
            // GoToChestWithdraw, GoToChestDeposit, GoToMerchant et GoToGroundItem utilisent
            // tous MoveTowardTarget mais étaient absents de cette liste : le PNJ restait donc
            // figé sur place en permanence pour ces 4 états, quel que soit AiTarget.
            //
            //  FILET DE SÉCURITÉ (vitesse) : chaque site du code qui décide d'un de ces états
            // est censé remettre Speed à une valeur non nulle (BaseSpeed/RunSpeed) avant de le
            // faire. Mais avec des dizaines de points de décision dispersés dans l'IA, il suffit
            // d'un seul oubli (ex : GoHome assigné juste après un Idle où Speed valait 0) pour
            // qu'un PNJ reste planté indéfiniment tout en "pensant" activement à un objectif :
            // MoveTowardTarget calcule bien un chemin et une cible, mais le déplacement réel
            // (dir * Speed * dt) est nul. Plutôt que de compter sur chaque appelant pour ne
            // jamais l'oublier, on garantit ici, à l'endroit unique où TOUS ces états convergent,
            // qu'un PNJ qui a "quelque chose en tête" a toujours de quoi s'y rendre : dès qu'il
            // est dans un état de déplacement mais que Speed est resté à 0, on le corrige
            // immédiatement (le PNJ agit sans attendre un correctif tiers).
            if (AiState != NpcAiState.Chase && _combatActive && _attackPhase == AttackPhase.None)
                ResetCombatState();
            if (AiState != NpcAiState.Wander)
                _wanderActive = false;

            bool isMovementState = IsMovementState(AiState)
                && !(AiState == NpcAiState.Wander && UsesMeanderWander);
            if (isMovementState && Speed <= 0f)
            {
                Speed = BaseSpeed > 0f ? BaseSpeed : SpeciesData.GetSpeed(Species);
            }

            if (isMovementState)
            {
                if (_pathRecalcTimer > 0f)
                {
                    _pathRecalcTimer -= dt;
                    if (_pathRecalcTimer < 0f)
                        _pathRecalcTimer = 0f;
                }

                //  Détection "chemin trouvé mais plus aucune progression" : indépendante du
                // recalcul de chemin ci-dessous, elle tourne en continu pendant que le PNJ
                // est censé se déplacer. Si sa position n'a quasiment pas bougé sur tout un
                // intervalle de contrôle, c'est qu'il est physiquement coincé (bousculé,
                // obstacle dynamique apparu sur le chemin déjà calculé, etc.) même si
                // _currentPath n'est pas vide.
                if (_currentPath.Count > 0)
                {
                    _stuckProgressTimer += dt;
                    if (_stuckProgressTimer >= STUCK_PROGRESS_CHECK_INTERVAL)
                    {
                        float moved = Vector2.Distance(WorldPos, _stuckProgressCheckPos);
                        _stuckProgressTimer = 0f;
                        _stuckProgressCheckPos = WorldPos;

                        if (moved < STUCK_PROGRESS_MIN_DISTANCE)
                        {
                            //  CORRECTIF : GoToChestDeposit se contentait de vider le chemin et de
                            // retenter indéfiniment sans jamais abandonner, contrairement à tous les
                            // autres états. Un PNJ physiquement coincé en permanence près de son point
                            // d'approche (bousculé par d'autres PNJ convergeant vers le même coffre,
                            // obstacle dynamique persistant) restait donc bloqué pour toujours au lieu
                            // de finir par renoncer comme n'importe quel autre trajet. On plafonne
                            // maintenant les tentatives, comme pour MAX_PATH_FAIL_STREAK.
                            if (AiState == NpcAiState.GoToChestDeposit
                                && ++_chestDepositStuckRetryCount < CHEST_DEPOSIT_STUCK_MAX_RETRIES)
                            {
                                _currentPath.Clear();
                                _pathRecalcTimer = 0f;
                                _stuckProgressTimer = 0f;
                            }
                            else
                            {
                                if (AiState == NpcAiState.GoToChestDeposit)
                                    _chestDepositStuckRetryCount = 0;
                                AbandonCurrentGoal("aucune progression sur le chemin (bloqué physiquement)");
                            }
                            goto skipPathRecompute; // AiState est repassé à Idle : rien d'autre à faire ce tick
                        }
                    }
                }
                else
                {
                    _stuckProgressTimer = 0f;
                    _stuckProgressCheckPos = WorldPos;
                }

                if (_pathRecalcTimer > 0f)
                {
                    // Garder le chemin courant ou attendre avant de retenter si le dernier
                    // calcul n'a trouvé aucun chemin.
                }
                else
                {
                    if (!Pathfinder.TryReservePathRequest())
                    {
                        // Le quota global protège le thread principal contre les pics d'A*.
                        // Ce n'est pas un échec de chemin : on laisse le PNJ retenter bientôt.
                        //  CORRECTIF FAMINE : un délai fixe (0.05f) faisait retenter TOUTES les
                        // entités recalées exactement au même tick, dans le même ordre de boucle
                        // qu'avant (voir Program.Update) - le même sous-ensemble d'entités
                        // gagnait donc indéfiniment le quota pendant que les autres restaient
                        // plantées sur place sans jamais obtenir de chemin. La gigue aléatoire
                        // désynchronise les tentatives suivantes pour que chacune finisse par
                        // passer devant les autres au moins une fois.
                        _pathRecalcTimer = 0.05f + (float)(Random.Shared.NextDouble() * 0.1f);
                        goto skipPathRecompute;
                    }

                    //  OPTIMISATION PERF (villages avec beaucoup de PNJ) : le calcul de chemin
                    // A* (Pathfinder.FindPath) est l'opération la plus coûteuse de l'IA d'un PNJ.
                    // Avant, tous les PNJ en mouvement le relançaient en rythme sur exactement le
                    // même intervalle (PATH_RECALC_INTERVAL), ce qui créait un "pic" de calcul
                    // périodique dès qu'une dizaine de villageois marchaient en même temps
                    // (effet "thundering herd"). Deux correctifs :
                    //   1) un peu de gigue (jitter) aléatoire sur l'intervalle pour étaler ces
                    //      recalculs sur plusieurs frames au lieu de les synchroniser ;
                    //   2) les PNJ loin du joueur (donc hors écran ou en périphérie) recalculent
                    //      leur chemin beaucoup moins souvent : la précision du pathfinding n'a
                    //      d'importance visuelle que pour ce que le joueur regarde réellement.
                    //  CORRECTIF : mêmes seuils trop bas que NEARBY_NPC_AI_DISTANCE_SQ (voir
                    // ShouldRunFullAiUpdate) - 350/700px coupaient déjà la fréquence de recalcul
                    // pour des PNJ encore nettement visibles à l'écran, aggravant l'impression de
                    // blocage (chemin périmé non rafraîchi). Alignés sur la même distance
                    // "réellement hors écran" que le seuil d'IA complète ci-dessus.
                    float distanceFactor = 1f;
                    float distToPlayerForPath = Vector2.Distance(WorldPos, playerPos);
                    if (distToPlayerForPath > 2200f)
                        distanceFactor = 4f;   // très loin / hors écran : recalculs bien plus rares
                    else if (distToPlayerForPath > 1100f)
                        distanceFactor = 2f;   // périphérie de l'écran : deux fois moins souvent

                    float jitter = 1f + ((float)Random.Shared.NextDouble() - 0.5f) * 0.4f; // ±20%
                    _pathRecalcTimer = PATH_RECALC_INTERVAL * distanceFactor * jitter;

                    //  GoHome utilise désormais exactement la même IA de pathfinding
                    // (Pathfinder.FindPath) que les autres états, jusqu'à HomePosition
                    // directement. Si la recherche vers la case intérieure échoue, on tente
                    // au moins de trouver un chemin jusqu'à la porte de la maison.
                    int searchMargin = ComputeDynamicPathMargin(WorldPos, AiTarget);
                    //  Le plafond de marge était FIXE d'un essai à l'autre : une cible qui exige un détour
                    // plus large (maison, étang, clôture du champ...) échouait donc trois fois à l'identique.
                    // Chaque échec consécutif élargit désormais la zone de recherche.
                    if (_pathFailStreak > 0)
                        searchMargin = Math.Min(searchMargin * (1 + _pathFailStreak) + 6, 48);
                    _currentPath = Pathfinder.FindPath(WorldPos, AiTarget, destroyed, searchMargin, Species);

                    if (_currentPath.Count == 0 && AiState == NpcAiState.GoHome && HomeBuildingId >= 0 && World.Houses.TryGetValue(HomeBuildingId, out var homeHouse))
                    {
                        var doorPath = Pathfinder.FindPath(WorldPos, homeHouse.DoorWorldPos, destroyed, searchMargin, Species);
                        if (doorPath.Count > 0)
                        {
                            _currentPath = doorPath;
                        }
                        else
                        {
                            // Aucun chemin A* vers l'intérieur ni vers la porte pour le moment :
                            // ne pas passer en Idle tout de suite, mais compter l'échec comme les
                            // autres états ci-dessous (voir bloc commun juste après).
                            _currentPath.Clear();
                            _pathRecalcTimer = PATH_RECALC_INTERVAL * 2f * distanceFactor; // attendre un peu avant de retenter
                        }
                    }

                    //  BLOC COMMUN À TOUS LES ÉTATS (y compris GoHome) : si FindPath n'a rien
                    // renvoyé, on compte l'échec. Au bout de MAX_PATH_FAIL_STREAK essais
                    // consécutifs, la cible est considérée comme injoignable pour l'instant :
                    // on abandonne l'envie plutôt que de laisser le PNJ figé indéfiniment avec
                    // une ligne rouge de debug sans ligne verte associée.
                    if (_currentPath.Count == 0)
                    {
                        _pathFailStreak++;
                        if (_pathFailStreak >= MAX_PATH_FAIL_STREAK)
                        {
                            AbandonCurrentGoal($"chemin introuvable après {_pathFailStreak} essais");
                        }
                        else if (AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToChestWithdraw)
                        {
                            _pathRecalcTimer = PATH_RECALC_INTERVAL * 2f * distanceFactor;
                        }
                    }
                    else
                    {
                        _pathFailStreak = 0;
                    }
                }
            }

        skipPathRecompute:

            // Exécution des états IA
            if (IsTalking)
            {
                //  Le PNJ s'arrête net et s'oriente vers son interlocuteur pendant qu'il
                // parle (au lieu de continuer son trajet en cours). S'il parle "dans le vide"
                // (pas de CurrentTalkPartner : petite réplique aléatoire), on le fait regarder
                // vers le joueur, comme le fait déjà UpdateGaze dans ce cas.
                AnimState = "idle";
                Vector2 lookTarget = CurrentTalkPartner != null ? CurrentTalkPartner.WorldPos : playerPos;
                float dx = lookTarget.X - WorldPos.X;
                if (MathF.Abs(dx) > 0.01f)
                    Facing = dx >= 0f ? 1f : -1f;
            }
            else if (ShowAlertIcon && !IsSlime && _currentGoalPriority <= NpcPriority.AlertFreeze)
            {
                AnimState = "idle";
            }
            else switch (AiState)
            {
                case NpcAiState.Idle:
                    AnimState = "idle";
                    if (AiTimer <= 0f) Decide();
                    break;
                case NpcAiState.WalkToTarget:
                    PerformMovement(dt, destroyed);
                    break;
                case NpcAiState.Wander:
                    if (IsSlime)
                        UpdateSlimeMovement(dt, destroyed);
                    else
                        Wander(dt, destroyed);
                    break;
                case NpcAiState.Flee:
                    if (IsSlime)
                    {
                        // Slimes should flee by jumping, not by running.
                        // Ensure AiTarget is set (OnHit sets it), then use the slime jump movement.
                        UpdateSlimeMovement(dt, destroyed);

                        // Reuse Flee end conditions: if far enough, stop fleeing.
                        Vector2 fearTargetPos = GetFearTargetPosition();
                        float distToThreat = Vector2.Distance(WorldPos, fearTargetPos);
                        if (distToThreat > GetFleeSafeRadius())
                        {
                            _continuousFearTimer = 0f;
                            AiState = NpcAiState.LookAtPlayer;
                            _lookAtPlayerTimer = 1.5f;
                            Speed = SpeciesData.GetSpeed(Species);
                            ResetPathfinding();
                        }
                        else if (AiTimer <= 0f)
                        {
                            AiTimer = 2f;
                        }
                    }
                    else
                    {
                        Flee(dt, destroyed, playerPos);
                    }
                    break;
                case NpcAiState.Chase:
                    if (IsSlime)
                    {
                        AiTarget = playerPos;
                        UpdateSlimeMovement(dt, destroyed);
                    }
                    else
                        ChaseBehavior(dt, destroyed, _combatTargetEntity?.WorldPos ?? playerPos);
                    break;
                case NpcAiState.LookAtPlayer:
                    LookAtPlayer(dt, destroyed, playerPos);
                    break;
                case NpcAiState.Follow:
                    PerformMovement(dt, destroyed);
                    break;
                case NpcAiState.GoHome:
                    if (IsInsideAssignedHouse())
                    {
                        ReleaseCurrentGoalLock();
                        AiState = NpcAiState.Idle;
                        AiTarget = WorldPos;
                        AnimState = "idle";
                        Speed = 0f;
                        _currentPath.Clear();
                    }
                    else
                    {
                        MoveTowardTarget(dt, destroyed, arrivalRadius: 20f);
                    }
                    break;
                case NpcAiState.GoToInfirmary:
                    if (Vector2.DistanceSquared(WorldPos, AiTarget) <= 35f * 35f)
                    {
                        AiState = NpcAiState.HealingAtInfirmary;
                        AiTarget = WorldPos;
                        _currentPath.Clear();
                        _infirmaryHealAccumulator = 0f;
                        Speed = 0f;
                        AnimState = "sit";
                    }
                    else
                    {
                        MoveTowardTarget(dt, destroyed, arrivalRadius: 25f);
                    }
                    break;
                case NpcAiState.HealingAtInfirmary:
                    HealAtInfirmary(dt);
                    break;
                case NpcAiState.Sitting:
                    AnimState = "sit";
                    _sitTimer -= dt;
                    if (_sitTimer <= 0f)
                        StopSitting();
                    break;
                case NpcAiState.GoToChestWithdraw:
                case NpcAiState.GoToChestDeposit:
                case NpcAiState.GoToMerchant:
                case NpcAiState.GoToGroundItem:
                case NpcAiState.GoToVillageCenter:
                    UpdateGoalArrival(dt, destroyed);
                    break;
            }

            //  Décélération / glissement résiduel si aucun déplacement piloté n'a eu lieu cette frame.
            ApplyResidualMomentum(dt, destroyed);
        }

        private void HealAtInfirmary(float dt)
        {
            if (_infirmaryBuildingId < 0)
            {
                ReleaseCurrentGoalLock();
                AiState = NpcAiState.Idle;
                Decide();
                return;
            }

            Speed = 0f;
            AnimState = "sit";
            _infirmaryHealAccumulator += dt * 5f;
            int healing = (int)_infirmaryHealAccumulator;
            if (healing <= 0)
                return;

            _infirmaryHealAccumulator -= healing;
            CurrentHP = Math.Min(MaxHP, CurrentHP + healing);
            if (CurrentHP < MaxHP)
                return;

            World.ReleaseInfirmary(_infirmaryBuildingId, NetId);
            _infirmaryBuildingId = -1;
            _infirmaryHealAccumulator = 0f;
            _hasFearTarget = false;
            _fearTargetEntity = null;
            _fearTargetIsPlayer = false;
            _fearTargetConnectionId = -1;
            _retaliationTargetEntity = null;
            _retaliateAgainstPlayer = false;
            _retaliationTimer = 0f;
            ReleaseCurrentGoalLock();
            AiState = NpcAiState.Idle;
            AiTarget = WorldPos;
            AiTimer = 0f;
            Decide();
        }

        //  Résultat d'une action de fin d'objectif (voir UpdateGoalArrival / GetGoalArrivalConfig).
        //  Complete : l'objectif est rempli, le PNJ redevient Idle et oublie sa cible.
        //  Retry : le PNJ reste sur place et retente la même action au prochain tick
        //          (ex : coffre plein, il faut réessayer plutôt qu'abandonner l'objectif).
        private enum GoalArrivalResult { Complete, Retry }

        //  ==================== ARRIVÉE À UN OBJECTIF (unifié) ====================
        //  Point d'entrée UNIQUE pour tous les états "le PNJ va quelque part pour y faire
        //  quelque chose" (retirer/déposer au coffre, acheter chez le marchand, ramasser un
        //  objet au sol, échanger au centre du village). Avant, chaque état dupliquait à la
        //  main : le test de distance d'arrivée, l'appel à MoveTowardTarget avec SON propre
        //  rayon, et le nettoyage (AiState = Idle, AiTarget = WorldPos, _currentPath.Clear()).
        //  Un correctif oublié dans un état ne l'était jamais appliqué aux autres — c'est ce
        //  qui produisait des PNJ qui restaient figés sur certains trajets et pas d'autres.
        //  Désormais la règle est unique et vaut pour tous les états listés ci-dessus :
        //      le PNJ a une intention (AiTarget) → tant qu'il n'est pas arrivé, il marche
        //      (MoveTowardTarget) → arrivé, il exécute l'action de fin propre à son état
        //      (GetGoalArrivalConfig) → Complete referme l'objectif, Retry le fait rester.
        private void UpdateGoalArrival(float dt, HashSet<string> destroyed)
        {
            var (arrivalRadius, onArrive) = GetGoalArrivalConfig(AiState);

            if (Vector2.DistanceSquared(WorldPos, AiTarget) < arrivalRadius * arrivalRadius)
            {
                GoalArrivalResult result = onArrive != null ? onArrive() : GoalArrivalResult.Complete;
                if (result == GoalArrivalResult.Complete)
                {
                    // Refermer l'objectif, ET relâcher la priorité qui le protégeait — sans ça,
                    // _currentGoalPriority restait bloqué (ex: Forage) après un ramassage
                    // pourtant réussi, empêchant Routine de reprendre la main pour aller
                    // déposer l'objet au coffre (le PNJ semblait alors figé en Idle).
                    ReleaseCurrentGoalLock();
                    AiState = NpcAiState.Idle;
                    AiTarget = WorldPos;
                    _currentPath.Clear();
                }
                else // Retry : rester sur place, retenter dès le prochain tick
                {
                    _pathRecalcTimer = 0f;
                }
            }
            else
            {
                MoveTowardTarget(dt, destroyed, arrivalRadius: arrivalRadius);
            }
        }

        //  Table de configuration : pour chaque état "objectif", le rayon d'arrivée et
        //  l'action à exécuter une fois arrivé. C'est le SEUL endroit à modifier pour changer
        //  ce qui se passe quand un PNJ atteint sa cible pour un état donné.
        private (float arrivalRadius, Func<GoalArrivalResult>? onArrive) GetGoalArrivalConfig(NpcAiState state)
        {
            switch (state)
            {
                case NpcAiState.GoToChestWithdraw:
                    return (Program.TileSize * 1.25f, () =>
                    {
                        if (_farmerSupplyWithdrawPending)
                        {
                            _farmerSupplyWithdrawPending = false;
                            PrepareFarmerSeedsFromHouseChest();
                        }
                        else if (HomeBuildingId >= 0)
                            World.TryWithdrawFoodFromChest(HomeBuildingId, this);
                        AiTimer = 0f;
                        return GoalArrivalResult.Complete;
                    });

                case NpcAiState.GoToChestDeposit:
                    return (Program.TileSize * 1.25f, () =>
                    {
                        bool logArrival = !_chestArrivalLogged;
                        if (logArrival)
                        {
                            _chestArrivalLogged = true;
                            Console.WriteLine($"[NPC-CHEST] ARRIVAL npc={NetId} home={HomeBuildingId} pos={WorldPos} target={AiTarget} distance={Vector2.Distance(WorldPos, AiTarget):F1} inventoryBefore={GetChestInventoryLog()}");
                        }

                        int deposited = 0;
                        if (HomeBuildingId >= 0)
                            deposited = World.DepositExcessItemsToChest(HomeBuildingId, this, logArrival);

                        bool hasRemainingItems = World.HasAnyDepositableItem(this);
                        if (!hasRemainingItems)
                        {
                            if (logArrival)
                                Console.WriteLine($"[NPC-CHEST] COMPLETE npc={NetId} home={HomeBuildingId} inventoryAfter={GetChestInventoryLog()}");
                            _hasDepositedTonight = true;
                            _chestDepositFullRetryCount = 0;
                            CompleteRoutineTask(RoutineTaskKind.DepositItems);
                            return GoalArrivalResult.Complete;
                        }

                        //  CORRECTIF : si CETTE tentative n'a réussi à déposer STRICTEMENT AUCUN
                        // item (coffre déjà plein pour tous les types transportés), retenter
                        // indéfiniment ne sert à rien : le coffre ne se videra pas tout seul
                        // pendant que le PNJ attend devant. Avant ce correctif, hasRemainingItems
                        // restait vrai pour toujours et GetGoalArrivalConfig renvoyait Retry sans
                        // aucune limite : le PNJ restait planté devant le coffre à l'infini,
                        // bloquant au passage sa priorité Routine pour le reste de la nuit. On
                        // plafonne donc les tentatives "coffre plein" : au bout de quelques essais
                        // consécutifs sans le moindre dépôt, on abandonne pour ce soir en gardant
                        // les objets restants sur le PNJ (rien n'est perdu, il retentera demain).
                        if (deposited == 0)
                        {
                            _chestDepositFullRetryCount++;
                            if (_chestDepositFullRetryCount >= CHEST_DEPOSIT_FULL_MAX_RETRIES)
                            {
                                if (logArrival)
                                    Console.WriteLine($"[NPC-CHEST] GIVE_UP_FULL npc={NetId} home={HomeBuildingId} inventoryRemaining={GetChestInventoryLog()}");
                                // Ne PAS utiliser _hasDepositedTonight ici : il serait immédiatement
                                // remis à false par le correctif "objet ajouté après coup" puisque
                                // HasAnyDepositableItem reste vrai, ce qui relancerait aussitôt un
                                // nouveau trajet vers le même coffre plein (boucle infinie de
                                // trajets au lieu d'un blocage sur place, mais tout aussi bloquant).
                                _chestDepositRetryCooldown = CHEST_DEPOSIT_RETRY_COOLDOWN;
                                _chestDepositFullRetryCount = 0;
                                CompleteRoutineTask(RoutineTaskKind.DepositItems);
                                return GoalArrivalResult.Complete;
                            }
                        }
                        else
                        {
                            _chestDepositFullRetryCount = 0;
                        }

                        if (logArrival)
                            Console.WriteLine($"[NPC-CHEST] RETRY npc={NetId} home={HomeBuildingId} inventoryRemaining={GetChestInventoryLog()}");

                        // Le coffre peut être partiellement plein (certains types déposés, d'autres
                        // non) : on retente au lieu d'abandonner tout de suite, pour ne pas laisser
                        // le PNJ dormir avec des objets sur lui alors qu'il reste de la place.
                        return GoalArrivalResult.Retry;
                    });

                case NpcAiState.GoToMerchant:
                    return (55f, () =>
                    {
                        Program.TryBuyFoodFromMerchant(this, _targetMerchantNetId);
                        _targetMerchantNetId = Guid.Empty;
                        return GoalArrivalResult.Complete;
                    });

                case NpcAiState.GoToGroundItem:
                    return (25f, () =>
                    {
                        Program.TryNpcPickupGroundItem(this, _targetGroundItemNetId);
                        _targetGroundItemNetId = Guid.Empty;
                        return GoalArrivalResult.Complete;
                    });

                case NpcAiState.GoToVillageCenter:
                    return (65f, () =>
                    {
                        if (_targetVillageCenter != null)
                            World.ExchangeItemsAtVillageCenter(this, _targetVillageCenter);
                        _targetVillageCenter = null;
                        CompleteRoutineTask(RoutineTaskKind.VisitVillageCenter);
                        _villageCenterVisitCooldown = RandomRange(100f, 160f);

                        if (Profession == ProfessionType.Farmer && TryFarmerWorkCycle())
                        {
                            AiTimer = 10f;
                            return GoalArrivalResult.Complete;
                        }

                        AiTimer = RandomRange(4f, 9f);
                        return GoalArrivalResult.Complete;
                    });

                default:
                    // Ne devrait pas arriver : tout état routé vers UpdateGoalArrival doit
                    // avoir sa configuration ici. Par sécurité, on considère l'arrivée comme
                    // terminale plutôt que de boucler indéfiniment sur un état inconnu.
                    return (65f, null);
            }
        }

        private bool IsInsideAssignedHouse()
        {
            if (HomeBuildingId < 0 || !World.HouseInteriorSpots.TryGetValue(HomeBuildingId, out var spots))
                return false;

            int tileX = (int)(WorldPos.X / Program.TileSize);
            int tileY = (int)MathF.Floor((WorldPos.Y + Program.FeetOffsetY) / Program.TileSize);
            return spots.Any(spot => spot.Tile.x == tileX && spot.Tile.y == tileY);
        }

        private string GetChestInventoryLog()
        {
            return string.Join(",", Inventory.Slots
                .Where(slot => !slot.IsEmpty && slot.Item != null)
                .Select(slot => $"{slot.Item!.Name}x{slot.Count}"));
        }

        private Vector2 GetHomeEntryTarget()
        {
            return GetHomeInteriorTarget(moveAwayFromCurrentPosition: false);
        }

        private Vector2 GetHomeInteriorTarget(bool moveAwayFromCurrentPosition)
        {
            if (HomeBuildingId < 0
                || !World.HouseInteriorSpots.TryGetValue(HomeBuildingId, out var spots)
                || spots.Count == 0)
                return HomePosition;

            // Occupied réserve les cases de couchage; ces cases restent des destinations
            // valides pour les déplacements et les rondes à l'intérieur.
            var candidates = spots
                .Where(spot => World.GetObjectIdAt(spot.Tile.x, spot.Tile.y) == 0)
                .Where(spot => WorldTileRegistry.GetTile(World.GetGroundTileIdAt(spot.Tile.x, spot.Tile.y))?.Walkable == true)
                .Select(spot => spot.WorldPos)
                .Distinct()
                .ToList();
            if (candidates.Count == 0)
                return HomePosition;

            var otherResidents = new List<Vector2>();
            if (moveAwayFromCurrentPosition && IsInsideAssignedHouse())
                otherResidents.Add(WorldPos);

            foreach (var resident in Program.GetEntities())
            {
                if (resident == this || !resident.IsAlive || resident.HomeBuildingId != HomeBuildingId)
                    continue;

                bool isHeadingHome = resident.AiState == NpcAiState.GoHome;
                bool isInside = resident.IsInsideAssignedHouse();
                if (!isHeadingHome && !isInside)
                    continue;

                bool hasInteriorMovementTarget = IsMovementState(resident.AiState)
                    && candidates.Any(candidate => Vector2.DistanceSquared(candidate, resident.AiTarget) < 4f);
                otherResidents.Add(isHeadingHome || hasInteriorMovementTarget
                    ? resident.AiTarget
                    : resident.WorldPos);
            }

            if (otherResidents.Count == 0)
                return candidates[Random.Shared.Next(candidates.Count)];

            // Répartir les objectifs dans les cases praticables les plus éloignées des
            // habitants déjà dans la maison ou en route, au lieu de faire converger tout
            // le monde vers le seuil ou vers une case de couchage commune.
            float bestDistance = float.MinValue;
            var bestCandidates = new List<Vector2>();
            foreach (var candidate in candidates)
            {
                float nearestResidentDistance = otherResidents.Min(position =>
                    Vector2.DistanceSquared(candidate, position));
                if (nearestResidentDistance > bestDistance + 0.01f)
                {
                    bestDistance = nearestResidentDistance;
                    bestCandidates.Clear();
                    bestCandidates.Add(candidate);
                }
                else if (MathF.Abs(nearestResidentDistance - bestDistance) <= 0.01f)
                {
                    bestCandidates.Add(candidate);
                }
            }

            return bestCandidates[Random.Shared.Next(bestCandidates.Count)];
        }

        private void StartSitting()
		{
			_walkingToBench = false;
			ResetMoveMomentum();
			AiState = NpcAiState.Sitting;
			AnimState = "sit";
			WorldPos = _benchSeatVisualPos;
			_sitTimer = (float)(Random.Shared.NextDouble() * 10f + 8f);
			// La clé est déjà stockée dans _reservedBench lors de la réservation
		}

        private void StopSitting()
		{
			if (_reservedBench.HasValue)
				World.ReleaseBench(_reservedBench.Value);
			_reservedBench = null;
			_walkingToBench = false;
			AiState = NpcAiState.Idle;
			AnimState = "idle";
			AiTimer = (float)(Random.Shared.NextDouble() * 2f + 1f);
			
			//  Chercher une position libre à côté du banc
			Vector2 newPos = FindFreePositionNear(_benchSeatVisualPos);
			if (newPos != Vector2.Zero)
			{
				WorldPos = newPos;
			}
			else
			{
				// Fallback : se déplacer un peu plus loin
				WorldPos = _benchSeatVisualPos + new Vector2(Program.TileSize * 0.8f, 0);
			}
		}

		//  Nouvelle méthode pour trouver une position libre autour du banc
		private Vector2 FindFreePositionNear(Vector2 centerPos)
		{
			int ts = Program.TileSize;
			float searchRadius = ts * 1.5f;
			int attempts = 0;
			int maxAttempts = 50;
			
			while (attempts < maxAttempts)
			{
				attempts++;
				float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
				float distance = ts * 0.6f + (float)(Random.Shared.NextDouble() * (searchRadius - ts * 0.6f));
				Vector2 candidate = centerPos + new Vector2(
					MathF.Cos(angle) * distance,
					MathF.Sin(angle) * distance
				);
				
				int tileX = (int)(candidate.X / ts);
				int tileY = (int)(candidate.Y / ts);
				
				// Vérifier que la position est valide
				int groundId = World.GetGroundTileIdAt(tileX, tileY);
				var groundTile = WorldTileRegistry.GetTile(groundId);
				if (groundTile == null || !groundTile.Walkable) continue;
				
				// Vérifier qu'il n'y a pas d'obstacle
				if (World.GetObjectIdAt(tileX, tileY) != 0) continue;
				
				// Vérifier qu'on ne reste pas sur le banc (pas de collision)
				if (World.IsCollidingEntity(candidate, Program.GetDestroyedObjects(), Species))
					continue;
				
				return candidate;
			}
			
			return Vector2.Zero;
		}


        //  Choisit un point de destination valide (sol praticable, pas d'objet bloquant)
        // autour d'un point d'ancrage, pour que le wander utilise l'IA de pathfinding
        // au lieu de foncer dans une direction aléatoire.
        private Vector2 PickWanderDestination(Vector2 anchor, float minRadius, float maxRadius)
        {
            int ts = Program.TileSize;
            int attempts = 0;
            int maxAttempts = 20;

            while (attempts < maxAttempts)
            {
                attempts++;
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                float distance = minRadius + (float)(Random.Shared.NextDouble() * (maxRadius - minRadius));
                Vector2 candidate = anchor + new Vector2(
                    MathF.Cos(angle) * distance,
                    MathF.Sin(angle) * distance
                );

                int tileX = (int)(candidate.X / ts);
                int tileY = (int)(candidate.Y / ts);

                int groundId = World.GetGroundTileIdAt(tileX, tileY);
                var groundTile = WorldTileRegistry.GetTile(groundId);
                if (groundTile == null || !groundTile.Walkable) continue;

                if (World.GetObjectIdAt(tileX, tileY) != 0) continue;

                return candidate;
            }

            // Repli : rester sur place, l'IA redécidera au prochain cycle
            return anchor;
        }

    }
}
