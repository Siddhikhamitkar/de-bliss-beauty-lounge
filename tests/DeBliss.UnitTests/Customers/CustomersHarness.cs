using DeBliss.Application.Customers;
using DeBliss.Domain.Scheduling;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.Customers;

/// <summary>
/// Wires the real customers service to the real repository over a throwaway
/// SQLite database. One context for the whole harness mirrors one web request,
/// where DI hands the repository and the unit of work the same scoped DbContext.
/// </summary>
internal sealed class CustomersHarness : IDisposable
{
    public CustomersHarness()
    {
        Database = new SqliteTestDatabase();
        Context = Database.CreateContext();

        var repository = new CustomerRepository(Context);

        Customers = new CustomerService(repository, Context, Database.Clock);
    }

    public SqliteTestDatabase Database { get; }

    public DeBlissDbContext Context { get; }

    public ICustomerService Customers { get; }

    /// <summary>A separate context, so assertions read what was actually written.</summary>
    public DeBlissDbContext Reopen() => Database.CreateContext();

    public async Task<Guid> SeedCustomerAsync(
        string firstName,
        string lastName,
        string? phoneNumber = null,
        string? email = null,
        bool isActive = true,
        DateOnly? dateOfBirth = null,
        string? gender = null,
        string? address = null)
    {
        await using var context = Reopen();
        var id = await CustomerSeed.CustomerAsync(
            context, firstName, lastName, phoneNumber, email, isActive, dateOfBirth, gender, address);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedNoteAsync(Guid customerId, string note)
    {
        await using var context = Reopen();
        var id = await CustomerSeed.NoteAsync(context, customerId, note);
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

    public async Task<Guid> SeedServiceAsync(Guid categoryId, string name, bool isActive = true)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.ServiceAsync(context, categoryId, name, 100m, isActive: isActive);
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

    public async Task<Guid> SeedAppointmentAsync(
        Guid customerId,
        DateTimeOffset startsAt,
        AppointmentStatus status = AppointmentStatus.Completed,
        IReadOnlyList<(Guid ServiceId, Guid? StaffId, string SnapshotName)>? lines = null)
    {
        await using var context = Reopen();
        var id = await CustomerSeed.AppointmentAsync(context, customerId, startsAt, status, lines);
        Context.ChangeTracker.Clear();
        return id;
    }

    public void Dispose()
    {
        Context.Dispose();
        Database.Dispose();
    }
}
