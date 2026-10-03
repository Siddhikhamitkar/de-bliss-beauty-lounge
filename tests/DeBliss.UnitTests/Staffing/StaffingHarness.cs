using DeBliss.Application.Staffing;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;
using StaffManagementService = DeBliss.Application.Staffing.StaffService;

namespace DeBliss.UnitTests.Staffing;

/// <summary>
/// Wires the real staffing services to the real repositories over a throwaway
/// SQLite database. One context for the whole harness mirrors one web request,
/// where DI hands the repositories and the unit of work the same scoped DbContext.
/// </summary>
internal sealed class StaffingHarness : IDisposable
{
    public StaffingHarness()
    {
        Database = new SqliteTestDatabase();
        Context = Database.CreateContext();

        var staffRepository = new StaffRepository(Context);
        var serviceRepository = new ServiceRepository(Context);
        var scheduleRepository = new ScheduleRepository(Context);

        Staff = new StaffManagementService(staffRepository, serviceRepository, Context);
        Schedule = new StaffScheduleService(staffRepository, scheduleRepository, Database.Clock, Context);
        Availability = new StaffAvailabilityService(staffRepository, serviceRepository, scheduleRepository);
    }

    public SqliteTestDatabase Database { get; }

    public DeBlissDbContext Context { get; }

    public IStaffService Staff { get; }

    public IStaffScheduleService Schedule { get; }

    public IStaffAvailabilityService Availability { get; }

    /// <summary>A separate context, so assertions read what was actually written.</summary>
    public DeBlissDbContext Reopen() => Database.CreateContext();

    public async Task<Guid> SeedStaffAsync(
        string firstName,
        string lastName,
        bool isActive = true,
        string? jobTitle = null,
        string? email = null,
        string? phoneNumber = null,
        string? calendarColor = null,
        int sortOrder = 0)
    {
        await using var context = Reopen();
        var id = await StaffingSeed.StaffAsync(
            context,
            firstName,
            lastName,
            isActive,
            jobTitle,
            email,
            phoneNumber,
            calendarColor,
            sortOrder);
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
        bool isActive = true,
        IReadOnlyList<Guid>? staffIds = null)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.ServiceAsync(
            context, categoryId, name, 40m, isActive: isActive, staffIds: staffIds);
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
        BlockedTimeType type = BlockedTimeType.Break)
    {
        await using var context = Reopen();
        var id = await StaffingSeed.BlockedTimeAsync(context, staffId, startsAt, endsAt, type);
        Context.ChangeTracker.Clear();
        return id;
    }

    public void Dispose()
    {
        Context.Dispose();
        Database.Dispose();
    }
}
