#nullable enable
using Raylib_cs;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    public partial class Entity
    {
        // Private helpers and Genie boss state machine
        // ==================== MÉTHODES PRIVÉES ====================
        private void UpdateKhamsinBoss(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            if (!IsBoss || !string.Equals(Species, "Khamsin", StringComparison.OrdinalIgnoreCase)) return;

            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);
            float distToPlayer = Vector2.Distance(
                World.GetEntityGroundPosition(WorldPos),
                World.GetEntityGroundPosition(playerPos));

            if (_khBossState == KhamsinBossState.Telegraph)
            {
                _khTelegraphTimer -= dt;
                Speed = 0f;
                AnimState = "attack";
                if (_khTelegraphTimer <= 0f)
                {
                    _khBossState = KhamsinBossState.Charge;
                    _khChargeTimer = KH_DASH_CHARGE_DURATION;
                    _khHasDealtDamage = false;
                    AnimState = "attack";
                }
                return;
            }

            if (_khBossState == KhamsinBossState.Charge)
            {
                _khChargeTimer -= dt;
                float brakeFactor = 1f;
                if (_khChargeTimer <= KH_DASH_BRAKE_DURATION)
                {
                    brakeFactor = Math.Clamp(_khChargeTimer / KH_DASH_BRAKE_DURATION, 0f, 1f);
                }

                Vector2 move = _khDashDirection * KH_DASH_SPEED * brakeFactor * dt;
                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                    WorldPos.X += move.X;
                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                    WorldPos.Y += move.Y;

                if (_khDashDirection.X != 0f)
                    Facing = Math.Sign(_khDashDirection.X);

                if (!_khHasDealtDamage && distToPlayer < 48f)
                {
                    Program.DamagePlayerTarget(Math.Max(6, Attack), AiTargetConnectionId, GetDisplayName(), Program.InferDeathCauseFromSpecies(Species));
                    Program.ApplyScreenShake(6f, 0.18f);
                    _khHasDealtDamage = true;
                }

                if (_khChargeTimer <= 0f)
                {
                    _khBossState = KhamsinBossState.Recover;
                    _khRecoveryTimer = KH_DASH_RECOVERY_DURATION;
                    _khHasTeleported = false;
                    Speed = 0f;
                    AnimState = "idle";
                }
                return;
            }

            if (_khBossState == KhamsinBossState.Recover)
            {
                _khRecoveryTimer -= dt;
                Speed = 0f;
                AnimState = "idle";
                if (_khRecoveryTimer <= 0f)
                {
                    if (!_khHasTeleported)
                    {
                        TeleportKhamsinAroundPlayer(playerPos, destroyed);
                        Program.ApplyScreenShake(3.2f, 0.12f);
                        _khHasTeleported = true;
                        _khRecoveryTimer = KH_DASH_RECOVERY_DURATION;
                    }
                    else if (_khDashBurstRemaining > 0)
                    {
                        StartKhamsinDash(playerPos, hpPercent);
                        _khDashBurstRemaining--;
                    }
                    else if (_khVolleyBurstRemaining > 0)
                    {
                        StartKhamsinVolley(playerPos, hpPercent);
                        _khVolleyBurstRemaining--;
                    }
                    else
                    {
                        _khBossState = KhamsinBossState.Idle;
                        _khDashCooldown = MathHelper.Lerp(0.7f, 0.3f, 1f - hpPercent);
                        _khDashBurstRemaining = GetDashBurstCount(hpPercent);
                        _khVolleyBurstRemaining = GetVolleyBurstCount(hpPercent);
                        Speed = 0f;
                    }
                }
                return;
            }

            if (_khBossState == KhamsinBossState.Volley)
            {
                _khVolleyTimer -= dt;
                Speed = 0f;
                AnimState = "idle";
                if (_khVolleyTimer <= 0f)
                {
                    _khVolleyShotsRemaining--;
                    if (_khVolleyShotsRemaining > 0)
                    {
                        FireKhamsinSalvo(playerPos);
                        _khVolleyTimer = MathHelper.Lerp(0.32f, 0.16f, 1f - hpPercent);
                    }
                    else if (_khVolleyBurstRemaining > 0)
                    {
                        StartKhamsinVolley(playerPos, hpPercent);
                        _khVolleyBurstRemaining--;
                    }
                    else
                    {
                        _khBossState = KhamsinBossState.Idle;
                        _khDashCooldown = MathHelper.Lerp(0.7f, 0.3f, 1f - hpPercent);
                        _khDashBurstRemaining = GetDashBurstCount(hpPercent);
                        _khVolleyBurstRemaining = GetVolleyBurstCount(hpPercent);
                    }
                }
                return;
            }

            if (_khDashCooldown > 0f) _khDashCooldown -= dt;
            if (_khBossState == KhamsinBossState.Idle)
            {
                if (_khDashBurstRemaining <= 0 && _khVolleyBurstRemaining <= 0)
                {
                    _khDashBurstRemaining = GetDashBurstCount(hpPercent);
                    _khVolleyBurstRemaining = GetVolleyBurstCount(hpPercent);
                }

                if (_khDashBurstRemaining > 0 && _khDashCooldown <= 0f)
                {
                    StartKhamsinDash(playerPos, hpPercent);
                    _khDashBurstRemaining--;
                    return;
                }
            }

            if (distToPlayer > 70f)
            {
                Vector2 dir = World.GetEntityGroundPosition(playerPos) - World.GetEntityGroundPosition(WorldPos);
                if (dir.Length() > 0.01f)
                    dir = Vector2.Normalize(dir);
                else
                    dir = new Vector2(1f, 0f);

                Vector2 move = dir * Math.Min(RunSpeed, 140f) * dt;
                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species))
                    WorldPos.X += move.X;
                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species))
                    WorldPos.Y += move.Y;
                Facing = dir.X >= 0f ? 1f : -1f;
                AnimState = "run";
            }
            else
            {
                AnimState = "idle";
            }
        }

        private void FireKhamsinSalvo(Vector2 playerPos)
        {
            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);
            int count = Math.Clamp(4 + (int)Math.Round((1f - hpPercent) * 8f), 4, 12);
            float speed = 240f + (1f - hpPercent) * 120f;
            Color color = new Color(255, 220, 180, 255);

            for (int i = 0; i < count; i++)
            {
                float angle = i * (MathF.PI * 2f / count);
                Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var projectile = new KhamsinProjectile(
                    World.GetEntityGroundPosition(WorldPos), dir, speed, Math.Max(4f, Attack * 0.6f), color, color);
                Program.AddSpellProjectile(projectile);
            }

            Program.ApplyScreenShake(2.4f, 0.1f);
        }

        private void StartKhamsinDash(Vector2 playerPos, float hpPercent)
        {
            _khBossState = KhamsinBossState.Telegraph;
            _khTelegraphTimer = KH_DASH_TELEGRAPH_DURATION;
            _khHasTeleported = false;
            Vector2 direction = World.GetEntityGroundPosition(playerPos) - World.GetEntityGroundPosition(WorldPos);
            if (direction.Length() > 0.01f)
                direction = Vector2.Normalize(direction);
            else
                direction = new Vector2(Facing, 0f);
            _khDashDirection = direction;
            // Augmenter la distance du dash de 1.5x pour un dash plus long
            float dashDistance = (260f + (1f - hpPercent) * 190f) * 2f * 1.5f;
            _khDashTarget = playerPos + direction * dashDistance;
            _khHasDealtDamage = false;
            AnimState = "attack";
            Speed = 0f;
        }

        private void StartKhamsinVolley(Vector2 playerPos, float hpPercent)
        {
            _khBossState = KhamsinBossState.Volley;
            _khVolleyShotsRemaining = Math.Clamp(3 + (int)Math.Round((1f - hpPercent) * 2f), 3, 5);
            _khVolleyTimer = MathHelper.Lerp(0.36f, 0.16f, 1f - hpPercent);
            FireKhamsinSalvo(playerPos);
            _khVolleyShotsRemaining--;
            Speed = 0f;
            AnimState = "idle";
        }

        private int GetDashBurstCount(float hpPercent)
        {
            return 3 + (int)Math.Round((1f - hpPercent) * 2f);
        }

        private int GetVolleyBurstCount(float hpPercent)
        {
            return 2 + (int)Math.Round((1f - hpPercent) * 1f);
        }

        private void TeleportKhamsinAroundPlayer(Vector2 playerPos, HashSet<string> destroyed)
        {
            SpawnKhamsinSmoke(WorldPos, 1f);
            for (int i = 0; i < 24; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float dist = 150f + (float)(Random.Shared.NextDouble() * 70f);
                Vector2 candidate = playerPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                int tileX = (int)(candidate.X / Program.TileSize);
                int tileY = (int)(candidate.Y / Program.TileSize);
                int groundId = World.GetGroundTileIdAt(tileX, tileY);
                var ground = WorldTileRegistry.GetTile(groundId);
                if (ground == null || !ground.Walkable) continue;
                if (World.GetObjectIdAt(tileX, tileY) != 0) continue;
                if (World.IsCollidingEntity(candidate, destroyed, Species)) continue;
                WorldPos = candidate;
                SpawnKhamsinSmoke(candidate, 1f);
                return;
            }
        }

        public static void SpawnKhamsinSmoke(Vector2 pos, float intensity)
        {
            int count = (int)Math.Round(36f + intensity * 28f);
            float spreadX = 34f + intensity * 18f;
            float spreadY = 56f + intensity * 28f;
            pos = World.GetEntityGroundPosition(pos);
            float baseHeight = pos.Y - 36f;

            for (int i = 0; i < count; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float speed = 30f + (float)(Random.Shared.NextDouble() * 110f) * intensity;
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                velocity.Y -= 18f + (float)(Random.Shared.NextDouble() * 12f);

                float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * spreadX;
                float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * spreadY;
                Vector2 spawnPos = new Vector2(pos.X + offsetX, baseHeight + offsetY);

                float size = 8f + (float)(Random.Shared.NextDouble() * 12f) + intensity * 6f;
                float lifetime = 0.35f + (float)(Random.Shared.NextDouble() * 0.45f);
                Program.GetParticleList().Add(new Particle(spawnPos, velocity, new Color(90, 90, 90, 220), size, lifetime));
            }
        }

        // ==================== GENIE BOSS : state machine ====================
        //  Boss en 3 phases, basées sur le pourcentage de vie restant :
        //   Phase 1 (100% -> 65%) "Le Marchand d'illusions" : téléportations, clones illusoires, volées de projectiles
        //   Phase 2 (65% -> 30%)  "Tempête de sable" : tourbillons de sable au sol + volées plus rapides
        //   Phase 3 (30% -> 0%)   "Les 3 voeux" (enrage) : + vitesse, sol de lave, laser tournant, voeu ultime
        private void UpdateGenieBoss(float dt, HashSet<string> destroyed, Vector2 playerPos)
        {
            if (!IsGenie) return;

            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);
            Vector2 entityGroundPos = World.GetEntityGroundPosition(WorldPos);
            Vector2 playerGroundPos = World.GetEntityGroundPosition(playerPos);
            float distToPlayer = Vector2.Distance(entityGroundPos, playerGroundPos);

            UpdateGenieHazardZones(dt, playerPos);
            UpdateGenieClones(dt);

            // ---- Transitions de phase ----
            if (_gePhase == 1 && hpPercent <= 0.65f)
            {
                _gePhase = 2;
            }
            if (_gePhase == 2 && hpPercent <= 0.30f)
            {
                _gePhase = 3;
            }
            if (_gePhase >= 2 && !_gePhase2Announced)
            {
                _gePhase2Announced = true;
                Program.AddNotification(new Notification(" Le Génie invoque une tempête de sable !", new Color(230, 190, 110, 255), 3f));
                Program.ApplyScreenShake(4f, 0.25f);
                SpawnGenieSmoke(WorldPos, 1.3f);
            }
            if (_gePhase >= 3 && !_gePhase3Announced)
            {
                _gePhase3Announced = true;
                Program.AddNotification(new Notification(" \"TROIS VOEUX, TROIS CHÂTIMENTS !\" Le Génie entre en fureur !", new Color(255, 90, 60, 255), 3.5f));
                Program.ApplyScreenShake(7f, 0.35f);
                SpawnGenieSmoke(WorldPos, 2f);
                RunSpeed *= 1.4f;
            }

            switch (_geState)
            {
                case GenieBossState.Teleport:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "idle";
                        if (_geStateTimer <= 0f)
                        {
                            TeleportGenieAroundPlayer(playerPos, destroyed);
                            Program.ApplyScreenShake(2.6f, 0.12f);
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.35f;
                        }
                        return;
                    }
                case GenieBossState.ClonesTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        if (_geStateTimer <= 0f)
                        {
                            SpawnGenieClones(playerPos, destroyed);
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.5f;
                        }
                        return;
                    }
                case GenieBossState.FanTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        if (_geStateTimer <= 0f)
                        {
                            _geState = GenieBossState.FanFiring;
                            _geFanShotsRemaining = _gePhase == 1 ? 2 : (_gePhase == 2 ? 3 : 4);
                            _geFanShotTimer = 0f;
                        }
                        return;
                    }
                case GenieBossState.FanFiring:
                    {
                        Speed = 0f;
                        AnimState = "attack";
                        _geFanShotTimer -= dt;
                        if (_geFanShotTimer <= 0f)
                        {
                            FireGenieSalvo(playerPos);
                            _geFanShotsRemaining--;
                            _geFanShotTimer = MathHelper.Lerp(0.42f, 0.22f, 1f - hpPercent);
                            if (_geFanShotsRemaining <= 0)
                            {
                                _geState = GenieBossState.Recover;
                                _geStateTimer = 0.45f;
                            }
                        }
                        return;
                    }
                case GenieBossState.SandTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        if (_geStateTimer <= 0f)
                        {
                            SpawnGenieHazardWave(playerPos, isSand: true);
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.5f;
                        }
                        return;
                    }
                case GenieBossState.LavaTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        if (_geStateTimer <= 0f)
                        {
                            SpawnGenieHazardWave(playerPos, isSand: false);
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.5f;
                        }
                        return;
                    }
                case GenieBossState.LaserTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        SpawnGenieSmoke(WorldPos, 0.35f);
                        if (_geStateTimer <= 0f)
                        {
                            _geState = GenieBossState.LaserSpinning;
                            _geLaserAngle = 0f;
                            _geLaserTimer = 0f;
                            _geLaserSpins = 0;
                            Program.ApplyScreenShake(3f, 0.15f);
                        }
                        return;
                    }
                case GenieBossState.LaserSpinning:
                    {
                        Speed = 0f;
                        AnimState = "attack";
                        _geLaserTimer -= dt;
                        _geLaserAngle += dt * 3.4f; // vitesse de rotation du laser
                        if (_geLaserTimer <= 0f)
                        {
                            FireGenieLaserTick();
                            _geLaserTimer = 0.06f;
                            _geLaserSpins++;
                        }
                        if (_geLaserSpins >= 55) // ~3.3s de rotation
                        {
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.6f;
                        }
                        return;
                    }
                case GenieBossState.WishTelegraph:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "attack";
                        if ((int)(_geStateTimer * 10f) % 2 == 0)
                            SpawnGenieWishWarningParticles(_geWishTargetPos);
                        if (_geStateTimer <= 0f)
                        {
                            ExecuteGenieWishStrike(playerPos);
                            _geState = GenieBossState.Recover;
                            _geStateTimer = 0.9f;
                        }
                        return;
                    }
                case GenieBossState.Recover:
                    {
                        _geStateTimer -= dt;
                        Speed = 0f;
                        AnimState = "idle";
                        if (_geStateTimer <= 0f)
                        {
                            _geState = GenieBossState.Idle;
                            _geAttackCooldown = _gePhase == 1
                                ? MathHelper.Lerp(1.4f, 0.9f, 1f - hpPercent)
                                : (_gePhase == 2 ? MathHelper.Lerp(1.0f, 0.6f, 1f - hpPercent) : MathHelper.Lerp(0.7f, 0.35f, 1f - hpPercent));
                        }
                        return;
                    }
            }

            // ---- État Idle : se replace, puis lance la prochaine attaque selon la phase ----
            if (_geAttackCooldown > 0f) _geAttackCooldown -= dt;
            if (_geAttackCooldown <= 0f)
            {
                StartNextGenieAttack(playerPos, destroyed, hpPercent);
                return;
            }

            // Se maintient à distance moyenne du joueur entre deux attaques (comme un lanceur de sorts)
            float desiredDist = _gePhase >= 3 ? 170f : 210f;
            if (distToPlayer > desiredDist + 40f)
            {
                Vector2 direction = playerGroundPos - entityGroundPos;
                Vector2 dir = Vector2.Normalize(direction == Vector2.Zero ? new Vector2(1, 0) : direction);
                Vector2 move = dir * Math.Min(RunSpeed, 160f) * dt;
                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species)) WorldPos.X += move.X;
                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species)) WorldPos.Y += move.Y;
                Facing = dir.X >= 0f ? 1f : -1f;
                AnimState = "run";
            }
            else if (distToPlayer < desiredDist - 40f)
            {
                Vector2 direction = entityGroundPos - playerGroundPos;
                Vector2 dir = Vector2.Normalize(direction == Vector2.Zero ? new Vector2(1, 0) : direction);
                Vector2 move = dir * Math.Min(RunSpeed, 130f) * dt;
                Vector2 newPosX = WorldPos + new Vector2(move.X, 0);
                if (!World.IsCollidingEntity(newPosX, destroyed, Species)) WorldPos.X += move.X;
                Vector2 newPosY = WorldPos + new Vector2(0, move.Y);
                if (!World.IsCollidingEntity(newPosY, destroyed, Species)) WorldPos.Y += move.Y;
                Facing = dir.X <= 0f ? 1f : -1f; // reste tourné vers le joueur en reculant
                AnimState = "walk";
            }
            else
            {
                Facing = (playerPos.X - WorldPos.X) >= 0f ? 1f : -1f;
                AnimState = "idle";
            }
        }

        private void StartNextGenieAttack(Vector2 playerPos, HashSet<string> destroyed, float hpPercent)
        {
            List<GenieBossState> pool;
            if (_gePhase == 1)
                pool = new List<GenieBossState> { GenieBossState.Teleport, GenieBossState.Teleport, GenieBossState.ClonesTelegraph, GenieBossState.FanTelegraph };
            else if (_gePhase == 2)
                pool = new List<GenieBossState> { GenieBossState.Teleport, GenieBossState.FanTelegraph, GenieBossState.SandTelegraph, GenieBossState.SandTelegraph, GenieBossState.ClonesTelegraph };
            else
            {
                pool = new List<GenieBossState> { GenieBossState.Teleport, GenieBossState.FanTelegraph, GenieBossState.LavaTelegraph, GenieBossState.LaserTelegraph };
                // Le voeu ultime ne revient qu'une fois toutes les ~4 attaques pour rester spectaculaire sans spammer
                if (Random.Shared.Next(0, 4) == 0) pool.Add(GenieBossState.WishTelegraph);
            }

            GenieBossState chosen = pool[Random.Shared.Next(pool.Count)];
            switch (chosen)
            {
                case GenieBossState.Teleport:
                    _geState = GenieBossState.Teleport;
                    _geStateTimer = 0.25f;
                    SpawnGenieSmoke(WorldPos, 0.6f);
                    break;
                case GenieBossState.ClonesTelegraph:
                    _geState = GenieBossState.ClonesTelegraph;
                    _geStateTimer = 0.55f;
                    break;
                case GenieBossState.FanTelegraph:
                    _geState = GenieBossState.FanTelegraph;
                    _geStateTimer = 0.4f;
                    break;
                case GenieBossState.SandTelegraph:
                    _geState = GenieBossState.SandTelegraph;
                    _geStateTimer = 0.5f;
                    break;
                case GenieBossState.LavaTelegraph:
                    _geState = GenieBossState.LavaTelegraph;
                    _geStateTimer = 0.45f;
                    break;
                case GenieBossState.LaserTelegraph:
                    _geState = GenieBossState.LaserTelegraph;
                    _geStateTimer = 0.65f;
                    break;
                case GenieBossState.WishTelegraph:
                    _geState = GenieBossState.WishTelegraph;
                    _geStateTimer = 1.3f;
                    _geWishTargetPos = playerPos;
                    Program.AddNotification(new Notification(" Le Génie formule un voeu... ÉLOIGNEZ-VOUS !", new Color(255, 120, 60, 255), 1.6f));
                    break;
            }
        }

        private void TeleportGenieAroundPlayer(Vector2 playerPos, HashSet<string> destroyed)
        {
            SpawnGenieSmoke(WorldPos, 1f);
            for (int i = 0; i < 24; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float dist = GE_TELEPORT_MIN_DIST + (float)(Random.Shared.NextDouble() * (GE_TELEPORT_MAX_DIST - GE_TELEPORT_MIN_DIST));
                Vector2 candidate = playerPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                int tileX = (int)(candidate.X / Program.TileSize);
                int tileY = (int)(candidate.Y / Program.TileSize);
                int groundId = World.GetGroundTileIdAt(tileX, tileY);
                var ground = WorldTileRegistry.GetTile(groundId);
                if (ground == null || !ground.Walkable) continue;
                if (World.GetObjectIdAt(tileX, tileY) != 0) continue;
                if (World.IsCollidingEntity(candidate, destroyed, Species)) continue;
                WorldPos = candidate;
                SpawnGenieSmoke(candidate, 1f);
                return;
            }
        }

        private void SpawnGenieClones(Vector2 playerPos, HashSet<string> destroyed)
        {
            int cloneCount = _gePhase == 1 ? 2 : 3;
            for (int i = 0; i < cloneCount; i++)
            {
                float angle = (float)(i * (Math.PI * 2f / cloneCount) + Random.Shared.NextDouble() * 0.6);
                float dist = 90f + (float)(Random.Shared.NextDouble() * 60f);
                Vector2 pos = WorldPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                int tileX = (int)(pos.X / Program.TileSize);
                int tileY = (int)(pos.Y / Program.TileSize);
                int groundId = World.GetGroundTileIdAt(tileX, tileY);
                var ground = WorldTileRegistry.GetTile(groundId);
                if (ground == null || !ground.Walkable) continue;

                var clone = new Entity(pos, "Genie", false)
                {
                    CustomName = "Illusion du Génie",
                    IsGenieIllusion = true,
                    IsBoss = false,
                    Behavior = "hostile",
                    Attack = Math.Max(2, Attack / 3),
                    VisionRange = 6,
                    AttackHitRange = 42f,
                    AlertDuration = 0.4f,
                    Speed = 140f,
                    BaseSpeed = 140f,
                    RunSpeed = 180f,
                    Scale = 1.05f,
                    // léger scintillement pour distinguer une illusion du vrai génie, comme demandé
                    Tint = new Color(255, 255, 255, 210)
                };
                clone.SetHealthSilently(1, 1);
                Program.GetEntities().Add(clone);
                var chunk = World.GetChunkAt(tileX, tileY);
                if (chunk != null) chunk.Entities.Add(clone);
                _geClones.Add(clone);
                SpawnGenieSmoke(pos, 0.7f);
            }
            _geCloneCleanupTimer = 7f;
        }

        private void UpdateGenieClones(float dt)
        {
            if (_geClones.Count == 0) return;
            _geCloneCleanupTimer -= dt;
            if (_geCloneCleanupTimer <= 0f)
            {
                foreach (var clone in _geClones)
                {
                    if (clone != null && clone.IsAlive)
                    {
                        SpawnGenieSmoke(clone.WorldPos, 0.5f);
                        clone.SetHealthSilently(0, clone.MaxHP); // se dissipe, comme une illusion qui s'évanouit
                    }
                }
                _geClones.Clear();
            }
            else
            {
                _geClones.RemoveAll(c => c == null || !c.IsAlive);
            }
        }

        private void FireGenieSalvo(Vector2 playerPos)
        {
            float hpPercent = Math.Clamp((float)CurrentHP / Math.Max(1, MaxHP), 0f, 1f);
            int count = Math.Clamp(5 + (int)Math.Round((1f - hpPercent) * 7f), 5, 12);
            float speed = 230f + (1f - hpPercent) * 130f;
            Color color = new Color(230, 190, 255, 255); // violet-doré, distinct des projectiles de Khamsin

            Vector2 toPlayer = World.GetEntityGroundPosition(playerPos) - World.GetEntityGroundPosition(WorldPos);
            Vector2 origin = World.GetEntityGroundPosition(WorldPos);
            float baseAngle = toPlayer.Length() > 0.01f ? MathF.Atan2(toPlayer.Y, toPlayer.X) : 0f;
            float spread = MathF.PI * 0.6f; // éventail dirigé vers le joueur plutôt qu'un cercle complet

            for (int i = 0; i < count; i++)
            {
                float t = count <= 1 ? 0.5f : (float)i / (count - 1);
                float angle = baseAngle - spread / 2f + spread * t;
                Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var projectile = new KhamsinProjectile(origin, dir, speed, Math.Max(5f, Attack * 0.5f), color, color);
                Program.AddSpellProjectile(projectile);
            }
            Program.ApplyScreenShake(2f, 0.08f);
        }

        private void FireGenieLaserTick()
        {
            // 4 faisceaux en croix qui tournent ensemble : un rayon "laser" simulé par une rafale
            // continue de petits projectiles rapides, alignés sur _geLaserAngle.
            Color color = new Color(255, 230, 120, 255);
            float speed = 420f;
            for (int arm = 0; arm < 4; arm++)
            {
                float angle = _geLaserAngle + arm * (MathF.PI / 2f);
                Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var projectile = new KhamsinProjectile(
                    World.GetEntityGroundPosition(WorldPos), dir, speed, Math.Max(4f, Attack * 0.4f), color, color);
                Program.AddSpellProjectile(projectile);
            }
        }

        private void SpawnGenieHazardWave(Vector2 playerPos, bool isSand)
        {
            int count = isSand ? (4 + Random.Shared.Next(0, 2)) : (5 + Random.Shared.Next(0, 2));
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float dist = (float)(Random.Shared.NextDouble() * 220f);
                Vector2 pos = playerPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                _geHazardZones.Add(new GenieHazardZone
                {
                    Pos = pos,
                    Radius = isSand ? 65f : 78f,
                    TelegraphTimer = isSand ? 0.9f : 0.55f,
                    ActiveTimer = isSand ? 2.4f : 2.8f,
                    IsSand = isSand,
                    DamageTickTimer = 0f
                });
            }
        }

        private void UpdateGenieHazardZones(float dt, Vector2 playerPos)
        {
            if (_geHazardZones.Count == 0) return;
            for (int i = _geHazardZones.Count - 1; i >= 0; i--)
            {
                var zone = _geHazardZones[i];
                if (zone.TelegraphTimer > 0f)
                {
                    zone.TelegraphTimer -= dt;
                    if (Random.Shared.NextDouble() < 0.3)
                    {
                        Color warnColor = zone.IsSand ? new Color(210, 180, 120, 160) : new Color(255, 110, 40, 160);
                        Program.GetParticleList().Add(new Particle(
                            World.GetEntityGroundPosition(zone.Pos) + new Vector2((float)(Random.Shared.NextDouble() - 0.5) * zone.Radius, (float)(Random.Shared.NextDouble() - 0.5) * zone.Radius * 0.5f),
                            new Vector2(0, -12f), warnColor, 5f, 0.3f));
                    }
                    if (zone.TelegraphTimer <= 0f) { /* devient active dès la frame suivante */ }
                    continue;
                }

                if (zone.ActiveTimer > 0f)
                {
                    zone.ActiveTimer -= dt;
                    zone.DamageTickTimer -= dt;

                    // Particules d'ambiance de la zone active
                    if (Random.Shared.NextDouble() < 0.5)
                    {
                        Color fxColor = zone.IsSand ? new Color(220, 190, 130, 200) : new Color(255, 130, 30, 220);
                        float rx = (float)(Random.Shared.NextDouble() - 0.5) * zone.Radius * 1.6f;
                        float ry = (float)(Random.Shared.NextDouble() - 0.5) * zone.Radius * 0.8f;
                        Program.GetParticleList().Add(new Particle(
                            World.GetEntityGroundPosition(zone.Pos) + new Vector2(rx, ry),
                            new Vector2(0, -20f), fxColor, 6f, 0.4f));
                    }

                    float dist = Vector2.Distance(zone.Pos, playerPos);
                    if (dist <= zone.Radius && zone.DamageTickTimer <= 0f)
                    {
                        float dmg = zone.IsSand ? Math.Max(3f, Attack * 0.25f) : Math.Max(6f, Attack * 0.5f);
                        Program.DamagePlayerTarget((int)MathF.Ceiling(dmg), AiTargetConnectionId, zone.IsSand ? "un tourbillon de sable" : "une flaque de lave", Program.InferDeathCauseFromSpecies(zone.IsSand ? "worm" : "khamsin"));
                        zone.DamageTickTimer = 0.5f;
                    }
                }

                if (zone.IsExpired) _geHazardZones.RemoveAt(i);
            }
        }

        private void SpawnGenieWishWarningParticles(Vector2 pos)
        {
            for (int i = 0; i < 6; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float dist = 60f + (float)(Random.Shared.NextDouble() * 40f);
                Vector2 visualPos = World.GetEntityGroundPosition(pos);
                Vector2 spawnPos = visualPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
                Vector2 velocity = (visualPos - spawnPos) * 1.8f; // converge vers le centre : lecture claire du danger
                Program.GetParticleList().Add(new Particle(spawnPos, velocity, new Color(255, 90, 40, 220), 6f, 0.35f));
            }
        }

        private void ExecuteGenieWishStrike(Vector2 playerPos)
        {
            float radius = 130f;
            Vector2 strikeVisualPos = World.GetEntityGroundPosition(_geWishTargetPos);
            SpawnGenieSmoke(_geWishTargetPos, 2.2f);
            Program.ApplyScreenShake(9f, 0.4f);

            for (int i = 0; i < 40; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float speed = 80f + (float)(Random.Shared.NextDouble() * 220f);
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                Program.GetParticleList().Add(new Particle(strikeVisualPos, velocity, new Color(255, 200, 90, 255), 9f, 0.5f));
            }

            float dist = Vector2.Distance(_geWishTargetPos, playerPos);
            if (dist <= radius)
            {
                float dmg = Math.Max(25f, Attack * 2.2f);
                Program.DamagePlayerTarget((int)MathF.Ceiling(dmg), AiTargetConnectionId, "le voeu du Génie", Program.InferDeathCauseFromSpecies("khamsin"));
            }
        }

        public static void SpawnGenieSmoke(Vector2 pos, float intensity)
        {
            pos = World.GetEntityGroundPosition(pos);
            int count = (int)Math.Round(30f + intensity * 24f);
            float spreadX = 30f + intensity * 16f;
            float spreadY = 50f + intensity * 24f;
            float baseHeight = pos.Y - 34f;

            for (int i = 0; i < count; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float speed = 26f + (float)(Random.Shared.NextDouble() * 100f) * intensity;
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                velocity.Y -= 16f + (float)(Random.Shared.NextDouble() * 14f);

                float offsetX = (float)(Random.Shared.NextDouble() - 0.5) * spreadX;
                float offsetY = (float)(Random.Shared.NextDouble() - 0.5) * spreadY;
                Vector2 spawnPos = new Vector2(pos.X + offsetX, baseHeight + offsetY);

                float size = 7f + (float)(Random.Shared.NextDouble() * 11f) + intensity * 5f;
                float lifetime = 0.35f + (float)(Random.Shared.NextDouble() * 0.4f);
                // Fumée dorée/violette caractéristique du Génie, plutôt que le gris de Khamsin
                Color color = Random.Shared.NextDouble() < 0.5
                    ? new Color(255, 210, 110, 220)
                    : new Color(190, 120, 220, 210);
                Program.GetParticleList().Add(new Particle(spawnPos, velocity, color, size, lifetime));
            }
        }

        private void AddWormGroundMark(WormGroundMarkType type, Vector2 position, float radius, float lifetime)
        {
            if (_wmGroundMarks.Count >= 48)
                _wmGroundMarks.RemoveAt(0);

            _wmGroundMarks.Add(new WormGroundMark(
                type, position, radius, 0f, lifetime,
                (float)(Random.Shared.NextDouble() * Math.PI * 2f)));
        }

        private void UpdateWormGroundMarks(float dt)
        {
            for (int i = _wmGroundMarks.Count - 1; i >= 0; i--)
            {
                WormGroundMark mark = _wmGroundMarks[i] with { Age = _wmGroundMarks[i].Age + dt };
                if (mark.Age >= mark.Lifetime)
                    _wmGroundMarks.RemoveAt(i);
                else
                    _wmGroundMarks[i] = mark;
            }
        }

        private void SpawnWormDebris(Vector2 position, float intensity)
        {
            Vector2 ground = World.GetEntityGroundPosition(position);
            int tileX = (int)(position.X / Program.TileSize);
            int tileY = (int)(position.Y / Program.TileSize);
            Color soil = World.GetParticleColorForGround(World.GetGroundTileIdAt(tileX, tileY));
            int count = (int)MathF.Round(12f + intensity * 12f);

            for (int i = 0; i < count; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float speed = 35f + (float)Random.Shared.NextDouble() * (60f + intensity * 70f);
                Vector2 velocity = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed - 30f);
                float size = 3f + (float)Random.Shared.NextDouble() * (4f + intensity * 3f);
                Particle debris = new(
                    ground + new Vector2((float)(Random.Shared.NextDouble() - 0.5) * 24f, -2f),
                    velocity, soil, size, 0.45f + (float)Random.Shared.NextDouble() * 0.55f)
                {
                    GravityY = 260f,
                    Drag = 0.94f,
                    GroundY = ground.Y + 8f
                };
                Program.GetParticleList().Add(debris);
            }
        }

    }
}
