using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.Identity;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Domain.Scheduling;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// The staff mobile surface over the real pipeline: a STAFF sign-in resolves to
/// the linked Staff record, the "me" routes can only ever read that record, and
/// the attendance commands hold the wire contract a future Expo client will rely
/// on. The API host keeps the real system clock (the factory replaces the
/// database, not the clock), so every "today" and "now" here is derived from
/// <see cref="DateTimeOffset.UtcNow"/> rather than a fixed date.
/// </summary>
public sealed class StaffMobileApiTests : ApiTestBase
{
    private const string StaffEmail = "aisha@debliss.test";
    private const string StaffPassword = "Test#Staff123";
    private const string ColleagueEmail = "ben@debliss.test";
    private const string ReceptionistEmail = "reception@debliss.test";
    private const string ReceptionistPassword = "Test#Front123";

    public StaffMobileApiTests()
    {
        StaffId = Factory.SeedStaffAsync(
            "Aisha", "Nasser", jobTitle: "Senior Stylist", email: "aisha@debliss.ae").GetAwaiter().GetResult();
        Factory.SeedUserAsync(
            StaffEmail, StaffPassword, RoleNames.Staff, "Aisha", "Nasser", linkedStaffId: StaffId)
            .GetAwaiter().GetResult();
    }

    private Guid StaffId { get; }

    /// <summary>The UTC date the API's real clock will call "today" during this test.</summary>
    private static DateOnly UtcToday => DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

