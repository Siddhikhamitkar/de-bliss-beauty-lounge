using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Settings.Dtos;
using DeBliss.Application.Settings.Ports;
using DeBliss.Domain.Settings;

namespace DeBliss.Application.Settings;

/// <summary>
/// The salon's tax configuration. Values live in the singleton settings row;
/// reads fall back to the same defaults the migration seeds (5% added at the
/// till), so a database that has never been configured still quotes the
/// salon's standard rate. Every consumer snapshots what it uses — a checkout
/// stores the rate it charged — so changing a setting here never rewrites a
/// finished bill.
/// </summary>
public sealed class SettingsService(
    ISalonSettingsRepository settings,
    IUnitOfWork unitOfWork) : ISettingsService
{
    public async Task<TaxSettingsDto> GetTaxAsync(CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken) ?? DefaultSettings();

        return new TaxSettingsDto(current.TaxRate, current.PricesIncludeTax);
    }

    public async Task<TaxSettingsDto> UpdateTaxAsync(
        SaveTaxSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrorBuilder();

        if (request.TaxRate is null)
        {
            errors.Add("taxRate", "Enter the VAT rate.");
        }
        else if (request.TaxRate.Value < 0m || request.TaxRate.Value > 100m)
        {
            errors.Add("taxRate", "Enter a VAT rate between 0 and 100.");
        }

        if (request.PricesIncludeTax is null)
        {
            errors.Add("pricesIncludeTax", "Choose whether prices already include VAT.");
        }

        errors.ThrowIfInvalid();

        // Upsert: the row is seeded by the migration, but a database created
        // without it (a test database, or one restored partially) still needs
        // a first save to work.
        var current = await settings.GetTrackedAsync(cancellationToken);

        if (current is null)
        {
            current = DefaultSettings();
            settings.Add(current);
        }

        current.TaxRate = request.TaxRate!.Value;
        current.PricesIncludeTax = request.PricesIncludeTax!.Value;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TaxSettingsDto(current.TaxRate, current.PricesIncludeTax);
    }

    private static SalonSettings DefaultSettings() => new()
    {
        Id = SalonSettings.SingletonId,
        TaxRate = 5m,
        PricesIncludeTax = false
    };
}
