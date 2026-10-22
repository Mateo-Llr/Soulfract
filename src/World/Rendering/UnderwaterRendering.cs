// UnderwaterRendering.cs
// ⚠ CODE MORT : ancien rendu sous-marin (était sous « if (false) » dans DrawWorld). Jamais appelé.
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
			/// <summary>ANCIEN rendu sous-marin désactivé (était entouré de « if (false) »). Conservé tel quel ; supprimable.</summary>
			internal static void DrawUnderwaterView(DrawWorldContext ctx)
			{
				Vector2 playerPos = ctx.playerPos;
				string anim = ctx.anim;
				int frame = ctx.frame;
				float prog = ctx.prog;
				float facing = ctx.facing;
				Color pSkin = ctx.pSkin;
				Texture2D missingTex = ctx.missingTex;
				List<Entity> entities = ctx.entities;
				List<Texture2D> hairBase = ctx.hairBase;
				List<Texture2D> hairOverlay = ctx.hairOverlay;
				float headAngle = ctx.headAngle;
				float playerDepth = ctx.playerDepth;
				IReadOnlyList<Program.LocalPlayer>? playersToRender = ctx.playersToRender;
				int sw = ctx.sw;
				int sh = ctx.sh;
				int ts = ctx.ts;
				float currentTime = ctx.currentTime;
				int startX = ctx.startX;
				int endX = ctx.endX;
				int startY = ctx.startY;
				int endY = ctx.endY;

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
					if (!ctx.IsEntityNearScreen(entity.WorldPos))
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
						if (!ctx.IsEntityNearScreen(extraPlayer.Position)) continue;
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
        }
    }
}
