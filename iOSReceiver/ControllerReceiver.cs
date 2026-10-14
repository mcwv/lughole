using System.Buffers.Binary;
using System.Net.Sockets;

namespace ZeroCastReceiver;

/// <summary>Receives XInput state over UDP independently of the audio stream.</summary>
public sealed class ControllerReceiver : IDisposable
{
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    public event Action<string>? StatusChanged;

    public void Start()
    {
        if (_client is not null) return;
        _cts = new CancellationTokenSource();
        _client = new UdpClient(58092);
        StatusChanged?.Invoke("Listening for Xbox controller on UDP 58092");
        _ = Task.Run(() => ReceiveLoopAsync(_client, _cts.Token));
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(token);
                var packet = result.Buffer;
                if (packet.Length != 14) continue;
                ushort buttons = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
                byte leftTrigger = packet[2], rightTrigger = packet[3];
                short lx = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(4, 2));
                short ly = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(6, 2));
                short rx = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(8, 2));
                short ry = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(10, 2));
                MainThread.BeginInvokeOnMainThread(() =>
                    StatusChanged?.Invoke($"Buttons: 0x{buttons:X4} | LT: {leftTrigger} | RT: {rightTrigger} | L: {lx},{ly} | R: {rx},{ry}"));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception e)
        {
            MainThread.BeginInvokeOnMainThread(() => StatusChanged?.Invoke($"Controller error: {e.Message}"));
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _client?.Dispose();
        _cts?.Dispose();
        _client = null;
        _cts = null;
    }
}
