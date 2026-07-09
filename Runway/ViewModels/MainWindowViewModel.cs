using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using Runway.Framing;
using Runway.Protocol;
using Runway.Transport;

namespace Runway.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly FrameReader _frameReader;
    private readonly ISerialTransport _transport;
    private readonly IPortLister _portLister;

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

    public MainWindowViewModel(
        FrameReader frameReader,
        ISerialTransport transport,
        IPortLister portLister
    )
    {
        _frameReader = frameReader;
        _transport = transport;
        _portLister = portLister;

        _transport.DataReceived += OnDataReceived;
        _processingTask = Task.Run(() => ProcessFramesAsync(_processingCts.Token));
    }

    public string Greeting => "Welcome to Avalonia!";
    public IReadOnlyList<string> AvailablePorts => _portLister.GetAvailablePorts();

    // Сюда GUI будет смотреть, чтобы показать лог принятых кадров
    public ObservableCollection<string> LogEntries { get; } = new();

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
                        TelemetryPacket t =>
                            $"Seq={frame.Sequence}  T={t.Temperature:F2}°C  H={t.Humidity:F2}%",
                        string s => $"Seq={frame.Sequence}  {s}",
                        _ => $"Seq={frame.Sequence}  Type={frame.MessageType}",
                    };
                }
                catch (Exception ex)
                {
                    line =
                        $"Seq={frame.Sequence}  Type=0x{frame.MessageType:X2}  ParseError: {ex.Message}";
                }

                // LogEntries привязана к GUI — трогать её можно только из UI-потока.
                // Dispatcher.UIThread.Post перекладывает это действие туда.
                Dispatcher.UIThread.Post(() => LogEntries.Add(line));
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка при Dispose — не ошибка
        }
    }

    // Останавливает консьюмера и отписывается от порта. Вызывается из App при выходе
    // из приложения (см. App.axaml.cs), чтобы не оставлять висящую фоновую задачу.
    public void Dispose()
    {
        _transport.DataReceived -= OnDataReceived;
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
