// RoofRendering.cs
// Overlays (toits, minerais...) affichés quand le joueur n'est pas à l'intérieur.
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
			/// <summary>Ajoute les overlays/toits aux RenderItems.</summary>
			internal static void AddRoofsAndOverlays(DrawWorldContext ctx)
			{
				Texture2D missingTex = ctx.missingTex;
				int ts = ctx.ts;
				float currentTime = ctx.currentTime;
				int startX = ctx.startX;
				int startY = ctx.startY;
				int width = ctx.width;
				int heightTiles = ctx.heightTiles;
				int[,] overlayIds = ctx.overlayIds;
				int[,] roofHeights = ctx.roofHeights;
				var renderItems = _renderItemsPool;

				// Overlays (toits) si joueur pas à l'intérieur
				for (int ix = 0; ix < width; ix++)
				{
					int worldX = startX + ix;
					for (int iy = 0; iy < heightTiles; iy++)
					{
						int worldY = startY + iy;
						int localIx = ix;   // COPIE LOCALE
						int localIy = iy;   // COPIE LOCALE
						int overlayId = overlayIds[localIx, localIy];
						if (overlayId == 0 || (_isPlayerIndoors && overlayId != 123)) continue;
						var overlayTileData = WorldTileRegistry.GetTile(overlayId);
						if (overlayTileData == null) continue;
						float wx = worldX * ts;
						float wy = worldY * ts;
						int roofHeightTiles = roofHeights[ix, iy];
						// Décalage vertical proportionnel à la hauteur DE CETTE TUILE (plafonnée au
						// faîtage) : c'est ce qui donne la perspective en pente au lieu d'un toit plat.
						// Les pans horizontaux restent à hauteur pleine (comme avant, positions
						// inchangées) ; les pans verticaux montent depuis les bords vers le milieu.
						float yOffset = -(roofHeightTiles) * ts;
						float adjustedWy = wy + yOffset;
						// Le tri en Y doit se baser sur la position AU SOL (non décalée), pas sur la
						// position de dessin déjà remontée par la hauteur : sinon une colonne haute
						// (empilement de roof_wall) se retrouve triée "avant" la rangée du dessous,
						// qui se dessine alors PAR-DESSUS et cache le bas de l'empilement. Le
						// décalage de hauteur ne doit affecter que le DESSIN, jamais l'ordre.
						//
						//  CORRECTIF : cette ligne ajoutait auparavant +100000f, ce qui plaçait le
						// toit systématiquement TOUT EN HAUT du tri de profondeur, quelle que soit sa
						// position réelle. Résultat : un arbre (ou tout autre sprite) situé plus bas
						// à l'écran — donc censé se dessiner PAR-DESSUS le toit, comme pour n'importe
						// quel autre objet (les arbres utilisent exactement la même formule de base,
						// `wy + ts`, voir plus haut dans DrawWorld) — se faisait quand même recouvrir
						// par le toit, qui l'emportait toujours artificiellement. Le toit doit donc
						// concourir avec la même valeur de base que tout le reste ; seuls de très
						// petits écarts (bien inférieurs à la hauteur d'une tuile) sont ajoutés
						// ci-dessous pour départager les COUCHES INTERNES d'un même toit entre elles
						// (pente verticale/horizontale, raccords, empilement de roof_wall), sans
						// jamais pouvoir l'emporter sur un objet d'une autre rangée.
						// Les murs utilisent le même départage horizontal basé sur ix. Le toit doit
						// rester juste au-dessus du mur de la même tuile, sinon le mur le recouvre
						// dès que la tuile n'est plus à l'extrême gauche de la fenêtre.
						float sortY = overlayId == 123 ? wy + ts - 0.01f : wy + ts + (ix + 1) * 0.001f;

						bool hasSlopeInfo = TryGetRoofTileInfo(worldX, worldY, out RoofTileInfo slopeInfo);
						// Les pans "horizontaux" (plats, à hauteur classique) sont dessinés en
						// premier (en arrière-plan) ; les pans "verticaux" (surélevés par
						// l'empilement de roof_wall) passent par-dessus. Écart minime : ça ne
						// concerne que l'ordre ENTRE pans du même toit, jamais vis-à-vis d'un objet
						// extérieur à cette rangée.
						bool isVerticalZone = hasSlopeInfo && !slopeInfo.IsHorizontalZone;
						if (isVerticalZone)
							sortY += 0.05f;

						int overlayDrawWidth = overlayTileData.Size != null ? ts * overlayTileData.Size.Width : ts;
						int overlayDrawHeight = overlayTileData.Size != null ? ts * overlayTileData.Size.Height : ts;
						float overlayDrawX = wx + (overlayTileData.DrawOffset?.X ?? 0);
						float overlayDrawY = (overlayId == 123 ? wy : adjustedWy) - overlayDrawHeight + ts;
						float overlayTop = overlayDrawY;
						if (hasSlopeInfo && slopeInfo.IsColumnAnchor && slopeInfo.WallStackCount > 0)
						{
							float stackTop = wy - (roofHeightTiles - slopeInfo.WallStackCount) * ts
								- (slopeInfo.WallStackCount - 1) * ts;
							overlayTop = MathF.Min(overlayTop, stackTop);
						}
						if (!ctx.IsWorldRectVisible(overlayDrawX, overlayTop, overlayDrawWidth,
							MathF.Max(overlayDrawY + overlayDrawHeight, wy + ts) - overlayTop))
							continue;

						int capturedX = worldX, capturedY = worldY;
						float capturedWx = wx, capturedWy = overlayId == 123 ? wy : adjustedWy;
						float capturedWyBase = wy;
						int capturedSortLayer = overlayTileData.OnFloor ? -1 : 0;
						int capturedHeight = roofHeightTiles;
						int capturedOverlayId = overlayId;
						bool capturedIsColumnAnchor = hasSlopeInfo && slopeInfo.IsColumnAnchor;
						int capturedWallStackCount = hasSlopeInfo ? slopeInfo.WallStackCount : 0;
						RoofTexKind capturedTexKind = hasSlopeInfo ? slopeInfo.TexKind : RoofTexKind.Default;

						// L'empilement de roof_wall part dans SON PROPRE élément de rendu, avec une
						// priorité de tri légèrement supérieure (voir note plus haut : l'écart reste
						// minime, il ne sert qu'à départager les couches internes du même toit) :
						// sinon, sur la rangée du bas, une tuile voisine (sol, mur, autre bâtiment)
						// au sortY quasi identique pouvait se dessiner juste après et cacher le bas
						// de la pile.
						if (capturedIsColumnAnchor && capturedWallStackCount > 0)
						{
							renderItems.Add(new RenderItem
							{
								SortLayer = capturedSortLayer,
								// Doit rester au-dessus du pan de toit et de ses raccords (ci-dessous),
								// mais seulement de quelques centièmes : voir la note sur `sortY` plus haut.
								Y = sortY + 0.02f,
								DrawAction = () =>
								{
									Texture2D wallTex = GetRoofWallTexture(missingTex);
									int baseHeightTiles = capturedHeight - capturedWallStackCount; // hauteur classique (sans pente)
									float baseY = capturedWyBase - baseHeightTiles * ts;
									for (int lvl = 1; lvl <= capturedWallStackCount; lvl++)
									{
										// Les roof_wall doivent commencer juste sous la base du toit, pas au-dessus.
float stackY = baseY - (lvl - 1) * ts;
										Raylib.DrawTexturePro(wallTex, new Rectangle(0, 0, wallTex.Width, wallTex.Height),
											new Rectangle((int)capturedWx, (int)stackY, ts, ts), Vector2.Zero, 0, Color.White);
									}
								}
							});
						}

						renderItems.Add(new RenderItem
						{
							Y = sortY,
							SortLayer = capturedSortLayer,
							DrawAction = () =>
							{
								Texture2D tex = capturedTexKind switch
								{
									RoofTexKind.Up => GetRoofUpTexture(missingTex),
									RoofTexKind.Down => GetRoofDownTexture(missingTex),
									_ => GetTileTexture(capturedX, capturedY, capturedOverlayId, currentTime),
								};
								if (tex.Id == 0) tex = missingTex;
								int drawW = ts, drawH = ts;
								int drawX = (int)capturedWx, drawY = (int)capturedWy;
								var tileDef = WorldTileRegistry.GetTile(capturedOverlayId);
								if (tileDef?.Size != null)
								{
									drawW = ts * tileDef.Size.Width;
									drawH = ts * tileDef.Size.Height;
									if (tileDef.DrawOffset != null)
									{
										drawX += tileDef.DrawOffset.X;
										drawY -= tileDef.DrawOffset.Y;
									}
									drawY = (int)(capturedWy - drawH + ts);
								}
								if (tex.Id != 0)
								{
									if (capturedOverlayId == 123)
									{
										Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
											new Rectangle(drawX + drawW / 2f, drawY + drawH / 2f, drawW, drawH),
											new Vector2(drawW / 2f, drawH / 2f), GetRailwayRotation(capturedX, capturedY), Color.White);
									}
									else
									{
										Raylib.DrawTexturePro(tex, new Rectangle(0,0,tex.Width,tex.Height),
											new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
									}
								}
							}
						});
							renderItems.Add(new RenderItem
						{
							SortLayer = capturedSortLayer,
							// Raccords dessinés par-dessus la texture de base du pan (voir note sur
						// `sortY` plus haut), mais toujours en dessous de l'empilement de roof_wall.
						Y = sortY + 0.01f,
							DrawAction = () =>
							{
								int drawW = ts, drawH = ts;
								int drawX = (int)capturedWx, drawY = (int)capturedWy;
								var tileDef = WorldTileRegistry.GetTile(capturedOverlayId);
								if (tileDef?.Size != null)
								{
									drawW = ts * tileDef.Size.Width;
									drawH = ts * tileDef.Size.Height;
									if (tileDef.DrawOffset != null)
									{
										drawX += tileDef.DrawOffset.X;
										drawY -= tileDef.DrawOffset.Y;
									}
									drawY = (int)(capturedWy - drawH + ts);
								}
								Texture2D GetConnectionTextureForKind(string direction)
								{
									return capturedTexKind switch
									{
										RoofTexKind.Up => GetRoofVariantConnectionTexture(missingTex, "roof_up", direction),
										RoofTexKind.Down => GetRoofVariantConnectionTexture(missingTex, "roof_down", direction),
										_ => WorldTileRegistry.GetConnectionTexture(capturedOverlayId, direction),
									};
								}
								if (Program.IsTileLit(capturedX - 1, capturedY) && GetOverlayAt(capturedX - 1, capturedY) == capturedOverlayId)
								{
									Texture2D connTex = GetConnectionTextureForKind("left");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
								}
								if (Program.IsTileLit(capturedX + 1, capturedY) && GetOverlayAt(capturedX + 1, capturedY) == capturedOverlayId)
								{
									Texture2D connTex = GetConnectionTextureForKind("right");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
								}
								if (Program.IsTileLit(capturedX, capturedY - 1) && GetOverlayAt(capturedX, capturedY - 1) == capturedOverlayId)
								{
									Texture2D connTex = GetConnectionTextureForKind("up");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
								}
								if (Program.IsTileLit(capturedX, capturedY + 1) && GetOverlayAt(capturedX, capturedY + 1) == capturedOverlayId)
								{
									Texture2D connTex = GetConnectionTextureForKind("down");
									if (connTex.Id != 0)
										Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
											new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
								}
							}
						});
					}
				}
			}
        }
    }
}
