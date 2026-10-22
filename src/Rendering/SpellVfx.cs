// SpellVfx.cs - Moteur d'effets visuels des sorts (projectiles, traînées, impacts, éclairs, jauge, icônes, aperçu).
// Tout est dessiné en code ; des sprites optionnels peuvent surcharger le rendu (voir SpellVfxAssets).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Raylib_cs;

namespace Soulfract
{
    // ════════════════════════════════════════════════════════════════════════
    //  STYLE PAR ÉLÉMENT (couleurs)
    // ════════════════════════════════════════════════════════════════════════
    public sealed class ElementStyle
    {
        public Color Core, Mid, Outer, Dark;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SPRITES OPTIONNELS : assets/spells/<element>/<prefix>_<n>.png  (n = 1, 2, 3...)
    //    proj_N   : frames du projectile (dessinées orientées vers la DROITE)
    //    impact_N : frames de l'impact (animation jouée une fois)
    //    icon_N   : icône du menu (animée si plusieurs frames)
    //  Si aucun fichier n'existe, le rendu procédural est utilisé.
    // ════════════════════════════════════════════════════════════════════════
    public static class SpellVfxAssets
    {
        private static readonly Dictionary<(SpellElement, string), List<Texture2D>> _cache = new();

        public static List<Texture2D> GetFrames(SpellElement element, string prefix)
        {
            var key = (element, prefix);
            if (_cache.TryGetValue(key, out var frames)) return frames;

            frames = new List<Texture2D>();
            string dir = "assets/spells/" + element.ToString().ToLowerInvariant();
            for (int i = 1; i <= 32; i++)
            {
                string path = $"{dir}/{prefix}_{i}.png";
                if (!File.Exists(path)) break;
                Texture2D tex = Raylib.LoadTexture(path);
                if (tex.Id == 0) break;
                frames.Add(tex);
            }
            _cache[key] = frames;
            return frames;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PARTICULE D'EFFET (dessinée par lots via SpellVfx.DrawBatched)
    // ════════════════════════════════════════════════════════════════════════
    public enum FxShape { Dot, Square, Streak, Ring, Glow, Smoke, Drop, Shard, Star, Flake, Debris, Bolt, Sprite }

    public class SpellFxParticle : Particle
    {
        public FxShape Shape;
        public Color ColorEnd;
        public float SizeStart, SizeEnd;
        public bool Additive;
        public float Rotation;          // degrés
        public float Spin;              // degrés / seconde
        public float Thickness = 3f;    // Ring / Streak / Bolt
        public float Sway, SwaySpeed = 6f, Phase;
        public List<Texture2D>? Frames; // Sprite
        public List<Vector2[]>? Paths;  // Bolt (0 = tronc, suivants = branches)

        public SpellFxParticle(FxShape shape, Vector2 pos, Vector2 vel, Color c0, Color c1, float s0, float s1, float life, bool additive)
            : base(pos, vel, c0, s0, life)
        {
            Shape = shape;
            ColorEnd = c1;
            SizeStart = s0;
            SizeEnd = s1;
            Additive = additive;
        }

        public override void Update(float dt)
        {
            base.Update(dt);
            Rotation += Spin * dt;
        }

        public void Draw()
        {
            float age = MaxLifetime - Lifetime;
            float t = Math.Clamp(age / MathF.Max(0.0001f, MaxLifetime), 0f, 1f);
            float fade = Shape == FxShape.Smoke ? MathF.Min(t * 8f, 1f) * (1f - t) : 1f - t * t;

            if (Shape == FxShape.Bolt) { DrawBolt(age, fade); return; }

            Color c = SpellVfx.LerpColor(Color, ColorEnd, t);
            c = new Color(c.R, c.G, c.B, (byte)Math.Clamp((int)(c.A * fade), 0, 255));
            if (c.A == 0) return;

            float size = SpellVfx.Lerpf(SizeStart, SizeEnd, t);
            Vector2 pos = Position;
            if (Sway != 0f) pos.X += MathF.Sin(age * SwaySpeed + Phase) * Sway;
            float r = size * 0.5f;

            switch (Shape)
            {
                case FxShape.Dot:
                case FxShape.Smoke:
                    Raylib.DrawCircleV(pos, r, c);
                    break;

                case FxShape.Square:
                    Raylib.DrawRectanglePro(new Rectangle(MathF.Round(pos.X), MathF.Round(pos.Y), size, size), new Vector2(r, r), Rotation, c);
                    break;

                case FxShape.Debris:
                {
                    Raylib.DrawRectanglePro(new Rectangle(pos.X, pos.Y, size, size * 0.8f), new Vector2(r, size * 0.4f), Rotation, c);
                    Color hi = new Color((byte)Math.Min(255, c.R + 45), (byte)Math.Min(255, c.G + 45), (byte)Math.Min(255, c.B + 45), c.A);
                    Raylib.DrawRectanglePro(new Rectangle(pos.X, pos.Y, size * 0.5f, size * 0.35f), new Vector2(size * 0.25f, size * 0.1f), Rotation, hi);
                    break;
                }

                case FxShape.Streak:
                {
                    Vector2 v = Velocity;
                    Vector2 dir = v.LengthSquared() > 1f ? Vector2.Normalize(v) : new Vector2(MathF.Cos(Rotation * MathF.PI / 180f), MathF.Sin(Rotation * MathF.PI / 180f));
                    Raylib.DrawLineEx(pos - dir * size, pos, MathF.Max(1f, Thickness * (1f - t * 0.5f)), c);
                    break;
                }

                case FxShape.Ring:
                {
                    float th = MathF.Max(1f, Thickness * (1f - t * 0.6f));
                    Raylib.DrawRing(pos, MathF.Max(0f, r - th * 0.5f), r + th * 0.5f, 0f, 360f, 36, c);
                    break;
                }

                case FxShape.Glow:
                    SpellVfx.Glow(pos, r, c);
                    break;

                case FxShape.Drop:
                {
                    Vector2 v = Velocity;
                    Vector2 dir = v.LengthSquared() > 1f ? Vector2.Normalize(v) : new Vector2(0f, 1f);
                    Vector2 tail = pos - dir * r * 0.9f;
                    float rotDeg = MathF.Atan2(-dir.Y, -dir.X) * 180f / MathF.PI;
                    Raylib.DrawPoly(tail, 3, r * 1.1f, rotDeg, c);
                    Raylib.DrawCircleV(pos, r, c);
                    Raylib.DrawCircleV(pos + new Vector2(-r * 0.3f, -r * 0.3f), r * 0.28f, new Color((byte)255, (byte)255, (byte)255, (byte)(c.A * 0.7f)));
                    break;
                }

                case FxShape.Shard:
                {
                    Raylib.DrawPoly(pos, 4, r, Rotation, c);
                    Raylib.DrawPoly(pos, 4, r * 0.5f, Rotation, new Color((byte)255, (byte)255, (byte)255, (byte)(c.A * 0.8f)));
                    break;
                }

                case FxShape.Star:
                {
                    float rad = Rotation * MathF.PI / 180f;
                    Vector2 a = new Vector2(MathF.Cos(rad), MathF.Sin(rad)) * r;
                    Vector2 b = new Vector2(-a.Y, a.X) * 0.8f;
                    Raylib.DrawLineEx(pos - a, pos + a, 1.6f, c);
                    Raylib.DrawLineEx(pos - b, pos + b, 1.6f, c);
                    Raylib.DrawCircleV(pos, MathF.Max(1f, r * 0.28f), new Color((byte)255, (byte)255, (byte)255, c.A));
                    break;
                }

                case FxShape.Flake:
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float rad = (Rotation + i * 60f) * MathF.PI / 180f;
                        Vector2 a = new Vector2(MathF.Cos(rad), MathF.Sin(rad)) * r;
                        Raylib.DrawLineEx(pos - a, pos + a, 1.3f, c);
                    }
                    break;
                }

                case FxShape.Sprite:
                {
                    if (Frames == null || Frames.Count == 0) break;
                    int idx = Math.Clamp((int)(t * Frames.Count), 0, Frames.Count - 1);
                    Texture2D tex = Frames[idx];
                    float scale = size / MathF.Max(1f, tex.Width);
                    Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
                        new Rectangle(pos.X, pos.Y, tex.Width * scale, tex.Height * scale),
                        new Vector2(tex.Width * scale * 0.5f, tex.Height * scale * 0.5f), Rotation, new Color((byte)255, (byte)255, (byte)255, c.A));
                    break;
                }
            }
        }

