// MusicPlayer.cs - Version MIDI + SoundFont (lecture de .mid/.midi via une banque .sf2)
//
// Dépendances NuGet requises :
//   dotnet add package MeltySynth
//   dotnet add package Melanchall.DryWetMidi
//
// MeltySynth  -> synthèse audio en temps réel du MIDI à partir d'une SoundFont (.sf2)
// DryWetMidi  -> lecture "haut niveau" du fichier .mid (notes, durées, tempo) pour le visualiseur
//
// Un fichier SoundFont (.sf2) doit être présent (par défaut "assets/soundfonts/default.sf2").
// Les musiques doivent être des fichiers .mid ou .midi placés dans "assets/musics".
using Raylib_cs;
using System.Numerics;
using MeltySynth;
using Melanchall.DryWetMidi.Interaction;
using SynthMidiFile = MeltySynth.MidiFile;
using DryWetMidiFile = Melanchall.DryWetMidi.Core.MidiFile;

namespace Soulfract
{
    // Classe publique pour exposer les données des notes au visualiseur
    public class NoteDisplayInfo
    {
        public float StartTime { get; set; }
        public float Duration { get; set; }
        public int Pitch { get; set; }
        public int Velocity { get; set; }
        public int InstrumentIndex { get; set; } // canal MIDI (0-15), utilisé pour la couleur du visualiseur
    }

    public static class MusicPlayer
    {
        // --- Configuration audio ---
        private const int SAMPLE_RATE = 44100;
        private const int CHANNELS = 2;
        private const int RENDER_FRAMES = 2048; // nb d'échantillons (par canal) rendus à chaque remplissage
        private const float MIDI_MASTER_GAIN = 2.2f; // augmente le niveau de sortie du synthé MIDI

        // --- Etat SoundFont / Synthé ---
        private static SoundFont? _soundFont = null;
        private static Synthesizer? _synthesizer = null;
        private static bool _initialized = false;
        private static string _soundFontPath = "assets/soundfonts/default.sf2";

        // --- Flux audio Raylib ---
        private static AudioStream _stream;
        private static bool _streamReady = false;
        private static short[] _renderBuffer = new short[RENDER_FRAMES * CHANNELS];

        // --- MIDI en cours ---
        private static SynthMidiFile? _midiFile = null;           // MeltySynth.MidiFile (pour la lecture/synthèse)
        private static MidiFileSequencer? _sequencer = null;      // MeltySynth.MidiFileSequencer

        private static List<NoteDisplayInfo> _events = new List<NoteDisplayInfo>();
        private static readonly object _eventsLock = new object();

        private static bool _isPlaying = false;
        private static bool _isPaused = false;
        private static long _samplesRendered = 0;
        private static float _currentPlayTime = 0f;
        private static float _totalDuration = 0f;
        private static int _originalTempo = 120;

        // --- Propriétés publiques (inchangées pour compatibilité avec InstrumentUI.cs) ---
        public static float CurrentPlayTime => _currentPlayTime;
        public static float TotalDuration => _totalDuration;
        public static bool IsPlaying => _isPlaying && !_isPaused;
        public static bool IsPaused => _isPaused;

        /// <summary>
        /// A appeler une seule fois, après Raylib.InitAudioDevice(), pour charger la SoundFont
        /// et préparer le flux audio de streaming.
        /// </summary>
        public static void Init(string soundFontPath = "assets/soundfonts/default.sf2")
        {
            if (_initialized) return;
            _soundFontPath = soundFontPath;

            if (!File.Exists(_soundFontPath))
            {
                Console.WriteLine($" SoundFont introuvable : {_soundFontPath}");
                Console.WriteLine($"   Chemin absolu recherché : {Path.GetFullPath(_soundFontPath)}");
                Console.WriteLine($"   Répertoire de travail actuel : {Directory.GetCurrentDirectory()}");
                return;
            }

            try
            {
                _soundFont = new SoundFont(_soundFontPath);
                var settings = new SynthesizerSettings(SAMPLE_RATE);
                _synthesizer = new Synthesizer(_soundFont, settings)
                {
                    MasterVolume = MIDI_MASTER_GAIN
                };
                _sequencer = new MidiFileSequencer(_synthesizer);

                Raylib.SetAudioStreamBufferSizeDefault(RENDER_FRAMES);
                _stream = Raylib.LoadAudioStream(SAMPLE_RATE, 16, CHANNELS);
                Raylib.SetAudioStreamVolume(_stream, 1f);
                _streamReady = true;

                _initialized = true;
                Console.WriteLine($" SoundFont chargée : {_soundFontPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erreur de chargement de la SoundFont : {ex.Message}");
            }
        }

