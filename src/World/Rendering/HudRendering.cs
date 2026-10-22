// HudRendering.cs
// Prompts d'interaction sous la souris et bandeau « GROTTE ».
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
			/// <summary>Prompts d'interaction (portes, lits, cultures, coffres...) pour la tuile sous la souris.</summary>
			internal static void DrawInteractionPrompts(DrawWorldContext ctx)
			{
				bool isCarrying = ctx.isCarrying;
				int ts = ctx.ts;
				int startX = ctx.startX;
				int startY = ctx.startY;
				int width = ctx.width;
				int heightTiles = ctx.heightTiles;
				int[,] objectIds = ctx.objectIds;

				// Interactions UI
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
			}

			/// <summary>Bandeau « GROTTE » en sous-sol, avec indication de sortie.</summary>
			internal static void DrawUndergroundBanner(DrawWorldContext ctx)
			{
				Vector2 playerPos = ctx.playerPos;
				int sw = ctx.sw;
				int sh = ctx.sh;
				int ts = ctx.ts;

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
			}
        }
    }
}
