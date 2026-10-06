using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Persistence;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// Writes staffing rows straight to the database, past the application services,
/// so a test can arrange a schedule or absence the UI would normally create.
/// </summary>
internal static class StaffingSeed
{
    public static async Task<Guid> StaffAsync(
        DeBlissDbContext context,
        string firstName,
        string lastName,
        bool isActive = true,
        string? jobTitle = null,
        string? email = null,
        string? phoneNumber = null,
        string? calendarColor = null,
        int sortOrder = 0)
    {
        var staff = new Staff
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            IsActive = isActive,
            JobTitle = jobTitle,
            Email = email,
            PhoneNumber = phoneNumber,
            CalendarColor = calendarColor,
            SortOrder = sortOrder
        };

        context.Staff.Add(staff);
        await context.SaveChangesAsync();

        return staff.Id;
    }

    public static async Task<Guid> AssignmentAsync(
        DeBlissDbContext context,
        Guid staffId,
        Guid serviceId)
    {
        var line = new StaffService
        {
            Id = Guid.NewGuid(),
            StaffId = staffId,
            ServiceId = serviceId
        };

        context.StaffServices.Add(line);
        await context.SaveChangesAsync();

        return line.Id;
    }

    public static async Task WorkingHoursAsync(
        DeBlissDbContext context,
        Guid staffId,
        DayOfWeek day,
        TimeSpan start,
        TimeSpan end)
    {
        context.StaffWorkingHours.Add(new StaffWorkingHour
        {
            Id = Guid.NewGuid(),
            StaffId = staffId,
            DayOfWeek = day,
            StartTime = start,
            EndTime = end,
            IsWorkingDay = true
        });

        await context.SaveChangesAsync();
    }

    public static async Task<Guid> TimeOffAsync(
        DeBlissDbContext context,
        Guid staffId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        TimeOffType type = TimeOffType.Leave,
        string? reason = null)
    {
        var entry = new StaffTimeOff
        {
            Id = Guid.NewGuid(),
            StaffId = staffId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Type = type,
            Reason = reason
        };

        context.StaffTimeOff.Add(entry);
        await context.SaveChangesAsync();

        return entry.Id;
    }

    public static async Task<Guid> BlockedTimeAsync(
        DeBlissDbContext context,
        Guid? staffId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        BlockedTimeType type = BlockedTimeType.Break,
        string? reason = null)
    {
        var block = new BlockedTime
        {
            Id = Guid.NewGuid(),
            StaffId = staffId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Type = type,
            Reason = reason
        };

        context.BlockedTime.Add(block);
        await context.SaveChangesAsync();

        return block.Id;
    }

    /// <summary>
    /// An attendance row with its timestamps already set, so a test can arrange a
    /// day the clock-in flow could not have produced (a past shift, a closed day).
    /// </summary>
    public static async Task<Guid> AttendanceAsync(
        DeBlissDbContext context,
        Guid staffId,
        DateOnly workDate,
        DateTimeOffset checkInAt,
        DateTimeOffset? checkOutAt = null)
    {
        var entry = new StaffAttendance
        {
            Id = Guid.NewGuid(),
            StaffId = staffId,
            WorkDate = workDate,
            CheckInAt = checkInAt,
            CheckOutAt = checkOutAt,
            Status = checkOutAt is null ? AttendanceStatus.CheckedIn : AttendanceStatus.CheckedOut
        };

        context.StaffAttendances.Add(entry);
        await context.SaveChangesAsync();

        return entry.Id;
    }
}
