using DeBliss.Application.Scheduling.Dtos;

namespace DeBliss.Application.Scheduling;

/// <summary>Read model behind the calendar grid: columns, bookings and non-bookable markers for a range.</summary>
public interface ICalendarService
{
    Task<CalendarDto> GetAsync(CalendarQuery query, CancellationToken cancellationToken = default);
}
