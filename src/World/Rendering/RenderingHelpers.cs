// RenderingHelpers.cs
// Fichier extrait de World.Core.cs — classe partielle World.
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {
		private static readonly List<(int x, int y, BiomeBorderDirection direction)> _renderBiomeBorders = new();

		private static void CollectBiomeBordersForTile(int[,] groundIds, int x, int y, int width, int height)
		{
			int currentGround = groundIds[x, y];
			if ((!WaterLegacyBorderEnabled && currentGround == 10) || !HasBiomeBorder(currentGround))
				return;

			if (x + 1 < width)
			{
				int neighborGround = groundIds[x + 1, y];
				if (neighborGround != currentGround && GetBiomePriority(currentGround) > GetBiomePriority(neighborGround))
					_renderBiomeBorders.Add((x, y, BiomeBorderDirection.Right));
			}
			if (x > 0)
			{
				int neighborGround = groundIds[x - 1, y];
				if (neighborGround != currentGround && GetBiomePriority(currentGround) > GetBiomePriority(neighborGround))
					_renderBiomeBorders.Add((x, y, BiomeBorderDirection.Left));
			}
			if (y + 1 < height)
			{
				int neighborGround = groundIds[x, y + 1];
				if (neighborGround != currentGround && GetBiomePriority(currentGround) > GetBiomePriority(neighborGround))
					_renderBiomeBorders.Add((x, y, BiomeBorderDirection.Bottom));
			}
			if (y > 0)
			{
				int neighborGround = groundIds[x, y - 1];
				if (neighborGround != currentGround && GetBiomePriority(currentGround) > GetBiomePriority(neighborGround))
					_renderBiomeBorders.Add((x, y, BiomeBorderDirection.Top));
			}
		}

		//  PERF : cache des clés "x_y" utilisées par destroyed/objectHP. Avant : une string allouée par
		// objet visible et par frame (milliers en dézoom forestier => pression GC).
		private static readonly Dictionary<(int, int), string> _tileKeyCache = new();
		private static string GetTileKeyString(int x, int y)
		{
			if (_tileKeyCache.TryGetValue((x, y), out var k)) return k;
			if (_tileKeyCache.Count > 60000) _tileKeyCache.Clear();
			k = $"{x}_{y}";
			_tileKeyCache[(x, y)] = k;
			return k;
		}

		//  PERF : noms "<prefix>_trunk" / "<prefix>_bush" mis en cache (interpolation par arbre et par frame).
		private static readonly Dictionary<string, (string trunk, string bush)> _treeLayerNames = new();
		private static (string trunk, string bush) GetTreeLayerNames(string prefix)
		{
			if (_treeLayerNames.TryGetValue(prefix, out var n)) return n;
			n = ($"{prefix}_trunk", $"{prefix}_bush");
			_treeLayerNames[prefix] = n;
			return n;
		}

		//  PERF : cache des sols statiques simples, qui partagent leur texture sur toutes les tuiles.
		private static readonly Dictionary<int, Texture2D> _plainGroundTexCache = new();
		private static Dictionary<int, Texture2D>? _plainGroundTexOwner;

		private static Texture2D GetGroundTileTextureFast(int x, int y, int tileId, float currentTime)
		{
			if (tileId == 123) return GetTileTexture(x, y, tileId, currentTime);
			if (!ReferenceEquals(_plainGroundTexOwner, _tileTextures))
			{
				_plainGroundTexCache.Clear();
				_plainGroundTexOwner = _tileTextures;
			}
			if (_plainGroundTexCache.TryGetValue(tileId, out var cached)) return cached;

			var tileData = WorldTileRegistry.GetTile(tileId);
			if (tileData != null && !tileData.HasGrowthStages && !WorldTileRegistry.IsAnimated(tileId))
			{
				var variations = WorldTileRegistry.GetVariations(tileId);
				if ((variations == null || variations.Count <= 1) && _tileTextures.TryGetValue(tileId, out var plain))
				{
					_plainGroundTexCache[tileId] = plain;
					return plain;
				}
			}
			return GetTileTexture(x, y, tileId, currentTime);
		}

		/// <summary>À appeler si les textures de tuiles sont rechargées/changées (pack de textures...).</summary>
		public static void ResetTerrainTextureCaches()
		{
			_plainGroundTexCache.Clear();
			_plainGroundTexOwner = null;
		}

		private static void EnsureRenderTileBuffers(int width, int height)
		{
			if (_renderHeights.GetLength(0) >= width && _renderHeights.GetLength(1) >= height)
				return;

			_renderHeights = new int[width, height];
			_renderGroundIds = new int[width, height];
			_renderObjectIds = new int[width, height];
			_renderOverlayIds = new int[width, height];
			_renderBuildingIds = new int[width, height];
			_renderRoofHeights = new int[width, height];
			_renderDecorationIndex = new int[width, height];
			_renderWallCoveringIds = new int[width, height];
			_renderIsWallToHide = new bool[width, height];
			_renderTileLit = new bool[width, height];
		}

		private static readonly List<Entity> _doorIconResidentScratch = new();
		private static readonly List<(string actionKey, string actionType, Texture2D icon)> _interactionPromptScratch = new();

		private static void AddHouseResidentDoorIcons(List<RenderItem> renderItems, List<Entity> entities, Rectangle viewBounds)
		{
			foreach (var house in Houses.Values)
			{
				if (house.BuildingId <= 0) continue;
				//  PERF : avant, TOUTES les maisons du monde passaient ici chaque frame (LINQ sur toutes
				// les entités + DrawEntity de leurs résidents), même très hors écran.
				Vector2 doorPosCull = house.DoorWorldPos;
				if (doorPosCull.X < viewBounds.X || doorPosCull.X > viewBounds.X + viewBounds.Width ||
					doorPosCull.Y < viewBounds.Y || doorPosCull.Y > viewBounds.Y + viewBounds.Height)
					continue;
				var residents = _doorIconResidentScratch;
				residents.Clear();
				for (int ei = 0; ei < entities.Count; ei++)
				{
					var cand = entities[ei];
					if (cand.IsAlive && cand.IsVillager && cand.HomeBuildingId == house.BuildingId)
						residents.Add(cand);
				}
				if (residents.Count == 0) continue;
				if (residents.Count > 1) residents.Sort(static (a, b) => a.NetId.CompareTo(b.NetId));

				int extraCount = Math.Max(0, residents.Count - 3);
				int visibleCount = Math.Min(3, residents.Count);
				float iconSize = 22f;
				float gap = 18f;
				float totalWidth = Math.Max(0f, (visibleCount - 1) * gap);
				Vector2 basePos = house.DoorWorldPos + new Vector2(0f, -28f);
				float startX = basePos.X - totalWidth / 2f;

				for (int i = 0; i < visibleCount; i++)
				{
					var resident = residents[i];
					var hairBaseTex = (resident.Species == "human" && Program.hairBaseTextures.Count > 0)
						? Program.hairBaseTextures[Math.Clamp(resident.HairStyle, 0, Program.hairBaseTextures.Count - 1)]
						: new Texture2D();
					var hairOverlayTex = (resident.Species == "human" && Program.hairOverlayTextures.Count > 0)
						? Program.hairOverlayTextures[Math.Clamp(resident.HairStyle, 0, Program.hairOverlayTextures.Count - 1)]
						: new Texture2D();
					var eyeBaseTex = (resident.Species == "human" && Program.EyeBaseTextures.Count > 0)
						? Program.EyeBaseTextures[Math.Clamp(Program.EyeStyle, 0, Program.EyeBaseTextures.Count - 1)]
						: new Texture2D();
					var eyeOverlayTex = (resident.Species == "human" && Program.EyeOverlayTextures.Count > 0)
						? Program.EyeOverlayTextures[Math.Clamp(Program.EyeStyle, 0, Program.EyeOverlayTextures.Count - 1)]
						: new Texture2D();
					Color tint = resident.Tint.R != 0 || resident.Tint.G != 0 || resident.Tint.B != 0 || resident.Tint.A != 0 ? resident.Tint : Color.White;
					Color hairColor = resident.HairColor.R != 0 || resident.HairColor.G != 0 || resident.HairColor.B != 0 || resident.HairColor.A != 0 ? resident.HairColor : Color.White;
					Vector2 iconPos = new Vector2(startX + i * gap, basePos.Y);

					renderItems.Add(new RenderItem
					{
						Y = float.MaxValue - 1000f,
						DrawAction = () =>
						{
							Rectangle bgRect = new Rectangle(iconPos.X - iconSize / 2f - 3f, iconPos.Y - iconSize / 2f - 3f, iconSize + 6f, iconSize + 6f);
							Raylib.DrawRectangleRounded(bgRect, 0.35f, 10, new Color(18, 18, 20, 200));
							Raylib.DrawRectangleRoundedLines(bgRect, 0.35f, 10, 2, new Color(215, 180, 110, 180));
							EntityRenderer.DrawEntity(
								resident.Species,
								"idle",
								0,
								0f,
								1f,
								new Vector2(iconPos.X, iconPos.Y + 8f),
								tint,
								hairBaseTex,
								hairOverlayTex,
								hairColor,
								SpeciesData.Skeletons,
								eyeBaseTex,
								eyeOverlayTex,
								customScale: 0.42f,
								equipment: resident.Equipment,
								isCarrying: false,
								inWater: false,
								attackSwingProgress: 0f,
								heldItemTexture: default,
								headAngle: 0f,
								keepItemHorizontal: false,
								isBow: false,
								underwearTexture: default,
								beardStyle: resident.BeardStyle,
								prevAnim: "",
								prevFrame: 0,
								prevProg: 0f,
								transitionWeight: 0f,
								flashWhite: false);
						}
					});
				}

				if (extraCount > 0)
				{
					Vector2 extraPos = basePos + new Vector2((visibleCount > 0 ? (visibleCount - 1) * gap : 0f) / 2f + 12f, 6f);
					renderItems.Add(new RenderItem
					{
						Y = float.MaxValue - 999f,
						DrawAction = () =>
						{
							Rectangle badgeRect = new Rectangle(extraPos.X - 8f, extraPos.Y - 8f, 16f, 16f);
							Raylib.DrawRectangleRounded(badgeRect, 0.5f, 8, new Color(72, 52, 30, 220));
							Raylib.DrawRectangleRoundedLines(badgeRect, 0.5f, 8, 1, new Color(240, 220, 160, 220));
							FontManager.DrawText($"+{extraCount}", (int)extraPos.X - 5, (int)extraPos.Y - 6, 10, new Color(255, 245, 220, 255));
						}
					});
				}
			}
		}

		/// <summary>
		/// Décalage visuel vertical (pixels, positif = vers le haut) appliqué à un objet posé au sol à
		/// la position logique donnée. Le terrain, les objets, les entités et le joueur sont tous
		/// dessinés remontés de (hauteur * ts / 4) ; les objets au sol ne l'étaient pas, donc ils
		/// paraissaient plus bas que le joueur de cette valeur et il fallait « monter » pour les
		/// attraper alors que la logique (ramassage, casse) compare des positions logiques.
		/// </summary>
		private static float GetGroundItemHeightLift(float worldX, float worldY)
		{
			if (_isUnderground) return 0f;
			int ts = Program.TileSize;
			int h = GetHeightAt((int)Math.Floor(worldX / ts), (int)Math.Floor(worldY / ts));
			return h * ts / 4f;
		}


    }
}
