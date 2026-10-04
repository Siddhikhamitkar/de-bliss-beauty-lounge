using DeBliss.Application.Dashboard.Dtos;
using DeBliss.Application.Dashboard.Ports;
using DeBliss.Application.Scheduling.Ports;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Dashboard;

/// <summary>
/// Builds the receptionist's read-only view of the day in one round trip.
/// It composes the scheduling, staffing, and customer ports the modules
/// already expose plus the dashboard read store's cross-cutting queries;
/// it deliberately exposes no money fields so the front-desk role cannot
/// see owner-level data.
/// </summary>
public sealed class ReceptionistDashboardService(
    IAppointmentRepository appointments,
    IStaffRepository staff,
    IScheduleRepository schedule,
    IDashboardReadStore dashboardStore,
    TimeProvider timeProvider) : IReceptionistDashboardService
{
    private static readonly HashSet<AppointmentStatus> LiveStatuses =
    [
        AppointmentStatus.Confirmed,
        AppointmentStatus.CheckedIn,
        AppointmentStatus.InProgress
    ];

    public async Task<ReceptionistDashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetLocalNow();
        var dayStart = new DateTimeOffset(now.Date, now.Offset);
        var dayEnd = dayStart.AddDays(1);

        var todaysAppointments = await appointments.GetInRangeAsync(dayStart, dayEnd, cancellationToken);
        var staffMembers = await staff.GetActiveAsync(cancellationToken);

        var schedule = todaysAppointments
            .OrderBy(appointment => appointment.StartsAt)
            .Select(MapAppointment)
            .ToList();

        var upcoming = todaysAppointments
            .Where(appointment =>
                appointment.StartsAt > now && LiveStatuses.Contains(appointment.Status))
            .OrderBy(appointment => appointment.StartsAt)
            .Take(5)
            .Select(MapAppointment)
            .ToList();

        var liveAppointments = todaysAppointments
            .Where(appointment =>
                LiveStatuses.Contains(appointment.Status) &&
                appointment.StartsAt <= now && appointment.EndsAt > now)
            .ToList();

        var staffStatuses = new List<DashboardStaffDto>(staffMembers.Count);
        foreach (var member in staffMembers)
        {
            staffStatuses.Add(await MapStaffAsync(member, now, dayStart, liveAppointments, cancellationToken));
        }

        var alerts = await MapAlertsAsync(dayStart, dayEnd, cancellationToken);

        var popularServices = todaysAppointments
            .Where(appointment => appointment.Status != AppointmentStatus.Cancelled &&
                                  appointment.Status != AppointmentStatus.NoShow)
            .SelectMany(appointment => appointment.Services)
            .GroupBy(line => line.ServiceNameSnapshot)
            .Select(group => new DashboardPopularServiceDto(group.Key, group.Count()))
            .OrderByDescending(service => service.Count)
            .ThenBy(service => service.Name, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        return new ReceptionistDashboardDto(
            now,
            MapToday(todaysAppointments, now),
            schedule,
            upcoming,
            staffStatuses,
            alerts,
            popularServices);
    }

    private static DashboardTodayDto MapToday(IReadOnlyCollection<Appointment> todaysAppointments, DateTimeOffset now)
    {
        return new DashboardTodayDto(
            DateOnly.FromDateTime(now.Date),
            todaysAppointments.Count,
            todaysAppointments.Count(appointment => appointment.Status == AppointmentStatus.Completed),
            todaysAppointments.Count(appointment =>
                appointment.Status is AppointmentStatus.CheckedIn or AppointmentStatus.InProgress),
            todaysAppointments.Count(appointment =>
                appointment.StartsAt > now &&
                appointment.Status is AppointmentStatus.Confirmed or AppointmentStatus.CheckedIn or AppointmentStatus.InProgress),
            todaysAppointments.Count(appointment => appointment.Status == AppointmentStatus.NoShow),
            todaysAppointments.Count(appointment => appointment.Status == AppointmentStatus.Cancelled),
            todaysAppointments.Count(appointment => appointment.CustomerId is null));
    }

    private static DashboardAppointmentDto MapAppointment(Appointment appointment)
    {
        var services = appointment.Services.OrderBy(line => line.SortOrder).ToList();

        var staffNames = services
            .Select(line => line.Staff)
            .Where(member => member is not null)
            .Select(member => member!.FirstName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new DashboardAppointmentDto(
            appointment.Id,
            appointment.CustomerId,
            appointment.Customer is not null
                ? $"{appointment.Customer.FirstName} {appointment.Customer.LastName}"
                : appointment.WalkInCustomerName ?? "Walk-in",
            appointment.StartsAt,
            appointment.EndsAt,
            appointment.Status.ToString(),
            string.Join(" + ", services.Select(line => line.ServiceNameSnapshot)),
            staffNames.Count > 0 ? string.Join(", ", staffNames) : null,
            services.Select(line => line.Staff?.CalendarColor).FirstOrDefault(color => color is not null),
            appointment.Status == AppointmentStatus.Completed);
    }

    private async Task<DashboardStaffDto> MapStaffAsync(
        Staff member,
        DateTimeOffset now,
        DateTimeOffset dayStart,
        IReadOnlyCollection<Appointment> liveAppointments,
        CancellationToken cancellationToken)
    {
        var timeOff = await schedule.GetOverlappingTimeOffAsync(member.Id, now, now.AddMinutes(1), cancellationToken);
        if (timeOff.Count > 0)
        {
            return new DashboardStaffDto(
                member.Id, member.FirstName, member.LastName, member.JobTitle, member.CalendarColor,
                "Off", timeOff[0].Reason);
        }

        if (liveAppointments.Any(appointment =>
                appointment.Services.Any(line => line.StaffId == member.Id)))
        {
            return new DashboardStaffDto(
                member.Id, member.FirstName, member.LastName, member.JobTitle, member.CalendarColor,
                "WithClient", null);
        }

        var workingHours = await schedule.GetWorkingHoursAsync(member.Id, cancellationToken);
        var todaysIntervals = workingHours
            .Where(hour => hour.DayOfWeek == now.DayOfWeek && hour.IsWorkingDay)
            .OrderBy(hour => hour.StartTime)
            .ToList();

        var nowTime = now.TimeOfDay;
        var blockedNow = await schedule.GetOverlappingBlockedTimesAsync(
            member.Id, now, now.AddMinutes(1), cancellationToken);
        var blockedReason = blockedNow.FirstOrDefault()?.Reason;

        var currentInterval = todaysIntervals.FirstOrDefault(interval =>
            nowTime >= interval.StartTime && nowTime < interval.EndTime);

        if (currentInterval is not null)
        {
            return blockedReason is not null
                ? new DashboardStaffDto(member.Id, member.FirstName, member.LastName, member.JobTitle,
                    member.CalendarColor, "Break", blockedReason)
                : new DashboardStaffDto(member.Id, member.FirstName, member.LastName, member.JobTitle,
                    member.CalendarColor, "Available", null);
        }

        var nextInterval = todaysIntervals.FirstOrDefault(interval => interval.StartTime > nowTime);
        if (nextInterval is not null)
        {
            // Already had an interval today and the next one is later: mid-shift
            // gap, so they are on a break rather than off duty.
            var startedShift = todaysIntervals.Any(interval => interval.EndTime <= nowTime);
            return startedShift
                ? new DashboardStaffDto(member.Id, member.FirstName, member.LastName, member.JobTitle,
                    member.CalendarColor, "Break", $"Back at {nextInterval.StartTime:hh\\:mm}")
                : new DashboardStaffDto(member.Id, member.FirstName, member.LastName, member.JobTitle,
                    member.CalendarColor, "Off", $"In at {nextInterval.StartTime:hh\\:mm}");
        }

        return new DashboardStaffDto(member.Id, member.FirstName, member.LastName, member.JobTitle,
            member.CalendarColor, "Off", null);
    }

    private async Task<DashboardAlertsDto> MapAlertsAsync(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        CancellationToken cancellationToken)
    {
        var birthdayDays = Enumerable.Range(0, 7)
            .Select(offset => DateOnly.FromDateTime(dayStart.Date.AddDays(offset)))
            .ToList();

        var birthdayCustomers = await dashboardStore.FindBirthdayCustomersAsync(birthdayDays, cancellationToken);
        var today = DateOnly.FromDateTime(dayStart.Date);

        var birthdaysToday = birthdayCustomers
            .Where(customer => customer.DateOfBirth!.Value.Month == today.Month &&
                               customer.DateOfBirth.Value.Day == today.Day)
            .ToList();

        var birthdayNamesToday = birthdaysToday
            .Select(customer => $"{customer.FirstName} {customer.LastName}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var birthdayNamesLaterThisWeek = birthdayCustomers
            .Except(birthdaysToday)
            .OrderBy(customer => customer.DateOfBirth!.Value.Month)
            .ThenBy(customer => customer.DateOfBirth!.Value.Day)
            .Select(customer => $"{customer.FirstName} {customer.LastName}")
            .ToList();

        var firstVisitIds = await dashboardStore.GetFirstVisitCustomerIdsAsync(dayStart, dayEnd, cancellationToken);

        var recentNotes = await dashboardStore.GetRecentNotesAsync(3, cancellationToken);

        return new DashboardAlertsDto(
            birthdaysToday.Count,
            birthdayNamesToday,
            birthdayNamesLaterThisWeek,
            firstVisitIds.Count,
            recentNotes.Select(note => new DashboardNoteDto(
                note.CustomerId,
                $"{note.Customer.FirstName} {note.Customer.LastName}",
                note.Note,
                note.CreatedAt)).ToList());
    }
}
