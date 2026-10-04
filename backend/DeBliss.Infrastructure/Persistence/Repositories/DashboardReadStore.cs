using DeBliss.Application.Dashboard.Ports;
using DeBliss.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// The dashboard's small set of cross-cutting queries. None of the module
/// repositories expose these shapes — the customer module has no reason to
/// search by month/day and the scheduling module never asks about a
/// customer's first visit — so they live here rather than widening those
/// contracts for one consumer.
/// </summary>
public sealed class DashboardReadStore(DeBlissDbContext context) : IDashboardReadStore
{
    public async Task<IReadOnlyList<Customer>> FindBirthdayCustomersAsync(
        IReadOnlyCollection<DateOnly> days,
        CancellationToken cancellationToken = default)
    {
        // Date-only month/day matching on a nullable column: materialise the
        // active customers' birthdays and match in memory. The set of
        // customers is small and the day list is at most seven entries.
        var customers = await context.Customers
            .AsNoTracking()
            .Where(customer => customer.IsActive && customer.DateOfBirth.HasValue)
            .Select(customer => new { customer.Id, customer.DateOfBirth })
            .ToListAsync(cancellationToken);

        var matches = customers
            .Where(customer => days.Any(day =>
                day.Month == customer.DateOfBirth!.Value.Month &&
                day.Day == customer.DateOfBirth.Value.Day))
            .Select(customer => customer.Id)
            .ToHashSet();

        return await context.Customers
            .AsNoTracking()
            .Where(customer => matches.Contains(customer.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetFirstVisitCustomerIdsAsync(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        CancellationToken cancellationToken = default)
    {
        // SQLite cannot translate DateTimeOffset comparisons, so appointments
        // materialise in memory (same approach as the appointment repository).
        // First-visit semantics need the customer's full history: a regular
        // who also has a booking today is not a first visit.
        var appointments = await context.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.DeletedAt == null && appointment.CustomerId.HasValue)
            .Select(appointment => new { appointment.CustomerId, appointment.StartsAt })
            .ToListAsync(cancellationToken);

        return appointments
            .GroupBy(appointment => appointment.CustomerId!.Value)
            .Select(group => new { CustomerId = group.Key, FirstVisitAt = group.Min(appointment => appointment.StartsAt) })
            .Where(group => group.FirstVisitAt >= dayStart && group.FirstVisitAt < dayEnd)
            .Select(group => group.CustomerId)
            .ToList();
    }

    public async Task<IReadOnlyList<CustomerNote>> GetRecentNotesAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        // SQLite cannot order by DateTimeOffset; the active-customer note set
        // is small, so materialise and sort in memory.
        var notes = await context.CustomerNotes
            .AsNoTracking()
            .Include(note => note.Customer)
            .Where(note => note.Customer.IsActive)
            .ToListAsync(cancellationToken);

        return notes
            .OrderByDescending(note => note.CreatedAt)
            .Take(take)
            .ToList();
    }
}
