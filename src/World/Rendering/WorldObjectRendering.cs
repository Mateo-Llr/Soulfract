// WorldObjectRendering.cs
// Objets du monde (arbres, rochers, maisons, ruches, ampoules, coffres...) et icônes de portes.
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
			/// <summary>Icônes des propriétaires de maison sur les portes.</summary>
			internal static void AddHouseDoorIcons(DrawWorldContext ctx)
			{
				List<Entity> entities = ctx.entities;
				Vector2 topLeft = ctx.topLeft;
				Vector2 bottomRight = ctx.bottomRight;
				var renderItems = _renderItemsPool;

				// =================================================================================
				// 4. ICÔNES DES PROPRIÉTAIRES DE MAISON SUR LES PORTES
				// =================================================================================
				AddHouseResidentDoorIcons(renderItems, entities,
					new Rectangle(topLeft.X - 160f, topLeft.Y - 160f,
						(bottomRight.X - topLeft.X) + 320f, (bottomRight.Y - topLeft.Y) + 320f));
			}

			/// <summary>Passe sur la grille visible : cercle rituel (dessin direct) et ajout de chaque objet du monde aux RenderItems.</summary>
			internal static void AddWorldObjects(DrawWorldContext ctx)
			{
				Texture2D missingTex = ctx.missingTex;
				HashSet<string> destroyed = ctx.destroyed;
				Dictionary<string, WorldObjectHP> objectHP = ctx.objectHP;
				Dictionary<int, Texture2D> tileTextures = ctx.tileTextures;
				Vector2 mouseWorldPos = ctx.mouseWorldPos;
				int ts = ctx.ts;
				float currentTime = ctx.currentTime;
				int startX = ctx.startX;
				int startY = ctx.startY;
				int endY = ctx.endY;
				int width = ctx.width;
				int heightTiles = ctx.heightTiles;
				int[,] objectIds = ctx.objectIds;
				int[,] overlayIds = ctx.overlayIds;
				int[,] buildingIds = ctx.buildingIds;
				int[,] wallCoveringIds = ctx.wallCoveringIds;
				var renderItems = _renderItemsPool;

				for (int ix = 0; ix < width; ix++)
				{
					int worldX = startX + ix;
					for (int iy = 0; iy < heightTiles; iy++)
					{
						int worldY = startY + iy;
						int objectId = objectIds[ix, iy];
						if (objectId == 0) continue;

						// Cercle rituel (traitement spécial)
						if (objectId == 4000)
						{
							float ritualWx = worldX * ts;
							float ritualWy = worldY * ts;
							var ritualTileData = WorldTileRegistry.GetTile(4000);
							if (ritualTileData != null)
							{
								int drawW = ts, drawH = ts;
								int drawX = (int)ritualWx, drawY = (int)ritualWy;
								if (ritualTileData.Size != null)
								{
									drawW = ts * ritualTileData.Size.Width;
									drawH = ts * ritualTileData.Size.Height;
									if (ritualTileData.DrawOffset != null)
									{
										drawX += ritualTileData.DrawOffset.X;
										drawY -= ritualTileData.DrawOffset.Y;
									}
									drawY = (int)(ritualWy - drawH + ts);
								}
								if (!ctx.IsWorldRectVisible(drawX, drawY, drawW, drawH))
									continue;
								Texture2D tex = GetTileTexture(worldX, worldY, 4000, currentTime);
								if (tex.Id != 0)
								{
									Raylib.DrawTexturePro(tex,
										new Rectangle(0, 0, tex.Width, tex.Height),
										new Rectangle(drawX, drawY, drawW, drawH),
										Vector2.Zero, 0, Color.White);
								}
							}
							continue;
						}

						var tileData = WorldTileRegistry.GetTile(objectId);
						if (tileData == null) continue;

						float wx = worldX * ts, wy = worldY * ts;

						int objectDrawWidth = tileData.Size != null ? ts * tileData.Size.Width : ts;
						int objectDrawHeight = tileData.Size != null ? ts * tileData.Size.Height : ts;
						float objectDrawX = wx + (tileData.DrawOffset?.X ?? 0);
						float objectDrawY = wy - objectDrawHeight + ts;
						float hpBarTop = wy - ts * 2 - 10f;
						if (objectId == 124)
						{
							objectDrawX -= ts;
							objectDrawWidth += ts * 2;
							objectDrawY -= ts;
							objectDrawHeight += ts * 2;
						}
						if (!ctx.IsWorldRectVisible(objectDrawX, MathF.Min(objectDrawY, hpBarTop),
							objectDrawWidth, MathF.Max(objectDrawY + objectDrawHeight, wy + 8f) - MathF.Min(objectDrawY, hpBarTop)))
							continue;

						string key = GetTileKeyString(worldX, worldY);
						if (destroyed.Contains(key)) continue;

						if (_isPlayerIndoors)
						{
							int tileBld = buildingIds[ix, iy];
							if (!_currentIndoorGroupIds.Contains(tileBld)) continue;
						}

						objectHP.TryGetValue(key, out var hp);

						//  On utilise l'index LOCAL "ix" (petit, borné à la largeur de la fenêtre visible)
						// plutôt que "worldX" (coordonnée monde potentiellement grande) pour ce micro-décalage
						// de départage : ajouté à un "wy" de grande magnitude, un décalage basé sur worldX
						// pouvait être totalement "avalé" par l'imprécision du float (perte de précision),
						// ce qui faisait redevenir deux sprites voisins strictement égaux en Y. Le départage
						// final et déterministe reste de toute façon assuré par SortIndex au moment du tri.
						float sortY = wy + ts + ix * 0.001f;

						//  CRITIQUE : Créer des COPIES LOCALES pour la lambda 
						int capturedX = worldX;
						int capturedY = worldY;
						float capturedWx = wx;
						float capturedWy = wy;
						var capturedTileData = tileData;
						var capturedHp = hp;
						int capturedObjectId = objectId;
						int capturedWallCovering = wallCoveringIds[ix, iy];
						int capturedIx = ix;
						int capturedIy = iy;

						bool isArmorStand = capturedTileData.IsArmorStand == true;
						ArmorStandData? standData = null;
						if (isArmorStand)
						{
							var chunk = GetChunkAt(capturedX, capturedY);
							standData = chunk?.GetArmorStandAt(capturedX, capturedY);
							if (standData == null) standData = new ArmorStandData();
						}

						bool isBannerHolder = (capturedObjectId == 7000);
						BannerHolderData? capturedBannerData = null;
						if (isBannerHolder)
						{
							var bChunk = GetChunkAt(capturedX, capturedY);
							capturedBannerData = bChunk?.GetBannerHolderAt(capturedX, capturedY);
						}

						renderItems.Add(new RenderItem
						{
							Y = sortY,
							SortLayer = capturedTileData.OnFloor ? -1 : 0,
							DrawAction = () =>
							{
								if (capturedObjectId == 123)
								{
									Texture2D railTex = GetTileTexture(capturedX, capturedY, capturedObjectId, currentTime);
									if (railTex.Id != 0)
									{
										Raylib.DrawTexturePro(railTex, new Rectangle(0, 0, railTex.Width, railTex.Height),
											new Rectangle(capturedWx + ts / 2f, capturedWy + ts / 2f, ts, ts),
											new Vector2(ts / 2f, ts / 2f), GetRailwayRotation(capturedX, capturedY), Color.White);
									}
								}
								else if (capturedObjectId == 124)
								{
									if (!Program.IsWagonMountedAt(capturedX, capturedY))
										Program.DrawWagon(new Vector2(capturedWx + ts / 2f, capturedWy + ts / 2f)
											+ Program.GetWagonDrawOffset(capturedX, capturedY), ts,
											Program.IsWagonDrawnVertical(capturedX, capturedY));
								}
								else if (isArmorStand)
								{
									Vector2 standPos = new Vector2(capturedWx + ts/2f, capturedWy + ts/2f);
									var tempEquipment = EntityRenderer.GetCachedEquipmentForArmorStand(standData);
									Texture2D emptyTex = new Texture2D();
									string poseToUse = standData.CurrentPose ?? "idle";
									if (SpeciesData.Skeletons.TryGetValue("armorstand", out var skeleton))
									{
										var firstPart = skeleton.FirstOrDefault();
										if (firstPart != null && !firstPart.Animations.ContainsKey(poseToUse))
											poseToUse = "idle";
									}
									EntityRenderer.DrawEntity(
										"armorstand", poseToUse, 0, 0f, 1f, standPos, Color.White,
										emptyTex, emptyTex, Color.Black, SpeciesData.Skeletons,
										Program.EyesTexture, Program.MouthTexture,
										customScale: 1.0f, equipment: tempEquipment,
										isCarrying: false, inWater: false, attackSwingProgress: 0f,
										heldItemTexture: default, headAngle: 0f);
									if (capturedHp != null && capturedHp.Current < capturedHp.Max)
									{
										float ratio = Math.Clamp((float)capturedHp.Current / Math.Max(1, capturedHp.Max), 0f, 1f);
										float barWidth = ts;
										float barHeight = 7f;
										float barX = capturedWx;
										float barY = capturedWy - ts * 2 - 8;
										Rectangle barRect = new Rectangle(barX, barY, barWidth, barHeight);
										Rectangle fillRect = new Rectangle(barX + 1.5f, barY + 1.5f, Math.Max(0f, (barWidth - 3f) * ratio), barHeight - 3f);
										Raylib.DrawRectangleRounded(new Rectangle(barX - 2f, barY - 2f, barWidth + 4f, barHeight + 4f), 0.45f, 8, new Color(0, 0, 0, 105));
										Raylib.DrawRectangleRounded(barRect, 0.45f, 8, new Color(32, 20, 24, 225));
										if (fillRect.Width > 0f)
										{
											Color fillColor = ratio < 0.3f
												? new Color(225, 65, 58, 255)
												: ratio < 0.6f
													? new Color(238, 165, 58, 255)
													: new Color(90, 210, 105, 255);
											Raylib.DrawRectangleRounded(fillRect, 0.35f, 6, fillColor);
											Raylib.DrawRectangleRounded(
												new Rectangle(fillRect.X + 1f, fillRect.Y + 0.5f, Math.Max(0f, fillRect.Width - 2f), 1.5f),
												0.5f, 6, new Color(255, 255, 255, 85));
										}
										Raylib.DrawRectangleRoundedLines(barRect, 0.45f, 8, 1, new Color(255, 220, 220, 150));
									}
								}
								else if (isBannerHolder) // ── PORTE-BANNIÈRE ──────────────────────────────
								{
									Texture2D tex = GetTileTexture(capturedX, capturedY, capturedObjectId, currentTime);
									if (tex.Id == 0) tex = tileTextures.GetValueOrDefault(capturedObjectId);
									if (tex.Id == 0 && missingTex.Id != 0) tex = missingTex;

									int drawW = ts, drawH = ts;
									int drawX = (int)capturedWx, drawY = (int)capturedWy;
									if (capturedTileData.Size != null)
									{
										drawW = ts * capturedTileData.Size.Width;
										drawH = ts * capturedTileData.Size.Height;
										if (capturedTileData.DrawOffset != null)
										{
											drawX += capturedTileData.DrawOffset.X;
											drawY -= capturedTileData.DrawOffset.Y;
										}
										drawY = (int)(capturedWy - drawH + ts);
									}

									bool isHovered = mouseWorldPos.X >= drawX && mouseWorldPos.X <= drawX + drawW &&
													   mouseWorldPos.Y >= drawY && mouseWorldPos.Y <= drawY + drawH;
									if (tex.Id != 0)
										Raylib.DrawTexturePro(tex,
											new Rectangle(0, 0, tex.Width, tex.Height),
											new Rectangle(drawX, drawY, drawW, drawH),
											Vector2.Zero, 0, Color.White);

									if (capturedBannerData != null && capturedBannerData.HasBanner)
									{
										if (_bannerShapeTextures.TryGetValue(capturedBannerData.ShapeName, out var shapeTex) && shapeTex.Id != 0)
											Raylib.DrawTexturePro(shapeTex,
												new Rectangle(0, 0, shapeTex.Width, shapeTex.Height),
												new Rectangle(drawX, drawY, drawW, drawH),
												Vector2.Zero, 0, capturedBannerData.BackgroundColor);

										if (_bannerPatternTextures.TryGetValue(capturedBannerData.PatternName, out var patTex) && patTex.Id != 0)
											Raylib.DrawTexturePro(patTex,
												new Rectangle(0, 0, patTex.Width, patTex.Height),
												new Rectangle(drawX, drawY, drawW, drawH),
												Vector2.Zero, 0, capturedBannerData.PatternColor);
									}

									if (isHovered)
									{
										string hint = (capturedBannerData?.HasBanner == true)
											? "[E] Décrocher la bannière"
											: "[E] Accrocher la bannière";
										Raylib.DrawText(hint, drawX, drawY - 14, 10, new Color(255, 255, 200, 220));
									}
								}
								else
								{
									Color tint = Color.White;
									if (capturedHp != null && capturedHp.DamageFlash > 0f)
										tint = new Color(255,80,80,255);

									int drawW = ts, drawH = ts;
									int drawX = (int)capturedWx, drawY = (int)capturedWy;
									if (capturedTileData.Size != null)
									{
										drawW = ts * capturedTileData.Size.Width;
										drawH = ts * capturedTileData.Size.Height;
										if (capturedTileData.DrawOffset != null)
										{
											drawX += capturedTileData.DrawOffset.X;
											drawY -= capturedTileData.DrawOffset.Y;
										}
										drawY = (int)(capturedWy - drawH + ts);
									}

									// Récupération de l'overlay une fois pour toute la lambda
									int currentOverlay = overlayIds[capturedIx, capturedIy];

									bool isHovered = mouseWorldPos.X >= drawX && mouseWorldPos.X <= drawX + drawW &&
													 mouseWorldPos.Y >= drawY && mouseWorldPos.Y <= drawY + drawH;
									Texture2D tex = GetTileTexture(capturedX, capturedY, capturedObjectId, currentTime);
									if (tex.Id == 0) tex = tileTextures.GetValueOrDefault(capturedObjectId);
									if (tex.Id == 0 && missingTex.Id != 0) tex = missingTex;

									// ========== AMPOULE ÉLECTRIQUE ==========
									if (capturedObjectId == 408)
									{
										bool isLit = false;
										if (Ampoules.TryGetValue((capturedX, capturedY), out var ampoule))
										{
											isLit = ampoule.IsOn;
										}

										Color bulbTint = isLit ? Color.White : new Color(80, 80, 80, 255);

										if (tex.Id != 0)
										{
											Raylib.DrawTexturePro(tex,
												new Rectangle(0, 0, tex.Width, tex.Height),
												new Rectangle(drawX, drawY, drawW, drawH),
												Vector2.Zero, 0, bulbTint);
										}

										if (isLit)
										{
											Raylib.BeginBlendMode(BlendMode.Additive);
											float pulse = 0.7f + 0.3f * MathF.Sin((float)Raylib.GetTime() * 5f);
											int glowRadius = (int)(drawW * 1.5f * pulse);
											Raylib.DrawCircleGradient((int)(drawX + drawW/2), (int)(drawY + drawH/2), glowRadius,
												new Color(255, 220, 150, 80),
												new Color(255, 220, 150, 0));
											Raylib.EndBlendMode();
										}
									}
									else
									{
										// Dessin normal pour les autres objets
										bool isCollectable = capturedTileData.Collectable;
										if (capturedTileData.IsCrop)
										{
											var cropData = World.GetCropDataAt(capturedX, capturedY);
											if (cropData != null)
											{
												float elapsed = cropData.AccumulatedGrowthTime;
												if (elapsed >= capturedTileData.GrowthTime)
													isCollectable = true;
											}
										}

										bool drawCollectableOutline = isHovered && tex.Id != 0 && isCollectable;

										bool isWall = IsWallToHide(capturedObjectId);
															bool hasVisibleInteriorTile = HasVisibleInteriorTile(capturedX, capturedY);
										int originalDrawH = drawH;
										int originalDrawY = drawY;
										if (isWall && _isPlayerIndoors && capturedTileData.Size != null)
										{
											int tileAboveY = capturedY - 1;
											if (tileAboveY >= startY && tileAboveY <= endY)
											{
												int aboveOverlay = GetOverlayAt(capturedX, tileAboveY);
												if (aboveOverlay == 82) drawH = 0;
											}
										}

															if (!hasVisibleInteriorTile)
																drawH = 0;

															if (drawH > 0)
									{
										//  Balancement dans le vent (flore uniquement) : on fait pivoter le(s)
										// sprite(s) de la tuile autour du bas de leur hitbox, pas autour du centre.
										// La hitbox est positionnée au sol, donc le pivot doit rester centré
										// horizontalement et aligné sur le bord inférieur de la case.
										Vector2 windOrigin = Vector2.Zero;
										float windAngle = 0f;
										Rectangle windDestRect = new Rectangle(drawX, drawY, drawW, drawH);
										bool isTree = IsLeafBearingTree(capturedObjectId);
										if (capturedTileData.SwaysInWind || isTree)
										{
											if (capturedTileData.SwaysInWind)
											{
												int tileWidth = capturedTileData.Size?.Width ?? 1;
												int tileHeight = capturedTileData.Size?.Height ?? 1;
												windAngle = GetWindSwayAngle(capturedX, capturedY, currentTime, tileWidth, tileHeight);
											}
											windOrigin = new Vector2(drawW / 2f, drawH);
											windDestRect = new Rectangle(drawX + windOrigin.X, drawY + windOrigin.Y, drawW, drawH);
										}
										if (isTree)
											windAngle += GetTreeHitSwayAngle(capturedX, capturedY, currentTime);

										if (IsMatureTree(capturedObjectId))
										{
											string treeTexturePrefix = GetMatureTreeTexturePrefix(capturedObjectId);
											var treeLayerNames = GetTreeLayerNames(treeTexturePrefix);
											Texture2D trunkTex = WorldTileRegistry.GetTextureByName(treeLayerNames.trunk);
											Texture2D bushTex = WorldTileRegistry.GetTextureByName(treeLayerNames.bush);
											Color treeTint = GetTreeLeafTintForObjectId(capturedObjectId, capturedX, capturedY);

											if (trunkTex.Id != 0)
											{
												Raylib.DrawTexturePro(trunkTex,
													new Rectangle(0, 0, trunkTex.Width, trunkTex.Height),
													windDestRect,
													windOrigin, windAngle, Color.White);
											}
											else if (tex.Id != 0)
											{
												Raylib.DrawTexturePro(tex,
													new Rectangle(0, 0, tex.Width, tex.Height),
													windDestRect, windOrigin, windAngle, Color.White);
											}

											if (bushTex.Id != 0)
											{
												Raylib.DrawTexturePro(bushTex,
													new Rectangle(0, 0, bushTex.Width, bushTex.Height),
													windDestRect,
													windOrigin, windAngle, treeTint);
											}
										}
										else if (IsTreeStump(capturedObjectId))
										{
											if (tex.Id != 0)
											{
												Raylib.DrawTexturePro(tex,
													new Rectangle(0, 0, tex.Width, tex.Height),
													new Rectangle(drawX, drawY, drawW, drawH),
													Vector2.Zero, 0, tint);
											}

											if (TryGetTreeFallVisual(capturedX, capturedY, out string treeTexturePrefix, out int fallenTreeId, out float fallAngle, out float fallAlpha))
											{
												Texture2D cutTex = WorldTileRegistry.GetTextureByName($"{treeTexturePrefix}_cut");
												if (cutTex.Id == 0)
													cutTex = GetTileTexture(capturedX, capturedY, fallenTreeId, currentTime);
												Texture2D leavesTex = WorldTileRegistry.GetTextureByName($"{treeTexturePrefix}_bush");
												Vector2 fallOrigin = new(drawW / 2f, drawH - TREE_JOINT_HEIGHT);
												Rectangle fallDest = new(drawX + fallOrigin.X, drawY + fallOrigin.Y, drawW, drawH);
												Color trunkTint = new((byte)255, (byte)255, (byte)255, (byte)(255 * fallAlpha));

												if (cutTex.Id != 0)
												{
													Raylib.DrawTexturePro(cutTex,
														new Rectangle(0, 0, cutTex.Width, cutTex.Height),
														fallDest, fallOrigin, fallAngle, trunkTint);
												}

												if (leavesTex.Id != 0)
												{
													Color leavesTint = GetTreeLeafTintForObjectId(fallenTreeId, capturedX, capturedY);
													leavesTint = new Color(leavesTint.R, leavesTint.G, leavesTint.B, (byte)(leavesTint.A * fallAlpha));
													Raylib.DrawTexturePro(leavesTex,
														new Rectangle(0, 0, leavesTex.Width, leavesTex.Height),
														fallDest, fallOrigin, fallAngle, leavesTint);
												}
											}
										}
										else if (capturedTileData.IsSapling &&
											WorldTileRegistry.SaplingTrunkStageTextures.TryGetValue(capturedObjectId, out var saplingTrunkStages) &&
											WorldTileRegistry.SaplingLeavesStageTextures.TryGetValue(capturedObjectId, out var saplingLeavesStages) &&
											saplingTrunkStages.Count > 0 && saplingLeavesStages.Count > 0)
										{
											//  Jeune arbre planté : mêmes deux couches (tronc + feuillage teinté)
											// que l'arbre adulte ci-dessus, mais avec une paire de textures par
											// stade de croissance actuel.
											int saplingStage = GetGrowthStageAt(capturedX, capturedY, capturedTileData);
											int saplingStageIdx = Math.Clamp(saplingStage - 1, 0, saplingTrunkStages.Count - 1);
											Texture2D saplingTrunkTex = saplingTrunkStages[saplingStageIdx];
											Texture2D saplingLeavesTex = saplingLeavesStages[saplingStageIdx];
											Color saplingTint = GetTreeLeafTintForObjectId(capturedObjectId, capturedX, capturedY);

											if (saplingTrunkTex.Id != 0)
											{
												Raylib.DrawTexturePro(saplingTrunkTex,
													new Rectangle(0, 0, saplingTrunkTex.Width, saplingTrunkTex.Height),
													windDestRect,
													windOrigin, windAngle, Color.White);
											}

											if (saplingLeavesTex.Id != 0)
											{
												Raylib.DrawTexturePro(saplingLeavesTex,
													new Rectangle(0, 0, saplingLeavesTex.Width, saplingLeavesTex.Height),
													windDestRect,
													windOrigin, windAngle, saplingTint);
											}
											else if (tex.Id != 0)
											{
												Raylib.DrawTexturePro(tex,
													new Rectangle(0, 0, tex.Width, tex.Height),
													windDestRect,
													windOrigin, windAngle, saplingTint);
											}
										}
										else if (tex.Id != 0)
										{
											Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
												windDestRect, windOrigin, windAngle, tint);
										}
										else if (missingTex.Id != 0)
										{
											Raylib.DrawTexturePro(missingTex, new Rectangle(0,0,missingTex.Width,missingTex.Height),
												windDestRect, windOrigin, windAngle, tint);
										}

										if (drawCollectableOutline)
											DrawCachedOutline(tex, windDestRect, windOrigin, windAngle, new Color(255, 255, 255, 180));
										}

										string wallTorchMeta = World.GetTileMeta(capturedX, capturedY, "wall_torch");
										if (int.TryParse(wallTorchMeta, out int wallTorchId))
										{
											var wallTorchData = WorldTileRegistry.GetTile(wallTorchId);
											if (wallTorchData?.WallMountable == true)
											{
												Texture2D wallTorchTexture = GetTileTexture(capturedX, capturedY, wallTorchId, currentTime);
												if (wallTorchTexture.Id == 0)
													wallTorchTexture = tileTextures.GetValueOrDefault(wallTorchId);
												if (wallTorchTexture.Id != 0)
												{
													int wallTorchWidth = ts * (wallTorchData.Size?.Width ?? 1);
													int wallTorchHeight = ts * (wallTorchData.Size?.Height ?? 1);
													float wallTorchX = capturedWx + (drawW - wallTorchWidth) / 2f;
													float wallTorchY = originalDrawY + (originalDrawH - wallTorchHeight) / 2f;
													Raylib.DrawTexturePro(wallTorchTexture,
														new Rectangle(0, 0, wallTorchTexture.Width, wallTorchTexture.Height),
														new Rectangle(wallTorchX, wallTorchY, wallTorchWidth, wallTorchHeight),
														Vector2.Zero, 0, Color.White);
												}
											}
										}

										if (capturedObjectId == 122)
											DrawGrandfatherClockHands(drawX, drawY, drawW, drawH, currentTime);

										if (capturedObjectId == 86)
										{
											string flowerId = World.GetTileMeta(capturedX, capturedY, "potted_flower");
											if (int.TryParse(flowerId, out int flowerItemId))
											{
												Rectangle potRect = new Rectangle(drawX, drawY, drawW, drawH);
												if (World.TryGetPottedFlowerDestination(flowerItemId, potRect, out int flowerTileId, out Rectangle flowerDestination))
												{
													Texture2D flowerTex = GetTileTexture(capturedX, capturedY, flowerTileId, currentTime);
													if (flowerTex.Id != 0)
														Raylib.DrawTexturePro(flowerTex,
															new Rectangle(0, 0, flowerTex.Width, flowerTex.Height),
															flowerDestination,
															Vector2.Zero, 0, Color.White);
												}
											}
										}

										drawH = originalDrawH;
										drawY = originalDrawY;

	if (hasVisibleInteriorTile && capturedWallCovering != 0)
																	{
																		var coveringTileData = WorldTileRegistry.GetTile(capturedWallCovering);
																		if (coveringTileData != null)
																		{
																			Texture2D coveringTex = GetTileTexture(capturedX, capturedY, capturedWallCovering, currentTime);
																			if (coveringTex.Id != 0)
																			{
																				Raylib.DrawTexturePro(coveringTex,
																					new Rectangle(0, 0, coveringTex.Width, coveringTex.Height),
																					new Rectangle(drawX, drawY, drawW, originalDrawH), Vector2.Zero, 0, Color.White);
																			}
																		}
																	}

																	if (hasVisibleInteriorTile && originalDrawH > 0 && WorldTileRegistry.IsConnectable(capturedObjectId))
																	{
																		bool IsVisibleNeighbor(int nx, int ny, bool isVertical = false)
																		{
																			if (!IsConnectableSameType(nx, ny, capturedObjectId)) return false;
																		if (!Program.IsTileLit(nx, ny)) return false;
																			if (_isPlayerIndoors)
																			{
																				if (!HasVisibleInteriorTile(nx, ny)) return false;
													// Vérifier le toit seulement pour les connexions verticales
													if (isVertical && GetOverlayAt(nx, ny - 1) == 82) return false;
												}
												return true;
											}

											if (IsVisibleNeighbor(capturedX - 1, capturedY, false))
											{
												Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "left");
												if (connTex.Id != 0)
													Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
														new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
											}
											if (IsVisibleNeighbor(capturedX + 1, capturedY, false))
											{
												Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "right");
												if (connTex.Id != 0)
													Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
														new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
											}
											if (IsVisibleNeighbor(capturedX, capturedY - 1, true))
											{
												Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "up");
												if (connTex.Id != 0)
													Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
														new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
											}
											if (IsVisibleNeighbor(capturedX, capturedY + 1, true))
											{
												Texture2D connTex = WorldTileRegistry.GetConnectionTexture(capturedObjectId, "down");
												if (connTex.Id != 0)
													Raylib.DrawTexturePro(connTex, new Rectangle(0,0,connTex.Width,connTex.Height),
														new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
											}
										}

										// ==========  DESSIN DE LA RUCHE (métadonnée de tuile "beehive") ==========
												if (World.HasTileMeta(capturedX, capturedY, "ore"))
												{
													Texture2D oreTex = World.GetOreOverlayTexture(capturedX, capturedY);
													if (oreTex.Id != 0)
													{
														Raylib.DrawTexturePro(oreTex,
															new Rectangle(0, 0, oreTex.Width, oreTex.Height),
															new Rectangle(drawX, drawY, drawW, drawH), Vector2.Zero, 0, Color.White);
													}
												}
												if (World.HasTileMeta(capturedX, capturedY, "beehive"))
										{
											Texture2D beehiveTex = WorldTileRegistry.GetFirstTexture(6000);
											if (beehiveTex.Id != 0)
											{
												var beehiveTileData = WorldTileRegistry.GetTile(6000);
												int offX = beehiveTileData?.DrawOffset?.X ?? 0;
												int offY = beehiveTileData?.DrawOffset?.Y ?? 0;
												Rectangle beeDest = new Rectangle(
													drawX + offX,
													drawY + offY,
													drawW,
													drawH
												);
												Raylib.DrawTexturePro(beehiveTex,
													new Rectangle(0, 0, beehiveTex.Width, beehiveTex.Height),
													beeDest, Vector2.Zero, 0, Color.White);
											}
										}

										// Affichage spécial pour les étagères (ID 81)
										if (capturedObjectId == 81 && capturedTileData.IsContainer == true)
										{
											var chunkAtPos = GetChunkAt(capturedX, capturedY);
											var container = chunkAtPos?.GetContainerAt(capturedX, capturedY);
											if (container != null)
											{
												int shelfW = drawW;
												int itemSize = 14;
												int spacingX = itemSize + 1;
												int spacingY = itemSize + 8;
												int cols = container.Columns;
												int startX = drawX + (shelfW - (cols * itemSize + (cols - 1) * 4)) / 2;
												int startY = drawY + 15;
												for (int i = 0; i < container.Slots.Count; i++)
												{
													int col = i % cols;
													int row = i / cols;
													var slot = container.Slots[i];
													if (!slot.IsEmpty && slot.Item != null)
													{
														int iconX = startX + col * spacingX;
														int iconY = startY + row * spacingY;
														//  Toujours passer par ItemRenderer (gère lui-même le repli
														// couches teintées / icône / couleur) plutôt que de dessiner
														// slot.Item.Icon à la main : sinon les items sans texture de
														// base (chapka, fedora...) restent invisibles sur l'étagère.
														ItemRenderer.DrawItemCompact(slot.Item, iconX, iconY, itemSize);
													}
												}
											}
										}
										// Aquarium (ID 99)
										else if (capturedObjectId == 99 && capturedTileData.IsContainer == true)
										{
											var chunkAtPos = GetChunkAt(capturedX, capturedY);
											var container = chunkAtPos?.GetContainerAt(capturedX, capturedY);
											var aquarium = GetAquariumData(capturedX, capturedY);
											if (aquarium != null && container != null)
											{
												foreach (var fish in aquarium.FishStates.Values)
												{
													if (fish.SlotIndex < 0 || fish.SlotIndex >= container.Slots.Count) continue;
													var slot = container.Slots[fish.SlotIndex];
													if (slot?.Item == null) continue;

													float baseWorldY = aquarium.SwimArea.Y + fish.BaseY * aquarium.SwimArea.Height;
													float bob = MathF.Sin(currentTime * fish.BobSpeed + fish.BobPhase) * 3f;
													Vector2 worldPos = new Vector2(
														aquarium.SwimArea.X + fish.LocalPosition.X * aquarium.SwimArea.Width,
														baseWorldY + bob
													);

													float size = 14f;
													Rectangle destRect = new Rectangle(worldPos.X - size/2, worldPos.Y - size/2, size, size);
													//  Toujours passer par ItemRenderer (gère lui-même le repli
													// couches teintées / icône / couleur) plutôt que de dessiner
													// slot.Item.Icon à la main.
													ItemRenderer.DrawItem(slot.Item, destRect, forceSingleLayer: true);
												}
											}
										}
									}

									// Overlays (minerais, etc.)
									if (currentOverlay != 0 && currentOverlay != 82 && currentOverlay != 123 && currentOverlay != 6000 && capturedObjectId != 408)
									{
										Texture2D overlayTex = GetTileTexture(capturedX, capturedY, currentOverlay, currentTime);
										if (overlayTex.Id != 0)
										{
											Raylib.DrawTexturePro(overlayTex,
												new Rectangle(0, 0, overlayTex.Width, overlayTex.Height),
												new Rectangle(drawX, drawY, drawW, drawH),
												Vector2.Zero, 0, Color.White);
										}
									}

									if (capturedHp != null && capturedHp.Current < capturedHp.Max)
									{
										float ratio = Math.Clamp((float)capturedHp.Current / Math.Max(1, capturedHp.Max), 0f, 1f);
										float barWidth = ts;
										float barHeight = 7f;
										float barX = capturedWx;
										float barY = capturedWy - ts * 2 - 8;
										Rectangle barRect = new Rectangle(barX, barY, barWidth, barHeight);
										Rectangle fillRect = new Rectangle(barX + 1.5f, barY + 1.5f, Math.Max(0f, (barWidth - 3f) * ratio), barHeight - 3f);
										Raylib.DrawRectangleRounded(new Rectangle(barX - 2f, barY - 2f, barWidth + 4f, barHeight + 4f), 0.45f, 8, new Color(0, 0, 0, 105));
										Raylib.DrawRectangleRounded(barRect, 0.45f, 8, new Color(32, 20, 24, 225));
										if (fillRect.Width > 0f)
										{
											Color fillColor = ratio < 0.3f
												? new Color(225, 65, 58, 255)
												: ratio < 0.6f
													? new Color(238, 165, 58, 255)
													: new Color(90, 210, 105, 255);
											Raylib.DrawRectangleRounded(fillRect, 0.35f, 6, fillColor);
											Raylib.DrawRectangleRounded(
												new Rectangle(fillRect.X + 1f, fillRect.Y + 0.5f, Math.Max(0f, fillRect.Width - 2f), 1.5f),
												0.5f, 6, new Color(255, 255, 255, 85));
										}
										Raylib.DrawRectangleRoundedLines(barRect, 0.45f, 8, 1, new Color(255, 220, 220, 150));
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
