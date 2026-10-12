using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Concentus;
using Concentus.Enums;
using NAudio.Wave;

namespace Lughole.Server;

// Lughole server: captures whatever Windows is playing (WASAPI loopback),
// encodes it as 20 ms Opus frames and sends them over UDP to every phone
// that has said hello in the last few seconds.
//
// Protocol (deliberately tiny):
//   phone -> PC : the ASCII bytes "LUGHOLE" roughly once a second (keepalive)
//   PC -> phone : one raw Opus packet per UDP datagram, 48 kHz stereo, 20 ms
static class Program
{
    const int DefaultPort = 58091;
    const int SampleRate = 48000;
    const int Channels = 2;
    const int FrameSize = 960; // 20 ms at 48 kHz
    static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);
    static readonly byte[] Hello = Encoding.ASCII.GetBytes("LUGHOLE");

    static readonly ConcurrentDictionary<IPEndPoint, DateTime> Clients = new();

    static void Main(string[] args)
    {
        int port = DefaultPort;
        if (args.Length > 0 && !int.TryParse(args[0], out port))
        {
            Console.WriteLine("Usage: Lughole.Server [port]");
            return;
        }

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        // Stop Windows from killing the socket when a phone disappears (ICMP port unreachable).
        const int SIO_UDP_CONNRESET = -1744830452;
        udp.Client.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);

        Console.WriteLine($"[Lughole] Listening on UDP port {port}.");
        Console.WriteLine("[Lughole] In the Lughole app on your phone, enter one of these addresses:");
        foreach (var ip in LocalIPv4Addresses())
            Console.WriteLine($"           {ip}");

        var cts = new CancellationTokenSource();
        var listener = Task.Run(() => ListenForClients(udp, cts.Token));

#pragma warning disable CS0618 // Still the simplest way to grab system-wide loopback in NAudio 3.
        using var capture = new WasapiLoopbackCapture();
#pragma warning restore CS0618
        var format = capture.WaveFormat;
        Console.WriteLine($"[Lughole] Capturing: {format.SampleRate} Hz, {format.Channels} ch, {format.BitsPerSample}-bit {format.Encoding}");

        var encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
        encoder.Bitrate = 128000;
        encoder.SignalType = OpusSignal.OPUS_SIGNAL_MUSIC;

        var resampler = new StereoResampler(format.SampleRate, SampleRate);
        var pending = new List<float>(FrameSize * Channels * 4);
        var frame = new float[FrameSize * Channels];
        var packet = new byte[1276];

        capture.DataAvailable += (_, e) =>
        {
            var stereo = ToStereoFloat(e.Buffer, e.BytesRecorded, format);
            resampler.Process(stereo, pending);

            while (pending.Count >= frame.Length)
            {
                pending.CopyTo(0, frame, 0, frame.Length);
                pending.RemoveRange(0, frame.Length);

                var targets = LiveClients();
                if (targets.Count == 0)
                    continue;

                try
                {
                    int length = encoder.Encode(frame, FrameSize, packet, packet.Length);
                    foreach (var client in targets)
                        udp.Send(packet, length, client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Lughole] Dropped a frame: {ex.Message}");
                }
            }
        };

        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null)
                Console.WriteLine($"[Lughole] Capture stopped: {e.Exception.Message}");
        };

        capture.StartRecording();
        Console.WriteLine("[Lughole] Streaming. Press ENTER to quit.");
        Console.ReadLine();

        capture.StopRecording();
        cts.Cancel();
    }

    static async Task ListenForClients(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            if (!result.Buffer.AsSpan().SequenceEqual(Hello))
                continue;

            if (Clients.TryAdd(result.RemoteEndPoint, DateTime.UtcNow))
                Console.WriteLine($"[Lughole] Phone connected: {result.RemoteEndPoint}");
            else
                Clients[result.RemoteEndPoint] = DateTime.UtcNow;
        }
    }

    static List<IPEndPoint> LiveClients()
    {
        var now = DateTime.UtcNow;
        var live = new List<IPEndPoint>();
        foreach (var (endpoint, lastSeen) in Clients)
        {
            if (now - lastSeen < ClientTimeout)
                live.Add(endpoint);
            else if (Clients.TryRemove(endpoint, out _))
                Console.WriteLine($"[Lughole] Phone disconnected: {endpoint}");
        }
        return live;
    }

    // Converts a raw capture buffer (any channel count, float or 16-bit PCM)
    // into interleaved stereo floats.
    static float[] ToStereoFloat(byte[] buffer, int bytes, WaveFormat format)
    {
        int bytesPerSample = format.BitsPerSample / 8;
        int inChannels = format.Channels;
        int frames = bytes / (bytesPerSample * inChannels);
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);

        var output = new float[frames * Channels];
        for (int f = 0; f < frames; f++)
        {
            int baseOffset = f * inChannels * bytesPerSample;
            float left = ReadSample(buffer, baseOffset, isFloat, bytesPerSample);
            float right = inChannels > 1
                ? ReadSample(buffer, baseOffset + bytesPerSample, isFloat, bytesPerSample)
                : left;
            output[f * 2] = left;
            output[f * 2 + 1] = right;
        }
        return output;
    }

    static float ReadSample(byte[] buffer, int offset, bool isFloat, int bytesPerSample)
    {
        if (isFloat)
            return BitConverter.ToSingle(buffer, offset);
        return bytesPerSample switch
        {
            2 => BitConverter.ToInt16(buffer, offset) / 32768f,
            3 => ((buffer[offset] << 8 | buffer[offset + 1] << 16 | buffer[offset + 2] << 24) >> 8) / 8388608f,
            4 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0f,
        };
    }

    static IEnumerable<IPAddress> LocalIPv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork);
}

// Linear-interpolation resampler for interleaved stereo. Only does work when
// Windows isn't already running at 48 kHz (e.g. a 44.1 kHz output device).
sealed class StereoResampler(int inputRate, int outputRate)
{
    readonly double _step = (double)inputRate / outputRate;
    readonly List<float> _input = new();
    double _position;

    public void Process(float[] stereo, List<float> output)
    {
        if (inputRate == outputRate)
        {
            output.AddRange(stereo);
            return;
        }

        _input.AddRange(stereo);
        int frames = _input.Count / 2;
        while (_position + 1 < frames)
        {
            int i = (int)_position;
            float t = (float)(_position - i);
            output.Add(_input[i * 2] + (_input[i * 2 + 2] - _input[i * 2]) * t);
            output.Add(_input[i * 2 + 1] + (_input[i * 2 + 3] - _input[i * 2 + 1]) * t);
            _position += _step;
        }

        int consumed = (int)_position;
        _input.RemoveRange(0, consumed * 2);
        _position -= consumed;
    }
}
