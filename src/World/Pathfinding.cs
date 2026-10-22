using System.Numerics;

namespace Soulfract
{
    public static class Pathfinder
    {
        private const int NODE_SIZE = 40;
        private const int DEFAULT_SEARCH_MARGIN = 8;
        // Protection contre explorations excessives (évite gel sur grosses demandes simultanées)
        private const int MAX_NODE_EXPANSIONS = 3000;

        // Le pathfinding est synchrone et s'exécute sur le thread principal. Même avec
        // des timers individuels, plusieurs PNJ peuvent donc lancer un A* dans la même
        // frame et provoquer un pic de temps CPU. Les demandes excédentaires sont différées
        // par Entity.Update plutôt que traitées comme des chemins introuvables.
        //  CORRECTIF FAMINE : 2 requêtes/frame suffisaient en usage isolé mais, dès qu'un
        // groupe d'entités (village, horde de gobelins, troupeau) demandait toutes un
        // recalcul la même frame, seules les 2 dernières de la liste globale (voir l'ordre
        // de boucle dans Program.Update) obtenaient leur chemin. Les autres échouaient,
        // retentaient très vite (voir le délai de retry côté Entity), mais dans le même
        // ordre à chaque fois : les mêmes entités gagnaient indéfiniment le quota pendant
        // que les autres restaient plantées sur place sans jamais recalculer de chemin -
        // ce qui donnait l'impression d'entités visibles mais figées. On relève le budget
        // pour réduire fortement la fréquence de ces conflits.
        private const int MAX_REQUESTS_PER_TIMESLICE = 8;
        private const double PATH_REQUEST_TIMESLICE = 1.0 / 60.0;
        private static double _pathRequestSliceStart;
        private static int _pathRequestsInSlice;

        public static bool TryReservePathRequest()
        {
            double now = Raylib_cs.Raylib.GetTime();
            if (now - _pathRequestSliceStart >= PATH_REQUEST_TIMESLICE)
            {
                _pathRequestSliceStart = now;
                _pathRequestsInSlice = 0;
            }

            if (_pathRequestsInSlice >= MAX_REQUESTS_PER_TIMESLICE)
                return false;

            _pathRequestsInSlice++;
            return true;
        }

        //  OPTIMISATION PERF (villages) : GetHitboxBlockedTiles était recalculé intégralement
        // (rescan de toute la fenêtre de recherche, jusqu'à 64x64 tuiles) à CHAQUE appel de
        // FindPath, y compris quand plusieurs PNJ voisins demandent un chemin dans des zones qui
        // se recouvrent largement au même moment (cas typique d'un village avec ~130 entités).
        // On découpe maintenant le monde en cellules de cache fixes : le résultat de
        // World.GetHitboxBlockedTiles pour une cellule est réutilisé pendant CACHE_TTL secondes
        // par TOUTES les entités dont la fenêtre de recherche recouvre cette cellule, au lieu
        // d'être recalculé individuellement. Le TTL court garantit qu'un objet cassé/posé est
        // pris en compte quasi immédiatement (léger délai de perception, sans impact gameplay).
        private const int CACHE_CELL = 24;
        private const float CACHE_TTL = 0.25f;
        private static readonly Dictionary<(int cx, int cy), (HashSet<(int x, int y)> tiles, double time)> _blockedTilesCache = new();

