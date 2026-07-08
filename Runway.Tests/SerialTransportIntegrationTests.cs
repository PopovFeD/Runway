using System.IO.Ports;
using System.Linq;
using Runway.Framing;
using Runway.Tests.Support;
using Runway.Transport;
using Xunit;

namespace Runway.Tests;

// Интеграционные тесты реальной пары виртуальных COM-портов (com0com).
// Требуют физически настроенных портов на машине, где запускается dotnet test,
// и НЕ входят в обычный быстрый прогон:
//   dotnet test --filter Category!=Integration   — обычные тесты
//   dotnet test --filter Category=Integration     — только эти
[Trait("Category", "Integration")]
public class SerialTransportIntegrationTests : IDisposable
{
    // Порт, который открывает наш SerialTransport (сторона приложения)
    private static readonly string PortA =
        Environment.GetEnvironmentVariable("RUNWAY_TEST_PORT_A") ?? "COM6";

    // Другой конец пары com0com — сюда пишем сырым SerialPort из BCL, как будто это устройство
    private static readonly string PortB =
        Environment.GetEnvironmentVariable("RUNWAY_TEST_PORT_B") ?? "COM4";

    private const int BaudRate = 115200;

    private readonly SerialTransport _transport = new();
    private SerialPort? _emulatorPort;

    [Fact]
    public void DataReceived_ParsesFrame_WhenFrameArrivesWhole()
    {
        EnsurePortsAvailable();

        byte[] payload = { 0x11, 0x22, 0x33 };
        byte[] frameBytes = FrameTestHelper.BuildFrameBytes(
            version: 0x01,
            messageType: 0x05,
            sequence: 0x0042,
            payload
        );

        var frameReader = new FrameReader();
        var receivedFrames = new List<Frame>();
        using var frameArrived = new ManualResetEventSlim(false);

        _transport.DataReceived += bytes =>
        {
            var frames = frameReader.Append(bytes);
            if (frames.Count > 0)
            {
                receivedFrames.AddRange(frames);
                frameArrived.Set();
            }
        };

        _transport.Open(PortA, BaudRate);

        _emulatorPort = new SerialPort(PortB, BaudRate);
        _emulatorPort.Open();
        _emulatorPort.Write(frameBytes, 0, frameBytes.Length);

        bool signaled = frameArrived.Wait(TimeSpan.FromSeconds(3));

        Assert.True(
            signaled,
            $"Кадр не пришёл за 3 секунды — проверь, что порты {PortA}/{PortB} открыты, "
                + "не заняты другим процессом, и com0com настроен верно."
        );

        var frame = Assert.Single(receivedFrames);
        Assert.Equal((byte)0x01, frame.Version);
        Assert.Equal((byte)0x05, frame.MessageType);
        Assert.Equal((ushort)0x0042, frame.Sequence);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public void DataReceived_ParsesFrame_WhenFrameArrivesInTwoWrites()
    {
        EnsurePortsAvailable();

        byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF };
        byte[] frameBytes = FrameTestHelper.BuildFrameBytes(
            version: 0x02,
            messageType: 0x06,
            sequence: 0x00AA,
            payload
        );

        var frameReader = new FrameReader();
        var receivedFrames = new List<Frame>();
        using var frameArrived = new ManualResetEventSlim(false);

        _transport.DataReceived += bytes =>
        {
            var frames = frameReader.Append(bytes);
            if (frames.Count > 0)
            {
                receivedFrames.AddRange(frames);
                frameArrived.Set();
            }
        };

        _transport.Open(PortA, BaudRate);

        _emulatorPort = new SerialPort(PortB, BaudRate);
        _emulatorPort.Open();

        int splitPoint = frameBytes.Length - 2;

        // Пишем кадр двумя частями с паузой — проверяем, что настоящий драйвер COM-порта
        // не склеивает/не режет данные неожиданно, и что SerialTransport + FrameReader
        // всё равно корректно соберут кадр по частям (в отличие от синтетического теста
        // в UnitTest1.cs, где мы сами резали байты in-memory).
        _emulatorPort.Write(frameBytes, 0, splitPoint);
        Thread.Sleep(100);
        _emulatorPort.Write(frameBytes, splitPoint, frameBytes.Length - splitPoint);

        bool signaled = frameArrived.Wait(TimeSpan.FromSeconds(3));

        Assert.True(
            signaled,
            $"Кадр не собрался за 3 секунды при передаче по частям — проверь порты {PortA}/{PortB} "
                + "и настройку com0com."
        );

        var frame = Assert.Single(receivedFrames);
        Assert.Equal((byte)0x02, frame.Version);
        Assert.Equal((byte)0x06, frame.MessageType);
        Assert.Equal((ushort)0x00AA, frame.Sequence);
        Assert.Equal(payload, frame.Payload);
    }

    // Явная, понятная ошибка вместо невнятного таймаута или NullReferenceException,
    // если на машине не настроена пара com0com или порты называются иначе.
    private static void EnsurePortsAvailable()
    {
        string[] availablePorts = SerialPort.GetPortNames();

        bool hasPortA = availablePorts.Contains(PortA);
        bool hasPortB = availablePorts.Contains(PortB);

        if (!hasPortA || !hasPortB)
        {
            Assert.Fail(
                $"Не найдены оба порта пары com0com: {PortA}, {PortB}. "
                    + $"Доступные порты: {string.Join(", ", availablePorts)}. "
                    + "Настрой com0com или укажи другие имена через переменные окружения "
                    + "RUNWAY_TEST_PORT_A / RUNWAY_TEST_PORT_B."
            );
        }
    }

    public void Dispose()
    {
        _transport.Close();

        if (_emulatorPort is { IsOpen: true })
        {
            _emulatorPort.Close();
        }

        _emulatorPort?.Dispose();
    }
}
