using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Application.Staffing;

namespace DeBliss.Application.Scheduling;

public interface IPublicAvailabilityService
{
    /// <summary>
    /// Bookable start times for one service on one salon-local day, each with
    /// the staff who can take it. Only genuinely bookable slots come back:
    /// past moments, days beyond the booking horizon and windows that cannot
    /// fit the full service duration are never offered.
    /// </summary>
    Task<PublicAvailabilityDto> GetDayAsync(Guid serviceId, DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>
/// Slot generation on top of the existing availability engine: the day is cut
/// into a fixed grid of candidate starts, and every candidate is answered by
/// <see cref="IStaffAvailabilityService.GetAvailableStaffAsync"/> — the same
/// active/assigned/working-hours/time-off/blocked/appointment rules the front
/// desk books against. No scheduling rule is duplicated here.
/// </summary>
public sealed class PublicAvailabilityService(
    IServiceRepository services,
    IStaffAvailabilityService availability,
    ISalonTimeZone salonTimeZone,
    TimeProvider clock) : IPublicAvailabilityService
{
    /// <summary>The farthest day a customer may ask about, counted in salon-local days.</summary>
    public const int MaximumDaysAhead = 90;

    /// <summary>Candidate starts sit on a half-hour grid anchored at midnight salon-local.</summary>
    public const int SlotStepMinutes = 30;

    public async Task<PublicAvailabilityDto> GetDayAsync(
        Guid serviceId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var service = await services.GetByIdAsync(serviceId, cancellationToken)
            ?? throw new NotFoundException($"Service '{serviceId}' was not found.");

        if (!service.IsActive || service.ServiceCategory is { IsActive: false } || !service.IsOnlineBookable)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["serviceId"] = [$"'{service.Name}' is not available for online booking."]
            });
        }

        var zone = salonTimeZone.Zone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);

        if (date < today)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["date"] = ["Choose today or a future date."]
            });
        }

        if (date > today.AddDays(MaximumDaysAhead))
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["date"] = [$"Choose a date within {MaximumDaysAhead} days from today."]
            });
        }

        var dayStart = ToInstant(date, zone);
        var dayEnd = ToInstant(date.AddDays(1), zone);
        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var now = clock.GetUtcNow();
        var slots = new List<PublicAvailabilitySlotDto>();

        for (var slot = dayStart; slot.Add(duration) <= dayEnd; slot = slot.AddMinutes(SlotStepMinutes))
        {
            if (slot <= now)
            {
                continue;
            }

            var windowEnd = slot.Add(duration);
            var staff = await availability.GetAvailableStaffAsync(service.Id, slot, windowEnd, cancellationToken);

            if (staff.Count > 0)
            {
                slots.Add(new PublicAvailabilitySlotDto(
                    slot,
                    windowEnd,
                    [.. staff.Select(member => new PublicStaffOptionDto(member.Id, $"{member.FirstName} {member.LastName}"))]));
            }
        }

        return new PublicAvailabilityDto(service.Id, date, slots);
    }

    private static DateTimeOffset ToInstant(DateOnly date, TimeZoneInfo zone) =>
        new(date.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue)));
}
