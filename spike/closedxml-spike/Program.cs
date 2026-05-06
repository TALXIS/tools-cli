using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

// ============================================================
// ClosedXML Feature Spike — exercises every Excel feature
// needed for the staging workbook.
// ============================================================

var outputPath = Path.Combine(AppContext.BaseDirectory, "spike-output.xlsx");
var results = new List<(string Feature, string Status, string Detail)>();

void Pass(string feature, string detail) => results.Add((feature, "✅", detail));
void Warn(string feature, string detail) => results.Add((feature, "⚠️", detail));
void Fail(string feature, string detail) => results.Add((feature, "❌", detail));

using var workbook = new XLWorkbook();

// ──────────────────────────────────────────────────────────────
// Setup: Lookup sheet with reference data
// ──────────────────────────────────────────────────────────────
var lookupSheet = workbook.Worksheets.Add("Lookup");
var lookupValues = new[] { "Option A", "Option B", "Option C", "Option D", "Option E" };
for (int i = 0; i < lookupValues.Length; i++)
    lookupSheet.Cell(i + 1, 1).Value = lookupValues[i];

// Status optionset values in column B of Lookup sheet
var statusValues = new[] { "Active [0]", "Inactive [1]", "Draft [2]" };
for (int i = 0; i < statusValues.Length; i++)
    lookupSheet.Cell(i + 1, 2).Value = statusValues[i];

var dataSheet = workbook.Worksheets.Add("Data");
dataSheet.Cell(1, 1).Value = "Entity Metadata Header — Merged";
dataSheet.Cell(2, 1).Value = "ID";
dataSheet.Cell(2, 2).Value = "Name";
dataSheet.Cell(2, 3).Value = "Status";
dataSheet.Cell(2, 4).Value = "Lookup";
dataSheet.Cell(2, 5).Value = "Formula";
dataSheet.Cell(2, 6).Value = "Notes";

// Row 3: logical names (will be hidden)
dataSheet.Cell(3, 1).Value = "id";
dataSheet.Cell(3, 2).Value = "name";
dataSheet.Cell(3, 3).Value = "statuscode";
dataSheet.Cell(3, 4).Value = "lookupfield";

// Sample data rows
for (int r = 4; r <= 8; r++)
{
    dataSheet.Cell(r, 1).Value = Guid.NewGuid().ToString();
    dataSheet.Cell(r, 2).Value = $"Record {r - 3}";
    dataSheet.Cell(r, 3).Value = r == 6 ? "" : "Active";
}

