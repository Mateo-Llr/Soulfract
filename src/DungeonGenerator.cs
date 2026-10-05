// DungeonGenerator.cs — Donjons labyrinthes procéduraux, isolés du reste du monde.
//
// Chaque donjon vit dans sa propre zone de coordonnées réservée (voir
// World.AllocateDungeonInstance / EnsureDungeonRegionChunks), jamais partagée avec
// une grotte ou un autre donjon : rien de ce qui est peint ici ne peut donc jamais
// écraser un terrain préexistant.
//
// Structure : une grille de cellules est reliée par un labyrinthe parfait (recursive
// backtracker) + quelques boucles. Chaque cellule est soit un simple carrefour, soit une
// SALLE d'un certain TYPE (voir RoomType) qui détermine sa taille, sa forme, son sol, son
// mobilier, ses monstres et son butin :
//
//   Rest (départ) · Boss · Treasure · Guard · Barracks · Crypt · Library · Storage
//   Alchemy · Forge · Shrine · Mushroom · PillarHall · Fountain
//
// La salle de départ est une salle de repos sûre ; la salle la plus éloignée (au sens du
// labyrinthe) devient la salle du boss, avec la porte de sortie verrouillée jusqu'à sa mort.
//
// Toute l'aléatoire passe par le Random fourni (graine du donjon) : un donjon est donc
// toujours régénéré à l'identique.
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class DungeonGenerator
    {
        // ─── Sols ───
        private const int DUNGEON_FLOOR_ID = 3000;   // NE PAS remplacer dans la salle du boss (voir EnsureExitApproach)
        private const int DUNGEON_WALL_ID = 3001;
        private const int FLOOR_CAVE = 100;
        private const int FLOOR_DIRT = 26;
        private const int FLOOR_WOOD = 7;
        private const int FLOOR_CARPET = 60;
        private const int FLOOR_PAVING = 83;

        // ─── Objets (ids de worldobjects.json) ───
        private const int OBJ_TABLE = 6;             // 2x1
        private const int OBJ_WORKBENCH = 9;
        private const int OBJ_CAMPFIRE = 32;
        private const int OBJ_TORCH = 33;
        private const int OBJ_CHEST = 42;
        private const int OBJ_ANVIL = 40;            // 2x1
        private const int OBJ_FURNACE = 25;          // 2x1
        private const int OBJ_CANDLE = 70;           // traversable
        private const int OBJ_SHELF = 81;            // conteneur
        private const int OBJ_POT = 86;
        private const int OBJ_TIKI = 88;
        private const int OBJ_ALCHEMY_TABLE = 89;
        private const int OBJ_FLOOR_LAMP = 93;       // traversable, lumineux
        private const int OBJ_CHAIR = 95;
        private const int OBJ_COFFEE_TABLE = 96;
        private const int OBJ_CAULDRON = 98;
        private const int OBJ_PYLON = 105;
        private const int OBJ_BARREL = 107;          // conteneur
        private const int OBJ_CRATE = 108;           // conteneur
        private const int OBJ_BANNER = 7000;
        private const int OBJ_BENCH = 114;           // 2x1
        private const int OBJ_BOX = 115;             // conteneur
        private const int OBJ_WELL = 136;            // 3x1
        private const int OBJ_CLOCK = 122;
        private const int OBJ_ARMOR_STAND = 300;
        private const int OBJ_ROCK_BLOCK = 23;
        private const int OBJ_STALAGMITE = 201;
        private const int OBJ_GLOW_MUSHROOM = 207;
        private const int OBJ_GIANT_MUSHROOM = 208;  // 2x3
        private const int OBJ_MUSHROOM = 15;         // ramassable, traversable

        // ─── Paramètres de la grille de cellules ───
        // GRID_SIZE * CELL_SIZE = 182 tuiles (≤ 192, l'ancienne taille de la zone réservée).
        private const int GRID_SIZE = 7;
        private const int CELL_SIZE = 26;
        private const int ROOM_MARGIN = 3;
        private const float JUNCTION_CHANCE = 0.12f;
        private const int CORRIDOR_WIDTH = 2;         // demi-largeur = 1 → couloirs de 3 tuiles
        private const int WALL_THICKNESS = 2;
        private const float EXTRA_LOOP_CHANCE = 0.10f;

        // ─── Réglages de peuplement (à ajuster à volonté) ───
        private const float MONSTER_DENSITY = 1.0f;       // multiplicateur global du nombre de monstres
        private const float CORRIDOR_PATROL_CHANCE = 0.30f;
        private const float CORRIDOR_PROP_CHANCE = 0.40f;
        private const float ELITE_CHANCE = 0.12f;         // chance qu'un monstre "normal" soit un élite

        // ═══════════════════════════════════════════════════════════════
        // TYPES
        // ═══════════════════════════════════════════════════════════════
        private enum RoomType
        {
            Junction, Rest, Boss, Treasure, Guard, Barracks, Crypt, Library,
            Storage, Alchemy, Forge, Shrine, Mushroom, PillarHall, Fountain
        }

        private enum RoomShape { Rect, Chamfer, Round, Cross, Blob }

        private enum LootTheme { Supplies, Food, Gear, Books, Alchemy, Smithing, Treasure, Grave }

        private enum Zone { Edge, Inner, Any }

        private class Cell
        {
            public int GridX, GridY;
            public bool IsRoom;
            public RoomType Type = RoomType.Junction;
            public RoomShape Shape = RoomShape.Rect;
            public bool IsRest => Type == RoomType.Rest;
            public bool IsBoss => Type == RoomType.Boss;
            public int RoomX, RoomY, RoomW, RoomH;   // coordonnées locales (relatives à l'origine du donjon)
            public bool[,] Mask = new bool[0, 0];     // forme réelle de la salle dans sa boîte englobante
            public int Depth = -1;                    // distance (en cellules) depuis la salle de repos
            public List<Cell> Neighbors = new();

            public Vector2 Center => new(RoomX + RoomW / 2f, RoomY + RoomH / 2f);
        }

        private class Corridor
        {
            public Cell A = null!, B = null!;
            public List<(int x, int y)> Verts = new();
        }

        private class DungeonConfig
        {
            public Cell[,] Grid = null!;
            public List<Cell> AllCells = new();
            public List<(Cell a, Cell b)> Links = new();
            public List<Corridor> Corridors = new();
            public Cell RestCell = null!;
            public Cell BossCell = null!;

            public int Width, Height;
            public bool[,] IsFloor = new bool[0, 0];
            public bool[,] IsCorridor = new bool[0, 0];
            public bool[,] IsRoomTile = new bool[0, 0];
            public Dictionary<LootTheme, List<ItemData>> PoolCache = new();
        }

        // Contexte de décoration d'une salle (coordonnées locales au donjon).
        private class RoomCtx
        {
            public DungeonConfig Cfg = null!;
            public Cell Cell = null!;
            public Random Rand = null!;
            public int OffX, OffY;
            public float Diff;                                  // 0 (départ) → 1 (boss)
            public (int x, int y) Center;
            public List<(int x, int y)> Tiles = new();
            public HashSet<(int x, int y)> TileSet = new();
            public List<(int x, int y)> Perimeter = new();      // tuiles collées à un mur / couloir
            public List<(int x, int y)> Interior = new();       // tuiles entourées de tuiles de la salle
            public HashSet<(int x, int y)> Reserved = new();    // couloirs d'accès + centre : jamais bloqués
            public HashSet<(int x, int y)> Occupied = new();    // objets bloquants
            public HashSet<(int x, int y)> Decor = new();       // objets traversables (bougies, lampes...)
            public HashSet<(int x, int y)> Spawned = new();     // tuiles où un monstre est déjà apparu
        }

        // ═══════════════════════════════════════════════════════════════
        // POINT D'ENTRÉE
        // ═══════════════════════════════════════════════════════════════
        public static void GenerateDungeon(DungeonInstance instance, Random rand)
        {
            var config = BuildLayout(rand);
            BuildDungeonTiles(instance, config, rand);
            PopulateRooms(instance, config, rand);

            instance.RestSpawnTile = (
                instance.OriginX + (int)config.RestCell.Center.X,
                instance.OriginY + (int)config.RestCell.Center.Y
            );
        }

        // ═══════════════════════════════════════════════════════════════
        // 1. GÉOMÉTRIE : grille + labyrinthe parfait + typage des salles
        // ═══════════════════════════════════════════════════════════════
        private static DungeonConfig BuildLayout(Random rand)
        {
            var config = new DungeonConfig();
            var grid = new Cell[GRID_SIZE, GRID_SIZE];

            for (int gx = 0; gx < GRID_SIZE; gx++)
            for (int gy = 0; gy < GRID_SIZE; gy++)
            {
                var cell = new Cell { GridX = gx, GridY = gy, IsRoom = rand.NextDouble() >= JUNCTION_CHANCE };
                grid[gx, gy] = cell;
                config.AllCells.Add(cell);
            }
            config.Grid = grid;

            // Point de départ : un coin, pour maximiser la profondeur du labyrinthe.
            var startCell = grid[0, 0];
            startCell.IsRoom = true;

            // Labyrinthe parfait par parcours en profondeur aléatoire (recursive backtracker).
            var visited = new HashSet<Cell> { startCell };
            var stack = new Stack<Cell>();
            stack.Push(startCell);
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                var candidates = GetGridNeighbors(grid, current)
                    .Where(n => !visited.Contains(n))
                    .OrderBy(_ => rand.Next())
                    .ToList();
                if (candidates.Count == 0) { stack.Pop(); continue; }

                var next = candidates[0];
                current.Neighbors.Add(next);
                next.Neighbors.Add(current);
                config.Links.Add((current, next));
                visited.Add(next);
                stack.Push(next);
            }

            // Quelques boucles supplémentaires.
            foreach (var cell in config.AllCells)
            {
                foreach (var neighbor in GetGridNeighbors(grid, cell))
                {
                    if (cell.Neighbors.Contains(neighbor)) continue;
                    if (rand.NextDouble() < EXTRA_LOOP_CHANCE)
                    {
                        cell.Neighbors.Add(neighbor);
                        neighbor.Neighbors.Add(cell);
                        config.Links.Add((cell, neighbor));
                    }
                }
            }

            // BFS : profondeur de chaque cellule + cellule la plus éloignée → boss.
            startCell.Depth = 0;
            var queue = new Queue<Cell>();
            queue.Enqueue(startCell);
            Cell farthest = startCell;
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var n in cur.Neighbors)
                {
                    if (n.Depth != -1) continue;
                    n.Depth = cur.Depth + 1;
                    if (n.Depth > farthest.Depth) farthest = n;
                    queue.Enqueue(n);
                }
            }
            farthest.IsRoom = true;

            config.RestCell = startCell;
            config.BossCell = farthest;

            AssignRoomTypes(config, rand);

            foreach (var cell in config.AllCells)
                PlaceRoom(cell, rand);

            foreach (var (a, b) in config.Links)
                config.Corridors.Add(new Corridor { A = a, B = b, Verts = ComputeCorridorVerts(a, b) });

            return config;
        }

        private static IEnumerable<Cell> GetGridNeighbors(Cell[,] grid, Cell cell)
        {
            (int dx, int dy)[] dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };
            foreach (var (dx, dy) in dirs)
            {
                int nx = cell.GridX + dx;
                int ny = cell.GridY + dy;
                if (nx >= 0 && nx < GRID_SIZE && ny >= 0 && ny < GRID_SIZE)
                    yield return grid[nx, ny];
            }
        }

        // Répartition pondérée des types de salles, avec plafonds et anti-répétition.
        private static void AssignRoomTypes(DungeonConfig config, Random rand)
        {
            int maxDepth = Math.Max(1, config.AllCells.Max(c => c.Depth));
            var counts = new Dictionary<RoomType, int>();
            var caps = new Dictionary<RoomType, int>
            {
                { RoomType.Treasure, 6 }, { RoomType.Fountain, 2 }, { RoomType.Forge, 2 },
                { RoomType.Alchemy, 3 }, { RoomType.Library, 3 }, { RoomType.Shrine, 2 },
                { RoomType.Barracks, 5 }, { RoomType.Crypt, 5 }
            };

            foreach (var cell in config.AllCells.OrderBy(c => c.Depth))
            {
                if (cell == config.RestCell) { cell.Type = RoomType.Rest; continue; }
                if (cell == config.BossCell) { cell.Type = RoomType.Boss; continue; }
                if (!cell.IsRoom) { cell.Type = RoomType.Junction; continue; }

                float d = cell.Depth / (float)maxDepth;
                bool deadEnd = cell.Neighbors.Count == 1;

                var options = new List<(RoomType t, float w)>
                {
                    (RoomType.Guard,      18f),
                    (RoomType.Storage,    12f),
                    (RoomType.Barracks,   8f + 12f * d),
                    (RoomType.Crypt,      8f + 10f * d),
                    (RoomType.Library,    10f),
                    (RoomType.Alchemy,    8f),
                    (RoomType.Forge,      6f),
                    (RoomType.Shrine,     5f + 6f * d),
                    (RoomType.Mushroom,   Math.Max(3f, 10f - 5f * d)),
                    (RoomType.PillarHall, 9f),
                    (RoomType.Fountain,   4f + 5f * d),
                    (RoomType.Treasure,   deadEnd ? 30f : 4f),
                };

                for (int i = 0; i < options.Count; i++)
                {
                    var (t, w) = options[i];
                    if (caps.TryGetValue(t, out int cap) && counts.GetValueOrDefault(t) >= cap) w = 0f;
                    if (cell.Neighbors.Any(n => n.Type == t)) w *= 0.25f; // évite les salles identiques côte à côte
                    options[i] = (t, w);
                }

                cell.Type = WeightedPick(options, rand);
                counts[cell.Type] = counts.GetValueOrDefault(cell.Type) + 1;
            }

            // Garantit au moins une salle "havre" (puits) avant la fin du labyrinthe.
            if (counts.GetValueOrDefault(RoomType.Fountain) == 0)
            {
                var swappable = new[] { RoomType.Guard, RoomType.Storage, RoomType.PillarHall, RoomType.Mushroom, RoomType.Alchemy, RoomType.Library };
                var pick = config.AllCells
                    .Where(c => swappable.Contains(c.Type))
                    .OrderByDescending(c => c.Depth)
                    .FirstOrDefault();
                if (pick != null) pick.Type = RoomType.Fountain;
            }
        }

        private static T WeightedPick<T>(List<(T item, float w)> list, Random rand)
        {
            float total = list.Sum(x => x.w);
            if (total <= 0f) return list[0].item;
            float roll = (float)rand.NextDouble() * total;
            foreach (var (item, w) in list)
            {
                roll -= w;
                if (roll <= 0f && w > 0f) return item;
            }
            return list.Last(x => x.w > 0f).item;
        }

        private static (int min, int max) SizeRange(RoomType t) => t switch
        {
            RoomType.Rest => (10, 12),
            RoomType.Boss => (18, 20),
            RoomType.Treasure => (8, 11),
            RoomType.Guard => (11, 15),
            RoomType.Barracks => (13, 17),
            RoomType.Crypt => (13, 17),
            RoomType.Library => (12, 16),
            RoomType.Storage => (9, 12),
            RoomType.Alchemy => (9, 12),
            RoomType.Forge => (10, 13),
            RoomType.Shrine => (12, 15),
            RoomType.Mushroom => (13, 17),
            RoomType.PillarHall => (14, 18),
            RoomType.Fountain => (10, 13),
            _ => (3, 3),
        };

        private static RoomShape PickShape(RoomType t, Random rand)
        {
            double r = rand.NextDouble();
            return t switch
            {
                RoomType.Boss => RoomShape.Chamfer,
                RoomType.Treasure => r < 0.5 ? RoomShape.Chamfer : RoomShape.Round,
                RoomType.Guard => r < 0.5 ? RoomShape.Rect : (r < 0.8 ? RoomShape.Chamfer : RoomShape.Cross),
                RoomType.Crypt => r < 0.6 ? RoomShape.Cross : RoomShape.Rect,
                RoomType.Alchemy => r < 0.5 ? RoomShape.Round : RoomShape.Chamfer,
                RoomType.Shrine => RoomShape.Round,
                RoomType.Mushroom => RoomShape.Blob,
                RoomType.PillarHall => r < 0.3 ? RoomShape.Chamfer : RoomShape.Rect,
                RoomType.Fountain => r < 0.5 ? RoomShape.Round : RoomShape.Chamfer,
                _ => RoomShape.Rect,
            };
        }

        private static bool[,] BuildMask(RoomShape shape, int w, int h, Random rand)
        {
            var m = new bool[w, h];
            double p1 = rand.NextDouble() * Math.PI * 2, p2 = rand.NextDouble() * Math.PI * 2;
            int k = Math.Max(2, Math.Min(w, h) / 4);
            int aw = Math.Max(3, w / 3), ah = Math.Max(3, h / 3);
            int ax0 = (w - aw) / 2, ay0 = (h - ah) / 2;

            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                double nx = (x + 0.5 - w / 2.0) / (w / 2.0);
                double ny = (y + 0.5 - h / 2.0) / (h / 2.0);
                bool on = true;
                switch (shape)
                {
                    case RoomShape.Chamfer:
                        on = Math.Min(x, w - 1 - x) + Math.Min(y, h - 1 - y) >= k;
                        break;
                    case RoomShape.Round:
                        on = nx * nx + ny * ny <= 1.0;
                        break;
                    case RoomShape.Cross:
                        on = (x >= ax0 && x < ax0 + aw) || (y >= ay0 && y < ay0 + ah);
                        break;
                    case RoomShape.Blob:
                    {
                        double r = Math.Sqrt(nx * nx + ny * ny);
                        double th = Math.Atan2(ny, nx);
                        double lim = 0.78 + 0.13 * Math.Sin(3 * th + p1) + 0.07 * Math.Sin(5 * th + p2) + 0.08;
                        on = r <= lim || r <= 0.35;
                        break;
                    }
                }
                m[x, y] = on;
            }
            return m;
        }

        private static void PlaceRoom(Cell cell, Random rand)
        {
            int w, h;
            if (!cell.IsRoom)
            {
                w = h = 3;
                cell.Shape = RoomShape.Rect;
            }
            else
            {
                var (min, max) = SizeRange(cell.Type);
                w = Math.Min(rand.Next(min, max + 1), CELL_SIZE - 2);
                h = Math.Min(rand.Next(min, max + 1), CELL_SIZE - 2);
                cell.Shape = PickShape(cell.Type, rand);
            }

            int cox = cell.GridX * CELL_SIZE;
            int coy = cell.GridY * CELL_SIZE;
            int maxOffX = CELL_SIZE - w - ROOM_MARGIN;
            int maxOffY = CELL_SIZE - h - ROOM_MARGIN;

            cell.RoomW = w;
            cell.RoomH = h;
            cell.RoomX = cox + (maxOffX > ROOM_MARGIN ? rand.Next(ROOM_MARGIN, maxOffX) : ROOM_MARGIN);
            cell.RoomY = coy + (maxOffY > ROOM_MARGIN ? rand.Next(ROOM_MARGIN, maxOffY) : ROOM_MARGIN);
            cell.Mask = BuildMask(cell.Shape, w, h, rand);
        }

        // Couloir en "Z" entre deux cellules voisines : part du centre de A, décroche à mi-chemin
        // dans l'espace libre entre les deux salles, et arrive au centre de B. Fonctionne même
        // quand les deux salles sont décalées l'une par rapport à l'autre.
        private static List<(int x, int y)> ComputeCorridorVerts(Cell a, Cell b)
        {
            int ax = (int)a.Center.X, ay = (int)a.Center.Y;
            int bx = (int)b.Center.X, by = (int)b.Center.Y;
            var v = new List<(int x, int y)>();
            if (a.GridX == b.GridX)
            {
                int midY = a.GridY < b.GridY
                    ? (a.RoomY + a.RoomH + b.RoomY) / 2
                    : (b.RoomY + b.RoomH + a.RoomY) / 2;
                v.Add((ax, ay)); v.Add((ax, midY)); v.Add((bx, midY)); v.Add((bx, by));
            }
            else
            {
                int midX = a.GridX < b.GridX
                    ? (a.RoomX + a.RoomW + b.RoomX) / 2
                    : (b.RoomX + b.RoomW + a.RoomX) / 2;
                v.Add((ax, ay)); v.Add((midX, ay)); v.Add((midX, by)); v.Add((bx, by));
            }
            return v;
        }

        private static IEnumerable<(int x, int y)> EnumeratePath(List<(int x, int y)> verts)
        {
            for (int i = 0; i < verts.Count - 1; i++)
            {
                var (x0, y0) = verts[i];
                var (x1, y1) = verts[i + 1];
                int dx = Math.Sign(x1 - x0), dy = Math.Sign(y1 - y0);
                int len = Math.Abs(x1 - x0) + Math.Abs(y1 - y0);
                for (int s = (i == 0 ? 0 : 1); s <= len; s++)
                    yield return (x0 + dx * s, y0 + dy * s);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 2. CONSTRUCTION DES TUILES (sol, murs, torches de couloir, porte)
        // ═══════════════════════════════════════════════════════════════
        private static void BuildDungeonTiles(DungeonInstance instance, DungeonConfig config, Random rand)
        {
            int width = instance.Width;
            int height = instance.Height;
            int offsetX = instance.OriginX;
            int offsetY = instance.OriginY;

            var isFloor = new bool[width, height];
            var isCorridor = new bool[width, height];
            var isRoomTile = new bool[width, height];
            var ground = new int[width, height];
            config.Width = width; config.Height = height;
            config.IsFloor = isFloor; config.IsCorridor = isCorridor; config.IsRoomTile = isRoomTile;

            bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

            // Salles (selon leur forme réelle) et carrefours
            foreach (var cell in config.AllCells)
                for (int lx = 0; lx < cell.RoomW; lx++)
                    for (int ly = 0; ly < cell.RoomH; ly++)
                    {
                        if (!cell.Mask[lx, ly]) continue;
                        int x = cell.RoomX + lx, y = cell.RoomY + ly;
                        if (!InBounds(x, y)) continue;
                        isFloor[x, y] = true;
                        isRoomTile[x, y] = true;
                    }

            // Couloirs (3 tuiles de large)
            int half = CORRIDOR_WIDTH / 2;
            foreach (var cor in config.Corridors)
            {
                for (int i = 0; i < cor.Verts.Count - 1; i++)
                {
                    var (x0, y0) = cor.Verts[i];
                    var (x1, y1) = cor.Verts[i + 1];
                    for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                    for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                        for (int d = -half; d <= half; d++)
                        {
                            int px = (x0 == x1) ? x + d : x;
                            int py = (x0 == x1) ? y : y + d;
                            if (!InBounds(px, py)) continue;
                            isFloor[px, py] = true;
                            isCorridor[px, py] = true;
                        }
                }
            }

            // Sol : roche de grotte dans les zones vides, sol du donjon sur les zones praticables,
            // puis revêtement propre à chaque type de salle.
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    ground[x, y] = isFloor[x, y] ? DUNGEON_FLOOR_ID : FLOOR_CAVE;

            foreach (var cell in config.AllCells)
            {
                if (!cell.IsRoom) continue;
                for (int lx = 0; lx < cell.RoomW; lx++)
                    for (int ly = 0; ly < cell.RoomH; ly++)
                    {
                        if (!cell.Mask[lx, ly]) continue;
                        int x = cell.RoomX + lx, y = cell.RoomY + ly;
                        if (!InBounds(x, y)) continue;
                        ground[x, y] = PickFloorTile(cell, lx, ly, rand);
                    }
            }

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    World.SetGroundTile(offsetX + x, offsetY + y, ground[x, y]);

            // Murs épais et infranchissables tout autour des zones de sol
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (!isFloor[x, y]) continue;
                    for (int dx = -WALL_THICKNESS; dx <= WALL_THICKNESS; dx++)
                        for (int dy = -WALL_THICKNESS; dy <= WALL_THICKNESS; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (!InBounds(nx, ny) || isFloor[nx, ny]) continue;
                            int wx = offsetX + nx, wy = offsetY + ny;
                            if (World.GetObjectIdAt(wx, wy) == 0)
                                World.AddPlacedObject(wx, wy, DUNGEON_WALL_ID);
                        }
                }

            // Torches le long des couloirs (une tous les ~7 pas, côtés alternés).
            PlaceCorridorTorches(config, offsetX, offsetY, rand);

            // Porte de sortie : sur un côté de la salle du boss SANS couloir.
            var bossCell = config.BossCell;
            (int doorX, int doorY) = PickOuterWallTile(bossCell, width, height);
            instance.ExitDoorTile = (offsetX + doorX, offsetY + doorY);
            if (World.GetObjectIdAt(offsetX + doorX, offsetY + doorY) == 0)
                World.AddPlacedObject(offsetX + doorX, offsetY + doorY, DUNGEON_WALL_ID);
            ClearExitApproach(bossCell, doorX, doorY, offsetX, offsetY);
        }

        private static int PickFloorTile(Cell cell, int lx, int ly, Random rand)
        {
            int dx = Math.Abs(lx - cell.RoomW / 2), dy = Math.Abs(ly - cell.RoomH / 2);
            switch (cell.Type)
            {
                case RoomType.Rest: return FLOOR_WOOD;
                case RoomType.Treasure: return FLOOR_CARPET;
                case RoomType.Library: return dx <= 1 ? FLOOR_CARPET : FLOOR_WOOD;
                case RoomType.Storage: return FLOOR_WOOD;
                case RoomType.Barracks:
                case RoomType.Fountain:
                case RoomType.Alchemy: return FLOOR_PAVING;
                case RoomType.Forge: return rand.NextDouble() < 0.12 ? FLOOR_CAVE : FLOOR_PAVING;
                case RoomType.Shrine: return Math.Max(dx, dy) <= 2 ? FLOOR_CARPET : FLOOR_PAVING;
                case RoomType.Crypt: return rand.NextDouble() < 0.18 ? DUNGEON_FLOOR_ID : FLOOR_PAVING;
                case RoomType.Guard: return rand.NextDouble() < 0.08 ? FLOOR_DIRT : DUNGEON_FLOOR_ID;
                case RoomType.Mushroom: return rand.NextDouble() < 0.22 ? FLOOR_DIRT : FLOOR_CAVE;
                case RoomType.PillarHall: return ((lx + ly) & 1) == 0 ? DUNGEON_FLOOR_ID : FLOOR_PAVING;
                // Boss : le sol reste DUNGEON_FLOOR_ID sur le passage vers la porte (EnsureExitApproach).
                case RoomType.Boss: return Math.Max(dx, dy) <= 2 ? FLOOR_CARPET : DUNGEON_FLOOR_ID;
                default: return DUNGEON_FLOOR_ID;
            }
        }

        private static void PlaceCorridorTorches(DungeonConfig config, int offX, int offY, Random rand)
        {
            var dirs = new (int dx, int dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            foreach (var cor in config.Corridors)
            {
                int counter = rand.Next(2, 5);
                bool flip = rand.Next(2) == 0;
                foreach (var (x, y) in EnumeratePath(cor.Verts))
                {
                    if (x < 0 || y < 0 || x >= config.Width || y >= config.Height) continue;
                    if (config.IsRoomTile[x, y]) continue;
                    if (++counter < 7) continue;

                    IEnumerable<(int dx, int dy)> order = flip ? Enumerable.Reverse(dirs) : dirs;
                    foreach (var (dx, dy) in order)
                    {
                        int fx = x + dx, fy = y + dy;          // tuile de couloir voisine
                        int wx = x + 2 * dx, wy = y + 2 * dy;  // mur juste derrière
                        if (fx < 0 || fy < 0 || fx >= config.Width || fy >= config.Height) continue;
                        if (wx < 0 || wy < 0 || wx >= config.Width || wy >= config.Height) continue;
                        if (!config.IsFloor[fx, fy] || config.IsFloor[wx, wy]) continue;
                        if (World.GetObjectIdAt(offX + wx, offY + wy) != DUNGEON_WALL_ID) continue;
                        PlaceWallTorch(offX + wx, offY + wy);
                        counter = 0;
                        flip = !flip;
                        break;
                    }
                }
            }
        }

        private static void PlaceWallTorch(int wallX, int wallY)
        {
            if (WorldTileRegistry.GetTile(OBJ_TORCH)?.WallMountable != true) return;
            World.SetTileMeta(wallX, wallY, "wall_torch", OBJ_TORCH.ToString());
        }

        // Choisit la tuile du mur extérieur de la salle du boss, sur un côté SANS couloir
        // (sinon la porte tomberait dans un passage), le plus proche de la limite de la zone.
        private static (int x, int y) PickOuterWallTile(Cell boss, int regionWidth, int regionHeight)
        {
            int cx = (int)boss.Center.X;
            int cy = (int)boss.Center.Y;

            bool HasNeighbor(int gdx, int gdy) =>
                boss.Neighbors.Any(n => n.GridX == boss.GridX + gdx && n.GridY == boss.GridY + gdy);

            var sides = new List<(int dist, bool blocked, (int x, int y) tile)>
            {
                (regionWidth - cx,  HasNeighbor(1, 0),  (Math.Min(regionWidth - 1,  boss.RoomX + boss.RoomW - 1 + WALL_THICKNESS), cy)),
                (cx,                HasNeighbor(-1, 0), (Math.Max(0,                boss.RoomX - WALL_THICKNESS), cy)),
                (regionHeight - cy, HasNeighbor(0, 1),  (cx, Math.Min(regionHeight - 1, boss.RoomY + boss.RoomH - 1 + WALL_THICKNESS))),
                (cy,                HasNeighbor(0, -1), (cx, Math.Max(0,                boss.RoomY - WALL_THICKNESS))),
            };

            var free = sides.Where(s => !s.blocked).ToList();
            var pool = free.Count > 0 ? free : sides;
            return pool.OrderBy(s => s.dist).First().tile;
        }

        private static void ClearExitApproach(Cell bossCell, int doorX, int doorY, int offsetX, int offsetY)
        {
            void ClearTile(int x, int y)
            {
                int worldX = offsetX + x;
                int worldY = offsetY + y;
                World.RemovePlacedObject(worldX, worldY);
                World.SetGroundTile(worldX, worldY, DUNGEON_FLOOR_ID);
            }

            if (doorX < bossCell.RoomX)
            {
                for (int x = doorX + 1; x < bossCell.RoomX; x++) ClearTile(x, doorY);
            }
            else if (doorX >= bossCell.RoomX + bossCell.RoomW)
            {
                for (int x = bossCell.RoomX + bossCell.RoomW; x < doorX; x++) ClearTile(x, doorY);
            }
            else if (doorY < bossCell.RoomY)
            {
                for (int y = doorY + 1; y < bossCell.RoomY; y++) ClearTile(doorX, y);
            }
            else
            {
                for (int y = bossCell.RoomY + bossCell.RoomH; y < doorY; y++) ClearTile(doorX, y);
            }
        }

        public static void EnsureExitApproach(DungeonInstance instance)
        {
            int doorX = instance.ExitDoorTile.x;
            int doorY = instance.ExitDoorTile.y;
            var directions = new (int x, int y)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            (int x, int y, int distance)? nearestFloor = null;

            foreach (var (dx, dy) in directions)
            {
                for (int distance = 1; distance <= WALL_THICKNESS + 3; distance++)
                {
                    int x = doorX + dx * distance;
                    int y = doorY + dy * distance;
                    if (World.GetGroundTileIdAt(x, y) != DUNGEON_FLOOR_ID) continue;
                    if (nearestFloor == null || distance < nearestFloor.Value.distance)
                        nearestFloor = (dx, dy, distance);
                    break;
                }
            }

            if (nearestFloor == null) return;
            var path = nearestFloor.Value;
            for (int step = 1; step < path.distance; step++)
            {
                int x = doorX + path.x * step;
                int y = doorY + path.y * step;
                World.RemovePlacedObject(x, y);
                World.SetGroundTile(x, y, DUNGEON_FLOOR_ID);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 3. PEUPLEMENT : mobilier, monstres, coffres, boss
        // ═══════════════════════════════════════════════════════════════
        private static void PopulateRooms(DungeonInstance instance, DungeonConfig config, Random rand)
        {
            int maxDepth = Math.Max(1, config.AllCells.Where(c => c.IsRoom).Max(c => c.Depth));

            foreach (var cell in config.AllCells)
            {
                if (!cell.IsRoom) continue; // les carrefours restent vides
                var ctx = CreateContext(instance, config, cell, rand, maxDepth);

                switch (cell.Type)
                {
                    case RoomType.Rest: FurnishRest(ctx); break;
                    case RoomType.Boss: FurnishBoss(ctx, instance, maxDepth); break;
                    case RoomType.Treasure: FurnishTreasure(ctx); break;
                    case RoomType.Guard: FurnishGuard(ctx); break;
                    case RoomType.Barracks: FurnishBarracks(ctx); break;
                    case RoomType.Crypt: FurnishCrypt(ctx); break;
                    case RoomType.Library: FurnishLibrary(ctx); break;
                    case RoomType.Storage: FurnishStorage(ctx); break;
                    case RoomType.Alchemy: FurnishAlchemy(ctx); break;
                    case RoomType.Forge: FurnishForge(ctx); break;
                    case RoomType.Shrine: FurnishShrine(ctx); break;
                    case RoomType.Mushroom: FurnishMushroom(ctx); break;
                    case RoomType.PillarHall: FurnishPillarHall(ctx); break;
                    case RoomType.Fountain: FurnishFountain(ctx); break;
                }
            }

            PopulateCorridors(instance, config, rand, maxDepth);
        }

        private static RoomCtx CreateContext(DungeonInstance instance, DungeonConfig config, Cell cell, Random rand, int maxDepth)
        {
            var c = new RoomCtx
            {
                Cfg = config, Cell = cell, Rand = rand,
                OffX = instance.OriginX, OffY = instance.OriginY,
                Diff = cell.Depth / (float)maxDepth,
                Center = ((int)cell.Center.X, (int)cell.Center.Y),
            };

            for (int lx = 0; lx < cell.RoomW; lx++)
                for (int ly = 0; ly < cell.RoomH; ly++)
                    if (cell.Mask[lx, ly])
                    {
                        var p = (cell.RoomX + lx, cell.RoomY + ly);
                        c.Tiles.Add(p);
                        c.TileSet.Add(p);
                    }

            var n4 = new (int dx, int dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            foreach (var (x, y) in c.Tiles)
            {
                if (n4.Any(d => !c.TileSet.Contains((x + d.dx, y + d.dy)))) c.Perimeter.Add((x, y));
                bool inner = true;
                for (int dx = -1; dx <= 1 && inner; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (!c.TileSet.Contains((x + dx, y + dy))) { inner = false; break; }
                if (inner) c.Interior.Add((x, y));
            }

            // Centre (3x3) + couloirs d'accès vers chaque voisin : toujours libres.
            int cx = c.Center.x, cy = c.Center.y;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    c.Reserved.Add((cx + dx, cy + dy));

            foreach (var n in cell.Neighbors)
            {
                if (n.GridX > cell.GridX) ReserveLane(c, cx, cell.RoomX + cell.RoomW - 1, cy, true);
                else if (n.GridX < cell.GridX) ReserveLane(c, cell.RoomX, cx, cy, true);
                else if (n.GridY > cell.GridY) ReserveLane(c, cy, cell.RoomY + cell.RoomH - 1, cx, false);
                else ReserveLane(c, cell.RoomY, cy, cx, false);
            }

            // Salle du boss : couloir jusqu'à la porte de sortie.
            if (cell.IsBoss)
            {
                int dx = instance.ExitDoorTile.x - c.OffX, dy = instance.ExitDoorTile.y - c.OffY;
                if (dx < cell.RoomX) ReserveLane(c, dx, cx, cy, true);
                else if (dx >= cell.RoomX + cell.RoomW) ReserveLane(c, cx, dx, cy, true);
                else if (dy < cell.RoomY) ReserveLane(c, dy, cy, cx, false);
                else ReserveLane(c, cy, dy, cx, false);
            }

            return c;
        }

        // Réserve une bande de 3 tuiles de large. horizontal : de x=from à x=to, autour de la ligne y=axis ;
        // sinon de y=from à y=to, autour de la colonne x=axis.
        private static void ReserveLane(RoomCtx c, int from, int to, int axis, bool horizontal)
        {
            for (int t = Math.Min(from, to); t <= Math.Max(from, to); t++)
                for (int d = -1; d <= 1; d++)
                    c.Reserved.Add(horizontal ? (t, axis + d) : (axis + d, t));
        }

        // ─────────────── Primitives de placement ───────────────

        // Place un objet bloquant (largeur > 1 : on réserve x-(w-1)..x+(w-1) par prudence sur l'ancrage).
        // Refuse si l'emplacement gêne un couloir d'accès ou couperait la salle en deux.
        private static bool Place(RoomCtx c, int x, int y, int id, int width = 1)
        {
            var fp = new List<(int x, int y)>();
            for (int i = -(width - 1); i <= width - 1; i++) fp.Add((x + i, y));

            foreach (var f in fp)
                if (!c.TileSet.Contains(f) || c.Reserved.Contains(f) || c.Occupied.Contains(f) || c.Decor.Contains(f))
                    return false;

            if (!StaysConnected(c, fp)) return false;

            foreach (var f in fp) c.Occupied.Add(f);
            World.AddPlacedObject(c.OffX + x, c.OffY + y, id);
            return true;
        }

        private static bool StaysConnected(RoomCtx c, List<(int x, int y)> extra)
        {
            var blocked = new HashSet<(int x, int y)>(c.Occupied);
            foreach (var e in extra) blocked.Add(e);
            int freeCount = c.Tiles.Count - blocked.Count;

            var seen = new HashSet<(int x, int y)> { c.Center };
            var q = new Queue<(int x, int y)>();
            q.Enqueue(c.Center);
            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue();
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = (x + dx, y + dy);
                    if (!c.TileSet.Contains(n) || blocked.Contains(n) || !seen.Add(n)) continue;
                    q.Enqueue(n);
                }
            }
            return seen.Count == freeCount;
        }

        private static List<(int x, int y)> ZoneTiles(RoomCtx c, Zone z) => z switch
        {
            Zone.Edge => c.Perimeter,
            Zone.Inner => c.Interior,
            _ => c.Tiles,
        };

        private static List<(int x, int y)> PlaceMany(RoomCtx c, int id, int count, Zone zone,
            int width = 1, int spacing = 0, Func<(int x, int y), bool>? filter = null)
        {
            var result = new List<(int x, int y)>();
            if (count <= 0) return result;

            foreach (var p in ZoneTiles(c, zone).OrderBy(_ => c.Rand.Next()).ToList())
            {
                if (result.Count >= count) break;
                if (filter != null && !filter(p)) continue;
                if (spacing > 0 && result.Any(q => Math.Max(Math.Abs(q.x - p.x), Math.Abs(q.y - p.y)) < spacing)) continue;
                if (Place(c, p.x, p.y, id, width)) result.Add(p);
            }
            return result;
        }

        private static void PlaceBanners(RoomCtx c, int count, Zone zone, int spacing)
        {
            var patternNames = new[] { "anchor", "bow", "cross", "crown", "deer", "fist", "fox", "hammer", "helmet", "key", "lys", "moon", "star", "sword" };
            var shapeNames = new[] { "angled", "forked", "long_pointed", "pointed", "scalloped", "shield_shaped", "square", "tatterned" };
            var palette = new[]
            {
                new Color(180, 20, 20, 255), new Color(20, 60, 180, 255),
                new Color(20, 130, 20, 255), new Color(160, 130, 0, 255),
                new Color(100, 0, 140, 255), new Color(0, 130, 130, 255),
                new Color(200, 80, 0, 255), new Color(30, 30, 30, 255),
                new Color(220, 220, 220, 255), new Color(140, 80, 30, 255),
            };

            foreach (var (x, y) in PlaceMany(c, OBJ_BANNER, count, zone, spacing: spacing))
            {
                int worldX = c.OffX + x;
                int worldY = c.OffY + y;
                int backgroundIndex = c.Rand.Next(palette.Length);
                int patternColorIndex = (backgroundIndex + 1 + c.Rand.Next(palette.Length - 1)) % palette.Length;
                Color backgroundColor = palette[backgroundIndex];
                Color patternColor = palette[patternColorIndex];

                World.GetChunkAt(worldX, worldY)?.SetBannerHolderAt(worldX, worldY, new BannerHolderData
                {
                    HasBanner = true,
                    PatternName = patternNames[c.Rand.Next(patternNames.Length)],
                    ShapeName = shapeNames[c.Rand.Next(shapeNames.Length)],
                    BgR = backgroundColor.R,
                    BgG = backgroundColor.G,
                    BgB = backgroundColor.B,
                    PatR = patternColor.R,
                    PatG = patternColor.G,
                    PatB = patternColor.B,
                });
            }
        }

        // Objet traversable (bougie, lampe, champignon ramassable...) : n'obstrue rien.
        private static int PlaceDecor(RoomCtx c, int id, int count, Zone zone, bool allowReserved = false)
        {
            int n = 0;
            foreach (var p in ZoneTiles(c, zone).OrderBy(_ => c.Rand.Next()).ToList())
            {
                if (n >= count) break;
                if (c.Occupied.Contains(p) || c.Decor.Contains(p)) continue;
                if (!allowReserved && c.Reserved.Contains(p)) continue;
                c.Decor.Add(p);
                World.AddPlacedObject(c.OffX + p.x, c.OffY + p.y, id);
                n++;
            }
            return n;
        }

        // Pose des chaises autour (au-dessus / au-dessous) d'un meuble déjà posé.
        private static void PlaceAround(RoomCtx c, (int x, int y) p, int id, int count)
        {
            var offs = new (int dx, int dy)[] { (0, -1), (0, 1), (1, -1), (1, 1), (-1, -1), (-1, 1) };
            int placed = 0;
            foreach (var o in offs.OrderBy(_ => c.Rand.Next()))
            {
                if (placed >= count) break;
                if (Place(c, p.x + o.dx, p.y + o.dy, id)) placed++;
            }
        }

        // Torches murales : remplacent une tuile de mur collée à la salle (elles sont traversables).
        private static void PlaceWallTorches(RoomCtx c, int count, int spacing = 4)
        {
            var placed = new List<(int x, int y)>();
            var dirs = new (int dx, int dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            foreach (var p in c.Perimeter.OrderBy(_ => c.Rand.Next()).ToList())
            {
                if (placed.Count >= count) break;
                if (placed.Any(q => Math.Abs(q.x - p.x) + Math.Abs(q.y - p.y) < spacing)) continue;
                foreach (var (dx, dy) in dirs.OrderBy(_ => c.Rand.Next()))
                {
                    int wx = p.x + dx, wy = p.y + dy;
                    if (wx < 0 || wy < 0 || wx >= c.Cfg.Width || wy >= c.Cfg.Height) continue;
                    if (c.Cfg.IsFloor[wx, wy]) continue;
                    if (World.GetObjectIdAt(c.OffX + wx, c.OffY + wy) != DUNGEON_WALL_ID) continue;
                    PlaceWallTorch(c.OffX + wx, c.OffY + wy);
                    placed.Add(p);
                    break;
                }
            }
        }

        // ─────────────── Butin ───────────────

        private static (int slots, int cols) ContainerLayout(int objId) => objId switch
        {
            OBJ_CHEST => (24, 5),
            OBJ_BARREL => (12, 3),
            OBJ_CRATE => (9, 3),
            OBJ_BOX => (4, 2),
            OBJ_SHELF => (6, 2),
            _ => (8, 4),
        };

        private static readonly string[] KEYS_BOOKS = { "scroll", "book", "paper", "parchment", "ink", "feather", "map", "wand", "card", "deck", "livre", "parchemin", "papier", "plume", "encre", "carte" };
        private static readonly string[] KEYS_ALCHEMY = { "potion", "bottle", "flower", "mushroom", "herb", "leaf", "wax", "dye", "milk", "honey", "petal", "fleur", "champignon", "herbe", "cire", "teinture", "lait", "miel" };
        private static readonly string[] KEYS_SMITHING = { "ore", "ingot", "iron", "copper", "coal", "steel", "nail", "gear", "clay", "brick", "minerai", "lingot", "charbon", "cuivre" };
        private static readonly string[] KEYS_TREASURE = { "gem", "garnet", "amber", "topaz", "onyx", "opal", "obsidian", "coin", "gold", "ring", "crown", "necklace", "jewel", "pearl", "ruby", "emerald", "sapphire", "diamond", "grenat", "ambre", "topaze", "anneau", "pièce" };
        private static readonly string[] KEYS_GRAVE = { "bone", "skull", "ossement", "crâne" };

        // Correspondance par mots entiers (ou sous-chaîne pour les mots de 5+ lettres), afin que
        // "map" ne matche pas "maple" et que "ore" ne matche pas "score".
        private static bool NameHas(ItemData d, string[] keys)
        {
            string name = (d.Name ?? "").ToLowerInvariant();
            var tokens = name.Split(new[] { '_', ' ', '-', '\'' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var key in keys)
                foreach (var t in tokens)
                    if (t == key || (key.Length >= 5 && t.Contains(key))) return true;
            return false;
        }

        private static List<ItemData> GetPool(DungeonConfig cfg, LootTheme theme)
        {
            if (cfg.PoolCache.TryGetValue(theme, out var cached)) return cached;

            var all = GameData.ItemDatabase.Values.ToList();
            bool Furniture(ItemData d) => d.Type == ItemType.Placeable || d.Type == ItemType.WallCovering;
            List<ItemData> Keyed(string[] keys) => all.Where(d => !Furniture(d) && NameHas(d, keys)).ToList();

            List<ItemData> supplies = all.Where(d => d.Type == ItemType.Food || d.Type == ItemType.Resource).ToList();
            List<ItemData> gear = all.Where(d => d.Type == ItemType.Weapon || d.Type == ItemType.Armor).ToList();

            List<ItemData> pool = theme switch
            {
                LootTheme.Food => all.Where(d => d.Type == ItemType.Food).ToList(),
                LootTheme.Supplies => supplies,
                LootTheme.Gear => gear,
                LootTheme.Books => Keyed(KEYS_BOOKS),
                LootTheme.Alchemy => Keyed(KEYS_ALCHEMY),
                LootTheme.Smithing => Keyed(KEYS_SMITHING).Concat(all.Where(d => d.Type == ItemType.Tool)).Distinct().ToList(),
                LootTheme.Treasure => Keyed(KEYS_TREASURE),
                LootTheme.Grave => Keyed(KEYS_GRAVE).Concat(Keyed(KEYS_TREASURE)).Distinct().ToList(),
                _ => supplies,
            };

            // Filets de sécurité : une catégorie vide ne doit jamais donner un coffre vide.
            if (pool.Count == 0) pool = theme is LootTheme.Treasure or LootTheme.Grave ? gear : supplies;
            if (pool.Count == 0) pool = all.Where(d => !Furniture(d)).ToList();
            if (pool.Count == 0) pool = all;

            cfg.PoolCache[theme] = pool;
            return pool;
        }

        private static void FillContainer(DungeonConfig cfg, ContainerInventoryData container, Random rand,
            LootTheme primary, LootTheme secondary, int tier, int minItems, int maxItems, float gearChance)
        {
            var primaryPool = GetPool(cfg, primary);
            var secondaryPool = GetPool(cfg, secondary);
            var gearPool = GetPool(cfg, LootTheme.Gear);
            int n = Math.Min(rand.Next(minItems, maxItems + 1), container.Slots.Count());

            for (int i = 0; i < n; i++)
            {
                List<ItemData> pool;
                if (rand.NextDouble() < gearChance && gearPool.Count > 0) pool = gearPool;
                else pool = rand.NextDouble() < 0.7 ? primaryPool : secondaryPool;
                if (pool.Count == 0) continue;

                var data = pool[rand.Next(pool.Count)];
                var slot = container.Slots.FirstOrDefault(s => s.IsEmpty);
                if (slot == null) break;
                int qty = (data.Type == ItemType.Weapon || data.Type == ItemType.Armor) ? 1 : rand.Next(1, tier >= 2 ? 5 : (tier == 1 ? 4 : 3));
                slot.Item = new Item(data.Name, qty, data.Color, data.Icon);
                slot.Count = slot.Item.Count;
            }
        }

        private static void AddContainerLoot(DungeonConfig cfg, int wx, int wy, int objId, Random rand,
            LootTheme primary, LootTheme secondary, int tier, int minItems, int maxItems, float gearChance = 0f)
        {
            var chunk = World.GetChunkAt(wx, wy);
            if (chunk == null) return;
            var (slots, cols) = ContainerLayout(objId);
            var container = new ContainerInventoryData(slots, cols);
            FillContainer(cfg, container, rand, primary, secondary, tier, minItems, maxItems, gearChance);
            chunk.SetContainerAt(wx, wy, container);
        }

        // Pose des conteneurs (coffre, tonneau, caisse, étagère...) et les remplit.
        private static void PlaceContainers(RoomCtx c, int objId, int count, Zone zone,
            LootTheme primary, LootTheme secondary, int tier, int minItems, int maxItems, float gearChance = 0f, int spacing = 0)
        {
            foreach (var p in PlaceMany(c, objId, count, zone, 1, spacing))
                AddContainerLoot(c.Cfg, c.OffX + p.x, c.OffY + p.y, objId, c.Rand, primary, secondary, tier, minItems, maxItems, gearChance);
        }

        private static int ChestTier(RoomCtx c) => c.Diff > 0.6f ? 2 : 1;

        // ─────────────── Monstres ───────────────

        // (espèce, espèce de repli si l'espèce n'a pas de stats dans le jeu, poids)
        private static readonly (string sp, string fb, float w)[] MIX_CAVE =
            { ("bat", "bat", 30), ("spider", "spider", 35), ("skeleton", "skeleton", 35) };
        private static readonly (string sp, string fb, float w)[] MIX_GUARD =
            { ("bat", "bat", 20), ("spider", "spider", 25), ("skeleton", "skeleton", 30), ("goblin", "skeleton", 15), ("wolf", "spider", 10) };
        private static readonly (string sp, string fb, float w)[] MIX_CRYPT =
            { ("skeleton", "skeleton", 85), ("bat", "bat", 15) };
        private static readonly (string sp, string fb, float w)[] MIX_BARRACKS =
            { ("goblin", "skeleton", 45), ("skeleton", "skeleton", 40), ("wolf", "spider", 15) };
        private static readonly (string sp, string fb, float w)[] MIX_FORGE =
            { ("goblin", "skeleton", 55), ("skeleton", "skeleton", 30), ("bat", "bat", 15) };
        private static readonly (string sp, string fb, float w)[] MIX_MUSHROOM =
            { ("spider", "spider", 60), ("bat", "bat", 40) };
        private static readonly (string sp, string fb, float w)[] MIX_LAB =
            { ("spider", "spider", 50), ("bat", "bat", 50) };
        private static readonly (string sp, string fb, float w)[] MIX_LIBRARY =
            { ("bat", "bat", 50), ("spider", "spider", 30), ("skeleton", "skeleton", 20) };

        private static readonly HashSet<string> CORE_SPECIES = new() { "bat", "spider", "skeleton" };

        private static Entity? CreateMonster(int tileX, int tileY, string species, string fallback, float difficulty, bool elite)
        {
            int ts = Program.TileSize;
            var pos = new Vector2(tileX * ts + ts / 2f, tileY * ts + ts / 2f);

            Entity? ent = null;
            try { ent = new Entity(pos, species, false); } catch { ent = null; }

            // Espèce inconnue du jeu (pas de stats) → on retombe sur l'espèce de repli.
            if ((ent == null || ent.MaxHP <= 0) && !string.Equals(species, fallback, StringComparison.OrdinalIgnoreCase))
            {
                try { ent = new Entity(pos, fallback, false); } catch { ent = null; }
            }
            if (ent == null) return null;

            // Filet de sécurité : sans stats valides, un monstre à 0 PV serait déjà "mort" et invisible.
            if (ent.MaxHP <= 0) ent.MaxHP = 25;
            if (ent.Attack <= 0) ent.Attack = 6;

            ent.MaxHP = (int)(ent.MaxHP * (1f + difficulty * 0.8f));
            ent.Attack = (int)(ent.Attack * (1f + difficulty * 0.5f));
            if (elite)
            {
                ent.MaxHP = (int)(ent.MaxHP * 2.2f);
                ent.Attack = (int)(ent.Attack * 1.4f);
                ent.Scale = 1.3f;
            }
            ent.CurrentHP = ent.MaxHP;
            ent.Behavior = "hostile";

            Program.GetEntities().Add(ent);
            World.GetChunkAt(tileX, tileY)?.Entities.Add(ent);
            return ent;
        }

        private static void SpawnMonsters(RoomCtx c, int count, (string sp, string fb, float w)[] mix, int elites = 0)
        {
            count = (int)Math.Round(count * MONSTER_DENSITY);
            if (count <= 0) return;

            var free = c.Tiles.Where(t => !c.Occupied.Contains(t)).OrderBy(_ => c.Rand.Next()).ToList();
            var weighted = mix.Select(m => (m, m.w)).ToList();

            for (int i = 0; i < count && i < free.Count; i++)
            {
                var t = free[i];
                var m = WeightedPick(weighted, c.Rand);
                bool elite = i < elites || c.Rand.NextDouble() < ELITE_CHANCE * (0.5f + c.Diff);
                if (CreateMonster(c.OffX + t.x, c.OffY + t.y, m.sp, m.fb, c.Diff, elite) != null)
                    c.Spawned.Add(t);
            }
        }

        // Nombre de monstres : proportionnel à la surface, augmente avec la profondeur.
        private static int MonsterCount(RoomCtx c, int areaDivisor, float depthBonus, int minCount, int maxCount)
        {
            int n = c.Tiles.Count / areaDivisor + (int)Math.Round(c.Diff * depthBonus) + c.Rand.Next(0, 2);
            return Math.Clamp(n, minCount, maxCount);
        }

        // ═══════════════════════════════════════════════════════════════
        // 4. AMÉNAGEMENT DES SALLES, PAR TYPE
        // ═══════════════════════════════════════════════════════════════

        // Départ : petit coin sûr avec feu de camp, sièges, réserves. Aucun monstre.
        private static void FurnishRest(RoomCtx c)
        {
            var fire = PlaceMany(c, OBJ_CAMPFIRE, 1, Zone.Inner);
            if (fire.Count > 0) PlaceAround(c, fire[0], OBJ_CHAIR, 2);
            PlaceMany(c, OBJ_COFFEE_TABLE, 1, Zone.Inner);
            PlaceContainers(c, OBJ_SHELF, 2, Zone.Edge, LootTheme.Supplies, LootTheme.Food, 0, 1, 3, 0f, 3);
            PlaceContainers(c, OBJ_CRATE, 2, Zone.Edge, LootTheme.Food, LootTheme.Supplies, 0, 1, 3, 0f, 2);
            PlaceContainers(c, OBJ_BARREL, 1, Zone.Edge, LootTheme.Food, LootTheme.Supplies, 0, 1, 3);
            if (c.Rand.NextDouble() < 0.6)
                PlaceContainers(c, OBJ_CHEST, 1, Zone.Edge, LootTheme.Supplies, LootTheme.Food, 0, 2, 4);
            PlaceMany(c, OBJ_POT, 2, Zone.Edge, 1, 3);
            PlaceDecor(c, OBJ_CANDLE, 3, Zone.Any);
            PlaceWallTorches(c, 4, 4);
        }

        // Boss : grande salle en croix de pylônes et bannières, escorte d'élites, gros butin.
        private static void FurnishBoss(RoomCtx c, DungeonInstance instance, int maxDepth)
        {
            var cell = c.Cell;
            int cx = c.Center.x, cy = c.Center.y;
            int ox = Math.Max(3, cell.RoomW / 2 - 3), oy = Math.Max(3, cell.RoomH / 2 - 3);

            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                Place(c, cx + sx * ox, cy + sy * oy, OBJ_PYLON);

            PlaceBanners(c, 6, Zone.Edge, 4);
            PlaceDecor(c, OBJ_CANDLE, 8, Zone.Any);
            PlaceWallTorches(c, 8, 4);

            PlaceContainers(c, OBJ_CHEST, 1, Zone.Edge, LootTheme.Gear, LootTheme.Treasure, 2, 8, 14, 0.5f);
            if (c.Rand.NextDouble() < 0.6)
                PlaceContainers(c, OBJ_CHEST, 1, Zone.Edge, LootTheme.Treasure, LootTheme.Supplies, 1, 4, 7, 0.25f, 5);

            // Le boss, au centre.
            int wx = c.OffX + cx, wy = c.OffY + cy;
            Vector2 bossPos = new(wx * Program.TileSize + Program.TileSize / 2f, wy * Program.TileSize + Program.TileSize / 2f);
            Entity boss = new(bossPos, "ogre", false);
            boss.IsBoss = true;
            boss.MaxHP = 200 + maxDepth * 15;
            boss.CurrentHP = boss.MaxHP;
            boss.Attack = 18;
            boss.Speed = 35;
            boss.Behavior = "hostile";
            boss.VisionRange = 6;
            Program.GetEntities().Add(boss);
            World.GetChunkAt(wx, wy)?.Entities.Add(boss);
            World.RegisterDungeonBoss(boss, instance.Id);

            // Escorte : quelques élites autour du boss.
            SpawnMonsters(c, 2 + maxDepth / 8, MIX_CRYPT, elites: 2);
        }

        // Salle au trésor : moquette, chandelles, coffres, et souvent des gardes d'élite.
        private static void FurnishTreasure(RoomCtx c)
        {
            int tier = ChestTier(c);
            int chests = c.Rand.Next(2, 5);
            PlaceContainers(c, OBJ_CHEST, chests, Zone.Any, LootTheme.Treasure, LootTheme.Gear, tier, tier == 2 ? 5 : 3, tier == 2 ? 8 : 6, 0.3f, 3);
            PlaceMany(c, OBJ_POT, 2, Zone.Edge, 1, 3);
            PlaceBanners(c, 2, Zone.Edge, 4);
            PlaceDecor(c, OBJ_CANDLE, 5, Zone.Any);
            PlaceWallTorches(c, 4, 4);

            if (c.Rand.NextDouble() < 0.6)
                SpawnMonsters(c, c.Rand.Next(2, 4), MIX_CRYPT, elites: 1);
        }

        // Salle de garde : couverts (rochers, caisses), meute de monstres variés, parfois un capitaine.
        private static void FurnishGuard(RoomCtx c)
        {
            PlaceMany(c, OBJ_ROCK_BLOCK, c.Rand.Next(3, 7), Zone.Inner, 1, 3);
            PlaceContainers(c, OBJ_CRATE, c.Rand.Next(1, 3), Zone.Edge, LootTheme.Supplies, LootTheme.Food, 0, 1, 3, 0.05f, 3);
            PlaceContainers(c, OBJ_BARREL, c.Rand.Next(1, 3), Zone.Edge, LootTheme.Food, LootTheme.Supplies, 0, 1, 3, 0f, 3);
            PlaceMany(c, OBJ_POT, 1, Zone.Edge);
            PlaceWallTorches(c, 3, 5);

            if (c.Rand.NextDouble() < 0.3)
                PlaceContainers(c, OBJ_CHEST, 1, Zone.Edge, LootTheme.Supplies, LootTheme.Gear, ChestTier(c), 3, 5, 0.3f);

            int elites = c.Rand.NextDouble() < 0.35 ? 1 : 0;
            SpawnMonsters(c, MonsterCount(c, 22, 3f, 3, 11), MIX_GUARD, elites);
        }

        // Caserne : bancs, tables, porte-armures, râteliers d'armes, garnison de gobelins/squelettes.
        private static void FurnishBarracks(RoomCtx c)
        {
            PlaceMany(c, OBJ_BENCH, c.Rand.Next(2, 4), Zone.Edge, 2, 5);
            var tables = PlaceMany(c, OBJ_TABLE, c.Rand.Next(1, 3), Zone.Inner, 2, 5);
            foreach (var t in tables) PlaceAround(c, t, OBJ_CHAIR, 2);
            PlaceMany(c, OBJ_ARMOR_STAND, c.Rand.Next(2, 5), Zone.Edge, 1, 3);
            PlaceContainers(c, OBJ_SHELF, 2, Zone.Edge, LootTheme.Gear, LootTheme.Supplies, 1, 2, 4, 0.6f, 3);
            PlaceContainers(c, OBJ_BARREL, 2, Zone.Edge, LootTheme.Food, LootTheme.Supplies, 0, 1, 3, 0f, 2);
            PlaceContainers(c, OBJ_CRATE, 1, Zone.Edge, LootTheme.Supplies, LootTheme.Gear, 1, 1, 3, 0.25f, 2);
            PlaceBanners(c, 2, Zone.Edge, 5);
            PlaceWallTorches(c, 5, 4);

            if (c.Rand.NextDouble() < 0.4)
                PlaceContainers(c, OBJ_CHEST, 1, Zone.Edge, LootTheme.Gear, LootTheme.Supplies, ChestTier(c), 3, 6, 0.5f);

            SpawnMonsters(c, MonsterCount(c, 26, 3f, 4, 9), MIX_BARRACKS, elites: 1);
        }

        // Crypte : niches de pierre, urnes, bougies, nuée de squelettes, coffre de "mobilier funéraire".
        private static void FurnishCrypt(RoomCtx c)
        {
            PlaceMany(c, OBJ_ROCK_BLOCK, c.Rand.Next(6, 10), Zone.Edge, 1, 2);
            PlaceMany(c, OBJ_POT, c.Rand.Next(3, 6), Zone.Any, 1, 3);
            PlaceMany(c, OBJ_TIKI, 1, Zone.Inner, 1, 4);
            PlaceDecor(c, OBJ_CANDLE, c.Rand.Next(5, 9), Zone.Any);
            PlaceWallTorches(c, 2, 6); // volontairement sombre

            PlaceContainers(c, OBJ_CHEST, 1, Zone.Any, LootTheme.Grave, LootTheme.Treasure, ChestTier(c), 3, 6, 0.3f);

            SpawnMonsters(c, MonsterCount(c, 24, 3f, 4, 10), MIX_CRYPT, elites: 1);
        }

        // Bibliothèque : étagères pleines de livres/parchemins, tables de lecture, lampadaires.
        private static void FurnishLibrary(RoomCtx c)
        {
            PlaceContainers(c, OBJ_SHELF, c.Rand.Next(5, 9), Zone.Edge, LootTheme.Books, LootTheme.Alchemy, 1, 2, 4, 0.05f, 2);
            var tables = PlaceMany(c, OBJ_TABLE, c.Rand.Next(1, 3), Zone.Inner, 2, 5);
            foreach (var t in tables) PlaceAround(c, t, OBJ_CHAIR, 2);
            PlaceMany(c, OBJ_COFFEE_TABLE, 1, Zone.Inner, 1, 4);
            if (c.Rand.NextDouble() < 0.5) PlaceMany(c, OBJ_CLOCK, 1, Zone.Edge);
            PlaceDecor(c, OBJ_FLOOR_LAMP, 2, Zone.Edge);
            PlaceDecor(c, OBJ_CANDLE, 3, Zone.Any);
            PlaceWallTorches(c, 2, 6);

            if (c.Rand.NextDouble() < 0.7)
                SpawnMonsters(c, MonsterCount(c, 45, 2f, 1, 5), MIX_LIBRARY);
        }

        // Réserve : tonneaux, caisses, boîtes... beaucoup de petits conteneurs, peu de danger.
        private static void FurnishStorage(RoomCtx c)
        {
            PlaceContainers(c, OBJ_BARREL, c.Rand.Next(3, 6), Zone.Any, LootTheme.Food, LootTheme.Supplies, 0, 1, 4, 0f, 2);
            PlaceContainers(c, OBJ_CRATE, c.Rand.Next(3, 6), Zone.Any, LootTheme.Supplies, LootTheme.Food, 0, 1, 4, 0.05f, 2);
            PlaceContainers(c, OBJ_BOX, c.Rand.Next(2, 4), Zone.Any, LootTheme.Supplies, LootTheme.Alchemy, 0, 1, 2, 0f, 2);
            PlaceContainers(c, OBJ_SHELF, c.Rand.Next(1, 3), Zone.Edge, LootTheme.Supplies, LootTheme.Smithing, 0, 2, 4, 0f, 3);
            PlaceMany(c, OBJ_POT, 2, Zone.Edge, 1, 3);
            PlaceWallTorches(c, 2, 5);

            if (c.Rand.NextDouble() < 0.35)
                PlaceContainers(c, OBJ_CHEST, 1, Zone.Edge, LootTheme.Supplies, LootTheme.Gear, 1, 3, 5, 0.3f);

            if (c.Rand.NextDouble() < 0.5)
                SpawnMonsters(c, c.Rand.Next(1, 4), MIX_CAVE);
        }

        // Laboratoire d'alchimie : tables, chaudrons, étagères de potions/herbes.
        private static void FurnishAlchemy(RoomCtx c)
        {
            PlaceMany(c, OBJ_ALCHEMY_TABLE, 2, Zone.Inner, 1, 3);
            PlaceMany(c, OBJ_CAULDRON, c.Rand.Next(1, 3), Zone.Inner, 1, 3);
            PlaceContainers(c, OBJ_SHELF, 3, Zone.Edge, LootTheme.Alchemy, LootTheme.Supplies, 1, 2, 4, 0f, 2);
            PlaceContainers(c, OBJ_CRATE, 1, Zone.Edge, LootTheme.Alchemy, LootTheme.Supplies, 0, 1, 3);
            PlaceMany(c, OBJ_POT, 3, Zone.Edge, 1, 2);
            PlaceDecor(c, OBJ_CANDLE, 4, Zone.Any);
            PlaceWallTorches(c, 3, 5);

            SpawnMonsters(c, MonsterCount(c, 30, 2f, 2, 6), MIX_LAB);
        }

        // Forge : enclume, fourneau, établis, minerai et outils ; gardée par des gobelins.
        private static void FurnishForge(RoomCtx c)
        {
            PlaceMany(c, OBJ_FURNACE, 1, Zone.Edge, 2);
            PlaceMany(c, OBJ_ANVIL, 1, Zone.Inner, 2);
            PlaceMany(c, OBJ_WORKBENCH, c.Rand.Next(1, 3), Zone.Edge, 1, 3);
            PlaceMany(c, OBJ_ROCK_BLOCK, 2, Zone.Edge, 1, 4);
            PlaceContainers(c, OBJ_BARREL, 2, Zone.Edge, LootTheme.Smithing, LootTheme.Supplies, 0, 1, 3, 0f, 2);
            PlaceContainers(c, OBJ_CRATE, 2, Zone.Edge, LootTheme.Smithing, LootTheme.Gear, 1, 2, 4, 0.2f, 2);
            PlaceWallTorches(c, 4, 4);

            if (c.Rand.NextDouble() < 0.4)
                PlaceContainers(c, OBJ_CHEST, 1, Zone.Edge, LootTheme.Smithing, LootTheme.Gear, ChestTier(c), 3, 5, 0.5f);

            SpawnMonsters(c, MonsterCount(c, 32, 2f, 2, 6), MIX_FORGE, elites: c.Rand.NextDouble() < 0.4 ? 1 : 0);
        }

        // Sanctuaire : dais de moquette, pylônes, totem, bougies en cercle ; gardé par des élites.
        private static void FurnishShrine(RoomCtx c)
        {
            int cx = c.Center.x, cy = c.Center.y;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                Place(c, cx + sx * 4, cy + sy * 4, OBJ_PYLON);

            PlaceMany(c, OBJ_TIKI, 2, Zone.Edge, 1, 5);
            PlaceBanners(c, c.Rand.Next(2, 5), Zone.Edge, 4);

            // Cercle de bougies autour du dais central.
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI * 2 / 12;
                var p = (cx + (int)Math.Round(Math.Cos(a) * 3.2), cy + (int)Math.Round(Math.Sin(a) * 3.2));
                if (!c.TileSet.Contains(p) || c.Occupied.Contains(p) || c.Decor.Contains(p)) continue;
                c.Decor.Add(p);
                World.AddPlacedObject(c.OffX + p.Item1, c.OffY + p.Item2, OBJ_CANDLE);
            }
            PlaceWallTorches(c, 4, 4);

            if (c.Rand.NextDouble() < 0.7)
                PlaceContainers(c, OBJ_CHEST, 1, Zone.Edge, LootTheme.Treasure, LootTheme.Books, ChestTier(c), 3, 6, 0.3f);

            SpawnMonsters(c, MonsterCount(c, 32, 3f, 3, 7), MIX_CRYPT, elites: 2);
        }

        // Caverne de champignons : lumineux, stalagmites, champignons à cueillir, araignées et chauves-souris.
        private static void FurnishMushroom(RoomCtx c)
        {
            PlaceMany(c, OBJ_GLOW_MUSHROOM, c.Rand.Next(8, 15), Zone.Any, 1, 2);
            PlaceMany(c, OBJ_STALAGMITE, c.Rand.Next(5, 10), Zone.Any, 1, 2);
            PlaceMany(c, OBJ_GIANT_MUSHROOM, c.Rand.Next(1, 3), Zone.Inner, 2, 5);
            PlaceDecor(c, OBJ_MUSHROOM, c.Rand.Next(6, 11), Zone.Any); // ramassables

            if (c.Rand.NextDouble() < 0.35)
                PlaceContainers(c, OBJ_BOX, 1, Zone.Edge, LootTheme.Alchemy, LootTheme.Supplies, 0, 2, 3);

            SpawnMonsters(c, MonsterCount(c, 28, 3f, 3, 9), MIX_MUSHROOM);
        }

        // Salle à piliers : damier au sol, colonnes en quinconce, bannières, embuscade de monstres.
        private static void FurnishPillarHall(RoomCtx c)
        {
            var cell = c.Cell;
            for (int x = cell.RoomX + 2; x < cell.RoomX + cell.RoomW - 1; x += 4)
                for (int y = cell.RoomY + 2; y < cell.RoomY + cell.RoomH - 1; y += 4)
                    Place(c, x, y, OBJ_PYLON);

            PlaceBanners(c, c.Rand.Next(3, 6), Zone.Edge, 4);
            PlaceContainers(c, OBJ_CRATE, 1, Zone.Edge, LootTheme.Supplies, LootTheme.Gear, 1, 1, 3, 0.2f);
            PlaceWallTorches(c, 4, 4);

            if (c.Rand.NextDouble() < 0.3)
                PlaceContainers(c, OBJ_CHEST, 1, Zone.Edge, LootTheme.Treasure, LootTheme.Supplies, ChestTier(c), 3, 5, 0.3f);

            SpawnMonsters(c, MonsterCount(c, 28, 3f, 3, 9), MIX_CAVE, elites: c.Rand.NextDouble() < 0.3 ? 1 : 0);
        }

        // Havre : puits, bancs, chaises, lampadaire. Presque toujours sans danger.
        private static void FurnishFountain(RoomCtx c)
        {
            // Le puits est large (3x1) : on cherche un emplacement dégagé hors du centre.
            PlaceMany(c, OBJ_WELL, 1, Zone.Inner, 3);
            PlaceMany(c, OBJ_BENCH, 2, Zone.Edge, 2, 5);
            PlaceMany(c, OBJ_CHAIR, 2, Zone.Edge, 1, 3);
            PlaceMany(c, OBJ_POT, 3, Zone.Edge, 1, 3);
            PlaceDecor(c, OBJ_FLOOR_LAMP, 2, Zone.Edge);
            PlaceDecor(c, OBJ_CANDLE, 3, Zone.Any);
            PlaceWallTorches(c, 3, 5);

            if (c.Rand.NextDouble() < 0.3)
                PlaceContainers(c, OBJ_CHEST, 1, Zone.Edge, LootTheme.Food, LootTheme.Alchemy, 0, 2, 4);

            if (c.Rand.NextDouble() < 0.15)
                SpawnMonsters(c, 1, MIX_CAVE);
        }

        // ═══════════════════════════════════════════════════════════════
        // 5. COULOIRS : patrouilles et petits accessoires
        // ═══════════════════════════════════════════════════════════════
        private static void PopulateCorridors(DungeonInstance instance, DungeonConfig config, Random rand, int maxDepth)
        {
            int offX = instance.OriginX, offY = instance.OriginY;
            var corridorProps = new[] { OBJ_POT, OBJ_BARREL, OBJ_CRATE, OBJ_BOX, OBJ_CANDLE };

            foreach (var cor in config.Corridors)
            {
                float diff = Math.Min(cor.A.Depth, cor.B.Depth) / (float)maxDepth;

                // Patrouille : un monstre au milieu du couloir (jamais près de la salle de départ).
                if (cor.A.Depth > 0 && cor.B.Depth > 0 && rand.NextDouble() < CORRIDOR_PATROL_CHANCE * MONSTER_DENSITY)
                {
                    var (mx, my) = ((cor.Verts[1].x + cor.Verts[2].x) / 2, (cor.Verts[1].y + cor.Verts[2].y) / 2);
                    if (mx >= 0 && my >= 0 && mx < config.Width && my < config.Height
                        && config.IsFloor[mx, my] && !config.IsRoomTile[mx, my])
                    {
                        string sp = new[] { "bat", "skeleton", "spider" }[rand.Next(3)];
                        CreateMonster(offX + mx, offY + my, sp, sp, diff, rand.NextDouble() < ELITE_CHANCE * (0.5f + diff));
                    }
                }

                // Accessoires : sur le bas-côté des tronçons rectilignes.
                if (rand.NextDouble() >= CORRIDOR_PROP_CHANCE) continue;
                int props = rand.Next(1, 3);
                for (int seg = 0; seg < cor.Verts.Count - 1 && props > 0; seg++)
                {
                    var (x0, y0) = cor.Verts[seg];
                    var (x1, y1) = cor.Verts[seg + 1];
                    int len = Math.Abs(x1 - x0) + Math.Abs(y1 - y0);
                    if (len < 8) continue;
                    int dx = Math.Sign(x1 - x0), dy = Math.Sign(y1 - y0);
                    int step = rand.Next(3, len - 2);
                    int side = rand.Next(2) == 0 ? -1 : 1;
                    int px = x0 + dx * step + (dy != 0 ? side : 0);
                    int py = y0 + dy * step + (dx != 0 ? side : 0);

                    if (px < 0 || py < 0 || px >= config.Width || py >= config.Height) continue;
                    if (!config.IsFloor[px, py] || config.IsRoomTile[px, py]) continue;
                    if (World.GetObjectIdAt(offX + px, offY + py) != 0) continue;

                    int id = corridorProps[rand.Next(corridorProps.Length)];
                    World.AddPlacedObject(offX + px, offY + py, id);
                    if (id == OBJ_BARREL || id == OBJ_CRATE || id == OBJ_BOX)
                        AddContainerLoot(config, offX + px, offY + py, id, rand, LootTheme.Supplies, LootTheme.Food, 0, 1, 3);
                    props--;
                }
            }
        }
    }
}
