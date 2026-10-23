using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Identity;
using DeBliss.Application.Loyalty.Dtos;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Loyalty;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Api;

/// <summary>Real authentication, routing and checkout over ApiTestBase's disposable SQLite database.</summary>
public sealed class LoyaltyApiTests : ApiTestBase
{
    private const string SettingsUrl = "/api/loyalty/settings";
    private const string Password = "Test#Loyalty123";
    private static readonly DateTimeOffset VisitTime = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Owner_reads_safe_defaults_without_creating_a_settings_row_and_can_update_all_fields()
    {
        var initial = await Client.GetAsync(SettingsUrl);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var defaults = await ReadAsync<LoyaltySettingsDto>(initial);
        Assert.False(defaults.IsEnabled);
        Assert.Equal(5m, defaults.EarningPercentage);
        Assert.Equal(1m, defaults.PointValueAed);
        Assert.Equal(0m, defaults.MinimumRedemptionPoints);
        Assert.Equal(50m, defaults.MaximumRedemptionPercentage);
        Assert.True(defaults.AllowPartialRedemption);
        Assert.Null(defaults.MinimumInvoiceAmount);
        await using (var verify = Factory.Database.CreateContext())
            Assert.Empty(await verify.Set<LoyaltySettings>().ToListAsync());

        var request = new SaveLoyaltySettingsRequest(true, 7.5m, 0.25m, 10m, 75m, false, 100m);
        var changed = await Client.PutAsJsonAsync(SettingsUrl, request);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var saved = await ReadAsync<LoyaltySettingsDto>(changed);
        Assert.Equal(new LoyaltySettingsDto(true, 7.5m, 0.25m, 10m, 75m, false, 100m), saved);
        Assert.Equal(saved, await ReadAsync<LoyaltySettingsDto>(await Client.GetAsync(SettingsUrl)));

        var cleared = await Client.PutAsJsonAsync(SettingsUrl, request with { MinimumInvoiceAmount = null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await ReadAsync<LoyaltySettingsDto>(cleared)).MinimumInvoiceAmount);
        await using var database = Factory.Database.CreateContext();
        Assert.Single(await database.Set<LoyaltySettings>().ToListAsync());
    }

