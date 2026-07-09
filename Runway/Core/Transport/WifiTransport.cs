using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Runway.Transport;

// Заглушка под будущее подключение к ESP32 по WiFi (TCP-сокет поверх локальной
// сети). Существует уже сейчас, чтобы GUI и MainWindowViewModel с самого начала
// работали со СПИСКОМ транспортов, а не с единственным SerialTransport — иначе
// добавление WiFi потом потребовало бы перекраивать ViewModel и разметку.
//
// Пока устройств нет, GetAvailableEndpoints возвращает пустой список — из-за
// этого в GUI нечего выбрать и кнопка "Подключить" остаётся выключенной,
// то есть Open() из интерфейса вызвать невозможно. Open() всё равно бросает
// исключение — чтобы программная ошибка (вызов мимо GUI) упала громко,
// а не имитировала подключение.
public class WifiTransport : ITransport
{
    private readonly ILogger<WifiTransport> _logger;

    public WifiTransport(ILogger<WifiTransport>? logger = null)
    {
        _logger = logger ?? NullLogger<WifiTransport>.Instance;
    }

    public string DisplayName => "WiFi (ESP32)";

    public bool IsOpen => false;

    // События пока никогда не поднимаются — реального канала нет. pragma глушит
    // предупреждение компилятора CS0067 ("event never used"): для заглушки это
    // ожидаемо, интерфейс требует объявить события даже без реализации.
#pragma warning disable CS0067
    public event Action<byte[]>? DataReceived;
    public event Action<ConnectionState>? ConnectionStateChanged;
#pragma warning restore CS0067

    public IReadOnlyList<string> GetAvailableEndpoints()
    {
        // Будущее: обнаружение ESP32 в сети (mDNS) или фиксированный список
        // адресов из settings.json. Пока — подключаться не к чему.
        return Array.Empty<string>();
    }

    public void Open(string endpoint)
    {
        _logger.LogError("WiFi-транспорт ещё не реализован (endpoint: {Endpoint}).", endpoint);
        throw new NotSupportedException("WiFi-транспорт ещё не реализован.");
    }

    public void Close()
    {
        // Открыть нельзя — значит, и закрывать нечего
    }
}
