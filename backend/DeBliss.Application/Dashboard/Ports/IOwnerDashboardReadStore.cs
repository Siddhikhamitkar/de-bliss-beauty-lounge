using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Dashboard.Ports;

/// <summary>
/// Read-only cross-cutting queries for the owner dashboard: billing aggregates
/// no module port exposes in these shapes plus the small inventory and
/// scheduling reads the dashboard composes. Projections are deliberately
/// narrow — only the columns the aggregates need.
/// </summary>
public interface IOwnerDashboardReadStore
{
    /// <summary>
    /// Paid, non-deleted invoices issued inside [from, to), with their charge
    /// lines. Voided and refunded invoices are excluded by status so a future
    /// cancellation flow cannot leak into revenue.
    /// </summary>
    Task<IReadOnlyList<OwnerInvoiceSnapshot>> GetPaidInvoicesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>Completed and refunded payments tendered inside [from, to), the
    /// two statuses that make up the collected-money basis.</summary>
    Task<IReadOnlyList<OwnerPaymentSnapshot>> GetPaymentsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>
    /// Open-bill state: how many drafts wait at the till, and the money still
    /// owed on invoices left Issued or PartiallyPaid (invoice total minus what
    /// has actually been collected against each). Drafts carry no reliable
    /// total — their VAT, coupon and tip are chosen at checkout — so they are
    /// counted, never summed.
    /// </summary>
    Task<OwnerOpenBillsSnapshot> GetOpenBillsAsync(CancellationToken ct = default);

    /// <summary>Appointment ids that have already been paid, so completed
    /// visits still awaiting checkout can be told apart from settled ones.</summary>
    Task<IReadOnlySet<Guid>> GetAppointmentIdsWithPaidInvoiceAsync(CancellationToken ct = default);

    /// <summary>
    /// Staff time occupied by appointments overlapping [from, to), excluding
    /// cancelled and no-show visits (they free their slots). The overlap pairs
    /// feed the schedule-conflict report.
    /// </summary>
    Task<IReadOnlyList<OwnerStaffBlockSnapshot>> GetStaffBlocksAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>Statuses and start times of appointments overlapping [from, to),
    /// the daily completion/cancellation trend's raw material.</summary>
    Task<IReadOnlyList<OwnerAppointmentOutcomeSnapshot>> GetAppointmentOutcomesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>Active products with their stock position, straight from the
    /// inventory module's tables — the dashboard never recomputes stock.</summary>
    Task<IReadOnlyList<OwnerProductStockSnapshot>> GetProductStockAsync(CancellationToken ct = default);

    /// <summary>The catalogue category name of each service id, for grouping
    /// service revenue. Services since deleted or uncategorised simply miss the
    /// map and report as uncategorised rather than disappearing from history.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetServiceCategoryNamesAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken ct = default);
}

/// <summary>A paid invoice's charge snapshot: the money the invoice froze at
/// checkout plus its lines for the service/retail split.</summary>
public sealed record OwnerInvoiceSnapshot(
    Guid Id,
    string InvoiceNumber,
    DateTimeOffset IssuedAt,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal TipAmount,
    decimal TotalAmount,
    IReadOnlyList<OwnerInvoiceLineSnapshot> Lines);

public sealed record OwnerInvoiceLineSnapshot(
    InvoiceItemType ItemType,
    Guid? ServiceId,
    Guid? ProductId,
    string Description,
    decimal Quantity,
    decimal LineTotal);

public sealed record OwnerPaymentSnapshot(PaymentStatus Status, decimal Amount, DateTimeOffset PaidAt);

public sealed record OwnerOpenBillsSnapshot(
    int DraftCount,
    int IssuedUnpaidCount,
    decimal IssuedUnpaidOutstanding);

public sealed record OwnerStaffBlockSnapshot(
    Guid AppointmentId,
    Guid StaffId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record OwnerAppointmentOutcomeSnapshot(Guid AppointmentId, DateTimeOffset StartsAt, AppointmentStatus Status);

public sealed record OwnerProductStockSnapshot(
    Guid ProductId,
    string Name,
    decimal CurrentStock,
    decimal MinimumStock,
    string UnitOfMeasure,
    bool IsActive);
