using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Billing.Ports;
using DeBliss.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Invoice persistence for the billing module. GetByIdAsync returns a tracked
/// invoice (lines, payments, customer and appointment included) because the
/// application service mutates what it loads; every other query does not track.
/// Deleted invoices are filtered out everywhere — nothing in Phase 1 deletes
/// one, but the soft-delete filter means a future delete would stay harmless.
/// </summary>
public sealed class InvoiceRepository(DeBlissDbContext context) : IInvoiceRepository
{
    public Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return context.Invoices
            .Include(invoice => invoice.Items)
            .Include(invoice => invoice.Payments)
            .Include(invoice => invoice.Customer)
            .Include(invoice => invoice.Appointment)
            .FirstOrDefaultAsync(invoice => invoice.Id == id && invoice.DeletedAt == null, ct);
    }

    public async Task<Invoice?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken ct = default)
    {
        var candidates = await context.Invoices
            .AsNoTracking()
            .Where(invoice =>
                invoice.AppointmentId == appointmentId
                && invoice.DeletedAt == null
                && invoice.Status != InvoiceStatus.Void)
            .Include(invoice => invoice.Items)
            .Include(invoice => invoice.Customer)
            .Include(invoice => invoice.Appointment)
            .ToListAsync(ct);

        // SQLite cannot order DateTimeOffset columns; an appointment has a
        // single invoice in Phase 1, so newest-first is applied in memory.
        return candidates
            .OrderByDescending(invoice => invoice.CreatedAt)
            .ThenByDescending(invoice => invoice.Id)
            .FirstOrDefault();
    }

    public async Task<(IReadOnlyList<Invoice> Items, int TotalCount)> SearchAsync(
        InvoiceQuery query,
        CancellationToken ct = default)
    {
        var filtered = ApplyFilters(context.Invoices.AsNoTracking(), query);

        var totalCount = await filtered.CountAsync(ct);

        // Zero-padded sequence numbers make the invoice number order
        // chronologically as a plain string, which SQLite can sort.
        var items = await filtered
            .Include(invoice => invoice.Customer)
            .Include(invoice => invoice.Appointment)
            .Include(invoice => invoice.Payments)
            .OrderByDescending(invoice => invoice.InvoiceNumber)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<int> GetHighestSequenceAsync(int year, CancellationToken ct = default)
    {
        var prefix = $"INV-{year:D4}-";

        // Deliberately not filtered by soft-delete: a number that was ever used
        // stays burned, so a future deleted invoice cannot have its number
        // resurrected for a different sale.
        var numbers = await context.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.InvoiceNumber.StartsWith(prefix))
            .Select(invoice => invoice.InvoiceNumber)
            .ToListAsync(ct);

        var highest = 0;

        foreach (var number in numbers)
        {
            if (int.TryParse(number.AsSpan(prefix.Length), out var sequence) && sequence > highest)
            {
                highest = sequence;
            }
        }

        return highest;
    }

    public void Add(Invoice invoice) => context.Invoices.Add(invoice);

    private static IQueryable<Invoice> ApplyFilters(IQueryable<Invoice> query, InvoiceQuery normalized)
    {
        if (normalized.Search is { Length: > 0 } term)
        {
            var lowered = term.ToLowerInvariant();

            // One term hunts through the invoice number, the customer's name
            // and the walk-in name captured on the appointment — the three
            // things the receptionist has in hand when chasing a bill.
            query = query.Where(invoice =>
                invoice.InvoiceNumber.ToLower().Contains(lowered)
                || (invoice.Customer != null
                    && (invoice.Customer.FirstName.ToLower().Contains(lowered)
                        || invoice.Customer.LastName.ToLower().Contains(lowered)
                        || (invoice.Customer.FirstName + " " + invoice.Customer.LastName).ToLower().Contains(lowered)))
                || (invoice.Appointment != null
                    && invoice.Appointment.WalkInCustomerName != null
                    && invoice.Appointment.WalkInCustomerName.ToLower().Contains(lowered)));
        }

        if (normalized.CustomerId is { } customerId)
        {
            query = query.Where(invoice => invoice.CustomerId == customerId);
        }

        query = normalized.Status switch
        {
            InvoiceStatusFilter.Draft => query.Where(invoice => invoice.Status == InvoiceStatus.Draft),
            InvoiceStatusFilter.Paid => query.Where(invoice => invoice.Status == InvoiceStatus.Paid),
            _ => query
        };

        query = normalized.PaymentMethod switch
        {
            InvoicePaymentMethodFilter.Cash => query.Where(invoice =>
                invoice.Payments.Any(payment =>
                    payment.Status == PaymentStatus.Completed
                    && payment.Method == PaymentMethod.Cash)),
            InvoicePaymentMethodFilter.Card => query.Where(invoice =>
                invoice.Payments.Any(payment =>
                    payment.Status == PaymentStatus.Completed
                    && payment.Method == PaymentMethod.Card)),
            InvoicePaymentMethodFilter.BankTransfer => query.Where(invoice =>
                invoice.Payments.Any(payment =>
                    payment.Status == PaymentStatus.Completed
                    && payment.Method == PaymentMethod.BankTransfer)),
            _ => query
        };

        return query.Where(invoice => invoice.DeletedAt == null);
    }
}
