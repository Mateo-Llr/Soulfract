// World.Houses.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {

		private static HashSet<int> _currentIndoorGroupIds = new HashSet<int>();

		// Texture affichée à la base des pans "verticaux" du toit (la noue), là où deux
		// pentes se rejoignent en bas. Chargée une seule fois et mise en cache.
		private static Texture2D? _roofWallTex;

		private static Texture2D GetRoofWallTexture(Texture2D missingTex)
		{
			if (_roofWallTex == null)
			{
				var tex = Raylib.LoadTexture("assets/tiles/roof_wall.png");
				_roofWallTex = tex.Id != 0 ? tex : missingTex;
			}
			return _roofWallTex.Value;
		}

		// Texture de la pente qui MONTE (moitié gauche d'un pan vertical, vue de dessus).
		private static Texture2D? _roofUpTex;

		private static Texture2D GetRoofUpTexture(Texture2D missingTex)
		{
			if (_roofUpTex == null)
			{
				var tex = Raylib.LoadTexture("assets/tiles/roof_up.png");
				_roofUpTex = tex.Id != 0 ? tex : missingTex;
			}
			return _roofUpTex.Value;
		}

		// Texture de la pente qui DESCEND (moitié droite d'un pan vertical, vue de dessus).
		private static Texture2D? _roofDownTex;

		private static Texture2D GetRoofDownTexture(Texture2D missingTex)
		{
			if (_roofDownTex == null)
			{
				var tex = Raylib.LoadTexture("assets/tiles/roof_down.png");
				_roofDownTex = tex.Id != 0 ? tex : missingTex;
			}
			return _roofDownTex.Value;
		}

		private static readonly Dictionary<(string roofVariant, string direction), Texture2D> _roofVariantConnectionTextures = new();

		private static Texture2D GetRoofVariantConnectionTexture(Texture2D missingTex, string roofVariant, string direction)
		{
			var key = (roofVariant, direction);
			if (_roofVariantConnectionTextures.TryGetValue(key, out var cached))
				return cached;

			string path = $"assets/tiles/{roofVariant}_{direction}.png";
			Texture2D tex = new Texture2D();
			if (File.Exists(path))
			{
				var loaded = Raylib.LoadTexture(path);
				if (loaded.Id != 0)
					tex = loaded;
			}

			_roofVariantConnectionTextures[key] = tex;
			return tex;
		}

		public static void SetRoofHeight(int x, int y, int heightInTiles)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				if (heightInTiles <= 0)
				{
					chunk.RoofHeights.Remove((x, y));
				}
				else
				{
					chunk.RoofHeights[(x, y)] = heightInTiles;
				}
				}
			}

		// Détection d'un mur (vous pouvez étendre la liste)
		public static bool IsWall(int tileId)
		{
			return tileId == 8 || tileId == 84; // Brick Wall
		}

		// Détection d'un mur À MASQUER visuellement (sans les portes)
		private static bool IsWallToHide(int tileId)
		{
			return tileId == 8; // Brick Wall seulement, pas les portes (84)
		}

		// Une tuile d'intérieur n'est pas visible si elle correspond à un mur caché par
		// le toit d'une maison. Dans ce cas, il ne faut ni dessiner la bordure/recouvrement
		// ni afficher les connexions autour de cette tuile.
		private static bool HasVisibleInteriorTile(int tileX, int tileY)
		{
			if (!_isPlayerIndoors)
				return true;

			int buildingId = GetBuildingIdAt(tileX, tileY);
			if (buildingId == 0 || !_currentIndoorGroupIds.Contains(buildingId))
				return false;

			int objectId = GetObjectIdAt(tileX, tileY);
			if (IsWallToHide(objectId))
			{
				int tileAboveY = tileY - 1;
				if (GetOverlayAt(tileX, tileAboveY) == 82)
					return false;
			}

			return true;
		}

		private static int GetWallHeightInPixels(int x, int y)
		{
			int wallId = GetObjectIdAt(x, y);
			if (wallId != 0 && IsWall(wallId))
			{
				var wallTileData = WorldTileRegistry.GetTile(wallId);
				if (wallTileData?.Size != null && wallTileData.Size.Height > 1)
					return wallTileData.Size.Height * Program.TileSize;
			}
			return Program.TileSize;
		}

		private static int GetRoofHeightFromWall(int x, int y)
		{
			int bestHeightTiles = 1;
			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dy = -1; dy <= 1; dy++)
				{
					int checkX = x + dx;
					int checkY = y + dy;
					int wallId = GetObjectIdAt(checkX, checkY);
					if (wallId == 0 || !IsWall(wallId)) continue;

					var wallTileData = WorldTileRegistry.GetTile(wallId);
					int wallHeightTiles = 1;
					if (wallTileData?.Size != null && wallTileData.Size.Height > 1)
						wallHeightTiles = wallTileData.Size.Height;

					bestHeightTiles = Math.Max(bestHeightTiles, wallHeightTiles);
				}
			}
			return Math.Max(1, bestHeightTiles - 1);
		}

		public static int GetRoofHeight(int x, int y)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			var chunksDict = GetChunksDict();
			if (chunksDict.TryGetValue((chunkX, chunkY), out var chunk))
			{
				if (chunk.RoofHeights != null && chunk.RoofHeights.TryGetValue((x, y), out int h))
					return h;
			}
			return 1;
		}

		private static bool TryFindHouseBoundsForTile(int x, int y, out int minX, out int maxX, out int minY, out int maxY, out int doorX, out int doorY, out bool doorFound)
		{
			minX = maxX = minY = maxY = doorX = doorY = 0;
			doorFound = false;
			if (!IsWall(GetObjectIdAt(x, y)) && GetObjectIdAt(x, y) != 84)
				return false;

			var queue = new Queue<(int x, int y)>();
			var visited = new HashSet<(int x, int y)>();
			var wallTiles = new List<(int x, int y)>();
			queue.Enqueue((x, y));
			visited.Add((x, y));

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				wallTiles.Add(current);
				for (int dx = -1; dx <= 1; dx++)
				{
					for (int dy = -1; dy <= 1; dy++)
					{
						if (dx == 0 && dy == 0) continue;
						var next = (x: current.x + dx, y: current.y + dy);
						if (visited.Contains(next)) continue;
						if (IsWall(GetObjectIdAt(next.x, next.y)))
						{
							visited.Add(next);
							queue.Enqueue(next);
						}
					}
				}
			}

			if (wallTiles.Count < 4)
				return false;

			minX = wallTiles.Min(t => t.x);
			maxX = wallTiles.Max(t => t.x);
			minY = wallTiles.Min(t => t.y);
			maxY = wallTiles.Max(t => t.y);

			if (maxX - minX + 1 < 3 || maxY - minY + 1 < 3)
				return false;

			//  Même exclusion des coins qu'en (FindBuildingEntrance) : un coin est adossé
			// à un mur sur ses deux axes, ce qui rend le passage instable.
			// (les paramètres out ne peuvent pas être capturés directement dans une fonction
			// locale : on les recopie dans des variables locales normales)
			int boundsMinX = minX, boundsMaxX = maxX, boundsMinY = minY, boundsMaxY = maxY;
			bool IsCorner((int x, int y) t) => (t.x == boundsMinX || t.x == boundsMaxX) && (t.y == boundsMinY || t.y == boundsMaxY);
			foreach (var t in wallTiles)
			{
				if (GetObjectIdAt(t.x, t.y) == 84 && !IsCorner(t))
				{
					doorX = t.x;
					doorY = t.y;
					doorFound = true;
					break;
				}
			}

			return true;
		}

		private static void ClearRoofForBuilding(int buildingId)
		{
			var chunksDict = GetChunksDict();
			foreach (var chunk in chunksDict.Values)
			{
				var roofTiles = chunk.Overlays
					.Where(kvp => kvp.Value == 82 && BuildingIds.GetValueOrDefault(kvp.Key, 0) == buildingId)
					.Select(kvp => kvp.Key)
					.ToList();

				foreach (var (x, y) in roofTiles)
				{
					chunk.Overlays.Remove((x, y));
					BuildingIds.Remove((x, y));
					chunk.RoofHeights.Remove((x, y));
					_roofTileInfo.Remove((x, y));
				}
			}
		}

		private static bool HasAnyWallOrDoorInBounds(int minX, int maxX, int minY, int maxY)
		{
			for (int x = minX; x <= maxX; x++)
			{
				for (int y = minY; y <= maxY; y++)
				{
					if (IsWall(GetObjectIdAt(x, y)))
						return true;
				}
			}
			return false;
		}

		// Détermine les tuiles de toit d'une maison en s'assurant qu'elle est ENTIÈREMENT fermée par des murs.
		// Principe : on part de l'extérieur de la zone (avec une marge d'une tuile) et on "inonde" (flood fill)
		// toutes les tuiles non-mur atteignables depuis l'extérieur. Toute tuile non-mur qui n'est PAS atteinte
		// par cette inondation est forcément enfermée derrière des murs : c'est une tuile intérieure valide.
		// Si la maison a un trou dans ses murs, l'inondation "fuit" à l'intérieur et aucune tuile intérieure
		// n'est trouvée -> pas de toit tant que la maison n'est pas complètement close.
		private static bool TryGetEnclosedRoofTiles(int minX, int maxX, int minY, int maxY, int doorX, int doorY, out List<(int x, int y)> roofTiles)
		{
			roofTiles = new List<(int x, int y)>();
			if (maxX - minX + 1 < 3 || maxY - minY + 1 < 3)
				return false;

			// Marge d'une tuile autour de la zone : point de départ de l'inondation extérieure.
			int padMinX = minX - 1;
			int padMaxX = maxX + 1;
			int padMinY = minY - 1;
			int padMaxY = maxY + 1;

			// IsWall() couvre déjà les murs (8) et les portes (84), donc une porte est bien un obstacle
			// pour l'inondation, exactement comme un mur : la maison doit être totalement fermée.
			bool IsSolid(int x, int y) => IsWall(GetObjectIdAt(x, y));

			var wallTiles = new HashSet<(int x, int y)>();
			for (int x = minX; x <= maxX; x++)
			{
				for (int y = minY; y <= maxY; y++)
				{
					if (IsSolid(x, y))
						wallTiles.Add((x, y));
				}
			}

			if (wallTiles.Count == 0)
				return false;

			var outside = new HashSet<(int x, int y)>();
			var queue = new Queue<(int x, int y)>();

			void TryEnqueueOutside(int x, int y)
			{
				if (x < padMinX || x > padMaxX || y < padMinY || y > padMaxY) return;
				var p = (x, y);
				if (outside.Contains(p)) return;
				if (IsSolid(x, y)) return;
				outside.Add(p);
				queue.Enqueue(p);
			}

			for (int x = padMinX; x <= padMaxX; x++)
			{
				TryEnqueueOutside(x, padMinY);
				TryEnqueueOutside(x, padMaxY);
			}
			for (int y = padMinY; y <= padMaxY; y++)
			{
				TryEnqueueOutside(padMinX, y);
				TryEnqueueOutside(padMaxX, y);
			}

			while (queue.Count > 0)
			{
				var (cx, cy) = queue.Dequeue();
				TryEnqueueOutside(cx + 1, cy);
				TryEnqueueOutside(cx - 1, cy);
				TryEnqueueOutside(cx, cy + 1);
				TryEnqueueOutside(cx, cy - 1);
			}

			// Tuile intérieure = non-mur, dans la zone d'origine, jamais atteinte depuis l'extérieur.
			var interiorTiles = new List<(int x, int y)>();
			for (int x = minX; x <= maxX; x++)
			{
				for (int y = minY; y <= maxY; y++)
				{
					if (IsSolid(x, y)) continue;
					if (outside.Contains((x, y))) continue;
					interiorTiles.Add((x, y));
				}
			}

			// Aucune tuile intérieure enfermée trouvée => la maison n'est pas complètement close
			// (mur manquant, trou dans le périmètre, etc.) : pas de toit.
			if (interiorTiles.Count == 0)
				return false;

			roofTiles = wallTiles
				.Concat(interiorTiles)
				.Distinct()
				.ToList();

			return roofTiles.Count > 0;
		}

		// Un pan de toit est soit "horizontal" (le bâtiment est plus large que profond : reste
		// PLAT, à la hauteur classique, comme un toit normal), soit "vertical" (plus profond
		// que large). Pour un pan vertical, CHAQUE COLONNE garde la hauteur classique comme
		// base, puis on empile des roof_wall pour monter jusqu'à la hauteur de cette colonne
		// (1 empilement sur les bords, +1 par colonne en s'approchant du milieu). Les tuiles
		// de toit normales de cette colonne (une par rangée de profondeur) se posent ensuite
		// par-dessus, toutes décalées de la même hauteur, avec la texture roof_up (moitié
		// gauche), roof_down (moitié droite) ou classique (colonne du milieu exacte, uniquement
		// si la largeur est impaire).
		public enum RoofTexKind { Default, Up, Down, Classic }

		public readonly struct RoofTileInfo
		{
			public readonly int Height;            // décalage total en tuiles (hauteur classique + empilement)
			public readonly bool IsHorizontalZone;
			public readonly int WallStackCount;     // nb de roof_wall à empiler sous cette colonne (0 sur les bords)
			public readonly RoofTexKind TexKind;
			public readonly bool IsColumnAnchor;     // vrai sur la tuile la plus basse de la colonne : c'est elle qui déclenche l'empilement des roof_wall
			public RoofTileInfo(int height, bool isHorizontalZone, int wallStackCount, RoofTexKind texKind, bool isColumnAnchor)
			{
				Height = height; IsHorizontalZone = isHorizontalZone;
				WallStackCount = wallStackCount; TexKind = texKind; IsColumnAnchor = isColumnAnchor;
			}
		}

		// Table calculée à chaque (ré)application d'un toit, consultée par le rendu pour
		// choisir la texture / l'ordre de dessin de chaque tuile. Clé = (x,y) monde.
		private static readonly Dictionary<(int x, int y), RoofTileInfo> _roofTileInfo = new();

		public static bool TryGetRoofTileInfo(int x, int y, out RoofTileInfo info)
			=> _roofTileInfo.TryGetValue((x, y), out info);

		public static Dictionary<(int x, int y), RoofTileInfo> GetRoofTileInfoSnapshot()
			=> new Dictionary<(int x, int y), RoofTileInfo>(_roofTileInfo);

		public static void RestoreRoofTileInfo(Dictionary<(int x, int y), RoofTileInfo> roofTileInfo)
		{
			if (roofTileInfo == null) return;
			foreach (var kv in roofTileInfo)
				_roofTileInfo[kv.Key] = kv.Value;
		}

		public static void RestoreRoofTileInfoForChunk(ChunkData chunk)
		{
			if (chunk == null) return;
			chunk.RoofTileInfo ??= new Dictionary<(int x, int y), RoofTileInfo>();
			foreach (var key in chunk.RoofTileInfo.Keys.ToList())
				_roofTileInfo.Remove(key);
			RestoreRoofTileInfo(chunk.RoofTileInfo);
		}

		// Un pied de toit n'est pas forcément un rectangle (ex: forme en L composée d'une
		// aile 7x4 et d'une aile 5x8). On le découpe en zones rectangulaires en balayant des
		// lignes de coupe VERTICALES de gauche à droite : pour chaque colonne x, on relève
		// l'étendue verticale (yStart..yEnd) occupée par le toit. Tant que deux colonnes
		// consécutives ont exactement la même étendue, elles appartiennent au même
		// quadrilatère (on avance juste le bord droit) ; dès que l'étendue change, la zone se
		// referme et une nouvelle commence à cette colonne. Chaque quadrilatère ainsi obtenu
		// va donc bien "d'un point à l'autre" et est ensuite comparé en largeur/hauteur pour
		// savoir s'il est horizontal ou vertical.
		private static List<(int minX, int maxX, int minY, int maxY)> DecomposeFootprintIntoRectangles(
			List<(int x, int y)> roofTileList)
		{
			var zones = new List<(int minX, int maxX, int minY, int maxY)>();
			if (roofTileList.Count == 0) return zones;

			int minX = roofTileList.Min(t => t.x);
			int maxX = roofTileList.Max(t => t.x);
			var footprint = new HashSet<(int x, int y)>(roofTileList);

			// Pour chaque colonne, la ou les étendues verticales contiguës occupées.
			List<(int yStart, int yEnd)> GetColumnRuns(int x)
			{
				var ys = footprint.Where(t => t.x == x).Select(t => t.y).OrderBy(v => v).ToList();
				var runs = new List<(int yStart, int yEnd)>();
				int i = 0;
				while (i < ys.Count)
				{
					int start = ys[i];
					int end = start;
					while (i + 1 < ys.Count && ys[i + 1] == end + 1) { end = ys[i + 1]; i++; }
					runs.Add((start, end));
					i++;
				}
				return runs;
			}

			// Zones actives (une par étendue verticale en cours de balayage) : bord gauche
			// où elles ont commencé, et l'étendue (yStart,yEnd) qui les définit.
			var active = new List<(int xStart, int yStart, int yEnd)>();

			for (int x = minX; x <= maxX + 1; x++)
			{
				var runs = x <= maxX ? GetColumnRuns(x) : new List<(int yStart, int yEnd)>();

				// Les zones actives dont l'étendue n'existe plus dans cette colonne se referment.
				for (int i = active.Count - 1; i >= 0; i--)
				{
					var a = active[i];
					if (!runs.Any(r => r.yStart == a.yStart && r.yEnd == a.yEnd))
					{
						zones.Add((a.xStart, x - 1, a.yStart, a.yEnd));
						active.RemoveAt(i);
					}
				}

				// Les étendues de cette colonne qui ne prolongent aucune zone active en démarrent une nouvelle.
				foreach (var run in runs)
				{
					if (!active.Any(a => a.yStart == run.yStart && a.yEnd == run.yEnd))
						active.Add((x, run.yStart, run.yEnd));
				}
			}

			return zones;
		}

		// Calcule pour chaque tuile d'une zone rectangulaire :
		// - orientation horizontale (largeur >= profondeur) -> toit plat à la hauteur
		//   classique (roofHeightTiles), comme avant, aucun changement de position ;
		// - orientation verticale (profondeur > largeur) -> chaque COLONNE garde la hauteur
		//   classique comme base, puis empile 1, 2, 3... roof_wall en s'approchant du milieu
		//   (1 sur les bords), et les tuiles de toit de cette colonne (une par rangée de
		//   profondeur) se posent par-dessus, toutes décalées de la même hauteur totale.
		private static Dictionary<(int x, int y), RoofTileInfo> ComputeRoofHeights(
			List<(int x, int y)> roofTileList, int roofHeightTiles)
		{
			var result = new Dictionary<(int x, int y), RoofTileInfo>();
			var zones = DecomposeFootprintIntoRectangles(roofTileList);

			foreach (var (zMinX, zMaxX, zMinY, zMaxY) in zones)
			{
				int width = zMaxX - zMinX + 1;
				int depth = zMaxY - zMinY + 1;
				bool isHorizontal = width >= depth;
				// "Bas" de la colonne = rangée la plus proche du spectateur (Y le plus grand) :
				// c'est là que l'empilement de roof_wall démarre, "en partant du bas".
				int columnAnchorY = zMaxY;
				int middleIndex = (width - 1) / 2; // colonne du milieu exacte si largeur impaire

				for (int x = zMinX; x <= zMaxX; x++)
				{
					int wallStackCount = 0;
					RoofTexKind texKind = RoofTexKind.Default;

					if (!isHorizontal)
					{
						int i = x - zMinX; // index de colonne, 0-based depuis la gauche de la zone
						wallStackCount = Math.Min(i, width - 1 - i) + 1; // 1 sur les bords, +1 en s'approchant du milieu

						if (width % 2 == 1 && i == middleIndex)
							texKind = RoofTexKind.Classic; // colonne du milieu exacte
						else if (i < middleIndex || (width % 2 == 0 && i < width / 2))
							texKind = RoofTexKind.Up;      // moitié gauche
						else
							texKind = RoofTexKind.Down;    // moitié droite
					}

					int height = roofHeightTiles + wallStackCount;

					for (int y = zMinY; y <= zMaxY; y++)
					{
						bool isColumnAnchor = !isHorizontal && y == columnAnchorY;
						result[(x, y)] = new RoofTileInfo(height, isHorizontal, wallStackCount, texKind, isColumnAnchor);
					}
				}
			}

			return result;
		}

		private static void ApplyRoofToHouse(int buildingId, IEnumerable<(int x, int y)> roofTiles)
		{
			var roofTileList = roofTiles.ToList();
			ClearRoofForBuilding(buildingId);
			if (roofTileList.Count == 0)
				return;

			// Une même maison a UNE seule hauteur de FAÎTAGE (celle de son mur le plus haut) :
			// c'est le plafond de la fonction de hauteur ci-dessous, pas une hauteur uniforme
			// appliquée à toutes les tuiles.
			int roofHeightTiles = 1;
			foreach (var (x, y) in roofTileList)
			{
				if (!IsWall(GetObjectIdAt(x, y))) continue;
				roofHeightTiles = Math.Max(roofHeightTiles, GetRoofHeightFromWall(x, y));
			}

			var infoByTile = ComputeRoofHeights(roofTileList, roofHeightTiles);

			foreach (var (x, y) in roofTileList)
			{
				var info = infoByTile[(x, y)];
				_roofTileInfo[(x, y)] = info;

				if (GetOverlayAt(x, y) == 82 && BuildingIds.GetValueOrDefault((x, y), 0) == buildingId
					&& GetRoofHeight(x, y) == info.Height) continue;

				AddOverlay(x, y, 82);
				BuildingIds[(x, y)] = buildingId;
				SetRoofHeight(x, y, info.Height);
			}
		}

		public static void ApplyRoofToPlacedStructure(StructureData structure, int baseX, int baseY, int buildingId, (int x, int y) doorPos)
		{
			if (structure?.Tiles == null || structure.Tiles.Count == 0)
				return;

			int minX = int.MaxValue;
			int maxX = int.MinValue;
			int minY = int.MaxValue;
			int maxY = int.MinValue;

			foreach (var tile in structure.Tiles)
			{
				int worldX = baseX + tile.X;
				int worldY = baseY + tile.Y;
				minX = Math.Min(minX, worldX);
				maxX = Math.Max(maxX, worldX);
				minY = Math.Min(minY, worldY);
				maxY = Math.Max(maxY, worldY);
			}

			if (maxX - minX + 1 < 3 || maxY - minY + 1 < 3)
				return;

			if (!HasAnyWallOrDoorInBounds(minX, maxX, minY, maxY))
			{
				ClearRoofForBuilding(buildingId);
				RefreshAllHouses();
				return;
			}

			if (!TryGetEnclosedRoofTiles(minX, maxX, minY, maxY, doorPos.x, doorPos.y, out var roofTiles))
			{
				ClearRoofForBuilding(buildingId);
				RefreshAllHouses();
				return;
			}

			ApplyRoofToHouse(buildingId, roofTiles);
			RefreshAllHouses();
		}

		// Met à jour les toits dans une zone autour de (x,y)
		public static void UpdateRoofsAt(int x, int y)
		{
			if (!TryFindHouseBoundsForTile(x, y, out int minX, out int maxX, out int minY, out int maxY, out int doorX, out int doorY, out bool doorFound))
				return;

			int refX = doorFound ? doorX : x;
			int refY = doorFound ? doorY : y;

			int buildingId = ComputeBuildingId(refX, refY);
			if (!HasAnyWallOrDoorInBounds(minX, maxX, minY, maxY))
			{
				ClearRoofForBuilding(buildingId);
				RefreshAllHouses();
				return;
			}

			if (!TryGetEnclosedRoofTiles(minX, maxX, minY, maxY, refX, refY, out var roofTiles))
			{
				ClearRoofForBuilding(buildingId);
				RefreshAllHouses();
				return;
			}

			ApplyRoofToHouse(buildingId, roofTiles);
			RefreshAllHouses();
		}

		public static Dictionary<int, HouseData> Houses = new();

		//  Il n'existe pas de tuile "porte" distincte dans ce jeu : l'ID 84 choisi comme
		// "porte" par TryFindHouseBoundsForTile / FindBuildingEntrance est en réalité le mur
		// en bois (voir Program.cs : "Les murs sont les IDs 8 (mur de pierre) et 84 (mur de
		// bois)"), exactement le même objet solide que n'importe quel autre mur du périmètre.
		// Sans intervention, la case "porte" bloque donc la collision physique exactement
		// comme un mur plein : les PNJ marchent jusqu'à elle et restent coincés dehors, ne
		// pouvant jamais entrer. Ce registre retient les coordonnées précises de la tuile
		// "porte" de chaque maison pour qu'on puisse y percer un passage physique (dans
		// CheckCollision) et logique (dans Pathfinder), sans toucher aux autres murs en
		// bois identiques qui doivent, eux, rester bloquants.
		private static readonly HashSet<(int x, int y)> _doorTiles = new();

		/// <summary>
		/// Trouve la place assise (banc/chaise) libre la plus proche située À L'INTÉRIEUR
		/// d'une maison (définie par son BuildingId). Utilisé pour permettre aux
		/// villageois de s'asseoir à l'intérieur si un siège existe.
		/// </summary>
		public static ((int x, int y, int index) key, Vector2 seatPos, Vector2 approachPos)? FindNearestFreeHouseSeat(int buildingId, Vector2 worldPos, float maxDistance = 800f)
		{
			if (Benches.Count == 0)
				RefreshAllBenches();

			if (!Houses.TryGetValue(buildingId, out var house))
				return null;

			(int x, int y, int index)? bestKey = null;
			BenchSeat? bestSeat = null;
			float bestDistSq = maxDistance * maxDistance;

			foreach (var kvp in Benches)
			{
				var seat = kvp.Value;
				if (seat.Occupied) continue;

				// Vérifier que l'ancre du banc/siège est à l'intérieur des bounds de la maison
				int bx = (int)house.Bounds.X;
				int by = (int)house.Bounds.Y;
				int bw = (int)house.Bounds.Width;
				int bh = (int)house.Bounds.Height;
				if (seat.AnchorTile.x < bx || seat.AnchorTile.x >= bx + bw) continue;
				if (seat.AnchorTile.y < by || seat.AnchorTile.y >= by + bh) continue;

				float distSq = Vector2.DistanceSquared(worldPos, seat.ApproachWorldPos);
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					bestKey = kvp.Key;
					bestSeat = seat;
				}
			}

			if (bestKey.HasValue && bestSeat != null)
			{
				bestSeat.Occupied = true;
				return (bestKey.Value, bestSeat.SeatWorldPos, bestSeat.ApproachWorldPos);
			}

			return null;
		}

		public static Vector2? FindNearestAvailableSleepPosition(int buildingId, Vector2 worldPos, Entity requester)
		{
			if (buildingId == Entity.TentHomeBuildingId)
				return FindNearestAvailableTentSleepPosition(worldPos, 12, requester);

			if (!Houses.TryGetValue(buildingId, out var house))
				return null;

			int minX = (int)house.Bounds.X;
			int minY = (int)house.Bounds.Y;
			int maxX = minX + (int)house.Bounds.Width;
			int maxY = minY + (int)house.Bounds.Height;
			Vector2? bestPosition = null;
			float bestDistanceSq = float.MaxValue;

			for (int x = minX; x < maxX; x++)
			{
				for (int y = minY; y < maxY; y++)
				{
					var tileData = WorldTileRegistry.GetTile(GetObjectIdAt(x, y));
					if (tileData?.IsSleepable != true)
						continue;

					Vector2 sleepPosition = Program.GetFurnitureAnchorPosition(x, y, tileData);
					if (Program.IsSleepPositionOccupied(sleepPosition, requester))
						continue;

					float distanceSq = Vector2.DistanceSquared(worldPos, sleepPosition);
					if (distanceSq < bestDistanceSq)
					{
						bestDistanceSq = distanceSq;
						bestPosition = sleepPosition;
					}
				}
			}

			return bestPosition;
		}

		public static Vector2? FindNearestSleepPosition(int buildingId, Vector2 worldPos)
		{
			if (buildingId == Entity.TentHomeBuildingId)
				return FindNearestTentSleepPosition(worldPos, 12);

			if (!Houses.TryGetValue(buildingId, out var house))
				return null;

			int minX = (int)house.Bounds.X;
			int minY = (int)house.Bounds.Y;
			int maxX = minX + (int)house.Bounds.Width;
			int maxY = minY + (int)house.Bounds.Height;
			Vector2? bestPosition = null;
			float bestDistanceSq = float.MaxValue;

			for (int x = minX; x < maxX; x++)
			{
				for (int y = minY; y < maxY; y++)
				{
					var tileData = WorldTileRegistry.GetTile(GetObjectIdAt(x, y));
					if (tileData?.IsSleepable != true)
						continue;

					Vector2 sleepPosition = Program.GetFurnitureAnchorPosition(x, y, tileData);
					float distanceSq = Vector2.DistanceSquared(worldPos, sleepPosition);
					if (distanceSq < bestDistanceSq)
					{
						bestDistanceSq = distanceSq;
						bestPosition = sleepPosition;
					}
				}
			}

			return bestPosition;
		}

		/// <summary>
		/// Renvoie un spot intérieur aléatoire (WorldPos) pour la maison donnée, ou Vector2.Zero
		/// si aucun spot n'est disponible.
		/// </summary>
		public static Vector2 PickRandomInteriorSpot(int buildingId)
		{
			if (!HouseInteriorSpots.TryGetValue(buildingId, out var spots) || spots.Count == 0)
				return Vector2.Zero;

			var candidates = spots.Select(s => s.WorldPos).ToList();
			int idx = Random.Shared.Next(candidates.Count);
			return candidates[idx];
		}

		/// <summary>
		/// Renvoie (et met en cache sur la HouseData) la position monde du coffre commun de la
		/// maison, trouvé en scannant les tuiles de Bounds à la recherche d'un conteneur posé
		/// (coffre fabriqué par le joueur ou un PNJ). Renvoie null si la maison n'a pas encore
		/// de coffre — dans ce cas rien n'est mis en cache, pour permettre une détection
		/// automatique si un coffre est posé plus tard.
		/// </summary>
		//  Un meuble est un VRAI coffre (par opposition à une étagère, un tonneau, une
		// caisse... qui portent aussi IsContainer=true dans worldobjects.json) si son nom
		// contient explicitement "coffre"/"chest", ou s'il partage l'ID du coffre par
		// défaut créé par CreateDefaultHouseChest (HOUSE_CHEST_OBJECT_ID).
		private static bool IsRealChestTile(int objectId, WorldTileData? tileData)
		{
			if (objectId == HOUSE_CHEST_OBJECT_ID) return true;
			if (tileData == null) return false;
			string name = tileData.Name ?? "";
			return name.IndexOf("coffre", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("chest", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		public static Vector2? GetOrFindHouseChest(int buildingId)
		{
			if (!Houses.ContainsKey(buildingId))
			{
				var resident = Program.GetEntities().FirstOrDefault(entity =>
					entity.HomeBuildingId == buildingId && entity.HomePosition != Vector2.Zero);
				if (resident != null)
				{
					int homeTileX = (int)(resident.HomePosition.X / Program.TileSize);
					int homeTileY = (int)(resident.HomePosition.Y / Program.TileSize);
					int homeChunkX = homeTileX / CHUNK_SIZE;
					int homeChunkY = homeTileY / CHUNK_SIZE;
					if (homeTileX < 0) homeChunkX = (homeTileX - CHUNK_SIZE + 1) / CHUNK_SIZE;
					if (homeTileY < 0) homeChunkY = (homeTileY - CHUNK_SIZE + 1) / CHUNK_SIZE;
					LoadOrGenerateChunk(homeChunkX, homeChunkY, Program.GetGameTime(), Program.GetEntities());
				}

				RefreshAllHouses();
				if (!Houses.ContainsKey(buildingId))
					TryRegisterHouseFromBuildingIds(buildingId);
				if (!Houses.ContainsKey(buildingId))
					return null;
			}

			if (!Houses.TryGetValue(buildingId, out var house))
				return null;

			//  CORRECTIF : StorageChestPos était renvoyé tel quel dès qu'il avait une valeur,
			// sans jamais être revalidé. Or la recherche ci-dessous privilégie un VRAI coffre
			// mais accepte n'importe quel meuble-conteneur (étagère, tonneau...) en repli s'il
			// en trouve un avant - et cette valeur de repli était mise en cache exactement comme
			// un vrai coffre. Une maison générée avec un meuble-conteneur déjà présent (ou un
			// coffre par défaut auto-créé la première fois qu'un PNJ a eu besoin de déposer,
			// potentiellement avant que le joueur ne pose son propre coffre) figeait donc
			// StorageChestPos pour toujours sur ce meuble/repli : un vrai coffre posé ensuite par
			// le joueur ailleurs dans la maison n'était alors plus jamais détecté, les PNJ
			// continuant de viser l'ancien conteneur indéfiniment. On revalide donc la case mise
			// en cache à chaque appel : si l'objet qui s'y trouve a disparu, OU si ce n'est qu'un
			// simple conteneur de repli (pas un vrai coffre), on relance la recherche complète
			// ci-dessous plutôt que de renvoyer aveuglément l'ancienne valeur - elle pourra alors
			// détecter un vrai coffre posé depuis. Un vrai coffre déjà en cache, lui, reste utilisé
			// tel quel (pas de rescan inutile à chaque appel).
			if (house.StorageChestPos.HasValue)
			{
				int cachedTx = (int)(house.StorageChestPos.Value.X / Program.TileSize);
				int cachedTy = (int)(house.StorageChestPos.Value.Y / Program.TileSize);
				var cachedChunk = GetChunkAt(cachedTx, cachedTy);
				if (cachedChunk == null)
				{
					int chunkX = cachedTx / CHUNK_SIZE;
					int chunkY = cachedTy / CHUNK_SIZE;
					if (cachedTx < 0) chunkX = (cachedTx - CHUNK_SIZE + 1) / CHUNK_SIZE;
					if (cachedTy < 0) chunkY = (cachedTy - CHUNK_SIZE + 1) / CHUNK_SIZE;
					LoadOrGenerateChunk(chunkX, chunkY, Program.GetGameTime(), Program.GetEntities());
					cachedChunk = GetChunkAt(cachedTx, cachedTy);
				}
				int cachedObjectId = 0;
				bool cachedObjectExists = cachedChunk != null
					&& cachedChunk.Objects.TryGetValue((cachedTx, cachedTy), out cachedObjectId)
					&& cachedObjectId != 0;
				var cachedTileData = cachedObjectExists ? WorldTileRegistry.GetTile(cachedObjectId) : null;
				bool cachedIsRealChest = cachedTileData != null && IsRealChestTile(cachedObjectId, cachedTileData);

				if (cachedIsRealChest)
				{
					EnsureHouseChestContainer(cachedChunk!, cachedTx, cachedTy, cachedTileData!);
					return house.StorageChestPos.Value;
				}

				// Repli disparu, ou simple meuble de repli : on oublie le cache et on retente
				// une recherche complète juste en dessous (elle pourra retomber sur le même
				// repli si aucun vrai coffre n'existe toujours, ce qui reste sans danger).
				house.StorageChestPos = null;
			}

			int bx = (int)house.Bounds.X;
			int by = (int)house.Bounds.Y;
			int bw = (int)house.Bounds.Width;
			int bh = (int)house.Bounds.Height;

			//  IMPORTANT : deux passes plutôt qu'une. Une maison meublée contient souvent
			// PLUSIEURS objets "conteneur" (étagère, tonneau, caisse...) en plus du vrai
			// coffre — tous partagent IsContainer=true dans worldobjects.json. Un simple
			// "premier trouvé" scannait la maison ligne par ligne et pouvait très bien
			// tomber sur un tonneau près de la porte avant d'atteindre le coffre : le PNJ
			// y déposait alors ses objets sans que ça n'apparaisse jamais dans LE coffre
			// que le joueur ouvre et surveille, qui restait donc vide en permanence. On
			// cherche donc d'abord spécifiquement un vrai coffre (nom "coffre"/"chest", ou
			// même ID que celui créé par défaut) dans toute la maison, et on ne se rabat
			// sur n'importe quel conteneur que si aucun vrai coffre n'existe.
			(int x, int y)? fallbackAnyContainer = null;

			for (int pass = 0; pass < 2; pass++)
			{
				bool requireRealChest = pass == 0;

				for (int tx = bx; tx < bx + bw; tx++)
				{
					for (int ty = by; ty < by + bh; ty++)
					{
						var chunk = GetChunkAt(tx, ty);
						if (chunk == null) continue;

						//  IMPORTANT : un coffre posé par le joueur n'a PAS de
						// ContainerInventoryData tant qu'il n'a jamais été ouvert au moins
						// une fois (voir Program_Core.cs, l'interaction "CONTENEURS", qui
						// crée les données à la volée au premier clic). On détecte donc la
						// tuile-objet elle-même plutôt que de se fier uniquement aux
						// données déjà créées, pour que le PNJ trouve le VRAI coffre posé
						// même si personne ne l'a encore ouvert.
						if (!chunk.Objects.TryGetValue((tx, ty), out int objectId) || objectId == 0)
							continue;
						var tileData = WorldTileRegistry.GetTile(objectId);
						if (tileData?.IsContainer != true)
							continue;

						bool isRealChest = IsRealChestTile(objectId, tileData);
						if (requireRealChest && !isRealChest)
						{
							if (!fallbackAnyContainer.HasValue)
								fallbackAnyContainer = (tx, ty);
							continue;
						}

						EnsureHouseChestContainer(chunk, tx, ty, tileData);

						var pos = new Vector2(tx * Program.TileSize + Program.TileSize / 2f,
											   ty * Program.TileSize + Program.TileSize / 2f);
						house.StorageChestPos = pos;
						return pos;
					}
				}

				// Pas de vrai coffre trouvé lors de la 1ère passe : si un conteneur
				// quelconque a été repéré au passage, on l'utilise plutôt que de créer un
				// coffre par défaut supplémentaire.
				if (requireRealChest && fallbackAnyContainer.HasValue)
				{
					var (tx, ty) = fallbackAnyContainer.Value;
					var chunk = GetChunkAt(tx, ty);
					if (chunk != null)
					{
						if (chunk.Objects.TryGetValue((tx, ty), out int objectId))
						{
							var tileData = WorldTileRegistry.GetTile(objectId);
							if (tileData?.IsContainer == true)
								EnsureHouseChestContainer(chunk, tx, ty, tileData);
						}

						var pos = new Vector2(tx * Program.TileSize + Program.TileSize / 2f,
											   ty * Program.TileSize + Program.TileSize / 2f);
						house.StorageChestPos = pos;
						return pos;
					}
				}
			}

			//  Vraiment aucun coffre (ni objet, ni données) dans la maison : on en crée
			// un par défaut sur une case intérieure libre, sinon les PNJ ne pourront
			// jamais déposer/retirer quoi que ce soit.
			return CreateDefaultHouseChest(buildingId, house);
		}

		private static bool TryRegisterHouseFromBuildingIds(int buildingId)
		{
			var tiles = BuildingIds
				.Where(entry => entry.Value == buildingId)
				.Select(entry => entry.Key)
				.ToList();
			if (tiles.Count == 0)
				return false;

			int minX = tiles.Min(tile => tile.x);
			int maxX = tiles.Max(tile => tile.x);
			int minY = tiles.Min(tile => tile.y);
			int maxY = tiles.Max(tile => tile.y);
			(int doorX, int doorY) = FindBuildingEntrance(minX, maxX, minY, maxY);
			if (doorX == -1)
			{
				var savedDoor = tiles.FirstOrDefault(tile => GetObjectIdAt(tile.x, tile.y) == 84);
				if (GetObjectIdAt(savedDoor.x, savedDoor.y) == 84)
				{
					doorX = savedDoor.x;
					doorY = savedDoor.y;
				}
				else
				{
					var resident = Program.GetEntities().FirstOrDefault(entity =>
						entity.HomeBuildingId == buildingId && entity.HomePosition != Vector2.Zero);
					var fallbackTile = resident != null
						? ((int)(resident.HomePosition.X / Program.TileSize), (int)(resident.HomePosition.Y / Program.TileSize))
						: tiles[0];
					doorX = fallbackTile.Item1;
					doorY = fallbackTile.Item2;
				}
			}

			var roofTiles = new HashSet<(int x, int y)>(tiles);
			var house = new HouseData
			{
				BuildingId = buildingId,
				DoorX = doorX,
				DoorY = doorY,
				DoorWorldPos = new Vector2(doorX * Program.TileSize + Program.TileSize / 2f,
					doorY * Program.TileSize + Program.TileSize / 2f - Program.FeetOffsetY),
				InteriorPosition = FindInteriorPosition(doorX, doorY, minX, maxX, minY, maxY),
				Bounds = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1),
				RoofTiles = roofTiles
			};

			Houses[buildingId] = house;
			if (GetObjectIdAt(doorX, doorY) == 84)
				RegisterDoorTile(doorX, doorY);
			RegisterHouseInteriorSpots(buildingId, tiles, doorX, doorY);
			return true;
		}

		private static Vector2? CreateDefaultHouseChest(int buildingId, HouseData house)
		{
			//  CORRECTIF : avant de créer quoi que ce soit, revérifier qu'aucun vrai coffre
			// n'existe déjà dans la maison. GetOrFindHouseChest ne nous a envoyés ici que
			// parce que SON scan (basé sur house.Bounds) n'a rien trouvé - or house.Bounds
			// reste (0,0,0,0) pour toute maison enregistrée via RegisterHouse (chemin de la
			// génération initiale du village), tant qu'aucun rechargement de chunk n'a
			// déclenché RebuildHousesFromRoofs. Un coffre par défaut posé plus tôt par un
			// autre PNJ (ou par CE PNJ lors d'un appel précédent dont le cache a ensuite été
			// perdu, ex. HouseData recréée par RefreshAllHouses sans StorageChestPos) reste
			// donc invisible à ce scan, et on repartait créer un second coffre - typiquement
			// sur la case libre suivante dans HouseInteriorSpots, juste à côté (souvent
			// littéralement en dessous) du premier. On revérifie ici sur TOUTES les tuiles
			// connues de la maison (HouseInteriorSpots, RoofTiles, Bounds), qui ne dépendent
			// pas toutes du même champ bugué, avant d'accepter de créer un nouveau coffre.
			var tilesToCheck = new HashSet<(int x, int y)>();
			if (HouseInteriorSpots.TryGetValue(buildingId, out var existingSpots))
				foreach (var s in existingSpots) tilesToCheck.Add(s.Tile);
			foreach (var t in house.RoofTiles) tilesToCheck.Add(t);
			if (house.Bounds.Width > 0 && house.Bounds.Height > 0)
			{
				for (int tx = (int)house.Bounds.X; tx < house.Bounds.X + house.Bounds.Width; tx++)
					for (int ty = (int)house.Bounds.Y; ty < house.Bounds.Y + house.Bounds.Height; ty++)
						tilesToCheck.Add((tx, ty));
			}

			foreach (var (tx, ty) in tilesToCheck)
			{
				var existingChunk = GetChunkAt(tx, ty);
				if (existingChunk == null) continue;
				if (!existingChunk.Objects.TryGetValue((tx, ty), out int existingObjectId) || existingObjectId == 0)
					continue;
				var existingTileData = WorldTileRegistry.GetTile(existingObjectId);
				if (existingTileData == null || !IsRealChestTile(existingObjectId, existingTileData))
					continue;

				EnsureHouseChestContainer(existingChunk, tx, ty, existingTileData);
				var existingPos = new Vector2(tx * Program.TileSize + Program.TileSize / 2f,
											   ty * Program.TileSize + Program.TileSize / 2f);
				house.StorageChestPos = existingPos;
				return existingPos;
			}

			(int x, int y)? tile = null;

			//  IMPORTANT : ne JAMAIS choisir une case qui sert déjà de position de sommeil
			// (HomePosition) à un PNJ. HouseInteriorSpots est la MÊME liste que celle utilisée
			// pour assigner les cases de lit (voir FindNearestFreeHouseSpot) : dans une petite
			// maison à une seule case intérieure valide, cette case est déjà "Occupied" par le
			// PNJ lui-même. Avant ce correctif, le repli `spots[0].Tile` retombait alors
			// systématiquement sur CETTE case, y posait un objet coffre (walkable=false), et
			// supprimait ainsi la seule case praticable de la maison. Résultat observé : plus
			// aucune case d'approche possible pour GetHouseChestApproachPosition, qui renvoie
			// null pour toujours, et le PNJ reste figé indéfiniment sur sa case de sommeil sans
			// jamais aller déposer son inventaire (UpdateSleepState réessaie chaque frame en
			// vain). On exclut donc explicitement toute case correspondant à la HomePosition
			// d'un PNJ vivant, en plus du simple flag Occupied.
			var occupiedHomeTiles = new HashSet<(int x, int y)>();
			foreach (var e in Program.entities)
			{
				if (e.HomeBuildingId != buildingId || e.HomePosition == Vector2.Zero) continue;
				occupiedHomeTiles.Add(((int)(e.HomePosition.X / Program.TileSize), (int)(e.HomePosition.Y / Program.TileSize)));
			}

			//  CORRECTIF : un HouseInteriorSpot "libre" ne l'est qu'au sens du champ Occupied
			// (réservation de lit), pas forcément au sens du terrain actuel - RegisterHouseInteriorSpots
			// n'est reconstruite qu'à un rechargement complet de chunk, alors qu'un meuble (dont un
			// coffre) peut avoir été posé sur une de ces cases entre-temps sans jamais retirer la case
			// de la liste ni marquer Occupied. Sans revérification ici, on risquait de placer un second
			// coffre par-dessus un objet déjà présent sur la case choisie. On ne retient donc qu'une
			// case réellement vide de tout objet au moment de l'appel.
			bool IsTileFreeOfObjects((int x, int y) t)
			{
				var c = GetChunkAt(t.x, t.y);
				if (c == null) return false;
				return !c.Objects.TryGetValue(t, out int oid) || oid == 0;
			}

			if (HouseInteriorSpots.TryGetValue(buildingId, out var spots) && spots.Count > 0)
			{
				var free = spots.FirstOrDefault(s => !s.Occupied && !occupiedHomeTiles.Contains(s.Tile) && IsTileFreeOfObjects(s.Tile));
				if (free != null)
				{
					tile = free.Tile;
				}
				else
				{
					// Aucune case libre : repli sur une case NON utilisée comme lit par un PNJ,
					// même si marquée "Occupied" pour une autre raison (siège, etc.). Si toutes
					// les cases servent de lit, on renonce plutôt que de rendre la maison
					// impraticable.
					var fallback = spots.FirstOrDefault(s => !occupiedHomeTiles.Contains(s.Tile) && IsTileFreeOfObjects(s.Tile));
					if (fallback != null) tile = fallback.Tile;
				}
			}

			if (!tile.HasValue)
			{
				// Repli : première case au sol praticable de l'emprise, en évitant la porte et
				// toute case de lit occupée par un PNJ.
				int bx = (int)house.Bounds.X;
				int by = (int)house.Bounds.Y;
				int bw = (int)house.Bounds.Width;
				int bh = (int)house.Bounds.Height;
				for (int tx = bx; tx < bx + bw && !tile.HasValue; tx++)
				{
					for (int ty = by; ty < by + bh; ty++)
					{
						if (tx == house.DoorX && ty == house.DoorY) continue;
						if (occupiedHomeTiles.Contains((tx, ty))) continue;
						var chunk = GetChunkAt(tx, ty);
						if (chunk == null) continue;
						if (chunk.Objects.TryGetValue((tx, ty), out int objectId) && objectId != 0) continue;
						var ground = WorldTileRegistry.GetTile(GetGroundTileIdAt(tx, ty));
						if (ground?.Walkable != true) continue;
						if (chunk.GetContainerAt(tx, ty) != null) continue;
						tile = (tx, ty);
						break;
					}
				}
			}

			//  Vraiment aucune case disponible sans détruire une case de lit : on renonce.
			// Mieux vaut ne pas créer de coffre par défaut que de rendre la maison impraticable
			// (un PNJ figé pour toujours vaut moins bien qu'un PNJ qui garde son surplus
			// d'inventaire en attendant qu'une case se libère).
			if (!tile.HasValue) return null;

			var targetChunk = GetChunkAt(tile.Value.x, tile.Value.y);
			if (targetChunk == null) return null;

			var chestData = WorldTileRegistry.GetTile(HOUSE_CHEST_OBJECT_ID);
			int chestSlots = chestData?.ContainerSlots ?? 24;
			int chestColumns = chestData?.ContainerColumns > 0 ? chestData.ContainerColumns : 5;
			targetChunk.Containers[(tile.Value.x, tile.Value.y)] = new ContainerInventoryData(chestSlots, chestColumns);
			targetChunk.Objects[(tile.Value.x, tile.Value.y)] = HOUSE_CHEST_OBJECT_ID;

			var pos = new Vector2(tile.Value.x * Program.TileSize + Program.TileSize / 2f,
								   tile.Value.y * Program.TileSize + Program.TileSize / 2f);
			house.StorageChestPos = pos;
			return pos;
		}

		//  Même ID que le coffre au trésor souterrain (objet "coffre" générique déjà
		// connu du renderer/registre de tuiles) ; à remplacer par un ID dédié si un
		// visuel spécifique "coffre de maison" existe.
		private const int HOUSE_CHEST_OBJECT_ID = 42;

		private static ContainerInventoryData EnsureHouseChestContainer(ChunkData chunk, int tileX, int tileY, WorldTileData tileData)
		{
			int requiredSlots = tileData.ContainerSlots ?? 20;
			int columns = tileData.ContainerColumns > 0 ? tileData.ContainerColumns : 8;
			var container = chunk.GetContainerAt(tileX, tileY);
			if (container == null)
			{
				container = new ContainerInventoryData(requiredSlots, columns);
				chunk.SetContainerAt(tileX, tileY, container);
			}
			else
			{
				while (container.Slots.Count < requiredSlots)
					container.Slots.Add(new InventorySlot());
				container.Columns = columns;
				container.Rows = (int)Math.Ceiling((double)container.Slots.Count / columns);
			}

			return container;
		}

		/// <summary>
		/// Renvoie une position PRATICABLE, adjacente au coffre commun de la maison, à utiliser
		/// comme AiTarget pour le pathfinding des PNJ (GoToChestDeposit / GoToChestWithdraw).
		/// TOUS les objets "conteneur" (coffre, étagère, tonneau, caisse...) ont walkable=false
		/// dans worldobjects.json (comme n'importe quel meuble) : un A* qui exige que la case
		/// d'arrivée soit praticable ne trouvera donc JAMAIS de chemin si on vise directement le
		/// centre du coffre (renvoyé par GetOrFindHouseChest). Résultat observé : le PNJ passe en
		/// GoToChestDeposit/Withdraw, _currentPath reste vide pour toujours (aucun repli pour ces
		/// états, contrairement à GoHome qui a un repli vers la porte), et MoveTowardTarget se
		/// contente de le figer sur place sans jamais le rapprocher du coffre — le dépôt du soir
		/// ne se produit donc jamais via ce chemin. On cible ici la première case voisine libre et
		/// praticable autour du coffre, exactement comme un joueur qui interagit avec un meuble
		/// depuis la case d'à côté sans jamais marcher dessus.
		/// </summary>
		public static Vector2? GetHouseChestApproachPosition(int buildingId, Entity? requester = null)
		{
			var chestPos = GetOrFindHouseChest(buildingId);
			if (!chestPos.HasValue)
				return null;

			int cx = (int)(chestPos.Value.X / Program.TileSize);
			int cy = (int)(chestPos.Value.Y / Program.TileSize);

			//  IMPORTANT : la recherche ci-dessous ne doit JAMAIS pouvoir désigner une case
			// hors de la maison. Avant ce correctif, l'anneau de recherche s'élargissait
			// jusqu'à 8 tuiles sans la moindre vérification des limites (house.Bounds) : si
			// le coffre est coincé contre un mur ou entouré de meubles côté intérieur (donc
			// aucune case libre proche à l'intérieur), la recherche continuait tout
			// naturellement À TRAVERS le mur et finissait par renvoyer une case praticable
			// de la RUE. Le PNJ recevait alors une cible fixe, hors de la maison, pour
			// laquelle aucun chemin A* n'existe (le mur bloque le passage direct) : il
			// restait planté sur place indéfiniment, avec la ligne rouge de debug pointant
			// toujours vers ce même point extérieur.
			//
			// On privilégie donc d'abord les cases intérieures DÉJÀ validées de cette maison
			// (HouseInteriorSpots, la même liste que celle utilisée pour les lits/sièges,
			// garantie à l'intérieur et pathfindable), en prenant la plus proche du coffre
			// et libre de collision. Seulement si cette liste est vide/absente, on retombe
			// sur la recherche par anneau, mais strictement bornée à house.Bounds.
			if (HouseInteriorSpots.TryGetValue(buildingId, out var interiorSpots) && interiorSpots.Count > 0)
			{
				var best = interiorSpots
					.Where(s => !s.Occupied)
					.Where(s => s.Tile.x != cx || s.Tile.y != cy)
					.Where(s =>
					{
						var chunk = GetChunkAt(s.Tile.x, s.Tile.y);
						if (chunk == null) return false;
						if (chunk.Objects.TryGetValue(s.Tile, out int objectId) && objectId != 0) return false;
						var ground = WorldTileRegistry.GetTile(GetGroundTileIdAt(s.Tile.x, s.Tile.y));
						return ground?.Walkable == true;
					})
					.Where(s => !Program.entities.Any(e => !ReferenceEquals(e, requester) && e.IsAlive
							&& Vector2.DistanceSquared(e.WorldPos, s.WorldPos) < 35f * 35f))
					.OrderBy(s => Vector2.DistanceSquared(s.WorldPos, chestPos.Value))
					.FirstOrDefault();

				if (best != null)
					return best.WorldPos;
			}

			if (!Houses.TryGetValue(buildingId, out var boundsHouse))
				return null;
			int minTx = (int)boundsHouse.Bounds.X;
			int minTy = (int)boundsHouse.Bounds.Y;
			int maxTx = minTx + (int)boundsHouse.Bounds.Width - 1;
			int maxTy = minTy + (int)boundsHouse.Bounds.Height - 1;

			// Cherche d'abord les cases proches, puis élargit progressivement la recherche,
			// SANS jamais sortir des limites de la maison (voir commentaire ci-dessus).
			for (int radius = 1; radius <= 8; radius++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					for (int dy = -radius; dy <= radius; dy++)
					{
						if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;

						int tx = cx + dx;
						int ty = cy + dy;

						//  Ne jamais désigner une case hors des limites de la maison : c'est
						// précisément ce qui produisait une cible fixe dans la rue.
						if (tx < minTx || tx > maxTx || ty < minTy || ty > maxTy)
							continue;

						var chunk = GetChunkAt(tx, ty);
						if (chunk == null) continue;

						if (chunk.Objects.TryGetValue((tx, ty), out int objectId) && objectId != 0)
						{
							var objTileData = WorldTileRegistry.GetTile(objectId);
							if (objTileData == null || !objTileData.Walkable)
								continue;
						}

						int groundId = GetGroundTileIdAt(tx, ty);
						var groundTileData = WorldTileRegistry.GetTile(groundId);
						if (groundTileData == null || !groundTileData.Walkable)
							continue;

						Vector2 approachPos = new Vector2(
							tx * Program.TileSize + Program.TileSize / 2f,
							ty * Program.TileSize + Program.TileSize / 2f - Program.FeetOffsetY);
						// Le coffre possède une vraie hitbox : une case seulement marquée
						// "walkable" peut malgré tout être trop proche pour la hitbox du PNJ.
						if (Program.entities.Any(e => !ReferenceEquals(e, requester) && e.IsAlive
								&& Vector2.DistanceSquared(e.WorldPos, approachPos) < 35f * 35f))
							continue;

						return approachPos;
					}
				}
			}

			return null;
		}

		/// <summary>
		/// Indique si le coffre commun de la maison contient au moins un item de type Food.
		/// </summary>
		public static bool HouseChestHasFood(int buildingId)
		{
			var chestPos = GetOrFindHouseChest(buildingId);
			if (!chestPos.HasValue) return false;

			int tx = (int)(chestPos.Value.X / Program.TileSize);
			int ty = (int)(chestPos.Value.Y / Program.TileSize);
			var container = GetChunkAt(tx, ty)?.GetContainerAt(tx, ty);
			if (container == null) return false;

			return container.Slots.Any(s => !s.IsEmpty && s.Item != null
				&& GameData.ItemDatabase.TryGetValue(GameData.GetItemId(s.Item.Name), out var d) && d.Type == ItemType.Food);
		}

		/// <summary>
		/// Retire une unité de nourriture du coffre commun de la maison et remet la faim du PNJ
		/// à zéro. Ne fait rien si le coffre n'existe pas ou ne contient aucune nourriture (le
		/// PNJ retentera sa chance plus tard).
		/// </summary>
		public static void TryWithdrawFoodFromChest(int buildingId, Entity npc)
		{
			var chestPos = GetOrFindHouseChest(buildingId);
			if (!chestPos.HasValue) return;

			int tx = (int)(chestPos.Value.X / Program.TileSize);
			int ty = (int)(chestPos.Value.Y / Program.TileSize);
			var container = GetChunkAt(tx, ty)?.GetContainerAt(tx, ty);
			if (container == null) return;

			var slot = container.Slots.FirstOrDefault(s => !s.IsEmpty && s.Item != null
				&& GameData.ItemDatabase.TryGetValue(GameData.GetItemId(s.Item.Name), out var d) && d.Type == ItemType.Food);
			if (slot == null) return;

			npc.Hunger = 0f;
			slot.Count -= 1;
			if (slot.Count <= 0) { slot.Item = null; slot.Count = 0; }
		}

		/// <summary>
		/// Dépose dans le coffre commun de la maison tout l'inventaire du PNJ. Ne fait rien si la
		/// maison n'a pas de coffre. Si le coffre est plein, le surplus non déposé reste
		/// simplement dans l'inventaire du PNJ (pas de perte d'item).
		/// </summary>
		/// <summary>
		/// Indique si l'inventaire du PNJ contient au moins un item à déposer. Sert à
		/// éviter d'envoyer le PNJ marcher jusqu'au coffre pour rien.
		/// </summary>
		public static bool HasAnyDepositableItem(Entity npc)
		{
			foreach (var slot in npc.Inventory.Slots)
			{
				if (slot.IsEmpty || slot.Item == null) continue;
				if (slot.Count > 0) return true;
			}
			return false;
		}

		//  Renvoie le nombre TOTAL d'unités effectivement déposées dans le coffre (toutes piles
		// confondues). Le PNJ (voir GetGoalArrivalConfig/GoToChestDeposit) s'en sert pour
		// détecter le cas "coffre plein, rien n'a pu être déposé" et cesser de retenter
		// indéfiniment - avant, cette méthode ne renvoyait rien et l'appelant ne pouvait pas
		// distinguer "coffre plein" de "un dépôt partiel a bien eu lieu", d'où une boucle de
		// Retry sans fin dès que le coffre était saturé.
		public static int DepositExcessItemsToChest(int buildingId, Entity npc, bool logDetails = false)
		{
			var chestPos = GetOrFindHouseChest(buildingId);
			if (!chestPos.HasValue)
			{
				if (logDetails)
					Console.WriteLine($"[NPC-CHEST] NO_CHEST npc={npc.NetId} home={buildingId}");
				return 0;
			}

			int tx = (int)(chestPos.Value.X / Program.TileSize);
			int ty = (int)(chestPos.Value.Y / Program.TileSize);
			if (logDetails)
				Console.WriteLine($"[NPC-CHEST] RESOLVE npc={npc.NetId} home={buildingId} chestPos={chestPos.Value} tile={tx}_{ty}");

			var chunk = GetChunkAt(tx, ty);
			var container = chunk?.GetContainerAt(tx, ty);
			if (chunk != null && chunk.Objects.TryGetValue((tx, ty), out int objectId))
			{
				var tileData = WorldTileRegistry.GetTile(objectId);
				if (tileData?.IsContainer == true && IsRealChestTile(objectId, tileData))
					container = EnsureHouseChestContainer(chunk, tx, ty, tileData);
			}
			if (container == null && chunk != null)
			{
				// GetOrFindHouseChest owns chest creation and already verifies that the
				// destination tile is free. Never repair a missing container by replacing
				// whatever object currently occupies this cached position.
				if (Houses.TryGetValue(buildingId, out var house))
					house.StorageChestPos = null;
			}
			if (container == null)
			{
				if (logDetails)
					Console.WriteLine($"[NPC-CHEST] NO_CONTAINER npc={npc.NetId} home={buildingId} tile={tx}_{ty} chunkFound={chunk != null}");
				return 0;
			}

			if (logDetails)
				Console.WriteLine($"[NPC-CHEST] CONTAINER npc={npc.NetId} slots={container.Slots.Count} occupied={container.Slots.Count(slot => !slot.IsEmpty && slot.Item != null)}");

			int totalDeposited = 0;
			foreach (var slot in npc.Inventory.Slots)
			{
				if (slot.IsEmpty || slot.Item == null) continue;

				int qtyToDeposit = slot.Count;
				if (qtyToDeposit <= 0) continue;

				int deposited = StackItemIntoContainer(container, slot.Item, qtyToDeposit);
				slot.Count -= deposited;
				if (slot.Count <= 0) { slot.Item = null; slot.Count = 0; }
				totalDeposited += deposited;
				if (logDetails)
					Console.WriteLine($"[NPC-CHEST] TRANSFER npc={npc.NetId} requested={qtyToDeposit} deposited={deposited} remaining={slot.Count}");
			}

			return totalDeposited;
		}

		public static Item? TakeItemFromHouseChest(int buildingId, string itemName)
		{
			var chestPos = GetOrFindHouseChest(buildingId);
			if (!chestPos.HasValue) return null;

			int tx = (int)(chestPos.Value.X / Program.TileSize);
			int ty = (int)(chestPos.Value.Y / Program.TileSize);
			var container = GetChunkAt(tx, ty)?.GetContainerAt(tx, ty);
			var slot = container?.Slots.FirstOrDefault(candidate =>
				!candidate.IsEmpty && candidate.Item != null && candidate.Item.Name == itemName);
			if (slot?.Item == null) return null;

			var item = slot.Item;
			if (slot.Count > 1)
			{
				slot.Count--;
				item = new Item(item.Name, 1, item.BaseColor, item.Icon);
				item.Metadata = slot.Item.Metadata;
				item.Meta = new Dictionary<string, string>(slot.Item.Meta);
			}
			else
				slot.Clear();

			return item;
		}

		/// <summary>
		/// Empile "quantity" unités de "item" dans le premier emplacement libre/compatible du
		/// conteneur donné. Renvoie la quantité effectivement déposée (peut être inférieure à
		/// "quantity" si le conteneur n'a plus assez de place).
		/// </summary>
		/// <summary>
		/// Indique si ce conteneur (inventaire de PNJ, coffre...) peut encore accueillir l'item
		/// nommé itemName : un emplacement vide, ou une pile existante du même item (les piles
		/// n'ont pas de plafond dans ce jeu, donc toute pile existante convient).
		/// </summary>
		public static bool ContainerHasSpaceForItem(ContainerInventoryData container, string itemName)
		{
			return container.Slots.Any(s => s.IsEmpty || (s.Item != null && s.Item.Name == itemName));
		}

		internal static int StackItemIntoContainer(ContainerInventoryData container, Item item, int quantity)
		{
			int remaining = quantity;

			// D'abord compléter les piles existantes du même item.
			foreach (var slot in container.Slots)
			{
				if (remaining <= 0) break;
				if (slot.IsEmpty || slot.Item == null || slot.Item.Name != item.Name) continue;
				slot.Count += remaining;
				remaining = 0;
			}

			// Puis utiliser des emplacements vides pour le reste.
			if (remaining > 0)
			{
				foreach (var slot in container.Slots)
				{
					if (remaining <= 0) break;
					if (!slot.IsEmpty) continue;
					slot.Item = new Item(item.Name, remaining, item.BaseColor, item.Icon);
					slot.Count = remaining;
					remaining = 0;
				}
			}

			return quantity - remaining;
		}

		/// <summary>
		/// Cherche le PNJ marchand (métier ou marqué IsTrader) le plus proche, dans la limite de
		/// maxDistance. Utilisé de jour uniquement par le système de faim des villageois.
		/// </summary>
		public static Entity? FindNearestFoodMerchant(Vector2 worldPos, float maxDistance = 2000f)
		{
			Entity? closest = null;
			float bestDistSq = maxDistance * maxDistance;

			foreach (var entity in Program.entities)
			{
				if (!entity.IsAlive) continue;
				if (entity.Species != "human") continue;
				if (!entity.IsTrader && !entity.HasProfession) continue;
				if (entity.IsTamed && !entity.IsGuildMember) continue;

				float distSq = Vector2.DistanceSquared(worldPos, entity.WorldPos);
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					closest = entity;
				}
			}
			return closest;
		}

		public static int RegisterHouse(int doorX, int doorY, Vector2 interiorPos)
		{
			int buildingId = Math.Abs(doorX * 73856093 ^ doorY * 19349663) % 10000 + 1000;
			
			if (!Houses.ContainsKey(buildingId))
			{
				var house = new HouseData
				{
					BuildingId = buildingId,
					DoorX = doorX,
					DoorY = doorY,
					//  CORRECTIF : DoorWorldPos doit être exprimé dans le même référentiel
					// "pieds" que WorldPos (voir Pathfinder.FeetTileYToWorldY / WorldYToFeetTileY).
					// L'ancien calcul (doorY*TileSize + TileSize/2, sans compensation) faisait que
					// le pathfinder, qui reconvertit toute position monde en case via
					// WorldYToFeetTileY (+FeetOffsetY), retombait sur la case doorY+1 au lieu de
					// doorY : la cible réelle du chemin n'était donc jamais la tuile de porte
					// (seule tuile "trouée" dans le mur, voir IsDoorTile) mais la case juste après,
					// un mur plein. Le PNJ n'atteignait jamais cette cible et recalculait sans fin
					// le même chemin invalide, d'où l'aller-retour infini pile devant la porte.
					// On soustrait FeetOffsetY ici pour compenser exactement l'ajout fait par
					// WorldYToFeetTileY au moment où ce point est réutilisé comme cible de FindPath.
					DoorWorldPos = new Vector2(doorX * Program.TileSize + Program.TileSize/2f,
											   doorY * Program.TileSize + Program.TileSize/2f - Program.FeetOffsetY),
					InteriorPosition = interiorPos,
					IsOccupied = false
				};
				Houses[buildingId] = house;
				RegisterDoorTile(doorX, doorY);
			}
			
			return buildingId;
		}

		public static bool TryPlaceVillageChair(int buildingId)
		{
			if (!Houses.TryGetValue(buildingId, out var house)
				|| !HouseInteriorSpots.TryGetValue(buildingId, out var spots))
				return false;

			var chairData = WorldTileRegistry.GetTile(95);
			if (chairData == null || chairData.IsSittable != true)
				return false;

			var candidates = spots
				.Where(spot => !spot.Occupied && GetObjectIdAt(spot.Tile.x, spot.Tile.y) == 0)
				.Where(spot => Math.Abs(spot.Tile.x - house.DoorX) + Math.Abs(spot.Tile.y - house.DoorY) > 2)
				.ToList();

			if (candidates.Count == 0)
				candidates = spots
					.Where(spot => !spot.Occupied && GetObjectIdAt(spot.Tile.x, spot.Tile.y) == 0)
					.ToList();

			if (candidates.Count == 0)
				return false;

			var seatSpot = candidates
				.OrderBy(_ => Random.Shared.Next())
				.First();

			var chunk = GetChunkAt(seatSpot.Tile.x, seatSpot.Tile.y);
			if (chunk == null || chunk.Objects.ContainsKey(seatSpot.Tile))
				return false;

			chunk.Objects[seatSpot.Tile] = 95;
			seatSpot.Occupied = true;
			return true;
		}

		public static bool TryPlaceVillageInfirmaryToilet(int buildingId)
		{
			if (!Houses.TryGetValue(buildingId, out var house)
				|| !HouseInteriorSpots.TryGetValue(buildingId, out var spots))
				return false;

			if (HouseHasToilet(house))
				return true;

			var toiletData = WorldTileRegistry.GetTile(85);
			if (toiletData == null || toiletData.IsSittable != true)
				return false;

			var freeSpots = spots.Where(spot => !spot.Occupied
				&& GetObjectIdAt(spot.Tile.x, spot.Tile.y) == 0
				&& WorldTileRegistry.GetTile(GetGroundTileIdAt(spot.Tile.x, spot.Tile.y))?.Walkable == true)
				.ToList();
			var availablePlacements = new List<(HouseInteriorSpot Toilet, HouseInteriorSpot Seat)>();
			foreach (var toiletCandidate in freeSpots)
			{
				foreach (var seatCandidate in freeSpots)
				{
					if (ReferenceEquals(toiletCandidate, seatCandidate)) continue;
					int tileDistance = Math.Abs(toiletCandidate.Tile.x - seatCandidate.Tile.x)
						+ Math.Abs(toiletCandidate.Tile.y - seatCandidate.Tile.y);
					if (tileDistance == 1)
						availablePlacements.Add((toiletCandidate, seatCandidate));
				}
			}
			if (availablePlacements.Count == 0)
				return false;

			var selectedPlacement = availablePlacements[Random.Shared.Next(availablePlacements.Count)];
			var toiletSpot = selectedPlacement.Toilet;
			var chunk = GetChunkAt(toiletSpot.Tile.x, toiletSpot.Tile.y);
			if (chunk == null || chunk.Objects.ContainsKey(toiletSpot.Tile))
				return false;

			chunk.Objects[toiletSpot.Tile] = 85;
			toiletSpot.Occupied = true;
			selectedPlacement.Seat.Occupied = true;
			selectedPlacement.Seat.IsInfirmarySeat = true;
			return true;
		}

		private static bool HouseHasToilet(HouseData house)
		{
			var tilesToCheck = new HashSet<(int x, int y)>(house.RoofTiles);
			if (HouseInteriorSpots.TryGetValue(house.BuildingId, out var spots))
				foreach (var spot in spots) tilesToCheck.Add(spot.Tile);
			if (house.Bounds.Width > 0 && house.Bounds.Height > 0)
			{
				for (int tileX = (int)house.Bounds.X; tileX < house.Bounds.X + house.Bounds.Width; tileX++)
					for (int tileY = (int)house.Bounds.Y; tileY < house.Bounds.Y + house.Bounds.Height; tileY++)
						tilesToCheck.Add((tileX, tileY));
			}

			return tilesToCheck.Any(tile => GetObjectIdAt(tile.x, tile.y) == 85);
		}

		public static bool TryReserveNearestInfirmary(Entity patient, out int buildingId, out Vector2 approachPos)
		{
			buildingId = -1;
			approachPos = Vector2.Zero;
			if (patient.HomeBuildingId >= 0 && !Houses.ContainsKey(patient.HomeBuildingId))
				RefreshAllHouses();

			int nearestBuildingId = -1;
			Vector2 nearestApproachPos = Vector2.Zero;
			float nearestDistanceSq = 6000f * 6000f;
			HashSet<string> destroyed = Program.GetDestroyedObjects();

			foreach (var entry in Houses)
			{
				var house = entry.Value;
				if (house.InfirmaryPatientNetId.HasValue && house.InfirmaryPatientNetId.Value != patient.NetId)
				{
					bool patientStillUsingRoom = Program.GetEntities().Any(entity =>
						entity.NetId == house.InfirmaryPatientNetId.Value && entity.IsAlive
						&& (entity.AiState == NpcAiState.GoToInfirmary || entity.AiState == NpcAiState.HealingAtInfirmary));
					if (patientStillUsingRoom)
						continue;

					house.InfirmaryPatientNetId = null;
				}

				if (!TryFindInfirmaryToilet(house, out _, out var candidateApproach))
					continue;

				float distanceSq = Vector2.DistanceSquared(patient.WorldPos, candidateApproach);
				if (distanceSq >= nearestDistanceSq)
					continue;
				if (distanceSq > 55f * 55f
					&& Pathfinder.FindPath(patient.WorldPos, candidateApproach, destroyed, 12, patient.Species).Count == 0)
					continue;

				nearestDistanceSq = distanceSq;
				nearestBuildingId = entry.Key;
				nearestApproachPos = candidateApproach;
			}

			if (nearestBuildingId < 0 || !Houses.TryGetValue(nearestBuildingId, out var selectedHouse))
				return false;

			selectedHouse.InfirmaryPatientNetId = patient.NetId;
			buildingId = nearestBuildingId;
			approachPos = nearestApproachPos;
			return true;
		}

		public static void ReleaseInfirmary(int buildingId, Guid patientNetId)
		{
			if (Houses.TryGetValue(buildingId, out var house)
				&& house.InfirmaryPatientNetId == patientNetId)
				house.InfirmaryPatientNetId = null;
		}

		private static bool TryFindInfirmaryToilet(HouseData house, out (int x, int y) toiletTile, out Vector2 approachPos)
		{
			toiletTile = default;
			approachPos = Vector2.Zero;
			var candidateTiles = new HashSet<(int x, int y)>(house.RoofTiles);
			if (HouseInteriorSpots.TryGetValue(house.BuildingId, out var spots))
				foreach (var spot in spots) candidateTiles.Add(spot.Tile);
			if (house.Bounds.Width > 0 && house.Bounds.Height > 0)
			{
				for (int tileX = (int)house.Bounds.X; tileX < house.Bounds.X + house.Bounds.Width; tileX++)
					for (int tileY = (int)house.Bounds.Y; tileY < house.Bounds.Y + house.Bounds.Height; tileY++)
						candidateTiles.Add((tileX, tileY));
			}

			foreach (var tile in candidateTiles)
			{
				if (GetObjectIdAt(tile.x, tile.y) != 85)
					continue;

				IEnumerable<(int x, int y)> approachTiles = HouseInteriorSpots.TryGetValue(house.BuildingId, out var interiorSpots)
					? interiorSpots.OrderBy(spot => spot.IsInfirmarySeat ? 0 : spot.Occupied ? 1 : 2).Select(spot => spot.Tile)
					: candidateTiles;
				foreach (var approachTile in approachTiles)
				{
					int tileDistance = Math.Abs(tile.x - approachTile.x) + Math.Abs(tile.y - approachTile.y);
					if (tileDistance != 1)
						continue;
					int approachX = approachTile.x;
					int approachY = approachTile.y;
					if (GetObjectIdAt(approachX, approachY) != 0)
						continue;
					if (WorldTileRegistry.GetTile(GetGroundTileIdAt(approachX, approachY))?.Walkable != true)
						continue;

					toiletTile = tile;
					approachPos = new Vector2(approachX * Program.TileSize + Program.TileSize / 2f,
						approachY * Program.TileSize + Program.TileSize / 2f - Program.FeetOffsetY);
					return true;
				}
			}

			return false;
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  INTÉRIEUR DES MAISONS
		// ─────────────────────────────────────────────────────────────────────────
		// Avant : chaque maison n'avait qu'UN seul point "intérieur" (une case juste
		// derrière la porte), partagé par tous ses occupants, et l'attribution ne
		// réservait que "la maison" (IsOccupied bool). Résultat : les PNJ se
		// contentaient d'atteindre le pas de la porte (le point était trop proche du
		// seuil et le rayon d'arrivée du déplacement le dépassait déjà), et deux
		// villageois de la même maison se seraient superposés au même endroit.
		//
		// Désormais chaque maison expose une LISTE de cases de sol praticables situées
		// réellement à l'intérieur (calculées une fois, à partir des tuiles sous le
		// toit), et chaque villageois se voit attribuer SA PROPRE case, comme pour les
		// places de banc (voir BenchSeat plus haut).
		public class HouseInteriorSpot
		{
			public int BuildingId;
			public (int x, int y) Tile;
			public Vector2 WorldPos;
			public bool Occupied;
			public bool IsInfirmarySeat;
		}

		public static Dictionary<int, List<HouseInteriorSpot>> HouseInteriorSpots = new();

		/// <summary>
		/// Construit (une seule fois par maison) la liste des cases de sol praticables à
		/// l'intérieur d'une maison à partir d'un ensemble de tuiles candidates (l'emprise
		/// du bâtiment, ou ses tuiles de toit). Exclut les murs, la porte elle-même, et
		/// toute case déjà occupée par un objet/meuble.
		/// </summary>
		public static void RegisterHouseInteriorSpots(int buildingId, IEnumerable<(int x, int y)> candidateTiles, int doorX, int doorY)
		{
			if (HouseInteriorSpots.ContainsKey(buildingId))
				return; // déjà calculé : on préserve l'occupation actuelle des places

			int ts = Program.TileSize;
			var spots = new List<HouseInteriorSpot>();

			// Preferer les tuiles réellement "intérieures" déterminées par le flood-fill
			// (TryGetEnclosedRoofTiles) si possible, plutôt que d'utiliser aveuglément
			// l'emprise de la structure générée.
			List<(int x, int y)> tilesToScan;
			int minX, maxX, minY, maxY, detectedDoorX, detectedDoorY;
			bool detectedDoorFound;
			if (TryFindHouseBoundsForTile(doorX, doorY, out minX, out maxX, out minY, out maxY, out detectedDoorX, out detectedDoorY, out detectedDoorFound))
			{
				if (TryGetEnclosedRoofTiles(minX, maxX, minY, maxY, detectedDoorX, detectedDoorY, out var enclosed))
				{
					// ne garder que les tuiles non-mur (tuiles intérieures)
					tilesToScan = enclosed.Where(t => !IsWall(GetObjectIdAt(t.x, t.y))).ToList();
				}
				else
				{
					// Pas d'intérieur détecté via l'algorithme -> retomber sur les candidats fournis
					tilesToScan = candidateTiles.ToList();
				}
			}
			else
			{
				tilesToScan = candidateTiles.ToList();
			}

			foreach (var (x, y) in tilesToScan)
			{
				if (x == doorX && y == doorY) continue; // pas sur le seuil de la porte
				if (IsWall(GetObjectIdAt(x, y))) continue;
				if (GetObjectIdAt(x, y) != 0) continue; // meuble/objet bloquant la case

				int groundId = GetGroundTileIdAt(x, y);
				var groundTile = WorldTileRegistry.GetTile(groundId);
				if (groundTile == null || !groundTile.Walkable) continue;

				spots.Add(new HouseInteriorSpot
				{
					BuildingId = buildingId,
					Tile = (x, y),
					WorldPos = new Vector2(x * ts + ts / 2f, y * ts + ts / 2f - Program.FeetOffsetY),
					Occupied = false
				});
			}

			// Sécurité : aucune case intérieure valide trouvée (maison meublée à ras
			// bord, trop petite, etc.) → on garde au moins un point de repli pour que
			// la maison reste habitable plutôt que de n'avoir aucun logement possible.
			if (spots.Count == 0 && Houses.TryGetValue(buildingId, out var fallbackHouse))
			{
				spots.Add(new HouseInteriorSpot
				{
					BuildingId = buildingId,
					Tile = (fallbackHouse.DoorX, fallbackHouse.DoorY),
					WorldPos = fallbackHouse.InteriorPosition,
					Occupied = false
				});
			}

			HouseInteriorSpots[buildingId] = spots;

			foreach (var toiletTile in tilesToScan.Where(tile => GetObjectIdAt(tile.x, tile.y) == 85))
			{
				var seatSpot = spots
					.Where(spot => Math.Abs(spot.Tile.x - toiletTile.x) + Math.Abs(spot.Tile.y - toiletTile.y) == 1)
					.OrderBy(spot => spot.Occupied)
					.FirstOrDefault();
				if (seatSpot == null) continue;

				seatSpot.Occupied = true;
				seatSpot.IsInfirmarySeat = true;
				break;
			}

			//  CORRECTIF : house.Bounds n'est JAMAIS renseigné par RegisterHouse (le chemin
			// d'enregistrement utilisé à la génération initiale du village) - seule la
			// reconstruction après rechargement de chunk (RebuildHousesFromRoofs) le fait. Or
			// GetHouseChestApproachPosition retombe sur une recherche en anneau bornée par
			// house.Bounds dès que les cases de HouseInteriorSpots sont toutes occupées/en
			// collision (typiquement : le joueur pose un coffre sur la seule case intérieure
			// d'une petite maison). Avec Bounds resté à (0,0,0,0) par défaut, maxTx/maxTy valent
			// alors minTx-1/minTy-1 : la condition de bornage rejette systématiquement TOUTE
			// case testée, et la recherche échoue à coup sûr, pour toujours. On calcule donc ici
			// l'emprise réelle à partir des cases scannées (+ la porte), pour que ce repli soit
			// enfin utilisable. Idempotent et sans risque si RebuildHousesFromRoofs l'a déjà fixé
			// par ailleurs (on ne fait que l'élargir si besoin, jamais le rétrécir).
			if (Houses.TryGetValue(buildingId, out var houseToUpdateBounds))
			{
				int bMinX = doorX, bMaxX = doorX, bMinY = doorY, bMaxY = doorY;
				foreach (var spot in spots)
				{
					bMinX = Math.Min(bMinX, spot.Tile.x);
					bMaxX = Math.Max(bMaxX, spot.Tile.x);
					bMinY = Math.Min(bMinY, spot.Tile.y);
					bMaxY = Math.Max(bMaxY, spot.Tile.y);
				}

				var existing = houseToUpdateBounds.Bounds;
				bool existingIsEmpty = existing.Width <= 0 || existing.Height <= 0;
				if (existingIsEmpty)
				{
					houseToUpdateBounds.Bounds = new Rectangle(bMinX, bMinY, bMaxX - bMinX + 1, bMaxY - bMinY + 1);
				}
				else
				{
					// Élargit l'emprise existante plutôt que de l'écraser, au cas où elle
					// provenait déjà d'une reconstruction par toit plus précise.
					float newMinX = Math.Min(existing.X, bMinX);
					float newMinY = Math.Min(existing.Y, bMinY);
					float newMaxX = Math.Max(existing.X + existing.Width - 1, bMaxX);
					float newMaxY = Math.Max(existing.Y + existing.Height - 1, bMaxY);
					houseToUpdateBounds.Bounds = new Rectangle(newMinX, newMinY, newMaxX - newMinX + 1, newMaxY - newMinY + 1);
				}
			}
		}

		public static int GetHouseResidentCapacity(int buildingId)
		{
			if (!Houses.TryGetValue(buildingId, out var house))
				return 0;

			int freeInteriorTiles = HouseInteriorSpots.TryGetValue(buildingId, out var spots)
				? spots.Count(s => !s.Occupied && GetObjectIdAt(s.Tile.x, s.Tile.y) == 0)
				: 0;

			if (freeInteriorTiles < 5)
				return 0;

			int chairCount = 0;
			int minX = (int)house.Bounds.X;
			int minY = (int)house.Bounds.Y;
			int maxX = minX + (int)house.Bounds.Width;
			int maxY = minY + (int)house.Bounds.Height;

			for (int x = minX; x < maxX; x++)
			{
				for (int y = minY; y < maxY; y++)
				{
					int objectId = GetObjectIdAt(x, y);
					if (objectId == 0) continue;
					var tileData = WorldTileRegistry.GetTile(objectId);
					if (tileData?.IsSittable == true)
						chairCount++;
				}
			}

			foreach (var bench in Benches.Values)
			{
				if (bench.AnchorTile.x >= minX && bench.AnchorTile.x < maxX && bench.AnchorTile.y >= minY && bench.AnchorTile.y < maxY)
					chairCount++;
			}

			if (HouseInteriorSpots.TryGetValue(buildingId, out var interiorSpots))
			{
				chairCount += interiorSpots.Count(spot => spot.IsInfirmarySeat);
			}

			if (chairCount <= 0)
				return 0;

			return Math.Min(freeInteriorTiles / 5, chairCount);
		}

		public static int GetHouseResidentCount(int buildingId)
		{
			return Program.GetEntities().Count(e => e.IsAlive && e.IsVillager && e.HomeBuildingId == buildingId);
		}

		public static bool CanHouseAcceptMoreResidents(int buildingId)
		{
			if (!Houses.ContainsKey(buildingId))
				return true;
			return GetHouseResidentCount(buildingId) < GetHouseResidentCapacity(buildingId);
		}

		/// <summary>
		/// Trouve la maison la plus proche possédant encore une case intérieure libre,
		/// réserve CETTE case précise (pas juste "la maison"), et retourne sa position.
		/// Remplace FindNearestFreeHouse : plusieurs villageois peuvent désormais
		/// cohabiter dans une même maison tant qu'il reste une case libre à l'intérieur.
		/// </summary>
		public static (int buildingId, Vector2 spotPos)? FindNearestFreeHouseSpot(Vector2 worldPos, float maxDistance = 2400f)
		{
			if (Houses.Count == 0)
				RefreshAllHouses();

			var candidates = new List<(int buildingId, int residentCount, float distSq)>();
			foreach (var house in Houses.Values)
			{
				if (!CanHouseAcceptMoreResidents(house.BuildingId))
					continue;

				if (!HouseInteriorSpots.TryGetValue(house.BuildingId, out var spots) || !spots.Any(s => !s.Occupied))
					continue;

				float distSq = Vector2.DistanceSquared(worldPos, house.DoorWorldPos);
				if (distSq > maxDistance * maxDistance)
					continue;

				int residentCount = GetHouseResidentCount(house.BuildingId);
				candidates.Add((house.BuildingId, residentCount, distSq));
			}

			if (candidates.Count == 0)
				return null;

			var bestCandidate = candidates
				.OrderBy(c => c.residentCount)
				.ThenBy(c => c.distSq)
				.First();

			int bestBuildingId = bestCandidate.buildingId;

			//  Choisir la case libre la plus proche de la porte : le trajet "rentrer à la
			// maison" se fait désormais en ligne droite porte -> case (voir
			// Entity.SetGoHomeWaypoints, sans recherche A*, pour rester peu coûteux). Une
			// case trop profonde dans la maison risquerait de se retrouver séparée de la
			// porte par un meuble que la marche en ligne droite ne saurait pas contourner.
			Vector2 doorWorldPos = Houses.TryGetValue(bestBuildingId, out var bestHouse)
				? bestHouse.DoorWorldPos
				: worldPos;

			var chosen = HouseInteriorSpots[bestBuildingId]
				.Where(s => !s.Occupied)
				.OrderBy(s => Vector2.DistanceSquared(doorWorldPos, s.WorldPos))
				.First();

			chosen.Occupied = true;

			if (Houses.TryGetValue(bestBuildingId, out var houseToMark))
				houseToMark.IsOccupied = HouseInteriorSpots[bestBuildingId].All(s => s.Occupied);

			return (bestBuildingId, chosen.WorldPos);
		}

		/// <summary>
		/// Libère la case intérieure précédemment réservée (PNJ mort, déménagement, etc.)
		/// pour qu'elle redevienne disponible pour un autre villageois.
		/// </summary>
		public static void ReleaseHouseSpot(int buildingId, Vector2 spotPos)
		{
			if (!HouseInteriorSpots.TryGetValue(buildingId, out var spots))
				return;

			var spot = spots.FirstOrDefault(s => s.WorldPos == spotPos);
			if (spot != null)
			{
				spot.Occupied = false;
				if (Houses.TryGetValue(buildingId, out var house))
					house.IsOccupied = false;
			}
		}

		public static bool IsValidFurnitureSurface(int x, int y)
		{
			int groundId = GetGroundTileIdAt(x, y);
			var tileData = WorldTileRegistry.GetTile(groundId);
			if (tileData == null || !tileData.Walkable)
				return false;

			if (tileData.Id == 1 || tileData.Id == 2 || tileData.Id == 3 || tileData.Id == 4 || tileData.Id == 7 || tileData.Id == 20 || tileData.Id == 100 || tileData.Id == 3000)
				return false;

			string normalizedName = (tileData.Name ?? string.Empty).ToLowerInvariant();
			bool isInteriorFloorSurface = tileData.Id == 54 || tileData.Id == 55 || tileData.Id == 56 || tileData.Id == 57 || tileData.Id == 58 || tileData.Id == 59 || tileData.Id == 60 ||
				normalizedName.Contains("floor") || normalizedName.Contains("carpet") || normalizedName.Contains("tile") || normalizedName.Contains("parquet");

			return isInteriorFloorSurface;
		}

		// Met à jour la liste des maisons à partir des toits existants
		public static void RefreshAllHouses()
		{
			var knownHouses = Houses.ToDictionary(entry => entry.Key, entry => entry.Value);
			var knownInteriorSpots = HouseInteriorSpots.ToDictionary(
				entry => entry.Key,
				entry => entry.Value);

			// Sauvegarder les données des PNJ existants avant de reconstruire
			var existingNpcs = new Dictionary<int, List<(Vector2 homePos, bool isOccupied, Vector2 spotPos)>>();
			
			// Parcourir les entités existantes pour préserver les PNJ
			foreach (var entity in Program.GetEntities())
			{
				if (!entity.IsAlive) continue;
				if (!entity.IsVillager) continue;
				if (entity.HomeBuildingId <= 0) continue;
				
				// Sauvegarder les données du PNJ
				if (!existingNpcs.ContainsKey(entity.HomeBuildingId))
				{
					existingNpcs[entity.HomeBuildingId] = new List<(Vector2, bool, Vector2)>();
				}
				
				// Trouver la place occupée par ce PNJ
				Vector2 occupiedSpot = entity.HomePosition;
				if (HouseInteriorSpots.TryGetValue(entity.HomeBuildingId, out var spots))
				{
					foreach (var spot in spots)
					{
						if (spot.Occupied && Vector2.DistanceSquared(spot.WorldPos, entity.HomePosition) < (10f) * (10f))
						{
							occupiedSpot = spot.WorldPos;
							break;
						}
					}
				}
				
				existingNpcs[entity.HomeBuildingId].Add((entity.HomePosition, true, occupiedSpot));
			}
			
			// Vider les dictionnaires et réinitialiser les tuiles porte
			ClearDoorTiles();
			Houses.Clear();
			HouseInteriorSpots.Clear();
			
			// Reconstruire les maisons à partir des toits
			var chunksDict = GetChunksDict();
			var roofsByBuilding = new Dictionary<int, List<(int x, int y)>>();
			
			foreach (var chunk in chunksDict.Values)
			{
				foreach (var ((x, y), overlayId) in chunk.Overlays)
				{
					if (overlayId == 82) // Toit
					{
						int buildingId = GetBuildingIdAt(x, y);
						if (buildingId != 0)
						{
							if (!roofsByBuilding.ContainsKey(buildingId))
								roofsByBuilding[buildingId] = new List<(int, int)>();
							roofsByBuilding[buildingId].Add((x, y));
						}
					}
				}
			}
			
			// Pour chaque bâtiment, trouver une porte et un point intérieur
			foreach (var building in roofsByBuilding)
			{
				int buildingId = building.Key;
				var roofTiles = building.Value;
				
				int minX = roofTiles.Min(t => t.x);
				int maxX = roofTiles.Max(t => t.x);
				int minY = roofTiles.Min(t => t.y);
				int maxY = roofTiles.Max(t => t.y);

				if (!HasAnyWallOrDoorInBounds(minX, maxX, minY, maxY))
					continue;
				
				(int doorX, int doorY) = FindBuildingEntrance(minX, maxX, minY, maxY);
				
				if (doorX != -1)
				{
					Vector2 interiorPos = FindInteriorPosition(doorX, doorY, minX, maxX, minY, maxY);
					
					var house = new HouseData
					{
						BuildingId = buildingId,
						DoorX = doorX,
						DoorY = doorY,
						//  CORRECTIF : voir le commentaire équivalent dans RegisterHouse. Même
						// compensation FeetOffsetY nécessaire ici, sinon les maisons restaurées
						// après rechargement de chunk retombent dans le même bug (PNJ bloqués
						// devant leur porte, aller-retour infini).
						DoorWorldPos = new Vector2(doorX * Program.TileSize + Program.TileSize/2f,
												doorY * Program.TileSize + Program.TileSize/2f - Program.FeetOffsetY),
						InteriorPosition = interiorPos,
						IsOccupied = false,
						HasVillagerSpawned = false,
						RoofTiles = new HashSet<(int, int)>(roofTiles),
						Bounds = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
					};

					//  CORRECTIF : cette reconstruction crée une toute nouvelle HouseData à
					// chaque rechargement de chunk visible, ce qui effaçait silencieusement
					// StorageChestPos même quand le coffre existant était toujours bien là dans
					// le monde. GetOrFindHouseChest repartait alors de zéro, ne le retrouvait pas
					// forcément immédiatement (ex. chunk voisin pas encore chargé) et pouvait finir
					// par en faire créer un second par un PNJ. On reporte donc la valeur connue
					// depuis l'ancienne HouseData quand elle existe, elle sera de toute façon
					// revalidée (et oubliée si le coffre a réellement disparu) au prochain appel de
					// GetOrFindHouseChest.
					if (knownHouses.TryGetValue(buildingId, out var previousHouse))
					{
						house.StorageChestPos = previousHouse.StorageChestPos;
						house.InfirmaryPatientNetId = previousHouse.InfirmaryPatientNetId;
					}

					Houses[buildingId] = house;
					RegisterDoorTile(doorX, doorY);
					
					// Restaurer les places intérieures
					RegisterHouseInteriorSpots(buildingId, roofTiles, doorX, doorY);
					
					// Restaurer l'occupation des PNJ
					if (existingNpcs.TryGetValue(buildingId, out var npcList))
					{
						// Marquer la maison comme occupée
						house.IsOccupied = npcList.Count > 0;
						
						// Restaurer les places occupées
						if (HouseInteriorSpots.TryGetValue(buildingId, out var spots))
						{
							foreach (var spot in spots)
							{
								foreach (var (_, _, occupiedSpot) in npcList)
								{
									if (Vector2.DistanceSquared(spot.WorldPos, occupiedSpot) < (10f) * (10f))
									{
										spot.Occupied = true;
										break;
									}
								}
							}
							
							// Si toutes les places sont occupées, la maison est pleine
							house.IsOccupied = spots.All(s => s.Occupied);
						}
					}
				}
			}

			// Les chunks hors écran peuvent être déchargés après la reconstruction. Préserver
			// leurs maisons déjà connues permet aux PNJ de conserver leur logement et leur coffre
			// même si le toit n'est momentanément plus présent dans les chunks actifs.
			foreach (var entry in knownHouses)
			{
				if (Houses.ContainsKey(entry.Key)) continue;
				Houses[entry.Key] = entry.Value;
				if (knownInteriorSpots.TryGetValue(entry.Key, out var spots))
					HouseInteriorSpots[entry.Key] = spots;
			}

			var occupiedVillagerHouses = existingNpcs
				.Where(entry => entry.Value.Count > 0 && Houses.ContainsKey(entry.Key))
				.ToList();
			var houseGroupsByVillage = new Dictionary<(int x, int y), List<int>>();
			foreach (var entry in occupiedVillagerHouses)
			{
				var villageCenter = FindNearestVillageCenter(entry.Value[0].homePos, 6000f);
				if (villageCenter == null) continue;

				if (!houseGroupsByVillage.TryGetValue(villageCenter.CenterTile, out var buildingIds))
					houseGroupsByVillage[villageCenter.CenterTile] = buildingIds = new List<int>();
				buildingIds.Add(entry.Key);
			}

			foreach (var group in houseGroupsByVillage)
			{
				var villageBuildings = group.Value
					.Where(buildingId => Houses.ContainsKey(buildingId))
					.ToList();
				if (villageBuildings.Any(buildingId => HouseHasToilet(Houses[buildingId])))
					continue;

				Vector2 centerPos = new((group.Key.x + 0.5f) * Program.TileSize,
					(group.Key.y + 0.5f) * Program.TileSize);
				foreach (int buildingId in villageBuildings
					.OrderBy(id => Vector2.DistanceSquared(Houses[id].DoorWorldPos, centerPos)))
				{
					if (TryPlaceVillageInfirmaryToilet(buildingId))
						break;
				}
			}
		}

		// Trouve une porte (tuile vide/walkable sur le bord du bâtiment)
		private static (int x, int y) FindBuildingEntrance(int minX, int maxX, int minY, int maxY)
		{
			int ts = Program.TileSize;

			//  Ne jamais choisir un coin du bâtiment comme porte : un coin est adossé à
			// un mur sur SES DEUX AXES à la fois (contrairement à une tuile de mur "normale",
			// qui n'a un mur voisin que dans un seul axe). Percer un trou à un coin crée une
			// ouverture géométriquement instable : la collision, testée séparément en X puis
			// en Y (voir World.CheckCollision), y devient incohérente selon l'angle d'approche
			// — tantôt bloquée, tantôt traversée en diagonale ("un coup sur deux"). On restreint
			// donc la recherche aux tuiles strictement entre les coins de chaque bord.
			
			// Parcourir le périmètre du bâtiment (coins exclus)
			for (int x = minX + 1; x <= maxX - 1; x++)
			{
				// Bord haut
				if (IsValidEntrance(x, minY - 1, x, minY))
					return (x, minY);
				// Bord bas
				if (IsValidEntrance(x, maxY + 1, x, maxY))
					return (x, maxY);
			}
			
			for (int y = minY + 1; y <= maxY - 1; y++)
			{
				// Bord gauche
				if (IsValidEntrance(minX - 1, y, minX, y))
					return (minX, y);
				// Bord droit
				if (IsValidEntrance(maxX + 1, y, maxX, y))
					return (maxX, y);
			}
			
			return (-1, -1);
		}

		// Vérifie si une position est une entrée valide
		private static bool IsValidEntrance(int outsideX, int outsideY, int insideX, int insideY)
		{
			//  La tuile candidate (insideX, insideY) est la tuile de MUR elle-même
			// (voir les appels dans FindBuildingEntrance : inside = bord du bâtiment).
			// On exige qu'elle porte réellement l'ID de porte (84) et non un ID de mur
			// plein (8) ni une case vide (0). Sans ce test, un mur normal dont les
			// abords sont temporairement dégagés (objet retiré, PNJ déplacé, etc.)
			// pouvait être confondu avec la porte et enregistré comme tel dans
			// _doorTiles, ce qui le rendait ensuite définitivement traversable en
			// collision physique (voir World.CheckCollision / IsDoorTile) alors
			// qu'il continue à s'afficher comme un mur plein.
			if (GetObjectIdAt(insideX, insideY) != 84)
				return false;

			// Extérieur doit être walkable
			int groundOutside = GetGroundTileIdAt(outsideX, outsideY);
			var tileOutside = WorldTileRegistry.GetTile(groundOutside);
			if (tileOutside == null || !tileOutside.Walkable)
				return false;
			
			// Pas d'obstacle à l'extérieur
			if (GetObjectIdAt(outsideX, outsideY) != 0)
				return false;
			
			// Intérieur doit être dans la maison (sous le toit) et walkable
			int groundInside = GetGroundTileIdAt(insideX, insideY);
			var tileInside = WorldTileRegistry.GetTile(groundInside);
			if (tileInside == null || !tileInside.Walkable)
				return false;
			
			return true;
		}

		// Trouve une position intérieure juste derrière la porte
		private static Vector2 FindInteriorPosition(int doorX, int doorY, int minX, int maxX, int minY, int maxY)
		{
			int ts = Program.TileSize;
			
			// Chercher la direction vers l'intérieur
			(int dx, int dy) = (0, 0);
			
			if (doorX == minX) dx = 1;      // Porte à gauche → entrer à droite
			else if (doorX == maxX) dx = -1; // Porte à droite → entrer à gauche
			else if (doorY == minY) dy = 1;  // Porte en haut → entrer en bas
			else if (doorY == maxY) dy = -1; // Porte en bas → entrer en haut
			
			int interiorX = doorX + dx;
			int interiorY = doorY + dy;
			
			// Vérifier que la position intérieure est dans les limites et walkable
			if (interiorX >= minX && interiorX <= maxX && interiorY >= minY && interiorY <= maxY)
			{
				return new Vector2(interiorX * ts + ts/2f, interiorY * ts + ts/2f - Program.FeetOffsetY);
			}
			
			// Fallback : centre du bâtiment
			int centerX = (minX + maxX) / 2;
			int centerY = (minY + maxY) / 2;
			return new Vector2(centerX * ts + ts/2f, centerY * ts + ts/2f - Program.FeetOffsetY);
		}

		public static Vector2 FindNearestFreeHouse(Vector2 worldPos, float maxDistance = 1200f)
		{
			// Prefer the per-spot reservation system so multiple occupants can live
			// in the same building when there are several interior spots. This
			// ensures each NPC gets a dedicated interior tile and won't steal
			// another NPC's home.
			if (Houses.Count == 0)
				RefreshAllHouses();

			var spot = FindNearestFreeHouseSpot(worldPos, maxDistance);
			if (spot.HasValue)
			{
				// Mark house IsOccupied if all spots are now taken
				if (Houses.TryGetValue(spot.Value.buildingId, out var h))
					h.IsOccupied = HouseInteriorSpots.TryGetValue(spot.Value.buildingId, out var spots) && spots.All(s => s.Occupied);
				return spot.Value.spotPos;
			}

			return Vector2.Zero;
		}

		public static void ForceRebuildBuildingIds()
        {
            BuildingIds.Clear();
            var chunksDict = GetChunksDict();
            var processed = new HashSet<(int x, int y)>();

            foreach (var chunkData in chunksDict.Values)
            {
                foreach (var kv in chunkData.Objects)
                {
                    var x = kv.Key.x;
                    var y = kv.Key.y;
                    if (processed.Contains((x, y))) continue;
                    int objId = GetObjectIdAt(x, y);
                    if (!IsWall(objId) && objId != 84) continue;

                    if (TryFindHouseBoundsForTile(x, y, out int minX, out int maxX, out int minY, out int maxY, out int doorX, out int doorY, out bool doorFound))
                    {
                        if (TryGetEnclosedRoofTiles(minX, maxX, minY, maxY, doorX, doorY, out var roofTiles))
                        {
                            int buildingId = ComputeBuildingId(doorX, doorY);
                            ApplyRoofToHouse(buildingId, roofTiles);
                            for (int tx = minX; tx <= maxX; tx++)
                                for (int ty = minY; ty <= maxY; ty++)
                                    processed.Add((tx, ty));
                            foreach (var rt in roofTiles)
                                processed.Add((rt.x, rt.y));
                        }
                    }
                }
            }

            foreach (var chunkData in chunksDict.Values)
            {
                foreach (var ((x, y), overlayId) in chunkData.Overlays)
                {
                    if (overlayId == 82 && !BuildingIds.ContainsKey((x, y)))
                    {
                        int uniqueBuildingId = 1000 + Math.Abs(x * 73856093 ^ y * 19349663) % 1000;
                        BuildingIds[(x, y)] = uniqueBuildingId;
                    }
                }
            }

            Console.WriteLine($" Reconstruit {BuildingIds.Count} buildingIds à partir des murs et des toits chargés");
        }

		public static void SaveChunkEntities(int chunkX, int chunkY, List<Entity> globalEntities)
        {
            //  CORRECTION DUPLICATION : on reconstruit entièrement la liste à partir de la
            // position RÉELLE des entités dans globalEntities (comme dans UpdateActiveChunks),
            // au lieu de "patcher" l'ancienne liste chunk.Entities par un rapprochement
            // espèce+distance qui pouvait confondre des entités différentes ou rater une
            // entité ayant bougé — laissant des entités fantômes derrière elle dans d'autres
            // chunks et provoquant des doublons au rechargement.
            var chunksDict = GetChunksDict();
            if (!chunksDict.TryGetValue((chunkX, chunkY), out var chunk)) return;

            var entitiesReallyInThisChunk = new List<Entity>();
            foreach (var entity in globalEntities)
            {
                //  Les compagnons d'armure (ex : petit crabe du set "Crabe", voir
                // Program.UpdateArmorCompanions) ne doivent JAMAIS être sauvegardés : ils sont
                // entièrement dérivés de l'équipement actuellement porté, et sont respawnés à
                // chaque chargement de partie si le set complet est toujours équipé. Les
                // persister créerait un doublon permanent (l'ancien + le nouveau) au rechargement.
                if (entity.IsPetCompanion) continue;

                int entityChunkX = (int)Math.Floor(entity.WorldPos.X / (CHUNK_SIZE * Program.TileSize));
                int entityChunkY = (int)Math.Floor(entity.WorldPos.Y / (CHUNK_SIZE * Program.TileSize));
                if (entityChunkX == chunkX && entityChunkY == chunkY)
                {
                    entitiesReallyInThisChunk.Add(entity);
                }
            }
            chunk.Entities = entitiesReallyInThisChunk;
        }

		public static int GetBuildingHeightAt(int x, int y)
		{
			// Récupère la hauteur maximale du bâtiment à cet emplacement
			int roofHeight = GetRoofHeight(x, y);
			return roofHeight;
		}

		public static void RebuildAllRoofs()
		{
			var chunksDict = GetChunksDict();
			// Parcourir tous les chunks chargés
			foreach (var kv in chunksDict.ToList()) // ToList() pour éviter les modifications pendant l'itération
			{
				var chunk = kv.Value;
				// Récupérer toutes les positions des murs (ID 8 ou 84)
				var wallPositions = chunk.Objects
					.Where(kvp => kvp.Value == 8 || kvp.Value == 84)
					.Select(kvp => kvp.Key)
					.ToList();

				foreach (var (x, y) in wallPositions)
				{
					UpdateRoofsAt(x, y);
				}
			}

			// Mettre à jour les données des maisons après régénération
			RefreshAllHouses();
			Console.WriteLine($" Toits régénérés pour {Houses.Count} maisons.");
		}

		private static bool _isPlayerIndoors = false;

		public static bool IsPlayerIndoors => _isPlayerIndoors;

		public static bool ShouldRenderEntity(Vector2 worldPos)
		{
			int ts = Program.TileSize;
			int tileX = (int)MathF.Floor(worldPos.X / ts);
			int tileY = (int)MathF.Floor(worldPos.Y / ts);
			int buildingId = GetBuildingIdForPosition(worldPos, tileX, tileY, ts);

			return _isPlayerIndoors
				? _currentIndoorGroupIds.Contains(buildingId)
				: buildingId == 0;
		}

		private static int GetBuildingIdForPosition(Vector2 worldPos, int tileX, int tileY, int tileSize)
		{
			if (!IsWall(GetObjectIdAt(tileX, tileY)))
				return GetBuildingIdAt(tileX, tileY);

			float fracX = worldPos.X / tileSize - tileX;
			float fracY = worldPos.Y / tileSize - tileY;
			var directions = new (int dx, int dy, float distance)[]
			{
				(-1, 0, fracX),
				(1, 0, 1f - fracX),
				(0, -1, fracY),
				(0, 1, 1f - fracY),
			};

			foreach (var direction in directions.OrderBy(direction => direction.distance))
			{
				int neighborX = tileX + direction.dx;
				int neighborY = tileY + direction.dy;
				if (!IsWall(GetObjectIdAt(neighborX, neighborY)))
					return GetBuildingIdAt(neighborX, neighborY);
			}

			return GetBuildingIdAt(tileX, tileY);
		}

		public static void UpdatePlayerIndoorStatus(Vector2 playerPos)
		{
			int ts = Program.TileSize;
			int tileX = (int)(playerPos.X / ts);
			int tileY = (int)(playerPos.Y / ts);
			// Note : les murs portent déjà le BuildingId de la maison à laquelle ils appartiennent
			// (voir ApplyRoofToHouse, qui l'assigne à la fois aux murs et aux tuiles intérieures).
			// Mais la hitbox de collision d'un mur est plus étroite que la tuile entière (CollisionSize /
			// CollisionOffset), donc le joueur peut se retrouver avec le même tileX/tileY qu'un mur tout
			// en étant encore visuellement à l'extérieur (jeu de collision, surtout sur les bords droit/bas).
			// Dans ce cas précis, on ne peut pas se fier au BuildingId du mur lui-même : il ne dit pas de
			// quel côté (intérieur/extérieur) le joueur se trouve. On regarde alors sa position fractionnaire
			// dans la tuile pour savoir de quel côté du mur il "penche", et on lit le BuildingId de la
			// première case voisine non-mur trouvée dans cette direction.
			int startBuildingId;
			int objIdAtPlayerTile = GetObjectIdAt(tileX, tileY);
			if (IsWall(objIdAtPlayerTile))
			{
				float fracX = playerPos.X / ts - tileX;
				float fracY = playerPos.Y / ts - tileY;
				var directions = new (int dx, int dy, float dist)[]
				{
					(-1, 0, fracX),        // penche vers la gauche
					(1, 0, 1f - fracX),    // penche vers la droite
					(0, -1, fracY),        // penche vers le haut
					(0, 1, 1f - fracY),    // penche vers le bas
				};
				int resolvedBuildingId = 0;
				bool resolved = false;
				foreach (var d in directions.OrderBy(d => d.dist))
				{
					int nx = tileX + d.dx, ny = tileY + d.dy;
					if (IsWall(GetObjectIdAt(nx, ny))) continue; // toujours un mur, pas une case exploitable
					resolvedBuildingId = GetBuildingIdAt(nx, ny);
					resolved = true;
					break;
				}
				startBuildingId = resolved ? resolvedBuildingId : GetBuildingIdAt(tileX, tileY);
			}
			else
			{
				startBuildingId = GetBuildingIdAt(tileX, tileY);
			}
			if (startBuildingId == 0)
			{
				_isPlayerIndoors = false;
				_currentIndoorGroupIds.Clear();
				return;
			}
			var visited = new HashSet<(int x, int y)>();
			var queue = new Queue<(int x, int y)>();
			var groupIds = new HashSet<int>();
			queue.Enqueue((tileX, tileY));
			visited.Add((tileX, tileY));
			while (queue.Count > 0)
			{
				var (cx, cy) = queue.Dequeue();
				int bid = GetBuildingIdAt(cx, cy);
				if (bid != 0) groupIds.Add(bid);
				foreach (var (dx, dy) in new[] { (1,0), (-1,0), (0,1), (0,-1) })
				{
					int nx = cx + dx, ny = cy + dy;
					if (visited.Contains((nx, ny))) continue;
					int neighborBid = GetBuildingIdAt(nx, ny);
					if (neighborBid != 0)
					{
						visited.Add((nx, ny));
						queue.Enqueue((nx, ny));
					}
				}
			}
			_isPlayerIndoors = groupIds.Count > 0;
			_currentIndoorGroupIds = groupIds;
		}
    }
}
