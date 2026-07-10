using System.Globalization;
using System.Text;
using Runway.Storage;

namespace Runway.Export;

// CSV-экспорт телеметрии. ';' как разделитель — его сразу понимает
// русскоязычный Excel; числа и таймстампы — InvariantCulture (см. logging.md).
public static class TelemetryCsvWriter
{
    public static void Write(string path, IReadOnlyList<TelemetryRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("timestamp;sequence;temperature;humidity;session_id");
        foreach (var r in records)
        {
            sb.Append(r.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            sb.Append(';').Append(r.Sequence.ToString(CultureInfo.InvariantCulture));
            sb.Append(';').Append(r.Temperature.ToString(CultureInfo.InvariantCulture));
            sb.Append(';').Append(r.Humidity.ToString(CultureInfo.InvariantCulture));
            sb.Append(';').Append(r.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "");
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}