        //  OPTIMISATION PERF : quand la fenêtre demandée tient entièrement dans UNE seule
        // cellule de cache déjà à jour (cas très fréquent : petites entités, faible clearance),
        // on renvoie directement le HashSet de la cellule sans le recopier dans un nouveau
        // HashSet. Avant, chaque appel de FindPath (même en cache-hit complet) réallouait un
        // HashSet et faisait un UnionWith - un coût inutile répété à chaque recalcul de chaque
        // PNJ. Le HashSet de cellule n'est jamais muté après coup (on en crée un nouveau plutôt
        // que de le modifier en place), donc le renvoyer directement sans copie est sûr.
        private static HashSet<(int x, int y)> GetHitboxBlockedTilesCached(int minX, int maxX, int minY, int maxY, HashSet<string> destroyedObjects)
        {
            int cellMinX = (int)Math.Floor((float)minX / CACHE_CELL);
            int cellMaxX = (int)Math.Floor((float)maxX / CACHE_CELL);
            int cellMinY = (int)Math.Floor((float)minY / CACHE_CELL);
            int cellMaxY = (int)Math.Floor((float)maxY / CACHE_CELL);

            double now = Raylib_cs.Raylib.GetTime();

            HashSet<(int x, int y)> GetOrBuildCell(int cx, int cy)
            {
                if (!_blockedTilesCache.TryGetValue((cx, cy), out var entry) || now - entry.time > CACHE_TTL)
                {
                    int tx0 = cx * CACHE_CELL;
                    int tx1 = tx0 + CACHE_CELL - 1;
                    int ty0 = cy * CACHE_CELL;
                    int ty1 = ty0 + CACHE_CELL - 1;
                    var tiles = World.GetHitboxBlockedTiles(tx0, tx1, ty0, ty1, destroyedObjects);
                    entry = (tiles, now);
                    _blockedTilesCache[(cx, cy)] = entry;
                }
                return entry.tiles;
            }

            //  Fast path : fenêtre entièrement contenue dans une seule cellule -> pas de copie.
            HashSet<(int x, int y)> result;
            if (cellMinX == cellMaxX && cellMinY == cellMaxY)
            {
                result = GetOrBuildCell(cellMinX, cellMinY);
            }
            else
            {
                result = new HashSet<(int x, int y)>();
                for (int cx = cellMinX; cx <= cellMaxX; cx++)
                {
                    for (int cy = cellMinY; cy <= cellMaxY; cy++)
                    {
                        var cellTiles = GetOrBuildCell(cx, cy);
                        if (cellTiles.Count > 0)
                            result.UnionWith(cellTiles);
                    }
                }
            }

            //  Purge périodique : sans ça, _blockedTilesCache grossirait indéfiniment au fil
            // d'une longue session au fur et à mesure que le joueur explore de nouvelles zones
            // (chaque nouvelle cellule visitée y reste pour toujours sinon). On ne purge pas à
            // chaque appel (trop coûteux vu la fréquence des FindPath) mais toutes les ~5s.
            if (now - _lastCachePurge > 5.0)
            {
                _lastCachePurge = now;
                var stale = new List<(int cx, int cy)>();
                foreach (var kv in _blockedTilesCache)
                {
                    if (now - kv.Value.time > CACHE_TTL * 4)
                        stale.Add(kv.Key);
                }
                foreach (var k in stale)
                    _blockedTilesCache.Remove(k);
            }

            return result;
        }
        private static double _lastCachePurge = 0.0;

        //  OPTIMISATION PERF : destroyedObjects est un HashSet<string> avec des clés
        // "x_y" formatées à la volée. IsSingleTileWalkable en construisait une par tuile testée,
        // soit jusqu'à MAX_NODE_EXPANSIONS x 8 voisins = ~24 000 allocations de string par appel
        // de FindPath. On convertit maintenant destroyedObjects UNE SEULE FOIS par appel en un
        // HashSet<(int,int)> (le set est en général petit : quelques objets cassés), qu'on
        // consulte ensuite en O(1) sans aucune allocation pendant toute l'exploration A*.
        private static HashSet<(int x, int y)> ParseDestroyedSet(HashSet<string> destroyedObjects)
        {
            if (destroyedObjects.Count == 0)
                return EmptyDestroyed;

            var set = new HashSet<(int x, int y)>(destroyedObjects.Count);
            foreach (var key in destroyedObjects)
            {
                int sep = key.IndexOf('_');
                if (sep <= 0) continue;
                if (int.TryParse(key.AsSpan(0, sep), out int x) && int.TryParse(key.AsSpan(sep + 1), out int y))
                    set.Add((x, y));
            }
            return set;
        }
        private static readonly HashSet<(int x, int y)> EmptyDestroyed = new();

