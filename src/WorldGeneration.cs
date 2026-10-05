// WorldGeneration.cs - Version corrigée avec cohérence climatique
using System.Numerics;

namespace Soulfract
{
    public enum Biome
    {
        // Biomes tempérés
        Plains,
        Forest,
        BirchForest,
        DarkForest,
        Swamp,
        Jungle,
        
        // Biomes secs
        Desert,
        Savanna,
        Badlands,
        Mesa,
        
        // Biomes froids
        Taiga,
        SnowyForest,
        Tundra,
        Snow,
        IceSpikes,
        
        // Biomes spéciaux
        Ocean,
        DeepOcean,
        Beach,
        Mountains,
        Peak,
        Steppe,
        Mangrove,
        Cliff
    }

    public static class WorldGeneration
    {
        // Échelles de bruit climatique
        private const float TEMP_NOISE_SCALE = 0.0012f;
        private const float HUMIDITY_NOISE_SCALE = 0.0012f;

        // ═══════════════════════════════════════════════════════════════════
        // Échelles du relief (approche "continentalness / erosion / peaks &
        // valleys" façon terrain moderne : chaque bruit a un rôle précis au
        // lieu d'empiler des octaves sans logique).
        // ═══════════════════════════════════════════════════════════════════

        // Distord légèrement les coordonnées avant l'échantillonnage pour casser
        // l'aspect "grille" et rendre les côtes/chaînes de montagnes organiques.
        private const float WARP_SCALE = 0.0006f;
        private const float WARP_STRENGTH = 90f;

        // Très basse fréquence : dessine la forme des continents/océans.
        private const float CONTINENT_SCALE = 0.00022f;
        private const int CONTINENT_OCTAVES = 3;

        // Fréquence moyenne : détermine si une zone est "érodée" (plate) ou
        // "accidentée" (propice aux montagnes), indépendamment du continent.
        private const float EROSION_SCALE = 0.0009f;
        private const int EROSION_OCTAVES = 2;

        // Bruit en crêtes : dessine les chaînes de montagnes elles-mêmes.
        private const float PEAKS_SCALE = 0.0035f;
        private const int PEAKS_OCTAVES = 3;

        // Détail fin (texture locale du terrain).
        private const float DETAIL_SCALE = 0.02f;

        // Seuils d'élévation (inchangés pour rester compatibles avec le reste du jeu)
        private const float DEEP_OCEAN_THRESHOLD = 0.15f;
        private const float OCEAN_THRESHOLD = 0.32f;
        private const float BEACH_THRESHOLD = 0.38f;
        private const float LOWLANDS_THRESHOLD = 0.48f;
        private const float HIGHLANDS_THRESHOLD = 0.70f;
        private const float MOUNTAIN_THRESHOLD = 0.85f;
        private const float PEAK_THRESHOLD = 0.94f;

        //  Seed partagée pour les bruits climatiques
        private static readonly int CLIMATE_SEED = 42;

        // ═══════════════════════════════════════════════════════════════════
        // Modèle de biomes : grille altitude x humidité.
        // La température N'EST PLUS un climat indépendant : elle découle de
        // l'altitude, exactement comme en montagne (plus haut = plus froid,
        // plus bas = plus chaud/tropical). L'humidité reste le second axe,
        // indépendant. C'est ce qui fait qu'un point très bas et très humide
        // est, à raison, un océan/une mangrove tropicale : altitude basse ->
        // chaud, humidité haute -> tropical/marin. Les cas extrêmes
        // (très haut, très bas) sortent de la grille "tempérée" et donnent
        // les biomes spéciaux (pics enneigés, océans...), comme les
        // extrémités du tableau 4x4 décrit.
        //
        // On garde tout de même une composante de bruit LOCALE (poids modéré)
        // par-dessus le biais d'altitude : sans ça, deux points de même
        // altitude auraient TOUJOURS exactement la même température, ce qui
        // interdit toute exception organique (ex: un haut-plateau anormalement
        // chaud et sec, comme dans le monde réel). Le biais d'altitude reste
        // toutefois dominant, pour que la règle "haut = froid" soit
        // clairement lisible en jouant.
        // ═══════════════════════════════════════════════════════════════════
        private const float ELEVATION_TEMP_STRENGTH = 0.85f;
        private const float TEMP_WARP_SCALE = 0.0008f;
        private const float TEMP_WARP_STRENGTH = 120f;

