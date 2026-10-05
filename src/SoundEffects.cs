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

        public static void Play(string name, float volume = 0.55f)
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
            Raylib.PlaySound(sound);
        }

        public static void PlayPlant() => Play("plant");
        public static void PlayHoe() => Play("hoe");
        public static void PlayCraft() => Play("craft");
        public static void PlayArrowImpact() => Play("arrow_impact", 0.45f);
    }
}