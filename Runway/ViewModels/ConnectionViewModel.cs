using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Runway.Storage;
using Runway.Threading;
using Runway.Transport;

namespace Runway.ViewModels;

// Всё, что касается ПОДКЛЮЧЕНИЯ: выбор транспорта и точки, единая кнопка
// вкл/выкл, статус, жизненный цикл сессии в БД. Выделен из MainWindowViewModel
// при разделении по вкладкам (см. Misc/docs/viewmodels.md).
//
// Подписан только на ConnectionStateChanged; поток ДАННЫХ (DataReceived →
// Channel<Frame>) остаётся в MainWindowViewModel — это конвейер, не подключение.
public class ConnectionViewModel : ViewModelBase, IDisposable
{
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IAppStore? _appStore;
    private readonly SessionTracker _sessions;

    // ФАКТ подключения; Selected* — лишь намерение (выбор в ComboBox).
    // StatusText и кнопка-переключатель строятся по факту, поэтому листание
    // списков их не путает.
    private ITransport? _activeTransport;
    private string? _activeEndpoint;

    private ConnectionState _connectionStatus = ConnectionState.Disconnected;
    private ITransport _selectedTransport;
    private string? _selectedEndpoint;

    public ConnectionViewModel(
        IReadOnlyList<ITransport> transports,
        IUiDispatcher uiDispatcher,
        IAppStore? appStore,
        SessionTracker sessions,
        string? initialEndpoint = null,
        string? initialTransportName = null
    )
    {
        if (transports.Count == 0)
        {
            throw new ArgumentException("Нужен хотя бы один транспорт.", nameof(transports));
        }

        Transports = transports;
        _uiDispatcher = uiDispatcher;
        _appStore = appStore;
        _sessions = sessions;
        // Сверка при старте: сохранённый транспорт может уже не существовать —
        // тогда молча берём первый (аналогично initialEndpoint ниже)
        _selectedTransport =
            transports.FirstOrDefault(t => t.DisplayName == initialTransportName)
            ?? transports[0];

        if (_selectedTransport is ISerialSettings serial)
        {
            _baudRateText = serial.BaudRate.ToString();
            _reconnectDelayText = serial.ReconnectDelaySeconds.ToString();
        }

        foreach (var transport in Transports)
        {
            transport.ConnectionStateChanged += OnConnectionStateChanged;
        }

        RefreshEndpointsCommand = new RelayCommand(RefreshEndpoints);
        ConnectCommand = new RelayCommand(Connect, CanConnect);
        DisconnectCommand = new RelayCommand(Disconnect, CanDisconnect);
        ToggleConnectionCommand = new RelayCommand(ToggleConnection, CanToggleConnection);

        RefreshEndpoints();

        // Порт из настроек — только предвыбор в списке, не автоподключение
        if (initialEndpoint != null && AvailableEndpoints.Contains(initialEndpoint))
        {
            SelectedEndpoint = initialEndpoint;
        }
    }

    public IReadOnlyList<ITransport> Transports { get; }
    public ObservableCollection<string> AvailableEndpoints { get; } = new();

