# Protocol

Верхний слой над Framing. Задача — придать смысл содержимому уже проверенного
`Frame`: понять, что за `MessageType`, и превратить `Payload` в конкретные данные
(либо собрать `Frame` из данных в обратную сторону).

Файлы: `Runway/Core/Protocol/MessageType.cs`, `Crc16.cs`, `PacketParser.cs`,
`PacketBuilder.cs`, `TelemetryPacket.cs`.

---

## Зона ответственности

* перечислить известные типы сообщений (`MessageType`);
* разобрать `Frame.Payload` в типизированный объект (`PacketParser`);
* собрать `Frame` из данных для отправки (`PacketBuilder`);
* посчитать CRC-16 (используется и здесь, и в `Framing.FrameReader`).

Explicitly **не** входит: поиск границ кадра, буферизация потока — это `Framing`.

---

## `MessageType`

```csharp
public enum MessageType : byte
{
    Ping = 0x01, Pong = 0x02,
    Telemetry = 0x10,
    Command = 0x20, Ack = 0x21,
    Error = 0xFF,
}
```

Это единственный источник истины для кодов типов сообщений. Python-эмулятор
(`mc_emulator.py`) обязан использовать те же числовые значения — синхронизация
ручная, автоматической проверки нет (см. известное ограничение ниже).
На момент написания реально используется только `Telemetry`; `Command`,
`Ack`, `Error` зарезервированы под будущее расширение (управление устройством).

## `Crc16`

CRC-16/MODBUS (полином `0xA001`, старт `0xFFFF`, сдвиг вправо). Должен побитово
совпадать с `crc16()` в `mc_emulator.py` — оттуда изначально и взят алгоритм.
Используется дважды: `FrameReader` — при проверке принятого кадра, `PacketBuilder`
неявно не используется (CRC добавляется на уровне `Framing`, не здесь) — сюда
вынесен только сам алгоритм как общая утилита.

## `PacketParser.Parse(Frame frame) : object`

```csharp
switch ((MessageType)frame.MessageType)
{
    case MessageType.Telemetry: return ParseTelemetry(frame); // -> TelemetryPacket
    case MessageType.Ping:  return "PING";
    case MessageType.Pong:  return "PONG";
    case MessageType.Ack:   return "ACK";
    case MessageType.Error: return "ERROR";
    default: throw new NotSupportedException(...);
}
```

Возвращает `object`: для `Telemetry` — типизированный `TelemetryPacket`, для
control-сообщений — просто строку-метку. Это осознанный временный компромисс,
не финальная архитектура (см. ограничения).

`ParseTelemetry` жёстко ожидает payload ровно 4 байта (`ushort` temp + `ushort`
hum, обе — сотые доли единицы, т.е. `24.53°C` передаётся как `2453`). При другой
длине бросает `ArgumentException`. При неизвестном `MessageType` —
`NotSupportedException`. **Оба исключения обязаны быть пойманы вызывающим
кодом** — `PacketParser` сам их не глотает (см. готчу в `transport.md` про
`SerialTransport.ReadLoop`, которая обрывает read-поток на необработанном
исключении; текущая защита — `try/catch` в `MainWindowViewModel.ProcessFramesAsync`,
куда разбор пакетов переехал из `OnDataReceived` вместе с `Channel<Frame>`).

## `PacketBuilder`

Обратная операция — собирает `Frame` из типизированных данных:
`CreatePing`, `CreatePong`, `CreateTelemetry(version, sequence, temperature, humidity)`.
Кодирование температуры/влажности — `Math.Round(value * 100)` в `ushort`,
little-endian вручную (`byte & 0xFF`, `byte >> 8`), без `BitConverter` — совпадает
по эндианности с `ParseTelemetry`, которая использует `BitConverter.ToUInt16`
(на всех актуальных платформах x86/x64/ARM `BitConverter` little-endian, так
что расхождения нет, но неявная зависимость от эндианности платформы имеется —
если код когда-то запустится на big-endian платформе, `PacketBuilder` и
`PacketParser` разойдутся).

На данный момент `PacketBuilder` не используется нигде в реальном приложении
(только в тестах) — актуален, когда появится отправка команд на устройство.

## `TelemetryPacket`

Простой DTO: `Temperature: double`, `Humidity: double`. Без валидации диапазона.

---

## Известные ограничения

* **`object` как тип возврата `Parse`.** Годится для двух текущих случаев
  (типизированный пакет / строка-заглушка), но не масштабируется — добавление
  `Command` с параметрами потребует либо ещё один `object`-каст на вызывающей
  стороне, либо перехода на иерархию `abstract record Packet` с подтипами
  (`TelemetryPacket`, `CommandPacket`, ...) и паттерн-матчинг вместо `switch`
  по enum. Пока не сделано намеренно — рано усложнять при одном реальном типе.
* **Ручная синхронизация `MessageType` с Python.** Уже приводило к реальному
  багу (эмулятор слал `0x01` вместо `0x10` для телеметрии, см. `Misc/diary/2026.07.08.md`).
  Автоматической проверки/общего файла-источника для обеих сторон протокола нет.
* **`BitConverter` эндианность** неявно совпадает с `PacketBuilder`, но явно
  нигде не зафиксирована (см. выше).

---

## Связь с другими модулями

* **Framing** — источник `Frame` (`PacketParser.Parse(frame)`), также источник
  `Crc16.Compute` для проверки кадра.
* **ViewModels** — потребитель разобранных пакетов (`MainWindowViewModel.ProcessFramesAsync`
  вызывает `PacketParser.Parse` и матчит результат через `switch`-выражение).
