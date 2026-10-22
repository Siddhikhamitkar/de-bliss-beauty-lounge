using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Identity;
using DeBliss.Application.Settings.Dtos;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the tax configuration through the real HTTP pipeline: both roles
/// read what the checkout will charge, only the owner may change it, and a
/// change reaches the very next checkout — the wire contract the React app
/// and the settings page consume. The rules live in
/// <see cref="Settings.SettingsServiceTests"/> and
/// <see cref="Billing.VatAndTipsTests"/>.
/// </summary>
public sealed class TaxSettingsApiTests : ApiTestBase
{
    private const string TaxUrl = "/api/settings/tax";
    private const string ReceptionistEmail = "receptionist@debliss.test";
    private const string ReceptionistPassword = "Test#Front123";

    [Fact]
    public async Task Both_roles_can_read_the_tax_configuration()
    {
        var ownerRead = await ReadAsync<TaxSettingsDto>(await Client.GetAsync(TaxUrl));

        // The test host configures the salon at a zero rate; the checkout
        // tests assert against exactly what this read returns.
        Assert.Equal(0m, ownerRead.TaxRate);
        Assert.False(ownerRead.PricesIncludeTax);

        using var receptionist = await SignInReceptionistAsync();

        var frontDeskRead = await ReadAsync<TaxSettingsDto>(await receptionist.GetAsync(TaxUrl));

        Assert.Equal(ownerRead, frontDeskRead);
    }

    [Fact]
    public async Task Only_the_owner_can_change_the_tax_configuration()
    {
        var updated = await Client.PutAsJsonAsync(TaxUrl, new { taxRate = 5m, pricesIncludeTax = false });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(5m, (await ReadAsync<TaxSettingsDto>(updated)).TaxRate);

        using var receptionist = await SignInReceptionistAsync();

        var refused = await receptionist.PutAsJsonAsync(TaxUrl, new { taxRate = 20m, pricesIncludeTax = true });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // The refused write changed nothing.
        var still = await ReadAsync<TaxSettingsDto>(await Client.GetAsync(TaxUrl));

        Assert.Equal(5m, still.TaxRate);
        Assert.False(still.PricesIncludeTax);
    }

    [Fact]
    public async Task An_invalid_update_is_a_field_level_validation_problem()
    {
        var response = await Client.PutAsJsonAsync(TaxUrl, new { taxRate = 150m, pricesIncludeTax = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Equal("Enter a VAT rate between 0 and 100.", Assert.Single(problem.Errors["taxRate"]));
    }

    [Fact]
    public async Task A_rate_change_reaches_the_next_checkout()
    {
        await Client.PutAsJsonAsync(TaxUrl, new { taxRate = 5m, pricesIncludeTax = false });

        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var haircutId = await Factory.SeedServiceAsync(categoryId, "Haircut", 100m);
        var customerId = await Factory.SeedCustomerAsync("Layla", "Haddad", phoneNumber: "+971 50 000 0000");
        var appointmentId = await Factory.SeedBillingAppointmentAsync(
            customerId,
            new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero),
            lines: [(haircutId, "Haircut", 100m, 0)]);

        var started = await Client.PostAsJsonAsync("/api/invoices", new { appointmentId });
        var invoice = await ReadAsync<InvoiceDto>(started);

        var paid = await Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/checkout", new
        {
            paymentMethod = "Cash",
            amount = 115.50m,
            tipPercentage = 10m
        });

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        var receipt = await ReadAsync<ReceiptDto>(paid);

        Assert.Equal(5m, receipt.TaxRate);
        Assert.Equal(5m, receipt.TaxTotal);
        Assert.Equal(10.50m, receipt.TipAmount);
        Assert.Equal(115.50m, receipt.TotalAmount);

        // The invoice carries the same snapshot the receipt printed.
        var stored = await ReadAsync<InvoiceDto>(await Client.GetAsync($"/api/invoices/{invoice.Id}"));

        Assert.Equal(5m, stored.TaxTotal);
        Assert.Equal(10.50m, stored.TipAmount);
        Assert.Equal(115.50m, stored.TotalAmount);
        Assert.Equal(115.50m, stored.Payments.Single().Amount);
    }

    [Fact]
    public async Task Anonymous_requests_are_refused()
    {
        using var anonymous = AnonymousClient;

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(TaxUrl)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync(TaxUrl, new { taxRate = 5m, pricesIncludeTax = false })).StatusCode);
    }

    private async Task<HttpClient> SignInReceptionistAsync()
    {
        await Factory.SeedUserAsync(ReceptionistEmail, ReceptionistPassword, RoleNames.Receptionist, "Rita", "Desk");

        var client = AnonymousClient;
        await AuthenticateAsync(client, ReceptionistEmail, ReceptionistPassword);

        return client;
    }
}
