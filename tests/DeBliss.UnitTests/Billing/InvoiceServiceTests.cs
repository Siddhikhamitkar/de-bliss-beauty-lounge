using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Billing;

public class InvoiceServiceTests
{
    private static readonly DateTimeOffset VisitTime =
        new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    private static CheckoutRequest Checkout(
        string? couponCode = null,
        string? manualDiscountType = null,
        decimal? manualDiscountValue = null,
        string? paymentMethod = "Cash",
        decimal? amount = 160m,
        string? reference = null) =>
        new(couponCode, manualDiscountType, manualDiscountValue, paymentMethod, amount, reference);

    /// <summary>
    /// A completed visit of two services. The booked prices (100 + 60) deliberately
    /// differ from the catalogue prices (120 + 40), so anything that wrongly reads
    /// the catalogue instead of the booking snapshot shows up immediately.
    /// </summary>
    private static async Task<Guid> ArrangedVisitAsync(
        BillingHarness harness,
        decimal firstPrice = 100m,
        decimal secondPrice = 60m,
        AppointmentStatus status = AppointmentStatus.Completed)
    {
        var customerId = await harness.SeedCustomerAsync();
        var haircutId = await harness.SeedServiceAsync("Haircut", 120m);
        var beardTrimId = await harness.SeedServiceAsync("Beard Trim", 40m);

        return await harness.SeedAppointmentAsync(
            customerId,
            VisitTime,
            status,
            lines:
            [
                (haircutId, "Haircut", firstPrice, 0),
                (beardTrimId, "Beard Trim", secondPrice, 1)
            ]);
    }

