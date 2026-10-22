// GroundItemRendering.cs
// Items au sol (solo/host) et instantané réseau (client multijoueur).
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
			/// <summary>Ajoute les items au sol aux RenderItems.</summary>
			internal static void AddGroundItems(DrawWorldContext ctx)
			{
				int ts = ctx.ts;
				var renderItems = _renderItemsPool;

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
					if (!ctx.IsWorldRectVisible(gi.Position.X - itemRenderWidth / 2f, itemRenderTop,
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
						if (!ctx.IsWorldRectVisible(ngi.PosX - itemRenderWidth / 2f, itemRenderTop,
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
			}
        }
    }
}
