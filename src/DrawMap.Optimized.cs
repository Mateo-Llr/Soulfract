// ============================================================================
//  DrawMap() — version optimisée & améliorée visuellement
// ============================================================================
//
//  CE QUI N'ALLAIT PAS DANS L'ANCIEN CODE
//  ---------------------------------------
//  1. `Raylib.DrawRectangle(0,0,sw,sh, ...)` était appelé DEUX FOIS (l. 390 et
//     467) : tout l'écran était rempli deux fois par frame pour rien.
//  2. Le contour de la carte (`DrawRectangleLines`) était dessiné deux fois
//     de suite, avec exactement les mêmes paramètres (copier-coller oublié).
//  3. Le cache "render texture" se recalculait à la moindre fraction de tuile
//     de déplacement (seuil de 0.001f). Résultat : dès que la souris bouge
//     d'1 pixel en glissant la carte, TOUTE la grille de tuiles est
//     reconstruite tuile par tuile (des centaines de DrawRectangle) — à
//     chaque frame pendant tout le drag. C'est l'essentiel du souci de
//     performance.
//  4. Comme le cache se reconstruit à chaque micro-mouvement, il ne sert
//     quasiment à jamais de cache : le "scroll" n'est pas fluide, il est
//     rendu par teleportation d'une texture entièrement neuve.
//  5. Le code de calcul de la zone visible (tuiles visibles, taille carte,
//     position) était dupliqué intégralement dans la gestion du zoom
//     (l. 576-595), avec risque de désynchronisation entre les deux copies.
//  6. Rendu plat : aucune grille, aucun panneau, aucune ombre, pas de
//     lisibilité pour les infos (zoom/aide) qui flottent sur le fond.
//
//  CE QUE FAIT CETTE VERSION
//  ---------------------------------------
//  • Un seul fond plein écran.
//  • Le cache ne se reconstruit QUE quand la tuile d'origine (partie
//    entière) change réellement, ou que le zoom/la taille de fenêtre
//    changent — pas à chaque pixel de drag.
//  • Pour un scroll fluide malgré le cache "par tuile entière", la texture
//    est rendue avec une bordure d'1 tuile tout autour, puis affichée
//    décalée du reste fractionnaire (sous-pixel) → glisser la carte est
//    fluide sans jamais redessiner les tuiles pendant le drag.
//  • Layout de la carte calculé par une seule fonction (`ComputeMapLayout`)
//    réutilisée partout (affichage + zoom à la molette), donc plus de
//    duplication ni de désync possible.
//  • Habillage visuel : panneau avec ombre + bordure arrondie, grille légère
//    toutes les 10 tuiles, titre, bandeau d'info avec fond, boussole simple.
//
//  INTÉGRATION
//  ---------------------------------------
//  Remplacer tout le corps de `static void DrawMap()` (lignes ~384 à 611
//  dans Program.Core.cs) par le contenu ci-dessous, et ajouter les deux
//  petits champs de cache indiqués en haut (juste après les champs
//  `_mapRenderTexture*` existants) :
//
//      private static int _mapCacheFloorTileX = int.MinValue;
//      private static int _mapCacheFloorTileY = int.MinValue;
//
//  reste inchangé : ce sont vos fonctions existantes, définies ailleurs dans
//  le projet, et cette version s'appuie dessus telles quelles.
// ============================================================================

#if false
using Raylib_cs;
using System;
using System.Numerics;

namespace Soulfract
{
	public static partial class Program
	{
		// --- à ajouter à côté des autres champs _mapRenderTexture* ---
		// private static int _mapCacheFloorTileX = int.MinValue;
		// private static int _mapCacheFloorTileY = int.MinValue;

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

			// Fond plein écran (une seule fois)
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

			// Partie entière de l'origine : c'est CE qui détermine quelles tuiles
			// sont visibles. Le reste (fraction) n'est qu'un décalage sous-pixel
			// géré à l'affichage, pas au rendu du cache.
			int floorStartTileX = (int)Math.Floor(startTileX);
			int floorStartTileY = (int)Math.Floor(startTileY);
			float fracX = startTileX - floorStartTileX;
			float fracY = startTileY - floorStartTileY;

