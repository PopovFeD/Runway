# Logging

После переезда истории в SQLite (см. `storage-and-logs-decision.md`, все
4 шага выполнены) картина такая:

* **Основной журнал приложения — БД** (таблица `events`): события ViewModel
  пишутся напрямую (`SaveEventQuietly`), diagnostics-события транспорта —
  через мост `StoreLoggerProvider : ILoggerProvider` (уровни M.E.L сводятся
  к Info/Warning/Error, категория укорачивается до имени класса, session_id
  берётся из общего `SessionTracker`). Читается во вкладке «Логи».
* **`runway.diagnostics.log` — "лог последней надежды"**: `FileLoggerProvider`
  остаётся вторым провайдером и дублирует diagnostics-события в файл — на
  случай, когда БД недоступна (не открылась, диск). Это единственный
  текстовый лог; `runway.log`/`LogFileWriter` упразднены.
* `AddConsole()` — дубль diagnostics в терминал при разработке.

Файлы: `Runway/Core/Logging/*` (`AppendOnlyFile`, `FileLoggerProvider`,
`StoreLoggerProvider`, `BoundedLog`).

* `AppendOnlyFile` — обёртка над `StreamWriter` (каталог, `AutoFlush`, `lock`).
* `BoundedLog` — кап живого вывода GUI (`MaxLogEntries`); история — в БД.
* Логгеры не имеют права бросать: `StoreLogger` глотает ошибки БД (след
  останется в файле), Serilog/NLog по-прежнему сознательно не используются.
* Таймстампы и числа — `InvariantCulture` везде (был реальный баг с русской
  локалью: `24,53` и другой разделитель времени).

Готча владения: `LoggerFactory` владеет переданными провайдерами — не
диспозить их отдельно и не делать `using var loggerFactory` (закрыл бы файл
сразу после старта). Порядок в `desktop.Exit`: транспорты → loggerFactory →
БД последней, чтобы StoreLoggerProvider не писал в закрытое соединение.
