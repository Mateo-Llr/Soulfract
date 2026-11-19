using System.Text.Json;
using System.Numerics;
using System.Linq;
using Soulfract;

namespace Soulfract
{
    public static class StructureManager
    {
        private static List<StructureData> _structures = new();

        public static void LoadStructures()
        {
            string path = "Data/structures.json";
            if (!File.Exists(path))
            {
                Console.WriteLine(" structures.json manquant, aucun village ne sera généré.");
                return;
            }

            string json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<StructuresRoot>(json);
            if (root?.Structures != null)
            {
                _structures = root.Structures;
                Console.WriteLine($" {_structures.Count} structures chargées pour les villages.");
            }
        }

        public static bool TryPlaceStructure(StructureData structure, int baseX, int baseY, World.ChunkData targetChunk, Random rand, float currentGameTime, out (int x, int y) doorPos, out Vector2 interiorPos, bool forceOverwrite = false, bool addVillageDecor = true)
		{
			doorPos = (-1, -1);
			interiorPos = Vector2.Zero;
			int width = structure.MaxX - structure.MinX + 1;
			int height = structure.MaxY - structure.MinY + 1;

			// Variables locales pour éviter les problèmes de capture
			(int x, int y) localDoorPos = (-1, -1);
			List<(int x, int y)> interiorTiles = new();

			// Vérification des collisions — PASSE 1 : validation pure, aucune mutation.
			// Important : on doit être sûrs que TOUT le footprint est plaçable avant de
			// toucher quoi que ce soit. Sinon un échec en cours de boucle laisserait
			// des objets déjà détruits (ex: murs d'une maison voisine) sans jamais poser
			// la nouvelle structure à la place — d'où les zones sans murs/toit mais
			// pourtant considérées comme intérieur par le reste du jeu.
			for (int dx = 0; dx < width; dx++)
			{
				for (int dy = 0; dy < height; dy++)
				{
					int worldX = baseX + dx;
					int worldY = baseY + dy;

					int objAt = World.GetObjectIdAt(worldX, worldY);
					if (objAt != 0 && !forceOverwrite)
						return false;

					int groundId = World.GetGroundTileIdAt(worldX, worldY);
					var groundTile = WorldTileRegistry.GetTile(groundId);
					if (groundTile == null || (!groundTile.Diggable && !forceOverwrite))
						return false;
				}
			}

			// PASSE 2 : le footprint entier est validé, on peut maintenant détruire/écraser
			// en confiance ce qui doit l'être.
			for (int dx = 0; dx < width; dx++)
			{
				for (int dy = 0; dy < height; dy++)
				{
					int worldX = baseX + dx;
					int worldY = baseY + dy;
					if (World.GetObjectIdAt(worldX, worldY) != 0)
						World.RemovePlacedObject_Apply(worldX, worldY);
				}
			}

			// Placer les tuiles
			foreach (var tile in structure.Tiles)
			{
				int worldX = baseX + tile.X;
				int worldY = baseY + tile.Y;
				targetChunk.GroundOverrides[(worldX, worldY)] = tile.GroundTileId;

				if (tile.PlacedTileId != 0)
				{
					targetChunk.Objects[(worldX, worldY)] = tile.PlacedTileId;
					var placedTileDef = WorldTileRegistry.GetTile(tile.PlacedTileId);
					if (placedTileDef != null && placedTileDef.Variation > 1 && tile.VariationIndex >= 0)
						targetChunk.Variations[(worldX, worldY)] = tile.VariationIndex;
					if (tile.PlacedTileId == HOUSE_WALL_TILE || tile.PlacedTileId == HOUSE_DOOR_TILE)
						targetChunk.SetWallCoveringAt(worldX, worldY, tile.WallCoveringId);
				}
				
				if (tile.PlacedTileId == 84) // Porte principale de la structure
				{
					if (localDoorPos == (-1, -1))
						localDoorPos = (worldX, worldY);
				}
				else if (tile.PlacedTileId == 0 && tile.GroundTileId != 0) // Sol à l'intérieur
				{
					interiorTiles.Add((worldX, worldY));
				}
				
				if (tile.PlacedTileId == 8 || tile.PlacedTileId == 84)
				{
					targetChunk.NewWallPositions.Add((worldX, worldY));
				}

				if (tile.OverlayId != 0)
					targetChunk.Overlays[(worldX, worldY)] = tile.OverlayId;
				if (tile.DecorationIndex >= 0)
					targetChunk.Decorations[(worldX, worldY)] = tile.DecorationIndex;
				if (tile.ContainerData != null)
				{
					var container = new ContainerInventoryData(tile.ContainerData.Slots.Count, 8);
					for (int i = 0; i < tile.ContainerData.Slots.Count && i < container.Slots.Count; i++)
					{
						var savedSlot = tile.ContainerData.Slots[i];
						if (!savedSlot.IsEmpty && !string.IsNullOrEmpty(savedSlot.ItemName))
						{
							var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == savedSlot.ItemName);
							if (itemData.ID != 0)
							{
								container.Slots[i].Item = new Item(itemData.Name, savedSlot.Count, itemData.Color, itemData.Icon);
								container.Slots[i].Count = savedSlot.Count;
							}
						}
					}
					targetChunk.SetContainerAt(worldX, worldY, container);
				}
				if (tile.ArmorStandData != null)
				{
					var stand = new ArmorStandData();
					stand.Head = CreateItemFromName(tile.ArmorStandData.Head);
					stand.Body = CreateItemFromName(tile.ArmorStandData.Body);
					stand.Legs = CreateItemFromName(tile.ArmorStandData.Legs);
					stand.MainHand = CreateItemFromName(tile.ArmorStandData.MainHand);
					stand.OffHand = CreateItemFromName(tile.ArmorStandData.OffHand);
					stand.CurrentPose = tile.ArmorStandData.CurrentPose ?? "idle";
					targetChunk.SetArmorStandAt(worldX, worldY, stand);
				}
				if (tile.CropData != null)
				{
					targetChunk.SetCropAt(worldX, worldY, new World.CropData
					{
						CropTileId = tile.CropData.CropTileId,
						PlantTime = currentGameTime
					});
				}
			}
			
