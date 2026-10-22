// TreasureAndOasis.cs
// Fichier extrait de World.Core.cs — classe partielle World.
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {
		private static readonly (string key, float weight, int min, int max)[] _undergroundTreasureLootTable = new[]
		{
			("coin", 25f, 5, 20),         // Pièce (monnaie)
			("ruby", 8f,  1, 3),          // Rubis
			("iron_ingot", 10f, 2, 5),    // Lingot de fer
			("runic_stone", 5f,  1, 1),   // Pierre runique
		};

		/// <summary>
		/// Génère le contenu (aléatoire) d'un coffre de trésor souterrain : quelques emplacements
		/// remplis d'items tirés dans la table de loot pondérée ci-dessus, le reste vide.
		/// </summary>
		private static ContainerInventoryData GenerateUndergroundTreasureLoot(Random rand)
		{
			var container = new ContainerInventoryData(9, 3);

			var table = _undergroundTreasureLootTable.Where(e => e.weight > 0f).ToArray();
			float totalWeight = table.Sum(e => e.weight);
			if (totalWeight <= 0f || table.Length == 0) return container;

			int rollCount = rand.Next(2, 5); // 2 à 4 items par trésor
			int slotIndex = 0;
			for (int i = 0; i < rollCount && slotIndex < container.Slots.Count; i++)
			{
				float roll = (float)rand.NextDouble() * totalWeight;
				float cumulative = 0f;
				foreach (var entry in table)
				{
					cumulative += entry.weight;
					if (roll <= cumulative)
					{
						if (GameData.TryGetItemByKey(entry.key, out var itemData))
						{
							int count = entry.min == entry.max ? entry.min : rand.Next(entry.min, entry.max + 1);
							var item = new Item(itemData.Name, count, itemData.Color, itemData.Icon);
							container.Slots[slotIndex].Item = item;
							container.Slots[slotIndex].Count = count;
							slotIndex++;
						}
						break;
					}
				}
			}
			return container;
		}

		private static void TryPlaceDesertOasis(int chunkX, int chunkY, Random chunkRandom, ChunkData chunkData)
		{
			int desertTiles = 0;
			for (int localX = 0; localX < CHUNK_SIZE; localX++)
			{
				for (int localY = 0; localY < CHUNK_SIZE; localY++)
				{
					int worldX = chunkX * CHUNK_SIZE + localX;
					int worldY = chunkY * CHUNK_SIZE + localY;
					if (GetBiomeAt(worldX, worldY) == Biome.Desert)
						desertTiles++;
				}
			}

			if (desertTiles < CHUNK_SIZE * CHUNK_SIZE / 3)
				return;

			const double oasisChance = 0.08;
			if (chunkRandom.NextDouble() >= oasisChance)
				return;

			int localCenterX = chunkRandom.Next(4, CHUNK_SIZE - 4);
			int localCenterY = chunkRandom.Next(4, CHUNK_SIZE - 4);
			int centerX = chunkX * CHUNK_SIZE + localCenterX;
			int centerY = chunkY * CHUNK_SIZE + localCenterY;
			int radius = chunkRandom.Next(2, 4);

			var waterPositions = new List<(int x, int y)>();
			for (int localX = localCenterX - radius; localX <= localCenterX + radius; localX++)
			{
				for (int localY = localCenterY - radius; localY <= localCenterY + radius; localY++)
				{
					if (localX < 0 || localX >= CHUNK_SIZE || localY < 0 || localY >= CHUNK_SIZE)
						continue;

					int dx = localX - localCenterX;
					int dy = localY - localCenterY;
					int distSq = dx * dx + dy * dy;
					int maxSq = radius * radius;
					if (distSq > maxSq)
						continue;

					int worldX = chunkX * CHUNK_SIZE + localX;
					int worldY = chunkY * CHUNK_SIZE + localY;
					if (chunkData.Objects.ContainsKey((worldX, worldY)))
						continue;

					int groundId = GetGroundTileIdAt(worldX, worldY);
					if (groundId != 11)
						continue;

					if (chunkRandom.NextDouble() < 0.75 || distSq <= (radius - 1) * (radius - 1))
					{
						waterPositions.Add((worldX, worldY));
					}
				}
			}

			if (waterPositions.Count == 0)
				return;

			foreach (var pos in waterPositions)
			{
				chunkData.GroundOverrides[(pos.x, pos.y)] = 10;
			}

			var palmCandidates = new List<(int x, int y)>();
			for (int localX = localCenterX - radius - 1; localX <= localCenterX + radius + 1; localX++)
			{
				for (int localY = localCenterY - radius - 1; localY <= localCenterY + radius + 1; localY++)
				{
					if (localX < 0 || localX >= CHUNK_SIZE || localY < 0 || localY >= CHUNK_SIZE)
						continue;

					int worldX = chunkX * CHUNK_SIZE + localX;
					int worldY = chunkY * CHUNK_SIZE + localY;
					if (chunkData.Objects.ContainsKey((worldX, worldY)))
						continue;
					if (waterPositions.Contains((worldX, worldY)))
						continue;

					int groundId = GetGroundTileIdAt(worldX, worldY);
					if (groundId != 11)
						continue;

					bool adjacentToWater = false;
					for (int nx = worldX - 1; nx <= worldX + 1 && !adjacentToWater; nx++)
					{
						for (int ny = worldY - 1; ny <= worldY + 1; ny++)
						{
							if (nx == worldX && ny == worldY) continue;
							if (waterPositions.Contains((nx, ny)))
							{
								adjacentToWater = true;
								break;
							}
						}
					}

					if (adjacentToWater)
						palmCandidates.Add((worldX, worldY));
				}
			}

			int palmsToPlace = Math.Min(3, Math.Max(1, chunkRandom.Next(1, 4)));
			for (int i = 0; i < palmsToPlace && palmCandidates.Count > 0; i++)
			{
				int index = chunkRandom.Next(palmCandidates.Count);
				var palmPos = palmCandidates[index];
				palmCandidates.RemoveAt(index);
				chunkData.Objects[palmPos] = 49;
				palmCandidates.RemoveAll(p => p == palmPos);
			}
		}


    }
}
