using Runway.Framing;
using Runway.Tests.Support;
using Runway.Transport;
using Runway.ViewModels;
using Xunit;

namespace Runway.Tests.ViewModels;

// Тесты на то, как MainWindowViewModel реагирует на смену состояния подключения
// (см. ISerialTransport.ConnectionStateChanged). Сам цикл переподключения внутри
// SerialTransport (настоящий SerialPort, попытки открыть/переоткрыть порт) этим
// намеренно не покрыт — реалистично симулировать физический разрыв порта что
// в юнит-тесте, что через com0com (виртуальная пара портов не "отваливается"
// физически) не получится без отдельной инфраструктуры. Здесь тестируется
// то, что действительно тестируется дёшево и честно: реакция ViewModel на уже
// произошедшее событие смены состояния.
public class MainWindowViewModelConnectionStatusTests
{
    [Fact]
    public void ConnectionStatus_DefaultsToDisconnected_BeforeAnyEventFires()
    {
        var vm = CreateViewModel(out _);

        Assert.Equal(ConnectionState.Disconnected, vm.ConnectionStatus);

        vm.Dispose();
    }

    [Theory]
    [InlineData(ConnectionState.Connected)]
    [InlineData(ConnectionState.Reconnecting)]
    [InlineData(ConnectionState.Disconnected)]
    public void ConnectionStatus_ReflectsLatestStateRaisedByTransport(ConnectionState state)
    {
        var vm = CreateViewModel(out var transport);

        transport.RaiseConnectionStateChanged(state);

        Assert.Equal(state, vm.ConnectionStatus);

        vm.Dispose();
    }

    [Fact]
    public void ConnectionStatus_FollowsFullDisconnectReconnectCycle()
    {
        var vm = CreateViewModel(out var transport);

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
    public void Dispose_UnsubscribesFromConnectionStateChanged()
    {
        var vm = CreateViewModel(out var transport);
        vm.Dispose();

        var exception = Record.Exception(() =>
            transport.RaiseConnectionStateChanged(ConnectionState.Connected)
        );

        Assert.Null(exception);
        // Событие после Dispose до VM уже не доходит — статус остаётся дефолтным.
        Assert.Equal(ConnectionState.Disconnected, vm.ConnectionStatus);
    }

    private static MainWindowViewModel CreateViewModel(out FakeSerialTransport transport)
    {
        transport = new FakeSerialTransport();
        return new MainWindowViewModel(
            new FrameReader(),
            transport,
            new FakePortLister(),
            new FakeLogFileWriter(),
            new ImmediateUiDispatcher()
        );
    }
}
