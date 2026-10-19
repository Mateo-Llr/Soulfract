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
		private struct MapLayout
		{
			public float TileSize;
			public int VisibleTilesX;
			public int VisibleTilesY;
			public int MapX;
			public int MapY;
			public int MapAreaW;
			public int MapAreaH;
		}

		private static MapLayout ComputeMapLayout(int sw, int sh, float zoom)
		{
			float tileSize = MAP_TILE_SIZE_BASE * zoom;
			float mapAreaWidth = sw - 80;
			float mapAreaHeight = sh - 100;

			int visibleTilesX = Math.Max(30, (int)(mapAreaWidth / tileSize));
			int visibleTilesY = Math.Max(20, (int)(mapAreaHeight / tileSize));

			return new MapLayout
			{
				TileSize = tileSize,
				VisibleTilesX = visibleTilesX,
				VisibleTilesY = visibleTilesY,
				MapX = 40,
				MapY = 50,
				MapAreaW = Math.Max(1, (int)Math.Ceiling(visibleTilesX * tileSize)),
				MapAreaH = Math.Max(1, (int)Math.Ceiling(visibleTilesY * tileSize)),
			};
		}

		static void DrawMap()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			int ts = Program.TileSize;

			Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 220));

			MapLayout layout = ComputeMapLayout(sw, sh, _mapZoom);
			float currentTileSize = layout.TileSize;
			int visibleTilesX = layout.VisibleTilesX;
			int visibleTilesY = layout.VisibleTilesY;
			int mapX = layout.MapX;
			int mapY = layout.MapY;
			int mapAreaW = layout.MapAreaW;
			int mapAreaH = layout.MapAreaH;

			Vector2 playerTilePos = new Vector2(_playerPos.X / ts, _playerPos.Y / ts);
			float halfWidth = visibleTilesX / 2f;
			float halfHeight = visibleTilesY / 2f;
			float startTileX = playerTilePos.X - halfWidth + _mapOffset.X;
			float startTileY = playerTilePos.Y - halfHeight + _mapOffset.Y;

			int floorStartTileX = (int)Math.Floor(startTileX);
			int floorStartTileY = (int)Math.Floor(startTileY);
			float fracX = startTileX - floorStartTileX;
			float fracY = startTileY - floorStartTileY;

			int cacheTilesX = visibleTilesX + 2;
			int cacheTilesY = visibleTilesY + 2;
			int cacheAreaW = Math.Max(1, (int)Math.Ceiling(cacheTilesX * currentTileSize));
			int cacheAreaH = Math.Max(1, (int)Math.Ceiling(cacheTilesY * currentTileSize));

			bool needsMapTextureRefresh =
				!_mapRenderTextureInitialized ||
				_mapRenderTextureWidth != cacheAreaW ||
				_mapRenderTextureHeight != cacheAreaH ||
				_mapCacheFloorTileX != floorStartTileX ||
				_mapCacheFloorTileY != floorStartTileY ||
				_mapRenderTextureVisibleTilesX != visibleTilesX ||
				_mapRenderTextureVisibleTilesY != visibleTilesY ||
				Math.Abs(_mapRenderTextureTileSize - currentTileSize) > 0.001f ||
				_mapRenderTextureGodMode != Program.IsGodMode ||
				_mapRenderTextureUnderground != World.IsUnderground ||
				_mapRenderTextureDungeon != World.IsInDungeon;

			if (needsMapTextureRefresh)
			{
				if (_mapRenderTextureInitialized && _mapRenderTexture.Id != 0)
					Raylib.UnloadRenderTexture(_mapRenderTexture);

				_mapRenderTexture = Raylib.LoadRenderTexture(cacheAreaW, cacheAreaH);
				_mapRenderTextureInitialized = true;
				_mapRenderTextureWidth = cacheAreaW;
				_mapRenderTextureHeight = cacheAreaH;
				_mapCacheFloorTileX = floorStartTileX;
				_mapCacheFloorTileY = floorStartTileY;
				_mapRenderTextureVisibleTilesX = visibleTilesX;
				_mapRenderTextureVisibleTilesY = visibleTilesY;
				_mapRenderTextureTileSize = currentTileSize;
				_mapRenderTextureGodMode = Program.IsGodMode;
				_mapRenderTextureUnderground = World.IsUnderground;
				_mapRenderTextureDungeon = World.IsInDungeon;

				Raylib.BeginTextureMode(_mapRenderTexture);
				Raylib.ClearBackground(new Color(0, 0, 0, 0));

				// PERF : le detailLevel doit continuer à grandir avec le dézoom, pas
				// être plafonné à une valeur fixe (avant : max 4x, donc à un dézoom
				// extrême avec des milliers de tuiles visibles, la boucle traitait
				// quand même des milliers d'itérations -> gros lag). Ici on calcule
				// le detailLevel nécessaire pour que le nombre de tuiles RÉELLEMENT
				// traitées reste borné par MAP_TARGET_SAMPLES dans chaque axe, quel
				// que soit le niveau de zoom : plus on dézoome, moins la carte est
				// précise (on saute plus de tuiles), mais la quantité de travail par
				// frame reste à peu près constante.
				int detailLevel = Math.Max(1, (int)Math.Ceiling(Math.Max(visibleTilesX, visibleTilesY) / (float)MAP_TARGET_SAMPLES));

				float renderTileSize = currentTileSize * detailLevel;

				for (int screenX = -1; screenX < visibleTilesX + 1; screenX += detailLevel)
				{
					int worldTileX = floorStartTileX + screenX;
					float drawX = (screenX + 1) * currentTileSize;

					for (int screenY = -1; screenY < visibleTilesY + 1; screenY += detailLevel)
					{
						int worldTileY = floorStartTileY + screenY;

						Color tileColor;
						bool shouldRenderTile = Program.IsGodMode || World.IsUnderground || World.IsTileExplored(worldTileX, worldTileY);
						tileColor = shouldRenderTile
							? GetTileColorForMap(worldTileX, worldTileY)
							: new Color(30, 30, 40, 255);

						float drawY = (screenY + 1) * currentTileSize;
						Raylib.DrawRectangleRec(new Rectangle(drawX, drawY, renderTileSize + 1f, renderTileSize + 1f), tileColor);
					}
				}

				// Espacement de la grille proportionnel au detailLevel (sinon, très
				// dézoomé, on recalculerait une grille tous les 10 tuiles alors que
				// chaque "pixel" de la carte représente déjà des dizaines de tuiles).
				Color gridColor = new Color(255, 255, 255, 18);
				int gridSpacing = Math.Max(10, detailLevel * 10);
				for (int screenX = -1; screenX < visibleTilesX + 1; screenX += detailLevel)
				{
					int worldTileX = floorStartTileX + screenX;
					if (worldTileX % gridSpacing != 0) continue;
					float lineX = (screenX + 1) * currentTileSize;
					Raylib.DrawLineEx(new Vector2(lineX, 0), new Vector2(lineX, cacheAreaH), 1f, gridColor);
				}
				for (int screenY = -1; screenY < visibleTilesY + 1; screenY += detailLevel)
				{
					int worldTileY = floorStartTileY + screenY;
					if (worldTileY % gridSpacing != 0) continue;
					float lineY = (screenY + 1) * currentTileSize;
					Raylib.DrawLineEx(new Vector2(0, lineY), new Vector2(cacheAreaW, lineY), 1f, gridColor);
				}

				Raylib.EndTextureMode();
			}

			Rectangle panelRect = new Rectangle(mapX - 6, mapY - 6, mapAreaW + 12, mapAreaH + 12);
			Rectangle shadowRect = new Rectangle(panelRect.X + 6, panelRect.Y + 8, panelRect.Width, panelRect.Height);
			Raylib.DrawRectangleRounded(shadowRect, 0.04f, 8, new Color(0, 0, 0, 120));
			Raylib.DrawRectangleRounded(panelRect, 0.04f, 8, new Color(18, 18, 26, 235));

			Raylib.BeginScissorMode(mapX, mapY, mapAreaW, mapAreaH);

			float offsetX = mapX - currentTileSize - fracX * currentTileSize;
			float offsetY = mapY - currentTileSize - fracY * currentTileSize;

			Rectangle src = new Rectangle(0, 0, _mapRenderTexture.Texture.Width, -_mapRenderTexture.Texture.Height);
			Rectangle dst = new Rectangle(offsetX, offsetY, _mapRenderTexture.Texture.Width, _mapRenderTexture.Texture.Height);
			Raylib.DrawTexturePro(_mapRenderTexture.Texture, src, dst, Vector2.Zero, 0f, Color.White);

			Raylib.EndScissorMode();

			Raylib.DrawRectangleRoundedLines(panelRect, 0.04f, 8, 2, new Color(110, 110, 135, 220));

			string title = Localization.Get("map.title");
			if (string.IsNullOrEmpty(title)) title = "Carte";
			FontManager.DrawText(title, mapX, mapY - 30, 20, new Color(230, 230, 240, 255));

			int compassCx = mapX + mapAreaW - 26;
			int compassCy = mapY + 26;
			Raylib.DrawCircle(compassCx, compassCy, 20, new Color(0, 0, 0, 140));
			Raylib.DrawCircleLines(compassCx, compassCy, 20, new Color(150, 150, 170, 200));
			FontManager.DrawText("N", compassCx - 4, compassCy - 18, 14, Color.White);
			FontManager.DrawText("S", compassCx - 4, compassCy + 4, 14, new Color(180, 180, 190, 255));
			FontManager.DrawText("E", compassCx + 8, compassCy - 6, 14, new Color(180, 180, 190, 255));
			FontManager.DrawText("O", compassCx - 18, compassCy - 6, 14, new Color(180, 180, 190, 255));

			Texture2D mapMarker = LoadMapMarkerTexture();
			float markerSize = 24f;

			float markerMapX = mapX + (playerTilePos.X - startTileX) * currentTileSize;
			float markerMapY = mapY + (playerTilePos.Y - startTileY) * currentTileSize;

			bool isMarkerInside = markerMapX >= mapX && markerMapX <= mapX + mapAreaW &&
						markerMapY >= mapY && markerMapY <= mapY + mapAreaH;

			if (isMarkerInside)
			{
				Raylib.DrawEllipse((int)markerMapX, (int)(markerMapY + markerSize * 0.32f), markerSize * 0.35f, markerSize * 0.14f, new Color(0, 0, 0, 90));
				Vector2 markerPos = new Vector2(markerMapX - markerSize / 2, markerMapY - markerSize / 2);
				DrawMapMarker(mapMarker, markerPos, markerSize, _playerFacing);
			}
			else
			{
				Vector2 centerMap = new Vector2(mapX + mapAreaW / 2f, mapY + mapAreaH / 2f);
				Vector2 direction = new Vector2(markerMapX - centerMap.X, markerMapY - centerMap.Y);
				direction = direction.Length() > 0.01f ? Vector2.Normalize(direction) : new Vector2(1, 0);

				Vector2 edgePoint = GetIntersectionWithRectEdge(centerMap, direction, mapX, mapY, mapAreaW, mapAreaH);
				Vector2 edgeMarkerPos = new Vector2(edgePoint.X - markerSize / 2, edgePoint.Y - markerSize / 2);
				DrawMapMarker(mapMarker, edgeMarkerPos, markerSize, _playerFacing);
				Raylib.DrawLineEx(centerMap, edgePoint, 2, new Color(255, 255, 255, 120));
			}

			if (HasDiedAtLeastOnce)
			{
				Texture2D skullMarker = LoadSkullMapTexture();
				if (skullMarker.Id != 0)
				{
					Vector2 deathTilePos = new Vector2(LastDeathPosition.X / ts, LastDeathPosition.Y / ts);
					float skullMapX = mapX + (deathTilePos.X - startTileX) * currentTileSize;
					float skullMapY = mapY + (deathTilePos.Y - startTileY) * currentTileSize;
					bool isSkullInside = skullMapX >= mapX && skullMapX <= mapX + mapAreaW &&
								  skullMapY >= mapY && skullMapY <= mapY + mapAreaH;
					if (isSkullInside)
					{
						float skullSize = 20f;
						Rectangle skullSrc = new Rectangle(0, 0, skullMarker.Width, skullMarker.Height);
						Rectangle skullDest = new Rectangle(skullMapX - skullSize / 2f, skullMapY - skullSize / 2f, skullSize, skullSize);
						Raylib.DrawTexturePro(skullMarker, skullSrc, skullDest, Vector2.Zero, 0f, Color.White);
					}
				}
			}

			Vector2 mousePos = Raylib.GetMousePosition();
			if (Raylib.IsMouseButtonPressed(MouseButton.Right) &&
				mousePos.X >= mapX && mousePos.X <= mapX + mapAreaW &&
				mousePos.Y >= mapY && mousePos.Y <= mapY + mapAreaH)
			{
				if (Program.IsGodMode)
				{
					float targetTileX = startTileX + (mousePos.X - mapX) / currentTileSize;
					float targetTileY = startTileY + (mousePos.Y - mapY) / currentTileSize;
					Vector2 targetWorld = new Vector2(
						targetTileX * ts + ts / 2f,
						targetTileY * ts + ts / 2f);
					SetPlayerPosition(targetWorld);
					_smoothedCameraTarget = targetWorld;
					_isDraggingMap = false;
					_gameState = GameState.Playing;
				}
			}
			if (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
				mousePos.X >= mapX && mousePos.X <= mapX + mapAreaW &&
				mousePos.Y >= mapY && mousePos.Y <= mapY + mapAreaH)
			{
				_isDraggingMap = true;
				_mapDragStart = mousePos;
			}
			if (_isDraggingMap && Raylib.IsMouseButtonDown(MouseButton.Left))
			{
				Vector2 delta = mousePos - _mapDragStart;
				_mapOffset.X -= delta.X / currentTileSize;
				_mapOffset.Y -= delta.Y / currentTileSize;
				_mapDragStart = mousePos;
			}
			if (Raylib.IsMouseButtonReleased(MouseButton.Left)) _isDraggingMap = false;

			if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(mapX, mapY, mapAreaW, mapAreaH)))
			{
				float wheel = Raylib.GetMouseWheelMove();
				if (wheel != 0)
				{
					Vector2 anchorTilePos = new Vector2(
						startTileX + (mousePos.X - mapX) / currentTileSize,
						startTileY + (mousePos.Y - mapY) / currentTileSize
					);

					_mapZoom = Math.Clamp(_mapZoom + wheel * MAP_ZOOM_STEP, MAP_ZOOM_MIN, MAP_ZOOM_MAX);
					MapLayout newLayout = ComputeMapLayout(sw, sh, _mapZoom);

					_mapOffset = new Vector2(
						anchorTilePos.X - playerTilePos.X + newLayout.VisibleTilesX / 2f
							- (mousePos.X - newLayout.MapX) / newLayout.TileSize,
						anchorTilePos.Y - playerTilePos.Y + newLayout.VisibleTilesY / 2f
							- (mousePos.Y - newLayout.MapY) / newLayout.TileSize
					);
				}
			}

			string zoomText = $"Zoom : {_mapZoom * 100:F0}% | Zone : {visibleTilesX}x{visibleTilesY} tuiles";
			string wheelHint = Localization.Get("map.wheel_hint");
			int infoY = mapY + mapAreaH + 14;
			int infoH = 46;
			Raylib.DrawRectangleRounded(new Rectangle(mapX - 4, infoY - 4, mapAreaW + 8, infoH), 0.15f, 6, new Color(0, 0, 0, 130));
			FontManager.DrawText(zoomText, mapX + 4, infoY, 14, new Color(210, 210, 220, 230));
			FontManager.DrawText(wheelHint, mapX + 4, infoY + 22, 12, new Color(160, 160, 170, 200));

			string helpText = Localization.Get("map.help_text");
			int tw = FontManager.MeasureText(helpText, 16);
			FontManager.DrawText(helpText, (sw - tw) / 2, sh - 30, 16, Color.Gray);
		}

    }
}
