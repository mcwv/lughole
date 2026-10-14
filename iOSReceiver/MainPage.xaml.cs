using System.Net;
using System.Net.Sockets;
using AVFoundation;
using Concentus;
using Microsoft.Maui.Controls;

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

        var format = new AVAudioFormat(AVAudioCommonFormat.PCMFormatFloat32, 48000, 2, false);
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

                var buffer = new AVAudioPCMBuffer(format, (nuint)decodedFrames);
                buffer.FrameLength = (nuint)decodedFrames;

                unsafe
                {
                    fixed (float* ptr = pcmOut)
                    {
                        var data = (IntPtr)ptr;
                        System.Runtime.InteropServices.Marshal.Copy(pcmOut, 0, data, decodedFrames * 2);
                    }
                }

                playerNode.ScheduleBuffer(buffer, out _);
            }
            catch
            {
                // Drop packet if anything goes wrong.
            }
        }
#endif
    }
}
