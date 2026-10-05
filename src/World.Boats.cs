// World.Boats.cs
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {

		private static Dictionary<Guid, Boat> _boats = new();

		public static Dictionary<Guid, Boat> GetAllBoats() => _boats;

		public static void ClearBoats() => _boats.Clear();

		public static void AddBoat(Boat boat) => _boats[boat.Id] = boat;

		// Ajouter une tuile à un bateau (création ou extension)
		public static bool AddBoatTile(int worldX, int worldY, out Boat? boat)
		{
			boat = null;
			int ts = Program.TileSize;
			Vector2 targetWorldPos = new Vector2(worldX * ts + ts/2f, worldY * ts + ts/2f);

			// 1. Le joueur est déjà sur un bateau existant ?
			Boat? playerBoat = GetBoatAtPosition(Program.GetPlayerPosition(), ts);
			if (playerBoat != null)
			{
				// Déterminer la position relative
				Vector2 local = targetWorldPos - playerBoat.Position;
				int relX = (int)Math.Round(local.X / ts);
				int relY = (int)Math.Round(local.Y / ts);

				// Vérifier que la case est libre et adjacente
				if (playerBoat.Tiles.ContainsKey((relX, relY)))
					return false;
				bool adjacent = false;
				foreach (var (dx, dy) in new[] { (1,0), (-1,0), (0,1), (0,-1) })
				{
					if (playerBoat.Tiles.ContainsKey((relX + dx, relY + dy)))
					{
						adjacent = true;
						break;
					}
				}
				if (!adjacent) return false;

				// Vérifier que la position monde est de l’eau (sol navigable)
				// (10 = eau peu profonde, 11 = eau profonde — cf. CanPlaceBoatTile)
				int groundId = GetGroundTileIdAt(worldX, worldY);
				if (groundId != 10 && groundId != 11) return false;

				playerBoat.AddTile(relX, relY);
				boat = playerBoat;
				return true;
			}

			// 2. Sinon, création d’un nouveau bateau
			if (!CanPlaceBoatTile(worldX, worldY)) return false;
			Vector2 center = new Vector2(worldX * ts + ts/2f, worldY * ts + ts/2f);
			var newBoat = new Boat(center);
			_boats[newBoat.Id] = newBoat;
			boat = newBoat;
			return true;
		}

		// ─── Reconstruction Container/ArmorStand depuis une sauvegarde ─────────────
		// Logique identique à celle utilisée pour les conteneurs/porte-armures des
		// chunks (voir LoadChunkFromSave) — factorisée ici pour être réutilisable par
		// le chargement des bateaux (voir Program.cs), qui ne la faisait pas du tout
		// auparavant (les conteneurs/porte-armures de bateau étaient perdus au reload).
		public static ContainerInventoryData BuildContainerFromSave(ContainerInventorySave save, int columns)
		{
			int slotCount = save.Slots.Count;
			var containerData = new ContainerInventoryData(slotCount, columns);

			for (int i = 0; i < save.Slots.Count && i < containerData.Slots.Count; i++)
			{
				var slotSave = save.Slots[i];
				var slot = containerData.Slots[i];

				if (!slotSave.IsEmpty && !string.IsNullOrEmpty(slotSave.ItemName))
				{
					var itemDef = GameData.ItemDatabase.Values.FirstOrDefault(d => d.Name == slotSave.ItemName);
					if (itemDef.ID != 0)
					{
var restoredItem = new Item(itemDef.Name, slotSave.Count, itemDef.Color, itemDef.Icon);

						if (slotSave.CustomColors != null && slotSave.CustomColors.Count > 0)
						{
							var colors = new List<Color?>();
							foreach (var colorSave in slotSave.CustomColors)
							{
								if (colorSave != null)
									colors.Add(new Color(colorSave.R, colorSave.G, colorSave.B, colorSave.A));
								else
									colors.Add(null);
							}
							restoredItem.CustomColors = colors;
						}

						slot.Item = restoredItem;
						slot.Count = slotSave.Count;
					}
				}
			}

			return containerData;
		}

		// Mettre à jour tous les bateaux
		public static void UpdateBoats(float dt, HashSet<string> destroyedObjects, List<Entity> entities, ref Vector2 playerPos)
		{
			foreach (var boat in _boats.Values)
			{
				boat.PreviousPosition = boat.Position;
				
				// Réduction du cooldown de rame
				if (boat.RowingCooldown > 0)
					boat.RowingCooldown -= dt;
				
				// ========== NOUVEAU : Application de la force continue des voiles ==========
				Vector2 sailThrust = boat.CalculateTotalThrust();
				boat.Velocity += sailThrust * dt;
				
				// ========== Frottement de l'eau (toujours présent) ==========
				// Coefficient exprimé "par seconde" puis élevé à dt, pour que le freinage
				// soit indépendant du framerate (avant : *0.98f par frame → un bateau
				// ralentissait bien plus vite à 30 FPS qu'à 144 FPS).
				const float waterDragPerSecond = 0.298f; // équivalent exact de l'ancien 0.98f/frame à 60 FPS
				float waterDrag = MathF.Pow(waterDragPerSecond, dt);
				boat.Velocity *= waterDrag;
				
				// Si la vitesse est très faible, l'annuler pour éviter les micro-déplacements
				if (boat.Velocity.LengthSquared() < 1f)
					boat.Velocity = Vector2.Zero;
				
				// Déplacement du bateau
				if (boat.Velocity.LengthSquared() > 0.01f)
				{
					Vector2 newPos = boat.Position + boat.Velocity * dt;
					if (!IsBoatColliding(newPos, boat, Program.TileSize))
					{
						boat.Position = newPos;
					}
					else
					{
						// Collision : annuler la vélocité dans la direction du mur
						boat.Velocity = Vector2.Zero;
					}
				}
				
				// Déplacement des entités et du joueur sur le bateau
				Vector2 delta = boat.Position - boat.PreviousPosition;
				if (delta.LengthSquared() > 0.01f)
				{
					foreach (var entity in entities)
					{
						if (entity.IsAlive && boat.ContainsPosition(entity.WorldPos, Program.TileSize))
						{
							entity.WorldPos += delta;
						}
					}
					if (boat.ContainsPosition(playerPos, Program.TileSize))
					{
						playerPos += delta;
					}
				}
			}
		}

		private static bool IsBoatColliding(Vector2 newPos, Boat boat, int tileSize)
		{
			// IMPORTANT : toutes les positions de tuiles ci-dessous doivent être calculées
			// à partir de newPos (la position CANDIDATE après déplacement), pas de
			// boat.Position (la position actuelle) — sinon on ne teste jamais l'endroit
			// où le bateau est sur le point d'aller, et la collision ne bloque jamais rien.
			foreach (var (relX, relY) in boat.Tiles.Keys)
			{
				Vector2 worldPos = newPos + new Vector2(relX * tileSize, relY * tileSize);
				int tx = (int)Math.Floor(worldPos.X / tileSize);
				int ty = (int)Math.Floor(worldPos.Y / tileSize);
				int groundId = GetGroundTileIdAt(tx, ty);

				// Un bateau doit rester sur l'eau : on bloque dès que la case cible n'est
				// pas de l'eau (10/11), et pas seulement quand elle est "non praticable"
				// (Walkable), sans quoi le bateau pouvait librement remonter sur la plage
				// ou l'herbe puisque ces sols sont, eux aussi, Walkable == true.
				bool isWater = (groundId == 10 || groundId == 11);
				if (!isWater)
					return true;
			}
			
			// Vérification des collisions avec d'autres bateaux (à la position candidate)
			foreach (var otherBoat in _boats.Values)
			{
				if (otherBoat.Id == boat.Id) continue; // Ignorer le même bateau
				
				foreach (var (relX, relY) in boat.Tiles.Keys)
				{
					Vector2 worldPos = newPos + new Vector2(relX * tileSize, relY * tileSize);
					
					// Vérifier si cette position chevauche une tuile de l'autre bateau
					foreach (var (otherRelX, otherRelY) in otherBoat.Tiles.Keys)
					{
						Vector2 otherWorldPos = otherBoat.GetTileWorldPos(otherRelX, otherRelY);
						if (Vector2.Distance(worldPos, otherWorldPos) < tileSize * 0.9f)
							return true;
					}
				}
			}
			
			return false;
		}

		public static Boat? GetBoatAtWorldPos(Vector2 worldPos)
		{
			foreach (var boat in _boats.Values)
			{
				foreach (var (relX, relY) in boat.Tiles.Keys)
				{
					Vector2 tileWorldPos = boat.GetTileWorldPos(relX, relY);
					int ts = Program.TileSize;
					Rectangle tileRect = new Rectangle(
						tileWorldPos.X - ts/2f, 
						tileWorldPos.Y - ts/2f, 
						ts, ts
					);
					if (Raylib.CheckCollisionPointRec(worldPos, tileRect))
					{
						return boat;
					}
				}
			}
			return null;
		}

		public static bool RemoveBoatTile(Vector2 worldPos, out Boat? boat)
		{
			boat = GetBoatAtWorldPos(worldPos);
			if (boat == null) return false;
			
			int ts = Program.TileSize;
			
			// Trouver la tuile relative la plus proche
			foreach (var (relX, relY) in boat.Tiles.Keys.ToList())
			{
				Vector2 tileWorldPos = boat.GetTileWorldPos(relX, relY);
				Rectangle tileRect = new Rectangle(
					tileWorldPos.X - ts/2f, 
					tileWorldPos.Y - ts/2f, 
					ts, ts
				);
				
				if (Raylib.CheckCollisionPointRec(worldPos, tileRect))
				{
					// Vérifier qu'on ne casse pas la dernière tuile
					if (boat.Tiles.Count <= 1)
					{
						// Détruire complètement le bateau
						_boats.Remove(boat.Id);
						return true;
					}
					
					// Retirer la tuile
					boat.Tiles.Remove((relX, relY));
					
					// Vérifier si le bateau est toujours connecté
					if (!IsBoatConnected(boat))
					{
						// Si le bateau est fragmenté, on garde la plus grande partie
						SplitBoat(boat);
					}
					
					return true;
				}
			}
			
			return false;
		}

		// Vérifie si toutes les tuiles du bateau sont connectées
		private static bool IsBoatConnected(Boat boat)
		{
			if (boat.Tiles.Count <= 1) return true;
			
			var visited = new HashSet<(int x, int y)>();
			var queue = new Queue<(int x, int y)>();
			
			// Commencer par la première tuile
			var first = boat.Tiles.Keys.First();
			queue.Enqueue(first);
			visited.Add(first);
			
			while (queue.Count > 0)
			{
				var (x, y) = queue.Dequeue();
				foreach (var (dx, dy) in new[] { (1,0), (-1,0), (0,1), (0,-1) })
				{
					var neighbor = (x + dx, y + dy);
					if (boat.Tiles.ContainsKey(neighbor) && !visited.Contains(neighbor))
					{
						visited.Add(neighbor);
						queue.Enqueue(neighbor);
					}
				}
			}
			
			return visited.Count == boat.Tiles.Count;
		}

		// Sépare un bateau en plusieurs morceaux connectés.
		// Corrigé : gère un nombre quelconque de fragments (pas seulement 2 — avant,
		// à partir du 3e fragment, tous les morceaux restants étaient regroupés dans un
		// seul "bateau" avec des tuiles non connexes) et transfère bien PlacedObjects
		// (voiles, gouvernails, meubles...) vers les nouveaux bateaux, au lieu de les
		// laisser orphelins sur l'ancien bateau ou de les perdre silencieusement.
		private static void SplitBoat(Boat originalBoat)
		{
			var remainingTiles = new Dictionary<(int x, int y), BoatTile>(originalBoat.Tiles);
			var processedTiles = new HashSet<(int x, int y)>();
			var allGroups = new List<HashSet<(int x, int y)>>();

			// 1. Identifier tous les groupes connectés
			foreach (var startTile in remainingTiles.Keys)
			{
				if (processedTiles.Contains(startTile)) continue;

				var group = new HashSet<(int x, int y)>();
				var queue = new Queue<(int x, int y)>();
				queue.Enqueue(startTile);
				group.Add(startTile);

				while (queue.Count > 0)
				{
					var (x, y) = queue.Dequeue();
					foreach (var (dx, dy) in new[] { (1,0), (-1,0), (0,1), (0,-1) })
					{
						var neighbor = (x + dx, y + dy);
						if (remainingTiles.ContainsKey(neighbor) && !group.Contains(neighbor))
						{
							group.Add(neighbor);
							queue.Enqueue(neighbor);
						}
					}
				}

				processedTiles.UnionWith(group);
				allGroups.Add(group);
			}

			if (allGroups.Count <= 1) return; // toujours connecté, rien à faire

			// 2. Garder le plus grand groupe sur le bateau original, créer un nouveau
			//    bateau pour chacun des autres groupes.
			var largestGroup = allGroups.OrderByDescending(g => g.Count).First();

			foreach (var group in allGroups)
			{
				if (group == largestGroup) continue;

				float avgX = 0, avgY = 0;
				foreach (var (rx, ry) in group)
				{
					Vector2 worldPos = originalBoat.GetTileWorldPos(rx, ry);
					avgX += worldPos.X;
					avgY += worldPos.Y;
				}
				avgX /= group.Count;
				avgY /= group.Count;

				var newBoat = new Boat(new Vector2(avgX, avgY));
				newBoat.Tiles.Clear(); // retire la tuile (0,0) par défaut du constructeur
				newBoat.GroundTileId = originalBoat.GroundTileId;

				foreach (var (rx, ry) in group)
				{
					// Coordonnées relatives au NOUVEAU centre, pas à l'ancien, pour que
					// GetTileWorldPos reste correct une fois le bateau recentré.
					Vector2 tileWorldPos = originalBoat.GetTileWorldPos(rx, ry);
					int newRelX = (int)MathF.Round((tileWorldPos.X - avgX) / Program.TileSize);
					int newRelY = (int)MathF.Round((tileWorldPos.Y - avgY) / Program.TileSize);
					var newKey = (newRelX, newRelY);

					newBoat.Tiles[newKey] = originalBoat.Tiles[(rx, ry)];

					if (originalBoat.Containers.TryGetValue((rx, ry), out var container))
						newBoat.Containers[newKey] = container;

					if (originalBoat.ArmorStands.TryGetValue((rx, ry), out var armorStand))
						newBoat.ArmorStands[newKey] = armorStand;

					// AJOUT : transférer les objets posés (voiles, gouvernails, meubles...)
					// — auparavant totalement ignorés par SplitBoat.
					int placedId = originalBoat.GetObjectIdAt(rx, ry);
					if (placedId != 0)
						newBoat.SetObjectAt(newRelX, newRelY, placedId);
				}

				_boats[newBoat.Id] = newBoat;

				// Retirer ces tuiles (et leurs données associées) du bateau original
				foreach (var (rx, ry) in group)
				{
					originalBoat.Tiles.Remove((rx, ry));
					originalBoat.Containers.Remove((rx, ry));
					originalBoat.ArmorStands.Remove((rx, ry));
					originalBoat.SetObjectAt(rx, ry, 0); // nettoie PlacedObjects
				}
			}
		}

		private static void ClearCurrentChunks()
        {
			_pendingActiveChunkLoads.Clear();
			_pendingActiveChunkLoadKeys.Clear();
			ClearTerrainChunkRenderCaches();
            if (_isUnderground)
            {
                _chunks.Clear();
                _heightCache.Clear();
				_climateCache.Clear();
            }
            else
            {
                _caveChunks.Clear();
                _caveHeightCache.Clear();
            }
			_boats.Clear();
        }

		public static bool CanPlaceBoatTile(int x, int y)
		{
			int groundId = GetGroundTileIdAt(x, y);
			bool isWater = (groundId == 10 || groundId == 11);
			int ts = Program.TileSize;
			Vector2 targetPos = new Vector2(x * ts + ts / 2f, y * ts + ts / 2f);
			bool noOtherBoat = !_boats.Values.Any(b =>
				b.Tiles.Keys.Any(rel =>
					Vector2.Distance(b.GetTileWorldPos(rel.x, rel.y), targetPos) < ts * 0.5f
				)
			);
			return isWater && noOtherBoat;
		}

		public static Boat? GetBoatAtPosition(Vector2 worldPos, int tileSize)
		{
			foreach (var boat in _boats.Values)
				if (boat.ContainsPosition(worldPos, tileSize))
					return boat;
			return null;
		}

		public static bool IsCollidingEntity(Vector2 groundPos, HashSet<string> destroyed, string species)
		{
			Vector2 dim = GetEntityDimensions(species);
			int tileX = (int)Math.Floor(groundPos.X / Program.TileSize);
			int tileY = (int)Math.Floor(groundPos.Y / Program.TileSize);
			int height = GetHeightAt(tileX, tileY);
			float yOffset = -height * Program.TileSize / 4;
			float entityBaseY = groundPos.Y + yOffset + Program.FeetOffsetY;
			Rectangle entityRect = new Rectangle(groundPos.X - dim.X / 2f, entityBaseY - dim.Y, dim.X, dim.Y);
			return CheckCollision(groundPos, entityRect, destroyed, Program.TileSize); // CheckCollision inclut déjà les bateaux maintenant
			
			// Collisions avec les voitures
			foreach (var car in Program.Cars)
			{
				if (car == null) continue;
				Vector2[] carCorners = car.GetCorners(car.Position, car.Angle);
				if (Car.IntersectOrientedRectangleWithAABB(carCorners, entityRect))
					return true;
			}

			return false;
			
		}

		private static bool CheckCollision(Vector2 groundPos, Rectangle playerRect, HashSet<string> destroyed, int ts)
		{
			int playerTileX = (int)Math.Floor(groundPos.X / ts);
			int playerTileY = (int)Math.Floor(groundPos.Y / ts);
			int playerHeight = GetHeightAt(playerTileX, playerTileY);
			for (int dx = -3; dx <= 3; dx++)
			for (int dy = -3; dy <= 3; dy++)
			{
				int wX = playerTileX + dx, wY = playerTileY + dy;
				string key = $"{wX}_{wY}";
				if (destroyed.Contains(key)) continue;
				//  Trou physique à la tuile "porte" : voir le commentaire sur _doorTiles.
				if (IsDoorTile(wX, wY)) continue;
				int tileId = GetTileIdAt(wX, wY);
				var tileData = WorldTileRegistry.GetTile(tileId);
				if (tileData == null || tileData.Walkable) continue;
				int tileHeight = GetHeightAt(wX, wY);
				if (tileHeight < playerHeight - 1) continue;
				
				var (x, y, w, h) = GetCollisionRect(wX, wY, tileData, ts);
				// (L'ancrage bas-gauche est désormais géré directement dans GetCollisionRect,
				// plus besoin de recorriger y ici pour les objets sur plusieurs tuiles.)
				
				Rectangle objRect = new Rectangle(x, y, w, h);
				if (Raylib.CheckCollisionRecs(playerRect, objRect))
					return true;
			}
			
			//  AJOUTER ICI : Vérification des collisions avec les objets sur les bateaux
			foreach (var boat in _boats.Values)
			{
				foreach (var (relX, relY) in boat.Tiles.Keys)
				{
					int boatObjectId = boat.GetObjectIdAt(relX, relY);
					if (boatObjectId == 0) continue;
					
					var boatTileData = WorldTileRegistry.GetTile(boatObjectId);
					if (boatTileData == null || boatTileData.Walkable) continue;
					
					Vector2 tileWorldPos = boat.GetTileWorldPos(relX, relY);
					int tileX = (int)(tileWorldPos.X / ts);
					int tileY = (int)(tileWorldPos.Y / ts);
					
					// Vérifier si la tuile est proche du joueur
					if (Math.Abs(tileX - playerTileX) > 3 || Math.Abs(tileY - playerTileY) > 3)
						continue;
					
					string key = $"{tileX}_{tileY}";
					if (destroyed.Contains(key)) continue;
					
					int height = GetHeightAt(tileX, tileY);
					float yOffset = -height * ts / 4;
					
					float drawX = tileWorldPos.X - ts/2f;
					float drawY = tileWorldPos.Y - ts/2f + yOffset;
					int drawW = ts, drawH = ts;
					int offX = 0, offY = 0;
					
					if (boatTileData.Size != null)
					{
						drawW = ts * boatTileData.Size.Width;
						drawH = ts * boatTileData.Size.Height;
						if (boatTileData.DrawOffset != null)
						{
							offX = boatTileData.DrawOffset.X;
							offY = boatTileData.DrawOffset.Y;
						}
						drawY = drawY - drawH + ts;
					}
					
					// Utiliser le même calcul de hitbox que GetCollisionRect
					int hitboxX = (int)(drawX + offX);
					int hitboxY = (int)(drawY + offY);
					int hitboxW = drawW;
					int hitboxH = drawH;
					
					// Ajuster pour les objets à taille personnalisée : même convention bas-gauche
					// que GetCollisionRect (ancre = coin bas-gauche de la tuile d'ancrage).
					if (boatTileData.CollisionSize != null)
					{
						hitboxW = ts * boatTileData.CollisionSize.Width;
						hitboxH = ts * boatTileData.CollisionSize.Height;
						int boatBaseY = tileY * ts - (height * ts / 4);
						hitboxX = (int)(drawX + (boatTileData.CollisionOffset?.X ?? 0));
						hitboxY = (int)(boatBaseY + ts - hitboxH - (boatTileData.CollisionOffset?.Y ?? 0));
					}
					
					Rectangle boatObjRect = new Rectangle(hitboxX, hitboxY, hitboxW, hitboxH);
					if (Raylib.CheckCollisionRecs(playerRect, boatObjRect))
						return true;
				}
			}
			
			return false;
		}

		private static bool IsBoatNeighborSameType(int relX, int relY, int targetId, Boat boat)
		{
			if (!boat.Tiles.ContainsKey((relX, relY))) return false;
			int neighborObjId = boat.GetObjectIdAt(relX, relY);
			return neighborObjId != 0 && neighborObjId == targetId;
		}
    }
}
