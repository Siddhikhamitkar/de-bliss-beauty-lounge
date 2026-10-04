using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.Scheduling.Dtos;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the real HTTP pipeline for booking: the RFC 9457 bodies on the
/// validation/availability failures, the 201 + Location round trip, and the
/// drag/resize and status patches. Business rules themselves are covered by
/// <see cref="Scheduling.AppointmentServiceTests"/>.
/// </summary>
public sealed class AppointmentsApiTests : ApiTestBase
{
    private const string BaseUrl = "/api/appointments";

    /// <summary>
    /// The API host runs on the real system clock, so booking times are
    /// computed relative to it: always the next Monday, never a hard-coded
    /// date the suite would outlive.
    /// </summary>
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

    private static object CreateRequest(
        Guid? customerId,
        DateTimeOffset startsAt,
        IReadOnlyList<object> services,
        string? walkInCustomerName = null,
        string? walkInCustomerPhone = null) =>
        new { customerId, walkInCustomerName, walkInCustomerPhone, startsAt, services };

    private async Task<(Guid StaffId, Guid ServiceId, Guid CustomerId, DateTimeOffset Start)> ArrangeBookableSlotAsync(
        bool withHours = true)
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var staffId = await Factory.SeedStaffAsync("Jasmin", "Rae");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Haircut", 100m, 30, staffIds: [staffId]);
        var customerId = await Factory.SeedCustomerAsync("Sara", "Haddad", "+971 50 000 0001");
        var start = NextMondayAt(10);

        if (withHours)
        {
            await Factory.SeedWorkingHoursAsync(staffId, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
        }

        return (staffId, serviceId, customerId, start);
    }

    [Fact]
    public async Task Create_returns_201_with_a_location_that_round_trips()
    {
        var (staffId, serviceId, customerId, start) = await ArrangeBookableSlotAsync();

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            CreateRequest(customerId, start, [new { serviceId, staffId }]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<AppointmentDto>(response);

        Assert.Equal(start, created.StartsAt);
        Assert.Equal(start.AddMinutes(30), created.EndsAt);
        Assert.Equal("Confirmed", created.Status);
        Assert.Equal(30, created.TotalDurationMinutes);

        var fetched = await Client.GetAsync(LocationPathOf(response));

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        var dto = await ReadAsync<AppointmentDto>(fetched);

        Assert.Equal("Sara Haddad", dto.CustomerName);
        Assert.Equal("+971 50 000 0001", dto.CustomerPhone);
        Assert.Equal("Haircut", Assert.Single(dto.Services).ServiceName);
    }

    [Fact]
    public async Task Create_with_a_customer_and_a_walk_in_name_returns_a_validation_problem()
    {
        var (staffId, serviceId, customerId, start) = await ArrangeBookableSlotAsync();

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            CreateRequest(customerId, start, [new { serviceId, staffId }], walkInCustomerName: "Maya"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.True(problem.Errors.ContainsKey("customerId"));
    }

    [Fact]
    public async Task Create_with_an_unknown_customer_returns_a_problem()
    {
        var (staffId, serviceId, _, start) = await ArrangeBookableSlotAsync();

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            CreateRequest(Guid.NewGuid(), start, [new { serviceId, staffId }]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Contains("was not found", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_outside_working_hours_returns_a_conflict_problem()
    {
        var (staffId, serviceId, customerId, start) = await ArrangeBookableSlotAsync(withHours: false);

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            CreateRequest(customerId, start, [new { serviceId, staffId }]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Contains("is not available at the selected time.", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_full_update_replaces_the_service_lines()
    {
        var (staffId, serviceId, customerId, start) = await ArrangeBookableSlotAsync();
        var categoryId = await Factory.SeedCategoryAsync("Skin");
        var colourId = await Factory.SeedServiceAsync(categoryId, "Colour", 150m, 60, staffIds: [staffId]);
        var created = await ReadAsync<AppointmentDto>(await Client.PostAsJsonAsync(
            BaseUrl,
            CreateRequest(customerId, start, [new { serviceId, staffId }])));

        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{created.Id}",
            CreateRequest(customerId, start, [new { serviceId = colourId, staffId }]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadAsync<AppointmentDto>(response);

        var line = Assert.Single(updated.Services);
        Assert.Equal("Colour", line.ServiceName);
        Assert.Equal(60, updated.TotalDurationMinutes);
        Assert.Equal(start.AddMinutes(60), updated.EndsAt);
    }

    [Fact]
    public async Task A_schedule_patch_moves_the_booking()
    {
        var (staffId, serviceId, customerId, start) = await ArrangeBookableSlotAsync();
        var created = await ReadAsync<AppointmentDto>(await Client.PostAsJsonAsync(
            BaseUrl,
            CreateRequest(customerId, start, [new { serviceId, staffId }])));
        var movedStart = NextMondayAt(12);

        var response = await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{created.Id}/schedule",
            new { startsAt = movedStart });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadAsync<AppointmentDto>(response);

        Assert.Equal(movedStart, updated.StartsAt);
        Assert.Equal(movedStart.AddMinutes(30), updated.EndsAt);
    }

    [Fact]
    public async Task A_status_patch_cancels_with_the_reason_and_then_blocks_further_changes()
    {
        var (staffId, serviceId, customerId, start) = await ArrangeBookableSlotAsync();
        var created = await ReadAsync<AppointmentDto>(await Client.PostAsJsonAsync(
            BaseUrl,
            CreateRequest(customerId, start, [new { serviceId, staffId }])));

        var cancel = await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{created.Id}/status",
            new { status = "Cancelled", reason = "Client called" });

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var cancelled = await ReadAsync<AppointmentDto>(cancel);

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Client called", cancelled.CancellationReason);

        var afterwards = await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{created.Id}/status",
            new { status = "CheckedIn" });

        Assert.Equal(HttpStatusCode.Conflict, afterwards.StatusCode);
    }

    [Fact]
    public async Task A_status_patch_with_an_unknown_value_returns_a_validation_problem()
    {
        var (staffId, serviceId, customerId, start) = await ArrangeBookableSlotAsync();
        var created = await ReadAsync<AppointmentDto>(await Client.PostAsJsonAsync(
            BaseUrl,
            CreateRequest(customerId, start, [new { serviceId, staffId }])));

        var response = await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{created.Id}/status",
            new { status = "Flying" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.True(problem.Errors.ContainsKey("status"));
    }
}
