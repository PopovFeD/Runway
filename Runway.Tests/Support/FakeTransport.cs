using Runway.Transport;

namespace Runway.Tests.Support;

// Тестовая реализация ITransport — не открывает реальный канал, а просто
// позволяет тесту вручную "впрыснуть" байты через RaiseDataReceived, как будто
// они только что пришли с устройства, и поднять смену состояния подключения.
// Для настоящего COM-порта см. SerialTransportIntegrationTests — это другой
// уровень тестирования.
public class FakeTransport : ITransport, ISerialSettings
{
    private readonly List<string> _endpoints;

    public FakeTransport(params string[] endpoints)
    {
        _endpoints = endpoints.ToList();
    }

    public string DisplayName { get; set; } = "Fake";

    public bool IsOpen { get; private set; }

    // ISerialSettings — чтобы тестировать применение бод/задержки из GUI
    public int BaudRate { get; set; } = 115200;
    public int ReconnectDelaySeconds { get; set; } = 2;

    // Что и сколько раз с этим транспортом делали — для проверок в тестах
    public string? LastOpenedEndpoint { get; private set; }
    public int CloseCallCount { get; private set; }

    public event Action<byte[]>? DataReceived;
    public event Action<ConnectionState>? ConnectionStateChanged;

    public IReadOnlyList<string> GetAvailableEndpoints() => _endpoints;

    public void Open(string endpoint)
    {
        IsOpen = true;
        LastOpenedEndpoint = endpoint;
    }

    public void Close()
    {
        IsOpen = false;
        CloseCallCount++;
    }

    public void RaiseDataReceived(byte[] bytes) => DataReceived?.Invoke(bytes);

    public void RaiseConnectionStateChanged(ConnectionState state) =>
        ConnectionStateChanged?.Invoke(state);
}
