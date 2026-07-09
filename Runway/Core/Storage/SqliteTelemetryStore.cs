using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Runway.Storage;

// Хранилище телеметрии в локальном SQLite-файле (Microsoft.Data.Sqlite, без EF —
// одна таблица и два SQL-запроса не оправдывают ORM).
//
// Потокобезопасность: Save вызывается только из одного потока — консьюмера
// очереди кадров (MainWindowViewModel.ProcessFramesAsync), поэтому одного
// соединения без блокировок достаточно. Если когда-нибудь появится второй
// писатель — сюда придётся добавить lock, как в AppendOnlyFile.
public sealed class SqliteTelemetryStore : ITelemetryStore
{
    private readonly SqliteConnection _connection;

    public SqliteTelemetryStore(string dbFilePath)
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
            CREATE TABLE IF NOT EXISTS telemetry (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp   TEXT    NOT NULL,
                sequence    INTEGER NOT NULL,
                temperature REAL    NOT NULL,
                humidity    REAL    NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Save(TelemetryRecord record)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO telemetry (timestamp, sequence, temperature, humidity)
            VALUES ($timestamp, $sequence, $temperature, $humidity);
            """;

        // "O" (round-trip) + InvariantCulture: строка восстанавливается в DateTime
        // без потерь и не зависит от локали машины — тот же принцип, что в логах.
        command.Parameters.AddWithValue(
            "$timestamp",
            record.Timestamp.ToString("O", CultureInfo.InvariantCulture)
        );
        command.Parameters.AddWithValue("$sequence", record.Sequence);
        command.Parameters.AddWithValue("$temperature", record.Temperature);
        command.Parameters.AddWithValue("$humidity", record.Humidity);

        command.ExecuteNonQuery();
    }

    // Пока используется только тестами (проверка roundtrip), но понадобится
    // и приложению, когда появится просмотр истории. В интерфейс ITelemetryStore
    // намеренно не входит — ViewModel сейчас только пишет.
    public IReadOnlyList<TelemetryRecord> ReadAll()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT timestamp, sequence, temperature, humidity
            FROM telemetry
            ORDER BY id;
            """;

        var records = new List<TelemetryRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(
                new TelemetryRecord(
                    DateTime.Parse(
                        reader.GetString(0),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind
                    ),
                    (ushort)reader.GetInt32(1),
                    reader.GetDouble(2),
                    reader.GetDouble(3)
                )
            );
        }

        return records;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
