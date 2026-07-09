# TODO

## Документация (Misc/docs/)

- [x] transport.md
- [x] framing.md
- [x] protocol.md
- [x] storage.md
- [x] settings.md
- [x] viewmodels.md (MainWindowViewModel, ViewLocator, связь с GUI)
- [x] logging.md — телеметрийный лог (LogFileWriter/BoundedLog) vs
      diagnostics-лог (Microsoft.Extensions.Logging/FileLoggerProvider),
      почему их два и почему не Serilog/NLog
- [ ] README.md-индекс по Misc/docs/, когда наберётся достаточно файлов

## Код — по итогам первого e2e (Misc/diary/2026.07.08.md)

Приоритет — перед подключением SQLite:

- [x] разделить read-поток и обработку через очередь
      (`System.Threading.Channels.Channel<Frame>`), иначе запись в БД
      будет тормозить чтение с порта
- [x] ограничить/очищать `LogEntries` (сейчас растёт бесконечно) —
      `BoundedLog` в GUI (капается по `MaxLogEntries`), полный лог
      всё равно пишется в файл (`LogFileWriter`, без ограничений)
- [x] переподключение при разрыве порта + индикация разрыва в GUI —
      `SerialTransport.RunLoop` переоткрывает порт с паузой
      (`ReconnectDelaySeconds`), `ConnectionStateChanged` прокинуто
      в `MainWindowViewModel.ConnectionStatus` и забиндено в GUI
- [x] забиндить выбор порта в `MainWindow.axaml` вместо жёсткого
      порта из `settings.json` — сделано шире исходной задачи: введён
      общий `ITransport` (Serial — рабочий, `WifiTransport` для ESP32 —
      заглушка), выбор транспорта и точки подключения в GUI, подключение
      по кнопке; `IPortLister`/`SerialPortLister` упразднены —
      перечисление точек подключения теперь обязанность самого транспорта
      (`GetAvailableEndpoints`)
- [x] слой хранения: SQLite (`ITelemetryStore`/`SqliteTelemetryStore`,
      Microsoft.Data.Sqlite 10.0.0 — версия проставлена без NuGet,
      проверить при restore); телеметрия пишется из ProcessFramesAsync
- [ ] `LogFileWriter` (`runway.log`) теперь дублирует телеметрию из БД —
      решить, когда БД станет основным хранилищем, не пора ли его убрать
- [ ] `PacketParser.Parse` — уйти от `object` к типизированной иерархии
      (`abstract record Packet` + подтипы), когда появится `Command`

Не срочно, но держать в уме:

- [ ] зафиксировать формат протокола (magic, коды типов) одним файлом,
      общим для Python и C#, чтобы не расходились молча повторно
      (см. баг с `TYPE_SENSOR`/magic из 2026.07.08.md)
- [ ] `FrameReader._buffer` на `List<byte>` — пересмотреть, если трафик
      станет высокочастотным (сейчас O(n) на `RemoveRange`)
- [ ] сам цикл переподключения в `SerialTransport` (открытие/переоткрытие
      реального `SerialPort`) не покрыт автотестами — только реакция
      `MainWindowViewModel` на уже случившееся событие. Если понадобится
      закрыть и это — придётся вводить абстракцию над `SerialPort`
      (`ISerialPortFactory` или похожую) ради фейка; com0com для этого
      не подходит (виртуальная пара портов не эмулирует физический разрыв)
- [ ] версии пакетов `Microsoft.Extensions.Logging`/`.Console` в
      `Runway.csproj` (`10.0.0`) проставлены по аналогии с
      `System.IO.Ports` не глядя в NuGet (сети не было) — проверить
      при первом `dotnet restore` и поправить, если предложит другую

## Общие вопросы (перенесено из старого TODO)

- [ ] Dependency injection — стоит ли вводить контейнер, когда
      ручной DI в `App.axaml.cs` станет неудобным
