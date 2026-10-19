using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The controller sender requires Windows.");
    return;
}

if (args.Length == 0 || !IPAddress.TryParse(args[0], out var address) || address.AddressFamily != AddressFamily.InterNetwork)
{
    Console.Error.WriteLine("Usage: dotnet run --project ControllerServer -- <iphone-ip> [controller-index]");
    return;
}

var index = 0;
if (args.Length > 1 && (!int.TryParse(args[1], out index) || index is < 0 or > 3))
{
    Console.Error.WriteLine("Controller index must be between 0 and 3.");
    return;
}

using var udp = new UdpClient();
var destination = new IPEndPoint(address, 58092);
Console.WriteLine($"Lughole XInput -> {destination} (controller {index}; Ctrl+C to stop)");
uint? previousPacket = null;
var wasConnected = false;
var previousSend = DateTime.UtcNow;
byte[] payload = new byte[14];

while (true)
{
    var state = new XInputState();
    int status = XInputGetState(index, ref state);
    if (status == 0)
    {
        if (!wasConnected)
        {
            Console.WriteLine("Controller connected.");
            previousPacket = null;
            wasConnected = true;
        }

        // An occasional heartbeat lets the receiver detect that the stream is still alive.
        if (previousPacket != state.PacketNumber || (DateTime.UtcNow - previousSend).TotalMilliseconds >= 500)
        {
            previousPacket = state.PacketNumber;
            previousSend = DateTime.UtcNow;
            var gamepad = state.Gamepad;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), gamepad.Buttons);
            payload[2] = gamepad.LeftTrigger;
            payload[3] = gamepad.RightTrigger;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(4, 2), gamepad.ThumbLX);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6, 2), gamepad.ThumbLY);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(8, 2), gamepad.ThumbRX);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(10, 2), gamepad.ThumbRY);
            // The original spec says 14 bytes, but its listed fields total 12.
            // Reserve the final two bytes for protocol version 1 (zero).
            payload[12] = 0;
            payload[13] = 0;
            udp.Send(payload, payload.Length, destination);
            Console.Write($"\rButtons 0x{gamepad.Buttons:X4} LT {gamepad.LeftTrigger,3} RT {gamepad.RightTrigger,3}     ");
        }
    }
    else if (wasConnected)
    {
        Console.WriteLine("\nController disconnected.");
        wasConnected = false;
        previousPacket = null;
        // Neutral state on disconnect prevents stuck inputs on the receiver.
        Array.Clear(payload);
        udp.Send(payload, payload.Length, destination);
    }

    Thread.Sleep(10);
}

[DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
static extern int XInputGetState(int dwUserIndex, ref XInputState state);

[StructLayout(LayoutKind.Sequential)]
struct XInputState
{
    public uint PacketNumber;
    public XInputGamepad Gamepad;
}

[StructLayout(LayoutKind.Sequential)]
struct XInputGamepad
{
    public ushort Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short ThumbLX;
    public short ThumbLY;
    public short ThumbRX;
    public short ThumbRY;
}
