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
        store.EnvRecords.Add(new EnvironmentRecord(DateTime.Now, 3, 1013.25, 347.5, SessionId: 2));
        return store;
    }

    [Fact]
    public void ExportAll_Csv_WritesAllRecords()
    {
        var vm = new ExportViewModel(StoreWithRecords(), new SessionTracker(), _exportDir);

        vm.ExportAllCommand.Execute(null);

        // CSV одно-табличный: по файлу на каждый выбранный протокол
        string[] files = Directory.GetFiles(_exportDir, "*.csv");
        Assert.Equal(2, files.Length);

        string telemetryPath = Assert.Single(files, f => f.Contains("telemetry"));
        string[] lines = File.ReadAllLines(telemetryPath);
        Assert.Equal("timestamp;sequence;temperature;humidity;session_id", lines[0]);
        Assert.Equal(3, lines.Length); // заголовок + 2 записи
        Assert.Contains(";24.53;", lines[1]);

        string envPath = Assert.Single(files, f => f.Contains("environment"));
        Assert.Contains(";1013.25;", File.ReadAllLines(envPath)[1]);

        Assert.StartsWith("Экспортировано 3 записей", vm.StatusText);
        Assert.Equal("Всего в БД: Telemetry — 2, Environment — 1", vm.TotalRecordsText);
    }

    [Fact]
    public void Export_WithNoProtocolsSelected_Complains()
    {
        var vm = new ExportViewModel(StoreWithRecords(), new SessionTracker(), _exportDir)
        {
            IncludeTelemetry = false,
            IncludeEnvironment = false,
        };

        vm.ExportAllCommand.Execute(null);

        Assert.Equal("Не выбран ни один протокол.", vm.StatusText);
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
        vm.IncludeEnvironment = false; // только телеметрия, чтобы был один файл
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

        // Второй протокол — второй лист той же книги
        var sheet2 = zip.GetEntry("xl/worksheets/sheet2.xml");
        Assert.NotNull(sheet2);
        using var reader2 = new StreamReader(sheet2!.Open());
        Assert.Contains("<v>1013.25</v>", reader2.ReadToEnd());
    }

    public void Dispose()
    {
        if (Directory.Exists(_exportDir))
        {
            Directory.Delete(_exportDir, recursive: true);
        }
    }
}
