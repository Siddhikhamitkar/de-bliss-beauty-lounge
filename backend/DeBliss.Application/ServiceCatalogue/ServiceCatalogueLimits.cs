namespace DeBliss.Application.ServiceCatalogue;

/// <summary>
/// The limits the service menu enforces. They mirror the column lengths in the
/// EF Core configurations so an over-long value is rejected with a clear 400
/// instead of failing inside SQL Server.
/// </summary>
public static class ServiceCatalogueLimits
{
    public const int CategoryNameMaxLength = 100;
    public const int CategoryDescriptionMaxLength = 500;

    public const int ServiceNameMaxLength = 150;
    public const int ServiceDescriptionMaxLength = 1000;

    /// <summary>A single appointment line cannot run longer than a day.</summary>
    public const int MaximumDurationMinutes = 1440;

    /// <summary>
    /// Comfortably below the <c>decimal(18,2)</c> column maximum, so an absurd
    /// keying error is reported as a validation problem rather than overflowing
    /// the column.
    /// </summary>
    public const decimal MaximumPrice = 100_000m;
}