        public static List<NoteDisplayInfo> GetNotesForDisplay()
        {
            lock (_eventsLock)
            {
                return _events.Select(e => new NoteDisplayInfo
                {
                    StartTime = e.StartTime,
                    Duration = e.Duration,
                    Pitch = e.Pitch,
                    Velocity = e.Velocity,
                    InstrumentIndex = e.InstrumentIndex
                }).ToList();
            }
        }

        public static List<string> GetAvailableMusicNames()
        {
            if (!Directory.Exists("assets/musics")) return new List<string>();
            return Directory.GetFiles("assets/musics", "*.mid")
                            .Concat(Directory.GetFiles("assets/musics", "*.midi"))
                            .Select(f => Path.GetFileNameWithoutExtension(f))
                            .Distinct()
                            .ToList();
        }

        public static bool LoadMusic(string midiPath)
        {
            if (!_initialized)
            {
                Console.WriteLine(" MusicPlayer non initialisé (appelez MusicPlayer.Init() après InitAudioDevice()).");
                return false;
            }

            if (!File.Exists(midiPath))
            {
                Console.WriteLine($"Fichier MIDI introuvable : {midiPath}");
                return false;
            }

            try
            {
                Stop();

                byte[] rawBytes = File.ReadAllBytes(midiPath);
                byte[] sanitizedBytes = SanitizeMidiRunningStatus(rawBytes);

                var readingSettings = new Melanchall.DryWetMidi.Core.ReadingSettings
                {
                    // Certains fichiers .mid exportés par des DAW/outils tiers contiennent aussi des
                    // octets de données (vélocité, pitch, etc.) légèrement hors norme (>127). Plutôt
                    // que de rejeter le fichier, on ramène la valeur dans les limites valides (0-127).
                    InvalidChannelEventParameterValuePolicy =
                        Melanchall.DryWetMidi.Core.InvalidChannelEventParameterValuePolicy.SnapToLimits,
                    NoHeaderChunkPolicy = Melanchall.DryWetMidi.Core.NoHeaderChunkPolicy.Ignore,
                    MissedEndOfTrackPolicy = Melanchall.DryWetMidi.Core.MissedEndOfTrackPolicy.Ignore,
                    InvalidChunkSizePolicy = Melanchall.DryWetMidi.Core.InvalidChunkSizePolicy.Ignore,
                    NotEnoughBytesPolicy = Melanchall.DryWetMidi.Core.NotEnoughBytesPolicy.Ignore,
                    UnknownChannelEventPolicy =
                        Melanchall.DryWetMidi.Core.UnknownChannelEventPolicy.SkipStatusByteAndTwoDataBytes
                };

                DryWetMidiFile dryWetFile;
                using (var msDry = new MemoryStream(sanitizedBytes, writable: false))
                {
                    dryWetFile = DryWetMidiFile.Read(msDry, readingSettings);
                }
                var tempoMap = dryWetFile.GetTempoMap();

                sanitizedBytes = SerializeDryWetMidiFile(dryWetFile);
                using (var msSynth = new MemoryStream(sanitizedBytes, writable: false))
                {
                    _midiFile = new SynthMidiFile(msSynth);
                }

                var initialTempo = tempoMap.GetTempoAtTime((MidiTimeSpan)0);
                _originalTempo = initialTempo != null
                    ? (int)Math.Round(initialTempo.BeatsPerMinute)
                    : 120;

                var notes = dryWetFile.GetNotes();
                var newEvents = new List<NoteDisplayInfo>();
                foreach (var note in notes)
                {
                    var start = note.TimeAs<MetricTimeSpan>(tempoMap);
                    var length = note.LengthAs<MetricTimeSpan>(tempoMap);
                    newEvents.Add(new NoteDisplayInfo
                    {
                        StartTime = (float)start.TotalMicroseconds / 1_000_000f,
                        Duration = (float)length.TotalMicroseconds / 1_000_000f,
                        Pitch = note.NoteNumber,
                        Velocity = note.Velocity,
                        InstrumentIndex = note.Channel
                    });
                }
                newEvents = newEvents.OrderBy(e => e.StartTime).ToList();

                var totalMetric = dryWetFile.GetDuration<MetricTimeSpan>();
                float duration = (float)totalMetric.TotalMicroseconds / 1_000_000f;

                lock (_eventsLock)
                {
                    _events = newEvents;
                    _totalDuration = duration;
                }

                _currentPlayTime = 0f;
                _samplesRendered = 0;
                _isPlaying = false;
                _isPaused = false;

                Console.WriteLine($"   Total : {_events.Count} notes, {_totalDuration:F1}s, tempo {_originalTempo} BPM");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erreur de chargement MIDI : {ex.Message}");
                Console.WriteLine($"   {ex.StackTrace}");
                return false;
            }
        }

