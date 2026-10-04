using System.Net;
using DeBliss.Application.Dashboard.Dtos;
using DeBliss.Domain.Scheduling;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// The receptionist snapshot over the wire: one round trip returns the day's
/// counts, the schedule rows with resolved names, staff statuses and alerts —
/// and nothing money-shaped.
/// </summary>
public sealed class DashboardApiTests : ApiTestBase
{
    [Fact]
    public async Task Get_returns_the_receptionist_snapshot()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var staffId = await Factory.SeedStaffAsync("Jasmin", "Rae", calendarColor: "#c9a86a");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Haircut", 100m, 30, staffIds: [staffId]);
        var customerId = await Factory.SeedCustomerAsync("Sara", "Haddad", "+971 50 000 0001");
        await Factory.SeedCustomerNoteAsync(customerId, "Prefers the window chair");
        await Factory.SeedCustomerAppointmentAsync(
            customerId,
            DateTimeOffset.UtcNow.AddHours(1),
            AppointmentStatus.Confirmed,
            [(serviceId, staffId, "Haircut")]);

        var response = await Client.GetAsync("/api/dashboard/receptionist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dashboard = await ReadAsync<ReceptionistDashboardDto>(response);

        Assert.Equal(1, dashboard.Today.Total);
        Assert.Equal(1, dashboard.Today.Upcoming);

        var appointment = Assert.Single(dashboard.Schedule);
        Assert.Equal("Sara Haddad", appointment.CustomerName);
        Assert.Equal("Haircut", appointment.ServicesLabel);
        Assert.Equal("Jasmin", appointment.StaffName);
        Assert.False(appointment.CanCheckout);

        var staff = Assert.Single(dashboard.Staff);
        Assert.Equal("Jasmin", staff.FirstName);

        var note = Assert.Single(dashboard.Alerts.RecentNotes);
        Assert.Equal("Prefers the window chair", note.Note);
        Assert.Equal("Sara Haddad", note.CustomerName);
    }

    [Fact]
    public async Task The_snapshot_contains_no_money_fields_on_the_wire()
    {
        var response = await Client.GetAsync("/api/dashboard/receptionist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadAsStringAsync()).ToLowerInvariant();
        foreach (var forbidden in new[] { "\"price", "\"amount", "\"revenue", "\"subtotal", "\"discount\"" })
        {
            Assert.DoesNotContain(forbidden, body);
        }
    }
}