    [Fact]
    public async Task Missing_settings_fields_return_field_errors_without_mutating_configuration()
    {
        await EnableAsync();
        var before = await ReadAsync<LoyaltySettingsDto>(await Client.GetAsync(SettingsUrl));
        var response = await Client.PutAsJsonAsync(SettingsUrl,
            new SaveLoyaltySettingsRequest(null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<ValidationProblem>(response);
        foreach (var field in new[] { "isEnabled", "earningPercentage", "pointValueAed", "minimumRedemptionPoints", "maximumRedemptionPercentage", "allowPartialRedemption" })
            Assert.Contains(field, problem.Errors.Keys);
        Assert.Equal(before, await ReadAsync<LoyaltySettingsDto>(await Client.GetAsync(SettingsUrl)));
    }

    [Theory]
    [InlineData("earningPercentage", "-0.01")]
    [InlineData("earningPercentage", "100.01")]
    [InlineData("earningPercentage", "5.001")]
    [InlineData("pointValueAed", "0")]
    [InlineData("pointValueAed", "0.00001")]
    [InlineData("minimumRedemptionPoints", "-1")]
    [InlineData("minimumRedemptionPoints", "1.001")]
    [InlineData("maximumRedemptionPercentage", "-0.01")]
    [InlineData("maximumRedemptionPercentage", "100.01")]
    [InlineData("minimumInvoiceAmount", "-1")]
    [InlineData("minimumInvoiceAmount", "10.001")]
    public async Task Invalid_settings_are_400_field_problems_not_silently_coerced(string field, string text)
    {
        await EnableAsync();
        var before = await ReadAsync<LoyaltySettingsDto>(await Client.GetAsync(SettingsUrl));
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);
        var request = Settings();
        request = field switch
        {
            "earningPercentage" => request with { EarningPercentage = value },
            "pointValueAed" => request with { PointValueAed = value },
            "minimumRedemptionPoints" => request with { MinimumRedemptionPoints = value },
            "maximumRedemptionPercentage" => request with { MaximumRedemptionPercentage = value },
            "minimumInvoiceAmount" => request with { MinimumInvoiceAmount = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var response = await Client.PutAsJsonAsync(SettingsUrl, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, (await ReadAsync<ValidationProblem>(response)).Errors.Keys);
        Assert.Equal(before, await ReadAsync<LoyaltySettingsDto>(await Client.GetAsync(SettingsUrl)));
    }

    [Fact]
    public async Task Receptionist_cannot_read_or_change_owner_loyalty_settings()
    {
        await EnableAsync();
        var before = await ReadAsync<LoyaltySettingsDto>(await Client.GetAsync(SettingsUrl));
        using var receptionist = await SignInAsync(RoleNames.Receptionist);
        Assert.Equal(HttpStatusCode.Forbidden, (await receptionist.GetAsync(SettingsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await receptionist.PutAsJsonAsync(SettingsUrl, Settings() with { EarningPercentage = 100m })).StatusCode);
        Assert.Equal(before, await ReadAsync<LoyaltySettingsDto>(await Client.GetAsync(SettingsUrl)));
    }

    [Theory]
    [InlineData(RoleNames.Owner)]
    [InlineData(RoleNames.Receptionist)]
    public async Task Owner_and_receptionist_can_read_summary_preview_redeem_and_page_history(string role)
    {
        await EnableAsync();
        var customer = await Factory.SeedCustomerAsync("Loyalty", "Member");
        var opening = await StartInvoiceAsync(customer, 300m);
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PostAsJsonAsync(CheckoutUrl(opening.Id), Checkout(300m))).StatusCode);
        var invoice = await StartInvoiceAsync(customer, 100m);
        using var operational = role == RoleNames.Owner ? null : await SignInAsync(role);
        var client = operational ?? Client;
        var summaryResponse = await client.GetAsync(SummaryUrl(customer));
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        var before = await ReadAsync<CustomerLoyaltySummaryDto>(summaryResponse);
        Assert.Equal(customer, before.CustomerId);
        Assert.Equal(15m, before.AvailablePoints);
        Assert.Equal(15m, before.LifetimePointsEarned);
        Assert.Equal(0m, before.PointsRedeemed);
        Assert.Equal(0m, before.PointsDebt);
        Assert.Single(before.RecentTransactions);

        var previewResponse = await client.PostAsJsonAsync(PreviewUrl(invoice.Id),
            Checkout(null, 10m) with { PaymentMethod = null });
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await ReadAsync<CheckoutPreviewDto>(previewResponse);
        Assert.Equal(100m, preview.SubTotal);
        Assert.Equal(90m, preview.TotalAmount);
        Assert.Equal(15m, preview.Loyalty.AvailablePoints);
        Assert.Equal(15m, preview.Loyalty.MaximumRedeemablePoints);
        Assert.Equal(10m, preview.Loyalty.PointsToRedeem);
        Assert.Equal(10m, preview.Loyalty.Discount);
        Assert.Equal(4.5m, preview.Loyalty.PointsEarned);
        var afterPreview = await ReadAsync<CustomerLoyaltySummaryDto>(await client.GetAsync(SummaryUrl(customer)));
        Assert.Equal(15m, afterPreview.AvailablePoints);
        Assert.Single(afterPreview.RecentTransactions);
        await AssertDraftAsync(invoice.Id);

        var paidResponse = await client.PostAsJsonAsync(CheckoutUrl(invoice.Id), Checkout(90m, 10m));
        Assert.Equal(HttpStatusCode.OK, paidResponse.StatusCode);
        var receipt = await ReadAsync<ReceiptDto>(paidResponse);
        Assert.Equal(preview.TotalAmount, receipt.TotalAmount);
        Assert.Equal(10m, receipt.LoyaltyDiscount);
        Assert.Equal(10m, receipt.LoyaltyPointsRedeemed);
        Assert.Equal(4.5m, receipt.LoyaltyPointsEarned);
        var stored = await ReadAsync<InvoiceDto>(await Client.GetAsync($"/api/invoices/{invoice.Id}"));
        Assert.Equal("Paid", stored.Status);
        Assert.Equal(10m, stored.LoyaltyDiscount);
        Assert.Equal(10m, stored.DiscountTotal);
        Assert.Equal(90m, Assert.Single(stored.Payments).Amount);
        var after = await ReadAsync<CustomerLoyaltySummaryDto>(await client.GetAsync(SummaryUrl(customer)));
        Assert.Equal(9.5m, after.AvailablePoints);
        Assert.Equal(19.5m, after.LifetimePointsEarned);
        Assert.Equal(10m, after.PointsRedeemed);
        Assert.Equal(0m, after.PointsDebt);
        Assert.Equal(3, after.RecentTransactions.Count);

        var pageOneResponse = await client.GetAsync($"{SummaryUrl(customer)}/transactions?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, pageOneResponse.StatusCode);
        var first = await ReadAsync<PagedResult<LoyaltyTransactionDto>>(pageOneResponse);
        var second = await ReadAsync<PagedResult<LoyaltyTransactionDto>>(
            await client.GetAsync($"{SummaryUrl(customer)}/transactions?page=2&pageSize=2"));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(3, second.TotalCount);
        Assert.Single(second.Items);
        var history = first.Items.Concat(second.Items).ToList();
        Assert.Equal(3, history.Select(row => row.Id).Distinct().Count());
        Assert.All(history, row =>
        {
            Assert.Equal(customer, row.CustomerId);
            Assert.NotNull(row.InvoiceNumber);
            Assert.False(string.IsNullOrWhiteSpace(row.IdempotencyKey));
        });
        var redeemed = Assert.Single(history, row => row.Type == "Redeem");
        Assert.Equal(invoice.Id, redeemed.InvoiceId);
        Assert.Equal(-10m, redeemed.Points);
        Assert.Equal(1m, redeemed.PointValueAed);
        Assert.Equal(5m, redeemed.EarningPercentage);
        Assert.Equal(90m, redeemed.EligibleSpend);
        Assert.NotNull(redeemed.CreatedByUserId);
    }

    [Theory]
    [InlineData(RoleNames.Staff)]
    [InlineData(RoleNames.Customer)]
    [InlineData(null)]
    public async Task Staff_customers_and_anonymous_callers_cannot_access_any_loyalty_operation(string? role)
    {
        await EnableAsync();
        var customer = await Factory.SeedCustomerAsync("Protected", "Member");
        var invoice = await StartInvoiceAsync(customer, 100m);
        using var client = role is null ? AnonymousClient : await SignInAsync(role);
        var expected = role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await client.GetAsync(SettingsUrl)).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync(SettingsUrl, Settings())).StatusCode);
        Assert.Equal(expected, (await client.GetAsync(SummaryUrl(customer))).StatusCode);
        Assert.Equal(expected, (await client.GetAsync($"{SummaryUrl(customer)}/transactions?page=1&pageSize=10")).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync(PreviewUrl(invoice.Id), Checkout(null))).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync(CheckoutUrl(invoice.Id), Checkout(100m))).StatusCode);
        await AssertDraftAsync(invoice.Id);
        await using var verify = Factory.Database.CreateContext();
        Assert.Empty(await verify.Set<CustomerLoyaltyTransaction>().ToListAsync());
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.001")]
    [InlineData("5.01")]
    public async Task Invalid_redemption_is_a_field_error_on_preview_and_checkout_and_never_writes(string text)
    {
        await EnableAsync();
        var customer = await Factory.SeedCustomerAsync("Validation", "Member");
        var opening = await StartInvoiceAsync(customer, 100m);
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PostAsJsonAsync(CheckoutUrl(opening.Id), Checkout(100m))).StatusCode);
        var invoice = await StartInvoiceAsync(customer, 100m);
        var points = decimal.Parse(text, CultureInfo.InvariantCulture);
        var request = Checkout(100m - points, points);
        foreach (var url in new[] { PreviewUrl(invoice.Id), CheckoutUrl(invoice.Id) })
        {
            var response = await Client.PostAsJsonAsync(url, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ReadAsync<ValidationProblem>(response);
            Assert.Contains("loyaltyPointsToRedeem", problem.Errors.Keys);
        }
        await AssertDraftAsync(invoice.Id);
        var summary = await ReadAsync<CustomerLoyaltySummaryDto>(await Client.GetAsync(SummaryUrl(customer)));
        Assert.Equal(5m, summary.AvailablePoints);
        Assert.Single(summary.RecentTransactions);
    }

    [Fact]
    public async Task Failed_amount_does_not_spend_points_and_successful_retry_then_paid_retry_is_409()
    {
        await EnableAsync();
        var customer = await Factory.SeedCustomerAsync("Retry", "Member");
        var opening = await StartInvoiceAsync(customer, 200m);
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PostAsJsonAsync(CheckoutUrl(opening.Id), Checkout(200m))).StatusCode);
        var invoice = await StartInvoiceAsync(customer, 100m);
        var failed = await Client.PostAsJsonAsync(CheckoutUrl(invoice.Id), Checkout(99m, 10m));
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        Assert.Contains("amount", (await ReadAsync<ValidationProblem>(failed)).Errors.Keys);
        await AssertDraftAsync(invoice.Id);
        Assert.Equal(10m, (await ReadAsync<CustomerLoyaltySummaryDto>(await Client.GetAsync(SummaryUrl(customer)))).AvailablePoints);
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PostAsJsonAsync(CheckoutUrl(invoice.Id), Checkout(90m, 10m))).StatusCode);
        var repeated = await Client.PostAsJsonAsync(CheckoutUrl(invoice.Id), Checkout(90m, 10m));
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Contains("already been paid", (await ReadAsync<Problem>(repeated)).Detail);
        await using var verify = Factory.Database.CreateContext();
        Assert.Single(await verify.Payments.Where(row => row.InvoiceId == invoice.Id).ToListAsync());
        var ledger = await verify.Set<CustomerLoyaltyTransaction>().Where(row => row.InvoiceId == invoice.Id).ToListAsync();
        Assert.Equal(2, ledger.Count);
        Assert.Equal(-10m, Assert.Single(ledger, row => row.Type == LoyaltyTransactionType.Redeem).Points);
        Assert.Equal(4.5m, Assert.Single(ledger, row => row.Type == LoyaltyTransactionType.Earn).Points);
    }

    [Fact]
    public async Task Checkout_recalculates_a_preview_after_settings_change_and_receipt_keeps_paid_snapshots()
    {
        await EnableAsync();
        var customer = await Factory.SeedCustomerAsync("Snapshot", "Member");
        var opening = await StartInvoiceAsync(customer, 400m);
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PostAsJsonAsync(CheckoutUrl(opening.Id), Checkout(400m))).StatusCode);
        var invoice = await StartInvoiceAsync(customer, 100m);
        var oldQuote = await ReadAsync<CheckoutPreviewDto>(
            await Client.PostAsJsonAsync(PreviewUrl(invoice.Id), Checkout(null, 10m)));
        Assert.Equal(90m, oldQuote.TotalAmount);
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PutAsJsonAsync(SettingsUrl, Settings() with { PointValueAed = 2m, EarningPercentage = 10m })).StatusCode);
        var stale = await Client.PostAsJsonAsync(CheckoutUrl(invoice.Id), Checkout(90m, 10m));
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        Assert.Contains("amount", (await ReadAsync<ValidationProblem>(stale)).Errors.Keys);
        await AssertDraftAsync(invoice.Id);
        var paidResponse = await Client.PostAsJsonAsync(CheckoutUrl(invoice.Id), Checkout(80m, 10m));
        Assert.Equal(HttpStatusCode.OK, paidResponse.StatusCode);
        var paid = await ReadAsync<ReceiptDto>(paidResponse);
        Assert.Equal(20m, paid.LoyaltyDiscount);
        Assert.Equal(4m, paid.LoyaltyPointsEarned);
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PutAsJsonAsync(SettingsUrl, Settings() with { IsEnabled = false, PointValueAed = 3m })).StatusCode);
        var reprint = await ReadAsync<ReceiptDto>(await Client.GetAsync($"/api/invoices/{invoice.Id}/receipt"));
        Assert.Equal(paid.LoyaltyDiscount, reprint.LoyaltyDiscount);
        Assert.Equal(paid.LoyaltyPointsRedeemed, reprint.LoyaltyPointsRedeemed);
        Assert.Equal(paid.LoyaltyPointsEarned, reprint.LoyaltyPointsEarned);
        Assert.Equal(paid.TotalAmount, reprint.TotalAmount);
        var history = await ReadAsync<PagedResult<LoyaltyTransactionDto>>(
            await Client.GetAsync($"{SummaryUrl(customer)}/transactions"));
        var checkoutEntries = history.Items.Where(row => row.InvoiceId == invoice.Id).ToList();
        Assert.Equal(2, checkoutEntries.Count);
        Assert.All(checkoutEntries, row =>
        {
            Assert.Equal(2m, row.PointValueAed);
            Assert.Equal(10m, row.EarningPercentage);
        });
    }

    [Fact]
    public async Task Summary_and_history_are_scoped_to_the_requested_customer_and_normalize_paging()
    {
        await EnableAsync();
        var first = await Factory.SeedCustomerAsync("First", "Member");
        var second = await Factory.SeedCustomerAsync("Second", "Member");
        var invoice = await StartInvoiceAsync(first, 100m);
        Assert.Equal(HttpStatusCode.OK,
            (await Client.PostAsJsonAsync(CheckoutUrl(invoice.Id), Checkout(100m))).StatusCode);
        var empty = await ReadAsync<CustomerLoyaltySummaryDto>(await Client.GetAsync(SummaryUrl(second)));
        Assert.Equal(second, empty.CustomerId);
        Assert.Equal(0m, empty.AvailablePoints);
        Assert.Equal(0m, empty.LifetimePointsEarned);
        Assert.Equal(0m, empty.PointsRedeemed);
        Assert.Equal(0m, empty.PointsDebt);
        Assert.Empty(empty.RecentTransactions);
        var history = await ReadAsync<PagedResult<LoyaltyTransactionDto>>(
            await Client.GetAsync($"{SummaryUrl(second)}/transactions?page=1&pageSize=10"));
        Assert.Equal(0, history.TotalCount);
        Assert.Empty(history.Items);
        var normalized = await ReadAsync<PagedResult<LoyaltyTransactionDto>>(
            await Client.GetAsync($"{SummaryUrl(first)}/transactions?page=0&pageSize=1000"));
        Assert.Equal(1, normalized.Page);
        Assert.Equal(200, normalized.PageSize);
        Assert.Equal(first, Assert.Single(normalized.Items).CustomerId);
    }

    [Fact]
    public async Task Unknown_customers_and_invoices_return_404_for_authorized_callers()
    {
        var missing = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(SummaryUrl(missing))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"{SummaryUrl(missing)}/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.PostAsJsonAsync(PreviewUrl(missing), Checkout(null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.PostAsJsonAsync(CheckoutUrl(missing), Checkout(100m))).StatusCode);
    }

    private static SaveLoyaltySettingsRequest Settings() => new(true, 5m, 1m, 0m, 50m, true);

    private async Task EnableAsync()
    {
        var response = await Client.PutAsJsonAsync(SettingsUrl, Settings());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static CheckoutRequest Checkout(decimal? amount, decimal points = 0m) =>
        new(null, null, null, "Cash", amount, null, LoyaltyPointsToRedeem: points);

    private static string SummaryUrl(Guid customer) => $"/api/customers/{customer}/loyalty";
    private static string CheckoutUrl(Guid invoice) => $"/api/invoices/{invoice}/checkout";
    private static string PreviewUrl(Guid invoice) => $"/api/invoices/{invoice}/checkout-preview";

    private async Task<HttpClient> SignInAsync(string role)
    {
        var email = $"{role.ToLowerInvariant()}-loyalty@debliss.test";
        await Factory.SeedUserAsync(email, Password, role);
        var client = AnonymousClient;
        await AuthenticateAsync(client, email, Password);
        return client;
    }

    private async Task<InvoiceDto> StartInvoiceAsync(Guid customer, decimal price)
    {
        var name = $"Loyalty service {Guid.NewGuid():N}";
        var category = await Factory.SeedCategoryAsync(name);
        var service = await Factory.SeedServiceAsync(category, name, price);
        var appointment = await Factory.SeedBillingAppointmentAsync(customer, VisitTime,
            lines: [(service, name, price, 0)]);
        var response = await Client.PostAsJsonAsync("/api/invoices", new CreateInvoiceRequest(appointment));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<InvoiceDto>(response);
    }

    private async Task AssertDraftAsync(Guid invoice)
    {
        await using var verify = Factory.Database.CreateContext();
        var stored = await verify.Invoices.Include(row => row.Payments).SingleAsync(row => row.Id == invoice);
        Assert.Equal(InvoiceStatus.Draft, stored.Status);
        Assert.Empty(stored.Payments);
        Assert.Equal(0m, stored.LoyaltyDiscount);
        Assert.Equal(0m, stored.LoyaltyPointsRedeemed);
        Assert.Equal(0m, stored.LoyaltyPointsEarned);
        Assert.Empty(await verify.Set<CustomerLoyaltyTransaction>().Where(row => row.InvoiceId == invoice).ToListAsync());
    }
}
