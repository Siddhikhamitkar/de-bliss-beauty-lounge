namespace DeBliss.Application.Dashboard.Dtos;

/// <summary>
/// The receptionist's operational snapshot of today. Deliberately free of money:
/// prices, revenue and every other owner-level figure stay in the billing module.
/// </summary>
public sealed record ReceptionistDashboardDto(
    DateTimeOffset GeneratedAt,
    DashboardTodayDto Today,
    IReadOnlyList<DashboardAppointmentDto> Schedule,
    IReadOnlyList<DashboardAppointmentDto> Upcoming,
    IReadOnlyList<DashboardStaffDto> Staff,
    DashboardAlertsDto Alerts,
    IReadOnlyList<DashboardPopularServiceDto> PopularServices);

/// <summary>Appointment workload counters for the current day — operational only.</summary>
public sealed record DashboardTodayDto(
    DateOnly Date,
    int Total,
    int Completed,
    int InProgress,
    int Upcoming,
    int NoShow,
    int Cancelled,
    int WalkIns);

/// <summary>One booking in today's schedule or the upcoming list, flattened for a row.</summary>
public sealed record DashboardAppointmentDto(
    Guid Id,
    Guid? CustomerId,
    string CustomerName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string ServicesLabel,
    string? StaffName,
    string? StaffColor,
    bool CanCheckout);

/// <summary>A staff member's derived right-now status, computable from existing data only.</summary>
public sealed record DashboardStaffDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? JobTitle,
    string? Color,
    string Status,
    string? StatusNote);

/// <summary>Front-desk alerts: birthdays, first visits and the freshest reception notes.</summary>
public sealed record DashboardAlertsDto(
    int BirthdaysToday,
    IReadOnlyList<string> BirthdayNamesToday,
    IReadOnlyList<string> BirthdayNamesLaterThisWeek,
    int FirstVisitsToday,
    IReadOnlyList<DashboardNoteDto> RecentNotes);

/// <summary>A customer reception note surfaced for the front desk.</summary>
public sealed record DashboardNoteDto(
    Guid CustomerId,
    string CustomerName,
    string Note,
    DateTimeOffset CreatedAt);

/// <summary>An operational workload indicator: how often a service is booked today.</summary>
public sealed record DashboardPopularServiceDto(string Name, int Count);
