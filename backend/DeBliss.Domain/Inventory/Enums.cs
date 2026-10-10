namespace DeBliss.Domain.Inventory;

/// <summary>
/// What a product is stocked for. A product must have exactly one type: retail
/// products are sold to customers through billing, salon-use products are
/// consumed delivering services and can never be sold as-is.
/// </summary>
public enum InventoryType
{
    Retail = 0,

    SalonUse = 1
}

/// <summary>
/// Why stock changed. The list is append-only history: correcting a mistake
/// adds a <see cref="Reversal"/> of the mistaken movement rather than editing
/// it, so the audit trail of what actually happened is never rewritten.
/// </summary>
public enum StockMovementType
{
    /// <summary>An explicit initial count recorded by the owner. Never invented by the system.</summary>
    OpeningStock = 0,

    /// <summary>Stock delivered into the salon.</summary>
    StockReceived = 1,

    /// <summary>Stock removed by a completed retail sale, at checkout.</summary>
    RetailSale = 2,

    /// <summary>Salon-use stock used delivering services.</summary>
    Consumption = 3,

    /// <summary>Salon-use stock lost, spoiled or unusable.</summary>
    Wastage = 4,

    /// <summary>A signed correction, such as reconciling to a physical count.</summary>
    Adjustment = 5,

    /// <summary>The undoing of a mistaken movement; the original stays untouched.</summary>
    Reversal = 6
}
