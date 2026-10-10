using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NAudio.Wave;
using Concentus.Structs;
using Concentus.Enums;

class Program
{
    static void Main()
    {
        // The exact IP and UDP port from your AudioCast logs
        string targetIp = "192.168.1.21"; 
        int targetPort = 58091;

        Console.WriteLine($"[ZeroCast] Initializing Server -> {targetIp}:{targetPort}");

        using var udpClient = new UdpClient();
        var endPoint = new IPEndPoint(IPAddress.Parse(targetIp), targetPort);

        using var capture = new WasapiLoopbackCapture();
        var format = capture.WaveFormat;
        Console.WriteLine($"[ZeroCast] WASAPI Hooked: {format.SampleRate}Hz {format.Channels}ch {format.BitsPerSample}bit");

        // FIXED: Using the standard constructor instead of a Create() method
        var encoder = new OpusEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO);
        encoder.Bitrate = 128000;
        encoder.SignalType = OpusSignal.OPUS_SIGNAL_MUSIC;

        int frameSize = 960; // 20ms frames at 48kHz
        int floatsPerFrame = frameSize * 2;
        byte[] packetBuffer = new byte[1276];
        
        List<float> sampleBuffer = new List<float>();

        capture.DataAvailable += (s, a) =>
        {
            int floatCount = a.BytesRecorded / 4;
            float[] pcm = new float[floatCount];
            Buffer.BlockCopy(a.Buffer, 0, pcm, 0, a.BytesRecorded);
            
            sampleBuffer.AddRange(pcm);

            // Slice exact 20ms frames and blast them over UDP instantly
            while (sampleBuffer.Count >= floatsPerFrame)
            {
                float[] frameData = sampleBuffer.GetRange(0, floatsPerFrame).ToArray();
                sampleBuffer.RemoveRange(0, floatsPerFrame);

                try
                {
                    int encodedLength = encoder.Encode(frameData, 0, frameSize, packetBuffer, 0, packetBuffer.Length);
                    udpClient.Send(packetBuffer, encodedLength, endPoint);
                }
                catch { } // Drop corrupt frames instantly to maintain real-time sync
            }
        };

        capture.StartRecording();
        Console.WriteLine("[ZeroCast] Streaming LIVE. Press ENTER to terminate.");
        Console.ReadLine();
        capture.StopRecording();
    }
}
