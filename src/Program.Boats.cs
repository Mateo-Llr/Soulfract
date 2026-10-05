// Program.Boats.cs
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

		static bool _isAdjustingSail = false;

		static Boat? _adjustedBoat = null;

		static float _currentSailAngle = 0f;

		static Vector2 _sailAdjustStartMouseScreen = Vector2.Zero;

		static float _initialSailAngle = 0f;

		//  Dernière étape (rapide) une fois tous les chunks chargés : synchronisation de
        // l'humidité des terres et restauration des bateaux.
        static void FinishLoadGame()
        {
            World.SyncFarmlandMoistureWithGroundOverrides();

            //  CORRECTIF : World.Houses et World.HouseInteriorSpots ne sont PAS sauvegardés
            // (ce sont des données dérivées, reconstruites en scannant les toits des chunks
            // chargés — voir World.RefreshAllHouses). Contrairement à la première session, où
            // ces dictionnaires se peuplent au fil de la construction du village, un
            // rechargement de sauvegarde ne les reconstruisait jamais : ils restaient vides
            // toute la session. Les PNJ rechargés gardent bien leur HomeBuildingId/HomePosition
            // (ça, c'est sauvegardé), mais toute recherche World.Houses.TryGetValue(...) ou
            // World.HouseInteriorSpots.TryGetValue(...) échouait silencieusement — cassant le
            // dépôt/retrait au coffre, l'approche du coffre, les sièges intérieurs et le repli
            // vers la porte dans GoHome. On reconstruit donc les maisons ici, maintenant que
            // tous les chunks du chargement initial sont en mémoire (donc que les toits sont
            // scannables), et avant que les PNJ ne reprennent leur mise à jour normale.
            World.RefreshAllHouses();

            //  Le crabe compagnon (set d'armure "Crabe") est réinvoqué ici plutôt que dans
            // BeginLoadGame : tous les chunks autour du joueur sont maintenant chargés, donc
            // SpawnCrabCompanion() peut s'accrocher à un chunk réel (voir World.GetChunkAt) au
            // lieu d'apparaître sur un terrain pas encore généré.
            UpdateArmorCompanions(0f);

			// Après avoir recréé toutes les entités et chunks, résoudre les références
			// OwnedMount -> remonter les montures PNJ si IsRidingMount était vrai dans la
			// sauvegarde (SaveSystem.RestoreEntityFromSave remplit OwnedMountNetId/IsRidingMount).
			SaveSystem.ResolveOwnedMountReferences(entities);

            if (_loadingSaveData != null)
            {
				RestoreWagonLinks(_loadingSaveData.WagonLinks);
                foreach (var boatSave in _loadingSaveData.Boats)
                {
                    var boat = new Boat(new Vector2(boatSave.PosX, boatSave.PosY));
                    boat.Tiles.Clear(); // retire la tuile (0,0) par défaut : on va restaurer la vraie grille
                    boat.Velocity = new Vector2(boatSave.VelX, boatSave.VelY);
                    boat.Angle = boatSave.Angle;
                    boat.RowingCooldown = boatSave.RowingCooldown;
                    boat.GroundTileId = boatSave.GroundTileId != 0 ? boatSave.GroundTileId : 500;
                    boat.SailAngle = boatSave.SailAngle;
                    boat.IsSailAdjusted = boatSave.IsSailAdjusted;

                    // Grille des tuiles du bateau — AVANT, seule la tuile (0,0) créée par le
                    // constructeur subsistait après un rechargement, tout bateau à plusieurs
                    // tuiles retombait donc à 1x1 en quittant/rechargeant la partie.
                    for (int i = 0; i < boatSave.RelX.Count && i < boatSave.RelY.Count; i++)
                    {
                        var key = (boatSave.RelX[i], boatSave.RelY[i]);
                        boat.Tiles[key] = new BoatTile { GroundId = boat.GroundTileId };
                    }
                    // Sécurité : si la sauvegarde ne contenait aucune tuile (ancien format,
                    // ou données corrompues), on garde au moins une tuile de départ pour
                    // éviter un bateau totalement vide et injouable.
                    if (boat.Tiles.Count == 0)
                        boat.Tiles[(0, 0)] = new BoatTile { GroundId = boat.GroundTileId };

                    for (int i = 0; i < boatSave.PlacedRelX.Count && i < boatSave.PlacedObjectIds.Count; i++)
                    {
                        boat.SetObjectAt(boatSave.PlacedRelX[i], boatSave.PlacedRelY[i], boatSave.PlacedObjectIds[i]);
                    }

                    // Conteneurs — auparavant jamais restaurés, tout le contenu des coffres
                    // de bateau était perdu au rechargement.
                    for (int i = 0; i < boatSave.ContainerRelX.Count && i < boatSave.ContainerRelY.Count && i < boatSave.ContainerData.Count; i++)
                    {
                        var key = (boatSave.ContainerRelX[i], boatSave.ContainerRelY[i]);
                        int columns = 8;
                        int placedId = boat.GetObjectIdAt(key.Item1, key.Item2);
                        if (placedId != 0)
                        {
                            var tileData = WorldTileRegistry.GetTile(placedId);
                            if (tileData?.IsContainer == true)
                                columns = tileData.ContainerColumns > 0 ? tileData.ContainerColumns : 8;
                        }
                        boat.Containers[key] = World.BuildContainerFromSave(boatSave.ContainerData[i], columns);
                    }

                    // Porte-armures — auparavant jamais restaurés non plus.
                    for (int i = 0; i < boatSave.ArmorStandRelX.Count && i < boatSave.ArmorStandRelY.Count && i < boatSave.ArmorStandData.Count; i++)
                    {
                        var key = (boatSave.ArmorStandRelX[i], boatSave.ArmorStandRelY[i]);
                        boat.ArmorStands[key] = World.BuildArmorStandFromSave(boatSave.ArmorStandData[i]);
                    }

                    if (Guid.TryParse(boatSave.Id, out var guid))
                        boat.Id = guid;

                    World.AddBoat(boat);
                }
            }

            _pendingChunkLoads = null;
            _loadingSaveData = null;
        }

		private static bool TryPickupFurniture(Vector2 playerPos, Camera2D camera, Rectangle? viewport = null, int playerIndex = 0)
		{
			Vector2 mouseWorld = viewport.HasValue
				? GetViewportMouseWorldPosition(camera, viewport.Value)
				: Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			int ts = Program.TileSize;
			
			Boat? playerBoat = World.GetBoatAtPosition(playerPos, ts);
			
			// ========== PRIORITÉ 1 : MEUBLE SUR UN BATEAU ==========
			if (playerBoat != null)
			{
				Vector2 localMouse = mouseWorld - playerBoat.Position;
				int relX = (int)Math.Floor((localMouse.X + ts/2f) / ts);
				int relY = (int)Math.Floor((localMouse.Y + ts/2f) / ts);
				
				if (playerBoat.Tiles.ContainsKey((relX, relY)))
				{
					int boatObjectId = playerBoat.GetObjectIdAt(relX, relY);
					if (boatObjectId == 0) return false;
					
					var tileData = WorldTileRegistry.GetTile(boatObjectId);
					if (tileData == null || !tileData.IsFurniture) return false;
					
					// Vérifier la distance
					Vector2 tileWorldPos = playerBoat.GetTileWorldPos(relX, relY);
					float dist = Vector2.Distance(playerPos, tileWorldPos);
					if (dist > ts * 3f) return false;
					
					// Récupérer l'item ID correspondant
					int itemId = GetItemIdFromPlaceableId(boatObjectId);
					if (itemId == 0) return false;
					
					// Sauvegarder les données du conteneur si c'en est un
					ContainerInventoryData? containerData = null;
					if (tileData.IsContainer == true && playerBoat.Containers.TryGetValue((relX, relY), out var boatContainer))
					{
						containerData = boatContainer;
						playerBoat.Containers.Remove((relX, relY));
					}
					
					// Sauvegarder les données du porte-armure si c'en est un
					ArmorStandData? armorStandData = null;
					if (tileData.IsArmorStand == true && playerBoat.ArmorStands.TryGetValue((relX, relY), out var boatStand))
					{
						armorStandData = boatStand;
						playerBoat.ArmorStands.Remove((relX, relY));
					}
					
					// Supprimer l'objet du bateau
					playerBoat.SetObjectAt(relX, relY, 0);
					
					_carriedFurniture = new CarriedFurniture
					{
						ItemId = itemId,
						PlaceableId = boatObjectId,
						ItemName = tileData.Name,
						ContainerData = containerData,
						ArmorStandData = armorStandData
					};
					return true;
				}
			}
			
			// ========== PRIORITÉ 2 : MEUBLE SUR LE TERRAIN NORMAL ==========
			(int tileX, int tileY, float _, int objectId) = FindTileUnderMouse(mouseWorld, ts);
			
			if (tileX == -1 || tileY == -1) return false;
			
			var normalTileData = WorldTileRegistry.GetTile(objectId);
			if (normalTileData == null || !normalTileData.IsFurniture) return false;
			
			float playerDist = Vector2.Distance(playerPos, new Vector2(tileX * ts + ts/2f, tileY * ts + ts/2f));
			if (playerDist > ts * 2f) return false;
			
			int normalItemId = GetItemIdFromPlaceableId(objectId);
			if (normalItemId == 0) return false;
			
			var chunk = World.GetChunkAt(tileX, tileY);
			ContainerInventoryData? normalContainerData = null;
			ArmorStandData? normalArmorStandData = null;
			string? pottedFlowerItemId = objectId == 86
				? World.GetTileMeta(tileX, tileY, "potted_flower")
				: null;
			
			if (normalTileData.IsContainer == true && chunk != null)
			{
				normalContainerData = chunk.GetContainerAt(tileX, tileY);
				if (normalContainerData != null)
				{
					chunk.SetContainerAt(tileX, tileY, null);
				}
			}
			else if (normalTileData.IsArmorStand == true && chunk != null)
			{
				normalArmorStandData = chunk.GetArmorStandAt(tileX, tileY);
				if (normalArmorStandData != null)
				{
					chunk.SetArmorStandAt(tileX, tileY, null);
				}
			}
			
			World.RemovePlacedObject(tileX, tileY);
			if (pottedFlowerItemId != null)
				World.RemoveTileMeta(tileX, tileY, "potted_flower");
			
			_carriedFurniture = new CarriedFurniture
			{
				ItemId = normalItemId,
				PlaceableId = objectId,
				ItemName = normalTileData.Name,
				PottedFlowerItemId = pottedFlowerItemId,
				ContainerData = normalContainerData,
				ArmorStandData = normalArmorStandData
			};
			return true;
		}

		private static (Boat? boat, int relX, int relY, int objectId) GetInteractiveBoatPartUnderMouse(Camera2D camera, int targetId)
		{
			int ts = Program.TileSize;
			Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
			foreach (var boat in World.GetAllBoats().Values)
			{
				foreach (var (relX, relY) in boat.Tiles.Keys)
				{
					int objId = boat.GetObjectIdAt(relX, relY);
					if (objId == targetId)
					{
						Vector2 tileWorldPos = boat.GetTileWorldPos(relX, relY);
						int height = World.GetHeightAt((int)(tileWorldPos.X / ts), (int)(tileWorldPos.Y / ts));
						float yOffset = -height * ts / 4;
						float drawX = tileWorldPos.X - ts/2f;
						float drawY = tileWorldPos.Y - ts/2f + yOffset;
						var tileData = WorldTileRegistry.GetTile(targetId);
						if (tileData?.Size != null)
						{
							drawY -= ts * (tileData.Size.Height - 1);
						}
						Rectangle rect = new Rectangle(drawX, drawY, ts, ts * (tileData?.Size?.Height ?? 1));
						if (Raylib.CheckCollisionPointRec(mouseWorld, rect))
						{
							return (boat, relX, relY, objId);
						}
					}
				}
			}
			return (null, -1, -1, 0);
		}

		// (Ancienne fonction GetWheelUnderMouse supprimée : c'était un doublon exact,
		// jamais appelé, de GetInteractiveBoatPartUnderMouse(camera, 109) déjà utilisé
		// ailleurs — même ID de gouvernail codé en double, seule la constante
		// Boat.WheelObjectId doit désormais faire foi.)
		private static void DrawSailAdjustmentIndicator(Camera2D camera)
		{
			if (!_isAdjustingSail || _adjustedBoat == null) return;
			
			// Trouver le gouvernail sur le bateau (pour afficher l'indicateur à cet endroit)
			int foundRelX = -1, foundRelY = -1;
			foreach (var (relX, relY) in _adjustedBoat.Tiles.Keys)
			{
				if (_adjustedBoat.GetObjectIdAt(relX, relY) == Boat.WheelObjectId)
				{
					foundRelX = relX;
					foundRelY = relY;
					break;
				}
			}
			
			if (foundRelX == -1) return;
			
			Vector2 wheelWorldPos = _adjustedBoat.GetTileWorldPos(foundRelX, foundRelY);
			int h = World.GetHeightAt((int)(wheelWorldPos.X / Program.TileSize), (int)(wheelWorldPos.Y / Program.TileSize));
			float yOff = -h * Program.TileSize / 4;
			Vector2 screenPos = Raylib.GetWorldToScreen2D(new Vector2(wheelWorldPos.X, wheelWorldPos.Y + yOff), camera);
			float radius = 60f;
			
			Raylib.DrawCircleLines((int)screenPos.X, (int)screenPos.Y, radius, Color.Yellow);
			
			float rad = _currentSailAngle * MathF.PI / 180f;
			Vector2 arrowDir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
			Vector2 arrowTip = screenPos + arrowDir * radius;
			Raylib.DrawLineEx(screenPos, arrowTip, 4, Color.Red);
			
			// Ajouter une petite icône de gouvernail au centre
			Raylib.DrawCircle((int)screenPos.X, (int)screenPos.Y, 8, new Color(200, 150, 50, 200));
			
			FontManager.DrawText($"{_currentSailAngle:F0}°", (int)screenPos.X - 20, (int)(screenPos.Y - radius - 20), 14, Color.White);
		}

		private static bool IsAdjacent(Boat boat, int relX, int relY)
		{
			foreach (var (dx, dy) in new[] { (1,0), (-1,0), (0,1), (0,-1) })
			{
				if (boat.Tiles.ContainsKey((relX + dx, relY + dy)))
					return true;
			}
			return false;
		}
    }
}
