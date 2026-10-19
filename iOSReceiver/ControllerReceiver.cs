using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace ZeroCastReceiver;

/// <summary>Receives controller state without requiring a connected UDP socket.</summary>
public sealed class ControllerReceiver : IDisposable
{
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    public event Action<string>? StatusChanged;

    public void Start()
    {
        if (_client is not null) return;
        _cts = new CancellationTokenSource();
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, 58092));
        var client = _client;
        var token = _cts.Token;
        StatusChanged?.Invoke("Listening for Xbox controller on UDP 58092");
        _ = Task.Run(() => ReceiveLoop(client, token));
    }

    private void ReceiveLoop(UdpClient client, CancellationToken token)
    {
        // The synchronous endpoint-based API avoids iOS's unconnected UDP
        // ReceiveAsync path throwing SocketException: NotConnected.
        var sender = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            while (!token.IsCancellationRequested)
            {
                byte[] packet = client.Receive(ref sender);
                if (packet.Length != 14)
                {
                    Report($"Ignoring {packet.Length}-byte packet from {sender.Address}");
                    continue;
                }

                ushort buttons = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
                byte leftTrigger = packet[2], rightTrigger = packet[3];
                short lx = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(4, 2));
                short ly = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(6, 2));
                short rx = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(8, 2));
                short ry = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(10, 2));
                Report($"From {sender.Address} | Buttons: 0x{buttons:X4} | LT: {leftTrigger} | RT: {rightTrigger} | L: {lx},{ly} | R: {rx},{ry}");
            }
        }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            Report($"Controller receive error ({e.GetType().Name}): {e.Message}");
        }
    }

    private void Report(string status) =>
        MainThread.BeginInvokeOnMainThread(() => StatusChanged?.Invoke(status));

    public void Dispose()
    {
        _cts?.Cancel();
        _client?.Dispose();
        _cts?.Dispose();
        _client = null;
        _cts = null;
    }
}