			// Assigner la valeur de sortie
			doorPos = localDoorPos;
			
			// Trouver une position intérieure (utiliser localDoorPos ici, pas doorPos)
			if (localDoorPos.x != -1 && interiorTiles.Count > 0)
			{
				// Stocker les valeurs dans des variables locales pour la lambda
				int doorX = localDoorPos.x;
				int doorY = localDoorPos.y;
				
				var adjacent = interiorTiles.FirstOrDefault(t => 
					Math.Abs(t.x - doorX) + Math.Abs(t.y - doorY) == 1);
				
				if (adjacent != default)
				{
					int ts = Program.TileSize;
					interiorPos = new Vector2(adjacent.x * ts + ts/2f, adjacent.y * ts + ts/2f);
				}
				else if (interiorTiles.Count > 0)
				{
					var center = interiorTiles[interiorTiles.Count / 2];
					int ts = Program.TileSize;
					interiorPos = new Vector2(center.x * ts + ts/2f, center.y * ts + ts/2f);
				}
			}

			// Ajouter du mobilier aléatoire dans la maison — sauf pour les maisons procédurales,
			// qui sont déjà meublées pièce par pièce par le générateur.
			bool alreadyFurnished = structure.Name != null && structure.Name.StartsWith(ProceduralHouseNamePrefix);
			if (!alreadyFurnished)
				AddRandomFurnitureToStructure(structure, baseX, baseY, targetChunk, rand);

			if (addVillageDecor)
			{
				// Puits : un seul par zone de village (on vérifie qu'aucun puits n'existe déjà à proximité)
				if (localDoorPos.x != -1 && rand.NextSingle() < 0.35f && !IsWellNearby(localDoorPos.x, localDoorPos.y, 40))
				TryPlaceWellNear(localDoorPos.x, localDoorPos.y, targetChunk, rand, 6, out _);
			}

			return true;
		}