        public static void Play()
        {
            if (!_initialized || _midiFile == null || _sequencer == null)
            {
                Console.WriteLine(" Aucune musique chargée ou lecteur non initialisé.");
                return;
            }

            _sequencer.Play(_midiFile, false);
            _synthesizer?.Reset();
            _samplesRendered = 0;
            _currentPlayTime = 0f;
            _isPlaying = true;
            _isPaused = false;

            Raylib.PlayAudioStream(_stream);
            Console.WriteLine($"▶ Lecture démarrée ({_events.Count} notes)");
        }

        public static unsafe void Update()
        {
            if (!_initialized || !_streamReady) return;
            if (!_isPlaying || _isPaused || _sequencer == null) return;

            try
            {
                // Remplit le flux audio tant que Raylib a besoin de nouvelles données
                // (borné à quelques itérations pour éviter toute boucle infinie en cas de souci de timing)
                int safety = 8;
                while (safety-- > 0 && Raylib.IsAudioStreamProcessed(_stream))
                {
                    _sequencer.RenderInterleavedInt16(_renderBuffer);

                    fixed (short* bufferPtr = _renderBuffer)
                    {
                        Raylib.UpdateAudioStream(_stream, bufferPtr, RENDER_FRAMES);
                    }

                    _samplesRendered += RENDER_FRAMES;
                    _currentPlayTime = _samplesRendered / (float)SAMPLE_RATE;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erreur pendant le rendu audio MIDI : {ex.Message}");
                Console.WriteLine($"   {ex.StackTrace}");
                Stop();
                return;
            }

            if (_sequencer.EndOfSequence && _currentPlayTime >= _totalDuration)
            {
                Console.WriteLine("Musique terminée");
                _isPlaying = false;
            }
        }

        public static void Pause()
        {
            if (_isPlaying && !_isPaused)
            {
                _isPaused = true;
                Raylib.PauseAudioStream(_stream);
                Console.WriteLine($"Pause à t={_currentPlayTime:F1}s");
            }
        }

        public static void Resume()
        {
            if (_isPlaying && _isPaused)
            {
                _isPaused = false;
                Raylib.ResumeAudioStream(_stream);
                Console.WriteLine($"Reprise à t={_currentPlayTime:F1}s");
            }
        }

        public static void Stop()
        {
            _isPlaying = false;
            _isPaused = false;
            _samplesRendered = 0;
            _currentPlayTime = 0f;
            _sequencer?.Stop();
            if (_streamReady)
                Raylib.StopAudioStream(_stream);
            Console.WriteLine("Arrêt");
        }

        public static int GetPlayedNotes()
        {
            lock (_eventsLock)
                return _events.Count(e => e.StartTime <= _currentPlayTime);
        }

        public static int GetTotalNotes()
        {
            lock (_eventsLock)
                return _events.Count;
        }

        public static float GetProgress()
        {
            return _totalDuration > 0 ? Math.Clamp(_currentPlayTime / _totalDuration, 0f, 1f) : 0f;
        }

        public static int GetCurrentTempo() => _originalTempo;

        // =====================================================================================
        // Assainissement bas-niveau d'un fichier MIDI (Standard MIDI File) : élimine tout usage
        // du "running status" en réécrivant chaque évènement avec son octet de statut EXPLICITE.
        //
        // Contexte : certains fichiers .mid (mal exportés) utilisent un running status implicite
        // à un endroit où aucun statut de canal valide n'est encore défini (ex: tout début d'une
        // piste, ou juste après un évènement Meta/SysEx selon l'implémentation). Les parseurs
        // stricts (DryWetMidi, MeltySynth) rejettent alors le fichier avec une erreur du type
        // "Unexpected running status". En réécrivant le flux d'évènements avec un statut explicite
        // à chaque fois, cette ambiguïté disparaît complètement, sans perte d'information musicale.
        //
        // Limitation connue : les messages "System Common" rares (0xF1 MTC quarter-frame,
        // 0xF2 Song Position, 0xF3 Song Select, 0xF6 Tune Request) sont copiés tels quels avec un
        // nombre de données par défaut simplifié ; ils n'apparaissent quasiment jamais dans des
        // fichiers de musique exportés depuis un DAW/séquenceur classique.
        // =====================================================================================
        private static byte[] SanitizeMidiRunningStatus(byte[] input)
        {
            int pos = 0;

            // --- En-tête MThd : copié tel quel ---
            if (input.Length < 14 || input[0] != 'M' || input[1] != 'T' || input[2] != 'h' || input[3] != 'd')
            {
                // Pas un fichier MIDI reconnaissable : on le laisse tel quel, le parseur lèvera
                // l'erreur appropriée plus haut dans la pile.
                return input;
            }

            uint headerLength = ReadUInt32BE(input, 4);
            int headerChunkTotalSize = 8 + (int)headerLength;
            var output = new List<byte>(input.Length + 256);
            output.AddRange(new ArraySegment<byte>(input, 0, headerChunkTotalSize));
            pos = headerChunkTotalSize;

            // --- Pistes MTrk : réécrites une par une avec statuts explicites ---
            while (pos + 8 <= input.Length)
            {
                if (input[pos] != 'M' || input[pos + 1] != 'T' || input[pos + 2] != 'r' || input[pos + 3] != 'k')
                {
                    // Chunk inconnu/non-MTrk : on le copie tel quel sans le modifier.
                    uint unknownLength = ReadUInt32BE(input, pos + 4);
                    int unknownTotal = 8 + (int)unknownLength;
                    if (pos + unknownTotal > input.Length) break;
                    output.AddRange(new ArraySegment<byte>(input, pos, unknownTotal));
                    pos += unknownTotal;
                    continue;
                }

                uint trackLength = ReadUInt32BE(input, pos + 4);
                int trackContentStart = pos + 8;
                int trackContentEnd = Math.Min(trackContentStart + (int)trackLength, input.Length);

                byte[] rewrittenTrack = RewriteTrackExplicitStatus(input, trackContentStart, trackContentEnd);

                output.Add((byte)'M'); output.Add((byte)'T'); output.Add((byte)'r'); output.Add((byte)'k');
                WriteUInt32BE(output, (uint)rewrittenTrack.Length);
                output.AddRange(rewrittenTrack);

                pos = trackContentEnd;
            }

            return output.ToArray();
        }

        private static byte[] RewriteTrackExplicitStatus(byte[] data, int start, int end)
        {
            var output = new List<byte>(end - start + 64);
            int pos = start;
            byte runningStatus = 0; // 0 = aucun statut de canal connu pour l'instant
            bool endOfTrackWritten = false;
            long pendingDelta = 0; // delta-time accumulé pour les évènements supprimés (non réémis)

            while (pos < end)
            {
                // --- Lecture du delta-time (VLQ) ---
                int deltaStart = pos;
                byte db;
                do
                {
                    if (pos >= end) { pos = end; break; }
                    db = data[pos++];
                } while ((db & 0x80) != 0);
                if (pos > end || deltaStart >= end) break;
                int deltaValue = DecodeVarLen(data, deltaStart, pos);
                pendingDelta += deltaValue;

                if (pos >= end) break; // fichier tronqué juste après un delta-time : on s'arrête proprement

                byte eventByte = data[pos];
                byte statusByte;
                List<byte> payload = new List<byte>();
                bool dropEvent = false;

                if (eventByte >= 0x80)
                {
                    // Octet de statut explicite déjà présent
                    statusByte = eventByte;
                    pos++;

                    if (statusByte == 0xFF) // Meta event : FF <type> <varlen> <data...>
                    {
                        if (pos >= end) break;
                        byte metaType = data[pos++];
                        int len = ReadVarLen(data, ref pos);
                        int dataEnd = Math.Min(pos + len, end);

                        payload.Add(metaType);
                        AppendVarLen(payload, len);
                        payload.AddRange(new ArraySegment<byte>(data, pos, dataEnd - pos));

                        if (metaType == 0x2F) endOfTrackWritten = true;
                        pos = dataEnd;
                    }
                    else if (statusByte == 0xF0 || statusByte == 0xF7) // SysEx : F0/F7 <varlen> <data...>
                    {
                        int len = ReadVarLen(data, ref pos);
                        int dataEnd = Math.Min(pos + len, end);
                        AppendVarLen(payload, len);
                        payload.AddRange(new ArraySegment<byte>(data, pos, dataEnd - pos));
                        pos = dataEnd;
                    }
                    else if (statusByte >= 0xF1 && statusByte <= 0xFE)
                    {
                        // "System Common" (F1-F6) et "System Realtime" (F8-FE) : DryWetMidi (et la
                        // norme elle-même dans le contexte d'un fichier .mid) ne les route vers
                        // aucun lecteur dédié au niveau piste - ils sont interprétés à tort comme
                        // des "channel events" avec un nibble de canal invalide et font planter la
                        // lecture. On les supprime entièrement : ils ne portent aucune information
                        // utile pour la lecture de notes/synthèse. Leur delta-time est reporté sur
                        // le prochain évènement conservé pour ne pas décaler le morceau.
                        int scCount = statusByte == 0xF2 ? 2
                                    : (statusByte >= 0xF8 ? 0
                                    : (statusByte == 0xF6 ? 0 : 1));
                        int available = Math.Min(scCount, end - pos);
                        pos += available;
                        runningStatus = 0; // ces messages annulent le running status
                        dropEvent = true;
                    }
                    else
                    {
                        int dataCount = GetDataByteCount(statusByte);
                        // Message de canal (0x80-0xEF) : définit le running status
                        runningStatus = statusByte;
                        int available = Math.Min(Math.Max(dataCount, 0), end - pos);
                        payload.AddRange(new ArraySegment<byte>(data, pos, available));
                        pos += available;
                    }
                }
                else
                {
                    // Octet de donnée : implique un running status
                    if (runningStatus == 0)
                    {
                        // Aucun statut connu (running status invalide/orphelin dans le fichier
                        // d'origine) : on ignore cet octet corrompu plutôt que de faire planter
                        // la lecture, et on ne consomme rien d'autre pour cet "évènement".
                        pos++;
                        continue;
                    }

                    statusByte = runningStatus;
                    int dataCount = GetDataByteCount(statusByte);
                    int need = Math.Max(dataCount, 0);
                    // Le premier octet de données est déjà celui qu'on vient de lire (eventByte)
                    int available = Math.Min(need, end - pos);
                    payload.AddRange(new ArraySegment<byte>(data, pos, available));
                    pos += available;
                }

                if (dropEvent) continue;

                // --- Écriture explicite : delta-time (accumulé) + statut + données ---
                AppendVarLen(output, (int)Math.Min(pendingDelta, int.MaxValue));
                pendingDelta = 0;
                output.Add(statusByte);
                output.AddRange(payload);
            }

            if (!endOfTrackWritten)
            {
                // Ajoute un End Of Track si absent (delta accumulé restant + FF 2F 00)
                AppendVarLen(output, (int)Math.Min(pendingDelta, int.MaxValue));
                output.Add(0xFF);
                output.Add(0x2F);
                output.Add(0x00);
            }

            return output.ToArray();
        }

        private static int DecodeVarLen(byte[] data, int start, int end)
        {
            int value = 0;
            for (int i = start; i < end; i++)
                value = (value << 7) | (data[i] & 0x7F);
            return value;
        }

        private static void AppendVarLen(List<byte> output, int value)
        {
            var stack = new Stack<byte>();
            stack.Push((byte)(value & 0x7F));
            value >>= 7;
            while (value > 0)
            {
                stack.Push((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            while (stack.Count > 0) output.Add(stack.Pop());
        }

        private static int GetDataByteCount(byte status)
        {
            int type = status & 0xF0;
            switch (type)
            {
                case 0x80: case 0x90: case 0xA0: case 0xB0: case 0xE0: return 2;
                case 0xC0: case 0xD0: return 1;
                default: return -1;
            }
        }

        private static int ReadVarLen(byte[] data, ref int pos)
        {
            int value = 0;
            byte b;
            do
            {
                b = data[pos++];
                value = (value << 7) | (b & 0x7F);
            } while ((b & 0x80) != 0);
            return value;
        }

        private static byte[] SerializeDryWetMidiFile(DryWetMidiFile file)
        {
            using var ms = new MemoryStream();
            var writingSettings = new Melanchall.DryWetMidi.Core.WritingSettings
            {
                UseRunningStatus = false,
                DeleteUnknownMetaEvents = false,
                DeleteUnknownChunks = false,
                WriteHeaderChunk = true,
                TextEncoding = System.Text.Encoding.UTF8
            };
            file.Write(ms, file.OriginalFormat, writingSettings);
            return ms.ToArray();
        }

        private static uint ReadUInt32BE(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static void WriteUInt32BE(List<byte> output, uint value)
        {
            output.Add((byte)((value >> 24) & 0xFF));
            output.Add((byte)((value >> 16) & 0xFF));
            output.Add((byte)((value >> 8) & 0xFF));
            output.Add((byte)(value & 0xFF));
        }
    }
}