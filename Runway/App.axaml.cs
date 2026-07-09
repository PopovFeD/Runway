using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
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

            // Path.Combine с AppContext.BaseDirectory — чтобы файлы не "терялись"
            // в зависимости от того, откуда запущен процесс (тот же класс проблемы,
            // что уже отмечен для settings.json в Misc/diary/2026.07.08-code-review.md).
            var logFilePath = Path.Combine(AppContext.BaseDirectory, settings.LogFilePath);
            var diagnosticsLogPath = Path.Combine(
                AppContext.BaseDirectory,
                settings.DiagnosticsLogFilePath
            );

            var logFileWriter = new LogFileWriter(logFilePath);
            var fileLoggerProvider = new FileLoggerProvider(diagnosticsLogPath);

            // Console — чтобы видеть diagnostics-события прямо в терминале при разработке.
            // Файловый провайдер — чтобы они же оставались на диске после закрытия окна.
            // Важно: НЕ "using var" — OnFrameworkInitializationCompleted возвращается
            // сразу после настройки, задолго до реального выхода из приложения. Если бы
            // loggerFactory тут же диспозился по выходу из метода, он утянул бы за собой
            // (LoggerFactory владеет добавленными провайдерами) и fileLoggerProvider —
            // diagnostics-файл закрылся бы прежде, чем успел бы принять хоть одну запись.
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(fileLoggerProvider);
                builder.AddConsole();
            });

            var frameReader = new FrameReader();
            var transport = new SerialTransport(
                loggerFactory.CreateLogger<SerialTransport>(),
                TimeSpan.FromSeconds(settings.ReconnectDelaySeconds)
            );
            var portLister = new SerialPortLister();
            var uiDispatcher = new AvaloniaUiDispatcher();

            // Конструктор ViewModel уже подписывается на transport.DataReceived
            // и transport.ConnectionStateChanged
            var mainViewModel = new MainWindowViewModel(
                frameReader,
                transport,
                portLister,
                logFileWriter,
                uiDispatcher,
                settings.MaxLogEntries
            );

            transport.Open(settings.PortName, settings.BaudRate);

            // Порядок важен: сначала останавливаем консьюмера очереди кадров
            // (см. MainWindowViewModel.Dispose), потом транспорт (иначе он может
            // успеть дёрнуть уже отписанный OnDataReceived), и только в конце —
            // закрываем файлы логов, чтобы в них не полетела запись в уже
            // закрытый StreamWriter. loggerFactory.Dispose() сам закроет
            // fileLoggerProvider — он ей передан через AddProvider, отдельно
            // диспозить его не нужно.
            desktop.Exit += (_, _) =>
            {
                mainViewModel.Dispose();
                transport.Close();
                logFileWriter.Dispose();
                loggerFactory.Dispose();
            };

            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
