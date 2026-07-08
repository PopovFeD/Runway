namespace Runway.Framing;

// Один уже собранный и проверенный кадр
public class Frame
{
    public byte Version { get; }
    public byte MessageType { get; }
    public ushort Sequence { get; }
    public byte[] Payload { get; }

    public Frame(byte version, byte messageType, ushort sequence, byte[] payload)
    {
        Version = version;
        MessageType = messageType;
        Sequence = sequence;
        Payload = payload;
    }
}
