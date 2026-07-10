namespace Runway.Logging;

// Общая обёртка над StreamWriter для append-only файлов: создаёт директорию,
// пишет с AutoFlush (чтобы при аварийном завершении процесса на диске оставалось
// всё, что успело прийти). Используется diagnostics-логом
// (FileLoggerProvider/FileLogger) — "логом последней надежды" при недоступной БД.
public sealed class AppendOnlyFile : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public AppendOnlyFile(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
    }

    public void WriteLine(string line)
    {
        // Диагностический логгер может писать из нескольких потоков одновременно
        // (разные категории через ILogger<T>), телеметрийный — только из своего
        // консьюмера. Лочим в обоих случаях — дёшево, а падать на конкурентной
        // записи в один StreamWriter не хочется.
        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Dispose();
        }
    }
}
