using Runway.Framing;
using Runway.Tests.Support;
using Runway.Transport;
using Runway.ViewModels;
using Xunit;

namespace Runway.Tests.ViewModels;

// Тесты на выбор транспорта/точки подключения и команды Подключить/Отключить —
// то есть на ту часть MainWindowViewModel, которая появилась вместе с выбором
// порта из GUI (вместо жёсткого porta из settings.json при старте).
public class MainWindowViewModelConnectionCommandsTests
{
    [Fact]
    public void Ctor_PreselectsInitialEndpoint_WhenItExists()
    {
        var transport = new FakeTransport("COM3", "COM6");

        var vm = CreateViewModel(new[] { transport }, initialEndpoint: "COM6");

        Assert.Equal("COM6", vm.SelectedEndpoint);

        vm.Dispose();
    }

    [Fact]
    public void Ctor_FallsBackToFirstEndpoint_WhenInitialEndpointIsMissing()
    {
        var transport = new FakeTransport("COM3", "COM4");

        // Порт из настроек может отсутствовать в системе (другая машина,
        // переткнутый адаптер) — тогда берём первый доступный, а не ломаемся.
        var vm = CreateViewModel(new[] { transport }, initialEndpoint: "COM6");

        Assert.Equal("COM3", vm.SelectedEndpoint);

        vm.Dispose();
    }

    [Fact]
    public void ConnectCommand_IsDisabled_WhenTransportHasNoEndpoints()
    {
        // Поведение WiFi-заглушки: точек подключения нет — подключаться не к чему
        var transport = new FakeTransport();

        var vm = CreateViewModel(new[] { transport });

        Assert.Null(vm.SelectedEndpoint);
        Assert.False(vm.ConnectCommand.CanExecute(null));

        vm.Dispose();
    }

    [Fact]
    public void Connect_OpensSelectedTransport_WithSelectedEndpoint()
    {
        var transport = new FakeTransport("COM3", "COM6");
        var vm = CreateViewModel(new[] { transport });
        vm.SelectedEndpoint = "COM6";

        vm.ConnectCommand.Execute(null);

        Assert.True(transport.IsOpen);
        Assert.Equal("COM6", transport.LastOpenedEndpoint);

        vm.Dispose();
    }

    [Fact]
    public void Disconnect_ClosesActiveTransport_AndReportsDisconnected()
    {
        var transport = new FakeTransport("COM3");
        var vm = CreateViewModel(new[] { transport });
        vm.ConnectCommand.Execute(null);
        transport.RaiseConnectionStateChanged(ConnectionState.Connected);

        vm.DisconnectCommand.Execute(null);

        Assert.False(transport.IsOpen);
        Assert.Equal(1, transport.CloseCallCount);
        // SerialTransport.Close() сам события не поднимает (штатная остановка,
        // не разрыв) — статус обязана выставить сама ViewModel.
        Assert.Equal(ConnectionState.Disconnected, vm.ConnectionStatus);

        vm.Dispose();
    }

    [Fact]
    public void ConnectCommand_DisabledWhileConnected_EnabledAgainAfterDisconnect()
    {
        var transport = new FakeTransport("COM3");
        var vm = CreateViewModel(new[] { transport });

        Assert.True(vm.ConnectCommand.CanExecute(null));

        vm.ConnectCommand.Execute(null);
        transport.RaiseConnectionStateChanged(ConnectionState.Connected);
        Assert.False(vm.ConnectCommand.CanExecute(null));
        Assert.True(vm.DisconnectCommand.CanExecute(null));

        vm.DisconnectCommand.Execute(null);
        Assert.True(vm.ConnectCommand.CanExecute(null));
        Assert.False(vm.DisconnectCommand.CanExecute(null));

        vm.Dispose();
    }

