using Runway.Framing;

namespace Runway.Protocol;

public static class PacketBuilder
{
    public static Frame CreatePing(byte version, ushort sequence)
    {
        return new Frame(version, (byte)MessageType.Ping, sequence, Array.Empty<byte>());
    }

    public static Frame CreatePong(byte version, ushort sequence)
    {
        return new Frame(version, (byte)MessageType.Pong, sequence, Array.Empty<byte>());
    }

    public static Frame CreateTelemetry(
        byte version,
        ushort sequence,
        double temperature,
        double humidity
    )
    {
        ushort temp = (ushort)Math.Round(temperature * 100);
        ushort hum = (ushort)Math.Round(humidity * 100);

        byte[] payload =
        {
            (byte)(temp & 0xFF),
            (byte)(temp >> 8),
            (byte)(hum & 0xFF),
            (byte)(hum >> 8),
        };

        return new Frame(version, (byte)MessageType.Telemetry, sequence, payload);
    }
}
