// Weather.cs - Système météo (pluie) : rendu par-dessus le monde, stoppé par les toits, plocs au sol,
// son d'ambiance en boucle, et cycle automatique aléatoire (démarre/s'arrête aléatoirement).
//
// Principe (top-down) : chaque goutte vise un POINT DU SOL fixe (Target). Elle "tombe" depuis
// une hauteur virtuelle importante jusqu'à ce point en un temps court (quelques centaines de ms),
// ce qui donne l'illusion d'une chute rapide depuis le ciel. Une fois au sol, elle produit un ploc
// (si la case n'est pas protégée par un toit) puis attend un délai aléatoire avant de recommencer
// sur une nouvelle case aléatoire. Comme chaque goutte a son propre timer/délai, il n'y a pas de
// "vague" synchronisée : la pluie tombe en continu, dispersée sur toute la zone visible.
//
// Cycle automatique : un minuteur interne alterne entre périodes sèches (quelques minutes à
// quelques dizaines de minutes) et périodes de pluie (une à quelques minutes), avec un fondu
// d'intensité à l'entrée/sortie (densité de gouttes + volume du son) pour un début/fin progressifs.
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.IO;
using System.Runtime.InteropServices;

namespace Soulfract
{
    public enum WeatherType
    {
        Clear,
        Rain,
        Storm
    }

    public static class Weather
    {
        public static WeatherType Current { get; private set; } = WeatherType.Clear;

        private const int MAX_DROPS = 760;
        private const int NORMAL_RAIN_DROP_COUNT = 260;
        private const int STORM_RAIN_DROP_COUNT = 680;

        // Hauteur virtuelle depuis laquelle la goutte "tombe" (donne la vitesse/l'inclinaison visuelle)
        private const float FALL_HEIGHT_MIN = 350f;
        private const float FALL_HEIGHT_MAX = 650f;

        // Durée de chute : rapide, pour ne pas traverser l'écran lentement
        private const float FALL_DURATION_MIN = 0.22f;
        private const float FALL_DURATION_MAX = 0.40f;

        // Pause avant de retomber ailleurs, désynchronise complètement chaque goutte
        private const float RESPAWN_DELAY_MAX = 1.4f;

        // Épaisseur et longueur des gouttes : légèrement plus épaisses en moyenne, avec variation aléatoire
        private const float DROP_THICKNESS_MIN = 2.2f;
        private const float DROP_THICKNESS_MAX = 4.2f;
        private const float DROP_LENGTH_MIN = 14f;
        private const float DROP_LENGTH_MAX = 30f;

        private const float DRIFT_RATIO = 0.22f; // inclinaison (vent) proportionnelle à la hauteur restante

        // ─────────────────────── Cycle automatique (démarre/s'arrête aléatoirement) ───────────────────────
        private const float RAIN_DURATION_MIN = 90f;   // 1,5 min
        private const float RAIN_DURATION_MAX = 240f;  // 4 min
        private const float STORM_DURATION_MIN = 90f;
        private const float STORM_DURATION_MAX = 180f;
        private const float DRY_DURATION_MIN = 150f;   // 2,5 min
        private const float DRY_DURATION_MAX = 600f;   // 10 min
        private const float INTENSITY_FADE_SPEED = 0.35f; // par seconde (≈ 3s pour un fondu complet)
    private const float STORM_TRANSITION_SPEED = 0.18f;

        private static bool _autoInitialized = false;
        private static float _autoStateTimer = 0f;
        private static float _intensity = 0f; // 0 = pas de pluie visible/audible, 1 = pluie pleine intensité
        private static float _stormIntensity;
        private static bool _isPaused;
        private static bool _thunderPaused;
        private static float _nextLightningTimer;
        private static float _lightningFlashTimer;
        private static float _thunderDelay = -1f;
        private static readonly List<Vector2> _lightningPoints = new();

        // ─────────────────────────────────────────── Son ───────────────────────────────────────────
        // Double piste pour éviter le "cut" entre les boucles
        private const float RAIN_VOLUME = 0.55f;
        private static Music _rainMusicA;
        private static Music _rainMusicB;
        private static bool _rainMusicLoadAttempted = false;
        private static bool _rainMusicReady = false;
        private static bool _rainMusicPlaying = false;
        private static float _rainMusicStartDelay = 0f;
        private const float MUSIC_OFFSET = 0.3f; // décalage en secondes pour masquer le cut
        private static Sound _thunderSound;
        private static bool _thunderSoundLoadAttempted;
        private static bool _thunderSoundReady;

