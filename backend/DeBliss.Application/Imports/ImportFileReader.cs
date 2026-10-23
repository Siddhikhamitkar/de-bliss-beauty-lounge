using DeBliss.Application.Common.Exceptions;

namespace DeBliss.Application.Imports;

/// <summary>
/// The file-level gate every import passes before a single row is read:
/// extension, size, real xlsx content (never trusting the extension), the named
/// sheet, the exact header set, and the row limit. Every failure here comes
/// back as a validation error keyed "file", which the API turns into a 400 that
/// names the problem — nothing is ever half-read.
/// </summary>
internal static class ImportFileReader
{
    public static ImportSheet Read(
        IImportFiles files,
        Stream content,
        string fileName,
        ImportTypeDescriptor descriptor)
    {
        var errors = new List<string>();

        if (!string.Equals(Path.GetExtension(fileName), ImportRules.XlsxExtension, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Import an .xlsx workbook — download a template and fill it in.");
        }

        var length = EnsureSeekable(ref content);

        if (length == 0)
        {
            errors.Add("The file is empty.");
        }
        else if (length > ImportRules.MaxFileBytes)
        {
            errors.Add($"The file is {length / 1024d / 1024d:0.#} MB; the limit is {ImportRules.MaxFileBytes / 1024d / 1024d:0} MB.");
        }

        if (errors.Count > 0)
        {
            throw FileErrors(errors);
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var read = files.Read(content, descriptor.SheetName);

        switch (read.Status)
        {
            case ImportReadStatus.NotXlsx:
                throw FileErrors($"'{fileName}' is not an .xlsx workbook. The legacy .xls format and other formats are not accepted.");
            case ImportReadStatus.Unreadable:
                throw FileErrors($"'{fileName}' could not be opened as an Excel workbook. It may be corrupt or password-protected.");
            case ImportReadStatus.SheetMissing:
                throw FileErrors($"'{fileName}' has no sheet named '{descriptor.SheetName}'. Download the template and paste your rows into it.");
            case ImportReadStatus.TooManyRows:
                throw FileErrors($"The file exceeds the limit of {ImportRules.MaxRows:N0} data rows.");
        }

        var sheet = read.Sheet!;

        var missing = new List<string>();
        var unexpected = new List<string>();

        for (var i = 0; i < descriptor.Headers.Count; i++)
        {
            var actual = i < sheet.Headers.Count ? sheet.Headers[i] : null;

            if (!string.Equals(actual, descriptor.Headers[i], StringComparison.OrdinalIgnoreCase))
            {
                if (actual is null || actual.Length == 0)
                {
                    missing.Add(descriptor.Headers[i]);
                }
                else
                {
                    unexpected.Add(actual);
                }
            }
        }

        for (var i = descriptor.Headers.Count; i < sheet.Headers.Count; i++)
        {
            unexpected.Add(sheet.Headers[i].Length > 0 ? sheet.Headers[i] : $"Column {i + 1} (no header)");
        }

        if (missing.Count > 0 || unexpected.Count > 0)
        {
            var detail = $"The '{descriptor.SheetName}' sheet must have exactly these columns: {string.Join(", ", descriptor.Headers)}.";

            if (missing.Count > 0)
            {
                detail += $" Missing: {string.Join(", ", missing)}.";
            }

            if (unexpected.Count > 0)
            {
                detail += $" Unexpected: {string.Join(", ", unexpected)}.";
            }

            throw FileErrors(detail);
        }

        if (sheet.Rows.Count > ImportRules.MaxRows)
        {
            throw FileErrors($"The file has {sheet.Rows.Count:N0} data rows; the limit is {ImportRules.MaxRows:N0}.");
        }

        // Blank rows — trailing formatting, stray spaces — are ignored entirely
        // and never counted, so an untidy file does not invent invalid rows.
        var dataRows = sheet.Rows
            .Where(row => row.Cells.Count == 0 || row.Cells.Any(cell => !ImportValues.IsBlank(cell)))
            .ToList();

        if (dataRows.Count == 0)
        {
            throw FileErrors($"The '{descriptor.SheetName}' sheet has no data rows to import. Add your rows under the headers.");
        }

        return sheet with { Rows = dataRows };
    }

    private static long EnsureSeekable(ref Stream content)
    {
        if (content.CanSeek)
        {
            return content.Length;
        }

        // Buffer up to the limit plus one byte, so an oversized upload is
        // detected without reading the rest of it, and parsing can still work
        // off the buffered copy.
        var buffered = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;

            if (total > ImportRules.MaxFileBytes)
            {
                return total;
            }

            buffered.Write(buffer, 0, read);
        }

        buffered.Position = 0;
        content = buffered;

        return total;
    }

    internal static ValidationFailedException FileErrors(params IReadOnlyList<string> messages) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["file"] = [.. messages]
        });
}
