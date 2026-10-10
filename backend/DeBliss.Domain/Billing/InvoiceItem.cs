using DeBliss.Domain.Common;
using DeBliss.Domain.Inventory;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;

namespace DeBliss.Domain.Billing;

/// <summary>
/// A single charged line on an invoice. <see cref="UnitPrice"/> records what was
/// actually charged, which may differ from both the current catalogue price and
/// the price snapshotted at booking time.
/// </summary>
public class InvoiceItem : AuditableEntity
{
    public Guid InvoiceId { get; set; }

    public Invoice Invoice { get; set; } = null!;

    /// <summary>Traces the line back to the booked service, when it came from one.</summary>
    public Guid? AppointmentServiceId { get; set; }

    public AppointmentService? AppointmentService { get; set; }

    public Guid? ServiceId { get; set; }

    public Service? Service { get; set; }

    /// <summary>
    /// The retail product this line sold, when it came from one. The link lets
    /// checkout deduct the sold units and keeps every sale traceable from the
    /// product's stock history back to the invoice it left on.
    /// </summary>
    public Guid? ProductId { get; set; }

    public InventoryProduct? Product { get; set; }

    public InvoiceItemType ItemType { get; set; }

    public string Description { get; set; } = null!;

    /// <summary>
    /// Decimal rather than int so that fractional units such as time-based or
    /// weighed retail items can be billed without a schema change.
    /// </summary>
    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal LineTotal { get; set; }

    public int SortOrder { get; set; }
}
