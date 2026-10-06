using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing;

/// <summary>
/// What a staff member sees about themselves on mobile. Nothing here is staff
/// configuration: the recurring weekly pattern and the dated time off are read
/// through <see cref="IStaffScheduleService"/> so the owner keeps a single place
/// to maintain them, and bookings arrive through the scheduling module's port
/// rather than a second query path.
/// </summary>
public sealed class StaffMobileService(
    IStaffRepository staff,
    IStaffScheduleService schedule,
    IStaffAppointmentPort appointments,
    TimeProvider timeProvider) : IStaffMobileService
{
    internal const int UpcomingWindowDays = 31;
    internal const int HistoryWindowDays = 90;
    internal const int ScheduleWindowDays = 7;

    public async Task<StaffMobileProfileDto> GetProfileAsync(Guid staffId, CancellationToken cancellationToken = default)
    {
        var member = await LoadAsync(staffId, cancellationToken);

        return new StaffMobileProfileDto(
            member.Id,
            member.FirstName,
            member.LastName,
            member.Email,
            member.PhoneNumber,
            member.JobTitle,
            member.IsActive);
    }

    public async Task<IReadOnlyList<StaffAppointmentDto>> GetAppointmentsAsync(
        Guid staffId,
        StaffAppointmentScope scope,
        CancellationToken cancellationToken = default)
    {
        await LoadAsync(staffId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var startOfToday = new DateTimeOffset(
            DateOnly.FromDateTime(now.UtcDateTime).ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);

        var (from, to) = scope switch
        {
            StaffAppointmentScope.Today => (startOfToday, startOfToday.AddDays(1)),
            StaffAppointmentScope.Upcoming => (now, now.AddDays(UpcomingWindowDays)),
            StaffAppointmentScope.History => (now.AddDays(-HistoryWindowDays), now),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };

        var found = await appointments.GetForStaffInRangeAsync(staffId, from, to, cancellationToken);

        return scope == StaffAppointmentScope.History
            ? [.. found.Select(entry => Map(entry, staffId)).OrderByDescending(entry => entry.StartsAt)]
            : [.. found.Select(entry => Map(entry, staffId)).OrderBy(entry => entry.StartsAt)];
    }

    public async Task<StaffScheduleDto> GetScheduleAsync(Guid staffId, CancellationToken cancellationToken = default)
    {
        var weekly = await schedule.GetWeekScheduleAsync(staffId, cancellationToken);
        var timeOff = await schedule.GetTimeOffAsync(staffId, cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var pattern = weekly.Days.ToDictionary(day => day.DayOfWeek, StringComparer.Ordinal);

        var days = new List<StaffShiftDayDto>(ScheduleWindowDays);

        for (var offset = 0; offset < ScheduleWindowDays; offset++)
        {
            var date = today.AddDays(offset);
            var dayName = Enum.GetName(date.DayOfWeek)!;

            pattern.TryGetValue(dayName, out var planned);

            days.Add(new StaffShiftDayDto(
                date,
                dayName,
                planned?.IsWorkingDay ?? false,
                planned?.Intervals ?? [],
                [.. timeOff.Where(entry => Covers(entry, date))]));
        }

        return new StaffScheduleDto(today, days, weekly);
    }

    private async Task<Staff> LoadAsync(Guid staffId, CancellationToken cancellationToken)
    {
        return await staff.GetByIdForScheduleAsync(staffId, cancellationToken)
            ?? throw new NotFoundException($"Staff member '{staffId}' was not found.");
    }

    private static bool Covers(StaffTimeOffDto entry, DateOnly date)
    {
        var starts = DateOnly.FromDateTime(entry.StartsAt.UtcDateTime);
        var ends = DateOnly.FromDateTime(entry.EndsAt.UtcDateTime);

        return starts <= date && date <= ends;
    }

    /// <summary>
    /// Only the lines this staff member personally performs are projected, so a
    /// shared booking never names a colleague, and no price is carried — a team
    /// member sees what to do and when, not what the salon charges.
    /// </summary>
    private static StaffAppointmentDto Map(Appointment appointment, Guid staffId)
    {
        var lines = appointment.Services
            .Where(line => line.StaffId == staffId)
            .OrderBy(line => line.SortOrder)
            .Select(line => new StaffAppointmentServiceDto(
                line.ServiceId,
                line.ServiceNameSnapshot,
                line.DurationMinutes,
                line.Notes))
            .ToList();

        var customerName = appointment.Customer is { } customer
            ? $"{customer.FirstName} {customer.LastName}"
            : appointment.WalkInCustomerName ?? "Walk-in";

        return new StaffAppointmentDto(
            appointment.Id,
            appointment.StartsAt,
            appointment.EndsAt,
            Enum.GetName(appointment.Status)!,
            customerName,
            appointment.Notes,
            lines.Sum(line => line.DurationMinutes),
            appointment.ServiceBundleId,
            appointment.ServiceBundle?.Name,
            lines);
    }
}
