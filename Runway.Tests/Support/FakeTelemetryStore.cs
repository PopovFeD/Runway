using Runway.Storage;

namespace Runway.Tests.Support;

// Тестовая реализация ITelemetryStore: копит записи в памяти, чтобы проверить,
// ЧТО ViewModel отправляет в хранилище, не поднимая настоящий SQLite.
public class FakeTelemetryStore : ITelemetryStore
{
    public List<TelemetryRecord> Records { get; } = new();

    // Если выставить — Save бросает это исключение (проверка, что ошибка БД
    // не убивает конвейер обработки кадров).
    public Exception? ThrowOnSave { get; set; }

    public void Save(TelemetryRecord record)
    {
        if (ThrowOnSave != null)
            throw ThrowOnSave;

        Records.Add(record);
    }

    public void Dispose() { }
}
