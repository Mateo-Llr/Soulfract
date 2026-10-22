// BoatRendering.cs
// Bateaux : tuiles/radeaux (dessin direct) et objets posés dessus (RenderItems).
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
			/// <summary>Dessine les tuiles de radeau et leurs connexions.</summary>
			internal static void DrawBoatTiles(DrawWorldContext ctx)
			{
				int ts = ctx.ts;

				// =================================================================================
				// 2. BATEAUX (tuiles + voile) - Version unique et optimisée
				// =================================================================================
				foreach (var boat in World.GetAllBoats().Values)
				{
					if (boat == null) continue;
				
					if (_isPlayerIndoors)
					{
						int tileOverlay = GetBuildingIdAt((int)(boat.Position.X / ts), (int)(boat.Position.Y / ts));
						if (!_currentIndoorGroupIds.Contains(tileOverlay))
							continue;
					}
				
					// 2. Dessiner toutes les tuiles du bateau (radeau)
					foreach (var (relX, relY) in boat.Tiles.Keys)
					{
						Vector2 boatWorldPos = boat.GetTileWorldPos(relX, relY);
						int boatTileX = (int)(boatWorldPos.X / ts);
						int boatTileY = (int)(boatWorldPos.Y / ts);
						if (!Program.IsTileLit(boatTileX, boatTileY)) continue;
						int boatHeight = GetHeightAt(boatTileX, boatTileY);
						float boatYOffset = -boatHeight * ts / 4;
						float boatDrawX = boatWorldPos.X - ts/2f;
						float boatDrawY = boatWorldPos.Y - ts/2f + boatYOffset;
						if (!ctx.IsWorldRectVisible(boatDrawX, boatDrawY, ts, ts))
							continue;
					
						Texture2D boatTex = WorldTileRegistry.GetFirstTexture(500);
						if (boatTex.Id != 0)
						{
							Raylib.DrawTexturePro(boatTex,
								new Rectangle(0, 0, boatTex.Width, boatTex.Height),
								new Rectangle(boatDrawX, boatDrawY, ts, ts),
								Vector2.Zero, 0, Color.White);
						}
					
						// Connexions du radeau
						if (WorldTileRegistry.IsConnectable(500))
						{
							if (boat.Tiles.ContainsKey((relX - 1, relY)) && Program.IsTileLit(boatTileX - 1, boatTileY))
							{
								Texture2D connTex = WorldTileRegistry.GetConnectionTexture(500, "left");
								if (connTex.Id != 0)
								{
									Raylib.DrawTexturePro(connTex,
										new Rectangle(0, 0, connTex.Width, connTex.Height),
										new Rectangle(boatDrawX, boatDrawY, ts, ts),
										Vector2.Zero, 0, Color.White);
								}
							}
							if (boat.Tiles.ContainsKey((relX + 1, relY)) && Program.IsTileLit(boatTileX + 1, boatTileY))
							{
								Texture2D connTex = WorldTileRegistry.GetConnectionTexture(500, "right");
								if (connTex.Id != 0)
								{
									Raylib.DrawTexturePro(connTex,
										new Rectangle(0, 0, connTex.Width, connTex.Height),
										new Rectangle(boatDrawX, boatDrawY, ts, ts),
										Vector2.Zero, 0, Color.White);
								}
							}
							if (boat.Tiles.ContainsKey((relX, relY - 1)) && Program.IsTileLit(boatTileX, boatTileY - 1))
							{
								Texture2D connTex = WorldTileRegistry.GetConnectionTexture(500, "up");
								if (connTex.Id != 0)
								{
									Raylib.DrawTexturePro(connTex,
										new Rectangle(0, 0, connTex.Width, connTex.Height),
										new Rectangle(boatDrawX, boatDrawY, ts, ts),
										Vector2.Zero, 0, Color.White);
								}
							}
							if (boat.Tiles.ContainsKey((relX, relY + 1)) && Program.IsTileLit(boatTileX, boatTileY + 1))
							{
								Texture2D connTex = WorldTileRegistry.GetConnectionTexture(500, "down");
								if (connTex.Id != 0)
								{
									Raylib.DrawTexturePro(connTex,
										new Rectangle(0, 0, connTex.Width, connTex.Height),
										new Rectangle(boatDrawX, boatDrawY, ts, ts),
										Vector2.Zero, 0, Color.White);
								}
							}
						}
					}
				}
			}

			/// <summary>Ajoute aux RenderItems les objets posés sur les bateaux (coffres, porte-armures...).</summary>
			internal static void AddBoatObjects(DrawWorldContext ctx)
			{
				int ts = ctx.ts;
				float currentTime = ctx.currentTime;
				var renderItems = _renderItemsPool;

				// =================================================================================
				// 6. OBJETS POSÉS SUR LES BATEAUX (coffres, porte-armures, etc.)
				// =================================================================================
				foreach (var boat in World.GetAllBoats().Values)
				{
					foreach (var (relX, relY) in boat.Tiles.Keys)
					{
						int boatObjectId = boat.GetObjectIdAt(relX, relY);
						if (boatObjectId == 0) continue;
					
						var boatTileData = WorldTileRegistry.GetTile(boatObjectId);
						if (boatTileData == null) continue;
					
						if (_isPlayerIndoors)
						{
							int tileOverlay = GetBuildingIdAt(
								(int)(boat.GetTileWorldPos(relX, relY).X / ts),
								(int)(boat.GetTileWorldPos(relX, relY).Y / ts)
							);
							if (!_currentIndoorGroupIds.Contains(tileOverlay))
								continue;
						}
					
						Vector2 tileWorldPos = boat.GetTileWorldPos(relX, relY);
						int tileX = (int)(tileWorldPos.X / ts);
						int tileY = (int)(tileWorldPos.Y / ts);
						if (!Program.IsTileLit(tileX, tileY)) continue;
						int height = GetHeightAt(tileX, tileY);
						float yOffset = -height * ts / 4;
					
						float drawX = tileWorldPos.X - ts/2f;
						float drawY = tileWorldPos.Y - ts/2f + yOffset;
						int drawW = ts, drawH = ts;
						int offX = 0, offY = 0;
					
						if (boatTileData.Size != null)
						{
							drawW = ts * boatTileData.Size.Width;
							drawH = ts * boatTileData.Size.Height;
							if (boatTileData.DrawOffset != null)
							{
								offX = boatTileData.DrawOffset.X;
								offY = boatTileData.DrawOffset.Y;
							}
							drawY = drawY - drawH + ts;
						}
						if (!ctx.IsWorldRectVisible(drawX + offX, drawY + offY, drawW, drawH))
							continue;
					
						float sortY = tileWorldPos.Y + yOffset;
					
						var capturedBoat = boat;
						var capturedRelX = relX;
						var capturedRelY = relY;
						var capturedObjectId = boatObjectId;
						var capturedTileData = boatTileData;
						var capturedDrawX = drawX;
						var capturedDrawY = drawY;
						var capturedDrawW = drawW;
						var capturedDrawH = drawH;
						var capturedOffX = offX;
						var capturedOffY = offY;
					
						renderItems.Add(new RenderItem
						{
							Y = sortY,
							SortLayer = capturedTileData.OnFloor ? -1 : 0,
							DrawAction = () =>
							{
								Texture2D objTex = GetTileTexture(
									(int)(capturedBoat.GetTileWorldPos(capturedRelX, capturedRelY).X / ts),
									(int)(capturedBoat.GetTileWorldPos(capturedRelX, capturedRelY).Y / ts),
									capturedObjectId, currentTime);
							
								if (objTex.Id != 0)
								{
									Raylib.DrawTexturePro(objTex,
										new Rectangle(0, 0, objTex.Width, objTex.Height),
										new Rectangle(capturedDrawX + capturedOffX, capturedDrawY + capturedOffY, 
													 capturedDrawW, capturedDrawH),
										Vector2.Zero, 0, Color.White);
								}
							
								// Connexions pour les objets connectables sur le bateau
								if (WorldTileRegistry.IsConnectable(capturedObjectId))
								{
									if (capturedBoat.Tiles.ContainsKey((capturedRelX - 1, capturedRelY)) && 
										capturedBoat.GetObjectIdAt(capturedRelX - 1, capturedRelY) == capturedObjectId)
									{
										Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "left");
										if (connTex.Id != 0)
											Raylib.DrawTexturePro(connTex,
												new Rectangle(0, 0, connTex.Width, connTex.Height),
												new Rectangle(capturedDrawX + capturedOffX, capturedDrawY + capturedOffY, 
															 capturedDrawW, capturedDrawH),
												Vector2.Zero, 0, Color.White);
									}
									if (capturedBoat.Tiles.ContainsKey((capturedRelX + 1, capturedRelY)) && 
										capturedBoat.GetObjectIdAt(capturedRelX + 1, capturedRelY) == capturedObjectId)
									{
										Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "right");
										if (connTex.Id != 0)
											Raylib.DrawTexturePro(connTex,
												new Rectangle(0, 0, connTex.Width, connTex.Height),
												new Rectangle(capturedDrawX + capturedOffX, capturedDrawY + capturedOffY, 
															 capturedDrawW, capturedDrawH),
												Vector2.Zero, 0, Color.White);
									}
									if (capturedBoat.Tiles.ContainsKey((capturedRelX, capturedRelY - 1)) && 
										capturedBoat.GetObjectIdAt(capturedRelX, capturedRelY - 1) == capturedObjectId)
									{
										Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "up");
										if (connTex.Id != 0)
											Raylib.DrawTexturePro(connTex,
												new Rectangle(0, 0, connTex.Width, connTex.Height),
												new Rectangle(capturedDrawX + capturedOffX, capturedDrawY + capturedOffY, 
															 capturedDrawW, capturedDrawH),
												Vector2.Zero, 0, Color.White);
									}
									if (capturedBoat.Tiles.ContainsKey((capturedRelX, capturedRelY + 1)) && 
										capturedBoat.GetObjectIdAt(capturedRelX, capturedRelY + 1) == capturedObjectId)
									{
										Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "down");
										if (connTex.Id != 0)
											Raylib.DrawTexturePro(connTex,
												new Rectangle(0, 0, connTex.Width, connTex.Height),
												new Rectangle(capturedDrawX + capturedOffX, capturedDrawY + capturedOffY, 
															 capturedDrawW, capturedDrawH),
												Vector2.Zero, 0, Color.White);
									}
								}
							}
						});
					}
				}
			}
        }
    }
}
