using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing;

public sealed class StaffAvailabilityService(
    IStaffRepository staff,
    IServiceRepository services,
    IScheduleRepository schedule,
    IAppointmentConflictPort appointments) : IStaffAvailabilityService
{
    public async Task<StaffAvailabilityCheckDto> CheckAsync(
        Guid staffId,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? excludeAppointmentId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWindow(start, end);

        var member = await staff.GetByIdAsync(staffId, cancellationToken)
            ?? throw new NotFoundException($"Staff member '{staffId}' was not found.");

        var isAvailable = await IsAvailableAsync(member, serviceId: null, start, end, excludeAppointmentId, cancellationToken);

        return new StaffAvailabilityCheckDto(staffId, start, end, isAvailable);
    }

    public async Task<IReadOnlyList<StaffOptionDto>> GetAvailableStaffAsync(
        Guid serviceId,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        EnsureWindow(start, end);

        _ = await services.GetByIdAsync(serviceId, cancellationToken)
            ?? throw new NotFoundException($"Service '{serviceId}' was not found.");

        var candidates = await staff.GetActiveAssignedToServiceAsync(serviceId, cancellationToken);

        var available = new List<Staff>();

        foreach (var member in candidates)
        {
            if (await IsAvailableAsync(member, serviceId, start, end, excludeAppointmentId: null, cancellationToken))
            {
                available.Add(member);
            }
        }

        return [.. available.Select(MapOption)];
    }

    private static void EnsureWindow(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["end"] = ["End must be after the start."]
            });
        }
    }

    /// <summary>
    /// The six rules, in the order cheapest to most expensive to check:
    /// active, assigned to the service, the slot sits inside one working
    /// interval of the right weekday (breaks fall between intervals and so
    /// exclude the slot), no time off overlaps it, no blocked period overlaps
    /// it, and — added by the appointment module — no overlapping appointment
    /// with a line assigned to this member. A reschedule passes its own id as
    /// excludeAppointmentId so the appointment being moved never conflicts
    /// with itself.
    /// </summary>
    private async Task<bool> IsAvailableAsync(
        Staff member,
        Guid? serviceId,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? excludeAppointmentId,
        CancellationToken cancellationToken)
    {
        if (!member.IsActive)
        {
            return false;
        }

        if (serviceId is { } requiredServiceId
            && !member.StaffServices.Any(line => line.ServiceId == requiredServiceId))
        {
            return false;
        }

        // A slot ending exactly at midnight belongs to the previous day.
        var dayEnd = end.TimeOfDay == TimeSpan.Zero ? end.AddTicks(-1) : end;
        var sameDay = start.Year == dayEnd.Year && start.Month == dayEnd.Month && start.Day == dayEnd.Day;

        if (!sameDay)
        {
            return false;
        }

        var endTime = end.TimeOfDay == TimeSpan.Zero ? TimeSpan.FromHours(24) : end.TimeOfDay;
        var covered = member.WorkingHours.Any(row =>
            row.DayOfWeek == start.DayOfWeek
            && row.StartTime <= start.TimeOfDay
            && endTime <= row.EndTime);

        if (!covered)
        {
            return false;
        }

        if ((await schedule.GetOverlappingTimeOffAsync(member.Id, start, end, cancellationToken)).Count > 0)
        {
            return false;
        }

        if ((await schedule.GetOverlappingBlockedTimesAsync(member.Id, start, end, cancellationToken)).Count > 0)
        {
            return false;
        }

        if (await appointments.HasConflictingAppointmentAsync(member.Id, start, end, excludeAppointmentId, cancellationToken))
        {
            return false;
        }

        return true;
    }

    private static StaffOptionDto MapOption(Staff member) => new(
        member.Id,
        member.FirstName,
        member.LastName,
        member.JobTitle,
        member.IsActive);
}
