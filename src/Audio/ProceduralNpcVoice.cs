using System.Buffers.Binary;
using System.Text;
using Raylib_cs;

namespace Soulfract
{
    internal static class ProceduralNpcVoice
    {
        private const int SampleRate = 22050;
        private const float SoundDuration = 0.12f;
        private const int PitchStep = 8;
        private const int TakeCount = 3;
        private const int MaxCachedSounds = 1500;

        // Consonant : consonne qui précède la voyelle (voir les constantes C* plus bas : fricatives,
        //             occlusives, nasale, latérale, "r", glissante... chacune avec son propre bruit,
        //             sa durée et sa coarticulation)
        // Effort    : 0 doux/soufflé, 1 normal, 2 tendu/crié
        // Length    : 0 bref, 1 normal, 2 long, 3 très long (voyelles étirées)
        // Glide     : -2..+2, mouvement de hauteur DANS la syllabe (descente / montée)
        // Coda      : 1 = la syllabe se termine par une nasale (n/m) -> voyelle nasalisée
        private readonly record struct SoundKey(
            int Voice, int Vowel, int Pitch, int Take, int Consonant, int Effort, int Length, int Glide, int Coda);
        private readonly record struct VoiceProfile(
            float BasePitch,
            float FormantScale,
            float SpectralTilt,
            float Breathiness,
            float Roughness,       // irrégularité des cordes vocales (jitter / shimmer / craquement)
            float Nasality,        // coloration nasale permanente
            float Expressiveness); // amplitude des variations de mélodie
        private readonly record struct PhraseInfo(
            int Start, int End, char Ending,
            bool IsQuestion, bool IsWhQuestion, int ExclamationCount, bool IsEllipsis);
        private readonly record struct Prosody(float Pitch, float Volume, int Effort, int Length, int Glide);

        private static readonly Dictionary<SoundKey, Sound> Sounds = new();
        private static readonly List<Sound> PlayingSounds = new();
        private static readonly Random Rng = new();
        private static int _nextTake;
        private static float _melody; // marche aléatoire lente de la mélodie

        private static readonly float[] EffortVolume = { 0.72f, 1.00f, 1.45f };
        private static readonly double[] LengthScale = { 0.82, 1.00, 1.35, 1.90 };

        private static readonly HashSet<string> QuestionWords = new()
        {
            "qui", "que", "qu", "quoi", "quel", "quelle", "quels", "quelles", "où", "quand", "comment",
            "pourquoi", "combien", "lequel", "laquelle",
            "who", "what", "where", "when", "why", "how", "which",
            "wer", "was", "wo", "wann", "warum", "wie",
            "quién", "qué", "dónde", "cuándo", "cómo", "cuál"
        };

        private static readonly HashSet<string> Hesitations = new()
        {
            "euh", "heu", "hum", "hmm", "hmmm", "mmh", "mmm", "uh", "um", "uhm", "er"
        };

        // Les 8 premières voix sont celles d'origine (mêmes hauteurs / formants) auxquelles on ajoute
        // rugosité, nasalité et expressivité ; les deux dernières sont des voix plus aiguës (féminine / jeune).
        private static readonly VoiceProfile[] Profiles =
        {
            new(112f, 0.91f, 1.42f, 0.050f, 1.30f, 0.10f, 0.90f), new(120f, 1.08f, 1.24f, 0.036f, 0.80f, 0.05f, 1.10f),
            new(128f, 0.97f, 1.34f, 0.044f, 1.00f, 0.16f, 1.00f), new(136f, 1.14f, 1.18f, 0.032f, 0.70f, 0.08f, 1.15f),
            new(144f, 1.02f, 1.30f, 0.046f, 1.10f, 0.20f, 0.95f), new(152f, 0.94f, 1.48f, 0.055f, 1.60f, 0.12f, 0.85f),
            new(160f, 1.11f, 1.20f, 0.035f, 0.80f, 0.06f, 1.10f), new(170f, 1.00f, 1.38f, 0.042f, 1.00f, 0.10f, 1.00f),
            new(205f, 1.17f, 1.14f, 0.040f, 0.70f, 0.07f, 1.20f), new(235f, 1.21f, 1.10f, 0.034f, 0.60f, 0.05f, 1.25f)
        };

        private static readonly (float F1, float F2, float F3)[] VowelFormants =
        {
            (800f, 1200f, 2600f), // a
            (530f, 1840f, 2480f), // e
            (270f, 2290f, 3010f), // i
            (570f, 840f, 2410f),  // o
            (300f, 870f, 2240f),  // u
            (640f, 1190f, 2390f)  // voyelle neutre
        };

        // Loudness intrinsèque (un "a" sonne plus fort qu'un "i" ou un "u" à effort égal) et
        // hauteur intrinsèque (les voyelles fermées sont un peu plus aiguës).
        private static readonly float[] VowelLoudness = { 1.00f, 0.90f, 0.74f, 0.90f, 0.78f, 0.88f };
        private static readonly double[] VowelIntrinsicPitch = { 0.990, 1.000, 1.025, 1.005, 1.020, 1.000 };

        // Niveau RMS visé pour la partie voisée (comparable à l'ancien rendu, pour ne pas toucher au volume global)
        private const double TargetVoicedRms = 0.27;