        private struct RainDrop
        {
            public Vector2 Target;      // point du sol visé
            public float FallHeight;    // hauteur virtuelle de départ
            public float FallDuration;  // durée de la chute
            public float Delay;         // attente avant de retomber (désynchro)
            public float Timer;         // 0 -> Delay : attente ; Delay -> Delay+FallDuration : chute
            public float Thickness;     // épaisseur du trait (variable par goutte)
            public float Length;        // longueur du trait (variable par goutte)
        }

        private struct Splash
        {
            public Vector2 Pos;
            public float Life;
            public float MaxLife;
        }

        private static List<RainDrop> _drops = new();
        private static List<Splash> _splashes = new();
        private static Random _rand = new Random();
        private static bool _initialized = false;

        private static readonly Color RainColor = new Color(180, 200, 230, 200);
        private static readonly Color SplashColor = new Color(200, 215, 240, 170);
        private static readonly Color SnowColor = new Color(245, 250, 255, 245);
        private static readonly Color SnowSplashColor = new Color(245, 248, 255, 140);

        public static float TimeUntilNextChange => _autoStateTimer;

        public static void ResetForWorld(string? weatherName = "clear", float timeRemaining = 0f)
        {
            WeatherType type = weatherName?.Trim().ToLowerInvariant() switch
            {
                "rain" or "pluie" => WeatherType.Rain,
                "storm" or "tempete" or "tempête" or "orage" => WeatherType.Storm,
                _ => WeatherType.Clear
            };

            if (_rainMusicPlaying && _rainMusicReady)
            {
                Raylib.StopMusicStream(_rainMusicA);
                Raylib.StopMusicStream(_rainMusicB);
            }
            if (_thunderSoundReady)
                Raylib.StopSound(_thunderSound);

            Current = type;
            _autoInitialized = true;
            _autoStateTimer = timeRemaining > 0f
                ? timeRemaining
                : type switch
                {
                    WeatherType.Rain => RandRange(RAIN_DURATION_MIN, RAIN_DURATION_MAX),
                    WeatherType.Storm => RandRange(STORM_DURATION_MIN, STORM_DURATION_MAX),
                    _ => RandRange(DRY_DURATION_MIN, DRY_DURATION_MAX)
                };
            _intensity = 0f;
            _stormIntensity = 0f;
            _drops.Clear();
            _splashes.Clear();
            _initialized = false;
            _lightningPoints.Clear();
            _lightningFlashTimer = 0f;
            _thunderDelay = -1f;
            _thunderPaused = false;
            _nextLightningTimer = type == WeatherType.Storm ? RandRange(2f, 7f) : 0f;
            _rainMusicPlaying = false;
            _rainMusicStartDelay = 0f;
        }

        public static void SetPaused(bool paused)
        {
            if (_isPaused == paused)
                return;

            _isPaused = paused;
            if (_rainMusicReady && _rainMusicPlaying)
            {
                if (paused)
                {
                    Raylib.PauseMusicStream(_rainMusicA);
                    Raylib.PauseMusicStream(_rainMusicB);
                }
                else
                {
                    Raylib.ResumeMusicStream(_rainMusicA);
                    Raylib.ResumeMusicStream(_rainMusicB);
                }
            }

            if (_thunderSoundReady)
            {
                if (paused && Raylib.IsSoundPlaying(_thunderSound))
                {
                    Raylib.PauseSound(_thunderSound);
                    _thunderPaused = true;
                }
                else if (!paused && _thunderPaused)
                {
                    Raylib.ResumeSound(_thunderSound);
                    _thunderPaused = false;
                }
            }

            if (paused)
            {
                _lightningPoints.Clear();
                _lightningFlashTimer = 0f;
                _thunderDelay = -1f;
            }
        }

        private static bool IsSnowBiome(Biome biome)
        {
            return biome == Biome.Snow || biome == Biome.SnowyForest || biome == Biome.Tundra || biome == Biome.IceSpikes;
        }