			// La texture couvre 1 tuile de bordure de chaque côté pour permettre
			// un décalage sous-pixel fluide sans avoir à re-render à chaque frame.
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
				_mapRenderTextureGodMode != Program.IsGodMode;

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

				Raylib.BeginTextureMode(_mapRenderTexture);
				Raylib.ClearBackground(new Color(0, 0, 0, 0));

				int tilePixelSize = Math.Max(1, (int)Math.Round(currentTileSize));

				// -1 : on part une tuile avant l'origine visible (bordure de secours)
				for (int screenX = -1; screenX < visibleTilesX + 1; screenX++)
				{
					int worldTileX = floorStartTileX + screenX;
					int drawX = (int)Math.Round((screenX + 1) * currentTileSize);

					for (int screenY = -1; screenY < visibleTilesY + 1; screenY++)
					{
						int worldTileY = floorStartTileY + screenY;

						Color tileColor;
						bool shouldRenderTile = Program.IsGodMode || World.IsTileExplored(worldTileX, worldTileY);
						tileColor = shouldRenderTile
							? GetTileColorForMap(worldTileX, worldTileY)
							: new Color(30, 30, 40, 255);

						int drawY = (int)Math.Round((screenY + 1) * currentTileSize);
						Raylib.DrawRectangle(drawX, drawY, tilePixelSize, tilePixelSize, tileColor);
					}
				}

				// Grille légère toutes les 10 tuiles, pour donner un repère
				// d'échelle sans surcharger visuellement.
				Color gridColor = new Color(255, 255, 255, 18);
				for (int screenX = -1; screenX < visibleTilesX + 1; screenX++)
				{
					int worldTileX = floorStartTileX + screenX;
					if (worldTileX % 10 != 0) continue;
					int lineX = (int)Math.Round((screenX + 1) * currentTileSize);
					Raylib.DrawLine(lineX, 0, lineX, cacheAreaH, gridColor);
				}
				for (int screenY = -1; screenY < visibleTilesY + 1; screenY++)
				{
					int worldTileY = floorStartTileY + screenY;
					if (worldTileY % 10 != 0) continue;
					int lineY = (int)Math.Round((screenY + 1) * currentTileSize);
					Raylib.DrawLine(0, lineY, cacheAreaW, lineY, gridColor);
				}

				Raylib.EndTextureMode();
			}

			// ---- Panneau : ombre + fond + bordure arrondie ----
			Rectangle panelRect = new Rectangle(mapX - 6, mapY - 6, mapAreaW + 12, mapAreaH + 12);
			Rectangle shadowRect = new Rectangle(panelRect.X + 6, panelRect.Y + 8, panelRect.Width, panelRect.Height);
			Raylib.DrawRectangleRounded(shadowRect, 0.04f, 8, new Color(0, 0, 0, 120));
			Raylib.DrawRectangleRounded(panelRect, 0.04f, 8, new Color(18, 18, 26, 235));

			// ---- Contenu de la carte (texture mise en cache), avec décalage
			// sous-pixel + scissor pour rogner exactement à la zone visible ----
			Raylib.BeginScissorMode(mapX, mapY, mapAreaW, mapAreaH);

			float offsetX = mapX - currentTileSize - fracX * currentTileSize;
			float offsetY = mapY - currentTileSize - fracY * currentTileSize;

			// La render texture est verticalement inversée par OpenGL : on
			// compense avec une source à hauteur négative (comme le fait
			// classiquement Raylib pour dessiner une RenderTexture2D).
			Rectangle src = new Rectangle(0, 0, _mapRenderTexture.Texture.Width, -_mapRenderTexture.Texture.Height);
			Rectangle dst = new Rectangle(offsetX, offsetY, _mapRenderTexture.Texture.Width, _mapRenderTexture.Texture.Height);
			Raylib.DrawTexturePro(_mapRenderTexture.Texture, src, dst, Vector2.Zero, 0f, Color.White);

			Raylib.EndScissorMode();

			// ---- Bordure du panneau (une seule fois, plus de doublon) ----
			Raylib.DrawRectangleRoundedLines(panelRect, 0.04f, 8, 2, new Color(110, 110, 135, 220));

