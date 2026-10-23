using System.Globalization;
using DeBliss.Application.Billing;
using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Loyalty;
using DeBliss.Application.Loyalty.Dtos;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Loyalty;
using DeBliss.Domain.Scheduling;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Interceptors;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeBliss.UnitTests.Billing;

/// <summary>Real checkout/repository integration, exclusively against the existing ephemeral SQLite fixture.</summary>
public sealed class LoyaltyCheckoutTests
{
    private static readonly DateTimeOffset VisitTime = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Cash")]
    [InlineData("Card")]
    [InlineData("BankTransfer")]
    public async Task Successful_payment_earns_once_and_paid_retry_cannot_duplicate_the_award(string method)
    {
        using var harness = new BillingHarness();
        await ConfigureAsync(harness);
        var customer = await harness.SeedCustomerAsync();
        var invoice = await StartAsync(harness, customer, 100m);

        var receipt = await harness.Invoices.CheckoutAsync(invoice, Checkout(100m, method: method));
        Assert.Equal(5m, receipt.LoyaltyPointsEarned);
        Assert.Equal(0m, receipt.LoyaltyPointsRedeemed);
        Assert.Equal(method, receipt.PaymentMethod);
        await Assert.ThrowsAsync<ConflictException>(() => harness.Invoices.CheckoutAsync(invoice, Checkout(100m)));

        await using var verify = harness.Reopen();
        var stored = await verify.Invoices.Include(row => row.Payments).SingleAsync(row => row.Id == invoice);
        Assert.Equal(InvoiceStatus.Paid, stored.Status);
        Assert.Equal(100m, Assert.Single(stored.Payments).Amount);
        var earned = Assert.Single(await verify.Set<CustomerLoyaltyTransaction>().ToListAsync());
        Assert.Equal(LoyaltyTransactionType.Earn, earned.Type);
        Assert.Equal(customer, earned.CustomerId);
        Assert.Equal(invoice, earned.InvoiceId);
        Assert.Equal(5m, earned.Points);
        Assert.Equal(100m, earned.EligibleSpend);
        Assert.Equal(5m, earned.EarningPercentage);
        Assert.Equal(1m, earned.PointValueAed);
        Assert.Equal(harness.Database.Clock.Now, earned.OccurredAt);
        Assert.Equal($"invoice:{invoice:D}:earn", earned.IdempotencyKey);
    }

    [Fact]
    public async Task Default_disabled_checkout_and_preview_never_create_settings_or_points()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        var invoice = await StartAsync(harness, customer, 100m);
        var defaults = await Loyalty(harness.Context, harness.Database).GetSettingsAsync();
        Assert.False(defaults.IsEnabled);
        Assert.Equal(5m, defaults.EarningPercentage);
        Assert.Equal(1m, defaults.PointValueAed);
        Assert.Equal(0m, defaults.MinimumRedemptionPoints);
        Assert.Equal(50m, defaults.MaximumRedemptionPercentage);
        Assert.True(defaults.AllowPartialRedemption);

