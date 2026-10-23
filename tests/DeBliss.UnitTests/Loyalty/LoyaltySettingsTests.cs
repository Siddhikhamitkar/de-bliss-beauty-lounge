using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Loyalty.Dtos;
using DeBliss.Domain.Loyalty;

namespace DeBliss.UnitTests.Loyalty;

public sealed class LoyaltySettingsTests
{
    private static readonly SaveLoyaltySettingsRequest Valid = new(true, 5m, 1m, 0m, 50m, true);

    [Fact]
    public async Task Missing_settings_return_disabled_defaults_without_creating_a_row()
    {
        var harness = new LoyaltyHarness();
        var settings = await harness.Service.GetSettingsAsync();

        Assert.Equal(new LoyaltySettingsDto(false, 5m, 1m, 0m, 50m, true, null), settings);
        Assert.Null(harness.Repository.Settings);
        Assert.Equal(0, harness.Repository.SettingsAdded);
        Assert.Equal(0, harness.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Saving_upserts_singleton_tracks_actor_and_rotates_concurrency_token()
    {
        var harness = new LoyaltyHarness();
        var actor = Guid.NewGuid();
        await harness.Service.UpdateSettingsAsync(Valid with { MinimumInvoiceAmount = 100m }, actor);
        var row = Assert.IsType<LoyaltySettings>(harness.Repository.Settings);
        var version = row.Version;

        Assert.Equal(LoyaltySettings.SingletonId, row.Id);
        Assert.Equal(actor, row.UpdatedByUserId);
        Assert.Equal(100m, row.MinimumInvoiceAmount);
        Assert.True(harness.Repository.TrackedSettingsRequested);
        Assert.NotEqual(Guid.Empty, version);

        var nextActor = Guid.NewGuid();
        var saved = await harness.Service.UpdateSettingsAsync(Valid with { EarningPercentage = 7.25m }, nextActor);
        Assert.Equal(7.25m, saved.EarningPercentage);
        Assert.Null(saved.MinimumInvoiceAmount);
        Assert.Same(row, harness.Repository.Settings);
        Assert.Equal(nextActor, row.UpdatedByUserId);
        Assert.NotEqual(version, row.Version);
        Assert.Equal(1, harness.Repository.SettingsAdded);
        Assert.Equal(2, harness.UnitOfWork.Saves);
    }

    public static TheoryData<SaveLoyaltySettingsRequest, string> InvalidSettings => new()
    {
        { Valid with { IsEnabled = null }, "isEnabled" },
        { Valid with { AllowPartialRedemption = null }, "allowPartialRedemption" },
        { Valid with { EarningPercentage = null }, "earningPercentage" },
        { Valid with { EarningPercentage = -0.01m }, "earningPercentage" },
        { Valid with { EarningPercentage = 100.01m }, "earningPercentage" },
        { Valid with { EarningPercentage = 5.001m }, "earningPercentage" },
        { Valid with { PointValueAed = null }, "pointValueAed" },
        { Valid with { PointValueAed = 0m }, "pointValueAed" },
        { Valid with { PointValueAed = -1m }, "pointValueAed" },
        { Valid with { PointValueAed = 1000.0001m }, "pointValueAed" },
        { Valid with { PointValueAed = 0.00001m }, "pointValueAed" },
        { Valid with { MinimumRedemptionPoints = null }, "minimumRedemptionPoints" },
        { Valid with { MinimumRedemptionPoints = -0.01m }, "minimumRedemptionPoints" },
        { Valid with { MinimumRedemptionPoints = 1_000_000_000.01m }, "minimumRedemptionPoints" },
        { Valid with { MinimumRedemptionPoints = 0.001m }, "minimumRedemptionPoints" },
        { Valid with { MaximumRedemptionPercentage = null }, "maximumRedemptionPercentage" },
        { Valid with { MaximumRedemptionPercentage = -0.01m }, "maximumRedemptionPercentage" },
        { Valid with { MaximumRedemptionPercentage = 100.01m }, "maximumRedemptionPercentage" },
        { Valid with { MaximumRedemptionPercentage = 50.001m }, "maximumRedemptionPercentage" },
        { Valid with { MinimumInvoiceAmount = -0.01m }, "minimumInvoiceAmount" },
        { Valid with { MinimumInvoiceAmount = 1_000_000_000.01m }, "minimumInvoiceAmount" },
        { Valid with { MinimumInvoiceAmount = 0.001m }, "minimumInvoiceAmount" },
        { Valid with { PointValueAed = decimal.MaxValue }, "pointValueAed" }
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public async Task Invalid_settings_do_not_write(SaveLoyaltySettingsRequest request, string field)
    {
        var harness = new LoyaltyHarness();
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Service.UpdateSettingsAsync(request));

        Assert.Contains(field, exception.Errors.Keys);
        Assert.Null(harness.Repository.Settings);
        Assert.Equal(0, harness.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Invalid_update_preserves_existing_settings_and_version()
    {
        var harness = new LoyaltyHarness();
        var settings = harness.Enable();
        var version = settings.Version;
        await Assert.ThrowsAsync<ValidationFailedException>(() => harness.Service.UpdateSettingsAsync(
            Valid with { IsEnabled = false, EarningPercentage = 100.01m }));
        Assert.True(settings.IsEnabled);
        Assert.Equal(5m, settings.EarningPercentage);
        Assert.Equal(version, settings.Version);
        Assert.Equal(0, harness.UnitOfWork.Saves);
    }

    public static TheoryData<SaveLoyaltySettingsRequest> BoundarySettings => new()
    {
        new SaveLoyaltySettingsRequest(false, 0m, 0.0001m, 0m, 0m, false, 0m),
        new SaveLoyaltySettingsRequest(true, 100m, 1000m, 1_000_000_000m, 100m, true, 1_000_000_000m),
        new SaveLoyaltySettingsRequest(true, 5.00m, 1.00000m, 0.000m, 50.000m, true)
    };

    [Theory]
    [MemberData(nameof(BoundarySettings))]
    public async Task Inclusive_bounds_and_insignificant_trailing_zeroes_are_valid(SaveLoyaltySettingsRequest request)
    {
        var harness = new LoyaltyHarness();
        var saved = await harness.Service.UpdateSettingsAsync(request);
        Assert.Equal(request.EarningPercentage, saved.EarningPercentage);
        Assert.Equal(request.PointValueAed, saved.PointValueAed);
        Assert.Equal(request.MinimumInvoiceAmount, saved.MinimumInvoiceAmount);
        Assert.Equal(1, harness.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Missing_required_fields_are_reported_together()
    {
        var harness = new LoyaltyHarness();
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(() => harness.Service.UpdateSettingsAsync(
            new SaveLoyaltySettingsRequest(null, null, null, null, null, null)));
        Assert.Equal(6, exception.Errors.Count);
        Assert.DoesNotContain("minimumInvoiceAmount", exception.Errors.Keys);
    }
}
