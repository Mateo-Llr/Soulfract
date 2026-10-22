#nullable enable
using Raylib_cs;

namespace Soulfract
{
    public static partial class World
    {
        private static Texture2D _railwayVerticalTexture;
        private static Texture2D _railwayCornerTexture;

        public static bool IsRailwayAt(int tileX, int tileY)
        {
            return GetOverlayAt(tileX, tileY) == 123
                || GetGroundTileIdAt(tileX, tileY) == 123
                || GetObjectIdAt(tileX, tileY) == 123;
        }

        private static Texture2D GetRailwayTexture(int tileX, int tileY)
        {
            bool north = IsRailwayAt(tileX, tileY - 1);
            bool east = IsRailwayAt(tileX + 1, tileY);
            bool south = IsRailwayAt(tileX, tileY + 1);
            bool west = IsRailwayAt(tileX - 1, tileY);
            bool hasHorizontal = east || west;
            bool hasVertical = north || south;

            if (hasHorizontal && hasVertical)
            {
                if (_railwayCornerTexture.Id == 0 && File.Exists("assets/tiles/railway_c.png"))
                    _railwayCornerTexture = Raylib.LoadTexture("assets/tiles/railway_c.png");
                if (_railwayCornerTexture.Id != 0) return _railwayCornerTexture;
            }

            if (hasVertical && !hasHorizontal)
            {
                if (_railwayVerticalTexture.Id == 0 && File.Exists("assets/tiles/railway_v.png"))
                    _railwayVerticalTexture = Raylib.LoadTexture("assets/tiles/railway_v.png");
                if (_railwayVerticalTexture.Id != 0) return _railwayVerticalTexture;
            }

            var horizontalTexture = WorldTileRegistry.GetTextureByName("railway_h");
            return horizontalTexture.Id != 0 ? horizontalTexture : _tileTextures.GetValueOrDefault(123);
        }

        private static float GetRailwayRotation(int tileX, int tileY)
        {
            return 0f;
        }
    }
}