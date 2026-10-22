using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Dashboard;
using DeBliss.Application.Dashboard.Dtos;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;

namespace DeBliss.UnitTests.Dashboard;

/// <summary>
/// The owner dashboard's financial definitions and window boundaries, proven
/// against rows written directly into a real database. The clock is fixed at
/// 2026-10-03 09:00 UTC — 13:00 Dubai — so "today", "yesterday", "month to
/// date" and the previous-month comparison are all exact dates, not guesses
/// about when the test runs.
/// </summary>
public sealed class OwnerDashboardServiceTests : IDisposable
{
    private readonly OwnerDashboardHarness _harness = new();

    private OwnerDashboardHarness Harness => _harness;

    private Task<OwnerDashboardDto> GetAsync(OwnerDashboardRange range) =>
        Harness.Dashboard.GetAsync(new OwnerDashboardRangeRequest(range, null, null));

    [Fact]
    public async Task Net_revenue_excludes_vat_and_tips_and_splits_service_from_retail()
    {
        var category = await Harness.SeedCategoryAsync("Hair");
        var haircut = await Harness.SeedServiceAsync(category, "Haircut");
        var shampoo = await Harness.SeedProductAsync("Shampoo", 10, 5);

        // Payable 255 = 250 lines − 20 discount + 10 VAT + 15 tip; the salon
        // keeps 230 of it, and VAT and the tip are the stylist's/customer's.
        await Harness.SeedPaidInvoiceAsync(
            "INV-100",
            Harness.Local(Harness.Today, 11),
            [
                new(InvoiceItemType.Service, "Haircut", 200m, ServiceId: haircut),
                new(InvoiceItemType.Product, "Shampoo", 50m, ProductId: shampoo),
            ],
            discountTotal: 20m,
            taxTotal: 10m,
            tipAmount: 15m);

        var dto = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal(230m, dto.Kpis.NetRevenueToday);
        Assert.Equal(230m, dto.Revenue.NetRevenue);
        Assert.Equal(200m, dto.Kpis.ServiceRevenue);
        Assert.Equal(50m, dto.Kpis.RetailRevenue);
        Assert.Equal(10m, dto.Revenue.VatTotal);
        Assert.Equal(15m, dto.Revenue.TipTotal);
        Assert.Equal(20m, dto.Revenue.DiscountTotal);
        Assert.Equal(255m, dto.Revenue.Collected);
        Assert.Equal(1, dto.Kpis.PaidInvoiceCount);
        Assert.Equal(230m, dto.Revenue.AverageTransactionValue);
    }

    [Fact]
    public async Task Day_boundaries_follow_the_salon_time_zone_not_utc()
    {
        // 2026-10-02 20:00 UTC is 2026-10-03 00:00 Dubai — the first instant
        // of today. It belongs to today, not to yesterday.
        await Harness.SeedPaidInvoiceAsync(
            "INV-1", new DateTimeOffset(2026, 10, 2, 20, 0, 0, TimeSpan.Zero),
            [new(InvoiceItemType.Service, "Cut", 100m)]);

        // 2026-10-03 20:00 UTC is 2026-10-04 00:00 Dubai — one tick past the
        // last instant of today, so it belongs to no window the dashboard sums.
        await Harness.SeedPaidInvoiceAsync(
            "INV-2", new DateTimeOffset(2026, 10, 3, 20, 0, 0, TimeSpan.Zero),
            [new(InvoiceItemType.Service, "Cut", 500m)]);

        await Harness.SeedPaidInvoiceAsync(
            "INV-3", Harness.Local(Harness.Today.AddDays(-1), 12),
            [new(InvoiceItemType.Service, "Cut", 70m)]);

        var dto = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal(100m, dto.Kpis.NetRevenueToday);
        Assert.Equal(70m, dto.Kpis.NetRevenueYesterday);
        Assert.Equal(170m, dto.Kpis.NetRevenueMonthToDate);
    }

