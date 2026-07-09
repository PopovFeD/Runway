using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Runway.Framing;
using Runway.Logging;
using Runway.Protocol;
using Runway.Storage;
using Runway.Threading;
using Runway.Transport;

namespace Runway.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly FrameReader _frameReader;
    private readonly ILogFileWriter _logFileWriter;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly BoundedLog _boundedLog;

    // Транспорт, которому реально сказали Open() — только его и нужно закрывать.
    // Не то же самое, что SelectedTransport: пользователь может выбрать в списке
    // другой транспорт, пока активный ещё подключён (Connect до этого не дойдёт —
    // кнопка выключена, — но выбор в ComboBox уже поменяется).
    private ITransport? _activeTransport;

    // Точка, к которой реально подключены (для индикатора StatusText) — выбор
    // в ComboBox может уже уехать на другой транспорт/порт, индикатор не должен.
    private string? _activeEndpoint;

    // Хранилище телеметрии; null — работаем без БД (например, в части тестов).
    private readonly ITelemetryStore? _telemetryStore;

    // Очередь между read-потоком порта (продюсер) и разбором пакетов (консьюмер).
    // Смысл: OnDataReceived вызывается прямо из фонового потока SerialTransport.ReadLoop,
    // и всё, что там выполняется синхронно, тормозит следующий Port.Read().
    // Сейчас разбор дешёвый, но когда сюда добавится запись в SQLite — это будет уже
    // не мелочь. Поэтому OnDataReceived только режет байты на кадры и кладёт их в канал,
    // а PacketParser.Parse (и в будущем — запись в БД) переезжает в ProcessFramesAsync,
    // который крутится в отдельной задаче независимо от порта.
    private readonly Channel<Frame> _frameChannel = Channel.CreateUnbounded<Frame>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
    );

    private readonly CancellationTokenSource _processingCts = new();
    private readonly Task _processingTask;

    private ConnectionState _connectionStatus = ConnectionState.Disconnected;
    private ITransport _selectedTransport;
    private string? _selectedEndpoint;

    public MainWindowViewModel(
        FrameReader frameReader,
        IReadOnlyList<ITransport> transports,
        ILogFileWriter logFileWriter,
        IUiDispatcher uiDispatcher,
        ITelemetryStore? telemetryStore = null,
        int maxLogEntries = 500,
        string? initialEndpoint = null
    )
    {
        if (transports.Count == 0)
        {
            throw new ArgumentException("Нужен хотя бы один транспорт.", nameof(transports));
        }

        _frameReader = frameReader;
        Transports = transports;
        _logFileWriter = logFileWriter;
        _uiDispatcher = uiDispatcher;
        _telemetryStore = telemetryStore;
        _boundedLog = new BoundedLog(LogEntries, maxLogEntries);

        // Напрямую в поле, не через свойство: сеттер SelectedTransport дёргает
        // RefreshEndpoints, а команды на этот момент ещё не созданы.
        _selectedTransport = transports[0];

        // Подписываемся сразу на все транспорты, а не только на активный:
        // события всё равно шлёт лишь тот, у кого вызван Open, зато не нужно
        // переподписываться при каждом Connect/Disconnect.
        foreach (var transport in Transports)
        {
            transport.DataReceived += OnDataReceived;
            transport.ConnectionStateChanged += OnConnectionStateChanged;
        }

        RefreshEndpointsCommand = new RelayCommand(RefreshEndpoints);
        ConnectCommand = new RelayCommand(Connect, CanConnect);
        DisconnectCommand = new RelayCommand(Disconnect, CanDisconnect);

        RefreshEndpoints();

        // Порт из настроек — только предвыбор в списке, не автоподключение.
        // Если такого порта в системе сейчас нет, остаётся выбор RefreshEndpoints.
        if (initialEndpoint != null && AvailableEndpoints.Contains(initialEndpoint))
        {
            SelectedEndpoint = initialEndpoint;
        }

        _processingTask = Task.Run(() => ProcessFramesAsync(_processingCts.Token));
    }

    // Все известные приложению способы подключения (Serial, WiFi-заглушка, ...) —
    // источник для ComboBox выбора транспорта в GUI.
    public IReadOnlyList<ITransport> Transports { get; }

    // Точки подключения выбранного транспорта (COM-порты / адреса устройств) —
    // источник для второго ComboBox. Обновляется при смене транспорта и по кнопке.
    public ObservableCollection<string> AvailableEndpoints { get; } = new();

    // Сюда GUI смотрит, чтобы показать лог принятых кадров. Ограничена по размеру
    // через _boundedLog — полный, неограниченный лог всегда есть в файле (LogFileWriter).
    public ObservableCollection<string> LogEntries { get; } = new();

    public RelayCommand RefreshEndpointsCommand { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }

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
                OnPropertyChanged(nameof(StatusText));
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
        _activeTransport.Open(SelectedEndpoint);
        OnPropertyChanged(nameof(StatusText));
    }

    private void Disconnect()
    {
        _activeTransport?.Close();
        _activeTransport = null;
        _activeEndpoint = null;

        // Close() по команде пользователя — штатная остановка, а не разрыв,
        // транспорт сам ConnectionStateChanged не поднимает. Статус выставляем сами.
        ConnectionStatus = ConnectionState.Disconnected;
        OnPropertyChanged(nameof(StatusText));
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
            }
        });
    }

    // Вызывается напрямую из read-потока транспорта (см. SerialTransport.ReadLoop).
    // Должен оставаться максимально дешёвым: только выделение кадров из потока байт
    // (FrameReader.Append — операции в памяти без I/O) и запись готовых кадров в канал.
    // Никакого разбора протокола и никакого I/O здесь быть не должно.
    private void OnDataReceived(byte[] bytes)
    {
        var frames = _frameReader.Append(bytes);

        foreach (var frame in frames)
        {
            // Канал безлимитный, TryWrite не блокирует и не может вернуть false —
            // пишем без ожидания, чтобы read-поток порта не задержался ни на миллисекунду.
            _frameChannel.Writer.TryWrite(frame);
        }
    }

    // Консьюмер очереди. Работает в собственной задаче, никак не связанной с потоком
    // чтения порта — сюда же в будущем переедет запись разобранного пакета в SQLite.
    private async Task ProcessFramesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _frameChannel.Reader.ReadAllAsync(cancellationToken))
            {
                string line;
                try
                {
                    object packet = PacketParser.Parse(frame);

                    // Телеметрия — в БД. Мы в консьюмере, read-поток порта это
                    // не задевает (ради чего Channel<Frame> и заводился).
                    // Ошибка записи не должна ни маскироваться под ParseError,
                    // ни останавливать конвейер — ловим её отдельно.
                    if (packet is TelemetryPacket telemetry && _telemetryStore != null)
                    {
                        try
                        {
                            _telemetryStore.Save(
                                new TelemetryRecord(
                                    DateTime.Now,
                                    frame.Sequence,
                                    telemetry.Temperature,
                                    telemetry.Humidity
                                )
                            );
                        }
                        catch (Exception ex)
                        {
                            _logFileWriter.WriteLine(
                                $"Seq={frame.Sequence}  StoreError: {ex.Message}"
                            );
                        }
                    }

                    line = packet switch
                    {
                        // CultureInfo.InvariantCulture — иначе на системах с русской локалью
                        // {t.Temperature:F2} даёт "24,53" вместо "24.53" (запятая вместо точки
                        // как разделитель дробной части). Лог должен выглядеть одинаково
                        // независимо от локали ОС, на которой запущено приложение.
                        TelemetryPacket t => string.Create(
                            CultureInfo.InvariantCulture,
                            $"Seq={frame.Sequence}  T={t.Temperature:F2}°C  H={t.Humidity:F2}%"
                        ),
                        string s => $"Seq={frame.Sequence}  {s}",
                        _ => $"Seq={frame.Sequence}  Type={frame.MessageType}",
                    };
                }
                catch (Exception ex)
                {
                    line =
                        $"Seq={frame.Sequence}  Type=0x{frame.MessageType:X2}  ParseError: {ex.Message}";
                }

                // Полный лог — на диск, без ограничений по размеру. Мы уже не в
                // read-потоке порта, так что запись на диск ему не мешает.
                _logFileWriter.WriteLine(line);

                // В GUI — с ограничением через BoundedLog, чтобы не тормозить ListBox
                // и не есть память сколь угодно долго работающей сессии.
                _uiDispatcher.Post(() => _boundedLog.Add(line));
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка при Dispose — не ошибка
        }
    }

    // Останавливает консьюмера и отписывается от транспортов. Вызывается из App при
    // выходе из приложения (см. App.axaml.cs). Сами транспорты здесь не закрываются —
    // ими, как и LogFileWriter, владеет тот, кто их создал (App.axaml.cs).
    public void Dispose()
    {
        foreach (var transport in Transports)
        {
            transport.DataReceived -= OnDataReceived;
            transport.ConnectionStateChanged -= OnConnectionStateChanged;
        }

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
