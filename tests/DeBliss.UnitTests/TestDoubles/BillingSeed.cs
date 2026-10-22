using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;
using DeBliss.Infrastructure.Persistence;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// Writes billing rows straight to the database. The coupon writer arranges the
/// states reception can create; the appointment writer books lines at chosen
/// prices, which the customers seed cannot do — billing tests need invoice
/// amounts that differ from the catalogue to prove the price snapshot.
/// </summary>
internal static class BillingSeed
{
    public static async Task<Guid> CouponAsync(
        DeBlissDbContext context,
        string code,
        DiscountType discountType = DiscountType.Fixed,
        decimal discountValue = 10m,
        bool isActive = true,
        DateOnly? validFrom = null,
        DateOnly? validUntil = null)
    {
        var coupon = new Coupon
        {
            Id = Guid.NewGuid(),
            Code = code,
            DiscountType = discountType,
            DiscountValue = discountValue,
            IsActive = isActive,
            ValidFrom = validFrom,
            ValidUntil = validUntil
        };

        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        return coupon.Id;
    }

    public static async Task<Guid> AppointmentAsync(
        DeBlissDbContext context,
        Guid? customerId,
        DateTimeOffset startsAt,
        AppointmentStatus status = AppointmentStatus.Completed,
        string? walkInCustomerName = null,
        IReadOnlyList<(Guid ServiceId, string SnapshotName, decimal UnitPrice, int SortOrder)>? lines = null,
        Guid? serviceBundleId = null,
        decimal? bundlePriceSnapshot = null,
        IReadOnlyList<Guid?>? lineStaffIds = null)
    {
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            StartsAt = startsAt,
            EndsAt = startsAt.AddHours(1),
            Status = status,
            Source = AppointmentSource.Reception,
            WalkInCustomerName = walkInCustomerName,
            ServiceBundleId = serviceBundleId,
            BundlePriceSnapshot = bundlePriceSnapshot
        };

        var bookedLines = lines ?? [];

        for (var index = 0; index < bookedLines.Count; index++)
        {
            var (serviceId, snapshotName, unitPrice, sortOrder) = bookedLines[index];

            appointment.Services.Add(new AppointmentService
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id,
                ServiceId = serviceId,
                ServiceNameSnapshot = snapshotName,
                DurationMinutes = 60,
                UnitPrice = unitPrice,
                SortOrder = sortOrder,
                StaffId = lineStaffIds is { Count: > 0 } ? lineStaffIds[index] : null
            });
        }

        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        return appointment.Id;
    }

    /// <summary>An invoice row that already exists, so numbering can be proven against it.</summary>
    public static async Task<Guid> InvoiceAsync(
        DeBlissDbContext context,
        string invoiceNumber,
        Guid? appointmentId = null)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            AppointmentId = appointmentId,
            Status = InvoiceStatus.Draft,
            Currency = "AED"
        };

        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        return invoice.Id;
    }
}
