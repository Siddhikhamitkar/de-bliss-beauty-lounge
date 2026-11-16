using DeBliss.Application.Scheduling;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// A salon zone fixed for tests. Most scheduling tests write their times with a
/// zero offset and mean them as salon wall-clock time, so <see cref="Utc"/> keeps
/// that meaning; zone-specific tests pass a real zone such as Asia/Dubai.
/// </summary>
internal sealed class FixedSalonTimeZone(TimeZoneInfo zone) : ISalonTimeZone
{
    public static FixedSalonTimeZone Utc { get; } = new(TimeZoneInfo.Utc);

    public TimeZoneInfo Zone { get; } = zone;
}
