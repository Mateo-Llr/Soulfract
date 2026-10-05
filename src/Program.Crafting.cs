// Program.Crafting.cs
#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Soulfract.WorldGeneration;
using DiscordRPC;

namespace Soulfract
{
    public static partial class Program
    {

		static bool _craftingUIOpen = false;

		private static void HandlePrimaryPlayerGamepadInput(Camera2D camera)
		{
			if (_gameState != GameState.Playing || ChatSystem.IsOpen || InventoryRenderer.IsInventoryOpen)
				return;
			if (!TryGetPrimaryGamepadIndex(out int gamepadIndex)) return;
			if (!Raylib.IsGamepadAvailable(gamepadIndex)) return;
			
			if (IsGamepadButtonPressedForPrimaryPlayer(GamepadButton.LeftFaceLeft))
			{
				hotbarSlot = Math.Clamp(hotbarSlot - 1, 0, 9);
				UpdateMainHandFromHotbar();
				return;
			}
			if (IsGamepadButtonPressedForPrimaryPlayer(GamepadButton.LeftFaceRight))
			{
				hotbarSlot = Math.Clamp(hotbarSlot + 1, 0, 9);
				UpdateMainHandFromHotbar();
				return;
			}
			if (IsGamepadButtonPressedForPrimaryPlayer(GamepadButton.RightFaceLeft))
			{
				if (InventoryRenderer.IsInventoryOpen) UIManager.PopUI(); else InventoryRenderer.Open(0);
				return;
			}
			if (IsGamepadButtonPressedForPrimaryPlayer(GamepadButton.RightFaceRight))
			{
				if (CraftingUI.IsOpen) UIManager.PopUI(); else if (!UIManager.IsAnyUIOpen()) CraftingUI.Open("", 0);
				return;
			}
			if (IsGamepadButtonPressedForPrimaryPlayer(GamepadButton.RightFaceDown))
			{
				if (PlayerCurrentCar != null)
				{
					PlayerCurrentCar.Driver = null;
					_playerPos = PlayerCurrentCar.Position + new Vector2(50, 0);
					PlayerCurrentCar = null;
				}
				else
				{
					foreach (var car in Cars)
					{
						if (car.Driver != null) continue;
						float dist = Vector2.Distance(_playerPos, car.Position);
						if (dist < 80f)
						{
							car.Driver = GetPlayerEntity();
							PlayerCurrentCar = car;
							AddNotification(new Notification(" Vous montez dans la voiture.", new Color(100, 200, 255, 255), 2f));
							break;
						}
					}
				}
				return;
			}
			if (IsGamepadButtonPressedForPrimaryPlayer(GamepadButton.LeftFaceDown))
			{
				TryInteractWithStation(_playerPos, camera, null, 0);
				return;
			}
			if (IsGamepadButtonPressedForPrimaryPlayer(GamepadButton.LeftFaceUp))
			{
				TryPickupFurnitureOrCreature(_playerPos, camera, null, 0);
			}
		}

