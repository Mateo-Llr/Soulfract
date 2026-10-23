// DrawWorld.cs
// Point d'entrée du rendu du monde. Fichier extrait de World.Core.cs — classe partielle World.
//
// L'ancienne méthode géante (~3300 lignes) est découpée en phases, une par fichier :
//   Context      : DrawWorldContext (paramètres + bornes visibles + grilles de tuiles)
//   TileGrid     : remplissage des grilles de tuiles
//   Terrain      : sol, eau, bordures de biomes
//   Boats        : radeaux + objets posés dessus
//   GroundItems  : items au sol
//   WorldObjects : objets du monde, icônes de portes
//   Roofs        : toits / overlays
//   Vehicles     : voitures, wagons
//   Entities     : entités (client / host / solo)
//   Wires        : fils électriques
//   Players      : joueurs locaux supplémentaires + joueur principal
//   RenderQueue  : init, projectiles, éléments externes, tri
//   Debug        : overlay de debug
//   Hud          : prompts d'interaction, bandeau grotte
//   Underwater   : CODE MORT (ancien rendu sous-marin, jamais appelé)
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {
		public static void DrawWorld(
			Vector2 playerPos,
			Camera2D camera,
			string anim, int frame, float prog,
			float facing, Vector2 pDim, Color pSkin,
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
			Vector2? sittingPosition = null,
			float playerDepth = 0f,
			IReadOnlyList<Program.LocalPlayer>? playersToRender = null,
			List<SpellProjectile>? spellProjectiles = null,
			bool drawPlayer = true)
		{
			_tileTextures = tileTextures;
			PerfStats.StartSection(PerfStats.Section.WorldDrawSetup);
			var ctx = DrawWorldContext.Create(
				playerPos,
				camera,
				anim,
				frame,
				prog,
				facing,
				pDim,
				pSkin,
				missingTex,
				destroyed,
				objectHP,
				entities,
				tileTextures,
				hairBase,
				hairOverlay,
				mouseWorldPos,
				hoverTileX,
				hoverTileY,
				showDebug,
				isCarrying,
				isPlayerInWater,
				headAngle,
				sittingPosition,
				playerDepth,
				playersToRender,
				spellProjectiles,
				drawPlayer);
			PerfStats.EndSection(PerfStats.Section.WorldDrawSetup);

			PerfStats.StartSection(PerfStats.Section.TileGridFill);
			DrawPhases.FillTileGrid(ctx);
			PerfStats.EndSection(PerfStats.Section.TileGridFill);

			// ---- Terrain (dessin direct) ----
			PerfStats.StartSection(PerfStats.Section.TerrainDraw);
			DrawPhases.DrawTerrain(ctx);
			DrawPhases.DrawBoatTiles(ctx);
			DrawPhases.InitRenderItems(ctx);
			PerfStats.EndSection(PerfStats.Section.TerrainDraw);

			// ---- Construction de la liste de RenderItems (triés par Y ensuite) ----
			PerfStats.StartSection(PerfStats.Section.WorldRenderPrepare);
			DrawPhases.AddHouseDoorIcons(ctx);
			DrawPhases.AddGroundItems(ctx);
			DrawPhases.AddBoatObjects(ctx);
			DrawPhases.AddWorldObjects(ctx);
			DrawPhases.AddRoofsAndOverlays(ctx);
			DrawPhases.AddCarsAndWagons(ctx);
			DrawPhases.AddEntities(ctx);
			DrawPhases.AddWires(ctx);
			DrawPhases.AddExtraPlayers(ctx);
			DrawPhases.AddMainPlayer(ctx);
			DrawPhases.AddMountedPlayerWagon(ctx);
			DrawPhases.AddSpellProjectiles(ctx);
			DrawPhases.AddExternalItems(ctx);
			PerfStats.EndSection(PerfStats.Section.WorldRenderPrepare);

			// ---- Tri puis dessin ----
			var renderItems = _renderItemsPool;
			PerfStats.NotifyRenderItems(renderItems.Count);
			PerfStats.StartSection(PerfStats.Section.RenderItemSort);
			DrawPhases.SortRenderItems(ctx);
			PerfStats.EndSection(PerfStats.Section.RenderItemSort);
			PerfStats.StartSection(PerfStats.Section.RenderItemDraw);
			foreach (var item in renderItems) item.Draw();
			PerfStats.EndSection(PerfStats.Section.RenderItemDraw);
			PerfStats.EndEntityRenderFrame();
			_renderItemsPool = renderItems; // On garde la liste pour reuse

			// ---- Debug + interface ----
			PerfStats.StartSection(PerfStats.Section.WorldDrawTail);
			PerfStats.StartSection(PerfStats.Section.WorldDebugOverlay);
			DrawPhases.DrawDebugOverlay(ctx);
			PerfStats.EndSection(PerfStats.Section.WorldDebugOverlay);

			PerfStats.StartSection(PerfStats.Section.InteractionPrompts);
			DrawPhases.DrawInteractionPrompts(ctx);
			PerfStats.EndSection(PerfStats.Section.InteractionPrompts);

			DrawPhases.DrawUndergroundBanner(ctx);
			PerfStats.EndSection(PerfStats.Section.WorldDrawTail);
		}
    }
}
