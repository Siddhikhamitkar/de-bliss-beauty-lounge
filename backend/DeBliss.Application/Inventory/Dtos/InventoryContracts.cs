namespace DeBliss.Application.Inventory.Dtos;

/// <summary>How the inventory list is narrowed by product type. Sent by name, as the API binds it.</summary>
public enum InventoryTypeFilter
{
    All = 0,
    Retail = 1,
    SalonUse = 2
}

/// <summary>How the inventory list is narrowed by the product's active state.</summary>
public enum InventoryProductStatusFilter
{
    All = 0,
    Active = 1,
    Inactive = 2
}

/// <summary>
/// How the inventory list is narrowed by stock level. Low stock means at or
/// below the product's minimum; a product with no minimum set is never low.
/// </summary>
public enum InventoryStockStatusFilter
{
    All = 0,
    InStock = 1,
    LowStock = 2,
    OutOfStock = 3
}

/// <summary>How a product's movement history is narrowed by movement type.</summary>
public enum StockMovementTypeFilter
{
    All = 0,
    OpeningStock = 1,
    StockReceived = 2,
    RetailSale = 3,
    Consumption = 4,
    Wastage = 5,
    Adjustment = 6,
    Reversal = 7
}

/// <summary>
/// Everything the inventory list can be narrowed by, normalised the same way
/// the other list queries in the system are.
/// </summary>
public sealed record InventoryQuery(
    string? Search,
    InventoryTypeFilter Type,
    InventoryProductStatusFilter Status,
    InventoryStockStatusFilter StockStatus,
    string? Category,
    int Page,
    int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public InventoryQuery Normalized()
    {
        var pageSize = PageSize <= 0 ? DefaultPageSize : Math.Min(PageSize, MaximumPageSize);

        return this with
        {
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            Category = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim(),
            Page = Math.Max(Page, 1),
            PageSize = pageSize
        };
    }
}

/// <summary>
/// One row of the inventory list and the product drawer's overview: the stock
/// the salon holds, the level it should not fall below, and — straight from the
/// movement history — when stock last arrived and how much that delivery was.
/// Reductions never touch the last-added pair: it always describes the most
/// recent thing the salon received, not the most recent thing that happened.
/// </summary>
public sealed record InventoryProductDto(
    Guid Id,
    string Name,
    string? Brand,
    string Sku,
    string? Category,
    string InventoryType,
    string UnitOfMeasure,
    decimal? CostPrice,
    decimal? SellingPrice,
    decimal CurrentStock,
    decimal MinimumStock,
    string StockStatus,
    bool IsActive,
    DateTimeOffset? LastStockAddedAt,
    decimal? LastAddedQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// One entry in a product's append-only stock history: what changed, the stock
/// on both sides of the change, when, why, by whom, and the invoice the stock
/// left through when it was a sale.
/// </summary>
public sealed record StockMovementDto(
    Guid Id,
    Guid ProductId,
    string MovementType,
    decimal QuantityChange,
    decimal StockBefore,
    decimal StockAfter,
    DateTimeOffset OccurredAt,
    string? Reference,
    string? Notes,
    Guid? InvoiceId,
    string? InvoiceNumber,
    Guid? PerformedByUserId,
    string? PerformedByName,
    Guid? ReversalOfMovementId,
    DateTimeOffset CreatedAt);

/// <summary>
/// The movement history query for one product, newest first.
/// </summary>
public sealed record InventoryMovementQuery(
    Guid ProductId,
    StockMovementTypeFilter Type,
    int Page,
    int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public InventoryMovementQuery Normalized()
    {
        var pageSize = PageSize <= 0 ? DefaultPageSize : Math.Min(PageSize, MaximumPageSize);

        return this with
        {
            Page = Math.Max(Page, 1),
            PageSize = pageSize
        };
    }
}

/// <summary>
/// Payload for creating and editing a product. Every field is nullable because
/// the form is filled incrementally and the server owns the rules. Retail
/// products must carry a cost and a selling price; salon-use products must not,
/// because they are never priced for sale.
/// </summary>
public sealed record SaveProductRequest(
    string? Name,
    string? Brand,
    string? Sku,
    string? Category,
    string? InventoryType,
    string? UnitOfMeasure,
    decimal? CostPrice,
    decimal? SellingPrice,
    decimal? MinimumStock,
    bool? IsActive);

/// <summary>Body of the product activate/deactivate endpoint.</summary>
public sealed record SetProductStatusRequest(bool IsActive);

/// <summary>
/// Body of "stock arrived". Quantity is what physically came in; the date
/// defaults to the current business date and may be backdated to the delivery
/// day; reference captures the supplier or delivery note. Kind distinguishes a
/// normal delivery from an explicit opening count — the system never invents
/// the latter, only the owner records it.
/// </summary>
public sealed record ReceiveStockRequest(
    decimal? Quantity,
    DateTimeOffset? ReceivedAt,
    string? Reference,
    string? Notes,
    string? Kind);

/// <summary>
/// Body of "salon-use stock was used or lost". UsageType is Consumption or
/// Wastage; a reason is required so the history can explain itself.
/// </summary>
public sealed record RecordUsageRequest(
    decimal? Quantity,
    string? UsageType,
    string? Reference,
    string? Notes);

/// <summary>
/// Body of "the stock number is wrong". A signed correction — positive adds
/// units, negative removes them — with a reason, so a physical count can be
/// reconciled without pretending anything was received or wasted.
/// </summary>
public sealed record AdjustStockRequest(
    decimal? QuantityChange,
    string? Reference,
    string? Notes);

/// <summary>Body of "undo that movement". The original row stays untouched; the reversal is its own audited row.</summary>
public sealed record ReverseStockMovementRequest(string? Notes);

/// <summary>
/// One sellable product for the checkout screen's product picker: the active
/// retail catalogue with its price and the stock actually on the shelf.
/// </summary>
public sealed record InventoryProductOptionDto(
    Guid Id,
    string Name,
    string? Brand,
    string Sku,
    decimal SellingPrice,
    decimal CurrentStock,
    string UnitOfMeasure);
