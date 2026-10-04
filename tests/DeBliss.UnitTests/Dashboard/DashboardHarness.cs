using DeBliss.Application.Dashboard;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.Dashboard;

/// <summary>
/// Wires the real dashboard service to the real repositories over a throwaway
/// SQLite database, exactly as DI does in the API — one context per harness,
/// mirroring one web request.
/// </summary>
internal sealed class DashboardHarness : IDisposable
{
    public DashboardHarness()
    {
        Database = new SqliteTestDatabase();
        Context = Database.CreateContext();

        var appointmentRepository = new AppointmentRepository(Context);
        var staffRepository = new StaffRepository(Context);
        var scheduleRepository = new ScheduleRepository(Context);
        var dashboardStore = new DashboardReadStore(Context);

        Dashboard = new ReceptionistDashboardService(
            appointmentRepository,
            staffRepository,
            scheduleRepository,
            dashboardStore,
            Database.Clock);
    }

    public SqliteTestDatabase Database { get; }

    public DeBlissDbContext Context { get; }

    public IReceptionistDashboardService Dashboard { get; }

    /// <summary>The service's "now" — tests seed relative to this, never to wall clock.</summary>
    public DateTimeOffset Now => Database.Clock.GetLocalNow();

    public async Task<Guid> SeedStaffAsync(
        string firstName,
        string lastName,
        bool isActive = true,
        string? calendarColor = null,
        int sortOrder = 0)
    {
        await using var context = Database.CreateContext();
        var id = await StaffingSeed.StaffAsync(
            context, firstName, lastName, isActive, calendarColor: calendarColor, sortOrder: sortOrder);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task SeedWorkingHoursAsync(Guid staffId, DayOfWeek day, TimeSpan start, TimeSpan end)
    {
        await using var context = Database.CreateContext();
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
        await using var context = Database.CreateContext();
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
        await using var context = Database.CreateContext();
        var id = await StaffingSeed.BlockedTimeAsync(context, staffId, startsAt, endsAt, type, reason);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedCategoryAsync(string name)
    {
        await using var context = Database.CreateContext();
        var id = await CatalogueSeed.CategoryAsync(context, name);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedServiceAsync(
        Guid categoryId,
        string name,
        int durationMinutes = 60,
        decimal price = 100m,
        IReadOnlyList<Guid>? staffIds = null)
    {
        await using var context = Database.CreateContext();
        var id = await CatalogueSeed.ServiceAsync(
            context, categoryId, name, price, durationMinutes, staffIds: staffIds);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedCustomerAsync(
        string firstName,
        string lastName,
        string? phoneNumber = null,
        DateOnly? dateOfBirth = null,
        bool isActive = true)
    {
        await using var context = Database.CreateContext();
        var id = await CustomerSeed.CustomerAsync(
            context, firstName, lastName, phoneNumber, dateOfBirth: dateOfBirth, isActive: isActive);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedNoteAsync(Guid customerId, string note)
    {
        await using var context = Database.CreateContext();
        var id = await CustomerSeed.NoteAsync(context, customerId, note);
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
        await using var context = Database.CreateContext();
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
