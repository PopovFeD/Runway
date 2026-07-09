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

    public static Frame CreateEnvironment(
        byte version,
        ushort sequence,
        double pressureHpa,
        double lightLux
    )
    {
        // Схема как в ParseEnvironment: uint32 Па + uint32 сотые люкса, little-endian
        uint pressurePa = (uint)Math.Round(pressureHpa * 100);
        uint lightCentiLux = (uint)Math.Round(lightLux * 100);

        byte[] payload =
        {
            (byte)(pressurePa & 0xFF),
            (byte)((pressurePa >> 8) & 0xFF),
            (byte)((pressurePa >> 16) & 0xFF),
            (byte)((pressurePa >> 24) & 0xFF),
            (byte)(lightCentiLux & 0xFF),
            (byte)((lightCentiLux >> 8) & 0xFF),
            (byte)((lightCentiLux >> 16) & 0xFF),
            (byte)((lightCentiLux >> 24) & 0xFF),
        };

        return new Frame(version, (byte)MessageType.Environment, sequence, payload);
    }
}
