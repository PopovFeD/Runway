namespace Runway.Storage;

// Одна принятая точка телеметрии. Timestamp — момент приёма на стороне
// приложения; SessionId — сессия подключения, в рамках которой точка пришла
// (null — вне сессии, например в тестах без подключения).
public record TelemetryRecord(
    DateTime Timestamp,
    ushort Sequence,
    double Temperature,
    double Humidity,
    long? SessionId = null
);

// Событие уровня приложения (подключение, разрыв, ошибка разбора...) —
// то, что раньше жило только строками в diagnostics-логе, а теперь
// структурировано и фильтруемо (уровень/категория/сессия).
public record EventRecord(
    DateTime Timestamp,
    string Level, // "Info" | "Warning" | "Error"
    string Category,
    string Message,
    long? SessionId = null
);

// Хранилище данных приложения (SQLite) по Misc/docs/storage-and-logs-decision.md:
// сессии (период подключения), телеметрия и события — единый источник правды.
// Заменяет прежний ITelemetryStore. Жизненным циклом владеет App.axaml.cs.
public interface IAppStore : IDisposable
{
    // Сессия = период от "Подключить" до "Отключить" (переподключения внутри
    // разрыва сессию НЕ дробят — они события внутри неё). Возвращает id сессии.
    long BeginSession(string transport, string endpoint);
    void EndSession(long sessionId);

    void SaveTelemetry(TelemetryRecord record);
    void SaveEvent(EventRecord record);

    // Фильтры: levels == null — все уровни (пустой набор — ничего);
    // sessionId == null — все сессии. Ровно эта же выборка используется
    // и вкладкой "Логи", и экспортом — что видишь, то и экспортируешь.
    IReadOnlyList<EventRecord> ReadEvents(IReadOnlyCollection<string>? levels, long? sessionId);

    // Телеметрия для экспорта/истории: sessionId == null — вся, иначе — одной сессии
    IReadOnlyList<TelemetryRecord> ReadTelemetry(long? sessionId);

    // Сколько всего точек телеметрии в БД (счётчик "всего N записей" в GUI)
    long CountTelemetry();
}