        /// <summary>
        /// Version pratique qui recalcule l'élévation. Préférer l'overload avec
        /// élévation déjà connue (voir World.GetClimateAt) pour éviter de
        /// recalculer un bruit coûteux deux fois.
        /// </summary>
        public static float GetTemperature(float worldX, float worldY)
        {
            float elevation = GetElevation(worldX, worldY);
            return GetTemperature(worldX, worldY, elevation);
        }

        public static float GetTemperature(float worldX, float worldY, float elevation)
        {
            // Biais dominant : l'altitude pilote la température. elevation=0
            // (fond océanique) -> le plus chaud (+1). elevation=1 (sommet) ->
            // le plus froid (-1). Point milieu (~0.5, autour des plaines) -> ~0.
            float elevationBias = (1f - elevation * 2f) * ELEVATION_TEMP_STRENGTH;

            // Domain warp dédié pour casser l'aspect "grille" de la variation
            // locale (seed différente du warp d'élévation pour ne pas être
            // corrélés de façon trop visible).
            float warpX = PerlinNoise.Noise(worldX * TEMP_WARP_SCALE + 4000f, worldY * TEMP_WARP_SCALE + 4000f) * TEMP_WARP_STRENGTH;
            float warpY = PerlinNoise.Noise(worldX * TEMP_WARP_SCALE + 6000f, worldY * TEMP_WARP_SCALE + 6000f) * TEMP_WARP_STRENGTH;
            float wx = worldX + warpX;
            float wy = worldY + warpY;

            // Variation locale autour du biais d'altitude (poches un peu plus
            // chaudes ou froides que ce que l'altitude seule indiquerait).
            float tempNoise = PerlinNoise.FBM(
                (wx * TEMP_NOISE_SCALE) + CLIMATE_SEED,
                (wy * TEMP_NOISE_SCALE) + CLIMATE_SEED * 2,
                4, 0.5f, 2f
            ) * 0.30f;

            float tempNoise2 = PerlinNoise.FBM(
                (worldX * 0.0025f) + CLIMATE_SEED * 3,
                (worldY * 0.0025f) + CLIMATE_SEED * 4,
                2, 0.5f, 2f
            ) * 0.15f;

            float result = elevationBias + tempNoise + tempNoise2;
            result = Math.Clamp(result, -1f, 1f);

            // Équalisation : repousse légèrement vers les extrêmes pour éviter
            // que le bruit local n'écrase trop le biais d'altitude au centre
            // de la distribution (voir EqualizeSigned plus bas).
            result = EqualizeSigned(result, TEMPERATURE_EQUALIZE_POWER);

            return Math.Clamp(result, -1f, 1f);
        }

        private const float TEMPERATURE_EQUALIZE_POWER = 0.85f;
        private const float HUMIDITY_EQUALIZE_POWER = 0.75f;

        /// <summary>
        /// Repousse une valeur signée [-1, 1] vers ses extrêmes (power &lt; 1)
        /// pour compenser l'accumulation près de 0 typique des sommes de bruit
        /// multi-octaves, et obtenir une distribution plus proche de l'uniforme.
        /// </summary>
        private static float EqualizeSigned(float x, float power)
        {
            float sign = Math.Sign(x);
            return sign * MathF.Pow(Math.Abs(x), power);
        }

        public static float GetHumidity(float worldX, float worldY)
        {
            float elevation = GetElevation(worldX, worldY);
            float temperature = GetTemperature(worldX, worldY, elevation);
            return GetHumidity(worldX, worldY, temperature, elevation);
        }

