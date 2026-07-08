namespace Runway.Transport;

public interface ISerialTransport
{
    bool IsOpen { get; }

    void Open(string portName, int baudRate);
    void Close();

    // Вызывается каждый раз, когда пришли новые байты из порта
    event Action<byte[]>? DataReceived;
}
