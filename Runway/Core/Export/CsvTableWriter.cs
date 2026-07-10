using System.Globalization;
using System.Text;

namespace Runway.Export;

// CSV-экспорт одной ExportTable. ';' как разделитель — его сразу понимает
// русскоязычный Excel; числа и таймстампы — InvariantCulture.
public static class CsvTableWriter
{
    public static void Write(string path, ExportTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', table.Columns));

        foreach (var row in table.Rows)
        {
            sb.AppendLine(string.Join(';', row.Select(Field)));
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string Field(object? value)
    {
        string text = value switch
        {
            null => "",
            double d => d.ToString(CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

        // Минимальное CSV-экранирование по правилам RFC: кавычим при
        // разделителе/кавычках/переносе, кавычки удваиваем
        if (text.Contains(';') || text.Contains('"') || text.Contains('\n'))
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }
        return text;
    }
}