    [Fact]
    public async Task Starting_a_checkout_creates_a_draft_invoice_from_the_appointments_lines()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);

        var result = await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId));

        Assert.True(result.WasCreated);

        var invoice = result.Invoice;

        Assert.Equal("Draft", invoice.Status);
        Assert.Equal("INV-2026-0001", invoice.InvoiceNumber);
        Assert.Equal("AED", invoice.Currency);
        Assert.Equal("Layla Haddad", invoice.CustomerName);
        Assert.Equal(appointmentId, invoice.AppointmentId);
        Assert.Null(invoice.IssuedAt);

        Assert.Collection(
            invoice.Items,
            first =>
            {
                Assert.Equal("Haircut", first.Description);
                Assert.Equal(1m, first.Quantity);
                Assert.Equal(100m, first.UnitPrice);
                Assert.Equal(100m, first.LineTotal);
            },
            second =>
            {
                Assert.Equal("Beard Trim", second.Description);
                Assert.Equal(60m, second.UnitPrice);
                Assert.Equal(60m, second.LineTotal);
            });

        Assert.Equal(160m, invoice.SubTotal);
        Assert.Equal(0m, invoice.CouponDiscount);
        Assert.Equal(0m, invoice.ManualDiscount);
        Assert.Equal(0m, invoice.DiscountTotal);
        Assert.Equal(160m, invoice.TotalAmount);
        Assert.Empty(invoice.Payments);

        await using var verify = harness.Reopen();

        var stored = await verify.Invoices.Include(i => i.Items).SingleAsync();

        Assert.Equal(InvoiceStatus.Draft, stored.Status);
        Assert.Equal("INV-2026-0001", stored.InvoiceNumber);
        Assert.Equal(2, stored.Items.Count);
    }

    [Fact]
    public async Task The_invoice_keeps_the_booked_prices_when_the_catalogue_changes()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);

        await using (var context = harness.Reopen())
        {
            var haircut = await context.Services.SingleAsync(service => service.Name == "Haircut");
            haircut.Price = 150m;
            await context.SaveChangesAsync();
        }

        var result = await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId));

        Assert.Equal(100m, result.Invoice.Items[0].UnitPrice);
        Assert.Equal(160m, result.Invoice.TotalAmount);

        await using var verify = harness.Reopen();

        Assert.Equal(150m, (await verify.Services.SingleAsync(service => service.Name == "Haircut")).Price);
    }

    [Fact]
    public async Task Starting_again_for_the_same_appointment_returns_the_existing_invoice()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);

        var first = await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId));
        var second = await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId));

        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.Invoice.Id, second.Invoice.Id);

        await using var verify = harness.Reopen();

        Assert.Equal(1, await verify.Invoices.CountAsync());
    }

    [Fact]
    public async Task Starting_a_checkout_requires_a_completed_appointment()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness, status: AppointmentStatus.Confirmed);

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId)));

        Assert.Equal("Only a completed appointment can be checked out.", failure.Message);
    }

    [Fact]
    public async Task Starting_a_checkout_for_an_unknown_appointment_is_not_found()
    {
        using var harness = new BillingHarness();
        var missingId = Guid.NewGuid();

        var failure = await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(missingId)));

        Assert.Contains(missingId.ToString(), failure.Message);
    }

    [Fact]
    public async Task Invoice_numbers_continue_the_sequence_for_the_year()
    {
        using var harness = new BillingHarness();
        await harness.SeedInvoiceAsync("INV-2026-0007");
        await harness.SeedInvoiceAsync("INV-2025-0099");
        var appointmentId = await ArrangedVisitAsync(harness);

        var result = await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId));

        Assert.Equal("INV-2026-0008", result.Invoice.InvoiceNumber);
    }

    [Fact]
    public async Task A_walk_in_appointment_invoices_under_the_walk_in_name()
    {
        using var harness = new BillingHarness();
        var serviceId = await harness.SeedServiceAsync("Haircut", 120m);
        var appointmentId = await harness.SeedAppointmentAsync(
            customerId: null,
            VisitTime,
            walkInCustomerName: "Noor",
            lines: [(serviceId, "Haircut", 100m, 0)]);

        var result = await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId));

        Assert.Null(result.Invoice.CustomerName);
        Assert.Equal("Noor", result.Invoice.WalkInCustomerName);
    }

    [Fact]
    public async Task Checkout_marks_the_invoice_paid_and_records_the_cash_payment()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var receipt = await harness.Invoices.CheckoutAsync(invoiceId, Checkout());

        Assert.Equal("Cash", receipt.PaymentMethod);
        Assert.Equal(160m, receipt.TotalAmount);
        Assert.Equal(harness.Database.Clock.Now, receipt.PaidAt);

        await using var verify = harness.Reopen();

        var stored = await verify.Invoices.Include(i => i.Payments).SingleAsync();

        Assert.Equal(InvoiceStatus.Paid, stored.Status);
        Assert.Equal(160m, stored.TotalAmount);
        Assert.Equal(harness.Database.Clock.Now, stored.IssuedAt);

        var payment = Assert.Single(stored.Payments);

        Assert.Equal(160m, payment.Amount);
        Assert.Equal("AED", payment.Currency);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Null(payment.Reference);
        Assert.Equal(harness.Database.Clock.Now, payment.PaidAt);
    }

    [Fact]
    public async Task A_fixed_coupon_reduces_the_total_and_is_kept_by_code()
    {
        using var harness = new BillingHarness();
        var couponId = await harness.SeedCouponAsync("DEBLISS50", discountValue: 50m);
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var receipt = await harness.Invoices.CheckoutAsync(
            invoiceId, Checkout(couponCode: " debliss50 ", amount: 110m));

        var stored = await harness.Invoices.GetAsync(invoiceId);

        Assert.Equal(couponId, stored.CouponId);
        Assert.Equal("DEBLISS50", stored.CouponCode);
        Assert.Equal(50m, stored.CouponDiscount);
        Assert.Equal(50m, stored.DiscountTotal);
        Assert.Equal(110m, stored.TotalAmount);
        Assert.Equal(110m, receipt.TotalAmount);
        Assert.Equal("DEBLISS50", receipt.CouponCode);
    }

    [Fact]
    public async Task A_percentage_coupon_rounds_the_discount_to_fils()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("TENOFF", DiscountType.Percentage, 10m);
        var appointmentId = await ArrangedVisitAsync(harness, firstPrice: 99.99m);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var receipt = await harness.Invoices.CheckoutAsync(
            invoiceId, Checkout(couponCode: "TENOFF", amount: 143.99m));

        // 10% of 159.99 is 15.999, rounded to 16.00 away from zero.
        Assert.Equal(16m, receipt.CouponDiscount);
        Assert.Equal(143.99m, receipt.TotalAmount);
        Assert.Equal(143.99m, receipt.SubTotal - receipt.CouponDiscount - receipt.ManualDiscount);
    }

    [Theory]
    [InlineData("NOPE")]
    [InlineData("EXPIRED")]
    [InlineData("SWITCHEDOFF")]
    public async Task A_coupon_that_is_not_applicable_is_rejected_with_one_generic_message(string code)
    {
        using var harness = new BillingHarness();

        if (code == "EXPIRED")
        {
            await harness.SeedCouponAsync("EXPIRED", validUntil: new DateOnly(2026, 10, 2));
        }
        else if (code == "SWITCHEDOFF")
        {
            await harness.SeedCouponAsync("SWITCHEDOFF", isActive: false);
        }

        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Invoices.CheckoutAsync(invoiceId, Checkout(couponCode: code)));

        Assert.Equal(
            "Coupon code is invalid or expired.",
            Assert.Single(failure.Errors["couponCode"]));

        await using var verify = harness.Reopen();

        Assert.Equal(InvoiceStatus.Draft, (await verify.Invoices.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_coupon_discount_above_the_subtotal_is_rejected()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("HUGE", discountValue: 200m);
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Invoices.CheckoutAsync(invoiceId, Checkout(couponCode: "HUGE")));

        Assert.Equal(
            "Discount cannot exceed the subtotal.",
            Assert.Single(failure.Errors["couponCode"]));
    }

    [Fact]
    public async Task A_fixed_manual_discount_reduces_the_total()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var receipt = await harness.Invoices.CheckoutAsync(
            invoiceId,
            Checkout(manualDiscountType: "Fixed", manualDiscountValue: 25m, paymentMethod: "Card", amount: 135m));

        Assert.Equal(25m, receipt.ManualDiscount);
        Assert.Equal(0m, receipt.CouponDiscount);
        Assert.Equal(135m, receipt.TotalAmount);
        Assert.Equal("Card", receipt.PaymentMethod);
    }

    [Fact]
    public async Task A_percentage_manual_discount_reduces_the_total()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var receipt = await harness.Invoices.CheckoutAsync(
            invoiceId,
            Checkout(manualDiscountType: "Percentage", manualDiscountValue: 10m, amount: 144m));

        Assert.Equal(16m, receipt.ManualDiscount);
        Assert.Equal(144m, receipt.TotalAmount);
    }

    [Fact]
    public async Task A_manual_discount_above_the_subtotal_is_rejected()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Invoices.CheckoutAsync(
                invoiceId,
                Checkout(manualDiscountType: "Fixed", manualDiscountValue: 200m)));

        Assert.Equal(
            "Discount cannot exceed the subtotal.",
            Assert.Single(failure.Errors["manualDiscountValue"]));
    }

    [Fact]
    public async Task Combined_discounts_cannot_exceed_the_subtotal()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("BIG", discountValue: 100m);
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Invoices.CheckoutAsync(
                invoiceId,
                Checkout(couponCode: "BIG", manualDiscountType: "Fixed", manualDiscountValue: 80m, amount: 0m)));

        Assert.Equal(
            "Discounts cannot exceed the subtotal.",
            Assert.Single(failure.Errors["manualDiscountValue"]));

        await using var verify = harness.Reopen();
        var stored = await verify.Invoices.Include(i => i.Payments).SingleAsync();

        Assert.Equal(InvoiceStatus.Draft, stored.Status);
        Assert.Empty(stored.Payments);
    }

    [Fact]
    public async Task A_coupon_and_a_manual_discount_are_stored_separately()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("TWENTY", DiscountType.Percentage, 20m);
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var receipt = await harness.Invoices.CheckoutAsync(
            invoiceId,
            Checkout(couponCode: "TWENTY", manualDiscountType: "Fixed", manualDiscountValue: 8m, amount: 120m));

        Assert.Equal(32m, receipt.CouponDiscount);
        Assert.Equal(8m, receipt.ManualDiscount);
        Assert.Equal(120m, receipt.TotalAmount);

        var stored = await harness.Invoices.GetAsync(invoiceId);

        Assert.Equal(32m, stored.CouponDiscount);
        Assert.Equal(8m, stored.ManualDiscount);
        Assert.Equal(40m, stored.DiscountTotal);
        Assert.Equal(120m, stored.Payments.Single().Amount);
    }

    [Fact]
    public async Task A_checkout_can_reach_zero_and_still_records_the_collection()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("FREE", discountValue: 160m);
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var receipt = await harness.Invoices.CheckoutAsync(
            invoiceId, Checkout(couponCode: "FREE", amount: 0m));

        Assert.Equal(0m, receipt.TotalAmount);

        await using var verify = harness.Reopen();
        var stored = await verify.Invoices.Include(i => i.Payments).SingleAsync();

        Assert.Equal(InvoiceStatus.Paid, stored.Status);
        Assert.Equal(0m, stored.TotalAmount);

        var payment = Assert.Single(stored.Payments);

        Assert.Equal(0m, payment.Amount);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
    }

    [Theory]
    [InlineData("Cash", "Cash")]
    [InlineData("cash", "Cash")]
    [InlineData("Card", "Card")]
    [InlineData("BankTransfer", "BankTransfer")]
    [InlineData("banktransfer", "BankTransfer")]
    public async Task Only_cash_card_and_bank_transfer_are_accepted(string method, string expected)
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var receipt = await harness.Invoices.CheckoutAsync(invoiceId, Checkout(paymentMethod: method));

        Assert.Equal(expected, receipt.PaymentMethod);
    }

    [Theory]
    [InlineData("MobileWallet")]
    [InlineData("GiftCard")]
    [InlineData("blah")]
    [InlineData(null)]
    public async Task An_unsupported_payment_method_is_rejected(string? method)
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Invoices.CheckoutAsync(invoiceId, Checkout(paymentMethod: method)));

        Assert.Equal(
            "Select a payment method (Cash, Card or Bank Transfer).",
            Assert.Single(failure.Errors["paymentMethod"]));
    }

    [Fact]
    public async Task An_amount_that_does_not_match_the_total_is_rejected()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Invoices.CheckoutAsync(invoiceId, Checkout(amount: 150m)));

        Assert.Equal(
            "Payment amount must equal the invoice total of AED 160.00.",
            Assert.Single(failure.Errors["amount"]));
    }

    [Fact]
    public async Task A_missing_amount_is_rejected()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Invoices.CheckoutAsync(invoiceId, Checkout(amount: null)));

        Assert.Equal(
            "Enter the amount collected.",
            Assert.Single(failure.Errors["amount"]));
    }

    [Fact]
    public async Task Missing_payment_details_report_every_problem_at_once()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Invoices.CheckoutAsync(invoiceId, Checkout(paymentMethod: null, amount: null)));

        Assert.Equal(2, failure.Errors.Count);
        Assert.Contains("paymentMethod", failure.Errors.Keys);
        Assert.Contains("amount", failure.Errors.Keys);
    }

    [Fact]
    public async Task A_bank_transfer_reference_is_stored_on_the_payment_and_receipt()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var receipt = await harness.Invoices.CheckoutAsync(
            invoiceId,
            Checkout(paymentMethod: "BankTransfer", reference: " FT-2026-8891 "));

        Assert.Equal("FT-2026-8891", receipt.PaymentReference);

        await using var verify = harness.Reopen();

        Assert.Equal("FT-2026-8891", (await verify.Payments.SingleAsync()).Reference);
    }

    [Fact]
    public async Task An_overlong_reference_is_rejected()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Invoices.CheckoutAsync(invoiceId, Checkout(reference: new string('x', 101))));

        Assert.Equal(
            "Reference must be 100 characters or fewer.",
            Assert.Single(failure.Errors["reference"]));
    }

    [Fact]
    public async Task Checking_out_an_already_paid_invoice_is_rejected()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        await harness.Invoices.CheckoutAsync(invoiceId, Checkout());

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Invoices.CheckoutAsync(invoiceId, Checkout()));

        Assert.Equal("Invoice 'INV-2026-0001' has already been paid.", failure.Message);

        await using var verify = harness.Reopen();

        Assert.Equal(1, await verify.Payments.CountAsync());
    }

    [Fact]
    public async Task The_receipt_shows_the_salon_and_the_checkout_detail()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("DEBLISS10", discountValue: 10m);
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        await harness.Invoices.CheckoutAsync(
            invoiceId,
            Checkout(
                couponCode: "DEBLISS10",
                manualDiscountType: "Fixed",
                manualDiscountValue: 5m,
                paymentMethod: "Card",
                amount: 145m,
                reference: "AUTH-77"));

        var receipt = await harness.Invoices.GetReceiptAsync(invoiceId);

        Assert.Equal("De Bliss Beauty Lounge", receipt.SalonName);
        Assert.Equal("INV-2026-0001", receipt.InvoiceNumber);
        Assert.Equal("Layla Haddad", receipt.CustomerName);
        Assert.Equal(2, receipt.Items.Count);
        Assert.Equal("Haircut", receipt.Items[0].Description);
        Assert.Equal(160m, receipt.SubTotal);
        Assert.Equal("DEBLISS10", receipt.CouponCode);
        Assert.Equal(10m, receipt.CouponDiscount);
        Assert.Equal(5m, receipt.ManualDiscount);
        Assert.Equal(145m, receipt.TotalAmount);
        Assert.Equal("AED", receipt.Currency);
        Assert.Equal("Card", receipt.PaymentMethod);
        Assert.Equal("AUTH-77", receipt.PaymentReference);
        Assert.Equal(harness.Database.Clock.Now, receipt.PaidAt);
        Assert.Equal(harness.Database.Clock.Now, receipt.IssuedAt);
    }

    [Fact]
    public async Task A_receipt_before_payment_is_rejected()
    {
        using var harness = new BillingHarness();
        var appointmentId = await ArrangedVisitAsync(harness);
        var invoiceId = (await harness.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Invoices.GetReceiptAsync(invoiceId));

        Assert.Equal("Invoice 'INV-2026-0001' has not been paid yet.", failure.Message);
    }

    [Fact]
    public async Task Getting_an_unknown_invoice_is_not_found()
    {
        using var harness = new BillingHarness();
        var missingId = Guid.NewGuid();

        var failure = await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Invoices.GetAsync(missingId));

        Assert.Contains(missingId.ToString(), failure.Message);
    }
}
