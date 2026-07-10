using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Runway.Framing;
using Runway.Logging;
using Runway.Protocol;
using Runway.Storage;
using Runway.Threading;
using Runway.Transport;

namespace Runway.ViewModels;

// Корневая ViewModel окна. После разделения по вкладкам здесь остались только
// КОНВЕЙЕР ДАННЫХ (кадры → пакеты → БД/живой вывод/плитки) и композиция
// дочерних ViewModel: Connection (подключение/сессии) и Logs (фильтры/экспорт).
public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly FrameReader _frameReader;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly BoundedLog _boundedLog;
    private readonly IAppStore? _appStore;
    private readonly SessionTracker _sessions;

    // Очередь между read-потоком порта (продюсер) и разбором пакетов (консьюмер):
    // OnDataReceived вызывается прямо из фонового потока транспорта, и всё
    // синхронное там тормозит следующий Port.Read(). Поэтому здесь только
    // нарезка на кадры, а разбор и запись в SQLite — в ProcessFramesAsync.
    private readonly Channel<Frame> _frameChannel = Channel.CreateUnbounded<Frame>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
    );

    private readonly CancellationTokenSource _processingCts = new();
    private readonly Task _processingTask;

    public MainWindowViewModel(
        FrameReader frameReader,
        IReadOnlyList<ITransport> transports,
        IUiDispatcher uiDispatcher,
        IAppStore? appStore = null,
        SessionTracker? sessionTracker = null,
        int maxLogEntries = 500,
        string? initialEndpoint = null,
        string? exportDirectory = null
    )
    {
        _frameReader = frameReader;
        _uiDispatcher = uiDispatcher;
        _appStore = appStore;
        _sessions = sessionTracker ?? new SessionTracker();
        _boundedLog = new BoundedLog(LogEntries, maxLogEntries);
<<<<<<< HEAD
        _exportDirectory = exportDirectory ?? Path.Combine(AppContext.BaseDirectory, "exports");
=======
>>>>>>> hmm

        Connection = new ConnectionViewModel(
            transports,
            uiDispatcher,
            appStore,
            _sessions,
            initialEndpoint
        );
        Logs = new LogsViewModel(appStore, _sessions, exportDirectory);

        // Поток ДАННЫХ подписан здесь (конвейер живёт в этой VM);
        // состоянием подключения занимается Connection.
        foreach (var transport in transports)
        {
            transport.DataReceived += OnDataReceived;
        }

        _processingTask = Task.Run(() => ProcessFramesAsync(_processingCts.Token));
    }

    public ConnectionViewModel Connection { get; }
    public LogsViewModel Logs { get; }

    // Живой вывод для Дашборда. Ограничен через _boundedLog — история в БД.
    public ObservableCollection<string> LogEntries { get; } = new();

