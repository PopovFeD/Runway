# Logging

Два независимых потока логов — это осознанно, у них разные потребители:

| | `runway.log` (данные) | `runway.diagnostics.log` (события) |
|---|---|---|
| Что | строки принятой телеметрии | разрыв порта, ретраи, ошибки открытия |
| Кто пишет | `LogFileWriter : ILogFileWriter` | `Microsoft.Extensions.Logging` → `FileLoggerProvider` |
| Зачем читать | греп/анализ данных | отладка поведения приложения |

Файлы: `Runway/Core/Logging/*` (`AppendOnlyFile`, `LogFileWriter`,
`FileLoggerProvider`, `BoundedLog`).

* `AppendOnlyFile` — общая обёртка над `StreamWriter` (создание каталога,
  `AutoFlush`, `lock` — diagnostics пишется из нескольких потоков).
* `FileLoggerProvider`/`FileLogger` — минимальный `ILoggerProvider` (~70 строк):
  уровни + категории + файл, без ротации. Serilog/NLog — сознательно нет:
  оверинжиниринг для текущих нужд. `AddConsole()` в `App` — дубль diagnostics
  в терминал при разработке.
* `BoundedLog` — кап GUI-списка `LogEntries` (`MaxLogEntries`); полный лог
  всё равно на диске. Отдельный класс ради тестов без Avalonia.
* Таймстампы и числа — `InvariantCulture` везде (был реальный баг с русской
  локалью: `24,53` и другой разделитель времени).

Ревизия 2026.07.09 (Claude Code): конструкция проверена на переусложнение —
вердикт «оставить как есть»: два файла оправданы разными потребителями,
`AppendOnlyFile` устраняет дублирование, свой провайдер дешевле зависимости.
Известное будущее: когда SQLite станет основным хранилищем телеметрии,
`LogFileWriter` — кандидат на удаление (см. TODO).

Готча владения: `LoggerFactory` владеет переданными провайдерами — не
диспозить `fileLoggerProvider` отдельно и не делать `using var loggerFactory`
(закрыл бы файл сразу после старта); всё закрывается в `desktop.Exit`.
