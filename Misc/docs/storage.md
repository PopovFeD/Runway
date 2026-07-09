# Storage

Слой хранения данных приложения в локальном SQLite: телеметрия, события
уровня приложения и сессии подключения (реализация решения из
`storage-and-logs-decision.md`, шаги 1–2).

Файлы: `Runway/Core/Storage/IAppStore.cs` (интерфейс + record'ы
`TelemetryRecord`/`EventRecord`), `SqliteAppStore.cs`.

Таблицы: `sessions(id, started_at, ended_at, transport, endpoint)`,
`telemetry(..., session_id)`, `events(timestamp, level, category, message,
session_id)`. Сессия = период от «Подключить» до «Отключить»; переподключения
внутри разрыва сессию не дробят — это события внутри неё. База от старой
версии мигрируется на лету (ALTER TABLE добавляет session_id).

Вкладка «Логи» в GUI читает `events` по фильтрам (уровень / текущая сессия)
по кнопке «Обновить»; живой нефильтрованный поток — на Дашборде.

---

## Зона ответственности

* сессии: `BeginSession`/`EndSession`;
* сохранить телеметрию (`SaveTelemetry`) и события (`SaveEvent`);
* фильтруемое чтение событий (`ReadEvents(level, sessionId)`);
* `ReadAllTelemetry` — пока вне интерфейса (тесты; пригодится экспорту).

Explicitly **не** входит: решение, *что* сохранять (это `MainWindowViewModel`),
и диагностические логи (это `Logging`).

---

## Как устроено

* `Microsoft.Data.Sqlite`, без EF Core — одна таблица и два запроса не
  оправдывают ORM.
* Таблица `telemetry(id, timestamp TEXT, sequence, temperature REAL, humidity REAL)`,
  `CREATE TABLE IF NOT EXISTS` в конструкторе — база переживает перезапуск.
* Timestamp — момент приёма на стороне приложения (устройство часов не шлёт),
  формат "O" (round-trip) + InvariantCulture, как в логах.
* Путь — `AppSettings.DatabaseFilePath` (`runway.db`), комбинируется с
  `AppContext.BaseDirectory` в `App.axaml.cs`, как и логи.

## Потоки

Писателей два: консьюмер очереди кадров (телеметрия, ошибки разбора) и
UI-поток (сессии, события подключения, чтение для вкладки «Логи») — одно
соединение под общим `lock` (предсказанный сценарий и наступил). Текущий id
сессии во ViewModel читается/пишется через `Volatile` — его трогают оба потока.

Ошибка записи ловится отдельно от ошибок разбора (`StoreError` в `runway.log`
vs `ParseError`) и не останавливает конвейер.

## Тестирование

* `SqliteAppStoreTests` — roundtrip через настоящий файл во временном
  каталоге (SQLite не требует сервера, так что это всё ещё быстрые тесты):
  телеметрия, сессии, фильтры событий, миграция/переоткрытие базы.
* `MainWindowViewModelTelemetryStoreTests` + `FakeAppStore` — что именно
  ViewModel кладёт в хранилище и что ошибка БД не убивает консьюмера.

## Связь с другими модулями

* **ViewModels** — писатель (телеметрия/ошибки из `ProcessFramesAsync`,
  сессии/события из команд подключения) и читатель (вкладка «Логи»).
* **Settings** — путь к файлу БД.
* `LogFileWriter` (`runway.log`) продолжает писать те же данные строками —
  осознанное дублирование на переходный период, кандидат на удаление,
  когда БД станет основным хранилищем (см. TODO).
