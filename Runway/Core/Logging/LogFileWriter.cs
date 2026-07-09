namespace Runway.Logging;

// Пишет полный, ничем не ограниченный лог принятых кадров на диск.
// В отличие от LogEntries в GUI (см. BoundedLog), тут ничего не обрезается —
// заводить SQLite ради одного этого пока преждевременно, а обычный append-only
// файл почти ничего не стоит по памяти процесса.
public class LogFileWriter : ILogFileWriter
{
    private readonly StreamWriter _writer;

    public LogFileWriter(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // AutoFlush — чтобы после аварийного завершения процесса на диске осталось
        // всё, что успело прийти, а не только то, что попало во внутренний буфер.
        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
    }

    public void WriteLine(string line)
    {
        _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}");
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
