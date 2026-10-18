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
using System.Collections.Concurrent;
using System.Diagnostics;
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
        public int InstrumentIndex { get; set; } // canal MIDI (0-15) d'origine
        public int PerformerIndex { get; set; } = -1; // musicien du groupe qui joue la note (-1 : non jouée / morceau complet)
    }

    public sealed class MusicNoteParticle : Particle
    {
        private static readonly Color[] PitchColors =
        {
            new(255, 112, 124, 255), new(255, 153, 102, 255), new(255, 207, 104, 255),
            new(194, 222, 105, 255), new(111, 218, 150, 255), new(94, 220, 201, 255),
            new(100, 190, 255, 255), new(126, 151, 255, 255), new(172, 139, 255, 255),
            new(218, 132, 239, 255), new(247, 131, 193, 255), new(255, 135, 155, 255)
        };

        private readonly int _flagCount;

        public MusicNoteParticle(Vector2 position, Vector2 velocity, int pitch, float size, float lifetime)
            : base(position, velocity, PitchColors[((pitch % 12) + 12) % 12], size, lifetime)
        {
            _flagCount = size >= 12f ? 2 : 1;
            Drag = 0.992f;
        }

        public void Draw()
        {
            byte alpha = (byte)(Math.Clamp(GetAlpha(), 0f, 1f) * Color.A);
            Color color = new(Color.R, Color.G, Color.B, alpha);
            float size = Size;
            Vector2 head = Position;
            Vector2 stemTop = head + new Vector2(size * 0.28f, -size * 1.8f);

            Raylib.DrawCircleV(head, size * 0.48f, new Color(Color.R, Color.G, Color.B, (byte)(alpha * 0.3f)));
            Raylib.DrawCircleV(head, size * 0.34f, color);
            Raylib.DrawLineEx(head + new Vector2(size * 0.25f, -size * 0.12f), stemTop, MathF.Max(1.5f, size * 0.16f), color);

            for (int flag = 0; flag < _flagCount; flag++)
            {
                Vector2 start = stemTop + new Vector2(0f, flag * size * 0.42f);
                Vector2 middle = start + new Vector2(size * 0.58f, size * 0.16f);
                Vector2 end = start + new Vector2(size * 0.36f, size * 0.52f);
                Raylib.DrawLineEx(start, middle, MathF.Max(1.5f, size * 0.15f), color);
                Raylib.DrawLineEx(middle, end, MathF.Max(1.5f, size * 0.15f), color);
            }
        }
    }

    public sealed class MusicPerformer
    {
        public int Program { get; set; }
        public int PlayerId { get; set; } = -1;
        public string EntityNetId { get; set; } = "";
        public bool IsPercussion { get; set; }
    }

    public static class MusicPlayer
    {
        private readonly record struct BandMusicEvent(long Sample, int Channel, int Pitch, int Velocity, float Duration, int PerformerIndex, bool IsNoteOn);
        private readonly record struct ParticleRequest(double DueTime, MusicPerformer Performer, int Pitch, int Velocity, float Duration);
        private readonly record struct InstrumentInfo(int Program, int FallbackProgram, bool IsPercussion);

        private sealed class ParsedMusic
        {
            public string Path = "";
            public string Name = "";
            public SynthMidiFile Midi = null!;
            public List<NoteDisplayInfo> Notes = new();
            public float Duration;
            public int Tempo = 120;
            public double LastUsed;
        }

        // =====================================================================================
        //  Configuration audio
        // =====================================================================================
        private const int SAMPLE_RATE = 44100;
        private const int CHANNELS = 2;
        // Taille d'un bloc rendu. Le rendu tourne maintenant sur un thread dédié (indépendant du
        // framerate du jeu) : on peut donc descendre à 1024 (~23 ms) pour réduire la latence des
        // notes jouées au clavier. Remonter à 2048 si des craquements apparaissent sur une machine lente.
        private const int RENDER_FRAMES = 1024;
        // Taille de buffer par défaut qu'avait le projet avant ce changement (restaurée après la
        // création de notre flux, au cas où d'autres flux audio en dépendraient).
        private const int LEGACY_STREAM_FRAMES = 2048;
        private const float MIDI_MASTER_GAIN = 0.22f;
        // Retard estimé entre "rendu dans le buffer" et "entendu" : sert à caler visualiseur,
        // particules et synchronisation réseau sur ce que le joueur entend réellement.
        private const double OUTPUT_LATENCY = RENDER_FRAMES * 1.5 / SAMPLE_RATE;
        private const int MAX_CACHED_MUSICS = 6;
        private const int DEFAULT_CC_VOLUME = 100; // valeur par défaut d'un canal MIDI (CC7)
        private const int DEFAULT_CC_PAN = 64;     // centre (CC10)
        private const int BAND_PERCUSSION_PITCH = 70; // maracas dans le kit GM

        // Sons de petites percussions (kit GM) assignés aux 12 touches du clavier live :
        // maracas, cabasa, shaker, tambourin, claves, wood blocks, guiros, triangles, agogo.
        private static readonly int[] LivePercussionPitches = { 70, 69, 82, 54, 75, 76, 77, 73, 74, 80, 81, 67 };

        // Tous les états ci-dessous (synthés, séquenceur, listes) sont protégés par _lock :
        // le thread de rendu et le thread principal y accèdent tous les deux.
        private static readonly object _lock = new object();

        // --- Etat SoundFont / Synthé ---
        private static SoundFont? _soundFont = null;
        private static Synthesizer? _synthesizer = null;           // morceau complet (séquenceur)
        private static Synthesizer? _instrumentSynthesizer = null; // notes jouées en direct
        private static Synthesizer? _bandSynthesizer = null;       // groupe de musiciens
        private static bool _initialized = false;
        private static string _soundFontPath = "assets/soundfonts/default.sf2";
        private static readonly Dictionary<int, bool> _presetCache = new();

        // --- Flux audio Raylib + thread de rendu ---
        private static AudioStream _stream;
        private static bool _streamReady = false;
        private static bool _audioStreamPlaying = false;
        private static Thread? _pumpThread;
        private static volatile bool _pumpRunning;
        private static readonly short[] _renderBuffer = new short[RENDER_FRAMES * CHANNELS];
        private static readonly float[] _srcL = new float[RENDER_FRAMES];
        private static readonly float[] _srcR = new float[RENDER_FRAMES];
        private static readonly float[] _mixL = new float[RENDER_FRAMES];
        private static readonly float[] _mixR = new float[RENDER_FRAMES];
        private static float _musicVolume = 1f;

        // --- MIDI en cours ---
        private static SynthMidiFile? _midiFile = null;
        private static MidiFileSequencer? _sequencer = null;
        private static List<NoteDisplayInfo> _events = new List<NoteDisplayInfo>();
        private static string _currentMusicName = "";
        private static string _pendingMusicName = "";
        private static int _notesVersion;
        private static string _lastEndedName = "";
        private static double _lastEndedAt = -1000;

        private static bool _isPlaying = false;
        private static bool _isPaused = false;
        private static long _samplesRendered = 0;
        private static long _tailEndSample = 0;
        private static float _currentPlayTime = 0f;
        private static float _totalDuration = 0f;
        private static int _originalTempo = 120;

        // --- Notes live (clavier de l'instrument) ---
        private static readonly List<(double EndTime, int Channel, int Pitch)> _scheduledNoteOffs = new();
        private static readonly int[] _liveInstrumentPrograms = Enumerable.Repeat(-1, 16).ToArray();
        private static readonly int[] _liveChannelPlayer = Enumerable.Repeat(int.MinValue, 16).ToArray();
        private static readonly double[] _liveChannelLastUsed = new double[16];
        private static readonly Dictionary<int, int> _livePlayerChannel = new();
        private static bool _liveDrumKitReady;
        private static double _instrumentAudioUntil;

        // --- Groupe ---
        private static List<BandMusicEvent> _bandMusicEvents = new();
        private static MusicPerformer[] _bandPerformers = Array.Empty<MusicPerformer>();
        private static int[] _bandPerformerChannels = Array.Empty<int>();
        private static float[] _bandGain = Array.Empty<float>();
        private static float[] _bandPan = Array.Empty<float>();
        private static int[] _bandSentVolume = Array.Empty<int>();
        private static int[] _bandSentPan = Array.Empty<int>();
        private static int _nextBandMusicEvent;
        private static bool _ensembleMode;
        private static double _nextSpatialUpdate;

        // --- Communication entre threads ---
        private static readonly ConcurrentQueue<Action> _mainThreadActions = new();
        private static readonly ConcurrentQueue<ParticleRequest> _particleQueue = new();
        private static int _playRequestId;

        // --- Cache des MIDI déjà analysés ---
        private static readonly Dictionary<string, ParsedMusic> _musicCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new object();

        // --- Instruments (programme MIDI par nom d'objet) ---
        // Table de données : pour ajouter un instrument, une ligne ici (ou RegisterInstrument).
        // FallbackProgram est utilisé si la SoundFont n'a pas le preset demandé (ex. saxophone).
        private static readonly Dictionary<string, InstrumentInfo> _instruments = new(StringComparer.OrdinalIgnoreCase)
        {
            ["flute"] = new(73, 73, false),
            ["guitar"] = new(24, 24, false),
            ["piano"] = new(0, 0, false),
            ["saxophone"] = new(65, 71, false), // la SoundFont fournie n'a pas de sax : clarinette
            ["maracas"] = new(0, 0, true),
        };

        // =====================================================================================
        //  Propriétés publiques
        // =====================================================================================
        public static float CurrentPlayTime => _currentPlayTime;
        public static float TotalDuration => _totalDuration;
        public static bool IsPlaying => _isPlaying && !_isPaused;
        public static bool IsPaused => _isPaused;
        /// <summary>Un morceau est en cours (lecture ou pause).</summary>
        public static bool IsActive => _isPlaying;
        /// <summary>Nom du morceau en cours, "" si arrêté ou terminé.</summary>
        public static string CurrentMusicName => _isPlaying ? _currentMusicName : "";
        /// <summary>Incrémenté à chaque changement de morceau ou d'affectation des musiciens.</summary>
        public static int NotesVersion => Volatile.Read(ref _notesVersion);

        /// <summary>Volume du bus musique (0..1), indépendant du volume maître du jeu.</summary>
        public static float MusicVolume
        {
            get => _musicVolume;
            set => _musicVolume = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 1f;
        }

        private static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        // =====================================================================================
        //  Instruments
        // =====================================================================================
        public static void RegisterInstrument(string name, int program, bool isPercussion = false, int fallbackProgram = -1)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            lock (_lock)
                _instruments[name] = new InstrumentInfo(program, fallbackProgram < 0 ? program : fallbackProgram, isPercussion);
        }

        public static int GetInstrumentProgram(string instrumentName)
        {
            lock (_lock)
            {
                if (!_instruments.TryGetValue(instrumentName ?? "", out var info))
                    return 0;
                return info.FallbackProgram != info.Program && !HasPreset(info.Program)
                    ? info.FallbackProgram
                    : info.Program;
            }
        }

        public static bool IsPercussionInstrument(string instrumentName)
        {
            lock (_lock)
                return _instruments.TryGetValue(instrumentName ?? "", out var info) && info.IsPercussion;
        }

        private static bool HasPreset(int program)
        {
            if (_soundFont == null) return false;
            if (_presetCache.TryGetValue(program, out bool cached)) return cached;
            bool has = _soundFont.Presets.Any(preset => preset.BankNumber == 0 && preset.PatchNumber == program);
            _presetCache[program] = has;
            return has;
        }

        /// <summary>
        /// Canal MIDI stable pour un joueur. Allocation dynamique (et non plus playerId % 15) : deux
        /// joueurs connectés ne partagent plus un canal tant qu'il reste moins de 15 joueurs, même si
        /// leurs identifiants de connexion sont grands. Au-delà, le canal le moins récemment utilisé est recyclé.
        /// </summary>
        public static int GetLiveInstrumentChannel(int playerId)
        {
            lock (_lock)
            {
                double now = Now();
                if (_livePlayerChannel.TryGetValue(playerId, out int existing) && _liveChannelPlayer[existing] == playerId)
                {
                    _liveChannelLastUsed[existing] = now;
                    return existing;
                }

                int best = -1;
                double bestTime = double.MaxValue;
                for (int c = 0; c < 16; c++)
                {
                    if (c == 9) continue; // canal 9 = percussions
                    if (_liveChannelPlayer[c] == int.MinValue) { best = c; break; }
                    if (_liveChannelLastUsed[c] < bestTime) { bestTime = _liveChannelLastUsed[c]; best = c; }
                }

                if (_liveChannelPlayer[best] != int.MinValue)
                {
                    _livePlayerChannel.Remove(_liveChannelPlayer[best]);
                    _instrumentSynthesizer?.NoteOffAll(best, true);
                    _scheduledNoteOffs.RemoveAll(pending => pending.Channel == best);
                }
                _liveInstrumentPrograms[best] = -1;
                _liveChannelPlayer[best] = playerId;
                _livePlayerChannel[playerId] = best;
                _liveChannelLastUsed[best] = now;
                return best;
            }
        }

        /// <summary>
        /// Joue une note en direct. gain (0..1) et pan (-1..1) permettent la spatialisation
        /// (voir ComputeSpatial) ; les valeurs par défaut correspondent au joueur local.
        /// </summary>
        public static void PlayInstrumentNote(int program, int pitch, int velocity, float duration, int channel,
            bool isPercussion = false, float gain = 1f, float pan = 0f)
        {
            if (!_initialized || _instrumentSynthesizer == null || !_streamReady)
                return;
            if (program < 0 || program > 127 || pitch < 0 || pitch > 127 || velocity < 1 || velocity > 127 || !float.IsFinite(duration))
                return;
            gain = float.IsFinite(gain) ? Math.Clamp(gain, 0f, 1f) : 1f;
            pan = float.IsFinite(pan) ? Math.Clamp(pan, -1f, 1f) : 0f;
            if (gain < 0.004f) return; // inaudible : inutile d'occuper une voix

            channel = isPercussion ? 9 : Math.Clamp(channel, 0, 15);
            duration = Math.Clamp(duration, 0.05f, 2f);

            lock (_lock)
            {
                double now = Now();
                int actualPitch = isPercussion ? LivePercussionPitches[((pitch % 12) + 12) % 12] : pitch;

                for (int i = _scheduledNoteOffs.Count - 1; i >= 0; i--)
                {
                    var pending = _scheduledNoteOffs[i];
                    if (pending.Channel == channel && pending.Pitch == actualPitch)
                    {
                        _instrumentSynthesizer.NoteOff(channel, actualPitch);
                        _scheduledNoteOffs.RemoveAt(i);
                    }
                }

                if (isPercussion)
                {
                    if (!_liveDrumKitReady)
                    {
                        _instrumentSynthesizer.ProcessMidiMessage(channel, 0xB0, 0, 1);
                        _instrumentSynthesizer.ProcessMidiMessage(channel, 0xB0, 32, 0);
                        _instrumentSynthesizer.ProcessMidiMessage(channel, 0xC0, 0, 0);
                        _liveDrumKitReady = true;
                    }
                }
                else if (_liveInstrumentPrograms[channel] != program)
                {
                    _instrumentSynthesizer.ProcessMidiMessage(channel, 0xC0, program, 0);
                    _liveInstrumentPrograms[channel] = program;
                }

                _instrumentSynthesizer.ProcessMidiMessage(channel, 0xB0, 7, VolumeToCc(gain));
                _instrumentSynthesizer.ProcessMidiMessage(channel, 0xB0, 10, PanToCc(pan));
                _instrumentSynthesizer.NoteOn(channel, actualPitch, velocity);
                _scheduledNoteOffs.Add((now + duration, channel, actualPitch));
                _instrumentAudioUntil = Math.Max(_instrumentAudioUntil, now + duration + 1.5);
                EnsureStreamPlaying();
            }
        }

        // CC7 est quadratique (norme GM) : on prend la racine du gain linéaire souhaité.
        private static int VolumeToCc(float gain) =>
            Math.Clamp((int)MathF.Round(DEFAULT_CC_VOLUME * MathF.Sqrt(Math.Clamp(gain, 0f, 1f))), 0, 127);

        private static int PanToCc(float pan) =>
            Math.Clamp((int)MathF.Round(DEFAULT_CC_PAN + Math.Clamp(pan, -1f, 1f) * 63f), 1, 127);

        // =====================================================================================
        //  Spatialisation
        // =====================================================================================
        /// <summary>
        /// Calcule gain et panoramique d'une source sonore (position monde) pour le joueur local.
        /// Plein volume jusqu'à ~7 tuiles, silence au-delà de ~36 tuiles. A appeler depuis le thread principal.
        /// </summary>
        public static void ComputeSpatial(Vector2 sourceWorldPos, out float gain, out float pan)
        {
            Vector2 listener = Program.GetPlayerVisualPosition(Program.GetPlayerPosition());
            float tile = MathF.Max((float)Program.TileSize, 1f);
            Vector2 delta = sourceWorldPos - listener;
            float distTiles = delta.Length() / tile;
            const float fullVolumeRadius = 7f;
            const float silenceRadius = 36f;
            float t = Math.Clamp((distTiles - fullVolumeRadius) / (silenceRadius - fullVolumeRadius), 0f, 1f);
            gain = (1f - t) * (1f - t);
            pan = Math.Clamp(delta.X / tile / 12f, -1f, 1f) * 0.85f;
        }

        private static bool TryGetPerformerPosition(MusicPerformer performer, out Vector2 position)
        {
            if (performer.PlayerId >= 0)
                return NetworkManager.TryGetMusicPlayerPosition(performer.PlayerId, out position);
            if (Guid.TryParse(performer.EntityNetId, out var entityNetId))
                return NetworkManager.TryGetMusicEntityPosition(entityNetId, out position);
            position = Vector2.Zero;
            return false;
        }

        // Appelé ~10 fois par seconde depuis le thread principal : volume (CC7) et pan (CC10) de
        // chaque musicien du groupe selon sa position par rapport au joueur local.
        private static void UpdateSpatial()
        {
            double now = Now();
            if (now < _nextSpatialUpdate) return;
            _nextSpatialUpdate = now + 0.1;

            MusicPerformer[] performers;
            lock (_lock)
            {
                if (!_ensembleMode || _bandPerformers.Length == 0) return;
                performers = _bandPerformers;
            }

            var gains = new float[performers.Length];
            var pans = new float[performers.Length];
            var found = new bool[performers.Length];
            for (int i = 0; i < performers.Length; i++)
            {
                found[i] = TryGetPerformerPosition(performers[i], out Vector2 pos);
                if (found[i])
                    ComputeSpatial(pos, out gains[i], out pans[i]);
            }

            lock (_lock)
            {
                if (!_ensembleMode || _bandSynthesizer == null || !ReferenceEquals(performers, _bandPerformers))
                    return;

                var channelsDone = new HashSet<int>();
                for (int i = 0; i < performers.Length; i++)
                {
                    int channel = _bandPerformerChannels[i];
                    // Les percussionnistes partagent le canal 9 : le premier trouvé donne le ton.
                    if (!found[i] || !channelsDone.Add(channel)) continue;

                    _bandGain[i] += (gains[i] - _bandGain[i]) * 0.6f;
                    _bandPan[i] += (pans[i] - _bandPan[i]) * 0.6f;
                    int volume = VolumeToCc(_bandGain[i]);
                    int panCc = PanToCc(_bandPan[i]);
                    if (volume != _bandSentVolume[i])
                    {
                        _bandSynthesizer.ProcessMidiMessage(channel, 0xB0, 7, volume);
                        _bandSentVolume[i] = volume;
                    }
                    if (panCc != _bandSentPan[i])
                    {
                        _bandSynthesizer.ProcessMidiMessage(channel, 0xB0, 10, panCc);
                        _bandSentPan[i] = panCc;
                    }
                }
            }
        }

        // =====================================================================================
        //  Initialisation / arrêt
        // =====================================================================================
        /// <summary>
        /// A appeler une seule fois, après Raylib.InitAudioDevice(), pour charger la SoundFont,
        /// préparer le flux audio et démarrer le thread de rendu.
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
                _synthesizer = new Synthesizer(_soundFont, settings) { MasterVolume = MIDI_MASTER_GAIN };
                _instrumentSynthesizer = new Synthesizer(_soundFont, settings) { MasterVolume = MIDI_MASTER_GAIN };
                _bandSynthesizer = new Synthesizer(_soundFont, settings) { MasterVolume = MIDI_MASTER_GAIN };
                Array.Fill(_liveInstrumentPrograms, -1);
                _liveDrumKitReady = false;
                _sequencer = new MidiFileSequencer(_synthesizer);

                Raylib.SetAudioStreamBufferSizeDefault(RENDER_FRAMES);
                _stream = Raylib.LoadAudioStream(SAMPLE_RATE, 16, CHANNELS);
                Raylib.SetAudioStreamBufferSizeDefault(LEGACY_STREAM_FRAMES);
                Raylib.SetAudioStreamVolume(_stream, 1f);
                _streamReady = true;

                _initialized = true;

                _pumpRunning = true;
                _pumpThread = new Thread(PumpLoop)
                {
                    Name = "MusicPump",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _pumpThread.Start();

                Console.WriteLine($" SoundFont chargée : {_soundFontPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erreur de chargement de la SoundFont : {ex.Message}");
            }
        }

        /// <summary>
        /// A appeler à la fermeture du jeu, AVANT Raylib.CloseAudioDevice() : arrête le thread de
        /// rendu puis libère le flux audio.
        /// </summary>
        public static void Shutdown()
        {
            if (!_initialized) return;
            _pumpRunning = false;
            _pumpThread?.Join(500);
            lock (_lock)
            {
                StopInternal();
                if (_streamReady)
                {
                    Raylib.StopAudioStream(_stream);
                    Raylib.UnloadAudioStream(_stream);
                    _streamReady = false;
                    _audioStreamPlaying = false;
                }
                _initialized = false;
            }
        }

        /// <summary>
        /// A appeler une fois par frame depuis le thread principal : exécute les rappels de
        /// chargement asynchrone, émet les particules de notes au bon moment et met à jour la spatialisation.
        /// </summary>
        public static void Update()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { Console.WriteLine($" Erreur MusicPlayer.Update : {ex.Message}"); }
            }

            double now = Now();
            int budget = 48;
            while (budget-- > 0 && _particleQueue.TryPeek(out var next) && next.DueTime <= now)
            {
                if (!_particleQueue.TryDequeue(out var request)) break;
                Program.EmitMusicNoteParticle(request.Performer, request.Pitch, request.Velocity, request.Duration);
            }

            if (_initialized)
                UpdateSpatial();
        }

        // =====================================================================================
        //  Chargement MIDI (analyse + cache)
        // =====================================================================================
        public static List<string> GetAvailableMusicNames()
        {
            if (!Directory.Exists("assets/musics")) return new List<string>();
            return Directory.GetFiles("assets/musics", "*.mid")
                            .Concat(Directory.GetFiles("assets/musics", "*.midi"))
                            .Select(f => Path.GetFileNameWithoutExtension(f))
                            .Distinct()
                            .ToList();
        }

        public static List<NoteDisplayInfo> GetNotesForDisplay()
        {
            lock (_lock)
            {
                return _events.Select(e => new NoteDisplayInfo
                {
                    StartTime = e.StartTime,
                    Duration = e.Duration,
                    Pitch = e.Pitch,
                    Velocity = e.Velocity,
                    InstrumentIndex = e.InstrumentIndex,
                    PerformerIndex = e.PerformerIndex
                }).ToList();
            }
        }

        /// <summary>Musiciens du morceau en cours (vide en lecture "morceau complet").</summary>
        public static List<MusicPerformer> GetCurrentPerformers()
        {
            lock (_lock)
                return _bandPerformers.ToList();
        }

        /// <summary>Vrai si ce morceau est en cours de lecture (ou de chargement).</summary>
        public static bool IsHandling(string musicName)
        {
            lock (_lock)
                return (_isPlaying && string.Equals(_currentMusicName, musicName, StringComparison.OrdinalIgnoreCase))
                    || string.Equals(_pendingMusicName, musicName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Vrai si ce morceau s'est terminé localement il y a moins de 'seconds' secondes.</summary>
        public static bool RecentlyEnded(string musicName, double seconds)
        {
            lock (_lock)
                return string.Equals(_lastEndedName, musicName, StringComparison.OrdinalIgnoreCase)
                    && Now() - _lastEndedAt < seconds;
        }

        private static ParsedMusic? GetOrParse(string midiPath)
        {
            string key = Path.GetFullPath(midiPath);
            lock (_cacheLock)
            {
                if (_musicCache.TryGetValue(key, out var cached))
                {
                    cached.LastUsed = Now();
                    return cached;
                }
            }

            ParsedMusic? parsed = ParseMidi(midiPath);
            if (parsed == null) return null;

            lock (_cacheLock)
            {
                _musicCache[key] = parsed;
                parsed.LastUsed = Now();
                while (_musicCache.Count > MAX_CACHED_MUSICS)
                {
                    string? oldest = _musicCache.OrderBy(kv => kv.Value.LastUsed).First().Key;
                    _musicCache.Remove(oldest);
                }
            }
            return parsed;
        }

        private static ParsedMusic? ParseMidi(string midiPath)
        {
            if (!File.Exists(midiPath))
            {
                Console.WriteLine($"Fichier MIDI introuvable : {midiPath}");
                return null;
            }

            try
            {
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
                SynthMidiFile synthMidi;
                using (var msSynth = new MemoryStream(sanitizedBytes, writable: false))
                {
                    synthMidi = new SynthMidiFile(msSynth);
                }

                var initialTempo = tempoMap.GetTempoAtTime((MidiTimeSpan)0);
                int tempo = initialTempo != null ? (int)Math.Round(initialTempo.BeatsPerMinute) : 120;

                var notes = new List<NoteDisplayInfo>();
                foreach (var note in dryWetFile.GetNotes())
                {
                    var start = note.TimeAs<MetricTimeSpan>(tempoMap);
                    var length = note.LengthAs<MetricTimeSpan>(tempoMap);
                    notes.Add(new NoteDisplayInfo
                    {
                        StartTime = (float)start.TotalMicroseconds / 1_000_000f,
                        Duration = (float)length.TotalMicroseconds / 1_000_000f,
                        Pitch = note.NoteNumber,
                        Velocity = note.Velocity,
                        InstrumentIndex = note.Channel
                    });
                }
                notes = notes.OrderBy(e => e.StartTime).ToList();

                var totalMetric = dryWetFile.GetDuration<MetricTimeSpan>();
                float duration = (float)totalMetric.TotalMicroseconds / 1_000_000f;

                Console.WriteLine($"   Total : {notes.Count} notes, {duration:F1}s, tempo {tempo} BPM");
                return new ParsedMusic
                {
                    Path = midiPath,
                    Name = Path.GetFileNameWithoutExtension(midiPath),
                    Midi = synthMidi,
                    Notes = notes,
                    Duration = duration,
                    Tempo = tempo
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erreur de chargement MIDI : {ex.Message}");
                Console.WriteLine($"   {ex.StackTrace}");
                return null;
            }
        }

        // Rend un morceau analysé "courant" (sous _lock).
        private static void ActivateMusic(ParsedMusic parsed)
        {
            _midiFile = parsed.Midi;
            _events = parsed.Notes;
            _totalDuration = parsed.Duration;
            _originalTempo = parsed.Tempo;
            _currentMusicName = parsed.Name;
            _currentPlayTime = 0f;
            _samplesRendered = 0;
            _isPlaying = false;
            _isPaused = false;
            Interlocked.Increment(ref _notesVersion);
        }

        /// <summary>Chargement synchrone (conservé pour compatibilité). Préférer PlayFileAsync.</summary>
        public static bool LoadMusic(string midiPath)
        {
            if (!_initialized)
            {
                Console.WriteLine(" MusicPlayer non initialisé (appelez MusicPlayer.Init() après InitAudioDevice()).");
                return false;
            }

            ParsedMusic? parsed = GetOrParse(midiPath);
            if (parsed == null) return false;
            lock (_lock)
            {
                StopInternal();
                ActivateMusic(parsed);
            }
            return true;
        }

        /// <summary>
        /// Charge (hors thread principal) puis démarre un morceau, sans figer le jeu ni couper
        /// l'audio en cours. 'startPosition' (secondes) est automatiquement augmentée du temps passé
        /// à charger, pour rester synchronisé avec l'hôte. onDone(true/false) est appelé sur le thread principal.
        /// Un nouvel appel, ou Stop(), annule la demande précédente.
        /// </summary>
        public static void PlayFileAsync(string midiPath, IReadOnlyList<MusicPerformer> performers, float startPosition, Action<bool>? onDone)
        {
            if (!_initialized)
            {
                Console.WriteLine(" MusicPlayer non initialisé (appelez MusicPlayer.Init() après InitAudioDevice()).");
                onDone?.Invoke(false);
                return;
            }

            int requestId = Interlocked.Increment(ref _playRequestId);
            MusicPerformer[] performersCopy = performers.ToArray();
            double requestedAt = Now();
            lock (_lock)
                _pendingMusicName = Path.GetFileNameWithoutExtension(midiPath);

            Task.Run(() =>
            {
                ParsedMusic? parsed = null;
                try { parsed = GetOrParse(midiPath); }
                catch (Exception ex) { Console.WriteLine($" Erreur de chargement MIDI : {ex.Message}"); }

                _mainThreadActions.Enqueue(() =>
                {
                    if (requestId != Volatile.Read(ref _playRequestId))
                        return; // annulé ou remplacé entre-temps

                    if (parsed == null)
                    {
                        lock (_lock) _pendingMusicName = "";
                        onDone?.Invoke(false);
                        return;
                    }

                    float position = startPosition + (float)(Now() - requestedAt);
                    lock (_lock)
                    {
                        _pendingMusicName = "";
                        ActivateMusic(parsed);
                        StartPlayback(performersCopy, position);
                    }
                    onDone?.Invoke(true);
                });
            });
        }

        // =====================================================================================
        //  Lecture
        // =====================================================================================
        public static void Play()
        {
            PlayWithBand(Array.Empty<MusicPerformer>());
        }

        public static void PlayWithBand(IReadOnlyList<MusicPerformer> performers)
        {
            lock (_lock)
            {
                if (!_initialized || _midiFile == null || _sequencer == null)
                {
                    Console.WriteLine(" Aucune musique chargée ou lecteur non initialisé.");
                    return;
                }
                StartPlayback(performers, 0f);
            }
        }

        // Démarre la lecture du morceau courant (sous _lock).
        private static void StartPlayback(IReadOnlyList<MusicPerformer> performers, float startPosition)
        {
            if (_midiFile == null || _sequencer == null) return;

            _synthesizer?.Reset();
            _bandSynthesizer?.Reset();
            _sequencer.Stop();
            ClearParticleQueue();

            _ensembleMode = false;
            _bandMusicEvents = new List<BandMusicEvent>();
            _nextBandMusicEvent = 0;
            _tailEndSample = 0;
            _bandPerformers = performers
                .Where(performer => performer.Program >= 0 && performer.Program <= 127)
                .Take(15)
                .ToArray();
            _bandPerformerChannels = Array.Empty<int>();
            foreach (var note in _events)
                note.PerformerIndex = -1;

            if (_bandPerformers.Length > 0 && _bandSynthesizer != null)
                BuildBandPlan();

            if (!_ensembleMode)
            {
                if (_bandPerformers.Length > 0)
                    Console.WriteLine("   Aucune note attribuable aux musiciens : lecture du morceau complet.");
                _bandPerformers = Array.Empty<MusicPerformer>();
                _sequencer.Play(_midiFile, false);
            }
            Interlocked.Increment(ref _notesVersion); // les PerformerIndex ont changé

            _samplesRendered = 0;
            _currentPlayTime = 0f;
            _isPlaying = true;
            _isPaused = false;

            if (startPosition > 0.05f)
                SeekInternal(startPosition);

            EnsureStreamPlaying();
            Console.WriteLine($"▶ Lecture démarrée ({_events.Count} notes, {_bandPerformers.Length} musiciens, départ à {startPosition:F2}s)");
        }

        // Prépare canaux, programmes et événements du groupe (sous _lock).
        private static void BuildBandPlan()
        {
            int count = _bandPerformers.Length;
            _bandPerformerChannels = new int[count];
            _bandGain = Enumerable.Repeat(1f, count).ToArray();
            _bandPan = new float[count];
            _bandSentVolume = Enumerable.Repeat(DEFAULT_CC_VOLUME, count).ToArray();
            _bandSentPan = Enumerable.Repeat(DEFAULT_CC_PAN, count).ToArray();

            int nextMelodicChannel = 0;
            bool drumKitConfigured = false;
            for (int i = 0; i < count; i++)
            {
                var performer = _bandPerformers[i];
                int channel;
                if (performer.IsPercussion)
                {
                    channel = 9;
                    if (!drumKitConfigured)
                    {
                        _bandSynthesizer!.ProcessMidiMessage(channel, 0xB0, 0, 1);
                        _bandSynthesizer.ProcessMidiMessage(channel, 0xB0, 32, 0);
                        _bandSynthesizer.ProcessMidiMessage(channel, 0xC0, 0, 0);
                        drumKitConfigured = true;
                    }
                }
                else
                {
                    if (nextMelodicChannel == 9)
                        nextMelodicChannel++;
                    channel = nextMelodicChannel++;
                    _bandSynthesizer!.ProcessMidiMessage(channel, 0xC0, performer.Program, 0);
                }
                _bandSynthesizer!.ProcessMidiMessage(channel, 0xB0, 7, DEFAULT_CC_VOLUME);
                _bandSynthesizer.ProcessMidiMessage(channel, 0xB0, 10, DEFAULT_CC_PAN);
                _bandPerformerChannels[i] = channel;
            }

            int[] map = BuildNotePerformerMap(_events, _bandPerformers);
            for (int i = 0; i < _events.Count; i++)
            {
                var note = _events[i];
                int performerIndex = map[i];
                note.PerformerIndex = performerIndex;
                if (performerIndex < 0) continue;

                int channel = _bandPerformerChannels[performerIndex];
                long startSample = Math.Max(0, (long)Math.Round(note.StartTime * SAMPLE_RATE));
                long endSample = Math.Max(startSample + 1, (long)Math.Round((note.StartTime + note.Duration) * SAMPLE_RATE));
                _bandMusicEvents.Add(new BandMusicEvent(startSample, channel, note.Pitch, note.Velocity, note.Duration, performerIndex, true));
                _bandMusicEvents.Add(new BandMusicEvent(endSample, channel, note.Pitch, 0, 0f, performerIndex, false));
            }
            _bandMusicEvents.Sort((left, right) =>
            {
                int sampleComparison = left.Sample.CompareTo(right.Sample);
                if (sampleComparison != 0) return sampleComparison;
                return left.IsNoteOn.CompareTo(right.IsNoteOn); // les NoteOff avant les NoteOn
            });
            _ensembleMode = _bandMusicEvents.Count > 0;
        }

        /// <summary>
        /// Associe chaque note du MIDI à un musicien selon son canal d'origine (et non plus en
        /// tourniquet) : la piste de percussions (canal 10) va au percussionniste, les autres pistes
        /// sont distribuées aux instruments mélodiques, la plus fournie en notes d'abord.
        /// Avec un seul instrument mélodique, seule la piste principale est jouée plutôt que de
        /// les écraser toutes sur un seul instrument. Valeur -1 : note non jouée.
        /// </summary>
        private static int[] BuildNotePerformerMap(List<NoteDisplayInfo> notes, MusicPerformer[] performers)
        {
            var map = new int[notes.Count];
            Array.Fill(map, -1);

            var melodic = new List<int>();
            int drummer = -1;
            for (int i = 0; i < performers.Length; i++)
            {
                if (performers[i].IsPercussion) { if (drummer < 0) drummer = i; }
                else melodic.Add(i);
            }

            var channelRank = notes
                .Where(n => n.InstrumentIndex != 9)
                .GroupBy(n => n.InstrumentIndex)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => g.Key)
                .ToList();

            var channelToPerformer = new Dictionary<int, int>();
            if (melodic.Count > 0)
            {
                int usableChannels = melodic.Count == 1 ? 1 : channelRank.Count;
                for (int rank = 0; rank < usableChannels && rank < channelRank.Count; rank++)
                    channelToPerformer[channelRank[rank]] = melodic[rank % melodic.Count];
            }

            for (int i = 0; i < notes.Count; i++)
            {
                int channel = notes[i].InstrumentIndex;
                if (channel == 9) map[i] = drummer;
                else if (channelToPerformer.TryGetValue(channel, out int performerIndex)) map[i] = performerIndex;
            }
            return map;
        }

        /// <summary>
        /// Se place à 'seconds' dans le morceau (temps entendu). Sert à la synchronisation réseau.
        /// </summary>
        public static void SeekTo(float seconds)
        {
            if (!float.IsFinite(seconds)) return;
            lock (_lock)
            {
                if (_isPlaying)
                    SeekInternal(seconds);
            }
        }

        private static void SeekInternal(float heardSeconds)
        {
            heardSeconds = Math.Clamp(heardSeconds, 0f, Math.Max(0f, _totalDuration));
            // Position de rendu = position entendue + latence (sauf tout début de morceau).
            long target = heardSeconds <= OUTPUT_LATENCY ? 0 : (long)((heardSeconds + OUTPUT_LATENCY) * SAMPLE_RATE);

            if (_ensembleMode)
            {
                _bandSynthesizer?.NoteOffAll(true);
                int lo = 0, hi = _bandMusicEvents.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (_bandMusicEvents[mid].Sample < target) lo = mid + 1; else hi = mid;
                }
                _nextBandMusicEvent = lo;
            }
            else if (_sequencer != null && _midiFile != null)
            {
                // MeltySynth ne sait pas se déplacer : on relance et on "consomme" l'audio jusqu'à la position.
                _synthesizer?.Reset();
                _sequencer.Stop();
                _sequencer.Play(_midiFile, false);
                long skipped = 0;
                while (skipped < target)
                {
                    int block = (int)Math.Min(RENDER_FRAMES, target - skipped);
                    _sequencer.Render(_srcL.AsSpan(0, block), _srcR.AsSpan(0, block));
                    skipped += block;
                }
            }

            _samplesRendered = target;
            _currentPlayTime = heardSeconds;
        }

        // Pause/reprise ne concernent que le morceau : les notes jouées au clavier restent audibles.
        public static void Pause()
        {
            lock (_lock)
            {
                if (_isPlaying && !_isPaused)
                {
                    _isPaused = true;
                    Console.WriteLine($"Pause à t={_currentPlayTime:F1}s");
                }
            }
        }

        public static void Resume()
        {
            lock (_lock)
            {
                if (_isPlaying && _isPaused)
                {
                    _isPaused = false;
                    EnsureStreamPlaying();
                    Console.WriteLine($"Reprise à t={_currentPlayTime:F1}s");
                }
            }
        }

        public static void Stop()
        {
            lock (_lock)
                StopInternal();
            Console.WriteLine("Arrêt");
        }

        // Arrête le morceau (sous _lock) et annule tout chargement asynchrone en cours.
        private static void StopInternal()
        {
            Interlocked.Increment(ref _playRequestId);
            _pendingMusicName = "";
            _isPlaying = false;
            _isPaused = false;
            _samplesRendered = 0;
            _tailEndSample = 0;
            _currentPlayTime = 0f;
            _sequencer?.Stop();
            _synthesizer?.NoteOffAll(true);
            _bandSynthesizer?.NoteOffAll(true);
            _bandMusicEvents = new List<BandMusicEvent>();
            _bandPerformers = Array.Empty<MusicPerformer>();
            _bandPerformerChannels = Array.Empty<int>();
            _nextBandMusicEvent = 0;
            _ensembleMode = false;
            ClearParticleQueue();
        }

        private static void ClearParticleQueue()
        {
            while (_particleQueue.TryDequeue(out _)) { }
        }

        public static int GetPlayedNotes()
        {
            lock (_lock)
                return _events.Count(e => e.StartTime <= _currentPlayTime);
        }

        public static int GetTotalNotes()
        {
            lock (_lock)
                return _events.Count;
        }

        public static float GetProgress()
        {
            return _totalDuration > 0 ? Math.Clamp(_currentPlayTime / _totalDuration, 0f, 1f) : 0f;
        }

        public static int GetCurrentTempo() => _originalTempo;

        // =====================================================================================
        //  Rendu audio (thread dédié)
        // =====================================================================================
        private static void EnsureStreamPlaying()
        {
            if (_streamReady && !_audioStreamPlaying)
            {
                Raylib.PlayAudioStream(_stream);
                _audioStreamPlaying = true;
            }
        }

        private static void PumpLoop()
        {
            while (_pumpRunning)
            {
                try
                {
                    lock (_lock)
                        PumpOnce();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" Erreur pendant le rendu audio MIDI : {ex.Message}");
                    Console.WriteLine($"   {ex.StackTrace}");
                    try { lock (_lock) StopInternal(); } catch { }
                }
                Thread.Sleep(2);
            }
        }

        private static void ComputeRenderFlags(out bool solo, out bool band, out bool live)
        {
            bool songRunning = _isPlaying && !_isPaused;
            bool tail = !_isPlaying && _samplesRendered < _tailEndSample; // fin de morceau (réverb./release)
            solo = !_ensembleMode && _sequencer != null && (songRunning || tail);
            band = _ensembleMode && (songRunning || tail);
            live = _scheduledNoteOffs.Count > 0 || Now() < _instrumentAudioUntil;
        }

        private static void PumpOnce()
        {
            if (!_initialized || !_streamReady) return;

            ComputeRenderFlags(out bool solo, out bool band, out bool live);
            if (!solo && !band && !live)
            {
                // Rien à jouer : on coupe le flux une fois les derniers buffers consommés.
                if (_audioStreamPlaying && Raylib.IsAudioStreamProcessed(_stream))
                {
                    Raylib.StopAudioStream(_stream);
                    _audioStreamPlaying = false;
                }
                return;
            }

            EnsureStreamPlaying();
            int safety = 4;
            while (safety-- > 0 && Raylib.IsAudioStreamProcessed(_stream))
            {
                if (!RenderBlock()) break;
            }
        }

        private static unsafe bool RenderBlock()
        {
            ComputeRenderFlags(out bool renderSolo, out bool renderBand, out bool renderLive);
            if (!renderSolo && !renderBand && !renderLive)
                return false;

            Array.Clear(_mixL);
            Array.Clear(_mixR);

            if (renderSolo)
            {
                _sequencer!.Render(_srcL, _srcR);
                AddToMix();
            }

            if (renderBand)
            {
                RenderBandBlock();
                AddToMix();
            }

            if (renderLive)
            {
                double renderTime = Now();
                for (int i = _scheduledNoteOffs.Count - 1; i >= 0; i--)
                {
                    var pending = _scheduledNoteOffs[i];
                    if (pending.EndTime <= renderTime)
                    {
                        _instrumentSynthesizer!.NoteOff(pending.Channel, pending.Pitch);
                        _scheduledNoteOffs.RemoveAt(i);
                    }
                }
                _instrumentSynthesizer!.Render(_srcL, _srcR);
                AddToMix();
            }

            // Mixage en float puis limiteur doux (pas d'écrêtage dur quand plusieurs sources s'additionnent).
            float volume = _musicVolume;
            for (int i = 0; i < RENDER_FRAMES; i++)
            {
                _renderBuffer[i * CHANNELS] = ToPcm(SoftLimit(_mixL[i] * volume));
                _renderBuffer[i * CHANNELS + 1] = ToPcm(SoftLimit(_mixR[i] * volume));
            }

            fixed (short* bufferPtr = _renderBuffer)
            {
                Raylib.UpdateAudioStream(_stream, bufferPtr, RENDER_FRAMES);
            }

            if (renderSolo || renderBand)
            {
                _samplesRendered += RENDER_FRAMES;
                if (_isPlaying)
                    _currentPlayTime = (float)Math.Max(0.0, _samplesRendered / (double)SAMPLE_RATE - OUTPUT_LATENCY);
            }

            CheckSongEnd();
            return true;
        }

        private static void AddToMix()
        {
            for (int i = 0; i < RENDER_FRAMES; i++)
            {
                _mixL[i] += _srcL[i];
                _mixR[i] += _srcR[i];
            }
        }

        private static float SoftLimit(float x)
        {
            if (!float.IsFinite(x)) return 0f;
            float a = MathF.Abs(x);
            const float knee = 0.8f;
            if (a <= knee) return x;
            float y = knee + (1f - knee) * MathF.Tanh((a - knee) / (1f - knee));
            return x < 0f ? -y : y;
        }

        private static short ToPcm(float x) =>
            (short)Math.Clamp((int)MathF.Round(x * 32767f), short.MinValue, short.MaxValue);

        private static void CheckSongEnd()
        {
            if (_isPlaying && !_isPaused)
            {
                bool ended = _ensembleMode
                    ? _currentPlayTime >= _totalDuration && _nextBandMusicEvent >= _bandMusicEvents.Count
                    : _sequencer != null && _sequencer.EndOfSequence && _currentPlayTime >= _totalDuration;
                if (ended)
                {
                    _isPlaying = false;
                    _tailEndSample = _samplesRendered + (long)(SAMPLE_RATE * 1.5);
                    _lastEndedName = _currentMusicName;
                    _lastEndedAt = Now();
                    Console.WriteLine("Musique terminée");
                }
            }
            else if (!_isPlaying && _ensembleMode && _samplesRendered >= _tailEndSample)
            {
                _ensembleMode = false;
            }
        }

        // Rend un bloc du groupe dans _srcL/_srcR, en déclenchant les notes au sample près.
        private static void RenderBandBlock()
        {
            var synth = _bandSynthesizer;
            if (synth == null)
            {
                Array.Clear(_srcL);
                Array.Clear(_srcR);
                return;
            }

            int frame = 0;
            long blockStart = _samplesRendered;
            double blockDue = Now() + OUTPUT_LATENCY;
            while (frame < RENDER_FRAMES)
            {
                long currentSample = blockStart + frame;
                while (_nextBandMusicEvent < _bandMusicEvents.Count
                    && _bandMusicEvents[_nextBandMusicEvent].Sample <= currentSample)
                {
                    var noteEvent = _bandMusicEvents[_nextBandMusicEvent++];
                    var performer = _bandPerformers[noteEvent.PerformerIndex];
                    int pitch = performer.IsPercussion ? BAND_PERCUSSION_PITCH : noteEvent.Pitch;
                    if (noteEvent.IsNoteOn)
                    {
                        synth.NoteOn(noteEvent.Channel, pitch, noteEvent.Velocity);
                        // Particule émise quand la note est réellement entendue, pas quand elle est rendue.
                        if (_particleQueue.Count < 256)
                            _particleQueue.Enqueue(new ParticleRequest(
                                blockDue + frame / (double)SAMPLE_RATE,
                                performer, noteEvent.Pitch, noteEvent.Velocity, noteEvent.Duration));
                    }
                    else
                    {
                        synth.NoteOff(noteEvent.Channel, pitch);
                    }
                }

                int renderFrames = RENDER_FRAMES - frame;
                if (_nextBandMusicEvent < _bandMusicEvents.Count)
                {
                    long untilNext = _bandMusicEvents[_nextBandMusicEvent].Sample - currentSample;
                    if (untilNext > 0 && untilNext < renderFrames)
                        renderFrames = (int)untilNext;
                }

                synth.Render(_srcL.AsSpan(frame, renderFrames), _srcR.AsSpan(frame, renderFrames));
                frame += renderFrames;
            }
        }

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