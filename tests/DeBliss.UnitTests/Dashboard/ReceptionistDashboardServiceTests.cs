using System.Text.Json;
using DeBliss.Domain.Scheduling;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.Dashboard;

/// <summary>
/// The receptionist snapshot over the real repositories: day-boundary handling,
/// status counts, schedule/upcoming shaping, staff right-now statuses, and the
/// alert queries. Everything seeds relative to the harness clock, never wall
/// clock, so the tests run the same in every timezone.
/// </summary>
public sealed class ReceptionistDashboardServiceTests : IDisposable
{
    private readonly DashboardHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private DateTimeOffset Now => _harness.Now;

    [Fact]
    public async Task An_empty_day_reports_zeros_and_empty_lists()
    {
        var dashboard = await _harness.Dashboard.GetAsync();

        Assert.Equal(Now, dashboard.GeneratedAt);
        Assert.Equal(0, dashboard.Today.Total);
        Assert.Equal(0, dashboard.Today.Completed);
        Assert.Equal(0, dashboard.Today.Upcoming);
        Assert.Equal(0, dashboard.Today.WalkIns);
        Assert.Empty(dashboard.Schedule);
        Assert.Empty(dashboard.Upcoming);
        Assert.Empty(dashboard.Staff);
        Assert.Equal(0, dashboard.Alerts.BirthdaysToday);
        Assert.Empty(dashboard.Alerts.BirthdayNamesToday);
        Assert.Empty(dashboard.Alerts.BirthdayNamesLaterThisWeek);
        Assert.Equal(0, dashboard.Alerts.FirstVisitsToday);
        Assert.Empty(dashboard.Alerts.RecentNotes);
        Assert.Empty(dashboard.PopularServices);
    }

    [Fact]
    public async Task Today_counts_split_by_status_and_count_walk_ins()
    {
        var customerId = await _harness.SeedCustomerAsync("Sara", "Haddad");
        var otherCustomerId = await _harness.SeedCustomerAsync("Mona", "Nader");

        await _harness.SeedAppointmentAsync(Now.AddHours(-3), Now.AddHours(-2), AppointmentStatus.Completed, customerId);
        await _harness.SeedAppointmentAsync(Now.AddMinutes(-30), Now.AddMinutes(30), AppointmentStatus.InProgress, customerId);
        await _harness.SeedAppointmentAsync(Now.AddHours(1), Now.AddHours(2), AppointmentStatus.Confirmed, customerId);
        await _harness.SeedAppointmentAsync(Now.AddHours(-1), Now, AppointmentStatus.NoShow, otherCustomerId);
        await _harness.SeedAppointmentAsync(Now.AddHours(4), Now.AddHours(5), AppointmentStatus.Cancelled, otherCustomerId);
        await _harness.SeedAppointmentAsync(Now.AddHours(2), Now.AddHours(3), AppointmentStatus.Confirmed, walkInCustomerName: "Drop-in Dana");

        var dashboard = await _harness.Dashboard.GetAsync();

        Assert.Equal(6, dashboard.Today.Total);
        Assert.Equal(1, dashboard.Today.Completed);
        Assert.Equal(1, dashboard.Today.InProgress);
        Assert.Equal(2, dashboard.Today.Upcoming);
        Assert.Equal(1, dashboard.Today.NoShow);
        Assert.Equal(1, dashboard.Today.Cancelled);
        Assert.Equal(1, dashboard.Today.WalkIns);
    }

    [Fact]
    public async Task Schedule_lists_today_ascending_with_labels_and_checkout_flags()
    {
        var customerId = await _harness.SeedCustomerAsync("Sara", "Haddad");
        var staffId = await _harness.SeedStaffAsync("Jasmin", "Rae", calendarColor: "#c9a86a");
        var categoryId = await _harness.SeedCategoryAsync("Hair");
        var serviceId = await _harness.SeedServiceAsync(categoryId, "Haircut");

        await _harness.SeedAppointmentAsync(
            Now.AddHours(2), Now.AddHours(3), AppointmentStatus.Confirmed, customerId,
            lines: [new SchedulingSeed.Line(serviceId, staffId, "Haircut")]);
        var completedId = await _harness.SeedAppointmentAsync(
            Now.AddHours(-2), Now.AddHours(-1), AppointmentStatus.Completed, customerId,
            lines: [new SchedulingSeed.Line(serviceId, staffId, "Colour")]);
        await _harness.SeedAppointmentAsync(
            Now.AddHours(4), Now.AddHours(5), AppointmentStatus.Confirmed, walkInCustomerName: null);

        var dashboard = await _harness.Dashboard.GetAsync();

        var schedule = dashboard.Schedule;
        Assert.Equal(3, schedule.Count);
        Assert.True(schedule[0].StartsAt < schedule[1].StartsAt && schedule[1].StartsAt < schedule[2].StartsAt);

        var completed = schedule[0];
        Assert.Equal(completedId, completed.Id);
        Assert.Equal("Sara Haddad", completed.CustomerName);
        Assert.Equal("Colour", completed.ServicesLabel);
        Assert.Equal("Jasmin", completed.StaffName);
        Assert.Equal("#c9a86a", completed.StaffColor);
        Assert.True(completed.CanCheckout);

        var walkIn = schedule[2];
        Assert.Equal("Walk-in", walkIn.CustomerName);
        Assert.False(walkIn.CanCheckout);
    }

