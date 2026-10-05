// PerlinNoise.cs - Version avec seed
namespace Soulfract
{
    public static class PerlinNoise
    {
        private static int[] _p = new int[512];
        private static int _seed = 0;

        public static void SetSeed(int seed)
        {
            _seed = seed;
            var random = new Random(seed);
            
            // Générer une permutation aléatoire basée sur la seed
            int[] permutation = new int[256];
            for (int i = 0; i < 256; i++)
                permutation[i] = i;
            
            // Mélanger avec Fisher-Yates
            for (int i = 255; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (permutation[i], permutation[j]) = (permutation[j], permutation[i]);
            }
            
            // Dupliquer pour éviter les débordements
            for (int i = 0; i < 256; i++)
                _p[256 + i] = _p[i] = permutation[i];
        }

        static PerlinNoise()
        {
            // Initialisation par défaut (seed 0)
            SetSeed(0);
        }

        // Le reste de la classe reste identique...
        public static float Noise(float x, float y)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;
            x -= (float)Math.Floor(x);
            y -= (float)Math.Floor(y);
            float u = Fade(x), v = Fade(y);
            int A = _p[X] + Y, AA = _p[A], AB = _p[A + 1], B = _p[X + 1] + Y, BA = _p[B], BB = _p[B + 1];
            return Lerp(v,
                Lerp(u, Grad(_p[AA], x, y),     Grad(_p[BA], x - 1, y)),
                Lerp(u, Grad(_p[AB], x, y - 1), Grad(_p[BB], x - 1, y - 1)));
        }

        /// <summary>
        /// Fractal Brownian Motion : superpose plusieurs octaves de bruit de Perlin
        /// pour obtenir un relief bien plus naturel (grandes formes + détails fins)
        /// qu'un simple bruit à fréquence unique. Retourne une valeur ~[-1, 1].
        /// </summary>
        public static float FBM(float x, float y, int octaves, float persistence, float lacunarity)
        {
            float total = 0f;
            float frequency = 1f;
            float amplitude = 1f;
            float maxValue = 0f;

            for (int i = 0; i < octaves; i++)
            {
                total += Noise(x * frequency, y * frequency) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return maxValue > 0f ? total / maxValue : 0f;
        }

        /// <summary>
        /// Bruit "ridged" (crêtes) : chaque octave est repliée autour de 0 puis inversée,
        /// ce qui crée des lignes de crête nettes au lieu de bosses arrondies. Idéal pour
        /// des chaînes de montagnes qui se ramifient de façon crédible. Retourne ~[0, 1].
        /// </summary>
        public static float RidgedFBM(float x, float y, int octaves, float persistence, float lacunarity)
        {
            float total = 0f;
            float frequency = 1f;
            float amplitude = 1f;
            float maxValue = 0f;

            for (int i = 0; i < octaves; i++)
            {
                float n = Noise(x * frequency, y * frequency);
                n = 1f - Math.Abs(n);
                n *= n; // accentue les crêtes, adoucit les fonds de vallée
                total += n * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return maxValue > 0f ? total / maxValue : 0f;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        static float Lerp(float t, float a, float b) => a + t * (b - a);
        static float Grad(int hash, float x, float y)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : (h == 12 || h == 14 ? x : 0);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}