namespace Runway.Storage;

// Одна принятая точка телеметрии — то, что уходит в хранилище.
// Timestamp — момент приёма на стороне приложения (устройство своих часов
// не передаёт), той же природы, что таймстампы в runway.log.
public record TelemetryRecord(
    DateTime Timestamp,
    ushort Sequence,
    double Temperature,
    double Humidity
);

// Слой хранения телеметрии. ViewModel пишет сюда из консьюмера очереди кадров
// (ProcessFramesAsync) — ровно тот сценарий, ради которого заводился
// Channel<Frame>: запись в БД не задевает read-поток порта.
// Жизненным циклом (Dispose) управляет создатель — App.axaml.cs, по аналогии
// с ILogFileWriter.
public interface ITelemetryStore : IDisposable
{
    void Save(TelemetryRecord record);
}