    /// <summary>A time inside the given day's calendar window regardless of when the test runs.</summary>
    private static DateTimeOffset NoonUtcOn(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(12);

    private async Task<HttpClient> SignInAsStaffAsync()
    {
        var client = AnonymousClient;
        await AuthenticateAsync(client, StaffEmail, StaffPassword);
        return client;
    }

    [Fact]
    public async Task A_staff_sign_in_resolves_to_the_linked_staff_record()
    {
        using var client = await SignInAsStaffAsync();

        var profile = await ReadAsync<StaffMobileProfileDto>(await client.GetAsync("/api/staff/me"));

        Assert.Equal(StaffId, profile.StaffId);
        Assert.Equal("Aisha", profile.FirstName);
        Assert.Equal("Nasser", profile.LastName);
        Assert.Equal("aisha@debliss.ae", profile.Email);
        Assert.Equal("Senior Stylist", profile.JobTitle);
        Assert.True(profile.IsActive);
    }

    [Fact]
    public async Task A_staff_user_without_a_staff_link_is_refused()
    {
        await Factory.SeedUserAsync("unlinked@debliss.test", StaffPassword, RoleNames.Staff, "No", "Link");
        using var client = AnonymousClient;
        await AuthenticateAsync(client, "unlinked@debliss.test", StaffPassword);

        foreach (var url in new[]
        {
            "/api/staff/me",
            "/api/staff/me/appointments",
            "/api/staff/me/schedule",
            "/api/staff/me/attendance/today",
            "/api/staff/me/attendance"
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsync("/api/staff/me/attendance/check-in", null)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_are_sent_to_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await AnonymousClient.GetAsync("/api/staff/me")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await AnonymousClient.GetAsync($"/api/staff/{StaffId}/attendance")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await AnonymousClient.PostAsync("/api/staff/me/attendance/check-in", null)).StatusCode);
    }

    [Fact]
    public async Task A_staff_member_sees_only_their_own_bookings()
    {
        var colleagueId = await Factory.SeedStaffAsync("Ben", "Osei");
        await Factory.SeedUserAsync(
            ColleagueEmail, StaffPassword, RoleNames.Staff, "Ben", "Osei", linkedStaffId: colleagueId);

        var customerId = await Factory.SeedCustomerAsync("Nadia", "Haddad");
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Cut", 40m, durationMinutes: 45);
        await Factory.SeedCustomerAppointmentAsync(
            customerId, NoonUtcOn(UtcToday), AppointmentStatus.Confirmed, [(serviceId, colleagueId, "Cut")]);

        using var aisha = await SignInAsStaffAsync();
        var aishaToday = await ReadAsync<IReadOnlyList<StaffAppointmentDto>>(
            await aisha.GetAsync("/api/staff/me/appointments?scope=Today"));
        Assert.Empty(aishaToday);

        using var ben = AnonymousClient;
        await AuthenticateAsync(ben, ColleagueEmail, StaffPassword);
        var benToday = await ReadAsync<IReadOnlyList<StaffAppointmentDto>>(
            await ben.GetAsync("/api/staff/me/appointments?scope=Today"));

        var booking = Assert.Single(benToday);

        Assert.Equal("Nadia Haddad", booking.CustomerName);
        Assert.Equal("Confirmed", booking.Status);
        // CustomerSeed stamps a 60-minute snapshot per line, whatever the catalogue says.
        Assert.Equal(60, booking.DurationMinutes);
        Assert.Equal("Cut", booking.Services[0].ServiceName);
    }

    [Fact]
    public async Task The_appointment_scope_is_selected_by_name_and_defaults_to_today()
    {
        var customerId = await Factory.SeedCustomerAsync("Nadia", "Haddad");
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Cut", 40m);
        var nearer = await Factory.SeedCustomerAppointmentAsync(
            customerId, DateTimeOffset.UtcNow.AddDays(2), AppointmentStatus.Confirmed, [(serviceId, StaffId, "Cut")]);
        var further = await Factory.SeedCustomerAppointmentAsync(
            customerId, DateTimeOffset.UtcNow.AddDays(3), AppointmentStatus.Confirmed, [(serviceId, StaffId, "Cut")]);
        var recent = await Factory.SeedCustomerAppointmentAsync(
            customerId, DateTimeOffset.UtcNow.AddDays(-10), AppointmentStatus.Completed, [(serviceId, StaffId, "Cut")]);
        var older = await Factory.SeedCustomerAppointmentAsync(
            customerId, DateTimeOffset.UtcNow.AddDays(-20), AppointmentStatus.Completed, [(serviceId, StaffId, "Cut")]);

        using var client = await SignInAsStaffAsync();

        var today = await ReadAsync<IReadOnlyList<StaffAppointmentDto>>(
            await client.GetAsync("/api/staff/me/appointments?scope=Today"));
        Assert.Empty(today);

        var omitted = await ReadAsync<IReadOnlyList<StaffAppointmentDto>>(
            await client.GetAsync("/api/staff/me/appointments"));
        Assert.Empty(omitted);

        var upcoming = await ReadAsync<IReadOnlyList<StaffAppointmentDto>>(
            await client.GetAsync("/api/staff/me/appointments?scope=Upcoming"));
        Assert.Equal([nearer, further], upcoming.Select(each => each.Id).ToArray());

        var history = await ReadAsync<IReadOnlyList<StaffAppointmentDto>>(
            await client.GetAsync("/api/staff/me/appointments?scope=History"));
        Assert.Equal([recent, older], history.Select(each => each.Id).ToArray());
    }

    [Fact]
    public async Task The_mobile_projection_carries_no_prices_or_contact_details()
    {
        var customerId = await Factory.SeedCustomerAsync(
            "Nadia", "Haddad", phoneNumber: "+971501112233", email: "nadia@example.com");
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Cut", 123.45m);
        await Factory.SeedCustomerAppointmentAsync(
            customerId, NoonUtcOn(UtcToday), AppointmentStatus.Confirmed, [(serviceId, StaffId, "Cut")]);

        using var client = await SignInAsStaffAsync();

        var response = await client.GetAsync("/api/staff/me/appointments?scope=Today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Nadia Haddad", body);
        Assert.DoesNotContain("123.45", body);
        Assert.DoesNotContain("+971501112233", body);
        Assert.DoesNotContain("nadia@example.com", body);
        Assert.DoesNotContain("price", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_schedule_is_derived_from_the_owner_maintained_pattern()
    {
        await Factory.SeedWorkingHoursAsync(
            StaffId, DayOfWeek.Friday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

        using var client = await SignInAsStaffAsync();

        var schedule = await ReadAsync<StaffScheduleDto>(await client.GetAsync("/api/staff/me/schedule"));

        Assert.Equal(UtcToday, schedule.Today);
        Assert.Equal(7, schedule.Days.Count);
        Assert.Equal(UtcToday, schedule.Days[0].Date);

        var friday = schedule.Days.Single(day => day.Date.DayOfWeek == DayOfWeek.Friday);

        Assert.True(friday.IsWorkingDay);
        Assert.Equal(("09:00", "17:00"), (friday.Intervals[0].Start, friday.Intervals[0].End));

        Assert.All(
            schedule.Days.Where(day => day.Date.DayOfWeek != DayOfWeek.Friday),
            day => Assert.False(day.IsWorkingDay));

        Assert.Equal(7, schedule.WeeklyPattern.Days.Count);
        Assert.Equal("Friday", Assert.Single(schedule.WeeklyPattern.Days, day => day.IsWorkingDay).DayOfWeek);
    }

    [Fact]
    public async Task The_owner_reads_any_staff_member_s_attendance()
    {
        using var client = await SignInAsStaffAsync();
        await client.PostAsync("/api/staff/me/attendance/check-in", null);

        var history = await ReadAsync<IReadOnlyList<StaffAttendanceDto>>(
            await Client.GetAsync($"/api/staff/{StaffId}/attendance"));

        var entry = Assert.Single(history);

        Assert.Equal(StaffId, entry.StaffId);
        Assert.Equal(UtcToday, entry.WorkDate);
        Assert.Equal("CheckedIn", entry.Status);
        Assert.Null(entry.CheckOutAt);
    }

    [Fact]
    public async Task A_receptionist_keeps_operational_staff_access_but_not_attendance()
    {
        await Factory.SeedUserAsync(
            ReceptionistEmail, ReceptionistPassword, RoleNames.Receptionist, "Rita", "Desk");
        using var client = AnonymousClient;
        await AuthenticateAsync(client, ReceptionistEmail, ReceptionistPassword);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/staff/{StaffId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/staff/{StaffId}/working-hours")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/staff/{StaffId}/time-off")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/staff/{StaffId}/attendance")).StatusCode);
    }

    [Fact]
    public async Task A_staff_member_cannot_read_owner_only_staff_views()
    {
        var colleagueId = await Factory.SeedStaffAsync("Ben", "Osei");

        using var client = await SignInAsStaffAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/staff/{colleagueId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/staff/{colleagueId}/attendance")).StatusCode);
    }

    [Fact]
    public async Task A_staff_member_s_attendance_never_contains_a_colleague_s()
    {
        var colleagueId = await Factory.SeedStaffAsync("Ben", "Osei");
        await Factory.SeedUserAsync(
            ColleagueEmail, StaffPassword, RoleNames.Staff, "Ben", "Osei", linkedStaffId: colleagueId);

        using var ben = AnonymousClient;
        await AuthenticateAsync(ben, ColleagueEmail, StaffPassword);
        Assert.Equal(
            HttpStatusCode.OK, (await ben.PostAsync("/api/staff/me/attendance/check-in", null)).StatusCode);

        using var aisha = await SignInAsStaffAsync();

        var history = await ReadAsync<IReadOnlyList<StaffAttendanceDto>>(
            await aisha.GetAsync("/api/staff/me/attendance"));
        Assert.Empty(history);

        var today = await aisha.GetAsync("/api/staff/me/attendance/today");

        Assert.True(today.IsSuccessStatusCode);
        Assert.Empty(await today.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Check_in_stamps_the_server_time_and_ignores_a_client_time()
    {
        using var client = await SignInAsStaffAsync();

        var clientClaimedTime = DateTimeOffset.UtcNow.AddDays(-3);
        var before = DateTimeOffset.UtcNow;
        var response = await client.PostAsJsonAsync(
            "/api/staff/me/attendance/check-in", new { checkInAt = clientClaimedTime });
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entry = await ReadAsync<StaffAttendanceDto>(response);

        Assert.Equal(StaffId, entry.StaffId);
        Assert.Equal(UtcToday, entry.WorkDate);
        Assert.Equal("CheckedIn", entry.Status);
        Assert.Null(entry.CheckOutAt);
        Assert.InRange(entry.CheckInAt, before, after);

        var today = await ReadAsync<StaffAttendanceDto>(
            await client.GetAsync("/api/staff/me/attendance/today"));

        Assert.Equal(entry.Id, today.Id);
        Assert.Equal(entry.CheckInAt, today.CheckInAt);
    }

    [Fact]
    public async Task A_duplicate_check_in_is_rejected()
    {
        using var client = await SignInAsStaffAsync();

        Assert.Equal(
            HttpStatusCode.OK, (await client.PostAsync("/api/staff/me/attendance/check-in", null)).StatusCode);

        var second = await client.PostAsync("/api/staff/me/attendance/check-in", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await ReadAsync<Problem>(second);

        Assert.Equal(409, problem.Status);
        Assert.Equal("You are already checked in for today.", problem.Detail);

        var history = await ReadAsync<IReadOnlyList<StaffAttendanceDto>>(
            await client.GetAsync("/api/staff/me/attendance"));
        Assert.Single(history);
    }

    [Fact]
    public async Task Check_out_closes_the_day_with_the_server_time()
    {
        using var client = await SignInAsStaffAsync();

        var opened = await ReadAsync<StaffAttendanceDto>(
            await client.PostAsync("/api/staff/me/attendance/check-in", null));

        var before = DateTimeOffset.UtcNow;
        var closed = await ReadAsync<StaffAttendanceDto>(
            await client.PostAsync("/api/staff/me/attendance/check-out", null));
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(opened.Id, closed.Id);
        Assert.Equal("CheckedOut", closed.Status);
        Assert.Equal(opened.CheckInAt, closed.CheckInAt);
        Assert.InRange(closed.CheckOutAt!.Value, before, after);

        var today = await ReadAsync<StaffAttendanceDto>(
            await client.GetAsync("/api/staff/me/attendance/today"));

        Assert.Equal("CheckedOut", today.Status);
    }

    [Fact]
    public async Task Checking_out_without_checking_in_is_rejected()
    {
        using var client = await SignInAsStaffAsync();

        var response = await client.PostAsync("/api/staff/me/attendance/check-out", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Check in before checking out.", (await ReadAsync<Problem>(response)).Detail);
    }

    [Fact]
    public async Task A_duplicate_check_out_is_rejected_and_the_day_stays_closed()
    {
        using var client = await SignInAsStaffAsync();

        await client.PostAsync("/api/staff/me/attendance/check-in", null);
        Assert.Equal(
            HttpStatusCode.OK, (await client.PostAsync("/api/staff/me/attendance/check-out", null)).StatusCode);

        var secondCheckout = await client.PostAsync("/api/staff/me/attendance/check-out", null);

        Assert.Equal(HttpStatusCode.Conflict, secondCheckout.StatusCode);
        Assert.Equal("You have already checked out for today.", (await ReadAsync<Problem>(secondCheckout)).Detail);

        var reopened = await client.PostAsync("/api/staff/me/attendance/check-in", null);

        Assert.Equal(HttpStatusCode.Conflict, reopened.StatusCode);
        Assert.Equal("Today's attendance has already been closed.", (await ReadAsync<Problem>(reopened)).Detail);

        var entry = Assert.Single(await ReadAsync<IReadOnlyList<StaffAttendanceDto>>(
            await client.GetAsync("/api/staff/me/attendance")));

        Assert.NotNull(entry.CheckOutAt);
    }

    [Fact]
    public async Task Attendance_history_honours_an_explicit_range_and_rejects_a_bad_one()
    {
        using var client = await SignInAsStaffAsync();
        await client.PostAsync("/api/staff/me/attendance/check-in", null);

        var from = UtcToday.AddDays(-3);
        var to = UtcToday;

        var history = await ReadAsync<IReadOnlyList<StaffAttendanceDto>>(
            await client.GetAsync($"/api/staff/me/attendance?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

        Assert.Equal(UtcToday, Assert.Single(history).WorkDate);

        var inverted = await client.GetAsync(
            $"/api/staff/me/attendance?from={to.AddDays(1):yyyy-MM-dd}&to={from:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        Assert.Contains("to", (await ReadAsync<ValidationProblem>(inverted)).Errors.Keys);

        var tooWide = await client.GetAsync(
            $"/api/staff/me/attendance?from={UtcToday.AddDays(-400):yyyy-MM-dd}&to={to:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, tooWide.StatusCode);
        Assert.Contains("to", (await ReadAsync<ValidationProblem>(tooWide)).Errors.Keys);
    }

    [Fact]
    public async Task The_owner_s_attendance_view_carries_the_scheduled_shift()
    {
        await Factory.SeedWorkingHoursAsync(
            StaffId, UtcToday.DayOfWeek, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

        using var client = await SignInAsStaffAsync();
        await client.PostAsync("/api/staff/me/attendance/check-in", null);

        var entry = Assert.Single(await ReadAsync<IReadOnlyList<StaffAttendanceDto>>(
            await Client.GetAsync($"/api/staff/{StaffId}/attendance")));

        Assert.True(entry.IsScheduledWorkingDay);

        var interval = Assert.Single(entry.ScheduledIntervals);

        Assert.Equal(("09:00", "17:00"), (interval.Start, interval.End));
    }

    [Fact]
    public async Task Attendance_history_for_an_unknown_staff_member_is_not_found()
    {
        var response = await Client.GetAsync($"/api/staff/{Guid.NewGuid()}/attendance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await ReadAsync<Problem>(response)).Status);
    }
}
