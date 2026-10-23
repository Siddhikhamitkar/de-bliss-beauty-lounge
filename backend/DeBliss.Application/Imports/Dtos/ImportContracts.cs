namespace DeBliss.Application.Imports.Dtos;

/// <summary>How one imported row was judged. Duplicates and existing records are not errors — they are shown so the user decides.</summary>
public enum ImportRowStatus
{
    Valid = 0,
    Invalid = 1,
    DuplicateInFile = 2,
    AlreadyExists = 3
}

/// <summary>One field-level problem on one row. <see cref="RawValue"/> is the offending cell's display text, sanitized before it reaches any report.</summary>
public sealed record ImportFieldError(int RowNumber, string Field, string Message, string? RawValue);

/// <summary>One row of the preview grid: who the row is, how it will be treated, and its values as read.</summary>
public sealed record ImportPreviewRow(int RowNumber, string Label, string Status, IReadOnlyList<string> Fields);

/// <summary>
/// The no-writes verdict for an uploaded workbook: the counts the confirmation
/// dialog states, the first few rows as read, and every field-level error.
/// Creating anything requires a second, explicit call.
/// </summary>
public sealed record ImportPreview(
    string Type,
    string FileName,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int DuplicateRows,
    int ExistingRows,
    int OpeningStockRows,
    IReadOnlyList<ImportPreviewRow> Rows,
    IReadOnlyList<ImportFieldError> Errors);

/// <summary>
/// What an import did. <see cref="Succeeded"/> is false only when persistence
/// itself failed — in which case nothing was saved and the counts describe the
/// attempt that was rolled back. Skipped rows were never written: they were
/// invalid, or already existed and the import never overwrites.
/// </summary>
public sealed record ImportResult(
    string Type,
    string FileName,
    bool Succeeded,
    int Created,
    int SkippedInvalid,
    int SkippedExisting,
    int Failed,
    int OpeningStockMovements,
    string? Message,
    IReadOnlyList<ImportFieldError> Errors);
