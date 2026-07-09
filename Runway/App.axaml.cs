using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Runway.Framing;
using Runway.Logging;
using Runway.Settings;
using Runway.Threading;
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

            // Path.Combine с AppContext.BaseDirectory — чтобы лог-файл не "терялся"
            // в зависимости от того, откуда запущен процесс (тот же класс проблемы,
            // что уже отмечен для settings.json в Misc/diary/2026.07.08-code-review.md).
            var logFilePath = Path.Combine(AppContext.BaseDirectory, settings.LogFilePath);
            var logFileWriter = new LogFileWriter(logFilePath);
            var uiDispatcher = new AvaloniaUiDispatcher();

            // Конструктор ViewModel уже подписывается на transport.DataReceived
            var mainViewModel = new MainWindowViewModel(
                frameReader,
                transport,
                portLister,
                logFileWriter,
                uiDispatcher,
                settings.MaxLogEntries
            );

            transport.Open(settings.PortName, settings.BaudRate);

            // Останавливаем консьюмера очереди кадров (см. MainWindowViewModel.Dispose)
            // и только потом закрываем лог-файл — порядок важен, иначе можно словить
            // запись в уже закрытый StreamWriter.
            desktop.Exit += (_, _) =>
            {
                mainViewModel.Dispose();
                logFileWriter.Dispose();
            };

            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