    [Fact]
    public async Task Upcoming_skips_finished_appointments_and_caps_at_five()
    {
        var customerId = await _harness.SeedCustomerAsync("Sara", "Haddad");

        for (var i = 0; i < 6; i++)
        {
            await _harness.SeedAppointmentAsync(
                Now.AddHours(1 + i), Now.AddHours(2 + i), AppointmentStatus.Confirmed, customerId);
        }

        await _harness.SeedAppointmentAsync(Now.AddHours(1), Now.AddHours(2), AppointmentStatus.Cancelled, customerId);
        await _harness.SeedAppointmentAsync(Now.AddHours(1), Now.AddHours(2), AppointmentStatus.NoShow, customerId);
        await _harness.SeedAppointmentAsync(Now.AddHours(-1), Now, AppointmentStatus.Completed, customerId);

        var dashboard = await _harness.Dashboard.GetAsync();

        Assert.Equal(5, dashboard.Upcoming.Count);
        Assert.Equal(dashboard.Upcoming.OrderBy(appointment => appointment.StartsAt), dashboard.Upcoming);
        Assert.All(dashboard.Upcoming, appointment => Assert.False(appointment.CanCheckout));
    }

    [Fact]
    public async Task Staff_statuses_reflect_time_off_live_bookings_hours_and_blocks()
    {
        var customerId = await _harness.SeedCustomerAsync("Sara", "Haddad");
        var categoryId = await _harness.SeedCategoryAsync("Hair");
        var serviceId = await _harness.SeedServiceAsync(categoryId, "Haircut");

        var day = Now.DayOfWeek;
        var nowTime = Now.TimeOfDay;

        // On the clock, inside working hours, nothing blocking.
        var availableId = await _harness.SeedStaffAsync("Nadia", "One");
        await _harness.SeedWorkingHoursAsync(availableId, day, nowTime.Add(TimeSpan.FromHours(-2)), nowTime.Add(TimeSpan.FromHours(4)));

        // On the clock, inside working hours, but on a break right now.
        var breakId = await _harness.SeedStaffAsync("Samar", "Two");
        await _harness.SeedWorkingHoursAsync(breakId, day, nowTime.Add(TimeSpan.FromHours(-2)), nowTime.Add(TimeSpan.FromHours(4)));
        await _harness.SeedBlockedTimeAsync(breakId, Now.AddMinutes(-15), Now.AddMinutes(15), reason: "Lunch");

        // On the clock between two intervals: back when the next one starts.
        var gapId = await _harness.SeedStaffAsync("Yasmin", "Three");
        var nextStart = nowTime.Add(TimeSpan.FromHours(1));
        await _harness.SeedWorkingHoursAsync(gapId, day, nowTime.Add(TimeSpan.FromHours(-4)), nowTime.Add(TimeSpan.FromHours(-1)));
        await _harness.SeedWorkingHoursAsync(gapId, day, nextStart, nowTime.Add(TimeSpan.FromHours(3)));

        // Starts later today.
        var laterId = await _harness.SeedStaffAsync("Noor", "Four");
        var laterStart = nowTime.Add(TimeSpan.FromHours(2));
        await _harness.SeedWorkingHoursAsync(laterId, day, laterStart, nowTime.Add(TimeSpan.FromHours(5)));

        // On leave covering right now.
        var offId = await _harness.SeedStaffAsync("Huda", "Five");
        await _harness.SeedTimeOffAsync(offId, Now.AddDays(-1), Now.AddDays(1), reason: "Annual leave");

        // Mid-appointment right now.
        var withClientId = await _harness.SeedStaffAsync("Mira", "Six");
        await _harness.SeedWorkingHoursAsync(withClientId, day, nowTime.Add(TimeSpan.FromHours(-4)), nowTime.Add(TimeSpan.FromHours(4)));
        await _harness.SeedAppointmentAsync(
            Now.AddMinutes(-30), Now.AddMinutes(30), AppointmentStatus.InProgress, customerId,
            lines: [new SchedulingSeed.Line(serviceId, withClientId, "Haircut")]);

        // No working hours at all.
        var noHoursId = await _harness.SeedStaffAsync("Lulu", "Seven");

        var dashboard = await _harness.Dashboard.GetAsync();
        var byId = dashboard.Staff.ToDictionary(staff => staff.Id);

        Assert.Equal("Available", byId[availableId].Status);
        Assert.Null(byId[availableId].StatusNote);

        Assert.Equal("Break", byId[breakId].Status);
        Assert.Equal("Lunch", byId[breakId].StatusNote);

        Assert.Equal("Break", byId[gapId].Status);
        Assert.Equal($"Back at {nextStart:hh\\:mm}", byId[gapId].StatusNote);

        Assert.Equal("Off", byId[laterId].Status);
        Assert.Equal($"In at {laterStart:hh\\:mm}", byId[laterId].StatusNote);

        Assert.Equal("Off", byId[offId].Status);
        Assert.Equal("Annual leave", byId[offId].StatusNote);

        Assert.Equal("WithClient", byId[withClientId].Status);

        Assert.Equal("Off", byId[noHoursId].Status);
        Assert.Null(byId[noHoursId].StatusNote);
    }

