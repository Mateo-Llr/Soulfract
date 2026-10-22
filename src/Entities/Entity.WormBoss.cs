#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public partial class Entity
    {
        // Worm boss state machine
        // ==================== WORM BOSS : state machine ====================
        private void UpdateWormBoss(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            if (!IsWorm) return;
            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);

            UpdateWormGroundMarks(dt);

            // Dégât reçu : si le ver est posé en surface, il est secoué puis se retire.
            if (_wmLastHP < 0) _wmLastHP = CurrentHP;
            if (CurrentHP < _wmLastHP && _wmState == WormBossState.SurfaceRecover)
            {
                _wmState = WormBossState.HitStagger;
                _wmTimer = 0f;
            }
            _wmLastHP = CurrentHP;
            // Initialiser le son de rumbling si nécessaire
            InitializeWormRumbleSound();

            switch (_wmState)
            {
                case WormBossState.IntroEmerging:
                {
                    Speed = 0f;
                    AnimState = "attack";
                    if (!_wmIntroStarted)
                    {
                        _wmIntroStarted = true;
                        _wmExitPoint = WorldPos;
                        _wmEntryPoint = _wmExitPoint;
                        _wmSurfacePoint = _wmExitPoint;
                        Facing = playerPos.X >= _wmExitPoint.X ? 1f : -1f;
                    }

                    _wmTimer += dt;
                    WorldPos = GetWormIntroSegmentPoses()[0].Position;
                    if (_wmTimer >= WM_INTRO_EMERGE_DURATION)
                    {
                        _wmState = WormBossState.IntroRoaring;
                        _wmTimer = 0f;
                        PlayWormRoarSound();
                        Program.ApplyScreenShake(9f, 0.75f);
                        SpawnKhamsinSmoke(_wmExitPoint, 1.5f);
                    }
                    return;
                }

                case WormBossState.IntroRoaring:
                {
                    Speed = 0f;
                    AnimState = "attack";
                    _wmTimer += dt;
                    WorldPos = GetWormIntroSegmentPoses()[0].Position;
                    if (_wmTimer >= WM_INTRO_ROAR_DURATION)
                    {
                        _wmState = WormBossState.IntroDiving;
                        _wmTimer = 0f;
                    }
                    return;
                }

                case WormBossState.IntroDiving:
                {
                    Speed = 0f;
                    AnimState = "attack";
                    _wmTimer += dt;
                    WorldPos = GetWormIntroSegmentPoses()[0].Position;
                    if (_wmTimer >= WM_INTRO_DIVE_DURATION)
                    {
                        _wmState = WormBossState.Underground;
                        _wmTimer = 0.2f;
                        WorldPos = _wmSurfacePoint;
                        _wmSurfacePoint = _wmExitPoint;
                        SpawnKhamsinSmoke(_wmExitPoint, 1.1f);
                        Program.ApplyScreenShake(4f, 0.2f);
                    }
                    return;
                }

                case WormBossState.Underground:
                {
                    // Suit le joueur sous terre (position logique invisible), pas de rendu du corps.
                    Speed = 0f;
                    AnimState = "idle";
                    _wmTimer -= dt;
                    if (_wmTimer <= 0f)
                    {
                        StartWormTelegraph(playerPos, destroyed);
                    }
                    return;
                }

                case WormBossState.Burrowing:
                {
                    Speed = 0f;
                    AnimState = "idle";
                    _wmTimer += dt;
                    float progress = Math.Clamp(_wmTimer / _wmBurrowDuration, 0f, 1f);
                    float easedProgress = progress * progress * (3f - 2f * progress);
                    WorldPos = Vector2.Lerp(_wmBurrowStart, _wmExitPoint, easedProgress);
                    float travelled = _wmBurrowDistance * easedProgress;
                    while (_wmNextWakeDistance <= travelled && _wmNextWakeDistance < _wmBurrowDistance)
                    {
                        Vector2 wakePosition = Vector2.Lerp(_wmBurrowStart, _wmExitPoint,
                            _wmBurrowDistance > 0.01f ? _wmNextWakeDistance / _wmBurrowDistance : 1f);
                        AddWormGroundMark(WormGroundMarkType.Wake, wakePosition, 25f, 1.8f);
                        _wmNextWakeDistance += WM_BURROW_WAKE_SPACING;
                    }

                    if (progress >= 1f)
                    {
                        WorldPos = _wmExitPoint;
                        _wmState = WormBossState.Telegraph;
                        _wmTimer = _wmAttackTelegraphDuration;
                        AddWormGroundMark(WormGroundMarkType.Telegraph, _wmExitPoint,
                            _wmAttackDamageRadius + 16f, _wmAttackTelegraphDuration + 0.4f);
                        PlayWormRumbleSound(0.65f, 0.9f);
                        _wmLastRumbleTick = -1;
                        _wmLastShakeTick = -1;
                        Facing = _wmEntryPoint.X >= _wmExitPoint.X ? 1f : -1f;
                    }
                    return;
                }

                case WormBossState.Telegraph:
                {
                    // Le sol tremble au point de sortie : le joueur voit venir l'attaque et peut s'écarter.
                    _wmTimer -= dt;
                    Speed = 0f;
                    AnimState = "attack";

                    // RUMBLING : son de tremblement de sol
                    // Le son s'intensifie plus on approche de la fin du telegraph
                    float telegraphProgress = 1f - Math.Clamp(_wmTimer / _wmAttackTelegraphDuration, 0f, 1f);
                    float rumbleVolume = 0.3f + telegraphProgress * 0.9f; // 0.3 -> 1.2
                    float rumblePitch = 0.8f + telegraphProgress * 0.3f;   // 0.8 -> 1.1

                    // Un seul déclenchement par tranche de temps (avant : la condition restait vraie
                    // ~6 frames de suite, donc son et secousse étaient rejoués en rafale).
                    int rumbleTick = (int)MathF.Floor(_wmTimer / 0.3f);
                    if (rumbleTick != _wmLastRumbleTick)
                    {
                        _wmLastRumbleTick = rumbleTick;
                        PlayWormRumbleSound(rumbleVolume, rumblePitch);
                    }

                    int shakeTick = (int)MathF.Floor(_wmTimer / 0.2f);
                    if (shakeTick != _wmLastShakeTick)
                    {
                        _wmLastShakeTick = shakeTick;
                        Program.ApplyScreenShake(1.2f + telegraphProgress * 0.8f, 0.05f);
                    }

                    if (_wmTimer <= 0f)
                    {
                        _wmState = WormBossState.Emerging;
                        _wmTimer = 0f;
                        _wmEntryPointLocked = true; // la tête sort : plus de changement de point d'entrée
                        _wmHasDealtDamage = false;
                        WorldPos = _wmExitPoint;
                        _wmSurfacePoint = _wmExitPoint;
                        AnimState = "attack";

                        // Jouer un dernier rumbling fort juste avant l'émergence
                        PlayWormRumbleSound(1.5f, 1.1f);

                        // Décalage temporel entre segments, calculé pour viser ~WM_SEGMENT_SPACING px
                        // entre deux segments consécutifs le long du trajet sortie -> entrée.
                        float horizontalDist = Vector2.Distance(_wmExitPoint, _wmEntryPoint);
                        float horizontalSpeed = horizontalDist / Math.Max(0.05f, WM_HEAD_LEAP_DURATION);
                        _wmSegmentStagger = WM_SEGMENT_SPACING / Math.Max(1f, horizontalSpeed);
                        // Durée totale de la manœuvre : le temps pour la tête + le retard cumulé du
                        // dernier segment, pour qu'il ait lui aussi le temps de finir SON propre arc.
                        _wmTotalFlightDuration = WM_HEAD_LEAP_DURATION + (WM_SEGMENT_COUNT - 1) * _wmSegmentStagger;

                        SpawnKhamsinSmoke(_wmExitPoint, 1.4f); // réutilise le FX de poussière existant
                        AddWormGroundMark(WormGroundMarkType.Crater, _wmExitPoint, 40f, WM_CRATER_LIFETIME);
                        SpawnWormDebris(_wmExitPoint, 1.25f);
                        Program.ApplyScreenShake(4f, 0.15f);
                    }
                    return;
                }

                case WormBossState.Emerging:
                case WormBossState.Airborne:
                case WormBossState.Diving:
                {
                    _wmTimer += dt;

                    // La tête suit son propre arc parabolique (sortie -> entrée), et se fige une fois
                    // arrivée : elle ne "recule" pas pendant que les segments suivants finissent le leur.
                    float headT = Math.Clamp(_wmTimer / WM_HEAD_LEAP_DURATION, 0f, 1f);
                    Vector2 flatPos = Vector2.Lerp(_wmExitPoint, _wmEntryPoint, headT);
                    float arc = 4f * _wmArcHeight * headT * (1f - headT); // 0 aux extrémités, max au milieu
                    Vector2 headGroundPos = World.GetEntityGroundPosition(flatPos);
                    Vector2 headPos = new Vector2(headGroundPos.X, headGroundPos.Y - arc);
                    WorldPos = headPos;

                    if (headT < 0.35f) _wmState = WormBossState.Emerging;
                    else if (headT > 0.65f) _wmState = WormBossState.Diving;
                    else _wmState = WormBossState.Airborne;

                    // Dégâts si la tête (ou son voisinage immédiat) passe trop près du joueur pendant le saut.
                    if (!_wmHasDealtDamage && headT < 1f)
                    {
                        Vector2 playerGroundPos = World.GetEntityGroundPosition(playerPos);
                        if (Vector2.DistanceSquared(headGroundPos, playerGroundPos) < _wmAttackDamageRadius * _wmAttackDamageRadius)
                        {
                            int damage = _wmLastAttackPattern == WormAttackPattern.Ambush
                                ? (int)MathF.Ceiling(Attack * (hpPercent < 0.5f ? 1.5f : 1.25f))
                                : Math.Max(8, Attack);
                            Program.DamagePlayerTarget(damage, AiTargetConnectionId, GetDisplayName(), Program.InferDeathCauseFromSpecies(Species));
                            Program.ApplyScreenShake(6f, 0.18f);
                            _wmHasDealtDamage = true;
                        }
                    }

                    Facing = (_wmEntryPoint.X - _wmExitPoint.X) >= 0f ? 1f : -1f;
                    AnimState = "attack";

                    // La manœuvre se termine seulement quand le DERNIER segment a fini son propre arc
                    // (donc a vraiment touché le sol) — voir EntityRenderer.DrawWormBoss pour le détail
                    // du calcul par segment (décalé de i * WormSegmentStagger).
                    if (_wmTimer >= _wmTotalFlightDuration)
                    {
                        _wmState = WormBossState.SurfaceEmerging;
                        _wmTimer = 0f;
                        _wmEntryPointLocked = false;
                        WorldPos = World.GetEntityGroundPosition(_wmEntryPoint);
                        _wmSurfacePoint = _wmEntryPoint;
                        SpawnKhamsinSmoke(_wmEntryPoint, 1.4f);
                        AddWormGroundMark(WormGroundMarkType.Crater, _wmEntryPoint, 46f, WM_CRATER_LIFETIME);
                        SpawnWormDebris(_wmEntryPoint, 1.4f);
                        Program.ApplyScreenShake(4f, 0.15f);
                    }
                    return;
                }

                case WormBossState.SurfaceEmerging:
                {
                    Speed = 0f;
                    AnimState = "attack";
                    Facing = (_wmEntryPoint.X - _wmExitPoint.X) >= 0f ? 1f : -1f;
                    _wmTimer += dt;
                    _wmSurfacePoint = _wmEntryPoint;
                    WorldPos = GetWormIntroSegmentPoses()[0].Position;
                    if (_wmTimer >= WM_SURFACE_EMERGE_DURATION)
                    {
                        _wmState = WormBossState.SurfaceRecover;
                        _wmTimer = WM_SURFACE_RECOVER_DURATION;
                    }
                    return;
                }

                case WormBossState.SurfaceRecover:
                {
                    Speed = 0f;
                    AnimState = "idle";
                    _wmSurfacePoint = _wmEntryPoint;
                    Facing = (_wmEntryPoint.X - _wmExitPoint.X) >= 0f ? 1f : -1f;
                    WorldPos = GetWormIntroSegmentPoses()[0].Position;
                    _wmTimer -= dt;
                    if (_wmTimer <= 0f)
                    {
                        _wmState = WormBossState.Retreat;
                        _wmTimer = 0f;
                    }
                    return;
                }

                case WormBossState.HitStagger:
                {
                    Speed = 0f;
                    AnimState = "hurt";
                    _wmSurfacePoint = _wmEntryPoint;
                    _wmTimer += dt;
                    WorldPos = GetWormIntroSegmentPoses()[0].Position;
                    if (_wmTimer >= WM_HIT_STAGGER_DURATION)
                    {
                        _wmState = WormBossState.Retreat;
                        _wmTimer = 0f;
                    }
                    return;
                }

                case WormBossState.Retreat:
                {
                    Speed = 0f;
                    AnimState = "attack";
                    _wmTimer += dt;
                    float retreatProgress = Math.Clamp(_wmTimer / WM_RETREAT_DURATION, 0f, 1f);
                    _wmSurfacePoint = _wmEntryPoint;
                    WorldPos = GetWormIntroSegmentPoses()[0].Position;
                    if (retreatProgress >= 1f)
                    {
                        _wmState = WormBossState.Recover;
                        _wmTimer = WM_RECOVER_DURATION;
                        WorldPos = _wmSurfacePoint;
                        AddWormGroundMark(WormGroundMarkType.Crater, _wmEntryPoint, 42f, WM_CRATER_LIFETIME);
                        SpawnWormDebris(_wmEntryPoint, 0.9f);
                        Program.ApplyScreenShake(3f, 0.16f);
                    }
                    return;
                }

                case WormBossState.Recover:
                {
                    Speed = 0f;
                    AnimState = "idle";
                    _wmTimer -= dt;
                    if (_wmTimer <= 0f)
                    {
                        _wmState = WormBossState.Underground;
                        // Cooldown sous terre plus court à bas HP -> boss plus agressif en fin de combat.
                        _wmTimer = MathHelper.Lerp(0.7f, 0.25f, 1f - hpPercent);
                    }
                    return;
                }
            }
        }

        private void UpdateSlimeMovement(float dt, HashSet<string> destroyed)
        {
            if (!IsSlime) return;

            bool wasJumping = _slimeJumpState == SlimeJumpState.Jumping;
            switch (_slimeJumpState)
            {
                case SlimeJumpState.Grounded:
                    // Si on a une cible et qu'elle est assez éloignée, on lance un saut
                    if (AiTarget != Vector2.Zero && Vector2.DistanceSquared(WorldPos, AiTarget) > (SLIME_MIN_JUMP_DIST) * (SLIME_MIN_JUMP_DIST))
                    {
                        _slimeJumpState = SlimeJumpState.Charging;
                        _slimeChargeTimer = SLIME_CHARGE_DURATION;
                        _slimeHasDealtDamageThisJump = false;
                        _slimeJumpStartPos = WorldPos;
                        // Clamp target to maximum jump distance
                        Vector2 toTarget = AiTarget - WorldPos;
                        float dist = toTarget.Length();
                        if (dist > SLIME_MAX_JUMP_DIST && dist > 0.01f)
                        {
                            toTarget = Vector2.Normalize(toTarget) * SLIME_MAX_JUMP_DIST;
                            _slimeJumpTargetPos = WorldPos + toTarget;
                        }
                        else
                        {
                            _slimeJumpTargetPos = AiTarget;
                        }
                    }
                    break;

                case SlimeJumpState.Charging:
                    _slimeChargeTimer -= dt;
                    if (_slimeChargeTimer <= 0f)
                    {
                        _slimeJumpState = SlimeJumpState.Jumping;
                        _slimeJumpProgress = 0f;
                        // On peut aussi ajuster la cible si elle a changé entre-temps,
                        // en la clampant à la portée maximale du slime.
                        Vector2 toTarget2 = AiTarget - _slimeJumpStartPos;
                        float dist2 = toTarget2.Length();
                        if (dist2 > SLIME_MAX_JUMP_DIST && dist2 > 0.01f)
                        {
                            toTarget2 = Vector2.Normalize(toTarget2) * SLIME_MAX_JUMP_DIST;
                            _slimeJumpTargetPos = _slimeJumpStartPos + toTarget2;
                        }
                        else
                        {
                            _slimeJumpTargetPos = AiTarget;
                        }
                    }
                    break;

                case SlimeJumpState.Jumping:
                    _slimeJumpProgress += dt / SLIME_JUMP_DURATION;
                    if (_slimeJumpProgress >= 1f)
                    {
                        _slimeJumpProgress = 1f;
                        _slimeJumpState = SlimeJumpState.Landing;
                        _slimeLandingTimer = SLIME_LANDING_DURATION;
                        _slimeHeight = 0f;
                        WorldPos = _slimeJumpTargetPos;
                    }
                    else
                    {
                        // Position horizontale interpolée
                        Vector2 newPos = Vector2.Lerp(_slimeJumpStartPos, _slimeJumpTargetPos, _slimeJumpProgress);
                        // Vérification des collisions horizontales (optionnel)
                        Vector2 move = newPos - WorldPos;
                        Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                        if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                            WorldPos.X += move.X;
                        Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                        if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                            WorldPos.Y += move.Y;

                        // Hauteur parabolique
                        float t = _slimeJumpProgress;
                        _slimeHeight = 4f * SLIME_ARC_HEIGHT * t * (1f - t);
                    }
                    break;

                case SlimeJumpState.Landing:
                    _slimeLandingTimer -= dt;
                    if (_slimeLandingTimer <= 0f)
                    {
                        _slimeJumpState = SlimeJumpState.Grounded;
                        _slimeHeight = 0f;
                        // On peut décider d'une nouvelle cible après atterrissage (ex: Wander)
                    }
                    break;
            }

            // CORRECTIF : UpdateSlimeMovement est aussi appelée en Wander / Flee / Follow /
            // WalkToTarget (voir PerformMovement). Dans ces états AiTarget n'est PAS le joueur
            // (point de balade, point de fuite, position du maître...) : l'ancien code testait la
            // collision entre le slime et une "hitbox joueur" posée sur AiTarget, donc un slime qui
            // atterrissait sur son point de balade (ou un slime apprivoisé qui suivait son maître)
            // infligeait des dégâts au joueur sans jamais le toucher. On ne blesse donc le joueur
            // que si le slime est hostile, en poursuite, et que sa cible est bien un joueur.
            if (wasJumping
                && !_slimeHasDealtDamageThisJump
                && !IsTamed
                && AiState == NpcAiState.Chase
                && _combatTargetEntity == null
                && AiTarget != Vector2.Zero)
            {
                Rectangle slimeHitbox = World.GetEntityCollisionHitbox(WorldPos, Species, Program.TileSize);
                Rectangle targetHitbox = World.GetEntityCollisionHitbox(
                    AiTarget,
                    Program.GetPlayerCollisionDimensions(),
                    Program.TileSize);
                if (Raylib.CheckCollisionRecs(slimeHitbox, targetHitbox))
                {
                    Program.DamagePlayerTarget(
                        Math.Max(1, Attack),
                        AiTargetConnectionId,
                        GetDisplayName(),
                        Program.InferDeathCauseFromSpecies(Species));
                    Program.NotifyPlayerAttackedByEntity(this, AiTargetConnectionId);
                    Program.ApplyScreenShake(2f, 0.1f);
                    _slimeHasDealtDamageThisJump = true;
                }
            }

            // Mise à jour de l'orientation (face à la cible si on saute)
            if (AiTarget != Vector2.Zero && _slimeJumpState != SlimeJumpState.Grounded)
            {
                float dx = AiTarget.X - WorldPos.X;
                if (MathF.Abs(dx) > 0.01f)
                    Facing = dx >= 0f ? 1f : -1f;
            }

            // Pas d'animation spéciale, on garde "idle"
            AnimState = "idle";
            Speed = 0f; // pas de déplacement linéaire
        }

        //  Déclenche un envol si l'oiseau est actuellement au sol. Appelée depuis Flee()
        // (fuite = décollage quasi systématique) et depuis Wander()/Decide() (envol spontané,
        // plus rare, pour donner un peu de vie aux abords même sans menace).
        private void StartBirdTakeoff(Vector2? preferredDir = null)
        {
            if (!IsBird || _birdFlightState != BirdFlightState.Grounded) return;

            _birdFlightState = BirdFlightState.TakingOff;

            // Cap initial : dans la direction de fuite si on décolle pour échapper au joueur,
            // sinon dans la direction actuellement "regardée" (Facing), avec une petite
            // variation aléatoire pour ne pas avoir des envols parfaitement rectilignes.
            Vector2 baseDir = preferredDir.HasValue && preferredDir.Value.LengthSquared() > 0.0001f
                ? Vector2.Normalize(preferredDir.Value)
                : new Vector2(Facing, 0f);
            float baseAngle = MathF.Atan2(baseDir.Y, baseDir.X);
            baseAngle += (float)(Random.Shared.NextDouble() - 0.5) * 0.6f;
            _birdFlightDir = new Vector2(MathF.Cos(baseAngle), MathF.Sin(baseAngle));

            //  La plupart des envols restent à hauteur "normale" (au-dessus des arbres/toits),
            // mais un oiseau peut aussi choisir de monter bien plus haut s'il le souhaite —
            // plus rare, mais bien plus spectaculaire.
            bool highAltitude = Random.Shared.NextDouble() < BIRD_HIGH_ALTITUDE_CHANCE;
            float minH = highAltitude ? BIRD_HIGH_MIN_FLIGHT_HEIGHT : BIRD_MIN_FLIGHT_HEIGHT;
            float maxH = highAltitude ? BIRD_MAX_FLIGHT_HEIGHT : BIRD_LOW_MAX_FLIGHT_HEIGHT;
            _birdTargetFlightHeight = minH + (float)Random.Shared.NextDouble() * (maxH - minH);

            // Monter/descendre plus haut prend logiquement plus de temps.
            float heightFactor = MathF.Sqrt(_birdTargetFlightHeight / BIRD_MIN_FLIGHT_HEIGHT);
            _birdTakeoffDuration = BIRD_TAKEOFF_DURATION * heightFactor;
            _birdLandingDuration = BIRD_LANDING_DURATION * heightFactor;
            _birdTakeoffTimer = _birdTakeoffDuration;

            // Un vol en haute altitude dure aussi un peu plus longtemps avant d'envisager
            // de redescendre (le temps d'en profiter).
            _birdFlightDuration = BIRD_MIN_FLIGHT_DURATION +
                (float)Random.Shared.NextDouble() * (BIRD_MAX_FLIGHT_DURATION - BIRD_MIN_FLIGHT_DURATION);
            if (highAltitude)
                _birdFlightDuration *= 1.4f;

            _birdPatternPhase = 0f;
            _birdFlockLeader = null;
            _birdFlockRecheckTimer = 0f;

            // Choix du style de vol : par défaut on tire parmi les patterns "solo". Le
            // regroupement en V ou en cercle est décidé un peu plus tard (TryJoinFlock),
            // une fois que l'oiseau est effectivement en l'air et peut voir ses voisins.
            var solo = new[] { BirdFlightPattern.Glide, BirdFlightPattern.Hop, BirdFlightPattern.Zigzag };
            _birdFlightPattern = solo[Random.Shared.Next(solo.Length)];
        }

        public void EscapeFromTreeHit(Vector2 sourceWorldPos)
        {
            if (!IsBird || _birdFlightState != BirdFlightState.Grounded) return;

            Vector2 fleeDirection = WorldPos - sourceWorldPos;
            if (fleeDirection.LengthSquared() < 0.0001f)
                fleeDirection = new Vector2(Facing, 0f);

            StartBirdTakeoff(fleeDirection);
        }

        //  Cherche un autre oiseau de la même espèce déjà en vol à proximité pour former un
        // petit groupe : soit en V (à la suite, façon oies migratrices), soit en cercle autour
        // d'un point commun (façon vautours/corbeaux tournoyant au-dessus d'un élément).
        private void TryJoinFlock()
        {
            if (_birdFlockLeader != null && _birdFlockLeader.IsAlive && _birdFlockLeader.IsBirdFlying)
                return; // déjà dans un groupe valide

            _birdFlockLeader = null;
            var candidates = Program.GetEntities();
            Entity? bestLeader = null;
            float bestDistSq = BIRD_FLOCK_SEARCH_RADIUS * BIRD_FLOCK_SEARCH_RADIUS;

            foreach (var other in candidates)
            {
                if (other == this || !other.IsAlive) continue;
                if (!string.Equals(other.Species, Species, StringComparison.OrdinalIgnoreCase)) continue;
                if (!other.IsBirdFlying) continue;

                float distSq = Vector2.DistanceSquared(WorldPos, other.WorldPos);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestLeader = other;
                }
            }

            if (bestLeader == null) return;

            // Un meneur qui suit déjà quelqu'un ne peut pas être suivi (on évite les chaînes
            // trop longues / les boucles) : on prend alors la tête de sa propre chaîne.
            while (bestLeader!._birdFlockLeader != null && bestLeader._birdFlockLeader.IsAlive && bestLeader._birdFlockLeader.IsBirdFlying)
                bestLeader = bestLeader._birdFlockLeader;
            if (bestLeader == this) return;

            _birdFlockLeader = bestLeader;

            bool goCircle = Random.Shared.NextDouble() < 0.5;
            if (goCircle)
            {
                _birdFlightPattern = BirdFlightPattern.Circle;
                _birdCircleCenter = bestLeader.WorldPos;
                _birdCircleRadius = 50f + (float)Random.Shared.NextDouble() * 60f;
                _birdCircleAngle = (float)(Random.Shared.NextDouble() * MathF.Tau);
                _birdCircleDir = Random.Shared.NextDouble() < 0.5 ? 1f : -1f;
            }
            else
            {
                _birdFlightPattern = BirdFlightPattern.VFormation;
                // Position dans le V : alternance gauche/droite, en retrait derrière le meneur.
                int slot = 1 + Random.Shared.Next(3);
                float side = Random.Shared.NextDouble() < 0.5 ? -1f : 1f;
                _birdFlockSlotOffset = new Vector2(side * slot * 26f, slot * 22f);
            }
        }

        private void UpdateBirdFlight(float dt, HashSet<string> destroyed)
        {
            if (!IsBird) return;

            switch (_birdFlightState)
            {
                case BirdFlightState.Grounded:
                    _birdFlightHeight = 0f;
                    return; // déplacement au sol géré normalement par MoveTowardTarget/Flee

                case BirdFlightState.TakingOff:
                    _birdTakeoffTimer -= dt;
                    float takeoffT = 1f - Math.Clamp(_birdTakeoffTimer / _birdTakeoffDuration, 0f, 1f);
                    _birdFlightHeight = _birdTargetFlightHeight * takeoffT;
                    if (_birdTakeoffTimer <= 0f)
                    {
                        _birdFlightState = BirdFlightState.Flying;
                        _birdFlightHeight = _birdTargetFlightHeight;
                    }
                    break;

                case BirdFlightState.Flying:
                    _birdFlightDuration -= dt;
                    _birdPatternPhase += dt;

                    // Petite chance de rejoindre/rester dans un groupe, revérifiée
                    // périodiquement (pas tous les frames, pour rester léger en perf).
                    _birdFlockRecheckTimer -= dt;
                    if (_birdFlockRecheckTimer <= 0f)
                    {
                        _birdFlockRecheckTimer = 1.5f;
                        if (_birdFlockLeader == null && Random.Shared.NextDouble() < 0.4)
                            TryJoinFlock();
                    }

                    ApplyBirdFlightPattern(dt);

                    if (_birdFlightDuration <= 0f)
                    {
                        _birdFlightState = BirdFlightState.Landing;
                        _birdLandingTimer = _birdLandingDuration;
                        _birdFlockLeader = null;
                    }
                    break;

                case BirdFlightState.Landing:
                    _birdLandingTimer -= dt;
                    float landT = Math.Clamp(_birdLandingTimer / _birdLandingDuration, 0f, 1f);
                    _birdFlightHeight = _birdTargetFlightHeight * landT;
                    if (_birdLandingTimer <= 0f)
                    {
                        _birdFlightState = BirdFlightState.Grounded;
                        _birdFlightHeight = 0f;
                    }
                    break;
            }
        }

        //  Applique le style de vol choisi. Un oiseau qui plane (Glide/Hop/Zigzag) ne fait
        // JAMAIS de surplace : il dérive en permanence le long de _birdFlightDir, un cap qui
        // tourne lentement et aléatoirement au fil du temps pour un vol naturel, qu'il soit
        // par ailleurs en train de fuir, d'errer ou simplement de regarder le joueur. Cette
        // dérive s'ajoute au déplacement horizontal éventuel de l'IA (Flee/MoveTowardTarget) :
        // même quand rien d'autre ne le ferait avancer (Idle, LookAtPlayer...), il continue de
        // planer, puis finit par atterrir au bout de sa durée de vol (voir UpdateBirdFlight).
        private void ApplyBirdFlightPattern(float dt)
        {
            // Le cap dérive légèrement au fil du temps, jamais parfaitement rectiligne.
            float turn = (float)(Random.Shared.NextDouble() - 0.5) * BIRD_HEADING_TURN_RATE * dt;
            float dirAngle = MathF.Atan2(_birdFlightDir.Y, _birdFlightDir.X) + turn;
            _birdFlightDir = new Vector2(MathF.Cos(dirAngle), MathF.Sin(dirAngle));

            switch (_birdFlightPattern)
            {
                case BirdFlightPattern.Glide:
                    // Vol plané : avance régulièrement, légère ondulation lente de la hauteur.
                    _birdFlightHeight = _birdTargetFlightHeight + MathF.Sin(_birdPatternPhase * 1.2f) * 6f;
                    WorldPos += _birdFlightDir * BIRD_CRUISE_SPEED * dt;
                    Facing = _birdFlightDir.X >= 0 ? 1f : -1f;
                    break;

                case BirdFlightPattern.Hop:
                    // "Sautille" dans les airs : petits bonds rythmés (façon moineau) tout en
                    // avançant, plus marqués que le vol plané.
                    _birdFlightHeight = _birdTargetFlightHeight + MathF.Abs(MathF.Sin(_birdPatternPhase * 5f)) * 18f;
                    WorldPos += _birdFlightDir * BIRD_CRUISE_SPEED * 0.8f * dt;
                    Facing = _birdFlightDir.X >= 0 ? 1f : -1f;
                    break;

                case BirdFlightPattern.Zigzag:
                {
                    // Trajectoire en zigzag : avance le long du cap tout en oscillant de part et
                    // d'autre (perpendiculairement au cap), plus une hauteur qui varie vite pour
                    // accentuer l'effet erratique.
                    _birdFlightHeight = _birdTargetFlightHeight + MathF.Sin(_birdPatternPhase * 4f) * 12f;
                    WorldPos += _birdFlightDir * BIRD_CRUISE_SPEED * 1.1f * dt;
                    Vector2 perp = new Vector2(-_birdFlightDir.Y, _birdFlightDir.X);
                    WorldPos += perp * MathF.Sin(_birdPatternPhase * 6f) * 40f * dt;
                    Facing = _birdFlightDir.X >= 0 ? 1f : -1f;
                    break;
                }

                case BirdFlightPattern.Circle:
                {
                    // Tourne autour d'un point fixe (ex : un meneur, un arbre...) à rayon constant.
                    if (_birdFlockLeader != null && _birdFlockLeader.IsAlive)
                        _birdCircleCenter = _birdFlockLeader.WorldPos;

                    _birdCircleAngle += _birdCircleDir * dt * 1.1f; // vitesse angulaire
                    WorldPos = _birdCircleCenter + new Vector2(
                        MathF.Cos(_birdCircleAngle) * _birdCircleRadius,
                        MathF.Sin(_birdCircleAngle) * _birdCircleRadius * 0.5f); // ellipse, vue de dessus
                    Facing = MathF.Cos(_birdCircleAngle + MathF.PI / 2f) >= 0 ? 1f : -1f;
                    _birdFlightHeight = _birdTargetFlightHeight + MathF.Sin(_birdPatternPhase * 1.5f) * 5f;
                    break;
                }

                case BirdFlightPattern.VFormation:
                {
                    // Suit le meneur avec un léger retard, en gardant une position en retrait
                    // (façon vol en V des oies/canards migrateurs).
                    if (_birdFlockLeader != null && _birdFlockLeader.IsAlive && _birdFlockLeader.IsBirdFlying)
                    {
                        Vector2 desired = _birdFlockLeader.WorldPos + _birdFlockSlotOffset * _birdFlockLeader.Facing;
                        WorldPos = Vector2.Lerp(WorldPos, desired, Math.Clamp(dt * 3f, 0f, 1f));
                        Facing = _birdFlockLeader.Facing;
                        _birdFlightHeight = _birdFlockLeader._birdFlightHeight;
                    }
                    else
                    {
                        // Le meneur a disparu/atterri : on repasse en vol plané solo.
                        _birdFlightPattern = BirdFlightPattern.Glide;
                    }
                    break;
                }
            }
        }

        private void UpdateGulperBoss(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            if (!IsGulper) return;
            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);

            switch (_guState)
            {
                case GulperBossState.Grounded:
                {
                    // Vient de s'écraser au sol : immobile et VULNÉRABLE. C'est la seule fenêtre pour le frapper.
                    Speed = 0f;
                    AnimState = "idle";
                    _guTimer -= dt;
                    if (_guTimer <= 0f)
                        _guState = GulperBossState.Rising;
                    break;
                }

                case GulperBossState.Rising:
                {
                    // Remonte doucement depuis le sol : redevient invulnérable dès qu'il quitte le sol
                    // (voir GulperIsGrounded / IsInvulnerable).
                    Speed = 0f;
                    AnimState = "idle";
                    _guHeight += GU_RISE_SPEED * dt;
                    if (_guHeight >= GU_HOVER_HEIGHT)
                    {
                        _guHeight = GU_HOVER_HEIGHT;
                        _guState = GulperBossState.Approach;
                    }
                    break;
                }

                case GulperBossState.Approach:
                {
                    // En vol : se rapproche pour se positionner à la verticale de sa cible.
                    AnimState = "run";
                    Vector2 toPlayer = World.GetEntityGroundPosition(playerPos) - World.GetEntityGroundPosition(_guGroundPos);
                    float distXY = toPlayer.Length();

                    if (distXY > GU_ABOVE_RADIUS)
                    {
                        Vector2 dir = distXY > 0.01f ? Vector2.Normalize(toPlayer) : new Vector2(Facing, 0f);
                        float moveSpeed = Math.Max(RunSpeed > 0f ? RunSpeed : Speed, GU_APPROACH_SPEED);
                        Vector2 move = dir * moveSpeed * dt;

                        Vector2 newPosX = _guGroundPos + new Vector2(move.X, 0);
                        if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                            _guGroundPos.X += move.X;
                        Vector2 newPosY = _guGroundPos + new Vector2(0, move.Y);
                        if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                            _guGroundPos.Y += move.Y;

                        Facing = dir.X >= 0f ? 1f : -1f;
                    }
                    else
                    {
                        // Il est désormais à la verticale de sa cible : il commence à charger son attaque.
                        _guState = GulperBossState.Telegraph;
                        _guTimer = GU_TELEGRAPH_DURATION;
                        _guHasDealtDamage = false;
                        AnimState = "attack";
                    }
                    break;
                }

                case GulperBossState.Telegraph:
                {
                    // Immobile en l'air au-dessus de la cible, il charge son attaque quelques instants :
                    // le joueur voit venir le crash et peut s'écarter avant qu'il ne se laisse tomber.
                    Speed = 0f;
                    AnimState = "attack";
                    _guTimer -= dt;

                    Vector2 toPlayer = World.GetEntityGroundPosition(playerPos) - World.GetEntityGroundPosition(_guGroundPos);
                    if (toPlayer.Length() > 0.01f)
                        Facing = toPlayer.X >= 0f ? 1f : -1f;

                    if (_guTimer <= 0f)
                    {
                        // Le point de crash se fige ICI : il chute à la verticale, sans plus poursuivre la cible.
                        _guState = GulperBossState.Slam;
                        _guHasDealtDamage = false;
                        AnimState = "attack";
                    }
                    break;
                }

                case GulperBossState.Slam:
                {
                    // Chute rapide et s'éclate contre le sol.
                    AnimState = "attack";
                    _guHeight -= GU_SLAM_SPEED * dt;

                    if (_guHeight <= 0f)
                    {
                        _guHeight = 0f;

                        if (!_guHasDealtDamage)
                        {
                            float distToPlayer = Vector2.Distance(
                                World.GetEntityGroundPosition(_guGroundPos),
                                World.GetEntityGroundPosition(playerPos));
                            if (distToPlayer < GU_GROUND_DAMAGE_RADIUS)
                            {
                                Program.DamagePlayerTarget(Math.Max(8, Attack), AiTargetConnectionId, GetDisplayName(), Program.InferDeathCauseFromSpecies(Species));
                            }
                            Program.ApplyScreenShake(5f, 0.16f);
                            SpawnKhamsinSmoke(_guGroundPos, 1.2f); // réutilise le FX de poussière existant
                            _guHasDealtDamage = true;
                        }

                        // Étourdi au sol : la récupération est plus courte qu'avant (boss globalement
                        // plus rapide), et encore plus courte à bas HP (boss plus agressif en fin de combat).
                        _guState = GulperBossState.Grounded;
                        _guTimer = MathHelper.Lerp(1.2f, 0.7f, 1f - hpPercent);
                        AnimState = "idle";
                    }
                    break;
                }
            }

            // Synchronise la position visuelle/logique : le vol est simulé en soustrayant _guHeight
            // à la position au sol (même principe que l'arc du WormBoss), donc aucun changement n'est
            // nécessaire côté rendu ou hitbox : ils suivent WorldPos comme pour toute autre entité.
            WorldPos = new Vector2(_guGroundPos.X, _guGroundPos.Y - _guHeight);
        }

        // Choisit un point de sortie à distance raisonnable du joueur, et fige déjà le point d'entrée
        // (généralement proche du joueur aussi, pour que le ver "saute sur sa cible").
        // Comme demandé : une fois la tête sortie (_wmEntryPointLocked = true), ce point ne bougera plus.
        private void StartWormTelegraph(Vector2 playerPos, HashSet<string> destroyed)
        {
            int pattern = Random.Shared.Next(Enum.GetValues<WormAttackPattern>().Length);
            if (_wmLastAttackPattern.HasValue && (int)_wmLastAttackPattern.Value == pattern)
                pattern = (pattern + 1 + Random.Shared.Next(Enum.GetValues<WormAttackPattern>().Length - 1))
                    % Enum.GetValues<WormAttackPattern>().Length;
            _wmLastAttackPattern = (WormAttackPattern)pattern;

            Vector2 towardExit;
            Vector2 biasAwayFromExit;
            Vector2 desiredEntry;
            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);
            _wmArcHeight = WM_ARC_HEIGHT;
            _wmAttackDamageRadius = WM_DAMAGE_RADIUS;
            _wmAttackTelegraphDuration = WM_TELEGRAPH_DURATION;

            switch (_wmLastAttackPattern.Value)
            {
                case WormAttackPattern.Sweep:
                {
                    float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                    Vector2 direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    Vector2 center = playerPos + new Vector2(
                        (float)(Random.Shared.NextDouble() - 0.5) * 100f,
                        (float)(Random.Shared.NextDouble() - 0.5) * 100f);
                    Vector2 start = center - direction * 240f;
                    Vector2 end = center + direction * 240f;
                    Vector2? sweepExit = FindWormGroundPoint(start, 0f, 70f, destroyed);
                    Vector2? sweepEntry = FindWormGroundPoint(end, 0f, 70f, destroyed);
                    if (sweepExit.HasValue && sweepEntry.HasValue)
                    {
                        _wmExitPoint = sweepExit.Value;
                        _wmEntryPoint = sweepEntry.Value;
                    }
                    else
                    {
                        _wmExitPoint = FindWormGroundPoint(playerPos, WM_MIN_EXIT_DISTANCE, WM_MAX_EXIT_DISTANCE, destroyed)
                                       ?? playerPos + new Vector2(WM_MIN_EXIT_DISTANCE, 0f);
                        _wmEntryPoint = playerPos;
                    }
                    _wmArcHeight = hpPercent < 0.5f ? 75f : 58f;
                    _wmAttackDamageRadius = 52f;
                    _wmAttackTelegraphDuration = 1.2f;
                    break;
                }

                case WormAttackPattern.Ambush:
                    _wmExitPoint = FindWormGroundPoint(playerPos, 170f, 250f, destroyed)
                                   ?? playerPos + new Vector2(190f, 0f);
                    towardExit = _wmExitPoint - playerPos;
                    biasAwayFromExit = towardExit.Length() > 0.01f ? -Vector2.Normalize(towardExit) : Vector2.Zero;
                    desiredEntry = playerPos + biasAwayFromExit * 85f;
                    _wmEntryPoint = FindWormGroundPoint(desiredEntry, 0f, 45f, destroyed) ?? desiredEntry;
                    _wmArcHeight = hpPercent < 0.5f ? 155f : 125f;
                    _wmAttackDamageRadius = 48f;
                    _wmAttackTelegraphDuration = 0.9f;
                    break;

                default:
                    _wmExitPoint = FindWormGroundPoint(playerPos, WM_MIN_EXIT_DISTANCE, WM_MAX_EXIT_DISTANCE, destroyed)
                                   ?? playerPos + new Vector2(WM_MIN_EXIT_DISTANCE, 0f);
                    towardExit = _wmExitPoint - playerPos;
                    biasAwayFromExit = towardExit.Length() > 0.01f ? -Vector2.Normalize(towardExit) : Vector2.Zero;
                    desiredEntry = playerPos + biasAwayFromExit * 40f;
                    _wmEntryPoint = FindWormGroundPoint(desiredEntry, 0f, 90f, destroyed) ?? desiredEntry;
                    break;
            }

            _wmEntryPointLocked = false; // se fige seulement au passage en Emerging

            _wmBurrowStart = World.GetEntityGroundPosition(WorldPos);
            _wmBurrowDistance = Vector2.Distance(_wmBurrowStart, _wmExitPoint);
            _wmBurrowDuration = Math.Max(0.25f, _wmBurrowDistance / WM_BURROW_SPEED);
            _wmNextWakeDistance = WM_BURROW_WAKE_SPACING;
            _wmState = WormBossState.Burrowing;
            _wmTimer = 0f;
        }

        // Cherche une position "au sol" (tuile marchable, pas d'obstacle) dans un anneau de distance
        // [minDist, maxDist] autour d'un centre. Réutilise les mêmes checks que TeleportKhamsinAroundPlayer.
        private Vector2? FindWormGroundPoint(Vector2 center, float minDist, float maxDist, HashSet<string> destroyed)
        {
            for (int i = 0; i < 24; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float dist = minDist + (float)(Random.Shared.NextDouble() * Math.Max(1f, maxDist - minDist));
                Vector2 candidate = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                int tileX = (int)(candidate.X / Program.TileSize);
                int tileY = (int)(candidate.Y / Program.TileSize);
                int groundId = World.GetGroundTileIdAt(tileX, tileY);
                var ground = WorldTileRegistry.GetTile(groundId);
                if (ground == null || !ground.Walkable) continue;
                if (World.GetObjectIdAt(tileX, tileY) != 0) continue;
                if (World.IsCollidingEntity(candidate, destroyed, Species)) continue;
                return candidate;
            }
            return null;
        }

    }
}
