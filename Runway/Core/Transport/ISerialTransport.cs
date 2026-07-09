namespace Runway.Transport;

public interface ISerialTransport
{
    bool IsOpen { get; }

    void Open(string portName, int baudRate);
    void Close();

    // Вызывается каждый раз, когда пришли новые байты из порта
    event Action<byte[]>? DataReceived;

    // Вызывается при смене состояния подключения: разрыв, попытка
    // переподключения, успешное (пере-)подключение. Нужно, чтобы GUI могло
    // показать пользователю, что порт разорван, не парся текст лога.
    event Action<ConnectionState>? ConnectionStateChanged;
}
