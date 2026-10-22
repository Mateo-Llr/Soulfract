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

        // Onset  : consonne qui précède la voyelle (0 aucune, 1 fricative, 2 occlusive, 3 nasale/liquide)
        // Effort : 0 doux/soufflé, 1 normal, 2 tendu/crié
        // Length : 0 bref, 1 normal, 2 long, 3 très long (voyelles étirées)
        // Glide  : -2..+2, mouvement de hauteur DANS la syllabe (descente / montée)
        private readonly record struct SoundKey(
            int Voice, int Vowel, int Pitch, int Take, int Onset, int Effort, int Length, int Glide);
        private readonly record struct VoiceProfile(
            float BasePitch,
            float FormantScale,
            float SpectralTilt,
            float Breathiness);
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

        private static readonly VoiceProfile[] Profiles =
        {
            new(112f, 0.91f, 1.42f, 0.050f), new(120f, 1.08f, 1.24f, 0.036f),
            new(128f, 0.97f, 1.34f, 0.044f), new(136f, 1.14f, 1.18f, 0.032f),
            new(144f, 1.02f, 1.30f, 0.046f), new(152f, 0.94f, 1.48f, 0.055f),
            new(160f, 1.11f, 1.20f, 0.035f), new(170f, 1.00f, 1.38f, 0.042f)
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

                // Mélodie vivante : dérive lente + micro-variation par syllabe
                _melody = _melody * 0.82f + (Rng.NextSingle() - 0.5f) * 0.10f;
                float pitch = profile.BasePitch * prosody.Pitch * (1f + Math.Clamp(_melody, -0.07f, 0.07f));
                int pitchBucket = Math.Clamp(
                    (int)MathF.Round(pitch / PitchStep) * PitchStep,
                    72,
                    270);

                SoundKey key = new(voice, vowel, pitchBucket, _nextTake, GetOnset(text, i),
                    prosody.Effort, prosody.Length, prosody.Glide);
                _nextTake = (_nextTake + 1) % TakeCount;
                if (!Sounds.TryGetValue(key, out Sound sound))
                {
                    sound = CreateSound(key, profile);
                    if (!Raylib.IsSoundReady(sound))
                    {
                        Console.WriteLine($"Impossible de générer la voix du PNJ (voyelle {vowel}, hauteur {pitchBucket} Hz).");
                        continue;
                    }
                    Sounds.Add(key, sound);
                }

                float volume = 0.19f * EffortVolume[prosody.Effort] * prosody.Volume
                    * (0.9f + Rng.NextSingle() * 0.2f);

                Raylib.SetSoundVolume(sound, Math.Clamp(volume, 0f, 0.8f));
                Raylib.SetSoundPitch(sound, 1f + (Rng.NextSingle() - 0.5f) * 0.06f);
                Raylib.PlaySound(sound);
                if (!PlayingSounds.Contains(sound))
                    PlayingSounds.Add(sound);
            }

            processedCharacters = visible;
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

        /// <summary>
        /// Synthèse source-filtre : onde glottale (jitter, shimmer, vibrato, souffle) passée dans des
        /// résonateurs formantiques qui glissent vers la voyelle cible, précédée d'un bruit de consonne.
        /// L'effort modifie le timbre (doux/soufflé ↔ tendu/brillant), Length la durée, Glide la hauteur.
        /// </summary>
        private static byte[] CreateWave(SoundKey key, VoiceProfile profile)
        {
            double vowelStart = key.Onset switch { 1 => 0.030, 2 => 0.012, _ => 0.0 };
            double consonantExtra = key.Onset == 1 ? 0.030 : key.Onset == 2 ? 0.012 : 0.0;
            double duration = consonantExtra
                + (SoundDuration + (key.Take - (TakeCount - 1) * 0.5) * 0.012) * LengthScale[key.Length];
            int frameCount = (int)(SampleRate * duration);

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

            // --- Formants : départ (coarticulation avec la consonne) -> cible ---
            (float t1, float t2, float t3) = VowelFormants[key.Vowel];
            (float n1, float n2, float n3) = VowelFormants[5];
            double scale = profile.FormantScale;
            double[] target = { t1 * scale * f1Scale, t2 * scale, t3 * scale, 3400.0 * scale };
            double[] neutral = { n1 * scale * f1Scale, n2 * scale, n3 * scale, 3400.0 * scale };
            double[] start = new double[4];
            double[] startMultiplier = key.Onset switch
            {
                2 => new[] { 0.55, 0.85, 0.95, 1.0 },
                3 => new[] { 0.45, 0.80, 0.95, 1.0 },
                _ => new[] { 1.0, 1.0, 1.0, 1.0 }
            };
            for (int k = 0; k < 4; k++)
            {
                start[k] = target[k] * startMultiplier[k];
                if (key.Onset <= 1)
                    start[k] += (neutral[k] - target[k]) * 0.20;
            }

            double transition = key.Onset >= 2 ? 0.050 : 0.040;
            double[] bandwidths = { 85.0, 110.0, 165.0, 260.0 };
            double[] formantGains = { 1.0, 0.60, 0.32, 0.12 };
            double[] y1 = new double[4];
            double[] y2 = new double[4];

            // --- Source glottale ---
            double openPhase = 0.38 + breathiness * 2.0;
            double closePhase = (0.08 + (profile.SpectralTilt - 1.1) * 0.30) * closeScale;
            double phase = (key.Take * 0.37) % 1.0;
            double jitter = 0, shimmer = 1, previousFlow = 0, smoothed = 0;
            double sourceScale = SampleRate / (double)key.Pitch;

            uint noiseState = unchecked((uint)(key.Voice * 73856093 ^ key.Vowel * 19349663 ^ key.Pitch * 83492791
                ^ key.Take * 1640531513 ^ key.Onset * 2654435761u
                ^ key.Effort * 40503 ^ key.Length * 9973 ^ (key.Glide + 2) * 7919));
            if (noiseState == 0) noiseState = 1;

            double[] voicedSamples = new double[frameCount];
            double[] consonantSamples = new double[frameCount];
            double voicedPeak = 0, consonantPeak = 0, previousNoise = 0;
            double vowelDuration = Math.Max(0.01, duration - vowelStart);

            for (int i = 0; i < frameCount; i++)
            {
                double time = i / (double)SampleRate;
                double vowelTime = time - vowelStart;

                // ---------- Partie voisée ----------
                double voiced = 0;
                if (vowelTime >= 0)
                {
                    double progress = Math.Clamp(vowelTime / vowelDuration, 0.0, 1.0);

                    // Contour : bosse naturelle + pente propre à la prise + mouvement expressif (Glide) + vibrato
                    double contour = 1.0
                        + 0.035 * Math.Sin(Math.PI * progress)
                        + (key.Take - (TakeCount - 1) * 0.5) * 0.020 * (progress - 0.4)
                        + key.Glide * 0.055 * (2.0 * progress - 1.0);
                    double vibrato = 1.0 + 0.004 * Math.Sin(2.0 * Math.PI * 5.2 * time + key.Take);
                    double f0 = key.Pitch * contour * vibrato * (1.0 + jitter);

                    phase += f0 / SampleRate;
                    if (phase >= 1.0)
                    {
                        phase -= 1.0;
                        jitter = jitter * 0.4 + NextNoise(ref noiseState) * 0.007;
                        shimmer = 1.0 + NextNoise(ref noiseState) * 0.06;
                    }

                    double flow = GlottalFlow(phase, openPhase, closePhase);
                    double glottalDerivative = (flow - previousFlow) * sourceScale;
                    previousFlow = flow;

                    double aspiration = NextNoise(ref noiseState) * breathiness * 6.0 * (0.25 + flow);
                    double source = glottalDerivative * shimmer + aspiration;

                    double mix = SmoothStep(vowelTime / transition);
                    for (int k = 0; k < 4; k++)
                    {
                        double frequency = start[k] + (target[k] - start[k]) * mix;
                        double r = Math.Exp(-Math.PI * bandwidths[k] / SampleRate);
                        double c = -r * r;
                        double b = 2.0 * r * Math.Cos(2.0 * Math.PI * frequency / SampleRate);
                        double a = 1.0 - b - c;
                        double y = a * source + b * y1[k] + c * y2[k];
                        y2[k] = y1[k];
                        y1[k] = y;
                        voiced += formantGains[k] * y;
                    }

                    smoothed += 0.65 * (voiced - smoothed);
                    voiced = smoothed;

                    double attackLength = key.Onset == 3 ? 0.030 : 0.014;
                    double attack = SmoothStep(vowelTime / attackLength);
                    double release = SmoothStep((duration - time) / 0.050);
                    double body = 1.0 - 0.22 * progress;
                    voiced *= attack * release * body;
                    if (key.Onset == 3)
                        voiced *= 0.55 + 0.45 * SmoothStep(vowelTime / 0.05);
                }

                voicedSamples[i] = voiced;
                voicedPeak = Math.Max(voicedPeak, Math.Abs(voiced));

                // ---------- Consonne ----------
                double consonant = 0;
                double noise = NextNoise(ref noiseState);
                double highPassedNoise = noise - previousNoise;
                previousNoise = noise;

                if (key.Onset == 1 && time < 0.055)
                    consonant = highPassedNoise * Math.Sin(Math.PI * Math.Clamp(time / 0.055, 0.0, 1.0));
                else if (key.Onset == 2 && time >= 0.004 && time < 0.030)
                    consonant = highPassedNoise * Math.Exp(-(time - 0.004) / 0.004);

                consonantSamples[i] = consonant;
                consonantPeak = Math.Max(consonantPeak, Math.Abs(consonant));
            }

            double voicedNormalization = voicedPeak > 0 ? 1.0 / voicedPeak : 0;
            double consonantLevel = key.Onset == 1 ? 0.30 : key.Onset == 2 ? 0.45 : 0.0;
            double consonantNormalization = consonantPeak > 0 ? consonantLevel / consonantPeak : 0;

            double peak = 0;
            double[] mixed = new double[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                mixed[i] = voicedSamples[i] * voicedNormalization + consonantSamples[i] * consonantNormalization;
                peak = Math.Max(peak, Math.Abs(mixed[i]));
            }

            double normalization = peak > 0 ? 0.72 / peak : 0;
            for (int i = 0; i < frameCount; i++)
            {
                short pcm = (short)Math.Clamp(
                    (int)Math.Round(mixed[i] * normalization * short.MaxValue),
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

        private static int GetOnset(string text, int index)
        {
            if (index == 0)
                return 0;

            char previous = char.ToLowerInvariant(text[index - 1]);
            if (!char.IsLetter(previous) || GetVowelIndex(previous) >= 0)
                return 0;

            return previous switch
            {
                's' or 'z' or 'f' or 'v' or 'h' or 'j' or 'x' or 'ß'
                    or 'ш' or 'щ' or 'ж' or 'с' or 'з' or 'ф' or 'в' or 'х' => 1,
                'p' or 't' or 'k' or 'b' or 'd' or 'g' or 'c' or 'q'
                    or 'п' or 'т' or 'к' or 'б' or 'д' or 'г' or 'ц' or 'ч' => 2,
                _ => 3
            };
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