        private static bool IsSnowTarget(int tileX, int tileY)
        {
            if (World.GetGroundTileIdAt(tileX, tileY) == 43)
                return true;

            float worldX = tileX * Program.TileSize + Program.TileSize * 0.5f;
            float worldY = tileY * Program.TileSize + Program.TileSize * 0.5f;
            return IsSnowBiome(World.GetBiomeAt(worldX, worldY));
        }

        /// <summary>Force un état météo (utilisé par la commande /weather). Réinitialise aussi le minuteur
        /// automatique pour que le cycle ne reparte pas immédiatement dans l'autre sens.</summary>
        public static void SetWeather(WeatherType type)
        {
            bool enteringStorm = type == WeatherType.Storm && Current != WeatherType.Storm;
            Current = type;
            _autoInitialized = true;
            _autoStateTimer = type switch
            {
                WeatherType.Rain => RandRange(RAIN_DURATION_MIN, RAIN_DURATION_MAX),
                WeatherType.Storm => RandRange(STORM_DURATION_MIN, STORM_DURATION_MAX),
                _ => RandRange(DRY_DURATION_MIN, DRY_DURATION_MAX)
            };

            if (enteringStorm)
                _nextLightningTimer = RandRange(2f, 7f);
            else if (type != WeatherType.Storm)
            {
                _lightningPoints.Clear();
                _lightningFlashTimer = 0f;
                _thunderDelay = -1f;
            }

        }

        public static bool SetWeather(string name)
        {
            switch (name.ToLower())
            {
                case "rain":
                case "pluie":
                    SetWeather(WeatherType.Rain);
                    return true;
                case "storm":
                case "tempete":
                case "tempête":
                case "orage":
                    SetWeather(WeatherType.Storm);
                    return true;
                case "clear":
                case "clair":
                case "beau":
                    SetWeather(WeatherType.Clear);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Renvoie true si la tuile est protégée de la pluie par un toit.</summary>
        private static bool IsShelteredAt(int tileX, int tileY)
        {
            return World.GetOverlayAt(tileX, tileY) != 0;
        }

        private static float RandRange(float min, float max) => min + (float)_rand.NextDouble() * (max - min);

        private static RainDrop MakeDrop(Rectangle bounds)
        {
            return new RainDrop
            {
                Target = new Vector2(
                    bounds.X + (float)_rand.NextDouble() * bounds.Width,
                    bounds.Y + (float)_rand.NextDouble() * bounds.Height),
                FallHeight = RandRange(FALL_HEIGHT_MIN, FALL_HEIGHT_MAX),
                FallDuration = RandRange(FALL_DURATION_MIN, FALL_DURATION_MAX),
                Delay = RandRange(0f, RESPAWN_DELAY_MAX),
                Timer = 0f,
                Thickness = RandRange(DROP_THICKNESS_MIN, DROP_THICKNESS_MAX),
                Length = RandRange(DROP_LENGTH_MIN, DROP_LENGTH_MAX)
            };
        }

        private static Rectangle GetVisibleBounds(Camera2D camera)
        {
            Vector2 topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera);
            Vector2 bottomRight = Raylib.GetScreenToWorld2D(new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), camera);
            const float margin = 40f; // léger débord pour éviter les "trous" sur les bords à l'écran
            return new Rectangle(
                topLeft.X - margin, topLeft.Y - margin,
                (bottomRight.X - topLeft.X) + margin * 2f,
                (bottomRight.Y - topLeft.Y) + margin * 2f);
        }

        private static void LoadRainMusicIfNeeded()
        {
            if (_rainMusicLoadAttempted) return;
            _rainMusicLoadAttempted = true;

            string path = Path.Combine("assets", "sounds", "rain.mp3");
            if (!File.Exists(path)) return;

            _rainMusicA = Raylib.LoadMusicStream(path);
            _rainMusicB = Raylib.LoadMusicStream(path);
            _rainMusicReady = Raylib.IsMusicReady(_rainMusicA) && Raylib.IsMusicReady(_rainMusicB);
            if (_rainMusicReady)
            {
                _rainMusicA.Looping = true;
                _rainMusicB.Looping = true;
                // Démarrage différé pour créer le décalage
                _rainMusicStartDelay = MUSIC_OFFSET;
            }
        }

