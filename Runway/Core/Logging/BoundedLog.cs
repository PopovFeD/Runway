using System.Collections.ObjectModel;

namespace Runway.Logging;

// Обёртка над ObservableCollection<string>, которая держит не больше Capacity
// последних строк — без этого LogEntries растёт всю сессию неограниченно и
// медленно, но верно съедает память (см. Misc/diary/TODO.md).
// Вынесена отдельным классом, а не встроена прямо в MainWindowViewModel,
// специально ради тестируемости без Avalonia-контекста: ObservableCollection
// можно создать и наполнить в чистом юнит-тесте, без Dispatcher.UIThread.
public class BoundedLog
{
    private readonly ObservableCollection<string> _entries;
    private readonly int _capacity;

    public BoundedLog(ObservableCollection<string> entries, int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Capacity must be positive."
            );
        }

        _entries = entries;
        _capacity = capacity;
    }

    public void Add(string line)
    {
        _entries.Add(line);

        // Обычно тут срабатывает не больше одного RemoveAt(0) за вызов —
        // цикл на случай, если ёмкость уменьшат уже после того, как коллекция
        // успела вырасти сверх нового лимита.
        while (_entries.Count > _capacity)
        {
            _entries.RemoveAt(0);
        }
    }
}
