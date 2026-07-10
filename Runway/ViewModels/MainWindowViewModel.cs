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
        string? exportDirectory = null,
        IReadOnlyCollection<string>? hiddenSections = null
    )
    {
        _frameReader = frameReader;
        _uiDispatcher = uiDispatcher;
        _appStore = appStore;
        _sessions = sessionTracker ?? new SessionTracker();
        _boundedLog = new BoundedLog(LogEntries, maxLogEntries);

        Connection = new ConnectionViewModel(
            transports,
            uiDispatcher,
            appStore,
            _sessions,
            initialEndpoint
        );
        Logs = new LogsViewModel(appStore, _sessions, exportDirectory);
        Export = new ExportViewModel(appStore, _sessions, exportDirectory);

        Sections.Add(
            new ProtocolSectionViewModel(
                "Telemetry",
                "v1 · Telemetry (0x10)",
                _tileTemperature,
                _tileHumidity
            )
        );
        Sections.Add(
            new ProtocolSectionViewModel(
                "Environment",
                "v1 · Environment (0x11)",
                _tilePressure,
                _tileLight
            )
        );
        foreach (var section in Sections)
        {
            section.IsVisible = hiddenSections?.Contains(section.ProtocolKey) != true;
        }

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
    public ExportViewModel Export { get; }

    // Живой вывод для Дашборда. Ограничен через _boundedLog — история в БД.
    public ObservableCollection<string> LogEntries { get; } = new();

    // --- Дашборд: разделы по протоколам (типам сообщений) ---
    // Новый тип сообщения = новый раздел со своими плитками; какие разделы
    // показывать — галочки во вкладке "Настройки" (persist в settings.json).

    private readonly TileViewModel _tileTemperature = new("Температура");
    private readonly TileViewModel _tileHumidity = new("Влажность");
    private readonly TileViewModel _tilePressure = new("Давление");
    private readonly TileViewModel _tileLight = new("Освещённость");

    public ObservableCollection<ProtocolSectionViewModel> Sections { get; } = new();

    // Автопрокрутка терминала к последней записи — только когда включена
    // кнопка-переключатель на Дашборде (использует code-behind MainWindow).
    private bool _followTail = true;
    public bool FollowTail
    {
        get => _followTail;
        set => SetProperty(ref _followTail, value);
    }

    // FormattableString + Invariant: "24.53", а не "24,53" на русской локали
    private static void SetTile(TileViewModel tile, FormattableString value, string updated)
    {
        tile.Value = FormattableString.Invariant(value);
        tile.UpdatedAtText = updated;
    }

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

                    // Сенсорные данные — в БД (каждый протокол в свою таблицу);
                    // ошибка записи не маскируется под ParseError и не
                    // останавливает конвейер
                    if (_appStore != null)
                    {
                        try
                        {
                            switch (packet)
                            {
                                case TelemetryPacket t:
                                    _appStore.SaveTelemetry(
                                        new TelemetryRecord(
                                            DateTime.Now,
                                            frame.Sequence,
                                            t.Temperature,
                                            t.Humidity,
                                            _sessions.CurrentId
                                        )
                                    );
                                    break;
                                case EnvironmentPacket e:
                                    _appStore.SaveEnvironment(
                                        new EnvironmentRecord(
                                            DateTime.Now,
                                            frame.Sequence,
                                            e.PressureHpa,
                                            e.LightLux,
                                            _sessions.CurrentId
                                        )
                                    );
                                    break;
                            }
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

                    // Плитки Дашборда: последние значения + время обновления
                    string updated = $"обновлено {DateTime.Now:HH:mm:ss}";
                    switch (uiPacket)
                    {
                        case TelemetryPacket t:
                            SetTile(_tileTemperature, $"{t.Temperature:F2} °C", updated);
                            SetTile(_tileHumidity, $"{t.Humidity:F2} %", updated);
                            break;
                        case EnvironmentPacket e:
                            SetTile(_tilePressure, $"{e.PressureHpa:F2} hPa", updated);
                            SetTile(_tileLight, $"{e.LightLux:F2} lx", updated);
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