    [Fact]
    public async Task Month_to_date_compares_with_the_same_calendar_days_of_last_month()
    {
        // Today is Oct 3, so month-to-date is Oct 1–3 and the like-for-like
        // previous period is Sep 1–4 — Sep 5 is already out of the window.
        await Harness.SeedPaidInvoiceAsync("INV-1", Harness.Local(Harness.Today, 10),
            [new(InvoiceItemType.Service, "Cut", 120m)]);
        await Harness.SeedPaidInvoiceAsync("INV-2", Harness.Local(Harness.Today.AddDays(-1), 10),
            [new(InvoiceItemType.Service, "Cut", 80m)]);
        await Harness.SeedPaidInvoiceAsync("INV-3", Harness.Local(new DateOnly(2026, 9, 2), 10),
            [new(InvoiceItemType.Service, "Cut", 400m)]);
        await Harness.SeedPaidInvoiceAsync("INV-4", Harness.Local(new DateOnly(2026, 9, 5), 10),
            [new(InvoiceItemType.Service, "Cut", 999m)]);

        var dto = await GetAsync(OwnerDashboardRange.ThisMonth);

        Assert.Equal(200m, dto.Revenue.NetRevenue);
        Assert.Equal(400m, dto.Revenue.PreviousNetRevenue);
        Assert.Equal(-50m, dto.Revenue.ChangePercent);
    }

    [Fact]
    public async Task Drafts_and_unpaid_bills_are_counted_never_summed()
    {
        await Harness.SeedPaidInvoiceAsync("INV-1", Harness.Local(Harness.Today, 10),
            [new(InvoiceItemType.Service, "Cut", 100m)]);

        // Drafts carry totals that predate VAT, coupon and tip — money that
        // was never agreed — so they are counted, never added up.
        await Harness.SeedInvoiceAsync(
            "INV-D", InvoiceStatus.Draft, Harness.Local(Harness.Today, 9),
            subTotal: 500m, totalAmount: 500m);

        await Harness.SeedInvoiceAsync(
            "INV-I", InvoiceStatus.Issued, Harness.Local(Harness.Today, 10),
            subTotal: 300m, totalAmount: 300m);

        await Harness.SeedInvoiceAsync(
            "INV-P", InvoiceStatus.PartiallyPaid, Harness.Local(Harness.Today, 11),
            subTotal: 300m, totalAmount: 300m,
            payments: [(PaymentStatus.Completed, 100m, Harness.Local(Harness.Today, 11))]);

        var dto = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal(100m, dto.Kpis.NetRevenueToday);
        Assert.Equal(500m, dto.Kpis.OutstandingAmount);
        Assert.Equal(2, dto.Kpis.OutstandingInvoiceCount);

        Assert.Contains(dto.Actions, action =>
            action.Message.Contains("draft", StringComparison.Ordinal) && action.Count == 1);
        Assert.Contains(dto.Actions, action =>
            action.Message.Contains("left unpaid", StringComparison.Ordinal) && action.Count == 2);
    }

    [Fact]
    public async Task Voided_invoices_and_refunded_payments_never_become_revenue()
    {
        // A voided bill and a failed tender are noise; the refund claws money
        // back out of collected but there is no invoice-reversal flow yet, so
        // net revenue keeps the settled amount.
        await Harness.SeedInvoiceAsync(
            "INV-1", InvoiceStatus.Paid, Harness.Local(Harness.Today, 10),
            subTotal: 100m, totalAmount: 100m,
            payments: [(PaymentStatus.Completed, 100m, Harness.Local(Harness.Today, 10)),
                       (PaymentStatus.Failed, 25m, Harness.Local(Harness.Today, 10))]);

        await Harness.SeedInvoiceAsync(
            "INV-V", InvoiceStatus.Void, Harness.Local(Harness.Today, 10),
            subTotal: 999m, totalAmount: 999m);

        await Harness.SeedInvoiceAsync(
            "INV-2", InvoiceStatus.Paid, Harness.Local(Harness.Today, 12),
            subTotal: 50m, totalAmount: 50m,
            payments: [(PaymentStatus.Completed, 50m, Harness.Local(Harness.Today, 12)),
                       (PaymentStatus.Refunded, 40m, Harness.Local(Harness.Today, 13))]);

        var dto = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal(150m, dto.Kpis.NetRevenueToday);
        Assert.Equal(2, dto.Kpis.PaidInvoiceCount);
        Assert.Equal(110m, dto.Revenue.Collected);
    }

