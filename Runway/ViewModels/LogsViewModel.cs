using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Runway.Storage;

namespace Runway.ViewModels;

// Вкладка "Логи": галочки-фильтры по типам сообщений, чтение событий из БД
// и .log-экспорт по тем же фильтрам. Выделен из MainWindowViewModel при
// разделении по вкладкам.
public class LogsViewModel : ViewModelBase
{
    private readonly IAppStore? _appStore;
    private readonly SessionTracker _sessions;
    private readonly string _exportDirectory;

    public LogsViewModel(IAppStore? appStore, SessionTracker sessions, string? exportDirectory)
    {
        _appStore = appStore;
        _sessions = sessions;
        _exportDirectory =
            exportDirectory ?? Path.Combine(AppContext.BaseDirectory, "exports");

        RefreshLogsCommand = new RelayCommand(RefreshLogs);
        ExportLogsCommand = new RelayCommand(ExportLogs);
    }

    public RelayCommand RefreshLogsCommand { get; }
    public RelayCommand ExportLogsCommand { get; }

    // Галочки-фильтры: каждая отвечает за свой тип сообщений. Что выбрано —
    // то и показывается, и ровно то же уходит в экспорт.
    private bool _showInfo = true;
    public bool ShowInfo
    {
        get => _showInfo;
        set => SetProperty(ref _showInfo, value);
    }

    private bool _showWarning = true;
    public bool ShowWarning
    {
        get => _showWarning;
        set => SetProperty(ref _showWarning, value);
    }

    private bool _showError = true;
    public bool ShowError
    {
        get => _showError;
        set => SetProperty(ref _showError, value);
    }

    private bool _onlyCurrentSession;
    public bool OnlyCurrentSession
    {
        get => _onlyCurrentSession;
        set => SetProperty(ref _onlyCurrentSession, value);
    }

    // Результат последнего RefreshLogs — уже отформатированные строки.
    // Обновление по кнопке, не live: живой поток и так виден на Дашборде.
    public ObservableCollection<string> FilteredLogEvents { get; } = new();

    private string _exportStatusText = "";
    public string ExportStatusText
    {
        get => _exportStatusText;
        private set => SetProperty(ref _exportStatusText, value);
    }

    // Один и тот же набор уровней для показа и для экспорта — "что видишь,
    // то и экспортируешь". null = все три галочки стоят (без SQL-фильтра).
    private IReadOnlyCollection<string>? SelectedLevels()
    {
        if (ShowInfo && ShowWarning && ShowError)
            return null;

        var levels = new List<string>(3);
        if (ShowInfo)
            levels.Add("Info");
        if (ShowWarning)
            levels.Add("Warning");
        if (ShowError)
            levels.Add("Error");
        return levels;
    }

    private IReadOnlyList<EventRecord> ReadFilteredEvents() =>
        _appStore?.ReadEvents(SelectedLevels(), OnlyCurrentSession ? _sessions.CurrentId : null)
        ?? Array.Empty<EventRecord>();

    private void RefreshLogs()
    {
        var events = ReadFilteredEvents();

        FilteredLogEvents.Clear();
        foreach (var e in events)
        {
            FilteredLogEvents.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{e.Timestamp:HH:mm:ss}  [{e.Level}]  {e.Category}  {e.Message}"
                )
            );
        }
    }

    private void ExportLogs()
    {
        try
        {
            var events = ReadFilteredEvents();

            Directory.CreateDirectory(_exportDirectory);
            string path = Path.Combine(
                _exportDirectory,
                $"runway-logs-{DateTime.Now:yyyyMMdd-HHmmss}.log"
            );

            // Логи — это лог: плоские строки .log (как в терминалах и
            // diagnostics-файле), а не таблица. Табличный экспорт — удел
            // телеметрии во вкладке "Экспорт".
            var sb = new System.Text.StringBuilder();
            foreach (var e in events)
            {
                sb.AppendLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff}  [{e.Level}]  {e.Category}  {e.Message}  (session={e.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "-"})"
                    )
                );
            }

            File.WriteAllText(path, sb.ToString());
            ExportStatusText = $"Экспортировано {events.Count} записей: {path}";
        }
        catch (Exception ex)
        {
            ExportStatusText = $"Ошибка экспорта: {ex.Message}";
        }
    }
}
