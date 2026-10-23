using System.IO.Compression;
using System.Xml;
using ClosedXML.Excel;
using DeBliss.Application.Imports;
using DeBliss.Application.Imports.Dtos;
using DocumentFormat.OpenXml.Packaging;

namespace DeBliss.Infrastructure.Imports;

/// <summary>
/// The ClosedXML implementation of the import file port. It also carries the
/// format-level safety rules: a workbook is recognised by its bytes, not its
/// file name; the parser only ever opens the one sheet it was asked for, so the
/// template's Example sheet can never be imported; and every value written to
/// an error report is stored as text with a leading quote when it could be
/// mistaken for a formula, so opening the report cannot execute anything.
/// </summary>
public sealed class ClosedXmlImportFiles : IImportFiles
{
    public ImportFileRead Read(Stream content, string sheetName)
    {
        if (!HasXlsxSignature(content))
        {
            return new ImportFileRead(ImportReadStatus.NotXlsx, null);
        }

        try
        {
            content.Position = 0;
            using (var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (archive.Entries.Count > 2048 || archive.Entries.Sum(entry => entry.Length) > 100L * 1024 * 1024
                    || archive.GetEntry("[Content_Types].xml") is null)
                {
                    return new ImportFileRead(ImportReadStatus.Unreadable, null);
                }
            }
            content.Position = 0;
            using (var package = SpreadsheetDocument.Open(content, false))
            {
                if (package.DocumentType != DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook
                    || package.WorkbookPart?.VbaProjectPart is not null)
                {
                    return new ImportFileRead(ImportReadStatus.NotXlsx, null);
                }
            }
            content.Position = 0;
            using var workbook = new XLWorkbook(content);

            if (!workbook.TryGetWorksheet(sheetName, out var worksheet))
            {
                return new ImportFileRead(ImportReadStatus.SheetMissing, null);
            }

            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
            if (lastRow - 1 > ImportRules.MaxRows)
            {
                return new ImportFileRead(ImportReadStatus.TooManyRows, null);
            }

            var headers = new List<string>();
            for (var column = 1; column <= lastColumn; column++)
            {
                var header = worksheet.Cell(1, column);
                if (header.HasFormula)
                {
                    return new ImportFileRead(ImportReadStatus.Unreadable, null);
                }
                headers.Add(header.GetString().Trim());
            }

            var expectedColumns = ImportSheets.All().Select(ImportSheets.Describe)
                .Single(descriptor => descriptor.SheetName == sheetName).Headers.Count;
            if (headers.Count != expectedColumns)
            {
                return new ImportFileRead(ImportReadStatus.Ok, new ImportSheet(headers, []));
            }

            var rows = new List<ImportRow>();

            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                var cells = new List<ImportCell>(headers.Count);

                for (var column = 1; column <= headers.Count; column++)
                {
                    cells.Add(ReadCell(worksheet.Cell(rowNumber, column)));
                }

                rows.Add(new ImportRow(rowNumber, cells));
            }

            return new ImportFileRead(ImportReadStatus.Ok, new ImportSheet(headers, rows));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or FormatException
            or XmlException or OpenXmlPackageException or ArgumentException or OverflowException)
        {
            return new ImportFileRead(ImportReadStatus.Unreadable, null);
        }
    }

    public byte[] CreateTemplate(ImportTemplateSpec spec)
    {
        using var workbook = new XLWorkbook();

        var data = workbook.AddWorksheet(spec.SheetName);

        for (var i = 0; i < spec.Headers.Count; i++)
        {
            var cell = data.Cell(1, i + 1);
            cell.Value = spec.Headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F0EAEE");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            data.Column(i + 1).Width = Math.Max(14, spec.Headers[i].Length + 6);
        }

        data.SheetView.FreezeRows(1);

        var example = workbook.AddWorksheet(ImportRules.ExampleSheetName);

        example.Cell(1, 1).Value = $"How to fill in the '{spec.SheetName}' sheet";
        example.Cell(1, 1).Style.Font.Bold = true;
        example.Cell(1, 1).Style.Font.FontSize = 13;

        example.Cell(2, 1).Value =
            "Put each row under the matching headers in the sheet named at the bottom. " +
            "This Example sheet is guidance only — it is never imported, and you can delete it. " +
            "Do not add formulas; enter plain values.";

        example.Cell(2, 1).Style.Alignment.WrapText = true;
        example.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#6B5E66");
        example.Row(2).Height = 42;

        for (var i = 0; i < spec.Headers.Count; i++)
        {
            var headerCell = example.Cell(4, i + 1);
            headerCell.Value = spec.Headers[i];
            headerCell.Style.Font.Bold = true;
            headerCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            var valueCell = example.Cell(5, i + 1);
            valueCell.Value = spec.ExampleValues[i];
            valueCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            valueCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FBF7F9");

            var guidanceCell = example.Cell(6, i + 1);
            guidanceCell.Value = spec.Guidance[i];
            guidanceCell.Style.Font.Italic = true;
            guidanceCell.Style.Font.FontColor = XLColor.FromHtml("#6B5E66");
            guidanceCell.Style.Alignment.WrapText = true;
            guidanceCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            example.Column(i + 1).Width = Math.Max(14, Math.Min(38, spec.Guidance[i].Length / 2));
        }

        example.Row(6).Height = 56;

        return ToBytes(workbook);
    }

    public byte[] CreateErrorReport(ImportErrorReportSpec report)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Errors");

        var headers = new[] { "Row", "Field", "Problem", "Cell value" };

        for (var i = 0; i < headers.Length; i++)
        {
            var headerCell = sheet.Cell(1, i + 1);
            headerCell.Value = headers[i];
            headerCell.Style.Font.Bold = true;
            headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F0EAEE");
            headerCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var nextRow = 2;

        foreach (var fileError in report.FileErrors)
        {
            WriteErrorRow(sheet, nextRow++, "-", "File", fileError, string.Empty);
        }

        foreach (var error in report.RowErrors)
        {
            WriteErrorRow(sheet, nextRow++, error.RowNumber.ToString(), error.Field, error.Message, error.RawValue ?? string.Empty);
        }

        sheet.Column(1).Width = 8;
        sheet.Column(2).Width = 18;
        sheet.Column(3).Width = 90;
        sheet.Column(4).Width = 30;
        sheet.SheetView.FreezeRows(1);

        return ToBytes(workbook);
    }

    private static void WriteErrorRow(
        IXLWorksheet sheet,
        int rowNumber,
        string rowLabel,
        string field,
        string problem,
        string rawValue)
    {
        sheet.Cell(rowNumber, 1).Value = rowLabel;
        sheet.Cell(rowNumber, 2).Value = field;
        sheet.Cell(rowNumber, 3).Value = problem;
        sheet.Cell(rowNumber, 4).Value = Sanitize(rawValue);
        sheet.Cell(rowNumber, 4).Style.Alignment.WrapText = true;
    }

    /// <summary>
    /// A spreadsheet cell whose text starts with these characters is treated as
    /// a formula when opened in Excel, so the raw value gets a leading apostrophe
    /// — Excel shows the value as plain text instead of executing it.
    /// </summary>
    public static string Sanitize(string rawValue)
    {
        if (rawValue.Length == 0)
        {
            return rawValue;
        }

        return rawValue[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + rawValue : rawValue;
    }

    private static ImportCell ReadCell(IXLCell cell)
    {
        if (cell.HasFormula)
        {
            return new ImportCell($"={cell.FormulaA1}", null, null, true);
        }

        if (cell.DataType == XLDataType.Error)
        {
            throw new FormatException("The workbook contains an Excel error cell.");
        }

        if (cell.IsEmpty())
        {
            return ImportCell.Empty;
        }

        var value = cell.Value;

        return value.Type switch
        {
            XLDataType.Number => new ImportCell(null, Normalize(value.GetNumber()), null, cell.HasFormula),
            XLDataType.DateTime => new ImportCell(null, null, value.GetDateTime(), cell.HasFormula),
            XLDataType.Text => new ImportCell(TrimToNull(value.GetText()), null, null, cell.HasFormula),
            XLDataType.Boolean => new ImportCell(value.GetBoolean() ? "true" : "false", null, null, cell.HasFormula),
            XLDataType.TimeSpan => new ImportCell(value.GetTimeSpan().ToString(), null, null, cell.HasFormula),
            _ => ImportCell.Empty
        };
    }

    /// <summary>Excel numbers are IEEE doubles; rounding at the 12th decimal removes the representation noise without touching real precision.</summary>
    private static decimal Normalize(double number) => decimal.Round((decimal)number, 12);

    private static string? TrimToNull(string text)
    {
        var trimmed = text.Trim();

        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool HasXlsxSignature(Stream content)
    {
        if (!content.CanSeek)
        {
            return false;
        }

        content.Position = 0;

        Span<byte> signature = stackalloc byte[4];
        var read = content.Read(signature);

        content.Position = 0;

        // PK\x03\x04 — the local file header of every Zip archive, which is what an .xlsx physically is.
        return read == 4 && signature[0] == 0x50 && signature[1] == 0x4B && signature[2] == 0x03 && signature[3] == 0x04;
    }

    private static byte[] ToBytes(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();

        workbook.SaveAs(stream);

        return stream.ToArray();
    }
}