    [Fact]
    public async Task A_live_booking_loses_to_time_off_in_the_status_derivation()
    {
        var customerId = await _harness.SeedCustomerAsync("Sara", "Haddad");
        var categoryId = await _harness.SeedCategoryAsync("Hair");
        var serviceId = await _harness.SeedServiceAsync(categoryId, "Haircut");

        var staffId = await _harness.SeedStaffAsync("Huda", "Five");
        await _harness.SeedWorkingHoursAsync(staffId, Now.DayOfWeek, Now.TimeOfDay.Add(TimeSpan.FromHours(-4)), Now.TimeOfDay.Add(TimeSpan.FromHours(4)));
        await _harness.SeedTimeOffAsync(staffId, Now.AddMinutes(-15), Now.AddMinutes(15), reason: "Doctor");
        await _harness.SeedAppointmentAsync(
            Now.AddMinutes(-30), Now.AddMinutes(30), AppointmentStatus.InProgress, customerId,
            lines: [new SchedulingSeed.Line(serviceId, staffId, "Haircut")]);

        var dashboard = await _harness.Dashboard.GetAsync();
        var staff = Assert.Single(dashboard.Staff);

        Assert.Equal("Off", staff.Status);
        Assert.Equal("Doctor", staff.StatusNote);
    }

    [Fact]
    public async Task Birthdays_cover_today_and_the_week_ahead()
    {
        var today = DateOnly.FromDateTime(_harness.Now.Date);
        var ahead = today.AddDays(4);
        var outside = today.AddDays(9);

        await _harness.SeedCustomerAsync("Born", "Today", dateOfBirth: new DateOnly(1991, today.Month, today.Day));
        await _harness.SeedCustomerAsync("Born", "Ahead", dateOfBirth: new DateOnly(1988, ahead.Month, ahead.Day));
        await _harness.SeedCustomerAsync("Born", "Outside", dateOfBirth: new DateOnly(1990, outside.Month, outside.Day));

        var dashboard = await _harness.Dashboard.GetAsync();

        Assert.Equal(1, dashboard.Alerts.BirthdaysToday);
        Assert.Contains("Born Today", dashboard.Alerts.BirthdayNamesToday);
        Assert.Contains("Born Ahead", dashboard.Alerts.BirthdayNamesLaterThisWeek);
        Assert.DoesNotContain("Born Outside", dashboard.Alerts.BirthdayNamesLaterThisWeek);
    }

    [Fact]
    public async Task Birthdays_match_month_and_day_across_a_year_boundary()
    {
        // Roll the clock to 29 December: the 7-day window has to see 1 January.
        _harness.Database.Clock.Advance(TimeSpan.FromDays(87));
        var today = DateOnly.FromDateTime(_harness.Now.Date);

        await _harness.SeedCustomerAsync("New", "Year", dateOfBirth: new DateOnly(1995, 1, 1));
        await _harness.SeedCustomerAsync("This", "Day", dateOfBirth: new DateOnly(1980, today.Month, today.Day));

        var dashboard = await _harness.Dashboard.GetAsync();

        Assert.Equal(1, dashboard.Alerts.BirthdaysToday);
        Assert.Contains("This Day", dashboard.Alerts.BirthdayNamesToday);
        Assert.Contains("New Year", dashboard.Alerts.BirthdayNamesLaterThisWeek);
        Assert.DoesNotContain("New Year", dashboard.Alerts.BirthdayNamesToday);
    }

