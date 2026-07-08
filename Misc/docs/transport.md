# Transport

Слой, отвечающий за физическую доставку байт между приложением и микроконтроллером
(в текущей стадии — Python-эмулятором). Ничего не знает о кадрах, протоколе или
формате данных — только сырые `byte[]` в обе стороны.

Файлы: `Runway/Core/Transport/ISerialTransport.cs`, `SerialTransport.cs`,
`IPortLister.cs`, `SerialPortLister.cs`.

---

## Зона ответственности

* открыть/закрыть последовательный порт;
* сообщить о новых полученных байтах через событие;
* дать список доступных портов в системе.

Explicitly **не** входит в зону ответственности: поиск границ кадра, проверка CRC,
разбор содержимого. Всё это — уровнем выше, в `Framing` и `Protocol`.

---

## Публичный API

```csharp
public interface ISerialTransport
{
    bool IsOpen { get; }
    void Open(string portName, int baudRate);
    void Close();
    event Action<byte[]>? DataReceived;
}

public interface IPortLister
{
    List<string> GetAvailablePorts();
}
```

`SerialTransport` — единственная реализация `ISerialTransport` на данный момент,
обёртка над `System.IO.Ports.SerialPort`.

`SerialPortLister` — обёртка над `SerialPort.GetPortNames()`, кроссплатформенная
(на Windows вернёт `COM3` и т.п., на Linux — `/dev/ttyUSB0` и т.п.).

Оба интерфейса существуют в первую очередь ради DI/тестируемости — конкретные
реализации создаются вручную в `App.axaml.cs`, контейнера DI в проекте нет.

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
оборачивает разбор пакета в свой `try/catch` (см. `Runway/ViewModels/MainWindowViewModel.cs`).
Но это ответственность подписчика, а не гарантия самого `SerialTransport` —
любой новый обработчик `DataReceived` обязан помнить про это правило сам.

---

## Конфигурация

Имя порта и скорость берутся не отсюда, а из `Settings.AppSettings`
(`PortName`, `BaudRate`, значения по умолчанию `"COM6"` / `115200`), которые
грузятся через `SettingsLoader` в `App.axaml.cs` при старте и передаются в
`transport.Open(...)`. Сам `Transport`-слой про `settings.json` ничего не знает.

`MainWindowViewModel.AvailablePorts` уже вызывает `IPortLister.GetAvailablePorts()`,
но на момент написания этого документа никак не забинджен в GUI — выбор порта
из списка пока не реализован, порт жёстко фиксируется настройками при старте.

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
