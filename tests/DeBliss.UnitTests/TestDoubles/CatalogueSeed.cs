using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Persistence;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// Writes rows straight to the database, past the application services. Tests use
/// this both to arrange a starting state and to prove what a rule did to data it
/// was not asked to touch.
/// </summary>
internal static class CatalogueSeed
{
    public static async Task<Guid> StaffAsync(
        DeBlissDbContext context,
        string firstName,
        string lastName,
        bool isActive = true,
        string? jobTitle = null,
        string? email = null,
        string? phoneNumber = null,
        string? calendarColor = null,
        int sortOrder = 0)
    {
        var staff = new Staff
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            IsActive = isActive,
            JobTitle = jobTitle,
            Email = email,
            PhoneNumber = phoneNumber,
            CalendarColor = calendarColor,
            SortOrder = sortOrder
        };

        context.Staff.Add(staff);
        await context.SaveChangesAsync();

        return staff.Id;
    }

    public static async Task<Guid> CategoryAsync(
        DeBlissDbContext context,
        string name,
        bool isActive = true,
        int sortOrder = 0,
        string? description = null)
    {
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            IsActive = isActive,
            SortOrder = sortOrder
        };

        context.ServiceCategories.Add(category);
        await context.SaveChangesAsync();

        return category.Id;
    }

    public static async Task<Guid> ServiceAsync(
        DeBlissDbContext context,
        Guid categoryId,
        string name,
        decimal price,
        int durationMinutes = 30,
        bool isActive = true,
        string? description = null,
        IReadOnlyList<Guid>? staffIds = null,
        int sortOrder = 0)
    {
        var service = new Service
        {
            Id = Guid.NewGuid(),
            ServiceCategoryId = categoryId,
            Name = name,
            Description = description,
            DurationMinutes = durationMinutes,
            Price = price,
            IsActive = isActive,
            SortOrder = sortOrder
        };

        foreach (var staffId in staffIds ?? [])
        {
            service.StaffServices.Add(new StaffService
            {
                Id = Guid.NewGuid(),
                ServiceId = service.Id,
                StaffId = staffId
            });
        }

        context.Services.Add(service);
        await context.SaveChangesAsync();

        return service.Id;
    }

    /// <summary>
    /// Writes a bundle straight to the menu. The component list order becomes
    /// the delivery sequence, the way the application service derives it from
    /// the submitted request.
    /// </summary>
    public static async Task<Guid> BundleAsync(
        DeBlissDbContext context,
        Guid categoryId,
        string name,
        decimal price,
        IReadOnlyList<Guid> serviceIds,
        bool isActive = true,
        string? description = null,
        int sortOrder = 0)
    {
        var bundle = new ServiceBundle
        {
            Id = Guid.NewGuid(),
            ServiceCategoryId = categoryId,
            Name = name,
            Description = description,
            Price = price,
            IsActive = isActive,
            SortOrder = sortOrder
        };

        for (var position = 0; position < serviceIds.Count; position++)
        {
            bundle.Items.Add(new ServiceBundleItem
            {
                Id = Guid.NewGuid(),
                ServiceBundleId = bundle.Id,
                ServiceId = serviceIds[position],
                DisplayOrder = position
            });
        }

        context.ServiceBundles.Add(bundle);
        await context.SaveChangesAsync();

        return bundle.Id;
    }

    /// <summary>
    /// Books a service at a price, the way the future appointment module will.
    /// Used to prove that editing the catalogue never rewrites history.
    /// </summary>
    public static async Task<Guid> BookedServiceAsync(
        DeBlissDbContext context,
        Guid serviceId,
        string snapshotName,
        decimal unitPrice,
        int durationMinutes)
    {
        var startsAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            StartsAt = startsAt,
            EndsAt = startsAt.AddMinutes(durationMinutes),
            Status = AppointmentStatus.Completed,
            Source = AppointmentSource.Reception,
            WalkInCustomerName = "Walk-in"
        };

        appointment.Services.Add(new AppointmentService
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id,
            ServiceId = serviceId,
            ServiceNameSnapshot = snapshotName,
            DurationMinutes = durationMinutes,
            UnitPrice = unitPrice
        });

        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        return appointment.Id;
    }
}