        /// <summary>
        /// Variante de GetHumidity qui accepte une température et une élévation déjà
        /// calculées par l'appelant. Évite de recalculer deux fois le même bruit coûteux
        /// (élévation = warp + FBM continent + FBM erosion + FBM ridged + détail) quand on
        /// a déjà besoin de la température/élévation à côté — voir World.GetClimateAt.
        /// </summary>
        public static float GetHumidity(float worldX, float worldY, float temperature, float elevation)
        {
            // Bruit d'humidité de base avec seed cohérent
            float n1 = PerlinNoise.Noise(
                (worldX * HUMIDITY_NOISE_SCALE) + CLIMATE_SEED * 5,
                (worldY * HUMIDITY_NOISE_SCALE) + CLIMATE_SEED * 6
            );
            float n2 = PerlinNoise.Noise(
                (worldX * 0.003f) + CLIMATE_SEED * 7,
                (worldY * 0.003f) + CLIMATE_SEED * 8
            );

            float combined = n1 * 0.7f + n2 * 0.3f;
            float humidity = (combined + 1f) / 2f;

            //  Les zones chaudes reçoivent plus de précipitations possibles
            // (plus d'évaporation -> plus d'humidité potentielle), donc le
            // plafond d'humidité monte avec la température au lieu d'être fixe.
            float tempCorrection = (temperature + 1f) / 2f;  // 0-1
            float maxHumidity = 0.7f + tempCorrection * 0.3f;
            humidity = Math.Min(humidity, maxHumidity);

            //  Correction par altitude (les montagnes bloquent les précipitations)
            float rainShadow = 1f - Math.Max(0, elevation - 0.5f) * 0.6f;
            humidity *= rainShadow;
            
            //  Ajouter un peu de bruit pour les variations locales
            float localVariation = PerlinNoise.Noise(
                (worldX * 0.008f) + CLIMATE_SEED * 9,
                (worldY * 0.008f) + CLIMATE_SEED * 10
            ) * 0.08f;
            humidity += localVariation;
            humidity = Math.Clamp(humidity, 0f, 1f);

            // Même équalisation que pour la température : sans ça, l'humidité
            // reste collée autour de 0.5 et les biomes très secs (désert,
            // badlands) ou très humides (mangrove, marais) n'ont presque pas
            // de place pour apparaître.
            float humiditySigned = (humidity - 0.5f) * 2f;
            humiditySigned = EqualizeSigned(humiditySigned, HUMIDITY_EQUALIZE_POWER);
            humidity = humiditySigned / 2f + 0.5f;

            return Math.Clamp(humidity, 0.05f, 0.98f);
        }

        public static float GetElevation(float worldX, float worldY)
        {
            //  1. Domain warping : on décale légèrement les coordonnées d'échantillonnage
            // avec un bruit à part. Ça évite les côtes/montagnes qui suivent visiblement
            // les axes X/Y et donne des formes bien plus organiques.
            float warpX = PerlinNoise.Noise(worldX * WARP_SCALE, worldY * WARP_SCALE) * WARP_STRENGTH;
            float warpY = PerlinNoise.Noise(worldX * WARP_SCALE + 500f, worldY * WARP_SCALE + 500f) * WARP_STRENGTH;
            float wx = worldX + warpX;
            float wy = worldY + warpY;

            //  2. Continentalness : bruit très basse fréquence qui dessine où sont les
            // continents, océans, et grandes plaines. C'est le squelette du terrain.
            float continent = PerlinNoise.FBM(wx * CONTINENT_SCALE, wy * CONTINENT_SCALE, CONTINENT_OCTAVES, 0.5f, 2f);
            continent = (continent + 1f) / 2f; // 0..1
            float baseElevation = ContinentCurve(continent);

            //  3. Erosion : indépendant du continent, définit si une région est plutôt
            // "plate" (érodée) ou "accidentée" (propice aux reliefs marqués).
            float erosion = PerlinNoise.FBM(worldX * EROSION_SCALE + 1000f, worldY * EROSION_SCALE + 1000f, EROSION_OCTAVES, 0.5f, 2f);
            erosion = (erosion + 1f) / 2f; // 0..1

            //  4. Peaks & Valleys : bruit en crêtes qui forme de vraies chaînes de
            // montagnes ramifiées plutôt que des bosses isolées.
            float ridged = PerlinNoise.RidgedFBM(worldX * PEAKS_SCALE, worldY * PEAKS_SCALE, PEAKS_OCTAVES, 0.5f, 2f);

            // Les montagnes n'apparaissent que sur la terre ferme (continent élevé)
            // et dans les zones peu érodées ; les côtes et plaines restent douces.
            float landMask = Math.Clamp((baseElevation - 0.42f) * 2.4f, 0f, 1f);
            float ruggedMask = Math.Clamp((erosion - 0.35f) * 1.6f, 0f, 1f);
            float mountainMask = landMask * ruggedMask;
            float mountainContribution = ridged * mountainMask * 0.6f;

            //  5. Détail fin : petite variation locale pour éviter les zones trop lisses.
            float detail = PerlinNoise.FBM(worldX * DETAIL_SCALE + 300f, worldY * DETAIL_SCALE + 300f, 2, 0.5f, 2f) * 0.035f;

            float elevation = baseElevation + mountainContribution + detail;

            return Math.Clamp(elevation, 0f, 1f);
        }

