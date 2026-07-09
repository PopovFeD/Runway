using Runway.Logging;
using Xunit;

namespace Runway.Tests.Logging;

// Настоящий файл на диске, во временной папке — а не заглушка. Абстракцию
// ILogFileWriter как таковую (используемую MainWindowViewModel) тестировать
// отдельно не нужно — это её единственная реализация; заглушку FakeLogFileWriter
// используем только там, где реального файла быть не должно (см. Runway.Tests.ViewModels).
public class LogFileWriterTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"runway-test-{Guid.NewGuid():N}.log"
    );

    [Fact]
    public void WriteLine_AppendsLineWithTimestamp_ToFile()
    {
        using (var writer = new LogFileWriter(_path))
        {
            writer.WriteLine("Seq=1  PING");
        }

        string content = File.ReadAllText(_path);

        Assert.Contains("Seq=1  PING", content);
    }

    [Fact]
    public void WriteLine_Appends_WhenFileAlreadyExistsFromPreviousSession()
    {
        using (var writer = new LogFileWriter(_path))
        {
            writer.WriteLine("first");
        }

        // Новый LogFileWriter поверх того же пути — как при перезапуске приложения.
        using (var writer = new LogFileWriter(_path))
        {
            writer.WriteLine("second");
        }

        var lines = File.ReadAllLines(_path);

        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, l => l.Contains("first"));
        Assert.Contains(lines, l => l.Contains("second"));
    }

    [Fact]
    public void Constructor_CreatesDirectory_WhenItDoesNotExistYet()
    {
        string nestedDir = Path.Combine(
            Path.GetTempPath(),
            $"runway-test-dir-{Guid.NewGuid():N}"
        );
        string nestedPath = Path.Combine(nestedDir, "runway.log");

        try
        {
            using var writer = new LogFileWriter(nestedPath);
            writer.WriteLine("hello");

            Assert.True(Directory.Exists(nestedDir));
            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            if (Directory.Exists(nestedDir))
                Directory.Delete(nestedDir, recursive: true);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
