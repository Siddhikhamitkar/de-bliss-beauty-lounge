using DeBliss.Domain.Common;

namespace DeBliss.Domain.Inventory;

/// <summary>
/// A product the salon stocks, either for resale (<see cref="InventoryType.Retail"/>)
/// or for use behind the chair (<see cref="InventoryType.SalonUse"/>). Current stock
/// is the sum of every movement ever recorded against the product and is changed
/// only through <see cref="ApplyStockChange"/>, which keeps it consistent with the
/// movement history and bumps the concurrency token that prevents two users from
/// overselling the same units at the same time.
/// </summary>
public class InventoryProduct : AuditableEntity
{
    public string Name { get; set; } = null!;

    public string? Brand { get; set; }

    /// <summary>
    /// Stored trimmed and upper-cased so matching is case-insensitive and a SKU
    /// can exist only once, exactly like coupon codes.
    /// </summary>
    public string Sku { get; set; } = null!;

    public string? Category { get; set; }

    public InventoryType InventoryType { get; set; }

    /// <summary>What a unit is measured in, such as pcs, ml or g. Fractional quantities are allowed.</summary>
    public string UnitOfMeasure { get; set; } = null!;

    /// <summary>Required for retail products; salon-use stock is not priced for sale.</summary>
    public decimal? CostPrice { get; set; }

    /// <summary>What the customer is charged per unit. Required for retail products; ignored for salon-use.</summary>
    public decimal? SellingPrice { get; set; }

    public decimal CurrentStock { get; set; }

    /// <summary>The level at or below which the list flags the product as running low.</summary>
    public decimal MinimumStock { get; set; }

    /// <summary>
    /// Retired products keep their movement history; deactivating hides them from
    /// sale but never deletes them.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Optimistic-concurrency token: every stock change assigns a fresh value, so
    /// the UPDATE that writes one change fails when another change was saved in
    /// between — the database-level guarantee that stock is never oversold. A
    /// client-maintained token rather than a SQL Server rowversion so the model
    /// behaves identically on every provider.
    /// </summary>
    public Guid StockVersion { get; set; }

    /// <summary>
    /// The single way stock changes. Callers validate their movement's rules and
    /// build the audit row; this keeps the arithmetic and the token bump that
    /// must always accompany them from drifting apart.
    /// </summary>
    public void ApplyStockChange(decimal quantityChange)
    {
        if (CurrentStock + quantityChange < 0m)
        {
            throw new InvalidOperationException(
                $"Stock of '{Name}' cannot go below zero.");
        }

        CurrentStock += quantityChange;
        StockVersion = Guid.NewGuid();
    }
}
