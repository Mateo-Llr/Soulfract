// World_StoryCamp.cs
#nullable enable
using System.Linq;
using System.Numerics;

namespace Soulfract
{
    /// <summary>
    /// Campement du Veilleur : un vieux sentier à moitié effacé part du socle d'âmes et mène à un
    /// petit camp (tente, feu de camp, caisses) où attend un PNJ qui explique la situation au joueur.
    /// Généré une seule fois, à la création d'un nouveau monde (voir Program.StartNewGame).
    /// </summary>
    public static partial class World
    {
        private const int STORY_PATH_TILE_ID = 61;      // "Path"
        private const int STORY_CAMP_GROUND_ID = 26;    // "Dirt" : sol tassé autour du feu
        private const int STORY_TENT_ID = 6001;         // "Tent" (3x4)
        private const int STORY_CAMPFIRE_ID = 32;       // "Campfire" (lumineux)
        private const int STORY_CRATE_ID = 108;         // "Wooden Crate"
        private const int STORY_BARREL_ID = 107;        // "Wooden Barrel"
        private const int STORY_BOX_ID = 115;           // "Wooden Box"

        private const int STORY_PATH_START_DISTANCE = 5;  // tuiles entre le centre du socle et le début du sentier
        private const int STORY_CAMP_AXIS_DISTANCE = 16;  // distance du feu au socle (directions cardinales)
        private const int STORY_CAMP_DIAG_DISTANCE = 11;  // idem en diagonale (≈ 15,5 tuiles)

        /// <summary>Nom du PNJ du campement (voir Entity.StoryGuideName).</summary>
        public static string StoryGuideNpcName => Entity.StoryGuideName;

        private static bool IsStoryCampTileUsable(int x, int y)
        {
            EnsureTileLoaded(x, y);
            var ground = WorldTileRegistry.GetTile(GetGroundTileIdAt(x, y));
            if (ground == null || !ground.Diggable || !ground.Walkable) return false;
            return !_villageClaimedTiles.Contains((x, y));
        }

        private static bool IsStoryCampSiteValid(int cx, int cy)
        {
            // Zone du camp : 13 x 13 tuiles autour du feu, sol praticable partout (ni eau, ni roche).
            for (int x = cx - 6; x <= cx + 6; x++)
                for (int y = cy - 6; y <= cy + 6; y++)
                    if (!IsStoryCampTileUsable(x, y))
                        return false;
            return true;
        }

        /// <summary>Remplit un conteneur du camp avec quelques objets ("clé", min, max) ; clés inconnues ignorées.</summary>
        private static void StockStoryContainer(int x, int y, int objectId, params (string key, int min, int max)[] items)
        {
            var chunk = GetChunkAt(x, y);
            var tileData = WorldTileRegistry.GetTile(objectId);
            if (chunk == null || tileData == null) return;

            var container = new ContainerInventoryData(
                tileData.ContainerSlots ?? 20,
                tileData.ContainerColumns > 0 ? tileData.ContainerColumns : 8);

            var rand = new Random(Program.WorldSeed ^ (x * 7919) ^ (y * 104729));
            int slotIndex = 0;
            foreach (var (key, min, max) in items)
            {
                if (slotIndex >= container.Slots.Count) break;
                if (!GameData.TryGetItemByKey(key, out var itemData)) continue;
                int count = rand.Next(min, max + 1);
                container.Slots[slotIndex].Item = new Item(itemData.Name, count, itemData.Color, itemData.Icon);
                container.Slots[slotIndex].Count = count;
                slotIndex++;
            }
            chunk.SetContainerAt(x, y, container);
        }

