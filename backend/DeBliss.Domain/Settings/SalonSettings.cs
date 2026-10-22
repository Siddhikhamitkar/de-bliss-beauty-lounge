using DeBliss.Domain.Common;

namespace DeBliss.Domain.Settings;

/// <summary>
/// The salon's operating configuration, held as a single row. Modules read
/// their values here and snapshot what mattered onto the records they produce,
/// so a settings change never rewrites what was already charged.
/// </summary>
public class SalonSettings : AuditableEntity
{
    /// <summary>
    /// The single row's fixed identity, so reads, upserts and the seeded
    /// migration row all agree on which record is "the settings".
    /// </summary>
    public static readonly Guid SingletonId = Guid.Parse("6d2f8b41-9c57-4e3a-b8d4-1f0a7e5c9b26");

    /// <summary>The VAT rate applied at checkout, in percent (0–100).</summary>
    public decimal TaxRate { get; set; }

    /// <summary>
    /// Whether the prices the salon displays already contain VAT (inclusive)
    /// or whether VAT is added at the till (exclusive).
    /// </summary>
    public bool PricesIncludeTax { get; set; }
}
