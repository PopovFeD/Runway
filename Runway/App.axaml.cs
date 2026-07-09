using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Runway.Framing;
using Runway.Settings;
using Runway.Transport;
using Runway.ViewModels;
using Runway.Views;

namespace Runway;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // App.axaml.cs — теперь проще, обработка кадров переехала во ViewModel
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = SettingsLoader.Load();

            var frameReader = new FrameReader();
            var transport = new SerialTransport();
            var portLister = new SerialPortLister();

            // Конструктор ViewModel уже подписывается на transport.DataReceived
            var mainViewModel = new MainWindowViewModel(frameReader, transport, portLister);

            transport.Open(settings.PortName, settings.BaudRate);

            // Останавливаем консьюмера очереди кадров (см. MainWindowViewModel.Dispose),
            // чтобы фоновая задача разбора не осталась висеть после закрытия окна.
            desktop.Exit += (_, _) => mainViewModel.Dispose();

            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
