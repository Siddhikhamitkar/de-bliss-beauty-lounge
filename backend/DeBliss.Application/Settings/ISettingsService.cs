using DeBliss.Application.Settings.Dtos;

namespace DeBliss.Application.Settings;

public interface ISettingsService
{
    Task<TaxSettingsDto> GetTaxAsync(CancellationToken cancellationToken = default);

    Task<TaxSettingsDto> UpdateTaxAsync(
        SaveTaxSettingsRequest request,
        CancellationToken cancellationToken = default);
}
