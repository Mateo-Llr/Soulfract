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
            int count = GetProjectileCount(spell.Power);
            float speed = GetProjectileSpeed(spell.Power);
            float damage = GetDamage(spell.Power);
            float spread = GetSpread(spell.Power);
            Color color = spell.GetElementColor();
            Color particleColor = spell.GetParticleColor();

            // Plus l'attaque est chargée, plus la déflagration est puissante (rayon + dégâts).
            float blastPowerMultiplier = 0.55f + (1.9f - 0.55f) * chargeRatio;

            switch (spell.Type)
            {
                case SpellType.Projectile:
                    LaunchProjectiles(startPos, direction, 1, speed, damage, spread, color, particleColor, spell.Element, false);
                    break;

                case SpellType.Homing:
                    var homingProj = new HomingProjectile(startPos, targetPos, speed * 0.8f, damage, color, particleColor);
                    Program.AddSpellProjectile(homingProj);
                    break;

                case SpellType.Area:
                    float radius = (80f + (int)spell.Power * 20f) * MathF.Sqrt(blastPowerMultiplier);
                    float areaDamage = damage * blastPowerMultiplier;
                    CreateExplosion(targetPos, radius, areaDamage, color, particleColor);
                    Program.ApplyScreenShake(radius * 0.1f, 0.3f);
                    break;

                case SpellType.Rain:
                    int rainCount = 8 + (int)spell.Power * 4;
                    float rainRadius = 100f + (int)spell.Power * 30f;
                    float dropHeight = 350f + (int)spell.Power * 50f;
                    for (int i = 0; i < rainCount; i++)
                    {
                        float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                        float radiusOffset = (float)(Random.Shared.NextDouble() * rainRadius);
                        Vector2 offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radiusOffset;
                        Vector2 groundPos = targetPos + offset;
                        Vector2 dropStartPos = new Vector2(groundPos.X, groundPos.Y - dropHeight);
                        var rainProj = new RainProjectile(dropStartPos, groundPos.Y, damage * 0.5f, color, particleColor);
                        Program.AddSpellProjectile(rainProj);
                    }
                    break;

                case SpellType.Lightning:
                    CreateLightning(startPos, targetPos, damage, color);
                    break;

                case SpellType.Shield:
                    int shieldCount = 4 + (int)spell.Power * 2;
                    for (int i = 0; i < shieldCount; i++)
                    {
                        float angle = i * (MathF.PI * 2 / shieldCount);
                        Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        var shieldProj = new OrbitalProjectile(startPos, dir, speed * 0.3f, damage * 0.3f, color, particleColor, spell.Element);
                        Program.AddSpellProjectile(shieldProj);
                    }
                    break;

                case SpellType.Summon:
                    var summonProj = new SummonProjectile(targetPos, damage, color, particleColor);
                    Program.AddSpellProjectile(summonProj);
                    break;

                case SpellType.Burst:
                    int burstCount = 8 + (int)spell.Power * 4;
                    float burstSpeed = speed * 1.2f;
                    for (int i = 0; i < burstCount; i++)
                    {
                        float angle = i * (MathF.PI * 2 / burstCount) + (float)Random.Shared.NextDouble() * 0.2f;
                        Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        var proj = new SpellProjectile(startPos, dir, burstSpeed, damage * 0.5f, color, particleColor, spell.Element, false);
                        Program.AddSpellProjectile(proj);
                    }
                    break;

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

        public static void CreateExplosion(Vector2 center, float radius, float damage, Color color, Color particleColor)
        {
            Random rand = new Random();
            for (int i = 0; i < 30; i++)
            {
                float angle = (float)(rand.NextDouble() * Math.PI * 2);
                float speed = rand.Next(50, 200);
                Vector2 vel = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                var p = new Particle(center, vel, particleColor, rand.Next(3, 8), rand.Next(300, 800) / 1000f);
                Program.GetParticleList().Add(p);
            }
            float radiusSq = radius * radius;
            foreach (var entity in Program.GetEntities())
            {
                if (!entity.IsAlive) continue;
                if (entity.Species == "human") continue;
                if (entity.IsTamed) continue;
                float distSq = Vector2.DistanceSquared(entity.WorldPos, center);
                if (distSq < radiusSq)
                {
                    float multiplier = 1f - (MathF.Sqrt(distSq) / radius);
                    int dmg = (int)(damage * multiplier);
                    entity.CurrentHP -= dmg;
                    entity.OnHit(center);
                    Program.AddFloatingDamage(entity.WorldPos, dmg, dmg >= 10, isEnemy: true);
                    if (!entity.IsAlive)
                    {
                        foreach (var (id, qty, _) in GameData.GetAnimalDrops(entity.Species))
                            Program.GiveItemToPlayer(id, qty, entity.WorldPos);
                    }
                }
            }
        }

        private static void CreateLightning(Vector2 start, Vector2 end, float damage, Color color)
        {
            Vector2 dir = end - start;
            float length = dir.Length();
            if (length < 0.01f) return;
            dir /= length;
            Vector2 normal = new Vector2(-dir.Y, dir.X);

            Color coreColor = new Color((byte)255, (byte)255, (byte)Math.Min(255, color.B + 60), (byte)255);
            Color glowColor = new Color(color.R, color.G, color.B, (byte)90);
            Color midColor = new Color(color.R, color.G, color.B, (byte)200);

            List<Vector2> mainPath = BuildBoltPath(start, end, length, dir, normal, jitterScale: 1f);

            // Couche externe : halo large et diffus pour donner de l'épaisseur lumineuse.
            DrawBoltPolyline(mainPath, 11f, glowColor);
            // Couche intermédiaire : couleur de l'élément, bien visible.
            DrawBoltPolyline(mainPath, 5.5f, midColor);
            // Coeur : trait blanc-vif fin, façon éclair réel.
            DrawBoltPolyline(mainPath, 2f, coreColor);

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

                DrawBoltPolyline(branchPath, 5f, glowColor);
                DrawBoltPolyline(branchPath, 2.5f, midColor);
                DrawBoltPolyline(branchPath, 1f, coreColor);
            }

            // Étincelles le long du tronc principal.
            foreach (var point in mainPath)
            {
                if (Random.Shared.NextDouble() < 0.6)
                {
                    float sparkAngle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                    float sparkSpeed = 20f + (float)Random.Shared.NextDouble() * 60f;
                    Vector2 sparkVel = new Vector2(MathF.Cos(sparkAngle), MathF.Sin(sparkAngle)) * sparkSpeed;
                    Program.GetParticleList().Add(new Particle(point, sparkVel, coreColor, 2f + (float)Random.Shared.NextDouble() * 2f, 0.15f + (float)Random.Shared.NextDouble() * 0.15f));
                }
            }

            // Flash d'impact à l'arrivée.
            for (int i = 0; i < 14; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                float speed = 40f + (float)Random.Shared.NextDouble() * 140f;
                Vector2 vel = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                Program.GetParticleList().Add(new Particle(end, vel, coreColor, 3f + (float)Random.Shared.NextDouble() * 3f, 0.2f + (float)Random.Shared.NextDouble() * 0.2f));
            }
            Program.SpawnMagicExplosionEffect(end, color, 1.1f);
            Program.ApplyScreenShake(3.5f, 0.12f);

            foreach (var entity in Program.GetEntities())
            {
                if (!entity.IsAlive) continue;
                if (entity.Species == "human") continue;
                if (entity.IsTamed) continue;
                float dist = DistanceToSegment(entity.WorldPos, start, end);
                if (dist < 20f)
                {
                    entity.CurrentHP -= (int)damage;
                    entity.OnHit(start);
                    Program.AddFloatingDamage(entity.WorldPos, (int)damage, damage >= 10, isEnemy: true);
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

        private static void DrawBoltPolyline(List<Vector2> path, float thickness, Color color)
        {
            for (int i = 1; i < path.Count; i++)
                Raylib.DrawLineEx(path[i - 1], path[i], thickness, color);
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            Vector2 ap = point - a;
            float t = Math.Clamp(Vector2.Dot(ap, ab) / Vector2.Dot(ab, ab), 0f, 1f);
            Vector2 closest = a + ab * t;
            return Vector2.Distance(point, closest);
        }

        private static int GetProjectileCount(PowerLevel power)
        {
            return power switch
            {
                PowerLevel.Weak => 1,
                PowerLevel.Medium => 2,
                PowerLevel.Strong => 5,
                PowerLevel.Massive => 10,
                _ => 1
            };
        }

        private static float GetProjectileSpeed(PowerLevel power)
        {
            return power switch
            {
                PowerLevel.Weak => 450f,
                PowerLevel.Medium => 600f,
                PowerLevel.Strong => 900f,
                PowerLevel.Massive => 1200f,
                _ => 600f
            };
        }

        private static float GetDamage(PowerLevel power)
        {
            return power switch
            {
                PowerLevel.Weak => 5f,
                PowerLevel.Medium => 12f,
                PowerLevel.Strong => 25f,
                PowerLevel.Massive => 40f,
                _ => 12f
            };
        }

        private static float GetSpread(PowerLevel power)
        {
            return power switch
            {
                PowerLevel.Weak => 0.1f,
                PowerLevel.Medium => 0.2f,
                PowerLevel.Strong => 0.4f,
                PowerLevel.Massive => 0.6f,
                _ => 0.2f
            };
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
        public Raylib_cs.Texture2D VisualTexture = new Raylib_cs.Texture2D();
        public float VisualRotation = 0f;
        public bool IsHoming;
        public float HomingStrength = 0.5f;
        public Color ParticleColor;

        // Traînée : distances accumulées depuis le dernier spawn, pour un espacement
        // constant des particules quelle que soit la vitesse du projectile / le framerate.
        private float _trailDistanceAccum = 0f;
        private float _smokeDistanceAccum = 0f;
        private const float TrailSpawnSpacing = 7f;   // px entre 2 particules de couleur
        private const float SmokeSpawnSpacing = 16f;  // px entre 2 puffs de fumée

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
            Program.SpawnMagicExplosionEffect(pos, Color, Math.Max(1.4f, Size / 6f));
        }

        // Laisse derrière le projectile de petites particules de la couleur du sort,
        // puis un peu plus loin (plus en arrière) de la fumée qui se dissipe lentement.
        protected void SpawnTrail(Vector2 fromPosition, Vector2 toPosition)
        {
            float segmentLength = Vector2.Distance(fromPosition, toPosition);
            if (segmentLength < 0.001f) return;
            Vector2 dir = (toPosition - fromPosition) / segmentLength;
            Vector2 backward = -dir;

            _trailDistanceAccum += segmentLength;
            while (_trailDistanceAccum >= TrailSpawnSpacing)
            {
                _trailDistanceAccum -= TrailSpawnSpacing;
                float t = _trailDistanceAccum / segmentLength;
                Vector2 point = Vector2.Lerp(toPosition, fromPosition, Math.Clamp(t, 0f, 1f));
                float speedFactor = (float)(Random.Shared.NextDouble() * 20f - 10f);
                Vector2 vel = backward * speedFactor + new Vector2((float)(Random.Shared.NextDouble() * 20f - 10f), (float)(Random.Shared.NextDouble() * 20f - 10f));
                float size = Math.Max(2f, Size * 0.28f) * (0.7f + (float)Random.Shared.NextDouble() * 0.5f);
                Program.GetParticleList().Add(new Particle(point, vel, ParticleColor, size, 0.25f + (float)Random.Shared.NextDouble() * 0.15f));
            }

            _smokeDistanceAccum += segmentLength;
            while (_smokeDistanceAccum >= SmokeSpawnSpacing)
            {
                _smokeDistanceAccum -= SmokeSpawnSpacing;
                float t = _smokeDistanceAccum / segmentLength;
                Vector2 point = Vector2.Lerp(toPosition, fromPosition, Math.Clamp(t, 0f, 1f));
                // La fumée apparaît un peu plus loin en arrière du projectile que les particules de couleur.
                point += backward * (Size * 1.2f + 6f);
                Vector2 smokeVel = backward * (10f + (float)Random.Shared.NextDouble() * 15f) + new Vector2(0f, -8f - (float)Random.Shared.NextDouble() * 10f);
                Color smokeBase = new Color((byte)150, (byte)150, (byte)150, (byte)120);
                float smokeSize = Math.Max(4f, Size * 0.55f) * (0.8f + (float)Random.Shared.NextDouble() * 0.6f);
                Program.GetParticleList().Add(new Particle(point, smokeVel, smokeBase, smokeSize, 0.5f + (float)Random.Shared.NextDouble() * 0.35f));
            }
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
                    if (!e.IsAlive || e.Species == "human" || e.IsTamed) continue;
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
                if (!e.IsAlive || e.Species == "human" || e.IsTamed) continue;
                Rectangle damageHitbox = World.GetEntityDamageHitbox(e, Program.TileSize);
                if (Raylib.CheckCollisionCircleRec(Position, Math.Max(4f, Size * 0.6f), damageHitbox))
                {
                    e.CurrentHP -= (int)Damage;
                    e.OnHit(Position);
                    Program.AddFloatingDamage(e.WorldPos, (int)Damage, Damage >= 10, isEnemy: true);
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

            Vector2 playerPos = Program.GetPlayerPosition();
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
        public HomingProjectile(Vector2 pos, Vector2 target, float speed, float damage, Color color, Color particleColor)
            : base(pos, Vector2.Normalize(target - pos), speed, damage, color, particleColor, SpellElement.Fire, true)
        { }
    }

    public class RainProjectile : SpellProjectile
    {
        public float GroundY;
        public float Gravity = 800f;

        public RainProjectile(Vector2 pos, float groundY, float damage, Color color, Color particleColor)
            : base(pos, new Vector2(0, 50f), 0f, damage, color, particleColor, SpellElement.Water, false)
        {
            GroundY = groundY;
            Size = 4f;
            Lifetime = 3f;
            Velocity = new Vector2(0, 50f);
        }

        public override void Update(float dt, List<Entity> entities)
        {
            // Gravité
            Vector2 previousPosition = Position;
            Velocity.Y += Gravity * dt;
            Position += Velocity * dt;
            Lifetime -= dt;

            if (TryExplodeOnTileHit(previousPosition, Position))
                return;

            if (Lifetime <= 0 || Position.Y >= GroundY)
            {
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
                    Program.AddFloatingDamage(e.WorldPos, (int)Damage, Damage >= 10, isEnemy: true);
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

        public OrbitalProjectile(Vector2 center, Vector2 dir, float speed, float damage, Color color, Color particleColor, SpellElement element)
            : base(center + dir * 30f, Vector2.Zero, 0, damage, color, particleColor, element, false)
        {
            Center = center;
            Angle = MathF.Atan2(dir.Y, dir.X);
            OrbitSpeed = speed * 0.1f;
            Lifetime = 5f;
        }

        public override void Update(float dt, List<Entity> entities)
        {
            Angle += OrbitSpeed * dt;
            Position = Center + new Vector2(MathF.Cos(Angle), MathF.Sin(Angle)) * 30f;
            Lifetime -= dt;
            if (Lifetime <= 0) Kill();

        }
    }

    public class SummonProjectile : SpellProjectile
    {
        private float _timer = 1.5f;

        public SummonProjectile(Vector2 pos, float damage, Color color, Color particleColor)
            : base(pos, Vector2.Zero, 0f, damage, color, particleColor, SpellElement.Dark, false)
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
                SpellCaster.CreateExplosion(Position, 50f, Damage, Color, Color);
                Kill();
            }

        }
    }
}