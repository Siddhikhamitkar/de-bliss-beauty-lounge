using System.Net.Mail;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Customers;
using DeBliss.Application.Customers.Dtos;
using DeBliss.Application.Customers.Ports;
using DeBliss.Application.Imports.Dtos;

namespace DeBliss.Application.Imports;

public interface ICustomerImportService
{
    byte[] CreateTemplate();

    /// <summary>Reads and judges a workbook without writing anything.</summary>
    Task<ImportPreview> PreviewAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the customers the workbook describes. Runs inside one
    /// transaction: either every row it commits to importing lands, or nothing
    /// does. Existing records are skipped, never overwritten; duplicates are
    /// imported only when the caller passes the explicit per-file opt-in.
    /// </summary>
    Task<ImportResult> ExecuteAsync(
        Stream content,
        string fileName,
        Guid actorUserId,
        bool importDuplicates,
        CancellationToken cancellationToken = default);

    /// <summary>The same validation as the preview, laid out as a spreadsheet the user can fix from.</summary>
    Task<byte[]> CreateErrorReportAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Excel import for customers. Parsing mirrors <see cref="CustomerService"/>'s
/// rules exactly — same requiredness, same limits, same duplicate notion — so a
/// row the preview calls valid behaves identically to one typed into the
/// customer form. Persistence goes through <see cref="CustomerService"/> itself:
/// the import adds no second way to write a customer.
/// </summary>
public sealed class CustomerImportService(
    IImportFiles files,
    ICustomerRepository customers,
    ICustomerService customerService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICustomerImportService
{
    public byte[] CreateTemplate() =>
        files.CreateTemplate(ImportSheets.Template(ImportType.Customers));

    public async Task<ImportPreview> PreviewAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var parsed = await ParseAsync(content, fileName, cancellationToken);

        return new ImportPreview(
            ImportType.Customers.ToString(),
            fileName,
            parsed.Rows.Count,
            parsed.Rows.Count(row => row.Status == ImportRowStatus.Valid),
            parsed.Rows.Count(row => row.Status == ImportRowStatus.Invalid),
            parsed.Rows.Count(row => row.Status == ImportRowStatus.DuplicateInFile),
            parsed.Rows.Count(row => row.Status == ImportRowStatus.AlreadyExists),
            OpeningStockRows: 0,
            [.. parsed.Rows.Take(ImportRules.PreviewRowCount).Select(ToPreviewRow)],
            parsed.Errors);
    }

    public async Task<ImportResult> ExecuteAsync(
        Stream content,
        string fileName,
        Guid actorUserId,
        bool importDuplicates,
        CancellationToken cancellationToken = default)
    {
        var parsed = await ParseAsync(content, fileName, cancellationToken);

        var created = 0;
        var skippedInvalid = 0;
        var skippedExisting = 0;
        var attempted = 0;

        // One transaction for the whole file: the per-row re-checks and the
        // writes they approve are indivisible, which is also what makes a
        // retried upload safe — everything the first attempt committed is found
        // already existing, so the retry skips instead of duplicating.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var row in parsed.Rows)
            {
                if (row.Status == ImportRowStatus.Invalid)
                {
                    skippedInvalid++;
                    continue;
                }

                if (row.Status == ImportRowStatus.AlreadyExists
                    || (row.Status == ImportRowStatus.DuplicateInFile && !importDuplicates))
                {
                    skippedExisting++;
                    continue;
                }

                // The preview judged this row against an earlier database
                // state; inside the transaction the verdict is re-checked, so
                // nothing slips between the check and the write.
                if (!importDuplicates && row.NormalizedPhone is { } phone)
                {
                    var matches = await customers.GetByNormalizedPhoneAsync(phone, null, cancellationToken);

                    if (matches.Count > 0)
                    {
                        skippedExisting++;
                        continue;
                    }
                }

                attempted++;

                try
                {
                    var customer = await customerService.CreateAsync(
                        new SaveCustomerRequest(
                            row.FirstName,
                            row.LastName,
                            row.Phone,
                            row.Email,
                            row.DateOfBirth,
                            row.Gender,
                            row.Address,
                            IsActive: row.Active,
                            AllowDuplicatePhone: importDuplicates),
                        cancellationToken);

                    created++;

                    if (row.Note is { } note)
                    {
                        await customerService.AddNoteAsync(customer.Id, new SaveCustomerNoteRequest(note), cancellationToken);
                    }
                }
                catch (ConflictException)
                {
                    // A concurrent save claimed the number between the
                    // re-check and the write. Skipped, never overwritten.
                    skippedExisting++;
                    attempted--;
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Disposing the transaction rolls every write back. The result
            // says so plainly instead of reporting success that never landed.
            return new ImportResult(
                ImportType.Customers.ToString(),
                fileName,
                Succeeded: false,
                Created: 0,
                SkippedInvalid: skippedInvalid,
                SkippedExisting: skippedExisting,
                Failed: attempted,
                OpeningStockMovements: 0,
                "The import failed and nothing was saved. Resolve the problem and try again.",
                parsed.Errors);
        }

        return new ImportResult(
            ImportType.Customers.ToString(),
            fileName,
            Succeeded: true,
            created,
            skippedInvalid,
            skippedExisting,
            Failed: 0,
            OpeningStockMovements: 0,
            Message: null,
            parsed.Errors);
    }

    public async Task<byte[]> CreateErrorReportAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parsed = await ParseAsync(content, fileName, cancellationToken);

            return files.CreateErrorReport(new ImportErrorReportSpec(fileName, [], parsed.Errors));
        }
        catch (ValidationFailedException validation)
        {
            var fileErrors = validation.Errors.TryGetValue("file", out var messages)
                ? messages
                : [.. validation.Errors.SelectMany(pair => pair.Value)];

            return files.CreateErrorReport(new ImportErrorReportSpec(fileName, fileErrors, []));
        }
    }

