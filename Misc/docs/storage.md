# Storage

Слой хранения телеметрии. Принятые точки (`TelemetryPacket` + sequence +
момент приёма) пишутся в локальный SQLite-файл.

Файлы: `Runway/Core/Storage/ITelemetryStore.cs` (интерфейс + `TelemetryRecord`),
`SqliteTelemetryStore.cs`.

---

## Зона ответственности

* сохранить принятую точку телеметрии (`Save`);
* прочитать всё сохранённое (`ReadAll` — пока только для тестов, войдёт в
  интерфейс, когда появится просмотр истории в GUI).

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

`Save` вызывается только из консьюмера очереди кадров
(`MainWindowViewModel.ProcessFramesAsync`) — ровно тот сценарий, ради которого
заводился `Channel<Frame>`: запись в БД не тормозит read-поток порта.
Одно соединение без блокировок; появится второй писатель — нужен lock
(см. аналогию в `AppendOnlyFile`).

Ошибка записи ловится отдельно от ошибок разбора (`StoreError` в `runway.log`
vs `ParseError`) и не останавливает конвейер.

## Тестирование

* `SqliteTelemetryStoreTests` — roundtrip через настоящий файл во временном
  каталоге (SQLite не требует сервера, так что это всё ещё быстрые тесты).
* `MainWindowViewModelTelemetryStoreTests` + `FakeTelemetryStore` — что именно
  ViewModel кладёт в хранилище и что ошибка БД не убивает консьюмера.

## Связь с другими модулями

* **ViewModels** — единственный писатель (`ProcessFramesAsync`).
* **Settings** — путь к файлу БД.
* `LogFileWriter` (`runway.log`) продолжает писать те же данные строками —
  осознанное дублирование на переходный период, кандидат на удаление,
  когда БД станет основным хранилищем (см. TODO).
