namespace Runway.Settings;

public class AppSettings
{
    // Порт, который будет предвыбран в списке в GUI при старте (если он есть
    // в системе). Автоподключения по нему больше нет — подключение только по
    // кнопке "Подключить" (см. MainWindowViewModel.ConnectCommand).
    public string PortName { get; set; } = "COM6";

    public int BaudRate { get; set; } = 115200;

    // Путь к файлу полного лога (относительно каталога сборки — см. AvaloniaUiDispatcher
    // и App.axaml.cs, где путь комбинируется с AppContext.BaseDirectory, чтобы не
    // повторить баг с относительным путём settings.json из code-review 2026.07.08).
    public string LogFilePath { get; set; } = "runway.log";

    // Сколько последних строк держать в GUI (LogEntries). Полный лог всегда пишется
    // в LogFilePath целиком — ограничение касается только памяти процесса.
    public int MaxLogEntries { get; set; } = 500;

    // Путь к файлу diagnostics-лога (Microsoft.Extensions.Logging, см. FileLoggerProvider).
    // Отдельно от LogFilePath: там данные телеметрии, тут — события уровня приложения
    // (разрыв порта, попытки переподключения), их принципиально не стоит мешать в одном файле.
    public string DiagnosticsLogFilePath { get; set; } = "runway.diagnostics.log";

    // Пауза перед очередной попыткой переподключиться после разрыва порта.
    public int ReconnectDelaySeconds { get; set; } = 2;

    // Путь к SQLite-файлу с телеметрией (относительно каталога сборки, как и логи).
    public string DatabaseFilePath { get; set; } = "runway.db";
}