        /// <summary>Alterne automatiquement entre périodes sèches et pluvieuses, à des durées aléatoires.
        /// N'écrase pas un /weather manuel récent puisque SetWeather() réinitialise déjà le minuteur.</summary>
        private static void UpdateAutoCycle(float dt)
        {
            if (NetworkManager.IsClient)
                return;

            if (!_autoInitialized)
            {
                _autoInitialized = true;
                // Premier délai avant la première pluie de la session, lui aussi aléatoire
                Current = WeatherType.Clear;
                _autoStateTimer = RandRange(DRY_DURATION_MIN, DRY_DURATION_MAX);
                return;
            }

            _autoStateTimer -= dt;
            if (_autoStateTimer > 0f) return;

            if (Current == WeatherType.Storm)
            {
                SetWeather(WeatherType.Clear);
            }
            else if (Current == WeatherType.Rain)
                SetWeather(_rand.NextDouble() < 0.2 ? WeatherType.Storm : WeatherType.Clear);
            else
                SetWeather(_rand.NextDouble() < 0.24 ? WeatherType.Storm : WeatherType.Rain);
        }

        private static int GetActiveDropCount()
        {
            if (Current == WeatherType.Clear && _stormIntensity <= 0f)
                return 0;

            float stormBlend = Math.Clamp(_stormIntensity, 0f, 1f);
            float targetDropCount = NORMAL_RAIN_DROP_COUNT
                + (STORM_RAIN_DROP_COUNT - NORMAL_RAIN_DROP_COUNT) * stormBlend;
            return Math.Clamp((int)(targetDropCount * _intensity), 0, MAX_DROPS);
        }

        private static void LoadThunderSoundIfNeeded()
        {
            if (_thunderSoundLoadAttempted)
                return;

            _thunderSoundLoadAttempted = true;
            const int sampleRate = 22050;
            int sampleCount = sampleRate * 3;
            var samples = new short[sampleCount];
            float lowPass = 0f;

            for (int i = 0; i < samples.Length; i++)
            {
                float time = (float)i / sampleRate;
                float noise = (float)(_rand.NextDouble() * 2.0 - 1.0);
                lowPass += (noise - lowPass) * 0.035f;
                float crack = time < 0.09f ? noise * 0.35f * (1f - time / 0.09f) : 0f;
                float rumble = MathF.Sin(time * 23f + lowPass * 8f) * MathF.Exp(-time * 2.1f);
                float tail = MathF.Exp(-time * 1.25f);
                float signal = (lowPass * 0.78f + noise * 0.12f + rumble * 0.5f + crack) * tail;
                samples[i] = (short)(Math.Clamp(signal, -1f, 1f) * short.MaxValue * 0.72f);
            }

            IntPtr sampleData = Marshal.AllocHGlobal(samples.Length * sizeof(short));
            try
            {
                Marshal.Copy(samples, 0, sampleData, samples.Length);
                unsafe
                {
                    Wave wave = new()
                    {
                        SampleCount = (uint)sampleCount,
                        SampleRate = sampleRate,
                        SampleSize = 16,
                        Channels = 1,
                        Data = sampleData.ToPointer()
                    };
                    _thunderSound = Raylib.LoadSoundFromWave(wave);
                }
                _thunderSoundReady = Raylib.IsSoundReady(_thunderSound);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Weather] Son de tonnerre indisponible : {ex.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(sampleData);
            }
        }

        private static void StartLightning(Rectangle bounds)
        {
            _lightningPoints.Clear();
            float x = RandRange(bounds.X + bounds.Width * 0.1f, bounds.X + bounds.Width * 0.9f);
            float y = bounds.Y;
            float endY = bounds.Y + bounds.Height * RandRange(0.55f, 0.92f);
            _lightningPoints.Add(new Vector2(x, y));

            const int segments = 9;
            for (int i = 1; i <= segments; i++)
            {
                float progress = (float)i / segments;
                x += RandRange(-42f, 42f);
                y = bounds.Y + (endY - bounds.Y) * progress;
                _lightningPoints.Add(new Vector2(x, y));
            }

            _lightningFlashTimer = 0.24f;
            _thunderDelay = RandRange(0.35f, 1.25f);
            _nextLightningTimer = RandRange(8f, 16f);
        }

