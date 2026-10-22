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

			// Ajouter du mobilier aléatoire dans la maison
			AddRandomFurnitureToStructure(structure, baseX, baseY, targetChunk, rand);

			if (addVillageDecor)
			{
				// Puits : un seul par zone de village (on vérifie qu'aucun puits n'existe déjà à proximité)
				if (localDoorPos.x != -1 && rand.NextSingle() < 0.35f && !IsWellNearby(localDoorPos.x, localDoorPos.y, 40))
				TryPlaceWellNear(localDoorPos.x, localDoorPos.y, targetChunk, rand, 6, out _);
				if (localDoorPos.x != -1 && rand.NextSingle() < 0.35f)
					TryPlaceBenchNear(localDoorPos.x, localDoorPos.y, targetChunk, rand);
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

        public static StructureData CreateProceduralVillageHouse(Random rand)
        {
            int mainWidth = rand.Next(9, 15);
            int mainHeight = rand.Next(8, 12);

            var rooms = new List<(int x, int y, int w, int h, int side)> { (0, 0, mainWidth, mainHeight, -1) };
            int targetExtraRooms = rand.Next(1, 4);
            int attempts = 0;

            while (rooms.Count - 1 < targetExtraRooms && attempts < 80)
            {
                attempts++;

                int side = rand.Next(4);
                int roomW;
                int roomH;
                int x;
                int y;

                switch (side)
                {
                    case 0:
                        roomW = rand.Next(4, 8);
                        roomH = rand.Next(4, Math.Min(9, mainHeight));
                        // Chevauche la pièce principale d'1 tuile pour que le mur droit de la
                        // maison et le mur gauche de la pièce annexe soient le MÊME mur (partagé).
                        x = mainWidth - 1;
                        y = rand.Next(0, Math.Max(1, mainHeight - roomH + 1));
                        break;
                    case 1:
                        roomW = rand.Next(4, Math.Min(9, mainWidth));
                        roomH = rand.Next(4, 7);
                        x = rand.Next(0, Math.Max(1, mainWidth - roomW + 1));
                        y = mainHeight - 1;
                        break;
                    case 2:
                        roomW = rand.Next(4, 8);
                        roomH = rand.Next(4, Math.Min(9, mainHeight));
                        x = -roomW + 1;
                        y = rand.Next(0, Math.Max(1, mainHeight - roomH + 1));
                        break;
                    default:
                        roomW = rand.Next(4, Math.Min(9, mainWidth));
                        roomH = rand.Next(4, 7);
                        x = rand.Next(0, Math.Max(1, mainWidth - roomW + 1));
                        y = -roomH + 1;
                        break;
                }

                var candidate = (x: x, y: y, w: roomW, h: roomH, side: side);
                bool overlaps = false;
                foreach (var existing in rooms)
                {
                    // La pièce principale (side == -1) chevauche volontairement la pièce annexe
                    // d'1 tuile (mur commun) : on ne teste donc le chevauchement que contre les
                    // autres pièces annexes déjà placées, pas contre la pièce principale.
                    if (existing.side == -1)
                        continue;

                    bool intersects = !(candidate.x + candidate.w <= existing.x || existing.x + existing.w <= candidate.x
                        || candidate.y + candidate.h <= existing.y || existing.y + existing.h <= candidate.y);
                    if (intersects)
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                    rooms.Add(candidate);
            }

            int minX = rooms.Min(r => r.x);
            int minY = rooms.Min(r => r.y);
            int maxX = rooms.Max(r => r.x + r.w - 1);
            int maxY = rooms.Max(r => r.y + r.h - 1);
            int offsetX = -minX;
            int offsetY = -minY;

            var tiles = new Dictionary<(int x, int y), StructureTileData>();
            void SetTile(int x, int y, int groundTileId, int placedTileId = 0)
            {
                tiles[(x, y)] = new StructureTileData
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
            }

            void AddRoom(int x, int y, int w, int h)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    for (int dy = 0; dy < h; dy++)
                    {
                        int px = x + dx;
                        int py = y + dy;
                        bool border = dx == 0 || dy == 0 || dx == w - 1 || dy == h - 1;
                        SetTile(px, py, 7, border ? 8 : 0);
                    }
                }
            }

            void ClearWall(int x, int y)
            {
                if (!tiles.TryGetValue((x, y), out var wallTile))
                    return;

                wallTile.PlacedTileId = 0;
                wallTile.GroundTileId = 7;
                tiles[(x, y)] = wallTile;
            }

            void PlaceDoor(int x, int y, bool allowTwoDoors = false)
            {
                if (!tiles.TryGetValue((x, y), out var tile))
                    return;

                tile.PlacedTileId = 84;
                tile.GroundTileId = 7;
                tiles[(x, y)] = tile;
            }

            foreach (var room in rooms)
            {
                AddRoom(room.x + offsetX, room.y + offsetY, room.w, room.h);
            }

            foreach (var room in rooms.Skip(1))
            {
                int rx = room.x + offsetX;
                int ry = room.y + offsetY;
                int startIndex = 0;

                switch (room.side)
                {
                    case 0: // pièce à droite : le mur gauche de la pièce (dx=0, colonne rx)
                            // est exactement le mur droit de la maison principale (mur unique et partagé)
                        startIndex = rand.Next(1, Math.Max(2, room.h - 1));
                        int doorYRight = Math.Clamp(ry + startIndex, 1, ry + room.h - 2);
                        int sharedWallXRight = rx;
                        ClearWall(sharedWallXRight, doorYRight);
                        PlaceDoor(sharedWallXRight, doorYRight);
                        break;
                    case 1: // pièce en dessous : le mur du haut de la pièce (dy=0, ligne ry)
                            // est exactement le mur bas de la maison principale (mur unique et partagé)
                        startIndex = rand.Next(1, Math.Max(2, room.w - 1));
                        int doorXBottom = Math.Clamp(rx + startIndex, 1, rx + room.w - 2);
                        int sharedWallYBottom = ry;
                        ClearWall(doorXBottom, sharedWallYBottom);
                        PlaceDoor(doorXBottom, sharedWallYBottom);
                        break;
                    case 2: // pièce à gauche : le mur droit de la pièce (dx=room.w-1)
                            // est exactement le mur gauche de la maison principale (mur unique et partagé)
                        startIndex = rand.Next(1, Math.Max(2, room.h - 1));
                        int doorYLeft = Math.Clamp(ry + startIndex, 1, ry + room.h - 2);
                        int sharedWallXLeft = rx + room.w - 1;
                        ClearWall(sharedWallXLeft, doorYLeft);
                        PlaceDoor(sharedWallXLeft, doorYLeft);
                        break;
                    default: // pièce au-dessus : le mur bas de la pièce (dy=room.h-1)
                             // est exactement le mur haut de la maison principale (mur unique et partagé)
                        startIndex = rand.Next(1, Math.Max(2, room.w - 1));
                        int doorXTop = Math.Clamp(rx + startIndex, 1, rx + room.w - 2);
                        int sharedWallYTop = ry + room.h - 1;
                        ClearWall(doorXTop, sharedWallYTop);
                        PlaceDoor(doorXTop, sharedWallYTop);
                        break;
                }
            }

            int frontDoorCount = rand.NextDouble() < 0.7 ? 2 : 1;
            var usableDoorSides = new List<int> { 0, 1, 2, 3 };
            foreach (var room in rooms.Skip(1))
            {
                if (room.side == 0) usableDoorSides.Remove(1);
                if (room.side == 1) usableDoorSides.Remove(3);
                if (room.side == 2) usableDoorSides.Remove(0);
                if (room.side == 3) usableDoorSides.Remove(2);
            }
            if (usableDoorSides.Count == 0)
                usableDoorSides = new List<int> { 0, 1, 2, 3 };

            // Les entrées principales sont désormais toujours placées sur la face du bas.
            // La façade latérale n'est plus une option de génération valide.
            int frontDoorSide = 3;
            if (!usableDoorSides.Contains(frontDoorSide))
            {
                frontDoorSide = usableDoorSides[rand.Next(usableDoorSides.Count)];
            }

            // Position de la porte d'entrée EXTÉRIEURE (première porte de façade posée),
            // capturée explicitement : TryPlaceStructure choisit "la première tuile porte
            // (PlacedTileId == 84) rencontrée" comme doorPos utilisé pour relier la maison
            // au réseau routier. Or les portes intérieures reliant les pièces annexes à la
            // pièce principale (voir plus haut) ont, elles aussi, PlacedTileId == 84 et
            // peuvent se retrouver AVANT la porte de façade dans l'ordre d'insertion du
            // dictionnaire (qui suit l'ordre de balayage des murs, pas le sens logique
            // "extérieur / intérieur"). Résultat : doorPos pointait parfois vers une porte
            // intérieure totalement enfermée dans le bâtiment, donc impossible à relier
            // ("terrain géométriquement bloqué"). On force donc explicitement cette porte de
            // façade en tête de liste des tuiles ci-dessous, quel que soit l'ordre naturel.
            (int x, int y) mainEntranceWorldPos = (-1, -1);
            for (int doorIndex = 0; doorIndex < frontDoorCount; doorIndex++)
            {
                int dx = 0;
                int dy = 0;
                int baseX = 0;
                int baseY = 0;

                switch (frontDoorSide)
                {
                    case 0:
                        baseX = 0;
                        baseY = rand.Next(1, Math.Max(2, mainHeight - 1));
                        dy = baseY + (doorIndex == 0 ? 0 : rand.Next(2, 4));
                        dx = 0;
                        break;
                    case 1:
                        baseX = mainWidth - 1;
                        baseY = rand.Next(1, Math.Max(2, mainHeight - 1));
                        dy = baseY + (doorIndex == 0 ? 0 : rand.Next(2, 4));
                        dx = mainWidth - 1;
                        break;
                    case 2:
                        baseX = rand.Next(1, Math.Max(2, mainWidth - 1));
                        baseY = 0;
                        dx = baseX + (doorIndex == 0 ? 0 : rand.Next(2, 4));
                        dy = 0;
                        break;
                    default:
                        baseX = rand.Next(1, Math.Max(2, mainWidth - 1));
                        baseY = mainHeight - 1;
                        dx = baseX + (doorIndex == 0 ? 0 : rand.Next(2, 4));
                        dy = mainHeight - 1;
                        break;
                }

                int worldX = dx + offsetX;
                int worldY = dy + offsetY;
                if (tiles.TryGetValue((worldX, worldY), out var frontDoorTile))
                {
                    frontDoorTile.PlacedTileId = 84;
                    frontDoorTile.GroundTileId = 7;
                    tiles[(worldX, worldY)] = frontDoorTile;

                    if (doorIndex == 0)
                        mainEntranceWorldPos = (worldX, worldY);
                }
            }

            int totalWidth = maxX - minX + 1;
            int totalHeight = maxY - minY + 1;

            // La liste finale des tuiles doit présenter la porte de façade AVANT toute
            // porte intérieure : TryPlaceStructure prend la première tuile PlacedTileId==84
            // rencontrée comme "la" porte de la structure (doorPos, utilisée notamment pour
            // relier la maison au réseau routier du village).
            var orderedTiles = tiles.Values.ToList();
            if (mainEntranceWorldPos != (-1, -1))
            {
                int entranceIndex = orderedTiles.FindIndex(t => t.X == mainEntranceWorldPos.x && t.Y == mainEntranceWorldPos.y);
                if (entranceIndex > 0)
                {
                    var entranceTile = orderedTiles[entranceIndex];
                    orderedTiles.RemoveAt(entranceIndex);
                    orderedTiles.Insert(0, entranceTile);
                }
            }

            return new StructureData
            {
                Name = $"procedural_house_{rand.Next(1000, 9999)}",
                CreatedAt = DateTime.Now,
                MinX = 0,
                MinY = 0,
                MaxX = totalWidth - 1,
                MaxY = totalHeight - 1,
                Tiles = orderedTiles
            };
        }

        public static List<StructureData> GetStructures() => _structures;
    }
}