using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Runway.Logging;

// Минимальный провайдер Microsoft.Extensions.Logging поверх AppendOnlyFile.
// Без ротации и без сторонних пакетов вроде Serilog/NLog — только то, что реально
// нужно сейчас: уровни (LogWarning/LogInformation/...) и категория (обычно имя
// класса через ILogger<T>) для diagnostics-событий вроде переподключения порта.
// Телеметрийные данные сюда не попадают — для них по-прежнему LogFileWriter,
// пишущий в отдельный файл (см. AppSettings.LogFilePath vs DiagnosticsLogFilePath).
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly AppendOnlyFile _file;

    public FileLoggerProvider(string filePath)
    {
        _file = new AppendOnlyFile(filePath);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _file);

    public void Dispose()
    {
        _file.Dispose();
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly AppendOnlyFile _file;

    public FileLogger(string categoryName, AppendOnlyFile file)
    {
        _categoryName = categoryName;
        _file = file;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (!IsEnabled(logLevel))
            return;

        string timestamp = DateTime.Now.ToString(
            "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture
        );
        string message = formatter(state, exception);

        string line = $"{timestamp}  [{logLevel}]  {_categoryName}  {message}";
        if (exception != null)
        {
            line += $"{Environment.NewLine}{exception}";
        }

        _file.WriteLine(line);
    }
}
