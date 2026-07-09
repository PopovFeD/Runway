namespace Runway.Protocol;

// Данные окружения (второй сенсорный тип сообщения, MessageType.Environment):
// давление и освещённость. Добавлен как демонстрация расширяемости протокола
// под плитки Дашборда из GUI-макета: температура/влажность и давление/свет —
// разные типы кадров, GUI показывает плитки по мере поступления типов.
//
// На проводе payload 8 байт (little-endian):
//   uint32 давление в Па (101325 Па не влезает в ushort — поэтому 4 байта);
//   uint32 освещённость в сотых долях люкса (та же схема "х100", что у T/H).
public sealed record EnvironmentPacket : Packet
{
    public double PressureHpa { get; init; }

    public double LightLux { get; init; }
}
