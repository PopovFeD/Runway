using Runway.Storage;

namespace Runway.Tests.Support;

// Тестовая реализация IAppStore: копит записи в памяти, чтобы проверять,
// ЧТО ViewModel отправляет в хранилище, не поднимая настоящий SQLite.
public class FakeAppStore : IAppStore
{
    private long _nextSessionId = 1;

    public List<TelemetryRecord> Records { get; } = new();
    public List<EventRecord> Events { get; } = new();
    public List<(long Id, string Transport, string Endpoint)> StartedSessions { get; } = new();
    public List<long> EndedSessions { get; } = new();

    // Если выставить — SaveTelemetry бросает это исключение (проверка, что
    // ошибка БД не убивает конвейер обработки кадров).
    public Exception? ThrowOnSave { get; set; }

    public long BeginSession(string transport, string endpoint)
    {
        long id = _nextSessionId++;
        StartedSessions.Add((id, transport, endpoint));
        return id;
    }

    public void EndSession(long sessionId) => EndedSessions.Add(sessionId);

    public void SaveTelemetry(TelemetryRecord record)
    {
        if (ThrowOnSave != null)
            throw ThrowOnSave;

        Records.Add(record);
    }

    public void SaveEvent(EventRecord record) => Events.Add(record);

    public IReadOnlyList<EventRecord> ReadEvents(
        IReadOnlyCollection<string>? levels,
        long? sessionId
    ) =>
        Events
            .Where(e => levels == null || levels.Contains(e.Level))
            .Where(e => sessionId == null || e.SessionId == sessionId)
            .ToList();

    public void Dispose() { }
}
