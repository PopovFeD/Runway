namespace Runway.Logging;

// Абстракция вокруг записи в лог-файл — нужна, чтобы MainWindowViewModel можно было
// протестировать без реального файла на диске (см. FakeLogFileWriter в Runway.Tests).
public interface ILogFileWriter : IDisposable
{
    void WriteLine(string line);
}
