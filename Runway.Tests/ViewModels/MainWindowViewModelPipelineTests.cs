using System.Diagnostics;
using Runway.Framing;
using Runway.Protocol;
using Runway.Tests.Support;
using Runway.ViewModels;
using Xunit;

namespace Runway.Tests.ViewModels;

// Тесты на связку "разбор кадров через Channel<Frame>" + "ограничение LogEntries" +
// "полный лог в файл". Транспорт, лог-файл и UI-диспетчер — тестовые заглушки
// (см. Runway.Tests.Support), реальный COM-порт и диск не участвуют — это другой
// уровень, чем SerialTransportIntegrationTests (тот бьёт по настоящим COM-портам).
//
// Пайплайн асинхронный (обработка кадров крутится в отдельной Task, см.
// MainWindowViewModel.ProcessFramesAsync), поэтому тесты ждут результата через
// WaitUntilAsync с таймаутом, а не проверяют состояние сразу после вызова.
public class MainWindowViewModelPipelineTests
{
    [Fact]
    public async Task DataReceived_ParsesTelemetryFrame_AndAddsLineToLogEntries()
    {
        var transport = new FakeTransport();
        var vm = new MainWindowViewModel(
            new FrameReader(),
            new[] { transport },
            new ImmediateUiDispatcher()
        );

        // Тот же payload, что в PacketParserTests.ParseTelemetry_ShouldDecodeValues:
        // T=24.53°C, H=51.28%.
        byte[] payload = { 0x95, 0x09, 0x08, 0x14 };
        byte[] frameBytes = FrameTestHelper.BuildFrameBytes(
            version: 1,
            messageType: (byte)MessageType.Telemetry,
            sequence: 7,
            payload
        );

        transport.RaiseDataReceived(frameBytes);

        await WaitUntilAsync(() => vm.LogEntries.Count > 0, TimeSpan.FromSeconds(2));

        var line = Assert.Single(vm.LogEntries);
        Assert.Contains("Seq=7", line);
        Assert.Contains("T=24.53", line);
        Assert.Contains("H=51.28", line);

        vm.Dispose();
    }

    [Fact]
    public async Task DataReceived_UnknownMessageType_LogsParseError_ButKeepsProcessingNextFrames()
    {
        var transport = new FakeTransport();
        var vm = new MainWindowViewModel(
            new FrameReader(),
            new[] { transport },
            new ImmediateUiDispatcher()
        );

        byte[] badFrame = FrameTestHelper.BuildFrameBytes(
            version: 1,
            messageType: 0x7E, // не входит в MessageType — PacketParser бросит исключение
            sequence: 1,
            payload: Array.Empty<byte>()
        );
        byte[] goodFrame = FrameTestHelper.BuildFrameBytes(
            version: 1,
            messageType: (byte)MessageType.Ping,
            sequence: 2,
            payload: Array.Empty<byte>()
        );

        transport.RaiseDataReceived(badFrame);
        transport.RaiseDataReceived(goodFrame);

        await WaitUntilAsync(() => vm.LogEntries.Count >= 2, TimeSpan.FromSeconds(2));

        Assert.Contains(vm.LogEntries, l => l.Contains("ParseError"));
        Assert.Contains(vm.LogEntries, l => l.Contains("PING"));

        vm.Dispose();
    }

    [Fact]
    public async Task LiveOutput_IsBounded_ButAllTelemetryReachesStore()
    {
        var transport = new FakeTransport();
        var store = new FakeAppStore();

        // Намеренно маленькая ёмкость живого вывода — 2 записи. Проверяем, что
        // в хранилище (историю) уходят ВСЕ кадры телеметрии, а GUI держит кап.
        var vm = new MainWindowViewModel(
            new FrameReader(),
            new[] { transport },
            new ImmediateUiDispatcher(),
            store,
            maxLogEntries: 2
        );

        // Тот же payload, что в PacketParserTests: T=24.53°C, H=51.28%
        byte[] payload = { 0x95, 0x09, 0x08, 0x14 };
        const int frameCount = 5;
        for (ushort seq = 0; seq < frameCount; seq++)
        {
            byte[] frame = FrameTestHelper.BuildFrameBytes(
                version: 1,
                messageType: (byte)MessageType.Telemetry,
                sequence: seq,
                payload: payload
            );
            transport.RaiseDataReceived(frame);
        }

        await WaitUntilAsync(() => store.Records.Count >= frameCount, TimeSpan.FromSeconds(2));

        Assert.Equal(frameCount, store.Records.Count);
        Assert.True(vm.LogEntries.Count <= 2);

        vm.Dispose();
    }

    [Fact]
    public void Dispose_UnsubscribesFromTransport_SoLateEventsAreIgnored()
    {
        var transport = new FakeTransport();
        var vm = new MainWindowViewModel(
            new FrameReader(),
            new[] { transport },
            new ImmediateUiDispatcher()
        );

        vm.Dispose();

        byte[] frame = FrameTestHelper.BuildFrameBytes(
            version: 1,
            messageType: (byte)MessageType.Ping,
            sequence: 1,
            payload: Array.Empty<byte>()
        );

        // После Dispose подписка на transport.DataReceived снята — поднять событие
        // можно без исключений, но обрабатывать его уже некому.
        var exception = Record.Exception(() => transport.RaiseDataReceived(frame));

        Assert.Null(exception);
        Assert.Empty(vm.LogEntries);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > timeout)
                throw new TimeoutException("Condition was not met within the timeout.");

            await Task.Delay(10);
        }
    }
}
