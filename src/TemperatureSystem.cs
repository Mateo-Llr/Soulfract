// TemperatureSystem.cs - Version corrigée utilisant le sol réel du joueur
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class TemperatureSystem
    {
        // ===================== RÉGLAGES =====================
        private const float COLD_START = -0.1f;
        private const float HOT_START = 0.55f;

        private const float EXTREME_RANGE = 0.6f;

        private const float WARMTH_PER_POINT = 0.12f;
        private const float COOLING_PER_POINT = 0.12f;
        
        private const float WARMTH_HEAT_PENALTY_FACTOR = 0.15f;
        private const float COOLING_COLD_PENALTY_FACTOR = 0.15f;

        private const float FADE_SPEED = 0.5f;
        private const float DAMAGE_INTENSITY_THRESHOLD = 0.6f;
        private const float DAMAGE_INTERVAL = 2.5f;
        private const int DAMAGE_AMOUNT = 1;
        private const float MAX_OVERLAY_ALPHA = 190f;

        // ===================== ÉTAT INTERNE =====================
        private static float _rawIntensity = 0f;
        private static float _displayedIntensity = 0f;
        private static float _damageTimer = 0f;

        private static Texture2D _coldTexture;
        private static Texture2D _hotTexture;
        private static bool _texturesLoaded = false;

        //  EXPOSÉ POUR LE HUD
        public static float RawAmbientTemperature { get; private set; } = 0f;
        public static Biome CurrentBiome { get; private set; } = Biome.Plains;
        
        //  POUR LE DEBUG
        private static int _lastTileX = 0;
        private static int _lastTileY = 0;

        public static float CurrentIntensity => _displayedIntensity;
        public static bool IsCold => _displayedIntensity < -0.01f;
        public static bool IsHot => _displayedIntensity > 0.01f;

        private static void EnsureTexturesLoaded()
        {
            if (_texturesLoaded) return;
            _texturesLoaded = true;

            if (File.Exists("assets/gui/cold.png"))
                _coldTexture = Raylib.LoadTexture("assets/gui/cold.png");
            else
                Console.WriteLine(" Texture manquante : assets/gui/cold.png");

            if (File.Exists("assets/gui/hot.png"))
                _hotTexture = Raylib.LoadTexture("assets/gui/hot.png");
            else
                Console.WriteLine(" Texture manquante : assets/gui/hot.png");
        }

        private static (int warmth, int cooling) GetEquippedThermalValues(Equipment? equipment)
        {
            if (equipment?.EquippedItems == null) return (0, 0);

            int totalWarmth = 0;
            int totalCooling = 0;

            foreach (var item in equipment.EquippedItems.Values)
            {
                if (item == null) continue;
                int itemId = Program.GetItemId(item.Name);
                if (GameData.ItemDatabase.TryGetValue(itemId, out var data))
                {
                    totalWarmth += data.Warmth;
                    totalCooling += data.Cooling;
                }
            }

            return (totalWarmth, totalCooling);
        }

        /// <summary>
        ///  NOUVEAU : Détermine le biome à partir du sol réel
        /// </summary>
        private static Biome GetBiomeFromGroundId(int groundId, int tileX, int tileY)
        {
            if (groundId == 11)
            {
                // Le sable est partagé entre plage, désert et mangrove.
                // Dans ces cas, on utilise le biome réel issu du climat plutôt que
                // le seul ID de tuile pour éviter de confondre désert et plage.
                var actualBiome = World.GetBiomeAt(tileX, tileY);
                if (actualBiome == Biome.Desert || actualBiome == Biome.Beach || actualBiome == Biome.Mangrove)
                    return actualBiome;

                return Biome.Beach;
            }

            return groundId switch
            {
                // Herbe des plaines
                1 => Biome.Plains,
                
                // Herbe de forêt
                44 => Biome.Forest,
                
                // Herbe de savane
                46 => Biome.Savanna,
                
                // Herbe de taïga
                47 => Biome.Taiga,
                
                // Neige
                43 => Biome.Snow,
                
                // Pierre / Montagne
                26 => Biome.Mountains,
                
                // Eau
                10 => Biome.Ocean,
                
                // Marais
                53 => Biome.Swamp,
                
                // Terre labourée
                27 => Biome.Plains,
                29 => Biome.Plains,
                
                // Chemin pavé
                83 => Biome.Plains,
                
                // Parquet
                54 => Biome.Plains,
                55 => Biome.Plains,
                56 => Biome.Plains,
                57 => Biome.Plains,
                58 => Biome.Plains,
                
                // Souterrain
                100 => Biome.Plains, // Cave
                101 => Biome.Plains, // Lave
                102 => Biome.Plains, // Cristal
                103 => Biome.Plains, // Champignon
                104 => Biome.Plains, // Abysse
                
                _ => Biome.Plains
            };
        }

        /// <summary>
        ///  Obtient la température de base pour un biome donné
        /// </summary>
        private static float GetBaseTemperatureForBiome(Biome biome)
        {
            return biome switch
            {
                // Biomes très froids
                Biome.Snow => -0.7f,
                Biome.IceSpikes => -0.8f,
                Biome.Tundra => -0.6f,
                
                // Biomes froids
                Biome.SnowyForest => -0.3f,
                Biome.Taiga => -0.2f,
                
                // Biomes tempérés frais
                Biome.Mountains => -0.1f,
                Biome.Peak => -0.2f,
                Biome.Steppe => 0.0f,
                
                // Biomes tempérés
                Biome.Plains => 0.2f,
                Biome.Forest => 0.35f,   //  AUGMENTÉ pour correspondre à la réalité
                Biome.BirchForest => 0.25f,
                Biome.DarkForest => 0.2f,
                Biome.Swamp => 0.3f,
                
                // Biomes chauds
                Biome.Desert => 0.7f,
                Biome.Savanna => 0.6f,
                Biome.Jungle => 0.65f,
                Biome.Badlands => 0.5f,
                Biome.Mesa => 0.5f,
                Biome.Mangrove => 0.55f,
                
                // Aquatiques
                Biome.Ocean => 0.2f,
                Biome.DeepOcean => 0.1f,
                Biome.Beach => 0.3f,
                Biome.Cliff => 0.2f,
                
                // Souterrains (température constante)
                _ => 0.15f
            };
        }

        public static void Update(float dt, Vector2 playerPos, Equipment? equipment)
        {
            EnsureTexturesLoaded();

            int ts = Program.TileSize;
            int tileX = (int)Math.Floor(playerPos.X / ts);
            int tileY = (int)Math.Floor(playerPos.Y / ts);
            _lastTileX = tileX;
            _lastTileY = tileY;
            
            //  CRITIQUE : Utiliser le sol réel pour déterminer le biome
            // Cela garantit que le biome affiché correspond à ce qu'on voit
            int groundId = World.GetGroundTileIdAt(tileX, tileY);
            CurrentBiome = GetBiomeFromGroundId(groundId, tileX, tileY);
            
            //  Utiliser la température de base du biome
            float ambientTemp = GetBaseTemperatureForBiome(CurrentBiome);
            
            //  Ajouter une petite variation réaliste
            float variation = PerlinNoise.Noise(tileX * 0.05f, tileY * 0.05f) * 0.08f;
            ambientTemp += variation;
            
            // Correction d'altitude (plus haut = plus froid)
            float elevation = WorldGeneration.GetElevation(playerPos.X, playerPos.Y);
            ambientTemp -= elevation * 0.15f;
            
            // Clamp final
            ambientTemp = Math.Clamp(ambientTemp, -1f, 1f);
            
            RawAmbientTemperature = ambientTemp;

            var (warmth, cooling) = GetEquippedThermalValues(equipment);

            // ===== CALCUL DE LA TEMPÉRATURE RESSENTIE =====
            float feltTemp = ambientTemp;
            feltTemp += warmth * WARMTH_PER_POINT;
            feltTemp -= cooling * COOLING_PER_POINT;

            if (ambientTemp > HOT_START && warmth > 0)
                feltTemp += warmth * WARMTH_HEAT_PENALTY_FACTOR * WARMTH_PER_POINT;

            if (ambientTemp < COLD_START && cooling > 0)
                feltTemp -= cooling * COOLING_COLD_PENALTY_FACTOR * COOLING_PER_POINT;

            feltTemp = Math.Clamp(feltTemp, -1f, 1f);

            // ===== CALCUL DE L'INTENSITÉ =====
            if (feltTemp < COLD_START)
                _rawIntensity = -Math.Clamp((COLD_START - feltTemp) / EXTREME_RANGE, 0f, 1f);
            else if (feltTemp > HOT_START)
                _rawIntensity = Math.Clamp((feltTemp - HOT_START) / EXTREME_RANGE, 0f, 1f);
            else
                _rawIntensity = 0f;

            // ===== TRANSITION DOUCE =====
            float diff = _rawIntensity - _displayedIntensity;
            float maxStep = FADE_SPEED * dt;
            _displayedIntensity += Math.Clamp(diff, -maxStep, maxStep);

            // ===== DÉGÂTS =====
            float absIntensity = MathF.Abs(_displayedIntensity);
            if (absIntensity >= DAMAGE_INTENSITY_THRESHOLD)
            {
                _damageTimer += dt;
                if (_damageTimer >= DAMAGE_INTERVAL)
                {
                    _damageTimer = 0f;
                    Program.DamagePlayer(DAMAGE_AMOUNT);
                }
            }
            else
            {
                _damageTimer = 0f;
            }
        }

        public static void Draw()
        {
            float absIntensity = MathF.Abs(_displayedIntensity);
            if (absIntensity <= 0.01f) return;

            Texture2D tex = _displayedIntensity < 0f ? _coldTexture : _hotTexture;
            if (tex.Id == 0) return;

            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            byte alpha = (byte)Math.Clamp(absIntensity * MAX_OVERLAY_ALPHA, 0f, 255f);

            Raylib.DrawTexturePro(
                tex,
                new Rectangle(0, 0, tex.Width, tex.Height),
                new Rectangle(0, 0, sw, sh),
                Vector2.Zero, 0f,
                new Color((byte)255, (byte)255, (byte)255, alpha)
            );
        }

        public static void DrawHUD()
        {
            float absIntensity = MathF.Abs(_displayedIntensity);
            if (absIntensity <= 0.01f) return;

            Texture2D tex = _displayedIntensity < 0f ? _coldTexture : _hotTexture;
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            int alpha = (int)Math.Clamp(absIntensity * MAX_OVERLAY_ALPHA, 0f, 255f);

            if (tex.Id != 0)
            {
                Raylib.DrawTexturePro(
                    tex,
                    new Rectangle(0, 0, tex.Width, tex.Height),
                    new Rectangle(0, 0, sw, sh),
                    Vector2.Zero,
                    0f,
                    new Color(255, 255, 255, alpha)
                );
            }
            else
            {
                Color borderColor = _displayedIntensity < 0f
                    ? new Color(140, 180, 255, alpha)
                    : new Color(255, 150, 80, alpha);

                int thickness = 6;
                Raylib.DrawRectangleLinesEx(new Rectangle(0, 0, sw, sh), thickness, borderColor);
            }
        }

        public static void Reset()
        {
            _rawIntensity = 0f;
            _displayedIntensity = 0f;
            _damageTimer = 0f;
            RawAmbientTemperature = 0f;
            CurrentBiome = Biome.Plains;
        }

        public static void ClearTextures()
        {
            if (_coldTexture.Id != 0) Raylib.UnloadTexture(_coldTexture);
            if (_hotTexture.Id != 0) Raylib.UnloadTexture(_hotTexture);
            _texturesLoaded = false;
        }
    }
}