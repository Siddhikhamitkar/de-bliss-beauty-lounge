using System.Net;
using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Domain.Scheduling;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// The calendar read model over the wire: query binding for the range, the
/// RFC 9457 validation body for a bad range, and the JSON shape the grid
/// consumes — columns, bookings with resolved customer names, markers.
/// </summary>
public sealed class CalendarApiTests : ApiTestBase
{
    [Fact]
    public async Task Get_returns_columns_and_bookings_for_the_range()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var staffId = await Factory.SeedStaffAsync("Jasmin", "Rae", calendarColor: "#c9a86a");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Haircut", 100m, 30, staffIds: [staffId]);
        var customerId = await Factory.SeedCustomerAsync("Sara", "Haddad", "+971 50 000 0001");

        var monday = NextMondayAt(10);
        var from = new DateTimeOffset(monday.Date, TimeSpan.Zero);
        var to = from.AddDays(1);

        await Factory.SeedCustomerAppointmentAsync(
            customerId, monday, AppointmentStatus.Confirmed, [(serviceId, staffId, "Haircut")]);

        var response = await Client.GetAsync(
            $"/api/calendar?from={Uri.EscapeDataString(from.ToString("o"))}&to={Uri.EscapeDataString(to.ToString("o"))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var calendar = await ReadAsync<CalendarDto>(response);

        Assert.Equal(from, calendar.From);
        Assert.Equal(to, calendar.To);

        var column = Assert.Single(calendar.Staff);
        Assert.Equal(staffId, column.Id);
        Assert.Equal("Jasmin", column.FirstName);
        Assert.Equal("#c9a86a", column.CalendarColor);

        var appointment = Assert.Single(calendar.Appointments);
        Assert.Equal("Sara Haddad", appointment.CustomerName);
        Assert.Equal("Confirmed", appointment.Status);

        var line = Assert.Single(appointment.Services);
        Assert.Equal("Haircut", line.ServiceName);
        Assert.Equal("Jasmin", line.StaffFirstName);
    }

    [Fact]
    public async Task A_missing_start_returns_a_validation_problem()
    {
        var response = await Client.GetAsync("/api/calendar?to=2026-10-06T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.True(problem.Errors.ContainsKey("from"));
    }

    [Fact]
    public async Task An_end_before_the_start_returns_a_validation_problem()
    {
        var monday = NextMondayAt(10);
        var from = new DateTimeOffset(monday.Date, TimeSpan.Zero);
        var to = from.AddHours(-1);

        var response = await Client.GetAsync(
            $"/api/calendar?from={Uri.EscapeDataString(from.ToString("o"))}&to={Uri.EscapeDataString(to.ToString("o"))}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.True(problem.Errors.ContainsKey("to"));
    }

    private static DateTimeOffset NextMondayAt(int hourUtc)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var daysAhead = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;

        if (daysAhead == 0)
        {
            daysAhead = 7;
        }

        return new DateTimeOffset(today.AddDays(daysAhead), TimeSpan.Zero).AddHours(hourUtc);
    }
}
