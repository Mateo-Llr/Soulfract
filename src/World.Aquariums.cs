// World.Aquariums.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {

		private static Dictionary<(int x, int y), AquariumData> _aquariums = new();

		public static void UpdateAquariums(float dt, float currentTime)
		{
			foreach (var (pos, aquarium) in _aquariums)
			{
				var chunk = GetChunkAt(pos.x, pos.y);
				if (chunk == null) continue;
				var container = chunk.GetContainerAt(pos.x, pos.y);
				if (container == null) continue;

				var tileData = WorldTileRegistry.GetTile(99);
				if (tileData?.Size == null) continue;
				int ts = Program.TileSize;
				int height = GetHeightAt(pos.x, pos.y);
				float yOffset = -height * ts / 4;

				float drawX = pos.x * ts + (tileData.DrawOffset?.X ?? 0);
				float drawY = pos.y * ts + yOffset + (tileData.DrawOffset?.Y ?? 0);
				int objWidth = ts * tileData.Size.Width;
				int objHeight = ts * tileData.Size.Height;
				
				drawY = drawY - objHeight + ts;

				int marginX = 12;
				int marginTop = 20;
				int marginBottom = 12;
				
				if (aquarium.SwimArea.Width <= 0 || aquarium.SwimArea.Height <= 0)
				{
					aquarium.SwimArea = new Rectangle(
						drawX + marginX,
						drawY + marginTop,
						objWidth - marginX * 2,
						objHeight - marginTop - marginBottom
					);
				}

				for (int i = 0; i < container.Slots.Count; i++)
				{
					var slot = container.Slots[i];
					if (!slot.IsEmpty && slot.Item != null)
					{
						if (!aquarium.FishStates.TryGetValue(i, out var fish))
						{
							Random rand = new Random();
							float baseY = 0.4f + (float)rand.NextDouble() * 0.3f;
							fish = new AquariumFishState
							{
								SlotIndex = i,
								LocalPosition = new Vector2(
									0.1f + (float)rand.NextDouble() * 0.8f,
									baseY
								),
								TargetPosition = Vector2.Zero,
								MoveSpeed = 0.12f + (float)rand.NextDouble() * 0.1f,
								IsMoving = false,
								IdleTimer = 1f + (float)rand.NextDouble() * 2f,
								BobSpeed = 1.2f + (float)rand.NextDouble() * 0.8f,
								BobPhase = (float)(rand.NextDouble() * Math.PI * 2),
								MoveStartTime = 0f,
								MoveDuration = 0f,
								BaseY = baseY
							};
							aquarium.FishStates[i] = fish;
						}
						else
						{
							if (!fish.IsMoving)
							{
								// Réduire le timer d'immobilité
								fish.IdleTimer -= dt;
								
								if (fish.IdleTimer <= 0f)
								{
									// Décider d'une nouvelle position cible (TOUJOURS un mouvement, mais parfois très court)
									// 70% de chance de se déplacer, 30% de chance de "bouger sur place"
									if (Random.Shared.NextDouble() < 0.7f)
									{
										// Nouvelle position X (déplacement visible)
										float targetX = fish.LocalPosition.X + (float)(Random.Shared.NextDouble() - 0.5) * 0.35f;
										targetX = Math.Clamp(targetX, 0.08f, 0.92f);
										fish.TargetPosition = new Vector2(targetX, fish.BaseY);
									}
									else
									{
										// "Bouge sur place" : reste à la même position
										fish.TargetPosition = new Vector2(fish.LocalPosition.X, fish.BaseY);
									}
									
									fish.IsMoving = true;
									fish.MoveStartTime = currentTime;
									float distance = Math.Abs(fish.LocalPosition.X - fish.TargetPosition.X);
									fish.MoveDuration = distance / fish.MoveSpeed;
									if (fish.MoveDuration < 0.1f) fish.MoveDuration = 0.3f;
								}
							}
							else
							{
								float progress = (currentTime - fish.MoveStartTime) / fish.MoveDuration;
								
								if (progress >= 1f)
								{
									// Mouvement terminé
									fish.LocalPosition.X = fish.TargetPosition.X;
									fish.LocalPosition.Y = fish.BaseY;
									fish.IsMoving = false;
									// Temps de pause entre les mouvements
									fish.IdleTimer = 1.5f + (float)Random.Shared.NextDouble() * 3f;
								}
								else
								{
									// Déplacement fluide (easing)
									float t = progress;
									// Easing smooth
									float easedT = t * t * (3f - 2f * t);
									fish.LocalPosition.X = MathHelper.Lerp(fish.LocalPosition.X, fish.TargetPosition.X, easedT * 0.15f);
									fish.LocalPosition.Y = fish.BaseY;
								}
							}
						}
					}
					else
					{
						aquarium.FishStates.Remove(i);
					}
				}
				aquarium.LastUpdateTime = currentTime;
			}
		}

		public static AquariumData? GetAquariumData(int x, int y)
        {
            if (_aquariums.TryGetValue((x, y), out var aquariumData))
                return aquariumData;
            return null;
        }

		public static void SetAquariumData(int x, int y, AquariumData aquariumData)
        {
            _aquariums[(x, y)] = aquariumData;
        }
    }
}
