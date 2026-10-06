using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing;

/// <summary>
/// Clock-in and clock-out against the existing Staff record. "Today" follows the
/// same UTC-date convention the rest of the application uses, and both timestamps
/// are taken from the server clock so a device with the wrong time cannot falsify
/// a shift.
/// </summary>
public sealed class StaffAttendanceService(
    IStaffRepository staff,
    IStaffAttendanceRepository attendance,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IStaffAttendanceService
{
    public async Task<StaffAttendanceDto> CheckInAsync(Guid staffId, CancellationToken cancellationToken = default)
    {
        var member = await LoadAsync(staffId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var workDate = DateOnly.FromDateTime(now.UtcDateTime);

        var existing = await attendance.GetForDateAsync(staffId, workDate, cancellationToken);

        if (existing is not null)
        {
            throw new ConflictException(existing.CheckOutAt is null
                ? "You are already checked in for today."
                : "Today's attendance has already been closed.");
        }

        var entry = new StaffAttendance
        {
            Staff = member,
            WorkDate = workDate,
            CheckInAt = now,
            Status = AttendanceStatus.CheckedIn
        };

        attendance.Add(entry);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(entry, member.WorkingHours);
    }

    public async Task<StaffAttendanceDto> CheckOutAsync(Guid staffId, CancellationToken cancellationToken = default)
    {
        var member = await LoadAsync(staffId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var workDate = DateOnly.FromDateTime(now.UtcDateTime);

        var entry = await attendance.GetForDateAsync(staffId, workDate, cancellationToken)
            ?? throw new ConflictException("Check in before checking out.");

        if (entry.CheckOutAt is not null)
        {
            throw new ConflictException("You have already checked out for today.");
        }

        entry.CheckOutAt = now;
        entry.Status = AttendanceStatus.CheckedOut;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(entry, member.WorkingHours);
    }

    public async Task<StaffAttendanceDto?> GetTodayAsync(Guid staffId, CancellationToken cancellationToken = default)
    {
        var member = await LoadAsync(staffId, cancellationToken);

        var workDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var entry = await attendance.GetForDateAsync(staffId, workDate, cancellationToken);

        return entry is null ? null : Map(entry, member.WorkingHours);
    }

    public async Task<IReadOnlyList<StaffAttendanceDto>> GetHistoryAsync(
        Guid staffId,
        AttendanceQuery query,
        CancellationToken cancellationToken = default)
    {
        var member = await LoadAsync(staffId, cancellationToken);

        var range = ResolveRange(query);

        var entries = await attendance.GetInRangeAsync(staffId, range.From, range.To, cancellationToken);

        return [.. entries.Select(entry => Map(entry, member.WorkingHours))];
    }

    /// <summary>
    /// An omitted end date means "up to today" and an omitted start date means the
    /// default look-back from there, so a mobile client can ask for its history
    /// with no parameters at all.
    /// </summary>
    private (DateOnly From, DateOnly To) ResolveRange(AttendanceQuery query)
    {
        var errors = new ValidationErrorBuilder();

        var to = query.To ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        if (query.From is { } from && from > to)
        {
            errors.Add("to", "The end date cannot be before the start date.");
        }

        var resolvedFrom = query.From ?? to.AddDays(-(AttendanceQuery.DefaultRangeDays - 1));

        if (resolvedFrom.AddDays(AttendanceQuery.MaximumRangeDays - 1) < to)
        {
            errors.Add("to", $"The attendance range cannot span more than {AttendanceQuery.MaximumRangeDays} days.");
        }

        errors.ThrowIfInvalid();

        return (resolvedFrom, to);
    }

    private async Task<Staff> LoadAsync(Guid staffId, CancellationToken cancellationToken)
    {
        return await staff.GetByIdForScheduleAsync(staffId, cancellationToken)
            ?? throw new NotFoundException($"Staff member '{staffId}' was not found.");
    }

    /// <summary>
    /// Carries the shift that was planned for the weekday alongside the actual
    /// timestamps, so the owner can compare them without a second call. No worked
    /// hours are derived: that belongs to the later compensation phase.
    /// </summary>
    private static StaffAttendanceDto Map(StaffAttendance entry, ICollection<StaffWorkingHour> workingHours)
    {
        var intervals = WorkingHourIntervals.For(workingHours, entry.WorkDate.DayOfWeek);

        return new StaffAttendanceDto(
            entry.Id,
            entry.StaffId,
            entry.WorkDate,
            entry.CheckInAt,
            entry.CheckOutAt,
            Enum.GetName(entry.Status)!,
            IsScheduledWorkingDay: intervals.Count > 0,
            intervals,
            entry.CreatedAt,
            entry.UpdatedAt);
    }
}
