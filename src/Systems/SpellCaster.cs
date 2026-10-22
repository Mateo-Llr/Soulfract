// SpellCaster.cs - Version corrigée avec pluie réaliste
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class SpellCaster
    {
        public static void CastSpell(SpellData spell, Vector2 startPos, Vector2 direction, Vector2 targetPos, float chargeRatio = 1f)
        {
            chargeRatio = Math.Clamp(chargeRatio, 0f, 1f);
            int count = GetProjectileCount(spell.Power, chargeRatio);
            float speed = GetProjectileSpeed(spell.Power, chargeRatio);
            float damage = GetDamage(spell.Power, chargeRatio);
            float spread = GetSpread(spell.Power, chargeRatio);
            Color color = spell.GetElementColor();
            Color particleColor = spell.GetParticleColor();

            // Plus l'attaque est chargée, plus la déflagration est puissante (rayon + dégâts).
            float blastPowerMultiplier = 1f + 0.9f * chargeRatio;

            switch (spell.Type)
            {
                case SpellType.Projectile:
                    LaunchProjectiles(startPos, direction, 1, speed, damage, spread, color, particleColor, spell.Element, false);
                    break;

                case SpellType.Homing:
                    var homingProj = new HomingProjectile(startPos, targetPos, speed * 0.8f, damage, color, particleColor, spell.Element);
                    Program.AddSpellProjectile(homingProj);
                    break;

                case SpellType.Area:
                {
                    float chargedPower = GetChargedPower(spell.Power, chargeRatio);
                    float radius = (80f + chargedPower * 20f) * MathF.Sqrt(blastPowerMultiplier);
                    float areaDamage = damage * blastPowerMultiplier;
                    CreateExplosion(targetPos, radius, areaDamage, spell.Element);
                    Program.ApplyScreenShake(radius * 0.1f, 0.3f);
                    break;
                }

                case SpellType.Rain:
                {
                    float chargedPower = GetChargedPower(spell.Power, chargeRatio);
                    int rainCount = (int)MathF.Round(8f + chargedPower * 4f);
                    float rainRadius = 100f + chargedPower * 30f;
                    float dropHeight = 350f + chargedPower * 50f;
                    for (int i = 0; i < rainCount; i++)
                    {
                        float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                        float radiusOffset = (float)(Random.Shared.NextDouble() * rainRadius);
                        Vector2 offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radiusOffset;
                        Vector2 groundPos = targetPos + offset;
                        Vector2 dropStartPos = new Vector2(groundPos.X, groundPos.Y - dropHeight);
                        var rainProj = new RainProjectile(dropStartPos, groundPos.Y, damage * 0.5f, color, particleColor, spell.Element);
                        Program.AddSpellProjectile(rainProj);
                    }
                    break;
                }

                case SpellType.Lightning:
                    CreateLightning(startPos, targetPos, damage, spell.Element);
                    break;

                case SpellType.Shield:
                {
                    float chargedPower = GetChargedPower(spell.Power, chargeRatio);
                    int shieldCount = (int)MathF.Round(4f + chargedPower * 2f);
                    for (int i = 0; i < shieldCount; i++)
                    {
                        float angle = i * (MathF.PI * 2 / shieldCount);
                        Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        var shieldProj = new OrbitalProjectile(startPos, dir, damage * 0.3f, color, particleColor, spell.Element);
                        Program.AddSpellProjectile(shieldProj);
                    }
                    break;
                }

                case SpellType.Summon:
                    var summonProj = new SummonProjectile(targetPos, damage, color, particleColor, spell.Element);
                    Program.AddSpellProjectile(summonProj);
                    break;

                case SpellType.Burst:
                {
                    float chargedPower = GetChargedPower(spell.Power, chargeRatio);
                    int burstCount = (int)MathF.Round(8f + chargedPower * 4f);
                    float burstSpeed = speed * 1.2f;
                    for (int i = 0; i < burstCount; i++)
                    {
                        float angle = i * (MathF.PI * 2 / burstCount) + (float)Random.Shared.NextDouble() * 0.2f;
                        Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        var proj = new SpellProjectile(startPos, dir, burstSpeed, damage * 0.5f, color, particleColor, spell.Element, false);
                        Program.AddSpellProjectile(proj);
                    }
                    break;
                }

                default:
                    LaunchProjectiles(startPos, direction, count, speed, damage, spread, color, particleColor, spell.Element, false);
                    break;
            }
        }

        private static void LaunchProjectiles(Vector2 start, Vector2 dir, int count, float speed, float damage, float spread, Color color, Color particleColor, SpellElement element, bool homing)
        {
            for (int i = 0; i < count; i++)
            {
                float angleOffset = (float)((i - (count - 1) / 2f) * spread * 0.1f);
                float rad = MathF.Atan2(dir.Y, dir.X) + angleOffset;
                Vector2 d = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
                var proj = new SpellProjectile(start, d, speed, damage, color, particleColor, element, homing);
                Program.AddSpellProjectile(proj);
            }
        }

        public static void CreateExplosion(Vector2 center, float radius, float damage, SpellElement element)
        {
            SpellVfx.AreaBlast(Program.GetParticleList(), element, center, radius);
            float radiusSq = radius * radius;
            foreach (var entity in Program.GetEntities())
            {
                if (!entity.IsAlive) continue;
                if (entity.Species == "human") continue;
                if (entity.IsTamed) continue;
                if (entity.IsInvulnerable) continue;
                float distSq = Vector2.DistanceSquared(entity.WorldPos, center);
                if (distSq < radiusSq)
                {
                    float multiplier = 1f - (MathF.Sqrt(distSq) / radius);
                    int dmg = (int)(damage * multiplier);
                    entity.CurrentHP -= dmg;
                    entity.OnHit(center);
                    Program.AddFloatingDamage(World.GetEntityGroundPosition(entity.WorldPos), dmg, dmg >= 10, isEnemy: true);
                    if (!entity.IsAlive)
                    {
                        foreach (var (id, qty, _) in GameData.GetAnimalDrops(entity.Species))
                            Program.GiveItemToPlayer(id, qty, entity.WorldPos);
                    }
                }
            }
        }

        private static void CreateLightning(Vector2 start, Vector2 end, float damage, SpellElement element)
        {
            Vector2 dir = end - start;
            float length = dir.Length();
            if (length < 0.01f) return;
            dir /= length;
            Vector2 normal = new Vector2(-dir.Y, dir.X);

            List<Vector2> mainPath = BuildBoltPath(start, end, length, dir, normal, jitterScale: 1f);
            var paths = new List<Vector2[]> { mainPath.ToArray() };

            // Ramifications aléatoires qui partent du tronc principal.
            int branchCount = 2 + Random.Shared.Next(0, 3);
            for (int b = 0; b < branchCount; b++)
            {
                int branchStartIndex = Random.Shared.Next(1, Math.Max(2, mainPath.Count - 2));
                Vector2 branchOrigin = mainPath[branchStartIndex];
                float branchLength = length * (0.15f + (float)Random.Shared.NextDouble() * 0.25f);
                float branchAngle = (float)Math.Atan2(dir.Y, dir.X) + (Random.Shared.NextDouble() < 0.5 ? -1f : 1f) * (0.5f + (float)Random.Shared.NextDouble() * 0.7f);
                Vector2 branchDir = new Vector2(MathF.Cos(branchAngle), MathF.Sin(branchAngle));
                Vector2 branchEnd = branchOrigin + branchDir * branchLength;
                Vector2 branchNormal = new Vector2(-branchDir.Y, branchDir.X);
                List<Vector2> branchPath = BuildBoltPath(branchOrigin, branchEnd, branchLength, branchDir, branchNormal, jitterScale: 0.7f);

                paths.Add(branchPath.ToArray());
            }

            SpellVfx.SpawnBolt(Program.GetParticleList(), paths, SpellVfx.Style(element).Mid);
            SpellVfx.Impact(Program.GetParticleList(), element, end, 1.1f);
            Program.ApplyScreenShake(3.5f, 0.12f);

            foreach (var entity in Program.GetEntities())
            {
                if (!entity.IsAlive) continue;
                if (entity.Species == "human") continue;
                if (entity.IsTamed) continue;
                if (entity.IsInvulnerable) continue;
                Rectangle hitbox = World.GetEntityDamageHitbox(entity, Program.TileSize);
                hitbox.X -= 10f;
                hitbox.Y -= 10f;
                hitbox.Width += 20f;
                hitbox.Height += 20f;
                bool hitByLightning = false;
                foreach (Vector2[] path in paths)
                {
                    for (int i = 1; i < path.Length; i++)
                    {
                        if (SegmentIntersectsRectangle(path[i - 1], path[i], hitbox))
                        {
                            hitByLightning = true;
                            break;
                        }
                    }

                    if (hitByLightning)
                        break;
                }

                if (hitByLightning)
                {
                    entity.CurrentHP -= (int)damage;
                    entity.OnHit(start);
                    Program.AddFloatingDamage(World.GetEntityGroundPosition(entity.WorldPos), (int)damage, damage >= 10, isEnemy: true);
                    if (!entity.IsAlive)
                    {
                        foreach (var (id, qty, _) in GameData.GetAnimalDrops(entity.Species))
                            Program.GiveItemToPlayer(id, qty, entity.WorldPos);
                    }
                }
            }
        }

        // Construit un chemin en zigzag façon éclair : décalage perpendiculaire au segment,
        // plus prononcé au milieu et resserré aux extrémités pour rester ancré à ses points.
        private static List<Vector2> BuildBoltPath(Vector2 start, Vector2 end, float length, Vector2 dir, Vector2 normal, float jitterScale)
        {
            int segments = Math.Max(4, 6 + (int)(length / 22));
            float maxJitter = MathF.Min(26f, length * 0.12f) * jitterScale;
            var path = new List<Vector2>(segments + 1) { start };
            for (int i = 1; i < segments; i++)
            {
                float t = i / (float)segments;
                float taper = MathF.Sin(t * MathF.PI); // 0 aux extrémités, 1 au centre
                float jitter = ((float)Random.Shared.NextDouble() * 2f - 1f) * maxJitter * taper;
                Vector2 point = start + dir * (t * length) + normal * jitter;
                path.Add(point);
            }
            path.Add(end);
            return path;
        }

        private static bool SegmentIntersectsRectangle(Vector2 start, Vector2 end, Rectangle rectangle)
        {
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            float tMin = 0f;
            float tMax = 1f;

            return Clip(-dx, start.X - rectangle.X, ref tMin, ref tMax)
                && Clip(dx, rectangle.X + rectangle.Width - start.X, ref tMin, ref tMax)
                && Clip(-dy, start.Y - rectangle.Y, ref tMin, ref tMax)
                && Clip(dy, rectangle.Y + rectangle.Height - start.Y, ref tMin, ref tMax);
        }

        private static bool Clip(float p, float q, ref float tMin, ref float tMax)
        {
            if (MathF.Abs(p) < float.Epsilon)
                return q >= 0f;

            float t = q / p;
            if (p < 0f)
            {
                if (t > tMax) return false;
                if (t > tMin) tMin = t;
            }
            else
            {
                if (t < tMin) return false;
                if (t < tMax) tMax = t;
            }

            return true;
        }

        private static PowerLevel GetNextPower(PowerLevel power) => power switch
        {
            PowerLevel.Medium => PowerLevel.Strong,
            PowerLevel.Strong => PowerLevel.Massive,
            _ => power
        };

        private static float GetChargedPower(PowerLevel power, float chargeRatio)
        {
            if (power == PowerLevel.Weak || power == PowerLevel.Massive)
                return (int)power;

            return (int)power + Math.Clamp(chargeRatio, 0f, 1f);
        }

        private static float InterpolatePower(PowerLevel power, float chargeRatio, Func<PowerLevel, float> value)
        {
            float progress = power == PowerLevel.Weak || power == PowerLevel.Massive
                ? 0f
                : Math.Clamp(chargeRatio, 0f, 1f);
            return value(power) + (value(GetNextPower(power)) - value(power)) * progress;
        }

        private static int GetProjectileCount(PowerLevel power, float chargeRatio)
        {
            return (int)MathF.Round(InterpolatePower(power, chargeRatio, p => p switch
            {
                PowerLevel.Weak => 1,
                PowerLevel.Medium => 2,
                PowerLevel.Strong => 5,
                PowerLevel.Massive => 10,
                _ => 1
            }));
        }

        private static float GetProjectileSpeed(PowerLevel power, float chargeRatio)
        {
            return InterpolatePower(power, chargeRatio, p => p switch
            {
                PowerLevel.Weak => 450f,
                PowerLevel.Medium => 600f,
                PowerLevel.Strong => 900f,
                PowerLevel.Massive => 1200f,
                _ => 600f
            });
        }

        private static float GetDamage(PowerLevel power, float chargeRatio)
        {
            return InterpolatePower(power, chargeRatio, p => p switch
            {
                PowerLevel.Weak => 5f,
                PowerLevel.Medium => 12f,
                PowerLevel.Strong => 25f,
                PowerLevel.Massive => 40f,
                _ => 12f
            });
        }

        private static float GetSpread(PowerLevel power, float chargeRatio)
        {
            return InterpolatePower(power, chargeRatio, p => p switch
            {
                PowerLevel.Weak => 0.1f,
                PowerLevel.Medium => 0.2f,
                PowerLevel.Strong => 0.4f,
                PowerLevel.Massive => 0.6f,
                _ => 0.2f
            });
        }
    }

    // ========================================================================
    //  CLASSES DE PROJECTILES
    // ========================================================================

    public class SpellProjectile : Particle
    {
        private bool _isAlive = true;

        public new Vector2 Velocity;
        public float Damage;
        public new bool IsAlive => _isAlive && Lifetime > 0f;
        public float Lifetime = 3f;
        public SpellElement Element;
        public float VisualSize => Size;
        public float Phase { get; } = (float)Random.Shared.NextDouble() * MathF.PI * 2f;
        public Raylib_cs.Texture2D VisualTexture = new Raylib_cs.Texture2D();
        public float VisualRotation = 0f;
        public bool IsHoming;
        public float HomingStrength = 0.5f;
        public Color ParticleColor;

        // Traînée : distances accumulées depuis le dernier spawn, pour un espacement
        // constant des particules quelle que soit la vitesse du projectile / le framerate.
        private float _trailDistanceAccum = 0f;
        public SpellProjectile(Vector2 pos, Vector2 dir, float speed, float damage, Color color, Color particleColor, SpellElement element, bool homing)
            : base(pos, Vector2.Zero, color, 6f, 3f)
        {
            Velocity = dir * speed;
            Damage = damage;
            Element = element;
            IsHoming = homing;
            Color = color;
            ParticleColor = particleColor;
            Size = 11f + (int)element * 0.8f;
        }

        public new void Kill()
        {
            _isAlive = false;
            Lifetime = 0f;
        }

        // Une seule explosion visuelle par projectile, quel que soit ce qu'il touche.
        // Le scale de base est plus élevé qu'avant pour un impact plus visible.
        protected void ExplodeAt(Vector2 pos)
        {
            SpellVfx.Impact(Program.GetParticleList(), Element, pos, Math.Max(1.4f, Size / 6f));
        }

        protected void SpawnTrail(Vector2 fromPosition, Vector2 toPosition)
        {
            float segmentLength = Vector2.Distance(fromPosition, toPosition);
            if (segmentLength < 0.001f) return;
            Vector2 dir = (toPosition - fromPosition) / segmentLength;
            float spacing = SpellVfx.TrailSpacing(Element);
            float traveled = 0f;
            float distanceToNext = spacing - _trailDistanceAccum;
            while (distanceToNext <= segmentLength - traveled)
            {
                traveled += distanceToNext;
                Vector2 point = fromPosition + dir * traveled;
                SpellVfx.EmitTrail(Program.GetParticleList(), Element, point, -dir, Size);
                distanceToNext = spacing;
            }
            _trailDistanceAccum = (_trailDistanceAccum + segmentLength) % spacing;
        }

        protected bool TryExplodeOnTileHit(Vector2 fromPosition, Vector2 toPosition)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(fromPosition, toPosition) / (Program.TileSize * 0.25f)));
            for (int step = 0; step <= steps; step++)
            {
                float t = steps == 0 ? 1f : step / (float)steps;
                Vector2 samplePos = Vector2.Lerp(fromPosition, toPosition, t);
                if (CheckTileHitAt(samplePos))
                {
                    ExplodeAt(samplePos);
                    Kill();
                    return true;
                }
            }
            return false;
        }

        private bool CheckTileHitAt(Vector2 worldPosition)
        {
            int tileX = (int)Math.Floor(worldPosition.X / Program.TileSize);
            int tileY = (int)Math.Floor(worldPosition.Y / Program.TileSize);
            float radius = Math.Max(4f, Size * 0.6f);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int checkX = tileX + dx;
                int checkY = tileY + dy;
                int tileId = World.GetTileIdAt(checkX, checkY);
                var tileData = WorldTileRegistry.GetTile(tileId);
                if (tileData == null || tileData.Walkable) continue;
                var (x, y, w, h) = World.GetCollisionRect(checkX, checkY, tileData, Program.TileSize);
                Rectangle rect = new Rectangle(x, y, w, h);
                if (Raylib.CheckCollisionCircleRec(worldPosition, radius, rect))
                    return true;
            }
            return false;
        }

        public virtual void Update(float dt, List<Entity> entities)
        {
            if (!IsAlive) return;

            Vector2 previousPosition = Position;

            if (IsHoming)
            {
                Entity? target = null;
                float bestDist = 200f;
                foreach (var e in entities)
                {
                    if (!e.IsAlive || e.Species == "human" || e.IsTamed || e.IsInvulnerable) continue;
                    float d = Vector2.Distance(Position, e.WorldPos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        target = e;
                    }
                }
                if (target != null)
                {
                    Vector2 dirToTarget = target.WorldPos - Position;
                    if (dirToTarget.Length() > 0.01f)
                        dirToTarget = Vector2.Normalize(dirToTarget);
                    Velocity = Vector2.Lerp(Velocity, dirToTarget * Velocity.Length(), HomingStrength * dt);
                }
            }

            Position += Velocity * dt;
            Lifetime -= dt;
            if (Lifetime <= 0f)
            {
                _isAlive = false;
                return;
            }

            SpawnTrail(previousPosition, Position);

            if (TryExplodeOnTileHit(previousPosition, Position))
                return;

            foreach (var e in entities)
            {
                if (!e.IsAlive || e.Species == "human" || e.IsTamed || e.IsInvulnerable) continue;
                Rectangle damageHitbox = World.GetEntityDamageHitbox(e, Program.TileSize);
                if (Raylib.CheckCollisionCircleRec(Position, Math.Max(4f, Size * 0.6f), damageHitbox))
                {
                    e.CurrentHP -= (int)Damage;
                    e.OnHit(Position);
                    Program.AddFloatingDamage(World.GetEntityGroundPosition(e.WorldPos), (int)Damage, Damage >= 10, isEnemy: true);
                    if (!e.IsAlive)
                    {
                        foreach (var (id, qty, _) in GameData.GetAnimalDrops(e.Species))
                            Program.GiveItemToPlayer(id, qty, e.WorldPos);
                    }
                    ExplodeAt(Position);
                    Kill();
                    break;
                }
            }

        }
    }

    public class KhamsinProjectile : SpellProjectile
    {
        private float _distanceTraveled;
        private readonly float _maxDistance;

        public KhamsinProjectile(Vector2 pos, Vector2 dir, float speed, float damage, Color color, Color particleColor)
            : base(pos, dir, speed, damage, color, particleColor, SpellElement.Fire, false)
        {
            _maxDistance = 720f;
            Size = 12f;
            Lifetime = 3.2f;
            VisualTexture = Program.KhamsinBulletTexture;
            VisualRotation = (float)Math.Atan2(dir.Y, dir.X);
        }

        public override void Update(float dt, List<Entity> entities)
        {
            if (!IsAlive) return;

            Vector2 previousPosition = Position;
            Position += Velocity * dt;
            _distanceTraveled += Velocity.Length() * dt;
            Lifetime -= dt;

            SpawnTrail(previousPosition, Position);

            if (TryExplodeOnTileHit(previousPosition, Position))
                return;

            if (Lifetime <= 0f || _distanceTraveled >= _maxDistance)
            {
                SpawnDissipationParticles();
                Kill();
                return;
            }

            Vector2 playerPos = World.GetEntityGroundPosition(Program.GetPlayerPosition());
            if (Vector2.Distance(Position, playerPos) < 16f)
            {
                Program.DamagePlayer(Math.Max(1, (int)Damage));
                Program.ApplyScreenShake(2.2f, 0.08f);
                SpawnDissipationParticles();
                Kill();
                return;
            }

        }

        private void SpawnDissipationParticles()
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2f);
                float speed = 30f + (float)(Random.Shared.NextDouble() * 70f);
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                Program.GetParticleList().Add(new Particle(Position, velocity, new Color(180, 70, 70, 220), 3f + (float)(Random.Shared.NextDouble() * 2f), 0.25f + (float)(Random.Shared.NextDouble() * 0.15f)));
            }
        }
    }

    public class HomingProjectile : SpellProjectile
    {
        public HomingProjectile(Vector2 pos, Vector2 target, float speed, float damage, Color color, Color particleColor, SpellElement element = SpellElement.Fire)
            : base(pos, target == pos ? Vector2.UnitX : Vector2.Normalize(target - pos), speed, damage, color, particleColor, element, true)
        { }
    }

    public class RainProjectile : SpellProjectile
    {
        public float GroundY;
        public float Gravity = 800f;

        public RainProjectile(Vector2 pos, float groundY, float damage, Color color, Color particleColor, SpellElement element = SpellElement.Water)
            : base(pos, new Vector2(0, 50f), 0f, damage, color, particleColor, element, false)
        {
            GroundY = groundY;
            Size = 6.5f;
            Lifetime = 3f;
            Velocity = new Vector2(0, 50f);
        }

        public override void Update(float dt, List<Entity> entities)
        {
            if (!IsAlive) return;

            // Gravité
            Vector2 previousPosition = Position;
            Velocity.Y += Gravity * dt;
            Position += Velocity * dt;
            Lifetime -= dt;
            SpawnTrail(previousPosition, Position);

            if (TryExplodeOnTileHit(previousPosition, Position))
                return;

            if (Lifetime <= 0 || Position.Y >= GroundY)
            {
                if (Position.Y >= GroundY)
                    ExplodeAt(new Vector2(Position.X, GroundY));
                Kill();
                return;
            }

            foreach (var e in entities)
            {
                if (!e.IsAlive || e.Species == "human" || e.IsTamed) continue;
                Rectangle damageHitbox = World.GetEntityDamageHitbox(e, Program.TileSize);
                if (Raylib.CheckCollisionCircleRec(Position, Math.Max(4f, Size * 0.6f), damageHitbox))
                {
                    e.CurrentHP -= (int)Damage;
                    e.OnHit(Position);
                    Program.AddFloatingDamage(World.GetEntityGroundPosition(e.WorldPos), (int)Damage, Damage >= 10, isEnemy: true);
                    if (!e.IsAlive)
                    {
                        foreach (var (id, qty, _) in GameData.GetAnimalDrops(e.Species))
                            Program.GiveItemToPlayer(id, qty, e.WorldPos);
                    }
                    ExplodeAt(Position);
                    Kill();
                    break;
                }
            }

        }
    }

    public class OrbitalProjectile : SpellProjectile
    {
        public Vector2 Center;
        public float Angle;
        public float OrbitSpeed;

        public OrbitalProjectile(Vector2 center, Vector2 dir, float damage, Color color, Color particleColor, SpellElement element)
            : base(center + dir * 40f, Vector2.Zero, 0, damage, color, particleColor, element, false)
        {
            Center = center;
            Angle = MathF.Atan2(dir.Y, dir.X);
            OrbitSpeed = 2.4f;
            Lifetime = 5f;
        }

        public override void Update(float dt, List<Entity> entities)
        {
            Angle += OrbitSpeed * dt;
            Position = Center + new Vector2(MathF.Cos(Angle), MathF.Sin(Angle)) * 40f;
            Lifetime -= dt;
            if ((float)Random.Shared.NextDouble() < dt * 20f)
                SpellVfx.EmitTrail(Program.GetParticleList(), Element, Position,
                    new Vector2(MathF.Sin(Angle), -MathF.Cos(Angle)), Size * 0.7f);
            if (Lifetime <= 0) Kill();

        }
    }

    public class SummonProjectile : SpellProjectile
    {
        private float _timer = 1.5f;
        public float Progress => Math.Clamp(1f - _timer / 1.5f, 0f, 1f);

        public SummonProjectile(Vector2 pos, float damage, Color color, Color particleColor, SpellElement element = SpellElement.Dark)
            : base(pos, Vector2.Zero, 0f, damage, color, particleColor, element, false)
        {
            Size = 20f;
            Lifetime = 1.5f;
            Velocity = Vector2.Zero;
        }

        public override void Update(float dt, List<Entity> entities)
        {
            _timer -= dt;
            if (_timer <= 0)
            {
                SpellCaster.CreateExplosion(Position, 50f, Damage, Element);
                Kill();
            }

        }
    }
}