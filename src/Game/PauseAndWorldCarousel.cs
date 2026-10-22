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
		static void DrawPauseMenu()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();

			// ── Animation d'ouverture / fermeture ──
			const float SLIDE_SPEED = 5f;
			float dt = Raylib.GetFrameTime();
			if (_pauseMenuClosing)
			{
				_pauseMenuSlide -= dt * SLIDE_SPEED;
				if (_pauseMenuSlide <= 0f)
				{
					_pauseMenuSlide = 0f;
					_pauseMenuClosing = false;
					var target = _pausePendingState;
					var action = _pausePendingAction;
					_pausePendingAction = null;
					action?.Invoke();
					_gameState = target;
					return;
				}
			}
			else if (_pauseMenuSlide < 1f)
			{
				_pauseMenuSlide += dt * SLIDE_SPEED;
				if (_pauseMenuSlide > 1f) _pauseMenuSlide = 1f;
			}
			float eased = 1f - MathF.Pow(1f - _pauseMenuSlide, 3f);

			Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, (int)(120 * eased)));

			// ── Panneau nine-slice "scroll" ──
			int panelWidth = 450;
			bool canHost = !NetworkManager.IsOnline;
			var pauseMenuItemsList = new List<(string Key, string Label)>
			{
				("pause.button.resume", Localization.Get("pause.button.resume"))
			};
			if (canHost) pauseMenuItemsList.Add(("pause.button.host", Localization.Get("pause.button.host")));
			pauseMenuItemsList.Add(("pause.button.options", Localization.Get("pause.button.options")));
			pauseMenuItemsList.Add(("pause.button.quit", Localization.Get("pause.button.quit")));
			var pauseMenuItems = pauseMenuItemsList.ToArray();

			int titleAreaHeight = 140; // Réduit pour laisser plus de place au titre
			int buttonWidth = 320;
			int buttonHeight = 56;
			int buttonSpacing = 22;
			int panelPadding = 30;
			int buttonsHeight = pauseMenuItems.Length * buttonHeight + (pauseMenuItems.Length - 1) * buttonSpacing;
			int panelHeight = titleAreaHeight + buttonsHeight + panelPadding * 2 + 75;

			int panelX = (sw - panelWidth) / 2;
			int restingPanelY = (sh - panelHeight) / 2;
			int hiddenPanelY = -panelHeight - 20;
			int panelY = (int)MathHelper.Lerp(hiddenPanelY, restingPanelY, eased);

			Rectangle panelRect = new Rectangle(panelX, panelY, panelWidth, panelHeight);
			if (UIManager.ScrollPanelTexture != null && UIManager.ScrollPanelTexture.IsValid)
			{
				UIManager.ScrollPanelTexture.Draw(panelRect, Color.White);
				UIManager.ScrollBorderTexture?.DrawAlignedToNineSlice(panelRect, UIManager.ScrollPanelTexture, Color.White);
			}
			else
			{
				Raylib.DrawRectangleRounded(panelRect, 0.05f, 8, new Color(45, 35, 25, 235));
				Raylib.DrawRectangleRoundedLines(panelRect, 0.05f, 8, 2, new Color(200, 170, 100, 200));
			}

			// ── Titre en TRÈS GROS, centré en haut du parchemin ──
			Texture2D titleTex = _menuTitle;
			if (titleTex.Id != 0)
			{
				//  TITRE PLUS GROS : 2.4x au lieu de 1.6x (ou plus selon vos préférences)
				float titleScale = Math.Min(2.4f, (float)(panelWidth - 40) / titleTex.Width);
				int titleWidth = (int)(titleTex.Width * titleScale);
				int titleX = panelX + (panelWidth - titleWidth) / 2;
				int titleY = panelY + 12; // Décalé vers le haut pour rester dans le panneau
				SafeDrawTexture(titleTex, new Vector2(titleX, titleY), 0, titleScale, Color.White);
			}
			else
			{
				// Fallback textuel si la texture est manquante : police plus grande
				string title = Localization.Get("pause.title");
				int titleFontSize = 60; // Augmenté de 72 à 60 (ajusté pour ne pas déborder)
				int titleWidth = FontManager.MeasureText(title, titleFontSize);
				FontManager.DrawText(title, panelX + (panelWidth - titleWidth) / 2, panelY + 16, titleFontSize, new Color(230, 200, 100, 255));
			}

			// ── Boutons, en nine-slice "inventory" ──
			int buttonX = panelX + (panelWidth - buttonWidth) / 2;
			int startButtonY = panelY + titleAreaHeight + panelPadding - 10; // Ajusté
			Vector2 mousePos = Raylib.GetMousePosition();
			for (int i = 0; i < pauseMenuItems.Length; i++)
			{
				int currentButtonY = startButtonY + i * (buttonHeight + buttonSpacing);
				Rectangle buttonRect = new Rectangle(buttonX, currentButtonY, buttonWidth, buttonHeight);
				bool interactive = !_pauseMenuClosing && _pauseMenuSlide >= 0.999f && !_isMultiplayerMenu;
				bool isHovered = interactive && Raylib.CheckCollisionPointRec(mousePos, buttonRect);
				if (!interactive)
				{
					_prevPauseButtonsHovered.Clear();
				}
				else
				{
					if (isHovered && !_prevPauseButtonsHovered.Contains(i)) PlayHoverSound();
					if (isHovered) _prevPauseButtonsHovered.Add(i); else _prevPauseButtonsHovered.Remove(i);
				}

				int fontSize = 28;
				int textWidth = FontManager.MeasureText(pauseMenuItems[i].Label, fontSize);
				Color buttonShade = isHovered
					? new Color(92, 48, 26, 125)
					: new Color(120, 68, 38, 55);
				Raylib.DrawRectangleRounded(buttonRect, 0.12f, 6, buttonShade);
				DrawCreationText(
					pauseMenuItems[i].Label,
					(int)(buttonX + buttonWidth / 2 - textWidth / 2),
					(int)(currentButtonY + buttonHeight / 2 - fontSize / 2),
					fontSize,
					Color.White);

				if (isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					PlayButtonClickSound();
					string action = pauseMenuItems[i].Key;
					if (action == "pause.button.resume")
					{
						RequestClosePauseMenu(GameState.Playing);
					}
					else if (action == "pause.button.host")
					{
						RequestClosePauseMenu(GameState.Playing, () =>
						{
							_mpPseudoInput = string.IsNullOrWhiteSpace(LocalPlayerName) ? Localization.Get("multiplayer.default_host_name") : LocalPlayerName;
							_mpFocusedField = 0;
							_mpStatusMessage = "";
							_isHostingFromPause = true;
							_isHostSetup = true;
							_isMultiplayerMenu = true;
						});
					}
					else if (action == "pause.button.options")
					{
						RequestClosePauseMenu(GameState.Options, () => OpenOptions(GameState.Paused));
					}
					else if (action == "pause.button.quit")
					{
						RequestClosePauseMenu(GameState.Playing, () =>
						{
							StartReturnToMainMenuFade();
						});
					}
				}
			}

			// ── Bouton "Succès" ──
			if (_achievementsMenuIcon.Id != 0 && _achievementsMenuIcon.Width > 0)
			{
				int achSize = 48;
				int achX = panelX + panelWidth - achSize - 24;
				int achY = panelY + panelHeight - achSize - 20;
				Rectangle achRect = new Rectangle(achX, achY, achSize, achSize);
				bool achInteractive = !_pauseMenuClosing && _pauseMenuSlide >= 0.999f && !_isMultiplayerMenu;
				bool achHovered = achInteractive && Raylib.CheckCollisionPointRec(mousePos, achRect);
				if (!achInteractive) _prevAchievementsIconHovered = false;
				UpdateButtonHoverSound(achHovered, ref _prevAchievementsIconHovered);
				Color achBg = achHovered ? new Color(60, 50, 35, 220) : new Color(25, 25, 35, 180);
				Raylib.DrawRectangleRounded(achRect, 0.25f, 8, achBg);
				Raylib.DrawRectangleRoundedLines(achRect, 0.25f, 8, 2, achHovered ? new Color(200, 170, 100, 255) : new Color(100, 90, 70, 180));
				Rectangle achSrc = new Rectangle(0, 0, _achievementsMenuIcon.Width, _achievementsMenuIcon.Height);
				Rectangle achDst = new Rectangle(achX + 6, achY + 6, achSize - 12, achSize - 12);
				Raylib.DrawTexturePro(_achievementsMenuIcon, achSrc, achDst, Vector2.Zero, 0, Color.White);
				if (achHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					PlayButtonClickSound();
					RequestClosePauseMenu(GameState.Achievements, () =>
					{
						_optionsReturnState = GameState.Paused;
						AchievementManager.OpenMenu();
					});
				}
			}

			string resumeHint = Localization.Get("pause.hint.resume");
			int hintWidth = FontManager.MeasureText(resumeHint, 12);
			FontManager.DrawText(resumeHint, panelX + panelWidth - hintWidth - 20, panelY + panelHeight - 25, 12, new Color(200, 190, 160, (int)(180 * eased)));
		}

		// Carrousel interactif des mondes disponibles + bouton "Créer un monde" tout à droite.
		// Jusqu'à 5 éléments affichés : le centre (plus gros, c'est lui qu'on lance),
		// puis 2 de chaque côté, de plus en plus petits et sombres en s'éloignant du centre.
		// Reconstruit le cache des aperçus (équipement + apparence) pour tous les mondes
		// actuellement dans _availableSaves. Fait le travail lourd (lecture disque, parse JSON,
		// reconstruction de l'Equipment, chargement des textures) UNE SEULE FOIS par changement
		// de liste, au lieu de le refaire à chaque frame comme avant.
		static void RebuildCarouselPreviewCache()
		{
			_carouselPreviewCache.Clear();

			foreach (var save in _availableSaves)
			{
				string savePath = Path.Combine("Saves", save.Name + ".json");
				GameSaveData? preview = null;
				if (File.Exists(savePath))
				{
					try
					{
						string json = File.ReadAllText(savePath);
						var opts = new JsonSerializerOptions
						{
							NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
							PropertyNameCaseInsensitive = true
						};
						preview = JsonSerializer.Deserialize<GameSaveData>(json, opts);
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Erreur de désérialisation pour {save.Name} : {ex.Message}");
						preview = null;
					}
				}

				var tempEquipment = new Equipment();

				// --- Restauration de l'équipement (zone items) ---
				if (preview != null)
				{
					foreach (var zoneSave in preview.Equipment.ZoneItems)
					{
						if (string.IsNullOrEmpty(zoneSave.ItemName)) continue;
						var itemDef = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == zoneSave.ItemName);
						if (itemDef.ID == 0) continue;
						var zoneItem = new Item(itemDef.Name, 1, itemDef.Color, itemDef.Icon);
						var loadedColors = SaveSystem.LoadCustomColors(zoneSave.CustomColors);
						if (loadedColors != null && loadedColors.Count > 0)
							zoneItem.CustomColors = loadedColors;
						zoneItem.RestoreMetadataFromSave(zoneSave.Metadata ?? "");
						if (zoneSave.Meta != null && zoneSave.Meta.Count > 0)
							foreach (var kv in zoneSave.Meta)
								zoneItem.Meta[kv.Key] = kv.Value;
						if (itemDef.RuneSlots > 0)
							SaveSystem.RestoreRunes(zoneItem, itemDef.RuneSlots, zoneSave.Runes, zoneSave.Metadata);
						SaveSystem.RestoreCoatVariantMeta(zoneItem, itemDef);
						tempEquipment.EquipOnBody(zoneItem, out _);
					}

					if (!string.IsNullOrEmpty(preview.Equipment.MainHand))
					{
						var item = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == preview.Equipment.MainHand);
						if (item.ID != 0)
						{
							tempEquipment.MainHand = new Item(item.Name, 1, item.GetColor(), item.Icon);
							var lc = SaveSystem.LoadCustomColors(preview.Equipment.MainHandCustomColors);
							if (lc != null && lc.Count > 0)
								tempEquipment.MainHand.CustomColors = lc;
						}
					}

					if (!string.IsNullOrEmpty(preview.Equipment.OffHand))
					{
						var item = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == preview.Equipment.OffHand);
						if (item.ID != 0)
						{
							tempEquipment.OffHand = new Item(item.Name, 1, item.GetColor(), item.Icon);
							var lc = SaveSystem.LoadCustomColors(preview.Equipment.OffHandCustomColors);
							if (lc != null && lc.Count > 0)
								tempEquipment.OffHand.CustomColors = lc;
						}
					}

					// Conteneur portable (panier)
					if (preview.Equipment.OffHandContainer != null)
					{
						int slotCount = preview.Equipment.OffHandContainer.Slots.Count;
						int columns = preview.Equipment.OffHandContainer.Columns > 0 ? preview.Equipment.OffHandContainer.Columns : 3;
						var container = new PortableContainerData(slotCount, columns);
						for (int i = 0; i < preview.Equipment.OffHandContainer.Slots.Count && i < container.Inventory.Slots.Count; i++)
						{
							var savedSlot = preview.Equipment.OffHandContainer.Slots[i];
							if (!savedSlot.IsEmpty && !string.IsNullOrEmpty(savedSlot.ItemName))
							{
								var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == savedSlot.ItemName);
								if (itemData.ID != 0)
								{
									var contItem = new Item(itemData.Name, savedSlot.Count, itemData.Color, itemData.Icon);
									var lc = SaveSystem.LoadCustomColors(savedSlot.CustomColors);
									if (lc != null && lc.Count > 0)
										contItem.CustomColors = lc;
									container.Inventory.Slots[i].Item = contItem;
									container.Inventory.Slots[i].Count = savedSlot.Count;
								}
							}
						}
						tempEquipment.OffHandContainer = container;
						if (tempEquipment.OffHand != null)
							tempEquipment.OffHand.Container = container;
					}

					// Sac à dos
					if (preview.Equipment.BackpackContainer != null)
					{
						int slotCount = preview.Equipment.BackpackContainer.Slots.Count;
						int columns = preview.Equipment.BackpackContainer.Columns > 0 ? preview.Equipment.BackpackContainer.Columns : 5;
						var backpack = new BackpackData(slotCount, columns);
						for (int i = 0; i < preview.Equipment.BackpackContainer.Slots.Count && i < backpack.Inventory.Slots.Count; i++)
						{
							var savedSlot = preview.Equipment.BackpackContainer.Slots[i];
							if (!savedSlot.IsEmpty && !string.IsNullOrEmpty(savedSlot.ItemName))
							{
								var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == savedSlot.ItemName);
								if (itemData.ID != 0)
								{
									var backItem = new Item(itemData.Name, savedSlot.Count, itemData.Color, itemData.Icon);
									var lc = SaveSystem.LoadCustomColors(savedSlot.CustomColors);
									if (lc != null && lc.Count > 0)
										backItem.CustomColors = lc;
									backpack.Inventory.Slots[i].Item = backItem;
									backpack.Inventory.Slots[i].Count = savedSlot.Count;
								}
							}
						}
						tempEquipment.BackpackContainer = backpack;
						if (tempEquipment.Backpack != null)
							tempEquipment.Backpack.Backpack = backpack;
					}

					// EquippedItems (armures/accessoires dans les slots)
					if (preview.Equipment.EquippedItems != null && preview.Equipment.EquippedItems.Count > 0)
					{
						foreach (var equippedSave in preview.Equipment.EquippedItems)
						{
							if (string.IsNullOrEmpty(equippedSave.ItemName)) continue;
							var itemData = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == equippedSave.ItemName);
							if (itemData.ID == 0) continue;
							var item = new Item(itemData.Name, equippedSave.Count, itemData.Color, itemData.Icon);
							var lc = SaveSystem.LoadCustomColors(equippedSave.CustomColors);
							if (lc != null && lc.Count > 0)
								item.CustomColors = lc;
									item.RestoreMetadataFromSave(equippedSave.Metadata ?? "");
									if (equippedSave.Meta != null && equippedSave.Meta.Count > 0)
										foreach (var kv in equippedSave.Meta)
											item.Meta[kv.Key] = kv.Value;
							if (itemData.RuneSlots > 0)
								SaveSystem.RestoreRunes(item, itemData.RuneSlots, equippedSave.Runes, equippedSave.Metadata);
							SaveSystem.RestoreCoatVariantMeta(item, itemData);
							tempEquipment.EquipItem(item, equippedSave.Slot, equippedSave.Index);
						}
					}
				}

				tempEquipment.LoadEquipmentTextures();

				// --- Couleurs ---
				// Couleur de peau : si preview est null ou que les composantes sont toutes à 0, on utilise une valeur par défaut fixe
				Color defaultSkin = new Color(255, 235, 200, 255);
				Color skin;
				if (preview != null && (preview.PlayerSkinColorR != 0 || preview.PlayerSkinColorG != 0 || preview.PlayerSkinColorB != 0))
				{
					skin = new Color(
						(byte)preview.PlayerSkinColorR,
						(byte)preview.PlayerSkinColorG,
						(byte)preview.PlayerSkinColorB,
						(byte)preview.PlayerSkinColorA
					);
				}
				else
				{
					skin = defaultSkin;
				}

				// Couleur des cheveux : fallback sur une couleur brune par défaut si absente
				Color hairColor = preview != null
					? new Color((byte)preview.PlayerHairColorR, (byte)preview.PlayerHairColorG, (byte)preview.PlayerHairColorB, (byte)preview.PlayerHairColorA)
					: new Color(60, 40, 30, 255);

				// Styles
				int hairIdx = preview != null ? Math.Clamp(preview.PlayerHairStyle, 0, Program.hairBaseTextures.Count - 1) : 0;
				Texture2D hBase = Program.hairBaseTextures.Count > 0 ? Program.hairBaseTextures[hairIdx] : new Texture2D();
				Texture2D hOverlay = Program.hairOverlayTextures.Count > 0 ? Program.hairOverlayTextures[hairIdx] : new Texture2D();

				int beardStyle = preview != null ? preview.PlayerBeardStyle : 0;

				string species = preview != null && !string.IsNullOrWhiteSpace(preview.PlayerMorphSpecies)
					? preview.PlayerMorphSpecies
					: "human";
				Color morphTint = preview?.PlayerMorphTint == null
					? Color.White
					: new Color(preview.PlayerMorphTint.R, preview.PlayerMorphTint.G, preview.PlayerMorphTint.B, preview.PlayerMorphTint.A);
				var featureVariants = preview?.PlayerMorphFeatureVariants?.ToDictionary(feature => feature.Key, feature => feature.Value)
					?? new Dictionary<string, int>();
				var featureColors = preview?.PlayerMorphFeatureColors?.ToDictionary(
					feature => feature.Key,
					feature => new Color(feature.Value.R, feature.Value.G, feature.Value.B, feature.Value.A))
					?? new Dictionary<string, Color>();
				if (!string.Equals(species, "human", StringComparison.OrdinalIgnoreCase)
					&& preview?.PlayerMorphFeatureVariants == null && preview?.PlayerMorphFeatureColors == null)
				{
					Entity.GenerateRandomAppearance(species, Vector2.Zero, out morphTint, featureVariants, featureColors);
				}

				_saveBackgroundStyles[save.Name] = preview?.CharacterSelectionBackgroundStyle ?? 0;
				_carouselPreviewCache[save.Name] = new CarouselPreviewCache
				{
					Equipment = tempEquipment,
					Skin = skin,
					HairColor = hairColor,
					HairBase = hBase,
					HairOverlay = hOverlay,
					BeardStyle = beardStyle,
					Species = species,
					MorphTint = morphTint,
					FeatureVariants = featureVariants,
					FeatureColors = featureColors
				};
			}

			EntityRenderer.ClearEquipmentCache();
			_carouselCacheDirty = false;
		}

		static void DrawWorldCarousel(int sw, int sh, float visibility, Vector2 mousePos)
		{
			// Plus de SaveSystem.GetSaves() ici : ça scannait le disque à CHAQUE FRAME.
			// _availableSaves est déjà à jour (rafraîchi via RefreshAvailableSaves()).
			if (_carouselCacheDirty)
			{
				RebuildCarouselPreviewCache();
			}
			int worldCount = _availableSaves.Count;
			int totalItems = worldCount + 2; // +2 = bouton "Rejoindre une partie" + bouton "Créer un monde"
			_worldCarouselIndex = Math.Clamp(_worldCarouselIndex, 0, totalItems - 1);

			// Le carrousel est ouvert : la caméra-vitrine du menu (Program_MenuPreview)
			// suit le personnage correspondant à la sauvegarde actuellement au centre,
			// au lieu du panorama automatique. Slots 0/1 = "Rejoindre"/"Créer", donc
			// les sauvegardes commencent à l'index 2 (voir label plus bas).
			// Lissage de l'index sélectionné pour une animation de glissement fluide.
			float diff = _worldCarouselIndex - _worldCarouselAnimIndex;
			_worldCarouselAnimIndex += diff * Math.Min(1f, Raylib.GetFrameTime() * 10f);
			if (Math.Abs(diff) < 0.01f) _worldCarouselAnimIndex = _worldCarouselIndex;

			int centerY = (int)(sh * 0.56f);
			float slotSpacing = sw * 0.19f;
			float centerScaleMax = Math.Min(sw, sh) * 0.00042f; // facteur d'échelle pour le personnage central

			// On affiche au plus 2 voisins de chaque côté du centre (5 éléments visibles au total).
				for (int offset = -2; offset <= 2; offset++)
				{
					int itemIndex = _worldCarouselIndex + offset;
				if (itemIndex < 0 || itemIndex >= totalItems) continue;

				float slideOffset = itemIndex - _worldCarouselAnimIndex; // position réelle lissée (peut être fractionnaire)

				float absOffset = Math.Abs(slideOffset);
				float distanceFactor = Math.Clamp(absOffset, 0f, 2f) / 2f; // 0 = centre, 1 = bord

				float x = sw / 2f + slideOffset * slotSpacing;
				float scale = (1f - distanceFactor * 0.62f) * centerScaleMax * visibility;
				byte darken = (byte)(255 - distanceFactor * 150);
				Color tint = new Color(darken, darken, darken, (byte)(255 * visibility));

				bool isJoinSlot = itemIndex == 0;
				bool isCreateSlot = itemIndex == 1;
				// Déterminer la taille en interpolant en continu selon la distance lissée (fluidité)
				float normalized = Math.Clamp(absOffset / 2f, 0f, 1f); // 0 = centre, 1 = bord
				float sizeMultiplier = MathHelper.Lerp(4f, 2f, normalized); // 4x au centre → 2x au bord
				float baseCharHeight = Math.Min(sw, sh) * 0.06f; // hauteur de base pour multiplier

				float entryDelay = Math.Clamp(Math.Abs(slideOffset) * 0.08f, 0f, 0.55f);
				float entryProgress = Math.Clamp((visibility - entryDelay) / Math.Max(0.001f, 1f - entryDelay), 0f, 1f);
				float entryEase = 1f - (float)Math.Pow(1f - entryProgress, 3f);
				float impulseScale = 1f + (float)Math.Sin(entryEase * MathF.PI) * 0.08f;
				float appearScale = MathHelper.Lerp(0.12f, 1f, entryEase);
				float appearAlpha = MathHelper.Lerp(0f, 1f, entryEase);
				float bounceOffset = (1f - entryEase) * 14f;

				float texH = baseCharHeight * sizeMultiplier * appearScale * visibility;
				float texW = texH * 0.6f;
				float baseY = centerY - texH * 0.5f - bounceOffset;

				// Socle d'ombre sous le personnage (pas sur le bouton "Créer un monde").
				if (!isCreateSlot && !isJoinSlot)
				{
					float shadowW = texW * 0.7f;
					float shadowH = shadowW * 0.28f;
					Raylib.DrawEllipse((int)x, (int)(baseY + texH * 0.92f), shadowW / 2f, shadowH / 2f, new Color((byte)0, (byte)0, (byte)0, (byte)(140 * appearAlpha * visibility * (1f - distanceFactor * 0.3f))));
				}

				// Socle décoratif (assets/gui/save_base.png), proportionnel à la taille du personnage.
				if (_carouselSaveBase.Id != 0 && _carouselSaveBase.Width > 0 && _carouselSaveBase.Height > 0)
				{
					float baseTexW = texW * 1.95f;
					float baseTexH = baseTexW * ((float)_carouselSaveBase.Height / _carouselSaveBase.Width);
					Rectangle baseSrc = new Rectangle(0, 0, _carouselSaveBase.Width, _carouselSaveBase.Height);
					Rectangle baseDst = new Rectangle(x, baseY + texH * 0.98f, baseTexW, baseTexH);
					Vector2 baseOrigin = new Vector2(baseTexW / 2f, baseTexH / 2f);
					Raylib.DrawTexturePro(_carouselSaveBase, baseSrc, baseDst, baseOrigin, 0, new Color(tint.R, tint.G, tint.B, (byte)(tint.A * appearAlpha)));
				}

				// Ne pas dessiner seulement si l'aperçu est complètement hors-écran
				float halfW = texW / 2f;
				if (x + halfW < 0f - halfW || x - halfW > sw + halfW) continue;

				if (isJoinSlot)
				{
					Texture2D charTex = _mainMenuJoinWorldIcon;
					if (charTex.Id != 0 && charTex.Width != 0)
					{
						float iconW = texW * 1.02f;
						float iconH = iconW * ((float)charTex.Height / charTex.Width);
						if (iconH > texH * 1.02f)
						{
							iconH = texH * 1.02f;
							iconW = iconH * ((float)charTex.Width / charTex.Height);
						}
						Rectangle src = new Rectangle(0, 0, charTex.Width, charTex.Height);
						Rectangle dst = new Rectangle(x, baseY + texH / 2f, iconW, iconH);
						Vector2 origin = new Vector2(iconW / 2f, iconH / 2f);
						Raylib.DrawTexturePro(charTex, src, dst, origin, 0, new Color(tint.R, tint.G, tint.B, (byte)(tint.A * appearAlpha)));
					}
				}
				else if (isCreateSlot)
				{
					Texture2D charTex = _mainMenuCreateWorldIcon;
					if (charTex.Id != 0 && charTex.Width != 0)
					{
						float iconW = texW * 1.02f;
						float iconH = iconW * ((float)charTex.Height / charTex.Width);
						if (iconH > texH * 1.02f)
						{
							iconH = texH * 1.02f;
							iconW = iconH * ((float)charTex.Width / charTex.Height);
						}
						Rectangle src = new Rectangle(0, 0, charTex.Width, charTex.Height);
						Rectangle dst = new Rectangle(x, baseY + texH / 2f, iconW, iconH);
						Vector2 origin = new Vector2(iconW / 2f, iconH / 2f);
						Raylib.DrawTexturePro(charTex, src, dst, origin, 0, new Color(tint.R, tint.G, tint.B, (byte)(tint.A * appearAlpha)));
					}
				}
				else
				{
					//  Aperçu lu depuis le cache (plus de lecture/parse JSON ni de rechargement
					// de textures d'équipement à chaque frame — voir RebuildCarouselPreviewCache).
					int previewSaveIndex = itemIndex - 2;
					string saveName = _availableSaves[previewSaveIndex].Name;
					if (!_carouselPreviewCache.TryGetValue(saveName, out var cached))
					{
						// Filet de sécurité : ne devrait pas arriver, mais on reconstruit tout le
						// cache si une entrée manque (ex: sauvegarde ajoutée hors cycle normal).
						RebuildCarouselPreviewCache();
						_carouselPreviewCache.TryGetValue(saveName, out cached);
					}

					if (cached != null)
					{
						Vector2 previewPos = new Vector2(x, baseY + texH * 0.6f);
						float previewScale = sizeMultiplier * appearScale * impulseScale; // appliquer le multiplicateur directement avec l'effet d'impulsion

						// Calculer le regard local pour que l'aperçu du carrousel suive la souris
						var gaze = ComputeGazeOffsetsFor(previewPos, Raylib.GetMousePosition(), 1f);

						EntityRenderer.DrawEntity(
							speciesName: cached.Species,
							anim: "idle",
							frame: 0,
							prog: 0f,
							facing: 1f,
							pos: previewPos,
								tint: string.Equals(cached.Species, "human", StringComparison.OrdinalIgnoreCase) ? cached.Skin : cached.MorphTint,
							hBase: cached.HairBase,
							hOverlay: cached.HairOverlay,
							hColor: cached.HairColor,
							skeletons: SpeciesData.Skeletons,
							eyes: Program.EyesTexture,
							mouth: Program.MouthTexture,
							customScale: previewScale,
							equipment: cached.Equipment,
							isCarrying: false,
							inWater: false,
							attackSwingProgress: 0f,
							heldItemTexture: default,
							headAngle: 0f,
							keepItemHorizontal: false,
							isBow: false,
							underwearTexture: Program.LeafUnderpantsTexture,
							beardStyle: cached.BeardStyle,
								randomFeatureVariant: cached.FeatureVariants,
								randomFeatureColor: cached.FeatureColors,
							pupilLeftOffset: gaze.pupilLeft,
							pupilRightOffset: gaze.pupilRight,
							eyebrowLeftOffset: gaze.browLeft,
							eyebrowRightOffset: gaze.browRight
						);
					}
				}

				Rectangle hitRect = new Rectangle(x - texW / 2f, baseY, texW, texH);
				bool isCenter = itemIndex == _worldCarouselIndex && absOffset < 0.5f;
				bool hovered = Raylib.CheckCollisionPointRec(mousePos, hitRect);

				if (isCenter)
				{
					_mainMenuCenterHovered = hovered;
					string label = isJoinSlot ? Localization.Get("menu.join_game") : isCreateSlot ? Localization.Get("menu.create_world") : _availableSaves[itemIndex - 2].Name;
					int labelFontSize = Math.Max(16, (int)(sh * 0.022f));
					int labelWidth = FontManager.MeasureText(label, labelFontSize);
					// Relever légèrement l'interface (elle était calée sur les pieds du personnage)
				float overlayRaise = Math.Max(42f, texH * 0.38f);
				int labelX = (int)(x - labelWidth / 2f);
				int labelY = (int)(baseY + texH + 12 - overlayRaise);
				
				// Affichage du prénom avec bordure noire (comme en multijoueur)
				// Bordure noire : le texte est redessiné en noir tout autour avant la passe finale en blanc
				foreach (var (ox, oy) in new (int, int)[] { (-1, -1), (1, -1), (-1, 1), (1, 1), (-1, 0), (1, 0), (0, -1), (0, 1) })
				{
					FontManager.DrawText(label, labelX + ox, labelY + oy, labelFontSize, new Color((byte)0, (byte)0, (byte)0, (byte)(230 * visibility)));
				}
				FontManager.DrawText(label, labelX, labelY, labelFontSize, new Color((byte)255, (byte)255, (byte)255, (byte)(255 * visibility)));
			}

			// Bouton "supprimer" (assets/gui/bin.png), sous le rendu de chaque monde existant
			// (pas sur le bouton "Créer un monde" ou "Rejoindre une partie"). Sa taille suit celle du personnage affiché.
			bool binHovered = false;
			Rectangle binRect = default;
			int saveIndex = itemIndex - 2;
			if (!isCreateSlot && !isJoinSlot && saveIndex >= 0 && saveIndex < _availableSaves.Count && _worldDeleteIcon.Id != 0 && _worldDeleteIcon.Width > 0 && _worldDeleteIcon.Height > 0)
			{
				float binSize = Math.Max(22f, texW * 0.32f);
				// Réhausser la poubelle lorsque l'élément est au centre pour qu'elle suive l'overlay relevé
				float binY = (isCenter ? baseY + texH + 24 + Math.Max(18, (int)(sh * 0.026f)) + 16 : baseY + texH + 22) - Math.Max(42f, texH * 0.38f);
				binRect = new Rectangle(x - binSize / 2f, binY, binSize, binSize);
				binHovered = Raylib.CheckCollisionPointRec(mousePos, binRect) && visibility > 0.99f;

				Color binBg = binHovered ? new Color((byte)90, (byte)40, (byte)40, (byte)(220 * visibility)) : new Color((byte)40, (byte)25, (byte)25, (byte)(180 * visibility));
				Raylib.DrawRectangleRounded(binRect, 0.25f, 6, binBg);
				Raylib.DrawRectangleRoundedLines(binRect, 0.25f, 6, 1, new Color((byte)150, (byte)90, (byte)90, (byte)(200 * visibility)));

				float pad = binSize * 0.18f;
				Rectangle binSrc = new Rectangle(0, 0, _worldDeleteIcon.Width, _worldDeleteIcon.Height);
				Rectangle binDst = new Rectangle(binRect.X + pad, binRect.Y + pad, binRect.Width - pad * 2, binRect.Height - pad * 2);
				Raylib.DrawTexturePro(_worldDeleteIcon, binSrc, binDst, Vector2.Zero, 0, new Color((byte)255, (byte)255, (byte)255, (byte)(255 * visibility)));

				if (binHovered && _worldPendingDeleteName == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					_worldPendingDeleteName = _availableSaves[saveIndex].Name;
				}
			}

			if (hovered && !binHovered && _worldPendingDeleteName == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				if (isCenter)
				{
					ConfirmWorldCarouselSelection();
				}
				else
				{
					_worldCarouselIndex = itemIndex; // clic sur un côté : on le fait glisser au centre
				}
			}
		}

		// Flèches gauche/droite (indicateurs visuels, la navigation clavier fonctionne aussi).
		byte arrowAlpha = (byte)(200 * visibility);
			Color arrowTint = new Color((byte)220, (byte)200, (byte)160, arrowAlpha);
			if (_carouselArrowIcon.Id != 0 && _carouselArrowIcon.Width > 0 && _carouselArrowIcon.Height > 0)
			{
				float arrowH = Math.Max(28f, sh * 0.05f);
				float arrowW = arrowH * ((float)_carouselArrowIcon.Width / _carouselArrowIcon.Height);
				Rectangle arrowSrcRight = new Rectangle(0, 0, _carouselArrowIcon.Width, _carouselArrowIcon.Height);
				Rectangle arrowSrcLeft = new Rectangle(_carouselArrowIcon.Width, 0, -_carouselArrowIcon.Width, _carouselArrowIcon.Height);
				Vector2 arrowOrigin = new Vector2(arrowW / 2f, arrowH / 2f);

				if (_worldCarouselIndex > 0)
				{
					// La texture pointe vers la droite par défaut : rectangle source inversé pour pointer vers la gauche.
					Rectangle dst = new Rectangle(sw * 0.06f + arrowW / 2f, centerY, arrowW, arrowH);
					Raylib.DrawTexturePro(_carouselArrowIcon, arrowSrcLeft, dst, arrowOrigin, 0, arrowTint);
				}
				if (_worldCarouselIndex < totalItems - 1)
				{
					Rectangle dst = new Rectangle(sw * 0.92f + arrowW / 2f, centerY, arrowW, arrowH);
					Raylib.DrawTexturePro(_carouselArrowIcon, arrowSrcRight, dst, arrowOrigin, 0, arrowTint);
				}
			}
			else
			{
				// Repli si la texture est manquante : anciens indicateurs texte.
				int arrowFontSize = Math.Max(24, (int)(sh * 0.04f));
				if (_worldCarouselIndex > 0)
					FontManager.DrawText("<", (int)(sw * 0.06f), centerY - arrowFontSize / 2, arrowFontSize, arrowTint);
				if (_worldCarouselIndex < totalItems - 1)
					FontManager.DrawText(">", (int)(sw * 0.92f), centerY - arrowFontSize / 2, arrowFontSize, arrowTint);
			}
		}

		//  MULTIJOUEUR EN LIGNE : HUD réseau affiché en coin supérieur droit en jeu ───
		static void DrawNetworkHUD()
		{
			int sw = Raylib.GetScreenWidth();
			var players = NetworkManager.GetConnectedPlayersInfo();
			int lineH = 24;
			int extraLines = 0;
			bool showInternetInfo = NetworkManager.IsHost; // on garde pour les infos IP
			if (showInternetInfo) { extraLines = 1; if (NetworkManager.UpnpInProgress || !NetworkManager.UpnpMappingActive) extraLines++; }
			int panelH = 28 + players.Count * lineH + extraLines * lineH + (showInternetInfo ? 6 : 0);
			int panelW = 320;
			int px = sw - panelW - 10;
			int py = 10;

			Raylib.DrawRectangleRounded(new Rectangle(px, py, panelW, panelH), 0.15f, 6, new Color(10, 10, 20, 180));
			Raylib.DrawRectangleRoundedLines(new Rectangle(px, py, panelW, panelH), 0.15f, 6, 1,
				NetworkManager.AmIHost ? new Color(100, 220, 150, 200) : new Color(100, 160, 255, 200));

			//  Utilisation de AmIHost au lieu de IsHost
			string role = NetworkManager.AmIHost ? "Hôte" : "Client";
			FontManager.DrawText(role, px + 8, py + 5, 13, NetworkManager.AmIHost ? new Color(120, 255, 160, 255) : new Color(140, 200, 255, 255));

			int cursorY = py + 22;

			foreach (var (id, name, pvp) in players)
			{
				bool isSelf = (id == NetworkManager.LocalId);
				bool isHostPlayer = (id == 0); //  l'hôte a toujours l'ID 0

				Rectangle entryRect = new Rectangle(px + 4, cursorY, panelW - 8, lineH);
				bool hovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), entryRect);

				Texture2D statusIcon = isHostPlayer ? _serverOwnerIcon : _serverClientIcon;
				if (statusIcon.Id != 0)
				{
					float iconSize = 16;
					Vector2 pos = new Vector2(entryRect.X + 4, entryRect.Y + (lineH - iconSize) / 2);
					Raylib.DrawTextureEx(statusIcon, pos, 0, iconSize / statusIcon.Width, Color.White);
				}
				else
				{
					string bullet = isHostPlayer ? "" : "•";
					FontManager.DrawText(bullet, (int)entryRect.X + 4, cursorY, 13, isHostPlayer ? new Color(255, 215, 0, 255) : Color.White);
				}

				string displayName = isSelf ? name + " (vous)" : name;
				float nameX = entryRect.X + 26;
				FontManager.DrawText(displayName, (int)nameX, cursorY, 13, isSelf ? new Color(255, 230, 120, 255) : Color.White);

				//  PVP : icône à côté du pseudo (activable seulement pour soi-même,
				// visible en lecture seule pour les autres joueurs de la liste).
				{
					int nameWidth = FontManager.MeasureText(displayName, 13);
					float pvpIconSize = 16f;
					float pvpX = nameX + nameWidth + 6f;
					Texture2D pvpIcon = pvp ? _serverPvpOnIcon : _serverPvpOffIcon;
					Rectangle pvpRect = new Rectangle(pvpX, entryRect.Y + (lineH - pvpIconSize) / 2, pvpIconSize, pvpIconSize);
					bool pvpHover = isSelf && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), pvpRect);

					if (pvpHover)
						Raylib.DrawRectangleRounded(pvpRect, 0.2f, 6, new Color(255, 255, 255, 40));

					if (pvpIcon.Id != 0)
					{
						Color pvpTint = pvp ? new Color(255, 90, 90, 235) : new Color(200, 200, 200, isSelf ? 210 : 150);
						Raylib.DrawTextureEx(pvpIcon, new Vector2(pvpRect.X, pvpRect.Y), 0, pvpIconSize / pvpIcon.Width, pvpTint);
					}
					else
					{
						// Repli si les textures sont manquantes : pastille colorée + texte.
						Raylib.DrawCircle((int)(pvpRect.X + pvpIconSize / 2), (int)(pvpRect.Y + pvpIconSize / 2), pvpIconSize / 2,
							pvp ? new Color(220, 60, 60, 220) : new Color(90, 90, 90, 180));
						FontManager.DrawText("P", (int)(pvpRect.X + 4), (int)(pvpRect.Y + 1), 11, Color.White);
					}

					if (isSelf && pvpHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						Program.ToggleLocalPvp();
					}
				}

