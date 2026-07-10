namespace Runway.Storage;

public static class AppStoreEventExtensions
{
    // Событие приложения — в БД, не роняя вызывающего: хранилище может быть
    // недоступно, событие тогда теряется (дубль diagnostics-событий всё равно
    // есть в файле у FileLoggerProvider). Общая точка для всех ViewModel'ей.
    public static void TrySaveEvent(
        this IAppStore? store,
        SessionTracker sessions,
        string level,
        string category,
        string message
    )
    {
        if (store == null)
            return;

        try
        {
            store.SaveEvent(
                new EventRecord(DateTime.Now, level, category, message, sessions.CurrentId)
            );
        }
        catch
        {
            // Некуда репортить — БД и есть место для репортов
        }
    }
}
