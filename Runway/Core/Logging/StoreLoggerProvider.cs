using Microsoft.Extensions.Logging;
using Runway.Storage;

namespace Runway.Logging;

// Мост Microsoft.Extensions.Logging → таблица events (шаг 3 из
// Misc/docs/storage-and-logs-decision.md): diagnostics-события транспорта
// (разрывы порта, ретраи, ошибки открытия) ложатся в ту же БД, что и события
// ViewModel, с session_id из SessionTracker — и видны во вкладке "Логи"
// наравне с остальными.
//
// FileLoggerProvider при этом остаётся вторым провайдером — это "лог
// последней надежды" на случай, когда БД недоступна (см. decision doc).
public sealed class StoreLoggerProvider : ILoggerProvider
{
    private readonly IAppStore _store;
    private readonly SessionTracker _sessions;

    public StoreLoggerProvider(IAppStore store, SessionTracker sessions)
    {
        _store = store;
        _sessions = sessions;
    }

    public ILogger CreateLogger(string categoryName) =>
        new StoreLogger(categoryName, _store, _sessions);

    public void Dispose()
    {
        // Хранилищем владеет App.axaml.cs — здесь закрывать нечего
    }
}

internal sealed class StoreLogger : ILogger
{
    private readonly string _category;
    private readonly IAppStore _store;
    private readonly SessionTracker _sessions;

    public StoreLogger(string categoryName, IAppStore store, SessionTracker sessions)
    {
        // "Runway.Transport.SerialTransport" → "SerialTransport": в колонке
        // category БД полное имя типа — лишний шум при чтении в GUI
        int lastDot = categoryName.LastIndexOf('.');
        _category = lastDot >= 0 ? categoryName[(lastDot + 1)..] : categoryName;
        _store = store;
        _sessions = sessions;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    // Trace/Debug в БД не нужны — это уровень отладчика, не журнала приложения
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

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

        // Уровни M.E.L сводим к трём строкам events (те же, что пишет ViewModel)
        string level = logLevel switch
        {
            LogLevel.Information => "Info",
            LogLevel.Warning => "Warning",
            _ => "Error", // Error и Critical
        };

        string message = formatter(state, exception);
        if (exception != null)
        {
            message += $" ({exception.GetType().Name}: {exception.Message})";
        }

        try
        {
            _store.SaveEvent(
                new EventRecord(DateTime.Now, level, _category, message, _sessions.CurrentId)
            );
        }
        catch
        {
            // Логгер не имеет права бросать; при недоступной БД событие
            // всё равно останется в diagnostics-файле (FileLoggerProvider)
        }
    }
}
