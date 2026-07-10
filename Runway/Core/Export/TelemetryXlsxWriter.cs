using System.Globalization;
using System.IO.Compression;
using System.Text;
using Runway.Storage;

namespace Runway.Export;

// XLSX-экспорт телеметрии БЕЗ сторонних пакетов: .xlsx — это zip с несколькими
// XML-файлами (формат OpenXML/SpreadsheetML), а System.IO.Compression.ZipArchive
// есть в BCL. Пишется минимально необходимый набор частей: типы содержимого,
// связи, книга с одним листом. Строки — inline (без таблицы sharedStrings),
// числа — как числа, чтобы Excel сразу мог строить графики по колонкам.
// Полноценная библиотека (ClosedXML и т.п.) понадобится только если захочется
// стилей/формул — для таблицы данных это оверкилл.
public static class TelemetryXlsxWriter
{
    public static void Write(string path, IReadOnlyList<TelemetryRecord> records)
    {
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        AddEntry(
            zip,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """
        );
        AddEntry(
            zip,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """
        );
        AddEntry(
            zip,
            "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Telemetry" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """
        );
        AddEntry(
            zip,
            "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """
        );
        AddEntry(zip, "xl/worksheets/sheet1.xml", BuildSheet(records));
    }

    private static string BuildSheet(IReadOnlyList<TelemetryRecord> records)
    {
        var sb = new StringBuilder();
        sb.Append(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
            """
        );

        sb.Append("<row>");
        foreach (string h in new[] { "timestamp", "sequence", "temperature", "humidity", "session_id" })
        {
            sb.Append(StrCell(h));
        }
        sb.Append("</row>");

        foreach (var r in records)
        {
            sb.Append("<row>");
            // Таймстамп строкой ISO, не датой Excel: без потери миллисекунд
            // и без возни с эпохой 1900 года — сортируется лексикографически
            sb.Append(StrCell(r.Timestamp.ToString("O", CultureInfo.InvariantCulture)));
            sb.Append(NumCell(r.Sequence));
            sb.Append(NumCell(r.Temperature));
            sb.Append(NumCell(r.Humidity));
            sb.Append(r.SessionId is long id ? NumCell(id) : "<c/>");
            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static string NumCell(double value) =>
        $"<c t=\"n\"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c>";

    private static string StrCell(string value)
    {
        string escaped = value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
        return $"<c t=\"inlineStr\"><is><t>{escaped}</t></is></c>";
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
