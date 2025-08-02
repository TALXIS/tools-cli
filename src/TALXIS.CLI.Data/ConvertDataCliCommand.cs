using DotMake.CommandLine;

namespace TALXIS.CLI.Data;

[CliCommand(
    Name = "convert",
    Description = "Convert tables from an XLSX file to XML."
)]
public class ConvertDataCliCommand
{
    [CliOption(
        Name = "--input",
        Description = "Path to the input XLSX file.",
        Required = true
    )]
    public string? InputPath { get; set; }

    [CliOption(
        Name = "--output",
        Description = "Path to the output XML file.",
        Required = true
    )]
    public string? OutputPath { get; set; }

    public int Run()
    {
        if (string.IsNullOrWhiteSpace(InputPath) || string.IsNullOrWhiteSpace(OutputPath))
        {
            throw new ArgumentException("Both --input and --output must be specified.");
        }

        var xEntities = new System.Xml.Linq.XElement("entities",
            new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
            new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new System.Xml.Linq.XAttribute("timestamp", DateTime.UtcNow.ToString("o"))
        );

        using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(InputPath, false))
        {
            var workbookPart = doc.WorkbookPart;
            if (workbookPart == null)
            {
                throw new InvalidOperationException("Invalid XLSX file: missing workbook part.");
            }
            var sstPart = workbookPart.GetPartsOfType<DocumentFormat.OpenXml.Packaging.SharedStringTablePart>().FirstOrDefault();
            var sst = sstPart?.SharedStringTable;
            var sheets = workbookPart.Workbook?.Sheets?.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>() ?? Array.Empty<DocumentFormat.OpenXml.Spreadsheet.Sheet>();

            foreach (var wsPart in workbookPart.WorksheetParts)
            {
                var ws = wsPart.Worksheet;
                var sheetId = workbookPart.GetIdOfPart(wsPart);
                var sheet = sheets.FirstOrDefault(s => s.Id == sheetId);
                if (sheet == null) continue;

                // Find all tables in this worksheet
                var tableParts = wsPart.TableDefinitionParts;
                foreach (var tablePart in tableParts)
                {
                    var table = tablePart.Table;
                    var tableName = table?.Name?.Value ?? "Table";
                    var entityElem = new System.Xml.Linq.XElement("entity",
                        new System.Xml.Linq.XAttribute("name", tableName),
                        new System.Xml.Linq.XAttribute("displayname", tableName)
                    );
                    var recordsElem = new System.Xml.Linq.XElement("records");

                    // Get table range
                    var refRange = table?.Reference?.Value;
                    if (string.IsNullOrWhiteSpace(refRange))
                        continue;
                    var (startCol, startRow, endCol, endRow) = ParseRange(refRange);

                    // Get header row (first row in range)
                    var headerRow = GetRow(ws, startRow);
                    if (headerRow == null)
                        continue;
                    var colNames = new List<string>();
                    for (int col = startCol; col <= endCol; col++)
                    {
                        var cell = GetCell(headerRow, col);
                        var colName = GetCellValue(cell, sst) ?? $"Column{col}";
                        colNames.Add(colName);
                    }

                    // Data rows
                    for (uint rowIdx = startRow + 1; rowIdx <= endRow; rowIdx++)
                    {
                        var row = GetRow(ws, rowIdx);
                        if (row == null) continue;
                        var recordElem = new System.Xml.Linq.XElement("record",
                            new System.Xml.Linq.XAttribute("id", Guid.NewGuid().ToString())
                        );
                        for (int col = startCol, i = 0; col <= endCol && i < colNames.Count; col++, i++)
                        {
                            var cell = GetCell(row, col);
                            var value = GetCellValue(cell, sst) ?? string.Empty;
                            recordElem.Add(new System.Xml.Linq.XElement("field",
                                new System.Xml.Linq.XAttribute("name", colNames[i]),
                                new System.Xml.Linq.XAttribute("value", value)
                            ));
                        }
                        recordsElem.Add(recordElem);
                    }
                    entityElem.Add(recordsElem);
                    entityElem.Add(new System.Xml.Linq.XElement("m2mrelationships"));
                    xEntities.Add(entityElem);
                }
            }
        }

        var xdoc = new System.Xml.Linq.XDocument(xEntities);
        xdoc.Save(OutputPath);
        Console.WriteLine($"Converted '{InputPath}' to '{OutputPath}'.");
        return 0;
    }

    // Helpers for Open XML SDK
    private static (int startCol, uint startRow, int endCol, uint endRow) ParseRange(string range)
    {
        var parts = range.Split(':');
        var (startCol, startRow) = ParseCellRef(parts[0]);
        var (endCol, endRow) = ParseCellRef(parts[1]);
        return (startCol, startRow, endCol, endRow);
    }

    private static (int col, uint row) ParseCellRef(string cellRef)
    {
        int col = 0, i = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i]))
        {
            col = col * 26 + (char.ToUpper(cellRef[i]) - 'A' + 1);
            i++;
        }
        uint row = uint.Parse(cellRef.Substring(i));
        return (col, row);
    }

    private static DocumentFormat.OpenXml.Spreadsheet.Row? GetRow(DocumentFormat.OpenXml.Spreadsheet.Worksheet ws, uint rowIndex)
    {
        if (ws == null) return null;
        return ws.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().FirstOrDefault(r => r.RowIndex != null && r.RowIndex.Value == rowIndex);
    }

    private static DocumentFormat.OpenXml.Spreadsheet.Cell? GetCell(DocumentFormat.OpenXml.Spreadsheet.Row? row, int colIndex)
    {
        if (row == null) return null;
        string colRef = GetColumnLetter(colIndex) + row.RowIndex;
        return row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().FirstOrDefault(c => c.CellReference?.Value == colRef);
    }

    private static string GetColumnLetter(int colIndex)
    {
        string col = "";
        while (colIndex > 0)
        {
            int rem = (colIndex - 1) % 26;
            col = (char)('A' + rem) + col;
            colIndex = (colIndex - 1) / 26;
        }
        return col;
    }

    private static string? GetCellValue(DocumentFormat.OpenXml.Spreadsheet.Cell? cell, DocumentFormat.OpenXml.Spreadsheet.SharedStringTable? sst)
    {
        if (cell == null) return null;
        var value = cell.CellValue?.InnerText;
        if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString && sst != null)
        {
            if (int.TryParse(value, out int sstIdx) && sstIdx >= 0 && sstIdx < sst.Count())
                return sst.ElementAt(sstIdx).InnerText;
        }
        return value;
    }
}
