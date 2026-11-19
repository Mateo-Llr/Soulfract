// TileGridRendering.cs
// Phase 1 : remplit les tableaux de tuiles visibles (sol, objets, overlays...) en une passe.
// Fichier extrait de DrawWorld.cs (classe partielle World).
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Soulfract
{
    public static partial class World
    {
        private static partial class DrawPhases
        {
			// Drapeaux du cache par rangée de chunks (voir FillTileGrid).
			private const byte F_LOADED = 1;
			private const byte F_GROUND = 2;
			private const byte F_OBJECTS = 4;
			private const byte F_OVERLAYS = 8;
			private const byte F_ROOFS = 16;
			private const byte F_DECOS = 32;

			// Buffers réutilisés d'une frame à l'autre (zéro allocation) ; ThreadStatic par sécurité.
			[ThreadStatic] private static ChunkData?[]? s_rowChunks;
			[ThreadStatic] private static byte[]? s_rowFlags;

			/// <summary>Division entière arrondie vers le bas (identique au calcul de chunk d'origine).</summary>
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private static int FloorDivChunk(int v) => v >= 0 ? v / CHUNK_SIZE : (v - CHUNK_SIZE + 1) / CHUNK_SIZE;

			/// <summary>Remplit les grilles de rendu pour la fenêtre visible. Réutilise un seul chunk par tuile.</summary>
			internal static void FillTileGrid(DrawWorldContext ctx)
			{
				int startX = ctx.startX;
				int endX = ctx.endX;
				int startY = ctx.startY;
				int endY = ctx.endY;
				int width = ctx.width;
				int heightTiles = ctx.heightTiles;
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
				//  PERF (dézoom) : chunk récupéré UNE SEULE FOIS par tuile (et même une seule fois par chunk
				// et par passe, voir le cache par rangée ci-dessous), dictionnaires internes lus directement.
				// Les fonctions globales (GetGroundTileIdAt, GetBuildingIdAt, GetRoofHeight) ne sont rappelées
				// qu'en repli, pour les tuiles sans entrée explicite dans le chunk (génération procédurale /
				// cache climat).
				int visibleObjectCount = 0;

				//  PERF : valeurs invariantes de la passe, lues une seule fois au lieu d'une fois par tuile.
				bool indoors = _isPlayerIndoors;
				bool needBuildingIds = indoors || DebugOverlaySettings.BuildingZones;

				//  PERF : cache de chunks par "rangée de chunks" pour la colonne de chunks courante.
				// La boucle est x (externe) / y (interne) : l'ancien cache (un seul chunk) était invalidé
				// à chaque nouvelle colonne x, soit ~(hauteur/16) GetChunkAt par colonne. Ici, tant que x
				// reste dans la même colonne de chunks, chaque chunk n'est demandé qu'une seule fois.
				// On mémorise aussi quels dictionnaires du chunk sont non vides : un dictionnaire vide
				// (cas fréquent : overlays, décorations, toits, objets en pleine nature) évite tous ses
				// lookups (TryGetValue sur clé tuple) pour les 256 tuiles du chunk.
				int firstCy = FloorDivChunk(startY);
				int rowCount = Math.Max(1, FloorDivChunk(endY) - firstCy + 1);
				if (s_rowChunks == null || s_rowFlags == null || s_rowChunks.Length < rowCount)
				{
					s_rowChunks = new ChunkData?[rowCount + 4];
					s_rowFlags = new byte[rowCount + 4];
				}
				ChunkData?[] rowChunks = s_rowChunks;
				byte[] rowFlags = s_rowFlags;
				int colCx = int.MinValue;

				//  PERF : IsWallToHide(oid) est appelé pour chaque tuile en intérieur, la grande majorité
				// ayant le même oid (0) que la précédente : on mémorise le dernier résultat.
				int lastWallOid = int.MinValue;
				bool lastWallRes = false;

				for (int x = startX; x <= endX; x++)
				{
					int ix = Math.Clamp(x - startX, 0, width - 1);
					int cx = FloorDivChunk(x);
					if (cx != colCx)
					{
						colCx = cx;
						Array.Clear(rowFlags, 0, rowCount);
					}
					for (int y = startY; y <= endY; y++)
					{
						int iy = Math.Clamp(y - startY, 0, heightTiles - 1);
						bool isTileLit = Program.IsTileLit(x, y);
						tileLit[ix, iy] = isTileLit;
						if (!isTileLit)
						{
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
							int slot = FloorDivChunk(y) - firstCy;
							byte fl = rowFlags[slot];
							ChunkData? chunk;
							if (fl == 0)
							{
								chunk = GetChunkAt(x, y);
								rowChunks[slot] = chunk;
								fl = F_LOADED;
								if (chunk != null)
								{
									if (chunk.GroundOverrides.Count != 0) fl |= F_GROUND;
									if (chunk.Objects.Count != 0) fl |= F_OBJECTS;
									if (chunk.Overlays.Count != 0) fl |= F_OVERLAYS;
									if (chunk.RoofHeights.Count != 0) fl |= F_ROOFS;
									if (chunk.Decorations.Count != 0) fl |= F_DECOS;
								}
								rowFlags[slot] = fl;
							}
							else
							{
								chunk = rowChunks[slot];
							}

							var key = (x, y);

							//  PERF : si pas d'override dans le chunk, on va DIRECT à la génération procédurale.
							int gid = ((fl & F_GROUND) != 0 && chunk!.GroundOverrides.TryGetValue(key, out int gv))
								? gv : GetProceduralGroundTileId(x, y);
							int oid = (fl & F_OBJECTS) != 0 ? chunk!.Objects.GetValueOrDefault(key, 0) : 0;
							int ovid = (fl & F_OVERLAYS) != 0 ? chunk!.Overlays.GetValueOrDefault(key, 0) : 0;

							// Les buildingIds vivent dans un registre séparé (BuildingIds), pas dans ChunkData.
							//  PERF : buildingIds n'est lu que en intérieur (filtre) ou en debug F3.
							buildingIds[ix, iy] = needBuildingIds ? GetBuildingIdAt(x, y) : 0;

							// Dans l'enceinte où se trouve le joueur, les données de l'étage actif
							// remplacent celles du rez-de-chaussée. Le reste de l'étage reste ouvert.
							bool onUpperFloor = indoors && CurrentHouseFloor > 0;
							bool useFloorData = onUpperFloor
								&& !IsCurrentFloorOpenAir
								&& IsCurrentFloorEnclosedTile(x, y);
							if (useFloorData
								&& (buildingIds[ix, iy] == CurrentHouseBuildingId
									|| TryGetCurrentHouseFloorTile(x, y, out _)))
								buildingIds[ix, iy] = CurrentHouseBuildingId;
							if (useFloorData)
							{
								gid = GetGroundTileIdAt(x, y);
								oid = GetObjectIdAt(x, y);
								ovid = GetOverlayAt(x, y);
							}

							// CORRECTIF : groundIds est écrit APRÈS le remplacement de gid par le sol de
							// l'étage (avant, il gardait l'herbe procédurale).
							groundIds[ix, iy] = gid;
							objectIds[ix, iy] = oid;
							if (oid != 0) visibleObjectCount++;
							overlayIds[ix, iy] = ovid;

							//  PERF : roofHeights n'est lu que pour les tuiles ayant un overlay (toit).
							if (ovid != 0)
								roofHeights[ix, iy] = ((fl & F_ROOFS) != 0 && chunk!.RoofHeights.TryGetValue(key, out int rhv))
									? rhv : GetRoofHeight(x, y);
							else
								roofHeights[ix, iy] = 0;

							//  PERF : isWallToHide n'est lu qu'en intérieur ; wallCoveringIds qu'avec un objet.
							if (indoors)
							{
								if (oid != lastWallOid)
								{
									lastWallOid = oid;
									lastWallRes = IsWallToHide(oid);
								}
								isWallToHide[ix, iy] = lastWallRes;
							}
							else
							{
								isWallToHide[ix, iy] = false;
							}
							wallCoveringIds[ix, iy] = oid != 0 ? (chunk?.GetWallCoveringAt(x, y) ?? 0) : 0;
							if (useFloorData && oid != 0)
								wallCoveringIds[ix, iy] = GetWallCoveringIdAt(x, y);

							// Décoration (même chunk déjà en main) ; jamais sur un sol d'étage.
							decorationIndex[ix, iy] = (!useFloorData && (fl & F_DECOS) != 0
								&& chunk!.Decorations.TryGetValue(key, out int deco))
								? deco : -1;
						}
						catch (Exception ex)
						{
							// Log l'erreur mais continue pour éviter le crash
							Console.WriteLine($" Erreur à ({x},{y}) : {ex.Message}");
						}
					}
				}

				// Ne pas garder de références de chunks dans le buffer statique entre deux frames.
				Array.Clear(rowChunks, 0, rowCount);
				ctx.visibleObjectCount = visibleObjectCount;
			}
        }
    }
}
