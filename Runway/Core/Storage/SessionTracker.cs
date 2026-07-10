namespace Runway.Storage;

// Текущая сессия подключения, разделяемая между MainWindowViewModel
// (открывает/закрывает сессию) и StoreLoggerProvider (проставляет session_id
// diagnostics-событиям транспорта). Потокобезопасна: пишется из UI-потока,
// читается из транспортного потока и консьюмера кадров.
public sealed class SessionTracker
{
    private long _currentId; // 0 — сессии нет

    public long? CurrentId
    {
        get
        {
            long value = Volatile.Read(ref _currentId);
            return value == 0 ? null : value;
        }
    }

    public void Set(long sessionId) => Volatile.Write(ref _currentId, sessionId);

    public void Clear() => Volatile.Write(ref _currentId, 0);
}
