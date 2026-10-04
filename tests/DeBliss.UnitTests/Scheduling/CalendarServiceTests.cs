using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Domain.Scheduling;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.Scheduling;

/// <summary>
/// The calendar read model: range validation, the column list (active staff
/// plus booked-but-inactive members), the week pattern each column shades by,
/// and the dated markers — time off and blocked periods, with salon-wide
/// blocks deduplicated so they appear once, not once per column.
/// </summary>
public class CalendarServiceTests
{
    private static readonly DateTimeOffset Monday =
        new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int dayOffset, int hour, int minute = 0) =>
        Monday.AddDays(dayOffset).AddHours(hour).AddMinutes(minute);

    private static CalendarQuery WeekQuery() => new(Monday, Monday.AddDays(5));

    [Fact]
    public async Task A_missing_start_date_is_a_400()
    {
        using var harness = new SchedulingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Calendar.GetAsync(new CalendarQuery(null, Monday.AddDays(1))));

        Assert.True(failure.Errors.ContainsKey("from"));
    }

    [Fact]
    public async Task A_missing_end_date_is_a_400()
    {
        using var harness = new SchedulingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Calendar.GetAsync(new CalendarQuery(Monday, null)));

        Assert.True(failure.Errors.ContainsKey("to"));
    }

    [Fact]
    public async Task An_end_before_the_start_is_a_400()
    {
        using var harness = new SchedulingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Calendar.GetAsync(new CalendarQuery(Monday.AddDays(1), Monday)));

        Assert.Contains(failure.Errors["to"], message => message.Contains("must be after the start date", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_range_longer_than_two_months_is_a_400()
    {
        using var harness = new SchedulingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Calendar.GetAsync(new CalendarQuery(Monday, Monday.AddDays(CalendarQuery.MaximumRangeDays + 1))));

        Assert.Contains(failure.Errors["to"], message => message.Contains("62 days", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Columns_list_active_staff_in_display_order_then_booked_inactive_members()
    {
        using var harness = new SchedulingHarness();
        var amyId = await harness.SeedStaffAsync("Amy", "Lane", sortOrder: 1);
        var zedId = await harness.SeedStaffAsync("Zed", "Parr", sortOrder: 5);
        var retiredId = await harness.SeedStaffAsync("Retta", "Olds", isActive: false, sortOrder: 0);
        var goneId = await harness.SeedStaffAsync("Gone", "Away", isActive: false, sortOrder: 2);
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", staffIds: [retiredId]);
        await harness.SeedAppointmentAsync(
            At(0, 10), At(0, 11),
            lines: [new SchedulingSeed.Line(serviceId, retiredId, "Haircut")]);

        var calendar = await harness.Calendar.GetAsync(WeekQuery());

        // "Gone" has no booking inside this range, so her column drops out;
        // only the booking keeps Retta's column alive, flagged as inactive.
        Assert.Equal([amyId, zedId, retiredId], calendar.Staff.Select(column => column.Id).ToArray());

        var retired = calendar.Staff.Single(column => column.Id == retiredId);
        Assert.False(retired.IsActive);
        Assert.All(calendar.Staff.Where(column => column.Id != retiredId), column => Assert.True(column.IsActive));
    }

    [Fact]
    public async Task Each_column_carries_the_members_week_pattern()
    {
        using var harness = new SchedulingHarness();
        var staffId = await harness.SeedStaffAsync("Amy", "Lane");
        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Wednesday, new TimeSpan(10, 0, 0), new TimeSpan(14, 0, 0));

        var calendar = await harness.Calendar.GetAsync(WeekQuery());

        var column = Assert.Single(calendar.Staff);
        var monday = column.WorkingHours.Days.Single(day => day.DayOfWeek == "Monday");
        Assert.True(monday.IsWorkingDay);
        var interval = Assert.Single(monday.Intervals);
        Assert.Equal("09:00", interval.Start);
        Assert.Equal("17:00", interval.End);

        var wednesday = column.WorkingHours.Days.Single(day => day.DayOfWeek == "Wednesday");
        Assert.True(wednesday.IsWorkingDay);

        var sunday = column.WorkingHours.Days.Single(day => day.DayOfWeek == "Sunday");
        Assert.False(sunday.IsWorkingDay);
        Assert.Empty(sunday.Intervals);
    }

    [Fact]
    public async Task Markers_carry_kind_specific_type_and_reason_and_salon_wide_blocks_appear_once()
    {
        using var harness = new SchedulingHarness();
        var firstId = await harness.SeedStaffAsync("Amy", "Lane");
        var secondId = await harness.SeedStaffAsync("Zed", "Parr");
        await harness.SeedTimeOffAsync(firstId, At(0, 12), At(0, 13), reason: "Dentist");
        var salonWideBlockId = await harness.SeedBlockedTimeAsync(null, At(0, 15), At(0, 16), type: BlockedTimeType.Holiday);

        var calendar = await harness.Calendar.GetAsync(WeekQuery());

        var timeOff = calendar.Markers.Single(marker => marker.Kind == "TimeOff");
        Assert.Equal(firstId, timeOff.StaffId);
        Assert.Equal("Leave", timeOff.Type);
        Assert.Equal("Dentist", timeOff.Label);

        // The salon-wide block overlaps both columns' queries; the service
        // deduplicates it so the grid draws it a single time.
        var block = calendar.Markers.Single(marker => marker.Id == salonWideBlockId);
        Assert.Equal("Blocked", block.Kind);
        Assert.Equal("Holiday", block.Type);
        Assert.Null(block.StaffId);
        Assert.Single(calendar.Markers, marker => marker.Id == salonWideBlockId);
    }

    [Fact]
    public async Task Bookings_in_range_are_mapped_with_customer_and_staff_names()
    {
        using var harness = new SchedulingHarness();
        var customerId = await harness.SeedCustomerAsync("Sara", "Haddad");
        var staffId = await harness.SeedStaffAsync("Amy", "Lane", calendarColor: "#c9a86a");
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", staffIds: [staffId]);
        await harness.SeedAppointmentAsync(
            At(0, 10), At(0, 10, 30), customerId: customerId,
            lines: [new SchedulingSeed.Line(serviceId, staffId, "Haircut")]);
        await harness.SeedAppointmentAsync(
            At(7, 10), At(7, 11), customerId: customerId,
            lines: [new SchedulingSeed.Line(serviceId, staffId, "Haircut")]);

        var calendar = await harness.Calendar.GetAsync(WeekQuery());

        var appointment = Assert.Single(calendar.Appointments);
        Assert.Equal("Sara Haddad", appointment.CustomerName);
        Assert.Equal(At(0, 10), appointment.StartsAt);
        Assert.Equal(At(0, 10, 30), appointment.EndsAt);

        var line = Assert.Single(appointment.Services);
        Assert.Equal("Haircut", line.ServiceName);
        Assert.Equal(staffId, line.StaffId);
        Assert.Equal("Amy", line.StaffFirstName);
        Assert.Equal("#c9a86a", line.StaffColor);
    }
}
