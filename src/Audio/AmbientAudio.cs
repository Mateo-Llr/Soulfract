#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class AmbientAudio
    {
        private const float WIND_VOLUME = 0.12f;
        private const float STORM_WIND_VOLUME = 0.32f;
        private const float CAMPFIRE_VOLUME = 0.28f;
        private const float FADE_SPEED = 0.22f;

        private static Music _windMusic;
        private static Sound _campfireSound;
        private static bool _windReady;
        private static bool _windPlaying;
        private static bool _campfireReady;
        private static bool _loadAttempted;
        private static float _windVolume;
        private static float _campfireVolume;
        private static bool _isPaused;

        public static void SetPaused(bool paused)
        {
            if (_isPaused == paused)
                return;

            _isPaused = paused;
            if (_windReady && _windPlaying)
            {
                if (paused) Raylib.PauseMusicStream(_windMusic);
                else Raylib.ResumeMusicStream(_windMusic);
            }

            if (_campfireReady && Raylib.IsSoundPlaying(_campfireSound))
            {
                if (paused) Raylib.PauseSound(_campfireSound);
                else Raylib.ResumeSound(_campfireSound);
            }
        }

        private static void LoadIfNeeded()
        {
            if (_loadAttempted)
                return;

            _loadAttempted = true;
            _windMusic = Raylib.LoadMusicStream(Path.Combine("assets", "sounds", "wind.mp3"));
            _windReady = Raylib.IsMusicReady(_windMusic);
            if (_windReady)
                _windMusic.Looping = true;
            _campfireSound = Raylib.LoadSound(Path.Combine("assets", "sounds", "campfire.mp3"));
            _campfireReady = Raylib.IsSoundReady(_campfireSound);
        }

        private static bool IsInterior(Vector2 playerPosition)
        {
            int tileX = (int)MathF.Floor(playerPosition.X / Program.TileSize);
            int tileY = (int)MathF.Floor(playerPosition.Y / Program.TileSize);
            return World.GetOverlayAt(tileX, tileY) != 0;
        }

        private static float Approach(float current, float target, float maxStep)
        {
            return MathF.Abs(target - current) <= maxStep
                ? target
                : current + MathF.Sign(target - current) * maxStep;
        }

        private static float GetNearestCampfireDistance(Vector2 playerPosition)
        {
            float nearestDistance = float.MaxValue;
            int radius = 6;
            int playerTileX = (int)MathF.Floor(playerPosition.X / Program.TileSize);
            int playerTileY = (int)MathF.Floor(playerPosition.Y / Program.TileSize);

            for (int x = playerTileX - radius; x <= playerTileX + radius; x++)
            {
                for (int y = playerTileY - radius; y <= playerTileY + radius; y++)
                {
                    if (World.GetObjectIdAt(x, y) != 18)
                        continue;

                    Vector2 campfirePosition = new(x * Program.TileSize + Program.TileSize / 2f, y * Program.TileSize + Program.TileSize / 2f);
                    nearestDistance = MathF.Min(nearestDistance, Vector2.Distance(playerPosition, campfirePosition));
                }
            }

            return nearestDistance;
        }

        public static void Update(float dt, Vector2 playerPosition)
        {
            if (_isPaused)
                return;

            LoadIfNeeded();
            bool canHearWind = !World.IsUnderground && !IsInterior(playerPosition);
            float stormWindVolume = WIND_VOLUME + (STORM_WIND_VOLUME - WIND_VOLUME) * Weather.StormIntensity;
            float targetWindVolume = canHearWind ? stormWindVolume : 0f;
            float maxStep = FADE_SPEED * dt;
            _windVolume = Approach(_windVolume, targetWindVolume, maxStep);

            if (_windReady)
            {
                if (!_windPlaying)
                {
                    Raylib.PlayMusicStream(_windMusic);
                    _windPlaying = true;
                }
                Raylib.UpdateMusicStream(_windMusic);
                Raylib.SetMusicVolume(_windMusic, _windVolume);
            }

            float campfireDistance = canHearWind ? GetNearestCampfireDistance(playerPosition) : float.MaxValue;
            float targetCampfireVolume = campfireDistance <= Program.TileSize * 6f
                ? CAMPFIRE_VOLUME * (1f - campfireDistance / (Program.TileSize * 6f))
                : 0f;
            _campfireVolume = Approach(_campfireVolume, targetCampfireVolume, maxStep);

            if (_campfireReady)
            {
                if (_campfireVolume > 0.001f && !Raylib.IsSoundPlaying(_campfireSound))
                    Raylib.PlaySound(_campfireSound);
                Raylib.SetSoundVolume(_campfireSound, _campfireVolume);
                if (_campfireVolume <= 0.001f && Raylib.IsSoundPlaying(_campfireSound))
                    Raylib.StopSound(_campfireSound);
            }
        }
    }
}