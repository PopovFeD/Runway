using System.Diagnostics;
using Runway.Framing;
using Runway.Protocol;
using Runway.Tests.Support;
using Runway.ViewModels;
using Xunit;

namespace Runway.Tests.ViewModels;

// Тесты на запись телеметрии в ITelemetryStore из консьюмера очереди кадров.
public class MainWindowViewModelTelemetryStoreTests
{
    // Payload как в PacketParserTests: T=24.53°C, H=51.28%
    private static readonly byte[] TelemetryPayload = { 0x95, 0x09, 0x08, 0x14 };

    [Fact]
    public async Task TelemetryFrame_IsSavedToStore_WithParsedValues()
    {
        var transport = new FakeTransport();
        var store = new FakeTelemetryStore();
        var vm = CreateViewModel(transport, store);

        transport.RaiseDataReceived(
            FrameTestHelper.BuildFrameBytes(1, (byte)MessageType.Telemetry, 7, TelemetryPayload)
        );

        await WaitUntilAsync(() => store.Records.Count > 0, TimeSpan.FromSeconds(2));

        var record = Assert.Single(store.Records);
        Assert.Equal(7, record.Sequence);
        Assert.Equal(24.53, record.Temperature, precision: 2);
        Assert.Equal(51.28, record.Humidity, precision: 2);

        vm.Dispose();
    }

    [Fact]
    public async Task NonTelemetryFrame_IsNotSavedToStore()
    {
        var transport = new FakeTransport();
        var store = new FakeTelemetryStore();
        var vm = CreateViewModel(transport, store);

        transport.RaiseDataReceived(
            FrameTestHelper.BuildFrameBytes(1, (byte)MessageType.Ping, 1, Array.Empty<byte>())
        );

        // Дожидаемся, пока кадр пройдёт конвейер (появится в логе), и лишь
        // потом проверяем, что в хранилище ничего не попало.
        await WaitUntilAsync(() => vm.LogEntries.Count > 0, TimeSpan.FromSeconds(2));

        Assert.Empty(store.Records);

        vm.Dispose();
    }

    [Fact]
    public async Task StoreFailure_IsLogged_ButPipelineKeepsProcessing()
    {
        var transport = new FakeTransport();
        var store = new FakeTelemetryStore
        {
            ThrowOnSave = new InvalidOperationException("db is broken"),
        };
        var vm = CreateViewModel(transport, store);

        transport.RaiseDataReceived(
            FrameTestHelper.BuildFrameBytes(1, (byte)MessageType.Telemetry, 1, TelemetryPayload)
        );
        transport.RaiseDataReceived(
            FrameTestHelper.BuildFrameBytes(1, (byte)MessageType.Ping, 2, Array.Empty<byte>())
        );

        // Ошибка БД не должна убить консьюмера: следующий кадр обработан
        await WaitUntilAsync(() => vm.LogEntries.Count >= 2, TimeSpan.FromSeconds(2));

        Assert.Contains(vm.LogEntries, l => l.Contains("PING"));

        vm.Dispose();
    }

    private static MainWindowViewModel CreateViewModel(
        FakeTransport transport,
        FakeTelemetryStore store
    )
    {
        return new MainWindowViewModel(
            new FrameReader(),
            new[] { transport },
            new FakeLogFileWriter(),
            new ImmediateUiDispatcher(),
            store
        );
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
