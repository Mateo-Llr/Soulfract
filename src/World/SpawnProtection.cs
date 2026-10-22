// World_SpawnProtection.cs
#nullable enable

namespace Soulfract
{
    /// <summary>
    /// Zone de protection autour du socle d'âmes (point d'apparition du monde).
    /// Empêche la génération (même tardive, chunk par chunk) d'arbres, de maisons, de champs,
    /// de totems ou de zones de travail à proximité immédiate du socle, et permet de choisir un
    /// point de départ éloigné de tout village.
    /// </summary>
    public static partial class World
    {
        /// <summary>Rayon (en tuiles) autour du socle où aucun objet naturel (arbre, rocher...) n'est autorisé.</summary>
        public const int SPAWN_CLEAR_RADIUS = 8;

        /// <summary>Rayon (en tuiles) autour du socle où aucune structure de village (maison, champ, totem...) n'est autorisée.</summary>
        public const int SPAWN_STRUCTURE_BUFFER = 24;

        /// <summary>Distance minimale (en tuiles) entre le socle et le centre (puits) d'un village.</summary>
        public const int SPAWN_VILLAGE_MIN_DISTANCE = 70;

        private static bool _hasSpawnProtection;
        private static int _spawnProtectionX;
        private static int _spawnProtectionY;

        public static void SetSpawnProtection(int tileX, int tileY)
        {
            _hasSpawnProtection = true;
            _spawnProtectionX = tileX;
            _spawnProtectionY = tileY;
        }

        public static void ClearSpawnProtection()
        {
            _hasSpawnProtection = false;
        }

        /// <summary>True si la tuile est à moins de <paramref name="margin"/> tuiles (distance de Chebyshev) du socle.</summary>
        public static bool IsSpawnProtected(int x, int y, int margin)
        {
            if (!_hasSpawnProtection || _isUnderground) return false;
            return Math.Abs(x - _spawnProtectionX) <= margin && Math.Abs(y - _spawnProtectionY) <= margin;
        }

        /// <summary>True si le rectangle [x, x+w-1] × [y, y+h-1] touche la zone protégée élargie de <paramref name="margin"/>.</summary>
        public static bool IsSpawnProtectedRect(int x, int y, int w, int h, int margin)
        {
            if (!_hasSpawnProtection || _isUnderground) return false;
            return x <= _spawnProtectionX + margin && x + w - 1 >= _spawnProtectionX - margin
                && y <= _spawnProtectionY + margin && y + h - 1 >= _spawnProtectionY - margin;
        }

        private static int FloorDivTiles(int value, int divisor)
            => (int)Math.Floor(value / (double)divisor);

        /// <summary>
        /// Reproduit exactement le test de déclenchement de village de GenerateNewChunk
        /// (chunk multiple de 5, 18 % de chance, centre en plaine) SANS rien générer.
        /// </summary>
        private static bool WouldVillageGenerateInChunk(int chunkX, int chunkY)
        {
            if (chunkX % 5 != 0 || chunkY % 5 != 0) return false;
            int hash = Program.WorldSeed ^ (chunkX * 73856093) ^ (chunkY * 19349663);
            var villageRand = new Random(hash);
            if (villageRand.NextDouble() >= 0.18f) return false;
            int centralX = chunkX * CHUNK_SIZE + CHUNK_SIZE / 2;
            int centralY = chunkY * CHUNK_SIZE + CHUNK_SIZE / 2;
            return GetBiomeAt(centralX, centralY) == Biome.Plains;
        }

        /// <summary>True si un village (déjà généré OU qui sera généré) a son centre à moins de minDistance tuiles.</summary>
        public static bool IsNearVillageCenter(int tileX, int tileY, int minDistance)
        {
            int minChunkX = FloorDivTiles(tileX - minDistance, CHUNK_SIZE);
            int maxChunkX = FloorDivTiles(tileX + minDistance, CHUNK_SIZE);
            int minChunkY = FloorDivTiles(tileY - minDistance, CHUNK_SIZE);
            int maxChunkY = FloorDivTiles(tileY + minDistance, CHUNK_SIZE);

            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                if (cx % 5 != 0) continue;
                for (int cy = minChunkY; cy <= maxChunkY; cy++)
                {
                    if (cy % 5 != 0) continue;
                    int centerX = cx * CHUNK_SIZE + CHUNK_SIZE / 2;
                    int centerY = cy * CHUNK_SIZE + CHUNK_SIZE / 2;
                    if (Math.Abs(centerX - tileX) > minDistance || Math.Abs(centerY - tileY) > minDistance) continue;
                    if (WouldVillageGenerateInChunk(cx, cy)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Pré-filtre PUREMENT basé sur le bruit (aucun chunk généré) : toute la zone autour du point
        /// est en plaine et aucun village n'est prévu à proximité.
        /// </summary>
        public static bool IsSpawnAreaNaturallySuitable(int tileX, int tileY)
        {
            int r = SPAWN_STRUCTURE_BUFFER / 2;
            for (int dx = -r; dx <= r; dx += 2)
                for (int dy = -r; dy <= r; dy += 2)
                    if (GetBiomeAt(tileX + dx, tileY + dy) != Biome.Plains)
                        return false;

            return !IsNearVillageCenter(tileX, tileY, SPAWN_VILLAGE_MIN_DISTANCE);
        }

        /// <summary>
        /// Vérification finale, À FAIRE APRÈS génération des chunks autour du point : le sol est
        /// praticable (ni eau ni roche) et aucune maison/champ de village n'empiète sur la zone tampon.
        /// </summary>
        public static bool IsSpawnAreaVerified(int tileX, int tileY)
        {
            int r = SPAWN_CLEAR_RADIUS + 2;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    var ground = WorldTileRegistry.GetTile(GetGroundTileIdAt(tileX + dx, tileY + dy));
                    if (ground == null || !ground.Diggable)
                        return false;
                }
            }

            int b = SPAWN_STRUCTURE_BUFFER;
            for (int dx = -b; dx <= b; dx++)
                for (int dy = -b; dy <= b; dy++)
                    if (_villageClaimedTiles.Contains((tileX + dx, tileY + dy)))
                        return false;

            return true;
        }

        /// <summary>
        /// Retire tout objet posé (arbres, buissons, rochers...) dans le rayon donné autour du point,
        /// sauf l'objet <paramref name="keepObjectId"/> (le socle lui-même). Retourne le nombre d'objets retirés.
        /// </summary>
        public static int ClearSpawnArea(int tileX, int tileY, int radius, int keepObjectId)
        {
            int removed = 0;
            for (int x = tileX - radius; x <= tileX + radius; x++)
            {
                for (int y = tileY - radius; y <= tileY + radius; y++)
                {
                    int objectId = GetObjectIdAt(x, y);
                    if (objectId == 0 || objectId == keepObjectId) continue;
                    RemovePlacedObject(x, y);
                    removed++;
                }
            }
            return removed;
        }
    }
}