        private void DrawBolt(float age, float fade)
        {
            if (Paths == null || Paths.Count == 0) return;
            float flicker = 0.65f + 0.35f * MathF.Sin(age * 90f);
            float k = fade * flicker;
            Color mid = Color;
            Color core = ColorEnd;
            for (int i = 0; i < Paths.Count; i++)
            {
                var path = Paths[i];
                float w = i == 0 ? 1f : 0.5f;
                float shrink = 1f - (age / MathF.Max(0.0001f, MaxLifetime)) * 0.5f;
                SpellVfx.Polyline(path, 13f * w * shrink, new Color(mid.R, mid.G, mid.B, (byte)(70 * k)));
                SpellVfx.Polyline(path, 6f * w * shrink, new Color(mid.R, mid.G, mid.B, (byte)(190 * k)));
                SpellVfx.Polyline(path, MathF.Max(1f, 2.2f * w * shrink), new Color(core.R, core.G, core.B, (byte)(255 * k)));
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MOTEUR PRINCIPAL
    // ════════════════════════════════════════════════════════════════════════
    public static class SpellVfx
    {
        private const int MaxParticles = 1800;
        private const float TAU = MathF.PI * 2f;

        // ───────────────────────── utilitaires ─────────────────────────
        public static float Lerpf(float a, float b, float t) => a + (b - a) * t;
        private static float Rand() => (float)Random.Shared.NextDouble();
        private static float Rr(float a, float b) => a + (b - a) * Rand();
        private static Vector2 Dir(float ang) => new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        private static Vector2 RandDir() => Dir(Rand() * TAU);
        private static Color C(int r, int g, int b, int a = 255) => new Color((byte)r, (byte)g, (byte)b, (byte)Math.Clamp(a, 0, 255));
        private static Color Ca(int r, int g, int b, int a, float k) => C(r, g, b, (int)(a * k));
        private static Color WithA(Color c, int a) => new Color(c.R, c.G, c.B, (byte)Math.Clamp(a, 0, 255));
        private static int Cnt(int baseCount, float k) => Math.Max(2, (int)MathF.Round(baseCount * MathF.Min(k, 2f)));

        public static Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Color(
                (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
        }

        private static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return 1f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824f;
        }

        public static void Polyline(IReadOnlyList<Vector2> path, float thickness, Color color)
        {
            for (int i = 1; i < path.Count; i++)
                Raylib.DrawLineEx(path[i - 1], path[i], thickness, color);
        }

        // ───────────────────────── halo lumineux ─────────────────────────
        private static Texture2D _glow;

        private static void EnsureGlow()
        {
            if (_glow.Id != 0) return;
            const int S = 96;
            Image img = Raylib.GenImageColor(S, S, Color.Blank);
            float half = S * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = MathF.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
                    float a = d >= 1f ? 0f : MathF.Pow(1f - d, 2.2f);
                    Raylib.ImageDrawPixel(ref img, x, y, new Color((byte)255, (byte)255, (byte)255, (byte)(a * 255f)));
                }
            _glow = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
        }

        public static void Glow(Vector2 p, float radius, Color c)
        {
            if (radius < 0.5f || c.A == 0) return;
            EnsureGlow();
            Raylib.DrawTexturePro(_glow, new Rectangle(0, 0, _glow.Width, _glow.Height),
                new Rectangle(p.X - radius, p.Y - radius, radius * 2f, radius * 2f), Vector2.Zero, 0f, c);
        }

        private static void BeginAdd() => Raylib.BeginBlendMode(BlendMode.Additive);
        private static void EndAdd() => Raylib.EndBlendMode();

        // ───────────────────────── styles ─────────────────────────
        private static readonly Dictionary<SpellElement, ElementStyle> _styles = BuildStyles();
        private static readonly ElementStyle _defaultStyle = new ElementStyle
        {
            Core = Color.White, Mid = new Color((byte)200, (byte)200, (byte)220, (byte)255), Outer = new Color((byte)140, (byte)140, (byte)170, (byte)255), Dark = new Color((byte)60, (byte)60, (byte)80, (byte)255)
        };

        private static ElementStyle St(Color core, Color mid, Color outer, Color dark)
            => new ElementStyle { Core = core, Mid = mid, Outer = outer, Dark = dark };

        private static Dictionary<SpellElement, ElementStyle> BuildStyles()
        {
            var d = new Dictionary<SpellElement, ElementStyle>();
            void Add(string key, ElementStyle s) { if (Enum.TryParse<SpellElement>(key, out var e)) d[e] = s; }
            Add("Fire", St(C(255, 245, 170), C(255, 150, 30), C(210, 50, 10), C(70, 20, 10)));
            Add("Water", St(C(230, 250, 255), C(70, 160, 255), C(30, 80, 200), C(15, 40, 110)));
            Add("Earth", St(C(190, 150, 100), C(140, 100, 60), C(95, 65, 40), C(55, 38, 25)));
            Add("Air", St(C(255, 255, 255), C(205, 235, 255), C(150, 200, 235), C(110, 150, 190)));
            Add("Ice", St(C(255, 255, 255), C(180, 240, 255), C(90, 190, 240), C(50, 120, 190)));
            Add("Lightning", St(C(255, 255, 255), C(255, 235, 80), C(255, 190, 30), C(130, 100, 10)));
            Add("Poison", St(C(215, 255, 150), C(110, 210, 70), C(60, 140, 50), C(30, 70, 40)));
            Add("Light", St(C(255, 255, 255), C(255, 245, 170), C(255, 215, 110), C(200, 160, 70)));
            Add("Dark", St(C(200, 150, 255), C(120, 70, 200), C(70, 35, 130), C(15, 8, 30)));
            Add("Metal", St(C(255, 255, 255), C(205, 210, 225), C(140, 145, 165), C(70, 75, 90)));
            return d;
        }

        public static ElementStyle Style(SpellElement e) => _styles.TryGetValue(e, out var s) ? s : _defaultStyle;

        public static string GetElementName(SpellElement e) => Localization.Get($"magic.element.{e.ToString().ToLowerInvariant()}.name");
        public static string GetElementGenitive(SpellElement e) => Localization.Get($"magic.element.{e.ToString().ToLowerInvariant()}.genitive");
        public static string GetElementDescription(SpellElement e) => Localization.Get($"magic.element.{e.ToString().ToLowerInvariant()}.description");

        public static string GetTypeName(SpellType t) => Localization.Get(t switch
        {
            SpellType.Projectile => "magic.type.projectile.name",
            SpellType.Homing => "magic.type.homing.name",
            SpellType.Area => "magic.type.area.name",
            SpellType.Rain => "magic.type.rain.name",
            SpellType.Lightning => "magic.type.lightning.name",
            SpellType.Shield => "magic.type.shield.name",
            SpellType.Summon => "magic.type.summon.name",
            SpellType.Burst => "magic.type.burst.name",
            _ => "magic.type.unknown.name"
        });

        public static string GetTypeDescription(SpellType t) => Localization.Get(t switch
        {
            SpellType.Projectile => "magic.type.projectile.description",
            SpellType.Homing => "magic.type.homing.description",
            SpellType.Area => "magic.type.area.description",
            SpellType.Rain => "magic.type.rain.description",
            SpellType.Lightning => "magic.type.lightning.description",
            SpellType.Shield => "magic.type.shield.description",
            SpellType.Summon => "magic.type.summon.description",
            SpellType.Burst => "magic.type.burst.description",
            _ => "magic.type.unknown.description"
        });

        public static string GetPowerName(PowerLevel p) => Localization.Get(p switch
        {
            PowerLevel.Weak => "magic.power.weak",
            PowerLevel.Medium => "magic.power.medium",
            PowerLevel.Strong => "magic.power.strong",
            PowerLevel.Massive => "magic.power.massive",
            _ => "magic.power.unknown"
        });

        public static string GetSpellName(SpellData s)
        {
            string genitive = GetElementGenitive(s.Element);
            return Localization.Get(s.Type switch
            {
                SpellType.Projectile => "magic.spell.projectile",
                SpellType.Homing => "magic.spell.homing",
                SpellType.Area => "magic.spell.area",
                SpellType.Rain => "magic.spell.rain",
                SpellType.Lightning => "magic.spell.lightning",
                SpellType.Shield => "magic.spell.shield",
                SpellType.Summon => "magic.spell.summon",
                SpellType.Burst => "magic.spell.burst",
                _ => "magic.spell.unknown"
            }, genitive);
        }

        // ───────────────────────── création de particules ─────────────────────────
        public static SpellFxParticle Spawn(List<Particle> sink, FxShape shape, Vector2 pos, Vector2 vel, Color c0, Color c1,
            float s0, float s1, float life, bool additive, float gravity = 0f, float drag = 0.97f, float spin = 0f)
        {
            var p = new SpellFxParticle(shape, pos, vel, c0, c1, s0, s1, life, additive)
            {
                GravityY = gravity,
                Drag = drag,
                Spin = spin,
                Rotation = Rand() * 360f,
                Phase = Rand() * TAU
            };
            if (sink.Count < MaxParticles) sink.Add(p);
            return p;
        }

        public static float TrailSpacing(SpellElement e) => e switch
        {
            SpellElement.Lightning => 4f,
            SpellElement.Fire => 5f,
            SpellElement.Metal => 5f,
            _ => 6.5f
        };

        // ───────────────────────── TRAÎNÉES ─────────────────────────
        /// <param name="back">vecteur unitaire pointant vers l'arrière du projectile</param>
        public static void EmitTrail(List<Particle> sink, SpellElement e, Vector2 p, Vector2 back, float size)
        {
            Vector2 up = new Vector2(0f, -1f);
            Vector2 jit() => new Vector2(Rr(-1f, 1f), Rr(-1f, 1f));
            Vector2 at() => p + jit() * size * 0.25f;

            switch (e)
            {
                case SpellElement.Fire:
                    Spawn(sink, FxShape.Dot, at(), back * Rr(5, 25) + up * Rr(10, 35), C(255, 175, 40, 230), C(180, 30, 0, 0), size * 0.8f, size * 0.1f, Rr(0.3f, 0.5f), true, -20f, 0.95f);
                    if (Rand() < 0.5f)
                        Spawn(sink, FxShape.Square, at(), back * Rr(10, 40) + up * Rr(15, 45) + jit() * 15f, C(255, 240, 150, 255), C(255, 110, 20, 0), Rr(2f, 3.5f), 1f, Rr(0.5f, 0.9f), true, -30f, 0.97f);
                    if (Rand() < 0.28f)
                        Spawn(sink, FxShape.Smoke, p + back * size * 0.8f, up * Rr(14, 30) + jit() * 6f, C(70, 60, 58, 150), C(30, 30, 30, 0), size * 0.5f, size * 1.5f, Rr(0.7f, 1.1f), false, -8f, 0.97f);
                    break;

                case SpellElement.Water:
                    Spawn(sink, FxShape.Drop, at(), back * Rr(5, 30) + new Vector2(Rr(-12, 12), Rr(-40, -8)), C(175, 225, 255, 235), C(60, 130, 230, 0), Rr(3.5f, 5.5f), 3f, Rr(0.5f, 0.75f), false, 420f, 0.985f);
                    if (Rand() < 0.3f)
                        Spawn(sink, FxShape.Ring, at(), up * Rr(8, 20), C(210, 240, 255, 210), C(150, 210, 255, 0), Rr(4f, 6f), Rr(8f, 11f), Rr(0.5f, 0.8f), false, -10f, 0.97f).Thickness = 1.5f;
                    Spawn(sink, FxShape.Glow, p + back * size * 0.4f, Vector2.Zero, C(60, 140, 255, 70), C(40, 100, 255, 0), size * 2.4f, size * 1.4f, 0.28f, true);
                    break;

                case SpellElement.Earth:
                    Spawn(sink, FxShape.Debris, at(), back * Rr(10, 40) + jit() * 25f, C(140, 105, 70, 255), C(90, 65, 40, 0), Rr(2.5f, 4.5f), 2f, Rr(0.5f, 0.8f), false, 430f, 0.98f, Rr(-360, 360));
                    if (Rand() < 0.5f)
                        Spawn(sink, FxShape.Smoke, p + back * size * 0.5f, jit() * 12f + up * 8f, C(155, 125, 90, 140), C(110, 90, 65, 0), size * 0.4f, size * 1.1f, Rr(0.5f, 0.8f), false, 0f, 0.96f);
                    break;

                case SpellElement.Air:
                {
                    Vector2 perp = new Vector2(-back.Y, back.X);
                    Spawn(sink, FxShape.Streak, at() + perp * Rr(-4, 4), back * Rr(40, 90) + perp * Rr(-35, 35), C(240, 250, 255, 190), C(180, 220, 250, 0), size * Rr(1.2f, 2.2f), 0f, Rr(0.25f, 0.4f), true, 0f, 0.95f).Thickness = Rr(1.2f, 2.4f);
                    if (Rand() < 0.35f)
                        Spawn(sink, FxShape.Ring, p, back * 10f, C(255, 255, 255, 130), C(200, 235, 255, 0), 6f, Rr(14f, 20f), 0.35f, true).Thickness = 1.2f;
                    break;
                }

                case SpellElement.Ice:
                    Spawn(sink, FxShape.Shard, at(), back * Rr(10, 45) + jit() * 20f, C(255, 255, 255, 255), C(120, 210, 250, 0), Rr(3.5f, 6f), 2f, Rr(0.5f, 0.8f), false, 130f, 0.97f, Rr(-200, 200));
                    if (Rand() < 0.4f)
                        Spawn(sink, FxShape.Star, at(), jit() * 10f, C(255, 255, 255, 255), C(180, 240, 255, 0), Rr(5f, 8f), 3f, Rr(0.3f, 0.5f), true, 0f, 0.95f, Rr(-120, 120));
                    if (Rand() < 0.3f)
                        Spawn(sink, FxShape.Smoke, p + back * size * 0.6f, jit() * 10f, C(210, 245, 255, 110), C(150, 215, 250, 0), size * 0.5f, size * 1.2f, Rr(0.5f, 0.8f), false, 5f, 0.96f);
                    break;

                case SpellElement.Lightning:
                    for (int i = 0; i < 2; i++)
                        Spawn(sink, FxShape.Streak, at(), RandDir() * Rr(60, 190), C(255, 250, 180, 255), C(255, 200, 40, 0), Rr(6f, 14f), 0f, Rr(0.12f, 0.25f), true, 0f, 0.9f).Thickness = Rr(1.2f, 2f);
                    Spawn(sink, FxShape.Glow, p, Vector2.Zero, C(255, 230, 80, 90), C(255, 200, 40, 0), size * 1.7f, size * 1.1f, 0.12f, true);
                    break;

                case SpellElement.Poison:
                    Spawn(sink, FxShape.Drop, at(), back * Rr(5, 20) + new Vector2(Rr(-10, 10), Rr(-15, 0)), C(150, 230, 90, 240), C(60, 150, 50, 0), Rr(3.5f, 5.5f), 3f, Rr(0.6f, 0.85f), false, 420f, 0.985f);
                    if (Rand() < 0.35f)
                        Spawn(sink, FxShape.Ring, at(), up * Rr(12, 28), C(195, 255, 145, 220), C(120, 210, 80, 0), Rr(4f, 7f), 8f, Rr(0.6f, 0.9f), false, -8f, 0.97f).Thickness = 1.5f;
                    if (Rand() < 0.35f)
                        Spawn(sink, FxShape.Smoke, p + back * size * 0.5f, up * Rr(10, 25) + jit() * 6f, C(95, 175, 65, 125), C(50, 100, 50, 0), size * 0.5f, size * 1.5f, Rr(0.6f, 1f), false, -6f, 0.97f);
                    if (Rand() < 0.25f)
                        Spawn(sink, FxShape.Dot, at(), jit() * 14f + up * 8f, C(175, 95, 215, 210), C(120, 50, 170, 0), 2.5f, 1f, 0.6f, true);
                    break;

                case SpellElement.Light:
                    if (Rand() < 0.6f)
                        Spawn(sink, FxShape.Star, at(), jit() * 14f, C(255, 255, 230, 255), C(255, 220, 120, 0), Rr(6f, 10f), 2f, Rr(0.35f, 0.6f), true, 0f, 0.95f, Rr(-90, 90));
                    Spawn(sink, FxShape.Dot, at(), up * Rr(20, 45) + jit() * 10f, C(255, 240, 160, 230), C(255, 200, 80, 0), Rr(2f, 3.2f), 1f, Rr(0.6f, 0.9f), true);
                    Spawn(sink, FxShape.Glow, p, Vector2.Zero, C(255, 240, 170, 70), C(255, 220, 120, 0), size * 2f, size * 1.2f, 0.25f, true);
                    break;

                case SpellElement.Dark:
                    Spawn(sink, FxShape.Smoke, p + back * size * 0.4f, back * 10f + up * Rr(4, 14) + jit() * 8f, C(28, 10, 48, 210), C(10, 0, 20, 0), size * 0.6f, size * 1.5f, Rr(0.6f, 1f), false, -4f, 0.97f);
                    Spawn(sink, FxShape.Glow, p, Vector2.Zero, C(130, 70, 230, 90), C(90, 40, 180, 0), size * 1.8f, size, 0.25f, true);
                    if (Rand() < 0.3f)
                        Spawn(sink, FxShape.Star, at(), jit() * 10f, C(190, 140, 255, 255), C(120, 70, 200, 0), 5f, 2f, 0.4f, true, 0f, 0.95f, Rr(-100, 100));
                    break;

                case SpellElement.Metal:
                    for (int i = 0; i < 2; i++)
                        Spawn(sink, FxShape.Streak, at(), back * Rr(40, 150) + jit() * 90f, C(255, 210, 100, 255), C(255, 90, 20, 0), Rr(6f, 12f), 0f, Rr(0.2f, 0.4f), true, 300f, 0.96f).Thickness = Rr(1.2f, 1.8f);
                    if (Rand() < 0.3f)
                        Spawn(sink, FxShape.Star, at(), Vector2.Zero, C(255, 255, 255, 255), C(210, 220, 240, 0), Rr(6f, 9f), 3f, Rr(0.15f, 0.25f), true, 0f, 0.95f, Rr(-90, 90));
                    if (Rand() < 0.3f)
                        Spawn(sink, FxShape.Debris, at(), back * Rr(10, 35) + jit() * 25f, C(150, 155, 175, 255), C(90, 95, 110, 0), Rr(2f, 3f), 1.5f, 0.5f, false, 320f, 0.98f, Rr(-300, 300));
                    break;

                default:
                    Spawn(sink, FxShape.Dot, at(), back * Rr(5, 25), C(220, 220, 255, 220), C(150, 150, 200, 0), size * 0.5f, 1f, 0.35f, true);
                    break;
            }
        }

        /// <summary>Petit effet de dissipation quand un projectile arrive en fin de vie sans rien toucher.</summary>
        public static void Fizzle(List<Particle> sink, SpellElement e, Vector2 pos, float size)
        {
            for (int i = 0; i < 5; i++)
                EmitTrail(sink, e, pos, RandDir(), size);
        }

        // ───────────────────────── IMPACTS ─────────────────────────
        public static void Impact(List<Particle> sink, SpellElement e, Vector2 pos, float k)
        {
            var s = Style(e);
            k = Math.Max(0.3f, k);

            // Impact en sprites si disponible
            var frames = SpellVfxAssets.GetFrames(e, "impact");
            if (frames.Count > 0)
            {
                var sp = Spawn(sink, FxShape.Sprite, pos, Vector2.Zero, C(255, 255, 255, 255), C(255, 255, 255, 255), 48f * MathF.Min(k, 2.5f), 48f * MathF.Min(k, 2.5f), frames.Count * 0.05f, true);
                sp.Frames = frames;
                sp.Rotation = 0f;
            }

            // onde de choc + flash communs
            Spawn(sink, FxShape.Ring, pos, Vector2.Zero, WithA(s.Mid, 210), WithA(s.Outer, 0), 6f * k, 46f * k, 0.3f, true).Thickness = 3f;
            Spawn(sink, FxShape.Glow, pos, Vector2.Zero, WithA(s.Core, 220), WithA(s.Mid, 0), 56f * k, 30f * k, 0.2f, true);

            switch (e)
            {
                case SpellElement.Fire:
                    for (int i = 0; i < Cnt(14, k); i++)
                        Spawn(sink, FxShape.Dot, pos, RandDir() * Rr(40, 130) + new Vector2(0, -25), C(255, 190, 50, 240), C(190, 35, 5, 0), Rr(8f, 13f) * k, 1.5f, Rr(0.35f, 0.65f), true, -45f, 0.93f);
                    for (int i = 0; i < Cnt(12, k); i++)
                        Spawn(sink, FxShape.Square, pos, RandDir() * Rr(60, 170) + new Vector2(0, -30), C(255, 245, 160, 255), C(255, 100, 20, 0), Rr(2.5f, 4f), 1f, Rr(0.5f, 1f), true, 60f, 0.95f);
                    for (int i = 0; i < Cnt(5, k); i++)
                        Spawn(sink, FxShape.Smoke, pos + RandDir() * 8f, RandDir() * Rr(10, 35) + new Vector2(0, -25), C(70, 60, 58, 170), C(30, 30, 30, 0), Rr(10f, 16f) * k, Rr(26f, 38f) * k, Rr(0.8f, 1.3f), false, -10f, 0.96f);
                    break;

                case SpellElement.Water:
                    for (int i = 0; i < Cnt(14, k); i++)
                        Spawn(sink, FxShape.Drop, pos, new Vector2(Rr(-90, 90), Rr(-170, -60)), C(185, 230, 255, 240), C(60, 130, 230, 0), Rr(4f, 7f), 3f, Rr(0.5f, 0.85f), false, 520f, 0.985f);
                    for (int i = 0; i < Cnt(6, k); i++)
                        Spawn(sink, FxShape.Ring, pos + RandDir() * 10f * k, new Vector2(0, -20), C(215, 240, 255, 210), C(150, 210, 255, 0), 4f, Rr(10f, 18f), Rr(0.4f, 0.7f), false, -5f, 0.97f).Thickness = 1.5f;
                    Spawn(sink, FxShape.Glow, pos, Vector2.Zero, C(70, 160, 255, 90), C(40, 100, 255, 0), 70f * k, 90f * k, 0.5f, true);
                    break;

                case SpellElement.Earth:
                    for (int i = 0; i < Cnt(12, k); i++)
                        Spawn(sink, FxShape.Debris, pos, new Vector2(Rr(-110, 110), Rr(-190, -40)), C(145, 108, 72, 255), C(90, 65, 40, 0), Rr(3f, 6f) * MathF.Min(k, 1.6f), 2f, Rr(0.6f, 1f), false, 560f, 0.985f, Rr(-420, 420));
                    for (int i = 0; i < Cnt(6, k); i++)
                        Spawn(sink, FxShape.Smoke, pos + RandDir() * 8f, RandDir() * Rr(15, 45), C(160, 130, 95, 170), C(110, 90, 65, 0), Rr(10f, 16f) * k, Rr(28f, 40f) * k, Rr(0.7f, 1.2f), false, -4f, 0.95f);
                    break;

                case SpellElement.Air:
                    for (int i = 0; i < 2; i++)
                        Spawn(sink, FxShape.Ring, pos, Vector2.Zero, C(255, 255, 255, 150), C(200, 235, 255, 0), 8f * k, (30f + i * 22f) * k, 0.3f + i * 0.12f, true).Thickness = 2f;
                    for (int i = 0; i < Cnt(10, k); i++)
                        Spawn(sink, FxShape.Streak, pos, RandDir() * Rr(90, 210), C(240, 250, 255, 200), C(180, 220, 250, 0), Rr(10f, 22f) * k, 0f, Rr(0.25f, 0.45f), true, 0f, 0.93f).Thickness = Rr(1.2f, 2.2f);
                    break;

                case SpellElement.Ice:
                    for (int i = 0; i < Cnt(14, k); i++)
                        Spawn(sink, FxShape.Shard, pos, RandDir() * Rr(50, 170) + new Vector2(0, -30), C(255, 255, 255, 255), C(120, 210, 250, 0), Rr(4f, 8f) * MathF.Min(k, 1.5f), 2f, Rr(0.5f, 0.9f), false, 180f, 0.96f, Rr(-260, 260));
                    for (int i = 0; i < Cnt(8, k); i++)
                        Spawn(sink, FxShape.Star, pos + RandDir() * 18f * k, RandDir() * 14f, C(255, 255, 255, 255), C(180, 240, 255, 0), Rr(6f, 11f), 3f, Rr(0.3f, 0.7f), true, 0f, 0.95f, Rr(-100, 100));
                    Spawn(sink, FxShape.Ring, pos, Vector2.Zero, C(220, 250, 255, 220), C(150, 220, 255, 0), 10f * k, 62f * k, 0.4f, true).Thickness = 2f;
                    break;

                case SpellElement.Lightning:
                    for (int i = 0; i < Cnt(10, k); i++)
                        Spawn(sink, FxShape.Streak, pos, RandDir() * Rr(120, 320), C(255, 252, 190, 255), C(255, 200, 40, 0), Rr(10f, 22f) * k, 0f, Rr(0.12f, 0.28f), true, 0f, 0.9f).Thickness = Rr(1.4f, 2.4f);
                    for (int i = 0; i < 3; i++)
                        Spawn(sink, FxShape.Star, pos, Vector2.Zero, C(255, 255, 255, 255), C(255, 230, 80, 0), Rr(18f, 30f) * k, 6f, 0.2f + i * 0.04f, true, 0f, 0.95f, Rr(-180, 180));
                    break;

                case SpellElement.Poison:
                    for (int i = 0; i < Cnt(10, k); i++)
                        Spawn(sink, FxShape.Drop, pos, new Vector2(Rr(-80, 80), Rr(-140, -40)), C(150, 230, 90, 240), C(60, 150, 50, 0), Rr(4f, 7f), 3f, Rr(0.6f, 1f), false, 460f, 0.985f);
                    for (int i = 0; i < Cnt(8, k); i++)
                        Spawn(sink, FxShape.Ring, pos + RandDir() * 12f * k, new Vector2(0, -Rr(10, 30)), C(195, 255, 145, 220), C(120, 210, 80, 0), 5f, Rr(9f, 16f), Rr(0.6f, 1f), false, -8f, 0.97f).Thickness = 1.5f;
                    for (int i = 0; i < Cnt(6, k); i++)
                        Spawn(sink, FxShape.Smoke, pos + RandDir() * 10f, RandDir() * Rr(8, 26) + new Vector2(0, -12), C(95, 175, 65, 140), C(50, 100, 50, 0), Rr(10f, 16f) * k, Rr(28f, 40f) * k, Rr(0.9f, 1.5f), false, -6f, 0.96f);
                    Spawn(sink, FxShape.Glow, pos, Vector2.Zero, C(110, 220, 80, 70), C(70, 160, 60, 0), 50f * k, 56f * k, 1.2f, true);
                    break;

                case SpellElement.Light:
                    for (int i = 0; i < Cnt(8, k); i++)
                    {
                        float a = i * TAU / Cnt(8, k);
                        Spawn(sink, FxShape.Streak, pos, Dir(a) * Rr(140, 220), C(255, 250, 200, 230), C(255, 220, 120, 0), Rr(16f, 28f) * k, 0f, 0.25f, true, 0f, 0.92f).Thickness = 2.6f;
                    }
                    for (int i = 0; i < Cnt(6, k); i++)
                        Spawn(sink, FxShape.Star, pos + RandDir() * 14f * k, RandDir() * 30f, C(255, 255, 235, 255), C(255, 220, 120, 0), Rr(8f, 14f), 3f, Rr(0.35f, 0.7f), true, 0f, 0.95f, Rr(-90, 90));
                    for (int i = 0; i < Cnt(10, k); i++)
                        Spawn(sink, FxShape.Dot, pos, RandDir() * Rr(20, 70) + new Vector2(0, -30), C(255, 240, 160, 230), C(255, 200, 80, 0), Rr(2f, 3.5f), 1f, Rr(0.6f, 1f), true);
                    break;

                case SpellElement.Dark:
                    for (int i = 0; i < Cnt(10, k); i++)
                        Spawn(sink, FxShape.Smoke, pos + RandDir() * 6f, RandDir() * Rr(20, 60), C(28, 10, 48, 220), C(10, 0, 20, 0), Rr(10f, 16f) * k, Rr(26f, 40f) * k, Rr(0.7f, 1.2f), false, -4f, 0.95f);
                    for (int i = 0; i < Cnt(8, k); i++)
                        Spawn(sink, FxShape.Star, pos + RandDir() * 20f * k, RandDir() * 20f, C(190, 140, 255, 255), C(120, 70, 200, 0), Rr(6f, 10f), 2f, Rr(0.4f, 0.8f), true, 0f, 0.95f, Rr(-100, 100));
                    Spawn(sink, FxShape.Glow, pos, Vector2.Zero, C(120, 60, 220, 110), C(80, 30, 160, 0), 60f * k, 80f * k, 0.5f, true);
                    break;

                case SpellElement.Metal:
                    for (int i = 0; i < Cnt(14, k); i++)
                        Spawn(sink, FxShape.Streak, pos, RandDir() * Rr(90, 260) + new Vector2(0, -30), C(255, 215, 110, 255), C(255, 90, 20, 0), Rr(7f, 15f), 0f, Rr(0.2f, 0.45f), true, 380f, 0.95f).Thickness = Rr(1.3f, 2f);
                    for (int i = 0; i < Cnt(6, k); i++)
                        Spawn(sink, FxShape.Debris, pos, RandDir() * Rr(50, 130) + new Vector2(0, -50), C(150, 155, 175, 255), C(90, 95, 110, 0), Rr(3f, 5f), 2f, Rr(0.5f, 0.9f), false, 520f, 0.985f, Rr(-360, 360));
                    for (int i = 0; i < 3; i++)
                        Spawn(sink, FxShape.Star, pos + RandDir() * 10f, Vector2.Zero, C(255, 255, 255, 255), C(210, 220, 240, 0), Rr(12f, 20f) * k, 4f, 0.2f, true, 0f, 0.95f, Rr(-120, 120));
                    break;
            }
        }

        /// <summary>Grosse déflagration (sorts de zone) : ondes de choc élargies + impact amplifié.</summary>
        public static void AreaBlast(List<Particle> sink, SpellElement e, Vector2 c, float radius)
        {
            var s = Style(e);
            float k = Math.Clamp(radius / 40f, 1f, 3.5f);
            Spawn(sink, FxShape.Ring, c, Vector2.Zero, WithA(s.Mid, 230), WithA(s.Outer, 0), 10f, radius * 2f, 0.42f, true).Thickness = 5f;
            Spawn(sink, FxShape.Ring, c, Vector2.Zero, WithA(s.Core, 200), WithA(s.Mid, 0), 6f, radius * 1.3f, 0.3f, true).Thickness = 3f;
            Spawn(sink, FxShape.Glow, c, Vector2.Zero, WithA(s.Core, 230), WithA(s.Mid, 0), radius * 1.5f, radius * 0.8f, 0.25f, true);
            Impact(sink, e, c, k);
        }

        // ───────────────────────── ÉCLAIR (remplace le dessin immédiat) ─────────────────────────
        public static void SpawnBolt(List<Particle> sink, List<Vector2[]> paths, Color mid)
        {
            if (paths.Count == 0 || paths[0].Length == 0) return;
            Color core = new Color((byte)255, (byte)255, (byte)Math.Min(255, mid.B + 80), (byte)255);
            var p = new SpellFxParticle(FxShape.Bolt, paths[0][0], Vector2.Zero, mid, core, 10f, 2f, 0.32f, true) { Paths = paths };
            sink.Add(p);
        }

        public static Vector2[] BuildBolt(Vector2 start, Vector2 end, float jitterScale = 1f)
        {
            Vector2 d = end - start;
            float length = d.Length();
            if (length < 0.01f) return new[] { start, end };
            d /= length;
            Vector2 normal = new Vector2(-d.Y, d.X);
            int segs = Math.Max(4, 6 + (int)(length / 22f));
            float maxJ = MathF.Min(26f, length * 0.12f) * jitterScale;
            var path = new Vector2[segs + 1];
            path[0] = start;
            for (int i = 1; i < segs; i++)
            {
                float t = i / (float)segs;
                float taper = MathF.Sin(t * MathF.PI);
                path[i] = start + d * (t * length) + normal * (Rr(-1f, 1f) * maxJ * taper);
            }
            path[segs] = end;
            return path;
        }

        // ───────────────────────── RENDU PAR LOTS ─────────────────────────
        /// <summary>À appeler une fois par frame après la boucle d'affichage des particules (mode monde).</summary>
        public static void DrawBatched(List<Particle> list)
        {
            EnsureGlow();
            for (int i = 0; i < list.Count; i++)
                if (list[i] is SpellFxParticle fx && !fx.Additive) fx.Draw();

            bool begun = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is SpellFxParticle fx && fx.Additive)
                {
                    if (!begun) { BeginAdd(); begun = true; }
                    fx.Draw();
                }
            }
            if (begun) EndAdd();
        }

