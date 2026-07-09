namespace Runway.Protocol;

public enum MessageType : byte
{
    Ping = 0x01,
    Pong = 0x02,

    Telemetry = 0x10,
    Environment = 0x11,

    Command = 0x20,
    Ack = 0x21,

    Error = 0xFF,
}
