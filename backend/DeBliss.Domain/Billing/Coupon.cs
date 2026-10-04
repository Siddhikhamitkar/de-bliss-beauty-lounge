using DeBliss.Domain.Common;

namespace DeBliss.Domain.Billing;

/// <summary>
/// A discount code the front desk can apply at checkout. Codes are stored trimmed
/// and upper-cased by the application so matching is case-insensitive and a code
/// can exist only once. Validity is a window of whole days so a promotion can be
/// prepared ahead of the day it starts and stops applying the day after it ends.
/// Deactivating a coupon keeps its history; nothing here is ever hard deleted.
/// </summary>
public class Coupon : AuditableEntity
{
    public string Code { get; set; } = null!;

    public DiscountType DiscountType { get; set; }

    /// <summary>
    /// A fixed amount off when <see cref="DiscountType"/> is Fixed, otherwise the
    /// percentage off applied to the invoice subtotal.
    /// </summary>
    public decimal DiscountValue { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Inclusive first day on which the coupon may be applied; null means no start limit.</summary>
    public DateOnly? ValidFrom { get; set; }

    /// <summary>Inclusive last day on which the coupon may be applied; null means no end limit.</summary>
    public DateOnly? ValidUntil { get; set; }
}