        public static void Update(Entity npc, string text, int visibleCharCount, ref int processedCharacters)
        {
            PlayingSounds.RemoveAll(sound => !Raylib.IsSoundPlaying(sound));
            int visible = Math.Clamp(visibleCharCount, processedCharacters, text.Length);
            int voice = (int)(unchecked((uint)npc.NetId.GetHashCode()) % (uint)Profiles.Length);

            for (int i = processedCharacters; i < visible; i++)
            {
                if (!IsSyllableStart(text, i))
                    continue;

                int vowel = GetVowelIndex(text[i]);
                VoiceProfile profile = Profiles[voice];
                Prosody prosody = GetProsody(text, i);

                // Mélodie vivante : dérive lente + micro-variation par syllabe.
                // L'expressivité de la voix amplifie / atténue les écarts de la prosodie.
                _melody = _melody * 0.82f + (Rng.NextSingle() - 0.5f) * 0.10f;
                float pitchFactor = 1f + (prosody.Pitch - 1f) * profile.Expressiveness;
                float pitch = profile.BasePitch * pitchFactor * (1f + Math.Clamp(_melody, -0.07f, 0.07f));
                int pitchBucket = Math.Clamp(
                    (int)MathF.Round(pitch / PitchStep) * PitchStep,
                    70,
                    340);

                int consonant = GetConsonant(text, i);
                int coda = GetCoda(text, i);

                // On évite de relancer un son encore en cours de lecture (ça coupait la queue du son
                // précédent avec un "clic") : on prend la prise suivante si besoin.
                Sound sound = default;
                bool found = false, failed = false;
                for (int attempt = 0; attempt < TakeCount && !failed; attempt++)
                {
                    int take = (_nextTake + attempt) % TakeCount;
                    SoundKey key = new(voice, vowel, pitchBucket, take, consonant,
                        prosody.Effort, prosody.Length, prosody.Glide, coda);

                    if (!Sounds.TryGetValue(key, out sound))
                    {
                        if (Sounds.Count >= MaxCachedSounds)
                            TrimCache();

                        sound = CreateSound(key, profile);
                        if (!Raylib.IsSoundReady(sound))
                        {
                            Console.WriteLine($"Impossible de générer la voix du PNJ (voyelle {vowel}, hauteur {pitchBucket} Hz).");
                            failed = true;
                            break;
                        }
                        Sounds.Add(key, sound);
                    }

                    if (!Raylib.IsSoundPlaying(sound) || attempt == TakeCount - 1)
                    {
                        _nextTake = (take + 1) % TakeCount;
                        found = true;
                        break;
                    }
                }

                if (failed || !found)
                    continue;

                float volume = 0.19f * EffortVolume[prosody.Effort] * prosody.Volume
                    * (0.92f + Rng.NextSingle() * 0.16f);

                Raylib.SetSoundVolume(sound, Math.Clamp(volume, 0f, 0.8f));
                // Variation de hauteur à la lecture volontairement faible : elle décale aussi les formants
                // (effet "bande magnétique"), donc plus on l'augmente, moins la voix est naturelle.
                Raylib.SetSoundPitch(sound, 1f + (Rng.NextSingle() - 0.5f) * 0.03f);
                Raylib.PlaySound(sound);
                if (!PlayingSounds.Contains(sound))
                    PlayingSounds.Add(sound);
            }

            processedCharacters = visible;
        }

        // Libère les sons du cache qui ne sont pas en cours de lecture (garde-fou mémoire : les
        // combinaisons voyelle x consonne x hauteur x effort... peuvent croître au fil d'une longue partie).
        private static void TrimCache()
        {
            PlayingSounds.RemoveAll(s => !Raylib.IsSoundPlaying(s));
            List<SoundKey> toRemove = new();
            foreach (KeyValuePair<SoundKey, Sound> entry in Sounds)
            {
                if (Raylib.IsSoundPlaying(entry.Value))
                    continue;
                Raylib.UnloadSound(entry.Value);
                toRemove.Add(entry.Key);
            }
            foreach (SoundKey key in toRemove)
                Sounds.Remove(key);
        }

        /// <summary>
        /// Multiplicateur à appliquer à l'intervalle d'affichage du caractère `index`
        /// (pauses après la ponctuation, débit rapide sur les exclamations, ralenti sur les points de suspension).
        /// Facultatif : à brancher sur la vitesse d'écriture du texte pour synchroniser rythme et voix.
        /// </summary>
        public static float GetCharacterDelayMultiplier(string text, int index)
        {
            if (index < 0 || index >= text.Length)
                return 1f;

            char c = text[index];
            if (IsPhrasePunctuation(c))
            {
                bool inRun = index + 1 < text.Length && text[index + 1] is '!' or '?' or '.' or '…';
                if (inRun)
                    return 1.3f;

                bool ellipsis = c == '…' || (c == '.' && index > 0 && text[index - 1] == '.');
                return c switch
                {
                    ',' => 3f,
                    ';' or ':' => 3.5f,
                    '…' => 7f,
                    '.' when ellipsis => 7f,
                    _ => 5f
                };
            }

            if (!char.IsLetter(c))
                return 1f;

            PhraseInfo phrase = AnalyzePhrase(text, index);
            float multiplier = 1f;
            if (phrase.ExclamationCount > 0 && !phrase.IsQuestion)
                multiplier *= 0.85f;
            if (phrase.IsEllipsis && phrase.End > phrase.Start
                && (index - phrase.Start) / (float)(phrase.End - phrase.Start) > 0.6f)
                multiplier *= 1.25f;
            if (IsUppercaseWord(text, index))
                multiplier *= 1.15f;
            return multiplier;
        }

        public static void Stop()
        {
            foreach (Sound sound in PlayingSounds)
            {
                if (Raylib.IsSoundPlaying(sound))
                    Raylib.StopSound(sound);
            }
            PlayingSounds.Clear();
        }

        public static void Unload()
        {
            Stop();
            foreach (Sound sound in Sounds.Values)
                Raylib.UnloadSound(sound);
            Sounds.Clear();
        }

