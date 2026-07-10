using CommunityToolkit.Mvvm.Input;
using Runway.Export;
using Runway.Storage;

namespace Runway.ViewModels;

// Вкладка "Экспорт" (по GUI-макету): формат CSV/XLSX, галочки протоколов и
// две кнопки — «все записи» и «эта сессия». Каждый выбранный протокол — своя
// таблица: в XLSX — отдельный лист одной книги, в CSV — отдельный файл
// (CSV по своей природе одно-табличный). Экспорт ЛОГОВ живёт во вкладке "Логи".
public class ExportViewModel : ViewModelBase
{
    public const string FormatCsv = "CSV";
    public const string FormatXlsx = "Excel (XLSX)";

    private readonly IAppStore? _appStore;
    private readonly SessionTracker _sessions;
    private readonly string _exportDirectory;

    public ExportViewModel(IAppStore? appStore, SessionTracker sessions, string? exportDirectory)
    {
        _appStore = appStore;
        _sessions = sessions;
        _exportDirectory =
            exportDirectory ?? Path.Combine(AppContext.BaseDirectory, "exports");

        ExportAllCommand = new RelayCommand(() => Export(null));
        ExportSessionCommand = new RelayCommand(ExportCurrentSession);

        RefreshTotal();
    }

    public IReadOnlyList<string> Formats { get; } = new[] { FormatCsv, FormatXlsx };

    private string _selectedFormat = FormatCsv;
    public string SelectedFormat
    {
        get => _selectedFormat;
        set => SetProperty(ref _selectedFormat, value);
    }

    // Галочки протоколов: что выбрано — то и экспортируется. Новый тип
    // пакета в протоколе = новая галочка + ветка в BuildTables.
    private bool _includeTelemetry = true;
    public bool IncludeTelemetry
    {
        get => _includeTelemetry;
        set => SetProperty(ref _includeTelemetry, value);
    }

    private bool _includeEnvironment = true;
    public bool IncludeEnvironment
    {
        get => _includeEnvironment;
        set => SetProperty(ref _includeEnvironment, value);
    }

    public RelayCommand ExportAllCommand { get; }
    public RelayCommand ExportSessionCommand { get; }

    private string _totalRecordsText = "";
    public string TotalRecordsText
    {
        get => _totalRecordsText;
        private set => SetProperty(ref _totalRecordsText, value);
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private void RefreshTotal()
    {
        try
        {
            long telemetry = _appStore?.CountTelemetry() ?? 0;
            long environment = _appStore?.CountEnvironment() ?? 0;
            TotalRecordsText =
                $"Всего в БД: Telemetry — {telemetry}, Environment — {environment}";
        }
        catch (Exception ex)
        {
            TotalRecordsText = $"БД недоступна: {ex.Message}";
        }
    }

    private void ExportCurrentSession()
    {
        if (_sessions.CurrentId is not long session)
        {
            StatusText = "Нет активной сессии — сначала подключитесь.";
            return;
        }
        Export(session);
    }

    private List<ExportTable> BuildTables(long? sessionId)
    {
        var tables = new List<ExportTable>();
        if (_appStore == null)
            return tables;

        if (IncludeTelemetry)
        {
            var rows = _appStore
                .ReadTelemetry(sessionId)
                .Select(r =>
                    (IReadOnlyList<object?>)
                        new object?[]
                        {
                            r.Timestamp,
                            r.Sequence,
                            r.Temperature,
                            r.Humidity,
                            r.SessionId,
                        }
                )
                .ToList();
            tables.Add(
                new ExportTable(
                    "Telemetry",
                    new[] { "timestamp", "sequence", "temperature", "humidity", "session_id" },
                    rows
                )
            );
        }

        if (IncludeEnvironment)
        {
            var rows = _appStore
                .ReadEnvironment(sessionId)
                .Select(r =>
                    (IReadOnlyList<object?>)
                        new object?[]
                        {
                            r.Timestamp,
                            r.Sequence,
                            r.PressureHpa,
                            r.LightLux,
                            r.SessionId,
                        }
                )
                .ToList();
            tables.Add(
                new ExportTable(
                    "Environment",
                    new[] { "timestamp", "sequence", "pressure_hpa", "light_lux", "session_id" },
                    rows
                )
            );
        }

        return tables;
    }

    private void Export(long? sessionId)
    {
        try
        {
            var tables = BuildTables(sessionId);
            if (tables.Count == 0)
            {
                StatusText = "Не выбран ни один протокол.";
                return;
            }

            Directory.CreateDirectory(_exportDirectory);
            string scope = sessionId is long id ? $"session{id}" : "all";
            string stamp = $"{DateTime.Now:yyyyMMdd-HHmmss}";
            int totalRows = tables.Sum(t => t.Rows.Count);

            var paths = new List<string>();
            if (SelectedFormat == FormatXlsx)
            {
                // Один файл, лист на протокол
                string path = Path.Combine(
                    _exportDirectory,
                    $"runway-{scope}-{stamp}.xlsx"
                );
                XlsxTableWriter.Write(path, tables);
                paths.Add(path);
            }
            else
            {
                // CSV одно-табличный — файл на протокол
                foreach (var table in tables)
                {
                    string path = Path.Combine(
                        _exportDirectory,
                        $"runway-{table.Name.ToLowerInvariant()}-{scope}-{stamp}.csv"
                    );
                    CsvTableWriter.Write(path, table);
                    paths.Add(path);
                }
            }

            StatusText = $"Экспортировано {totalRows} записей: {string.Join("; ", paths)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка экспорта: {ex.Message}";
        }

        RefreshTotal();
    }
}
