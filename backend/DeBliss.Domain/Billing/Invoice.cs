using DeBliss.Domain.Common;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Domain.Billing;

/// <summary>
/// A billable checkout. An invoice is what is owed; <see cref="Payment"/> is what
/// was actually tendered, and the two are deliberately separate concepts so that
/// part payments, refunds and multiple tender types can be represented.
/// </summary>
public class Invoice : AuditableEntity, ISoftDeletable
{
    public string InvoiceNumber { get; set; } = null!;

    /// <summary>
    /// Null for a sale that is not tied to a booking, such as a retail product
    /// purchase. The convention of one invoice per checkout is enforced in the
    /// application layer rather than by a unique constraint, so that split or
    /// deposit-plus-final billing stays possible later.
    /// </summary>
    public Guid? AppointmentId { get; set; }

    public Appointment? Appointment { get; set; }

    public Guid? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public InvoiceStatus Status { get; set; }

    /// <summary>ISO 4217 code. Set by the application from salon configuration.</summary>
    public string Currency { get; set; } = null!;

    public decimal SubTotal { get; set; }

    public decimal DiscountTotal { get; set; }

    public decimal TaxTotal { get; set; }

    public decimal TotalAmount { get; set; }

    public DateTimeOffset? IssuedAt { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public string? Notes { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
