using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;

namespace Soulfract
{
    public static class VoiceChat
    {
        private const int SampleRate = 24000;
        private const int FrameSamples = 480;
        private const int FrameBytes = FrameSamples * 2;
        private const int PacketHeaderBytes = 7;
        private const byte RegisterPacket = 1;
        private const byte VoicePacket = 2;
        private static readonly WaveFormat VoiceFormat = new(SampleRate, 16, 1);
        private static readonly ConcurrentDictionary<int, IPEndPoint> HostPeers = new();
        private static readonly ConcurrentDictionary<int, BufferedWaveProvider> VoiceSources = new();
        private static readonly object CaptureLock = new();
        private static readonly object MicrophoneLock = new();
        private static readonly byte[] CaptureFrame = new byte[FrameBytes];
        private static int _captureFrameCount;
        private static UdpClient? _udp;
        private static CancellationTokenSource? _receiveCancellation;
        private static Task? _receiveTask;
        private static WaveInEvent? _microphone;
        private static WaveOutEvent? _speaker;
        private static Func<int, string?>? _getVoiceToken;
        private static bool _isHost;
        private static int _localConnectionId;
        private static string _voiceToken = string.Empty;
        private static DateTime _lastRegistration;
        private static DateTime _microphoneRetryAfter;
        private static volatile bool _transmitting;

        public static bool IsTransmitting => _transmitting;

