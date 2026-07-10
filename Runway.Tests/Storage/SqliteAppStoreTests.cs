using Runway.Storage;
using Xunit;

namespace Runway.Tests.Storage;

// Roundtrip-тесты настоящего SQLite-файла (Microsoft.Data.Sqlite работает без
// внешнего сервера, так что это всё ещё быстрый юнит-тест, не Integration).
public class SqliteAppStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"runway-test-{Guid.NewGuid():N}.db"
    );

    [Fact]
    public void SaveTelemetry_ThenReadAll_ReturnsSameRecords()
    {
        var first = new TelemetryRecord(
            new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Local),
            Sequence: 7,
            Temperature: 24.53,
            Humidity: 51.28,
            SessionId: 1
        );
        var second = new TelemetryRecord(
            new DateTime(2026, 7, 9, 12, 0, 1, DateTimeKind.Local),
            Sequence: 8,
            Temperature: -3.25,
            Humidity: 99.99
        );

        using (var store = new SqliteAppStore(_dbPath))
        {
            store.SaveTelemetry(first);
            store.SaveTelemetry(second);
        }

        // Отдельное открытие того же файла — данные реально на диске
        using var reopened = new SqliteAppStore(_dbPath);
        Assert.Equal(new[] { first, second }, reopened.ReadTelemetry(null));
    }

    [Fact]
    public void Sessions_And_Events_RoundtripWithFilters()
    {
        using var store = new SqliteAppStore(_dbPath);

        long s1 = store.BeginSession("Serial (COM)", "COM6");
        long s2 = store.BeginSession("WiFi (ESP32)", "192.168.1.42:3333");
        Assert.NotEqual(s1, s2);
        store.EndSession(s1);

        store.SaveEvent(new EventRecord(DateTime.Now, "Info", "Connection", "открыт", s1));
        store.SaveEvent(new EventRecord(DateTime.Now, "Warning", "Connection", "разрыв", s1));
        store.SaveEvent(new EventRecord(DateTime.Now, "Warning", "Parser", "мусор", s2));

        Assert.Equal(3, store.ReadEvents(null, null).Count);
        Assert.Equal(2, store.ReadEvents(new[] { "Warning" }, null).Count);
        Assert.Equal(3, store.ReadEvents(new[] { "Info", "Warning" }, null).Count);
        Assert.Equal(2, store.ReadEvents(null, s1).Count);
        var only = Assert.Single(store.ReadEvents(new[] { "Warning" }, s2));
        Assert.Equal("Parser", only.Category);

        // Пустой набор уровней = все галочки сняты = пусто
        Assert.Empty(store.ReadEvents(Array.Empty<string>(), null));
    }

    [Fact]
    public void Ctor_OnExistingDatabase_DoesNotDestroyData()
    {
        using (var store = new SqliteAppStore(_dbPath))
        {
            store.SaveTelemetry(new TelemetryRecord(DateTime.Now, 1, 20.0, 40.0));
        }

        // Повторное открытие поверх существующего файла (CREATE TABLE IF NOT
        // EXISTS + миграция session_id) — старые записи переживают перезапуск
        using (var store = new SqliteAppStore(_dbPath))
        {
            Assert.Single(store.ReadTelemetry(null));
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
