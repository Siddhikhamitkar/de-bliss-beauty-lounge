using DeBliss.Application.Dashboard.Dtos;

namespace DeBliss.Application.Dashboard.Dtos;

/// <summary>
/// The owner's business snapshot: money the receptionist dashboard deliberately
/// never carries, behind the owner-only endpoint.
///
/// Financial definitions used throughout (all amounts in the invoice currency):
/// <list type="bullet">
/// <item><b>Net revenue</b> — over paid invoices (Draft/Issued/PartiallyPaid/Void/
/// Refunded never count), issued in the window: TotalAmount − TaxTotal − TipAmount.
/// Discounts are already inside TotalAmount; VAT is removed in both pricing modes
/// because TaxTotal is the embedded VAT when prices are tax-inclusive; tips are
/// staff money and never salon revenue.</item>
/// <item><b>Collected</b> — payments tendered in the window by PaidAt: Σ Completed
/// − Σ Refunded. Today the checkout flow makes these equal net revenue; the two
/// bases stay separate so part payments or refunds cannot silently blur them.</item>
/// <item><b>Outstanding</b> — over invoices left Issued/PartiallyPaid (never Draft,
/// whose stored totals predate the coupon, VAT and tip chosen at the till):
/// Σ(TotalAmount − completed payments). The current flow always charges in full at
/// checkout, so this reads 0.00 until a part-payment flow exists; drafts waiting on
/// the till are reported as counts in the action list instead of as money.</item>
/// <item><b>Service/retail revenue</b> — Σ LineTotal of Service/Product lines on
/// paid invoices. Line totals are pre-discount: invoice-level discounts (coupon,
/// manual, bundle saving) are not attributable to a single line, so the split can
/// exceed net revenue by the discount amount. Tips and VAT are invoice-level too
/// and never touch line revenue.</item>
/// </list>
/// </summary>
public sealed record OwnerDashboardDto(
    DateTimeOffset GeneratedAt,
    string TimeZoneId,
    OwnerDashboardKpisDto Kpis,
    OwnerDashboardOperationsDto Operations,
    OwnerDashboardRevenueDto Revenue,
    OwnerDashboardInventoryDto Inventory,
    IReadOnlyList<OwnerDashboardActionDto> Actions);

/// <summary>
/// The fixed headline cards. Today and month-to-date keep their own windows
/// regardless of the trend filter — an owner reads the day and the month side
/// by side — while average transaction, retail split and outstanding follow the
/// selected range (today by default). Every revenue figure carries its
/// previous-equivalent comparison: yesterday for today, the same number of
/// calendar days of the previous month for month-to-date.
/// </summary>
public sealed record OwnerDashboardKpisDto(
    string Currency,
    decimal NetRevenueToday,
    decimal NetRevenueYesterday,
    decimal NetRevenueMonthToDate,
    decimal NetRevenueSamePeriodLastMonth,
    int CompletedAppointmentsToday,
    int UpcomingAppointmentsToday,
    decimal AverageTransactionValue,
    int PaidInvoiceCount,
    decimal RetailRevenue,
    decimal ServiceRevenue,
    decimal OutstandingAmount,
    int OutstandingInvoiceCount);

/// <summary>Today's floor: the receptionist snapshot's operational views plus
/// the money-adjacent counter the front desk is not shown — visits that finished
/// but have not been through checkout.</summary>
public sealed record OwnerDashboardOperationsDto(
    DashboardTodayDto Today,
    IReadOnlyList<DashboardAppointmentDto> Schedule,
    IReadOnlyList<DashboardAppointmentDto> Upcoming,
    IReadOnlyList<DashboardStaffDto> Staff,
    int AwaitingCheckout,
    IReadOnlyList<OwnerScheduleConflictDto> Conflicts);

/// <summary>Two staff slots that overlap in time. Booking normally prevents
/// these; when data hand-edited elsewhere produces one, the owner sees it.</summary>
public sealed record OwnerScheduleConflictDto(
    Guid StaffId,
    string StaffName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int AppointmentCount);

/// <summary>
/// Revenue analytics for the selected window. Line-level revenues are
/// pre-discount (see the dashboard record's definitions); Collected is the
/// payment-basis figure; the daily series carries a point for every day in the
/// window so the chart's x-axis is continuous.
/// </summary>
public sealed record OwnerDashboardRevenueDto(
    string RangeLabel,
    DateOnly From,
    DateOnly To,
    decimal NetRevenue,
    decimal PreviousNetRevenue,
    decimal? ChangePercent,
    decimal ServiceRevenue,
    decimal RetailRevenue,
    decimal VatTotal,
    decimal TipTotal,
    decimal DiscountTotal,
    decimal Collected,
    int PaidInvoiceCount,
    decimal AverageTransactionValue,
    IReadOnlyList<OwnerDailyPointDto> Daily,
    IReadOnlyList<OwnerMonthlyPointDto> Monthly,
    IReadOnlyList<OwnerNamedRevenueDto> TopServices,
    IReadOnlyList<OwnerNamedRevenueDto> Categories,
    IReadOnlyList<OwnerNamedRevenueDto> RetailProducts);

/// <summary>One day of the selected window: net revenue and appointment outcomes
/// by the salon-zone calendar day. Empty days are real zeros.</summary>
public sealed record OwnerDailyPointDto(
    DateOnly Date,
    decimal NetRevenue,
    int Completed,
    int Cancelled);

/// <summary>The last six salon-zone calendar months including the current one;
/// the current month accrues month-to-date.</summary>
public sealed record OwnerMonthlyPointDto(DateOnly MonthStart, decimal NetRevenue);

/// <summary>Revenue attributed to a named line grouping. Services group by the
/// name snapshot charged on the day; categories resolve through the service's
/// current catalogue category, with anything unmatched shown as uncategorised.</summary>
public sealed record OwnerNamedRevenueDto(string Name, decimal Revenue, int Count);

/// <summary>Stock attention list from the existing inventory ledger: out-of-stock
/// first, then at-or-below the product's own minimum. Inactive products are
/// ignored, and counts cover the full catalogue while the list caps for display.</summary>
public sealed record OwnerDashboardInventoryDto(
    int LowStockCount,
    int OutOfStockCount,
    IReadOnlyList<OwnerInventoryItemDto> Items);

public sealed record OwnerInventoryItemDto(
    Guid ProductId,
    string Name,
    decimal CurrentStock,
    decimal MinimumStock,
    string UnitOfMeasure,
    string Status);

/// <summary>A prioritised, data-backed item for the owner's attention. Every
/// entry is derived from a real count; nothing is raised speculatively.</summary>
public sealed record OwnerDashboardActionDto(
    string Severity,
    string Message,
    int Count,
    string Link);
