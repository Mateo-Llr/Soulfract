// Program_DebugOverlays.cs
//  F3 à deux niveaux, pour pouvoir MESURER les performances sans fausser la mesure :
//   1er appui  : panneau de debug/perf seul (aucun dessin supplémentaire dans le monde)
//   2e appui   : panneau + overlays monde (hitboxes de tous les objets, chemins IA, zones de maisons...)
//   3e appui   : tout éteint
//  Avant, F3 activait d'emblée tous les overlays monde, qui coûtent à eux seuls ~3-4 ms/frame
//  (hitbox de chaque objet visible, diagnostic du pathfinding, bâtiments de chaque tuile...) :
//  le panneau de perf mesurait donc en grande partie le coût de F3 lui-même.
#nullable enable

namespace Soulfract
{
    internal static class DebugOverlaySettings
    {
        public static bool EntityHitboxes { get; private set; }
        public static bool TileHitboxes { get; private set; }
        public static bool AiDiagnostics { get; private set; }
        public static bool BuildingZones { get; private set; }
        public static bool VehicleAndAttackHitboxes { get; private set; }

        public static bool AnyEnabled =>
            EntityHitboxes || TileHitboxes || AiDiagnostics || BuildingZones || VehicleAndAttackHitboxes;

        public static void SetAll(bool enabled)
        {
            EntityHitboxes = enabled;
            TileHitboxes = enabled;
            AiDiagnostics = enabled;
            BuildingZones = enabled;
            VehicleAndAttackHitboxes = enabled;
        }

        public static void ToggleEntityHitboxes() => EntityHitboxes = !EntityHitboxes;
        public static void ToggleTileHitboxes() => TileHitboxes = !TileHitboxes;
        public static void ToggleAiDiagnostics() => AiDiagnostics = !AiDiagnostics;
        public static void ToggleBuildingZones() => BuildingZones = !BuildingZones;
        public static void ToggleVehicleAndAttackHitboxes() =>
            VehicleAndAttackHitboxes = !VehicleAndAttackHitboxes;
    }

    public static partial class Program
    {
        private static bool _debugWorldOverlays;

        private static void CycleDebugMode()
        {
            if (!_showDebug)
            {
                _showDebug = true;
                _debugWorldOverlays = false;
                DebugOverlaySettings.SetAll(false);
            }
            else if (!_debugWorldOverlays)
            {
                _debugWorldOverlays = true;
                DebugOverlaySettings.SetAll(true);
            }
            else
            {
                _showDebug = false;
                _debugWorldOverlays = false;
                DebugOverlaySettings.SetAll(false);
            }
        }
    }
}