        // ───────────────────────── CORPS DES PROJECTILES ─────────────────────────
        /// <summary>Dessine le projectile d'un sort. Appelé depuis World.DrawProjectileRenderTag.</summary>
        public static void DrawProjectile(SpellProjectile p, Vector2 pos)
        {
            float time = (float)Raylib.GetTime();

            if (p is SummonProjectile sm)
            {
                DrawSummonRune(sm.Element, pos, sm.Progress, time);
                return;
            }

            if (p is RainProjectile)
            {
                Vector2 velocity = p.Velocity.LengthSquared() > 1f ? p.Velocity : Vector2.UnitY;
                DrawBody(p.Element, pos, p.VisualSize, MathF.Atan2(velocity.Y, velocity.X), time, p.Phase, 1f);
                return;
            }

            float angle;
            if (p is OrbitalProjectile orb) angle = orb.Angle + MathF.PI * 0.5f;
            else
            {
                Vector2 v = p.Velocity;
                angle = v.LengthSquared() > 1f ? MathF.Atan2(v.Y, v.X) : 0f;
            }
            DrawBody(p.Element, pos, p.VisualSize, angle, time, p.Phase, 1f);
        }

        public static void DrawBody(SpellElement e, Vector2 p, float size, float angle, float time, float phase, float alpha = 1f)
        {
            EnsureGlow();
            float r = size * 0.75f;

            var frames = SpellVfxAssets.GetFrames(e, "proj");
            if (frames.Count > 0)
            {
                var st = Style(e);
                BeginAdd();
                Glow(p, r * 2.6f, Ca(st.Mid.R, st.Mid.G, st.Mid.B, 110, alpha));
                EndAdd();
                Texture2D tex = frames[((int)(time * 14f + phase * 5f) % frames.Count + frames.Count) % frames.Count];
                float w = size * 3f;
                float scale = w / MathF.Max(1f, tex.Width);
                Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
                    new Rectangle(p.X, p.Y, tex.Width * scale, tex.Height * scale),
                    new Vector2(tex.Width * scale * 0.5f, tex.Height * scale * 0.5f), angle * 180f / MathF.PI,
                    new Color((byte)255, (byte)255, (byte)255, (byte)(255 * alpha)));
                return;
            }

