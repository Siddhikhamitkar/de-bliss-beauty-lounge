using DeBliss.Domain.Common;
using DeBliss.Domain.Identity;

namespace DeBliss.Domain.Billing;

/// <summary>
/// An amount tendered against an invoice. Refunds are recorded as separate rows
/// pointing back at the original payment rather than by editing or deleting the
/// original, so the financial history stays intact.
/// </summary>
public class Payment : AuditableEntity, ISoftDeletable
{
    public Guid InvoiceId { get; set; }

    public Invoice Invoice { get; set; } = null!;

    public decimal Amount { get; set; }

    /// <summary>ISO 4217 code. Set by the application from salon configuration.</summary>
    public string Currency { get; set; } = null!;

    public PaymentMethod Method { get; set; }

    public PaymentStatus Status { get; set; }

    /// <summary>Gateway or card terminal reference, when applicable.</summary>
    public string? Reference { get; set; }

    public DateTimeOffset PaidAt { get; set; }

    /// <summary>Set on a refund row to identify the payment being refunded.</summary>
    public Guid? RefundOfPaymentId { get; set; }

    public Payment? RefundOfPayment { get; set; }

    public Guid? ReceivedByUserId { get; set; }

    public User? ReceivedByUser { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
