namespace DeBliss.Application.Imports;

/// <summary>
/// The hard limits and shared strings of the import feature. A workbook larger
/// than this, or with more data rows than this, is refused before a single cell
/// is interpreted, so a pasted-in database cannot stall the API.
/// </summary>
public static class ImportRules
{
    public const long MaxFileBytes = 5L * 1024 * 1024;
    public const int MaxRows = 1000;
    public const int PreviewRowCount = 8;

    /// <summary>The reference stamped on every opening-stock movement an import creates.</summary>
    public const string MovementReference = "Excel import";

    public const string ExampleSheetName = "Example";
    public const string XlsxExtension = ".xlsx";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string ErrorReportSuffix = "-errors.xlsx";
}