            switch (e)
            {
                case SpellElement.Fire: DrawFire(p, r, angle, time, phase, alpha); break;
                case SpellElement.Water: DrawWater(p, r, angle, time, phase, alpha); break;
                case SpellElement.Earth: DrawEarth(p, r, angle, time, phase, alpha); break;
                case SpellElement.Air: DrawAir(p, r, angle, time, phase, alpha); break;
                case SpellElement.Ice: DrawIce(p, r, angle, time, phase, alpha); break;
                case SpellElement.Lightning: DrawLightningBall(p, r, angle, time, phase, alpha); break;
                case SpellElement.Poison: DrawPoison(p, r, angle, time, phase, alpha); break;
                case SpellElement.Light: DrawLight(p, r, angle, time, phase, alpha); break;
                case SpellElement.Dark: DrawDark(p, r, angle, time, phase, alpha); break;
                case SpellElement.Metal: DrawMetal(p, r, angle, time, phase, alpha); break;
                default:
                    BeginAdd(); Glow(p, r * 2.4f, Ca(200, 200, 255, 120, alpha)); EndAdd();
                    Raylib.DrawCircleV(p, r, Ca(230, 230, 255, 255, alpha));
                    break;
            }
        }

        private static void DrawFire(Vector2 p, float r, float ang, float t, float ph, float k)
        {
            Vector2 dir = Dir(ang), back = -dir, perp = new Vector2(-dir.Y, dir.X);
            BeginAdd();
            Glow(p, r * 3.2f, Ca(255, 90, 15, 110, k));
            Glow(p, r * 1.9f, Ca(255, 170, 50, 150, k));
            for (int i = 1; i <= 6; i++)
            {
                float f = i / 6f;
                float wob = MathF.Sin(t * 20f + ph + i * 1.4f) * r * 0.32f * f;
                Vector2 q = p + back * (r * 0.7f * i) + perp * wob + new Vector2(0f, -r * 0.18f * i);
                Color col = LerpColor(C(255, 200, 70), C(210, 45, 10), f);
                Raylib.DrawCircleV(q, r * (1f - f * 0.78f), Ca(col.R, col.G, col.B, (int)(200 * (1f - f * 0.8f)), k));
            }
            for (int i = 0; i < 3; i++)
            {
                float a = t * 14f + ph + i * 2.1f;
                Vector2 q = p + new Vector2(MathF.Sin(a) * r * 0.5f, -r * (0.7f + 0.35f * MathF.Abs(MathF.Cos(a * 1.3f))));
                Raylib.DrawCircleV(q, r * 0.28f, Ca(255, 150, 30, 190, k));
            }
            EndAdd();
            Raylib.DrawCircleV(p, r * 0.82f, Ca(255, 140, 25, 255, k));
            Raylib.DrawCircleV(p, r * 0.58f, Ca(255, 205, 70, 255, k));
            Raylib.DrawCircleV(p, r * 0.32f, Ca(255, 250, 200, 255, k));
        }