        /// <summary>
        /// Transforme la valeur brute de "continentalness" (0..1) en élévation de base,
        /// via une courbe par segments façon spline : océans profonds, remontée douce
        /// vers les côtes, grand plateau de plaines, puis montée vers les hauts-plateaux.
        /// Les vraies montagnes sont ensuite ajoutées par-dessus dans GetElevation.
        /// </summary>
        private static float ContinentCurve(float c)
        {
            if (c < 0.28f)
                return Lerp01(c, 0f, 0.28f, 0.00f, 0.14f);          // océans profonds
            if (c < 0.42f)
                return Lerp01(c, 0.28f, 0.42f, 0.14f, 0.32f);       // remontée vers les côtes
            if (c < 0.46f)
                return Lerp01(c, 0.42f, 0.46f, 0.32f, 0.40f);       // plage / littoral
            if (c < 0.62f)
                return Lerp01(c, 0.46f, 0.62f, 0.40f, 0.55f);       // grandes plaines (plateau doux)
            if (c < 0.80f)
                return Lerp01(c, 0.62f, 0.80f, 0.55f, 0.76f);       // hauts-plateaux
            return Lerp01(c, 0.80f, 1.00f, 0.76f, 0.90f);           // avant-monts (les pics viennent du bruit ridged)
        }

        private static float Lerp01(float value, float inMin, float inMax, float outMin, float outMax)
        {
            float t = (value - inMin) / (inMax - inMin);
            return outMin + t * (outMax - outMin);
        }

        public static Biome GetBiome(float temperature, float humidity, float elevation)
        {
            // 1. Océans et zones très basses : très majoritairement aquatiques.
            if (elevation < DEEP_OCEAN_THRESHOLD)
                return Biome.DeepOcean;
            if (elevation < OCEAN_THRESHOLD)
                return Biome.Ocean;
            if (elevation < BEACH_THRESHOLD)
            {
                if (temperature > 0.2f && humidity < 0.25f)
                    return Biome.Desert;
                if (temperature > -0.05f && humidity > 0.65f)
                    return Biome.Mangrove;
                return Biome.Beach;
            }

            // 2. Très hautes altitudes : sommets enneigés ou rochers d'altitude.
            if (elevation > PEAK_THRESHOLD)
            {
                if (temperature < 0.1f)
                    return Biome.Snow;
                return Biome.Peak;
            }
            if (elevation > MOUNTAIN_THRESHOLD)
                return Biome.Mountains;

            // 3. Hauts plateaux : zones moins fréquentes et plus rocheuses.
            if (elevation > HIGHLANDS_THRESHOLD)
                return GetHighlandBiome(temperature, humidity);

            // 4. Plaine / basse altitude : biomes tempérés plus fréquents.
            return GetLowlandBiome(temperature, humidity);
        }

