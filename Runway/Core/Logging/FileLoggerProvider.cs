using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Runway.Logging;

// Минимальный провайдер Microsoft.Extensions.Logging поверх AppendOnlyFile.
// После переезда событий в БД (StoreLoggerProvider) файл выполняет роль
// "лога последней надежды": сюда события пишутся параллельно с БД и остаются
// доступными, даже когда сама БД не открылась (см. storage-and-logs-decision.md).
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
