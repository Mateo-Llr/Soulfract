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
		private static void DrawUIBar()
		{
			if (_gameState != GameState.Playing) return;

			var settings = SettingsManager.Settings;
			var uiItems = new List<(string id, bool enabled, Func<bool> isOpen, Action toggle)>
			{
				("inventory", settings.ShowInventoryButton, () => InventoryRenderer.IsInventoryOpen, () =>
				{
					if (InventoryRenderer.IsInventoryOpen) UIManager.PopUI();
					else InventoryRenderer.Open(0);
				}),
				("craft", settings.ShowCraftButton, () => CraftingUI.IsOpen, () =>
				{
					if (CraftingUI.IsOpen) UIManager.PopUI();
					else CraftingUI.Open("", 0);
				}),
				("guild", settings.ShowGuildButton && (Program.PlayerGuild != null || GuildUI.IsOpen), () => GuildUI.IsOpen, () =>
				{
					if (GuildUI.IsOpen)
						GuildUI.Close();
					else if (Program.PlayerGuild != null)
						GuildUI.Open();
				}),
				("quest", settings.ShowQuestButton, () => QuestJournalUI.IsOpen, () => QuestJournalUI.Toggle()),
				("map", settings.ShowMapButton, () => _gameState == GameState.Map, () =>
				{
					if (_gameState == GameState.Map) _gameState = GameState.Playing;
					else _gameState = GameState.Map;
				}),
				("bestiary", settings.ShowBestiaryButton, () => BestiaryUI.IsOpen, () =>
				{
					if (BestiaryUI.IsOpen)
						UIManager.PopUI();
					else
						BestiaryUI.Open();
				}),
			};

			// Ajout du bouton Items (Godmode uniquement)
			if (Program.IsGodMode && settings.ShowItemsButton)
    		{
				uiItems.Add(("items", true, () => ItemMenuUI.IsOpen, () =>
				{
					if (ItemMenuUI.IsOpen)
						UIManager.PopUI();
					else
						ItemMenuUI.Open();
				}));
			}

			Item? heldItem = equipment.MainHand;
			bool hasMagicWand = heldItem != null
				&& GetItemId(heldItem.Name) == GameData.GetItemId("magic_wand");
			if (hasMagicWand || SpellCraftingUI.IsOpen)
			{
				uiItems.Add(("magic", true, () => SpellCraftingUI.IsOpen, () =>
				{
					if (SpellCraftingUI.IsOpen)
						UIManager.PopUI();
					else
						SpellCraftingUI.Open();
				}));
			}

			var activeButtons = uiItems.Where(x => x.enabled).ToList();
			if (activeButtons.Count == 0) return;

			int startX = UIManager.ScaleInt(12);
			int spacing = Math.Clamp(ScreenWidth / 160, 2, UIManager.ScaleInt(6));
			int availableWidth = Math.Max(activeButtons.Count * UIManager.ScaleInt(24),
				ScreenWidth - startX * 2 - spacing * (activeButtons.Count - 1));
			int buttonSize = Math.Clamp(availableWidth / activeButtons.Count,
				UIManager.ScaleInt(24), UIManager.ScaleInt(48));
			int startY = ScreenHeight - buttonSize - UIManager.ScaleInt(12);

			// ==== CONNEXIONS (en arrière‑plan) ====
			if (_uiBarConnectionTex.Id != 0 && activeButtons.Count > 1)
			{
				int inset = buttonSize / 8;
				for (int i = 0; i < activeButtons.Count - 1; i++)
				{
					int x1 = startX + i * (buttonSize + spacing) + buttonSize - inset;
					int x2 = startX + (i + 1) * (buttonSize + spacing) + inset;
					int connWidth = x2 - x1;
					if (connWidth > 0)
					{
						float connHeight = buttonSize * 0.6f;
						float connY = startY + (buttonSize - connHeight) / 2;
						Rectangle srcRect = new Rectangle(0, 0, _uiBarConnectionTex.Width, _uiBarConnectionTex.Height);
						Rectangle destRect = new Rectangle(x1, connY, connWidth, connHeight);
						Raylib.DrawTexturePro(_uiBarConnectionTex, srcRect, destRect, Vector2.Zero, 0, Color.White);
					}
				}
			}

			// ==== BOUTONS ====
			for (int i = 0; i < activeButtons.Count; i++)
			{
				var (id, _, isOpen, toggle) = activeButtons[i];
				int x = startX + i * (buttonSize + spacing);
				int y = startY;

				Rectangle destRect = new Rectangle(x, y, buttonSize, buttonSize);

				// 1. Slot de fond
				Texture2D slotTex = _uiBarSlotTex;
				if (slotTex.Id == 0)
				{
					Raylib.DrawRectangle(x, y, buttonSize, buttonSize, new Color(40, 40, 50, 200));
				}

				if (slotTex.Id != 0)
				{
					float scale = Math.Min(destRect.Width / slotTex.Width, destRect.Height / slotTex.Height);
					float drawW = slotTex.Width * scale;
					float drawH = slotTex.Height * scale;
					float drawX = destRect.X + (destRect.Width - drawW) / 2;
					float drawY = destRect.Y + (destRect.Height - drawH) / 2;
					Raylib.DrawTexturePro(slotTex,
						new Rectangle(0, 0, slotTex.Width, slotTex.Height),
						new Rectangle(drawX, drawY, drawW, drawH),
						Vector2.Zero, 0, Color.White);
				}

				// 2. Icône
				Texture2D iconTex = GetUIBarButtonTexture(id);
				Rectangle iconRect = destRect;
				if (iconTex.Id != 0)
				{
					float scale = Math.Min(destRect.Width / iconTex.Width, destRect.Height / iconTex.Height);
					float drawW = iconTex.Width * scale;
					float drawH = iconTex.Height * scale;
					float drawX = destRect.X + (destRect.Width - drawW) / 2;
					float drawY = destRect.Y + (destRect.Height - drawH) / 2;
					iconRect = new Rectangle(drawX, drawY, drawW, drawH);
				}

				// 3. Bordure de sélection autour des pixels opaques de l’icône active
				if (isOpen())
				{
					Texture2D outlineTex = GetUIBarButtonOutlineTexture(id, iconTex);
					if (outlineTex.Id != 0)
					{
						float outlineOffsetX = iconRect.Width / iconTex.Width;
						float outlineOffsetY = iconRect.Height / iconTex.Height;
						for (int offsetY = -1; offsetY <= 1; offsetY++)
						{
							for (int offsetX = -1; offsetX <= 1; offsetX++)
							{
								if (offsetX == 0 && offsetY == 0) continue;
								Rectangle outlineRect = new Rectangle(
									iconRect.X + offsetX * outlineOffsetX,
									iconRect.Y + offsetY * outlineOffsetY,
									iconRect.Width,
									iconRect.Height);
								Raylib.DrawTexturePro(outlineTex,
									new Rectangle(0, 0, outlineTex.Width, outlineTex.Height),
									outlineRect, Vector2.Zero, 0, Color.White);
							}
						}
					}
				}

				if (iconTex.Id != 0)
				{
					Rectangle src = new Rectangle(0, 0, iconTex.Width, iconTex.Height);
					Raylib.DrawTexturePro(iconTex, src, iconRect, Vector2.Zero, 0, Color.White);
				}

				// Détection de clic
				Rectangle btnRect = new Rectangle(x, y, buttonSize, buttonSize);
				Vector2 mousePos = Raylib.GetMousePosition();
				if (Raylib.CheckCollisionPointRec(mousePos, btnRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
				{
					toggle();
				}
			}
		}

		private static void LoadCharacterTextures(List<string> missingTextures, LoadStats stats)
		{
			// ========== CHEVEUX ==========
			
			string hairsDir = "assets/animals/Human/hairs/";
			hairBaseTextures.Clear();
			hairBackTextures.Clear();
			hairOverlayTextures.Clear();
			NoneStyleIcon = File.Exists("assets/gui/icon_none.png")
				? Raylib.LoadTexture("assets/gui/icon_none.png")
				: new Texture2D();

			hairBaseTextures.Add(new Texture2D());
			hairBackTextures.Add(new Texture2D());
			hairOverlayTextures.Add(new Texture2D());
			
			int loaded = 0, missing = 0;
			
			if (Directory.Exists(hairsDir))
			{
				var hairFiles = Directory.GetFiles(hairsDir, "Humanhair*.png")
					.Where(f => !f.Contains("R") && !Path.GetFileNameWithoutExtension(f).EndsWith("_back", StringComparison.OrdinalIgnoreCase))
					.Select(Path.GetFileNameWithoutExtension)
					.Distinct()
					.OrderBy(f => 
					{
						string numStr = f.Replace("Humanhair", "");
						if (int.TryParse(numStr, out int num))
							return num;
						return int.MaxValue;
					})
					.ToList();
				
				foreach (var baseName in hairFiles)
				{
					string path = $"{hairsDir}{baseName}.png";
					var tex = TryLoad(path);
					hairBaseTextures.Add(tex);
					string backPath = $"{hairsDir}{baseName}_back.png";
					hairBackTextures.Add(File.Exists(backPath) ? Raylib.LoadTexture(backPath) : new Texture2D());
					hairOverlayTextures.Add(new Texture2D());
					
					if (tex.Id == 0)
					{
						missing++;
						missingTextures.Add(path);
					}
					else
						loaded++;
				}
			}
			else
			{
				for (int i = 1; i <= 12; i++)
				{
					var tex = TryLoad($"{hairsDir}Humanhair{i}.png");
					hairBaseTextures.Add(tex);
					string backPath = $"{hairsDir}Humanhair{i}_back.png";
					hairBackTextures.Add(File.Exists(backPath) ? Raylib.LoadTexture(backPath) : new Texture2D());
					hairOverlayTextures.Add(new Texture2D());
					if (tex.Id != 0) loaded++;
					else missing++;
				}
			}
			
			if (hairBaseTextures.Count == 0)
			{
				hairBaseTextures.Add(missingTexture);
				hairBackTextures.Add(new Texture2D());
				hairOverlayTextures.Add(new Texture2D());
			}
			
			// ========== BARBES ==========
			beardBaseTextures.Clear();
			beardOverlayTextures.Clear();
			beardBaseTextures.Add(new Texture2D());
			beardOverlayTextures.Add(new Texture2D());
			loaded = 0;
			missing = 0;
			
			if (Directory.Exists(hairsDir))
			{
				var beardFiles = Directory.GetFiles(hairsDir, "Humanbeard*.png")
					.Where(f => !f.Contains("R"))
					.Select(Path.GetFileNameWithoutExtension)
					.Distinct()
					.OrderBy(f =>
					{
						string numStr = f.Replace("Humanbeard", "");
						if (int.TryParse(numStr, out int num))
							return num;
						return int.MaxValue;
					})
					.ToList();
				
				foreach (var baseName in beardFiles)
				{
					string path = $"{hairsDir}{baseName}.png";
					var tex = TryLoad(path);
					beardBaseTextures.Add(tex);
					beardOverlayTextures.Add(new Texture2D());
					
					if (tex.Id == 0)
					{
						missing++;
						missingTextures.Add(path);
					}
					else
						loaded++;
				}
			}
			
			if (beardBaseTextures.Count == 0)
			{
				beardBaseTextures.Add(new Texture2D());
				beardOverlayTextures.Add(new Texture2D());
			}
			
			Console.WriteLine();
			stats.Success += loaded;
			stats.Warnings += missing;
			
			// ========== YEUX ==========
			
			EyeWhiteTextures.Clear();
			PupilLeftTextures.Clear();
			PupilRightTextures.Clear();
			EyebrowLeftTextures.Clear();
			EyebrowRightTextures.Clear();
			EyeClosedTextures.Clear();
			EyeBaseTextures.Clear();
			EyeOverlayTextures.Clear();
			
			string eyesDir = "assets/animals/Human/";
			loaded = 0;
			missing = 0;
			
			if (Directory.Exists(eyesDir))
			{
				var eyeFiles = Directory.GetFiles(eyesDir, "Humaneyes*.png")
					.Select(Path.GetFileNameWithoutExtension)
					.Distinct()
					.OrderBy(f =>
					{
						string numStr = f.Replace("Humaneyes", "");
						if (int.TryParse(numStr, out int num))
							return num;
						return int.MaxValue;
					})
					.ToList();
				
				foreach (var baseName in eyeFiles)
				{
					string styleSuffix = baseName.Replace("Humaneyes", "");

					string whitePath = $"{eyesDir}Humaneyes{styleSuffix}.png";
					string lpupilPath = $"{eyesDir}Humanlpupil{styleSuffix}.png";
					string rpupilPath = $"{eyesDir}Humanrpupil{styleSuffix}.png";
					string leyebrowPath = $"{eyesDir}Humanleyebrow{styleSuffix}.png";
					string reyebrowPath = $"{eyesDir}Humanreyebrow{styleSuffix}.png";
					string closedPath = $"{eyesDir}Humaneyes{styleSuffix}closed.png";

					var whiteTex = TryLoad(whitePath);
					var lpupilTex = TryLoad(lpupilPath);
					var rpupilTex = TryLoad(rpupilPath);
					var leyebrowTex = TryLoad(leyebrowPath);
					var reyebrowTex = TryLoad(reyebrowPath);
					var closedTex = TryLoad(closedPath);

					EyeWhiteTextures.Add(whiteTex);
					PupilLeftTextures.Add(lpupilTex);
					PupilRightTextures.Add(rpupilTex);
					EyebrowLeftTextures.Add(leyebrowTex);
					EyebrowRightTextures.Add(reyebrowTex);
					EyeClosedTextures.Add(closedTex);

					bool allOk = whiteTex.Id != 0 && lpupilTex.Id != 0 && rpupilTex.Id != 0 && leyebrowTex.Id != 0 && reyebrowTex.Id != 0;
					if (allOk)
						loaded++;
					else
					{
						missing++;
						if (whiteTex.Id == 0) missingTextures.Add(whitePath);
						if (lpupilTex.Id == 0) missingTextures.Add(lpupilPath);
						if (rpupilTex.Id == 0) missingTextures.Add(rpupilPath);
						if (leyebrowTex.Id == 0) missingTextures.Add(leyebrowPath);
						if (reyebrowTex.Id == 0) missingTextures.Add(reyebrowPath);
					}
				}
			}
			
			if (EyeWhiteTextures.Count == 0)
			{
				EyeWhiteTextures.Add(missingTexture);
				PupilLeftTextures.Add(missingTexture);
				PupilRightTextures.Add(missingTexture);
				EyebrowLeftTextures.Add(missingTexture);
				EyebrowRightTextures.Add(missingTexture);
			}
			stats.Success += loaded;
			stats.Warnings += missing;
			
			// ========== BOUCHE ==========
			MouthTexture = TryLoad("assets/animals/Human/Humanmouth.png");
			
			if (MouthTexture.Id == 0)
			{ 
				missingTextures.Add("assets/animals/Human/Humanmouth.png"); 
				stats.Warnings++; 
			}

			MouthOpenTexture = TryLoad("assets/animals/Human/Humanmouth_open.png");
			if (MouthOpenTexture.Id == 0)
			{
				missingTextures.Add("assets/animals/Human/Humanmouth_open.png");
				stats.Warnings++;
			}
		}

    }
}
