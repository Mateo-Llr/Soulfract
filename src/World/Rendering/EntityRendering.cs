// EntityRendering.cs
// Entités : snapshot réseau côté client, liste locale côté host/solo.
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
			/// <summary>Ajoute les entités visibles aux RenderItems (client : snapshot du host ; host/solo : liste entities).</summary>
			internal static void AddEntities(DrawWorldContext ctx)
			{
				List<Entity> entities = ctx.entities;
				List<Texture2D> hairBase = ctx.hairBase;
				List<Texture2D> hairOverlay = ctx.hairOverlay;
				int ts = ctx.ts;
				var renderItems = _renderItemsPool;

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
						Vector2 dtoVisualPos = dtoPos;
						if (!ctx.IsEntityNearScreen(dtoVisualPos))
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
					if (!ctx.IsEntityNearScreen(entityVisualPos))
						continue;
					{
					int tileX = (int)(entityVisualPos.X / ts);
					int tileY = (int)(entityVisualPos.Y / ts);
					if (!Program.IsTileLit(tileX, tileY)) continue;
					var e = entity;
					bool isTrader = e.IsTrader;
					Vector2 traderIconPos = new Vector2(entityVisualPos.X, entityVisualPos.Y - e.VisualOffsetY);
					renderItems.Add(new RenderItem
					{
						Y = entityVisualPos.Y + Program.FeetOffsetY,
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
								e.Facing, new Vector2(entityVisualPos.X, entityVisualPos.Y - e.VisualOffsetY), e.Tint,
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
								Vector2 entityDrawPos = new Vector2(e.WorldPos.X, e.WorldPos.Y - e.VisualOffsetY);
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
								Vector2 entityDrawPos = new Vector2(e.WorldPos.X, e.WorldPos.Y - e.VisualOffsetY);
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
						Vector2 iconWorldPos = new Vector2(e.WorldPos.X, e.WorldPos.Y - e.VisualOffsetY);
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
						float entityYOffset = 0f;
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

				//  CORPS MORTS (ragdoll) : ils sont rendus par le MÊME moteur de rendu que
				//  les entités vivantes (mêmes textures, mêmes équipements, mêmes teintures) :
				//  seule la pose change, elle vient du solveur physique au lieu de l'animation.
				//  Le corps est trié au niveau de ses pieds, comme une entité debout.
				if (CorpseSystem.Corpses.Count > 0)
				{
					var capturedHairBase = hairBase;
					var capturedHairOverlay = hairOverlay;
					foreach (var corpse in CorpseSystem.Corpses)
					{
						if (!ctx.IsEntityNearScreen(corpse.Position)) continue;
						var c = corpse;
						renderItems.Add(new RenderItem
						{
							Y = c.SortY,
							DrawAction = () => c.Draw(capturedHairBase, capturedHairOverlay)
						});
					}
				}
			}
        }
    }
}
