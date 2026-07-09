using Microsoft.Extensions.Logging;
using Runway.Logging;
using Xunit;

namespace Runway.Tests.Logging;

public class FileLoggerProviderTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"runway-diagnostics-test-{Guid.NewGuid():N}.log"
    );

    [Fact]
    public void Logger_WritesLevelAndCategoryAndMessage_ToFile()
    {
        using (var provider = new FileLoggerProvider(_path))
        {
            var logger = provider.CreateLogger("Runway.Transport.SerialTransport");
            logger.LogWarning("Порт {PortName} разорван.", "COM6");
        }

        string content = File.ReadAllText(_path);

        Assert.Contains("[Warning]", content);
        Assert.Contains("Runway.Transport.SerialTransport", content);
        Assert.Contains("Порт COM6 разорван.", content);
    }

    [Fact]
    public void Logger_WritesExceptionDetails_WhenExceptionIsPassed()
    {
        using (var provider = new FileLoggerProvider(_path))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogError(new InvalidOperationException("boom"), "Не удалось открыть порт.");
        }

        string content = File.ReadAllText(_path);

        Assert.Contains("[Error]", content);
        Assert.Contains("Не удалось открыть порт.", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("boom", content);
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_OnlyForNoneLevel()
    {
        using var provider = new FileLoggerProvider(_path);
        var logger = provider.CreateLogger("Test");

        Assert.False(logger.IsEnabled(LogLevel.None));
        Assert.True(logger.IsEnabled(LogLevel.Trace));
        Assert.True(logger.IsEnabled(LogLevel.Critical));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
