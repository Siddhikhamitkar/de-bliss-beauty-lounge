using DeBliss.Application.Dashboard.Ports;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// The owner dashboard's billing, inventory and scheduling reads. The money
/// filters lean on the only statuses that mean money actually moved
/// (<c>Paid</c> invoices, Completed/Refunded payments), so a future
/// cancellation or refund flow cannot silently change reported revenue.
/// DateTimeOffset comparisons do not translate to SQLite, so range filtering
/// happens in memory over narrow projections (same approach as the
/// appointment repository); the non-date predicates do translate and are
/// applied in SQL.
/// </summary>
public sealed class OwnerDashboardReadStore(DeBlissDbContext context) : IOwnerDashboardReadStore
{
    public async Task<IReadOnlyList<OwnerInvoiceSnapshot>> GetPaidInvoicesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var invoices = await context.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.DeletedAt == null && invoice.Status == InvoiceStatus.Paid)
            .Select(invoice => new
            {
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.IssuedAt,
                invoice.SubTotal,
                invoice.DiscountTotal,
                invoice.TaxTotal,
                invoice.TipAmount,
                invoice.TotalAmount,
                Lines = invoice.Items
                    .OrderBy(line => line.SortOrder)
                    .Select(line => new
                    {
                        line.ItemType,
                        line.ServiceId,
                        line.ProductId,
                        line.Description,
                        line.Quantity,
                        line.LineTotal
                    })
                    .ToList()
            })
            .ToListAsync(ct);

        return invoices
            .Where(invoice => invoice.IssuedAt >= from && invoice.IssuedAt < to)
            .Select(invoice => new OwnerInvoiceSnapshot(
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.IssuedAt!.Value,
                invoice.SubTotal,
                invoice.DiscountTotal,
                invoice.TaxTotal,
                invoice.TipAmount,
                invoice.TotalAmount,
                invoice.Lines
                    .Select(line => new OwnerInvoiceLineSnapshot(
                        line.ItemType, line.ServiceId, line.ProductId, line.Description, line.Quantity, line.LineTotal))
                    .ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OwnerPaymentSnapshot>> GetPaymentsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var payments = await context.Payments
            .AsNoTracking()
            .Where(payment => payment.DeletedAt == null &&
                (payment.Status == PaymentStatus.Completed || payment.Status == PaymentStatus.Refunded))
            .Select(payment => new { payment.Status, payment.Amount, payment.PaidAt })
            .ToListAsync(ct);

        return payments
            .Where(payment => payment.PaidAt >= from && payment.PaidAt < to)
            .Select(payment => new OwnerPaymentSnapshot(payment.Status, payment.Amount, payment.PaidAt))
            .ToList();
    }

    public async Task<OwnerOpenBillsSnapshot> GetOpenBillsAsync(CancellationToken ct = default)
    {
        var draftCount = await context.Invoices
            .AsNoTracking()
            .CountAsync(invoice => invoice.DeletedAt == null && invoice.Status == InvoiceStatus.Draft, ct);

        // Status and soft-delete predicates translate to SQL; the balance
        // arithmetic runs in memory over the tiny open-bill set.
        var openInvoices = await context.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.DeletedAt == null &&
                (invoice.Status == InvoiceStatus.Issued || invoice.Status == InvoiceStatus.PartiallyPaid))
            .Select(invoice => new
            {
                invoice.TotalAmount,
                Collected = invoice.Payments
                    .Where(payment => payment.Status == PaymentStatus.Completed && payment.DeletedAt == null)
                    .Sum(payment => (decimal?)payment.Amount)
            })
            .ToListAsync(ct);

        var balances = openInvoices
            .Select(invoice => invoice.TotalAmount - (invoice.Collected ?? 0m))
            .ToList();

        return new OwnerOpenBillsSnapshot(
            draftCount,
            balances.Count(balance => balance > 0m),
            balances.Sum(balance => Math.Max(0m, balance)));
    }

    public async Task<IReadOnlySet<Guid>> GetAppointmentIdsWithPaidInvoiceAsync(CancellationToken ct = default)
    {
        var appointmentIds = await context.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.DeletedAt == null &&
                invoice.Status == InvoiceStatus.Paid &&
                invoice.AppointmentId != null)
            .Select(invoice => invoice.AppointmentId!.Value)
            .ToListAsync(ct);

        return appointmentIds.ToHashSet();
    }

    public async Task<IReadOnlyList<OwnerStaffBlockSnapshot>> GetStaffBlocksAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        // Cancelled and no-show visits free their staff slot; the status
        // predicate translates to SQL, the date overlap is applied in memory.
        // A nested collection projection translates to SQLite; SelectMany over
        // the navigation would need APPLY, which SQLite does not support.
        var appointments = await context.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.DeletedAt == null &&
                appointment.Status != AppointmentStatus.Cancelled &&
                appointment.Status != AppointmentStatus.NoShow)
            .Select(appointment => new
            {
                AppointmentId = appointment.Id,
                appointment.StartsAt,
                appointment.EndsAt,
                StaffIds = appointment.Services
                    .Where(line => line.StaffId != null)
                    .Select(line => line.StaffId!.Value)
                    .ToList()
            })
            .ToListAsync(ct);

        return appointments
            .Where(appointment => appointment.StartsAt < to && appointment.EndsAt > from)
            .SelectMany(appointment => appointment.StaffIds.Select(staffId =>
                new OwnerStaffBlockSnapshot(appointment.AppointmentId, staffId, appointment.StartsAt, appointment.EndsAt)))
            .ToList();
    }

    public async Task<IReadOnlyList<OwnerAppointmentOutcomeSnapshot>> GetAppointmentOutcomesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var appointments = await context.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.DeletedAt == null)
            .Select(appointment => new { appointment.Id, appointment.StartsAt, appointment.Status })
            .ToListAsync(ct);

        return appointments
            .Where(appointment => appointment.StartsAt >= from && appointment.StartsAt < to)
            .Select(appointment => new OwnerAppointmentOutcomeSnapshot(
                appointment.Id, appointment.StartsAt, appointment.Status))
            .ToList();
    }

    public async Task<IReadOnlyList<OwnerProductStockSnapshot>> GetProductStockAsync(CancellationToken ct = default)
    {
        var products = await context.InventoryProducts
            .AsNoTracking()
            .Where(product => product.IsActive)
            .Select(product => new
            {
                ProductId = product.Id,
                product.Name,
                product.CurrentStock,
                product.MinimumStock,
                product.UnitOfMeasure,
                product.IsActive
            })
            .ToListAsync(ct);

        return products
            .Select(product => new OwnerProductStockSnapshot(
                product.ProductId, product.Name, product.CurrentStock,
                product.MinimumStock, product.UnitOfMeasure, product.IsActive))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetServiceCategoryNamesAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken ct = default)
    {
        if (serviceIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await context.Services
            .AsNoTracking()
            .Where(service => serviceIds.Contains(service.Id) && service.ServiceCategoryId != null)
            .Select(service => new { ServiceId = service.Id, Name = service.ServiceCategory!.Name })
            .ToListAsync(ct);

        return rows.ToDictionary(row => row.ServiceId, row => row.Name);
    }
}