		private static bool TryUseRecipeItem(Item item, Vector2 playerPos, Camera2D camera)
		{
			if (item == null) return false;
			
			int itemId = GetItemId(item.Name);
			
			//  Vérifier si c'est bien l'item 212 (Recette)
			if (itemId != 212)
				return false;
			
			if (!RecipeSystem.IsRecipeItem(itemId))
				return false;
			
			var recipeData = RecipeSystem.GetRecipeData(item);
			if (recipeData == null)
				return false;
			
			//  SI LA RECETTE EST DÉJÀ CONNUE
			if (recipeData.IsDiscovered)
			{
				// Consommer l'item quand même (on le jette)
				RemoveItemFromInventory(item.Name, 1);
				
				// Afficher un message informatif
				Program.AddNotification(new Notification($"Recette déjà connue : {recipeData.DisplayName}", new Color(200, 200, 100, 255), 2f));
				
				// Petit effet visuel
				Vector2 visualPos = GetPlayerVisualPosition(playerPos);
				for (int i = 0; i < 8; i++)
				{
					float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
					float speed = Random.Shared.Next(30, 80);
					Vector2 vel = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
					Color color = new Color(200, 180, 100, 180);
					_particles.Add(new Particle(visualPos, vel, color, 3, 0.4f));
				}
				
				return true;
			}
			
			//  DÉCOUVRIR LA RECETTE (ET LA CONSOMMER)
			bool discovered = RecipeSystem.DiscoverRecipe(item);
			if (discovered)
			{
				//  CONSOMMER L'ITEM
				RemoveItemFromInventory(item.Name, 1);
				
				// Effet visuel de découverte (plus impressionnant)
				Vector2 visualPos = GetPlayerVisualPosition(playerPos);
				for (int i = 0; i < 25; i++)
				{
					float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
					float speed = Random.Shared.Next(80, 250);
					Vector2 vel = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
					Color color = new Color(200, 180, 100, 220);
					_particles.Add(new Particle(visualPos, vel, color, 4, 0.7f));
				}
				
				// Appliquer un léger screen shake
				ApplyScreenShake(4f, 0.2f);
				
				//  LA NOTIFICATION EST DÉJÀ DANS DiscoverRecipe()
				// On ajoute juste une notification supplémentaire pour le chaudron
				if (recipeData.RequiresStation == "cauldron")
				{
					Program.AddNotification(new Notification($"Recette disponible dans le chaudron !", new Color(100, 200, 255, 255), 2.5f));
				}
				
				return true;
			}
			
			return false;
		}

		// Dessine l'écran de chargement plein écran (fondu d'entrée + attente animée).
        static void DrawLoadingScreen(Vector2 playerPos, float facing, Vector2 hitboxDim, Camera2D camera,
                 int craftSelected, bool showDebug)
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            Raylib.ClearBackground(new Color(6, 10, 18, 255));