		private static bool IsWellNearby(int centerX, int centerY, int radius)
		{
			const int WELL_ID = 136;
			for (int dx = -radius; dx <= radius; dx++)
			{
				for (int dy = -radius; dy <= radius; dy++)
				{
					if (World.GetObjectIdAt(centerX + dx, centerY + dy) == WELL_ID)
						return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Essaie de placer un banc (2x2) sur un terrain dégagé proche d'un point donné (typiquement une porte de maison).
		/// </summary>
		public static bool TryPlaceBenchNear(int nearX, int nearY, World.ChunkData targetChunk, Random rand, int searchRadius = 6)
		{
			const int BENCH_ID = 114;
			const int benchW = 2, benchH = 2;

			var candidates = new List<(int x, int y)>();
			for (int dx = -searchRadius; dx <= searchRadius; dx++)
				for (int dy = -searchRadius; dy <= searchRadius; dy++)
					candidates.Add((nearX + dx, nearY + dy));

			candidates = candidates.OrderBy(_ => rand.Next()).ToList();

			foreach (var (baseX, baseY) in candidates)
			{
				bool valid = true;
				for (int dx = 0; dx < benchW && valid; dx++)
				{
					for (int dy = 0; dy < benchH && valid; dy++)
					{
						int wx = baseX + dx;
						int wy = baseY + dy;
						if (World.GetObjectIdAt(wx, wy) != 0) { valid = false; break; }
						if (World.Houses.Values.Any(house =>
							house.Bounds.Width > 0 && house.Bounds.Height > 0 &&
							wx >= house.Bounds.X && wx < house.Bounds.X + house.Bounds.Width &&
							wy >= house.Bounds.Y && wy < house.Bounds.Y + house.Bounds.Height))
						{
							valid = false;
							break;
						}
						int groundId = World.GetGroundTileIdAt(wx, wy);
						var groundTile = WorldTileRegistry.GetTile(groundId);
						if (groundTile == null || !groundTile.Walkable || !groundTile.Diggable) { valid = false; break; }
					}
				}
				if (!valid) continue;

				// On ne remplit que la tuile d'ancrage (comme les autres meubles multi-tuiles :
				// Table, Lit, etc.) — la collision réelle est gérée séparément par le moteur.
				targetChunk.Objects[(baseX, baseY)] = BENCH_ID;

				for (int i = 0; i < benchW; i++)
					World.RegisterBenchSeat((baseX + i, baseY), (baseX, baseY), i);

				return true;
			}

			return false;
		}

		/// <summary>
		/// Essaie de placer un puits (3x2) sur un terrain dégagé proche d'un point donné (typiquement une porte de maison).
		/// </summary>
        public static bool TryPlaceWellNear(int nearX, int nearY, World.ChunkData targetChunk, Random rand, int searchRadius, out (int x, int y) wellPos)
        {
            const int WELL_ID = 136;
            const int wellW = 3, wellH = 2;
            wellPos = (-1, -1);

            var candidates = new List<(int x, int y)>();
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
                for (int dy = -searchRadius; dy <= searchRadius; dy++)
                    candidates.Add((nearX + dx, nearY + dy));

            // Mélanger pour varier le résultat
            candidates = candidates.OrderBy(_ => rand.Next()).ToList();

            foreach (var (baseX, baseY) in candidates)
            {
                bool valid = true;
                for (int dx = 0; dx < wellW && valid; dx++)
                {
                    for (int dy = 0; dy < wellH && valid; dy++)
                    {
                        int wx = baseX + dx;
                        int wy = baseY + dy;
                        if (World.GetObjectIdAt(wx, wy) != 0) { valid = false; break; }
                        int groundId = World.GetGroundTileIdAt(wx, wy);
                        var groundTile = WorldTileRegistry.GetTile(groundId);
                        if (groundTile == null || !groundTile.Walkable || !groundTile.Diggable) { valid = false; break; }
                    }
                }
                if (!valid) continue;

                targetChunk.Objects[(baseX, baseY)] = WELL_ID;
                wellPos = (baseX, baseY);
                return true;
            }

            return false;
        }

        private static void AddRandomFurnitureToStructure(StructureData structure, int baseX, int baseY, World.ChunkData targetChunk, Random rand)
		{
			var furnitureOptions = new[]
			{
				(id: 6, name: "Table", probability: 0.15f, width: 2, height: 2),        // Table
				(id: 87, name: "Bed", probability: 0.3f, width: 2, height: 3),          // Lit
				(id: 93, name: "Floor lamp", probability: 0.25f, width: 1, height: 3), // Lampadaire
				(id: 95, name: "Chair", probability: 0.4f, width: 1, height: 2),       // Chaise
				(id: 96, name: "Wooden coffee table", probability: 0.12f, width: 1, height: 1), // Table basse
				(id: 42, name: "Wooden Chest", probability: 0.2f, width: 1, height: 1) // Coffre en bois
			};

			var candidateTiles = structure.Tiles
				.Where(tile => tile.PlacedTileId == 0)
				.Select(tile => (tileX: baseX + tile.X, tileY: baseY + tile.Y))
				.Where(pos => World.IsValidFurnitureSurface(pos.tileX, pos.tileY))
				.OrderBy(_ => rand.Next())
				.ToList();

			if (candidateTiles.Count == 0)
				return;

			int maxFurniture = Math.Clamp(candidateTiles.Count / 10, 1, 3);
			var placedFurniture = new List<(int x, int y)>();

			foreach (var candidate in candidateTiles)
			{
				if (placedFurniture.Count >= maxFurniture)
					break;

				if (rand.NextSingle() > 0.55f)
					continue;

				if (placedFurniture.Any(pos => Math.Abs(pos.x - candidate.tileX) + Math.Abs(pos.y - candidate.tileY) < 3))
					continue;

				foreach (var furniture in furnitureOptions.OrderBy(_ => rand.Next()).ToList())
				{
					if (rand.NextSingle() > furniture.probability)
						continue;

					if (CanPlaceFurniture(candidate.tileX, candidate.tileY, furniture.width, furniture.height, baseX, baseY, structure))
					{
						targetChunk.Objects[(candidate.tileX, candidate.tileY)] = furniture.id;
						placedFurniture.Add((candidate.tileX, candidate.tileY));
						break;
					}
				}
			}
		}

		private static bool CanPlaceFurniture(int x, int y, int width, int height, int baseX, int baseY, StructureData structure)
		{
			// Étendue réelle de la structure telle qu'elle est posée dans le monde, en
			// coordonnées LOCALES 0-based (baseX/baseY + tile.X/tile.Y). On ne peut pas se
			// fier à structure.MinX/MaxX/MinY/MaxY : sur certains templates (ex: smallhouse2,
			// smallhouse3), ces champs contiennent encore les coordonnées absolues du monde
			// au moment de la capture de la structure, pas des bornes relatives — comparer
			// des coordonnées locales à ces valeurs échouait donc systématiquement.
			int structWidth = structure.MaxX - structure.MinX + 1;
			int structHeight = structure.MaxY - structure.MinY + 1;

			for (int dx = 0; dx < width; dx++)
			{
				for (int dy = 0; dy < height; dy++)
				{
					int checkX = x + dx;
					int checkY = y + dy;

					int structX = checkX - baseX;
					int structY = checkY - baseY;

					if (structX < 0 || structX >= structWidth ||
						structY < 0 || structY >= structHeight)
						return false;

					if (World.GetObjectIdAt(checkX, checkY) != 0)
						return false;

					if (!World.IsValidFurnitureSurface(checkX, checkY))
						return false;

					var tile = structure.Tiles.FirstOrDefault(t => t.X == structX && t.Y == structY);
					if (tile == null || tile.PlacedTileId != 0)
						return false;

					// Un meuble ne doit jamais se coller au mur de la pièce, ni au seuil de
					// la porte. Si le tile est sur le bord immédiat d'une boîte, il est trop
					// proche d'un mur et la chaise/lampe se retrouve par-dessus la cloison.
					bool tooCloseToWall = false;
					for (int ox = -1; ox <= 1; ox++)
					{
						for (int oy = -1; oy <= 1; oy++)
						{
							if (ox == 0 && oy == 0)
								continue;

							int neighborStructX = structX + ox;
							int neighborStructY = structY + oy;
							if (neighborStructX < 0 || neighborStructX >= structWidth ||
								neighborStructY < 0 || neighborStructY >= structHeight)
							{
								tooCloseToWall = true;
								break;
							}

							var neighborTile = structure.Tiles.FirstOrDefault(t => t.X == neighborStructX && t.Y == neighborStructY);
							if (neighborTile != null && neighborTile.PlacedTileId != 0)
							{
								tooCloseToWall = true;
								break;
							}
						}
						if (tooCloseToWall)
							break;
					}

					if (tooCloseToWall)
						return false;
				}
			}

			return true;
		}

        private static Item? CreateItemFromName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == name);
            if (itemData.ID != 0)
                return new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
            return null;
        }

        // ═════════════════════════════════════════════════════════════════════════
        //  MAISONS PROCÉDURALES DE VILLAGE
        // ─────────────────────────────────────────────────────────────────────────
        //  Principe : on pose un séjour en façade, puis on y colle 2 à 5 pièces (chambres,
        //  cuisine, salle de bain, réserve) de tailles et de proportions propres à leur rôle,
        //  avec des décalages aléatoires : la maison n'est donc pas un rectangle (L, T,
        //  escalier...). On construit ensuite murs / portes à partir des pièces, et enfin on
        //  MEUBLE chaque pièce selon son rôle.
        //
        //  Règles garanties par construction :
        //   - gabarit maximal 16 x 16 tuiles pour tenir dans un seul chunk
        //     (cf. ChunkGeneration) ;
        //   - une seule porte d'entrée, sur la façade basse, au centre du séjour
        //     commune, jamais dans un coin ni sur un mur mitoyen ;
        //   - les pièces ne sont reliées que par de vraies portes sur un mur
        //     mitoyen (jamais dans un angle) ;
        //   - le mobilier reste à ≥ 1 tuile des murs (même règle que
        //     CanPlaceFurniture) : l'anneau de circulation autour de chaque pièce
        //     est donc toujours libre, portes et entrée ne sont jamais bloquées ;
        //   - les pièces ont une taille minimale qui permet d'y mettre du mobilier ;
        //   - toutes les pièces sont accessibles depuis le séjour (arbre de portes + boucles).
        // ═════════════════════════════════════════════════════════════════════════
        public const string ProceduralHouseNamePrefix = "procedural_house_";

        private const int HOUSE_FLOOR_TILE = 7;
        private const int HOUSE_WALL_TILE = 8;
        private const int HOUSE_DOOR_TILE = 84;
        private const int TAVERN_WALL_COVERING_ID = 901;
        private const int BATHROOM_WALL_COVERING_ID = 902;

        private const int FURNITURE_TABLE = 6;
        private const int FURNITURE_BED = 87;
        private const int FURNITURE_LAMP = 93;
        private const int FURNITURE_CHAIR = 95;
        private const int FURNITURE_COFFEE_TABLE = 96;
        private const int FURNITURE_CHEST = 42;
        private const int FURNITURE_TOILET = 85;
        private const int FURNITURE_POT = 86;
        private const int FURNITURE_SINK = 94;

        private enum HouseRoomKind { LivingRoom, Kitchen, Bedroom, Bathroom, Storage }

        private sealed class HouseRoom
        {
            public int X, Y, W, H;
            public HouseRoomKind Kind;
            public int FloorTileId;

            public HouseRoom(int x, int y, int w, int h, HouseRoomKind kind)
            {
                X = x; Y = y; W = w; H = h; Kind = kind;
            }

            public bool IsInterior(int px, int py) => px > X && px < X + W - 1 && py > Y && py < Y + H - 1;

            public bool IsBorder(int px, int py) =>
                px >= X && px <= X + W - 1 && py >= Y && py <= Y + H - 1 && !IsInterior(px, py);

            public bool IsCorner(int px, int py) =>
                (px == X || px == X + W - 1) && (py == Y || py == Y + H - 1);
        }

        // ── Paramètres de génération du plan ────────────────────────────────────
        private const int HOUSE_MAX_SIZE = 16;               // un seul chunk
        private const double HOUSE_EXTRA_DOOR_CHANCE = 0.25; // portes en plus (boucles de circulation)

        /// <summary>
        /// Taille minimale (murs compris) pour que le mobilier d'un type de pièce tienne
        /// dans la zone utile (cf. FurnishHouseRoom : lit 2x3, WC + lavabo, table + marmite).
        /// </summary>
        private static bool RoomFits(HouseRoomKind kind, int w, int h)
        {
            switch (kind)
            {
                case HouseRoomKind.Bedroom: return w >= 6 && h >= 7;
                case HouseRoomKind.Bathroom: return w >= 6 && h >= 6;
                case HouseRoomKind.Kitchen: return (w >= 7 && h >= 6) || (w >= 6 && h >= 8);
                default: return true;
            }
        }

        /// <summary>
        /// Tire une taille (murs compris) propre au rôle de la pièce. Les proportions sont bornées
        /// (≈ carré pour une chambre, un peu plus large pour un séjour) : pas de pièce en couloir.
        /// <paramref name="smallest"/> force la plus petite taille possible (repli quand rien ne rentre).
        /// </summary>
        private static (int w, int h) RollRoomSize(HouseRoomKind kind, Random rand, bool smallest)
        {
            int minW, maxW, minH, maxH;
            double maxRatio = 1.35;
            switch (kind)
            {
                case HouseRoomKind.LivingRoom: minW = 7; maxW = 9; minH = 6; maxH = 8; maxRatio = 1.7; break;
                case HouseRoomKind.Bedroom: minW = 6; maxW = 8; minH = 7; maxH = 8; break;
                case HouseRoomKind.Kitchen: minW = 6; maxW = 8; minH = 6; maxH = 8; break;
                case HouseRoomKind.Bathroom: minW = 6; maxW = 7; minH = 6; maxH = 7; break;
                default: minW = 5; maxW = 6; minH = 5; maxH = 6; break;
            }

            var options = new List<(int w, int h)>();
            for (int w = minW; w <= maxW; w++)
                for (int h = minH; h <= maxH; h++)
                    if (Math.Max(w, h) / (double)Math.Min(w, h) <= maxRatio && RoomFits(kind, w, h))
                        options.Add((w, h));

            if (smallest)
                return options.OrderBy(o => o.w * o.h).First();
            return options[rand.Next(options.Count)];
        }

        private static HouseRoom PickWeightedRoom(List<HouseRoom> items, Random rand, Func<HouseRoom, double> weight)
        {
            double total = 0;
            foreach (var item in items)
                total += weight(item);
            double roll = rand.NextDouble() * total;
            foreach (var item in items)
            {
                roll -= weight(item);
                if (roll <= 0)
                    return item;
            }
            return items[items.Count - 1];
        }

        /// <summary>
        /// Tuiles où une porte peut relier deux pièces : sur le mur mitoyen, jamais dans un angle.
        /// Liste vide = les pièces ne sont pas voisines.
        /// </summary>
        private static List<(int x, int y)> GetSharedWallCandidates(HouseRoom a, HouseRoom b)
        {
            var candidates = new List<(int x, int y)>();
            for (int x = Math.Max(a.X, b.X); x <= Math.Min(a.X + a.W - 1, b.X + b.W - 1); x++)
            {
                for (int y = Math.Max(a.Y, b.Y); y <= Math.Min(a.Y + a.H - 1, b.Y + b.H - 1); y++)
                {
                    if (a.IsBorder(x, y) && b.IsBorder(x, y) && !a.IsCorner(x, y) && !b.IsCorner(x, y))
                        candidates.Add((x, y));
                }
            }
            return candidates;
        }

        /// <summary>Vrai si toutes les pièces sont atteignables de proche en proche par une porte possible.</summary>
        private static bool IsHouseConnected(List<HouseRoom> rooms)
        {
            if (rooms.Count == 0)
                return false;
            var seen = new HashSet<int> { 0 };
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                for (int j = 0; j < rooms.Count; j++)
                {
                    if (seen.Contains(j) || GetSharedWallCandidates(rooms[i], rooms[j]).Count == 0)
                        continue;
                    seen.Add(j);
                    queue.Enqueue(j);
                }
            }
            return seen.Count == rooms.Count;
        }

