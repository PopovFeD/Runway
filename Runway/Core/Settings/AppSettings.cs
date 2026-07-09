namespace Runway.Settings;

public class AppSettings
{
    public string PortName { get; set; } = "COM6";
    public int BaudRate { get; set; } = 115200;

    // Путь к файлу полного лога (относительно каталога сборки — см. AvaloniaUiDispatcher
    // и App.axaml.cs, где путь комбинируется с AppContext.BaseDirectory, чтобы не
    // повторить баг с относительным путём settings.json из code-review 2026.07.08).
    public string LogFilePath { get; set; } = "runway.log";

    // Сколько последних строк держать в GUI (LogEntries). Полный лог всегда пишется
    // в LogFilePath целиком — ограничение касается только памяти процесса.
    public int MaxLogEntries { get; set; } = 500;
}
