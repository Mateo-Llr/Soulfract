// FishingProjectile.cs
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public class FishingProjectile
    {
        public Vector2 Position;      // Position actuelle (monde)
        public Vector2 TargetPosition; // Point d'impact sur l'eau
        public Vector2 StartPosition;
        public float Height = 0f;
        public float MaxHeight = 60f;
        public float Progress = 0f;
        public float Speed = 0.8f;     // secondes pour arriver
        public bool IsAlive = true;
        public bool HasLanded = false;

        // État une fois dans l'eau (avant la morsure)
        public float WaitTimer = 0f;

        // ─── Système de rareté / mini-jeu à étoiles ─────────────────────────
        // Nombre d'étoiles à valider pour attraper le poisson (dépend de sa rareté).
        public int Rarity = 1;
        // Il faut 3 clics réussis par étoile.
        public int RequiredClicks => Rarity * 3;
        // Nombre total de clics réussis jusqu'ici.
        public int ClicksDone = 0;
        // Nombre d'étoiles actuellement affichées au-dessus du flotteur (apparition progressive).
        public int StarsRevealed = 0;
        public float StarRevealTimer = 0f;
        public const float STAR_REVEAL_INTERVAL = 0.22f;

        // Le poisson a "mordu" et le mini-jeu de ferrage a commencé.
        public bool HookStarted = false;
        // Une fenêtre de clic (clapotis) est active : le joueur doit cliquer maintenant.
        public bool IsBiting = false;
        public float BiteWindowTimer = 0f;
        public float BiteWindowDuration = 0.55f;
        // Délai avant la prochaine fenêtre de clic.
        public float NextBiteDelay = 0f;

        public bool Caught = false;
        public bool Fled = false; // Le poisson s'est échappé (raté / trop tôt / trop tard)

        private static Texture2D _starEmptyTexture = new Texture2D();
        private static Texture2D _star1Texture = new Texture2D();
        private static Texture2D _star2Texture = new Texture2D();
        private static Texture2D _starFullTexture = new Texture2D();
        private static bool _starTexturesLoaded = false;

        public float LifeTime = 0f;
        public const float MAX_LIFETIME = 25f;

        // Pour l'animation
        public float BobOffset = 0f;
        public float BobSpeed = 3f;

        // Petit flash visuel quand une fenêtre de clic démarre / est ratée
        public float RippleFlash = 0f;

        public FishingProjectile(Vector2 start, Vector2 target, float power, int rarity = 1)
        {
            StartPosition = start;
            TargetPosition = target;
            // La puissance influence vitesse et hauteur
            float normPower = Math.Clamp(power, 0.3f, 1f);
            Speed = 0.6f + normPower * 0.6f;  // entre 0.6 et 1.2 sec
            MaxHeight = 40f + normPower * 80f;
            Progress = 0f;
            Height = 0f;
            WaitTimer = 0f;
            HookStarted = false;
            IsBiting = false;
            Caught = false;
            Fled = false;
            Rarity = Math.Clamp(rarity, 1, 5);
        }

        public void Update(float dt)
        {
            if (!IsAlive) return;

            if (RippleFlash > 0f) RippleFlash -= dt;

            if (!HasLanded)
            {
                Progress += dt / Speed;
                if (Progress >= 1f)
                {
                    // Atterrissage
                    HasLanded = true;
                    Position = TargetPosition;
                    Height = 0f;
                    LifeTime = 0f;
                    WaitTimer = 1.5f + (float)Random.Shared.NextDouble() * 2f; // délai avant morsure
                    // Effet de clapotis : quelques particules bleues
                    try
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            float ang = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                            float speed = (float)(20 + Random.Shared.NextDouble() * 40);
                            Vector2 vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * speed + new Vector2(0, -20);
                            Color color = new Color(120 + Random.Shared.Next(0, 80), 160 + Random.Shared.Next(0, 80), 220, 220);
                            Program.GetParticleList().Add(new Particle(Position, vel, color, 3, 0.45f + (float)Random.Shared.NextDouble() * 0.2f));
                        }
                    }
                    catch { }
                }
                else
                {
                    float t = Progress;
                    Position = Vector2.Lerp(StartPosition, TargetPosition, t);
                    float parabola = MathF.Sin(t * MathF.PI) * MaxHeight;
                    Height = parabola;
                }
            }
            else
            {
                // Dans l'eau
                LifeTime += dt;
                if (LifeTime > MAX_LIFETIME)
                {
                    IsAlive = false;
                    return;
                }

                BobOffset = MathF.Sin(LifeTime * BobSpeed) * 3f;

                if (!HookStarted)
                {
                    WaitTimer -= dt;
                    if (WaitTimer <= 0f)
                    {
                        // Le poisson mord : démarrage du mini-jeu de ferrage
                        HookStarted = true;
                        ClicksDone = 0;
                        StarsRevealed = 0;
                        StarRevealTimer = 0f;
                        NextBiteDelay = 0.35f; // première fenêtre bientôt
                        IsBiting = false;
                    }
                }
                else if (!Caught && !Fled)
                {
                    // Apparition progressive des étoiles au-dessus de l'eau
                    if (StarsRevealed < Rarity)
                    {
                        StarRevealTimer += dt;
                        if (StarRevealTimer >= STAR_REVEAL_INTERVAL)
                        {
                            StarRevealTimer = 0f;
                            StarsRevealed++;
                        }
                    }

                    if (!IsBiting)
                    {
                        NextBiteDelay -= dt;
                        if (NextBiteDelay <= 0f)
                        {
                            IsBiting = true;
                            RippleFlash = 0.15f;
                            // Fenêtre de clic plus courte à mesure que la rareté (donc la difficulté) augmente
                            BiteWindowDuration = MathF.Max(0.28f, 0.6f - (Rarity - 1) * 0.06f);
                            BiteWindowTimer = BiteWindowDuration;
                            try
                            {
                                for (int i = 0; i < 5; i++)
                                {
                                    float ang = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                                    float speed = (float)(15 + Random.Shared.NextDouble() * 25);
                                    Vector2 vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * speed;
                                    Color color = new Color(150, 190, 230, 200);
                                    Program.GetParticleList().Add(new Particle(Position, vel, color, 2, 0.3f));
                                }
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        BiteWindowTimer -= dt;
                        if (BiteWindowTimer <= 0f)
                        {
                            // Le joueur n'a pas cliqué à temps : le poisson s'échappe
                            IsBiting = false;
                            Fled = true;
                            IsAlive = false;
                        }
                    }
                }
            }
        }

        // Appelé quand le joueur clique gauche pendant que le poisson est ferré.
        // Retourne true uniquement si le poisson est complètement attrapé (toutes les étoiles validées).
        public bool TryCatch()
        {
            if (!HasLanded || !HookStarted || Caught || Fled) return false;

            if (!IsBiting)
            {
                // Clic hors fenêtre (trop tôt, ou pas de clapotis en cours) -> le poisson part
                Fled = true;
                IsAlive = false;
                return false;
            }

            // Clic valide pendant la fenêtre de clapotis
            ClicksDone++;
            IsBiting = false;
            RippleFlash = 0.2f;

            if (ClicksDone >= RequiredClicks)
            {
                Caught = true;
                IsAlive = false;
                return true;
            }

            // Prépare la prochaine fenêtre de clic
            NextBiteDelay = 0.35f + (float)Random.Shared.NextDouble() * 0.5f;
            return false;
        }

        // Index (0..2) du palier de l'étoile en cours de remplissage, pour choisir la bonne texture.
        // 0 = vide, 1 = un tiers, 2 = deux tiers, 3 = pleine.
        public int GetStarTier(int starIndex)
        {
            int clicksForThisStar = ClicksDone - starIndex * 3;
            return Math.Clamp(clicksForThisStar, 0, 3);
        }

        public void Draw()
        {
            if (!IsAlive) return;

            Vector2 drawPos = new Vector2(Position.X, Position.Y - Height);
            // Ombre portée
            Raylib.DrawCircle((int)Position.X, (int)Position.Y, 6, new Color(0, 0, 0, 80));

            // Flotteur
            if (!HasLanded)
            {
                Raylib.DrawCircle((int)drawPos.X, (int)drawPos.Y, 6, new Color(200, 50, 50, 255));
                Raylib.DrawCircle((int)drawPos.X, (int)drawPos.Y - 2, 4, Color.White);
                return;
            }

            float bob = BobOffset;
            Vector2 floatPos = new Vector2(drawPos.X, drawPos.Y + bob);

            // Anneau de clapotis quand une fenêtre de clic est active
            if (HookStarted && IsBiting)
            {
                float pulse = 0.6f + 0.4f * MathF.Sin((float)Raylib.GetTime() * 14f);
                Raylib.DrawCircleLines((int)floatPos.X, (int)floatPos.Y, 14 + pulse * 4f, new Color(200, 230, 255, (int)(220 * pulse)));
                Raylib.DrawCircleLines((int)floatPos.X, (int)floatPos.Y, 22 + pulse * 6f, new Color(200, 230, 255, (int)(120 * pulse)));
            }

            Raylib.DrawCircle((int)floatPos.X, (int)floatPos.Y, 6, new Color(200, 50, 50, 255));
            Raylib.DrawCircle((int)floatPos.X, (int)floatPos.Y - 2, 4, Color.White);

            if (HookStarted && !Caught && !Fled)
            {
                DrawStars(floatPos);

                if (IsBiting)
                {
                    string text = "CLIQUE !";
                    int tw = Raylib.MeasureText(text, 16);
                    Raylib.DrawText(text, (int)(floatPos.X - tw / 2), (int)(floatPos.Y - 30), 16, Color.Yellow);
                }
            }
        }

        private void DrawStars(Vector2 floatPos)
        {
            if (StarsRevealed <= 0) return;

            const int starSize = 12;
            const int spacing = 16;
            float totalWidth = (StarsRevealed - 1) * spacing;
            float startX = floatPos.X - totalWidth / 2f;
            float y = floatPos.Y - 38;

            for (int i = 0; i < StarsRevealed; i++)
            {
                float x = startX + i * spacing;
                int tier = GetStarTier(i); // 0..3
                DrawStarIcon(new Vector2(x, y), starSize, tier);
            }
        }

        private static void EnsureStarTexturesLoaded()
        {
            if (_starTexturesLoaded) return;

            _starEmptyTexture = Raylib.LoadTexture("assets/gui/star_empty.png");
            _star1Texture = Raylib.LoadTexture("assets/gui/star_1.png");
            _star2Texture = Raylib.LoadTexture("assets/gui/star_2.png");
            _starFullTexture = Raylib.LoadTexture("assets/gui/star_full.png");
            _starTexturesLoaded = true;
        }

        private static Texture2D GetStarTextureForTier(int tier)
        {
            EnsureStarTexturesLoaded();
            return tier switch
            {
                1 => _star1Texture,
                2 => _star2Texture,
                3 => _starFullTexture,
                _ => _starEmptyTexture,
            };
        }

        // Dessine une étoile à partir des textures GUI correspondant au palier (0 = vide, 3 = pleine).
        private void DrawStarIcon(Vector2 center, int size, int tier)
        {
            Texture2D texture = GetStarTextureForTier(tier);
            if (texture.Id == 0) return;

            Rectangle source = new Rectangle(0, 0, texture.Width, texture.Height);
            Rectangle destination = new Rectangle(center.X - size / 2f, center.Y - size / 2f, size, size);
            Raylib.DrawTexturePro(texture, source, destination, Vector2.Zero, 0f, Color.White);
        }
    }
}
