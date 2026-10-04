namespace DeBliss.Application.Billing;

/// <summary>
/// Constants shared across the billing module: the single currency the salon
/// trades in for Phase 1, the invoice numbering scheme and the field limits
/// that the DTO validation and the EF configuration must agree on.
/// </summary>
internal static class BillingLimits
{
    /// <summary>The only currency supported in Phase 1.</summary>
    public const string CurrencyCode = "AED";

    /// <summary>Invoices are numbered INV-{year}-{sequence:D4}.</summary>
    public const string InvoiceNumberPrefix = "INV";

    public const int CouponCodeMaxLength = 50;

    public const int PaymentReferenceMaxLength = 100;

    public const string SalonName = "De Bliss Beauty Lounge";
}