// ──────────────────────────────────────────────────────────────
// 1. Data Validation — three strategies compared
//    Column C (Status) = optionset dropdown
//    Column D (Lookup) = lookup dropdown
//    We test: named range, direct sheet ref, and INDIRECT
// ──────────────────────────────────────────────────────────────
try
{
    // Define named ranges for both option lists
    workbook.DefinedNames.Add("LookupOptions", lookupSheet.Range("A1:A5"));
    workbook.DefinedNames.Add("StatusOptions", lookupSheet.Range("B1:B3"));

    // Strategy A: Named range reference (recommended — works everywhere)
    // Status column C4:C8 via named range
    dataSheet.Range("C4:C8").CreateDataValidation()
        .List("=StatusOptions");
    Pass("1a. Data validation — named range (Status C4:C8)",
        ".List(\"=StatusOptions\") → references Lookup!B1:B3 via named range");

    // Lookup column D4:D5 via named range
    dataSheet.Range("D4:D5").CreateDataValidation()
        .List("=LookupOptions");
    Pass("1b. Data validation — named range (Lookup D4:D5)",
        ".List(\"=LookupOptions\") → references Lookup!A1:A5 via named range");

    // Strategy B: Direct sheet!range reference (works in most Excel versions)
    dataSheet.Range("D6:D7").CreateDataValidation()
        .List("=Lookup!$A$1:$A$5");
    Pass("1c. Data validation — direct sheet ref (Lookup D6:D7)",
        ".List(\"=Lookup!$A$1:$A$5\") → direct cross-sheet reference");

    // Strategy C: INDIRECT formula (volatile — may NOT show dropdown in all Excel versions)
    dataSheet.Range("D8:D8").CreateDataValidation()
        .List("=INDIRECT(\"Lookup!$A$1:$A$5\")");
    Warn("1d. Data validation — INDIRECT (Lookup D8)",
        ".List(\"=INDIRECT(...)\") → accepted by API, but dropdown may not appear in Excel desktop");
}
catch (Exception ex)
{
    Fail("1. Data validation", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 2. Conditional Formatting
// ──────────────────────────────────────────────────────────────
try
{
    // CellIsRule: red when empty
    dataSheet.Range("C4:C8").AddConditionalFormat()
        .WhenEquals("\"\"")
        .Fill.SetBackgroundColor(XLColor.Red);

    // CellIsRule: green when not empty
    dataSheet.Range("C4:C8").AddConditionalFormat()
        .WhenNotEquals("\"\"")
        .Fill.SetBackgroundColor(XLColor.Green);

    Pass("2a. Conditional format (CellIs)", "WhenEquals / WhenNotEquals with fill colors");

    // FormulaRule: orange when contains ERROR
    dataSheet.Range("C4:C8").AddConditionalFormat()
        .WhenIsTrue("=ISNUMBER(SEARCH(\"ERROR\",C4))")
        .Fill.SetBackgroundColor(XLColor.Orange);

    Pass("2b. Conditional format (Formula)", "WhenIsTrue(\"=ISNUMBER(SEARCH(...))\") works");
}
catch (Exception ex)
{
    Fail("2. Conditional formatting", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 3. Named Ranges
// ──────────────────────────────────────────────────────────────
try
{
    // Workbook-level (already created LookupOptions above)
    Pass("3a. Named range (workbook)", "workbook.NamedRanges.Add(name, range)");

    // Sheet-level named range
    dataSheet.DefinedNames.Add("DataHeaders", dataSheet.Range("A2:F2"));
    Pass("3b. Named range (sheet)", "worksheet.NamedRanges.Add(name, range)");
}
catch (Exception ex)
{
    Fail("3. Named ranges", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 4. Sheet Protection with Per-Cell Unlock
// ──────────────────────────────────────────────────────────────
try
{
    // Mark editable cells as unlocked BEFORE protecting
    dataSheet.Range("C4:C8").Style.Protection.Locked = false;
    dataSheet.Range("D4:D8").Style.Protection.Locked = false;

    // Protect the sheet (no password)
    dataSheet.Protect();

    Pass("4. Sheet protection + per-cell unlock",
        "Style.Protection.Locked = false on ranges, then sheet.Protect()");
}
catch (Exception ex)
{
    Fail("4. Sheet protection", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 5. Hidden Rows and Columns
// ──────────────────────────────────────────────────────────────
try
{
    // Hide row 3 (logical names)
    dataSheet.Row(3).Hide();
    Pass("5a. Hidden row", "Row(3).Hide()");

    // Hide column F
    dataSheet.Column(6).Hide();
    Pass("5b. Hidden column", "Column(6).Hide()");
}
catch (Exception ex)
{
    Fail("5. Hidden rows/columns", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 6. Cell Comments / Notes
// ──────────────────────────────────────────────────────────────
try
{
    var comment = dataSheet.Cell("A2").GetComment();
    comment.AddText("Primary key column.").AddNewLine();
    comment.AddText("Auto-generated GUID — do not edit.");
    Pass("6. Cell comments", "cell.GetComment().AddText(...).AddNewLine().AddText(...)");
}
catch (Exception ex)
{
    Fail("6. Cell comments", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 7. Frozen Panes
// ──────────────────────────────────────────────────────────────
try
{
    // Freeze rows 1-3 and column A
    dataSheet.SheetView.FreezeRows(3);
    dataSheet.SheetView.FreezeColumns(1);
    Pass("7. Frozen panes", "SheetView.FreezeRows(3) + FreezeColumns(1)");
}
catch (Exception ex)
{
    Fail("7. Frozen panes", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 8. Auto-Filter
// ──────────────────────────────────────────────────────────────
try
{
    dataSheet.Range("A2:F2").SetAutoFilter();
    Pass("8. Auto-filter", "range.SetAutoFilter()");
}
catch (Exception ex)
{
    Fail("8. Auto-filter", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 9. Tab Colors
// ──────────────────────────────────────────────────────────────
try
{
    lookupSheet.TabColor = XLColor.Blue;
    dataSheet.TabColor = XLColor.Green;
    Pass("9. Tab colors", "worksheet.TabColor = XLColor.X");
}
catch (Exception ex)
{
    Fail("9. Tab colors", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 10. Formulas
// ──────────────────────────────────────────────────────────────
try
{
    for (int r = 4; r <= 8; r++)
    {
        dataSheet.Cell(r, 5).FormulaA1 = $"=B{r}&\" [\"&A{r}&\"]\"";
    }
    Pass("10. Formulas", "cell.FormulaA1 = \"=B4&\\\" [\\\"&A4&\\\"]\\\"\"; evaluates on open");
}
catch (Exception ex)
{
    Fail("10. Formulas", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 11. Column Widths / Auto-Fit
// ──────────────────────────────────────────────────────────────
try
{
    dataSheet.Column(1).Width = 38; // GUID column
    dataSheet.Column(2).Width = 25; // Name
    dataSheet.Column(3).Width = 15; // Status
    dataSheet.Column(4).Width = 20; // Lookup
    dataSheet.Column(5).Width = 45; // Formula

    Pass("11a. Explicit column widths", "column.Width = N");

    // Auto-fit test on lookup sheet
    lookupSheet.Columns().AdjustToContents();
    Pass("11b. Column auto-fit", "columns.AdjustToContents()");
}
catch (Exception ex)
{
    Fail("11. Column widths", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 12. Merged Cells
// ──────────────────────────────────────────────────────────────
try
{
    dataSheet.Range("A1:F1").Merge();
    Pass("12. Merged cells", "range.Merge()");
}
catch (Exception ex)
{
    Fail("12. Merged cells", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// 13. Cell Styling
// ──────────────────────────────────────────────────────────────
try
{
    // Merged header row
    var headerMerged = dataSheet.Range("A1:F1");
    headerMerged.Style.Font.Bold = true;
    headerMerged.Style.Font.FontSize = 14;
    headerMerged.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

    // Column headers (row 2)
    var headers = dataSheet.Range("A2:F2");
    headers.Style.Font.Bold = true;
    headers.Style.Fill.BackgroundColor = XLColor.Green;
    headers.Style.Font.FontColor = XLColor.White;

    // Lookup column header highlight
    dataSheet.Cell("D2").Style.Fill.BackgroundColor = XLColor.LightBlue;
    dataSheet.Cell("D2").Style.Font.FontColor = XLColor.Black;

    // Data cell borders
    var dataCells = dataSheet.Range("A4:F8");
    dataCells.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    dataCells.Style.Border.OutsideBorderColor = XLColor.Gray;
    dataCells.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    dataCells.Style.Border.InsideBorderColor = XLColor.Gray;

    Pass("13. Cell styling", "Font.Bold, Fill.BackgroundColor, Font.FontColor, Border styles all work");
}
catch (Exception ex)
{
    Fail("13. Cell styling", ex.Message);
}

// ──────────────────────────────────────────────────────────────
// Save and report
// ──────────────────────────────────────────────────────────────
try
{
    workbook.SaveAs(outputPath);
    Console.WriteLine($"Workbook saved to: {outputPath}");
    Console.WriteLine($"File size: {new FileInfo(outputPath).Length:N0} bytes");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"SAVE FAILED: {ex.Message}");
}

// Print results table
// ──────────────────────────────────────────────────────────────
// OpenXml SDK Validation
// ──────────────────────────────────────────────────────────────
Console.WriteLine("═══════════════════════════════════════════════════════════════════");
Console.WriteLine("  OpenXml Validation");
Console.WriteLine("═══════════════════════════════════════════════════════════════════");

using (var doc = SpreadsheetDocument.Open(outputPath, false))
{
    var validator = new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019);
    var errors = validator.Validate(doc).ToList();

    if (errors.Count == 0)
    {
        Console.WriteLine("  ✅ No validation errors found.");
    }
    else
    {
        Console.WriteLine($"  ❌ {errors.Count} validation error(s) found:\n");
        foreach (var error in errors)
        {
            Console.WriteLine($"  [{error.ErrorType}] {error.Description}");
            Console.WriteLine($"    Part: {error.Part?.Uri}");
            Console.WriteLine($"    Path: {error.Path?.XPath}");
            if (error.Node != null)
                Console.WriteLine($"    Node: {error.Node.OuterXml[..Math.Min(200, error.Node.OuterXml.Length)]}");
            Console.WriteLine();
        }
    }
}

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════════════");
Console.WriteLine("  ClosedXML Feature Spike Results");
Console.WriteLine("═══════════════════════════════════════════════════════════════════");
foreach (var (feature, status, detail) in results)
{
    Console.WriteLine($"  {status} {feature}");
    Console.WriteLine($"     {detail}");
    Console.WriteLine();
}
Console.WriteLine("═══════════════════════════════════════════════════════════════════");
