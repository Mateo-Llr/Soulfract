// World.Underwater.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {

		public static bool IsUnderwater { get; set; } = false;

		private static Dictionary<(int chunkX, int chunkY), UnderwaterChunkData> _underwaterChunks = new();

		// hauteur du fond marin

		// Structure d'un chunk sous-marin
		public class UnderwaterChunkData
		{
			public Dictionary<(int x, int y), int> Objects = new();    // objets (coraux, rochers...)
			public Dictionary<(int x, int y), int> Decorations = new();
			public Dictionary<(int x, int y), int> SeaFloor = new();    // type de sol (sable, vase...)
			public List<Entity> Entities = new();                        // poissons, créatures
			public bool IsGenerated = false;
			public float LastAccessTime = 0;
		}

		private static void UpdateUnderwaterChunks(Vector2 playerPos, float currentTime)
		{
			int ts = Program.TileSize;
			Camera2D camera = Program.GetCurrentCamera();
			
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			
			Vector2 topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera);
			Vector2 bottomRight = Raylib.GetScreenToWorld2D(new Vector2(sw, sh), camera);
			
			int minChunkX = (int)Math.Floor(topLeft.X / (CHUNK_SIZE * ts)) - 1;
			int maxChunkX = (int)Math.Ceiling(bottomRight.X / (CHUNK_SIZE * ts)) + 1;
			int minChunkY = (int)Math.Floor(topLeft.Y / (CHUNK_SIZE * ts)) - 1;
			int maxChunkY = (int)Math.Ceiling(bottomRight.Y / (CHUNK_SIZE * ts)) + 1;
			
			for (int x = minChunkX; x <= maxChunkX; x++)
			{
				for (int y = minChunkY; y <= maxChunkY; y++)
				{
					if (!_underwaterChunks.ContainsKey((x, y)))
					{
						var chunk = GenerateUnderwaterChunk(x, y);
						chunk.LastAccessTime = currentTime;
						_underwaterChunks[(x, y)] = chunk;
					}
				}
			}
		}

		private static Texture2D GetTileTextureForSeaFloor(int x, int y, int tileId, float currentTime)
		{
			// Utilise la même logique que GetTileTexture mais avec les textures sous-marines
			if (tileId == 60) return _tileTextures.GetValueOrDefault(60); // Sable
			if (tileId == 61) return _tileTextures.GetValueOrDefault(61); // Roche
			
			// Fallback
			return _tileTextures.GetValueOrDefault(tileId);
		}

		private static int GetUnderwaterChunkSeaFloor(int x, int y)
		{
			int chunkX = x / CHUNK_SIZE;
			int chunkY = y / CHUNK_SIZE;
			if (x < 0) chunkX = (x - CHUNK_SIZE + 1) / CHUNK_SIZE;
			if (y < 0) chunkY = (y - CHUNK_SIZE + 1) / CHUNK_SIZE;
			
			if (_underwaterChunks.TryGetValue((chunkX, chunkY), out var chunk))
			{
				if (chunk.SeaFloor.TryGetValue((x, y), out int floorId))
					return floorId;
			}
			return 60; // Sable par défaut
		}
    }
}
