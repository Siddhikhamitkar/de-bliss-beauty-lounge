using DeBliss.Domain.Customers;
using DeBliss.Domain.Scheduling;
using DeBliss.Infrastructure.Persistence;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// Writes customer rows straight to the database, past the application services,
/// so a test can arrange the state the front desk would normally create — and,
/// for the appointment history, the state only the future appointment module
/// will normally create.
/// </summary>
internal static class CustomerSeed
{
    public static async Task<Guid> CustomerAsync(
        DeBlissDbContext context,
        string firstName,
        string lastName,
        string? phoneNumber = null,
        string? email = null,
        bool isActive = true,
        DateOnly? dateOfBirth = null,
        string? gender = null,
        string? address = null)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phoneNumber,
            Email = email,
            IsActive = isActive,
            DateOfBirth = dateOfBirth,
            Gender = gender,
            Address = address
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        return customer.Id;
    }

    public static async Task<Guid> NoteAsync(
        DeBlissDbContext context,
        Guid customerId,
        string note)
    {
        var entry = new CustomerNote
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Note = note
        };

        context.CustomerNotes.Add(entry);
        await context.SaveChangesAsync();

        return entry.Id;
    }

    public static async Task<Guid> AppointmentAsync(
        DeBlissDbContext context,
        Guid customerId,
        DateTimeOffset startsAt,
        AppointmentStatus status = AppointmentStatus.Completed,
        IReadOnlyList<(Guid ServiceId, Guid? StaffId, string SnapshotName)>? lines = null)
    {
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            StartsAt = startsAt,
            EndsAt = startsAt.AddHours(1),
            Status = status,
            Source = AppointmentSource.Reception
        };

        var sortOrder = 0;

        foreach (var (serviceId, staffId, snapshotName) in lines ?? [])
        {
            appointment.Services.Add(new AppointmentService
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id,
                ServiceId = serviceId,
                StaffId = staffId,
                ServiceNameSnapshot = snapshotName,
                DurationMinutes = 60,
                UnitPrice = 100m,
                SortOrder = sortOrder++
            });
        }

        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        return appointment.Id;
    }
}
