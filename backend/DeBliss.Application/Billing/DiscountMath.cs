using DeBliss.Domain.Billing;

namespace DeBliss.Application.Billing;

/// <summary>
/// The one place discount amounts are computed. The coupon preview endpoint and
/// the checkout both call this so a customer can never be quoted one discount
/// and charged another. Percentages round to fils (two decimal places, away
/// from zero) — the result is a decimal throughout; money is never a double.
/// </summary>
internal static class DiscountMath
{
    public static decimal Compute(decimal subtotal, DiscountType type, decimal value)
    {
        return type == DiscountType.Fixed
            ? value
            : decimal.Round(subtotal * value / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
