// RenderContext.cs
// Contexte partagé entre les phases de rendu (remplace les variables locales de l'ancienne méthode géante).
// Fichier extrait de DrawWorld.cs (classe partielle World).
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {
        /// <summary>
        /// Paramètres de DrawWorld + valeurs calculées une fois par frame (bornes visibles, grilles de tuiles...).
        /// Chaque phase de <see cref="DrawPhases"/> relit ce dont elle a besoin ici.
        /// </summary>
        private sealed class DrawWorldContext
        {
			public Vector2 playerPos;
			public Camera2D camera;
			public string anim = default!;
			public int frame;
			public float prog;
			public float facing;
			public Vector2 pDim;
			public Color pSkin;
			public Texture2D missingTex;
			public HashSet<string> destroyed = default!;
			public Dictionary<string, WorldObjectHP> objectHP = default!;
			public List<Entity> entities = default!;
			public Dictionary<int, Texture2D> tileTextures = default!;
			public List<Texture2D> hairBase = default!;
			public List<Texture2D> hairOverlay = default!;
			public Vector2 mouseWorldPos;
			public int hoverTileX;
			public int hoverTileY;
			public bool showDebug;
			public bool isCarrying;
			public bool isPlayerInWater;
			public float headAngle;
			public Vector2? sittingPosition;
			public float playerDepth;
			public IReadOnlyList<Program.LocalPlayer>? playersToRender;
			public List<SpellProjectile>? spellProjectiles;
			public bool drawPlayer;
			public int sw;
			public int sh;
			public int ts;
			public float currentTime;
			public Vector2 topLeft;
			public Vector2 bottomRight;
			public int startX;
			public int endX;
			public int startY;
			public int endY;
			public float entityCullMargin;
			public int width;
			public int heightTiles;
			public int[,] groundIds = default!;
			public int[,] objectIds = default!;
			public int[,] overlayIds = default!;
			public int[,] buildingIds = default!;
			public int[,] roofHeights = default!;
			public int[,] decorationIndex = default!;
			public int[,] wallCoveringIds = default!;
			public bool[,] isWallToHide = default!;
			public bool[,] tileLit = default!;
			public bool drawBiomeBorders;
			/// <summary>Nombre d'objets non vides dans la fenêtre (renseigné par FillTileGrid, lu par le debug).</summary>
			public int visibleObjectCount;

			public bool IsEntityNearScreen(Vector2 pos)
			{
				if (pos.X < topLeft.X - entityCullMargin || pos.X > bottomRight.X + entityCullMargin)
					return false;

				float minY = topLeft.Y - entityCullMargin;
				float maxY = bottomRight.Y + entityCullMargin;
				if (pos.Y >= minY && pos.Y <= maxY)
					return true;

				return false;
			}

			public bool IsSegmentNearScreen(Vector2 start, Vector2 end)
			{
				return MathF.Max(start.X, end.X) >= topLeft.X - entityCullMargin &&
					MathF.Min(start.X, end.X) <= bottomRight.X + entityCullMargin &&
					MathF.Max(start.Y, end.Y) >= topLeft.Y - entityCullMargin &&
					MathF.Min(start.Y, end.Y) <= bottomRight.Y + entityCullMargin;
			}

			public bool IsWorldRectVisible(float x, float y, float rectWidth, float rectHeight)
			{
				return x + rectWidth > topLeft.X && x < bottomRight.X
					&& y + rectHeight > topLeft.Y && y < bottomRight.Y;
			}

			/// <summary>Calcule les bornes visibles, alloue/réutilise les buffers de grille et construit le contexte.</summary>
			public static DrawWorldContext Create(
				Vector2 playerPos,
				Camera2D camera,
				string anim,
				int frame,
				float prog,
				float facing,
				Vector2 pDim,
				Color pSkin,
				Texture2D missingTex,
				HashSet<string> destroyed,
				Dictionary<string, WorldObjectHP> objectHP,
				List<Entity> entities,
				Dictionary<int, Texture2D> tileTextures,
				List<Texture2D> hairBase,
				List<Texture2D> hairOverlay,
				Vector2 mouseWorldPos,
				int hoverTileX,
				int hoverTileY,
				bool showDebug,
				bool isCarrying,
				bool isPlayerInWater,
				float headAngle,
				Vector2? sittingPosition,
				float playerDepth,
				IReadOnlyList<Program.LocalPlayer>? playersToRender,
				List<SpellProjectile>? spellProjectiles,
				bool drawPlayer)
			{
				int sw = Raylib.GetScreenWidth();
				int sh = Raylib.GetScreenHeight();
				int ts = Program.TileSize;
				float currentTime = Program.GetGameTime();

				//  PERF (dézoom) : informe EntityRenderer du niveau de zoom courant, pour son LOD
				// interne (voir _currentZoomLevel dans EntityRenderer.cs). Une seule fois par frame,
				// avant tout DrawEntity de cette frame.
				EntityRenderer.SetCurrentZoomLevel(camera.Zoom);

				Vector2 topLeft = Raylib.GetScreenToWorld2D(new Vector2(0, 0), camera);
				Vector2 bottomRight = Raylib.GetScreenToWorld2D(new Vector2(sw, sh), camera);

				const int MAX_RENDER_TILES_X = 120;
				const int MAX_RENDER_TILES_Y = 80;

				//  MARGE POUR ÉLÉMENTS "HAUTS" (arbres, toits...) : ces objets sont dessinés
				// depuis leur tuile d'ancrage (souvent la base) avec un décalage vertical/horizontal
				// de plusieurs tuiles (roofHeightTiles ou DrawOffset). Avec une marge d'1 seule tuile, dès que la
				// tuile d'ancrage sort de la fenêtre visible (typiquement en bas de l'écran), l'objet
				// entier disparaît instantanément — alors qu'une partie de son sprite (le haut de
				// l'arbre, le bas du toit) devrait encore être visible à l'écran. On élargit donc la
				// zone de tuiles VALIDÉES (celle qui alimente les tableaux d'objets/...)
				// bien au-delà du strict cadrage caméra, pour que ces tuiles d'ancrage restent prises
				// en compte et leurs objets dessinés jusqu'à ce qu'ils quittent réellement l'écran.
				// Coût : quelques dizaines de tuiles de plus à traiter par frame, négligeable face au
				// gain visuel (pas de pop-out brutal), et toujours borné par MAX_RENDER_TILES_X/Y.
				const int TALL_FEATURE_MARGIN_X = 3;
				const int TALL_FEATURE_MARGIN_Y = 8;

				int startX = (int)Math.Floor(topLeft.X / ts) - 1 - TALL_FEATURE_MARGIN_X;
				int endX = (int)Math.Ceiling(bottomRight.X / ts) + 1 + TALL_FEATURE_MARGIN_X;
				int startY = (int)Math.Floor(topLeft.Y / ts) - 1;
				int endY = (int)Math.Ceiling(bottomRight.Y / ts) + 1 + TALL_FEATURE_MARGIN_Y;

				float entityCullMargin = ts * 2f;

				int renderWidth = endX - startX;
				int renderHeight = endY - startY;

				if (renderWidth > MAX_RENDER_TILES_X)
				{
					int excess = renderWidth - MAX_RENDER_TILES_X;
					startX += excess / 2;
					endX -= excess / 2;
				}
				if (renderHeight > MAX_RENDER_TILES_Y)
				{
					int excess = renderHeight - MAX_RENDER_TILES_Y;
					startY += excess / 2;
					endY -= excess / 2;
				}

				//  BUG CORRIGÉ ("Arithmetic operation resulted in an overflow" en cas de téléportation
				// lointaine) : ce clamp raisonnait en coordonnées ABSOLUES (± 10000 tuiles autour de
				// l'origine du monde), pas en distance relative à la caméra. Dès que le joueur se
				// téléporte loin (ex: tuile 125000), startX ET endX valent tous les deux ~125000
				// (déjà bornés à une petite fenêtre par MAX_RENDER_TILES_X/Y ci-dessus) : mais
				// Math.Min(endX, 10000) écrasait alors endX à 10000 tout seul, sans toucher startX
				// (toujours ~124995, car > -10000). endX se retrouvait < startX, donc
				// `width = endX - startX + 1` devenait négatif, et `new int[width, heightTiles]`
				// avec une dimension négative lève justement OverflowException. Comme
				// MAX_RENDER_TILES_X/Y borne déjà la fenêtre de rendu (~120x80 tuiles) quelle que
				// soit la position du joueur dans le monde, ce clamp absolu est à la fois inutile et
				// dangereux : on le retire, et on ne garde qu'un garde-fou final qui ne peut jamais
				// inverser start/end (protège seulement contre un topLeft/bottomRight invalide,
				// NaN/Infini, qui donnerait un cast (int) aberrant).
				int width = endX - startX + 1;
				int heightTiles = endY - startY + 1;

				const int MAX_SAFE_DIM = MAX_RENDER_TILES_X + MAX_RENDER_TILES_Y + 4; // marge large, jamais atteint en usage normal
				if (width <= 0 || width > MAX_SAFE_DIM)
				{
					width = Math.Clamp(width, 1, MAX_SAFE_DIM);
					endX = startX + width - 1;
				}
				if (heightTiles <= 0 || heightTiles > MAX_SAFE_DIM)
				{
					heightTiles = Math.Clamp(heightTiles, 1, MAX_SAFE_DIM);
					endY = startY + heightTiles - 1;
				}

				// Buffers réutilisés : la grille est entièrement réécrite ci-dessous à chaque frame.
				EnsureRenderTileBuffers(width, heightTiles);
				int[,] groundIds = _renderGroundIds;
				int[,] objectIds = _renderObjectIds;
				int[,] overlayIds = _renderOverlayIds;
				int[,] buildingIds = _renderBuildingIds;
				int[,] roofHeights = _renderRoofHeights;
				int[,] decorationIndex = _renderDecorationIndex;
				int[,] wallCoveringIds = _renderWallCoveringIds;
				bool[,] isWallToHide = _renderIsWallToHide;
				bool[,] tileLit = _renderTileLit;
				bool drawBiomeBorders = !_isUnderground && !_isPlayerIndoors;

				return new DrawWorldContext
				{
					playerPos = playerPos,
					camera = camera,
					anim = anim,
					frame = frame,
					prog = prog,
					facing = facing,
					pDim = pDim,
					pSkin = pSkin,
					missingTex = missingTex,
					destroyed = destroyed,
					objectHP = objectHP,
					entities = entities,
					tileTextures = tileTextures,
					hairBase = hairBase,
					hairOverlay = hairOverlay,
					mouseWorldPos = mouseWorldPos,
					hoverTileX = hoverTileX,
					hoverTileY = hoverTileY,
					showDebug = showDebug,
					isCarrying = isCarrying,
					isPlayerInWater = isPlayerInWater,
					headAngle = headAngle,
					sittingPosition = sittingPosition,
					playerDepth = playerDepth,
					playersToRender = playersToRender,
					spellProjectiles = spellProjectiles,
					drawPlayer = drawPlayer,
					sw = sw,
					sh = sh,
					ts = ts,
					currentTime = currentTime,
					topLeft = topLeft,
					bottomRight = bottomRight,
					startX = startX,
					endX = endX,
					startY = startY,
					endY = endY,
					entityCullMargin = entityCullMargin,
					width = width,
					heightTiles = heightTiles,
					groundIds = groundIds,
					objectIds = objectIds,
					overlayIds = overlayIds,
					buildingIds = buildingIds,
					roofHeights = roofHeights,
					decorationIndex = decorationIndex,
					wallCoveringIds = wallCoveringIds,
					isWallToHide = isWallToHide,
					tileLit = tileLit,
					drawBiomeBorders = drawBiomeBorders
				};
			}
        }
    }
}
