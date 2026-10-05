// World.Pathfinding.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {

		public static Vector2 FindSpawnNearExit(int tileX, int tileY)
		{
			int ts = Program.TileSize;

			// Si la tuile est une entrée ou sortie de grotte, on préfère une case libre juste en dessous.
			// Cela évite de réapparaître dans la tuile de l’ouverture elle-même.
			int objectId = GetObjectIdAt(tileX, tileY);
			if (objectId == 50 || objectId == 52)
			{
				// On cherche une case libre en dessous de l’ouverture, puis sur les cases voisines,
				// pour que l’apparition soit toujours un peu plus bas et cohérente à l’entrée et à la sortie.
				for (int dy = 1; dy <= 6; dy++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						int cx = tileX + dx;
						int cy = tileY + dy;
						if (GetObjectIdAt(cx, cy) != 0)
							continue;

						int groundId = GetGroundTileIdAt(cx, cy);
						var groundTile = WorldTileRegistry.GetTile(groundId);
						if (groundTile == null || !groundTile.Walkable)
							continue;

						_heightCache.Remove((cx, cy));
						_caveHeightCache.Remove((cx, cy));

						int height = GetHeightAt(cx, cy);
						float yOffset = -height * ts / 4;
						float groundTop = cy * ts + yOffset + ts;
						float playerY = groundTop - Program.FeetOffsetY;
						return new Vector2(cx * ts + ts / 2f, playerY);
					}
				}

				// Fallback : si aucune case libre ne convient, on garde la case de l’ouverture.
				_heightCache.Remove((tileX, tileY));
				_caveHeightCache.Remove((tileX, tileY));

				int heightFallback = GetHeightAt(tileX, tileY);
				float yOffsetFallback = -heightFallback * ts / 4;
				float groundTopFallback = tileY * ts + yOffsetFallback + ts;
				float playerYFallback = groundTopFallback - Program.FeetOffsetY;
				return new Vector2(tileX * ts + ts / 2f, playerYFallback);
			}

			// Sinon, recherche d'une tuile adjacente libre et walkable
			int[][] dirs = { new[] { 0, 1 }, new[] { 1, 0 }, new[] { 0, -1 }, new[] { -1, 0 } };
			foreach (var dir in dirs)
			{
				int nx = tileX + dir[0];
				int ny = tileY + dir[1];
				int groundId = GetGroundTileIdAt(nx, ny);
				var groundTile = WorldTileRegistry.GetTile(groundId);
				if (groundTile != null && groundTile.Walkable && GetObjectIdAt(nx, ny) == 0)
				{
					int height = GetHeightAt(nx, ny);
					float yOffset = -height * ts / 4;
					float groundTop = ny * ts + yOffset + ts;
					float playerY = groundTop - Program.FeetOffsetY;
					return new Vector2(nx * ts + ts / 2f, playerY);
				}
			}

			// Fallback : spawn sur la tuile elle‑même (cas extrême)
			int heightExit = GetHeightAt(tileX, tileY);
			float yOffsetExit = -heightExit * ts / 4;
			float exitTop = tileY * ts + yOffsetExit + ts;
			float playerYExit = exitTop - Program.FeetOffsetY;
			return new Vector2(tileX * ts + ts / 2f, playerYExit);
		}

		private const float GLOBAL_SPAWN_CHANCE = 0.65f;

		private const float CAVE_ENTRY_SPAWN_CHANCE = 0.02f;

		private static float _particleSpawnTimer = 0f;

		private const float PARTICLE_SPAWN_INTERVAL = 0.1f;

		// Une particule toutes les 0.1 sec max par source

		public static void UpdateTileParticles(Vector2 playerPos, List<Particle> particles, float dt)
		{
			_particleSpawnTimer += dt;
			if (_particleSpawnTimer < PARTICLE_SPAWN_INTERVAL) return;
			_particleSpawnTimer = 0f;

			int ts = Program.TileSize;
			int range = 10; // Rayon autour du joueur pour générer des particules
			
			int startX = (int)(playerPos.X / ts) - range;
			int startY = (int)(playerPos.Y / ts) - range;
			int endX = (int)(playerPos.X / ts) + range;
			int endY = (int)(playerPos.Y / ts) + range;

			for (int x = startX; x <= endX; x++)
			{
				for (int y = startY; y <= endY; y++)
				{
					int objectId = GetObjectIdAt(x, y);
					if (objectId == 0) continue;

					// Vérifier si c'est une torche (33) ou un feu de camp (18/32)
					bool isTorch = (objectId == 33);
					bool isCampfire = (objectId == 18 || objectId == 32);
					bool isLeafTree = IsLeafBearingTree(objectId);

					if (!isTorch && !isCampfire && !isLeafTree) continue;

					if (isLeafTree)
					{
						//  De temps en temps, une feuille se détache toute seule (chute ambiante,
						// bien plus rare qu'un effet de feu)
						if (Random.Shared.NextDouble() < 0.015)
							SpawnLeafParticle(x, y, particles);
						continue;
					}

					// Générer une particule avec 20% de chance par tuile (on limite à une par source par intervalle)
					if (Random.Shared.NextDouble() > 0.2) continue;

					// Position monde de la tuile
					int height = GetHeightAt(x, y);
					float yOffset = -height * ts / 4;
					float worldX = x * ts + ts / 2f;
					float worldY = y * ts + yOffset + ts / 2f;

					// Légère variation aléatoire
					worldX += (float)(Random.Shared.NextDouble() - 0.5) * ts * 0.5f;
					worldY += (float)(Random.Shared.NextDouble() - 0.5) * ts * 0.3f;

					// Type de particule : fumée pour les deux, braise pour le campfire
					if (isCampfire && Random.Shared.NextDouble() < 0.6)
						SpawnEmberParticle(worldX, worldY, particles);
					else
						SpawnSmokeParticle(worldX, worldY, particles);
				}
			}
		}

		public static void SpawnSmokeParticle(float x, float y, List<Particle> particles)
		{
			Vector2 velocity = new Vector2(
				(float)(Random.Shared.NextDouble() - 0.5) * 20f,
				-(float)(Random.Shared.NextDouble() * 30f + 20f)
			);
			Color smokeColor = new Color(100, 100, 100, 180);
			float size = Random.Shared.Next(4, 10);
			float lifetime = Random.Shared.Next(800, 1500) / 1000f;
			particles.Add(new Particle(new Vector2(x, y), velocity, smokeColor, size, lifetime));
		}

		public static void SpawnEmberParticle(float x, float y, List<Particle> particles)
		{
			Vector2 velocity = new Vector2(
				(float)(Random.Shared.NextDouble() - 0.5) * 40f,
				-(float)(Random.Shared.NextDouble() * 40f + 30f)
			);
			Color emberColor = new Color(255, 140 + Random.Shared.Next(-30, 0), 40, 255);
			float size = Random.Shared.Next(3, 7);
			float lifetime = Random.Shared.Next(400, 800) / 1000f;
			particles.Add(new Particle(new Vector2(x, y), velocity, emberColor, size, lifetime));
		}

		public static void SpawnLeafParticle(int tileX, int tileY, List<Particle> particles)
		{
			if (!TryGetTreeLeafSpawnPoints(tileX, tileY, out List<Vector2> spawnPoints, out float groundY)) return;
			if (spawnPoints.Count == 0) return;

			int treeId = GetObjectIdAt(tileX, tileY);
			Random rand = new Random();
			Vector2 spawn = spawnPoints[rand.Next(spawnPoints.Count)];
			float lifetime = 6f + (float)rand.NextDouble() * 4f;
			Color tint = GetTreeLeafTintForObjectId(treeId, tileX, tileY);

			particles.Add(new LeafParticle(spawn, groundY, lifetime, tint));
		}

		//  Rafale de feuilles : utilisée quand l'arbre est frappé (peu de feuilles) ou abattu
		// (beaucoup de feuilles), pour un effet plus spectaculaire que la chute ambiante.
		public static void SpawnLeafBurst(int tileX, int tileY, List<Particle> particles, int count)
		{
			if (!TryGetTreeLeafSpawnPoints(tileX, tileY, out List<Vector2> spawnPoints, out float groundY)) return;
			if (spawnPoints.Count == 0) return;

			int treeId = GetObjectIdAt(tileX, tileY);
			Random rand = new Random();
			Color tint = GetTreeLeafTintForObjectId(treeId, tileX, tileY);
			for (int i = 0; i < count; i++)
			{
				Vector2 spawn = spawnPoints[rand.Next(spawnPoints.Count)];
				float lifetime = 5f + (float)rand.NextDouble() * 4f;
				particles.Add(new LeafParticle(spawn, groundY, lifetime, tint));
			}
		}

		//  Utilisé par le pathfinding (Pathfinding.cs) pour savoir quelles tuiles d'une zone
        // sont bloquées par la hitbox RÉELLE d'un objet, y compris quand cette hitbox déborde
        // sur des tuiles voisines qui n'ont elles-mêmes aucun objectId enregistré (décor avec
        // CollisionSize ou Size > 1 tuile, cf. GetCollisionRect ci-dessus). Avant, le
        // pathfinding se contentait de regarder l'objectId de la tuile testée elle-même : il
        // ignorait ces débordements de hitbox et laissait passer des chemins qui, dans le vrai
        // système de collision (CheckCollision), sont en réalité bloqués — d'où des
        // trajectoires en vert qui traversaient des tuiles affichant pourtant une hitbox en
        // debug (F3).
        //  IMPORTANT (perf) : cette fonction calcule le résultat pour TOUTE une zone en une
        // seule passe (un seul parcours des objets de la zone), au lieu d'être appelée tuile
        // par tuile avec un rescan de voisinage à chaque fois. Une première version appelait un
        // scan de 7x7 tuiles par case testée : avec des milliers de cases explorées par l'A*
        // (jusqu'à MAX_NODE_EXPANSIONS × 8 voisins), ça faisait des millions de vérifications
        // par calcul de chemin et provoquait un lag énorme. Ici, on parcourt une seule fois les
        // objets de la zone et on marque directement, pour chacun, les quelques tuiles que sa
        // hitbox recouvre réellement — le coût redevient proportionnel à la taille de la zone,
        // plus le nombre d'objets qu'elle contient, et non plus au nombre de nœuds explorés.
        public static HashSet<(int x, int y)> GetHitboxBlockedTiles(int minX, int maxX, int minY, int maxY, HashSet<string> destroyed)
        {
            var blocked = new HashSet<(int x, int y)>();
            int ts = Program.TileSize;
            // Marge pour couvrir les objets ancrés juste hors de la zone mais dont la hitbox
            // déborde dedans (un gros objet peut être ancré plusieurs tuiles avant son bord).
            const int pad = 3;

            for (int wX = minX - pad; wX <= maxX + pad; wX++)
            {
                for (int wY = minY - pad; wY <= maxY + pad; wY++)
                {
                    string key = $"{wX}_{wY}";
                    if (destroyed.Contains(key)) continue;
                    if (IsDoorTile(wX, wY)) continue;

                    int objectId = GetObjectIdAt(wX, wY);
                    if (objectId == 0) continue;
                    var tileData = WorldTileRegistry.GetTile(objectId);
                    if (tileData == null || tileData.Walkable) continue;

                    var (x, y, w, h) = GetCollisionRect(wX, wY, tileData, ts);
                    // (Ancrage bas-gauche géré dans GetCollisionRect, plus de recorrection ici.)
                    Rectangle objRect = new Rectangle(x, y, w, h);

                    int tileMinX = (int)Math.Floor(objRect.X / (float)ts) - 1;
                    int tileMaxX = (int)Math.Floor((objRect.X + objRect.Width) / (float)ts) + 1;
                    int tileMinY = (int)Math.Floor(objRect.Y / (float)ts) - 1;
                    int tileMaxY = (int)Math.Floor((objRect.Y + objRect.Height) / (float)ts) + 1;

                    for (int tx = Math.Max(tileMinX, minX - pad); tx <= Math.Min(tileMaxX, maxX + pad); tx++)
                    {
                        for (int ty = Math.Max(tileMinY, minY - pad); ty <= Math.Min(tileMaxY, maxY + pad); ty++)
                        {
                            if (blocked.Contains((tx, ty))) continue;
                            int cellHeight = GetHeightAt(tx, ty);
                            int cellBaseY = ty * ts - (cellHeight * ts / 4);
                            Rectangle tileRect = new Rectangle(tx * ts, cellBaseY, ts, ts);
                            if (Raylib.CheckCollisionRecs(tileRect, objRect))
                                blocked.Add((tx, ty));
                        }
                    }
                }
            }

            return blocked;
        }
    }
}
