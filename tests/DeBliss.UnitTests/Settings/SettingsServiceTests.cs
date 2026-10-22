using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Settings;
using DeBliss.Application.Settings.Dtos;
using DeBliss.Domain.Settings;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Settings;

/// <summary>
/// The salon's tax configuration: a singleton row that the owner edits, with
/// the same defaults the migration seeds quoted when the row is missing, so a
/// database that has never been configured still charges the standard rate.
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly DeBlissDbContext _context;
    private readonly ISettingsService _settings;

    public SettingsServiceTests()
    {
        _context = _database.CreateContext();
        _settings = new SettingsService(new SalonSettingsRepository(_context), _context);
    }

    [Fact]
    public async Task A_database_with_no_settings_row_quotes_the_seeded_defaults()
    {
        var tax = await _settings.GetTaxAsync();

        Assert.Equal(5m, tax.TaxRate);
        Assert.False(tax.PricesIncludeTax);
    }

    [Fact]
    public async Task Saving_writes_the_singleton_row_and_reads_it_back()
    {
        var saved = await _settings.UpdateTaxAsync(new SaveTaxSettingsRequest(7.5m, true));

        Assert.Equal(7.5m, saved.TaxRate);
        Assert.True(saved.PricesIncludeTax);

        await using var verify = _database.CreateContext();
        var row = await verify.SalonSettings.SingleAsync();

        Assert.Equal(SalonSettings.SingletonId, row.Id);
        Assert.Equal(7.5m, row.TaxRate);
        Assert.True(row.PricesIncludeTax);
    }

    [Fact]
    public async Task A_second_save_overwrites_the_row_rather_than_adding_one()
    {
        await _settings.UpdateTaxAsync(new SaveTaxSettingsRequest(5m, false));
        await _settings.UpdateTaxAsync(new SaveTaxSettingsRequest(0m, true));

        await using var verify = _database.CreateContext();
        var row = await verify.SalonSettings.SingleAsync();

        Assert.Equal(0m, row.TaxRate);
        Assert.True(row.PricesIncludeTax);
    }

    /// <summary>Invalid saves: the rate outside 0–100, or a field missing. Field name last.</summary>
    public static TheoryData<decimal?, bool?, string> InvalidSaves => new()
    {
        { null, null, "taxRate" },
        { -1m, true, "taxRate" },
        { 101m, false, "taxRate" },
        { 5m, null, "pricesIncludeTax" }
    };

    [Theory]
    [MemberData(nameof(InvalidSaves))]
    public async Task An_invalid_save_is_rejected_without_writing_a_row(decimal? rate, bool? inclusive, string field)
    {
        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => _settings.UpdateTaxAsync(new SaveTaxSettingsRequest(rate, inclusive)));

        Assert.Contains(field, failure.Errors.Keys);

        await using var verify = _database.CreateContext();

        Assert.Empty(await verify.SalonSettings.ToListAsync());
    }

    [Theory]
    [MemberData(nameof(ValidRates))]
    public async Task Zero_and_a_hundred_are_valid_rates(decimal rate)
    {
        var saved = await _settings.UpdateTaxAsync(new SaveTaxSettingsRequest(rate, false));

        Assert.Equal(rate, saved.TaxRate);
    }

    public static TheoryData<decimal> ValidRates => new() { 0m, 100m };

    public void Dispose()
    {
        _context.Dispose();
        _database.Dispose();
    }
}
