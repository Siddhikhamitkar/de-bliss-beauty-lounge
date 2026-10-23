using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Imports.Dtos;
using DeBliss.Application.Inventory;
using DeBliss.Application.Inventory.Dtos;
using DeBliss.Application.Inventory.Ports;
using DeBliss.Domain.Inventory;

namespace DeBliss.Application.Imports;

public interface IInventoryImportService
{
    byte[] CreateTemplate(ImportType type);

    /// <summary>Reads and judges a workbook without writing anything.</summary>
    Task<ImportPreview> PreviewAsync(
        Stream content,
        string fileName,
        ImportType type,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the products and their opening stock. Rows whose SKU already
    /// exists are always skipped — the import never updates or overwrites a
    /// product. Opening stock is never written onto the shelf: each product
    /// with an opening quantity gets an audited OpeningStock movement through
    /// the inventory service, with the acting user and the import time on it.
    /// </summary>
    Task<ImportResult> ExecuteAsync(
        Stream content,
        string fileName,
        ImportType type,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<byte[]> CreateErrorReportAsync(
        Stream content,
        string fileName,
        ImportType type,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Excel import for retail and salon-use products. Row rules mirror
/// <see cref="InventoryService"/>'s exactly — retail needs a cost and a selling
/// price, salon-use products are never priced, stock quantities carry at most
/// three decimals — and persistence goes through the inventory service, so
/// every product and every movement behaves exactly as if typed into the
/// product form and the stock-receipt form.
/// </summary>
public sealed class InventoryImportService(
    IImportFiles files,
    IInventoryRepository inventory,
    IInventoryService inventoryService,
    IUnitOfWork unitOfWork) : IInventoryImportService
{
    public byte[] CreateTemplate(ImportType type) =>
        files.CreateTemplate(ImportSheets.Template(type));

    public async Task<ImportPreview> PreviewAsync(
        Stream content,
        string fileName,
        ImportType type,
        CancellationToken cancellationToken = default)
    {
        var parsed = await ParseAsync(content, fileName, type, cancellationToken);

        return new ImportPreview(
            type.ToString(),
            fileName,
            parsed.Rows.Count,
            parsed.Rows.Count(row => row.Status == ImportRowStatus.Valid),
            parsed.Rows.Count(row => row.Status == ImportRowStatus.Invalid),
            parsed.Rows.Count(row => row.Status == ImportRowStatus.DuplicateInFile),
            parsed.Rows.Count(row => row.Status == ImportRowStatus.AlreadyExists),
            parsed.Rows.Count(row => row.Status != ImportRowStatus.Invalid && row.OpeningStock > 0m),
            [.. parsed.Rows.Take(ImportRules.PreviewRowCount).Select(ToPreviewRow)],
            parsed.Errors);
    }

    public async Task<ImportResult> ExecuteAsync(
        Stream content,
        string fileName,
        ImportType type,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var parsed = await ParseAsync(content, fileName, type, cancellationToken);

        var created = 0;
        var skippedInvalid = 0;
        var skippedExisting = 0;
        var movements = 0;
        var attempted = 0;

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

                // Inventory skips duplicates and existing SKUs without an
                // override: there is no import path that rewrites a product.
                if (row.Status is ImportRowStatus.DuplicateInFile or ImportRowStatus.AlreadyExists)
                {
                    skippedExisting++;
                    continue;
                }

                if (await inventory.SkuExistsAsync(row.Sku!, null, cancellationToken))
                {
                    skippedExisting++;
                    continue;
                }

                attempted++;

                try
                {
                    var product = await inventoryService.CreateAsync(
                        new SaveProductRequest(
                            row.Name,
                            row.Brand,
                            row.Sku,
                            row.Category,
                            type == ImportType.RetailProducts ? InventoryType.Retail.ToString() : InventoryType.SalonUse.ToString(),
                            row.UnitOfMeasure,
                            row.CostPrice,
                            row.SellingPrice,
                            row.MinimumStock,
                            IsActive: row.Active),
                        cancellationToken);

                    created++;

                    if (row.OpeningStock > 0m)
                    {
                        await inventoryService.ReceiveStockAsync(
                            product.Id,
                            new ReceiveStockRequest(
                                row.OpeningStock,
                                ReceivedAt: null,
                                Reference: ImportRules.MovementReference,
                                Notes: TruncateNotes($"Opening stock imported from '{fileName}'"),
                                Kind: StockMovementType.OpeningStock.ToString()),
                            actorUserId,
                            cancellationToken);

                        movements++;
                    }
                }
                catch (ConflictException)
                {
                    // Lost a race inside the transaction: the SKU was claimed
                    // by a concurrent save. Skipped, never overwritten.
                    skippedExisting++;
                    attempted--;
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            return new ImportResult(
                type.ToString(),
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
            type.ToString(),
            fileName,
            Succeeded: true,
            created,
            skippedInvalid,
            skippedExisting,
            Failed: 0,
            movements,
            Message: null,
            parsed.Errors);
    }

    public async Task<byte[]> CreateErrorReportAsync(
        Stream content,
        string fileName,
        ImportType type,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parsed = await ParseAsync(content, fileName, type, cancellationToken);

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

    private async Task<ParsedFile> ParseAsync(
        Stream content,
        string fileName,
        ImportType type,
        CancellationToken cancellationToken)
    {
        var descriptor = ImportSheets.Describe(type);
        var sheet = ImportFileReader.Read(files, content, fileName, descriptor);
        var retail = type == ImportType.RetailProducts;

        var rows = new List<ParsedRow>(sheet.Rows.Count);
        var errors = new List<ImportFieldError>();
        var firstRowWithSku = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in sheet.Rows)
        {
            var parsedRow = ParseRow(row, descriptor.Headers, retail);

            if (parsedRow.Sku is { } sku && parsedRow.Status != ImportRowStatus.Invalid)
            {
                if (firstRowWithSku.TryGetValue(sku, out var firstRow))
                {
                    var duplicate = parsedRow with
                    {
                        Status = ImportRowStatus.DuplicateInFile,
                        Errors =
                        [
                            .. parsedRow.Errors,
                            new ImportFieldError(
                                row.RowNumber,
                                "SKU",
                                $"Same SKU as row {firstRow}. Only one of them can be imported.",
                                ImportValues.Display(row.Cells[retail ? RetailColumns.Sku : SalonUseColumns.Sku]))
                        ]
                    };

                    rows.Add(duplicate);
                    errors.AddRange(duplicate.Errors);
                    continue;
                }

                firstRowWithSku[sku] = row.RowNumber;
            }

            rows.Add(parsedRow);
            errors.AddRange(parsedRow.Errors);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (row.Status != ImportRowStatus.Valid || row.Sku is not { } sku)
            {
                continue;
            }

            if (!await inventory.SkuExistsAsync(sku, null, cancellationToken))
            {
                continue;
            }

            var skuColumn = retail ? RetailColumns.Sku : SalonUseColumns.Sku;

            var existing = row with
            {
                Status = ImportRowStatus.AlreadyExists,
                Errors =
                [
                    .. row.Errors,
                    new ImportFieldError(
                        row.RowNumber,
                        "SKU",
                        $"A product with the SKU '{sku}' already exists; the import skips it rather than overwriting anything.",
                        ImportValues.Display(sheet.Rows.First(sheetRow => sheetRow.RowNumber == row.RowNumber).Cells[skuColumn]))
                ]
            };

            rows[i] = existing;
            errors.Add(existing.Errors[^1]);
        }

        return new ParsedFile(rows, errors);
    }

    private ParsedRow ParseRow(ImportRow row, IReadOnlyList<string> headers, bool retail)
    {
        var nameColumn = retail ? RetailColumns.Name : SalonUseColumns.Name;
        var brandColumn = retail ? RetailColumns.Brand : SalonUseColumns.Brand;
        var skuColumn = retail ? RetailColumns.Sku : SalonUseColumns.Sku;
        var categoryColumn = retail ? RetailColumns.Category : SalonUseColumns.Category;
        var minimumColumn = retail ? RetailColumns.MinimumStock : SalonUseColumns.MinimumStock;
        var openingColumn = retail ? RetailColumns.OpeningStock : SalonUseColumns.OpeningStock;
        var activeColumn = retail ? RetailColumns.Active : SalonUseColumns.Active;

        var label = string.Empty;
        var errors = new List<ImportFieldError>();

        void Fail(int column, string message)
        {
            errors.Add(new ImportFieldError(row.RowNumber, headers[column], message, ImportValues.Display(row.Cells[column])));
        }

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

        var name = ImportValues.Text(row.Cells[nameColumn]);

        if (name is null)
        {
            Fail(nameColumn, "Product name is required.");
        }
        else if (name.Length > InventoryLimits.NameMaxLength)
        {
            Fail(nameColumn, $"Product name must be {InventoryLimits.NameMaxLength} characters or fewer.");
        }

        var brand = ImportValues.Text(row.Cells[brandColumn]);

        if (brand?.Length > InventoryLimits.BrandMaxLength)
        {
            Fail(brandColumn, $"Brand must be {InventoryLimits.BrandMaxLength} characters or fewer.");
        }

        var sku = ImportValues.Text(row.Cells[skuColumn])?.ToUpperInvariant();

        if (sku is null)
        {
            Fail(skuColumn, "SKU is required.");
        }
        else if (sku.Length > InventoryLimits.SkuMaxLength)
        {
            Fail(skuColumn, $"SKU must be {InventoryLimits.SkuMaxLength} characters or fewer.");
        }

        var category = ImportValues.Text(row.Cells[categoryColumn]);

        if (category?.Length > InventoryLimits.CategoryMaxLength)
        {
            Fail(categoryColumn, $"Category must be {InventoryLimits.CategoryMaxLength} characters or fewer.");
        }

        decimal? costPrice = null;
        decimal? sellingPrice = null;

        if (retail)
        {
            var costCell = row.Cells[RetailColumns.CostPrice];

            if (ImportValues.IsBlank(costCell))
            {
                Fail(RetailColumns.CostPrice, "Retail products need a cost price.");
            }
            else if (!ImportValues.TryGetDecimal(costCell, out var cost, out var costError))
            {
                Fail(RetailColumns.CostPrice, costError!);
            }
            else if (cost < 0m)
            {
                Fail(RetailColumns.CostPrice, "Cost price cannot be negative.");
            }
            else if (ImportValues.ExceedsDecimalPlaces(cost, 2))
            {
                Fail(RetailColumns.CostPrice, "Cost price can have at most 2 decimal places.");
            }
            else
            {
                costPrice = cost;
            }

            var sellingCell = row.Cells[RetailColumns.SellingPrice];

            if (ImportValues.IsBlank(sellingCell))
            {
                Fail(RetailColumns.SellingPrice, "Retail products need a selling price.");
            }
            else if (!ImportValues.TryGetDecimal(sellingCell, out var selling, out var sellingError))
            {
                Fail(RetailColumns.SellingPrice, sellingError!);
            }
            else if (selling <= 0m)
            {
                Fail(RetailColumns.SellingPrice, "Selling price must be greater than zero.");
            }
            else if (ImportValues.ExceedsDecimalPlaces(selling, 2))
            {
                Fail(RetailColumns.SellingPrice, "Selling price can have at most 2 decimal places.");
            }
            else
            {
                sellingPrice = selling;
            }
        }

        string? unitOfMeasure = null;

        if (!retail)
        {
            unitOfMeasure = ImportValues.Text(row.Cells[SalonUseColumns.UnitOfMeasure]);

            if (unitOfMeasure?.Length > InventoryLimits.UnitOfMeasureMaxLength)
            {
                Fail(SalonUseColumns.UnitOfMeasure, $"Unit must be {InventoryLimits.UnitOfMeasureMaxLength} characters or fewer.");
                unitOfMeasure = null;
            }
        }

        decimal? minimumStock = null;

        if (!ImportValues.IsBlank(row.Cells[minimumColumn]))
        {
            if (!ImportValues.TryGetDecimal(row.Cells[minimumColumn], out var minimum, out var minimumError))
            {
                Fail(minimumColumn, minimumError!);
            }
            else if (minimum < 0m)
            {
                Fail(minimumColumn, "Minimum stock cannot be negative.");
            }
            else if (ImportValues.ExceedsDecimalPlaces(minimum, 3))
            {
                Fail(minimumColumn, "Minimum stock can have at most 3 decimal places.");
            }
            else
            {
                minimumStock = minimum;
            }
        }

        decimal? openingStock = null;

        if (!ImportValues.IsBlank(row.Cells[openingColumn]))
        {
            if (!ImportValues.TryGetDecimal(row.Cells[openingColumn], out var opening, out var openingError))
            {
                Fail(openingColumn, openingError!);
            }
            else if (opening < 0m)
            {
                Fail(openingColumn, "Opening stock cannot be negative.");
            }
            else if (ImportValues.ExceedsDecimalPlaces(opening, 3))
            {
                Fail(openingColumn, "Opening stock can have at most 3 decimal places.");
            }
            else
            {
                openingStock = opening;
            }
        }

        var active = true;

        if (!ImportValues.IsBlank(row.Cells[activeColumn])
            && !ImportValues.TryGetBool(row.Cells[activeColumn], out active, out var activeError))
        {
            Fail(activeColumn, activeError!);
        }

        label = name ?? string.Empty;

        return new ParsedRow(
            row.RowNumber,
            label,
            name,
            brand,
            sku,
            category,
            costPrice,
            sellingPrice,
            unitOfMeasure,
            minimumStock,
            openingStock,
            active,
            errors.Count == 0 ? ImportRowStatus.Valid : ImportRowStatus.Invalid,
            errors,
            [.. row.Cells.Select(ImportValues.Display)]);
    }

    private static ImportPreviewRow ToPreviewRow(ParsedRow row) =>
        new(row.RowNumber, row.Label, row.Status.ToString(), row.DisplayFields);

    private static string TruncateNotes(string notes) =>
        notes.Length > InventoryLimits.NotesMaxLength ? notes[..InventoryLimits.NotesMaxLength] : notes;

    private sealed record ParsedFile(
        IReadOnlyList<ParsedRow> Rows,
        IReadOnlyList<ImportFieldError> Errors);

    private sealed record ParsedRow(
        int RowNumber,
        string Label,
        string? Name,
        string? Brand,
        string? Sku,
        string? Category,
        decimal? CostPrice,
        decimal? SellingPrice,
        string? UnitOfMeasure,
        decimal? MinimumStock,
        decimal? OpeningStock,
        bool Active,
        ImportRowStatus Status,
        IReadOnlyList<ImportFieldError> Errors,
        IReadOnlyList<string> DisplayFields);
}