        public static void StartHost(int port, Func<int, string?> getVoiceToken)
        {
            Stop();
            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                _isHost = true;
                _getVoiceToken = getVoiceToken;
                StartPlayback();
                StartReceiver();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Voice] Impossible d'ouvrir le canal vocal : {ex.Message}");
                Stop();
            }
        }

        public static void StartClient(IPAddress hostAddress, int port, int connectionId, string token)
        {
            Stop();
            try
            {
                _udp = new UdpClient(AddressFamily.InterNetwork);
                _udp.Connect(new IPEndPoint(hostAddress, port));
                _localConnectionId = connectionId;
                _voiceToken = token;
                StartPlayback();
                StartReceiver();
                SendRegistration();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Voice] Impossible de rejoindre le canal vocal : {ex.Message}");
                Stop();
            }
        }

        public static void Update(bool pushToTalk)
        {
            if (_udp == null)
                return;

            if (!_isHost && (DateTime.UtcNow - _lastRegistration).TotalSeconds >= 2)
                SendRegistration();

            if (pushToTalk)
            {
                StartMicrophone();
            }
            else
            {
                StopMicrophone();
            }
        }

        public static void Stop()
        {
            _transmitting = false;
            StopMicrophone();
            try { _receiveCancellation?.Cancel(); } catch { }
            try { _udp?.Close(); } catch { }
            try { _speaker?.Stop(); } catch { }
            try { _speaker?.Dispose(); } catch { }
            _speaker = null;
            _udp = null;
            _receiveCancellation?.Dispose();
            _receiveCancellation = null;
            _receiveTask = null;
            _getVoiceToken = null;
            _isHost = false;
            _localConnectionId = 0;
            _voiceToken = string.Empty;
            _lastRegistration = DateTime.MinValue;
            _microphoneRetryAfter = DateTime.MinValue;
            _captureFrameCount = 0;
            HostPeers.Clear();
            VoiceSources.Clear();
        }

        public static void Draw()
        {
            if (!_transmitting)
                return;

            const int width = 132;
            const int height = 30;
            int x = Raylib_cs.Raylib.GetScreenWidth() - width - 16;
            int y = 16;
            Raylib_cs.Raylib.DrawRectangleRounded(new Raylib_cs.Rectangle(x, y, width, height), 0.25f, 6, new Raylib_cs.Color(22, 38, 30, 230));
            Raylib_cs.Raylib.DrawCircle(x + 14, y + height / 2, 5, new Raylib_cs.Color(100, 235, 135, 255));
            FontManager.DrawText(Localization.GetOrDefault("voice.transmitting", "MIC ON"), x + 26, y + 7, 13, new Raylib_cs.Color(205, 245, 215, 255));
        }

        public static void RemovePeer(int connectionId)
        {
            HostPeers.TryRemove(connectionId, out _);
            VoiceSources.TryRemove(connectionId, out _);
        }

        private static void StartReceiver()
        {
            if (_udp == null)
                return;

            _receiveCancellation = new CancellationTokenSource();
            UdpClient udp = _udp;
            CancellationToken cancellationToken = _receiveCancellation.Token;
            _receiveTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        UdpReceiveResult result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                        if (_isHost)
                            HandleHostPacket(result.Buffer, result.RemoteEndPoint);
                        else
                            HandleClientPacket(result.Buffer);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (SocketException)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                            continue;
                        break;
                    }
                }
            }, cancellationToken);
        }

        private static void HandleHostPacket(byte[] packet, IPEndPoint remoteEndPoint)
        {
            if (!HasValidHeader(packet))
                return;

            int connectionId = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(3, 4));
            if (packet[2] == RegisterPacket)
            {
                string? expectedToken = _getVoiceToken?.Invoke(connectionId);
                if (connectionId <= 0 || string.IsNullOrEmpty(expectedToken))
                    return;

                byte[] expected = Encoding.UTF8.GetBytes(expectedToken);
                ReadOnlySpan<byte> supplied = packet.AsSpan(PacketHeaderBytes);
                if (supplied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(supplied, expected))
                    return;

                HostPeers[connectionId] = remoteEndPoint;
                return;
            }

            if (packet[2] != VoicePacket || packet.Length != PacketHeaderBytes + FrameSamples)
                return;
            if (!HostPeers.TryGetValue(connectionId, out IPEndPoint? knownEndPoint) || !knownEndPoint.Equals(remoteEndPoint))
                return;

            ReadOnlySpan<byte> encodedAudio = packet.AsSpan(PacketHeaderBytes);
            PlayVoice(connectionId, encodedAudio);
            byte[] relayPacket = CreatePacket(VoicePacket, connectionId, encodedAudio);
            foreach (var peer in HostPeers)
            {
                if (peer.Key == connectionId)
                    continue;
                try { _udp?.Send(relayPacket, relayPacket.Length, peer.Value); } catch { }
            }
        }

        private static void HandleClientPacket(byte[] packet)
        {
            if (!HasValidHeader(packet) || packet[2] != VoicePacket || packet.Length != PacketHeaderBytes + FrameSamples)
                return;

            int speakerId = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(3, 4));
            if (speakerId < 0 || speakerId == _localConnectionId)
                return;
            PlayVoice(speakerId, packet.AsSpan(PacketHeaderBytes));
        }

        private static bool HasValidHeader(byte[] packet) =>
            packet.Length >= PacketHeaderBytes && packet[0] == (byte)'S' && packet[1] == (byte)'F';

        private static byte[] CreatePacket(byte type, int connectionId, ReadOnlySpan<byte> payload)
        {
            byte[] packet = new byte[PacketHeaderBytes + payload.Length];
            packet[0] = (byte)'S';
            packet[1] = (byte)'F';
            packet[2] = type;
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(3, 4), connectionId);
            payload.CopyTo(packet.AsSpan(PacketHeaderBytes));
            return packet;
        }

        private static void SendRegistration()
        {
            if (_udp == null || _isHost || string.IsNullOrEmpty(_voiceToken))
                return;

            byte[] token = Encoding.UTF8.GetBytes(_voiceToken);
            byte[] packet = CreatePacket(RegisterPacket, _localConnectionId, token);
            try
            {
                _udp.Send(packet, packet.Length);
                _lastRegistration = DateTime.UtcNow;
            }
            catch { }
        }

        private static void StartPlayback()
        {
            try
            {
                var mixer = new VoiceMixer(VoiceSources, VoiceFormat);
                _speaker = new WaveOutEvent();
                _speaker.Init(mixer);
                _speaker.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Voice] Lecture audio indisponible : {ex.Message}");
                try { _speaker?.Dispose(); } catch { }
                _speaker = null;
            }
        }

        private static void StartMicrophone()
        {
            lock (MicrophoneLock)
            {
                if (_microphone != null || DateTime.UtcNow < _microphoneRetryAfter)
                    return;

                WaveInEvent? microphone = null;
                try
                {
                    microphone = new WaveInEvent
                    {
                        WaveFormat = VoiceFormat,
                        BufferMilliseconds = 20,
                        NumberOfBuffers = 3
                    };
                    microphone.DataAvailable += OnMicrophoneData;
                    microphone.RecordingStopped += OnMicrophoneStopped;
                    _microphone = microphone;
                    microphone.StartRecording();
                    _transmitting = true;
                }
                catch (Exception ex)
                {
                    if (ReferenceEquals(_microphone, microphone))
                        _microphone = null;
                    _transmitting = false;
                    _microphoneRetryAfter = DateTime.UtcNow.AddSeconds(3);
                    if (microphone != null)
                    {
                        microphone.DataAvailable -= OnMicrophoneData;
                        microphone.RecordingStopped -= OnMicrophoneStopped;
                        try { microphone.Dispose(); } catch { }
                    }
                    Console.WriteLine($"[Voice] Micro indisponible : {ex.Message}");
                }
            }
        }

        private static void StopMicrophone()
        {
            WaveInEvent? microphone;
            lock (MicrophoneLock)
            {
                _transmitting = false;
                microphone = _microphone;
                _microphone = null;
            }

            if (microphone != null)
            {
                microphone.DataAvailable -= OnMicrophoneData;
                microphone.RecordingStopped -= OnMicrophoneStopped;
                try { microphone.StopRecording(); } catch { }
                try { microphone.Dispose(); } catch { }
            }

            lock (CaptureLock)
                _captureFrameCount = 0;
        }

        private static void OnMicrophoneStopped(object? sender, StoppedEventArgs args)
        {
            if (sender is not WaveInEvent microphone)
                return;

            lock (MicrophoneLock)
            {
                if (!ReferenceEquals(_microphone, microphone))
                    return;

                _microphone = null;
                _transmitting = false;
                _microphoneRetryAfter = DateTime.UtcNow.AddSeconds(3);
            }

            lock (CaptureLock)
                _captureFrameCount = 0;

            microphone.DataAvailable -= OnMicrophoneData;
            microphone.RecordingStopped -= OnMicrophoneStopped;
            try { microphone.Dispose(); } catch { }

            if (args.Exception != null)
                Console.WriteLine($"[Voice] Capture micro interrompue : {args.Exception.Message}");
            else
                Console.WriteLine("[Voice] Capture micro interrompue.");
        }

        private static void OnMicrophoneData(object? sender, WaveInEventArgs args)
        {
            List<byte[]> frames = new();
            lock (CaptureLock)
            {
                lock (MicrophoneLock)
                {
                    if (!_transmitting || !ReferenceEquals(sender, _microphone))
                        return;
                }

                int offset = 0;
                while (offset < args.BytesRecorded)
                {
                    int copyCount = Math.Min(FrameBytes - _captureFrameCount, args.BytesRecorded - offset);
                    Buffer.BlockCopy(args.Buffer, offset, CaptureFrame, _captureFrameCount, copyCount);
                    _captureFrameCount += copyCount;
                    offset += copyCount;
                    if (_captureFrameCount == FrameBytes)
                    {
                        frames.Add((byte[])CaptureFrame.Clone());
                        _captureFrameCount = 0;
                    }
                }
            }

            foreach (byte[] frame in frames)
            {
                byte[] encoded = new byte[FrameSamples];
                float inputGain = Math.Clamp(SettingsManager.Settings.VoiceInputVolume, 0, 300) / 100f;
                for (int i = 0; i < FrameSamples; i++)
                {
                    short sample = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(i * 2, 2));
                    short amplifiedSample = (short)Math.Clamp(
                        (int)Math.Round(sample * inputGain),
                        short.MinValue,
                        short.MaxValue);
                    encoded[i] = EncodeMuLaw(amplifiedSample);
                }
                SendVoice(encoded);
            }
        }

        private static void SendVoice(byte[] encodedAudio)
        {
            UdpClient? udp = _udp;
            if (udp == null || !_transmitting)
                return;

            if (_isHost)
            {
                byte[] packet = CreatePacket(VoicePacket, 0, encodedAudio);
                foreach (var peer in HostPeers)
                {
                    try { udp.Send(packet, packet.Length, peer.Value); } catch { }
                }
            }
            else
            {
                byte[] packet = CreatePacket(VoicePacket, _localConnectionId, encodedAudio);
                try { udp.Send(packet, packet.Length); } catch { }
            }
        }

        private static void PlayVoice(int speakerId, ReadOnlySpan<byte> encodedAudio)
        {
            if (_speaker == null)
                return;

            byte[] pcm = new byte[FrameBytes];
            for (int i = 0; i < FrameSamples; i++)
            {
                short sample = DecodeMuLaw(encodedAudio[i]);
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), sample);
            }

            var source = VoiceSources.GetOrAdd(speakerId, _ => new BufferedWaveProvider(VoiceFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(250),
                DiscardOnBufferOverflow = true
            });
            try { source.AddSamples(pcm, 0, pcm.Length); } catch { }
        }

        private static byte EncodeMuLaw(short sample)
        {
            int value = sample;
            int sign = (value >> 8) & 0x80;
            if (sign != 0)
                value = -value;
            value = Math.Min(value, 32635) + 0x84;
            int exponent = 7;
            for (int mask = 0x4000; exponent > 0 && (value & mask) == 0; exponent--, mask >>= 1) { }
            int mantissa = (value >> (exponent + 3)) & 0x0f;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        private static short DecodeMuLaw(byte encoded)
        {
            int value = (byte)~encoded;
            int sample = (((value & 0x0f) << 3) + 0x84) << ((value >> 4) & 0x07);
            sample -= 0x84;
            return (short)((value & 0x80) != 0 ? -sample : sample);
        }

        private sealed class VoiceMixer : IWaveProvider
        {
            private readonly ConcurrentDictionary<int, BufferedWaveProvider> _sources;

            public VoiceMixer(ConcurrentDictionary<int, BufferedWaveProvider> sources, WaveFormat waveFormat)
            {
                _sources = sources;
                WaveFormat = waveFormat;
            }

            public WaveFormat WaveFormat { get; }

            public int Read(byte[] buffer, int offset, int count)
            {
                Array.Clear(buffer, offset, count);
                int sampleCount = count / 2;
                int[] mixedSamples = new int[sampleCount];
                byte[] sourceBuffer = new byte[count];

                foreach (BufferedWaveProvider source in _sources.Values)
                {
                    Array.Clear(sourceBuffer, 0, sourceBuffer.Length);
                    int bytesRead = source.Read(sourceBuffer, 0, count);
                    for (int i = 0; i + 1 < bytesRead; i += 2)
                        mixedSamples[i / 2] += BinaryPrimitives.ReadInt16LittleEndian(sourceBuffer.AsSpan(i, 2));
                }

                float outputGain = Math.Clamp(SettingsManager.Settings.VoiceOutputVolume, 0, 300) / 100f;
                for (int i = 0; i < sampleCount; i++)
                {
                    short sample = (short)Math.Clamp(
                        (int)Math.Round(mixedSamples[i] * outputGain),
                        short.MinValue,
                        short.MaxValue);
                    BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset + i * 2, 2), sample);
                }
                return count;
            }
        }
    }
}
