// World.ActiveChunks.cs
// Fichier extrait de World.Core.cs — classe partielle World.
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {
		// Réutilisé à chaque mise à jour pour éviter une allocation de HashSet par frame.
		// UpdateActiveChunks est exécutée séquentiellement sur le thread du jeu.
		private static readonly HashSet<(int x, int y)> _shouldBeLoadedChunksScratch = new();

		public static void UpdateActiveChunks(Vector2 playerPos, float currentTime, List<Entity> globalEntities)
		{
			CheckDungeonBossDeaths();

			if (IsUnderwater)
			{
				UpdateUnderwaterChunks(playerPos, currentTime);
				return;
			}
			
			int ts = Program.TileSize;
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			
			// Obtenir la caméra actuelle
			Camera2D camera = Program.GetCurrentCamera();
			
			// Calculer les limites de l'écran dans le monde
			Vector2 topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera);
			Vector2 bottomRight = Raylib.GetScreenToWorld2D(new Vector2(sw, sh), camera);
			
			// Étendre légèrement pour éviter les clignotements
			int extraPixels = ts * EXTRA_CHUNKS_AROUND_PLAYER;
			topLeft.X -= extraPixels;
			topLeft.Y -= extraPixels;
			bottomRight.X += extraPixels;
			bottomRight.Y += extraPixels;
			
			// Convertir en coordonnées de chunks
			int minChunkX = (int)Math.Floor(topLeft.X / (CHUNK_SIZE * ts));
			int maxChunkX = (int)Math.Floor(bottomRight.X / (CHUNK_SIZE * ts));
			int minChunkY = (int)Math.Floor(topLeft.Y / (CHUNK_SIZE * ts));
			int maxChunkY = (int)Math.Floor(bottomRight.Y / (CHUNK_SIZE * ts));
			
			// Ajouter une marge de sécurité
			minChunkX -= EXTRA_CHUNKS_AROUND_PLAYER;
			minChunkY -= EXTRA_CHUNKS_AROUND_PLAYER;
			maxChunkX += EXTRA_CHUNKS_AROUND_PLAYER;
			maxChunkY += EXTRA_CHUNKS_AROUND_PLAYER;

			//  PERF (dézoom) : sans plafond, cette fenêtre grandit avec le dézoom (l'écran couvre
			// une zone du monde plus grande) et charge/simule bien plus de chunks — donc bien plus
			// de PNJ (villages entiers en plus) — que ce que DrawWorld dessine réellement (sa
			// fenêtre de tuiles est déjà plafonnée à MAX_RENDER_TILES_X/Y, indépendamment du
			// zoom). Résultat : à fort dézoom, on simulait ET dessinait des PNJ de villages
			// entiers en plus, ce qu'aucune réduction de détail par PNJ ne peut compenser
			// puisque c'est le NOMBRE de PNJ qui explose, pas leur coût individuel. On applique
			// donc le même principe de plafond ici, pour que la zone de chunks actifs reste
			// bornée quel que soit le niveau de zoom (le joueur dézoomé voit alors le monde déjà
			// exploré au loin, mais sans re-simuler/re-dessiner des villages hors de la fenêtre
			// réellement rendue).
			const int MAX_ACTIVE_CHUNKS_X = 12;
			const int MAX_ACTIVE_CHUNKS_Y = 9;
			int chunkRangeX = maxChunkX - minChunkX;
			if (chunkRangeX > MAX_ACTIVE_CHUNKS_X)
			{
				int excess = chunkRangeX - MAX_ACTIVE_CHUNKS_X;
				minChunkX += excess / 2;
				maxChunkX -= excess / 2;
			}
			int chunkRangeY = maxChunkY - minChunkY;
			if (chunkRangeY > MAX_ACTIVE_CHUNKS_Y)
			{
				int excess = chunkRangeY - MAX_ACTIVE_CHUNKS_Y;
				minChunkY += excess / 2;
				maxChunkY -= excess / 2;
			}
			
			var chunksDict = GetChunksDict();
			
			// Réutiliser la collection temporaire au lieu de l'allouer à chaque frame.
			var shouldBeLoaded = _shouldBeLoadedChunksScratch;
			shouldBeLoaded.Clear();
			
			for (int x = minChunkX; x <= maxChunkX; x++)
			{
				for (int y = minChunkY; y <= maxChunkY; y++)
				{
					shouldBeLoaded.Add((x, y));
					
					var chunkKey = (x, y);
					// Une seule recherche dans le dictionnaire : TryGetValue couvre les deux cas.
					if (chunksDict.TryGetValue(chunkKey, out var chunk))
					{
						chunk.LastAccessTime = currentTime;
					}
					else if (_pendingActiveChunkLoadKeys.Add(chunkKey))
					{
						_pendingActiveChunkLoads.Enqueue(chunkKey);
					}
				}
			}

			// Retirer les demandes devenues hors champ avant de consommer le budget de
			// chargement : après un déplacement rapide, une longue file de chunks périmés
			// pouvait retarder pendant plusieurs frames ceux qui sont réellement visibles.
			int queuedLoadCount = _pendingActiveChunkLoads.Count;
			_pendingActiveChunkLoadKeys.Clear();
			for (int i = 0; i < queuedLoadCount; i++)
			{
				var chunkKey = _pendingActiveChunkLoads.Dequeue();
				if (shouldBeLoaded.Contains(chunkKey) && !chunksDict.ContainsKey(chunkKey)
					&& _pendingActiveChunkLoadKeys.Add(chunkKey))
				{
					_pendingActiveChunkLoads.Enqueue(chunkKey);
				}
			}

			for (int i = 0; i < ACTIVE_CHUNKS_PER_FRAME && _pendingActiveChunkLoads.Count > 0; i++)
			{
				var chunkKey = _pendingActiveChunkLoads.Dequeue();
				_pendingActiveChunkLoadKeys.Remove(chunkKey);
				if (shouldBeLoaded.Contains(chunkKey) && !chunksDict.ContainsKey(chunkKey))
					LoadOrGenerateChunk(chunkKey.chunkX, chunkKey.chunkY, currentTime, globalEntities);
			}
			
			// Décharger les chunks qui ne sont pas dans la zone visible.
			// EXCEPTION : un donjon reste intégralement chargé pendant toute la visite. Sa zone
			// est petite (quelques centaines de chunks au pire) mais s'étend largement au-delà du
			// rayon de rendu autour du joueur (le boss et les salles éloignées sont volontairement
			// loin de l'entrée) — les décharger comme le monde normal supprimerait immédiatement
			// leurs entités (boss compris) de la liste globale dès la génération.
			if (_isInDungeon)
				return;

			PerfStats.StartSection(PerfStats.Section.ChunkUnloadWork);
			var toUnload = new List<(int x, int y)>();
			foreach (var chunk in chunksDict)
			{
				// CORRECTION : utiliser chunk.Key.Item1 et chunk.Key.Item2
				if (!shouldBeLoaded.Contains((chunk.Key.Item1, chunk.Key.Item2)))
				{
					//  CORRECTION DUPLICATION : chunk.Entities n'est JAMAIS tenu à jour quand
					// une entité se déplace d'un chunk à l'autre en cours de jeu (rien ne la
					// retire du chunk qu'elle quitte, rien ne l'ajoute à celui qu'elle rejoint).
					// Si on sauvegardait chunk.Entities tel quel, on pouvait donc écrire sur
					// disque une entité qui a déjà quitté ce chunk depuis longtemps (avec sa
					// position actuelle, potentiellement dans un tout autre chunk), tandis que
					// le chunk qu'elle occupe réellement ne la connaît pas. Au rechargement,
					// cette entité "fantôme" ressortait comme une copie en plus de l'originale.
					// On reconstruit donc la liste juste avant sauvegarde à partir de la
					// position RÉELLE des entités dans globalEntities : chaque entité n'est
					// ainsi jamais persistée que dans le fichier du chunk où elle se trouve
					// vraiment au moment de la sauvegarde.
					var entitiesReallyInThisChunk = new List<Entity>();
					for (int i = globalEntities.Count - 1; i >= 0; i--)
					{
						var entity = globalEntities[i];
						//  Les compagnons d'armure (crabe, etc.) ne sont jamais liés à un chunk : ils
						// sont entièrement dérivés de l'équipement porté et respawnés à chaque
						// chargement (voir Program.UpdateArmorCompanions / SaveChunkEntities). Les
						// laisser passer ici les faisait disparaître silencieusement dès que leur
						// chunk du moment se déchargeait (ex : juste après le chargement d'une
						// partie, avant que la caméra ne soit recentrée sur le joueur) : le nuage de
						// fumée d'apparition se déclenchait, mais le crabe était retiré juste après.
						if (entity.IsPetCompanion) continue;
						int entityChunkX = (int)Math.Floor(entity.WorldPos.X / (CHUNK_SIZE * Program.TileSize));
						int entityChunkY = (int)Math.Floor(entity.WorldPos.Y / (CHUNK_SIZE * Program.TileSize));
						if (entityChunkX == chunk.Key.Item1 && entityChunkY == chunk.Key.Item2)
						{
							entitiesReallyInThisChunk.Add(entity);
							globalEntities.RemoveAt(i);
						}
					}
					chunk.Value.Entities = entitiesReallyInThisChunk;

					// Sauvegarder avant de décharger
					if (!string.IsNullOrEmpty(CurrentSaveName))
					{
						if (_isInDungeon && _currentDungeonInstanceId >= 0)
							SaveSystem.SaveDungeonChunk(CurrentSaveName, _currentDungeonInstanceId, chunk.Key.Item1, chunk.Key.Item2, chunk.Value);
						else
							SaveSystem.SaveChunk(CurrentSaveName, chunk.Key.Item1, chunk.Key.Item2, chunk.Value, _isUnderground);
					}
					
					toUnload.Add((chunk.Key.Item1, chunk.Key.Item2));
				}
			}
			
			foreach (var key in toUnload)
			{
				chunksDict.Remove((key.x, key.y));
			}
			PerfStats.EndSection(PerfStats.Section.ChunkUnloadWork);
		}

		// ─────────────────────────────────────────────────────────────────────────
		//  DESSIN DU MONDE (UNIFIÉ) - VERSION OPTIMISÉE AVEC CACHE LOCAL
		// ─────────────────────────────────────────────────────────────────────────
		private static void DrawGrandfatherClockHands(float drawX, float drawY, int drawW, int drawH, float gameTime)
		{
			float dayProgress = (gameTime % 600f + 600f) % 600f / 600f;
			float gameHours = dayProgress * 24f;
			float hour = gameHours % 12f;
			float minute = (gameHours - MathF.Floor(gameHours)) * 60f;
			Vector2 center = new(drawX + drawW * 0.5f, drawY + drawH * 0.32f - 8f);
			float radius = drawW * 0.2f;

			float hourAngle = (hour / 12f) * MathF.Tau - MathF.PI / 2f;
			float minuteAngle = (minute / 60f) * MathF.Tau - MathF.PI / 2f;
			Vector2 hourEnd = center + new Vector2(MathF.Cos(hourAngle), MathF.Sin(hourAngle)) * radius * 0.62f;
			Vector2 minuteEnd = center + new Vector2(MathF.Cos(minuteAngle), MathF.Sin(minuteAngle)) * radius;

			Raylib.DrawLineEx(center, hourEnd, MathF.Max(1.5f, drawW * 0.045f), new Color(54, 28, 18, 255));
			Raylib.DrawLineEx(center, minuteEnd, MathF.Max(1f, drawW * 0.03f), new Color(54, 28, 18, 255));
			Raylib.DrawCircleV(center, MathF.Max(1.5f, drawW * 0.045f), new Color(54, 28, 18, 255));
		}

		private static int[,] _renderHeights = new int[0, 0];
		private static int[,] _renderGroundIds = new int[0, 0];
		private static int[,] _renderObjectIds = new int[0, 0];
		private static int[,] _renderOverlayIds = new int[0, 0];
		private static int[,] _renderBuildingIds = new int[0, 0];
		private static int[,] _renderRoofHeights = new int[0, 0];
		private static int[,] _renderDecorationIndex = new int[0, 0];
		private static int[,] _renderWallCoveringIds = new int[0, 0];
		private static bool[,] _renderIsWallToHide = new bool[0, 0];
		private static bool[,] _renderTileLit = new bool[0, 0];
		private enum BiomeBorderDirection { Right, Left, Bottom, Top }

    }
}
