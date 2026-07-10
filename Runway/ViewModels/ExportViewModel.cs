using CommunityToolkit.Mvvm.Input;
using Runway.Export;
using Runway.Storage;

namespace Runway.ViewModels;

// Вкладка "Экспорт" (по GUI-макету): формат CSV/XLSX и две кнопки —
// «все записи» и «эта сессия». Экспортируется телеметрия из БД
// (экспорт ЛОГОВ живёт во вкладке "Логи" и идёт по её галочкам).
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

    public RelayCommand ExportAllCommand { get; }
    public RelayCommand ExportSessionCommand { get; }

    // "Всего в БД: N записей" — как в макете. Обновляется при создании
    // и после каждого экспорта (живой счётчик здесь не нужен).
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
            long count = _appStore?.CountTelemetry() ?? 0;
            TotalRecordsText = $"Всего в БД: {count} записей";
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

    private void Export(long? sessionId)
    {
        try
        {
            var records = _appStore?.ReadTelemetry(sessionId) ?? Array.Empty<TelemetryRecord>();

            Directory.CreateDirectory(_exportDirectory);
            string scope = sessionId is long id ? $"session{id}" : "all";
            string extension = SelectedFormat == FormatXlsx ? "xlsx" : "csv";
            string path = Path.Combine(
                _exportDirectory,
                $"runway-telemetry-{scope}-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}"
            );

            if (SelectedFormat == FormatXlsx)
            {
                TelemetryXlsxWriter.Write(path, records);
            }
            else
            {
                TelemetryCsvWriter.Write(path, records);
            }

            StatusText = $"Экспортировано {records.Count} записей: {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка экспорта: {ex.Message}";
        }

        RefreshTotal();
    }
}