        // ------------------------------------------------------------------
        //  Prosodie : décide hauteur, volume, effort, durée et mouvement de
        //  hauteur de chaque syllabe selon la phrase qui la contient.
        // ------------------------------------------------------------------
        private static Prosody GetProsody(string text, int index)
        {
            PhraseInfo phrase = AnalyzePhrase(text, index);
            GetSyllablePosition(text, phrase, index, out int syllableIndex, out int syllableCount);
            bool isLast = syllableIndex >= syllableCount - 1;
            float progress = syllableCount > 1 ? syllableIndex / (float)(syllableCount - 1) : 0f;
            bool stressed = IsFirstSyllableOfWord(text, index);

            float pitch;
            float volume = 1f;
            int effort = 1, length = 1, glide = 0;

            if (phrase.IsQuestion)
            {
                if (phrase.IsWhQuestion)
                {
                    // "Pourquoi ... ?" : départ haut puis descente, fin quasi plate
                    pitch = 1.16f - 0.20f * progress;
                    if (isLast) { pitch += 0.03f; length = 2; }
                }
                else
                {
                    // Question fermée : corps plat et bas, montée sur le dernier mot
                    pitch = 1.04f - 0.06f * progress;
                    if (IsInLastWord(text, index, phrase.End)) pitch += 0.08f;
                    if (isLast) { pitch += 0.06f; glide = 2; length = 2; }
                }

                if (phrase.ExclamationCount > 0) // "?!" : incrédulité
                {
                    pitch += 0.08f;
                    volume *= 1.15f;
                    effort = 2;
                }
            }
            else if (phrase.ExclamationCount > 0)
            {
                // Exclamation : plus haute, plus rapide, accents marqués, chute franche à la fin
                int power = Math.Min(phrase.ExclamationCount, 3);
                pitch = 1.14f + 0.03f * power - 0.14f * progress;
                effort = (power >= 2 || stressed) ? 2 : 1;
                volume *= 1f + 0.06f * power;
                if (!stressed) length = 0;
                if (isLast) { glide = -2; length = 2; }
            }
            else if (phrase.IsEllipsis)
            {
                // Suspension : la voix s'éteint, devient soufflée et traîne
                pitch = 1.04f - 0.16f * progress;
                volume *= 1f - 0.45f * progress;
                if (progress > 0.5f) effort = 0;
                if (progress > 0.6f) length = 2;
                if (isLast) { pitch -= 0.05f; glide = -1; length = 2; }
            }
            else
            {
                switch (phrase.Ending)
                {
                    case ',':
                    case ';':
                        // Continuation : la voix ne retombe pas, légère montée finale
                        pitch = 1.08f - 0.05f * progress;
                        if (isLast) { glide = 1; length = 2; }
                        break;
                    case ':':
                        // Annonce : maintenue, plate
                        pitch = 1.06f - 0.03f * progress;
                        if (isLast) length = 2;
                        break;
                    default:
                        // Affirmation : déclinaison naturelle, chute et allongement final
                        pitch = 1.10f - 0.20f * progress;
                        volume *= 1f - 0.18f * progress;
                        if (isLast) { pitch -= 0.03f; glide = -1; length = 2; }
                        break;
                }
            }

            // Accent de mot
            pitch *= stressed ? 1.05f : 1f;
            volume *= stressed ? 1.12f : 0.92f;

            GetWordBounds(text, index, out int wordStart, out int wordEnd);

            // MOT EN MAJUSCULES : cri
            if (IsUppercaseWord(text, index))
            {
                pitch *= 1.12f;
                effort = 2;
                length = Math.Max(length, 1);
            }
            else
            {
                // Nom propre au milieu d'une phrase : léger accent
                int firstLetter = phrase.Start;
                while (firstLetter < phrase.End && !char.IsLetter(text[firstLetter])) firstLetter++;
                if (stressed && wordStart > firstLetter && char.IsUpper(text[wordStart]))
                    pitch += 0.03f;
            }

            // *mot* ou _mot_ : emphase
            if (wordStart > 0 && text[wordStart - 1] is '*' or '_')
            {
                pitch += 0.06f;
                length = Math.Max(length, 2);
            }

            // (aparté entre parenthèses) : voix plus basse et douce
            if (IsInParentheses(text, index))
            {
                pitch *= 0.94f;
                effort = 0;
                volume *= 0.85f;
            }

            // Hésitation : euh, hum... → grave, plate, traînante
            if (IsHesitation(text, wordStart, wordEnd))
            {
                pitch = 0.94f;
                effort = 0;
                length = 2;
                glide = -1;
                volume = 0.9f;
            }

            // Syllabes médianes d'un mot (ni la première, ni la dernière) : courtes et centralisées,
            // comme dans la parole réelle où seules les syllabes porteuses d'accent sont articulées à fond.
            if (!stressed && length == 1 && effort == 1 && !IsLastSyllableOfWord(text, index, wordEnd))
                length = 0;

            // Voyelles étirées : "noooon", "heeeey"
            int run = CountRepeatedLetters(text, index);
            if (run >= 3)
                length = Math.Max(length, run >= 5 ? 3 : 2);

            return new Prosody(pitch, volume, effort, length, glide);
        }

        private static PhraseInfo AnalyzePhrase(string text, int index)
        {
            int start = 0;
            for (int i = index - 1; i >= 0; i--)
            {
                if (IsPhrasePunctuation(text[i]))
                {
                    start = i + 1;
                    break;
                }
            }

            int end = text.Length;
            for (int i = index + 1; i < text.Length; i++)
            {
                if (IsPhrasePunctuation(text[i]))
                {
                    end = i;
                    break;
                }
            }

            char ending = end < text.Length ? text[end] : '\0';
            bool question = false, ellipsis = false;
            int exclamations = 0, dots = 0;
            for (int i = end; i < text.Length && text[i] is '!' or '?' or '.' or '…'; i++)
            {
                switch (text[i])
                {
                    case '?': question = true; break;
                    case '!': exclamations++; break;
                    case '.': dots++; break;
                    case '…': ellipsis = true; break;
                }
            }
            ellipsis |= dots >= 2;

            bool wh = question && StartsWithQuestionWord(text, start, end);
            return new PhraseInfo(start, end, ending, question, wh, exclamations, ellipsis);
        }

        private static bool StartsWithQuestionWord(string text, int start, int end)
        {
            int s = start;
            while (s < end && !char.IsLetter(text[s])) s++;
            int e = s;
            while (e < end && char.IsLetter(text[e])) e++;
            return e > s && QuestionWords.Contains(text.Substring(s, e - s).ToLowerInvariant());
        }

        private static void GetSyllablePosition(string text, PhraseInfo phrase, int index,
            out int syllableIndex, out int syllableCount)
        {
            syllableIndex = 0;
            syllableCount = 0;
            for (int j = phrase.Start; j < phrase.End; j++)
            {
                if (!IsSyllableStart(text, j))
                    continue;
                if (j < index) syllableIndex++;
                syllableCount++;
            }
            syllableCount = Math.Max(syllableCount, 1);
        }

        private static bool IsSyllableStart(string text, int index)
        {
            if (GetVowelIndex(text[index]) < 0)
                return false;

            bool followsVowel = index > 0
                && !IsSyllabicCharacter(text[index])
                && !IsSyllabicCharacter(text[index - 1])
                && GetVowelIndex(text[index - 1]) >= 0;
            return !followsVowel;
        }

        private static bool IsLastSyllableOfWord(string text, int index, int wordEnd)
        {
            for (int j = index + 1; j < wordEnd; j++)
            {
                if (IsSyllableStart(text, j))
                    return false;
            }
            return true;
        }