        var quote = await harness.Invoices.PreviewCheckoutAsync(invoice, Checkout(null));
        Assert.False(quote.Loyalty.IsEnabled);
        Assert.Equal(0m, quote.Loyalty.PointsEarned);
        var receipt = await harness.Invoices.CheckoutAsync(invoice, Checkout(100m));
        Assert.Equal(0m, receipt.LoyaltyPointsEarned);
        await using var verify = harness.Reopen();
        Assert.Empty(await verify.Set<CustomerLoyaltyTransaction>().ToListAsync());
        Assert.Empty(await verify.Set<LoyaltySettings>().ToListAsync());
        Assert.Single(await verify.Payments.ToListAsync());
    }

    [Fact]
    public async Task Walk_in_can_pay_but_cannot_earn_or_redeem()
    {
        using var harness = new BillingHarness();
        await ConfigureAsync(harness);
        var invoice = await StartAsync(harness, null, 100m);
        var preview = await harness.Invoices.PreviewCheckoutAsync(invoice, Checkout(null));
        Assert.Equal(0m, preview.Loyalty.PointsEarned);
        Assert.Equal(0m, preview.Loyalty.MaximumRedeemablePoints);
        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Invoices.CheckoutAsync(invoice, Checkout(99m, points: 1m)));
        await harness.Invoices.CheckoutAsync(invoice, Checkout(100m));
        await using var verify = harness.Reopen();
        Assert.Empty(await verify.Set<CustomerLoyaltyTransaction>().ToListAsync());
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Confirmed)]
    public async Task Cancelled_or_uncompleted_visits_cannot_start_an_earning_checkout(AppointmentStatus status)
    {
        using var harness = new BillingHarness();
        await ConfigureAsync(harness);
        var customer = await harness.SeedCustomerAsync();
        var service = await harness.SeedServiceAsync("Uncompleted visit", 100m);
        var appointment = await harness.SeedAppointmentAsync(customer, VisitTime, status,
            lines: [(service, "Uncompleted visit", 100m, 0)]);
        await Assert.ThrowsAsync<ConflictException>(() =>
            harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointment)));
        await using var verify = harness.Reopen();
        Assert.Empty(await verify.Invoices.ToListAsync());
        Assert.Empty(await verify.Payments.ToListAsync());
        Assert.Empty(await verify.Set<CustomerLoyaltyTransaction>().ToListAsync());
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft, PaymentStatus.Completed)]
    [InlineData(InvoiceStatus.Void, PaymentStatus.Completed)]
    [InlineData(InvoiceStatus.Issued, PaymentStatus.Pending)]
    [InlineData(InvoiceStatus.PartiallyPaid, PaymentStatus.Completed)]
    [InlineData(InvoiceStatus.Paid, PaymentStatus.Pending)]
    [InlineData(InvoiceStatus.Paid, PaymentStatus.Failed)]
    public async Task Ledger_recording_requires_both_paid_invoice_and_completed_payment(
        InvoiceStatus status, PaymentStatus paymentStatus)
    {
        using var harness = new BillingHarness();
        await ConfigureAsync(harness);
        var customer = await harness.SeedCustomerAsync();
        var invoiceId = await StartAsync(harness, customer, 100m);
        var invoice = await harness.Context.Invoices.Include(row => row.Payments).SingleAsync(row => row.Id == invoiceId);
        invoice.Status = status;
        invoice.Payments.Add(new Payment
        {
            InvoiceId = invoiceId, Amount = 100m, Currency = "AED", Method = PaymentMethod.Cash,
            Status = paymentStatus, PaidAt = harness.Database.Clock.Now
        });
        var loyalty = Loyalty(harness.Context, harness.Database);
        var calculation = await loyalty.CalculateAsync(customer, 100m, 0m, false, 0m);
        loyalty.RecordCheckout(invoice, calculation, null);
        await harness.Context.SaveChangesAsync();
        await using var verify = harness.Reopen();
        Assert.Empty(await verify.Set<CustomerLoyaltyTransaction>().ToListAsync());
    }

    [Theory]
    [InlineData("10.10", "1", "0.51")]
    [InlineData("0.10", "1", "0.01")]
    [InlineData("10.10", "0.5", "0.51")]
    [InlineData("99.99", "1", "5.00")]
    public async Task Earnings_round_to_two_points_decimals_away_from_zero(
        string priceText, string valueText, string expectedText)
    {
        using var harness = new BillingHarness();
        await ConfigureAsync(harness, pointValue: Number(valueText));
        var price = Number(priceText);
        var invoice = await StartAsync(harness, await harness.SeedCustomerAsync(), price);
        var receipt = await harness.Invoices.CheckoutAsync(invoice, Checkout(price));
        Assert.Equal(Number(expectedText), receipt.LoyaltyPointsEarned);
        await using var verify = harness.Reopen();
        Assert.Equal(Number(expectedText), (await verify.Set<CustomerLoyaltyTransaction>().SingleAsync()).Points);
    }

    [Theory]
    [InlineData("Cash")]
    [InlineData("Card")]
    [InlineData("BankTransfer")]
    public async Task Redemption_and_new_earnings_commit_with_each_supported_payment_method(string method)
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 50m);
        var invoice = await StartAsync(harness, customer, 100m);
        var receipt = await harness.Invoices.CheckoutAsync(invoice, Checkout(80m, 20m, method));
        Assert.Equal(20m, receipt.LoyaltyDiscount);
        Assert.Equal(20m, receipt.LoyaltyPointsRedeemed);
        Assert.Equal(4m, receipt.LoyaltyPointsEarned);
        Assert.Equal(80m, receipt.TotalAmount);
        await Assert.ThrowsAsync<ConflictException>(() =>
            harness.Invoices.CheckoutAsync(invoice, Checkout(80m, 20m, method)));

        await using var verify = harness.Reopen();
        var entries = await verify.Set<CustomerLoyaltyTransaction>().Where(row => row.InvoiceId == invoice).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(-20m, Assert.Single(entries, row => row.Type == LoyaltyTransactionType.Redeem).Points);
        Assert.Equal(4m, Assert.Single(entries, row => row.Type == LoyaltyTransactionType.Earn).Points);
        var payment = Assert.Single(await verify.Payments.Where(row => row.InvoiceId == invoice).ToListAsync());
        Assert.Equal(80m, payment.Amount);
        Assert.Equal(method, payment.Method.ToString());
        var summary = await Loyalty(verify, harness.Database).GetSummaryAsync(customer);
        Assert.Equal(34m, summary.AvailablePoints);
        Assert.Equal(54m, summary.LifetimePointsEarned);
        Assert.Equal(20m, summary.PointsRedeemed);
        Assert.Equal(0m, summary.PointsDebt);
    }

    [Fact]
    public async Task Later_settings_changes_do_not_reprice_paid_invoice_receipt_or_ledger_snapshots()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 50m);
        var invoice = await StartAsync(harness, customer, 100m);
        await harness.Invoices.CheckoutAsync(invoice, Checkout(80m, 20m));
        await ConfigureAsync(harness, enabled: false, earning: 25m, pointValue: 2m, maximum: 100m);
        harness.Context.ChangeTracker.Clear();

        var receipt = await harness.Invoices.GetReceiptAsync(invoice);
        var stored = await harness.Invoices.GetAsync(invoice);
        Assert.Equal(20m, receipt.LoyaltyDiscount);
        Assert.Equal(20m, receipt.LoyaltyPointsRedeemed);
        Assert.Equal(4m, receipt.LoyaltyPointsEarned);
        Assert.Equal(80m, receipt.TotalAmount);
        Assert.Equal(receipt.LoyaltyDiscount, stored.LoyaltyDiscount);
        Assert.Equal(receipt.LoyaltyPointsEarned, stored.LoyaltyPointsEarned);
        await using var verify = harness.Reopen();
        var entries = await verify.Set<CustomerLoyaltyTransaction>().Where(row => row.InvoiceId == invoice).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, row =>
        {
            Assert.Equal(5m, row.EarningPercentage);
            Assert.Equal(1m, row.PointValueAed);
            Assert.Equal(80m, row.EligibleSpend);
        });
    }

    [Theory]
    [InlineData(false, "100", "4", "91", "80", "4")]
    [InlineData(true, "105", "4.05", "92", "80.95", "4.05")]
    public async Task Cap_excludes_VAT_and_tip_but_discount_reduces_sticker_price_before_recomputing_VAT(
        bool inclusive, string priceText, string taxText, string totalText, string spendText, string earnedText)
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 100m);
        await harness.SetTaxAsync(5m, inclusive);
        var invoice = await StartAsync(harness, customer, Number(priceText));
        var request = Checkout(Number(totalText), 20m) with { TipAmount = 7m };
        var preview = await harness.Invoices.PreviewCheckoutAsync(invoice, request);
        Assert.Equal(50m, preview.Loyalty.MaximumRedeemablePoints);
        Assert.Equal(50m, preview.Loyalty.MaximumDiscount);
        Assert.Equal(20m, preview.Loyalty.Discount);
        Assert.Equal(Number(spendText), preview.Loyalty.EligibleSpend);
        Assert.Equal(Number(earnedText), preview.Loyalty.PointsEarned);
        Assert.Equal(Number(taxText), preview.TaxTotal);
        Assert.Equal(Number(totalText), preview.TotalAmount);
        var receipt = await harness.Invoices.CheckoutAsync(invoice, request);
        Assert.Equal(preview.TotalAmount, receipt.TotalAmount);
        Assert.Equal(preview.TaxTotal, receipt.TaxTotal);
        Assert.Equal(7m, receipt.TipAmount);
        Assert.Equal(Number(earnedText), receipt.LoyaltyPointsEarned);
    }

    [Fact]
    public async Task Bundle_coupon_and_manual_discounts_are_applied_before_loyalty_and_earning()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 50m);
        var hair = await harness.SeedServiceAsync("Hair", 100m);
        var beard = await harness.SeedServiceAsync("Beard", 60m);
        var bundle = await harness.SeedBundleAsync("Grooming", 140m, [hair, beard]);
        var appointment = await harness.SeedAppointmentAsync(customer, VisitTime,
            lines: [(hair, "Hair", 100m, 0), (beard, "Beard", 60m, 1)],
            serviceBundleId: bundle, bundlePriceSnapshot: 140m);
        var invoice = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointment))).Invoice.Id;
        await harness.SeedCouponAsync("TEN", DiscountType.Percentage, 10m);
        var request = Checkout(100m, 20m) with
        {
            CouponCode = "TEN", ManualDiscountType = "Fixed", ManualDiscountValue = 6m
        };
        var preview = await harness.Invoices.PreviewCheckoutAsync(invoice, request);
        Assert.Equal(20m, preview.BundleDiscount);
        Assert.Equal(14m, preview.CouponDiscount);
        Assert.Equal(6m, preview.ManualDiscount);
        Assert.Equal(100m, preview.Loyalty.EligibleSpend);
        var receipt = await harness.Invoices.CheckoutAsync(invoice, request);
        Assert.Equal(100m, receipt.TotalAmount);
        Assert.Equal(5m, receipt.LoyaltyPointsEarned);
        var stored = await harness.Invoices.GetAsync(invoice);
        Assert.Equal(60m, stored.DiscountTotal);
        Assert.Equal(20m, stored.LoyaltyDiscount);
    }

    public static IEnumerable<object[]> RedemptionCases()
    {
        // balance, merchandise, minimum points, minimum invoice, partial, requested, allowed
        yield return [30m, 100m, 0m, null!, true, 30m, true];
        yield return [30m, 100m, 0m, null!, true, 30.01m, false];
        yield return [100m, 100m, 0m, null!, true, 50m, true];
        yield return [100m, 100m, 0m, null!, true, 50.01m, false];
        yield return [30m, 100m, 10m, null!, true, 9.99m, false];
        yield return [30m, 100m, 10m, null!, true, 10m, true];
        yield return [30m, 99.99m, 0m, 100m, true, 1m, false];
        yield return [30m, 100m, 0m, 100m, true, 1m, true];
        yield return [30m, 100m, 0m, null!, false, 20m, false];
        yield return [30m, 100m, 0m, null!, false, 30m, true];
        yield return [60m, 100m, 0m, null!, false, 50m, false];
        yield return [30m, 100m, 0m, null!, true, -0.01m, false];
        yield return [30m, 100m, 0m, null!, true, 1.001m, false];
        yield return [30m, 100m, 0m, null!, true, 0m, true];
        yield return [30m, 100m, 40m, null!, true, 30m, false];
    }

    [Theory]
    [MemberData(nameof(RedemptionCases))]
    public async Task Redemption_enforces_balance_cap_minimums_full_balance_and_precision(
        decimal balance, decimal merchandise, decimal minimumPoints, decimal? minimumInvoice,
        bool partial, decimal requested, bool allowed)
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, balance);
        await ConfigureAsync(harness, minimumPoints: minimumPoints, minimumInvoice: minimumInvoice, partial: partial);
        var invoice = await StartAsync(harness, customer, merchandise);
        var request = Checkout(merchandise - requested, requested);
        if (allowed)
        {
            var preview = await harness.Invoices.PreviewCheckoutAsync(invoice, request);
            Assert.Equal(requested, preview.Loyalty.PointsToRedeem);
            var receipt = await harness.Invoices.CheckoutAsync(invoice, request);
            Assert.Equal(merchandise - requested, receipt.TotalAmount);
            Assert.Equal(requested, receipt.LoyaltyPointsRedeemed);
            await using var verify = harness.Reopen();
            var redemptions = await verify.Set<CustomerLoyaltyTransaction>()
                .Where(row => row.InvoiceId == invoice && row.Type == LoyaltyTransactionType.Redeem).ToListAsync();
            if (requested == 0m)
                Assert.Empty(redemptions);
            else
                Assert.Equal(-requested, Assert.Single(redemptions).Points);
        }
        else
        {
            var previewError = await Assert.ThrowsAsync<ValidationFailedException>(() =>
                harness.Invoices.PreviewCheckoutAsync(invoice, request));
            Assert.Contains("loyaltyPointsToRedeem", previewError.Errors.Keys);
            var checkoutError = await Assert.ThrowsAsync<ValidationFailedException>(() =>
                harness.Invoices.CheckoutAsync(invoice, request));
            Assert.Contains("loyaltyPointsToRedeem", checkoutError.Errors.Keys);
            await AssertUnpaidAsync(harness, invoice, balance);
        }
    }

    [Fact]
    public async Task A_disabled_program_refuses_redemption_without_spending_existing_balance()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 30m);
        await ConfigureAsync(harness, enabled: false);
        var invoice = await StartAsync(harness, customer, 100m);
        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Invoices.CheckoutAsync(invoice, Checkout(90m, 10m)));
        await AssertUnpaidAsync(harness, invoice, 30m);
    }

    [Fact]
    public async Task New_customer_cannot_spend_points_that_this_checkout_would_earn()
    {
        using var harness = new BillingHarness();
        await ConfigureAsync(harness);
        var invoice = await StartAsync(harness, await harness.SeedCustomerAsync(), 100m);
        var preview = await harness.Invoices.PreviewCheckoutAsync(invoice, Checkout(null));
        Assert.Equal(0m, preview.Loyalty.AvailablePoints);
        Assert.Equal(0m, preview.Loyalty.MaximumRedeemablePoints);
        Assert.Equal(5m, preview.Loyalty.PointsEarned);
        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Invoices.CheckoutAsync(invoice, Checkout(95m, 5m)));
        await AssertUnpaidAsync(harness, invoice, 0m);
    }

    [Fact]
    public async Task Maximum_points_are_floored_not_rounded_past_the_invoice_cap()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 100m);
        await ConfigureAsync(harness, pointValue: 3m);
        var invoice = await StartAsync(harness, customer, 100m);
        var preview = await harness.Invoices.PreviewCheckoutAsync(invoice, Checkout(null, 16.66m));
        Assert.Equal(16.66m, preview.Loyalty.MaximumRedeemablePoints);
        Assert.Equal(49.98m, preview.Loyalty.MaximumDiscount);
        Assert.Equal(49.98m, preview.Loyalty.Discount);
        Assert.Equal(50.02m, preview.TotalAmount);
        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Invoices.CheckoutAsync(invoice, Checkout(49.99m, 16.67m)));
        var receipt = await harness.Invoices.CheckoutAsync(invoice, Checkout(50.02m, 16.66m));
        Assert.Equal(0.83m, receipt.LoyaltyPointsEarned);
    }

    [Fact]
    public async Task Redemption_value_is_floored_to_fils_and_sub_fils_redemption_is_rejected()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 10m);
        await ConfigureAsync(harness, pointValue: 0.3333m);
        var invoice = await StartAsync(harness, customer, 100m);
        var receipt = await harness.Invoices.CheckoutAsync(invoice, Checkout(99.67m, 1m));
        Assert.Equal(0.33m, receipt.LoyaltyDiscount);
        await ConfigureAsync(harness, pointValue: 0.0001m);
        var next = await StartAsync(harness, customer, 100m);
        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Invoices.CheckoutAsync(next, Checkout(100m, 1m)));
    }

    [Fact]
    public async Task A_hundred_percent_cap_allows_zero_payable_and_records_redemption_but_no_earn()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 100m);
        await ConfigureAsync(harness, maximum: 100m);
        var invoice = await StartAsync(harness, customer, 100m);
        var receipt = await harness.Invoices.CheckoutAsync(invoice, Checkout(0m, 100m));
        Assert.Equal(0m, receipt.TotalAmount);
        Assert.Equal(0m, receipt.LoyaltyPointsEarned);
        await using var verify = harness.Reopen();
        var stored = await verify.Invoices.Include(row => row.Payments).SingleAsync(row => row.Id == invoice);
        Assert.Equal(InvoiceStatus.Paid, stored.Status);
        Assert.Equal(0m, Assert.Single(stored.Payments).Amount);
        var entry = Assert.Single(await verify.Set<CustomerLoyaltyTransaction>().Where(row => row.InvoiceId == invoice).ToListAsync());
        Assert.Equal(LoyaltyTransactionType.Redeem, entry.Type);
        Assert.Equal(-100m, entry.Points);
        Assert.Equal(0m, (await Loyalty(verify, harness.Database).GetSummaryAsync(customer)).AvailablePoints);
    }

    [Fact]
    public async Task Preview_is_read_only_even_when_it_quotes_redemption_discounts_and_tip()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 50m);
        var (invoice, product) = await RetailInvoiceAsync(harness, customer);
        await harness.SeedCouponAsync("PREVIEW", discountValue: 10m);
        var before = await harness.Invoices.GetAsync(invoice);
        var request = Checkout(null, 20m) with { CouponCode = "PREVIEW", TipAmount = 7m, PaymentMethod = null };
        var first = await harness.Invoices.PreviewCheckoutAsync(invoice, request);
        var second = await harness.Invoices.PreviewCheckoutAsync(invoice, request);
        Assert.Equal(first, second);
        Assert.Equal(77m, first.TotalAmount);
        Assert.Equal(3.5m, first.Loyalty.PointsEarned);
        await AssertUnpaidAsync(harness, invoice, 50m);
        await using var verify = harness.Reopen();
        var stored = await verify.Invoices.SingleAsync(row => row.Id == invoice);
        Assert.Equal(100m, stored.TotalAmount);
        Assert.Equal(0m, stored.CouponDiscount);
        Assert.Equal(0m, stored.TipAmount);
        Assert.Equal(before.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(2m, (await verify.InventoryProducts.SingleAsync(row => row.Id == product)).CurrentStock);
        Assert.Empty(await verify.StockMovements.Where(row => row.InvoiceId == invoice).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wrong_amount_or_unavailable_stock_leaves_payment_stock_and_ledger_unchanged(bool failStock)
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 50m);
        var (invoice, product) = await RetailInvoiceAsync(harness, customer);
        if (failStock)
        {
            await using var otherSale = harness.Reopen();
            (await otherSale.InventoryProducts.SingleAsync(row => row.Id == product)).ApplyStockChange(-2m);
            await otherSale.SaveChangesAsync();
            harness.Context.ChangeTracker.Clear();
        }
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Invoices.CheckoutAsync(invoice, Checkout(failStock ? 80m : 79.99m, 20m)));
        Assert.Contains(failStock ? "items" : "amount", error.Errors.Keys);
        await AssertUnpaidAsync(harness, invoice, 50m);
        await using var verify = harness.Reopen();
        Assert.Equal(failStock ? 0m : 2m,
            (await verify.InventoryProducts.SingleAsync(row => row.Id == product)).CurrentStock);
        Assert.Empty(await verify.StockMovements.Where(row => row.InvoiceId == invoice).ToListAsync());
    }

    [Fact]
    public async Task Failure_after_database_save_rolls_back_payment_stock_and_both_ledger_entries()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 50m);
        var (invoice, product) = await RetailInvoiceAsync(harness, customer);
        var failure = new FailAfterCheckoutSave();
        var options = new DbContextOptionsBuilder<DeBlissDbContext>()
            .UseSqlite(harness.Database.ConnectionString)
            .AddInterceptors(new AuditableEntityInterceptor(harness.Database.Clock), failure).Options;
        await using (var request = new DeBlissDbContext(options))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Invoices(request, harness.Database).CheckoutAsync(invoice, Checkout(80m, 20m)));
            Assert.Equal(FailAfterCheckoutSave.Message, exception.Message);
            Assert.True(failure.ReachedDatabaseSave);
        }
        await AssertUnpaidAsync(harness, invoice, 50m);
        await using var verify = harness.Reopen();
        Assert.Equal(2m, (await verify.InventoryProducts.SingleAsync(row => row.Id == product)).CurrentStock);
        Assert.Empty(await verify.StockMovements.Where(row => row.InvoiceId == invoice).ToListAsync());

        // The rolled-back request did not consume the invoice or ledger idempotency keys.
        await using var retry = harness.Reopen();
        await Invoices(retry, harness.Database).CheckoutAsync(invoice, Checkout(80m, 20m));
        await using var final = harness.Reopen();
        Assert.Equal(1m, (await final.InventoryProducts.SingleAsync(row => row.Id == product)).CurrentStock);
        Assert.Single(await final.Payments.Where(row => row.InvoiceId == invoice).ToListAsync());
        Assert.Single(await final.StockMovements.Where(row => row.InvoiceId == invoice).ToListAsync());
        Assert.Equal(2, await final.Set<CustomerLoyaltyTransaction>().CountAsync(row => row.InvoiceId == invoice));
    }

    [Fact]
    public async Task Separate_contexts_cannot_spend_one_customers_balance_on_two_invoices()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 50m);
        await ConfigureAsync(harness, earning: 0m);
        var first = await StartAsync(harness, customer, 100m);
        var second = await StartAsync(harness, customer, 100m);
        var outcomes = await RaceAsync(harness, first, second, Checkout(60m, 40m));
        Assert.Single(outcomes, outcome => outcome is null);
        AssertAcceptedRaceFailure(Assert.Single(outcomes, outcome => outcome is not null)!);
        await using var verify = harness.Reopen();
        var invoices = await verify.Invoices.Where(row => row.Id == first || row.Id == second).ToListAsync();
        Assert.Single(invoices, row => row.Status == InvoiceStatus.Paid);
        Assert.Single(invoices, row => row.Status == InvoiceStatus.Draft);
        Assert.Single(await verify.Payments.Where(row => row.InvoiceId == first || row.InvoiceId == second).ToListAsync());
        var entries = await verify.Set<CustomerLoyaltyTransaction>().Where(row => row.CustomerId == customer).ToListAsync();
        Assert.Equal(10m, entries.Sum(row => row.Points));
        Assert.Equal(-40m, Assert.Single(entries, row => row.Type == LoyaltyTransactionType.Redeem).Points);
    }

    [Fact]
    public async Task Separate_contexts_checking_out_the_same_invoice_create_only_one_payment_and_award()
    {
        using var harness = new BillingHarness();
        await ConfigureAsync(harness);
        var invoice = await StartAsync(harness, await harness.SeedCustomerAsync(), 100m);
        var outcomes = await RaceAsync(harness, invoice, invoice, Checkout(100m));
        Assert.Single(outcomes, outcome => outcome is null);
        AssertAcceptedRaceFailure(Assert.Single(outcomes, outcome => outcome is not null)!);
        await using var verify = harness.Reopen();
        Assert.Equal(InvoiceStatus.Paid, (await verify.Invoices.SingleAsync()).Status);
        Assert.Single(await verify.Payments.ToListAsync());
        var earned = Assert.Single(await verify.Set<CustomerLoyaltyTransaction>().ToListAsync());
        Assert.Equal(LoyaltyTransactionType.Earn, earned.Type);
        Assert.Equal(5m, earned.Points);
    }

    [Fact]
    public async Task Invoice_status_concurrency_token_rejects_a_stale_draft_writer()
    {
        using var harness = new BillingHarness();
        await ConfigureAsync(harness);
        var invoice = await StartAsync(harness, await harness.SeedCustomerAsync(), 100m);
        await using var stale = harness.Reopen();
        var staleInvoice = await stale.Invoices.SingleAsync(row => row.Id == invoice);
        Assert.True(stale.Model.FindEntityType(typeof(Invoice))!.FindProperty(nameof(Invoice.Status))!.IsConcurrencyToken);
        await harness.Invoices.CheckoutAsync(invoice, Checkout(100m));
        staleInvoice.Status = InvoiceStatus.Void;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var verify = harness.Reopen();
        Assert.Equal(InvoiceStatus.Paid, (await verify.Invoices.SingleAsync()).Status);
        Assert.Single(await verify.Set<CustomerLoyaltyTransaction>().ToListAsync());
    }

    [Fact]
    public async Task Free_product_line_edits_cannot_bypass_a_concurrent_paid_transition()
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        Guid product;
        await using (var seed = harness.Reopen())
            product = await InventorySeed.ProductAsync(seed, "Free sample", "FREE-SAMPLE",
                sellingPrice: 0m, currentStock: 3m);
        var invoice = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(null, customer))).Invoice.Id;
        var draft = await harness.Invoices.AddLineAsync(invoice, new AddInvoiceLineRequest(product, 1m));
        await using var stale = harness.Reopen();
        var staleService = Invoices(stale, harness.Database);
        await staleService.GetAsync(invoice);
        await harness.Invoices.CheckoutAsync(invoice, Checkout(0m));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            staleService.UpdateLineAsync(invoice, draft.Items.Single().Id, new UpdateInvoiceLineRequest(2m)));
        await using var verify = harness.Reopen();
        Assert.Equal(1m, (await verify.InvoiceItems.SingleAsync(row => row.InvoiceId == invoice)).Quantity);
        Assert.Equal(2m, (await verify.InventoryProducts.SingleAsync(row => row.Id == product)).CurrentStock);
        Assert.Single(await verify.Payments.Where(row => row.InvoiceId == invoice).ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sub_fils_discount_or_tip_is_rejected_without_points_or_payment(bool tip)
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        var invoice = await StartAsync(harness, customer, 100m);
        var request = tip
            ? Checkout(100.001m) with { TipAmount = 0.001m }
            : Checkout(99.999m) with { ManualDiscountType = "Fixed", ManualDiscountValue = 0.001m };
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => harness.Invoices.CheckoutAsync(invoice, request));
        Assert.Contains(tip ? "tipAmount" : "manualDiscountValue", error.Errors.Keys);
        await AssertUnpaidAsync(harness, invoice, 0m);
    }

    [Theory]
    [InlineData(LoyaltyTransactionType.Earn, false)]
    [InlineData(LoyaltyTransactionType.Redeem, false)]
    [InlineData(LoyaltyTransactionType.Earn, true)]
    public async Task Database_rejects_duplicate_invoice_type_and_cross_invoice_idempotency_key(
        LoyaltyTransactionType type, bool duplicateKey)
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 50m);
        var invoice = await StartAsync(harness, customer, 100m);
        await harness.Invoices.CheckoutAsync(invoice, Checkout(80m, 20m));
        var otherInvoice = duplicateKey ? await StartAsync(harness, customer, 100m) : invoice;
        await using var attempt = harness.Reopen();
        var original = await attempt.Set<CustomerLoyaltyTransaction>().AsNoTracking()
            .SingleAsync(row => row.InvoiceId == invoice && row.Type == type);
        attempt.Set<CustomerLoyaltyTransaction>().Add(new CustomerLoyaltyTransaction
        {
            Type = type, CustomerId = customer, InvoiceId = otherInvoice, Points = original.Points,
            OccurredAt = original.OccurredAt, Description = "Attempted duplicate",
            EligibleSpend = original.EligibleSpend, EarningPercentage = original.EarningPercentage,
            PointValueAed = original.PointValueAed,
            IdempotencyKey = duplicateKey ? original.IdempotencyKey : $"different:{Guid.NewGuid():N}"
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => attempt.SaveChangesAsync());
        Assert.Equal(19, Assert.IsType<SqliteException>(error.InnerException).SqliteErrorCode);
        await using var verify = harness.Reopen();
        Assert.Equal(3, await verify.Set<CustomerLoyaltyTransaction>().CountAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Saved_history_rejects_modification_and_deletion_on_sync_and_async_save(bool delete, bool asyncSave)
    {
        using var harness = new BillingHarness();
        var customer = await harness.SeedCustomerAsync();
        await FundAsync(harness, customer, 50m);
        await using var attempt = harness.Reopen();
        var original = await attempt.Set<CustomerLoyaltyTransaction>().SingleAsync();
        if (delete)
            attempt.Remove(original);
        else
            attempt.Entry(original).Property(row => row.Description).CurrentValue = "Rewritten history";
        if (asyncSave)
            await Assert.ThrowsAsync<InvalidOperationException>(() => attempt.SaveChangesAsync());
        else
            Assert.Throws<InvalidOperationException>(() => attempt.SaveChanges());
        await using var verify = harness.Reopen();
        var unchanged = await verify.Set<CustomerLoyaltyTransaction>().SingleAsync();
        Assert.Equal(original.Id, unchanged.Id);
        Assert.Equal(50m, unchanged.Points);
        Assert.NotEqual("Rewritten history", unchanged.Description);
    }

    private static CheckoutRequest Checkout(decimal? amount, decimal points = 0m, string method = "Cash") =>
        new(null, null, null, method, amount, null, LoyaltyPointsToRedeem: points);

    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static LoyaltyService Loyalty(DeBlissDbContext context, SqliteTestDatabase database) =>
        new(new LoyaltyRepository(context), new CustomerRepository(context), context, database.Clock);

    private static InvoiceService Invoices(DeBlissDbContext context, SqliteTestDatabase database) =>
        new(new InvoiceRepository(context), new CouponRepository(context), new AppointmentRepository(context),
            new CustomerRepository(context), new InventoryRepository(context), new SalonSettingsRepository(context),
            context, database.Clock, Loyalty(context, database));

    private static async Task ConfigureAsync(BillingHarness harness, bool enabled = true, decimal earning = 5m,
        decimal pointValue = 1m, decimal minimumPoints = 0m, decimal maximum = 50m,
        bool partial = true, decimal? minimumInvoice = null)
    {
        await Loyalty(harness.Context, harness.Database).UpdateSettingsAsync(
            new SaveLoyaltySettingsRequest(enabled, earning, pointValue, minimumPoints, maximum, partial, minimumInvoice));
    }

    private static async Task<Guid> StartAsync(BillingHarness harness, Guid? customer, decimal price)
    {
        var name = $"Visit {Guid.NewGuid():N}";
        var service = await harness.SeedServiceAsync(name, price);
        var appointment = await harness.SeedAppointmentAsync(customer, VisitTime,
            walkInCustomerName: customer is null ? "Walk-in" : null,
            lines: [(service, name, price, 0)]);
        return (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointment))).Invoice.Id;
    }

    // Opening points are earned by a real paid checkout, never invented by inserting a ledger row.
    private static async Task FundAsync(BillingHarness harness, Guid customer, decimal points)
    {
        await ConfigureAsync(harness, earning: 100m);
        var invoice = await StartAsync(harness, customer, points);
        var paid = await harness.Invoices.CheckoutAsync(invoice, Checkout(points));
        Assert.Equal(points, paid.LoyaltyPointsEarned);
        await ConfigureAsync(harness);
    }

    private static async Task<(Guid Invoice, Guid Product)> RetailInvoiceAsync(BillingHarness harness, Guid customer)
    {
        Guid product;
        await using (var seed = harness.Reopen())
            product = await InventorySeed.ProductAsync(seed, "Retail mask", "LOYALTY-MASK",
                sellingPrice: 100m, currentStock: 2m);
        harness.Context.ChangeTracker.Clear();
        var invoice = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(null, customer))).Invoice.Id;
        await harness.Invoices.AddLineAsync(invoice, new AddInvoiceLineRequest(product, 1m));
        return (invoice, product);
    }

    private static async Task AssertUnpaidAsync(BillingHarness harness, Guid invoice, decimal expectedBalance)
    {
        await using var verify = harness.Reopen();
        var stored = await verify.Invoices.Include(row => row.Payments).SingleAsync(row => row.Id == invoice);
        Assert.Equal(InvoiceStatus.Draft, stored.Status);
        Assert.Empty(stored.Payments);
        Assert.Equal(0m, stored.LoyaltyDiscount);
        Assert.Equal(0m, stored.LoyaltyPointsRedeemed);
        Assert.Equal(0m, stored.LoyaltyPointsEarned);
        Assert.Empty(await verify.Set<CustomerLoyaltyTransaction>().Where(row => row.InvoiceId == invoice).ToListAsync());
        var entries = await verify.Set<CustomerLoyaltyTransaction>().Where(row => row.CustomerId == stored.CustomerId).ToListAsync();
        Assert.Equal(expectedBalance, entries.Sum(row => row.Points));
    }

    private static async Task<Exception?[]> RaceAsync(BillingHarness harness, Guid first, Guid second, CheckoutRequest request)
    {
        using var ready = new Barrier(2);
        Task<Exception?> Run(Guid id) => Task.Run<Exception?>(async () =>
        {
            await using var context = harness.Reopen();
            var service = Invoices(context, harness.Database);
            if (!ready.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Checkout participants did not reach the start barrier.");
            return await Record.ExceptionAsync(() => service.CheckoutAsync(id, request));
        });
        return await Task.WhenAll(Run(first), Run(second));
    }

    private static void AssertAcceptedRaceFailure(Exception exception)
    {
        if (exception is ConflictException or ValidationFailedException or DbUpdateConcurrencyException)
            return;
        // SQLite may surface BUSY/LOCKED rather than the production provider's conflict handling.
        // This allowance is paired with fresh-context assertions of payment/ledger consistency;
        // it is not evidence that live SQL Server range-lock behavior has been exercised.
        var sqlite = exception as SqliteException ?? exception.InnerException as SqliteException;
        Assert.True(sqlite is { SqliteErrorCode: 5 or 6 }, $"Unexpected checkout race failure: {exception}");
    }

    private sealed class FailAfterCheckoutSave : SaveChangesInterceptor
    {
        public const string Message = "Test-only failure after checkout save, before commit.";
        public bool ReachedDatabaseSave { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            Assert.Contains(context.ChangeTracker.Entries<CustomerLoyaltyTransaction>(), row => row.Entity.Type == LoyaltyTransactionType.Earn);
            Assert.Contains(context.ChangeTracker.Entries<CustomerLoyaltyTransaction>(), row => row.Entity.Type == LoyaltyTransactionType.Redeem);
            Assert.Contains(context.ChangeTracker.Entries<Payment>(), row => row.Entity.Status == PaymentStatus.Completed);
            ReachedDatabaseSave = true;
            throw new InvalidOperationException(Message);
        }
    }
}