<<<<<<< HEAD
    public RelayCommand RefreshEndpointsCommand { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand RefreshLogsCommand { get; }
    public RelayCommand ExportLogsCommand { get; }
    public RelayCommand ToggleConnectionCommand { get; }

    // Единая кнопка вкл/выкл в верхней панели (пункт из TODO). Решение
    // принимается по ФАКТУ подключения (_activeTransport), а не по выбору
    // в ComboBox — как и StatusText.
    public string ToggleConnectionText => _activeTransport == null ? "Подключить" : "Отключить";

    private bool CanToggleConnection() => _activeTransport != null || (SelectedEndpoint != null);

    private void ToggleConnection()
    {
        if (_activeTransport == null)
        {
            Connect();
        }
        else
        {
            Disconnect();
        }
    }

=======
>>>>>>> hmm
    // --- Последние значения для плиток Дашборда ---

    private string _lastTemperatureText = "—";
    public string LastTemperatureText
    {
        get => _lastTemperatureText;
        private set => SetProperty(ref _lastTemperatureText, value);
    }

    private string _lastHumidityText = "—";
    public string LastHumidityText
    {
        get => _lastHumidityText;
        private set => SetProperty(ref _lastHumidityText, value);
    }

    private string _lastPressureText = "—";
    public string LastPressureText
    {
        get => _lastPressureText;
        private set => SetProperty(ref _lastPressureText, value);
    }

    private string _lastLightText = "—";
    public string LastLightText
    {
        get => _lastLightText;
        private set => SetProperty(ref _lastLightText, value);
    }

<<<<<<< HEAD
    // --- Вкладка "Логи": фильтруемое чтение событий из БД ---

    // Галочки-фильтры: каждая отвечает за свой тип сообщений. Что выбрано —
    // то и показывается, и ровно то же уходит в экспорт (см. ExportLogs).
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
    // Обновление по кнопке, не live: чтение из SQLite на каждый чих не нужно,
    // а живой поток и так виден на Дашборде.
    public ObservableCollection<string> FilteredLogEvents { get; } = new();

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

    // --- Экспорт логов: те же галочки-фильтры, что и у показа ---

    private string _exportStatusText = "";
    public string ExportStatusText
    {
        get => _exportStatusText;
        private set => SetProperty(ref _exportStatusText, value);
    }

    private void ExportLogs()
    {
        try
        {
            var events = ReadFilteredEvents();

            Directory.CreateDirectory(_exportDirectory);
            string path = Path.Combine(
                _exportDirectory,
                $"runway-logs-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            );

            // CSV с ';' — его сразу понимает русскоязычный Excel
            // (разделитель списка в этой локали — точка с запятой)
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("timestamp;level;category;message;session_id");
            foreach (var e in events)
            {
                sb.Append(e.Timestamp.ToString("O", CultureInfo.InvariantCulture));
                sb.Append(';').Append(e.Level);
                sb.Append(';').Append(CsvField(e.Category));
                sb.Append(';').Append(CsvField(e.Message));
                sb.Append(';').Append(e.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "");
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
            ExportStatusText = $"Экспортировано {events.Count} записей: {path}";
        }
        catch (Exception ex)
        {
            ExportStatusText = $"Ошибка экспорта: {ex.Message}";
        }
    }

    // Минимальное CSV-экранирование: кавычим поле, если внутри разделитель,
    // кавычки или перенос строки; кавычки удваиваются по правилам CSV.
    private static string CsvField(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    // Событие приложения — в БД. Ошибка записи не должна ронять UI-поток
    // или консьюмер: хранилище может быть недоступно, событие тогда теряется
    // (его дубль всё равно есть в diagnostics-логе у SerialTransport).
    private void SaveEventQuietly(string level, string category, string message)
    {
        if (_appStore == null)
            return;

        try
        {
            _appStore.SaveEvent(
                new EventRecord(DateTime.Now, level, category, message, _sessions.CurrentId)
            );
        }
        catch
        {
            // Некуда репортить — БД и есть место для репортов
        }
    }

    public ITransport SelectedTransport
    {
        get => _selectedTransport;
        set
        {
            if (SetProperty(ref _selectedTransport, value))
            {
                // У другого транспорта — другие точки подключения
                RefreshEndpoints();
            }
        }
    }

    public string? SelectedEndpoint
    {
        get => _selectedEndpoint;
        set
        {
            if (SetProperty(ref _selectedEndpoint, value))
            {
                ConnectCommand.NotifyCanExecuteChanged();
                ToggleConnectionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // GUI смотрит сюда, чтобы показать, разорвана ли связь и идёт ли переподключение
    // (см. ITransport.ConnectionStateChanged) — без парсинга текста лога.
    public ConnectionState ConnectionStatus
    {
        get => _connectionStatus;
        private set
        {
            if (SetProperty(ref _connectionStatus, value))
            {
                // Доступность кнопок зависит от статуса — пересчитываем при каждой смене
                ConnectCommand.NotifyCanExecuteChanged();
                DisconnectCommand.NotifyCanExecuteChanged();
                ToggleConnectionCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ToggleConnectionText));
            }
        }
    }

    // Индикатор для GUI: не только состояние, но и К ЧЕМУ оно относится.
    // Выбор в ComboBox — это намерение, а не факт: пользователь может листать
    // список транспортов/портов, не трогая живое подключение, и индикатор
    // продолжает показывать реальное соединение, а не текущий выбор.
    public string StatusText =>
        _activeTransport == null
            ? "Отключено"
            : ConnectionStatus switch
            {
                ConnectionState.Connected =>
                    $"Подключено: {_activeTransport.DisplayName} · {_activeEndpoint}",
                ConnectionState.Reconnecting =>
                    $"Переподключение: {_activeTransport.DisplayName} · {_activeEndpoint}",
                // Disconnected при живом _activeTransport — короткие переходные
                // моменты: сразу после нажатия "Подключить" (транспорт ещё не
                // отчитался) или между разрывом и началом переподключения.
                _ => $"Подключение: {_activeTransport.DisplayName} · {_activeEndpoint}",
            };

    private bool CanConnect() =>
        SelectedEndpoint != null && ConnectionStatus == ConnectionState.Disconnected;

    // Reconnecting тоже считается "подключён" в смысле кнопки: Disconnect в этом
    // состоянии — это способ отменить бесконечные попытки переподключения.
    private bool CanDisconnect() => ConnectionStatus != ConnectionState.Disconnected;

    private void Connect()
    {
        if (SelectedEndpoint == null)
            return;

        // Защита от повторного вызова: прежний активный транспорт закрываем
        // до открытия нового, чтобы не осталось двух живых read-потоков.
        _activeTransport?.Close();

        _activeTransport = SelectedTransport;
        _activeEndpoint = SelectedEndpoint;

        // Сессия открывается ДО Open: первые события подключения должны уже
        // ложиться с session_id. Переподключения внутри разрыва сессию не дробят.
        if (_appStore != null)
        {
            try
            {
                _sessions.Set(
                    _appStore.BeginSession(_activeTransport.DisplayName, SelectedEndpoint)
                );
            }
            catch
            {
                _sessions.Clear();
            }
        }
        SaveEventQuietly(
            "Info",
            "Connection",
            $"Подключение: {_activeTransport.DisplayName} · {SelectedEndpoint}"
        );

        _activeTransport.Open(SelectedEndpoint);
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToggleConnectionText));
        ToggleConnectionCommand.NotifyCanExecuteChanged();
    }

    private void Disconnect()
    {
        _activeTransport?.Close();
        _activeTransport = null;
        _activeEndpoint = null;

        SaveEventQuietly("Info", "Connection", "Отключение по команде пользователя");

        if (_sessions.CurrentId is long session && _appStore != null)
        {
            try
            {
                _appStore.EndSession(session);
            }
            catch
            {
                // См. SaveEventQuietly — репортить некуда
            }
            _sessions.Clear();
        }

        // Close() по команде пользователя — штатная остановка, а не разрыв,
        // транспорт сам ConnectionStateChanged не поднимает. Статус выставляем сами.
        ConnectionStatus = ConnectionState.Disconnected;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToggleConnectionText));
        ToggleConnectionCommand.NotifyCanExecuteChanged();
    }

    private void RefreshEndpoints()
    {
        var endpoints = SelectedTransport.GetAvailableEndpoints();

        AvailableEndpoints.Clear();
        foreach (var endpoint in endpoints)
        {
            AvailableEndpoints.Add(endpoint);
        }

        // Прежний выбор сохраняем, только если он всё ещё существует,
        // иначе берём первую доступную точку (или null, если список пуст —
        // тогда CanConnect не даст нажать "Подключить").
        if (SelectedEndpoint == null || !AvailableEndpoints.Contains(SelectedEndpoint))
        {
            SelectedEndpoint = AvailableEndpoints.FirstOrDefault();
        }
    }

    // Вызывается напрямую из read-потока транспорта (см. SerialTransport.RunLoop).
    private void OnConnectionStateChanged(ConnectionState state)
    {
        _uiDispatcher.Post(() =>
        {
            // Если пользователь уже нажал "Отключить", запоздавшее событие от
            // только что закрытого транспорта не должно перетирать статус.
            if (_activeTransport != null)
            {
                ConnectionStatus = state;

                SaveEventQuietly(
                    state == ConnectionState.Connected ? "Info" : "Warning",
                    "Connection",
                    $"Состояние: {state} ({_activeTransport.DisplayName} · {_activeEndpoint})"
                );
            }
        });
    }

=======
>>>>>>> hmm
    // Вызывается напрямую из read-потока транспорта (см. SerialTransport.ReadLoop).
    // Должен оставаться максимально дешёвым: только выделение кадров из потока
    // байт (операции в памяти) и запись в канал. Никакого I/O здесь.
    private void OnDataReceived(byte[] bytes)
    {
        var frames = _frameReader.Append(bytes);

        foreach (var frame in frames)
        {
            // Канал безлимитный: TryWrite не блокирует и не возвращает false
            _frameChannel.Writer.TryWrite(frame);
        }
    }

    // Консьюмер очереди — отдельная задача, не связанная с потоком порта.
    private async Task ProcessFramesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _frameChannel.Reader.ReadAllAsync(cancellationToken))
            {
                string line;
                Packet? packet = null;
                try
                {
                    packet = PacketParser.Parse(frame);

                    // Телеметрия — в БД; ошибка записи не маскируется под
                    // ParseError и не останавливает конвейер
                    if (packet is TelemetryPacket telemetry && _appStore != null)
                    {
                        try
                        {
                            _appStore.SaveTelemetry(
                                new TelemetryRecord(
                                    DateTime.Now,
                                    frame.Sequence,
                                    telemetry.Temperature,
                                    telemetry.Humidity,
                                    _sessions.CurrentId
                                )
                            );
                        }
                        catch (Exception ex)
                        {
                            _appStore.TrySaveEvent(
                                _sessions,
                                "Error",
                                "Storage",
                                $"Seq={frame.Sequence}: {ex.Message}"
                            );
                        }
                    }

                    line = packet switch
                    {
                        // InvariantCulture — иначе на русской локали "24,53"
                        TelemetryPacket t => string.Create(
                            CultureInfo.InvariantCulture,
                            $"Seq={frame.Sequence}  T={t.Temperature:F2}°C  H={t.Humidity:F2}%"
                        ),
                        EnvironmentPacket e => string.Create(
                            CultureInfo.InvariantCulture,
                            $"Seq={frame.Sequence}  P={e.PressureHpa:F2} hPa  L={e.LightLux:F2} lx"
                        ),
                        // ToUpperInvariant — служебные записи исторически "PING"
                        ControlPacket c =>
                            $"Seq={frame.Sequence}  {c.Type.ToString().ToUpperInvariant()}",
                        _ => $"Seq={frame.Sequence}  Type={frame.MessageType}",
                    };
                }
                catch (Exception ex)
                {
                    line =
                        $"Seq={frame.Sequence}  Type=0x{frame.MessageType:X2}  ParseError: {ex.Message}";
                    _appStore.TrySaveEvent(
                        _sessions,
                        "Warning",
                        "Parser",
                        $"Seq={frame.Sequence} Type=0x{frame.MessageType:X2}: {ex.Message}"
                    );
                }

                // В GUI — строка в лог-стиле (метка времени, как в терминале)
                string guiLine = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{DateTime.Now:HH:mm:ss.fff}  {line}"
                );
                Packet? uiPacket = packet;
                _uiDispatcher.Post(() =>
                {
                    _boundedLog.Add(guiLine);

                    // Плитки Дашборда: последние значения по типу пакета
                    switch (uiPacket)
                    {
                        case TelemetryPacket t:
                            LastTemperatureText = string.Create(
                                CultureInfo.InvariantCulture,
                                $"{t.Temperature:F2} °C"
                            );
                            LastHumidityText = string.Create(
                                CultureInfo.InvariantCulture,
                                $"{t.Humidity:F2} %"
                            );
                            break;
                        case EnvironmentPacket e:
                            LastPressureText = string.Create(
                                CultureInfo.InvariantCulture,
                                $"{e.PressureHpa:F2} hPa"
                            );
                            LastLightText = string.Create(
                                CultureInfo.InvariantCulture,
                                $"{e.LightLux:F2} lx"
                            );
                            break;
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка при Dispose — не ошибка
        }
    }

    // Останавливает консьюмера и отписывается от транспортов. Транспортами
    // и хранилищем владеет App.axaml.cs.
    public void Dispose()
    {
        foreach (var transport in Connection.Transports)
        {
            transport.DataReceived -= OnDataReceived;
        }
        Connection.Dispose();

        _frameChannel.Writer.TryComplete();
        _processingCts.Cancel();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Задача остановилась через отмену канала/токена — это ожидаемо
        }

        _processingCts.Dispose();
    }
}
