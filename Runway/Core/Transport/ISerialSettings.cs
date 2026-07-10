namespace Runway.Transport;

// Настраиваемые из GUI параметры последовательного канала. Отдельный
// интерфейс, а не часть ITransport: скорость/задержка — специфика Serial,
// WiFi-транспорту они не нужны. ViewModel проверяет транспорт паттерном
// "is ISerialSettings" и показывает поля только по делу.
public interface ISerialSettings
{
    // Применяются при СЛЕДУЮЩЕМ открытии/переоткрытии порта
    int BaudRate { get; set; }
    int ReconnectDelaySeconds { get; set; }
}