    [Fact]
    public async Task Multiple_payments_against_one_invoice_are_counted_once()
    {
        await Harness.SeedInvoiceAsync(
            "INV-1", InvoiceStatus.Paid, Harness.Local(Harness.Today, 10),
            subTotal: 250m, totalAmount: 250m,
            payments: [(PaymentStatus.Completed, 150m, Harness.Local(Harness.Today, 10)),
                       (PaymentStatus.Completed, 100m, Harness.Local(Harness.Today, 14))]);

        var dto = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal(250m, dto.Kpis.NetRevenueToday);
        Assert.Equal(1, dto.Kpis.PaidInvoiceCount);
        Assert.Equal(250m, dto.Revenue.Collected);
        Assert.Equal(250m, dto.Revenue.AverageTransactionValue);
    }

    [Fact]
    public async Task Retail_sale_without_an_appointment_still_counts()
    {
        var shampoo = await Harness.SeedProductAsync("Shampoo", 10, 5);

        await Harness.SeedPaidInvoiceAsync(
            "INV-1", Harness.Local(Harness.Today, 10),
            [new(InvoiceItemType.Product, "Shampoo", 80m, ProductId: shampoo)]);

        var dto = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal(80m, dto.Kpis.RetailRevenue);
        Assert.Equal(0m, dto.Kpis.ServiceRevenue);
        Assert.Equal(80m, dto.Kpis.NetRevenueToday);
        Assert.Equal(0, dto.Kpis.CompletedAppointmentsToday);
    }

    [Fact]
    public async Task Average_transaction_value_is_net_per_paid_invoice()
    {
        var category = await Harness.SeedCategoryAsync("Hair");
        var haircut = await Harness.SeedServiceAsync(category, "Haircut");

        await Harness.SeedPaidInvoiceAsync(
            "INV-1", Harness.Local(Harness.Today, 10),
            [new(InvoiceItemType.Service, "Haircut", 200m, ServiceId: haircut)],
            discountTotal: 20m, taxTotal: 10m, tipAmount: 15m);
        await Harness.SeedPaidInvoiceAsync(
            "INV-2", Harness.Local(Harness.Today, 12),
            [new(InvoiceItemType.Service, "Haircut", 100m, ServiceId: haircut)]);

        var dto = await GetAsync(OwnerDashboardRange.Today);

        // First visit nets 180 (200 − 20 discount; VAT and tip excluded),
        // second nets 100 — so the average net is 140, not the payables' mean.
        Assert.Equal(140m, dto.Kpis.AverageTransactionValue);
        Assert.Equal(2, dto.Kpis.PaidInvoiceCount);
    }

    [Fact]
    public async Task Inventory_alerts_rank_out_of_stock_first_and_skip_inactive()
    {
        await Harness.SeedProductAsync("Alpha Wax", 0m, 5m);
        await Harness.SeedProductAsync("Beta Oil", 3m, 5m);
        await Harness.SeedProductAsync("Gamma Mask", 10m, 5m);
        await Harness.SeedProductAsync("Delta Retired", 0m, 5m, isActive: false);

        var dto = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal(1, dto.Inventory.OutOfStockCount);
        Assert.Equal(1, dto.Inventory.LowStockCount);

        var items = dto.Inventory.Items;

        Assert.Equal(2, items.Count);
        Assert.Equal("Alpha Wax", items[0].Name);
        Assert.Equal("OutOfStock", items[0].Status);
        Assert.Equal("Beta Oil", items[1].Name);
        Assert.Equal("LowStock", items[1].Status);

        Assert.Contains(dto.Actions, action =>
            action.Link == "/inventory" && action.Count == 2);
    }

