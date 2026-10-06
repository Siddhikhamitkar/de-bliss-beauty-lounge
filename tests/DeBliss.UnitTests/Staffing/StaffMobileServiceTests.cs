using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Staffing;

/// <summary>
/// What a staff member sees about themselves. The harness clock sits at
/// 2026-10-03 09:00 UTC (a Saturday), so the scope windows are deterministic.
/// Everything here is filtered by the staff id the caller passes — in production
/// that id comes from the signed token, which the API tests cover.
/// </summary>
public class StaffMobileServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EarlierToday = Now.AddHours(-2);
    private static readonly DateTimeOffset LaterToday = Now.AddHours(1);
    private static readonly DateTimeOffset Tomorrow = Now.AddDays(1);
    private static readonly DateTimeOffset ThisMonth = Now.AddDays(17);
    private static readonly DateTimeOffset NextMonth = Now.AddDays(48);
    private static readonly DateTimeOffset Yesterday = Now.AddDays(-1);
    private static readonly DateTimeOffset LastQuarter = Now.AddDays(-30);
    private static readonly DateTimeOffset OlderThanAQuarter = Now.AddDays(-124);

    [Fact]
    public async Task Profile_returns_the_staff_members_own_record()
    {
        using var harness = new StaffingHarness();
        var staffId = await harness.SeedStaffAsync(
            "Jasmin", "Rae", jobTitle: "Senior Stylist", email: "jasmin@debliss.ae", phoneNumber: "+971501234567");

        var profile = await harness.Mobile.GetProfileAsync(staffId);

        Assert.Equal(staffId, profile.StaffId);
        Assert.Equal("Jasmin", profile.FirstName);
        Assert.Equal("Rae", profile.LastName);
        Assert.Equal("Senior Stylist", profile.JobTitle);
        Assert.Equal("jasmin@debliss.ae", profile.Email);
        Assert.Equal("+971501234567", profile.PhoneNumber);
        Assert.True(profile.IsActive);
    }

    [Fact]
    public async Task Profile_is_not_found_for_an_unknown_staff_member()
    {
        using var harness = new StaffingHarness();

        await Assert.ThrowsAsync<NotFoundException>(() => harness.Mobile.GetProfileAsync(Guid.NewGuid()));
    }

    [Fact]
    public void The_mobile_projection_carries_no_price_and_no_colleague()
    {
        // §6 of the brief: a team member sees what to do and when, not what the
        // salon charges and not who else is on the booking.
        Assert.DoesNotContain(
            PropertyNamesOf<StaffAppointmentDto>(),
            name => name.Contains("Price", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Staff", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            PropertyNamesOf<StaffAppointmentServiceDto>(),
            name => name.Contains("Price", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Staff", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Today_returns_only_the_bookings_that_start_today()
    {
        using var harness = new MobileFixture();

        Assert.Equal(
            [harness.EarlierTodayId, harness.LaterTodayId],
            (await harness.AppointmentsFor(StaffAppointmentScope.Today))
                .Select(appointment => appointment.Id)
                .ToArray());
    }

    [Fact]
    public async Task Upcoming_returns_the_next_month_from_now()
    {
        using var harness = new MobileFixture();

        Assert.Equal(
            [harness.LaterTodayId, harness.TomorrowId, harness.ThisMonthId],
            (await harness.AppointmentsFor(StaffAppointmentScope.Upcoming))
                .Select(appointment => appointment.Id)
                .ToArray());
    }

    [Fact]
    public async Task History_returns_the_last_quarter_newest_first()
    {
        using var harness = new MobileFixture();

        // Earlier today is already behind "now", so it reads as history too.
        Assert.Equal(
            [harness.EarlierTodayId, harness.YesterdayId, harness.LastQuarterId],
            (await harness.AppointmentsFor(StaffAppointmentScope.History))
                .Select(appointment => appointment.Id)
                .ToArray());
    }

    [Fact]
    public async Task A_booking_assigned_to_somebody_else_is_never_returned()
    {
        using var harness = new MobileFixture();
        var colleagueId = await harness.Harness.SeedStaffAsync("Erica", "Mae");
        var appointmentId = await harness.Harness.SeedAppointmentAsync(
            harness.CustomerId,
            LaterToday,
            lines: [(harness.ServiceId, colleagueId, "Colour")]);

        foreach (var scope in Enum.GetValues<StaffAppointmentScope>())
        {
            var found = await harness.Harness.Mobile.GetAppointmentsAsync(harness.StaffId, scope);

            Assert.DoesNotContain(found, appointment => appointment.Id == appointmentId);
        }
    }

    [Fact]
    public async Task A_shared_booking_shows_only_the_signed_in_staff_members_own_lines()
    {
        using var harness = new MobileFixture();
        var colleagueId = await harness.Harness.SeedStaffAsync("Erica", "Mae");
        var shared = await harness.Harness.SeedAppointmentAsync(
            harness.CustomerId,
            LaterToday,
            lines:
            [
                (harness.ServiceId, harness.StaffId, "Cut"),
                (harness.ServiceId, colleagueId, "Colour")
            ]);

        var mine = (await harness.Harness.Mobile.GetAppointmentsAsync(
                harness.StaffId, StaffAppointmentScope.Today))
            .Single(appointment => appointment.Id == shared);

        var line = Assert.Single(mine.Services);

        Assert.Equal("Cut", line.ServiceName);
        Assert.Equal(60, mine.DurationMinutes);

        var theirs = (await harness.Harness.Mobile.GetAppointmentsAsync(
                colleagueId, StaffAppointmentScope.Today))
            .Single(appointment => appointment.Id == shared);

        Assert.Equal("Colour", Assert.Single(theirs.Services).ServiceName);
    }

    [Fact]
    public async Task A_booking_with_no_staff_on_the_line_belongs_to_nobody()
    {
        using var harness = new MobileFixture();
        var unassigned = await harness.Harness.SeedAppointmentAsync(
            harness.CustomerId, LaterToday, lines: [(harness.ServiceId, null, "Cut")]);

        var found = await harness.Harness.Mobile.GetAppointmentsAsync(
            harness.StaffId, StaffAppointmentScope.Today);

        Assert.DoesNotContain(found, appointment => appointment.Id == unassigned);
    }

    [Fact]
    public async Task Appointments_carry_the_customer_name_status_and_operational_notes()
    {
        using var harness = new MobileFixture();

        await RewriteAsync(harness.Harness, harness.LaterTodayId, appointment =>
        {
            appointment.Notes = "Allergic to ammonia";
            appointment.Services.First().Notes = "Use low heat";
        });

        var found = (await harness.AppointmentsFor(StaffAppointmentScope.Today))
            .Single(appointment => appointment.Id == harness.LaterTodayId);

        Assert.Equal("Nadia Haddad", found.CustomerName);
        Assert.Equal("Confirmed", found.Status);
        Assert.Equal("Allergic to ammonia", found.Notes);
        Assert.Equal(LaterToday, found.StartsAt);
        Assert.Equal(LaterToday.AddHours(1), found.EndsAt);
        Assert.Equal("Use low heat", Assert.Single(found.Services).Notes);
    }

    [Fact]
    public async Task A_walk_in_booking_is_named_from_the_booking_itself()
    {
        using var harness = new MobileFixture();
        var walkInId = await SeedWalkInAsync(harness.Harness, LaterToday, harness.StaffId, harness.ServiceId);

        var found = (await harness.AppointmentsFor(StaffAppointmentScope.Today))
            .Single(appointment => appointment.Id == walkInId);

        Assert.Equal("Walk-in Guest", found.CustomerName);
    }

    [Fact]
    public async Task A_cancelled_booking_still_appears_so_the_day_reads_honestly()
    {
        using var harness = new MobileFixture();

        await RewriteAsync(
            harness.Harness, harness.LaterTodayId, appointment =>
                appointment.Status = AppointmentStatus.Cancelled);

        var found = (await harness.AppointmentsFor(StaffAppointmentScope.Today))
            .Single(appointment => appointment.Id == harness.LaterTodayId);

        Assert.Equal("Cancelled", found.Status);
    }

    [Fact]
    public async Task A_deleted_booking_is_never_returned()
    {
        using var harness = new MobileFixture();

        await RewriteAsync(
            harness.Harness, harness.LaterTodayId, appointment => appointment.DeletedAt = Now);

        Assert.Equal(
            [harness.EarlierTodayId],
            (await harness.AppointmentsFor(StaffAppointmentScope.Today))
                .Select(appointment => appointment.Id)
                .ToArray());
    }

    [Fact]
    public async Task Schedule_covers_today_and_the_six_days_after_it()
    {
        using var harness = new MobileFixture();
        await harness.Harness.Schedule.SaveWeekScheduleAsync(
            harness.StaffId,
            new SaveWeekScheduleRequest(
                [
                    new DayScheduleDto("Saturday", true, [new WorkingIntervalDto("09:00", "18:00")]),
                    new DayScheduleDto("Sunday", false, [])
                ]));

        var schedule = await harness.Harness.Mobile.GetScheduleAsync(harness.StaffId);

        Assert.Equal(Today, schedule.Today);
        Assert.Equal(7, schedule.Days.Count);
        Assert.Equal(Today, schedule.Days[0].Date);
        Assert.Equal(Today.AddDays(6), schedule.Days[6].Date);

        Assert.True(schedule.Days[0].IsWorkingDay);
        Assert.Equal("Saturday", schedule.Days[0].DayOfWeek);
        Assert.Equal(("09:00", "18:00"), (schedule.Days[0].Intervals[0].Start, schedule.Days[0].Intervals[0].End));

        Assert.False(schedule.Days[1].IsWorkingDay);
        Assert.Equal("Sunday", schedule.Days[1].DayOfWeek);
        Assert.Empty(schedule.Days[1].Intervals);
    }

    [Fact]
    public async Task Schedule_reuses_the_pattern_the_owner_already_maintains()
    {
        // §7: no second scheduling system. Whatever the existing weekly editor
        // stores is what the staff member sees, with no copy kept alongside.
        using var harness = new MobileFixture();
        await harness.Harness.Schedule.SaveWeekScheduleAsync(
            harness.StaffId,
            new SaveWeekScheduleRequest(
                [new DayScheduleDto("Monday", true, [new WorkingIntervalDto("10:00", "14:00")])]));

        var schedule = await harness.Harness.Mobile.GetScheduleAsync(harness.StaffId);

        var monday = schedule.WeeklyPattern.Days.Single(day => day.DayOfWeek == "Monday");

        Assert.True(monday.IsWorkingDay);
        Assert.Equal(("10:00", "14:00"), (monday.Intervals[0].Start, monday.Intervals[0].End));
        Assert.True(schedule.Days.Single(day => day.Date == Today.AddDays(2)).IsWorkingDay);
    }

    [Fact]
    public async Task Schedule_lists_the_time_off_that_covers_each_day()
    {
        using var harness = new MobileFixture();
        await harness.Harness.Schedule.SaveWeekScheduleAsync(
            harness.StaffId,
            new SaveWeekScheduleRequest(
                [new DayScheduleDto("Saturday", true, [new WorkingIntervalDto("09:00", "18:00")])]));
        await harness.Harness.SeedTimeOffAsync(
            harness.StaffId, Now.AddDays(1), Now.AddDays(2), TimeOffType.Leave, "Family");

        var schedule = await harness.Harness.Mobile.GetScheduleAsync(harness.StaffId);

        Assert.Empty(schedule.Days[0].TimeOff);

        var off = Assert.Single(schedule.Days[1].TimeOff);

        Assert.Equal("Leave", off.Type);
        Assert.Equal("Family", off.Reason);
        Assert.Empty(schedule.Days[3].TimeOff);
    }

    [Fact]
    public async Task Schedule_is_not_found_for_an_unknown_staff_member()
    {
        using var harness = new StaffingHarness();

        await Assert.ThrowsAsync<NotFoundException>(() => harness.Mobile.GetScheduleAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Appointments_are_not_found_for_an_unknown_staff_member()
    {
        using var harness = new StaffingHarness();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Mobile.GetAppointmentsAsync(Guid.NewGuid(), StaffAppointmentScope.Today));
    }

    private static string[] PropertyNamesOf<T>() =>
        typeof(T).GetProperties().Select(property => property.Name).ToArray();

    private static async Task RewriteAsync(
        StaffingHarness harness,
        Guid appointmentId,
        Action<Appointment> rewrite)
    {
        await using var context = harness.Reopen();

        var appointment = await context.Appointments
            .Include(each => each.Services)
            .SingleAsync(each => each.Id == appointmentId);

        rewrite(appointment);
        await context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();
    }

    private static async Task<Guid> SeedWalkInAsync(
        StaffingHarness harness,
        DateTimeOffset startsAt,
        Guid staffId,
        Guid serviceId)
    {
        await using var context = harness.Reopen();

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            WalkInCustomerName = "Walk-in Guest",
            StartsAt = startsAt,
            EndsAt = startsAt.AddHours(1),
            Status = AppointmentStatus.Confirmed,
            Source = AppointmentSource.WalkIn
        };

        appointment.Services.Add(new AppointmentService
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id,
            ServiceId = serviceId,
            StaffId = staffId,
            ServiceNameSnapshot = "Cut",
            DurationMinutes = 60,
            UnitPrice = 100m,
            SortOrder = 0
        });

        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        return appointment.Id;
    }

    /// <summary>
    /// One staff member with a spread of bookings across every scope window, so
    /// each window test asserts against the same arranged diary.
    /// </summary>
    private sealed class MobileFixture : IDisposable
    {
        public MobileFixture()
        {
            Harness = new StaffingHarness();
            StaffId = Harness.SeedStaffAsync("Jasmin", "Rae").GetAwaiter().GetResult();
            CustomerId = Harness.SeedCustomerAsync("Nadia", "Haddad").GetAwaiter().GetResult();
            var categoryId = Harness.SeedCategoryAsync("Hair").GetAwaiter().GetResult();
            ServiceId = Harness.SeedServiceAsync(categoryId, "Cut").GetAwaiter().GetResult();

            EarlierTodayId = Seed(EarlierToday);
            LaterTodayId = Seed(LaterToday);
            TomorrowId = Seed(Tomorrow);
            ThisMonthId = Seed(ThisMonth);
            NextMonthId = Seed(NextMonth);
            YesterdayId = Seed(Yesterday);
            LastQuarterId = Seed(LastQuarter);
            OlderThanAQuarterId = Seed(OlderThanAQuarter);
        }

        public StaffingHarness Harness { get; }

        public Guid StaffId { get; }

        public Guid CustomerId { get; }

        public Guid ServiceId { get; }

        public Guid EarlierTodayId { get; }
        public Guid LaterTodayId { get; }
        public Guid TomorrowId { get; }
        public Guid ThisMonthId { get; }
        public Guid NextMonthId { get; }
        public Guid YesterdayId { get; }
        public Guid LastQuarterId { get; }
        public Guid OlderThanAQuarterId { get; }

        public Task<IReadOnlyList<StaffAppointmentDto>> AppointmentsFor(StaffAppointmentScope scope) =>
            Harness.Mobile.GetAppointmentsAsync(StaffId, scope);

        private Guid Seed(DateTimeOffset startsAt) =>
            Harness.SeedAppointmentAsync(
                CustomerId, startsAt, lines: [(ServiceId, StaffId, "Cut")]).GetAwaiter().GetResult();

        public void Dispose() => Harness.Dispose();
    }
}
