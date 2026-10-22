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
	private static void UpdateMainMenuFade(float dt)
	{
		if (_mainMenuFadePhase == MainMenuFadePhase.None) return;

		if (_mainMenuFadePhase == MainMenuFadePhase.FadeOut)
		{
			_mainMenuFadeAlpha += dt / MAIN_MENU_FADE_DURATION;
			if (_mainMenuFadeAlpha >= 1f)
			{
				_mainMenuFadeAlpha = 1f;
				PerformReturnToMainMenuCleanup();
				_gameState = GameState.MainMenu;
				_mainMenuFadePhase = MainMenuFadePhase.FadeIn;
			}
		}
		else if (_mainMenuFadePhase == MainMenuFadePhase.FadeIn)
		{
			_mainMenuFadeAlpha -= dt / MAIN_MENU_FADE_DURATION;
			if (_mainMenuFadeAlpha <= 0f)
			{
				_mainMenuFadeAlpha = 0f;
				_mainMenuFadePhase = MainMenuFadePhase.None;
			}
		}
	}

	private static void DrawMainMenuTransitionOverlay()
	{
		byte alpha = (byte)Math.Clamp(_mainMenuFadeAlpha * 255f, 0, 255);
		Raylib.DrawRectangle(0, 0, ScreenWidth, ScreenHeight, new Color((byte)0, (byte)0, (byte)0, alpha));
	}

	private static void StartReturnToMainMenuFade()
	{
		if (_mainMenuFadePhase != MainMenuFadePhase.None) return;
		_mainMenuFadePhase = MainMenuFadePhase.FadeOut;
		_mainMenuFadeAlpha = 0f;
		_pendingReturnToMainMenuCleanup = true;
	}

	private static void PerformReturnToMainMenuCleanup()
	{
		if (!_pendingReturnToMainMenuCleanup) return;
		_pendingReturnToMainMenuCleanup = false;

		if (NetworkManager.IsOnline)
			NetworkManager.Disconnect();

		SaveCurrentGameOnExit();
		if (ShouldSaveCurrentGameOnExit())
			InventoryRenderer.CloseInventory();
		World.Reset();
		//  CRITIQUE : sans ceci, World.CurrentSaveName garde le nom de la dernière
		// partie jouée. Comme le monde-vitrine du menu principal (Program_MenuPreview.cs)
		// génère et décharge des chunks comme en jeu (UpdateActiveChunks), tout chunk
		// du panorama aléatoire du menu qui se décharge se retrouvait alors SAUVEGARDÉ
		// dans le dossier de la vraie sauvegarde (World_Chunks.cs / World_Core.cs ne
		// vérifient que "CurrentSaveName non vide" avant d'écrire sur disque) : le menu
		// polluait donc silencieusement la partie du joueur avec du terrain/contenu qui
		// n'a rien à voir. Le menu principal doit être un monde totalement à part, qui
		// ne doit plus jamais avoir de nom de sauvegarde associé une fois qu'on le quitte.
		World.CurrentSaveName = "";
		_currentSaveName = "";
		entities.Clear();
		destroyedObjects.Clear();
		objectHPs.Clear();
		PerlinNoise.SetSeed(0);
		Program.ActiveStation = "";
		_isMultiplayerMenu = false;
		_isJoinSetup = false;
		_isCharacterSelect = false;
		_isHostSetup = false;
		_isHostingFromPause = false;
		// Repartir directement sur l'écran d'accueil de base : sans ça, _mainMenuActivated
		// restait à true (hérité de la partie qu'on vient de quitter) et on retombait un
		// court instant sur le carrousel des sauvegardes avant de revenir à l'accueil.
		_mainMenuActivated = false;
		_mainMenuAnim = 0f;
		RefreshAvailableSaves();
	}

	private static int ResolvePlaceableId(Item item, ItemData itemData)
	{
		if (!string.Equals(itemData.Key, "wooden_floor", StringComparison.OrdinalIgnoreCase))
			return itemData.PlaceableId;

		string floorStyle = item.GetMetadataValue("floor_style", "");

		return (string.IsNullOrWhiteSpace(floorStyle) ? "zigzag" : floorStyle).ToLowerInvariant() switch
		{
			"zigzag" => 54,
			"tile" => 55,
			"square" => 56,
			"plank" => 57,
			"flat" => 58,
			_ => itemData.PlaceableId
		};
	}

	static Vector2 GetVisualTileCenter(int tileX, int tileY)
	{
		int ts = TileSize;
		Vector2 tileCenter = new(tileX * ts + ts / 2f, tileY * ts + ts / 2f);
		return World.GetEntityGroundPosition(tileCenter);
	}

	static float GetPlacementDistanceFromPlayerFeet(Vector2 playerPos, int tileX, int tileY)
	{
		return Vector2.Distance(
			World.GetEntityGroundPosition(GetPlayerFeetPosition(playerPos)),
			GetVisualTileCenter(tileX, tileY));
	}

	static float GetPlacementDistanceFromPlayerTile(Vector2 playerPos, int tileX, int tileY)
	{
		int ts = TileSize;
		Vector2 playerFeet = GetPlayerFeetPosition(playerPos);
		Vector2 playerTileCenter = new(
			MathF.Floor(playerFeet.X / ts) * ts + ts / 2f,
			MathF.Floor(playerFeet.Y / ts) * ts + ts / 2f);
		return Vector2.Distance(
			World.GetEntityGroundPosition(playerTileCenter),
			GetVisualTileCenter(tileX, tileY));
	}

	static void DrawPlacementGhost(Camera2D camera, Item heldItem, ItemData itemData)
	{
		itemData.PlaceableId = ResolvePlaceableId(heldItem, itemData);
		int ts = TileSize;
		Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
		
		// ─────────────────────────────────────────────────────────
		// CAS DES GRAINES
		// ─────────────────────────────────────────────────────────
		if (itemData.IsSeed)
		{
				int cropId = itemData.CropId;
				if (cropId == 0) return;
				var cropTileData = WorldTileRegistry.GetTile(cropId);
				if (cropTileData == null) return;
				
				(int seedTileX, int seedTileY, float seedDist, int _) = FindTileUnderMouse(mouseWorld, ts);
				if (seedTileX == -1 || seedTileY == -1) return;
				
				int groundId = World.GetGroundTileIdAt(seedTileX, seedTileY);
				var seedGroundTile = WorldTileRegistry.GetTile(groundId);
				bool canPlantGhost;
				if (itemData.PlantType == "classic")
				{
					//  Arbre (ex : gland) : se plante n'importe où sur un sol praticable,
					// comme un objet plaçable — pas besoin de farmland/arrosage.
					canPlantGhost = seedGroundTile != null && seedGroundTile.Walkable;
				}
				else if (itemData.PlantType == "grass")
				{
					canPlantGhost = seedGroundTile != null && seedGroundTile.Material == "grass";
				}
				else
				{
					//  Culture classique : accepter la terre sèche (27) ET humide (29), comme
					// dans PlantCrop_Apply (qui humidifie automatiquement la terre sèche).
					canPlantGhost = groundId == 27 || groundId == 29;
				}
				if (!canPlantGhost) return;
				if (World.GetObjectIdAt(seedTileX, seedTileY) != 0) return;
				
				float distanceToTile = GetPlacementDistanceFromPlayerTile(_playerPos, seedTileX, seedTileY);
				if (distanceToTile > MAX_INTERACTION_DISTANCE) return;
				
				// Récupérer la texture du stade final (adulte pour une culture ; pour un arbre
				// planté, on préfère montrer directement l'arbre adulte final si possible).
				Texture2D seedTex = new Texture2D();
				if (cropTileData.IsSapling && cropTileData.AdultTileId > 0)
				{
					seedTex = WorldTileRegistry.GetFirstTexture(cropTileData.AdultTileId);
				}
				if (seedTex.Id == 0 && WorldTileRegistry.CropStageTextures.TryGetValue(cropId, out var stages) && stages.Count > 0)
				{
					seedTex = stages[stages.Count - 1];
				}
				if (seedTex.Id == 0) seedTex = missingTexture;
				if (seedTex.Id == 0) return;

				var previewTreeData = cropTileData.IsSapling && cropTileData.AdultTileId > 0
					? WorldTileRegistry.GetTile(cropTileData.AdultTileId)
					: cropTileData;
				int seedDrawW = ts;
				int seedDrawH = ts;
				if (previewTreeData?.Size != null)
				{
					seedDrawW = ts * previewTreeData.Size.Width;
					seedDrawH = ts * previewTreeData.Size.Height;
				}
				
				int seedHeight = World.GetHeightAt(seedTileX, seedTileY);
				int seedDrawX = seedTileX * ts + (previewTreeData?.DrawOffset?.X ?? 0);
				int seedDrawY = seedTileY * ts - (seedHeight * ts / 4) - seedDrawH + ts;
				
				Color seedGhostColor = new Color(255, 255, 255, 180);
				
				Raylib.DrawTexturePro(seedTex,
					new Rectangle(0, 0, seedTex.Width, seedTex.Height),
					new Rectangle(seedDrawX, seedDrawY, seedDrawW, seedDrawH),
					Vector2.Zero, 0, seedGhostColor);
				
				UpdateSelectAnimation(Raylib.GetFrameTime());
				if (_selectTextures.Count > 0 && _selectTextures[_selectAnimFrame].Id != 0)
				{
					Texture2D selectTex = _selectTextures[_selectAnimFrame];
					int selectPadding = 16;
					int selectW = seedDrawW + selectPadding * 2;
					int selectH = seedDrawH + selectPadding * 2;
					int selectX = seedDrawX - selectPadding;
					int selectY = seedDrawY - selectPadding;
					Raylib.DrawTexturePro(selectTex,
						new Rectangle(0, 0, selectTex.Width, selectTex.Height),
						new Rectangle(selectX, selectY, selectW, selectH),
						Vector2.Zero, 0, new Color(255, 255, 255, 200));
				}
				return;
			}
			
			// ─────────────────────────────────────────────────────────
			// CAS DES OBJETS PLACABLES (code existant)
			// ─────────────────────────────────────────────────────────
			int placeableId = itemData.PlaceableId;
			if (placeableId == 0) return;
			var tileData = WorldTileRegistry.GetTile(placeableId);
			if (tileData == null) return;
			
			//  CORRECTION : récupérer la première frame d'animation si la tuile est animée
			Texture2D tex;
			if (WorldTileRegistry.IsAnimated(placeableId))
			{
				var frames = WorldTileRegistry.GetAnimationFrames(placeableId);
				tex = (frames != null && frames.Count > 0) ? frames[0] : WorldTileRegistry.GetFirstTexture(placeableId);
			}
			else
			{
				tex = WorldTileRegistry.GetFirstTexture(placeableId);
			}
			
			if (tex.Id == 0) tex = missingTexture;
			if (tex.Id == 0) return;

			// ─────────────────────────────────────────────────────────
			// CAS PARTICULIER : Radeau (ID 500) → gestion bateau
			// ─────────────────────────────────────────────────────────
			Boat? previewBoat = World.GetBoatAtPosition(_playerPos, ts);

			if (placeableId == 500)
			{
				if (previewBoat != null)
				{
					// ── On est sur un bateau existant ─────────────────
					Vector2 mouseLocal = mouseWorld - previewBoat.Position;
					int cursorRelX = (int)Math.Floor((mouseLocal.X + ts / 2f) / ts);
					int cursorRelY = (int)Math.Floor((mouseLocal.Y + ts / 2f) / ts);

					bool isAdjacent = false;
					foreach (var (dx, dy) in new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
					{
						if (previewBoat.Tiles.ContainsKey((cursorRelX + dx, cursorRelY + dy)))
						{
							isAdjacent = true;
							break;
						}
					}

					if (!previewBoat.Tiles.ContainsKey((cursorRelX, cursorRelY)) && isAdjacent)
					{
						Vector2 candidateWorld = previewBoat.GetTileWorldPos(cursorRelX, cursorRelY);
						int worldTileX = (int)(candidateWorld.X / ts);
						int worldTileY = (int)(candidateWorld.Y / ts);

						if ((World.GetGroundTileIdAt(worldTileX, worldTileY) == 10 ||
							 World.GetGroundTileIdAt(worldTileX, worldTileY) == 11) &&
							World.GetObjectIdAt(worldTileX, worldTileY) == 0)
						{
							// Position réelle et continue de la case candidate (pas re-alignée sur
							// la grille MONDE) + décalage de hauteur, pour rester exact même quand
							// le bateau a dérivé et n'est plus pile sur une frontière de tuile.
							int candidateHeight = World.GetHeightAt(worldTileX, worldTileY);
							float candYOffset = -candidateHeight * ts / 4f;
							float boatDrawX = candidateWorld.X - ts / 2f;
							float boatDrawY = candidateWorld.Y - ts / 2f + candYOffset;

							UpdateSelectAnimation(Raylib.GetFrameTime());
							if (_selectTextures.Count > 0 && _selectTextures[_selectAnimFrame].Id != 0)
							{
								Texture2D selectTex = _selectTextures[_selectAnimFrame];
								int selectPadding = 16;
								int selectW = ts + selectPadding * 2;
								int selectH = ts + selectPadding * 2;
								float selectX = boatDrawX - selectPadding;
								float selectY = boatDrawY - selectPadding;
								Raylib.DrawTexturePro(selectTex,
									new Rectangle(0, 0, selectTex.Width, selectTex.Height),
									new Rectangle(selectX, selectY, selectW, selectH),
									Vector2.Zero, 0, new Color(255, 255, 255, 200));
							}

							Color boatGhostColor = new Color(255, 255, 255, 180);
							Raylib.DrawTexturePro(tex,
								new Rectangle(0, 0, tex.Width, tex.Height),
								new Rectangle(boatDrawX, boatDrawY, ts, ts),
								Vector2.Zero, 0, boatGhostColor);
						}
					}
					return;
				}
				// else: on continue vers le cas général (créer un nouveau bateau sur l'eau libre)
			}
			else if (previewBoat != null)
			{
				// ─────────────────────────────────────────────────────────
				// NOUVEAU : tout objet plaçable AUTRE que le radeau, alors que le
				// joueur est sur un bateau → doit obligatoirement viser une case de
				// LA GRILLE DU BATEAU (et non la grille du monde en dessous). Avant
				// ce correctif, seul le radeau (ID 500) avait cet aperçu spécifique ;
				// tous les autres objets (meubles, voiles, gouvernails...) utilisaient
				// FindTileUnderMouse (grille MONDE), ce qui désynchronisait totalement
				// le curseur select1-3 de la vraie case ciblée par HandlePlace (qui,
				// lui, utilise bien la grille du bateau) dès que le bateau avait bougé.
				Vector2 mouseLocal = mouseWorld - previewBoat.Position;
				int relX = (int)Math.Floor((mouseLocal.X + ts / 2f) / ts);
				int relY = (int)Math.Floor((mouseLocal.Y + ts / 2f) / ts);

				if (!previewBoat.Tiles.ContainsKey((relX, relY)))
					return; // hors de la grille du bateau : aucun aperçu, placement impossible

				if (previewBoat.GetObjectIdAt(relX, relY) != 0)
					return; // case déjà occupée

				Vector2 tileWorldPos = previewBoat.GetTileWorldPos(relX, relY);
				int furnTileX = (int)(tileWorldPos.X / ts);
				int furnTileY = (int)(tileWorldPos.Y / ts);
				int height = World.GetHeightAt(furnTileX, furnTileY);
				float yOffset = -height * ts / 4f;

				float furnDrawX = tileWorldPos.X - ts / 2f;
				float furnDrawY = tileWorldPos.Y - ts / 2f + yOffset;
				float furnW = ts, furnH = ts;

				if (tileData.Size != null)
				{
					furnW = ts * tileData.Size.Width;
					furnH = ts * tileData.Size.Height;
					if (tileData.DrawOffset != null)
					{
						furnDrawX += tileData.DrawOffset.X;
						furnDrawY -= tileData.DrawOffset.Y;
					}
					furnDrawY = furnDrawY - furnH + ts;
				}

				UpdateSelectAnimation(Raylib.GetFrameTime());
				if (_selectTextures.Count > 0 && _selectTextures[_selectAnimFrame].Id != 0)
				{
					Texture2D selectTex = _selectTextures[_selectAnimFrame];
					int selectPadding = 16;
					float selectW = furnW + selectPadding * 2;
					float selectH = furnH + selectPadding * 2;
					float selectX = furnDrawX - selectPadding;
					float selectY = furnDrawY - selectPadding;
					Raylib.DrawTexturePro(selectTex,
						new Rectangle(0, 0, selectTex.Width, selectTex.Height),
						new Rectangle(selectX, selectY, selectW, selectH),
						Vector2.Zero, 0, new Color(255, 255, 255, 200));
				}

				Color furnitureGhostColor = new Color(255, 255, 255, 180);
				Raylib.DrawTexturePro(tex,
					new Rectangle(0, 0, tex.Width, tex.Height),
					new Rectangle(furnDrawX, furnDrawY, furnW, furnH),
					Vector2.Zero, 0, furnitureGhostColor);

				return; // sur un bateau : jamais de repli vers la grille du monde
			}

			// ─────────────────────────────────────────────────────────
			// CAS GÉNÉRAL (hors bateau)
			// ─────────────────────────────────────────────────────────
			(int tileX, int tileY, float dist, int _) = FindTileUnderMouse(mouseWorld, ts);
			if (tileX == -1 || tileY == -1) return;

			if (GetPlacementDistanceFromPlayerTile(_playerPos, tileX, tileY) > MAX_INTERACTION_DISTANCE)
				return;

			if (World.GetObjectIdAt(tileX, tileY) != 0) return;

			if (itemData.IsGroundTile)
			{
				int currentGroundId = World.GetGroundTileIdAt(tileX, tileY);
				var currentTileData = WorldTileRegistry.GetTile(currentGroundId);
				if (currentTileData != null && !currentTileData.Diggable) return;
			}

			int heightAtTarget = World.GetHeightAt(tileX, tileY);

			int drawX = tileX * ts;
			int drawY = tileY * ts - (heightAtTarget * ts / 4);

			int w = ts, h = ts;

			if (tileData.Size != null)
			{
				w = ts * tileData.Size.Width;
				h = ts * tileData.Size.Height;

				if (tileData.DrawOffset != null)
				{
					drawX += tileData.DrawOffset.X;
					drawY -= tileData.DrawOffset.Y;
				}

				drawY = drawY - h + ts;
			}

			UpdateSelectAnimation(Raylib.GetFrameTime());
			if (_selectTextures.Count > 0 && _selectTextures[_selectAnimFrame].Id != 0)
			{
				Texture2D selectTex = _selectTextures[_selectAnimFrame];
				int selectPadding = 16;
				int selectW = w + selectPadding * 2;
				int selectH = h + selectPadding * 2;
				int selectX = drawX - selectPadding;
				int selectY = drawY - selectPadding;

				Raylib.DrawTexturePro(selectTex,
					new Rectangle(0, 0, selectTex.Width, selectTex.Height),
					new Rectangle(selectX, selectY, selectW, selectH),
					Vector2.Zero, 0, new Color(255, 255, 255, 200));
			}

			Color ghostColor = new Color(255, 255, 255, 180);
			Raylib.DrawTexturePro(tex,
				new Rectangle(0, 0, tex.Width, tex.Height),
				new Rectangle(drawX, drawY, w, h),
				Vector2.Zero, 0, ghostColor);

			if (_showDebug && DebugOverlaySettings.TileHitboxes && tileData.CollisionSize != null)
			{
				int colW = ts * tileData.CollisionSize.Width;
				int colH = ts * tileData.CollisionSize.Height;
				int colX = drawX + (w - colW) / 2;
				int colY = drawY + (h - colH);

				if (tileData.CollisionOffset != null)
				{
					colX += tileData.CollisionOffset.X;
					colY -= tileData.CollisionOffset.Y;
				}

				Raylib.DrawRectangleLines(colX, colY, colW, colH, Color.Red);
			}
		}

    }
}
