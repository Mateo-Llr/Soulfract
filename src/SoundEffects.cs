#nullable enable
using Raylib_cs;

namespace Soulfract
{
    public static class SoundEffects
    {
        private static readonly Dictionary<string, Sound> Sounds = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> MissingSounds = new(StringComparer.OrdinalIgnoreCase);

        private static string? FindSoundPath(string name)
        {
            string baseName = Path.Combine("assets", "sounds", Path.GetFileNameWithoutExtension(name));
            string[] candidates =
            {
                Path.Combine("assets", "sounds", name),
                $"{baseName}.mp3",
                $"{baseName}.wav",
                $"{baseName}.ogg"
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        public static void Play(string name, float volume = 0.55f, float pitchVariation = 0f)
        {
            if (!Sounds.TryGetValue(name, out Sound sound))
            {
                string? path = FindSoundPath(name);
                if (path == null)
                {
                    if (MissingSounds.Add(name))
                        Console.WriteLine($" Effet sonore introuvable : assets\\sounds\\{Path.GetFileNameWithoutExtension(name)}.(mp3/wav/ogg)");
                    return;
                }

                sound = Raylib.LoadSound(path);
                if (!Raylib.IsSoundReady(sound))
                    return;
                Sounds[name] = sound;
            }

            Raylib.SetSoundVolume(sound, volume);
            float pitch = 1f + (Random.Shared.NextSingle() * 2f - 1f) * Math.Clamp(pitchVariation, 0f, 1f);
            Raylib.SetSoundPitch(sound, pitch);
            Raylib.PlaySound(sound);
        }

        public static void PlayPlant() => Play("plant");
        public static void PlayHoe() => Play("hoe");
        public static void PlayCraft() => Play("craft");
        public static void PlayArrowImpact() => Play("arrow_impact", 0.45f);
        public static void PlayEntitySurprise() => Play("entity_surprise", pitchVariation: 0.04f);
        public static void PlayEnemyDeathPuff() => Play("enemydeathpuff", pitchVariation: 0.04f);
        public static void PlaySwing() => Play("swing", pitchVariation: 0.04f);
        public static void PlayTreeBreaking() =>
            Play($"tree_breaking{Random.Shared.Next(1, 3)}", pitchVariation: 0.04f);
        public static void PlayTreeHitGround() =>
            Play($"tree_hitground{Random.Shared.Next(1, 3)}", pitchVariation: 0.04f);
        public static void PlayFistHitOrganic() =>
            Play($"fist_hit_organic{Random.Shared.Next(1, 3)}", pitchVariation: 0.04f);
        public static void PlaySwordKillOrganic()
        {
            int variant = Random.Shared.Next(1, 5);
            Play($"sword_kill_organic{variant}", pitchVariation: 0.04f);
        }
    }
}