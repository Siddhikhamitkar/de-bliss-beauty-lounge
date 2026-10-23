using DeBliss.Domain.Common;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;

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

    /// <summary>The coupon applied at checkout, when one was used.</summary>
    public Guid? CouponId { get; set; }

    public Coupon? Coupon { get; set; }

    /// <summary>
    /// The code as it was on the day of checkout, so a receipt reprinted after the
    /// coupon was renamed or deactivated still shows what the customer presented.
    /// </summary>
    public string? CouponCode { get; set; }

    /// <summary>
    /// The bundle the checked-out appointment was booked as, when it was one.
    /// The component services are still billed as their own lines; this keeps
    /// the bundle as the purchased offering for history and reporting.
    /// </summary>
    public Guid? ServiceBundleId { get; set; }

    public ServiceBundle? ServiceBundle { get; set; }

    /// <summary>The bundle name as it was on the day of checkout, like <see cref="CouponCode"/>.</summary>
    public string? BundleNameSnapshot { get; set; }

    public InvoiceStatus Status { get; set; }

    public int Revision { get; set; }

    /// <summary>ISO 4217 code. Set by the application from salon configuration.</summary>
    public string Currency { get; set; } = null!;

    public decimal SubTotal { get; set; }

    /// <summary>
    /// Total of every discount on the invoice; see <see cref="BundleDiscount"/>,
    /// <see cref="CouponDiscount"/> and <see cref="ManualDiscount"/> for the split.
    /// </summary>
    public decimal DiscountTotal { get; set; }

    /// <summary>Discount from the applied coupon. Stored separately from the manual discount by design.</summary>
    public decimal CouponDiscount { get; set; }

    /// <summary>Discount entered by reception. Stored separately from the coupon discount by design.</summary>
    public decimal ManualDiscount { get; set; }

    /// <summary>
    /// The saving a booked bundle carries against its component subtotal. Part of
    /// the offering's own price, never a manually keyed discount, so it is split
    /// out like <see cref="CouponDiscount"/> and <see cref="ManualDiscount"/>.
    /// </summary>
    public decimal BundleDiscount { get; set; }

    public decimal LoyaltyDiscount { get; set; }

    public decimal LoyaltyPointsRedeemed { get; set; }

    public decimal LoyaltyPointsEarned { get; set; }

    public decimal TaxTotal { get; set; }

    /// <summary>
    /// The tax rate in percent as configured on the day of checkout. Snapshotted
    /// alongside <see cref="TaxTotal"/> so changing the salon's configuration
    /// never rewrites what a finished bill charged.
    /// </summary>
    public decimal TaxRate { get; set; }

    /// <summary>
    /// Whether prices were tax-inclusive on the day of checkout, snapshotted
    /// with <see cref="TaxRate"/> for the same reason.
    /// </summary>
    public bool PricesIncludeTax { get; set; }

    /// <summary>
    /// The discretionary tip added at checkout. Deliberately its own column and
    /// never an invoice line: tips are kept apart from service and retail
    /// revenue, and enter the payable amount exactly once.
    /// </summary>
    public decimal TipAmount { get; set; }

    /// <summary>
    /// The percentage the receptionist picked, when the tip was chosen as a
    /// percentage rather than a keyed amount. Null for a custom amount or no tip.
    /// </summary>
    public decimal? TipPercentage { get; set; }

    /// <summary>
    /// The staff member the tip is attributed to: set only when every service on
    /// the visit was performed by the same one person. A shared or unassigned
    /// visit — and a retail sale — keeps the tip unallocated; an allocation rule
    /// for shared visits is a policy decision that has not been invented here.
    /// </summary>
    public Guid? TipStaffId { get; set; }

    public Staff? TipStaff { get; set; }

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
