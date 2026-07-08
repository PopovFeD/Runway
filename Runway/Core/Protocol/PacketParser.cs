using Runway.Framing;

namespace Runway.Protocol;

public static class PacketParser
{
    public static object Parse(Frame frame)
    {
        switch ((MessageType)frame.MessageType)
        {
            case MessageType.Telemetry:
                return ParseTelemetry(frame);

            case MessageType.Ping:
                return "PING";

            case MessageType.Pong:
                return "PONG";

            case MessageType.Ack:
                return "ACK";

            case MessageType.Error:
                return "ERROR";

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
}