        private static bool IsInLastWord(string text, int index, int phraseEnd)
        {
            bool seenSpace = false;
            for (int j = index; j < phraseEnd; j++)
            {
                if (char.IsWhiteSpace(text[j]))
                    seenSpace = true;
                else if (seenSpace && char.IsLetterOrDigit(text[j]))
                    return false;
            }
            return true;
        }

        private static bool IsInParentheses(string text, int index)
        {
            int open = text.LastIndexOf('(', index);
            return open >= 0 && open > text.LastIndexOf(')', index);
        }

        private static bool IsHesitation(string text, int wordStart, int wordEnd)
            => wordEnd - wordStart is >= 2 and <= 5
                && Hesitations.Contains(text.Substring(wordStart, wordEnd - wordStart).ToLowerInvariant());

        private static int CountRepeatedLetters(string text, int index)
        {
            char first = char.ToLowerInvariant(text[index]);
            int j = index;
            while (j + 1 < text.Length && char.ToLowerInvariant(text[j + 1]) == first) j++;
            return j - index + 1;
        }

        private static void GetWordBounds(string text, int index, out int start, out int end)
        {
            start = index;
            while (start > 0 && char.IsLetter(text[start - 1])) start--;
            end = index + 1;
            while (end < text.Length && char.IsLetter(text[end])) end++;
        }

        private static Sound CreateSound(SoundKey key, VoiceProfile profile)
        {
            byte[] waveData = CreateWave(key, profile);
            Wave wave = Raylib.LoadWaveFromMemory(".wav", waveData);
            if (!Raylib.IsWaveReady(wave))
            {
                Console.WriteLine("La génération de la voix du PNJ a produit une onde audio invalide.");
                return default;
            }

            try
            {
                return Raylib.LoadSoundFromWave(wave);
            }
            finally
            {
                Raylib.UnloadWave(wave);
            }
        }

        // ------------------------------------------------------------------
        //  Consonnes : une "fiche" par classe (fricative sifflante, occlusive labiale/alvéolaire/
        //  vélaire, voisée ou non, nasale, latérale, "r", glissante...). Chaque fiche décrit le
        //  bruit propre à la consonne, sa durée, et le point de départ des formants (locus) d'où
        //  la voix glisse vers la voyelle : c'est cette transition qui fait "entendre" p/t/k/b/d/g.
        // ------------------------------------------------------------------
        private const int CNone = 0, CS = 1, CZ = 2, CSh = 3, CZh = 4, CF = 5, CV = 6, CH = 7,
            CP = 8, CT = 9, CK = 10, CB = 11, CD = 12, CG = 13,
            CNasal = 14, CLateral = 15, CRhotic = 16, CGlide = 17;

        private enum ConsonantKind { None, Fricative, Stop, Nasal, Lateral, Rhotic, Glide, Aspirate }

        // Lead : durée (s) avant la voyelle. Noise* : bruit de la consonne (0 = calculé d'après la voyelle).
        // Locus* : formants de départ en Hz (0 = "pincement" calculé d'après la voyelle, cas des vélaires),
        // LocusWeight : part de la distance locus -> voyelle appliquée au départ. Vot : délai avant voisement.
        private readonly record struct ConsonantSpec(
            ConsonantKind Kind, bool Voiced, double Lead,
            double NoiseCenter, double NoiseBandwidth, double NoiseGain, double NoiseDuration,
            double LocusF1, double LocusF2, double LocusF3, double LocusWeight, double Vot);

        private static readonly ConsonantSpec[] ConsonantSpecs =
        {
            new(ConsonantKind.None,      false, 0.000,    0,    0, 0.00, 0.000,   0,    0,    0, 0.00, 0.000), // aucune
            new(ConsonantKind.Fricative, false, 0.042, 6400, 2800, 0.40, 0.058, 500, 1500, 2500, 0.20, 0.000), // s
            new(ConsonantKind.Fricative, true,  0.040, 6200, 2800, 0.26, 0.050, 300, 1400, 2400, 0.35, 0.000), // z
            new(ConsonantKind.Fricative, false, 0.050, 3500, 1800, 0.42, 0.066, 400, 1300, 2300, 0.25, 0.000), // ch / sh
            new(ConsonantKind.Fricative, true,  0.045, 3200, 1800, 0.28, 0.055, 300, 1300, 2300, 0.35, 0.000), // j / ge
            new(ConsonantKind.Fricative, false, 0.034, 5200, 5200, 0.13, 0.045, 450, 1300, 2400, 0.15, 0.000), // f / th
            new(ConsonantKind.Fricative, true,  0.032, 5000, 5000, 0.09, 0.040, 300, 1100, 2300, 0.35, 0.000), // v
            new(ConsonantKind.Aspirate,  false, 0.028,    0,    0, 0.00, 0.000, 500, 1500, 2500, 0.10, 0.000), // h
            new(ConsonantKind.Stop,      false, 0.030, 1300, 1600, 0.30, 0.012, 250,  900, 2200, 0.55, 0.018), // p
            new(ConsonantKind.Stop,      false, 0.030, 4600, 2400, 0.45, 0.012, 300, 1700, 2700, 0.45, 0.018), // t
            new(ConsonantKind.Stop,      false, 0.036,    0, 1000, 0.45, 0.014, 300,    0,    0, 0.50, 0.024), // k
            new(ConsonantKind.Stop,      true,  0.022, 1300, 1600, 0.16, 0.010, 250,  900, 2200, 0.55, 0.004), // b
            new(ConsonantKind.Stop,      true,  0.022, 4600, 2400, 0.22, 0.010, 300, 1700, 2700, 0.45, 0.004), // d
            new(ConsonantKind.Stop,      true,  0.024,    0, 1000, 0.22, 0.010, 300,    0,    0, 0.50, 0.004), // g
            new(ConsonantKind.Nasal,     true,  0.044,    0,    0, 0.00, 0.000, 250, 1000, 2300, 0.85, 0.000), // m / n
            new(ConsonantKind.Lateral,   true,  0.034,    0,    0, 0.00, 0.000, 360, 1250, 2800, 0.70, 0.000), // l
            new(ConsonantKind.Rhotic,    true,  0.036, 1600,  900, 0.12, 0.040, 420, 1350, 1900, 0.70, 0.000), // r
            new(ConsonantKind.Glide,     true,  0.030,    0,    0, 0.00, 0.000, 300,  700, 2200, 0.80, 0.000), // w
        };

