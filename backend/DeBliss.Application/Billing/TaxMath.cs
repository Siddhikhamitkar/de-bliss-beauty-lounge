namespace DeBliss.Application.Billing;

/// <summary>
/// The one place VAT amounts are computed, so the checkout and every preview
/// quote the same figure. Exclusive prices carry the tax on top of what is
/// displayed (net × rate / 100); inclusive prices already contain it
/// (net × rate / (100 + rate)). Rounds to fils away from zero like every
/// other money figure in the module.
/// </summary>
internal static class TaxMath
{
    public static decimal Compute(decimal netAmount, decimal ratePercent, bool pricesIncludeTax)
    {
        if (ratePercent <= 0m)
        {
            return 0m;
        }

        return pricesIncludeTax
            ? decimal.Round(netAmount * ratePercent / (100m + ratePercent), 2, MidpointRounding.AwayFromZero)
            : decimal.Round(netAmount * ratePercent / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
