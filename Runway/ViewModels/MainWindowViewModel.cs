using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Runway.Framing;
using Runway.Logging;
using Runway.Protocol;
using Runway.Threading;
using Runway.Transport;

namespace Runway.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly FrameReader _frameReader;
    private readonly ISerialTransport _transport;
    private readonly IPortLister _portLister;
    private readonly ILogFileWriter _logFileWriter;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly BoundedLog _boundedLog;

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

    public MainWindowViewModel(
        FrameReader frameReader,
        ISerialTransport transport,
        IPortLister portLister,
        ILogFileWriter logFileWriter,
        IUiDispatcher uiDispatcher,
        int maxLogEntries = 500
    )
    {
        _frameReader = frameReader;
        _transport = transport;
        _portLister = portLister;
        _logFileWriter = logFileWriter;
        _uiDispatcher = uiDispatcher;
        _boundedLog = new BoundedLog(LogEntries, maxLogEntries);

        _transport.DataReceived += OnDataReceived;
        _transport.ConnectionStateChanged += OnConnectionStateChanged;
        _processingTask = Task.Run(() => ProcessFramesAsync(_processingCts.Token));
    }

    public string Greeting => "Welcome to Avalonia!";
    public IReadOnlyList<string> AvailablePorts => _portLister.GetAvailablePorts();

    // GUI смотрит сюда, чтобы показать, разорван ли порт и идёт ли переподключение
    // (см. SerialTransport.ConnectionStateChanged) — без парсинга текста лога.
    public ConnectionState ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    // Сюда GUI смотрит, чтобы показать лог принятых кадров. Ограничена по размеру
    // через _boundedLog — полный, неограниченный лог всегда есть в файле (LogFileWriter).
    public ObservableCollection<string> LogEntries { get; } = new();

    // Вызывается напрямую из read-потока SerialTransport (см. SerialTransport.RunLoop).
    private void OnConnectionStateChanged(ConnectionState state)
    {
        _uiDispatcher.Post(() => ConnectionStatus = state);
    }

    // Вызывается напрямую из read-потока SerialTransport (см. SerialTransport.ReadLoop).
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

    // Останавливает консьюмера и отписывается от порта. Вызывается из App при выходе
    // из приложения (см. App.axaml.cs), чтобы не оставлять висящую фоновую задачу.
    // LogFileWriter здесь намеренно не закрывается — им управляет тот, кто его создал
    // (App.axaml.cs), по аналогии с transport/portLister, которыми VM тоже не владеет.
    public void Dispose()
    {
        _transport.DataReceived -= OnDataReceived;
        _transport.ConnectionStateChanged -= OnConnectionStateChanged;
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
