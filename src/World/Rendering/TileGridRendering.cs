// TileGridRendering.cs
// Phase 1 : remplit les tableaux de tuiles visibles (hauteurs, sol, objets, overlays...) en une passe.
// Fichier extrait de DrawWorld.cs (classe partielle World).
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {
        private static partial class DrawPhases
        {
			/// <summary>Remplit les grilles de rendu (heights, groundIds, objectIds, overlayIds...) pour la fenêtre visible. Réutilise un seul chunk par tuile.</summary>
			internal static void FillTileGrid(DrawWorldContext ctx)
			{
				int startX = ctx.startX;
				int endX = ctx.endX;
				int startY = ctx.startY;
				int endY = ctx.endY;
				int width = ctx.width;
				int heightTiles = ctx.heightTiles;
				int[,] heights = ctx.heights;
				int[,] groundIds = ctx.groundIds;
				int[,] objectIds = ctx.objectIds;
				int[,] overlayIds = ctx.overlayIds;
				int[,] buildingIds = ctx.buildingIds;
				int[,] roofHeights = ctx.roofHeights;
				int[,] decorationIndex = ctx.decorationIndex;
				int[,] wallCoveringIds = ctx.wallCoveringIds;
				bool[,] isWallToHide = ctx.isWallToHide;
				bool[,] tileLit = ctx.tileLit;

				// Remplir les tableaux en une seule passe
				//
				//  PERF (dézoom) : ce bloc tournait auparavant sur un chunk (GetChunkAt) DIFFÉRENT
				// par appel — un pour la hauteur, un pour le sol, un pour l'objet, un pour l'overlay,
				// un pour le revêtement mural, un pour la décoration — soit jusqu'à 8 lookups de
				// Dictionary<(int,int), ChunkData> pour LA MÊME tuile, chacun recalculant en plus
				// chunkX/chunkY. En dézoom, MAX_RENDER_TILES_X/Y (120x80) peut porter ce nombre de
				// tuiles à ~9600/frame, donc jusqu'à ~77 000 lookups de chunk rien que pour cette
				// boucle. On récupère maintenant le chunk UNE SEULE FOIS par tuile et on lit ses
				// dictionnaires internes directement ; les fonctions globales (GetHeightAt,
				// GetGroundTileIdAt, GetBuildingIdAt, GetRoofHeight) ne sont rappelées qu'en repli,
				// pour les tuiles qui n'ont pas encore d'entrée explicite dans le chunk (génération
				// procédurale / cache climat), ce qui reste correct au sous-sol/en extérieur du
				// chunk chargé.
				int visibleObjectCount = 0;
				//  PERF : on ne redemande le chunk que quand on change de chunk (la boucle parcourt y en interne,
				// donc ~15 tuiles sur 16 réutilisent le chunk précédent) au lieu d'un GetChunkAt par tuile.
				ChunkData? fillChunk = null;
				int fillChunkX = int.MinValue, fillChunkY = int.MinValue;
				for (int x = startX; x <= endX; x++)
				{
					int ix = Math.Clamp(x - startX, 0, width - 1);
					for (int y = startY; y <= endY; y++)
					{
						int iy = Math.Clamp(y - startY, 0, heightTiles - 1);
						bool isTileLit = Program.IsTileLit(x, y);
						tileLit[ix, iy] = isTileLit;
						if (!isTileLit)
						{
							heights[ix, iy] = 0;
							groundIds[ix, iy] = 0;
							objectIds[ix, iy] = 0;
							overlayIds[ix, iy] = 0;
							buildingIds[ix, iy] = 0;
							roofHeights[ix, iy] = 0;
							decorationIndex[ix, iy] = -1;
							wallCoveringIds[ix, iy] = 0;
							isWallToHide[ix, iy] = false;
							continue;
						}
					
						try
						{
							int fillCx = x / CHUNK_SIZE;
							int fillCy = y / CHUNK_SIZE;
							if (x < 0) fillCx = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
							if (y < 0) fillCy = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
							if (fillCx != fillChunkX || fillCy != fillChunkY)
							{
								fillChunk = GetChunkAt(x, y);
								fillChunkX = fillCx;
								fillChunkY = fillCy;
							}
							var chunk = fillChunk;

							//  PERF : si pas d'override dans le chunk, on va DIRECT à la génération procédurale
							// (GetGroundTileIdAt refaisait inutilement chunk + override, qui ne pouvaient que rater).
							int gid = (chunk != null && chunk.GroundOverrides.TryGetValue((x, y), out int gv))
								? gv : GetProceduralGroundTileId(x, y);
							int h = _isUnderground
								? chunk != null && chunk.Heights.TryGetValue((x, y), out int undergroundHeight)
									? undergroundHeight : 0
								: (_isInDungeon ? GetHeightAt(x, y) : GetHeightAtWithChunk(x, y, chunk));
							if (!_isUnderground && gid == 10)
								h = (int)WorldGeneration.GetWaterSurfaceHeight();
							heights[ix, iy] = h;
							groundIds[ix, iy] = gid;

							int oid = chunk?.Objects.GetValueOrDefault((x, y), 0) ?? 0;
							objectIds[ix, iy] = oid;
							if (oid != 0) visibleObjectCount++;

							int ovid = chunk?.Overlays.GetValueOrDefault((x, y), 0) ?? 0;
							overlayIds[ix, iy] = ovid;

							// Les buildingIds vivent dans un registre séparé (BuildingIds), pas dans
							// ChunkData : pas de lookup redondant à éliminer ici.
							//  PERF : buildingIds n'est lu que en intérieur (filtre) ou en debug F3.
							buildingIds[ix, iy] = (_isPlayerIndoors || DebugOverlaySettings.BuildingZones) ? GetBuildingIdAt(x, y) : 0;

							//  PERF : roofHeights n'est lu que pour les tuiles ayant un overlay (toit).
							// Avant : GetRoofHeight() appelé en repli pour CHAQUE tuile sans entrée explicite.
							if (ovid != 0)
								roofHeights[ix, iy] = (chunk != null && chunk.RoofHeights.TryGetValue((x, y), out int rhv))
									? rhv : GetRoofHeight(x, y);
							else
								roofHeights[ix, iy] = 0;

							//  PERF : isWallToHide n'est lu qu'en intérieur ; wallCoveringIds qu'avec un objet.
							isWallToHide[ix, iy] = _isPlayerIndoors && IsWallToHide(oid);
							wallCoveringIds[ix, iy] = oid != 0 ? (chunk?.GetWallCoveringAt(x, y) ?? 0) : 0;

							// Décoration (même chunk déjà en main, plus besoin de le redemander)
							decorationIndex[ix, iy] = (chunk != null && chunk.Decorations.TryGetValue((x, y), out int deco))
								? deco : -1;
						}
						catch (Exception ex)
						{
							// Log l'erreur mais continue pour éviter le crash
							Console.WriteLine($" Erreur à ({x},{y}) : {ex.Message}");
						}
					}
				}
				ctx.visibleObjectCount = visibleObjectCount;
			}
        }
    }
}
