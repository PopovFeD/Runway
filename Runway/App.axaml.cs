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
            var diagnosticsLogPath = Path.Combine(
                AppContext.BaseDirectory,
                settings.DiagnosticsLogFilePath
            );

            // БД создаётся до логгеров: StoreLoggerProvider пишет события в неё
            var appStore = new SqliteAppStore(
                Path.Combine(AppContext.BaseDirectory, settings.DatabaseFilePath)
            );
            var sessionTracker = new SessionTracker();

            // Получатели diagnostics-событий: БД (основной, фильтруемый в GUI),
            // файл ("лог последней надежды" на случай повреждённой/недоступной БД —
            // выключается настройкой DiagnosticsFileEnabled), консоль (при
            // разработке). Важно: НЕ "using var" — LoggerFactory владеет
            // добавленными провайдерами и закрывается в desktop.Exit.
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new StoreLoggerProvider(appStore, sessionTracker));
                if (settings.DiagnosticsFileEnabled)
                {
                    builder.AddProvider(new FileLoggerProvider(diagnosticsLogPath));
                }
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
            var mainViewModel = new MainWindowViewModel(
                frameReader,
                transports,
                uiDispatcher,
                appStore,
                sessionTracker,
                settings.MaxLogEntries,
                initialEndpoint: settings.PortName,
                hiddenSections: settings.HiddenDashboardSections
            );

            // Галочки разделов Дашборда (вкладка "Настройки") сохраняются
            // в settings.json при каждом переключении
            foreach (var section in mainViewModel.Sections)
            {
                section.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ProtocolSectionViewModel.IsVisible))
                    {
                        settings.HiddenDashboardSections = mainViewModel
                            .Sections.Where(s => !s.IsVisible)
                            .Select(s => s.ProtocolKey)
                            .ToList();
                        SettingsLoader.Save(settings);
                    }
                };
            }

            // Порядок важен: консьюмер кадров → транспорты (они ещё могут писать
            // в логи при закрытии) → фабрика логгеров (закроет fileLoggerProvider
            // и StoreLoggerProvider) → в самом конце БД, чтобы StoreLoggerProvider
            // не писал в уже закрытое соединение.
            desktop.Exit += (_, _) =>
            {
                mainViewModel.Dispose();
                foreach (var transport in transports)
                {
                    transport.Close();
                }
                loggerFactory.Dispose();
                appStore.Dispose();
            };

            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