        // Résonateur du second ordre (gain 1 en continu) : sert aux formants, au bruit des consonnes...
        private struct Resonator
        {
            public double A, B, C, Y1, Y2;

            public void Set(double frequency, double bandwidth)
            {
                frequency = Math.Clamp(frequency, 60.0, SampleRate * 0.46);
                double r = Math.Exp(-Math.PI * bandwidth / SampleRate);
                C = -r * r;
                B = 2.0 * r * Math.Cos(2.0 * Math.PI * frequency / SampleRate);
                A = 1.0 - B - C;
            }

            public double Process(double x)
            {
                double y = A * x + B * Y1 + C * Y2;
                Y2 = Y1;
                Y1 = y;
                return y;
            }
        }

        // Anti-résonance (notch) : creuse le spectre autour d'une fréquence. Sert à la nasalisation.
        private struct Notch
        {
            public double Cos2, R, X1, X2, Y1, Y2;

            public void Set(double frequency, double radius)
            {
                Cos2 = 2.0 * Math.Cos(2.0 * Math.PI * Math.Clamp(frequency, 80.0, SampleRate * 0.45) / SampleRate);
                R = radius;
            }

            public double Process(double x)
            {
                double y = x - Cos2 * X1 + X2 + R * Cos2 * Y1 - R * R * Y2;
                X2 = X1;
                X1 = x;
                Y2 = Y1;
                Y1 = y;
                return y;
            }
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        // Amplitude de la source voisée au temps t : une nasale/latérale/"r"/"w" est voisée dès le
        // début, une consonne voisée (b, d, z...) laisse filtrer un léger "murmure" de voisement
        // pendant la fermeture, une consonne sourde (p, t, s...) n'a de voisement qu'après elle.
        private static double GetVoicedAmplitude(in ConsonantSpec spec, double t)
        {
            switch (spec.Kind)
            {
                case ConsonantKind.Nasal:
                case ConsonantKind.Lateral:
                case ConsonantKind.Rhotic:
                case ConsonantKind.Glide:
                {
                    double level = spec.Kind switch
                    {
                        ConsonantKind.Nasal => 0.60,
                        ConsonantKind.Lateral => 0.75,
                        ConsonantKind.Rhotic => 0.65,
                        _ => 0.70
                    };
                    return SmoothStep(t / 0.008)
                        * (level + (1.0 - level) * SmoothStep((t - spec.Lead * 0.45) / (spec.Lead * 0.8 + 0.02)));
                }
                case ConsonantKind.Fricative when spec.Voiced:
                case ConsonantKind.Stop when spec.Voiced:
                {
                    double pre = spec.Kind == ConsonantKind.Fricative ? 0.16 : 0.10;
                    return t < spec.Lead
                        ? pre * SmoothStep(t / 0.006)
                        : pre + (1.0 - pre) * SmoothStep((t - spec.Lead) / 0.010);
                }
                case ConsonantKind.Fricative:
                    return SmoothStep((t - (spec.Lead - 0.008)) / 0.014);
                case ConsonantKind.Stop:
                    return SmoothStep((t - spec.Lead) / 0.008);
                case ConsonantKind.Aspirate:
                    return SmoothStep((t - spec.Lead * 0.8) / 0.012);
                default:
                    return SmoothStep(t / 0.014);
            }
        }

