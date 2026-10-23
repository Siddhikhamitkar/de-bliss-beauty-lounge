using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Imports.Dtos;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;

namespace DeBliss.Application.Imports;

public interface IServiceImportService
{
    byte[] CreateTemplate();
    Task<ImportPreview> PreviewAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
    Task<ImportResult> ExecuteAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
    Task<byte[]> CreateErrorReportAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
}

public sealed class ServiceImportService(
    IImportFiles files,
    IServiceCategoryRepository categories,
    IStaffDirectory staffDirectory,
    IServiceCatalogueService catalogue,
    IUnitOfWork unitOfWork) : IServiceImportService
{
    private static readonly ImportTypeDescriptor Descriptor = ImportSheets.Describe(ImportType.Services);
    private static readonly IReadOnlyDictionary<string, int> FieldColumns = new Dictionary<string, int>
    {
        ["name"] = 0,
        ["categoryId"] = 1,
        ["price"] = 2,
        ["durationMinutes"] = 3,
        ["description"] = 4,
        ["isActive"] = 5,
        ["eligibleStaffIds"] = 6
    };

    public byte[] CreateTemplate() => files.CreateTemplate(ImportSheets.Template(ImportType.Services));

    public async Task<ImportPreview> PreviewAsync(
        Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var parsed = await ParseAsync(content, fileName, cancellationToken);
        return new ImportPreview(
            nameof(ImportType.Services), fileName, parsed.Rows.Count,
            parsed.Rows.Count(row => row.Status == ImportRowStatus.Valid),
            parsed.Rows.Count(row => row.Status == ImportRowStatus.Invalid),
            parsed.Rows.Count(row => row.Status == ImportRowStatus.DuplicateInFile),
            parsed.Rows.Count(row => row.Status == ImportRowStatus.AlreadyExists),
            0,
            [.. parsed.Rows.Take(ImportRules.PreviewRowCount).Select(row => new ImportPreviewRow(
                row.RowNumber, row.Request.Name ?? string.Empty, row.Status.ToString(), row.Fields))],
            parsed.Errors);
    }

    public async Task<ImportResult> ExecuteAsync(
        Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sheet = ImportFileReader.Read(files, content, fileName, Descriptor);
        var skippedInvalid = 0;
        var skippedExisting = 0;
        var errors = new List<ImportFieldError>();
        var created = 0;
        ParsedRow? current = null;

        try
        {
            // Load tracked categories and staff only after the transaction protects their eligibility.
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            var parsed = await ParseAsync(sheet, cancellationToken);
            skippedInvalid = parsed.Rows.Count(row => row.Status == ImportRowStatus.Invalid);
            skippedExisting = parsed.Rows.Count(row => row.Status is ImportRowStatus.AlreadyExists or ImportRowStatus.DuplicateInFile);
            errors.AddRange(parsed.Errors);
            foreach (var row in parsed.Rows.Where(row => row.Status == ImportRowStatus.Valid))
            {
                current = row;
                try
                {
                    await catalogue.CreateAsync(row.Request, cancellationToken);
                    created++;
                }
                catch (ConflictException)
                {
                    skippedExisting++;
                    errors.Add(FieldError(row.RowNumber, 0,
                        "This service name now exists in the category; it was skipped without changing it.", row.Fields[0]));
                }
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (exception is ValidationFailedException validation && current is not null)
            {
                errors.AddRange(ValidationErrors(current.RowNumber, current.Fields, validation));
            }
            return new ImportResult(nameof(ImportType.Services), fileName, false, 0,
                skippedInvalid, skippedExisting, sheet.Rows.Count - skippedInvalid - skippedExisting, 0,
                "The import failed and nothing was saved. Preview the file again to check for changes, then retry.", errors);
        }

        return new ImportResult(nameof(ImportType.Services), fileName, true, created,
            skippedInvalid, skippedExisting, 0, 0, null, errors);
    }

    public async Task<byte[]> CreateErrorReportAsync(
        Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var parsed = await ParseAsync(content, fileName, cancellationToken);
            return files.CreateErrorReport(new ImportErrorReportSpec(fileName, [], parsed.Errors));
        }
        catch (ValidationFailedException validation)
        {
            return files.CreateErrorReport(new ImportErrorReportSpec(fileName,
                [.. validation.Errors.SelectMany(pair => pair.Value)], []));
        }
    }

    private Task<ParsedFile> ParseAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ParseAsync(ImportFileReader.Read(files, content, fileName, Descriptor), cancellationToken);
    }

    private async Task<ParsedFile> ParseAsync(ImportSheet sheet, CancellationToken cancellationToken)
    {
        var categoryLookup = (await categories.QueryAsync(ServiceStatusFilter.All, cancellationToken))
            .ToLookup(category => category.Name.Trim().ToLowerInvariant());
        var staffLookup = (await staffDirectory.GetSelectableAsync(includeInactive: true, cancellationToken))
            .ToLookup(member => $"{member.FirstName} {member.LastName}".Trim().ToLowerInvariant());
        var firstRows = new Dictionary<(Guid CategoryId, string Name), int>();
        var rows = new List<ParsedRow>();
        var errors = new List<ImportFieldError>();

        foreach (var row in sheet.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = row.Cells.Select(ImportValues.Display).ToArray();
            var rowErrors = new List<ImportFieldError>();
            void Fail(int column, string message) => rowErrors.Add(FieldError(row.RowNumber, column, message, fields[column]));

            for (var column = 0; column < Descriptor.Headers.Count; column++)
            {
                if (row.Cells[column].HasFormula)
                    Fail(column, "Formulas are not allowed. Enter a plain value.");
            }

            var name = ImportValues.Text(row.Cells[0]);
            var categoryName = ImportValues.Text(row.Cells[1]);
            var matchingCategories = categoryLookup[categoryName?.ToLowerInvariant() ?? string.Empty].ToList();
            Guid? categoryId = matchingCategories.Count == 1 ? matchingCategories[0].Id : null;
            if (categoryName is null)
                Fail(1, "Category name is required. Choose an existing category.");
            else if (matchingCategories.Count == 0)
                Fail(1, "No category matches this name. Use an existing category; imports never create categories.");
            else if (matchingCategories.Count > 1)
                Fail(1, "More than one category matches this name. Resolve the ambiguity before importing.");

            if (!ImportValues.TryGetDecimal(row.Cells[2], out var price, out var priceError))
                Fail(2, priceError!);
            var duration = 0;
            if (!ImportValues.TryGetDecimal(row.Cells[3], out var minutes, out var durationError))
                Fail(3, durationError!);
            else if (minutes != decimal.Truncate(minutes) || minutes < int.MinValue || minutes > int.MaxValue)
                Fail(3, "Duration must be a whole number of minutes from 1 to 1440.");
            else
                duration = (int)minutes;

            var active = true;
            if (!ImportValues.IsBlank(row.Cells[5]) && !ImportValues.TryGetBool(row.Cells[5], out active, out var activeError))
                Fail(5, activeError!);

            var staffIds = new HashSet<Guid>();
            var staffNames = ImportValues.Text(row.Cells[6]);
            if (staffNames is not null)
            {
                foreach (var staffName in staffNames.Split(';', StringSplitOptions.TrimEntries))
                {
                    var matches = staffLookup[staffName.ToLowerInvariant()].ToList();
                    if (matches.Count != 1)
                        Fail(6, matches.Count == 0
                            ? $"No staff member matches '{staffName}'. Use existing full names separated by semicolons."
                            : $"More than one staff member matches '{staffName}'. Assign staff manually after importing this service as inactive with an empty Eligible staff cell.");
                    else
                        staffIds.Add(matches[0].Id);
                }
            }

            for (var column = 4; column <= 6; column += 2)
            {
                if (!ImportValues.IsBlank(row.Cells[column]) && row.Cells[column].Text is null)
                    Fail(column, "Enter plain text.");
            }

            var request = new SaveServiceRequest(name, categoryId, ImportValues.Text(row.Cells[4]), duration,
                price, active, IsOnlineBookable: false, SortOrder: 0, EligibleStaffIds: [.. staffIds]);
            var status = ImportRowStatus.Valid;
            try
            {
                await catalogue.ValidateCreateAsync(request, cancellationToken);
            }
            catch (ValidationFailedException validation)
            {
                rowErrors.AddRange(ValidationErrors(row.RowNumber, fields, validation)
                    .Where(error => !rowErrors.Any(existing => existing.Field == error.Field)));
            }
            catch (ConflictException)
            {
                status = ImportRowStatus.AlreadyExists;
            }

            if (rowErrors.Count > 0)
            {
                status = ImportRowStatus.Invalid;
            }
            else
            {
                var key = (categoryId!.Value, name!.ToLowerInvariant());
                if (firstRows.TryGetValue(key, out var firstRow))
                {
                    status = ImportRowStatus.DuplicateInFile;
                    Fail(0, $"Same service name and category as row {firstRow}; this row will be skipped.");
                }
                else
                {
                    firstRows[key] = row.RowNumber;
                    if (status == ImportRowStatus.AlreadyExists)
                        Fail(0, "This service name already exists in the category; it will be skipped. Existing prices and details are never overwritten.");
                }
            }

            if (!rowErrors.Any(error => error.Field == Descriptor.Headers[5]))
                fields[5] = active ? "Yes" : "No";
            rows.Add(new ParsedRow(row.RowNumber, request, status, fields));
            errors.AddRange(rowErrors);
        }

        return new ParsedFile(rows, errors);
    }

    private static ImportFieldError FieldError(int rowNumber, int column, string message, string rawValue) =>
        new(rowNumber, Descriptor.Headers[column], message, rawValue);

    private static IEnumerable<ImportFieldError> ValidationErrors(
        int rowNumber, IReadOnlyList<string> fields, ValidationFailedException validation) =>
        validation.Errors.SelectMany(pair => pair.Value.Select(message =>
            FieldError(rowNumber, FieldColumns[pair.Key], message, fields[FieldColumns[pair.Key]])));

    private sealed record ParsedRow(int RowNumber, SaveServiceRequest Request, ImportRowStatus Status, IReadOnlyList<string> Fields);
    private sealed record ParsedFile(IReadOnlyList<ParsedRow> Rows, IReadOnlyList<ImportFieldError> Errors);
}
