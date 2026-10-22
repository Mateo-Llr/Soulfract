// WireRendering.cs
// Connexions électriques (fils) et fil en cours de placement.
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
			/// <summary>Ajoute les fils électriques et la ligne de connexion en cours.</summary>
			internal static void AddWires(DrawWorldContext ctx)
			{
				Vector2 mouseWorldPos = ctx.mouseWorldPos;
				int ts = ctx.ts;
				var renderItems = _renderItemsPool;

				// ========== CONNEXIONS ÉLECTRIQUES (fils) ==========
				foreach (var conn in Program.GetEnergyConnections())
				{
					if (!Program.IsTileLit(conn.X1, conn.Y1) || !Program.IsTileLit(conn.X2, conn.Y2))
						continue;
					Vector2 p1 = new Vector2(conn.X1 * ts + ts / 2f, conn.Y1 * ts + ts / 2f);
					Vector2 p2 = new Vector2(conn.X2 * ts + ts / 2f, conn.Y2 * ts + ts / 2f);
					if (!ctx.IsSegmentNearScreen(p1, p2))
						continue;
				
					// Ajuster pour la hauteur du terrain
					int h1 = GetHeightAt(conn.X1, conn.Y1);
					int h2 = GetHeightAt(conn.X2, conn.Y2);
					p1.Y -= h1 * ts / 4f;
					p2.Y -= h2 * ts / 4f;
				
					float sortY = Math.Min(p1.Y, p2.Y);
				
					renderItems.Add(new RenderItem
					{
						Y = sortY,
						Kind = RenderItemKind.Wire,
						Tag = new WireRenderTag(p1, p2, (p1 + p2) / 2f, IsConnectionPowered(conn), 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 8f))
					});
				}
			
				// Ligne de connexion en cours (mode placement)
				if (Program.IsConnectingWire() && Program.GetWireStartTile() != (-1, -1)
					&& Program.IsTileLit(Program.GetWireStartTile().x, Program.GetWireStartTile().y))
				{
					var startTile = Program.GetWireStartTile();
					Vector2 startPos = new Vector2(startTile.x * ts + ts / 2f, startTile.y * ts + ts / 2f);
					startPos.Y -= GetHeightAt(startTile.x, startTile.y) * ts / 4f;
				
					Vector2 endPos = mouseWorldPos;
					float sortY = Math.Min(startPos.Y, endPos.Y);
				
					renderItems.Add(new RenderItem
					{
						Y = sortY,
						Kind = RenderItemKind.Wire,
						Tag = new WireRenderTag(startPos, endPos, (startPos + endPos) / 2f, false, 0f)
					});
				}
			}
        }
    }
}
