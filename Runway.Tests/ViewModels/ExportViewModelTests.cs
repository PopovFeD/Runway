using System.IO.Compression;
using Runway.Storage;
using Runway.ViewModels;
using Runway.Tests.Support;
using Xunit;

namespace Runway.Tests.ViewModels;

// Тесты вкладки "Экспорт": CSV/XLSX по всей телеметрии или по текущей сессии.
public class ExportViewModelTests : IDisposable
{
    private readonly string _exportDir = Path.Combine(
        Path.GetTempPath(),
        $"runway-export-{Guid.NewGuid():N}"
    );

    private static FakeAppStore StoreWithRecords()
    {
        var store = new FakeAppStore();
        store.Records.Add(new TelemetryRecord(DateTime.Now, 1, 24.53, 51.28, SessionId: 1));
        store.Records.Add(new TelemetryRecord(DateTime.Now, 2, 25.00, 50.00, SessionId: 2));
        return store;
    }

    [Fact]
    public void ExportAll_Csv_WritesAllRecords()
    {
        var vm = new ExportViewModel(StoreWithRecords(), new SessionTracker(), _exportDir);

        vm.ExportAllCommand.Execute(null);

        string path = Assert.Single(Directory.GetFiles(_exportDir, "*.csv"));
        string[] lines = File.ReadAllLines(path);
        Assert.Equal("timestamp;sequence;temperature;humidity;session_id", lines[0]);
        Assert.Equal(3, lines.Length); // заголовок + 2 записи
        Assert.Contains(";24.53;", lines[1]);
        Assert.StartsWith("Экспортировано 2 записей", vm.StatusText);
        Assert.Equal("Всего в БД: 2 записей", vm.TotalRecordsText);
    }

    [Fact]
    public void ExportSession_TakesOnlyCurrentSession_OrComplainsWithoutOne()
    {
        var sessions = new SessionTracker();
        var vm = new ExportViewModel(StoreWithRecords(), sessions, _exportDir);

        vm.ExportSessionCommand.Execute(null);
        Assert.Contains("Нет активной сессии", vm.StatusText);
        Assert.Empty(Directory.Exists(_exportDir) ? Directory.GetFiles(_exportDir) : Array.Empty<string>());

        sessions.Set(2);
        vm.ExportSessionCommand.Execute(null);

        string path = Assert.Single(Directory.GetFiles(_exportDir, "*.csv"));
        Assert.Equal(2, File.ReadAllLines(path).Length); // заголовок + 1 запись сессии 2
    }

    [Fact]
    public void ExportAll_Xlsx_ProducesValidZipWithSheetData()
    {
        var vm = new ExportViewModel(StoreWithRecords(), new SessionTracker(), _exportDir)
        {
            SelectedFormat = ExportViewModel.FormatXlsx,
        };

        vm.ExportAllCommand.Execute(null);

        string path = Assert.Single(Directory.GetFiles(_exportDir, "*.xlsx"));

        // .xlsx — это zip: проверяем структуру и содержимое листа
        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheet);

        using var reader = new StreamReader(sheet!.Open());
        string xml = reader.ReadToEnd();
        Assert.Contains("<v>24.53</v>", xml);
        Assert.Contains("<t>temperature</t>", xml);
    }

    public void Dispose()
    {
        if (Directory.Exists(_exportDir))
        {
            Directory.Delete(_exportDir, recursive: true);
        }
    }
}
