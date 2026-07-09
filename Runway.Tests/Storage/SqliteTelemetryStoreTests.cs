using Runway.Storage;
using Xunit;

namespace Runway.Tests.Storage;

// Roundtrip-тесты настоящего SQLite-файла (Microsoft.Data.Sqlite работает без
// внешнего сервера, так что это всё ещё быстрый юнит-тест, не Integration).
public class SqliteTelemetryStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"runway-test-{Guid.NewGuid():N}.db"
    );

    [Fact]
    public void Save_ThenReadAll_ReturnsSameRecords()
    {
        var first = new TelemetryRecord(
            new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Local),
            Sequence: 7,
            Temperature: 24.53,
            Humidity: 51.28
        );
        var second = new TelemetryRecord(
            new DateTime(2026, 7, 9, 12, 0, 1, DateTimeKind.Local),
            Sequence: 8,
            Temperature: -3.25,
            Humidity: 99.99
        );

        using (var store = new SqliteTelemetryStore(_dbPath))
        {
            store.Save(first);
            store.Save(second);
        }

        // Отдельное открытие того же файла — проверяем, что данные реально
        // на диске, а не в состоянии соединения.
        using var reopened = new SqliteTelemetryStore(_dbPath);
        var records = reopened.ReadAll();

        Assert.Equal(new[] { first, second }, records);
    }

    [Fact]
    public void Ctor_OnExistingDatabase_DoesNotDestroyData()
    {
        var record = new TelemetryRecord(DateTime.Now, 1, 20.0, 40.0);

        using (var store = new SqliteTelemetryStore(_dbPath))
        {
            store.Save(record);
        }

        // Повторное создание поверх существующего файла (CREATE TABLE IF NOT
        // EXISTS) — старые записи должны пережить перезапуск приложения.
        using (var store = new SqliteTelemetryStore(_dbPath))
        {
            Assert.Single(store.ReadAll());
        }
    }

    public void Dispose()
    {
        // Пул соединений Microsoft.Data.Sqlite может держать файл — сбрасываем,
        // иначе File.Delete на Windows упадёт с "file in use".
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
