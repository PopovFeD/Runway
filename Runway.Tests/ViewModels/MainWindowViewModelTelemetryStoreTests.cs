using System.Diagnostics;
using Runway.Framing;
using Runway.Protocol;
using Runway.Tests.Support;
using Runway.ViewModels;
using Xunit;

namespace Runway.Tests.ViewModels;

// Тесты на запись телеметрии/событий/сессий в IAppStore из ViewModel.
public class MainWindowViewModelTelemetryStoreTests
{
    // Payload как в PacketParserTests: T=24.53°C, H=51.28%
    private static readonly byte[] TelemetryPayload = { 0x95, 0x09, 0x08, 0x14 };

    [Fact]
    public async Task TelemetryFrame_IsSavedToStore_WithParsedValues()
    {
        var transport = new FakeTransport();
        var store = new FakeAppStore();
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
        var store = new FakeAppStore();
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
        var store = new FakeAppStore
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

    [Fact]
    public async Task Connect_BeginsSession_AndTelemetryCarriesItsId()
    {
        var transport = new FakeTransport("COM3");
        var store = new FakeAppStore();
        var vm = CreateViewModel(transport, store);

        vm.ConnectCommand.Execute(null);
        transport.RaiseDataReceived(
            FrameTestHelper.BuildFrameBytes(1, (byte)MessageType.Telemetry, 5, TelemetryPayload)
        );
        await WaitUntilAsync(() => store.Records.Count > 0, TimeSpan.FromSeconds(2));

        var session = Assert.Single(store.StartedSessions);
        Assert.Equal("COM3", session.Endpoint);
        Assert.Equal(session.Id, Assert.Single(store.Records).SessionId);

        vm.DisconnectCommand.Execute(null);
        Assert.Equal(session.Id, Assert.Single(store.EndedSessions));

        vm.Dispose();
    }

    [Fact]
    public void ConnectionEvents_AreSaved_AndFilterableViaRefreshLogs()
    {
        var transport = new FakeTransport("COM3");
        var store = new FakeAppStore();
        var vm = CreateViewModel(transport, store);

        vm.ConnectCommand.Execute(null);
        transport.RaiseConnectionStateChanged(Runway.Transport.ConnectionState.Connected);
        transport.RaiseConnectionStateChanged(Runway.Transport.ConnectionState.Reconnecting);

        // Info "Подключение", Info "Состояние: Connected", Warning "Reconnecting"
        Assert.Equal(3, store.Events.Count);
        Assert.All(store.Events, e => Assert.NotNull(e.SessionId));

        // Оставляем только галочку Warning
        vm.ShowInfo = false;
        vm.ShowError = false;
        vm.RefreshLogsCommand.Execute(null);

        var line = Assert.Single(vm.FilteredLogEvents);
        Assert.Contains("Reconnecting", line);
        Assert.Contains("[Warning]", line);

        // Все галочки сняты — показывать нечего
        vm.ShowWarning = false;
        vm.RefreshLogsCommand.Execute(null);
        Assert.Empty(vm.FilteredLogEvents);

        vm.Dispose();
    }

    [Fact]
    public void ExportLogs_WritesCsv_UsingSameCheckboxFilters()
    {
        string exportDir = Path.Combine(
            Path.GetTempPath(),
            $"runway-export-{Guid.NewGuid():N}"
        );
        var transport = new FakeTransport("COM3");
        var store = new FakeAppStore();
        var vm = new MainWindowViewModel(
            new FrameReader(),
            new[] { transport },
            new ImmediateUiDispatcher(),
            store,
            exportDirectory: exportDir
        );

        try
        {
            vm.ConnectCommand.Execute(null); // Info "Подключение"
            transport.RaiseConnectionStateChanged(
                Runway.Transport.ConnectionState.Reconnecting
            ); // Warning

            // Экспортируем только Warning — как будто стоит одна галочка
            vm.ShowInfo = false;
            vm.ShowError = false;
            vm.ExportLogsCommand.Execute(null);

            Assert.StartsWith("Экспортировано 1 записей", vm.ExportStatusText);

            string csvPath = Assert.Single(Directory.GetFiles(exportDir, "*.csv"));
            string[] lines = File.ReadAllLines(csvPath);
            Assert.Equal("timestamp;level;category;message;session_id", lines[0]);
            Assert.Equal(2, lines.Length); // заголовок + одна запись
            Assert.Contains(";Warning;", lines[1]);
            Assert.Contains("Reconnecting", lines[1]);

            vm.Dispose();
        }
        finally
        {
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DashboardTiles_ShowLastValues_PerPacketType()
    {
        var transport = new FakeTransport();
        var store = new FakeAppStore();
        var vm = CreateViewModel(transport, store);

        Assert.Equal("—", vm.LastTemperatureText);

        transport.RaiseDataReceived(
            FrameTestHelper.BuildFrameBytes(1, (byte)MessageType.Telemetry, 1, TelemetryPayload)
        );
        var envFrame = PacketBuilder.CreateEnvironment(1, 2, 1013.25, 347.5);
        transport.RaiseDataReceived(
            FrameTestHelper.BuildFrameBytes(1, (byte)MessageType.Environment, 2, envFrame.Payload)
        );

        await WaitUntilAsync(() => vm.LastLightText != "—", TimeSpan.FromSeconds(2));

        Assert.Equal("24.53 °C", vm.LastTemperatureText);
        Assert.Equal("51.28 %", vm.LastHumidityText);
        Assert.Equal("1013.25 hPa", vm.LastPressureText);
        Assert.Equal("347.50 lx", vm.LastLightText);

        // Строка живого вывода — в лог-стиле, с меткой времени HH:mm:ss.fff
        Assert.All(vm.LogEntries, l => Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3}  ", l));

        vm.Dispose();
    }

    private static MainWindowViewModel CreateViewModel(FakeTransport transport, FakeAppStore store)
    {
        return new MainWindowViewModel(
            new FrameReader(),
            new[] { transport },
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
