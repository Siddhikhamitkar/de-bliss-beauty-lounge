using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Paging;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the real HTTP pipeline for billing: starting a checkout from a
/// completed appointment, paying it, reprinting the receipt, listing and
/// searching invoices, and the coupon endpoints. The rules are covered by
/// <see cref="Billing.InvoiceServiceTests"/> and <see cref="Billing.CouponServiceTests"/>;
/// these tests catch what the wire adds: query binding, the RFC 9457 problem
/// bodies, and the JSON shape the React app consumes.
/// </summary>
public sealed class BillingApiTests : ApiTestBase
{
    private const string InvoicesUrl = "/api/invoices";
    private const string CouponsUrl = "/api/coupons";

    private static readonly DateTimeOffset VisitTime =
        new(2026, 9, 14, 14, 0, 0, TimeSpan.Zero);

    private static object Checkout(
        string? couponCode = null,
        string? manualDiscountType = null,
        decimal? manualDiscountValue = null,
        string? paymentMethod = "Cash",
        decimal? amount = 160m,
        string? reference = null,
        decimal? tipPercentage = null,
        decimal? tipAmount = null) =>
        new
        {
            couponCode,
            manualDiscountType,
            manualDiscountValue,
            paymentMethod,
            amount,
            reference,
            tipPercentage,
            tipAmount
        };

    /// <summary>A completed visit whose booked prices differ from the catalogue, so the snapshot is provable.</summary>
    private async Task<Guid> SeedCompletedVisitAsync(decimal firstPrice = 100m, decimal secondPrice = 60m)
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var haircutId = await Factory.SeedServiceAsync(categoryId, "Haircut", 120m);
        var beardId = await Factory.SeedServiceAsync(categoryId, "Beard Trim", 40m);
        var customerId = await Factory.SeedCustomerAsync("Layla", "Haddad", phoneNumber: "+971 50 000 0000");

