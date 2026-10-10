using DeBliss.Domain.Billing;
using DeBliss.Domain.Common;
using DeBliss.Domain.Identity;

namespace DeBliss.Domain.Inventory;

/// <summary>
/// One append-only entry in a product's stock history. Stock never changes
/// without a movement recording exactly what changed, by whom, and the stock
/// level on both sides of the change. Rows are never edited or deleted: a
/// mistake is corrected by a <see cref="StockMovementType.Reversal"/> row that
/// points back at the mistaken one through <see cref="ReversalOfMovementId"/>.
/// </summary>
public class StockMovement : AuditableEntity
{
    public Guid ProductId { get; set; }

    public InventoryProduct Product { get; set; } = null!;

    public StockMovementType MovementType { get; set; }

    /// <summary>
    /// Signed: positive adds stock, negative removes it. Adjustment and Reversal
    /// may be either; the other types have a fixed direction.
    /// </summary>
    public decimal QuantityChange { get; set; }

    public decimal StockBefore { get; set; }

    public decimal StockAfter { get; set; }

    /// <summary>
    /// When the stock event happened: the delivery date for received stock (which
    /// may be backdated), the moment of action for everything else.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Supplier or delivery reference for receipts; free reference for the manual movements.</summary>
    public string? Reference { get; set; }

    /// <summary>Why the stock changed, in the actor's words. Required for usage, adjustments and reversals.</summary>
    public string? Notes { get; set; }

    /// <summary>The invoice the stock left through, for retail sales and their reversals.</summary>
    public Guid? InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    /// <summary>The login that performed the action, so history answers "who did this".</summary>
    public Guid? PerformedByUserId { get; set; }

    public User? PerformedByUser { get; set; }

    /// <summary>Set on a reversal row to identify the movement being undone. One reversal per movement, ever.</summary>
    public Guid? ReversalOfMovementId { get; set; }

    public StockMovement? ReversalOf { get; set; }
}
