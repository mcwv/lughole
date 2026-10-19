using System.Net;
using System.Net.Sockets;
using AVFoundation;
using Concentus.Structs;
using Microsoft.Maui.Controls;
using System.Runtime.InteropServices;

namespace ZeroCastReceiver;

public partial class MainPage : ContentPage
{
    private bool _isListening;
    private readonly ControllerReceiver _controllerReceiver = new();

    public MainPage()
    {
        InitializeComponent();
        _controllerReceiver.StatusChanged += status => ControllerStatusLabel.Text = status;
        ListenButton.Clicked += (_, _) =>
        {
            if (_isListening) return;
            _isListening = true;
            ListenButton.IsEnabled = false;
            _ = Task.Run(StartListening);
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try { _controllerReceiver.Start(); }
        catch (Exception ex) { ControllerStatusLabel.Text = $"Controller listener error: {ex.Message}"; }
    }

    protected override void OnDisappearing()
    {
        _controllerReceiver.Dispose();
        base.OnDisappearing();
    }

    private void StartListening()
    {
#if IOS
        var engine = new AVAudioEngine();
        var playerNode = new AVAudioPlayerNode();
        engine.AttachNode(playerNode);

        var format = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, 48000, 2, false);
        engine.Connect(playerNode, engine.OutputNode, format);
        engine.StartAndReturnError(out _);
        playerNode.Play();

        using var udpClient = new UdpClient(58091);
        var endPoint = new IPEndPoint(IPAddress.Any, 58091);
        var decoder = new OpusDecoder(48000, 2);

        const int frameSize = 960;
        var pcmOut = new float[frameSize * 2];

        while (_isListening)
        {
            try
            {
                byte[] packet = udpClient.Receive(ref endPoint);
                int decodedFrames = decoder.Decode(packet, 0, packet.Length, pcmOut, 0, frameSize, false);

                if (decodedFrames <= 0)
                    continue;

                var buffer = new AVAudioPcmBuffer(format, (uint)decodedFrames);
                buffer.FrameLength = (uint)decodedFrames;

                // The decoder outputs interleaved stereo floats. Copy those actual
                // samples into the native playback buffer (the old code copied
                // the managed array back into itself and played silence).
                var channels = buffer.FloatChannelData;
                if (channels is null || channels.Length == 0)
                    throw new InvalidOperationException("No native float audio buffer");
                Marshal.Copy(pcmOut, 0, channels[0], decodedFrames * 2);

                playerNode.ScheduleBuffer(buffer, null);
            }
            catch
            {
                // Drop packet if anything goes wrong.
            }
        }
#endif
    }
}