        private static void DrawWater(Vector2 p, float r, float ang, float t, float ph, float k)
        {
            Vector2 back = -Dir(ang);
            float w = MathF.Sin(t * 13f + ph) * 0.09f;
            float rx = r * (1f + w), ry = r * (1f - w);
            BeginAdd();
            Glow(p, r * 2.6f, Ca(60, 150, 255, 100, k));
            EndAdd();
            for (int i = 1; i <= 3; i++)
                Raylib.DrawCircleV(p + back * (r * 0.9f * i) + new Vector2(0f, r * 0.1f * i), r * (0.55f - 0.12f * i), Ca(60, 140, 240, 190 - i * 40, k));
            Raylib.DrawEllipse((int)p.X, (int)p.Y, rx, ry, Ca(28, 85, 205, 255, k));
            Raylib.DrawEllipse((int)(p.X - r * 0.06f), (int)(p.Y - r * 0.08f), rx * 0.84f, ry * 0.84f, Ca(65, 155, 250, 255, k));
            Raylib.DrawEllipse((int)(p.X - r * 0.18f), (int)(p.Y - r * 0.24f), rx * 0.5f, ry * 0.45f, Ca(150, 215, 255, 255, k));
            Raylib.DrawCircleV(p + new Vector2(-r * 0.32f, -r * 0.38f), r * 0.17f, Ca(255, 255, 255, 240, k));
            Raylib.DrawCircleV(p + new Vector2(r * 0.28f, r * 0.3f), r * 0.08f, Ca(190, 235, 255, 200, k));
        }

        private static void DrawEarth(Vector2 p, float r, float ang, float t, float ph, float k)
        {
            float rot = t * 120f + ph * 57f;
            BeginAdd();
            Glow(p, r * 1.8f, Ca(160, 110, 60, 60, k));
            EndAdd();
            Raylib.DrawPoly(p, 6, r, rot, Ca(62, 42, 27, 255, k));
            Raylib.DrawPoly(p, 6, r * 0.86f, rot, Ca(125, 92, 58, 255, k));
            Raylib.DrawPoly(p + new Vector2(-r * 0.2f, -r * 0.2f), 5, r * 0.46f, rot * 1.3f, Ca(172, 132, 92, 255, k));
            Raylib.DrawPoly(p + new Vector2(r * 0.25f, r * 0.2f), 4, r * 0.22f, -rot, Ca(88, 62, 40, 255, k));
            for (int i = 0; i < 2; i++)
            {
                float a = t * 6f + ph + i * MathF.PI;
                Vector2 q = p + Dir(a) * r * 1.5f;
                Raylib.DrawPoly(q, 4, r * 0.22f, rot + i * 30f, Ca(110, 80, 50, 255, k));
            }
        }

        private static void DrawAir(Vector2 p, float r, float ang, float t, float ph, float k)
        {
            Vector2 back = -Dir(ang), perp = new Vector2(-back.Y, back.X);
            BeginAdd();
            Glow(p, r * 2.1f, Ca(220, 240, 255, 75, k));
            for (int i = 0; i < 3; i++)
            {
                float off = (i - 1) * r * 0.55f + MathF.Sin(t * 10f + ph + i) * r * 0.2f;
                Vector2 a = p + perp * off;
                Raylib.DrawLineEx(a, a + back * r * (2.0f + i * 0.4f), 1.6f, Ca(235, 248, 255, 130, k));
            }
            for (int i = 0; i < 16; i++)
            {
                float a = t * 9f + ph + i * 0.55f;
                float rad = r * (0.2f + i * 0.07f);
                Vector2 q = p + Dir(a) * rad;
                Color col = LerpColor(C(255, 255, 255), C(180, 220, 250), i / 16f);
                Raylib.DrawCircleV(q, MathF.Max(1f, r * 0.2f * (1f - i / 20f)), Ca(col.R, col.G, col.B, 255 - i * 10, k));
            }
            EndAdd();
        }

