using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Staffing;

/// <summary>
/// Clock-in and clock-out rules. The harness clock starts at 2026-10-03 09:00 UTC,
/// so "today" is that date until a test advances it — which is exactly how the
/// service derives the work date, never from anything the caller supplies.
/// </summary>
public class StaffAttendanceServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    /// <summary>An early-morning timestamp on the given work date.</summary>
    private static DateTimeOffset At(DateOnly date) =>
        new(date.Year, date.Month, date.Day, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Check_in_opens_todays_record_with_the_server_timestamp()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var opened = await harness.Attendance.CheckInAsync(staffId);

        Assert.Equal(staffId, opened.StaffId);
        Assert.Equal(Today, opened.WorkDate);
        Assert.Equal(harness.Database.Clock.Now, opened.CheckInAt);
        Assert.Null(opened.CheckOutAt);
        Assert.Equal("CheckedIn", opened.Status);

        await using var verify = harness.Reopen();

        var stored = await verify.StaffAttendances.SingleAsync();

        Assert.Equal(staffId, stored.StaffId);
        Assert.Equal(Today, stored.WorkDate);
        Assert.Equal(harness.Database.Clock.Now, stored.CheckInAt);
        Assert.Null(stored.CheckOutAt);
        Assert.Equal(AttendanceStatus.CheckedIn, stored.Status);
    }

    [Fact]
    public void Check_in_accepts_no_timestamp_from_the_caller()
    {
        // Neither command takes a time, so a device with the wrong clock cannot
        // falsify a shift. Asserting the signature keeps that guarantee structural.
        Assert.Equal(
            [typeof(Guid), typeof(CancellationToken)],
            ParameterTypesOf(nameof(IStaffAttendanceService.CheckInAsync)));
        Assert.Equal(
            [typeof(Guid), typeof(CancellationToken)],
            ParameterTypesOf(nameof(IStaffAttendanceService.CheckOutAsync)));
    }

    [Fact]
    public async Task A_second_check_in_on_the_same_day_is_refused()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.Attendance.CheckInAsync(staffId);

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Attendance.CheckInAsync(staffId));

        Assert.Equal("You are already checked in for today.", failure.Message);

        await using var verify = harness.Reopen();

        Assert.Equal(1, await verify.StaffAttendances.CountAsync());
    }

    [Fact]
    public async Task Check_in_after_the_day_is_closed_is_refused()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.Attendance.CheckInAsync(staffId);
        await harness.Attendance.CheckOutAsync(staffId);

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Attendance.CheckInAsync(staffId));

        Assert.Equal("Today's attendance has already been closed.", failure.Message);
    }

    [Fact]
    public async Task Check_out_closes_the_open_record()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.Attendance.CheckInAsync(staffId);

        harness.Database.Clock.Advance(TimeSpan.FromHours(9));

        var closed = await harness.Attendance.CheckOutAsync(staffId);

        Assert.Equal(harness.Database.Clock.Now, closed.CheckOutAt);
        Assert.Equal("CheckedOut", closed.Status);

        await using var verify = harness.Reopen();

        var stored = await verify.StaffAttendances.SingleAsync();

        Assert.Equal(harness.Database.Clock.Now, stored.CheckOutAt);
        Assert.Equal(AttendanceStatus.CheckedOut, stored.Status);
    }

    [Fact]
    public async Task Check_out_without_a_check_in_is_refused()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Attendance.CheckOutAsync(staffId));

        Assert.Equal("Check in before checking out.", failure.Message);
    }

    [Fact]
    public async Task A_second_check_out_is_refused_and_leaves_the_first_close_alone()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.Attendance.CheckInAsync(staffId);
        await harness.Attendance.CheckOutAsync(staffId);

        var firstClose = harness.Database.Clock.Now;
        harness.Database.Clock.Advance(TimeSpan.FromHours(1));

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Attendance.CheckOutAsync(staffId));

        Assert.Equal("You have already checked out for today.", failure.Message);

        await using var verify = harness.Reopen();

        Assert.Equal(firstClose, (await verify.StaffAttendances.SingleAsync()).CheckOutAt);
    }

    [Fact]
    public async Task The_next_work_date_opens_a_fresh_record()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.Attendance.CheckInAsync(staffId);
        await harness.Attendance.CheckOutAsync(staffId);

        harness.Database.Clock.Advance(TimeSpan.FromDays(1));

        var tomorrow = await harness.Attendance.CheckInAsync(staffId);

        Assert.Equal(Today.AddDays(1), tomorrow.WorkDate);

        await using var verify = harness.Reopen();

        Assert.Equal(2, await verify.StaffAttendances.CountAsync());
    }

    [Fact]
    public async Task The_database_refuses_a_second_record_for_the_same_staff_and_date()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.Attendance.CheckInAsync(staffId);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => harness.SeedAttendanceAsync(staffId, Today, At(Today).AddMinutes(5)));
    }

    [Fact]
    public async Task Two_staff_members_each_hold_their_own_record_for_the_same_date()
    {
        using var harness = new StaffingHarness();
        var jasmin = await harness.SeedStaffAsync("Jasmin", "Rae");
        var erica = await harness.SeedStaffAsync("Erica", "Mae");

        await harness.Attendance.CheckInAsync(jasmin);
        await harness.Attendance.CheckInAsync(erica);

        await using var verify = harness.Reopen();

        var rows = await verify.StaffAttendances.ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.StaffId == jasmin);
        Assert.Contains(rows, row => row.StaffId == erica);
    }

    [Fact]
    public async Task Today_returns_nothing_before_the_first_check_in()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        Assert.Null(await harness.Attendance.GetTodayAsync(staffId));

        await harness.Attendance.CheckInAsync(staffId);

        Assert.NotNull(await harness.Attendance.GetTodayAsync(staffId));
    }

    [Fact]
    public async Task Today_does_not_return_another_staff_members_record()
    {
        using var harness = new StaffingHarness();
        var jasmin = await harness.SeedStaffAsync("Jasmin", "Rae");
        var erica = await harness.SeedStaffAsync("Erica", "Mae");
        await harness.Attendance.CheckInAsync(jasmin);

        Assert.Null(await harness.Attendance.GetTodayAsync(erica));
    }

    [Fact]
    public async Task History_lists_work_dates_newest_first_with_the_shift_planned_for_each()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.Schedule.SaveWeekScheduleAsync(
            staffId,
            new SaveWeekScheduleRequest(
                [new DayScheduleDto("Friday", true, [new WorkingIntervalDto("09:00", "18:00")])]));

        var earlier = new DateOnly(2026, 9, 25);
        var later = new DateOnly(2026, 10, 2);

        await harness.SeedAttendanceAsync(staffId, earlier, At(earlier));
        await harness.SeedAttendanceAsync(staffId, later, At(later));

        var history = await harness.Attendance.GetHistoryAsync(staffId, new AttendanceQuery(null, null));

        Assert.Equal([later, earlier], history.Select(entry => entry.WorkDate).ToArray());
        Assert.All(history, entry =>
        {
            Assert.True(entry.IsScheduledWorkingDay);
            Assert.Equal(("09:00", "18:00"), (entry.ScheduledIntervals[0].Start, entry.ScheduledIntervals[0].End));
        });
    }

    [Fact]
    public async Task History_marks_a_day_the_working_pattern_does_not_cover()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.SeedAttendanceAsync(staffId, Today, At(Today));

        var entry = Assert.Single(
            await harness.Attendance.GetHistoryAsync(staffId, new AttendanceQuery(null, null)));

        Assert.False(entry.IsScheduledWorkingDay);
        Assert.Empty(entry.ScheduledIntervals);
    }

    [Fact]
    public async Task History_defaults_to_the_last_ninety_days_ending_today()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var inside = Today.AddDays(-1);
        var outside = Today.AddDays(-(AttendanceQuery.DefaultRangeDays + 1));

        await harness.SeedAttendanceAsync(staffId, inside, At(inside));
        await harness.SeedAttendanceAsync(staffId, outside, At(outside));

        var entry = Assert.Single(
            await harness.Attendance.GetHistoryAsync(staffId, new AttendanceQuery(null, null)));

        Assert.Equal(inside, entry.WorkDate);
    }

    [Fact]
    public async Task History_returns_nothing_for_a_staff_member_who_never_clocked_in()
    {
        using var harness = new StaffingHarness();
        var jasmin = await harness.SeedStaffAsync("Jasmin", "Rae");
        var erica = await harness.SeedStaffAsync("Erica", "Mae");
        await harness.Attendance.CheckInAsync(jasmin);

        Assert.Empty(await harness.Attendance.GetHistoryAsync(erica, new AttendanceQuery(null, null)));
    }

    [Fact]
    public async Task History_honours_an_explicit_range()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        await harness.SeedAttendanceAsync(staffId, new DateOnly(2026, 9, 1), At(new DateOnly(2026, 9, 1)));
        await harness.SeedAttendanceAsync(staffId, new DateOnly(2026, 9, 15), At(new DateOnly(2026, 9, 15)));
        await harness.SeedAttendanceAsync(staffId, new DateOnly(2026, 9, 30), At(new DateOnly(2026, 9, 30)));

        var history = await harness.Attendance.GetHistoryAsync(
            staffId, new AttendanceQuery(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20)));

        Assert.Equal(new DateOnly(2026, 9, 15), Assert.Single(history).WorkDate);
    }

    [Fact]
    public async Task History_rejects_a_range_that_ends_before_it_starts()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Attendance.GetHistoryAsync(
                staffId, new AttendanceQuery(new DateOnly(2026, 10, 3), new DateOnly(2026, 9, 1))));

        Assert.Contains("to", failure.Errors.Keys);
    }

    [Fact]
    public async Task History_rejects_a_range_wider_than_a_year()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var from = new DateOnly(2024, 1, 1);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Attendance.GetHistoryAsync(
                staffId, new AttendanceQuery(from, from.AddDays(AttendanceQuery.MaximumRangeDays))));

        Assert.Contains("to", failure.Errors.Keys);
    }

    [Fact]
    public async Task Attendance_is_not_found_for_an_unknown_staff_member()
    {
        using var harness = new StaffingHarness();
        var unknown = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() => harness.Attendance.CheckInAsync(unknown));
        await Assert.ThrowsAsync<NotFoundException>(() => harness.Attendance.CheckOutAsync(unknown));
        await Assert.ThrowsAsync<NotFoundException>(() => harness.Attendance.GetTodayAsync(unknown));
        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Attendance.GetHistoryAsync(unknown, new AttendanceQuery(null, null)));
    }

    private static Type[] ParameterTypesOf(string methodName) =>
        typeof(IStaffAttendanceService)
            .GetMethod(methodName)!
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
}
