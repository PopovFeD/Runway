using Runway.Framing;
using Runway.Tests.Support;
using Runway.Transport;
using Runway.ViewModels;
using Xunit;

namespace Runway.Tests.ViewModels;

// Тесты на то, как MainWindowViewModel реагирует на смену состояния подключения
// (см. ITransport.ConnectionStateChanged). Сам цикл переподключения внутри
// SerialTransport (настоящий SerialPort, попытки открыть/переоткрыть порт) этим
// намеренно не покрыт — реалистично симулировать физический разрыв порта что
// в юнит-тесте, что через com0com (виртуальная пара портов не "отваливается"
// физически) не получится без отдельной инфраструктуры. Здесь тестируется
// то, что действительно тестируется дёшево и честно: реакция ViewModel на уже
// произошедшее событие смены состояния.
//
// Каждый тест сначала реально подключается (ConnectCommand): события от
// транспорта, к которому пользователь не подключался (или уже отключился),
// ViewModel намеренно игнорирует — см. защиту в OnConnectionStateChanged.
public class MainWindowViewModelConnectionStatusTests
{
    [Fact]
    public void ConnectionStatus_DefaultsToDisconnected_BeforeAnyEventFires()
    {
        var vm = CreateConnectedViewModel(out _);

        Assert.Equal(ConnectionState.Disconnected, vm.ConnectionStatus);

        vm.Dispose();
    }

    [Theory]
    [InlineData(ConnectionState.Connected)]
    [InlineData(ConnectionState.Reconnecting)]
    [InlineData(ConnectionState.Disconnected)]
    public void ConnectionStatus_ReflectsLatestStateRaisedByTransport(ConnectionState state)
    {
        var vm = CreateConnectedViewModel(out var transport);

        transport.RaiseConnectionStateChanged(state);

        Assert.Equal(state, vm.ConnectionStatus);

        vm.Dispose();
    }

    [Fact]
    public void ConnectionStatus_FollowsFullDisconnectReconnectCycle()
    {
        var vm = CreateConnectedViewModel(out var transport);

        transport.RaiseConnectionStateChanged(ConnectionState.Connected);
        Assert.Equal(ConnectionState.Connected, vm.ConnectionStatus);

        transport.RaiseConnectionStateChanged(ConnectionState.Disconnected);
        Assert.Equal(ConnectionState.Disconnected, vm.ConnectionStatus);

        transport.RaiseConnectionStateChanged(ConnectionState.Reconnecting);
        Assert.Equal(ConnectionState.Reconnecting, vm.ConnectionStatus);

        transport.RaiseConnectionStateChanged(ConnectionState.Connected);
        Assert.Equal(ConnectionState.Connected, vm.ConnectionStatus);

        vm.Dispose();
    }

    [Fact]
    public void ConnectionStatus_IgnoresLateEvent_AfterUserDisconnected()
    {
        var vm = CreateConnectedViewModel(out var transport);
        transport.RaiseConnectionStateChanged(ConnectionState.Connected);

        vm.DisconnectCommand.Execute(null);

        // Событие, "застрявшее" в очереди UI-диспетчера на момент отключения,
        // не должно перетереть статус Disconnected, выставленный пользователем.
        transport.RaiseConnectionStateChanged(ConnectionState.Connected);

        Assert.Equal(ConnectionState.Disconnected, vm.ConnectionStatus);

        vm.Dispose();
    }

    [Fact]
    public void Dispose_UnsubscribesFromConnectionStateChanged()
    {
        var vm = CreateConnectedViewModel(out var transport);
        transport.RaiseConnectionStateChanged(ConnectionState.Connected);
        vm.Dispose();

        var exception = Record.Exception(() =>
            transport.RaiseConnectionStateChanged(ConnectionState.Disconnected)
        );

        Assert.Null(exception);
        // Событие после Dispose до VM уже не доходит — статус остаётся прежним.
        Assert.Equal(ConnectionState.Connected, vm.ConnectionStatus);
    }

    private static MainWindowViewModel CreateConnectedViewModel(out FakeTransport transport)
    {
        transport = new FakeTransport("FAKE1");
        var vm = new MainWindowViewModel(
            new FrameReader(),
            new[] { transport },
            new FakeLogFileWriter(),
            new ImmediateUiDispatcher()
        );

        // Endpoint "FAKE1" выбран автоматически (единственный в списке)
        vm.ConnectCommand.Execute(null);

        return vm;
    }
}
