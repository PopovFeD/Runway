namespace Runway.Protocol;

public static class Crc16
{
    // CRC-16/MODBUS: полином 0xA001, начальное значение 0xFFFF, сдвиг вправо.
    // Должен побитово совпадать с crc16() в tools/mc_emulator.py — оттуда и взят.
    public static ushort Compute(byte[] data)
    {
        ushort crc = 0xFFFF;

        foreach (byte currentByte in data)
        {
            crc ^= currentByte;

            for (int bit = 0; bit < 8; bit++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                }
                else
                {
                    crc = (ushort)(crc >> 1);
                }
            }
        }

        return crc;
    }
}