        private static void UpdateLightning(float dt, Rectangle bounds)
        {
            if (Current != WeatherType.Storm)
                return;

            if (_lightningFlashTimer > 0f)
                _lightningFlashTimer = Math.Max(0f, _lightningFlashTimer - dt);

            if (_thunderDelay >= 0f)
            {
                _thunderDelay -= dt;
                if (_thunderDelay <= 0f)
                {
                    LoadThunderSoundIfNeeded();
                    if (_thunderSoundReady)
                    {
                        Raylib.SetSoundVolume(_thunderSound, 0.72f);
                        Raylib.PlaySound(_thunderSound);
                    }
                    _thunderDelay = -1f;
                }
            }

            _nextLightningTimer -= dt;
            if (_nextLightningTimer <= 0f)
                StartLightning(bounds);
        }

        public static void Update(float dt, Camera2D camera)
        {
            if (_isPaused)
                return;

            if (World.IsUnderground)
            {
                if (_rainMusicPlaying)
                {
                    Raylib.StopMusicStream(_rainMusicA);
                    Raylib.StopMusicStream(_rainMusicB);
                    _rainMusicPlaying = false;
                }

                _intensity = 0f;
                _drops.Clear();
                _splashes.Clear();
                _initialized = false;
                return;
            }

            UpdateAutoCycle(dt);

            // Fondu progressif de l'intensité (densité de gouttes + volume du son), pour un
            // début et une fin de pluie progressifs plutôt qu'un interrupteur brutal.
            float targetStormIntensity = Current == WeatherType.Storm ? 1f : 0f;
            float stormStep = STORM_TRANSITION_SPEED * dt;
            _stormIntensity = Math.Abs(targetStormIntensity - _stormIntensity) <= stormStep
                ? targetStormIntensity
                : _stormIntensity + Math.Sign(targetStormIntensity - _stormIntensity) * stormStep;

            float targetIntensity = Current == WeatherType.Clear ? 0f : 1f;
            float fadeSpeed = Current == WeatherType.Storm || _stormIntensity > 0.001f
                ? STORM_TRANSITION_SPEED
                : INTENSITY_FADE_SPEED;
            float maxStep = fadeSpeed * dt;
            _intensity = Math.Abs(targetIntensity - _intensity) <= maxStep
                ? targetIntensity
                : _intensity + Math.Sign(targetIntensity - _intensity) * maxStep;

            // ── Son d'ambiance (double piste) ──
            if (_intensity > 0.001f)
            {
                LoadRainMusicIfNeeded();
                if (_rainMusicReady)
                {
                    if (!_rainMusicPlaying)
                    {
                        // Lance A immédiatement, B avec un court délai
                        Raylib.PlayMusicStream(_rainMusicA);
                        _rainMusicStartDelay = MUSIC_OFFSET;
                        _rainMusicPlaying = true;
                    }

                    // Mise à jour et volume des deux flux
                    Raylib.UpdateMusicStream(_rainMusicA);
                    if (_rainMusicStartDelay > 0f)
                    {
                        _rainMusicStartDelay -= dt;
                        if (_rainMusicStartDelay <= 0f)
                            Raylib.PlayMusicStream(_rainMusicB);
                    }
                    else
                    {
                        Raylib.UpdateMusicStream(_rainMusicB);
                    }

                    float vol = _intensity * RAIN_VOLUME * (Current == WeatherType.Storm ? 1.18f : 1f);
                    // Chacune à 50% pour garder le volume total souhaité
                    Raylib.SetMusicVolume(_rainMusicA, vol * 0.5f);
                    Raylib.SetMusicVolume(_rainMusicB, vol * 0.5f);
                }
            }
            else if (_rainMusicPlaying)
            {
                Raylib.StopMusicStream(_rainMusicA);
                Raylib.StopMusicStream(_rainMusicB);
                _rainMusicPlaying = false;
            }

            if (_intensity <= 0.001f)
            {
                _drops.Clear();
                _splashes.Clear();
                _initialized = false;
                return;
            }

            var bounds = GetVisibleBounds(camera);
            UpdateLightning(dt, bounds);

            if (!_initialized)
            {
                _drops.Clear();
                for (int i = 0; i < MAX_DROPS; i++)
                    _drops.Add(MakeDrop(bounds));
                _initialized = true;
            }

            int ts = Program.TileSize;

            // Le nombre de gouttes réellement actives suit l'intensité : peu au début/à la fin, toutes à fond.
            int activeDrops = GetActiveDropCount();

            for (int i = 0; i < _drops.Count; i++)
            {
                if (i >= activeDrops) break; // gouttes au-delà de l'intensité actuelle restent en pause

                var drop = _drops[i];
                drop.Timer += dt;

                float cycleLength = drop.Delay + drop.FallDuration;
                if (drop.Timer >= cycleLength)
                {
                    // La goutte vient de toucher le sol : ploc si la case visée est à ciel ouvert
                    int tileX = (int)MathF.Floor(drop.Target.X / ts);
                    int tileY = (int)MathF.Floor(drop.Target.Y / ts);
                    if (!IsShelteredAt(tileX, tileY))
                    {
                        bool isSnow = IsSnowTarget(tileX, tileY);

                        if (_splashes.Count < MAX_DROPS * 2)
                        {
                            float splashSize = MathHelper.Lerp(0.7f, 1.3f, (drop.Thickness - DROP_THICKNESS_MIN) / (DROP_THICKNESS_MAX - DROP_THICKNESS_MIN));
                            _splashes.Add(new Splash { Pos = drop.Target, Life = 0.3f * splashSize, MaxLife = 0.3f * splashSize });
                        }

                        //  La pluie mouille les tuiles de farmland sèches seulement si ce n'est pas de la neige.
                        if (!isSnow && World.GetGroundTileIdAt(tileX, tileY) == 27 && !World.IsFarmlandWet(tileX, tileY))
                        {
                            World.WaterFarmland(tileX, tileY);
                        }
                    }

                    // Nouvelle goutte ailleurs sur la zone visible, avec son propre délai/taille aléatoires
                    _drops[i] = MakeDrop(bounds);
                    continue;
                }

                _drops[i] = drop;
            }

            for (int i = _splashes.Count - 1; i >= 0; i--)
            {
                var s = _splashes[i];
                s.Life -= dt;
                if (s.Life <= 0)
                {
                    _splashes.RemoveAt(i);
                    continue;
                }
                _splashes[i] = s;
            }
        }