    public RelayCommand RefreshEndpointsCommand { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand ToggleConnectionCommand { get; }

    public string ToggleConnectionText => _activeTransport == null ? "Подключить" : "Отключить";

    // Поднимается после успешного запуска подключения — App сохраняет выбор
    // (транспорт/порт/бод/задержку) в settings.json, VM про файл не знает.
    public event Action? ConnectionSettingsChanged;

    // --- Параметры Serial (видны, только если транспорт их поддерживает) ---

    public bool HasSerialSettings => SelectedTransport is ISerialSettings;

    private string _baudRateText = "115200";
    public string BaudRateText
    {
        get => _baudRateText;
        set => SetProperty(ref _baudRateText, value);
    }

    private string _reconnectDelayText = "2";
    public string ReconnectDelayText
    {
        get => _reconnectDelayText;
        set => SetProperty(ref _reconnectDelayText, value);
    }

    // Применяем то, что распарсилось; мусор в поле молча игнорируется
    // (поле остаётся, транспорт работает на прежнем значении)
    private void ApplySerialSettings(ITransport transport)
    {
        if (transport is not ISerialSettings serial)
            return;

        if (int.TryParse(BaudRateText, out int baud) && baud > 0)
        {
            serial.BaudRate = baud;
        }
        if (int.TryParse(ReconnectDelayText, out int delay) && delay >= 0)
        {
            serial.ReconnectDelaySeconds = delay;
        }
    }

    public ITransport SelectedTransport
    {
        get => _selectedTransport;
        set
        {
            if (SetProperty(ref _selectedTransport, value))
            {
                // У другого транспорта — другие точки подключения и параметры
                RefreshEndpoints();
                OnPropertyChanged(nameof(HasSerialSettings));
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

    public ConnectionState ConnectionStatus
    {
        get => _connectionStatus;
        private set
        {
            if (SetProperty(ref _connectionStatus, value))
            {
                ConnectCommand.NotifyCanExecuteChanged();
                DisconnectCommand.NotifyCanExecuteChanged();
                ToggleConnectionCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ToggleConnectionText));
            }
        }
    }

    // Индикатор для GUI: не только состояние, но и К ЧЕМУ оно относится
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
                // моменты сразу после "Подключить" или между разрывом и ретраем
                _ => $"Подключение: {_activeTransport.DisplayName} · {_activeEndpoint}",
            };

    private bool CanConnect() =>
        SelectedEndpoint != null && ConnectionStatus == ConnectionState.Disconnected;

    // Reconnecting тоже "подключён" в смысле кнопки: Disconnect — отмена ретраев
    private bool CanDisconnect() => ConnectionStatus != ConnectionState.Disconnected;

    private bool CanToggleConnection() => _activeTransport != null || SelectedEndpoint != null;

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

    private void Connect()
    {
        if (SelectedEndpoint == null)
            return;

        // Защита от повторного вызова: прежний активный транспорт закрываем
        // до открытия нового, чтобы не осталось двух живых read-потоков
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
        _appStore.TrySaveEvent(
            _sessions,
            "Info",
            "Connection",
            $"Подключение: {_activeTransport.DisplayName} · {SelectedEndpoint}"
        );

        ApplySerialSettings(_activeTransport);
        _activeTransport.Open(SelectedEndpoint);
        NotifyConnectionFactChanged();

        // Выбор подключения запоминается (persist в App)
        ConnectionSettingsChanged?.Invoke();
    }

    private void Disconnect()
    {
        _activeTransport?.Close();
        _activeTransport = null;
        _activeEndpoint = null;

        _appStore.TrySaveEvent(_sessions, "Info", "Connection", "Отключение по команде пользователя");

        if (_sessions.CurrentId is long session && _appStore != null)
        {
            try
            {
                _appStore.EndSession(session);
            }
            catch
            {
                // См. TrySaveEvent — репортить некуда
            }
            _sessions.Clear();
        }

        // Close() по команде пользователя — штатная остановка, а не разрыв,
        // транспорт сам ConnectionStateChanged не поднимает. Статус выставляем сами.
        ConnectionStatus = ConnectionState.Disconnected;
        NotifyConnectionFactChanged();
    }

    private void NotifyConnectionFactChanged()
    {
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

        // Прежний выбор сохраняем, только если он всё ещё существует
        if (SelectedEndpoint == null || !AvailableEndpoints.Contains(SelectedEndpoint))
        {
            SelectedEndpoint = AvailableEndpoints.FirstOrDefault();
        }
    }

    // Вызывается напрямую из read-потока транспорта (см. SerialTransport.RunLoop)
    private void OnConnectionStateChanged(ConnectionState state)
    {
        _uiDispatcher.Post(() =>
        {
            // Если пользователь уже нажал "Отключить", запоздавшее событие от
            // только что закрытого транспорта не должно перетирать статус
            if (_activeTransport != null)
            {
                ConnectionStatus = state;

                _appStore.TrySaveEvent(
                    _sessions,
                    state == ConnectionState.Connected ? "Info" : "Warning",
                    "Connection",
                    $"Состояние: {state} ({_activeTransport.DisplayName} · {_activeEndpoint})"
                );
            }
        });
    }

    public void Dispose()
    {
        foreach (var transport in Transports)
        {
            transport.ConnectionStateChanged -= OnConnectionStateChanged;
        }
    }
}