        //  OPTIMISATION PERF : offsets des 8 voisins pré-alloués une seule fois au lieu de
        // construire une nouvelle List<(int,int)> à chaque nœud exploré par l'A* (jusqu'à 3000
        // nœuds par appel de FindPath).
        private static readonly (int dx, int dy)[] NeighborOffsets = new (int, int)[]
        {
            (-1, -1), (0, -1), (1, -1),
            (-1,  0),          (1,  0),
            (-1,  1), (0,  1), (1,  1),
        };
        //  Il n'existe pas d'objet "porte" séparé dans ce jeu (voir World._doorTiles) :
        // l'ancienne exception générale sur l'ID 84 laissait passer TOUS les murs en bois,
        // pas seulement la porte d'une maison précise. On interroge maintenant World pour
        // ne percer que la tuile "porte" réellement enregistrée pour la maison concernée.

        //  BUG CORRIGÉ (décalage vertical d'une case) : toutes les collisions réelles
        // (World.CheckCollision / IsCollidingEntity) testent la hitbox de l'entité à partir de
        // ses PIEDS, c'est-à-dire WorldPos.Y + Program.FeetOffsetY (32px), et non WorldPos.Y
        // brut. Le pathfinding, lui, convertissait WorldPos directement en case (Y / NODE_SIZE)
        // sans ce décalage : avec FeetOffsetY = 32 et NODE_SIZE = 40, ça représente presque une
        // case entière d'écart entre "la case où le pathfinding pense que se trouve l'entité" et
        // "la case où ses pieds (donc sa vraie hitbox) se trouvent". D'où les chemins qui
        // semblaient corrects mais réagissaient à un obstacle placé une case plus haut que la
        // ligne affichée. On centralise donc ici la conversion monde -> case en tenant compte
        // des pieds, et son inverse (case -> point monde) pour que les cibles de déplacement
        // restent cohérentes avec WorldPos (référentiel utilisé par Entity pour se déplacer).
        private static int WorldYToFeetTileY(float worldY) => (int)Math.Floor((worldY + Program.FeetOffsetY) / NODE_SIZE);
        private static float FeetTileYToWorldY(int tileY) => (tileY * NODE_SIZE + NODE_SIZE / 2f) - Program.FeetOffsetY;

        //  Vérification légère (une seule tuile, sans recalculer tout le hitboxBlocked de la
        // zone) utilisée par Entity.MoveTowardTarget pour revalider le PROCHAIN nœud du chemin
        // actuel à chaque frame, au lieu d'attendre jusqu'à PATH_RECALC_INTERVAL (0.5s) pour
        // s'apercevoir qu'un objet a été posé/détruit entre-temps sur la case visée. Ça évite
        // qu'un PNJ continue de foncer vers un nœud qui vient de devenir un mur.
        public static bool IsWorldPositionWalkable(Vector2 worldPos, HashSet<string> destroyedObjects, string species = "")
        {
            int tileX = (int)Math.Floor(worldPos.X / NODE_SIZE);
            int tileY = WorldYToFeetTileY(worldPos.Y);
            int clearance = GetClearanceTiles(species);
            var hitboxBlocked = GetHitboxBlockedTilesCached(tileX - 1 - clearance, tileX + 1 + clearance, tileY - 1 - clearance, tileY + 1 + clearance, destroyedObjects);
            var destroyedSet = ParseDestroyedSet(destroyedObjects);
            return IsWalkableSlow(tileX, tileY, destroyedSet, hitboxBlocked, clearance);
        }

