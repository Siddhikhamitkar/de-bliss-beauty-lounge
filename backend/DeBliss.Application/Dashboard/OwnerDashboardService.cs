using DeBliss.Application.Billing;
using DeBliss.Application.Dashboard.Dtos;
using DeBliss.Application.Dashboard.Ports;
using DeBliss.Application.Scheduling;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Dashboard;

/// <summary>
/// Composes the owner's business snapshot. The operations half is the existing
/// receptionist snapshot reused verbatim — the same floor view, now alongside
/// money the front desk never sees. Every financial figure follows the
/// definitions documented on <see cref="OwnerDashboardDto"/>; this service
/// only windows and sums, it never invents a number.
/// </summary>
public sealed class OwnerDashboardService(
    IReceptionistDashboardService receptionistDashboard,
    IOwnerDashboardReadStore store,
    ISalonTimeZone salonTimeZone,
    TimeProvider timeProvider) : IOwnerDashboardService
{
    private const string Uncategorised = "Uncategorised";
    private const int TopListSize = 6;
    private const int InventoryListSize = 8;
    private const int TrendMonths = 6;

    public async Task<OwnerDashboardDto> GetAsync(
        OwnerDashboardRangeRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();

        var zone = salonTimeZone.Zone;
        var nowLocal = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var today = DateOnly.FromDateTime(nowLocal.Date);

        var range = ResolveRange(request.Range, request.From, request.To, today, zone);
        var todayWindow = DayWindow(today, zone);
        var yesterdayWindow = DayWindow(today.AddDays(-1), zone);

        var monthFirst = new DateOnly(today.Year, today.Month, 1);
        var monthWindow = (Start: At(monthFirst, zone), End: At(monthFirst.AddMonths(1), zone));
        var daysIntoMonth = today.DayNumber - monthFirst.DayNumber + 1;
        var prevMonthFirst = monthFirst.AddMonths(-1);
        var prevMonthSameWindow = (Start: At(prevMonthFirst, zone), End: At(prevMonthFirst.AddDays(daysIntoMonth), zone));

        // One billing read covers every window the dashboard sums: the trend's
        // six months and the comparison windows reach further back than the
        // selected range alone.
        var dataFrom = Min(At(monthFirst.AddMonths(1 - TrendMonths), zone), range.Start, yesterdayWindow.Start, prevMonthSameWindow.Start);
        var dataTo = Max(range.End, todayWindow.End);

        var invoices = await store.GetPaidInvoicesAsync(dataFrom, dataTo, cancellationToken);
        var payments = await store.GetPaymentsAsync(dataFrom, dataTo, cancellationToken);
        var openBills = await store.GetOpenBillsAsync(cancellationToken);
        var paidAppointmentIds = await store.GetAppointmentIdsWithPaidInvoiceAsync(cancellationToken);
        var products = await store.GetProductStockAsync(cancellationToken);

        var outcomesFrom = Min(range.Start, todayWindow.Start);
        var outcomesTo = Max(range.End, todayWindow.End);
        var outcomes = await store.GetAppointmentOutcomesAsync(outcomesFrom, outcomesTo, cancellationToken);
        var blocks = await store.GetStaffBlocksAsync(todayWindow.Start, todayWindow.End, cancellationToken);

        var floor = await receptionistDashboard.GetAsync(cancellationToken);

        // ---- fixed KPI cards: the day and the month, each against its own
        // previous equivalent. ----
        var todayTotals = SumInvoices(invoices, todayWindow.Start, todayWindow.End);
        var yesterdayTotals = SumInvoices(invoices, yesterdayWindow.Start, yesterdayWindow.End);
        var monthTotals = SumInvoices(invoices, monthWindow.Start, monthWindow.End);
        var prevMonthTotals = SumInvoices(invoices, prevMonthSameWindow.Start, prevMonthSameWindow.End);

        // ---- the selected window drives trends, the split and the averages. ----
        var rangeTotals = SumInvoices(invoices, range.Start, range.End);
        var rangeCollected = SumPayments(payments, range.Start, range.End);
        var rangeOutcomes = outcomes
            .Where(appointment => appointment.StartsAt >= range.Start && appointment.StartsAt < range.End)
            .ToList();

        var currency = BillingLimits.CurrencyCode;

        var kpis = new OwnerDashboardKpisDto(
            currency,
            todayTotals.Net,
            yesterdayTotals.Net,
            monthTotals.Net,
            prevMonthTotals.Net,
            floor.Today.Completed,
            floor.Today.Upcoming,
            AverageOf(rangeTotals.Net, rangeTotals.Count),
            rangeTotals.Count,
            rangeTotals.Retail,
            rangeTotals.Service,
            openBills.IssuedUnpaidOutstanding,
            openBills.IssuedUnpaidCount);

        var operations = new OwnerDashboardOperationsDto(
            floor.Today,
            floor.Schedule,
            floor.Upcoming,
            floor.Staff,
            floor.Schedule.Count(row => row.CanCheckout && !paidAppointmentIds.Contains(row.Id)),
            FindConflicts(blocks, floor.Staff));

        var revenue = new OwnerDashboardRevenueDto(
            range.Label,
            range.From,
            range.To,
            rangeTotals.Net,
            PreviousOf(range, invoices, prevMonthSameWindow),
            ChangePercentOf(rangeTotals.Net, PreviousOf(range, invoices, prevMonthSameWindow)),
            rangeTotals.Service,
            rangeTotals.Retail,
            rangeTotals.Vat,
            rangeTotals.Tip,
            rangeTotals.Discount,
            rangeCollected,
            rangeTotals.Count,
            AverageOf(rangeTotals.Net, rangeTotals.Count),
            DailySeries(range, invoices, rangeOutcomes, zone),
            MonthlySeries(invoices, monthFirst, zone),
            NamedRevenue(rangeTotals.ServiceLines, countIsUnits: false),
            await CategoryRevenueAsync(rangeTotals.ServiceLines, cancellationToken),
            NamedRevenue(rangeTotals.ProductLines, countIsUnits: true));

        var inventory = BuildInventory(products);

        return new OwnerDashboardDto(
            timeProvider.GetUtcNow(),
            zone.Id,
            kpis,
            operations,
            revenue,
            inventory,
            BuildActions(operations, openBills, inventory));
    }

    private sealed record RangeWindow(DateOnly From, DateOnly To, DateTimeOffset Start, DateTimeOffset End, string Label);

    private static DateTimeOffset At(DateOnly date, TimeZoneInfo zone)
    {
        var localMidnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(localMidnight, zone.GetUtcOffset(localMidnight));
    }

    private static RangeWindow DayWindow(DateOnly date, TimeZoneInfo zone) =>
        new(date, date, At(date, zone), At(date.AddDays(1), zone), "Today");

    private static RangeWindow ResolveRange(
        OwnerDashboardRange range,
        DateOnly? from,
        DateOnly? to,
        DateOnly today,
        TimeZoneInfo zone)
    {
        return range switch
        {
            OwnerDashboardRange.Yesterday => Shift(DayWindow(today.AddDays(-1), zone), "Yesterday"),
            OwnerDashboardRange.Last7Days => Window(today.AddDays(-6), today, "Last 7 days"),
            OwnerDashboardRange.ThisMonth => Window(new DateOnly(today.Year, today.Month, 1), today, "This month"),
            OwnerDashboardRange.LastMonth => Window(
                new DateOnly(today.Year, today.Month, 1).AddMonths(-1),
                new DateOnly(today.Year, today.Month, 1).AddDays(-1),
                "Last month"),
            OwnerDashboardRange.Custom => Window(from!.Value, to!.Value, $"{from:MMM d} – {to:MMM d, yyyy}"),
            _ => DayWindow(today, zone)
        };

        RangeWindow Window(DateOnly first, DateOnly last, string label) =>
            new(first, last, At(first, zone), At(last.AddDays(1), zone), label);

        // A single-day window built by DayWindow is labelled "Today"; the
        // yesterday and custom paths relabel without recomputing the instants.
        static RangeWindow Shift(RangeWindow window, string label) =>
            window with { Label = label };
    }

    /// <summary>
    /// The window to compare against: the month compares with the same calendar
    /// days of the previous month (month-to-date against month-to-date), every
    /// other window with the equally long stretch immediately before it.
    /// </summary>
    private static decimal PreviousOf(
        RangeWindow range,
        IReadOnlyList<OwnerInvoiceSnapshot> invoices,
        (DateTimeOffset Start, DateTimeOffset End) prevMonthSameWindow)
    {
        if (range.Label == "This month")
        {
            return SumInvoices(invoices, prevMonthSameWindow.Start, prevMonthSameWindow.End).Net;
        }

        var length = range.To.DayNumber - range.From.DayNumber + 1;

        return SumInvoices(invoices, range.Start.AddDays(-length), range.Start).Net;
    }

    private static (decimal Net, decimal Service, decimal Retail, decimal Vat, decimal Tip, decimal Discount, int Count,
        IReadOnlyList<OwnerInvoiceLineSnapshot> ServiceLines, IReadOnlyList<OwnerInvoiceLineSnapshot> ProductLines)
        SumInvoices(IReadOnlyList<OwnerInvoiceSnapshot> invoices, DateTimeOffset from, DateTimeOffset to)
    {
        decimal net = 0m, service = 0m, retail = 0m, vat = 0m, tip = 0m, discount = 0m;
        var count = 0;
        List<OwnerInvoiceLineSnapshot>? serviceLines = null;
        List<OwnerInvoiceLineSnapshot>? productLines = null;

        foreach (var invoice in invoices)
        {
            if (invoice.IssuedAt < from || invoice.IssuedAt >= to)
            {
                continue;
            }

            count++;
            // Both pricing conventions remove VAT the same way: TaxTotal is the
            // VAT charged on top or the VAT embedded in TotalAmount. Tips never
            // belong to the salon.
            net += invoice.TotalAmount - invoice.TaxTotal - invoice.TipAmount;
            vat += invoice.TaxTotal;
            tip += invoice.TipAmount;
            discount += invoice.DiscountTotal;

            foreach (var line in invoice.Lines)
            {
                if (line.ItemType == InvoiceItemType.Service)
                {
                    service += line.LineTotal;
                    (serviceLines ??= []).Add(line);
                }
                else if (line.ItemType == InvoiceItemType.Product)
                {
                    retail += line.LineTotal;
                    (productLines ??= []).Add(line);
                }
            }
        }

        return (net, service, retail, vat, tip, discount, count,
            serviceLines ?? (IReadOnlyList<OwnerInvoiceLineSnapshot>)[],
            productLines ?? (IReadOnlyList<OwnerInvoiceLineSnapshot>)[]);
    }

    private static decimal SumPayments(IReadOnlyList<OwnerPaymentSnapshot> payments, DateTimeOffset from, DateTimeOffset to)
    {
        decimal collected = 0m;

        foreach (var payment in payments)
        {
            if (payment.PaidAt >= from && payment.PaidAt < to)
            {
                collected += payment.Status == PaymentStatus.Completed ? payment.Amount
                    : payment.Status == PaymentStatus.Refunded ? -payment.Amount
                    : 0m;
            }
        }

        return collected;
    }

    private static decimal AverageOf(decimal net, int count) =>
        count == 0 ? 0m : Math.Round(net / count, 2, MidpointRounding.AwayFromZero);

    private static decimal? ChangePercentOf(decimal current, decimal previous) =>
        previous == 0m ? null : Math.Round((current - previous) / previous * 100m, 1, MidpointRounding.AwayFromZero);

    private static IReadOnlyList<OwnerDailyPointDto> DailySeries(
        RangeWindow range,
        IReadOnlyList<OwnerInvoiceSnapshot> invoices,
        IReadOnlyList<OwnerAppointmentOutcomeSnapshot> outcomes,
        TimeZoneInfo zone)
    {
        var points = new List<OwnerDailyPointDto>();

        for (var day = range.From; day <= range.To; day = day.AddDays(1))
        {
            var start = At(day, zone);
            var end = At(day.AddDays(1), zone);
            decimal net = 0m;
            var completed = 0;
            var cancelled = 0;

            foreach (var invoice in invoices)
            {
                if (invoice.IssuedAt >= start && invoice.IssuedAt < end)
                {
                    net += invoice.TotalAmount - invoice.TaxTotal - invoice.TipAmount;
                }
            }

            foreach (var appointment in outcomes)
            {
                var appointmentDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(appointment.StartsAt, zone).Date);

                if (appointmentDay != day)
                {
                    continue;
                }

                if (appointment.Status == AppointmentStatus.Completed)
                {
                    completed++;
                }
                else if (appointment.Status == AppointmentStatus.Cancelled)
                {
                    cancelled++;
                }
            }

            points.Add(new OwnerDailyPointDto(day, net, completed, cancelled));
        }

        return points;
    }

    private static IReadOnlyList<OwnerMonthlyPointDto> MonthlySeries(
        IReadOnlyList<OwnerInvoiceSnapshot> invoices,
        DateOnly currentMonthFirst,
        TimeZoneInfo zone)
    {
        var points = new List<OwnerMonthlyPointDto>();

        for (var back = TrendMonths - 1; back >= 0; back--)
        {
            var month = currentMonthFirst.AddMonths(-back);
            var start = At(month, zone);
            var end = At(month.AddMonths(1), zone);
            decimal net = 0m;

            foreach (var invoice in invoices)
            {
                if (invoice.IssuedAt >= start && invoice.IssuedAt < end)
                {
                    net += invoice.TotalAmount - invoice.TaxTotal - invoice.TipAmount;
                }
            }

            points.Add(new OwnerMonthlyPointDto(month, net));
        }

        return points;
    }

    private static IReadOnlyList<OwnerNamedRevenueDto> NamedRevenue(
        IReadOnlyList<OwnerInvoiceLineSnapshot> lines,
        bool countIsUnits)
    {
        return lines
            .GroupBy(line => line.Description, StringComparer.Ordinal)
            .Select(group => new OwnerNamedRevenueDto(
                group.Key,
                group.Sum(line => line.LineTotal),
                countIsUnits ? (int)group.Sum(line => line.Quantity) : group.Count()))
            .OrderByDescending(row => row.Revenue)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .Take(TopListSize)
            .ToList();
    }

    private async Task<IReadOnlyList<OwnerNamedRevenueDto>> CategoryRevenueAsync(
        IReadOnlyList<OwnerInvoiceLineSnapshot> serviceLines,
        CancellationToken cancellationToken)
    {
        if (serviceLines.Count == 0)
        {
            return [];
        }

        var serviceIds = serviceLines
            .Where(line => line.ServiceId is not null)
            .Select(line => line.ServiceId!.Value)
            .Distinct()
            .ToList();

        var categoryNames = await store.GetServiceCategoryNamesAsync(serviceIds, cancellationToken);

        return serviceLines
            .GroupBy(line =>
                (line.ServiceId is not null && categoryNames.TryGetValue(line.ServiceId.Value, out var name)
                    ? name
                    : Uncategorised),
                StringComparer.Ordinal)
            .Select(group => new OwnerNamedRevenueDto(
                group.Key,
                group.Sum(line => line.LineTotal),
                group.Count()))
            .OrderByDescending(row => row.Revenue)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .Take(TopListSize)
            .ToList();
    }

    private static IReadOnlyList<OwnerScheduleConflictDto> FindConflicts(
        IReadOnlyList<OwnerStaffBlockSnapshot> blocks,
        IReadOnlyList<DashboardStaffDto> staff)
    {
        var conflicts = new List<OwnerScheduleConflictDto>();

        foreach (var group in blocks.GroupBy(block => block.StaffId))
        {
            var ordered = group.OrderBy(block => block.StartsAt).ThenBy(block => block.EndsAt).ToList();

            for (var i = 0; i < ordered.Count - 1; i++)
            {
                // Adjacent-in-list blocks that overlap mean two visits claimed
                // the same staff member at once.
                if (ordered[i + 1].StartsAt < ordered[i].EndsAt)
                {
                    var member = staff.FirstOrDefault(person => person.Id == group.Key);
                    var overlapping = new[] { ordered[i], ordered[i + 1] };

                    conflicts.Add(new OwnerScheduleConflictDto(
                        group.Key,
                        member is not null ? $"{member.FirstName} {member.LastName}" : group.Key.ToString(),
                        overlapping.Min(block => block.StartsAt),
                        overlapping.Max(block => block.EndsAt),
                        2));
                }
            }
        }

        return conflicts
            .OrderBy(conflict => conflict.StartsAt)
            .ToList();
    }

    private static OwnerDashboardInventoryDto BuildInventory(IReadOnlyList<OwnerProductStockSnapshot> products)
    {
        var alerts = new List<(OwnerInventoryItemDto Item, int Rank)>();

        foreach (var product in products.Where(product => product.IsActive))
        {
            string status;
            int rank;

            if (product.CurrentStock <= 0m)
            {
                status = "OutOfStock";
                rank = 0;
            }
            else if (product.CurrentStock <= product.MinimumStock)
            {
                status = "LowStock";
                rank = 1;
            }
            else
            {
                continue;
            }

            alerts.Add((new OwnerInventoryItemDto(
                product.ProductId,
                product.Name,
                product.CurrentStock,
                product.MinimumStock,
                product.UnitOfMeasure,
                status), rank));
        }

        var ordered = alerts
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.Item.Name, StringComparer.Ordinal)
            .ToList();

        return new OwnerDashboardInventoryDto(
            ordered.Count(entry => entry.Rank == 1),
            ordered.Count(entry => entry.Rank == 0),
            ordered.Take(InventoryListSize).Select(entry => entry.Item).ToList());
    }

    private static IReadOnlyList<OwnerDashboardActionDto> BuildActions(
        OwnerDashboardOperationsDto operations,
        OwnerOpenBillsSnapshot openBills,
        OwnerDashboardInventoryDto inventory)
    {
        var actions = new List<OwnerDashboardActionDto>();

        if (openBills.IssuedUnpaidCount > 0)
        {
            actions.Add(new OwnerDashboardActionDto(
                "medium",
                $"{openBills.IssuedUnpaidCount} {(openBills.IssuedUnpaidCount == 1 ? "bill" : "bills")} left unpaid after checkout.",
                openBills.IssuedUnpaidCount,
                "/billing"));
        }

        if (openBills.DraftCount > 0)
        {
            actions.Add(new OwnerDashboardActionDto(
                "medium",
                $"{openBills.DraftCount} draft {(openBills.DraftCount == 1 ? "bill" : "bills")} waiting at the till.",
                openBills.DraftCount,
                "/billing"));
        }

        if (operations.AwaitingCheckout > 0)
        {
            actions.Add(new OwnerDashboardActionDto(
                "medium",
                $"{operations.AwaitingCheckout} completed {(operations.AwaitingCheckout == 1 ? "visit" : "visits")} awaiting checkout today.",
                operations.AwaitingCheckout,
                "/calendar"));
        }

        if (operations.Conflicts.Count > 0)
        {
            actions.Add(new OwnerDashboardActionDto(
                "high",
                $"{operations.Conflicts.Count} schedule {(operations.Conflicts.Count == 1 ? "conflict" : "conflicts")} today — two visits claim the same staff member.",
                operations.Conflicts.Count,
                "/calendar"));
        }

        var stockAttention = inventory.LowStockCount + inventory.OutOfStockCount;

        if (stockAttention > 0)
        {
            actions.Add(new OwnerDashboardActionDto(
                "low",
                $"{inventory.OutOfStockCount} out of stock and {inventory.LowStockCount} running low.",
                stockAttention,
                "/inventory"));
        }

        return actions;
    }

    private static DateTimeOffset Min(params DateTimeOffset[] values) => values.Min();
    private static DateTimeOffset Max(params DateTimeOffset[] values) => values.Max();
}