    [Fact]
    public async Task First_visits_count_only_customers_whose_first_appointment_is_today()
    {
        var firstTimerId = await _harness.SeedCustomerAsync("First", "Timer");
        var regularId = await _harness.SeedCustomerAsync("Regular", "Guest");

        await _harness.SeedAppointmentAsync(Now.AddHours(2), Now.AddHours(3), AppointmentStatus.Confirmed, firstTimerId);
        await _harness.SeedAppointmentAsync(Now.AddDays(-7), Now.AddDays(-7).AddHours(1), AppointmentStatus.Completed, regularId);
        await _harness.SeedAppointmentAsync(Now.AddHours(4), Now.AddHours(5), AppointmentStatus.Confirmed, regularId);

        var dashboard = await _harness.Dashboard.GetAsync();

        Assert.Equal(1, dashboard.Alerts.FirstVisitsToday);
    }

    [Fact]
    public async Task Recent_notes_return_the_newest_three_with_customer_names()
    {
        var saraId = await _harness.SeedCustomerAsync("Sara", "Haddad");
        var monaId = await _harness.SeedCustomerAsync("Mona", "Nader");

        await _harness.SeedNoteAsync(saraId, "oldest");
        _harness.Database.Clock.Advance(TimeSpan.FromMinutes(1));
        await _harness.SeedNoteAsync(monaId, "second");
        _harness.Database.Clock.Advance(TimeSpan.FromMinutes(1));
        await _harness.SeedNoteAsync(saraId, "third");
        _harness.Database.Clock.Advance(TimeSpan.FromMinutes(1));
        await _harness.SeedNoteAsync(monaId, "newest");

        var dashboard = await _harness.Dashboard.GetAsync();

        var notes = dashboard.Alerts.RecentNotes;
        Assert.Equal(3, notes.Count);
        Assert.Equal("newest", notes[0].Note);
        Assert.Equal("Mona Nader", notes[0].CustomerName);
        Assert.Equal("third", notes[1].Note);
        Assert.Equal("Sara Haddad", notes[1].CustomerName);
        Assert.Equal("second", notes[2].Note);
    }

    [Fact]
    public async Task Popular_services_count_todays_bookings_and_skip_cancelled()
    {
        var customerId = await _harness.SeedCustomerAsync("Sara", "Haddad");
        var categoryId = await _harness.SeedCategoryAsync("Hair");
        var haircutId = await _harness.SeedServiceAsync(categoryId, "Haircut");
        var colourId = await _harness.SeedServiceAsync(categoryId, "Colour");

        await _harness.SeedAppointmentAsync(
            Now.AddHours(1), Now.AddHours(2), AppointmentStatus.Confirmed, customerId,
            lines: [new SchedulingSeed.Line(haircutId, null, "Haircut")]);
        await _harness.SeedAppointmentAsync(
            Now.AddHours(2), Now.AddHours(3), AppointmentStatus.Confirmed, customerId,
            lines:
            [
                new SchedulingSeed.Line(haircutId, null, "Haircut"),
                new SchedulingSeed.Line(colourId, null, "Colour")
            ]);
        await _harness.SeedAppointmentAsync(
            Now.AddHours(3), Now.AddHours(4), AppointmentStatus.Cancelled, customerId,
            lines: [new SchedulingSeed.Line(colourId, null, "Colour")]);

        var dashboard = await _harness.Dashboard.GetAsync();

        var popular = dashboard.PopularServices.ToDictionary(service => service.Name);
        Assert.Equal(2, popular["Haircut"].Count);
        Assert.Equal(1, popular["Colour"].Count);
    }

    [Fact]
    public async Task The_snapshot_serialises_without_any_money_fields()
    {
        var customerId = await _harness.SeedCustomerAsync("Sara", "Haddad");
        await _harness.SeedAppointmentAsync(
            Now.AddHours(1), Now.AddHours(2), AppointmentStatus.Confirmed, customerId);

        var dashboard = await _harness.Dashboard.GetAsync();
        var json = JsonSerializer.Serialize(
            dashboard, new JsonSerializerOptions(JsonSerializerDefaults.Web)).ToLowerInvariant();

        foreach (var forbidden in new[] { "\"price", "\"amount", "\"revenue", "\"subtotal", "\"discount", "\"tax\"" })
        {
            Assert.DoesNotContain(forbidden, json);
        }
    }
}
