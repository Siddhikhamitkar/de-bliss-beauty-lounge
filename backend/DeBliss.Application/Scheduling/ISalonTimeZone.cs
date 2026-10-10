namespace DeBliss.Application.Scheduling;

/// <summary>
/// The zone the salon treats as local business time. Working hours, public
/// availability dates and "days ahead" limits are all wall-clock concepts, so
/// they convert through this zone rather than assuming UTC or server-local time.
/// Configured at <c>Scheduling:SalonTimeZone</c> (IANA id); the default is the
/// salon's home zone.
/// </summary>
public interface ISalonTimeZone
{
    TimeZoneInfo Zone { get; }
}
