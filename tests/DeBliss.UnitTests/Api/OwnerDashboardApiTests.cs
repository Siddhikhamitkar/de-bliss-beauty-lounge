using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeBliss.Application.Identity;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the owner dashboard through the real HTTP pipeline: the owner reads
/// the full financial snapshot, receptionists and staff are turned away with
/// 403 — no revenue, VAT, tip or outstanding figure may reach the front desk —
/// and a bad range request is a field-level validation problem.
/// </summary>
public sealed class OwnerDashboardApiTests : ApiTestBase
{
    private const string Url = "/api/dashboard/owner";
    private const string ReceptionistEmail = "receptionist@debliss.test";
    private const string ReceptionistPassword = "Test#Front123";
    private const string StaffEmail = "staff@debliss.test";
    private const string StaffPassword = "Test#Staff123";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Owner_receives_the_full_dashboard_after_a_real_checkout()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var haircutId = await Factory.SeedServiceAsync(categoryId, "Haircut", 100m);
        var customerId = await Factory.SeedCustomerAsync("Layla", "Haddad", phoneNumber: "+971 50 000 0000");
        var appointmentId = await Factory.SeedBillingAppointmentAsync(
            customerId,
            DateTimeOffset.UtcNow.AddHours(-1),
            lines: [(haircutId, "Haircut", 100m, 0)]);

        var started = await Client.PostAsJsonAsync("/api/invoices", new { appointmentId });
        var invoice = await ReadAsync<InvoiceRef>(started);

        var paid = await Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/checkout", new
        {
            paymentMethod = "Cash",
            amount = 100m
        });

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        var dashboard = await ReadAsync<OwnerDashboardWire>(await Client.GetAsync(Url));

        Assert.Equal(100m, dashboard.Kpis.NetRevenueToday);
        Assert.Equal("Today", dashboard.Revenue.RangeLabel);
        Assert.Equal(0m, dashboard.Revenue.VatTotal);
        Assert.Equal(0m, dashboard.Revenue.TipTotal);
        Assert.Equal(1, dashboard.Kpis.PaidInvoiceCount);
        Assert.Equal("Asia/Dubai", dashboard.TimeZoneId);
        Assert.NotNull(dashboard.Inventory);
        Assert.NotNull(dashboard.Operations);
    }

    [Fact]
    public async Task A_custom_range_reaches_the_query()
    {
        var response = await Client.GetAsync($"{Url}?range=Custom&from=2026-10-01&to=2026-10-03");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dashboard = await ReadAsync<OwnerDashboardWire>(response);

        Assert.Contains("2026", dashboard.Revenue.RangeLabel, StringComparison.Ordinal);
        Assert.Equal(3, dashboard.Revenue.Daily.Length);
    }

    [Fact]
    public async Task Receptionist_and_staff_are_refused_financials()
    {
        using var receptionist = await SignInAsync(ReceptionistEmail, ReceptionistPassword, RoleNames.Receptionist, "Rita", "Desk");
        using var staff = await SignInAsync(StaffEmail, StaffPassword, RoleNames.Staff, "Sam", "Scissors");

        Assert.Equal(HttpStatusCode.Forbidden, (await receptionist.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_refused()
    {
        using var anonymous = AnonymousClient;

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task An_unknown_range_is_a_field_level_validation_problem()
    {
        var response = await Client.GetAsync($"{Url}?range=Bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Single(problem.Errors["range"]);
    }

    [Fact]
    public async Task A_custom_range_without_dates_is_a_validation_problem()
    {
        var response = await Client.GetAsync($"{Url}?range=Custom");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains("from", problem.Errors.Keys);
    }

    private async Task<HttpClient> SignInAsync(
        string email, string password, string role, string firstName, string lastName)
    {
        await Factory.SeedUserAsync(email, password, role, firstName, lastName);

        var client = AnonymousClient;
        await AuthenticateAsync(client, email, password);

        return client;
    }

    private sealed record InvoiceRef(Guid Id);

    private sealed record OwnerDashboardWire(
        string TimeZoneId,
        KpisWire Kpis,
        OperationsWire Operations,
        RevenueWire Revenue,
        InventoryWire Inventory);

    private sealed record KpisWire(
        decimal NetRevenueToday,
        int PaidInvoiceCount);

    private sealed record OperationsWire(int AwaitingCheckout);

    private sealed record RevenueWire(string RangeLabel, DailyPointWire[] Daily, decimal VatTotal, decimal TipTotal);

    private sealed record DailyPointWire(DateOnly Date, decimal NetRevenue);

    private sealed record InventoryWire(int LowStockCount, int OutOfStockCount);
}
