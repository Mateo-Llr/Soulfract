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
		static void DrawCreationText(string text, int x, int y, int fontSize, Color color = default)
		{
			Color fill = color.A == 0 ? Color.White : color;
			for (int offsetX = -1; offsetX <= 1; offsetX++)
			{
				for (int offsetY = -1; offsetY <= 1; offsetY++)
				{
					if (offsetX == 0 && offsetY == 0) continue;
					FontManager.DrawText(text, x + offsetX, y + offsetY, fontSize, Color.Black);
				}
			}
			FontManager.DrawText(text, x, y, fontSize, fill);
		}

		static void DrawCreateWorldMenu()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			Vector2 mousePos = Raylib.GetMousePosition();
			float dt = Raylib.GetFrameTime();
			float creationScale = Math.Clamp(MathF.Min(sw / 1280f, sh / 900f), 0.65f, 1f);
			int Scale(float value) => Math.Max(1, (int)MathF.Round(value * creationScale));

			// Fond défilant (repris de l'ancien menu) — le menu n'est plus dans une boîte,
			// il est directement ouvert sur le fond.
			DrawScrollingMenuBackground(sw, sh, dt);

			int centerX = sw / 2;

			// Titre (adapté selon qu'on personnalise un personnage pour rejoindre un
			// serveur, ou qu'on crée un nouveau monde en solo/hébergement)
			string screenTitle = _isJoinCharacterCreation ? Localization.Get("menu.new_character") : Localization.Get("menu.create_world_title");
			int titleFontSize = Scale(34);
			int tw = FontManager.MeasureText(screenTitle, titleFontSize);
			DrawCreationText(screenTitle, centerX - tw / 2, (int)(sh * 0.05f), titleFontSize, new Color(230, 200, 100, 255));

			// ===================================================
			// 1. APERÇU DU PERSONNAGE (centré, légèrement en haut, avec rotation)
			// ===================================================
			int previewSize = Scale(220);
			int previewX = centerX - previewSize / 2;
			int previewY = (int)(sh * 0.14f);
			Vector2 previewPos = new Vector2(previewX + previewSize / 2, previewY + previewSize / 2);

			// Mise à jour de la rotation
			if (_worldCreationRotating)
			{
				_worldCreationRot += dt * 90f; // Rotation lente
			}
			if (_worldCreationRot > 360f) _worldCreationRot -= 360f;

			// Rotation auto si la souris survole le personnage (plus de flèches cliquables)
			Rectangle previewRect = new Rectangle(previewX, previewY, previewSize, previewSize);
			_worldCreationRotating = Raylib.CheckCollisionPointRec(mousePos, previewRect);

			// Dessin du personnage avec la rotation
			float facing = MathF.Cos(_worldCreationRot * MathF.PI / 180f) >= 0 ? 1f : -1f;
			DrawCharacterPreviewWithRotation(previewPos, _tempSkinColor, _tempHairStyle, _tempBeardStyle, _tempHairColor, _tempEyeStyle, _tempEyeColor, facing);

			// ===================================================
			// 2. BARRE D'ONGLETS (creator_bar_left / mid / right + icônes de catégorie)
			// ===================================================
			//  En mode "rejoindre" on personnalise seulement le personnage : l'onglet
			// "OPTIONS" (règles du monde : armes cassables, nourriture qui pourrit...) n'a
			// pas de sens ici, ces réglages appartiennent à l'hôte.
			int tabCount = _isJoinCharacterCreation ? _creationTabs.Length - 1 : _creationTabs.Length;
			int segW = Scale(76);
			int barH = Scale(58);
			int capW = Scale(_creatorBarLeft.Id != 0 ? Math.Max(16, _creatorBarLeft.Width) : 20);
			int barY = previewY + previewSize + Scale(18);
			int midW = segW * tabCount;
			int barTotalW = capW * 2 + midW;
			int barX = centerX - barTotalW / 2;

			// Capuchon gauche
			if (_creatorBarLeft.Id != 0)
				Raylib.DrawTexturePro(_creatorBarLeft, new Rectangle(0, 0, _creatorBarLeft.Width, _creatorBarLeft.Height),
					new Rectangle(barX, barY, capW, barH), Vector2.Zero, 0, Color.White);
			else
				Raylib.DrawRectangle(barX, barY, capW, barH, new Color(40, 35, 25, 220));

			// Section centrale (étirée pour couvrir tous les onglets)
			if (_creatorBarMid.Id != 0)
				Raylib.DrawTexturePro(_creatorBarMid, new Rectangle(0, 0, _creatorBarMid.Width, _creatorBarMid.Height),
					new Rectangle(barX + capW, barY, midW, barH), Vector2.Zero, 0, Color.White);
			else
				Raylib.DrawRectangle(barX + capW, barY, midW, barH, new Color(40, 35, 25, 220));

			// Capuchon droit
			if (_creatorBarRight.Id != 0)
				Raylib.DrawTexturePro(_creatorBarRight, new Rectangle(0, 0, _creatorBarRight.Width, _creatorBarRight.Height),
					new Rectangle(barX + capW + midW, barY, capW, barH), Vector2.Zero, 0, Color.White);
			else
				Raylib.DrawRectangle(barX + capW + midW, barY, capW, barH, new Color(40, 35, 25, 220));

			Raylib.DrawRectangleLines(barX, barY, barTotalW, barH, new Color(80, 70, 50, 180));

			Texture2D[] tabIcons = { _creatorIconHair, _creatorIconBeard, _creatorIconSkin, _creatorIconEyes, _creatorIconName, new Texture2D() };

			for (int i = 0; i < tabCount; i++)
			{
				Rectangle segRect = new Rectangle(barX + capW + i * segW, barY, segW, barH);
				bool hover = Raylib.CheckCollisionPointRec(mousePos, segRect);
				bool selected = (i == _creationTab);

				// Séparateur entre segments
				if (i > 0)
					Raylib.DrawLine((int)segRect.X, barY + 4, (int)segRect.X, barY + barH - 4, new Color(80, 70, 50, 150));

				// Surbrillance du segment actif / survolé
				if (selected)
					Raylib.DrawRectangle((int)segRect.X, (int)segRect.Y, (int)segRect.Width, (int)segRect.Height, new Color(210, 180, 100, 70));
				else if (hover)
					Raylib.DrawRectangle((int)segRect.X, (int)segRect.Y, (int)segRect.Width, (int)segRect.Height, new Color(210, 180, 100, 30));
				if (selected)
					Raylib.DrawRectangleLinesEx(segRect, 2, new Color(230, 200, 100, 255));

				// Icône de la catégorie
				Texture2D icon = tabIcons[i];
				if (icon.Id != 0)
				{
					int iconSize = Scale(30);
					float scale = iconSize / (float)Math.Max(icon.Width, icon.Height);
					Vector2 iconPos = new Vector2(segRect.X + segRect.Width / 2 - (icon.Width * scale) / 2, segRect.Y + 6);
					SafeDrawTexture(icon, iconPos, 0, scale, Color.White);
				}

				// Libellé de la catégorie
				int labelFontSize = Scale(11);
				int labelW = FontManager.MeasureText(_creationTabs[i], labelFontSize);
				DrawCreationText(_creationTabs[i], (int)(segRect.X + segRect.Width / 2 - labelW / 2), (int)(segRect.Y + barH - Scale(18)), labelFontSize,
					selected ? new Color(230, 210, 150, 255) : Color.White);

				if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					_creationTab = i;
					// Synchroniser les sliders avec la couleur de la nouvelle catégorie
					if (i == 0 || i == 1) SyncSlidersFromColor(_tempHairColor);
					else if (i == 2) SyncSlidersFromColor(_tempSkinColor);
					else if (i == 3) SyncSlidersFromColor(_tempEyeColor);
				}
			}

			// Bouton "aléatoire" (random.png), juste à droite de la barre d'onglets :
			// randomise tous les éléments (styles) et toutes les couleurs du personnage.
			int randomButtonSize = Scale(44);
			Rectangle randomBtnRect = new Rectangle(barX + barTotalW + Scale(14), barY + (barH - randomButtonSize) / 2f, randomButtonSize, randomButtonSize);
			bool randomHover = Raylib.CheckCollisionPointRec(mousePos, randomBtnRect);
			Raylib.DrawRectangleRounded(randomBtnRect, 0.25f, 8, randomHover ? new Color(70, 60, 40, 220) : new Color(40, 35, 25, 180));
			Raylib.DrawRectangleRoundedLines(randomBtnRect, 0.25f, 8, 2, new Color(120, 100, 70, 200));
			if (_creatorRandomIcon.Id != 0)
			{
				float iconPad = Scale(8);
				float iconSize = randomBtnRect.Width - iconPad * 2;
				float scale = iconSize / Math.Max(_creatorRandomIcon.Width, _creatorRandomIcon.Height);
				Vector2 iconPos = new Vector2(randomBtnRect.X + iconPad, randomBtnRect.Y + iconPad);
				SafeDrawTexture(_creatorRandomIcon, iconPos, 0, scale, Color.White);
			}
			else
			{
				int qFontSize = Scale(22);
				int qW = FontManager.MeasureText("?", qFontSize);
				DrawCreationText("?", (int)(randomBtnRect.X + randomBtnRect.Width / 2 - qW / 2), (int)(randomBtnRect.Y + Scale(10)), qFontSize);
			}
			if (randomHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				RandomizeCharacterCustomization();
				SyncSlidersFromColor(_creationTab == 2 ? _tempSkinColor : (_creationTab == 3 ? _tempEyeColor : _tempHairColor));
			}

			// ===================================================
			// 3. ZONE DE SÉLECTION (styles à gauche, sélecteur RGB à droite si besoin)
			// ===================================================
			int selectionY = barY + barH + Scale(24);
			int pickerSlotSize = Math.Clamp(Scale(60), 44, 76);
			int pickerSpacing = -Math.Max(1, (int)MathF.Round(pickerSlotSize * 3f / 22f));
			int pickerCols = 5;
			int pickerWidth = pickerCols * pickerSlotSize + (pickerCols - 1) * pickerSpacing;
			int leftColumnX = Math.Max(Scale(20), centerX - pickerWidth - Scale(36));
			int rightColumnX = Math.Min(sw - Scale(260), leftColumnX + pickerWidth + Scale(48));

			switch (_creationTab)
			{
				case 0: // Cheveux
					DrawHairStylePicker(leftColumnX, selectionY, pickerSlotSize, pickerSpacing, pickerCols);
					DrawColorPicker(rightColumnX, selectionY, "Couleur des cheveux", ref _tempHairColor, creationScale);
					break;
				case 1: // Barbe
					DrawBeardStylePicker(leftColumnX, selectionY, pickerSlotSize, pickerSpacing, pickerCols);
					DrawColorPicker(rightColumnX, selectionY, "Couleur des cheveux/barbe", ref _tempHairColor, creationScale);
					break;
				case 2: // Peau
					DrawColorPicker(leftColumnX, selectionY, "Couleur de peau", ref _tempSkinColor, creationScale);
					break;
				case 3: // Yeux
					DrawEyeStylePicker(leftColumnX, selectionY, pickerSlotSize, pickerSpacing, pickerCols);
					DrawColorPicker(rightColumnX, selectionY, "Couleur des yeux", ref _tempEyeColor, creationScale);
					break;
				case 4: // Prénom
					{
						int inputW = Scale(340);
						int inputX = centerX - inputW / 2;
						int inputY = selectionY + Scale(10);
						string fieldLabel = _isJoinCharacterCreation ? "Nom du personnage" : "Prénom / nom du monde";
						DrawCreationText(fieldLabel, inputX, inputY - Scale(24), Scale(14));
						int inputH = Scale(42);
						Raylib.DrawRectangleRounded(new Rectangle(inputX, inputY, inputW, inputH), 0.1f, 8, new Color(30, 30, 40, 255));
						Raylib.DrawRectangleRoundedLines(new Rectangle(inputX, inputY, inputW, inputH), 0.1f, 8, 2, new Color(120, 100, 70, 255));
						TextInput.DrawSingleLine(_newWorldName, "world-name", new Rectangle(inputX, inputY, inputW, inputH), Scale(18),
							Color.White, new Color(90, 110, 150, 220), new Color(230, 200, 100, 255), 14);

						if (!string.IsNullOrEmpty(_creationError))
						{
							int errW = FontManager.MeasureText(_creationError, 14);
							DrawCreationText(_creationError, centerX - errW / 2, inputY + Scale(56), Scale(14), Color.Red);
						}

						// Edition du champ de texte (actif uniquement sur cet onglet)
						if (!TextInput.IsFocused("world-name"))
							TextInput.Focus("world-name", _newWorldName.Length);
						TextInput.Update(ref _newWorldName, "world-name", new Rectangle(inputX, inputY, inputW, inputH), Math.Max(12, Scale(30)));
						if (Raylib.IsKeyPressed(KeyboardKey.Enter) && (_isJoinCharacterCreation || _newWorldName.Length > 0))
						{
							if (_isJoinCharacterCreation)
							{
								ConfirmJoinCharacterCreation();
								return;
							}
							ApplyCharacterCustomization(_tempSkinColor, _tempHairStyle, _tempBeardStyle, _tempHairColor, _tempEyeStyle, _tempEyeColor);
							if (TryStartNewGame(_newWorldName))
							{
								_isInWorldCreation = false;
								_currentSaveName = _newWorldName;
								_gameState = GameState.Playing;
								BeginSoulArrivalCinematic();
								return;
							}
						}
					}
					break;
				case 5: // Options
					{
						int optionW = Scale(380);
						int optionX = centerX - optionW / 2;
						int optionY = selectionY + Scale(10);

						DrawCreationText("Options du monde", optionX, optionY, Scale(18));

						int rowH = Scale(70);
						Rectangle weaponsRect = new Rectangle(optionX, optionY + Scale(40), optionW, rowH - Scale(10));
						Rectangle foodRect = new Rectangle(optionX, optionY + Scale(40) + rowH, optionW, rowH - Scale(10));

						Texture2D weaponsIcon = _worldSettingsIconWeaponsBreak;
						Texture2D foodIcon = _worldSettingsIconFoodSpoil;

						Rectangle weaponsIconRect = new Rectangle(optionX + Scale(12), optionY + Scale(48), Scale(44), Scale(44));
						Rectangle foodIconRect = new Rectangle(optionX + Scale(12), optionY + Scale(48) + rowH, Scale(44), Scale(44));
						Rectangle weaponsToggleRect = new Rectangle(optionX + Scale(70), optionY + Scale(54), Scale(64), Scale(28));
						Rectangle foodToggleRect = new Rectangle(optionX + Scale(70), optionY + Scale(54) + rowH, Scale(64), Scale(28));
						int textX = optionX + Scale(150);

						Color optionBorder = new Color(120, 120, 130, 180);
						Color weaponsBorder = Raylib.CheckCollisionPointRec(mousePos, weaponsRect) ? new Color(180, 180, 200, 220) : optionBorder;
						Color foodBorder = Raylib.CheckCollisionPointRec(mousePos, foodRect) ? new Color(180, 180, 200, 220) : optionBorder;

						Raylib.DrawRectangleRoundedLines(weaponsRect, 0.15f, 8, 2, weaponsBorder);
						Raylib.DrawRectangleRoundedLines(foodRect, 0.15f, 8, 2, foodBorder);

						if (weaponsIcon.Id != 0)
						{
							float iconScale = Math.Min(weaponsIconRect.Width / (float)weaponsIcon.Width, weaponsIconRect.Height / (float)weaponsIcon.Height);
							SafeDrawTexture(weaponsIcon, new Vector2(weaponsIconRect.X + (weaponsIconRect.Width - weaponsIcon.Width * iconScale) / 2, weaponsIconRect.Y + (weaponsIconRect.Height - weaponsIcon.Height * iconScale) / 2), 0, iconScale, Color.White);
						}
						else
						{
							Raylib.DrawRectangleRounded(weaponsIconRect, 0.15f, 6, new Color(50, 50, 50, 200));
							Raylib.DrawRectangleRoundedLines(weaponsIconRect, 0.15f, 6, 1, new Color(120, 110, 100, 180));
						}

						if (foodIcon.Id != 0)
						{
							float iconScale = Math.Min(foodIconRect.Width / (float)foodIcon.Width, foodIconRect.Height / (float)foodIcon.Height);
							SafeDrawTexture(foodIcon, new Vector2(foodIconRect.X + (foodIconRect.Width - foodIcon.Width * iconScale) / 2, foodIconRect.Y + (foodIconRect.Height - foodIcon.Height * iconScale) / 2), 0, iconScale, Color.White);
						}
						else
						{
							Raylib.DrawRectangleRounded(foodIconRect, 0.15f, 6, new Color(50, 50, 50, 200));
							Raylib.DrawRectangleRoundedLines(foodIconRect, 0.15f, 6, 1, new Color(120, 110, 100, 180));
						}

						Texture2D weaponsToggleTexture = WorldWeaponsBreak ? _buttonToggleOn : _buttonToggleOff;
						Texture2D foodToggleTexture = WorldFoodSpoil ? _buttonToggleOn : _buttonToggleOff;
						bool weaponsToggleHover = Raylib.CheckCollisionPointRec(mousePos, weaponsToggleRect);
						bool foodToggleHover = Raylib.CheckCollisionPointRec(mousePos, foodToggleRect);
						Color weaponsToggleTint = weaponsToggleHover ? new Color(255, 255, 230, 220) : Color.White;
						Color foodToggleTint = foodToggleHover ? new Color(255, 255, 230, 220) : Color.White;
						Color weaponsToggleBg = weaponsToggleHover ? new Color(255, 255, 255, 40) : new Color(0, 0, 0, 0);
						Color foodToggleBg = foodToggleHover ? new Color(255, 255, 255, 40) : new Color(0, 0, 0, 0);

						Raylib.DrawRectangleRounded(weaponsToggleRect, 0.2f, 6, weaponsToggleBg);
						Raylib.DrawRectangleRounded(foodToggleRect, 0.2f, 6, foodToggleBg);

						if (weaponsToggleTexture.Id != 0)
						{
							float toggleScale = Math.Min(weaponsToggleRect.Width / (float)weaponsToggleTexture.Width, weaponsToggleRect.Height / (float)weaponsToggleTexture.Height);
							SafeDrawTexture(weaponsToggleTexture, new Vector2(weaponsToggleRect.X + (weaponsToggleRect.Width - weaponsToggleTexture.Width * toggleScale) / 2, weaponsToggleRect.Y + (weaponsToggleRect.Height - weaponsToggleTexture.Height * toggleScale) / 2), 0, toggleScale, weaponsToggleTint);
						}
						else
						{
							Raylib.DrawRectangleRounded(weaponsToggleRect, 0.2f, 6, WorldWeaponsBreak ? new Color(90, 170, 90, 255) : new Color(80, 80, 80, 255));
							Raylib.DrawRectangleRoundedLines(weaponsToggleRect, 0.2f, 6, 1, new Color(140, 140, 140, 200));
						}

						if (foodToggleTexture.Id != 0)
						{
							float toggleScale = Math.Min(foodToggleRect.Width / (float)foodToggleTexture.Width, foodToggleRect.Height / (float)foodToggleTexture.Height);
							SafeDrawTexture(foodToggleTexture, new Vector2(foodToggleRect.X + (foodToggleRect.Width - foodToggleTexture.Width * toggleScale) / 2, foodToggleRect.Y + (foodToggleRect.Height - foodToggleTexture.Height * toggleScale) / 2), 0, toggleScale, foodToggleTint);
						}
						else
						{
							Raylib.DrawRectangleRounded(foodToggleRect, 0.2f, 6, WorldFoodSpoil ? new Color(90, 170, 90, 255) : new Color(80, 80, 80, 255));
							Raylib.DrawRectangleRoundedLines(foodToggleRect, 0.2f, 6, 1, foodToggleHover ? new Color(220, 220, 180, 255) : new Color(140, 140, 140, 200));
						}

						DrawCreationText("Armes se cassent", textX, optionY + Scale(52), Scale(18));
						DrawCreationText("Les armes peuvent se briser pendant le combat.", textX, optionY + Scale(72), Scale(12));
						DrawCreationText("La nourriture pourrit", textX, optionY + Scale(52) + rowH, Scale(18));
						DrawCreationText("Les aliments périssables se dégradent avec le temps.", textX, optionY + Scale(72) + rowH, Scale(12));

						if (Raylib.CheckCollisionPointRec(mousePos, weaponsToggleRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
							WorldWeaponsBreak = !WorldWeaponsBreak;
						if (Raylib.CheckCollisionPointRec(mousePos, foodToggleRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
							WorldFoodSpoil = !WorldFoodSpoil;
						int seedRowY = optionY + Scale(40) + rowH * 2;
						Rectangle seedFieldRect = new Rectangle(optionX + Scale(14), seedRowY + Scale(30), optionW - Scale(28), Scale(42));
bool seedFieldHover = Raylib.CheckCollisionPointRec(mousePos, seedFieldRect);
string seedLabel = string.IsNullOrEmpty(_newWorldSeed) ? "Al?atoire" : _newWorldSeed;
DrawCreationText("Seed du monde", optionX + Scale(14), seedRowY, Scale(16));
Raylib.DrawRectangleRounded(seedFieldRect, 0.1f, 8, new Color(30, 30, 40, 255));
Raylib.DrawRectangleRoundedLines(seedFieldRect, 0.1f, 8, 2, seedFieldHover || _seedFieldFocused ? new Color(230, 200, 100, 255) : new Color(120, 100, 70, 255));
TextInput.DrawSingleLine(_newWorldSeed, "world-seed", seedFieldRect, 16, Color.White,
	new Color(90, 110, 150, 220), new Color(230, 200, 100, 255), 12, placeholder: "Aléatoire");

if (Raylib.IsMouseButtonPressed(MouseButton.Left))
{
if (seedFieldHover)
{
_seedFieldFocused = true;
}
else if (!Raylib.CheckCollisionPointRec(mousePos, weaponsToggleRect) && !Raylib.CheckCollisionPointRec(mousePos, foodToggleRect))
{
_seedFieldFocused = false;
}
}

if (_seedFieldFocused)
{
if (!TextInput.IsFocused("world-seed"))
TextInput.Focus("world-seed", _newWorldSeed.Length);
TextInput.Update(ref _newWorldSeed, "world-seed", seedFieldRect, 11, c => c == '-' || char.IsDigit(c));
}
					}
					break;
			}

			// ===================================================
			// 4. BOUTON "CRÉER ET JOUER"
			// ===================================================
			int btnW = Scale(240), btnH = Scale(52);
			int btnX = centerX - btnW / 2;
			int btnY = sh - Scale(90);
			Rectangle createBtn = new Rectangle(btnX, btnY, btnW, btnH);
			bool createHover = Raylib.CheckCollisionPointRec(mousePos, createBtn);
			//  En mode "rejoindre", le nom du personnage est optionnel (LocalCharacters lui
			// donnera un nom par défaut) : le bouton reste donc toujours activé.
			bool canCreate = _isJoinCharacterCreation || _newWorldName.Length > 0;

			Color createColor = canCreate ? (createHover ? new Color(100, 180, 100, 255) : new Color(80, 140, 80, 255)) : new Color(50, 70, 50, 255);
			Raylib.DrawRectangleRounded(createBtn, 0.25f, 8, createColor);
			Raylib.DrawRectangleRoundedLines(createBtn, 0.25f, 8, 2, canCreate ? new Color(100, 200, 100, 200) : new Color(80, 100, 80, 100));

			string btnText = canCreate ? (_isJoinCharacterCreation ? Localization.Get("menu.rejoin") : Localization.Get("menu.create_and_play")) : "ENTREZ UN PRÉNOM";
			int btnTextW = FontManager.MeasureText(btnText, 20);
			DrawCreationText(btnText, (int)(createBtn.X + btnW / 2 - btnTextW / 2), (int)(createBtn.Y + Scale(16)), Scale(20));

			if (canCreate && createHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
			{
				if (_isJoinCharacterCreation)
				{
					ConfirmJoinCharacterCreation();
				}
				else
				{
					// Appliquer les changements de personnalisation
					ApplyCharacterCustomization(_tempSkinColor, _tempHairStyle, _tempBeardStyle, _tempHairColor, _tempEyeStyle, _tempEyeColor);
					if (TryStartNewGame(_newWorldName))
					{
						_isInWorldCreation = false;
						_currentSaveName = _newWorldName;
						_gameState = GameState.Playing;
						BeginSoulArrivalCinematic();
					}
				}
			}

			// ===================================================
			// 5. TOUCHE ECHAP POUR REVENIR EN ARRIÈRE
			// ===================================================
			if (Raylib.IsKeyPressed(KeyboardKey.Escape))
			{
				if (_isJoinCharacterCreation)
				{
					// Retour à l'écran de choix du personnage plutôt qu'au menu principal :
					// IP/pseudo/port et la liste des personnages connus restent valides.
					_isJoinCharacterCreation = false;
					_creationError = "";
					_newWorldName = "";
					_isCharacterSelect = true;
					_isMultiplayerMenu = true;
				}
				else
				{
					_isInWorldCreation = false;
					_gameState = GameState.MainMenu;
					_creationError = "";
					_newWorldName = "";
					_newWorldSeed = "";
				}
			}
		}

		//  MULTIJOUEUR : appelé depuis l'écran de personnalisation (DrawCreateWorldMenu, en
		// mode _isJoinCharacterCreation) une fois que le joueur a validé l'apparence de son
		// nouveau personnage. Applique la customisation, crée et enregistre localement le
		// personnage pour cet hôte, puis lance réellement la connexion — jusqu'ici on
		// n'avait fait qu'ouvrir l'écran de personnalisation (voir le handler de clic
		// "+ Nouveau personnage" plus haut dans UpdateMultiplayerMenu).
		static void ConfirmJoinCharacterCreation()
		{
			ApplyCharacterCustomization(_tempSkinColor, _tempHairStyle, _tempBeardStyle, _tempHairColor, _tempEyeStyle, _tempEyeColor);

			string hostKey = $"{_pendingJoinIp}:{_pendingJoinPort}";
			string label = string.IsNullOrWhiteSpace(_newWorldName) ? $"Personnage {_joinCharacterOptions.Count + 1}" : _newWorldName.Trim();
			var created = LocalCharacters.CreateNew(hostKey, _pendingJoinWorldName, label);
			UseCharacterGuidForNextJoin(created.Guid);

			LocalPlayerName = string.IsNullOrWhiteSpace(_mpPseudoInput) ? "Joueur" : _mpPseudoInput;
			NetworkManager.JoinHostAsync(_pendingJoinIp, _pendingJoinPort, LocalPlayerName);
			SavedServers.AddOrUpdate(_pendingJoinIp, _pendingJoinPort);

			_isJoinCharacterCreation = false;
			_creationError = "";
			_newWorldName = "";
			_isMultiplayerMenu = true;
			_mpWaitingForWorld = true;
			_mpStatusMessage = "Connexion en cours...";
		}

		static void EnsureWorldRevealShader()
		{
			if (_worldRevealShaderLoaded) return;
			_worldRevealShaderLoaded = true;
			_worldRevealShader = Raylib.LoadShaderFromMemory(null, WorldRevealFragmentShader);
			if (_worldRevealShader.Id == 0)
			{
				Console.WriteLine("[Loading] Impossible de charger le shader de révélation du monde.");
				return;
			}
			_worldRevealProgressLocation = Raylib.GetShaderLocation(_worldRevealShader, "revealProgress");
		}

    }
}
