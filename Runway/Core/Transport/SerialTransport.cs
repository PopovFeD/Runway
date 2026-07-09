using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Runway.Transport;

public class SerialTransport : ISerialTransport
{
    private readonly ILogger<SerialTransport> _logger;
    private readonly TimeSpan _reconnectDelay;

    private SerialPort? _port;
    private Thread? _runThread;
    private volatile bool _keepRunning;

    private string _portName = string.Empty;
    private int _baudRate;

    public SerialTransport(ILogger<SerialTransport>? logger = null, TimeSpan? reconnectDelay = null)
    {
        _logger = logger ?? NullLogger<SerialTransport>.Instance;
        _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(2);
    }

    public bool IsOpen => _port is { IsOpen: true };

    public event Action<byte[]>? DataReceived;
    public event Action<ConnectionState>? ConnectionStateChanged;

    public void Open(string portName, int baudRate)
    {
        if (_keepRunning)
        {
            Close();
        }

        _portName = portName;
        _baudRate = baudRate;

        _keepRunning = true;
        _runThread = new Thread(RunLoop) { IsBackground = true };
        _runThread.Start();
    }

    public void Close()
    {
        _keepRunning = false;

        // Join не привязан к _reconnectDelay: и ReadLoop (ReadTimeout=500мс), и пауза
        // между попытками переподключения (WaitBeforeRetry) проверяют _keepRunning
        // короткими шагами, так что поток должен успеть выйти намного раньше.
        _runThread?.Join(1500);
        _runThread = null;

        ClosePortQuietly();
    }

    // Верхнеуровневый цикл жизни соединения: открыть порт → читать, пока получается →
    // при разрыве закрыть и подождать → снова открыть. Крутится, пока не позовут Close().
    private void RunLoop()
    {
        while (_keepRunning)
        {
            if (!TryOpenPort())
            {
                WaitBeforeRetry();
                continue;
            }

            _logger.LogInformation(
                "Порт {PortName} открыт ({BaudRate} бод).",
                _portName,
                _baudRate
            );
            ConnectionStateChanged?.Invoke(ConnectionState.Connected);

            ReadLoop(); // возвращается, когда порт разорвался или пришла команда остановки

            ClosePortQuietly();

            if (_keepRunning)
            {
                _logger.LogWarning(
                    "Порт {PortName} разорван, пробуем переподключиться через {Delay}.",
                    _portName,
                    _reconnectDelay
                );
                ConnectionStateChanged?.Invoke(ConnectionState.Disconnected);
                WaitBeforeRetry();
            }
        }
    }

    private bool TryOpenPort()
    {
        try
        {
            _port = new SerialPort(_portName, _baudRate) { ReadTimeout = 500 };
            _port.Open();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось открыть порт {PortName}.", _portName);
            _port = null;
            return false;
        }
    }

    // Спит небольшими шагами, а не одним Thread.Sleep(_reconnectDelay) — чтобы Close()
    // не пришлось ждать до конца полной паузы перед выходом из потока.
    private void WaitBeforeRetry()
    {
        ConnectionStateChanged?.Invoke(ConnectionState.Reconnecting);

        var step = TimeSpan.FromMilliseconds(100);
        var elapsed = TimeSpan.Zero;
        while (_keepRunning && elapsed < _reconnectDelay)
        {
            Thread.Sleep(step);
            elapsed += step;
        }
    }

    // Работает, пока порт открыт и жив. Это тот же подход, что в tools/com_reading.cs —
    // читаем блокирующе, а не полагаемся на событие DataReceived (оно ненадёжно с com0com).
    private void ReadLoop()
    {
        var buffer = new byte[1024];

        while (_keepRunning && _port != null)
        {
            try
            {
                int bytesRead = _port.Read(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                {
                    var received = new byte[bytesRead];
                    Array.Copy(buffer, received, bytesRead);
                    DataReceived?.Invoke(received);
                }
            }
            catch (TimeoutException)
            {
                // Нормальная ситуация — просто нет данных за последние 500 мс, пробуем снова
            }
            catch (Exception ex)
            {
                // Порт закрылся или сломался — выходим из ReadLoop, RunLoop решит,
                // переподключаться или нет (зависит от _keepRunning).
                _logger.LogError(ex, "Ошибка чтения с порта {PortName}.", _portName);
                break;
            }
        }
    }

    private void ClosePortQuietly()
    {
        if (_port == null)
            return;

        try
        {
            _port.Close();
        }
        catch
        {
            // Порт уже мог быть закрыт или сломан снаружи — не мешаем очистке ресурсов
        }

        _port.Dispose();
        _port = null;
    }
}
