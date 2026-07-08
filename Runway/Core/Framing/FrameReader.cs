namespace Runway.Framing;

public class FrameReader
{
    // Тут накапливаются байты, которые ещё не удалось собрать в кадр
    private readonly List<byte> _buffer = new();

    // Вызывается каждый раз, когда пришли новые байты из COM-порта.
    // Возвращает все кадры, которые получилось собрать за один вызов (может быть 0, 1 или несколько).
    public List<Frame> Append(byte[] newBytes)
    {
        _buffer.AddRange(newBytes);

        var readyFrames = new List<Frame>();

        // Пытаемся вытащить кадры из буфера, пока получается
        while (true)
        {
            var frame = TryExtractOneFrame();
            if (frame == null)
                break; // либо данных ещё не хватает, либо больше нечего разбирать

            readyFrames.Add(frame);
        }

        return readyFrames;
    }

    // Пытается вытащить ОДИН кадр из начала буфера.
    // Возвращает null, если кадр ещё не собрался целиком (ждём новых байтов).
    private Frame? TryExtractOneFrame()
    {
        int magicPosition = FindMagic();

        if (magicPosition == -1)
        {
            // Магии вообще нет в буфере — это мусор, весь можно выбросить
            _buffer.Clear();
            return null;
        }

        if (magicPosition > 0)
        {
            // Перед магией есть мусорные байты — выбрасываем их
            _buffer.RemoveRange(0, magicPosition);
        }

        // Ещё не пришёл весь заголовок целиком — ждём
        if (_buffer.Count < FrameHeader.HeaderSize)
            return null;

        byte version = _buffer[2];
        byte messageType = _buffer[3];
        ushort sequence = ReadUInt16(_buffer[4], _buffer[5]);
        ushort payloadLength = ReadUInt16(_buffer[6], _buffer[7]);

        int fullFrameSize = FrameHeader.HeaderSize + payloadLength + FrameHeader.Crc16Size;

        // Пакет ещё не пришёл целиком — ждём остальные байты
        if (_buffer.Count < fullFrameSize)
            return null;

        // Достаём payload
        byte[] payload = new byte[payloadLength];
        for (int i = 0; i < payloadLength; i++)
        {
            payload[i] = _buffer[FrameHeader.HeaderSize + i];
        }

        // Достаём CRC, которую нам прислали
        int crcPosition = FrameHeader.HeaderSize + payloadLength;
        ushort receivedCrc = ReadUInt16(_buffer[crcPosition], _buffer[crcPosition + 1]);

        // Считаем свою CRC по всему кадру, кроме самой CRC
        byte[] dataForCrc = new byte[crcPosition];
        for (int i = 0; i < crcPosition; i++)
        {
            dataForCrc[i] = _buffer[i];
        }
        ushort calculatedCrc = Protocol.Crc16.Compute(dataForCrc);

        if (calculatedCrc != receivedCrc)
        {
            // CRC не совпала — это не настоящий кадр, а случайное совпадение с Magic.
            // Убираем только первый байт магии и ищем заново.
            _buffer.RemoveAt(0);
            return null;
        }

        // Всё сошлось — убираем этот кадр из буфера и возвращаем его
        _buffer.RemoveRange(0, fullFrameSize);

        return new Frame(version, messageType, sequence, payload);
    }

    // Ищет позицию, с которой начинается Magic. Возвращает -1, если не нашёл.
    private int FindMagic()
    {
        for (int i = 0; i <= _buffer.Count - FrameHeader.Magic.Length; i++)
        {
            if (_buffer[i] == FrameHeader.Magic[0] && _buffer[i + 1] == FrameHeader.Magic[1])
            {
                return i;
            }
        }
        return -1;
    }

    // Собирает два байта в одно 16-битное число (little-endian: младший байт первый)
    private static ushort ReadUInt16(byte lowByte, byte highByte)
    {
        return (ushort)(lowByte | (highByte << 8));
    }
}
