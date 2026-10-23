// DebugRendering.cs
// Overlay de debug (hitboxes, IA, zones...).
// Fichier extrait de DrawWorld.cs (classe partielle World).
#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public static partial class World
    {
        private static partial class DrawPhases
        {
			/// <summary>Hitboxes et diagnostics debug (F3).</summary>
			internal static void DrawDebugOverlay(DrawWorldContext ctx)
			{
				Vector2 playerPos = ctx.playerPos;
				HashSet<string> destroyed = ctx.destroyed;
				List<Entity> entities = ctx.entities;
				bool showDebug = ctx.showDebug;
				IReadOnlyList<Program.LocalPlayer>? playersToRender = ctx.playersToRender;
				bool drawPlayer = ctx.drawPlayer;
				int ts = ctx.ts;
				int startX = ctx.startX;
				int endX = ctx.endX;
				int startY = ctx.startY;
				int endY = ctx.endY;
				int visibleObjectCount = ctx.visibleObjectCount;

				// Hitboxes debug
				if (showDebug)
				{
					if (DebugOverlaySettings.EntityHitboxes || DebugOverlaySettings.AiDiagnostics
						|| DebugOverlaySettings.VehicleAndAttackHitboxes)
					{
						foreach (var entity in entities)
						{
							if (!entity.IsAlive) continue;
							if (!ShouldRenderEntity(entity)) continue;
							Vector2 entityDebugPos = entity.VisualWorldPos;
							if (DebugOverlaySettings.VehicleAndAttackHitboxes)
								entity.DrawAttackDebugZone();

							if (DebugOverlaySettings.EntityHitboxes)
							{
								Rectangle hitboxRect = GetEntityCollisionHitbox(entityDebugPos, entity.Species, ts);
								Rectangle damageHitboxRect = GetEntityDamageHitbox(entity, ts);
								Raylib.DrawRectangleLinesEx(hitboxRect, 2, Color.Red);
								Raylib.DrawRectangleLinesEx(damageHitboxRect, 2, Color.Blue);
								string speciesName = entity.Species;
								int textWidth = Raylib.MeasureText(speciesName, 10);
								Raylib.DrawText(speciesName,
									(int)(hitboxRect.X + hitboxRect.Width / 2 - textWidth / 2),
									(int)(hitboxRect.Y - 12), 10, Color.White);
							}

							if (DebugOverlaySettings.AiDiagnostics)
							{
								entity.DrawPath();
								entity.DrawPriorityDebugLabels();
							}
						}
					}

					if (DebugOverlaySettings.EntityHitboxes && drawPlayer)
					{
						Rectangle playerCollision = GetEntityCollisionHitbox(playerPos, Program.GetPlayerCollisionDimensions(), ts);
						Rectangle playerDamage = GetEntityDamageHitbox(playerPos, Program.PlayerMorphSpecies, ts);
						Raylib.DrawRectangleLinesEx(playerCollision, 2, Color.Red);
						Raylib.DrawRectangleLinesEx(playerDamage, 2, Color.Blue);
						Raylib.DrawText("joueur local", (int)playerDamage.X, (int)(playerDamage.Y - 12), 10, Color.White);
					}

					if (DebugOverlaySettings.EntityHitboxes && playersToRender != null)
					{
						foreach (var remotePlayer in playersToRender)
						{
							string species = remotePlayer.SpeciesOverride ?? "human";
							Rectangle playerCollision = GetEntityCollisionHitbox(remotePlayer.Position, Program.GetPlayerCollisionDimensions(), ts);
							Rectangle playerDamage = GetEntityDamageHitbox(remotePlayer.Position, species, ts);
							Raylib.DrawRectangleLinesEx(playerCollision, 2, Color.Red);
							Raylib.DrawRectangleLinesEx(playerDamage, 2, Color.Blue);
							string label = string.IsNullOrWhiteSpace(remotePlayer.DisplayName) ? "joueur réseau" : remotePlayer.DisplayName;
							Raylib.DrawText(label, (int)playerDamage.X, (int)(playerDamage.Y - 12), 10, Color.White);
						}
					}

					//  Hitboxes de TOUS les objets du monde visibles (meubles, arbres, murs, etc.),
					// via la même géométrie que GetCollisionRect (celle réellement utilisée pour les
					// collisions et pour la détection de clic). Avant, seules les entités et les
					// quelques tuiles voisines débordées par une hitbox (contour orange ci-dessous)
					// étaient visibles en debug — l'objet lui-même n'affichait jamais sa hitbox.
					if (DebugOverlaySettings.TileHitboxes)
					{
						for (int dhx = startX; dhx <= endX; dhx++)
						{
							for (int dhy = startY; dhy <= endY; dhy++)
							{
								int dhObjectId = GetObjectIdAt(dhx, dhy);
								if (dhObjectId == 0) continue;
								var dhTileData = WorldTileRegistry.GetTile(dhObjectId);
								if (dhTileData == null) continue;
								var (dhX, dhY, dhW, dhH) = GetCollisionRect(dhx, dhy, dhTileData, ts);
								Raylib.DrawRectangleLinesEx(new Rectangle(dhX, dhY, dhW, dhH), 2, Color.Lime);
							}
						}

						// Tuiles sans objet directement dessus mais bloquées par la hitbox d'un objet voisin.
						if (visibleObjectCount > 0)
						{
							var pfBlocked = GetHitboxBlockedTiles(startX, endX, startY, endY, destroyed);
							for (int dbx = startX; dbx <= endX; dbx++)
							{
								for (int dby = startY; dby <= endY; dby++)
								{
									if (!pfBlocked.Contains((dbx, dby))) continue;
									if (GetObjectIdAt(dbx, dby) != 0) continue;
									int dbBaseY = dby * ts;
									Raylib.DrawRectangleLinesEx(new Rectangle(dbx * ts, dbBaseY, ts, ts), 2, Color.Orange);
								}
							}
						}
					}
				}
			}
        }
    }
}
