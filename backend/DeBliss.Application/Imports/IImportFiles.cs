namespace DeBliss.Application.Imports;

/// <summary>
/// One parsed spreadsheet cell. Exactly one of <see cref="Text"/>, <see cref="Number"/>
/// or <see cref="Date"/> is populated depending on what the cell held; a blank cell
/// carries all three as null. <see cref="HasFormula"/> marks a cell the author typed a
/// formula into, which the import always rejects rather than evaluating.
/// </summary>
public sealed record ImportCell(string? Text, decimal? Number, DateTime? Date, bool HasFormula)
{
    public static readonly ImportCell Empty = new(null, null, null, false);
}

/// <summary>One data row of a parsed sheet. <see cref="RowNumber"/> is the 1-based worksheet row, so row 2 is the first data row.</summary>
public sealed record ImportRow(int RowNumber, IReadOnlyList<ImportCell> Cells);

/// <summary>A parsed worksheet: the header row and the data rows under it.</summary>
public sealed record ImportSheet(IReadOnlyList<string> Headers, IReadOnlyList<ImportRow> Rows);

/// <summary>What a workbook read attempt concluded.</summary>
public enum ImportReadStatus
{
    Ok = 0,
    NotXlsx = 1,
    Unreadable = 2,
    SheetMissing = 3,
    TooManyRows = 4
}

public sealed record ImportFileRead(ImportReadStatus Status, ImportSheet? Sheet);

/// <summary>Layout of one downloadable import template.</summary>
public sealed record ImportTemplateSpec(
    string SheetName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<string> ExampleValues,
    IReadOnlyList<string> Guidance);

/// <summary>Layout of one downloadable error report.</summary>
public sealed record ImportErrorReportSpec(
    string SourceFileName,
    IReadOnlyList<string> FileErrors,
    IReadOnlyList<DeBliss.Application.Imports.Dtos.ImportFieldError> RowErrors);

/// <summary>
/// The spreadsheet mechanics behind the import feature: reading an uploaded
/// workbook, generating a template and generating an error report. Keeping the
/// library behind this port means the import services test the rules, not the
/// file format, and the spreadsheet library stays swappable.
/// </summary>
public interface IImportFiles
{
    /// <summary>Reads the named worksheet. Never throws for bad input; reports it through the status instead.</summary>
    ImportFileRead Read(Stream content, string sheetName);

    byte[] CreateTemplate(ImportTemplateSpec spec);

    byte[] CreateErrorReport(ImportErrorReportSpec report);
}
