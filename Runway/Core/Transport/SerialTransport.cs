using System.IO.Ports;

namespace Runway.Transport;

public class SerialTransport : ISerialTransport
{
    private SerialPort? _port;
    private Thread? _readThread;
    private volatile bool _keepReading;

    public bool IsOpen => _port is { IsOpen: true };

    public event Action<byte[]>? DataReceived;

    public void Open(string portName, int baudRate)
    {
        if (IsOpen)
        {
            Close();
        }

        // ReadTimeout нужен, чтобы port.Read() не блокировался навечно,
        // а периодически "просыпался" и проверял, не пора ли остановиться (см. Close())
        _port = new SerialPort(portName, baudRate) { ReadTimeout = 500 };
        _port.Open();

        _keepReading = true;
        _readThread = new Thread(ReadLoop) { IsBackground = true };
        _readThread.Start();
    }

    public void Close()
    {
        _keepReading = false;
        _readThread?.Join(1000); // ждём, пока поток чтения сам остановится
        _readThread = null;

        if (_port == null)
            return;

        _port.Close();
        _port.Dispose();
        _port = null;
    }

    // Работает в отдельном потоке, пока порт открыт.
    // Это тот же подход, что в tools/com_reading.cs — просто читаем блокирующе,
    // а не полагаемся на событие DataReceived (оно ненадёжно с com0com).
    private void ReadLoop()
    {
        var buffer = new byte[1024];

        while (_keepReading && _port != null)
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
            catch (Exception)
            {
                // Порт закрылся или сломался — выходим из цикла
                break;
            }
        }
    }
}
