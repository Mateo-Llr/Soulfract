// VehicleRendering.cs
// Voitures, wagons et liens de wagons.
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
			/// <summary>Voitures, liens visuels de wagons et wagon courant du joueur.</summary>
			internal static void AddCarsAndWagons(DrawWorldContext ctx)
			{
				var renderItems = _renderItemsPool;

				foreach (var car in Program.Cars)
				{
					if (!ctx.IsEntityNearScreen(car.Position))
						continue;
					float sortY = car.Position.Y + Program.FeetOffsetY;
					renderItems.Add(new RenderItem
					{
						Y = sortY,
						Kind = RenderItemKind.Car,
						Tag = new CarRenderTag(car)
					});
				}

				foreach (var sb in Program.Skateboards)
				{
					if (!ctx.IsEntityNearScreen(sb.Position))
						continue;
					renderItems.Add(sb.ToRenderItem(Program.GetSkateboardSortY(sb)));
				}

				if (Program.HasWagonLinkVisuals())
				{
					// Barres d'attelage : triées tout en bas pour passer sous les sprites des wagons.
					renderItems.Add(new RenderItem
					{
						Y = float.MinValue,
						DrawAction = () => Program.DrawWagonLinks()
					});
				}

				if (Program.PlayerCurrentWagonTile.HasValue)
				{
					Vector2 wagonPos = Program.GetMountedWagonDrawPosition();
					if (ctx.IsEntityNearScreen(wagonPos))
					{
						renderItems.Add(new RenderItem
						{
							Y = wagonPos.Y + Program.FeetOffsetY - 0.5f,
							DrawAction = () => Program.DrawWagon(
								Program.GetMountedWagonDrawPosition(),
								(int)Program.TileSize,
								Program.IsMountedWagonVertical())
						});
					}
				}
			}

			/// <summary>Wagon monté par le joueur (tri par Y avec le reste).</summary>
			internal static void AddMountedPlayerWagon(DrawWorldContext ctx)
			{
				var renderItems = _renderItemsPool;

				if (Program.PlayerCurrentWagonTile.HasValue)
				{
					Vector2 wagonPos = Program.GetMountedWagonDrawPosition();
					if (ctx.IsEntityNearScreen(wagonPos))
					{
						renderItems.Add(new RenderItem
						{
							Y = wagonPos.Y + Program.FeetOffsetY + 0.5f,
							DrawAction = () => Program.DrawMountedWagonFront(
								Program.GetMountedWagonDrawPosition(),
								(int)Program.TileSize,
								Program.IsMountedWagonVertical())
						});
					}
				}
			}
        }
    }
}