    [Fact]
    public void DisconnectCommand_IsEnabledWhileReconnecting_ToCancelRetries()
    {
        var transport = new FakeTransport("COM3");
        var vm = CreateViewModel(new[] { transport });
        vm.ConnectCommand.Execute(null);

        // Порт не открылся / разорвался — транспорт ушёл в цикл переподключения.
        // "Отключить" в этом состоянии — единственный способ его остановить.
        transport.RaiseConnectionStateChanged(ConnectionState.Reconnecting);

        Assert.True(vm.DisconnectCommand.CanExecute(null));
        Assert.False(vm.ConnectCommand.CanExecute(null));

        vm.Dispose();
    }

    [Fact]
    public void SelectedTransportChange_RefreshesEndpointList()
    {
        var serial = new FakeTransport("COM3", "COM6") { DisplayName = "Serial" };
        var wifi = new FakeTransport("192.168.1.42:3333") { DisplayName = "WiFi" };
        var vm = CreateViewModel(new[] { serial, wifi });

        Assert.Equal(new[] { "COM3", "COM6" }, vm.AvailableEndpoints);

        vm.SelectedTransport = wifi;

        Assert.Equal(new[] { "192.168.1.42:3333" }, vm.AvailableEndpoints);
        Assert.Equal("192.168.1.42:3333", vm.SelectedEndpoint);

        vm.Dispose();
    }

    [Fact]
    public void RefreshEndpoints_KeepsSelection_WhenEndpointStillExists()
    {
        var transport = new FakeTransport("COM3", "COM6");
        var vm = CreateViewModel(new[] { transport });
        vm.SelectedEndpoint = "COM6";

        vm.RefreshEndpointsCommand.Execute(null);

        Assert.Equal("COM6", vm.SelectedEndpoint);

        vm.Dispose();
    }

    [Fact]
    public void StatusText_ShowsActiveTransportAndEndpoint_NotCurrentSelection()
    {
        var serial = new FakeTransport("COM3") { DisplayName = "Serial" };
        var wifi = new FakeTransport("192.168.1.42:3333") { DisplayName = "WiFi" };
        var vm = CreateViewModel(new[] { serial, wifi });

        Assert.Equal("Отключено", vm.StatusText);

        vm.ConnectCommand.Execute(null);
        serial.RaiseConnectionStateChanged(ConnectionState.Connected);
        Assert.Equal("Подключено: Serial · COM3", vm.StatusText);

        // Пользователь листает список — реальное соединение не меняется,
        // и индикатор продолжает показывать его, а не текущий выбор.
        vm.SelectedTransport = wifi;
        Assert.Equal("Подключено: Serial · COM3", vm.StatusText);

        vm.DisconnectCommand.Execute(null);
        Assert.Equal("Отключено", vm.StatusText);

        vm.Dispose();
    }

    [Fact]
    public void ToggleConnection_ConnectsThenDisconnects_ByActualState()
    {
        var transport = new FakeTransport("COM3");
        var vm = CreateViewModel(new[] { transport });

        Assert.Equal("Подключить", vm.ToggleConnectionText);

        vm.ToggleConnectionCommand.Execute(null);
        Assert.True(transport.IsOpen);
        Assert.Equal("Отключить", vm.ToggleConnectionText);

        vm.ToggleConnectionCommand.Execute(null);
        Assert.False(transport.IsOpen);
        Assert.Equal("Подключить", vm.ToggleConnectionText);
        Assert.Equal(ConnectionState.Disconnected, vm.ConnectionStatus);

        vm.Dispose();
    }

    [Fact]
    public void ToggleConnection_IsDisabled_WithoutEndpoint()
    {
        var transport = new FakeTransport();
        var vm = CreateViewModel(new[] { transport });

        Assert.False(vm.ToggleConnectionCommand.CanExecute(null));

        vm.Dispose();
    }

    private static MainWindowViewModel CreateViewModel(
        FakeTransport[] transports,
        string? initialEndpoint = null
    )
    {
        return new MainWindowViewModel(
            new FrameReader(),
            transports,
            new FakeLogFileWriter(),
            new ImmediateUiDispatcher(),
            initialEndpoint: initialEndpoint
        );
    }
}
