using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Loyalty.Dtos;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Loyalty;

namespace DeBliss.UnitTests.Loyalty;

public sealed class LoyaltyMathTests
{
    [Fact]
    public async Task Exclusive_prices_earn_on_post_discount_merchandise_not_VAT()
    {
        var harness = new LoyaltyHarness();
        harness.Enable();
        var quote = (await harness.Calculate(amount: 100m, points: 20m)).Quote;
        Assert.Equal(50m, quote.MaximumRedeemablePoints);
        Assert.Equal(20m, quote.Discount);
        Assert.Equal(80m, quote.EligibleSpend);
        Assert.Equal(4m, quote.PointsEarned);
        Assert.Equal(100m, quote.AvailableValue);
        Assert.Equal(0, harness.UnitOfWork.Saves);
        Assert.Single(harness.Repository.Transactions);
    }

    [Fact]
    public async Task Inclusive_prices_cap_on_exVAT_and_discount_sticker_price_before_extracting_VAT()
    {
        var harness = new LoyaltyHarness();
        harness.Enable();
        var quote = (await harness.Calculate(amount: 105m, points: 50m, inclusive: true)).Quote;
        Assert.Equal(50m, quote.MaximumDiscount);
        Assert.Equal(50m, quote.Discount);
        Assert.Equal(52.38m, quote.EligibleSpend);
        Assert.Equal(2.62m, quote.PointsEarned);
    }

    [Fact]
    public async Task Earning_percentage_is_independent_of_the_redemption_conversion_value()
    {
        var harness = new LoyaltyHarness();
        harness.Enable().PointValueAed = 0.5m;
        var quote = (await harness.Calculate(amount: 100m)).Quote;
        Assert.Equal(5m, quote.PointsEarned);
        Assert.Equal(100m, quote.MaximumRedeemablePoints);
        Assert.Equal(50m, quote.MaximumDiscount);
        Assert.Equal(50m, quote.AvailableValue);
    }

    [Fact]
    public async Task Earnings_round_half_a_point_cent_away_from_zero()
    {
        var harness = new LoyaltyHarness();
        harness.Enable();
        var quote = (await harness.Calculate(amount: 0.10m)).Quote;
        Assert.Equal(0.01m, quote.PointsEarned);
    }

    [Fact]
    public async Task Points_and_discount_floor_so_the_cap_is_never_exceeded()
    {
        var harness = new LoyaltyHarness();
        harness.Enable().PointValueAed = 0.3333m;
        var quote = (await harness.Calculate(amount: 1.01m, points: 1.51m)).Quote;
        Assert.Equal(1.51m, quote.MaximumRedeemablePoints);
        Assert.Equal(0.50m, quote.MaximumDiscount);
        Assert.Equal(0.50m, quote.Discount);
        Assert.True(quote.Discount <= 1.01m * 0.5m);
        await AssertRedemptionRejected(harness, 1.52m, 1.01m);
    }

    [Fact]
    public async Task Minimum_invoice_uses_pre_loyalty_exVAT_spend_not_discounted_spend()
    {
        var harness = new LoyaltyHarness();
        harness.Enable().MinimumInvoiceAmount = 100m;
        var quote = (await harness.Calculate(amount: 105m, points: 50m, inclusive: true)).Quote;
        Assert.Null(quote.UnavailableReason);
        Assert.Equal(50m, quote.Discount);
        var tooSmall = (await harness.Calculate(amount: 104.99m, inclusive: true)).Quote;
        Assert.NotNull(tooSmall.UnavailableReason);
        Assert.Equal(0m, tooSmall.MaximumRedeemablePoints);
        Assert.True(tooSmall.PointsEarned > 0m);
    }

    [Fact]
    public async Task Partial_disabled_requires_full_balance_and_never_truncates_to_cap()
    {
        var harness = new LoyaltyHarness();
        harness.Enable(balance: 60m).AllowPartialRedemption = false;
        var quote = (await harness.Calculate()).Quote;
        Assert.Equal(0m, quote.MaximumRedeemablePoints);
        Assert.NotNull(quote.UnavailableReason);
        await AssertRedemptionRejected(harness, 50m);
        await AssertRedemptionRejected(harness, 60m);

        var full = (await harness.Calculate(amount: 120m, points: 60m)).Quote;
        Assert.Null(full.UnavailableReason);
        Assert.Equal(60m, full.Discount);
        await AssertRedemptionRejected(harness, 59.99m, 120m);
    }

