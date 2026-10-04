using DeBliss.Domain.Scheduling;
using DeBliss.Infrastructure.Persistence;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// Writes appointments straight to the database, past the application
/// services, so a test can arrange the bookings the front desk would normally
/// create — including overlapping ones the validation rules must reject.
/// </summary>
internal static class SchedulingSeed
{
    public sealed record Line(
        Guid ServiceId,
        Guid? StaffId,
        string SnapshotName,
        int DurationMinutes = 60,
        decimal UnitPrice = 100m);

    public static async Task<Guid> AppointmentAsync(
        DeBlissDbContext context,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        AppointmentStatus status = AppointmentStatus.Confirmed,
        Guid? customerId = null,
        string? walkInCustomerName = null,
        string? walkInCustomerPhone = null,
        IReadOnlyList<Line>? lines = null)
    {
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            WalkInCustomerName = walkInCustomerName,
            WalkInCustomerPhone = walkInCustomerPhone,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Status = status,
            Source = AppointmentSource.Reception
        };

        var sortOrder = 0;

        foreach (var line in lines ?? [])
        {
            appointment.Services.Add(new AppointmentService
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id,
                ServiceId = line.ServiceId,
                StaffId = line.StaffId,
                ServiceNameSnapshot = line.SnapshotName,
                DurationMinutes = line.DurationMinutes,
                UnitPrice = line.UnitPrice,
                SortOrder = sortOrder++
            });
        }

        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        return appointment.Id;
    }
}
