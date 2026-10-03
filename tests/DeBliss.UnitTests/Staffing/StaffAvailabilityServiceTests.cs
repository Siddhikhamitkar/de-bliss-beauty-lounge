using DeBliss.Application.Common.Exceptions;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;

namespace DeBliss.UnitTests.Staffing;

public class StaffAvailabilityServiceTests
{
    private static readonly DateTimeOffset Monday =
        new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int dayOffset, int hour, int minute = 0) =>
        Monday.AddDays(dayOffset).AddHours(hour).AddMinutes(minute);

    private sealed record Fixture(StaffingHarness Harness, Guid StaffId, Guid ServiceId);

    private static async Task<Fixture> ArrangeAsync(bool isActive = true, bool withHours = true)
    {
        var harness = new StaffingHarness();
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae", isActive: isActive, jobTitle: "Stylist");
        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", staffIds: [staffId]);

        if (withHours)
        {
            await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
        }

        return new Fixture(harness, staffId, serviceId);
    }

    [Fact]
    public async Task A_member_is_available_when_active_assigned_and_inside_working_hours()
    {
        var (harness, staffId, serviceId) = await ArrangeAsync();
        using var _ = harness;

        var check = await harness.Availability.CheckAsync(staffId, At(0, 10), At(0, 11));

        Assert.True(check.IsAvailable);
        Assert.Equal(staffId, check.StaffId);
        Assert.Equal(At(0, 10), check.Start);
        Assert.Equal(At(0, 11), check.End);

        var options = await harness.Availability.GetAvailableStaffAsync(serviceId, At(0, 10), At(0, 11));

        var option = Assert.Single(options);
        Assert.Equal(staffId, option.Id);
        Assert.Equal("Jasmin", option.FirstName);
        Assert.Equal("Stylist", option.JobTitle);
        Assert.True(option.IsActive);
    }

    [Fact]
    public async Task An_inactive_member_is_excluded()
    {
        var (harness, staffId, serviceId) = await ArrangeAsync(isActive: false);
        using var _ = harness;

        var check = await harness.Availability.CheckAsync(staffId, At(0, 10), At(0, 11));

        Assert.False(check.IsAvailable);

        var options = await harness.Availability.GetAvailableStaffAsync(serviceId, At(0, 10), At(0, 11));

        Assert.Empty(options);
    }

    [Fact]
    public async Task A_member_not_assigned_to_the_service_is_excluded()
    {
        var harness = new StaffingHarness();
        using var _ = harness;
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", staffIds: [staffId]);
        var otherServiceId = await harness.SeedServiceAsync(categoryId, "Colour");
        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

        var options = await harness.Availability.GetAvailableStaffAsync(otherServiceId, At(0, 10), At(0, 11));

        Assert.Empty(options);
    }

    [Fact]
    public async Task A_slot_after_the_working_day_is_unavailable()
    {
        var (harness, staffId, _) = await ArrangeAsync();
        using var _ = harness;

        var check = await harness.Availability.CheckAsync(staffId, At(0, 18), At(0, 19));

        Assert.False(check.IsAvailable);
    }

    [Fact]
    public async Task A_day_with_no_schedule_is_unavailable()
    {
        var (harness, staffId, _) = await ArrangeAsync();
        using var _ = harness;

        var check = await harness.Availability.CheckAsync(staffId, At(-1, 10), At(-1, 11));

        Assert.False(check.IsAvailable);
    }

    [Fact]
    public async Task A_slot_spanning_the_mid_day_break_is_unavailable()
    {
        var (harness, staffId, _) = await ArrangeAsync(withHours: false);
        using var _ = harness;

        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(13, 0, 0));
        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Monday, new TimeSpan(14, 0, 0), new TimeSpan(18, 0, 0));

        var check = await harness.Availability.CheckAsync(staffId, At(0, 12, 30), At(0, 13, 30));

        Assert.False(check.IsAvailable);
    }

    [Fact]
    public async Task A_slot_inside_one_split_interval_is_available()
    {
        var (harness, staffId, _) = await ArrangeAsync(withHours: false);
        using var _ = harness;

        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(13, 0, 0));
        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Monday, new TimeSpan(14, 0, 0), new TimeSpan(18, 0, 0));

        var check = await harness.Availability.CheckAsync(staffId, At(0, 10), At(0, 11));

        Assert.True(check.IsAvailable);
    }

    [Fact]
    public async Task A_slot_overlapping_time_off_is_unavailable()
    {
        var (harness, staffId, _) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedTimeOffAsync(staffId, At(0, 10), At(0, 11), TimeOffType.Sick);

        var check = await harness.Availability.CheckAsync(staffId, At(0, 10, 30), At(0, 11, 30));

        Assert.False(check.IsAvailable);
    }

    [Fact]
    public async Task A_slot_back_to_back_with_time_off_is_available()
    {
        var (harness, staffId, _) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedTimeOffAsync(staffId, At(0, 9), At(0, 10), TimeOffType.Sick);

        var check = await harness.Availability.CheckAsync(staffId, At(0, 10), At(0, 10, 30));

        Assert.True(check.IsAvailable);
    }

    [Fact]
    public async Task A_staff_blocked_period_makes_the_slot_unavailable()
    {
        var (harness, staffId, _) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedBlockedTimeAsync(staffId, At(0, 12), At(0, 13), BlockedTimeType.Meeting);

        var check = await harness.Availability.CheckAsync(staffId, At(0, 12), At(0, 13));

        Assert.False(check.IsAvailable);
    }

    [Fact]
    public async Task A_salon_wide_blocked_period_makes_the_slot_unavailable()
    {
        var (harness, staffId, _) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedBlockedTimeAsync(null, At(0, 12), At(0, 13), BlockedTimeType.Holiday);

        var check = await harness.Availability.CheckAsync(staffId, At(0, 12), At(0, 13));

        Assert.False(check.IsAvailable);
    }

    [Fact]
    public async Task A_slot_ending_at_midnight_belongs_to_the_same_day()
    {
        var (harness, staffId, _) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Friday, new TimeSpan(9, 0, 0), new TimeSpan(24, 0, 0));

        var check = await harness.Availability.CheckAsync(staffId, At(-3, 23), At(-2, 0));

        Assert.True(check.IsAvailable);
    }

    [Fact]
    public async Task A_slot_spanning_midnight_is_unavailable()
    {
        var (harness, staffId, _) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Friday, new TimeSpan(9, 0, 0), new TimeSpan(24, 0, 0));

        var check = await harness.Availability.CheckAsync(staffId, At(-3, 23), At(-2, 1));

        Assert.False(check.IsAvailable);
    }

    [Fact]
    public async Task Check_rejects_a_window_that_does_not_end_after_the_start()
    {
        var (harness, staffId, _) = await ArrangeAsync();
        using var _ = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Availability.CheckAsync(staffId, At(0, 10), At(0, 10)));

        Assert.Contains("end", failure.Errors.Keys);
    }

    [Fact]
    public async Task Check_is_not_found_for_an_unknown_staff_member()
    {
        using var harness = new StaffingHarness();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Availability.CheckAsync(Guid.NewGuid(), At(0, 10), At(0, 11)));
    }

    [Fact]
    public async Task Available_staff_is_not_found_for_an_unknown_service()
    {
        using var harness = new StaffingHarness();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Availability.GetAvailableStaffAsync(Guid.NewGuid(), At(0, 10), At(0, 11)));
    }
}
