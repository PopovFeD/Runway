using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Runway.Storage;

// SQLite-реализация IAppStore (Microsoft.Data.Sqlite, без EF — три таблицы
// и простые запросы не оправдывают ORM).
//
// Потокобезопасность: в отличие от прежнего SqliteTelemetryStore, писателей
// теперь два — консьюмер кадров (телеметрия, ошибки разбора) и UI-поток
// (сессии, события подключения, чтение для вкладки "Логи"). Одно соединение
// под общим замком — тот сценарий, который был предсказан в storage.md.
public sealed class SqliteAppStore : IAppStore
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();

    public SqliteAppStore(string dbFilePath)
    {
        string? directory = Path.GetDirectoryName(dbFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection($"Data Source={dbFilePath}");
        _connection.Open();

        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS sessions (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                started_at TEXT    NOT NULL,
                ended_at   TEXT    NULL,
                transport  TEXT    NOT NULL,
                endpoint   TEXT    NOT NULL
            );

            CREATE TABLE IF NOT EXISTS telemetry (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp   TEXT    NOT NULL,
                sequence    INTEGER NOT NULL,
                temperature REAL    NOT NULL,
                humidity    REAL    NOT NULL
            );

            CREATE TABLE IF NOT EXISTS events (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp  TEXT    NOT NULL,
                level      TEXT    NOT NULL,
                category   TEXT    NOT NULL,
                message    TEXT    NOT NULL,
                session_id INTEGER NULL
            );
            """;
        command.ExecuteNonQuery();

        MigrateTelemetrySessionId();
    }

    // База, созданная прежним SqliteTelemetryStore, не имела session_id
    // у телеметрии — доливаем колонку, не трогая накопленные данные.
    private void MigrateTelemetrySessionId()
    {
        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('telemetry') WHERE name = 'session_id';";
        long exists = (long)check.ExecuteScalar()!;

        if (exists == 0)
        {
            using var alter = _connection.CreateCommand();
            alter.CommandText = "ALTER TABLE telemetry ADD COLUMN session_id INTEGER NULL;";
            alter.ExecuteNonQuery();
        }
    }

    public long BeginSession(string transport, string endpoint)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sessions (started_at, transport, endpoint)
                VALUES ($startedAt, $transport, $endpoint);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$startedAt", FormatTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("$transport", transport);
            command.Parameters.AddWithValue("$endpoint", endpoint);

            return (long)command.ExecuteScalar()!;
        }
    }

    public void EndSession(long sessionId)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "UPDATE sessions SET ended_at = $endedAt WHERE id = $id;";
            command.Parameters.AddWithValue("$endedAt", FormatTimestamp(DateTime.Now));
            command.Parameters.AddWithValue("$id", sessionId);
            command.ExecuteNonQuery();
        }
    }

    public void SaveTelemetry(TelemetryRecord record)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO telemetry (timestamp, sequence, temperature, humidity, session_id)
                VALUES ($timestamp, $sequence, $temperature, $humidity, $sessionId);
                """;
            command.Parameters.AddWithValue("$timestamp", FormatTimestamp(record.Timestamp));
            command.Parameters.AddWithValue("$sequence", record.Sequence);
            command.Parameters.AddWithValue("$temperature", record.Temperature);
            command.Parameters.AddWithValue("$humidity", record.Humidity);
            command.Parameters.AddWithValue("$sessionId", (object?)record.SessionId ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    public void SaveEvent(EventRecord record)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO events (timestamp, level, category, message, session_id)
                VALUES ($timestamp, $level, $category, $message, $sessionId);
                """;
            command.Parameters.AddWithValue("$timestamp", FormatTimestamp(record.Timestamp));
            command.Parameters.AddWithValue("$level", record.Level);
            command.Parameters.AddWithValue("$category", record.Category);
            command.Parameters.AddWithValue("$message", record.Message);
            command.Parameters.AddWithValue("$sessionId", (object?)record.SessionId ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<EventRecord> ReadEvents(string? level, long? sessionId)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT timestamp, level, category, message, session_id
                FROM events
                WHERE ($level IS NULL OR level = $level)
                  AND ($sessionId IS NULL OR session_id = $sessionId)
                ORDER BY id;
                """;
            command.Parameters.AddWithValue("$level", (object?)level ?? DBNull.Value);
            command.Parameters.AddWithValue("$sessionId", (object?)sessionId ?? DBNull.Value);

            var records = new List<EventRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(
                    new EventRecord(
                        ParseTimestamp(reader.GetString(0)),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetInt64(4)
                    )
                );
            }

            return records;
        }
    }

    // Пока используется только тестами (roundtrip); войдёт в IAppStore,
    // когда появится экспорт/просмотр истории телеметрии.
    public IReadOnlyList<TelemetryRecord> ReadAllTelemetry()
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT timestamp, sequence, temperature, humidity, session_id
                FROM telemetry
                ORDER BY id;
                """;

            var records = new List<TelemetryRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(
                    new TelemetryRecord(
                        ParseTimestamp(reader.GetString(0)),
                        (ushort)reader.GetInt32(1),
                        reader.GetDouble(2),
                        reader.GetDouble(3),
                        reader.IsDBNull(4) ? null : reader.GetInt64(4)
                    )
                );
            }

            return records;
        }
    }

    // "O" (round-trip) + InvariantCulture: восстанавливается без потерь
    // и не зависит от локали машины — тот же принцип, что в логах.
    private static string FormatTimestamp(DateTime value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseTimestamp(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public void Dispose()
    {
        lock (_lock)
        {
            _connection.Dispose();
        }
    }
}
