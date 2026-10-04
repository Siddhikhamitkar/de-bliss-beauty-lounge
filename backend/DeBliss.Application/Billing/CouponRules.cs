using DeBliss.Domain.Billing;

namespace DeBliss.Application.Billing;

/// <summary>
/// The single definition of when a coupon may be applied: it must be active and
/// the day of use must fall inside the inclusive validity window. Both the
/// checkout and the preview endpoint consult this so they can never disagree.
/// </summary>
internal static class CouponRules
{
    public static bool IsApplicable(Coupon coupon, DateOnly today)
    {
        return coupon.IsActive
            && (coupon.ValidFrom is null || today >= coupon.ValidFrom.Value)
            && (coupon.ValidUntil is null || today <= coupon.ValidUntil.Value);
    }
}