        private static void DrawIce(Vector2 p, float r, float ang, float t, float ph, float k)
        {
            float deg = ang * 180f / MathF.PI;
            BeginAdd();
            Glow(p, r * 2.5f, Ca(150, 230, 255, 105, k));
            EndAdd();
            Raylib.DrawPoly(p, 4, r * 1.2f, deg, Ca(55, 150, 220, 255, k));
            Raylib.DrawPoly(p, 4, r * 0.95f, deg, Ca(130, 215, 250, 255, k));
            Raylib.DrawPoly(p, 4, r * 0.55f, deg, Ca(235, 252, 255, 255, k));
            Raylib.DrawPoly(p, 4, r * 0.6f, deg + 45f, Ca(255, 255, 255, 150, k));
            BeginAdd();
            float pulse = 0.7f + 0.3f * MathF.Sin(t * 9f + ph);
            Vector2 g = p + new Vector2(-r * 0.25f, -r * 0.25f);
            Raylib.DrawLineEx(g - new Vector2(r * 0.5f * pulse, 0), g + new Vector2(r * 0.5f * pulse, 0), 1.6f, Ca(255, 255, 255, 230, k));
            Raylib.DrawLineEx(g - new Vector2(0, r * 0.5f * pulse), g + new Vector2(0, r * 0.5f * pulse), 1.6f, Ca(255, 255, 255, 230, k));
            EndAdd();
        }

        private static void DrawLightningBall(Vector2 p, float r, float ang, float t, float ph, float k)
        {
            int seed = (int)(t * 24f) * 31 + (int)(ph * 10f);
            BeginAdd();
            Glow(p, r * 3f, Ca(255, 220, 60, 140, k));
            Glow(p, r * 1.6f, Ca(255, 255, 200, 200, k));
            for (int i = 0; i < 6; i++)
            {
                float a = (Hash(seed + i * 7) * 0.5f + 0.5f) * TAU;
                float len = r * (1.1f + (Hash(seed + i * 13) * 0.5f + 0.5f) * 1.1f);
                Vector2 d = Dir(a), n = new Vector2(-d.Y, d.X);
                Vector2 p1 = p + d * r * 0.5f;
                Vector2 p2 = p + d * (r * 0.5f + len * 0.5f) + n * (Hash(seed + i * 3) * r * 0.35f);
                Vector2 p3 = p + d * (r * 0.5f + len);
                Raylib.DrawLineEx(p1, p2, 2.2f, Ca(255, 235, 110, 230, k));
                Raylib.DrawLineEx(p2, p3, 2.2f, Ca(255, 235, 110, 230, k));
                Raylib.DrawLineEx(p1, p2, 1f, Ca(255, 255, 255, 255, k));
                Raylib.DrawLineEx(p2, p3, 1f, Ca(255, 255, 255, 255, k));
            }
            EndAdd();
            Raylib.DrawCircleV(p, r * 0.6f, Ca(255, 240, 120, 255, k));
            Raylib.DrawCircleV(p, r * 0.38f, Ca(255, 255, 255, 255, k));
        }

        private static void DrawPoison(Vector2 p, float r, float ang, float t, float ph, float k)
        {
            Vector2 back = -Dir(ang);
            float w = MathF.Sin(t * 11f + ph) * 0.1f;
            float rx = r * (1f + w), ry = r * (1f - w);
            BeginAdd();
            Glow(p, r * 2.3f, Ca(110, 220, 80, 95, k));
            Glow(p, r * 2.9f, Ca(160, 70, 200, 55, k));
            EndAdd();
            for (int i = 1; i <= 3; i++)
                Raylib.DrawCircleV(p + back * (r * 0.8f * i) + new Vector2(0f, r * 0.22f * i), r * (0.6f - 0.14f * i), Ca(70, 150, 55, 200 - i * 45, k));
            Raylib.DrawEllipse((int)p.X, (int)p.Y, rx, ry, Ca(38, 105, 42, 255, k));
            Raylib.DrawEllipse((int)p.X, (int)p.Y, rx * 0.85f, ry * 0.85f, Ca(95, 195, 65, 255, k));
            Raylib.DrawEllipse((int)(p.X - r * 0.15f), (int)(p.Y - r * 0.2f), rx * 0.5f, ry * 0.45f, Ca(180, 245, 130, 255, k));
            for (int i = 0; i < 3; i++)
            {
                float a = t * 3.2f + ph + i * 2.1f;
                Vector2 q = p + new Vector2(MathF.Cos(a) * r * 0.5f, MathF.Sin(a * 1.3f) * r * 0.45f);
                float br = r * (0.12f + 0.05f * i);
                Raylib.DrawCircleV(q, br + 1f, Ca(30, 90, 35, 220, k));
                Raylib.DrawCircleV(q, br, Ca(205, 255, 165, 220, k));
            }
            Raylib.DrawCircleV(p + new Vector2(-r * 0.35f, r * 0.55f + 2f * MathF.Sin(t * 8f + ph)), r * 0.1f, Ca(150, 230, 90, 230, k));
        }

        private static void DrawLight(Vector2 p, float r, float ang, float t, float ph, float k)
        {
            BeginAdd();
            Glow(p, r * 3.6f, Ca(255, 240, 170, 125, k));
            Glow(p, r * 1.8f, Ca(255, 255, 220, 190, k));
            for (int i = 0; i < 8; i++)
            {
                float a = t * 2f + ph + i * MathF.PI / 4f;
                float len = r * (1.7f + 0.45f * MathF.Sin(t * 8f + i));
                Vector2 d = Dir(a);
                Raylib.DrawLineEx(p + d * r * 0.55f, p + d * len, 2.6f, Ca(255, 245, 190, 210, k));
            }
            for (int i = 0; i < 4; i++)
            {
                float a = -t * 1.3f + ph + i * MathF.PI / 2f + MathF.PI / 4f;
                Vector2 d = Dir(a);
                Raylib.DrawLineEx(p + d * r * 0.4f, p + d * r * 2.3f, 1.2f, Ca(255, 255, 230, 150, k));
            }
            Raylib.DrawCircleV(p, r * 0.9f, Ca(255, 250, 205, 255, k));
            Raylib.DrawCircleV(p, r * 0.6f, Ca(255, 255, 255, 255, k));
            EndAdd();
        }

        private static void DrawDark(Vector2 p, float r, float ang, float t, float ph, float k)
        {
            Vector2 back = -Dir(ang);
            BeginAdd();
            Glow(p, r * 2.7f, Ca(120, 60, 220, 105, k));
            EndAdd();
            for (int i = 1; i <= 4; i++)
                Raylib.DrawCircleV(p + back * (r * 0.8f * i) + new Vector2(0f, -r * 0.1f * i), r * (0.85f - i * 0.17f), Ca(25, 10, 45, 190 - i * 35, k));
            Raylib.DrawCircleV(p, r, Ca(12, 5, 25, 255, k));
            float pulse = 0.12f + 0.05f * MathF.Sin(t * 9f + ph);
            Raylib.DrawRing(p, r * (0.92f - pulse * 0.2f), r * (1.05f + pulse), 0f, 360f, 28, Ca(140, 80, 230, 235, k));
            for (int i = 0; i < 5; i++)
            {
                float a = -t * 5f + ph + i * TAU / 5f;
                float rad = r * (0.75f - 0.35f * ((t * 1.2f + i * 0.2f) % 1f));
                Raylib.DrawCircleV(p + Dir(a) * rad, MathF.Max(1f, r * 0.1f), Ca(195, 145, 255, 230, k));
            }
            BeginAdd();
            Glow(p, r * 1.1f, Ca(150, 90, 240, 70, k));
            EndAdd();
        }

        private static void DrawMetal(Vector2 p, float r, float ang, float t, float ph, float k)
        {
            float a2 = t * 14f + ph;
            BeginAdd();
            Glow(p, r * 1.7f, Ca(190, 200, 230, 65, k));
            EndAdd();
            for (int i = 0; i < 2; i++)
            {
                float deg = (a2 + i * MathF.PI * 0.5f) * 180f / MathF.PI;
                float len = r * (i == 0 ? 2.5f : 2.0f);
                float wid = r * (i == 0 ? 0.6f : 0.5f);
                Raylib.DrawRectanglePro(new Rectangle(p.X, p.Y, len, wid), new Vector2(len * 0.5f, wid * 0.5f), deg, Ca(95, 100, 120, 255, k));
                Raylib.DrawRectanglePro(new Rectangle(p.X, p.Y, len * 0.9f, wid * 0.55f), new Vector2(len * 0.45f, wid * 0.275f), deg, Ca(168, 174, 195, 255, k));
                Raylib.DrawRectanglePro(new Rectangle(p.X, p.Y, len * 0.85f, wid * 0.18f), new Vector2(len * 0.425f, wid * 0.4f), deg, Ca(235, 240, 250, 255, k));
            }
            Raylib.DrawCircleV(p, r * 0.38f, Ca(70, 75, 90, 255, k));
            Raylib.DrawCircleV(p, r * 0.26f, Ca(225, 230, 242, 255, k));
            BeginAdd();
            float pulse = 0.5f + 0.5f * MathF.Sin(t * 16f + ph);
            Vector2 tip = p + Dir(a2) * r * 1.2f;
            Raylib.DrawLineEx(tip - new Vector2(r * 0.6f * pulse, 0), tip + new Vector2(r * 0.6f * pulse, 0), 1.5f, Ca(255, 255, 255, 220, k));
            Raylib.DrawLineEx(tip - new Vector2(0, r * 0.6f * pulse), tip + new Vector2(0, r * 0.6f * pulse), 1.5f, Ca(255, 255, 255, 220, k));
            EndAdd();
        }

