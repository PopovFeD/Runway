using Runway.Framing;

namespace Runway.Protocol;

public static class PacketParser
{
    // Возвращает типизированный Packet (см. Packet.cs) — вызывающий код матчит
    // подтипы switch-выражением. Исключения (неизвестный тип, битая длина)
    // по-прежнему обязан ловить вызывающий — см. ProcessFramesAsync.
    public static Packet Parse(Frame frame)
    {
        switch ((MessageType)frame.MessageType)
        {
            case MessageType.Telemetry:
                return ParseTelemetry(frame);

            case MessageType.Environment:
                return ParseEnvironment(frame);

            case MessageType.Ping:
            case MessageType.Pong:
            case MessageType.Ack:
            case MessageType.Error:
                return new ControlPacket((MessageType)frame.MessageType);

            default:
                throw new NotSupportedException($"Unknown message type: 0x{frame.MessageType:X2}");
        }
    }

    private static TelemetryPacket ParseTelemetry(Frame frame)
    {
        if (frame.Payload.Length != 4)
            throw new ArgumentException("Telemetry payload must contain exactly 4 bytes.");

        ushort temp = BitConverter.ToUInt16(frame.Payload, 0);
        ushort hum = BitConverter.ToUInt16(frame.Payload, 2);

        return new TelemetryPacket { Temperature = temp / 100.0, Humidity = hum / 100.0 };
    }

    private static EnvironmentPacket ParseEnvironment(Frame frame)
    {
        if (frame.Payload.Length != 8)
            throw new ArgumentException("Environment payload must contain exactly 8 bytes.");

        uint pressurePa = BitConverter.ToUInt32(frame.Payload, 0);
        uint lightCentiLux = BitConverter.ToUInt32(frame.Payload, 4);

        return new EnvironmentPacket
        {
            PressureHpa = pressurePa / 100.0,
            LightLux = lightCentiLux / 100.0,
        };
    }
}