			// ---- Titre ----
			string title = Localization.Get("map.title");
			if (string.IsNullOrEmpty(title)) title = "Carte";
			FontManager.DrawText(title, mapX, mapY - 30, 20, new Color(230, 230, 240, 255));

			// ---- Boussole simple (N/E/S/O) ----
			int compassCx = mapX + mapAreaW - 26;
			int compassCy = mapY + 26;
			Raylib.DrawCircle(compassCx, compassCy, 20, new Color(0, 0, 0, 140));
			Raylib.DrawCircleLines(compassCx, compassCy, 20, new Color(150, 150, 170, 200));
			FontManager.DrawText("N", compassCx - 4, compassCy - 18, 14, Color.White);
			FontManager.DrawText("S", compassCx - 4, compassCy + 4, 14, new Color(180, 180, 190, 255));
			FontManager.DrawText("E", compassCx + 8, compassCy - 6, 14, new Color(180, 180, 190, 255));
			FontManager.DrawText("O", compassCx - 18, compassCy - 6, 14, new Color(180, 180, 190, 255));

			// ---- Marqueur joueur (taille constante, indépendante du zoom) ----
			Texture2D mapMarker = LoadMapMarkerTexture();
			float markerSize = 24f;

			float markerMapX = mapX + (playerTilePos.X - startTileX) * currentTileSize;
			float markerMapY = mapY + (playerTilePos.Y - startTileY) * currentTileSize;

			bool isMarkerInside = markerMapX >= mapX && markerMapX <= mapX + mapAreaW &&
								   markerMapY >= mapY && markerMapY <= mapY + mapAreaH;

			if (isMarkerInside)
			{
				Vector2 markerPos = new Vector2(markerMapX - markerSize / 2, markerMapY - markerSize / 2);
				// petite ombre sous le marqueur pour le détacher du fond
				Raylib.DrawEllipse((int)markerMapX, (int)(markerMapY + markerSize * 0.32f), markerSize * 0.35f, markerSize * 0.14f, new Color(0, 0, 0, 90));
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

			// ---- Marqueur du dernier lieu de mort ----
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

			// ---- Drag ----
			Vector2 mousePos = Raylib.GetMousePosition();
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
				_mapOffset.X += delta.X / currentTileSize;
				_mapOffset.Y += delta.Y / currentTileSize;
				_mapDragStart = mousePos;
			}
			if (Raylib.IsMouseButtonReleased(MouseButton.Left)) _isDraggingMap = false;

			// ---- Zoom (molette), même layout que l'affichage : plus de duplication ----
			if (Raylib.CheckCollisionPointRec(mousePos, new Rectangle(mapX, mapY, mapAreaW, mapAreaH)))
			{
				float wheel = Raylib.GetMouseWheelMove();
				if (wheel != 0)
				{
					Vector2 mouseMapPos = new Vector2(
						(mousePos.X - mapX) / currentTileSize,
						(mousePos.Y - mapY) / currentTileSize
					);

					_mapZoom = Math.Clamp(_mapZoom + wheel * MAP_ZOOM_STEP, MAP_ZOOM_MIN, MAP_ZOOM_MAX);

					MapLayout newLayout = ComputeMapLayout(sw, sh, _mapZoom);

					Vector2 newMouseMapPos = new Vector2(
						(mousePos.X - newLayout.MapX) / newLayout.TileSize,
						(mousePos.Y - newLayout.MapY) / newLayout.TileSize
					);

					float oldHalfWidth = visibleTilesX / 2f;
					float newHalfWidth = newLayout.VisibleTilesX / 2f;
				float oldHalfHeight = visibleTilesY / 2f;
				float newHalfHeight = newLayout.VisibleTilesY / 2f;

				_mapOffset += (mouseMapPos - newMouseMapPos);
				_mapOffset += new Vector2(newHalfWidth - oldHalfWidth, newHalfHeight - oldHalfHeight);
				}
			}

			// ---- Bandeau d'info (avec fond, pour rester lisible sur tout terrain) ----
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
#endif