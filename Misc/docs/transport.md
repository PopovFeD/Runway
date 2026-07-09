# Transport

Слой, отвечающий за физическую доставку байт между приложением и микроконтроллером
(в текущей стадии — Python-эмулятором). Ничего не знает о кадрах, протоколе или
формате данных — только сырые `byte[]` в обе стороны.

Файлы: `Runway/Core/Transport/ITransport.cs`, `SerialTransport.cs`,
`WifiTransport.cs`, `ConnectionState.cs`.

---

## Зона ответственности

* открыть/закрыть канал связи (COM-порт сейчас, TCP-сокет для ESP32 — в будущем);
* сообщить о новых полученных байтах через событие;
* перечислить доступные точки подключения своего типа (список COM-портов в
  системе; для WiFi в будущем — обнаруженные устройства).

Explicitly **не** входит в зону ответственности: поиск границ кадра, проверка CRC,
разбор содержимого. Всё это — уровнем выше, в `Framing` и `Protocol`.

---

## Публичный API

```csharp
public interface ITransport
{
    string DisplayName { get; }                     // имя для ComboBox в GUI
    bool IsOpen { get; }
    IReadOnlyList<string> GetAvailableEndpoints();  // "COM6" / "/dev/ttyUSB0" / (будущее) "ip:port"
    void Open(string endpoint);
    void Close();
    event Action<byte[]>? DataReceived;
    event Action<ConnectionState>? ConnectionStateChanged;
}
```

**Endpoint** — строка-адрес конкретной точки подключения в терминах транспорта:
у Serial это имя порта, у WiFi будет `"192.168.1.42:3333"`. Конфигурация,
специфичная для типа транспорта (например, baud rate у Serial), в интерфейс
намеренно не входит — она задаётся в конструкторе реализации из `AppSettings`.
Пользователь в GUI выбирает *точку подключения*, а не скорость порта.

Реализации:

* `SerialTransport` — рабочая, обёртка над `System.IO.Ports.SerialPort`.
  Перечисление точек — `SerialPort.GetPortNames()`, кроссплатформенно
  (на Windows `COM3` и т.п., на Linux — `/dev/ttyUSB0` и т.п.).
* `WifiTransport` — **заглушка** под будущее подключение к ESP32 по WiFi.
  `GetAvailableEndpoints()` возвращает пустой список, поэтому из GUI её
  `Open()` вызвать невозможно (кнопка "Подключить" выключена без выбранной
  точки); прямой вызов `Open()` бросает `NotSupportedException`. Существует,
  чтобы ViewModel и GUI уже сейчас работали со списком транспортов.

Ранее существовавшие `ISerialTransport`/`IPortLister`/`SerialPortLister`
упразднены: перечисление точек подключения — естественная обязанность самого
транспорта (у каждого типа канала свой способ), отдельный "листер" был бы
лишней сущностью при появлении второго транспорта.

Конкретные реализации создаются вручную в `App.axaml.cs` (список
`ITransport[]`), контейнера DI в проекте нет.

---

## Модель потоков — важно

`SerialTransport.Open` запускает отдельный `Thread` (`_readThread`), который в
цикле блокирующе читает `_port.Read(...)`. Событие `DataReceived` вызывается
**из этого фонового потока**, не из UI-потока.

Причина, почему не используется штатное событие `SerialPort.DataReceived`:
оно ненадёжно себя ведёт с виртуальными портами com0com (не всегда стреляет).
Поэтому выбран собственный блокирующий read-loop с `ReadTimeout = 500`мс —
таймаут нужен не для логики, а чтобы поток не завис навечно в `Read()` и мог
периодически проверять флаг `_keepReading` (используется при `Close()`).

### Готча: `catch (Exception) { break; }` в `ReadLoop`

```csharp
catch (TimeoutException) { /* норма, нет данных */ }
catch (Exception) { break; } // порт закрылся/сломался — выходим из цикла
```

Этот `catch` ловит **любое** исключение, вылетевшее из `_port.Read(...)`,
но также — любое исключение, вылетевшее из подписчиков `DataReceived?.Invoke(...)`,
поскольку вызов происходит внутри того же `try`. Если код выше по цепочке
(`FrameReader`, `PacketParser`, что угодно в обработчике события) выбросит
необработанное исключение — весь read-поток тихо остановится, без явной ошибки
в интерфейсе.

На сегодня это компенсируется тем, что `MainWindowViewModel.OnDataReceived`
делает минимум (только `FrameReader.Append` — операции в памяти) и сразу
передаёт кадры через `Channel<Frame>` в отдельную задачу `ProcessFramesAsync`,
где разбор пакета обёрнут в `try/catch` (см. `Runway/ViewModels/MainWindowViewModel.cs`).
Но это ответственность подписчика, а не гарантия самого `SerialTransport` —
любой новый обработчик `DataReceived` обязан помнить про это правило сам.

---

## Конфигурация

Скорость порта (`BaudRate`) берётся из `Settings.AppSettings` и передаётся в
конструктор `SerialTransport` в `App.axaml.cs`. Сам `Transport`-слой про
`settings.json` ничего не знает.

Автоподключения при старте больше нет: пользователь выбирает транспорт и точку
подключения в GUI (два `ComboBox` + кнопки Подключить/Отключить/Обновить в
`MainWindow.axaml`, команды — в `MainWindowViewModel`). `AppSettings.PortName`
теперь означает лишь *предвыбранный* порт в списке при старте (если он
присутствует в системе), а не порт, к которому приложение подключается само.

---

## Тестирование

Юнит-тестов на сам `SerialTransport` нет (нечего тестировать без реального порта).
Есть интеграционные тесты — `Runway.Tests/SerialTransportIntegrationTests.cs`,
помеченные `[Trait("Category", "Integration")]`:

```text
dotnet test --filter Category!=Integration   # обычный прогон, без реальных портов
dotnet test --filter Category=Integration     # только эти, требуют com0com
```

Требуют настроенной пары виртуальных COM-портов (com0com) на машине, где
запускается `dotnet test`. Имена портов задаются через переменные окружения
`RUNWAY_TEST_PORT_A` / `RUNWAY_TEST_PORT_B` (по умолчанию `COM6`/`COM4`,
совпадает со схемой `Python → COM4 ↔ COM6 → C#` из дневника разработки).
Тесты пишут в `PortB` сырым `SerialPort` из BCL (эмулируя устройство) и проверяют,
что `SerialTransport` + `FrameReader` вместе корректно собирают кадр — в том
числе при разбиении на две записи с паузой (`DataReceived_ParsesFrame_WhenFrameArrivesInTwoWrites`).

---

## Связь с другими модулями

* **Framing** — потребитель `DataReceived`, превращает поток байт в `Frame`.
* **Settings** — источник параметров для `Open(...)`.
* **ViewModels** — текущий (единственный) подписчик `DataReceived` в реальном
  приложении; там же живёт временная защита от гочи с `catch (Exception) { break; }`.
