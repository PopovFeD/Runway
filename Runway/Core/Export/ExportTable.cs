namespace Runway.Export;

// Обобщённая таблица для экспорта: один протокол = одна таблица.
// CSV-писатель делает из неё файл, XLSX-писатель — лист книги. Ячейки:
// double/целые → числа, DateTime → строка ISO, остальное → текст.
public sealed record ExportTable(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows
);
