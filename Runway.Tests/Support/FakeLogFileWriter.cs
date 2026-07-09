using Runway.Logging;

namespace Runway.Tests.Support;

// Не пишет на диск — просто копит строки в памяти, чтобы тест мог проверить,
// что полный (неограниченный) лог получает вообще все строки, включая те,
// что уже вытеснены из BoundedLog/LogEntries в GUI. Реальная запись в файл
// проверяется отдельно, в LogFileWriterTests.
public class FakeLogFileWriter : ILogFileWriter
{
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines;
    public bool Disposed { get; private set; }

    public void WriteLine(string line) => _lines.Add(line);

    public void Dispose() => Disposed = true;
}
