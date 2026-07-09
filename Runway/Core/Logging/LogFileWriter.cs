using System.Globalization;

namespace Runway.Logging;

// Пишет полный, ничем не ограниченный лог принятых кадров на диск.
// В отличие от LogEntries в GUI (см. BoundedLog), тут ничего не обрезается —
// заводить SQLite ради одного этого пока преждевременно, а обычный append-only
// файл почти ничего не стоит по памяти процесса. Это лог ДАННЫХ (телеметрия),
// а не диагностики приложения — события вроде переподключения порта идут через
// Microsoft.Extensions.Logging (см. FileLoggerProvider) в отдельный файл, чтобы
// не путать одно с другим в одном потоке строк.
public class LogFileWriter : ILogFileWriter
{
    private readonly AppendOnlyFile _file;

    public LogFileWriter(string filePath)
    {
        _file = new AppendOnlyFile(filePath);
    }

    public void WriteLine(string line)
    {
        // CultureInfo.InvariantCulture — иначе ":" в формате даты/времени заменяется
        // на TimeSeparator текущей локали (не везде это ":"), и лог с одной машины
        // может визуально не совпадать по формату с логом на другой.
        string timestamp = DateTime.Now.ToString(
            "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture
        );
        _file.WriteLine($"{timestamp}  {line}");
    }

    public void Dispose()
    {
        _file.Dispose();
    }
}