    [Fact]
    public async Task An_empty_salon_reports_zeroes_and_empty_lists()
    {
        var dto = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal(0m, dto.Kpis.NetRevenueToday);
        Assert.Equal(0m, dto.Kpis.NetRevenueMonthToDate);
        Assert.Equal(0m, dto.Kpis.OutstandingAmount);
        Assert.Null(dto.Revenue.ChangePercent);
        Assert.Equal("Today", dto.Revenue.RangeLabel);
        Assert.Equal("Asia/Dubai", dto.TimeZoneId);

        // The range is one day, so the daily trend carries one zero-filled point.
        var today = Assert.Single(dto.Revenue.Daily);
        Assert.Equal(0m, today.NetRevenue);

        Assert.Equal(6, dto.Revenue.Monthly.Count);
        Assert.All(dto.Revenue.Monthly, point => Assert.Equal(0m, point.NetRevenue));
        Assert.Empty(dto.Revenue.TopServices);
        Assert.Empty(dto.Revenue.Categories);
        Assert.Empty(dto.Revenue.RetailProducts);
        Assert.Empty(dto.Inventory.Items);
        Assert.Empty(dto.Actions);
    }

    [Fact]
    public async Task Completed_visits_await_checkout_until_paid()
    {
        var category = await Harness.SeedCategoryAsync("Hair");
        var cut = await Harness.SeedServiceAsync(category, "Cut");
        var staff = await Harness.SeedStaffAsync();
        var appointment = await Harness.SeedAppointmentAsync(
            Harness.Local(Harness.Today, 10), AppointmentStatus.Completed, staff, cut);

        var before = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal(1, before.Kpis.CompletedAppointmentsToday);
        Assert.Equal(1, before.Operations.AwaitingCheckout);

        await Harness.SeedPaidInvoiceAsync(
            "INV-1", Harness.Local(Harness.Today, 11),
            [new(InvoiceItemType.Service, "Cut", 100m, ServiceId: cut)],
            appointmentId: appointment);

        var after = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal(0, after.Operations.AwaitingCheckout);
        Assert.Equal(100m, after.Kpis.NetRevenueToday);
    }

    [Fact]
    public async Task Overlapping_visits_conflict_and_cancelled_ones_free_their_slot()
    {
        var category = await Harness.SeedCategoryAsync("Hair");
        var cut = await Harness.SeedServiceAsync(category, "Cut");
        var sara = await Harness.SeedStaffAsync("Sara");
        var dana = await Harness.SeedStaffAsync("Dana");

        await Harness.SeedAppointmentAsync(Harness.Local(Harness.Today, 10), AppointmentStatus.Completed, sara, cut);
        await Harness.SeedAppointmentAsync(Harness.Local(Harness.Today, 10, 30), AppointmentStatus.Confirmed, sara, cut);

        // Dana's second visit is cancelled, so her overlap is not a conflict.
        await Harness.SeedAppointmentAsync(Harness.Local(Harness.Today, 10), AppointmentStatus.Completed, dana, cut);
        await Harness.SeedAppointmentAsync(Harness.Local(Harness.Today, 10, 30), AppointmentStatus.Cancelled, dana, cut);

        var dto = await GetAsync(OwnerDashboardRange.Today);

        var conflict = Assert.Single(dto.Operations.Conflicts);
        Assert.Equal(sara, conflict.StaffId);
        Assert.Equal(2, conflict.AppointmentCount);
    }

