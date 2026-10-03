using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Staffing;

public class StaffScheduleServiceTests
{
    private static readonly DateTimeOffset FutureStart =
        new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset FutureEnd =
        new(2026, 10, 6, 17, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset PastStart =
        new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset PastEnd =
        new(2026, 10, 1, 17, 0, 0, TimeSpan.Zero);

    private static WorkingIntervalDto Interval(string start, string end) => new(start, end);

    private static DayScheduleDto Day(
        string dayOfWeek,
        bool isWorkingDay,
        params WorkingIntervalDto[] intervals) => new(dayOfWeek, isWorkingDay, intervals);

    private static SaveWeekScheduleRequest Week(params DayScheduleDto[] days) => new(days);

    private static SaveStaffTimeOffRequest TimeOff(
        string? type = "Leave",
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        string? reason = null) =>
        new(type, startsAt ?? FutureStart, endsAt ?? FutureEnd, reason);

    [Fact]
    public async Task Get_returns_an_empty_week_for_a_new_staff_member()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var week = await harness.Schedule.GetWeekScheduleAsync(staffId);

        Assert.Equal(7, week.Days.Count);
        Assert.Equal("Monday", week.Days[0].DayOfWeek);
        Assert.Equal("Sunday", week.Days[6].DayOfWeek);
        Assert.All(week.Days, day =>
        {
            Assert.False(day.IsWorkingDay);
            Assert.Empty(day.Intervals);
        });
    }

