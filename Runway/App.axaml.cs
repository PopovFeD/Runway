using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using Runway.Framing;
using Runway.Logging;
using Runway.Settings;
using Runway.Storage;
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
            var uiDispatcher = new AvaloniaUiDispatcher();

            // Все способы подключения, которые знает приложение. Первый в списке —
            // предвыбранный в GUI. WifiTransport пока заглушка (см. его комментарий),
            // но список уже сейчас приучает остальной код не считать Serial
            // единственным вариантом.
            var transports = new ITransport[]
            {
                new SerialTransport(
                    settings.BaudRate,
                    loggerFactory.CreateLogger<SerialTransport>(),
                    TimeSpan.FromSeconds(settings.ReconnectDelaySeconds)
                ),
                new WifiTransport(loggerFactory.CreateLogger<WifiTransport>()),
            };

            // Конструктор ViewModel уже подписывается на события всех транспортов.
            // Автоподключения при старте больше нет — порт выбирается в GUI,
            // PortName из настроек лишь предвыбирается в списке, если он есть.
            var telemetryStore = new SqliteTelemetryStore(
                Path.Combine(AppContext.BaseDirectory, settings.DatabaseFilePath)
            );

            var mainViewModel = new MainWindowViewModel(
                frameReader,
                transports,
                logFileWriter,
                uiDispatcher,
                telemetryStore,
                settings.MaxLogEntries,
                initialEndpoint: settings.PortName
            );

            // Порядок важен: сначала останавливаем консьюмера очереди кадров
            // (см. MainWindowViewModel.Dispose), потом транспорты (иначе они могут
            // успеть дёрнуть уже отписанный OnDataReceived), и только в конце —
            // закрываем файлы логов, чтобы в них не полетела запись в уже
            // закрытый StreamWriter. loggerFactory.Dispose() сам закроет
            // fileLoggerProvider — он ей передан через AddProvider, отдельно
            // диспозить его не нужно.
            desktop.Exit += (_, _) =>
            {
                mainViewModel.Dispose();
                foreach (var transport in transports)
                {
                    transport.Close();
                }
                telemetryStore.Dispose();
                logFileWriter.Dispose();
                loggerFactory.Dispose();
            };

            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
