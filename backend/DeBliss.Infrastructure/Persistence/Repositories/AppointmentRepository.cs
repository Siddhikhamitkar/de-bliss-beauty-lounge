using DeBliss.Application.Scheduling.Ports;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Appointment persistence for the scheduling module. GetByIdAsync returns
/// the tracked appointment (lines and customer included) because the
/// application service mutates what it loads; the range queries do not track.
/// DateTimeOffset predicates run in memory because SQLite cannot translate
/// them; the translatable filters (soft-delete) narrow the candidate set
/// first. Also implements the availability module's conflict port: a booking
/// still holds its slot unless it is cancelled, no-show or completed.
/// </summary>
public sealed class AppointmentRepository(DeBlissDbContext context)
    : IAppointmentRepository, IAppointmentConflictPort
{
    private static readonly AppointmentStatus[] SlotFreeingStatuses =
    [
        AppointmentStatus.Cancelled,
        AppointmentStatus.NoShow,
        AppointmentStatus.Completed
    ];

    public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return context.Appointments
            .Include(appointment => appointment.Services)
            .ThenInclude(line => line.Staff)
            .Include(appointment => appointment.Customer)
            .Include(appointment => appointment.ServiceBundle)
            .FirstOrDefaultAsync(appointment => appointment.Id == id, ct);
    }

    public async Task<IReadOnlyList<Appointment>> GetInRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var candidates = await context.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.DeletedAt == null)
            .Include(appointment => appointment.Services)
            .ThenInclude(line => line.Staff)
            .Include(appointment => appointment.Customer)
            .Include(appointment => appointment.ServiceBundle)
            .ToListAsync(ct);

        // DateTimeOffset comparisons do not translate to SQLite; the
        // soft-delete filter narrows in the database, overlap and ordering
        // run here.
        return candidates
            .Where(appointment => appointment.StartsAt < to && appointment.EndsAt > from)
            .OrderByDescending(appointment => appointment.StartsAt)
            .ToList();
    }

    public async Task<IReadOnlySet<Guid>> GetStaffIdsInRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var candidates = await context.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.DeletedAt == null)
            .Include(appointment => appointment.Services)
            .ToListAsync(ct);

        return candidates
            .Where(appointment => appointment.StartsAt < to && appointment.EndsAt > from)
            .SelectMany(appointment => appointment.Services.Select(line => line.StaffId))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();
    }

    public async Task<bool> HasConflictingAppointmentAsync(
        Guid staffId,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? excludeAppointmentId,
        CancellationToken ct = default)
    {
        var candidates = await context.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.DeletedAt == null)
            .Include(appointment => appointment.Services)
            .ToListAsync(ct);

        return candidates.Any(appointment =>
            appointment.Id != excludeAppointmentId
            && !SlotFreeingStatuses.Contains(appointment.Status)
            && appointment.StartsAt < end
            && appointment.EndsAt > start
            && appointment.Services.Any(line => line.StaffId == staffId));
    }

    public void Add(Appointment appointment) => context.Appointments.Add(appointment);
}