            if (_loadingPhase == LoadingPhase.FadeIn)
            {
				if (!_loadingMenuCaptured)
				{
					if (_loadingMenuTexture.Id != 0) Raylib.UnloadRenderTexture(_loadingMenuTexture);
					_loadingMenuTexture = Raylib.LoadRenderTexture(sw, sh);
					Raylib.BeginTextureMode(_loadingMenuTexture);
					Raylib.ClearBackground(new Color((byte)0, (byte)0, (byte)0, (byte)0));
					DrawClassicMenuCarousel(sw, sh, Raylib.GetMousePosition(), interactive: false, drawCharacterPreviews: true, drawDetailPreview: false);
					Raylib.EndTextureMode();
					_loadingMenuCaptured = true;
				}

				if (_menuRevealTexture.Id == 0 || _menuRevealTexture.Texture.Width != sw || _menuRevealTexture.Texture.Height != sh)
				{
					if (_menuRevealTexture.Id != 0) Raylib.UnloadRenderTexture(_menuRevealTexture);
					_menuRevealTexture = Raylib.LoadRenderTexture(sw, sh);
				}

				Raylib.BeginTextureMode(_menuRevealTexture);
				DrawClassicMenuBackground(sw, sh);
				Raylib.EndTextureMode();

				Rectangle menuSource = new Rectangle(0, 0, sw, -sh);
				Rectangle menuDestination = new Rectangle(0, 0, sw, sh);
				Raylib.DrawTexturePro(_menuRevealTexture.Texture, menuSource, menuDestination, Vector2.Zero, 0f, Color.White);

				if (_loadingMenuTexture.Id != 0 && _loadingMenuExitProgress < 1f)
				{
					float exitProgress = _loadingMenuExitProgress * _loadingMenuExitProgress * (3f - 2f * _loadingMenuExitProgress);
					Rectangle exitMenuDestination = new Rectangle(0f, -sh * 0.08f * exitProgress, sw, sh);
					byte menuAlpha = (byte)Math.Clamp((1f - exitProgress) * 255f, 0f, 255f);
					Raylib.DrawTexturePro(_loadingMenuTexture.Texture, menuSource, exitMenuDestination, Vector2.Zero, 0f,
						new Color((byte)255, (byte)255, (byte)255, menuAlpha));
				}

                if (!string.IsNullOrEmpty(_pendingLoadSaveName) && _carouselPreviewCache.TryGetValue(_pendingLoadSaveName, out var preview))
                {
					const float moveEnd = 0.58f;
					float moveProgress = Math.Clamp(_loadingCharacterProgress / moveEnd, 0f, 1f);
					float scaleProgress = Math.Clamp((_loadingCharacterProgress - moveEnd) / (1f - moveEnd), 0f, 1f);
					float moveEased = moveProgress * moveProgress * (3f - 2f * moveProgress);
					float scaleEased = scaleProgress * scaleProgress * (3f - 2f * scaleProgress);
                    Vector2 startPos = new Vector2(sw * 0.8f, sh * 0.68f);
					Camera2D transitionCamera = camera;
					transitionCamera.Offset = new Vector2(sw / 2f, sh / 2f);
					transitionCamera.Target = GetPlayerCameraTarget(_playerPos);
					Vector2 endPos = Raylib.GetWorldToScreen2D(GetPlayerCameraTarget(_playerPos), transitionCamera);
					Vector2 drawPos = Vector2.Lerp(startPos, endPos, moveEased);
					float endScale = Math.Max(0.01f, camera.Zoom);
					float startScale = endScale * 2.25f;
					float drawScale = MathHelper.Lerp(startScale, endScale, scaleEased);
                    float baseHeight = Math.Max(80f, sh * 0.18f);
					DrawClassicSavePreview(_pendingLoadSaveName, drawPos, drawScale, baseHeight, 1f, _animFrame, _animProg);

                    string label = _pendingLoadSaveName;
                    int labelSize = Math.Max(20, Math.Min(30, sh / 26));
                    int labelWidth = FontManager.MeasureText(label, labelSize);
                    int labelX = (int)(drawPos.X - labelWidth / 2f);
                    int labelY = (int)(drawPos.Y + baseHeight * 0.8f + 26f);
					foreach (var (ox, oy) in new (int, int)[] { (-1, -1), (1, -1), (-1, 1), (1, 1), (0, -1), (0, 1), (-1, 0), (1, 0) })
						FontManager.DrawText(label, labelX + ox, labelY + oy, labelSize, Color.Black);
                    FontManager.DrawText(label, labelX, labelY, labelSize, Color.White);
                }


				byte dim = (byte)Math.Clamp((1f - _loadingCharacterProgress) * 140f + 55f, 0f, 255f);
				Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)0, (byte)0, (byte)0, dim));
                return;
            }

            byte alpha = (byte)Math.Clamp(_loadingFadeAlpha * 255f, 0, 255);
            DrawLoadingOverlayContent(alpha);
        }

		// Vérifie si on a assez d'ingrédients pour craft (dans TOUS les conteneurs)
		public static bool HasEnoughIngredients(CraftRecipe recipe, int quantity)
		{
			foreach (var (id, needQty) in recipe.Ingredients)
			{
				if (!GameData.ItemDatabase.TryGetValue(id, out var itemData))
					return false;
				
				int totalHave = GetTotalItemCount(itemData.Name);
				if (totalHave < needQty * quantity)
					return false;
			}
			return true;
		}

		// Retire les ingrédients de TOUS les conteneurs pour le craft
		public static void ConsumeIngredients(CraftRecipe recipe, int quantity)
		{
            if (!IsGodMode)
            {
                foreach (var (id, needQty) in recipe.Ingredients)
                {
                    if (!GameData.ItemDatabase.TryGetValue(id, out var itemData))
                        continue;
                    
                    int totalToRemove = needQty * quantity;
                    RemoveItemFromAllInventories(itemData.Name, totalToRemove);
                }
            }
            if (GameData.ItemDatabase.TryGetValue(recipe.ResultId, out var resultItem))
                SkillSystem.AddXP(SkillType.Artisan, resultItem.Value * quantity);
        }
    }
}