        // ───────────────────────── CERCLE D'INVOCATION ─────────────────────────
        private static void EllipseRing(Vector2 c, float rx, float ry, float thickness, Color col, int segs = 44)
        {
            Vector2 prev = c + new Vector2(rx, 0f);
            for (int i = 1; i <= segs; i++)
            {
                float a = i * TAU / segs;
                Vector2 q = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
                Raylib.DrawLineEx(prev, q, thickness, col);
                prev = q;
            }
        }

        public static void DrawSummonRune(SpellElement e, Vector2 pos, float progress, float time)
        {
            var s = Style(e);
            progress = Math.Clamp(progress, 0f, 1f);
            float open = MathF.Min(1f, progress * 3.5f);
            float R = (30f + 12f * progress) * open;
            const float flat = 0.55f;
            float a = open * 255f;
            BeginAdd();
            Glow(pos, R * 1.6f, WithA(s.Mid, (int)(70 * open + 70 * progress)));
            EllipseRing(pos, R, R * flat, 2.5f, WithA(s.Mid, (int)a));
            EllipseRing(pos, R * 0.78f, R * 0.78f * flat, 1.5f, WithA(s.Core, (int)(a * 0.8f)));
            // pentagramme tournant
            float rot = time * 0.9f;
            for (int i = 0; i < 5; i++)
            {
                float a1 = rot + i * TAU / 5f, a2 = rot + ((i + 2) % 5) * TAU / 5f;
                Vector2 p1 = pos + new Vector2(MathF.Cos(a1) * R * 0.74f, MathF.Sin(a1) * R * 0.74f * flat);
                Vector2 p2 = pos + new Vector2(MathF.Cos(a2) * R * 0.74f, MathF.Sin(a2) * R * 0.74f * flat);
                Raylib.DrawLineEx(p1, p2, 1.5f, WithA(s.Outer.R > 150 ? s.Outer : s.Mid, (int)(a * 0.85f)));
            }
            // glyphes sur le cercle
            for (int i = 0; i < 12; i++)
            {
                float g = -time * 0.5f + i * TAU / 12f;
                Vector2 q1 = pos + new Vector2(MathF.Cos(g) * R * 0.88f, MathF.Sin(g) * R * 0.88f * flat);
                Vector2 q2 = pos + new Vector2(MathF.Cos(g) * R * 1.0f, MathF.Sin(g) * R * 1.0f * flat);
                Raylib.DrawLineEx(q1, q2, 2f, WithA(s.Core, (int)(a * 0.9f)));
            }
            if (progress > 0.8f)
                Glow(pos, R * 1.2f, WithA(s.Core, (int)((progress - 0.8f) / 0.2f * 200f)));
            EndAdd();
        }

        public static void EmitSummonCharge(List<Particle> sink, SpellElement e, Vector2 pos, float progress, float dt)
        {
            var s = Style(e);
            float R = 30f + 12f * progress;
            if (Rand() < dt * (25f + 40f * progress))
            {
                float a = Rand() * TAU;
                Vector2 q = pos + new Vector2(MathF.Cos(a) * R, MathF.Sin(a) * R * 0.55f);
                Spawn(sink, FxShape.Dot, q, new Vector2(Rr(-6, 6), Rr(-70, -30)), WithA(s.Core, 240), WithA(s.Mid, 0), Rr(2.5f, 4f), 1f, Rr(0.5f, 0.9f), true, -20f, 0.97f);
            }
            if (progress > 0.5f && Rand() < dt * 12f)
                EmitTrail(sink, e, pos + new Vector2(Rr(-R, R) * 0.6f, Rr(-6, 6)), new Vector2(0, 1), 12f);
        }

        // ───────────────────────── JAUGE DE CHARGE (écran) ─────────────────────────
        public static void DrawChargeIndicator(SpellElement e, Vector2 center, float charge, float barOffsetY)
        {
            EnsureGlow();
            var s = Style(e);
            float time = (float)Raylib.GetTime();
            charge = Math.Clamp(charge, 0f, 1f);
            float ringR = 24f + charge * 34f;

            BeginAdd();
            Glow(center, ringR * 1.4f, WithA(s.Mid, (int)(30 + 90 * charge)));
            EndAdd();

            // arc de progression
            Raylib.DrawRing(center, ringR - 2.5f, ringR, -90f, -90f + 360f * charge, 48, WithA(s.Mid, 230));
            Raylib.DrawRing(center, ringR - 1f, ringR, 0f, 360f, 48, WithA(s.Dark, 120));

            // éclats du style de l'élément qui tournent autour
            int n = 3 + (int)(charge * 7f);
            for (int i = 0; i < n; i++)
            {
                float a = time * (2.5f + charge * 4f) + i * TAU / n;
                Vector2 q = center + Dir(a) * ringR;
                DrawBody(e, q, 3.4f + charge * 3.2f, a + MathF.PI * 0.5f, time, i * 1.7f, 0.9f);
            }

            // barre
            float bw = 120f, bh = 10f;
            Rectangle bg = new Rectangle(center.X - bw * 0.5f, center.Y - barOffsetY, bw, bh);
            Raylib.DrawRectangleRounded(new Rectangle(bg.X - 2, bg.Y - 2, bw + 4, bh + 4), 0.5f, 6, new Color((byte)10, (byte)10, (byte)16, (byte)200));
            Raylib.DrawRectangleRounded(bg, 0.5f, 6, new Color((byte)35, (byte)36, (byte)46, (byte)230));
            int fill = (int)(bw * charge);
            if (fill > 2)
                Raylib.DrawRectangleGradientH((int)bg.X, (int)bg.Y + 1, fill, (int)bh - 2, s.Outer, s.Core);
            Raylib.DrawRectangleRoundedLines(new Rectangle(bg.X - 2, bg.Y - 2, bw + 4, bh + 4), 0.5f, 6, 1, WithA(s.Mid, 200));
            if (charge >= 0.999f)
            {
                BeginAdd();
                float pulse = 0.5f + 0.5f * MathF.Sin(time * 12f);
                Glow(new Vector2(center.X, bg.Y + bh * 0.5f), bw * 0.65f, WithA(s.Mid, (int)(60 + 70 * pulse)));
                EndAdd();
            }
        }