        /// <summary>À appeler DANS BeginMode2D/EndMode2D, après le dessin du monde (toits compris), pour un rendu "par-dessus le reste".</summary>
        public static void Draw(Camera2D camera)
        {
            if (World.IsUnderground || _intensity <= 0.001f) return;

            int ts = Program.TileSize;
            int activeDrops = GetActiveDropCount();
            float driftRatio = DRIFT_RATIO + (0.68f - DRIFT_RATIO) * _stormIntensity;

            for (int i = 0; i < _drops.Count && i < activeDrops; i++)
            {
                var drop = _drops[i];

                // Toujours en attente : rien à dessiner
                if (drop.Timer < drop.Delay) continue;

                int tileX = (int)MathF.Floor(drop.Target.X / ts);
                int tileY = (int)MathF.Floor(drop.Target.Y / ts);

                // Stoppée par les toits : on ne dessine tout simplement pas la goutte sur une case couverte
                if (IsShelteredAt(tileX, tileY)) continue;

                float fallT = Math.Clamp((drop.Timer - drop.Delay) / drop.FallDuration, 0f, 1f);
                float remainingHeight = drop.FallHeight * (1f - fallT);

                // Position actuelle : au-dessus de la cible, se rapprochant à mesure que fallT -> 1
                Vector2 pos = new Vector2(
                    drop.Target.X + remainingHeight * driftRatio,
                    drop.Target.Y - remainingHeight);

                bool isSnow = IsSnowTarget(tileX, tileY);

                if (isSnow)
                {
                    float size = MathHelper.Lerp(7f, 12f, (drop.Thickness - DROP_THICKNESS_MIN) / (DROP_THICKNESS_MAX - DROP_THICKNESS_MIN));
                    float half = size * 0.45f;
                    byte alpha = (byte)(SnowColor.A * MathHelper.Lerp(0.8f, 1f, fallT) * _intensity);
                    Color c = new Color(SnowColor.R, SnowColor.G, SnowColor.B, alpha);
                    Raylib.DrawLineEx(new Vector2(pos.X - half, pos.Y), new Vector2(pos.X + half, pos.Y), 2.8f, c);
                    Raylib.DrawLineEx(new Vector2(pos.X, pos.Y - half), new Vector2(pos.X, pos.Y + half), 2.8f, c);
                }
                else
                {
                    // La traînée raccourcit visuellement en approchant du sol (longueur propre à chaque goutte)
                    float visualLength = drop.Length * MathHelper.Lerp(1.4f, 0.5f, fallT);
                    Vector2 dir = Vector2.Normalize(new Vector2(-driftRatio, 1f));
                    Vector2 end = pos - dir * visualLength;

                    byte alpha = (byte)(RainColor.A * MathHelper.Lerp(0.5f, 1f, fallT) * _intensity);
                    Color c = new Color(RainColor.R, RainColor.G, RainColor.B, alpha);
                    Raylib.DrawLineEx(pos, end, drop.Thickness, c);
                }
            }

            foreach (var s in _splashes)
            {
                int tileX = (int)MathF.Floor(s.Pos.X / ts);
                int tileY = (int)MathF.Floor(s.Pos.Y / ts);
                if (IsShelteredAt(tileX, tileY)) continue;

                bool isSnowSplash = IsSnowTarget(tileX, tileY);

                float t = 1f - (s.Life / s.MaxLife); // 0 -> 1
                float radius = 2.5f + t * 8.5f;
                byte alpha = (byte)((isSnowSplash ? SnowSplashColor.A : SplashColor.A) * (1f - t) * _intensity);
                Color c = new Color(
                    isSnowSplash ? SnowSplashColor.R : SplashColor.R,
                    isSnowSplash ? SnowSplashColor.G : SplashColor.G,
                    isSnowSplash ? SnowSplashColor.B : SplashColor.B,
                    alpha);

                if (isSnowSplash)
                {
                    Raylib.DrawCircle((int)s.Pos.X, (int)s.Pos.Y, radius * 0.55f, c);
                }
                else
                {
                    Raylib.DrawCircleLines((int)s.Pos.X, (int)s.Pos.Y, radius, c);
                    float crossSize = Math.Max(2f, radius * 0.35f);
                    Raylib.DrawLineEx(new Vector2(s.Pos.X - crossSize, s.Pos.Y), new Vector2(s.Pos.X + crossSize, s.Pos.Y), 1.4f, c);
                    Raylib.DrawLineEx(new Vector2(s.Pos.X, s.Pos.Y - crossSize), new Vector2(s.Pos.X, s.Pos.Y + crossSize), 1.4f, c);
                }
            }

            if (Current == WeatherType.Storm && _lightningFlashTimer > 0f)
            {
                for (int i = 1; i < _lightningPoints.Count; i++)
                {
                    Raylib.DrawLineEx(_lightningPoints[i - 1], _lightningPoints[i], 9f, new Color(115, 175, 255, 110));
                    Raylib.DrawLineEx(_lightningPoints[i - 1], _lightningPoints[i], 3.2f, new Color(245, 250, 255, 255));
                }
            }
        }

        public static float CurrentIntensity => _intensity;
    public static float StormIntensity => _stormIntensity;

        public static void DrawScreenTint()
        {
            if (World.IsUnderground || _intensity <= 0.001f) return;

            int screenWidth = Raylib.GetScreenWidth();
            int screenHeight = Raylib.GetScreenHeight();
            byte alpha = (byte)Math.Clamp(_intensity * (Current == WeatherType.Storm ? 92f : 58f), 0f, 130f);
            Color tint = new Color((byte)36, (byte)50, (byte)82, alpha);
            Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, tint);

            if (Current == WeatherType.Storm && _lightningFlashTimer > 0f)
            {
                float flash = _lightningFlashTimer > 0.16f
                    ? 1f
                    : Math.Clamp(_lightningFlashTimer / 0.16f, 0f, 1f);
                byte flashAlpha = (byte)(145f * flash);
                Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(215, 230, 255, (int)flashAlpha));
            }
        }
    }
}