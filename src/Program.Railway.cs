#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static partial class Program
    {

        private static bool TryHandleWagonInteraction()
        {
            if (!PlayerCurrentWagonTile.HasValue && PlayerCurrentCar != null) return false;

            if (PlayerCurrentWagonTile is { } currentTile)
            {
                _looseWagons[currentTile] = new LooseWagonState
                {
                    Tile = currentTile,
                    Position = _wagonPos,
                    Heading = _wagonHeading,
                    Speed = _wagonSpeed
                };
                PlayerCurrentWagonTile = null;
                ResetWagonPhysics();

                int[,] offsets = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
                for (int i = 0; i < offsets.GetLength(0); i++)
                {
                    int tileX = currentTile.x + offsets[i, 0];
                    int tileY = currentTile.y + offsets[i, 1];
                    int objectId = World.GetObjectIdAt(tileX, tileY);
                    if (objectId != 0 && objectId != 123) continue;
                    var ground = WorldTileRegistry.GetTile(World.GetGroundTileIdAt(tileX, tileY));
                    if (ground?.Walkable != true) continue;

                    _playerPos = new Vector2(tileX * TileSize + TileSize / 2f, tileY * TileSize + TileSize / 2f);
                    return true;
                }

                _playerPos = GetWagonWorldPosition(currentTile) + new Vector2(TileSize, 0);
                return true;
            }

            int playerTileX = (int)MathF.Floor(_playerPos.X / TileSize);
            int playerTileY = (int)MathF.Floor(_playerPos.Y / TileSize);
            float nearestDistance = 90f;
            (int x, int y)? nearestWagon = null;

            for (int x = playerTileX - 2; x <= playerTileX + 2; x++)
            {
                for (int y = playerTileY - 2; y <= playerTileY + 2; y++)
                {
                    if (World.GetObjectIdAt(x, y) != 124) continue;
                    Vector2 wagonPosition = GetWagonWorldPosition((x, y));
                    float distance = Vector2.Distance(_playerPos, wagonPosition);
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    nearestWagon = (x, y);
                }
            }

            if (!nearestWagon.HasValue) return false;
            PlayerCurrentWagonTile = nearestWagon;
            if (_looseWagons.Remove(nearestWagon.Value, out var looseWagon))
            {
                _wagonPos = looseWagon.Position;
                _wagonHeading = looseWagon.Heading;
                _wagonSpeed = looseWagon.Speed;
            }
            else
            {
                _wagonPos = GetWagonWorldPosition(nearestWagon.Value);
                ResetWagonPhysics();
            }
            _playerPos = _wagonPos;
            AddNotification(new Notification("Vous montez dans le wagon.", new Color(100, 200, 255, 255), 2f));
            return true;
        }

        // ---------- Physique du wagonnet ----------
        // Tout est exprimé en tuiles/s (converti en pixels avec TileSize).
        private const float WagonMaxSpeedTiles = 7.0f;   // vitesse max
        private const float WagonPushMaxSpeedTiles = 4.0f;
        private const float WagonAccelTiles = 4.0f;      // accélération quand on pousse
        private const float WagonFrictionTiles = 2.2f;   // ralentissement naturel sans touche
        private const float WagonBrakeTiles = 9.0f;      // freinage (touche opposée)
        private const float WagonCornerSpeedTiles = 3.5f;// vitesse max conservée dans un virage
        private const float WagonTurnBufferTime = 0.35f; // mémorisation d'un virage demandé
        private const float WagonRestitution = 0.12f;
        private const float WagonPushResistance = 150f;
        private const float WagonDamageThresholdTiles = 3.0f;
        private const float WagonDamageCooldown = 0.8f;
        // Tolérance (en fraction de tuile) autour du centre d'une tuile pour prendre un virage :
        // - à l'arrêt : on peut partir dans la direction perpendiculaire si on est assez proche du centre ;
        // - en roulant : on peut encore tourner juste après avoir dépassé le centre.
        // Dans les deux cas le wagon est recalé sur la ligne de la nouvelle voie. Doit rester < 0.5.
        private const float WagonTurnTolerance = 0.3f;

        private static Texture2D _wagonHorizontalTexture;
        private static Texture2D _wagonVerticalTexture;
        private static Texture2D _wagonHorizontalFrontTexture;
        private static Texture2D _wagonVerticalFrontTexture;
        private static bool _wagonTexturesLoaded;

        private static Vector2 _wagonPos;                // position réelle (continue)
        private static float _wagonSpeed;                // vitesse (>= 0) le long du cap
        private static (int x, int y) _wagonHeading = (1, 0);
        private static (int x, int y)? _wagonTurnBuffer;
        private static float _wagonTurnTimer;

        private sealed class LooseWagonState
        {
            public (int x, int y) Tile;
            public Vector2 Position;
            public (int x, int y) Heading;
            public float Speed;
            public Dictionary<Entity, float> DamageCooldowns = new();
        }

        private static readonly Dictionary<(int x, int y), LooseWagonState> _looseWagons = new();
        private static readonly Dictionary<Entity, float> _mountedWagonDamageCooldowns = new();

        private static void ResetWagonPhysics()
        {
            _wagonSpeed = 0f;
            _wagonHeading = (1, 0);
            _wagonTurnBuffer = null;
            _wagonTurnTimer = 0f;
        }

        private static bool CanWagonEnter(int tileX, int tileY)
        {
            int id = World.GetObjectIdAt(tileX, tileY);
            return World.IsRailwayAt(tileX, tileY) && (id == 0 || id == 123);
        }

        private static void UpdateLooseWagons(float dt)
        {
            foreach (var pair in _looseWagons.ToArray())
            {
                if (World.GetObjectIdAt(pair.Key.x, pair.Key.y) != 124)
                    _looseWagons.Remove(pair.Key);
            }

            foreach (var wagon in _looseWagons.Values.ToArray())
            {
                TickWagonDamageCooldowns(wagon.DamageCooldowns, dt);
                if (wagon.Speed <= 0f) continue;
                wagon.Speed = Math.Max(0f, wagon.Speed - WagonFrictionTiles * TileSize * dt);
                if (wagon.Speed > 0f) AdvanceLooseWagon(wagon, dt);
            }
        }

        private static void AdvanceLooseWagon(LooseWagonState wagon, float dt)
        {
            float remaining = wagon.Speed * dt;
            float ts = TileSize;
            const float eps = 0.01f;

            for (int guard = 0; guard < 16 && remaining > eps; guard++)
            {
                var (hx, hy) = wagon.Heading;
                Vector2 center = GetWagonWorldPosition(wagon.Tile);
                Vector2 toCenter = center - wagon.Position;
                float ahead = toCenter.X * hx + toCenter.Y * hy;

                if (Math.Abs(toCenter.X) + Math.Abs(toCenter.Y) < eps)
                {
                    wagon.Position = center;
                    int nextX = wagon.Tile.x + hx;
                    int nextY = wagon.Tile.y + hy;
                    if (!CanWagonEnter(nextX, nextY))
                    {
                        if (World.GetObjectIdAt(nextX, nextY) == 124)
                            ResolveLooseWagonCollision(wagon, (nextX, nextY));
                        else
                            BounceLooseWagon(wagon);
                        return;
                    }
                    ahead = 0f;
                }

                float distance = ahead > eps ? ahead : ts + ahead;
                float step = Math.Min(remaining, distance);
                wagon.Position += new Vector2(hx, hy) * step;
                remaining -= step;
                if (step >= distance - 1e-4f)
                    wagon.Position = GetWagonWorldPosition(WorldToTile(wagon.Position));
                SyncLooseWagonTile(wagon);
                DamageEntitiesHitByWagon(wagon.Position, wagon.Heading, wagon.Speed, wagon.DamageCooldowns);
            }
        }

        private static void ResolveLooseWagonCollision(LooseWagonState first, (int x, int y) targetTile)
        {
            var direction = first.Heading;
            float secondVelocity;
            LooseWagonState? second = null;
            bool hitMountedWagon = PlayerCurrentWagonTile is { } mountedTile && mountedTile == targetTile;

            if (hitMountedWagon)
            {
                int headingDot = _wagonHeading.x * direction.x + _wagonHeading.y * direction.y;
                secondVelocity = _wagonSpeed * headingDot;
            }
            else
            {
                second = GetOrCreateLooseWagon(targetTile);
                int headingDot = second.Heading.x * direction.x + second.Heading.y * direction.y;
                secondVelocity = second.Speed * headingDot;
            }

            (float firstAfter, float secondAfter) = ResolveImpact(first.Speed, secondVelocity);
            ApplyLooseVelocity(first, firstAfter, direction);
            if (hitMountedWagon)
                ApplyMountedVelocity(secondAfter, direction);
            else if (second != null)
                ApplyLooseVelocity(second, secondAfter, direction);
        }

        private static void ResolveMountedWagonCollision((int x, int y) targetTile)
        {
            var direction = _wagonHeading;
            LooseWagonState target = GetOrCreateLooseWagon(targetTile);
            int headingDot = target.Heading.x * direction.x + target.Heading.y * direction.y;
            (float firstAfter, float secondAfter) = ResolveImpact(_wagonSpeed, target.Speed * headingDot);
            ApplyMountedVelocity(firstAfter, direction);
            ApplyLooseVelocity(target, secondAfter, direction);
        }

        private static (float first, float second) ResolveImpact(float firstVelocity, float secondVelocity)
        {
            float relativeSpeed = firstVelocity - secondVelocity;
            if (relativeSpeed <= 0f) return (firstVelocity, secondVelocity);
            if (Math.Abs(secondVelocity) < 0.01f)
                return (-firstVelocity * WagonRestitution, firstVelocity * (1f + WagonRestitution));
            float impulse = (1f + WagonRestitution) * relativeSpeed * 0.5f;
            return (firstVelocity - impulse, secondVelocity + impulse);
        }

        private static void ApplyMountedVelocity(float signedSpeed, (int x, int y) direction)
        {
            _wagonHeading = signedSpeed < 0f ? (-direction.x, -direction.y) : direction;
            _wagonSpeed = Math.Clamp(Math.Abs(signedSpeed), 0f, WagonMaxSpeedTiles * TileSize);
        }

        private static void ApplyLooseVelocity(LooseWagonState wagon, float signedSpeed, (int x, int y) direction)
        {
            wagon.Heading = signedSpeed < 0f ? (-direction.x, -direction.y) : direction;
            wagon.Speed = Math.Clamp(Math.Abs(signedSpeed), 0f, WagonMaxSpeedTiles * TileSize);
        }

        private static void BounceMountedWagon()
        {
            _wagonHeading = (-_wagonHeading.x, -_wagonHeading.y);
            _wagonSpeed *= WagonRestitution;
        }

        private static void BounceLooseWagon(LooseWagonState wagon)
        {
            wagon.Heading = (-wagon.Heading.x, -wagon.Heading.y);
            wagon.Speed *= WagonRestitution;
        }

        private static LooseWagonState GetOrCreateLooseWagon((int x, int y) tile)
        {
            if (_looseWagons.TryGetValue(tile, out var wagon)) return wagon;
            bool hasVerticalRail = World.IsRailwayAt(tile.x, tile.y - 1) || World.IsRailwayAt(tile.x, tile.y + 1);
            bool hasHorizontalRail = World.IsRailwayAt(tile.x - 1, tile.y) || World.IsRailwayAt(tile.x + 1, tile.y);
            wagon = new LooseWagonState
            {
                Tile = tile,
                Position = GetWagonWorldPosition(tile),
                Heading = hasVerticalRail && !hasHorizontalRail ? (0, 1) : (1, 0)
            };
            _looseWagons[tile] = wagon;
            return wagon;
        }

        private static IEnumerable<LooseWagonState> GetNearbyLooseWagons(Vector2 position)
        {
            int tileX = (int)MathF.Floor(position.X / TileSize);
            int tileY = (int)MathF.Floor(position.Y / TileSize);
            for (int x = tileX - 1; x <= tileX + 1; x++)
            {
                for (int y = tileY - 1; y <= tileY + 1; y++)
                {
                    if (World.GetObjectIdAt(x, y) != 124 || IsWagonMountedAt(x, y)) continue;
                    yield return GetOrCreateLooseWagon((x, y));
                }
            }
        }

        private static Rectangle GetWagonHitbox(Vector2 position, (int x, int y) heading)
        {
            bool vertical = heading.y != 0;
            float width = TileSize * (vertical ? 0.55f : 0.85f);
            float height = TileSize * (vertical ? 0.85f : 0.55f);
            return World.GetEntityCollisionHitbox(position, new Vector2(width, height), TileSize);
        }

        private static Rectangle GetWagonPushHitbox(LooseWagonState wagon)
            => GetWagonHitbox(wagon.Position, wagon.Heading);

        private static void TickWagonDamageCooldowns(Dictionary<Entity, float> cooldowns, float dt)
        {
            foreach (var entity in cooldowns.Keys.ToArray())
            {
                float remaining = cooldowns[entity] - dt;
                if (remaining <= 0f || !entity.IsAlive)
                    cooldowns.Remove(entity);
                else
                    cooldowns[entity] = remaining;
            }
        }

        private static void DamageEntitiesHitByWagon(
            Vector2 position,
            (int x, int y) heading,
            float speed,
            Dictionary<Entity, float> cooldowns)
        {
            if (speed < WagonDamageThresholdTiles * TileSize) return;

            Rectangle wagonHitbox = GetWagonHitbox(position, heading);
            Vector2 impactDirection = Vector2.Normalize(new Vector2(heading.x, heading.y));
            int damage = Math.Clamp((int)(speed / TileSize * 2f), 4, 14);
            foreach (Entity entity in GetEntities())
            {
                if (!entity.IsAlive || entity.IsPlayer || entity.CarriedByConnectionId != -1
                    || cooldowns.ContainsKey(entity))
                    continue;

                Rectangle entityHitbox = World.GetEntityCollisionHitbox(entity.VisualWorldPos, entity.Species, TileSize);
                if (!Raylib.CheckCollisionRecs(wagonHitbox, entityHitbox)) continue;

                entity.CurrentHP = Math.Max(0, entity.CurrentHP - damage);
                entity.OnHit(position, attackerIsPlayer: false);
                entity.ApplyKnockback(impactDirection, speed * 0.35f);
                AddFloatingDamage(entity.WorldPos - new Vector2(0f, 20f), damage, damage >= 10);
                cooldowns[entity] = WagonDamageCooldown;

                if (!entity.IsAlive)
                {
                    foreach (var (id, quantity, _) in GameData.GetAnimalDrops(entity.Species))
                        GiveItemToPlayer(id, quantity, entity.WorldPos);
                }
            }
        }

        private static void PushLooseWagon(
            LooseWagonState wagon,
            Rectangle pusherHitbox,
            Vector2 attemptedMove,
            float dt,
            Entity? pusherEntity = null,
            bool pusherIsPlayer = false)
        {
            Rectangle wagonHitbox = GetWagonPushHitbox(wagon);
            if (!Raylib.CheckCollisionRecs(pusherHitbox, wagonHitbox)) return;

            float overlapX = MathF.Min(pusherHitbox.X + pusherHitbox.Width, wagonHitbox.X + wagonHitbox.Width)
                - MathF.Max(pusherHitbox.X, wagonHitbox.X);
            float overlapY = MathF.Min(pusherHitbox.Y + pusherHitbox.Height, wagonHitbox.Y + wagonHitbox.Height)
                - MathF.Max(pusherHitbox.Y, wagonHitbox.Y);
            if (overlapX <= 0f || overlapY <= 0f) return;

            Vector2 pusherCenter = new(pusherHitbox.X + pusherHitbox.Width / 2f, pusherHitbox.Y + pusherHitbox.Height / 2f);
            Vector2 wagonCenter = new(wagonHitbox.X + wagonHitbox.Width / 2f, wagonHitbox.Y + wagonHitbox.Height / 2f);
            Vector2 awayFromWagon = pusherCenter - wagonCenter;
            float centerDistance = awayFromWagon.Length();
            if (centerDistance > 0.001f)
                awayFromWagon /= centerDistance;
            else
                awayFromWagon = new Vector2(-wagon.Heading.x, -wagon.Heading.y);

            float supportDistance =
                MathF.Abs(awayFromWagon.X) * (pusherHitbox.Width + wagonHitbox.Width) / 2f
                + MathF.Abs(awayFromWagon.Y) * (pusherHitbox.Height + wagonHitbox.Height) / 2f;
            float proximity = Math.Clamp(1f - centerDistance / MathF.Max(0.001f, supportDistance), 0f, 1f);
            float penetration = MathF.Min(overlapX, overlapY);
            Vector2 separation = awayFromWagon * MathF.Min(0.5f, penetration * 0.05f);

            if (pusherIsPlayer)
                ApplyEntityPush(separation);
            else if (pusherEntity != null)
            {
                Vector2 separatedX = pusherEntity.WorldPos + new Vector2(separation.X, 0f);
                if (!World.IsCollidingEntity(separatedX, GetDestroyedObjects(), pusherEntity.Species))
                    pusherEntity.WorldPos.X = separatedX.X;
                Vector2 separatedY = pusherEntity.WorldPos + new Vector2(0f, separation.Y);
                if (!World.IsCollidingEntity(separatedY, GetDestroyedObjects(), pusherEntity.Species))
                    pusherEntity.WorldPos.Y = separatedY.Y;
            }

            float pushAcceleration = proximity * proximity * 1200f;
            Vector2 push = awayFromWagon * (pushAcceleration * dt * 0.5f);
            if (pusherIsPlayer)
                ApplyEntityPush(push);
            else if (pusherEntity != null)
            {
                pusherEntity.ApplyPushImpulse(push);
            }

            Vector2 railAxis = wagon.Heading.y != 0 ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
            float pushDirection = Vector2.Dot(attemptedMove, railAxis);
            if (MathF.Abs(pushDirection) < 0.001f)
                pushDirection = -Vector2.Dot(awayFromWagon, railAxis);
            if (MathF.Abs(pushDirection) < 0.001f)
                pushDirection = 1f;

            wagon.Heading = pushDirection < 0f
                ? (-((int)railAxis.X), -((int)railAxis.Y))
                : (((int)railAxis.X), ((int)railAxis.Y));

            float movementSpeed = attemptedMove.Length() / MathF.Max(dt, 0.0001f);
            float acceleration;
            if (pusherIsPlayer)
            {
                float pushForce = movementSpeed * 0.9f + proximity * proximity * 160f;
                if (pushForce <= WagonPushResistance * 0.8f) return;
                acceleration = Math.Clamp((pushForce - WagonPushResistance * 0.8f) * 2f, 160f, 350f);
            }
            else
            {
                float pushForce = movementSpeed * 0.7f + proximity * proximity * 300f;
                if (pushForce <= WagonPushResistance) return;
                acceleration = MathF.Min(500f, (pushForce - WagonPushResistance) * 0.8f);
            }

            wagon.Speed = Math.Clamp(wagon.Speed + acceleration * dt, 0f, WagonPushMaxSpeedTiles * TileSize);
        }

        public static void ApplyPlayerWagonPush(Vector2 playerPosition, Vector2 attemptedMove, float dt)
        {
            if (NetworkManager.IsClient || attemptedMove.LengthSquared() < 0.0001f) return;
            if (PlayerCurrentWagonTile.HasValue) return; // dans un wagon : on ne pousse pas ses propres wagons liés

            Rectangle playerHitbox = World.GetEntityCollisionHitbox(
                playerPosition + attemptedMove,
                GetPlayerCollisionDimensions(),
                TileSize);
            foreach (LooseWagonState wagon in GetNearbyLooseWagons(playerPosition + attemptedMove))
                PushLooseWagon(wagon, playerHitbox, attemptedMove, dt, pusherIsPlayer: true);
        }

        public static void ResolveWagonPushes(float dt)
        {
            if (NetworkManager.IsClient) return;

            foreach (Entity entity in GetEntities())
            {
                if (!entity.IsAlive || entity.CarriedByConnectionId != -1) continue;
                if (entity.IsPlayer && PlayerCurrentWagonTile.HasValue) continue;
                Rectangle entityHitbox = World.GetEntityCollisionHitbox(entity.VisualWorldPos, entity.Species, TileSize);
                foreach (LooseWagonState wagon in GetNearbyLooseWagons(entity.VisualWorldPos))
                {
                    Rectangle wagonHitbox = GetWagonPushHitbox(wagon);
                    if (!Raylib.CheckCollisionRecs(entityHitbox, wagonHitbox)) continue;

                    PushLooseWagon(wagon, entityHitbox, Vector2.Zero, dt, pusherEntity: entity);
                }
            }
        }

        private static void UpdateMountedWagon(float dt)
        {
            if (PlayerCurrentWagonTile is not { } startTile) return;
            TickWagonDamageCooldowns(_mountedWagonDamageCooldowns, dt);
            float ts = TileSize;
            const float eps = 0.01f;

            // --- Entrées ---
            int dx = 0, dy = 0;
            if (KeyBindings.IsDown(GameAction.MoveUp)) dy--;
            if (KeyBindings.IsDown(GameAction.MoveDown)) dy++;
            if (KeyBindings.IsDown(GameAction.MoveLeft)) dx--;
            if (KeyBindings.IsDown(GameAction.MoveRight)) dx++;

            var (hx, hy) = _wagonHeading;
            bool headingHorizontal = hx != 0;
            int along = dx * hx + dy * hy;                                   // -1, 0, +1
            (int x, int y) perp = headingHorizontal ? (0, dy) : (dx, 0);     // direction perpendiculaire demandée
            bool hasPerp = perp.x != 0 || perp.y != 0;

            _wagonTurnTimer = Math.Max(0f, _wagonTurnTimer - dt);
            if (hasPerp) { _wagonTurnBuffer = perp; _wagonTurnTimer = WagonTurnBufferTime; }
            else if (_wagonTurnTimer <= 0f) _wagonTurnBuffer = null;

            Vector2 tileCenter = GetWagonWorldPosition(startTile);
            float centerDistance = Vector2.Distance(_wagonPos, tileCenter);
            bool atCenter = centerDistance < 0.5f;
            bool nearCenter = centerDistance <= WagonTurnTolerance * ts;

            // --- Vitesse ---
            if (_wagonSpeed <= eps)
            {
                _wagonSpeed = 0f;
                // Démarrage : on essaie d'abord l'axe du cap (dans les deux sens), puis un virage si on est au centre.
                (int x, int y)? want = null;
                if (along != 0) want = (hx * along, hy * along);
                else if (hasPerp && nearCenter) want = perp;

                if (want is { } w)
                {
                    bool isPerpStart = w == perp && hasPerp && along == 0;
                    bool ok = isPerpStart
                        ? CanWagonEnter(startTile.x + w.x, startTile.y + w.y)
                        : !atCenter || CanWagonEnter(startTile.x + w.x, startTile.y + w.y);
                    if (!ok && along != 0 && hasPerp && nearCenter
                        && CanWagonEnter(startTile.x + perp.x, startTile.y + perp.y))
                    {
                        w = perp; ok = true; isPerpStart = true;
                    }
                    if (ok)
                    {
                        // Virage à l'arrêt : on recale le wagon sur le centre de la tuile (nouvelle voie).
                        if (isPerpStart) _wagonPos = tileCenter;
                        _wagonHeading = w;
                        _wagonSpeed = WagonAccelTiles * ts * dt;
                    }
                }
            }
            else
            {
                if (along > 0) _wagonSpeed += WagonAccelTiles * ts * dt;
                else if (along < 0) _wagonSpeed -= WagonBrakeTiles * ts * dt;
                else _wagonSpeed -= WagonFrictionTiles * ts * dt;
                _wagonSpeed = Math.Clamp(_wagonSpeed, 0f, WagonMaxSpeedTiles * ts);
            }

            if (_wagonSpeed <= 0f) return;

            // --- Déplacement continu le long des rails ---
            float remaining = _wagonSpeed * dt;
            for (int guard = 0; guard < 16 && remaining > eps; guard++)
            {
                if (PlayerCurrentWagonTile is not { } tile) return;
                (hx, hy) = _wagonHeading;
                Vector2 center = GetWagonWorldPosition(tile);
                Vector2 toCenter = center - _wagonPos;
                float ahead = toCenter.X * hx + toCenter.Y * hy;

                if (Math.Abs(toCenter.X) + Math.Abs(toCenter.Y) < eps)
                {
                    // Au centre d'une tuile : on peut tourner, sinon vérifier que la suite est libre.
                    _wagonPos = center;
                    if (_wagonTurnBuffer is { } turn && CanWagonEnter(tile.x + turn.x, tile.y + turn.y))
                    {
                        _wagonHeading = turn;
                        _wagonTurnBuffer = null;
                        _wagonSpeed = Math.Min(_wagonSpeed, WagonCornerSpeedTiles * ts);
                        (hx, hy) = turn;
                    }
                    if (!CanWagonEnter(tile.x + hx, tile.y + hy))
                    {
                        int nextX = tile.x + hx, nextY = tile.y + hy;
                        if (World.GetObjectIdAt(nextX, nextY) == 124)
                            ResolveMountedWagonCollision((nextX, nextY));
                        else
                            BounceMountedWagon();
                        return;
                    }
                    ahead = 0f;
                }
                else if (_wagonTurnBuffer is { } lateTurn
                    && ahead < -eps && -ahead <= WagonTurnTolerance * ts
                    && lateTurn.x * hx + lateTurn.y * hy == 0
                    && CanWagonEnter(tile.x + lateTurn.x, tile.y + lateTurn.y))
                {
                    // Virage tardif : on vient de dépasser le centre de peu. On recale le wagon sur
                    // la nouvelle voie (axe du centre) en conservant la distance déjà parcourue.
                    float overshoot = -ahead;
                    _wagonHeading = lateTurn;
                    _wagonTurnBuffer = null;
                    _wagonTurnTimer = 0f;
                    _wagonSpeed = Math.Min(_wagonSpeed, WagonCornerSpeedTiles * ts);
                    (hx, hy) = lateTurn;
                    _wagonPos = center + new Vector2(hx, hy) * overshoot;
                    ahead = -overshoot;
                }
                else if (ahead < -eps && !CanWagonEnter(tile.x + hx, tile.y + hy))
                {
                    int nextX = tile.x + hx, nextY = tile.y + hy;
                    if (World.GetObjectIdAt(nextX, nextY) == 124)
                        ResolveMountedWagonCollision((nextX, nextY));
                    else
                        BounceMountedWagon();
                    return;
                }

                // Distance jusqu'au prochain centre de tuile sur notre cap
                float dist = ahead > eps ? ahead : ts + ahead;
                float step = Math.Min(remaining, dist);
                _wagonPos += new Vector2(hx, hy) * step;
                remaining -= step;
                if (step >= dist - 1e-4f)
                    _wagonPos = GetWagonWorldPosition(WorldToTile(_wagonPos)); // recale exactement sur le centre

                SyncWagonTile();
                DamageEntitiesHitByWagon(_wagonPos, _wagonHeading, _wagonSpeed, _mountedWagonDamageCooldowns);
            }
        }

        // ---------- Attelage de wagons ----------
        // Deux wagons reliés par un rail continu (WagonLinkMaxRailDistance tuiles max) se lient avec la touche R
        // (R sur le premier, puis R sur le second).
        // Le lien se comporte comme une barre d'attelage de longueur fixe : tant que les wagons
        // sont plus proches que cette longueur il ne se passe rien (mou), dès qu'un wagon s'éloigne
        // de l'autre, celui-ci est tiré derrière lui en suivant exactement le chemin des rails.
        // Deux wagons liés sont toujours maintenus à WagonLinkSpacingTiles l'un de l'autre : trop proches,
        // le wagon à l'arrêt est repoussé (voir SpreadLinkedWagons) ; trop loin, il est tiré.
        // Écart (centre à centre, en tuiles) entre deux wagons liés. Doit rester > 1 : ainsi deux wagons
        // d'une même rame ne peuvent jamais se retrouver sur la même tuile (même dans un virage).
        private const float WagonLinkSpacingTiles = 1.3f;
        private const int WagonLinkMaxRailDistance = 8;   // distance max (en tuiles de rail) entre deux wagons pour les lier
        private const float WagonLinkSpreadSpeedTiles = 5.0f; // vitesse à laquelle des wagons fraîchement liés s'écartent

        private sealed class WagonLink
        {
            public (int x, int y) A;   // wagon "meneur" (premier clic) : un clic droit dessus retire le lien
            public (int x, int y) B;   // wagon lié (second clic)
            public float Length;       // longueur maximale du lien, mesurée le long des rails
            public List<Vector2> Path = new(); // polyligne suivie par le lien : de A vers B
        }

        private static readonly List<WagonLink> _wagonLinks = new();
        private static (int x, int y)? _wagonLinkSource; // wagon sélectionné par le premier clic droit

        private static List<WagonLinkSaveData> CaptureWagonLinks()
            => _wagonLinks.Select(link => new WagonLinkSaveData
            {
                AX = link.A.x,
                AY = link.A.y,
                BX = link.B.x,
                BY = link.B.y,
                Length = link.Length,
                Path = link.Path.Select(point => new WagonLinkPointSaveData { X = point.X, Y = point.Y }).ToList()
            }).ToList();

        private static void RestoreWagonLinks(IEnumerable<WagonLinkSaveData>? savedLinks)
        {
            _wagonLinks.Clear();
            _wagonLinkSource = null;
            if (savedLinks == null) return;

            foreach (var saved in savedLinks)
            {
                if (saved.Path == null || saved.Path.Count < 2 || saved.Length <= 0f) continue;
                _wagonLinks.Add(new WagonLink
                {
                    A = (saved.AX, saved.AY),
                    B = (saved.BX, saved.BY),
                    Length = saved.Length,
                    Path = saved.Path.Select(point => new Vector2(point.X, point.Y)).ToList()
                });
            }
        }

        private static void ClearWagonLinks()
        {
            _wagonLinks.Clear();
            _wagonLinkSource = null;
        }

        private static bool IsWagonTileChunkLoaded((int x, int y) tile)
        {
            int chunkX = tile.x >= 0 ? tile.x / World.CHUNK_SIZE : (tile.x - World.CHUNK_SIZE + 1) / World.CHUNK_SIZE;
            int chunkY = tile.y >= 0 ? tile.y / World.CHUNK_SIZE : (tile.y - World.CHUNK_SIZE + 1) / World.CHUNK_SIZE;
            return World.IsChunkLoaded(chunkX, chunkY);
        }

        /// <summary>Touche R sur le wagon sous la souris : sélection, liaison ou déliaison. Renvoie vrai si l'action a été consommée.</summary>
        private static bool TryHandleWagonLinkClick(Camera2D camera, Rectangle? viewport)
        {
            if (IsMouseOverAnyUI()) return false;

            Vector2 mouseWorld = viewport.HasValue
                ? GetViewportMouseWorldPosition(camera, viewport.Value)
                : Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);

            if (FindWagonUnderMouse(mouseWorld) is not { } tile) return false;

            if (!TryGetLinkedWagonPosition(tile, out Vector2 wagonPos)) return false;
            bool isMountedWagon = IsWagonMountedAt(tile.x, tile.y);
            // La portée du joueur ne compte que pour le premier wagon : le second peut être plus loin
            // (dans la limite WagonLinkMaxRailDistance le long des rails).
            bool completingLink = _wagonLinkSource is { } pending && pending != tile
                && World.GetObjectIdAt(pending.x, pending.y) == 124;
            if (!isMountedWagon && !completingLink && Vector2.Distance(_playerPos, wagonPos) > MAX_INTERACTION_DISTANCE)
            {
                AddNotification(new Notification("Ce wagon est trop loin.", new Color(255, 200, 100, 255), 1.5f));
                return true;
            }

            // Appuyer sur R sur le wagon qui en tire un autre retire le lien.
            WagonLink? ownLink = _wagonLinks.FirstOrDefault(l => l.A == tile);
            if (ownLink != null)
            {
                _wagonLinks.Remove(ownLink);
                _wagonLinkSource = null;
                AddNotification(new Notification("Wagons déliés.", new Color(255, 200, 100, 255), 2f));
                return true;
            }

            if (_wagonLinkSource is { } source && World.GetObjectIdAt(source.x, source.y) == 124)
            {
                _wagonLinkSource = null;
                if (source == tile)
                {
                    AddNotification(new Notification("Sélection annulée.", new Color(200, 200, 200, 255), 1.5f));
                    return true;
                }
                TryCreateWagonLink(source, tile);
                return true;
            }

            _wagonLinkSource = tile;
            AddNotification(new Notification("Wagon sélectionné : visez un autre wagon proche sur le rail et appuyez sur R pour les lier.",
                new Color(100, 200, 255, 255), 2.5f));
            return true;
        }

        private static (int x, int y)? FindWagonUnderMouse(Vector2 mouseWorld)
        {
            int ts = TileSize;
            int mouseTileX = (int)MathF.Floor(mouseWorld.X / ts);
            int mouseTileY = (int)MathF.Floor(mouseWorld.Y / ts);
            (int x, int y)? best = null;
            float bestDistance = ts * 0.9f;

            // Les wagons sont dessinés remontés selon le relief : on regarde aussi quelques tuiles plus bas.
            for (int x = mouseTileX - 2; x <= mouseTileX + 2; x++)
            {
                for (int y = mouseTileY - 2; y <= mouseTileY + 3; y++)
                {
                    if (World.GetObjectIdAt(x, y) != 124) continue;
                    if (!TryGetLinkedWagonPosition((x, y), out Vector2 position)) continue;
                    float distance = Vector2.Distance(mouseWorld, WagonLinkVisualPoint(position));
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = (x, y);
                }
            }
            return best;
        }

        private static bool TryCreateWagonLink((int x, int y) a, (int x, int y) b)
        {
            Color warning = new Color(255, 100, 100, 255);

            if (World.GetObjectIdAt(a.x, a.y) != 124 || World.GetObjectIdAt(b.x, b.y) != 124) return false;

            List<(int x, int y)>? railTiles = FindRailTilePath(a, b, WagonLinkMaxRailDistance);
            if (railTiles == null)
            {
                AddNotification(new Notification("Les wagons doivent être sur le même rail, à moins de "
                    + WagonLinkMaxRailDistance + " tuiles, sans wagon entre les deux.", warning, 2.5f));
                return false;
            }
            if (AreWagonsConnected(a, b))
            {
                AddNotification(new Notification("Ces wagons sont déjà reliés.", warning, 2f));
                return false;
            }
            if (_wagonLinks.Any(l => l.B == b))
            {
                AddNotification(new Notification("Ce wagon est déjà attelé derrière un autre wagon.", warning, 2f));
                return false;
            }
            if (!TryGetLinkedWagonPosition(a, out Vector2 posA) || !TryGetLinkedWagonPosition(b, out Vector2 posB))
                return false;

            var link = new WagonLink { A = a, B = b };
            // Polyligne le long des rails : sommets aux virages (centres de tuiles), extrémités = positions réelles.
            link.Path.Add(posA);
            for (int i = 1; i < railTiles.Count - 1; i++)
            {
                var prev = railTiles[i - 1];
                var cur = railTiles[i];
                var next = railTiles[i + 1];
                if (cur.x - prev.x == next.x - cur.x && cur.y - prev.y == next.y - cur.y) continue; // tout droit
                link.Path.Add(GetWagonWorldPosition(cur));
            }
            link.Path.Add(posB);
            link.Length = Math.Max(GetPathLength(link.Path), TileSize * WagonLinkSpacingTiles);
            _wagonLinks.Add(link);

            AddNotification(new Notification("Wagons liés !", new Color(100, 255, 100, 255), 2f));
            return true;
        }

        // Plus court chemin de tuiles de rail entre deux wagons (inclus), sans autre wagon sur le trajet.
        // Renvoie null s'il n'existe pas ou s'il dépasse maxSteps tuiles.
        private static List<(int x, int y)>? FindRailTilePath((int x, int y) from, (int x, int y) target, int maxSteps)
        {
            var previous = new Dictionary<(int x, int y), (int x, int y)>();
            var depth = new Dictionary<(int x, int y), int> { [from] = 0 };
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue(from);
            int[,] offsets = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == target)
                {
                    var result = new List<(int x, int y)> { current };
                    while (current != from) { current = previous[current]; result.Add(current); }
                    result.Reverse();
                    return result;
                }
                if (depth[current] >= maxSteps) continue;
                for (int i = 0; i < 4; i++)
                {
                    var next = (x: current.x + offsets[i, 0], y: current.y + offsets[i, 1]);
                    if (depth.ContainsKey(next) || !World.IsRailwayAt(next.x, next.y)) continue;
                    if (next != target && World.GetObjectIdAt(next.x, next.y) == 124) continue; // wagon sur le chemin
                    depth[next] = depth[current] + 1;
                    previous[next] = current;
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        // Vrai si les deux wagons appartiennent déjà à la même rame (évite doublons et boucles).
        private static bool AreWagonsConnected((int x, int y) from, (int x, int y) target)
        {
            var visited = new HashSet<(int x, int y)> { from };
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == target) return true;
                foreach (var link in _wagonLinks)
                {
                    if (link.A == current && visited.Add(link.B)) queue.Enqueue(link.B);
                    else if (link.B == current && visited.Add(link.A)) queue.Enqueue(link.A);
                }
            }
            return false;
        }

        // Un wagon lié change de tuile : on met à jour ses liens (les wagons n'ont pas d'identifiant propre).
        private static void RelocateWagonLinkTile((int x, int y) previous, (int x, int y) next)
        {
            foreach (var link in _wagonLinks)
            {
                if (link.A == previous) link.A = next;
                if (link.B == previous) link.B = next;
            }
            if (_wagonLinkSource is { } source && source == previous)
                _wagonLinkSource = next;
        }

        private static bool TryGetLinkedWagonPosition((int x, int y) tile, out Vector2 position)
        {
            if (PlayerCurrentWagonTile is { } mounted && mounted == tile)
            {
                position = _wagonPos;
                return true;
            }
            if (_looseWagons.TryGetValue(tile, out var loose))
            {
                position = loose.Position;
                return true;
            }
            if (World.GetObjectIdAt(tile.x, tile.y) == 124)
            {
                position = GetWagonWorldPosition(tile);
                return true;
            }
            position = default;
            return false;
        }

        private static float GetLinkedWagonSpeed((int x, int y) tile)
        {
            if (PlayerCurrentWagonTile is { } mounted && mounted == tile) return _wagonSpeed;
            return _looseWagons.TryGetValue(tile, out var loose) ? loose.Speed : 0f;
        }

        /// <summary>À appeler chaque frame, une fois la physique des wagons (posés et monté) mise à jour.</summary>
        private static void UpdateWagonLinks()
        {
            if (_wagonLinkSource is { } source && World.GetObjectIdAt(source.x, source.y) != 124)
                _wagonLinkSource = null;
            if (_wagonLinks.Count == 0) return;

            // Un wagon détruit ou ramassé casse son lien.
            _wagonLinks.RemoveAll(l => (IsWagonTileChunkLoaded(l.A) && World.GetObjectIdAt(l.A.x, l.A.y) != 124)
                                    || (IsWagonTileChunkLoaded(l.B) && World.GetObjectIdAt(l.B.x, l.B.y) != 124));

            // Plusieurs passes : un wagon tiré par un lien peut à son tour tirer le suivant de la rame.
            for (int pass = 0; pass <= _wagonLinks.Count; pass++)
            {
                bool dragged = false;
                foreach (var link in _wagonLinks.ToArray())
                    dragged |= ResolveWagonLink(link);
                if (!dragged) break;
            }
        }

        // Renvoie vrai si un wagon a été tiré (ou écarté) par le lien.
        private static bool ResolveWagonLink(WagonLink link)
        {
            const float eps = 0.05f;
            if (!TryGetLinkedWagonPosition(link.A, out Vector2 posA)
                || !TryGetLinkedWagonPosition(link.B, out Vector2 posB)
                || link.Path.Count == 0)
                return false;

            float movedA = Vector2.Distance(posA, link.Path[0]);
            float movedB = Vector2.Distance(posB, link.Path[link.Path.Count - 1]);

            // Le lien suit le déplacement réel des deux extrémités (y compris les demi-tours).
            if (movedA > eps)
            {
                link.Path.Reverse();
                ExtendWagonPath(link.Path, posA);
                link.Path.Reverse();
            }
            if (movedB > eps)
                ExtendWagonPath(link.Path, posB);

            float excess = GetPathLength(link.Path) - link.Length;
            if (excess < -eps) return SpreadLinkedWagons(link, -excess); // trop proches : on les écarte
            if (excess <= eps) return false; // lien tendu ou mou : personne n'est tiré

            // Le wagon qui a le moins bougé de lui-même est tiré vers l'autre.
            bool dragB = movedB <= movedA;
            var draggedTile = dragB ? link.B : link.A;
            var pullerTile = dragB ? link.A : link.B;

            if (dragB) link.Path.Reverse();
            (Vector2 newPosition, Vector2 direction) = TrimPathStart(link.Path, excess);
            if (dragB) link.Path.Reverse();

            DragLinkedWagon(draggedTile, newPosition, direction, GetLinkedWagonSpeed(pullerTile));
            return true;
        }

        // Deux wagons liés plus proches que la longueur du lien (juste après l'attelage, ou quand on
        // recule dans la rame) : le wagon à l'arrêt est repoussé le long des rails, en douceur, jusqu'à
        // retrouver l'écart voulu. Il ne quitte sa tuile que si la suivante est un rail libre, donc il
        // n'écrase jamais un autre wagon. Le wagon conduit n'est jamais déplacé.
        private static bool SpreadLinkedWagons(WagonLink link, float deficit)
        {
            bool bOk = !IsWagonMountedAt(link.B.x, link.B.y) && GetLinkedWagonSpeed(link.B) <= 0.01f;
            bool aOk = !IsWagonMountedAt(link.A.x, link.A.y) && GetLinkedWagonSpeed(link.A) <= 0.01f;
            if (!bOk && !aOk) return false;
            bool pushB = bOk;
            var tile = pushB ? link.B : link.A;

            // Direction "s'éloigner de l'autre wagon" : dernier segment du lien (côté B) ou premier (côté A).
            int n = link.Path.Count;
            Vector2 seg = n >= 2
                ? (pushB ? link.Path[n - 1] - link.Path[n - 2] : link.Path[0] - link.Path[1])
                : Vector2.Zero;
            if (seg.LengthSquared() < 0.0001f)
                seg = pushB ? new Vector2(link.B.x - link.A.x, link.B.y - link.A.y)
                            : new Vector2(link.A.x - link.B.x, link.A.y - link.B.y);
            if (seg.LengthSquared() < 0.0001f) return false;
            (int x, int y) dir = MathF.Abs(seg.X) >= MathF.Abs(seg.Y) ? (Math.Sign(seg.X), 0) : (0, Math.Sign(seg.Y));

            if (!TryGetLinkedWagonPosition(tile, out Vector2 pos)) return false;
            float ts = TileSize;
            float dt = Math.Clamp(Raylib.GetFrameTime(), 0f, 0.05f);
            float step = Math.Min(deficit, ts * WagonLinkSpreadSpeedTiles * dt);
            if (step <= 0.001f) return false;

            Vector2 target = pos + new Vector2(dir.x, dir.y) * step;
            var targetTile = WorldToTile(target);
            if (targetTile != tile)
            {
                // On ne change de tuile que si c'est un rail libre (pas de wagon dessus).
                if (!CanWagonEnter(targetTile.x, targetTile.y)) target = ClampToTile(target, tile);
            }
            else if (!World.IsRailwayAt(tile.x + dir.x, tile.y + dir.y))
            {
                // Pas de rail au-delà : on ne décale pas le wagon trop loin du centre de sa tuile.
                Vector2 center = GetWagonWorldPosition(tile);
                float along = Vector2.Dot(target - center, new Vector2(dir.x, dir.y));
                float maxAlong = 0.3f * ts;
                if (along > maxAlong) target -= new Vector2(dir.x, dir.y) * (along - maxAlong);
            }

            if (Vector2.Distance(target, pos) < 0.01f) return false;
            // (le wagon poussé n'est jamais le wagon monté : c'est toujours un wagon posé)
            LooseWagonState wagon = GetOrCreateLooseWagon(tile);
            wagon.Position = target;
            SyncLooseWagonTile(wagon);
            return true;
        }

        private static Vector2 ClampToTile(Vector2 p, (int x, int y) tile)
        {
            float ts = TileSize;
            return new Vector2(
                Math.Clamp(p.X, tile.x * ts, (tile.x + 1) * ts - 0.01f),
                Math.Clamp(p.Y, tile.y * ts, (tile.y + 1) * ts - 0.01f));
        }

        private static void DragLinkedWagon((int x, int y) tile, Vector2 newPosition, Vector2 direction, float pullerSpeed)
        {
            (int x, int y)? heading = null;
            if (direction.LengthSquared() > 0.0001f)
                heading = MathF.Abs(direction.X) >= MathF.Abs(direction.Y)
                    ? (Math.Sign(direction.X), 0)
                    : (0, Math.Sign(direction.Y));

            if (PlayerCurrentWagonTile is { } mounted && mounted == tile)
            {
                _wagonPos = newPosition;
                if (heading.HasValue) _wagonHeading = heading.Value;
                SyncWagonTile();
                DamageEntitiesHitByWagon(_wagonPos, _wagonHeading, pullerSpeed, _mountedWagonDamageCooldowns);
                return;
            }

            LooseWagonState wagon = GetOrCreateLooseWagon(tile);
            wagon.Position = newPosition;
            if (heading.HasValue) wagon.Heading = heading.Value;
            wagon.Speed = 0f; // le wagon tiré n'a plus d'élan propre : il suit l'autre
            SyncLooseWagonTile(wagon);
            DamageEntitiesHitByWagon(wagon.Position, wagon.Heading, pullerSpeed, wagon.DamageCooldowns);
        }

        // ---------- Géométrie du lien (polyligne le long des rails) ----------
        private static bool IsWagonAxisCentered(float value)
            => MathF.Abs(MathF.IEEERemainder(value - TileSize / 2f, TileSize)) < 0.75f;

        private static float GetPathLength(List<Vector2> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++)
                length += Vector2.Distance(path[i - 1], path[i]);
            return length;
        }

        private static bool IsPointOnSegment(Vector2 point, Vector2 a, Vector2 b, float eps)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared();
            if (lengthSquared < 0.0001f) return false;
            float t = Vector2.Dot(point - a, ab) / lengthSquared;
            if (t < 0f || t > 1f) return false;
            return Vector2.DistanceSquared(point, a + ab * t) <= eps * eps;
        }

        // Prolonge la polyligne jusqu'à la nouvelle position de son extrémité (fin de liste).
        // - retour en arrière sur le dernier segment : la polyligne se raccourcit ;
        // - même direction : le dernier segment s'allonge ;
        // - virage : un sommet est ajouté (au centre de la tuile où le wagon a tourné).
        private static void ExtendWagonPath(List<Vector2> path, Vector2 point)
        {
            const float eps = 0.05f;
            if (path.Count == 0) { path.Add(point); return; }

            int n = path.Count;
            Vector2 last = path[n - 1];
            if (Vector2.DistanceSquared(last, point) <= eps * eps) return;

            if (n >= 2)
            {
                Vector2 previous = path[n - 2];
                if (IsPointOnSegment(point, previous, last, eps))
                {
                    if (Vector2.DistanceSquared(previous, point) <= eps * eps) path.RemoveAt(n - 1);
                    else path[n - 1] = point;
                    return;
                }

                Vector2 before = last - previous;
                Vector2 after = point - last;
                if (before.LengthSquared() > 0.0001f && after.LengthSquared() > 0.0001f
                    && Vector2.Dot(Vector2.Normalize(before), Vector2.Normalize(after)) > 0.99f)
                {
                    path[n - 1] = point;
                    return;
                }
            }

            // Déplacement en diagonale sur une frame : on retrouve le coin de rail où le wagon a tourné
            // (celui dont les deux coordonnées sont des centres de tuile).
            // Le coin est traité comme un vrai point de passage : s'il est DERRIÈRE l'extrémité actuelle
            // (virage pris juste après le centre de la tuile, le wagon est recalé en arrière), la
            // polyligne se raccourcit au lieu de faire un aller-retour qui la rallongerait à tort
            // (ce qui tirait les wagons suivants en avant, jusque sur la tuile du wagon de tête).
            if (MathF.Abs(point.X - last.X) > eps && MathF.Abs(point.Y - last.Y) > eps)
            {
                Vector2? corner = null;
                if (IsWagonAxisCentered(last.X) && IsWagonAxisCentered(point.Y))
                    corner = new Vector2(last.X, point.Y);
                else if (IsWagonAxisCentered(point.X) && IsWagonAxisCentered(last.Y))
                    corner = new Vector2(point.X, last.Y);
                if (corner.HasValue)
                {
                    ExtendWagonPath(path, corner.Value);
                    ExtendWagonPath(path, point);
                    return;
                }
            }
            path.Add(point);
        }

        // Retire "distance" de longueur au début de la polyligne. Renvoie le nouveau point de départ
        // et la direction de la polyligne à cet endroit (vers l'autre extrémité).
        private static (Vector2 point, Vector2 direction) TrimPathStart(List<Vector2> path, float distance)
        {
            Vector2 direction = Vector2.Zero;
            while (path.Count >= 2)
            {
                Vector2 segment = path[1] - path[0];
                float length = segment.Length();
                if (length > 0.0001f) direction = segment / length;

                if (path.Count == 2 || distance < length - 0.0001f)
                {
                    path[0] += direction * Math.Min(distance, length);
                    return (path[0], direction);
                }
                distance -= length;
                path.RemoveAt(0);
            }
            return (path.Count > 0 ? path[0] : Vector2.Zero, direction);
        }

        // ---------- Rendu du lien ----------
        public static bool HasWagonLinkVisuals() => _wagonLinks.Count > 0 || _wagonLinkSource.HasValue;

        private static Vector2 WagonLinkVisualPoint(Vector2 worldPoint)
        {
            var tile = WorldToTile(worldPoint);
            int height = World.GetHeightAt(tile.x, tile.y);
            return new Vector2(worldPoint.X, worldPoint.Y - height * TileSize / 4f);
        }

        /// <summary>Dessine les barres d'attelage (sous les wagons) et le wagon actuellement sélectionné.</summary>
        public static void DrawWagonLinks()
        {
            float ts = TileSize;
            Vector2 hitchOffset = new Vector2(0f, ts * 0.12f);
            Color outline = new Color(45, 40, 36, 255);
            Color metal = new Color(165, 160, 150, 255);

            foreach (var link in _wagonLinks)
            {
                for (int i = 1; i < link.Path.Count; i++)
                {
                    Vector2 from = WagonLinkVisualPoint(link.Path[i - 1]) + hitchOffset;
                    Vector2 to = WagonLinkVisualPoint(link.Path[i]) + hitchOffset;
                    Raylib.DrawLineEx(from, to, 4f, outline);
                    Raylib.DrawLineEx(from, to, 2f, metal);
                }
            }

            if (_wagonLinkSource is { } source && TryGetLinkedWagonPosition(source, out Vector2 sourcePos))
            {
                float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 6f);
                Vector2 ringCenter = WagonLinkVisualPoint(sourcePos);
                Raylib.DrawCircleLines((int)ringCenter.X, (int)ringCenter.Y, ts * (0.42f + 0.06f * pulse),
                    new Color(100, 200, 255, 230));
            }
        }

        private static (int x, int y) WorldToTile(Vector2 p)
            => ((int)MathF.Floor(p.X / TileSize), (int)MathF.Floor(p.Y / TileSize));

        // Le wagon reste enregistré comme objet posé sur la tuile qui contient sa position réelle.
        private static void SyncWagonTile()
        {
            if (PlayerCurrentWagonTile is not { } cur) return;
            var t = WorldToTile(_wagonPos);
            if (t == cur) return;
            if (World.GetObjectIdAt(t.x, t.y) == 124)
            {
                // Tuile déjà occupée par un autre wagon : on ne l'écrase pas, on reste dans notre tuile.
                _wagonPos = ClampToTile(_wagonPos, cur);
                _wagonSpeed = 0f;
                return;
            }
            World.RemovePlacedObject(cur.x, cur.y);
            World.AddPlacedObject(t.x, t.y, 124);
            RelocateWagonLinkTile(cur, t);
            PlayerCurrentWagonTile = t;
        }

        private static void SyncLooseWagonTile(LooseWagonState wagon)
        {
            var tile = WorldToTile(wagon.Position);
            if (tile == wagon.Tile) return;
            if (World.GetObjectIdAt(tile.x, tile.y) == 124)
            {
                // Tuile déjà occupée par un autre wagon : on ne l'écrase pas, on reste dans notre tuile.
                wagon.Position = ClampToTile(wagon.Position, wagon.Tile);
                wagon.Speed = 0f;
                return;
            }
            var previous = wagon.Tile;
            World.RemovePlacedObject(previous.x, previous.y);
            World.AddPlacedObject(tile.x, tile.y, 124);
            _looseWagons.Remove(previous);
            wagon.Tile = tile;
            _looseWagons[tile] = wagon;
            RelocateWagonLinkTile(previous, tile);
        }

        /// <summary>Position réelle du wagon (et du joueur) pendant le trajet.</summary>
        public static Vector2 GetMountedWagonPosition() => _wagonPos;

        /// <summary>Position de dessin du wagon : comme tous les objets du monde (et le joueur),
        /// il est remonté de hauteur * TileSize / 4 selon le relief de la tuile où il se trouve.</summary>
        public static Vector2 GetMountedWagonDrawPosition()
        {
            var tile = WorldToTile(_wagonPos);
            int height = World.GetHeightAt(tile.x, tile.y);
            return new Vector2(_wagonPos.X, _wagonPos.Y - height * TileSize / 4f);
        }

        /// <summary>Décalage de rendu du wagon monté par rapport au centre de sa tuile.</summary>
        public static Vector2 GetWagonDrawOffset(int tileX, int tileY)
            => PlayerCurrentWagonTile is { } c && c.x == tileX && c.y == tileY
                ? _wagonPos - GetWagonWorldPosition(c)
                : _looseWagons.TryGetValue((tileX, tileY), out var wagon)
                    ? wagon.Position - GetWagonWorldPosition(wagon.Tile)
                : Vector2.Zero;

        /// <summary>Vrai si le wagon posé sur cette tuile est celui qu'on est en train de conduire
        /// (il est alors dessiné à part, à sa position réelle, et pas avec sa tuile).</summary>
        public static bool IsWagonMountedAt(int tileX, int tileY)
            => PlayerCurrentWagonTile is { } c && c.x == tileX && c.y == tileY;

        public static bool IsMountedWagonVertical() => _wagonHeading.y != 0;

        public static bool IsWagonDrawnVertical(int tileX, int tileY)
            => PlayerCurrentWagonTile is { } c && c.x == tileX && c.y == tileY
                ? _wagonHeading.y != 0
                : _looseWagons.TryGetValue((tileX, tileY), out var wagon) && wagon.Heading.y != 0;

        private static Vector2 GetWagonWorldPosition((int x, int y) tile)
        {
            return new Vector2(tile.x * TileSize + TileSize / 2f, tile.y * TileSize + TileSize / 2f);
        }

        public static void DrawWagon(Vector2 center, int tileSize, bool vertical = false)
        {
            EnsureWagonTexturesLoaded();

            Texture2D texture = vertical ? _wagonVerticalTexture : _wagonHorizontalTexture;
            if (texture.Id != 0)
            {
                float textureScale = tileSize / 16f;
                float drawWidth = texture.Width * textureScale;
                float drawHeight = texture.Height * textureScale;
                Raylib.DrawTexturePro(texture,
                    new Rectangle(0, 0, texture.Width, texture.Height),
                    new Rectangle(center.X - drawWidth / 2f, center.Y + tileSize / 2f - drawHeight,
                        drawWidth, drawHeight),
                    Vector2.Zero, 0f, Color.White);
                return;
            }

            float bodyWidth = vertical ? tileSize * 0.38f : tileSize * 0.58f;
            float bodyHeight = vertical ? tileSize * 0.58f : tileSize * 0.38f;
            float wheelRadius = tileSize * 0.09f;
            var body = new Rectangle(center.X - bodyWidth / 2f, center.Y - bodyHeight / 2f, bodyWidth, bodyHeight);
            var wheelColor = new Color(38, 37, 34, 255);

            Raylib.DrawCircleV(new Vector2(body.X + wheelRadius, body.Y + wheelRadius), wheelRadius, wheelColor);
            Raylib.DrawCircleV(new Vector2(body.X + body.Width - wheelRadius, body.Y + wheelRadius), wheelRadius, wheelColor);
            Raylib.DrawCircleV(new Vector2(body.X + wheelRadius, body.Y + body.Height - wheelRadius), wheelRadius, wheelColor);
            Raylib.DrawCircleV(new Vector2(body.X + body.Width - wheelRadius, body.Y + body.Height - wheelRadius), wheelRadius, wheelColor);
            Raylib.DrawRectangleRounded(body, 0.12f, 4, new Color(139, 84, 43, 255));
            Raylib.DrawRectangleRoundedLines(body, 0.12f, 4, 2f, new Color(73, 43, 26, 255));
            if (vertical)
                Raylib.DrawRectangle((int)(center.X - bodyWidth * 0.28f), (int)(center.Y - bodyHeight * 0.36f),
                    (int)(bodyWidth * 0.12f), (int)(bodyHeight * 0.72f), new Color(191, 136, 76, 255));
            else
                Raylib.DrawRectangle((int)(center.X - bodyWidth * 0.36f), (int)(center.Y - bodyHeight * 0.28f),
                    (int)(bodyWidth * 0.72f), (int)(bodyHeight * 0.12f), new Color(191, 136, 76, 255));
        }

        public static void DrawMountedWagonFront(Vector2 center, int tileSize, bool vertical)
        {
            EnsureWagonTexturesLoaded();

            Texture2D texture = vertical ? _wagonVerticalFrontTexture : _wagonHorizontalFrontTexture;
            if (texture.Id == 0) return;

            float textureScale = tileSize / 16f;
            float drawWidth = texture.Width * textureScale;
            float drawHeight = texture.Height * textureScale;
            Raylib.DrawTexturePro(texture,
                new Rectangle(0, 0, texture.Width, texture.Height),
                new Rectangle(center.X - drawWidth / 2f, center.Y + tileSize / 2f - drawHeight,
                    drawWidth, drawHeight),
                Vector2.Zero, 0f, Color.White);
        }

        private static void EnsureWagonTexturesLoaded()
        {
            if (_wagonTexturesLoaded) return;

            const string horizontalPath = "assets/extras/wagon_h.png";
            const string verticalPath = "assets/extras/wagon_v.png";
            const string horizontalFrontPath = "assets/extras/wagon_h_front.png";
            const string verticalFrontPath = "assets/extras/wagon_v_front.png";
            if (File.Exists(horizontalPath)) _wagonHorizontalTexture = Raylib.LoadTexture(horizontalPath);
            if (File.Exists(verticalPath)) _wagonVerticalTexture = Raylib.LoadTexture(verticalPath);
            if (File.Exists(horizontalFrontPath)) _wagonHorizontalFrontTexture = Raylib.LoadTexture(horizontalFrontPath);
            if (File.Exists(verticalFrontPath)) _wagonVerticalFrontTexture = Raylib.LoadTexture(verticalFrontPath);
            _wagonTexturesLoaded = true;
        }
    }
}