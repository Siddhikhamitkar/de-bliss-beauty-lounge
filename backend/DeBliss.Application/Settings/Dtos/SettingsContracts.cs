namespace DeBliss.Application.Settings.Dtos;

/// <summary>
/// The salon's tax configuration as the settings screen and every checkout
/// preview see it: the rate in percent and whether displayed prices already
/// contain it.
/// </summary>
public sealed record TaxSettingsDto(decimal TaxRate, bool PricesIncludeTax);

/// <summary>
/// Body of "save the tax configuration". Owner-only at the API. Both fields are
/// nullable so a missing value comes back as a field-level validation error
/// rather than a silent default.
/// </summary>
public sealed record SaveTaxSettingsRequest(decimal? TaxRate, bool? PricesIncludeTax);
