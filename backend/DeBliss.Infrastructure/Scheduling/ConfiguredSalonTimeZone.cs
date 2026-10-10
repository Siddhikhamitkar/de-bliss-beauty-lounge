using DeBliss.Application.Scheduling;
using Microsoft.Extensions.Configuration;

namespace DeBliss.Infrastructure.Scheduling;

/// <summary>
/// The salon's business-time zone, read from <see cref="ConfigPath"/>. The
/// default is the salon's home zone; deployment environments override it in
/// configuration rather than in code.
/// </summary>
public sealed class ConfiguredSalonTimeZone(IConfiguration configuration) : ISalonTimeZone
{
    public const string ConfigPath = "Scheduling:SalonTimeZone";

    public const string DefaultZoneId = "Asia/Dubai";

    public TimeZoneInfo Zone { get; } = Resolve(configuration[ConfigPath]);

    private static TimeZoneInfo Resolve(string? configured)
    {
        var id = string.IsNullOrWhiteSpace(configured) ? DefaultZoneId : configured.Trim();

        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }
}
