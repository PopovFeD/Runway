using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Runway.Export;

// XLSX-экспорт БЕЗ сторонних пакетов: .xlsx — это zip с XML-частями
// (OpenXML/SpreadsheetML), ZipArchive есть в BCL. Каждая ExportTable —
// отдельный лист книги, так один файл несёт несколько протоколов сразу.
// Числа пишутся числами (Excel сразу строит графики), строки — inline,
// таймстампы — строкой ISO (без потери миллисекунд и эпохи 1900 года).
public static class XlsxTableWriter
{
    public static void Write(string path, IReadOnlyList<ExportTable> tables)
    {
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        var contentTypes = new StringBuilder(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
            """
        );
        var sheets = new StringBuilder();
        var workbookRels = new StringBuilder(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            """
        );

        for (int i = 0; i < tables.Count; i++)
        {
            int n = i + 1;
            contentTypes.Append(
                $"<Override PartName=\"/xl/worksheets/sheet{n}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
            );
            sheets.Append(
                $"<sheet name=\"{Escape(tables[i].Name)}\" sheetId=\"{n}\" r:id=\"rId{n}\"/>"
            );
            workbookRels.Append(
                $"<Relationship Id=\"rId{n}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{n}.xml\"/>"
            );
            AddEntry(zip, $"xl/worksheets/sheet{n}.xml", BuildSheet(tables[i]));
        }

        contentTypes.Append("</Types>");
        workbookRels.Append("</Relationships>");

        AddEntry(zip, "[Content_Types].xml", contentTypes.ToString());
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
            $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>{sheets}</sheets>
            </workbook>
            """
        );
        AddEntry(zip, "xl/_rels/workbook.xml.rels", workbookRels.ToString());
    }

    private static string BuildSheet(ExportTable table)
    {
        var sb = new StringBuilder(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
            """
        );

        sb.Append("<row>");
        foreach (string column in table.Columns)
        {
            sb.Append(StrCell(column));
        }
        sb.Append("</row>");

        foreach (var row in table.Rows)
        {
            sb.Append("<row>");
            foreach (object? cell in row)
            {
                sb.Append(Cell(cell));
            }
            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static string Cell(object? value) =>
        value switch
        {
            null => "<c/>",
            double d => NumCell(d.ToString(CultureInfo.InvariantCulture)),
            int or long or ushort or byte => NumCell(
                ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture)
            ),
            DateTime dt => StrCell(dt.ToString("O", CultureInfo.InvariantCulture)),
            _ => StrCell(value.ToString() ?? ""),
        };

    private static string NumCell(string invariant) => $"<c t=\"n\"><v>{invariant}</v></c>";

    private static string StrCell(string value) =>
        $"<c t=\"inlineStr\"><is><t>{Escape(value)}</t></is></c>";

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
