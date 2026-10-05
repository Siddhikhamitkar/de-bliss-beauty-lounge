using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Ports;
using DeBliss.Application.Scheduling;
using DeBliss.Application.Staffing;
using DeBliss.Domain.Notifications;
using DeBliss.Domain.Scheduling;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using BookingService = DeBliss.Application.Scheduling.AppointmentService;

namespace DeBliss.UnitTests.Reminders;

/// <summary>
/// Wires the reminder engine to the real booking service and the real
/// repositories over a throwaway SQLite database. The shared context mirrors
/// one web request — the booking service, the settings service and their
/// repositories all share it — while <see cref="DispatchAsync"/> builds a
/// fresh context per call, exactly as the hosted worker opens one scope per
/// tick, so a test can book and dispatch without leaking tracked state
/// between the two.
/// </summary>
internal sealed class RemindersHarness : IDisposable
{
    public const string SalonName = "DeBliss";
    public const string WorkerId = "test-worker";

    public RemindersHarness()
    {
        Database = new SqliteTestDatabase();
        Context = Database.CreateContext();

        var appointmentRepository = new AppointmentRepository(Context);
        var customerRepository = new CustomerRepository(Context);
        var serviceRepository = new ServiceRepository(Context);
        var bundleRepository = new ServiceBundleRepository(Context);
        var staffRepository = new StaffRepository(Context);
        var scheduleRepository = new ScheduleRepository(Context);
        var reminderRepository = new AppointmentReminderRepository(Context);
        var ruleRepository = new ReminderRuleRepository(Context);

        var availability = new StaffAvailabilityService(
            staffRepository, serviceRepository, scheduleRepository, appointmentRepository);

        var planner = new ReminderPlanner(ruleRepository, reminderRepository, Database.Clock);

        Appointments = new BookingService(
            appointmentRepository,
            customerRepository,
            serviceRepository,
            bundleRepository,
            staffRepository,
            availability,
            planner,
            Context,
            Database.Clock);

        Composer = new ReminderMessageComposer(SalonName, Database.Clock);
        Provider = new FakeNotificationProvider();

        Settings = new ReminderSettingsService(
            ruleRepository, Provider, Composer, Context, NullLogger<ReminderSettingsService>.Instance);

        History = new ReminderHistoryService(reminderRepository);

        // In-memory SQLite, so the sync-over-async seed cannot deadlock; xUnit
        // constructors cannot await. Tests that need a different rule set
        // replace it before booking.
        ReminderSeed.DefaultRulesAsync(Context).GetAwaiter().GetResult();
    }

    public SqliteTestDatabase Database { get; }

    public FakeTimeProvider Clock => Database.Clock;

    public DeBlissDbContext Context { get; }

    public IAppointmentService Appointments { get; }

    public FakeNotificationProvider Provider { get; }

    public ReminderMessageComposer Composer { get; }

    public IReminderSettingsService Settings { get; }

    public IReminderHistoryService History { get; }

    /// <summary>
    /// One dispatch cycle on its own context, like one worker tick in
    /// production. Call it again for the next cycle; the provider records
    /// every attempt across calls.
    /// </summary>
    public async Task<ReminderDispatchResult> DispatchAsync(string workerId = WorkerId)
    {
        await using var context = Database.CreateContext();

        var dispatcher = new ReminderDispatchService(
            new AppointmentReminderRepository(context),
            Provider,
            Composer,
            new ReminderWorkerIdentity(workerId),
            context,
            Database.Clock,
            NullLogger<ReminderDispatchService>.Instance);

        return await dispatcher.ProcessDueAsync();
    }

    /// <summary>A separate context, so assertions read what was actually written.</summary>
    public DeBlissDbContext Reopen() => Database.CreateContext();

    public async Task<Guid> SeedCustomerAsync(
        string firstName = "Sara",
        string lastName = "Haddad",
        string? phoneNumber = "+971 50 000 0001",
        WhatsAppOptInStatus optInStatus = WhatsAppOptInStatus.OptedIn)
    {
        await using var context = Reopen();
        var id = await CustomerSeed.CustomerAsync(context, firstName, lastName, phoneNumber);
        await ReminderSeed.SetOptInAsync(context, id, optInStatus);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedCategoryAsync(string name, int sortOrder = 0)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.CategoryAsync(context, name, sortOrder: sortOrder);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedServiceAsync(
        Guid categoryId,
        string name,
        int durationMinutes = 30,
        decimal price = 100m,
        IReadOnlyList<Guid>? staffIds = null)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.ServiceAsync(
            context, categoryId, name, price, durationMinutes, staffIds: staffIds);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedBundleAsync(
        Guid categoryId,
        string name,
        decimal price,
        IReadOnlyList<Guid> serviceIds)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.BundleAsync(context, categoryId, name, price, serviceIds);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedStaffAsync(string firstName, string lastName)
    {
        await using var context = Reopen();
        var id = await StaffingSeed.StaffAsync(context, firstName, lastName);
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

    public async Task<Guid> SeedAppointmentAsync(
        Guid? customerId,
        DateTimeOffset startsAt,
        AppointmentStatus status = AppointmentStatus.Confirmed,
        string? walkInCustomerName = null,
        IReadOnlyList<SchedulingSeed.Line>? lines = null)
    {
        await using var context = Reopen();
        var id = await SchedulingSeed.AppointmentAsync(
            context, startsAt, startsAt.AddHours(1), status, customerId, walkInCustomerName, lines: lines);
        Context.ChangeTracker.Clear();
        return id;
    }

    /// <summary>A reminder row in any status, for dispatcher tests that start mid-lifecycle.</summary>
    public Task<Guid> SeedReminderAsync(
        Guid appointmentId,
        Guid customerId,
        ReminderType reminderType,
        DateTimeOffset scheduledAt,
        ReminderStatus status = ReminderStatus.Pending,
        int attemptCount = 0,
        DateTimeOffset? sentAt = null,
        DateTimeOffset? lastAttemptAt = null,
        string? providerMessageId = null,
        string? failureReason = null,
        DateTimeOffset? nextAttemptAt = null,
        string? claimedBy = null,
        DateTimeOffset? claimedAt = null) =>
        WithNewContextAsync(context => ReminderSeed.ReminderAsync(
            context, appointmentId, customerId, reminderType, scheduledAt, status,
            attemptCount: attemptCount,
            sentAt: sentAt,
            lastAttemptAt: lastAttemptAt,
            providerMessageId: providerMessageId,
            failureReason: failureReason,
            nextAttemptAt: nextAttemptAt,
            claimedBy: claimedBy,
            claimedAt: claimedAt));

    public async Task ReplaceRulesAsync(
        params (ReminderType Type, int OffsetMinutes, bool IsEnabled)[] rules)
    {
        await using var context = Reopen();
        await ReminderSeed.ReplaceRulesAsync(context, rules);
        Context.ChangeTracker.Clear();
    }

    private async Task<Guid> WithNewContextAsync(Func<DeBlissDbContext, Task<Guid>> seed)
    {
        await using var context = Reopen();
        var id = await seed(context);
        Context.ChangeTracker.Clear();
        return id;
    }

    public void Dispose()
    {
        Context.Dispose();
        Database.Dispose();
    }
}
