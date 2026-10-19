#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public partial class Entity
    {
        // AI movement and unified combat entry points
        // ==================== IA - MOUVEMENTS ====================
        //  Le trajet "rentrer à la maison" (AiState.GoHome) n'a plus de logique dédiée : il
        // passe par le même appel générique à Pathfinder.FindPath(WorldPos, AiTarget, ...) que
        // WalkToTarget/Chase/Wander, dans Update, avec une destination choisie parmi les cases
        // praticables de l'intérieur. La tuile de la porte est traitée comme franchissable par le
        // pathfinder (voir World.IsDoorTile dans Pathfinding.IsSingleTileWalkable), donc l'A*
        // peut traverser l'entrée et contourner réellement les murs au lieu de viser le seuil.

        //  Liste UNIQUE des états "le PNJ a une intention de déplacement et doit suivre
        //  _currentPath / MoveTowardTarget". Avant, cette même liste était recopiée à la main
        //  à deux endroits (garde-fou de vitesse, puis recalcul de chemin) : un état de
        //  déplacement ajouté plus tard risquait de n'être ajouté qu'à l'un des deux, laissant
        //  le PNJ "penser" à un objectif sans jamais recalculer de chemin ni avoir de vitesse.
        //  Désormais il n'y a qu'un seul endroit à mettre à jour quand un nouvel état de
        //  déplacement est introduit.
        private static bool IsMovementState(NpcAiState state)
        {
            switch (state)
            {
                case NpcAiState.WalkToTarget:
                case NpcAiState.Chase:
                case NpcAiState.GoHome:
                case NpcAiState.Wander:
                case NpcAiState.Follow:
                case NpcAiState.GoToChestWithdraw:
                case NpcAiState.GoToChestDeposit:
                case NpcAiState.GoToInfirmary:
                case NpcAiState.GoToMerchant:
                case NpcAiState.GoToGroundItem:
                case NpcAiState.GoToVillageCenter:
                    return true;
                default:
                    return false;
            }
        }

        void MoveTowardTarget(float dt, HashSet<string> destroyed, float arrivalRadius = 65f)
        {
            if (AiTarget == WorldPos)
            {
                if (AiState == NpcAiState.WalkToTarget)
                    AiTimer = Math.Min(AiTimer, RandomRange(1f, 3f)); // pas de longue immobilité (timer de 20s hérité)
                AiState = NpcAiState.Idle;
                return;
            }

            // Aucun chemin A* disponible : attendre le prochain recalcul plutôt que de
            // se diriger directement vers la cible, ce qui ferait traverser les obstacles.
            // Cela concerne aussi les trajets sociaux, les balades et les suivis, pas seulement
            // le retour à la maison.
            if (_currentPath.Count == 0)
            {
                //  Les états de combat ne doivent pas être annulés simplement parce qu'il n'y a
                //  plus de nœud de chemin et que la cible est à portée d'attaque. Le combat a sa
                //  propre logique d'attaque / télégraph et doit pouvoir rester en "Chase" tant
                //  que le joueur est dans la zone de combat, sans se figer sur une tuile.
                if (AiState == NpcAiState.Chase)
                {
                    AnimState = "walk";
                    return;
                }

                if (Vector2.DistanceSquared(WorldPos, AiTarget) < arrivalRadius * arrivalRadius)
                {
                    //  Arrivé d'emblée à portée (case voisine) : le fermier agit tout de suite au lieu de
                    // passer en Idle 1-3 s et de dépendre d'un nouveau Decide().
                    if (AiState == NpcAiState.WalkToTarget && IsFarmerOnShift && !_walkingToBench)
                    {
                        HandleFarmerArrival();
                        return;
                    }

                    if (AiState == NpcAiState.WalkToTarget)
                        AiTimer = Math.Min(AiTimer, RandomRange(1f, 3f)); // pas de longue immobilité (timer de 20s hérité)
                    AiState = NpcAiState.Idle;
                    AiTarget = WorldPos;
                    AnimState = "idle";
                    return;
                }

                AnimState = "idle";
                return;
            }

            float effectiveArrivalRadius = AiState == NpcAiState.Chase
                ? Math.Max(AttackHitRange, arrivalRadius)
                : arrivalRadius;

            Vector2 target = AiTarget;

            // If we have a computed path, follow its first node
            if (_currentPath.Count > 0)
            {
                //  CORRECTIF : le tout premier nœud d'un chemin fraîchement calculé est TOUJOURS
                // la position exacte du PNJ au moment du calcul (voir Pathfinder.ReconstructPath,
                // qui force path[0] = start), pas une vraie case à atteindre. Or FindPathInternal
                // ne valide JAMAIS la marchabilité de sa propre case de départ (seuls les voisins
                // explorés passent par grid.IsBlocked) : un chemin est donc renvoyé même quand le
                // PNJ se tient actuellement sur une case qui échouerait le test de clearance
                // (une entité large - cheval, vache... - collée à un mur ou un arbre exige que les
                // cases voisines soient libres aussi). La revalidation ci-dessous, elle, applique
                // ce test complet : sur ce nœud d'ancrage, elle échouait donc perpétuellement sur
                // la propre position du PNJ, jamais sur une vraie case à venir. Résultat observé :
                // chemin vidé (_currentPath.Clear()) puis recalculé immédiatement (_pathRecalcTimer
                // = 0f) à chaque frame, retrouvant systématiquement le même chemin (rien n'a
                // bougé) et échouant de la même façon - le PNJ ne progresse jamais, AiTarget (ligne
                // rouge debug) reste affiché mais _currentPath (ligne verte) est vide à chaque
                // rendu, et AnimState oscille walk/idle au gré du quota global de calcul de chemin
                // (TryReservePathRequest). Un simple coup de pouce du joueur déplace le PNJ hors de
                // cette case limite et débloque tout, ce qui confirmait le diagnostic. On avance
                // donc directement sur ce nœud d'ancrage (rien à valider, le PNJ y est déjà) avant
                // toute revalidation, exactement comme le fera de toute façon la logique d'arrivée
                // de nœud juste après pour tout nœud à distance quasi nulle.
                if (Vector2.DistanceSquared(_currentPath[0], WorldPos) <= 0.01f)
                    _currentPath.RemoveAt(0);
            }

            if (_currentPath.Count > 0)
            {
                //  Revalidation légère : si la case visée par le prochain nœud vient de devenir
                // bloquée (objet posé, mur reconstruit, etc.) depuis le calcul du chemin, on ne
                // continue pas à foncer dessus en attendant le prochain recalcul (jusqu'à 0.5s,
                // voir _pathRecalcTimer) — c'est exactement ce qui donnait l'impression que le
                // PNJ "traversait" l'obstacle : la ligne verte pointait vers une case qui n'était
                // plus praticable, et MoveTowardTarget continuait de s'y diriger jusqu'à ce que
                // IsCollidingEntity le stoppe physiquement dessus.
                if (!Pathfinder.IsWorldPositionWalkable(_currentPath[0], destroyed, Species))
                {
                    _currentPath.Clear();
                    _pathRecalcTimer = 0f;
                    AnimState = "walk";
                    return;
                }
                target = _currentPath[0];
            }

            Vector2 dir = target - WorldPos;
            float distanceToTarget = dir.Length();

            //  Le rayon d'arrivée à un nœud doit tenir compte de la taille réelle de la hitbox
            // de l'espèce (un cheval ou une vache ne peut pas "raser" un coin de mur comme une poule) :
            // plus l'entité est large, plus elle a besoin de marge pour ne pas accrocher les angles.
            Vector2 entityDim = World.GetEntityDimensions(Species);
            float entityHalfDiag = MathF.Sqrt(entityDim.X * entityDim.X + entityDim.Y * entityDim.Y) * 0.5f;
            float nodeArrivalRadius = MathF.Max(18f, entityHalfDiag * 0.75f);
            if (_currentPath.Count > 0)
            {
                // If close enough to the node, pop it and proceed
                if (distanceToTarget < nodeArrivalRadius)
                {
                    _currentPath.RemoveAt(0);
                    if (_currentPath.Count == 0)
                    {
                        // If this was the final node, check final arrival
                        if (Vector2.DistanceSquared(WorldPos, AiTarget) < (effectiveArrivalRadius) * (effectiveArrivalRadius))
                        {
                            if (AiState == NpcAiState.GoHome
                                || AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToChestWithdraw
                                || AiState == NpcAiState.GoToMerchant || AiState == NpcAiState.GoToGroundItem
                                || AiState == NpcAiState.GoToVillageCenter)
                            {
                                _currentPath.Clear();
                                return;
                            }

                            if (AiState != NpcAiState.Chase)
                            {
                                if (_walkingToBench && _reservedBench.HasValue)
                                    StartSitting();
                                else if (IsFarmerOnShift)
                                {
                                    HandleFarmerArrival();
                                }
                                else
                                {
                                    ReleaseCurrentGoalLock();
                                    AiState = NpcAiState.Idle;
                                    AnimState = "idle";
                                }
                            }
                            _currentPath.Clear();
                            return;
                        }
                    }
                    // continue to next update to compute movement towards next node
                }
            }

            // Recompute direction if node was popped
            if (_currentPath.Count > 0)
            {
                dir = _currentPath[0] - WorldPos;
                distanceToTarget = dir.Length();
            }

            // If close enough to final target
            if (_currentPath.Count == 0 && distanceToTarget < effectiveArrivalRadius)
            {
                if (AiState == NpcAiState.GoHome
                    || AiState == NpcAiState.GoToChestDeposit || AiState == NpcAiState.GoToChestWithdraw
                    || AiState == NpcAiState.GoToMerchant || AiState == NpcAiState.GoToGroundItem
                    || AiState == NpcAiState.GoToVillageCenter)
                {
                    _currentPath.Clear();
                    return;
                }

                if (AiState != NpcAiState.Chase)
                {
                    if (_walkingToBench && _reservedBench.HasValue)
                    {
                        StartSitting();
                    }
                    else if (IsFarmerOnShift)
                                {
                                    HandleFarmerArrival();
                                }
                    else
                    {
                        ReleaseCurrentGoalLock();
                        AiState = NpcAiState.Idle;
                        AnimState = "idle";
                        AiTimer = (float)(Random.Shared.NextDouble() * 2f + 1f);
                    }
                }
                _currentPath.Clear();
                return;
            }

            if (distanceToTarget > 0.01f)
                dir /= distanceToTarget;
            else
                dir = new Vector2(1, 0);

            //  Un PNJ à dos de monture se déplace à la vitesse de sa monture plutôt qu'à la
            // sienne (typiquement plus rapide) - voir TryMountOwnedPet.
            float effectiveSpeed = Speed;
            if (IsRidingMount && OwnedMount != null && OwnedMount.IsAlive)
                effectiveSpeed = Math.Max(effectiveSpeed, OwnedMount.RunSpeed);

            //  MOMENTUM : au lieu de se téléporter à la vitesse voulue dans la direction exacte du
            // nœud, la vitesse réelle rejoint progressivement la vitesse voulue (voir
            // SteerVelocity). Le sprite, lui, regarde toujours dans le sens de l'intention.
            float desiredSpeed = IsInWater ? effectiveSpeed * 0.5f : effectiveSpeed;
            _moveVelocity = SteerVelocity(dir * desiredSpeed, dt, MOVE_ACCEL_TIME);
            _steeredThisFrame = true;
            Vector2 move = _moveVelocity * dt;
            Facing = dir.X >= 0 ? 1f : -1f;

            // Si l'entité chevauche sa monture, elle doit être assise
            if (IsRidingMount)
            {
                AnimState = "sit";
            }
            else
            {
                float runThreshold = BaseSpeed * 1.2f;
                if (Speed > runThreshold)
                    AnimState = "run";
                else
                    AnimState = "walk";
            }

            Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
            bool movedX = false;
            if (!World.IsCollidingEntity(newPosX, destroyed, Species))
            {
                WorldPos.X += move.X;
                movedX = true;
            }
            else
            {
                _moveVelocity.X = 0f; // mur sur l'axe X : l'élan ne doit pas continuer à pousser dedans
            }

            Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
            bool movedY = false;
            if (!World.IsCollidingEntity(newPosY, destroyed, Species))
            {
                WorldPos.Y += move.Y;
                movedY = true;
            }
            else
            {
                _moveVelocity.Y = 0f;
            }

            if (!movedX && !movedY)
            {
                ResetMoveMomentum();
                //  Blocage complet (coin de mur) : avant d'abandonner et de recalculer tout le
                // chemin, on tente un léger glissement latéral perpendiculaire à la direction
                // voulue. Ça évite l'effet "collé/accroché" contre l'angle et laisse l'entité
                // se dégager naturellement de la géométrie qui dépasse sa hitbox.
                Vector2 tangent = new Vector2(-dir.Y, dir.X);
                float slideDist = MathF.Max(4f, (IsInWater ? Speed * 0.5f : Speed) * dt);

                Vector2 slidePosPositive = WorldPos + tangent * slideDist;
                Vector2 slidePosNegative = WorldPos - tangent * slideDist;

                bool positiveCloser = Vector2.DistanceSquared(slidePosPositive, target) <= Vector2.DistanceSquared(slidePosNegative, target);
                Vector2 firstTry = positiveCloser ? slidePosPositive : slidePosNegative;
                Vector2 secondTry = positiveCloser ? slidePosNegative : slidePosPositive;

                if (!World.IsCollidingEntity(firstTry, destroyed, Species))
                {
                    WorldPos = firstTry;
                }
                else if (!World.IsCollidingEntity(secondTry, destroyed, Species))
                {
                    WorldPos = secondTry;
                }
                else if (_currentPath.Count > 0)
                {
                    // Vraiment coincé : sortir d'une position de spawn invalide avant de recalculer le chemin.
                    // La recherche teste les tuiles par distance croissante et valide la hitbox réelle.
                    Vector2? recoveryPosition = Pathfinder.FindNearestWalkablePosition(WorldPos, destroyed, Species);
                    if (recoveryPosition.HasValue)
                        WorldPos = recoveryPosition.Value;

                    // Abandonner ce chemin et en recalculer un nouveau immédiatement.
                    _currentPath.Clear();
                    _pathRecalcTimer = 0f;
                }
            }
        }

        void PerformMovement(float dt, HashSet<string> destroyed, float arrivalRadius = 65f)
        {
            if (IsSlime)
                UpdateSlimeMovement(dt, destroyed);
            else
                MoveTowardTarget(dt, destroyed, arrivalRadius);
        }

        void Wander(float dt, HashSet<string> destroyed)
        {
            if (IsBird && _birdFlightState == BirdFlightState.Grounded)
            {
                _birdRoostDecisionTimer -= dt;
                if (_birdRoostDecisionTimer <= 0f)
                {
                    _birdRoostDecisionTimer = RandomRange(5f, 12f);
                    if (Random.Shared.NextDouble() < 0.45
                        && World.TryFindNearbyBirdRoost(WorldPos, 90f, out int treeX, out int treeY))
                    {
                        _requestedBirdRoost = (treeX, treeY);
                        return;
                    }
                }
            }

            //  Envol spontané, sans menace : donne un peu de vie au monde (un oiseau qui
            // s'envole brièvement puis se repose plus loin), indépendamment de toute fuite.
            if (IsBird && _birdFlightState == BirdFlightState.Grounded && Random.Shared.NextDouble() < 0.0015)
                StartBirdTakeoff();

            // Petite chance d'interrompre la balade pour repartir en Idle (variation naturelle du comportement)
            if (Random.Shared.NextDouble() < 0.004)
            {
                AiState = NpcAiState.Idle;
                AiTimer = (float)(Random.Shared.NextDouble() * 2f + 1f);
                AnimState = "idle";
                _currentPath.Clear();
                return;
            }

            if (UsesMeanderWander)
            {
                MeanderWander(dt, destroyed);
                return;
            }

            //  Le wander suit désormais le chemin calculé par le pathfinding vers AiTarget,
            // exactement comme WalkToTarget, au lieu d'avancer en ligne droite dans une direction
            // aléatoire (ce qui causait les NPC coincés contre les murs/obstacles).
            MoveTowardTarget(dt, destroyed, arrivalRadius: 30f);
        }

        private static float GetWanderNoiseGradient(int index)
        {
            unchecked
            {
                uint hash = (uint)index * 2654435761u;
                hash ^= hash >> 15;
                hash *= 2246822519u;
                hash ^= hash >> 13;
                return (hash & 0xFFFF) / 32767.5f - 1f;
            }
        }

        private static float GetWanderNoise(float value)
        {
            int index = (int)MathF.Floor(value);
            float fraction = value - index;
            float smooth = fraction * fraction * fraction * (fraction * (fraction * 6f - 15f) + 10f);
            float left = GetWanderNoiseGradient(index) * fraction;
            float right = GetWanderNoiseGradient(index + 1) * (fraction - 1f);
            return (left + (right - left) * smooth) * 2f;
        }

        private void MeanderWander(float dt, HashSet<string> destroyed)
        {
            float speed = BaseSpeed > 0.01f ? BaseSpeed : SpeciesData.GetSpeed(Species);
            Speed = speed;
            if (speed <= 0.01f)
            {
                AnimState = "idle";
                return;
            }

            float tileSize = Program.TileSize;
            if (!_wanderActive)
            {
                _wanderActive = true;
                if (!_wanderAnchorSet
                    || Vector2.DistanceSquared(WorldPos, _wanderAnchor) > tileSize * tileSize * 196f)
                {
                    _wanderAnchor = HomePosition != Vector2.Zero ? HomePosition : WorldPos;
                    if (Vector2.DistanceSquared(WorldPos, _wanderAnchor) > tileSize * tileSize * 196f)
                        _wanderAnchor = WorldPos;
                    _wanderAnchorSet = true;
                }

                _wanderBaseHeading = RandomRange(0f, MathF.Tau);
                _wanderNoiseSeed = RandomRange(0f, 1000f);
                _wanderNoiseTime = RandomRange(0f, 100f);
                _contextMap ??= new ContextMap();
                _contextMap.Reset();
                _currentPath.Clear();
                _pathRecalcTimer = 0f;
            }

            ContextMap context = _contextMap ??= new ContextMap();
            _wanderNoiseTime += dt * WANDER_NOISE_RATE;
            float sway = GetWanderNoise(_wanderNoiseTime + _wanderNoiseSeed) * WANDER_MAX_SWAY;
            float heading = _wanderBaseHeading + sway;
            Vector2 desiredDirection = new(MathF.Cos(heading), MathF.Sin(heading));

            Vector2 toAnchor = _wanderAnchor - WorldPos;
            float anchorDistance = toAnchor.Length();
            float anchorPull = Math.Clamp((anchorDistance - tileSize * 2f) / (tileSize * 5f), 0f, 1f);
            anchorPull *= anchorPull;
            if (anchorPull > 0.001f && anchorDistance > 1f)
            {
                Vector2 blendedDirection = Vector2.Lerp(desiredDirection, toAnchor / anchorDistance, anchorPull * 0.9f);
                if (blendedDirection.LengthSquared() > 0.0001f)
                    desiredDirection = Vector2.Normalize(blendedDirection);
            }

            context.Timer -= dt;
            if (context.Timer <= 0f)
            {
                context.Timer = CONTEXT_WANDER_INTERVAL;
                context.BeginBuild();
                for (int i = 0; i < ContextMap.Slots; i++)
                    context.Interest[i] = MathF.Max(0f, Vector2.Dot(ContextMap.Directions[i], desiredDirection));

                BuildTerrainDanger(context, destroyed, new Vector2(float.MaxValue / 4f), onlyInterestingSlots: true);
                if (!context.Resolve(0.6f))
                {
                    _wanderBaseHeading += (Random.Shared.NextDouble() < 0.5 ? 1f : -1f)
                        * RandomRange(1.8f, 2.8f);
                }
                else if (Vector2.Dot(context.Direction, desiredDirection) < 0.64f)
                {
                    _wanderBaseHeading = MathF.Atan2(context.Direction.Y, context.Direction.X) - sway;
                }
            }

            Vector2 desiredVelocity = context.HasResult
                ? context.Direction * speed * (0.55f + 0.45f * context.Strength) * (IsInWater ? 0.5f : 1f)
                : Vector2.Zero;
            bool moved = MoveWithVelocity(dt, destroyed, desiredVelocity);
            _stuckProgressTimer = 0f;
            _stuckProgressCheckPos = WorldPos;

            if (moved && _moveVelocity.LengthSquared() > 4f)
            {
                if (MathF.Abs(_moveVelocity.X) > 1f)
                    Facing = _moveVelocity.X >= 0f ? 1f : -1f;
                AnimState = speed > BaseSpeed * 1.2f ? "run" : "walk";
                AiTarget = WorldPos + Vector2.Normalize(_moveVelocity) * 80f;
            }
            else
            {
                AnimState = "idle";
            }
        }

        void Flee(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            Vector2 fearTargetPos = GetFearTargetPosition();
            Vector2 fleeDir = WorldPos - fearTargetPos;
            if (fleeDir.Length() > 0.01f)
                fleeDir = Vector2.Normalize(fleeDir);
            else
                fleeDir = new Vector2(1, 0);

            //  Un oiseau qui fuit décolle logiquement plutôt que de courir au sol : dès que la
            // fuite démarre, on déclenche l'envol (StartBirdTakeoff ne fait rien s'il est déjà
            // en l'air), dans la direction opposée à la menace. Le déplacement horizontal
            // ci-dessous reste inchangé, seule la hauteur (gérée par UpdateBirdFlight, appelée
            // depuis Update()) change le rendu.
            if (IsBird)
                StartBirdTakeoff(fleeDir);

            Speed = RunSpeed;

            if (_currentPath.Count > 0)
            {
                MoveTowardTarget(dt, destroyed, arrivalRadius: 30f);
            }
            else
            {
                // Garder une tentative locale quand le pathfinder ne trouve pas encore
                // d'itinéraire, sans traverser les obstacles.
                Vector2 startPos = WorldPos;
                _moveVelocity = SteerVelocity(fleeDir * Speed, dt, MOVE_ACCEL_TIME);
                _steeredThisFrame = true;
                Vector2 move = _moveVelocity * dt;
                Facing = fleeDir.X >= 0 ? 1f : -1f;

                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                    WorldPos.X += move.X;
                else
                    _moveVelocity.X = 0f;

                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                    WorldPos.Y += move.Y;
                else
                    _moveVelocity.Y = 0f;

                bool moved = WorldPos != startPos;
                if (IsRidingMount)
                    AnimState = "sit";
                else if (moved)
                    AnimState = IsBirdFlying || Speed <= BaseSpeed * 1.2f ? "walk" : "run";
                else
                    AnimState = "idle";
            }

            float distToThreat = Vector2.Distance(WorldPos, fearTargetPos);

            if (distToThreat > GetFleeSafeRadius())
            {
                _continuousFearTimer = 0f;
                AiState = NpcAiState.LookAtPlayer;
                _lookAtPlayerTimer = 1.5f;
                Speed = SpeciesData.GetSpeed(Species);
            }
            else if (AiTimer <= 0f)
            {
                AiTimer = 2f;
            }
        }

        void LookAtPlayer(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            Vector2 fearTargetPos = GetFearTargetPosition();
            Facing = fearTargetPos.X >= WorldPos.X ? 1f : -1f;
            AnimState = "idle";

            _lookAtPlayerTimer -= dt;

            if (_lookAtPlayerTimer <= 0f)
            {
                IsAlerted = false;
                Decide();
            }
        }

        // ==================== IA DE COMBAT ====================
        //  Point d'entrée commun : aiguille vers un style de combat à distance (arc) ou de
        // mêlée selon l'arme actuellement portée. Les tamed animals / monstres sans arme
        // suivent toujours le comportement de mêlée (charge + morsure/griffe).
        private void DamageCombatTarget(Entity target)
        {
            if (!target.IsAlive || target.IsInvulnerable) return;

            int damage = Math.Max(1, Attack);
            damage = target.Equipment?.GetDamageAfterArmor(damage) ?? damage;
            target.CurrentHP = Math.Max(0, target.CurrentHP - damage);
            target.OnHit(WorldPos, this, attackerIsPlayer: false);

            Vector2 knockbackDirection = target.WorldPos - WorldPos;
            if (knockbackDirection.LengthSquared() > 0.01f)
                knockbackDirection = Vector2.Normalize(knockbackDirection);
            else
                knockbackDirection = new Vector2(Facing, 0f);
            target.ApplyKnockback(knockbackDirection, 100f);
            Program.AddFloatingDamage(
                World.GetEntityGroundPosition(target.WorldPos) - new Vector2(0f, 20f),
                damage, damage >= 10);

            if (!target.IsAlive)
            {
                foreach (var (id, quantity, metadata) in GameData.GetAnimalDrops(target.Species))
                    Program.GiveItemToPlayer(id, quantity, target.WorldPos, null, metadata);
            }
        }

        void ChaseBehavior(float dt, HashSet<string> destroyed, Vector2 targetPos)
        {
            if (IsSlime)
            {
                AiTarget = targetPos;
                UpdateSlimeMovement(dt, destroyed);
                return;
            }

            CombatBehavior(dt, destroyed, targetPos);
        }

        // ==================== COMBAT : POINT D'ENTRÉE UNIQUE ====================
        void CombatBehavior(float dt, HashSet<string> destroyed, Vector2 targetPos)
        {
            CombatProfile profile = GetCombatProfile();

            if (_attackPhase != AttackPhase.None)
            {
                TickAttackPhases(dt, destroyed, targetPos, profile);
                SyncAttackAnimation();
                return;
            }

            if (_postAttackCooldown > 0f) _postAttackCooldown -= dt;

            if (profile.Kind == CombatImpactKind.Arrow)
                ArcherPositioning(dt, destroyed, targetPos, profile);
            else
                MeleePositioning(dt, destroyed, targetPos, profile);

            SyncAttackAnimation();
        }

        private float GetDistanceToCombatTarget(Vector2 targetPos)
        {
            Rectangle targetDamageHitbox = _combatTargetEntity != null
                ? World.GetEntityDamageHitbox(_combatTargetEntity, Program.TileSize)
                : World.GetEntityDamageHitbox(targetPos, "human", Program.TileSize);
            return World.GetDistanceToDamageHitbox(World.GetEntityGroundPosition(WorldPos), targetDamageHitbox);
        }

        private void BeginCombatIfNeeded()
        {
            bool targetChanged = !_combatActive
                || !ReferenceEquals(_engagementTargetEntity, _combatTargetEntity)
                || (_combatTargetEntity == null && _engagementPlayerConnectionId != AiTargetConnectionId);
            if (!targetChanged)
                return;

            ResetCombatState();
            _combatActive = true;
            _engagementTargetEntity = _combatTargetEntity;
            _engagementPlayerConnectionId = _combatTargetEntity == null ? AiTargetConnectionId : -1;
            _engageWaitTimer = RandomRange(ENGAGE_HESITATION_MIN, ENGAGE_HESITATION_MAX);
            _orbitRadiusFactor = RandomRange(0.9f, 1.25f);
            _strafeDir = Random.Shared.NextDouble() < 0.5 ? -1 : 1;
            _strafeFlipTimer = RandomRange(1.8f, 4.5f);
            _contextMap ??= new ContextMap();
            _contextMap.Reset();
        }

        private void ResetCombatState()
        {
            _combatActive = false;
            _isEngaging = false;
            _engageWaitTimer = 0f;
            _engageTimeout = 0f;
            _engagementTargetEntity = null;
            _engagementPlayerConnectionId = -1;
            _contextMap?.Reset();
        }

        private bool HasSameEngagementTarget(Entity other)
        {
            if (_engagementTargetEntity != null)
                return ReferenceEquals(other._engagementTargetEntity, _engagementTargetEntity);

            return other._engagementTargetEntity == null
                && other._engagementPlayerConnectionId == _engagementPlayerConnectionId;
        }

        private int CountCurrentTargetEngagers()
        {
            int count = 0;
            foreach (Entity other in Program.entities)
            {
                if (ReferenceEquals(other, this) || !other.IsAlive || !other._combatActive)
                    continue;
                if (!other._isEngaging && other._attackPhase == AttackPhase.None)
                    continue;
                if (HasSameEngagementTarget(other))
                    count++;
            }

            return count;
        }

        private void TickEngagement(float dt, float distance, float acquireDistance)
        {
            if (_engageWaitTimer > 0f)
                _engageWaitTimer = Math.Max(0f, _engageWaitTimer - dt);

            if (_isEngaging)
            {
                _engageTimeout -= dt;
                if (_engageTimeout <= 0f)
                {
                    _isEngaging = false;
                    _engageWaitTimer = RandomRange(0.35f, 0.8f);
                }
                return;
            }

            if (_postAttackCooldown > 0f || _engageWaitTimer > 0f || distance > acquireDistance)
                return;

            int maxEngagers = Math.Max(1, MaxSimultaneousEngagers);
            if (CountCurrentTargetEngagers() >= maxEngagers)
            {
                _engageWaitTimer = RandomRange(0.35f, 0.8f);
                return;
            }

            _isEngaging = true;
            _engageTimeout = ENGAGE_TIMEOUT;
        }

        private float GetOrbitDistance()
        {
            return MathF.Max(ORBIT_MIN_EDGE_DISTANCE, AttackHitRange * 1.8f) * _orbitRadiusFactor;
        }

        private bool ContextProbeBlocked(Vector2 position, HashSet<string> destroyed, Vector2? ignoredTarget, float ignoreRadius)
        {
            if (ignoredTarget.HasValue
                && Vector2.DistanceSquared(position, ignoredTarget.Value) < ignoreRadius * ignoreRadius)
                return false;

            return World.IsCollidingEntity(position, destroyed, Species);
        }

        private bool HasClearLine(Vector2 from, Vector2 to, HashSet<string> destroyed)
        {
            Vector2 delta = to - from;
            float length = delta.Length();
            if (length < 50f)
                return true;

            Vector2 step = delta / length * 22f;
            Vector2 probe = from;
            float probeLength = MathF.Min(length, 220f);
            for (float travelled = 22f; travelled < probeLength; travelled += 22f)
            {
                probe += step;
                if (ContextProbeBlocked(probe, destroyed, to, 26f))
                    return false;
            }

            return true;
        }

        private void BuildTerrainDanger(ContextMap context, HashSet<string> destroyed, Vector2? ignoredTarget,
            bool onlyInterestingSlots)
        {
            Vector2 dimensions = World.GetEntityDimensions(Species);
            float halfDiagonal = MathF.Sqrt(dimensions.X * dimensions.X + dimensions.Y * dimensions.Y) * 0.5f;
            float nearDistance = MathF.Max(18f, halfDiagonal * 0.9f);
            float farDistance = nearDistance * 2.2f;

            for (int i = 0; i < ContextMap.Slots; i++)
            {
                if (onlyInterestingSlots && context.Interest[i] <= 0.001f)
                    continue;

                Vector2 direction = ContextMap.Directions[i];
                if (ContextProbeBlocked(WorldPos + direction * nearDistance, destroyed, ignoredTarget, 22f))
                    context.RawDanger[i] = 1f;
                else if (ContextProbeBlocked(WorldPos + direction * farDistance, destroyed, ignoredTarget, 22f))
                    context.RawDanger[i] = 0.5f;
            }

            context.FinalizeDanger();
        }

        private void AddSeparationInterest(ContextMap context)
        {
            int neighbours = 0;
            foreach (Entity other in Program.entities)
            {
                if (neighbours >= 6)
                    break;
                if (ReferenceEquals(other, this) || !other.IsAlive || ReferenceEquals(other, _combatTargetEntity))
                    continue;

                Vector2 away = WorldPos - other.WorldPos;
                float distanceSquared = away.LengthSquared();
                if (distanceSquared >= SEPARATION_RADIUS * SEPARATION_RADIUS)
                    continue;

                neighbours++;
                float distance = MathF.Sqrt(distanceSquared);
                Vector2 awayDirection = distance > 0.01f ? away / distance : new Vector2(1f, 0f);
                float proximity = 1f - distance / SEPARATION_RADIUS;
                for (int i = 0; i < ContextMap.Slots; i++)
                {
                    Vector2 slotDirection = ContextMap.Directions[i];
                    float angle = MathF.Acos(Math.Clamp(Vector2.Dot(slotDirection, awayDirection), -1f, 1f));
                    float angleWeight = Math.Clamp(1f - MathF.Abs(angle - 0.9f) / 1.6f, 0f, 1f);
                    float cross = awayDirection.X * slotDirection.Y - awayDirection.Y * slotDirection.X;
                    float sideBias = cross * _strafeDir > 0f ? 1f : 0.7f;
                    context.Interest[i] += 0.9f * proximity * angleWeight * sideBias;
                }
            }
        }

        private void CombatContextMove(float dt, HashSet<string> destroyed, Vector2 targetPos, float distance,
            float inner, float outer, float approachBand, bool charging)
        {
            ContextMap context = _contextMap ??= new ContextMap();
            _strafeFlipTimer -= dt;
            if (_strafeFlipTimer <= 0f)
            {
                _strafeFlipTimer = RandomRange(1.8f, 4.5f);
                if (Random.Shared.NextDouble() < 0.6)
                    _strafeDir = -_strafeDir;
            }

            context.Timer -= dt;
            if (context.Timer <= 0f)
            {
                context.Timer = CONTEXT_UPDATE_INTERVAL;
                context.BeginBuild();

                Vector2 toTarget = targetPos - WorldPos;
                float targetDistance = toTarget.Length();
                Vector2 targetDirection = targetDistance > 0.01f
                    ? toTarget / targetDistance
                    : new Vector2(Facing, 0f);
                Vector2 goalDirection = targetDirection;
                if (!HasClearLine(WorldPos, targetPos, destroyed))
                {
                    for (int i = 0; i < _currentPath.Count && i < 3; i++)
                    {
                        Vector2 toNode = _currentPath[i] - WorldPos;
                        if (toNode.LengthSquared() > 100f)
                        {
                            goalDirection = Vector2.Normalize(toNode);
                            break;
                        }
                    }
                }

                float approach = charging
                    ? 1f
                    : Math.Clamp((distance - outer) / MathF.Max(1f, approachBand), 0f, 1f);
                float retreat = charging
                    ? 0f
                    : Math.Clamp((inner - distance) / MathF.Max(1f, inner * 0.5f), 0f, 1f);
                float strafe = charging ? 0.15f : 1f - approach;
                for (int i = 0; i < ContextMap.Slots; i++)
                {
                    Vector2 direction = ContextMap.Directions[i];
                    float goalDot = Vector2.Dot(direction, goalDirection);
                    float targetDot = Vector2.Dot(direction, targetDirection);
                    float cross = targetDirection.X * direction.Y - targetDirection.Y * direction.X;
                    float sideBias = cross * _strafeDir > 0f ? 1f : 0.25f;
                    context.Interest[i] = approach * MathF.Max(0f, goalDot)
                        + retreat * 1.2f * MathF.Max(0f, -targetDot)
                        + strafe * MathF.Abs(cross) * sideBias;
                }

                AddSeparationInterest(context);
                BuildTerrainDanger(context, destroyed, targetPos, onlyInterestingSlots: false);
                if (!context.Resolve(CONTEXT_SMOOTHING))
                    _strafeDir = -_strafeDir;
            }

            _stuckProgressTimer = 0f;
            _stuckProgressCheckPos = WorldPos;
            float approachSpeed = charging
                ? 1f
                : Math.Clamp((distance - outer) / MathF.Max(1f, approachBand), 0f, 1f);
            float speed = charging ? RunSpeed : MathHelper.Lerp(BaseSpeed, RunSpeed, approachSpeed);
            if (IsRidingMount && OwnedMount != null && OwnedMount.IsAlive)
                speed = Math.Max(speed, OwnedMount.RunSpeed);
            Speed = speed;

            Vector2 desiredVelocity = Vector2.Zero;
            if (context.HasResult)
            {
                float strengthFactor = 0.45f + 0.55f * context.Strength;
                desiredVelocity = context.Direction * speed * strengthFactor * (IsInWater ? 0.5f : 1f);
            }

            bool moved = MoveWithVelocity(dt, destroyed, desiredVelocity);
            if (IsRidingMount)
                AnimState = "sit";
            else if (moved && _moveVelocity.LengthSquared() > 9f)
                AnimState = speed > BaseSpeed * 1.2f ? "run" : "walk";
            else
                AnimState = "idle";
        }

        private bool MoveWithVelocity(float dt, HashSet<string> destroyed, Vector2 desiredVelocity)
        {
            _moveVelocity = SteerVelocity(desiredVelocity, dt, MOVE_ACCEL_TIME);
            _steeredThisFrame = true;

            Vector2 start = WorldPos;
            Vector2 move = _moveVelocity * dt;
            if (!World.IsCollidingEntity(WorldPos + new Vector2(move.X, 0f), destroyed, Species))
                WorldPos.X += move.X;
            else
                _moveVelocity.X = 0f;

            if (!World.IsCollidingEntity(WorldPos + new Vector2(0f, move.Y), destroyed, Species))
                WorldPos.Y += move.Y;
            else
                _moveVelocity.Y = 0f;

            return WorldPos != start;
        }

        // ---- Approche, orbite et déclenchement (mêlée) ----
        void MeleePositioning(float dt, HashSet<string> destroyed, Vector2 targetPos, CombatProfile p)
        {
            AiTarget = targetPos;
            float distToTarget = GetDistanceToCombatTarget(targetPos);
            BeginCombatIfNeeded();
            float orbit = GetOrbitDistance();
            TickEngagement(dt, distToTarget, orbit + ORBIT_APPROACH_BAND + 40f);
            Facing = targetPos.X >= WorldPos.X ? 1f : -1f;

            float triggerRange = AttackHitRange + (p.FrontOnly ? STRIKE_REACH_TOLERANCE : 0f);
            bool requiresPathfinding = p.Kind == CombatImpactKind.Spores || IsMounted;
            if (distToTarget > orbit + COMBAT_CONTEXT_RANGE
                || (requiresPathfinding && distToTarget > triggerRange))
            {
                Speed = RunSpeed;
                PerformMovement(dt, destroyed, requiresPathfinding ? triggerRange : orbit + 15f);
                AnimState = _currentPath.Count > 0 ? "run" : "idle";
                return;
            }

            if (_isEngaging && distToTarget <= triggerRange && _postAttackCooldown <= 0f)
            {
                _isEngaging = false;
                BeginAttack(targetPos);
                return;
            }

            if (requiresPathfinding && distToTarget <= triggerRange)
            {
                Speed = 0f;
                AnimState = IsRidingMount ? "sit" : "idle";
                return;
            }

            CombatContextMove(dt, destroyed, targetPos, distToTarget,
                inner: orbit - 25f, outer: orbit + 10f, approachBand: ORBIT_APPROACH_BAND, charging: _isEngaging);
        }

        // ---- Positionnement des archers : fenêtre de tir, recul, rotation autour de la cible ----
        void ArcherPositioning(float dt, HashSet<string> destroyed, Vector2 targetPos, CombatProfile p)
        {
            Vector2 myGround = World.GetEntityGroundPosition(WorldPos);
            Vector2 targetGround = World.GetEntityGroundPosition(targetPos);
            float dist = Vector2.Distance(myGround, targetGround);
            BeginCombatIfNeeded();
            TickEngagement(dt, dist, RANGED_PREFERRED_MAX + 110f);
            Facing = targetGround.X >= myGround.X ? 1f : -1f;

            if (dist > RANGED_PREFERRED_MAX + COMBAT_CONTEXT_RANGE)
            {
                AiTarget = targetPos;
                Speed = BaseSpeed;
                MoveTowardTarget(dt, destroyed, RANGED_PREFERRED_MAX * 0.8f);
                return;
            }

            if (_isEngaging && dist >= RANGED_PREFERRED_MIN && dist <= RANGED_PREFERRED_MAX
                && _postAttackCooldown <= 0f)
            {
                _isEngaging = false;
                BeginAttack(targetPos);
                return;
            }

            CombatContextMove(dt, destroyed, targetPos, dist,
                inner: RANGED_PREFERRED_MIN, outer: RANGED_PREFERRED_MAX, approachBand: 110f, charging: false);
        }

        private void BeginAttack(Vector2 targetPos)
        {
            _attackPhase = AttackPhase.Telegraph;
            _telegraphTimer = 0f;
            _hasDealtDamage = false;
            Speed = 0f;
            ResetMoveMomentum();

            Vector2 dirToTarget = targetPos - WorldPos;
            _lockedDirection = dirToTarget.LengthSquared() > 0.0001f
                ? Vector2.Normalize(dirToTarget)
                : new Vector2(Facing, 0f);

            if (IsGoblinMeleeStyle)
                PlayAttackSound();

            SetCombatAnim(GetCombatAnimName());
        }

        // ---- Automate d'attaque : Armement -> Frappe -> Récupération ----
        //  Le temps excédentaire d'une phase est reporté sur la suivante (phaseDt) : aucune perte
        //  d'une frame par transition, donc pas de dérive entre le geste et l'impact.
        void TickAttackPhases(float dt, HashSet<string> destroyed, Vector2 targetPos, CombatProfile p)
        {
            AiTarget = targetPos;
            float distToTarget = GetDistanceToCombatTarget(targetPos);
            string anim = GetCombatAnimName();
            float phaseDt = dt;

            // ---- Armement ----
            if (_attackPhase == AttackPhase.Telegraph)
            {
                _telegraphTimer += dt;

                if (p.Tracks && distToTarget > AttackHitRange && !(IsMounted && Rider != null))
                {
                    Speed = Math.Max(BaseSpeed * 0.5f, 60f);
                    PerformMovement(dt, destroyed, AttackHitRange);
                }
                else
                {
                    Speed = 0f;
                }

                // La poursuite peut orienter le sprite selon le détour du chemin ; en fin
                // d'armement, la frappe doit rester tournée vers la cible elle-même.
                if (p.LungeSpeed <= 0f && MathF.Abs(targetPos.X - WorldPos.X) > 8f)
                    Facing = targetPos.X >= WorldPos.X ? 1f : -1f;

                SetCombatAnim(anim);

                if (_telegraphTimer < p.Windup) return;

                phaseDt = _telegraphTimer - p.Windup;
                _attackPhase = AttackPhase.Charge;
                _chargeTimer = 0f;
                _hasDealtDamage = false;
            }

            // ---- Frappe ----
            if (_attackPhase == AttackPhase.Charge)
            {
                _chargeTimer += phaseDt;
                SetCombatAnim(anim);

                if (p.LungeSpeed > 0f)
                {
                    // Élan en ligne droite dans la direction verrouillée au début de l'armement.
                    Vector2 chargeMove = _lockedDirection * p.LungeSpeed * phaseDt;
                    Vector2 newPosX = WorldPos + new Vector2(chargeMove.X, 0f);
                    if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                        WorldPos.X += chargeMove.X;
                    Vector2 newPosY = WorldPos + new Vector2(0f, chargeMove.Y);
                    if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                        WorldPos.Y += chargeMove.Y;

                    if (_lockedDirection.X != 0f)
                        Facing = Math.Sign(_lockedDirection.X);
                }
                else
                {
                    Speed = 0f;
                }

                // IMPACT : une seule évaluation par attaque (touche ou rate, elle est consommée).
                if (!_hasDealtDamage)
                {
                    float impactDistance = p.LungeSpeed > 0f
                        ? GetDistanceToCombatTarget(targetPos)
                        : distToTarget;
                    bool impactNow = p.ImpactDelay >= 0f
                        ? _chargeTimer >= p.ImpactDelay
                        : IsStrikeOnTarget(p, impactDistance, targetPos);
                    if (impactNow)
                    {
                        _hasDealtDamage = true;
                        PerformAttackImpact(p, targetPos, impactDistance);
                    }
                }

                if (_chargeTimer < p.Strike) return;

                phaseDt = _chargeTimer - p.Strike;
                _attackPhase = AttackPhase.Recovery;
                _recoveryTimer = 0f;
            }

            // ---- Récupération (pieds plantés) ----
            if (_attackPhase == AttackPhase.Recovery)
            {
                _recoveryTimer += phaseDt;
                Speed = 0f;
                SetCombatAnim(anim);

                if (_recoveryTimer >= p.Recovery)
                {
                    _attackPhase = AttackPhase.None;
                    Speed = RunSpeed;
                    _postAttackCooldown = p.CooldownMin + (float)Random.Shared.NextDouble() * p.CooldownJitter;
                }
            }
        }

        // Le coup ne touche que si la cible est à portée (et devant l'attaquant pour les « duellistes »).
        private bool IsStrikeOnTarget(CombatProfile p, float distToTarget, Vector2 targetPos)
        {
            float reach = AttackHitRange + (p.FrontOnly ? STRIKE_REACH_TOLERANCE : 0f);
            if (distToTarget >= reach) return false;
            if (!p.FrontOnly) return true;
            float dx = targetPos.X - WorldPos.X;
            return MathF.Abs(dx) < 12f || Math.Sign(dx) == Math.Sign(Facing);
        }

        private void PerformAttackImpact(CombatProfile p, Vector2 targetPos, float distToTarget)
        {
            switch (p.Kind)
            {
                case CombatImpactKind.Arrow:
                {
                    Vector2 origin = World.GetEntityGroundPosition(WorldPos);
                    Vector2 shootDir = World.GetEntityGroundPosition(targetPos) - origin;
                    shootDir = shootDir.LengthSquared() > 0.0001f ? Vector2.Normalize(shootDir) : new Vector2(Facing, 0f);
                    Program.SpawnHostileArrow(origin, shootDir, RANGED_ARROW_SPEED, Attack,
                        _combatTargetEntity, this, AiTargetConnectionId);
                    return;
                }
                case CombatImpactKind.Spores:
                    // Le bolet ne frappe pas : il relâche des nuages de spores empoisonnées autour de lui
                    // (voir Program.SpawnPoisonCloudBurst), quoi qu'il arrive à sa cible.
                    Program.SpawnPoisonCloudBurst(WorldPos, this, AiTargetConnectionId);
                    Program.NotifyPlayerAttackedByEntity(this, AiTargetConnectionId);
                    return;
                default:
                {
                    if (!IsStrikeOnTarget(p, distToTarget, targetPos)) return;
                    float poisonDuration = string.Equals(Species, "scorpio", StringComparison.OrdinalIgnoreCase) ? 6f : 0f;
                    if (_combatTargetEntity != null)
                    {
                        DamageCombatTarget(_combatTargetEntity);
                    }
                    else
                    {
                        Program.DamagePlayerTarget(Attack, AiTargetConnectionId, GetDisplayName(),
                            Program.InferDeathCauseFromSpecies(Species), poisonDuration);
                        Program.NotifyPlayerAttackedByEntity(this, AiTargetConnectionId);
                        Program.ApplyScreenShake(p.Shake, 0.12f);
                    }
                    return;
                }
            }
        }

        // ---- Animation du squelette synchronisée sur l'automate ----
        //  Sans cela, l'animation "attack" tournait librement (frame et vitesse sans rapport avec
        //  les durées d'armement/frappe/récupération) : le geste visible était décalé du coup réel.
        private void SetCombatAnim(string anim) => AnimState = IsRidingMount ? "sit" : anim;

        private int GetSkeletonAnimFrameCount(string anim)
        {
            if (SpeciesData.Skeletons.TryGetValue(Species, out var skeleton))
            {
                var firstPart = skeleton.FirstOrDefault();
                if (firstPart != null && firstPart.Animations.TryGetValue(anim, out var frames))
                    return frames.Count;
            }
            return 0;
        }

        private string GetCombatAnimName() => GetSkeletonAnimFrameCount("attack") > 0 ? "attack" : "idle";

        private void SyncAttackAnimation()
        {
            if (_attackPhase == AttackPhase.None || IsRidingMount || AnimState != "attack") return;
            int frameCount = GetSkeletonAnimFrameCount("attack");
            if (frameCount <= 0) return;

            float x = Math.Clamp(GetAttackSwingProgress(), 0f, 1f) * frameCount;
            int frame = (int)x;
            if (frame >= frameCount) { frame = 0; x = 0f; }
            CurrentFrame = frame;
            AnimProgress = x - frame;
        }

    }
}
