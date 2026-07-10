namespace Runway.Settings;

public class AppSettings
{
    // Порт, который будет предвыбран в списке в GUI при старте (если он есть
    // в системе). Автоподключения по нему больше нет — подключение только по
    // кнопке "Подключить" (см. MainWindowViewModel.ConnectCommand).
    public string PortName { get; set; } = "COM6";

    // Транспорт, предвыбранный при старте (DisplayName); вместе с PortName
    // обновляется при каждом успешном "Подключить".
    public string TransportName { get; set; } = "Serial (COM)";

    public int BaudRate { get; set; } = 115200;

    // Сколько последних строк держать в живом выводе GUI (LogEntries) —
    // ограничение касается только памяти процесса, история лежит в БД.
    public int MaxLogEntries { get; set; } = 500;

    // Путь к файлу diagnostics-лога — "лог последней надежды": основной поток
    // событий идёт в БД (StoreLoggerProvider), файл нужен на случай её недоступности.
    public string DiagnosticsLogFilePath { get; set; } = "runway.diagnostics.log";

    // Писать ли diagnostics-файл вообще. Компромисс двух мнений: с переездом
    // логов в БД файл почти бесполезен, НО SQLite может быть повреждён — файл
    // остаётся "логом последней надежды". По умолчанию включён; кому не нужен —
    // выключает здесь.
    public bool DiagnosticsFileEnabled { get; set; } = true;

    // Пауза перед очередной попыткой переподключиться после разрыва порта.
    public int ReconnectDelaySeconds { get; set; } = 2;

    // Путь к SQLite-файлу с телеметрией (относительно каталога сборки, как и логи).
    public string DatabaseFilePath { get; set; } = "runway.db";

    // Разделы Дашборда (по ProtocolKey), скрытые пользователем во вкладке
    // "Настройки". Пустой список — показывать всё.
    public List<string> HiddenDashboardSections { get; set; } = new();
}