    [Fact]
    public async Task Minimum_points_apply_to_both_maximum_and_requested_redemption()
    {
        var harness = new LoyaltyHarness();
        harness.Enable().MinimumRedemptionPoints = 20m;
        var below = (await harness.Calculate(amount: 30m)).Quote;
        Assert.NotNull(below.UnavailableReason);
        Assert.Equal(0m, below.MaximumRedeemablePoints);
        await AssertRedemptionRejected(harness, 19.99m);
        Assert.Equal(20m, (await harness.Calculate(points: 20m)).Quote.Discount);
    }

    [Fact]
    public async Task Sub_fils_value_is_unavailable_and_cannot_burn_points()
    {
        var harness = new LoyaltyHarness();
        harness.Enable(balance: 99.99m).PointValueAed = 0.0001m;
        var quote = (await harness.Calculate()).Quote;
        Assert.Equal(0m, quote.AvailableValue);
        Assert.Equal(0m, quote.MaximumRedeemablePoints);
        Assert.NotNull(quote.UnavailableReason);
        await AssertRedemptionRejected(harness, 99.99m);
    }

    [Fact]
    public async Task Requested_sub_fils_value_is_rejected_even_when_more_points_are_available()
    {
        var harness = new LoyaltyHarness();
        harness.Enable(balance: 1000m).PointValueAed = 0.0001m;
        await AssertRedemptionRejected(harness, 99.99m);
        Assert.Equal(0.01m, (await harness.Calculate(points: 100m)).Quote.Discount);
    }

    public static TheoryData<decimal> InvalidPoints => new() { -0.01m, 0.001m, 50.01m, 101m, decimal.MaxValue };

    [Theory]
    [MemberData(nameof(InvalidPoints))]
    public async Task Invalid_or_over_limit_points_are_field_validation_errors(decimal points)
    {
        var harness = new LoyaltyHarness();
        harness.Enable();
        await AssertRedemptionRejected(harness, points);
    }

    [Fact]
    public async Task Missing_settings_disable_earning_and_redemption_without_writing()
    {
        var harness = new LoyaltyHarness();
        var quote = (await harness.Calculate()).Quote;
        Assert.False(quote.IsEnabled);
        Assert.Equal(0m, quote.PointsEarned);
        Assert.Equal(0m, quote.MaximumDiscount);
        Assert.NotNull(quote.UnavailableReason);
        await AssertRedemptionRejected(harness, 1m);
        Assert.Null(harness.Repository.Settings);
        Assert.Empty(harness.Repository.Transactions);
    }