        /// <summary>
        /// Synthèse source-filtre :
        ///  - source glottale (jitter, shimmer, flottement lent, craquement "vocal fry" en fin de phrase,
        ///    micro-prosodie : la hauteur est légèrement perturbée par la consonne qui précède) ;
        ///  - bruit d'aspiration qui traverse le conduit vocal (souffle réaliste, "h", délai des p/t/k) ;
        ///  - conduit vocal en CASCADE de 5 résonateurs (comme un synthétiseur de Klatt) dont les formants
        ///    glissent d'un "locus" propre à la consonne vers la voyelle, avec réduction vocalique des
        ///    syllabes brèves et nasalisation (voyelle nasale, consonnes m/n, nasalité de la voix) ;
        ///  - consonnes : bruit filtré distinct par classe (s, ch, f, p, t, k, r...), voisement des b/d/g/z/v/j ;
        ///  - niveau normalisé sur le RMS de la voyelle (loudness intrinsèque par voyelle) + limiteur doux.
        /// L'effort modifie le timbre (doux/soufflé ↔ tendu/brillant), Length la durée, Glide la hauteur.
        /// </summary>
        private static byte[] CreateWave(SoundKey key, VoiceProfile profile)
        {
            ConsonantSpec spec = ConsonantSpecs[Math.Clamp(key.Consonant, 0, ConsonantSpecs.Length - 1)];
            bool sonorant = spec.Kind is ConsonantKind.Nasal or ConsonantKind.Lateral
                or ConsonantKind.Rhotic or ConsonantKind.Glide;
            double lead = spec.Lead;

            double vowelPart = (SoundDuration + (key.Take - (TakeCount - 1) * 0.5) * 0.012)
                * LengthScale[key.Length] * (key.Coda == 1 ? 1.08 : 1.0);
            double duration = lead + vowelPart;
            int frameCount = (int)(SampleRate * duration);
            double vowelDuration = Math.Max(0.01, duration - lead);

            byte[] wave = new byte[44 + frameCount * sizeof(short)];
            WriteTag(wave, 0, "RIFF");
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4, 4), wave.Length - 8);
            WriteTag(wave, 8, "WAVE");
            WriteTag(wave, 12, "fmt ");
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16, 4), 16);
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20, 2), 1);
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22, 2), 1);
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24, 4), SampleRate);
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28, 4), SampleRate * sizeof(short));
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32, 2), sizeof(short));
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34, 2), 16);
            WriteTag(wave, 36, "data");
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40, 4), frameCount * sizeof(short));

            // --- Effort vocal ---
            double breathiness = profile.Breathiness * (key.Effort == 0 ? 2.2 : key.Effort == 2 ? 0.7 : 1.0);
            double closeScale = key.Effort == 0 ? 1.35 : key.Effort == 2 ? 0.70 : 1.0;
            double f1Scale = key.Effort == 0 ? 0.96 : key.Effort == 2 ? 1.07 : 1.0;
            double bandwidthScale = key.Effort == 0 ? 1.30 : key.Effort == 2 ? 0.88 : 1.0;
            double brightness = key.Effort == 0 ? 0.60 : key.Effort == 2 ? 0.92 : 0.82; // 1 pôle passe-bas final

            // --- Formants : cible (voyelle, éventuellement réduite) et départ (locus de la consonne) ---
            (float t1, float t2, float t3) = VowelFormants[key.Vowel];
            (float n1, float n2, float n3) = VowelFormants[5];
            double scale = profile.FormantScale;
            double reduction = (key.Length == 0 ? 0.22 : 0.0) + (key.Effort == 0 ? 0.12 : 0.0);
            double[] target =
            {
                Lerp(t1, n1, reduction) * scale * f1Scale,
                Lerp(t2, n2, reduction) * scale,
                Lerp(t3, n3, reduction) * scale,
                3400.0 * scale,
                4500.0 * scale
            };

            double[] start = new double[5];
            if (spec.Kind == ConsonantKind.None)
            {
                // Pas de consonne : léger "flou" vers la voyelle neutre au tout début (comme avant)
                double[] neutral = { n1 * scale * f1Scale, n2 * scale, n3 * scale };
                for (int k = 0; k < 3; k++)
                    start[k] = target[k] + (neutral[k] - target[k]) * 0.20;
            }
            else
            {
                double locus2 = spec.LocusF2 > 0 ? spec.LocusF2 * scale : 0.5 * (target[1] + target[2]); // pincement vélaire
                double locus3 = spec.LocusF3 > 0 ? spec.LocusF3 * scale : target[2] * 0.97;
                double[] locus = { spec.LocusF1 * scale, locus2, locus3 };
                for (int k = 0; k < 3; k++)
                    start[k] = target[k] + (locus[k] - target[k]) * spec.LocusWeight;
            }
            start[3] = target[3];
            start[4] = target[4];

            double transition = spec.Kind == ConsonantKind.Stop ? 0.045 : 0.040;
            double[] baseBandwidth = { 85.0, 110.0, 165.0, 260.0, 330.0 };
            Resonator[] tract = new Resonator[5];

            // --- Nasalisation ---
            Notch nasalNotch = default;
            nasalNotch.Set(950.0 * scale, 0.93);
            double baseNasal = profile.Nasality * 0.5;

            // --- Bruit des consonnes ---
            Resonator consonantFilter = default;
            if (spec.Kind is ConsonantKind.Fricative or ConsonantKind.Stop or ConsonantKind.Rhotic)
            {
                double center = spec.NoiseCenter > 0
                    ? spec.NoiseCenter * (spec.Kind == ConsonantKind.Rhotic ? 1.0 : scale)
                    : target[1] * 1.05; // occlusive vélaire : l'éclat suit le F2 de la voyelle
                consonantFilter.Set(Math.Min(center, 9500.0), spec.NoiseBandwidth);
            }
            // Instant du relâchement (occlusives) : fermeture, puis éclat, puis délai de voisement
            double burstTime = Math.Max(0.002, lead - spec.Vot - 0.004);

            // --- Source glottale ---
            double openPhase = Math.Min(0.66, (0.38 + breathiness * 2.0) * (key.Effort == 2 ? 0.92 : key.Effort == 0 ? 1.10 : 1.0));
            double closePhase = (0.08 + (profile.SpectralTilt - 1.1) * 0.30) * closeScale;
            double phase = (key.Take * 0.37) % 1.0;
            double jitter = 0, shimmer = 1, previousFlow = 0, pulseGain = 1, creakPitch = 1;
            int pulseIndex = 0;
            double rough = profile.Roughness;

            // Perturbation de F0 par la consonne : sourde -> départ plus aigu, voisée -> légèrement plus grave
            double onsetPitchShift = spec.Kind == ConsonantKind.None || sonorant ? 0.0 : (spec.Voiced ? -0.04 : 0.07);
            bool creaky = key.Glide < 0 && key.Length >= 2 && key.Effort <= 1;
            double creakDepth = Math.Min(1.0, 0.45 + 0.35 * rough);

            double phaseOffsetA = key.Take * 1.7 + key.Voice * 0.9;
            double phaseOffsetB = key.Take * 2.3 + key.Vowel * 0.6;

            uint noiseState = unchecked((uint)(key.Voice * 73856093 ^ key.Vowel * 19349663 ^ key.Pitch * 83492791
                ^ key.Take * 1640531513 ^ key.Consonant * 2654435761u ^ key.Coda * 668265263
                ^ key.Effort * 40503 ^ key.Length * 9973 ^ (key.Glide + 2) * 7919));
            if (noiseState == 0) noiseState = 1;

            double[] voicedSamples = new double[frameCount];
            double[] consonantSamples = new double[frameCount];
            double consonantPeak = 0, previousNoise = 0, previousConsonantNoise = 0;
            double smoothed = 0, dcIn = 0, dcOut = 0;

            for (int i = 0; i < frameCount; i++)
            {
                double time = i / (double)SampleRate;
                double vowelTime = Math.Max(0.0, time - lead);
                double progress = Math.Clamp(vowelTime / vowelDuration, 0.0, 1.0);

                // ---------- Hauteur ----------
                double creak = creaky ? SmoothStep((progress - 0.55) / 0.40) * creakDepth : 0.0;

                // Contour : bosse naturelle + pente propre à la prise + mouvement expressif (Glide)
                double contour = 1.0
                    + 0.035 * Math.Sin(Math.PI * progress)
                    + (key.Take - (TakeCount - 1) * 0.5) * 0.020 * (progress - 0.4)
                    + key.Glide * 0.055 * (2.0 * progress - 1.0);
                // Flottement lent de la hauteur (remplace le vibrato, qui n'existe pas dans la parole)
                double flutter = 1.0
                    + 0.0035 * Math.Sin(2.0 * Math.PI * 3.1 * time + phaseOffsetA)
                    + 0.0022 * Math.Sin(2.0 * Math.PI * 7.3 * time + phaseOffsetB);
                double microProsody = 1.0 + onsetPitchShift * Math.Exp(-vowelTime / 0.025);
                double f0 = key.Pitch * VowelIntrinsicPitch[key.Vowel] * contour * flutter * microProsody
                    * (1.0 + jitter) * creakPitch;

                phase += f0 / SampleRate;
                if (phase >= 1.0)
                {
                    phase -= 1.0;
                    pulseIndex++;
                    jitter = jitter * 0.4 + NextNoise(ref noiseState) * 0.007 * rough * (1.0 + 3.0 * creak);
                    shimmer = 1.0 + NextNoise(ref noiseState) * 0.06 * rough;
                    // Craquement : périodes alternées (une impulsion sur deux plus faible et plus grave)
                    bool odd = (pulseIndex & 1) == 1;
                    pulseGain = odd ? 1.0 - 0.45 * creak : 1.0;
                    creakPitch = 1.0 - 0.20 * creak - (odd ? 0.06 * creak : 0.0);
                }

                // ---------- Source ----------
                double flow = GlottalFlow(phase, openPhase, closePhase);
                double glottalDerivative = (flow - previousFlow) * (SampleRate / f0);
                previousFlow = flow;

                double voicedAmplitude = GetVoicedAmplitude(spec, time);
                double aspiration = NextNoise(ref noiseState) * breathiness * 6.0 * (0.25 + flow);
                double source = (glottalDerivative * shimmer * pulseGain + aspiration) * voicedAmplitude;

                // Bruit d'aspiration "libre" (h, délai de voisement des p/t/k) qui traverse le conduit vocal
                double aspirationEnvelope = 0, aspirationGain = 0;
                if (spec.Kind == ConsonantKind.Stop)
                {
                    aspirationEnvelope = SmoothStep((time - burstTime) / 0.004) * (1.0 - SmoothStep((time - lead) / 0.014));
                    aspirationGain = spec.Voiced ? 0.25 : (key.Consonant == CK ? 1.6 : 1.4);
                }
                else if (spec.Kind == ConsonantKind.Aspirate)
                {
                    aspirationEnvelope = SmoothStep(time / 0.006) * (1.0 - SmoothStep((time - lead) / 0.018));
                    aspirationGain = 0.9;
                }
                if (aspirationEnvelope > 0)
                    source += NextNoise(ref noiseState) * aspirationGain * aspirationEnvelope;

                // ---------- Conduit vocal (cascade) ----------
                double nasalAmount = baseNasal;
                if (spec.Kind == ConsonantKind.Nasal)
                    nasalAmount += 1.0 - SmoothStep((time - lead * 0.7) / 0.030);
                if (key.Coda == 1)
                    nasalAmount += 0.85 * SmoothStep((progress - 0.50) / 0.35);
                nasalAmount = Math.Clamp(nasalAmount, 0.0, 1.0);

                if ((i & 7) == 0)
                {
                    double mix = sonorant
                        ? SmoothStep(time / (lead + 0.045))
                        : SmoothStep((time - (lead - 0.004)) / transition);
                    for (int k = 0; k < 5; k++)
                    {
                        double frequency = start[k] + (target[k] - start[k]) * mix;
                        if (k < 3)
                            frequency *= 1.0 + 0.010 * Math.Sin(2.0 * Math.PI * (2.6 + 0.4 * k) * time + phaseOffsetA + k);
                        double bandwidth = baseBandwidth[k] * bandwidthScale * (k == 0 ? 1.0 + 1.0 * nasalAmount : 1.0);
                        tract[k].Set(frequency, bandwidth);
                    }
                }

                double oral = source;
                for (int k = 0; k < 5; k++)
                    oral = tract[k].Process(oral);

                double voiced = Lerp(oral, nasalNotch.Process(oral), nasalAmount);

                double lowpass = brightness - 0.12 * nasalAmount;
                smoothed += lowpass * (voiced - smoothed);
                voiced = smoothed;

                // retire la composante continue / les infra-graves
                double blocked = voiced - dcIn + 0.995 * dcOut;
                dcIn = voiced;
                dcOut = blocked;
                voiced = blocked;

                double release = SmoothStep((duration - time) / 0.050);
                double body = 1.0 - 0.22 * progress;
                voiced *= release * body;

                voicedSamples[i] = voiced;

                // ---------- Consonne (bruit filtré distinct par classe) ----------
                double noise = NextNoise(ref noiseState);
                double highPassedNoise = noise - previousNoise;
                previousNoise = noise;
                double consonant = 0;

                if (spec.Kind == ConsonantKind.Fricative && time < spec.NoiseDuration)
                {
                    double envelope = SmoothStep(time / 0.012)
                        * (1.0 - SmoothStep((time - (spec.NoiseDuration - 0.022)) / 0.022));
                    double bandNoise = consonantFilter.Process(highPassedNoise);
                    // fricative voisée : le bruit est haché par les pulsations glottales
                    double voicing = spec.Voiced ? 0.45 + 0.55 * flow : 1.0;
                    consonant = bandNoise * envelope * voicing;
                }
                else if (spec.Kind == ConsonantKind.Stop && time >= burstTime)
                {
                    double envelope = SmoothStep((time - burstTime) / 0.0007)
                        * Math.Exp(-(time - burstTime) / (spec.NoiseDuration * 0.45));
                    consonant = consonantFilter.Process(highPassedNoise) * envelope;
                }
                else if (spec.Kind == ConsonantKind.Rhotic)
                {
                    // "r" français : friction voisée (buzz grave haché par la glotte)
                    double envelope = SmoothStep(time / 0.010) * (1.0 - SmoothStep((time - lead) / 0.025));
                    consonant = consonantFilter.Process(noise - previousConsonantNoise) * envelope * (0.4 + 0.6 * flow);
                }
                previousConsonantNoise = noise;

                consonantSamples[i] = consonant;
                consonantPeak = Math.Max(consonantPeak, Math.Abs(consonant));
            }

            // ---------- Niveaux : RMS de la partie voyelle (stable d'une syllabe à l'autre) ----------
            int regionStart = Math.Min(frameCount - 1, (int)((lead + 0.020) * SampleRate));
            int regionEnd = Math.Max(regionStart + 1, frameCount - (int)(0.050 * SampleRate));
            regionEnd = Math.Min(regionEnd, frameCount);
            double sumSquares = 0, vowelPeak = 0;
            for (int i = regionStart; i < regionEnd; i++)
            {
                sumSquares += voicedSamples[i] * voicedSamples[i];
                vowelPeak = Math.Max(vowelPeak, Math.Abs(voicedSamples[i]));
            }
            double rms = Math.Sqrt(sumSquares / Math.Max(1, regionEnd - regionStart));
            double voicedScale = rms > 1e-9 ? TargetVoicedRms * VowelLoudness[key.Vowel] / rms : 0.0;
            vowelPeak *= voicedScale;

            double consonantEffort = key.Effort == 2 ? 1.25 : key.Effort == 0 ? 0.70 : 1.0;
            double consonantScale = consonantPeak > 1e-9
                ? spec.NoiseGain * consonantEffort * Math.Max(vowelPeak, 0.2) / consonantPeak
                : 0.0;

            for (int i = 0; i < frameCount; i++)
            {
                double mixed = voicedSamples[i] * voicedScale + consonantSamples[i] * consonantScale;
                // limiteur doux (écrête en douceur au lieu de normaliser sur le pic)
                double limited = Math.Tanh(mixed * 1.1) * 0.88;
                short pcm = (short)Math.Clamp(
                    (int)Math.Round(limited * short.MaxValue),
                    short.MinValue,
                    short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(44 + i * sizeof(short), sizeof(short)), pcm);
            }

            return wave;
        }

        private static double GlottalFlow(double phase, double openPhase, double closePhase)
        {
            if (phase < openPhase)
                return 0.5 * (1.0 - Math.Cos(Math.PI * phase / openPhase));

            double closing = phase - openPhase;
            if (closing < closePhase)
                return Math.Cos(0.5 * Math.PI * closing / closePhase);

            return 0.0;
        }

        private static double SmoothStep(double x)
        {
            x = Math.Clamp(x, 0.0, 1.0);
            return x * x * (3.0 - 2.0 * x);
        }

        // Classe de la consonne qui précède la voyelle `index` (digrammes et lettres douces compris :
        // "ch" -> sh, "ph" -> f, "gn" -> nasale, "ce/ci" -> s, "ge/gi" -> j, "s" entre voyelles -> z...).
        private static int GetConsonant(string text, int index)
        {
            if (index == 0)
                return CNone;

            char previous = char.ToLowerInvariant(text[index - 1]);
            if (!char.IsLetter(previous) || GetVowelIndex(previous) >= 0)
                return CNone;

            char before = index >= 2 ? char.ToLowerInvariant(text[index - 2]) : '\0';
            bool softVowel = GetVowelIndex(text[index]) is 1 or 2; // e, i : adoucit c et g
            bool betweenVowels = before != '\0' && GetVowelIndex(before) >= 0;

            if (previous == 'h')
            {
                if (before is 'c' or 's') return CSh;
                if (before is 'p' or 't') return CF;
                return CH;
            }
            if (previous == 'n' && before == 'g')
                return CNasal;

            return previous switch
            {
                's' => betweenVowels ? CZ : CS,
                'z' or 'з' => CZ,
                'ç' or 'x' or 'ß' or 'с' or 'ц' => CS,
                'ш' or 'щ' or 'ч' => CSh,
                'j' or 'ж' => CZh,
                'f' or 'ф' => CF,
                'v' or 'в' => CV,
                'х' => CH,
                'p' or 'п' => CP,
                't' or 'т' => CT,
                'c' => softVowel ? CS : CK,
                'k' or 'q' or 'к' => CK,
                'b' or 'б' => CB,
                'd' or 'д' => CD,
                'g' => softVowel ? CZh : CG,
                'г' => CG,
                'm' or 'n' or 'ñ' or 'м' or 'н' => CNasal,
                'r' or 'р' => CRhotic,
                'w' or 'й' or 'ł' => CGlide,
                _ => CLateral
            };
        }

        // 1 si la syllabe se termine par une nasale (n/m) qui n'est pas l'attaque de la syllabe suivante :
        // la voyelle est alors nasalisée ("on", "an", "bonjour", "maintenant"...).
        private static int GetCoda(string text, int index)
        {
            if (IsSyllabicCharacter(text[index]))
                return 0;

            int j = index + 1;
            while (j < text.Length && GetVowelIndex(text[j]) >= 0 && !IsSyllabicCharacter(text[j]))
                j++;
            if (j >= text.Length)
                return 0;

            char c = char.ToLowerInvariant(text[j]);
            if (c is not ('n' or 'm' or 'н' or 'м'))
                return 0;

            int next = j + 1;
            if (next < text.Length && (GetVowelIndex(text[next]) >= 0 || char.ToLowerInvariant(text[next]) == 'h'))
                return 0;

            return 1;
        }

        private static bool IsFirstSyllableOfWord(string text, int index)
        {
            for (int j = index - 1; j >= 0 && char.IsLetter(text[j]); j--)
            {
                if (GetVowelIndex(text[j]) >= 0)
                    return false;
            }
            return true;
        }

        private static int GetVowelIndex(char character)
        {
            return char.ToLowerInvariant(character) switch
            {
                'a' or 'à' or 'â' or 'ä' or 'å' or 'ą' or 'ã' or 'а' or 'я' => 0,
                'e' or 'é' or 'è' or 'ê' or 'ë' or 'ę' or 'е' or 'ё' or 'э' => 1,
                'i' or 'î' or 'ï' or 'y' or 'ÿ' or 'и' or 'ы' => 2,
                'o' or 'ô' or 'ö' or 'ø' or 'õ' or 'о' => 3,
                'u' or 'ù' or 'û' or 'ü' or 'у' or 'ю' => 4,
                'æ' or 'œ' => 5,
                _ when IsSyllabicCharacter(character) => 5,
                _ => -1
            };
        }

        private static bool IsSyllabicCharacter(char character)
            => character is >= '\u3040' and <= '\u30FF'
                or >= '\u31F0' and <= '\u31FF'
                or >= '\u3400' and <= '\u4DBF'
                or >= '\u4E00' and <= '\u9FFF'
                or >= '\uAC00' and <= '\uD7AF';

        private static bool IsPhrasePunctuation(char character)
            => character is ',' or '.' or '!' or '?' or ';' or ':' or '…';

        private static bool IsUppercaseWord(string text, int index)
        {
            GetWordBounds(text, index, out int start, out int end);

            int letterCount = 0;
            for (int i = start; i < end; i++)
            {
                letterCount++;
                if (!char.IsUpper(text[i])) return false;
            }
            return letterCount >= 2;
        }

        private static double NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state / (double)uint.MaxValue) * 2.0 - 1.0;
        }

        private static void WriteTag(byte[] buffer, int offset, string tag)
            => Encoding.ASCII.GetBytes(tag).CopyTo(buffer, offset);
    }
}