        // ───────────────────────── ICÔNES DE MENU ─────────────────────────
        public static void DrawElementIcon(SpellElement e, Vector2 c, float size, float time)
        {
            var frames = SpellVfxAssets.GetFrames(e, "icon");
            if (frames.Count > 0)
            {
                Texture2D tex = frames[((int)(time * 8f) % frames.Count + frames.Count) % frames.Count];
                Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
                    new Rectangle(c.X - size * 0.5f, c.Y - size * 0.5f, size, size), Vector2.Zero, 0f, Color.White);
                return;
            }
            DrawBody(e, c + new Vector2(size * 0.1f, 0f), size * 0.36f, -0.5f, time, 1.3f, 1f);
        }

        public static void DrawTypeIcon(SpellType t, Vector2 c, float s, Color col)
        {
            float th = MathF.Max(1.5f, s * 0.14f);
            switch (t)
            {
                case SpellType.Projectile:
                    Raylib.DrawCircleV(c + new Vector2(s * 0.4f, 0), s * 0.38f, col);
                    for (int i = -1; i <= 1; i++)
                        Raylib.DrawLineEx(new Vector2(c.X - s * (0.15f), c.Y + i * s * 0.38f), new Vector2(c.X - s * (i == 0 ? 1f : 0.7f), c.Y + i * s * 0.38f), th, col);
                    break;
                case SpellType.Homing:
                {
                    Vector2[] path = { new(c.X - s, c.Y + s * 0.6f), new(c.X - s * 0.35f, c.Y + s * 0.55f), new(c.X + s * 0.05f, c.Y - s * 0.05f), new(c.X + s * 0.5f, c.Y - s * 0.4f) };
                    Polyline(path, th, col);
                    Raylib.DrawCircleV(path[3], s * 0.3f, col);
                    break;
                }
                case SpellType.Area:
                    Raylib.DrawRing(c, s * 0.45f, s * 0.45f + th, 0f, 360f, 24, col);
                    Raylib.DrawCircleV(c, s * 0.16f, col);
                    for (int i = 0; i < 8; i++)
                    {
                        Vector2 d = Dir(i * TAU / 8f);
                        Raylib.DrawLineEx(c + d * s * 0.72f, c + d * s * 1.0f, th, col);
                    }
                    break;
                case SpellType.Rain:
                    for (int i = 0; i < 3; i++)
                    {
                        float x = c.X + (i - 1) * s * 0.62f;
                        float y0 = c.Y - s * 0.75f + (i % 2) * s * 0.35f;
                        Raylib.DrawLineEx(new Vector2(x + s * 0.18f, y0), new Vector2(x - s * 0.1f, y0 + s * 0.85f), th, col);
                        Raylib.DrawCircleV(new Vector2(x - s * 0.1f, y0 + s * 0.85f), th * 0.8f, col);
                    }
                    break;
                case SpellType.Lightning:
                {
                    Vector2[] z = { new(c.X + s * 0.25f, c.Y - s), new(c.X - s * 0.4f, c.Y - s * 0.05f), new(c.X + s * 0.15f, c.Y + s * 0.05f), new(c.X - s * 0.3f, c.Y + s) };
                    Polyline(z, th * 1.2f, col);
                    break;
                }
                case SpellType.Shield:
                    Raylib.DrawRing(c, s * 0.62f, s * 0.62f + th * 0.7f, 0f, 360f, 28, col);
                    for (int i = 0; i < 3; i++)
                        Raylib.DrawCircleV(c + Dir(i * TAU / 3f - 1f) * s * 0.62f, s * 0.2f, col);
                    Raylib.DrawCircleV(c, s * 0.14f, col);
                    break;
                case SpellType.Summon:
                {
                    Raylib.DrawRing(c, s * 0.82f, s * 0.82f + th * 0.8f, 0f, 360f, 28, col);
                    for (int i = 0; i < 5; i++)
                    {
                        Vector2 p1 = c + Dir(i * TAU / 5f - MathF.PI / 2f) * s * 0.78f;
                        Vector2 p2 = c + Dir(((i + 2) % 5) * TAU / 5f - MathF.PI / 2f) * s * 0.78f;
                        Raylib.DrawLineEx(p1, p2, th * 0.8f, col);
                    }
                    break;
                }
                case SpellType.Burst:
                    for (int i = 0; i < 8; i++)
                    {
                        Vector2 d = Dir(i * TAU / 8f);
                        Raylib.DrawLineEx(c + d * s * 0.3f, c + d * s * 0.72f, th, col);
                        Raylib.DrawCircleV(c + d * s * 0.9f, s * 0.12f, col);
                    }
                    break;
                default:
                    Raylib.DrawCircleV(c, s * 0.4f, col);
                    break;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  APERÇU ANIMÉ DU SORT (menu de création)
    // ════════════════════════════════════════════════════════════════════════
    public sealed class SpellPreview
    {
        private struct Actor { public Vector2 Pos; public float Angle; public float Size; }

        private readonly List<Particle> _fx = new();
        private readonly List<Actor> _actors = new();
        private readonly List<(Vector2 pos, float vy)> _drops = new();
        private float _t;
        private float _trailAccum;
        private Vector2 _lastPos;
        private Vector2 _origin;
        private bool _hasOrigin;
        private SpellType _lastType;
        private SpellElement _lastElement;

        public void Reset()
        {
            _fx.Clear(); _actors.Clear(); _drops.Clear();
            _t = 0f; _trailAccum = 0f; _hasOrigin = false;
        }

        private static float Rr(float a, float b) => a + (b - a) * (float)Random.Shared.NextDouble();
        private static bool Crossed(float prev, float now, float at) => prev < at && now >= at;

        public void Update(float dt, Rectangle area, SpellElement e, SpellType type, PowerLevel power)
        {
            dt = MathF.Min(dt, 0.05f);
            Vector2 origin = new Vector2(area.X, area.Y);
            if (_hasOrigin)
            {
                Vector2 delta = origin - _origin;
                if (delta.LengthSquared() > 0.01f)
                    foreach (var p in _fx) p.Position += delta;
            }
            _origin = origin; _hasOrigin = true;

            if (type != _lastType || e != _lastElement)
            {
                _lastType = type; _lastElement = e;
                _fx.Clear(); _drops.Clear(); _t = 0f; _trailAccum = 0f;
            }

            _actors.Clear();
            Vector2 c = new Vector2(area.X + area.Width * 0.5f, area.Y + area.Height * 0.5f);
            float left = area.X + 50f, right = area.X + area.Width - 70f;
            float size = 11f + (int)e * 0.8f;
            float prev = _t;
            _t += dt;
            float cycle = 2f;

            switch (type)
            {
                case SpellType.Projectile:
                case SpellType.Homing:
                {
                    cycle = 1.9f;
                    if (_t > cycle) { _t -= cycle; prev = -1f; _trailAccum = 0f; }
                    if (_t < 1.2f)
                    {
                        float u = _t / 1.2f;
                        Vector2 pos = new Vector2(SpellVfx.Lerpf(left, right, u), c.Y + (type == SpellType.Homing ? MathF.Sin(u * MathF.PI * 2f) * 24f : 0f));
                        Vector2 mv = u > 0.001f ? pos - _lastPos : new Vector2(1, 0);
                        float ang = mv.LengthSquared() > 0.0001f ? MathF.Atan2(mv.Y, mv.X) : 0f;
                        _actors.Add(new Actor { Pos = pos, Angle = ang, Size = size });
                        if (u > 0.001f)
                        {
                            _trailAccum += mv.Length();
                            float sp = SpellVfx.TrailSpacing(e);
                            while (_trailAccum >= sp) { _trailAccum -= sp; SpellVfx.EmitTrail(_fx, e, pos, -Vector2.Normalize(mv), size); }
                        }
                        _lastPos = pos;
                    }
                    else if (Crossed(prev, _t, 1.2f))
                        SpellVfx.Impact(_fx, e, new Vector2(right, c.Y), 1.3f);
                    break;
                }

                case SpellType.Burst:
                {
                    cycle = 1.8f;
                    if (_t > cycle) { _t -= cycle; prev = -1f; }
                    if (_t < 0.8f)
                    {
                        int count = 8 + (int)power * 4;
                        for (int i = 0; i < count; i++)
                        {
                            float a = i * MathF.PI * 2f / count;
                            Vector2 d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                            Vector2 pos = c + d * (_t * 120f);
                            _actors.Add(new Actor { Pos = pos, Angle = a, Size = size * 0.6f });
                            if ((float)Random.Shared.NextDouble() < dt * 25f) SpellVfx.EmitTrail(_fx, e, pos, -d, size * 0.6f);
                        }
                    }
                    else if (Crossed(prev, _t, 0.8f))
                        SpellVfx.Impact(_fx, e, c, 0.8f);
                    break;
                }

                case SpellType.Area:
                    cycle = 2f;
                    if (_t > cycle) { _t -= cycle; prev = -1f; }
                    if (Crossed(prev, _t, 0.15f)) SpellVfx.AreaBlast(_fx, e, c, 56f);
                    break;

                case SpellType.Rain:
                {
                    if ((float)Random.Shared.NextDouble() < dt * 16f)
                        _drops.Add((new Vector2(Rr(area.X + 30f, area.X + area.Width - 30f), area.Y - 8f), 260f));
                    for (int i = _drops.Count - 1; i >= 0; i--)
                    {
                        var (pos, vy) = _drops[i];
                        vy += 800f * dt;
                        pos.Y += vy * dt;
                        if (pos.Y > area.Y + area.Height - 26f)
                        {
                            SpellVfx.Impact(_fx, e, pos, 0.45f);
                            _drops.RemoveAt(i);
                            continue;
                        }
                        _drops[i] = (pos, vy);
                        _actors.Add(new Actor { Pos = pos, Angle = MathF.PI * 0.5f, Size = 6.5f });
                        if ((float)Random.Shared.NextDouble() < dt * 30f) SpellVfx.EmitTrail(_fx, e, pos, new Vector2(0, -1), 6.5f);
                    }
                    break;
                }

                case SpellType.Lightning:
                {
                    cycle = 1.7f;
                    if (_t > cycle) { _t -= cycle; prev = -1f; }
                    if (Crossed(prev, _t, 0.2f))
                    {
                        Vector2 a = new Vector2(left, c.Y + 8f), b = new Vector2(right, c.Y - 8f);
                        var paths = new List<Vector2[]> { SpellVfx.BuildBolt(a, b) };
                        var main = paths[0];
                        for (int i = 0; i < 3; i++)
                        {
                            Vector2 o = main[Random.Shared.Next(1, main.Length - 1)];
                            float ba = MathF.Atan2(b.Y - a.Y, b.X - a.X) + (Random.Shared.Next(2) == 0 ? -1f : 1f) * Rr(0.5f, 1.1f);
                            Vector2 be = o + new Vector2(MathF.Cos(ba), MathF.Sin(ba)) * Rr(25f, 60f);
                            paths.Add(SpellVfx.BuildBolt(o, be, 0.7f));
                        }
                        SpellVfx.SpawnBolt(_fx, paths, SpellVfx.Style(e).Mid);
                        SpellVfx.Impact(_fx, e, b, 1.1f);
                    }
                    break;
                }

                case SpellType.Shield:
                    int shieldCount = 4 + (int)power * 2;
                    for (int i = 0; i < shieldCount; i++)
                    {
                        float a = _t * 2.4f + i * MathF.PI * 2f / shieldCount;
                        Vector2 d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                        Vector2 pos = c + d * 40f;
                        _actors.Add(new Actor { Pos = pos, Angle = a + MathF.PI * 0.5f, Size = size * 0.7f });
                        if ((float)Random.Shared.NextDouble() < dt * 20f)
                            SpellVfx.EmitTrail(_fx, e, pos, new Vector2(-MathF.Sin(a), MathF.Cos(a)) * -1f, size * 0.7f);
                    }
                    if (_t > 100f) _t = 0f;
                    break;

                case SpellType.Summon:
                    cycle = 2.4f;
                    if (_t > cycle) { _t -= cycle; prev = -1f; }
                    if (_t < 1.5f) SpellVfx.EmitSummonCharge(_fx, e, c + new Vector2(0, 14f), _t / 1.5f, dt);
                    if (Crossed(prev, _t, 1.5f)) SpellVfx.AreaBlast(_fx, e, c + new Vector2(0, 14f), 46f);
                    break;
            }

            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                _fx[i].Update(dt);
                if (!_fx[i].IsAlive) _fx.RemoveAt(i);
            }
        }

        public void Draw(Rectangle area, SpellElement e, SpellType type)
        {
            var st = SpellVfx.Style(e);
            float time = (float)Raylib.GetTime();

            Raylib.BeginScissorMode((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);

            // fond : dégradé sombre teinté par l'élément
            Raylib.DrawRectangleGradientV((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height,
                new Color((byte)12, (byte)14, (byte)22, (byte)255), new Color((byte)26, (byte)30, (byte)42, (byte)255));
            Vector2 c = new Vector2(area.X + area.Width * 0.5f, area.Y + area.Height * 0.5f);
            Raylib.BeginBlendMode(BlendMode.Additive);
            SpellVfx.Glow(c, area.Width * 0.5f, new Color(st.Dark.R, st.Dark.G, st.Dark.B, (byte)90));
            Raylib.EndBlendMode();
            // sol
            float groundY = area.Y + area.Height - 22f;
            Raylib.DrawRectangle((int)area.X, (int)groundY, (int)area.Width, (int)(area.Height - (groundY - area.Y)), new Color((byte)18, (byte)20, (byte)28, (byte)255));
            Raylib.DrawLine((int)area.X, (int)groundY, (int)(area.X + area.Width), (int)groundY, new Color(st.Mid.R, st.Mid.G, st.Mid.B, (byte)60));

            if (type == SpellType.Summon && _t < 1.9f)
            {
                float prog = _t < 1.5f ? _t / 1.5f : 1f;
                SpellVfx.DrawSummonRune(e, c + new Vector2(0, 14f), prog, time);
            }

            SpellVfx.DrawBatched(_fx);
            foreach (var a in _actors)
                SpellVfx.DrawBody(e, a.Pos, a.Size, a.Angle, time, 0.7f, 1f);

            Raylib.EndScissorMode();
        }
    }
}
