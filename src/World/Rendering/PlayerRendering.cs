// PlayerRendering.cs
// Joueurs locaux supplémentaires et joueur principal.
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
			/// <summary>Joueurs locaux supplémentaires (visibles dans toutes les vues).</summary>
			internal static void AddExtraPlayers(DrawWorldContext ctx)
			{
				Color pSkin = ctx.pSkin;
				List<Texture2D> hairBase = ctx.hairBase;
				List<Texture2D> hairOverlay = ctx.hairOverlay;
				bool isPlayerInWater = ctx.isPlayerInWater;
				float headAngle = ctx.headAngle;
				IReadOnlyList<Program.LocalPlayer>? playersToRender = ctx.playersToRender;
				int ts = ctx.ts;
				var renderItems = _renderItemsPool;

				// Joueurs locaux supplémentaires (visibles dans toutes les vues)
				if (playersToRender != null)
				{
					foreach (var extraPlayer in playersToRender)
					{
						if (!extraPlayer.Connected) continue;
						if (!ctx.IsEntityNearScreen(extraPlayer.Position)) continue;
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
			}

			/// <summary>Joueur principal (ou voiture) : ignore le ragdoll de mort, géré par CorpseSystem.</summary>
			internal static void AddMainPlayer(DrawWorldContext ctx)
			{
				Vector2 playerPos = ctx.playerPos;
				string anim = ctx.anim;
				int frame = ctx.frame;
				float prog = ctx.prog;
				float facing = ctx.facing;
				Color pSkin = ctx.pSkin;
				List<Texture2D> hairBase = ctx.hairBase;
				List<Texture2D> hairOverlay = ctx.hairOverlay;
				bool isCarrying = ctx.isCarrying;
				bool isPlayerInWater = ctx.isPlayerInWater;
				float headAngle = ctx.headAngle;
				Vector2? sittingPosition = ctx.sittingPosition;
				bool drawPlayer = ctx.drawPlayer;
				int ts = ctx.ts;
				var renderItems = _renderItemsPool;

				// Joueur ou voiture
				//  RAGDOLL : pendant le ragdoll de mort, le joueur n'est plus dessiné avec l'animation
				//  normale (le corps physique est dessiné par CorpseSystem, dans la passe de
				//  rendu du monde).
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
			}
        }
    }
}
