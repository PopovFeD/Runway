using Runway.Framing;
using Runway.Protocol;

namespace Runway.Tests.Support;

// Общий билдер байтов кадра для тестов — синтетический, тестовый путь сборки,
// не имеет отношения к тому, как кадры собираются на реальном устройстве/эмуляторе.
// Используется и в чистых юнит-тестах (FrameAndCrcTests), и в интеграционных
// тестах реального COM-порта — формат байтов должен совпадать один в один.
public static class FrameTestHelper
{
    public static byte[] BuildFrameBytes(
        byte version,
        byte messageType,
        ushort sequence,
        byte[] payload
    )
    {
        List<byte> frameBytes = new();
        frameBytes.AddRange(FrameHeader.Magic);
        frameBytes.Add(version);
        frameBytes.Add(messageType);
        frameBytes.Add((byte)(sequence & 0xFF));
        frameBytes.Add((byte)((sequence >> 8) & 0xFF));
        frameBytes.Add((byte)(payload.Length & 0xFF));
        frameBytes.Add((byte)((payload.Length >> 8) & 0xFF));
        frameBytes.AddRange(payload);

        ushort crc = Crc16.Compute(frameBytes.ToArray());
        frameBytes.Add((byte)(crc & 0xFF));
        frameBytes.Add((byte)((crc >> 8) & 0xFF));

        return frameBytes.ToArray();
    }
}
