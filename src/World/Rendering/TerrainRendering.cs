// TerrainRendering.cs
// Sol, falaises, eau et bordures de biomes.
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
			/// <summary>Phase 2 : fond (sous-sol / intérieur), mise à jour du shader d'eau, puis sol + falaises + bordures de biomes.</summary>
			internal static void DrawTerrain(DrawWorldContext ctx)
			{
				Vector2 playerPos = ctx.playerPos;
				Texture2D missingTex = ctx.missingTex;
				List<Entity> entities = ctx.entities;
				bool isPlayerInWater = ctx.isPlayerInWater;
				int sw = ctx.sw;
				int sh = ctx.sh;
				float currentTime = ctx.currentTime;

				Texture2D cliffDownTex = GetCliffDownTexture(missingTex);
				Texture2D cliffBorderTex = GetCliffBorderTexture(missingTex);

				if (_isUnderground)
					Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 100));
				if (_isPlayerIndoors)
					Raylib.ClearBackground(Color.Black);

				// Shader d'eau (caustiques procédurales) : horloge mise à jour une fois par
				// frame, avant la boucle de tuiles qui va s'en servir pour chaque tuile d'eau.
				UpdateWaterShaderTime(currentTime, playerPos, isPlayerInWater, entities);
				_renderBiomeBorders.Clear();

				DrawGroundTiles(ctx, cliffDownTex, cliffBorderTex);
				DrawBiomeBorders(ctx);
			}

			/// <summary>Sol (eau via shader, textures, rail legacy), décorations, zones debug et falaises, tout en une seule boucle.</summary>
			internal static void DrawGroundTiles(DrawWorldContext ctx, Texture2D cliffDownTex, Texture2D cliffBorderTex)
			{
				Texture2D missingTex = ctx.missingTex;
				Dictionary<int, Texture2D> tileTextures = ctx.tileTextures;
				int ts = ctx.ts;
				float currentTime = ctx.currentTime;
				int startX = ctx.startX;
				int startY = ctx.startY;
				int endY = ctx.endY;
				int width = ctx.width;
				int heightTiles = ctx.heightTiles;
				int[,] heights = ctx.heights;
				int[,] groundIds = ctx.groundIds;
				int[,] objectIds = ctx.objectIds;
				int[,] buildingIds = ctx.buildingIds;
				int[,] decorationIndex = ctx.decorationIndex;
				bool[,] isWallToHide = ctx.isWallToHide;
				bool[,] tileLit = ctx.tileLit;
				bool drawBiomeBorders = ctx.drawBiomeBorders;

				// =================================================================================
				// 1. SOL, DÉCORATIONS, BORDURES, FALAISES (tout en une seule boucle)
				// =================================================================================
				for (int ix = 0; ix < width; ix++)
				{
					int worldX = startX + ix;
					for (int iy = 0; iy < heightTiles; iy++)
					{
						int worldY = startY + iy;
						if (drawBiomeBorders)
							CollectBiomeBordersForTile(groundIds, ix, iy, width, heightTiles);
						if (!tileLit[ix, iy])
						{
							EnsureWaterShaderInactive();
							continue;
						}
						int height = heights[ix, iy];
						int drawY = worldY * ts - (height * ts / 4);
						int groundId = groundIds[ix, iy];
						int objectId = objectIds[ix, iy];
						bool tileDrawVisible = ctx.IsWorldRectVisible(worldX * ts, drawY, ts, ts);
						if (!tileDrawVisible)
							EnsureWaterShaderInactive();

						if (tileDrawVisible)
						{
							// Filtre intérieur
							if (_isPlayerIndoors)
							{
								int tileBld = buildingIds[ix, iy];
								if (!_currentIndoorGroupIds.Contains(tileBld)) continue;
							}

							bool hideGround = false;
							if (isWallToHide[ix, iy] && _isPlayerIndoors)
							{
								int tileAboveY = worldY - 1;
								if (tileAboveY >= startY && tileAboveY <= endY)
								{
									int aboveOverlay = GetOverlayAt(worldX, tileAboveY);
									if (aboveOverlay == 82) hideGround = true;
								}
							}

							if (hideGround) EnsureWaterShaderInactive();

							if (!hideGround)
							{
							// Sol
							if (groundId == 10)
							{
								// Ancien rendu : une texture d'eau statique par tuile, teintée
								// uniquement par l'alpha (GetWaterColor). Remplacé par un shader
								// procédural (voir Water.cs / shaders/water.fs) : deux
								// couches de bruit qui défilent forment les caustiques, un
								// troisième bruit très lent les fait "flotter" doucement — plus
								// aucune texture d'eau n'est nécessaire.
								//  PERF : une seule lecture de l'élévation (alpha + profondeur du shader).
								float waterElevation = GetClimateAt(worldX, worldY).elevation;
								byte waterAlphaTile = GetWaterColorFromElevation(waterElevation).A;
								EnsureWaterShaderActive();
								ComputeWaterShoreMasks(groundIds, ix, iy, width, heightTiles, out byte shoreNear, out byte shoreFar);
								DrawWaterTileShader(worldX, worldY, drawY, ts, waterAlphaTile, shoreNear, shoreFar, waterElevation);
							}
							else
							{
								Texture2D tex = GetGroundTileTextureFast(worldX, worldY, groundId, currentTime);
								if (tex.Id == 0) tex = tileTextures.GetValueOrDefault(groundId);

								// On quitte le mode shader d'eau avant de dessiner une texture
								// normale, sinon le fragment shader de l'eau s'appliquerait
								// aussi à cette texture.
								EnsureWaterShaderInactive();

								if (tex.Id == 0 && missingTex.Id != 0) tex = missingTex;
								if (tex.Id != 0)
								{
									bool isLegacyRail = groundId == 123;
									Raylib.DrawTexturePro(tex,
										new Rectangle(0, 0, tex.Width, tex.Height),
										isLegacyRail
											? new Rectangle(worldX * ts + ts / 2f, drawY + ts / 2f, ts, ts)
											: new Rectangle(worldX * ts, drawY, ts, ts),
										isLegacyRail ? new Vector2(ts / 2f, ts / 2f) : Vector2.Zero,
										isLegacyRail ? GetRailwayRotation(worldX, worldY) : 0, Color.White);
								}
								else
								{
									Raylib.DrawRectangle(worldX * ts, drawY, ts, ts, new Color(255, 0, 220, 200));
								}
							}

							// Zones de maisons visibles avec le mode debug (F3).
							if (DebugOverlaySettings.BuildingZones && buildingIds[ix, iy] != 0)
								Raylib.DrawRectangle(worldX * ts, drawY, ts, ts, new Color(170, 70, 230, 85));

								// Décoration
								int decoIdx = decorationIndex[ix, iy];
								if (decoIdx >= 0 && WorldTileRegistry.DecorationTextures.TryGetValue(groundId, out var decoTexList) && decoIdx < decoTexList.Count)
								{
									Texture2D decoTex = decoTexList[decoIdx];
									if (decoTex.Id != 0)
									{
										Raylib.DrawTexturePro(decoTex,
											new Rectangle(0, 0, decoTex.Width, decoTex.Height),
											new Rectangle(worldX * ts, drawY, ts, ts),
											Vector2.Zero, 0, Color.White);
									}
								}
							}
						}

						// Le relief du fond marin reste utilisé par le dégradé de profondeur,
						// mais ne dessine ni falaises sous-marines ni ruptures de hauteur au rivage.
						if (!_isUnderground && !_isPlayerIndoors && groundId != 10)
						{
							// Bas
							if (iy + 1 < heightTiles && groundIds[ix, iy + 1] != 10)
							{
								int downHeight = heights[ix, iy + 1];
								int diff = height - downHeight;
								if (diff > 0)
								{
									float cliffY = worldY * ts - height * ts / 4f + ts;
									if (ctx.IsWorldRectVisible(worldX * ts, cliffY, ts, diff * (ts / 4)))
										DrawCliffDown(worldX, worldY, diff, ts, cliffDownTex, height);
								}
							}
							// Gauche
							if (ix - 1 >= 0 && groundIds[ix - 1, iy] != 10)
							{
								int leftHeight = heights[ix - 1, iy];
								if (height > leftHeight)
								{
									float cliffY = worldY * ts - height * ts / 4f;
									if (ctx.IsWorldRectVisible(worldX * ts, cliffY, ts / 4f, ts))
										DrawCliffBorder(worldX, worldY, -1, 0, ts, cliffBorderTex, height);
								}
							}
							// Haut
							if (iy - 1 >= 0 && groundIds[ix, iy - 1] != 10)
							{
								int upHeight = heights[ix, iy - 1];
								if (height > upHeight)
								{
									float cliffY = worldY * ts - height * ts / 4f;
									if (ctx.IsWorldRectVisible(worldX * ts, cliffY, ts / 4f, ts))
										DrawCliffBorder(worldX, worldY, 0, -1, ts, cliffBorderTex, height);
								}
							}
							// Droite
							if (ix + 1 < width && groundIds[ix + 1, iy] != 10)
							{
								int rightHeight = heights[ix + 1, iy];
								if (height > rightHeight)
								{
									float cliffY = worldY * ts - height * ts / 4f;
									if (ctx.IsWorldRectVisible(worldX * ts + ts - ts / 4f, cliffY, ts / 4f, ts))
										DrawCliffBorder(worldX, worldY, 1, 0, ts, cliffBorderTex, height);
								}
							}
						}
					}
				}

				// Sécurité : au cas où la dernière tuile dessinée était de l'eau,
				// on referme le mode shader avant de continuer le rendu (bordures,
				// décorations, entités...), sinon tout le reste serait dessiné avec
				// le fragment shader de l'eau.
				EnsureWaterShaderInactive();
			}

			/// <summary>Bordures de biomes, dessinées après toutes les tuiles de sol.</summary>
			internal static void DrawBiomeBorders(DrawWorldContext ctx)
			{
				int ts = ctx.ts;
				float currentTime = ctx.currentTime;
				int startX = ctx.startX;
				int startY = ctx.startY;
				int[,] heights = ctx.heights;
				int[,] groundIds = ctx.groundIds;
				bool drawBiomeBorders = ctx.drawBiomeBorders;

				// =================================================================================
				// BORDURES DE BIOMES (dessinées après toutes les tuiles)
				// =================================================================================
				if (drawBiomeBorders)
				{
					foreach (var edge in _renderBiomeBorders)
					{
						int bx = edge.x;
						int by = edge.y;
						int wx = startX + bx;
						int wy = startY + by;
						int currentGround = groundIds[bx, by];
						Texture2D borderTex = GetBiomeBorderTexture(currentGround, wx, wy, currentTime);
						if (borderTex.Id == 0) continue;

						Rectangle source = new Rectangle(0, 0, borderTex.Width, borderTex.Height);
						Rectangle destination;
						Vector2 origin = Vector2.Zero;
						float rotation = 0f;
						switch (edge.direction)
						{
							case BiomeBorderDirection.Right:
								int rightDrawY = wy * ts - heights[bx + 1, by] * ts / 4;
								destination = new Rectangle((wx + 1) * ts, rightDrawY, ts, ts);
								break;
							case BiomeBorderDirection.Left:
								source = new Rectangle(borderTex.Width, 0, -borderTex.Width, borderTex.Height);
								int leftDrawY = wy * ts - heights[bx - 1, by] * ts / 4;
								destination = new Rectangle((wx - 1) * ts, leftDrawY, ts, ts);
								break;
							case BiomeBorderDirection.Bottom:
								int bottomDrawY = (wy + 1) * ts - heights[bx, by + 1] * ts / 4;
								destination = new Rectangle(wx * ts + ts / 2f, bottomDrawY + ts / 2f, ts, ts);
								origin = new Vector2(ts / 2f, ts / 2f);
								rotation = 90f;
								break;
							default:
								int topDrawY = (wy - 1) * ts - heights[bx, by - 1] * ts / 4;
								destination = new Rectangle(wx * ts + ts / 2f, topDrawY + ts / 2f, ts, ts);
								origin = new Vector2(ts / 2f, ts / 2f);
								rotation = -90f;
								break;
						}

						Raylib.DrawTexturePro(borderTex, source, destination, origin, rotation, Color.White);
					}
				}
			}
        }
    }
}
