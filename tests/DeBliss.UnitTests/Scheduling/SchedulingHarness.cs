using DeBliss.Application.Scheduling;
using DeBliss.Application.Staffing;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;
using BookingService = DeBliss.Application.Scheduling.AppointmentService;

namespace DeBliss.UnitTests.Scheduling;

/// <summary>
/// Wires the real scheduling services to the real repositories over a throwaway
/// SQLite database. One context for the whole harness mirrors one web request,
/// where DI hands the repositories and the unit of work the same scoped DbContext.
/// </summary>
internal sealed class SchedulingHarness : IDisposable
{
    public SchedulingHarness()
    {
        Database = new SqliteTestDatabase();
        Context = Database.CreateContext();

        var appointmentRepository = new AppointmentRepository(Context);
        var customerRepository = new CustomerRepository(Context);
        var serviceRepository = new ServiceRepository(Context);
        var bundleRepository = new ServiceBundleRepository(Context);
        var staffRepository = new StaffRepository(Context);
        var scheduleRepository = new ScheduleRepository(Context);

        var availability = new StaffAvailabilityService(
            staffRepository, serviceRepository, scheduleRepository, appointmentRepository);
        Appointments = new BookingService(
            appointmentRepository,
            customerRepository,
            serviceRepository,
            bundleRepository,
            staffRepository,
            availability,
            Context,
            Database.Clock);
        Calendar = new CalendarService(appointmentRepository, staffRepository, scheduleRepository);
    }

    public SqliteTestDatabase Database { get; }

    public DeBlissDbContext Context { get; }

    public IAppointmentService Appointments { get; }

    public ICalendarService Calendar { get; }

    /// <summary>A separate context, so assertions read what was actually written.</summary>
    public DeBlissDbContext Reopen() => Database.CreateContext();

    public async Task<Guid> SeedCustomerAsync(string firstName, string lastName, string? phoneNumber = null)
    {
        await using var context = Reopen();
        var id = await CustomerSeed.CustomerAsync(context, firstName, lastName, phoneNumber);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedCategoryAsync(string name, bool isActive = true, int sortOrder = 0)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.CategoryAsync(context, name, isActive, sortOrder);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedServiceAsync(
        Guid categoryId,
        string name,
        int durationMinutes = 30,
        decimal price = 100m,
        bool isActive = true,
        IReadOnlyList<Guid>? staffIds = null)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.ServiceAsync(
            context, categoryId, name, price, durationMinutes, isActive, staffIds: staffIds);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedBundleAsync(
        Guid categoryId,
        string name,
        decimal price,
        IReadOnlyList<Guid> serviceIds,
        bool isActive = true)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.BundleAsync(context, categoryId, name, price, serviceIds, isActive);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedStaffAsync(
        string firstName,
        string lastName,
        bool isActive = true,
        string? calendarColor = null,
        int sortOrder = 0)
    {
        await using var context = Reopen();
        var id = await StaffingSeed.StaffAsync(
            context, firstName, lastName, isActive, calendarColor: calendarColor, sortOrder: sortOrder);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedAssignmentAsync(Guid staffId, Guid serviceId)
    {
        await using var context = Reopen();
        var id = await StaffingSeed.AssignmentAsync(context, staffId, serviceId);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task SeedWorkingHoursAsync(Guid staffId, DayOfWeek day, TimeSpan start, TimeSpan end)
    {
        await using var context = Reopen();
        await StaffingSeed.WorkingHoursAsync(context, staffId, day, start, end);
        Context.ChangeTracker.Clear();
    }

    public async Task<Guid> SeedTimeOffAsync(
        Guid staffId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        TimeOffType type = TimeOffType.Leave,
        string? reason = null)
    {
        await using var context = Reopen();
        var id = await StaffingSeed.TimeOffAsync(context, staffId, startsAt, endsAt, type, reason);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedBlockedTimeAsync(
        Guid? staffId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        BlockedTimeType type = BlockedTimeType.Break,
        string? reason = null)
    {
        await using var context = Reopen();
        var id = await StaffingSeed.BlockedTimeAsync(context, staffId, startsAt, endsAt, type, reason);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedAppointmentAsync(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        AppointmentStatus status = AppointmentStatus.Confirmed,
        Guid? customerId = null,
        string? walkInCustomerName = null,
        IReadOnlyList<SchedulingSeed.Line>? lines = null)
    {
        await using var context = Reopen();
        var id = await SchedulingSeed.AppointmentAsync(
            context, startsAt, endsAt, status, customerId, walkInCustomerName, lines: lines);
        Context.ChangeTracker.Clear();
        return id;
    }

    public void Dispose()
    {
        Context.Dispose();
        Database.Dispose();
    }
}
