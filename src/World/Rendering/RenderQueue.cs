// RenderQueue.cs
// File de RenderItems : initialisation, projectiles, éléments externes, tri stable.
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
			/// <summary>Réinitialise (ou crée) la liste réutilisable de RenderItems pour cette frame.</summary>
			internal static void InitRenderItems(DrawWorldContext ctx)
			{
				if (_renderItemsPoolInUse)
					_renderItemsPool.Clear();
				else
					_renderItemsPoolInUse = true;
			}

			/// <summary>Projectiles magiques, intégrés au tri par Y.</summary>
			internal static void AddSpellProjectiles(DrawWorldContext ctx)
			{
				List<SpellProjectile>? spellProjectiles = ctx.spellProjectiles;
				int ts = ctx.ts;
				var renderItems = _renderItemsPool;

				// Projectiles magiques : intégrés au tri par Y pour réagir correctement aux
				// hitbox des tuiles/entités (passer devant/derrière selon leur profondeur),
				// au lieu d'être dessinés dans une passe séparée toujours au-dessus de tout.
				if (spellProjectiles != null)
				{
					foreach (var sp in spellProjectiles)
					{
						var spRef = sp;
						if (!Program.IsTileLit((int)(spRef.Position.X / ts), (int)(spRef.Position.Y / ts)))
							continue;
						renderItems.Add(new RenderItem
						{
							Y = spRef.Position.Y,
							Kind = RenderItemKind.Projectile,
							Tag = new ProjectileRenderTag(spRef, spRef.Position, spRef.Size, spRef.VisualRotation, spRef.VisualTexture, spRef.Color, spRef.VisualTexture.Id != 0)
						});
					}
				}
			}

			/// <summary>Éléments externes (offrandes du RitualCircleUI, etc.) injectés puis vidés.</summary>
			internal static void AddExternalItems(DrawWorldContext ctx)
			{
				var renderItems = _renderItemsPool;

				// Éléments externes (offrandes du RitualCircleUI, etc.) : injectés ici pour être triés
				// par Y avec le reste du monde, puis la liste externe est vidée pour la frame suivante.
				if (_externalRenderItems.Count > 0)
				{
					renderItems.AddRange(_externalRenderItems);
					_externalRenderItems.Clear();
				}
			}

			/// <summary>Tri par Y avec départage stable par index d'insertion (évite le clignotement à Y égal).</summary>
			internal static void SortRenderItems(DrawWorldContext ctx)
			{
				var renderItems = _renderItemsPool;

				//  Fix flicker "gros sprites" (ex : deux arbres voisins) : on assigne d'abord un index
				// d'insertion stable à chaque élément, puis on l'utilise comme départage lorsque deux
				// éléments ont un Y de tri identique (ou quasi identique). Sans ce départage explicite,
				// List<T>.Sort() (non stable) pouvait inverser aléatoirement l'ordre de deux sprites à
				// Y égal d'une frame à l'autre, ce qui les faisait clignoter/se repasser devant l'un
				// l'autre. Avec ce départage, l'ordre devient un ordre total déterministe : à
				// configuration égale, deux sprites à Y égal se dessinent toujours dans le même ordre.
				for (int ri = 0; ri < renderItems.Count; ri++)
				{
					var tmp = renderItems[ri];
					tmp.SortIndex = ri;
					renderItems[ri] = tmp;
				}
				renderItems.Sort((a, b) =>
				{
					int layerComparison = a.SortLayer.CompareTo(b.SortLayer);
					if (layerComparison != 0) return layerComparison;
					int c = a.Y.CompareTo(b.Y);
					if (c != 0) return c;
					return a.SortIndex.CompareTo(b.SortIndex);
				});
			}
        }
    }
}
