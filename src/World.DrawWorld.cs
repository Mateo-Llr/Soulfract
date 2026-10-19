// World.DrawWorld.cs
// Fichier extrait de World.Core.cs — classe partielle World.
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
			// qui peut représenter plusieurs tuiles (yOffset = -roofHeightTiles * ts pour un
			// toit, DrawOffset pour un arbre etc.). Avec une marge d'1 seule tuile, dès que la
			// tuile d'ancrage sort de la fenêtre visible (typiquement en bas de l'écran), l'objet
			// entier disparaît instantanément — alors qu'une partie de son sprite (le haut de
			// l'arbre, le bas du toit) devrait encore être visible à l'écran. On élargit donc la
			// zone de tuiles VALIDÉES (celle qui alimente les tableaux heights/objectIds/...)
			// bien au-delà du strict cadrage caméra, pour que ces tuiles d'ancrage restent prises
			// en compte et leurs objets dessinés jusqu'à ce qu'ils quittent réellement l'écran.
			// Coût : quelques dizaines de tuiles de plus à traiter par frame, négligeable face au
			// gain visuel (pas de pop-out brutal), et toujours borné par MAX_RENDER_TILES_X/Y.
			const int TALL_FEATURE_MARGIN_X = 3;
			const int TALL_FEATURE_MARGIN_Y = (MAX_HEIGHT_LEVELS + 3) / 4 > 8
				? (MAX_HEIGHT_LEVELS + 3) / 4
				: 8;

			int startX = (int)Math.Floor(topLeft.X / ts) - 1 - TALL_FEATURE_MARGIN_X;
			int endX = (int)Math.Ceiling(bottomRight.X / ts) + 1 + TALL_FEATURE_MARGIN_X;
			// L'altitude ne peut que remonter le dessin : une tuile ancrée très au-dessus
			// de l'écran ne peut donc pas redevenir visible. La grande marge ne sert qu'en bas,
			// pour les tuiles hautes dont l'ancre est sous le bord inférieur.
			int startY = (int)Math.Floor(topLeft.Y / ts) - 1;
			int endY = (int)Math.Ceiling(bottomRight.Y / ts) + 1 + TALL_FEATURE_MARGIN_Y;

			float entityCullMargin = ts * 2f;
			bool IsEntityNearScreen(Vector2 pos)
			{
				if (pos.X < topLeft.X - entityCullMargin || pos.X > bottomRight.X + entityCullMargin)
					return false;

				float minY = topLeft.Y - entityCullMargin;
				float maxY = bottomRight.Y + entityCullMargin;
				if (pos.Y >= minY && pos.Y <= maxY)
					return true;

				float renderedY = pos.Y - GetGroundItemHeightLift(pos.X, pos.Y);
				return renderedY >= minY && renderedY <= maxY;
			}

			bool IsSegmentNearScreen(Vector2 start, Vector2 end)
			{
				return MathF.Max(start.X, end.X) >= topLeft.X - entityCullMargin &&
					MathF.Min(start.X, end.X) <= bottomRight.X + entityCullMargin &&
					MathF.Max(start.Y, end.Y) >= topLeft.Y - entityCullMargin &&
					MathF.Min(start.Y, end.Y) <= bottomRight.Y + entityCullMargin;
			}

			bool IsWorldRectVisible(float x, float y, float rectWidth, float rectHeight)
			{
				return x + rectWidth > topLeft.X && x < bottomRight.X
					&& y + rectHeight > topLeft.Y && y < bottomRight.Y;
			}

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
			int[,] heights = _renderHeights;
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
			PerfStats.EndSection(PerfStats.Section.WorldDrawSetup);
			PerfStats.StartSection(PerfStats.Section.TileGridFill);
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
			PerfStats.EndSection(PerfStats.Section.TileGridFill);
			PerfStats.StartSection(PerfStats.Section.TerrainDraw);

			Texture2D cliffDownTex = GetCliffDownTexture(missingTex);
			Texture2D cliffBorderTex = GetCliffBorderTexture(missingTex);

			if (_isUnderground)
				Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 100));
			if (_isPlayerIndoors)
				Raylib.ClearBackground(Color.Black);
			
			if (false)
			{
				// ========== RENDU SOUS-MARIN ==========
				int playerTileX = (int)(playerPos.X / ts);
				int playerTileY = (int)(playerPos.Y / ts);
				
				// Effacer l'écran avec la couleur de l'eau
				Raylib.ClearBackground(new Color(10, 30, 60, 255));
				
				// Fond d'eau sombre
				Raylib.DrawRectangle(0, 0, sw, sh, new Color(10, 30, 60, 255));
				
				// Dessiner le fond marin
				for (int x = startX; x <= endX; x++)
				{
					for (int y = startY; y <= endY; y++)
					{
						int floorDepth = GetSeaFloorDepth(x, y);
						float distToFloor = Math.Max(0f, floorDepth - playerDepth);
						float visibility = Math.Clamp(1f - (distToFloor / 32f), 0f, 1f);
						visibility = (float)Math.Pow(visibility, 0.8f);
						
						if (visibility > 0.02f)
						{
							int seaFloorId = GetUnderwaterChunkSeaFloor(x, y);
							Texture2D tex = GetTileTextureForSeaFloor(x, y, seaFloorId, currentTime);
							if (tex.Id == 0 && missingTex.Id != 0) tex = missingTex;
							
							if (tex.Id != 0)
							{
								int drawY = (int)(y * ts - (playerDepth * 3f));
								
								float depthFactor = Math.Clamp(playerDepth / 40f, 0f, 1f);
								Color tint = new Color(
									(byte)(60 + (40 * (1f - depthFactor))),
									(byte)(80 + (60 * (1f - depthFactor))),
									(byte)(150 + (105 * depthFactor)),
									(byte)(255 * visibility)
								);
								
								Raylib.DrawTexturePro(tex,
									new Rectangle(0, 0, tex.Width, tex.Height),
									new Rectangle(x * ts, drawY, ts, ts),
									Vector2.Zero, 0, tint);
							}
						}
					}
				}
				
				// ========== ENTITÉS SOUS-MARINES ==========
				if (_renderItemsPoolInUse)
					_renderItemsPool.Clear();
				else
					_renderItemsPoolInUse = true;
				var underwaterRenderItems = _renderItemsPool;

				// Ajouter les entités visibles sous l'eau
				foreach (var entity in entities)
				{
					if (!entity.IsAlive) continue;
					if (Program.MountedAnimal == entity) continue; // dessinée avec le joueur (voir plus bas), pour l'ordre de superposition
					if (entity.IsMounted && entity.Rider != null) continue; // monture PNJ dessinée via le cavalier
					if (!IsEntityNearScreen(entity.WorldPos))
						continue;
					float entityYOffset = -playerDepth * 3f;
				
					var e = entity;
					underwaterRenderItems.Add(new RenderItem
					{
						Y = e.FeetPos.Y + entityYOffset,
						DrawAction = () =>
						{
								Vector2 drawPos = new Vector2(e.WorldPos.X, e.WorldPos.Y + entityYOffset - e.VisualOffsetY);
							Texture2D hairBaseTex = hairBase.Count > e.HairStyle ? hairBase[e.HairStyle] : new Texture2D();
					Texture2D hairOverlayTex = hairOverlay.Count > e.HairStyle ? hairOverlay[e.HairStyle] : new Texture2D();
					Entity? mount = e.IsRidingMount && e.OwnedMount != null && e.OwnedMount.IsMounted && e.OwnedMount.Rider == e ? e.OwnedMount : null;
					EntityRenderer.DrawEntity(
						e.Species, e.AnimState, e.CurrentFrame, e.AnimProgress,
						e.Facing, drawPos, e.Tint,
						hairBaseTex, hairOverlayTex, e.HairColor,
						SpeciesData.Skeletons,
						eyes: EntityRenderer.GetEyesTextureForSpecies(e.Species), mouth: e.Species == "human" ? Program.MouthTexture : default, forceBlinking: e.IsBlinking,
						customScale: e.Scale,
						equipment: null,
						isCarrying: false, inWater: true,
						attackSwingProgress: e.GetAttackSwingProgress(),
						flashWhite: e.DamageFlash > 0f,
						randomFeatureVariant: e.RandomFeatureVariant,
						randomFeatureColor: e.RandomFeatureColor,
						slimeStoredItem: e.IsSlime ? e.GetSlimeStorageSlot().Item : null,
						beardStyle: e.BeardStyle,
						shadowGroundOffset: e.VisualOffsetY,
						drawMount: mount != null ? () =>
						{
							var m = mount;
							if (m == null || !m.IsAlive) return;
							Texture2D mHairBase = hairBase.Count > m.HairStyle ? hairBase[m.HairStyle] : new Texture2D();
							Texture2D mHairOverlay = hairOverlay.Count > m.HairStyle ? hairOverlay[m.HairStyle] : new Texture2D();
							EntityRenderer.DrawEntity(
								m.Species, m.AnimState, m.CurrentFrame, m.AnimProgress,
								m.Facing, m.WorldPos, m.Tint,
								mHairBase, mHairOverlay, m.HairColor,
								SpeciesData.Skeletons,
								eyes: EntityRenderer.GetEyesTextureForSpecies(m.Species),
								mouth: Program.GetMouthTextureFor(m),
								forceBlinking: m.IsBlinking,
								customScale: m.Scale,
								equipment: m.Equipment,
								isCarrying: false, inWater: m.IsInWater,
								attackSwingProgress: m.GetAttackSwingProgress(),
								flashWhite: m.DamageFlash > 0f,
								pupilLeftOffset: m.PupilLeftOffset,
								pupilRightOffset: m.PupilRightOffset,
								eyebrowLeftOffset: m.EyebrowLeftOffset,
								eyebrowRightOffset: m.EyebrowRightOffset,
								randomFeatureVariant: m.RandomFeatureVariant,
								randomFeatureColor: m.RandomFeatureColor,
								beardStyle: m.BeardStyle,
								blendKey: m);
						} : null,
						blendKey: e);
						}
					});
				}

				// Joueurs locaux supplémentaires (sous l'eau)
				if (playersToRender != null)
				{
					foreach (var extraPlayer in playersToRender)
					{
						if (!extraPlayer.Connected) continue;
						if (!IsEntityNearScreen(extraPlayer.Position)) continue;
						float extraYOffset = -playerDepth * 3f;
						Vector2 extraDrawPos = new Vector2(extraPlayer.Position.X, extraPlayer.Position.Y + extraYOffset);
						float extraSortY = extraDrawPos.Y + Program.FeetOffsetY;
						var capturedExtraPlayer = extraPlayer;
						object capturedExtraPoseBlendKey = capturedExtraPlayer.Id >= 0
							? EntityRenderer.GetStablePoseBlendKey($"player:{capturedExtraPlayer.Id}")
							: capturedExtraPlayer;
						var capturedExtraDrawPos = extraDrawPos;
						var capturedExtraSkin = extraPlayer.SkinColorOverride ?? pSkin;
						var capturedExtraHairColor = extraPlayer.HairColorOverride ?? Program.HairColor;
						var capturedExtraEyes = Program.EyesTexture;
						var capturedExtraMouth = Program.MouthTexture;
						var capturedExtraEquipment = extraPlayer.IsNetworkPlayer
						? Program.GetNetworkPlayerEquipmentFull(extraPlayer.Id, extraPlayer.Equip)
						: Program.equipment;
						var capturedExtraUnderwear = Program.LeafUnderpantsTexture;
						var capturedHairBase = hairBase;
						var capturedHairOverlay = hairOverlay;
						//  Rotation de tête et regard propres à CE joueur distant (sinon tous les
						// joueurs distants copiaient la tête/les yeux du joueur qui regarde).
						float capturedExtraHeadAngle = extraPlayer.IsNetworkPlayer ? extraPlayer.HeadAngle : headAngle;
						var capturedExtraGaze = extraPlayer.IsNetworkPlayer
							? Program.ComputeGazeOffsetsFor(capturedExtraDrawPos, extraPlayer.LookWorldPos, capturedExtraPlayer.Facing)
							: (Program.PupilLeftOffset, Program.PupilRightOffset, Program.EyebrowLeftOffset, Program.EyebrowRightOffset);
						underwaterRenderItems.Add(new RenderItem
						{
							Y = extraSortY,
							DrawAction = () =>
							{
								int hairIndex = capturedExtraPlayer.HairStyleOverride ?? Program.PlayerHairStyle;
								if (hairIndex < 0 || hairIndex >= capturedHairBase.Count) hairIndex = 0;
								Texture2D hairBaseTex = capturedHairBase.Count > 0 ? capturedHairBase[hairIndex] : new Texture2D();
								Texture2D hairOverlayTex = capturedHairOverlay.Count > 0 ? capturedHairOverlay[hairIndex] : new Texture2D();
								Texture2D extraHeldItemTex = EntityRenderer.GetHeldItemTextureById(capturedExtraPlayer.HeldItemId);
								EntityRenderer.DrawEntity(
									capturedExtraPlayer.SpeciesOverride ?? "human",
									capturedExtraPlayer.AnimState,
									capturedExtraPlayer.AnimFrame,
									capturedExtraPlayer.AnimProg,
									capturedExtraPlayer.Facing,
									capturedExtraDrawPos,
									capturedExtraSkin,
									hairBaseTex,
									hairOverlayTex,
									capturedExtraHairColor,
									SpeciesData.Skeletons,
									capturedExtraEyes,
									capturedExtraMouth,
									customScale: 1.0f,
									equipment: capturedExtraEquipment,
									isCarrying: false,
									inWater: true,
									attackSwingProgress: capturedExtraPlayer.AttackSwingProgress,
									heldItemTexture: extraHeldItemTex,
									headAngle: capturedExtraHeadAngle,
									keepItemHorizontal: false,
									isBow: false,
									underwearTexture: capturedExtraUnderwear,
									beardStyle: capturedExtraPlayer.BeardStyleOverride ?? 0,
									eyeStyleOverride: capturedExtraPlayer.EyeStyleOverride,
									eyeColorOverride: capturedExtraPlayer.EyeColorOverride,
									pupilLeftOffset: capturedExtraGaze.Item1,
									pupilRightOffset: capturedExtraGaze.Item2,
									eyebrowLeftOffset: capturedExtraGaze.Item3,
									eyebrowRightOffset: capturedExtraGaze.Item4,
									blendKey: capturedExtraPoseBlendKey);
							}
						});
					}
				}

				// Ajouter le joueur
				if (!Program.isCarMode)
				{
					float playerYOffset = -playerDepth * 3f;
					
					Item? heldItem = null;
					var hotbarSlots = Program.GetHotbarSlotsCached(); //  OPTIM : cache par frame
					if (Program.hotbarSlot < hotbarSlots.Count && !hotbarSlots[Program.hotbarSlot].IsEmpty)
						heldItem = hotbarSlots[Program.hotbarSlot].Item;
					
					int hairIndex = Program.PlayerHairStyle;
					if (hairIndex < 0 || hairIndex >= hairBase.Count) hairIndex = 0;
					var playerHairBaseTex = hairBase.Count > 0 ? hairBase[hairIndex] : new Texture2D();
					var playerHairOverlayTex = hairOverlay.Count > 0 ? hairOverlay[hairIndex] : new Texture2D();
					
					float underwaterElevationOffset = IsUnderwater ? -Program.GetUnderwaterElevation() : 0f;
					Vector2 actualDrawPos = new Vector2(playerPos.X, playerPos.Y + playerYOffset + underwaterElevationOffset);
					float playerSortY = actualDrawPos.Y + Program.FeetOffsetY;
					
					Texture2D heldItemTex = default;
					string heldItemAnchor = "rarmbottom";
					if (heldItem != null)
					{
						var heldItemInfo = EntityRenderer.GetHeldItemTextureInfo(heldItem);
						heldItemTex = heldItemInfo.Texture;
						heldItemAnchor = heldItemInfo.AnchorPart;
					}

					var mountedAnimal = Program.MountedAnimal;
					// Le sens du cavalier suit toujours celui de la monture.
					float underwaterPlayerFacing = mountedAnimal != null ? mountedAnimal.Facing : facing;

					underwaterRenderItems.Add(new RenderItem
					{
						Y = playerSortY,
						DrawAction = () =>
						{
							// Ombre sur le fond
							int currentFloorDepth = GetSeaFloorDepth(playerTileX, playerTileY);
							float distToFloor = currentFloorDepth - playerDepth;
							if (distToFloor < 8f)
							{
								Raylib.DrawEllipse((int)actualDrawPos.X, (int)(actualDrawPos.Y + Program.FeetOffsetY), 20, 6, new Color(0,0,0,60));
							}
							
							EntityRenderer.DrawEntity(
								"human", anim, frame, prog, underwaterPlayerFacing,
								actualDrawPos, pSkin,
								playerHairBaseTex, playerHairOverlayTex, Program.HairColor,
								SpeciesData.Skeletons, Program.EyesTexture, Program.MouthTexture,
								customScale: 1.0f, equipment: Program.equipment,
								isCarrying: false, inWater: true,
								attackSwingProgress: Program.GetAttackSwingProgress(),
								heldItemTexture: heldItemTex,
								headAngle: headAngle,
								keepItemHorizontal: false,
								isBow: false,
								underwearTexture: Program.LeafUnderpantsTexture,
								drawMount: mountedAnimal != null ? () =>
								{
									var m = mountedAnimal;
									if (!m.IsAlive) return;
									Texture2D mHairBase = hairBase.Count > m.HairStyle ? hairBase[m.HairStyle] : new Texture2D();
									Texture2D mHairOverlay = hairOverlay.Count > m.HairStyle ? hairOverlay[m.HairStyle] : new Texture2D();
									EntityRenderer.DrawEntity(
										m.Species, m.AnimState, m.CurrentFrame, m.AnimProgress,
										m.Facing, m.WorldPos, m.Tint,
										mHairBase, mHairOverlay, m.HairColor,
										SpeciesData.Skeletons,
										eyes: EntityRenderer.GetEyesTextureForSpecies(m.Species),
										mouth: Program.GetMouthTextureFor(m), forceBlinking: m.IsBlinking,
										customScale: m.Scale,
										equipment: m.Equipment,
										isCarrying: false, inWater: m.IsInWater,
										attackSwingProgress: 0f,
										flashWhite: m.DamageFlash > 0f,
										pupilLeftOffset: m.PupilLeftOffset,
										pupilRightOffset: m.PupilRightOffset,
										eyebrowLeftOffset: m.EyebrowLeftOffset,
										eyebrowRightOffset: m.EyebrowRightOffset,
										randomFeatureVariant: m.RandomFeatureVariant,
										randomFeatureColor: m.RandomFeatureColor,
										beardStyle: m.BeardStyle,
										blendKey: m);
								} : null,
								blendKey: Program.PlayerPoseBlendKey);
						}
					});
				}

				// Tri et dessin des entités
				//  Même correctif que pour le rendu de surface : départage stable des égalités de Y
				// par index d'insertion, pour éviter le clignotement de sprites à Y identique.
				for (int ri = 0; ri < underwaterRenderItems.Count; ri++)
				{
					var tmp = underwaterRenderItems[ri];
					tmp.SortIndex = ri;
					underwaterRenderItems[ri] = tmp;
				}
				underwaterRenderItems.Sort((a, b) =>
				{
					int c = a.Y.CompareTo(b.Y);
					if (c != 0) return c;
					return a.SortIndex.CompareTo(b.SortIndex);
				});
				foreach (var item in underwaterRenderItems) item.DrawAction();
				_renderItemsPool = underwaterRenderItems;

				// Voile d'eau par-dessus les entités
				byte waterAlpha = (byte)(Math.Clamp(playerDepth * 3, 20, 220));
				Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)20, (byte)60, (byte)100, waterAlpha));
				
				// Obscurité supplémentaire en grande profondeur
				if (playerDepth > 30f)
				{
					byte deepAlpha = (byte)(Math.Clamp((playerDepth - 30f) * 4, 0, 100));
					Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)0, (byte)0, (byte)10, deepAlpha));
				}
				
				// Particules sous-marines
				float particleChance = playerDepth * 0.02f;
				if (Random.Shared.NextDouble() < particleChance)
				{
					int px = Random.Shared.Next(0, sw);
					int py = Random.Shared.Next(0, sh);
					int pSize = Random.Shared.Next(1, 3);
					byte pAlpha = (byte)Random.Shared.Next(30, 100);
					Raylib.DrawCircle(px, py, pSize, new Color((byte)200, (byte)220, (byte)255, pAlpha));
				}
				
				// Interface sous-marine
				string depthText = $"Profondeur: {playerDepth:F1}m";
				FontManager.DrawText(depthText, 10, 10, 18, new Color(100, 200, 255, 255));
				
				string helpText = "ESPACE: Nager vers le haut | WASD: Se deplacer";
				FontManager.DrawText(helpText, 10, 35, 14, new Color(150, 200, 255, 200));
				
				// Indicateur de proximite du fond
				int currentFloorDepth = GetSeaFloorDepth(playerTileX, playerTileY);
				float distToCurrentFloor = currentFloorDepth - playerDepth;
				if (distToCurrentFloor < 10f && distToCurrentFloor > 0f)
				{
					string floorText = $"Fond: {distToCurrentFloor:F1}m";
					FontManager.DrawText(floorText, 10, 60, 14, new Color(200, 200, 150, 200));
				}

				return;
			}
			// ========== FIN DU RENDU SOUS-MARIN ==========

			// Shader d'eau (caustiques procédurales) : horloge mise à jour une fois par
			// frame, avant la boucle de tuiles qui va s'en servir pour chaque tuile d'eau.
			UpdateWaterShaderTime(currentTime, playerPos, isPlayerInWater, entities);
			_renderBiomeBorders.Clear();

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
					bool tileDrawVisible = IsWorldRectVisible(worldX * ts, drawY, ts, ts);
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
							// procédural (voir World.Water.cs / shaders/water.fs) : deux
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
								if (IsWorldRectVisible(worldX * ts, cliffY, ts, diff * (ts / 4)))
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
								if (IsWorldRectVisible(worldX * ts, cliffY, ts / 4f, ts))
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
								if (IsWorldRectVisible(worldX * ts, cliffY, ts / 4f, ts))
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
								if (IsWorldRectVisible(worldX * ts + ts - ts / 4f, cliffY, ts / 4f, ts))
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
					if (!IsWorldRectVisible(boatDrawX, boatDrawY, ts, ts))
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

			// =================================================================================
			// 3. INITIALISATION DE LA LISTE DE RENDU (RenderItems)
			// =================================================================================
			if (_renderItemsPoolInUse)
				_renderItemsPool.Clear();
			else
				_renderItemsPoolInUse = true;
			var renderItems = _renderItemsPool;
			PerfStats.EndSection(PerfStats.Section.TerrainDraw);
			PerfStats.StartSection(PerfStats.Section.WorldRenderPrepare);

			// =================================================================================
			// 4. ICÔNES DES PROPRIÉTAIRES DE MAISON SUR LES PORTES
			// =================================================================================
			AddHouseResidentDoorIcons(renderItems, entities,
				new Rectangle(topLeft.X - 160f, topLeft.Y - 160f,
					(bottomRight.X - topLeft.X) + 320f, (bottomRight.Y - topLeft.Y) + 320f));

			// =================================================================================
			// 5. ITEMS AU SOL
			// =================================================================================
			foreach (var gi in Program.GroundItems)
			{
				if (!Program.IsTileLit((int)(gi.Position.X / ts), (int)(gi.Position.Y / ts)))
					continue;
				if (!GameData.ItemDatabase.TryGetValue(gi.ItemId, out var itemData))
					continue;
				const float itemSize = 28f;
				Color itemColor = gi.CustomColor.HasValue ? gi.CustomColor.Value : itemData.Color;
				bool isLootbag = gi.ItemId == GameData.GetItemId("basket_wicker") || !string.IsNullOrWhiteSpace(gi.LootbagPayload);
				float shadowWidth = isLootbag && Program.LootbagTexture.Id != 0
					? ItemRenderer.GetTextureAlphaWidth(Program.LootbagTexture, "lootbag") * itemSize
					: ItemRenderer.GetItemAlphaWidth(itemData) * itemSize;
				float alphaBottom = isLootbag && Program.LootbagTexture.Id != 0
					? ItemRenderer.GetTextureAlphaBottom(Program.LootbagTexture, "lootbag")
					: ItemRenderer.GetItemAlphaBottom(itemData);
				float groundLift = GetGroundItemHeightLift(gi.Position.X, gi.Position.Y);
				float shadowY = gi.Position.Y + 5f - groundLift;
				Vector2 drawPos = new Vector2(gi.Position.X, shadowY - gi.Height + itemSize / 2f - alphaBottom * itemSize);
				float itemRenderWidth = MathF.Max(itemSize, shadowWidth);
				float itemRenderTop = MathF.Min(drawPos.Y, shadowY - 4f);
				if (!IsWorldRectVisible(gi.Position.X - itemRenderWidth / 2f, itemRenderTop,
					itemRenderWidth, drawPos.Y + itemSize - itemRenderTop))
					continue;
				
				renderItems.Add(new RenderItem
				{
					Y = gi.Position.Y - groundLift,
					Kind = RenderItemKind.GroundItem,
					Tag = new GroundItemRenderTag(gi, itemData, itemColor, isLootbag, shadowWidth, alphaBottom, gi.GroundVelocity.X * 0.5f, drawPos, itemSize, gi.Metadata, gi.CustomColor)
				});
			}

			//  MULTIJOUEUR : côté client, Program.GroundItems reste vide (le host est
			// seul propriétaire de la liste) ; on affiche à la place le dernier instantané
			// reçu du réseau.
			if (NetworkManager.IsClient)
			{
				foreach (var ngi in NetworkManager.GetRemoteGroundItems())
				{
					if (!Program.IsTileLit((int)(ngi.PosX / ts), (int)(ngi.PosY / ts)))
						continue;
					if (!GameData.ItemDatabase.TryGetValue(ngi.ItemId, out var netItemData)) continue;
					Color? netCustomColor = (ngi.Colors != null && ngi.Colors.Count > 0 && ngi.Colors[0] != null)
						? new Color(ngi.Colors[0]!.R, ngi.Colors[0]!.G, ngi.Colors[0]!.B, ngi.Colors[0]!.A)
						: (Color?)null;
					Color netItemColor = netCustomColor ?? netItemData.Color;
					const float itemSize = 28f;
					bool isLootbag = ngi.ItemId == GameData.GetItemId("basket_wicker");
					float shadowWidth = isLootbag && Program.LootbagTexture.Id != 0
						? ItemRenderer.GetTextureAlphaWidth(Program.LootbagTexture, "lootbag") * itemSize
						: ItemRenderer.GetItemAlphaWidth(netItemData) * itemSize;
					float alphaBottom = isLootbag && Program.LootbagTexture.Id != 0
						? ItemRenderer.GetTextureAlphaBottom(Program.LootbagTexture, "lootbag")
						: ItemRenderer.GetItemAlphaBottom(netItemData);
					float netLift = GetGroundItemHeightLift(ngi.PosX, ngi.PosY);
					float shadowY = ngi.PosY + 5f - netLift;
					Vector2 drawPos = new Vector2(ngi.PosX, shadowY + itemSize / 2f - alphaBottom * itemSize);
					float itemRenderWidth = MathF.Max(itemSize, shadowWidth);
					float itemRenderTop = MathF.Min(drawPos.Y, shadowY - 4f);
					if (!IsWorldRectVisible(ngi.PosX - itemRenderWidth / 2f, itemRenderTop,
						itemRenderWidth, drawPos.Y + itemSize - itemRenderTop))
						continue;

					renderItems.Add(new RenderItem
					{
						Y = ngi.PosY - netLift,
						DrawAction = () =>
						{
							float size = itemSize;
							Raylib.DrawEllipse((int)ngi.PosX, (int)shadowY, shadowWidth / 2f, 4, new Color(0, 0, 0, 80));

							if (isLootbag && Program.LootbagTexture.Id != 0)
							{
								Rectangle srcRect = new Rectangle(0, 0, Program.LootbagTexture.Width, Program.LootbagTexture.Height);
								Rectangle destRect = new Rectangle(drawPos.X, drawPos.Y, size, size);
								Vector2 origin = new Vector2(size / 2, size / 2);
								Raylib.DrawTexturePro(Program.LootbagTexture, srcRect, destRect, origin, 0, netItemColor);
							}
							else
							{
								//  Dessine TOUTES les couches de l'item, pas seulement la première.
								// Teinte uniquement si l'item a réellement été teint (netCustomColor).
								var netCustomColors = ngi.Colors?.Select(color => color != null
									? (Color?)new Color(color.R, color.G, color.B, color.A)
									: null).ToList();
								ItemRenderer.DrawItemRotated(netItemData, drawPos, size, 0, netCustomColor, ngi.Metadata, netCustomColors);
							}

						}
					});
				}
			}

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
					if (!IsWorldRectVisible(drawX + offX, drawY + offY, drawW, drawH))
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

			// =================================================================================
			// 7. OBJETS DU MONDE (arbres, rochers, maisons, etc.)
			// =================================================================================
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
						int ritualHeight = heights[ix, iy];
						float ritualWx = worldX * ts;
						float ritualWy = worldY * ts - (ritualHeight * ts / 4);
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
							if (!IsWorldRectVisible(drawX, drawY, drawW, drawH))
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

					int height = heights[ix, iy];
					float wx = worldX * ts, wy = worldY * ts - (height * ts / 4);

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
					if (!IsWorldRectVisible(objectDrawX, MathF.Min(objectDrawY, hpBarTop),
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

			// Overlays (toits) si joueur pas à l'intérieur
			{
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
						int height = heights[ix, iy];
						float wx = worldX * ts;
						float wy = worldY * ts - (height * ts / 4);
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
						if (!IsWorldRectVisible(overlayDrawX, overlayTop, overlayDrawWidth,
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
			
			foreach (var car in Program.Cars)
			{
				if (!IsEntityNearScreen(car.Position))
					continue;
				float sortY = car.Position.Y + Program.FeetOffsetY;
				renderItems.Add(new RenderItem
				{
					Y = sortY,
					Kind = RenderItemKind.Car,
					Tag = new CarRenderTag(car)
				});
			}

			if (Program.HasWagonLinkVisuals())
			{
				// Barres d'attelage : triées tout en bas pour passer sous les sprites des wagons.
				renderItems.Add(new RenderItem
				{
					Y = float.MinValue,
					DrawAction = () => Program.DrawWagonLinks()
				});
			}

			if (Program.PlayerCurrentWagonTile.HasValue)
			{
				Vector2 wagonPos = Program.GetMountedWagonDrawPosition();
				if (IsEntityNearScreen(wagonPos))
				{
					renderItems.Add(new RenderItem
					{
						Y = wagonPos.Y + Program.FeetOffsetY - 0.5f,
						DrawAction = () => Program.DrawWagon(
							Program.GetMountedWagonDrawPosition(),
							(int)Program.TileSize,
							Program.IsMountedWagonVertical())
					});
				}
			}

			//  MULTIJOUEUR : côté client, les entités viennent du snapshot du host.
			// IMPORTANT : ce bloc doit être indépendant de la boucle "foreach (var entity in entities)"
			// ci-dessous, car côté client la liste Program.entities est toujours vide (le client ne
			// simule aucune entité) — placé À L'INTÉRIEUR de cette boucle comme avant, ce code ne
			// s'exécutait donc JAMAIS, ce qui rendait les entités totalement invisibles pour les clients.
			//  PERF : avant, GetPersistedQuestsSnapshot() était rappelé pour CHAQUE PNJ visible, chaque frame.
			var trackerQuests = NetworkManager.IsClient
				? null
				: QuestManager.GetPersistedQuestsSnapshot()
					.Where(q => q.State == QuestState.Accepted || q.State == QuestState.Delivered)
					.ToList();

			// PERF : on récupère le snapshot des entités distantes une seule fois par dessin.
			// La recherche de cible de quête est ensuite O(1), au lieu de reparcourir toutes
			// les entités distantes pour chaque PNJ humain affiché.
			var remoteEntitiesSnapshot = NetworkManager.IsClient ? NetworkManager.GetRemoteEntities() : null;
			HashSet<string>? acceptedQuestTargetNetIds = null;
			if (remoteEntitiesSnapshot != null)
			{
				acceptedQuestTargetNetIds = new HashSet<string>(StringComparer.Ordinal);
				foreach (var remoteEntity in remoteEntitiesSnapshot)
				{
					if (remoteEntity.QuestState == (int)QuestState.Accepted
						&& !string.IsNullOrEmpty(remoteEntity.QuestTargetNetId))
						acceptedQuestTargetNetIds.Add(remoteEntity.QuestTargetNetId);
				}
			}

			if (remoteEntitiesSnapshot != null)
			{
				foreach (var dto in remoteEntitiesSnapshot)
				{
					var dtoPos = new Vector2(dto.PosX, dto.PosY);
					int tileX = (int)(dtoPos.X / ts);
					int tileY = (int)(dtoPos.Y / ts);
					if (!Program.IsTileLit(tileX, tileY))
						continue;
					if (!ShouldRenderEntity(dtoPos))
						continue;
					Vector2 dtoVisualPos = new(dtoPos.X, dtoPos.Y - GetHeightAt(tileX, tileY) * ts / 4f);
					if (!IsEntityNearScreen(dtoVisualPos))
						continue;

					var capturedDto = dto;
					var capturedDtoVisualPos = dtoVisualPos;
					object capturedDtoPoseBlendKey = string.IsNullOrEmpty(capturedDto.NetId)
						? capturedDto
						: EntityRenderer.GetStablePoseBlendKey($"entity:{capturedDto.NetId}");
					var capturedHairBase = hairBase;
					var capturedHairOverlay = hairOverlay;
					int hairIdx = capturedDto.HairStyle < capturedHairBase.Count ? capturedDto.HairStyle : 0;
					Color dtoTint = new Color(capturedDto.Tint.R, capturedDto.Tint.G, capturedDto.Tint.B, capturedDto.Tint.A);
					Color dtoHair = new Color(capturedDto.HairColor.R, capturedDto.HairColor.G, capturedDto.HairColor.B, capturedDto.HairColor.A);
					var capturedDtoEquipment = Program.GetNetworkEntityEquipment(capturedDto.NetId, capturedDto.Equip);
					Item? capturedSlimeStoredItem = null;
					if (capturedDto.SlimeStorageSlot != null)
					{
						var storedSlot = new InventorySlot();
						SaveSystem.RestoreInventorySlotFromSave(capturedDto.SlimeStorageSlot, storedSlot);
						capturedSlimeStoredItem = storedSlot.Item;
					}
					renderItems.Add(new RenderItem
					{
						Y = capturedDtoVisualPos.Y + Program.FeetOffsetY,
						DrawAction = () =>
						{
							Texture2D hBase = capturedHairBase.Count > hairIdx ? capturedHairBase[hairIdx] : new Texture2D();
							Texture2D hOver = capturedHairOverlay.Count > hairIdx ? capturedHairOverlay[hairIdx] : new Texture2D();
							EntityRenderer.DrawEntity(
								capturedDto.Species, capturedDto.AnimState, capturedDto.AnimFrame, capturedDto.AnimProg,
								capturedDto.Facing, capturedDtoVisualPos,
								dtoTint, hBase, hOver, dtoHair,
								SpeciesData.Skeletons,
								customScale: capturedDto.Scale,
                                eyes: EntityRenderer.GetEyesTextureForSpecies(capturedDto.Species),
                                equipment: capturedDtoEquipment, isCarrying: false, inWater: false,
								flashWhite: capturedDto.DamageFlash > 0f,
								ghostly: capturedDto.IsSpiritAnimal,
								ghostTrailKey: capturedDto.NetId,
								slimeStoredItem: capturedSlimeStoredItem,
								blendKey: capturedDtoPoseBlendKey);
							float w = Program.TileSize * 1.2f;
							float ratio = 1f;
							int bx = (int)(capturedDto.PosX - w / 2f);
							int by = (int)(capturedDtoVisualPos.Y - Program.TileSize * 0.8f);
							Raylib.DrawRectangle(bx, by, (int)w, 5, new Color(60, 0, 0, 180));
							Raylib.DrawRectangle(bx, by, (int)(w * ratio), 5, new Color(80, 200, 80, 220));
							if (capturedDto.Concussion > 0f && capturedDto.MaxHP > 0)
							{
								float concussionRatio = Math.Clamp(capturedDto.Concussion / capturedDto.MaxHP, 0f, 1f);
								Raylib.DrawRectangle(bx, by - 6, (int)w, 4, new Color(18, 12, 32, 210));
								Raylib.DrawRectangle(bx, by - 6, (int)(w * concussionRatio), 4, new Color(170, 100, 255, 245));
							}
							// Nom personnalisé
							if (!string.IsNullOrEmpty(capturedDto.CustomName))
							{
								int nw = FontManager.MeasureText(capturedDto.CustomName, 13);
								FontManager.DrawText(capturedDto.CustomName,
									(int)capturedDto.PosX - nw / 2, (int)capturedDto.PosY - Program.TileSize - 10,
									13, new Color(220, 220, 100, 220));
							}
						}
					});

					//  MULTIJOUEUR : icônes de commerce/quête/guilde au-dessus de la tête du PNJ,
					// miroir du rendu côté hôte, calculées à partir des données désormais incluses
					// dans le snapshot réseau (EntityDto) plutôt que depuis Program.entities (vide
					// côté client). Sans ceci, un client ne voyait jamais l'icône signalant qu'un
					// PNJ a quelque chose à proposer. Comme côté hôte, c'est un RenderItem SÉPARÉ
					// avec Y = float.MaxValue - 1000 pour être dessiné en dernier (au-dessus de
					// tout le reste) : le mettre dans le même DrawAction que le sprite (Y =
					// PosY, trié avec le décor) le faisait recouvrir par des éléments dessinés
					// après lui, donc jamais visible à l'écran.
					if (capturedDto.Species == "human")
					{
						bool cShowTrader = (capturedDto.IsTrader || capturedDto.Profession != 0) && _traderIcon.Id != 0;
						bool cShowStart = capturedDto.QuestState == (int)QuestState.Offered;
						bool cShowFinish = capturedDto.QuestState == (int)QuestState.Delivered
							|| acceptedQuestTargetNetIds!.Contains(capturedDto.NetId);
						bool cShowQuest = (cShowStart && _questStartIcon.Id != 0) || (cShowFinish && _questFinishIcon.Id != 0);
						bool cShowGuild = (capturedDto.WantsToJoinGuild || capturedDto.Friendship >= 1f) && !capturedDto.IsTamed && !capturedDto.IsGuildMember
							&& Program.PlayerGuild != null && _guildJoinIcon.Id != 0;
						bool cShowTracker = capturedDto.QuestState == (int)QuestState.Accepted || capturedDto.QuestState == (int)QuestState.Delivered;

						var cActiveIcons = new List<Texture2D>();
						if (cShowTrader) cActiveIcons.Add(_traderIcon);
						if (cShowQuest) cActiveIcons.Add(cShowStart ? _questStartIcon : _questFinishIcon);
						if (cShowGuild) cActiveIcons.Add(_guildJoinIcon);
						if (cShowTracker) { var tracker = QuestTrackerUI.GetTrackerTexture(); if (tracker.Id != 0) cActiveIcons.Add(tracker); }

						if (cActiveIcons.Count > 0)
						{
							Vector2 iconBasePos = capturedDtoVisualPos;
							renderItems.Add(new RenderItem
							{
								Y = float.MaxValue - 1000,
								DrawAction = () =>
								{
									float iconSize = cActiveIcons.Count > 1 ? 26f : 28f;
									float yOffsetIcon = -90f;
									float step = 34f;
									int count = cActiveIcons.Count;
									for (int i = 0; i < count; i++)
									{
										Texture2D tex = cActiveIcons[i];
										float xOffset = (i - (count - 1) / 2f) * step;
										Vector2 pos = new Vector2(iconBasePos.X + xOffset, iconBasePos.Y);
										Raylib.DrawTexturePro(tex,
											new Rectangle(0, 0, tex.Width, tex.Height),
											new Rectangle(pos.X - iconSize / 2, pos.Y + yOffsetIcon - iconSize / 2, iconSize, iconSize),
											Vector2.Zero, 0, Color.White);
									}
								}
							});
						}
					}
				}
				// Ne pas dessiner les entités locales côté client (elles sont vides de toute façon)
			}
			else
			// Entités (host / solo) : dessinées depuis la liste Program.entities habituelle.
			foreach (var entity in entities)
			{
				if (!entity.IsAlive) continue;
				if (Program.MountedAnimal == entity) continue; // dessinée avec le joueur (voir plus bas), pour l'ordre de superposition
				if (entity.IsMounted && entity.Rider != null) continue; // monture PNJ dessinée via le cavalier
				Vector2 entityVisualPos = entity.VisualWorldPos;
				if (!ShouldRenderEntity(entity)) continue;
				if (!IsEntityNearScreen(entityVisualPos))
					continue;
				{
				int tileX = (int)(entityVisualPos.X / ts);
				int tileY = (int)(entityVisualPos.Y / ts);
				if (!Program.IsTileLit(tileX, tileY)) continue;
				int height = GetHeightAt(tileX, tileY);
				float yOffset = -height * ts / 4;
				var e = entity;
				bool isTrader = e.IsTrader;
				Vector2 traderIconPos = new Vector2(entityVisualPos.X, entityVisualPos.Y + yOffset - e.VisualOffsetY);
				renderItems.Add(new RenderItem
				{
					Y = entityVisualPos.Y + Program.FeetOffsetY + yOffset,
					DrawAction = () =>
					{
						//  BOSS VER DE TERRE : rendu segmenté dédié (trail), pas le squelette classique.
						// Invisible tant qu'il est sous terre (Underground/Telegraph/Recover) : IsAlive
						// reste true tout du long, seul WormHeadVisible pilote l'affichage.
						if (e.IsWorm)
						{
							EntityRenderer.DrawWormBoss(e, e.Scale);
							return;
						}

						Texture2D hairBaseTex = hairBase.Count > e.HairStyle ? hairBase[e.HairStyle] : new Texture2D();
						Texture2D hairOverlayTex = hairOverlay.Count > e.HairStyle ? hairOverlay[e.HairStyle] : new Texture2D();
						Entity? mount = e.IsRidingMount && e.OwnedMount != null && e.OwnedMount.IsMounted && e.OwnedMount.Rider == e ? e.OwnedMount : null;
						EntityRenderer.DrawEntity(
							e.Species, e.AnimState, e.CurrentFrame, e.AnimProgress,
							e.Facing, new Vector2(entityVisualPos.X, entityVisualPos.Y + yOffset - e.VisualOffsetY), e.Tint,
							hairBaseTex, hairOverlayTex, e.HairColor,
							SpeciesData.Skeletons,
							eyes: EntityRenderer.GetEyesTextureForSpecies(e.Species), mouth: Program.GetMouthTextureFor(e), forceBlinking: e.IsBlinking,
							customScale: e.Scale,  // ← utilisation du scale individuel
							equipment: e.Equipment,
							underwearTexture: Program.LeafUnderpantsTexture,
							isCarrying: false, inWater: e.IsInWater,
							attackSwingProgress: e.GetAttackSwingProgress(),
							flashWhite: e.DamageFlash > 0f,
							pupilLeftOffset: e.PupilLeftOffset,
							pupilRightOffset: e.PupilRightOffset,
							eyebrowLeftOffset: e.EyebrowLeftOffset,
							eyebrowRightOffset: e.EyebrowRightOffset,
							randomFeatureVariant: e.RandomFeatureVariant,
							randomFeatureColor: e.RandomFeatureColor,
							beardStyle: e.BeardStyle,
							shadowGroundOffset: e.VisualOffsetY,
							ghostly: e.IsSpiritAnimal,
							ghostTrailKey: e.NetId.ToString(),
							slimeStoredItem: e.IsSlime ? e.GetSlimeStorageSlot().Item : null,
							blendKey: e,
							drawMount: mount != null ? () =>
							{
								var m = mount;
								if (m == null || !m.IsAlive) return;
								Texture2D mHairBase = hairBase.Count > m.HairStyle ? hairBase[m.HairStyle] : new Texture2D();
								Texture2D mHairOverlay = hairOverlay.Count > m.HairStyle ? hairOverlay[m.HairStyle] : new Texture2D();
								EntityRenderer.DrawEntity(
									m.Species, m.AnimState, m.CurrentFrame, m.AnimProgress,
									m.Facing, m.WorldPos, m.Tint,
									mHairBase, mHairOverlay, m.HairColor,
									SpeciesData.Skeletons,
									eyes: EntityRenderer.GetEyesTextureForSpecies(m.Species),
									mouth: Program.GetMouthTextureFor(m),
									forceBlinking: m.IsBlinking,
									customScale: m.Scale,
									equipment: m.Equipment,
									isCarrying: false, inWater: m.IsInWater,
											attackSwingProgress: m.GetAttackSwingProgress(),
									flashWhite: m.DamageFlash > 0f,
									pupilLeftOffset: m.PupilLeftOffset,
									pupilRightOffset: m.PupilRightOffset,
									eyebrowLeftOffset: m.EyebrowLeftOffset,
									eyebrowRightOffset: m.EyebrowRightOffset,
									randomFeatureVariant: m.RandomFeatureVariant,
									randomFeatureColor: m.RandomFeatureColor,
									beardStyle: m.BeardStyle,
									blendKey: m);
							} : null);
						//  Plus de bulle de dialogue au-dessus de la tête : le PNJ s'arrête,
						// s'oriente vers son interlocuteur (Entity.Update) et "articule" via
						// l'alternance Humanmouth / Humanmouth_open ci-dessus (Program.GetMouthTextureFor).
						if (e.Species != "human" && e.CurrentHP < e.MaxHP)
						{
							float ratio = Math.Clamp((float)e.CurrentHP / Math.Max(1, e.MaxHP), 0f, 1f);
							Vector2 entityDrawPos = new Vector2(e.WorldPos.X, e.WorldPos.Y + yOffset - e.VisualOffsetY);
							float topY = EntityRenderer.GetVisualTopY(
								e.Species, e.AnimState, e.CurrentFrame, e.AnimProgress,
								e.Facing, entityDrawPos, e.Scale, blendKey: e);
							float w = Program.TileSize * 1.35f;
							float h = 7f;
							float bx = e.WorldPos.X - w / 2f;
							float by = topY - 13f;
							Rectangle barRect = new Rectangle(bx, by, w, h);
							Rectangle fillRect = new Rectangle(bx + 1.5f, by + 1.5f, Math.Max(0f, (w - 3f) * ratio), h - 3f);
							Raylib.DrawRectangleRounded(new Rectangle(bx - 2f, by - 2f, w + 4f, h + 4f), 0.45f, 8, new Color(0, 0, 0, 105));
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
						if (e.Concussion > 0f && e.MaxHP > 0)
						{
							Vector2 entityDrawPos = new Vector2(e.WorldPos.X, e.WorldPos.Y + yOffset - e.VisualOffsetY);
							float topY = EntityRenderer.GetVisualTopY(
								e.Species, e.AnimState, e.CurrentFrame, e.AnimProgress,
								e.Facing, entityDrawPos, e.Scale, blendKey: e);
							float w = Program.TileSize * 1.35f;
							float h = 5f;
							float bx = e.WorldPos.X - w / 2f;
							float by = topY - (e.CurrentHP < e.MaxHP && e.Species != "human" ? 23f : 13f);
							float ratio = Math.Clamp(e.Concussion / e.MaxHP, 0f, 1f);
							Raylib.DrawRectangleRounded(new Rectangle(bx, by, w, h), 0.45f, 8, new Color(18, 12, 32, 210));
							Raylib.DrawRectangleRounded(
								new Rectangle(bx + 1f, by + 1f, Math.Max(0f, (w - 2f) * ratio), h - 2f),
								0.35f, 6, new Color(170, 100, 255, 245));
						}
					}
				});
				//  Icônes au-dessus de la tête du PNJ (commerce ET quête) : on calcule d'abord
				// lesquelles sont actives afin de les positionner côte à côte plutôt que superposées.
				bool showTraderIcon = (isTrader || e.HasProfession) && _traderIcon.Id != 0;

				bool showStart = false, showFinish = false;
				if (e.IsVillager)
				{
					// 1) Ce PNJ propose une quête (pas encore acceptée) -> quest_start.png
					showStart = e.ActiveQuest != null && e.ActiveQuest.State == QuestState.Offered;

					// 2) Ce PNJ est la cible d'une quête acceptée par le joueur -> quest_finish.png
					var deliveryQuest = QuestManager.GetQuestToDeliverFor(e, entities);
					showFinish = deliveryQuest != null;

					// 3) Ce PNJ est le donneur d'une quête livrée -> aussi quest_finish.png pour indiquer la validation
					var rewardQuest = QuestManager.GetIncomingQuestFor(e, entities);
					showFinish = showFinish || rewardQuest != null;
				}
				bool showQuestIcon = (showStart && _questStartIcon.Id != 0) || (showFinish && _questFinishIcon.Id != 0);

				// 4) Ce PNJ propose de rejoindre la guilde du joueur (amitié maximale) -> guild_joinable.png.
				// Traité exactement comme les icônes de commerce/quête : même hauteur, même style
				// d'affichage côte à côte, et acceptation via la bulle de dialogue (pas de clic droit direct).
				bool showGuildIcon = e.IsVillager && GuildRecruitManager.CanAccept(e)
					&& Program.PlayerGuild != null && _guildJoinIcon.Id != 0;
				bool showTrackerIcon = false;
				foreach (var quest in trackerQuests!)
				{
					if (quest.State == QuestState.Accepted && quest.TargetId == e.NetId)
					{
						showTrackerIcon = true;
						break;
					}
					if (quest.State == QuestState.Delivered && quest.GiverId == e.NetId)
					{
						showTrackerIcon = true;
						break;
					}
				}

				//  On regroupe toutes les icônes actives (commerce / quête / guilde / traqueur) et on les
				// positionne côte à côte au-dessus de la tête, plutôt que de les superposer.
				var activeIcons = new List<Texture2D>();
				if (showTraderIcon) activeIcons.Add(_traderIcon);
				if (showQuestIcon) activeIcons.Add(showStart ? _questStartIcon : _questFinishIcon);
				if (showGuildIcon) activeIcons.Add(_guildJoinIcon);
				if (showTrackerIcon) { var trackerTex = QuestTrackerUI.GetTrackerTexture(); if (trackerTex.Id != 0) activeIcons.Add(trackerTex); }

				if (activeIcons.Count > 0)
				{
					float iconSize = activeIcons.Count > 1 ? 26f : 28f;
					float yOffsetIcon = -90f;
					float step = 34f; // distance entre les centres de deux icônes voisines
					Vector2 basePos = traderIconPos;
					int count = activeIcons.Count;

					renderItems.Add(new RenderItem
					{
						Y = float.MaxValue - 1000,
						DrawAction = () =>
						{
							for (int i = 0; i < count; i++)
							{
								Texture2D tex = activeIcons[i];
								float xOffset = (i - (count - 1) / 2f) * step;
								Vector2 pos = new Vector2(basePos.X + xOffset, basePos.Y);
								Raylib.DrawTexturePro(tex,
									new Rectangle(0, 0, tex.Width, tex.Height),
									new Rectangle(pos.X - iconSize/2, pos.Y + yOffsetIcon - iconSize/2, iconSize, iconSize),
									Vector2.Zero, 0, Color.White);
							}
						}
					});
				}
				if (e.ShowAlertIcon && _alertIcon.Id != 0)
				{
					Vector2 iconWorldPos = new Vector2(e.WorldPos.X, e.WorldPos.Y + yOffset - e.VisualOffsetY);
					renderItems.Add(new RenderItem
					{
						Y = float.MaxValue - 900,
						DrawAction = () =>
						{
							float iconSize = 34f;
							float yOffsetIcon = -110f;
							Rectangle srcRect = new Rectangle(0, 0, _alertIcon.Width, _alertIcon.Height);
							Rectangle destRect = new Rectangle(
								(int)(iconWorldPos.X - iconSize / 2),
								(int)(iconWorldPos.Y + yOffsetIcon - iconSize / 2),
								(int)iconSize,
								(int)iconSize
							);
							Raylib.DrawTexturePro(_alertIcon, srcRect, destRect, Vector2.Zero, 0, Color.White);
						}
					});
				}
				if (entity.IsInterestedInFood && !entity.IsTamed)
				{
					int entityTileX = (int)(entity.WorldPos.X / ts);
					int entityTileY = (int)(entity.WorldPos.Y / ts);
					int entityHeight = World.GetHeightAt(entityTileX, entityTileY);
					float entityYOffset = -entityHeight * ts / 4;
					Vector2 iconWorldPos = new Vector2(entity.WorldPos.X, entity.WorldPos.Y + entityYOffset - entity.VisualOffsetY);
					renderItems.Add(new RenderItem
					{
						Y = float.MaxValue - 1000,
						DrawAction = () =>
						{
							Texture2D questionTex = Program._questionIcon;
							if (questionTex.Id == 0) return;
							float iconSize = 32f;
							float pulse = 0.8f + 0.2f * MathF.Sin((float)Raylib.GetTime() * 5f);
							float yOffsetIcon = -90f * pulse;
							Rectangle srcRect = new Rectangle(0, 0, questionTex.Width, questionTex.Height);
							Rectangle destRect = new Rectangle(
								(int)(iconWorldPos.X - iconSize / 2),
								(int)(iconWorldPos.Y + yOffsetIcon - iconSize / 2),
								(int)iconSize,
								(int)iconSize
							);
							Raylib.DrawTexturePro(questionTex, srcRect, destRect, Vector2.Zero, 0, Color.White);
						}
					});
				}
			} // fin du foreach entity (guard IsAlive/frustum)
			} // fin du else { (rendu des entités côté host/solo)
			
			// ========== CONNEXIONS ÉLECTRIQUES (fils) ==========
			foreach (var conn in Program.GetEnergyConnections())
			{
				if (!Program.IsTileLit(conn.X1, conn.Y1) || !Program.IsTileLit(conn.X2, conn.Y2))
					continue;
				Vector2 p1 = new Vector2(conn.X1 * ts + ts / 2f, conn.Y1 * ts + ts / 2f);
				Vector2 p2 = new Vector2(conn.X2 * ts + ts / 2f, conn.Y2 * ts + ts / 2f);
				if (!IsSegmentNearScreen(p1, p2))
					continue;
				
				// Ajuster pour la hauteur du terrain
				int h1 = GetHeightAt(conn.X1, conn.Y1);
				int h2 = GetHeightAt(conn.X2, conn.Y2);
				p1.Y -= h1 * ts / 4f;
				p2.Y -= h2 * ts / 4f;
				
				float sortY = Math.Min(p1.Y, p2.Y);
				
				renderItems.Add(new RenderItem
				{
					Y = sortY,
					Kind = RenderItemKind.Wire,
					Tag = new WireRenderTag(p1, p2, (p1 + p2) / 2f, IsConnectionPowered(conn), 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 8f))
				});
			}
			
			// Ligne de connexion en cours (mode placement)
			if (Program.IsConnectingWire() && Program.GetWireStartTile() != (-1, -1)
				&& Program.IsTileLit(Program.GetWireStartTile().x, Program.GetWireStartTile().y))
			{
				var startTile = Program.GetWireStartTile();
				Vector2 startPos = new Vector2(startTile.x * ts + ts / 2f, startTile.y * ts + ts / 2f);
				startPos.Y -= GetHeightAt(startTile.x, startTile.y) * ts / 4f;
				
				Vector2 endPos = mouseWorldPos;
				float sortY = Math.Min(startPos.Y, endPos.Y);
				
				renderItems.Add(new RenderItem
				{
					Y = sortY,
					Kind = RenderItemKind.Wire,
					Tag = new WireRenderTag(startPos, endPos, (startPos + endPos) / 2f, false, 0f)
				});
			}

			// Joueurs locaux supplémentaires (visibles dans toutes les vues)
			if (playersToRender != null)
			{
				foreach (var extraPlayer in playersToRender)
				{
					if (!extraPlayer.Connected) continue;
					if (!IsEntityNearScreen(extraPlayer.Position)) continue;
					int playerHeight = Program.GetPlayerGroundHeight(extraPlayer.Position);
					float playerYOffset = -playerHeight * ts / 4;
					Vector2 actualDrawPos = new Vector2(extraPlayer.Position.X, extraPlayer.Position.Y + playerYOffset);
					float playerSortY = actualDrawPos.Y + Program.FeetOffsetY;
					int hairIndex = extraPlayer.HairStyleOverride ?? Program.PlayerHairStyle;
					if (hairIndex < 0 || hairIndex >= hairBase.Count) hairIndex = 0;
					var playerHairBaseTex = hairBase.Count > 0 ? hairBase[hairIndex] : new Texture2D();
					var playerHairOverlayTex = hairOverlay.Count > 0 ? hairOverlay[hairIndex] : new Texture2D();
					var capturedExtraPlayer = extraPlayer;
					object capturedExtraPoseBlendKey = capturedExtraPlayer.Id >= 0
						? EntityRenderer.GetStablePoseBlendKey($"player:{capturedExtraPlayer.Id}")
						: capturedExtraPlayer;
					var capturedExtraDrawPos = actualDrawPos;
					var capturedExtraSkin = extraPlayer.SkinColorOverride ?? pSkin;
					var capturedExtraTint = extraPlayer.MorphTintOverride ?? capturedExtraSkin;
					var capturedExtraFeatureVariants = extraPlayer.MorphFeatureVariantsOverride;
					var capturedExtraFeatureColors = extraPlayer.MorphFeatureColorsOverride;
					var capturedExtraEquipment = extraPlayer.IsNetworkPlayer
						? Program.GetNetworkPlayerEquipmentFull(extraPlayer.Id, extraPlayer.Equip)
						: Program.equipment;
					var capturedExtraEyes = Program.EyesTexture;
					var capturedExtraMouth = Program.MouthTexture;
					var capturedExtraHairColor = extraPlayer.HairColorOverride ?? Program.HairColor;
					var capturedExtraUnderwear = Program.LeafUnderpantsTexture;
					//  Rotation de tête et regard propres à CE joueur distant
					float capturedExtraHeadAngle = extraPlayer.IsNetworkPlayer ? extraPlayer.HeadAngle : headAngle;
					var capturedExtraGaze = extraPlayer.IsNetworkPlayer
						? Program.ComputeGazeOffsetsFor(capturedExtraDrawPos, extraPlayer.LookWorldPos, capturedExtraPlayer.Facing)
						: (Program.PupilLeftOffset, Program.PupilRightOffset, Program.EyebrowLeftOffset, Program.EyebrowRightOffset);
					//  Item tenu en main : même rendu attaché au bras (texture "_rarmbottom") que
					// pour le joueur local, au lieu de l'icône statique flottante précédente.
					Texture2D capturedExtraHeldItemTex = EntityRenderer.GetHeldItemTextureById(capturedExtraPlayer.HeldItemId);
					renderItems.Add(new RenderItem
					{
						Y = playerSortY,
						DrawAction = () =>
						{
							EntityRenderer.DrawEntity(
								capturedExtraPlayer.SpeciesOverride ?? "human",
								capturedExtraPlayer.AnimState,
								capturedExtraPlayer.AnimFrame,
								capturedExtraPlayer.AnimProg,
								capturedExtraPlayer.Facing,
								capturedExtraDrawPos,
								(string.Equals(capturedExtraPlayer.SpeciesOverride, "human", StringComparison.OrdinalIgnoreCase)
									? capturedExtraSkin : capturedExtraTint),
								playerHairBaseTex,
								playerHairOverlayTex,
								capturedExtraHairColor,
								SpeciesData.Skeletons,
								capturedExtraEyes,
								capturedExtraMouth,
								customScale: 1.0f,
								equipment: capturedExtraEquipment,
								isCarrying: false,
								inWater: isPlayerInWater,
									attackSwingProgress: capturedExtraPlayer.AttackSwingProgress,
								heldItemTexture: capturedExtraHeldItemTex,
								headAngle: capturedExtraHeadAngle,
								keepItemHorizontal: false,
								isBow: false,
underwearTexture: capturedExtraUnderwear,
									beardStyle: capturedExtraPlayer.BeardStyleOverride ?? 0,
									eyeStyleOverride: capturedExtraPlayer.EyeStyleOverride,
									eyeColorOverride: capturedExtraPlayer.EyeColorOverride,
									pupilLeftOffset: capturedExtraGaze.Item1,
									pupilRightOffset: capturedExtraGaze.Item2,
									eyebrowLeftOffset: capturedExtraGaze.Item3,
									eyebrowRightOffset: capturedExtraGaze.Item4,
									randomFeatureVariant: capturedExtraFeatureVariants,
									randomFeatureColor: capturedExtraFeatureColors,
									blendKey: capturedExtraPoseBlendKey);

							//  MULTIJOUEUR : pseudo + barre de vie
							// PVP : si ce joueur a activé le PVP, on ne l'affiche pas pour ne pas
							// pouvoir le localiser (ni pseudo, ni barre de vie).
							if (capturedExtraPlayer.IsNetworkPlayer && !capturedExtraPlayer.PvpEnabled)
							{
								string displayedSpecies = capturedExtraPlayer.SpeciesOverride ?? "human";
								float visualTopY = EntityRenderer.GetVisualTopY(
									displayedSpecies,
									capturedExtraPlayer.AnimState,
									capturedExtraPlayer.AnimFrame,
									capturedExtraPlayer.AnimProg,
									capturedExtraPlayer.Facing,
									capturedExtraDrawPos,
									blendKey: capturedExtraPoseBlendKey);

								float barWidth = 48f;
								float barHeight = 7f;
								float barY = visualTopY - 13f;
								float barX = capturedExtraDrawPos.X - barWidth / 2f;

								// Pseudo — remonté au-dessus de la tête (au lieu de -78) pour ne plus
								// être en superposition avec les cheveux/le crâne du joueur, avec une
								// bordure noire pour rester lisible sur tous les fonds.
								if (!string.IsNullOrEmpty(capturedExtraPlayer.DisplayName))
								{
									int nameWidth = FontManager.MeasureText(capturedExtraPlayer.DisplayName, 14);
									int nameX = (int)capturedExtraDrawPos.X - nameWidth / 2;
									int nameY = (int)barY - 19;
									// Bordure noire : le texte est redessiné en noir tout autour (4 ou 8
									// directions) avant la passe finale en blanc, par-dessus.
									foreach (var (ox, oy) in new (int, int)[] { (-1, -1), (1, -1), (-1, 1), (1, 1), (-1, 0), (1, 0), (0, -1), (0, 1) })
									{
										FontManager.DrawText(capturedExtraPlayer.DisplayName,
											nameX + ox, nameY + oy,
											14, new Color(0, 0, 0, 230));
									}
									FontManager.DrawText(capturedExtraPlayer.DisplayName,
										nameX, nameY,
										14, new Color(255, 255, 255, 230));
								}
								// Barre de vie ancrée au sommet réel du squelette, comme pour les entités.
								if (capturedExtraPlayer.MaxHP > 0)
								{
									float ratio = Math.Clamp((float)capturedExtraPlayer.HP / capturedExtraPlayer.MaxHP, 0f, 1f);
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

			// Joueur ou voiture
			//  RAGDOLL : pendant le ragdoll de mort, le joueur n'est plus dessiné avec l'animation
			// normale (le squelette physique est dessiné séparément par PlayerRagdoll.Draw).
			if (drawPlayer && Program.PlayerCurrentCar == null && !Program.IsPlayerRagdolled)
			{
				int playerHeight = Program.GetPlayerGroundHeight(playerPos);
				float playerYOffset = -playerHeight * ts / 4;
				Item? heldItem = null;
				var hotbarSlots = Program.GetHotbarSlotsCached(); //  OPTIM : cache par frame
				if (Program.hotbarSlot < hotbarSlots.Count && !hotbarSlots[Program.hotbarSlot].IsEmpty)
					heldItem = hotbarSlots[Program.hotbarSlot].Item;
				int hairIndex = Program.PlayerHairStyle;
				if (hairIndex < 0 || hairIndex >= hairBase.Count) hairIndex = 0;
				var playerHairBaseTex = hairBase.Count > 0 ? hairBase[hairIndex] : new Texture2D();
				var playerHairOverlayTex = hairOverlay.Count > 0 ? hairOverlay[hairIndex] : new Texture2D();
				var playerPosForDraw = playerPos;
				var playerYOffsetForDraw = playerYOffset;
				var mountedAnimal = Program.MountedAnimal;
				// Le sens du cavalier suit toujours celui de la monture, pour ne jamais les voir
				// tournés dans des directions différentes.
				var playerFacing = mountedAnimal != null ? mountedAnimal.Facing : facing;
				var playerAnim = anim;
				var playerFrame = frame;
				var playerProg = prog;
				var playerSkin = pSkin;
				var playerSkeletons = SpeciesData.Skeletons;
				var playerEyes = Program.EyesTexture;
				var playerMouth = Program.MouthTexture;
				var playerEquipment = Program.equipment;
				Texture2D heldItemTex = default;
				string heldItemAnchor = "rarmbottom";
				var carried = Program.CurrentCarriedFurniture;
				if (carried != null)
				{
					var tileDataCarry = WorldTileRegistry.GetTile(carried.PlaceableId);
					if (tileDataCarry != null) heldItemTex = WorldTileRegistry.GetFirstTexture(carried.PlaceableId);
				}
				else if (heldItem != null)
				{
					var heldItemInfo = EntityRenderer.GetHeldItemTextureInfo(heldItem);
					heldItemTex = heldItemInfo.Texture;
					heldItemAnchor = heldItemInfo.AnchorPart;
				}
				bool isFurniture = carried != null;
				var carriedCreatureRef = Program.CurrentCarriedCreature;
				bool isCarryingCreature = carriedCreatureRef != null;
				Vector2 actualDrawPos;
				if (sittingPosition.HasValue) actualDrawPos = Program.GetSittingVisualPosition();
				else actualDrawPos = new Vector2(playerPosForDraw.X, playerPosForDraw.Y + playerYOffsetForDraw);
						float tempIntensity = TemperatureSystem.CurrentIntensity;
						if (TemperatureSystem.IsCold)
						{
							float shake = Math.Clamp(Math.Abs(tempIntensity) * 3f, 0f, 5f);
							float phase = (float)Raylib.GetTime() * 24f;
							actualDrawPos += new Vector2(
								MathF.Sin(phase) * shake * 0.25f,
								MathF.Cos(phase * 1.2f) * shake * 0.08f
							);
						}
				float playerSortY = actualDrawPos.Y + Program.FeetOffsetY;
				if (Program.IsSitting)
					playerSortY = actualDrawPos.Y + (Program.IsSittingBehindFurniture ? 0f : 80f);
				bool isBow = false;
				if (heldItem != null)
				{
					isBow = GameData.IsRangedWeapon(heldItem.Name);
				}

				// --- DÉBUT MORPHOSE ---
				string speciesToDraw = Program.PlayerMorphSpecies; // "human" par défaut

				Texture2D morphHairBase = new Texture2D();
				Texture2D morphHairOverlay = new Texture2D();
				Color morphHairColor = Color.Black;
				Texture2D morphEyes = new Texture2D();
				Texture2D morphMouth = new Texture2D();
				Equipment? morphEquipment = null;
				Color morphTint = playerSkin; // par défaut (pour human)
				Texture2D underwearTex = Program.LeafUnderpantsTexture; // par défaut

				if (speciesToDraw == "human")
				{
					// Mode humain : on conserve tous les éléments personnalisés
					morphHairBase = playerHairBaseTex;
					morphHairOverlay = playerHairOverlayTex;
					morphHairColor = Program.HairColor;
					morphEyes = playerEyes;
					morphMouth = playerMouth;
					morphEquipment = playerEquipment;
					morphTint = playerSkin;
					underwearTex = Program.LeafUnderpantsTexture; // on garde le pagne
				}
				else
				{
					// Les espèces non humaines gardent leurs traits propres; les humanoïdes
					// conservent toutefois l'équipement du joueur.
					morphTint = Program.PlayerMorphTint;
					morphEquipment = SpeciesData.IsHumanoid(speciesToDraw) ? playerEquipment : null;
					underwearTex = new Texture2D(); // pas de pagne
				}
				// --- FIN MORPHOSE ---

				renderItems.Add(new RenderItem
				{
					Y = playerSortY,
					DrawAction = () =>
					{
						bool materializing = Program.IsSoulMaterializing;
						if (materializing)
						{
							float materializationTime = Program.SoulMaterializationTime;
							if (materializationTime < Program.SoulMaterializationRevealStart)
							{
								Program.DrawSoulMaterializationOrb(actualDrawPos);
								return;
							}
						}

						bool soulMaterializationShaderActive = materializing && Program.BeginSoulMaterializationRender();
						EntityRenderer.DrawEntity(
							speciesToDraw,                // ← espèce morphée (human ou autre)
							playerAnim, playerFrame, playerProg, playerFacing,
							actualDrawPos, morphTint,     // ← teinte adaptée
							morphHairBase, morphHairOverlay, morphHairColor,
							playerSkeletons, morphEyes, morphMouth,
							customScale: 1.0f, equipment: morphEquipment,
							isCarrying: !materializing && (isCarrying || isCarryingCreature), inWater: isPlayerInWater,
							isCarryingCreature: !materializing && isCarryingCreature,
							attackSwingProgress: Program.GetAttackSwingProgress(),
							heldItemTexture: materializing ? default : heldItemTex,
							heldItemInstance: materializing ? null : heldItem,
							headAngle: headAngle,
							keepItemHorizontal: isFurniture,
							isBow: isBow,
							underwearTexture: underwearTex, // ← texture adaptée
							beardStyle: Program.PlayerBeardStyle,
							forceBlinking: materializing || Program.IsBlinking,
							randomFeatureVariant: Program.PlayerMorphFeatureVariants,
							randomFeatureColor: Program.PlayerMorphFeatureColors,
							blendKey: Program.PlayerPoseBlendKey,
							drawCarriedEntity: isCarryingCreature && !materializing ? () =>
							{
								string carriedSpeciesKey = carriedCreatureRef!.Species.ToLower();
								if (!SpeciesData.Skeletons.ContainsKey(carriedSpeciesKey)) return;

								Vector2 carriedPos = EntityRenderer.GetCarryAnchorWorldPos(
													speciesToDraw, playerFacing, actualDrawPos, playerSkeletons,
									anim: playerAnim, frame: playerFrame, prog: playerProg,
									blendKey: Program.PlayerPoseBlendKey);
								// GetCarryAnchorWorldPos renvoie le PIVOT de rarmbottom (coude/poignet),
								// pas la main. On pousse donc vers l'avant/le bas pour amener la créature
								// à la position tenue, bien au bout du bras.
								carriedPos += new Vector2(24f * playerFacing, 4f);

								EntityRenderer.DrawEntity(
									carriedSpeciesKey,
									"hold", 0, 0f, playerFacing,
									carriedPos, carriedCreatureRef.Tint,
									default, default, Color.White,
									playerSkeletons,
									eyes: EntityRenderer.GetEyesTextureForSpecies(carriedSpeciesKey),
									customScale: 1.0f,
									randomFeatureVariant: carriedCreatureRef.RandomFeatureVariant,
									randomFeatureColor: carriedCreatureRef.RandomFeatureColor,
									blendKey: carriedCreatureRef
								);
							} : null,
							drawMount: mountedAnimal != null && !materializing ? () =>
							{
								var m = mountedAnimal;
								if (!m.IsAlive) return;
								Texture2D mHairBase = hairBase.Count > m.HairStyle ? hairBase[m.HairStyle] : new Texture2D();
								Texture2D mHairOverlay = hairOverlay.Count > m.HairStyle ? hairOverlay[m.HairStyle] : new Texture2D();
								EntityRenderer.DrawEntity(
									m.Species, m.AnimState, m.CurrentFrame, m.AnimProgress,
									// La monture garde son propre Facing (synchronisé avec celui du joueur
									// au moment du déplacement), afin que le cavalier et l'animal soient
									// toujours orientés dans le même sens.
									m.Facing, m.WorldPos, m.Tint,
									mHairBase, mHairOverlay, m.HairColor,
									playerSkeletons,
									eyes: EntityRenderer.GetEyesTextureForSpecies(m.Species),
									mouth: Program.GetMouthTextureFor(m), forceBlinking: m.IsBlinking,
									customScale: m.Scale,
									equipment: m.Equipment,
									isCarrying: false, inWater: m.IsInWater,
									attackSwingProgress: 0f,
									flashWhite: m.DamageFlash > 0f,
									pupilLeftOffset: m.PupilLeftOffset,
									pupilRightOffset: m.PupilRightOffset,
									eyebrowLeftOffset: m.EyebrowLeftOffset,
									eyebrowRightOffset: m.EyebrowRightOffset,
									randomFeatureVariant: m.RandomFeatureVariant,
									randomFeatureColor: m.RandomFeatureColor,
									beardStyle: m.BeardStyle,
									blendKey: m);
							} : null
						);
						if (soulMaterializationShaderActive)
							Program.EndSoulMaterializationRender();
						if (materializing)
							Program.DrawSoulMaterializationOrb(actualDrawPos);
					}
				});
			}

			if (Program.PlayerCurrentWagonTile.HasValue)
			{
				Vector2 wagonPos = Program.GetMountedWagonDrawPosition();
				if (IsEntityNearScreen(wagonPos))
				{
					renderItems.Add(new RenderItem
					{
						Y = wagonPos.Y + Program.FeetOffsetY + 0.5f,
						DrawAction = () => Program.DrawMountedWagonFront(
							Program.GetMountedWagonDrawPosition(),
							(int)Program.TileSize,
							Program.IsMountedWagonVertical())
					});
				}
			}

			// Projectiles magiques : intégrés au tri par Y pour réagir correctement aux
			// hitbox des tuiles/entités (passer devant/derrière selon leur profondeur),
			// au lieu d'être dessinés dans une passe séparée toujours au-dessus de tout.
			if (spellProjectiles != null)
			{
				foreach (var sp in spellProjectiles)
				{
					var spRef = sp;
					if (!Program.IsTileLit((int)(spRef.Position.X / ts), (int)(spRef.Position.Y / ts)))
						continue;
					renderItems.Add(new RenderItem
					{
						Y = spRef.Position.Y,
						Kind = RenderItemKind.Projectile,
						Tag = new ProjectileRenderTag(spRef, spRef.Position, spRef.Size, spRef.VisualRotation, spRef.VisualTexture, spRef.Color, spRef.VisualTexture.Id != 0)
					});
				}
			}

			// Éléments externes (offrandes du RitualCircleUI, etc.) : injectés ici pour être triés
			// par Y avec le reste du monde, puis la liste externe est vidée pour la frame suivante.
			if (_externalRenderItems.Count > 0)
			{
				renderItems.AddRange(_externalRenderItems);
				_externalRenderItems.Clear();
			}

			// Tri et dessin
			//  Fix flicker "gros sprites" (ex : deux arbres voisins) : on assigne d'abord un index
			// d'insertion stable à chaque élément, puis on l'utilise comme départage lorsque deux
			// éléments ont un Y de tri identique (ou quasi identique). Sans ce départage explicite,
			// List<T>.Sort() (non stable) pouvait inverser aléatoirement l'ordre de deux sprites à
			// Y égal d'une frame à l'autre, ce qui les faisait clignoter/se repasser devant l'un
			// l'autre. Avec ce départage, l'ordre devient un ordre total déterministe : à
			// configuration égale, deux sprites à Y égal se dessinent toujours dans le même ordre.
			PerfStats.EndSection(PerfStats.Section.WorldRenderPrepare);
			PerfStats.NotifyRenderItems(renderItems.Count);
			PerfStats.StartSection(PerfStats.Section.RenderItemSort);
			for (int ri = 0; ri < renderItems.Count; ri++)
			{
				var tmp = renderItems[ri];
				tmp.SortIndex = ri;
				renderItems[ri] = tmp;
			}
			renderItems.Sort((a, b) =>
			{
				int layerComparison = a.SortLayer.CompareTo(b.SortLayer);
				if (layerComparison != 0) return layerComparison;
				int c = a.Y.CompareTo(b.Y);
				if (c != 0) return c;
				return a.SortIndex.CompareTo(b.SortIndex);
			});
			PerfStats.EndSection(PerfStats.Section.RenderItemSort);
			PerfStats.StartSection(PerfStats.Section.RenderItemDraw);
			foreach (var item in renderItems) item.Draw();
			PerfStats.EndSection(PerfStats.Section.RenderItemDraw);
			PerfStats.EndEntityRenderFrame();
			_renderItemsPool = renderItems; // On garde la liste pour reuse
			
			// Hitboxes debug
			PerfStats.StartSection(PerfStats.Section.WorldDrawTail);
			PerfStats.StartSection(PerfStats.Section.WorldDebugOverlay);
			if (showDebug)
			{
				if (DebugOverlaySettings.EntityHitboxes || DebugOverlaySettings.AiDiagnostics
					|| DebugOverlaySettings.VehicleAndAttackHitboxes)
				{
					foreach (var entity in entities)
					{
						if (!entity.IsAlive) continue;
						if (!ShouldRenderEntity(entity)) continue;
						Vector2 entityDebugPos = entity.VisualWorldPos;
						if (DebugOverlaySettings.VehicleAndAttackHitboxes)
							entity.DrawAttackDebugZone();

						if (DebugOverlaySettings.EntityHitboxes)
						{
							Rectangle hitboxRect = GetEntityCollisionHitbox(entityDebugPos, entity.Species, ts);
							Rectangle damageHitboxRect = GetEntityDamageHitbox(entity, ts);
							Raylib.DrawRectangleLinesEx(hitboxRect, 2, Color.Red);
							Raylib.DrawRectangleLinesEx(damageHitboxRect, 2, Color.Blue);
							string speciesName = entity.Species;
							int textWidth = Raylib.MeasureText(speciesName, 10);
							Raylib.DrawText(speciesName,
								(int)(hitboxRect.X + hitboxRect.Width / 2 - textWidth / 2),
								(int)(hitboxRect.Y - 12), 10, Color.White);
						}

						if (DebugOverlaySettings.AiDiagnostics)
						{
							entity.DrawPath();
							entity.DrawPriorityDebugLabels();
						}
					}
				}

				if (DebugOverlaySettings.EntityHitboxes && drawPlayer)
				{
					Rectangle playerCollision = GetEntityCollisionHitbox(playerPos, Program.GetPlayerCollisionDimensions(), ts);
					Rectangle playerDamage = GetEntityDamageHitbox(playerPos, Program.PlayerMorphSpecies, ts);
					Raylib.DrawRectangleLinesEx(playerCollision, 2, Color.Red);
					Raylib.DrawRectangleLinesEx(playerDamage, 2, Color.Blue);
					Raylib.DrawText("joueur local", (int)playerDamage.X, (int)(playerDamage.Y - 12), 10, Color.White);
				}

				if (DebugOverlaySettings.EntityHitboxes && playersToRender != null)
				{
					foreach (var remotePlayer in playersToRender)
					{
						string species = remotePlayer.SpeciesOverride ?? "human";
						Rectangle playerCollision = GetEntityCollisionHitbox(remotePlayer.Position, Program.GetPlayerCollisionDimensions(), ts);
						Rectangle playerDamage = GetEntityDamageHitbox(remotePlayer.Position, species, ts);
						Raylib.DrawRectangleLinesEx(playerCollision, 2, Color.Red);
						Raylib.DrawRectangleLinesEx(playerDamage, 2, Color.Blue);
						string label = string.IsNullOrWhiteSpace(remotePlayer.DisplayName) ? "joueur réseau" : remotePlayer.DisplayName;
						Raylib.DrawText(label, (int)playerDamage.X, (int)(playerDamage.Y - 12), 10, Color.White);
					}
				}

				//  Hitboxes de TOUS les objets du monde visibles (meubles, arbres, murs, etc.),
				// via la même géométrie que GetCollisionRect (celle réellement utilisée pour les
				// collisions et pour la détection de clic). Avant, seules les entités et les
				// quelques tuiles voisines débordées par une hitbox (contour orange ci-dessous)
				// étaient visibles en debug — l'objet lui-même n'affichait jamais sa hitbox.
				if (DebugOverlaySettings.TileHitboxes)
				{
					for (int dhx = startX; dhx <= endX; dhx++)
					{
						for (int dhy = startY; dhy <= endY; dhy++)
						{
							int dhObjectId = GetObjectIdAt(dhx, dhy);
							if (dhObjectId == 0) continue;
							var dhTileData = WorldTileRegistry.GetTile(dhObjectId);
							if (dhTileData == null) continue;
							var (dhX, dhY, dhW, dhH) = GetCollisionRect(dhx, dhy, dhTileData, ts);
							Raylib.DrawRectangleLinesEx(new Rectangle(dhX, dhY, dhW, dhH), 2, Color.Lime);
						}
					}

					// Tuiles sans objet directement dessus mais bloquées par la hitbox d'un objet voisin.
					if (visibleObjectCount > 0)
					{
						var pfBlocked = GetHitboxBlockedTiles(startX, endX, startY, endY, destroyed);
						for (int dbx = startX; dbx <= endX; dbx++)
						{
							for (int dby = startY; dby <= endY; dby++)
							{
								if (!pfBlocked.Contains((dbx, dby))) continue;
								if (GetObjectIdAt(dbx, dby) != 0) continue;
								int dbHeight = GetHeightAt(dbx, dby);
								int dbBaseY = dby * ts - (dbHeight * ts / 4);
								Raylib.DrawRectangleLinesEx(new Rectangle(dbx * ts, dbBaseY, ts, ts), 2, Color.Orange);
							}
						}
					}
				}
			}
			PerfStats.EndSection(PerfStats.Section.WorldDebugOverlay);

			// Interactions UI
			PerfStats.StartSection(PerfStats.Section.InteractionPrompts);
			//  PERF : avant, on construisait un Dictionary + une List par tuile interactive visible (portes, lits,
			// cultures, coffres...) puis on ne dessinait QUE celle sous la souris, avec un GetScreenToWorld2D
			// recalculé à chaque entrée. Maintenant : souris convertie une fois, et on teste la hitbox AVANT de
			// construire la moindre liste de prompts. Rendu strictement identique.
			Vector2 promptMouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), Program.GetCurrentCamera());
			for (int ix = 0; ix < width; ix++)
			{
				int worldX = startX + ix;
				for (int iy = 0; iy < heightTiles; iy++)
				{
					int worldY = startY + iy;
					int localIx = ix;   // ← COPIE LOCALE
					int localIy = iy;   // ← COPIE LOCALE
					int objectId = objectIds[ix, iy];
					if (objectId == 0) continue;
					var tileData = WorldTileRegistry.GetTile(objectId);
					if (tileData == null) continue;
					bool isCaveEntryExit = (objectId == 50 || objectId == 52);
					bool isStation = tileData.IsStation == true;
					bool isContainer = tileData.IsContainer == true;
					bool isArmorStand = tileData.IsArmorStand == true;
					bool isFurniture = tileData.IsFurniture;
					bool isCrop = tileData.IsCrop;
					if (!isCaveEntryExit && !isStation && !isContainer && !isArmorStand && !isFurniture && !isCrop
						&& !tileData.IsSleepable && !tileData.IsSittable && !tileData.IsInteractable) continue;
					int x = worldX, y = worldY;
					var (hitboxX, hitboxY, hitboxW, hitboxH) = GetCollisionRect(x, y, tileData, ts);
					if (!Raylib.CheckCollisionPointRec(promptMouseWorld, new Rectangle(hitboxX, hitboxY, hitboxW, hitboxH)))
						continue;
					var prompts = _interactionPromptScratch;
					prompts.Clear();
					void AddPrompt(string actionKey, string actionType)
					{
						if (prompts.Any(prompt => prompt.actionKey == actionKey)) return;
						prompts.Add((actionKey, actionType, GetInteractionIcon(objectId, actionType)));
					}
					if (isCaveEntryExit)
					{
						string actionType = (objectId == 50) ? "enterCave" : "exitCave";
						AddPrompt("E", actionType);
					}
					if (isStation) AddPrompt("E", "craft");
					if (isContainer || isArmorStand || tileData.IsSleepable || tileData.IsSittable || tileData.IsInteractable)
						AddPrompt("E", "E");
					if (isCrop)
					{
						var cropData = GetCropDataAt(worldX, worldY);
						bool cropIsReady = cropData != null && cropData.AccumulatedGrowthTime >= tileData.GrowthTime;
						if (cropIsReady)
							AddPrompt("E", "E");
					}
					if (isFurniture && !isCarrying && !NetworkManager.IsClient && Program.GetItemIdFromPlaceableId(objectId) != 0)
						AddPrompt("F", "F");

					float objectX = x * ts + (tileData.DrawOffset?.X ?? 0);
					float objectY = y * ts - (GetHeightAt(x, y) * ts / 4) + ts;
					float objectWidth = ts;
					float objectHeight = ts;
					if (tileData.Size != null)
					{
						objectWidth = ts * tileData.Size.Width;
						objectHeight = ts * tileData.Size.Height;
						objectY -= objectHeight;
						objectY -= tileData.DrawOffset?.Y ?? 0;
					}
					else
					{
						objectY -= ts;
					}
					float centerX = objectX + objectWidth / 2f;
					float baseIconY = objectY - ts / 2f - 10f;
					for (int i = 0; i < prompts.Count; i++)
					{
						var (actionKey, actionType, iconTex) = prompts[i];
						Color textColor = actionType switch
						{
							"craft" => new Color(100, 200, 100, 255),
							"enterCave" => new Color(200, 150, 50, 255),
							"exitCave" => new Color(100, 150, 200, 255),
							"F" => new Color(100, 200, 255, 255),
							_ => Color.Yellow
						};
						float iconSize = 20f;
						float buttonSize = 28f;
						float actionGroupWidth = iconSize + 4f + buttonSize;
						float iconX = centerX - actionGroupWidth / 2f;
						float yOffsetList = i * (iconSize + 2f);
						float currentY = baseIconY - yOffsetList;
						if (iconTex.Id != 0)
						{
							Raylib.DrawTexturePro(iconTex,
								new Rectangle(0, 0, iconTex.Width, iconTex.Height),
								new Rectangle(iconX, currentY, iconSize, iconSize),
								Vector2.Zero, 0, Color.White);
						}
						float buttonX = iconX + iconSize + 4f;
						float buttonY = currentY + (iconSize - buttonSize) / 2;
						if (_inputButtonTexture.Id != 0)
						{
							Raylib.DrawTexturePro(_inputButtonTexture,
								new Rectangle(0, 0, _inputButtonTexture.Width, _inputButtonTexture.Height),
								new Rectangle(buttonX, buttonY, buttonSize, buttonSize),
								Vector2.Zero, 0, Color.White);
							int fontSize = 12;
							int textWidth = Raylib.MeasureText(actionKey, fontSize);
							float textX = buttonX + (buttonSize - textWidth) / 2;
							float textY = buttonY + (buttonSize - fontSize) / 2;
							Raylib.DrawText(actionKey, (int)textX, (int)textY, fontSize, Color.White);
						}
						else
						{
							Raylib.DrawRectangleRounded(new Rectangle(buttonX, buttonY, buttonSize, buttonSize), 0.2f, 6, new Color(40, 40, 50, 200));
							Raylib.DrawRectangleRoundedLines(new Rectangle(buttonX, buttonY, buttonSize, buttonSize), 0.2f, 6, 1, new Color(150, 150, 170, 200));
							int fontSize = 12;
							int textWidth = Raylib.MeasureText(actionKey, fontSize);
							float textX = buttonX + (buttonSize - textWidth) / 2;
							float textY = buttonY + (buttonSize - fontSize) / 2;
							Raylib.DrawText(actionKey, (int)textX, (int)textY, fontSize, textColor);
						}
					}
				}
			}

			PerfStats.EndSection(PerfStats.Section.InteractionPrompts);

			if (_isUnderground)
			{
				bool nearExit = false;
				int playerTileX_und = (int)(playerPos.X / ts);
				int playerTileY_und = (int)(playerPos.Y / ts);
				for (int dx = -3; dx <= 3; dx++)
				{
					for (int dy = -3; dy <= 3; dy++)
					{
						if (GetObjectIdAt(playerTileX_und + dx, playerTileY_und + dy) == 52)
						{
							nearExit = true;
							break;
						}
					}
					if (nearExit) break;
				}
				string caveText = nearExit ? " GROTTE - Cherchez la sortie lumineuse" : " GROTTE";
				int tw = Raylib.MeasureText(caveText, 16);
				Raylib.DrawText(caveText, (sw - tw) / 2, sh - 40, 16, new Color(200, 150, 50, 200));
			}
			PerfStats.EndSection(PerfStats.Section.WorldDrawTail);
		}

    }
}
