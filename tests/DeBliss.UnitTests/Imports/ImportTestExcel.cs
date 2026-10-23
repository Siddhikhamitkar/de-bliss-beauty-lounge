using ClosedXML.Excel;
using DeBliss.Application.Imports;

namespace DeBliss.UnitTests.Imports;

/// <summary>A cell that must be written as a real Excel formula, the thing an import must refuse.</summary>
internal sealed record Formula(string A1);

/// <summary>
/// Builds real .xlsx workbooks with ClosedXML so the import services are tested
/// against genuine Excel files, not doubles of the file port. Headers and sheet
/// names come from the same descriptors the templates use.
/// </summary>
internal static class ImportTestExcel
{
    public static byte[] For(ImportType type, params object?[][] rows)
    {
        var descriptor = ImportSheets.Describe(type);

        return Workbook(descriptor.SheetName, descriptor.Headers, rows);
    }

    public static byte[] Workbook(string sheetName, IReadOnlyList<string> headers, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);

        for (var column = 0; column < headers.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        for (var index = 0; index < rows.Length; index++)
        {
            WriteCells(sheet, index + 2, rows[index]);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    /// <summary>A workbook whose only sheet carries the given name and nothing else.</summary>
    public static byte[] EmptyWorkbook(string sheetName)
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet(sheetName);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    private static void WriteCells(IXLWorksheet sheet, int rowNumber, object?[] cells)
    {
        for (var column = 0; column < cells.Length; column++)
        {
            var cell = sheet.Cell(rowNumber, column + 1);

            switch (cells[column])
            {
                case null:
                    break;
                case Formula formula:
                    cell.FormulaA1 = formula.A1;
                    break;
                case string text:
                    cell.SetValue(text);
                    break;
                case bool flag:
                    cell.SetValue(flag);
                    break;
                case DateTime date:
                    cell.SetValue(date);
                    break;
                case IFormattable:
                    cell.SetValue(Convert.ToDecimal(cells[column], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                default:
                    cell.SetValue(cells[column].ToString() ?? string.Empty);
                    break;
            }
        }
    }
}
