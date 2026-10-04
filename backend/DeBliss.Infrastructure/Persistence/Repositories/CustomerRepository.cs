using DeBliss.Application.Customers;
using DeBliss.Application.Customers.Dtos;
using DeBliss.Application.Customers.Ports;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Customer persistence for the customers module. GetByIdAsync and GetNoteAsync
/// return tracked entities because the application service mutates what it
/// loads; the read-only list, notes, history and aggregate queries do not track.
/// </summary>
public sealed class CustomerRepository(DeBlissDbContext context) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return context.Customers.FirstOrDefaultAsync(customer => customer.Id == id, ct);
    }

    public async Task<(IReadOnlyList<Customer> Items, int TotalCount)> SearchAsync(
        CustomerQuery query,
        CancellationToken ct = default)
    {
        var filtered = ApplyFilters(context.Customers.AsNoTracking(), query);

        var totalCount = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderBy(customer => customer.LastName)
            .ThenBy(customer => customer.FirstName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Customer>> GetByNormalizedPhoneAsync(
        string normalizedPhone,
        Guid? excludeId,
        CancellationToken ct = default)
    {
        var matches = context.Customers
            .AsNoTracking()
            .Where(customer =>
                customer.PhoneNumber != null
                && customer.PhoneNumber
                    .Replace(" ", "")
                    .Replace("-", "")
                    .Replace("(", "")
                    .Replace(")", "")
                    .Replace(".", "")
                    .Replace("+", "")
                    .Replace("/", "") == normalizedPhone);

        if (excludeId is { } id)
        {
            matches = matches.Where(customer => customer.Id != id);
        }

        return await matches
            .OrderBy(customer => customer.LastName)
            .ThenBy(customer => customer.FirstName)
            .ToListAsync(ct);
    }

    public Task<CustomerNote?> GetNoteAsync(Guid customerId, Guid noteId, CancellationToken ct = default)
    {
        return context.CustomerNotes.FirstOrDefaultAsync(
            note => note.CustomerId == customerId && note.Id == noteId,
            ct);
    }

    public async Task<IReadOnlyList<CustomerNote>> ListNotesAsync(Guid customerId, CancellationToken ct = default)
    {
        var notes = await context.CustomerNotes
            .AsNoTracking()
            .Where(note => note.CustomerId == customerId)
            .ToListAsync(ct);

        // SQLite cannot order DateTimeOffset columns; a customer's notes are
        // few, so newest-first is applied in memory.
        return notes.OrderByDescending(note => note.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<Appointment>> ListAppointmentsAsync(Guid customerId, CancellationToken ct = default)
    {
        var appointments = await context.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.CustomerId == customerId && appointment.DeletedAt == null)
            .Include(appointment => appointment.Services)
            .ThenInclude(line => line.Staff)
            .ToListAsync(ct);

        // SQLite cannot order DateTimeOffset columns; a customer's history is
        // small, so newest-first is applied in memory.
        return appointments.OrderByDescending(appointment => appointment.StartsAt).ToList();
    }

    public async Task<(int Total, int Completed, int Cancelled, int NoShow, DateTimeOffset? LastVisitAt)> GetAppointmentSummaryAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        var appointments = context.Appointments.AsNoTracking()
            .Where(appointment => appointment.CustomerId == customerId && appointment.DeletedAt == null);

        var total = await appointments.CountAsync(ct);
        var completed = await appointments.CountAsync(appointment => appointment.Status == AppointmentStatus.Completed, ct);
        var cancelled = await appointments.CountAsync(appointment => appointment.Status == AppointmentStatus.Cancelled, ct);
        var noShow = await appointments.CountAsync(appointment => appointment.Status == AppointmentStatus.NoShow, ct);

        // SQLite cannot aggregate DateTimeOffset columns, so the last completed
        // visit is picked from the (small) projected list in memory.
        var completedStarts = await appointments
            .Where(appointment => appointment.Status == AppointmentStatus.Completed)
            .Select(appointment => appointment.StartsAt)
            .ToListAsync(ct);

        var lastVisitAt = completedStarts
            .Select(startsAt => (DateTimeOffset?)startsAt)
            .DefaultIfEmpty()
            .Max();

        return (total, completed, cancelled, noShow, lastVisitAt);
    }

    public void Add(Customer customer) => context.Customers.Add(customer);

    public void AddNote(CustomerNote note) => context.CustomerNotes.Add(note);

    private static IQueryable<Customer> ApplyFilters(IQueryable<Customer> query, CustomerQuery normalized)
    {
        if (normalized.Search is { Length: > 0 } term)
        {
            var lowered = term.ToLowerInvariant();

            // A term with digits also hunts through phone numbers with the
            // formatting stripped, so "501234567" finds "+971 50 123 4567".
            // The Replace chain must sit inline in the lambda: EF Core only
            // translates its own string methods, not calls to helpers. The
            // phone clause joins the name clauses with OR — a stacked Where
            // would AND it, and a digits-only term never matches a name.
            var digits = CustomerPhone.Normalize(term);

            query = digits is not null
                ? query.Where(customer =>
                    customer.FirstName.ToLower().Contains(lowered)
                    || customer.LastName.ToLower().Contains(lowered)
                    || (customer.FirstName + " " + customer.LastName).ToLower().Contains(lowered)
                    || (customer.Email != null && customer.Email.ToLower().Contains(lowered))
                    || (customer.PhoneNumber != null
                        && customer.PhoneNumber
                            .Replace(" ", "")
                            .Replace("-", "")
                            .Replace("(", "")
                            .Replace(")", "")
                            .Replace(".", "")
                            .Replace("+", "")
                            .Replace("/", "")
                            .Contains(digits)))
                : query.Where(customer =>
                    customer.FirstName.ToLower().Contains(lowered)
                    || customer.LastName.ToLower().Contains(lowered)
                    || (customer.FirstName + " " + customer.LastName).ToLower().Contains(lowered)
                    || (customer.Email != null && customer.Email.ToLower().Contains(lowered)));
        }

        return normalized.Status switch
        {
            CustomerStatusFilter.Active => query.Where(customer => customer.IsActive),
            CustomerStatusFilter.Inactive => query.Where(customer => !customer.IsActive),
            _ => query
        };
    }
}