        public static Vector2? FindNearestWalkablePosition(Vector2 origin, HashSet<string> destroyedObjects, string species = "", int maxRadiusTiles = 10)
        {
            int originX = (int)Math.Floor(origin.X / NODE_SIZE);
            int originY = WorldYToFeetTileY(origin.Y);
            var queue = new Queue<(int x, int y)>();
            var visited = new HashSet<(int x, int y)>();
            queue.Enqueue((originX, originY));
            visited.Add((originX, originY));

            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                int distance = Math.Max(Math.Abs(tile.x - originX), Math.Abs(tile.y - originY));
                if (distance > maxRadiusTiles)
                    continue;

                Vector2 candidate = new Vector2(
                    tile.x * NODE_SIZE + NODE_SIZE / 2f,
                    FeetTileYToWorldY(tile.y));

                if ((tile.x != originX || tile.y != originY) &&
                    IsWorldPositionWalkable(candidate, destroyedObjects, species) &&
                    !World.IsCollidingEntity(candidate, destroyedObjects, species))
                    return candidate;

                foreach (var (dx, dy) in NeighborOffsets)
                {
                    var next = (tile.x + dx, tile.y + dy);
                    if (visited.Contains(next)) continue;
                    if (Math.Max(Math.Abs(next.Item1 - originX), Math.Abs(next.Item2 - originY)) > maxRadiusTiles) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            return null;
        }

        public static List<Vector2> FindPath(Vector2 start, Vector2 target, HashSet<string> destroyedObjects, int searchMargin = DEFAULT_SEARCH_MARGIN, string species = "")
        {
            //  Math.Floor plutôt qu'un simple cast : un cast tronque vers zéro, ce qui décale
            // d'une tuile toute position à coordonnée négative par rapport à la grille utilisée
            // partout ailleurs (World.IsCollidingEntity, CheckCollision, etc. utilisent tous
            // Math.Floor). Ce décalage désynchronisait la carte du pathfinding de la vraie carte
            // de collision et provoquait des chemins qui semblaient valides mais menaient dans un mur.
            //  Y converti via WorldYToFeetTileY (voir commentaire plus haut) pour s'aligner sur
            // la case réellement occupée par les pieds de l'entité, pas son ancre visuelle.
            int startX = (int)Math.Floor(start.X / NODE_SIZE);
            int startY = WorldYToFeetTileY(start.Y);
            int targetX = (int)Math.Floor(target.X / NODE_SIZE);
            int targetY = WorldYToFeetTileY(target.Y);

            if (startX == targetX && startY == targetY)
                return new List<Vector2>();

            searchMargin = Math.Clamp(searchMargin, 4, 32);

            int clearance = GetClearanceTiles(species);

            var path = FindPathInternal(start, target, destroyedObjects, startX, startY, targetX, targetY, searchMargin, clearance);
            if (path != null)
                return path;

            //  Aucun chemin trouvé : on ne renvoie plus une ligne droite vers la cible (ça
            // forçait le PNJ à foncer indéfiniment dans l'obstacle qui bloquait justement la
            // recherche). Une liste vide indique clairement à l'appelant qu'il n'y a pas de
            // route possible ; on ne retente pas avec une fenêtre élargie ici (trop coûteux à
            // faire systématiquement) — c'est à l'appelant de décider s'il veut réessayer plus
            // tard ou choisir une autre destination.
            return new List<Vector2>();
        }

        // ═══════════════════════════════════════════════════════════════════
        // OPTIMISATION PERF (lag de l'IA de pathfinding) : deux gains majeurs, ci-dessous.
        //
        //   1) GRILLE DE MARCHABILITÉ PRÉCALCULÉE ("BlockedGrid") : avant, IsWalkable(clearance)
        //      était appelée pour CHAQUE nœud exploré ET pour les deux vérifications anti-angle
        //      diagonal, et re-testait à chaque fois jusqu'à 9 tuiles individuellement (grille
        //      3x3 pour clearance=1), chacune impliquant 2 requêtes World (GetObjectIdAt,
        //      GetGroundTileIdAt) + 2 lookups de registre. Pour une entité large (loup, gobelin
        //      monté...) explorant jusqu'à MAX_NODE_EXPANSIONS=3000 nœuds, ça pouvait dépasser
        //      500 000 requêtes World par recalcul de chemin. On calcule maintenant, UNE SEULE
        //      FOIS par appel de FindPath, un tableau dense de booléens "bloqué" pour toute la
        //      fenêtre de recherche (dilatée du clearance) : chaque tuile de base n'est testée
        //      qu'une fois, et la dilatation (prise en compte du clearance) se fait ensuite par
        //      simple lecture de voisins déjà connus - donc O(1) par nœud pendant tout le A*,
        //      au lieu de O(9) répété à chaque exploration ET chaque vérif diagonale.
        //
        //   2) BUFFERS A* RÉUTILISÉS (pas de réallocation par appel) : openSet (PriorityQueue),
        //      closedSet (HashSet) et nodes (Dictionary), ainsi que les objets Node eux-mêmes,
        //      étaient tous réalloués à chaque appel de FindPath. Avec ~130 PNJ dans un village
        //      qui relancent leur A* en rythme (même étalé/jitterisé), ça représentait une
        //      pression GC constante. Le pathfinding s'exécute toujours de façon synchrone sur
        //      le thread principal (jamais deux FindPath en parallèle) : on peut donc réutiliser
        //      en toute sécurité des buffers statiques, vidés (Clear) au début de chaque appel
        //      plutôt que réalloués.
        // ═══════════════════════════════════════════════════════════════════

        private struct NodeEntry
        {
            public Node Node;
            public float Priority;

            public NodeEntry(Node node, float priority)
            {
                Node = node;
                Priority = priority;
            }
        }

        private static readonly PriorityQueue<NodeEntry, float> _sharedOpenSet = new();
        private static readonly HashSet<(int x, int y)> _sharedClosedSet = new();
        private static readonly Dictionary<(int x, int y), Node> _sharedNodes = new();
        private static readonly Stack<Node> _nodePool = new();

        private static Node RentNode(int x, int y)
        {
            Node node = _nodePool.Count > 0 ? _nodePool.Pop() : new Node();
            node.X = x;
            node.Y = y;
            node.G = float.MaxValue;
            node.H = 0f;
            node.F = 0f;
            node.Parent = null;
            return node;
        }

        //  Rend tous les Node du dernier A* au pool, pour réutilisation par le prochain appel.
        // À appeler seulement après avoir fini d'exploiter `nodes` (ReconstructPath doit avoir
        // déjà copié tout ce dont il a besoin - Node.Parent - dans la liste de Vector2 renvoyée).
        private static void ReleaseNodesToPool()
        {
            foreach (var node in _sharedNodes.Values)
                _nodePool.Push(node);
            _sharedNodes.Clear();
        }

        //  Grille dense de marchabilité (avant dilatation clearance), indexée
        // [x - OffsetX, y - OffsetY]. Portée sur la fenêtre de recherche étendue du clearance
        // (pour que la dilatation aux bords de la fenêtre reste correcte).
        private sealed class BlockedGrid
        {
            public int OffsetX, OffsetY, Width, Height;
            public bool[] Base = Array.Empty<bool>();       // true = tuile bloquée (avant clearance)
            public bool[] Dilated = Array.Empty<bool>();     // true = bloquée en tenant compte du clearance

            public bool IsBlocked(int x, int y)
            {
                int ix = x - OffsetX, iy = y - OffsetY;
                if ((uint)ix >= (uint)Width || (uint)iy >= (uint)Height)
                    return true; // hors fenêtre précalculée -> traité comme bloqué (comportement prudent)
                return Dilated[ix * Height + iy];
            }
        }

        private static BlockedGrid BuildBlockedGrid(int minX, int maxX, int minY, int maxY, int clearance,
            HashSet<(int x, int y)> destroyedObjects, HashSet<(int x, int y)> hitboxBlocked)
        {
            int offsetX = minX - clearance;
            int offsetY = minY - clearance;
            int width = (maxX + clearance) - offsetX + 1;
            int height = (maxY + clearance) - offsetY + 1;

            var grid = new BlockedGrid
            {
                OffsetX = offsetX,
                OffsetY = offsetY,
                Width = width,
                Height = height,
                Base = new bool[width * height],
            };

            for (int ix = 0; ix < width; ix++)
            {
                int tileX = offsetX + ix;
                for (int iy = 0; iy < height; iy++)
                {
                    int tileY = offsetY + iy;
                    grid.Base[ix * height + iy] = !IsSingleTileWalkable(tileX, tileY, destroyedObjects, hitboxBlocked);
                }
            }

            if (clearance <= 0)
            {
                //  Pas de dilatation nécessaire : la grille "avec clearance" == la grille de base.
                grid.Dilated = grid.Base;
                return grid;
            }

            grid.Dilated = new bool[width * height];
            for (int ix = 0; ix < width; ix++)
            {
                for (int iy = 0; iy < height; iy++)
                {
                    // Une case ne devient "libre avec clearance" que si elle-même ET toutes ses
                    // voisines dans le rayon `clearance` (déjà présentes dans la grille, celle-ci
                    // ayant été étendue de `clearance` de chaque côté) sont libres.
                    bool blocked = false;
                    for (int dx = -clearance; dx <= clearance && !blocked; dx++)
                    {
                        int nx = ix + dx;
                        if ((uint)nx >= (uint)width) { blocked = true; break; }
                        for (int dy = -clearance; dy <= clearance; dy++)
                        {
                            int ny = iy + dy;
                            if ((uint)ny >= (uint)height || grid.Base[nx * height + ny])
                            {
                                blocked = true;
                                break;
                            }
                        }
                    }
                    grid.Dilated[ix * height + iy] = blocked;
                }
            }

            return grid;
        }

        private static List<Vector2>? FindPathInternal(Vector2 start, Vector2 target, HashSet<string> destroyedObjects,
            int startX, int startY, int targetX, int targetY, int searchMargin, int clearance)
        {
            int minX = Math.Min(startX, targetX) - searchMargin;
            int maxX = Math.Max(startX, targetX) + searchMargin;
            int minY = Math.Min(startY, targetY) - searchMargin;
            int maxY = Math.Max(startY, targetY) + searchMargin;

            //  Calculé UNE SEULE FOIS pour toute la zone de recherche (voir
            // World.GetHitboxBlockedTiles) : les tuiles bloquées par une hitbox d'objet qui
            // déborde de sa case d'ancrage. Consulté ensuite en O(1) par case testée au lieu de
            // rescanner le voisinage à chaque fois (ce qui faisait exploser le coût du A*).
            //  Version mise en cache par cellules, partagée entre toutes les entités qui
            // pathfindent dans la même zone au même moment (voir GetHitboxBlockedTilesCached).
            //  Bornes étendues du clearance : la grille dilatée (BuildBlockedGrid) a besoin de
            // connaître la marchabilité de base jusqu'à `clearance` cases au-delà de la fenêtre
            // de recherche pour dilater correctement les bords de celle-ci.
            var hitboxBlocked = GetHitboxBlockedTilesCached(minX - clearance, maxX + clearance, minY - clearance, maxY + clearance, destroyedObjects);
            //  destroyedObjects converti une seule fois ici (au lieu d'une string par tuile
            // testée pendant toute l'exploration A*, voir ParseDestroyedSet).
            var destroyedSet = ParseDestroyedSet(destroyedObjects);

            //  Grille dense précalculée une seule fois pour toute la recherche (voir le bloc de
            // commentaires au-dessus de FindPath) : remplace tous les appels répétés à
            // IsWalkable/IsSingleTileWalkable pendant l'exploration A* par un simple lookup O(1).
            var grid = BuildBlockedGrid(minX, maxX, minY, maxY, clearance, destroyedSet, hitboxBlocked);

            //  Buffers A* réutilisés d'un appel à l'autre (voir commentaire plus haut) : jamais
            // réentrant (le pathfinding tourne toujours de façon synchrone sur le thread
            // principal), donc sûr de les vider plutôt que d'en réallouer de nouveaux.
            var openSet = _sharedOpenSet;
            var closedSet = _sharedClosedSet;
            var nodes = _sharedNodes;
            openSet.Clear();
            closedSet.Clear();
            nodes.Clear();

            var startNode = RentNode(startX, startY);
            startNode.G = 0;
            startNode.H = Heuristic(startX, startY, targetX, targetY);
            startNode.F = startNode.H;
            nodes[(startX, startY)] = startNode;
            openSet.Enqueue(new NodeEntry(startNode, startNode.F), startNode.F);

            List<Vector2>? result = null;

            while (openSet.Count > 0)
            {
                var currentEntry = openSet.Dequeue();
                var current = currentEntry.Node;
                if (closedSet.Contains((current.X, current.Y)))
                    continue;
                if (currentEntry.Priority != current.F)
                    continue;

                // Si on a exploré trop de noeuds, retourner le chemin partiel jusqu'au noeud courant
                if (nodes.Count > MAX_NODE_EXPANSIONS)
                {
                    var partial = ReconstructPath(current, start);
                    result = SimplifyPath(partial);
                    break;
                }

                if (current.X == targetX && current.Y == targetY)
                {
                    var rawPath = ReconstructPath(current, start);
                    result = SimplifyPath(rawPath);
                    break;
                }

                closedSet.Add((current.X, current.Y));

                foreach (var (dx, dy) in NeighborOffsets)
                {
                    var neighbor = (x: current.X + dx, y: current.Y + dy);
                    if (neighbor.x < minX || neighbor.x > maxX || neighbor.y < minY || neighbor.y > maxY)
                        continue;

                    if (closedSet.Contains(neighbor))
                        continue;

                    if (grid.IsBlocked(neighbor.x, neighbor.y))
                        continue;

                    // Empêcher le passage diagonal entre deux murs en angle
                    if (neighbor.x != current.X && neighbor.y != current.Y)
                    {
                        if (grid.IsBlocked(current.X, neighbor.y) || grid.IsBlocked(neighbor.x, current.Y))
                            continue;
                    }

                    // Coût diagonal = 1.4, cardinal = 1
                    float stepCost = (neighbor.x != current.X && neighbor.y != current.Y) ? 1.414f : 1f;
                    float tentativeG = current.G + stepCost;

                    if (!nodes.TryGetValue(neighbor, out var neighborNode))
                    {
                        neighborNode = RentNode(neighbor.x, neighbor.y);
                        nodes[neighbor] = neighborNode;
                    }

                    if (tentativeG < neighborNode.G)
                    {
                        neighborNode.Parent = current;
                        neighborNode.G = tentativeG;
                        neighborNode.H = Heuristic(neighbor.x, neighbor.y, targetX, targetY);
                        neighborNode.F = neighborNode.G + neighborNode.H;
                        openSet.Enqueue(new NodeEntry(neighborNode, neighborNode.F), neighborNode.F);
                    }
                }
            }

            //  Tous les Node loués (RentNode) pendant cette recherche retournent au pool ici,
            // que la recherche ait réussi, échoué, ou été tronquée (nœuds partiels). Le chemin
            // final (result) est déjà une List<Vector2> indépendante des objets Node à ce stade
            // (ReconstructPath les a déjà convertis), donc les réutiliser ne pose aucun risque.
            ReleaseNodesToPool();

            return result; // null si openSet vidé sans jamais atteindre la cible
        }

        private static float Heuristic(int x1, int y1, int x2, int y2)
        {
            return Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
        }

        private static int GetClearanceTiles(string species)
        {
            if (string.IsNullOrEmpty(species))
                return 0;

            Vector2 dim = World.GetEntityDimensions(species);
            float widestSide = Math.Max(dim.X, dim.Y);

            // Au-delà d'environ une tuile de large, exiger que les tuiles voisines immédiates
            // soient également libres pour ne pas router l'entité trop près d'un angle qu'elle
            // ne peut pas physiquement raser (cf. Entity.MoveTowardTarget / nodeArrivalRadius
            // qui applique déjà une marge équivalente pour l'arrivée sur chaque noeud).
            return widestSide > NODE_SIZE * 1.05f ? 1 : 0;
        }

        //  Version "lente" (sans grille précalculée) conservée uniquement pour
        // IsWorldPositionWalkable : c'est un test ponctuel d'une seule tuile (pas une
        // exploration A* de milliers de nœuds), donc l'ancien coût O(9) par appel est
        // négligeable ici et ne justifie pas de construire une grille dédiée.
        private static bool IsWalkableSlow(int tileX, int tileY, HashSet<(int x, int y)> destroyedObjects, HashSet<(int x, int y)> hitboxBlocked, int clearance = 0)
        {
            if (!IsSingleTileWalkable(tileX, tileY, destroyedObjects, hitboxBlocked))
                return false;

            if (clearance <= 0)
                return true;

            for (int dx = -clearance; dx <= clearance; dx++)
            {
                for (int dy = -clearance; dy <= clearance; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (!IsSingleTileWalkable(tileX + dx, tileY + dy, destroyedObjects, hitboxBlocked))
                        return false;
                }
            }

            return true;
        }

        private static bool IsSingleTileWalkable(int tileX, int tileY, HashSet<(int x, int y)> destroyedObjects, HashSet<(int x, int y)> hitboxBlocked)
        {
            //  Plus d'allocation de string ici : destroyedObjects est désormais un
            // HashSet<(int,int)> pré-converti une seule fois par appel de FindPath
            // (voir ParseDestroyedSet), au lieu d'une string "x_y" construite à chaque
            // tuile testée (jusqu'à ~24 000 fois par recherche de chemin auparavant).
            if (destroyedObjects.Contains((tileX, tileY)))
                return true;

            int objectId = World.GetObjectIdAt(tileX, tileY);
            if (objectId != 0 && !World.IsDoorTile(tileX, tileY))
            {
                var tileData = WorldTileRegistry.GetTile(objectId);
                if (tileData != null && !tileData.Walkable)
                    return false;
            }

            int groundId = World.GetGroundTileIdAt(tileX, tileY);
            var groundTile = WorldTileRegistry.GetTile(groundId);
            if (groundTile == null || !groundTile.Walkable)
                return false;

            //  Un objet voisin (arbre, gros meuble, décor...) peut avoir une hitbox réelle qui
            // déborde sur cette tuile même si celle-ci n'a pas d'objectId propre (CollisionSize
            // ou Size > 1 tuile). hitboxBlocked est précalculé une seule fois pour toute la zone
            // de recherche (voir World.GetHitboxBlockedTiles) : lookup O(1) ici, pas de rescan.
            if (hitboxBlocked.Contains((tileX, tileY)))
                return false;

            return true;
        }

        private static List<Vector2> ReconstructPath(Node endNode, Vector2 start)
        {
            var path = new List<Vector2>();
            var current = endNode;
            while (current != null)
            {
                //  Retire le décalage "pieds" appliqué à l'entrée (voir WorldYToFeetTileY) pour
                // que le point renvoyé soit bien exprimé en WorldPos, le référentiel utilisé par
                // Entity.MoveTowardTarget pour déplacer WorldPos directement.
                float centerX = current.X * NODE_SIZE + NODE_SIZE / 2f;
                float centerY = FeetTileYToWorldY(current.Y);
                path.Insert(0, new Vector2(centerX, centerY));
                current = current.Parent;
            }
            if (path.Count > 0)
                path[0] = start;
            return path;
        }

        // Lissage : supprime les points intermédiaires qui sont alignés
        private static List<Vector2> SimplifyPath(List<Vector2> path)
        {
            if (path.Count < 3)
                return path;

            var simplified = new List<Vector2>();
            simplified.Add(path[0]);

            for (int i = 1; i < path.Count - 1; i++)
            {
                Vector2 prev = simplified[simplified.Count - 1];
                Vector2 curr = path[i];
                Vector2 next = path[i + 1];

                // Vérifier si (prev, curr, next) sont alignés (produit vectoriel ~ 0)
                float cross = (curr.X - prev.X) * (next.Y - curr.Y) - (curr.Y - prev.Y) * (next.X - curr.X);
                if (Math.Abs(cross) > 0.1f)
                    simplified.Add(curr);
            }
            simplified.Add(path[path.Count - 1]);
            return simplified;
        }

        //  Plus de constructeur qui force G = float.MaxValue à l'allocation : les Node sont
        // désormais réutilisés via un pool (RentNode), qui réinitialise explicitement tous les
        // champs à chaque location. Le constructeur par défaut suffit donc.
        private class Node
        {
            public int X, Y;
            public float G, H, F;
            public Node? Parent;
        }
    }
}