        private static void ClearStoryBox(int x0, int y0, int w, int h, int keepObjectId)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                for (int y = y0; y < y0 + h; y++)
                {
                    EnsureTileLoaded(x, y);
                    int objectId = GetObjectIdAt(x, y);
                    if (objectId != 0 && objectId != keepObjectId)
                        RemovePlacedObject(x, y);
                }
            }
        }

        /// <summary>
        /// Trace un sentier irrégulier de (sx,sy) à (ex,ey) : léger zigzag, largeur variable et
        /// tuiles volontairement manquantes pour donner l'impression d'un vieux chemin.
        /// Retourne (tuiles du sentier réellement peintes, toutes les tuiles de la trajectoire).
        /// </summary>
        private static (List<(int x, int y)> painted, List<(int x, int y)> corridor) BuildStoryPath(int sx, int sy, int ex, int ey, Random rand)
        {
            var painted = new List<(int x, int y)>();
            var corridor = new List<(int x, int y)>();
            int steps = Math.Max(Math.Abs(ex - sx), Math.Abs(ey - sy));
            if (steps <= 0) return (painted, corridor);

            float dx = (ex - sx) / (float)steps;
            float dy = (ey - sy) / (float)steps;
            // Perpendiculaire (longueur ≈ 1 en axe, ≈ 1,4 en diagonale) pour décaler le sentier.
            float px = -dy;
            float py = dx;
            int lateral = 0;

            for (int i = 0; i <= steps; i++)
            {
                bool nearEnds = i < 3 || i > steps - 3;
                if (!nearEnds && rand.NextDouble() < 0.28)
                    lateral = Math.Clamp(lateral + (rand.Next(2) == 0 ? -1 : 1), -2, 2);
                int offset = nearEnds ? 0 : lateral;

                int x = (int)MathF.Round(sx + dx * i + px * offset);
                int y = (int)MathF.Round(sy + dy * i + py * offset);
                corridor.Add((x, y));

                // Quelques tuiles manquantes (jamais aux extrémités) : le sentier est vieux.
                bool skip = !nearEnds && rand.NextDouble() < 0.22;
                if (!skip)
                    painted.Add((x, y));

                // Parfois plus large : une deuxième tuile à côté (plus fréquent près du camp).
                double widenChance = i > steps - 4 ? 0.8 : 0.3;
                if (rand.NextDouble() < widenChance)
                {
                    int wx = (int)MathF.Round(x + px);
                    int wy = (int)MathF.Round(y + py);
                    if (rand.NextDouble() < 0.8)
                        painted.Add((wx, wy));
                    corridor.Add((wx, wy));
                }
            }
            return (painted, corridor);
        }

        /// <summary>
        /// Construit le campement du Veilleur et son sentier à partir du socle d'âmes. Ajoute le PNJ
        /// au chunk et à la liste globale des entités. Retourne false si aucun emplacement valide n'a
        /// été trouvé (le monde reste alors simplement sans campement).
        /// </summary>
        public static bool TryBuildStoryCamp(int spawnTileX, int spawnTileY, int keepObjectId)
        {
            if (_isUnderground) return false;

            var rand = new Random(Program.WorldSeed ^ 0x51C0A11);

            // 8 directions possibles, tirées dans un ordre propre à la graine.
            var directions = new List<(int dx, int dy)>
            {
                (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)
            };
            for (int i = directions.Count - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                (directions[i], directions[j]) = (directions[j], directions[i]);
            }

            foreach (var (ddx, ddy) in directions)
            {
                bool diagonal = ddx != 0 && ddy != 0;
                int dist = diagonal ? STORY_CAMP_DIAG_DISTANCE : STORY_CAMP_AXIS_DISTANCE;
                int cx = spawnTileX + ddx * dist;
                int cy = spawnTileY + ddy * dist;

                if (!IsStoryCampSiteValid(cx, cy)) continue;

                // --- Sentier : du bord du socle jusqu'à la lisière du camp (3 tuiles avant le feu) ---
                int startX = spawnTileX + ddx * STORY_PATH_START_DISTANCE;
                int startY = spawnTileY + ddy * STORY_PATH_START_DISTANCE;
                int endX = cx - ddx * 3;
                int endY = cy - ddy * 3;
                var (painted, corridor) = BuildStoryPath(startX, startY, endX, endY, rand);

                // Dégage les arbres/buissons qui barreraient le sentier (corridor + 1 tuile de marge).
                foreach (var (tx, ty) in corridor)
                    ClearStoryBox(tx - 1, ty - 1, 3, 3, keepObjectId);

                // Dégage tout le camp (13 x 13) avant de poser les objets.
                ClearStoryBox(cx - 6, cy - 6, 13, 13, keepObjectId);

                // Sol du sentier : uniquement sur terrain praticable (jamais dans l'eau).
                int paintedCount = 0;
                foreach (var (tx, ty) in painted)
                {
                    if (!IsStoryCampTileUsable(tx, ty)) continue;
                    SetGroundTile(tx, ty, STORY_PATH_TILE_ID);
                    paintedCount++;
                }

                // Sol tassé autour du feu (rayon 2, avec quelques trous).
                for (int x = cx - 2; x <= cx + 2; x++)
                {
                    for (int y = cy - 2; y <= cy + 2; y++)
                    {
                        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > 5) continue;
                        if (rand.NextDouble() < 0.25) continue;
                        SetGroundTile(x, y, STORY_CAMP_GROUND_ID);
                    }
                }

                // --- Disposition du camp (adaptée au côté d'où arrive le sentier) ---
                int toSpawnX = Math.Sign(spawnTileX - cx);
                int toSpawnY = Math.Sign(spawnTileY - cy);
                int tentSideY = toSpawnY > 0 ? -1 : (toSpawnY < 0 ? 1 : -1);  // la tente est du côté opposé au sentier
                int crateSideX = toSpawnX > 0 ? -1 : 1;                        // les caisses aussi

                // Feu de camp au centre.
                AddPlacedObject(cx, cy, STORY_CAMPFIRE_ID);

                // Tente (3x4, ancrée en bas à gauche : voir la pose du socle dans StartNewGame), à
                // deux tuiles du feu côté opposé au sentier : rangées cy-6..cy-3 ou cy+3..cy+6.
                int tentBottomY = tentSideY < 0 ? cy - 3 : cy + 6;
                AddPlacedObject(cx - 1, tentBottomY, STORY_TENT_ID);

                // Caisses, tonneau et boîte regroupés sur le côté.
                var crateA = (x: cx + crateSideX * 3, y: cy);
                var crateB = (x: cx + crateSideX * 4, y: cy);
                var barrel = (x: cx + crateSideX * 3, y: cy + tentSideY);
                var box = (x: cx + crateSideX * 4, y: cy + tentSideY * 2);
                AddPlacedObject(crateA.x, crateA.y, STORY_CRATE_ID);
                AddPlacedObject(crateB.x, crateB.y, STORY_CRATE_ID);
                AddPlacedObject(barrel.x, barrel.y, STORY_BARREL_ID);
                AddPlacedObject(box.x, box.y, STORY_BOX_ID);

                // Quelques provisions pour que les caisses ne soient pas que décoratives.
                StockStoryContainer(crateA.x, crateA.y, STORY_CRATE_ID, ("apple", 3, 5), ("potato", 2, 4));
                StockStoryContainer(crateB.x, crateB.y, STORY_CRATE_ID, ("stick", 4, 8), ("plank", 2, 3));
                StockStoryContainer(box.x, box.y, STORY_BOX_ID, ("rock", 3, 6));

                // --- Le Veilleur, près du feu, côté sentier : il voit arriver le joueur ---
                int npcX = cx + (toSpawnX != 0 ? toSpawnX * 2 : 0);
                int npcY = cy + (toSpawnY != 0 ? toSpawnY * 2 : 2);
                float ts = Program.TileSize;
                var guide = new Entity(new Vector2((npcX + 0.5f) * ts, (npcY + 0.5f) * ts), "human", false, skipHomeAutoAssign: true);
                guide.AssignProfessionAndTrade(ProfessionType.None, allowGuard: false); // pas un marchand
                guide.FirstName = Entity.StoryGuideName;
                guide.CustomName = Entity.StoryGuideName;
                // Il dort dans sa tente : même système que les habitants des tentes de grotte
                // (HomeBuildingId = tente) ; il rentre à la nuit tombée devant la tente puis s'y couche.
                guide.HomeBuildingId = Entity.TentHomeBuildingId;
                guide.HomePosition = new Vector2((cx + 0.5f) * ts, (cy + tentSideY * 2 + 0.5f) * ts);
                var tentData = WorldTileRegistry.GetTile(STORY_TENT_ID)
                    ?? throw new InvalidOperationException($"Données manquantes pour la tente du camp (ID {STORY_TENT_ID}).");
                guide.TentPosition = Program.GetFurnitureAnchorPosition(cx - 1, tentBottomY, tentData);
                guide.AiTarget = guide.WorldPos;
                guide.AiState = NpcAiState.Idle;
                guide.Facing = toSpawnX != 0 ? toSpawnX : 1f;

                GetChunkAt(npcX, npcY)?.Entities.Add(guide);
                var globalEntities = Program.GetEntities();
                if (!globalEntities.Any(e => e.NetId == guide.NetId))
                    globalEntities.Add(guide);

                Console.WriteLine($"[StoryCamp] Campement du Veilleur en ({cx},{cy}), direction ({ddx},{ddy}), {paintedCount} tuile(s) de sentier.");
                return true;
            }

            Console.WriteLine("[StoryCamp] Aucun emplacement valide pour le campement du Veilleur : monde généré sans campement.");
            return false;
        }
    }
}
