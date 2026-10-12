using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using AVFoundation;
using Concentus.Structs;

namespace Lughole;

// Talks to Lughole.Server: sends a "LUGHOLE" hello once a second so the PC
// knows where to stream, then decodes the Opus packets that come back and
// plays them through AVAudioEngine.
public sealed class AudioReceiver(Action<string> reportStatus)
{
    public const int DefaultPort = 58091;

    const int SampleRate = 48000;
    const int Channels = 2;
    const int MaxFrameSize = 2880; // 60 ms, the largest Opus frame
    const int StartAfterFrames = 3; // ~60 ms cushion before playback starts
    const int MaxQueuedFrames = 10; // ~200 ms; beyond this we drop to keep latency down
    static readonly byte[] Hello = Encoding.ASCII.GetBytes("LUGHOLE");

    int _queued;

    public async Task RunAsync(string host, int port, CancellationToken ct)
    {
        reportStatus($"Looking up {host}…");
        var server = new IPEndPoint(await ResolveAsync(host, ct), port);

        StartAudioSession();
        var format = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, SampleRate, Channels, false);
        var engine = new AVAudioEngine();
        var player = new AVAudioPlayerNode();
        engine.AttachNode(player);
        engine.Connect(player, engine.MainMixerNode, format);
        engine.Prepare();
        if (!engine.StartAndReturnError(out var startError))
            throw new InvalidOperationException(startError?.LocalizedDescription ?? "Audio engine wouldn't start.");

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        long lastPacketTicks = 0;
        var helloTask = SendHellosAsync(udp, server, () => Interlocked.Read(ref lastPacketTicks), ct);

        var decoder = CreateDecoder();
        var pcm = new float[MaxFrameSize * Channels];
        var left = new float[MaxFrameSize];
        var right = new float[MaxFrameSize];
        bool playing = false;
        _queued = 0;

        try
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
                    break;
                }
                catch (SocketException)
                {
                    continue;
                }

                if (!result.RemoteEndPoint.Address.Equals(server.Address))
                    continue;

                if (Interlocked.Exchange(ref lastPacketTicks, DateTime.UtcNow.Ticks) == 0)
                    reportStatus($"Playing audio from {host}");

                // Phone calls and other apps can stop the engine; pick back up.
                if (!engine.Running)
                {
                    StartAudioSession();
                    engine.StartAndReturnError(out _);
                    playing = false;
                }

                if (Volatile.Read(ref _queued) >= MaxQueuedFrames)
                    continue;

                int frames;
                try
                {
                    frames = decoder.Decode(result.Buffer, pcm, MaxFrameSize, false);
                }
                catch
                {
                    continue;
                }
                if (frames <= 0)
                    continue;

                for (int i = 0; i < frames; i++)
                {
                    left[i] = pcm[i * 2];
                    right[i] = pcm[i * 2 + 1];
                }

                var buffer = new AVAudioPcmBuffer(format, (uint)frames) { FrameLength = (uint)frames };
                var channelPointers = buffer.FloatChannelData;
                Marshal.Copy(left, 0, Marshal.ReadIntPtr(channelPointers, 0), frames);
                Marshal.Copy(right, 0, Marshal.ReadIntPtr(channelPointers, IntPtr.Size), frames);

                Interlocked.Increment(ref _queued);
                player.ScheduleBuffer(buffer, () => Interlocked.Decrement(ref _queued));

                if (!playing && Volatile.Read(ref _queued) >= StartAfterFrames)
                {
                    player.Play();
                    playing = true;
                }
            }
        }
        finally
        {
            player.Stop();
            engine.Stop();
            AVAudioSession.SharedInstance().SetActive(false);
            await helloTask;
        }
    }

    async Task SendHellosAsync(UdpClient udp, IPEndPoint server, Func<long> lastPacketTicks, CancellationToken ct)
    {
        reportStatus($"Waiting for {server.Address}… is Lughole.Server running?");
        bool silent = false;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await udp.SendAsync(Hello, server, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                // Wi-Fi dropped or local network permission not granted yet; keep trying.
            }

            long last = lastPacketTicks();
            if (last != 0)
            {
                bool nowSilent = DateTime.UtcNow - new DateTime(last, DateTimeKind.Utc) > TimeSpan.FromSeconds(3);
                if (nowSilent != silent)
                {
                    silent = nowSilent;
                    reportStatus(silent
                        ? "Connected, but nothing's playing on the PC."
                        : $"Playing audio from {server.Address}");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    static void StartAudioSession()
    {
        var session = AVAudioSession.SharedInstance();
        session.SetCategory(AVAudioSessionCategory.Playback);
        session.SetActive(true);
    }

    static async Task<IPAddress> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var ip))
            return ip;
        var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
        return addresses.FirstOrDefault() ?? throw new InvalidOperationException($"Couldn't find {host} on the network.");
    }

    // The fully managed decoder; avoids Concentus probing for a native libopus,
    // which doesn't exist on iOS.
#pragma warning disable CS0618
    static OpusDecoder CreateDecoder() => new(SampleRate, Channels);
#pragma warning restore CS0618
}
