namespace Runway.Framing;

public static class FrameHeader
{
    // Сигнатура, с которой начинается каждый кадр — так мы отличаем "начало пакета" от мусора.
    public static readonly byte[] Magic = { 0xAA, 0x55 };

    public const int VersionSize = 1;
    public const int MessageTypeSize = 1;
    public const int SequenceSize = 2;
    public const int PayloadLengthSize = 2;
    public const int Crc16Size = 2;

    // Сколько байт занимает заголовок целиком (без payload и CRC)
    public const int HeaderSize =
        2 + VersionSize + MessageTypeSize + SequenceSize + PayloadLengthSize;
}
