# TODO

## Документация (Misc/docs/)

- [x] transport.md
- [x] framing.md
- [x] protocol.md
- [ ] settings.md
- [ ] viewmodels.md (MainWindowViewModel, ViewLocator, связь с GUI)
- [ ] storage.md — когда появится сам слой хранения
- [ ] README.md-индекс по Misc/docs/, когда наберётся достаточно файлов

## Код — по итогам первого e2e (Misc/diary/2026.07.08.md)

Приоритет — перед подключением SQLite:

- [ ] разделить read-поток и обработку через очередь
      (`System.Threading.Channels.Channel<Frame>`), иначе запись в БД
      будет тормозить чтение с порта
- [ ] ограничить/очищать `LogEntries` (сейчас растёт бесконечно)
- [ ] переподключение при разрыве порта + индикация разрыва в GUI
- [ ] забиндить `AvailablePorts` в `MainWindow.axaml` вместо жёсткого
      порта из `settings.json`
- [ ] `PacketParser.Parse` — уйти от `object` к типизированной иерархии
      (`abstract record Packet` + подтипы), когда появится `Command`

Не срочно, но держать в уме:

- [ ] зафиксировать формат протокола (magic, коды типов) одним файлом,
      общим для Python и C#, чтобы не расходились молча повторно
      (см. баг с `TYPE_SENSOR`/magic из 2026.07.08.md)
- [ ] `FrameReader._buffer` на `List<byte>` — пересмотреть, если трафик
      станет высокочастотным (сейчас O(n) на `RemoveRange`)

## Общие вопросы (перенесено из старого TODO)

- [ ] Dependency injection — стоит ли вводить контейнер, когда
      ручной DI в `App.axaml.cs` станет неудобным