    [Fact]
    public async Task Save_stores_one_row_per_interval_and_returns_the_full_week()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var week = await harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
            Day("Monday", true, Interval("09:00", "13:00"), Interval("14:00", "18:00"))));

        Assert.Equal(7, week.Days.Count);
        Assert.Equal("Monday", week.Days[0].DayOfWeek);
        Assert.True(week.Days[0].IsWorkingDay);
        Assert.Equal(
            new[] { ("09:00", "13:00"), ("14:00", "18:00") },
            week.Days[0].Intervals.Select(interval => (interval.Start, interval.End)).ToArray());
        Assert.All(week.Days.Skip(1), day => Assert.False(day.IsWorkingDay));

        await using var verify = harness.Reopen();

        var rows = await verify.StaffWorkingHours
            .Where(row => row.StaffId == staffId)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row =>
            row.DayOfWeek == DayOfWeek.Monday
            && row.StartTime == new TimeSpan(9, 0, 0)
            && row.EndTime == new TimeSpan(13, 0, 0));
        Assert.Contains(rows, row =>
            row.DayOfWeek == DayOfWeek.Monday
            && row.StartTime == new TimeSpan(14, 0, 0)
            && row.EndTime == new TimeSpan(18, 0, 0));
    }

    [Fact]
    public async Task Save_accepts_midnight_as_an_interval_end()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var week = await harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
            Day("Friday", true, Interval("09:00", "24:00"))));

        var friday = week.Days.Single(day => day.DayOfWeek == "Friday");

        Assert.Equal(("09:00", "24:00"), (friday.Intervals[0].Start, friday.Intervals[0].End));

        await using var verify = harness.Reopen();

        var row = await verify.StaffWorkingHours.SingleAsync(row => row.StaffId == staffId);

        Assert.Equal(new TimeSpan(24, 0, 0), row.EndTime);
    }

    [Fact]
    public async Task Save_marks_days_off_without_storing_rows()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var week = await harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
            Day("Tuesday", true, Interval("10:00", "16:00")),
            Day("Sunday", false)));

        var sunday = week.Days.Single(day => day.DayOfWeek == "Sunday");

        Assert.False(sunday.IsWorkingDay);
        Assert.Empty(sunday.Intervals);

        await using var verify = harness.Reopen();

        Assert.Equal(1, await verify.StaffWorkingHours.CountAsync(row => row.StaffId == staffId));
    }

    [Fact]
    public async Task Save_replaces_the_previous_pattern()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        await harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
            Day("Monday", true, Interval("09:00", "17:00"))));

        var week = await harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
            Day("Tuesday", true, Interval("10:00", "16:00"))));

        var monday = week.Days.Single(day => day.DayOfWeek == "Monday");

        Assert.False(monday.IsWorkingDay);

        await using var verify = harness.Reopen();

        var rows = await verify.StaffWorkingHours
            .Where(row => row.StaffId == staffId)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal(DayOfWeek.Tuesday, row.DayOfWeek);
        Assert.Equal(new TimeSpan(10, 0, 0), row.StartTime);
    }

    [Fact]
    public async Task Save_allows_intervals_that_only_touch()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var week = await harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
            Day("Monday", true, Interval("09:00", "13:00"), Interval("13:00", "18:00"))));

        Assert.Equal(2, week.Days[0].Intervals.Count);
    }

    [Fact]
    public async Task Save_rejects_an_end_before_the_start()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
                Day("Monday", true, Interval("18:00", "09:00")))));

        Assert.Contains("days", failure.Errors.Keys);
    }

    [Fact]
    public async Task Save_rejects_overlapping_intervals()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
                Day("Monday", true, Interval("09:00", "13:00"), Interval("12:00", "18:00")))));

        Assert.Contains("days", failure.Errors.Keys);
    }

    [Fact]
    public async Task Save_rejects_intervals_on_a_day_off()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
                Day("Monday", false, Interval("09:00", "17:00")))));

        Assert.Contains("days", failure.Errors.Keys);
    }

    [Fact]
    public async Task Save_rejects_a_working_day_without_intervals()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
                Day("Monday", true))));

        Assert.Contains("days", failure.Errors.Keys);
    }

    [Fact]
    public async Task Save_rejects_a_duplicate_day()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
                Day("Monday", true, Interval("09:00", "17:00")),
                Day("Monday", true, Interval("10:00", "18:00")))));

        Assert.Contains("days", failure.Errors.Keys);
    }

    [Fact]
    public async Task Save_rejects_an_unknown_day_name()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
                Day("Freeday", true, Interval("09:00", "17:00")))));

        Assert.Contains("days", failure.Errors.Keys);
    }

    [Theory]
    [InlineData("9am")]
    [InlineData("25:00")]
    [InlineData("24:30")]
    [InlineData("09:60")]
    public async Task Save_rejects_a_malformed_time(string end)
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.SaveWeekScheduleAsync(staffId, Week(
                Day("Monday", true, Interval("09:00", end)))));

        Assert.Contains("days", failure.Errors.Keys);
    }

    [Fact]
    public async Task Save_rejects_an_empty_week()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.SaveWeekScheduleAsync(staffId, new SaveWeekScheduleRequest([])));

        Assert.Contains("days", failure.Errors.Keys);
    }

    [Fact]
    public async Task The_week_schedule_is_not_found_for_an_unknown_staff_member()
    {
        using var harness = new StaffingHarness();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Schedule.GetWeekScheduleAsync(Guid.NewGuid()));

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Schedule.SaveWeekScheduleAsync(
                Guid.NewGuid(),
                Week(Day("Monday", true, Interval("09:00", "17:00")))));
    }

    [Fact]
    public async Task Create_time_off_maps_the_type_name_and_stores_the_entry()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var created = await harness.Schedule.CreateTimeOffAsync(
            staffId,
            TimeOff(type: "Sick", reason: "  Flu  "));

        Assert.Equal("Sick", created.Type);
        Assert.Equal(staffId, created.StaffId);
        Assert.Equal(FutureStart, created.StartsAt);
        Assert.Equal(FutureEnd, created.EndsAt);
        Assert.Equal("Flu", created.Reason);
        Assert.Equal(harness.Database.Clock.Now, created.CreatedAt);

        await using var verify = harness.Reopen();

        var stored = await verify.StaffTimeOff.SingleAsync();

        Assert.Equal(TimeOffType.Sick, stored.Type);
        Assert.Equal("Flu", stored.Reason);
    }

    [Fact]
    public async Task Create_time_off_rejects_an_end_that_is_not_after_the_start()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.CreateTimeOffAsync(
                staffId,
                TimeOff(startsAt: FutureStart, endsAt: FutureStart)));

        Assert.Contains("endsAt", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_time_off_rejects_an_unknown_type()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.CreateTimeOffAsync(staffId, TimeOff(type: "Vacation")));

        Assert.Contains("type", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_time_off_rejects_a_reason_over_the_limit()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Schedule.CreateTimeOffAsync(
                staffId,
                TimeOff(reason: new string('x', StaffingLimits.TimeOffReasonMaxLength + 1))));

        Assert.Contains("reason", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_time_off_is_not_found_for_an_unknown_staff_member()
    {
        using var harness = new StaffingHarness();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Schedule.CreateTimeOffAsync(Guid.NewGuid(), TimeOff()));
    }

    [Fact]
    public async Task Update_time_off_changes_the_entry()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var timeOffId = await harness.Schedule.CreateTimeOffAsync(staffId, TimeOff());

        var updated = await harness.Schedule.UpdateTimeOffAsync(
            staffId,
            timeOffId.Id,
            TimeOff(type: "Training", reason: "Course"));

        Assert.Equal("Training", updated.Type);
        Assert.Equal("Course", updated.Reason);

        await using var verify = harness.Reopen();

        var stored = await verify.StaffTimeOff.SingleAsync();

        Assert.Equal(TimeOffType.Training, stored.Type);
        Assert.Equal("Course", stored.Reason);
    }

    [Fact]
    public async Task Update_time_off_is_not_found_for_another_staff_members_entry()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var otherId = await harness.SeedStaffAsync("Erica", "Mae");
        var timeOffId = await harness.Schedule.CreateTimeOffAsync(staffId, TimeOff());

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Schedule.UpdateTimeOffAsync(otherId, timeOffId.Id, TimeOff()));
    }

    [Fact]
    public async Task Delete_time_off_removes_a_future_entry()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var created = await harness.Schedule.CreateTimeOffAsync(staffId, TimeOff());

        await harness.Schedule.DeleteTimeOffAsync(staffId, created.Id);

        await using var verify = harness.Reopen();

        Assert.Equal(0, await verify.StaffTimeOff.CountAsync());
    }

    [Fact]
    public async Task Delete_time_off_refuses_an_entry_that_has_already_started()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var created = await harness.Schedule.CreateTimeOffAsync(
            staffId,
            TimeOff(startsAt: PastStart, endsAt: PastEnd));

        await Assert.ThrowsAsync<ConflictException>(
            () => harness.Schedule.DeleteTimeOffAsync(staffId, created.Id));

        await using var verify = harness.Reopen();

        Assert.Equal(1, await verify.StaffTimeOff.CountAsync());
    }

    [Fact]
    public async Task Delete_time_off_is_not_found_for_another_staff_members_entry()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var otherId = await harness.SeedStaffAsync("Erica", "Mae");
        var created = await harness.Schedule.CreateTimeOffAsync(staffId, TimeOff());

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Schedule.DeleteTimeOffAsync(otherId, created.Id));
    }

    [Fact]
    public async Task Get_time_off_lists_entries_newest_first()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        await harness.Schedule.CreateTimeOffAsync(
            staffId,
            TimeOff(startsAt: FutureStart, endsAt: FutureEnd));
        await harness.Schedule.CreateTimeOffAsync(
            staffId,
            TimeOff(startsAt: FutureStart.AddDays(3), endsAt: FutureEnd.AddDays(3)));

        var entries = await harness.Schedule.GetTimeOffAsync(staffId);

        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].StartsAt > entries[1].StartsAt);
    }
}
