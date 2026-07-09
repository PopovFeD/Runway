# Settings

`AppSettings` (POCO со значениями по умолчанию) + `SettingsLoader`
(`settings.json` рядом с приложением; нет файла — создаётся с дефолтами).

Файлы: `Runway/Core/Settings/AppSettings.cs`, `SettingsLoader.cs`.

Параметры:

* `PortName` — порт, *предвыбранный* в GUI при старте (автоподключения нет);
* `BaudRate` — скорость Serial-транспорта (в конструктор `SerialTransport`);
* `LogFilePath` / `DiagnosticsLogFilePath` — два лога (см. logging.md);
* `DatabaseFilePath` — SQLite с телеметрией (см. storage.md);
* `MaxLogEntries` — кап GUI-лога (`BoundedLog`);
* `ReconnectDelaySeconds` — пауза между попытками переподключения.

Все пути к файлам комбинируются с `AppContext.BaseDirectory` в `App.axaml.cs`,
чтобы не зависеть от рабочего каталога процесса (старый баг settings.json).
Известное ограничение: сам `SettingsLoader` читает `settings.json` по
относительному пути — тот же класс проблемы, лечится аналогично при случае.