    private async Task<ParsedFile> ParseAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        var descriptor = ImportSheets.Describe(ImportType.Customers);
        var sheet = ImportFileReader.Read(files, content, fileName, descriptor);

        var rows = new List<ParsedRow>(sheet.Rows.Count);
        var errors = new List<ImportFieldError>();
        var firstRowWithPhone = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in sheet.Rows)
        {
            var parsedRow = ParseRow(row, descriptor.Headers);

            if (parsedRow.NormalizedPhone is { } phone && parsedRow.Status != ImportRowStatus.Invalid)
            {
                if (firstRowWithPhone.TryGetValue(phone, out var firstRow))
                {
                    var duplicate = parsedRow with
                    {
                        Status = ImportRowStatus.DuplicateInFile,
                        Errors =
                        [
                            .. parsedRow.Errors,
                            new ImportFieldError(
                                row.RowNumber,
                                "Mobile",
                                $"Same mobile number as row {firstRow}. Only one of them can be imported.",
                                ImportValues.Display(row.Cells[CustomerColumns.Mobile]))
                        ]
                    };

                    rows.Add(duplicate);
                    errors.AddRange(duplicate.Errors);
                    continue;
                }

                firstRowWithPhone[phone] = row.RowNumber;
            }

            rows.Add(parsedRow);
            errors.AddRange(parsedRow.Errors);
        }

        // One database check per row that could still be created: this is the
        // same duplicate rule the customer form warns with, applied per row.
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (row.Status != ImportRowStatus.Valid || row.NormalizedPhone is not { } phone)
            {
                continue;
            }

            var matches = await customers.GetByNormalizedPhoneAsync(phone, null, cancellationToken);

            if (matches.Count > 0)
            {
                var existing = matches[0];

                rows[i] = row with
                {
                    Status = ImportRowStatus.AlreadyExists,
                    Errors =
                    [
                        .. row.Errors,
                        new ImportFieldError(
                            row.RowNumber,
                            "Mobile",
                            $"{existing.FirstName} {existing.LastName} already uses this mobile number; the import never overwrites an existing customer.",
                            ImportValues.Display(sheet.Rows.First(sheetRow => sheetRow.RowNumber == row.RowNumber).Cells[CustomerColumns.Mobile]))
                    ]
                };

                errors.Add(rows[i].Errors[^1]);
            }
        }

        return new ParsedFile(rows, errors);
    }

    private ParsedRow ParseRow(ImportRow row, IReadOnlyList<string> headers)
    {
        var label = string.Empty;
        var errors = new List<ImportFieldError>();

        void Fail(int column, string message)
        {
            errors.Add(new ImportFieldError(row.RowNumber, headers[column], message, ImportValues.Display(row.Cells[column])));
        }

        // A formula anywhere in the row is refused outright: no cell of it is
        // interpreted, so no cached formula result can masquerade as data.
        for (var column = 0; column < headers.Count; column++)
        {
            if (row.Cells[column].HasFormula)
            {
                Fail(column, "Formulas are not allowed. Enter a plain value.");
            }
        }

        if (errors.Count > 0)
        {
            return new ParsedRow(
                row.RowNumber,
                label,
                null, null, null, null, null, null, null, null, null, false,
                ImportRowStatus.Invalid,
                errors,
                [.. row.Cells.Take(headers.Count).Select(ImportValues.Display)]);
        }

        var firstName = ImportValues.Text(row.Cells[CustomerColumns.FirstName]);

        if (firstName is null)
        {
            Fail(CustomerColumns.FirstName, "First name is required.");
        }
        else if (firstName.Length > CustomerLimits.NameMaxLength)
        {
            Fail(CustomerColumns.FirstName, $"First name must be {CustomerLimits.NameMaxLength} characters or fewer.");
        }

        var lastName = ImportValues.Text(row.Cells[CustomerColumns.LastName]);

        if (lastName is null)
        {
            Fail(CustomerColumns.LastName, "Last name is required.");
        }
        else if (lastName.Length > CustomerLimits.NameMaxLength)
        {
            Fail(CustomerColumns.LastName, $"Last name must be {CustomerLimits.NameMaxLength} characters or fewer.");
        }

        var phone = ImportValues.Text(row.Cells[CustomerColumns.Mobile]);
        string? normalizedPhone = null;

        if (phone is null)
        {
            Fail(CustomerColumns.Mobile, "Mobile number is required.");
        }
        else if (phone.Length > CustomerLimits.PhoneMaxLength)
        {
            Fail(CustomerColumns.Mobile, $"Mobile number must be {CustomerLimits.PhoneMaxLength} characters or fewer.");
        }
        else
        {
            normalizedPhone = CustomerPhone.Normalize(phone);

            if (normalizedPhone is null || normalizedPhone.Length < CustomerLimits.MinNormalizedPhoneDigits)
            {
                Fail(CustomerColumns.Mobile, "Enter a valid mobile number.");
                normalizedPhone = null;
            }
        }

        var email = ImportValues.Text(row.Cells[CustomerColumns.Email]);

        if (email is not null)
        {
            if (email.Length > CustomerLimits.EmailMaxLength)
            {
                Fail(CustomerColumns.Email, $"Email must be {CustomerLimits.EmailMaxLength} characters or fewer.");
            }
            else if (!IsValidEmail(email))
            {
                Fail(CustomerColumns.Email, "Enter a valid email address.");
            }
        }

        DateOnly? dateOfBirth = null;

        if (!ImportValues.IsBlank(row.Cells[CustomerColumns.DateOfBirth]))
        {
            if (ImportValues.TryGetDate(row.Cells[CustomerColumns.DateOfBirth], out var parsedDate, out var dateError))
            {
                if (parsedDate > DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
                {
                    Fail(CustomerColumns.DateOfBirth, "Date of birth cannot be in the future.");
                }
                else
                {
                    dateOfBirth = parsedDate;
                }
            }
            else
            {
                Fail(CustomerColumns.DateOfBirth, dateError!);
            }
        }

        var gender = ImportValues.Text(row.Cells[CustomerColumns.Gender]);

        if (gender?.Length > CustomerLimits.GenderMaxLength)
        {
            Fail(CustomerColumns.Gender, $"Gender must be {CustomerLimits.GenderMaxLength} characters or fewer.");
        }

        var address = ImportValues.Text(row.Cells[CustomerColumns.Address]);

        if (address?.Length > CustomerLimits.AddressMaxLength)
        {
            Fail(CustomerColumns.Address, $"Address must be {CustomerLimits.AddressMaxLength} characters or fewer.");
        }

        var note = ImportValues.Text(row.Cells[CustomerColumns.Notes]);

        if (note?.Length > CustomerLimits.NoteMaxLength)
        {
            Fail(CustomerColumns.Notes, $"Note must be {CustomerLimits.NoteMaxLength} characters or fewer.");
        }

        var active = true;

        if (!ImportValues.IsBlank(row.Cells[CustomerColumns.Active])
            && !ImportValues.TryGetBool(row.Cells[CustomerColumns.Active], out active, out var activeError))
        {
            Fail(CustomerColumns.Active, activeError!);
        }

        label = $"{firstName} {lastName}".Trim();

        return new ParsedRow(
            row.RowNumber,
            label,
            firstName,
            lastName,
            phone,
            normalizedPhone,
            email,
            dateOfBirth,
            gender,
            address,
            note,
            active,
            errors.Count == 0 ? ImportRowStatus.Valid : ImportRowStatus.Invalid,
            errors,
            [.. row.Cells.Select(ImportValues.Display)]);
    }

    private static ImportPreviewRow ToPreviewRow(ParsedRow row) =>
        new(row.RowNumber, row.Label, row.Status.ToString(), row.DisplayFields);

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record ParsedFile(
        IReadOnlyList<ParsedRow> Rows,
        IReadOnlyList<ImportFieldError> Errors);

    private sealed record ParsedRow(
        int RowNumber,
        string Label,
        string? FirstName,
        string? LastName,
        string? Phone,
        string? NormalizedPhone,
        string? Email,
        DateOnly? DateOfBirth,
        string? Gender,
        string? Address,
        string? Note,
        bool Active,
        ImportRowStatus Status,
        IReadOnlyList<ImportFieldError> Errors,
        IReadOnlyList<string> DisplayFields);
}