        return await Factory.SeedBillingAppointmentAsync(
            customerId,
            VisitTime,
            lines:
            [
                (haircutId, "Haircut", firstPrice, 0),
                (beardId, "Beard Trim", secondPrice, 1)
            ]);
    }

    private async Task<InvoiceDto> StartInvoiceAsync(Guid appointmentId)
    {
        var response = await Client.PostAsJsonAsync(InvoicesUrl, new { appointmentId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await ReadAsync<InvoiceDto>(response);
    }

    [Fact]
    public async Task Starting_a_checkout_returns_201_with_a_location_then_200_on_repeat()
    {
        var appointmentId = await SeedCompletedVisitAsync();

        var first = await Client.PostAsJsonAsync(InvoicesUrl, new { appointmentId });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var created = await ReadAsync<InvoiceDto>(first);

        Assert.Matches(@"^INV-\d{4}-\d{4}$", created.InvoiceNumber);
        Assert.Equal("Draft", created.Status);
        Assert.Equal("AED", created.Currency);
        Assert.Equal("Layla Haddad", created.CustomerName);
        Assert.Equal(160m, created.SubTotal);
        Assert.Equal(160m, created.TotalAmount);
        Assert.Equal(2, created.Items.Count);
        Assert.Equal("Haircut", created.Items[0].Description);
        Assert.Equal(100m, created.Items[0].UnitPrice);
        Assert.Empty(created.Payments);

        var readBack = await ReadAsync<InvoiceDto>(await Client.GetAsync(LocationPathOf(first)));

        Assert.Equal(created.Id, readBack.Id);

        var repeat = await Client.PostAsJsonAsync(InvoicesUrl, new { appointmentId });

        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);

        var resumed = await ReadAsync<InvoiceDto>(repeat);

        Assert.Equal(created.Id, resumed.Id);
        Assert.Equal(created.InvoiceNumber, resumed.InvoiceNumber);
    }

    [Fact]
    public async Task Starting_a_checkout_requires_a_completed_appointment()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Haircut", 100m);
        var customerId = await Factory.SeedCustomerAsync("Sara", "Osei");
        var openAppointmentId = await Factory.SeedBillingAppointmentAsync(
            customerId,
            VisitTime,
            AppointmentStatus.Confirmed,
            lines: [(serviceId, "Haircut", 100m, 0)]);

        var conflict = await Client.PostAsJsonAsync(InvoicesUrl, new { appointmentId = openAppointmentId });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var problem = await ReadAsync<Problem>(conflict);

        Assert.Contains("completed appointment", problem.Detail);

        var missing = await Client.PostAsJsonAsync(InvoicesUrl, new { appointmentId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Checkout_records_the_payment_and_the_receipt_reprints_the_same_numbers()
    {
        var invoice = await StartInvoiceAsync(await SeedCompletedVisitAsync());

        var response = await Client.PostAsJsonAsync($"{InvoicesUrl}/{invoice.Id}/checkout", Checkout());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var receipt = await ReadAsync<ReceiptDto>(response);

        Assert.Equal("De Bliss Beauty Lounge", receipt.SalonName);
        Assert.Equal(invoice.InvoiceNumber, receipt.InvoiceNumber);
        Assert.Equal("Layla Haddad", receipt.CustomerName);
        Assert.Equal(2, receipt.Items.Count);
        Assert.Equal(160m, receipt.SubTotal);
        Assert.Null(receipt.CouponCode);
        Assert.Equal(0m, receipt.CouponDiscount);
        Assert.Equal(0m, receipt.ManualDiscount);
        Assert.Equal(160m, receipt.TotalAmount);
        Assert.Equal("AED", receipt.Currency);
        Assert.Equal("Cash", receipt.PaymentMethod);
        Assert.Null(receipt.PaymentReference);
        Assert.NotEqual(default, receipt.PaidAt);

        var paid = await ReadAsync<InvoiceDto>(await Client.GetAsync($"{InvoicesUrl}/{invoice.Id}"));

        Assert.Equal("Paid", paid.Status);

        var payment = Assert.Single(paid.Payments);

        Assert.Equal(160m, payment.Amount);
        Assert.Equal("Cash", payment.Method);
        Assert.Equal("Completed", payment.Status);

        var reprint = await ReadAsync<ReceiptDto>(await Client.GetAsync($"{InvoicesUrl}/{invoice.Id}/receipt"));

        Assert.Equal(receipt.InvoiceNumber, reprint.InvoiceNumber);
        Assert.Equal(receipt.TotalAmount, reprint.TotalAmount);
        Assert.Equal(receipt.PaidAt, reprint.PaidAt);
    }

    [Fact]
    public async Task Checkout_with_a_coupon_and_a_manual_discount_keeps_the_discounts_separate()
    {
        await Factory.SeedCouponAsync("BLISS20", DiscountType.Percentage, 20m);

        var invoice = await StartInvoiceAsync(await SeedCompletedVisitAsync());

        var response = await Client.PostAsJsonAsync(
            $"{InvoicesUrl}/{invoice.Id}/checkout",
            Checkout(
                couponCode: " bliss20 ",
                manualDiscountType: "Fixed",
                manualDiscountValue: 8m,
                paymentMethod: "Card",
                amount: 120m,
                reference: "AUTH-77"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var receipt = await ReadAsync<ReceiptDto>(response);

        Assert.Equal("BLISS20", receipt.CouponCode);
        Assert.Equal(32m, receipt.CouponDiscount);
        Assert.Equal(8m, receipt.ManualDiscount);
        Assert.Equal(120m, receipt.TotalAmount);
        Assert.Equal("Card", receipt.PaymentMethod);
        Assert.Equal("AUTH-77", receipt.PaymentReference);

        var paid = await ReadAsync<InvoiceDto>(await Client.GetAsync($"{InvoicesUrl}/{invoice.Id}"));

        Assert.Equal("BLISS20", paid.CouponCode);
        Assert.Equal(32m, paid.CouponDiscount);
        Assert.Equal(8m, paid.ManualDiscount);
        Assert.Equal(40m, paid.DiscountTotal);
        Assert.Equal(120m, paid.TotalAmount);
        Assert.Equal("AUTH-77", Assert.Single(paid.Payments).Reference);
    }

    [Fact]
    public async Task Checkout_reports_missing_payment_details_as_field_errors()
    {
        var invoice = await StartInvoiceAsync(await SeedCompletedVisitAsync());

        var response = await Client.PostAsJsonAsync(
            $"{InvoicesUrl}/{invoice.Id}/checkout",
            Checkout(paymentMethod: null, amount: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains("paymentMethod", problem.Errors.Keys);
        Assert.Contains("amount", problem.Errors.Keys);

        var unchanged = await ReadAsync<InvoiceDto>(await Client.GetAsync($"{InvoicesUrl}/{invoice.Id}"));

        Assert.Equal("Draft", unchanged.Status);
        Assert.Empty(unchanged.Payments);
    }

    [Fact]
    public async Task Checkout_rejects_unknown_and_expired_coupons_with_the_exact_message()
    {
        await Factory.SeedCouponAsync("OLD10", validUntil: new DateOnly(2020, 1, 1));

        var invoice = await StartInvoiceAsync(await SeedCompletedVisitAsync());

        foreach (var code in new[] { "NOPE", "OLD10" })
        {
            var response = await Client.PostAsJsonAsync(
                $"{InvoicesUrl}/{invoice.Id}/checkout",
                Checkout(couponCode: code));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await ReadAsync<ValidationProblem>(response);

            Assert.Equal("Coupon code is invalid or expired.", Assert.Single(problem.Errors["couponCode"]));
        }
    }

    [Fact]
    public async Task Checkout_is_rejected_once_the_invoice_is_paid()
    {
        var invoice = await StartInvoiceAsync(await SeedCompletedVisitAsync());

        await Client.PostAsJsonAsync($"{InvoicesUrl}/{invoice.Id}/checkout", Checkout());

        var again = await Client.PostAsJsonAsync($"{InvoicesUrl}/{invoice.Id}/checkout", Checkout());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var problem = await ReadAsync<Problem>(again);

        Assert.Contains("already been paid", problem.Detail);
    }

    [Fact]
    public async Task The_receipt_is_not_available_until_the_invoice_is_paid()
    {
        var invoice = await StartInvoiceAsync(await SeedCompletedVisitAsync());

        var response = await Client.GetAsync($"{InvoicesUrl}/{invoice.Id}/receipt");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_tip_choice_saved_on_the_draft_survives_a_resumed_checkout()
    {
        var appointmentId = await SeedCompletedVisitAsync();
        var invoice = await StartInvoiceAsync(appointmentId);

        var saved = await Client.PutAsJsonAsync($"{InvoicesUrl}/{invoice.Id}/tip", new { tipPercentage = 10m });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(10m, (await ReadAsync<InvoiceDto>(saved)).TipPercentage);

        // The interrupted checkout resumes on the same bill, tip still picked.
        var resumed = await ReadAsync<InvoiceDto>(
            await Client.PostAsJsonAsync(InvoicesUrl, new { appointmentId }));

        Assert.Equal(invoice.Id, resumed.Id);
        Assert.Equal(10m, resumed.TipPercentage);

        var paid = await Client.PostAsJsonAsync(
            $"{InvoicesUrl}/{invoice.Id}/checkout",
            Checkout(amount: 176m, tipPercentage: 10m));

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        var receipt = await ReadAsync<ReceiptDto>(paid);

        // 10% of the 160 owed at the test salon's zero rate.
        Assert.Equal(16m, receipt.TipAmount);
        Assert.Equal(176m, receipt.TotalAmount);
    }

    [Fact]
    public async Task A_tip_cannot_be_saved_on_an_invoice_that_is_already_paid()
    {
        var invoice = await StartInvoiceAsync(await SeedCompletedVisitAsync());

        await Client.PostAsJsonAsync($"{InvoicesUrl}/{invoice.Id}/checkout", Checkout());

        var refused = await Client.PutAsJsonAsync($"{InvoicesUrl}/{invoice.Id}/tip", new { tipPercentage = 10m });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task A_tip_choice_with_both_a_percentage_and_an_amount_is_a_validation_problem()
    {
        var invoice = await StartInvoiceAsync(await SeedCompletedVisitAsync());

        var response = await Client.PutAsJsonAsync(
            $"{InvoicesUrl}/{invoice.Id}/tip",
            new { tipPercentage = 10m, tipAmount = 5m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains("tipAmount", problem.Errors.Keys);
    }

    [Fact]
    public async Task An_unknown_invoice_reads_back_as_404()
    {
        var response = await Client.GetAsync($"{InvoicesUrl}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_invoice_list_filters_by_status_payment_method_and_search()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Haircut", 100m);
        var laylaId = await Factory.SeedCustomerAsync("Layla", "Haddad", phoneNumber: "+971 50 000 0001");
        var benId = await Factory.SeedCustomerAsync("Ben", "Osei", phoneNumber: "+971 50 000 0002");

        var laylaVisit = await Factory.SeedBillingAppointmentAsync(
            laylaId, VisitTime, lines: [(serviceId, "Haircut", 100m, 0)]);
        var benVisit = await Factory.SeedBillingAppointmentAsync(
            benId, VisitTime.AddHours(2), lines: [(serviceId, "Haircut", 100m, 0)]);

        var laylaInvoice = await StartInvoiceAsync(laylaVisit);
        var benInvoice = await StartInvoiceAsync(benVisit);

        await Client.PostAsJsonAsync($"{InvoicesUrl}/{laylaInvoice.Id}/checkout", Checkout(amount: 100m));

        var all = await ReadAsync<PagedResult<InvoiceSummaryDto>>(await Client.GetAsync(InvoicesUrl));

        Assert.Equal(2, all.TotalCount);

        var drafts = await ReadAsync<PagedResult<InvoiceSummaryDto>>(
            await Client.GetAsync($"{InvoicesUrl}?status=Draft"));

        Assert.Equal(benInvoice.Id, Assert.Single(drafts.Items).Id);

        var paid = await ReadAsync<PagedResult<InvoiceSummaryDto>>(
            await Client.GetAsync($"{InvoicesUrl}?status=Paid"));

        var paidRow = Assert.Single(paid.Items);

        Assert.Equal(laylaInvoice.Id, paidRow.Id);
        Assert.Equal("Cash", paidRow.PaymentMethod);
        Assert.Equal(100m, paidRow.TotalAmount);

        var cash = await ReadAsync<PagedResult<InvoiceSummaryDto>>(
            await Client.GetAsync($"{InvoicesUrl}?paymentMethod=Cash"));

        Assert.Equal(laylaInvoice.Id, Assert.Single(cash.Items).Id);

        var card = await ReadAsync<PagedResult<InvoiceSummaryDto>>(
            await Client.GetAsync($"{InvoicesUrl}?paymentMethod=Card"));

        Assert.Empty(card.Items);

        var byName = await ReadAsync<PagedResult<InvoiceSummaryDto>>(
            await Client.GetAsync($"{InvoicesUrl}?search=osei"));

        Assert.Equal(benInvoice.Id, Assert.Single(byName.Items).Id);

        var byNumber = await ReadAsync<PagedResult<InvoiceSummaryDto>>(
            await Client.GetAsync($"{InvoicesUrl}?search={laylaInvoice.InvoiceNumber}"));

        Assert.Equal(laylaInvoice.Id, Assert.Single(byNumber.Items).Id);

        var byCustomer = await ReadAsync<PagedResult<InvoiceSummaryDto>>(
            await Client.GetAsync($"{InvoicesUrl}?customerId={laylaId}"));

        Assert.Equal(laylaInvoice.Id, Assert.Single(byCustomer.Items).Id);
    }

    [Fact]
    public async Task Creating_a_coupon_returns_201_and_normalises_the_code()
    {
        var response = await Client.PostAsJsonAsync(CouponsUrl, new
        {
            code = " bliss10 ",
            discountType = "Percentage",
            discountValue = 10m,
            validFrom = "2026-01-01",
            validUntil = "2026-12-31",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<CouponDto>(response);

        Assert.Equal("BLISS10", created.Code);
        Assert.Equal("Percentage", created.DiscountType);
        Assert.Equal(10m, created.DiscountValue);
        Assert.True(created.IsActive);
        Assert.Equal(new DateOnly(2026, 1, 1), created.ValidFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), created.ValidUntil);

        var readBack = await ReadAsync<CouponDto>(await Client.GetAsync(LocationPathOf(response)));

        Assert.Equal(created.Id, readBack.Id);

        var duplicate = await Client.PostAsJsonAsync(CouponsUrl, new
        {
            code = "BLISS10",
            discountType = "Fixed",
            discountValue = 5m
        });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var problem = await ReadAsync<Problem>(duplicate);

        Assert.Contains("BLISS10", problem.Detail);
    }

    [Fact]
    public async Task The_coupon_list_filters_by_search_and_status()
    {
        await Factory.SeedCouponAsync("BLISS10", DiscountType.Fixed, 10m);
        await Factory.SeedCouponAsync("SUMMER5", DiscountType.Percentage, 5m, isActive: false);

        var all = await ReadAsync<PagedResult<CouponDto>>(await Client.GetAsync(CouponsUrl));

        Assert.Equal(2, all.TotalCount);

        var inactive = await ReadAsync<PagedResult<CouponDto>>(
            await Client.GetAsync($"{CouponsUrl}?status=Inactive"));

        Assert.Equal("SUMMER5", Assert.Single(inactive.Items).Code);

        var search = await ReadAsync<PagedResult<CouponDto>>(
            await Client.GetAsync($"{CouponsUrl}?search=bliss"));

        Assert.Equal("BLISS10", Assert.Single(search.Items).Code);
    }

    [Fact]
    public async Task Editing_a_coupon_updates_the_form_and_the_status_toggles()
    {
        var couponId = await Factory.SeedCouponAsync("BLISS10", DiscountType.Fixed, 10m);

        var update = await Client.PutAsJsonAsync($"{CouponsUrl}/{couponId}", new
        {
            code = "BLISS25",
            discountType = "Fixed",
            discountValue = 25m,
            validFrom = (string?)null,
            validUntil = (string?)null,
            isActive = true
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var updated = await ReadAsync<CouponDto>(update);

        Assert.Equal("BLISS25", updated.Code);
        Assert.Equal(25m, updated.DiscountValue);

        var deactivate = await Client.PatchAsJsonAsync($"{CouponsUrl}/{couponId}/status", new { isActive = false });

        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.False((await ReadAsync<CouponDto>(deactivate)).IsActive);

        var readBack = await ReadAsync<CouponDto>(await Client.GetAsync($"{CouponsUrl}/{couponId}"));

        Assert.False(readBack.IsActive);

        var unknown = await Client.GetAsync($"{CouponsUrl}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Validating_a_coupon_previews_the_discount_for_the_checkout_screen()
    {
        await Factory.SeedCouponAsync("BLISS50", DiscountType.Fixed, 50m);
        await Factory.SeedCouponAsync("SUMMER5", DiscountType.Percentage, 5m, isActive: false);
        await Factory.SeedCouponAsync("OLD10", validUntil: new DateOnly(2020, 1, 1));

        var valid = await Client.PostAsJsonAsync($"{CouponsUrl}/validate", new { code = " blisS50 ", subtotal = 160m });

        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);

        var preview = await ReadAsync<CouponValidationDto>(valid);

        Assert.Equal("BLISS50", preview.Code);
        Assert.Equal("Fixed", preview.DiscountType);
        Assert.Equal(50m, preview.DiscountValue);
        Assert.Equal(50m, preview.DiscountAmount);

        foreach (var code in new[] { "NOPE", "SUMMER5", "OLD10" })
        {
            var response = await Client.PostAsJsonAsync($"{CouponsUrl}/validate", new { code, subtotal = 160m });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await ReadAsync<ValidationProblem>(response);

            Assert.Equal("Coupon code is invalid or expired.", Assert.Single(problem.Errors["code"]));
        }
    }
}