// Icônes d'action (téléportation et kick) – uniquement pour l'hôte
			if (NetworkManager.AmIHost && !isSelf)
			{
				float iconSize = 18f;
				float spacing = 4f;

				// Position de base : juste à droite du nom
				float baseX = entryRect.X + entryRect.Width - iconSize - 4f;

				// Icône de kick (à gauche)
				if (_serverKickIcon.Id != 0)
				{
					float kickX = baseX - (iconSize + spacing);
					Rectangle kickRect = new Rectangle(kickX, entryRect.Y + (lineH - iconSize) / 2, iconSize, iconSize);
					bool kickHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), kickRect);

					if (kickHover)
						Raylib.DrawRectangleRounded(kickRect, 0.2f, 6, new Color(255, 80, 80, 40));

					Raylib.DrawTextureEx(_serverKickIcon, new Vector2(kickRect.X, kickRect.Y), 0, iconSize / _serverKickIcon.Width,
						kickHover ? Color.White : new Color(200, 200, 200, 180));

					if (kickHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						NetworkManager.KickPlayer(id);
						Program.AddNotification(new Notification($"Joueur {name} expulsé.", new Color(255, 100, 100, 255), 2f));
					}
				}

				// Icône de téléportation (à droite)
				if (_serverTeleportIcon.Id != 0)
				{
					float teleX = baseX;
					Rectangle teleRect = new Rectangle(teleX, entryRect.Y + (lineH - iconSize) / 2, iconSize, iconSize);
					bool teleHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), teleRect);

					if (teleHover)
						Raylib.DrawRectangleRounded(teleRect, 0.2f, 6, new Color(255, 255, 255, 40));

					Raylib.DrawTextureEx(_serverTeleportIcon, new Vector2(teleRect.X, teleRect.Y), 0, iconSize / _serverTeleportIcon.Width,
						teleHover ? Color.White : new Color(200, 200, 200, 180));

					if (teleHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
					{
						Vector2 hostPos = Program.GetPlayerPosition();
						NetworkManager.SendTeleport(id, hostPos);
						Program.AddNotification(new Notification($"Téléportation de {name} vers l'hôte", new Color(100, 255, 200, 255), 2f));
					}
					}
				}

				cursorY += lineH;
			}

			// IP publique + statut UPnP (uniquement côté hôte)
			if (showInternetInfo)
			{
				cursorY += 4;
				if (NetworkManager.UpnpInProgress)
				{
					FontManager.DrawText(Localization.Get("network.searching_public_ip"), px + 10, cursorY, 12, new Color(200, 190, 160, 220));
					cursorY += lineH;
				}
				else if (!string.IsNullOrEmpty(NetworkManager.PublicIp))
				{
					FontManager.DrawText(string.Format(Localization.Get("network.public_ip"), NetworkManager.PublicIp), px + 10, cursorY, 12, new Color(180, 255, 200, 230));
					cursorY += lineH;
					if (!NetworkManager.UpnpMappingActive)
					{
						FontManager.DrawText(Localization.Get("network.port_not_forwarded"), px + 10, cursorY, 11, new Color(255, 190, 120, 220));
						cursorY += lineH;
					}
				}
				else
				{
					FontManager.DrawText(Localization.Get("network.public_ip_unavailable"), px + 10, cursorY, 12, new Color(230, 160, 160, 220));
					cursorY += lineH;
				}
			}
		}

		//  MULTIJOUEUR EN LIGNE : affichage du sous-menu Héberger / Rejoindre ──────
		static void DrawMultiplayerMenu()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			Vector2 mousePos = Raylib.GetMousePosition();

			int panelW = 560;
			//  Le panneau s'agrandit pour l'écran "Rejoindre" s'il y a des serveurs récents à afficher,
			// et pour l'écran de choix du personnage selon le nombre de personnages déjà créés.
			int savedServersShown = _isJoinSetup ? Math.Min(SavedServers.Entries.Count, 5) : 0;
			int characterRowsShown = _isCharacterSelect ? _joinCharacterOptions.Count + 1 : 0; // +1 = "Nouveau personnage"
			int panelH = _isCharacterSelect
				? 200 + characterRowsShown * 58
				: 420 + (savedServersShown > 0 ? 40 + savedServersShown * 34 : 0);
			int px = sw / 2 - panelW / 2, py = sh / 2 - panelH / 2;
			Rectangle panel = new Rectangle(px, py, panelW, panelH);
			Raylib.DrawRectangleRounded(panel, 0.06f, 8, new Color(18, 18, 26, 235));
			Raylib.DrawRectangleRoundedLines(panel, 0.06f, 8, 2, new Color(200, 170, 100, 200));

			if (_isWorldLookupPending)
			{
				FontManager.DrawText("Recherche du monde...", px + 40, py + 40, 24, new Color(230, 200, 100, 255));
				FontManager.DrawText("Récupération de l'identité du monde hébergé.", px + 40, py + 100, 16, Color.White);
				FontManager.DrawText(Localization.Get("multiplayer.cancel_hint"), px + 40, py + panelH - 40, 12, new Color(150, 140, 120, 200));
				return;
			}

			if (_mpWaitingForWorld)
			{
				FontManager.DrawText(Localization.Get("multiplayer.connecting"), px + 40, py + 40, 26, new Color(230, 200, 100, 255));
				FontManager.DrawText(_mpStatusMessage, px + 40, py + 100, 16, Color.White);
				FontManager.DrawText(Localization.Get("multiplayer.cancel_hint"), px + 40, py + panelH - 40, 12, new Color(150, 140, 120, 200));
				return;
			}

			if (_isHostSetup)
			{
				FontManager.DrawText(Localization.Get("multiplayer.host_game_title"), px + 40, py + 30, 24, new Color(230, 200, 100, 255));
				DrawMpTextField(Localization.Get("multiplayer.player_name"), _mpPseudoInput, px + 40, py + 90, panelW - 80, 0);
				DrawMpTextField(Localization.Get("multiplayer.port_default"), _mpPortInput, px + 40, py + 170, panelW - 80, 1);
				if (_isHostingFromPause)
				{
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_current_game"), px + 40, py + 230, 13, new Color(160, 150, 130, 200));
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_upnp"), px + 40, py + 248, 13, new Color(160, 150, 130, 200));
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_network"), px + 40, py + 266, 13, new Color(160, 150, 130, 200));
				}
				else
				{
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_upnp_friends"), px + 40, py + 230, 13, new Color(160, 150, 130, 200));
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_outside_network"), px + 40, py + 248, 13, new Color(160, 150, 130, 200));
					FontManager.DrawText(Localization.Get("multiplayer.hosting.info_manual_port"), px + 40, py + 266, 13, new Color(160, 150, 130, 200));
				}

				_mpConfirmButtonRect = new Rectangle(px + 40, py + panelH - 80, panelW - 80, 50);
				bool hov = Raylib.CheckCollisionPointRec(mousePos, _mpConfirmButtonRect);
				Raylib.DrawRectangleRounded(_mpConfirmButtonRect, 0.2f, 8, hov ? new Color(80, 70, 40, 230) : new Color(50, 45, 30, 200));
				Raylib.DrawRectangleRoundedLines(_mpConfirmButtonRect, 0.2f, 8, 2, new Color(200, 170, 100, 220));
				string confirmLabel = _isHostingFromPause ? Localization.Get("multiplayer.host_button") : Localization.Get("multiplayer.select_save_button");
				FontManager.DrawText(confirmLabel, px + 70, py + panelH - 65, 18, Color.White);
				FontManager.DrawText(Localization.Get("multiplayer.return_hint"), px + 40, py + panelH - 22, 12, new Color(150, 140, 120, 200));
				return;
			}

			if (_isJoinSetup)
			{
				FontManager.DrawText(Localization.Get("multiplayer.join_game_title"), px + 40, py + 30, 24, new Color(230, 200, 100, 255));
				DrawMpTextField(Localization.Get("multiplayer.player_name"), _mpPseudoInput, px + 40, py + 90, panelW - 80, 0);
				DrawMpTextField(Localization.Get("multiplayer.host_ip"), _mpHostIpInput, px + 40, py + 170, panelW - 80, 1);
				DrawMpTextField(Localization.Get("multiplayer.port"), _mpPortInput, px + 40, py + 250, panelW - 80, 2);

				//  Liste des serveurs déjà rejoints (Data/servers.json) : cliquer une ligne
				// remplit IP/port automatiquement, la croix la retire de la liste.
				if (savedServersShown > 0)
				{
					FontManager.DrawText("Serveurs récents :", px + 40, py + 316, 13, new Color(160, 150, 130, 220));
					int rowY = py + 336;
					for (int i = 0; i < savedServersShown; i++)
					{
						var entry = SavedServers.Entries[i];
						Rectangle rowRect = new Rectangle(px + 40, rowY, panelW - 80 - 36, 28);
						Rectangle delRect = new Rectangle(px + 40 + panelW - 80 - 30, rowY, 28, 28);
						bool rowHover = Raylib.CheckCollisionPointRec(mousePos, rowRect);
						Raylib.DrawRectangleRounded(rowRect, 0.2f, 6, rowHover ? new Color(60, 50, 35, 220) : new Color(28, 28, 38, 190));
						Raylib.DrawRectangleRoundedLines(rowRect, 0.2f, 6, 1, new Color(90, 80, 65, 160));
						string label = string.IsNullOrEmpty(entry.Name) ? $"{entry.Ip}:{entry.Port}" : $"{entry.Name}  ({entry.Ip}:{entry.Port})";
						FontManager.DrawText(label, (int)rowRect.X + 10, (int)rowRect.Y + 6, 14, Color.White);
						if (rowHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
						{
							_mpHostIpInput = entry.Ip;
							_mpPortInput = entry.Port.ToString();
						}

						bool delHover = Raylib.CheckCollisionPointRec(mousePos, delRect);
						Raylib.DrawRectangleRounded(delRect, 0.3f, 6, delHover ? new Color(140, 40, 40, 220) : new Color(60, 30, 30, 180));
						FontManager.DrawText("x", (int)delRect.X + 9, (int)delRect.Y + 5, 16, Color.White);
						if (delHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
						{
							SavedServers.Remove(entry);
						}
						rowY += 34;
					}
				}

				_mpConfirmButtonRect = new Rectangle(px + 40, py + panelH - 80, panelW - 80, 50);
				bool hov = Raylib.CheckCollisionPointRec(mousePos, _mpConfirmButtonRect);
				Raylib.DrawRectangleRounded(_mpConfirmButtonRect, 0.2f, 8, hov ? new Color(80, 70, 40, 230) : new Color(50, 45, 30, 200));
				Raylib.DrawRectangleRoundedLines(_mpConfirmButtonRect, 0.2f, 8, 2, new Color(200, 170, 100, 220));
				FontManager.DrawText(Localization.Get("multiplayer.connect_button"), px + 70, py + panelH - 65, 18, Color.White);
				if (!string.IsNullOrEmpty(_mpStatusMessage))
					FontManager.DrawText(_mpStatusMessage, px + 40, py + panelH - 110, 14, new Color(255, 140, 120, 230));
				FontManager.DrawText(Localization.Get("multiplayer.return_hint"), px + 40, py + panelH - 22, 12, new Color(150, 140, 120, 200));
				return;
			}

			if (_isCharacterSelect)
			{
				FontManager.DrawText("Choisir un personnage", px + 40, py + 30, 24, new Color(230, 200, 100, 255));
				FontManager.DrawText($"Sur {_pendingJoinIp}:{_pendingJoinPort}", px + 40, py + 62, 13, new Color(160, 150, 130, 200));

				_characterSelectRowRects.Clear();
				int rowY2 = py + 100;
				const int rowH = 50;

				//  Personnages déjà créés sur cet hôte (le premier, s'il correspond à l'identité
				// par défaut de l'installation, est affiché "Personnage principal").
				for (int i = 0; i < _joinCharacterOptions.Count; i++)
				{
					var profile = _joinCharacterOptions[i];
					Rectangle rowRect = new Rectangle(px + 40, rowY2, panelW - 80, rowH - 8);
					_characterSelectRowRects.Add(rowRect);
					bool rowHover = Raylib.CheckCollisionPointRec(mousePos, rowRect);
					Raylib.DrawRectangleRounded(rowRect, 0.15f, 6, rowHover ? new Color(70, 60, 40, 230) : new Color(32, 32, 42, 200));
					Raylib.DrawRectangleRoundedLines(rowRect, 0.15f, 6, 1.5f, new Color((byte)200, (byte)170, (byte)100, rowHover ? (byte)220 : (byte)140));
					string label = profile.Guid == DefaultLocalPlayerGuidReadOnly ? $"{profile.Label} (par défaut)" : profile.Label;
					FontManager.DrawText(label, (int)rowRect.X + 16, (int)rowRect.Y + 14, 16, Color.White);
					rowY2 += rowH;
				}

				// "+ Nouveau personnage" — toujours en dernière position.
				{
					Rectangle newRowRect = new Rectangle(px + 40, rowY2, panelW - 80, rowH - 8);
					_characterSelectRowRects.Add(newRowRect);
					bool rowHover = Raylib.CheckCollisionPointRec(mousePos, newRowRect);
					Raylib.DrawRectangleRounded(newRowRect, 0.15f, 6, rowHover ? new Color(60, 80, 60, 230) : new Color(28, 38, 30, 200));
					Raylib.DrawRectangleRoundedLines(newRowRect, 0.15f, 6, 1.5f, new Color((byte)140, (byte)220, (byte)140, rowHover ? (byte)230 : (byte)150));
					FontManager.DrawText("+ Nouveau personnage", (int)newRowRect.X + 16, (int)newRowRect.Y + 14, 16, new Color(200, 240, 200, 255));
				}

				FontManager.DrawText("Échap : retour", px + 40, py + panelH - 22, 12, new Color(150, 140, 120, 200));
				return;
			}

			// Menu Héberger / Rejoindre / Retour
			FontManager.DrawText(Localization.Get("multiplayer.title"), px + 40, py + 30, 28, new Color(230, 200, 100, 255));
			string[] options = { Localization.Get("multiplayer.host_game"), Localization.Get("multiplayer.join_game"), Localization.Get("button.return") };
			_hoveredMpSubOption = -1;
			for (int i = 0; i < options.Length; i++)
			{
				Rectangle r = new Rectangle(px + 40, py + 110 + i * 70, panelW - 80, 55);
				bool hov = Raylib.CheckCollisionPointRec(mousePos, r);
				if (hov) _hoveredMpSubOption = i;
				bool sel = i == _multiplayerSubOption;
				Raylib.DrawRectangleRounded(r, 0.2f, 8, sel || hov ? new Color(60, 50, 35, 220) : new Color(28, 28, 38, 190));
				Raylib.DrawRectangleRoundedLines(r, 0.2f, 8, 2, sel || hov ? new Color(200, 170, 100, 255) : new Color(90, 80, 65, 160));
				FontManager.DrawText(options[i], (int)r.X + 25, (int)r.Y + 16, 20, sel || hov ? new Color(255, 220, 100, 255) : Color.White);
			}
			FontManager.DrawText(Localization.Get("multiplayer.return_to_menu_hint"), px + 40, py + panelH - 30, 12, new Color(150, 140, 120, 200));
		}

    }
}
