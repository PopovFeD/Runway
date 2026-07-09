using Runway.Transport;

namespace Runway.Tests.Support;

// Тестовая реализация ISerialTransport — не открывает реальный порт, а просто
// позволяет тесту вручную "впрыснуть" байты через RaiseDataReceived, как будто
// они только что пришли с порта. Для настоящего COM-порта см.
// SerialTransportIntegrationTests — это другой уровень тестирования.
public class FakeSerialTransport : ISerialTransport
{
    public bool IsOpen { get; private set; }

    public event Action<byte[]>? DataReceived;
    public event Action<ConnectionState>? ConnectionStateChanged;

    public void Open(string portName, int baudRate) => IsOpen = true;

    public void Close() => IsOpen = false;

    public void RaiseDataReceived(byte[] bytes) => DataReceived?.Invoke(bytes);

    public void RaiseConnectionStateChanged(ConnectionState state) =>
        ConnectionStateChanged?.Invoke(state);
}