        private static Biome GetLowlandBiome(float temperature, float humidity)
        {
            // Bandes rééquilibrées : avec l'équalisation ci-dessus, temperature
            // et humidity sont maintenant réparties de façon plus homogène sur
            // leur plage, donc des bandes de largeur comparable donnent des
            // biomes de surface comparable au lieu de laisser le désert (et
            // consorts) coincés dans une queue de distribution minuscule.
            if (temperature > 0.25f)
            {
                if (humidity < 0.25f) return Biome.Desert;
                if (humidity < 0.50f) return Biome.Savanna;
                if (humidity < 0.80f) return Biome.Jungle;
                return Biome.Mangrove;
            }

            if (temperature > -0.1f)
            {
                if (humidity < 0.20f) return Biome.Steppe;
                if (humidity < 0.45f) return Biome.Plains;
                if (humidity < 0.65f) return Biome.Forest;
                if (humidity < 0.82f) return Biome.BirchForest;
                if (humidity < 0.93f) return Biome.DarkForest;
                return Biome.Swamp;
            }

            if (temperature > -0.35f)
            {
                if (humidity < 0.20f) return Biome.Tundra;
                if (humidity < 0.40f) return Biome.Steppe;
                if (humidity < 0.65f) return Biome.Taiga;
                if (humidity < 0.85f) return Biome.SnowyForest;
                return Biome.Snow;
            }

            if (humidity < 0.30f) return Biome.Tundra;
            if (humidity < 0.60f) return Biome.Snow;
            return Biome.IceSpikes;
        }

        private static Biome GetHighlandBiome(float temperature, float humidity)
        {
            if (temperature > 0.25f)
            {
                if (humidity < 0.30f) return Biome.Badlands;
                if (humidity < 0.50f) return Biome.Mesa;
                return Biome.Steppe;
            }

            if (temperature > -0.1f)
            {
                if (humidity < 0.25f) return Biome.Steppe;
                if (humidity < 0.50f) return Biome.Plains;
                if (humidity < 0.75f) return Biome.Forest;
                return Biome.SnowyForest;
            }

            if (temperature > -0.35f)
            {
                if (humidity < 0.30f) return Biome.Tundra;
                if (humidity < 0.55f) return Biome.Steppe;
                if (humidity < 0.80f) return Biome.Taiga;
                return Biome.SnowyForest;
            }

            if (humidity < 0.45f) return Biome.Tundra;
            if (humidity < 0.80f) return Biome.Snow;
            return Biome.IceSpikes;
        }

        public static int GetGroundTileForBiome(Biome biome)
        {
            return biome switch
        {
                Biome.Ocean => 10,
                Biome.DeepOcean => 10,
                Biome.Beach => 62,
                Biome.Cliff => 26,
                Biome.Plains => 1,
                Biome.Forest => 44,
                Biome.BirchForest => 1,
                Biome.DarkForest => 44,
                Biome.Swamp => 53,
                Biome.Jungle => 44,
                Biome.Desert => 11,
                Biome.Savanna => 46,
                Biome.Badlands => 26,
                Biome.Mesa => 26,
                Biome.Taiga => 47,
                Biome.SnowyForest => 43,
                Biome.Tundra => 43,
                Biome.Snow => 43,
                Biome.IceSpikes => 43,
                Biome.Mountains => 26,
                Biome.Peak => 26,
                Biome.Steppe => 43,
                Biome.Mangrove => 11,
                _ => 1
            };
        }

        public static string GetBiomeDisplayName(Biome biome)
        {
            return biome switch
            {
                Biome.Plains => "Plaines",
                Biome.Forest => "Forêt",
                Biome.BirchForest => "Forêt de Bouleaux",
                Biome.DarkForest => "Forêt Sombre",
                Biome.Swamp => "Marais",
                Biome.Jungle => "Jungle",
                Biome.Desert => "Désert",
                Biome.Savanna => "Savane",
                Biome.Badlands => "Badlands",
                Biome.Mesa => "Mesa",
                Biome.Taiga => "Taïga",
                Biome.SnowyForest => "Forêt Enneigée",
                Biome.Tundra => "Toundra",
                Biome.Snow => "Neige",
                Biome.IceSpikes => "Pointes de Glace",
                Biome.Ocean => "Océan",
                Biome.DeepOcean => "Océan Profond",
                Biome.Beach => "Plage",
                Biome.Mountains => "Montagnes",
                Biome.Peak => "Sommet",
                Biome.Steppe => "Steppe",
                Biome.Mangrove => "Mangrove",
                Biome.Cliff => "Falaise",
                _ => "Inconnu"
            };
        }
        
        public static float GetWaterSurfaceHeight()
        {
            return OCEAN_THRESHOLD * World.MAX_HEIGHT_LEVELS;
        }
        
        public static bool IsUnderwater(int heightValue)
        {
            float waterLevel = GetWaterSurfaceHeight();
            return heightValue < waterLevel;
        }
    }
}