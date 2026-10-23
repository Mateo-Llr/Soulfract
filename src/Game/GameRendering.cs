#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Soulfract.WorldGeneration;
using DiscordRPC;

namespace Soulfract
{
    public static partial class Program
    {
		static void Draw(Vector2 playerPos, float facing, Vector2 hitboxDim, Camera2D camera,
                 int craftSelected, bool showDebug)
		{
			Raylib.BeginDrawing();
			PerfStats.StartSection(PerfStats.Section.FrameDraw);
			bool screenOverlaysStarted = false;
			if (_isSoulArrivalCinematic)
			{
				DrawSoulArrivalCinematic();
			}
			else if (_gameState == GameState.Loading)
			{
				DrawLoadingScreen(playerPos, facing, hitboxDim, camera, craftSelected, showDebug);
			}
			else if (_isSelectingSave) DrawCreateWorldMenu();
			else if (_isInWorldCreation) DrawCreateWorldMenu();
			else if (_isJoinCharacterCreation) DrawCreateWorldMenu();
			else if (_isMultiplayerMenu)
			{
				Raylib.ClearBackground(Color.Black);
				DrawMultiplayerMenu();
			}
			else if (_gameState == GameState.MainMenu || _gameState == GameState.Options || _gameState == GameState.Achievements)
			{
				Raylib.ClearBackground(Color.Black);
				if (_gameState == GameState.MainMenu) DrawMainMenu();
				else if (_gameState == GameState.Options) DrawOptionsMenu();
				else if (_gameState == GameState.Achievements) DrawAchievementsMenu();
			}
			else if (_gameState == GameState.Map) DrawMap();
			else
			{
				Raylib.ClearBackground(new Color(135, 206, 235, 255));
				bool captureWorldForReveal = _fadeOutOverlayActive && _worldRevealAlpha < 1f;
				if (captureWorldForReveal)
				{
					if (_menuRevealTexture.Id != 0)
					{
						Rectangle menuSource = new Rectangle(0, 0, ScreenWidth, -ScreenHeight);
						Rectangle menuDestination = new Rectangle(0, 0, ScreenWidth, ScreenHeight);
						byte menuAlpha = (byte)Math.Clamp((1f - _worldRevealAlpha) * 255f, 0f, 255f);
						Raylib.DrawTexturePro(_menuRevealTexture.Texture, menuSource, menuDestination, Vector2.Zero, 0f,
							new Color((byte)255, (byte)255, (byte)255, menuAlpha));
					}
					if (_worldRevealTexture.Id == 0 || _worldRevealTexture.Texture.Width != ScreenWidth || _worldRevealTexture.Texture.Height != ScreenHeight)
					{
						if (_worldRevealTexture.Id != 0) Raylib.UnloadRenderTexture(_worldRevealTexture);
						_worldRevealTexture = Raylib.LoadRenderTexture(ScreenWidth, ScreenHeight);
					}
					Raylib.BeginTextureMode(_worldRevealTexture);
					Raylib.ClearBackground(new Color((byte)0, (byte)0, (byte)0, (byte)0));
				}
					ApplyPreciseCameraTransform(ref camera);
					Camera2D shakenCamera = camera;
				shakenCamera.Target += _shakeOffset;
				Raylib.BeginMode2D(shakenCamera);
				_currentCamera = camera;
				PerfStats.StartSection(PerfStats.Section.WorldPreDraw);
				Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
				(int hoverTileX, int hoverTileY, _, _) = FindTileUnderMouse(mouseWorldPos, TileSize);
				
				if (_isChargingThrow && _predictedThrowLanding.HasValue)
				{
					Vector2 pos = _predictedThrowLanding.Value;
					float pulse = 0.7f + 0.3f * MathF.Sin((float)Raylib.GetTime() * 8f);
					float chargePercent = _throwCharge / MAX_THROW_CHARGE;
					Color indicatorColor = new Color(255, 100 + (int)(100 * chargePercent), 50, (int)(180 * pulse));
					
					Raylib.DrawCircle((int)pos.X, (int)pos.Y, 12, indicatorColor);
					Raylib.DrawCircleLines((int)pos.X, (int)pos.Y, 12, Color.White);
					
					Raylib.DrawLine((int)pos.X - 8, (int)pos.Y, (int)pos.X + 8, (int)pos.Y, Color.White);
					Raylib.DrawLine((int)pos.X, (int)pos.Y - 8, (int)pos.X, (int)pos.Y + 8, Color.White);
					
					int chargeX = (int)pos.X - 20;
					int chargeY = (int)pos.Y - 25;
					Raylib.DrawRectangle(chargeX, chargeY, 40, 6, new Color(50, 50, 50, 180));
					Raylib.DrawRectangle(chargeX, chargeY, (int)(40 * chargePercent), 6, new Color(255, 100, 50, 220));
				}
				
				if (_isChargingFishing && _predictedFishingLanding.HasValue)
				{
					Vector2 pos = _predictedFishingLanding.Value;
					float pulse = 0.7f + 0.3f * MathF.Sin((float)Raylib.GetTime() * 8f);
					float chargePercent = _fishingCharge / MAX_FISHING_CHARGE;
					Color indicatorColor = new Color(100, 200, 255, (int)(180 * pulse));
					Raylib.DrawCircle((int)pos.X, (int)pos.Y, 12, indicatorColor);
					Raylib.DrawCircleLines((int)pos.X, (int)pos.Y, 12, Color.White);
					Raylib.DrawLine((int)pos.X - 8, (int)pos.Y, (int)pos.X + 8, (int)pos.Y, Color.White);
					Raylib.DrawLine((int)pos.X, (int)pos.Y - 8, (int)pos.X, (int)pos.Y + 8, Color.White);
					int chargeX = (int)pos.X - 20;
					int chargeY = (int)pos.Y - 25;
					Raylib.DrawRectangle(chargeX, chargeY, 40, 6, new Color(50, 50, 50, 180));
					Raylib.DrawRectangle(chargeX, chargeY, (int)(40 * chargePercent), 6, new Color(100, 200, 255, 220));
				}
				
				Item? heldItem = null;
				bool isCarryingFurniture = _carriedFurniture != null;
				int? carriedPlaceableId = _carriedFurniture?.PlaceableId;

				if (_carriedFurniture != null)
				{
					if (GameData.ItemDatabase.TryGetValue(_carriedFurniture.ItemId, out var itemData))
					{
						heldItem = new Item(itemData.Name, 1, itemData.Color, itemData.Icon);
					}
				}
				else
				{
					var hotbarSlots = GetHotbarSlotsCached(); //  OPTIM : cache par frame
					if (hotbarSlot < hotbarSlots.Count && !hotbarSlots[hotbarSlot].IsEmpty)
						heldItem = hotbarSlots[hotbarSlot].Item;
				}
				
				if (showDebug && DebugOverlaySettings.VehicleAndAttackHitboxes)
				{
					foreach (var car in Program.Cars)
					{
						car.DrawDebugHitbox();
					}
				}
				
				bool isCarrying = false;
				if (heldItem != null)
				{
					int id = GetItemId(heldItem.Name);
					if (GameData.ItemDatabase.TryGetValue(id, out var data) && data.Type == ItemType.Placeable) isCarrying = true;
				}
				
				List<LocalPlayer> remotePlayersToRender = null;
				if (NetworkManager.IsOnline)
				{
					remotePlayersToRender = new List<LocalPlayer>();
					foreach (var rp in NetworkManager.GetRemotePlayersAsLocalPlayers())
					{
						bool isDeadNow = rp.HP <= 0;
						bool wasDeadBefore = _remoteWasDead.TryGetValue(rp.Id, out var wd) && wd;
						if (isDeadNow && !wasDeadBefore)
						{
							//  Même explosion de particules bleues que pour le joueur local :
							// on la voit désormais nous aussi, au lieu de le voir rester debout.
							SpawnPlayerDeathEffect(GetPlayerVisualPosition(rp.Position));
							//  CORPS MORT (ragdoll) : le joueur distant laisse un corps au sol.
							CorpseSystem.SpawnRemotePlayer(rp);
						}
						_remoteWasDead[rp.Id] = isDeadNow;
						if (isDeadNow) continue; //  masqué tant qu'il n'est pas ressuscité (HP redevenu > 0)
						if (rp.Underground != World.IsUnderground) continue;
						if (rp.IsInDungeon != World.IsInDungeon
							|| (World.IsInDungeon && rp.DungeonInstanceId != World.CurrentDungeonInstanceId)) continue;
						remotePlayersToRender.Add(rp);
					}
				}

				// Offrandes du cercle de rituel : injectées dans le tri par Y du monde pour apparaître
				// comme de vrais objets posés au sol (devant/derrière selon la profondeur), sans "case".
				RitualCircleUI.QueueGroundItems();

				PerfStats.EndSection(PerfStats.Section.WorldPreDraw);
				PerfStats.StartSection(PerfStats.Section.LightGridPrepare);
				PrepareLightGridForWorldDraw(camera);
				PerfStats.EndSection(PerfStats.Section.LightGridPrepare);
				PerfStats.StartSection(PerfStats.Section.WorldDraw);
				World.DrawWorld(playerPos, camera, _animState, _animFrame, _animProg, facing, hitboxDim, SkinColor,
					missingTexture, destroyedObjects, objectHPs, entities, tileTextures,
					hairBaseTextures, hairOverlayTextures, mouseWorldPos, hoverTileX, hoverTileY, DebugOverlaySettings.AnyEnabled,
					isCarrying, _isPlayerInWater, _headAngle, _isSitting ? _sittingPosition : null, _playerDepth,
					playersToRender: remotePlayersToRender, spellProjectiles: _spellProjectiles,
					drawPlayer: !captureWorldForReveal || _worldRevealAlpha >= 0.82f);
				if (showDebug && DebugOverlaySettings.VehicleAndAttackHitboxes)
					DrawAttackDebugZone(playerPos, mouseWorldPos, facing, heldItem);
				PerfStats.EndSection(PerfStats.Section.WorldDraw);
				PerfStats.StartSection(PerfStats.Section.WorldEffects);
				//  PORTAGE DE CRÉATURES : dessinée désormais à l'intérieur même de World.cs / EntityRenderer,
				// entre le bras gauche et le bras droit du joueur, pour un rendu correctement superposé.

				// ==================== APERÇU DE SÉLECTION DE STRUCTURE (F7/F8) ====================
				// Affiche un point au centre de CHAQUE tuile qui sera effectivement incluse dans la
				// sauvegarde (SaveStructure), pour repérer immédiatement un décalage d'1 tuile comme
				// celui qui a fait "manger" la colonne de mur gauche de certaines structures : le point
				// est dessiné avec le même calcul de hauteur/offset que le rendu du monde, donc s'il
				// manque un point sur une tuile de mur, ou si le rectangle ne colle pas au mur, c'est
				// visible avant même d'appuyer sur F8.
				if (_selectionCorner1.HasValue)
				{
					int c1x = _selectionCorner1.Value.X;
					int c1y = _selectionCorner1.Value.Y;
					// Tant que le second coin n'est pas posé (F8), on prévisualise avec la tuile
					// survolée par la souris : le rectangle affiché est EXACTEMENT celui qui serait
					// sauvegardé si on appuyait sur F8 maintenant.
					int c2x = _selectionCorner2?.X ?? hoverTileX;
					int c2y = _selectionCorner2?.Y ?? hoverTileY;

					int selMinX = Math.Min(c1x, c2x);
					int selMaxX = Math.Max(c1x, c2x);
					int selMinY = Math.Min(c1y, c2y);
					int selMaxY = Math.Max(c1y, c2y);

					for (int sx = selMinX; sx <= selMaxX; sx++)
					{
						for (int sy = selMinY; sy <= selMaxY; sy++)
						{
							float centerX = sx * TileSize + TileSize / 2f;
							float centerY = sy * TileSize + TileSize / 2f;

							bool isFirstCorner = (sx == c1x && sy == c1y);
							bool isPerimeter = (sx == selMinX || sx == selMaxX || sy == selMinY || sy == selMaxY);

							Color dotColor = isFirstCorner
								? Color.Lime
								: (isPerimeter ? new Color(255, 210, 0, 230) : new Color(255, 255, 0, 130));
							int radius = isFirstCorner ? 5 : (isPerimeter ? 4 : 2);

							Raylib.DrawCircle((int)centerX, (int)centerY, radius, dotColor);
						}
					}

					// Contour du rectangle exact (en coordonnées tuiles -> monde), pour visualiser
					// sans ambiguïté les bords min/max réellement utilisés par SaveStructure.
					float rectX = selMinX * TileSize;
					float rectY = selMinY * TileSize;
					float rectW = (selMaxX - selMinX + 1) * TileSize;
					float rectH = (selMaxY - selMinY + 1) * TileSize;
					Raylib.DrawRectangleLinesEx(new Rectangle(rectX, rectY, rectW, rectH), 2f, new Color(255, 255, 0, 200));
				}

				//  OMBRE AU SOL DU GULPER (boss volant) : reste ancrée au sol pendant tout le vol
				// (contrairement au sprite, qui monte/descend avec _guHeight), et se resserre /
				// s'assombrit à mesure qu'il se rapproche du sol, pour bien lire sa trajectoire de chute.
				foreach (var gulperEnt in entities)
				{
					if (!gulperEnt.IsAlive || !gulperEnt.IsGulper) continue;
					if (!World.ShouldRenderEntity(gulperEnt.WorldPos)) continue;
					Vector2 groundPos = new Vector2(gulperEnt.WorldPos.X, gulperEnt.WorldPos.Y + gulperEnt.GulperHeight);
					float heightRatio = Math.Clamp(gulperEnt.GulperHeight / 85f, 0f, 1f); // 85 = GU_HOVER_HEIGHT
					float shadowW = MathHelper.Lerp(46f, 30f, heightRatio);
					float shadowH = shadowW * 0.35f;
					byte shadowAlpha = (byte)MathHelper.Lerp(150f, 60f, heightRatio);
					Raylib.DrawEllipse((int)groundPos.X, (int)groundPos.Y, shadowW / 2f, shadowH / 2f, new Color((byte)0, (byte)0, (byte)0, shadowAlpha));
				}
				
				// ==================== DÉGÂTS FLOTTANTS (CORRIGÉ AVEC CONVERSION ÉCRAN) ====================
				for (int i = _floatingDamages.Count - 1; i >= 0; i--)
				{
					var dmg = _floatingDamages[i];
					
					// Vérifier validité
					if (float.IsNaN(dmg.Position.X) || float.IsNaN(dmg.Position.Y) ||
						float.IsInfinity(dmg.Position.X) || float.IsInfinity(dmg.Position.Y))
					{
						_floatingDamages.RemoveAt(i);
						continue;
					}
					
					// On est encore DANS BeginMode2D ici : on dessine directement en coordonnées MONDE,
					// il ne faut pas convertir en écran (sinon la caméra transforme deux fois la position).
					float alpha = Math.Clamp(dmg.Timer / dmg.MaxTimer, 0f, 1f);
					byte alphaByte = (byte)(alpha * 255);
					Color drawColor = new Color(dmg.Color.R, dmg.Color.G, dmg.Color.B, alphaByte);
					float yOffset = Math.Clamp(dmg.FloatOffset, -80f, 0f);
					int fontSize = Math.Clamp(20 + (dmg.Damage / 5), 18, 28);
					
					bool isHeal = (dmg.Color.G > 150 && dmg.Color.R < 150);
					string displayText = isHeal ? $"+{dmg.Damage}" : $"-{dmg.Damage}";
					
					int textX = (int)dmg.Position.X - 15;
					int textY = (int)dmg.Position.Y - 30 + (int)yOffset;
					
					if (dmg.OutlineColor.HasValue)
					{
						//  Contour : on dessine le texte dans la couleur de contour décalé dans les 8 directions, puis la couleur principale par-dessus
						Color oc = dmg.OutlineColor.Value;
						Color outlineColor = new Color(oc.R, oc.G, oc.B, alphaByte);
						for (int ox = -1; ox <= 1; ox++)
						{
							for (int oy = -1; oy <= 1; oy++)
							{
								if (ox == 0 && oy == 0) continue;
								FontManager.DrawText(displayText, textX + ox, textY + oy, fontSize, outlineColor);
							}
						}
					}
					
					// Dessiner le texte avec les coordonnées monde (cohérent avec le reste du bloc BeginMode2D)
					FontManager.DrawText(displayText, textX, textY, fontSize, drawColor);
				}
				// ==================== FIN DÉGÂTS FLOTTANTS ====================
				
				SpellVfx.DrawBatched(_particles);
				foreach (var particle in _particles)
				{
					if (particle is SpellFxParticle)
					{
						continue;
					}
					else if (particle is MagicExplosionEffect explosion)
					{
						explosion.Draw();
					}
					else if (particle is SlashEffect slash)
					{
						slash.Draw();
					}
					else if (particle is PoisonCloudParticle poisonCloud)
					{
						poisonCloud.Draw();
					}
					else if (particle is ZzzParticle zzz)
					{
						zzz.Draw();
					}
					else if (particle is MusicNoteParticle musicNote)
					{
						musicNote.Draw();
					}
					else if (particle is LevelUpStreakParticle streak)
					{
						streak.Draw();
					}
					else if (particle is LeafParticle leaf)
					{
						leaf.Draw(TreeLeafTexture);
					}
					else
					{
						byte alpha = (byte)(particle.GetAlpha() * 255);
						Color drawColor = new Color(particle.Color.R, particle.Color.G, particle.Color.B, alpha);
						
						// Toutes les particules utilisent des rectangles
						Raylib.DrawRectangle((int)(particle.Position.X - particle.Size / 2), 
											(int)(particle.Position.Y - particle.Size / 2), 
											(int)particle.Size, (int)particle.Size, drawColor);
					}
				}

				//  Boucliers rotatifs autour de l'oeil (remplace l'ancienne barre de vie du boss).
				DrawEyeBossShields();

				//  Les projectiles magiques sont désormais dessinés à l'intérieur de World.DrawWorld,
				// intégrés au tri par Y avec les tuiles et les entités (voir spellProjectiles: ... plus haut).

				if (PlayerCurrentCar == null)
				{
					Item? held = null;
					var slots = GetHotbarSlotsCached(); //  OPTIM : cache par frame
					if (hotbarSlot < slots.Count && !slots[hotbarSlot].IsEmpty) held = slots[hotbarSlot].Item;
					if (held != null)
					{
						var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == held.Name);
						itemData.PlaceableId = ResolvePlaceableId(held, itemData);
						if (itemData.ID != 0 && (itemData.Type == ItemType.Placeable || itemData.IsSeed))
						{
							DrawPlacementGhost(camera, held, itemData);
						}
					}
				}
				
				foreach (var arrow in _arrows) arrow.Draw();
				foreach (var bullet in _bullets) bullet.Draw();
				foreach (var throwable in _throwables) throwable.Draw();

				if (_currentFishingProjectile != null) _currentFishingProjectile.Draw();
				
	
				//  MÉTÉO : mise à jour puis rendu de la pluie, DANS BeginMode2D pour qu'elle
				// suive le monde (zoom/scroll) et soit dessinée par-dessus tout ce qui précède,
				// toits inclus (World.DrawWorld a déjà dessiné les toits juste au-dessus).
				Weather.Update(Raylib.GetFrameTime(), camera);
				Weather.Draw(camera);
				
				Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
				int ts = Program.TileSize;

				foreach (var kv in World.SteamEngines)
				{
					var (x, y) = kv.Key;
					var engine = kv.Value;
					
					float drawX = x * ts;
					float drawY = y * ts;
					
					var tileData = WorldTileRegistry.GetTile(401);
					int machineTilesW = tileData?.Size?.Width ?? 1;
					int machineTilesH = tileData?.Size?.Height ?? 1;
					if (tileData?.Size != null)
					{
						drawX += tileData.DrawOffset?.X ?? 0;
						drawY += tileData.DrawOffset?.Y ?? 0;
						drawY = drawY - (ts * tileData.Size.Height) + ts;
					}
					
					//  FIX : la zone de survol se basait sur une taille fixe (ts*2) au lieu de la
					// taille réelle de la tuile (worldobjects.json -> "size"), ce qui la désynchronisait
					// dès que la machine n'était plus 2x2 (ou si sa taille venait à changer).
					Rectangle machineRect = new Rectangle(drawX, drawY, ts * machineTilesW, ts * machineTilesH);
					
					if (Raylib.CheckCollisionPointRec(mouseWorld, machineRect))
					{
						_hoveredMachine = (x, y, engine);
					}
				}
				
				foreach (var kv in World.Dynamos)
				{
					int x = kv.Key.x;
					int y = kv.Key.y;
					var dynamo = kv.Value;
					
					float drawX = x * ts;
					float drawY = y * ts;
					
					var tileData = WorldTileRegistry.GetTile(403);
					if (tileData?.Size != null)
					{
						drawX += tileData.DrawOffset?.X ?? 0;
						drawY += tileData.DrawOffset?.Y ?? 0;
						drawY = drawY - (ts * tileData.Size.Height) + ts;
					}
					
					if (dynamo.ElectricityOutput > 0 && _gearTexture.Id != 0)
					{
						float gearX = drawX + ts;
						float gearY = drawY - ts / 2f;
						float rotation = (float)Raylib.GetTime() * 180f;
						float scale = 0.7f;
						
						Rectangle srcRect = new Rectangle(0, 0, _gearTexture.Width, _gearTexture.Height);
						Rectangle destRect = new Rectangle(gearX - (ts * scale) / 2, gearY - (ts * scale) / 2, ts * scale, ts * scale);
						Vector2 origin = new Vector2(destRect.Width / 2, destRect.Height / 2);
						
						Raylib.DrawTexturePro(_gearTexture, srcRect, destRect, origin, rotation, Color.White);
					}
					
					Rectangle dynamoRect = new Rectangle(drawX, drawY, ts * 2, ts * 2);
					
					if (Raylib.CheckCollisionPointRec(mouseWorld, dynamoRect))
					{
						int tooltipW = 200, tooltipH = 75;
						Vector2 mouseScreen = Raylib.GetMousePosition();
						int tooltipX = (int)mouseScreen.X + 15;
						int tooltipY = (int)mouseScreen.Y + 15;
						
						if (tooltipX + tooltipW > Raylib.GetScreenWidth())
							tooltipX = (int)mouseScreen.X - tooltipW - 15;
						if (tooltipY + tooltipH > Raylib.GetScreenHeight())
							tooltipY = (int)mouseScreen.Y - tooltipH - 15;
						
						Raylib.DrawRectangleRounded(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, new Color(20, 20, 30, 220));
						Raylib.DrawRectangleRoundedLines(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, 1, new Color(210, 180, 100, 150));
						
						FontManager.DrawText(Localization.Get("tooltip.dynamo"), tooltipX + 8, tooltipY + 6, 12, new Color(210, 180, 100, 255));
						FontManager.DrawText($"Vapeur reçue: {dynamo.SteamInput:F0} kW", tooltipX + 8, tooltipY + 24, 11, Color.White);
						FontManager.DrawText($"Production: {dynamo.ElectricityOutput:F0} kW", tooltipX + 8, tooltipY + 40, 11, 
							dynamo.ElectricityOutput > 0 ? new Color(100, 255, 100, 255) : new Color(200, 200, 100, 255));
						FontManager.DrawText($" Connexions: {dynamo.Connections.Count}", tooltipX + 8, tooltipY + 56, 10, new Color(150, 150, 150, 255));
						
						if (!_isConnectingWire)
						{
							FontManager.DrawText(Localization.Get("tooltip.press_e_to_connect"), tooltipX + 8, tooltipY + 68, 9, new Color(100, 200, 255, 200));
						}
						else if (_wireStartTile == (-1, -1))
						{
							FontManager.DrawText(Localization.Get("tooltip.click_battery"), tooltipX + 8, tooltipY + 68, 9, new Color(255, 200, 100, 200));
						}
					}
				}

				// ====== CHARGEURS DE BATTERIE : détection du survol (infobulle dessinée après EndMode2D) ======
				foreach (var kv in World.BatteryChargers)
				{
					int x = kv.Key.x;
					int y = kv.Key.y;
					var charger = kv.Value;

					if (World.GetObjectIdAt(x, y) != 405) continue;

					float drawX = x * ts;
					float drawY = y * ts;

					var tileData = WorldTileRegistry.GetTile(405);
					if (tileData?.Size != null)
					{
						drawX += tileData.DrawOffset?.X ?? 0;
						drawY += tileData.DrawOffset?.Y ?? 0;
						drawY = drawY - (ts * tileData.Size.Height) + ts;
					}

					Rectangle chargerRect = new Rectangle(drawX, drawY, ts * (tileData?.Size?.Width ?? 2), ts * (tileData?.Size?.Height ?? 1));

					if (Raylib.CheckCollisionPointRec(mouseWorld, chargerRect))
					{
						_hoveredBatteryCharger = (x, y, charger);
					}
				}

				// ====== COLLECTEURS DE PLUIE : survol + jauge au-dessus de la tuile ======
				foreach (var kv in World.RainCollectors)
				{
					int x = kv.Key.x;
					int y = kv.Key.y;
					var rc = kv.Value;

					// Si la tuile n'est plus un collecteur, on l'ignore (sera nettoyé ailleurs)
					if (World.GetObjectIdAt(x, y) != 116) continue;

					float drawX = x * ts;
					float drawY = y * ts;

					var tileData = WorldTileRegistry.GetTile(116);
					if (tileData?.Size != null)
					{
						drawX += tileData.DrawOffset?.X ?? 0;
						drawY += tileData.DrawOffset?.Y ?? 0;
						drawY = drawY - (ts * tileData.Size.Height) + ts;
					}

					Rectangle rect = new Rectangle(drawX, drawY, ts * (tileData?.Size?.Width ?? 1), ts * (tileData?.Size?.Height ?? 1));

					if (Raylib.CheckCollisionPointRec(mouseWorld, rect))
					{
						_hoveredRainCollector = (x, y, rc);

						// Dessiner une petite jauge au-dessus de la tuile (coordonnées MONDE)
						float barW = Math.Min(48, ts);
						float barH = 6f;
						float barX = drawX + rect.Width / 2f - barW / 2f;
						float barY = drawY - 12f - barH;
						Raylib.DrawRectangle((int)barX, (int)barY, (int)barW, (int)barH, new Color(40, 40, 50, 200));
						float frac = rc.Capacity > 0 ? Math.Min(1f, rc.CurrentWater / rc.Capacity) : 0f;
						Raylib.DrawRectangle((int)barX, (int)barY, (int)(barW * frac), (int)barH, new Color(50, 150, 255, 220));
						FontManager.DrawText($"{(int)rc.CurrentWater}L / {(int)rc.Capacity}L", (int)barX, (int)(barY - 14f), 10, Color.White);
					}
				}

				//  CERCLE DE RITUEL : offrandes + animation d'invocation, dessinées ici en
				// coordonnées MONDE (comme les jauges ci-dessus) pour que ça fasse partie du
				// rendu du jeu et non une interface plaquée par-dessus.
				RitualCircleUI.Draw();

				PerfStats.EndSection(PerfStats.Section.WorldEffects);
				Raylib.EndMode2D();
				if (captureWorldForReveal)
				{
					Raylib.EndTextureMode();
					Rectangle source = new Rectangle(0, 0, ScreenWidth, -ScreenHeight);
					Rectangle destination = new Rectangle(0, 0, ScreenWidth, ScreenHeight);
					EnsureWorldRevealShader();
					if (_worldRevealShader.Id != 0 && _worldRevealProgressLocation >= 0)
					{
						Raylib.SetShaderValue(_worldRevealShader, _worldRevealProgressLocation, _worldRevealAlpha, ShaderUniformDataType.Float);
						Raylib.BeginShaderMode(_worldRevealShader);
					}
					Raylib.DrawTexturePro(_worldRevealTexture.Texture, source, destination, Vector2.Zero, 0f, Color.White);
					if (_worldRevealShader.Id != 0 && _worldRevealProgressLocation >= 0)
						Raylib.EndShaderMode();

					if (!string.IsNullOrEmpty(_pendingLoadSaveName) && _carouselPreviewCache.ContainsKey(_pendingLoadSaveName))
					{
						Camera2D transitionCamera = camera;
						transitionCamera.Offset = new Vector2(ScreenWidth / 2f, ScreenHeight / 2f);
						transitionCamera.Target = GetPlayerCameraTarget(_playerPos);
						Vector2 characterPosition = Raylib.GetWorldToScreen2D(GetPlayerCameraTarget(_playerPos), transitionCamera);
						float characterScale = Math.Max(0.01f, camera.Zoom);
						if (_worldRevealAlpha < 0.82f)
						{
							DrawClassicSavePreview(_pendingLoadSaveName, characterPosition, characterScale,
								Math.Max(80f, ScreenHeight * 0.18f), 1f, _animFrame, _animProg);
						}
					}
				}

				//  ÉCRAN DE MORT : vignette -> pop du crâne -> message qui s'écrit -> bouton
				// "Réapparaître" (voir DrawDeathScreen / UpdateDeathScreen). Retiré d'ici et
				// dessiné tout en bas, juste avant EndDrawing, pour être garanti au-dessus de
				// TOUT le reste (lumière jour/nuit, température, HUD, notifications...).
				
				if (_gameState == GameState.Playing || _gameState == GameState.Paused)
				{
					Weather.DrawScreenTint();
				}

				if (_gameState == GameState.Playing || _gameState == GameState.Paused)
				{
					PerfStats.StartSection(PerfStats.Section.ScreenLighting);
					DrawLighting(camera);
					//  Léger voile de couleur (bleu-violet la nuit, rose saumon à l'aube,
					// orange au coucher de soleil), dessiné après le masque de lumière comme
					// une couche cosmétique en plus, pas un remplacement.
					DrawDayNightColorFilter();
					PerfStats.EndSection(PerfStats.Section.ScreenLighting);
				}
				PerfStats.StartSection(PerfStats.Section.ScreenOverlays);
				screenOverlaysStarted = true;

				// Jauge de charge pour le clic gauche
				if (_isChargingLeftClick && _leftClickSpell.Power != PowerLevel.Weak && PlayerCurrentCar == null && !InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen)
				{
					Vector2 playerScreen = Raylib.GetWorldToScreen2D(GetPlayerVisualPosition(_playerPos), camera);
					SpellVfx.DrawChargeIndicator(_leftClickSpell.Element, playerScreen, _leftClickCharge, 60f);
				}
				// Jauge de charge pour le clic droit
				if (_isChargingRightClick && _rightClickSpell.Power != PowerLevel.Weak && PlayerCurrentCar == null && !InventoryRenderer.IsInventoryOpen && !ChatSystem.IsOpen && !QuestDialogUI.IsOpen)
				{
					Vector2 playerScreen = Raylib.GetWorldToScreen2D(GetPlayerVisualPosition(_playerPos), camera);
					SpellVfx.DrawChargeIndicator(_rightClickSpell.Element, playerScreen, _rightClickCharge, 80f);
				}
				
				if (_hoveredMachine.HasValue)
				{
					var (x, y, engine) = _hoveredMachine.Value;
					
					int screenWidth = Raylib.GetScreenWidth();
					int screenHeight = Raylib.GetScreenHeight();
					Vector2 mouseScreen = Raylib.GetMousePosition();
					
					int tooltipW = 205, tooltipH = 190;
					int tooltipX = (int)mouseScreen.X + 15;
					int tooltipY = (int)mouseScreen.Y + 15;
					
					if (tooltipX + tooltipW > screenWidth)
						tooltipX = (int)mouseScreen.X - tooltipW - 15;
					if (tooltipY + tooltipH > screenHeight)
						tooltipY = (int)mouseScreen.Y - tooltipH - 15;
					
					Raylib.DrawRectangleRounded(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, new Color(20,20,30,220));
					Raylib.DrawRectangleRoundedLines(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, 1, new Color(210,180,100,150));
					
					FontManager.DrawText(Localization.Get("tooltip.steam_machine"), tooltipX + 10, tooltipY + 8, 14, new Color(235, 200, 110, 255));
					string status = engine.IsOn ? "EN MARCHE" : "ARRETEE";
					FontManager.DrawText(status, tooltipX + 10, tooltipY + 29, 10,
						engine.IsOn ? new Color(100,255,100,255) : new Color(255,100,100,255));

					if (_tooltipWaterIcon.Id != 0)
						Raylib.DrawTexturePro(_tooltipWaterIcon, new Rectangle(0, 0, _tooltipWaterIcon.Width, _tooltipWaterIcon.Height),
							new Rectangle(tooltipX + 10, tooltipY + 49, 28, 28), Vector2.Zero, 0, Color.White);
					FontManager.DrawText($"Eau: {(int)(engine.Water * 100f)} cL / {(int)(SteamEngineData.MaxWater * 100f)} cL", tooltipX + 46, tooltipY + 51, 11, Color.White);
					Raylib.DrawRectangle(tooltipX + 46, tooltipY + 68, 145, 7, new Color(40,40,50,255));
					Raylib.DrawRectangle(tooltipX + 46, tooltipY + 68, (int)(145 * engine.Water / SteamEngineData.MaxWater), 7, new Color(50,150,255,255));

					if (_tooltipFireIcon.Id != 0)
						Raylib.DrawTexturePro(_tooltipFireIcon, new Rectangle(0, 0, _tooltipFireIcon.Width, _tooltipFireIcon.Height),
							new Rectangle(tooltipX + 10, tooltipY + 86, 28, 28), Vector2.Zero, 0, Color.White);
					FontManager.DrawText($"Charbon: {(int)engine.Coal} / {(int)SteamEngineData.MaxCoal}", tooltipX + 46, tooltipY + 88, 11, Color.White);
					Raylib.DrawRectangle(tooltipX + 46, tooltipY + 105, 145, 7, new Color(40,40,50,255));
					Raylib.DrawRectangle(tooltipX + 46, tooltipY + 105, (int)(145 * engine.Coal / SteamEngineData.MaxCoal), 7, new Color(210,120,55,255));

					FontManager.DrawText($"Pression: {engine.SteamPressure:F0}%", tooltipX + 10, tooltipY + 128, 11, Color.White);
					Raylib.DrawRectangle(tooltipX + 10, tooltipY + 144, 181, 7, new Color(40,40,50,255));
					Raylib.DrawRectangle(tooltipX + 10, tooltipY + 144, (int)(181 * engine.SteamPressure / 100f), 7,
						engine.SteamPressure > 80 ? new Color(255,100,100,255) : new Color(200,200,100,255));
					FontManager.DrawText($"Production: {engine.ProductionRate:F0} kW", tooltipX + 10, tooltipY + 164, 11,
						engine.IsOn ? new Color(100,255,100,255) : new Color(200,200,100,255));
					
					_hoveredMachine = null;
				}

				if (_hoveredBatteryCharger.HasValue)
				{
					var (x, y, charger) = _hoveredBatteryCharger.Value;

					int screenWidth = Raylib.GetScreenWidth();
					int screenHeight = Raylib.GetScreenHeight();
					Vector2 mouseScreen = Raylib.GetMousePosition();

					int tooltipW = 220, tooltipH = charger.InsertedBattery != null ? 96 : 70;
					int tooltipX = (int)mouseScreen.X + 15;
					int tooltipY = (int)mouseScreen.Y + 15;

					if (tooltipX + tooltipW > screenWidth)
						tooltipX = (int)mouseScreen.X - tooltipW - 15;
					if (tooltipY + tooltipH > screenHeight)
						tooltipY = (int)mouseScreen.Y - tooltipH - 15;

					Raylib.DrawRectangleRounded(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, new Color(20, 20, 30, 220));
					Raylib.DrawRectangleRoundedLines(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, 1, new Color(210, 180, 100, 150));

					FontManager.DrawText("Chargeur de batterie", tooltipX + 8, tooltipY + 6, 12, new Color(210, 180, 100, 255));

					string powerStatus = charger.HasPower ? " Alimenté" : " Pas de courant";
					FontManager.DrawText(powerStatus, tooltipX + 8, tooltipY + 24, 11,
						charger.HasPower ? new Color(100, 255, 100, 255) : new Color(200, 100, 100, 255));

					if (charger.InsertedBattery != null)
					{
						var battery = charger.InsertedBattery;
						float frac = battery.MaxCapacity > 0 ? Math.Min(1f, battery.StoredEnergy / battery.MaxCapacity) : 0f;

						FontManager.DrawText($" Batterie: {(int)battery.StoredEnergy} / {(int)battery.MaxCapacity} Wh", tooltipX + 8, tooltipY + 40, 11, Color.White);
						Raylib.DrawRectangle(tooltipX + 8, tooltipY + 52, 150, 6, new Color(40, 40, 50, 255));
						Raylib.DrawRectangle(tooltipX + 8, tooltipY + 52, (int)(150 * frac), 6,
							frac >= 0.999f ? new Color(100, 255, 100, 255) : new Color(255, 220, 100, 255));

						FontManager.DrawText(" E pour retirer", tooltipX + 8, tooltipY + 64, 9, new Color(200, 200, 200, 200));
					}
					else
					{
						FontManager.DrawText(" Aucune batterie posée", tooltipX + 8, tooltipY + 40, 11, new Color(200, 200, 200, 255));
						FontManager.DrawText(" E pour poser la batterie tenue en main", tooltipX + 8, tooltipY + 54, 9, new Color(150, 200, 255, 200));
					}

					_hoveredBatteryCharger = null;
				}

				if (_hoveredRainCollector.HasValue)
				{
					var (x, y, collector) = _hoveredRainCollector.Value;
					int screenWidth = Raylib.GetScreenWidth();
					int screenHeight = Raylib.GetScreenHeight();
					Vector2 mouseScreen = Raylib.GetMousePosition();

					int tooltipW = 220, tooltipH = 70;
					int tooltipX = (int)mouseScreen.X + 15;
					int tooltipY = (int)mouseScreen.Y + 15;

					if (tooltipX + tooltipW > screenWidth) tooltipX = (int)mouseScreen.X - tooltipW - 15;
					if (tooltipY + tooltipH > screenHeight) tooltipY = (int)mouseScreen.Y - tooltipH - 15;

					Raylib.DrawRectangleRounded(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, new Color(20,20,30,220));
					Raylib.DrawRectangleRoundedLines(new Rectangle(tooltipX, tooltipY, tooltipW, tooltipH), 0.15f, 8, 1, new Color(100,180,255,150));

					FontManager.DrawText("Collecteur de pluie", tooltipX+8, tooltipY+6, 12, new Color(150,200,255,255));
					FontManager.DrawText($"Eau: {(int)collector.CurrentWater}L / {(int)collector.Capacity}L", tooltipX+8, tooltipY+26, 11, Color.White);
					Raylib.DrawRectangle(tooltipX+8, tooltipY+44, 180, 8, new Color(40,40,50,255));
					float frac = collector.Capacity > 0 ? Math.Min(1f, collector.CurrentWater / collector.Capacity) : 0f;
					Raylib.DrawRectangle(tooltipX+8, tooltipY+44, (int)(180 * frac), 8, new Color(50,150,255,255));
					FontManager.DrawText("Clic droit pour remplir gourde/arrosoir", tooltipX+8, tooltipY+56, 10, new Color(160,160,200,220));

					_hoveredRainCollector = null;
				}
				
				if (showDebug)
				{
					float playerBaseY = playerPos.Y + FeetOffsetY;
					Rectangle playerHitboxWorld = new Rectangle(playerPos.X - hitboxDim.X / 2f, playerBaseY - hitboxDim.Y, hitboxDim.X, hitboxDim.Y);
					Vector2 topLeft = Raylib.GetWorldToScreen2D(new Vector2(playerHitboxWorld.X, playerHitboxWorld.Y), camera);
					Vector2 bottomRight = Raylib.GetWorldToScreen2D(new Vector2(playerHitboxWorld.X + playerHitboxWorld.Width, playerHitboxWorld.Y + playerHitboxWorld.Height), camera);
					Rectangle playerHitbox = new Rectangle(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
					Raylib.DrawRectangleLinesEx(playerHitbox, 2, Color.Lime);
				}
				
				if (_gameState == GameState.Paused) DrawPauseMenu();
				{
					if (_isEmoteWheelOpen)
					{
						DrawEmoteWheel();
					}
					else if (_radialMenu.IsOpen)
					{
						_radialMenu.Draw();
					}
					else
					{
						// plus d'indicateur bas droite ; on affiche l'item dans la bulle.
					}
					Item? hotbarHeldItem = InventoryRenderer.GetHotbarItem(hotbarSlot);
					HudRenderer.Draw(playerHP, GetEffectivePlayerMaxHP(), playerStamina, playerMaxStamina, staminaLocked, playerHunger, playerMaxHunger, playerThirst, playerMaxThirst, hotbarHeldItem);
					if (SettingsManager.Settings.HotbarStyle == "classic")
						HudRenderer.DrawClassicHotbar(hotbarSlot);
					Renderer.UpdateAndDrawNotifications(activeNotifications, Raylib.GetFrameTime());
					DrawPouilleuxInvitation();
					
					if (CurrentBoss != null && CurrentBoss.IsAlive)
					{
						int sw = Raylib.GetScreenWidth();
						int barWidth = 420;
						int barHeight = 28;
						int x = (sw - barWidth) / 2;
						int y = 16;
						float healthPercent = Math.Clamp((float)CurrentBoss.CurrentHP / Math.Max(1, CurrentBoss.MaxHP), 0f, 1f);

						// Fond et bordure (léger cadre pour lisibilité)
						Raylib.DrawRectangleRounded(new Rectangle(x - 2, y - 2, barWidth + 4, barHeight + 4), 0.25f, 8, new Color(0, 0, 0, 180));
						Raylib.DrawRectangleRounded(new Rectangle(x, y, barWidth, barHeight), 0.25f, 8, new Color(30, 20, 20, 240));

						// Calculs pour les ailes et le curseur
						int centerX = sw / 2;
						int cursorSize = (int)(barHeight * 2.0f);
						int wingMaxWidth = (barWidth - cursorSize) / 2;

						// Charger (et cacher) le curseur spécifique au boss si disponible
						string speciesLower = CurrentBoss.Species?.ToLower() ?? "";
						Texture2D cursorTex;
						if (!_bossCursorCache.TryGetValue(speciesLower, out cursorTex))
						{
							var loaded = TryLoad($"assets/gui/{speciesLower}_cursor.png");
							cursorTex = loaded.Id != 0 ? loaded : _defaultBossCursorTexture;
							_bossCursorCache[speciesLower] = cursorTex;
						}

						// Ailes : texture classique (base) puis overlay color qui sera rogné selon la vie
						Texture2D wingBase = _healthBarWingTexture;
						Texture2D wingColor = _healthBarWingColorTexture;

						float leftScale = wingBase.Id != 0 ? (float)wingMaxWidth / wingBase.Width : 1f;
						float wingHeight = wingBase.Id != 0 ? wingBase.Height * leftScale : barHeight;
						// Légère descente pour mieux aligner avec la barre
						float destY = y + barHeight/2f - wingHeight/2f + 4;

						// Draw left base (flipped)
						if (wingBase.Id != 0)
						{
							Rectangle src = new Rectangle(wingBase.Width, 0, -wingBase.Width, wingBase.Height);
							Rectangle dest = new Rectangle(centerX - cursorSize/2 - wingMaxWidth, (int)destY, wingMaxWidth, (int)wingHeight);
							Raylib.DrawTexturePro(wingBase, src, dest, Vector2.Zero, 0, Color.White);
						}

						// Draw right base (normal)
						if (wingBase.Id != 0)
						{
							Rectangle srcR = new Rectangle(0, 0, wingBase.Width, wingBase.Height);
							Rectangle destR = new Rectangle(centerX + cursorSize/2, (int)destY, wingMaxWidth, (int)wingHeight);
							Raylib.DrawTexturePro(wingBase, srcR, destR, Vector2.Zero, 0, Color.White);
						}

						// Overlay color: only draw a portion depending on healthPercent
						Color GetBossFrameColor(string species)
						{
							if (string.Equals(species, "Khamsin", StringComparison.OrdinalIgnoreCase))
								return new Color(240, 200, 80, 255);
							if (string.Equals(species, "Ogre", StringComparison.OrdinalIgnoreCase))
								return new Color(200, 150, 120, 255);
							if (string.Equals(species, "Genie", StringComparison.OrdinalIgnoreCase))
								return new Color(255, 195, 60, 255);
							return Color.White;
						}
						Color frameTint = GetBossFrameColor(CurrentBoss.Species);
						if (wingColor.Id != 0 && healthPercent > 0f)
						{
							int colorPx = (int)(wingColor.Width * healthPercent);
							int destColorW = (int)(wingMaxWidth * healthPercent);

							// Right color (grows to the right from cursor)
							if (destColorW > 0)
							{
								Rectangle srcColorR = new Rectangle(0, 0, colorPx, wingColor.Height);
								Rectangle destColorR = new Rectangle(centerX + cursorSize/2, (int)destY, destColorW, (int)wingHeight);
								Raylib.DrawTexturePro(wingColor, srcColorR, destColorR, Vector2.Zero, 0, frameTint);
							}

							// Left color (grows to the left from cursor) - take rightmost slice of source
							if (destColorW > 0)
							{
								Rectangle srcColorL = new Rectangle(wingColor.Width - colorPx, 0, colorPx, wingColor.Height);
								Rectangle destColorL = new Rectangle(centerX - cursorSize/2 - destColorW, (int)destY, destColorW, (int)wingHeight);
								// Draw flipped horizontally by using negative src width
								Rectangle srcColorLFlipped = new Rectangle(wingColor.Width - colorPx + colorPx, 0, -colorPx, wingColor.Height);
								Raylib.DrawTexturePro(wingColor, srcColorLFlipped, destColorL, Vector2.Zero, 0, frameTint);
							}
						}

						// Draw cursor at center
						if (cursorTex.Id != 0)
						{
							float cursorScale = (float)cursorSize / cursorTex.Width;
							Rectangle srcC = new Rectangle(0, 0, cursorTex.Width, cursorTex.Height);
							float centerY = y + barHeight / 2f;
							Rectangle destC = new Rectangle(centerX - cursorSize/2, (int)(centerY - (cursorTex.Height * cursorScale)/2f), cursorSize, (int)(cursorTex.Height * cursorScale));
							Raylib.DrawTexturePro(cursorTex, srcC, destC, Vector2.Zero, 0, Color.White);
						}

						// Label
						string bossName = CurrentBoss.Species == "Khamsin" ? "Khamsin" : (CurrentBoss.Species == "Ogre" ? "Ogre" : (CurrentBoss.Species == "Genie" ? "Génie de la Lampe" : CurrentBoss.Species));
						string bossLabel = $"{bossName}  •  {CurrentBoss.CurrentHP}/{CurrentBoss.MaxHP} HP";
						int textWidth = FontManager.MeasureText(bossLabel, 18);
						FontManager.DrawText(bossLabel, centerX - textWidth/2, y + 5, 18, Color.White);
					}

					// ---- Indicateur du combat du boss de l'oeil (nom + vague, sans barre de vie : ----
					// ---- l'état est désormais visible via les boucliers tournant autour de l'oeil, voir DrawEyeBossShields) ----
					if (_eyeBossActive)
					{
						int sw2 = Raylib.GetScreenWidth();
						string eyeBossLabel = _eyeBossIntroPlaying
							? "L'Oeil s'éveille..."
							: (_eyeBossWaitingNextWave
								? "L'Oeil  •  Vague suivante..."
								: $"L'Oeil  •  Vague {_eyeBossWaveIndex + 1}/{_eyeBossWaves.Length}");
						int eyeTextWidth = FontManager.MeasureText(eyeBossLabel, 18);
						FontManager.DrawText(eyeBossLabel, sw2 / 2 - eyeTextWidth / 2, 16, 18, Color.White);
					}
					
					SlimeStorageUI.Update();
					if (InventoryRenderer.IsInventoryOpen)
					{
						InventoryRenderer.Update();
						InventoryRenderer.Draw(equipment);
					}
					
					//  MULTIJOUEUR : HUD réseau (coin supérieur droit en jeu)
					if (NetworkManager.IsOnline) DrawNetworkHUD();
					
					ContainerUI.Update();
					DyeingUI.Update();
					CauldronUI.Update();
					
					if (PlayerCurrentCar != null)
					{
						DrawCarDashboard(PlayerCurrentCar);
						DrawCarPedals(PlayerCurrentCar);
					}
					else
					{
						Boat? currentBoat = World.GetBoatAtPosition(_playerPos, TileSize);
						if (currentBoat != null)
						{
							int speedKmph = (int)(currentBoat.Velocity.Length() * 3.6f);
							int sailCount = currentBoat.GetSailCount();
							string boatInfo = $" Voiles: {sailCount} | Vitesse: {speedKmph} km/h";
							if (currentBoat.IsSailAdjusted)
								boatInfo += $" | Cap: {currentBoat.SailAngle:F0}°";
							FontManager.DrawText(boatInfo, 10, 70, 14, new Color(100, 200, 255, 255));
						}
					}
					
					if (showDebug) 
						Renderer.DrawDebugMenu(playerPos, camera.Zoom, entities.Count(e=>e.Species=="human"), entities.Count(e=>e.Species!="human"), Raylib.GetFrameTime(), camera, InventoryRenderer.GetHotbarItem(hotbarSlot));
				}
				
				DrawUIBar();

				// Draw remote player pointers overlay (for players offscreen)
				// remotePlayersToRender may be null if offline
				if (remotePlayersToRender != null)
				{
					EnsureServerPointerLoaded();
					PlayerPointerUI.Draw(_currentCamera, remotePlayersToRender, _serverPointerTex);
				}
			}
			ArmorStandUI.Draw();
			TamedAnimalUI.Draw();
			SlimeStorageUI.Draw();
			TraderUI.Draw();
			TamedAnimalInteraction.Draw(camera);
			QuestDialogUI.Draw();
				CartesGameUI.Draw();
			QuestJournalUI.Draw(entities);
			QuestTrackerUI.Draw(camera, GetQuestNpcs());
			CraftingUI.Draw();
			BestiaryUI.Draw();
			ChatSystem.Draw();
				VoiceChat.Draw();
			GuildUI.Draw();
			ContainerUI.Draw();
			CharacterCustomizationUI.Draw();
			CauldronUI.Draw();
			DyeingUI.Draw();
			InstrumentUI.Draw();
				if (_gameState == GameState.Playing || _gameState == GameState.Paused)
					TemperatureSystem.DrawHUD();
			PortalUI.Draw();
			ItemMenuUI.Draw();
			AchievementManager.Draw();
			SpellCraftingUI.Draw();
			DrawSailAdjustmentIndicator(camera);
			
			// ========== DESSIN DE L'ICÔNE DE SAUVEGARDE ==========
			if (_savingDisplayTimer > 0f && _savingIconTexture.Id != 0)
			{
				int sw = Raylib.GetScreenWidth();
				int sh = Raylib.GetScreenHeight();
				
				// Calcul de l'alpha pour l'effet de fondu
				float alpha = 1f;
				if (_savingDisplayTimer < 0.3f)
					alpha = _savingDisplayTimer / 0.3f; // Fondu de sortie
				else if (_savingDisplayTimer > SAVING_DISPLAY_DURATION - 0.3f)
					alpha = (SAVING_DISPLAY_DURATION - _savingDisplayTimer) / 0.3f; // Fondu d'entrée
				
				byte alphaByte = (byte)(alpha * 255);
				Color tint = new Color((byte)255, (byte)255, (byte)255, alphaByte);
				
				// Position : en bas à droite, avec une marge
				float iconSize = 64f;
				float margin = 20f;
				float posX = sw - iconSize - margin;
				float posY = sh - iconSize - margin;
				
				Rectangle srcRect = new Rectangle(0, 0, _savingIconTexture.Width, _savingIconTexture.Height);
				Rectangle destRect = new Rectangle(posX, posY, iconSize, iconSize);
				
				// Ombre portée
				Raylib.DrawRectangleRounded(new Rectangle(posX + 2, posY + 2, iconSize, iconSize), 0.1f, 6, new Color((byte)0, (byte)0, (byte)0, (byte)(80 * alpha)));
				
				// Icône
				Raylib.DrawTexturePro(_savingIconTexture, srcRect, destRect, Vector2.Zero, 0, tint);
				
				// Petit effet de pulsation
				float pulse = 1f + 0.05f * MathF.Sin((float)Raylib.GetTime() * 8f);
				// On ne modifie pas la taille mais on peut ajouter une légère lueur
				if (alpha > 0.8f)
				{
					Raylib.DrawCircleGradient((int)(posX + iconSize/2), (int)(posY + iconSize/2), (int)(iconSize * 0.6f),
						new Color((byte)255, (byte)255, (byte)255, (byte)(20 * alpha)),
						new Color(255, 255, 255, 0));
				}
			}
			
			
			if (_caveTransitionPhase != CaveTransitionPhase.None)
			{
				DrawCaveTransitionBars();
			}

			if (_mainMenuFadePhase != MainMenuFadePhase.None)
			{
				DrawMainMenuTransitionOverlay();
			}
			else if (_fadeOutOverlayActive)
			{
				DrawLoadingFadeOutOverlay();
			}

			// Bandes cinématiques de l'introduction (matérialisation du personnage + rétractation).
			// Pendant la cinématique d'arrivée, elles sont dessinées par DrawSoulArrivalCinematic.
			if (!_isSoulArrivalCinematic)
				DrawSoulIntroBars();

			QuestSplashUI.Draw();

			//  ÉCRAN DE MORT : dessiné en tout dernier, par-dessus absolument tout le reste
			// (lumière jour/nuit, température, HUD, notifications, fade...) puisque c'est une
			// interface qui doit rester lisible quel que soit l'éclairage de la scène.
			if (IsPlayerRagdolled)
			{
				DrawDeathScreen();
			}

			if (screenOverlaysStarted)
				PerfStats.EndSection(PerfStats.Section.ScreenOverlays);
			Raylib.EndDrawing();
			PerfStats.EndSection(PerfStats.Section.FrameDraw);
		}

    }
}
