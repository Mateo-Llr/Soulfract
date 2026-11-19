#nullable enable
using System.Diagnostics;
using System.Numerics;

namespace Soulfract
{
    /// <summary>
    /// Budget TEMPS (et non plus seulement nombre) pour les recherches A* des entités.
    /// Pathfinder.TryReservePathRequest limite le NOMBRE de requêtes par frame, mais une seule
    /// recherche vers une cible injoignable explore toute la fenêtre (marge jusqu'à 48 tuiles)
    /// et peut coûter à elle seule des dizaines de ms. Ici on mesure le coût réel : un "seau"
    /// de ms qui se remplit avec le temps réel. Une recherche trop chère vide le seau, et les
    /// autres entités attendent quelques frames au lieu de s'empiler dans la même frame.
    /// </summary>
    internal static class AiPathBudget
    {
        private const double RefillPerMs = 0.15;   // ~2,5 ms d'A* par frame à 60 FPS
        private const double MaxTokensMs = 6.0;    // pic autorisé
        private const double MinTokensMs = -12.0;  // dette max (une grosse recherche ne bloque pas > ~80 ms)

        private static double _tokensMs = MaxTokensMs;
        private static long _lastStamp = Stopwatch.GetTimestamp();

        private static void Refill()
        {
            long now = Stopwatch.GetTimestamp();
            double elapsedMs = (now - _lastStamp) * 1000.0 / Stopwatch.Frequency;
            _lastStamp = now;
            _tokensMs = Math.Min(MaxTokensMs, _tokensMs + elapsedMs * RefillPerMs);
        }

        /// <summary>Vrai si le budget autorise de lancer un nouvel A* maintenant.</summary>
        public static bool CanSearch()
        {
            Refill();
            return _tokensMs > 0.0;
        }

        /// <summary>FindPath chronométré : débite le budget du coût réel de la recherche.</summary>
        public static List<Vector2> Find(Vector2 from, Vector2 to, HashSet<string> destroyed, int margin, string species)
        {
            long t0 = Stopwatch.GetTimestamp();
            var path = Pathfinder.FindPath(from, to, destroyed, margin, species);
            double costMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            _tokensMs = Math.Max(MinTokensMs, _tokensMs - costMs);
            return path;
        }
    }
}