    [Fact]
    public async Task No_customer_disables_earning_and_redemption()
    {
        var harness = new LoyaltyHarness();
        harness.Enable();
        var quote = (await harness.Service.CalculateAsync(null, 100m, 5m, false, 0m)).Quote;
        Assert.Equal(0m, quote.PointsEarned);
        Assert.Equal(0m, quote.MaximumDiscount);
        Assert.Equal(0m, quote.AvailablePoints);
        Assert.NotNull(quote.UnavailableReason);
        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Service.CalculateAsync(null, 100m, 5m, false, 1m));
        Assert.Contains("loyaltyPointsToRedeem", failure.Errors.Keys);
    }

    [Fact]
    public async Task Unknown_customers_are_not_silently_treated_as_zero_balance()
    {
        var harness = new LoyaltyHarness();
        var missing = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() => harness.Service.GetSummaryAsync(missing));
        await Assert.ThrowsAsync<NotFoundException>(() => harness.Service.ListTransactionsAsync(missing));
        await Assert.ThrowsAsync<NotFoundException>(() => harness.Service.CalculateAsync(missing, 100m, 5m, false, 0m));
    }

    [Fact]
    public async Task Zero_balance_can_earn_but_cannot_redeem_points_from_this_checkout()
    {
        var harness = new LoyaltyHarness();
        harness.Enable(balance: 0m);
        var quote = (await harness.Calculate()).Quote;
        Assert.Equal(5m, quote.PointsEarned);
        Assert.Equal(0m, quote.MaximumRedeemablePoints);
        await AssertRedemptionRejected(harness, 5m);
    }

    [Fact]
    public async Task Debt_is_separate_from_available_points_and_lifetime_totals()
    {
        var harness = new LoyaltyHarness();
        harness.Enable(balance: 10m);
        harness.Repository.Transactions.Add(new CustomerLoyaltyTransaction
        {
            CustomerId = harness.CustomerId, Type = LoyaltyTransactionType.Redeem, Points = -5m
        });
        harness.Repository.Transactions.Add(new CustomerLoyaltyTransaction
        {
            CustomerId = harness.CustomerId, Type = LoyaltyTransactionType.Reversal, Points = -20m
        });
        var summary = await harness.Service.GetSummaryAsync(harness.CustomerId);
        Assert.Equal(0m, summary.AvailablePoints);
        Assert.Equal(15m, summary.PointsDebt);
        Assert.Equal(10m, summary.LifetimePointsEarned);
        Assert.Equal(5m, summary.PointsRedeemed);
        Assert.Equal(3, summary.RecentTransactions.Count);
        Assert.Equal(0m, (await harness.Calculate()).Quote.MaximumRedeemablePoints);
    }

    [Fact]
    public async Task Recording_checkout_stages_signed_entries_with_snapshot_and_deterministic_keys_without_saving()
    {
        var harness = new LoyaltyHarness();
        var settings = harness.Enable();
        var calculation = await harness.Calculate(points: 20m);
        settings.EarningPercentage = 50m;
        settings.PointValueAed = 2m;
        var invoice = PaidInvoice(harness.CustomerId);
        invoice.TipAmount = 500m;
        var actor = Guid.NewGuid();
        harness.Service.RecordCheckout(invoice, calculation, actor);

        var entries = harness.Repository.Transactions.Where(entry => entry.InvoiceId == invoice.Id).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(-20m, Assert.Single(entries, entry => entry.Type == LoyaltyTransactionType.Redeem).Points);
        Assert.Equal(4m, Assert.Single(entries, entry => entry.Type == LoyaltyTransactionType.Earn).Points);
        Assert.All(entries, entry =>
        {
            Assert.Equal(80m, entry.EligibleSpend);
            Assert.Equal(5m, entry.EarningPercentage);
            Assert.Equal(1m, entry.PointValueAed);
            Assert.Equal(actor, entry.CreatedByUserId);
            Assert.Equal(harness.CustomerId, entry.CustomerId);
            Assert.Equal(harness.Clock.GetUtcNow(), entry.OccurredAt);
            Assert.Equal($"invoice:{invoice.Id:D}:{entry.Type.ToString().ToLowerInvariant()}", entry.IdempotencyKey);
            Assert.Null(entry.ReversesTransactionId);
            Assert.NotEqual(Guid.Empty, entry.Id);
        });
        Assert.Equal(0, harness.UnitOfWork.Saves);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("unpaid")]
    [InlineData("pending")]
    [InlineData("deleted-payment")]
    [InlineData("refund-payment")]
    [InlineData("no-customer")]
    [InlineData("disabled")]
    public async Task Ineligible_checkout_does_not_record_entries(string condition)
    {
        var harness = new LoyaltyHarness();
        var settings = harness.Enable();
        if (condition == "disabled") settings.IsEnabled = false;
        var calculation = await harness.Calculate();
        var invoice = PaidInvoice(harness.CustomerId);
        switch (condition)
        {
            case "draft": invoice.Status = InvoiceStatus.Draft; break;
            case "unpaid": invoice.Payments.Clear(); break;
            case "pending": invoice.Payments.Single().Status = PaymentStatus.Pending; break;
            case "deleted-payment": invoice.Payments.Single().DeletedAt = harness.Clock.GetUtcNow(); break;
            case "refund-payment": invoice.Payments.Single().RefundOfPaymentId = Guid.NewGuid(); break;
            case "no-customer": invoice.CustomerId = null; break;
        }
        harness.Service.RecordCheckout(invoice, calculation, null);
        Assert.Single(harness.Repository.Transactions);
        Assert.Equal(0, harness.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Fully_redeemed_free_invoice_records_redemption_with_a_completed_zero_payment()
    {
        var harness = new LoyaltyHarness();
        harness.Enable().MaximumRedemptionPercentage = 100m;
        var calculation = await harness.Calculate(points: 100m);
        var invoice = PaidInvoice(harness.CustomerId);
        invoice.TotalAmount = 0m;
        invoice.Payments.Single().Amount = 0m;
        harness.Service.RecordCheckout(invoice, calculation, null);
        var entry = Assert.Single(harness.Repository.Transactions, transaction => transaction.InvoiceId == invoice.Id);
        Assert.Equal(LoyaltyTransactionType.Redeem, entry.Type);
        Assert.Equal(-100m, entry.Points);
        Assert.Equal(0m, calculation.Quote.PointsEarned);
    }

    [Fact]
    public async Task Changed_customer_requires_a_fresh_calculation()
    {
        var harness = new LoyaltyHarness();
        harness.Enable();
        var calculation = await harness.Calculate();
        Assert.Throws<ConflictException>(() => harness.Service.RecordCheckout(PaidInvoice(Guid.NewGuid()), calculation, null));
        Assert.Single(harness.Repository.Transactions);
    }

    [Fact]
    public async Task Zero_rates_and_zero_invoice_do_not_emit_empty_entries()
    {
        var harness = new LoyaltyHarness();
        var settings = harness.Enable();
        settings.EarningPercentage = 0m;
        settings.MaximumRedemptionPercentage = 0m;
        var calculation = await harness.Calculate(amount: 0m, tax: 0m);
        Assert.Equal(0m, calculation.Quote.MaximumDiscount);
        Assert.Equal(0m, calculation.Quote.PointsEarned);
        harness.Service.RecordCheckout(PaidInvoice(harness.CustomerId), calculation, null);
        Assert.Single(harness.Repository.Transactions);
    }

    [Fact]
    public async Task Page_parameters_are_bounded_and_summary_only_contains_recent_entries()
    {
        var harness = new LoyaltyHarness();
        harness.Enable(balance: 0m);
        for (var i = 0; i < 12; i++)
            harness.Repository.Transactions.Add(new CustomerLoyaltyTransaction
            {
                CustomerId = harness.CustomerId, Type = LoyaltyTransactionType.Earn,
                Points = 1m, OccurredAt = harness.Clock.GetUtcNow().AddMinutes(i)
            });
        var page = await harness.Service.ListTransactionsAsync(harness.CustomerId, 2, 5);
        Assert.Equal(12, page.TotalCount);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.Items[0].OccurredAt > page.Items[1].OccurredAt);
        Assert.Equal(10, (await harness.Service.GetSummaryAsync(harness.CustomerId)).RecentTransactions.Count);
        var normalized = await harness.Service.ListTransactionsAsync(harness.CustomerId, -1, 0);
        Assert.Equal(1, normalized.Page);
        Assert.Equal(50, normalized.PageSize);
        Assert.Equal(200, (await harness.Service.ListTransactionsAsync(harness.CustomerId, 1, int.MaxValue)).PageSize);
    }

    [Fact]
    public void Operational_quote_does_not_expose_owner_earning_rate_or_settings_audit()
    {
        var properties = typeof(LoyaltyQuoteDto).GetProperties().Select(property => property.Name).ToList();
        Assert.DoesNotContain("EarningPercentage", properties);
        Assert.DoesNotContain("Settings", properties);
        Assert.DoesNotContain("UpdatedByUserId", properties);
        Assert.DoesNotContain("Version", properties);
    }

    private static async Task AssertRedemptionRejected(LoyaltyHarness harness, decimal points, decimal amount = 100m)
    {
        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() => harness.Calculate(amount, points));
        Assert.Contains("loyaltyPointsToRedeem", failure.Errors.Keys);
        Assert.Equal(0, harness.UnitOfWork.Saves);
    }

    private static Invoice PaidInvoice(Guid customerId)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), CustomerId = customerId, InvoiceNumber = "INV-LOYALTY-TEST",
            Status = InvoiceStatus.Paid, TotalAmount = 84m, Currency = "AED"
        };
        invoice.Payments.Add(new Payment { InvoiceId = invoice.Id, Status = PaymentStatus.Completed, Amount = 84m });
        return invoice;
    }
}