    [Fact]
    public async Task Custom_ranges_are_validated()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Harness.Dashboard.GetAsync(new OwnerDashboardRangeRequest(OwnerDashboardRange.Custom, null, null)));

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Harness.Dashboard.GetAsync(new OwnerDashboardRangeRequest(
                OwnerDashboardRange.Custom, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 1))));

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Harness.Dashboard.GetAsync(new OwnerDashboardRangeRequest(
                OwnerDashboardRange.Custom, new DateOnly(2026, 1, 1), new DateOnly(2027, 6, 1))));

        var dto = await Harness.Dashboard.GetAsync(new OwnerDashboardRangeRequest(
            OwnerDashboardRange.Custom, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));

        Assert.Equal(30, dto.Revenue.Daily.Count);
        Assert.Contains("2026", dto.Revenue.RangeLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Daily_and_monthly_series_count_by_salon_day()
    {
        var category = await Harness.SeedCategoryAsync("Hair");
        var cut = await Harness.SeedServiceAsync(category, "Cut");
        var staff = await Harness.SeedStaffAsync();

        await Harness.SeedPaidInvoiceAsync("INV-1", Harness.Local(Harness.Today.AddDays(-1), 10),
            [new(InvoiceItemType.Service, "Cut", 100m, ServiceId: cut)]);
        await Harness.SeedPaidInvoiceAsync("INV-2", Harness.Local(new DateOnly(2026, 9, 15), 10),
            [new(InvoiceItemType.Service, "Cut", 700m, ServiceId: cut)]);

        await Harness.SeedAppointmentAsync(
            Harness.Local(Harness.Today.AddDays(-1), 11), AppointmentStatus.Completed, staff, cut);
        await Harness.SeedAppointmentAsync(
            Harness.Local(Harness.Today, 11), AppointmentStatus.Cancelled, staff, cut);
        await Harness.SeedAppointmentAsync(
            Harness.Local(Harness.Today, 12), AppointmentStatus.Completed, staff, cut);

        var dto = await GetAsync(OwnerDashboardRange.ThisMonth);

        var daily = dto.Revenue.Daily;

        Assert.Equal(3, daily.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), daily[0].Date);
        Assert.Equal(0m, daily[0].NetRevenue);
        Assert.Equal(100m, daily[1].NetRevenue);
        Assert.Equal(1, daily[1].Completed);
        Assert.Equal(0, daily[1].Cancelled);
        Assert.Equal(0m, daily[2].NetRevenue);
        Assert.Equal(1, daily[2].Completed);
        Assert.Equal(1, daily[2].Cancelled);

        var monthly = dto.Revenue.Monthly;

        Assert.Equal(new DateOnly(2026, 5, 1), monthly[0].MonthStart);
        Assert.Equal(new DateOnly(2026, 10, 1), monthly[^1].MonthStart);
        Assert.Equal(700m, monthly[4].NetRevenue);
        Assert.Equal(100m, monthly[5].NetRevenue);
    }

    [Fact]
    public async Task Top_services_and_categories_group_through_the_catalogue()
    {
        var hair = await Harness.SeedCategoryAsync("Hair");
        var cut = await Harness.SeedServiceAsync(hair, "Cut");
        var blow = await Harness.SeedServiceAsync(hair, "Blow dry");

        // The third line's service was deleted since checkout; its revenue
        // still reports, as uncategorised, because history must not vanish.
        await Harness.SeedPaidInvoiceAsync(
            "INV-1", Harness.Local(Harness.Today, 10),
            [
                new(InvoiceItemType.Service, "Cut", 200m, ServiceId: cut),
                new(InvoiceItemType.Service, "Blow dry", 100m, ServiceId: blow),
                new(InvoiceItemType.Service, "Removed treatment", 50m),
            ]);

        var dto = await GetAsync(OwnerDashboardRange.Today);

        Assert.Equal("Cut", dto.Revenue.TopServices[0].Name);
        Assert.Equal(200m, dto.Revenue.TopServices[0].Revenue);
        Assert.Equal(1, dto.Revenue.TopServices[0].Count);

        Assert.Equal("Hair", dto.Revenue.Categories[0].Name);
        Assert.Equal(300m, dto.Revenue.Categories[0].Revenue);
        Assert.Equal(2, dto.Revenue.Categories[0].Count);
        Assert.Equal("Uncategorised", dto.Revenue.Categories[1].Name);
        Assert.Equal(50m, dto.Revenue.Categories[1].Revenue);
    }

    public void Dispose() => _harness.Dispose();
}