        /// <summary>
        /// Deux pièces sont compatibles si elles sont franchement séparées, ou si elles ne partagent
        /// qu'UN mur d'une tuile. On refuse les chevauchements, les contacts par un seul coin et les
        /// murs doublés ou ruelles d'une tuile entre deux pièces (écart de 1 ou 2 tuiles).
        /// </summary>
        private static bool AreRoomsCompatible(HouseRoom a, HouseRoom b)
        {
            int dx = Math.Max(a.X, b.X) - Math.Min(a.X + a.W - 1, b.X + b.W - 1);
            int dy = Math.Max(a.Y, b.Y) - Math.Min(a.Y + a.H - 1, b.Y + b.H - 1);
            if (dx < 0 && dy < 0)
                return false;
            if (dx == 0 && dy == 0)
                return false;
            if ((dx == 1 || dx == 2) && dy <= 0)
                return false;
            if ((dy == 1 || dy == 2) && dx <= 0)
                return false;
            return true;
        }

        private static (int w, int h) GetHouseBoundingSize(List<HouseRoom> rooms, HouseRoom? extra = null)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var r in extra == null ? rooms : rooms.Append(extra))
            {
                minX = Math.Min(minX, r.X);
                minY = Math.Min(minY, r.Y);
                maxX = Math.Max(maxX, r.X + r.W);
                maxY = Math.Max(maxY, r.Y + r.H);
            }
            return (maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Toutes les positions où une pièce w x h peut être collée à une pièce existante (à droite,
        /// à gauche ou au-dessus ; jamais sous le séjour, côté entrée), avec un décalage libre le
        /// long du mur tant qu'il reste de quoi poser une porte. C'est ce qui donne les silhouettes
        /// en L, en T ou en escalier plutôt qu'un rectangle.
        /// </summary>
        private static List<HouseRoom> FindRoomPlacements(List<HouseRoom> rooms, HouseRoomKind kind, int w, int h)
        {
            var result = new List<HouseRoom>();
            var tried = new HashSet<(int x, int y)>();

            void Consider(int x, int y)
            {
                if (!tried.Add((x, y)))
                    return;
                var candidate = new HouseRoom(x, y, w, h, kind);
                foreach (var existing in rooms)
                    if (!AreRoomsCompatible(candidate, existing))
                        return;
                var size = GetHouseBoundingSize(rooms, candidate);
                if (size.w > HOUSE_MAX_SIZE || size.h > HOUSE_MAX_SIZE)
                    return;
                foreach (var existing in rooms)
                {
                    if (GetSharedWallCandidates(candidate, existing).Count > 0)
                    {
                        result.Add(candidate);
                        return;
                    }
                }
            }

            foreach (var parent in rooms)
            {
                // À droite / à gauche : recouvrement vertical d'au moins 4 tuiles.
                for (int y = parent.Y - h + 4; y <= parent.Y + parent.H - 4; y++)
                {
                    Consider(parent.X + parent.W - 1, y);
                    Consider(parent.X - w + 1, y);
                }
                // Au-dessus / en dessous : recouvrement horizontal d'au moins 4 tuiles.
                for (int x = parent.X - w + 4; x <= parent.X + parent.W - 4; x++)
                {
                    Consider(x, parent.Y - h + 1);
                    if (parent.Kind != HouseRoomKind.LivingRoom)
                        Consider(x, parent.Y + parent.H - 1);
                }
            }
            return result;
        }

        /// <summary>
        /// Colonnes (monde du plan) où la porte d'entrée peut s'ouvrir sur la façade basse du séjour :
        /// jamais dans un angle, et seulement si la tuile juste en dessous est dehors
        /// (aucune autre pièce ne la recouvre).
        /// </summary>
        private static List<int> FindEntranceXs(HouseRoom hall, List<HouseRoom> rooms)
        {
            var result = new List<int>();
            int belowY = hall.Y + hall.H;
            for (int x = hall.X + 2; x <= hall.X + hall.W - 3; x++)
            {
                int column = x;
                bool covered = rooms.Any(r => column >= r.X && column < r.X + r.W && belowY >= r.Y && belowY < r.Y + r.H);
                if (!covered)
                    result.Add(x);
            }
            return result;
        }

        /// <summary>
        /// Génère un plan aléatoire : on pose le séjour, puis chaque pièce (taille et proportions
        /// propres à son rôle) est collée à une pièce existante avec un décalage aléatoire, en
        /// favorisant les emprises compactes. Les portes forment un arbre couvrant (toutes les pièces
        /// accessibles) plus quelques boucles. Retourne false si le tirage est inutilisable :
        /// l'appelant retente.
        /// </summary>
        private static bool TryGenerateHouseLayout(Random rand, out List<HouseRoom> rooms, out List<(int a, int b)> links)
        {
            rooms = new List<HouseRoom>();
            links = new List<(int a, int b)>();

            var (hallW, hallH) = RollRoomSize(HouseRoomKind.LivingRoom, rand, false);
            var hall = new HouseRoom(0, 0, hallW, hallH, HouseRoomKind.LivingRoom);
            var layout = new List<HouseRoom> { hall };

            // Pièces à poser, dans l'ordre : les essentielles d'abord (une chambre, la salle de bain),
            // puis ce qui reste tient selon la place disponible.
            var kinds = new List<HouseRoomKind> { HouseRoomKind.Bedroom };
            if (rand.NextDouble() < 0.9)
                kinds.Add(HouseRoomKind.Bathroom);
            if (rand.NextDouble() < 0.85)
                kinds.Add(HouseRoomKind.Kitchen);
            int roll = rand.Next(100);
            int extraBedrooms = roll < 45 ? 0 : roll < 80 ? 1 : 2;
            for (int i = 0; i < extraBedrooms; i++)
                kinds.Add(HouseRoomKind.Bedroom);
            if (rand.NextDouble() < 0.3)
                kinds.Add(HouseRoomKind.Storage);

            foreach (var kind in kinds)
            {
                var placements = new List<HouseRoom>();
                for (int attempt = 0; attempt < 8 && placements.Count == 0; attempt++)
                {
                    var (w, h) = RollRoomSize(kind, rand, attempt >= 3);
                    placements = FindRoomPlacements(layout, kind, w, h);
                }
                if (placements.Count == 0)
                    continue; // plus de place : cette pièce n'existera pas dans cette maison

                // Préférence pour les emprises compactes (sinon la maison s'étale et bute sur 16x16).
                var chosen = PickWeightedRoom(placements, rand, candidate =>
                {
                    var size = GetHouseBoundingSize(layout, candidate);
                    return Math.Pow(size.w * size.h / 100.0, -4);
                });
                layout.Add(chosen);
            }

            if (layout.Count < 3 || !layout.Any(r => r.Kind == HouseRoomKind.Bedroom))
                return false;
            // Le séjour reste en façade : il ne doit pas être loin derrière le bas de la maison.
            if (hall.Y + hall.H < layout.Max(r => r.Y + r.H) - 2)
                return false;
            if (FindEntranceXs(hall, layout).Count == 0)
                return false;
            if (!IsHouseConnected(layout))
                return false;

            // Portes : arbre couvrant aléatoire (Kruskal, léger avantage aux portes du séjour),
            // puis quelques portes supplémentaires pour créer des boucles.
            var edges = new List<(int a, int b, double weight)>();
            for (int i = 0; i < layout.Count; i++)
                for (int j = i + 1; j < layout.Count; j++)
                    if (GetSharedWallCandidates(layout[i], layout[j]).Count > 0)
                        edges.Add((i, j, rand.NextDouble() - (i == 0 ? 0.3 : 0.0)));
            edges.Sort((p, q) => p.weight.CompareTo(q.weight));

            var parent = Enumerable.Range(0, layout.Count).ToArray();
            int Find(int v)
            {
                while (parent[v] != v)
                {
                    parent[v] = parent[parent[v]];
                    v = parent[v];
                }
                return v;
            }

            var doorLinks = new List<(int a, int b)>();
            foreach (var edge in edges)
            {
                int rootA = Find(edge.a), rootB = Find(edge.b);
                if (rootA != rootB)
                {
                    parent[rootA] = rootB;
                    doorLinks.Add((edge.a, edge.b));
                }
                else if (rand.NextDouble() < HOUSE_EXTRA_DOOR_CHANCE)
                {
                    doorLinks.Add((edge.a, edge.b));
                }
            }

            rooms = layout;
            links = doorLinks;
            return true;
        }

        /// <summary>Plan de secours (ancien plan familial fixe), utilisé seulement si la génération échoue.</summary>
        private static List<HouseRoom> BuildFallbackHouseLayout(out List<(int a, int b)> links)
        {
            var rooms = new List<HouseRoom>
            {
                new HouseRoom(0, 6, 6, 6, HouseRoomKind.LivingRoom),
                new HouseRoom(5, 6, 6, 6, HouseRoomKind.Kitchen),
                new HouseRoom(10, 6, 6, 6, HouseRoomKind.Bathroom),
                new HouseRoom(0, 0, 6, 7, HouseRoomKind.Bedroom),
                new HouseRoom(5, 0, 6, 7, HouseRoomKind.Bedroom),
                new HouseRoom(10, 0, 6, 7, HouseRoomKind.Bedroom)
            };
            links = new List<(int a, int b)> { (0, 1), (1, 2), (3, 4), (4, 5), (0, 3), (1, 4), (2, 5) };
            return rooms;
        }

        /// <summary>
        /// Choisit un plan de maison aléatoire (cf. <see cref="TryGenerateHouseLayout"/>).
        /// La pièce d'index 0 est toujours le séjour, placé en façade pour recevoir l'entrée.
        /// Les pièces voisines partagent un mur d'une tuile et <paramref name="links"/> liste
        /// celles reliées par une porte.
        /// </summary>
        private static List<HouseRoom> BuildHouseLayout(Random rand, out List<(int a, int b)> links)
        {
            List<HouseRoom>? rooms = null;
            links = new List<(int a, int b)>();

            for (int attempt = 0; attempt < 60 && rooms == null; attempt++)
            {
                if (TryGenerateHouseLayout(rand, out var candidateRooms, out var candidateLinks))
                {
                    rooms = candidateRooms;
                    links = candidateLinks;
                }
            }
            rooms ??= BuildFallbackHouseLayout(out links);

            foreach (var room in rooms)
                room.FloorTileId = room.Kind == HouseRoomKind.Bathroom ? 59 : rand.Next(54, 59);

            return rooms;
        }

        /// <summary>
        /// Meuble une pièce. Le mobilier n'occupe que la zone utile (à ≥ 1 tuile des murs) ;
        /// l'anneau extérieur de la pièce reste libre pour circuler.
        /// Les meubles multi-tuiles sont ancrés en haut à gauche (comme ailleurs dans ce fichier).
        /// </summary>
        private static void FurnishHouseRoom(HouseRoom room, Random rand, Dictionary<(int x, int y), int> furniture)
        {
            int ux0 = room.X + 2, ux1 = room.X + room.W - 3;
            int uy0 = room.Y + 2, uy1 = room.Y + room.H - 3;
            int uw = ux1 - ux0 + 1, uh = uy1 - uy0 + 1;
            if (uw < 1 || uh < 1)
                return;

            var occupied = new HashSet<(int x, int y)>();

            bool Fits(int x, int y, int w, int h)
            {
                if (x < ux0 || y < uy0 || x + w - 1 > ux1 || y + h - 1 > uy1)
                    return false;
                for (int dx = 0; dx < w; dx++)
                    for (int dy = 0; dy < h; dy++)
                        if (occupied.Contains((x + dx, y + dy)))
                            return false;
                return true;
            }

            bool Put(int id, int x, int y, int w, int h)
            {
                if (!Fits(x, y, w, h))
                    return false;
                for (int dx = 0; dx < w; dx++)
                    for (int dy = 0; dy < h; dy++)
                        occupied.Add((x + dx, y + dy));
                furniture[(x, y)] = id;
                return true;
            }

            var corners = new List<(int x, int y)> { (ux0, uy0), (ux1, uy0), (ux0, uy1), (ux1, uy1) };
            List<(int x, int y)> Shuffled(List<(int x, int y)> src) => src.OrderBy(_ => rand.Next()).ToList();

            var allCells = new List<(int x, int y)>();
            for (int cx = ux0; cx <= ux1; cx++)
                for (int cy = uy0; cy <= uy1; cy++)
                    allCells.Add((cx, cy));

            bool PlaceBed(bool leftSide) => Put(FURNITURE_BED, leftSide ? ux0 : ux1 - 1, uy0, 2, 3);

            // Table + chaises sur les côtés, dans les colonnes [rx0, rx1].
            void PlaceTableSet(int rx0, int rx1)
            {
                int rw = rx1 - rx0 + 1;
                if (uh < 2 || rw < 2)
                    return;
                int ty = rand.Next(2) == 0 ? uy0 : uy1 - 1;

                if (rw >= 4)
                {
                    int sx = rx0 + rand.Next(rw - 3);
                    if (Fits(sx, ty, 4, 2))
                    {
                        Put(FURNITURE_CHAIR, sx, ty, 1, 2);
                        Put(FURNITURE_TABLE, sx + 1, ty, 2, 2);
                        Put(FURNITURE_CHAIR, sx + 3, ty, 1, 2);
                    }
                }
                else if (rw == 3)
                {
                    if (rand.Next(2) == 0)
                    {
                        if (Fits(rx0, ty, 3, 2))
                        {
                            Put(FURNITURE_CHAIR, rx0, ty, 1, 2);
                            Put(FURNITURE_TABLE, rx0 + 1, ty, 2, 2);
                        }
                    }
                    else if (Fits(rx0, ty, 3, 2))
                    {
                        Put(FURNITURE_TABLE, rx0, ty, 2, 2);
                        Put(FURNITURE_CHAIR, rx0 + 2, ty, 1, 2);
                    }
                }
                else
                {
                    Put(FURNITURE_TABLE, rx0, ty, 2, 2);
                }
            }

            void PlaceLamp(float chance)
            {
                if (uh < 3 || rand.NextSingle() > chance)
                    return;
                foreach (var c in Shuffled(new List<(int x, int y)> { (ux0, uy0), (ux1, uy0) }))
                    if (Put(FURNITURE_LAMP, c.x, c.y, 1, 3))
                        return;
            }

            void PlaceChest(float chance)
            {
                if (rand.NextSingle() > chance)
                    return;
                foreach (var c in Shuffled(corners))
                    if (Put(FURNITURE_CHEST, c.x, c.y, 1, 1))
                        return;
                foreach (var c in Shuffled(allCells))
                    if (Put(FURNITURE_CHEST, c.x, c.y, 1, 1))
                        return;
            }

            switch (room.Kind)
            {
                case HouseRoomKind.LivingRoom:
                    PlaceTableSet(ux0, ux1);
                    PlaceLamp(0.7f);
                    PlaceChest(0.8f);
                    if (rand.NextSingle() < 0.3f)
                        foreach (var c in Shuffled(allCells))
                            if (Put(FURNITURE_COFFEE_TABLE, c.x, c.y, 1, 1))
                                break;
                    break;

                case HouseRoomKind.Kitchen:
                {
                    if (uw >= 3 && uh >= 2)
                    {
                        int tableX = rand.Next(2) == 0 ? ux0 : ux1 - 2;
                        Put(FURNITURE_TABLE, tableX, uy0, 2, 2);
                        int potX = tableX == ux0 ? ux1 : ux0;
                        Put(FURNITURE_POT, potX, uy0, 1, 2);
                    }
                    else if (uh >= 4)
                    {
                        Put(FURNITURE_TABLE, ux0, uy0, 2, 2);
                        Put(FURNITURE_POT, ux0, uy0 + 2, 1, 2);
                    }
                    else
                    {
                        Put(FURNITURE_POT, ux0, uy0, 1, 2);
                    }
                    PlaceChest(0.35f);
                    break;
                }

                case HouseRoomKind.Bedroom:
                {
                    bool bedLeft = rand.Next(2) == 0;
                    if (uh >= 3 && uw >= 2)
                        PlaceBed(bedLeft);
                    PlaceLamp(0.8f);
                    PlaceChest(0.85f);
                    break;
                }

                case HouseRoomKind.Bathroom:
                    Put(FURNITURE_TOILET, ux0, uy0, 1, 2);
                    Put(FURNITURE_SINK, ux1, uy0, 1, 2);
                    break;

                default: // Storage : plusieurs coffres, parfois un établi.
                    PlaceChest(1.0f);
                    PlaceChest(0.8f);
                    PlaceChest(0.4f);
                    if (uw >= 2 && uh >= 2 && rand.NextSingle() < 0.5f)
                        foreach (var c in Shuffled(allCells))
                            if (Put(FURNITURE_TABLE, c.x, c.y, 2, 2))
                                break;
                    break;
            }
        }

        public static StructureData CreateProceduralVillageHouse(Random rand)
        {
            var rooms = BuildHouseLayout(rand, out var links);

            // ── Normalisation (origine en 0,0) + miroir horizontal aléatoire ──────
            int minX = rooms.Min(r => r.X);
            int minY = rooms.Min(r => r.Y);
            int totalWidth = rooms.Max(r => r.X + r.W - 1) - minX + 1;
            int totalHeight = rooms.Max(r => r.Y + r.H - 1) - minY + 1;
            bool mirror = rand.Next(2) == 0;
            foreach (var r in rooms)
            {
                int localX = r.X - minX;
                r.X = mirror ? totalWidth - localX - r.W : localX;
                r.Y -= minY;
            }

            // ── Sol et murs ────────────────────────────────────────────────────────
            // Une tuile est un mur si elle borde une pièce sans être l'intérieur d'une
            // autre : les murs mitoyens sont donc uniques et partagés.
            var floor = new Dictionary<(int x, int y), int>();
            var walls = new HashSet<(int x, int y)>();
            foreach (var r in rooms)
            {
                for (int x = r.X; x < r.X + r.W; x++)
                    for (int y = r.Y; y < r.Y + r.H; y++)
                    {
                        if (r.IsInterior(x, y))
                            floor[(x, y)] = r.FloorTileId;
                        else
                            walls.Add((x, y));
                    }
            }
            walls.ExceptWith(floor.Keys);

            // ── Portes intérieures : sur le mur mitoyen, jamais dans un angle ──────
            var doors = new HashSet<(int x, int y)>();
            foreach (var (ai, bi) in links)
            {
                var candidates = GetSharedWallCandidates(rooms[ai], rooms[bi]);
                if (candidates.Count == 0)
                    continue;
                candidates = candidates.OrderBy(c => c.y).ThenBy(c => c.x).ToList();
                // On s'écarte des extrémités du mur quand il est assez long.
                if (candidates.Count >= 4)
                    candidates = candidates.GetRange(1, candidates.Count - 2);
                doors.Add(candidates[rand.Next(candidates.Count)]);
            }

            // ── Porte d'entrée : façade basse de la salle commune, vers le centre ──
            var hall = rooms[0];
            int entranceY = hall.Y + hall.H - 1;
            // Colonnes dont la tuile extérieure est dégagée, de préférence près du centre.
            var entranceXs = FindEntranceXs(hall, rooms);
            int hallCenterX = hall.X + hall.W / 2;
            var preferredXs = entranceXs.Where(column => Math.Abs(column - hallCenterX) <= 1).ToList();
            if (preferredXs.Count == 0)
                preferredXs = entranceXs;
            int entranceX = preferredXs.Count > 0
                ? preferredXs[rand.Next(preferredXs.Count)]
                : Math.Clamp(hallCenterX, hall.X + 2, hall.X + hall.W - 3);
            var entrance = (x: entranceX, y: entranceY);
            doors.Add(entrance);

            // ── Mobilier ───────────────────────────────────────────────────────────
            var furniture = new Dictionary<(int x, int y), int>();
            foreach (var r in rooms)
                FurnishHouseRoom(r, rand, furniture);

            // Une partie des maisons adopte un thème de taverne. Les salles de bain
            // gardent toujours leur propre revêtement sur les murs du fond et des côtés.
            bool isTavernHouse = rand.NextDouble() < 0.25;
            var wallCoverings = new Dictionary<(int x, int y), int>();
            if (isTavernHouse)
            {
                foreach (var wall in walls)
                    if (!doors.Contains(wall))
                        wallCoverings[wall] = TAVERN_WALL_COVERING_ID;
            }

            foreach (var bathroom in rooms.Where(room => room.Kind == HouseRoomKind.Bathroom))
            {
                for (int x = bathroom.X; x < bathroom.X + bathroom.W; x++)
                {
                    for (int y = bathroom.Y; y < bathroom.Y + bathroom.H; y++)
                    {
                        bool isBackOrSideWall = y == bathroom.Y
                            || x == bathroom.X
                            || x == bathroom.X + bathroom.W - 1;
                        bool isBathroomCorner =
                            (x == bathroom.X || x == bathroom.X + bathroom.W - 1)
                            && (y == bathroom.Y || y == bathroom.Y + bathroom.H - 1);
                        var position = (x, y);
                        if (isBackOrSideWall && !isBathroomCorner
                            && walls.Contains(position) && !doors.Contains(position))
                            wallCoverings[position] = BATHROOM_WALL_COVERING_ID;
                    }
                }
            }

            // ── Assemblage des tuiles ──────────────────────────────────────────────
            StructureTileData MakeTile(int x, int y, int placedTileId, int groundTileId = HOUSE_FLOOR_TILE) => new StructureTileData
            {
                X = x,
                Y = y,
                GroundTileId = groundTileId,
                PlacedTileId = placedTileId,
                OverlayId = 0,
                DecorationIndex = -1,
                VariationIndex = -1,
                Height = 2
            };

            var tileList = new List<StructureTileData>();
            foreach (var tile in floor.OrderBy(p => p.Key.y).ThenBy(p => p.Key.x))
                tileList.Add(MakeTile(tile.Key.x, tile.Key.y,
                    furniture.TryGetValue(tile.Key, out int furnitureId) ? furnitureId : 0,
                    tile.Value));
            foreach (var (x, y) in walls.OrderBy(p => p.y).ThenBy(p => p.x))
            {
                var wallTile = MakeTile(x, y, doors.Contains((x, y)) ? HOUSE_DOOR_TILE : HOUSE_WALL_TILE);
                if (wallCoverings.TryGetValue((x, y), out int coveringId))
                    wallTile.WallCoveringId = coveringId;
                tileList.Add(wallTile);
            }

            // TryPlaceStructure prend la PREMIÈRE tuile porte (PlacedTileId == 84) comme
            // « la » porte du bâtiment (doorPos, utilisée pour relier la maison aux
            // chemins du village) : la porte d'entrée doit donc passer avant toute porte
            // intérieure.
            int entranceIndex = tileList.FindIndex(t => t.X == entrance.x && t.Y == entrance.y);
            if (entranceIndex > 0)
            {
                var entranceTile = tileList[entranceIndex];
                tileList.RemoveAt(entranceIndex);
                tileList.Insert(0, entranceTile);
            }

            return new StructureData
            {
                Name = $"{ProceduralHouseNamePrefix}{rand.Next(1000, 9999)}",
                CreatedAt = DateTime.Now,
                MinX = 0,
                MinY = 0,
                MaxX = totalWidth - 1,
                MaxY = totalHeight - 1,
                Tiles = tileList
            };
        }

        public static List<StructureData> GetStructures() => _structures;
    }
}