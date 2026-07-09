# ViewModels

`MainWindowViewModel` — центр приложения: соединяет Transport → Framing →
Protocol → Storage/Logging и отдаёт всё это GUI через биндинги. `ViewLocator` +
`ViewModelBase` — стандартный каркас Avalonia (CommunityToolkit.Mvvm,
`ObservableObject`).

Файлы: `Runway/ViewModels/MainWindowViewModel.cs`, `ViewModelBase.cs`,
`Runway/ViewLocator.cs`, разметка — `Runway/Views/MainWindow.axaml`.

---

## Подключение (GUI)

* `Transports` / `SelectedTransport` — список способов подключения
  (`ITransport`: Serial рабочий, WiFi — заглушка). Смена транспорта
  обновляет `AvailableEndpoints`.
* `AvailableEndpoints` / `SelectedEndpoint` — точки подключения выбранного
  транспорта; `RefreshEndpointsCommand` («Обновить список») перечитывает их.
* `ConnectCommand` / `DisconnectCommand` (вкладка «Подключение») и единая
  `ToggleConnectionCommand` (кнопка в верхней панели, текст по факту
  подключения) — вся доступность через `CanExecute`, в XAML логики нет.
  «Отключить» доступна и при `Reconnecting` — это отмена ретраев.
* На Connect открывается сессия в `IAppStore` (BeginSession), на
  Disconnect — закрывается; события подключения/ошибки разбора пишутся
  в БД через `SaveEventQuietly` (сбой БД ничего не роняет).
* `_activeTransport`/`_activeEndpoint` — ФАКТ подключения; Selected* — лишь
  намерение. `StatusText` («Подключено: Serial (COM) · COM6») строится по
  факту, поэтому листание ComboBox индикатор не путает.
* `Disconnect` сам выставляет `Disconnected`: `Close()` транспорта событий
  не поднимает (штатная остановка — не разрыв). Запоздавшие события от уже
  закрытого транспорта игнорируются (`_activeTransport == null`).

## Конвейер данных

`OnDataReceived` (read-поток транспорта!) только режет байты на кадры и кладёт
в безлимитный `Channel<Frame>`. Консьюмер `ProcessFramesAsync` (отдельная Task):
разбор (`PacketParser`), запись телеметрии в `IAppStore` с session_id (ошибка
БД — `StoreError` в лог, конвейер живёт), строка — в файл (`ILogFileWriter`)
и в GUI (`BoundedLog` + метка времени, через `IUiDispatcher`), там же
обновляются плитки Дашборда (LastTemperatureText и т.д.). Никакого I/O в
read-потоке. Автопрокрутка живого вывода — code-behind MainWindow.axaml.cs.

`IUiDispatcher` — абстракция над `Dispatcher.UIThread.Post` ради тестов
(`ImmediateUiDispatcher` выполняет синхронно).

## Владение

VM подписывается на все транспорты в конструкторе и отписывается в `Dispose`;
транспортами, логами и хранилищем владеет `App.axaml.cs` (создание и Dispose
в `desktop.Exit`, порядок важен — см. комментарий там).

## Тесты

`Runway.Tests/ViewModels/*`: pipeline (кадры → лог/файл), connection commands
(предвыбор порта, CanExecute-переходы, StatusText), connection status,
telemetry store. Все — на фейках из `Runway.Tests/Support/`.
