using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Inventory.Dtos;
using DeBliss.Application.Inventory.Ports;
using DeBliss.Domain.Inventory;

namespace DeBliss.Application.Inventory;

/// <summary>
/// The inventory use cases: product management and the append-only stock
/// ledger. Stock never changes except through a recorded movement, and every
/// quantity is validated here rather than trusted from the browser. Receipts
/// default to the current date but may be backdated to the delivery day; the
/// "last stock added" the list shows always describes the most recent thing the
/// salon received, never the most recent reduction. Mistakes are corrected with
/// an audited reversal, never by editing history.
/// </summary>
public sealed class InventoryService(
    IInventoryRepository inventory,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IInventoryService
{
    public async Task<PagedResult<InventoryProductDto>> SearchAsync(
        InventoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalized();

        var (items, totalCount) = await inventory.SearchAsync(normalized, cancellationToken);

        // The last-added pair comes from the movement history, not a column on
        // the product, so reductions can never overwrite it.
        var additions = await inventory.GetLatestStockAdditionsAsync(
            [.. items.Select(product => product.Id)], cancellationToken);

        return new PagedResult<InventoryProductDto>(
            [.. items.Select(product => Map(product, additions.GetValueOrDefault(product.Id)))],
            totalCount,
            normalized.Page,
            normalized.PageSize);
    }

    public async Task<InventoryProductDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await LoadAsync(id, cancellationToken);

        var additions = await inventory.GetLatestStockAdditionsAsync([id], cancellationToken);

        return Map(product, additions.GetValueOrDefault(id));
    }

    public async Task<IReadOnlyList<InventoryProductOptionDto>> GetSellableAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var products = await inventory.ListSellableAsync(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(), cancellationToken);

        return [.. products.Select(product => new InventoryProductOptionDto(
            product.Id,
            product.Name,
            product.Brand,
            product.Sku,
            product.SellingPrice!.Value,
            product.CurrentStock,
            product.UnitOfMeasure))];
    }

    public async Task<InventoryProductDto> CreateAsync(
        SaveProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateForSaveAsync(request, excludeId: null, cancellationToken);

        // A new product starts empty: the system never invents an opening
        // stock figure, the owner records one as an explicit movement.
        var product = new InventoryProduct
        {
            Name = validated.Name,
            Brand = validated.Brand,
            Sku = validated.Sku,
            Category = validated.Category,
            InventoryType = validated.InventoryType,
            UnitOfMeasure = validated.UnitOfMeasure,
            CostPrice = validated.CostPrice,
            SellingPrice = validated.SellingPrice,
            MinimumStock = validated.MinimumStock,
            CurrentStock = 0m,
            IsActive = request.IsActive ?? true,
            StockVersion = Guid.NewGuid()
        };

        inventory.Add(product);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(product, null);
    }

    public async Task<InventoryProductDto> UpdateAsync(
        Guid id,
        SaveProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateForSaveAsync(request, excludeId: id, cancellationToken);

        var product = await LoadAsync(id, cancellationToken);

        product.Name = validated.Name;
        product.Brand = validated.Brand;
        product.Sku = validated.Sku;
        product.Category = validated.Category;
        product.InventoryType = validated.InventoryType;
        product.UnitOfMeasure = validated.UnitOfMeasure;
        product.CostPrice = validated.CostPrice;
        product.SellingPrice = validated.SellingPrice;
        product.MinimumStock = validated.MinimumStock;

        // Current stock is deliberately absent: it changes only through
        // movements, so an edit form can never quietly restate it.
        if (request.IsActive is not null)
        {
            product.IsActive = request.IsActive.Value;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(product, null);
    }

    public async Task<InventoryProductDto> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var product = await LoadAsync(id, cancellationToken);

        product.IsActive = isActive;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(product, null);
    }

    public async Task<StockMovementDto> ReceiveStockAsync(
        Guid productId,
        ReceiveStockRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var product = await LoadAsync(productId, cancellationToken);

        var errors = new ValidationErrorBuilder();

        var quantity = ValidatePositiveQuantity(request.Quantity, errors);
        var receivedAt = ValidateNotFuture(request.ReceivedAt, errors);
        var kind = ParseReceiptKind(request.Kind, errors);
        var reference = ValidateOptionalLength(request.Reference, InventoryLimits.ReferenceMaxLength, "reference", errors);
        var notes = ValidateOptionalLength(request.Notes, InventoryLimits.NotesMaxLength, "notes", errors);

        errors.ThrowIfInvalid();

        return await RecordMovementAsync(
            product,
            kind!.Value,
            quantity!.Value,
            receivedAt,
            reference,
            notes,
            invoiceId: null,
            reversalOf: null,
            actorUserId,
            cancellationToken);
    }

    public async Task<StockMovementDto> RecordUsageAsync(
        Guid productId,
        RecordUsageRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var product = await LoadAsync(productId, cancellationToken);

        var errors = new ValidationErrorBuilder();

        var quantity = ValidatePositiveQuantity(request.Quantity, errors);
        var usageType = ParseUsageType(request.UsageType, errors);
        var reference = ValidateOptionalLength(request.Reference, InventoryLimits.ReferenceMaxLength, "reference", errors);
        var notes = ValidateRequiredReason(request.Notes, errors);

        // A salon-use-only rule: the front desk never records usage, and the
        // owner does not waste stock they sell.
        if (product.InventoryType == InventoryType.Retail)
        {
            errors.Add("usageType", "Retail products leave stock through sales, not usage.");
        }

        errors.ThrowIfInvalid();

        return await RecordMovementAsync(
            product,
            usageType!.Value,
            -quantity!.Value,
            timeProvider.GetUtcNow(),
            reference,
            notes,
            invoiceId: null,
            reversalOf: null,
            actorUserId,
            cancellationToken);
    }

    public async Task<StockMovementDto> AdjustStockAsync(
        Guid productId,
        AdjustStockRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var product = await LoadAsync(productId, cancellationToken);

        var errors = new ValidationErrorBuilder();

        var reference = ValidateOptionalLength(request.Reference, InventoryLimits.ReferenceMaxLength, "reference", errors);
        var notes = ValidateRequiredReason(request.Notes, errors);

        var change = request.QuantityChange;

        if (change is null)
        {
            errors.Add("quantityChange", "Enter the adjustment.");
        }
        else if (change.Value == 0m)
        {
            errors.Add("quantityChange", "An adjustment must change the stock level.");
        }
        else if (product.CurrentStock + change.Value < 0m)
        {
            errors.Add(
                "quantityChange",
                $"Adjustment would take '{product.Name}' below zero ({product.CurrentStock} {product.UnitOfMeasure} in stock).");
        }

        errors.ThrowIfInvalid();

        return await RecordMovementAsync(
            product,
            StockMovementType.Adjustment,
            change!.Value,
            timeProvider.GetUtcNow(),
            reference,
            notes,
            invoiceId: null,
            reversalOf: null,
            actorUserId,
            cancellationToken);
    }

    public async Task<StockMovementDto> ReverseMovementAsync(
        Guid productId,
        Guid movementId,
        ReverseStockMovementRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var product = await LoadAsync(productId, cancellationToken);

        var movement = await inventory.GetMovementAsync(productId, movementId, cancellationToken)
            ?? throw new NotFoundException($"Stock movement '{movementId}' was not found.");

        if (movement.MovementType == StockMovementType.Reversal)
        {
            throw new ConflictException("A reversal cannot itself be reversed.");
        }

        if (await inventory.IsReversedAsync(movementId, cancellationToken))
        {
            throw new ConflictException($"Movement '{movementId}' has already been reversed.");
        }

        var errors = new ValidationErrorBuilder();

        var notes = ValidateRequiredReason(request.Notes, errors);

        if (product.CurrentStock - movement.QuantityChange < 0m)
        {
            errors.Add(
                "notes",
                $"Reversing this movement would take '{product.Name}' below zero ({product.CurrentStock} {product.UnitOfMeasure} in stock).");
        }

        errors.ThrowIfInvalid();

        return await RecordMovementAsync(
            product,
            StockMovementType.Reversal,
            -movement.QuantityChange,
            timeProvider.GetUtcNow(),
            movement.Reference,
            notes,
            movement.InvoiceId,
            reversalOf: movement,
            actorUserId,
            cancellationToken);
    }

    public async Task<PagedResult<StockMovementDto>> GetMovementsAsync(
        Guid productId,
        InventoryMovementQuery query,
        CancellationToken cancellationToken = default)
    {
        await LoadAsync(productId, cancellationToken);

        var normalized = query.Normalized();

        var (items, totalCount) = await inventory.SearchMovementsAsync(normalized, cancellationToken);

        return new PagedResult<StockMovementDto>(
            [.. items.Select(Map)],
            totalCount,
            normalized.Page,
            normalized.PageSize);
    }

    private async Task<StockMovementDto> RecordMovementAsync(
        InventoryProduct product,
        StockMovementType type,
        decimal quantityChange,
        DateTimeOffset occurredAt,
        string? reference,
        string? notes,
        Guid? invoiceId,
        StockMovement? reversalOf,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var before = product.CurrentStock;

        product.ApplyStockChange(quantityChange);

        var movement = new StockMovement
        {
            ProductId = product.Id,
            MovementType = type,
            QuantityChange = quantityChange,
            StockBefore = before,
            StockAfter = product.CurrentStock,
            OccurredAt = occurredAt,
            Reference = reference,
            Notes = notes,
            InvoiceId = invoiceId,
            PerformedByUserId = actorUserId,
            ReversalOfMovementId = reversalOf?.Id
        };

        inventory.AddMovement(movement);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Reload with the performer and invoice links so the returned DTO
        // shows exactly what the history screen will show for this row.
        var stored = await inventory.GetMovementAsync(product.Id, movement.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Stock movement '{movement.Id}' was not found after saving.");

        return Map(stored);
    }

    private async Task<InventoryProduct> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await inventory.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Product '{id}' was not found.");
    }

    private async Task<ValidatedProduct> ValidateForSaveAsync(
        SaveProductRequest request,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrorBuilder();

        var name = request.Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            errors.Add("name", "Product name is required.");
        }
        else if (name.Length > InventoryLimits.NameMaxLength)
        {
            errors.Add("name", $"Product name must be {InventoryLimits.NameMaxLength} characters or fewer.");
        }

        var brand = OptionalTrimmed(request.Brand);

        if (brand?.Length > InventoryLimits.BrandMaxLength)
        {
            errors.Add("brand", $"Brand must be {InventoryLimits.BrandMaxLength} characters or fewer.");
        }

        // The stored form is trimmed and upper-cased; validating the stored
        // form is what keeps the unique index and the duplicate check aligned.
        var sku = request.Sku?.Trim().ToUpperInvariant() ?? string.Empty;

        if (sku.Length == 0)
        {
            errors.Add("sku", "SKU is required.");
        }
        else if (sku.Length > InventoryLimits.SkuMaxLength)
        {
            errors.Add("sku", $"SKU must be {InventoryLimits.SkuMaxLength} characters or fewer.");
        }

        var category = OptionalTrimmed(request.Category);

        if (category?.Length > InventoryLimits.CategoryMaxLength)
        {
            errors.Add("category", $"Category must be {InventoryLimits.CategoryMaxLength} characters or fewer.");
        }

        InventoryType? inventoryType = null;

        if (string.IsNullOrWhiteSpace(request.InventoryType)
            || !Enum.TryParse<InventoryType>(request.InventoryType.Trim(), ignoreCase: true, out var parsedType)
            || !Enum.IsDefined(parsedType))
        {
            errors.Add("inventoryType", "Select Retail or SalonUse.");
        }
        else
        {
            inventoryType = parsedType;
        }

        // The unit is what the owner counts in — pcs, ml, g — and defaults to
        // pieces rather than demanding a choice for every simple product.
        var unit = string.IsNullOrWhiteSpace(request.UnitOfMeasure)
            ? InventoryLimits.DefaultUnitOfMeasure
            : request.UnitOfMeasure.Trim();

        if (unit.Length > InventoryLimits.UnitOfMeasureMaxLength)
        {
            errors.Add("unitOfMeasure", $"Unit must be {InventoryLimits.UnitOfMeasureMaxLength} characters or fewer.");
        }

        var minimumStock = request.MinimumStock ?? 0m;

        if (minimumStock < 0m)
        {
            errors.Add("minimumStock", "Minimum stock cannot be negative.");
        }

        // Retail products carry the money the salon trades on; salon-use
        // products are never priced for sale, so a price on one is a mistake
        // rather than something to silently store.
        if (inventoryType == InventoryType.Retail)
        {
            if (request.CostPrice is null)
            {
                errors.Add("costPrice", "Retail products need a cost price.");
            }
            else if (request.CostPrice < 0m)
            {
                errors.Add("costPrice", "Cost price cannot be negative.");
            }

            if (request.SellingPrice is null)
            {
                errors.Add("sellingPrice", "Retail products need a selling price.");
            }
            else if (request.SellingPrice <= 0m)
            {
                errors.Add("sellingPrice", "Selling price must be greater than zero.");
            }
        }
        else if (inventoryType == InventoryType.SalonUse
            && (request.CostPrice is not null || request.SellingPrice is not null))
        {
            errors.Add("sellingPrice", "Salon-use products are not priced for sale.");
        }

        errors.ThrowIfInvalid();

        if (await inventory.SkuExistsAsync(sku, excludeId, cancellationToken))
        {
            throw new ConflictException($"A product with the SKU '{sku}' already exists.");
        }

        return new ValidatedProduct(
            name,
            brand,
            sku,
            category,
            inventoryType!.Value,
            unit,
            request.CostPrice,
            request.SellingPrice,
            minimumStock);
    }

    private decimal? ValidatePositiveQuantity(decimal? quantity, ValidationErrorBuilder errors)
    {
        if (quantity is null)
        {
            errors.Add("quantity", "Enter the quantity.");

            return null;
        }

        if (quantity.Value <= 0m)
        {
            errors.Add("quantity", "Quantity must be greater than zero.");

            return null;
        }

        return quantity;
    }

    private DateTimeOffset ValidateNotFuture(DateTimeOffset? occurredAt, ValidationErrorBuilder errors)
    {
        var value = occurredAt ?? timeProvider.GetUtcNow();

        if (value > timeProvider.GetUtcNow())
        {
            errors.Add("receivedAt", "Received date cannot be in the future.");
        }

        return value;
    }

    private static StockMovementType? ParseReceiptKind(string? kind, ValidationErrorBuilder errors)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return StockMovementType.StockReceived;
        }

        if (Enum.TryParse<StockMovementType>(kind.Trim(), ignoreCase: true, out var parsed)
            && (parsed == StockMovementType.StockReceived || parsed == StockMovementType.OpeningStock))
        {
            return parsed;
        }

        errors.Add("kind", "Kind must be StockReceived or OpeningStock.");

        return null;
    }

    private static StockMovementType? ParseUsageType(string? usageType, ValidationErrorBuilder errors)
    {
        if (!string.IsNullOrWhiteSpace(usageType)
            && Enum.TryParse<StockMovementType>(usageType.Trim(), ignoreCase: true, out var parsed)
            && (parsed == StockMovementType.Consumption || parsed == StockMovementType.Wastage))
        {
            return parsed;
        }

        errors.Add("usageType", "Select Consumption or Wastage.");

        return null;
    }

    private static string? ValidateOptionalLength(string? value, int maxLength, string field, ValidationErrorBuilder errors)
    {
        var trimmed = OptionalTrimmed(value);

        if (trimmed?.Length > maxLength)
        {
            errors.Add(field, $"{char.ToUpperInvariant(field[0])}{field[1..]} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? ValidateRequiredReason(string? value, ValidationErrorBuilder errors)
    {
        var trimmed = OptionalTrimmed(value);

        if (string.IsNullOrEmpty(trimmed))
        {
            errors.Add("notes", "Enter a reason.");
        }
        else if (trimmed.Length > InventoryLimits.NotesMaxLength)
        {
            errors.Add("notes", $"Reason must be {InventoryLimits.NotesMaxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? OptionalTrimmed(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static InventoryProductDto Map(InventoryProduct product, StockMovement? lastAddition) => new(
        product.Id,
        product.Name,
        product.Brand,
        product.Sku,
        product.Category,
        product.InventoryType.ToString(),
        product.UnitOfMeasure,
        product.CostPrice,
        product.SellingPrice,
        product.CurrentStock,
        product.MinimumStock,
        StockStatus(product),
        product.IsActive,
        lastAddition?.OccurredAt,
        lastAddition?.QuantityChange,
        product.CreatedAt,
        product.UpdatedAt);

    private static string StockStatus(InventoryProduct product)
    {
        if (product.CurrentStock <= 0m)
        {
            return "OutOfStock";
        }

        return product.CurrentStock <= product.MinimumStock ? "LowStock" : "InStock";
    }

    private static StockMovementDto Map(StockMovement movement) => new(
        movement.Id,
        movement.ProductId,
        movement.MovementType.ToString(),
        movement.QuantityChange,
        movement.StockBefore,
        movement.StockAfter,
        movement.OccurredAt,
        movement.Reference,
        movement.Notes,
        movement.InvoiceId,
        movement.Invoice?.InvoiceNumber,
        movement.PerformedByUserId,
        movement.PerformedByUser is { } user ? $"{user.FirstName} {user.LastName}" : null,
        movement.ReversalOfMovementId,
        movement.CreatedAt);

    /// <summary>The validated form of a save request, ready to become product fields.</summary>
    private sealed record ValidatedProduct(
        string Name,
        string? Brand,
        string Sku,
        string? Category,
        InventoryType InventoryType,
        string UnitOfMeasure,
        decimal? CostPrice,
        decimal? SellingPrice,
        decimal MinimumStock);
}
