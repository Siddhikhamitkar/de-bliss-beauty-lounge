using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Customers;
using DeBliss.Application.Identity;
using DeBliss.Application.Imports;
using DeBliss.Application.Inventory;
using DeBliss.Domain.Inventory;
using DeBliss.Infrastructure.Imports;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Imports;

/// <summary>
/// Wires the real import services to the real repositories and domain services
/// over a throwaway SQLite database, mirroring one web request where DI hands
/// the repositories, the domain services and the unit of work the same scoped
/// DbContext. The file port is the real ClosedXML adapter, so templates,
/// parsing and error reports are exercised as real Excel files.
/// </summary>
internal sealed class ImportHarness : IDisposable
{
    private readonly CustomerRepository _customerRepository;

    private readonly InventoryRepository _inventoryRepository;

    public ImportHarness()
    {
        Database = new SqliteTestDatabase();
        Context = Database.CreateContext();

        _customerRepository = new CustomerRepository(Context);
        _inventoryRepository = new InventoryRepository(Context);
        Files = new ClosedXmlImportFiles();

        Customers = new CustomerService(_customerRepository, Context, Database.Clock);
        Inventory = new InventoryService(_inventoryRepository, Context, Database.Clock);

        CustomerImports = new CustomerImportService(Files, _customerRepository, Customers, Context, Database.Clock);
        InventoryImports = new InventoryImportService(Files, _inventoryRepository, Inventory, Context);

        // The opening-stock movement carries a real FK to Users, so the acting
        // user is a seeded account, exactly as it is for a signed-in request.
        Actor = IdentitySeed
            .UserAsync(Database.CreateContext(), "importer@debliss.test", "Test#Owner123", RoleNames.Owner)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>The acting user the import attributes stock movements to — a seeded account, since movements carry a real FK to Users.</summary>
    public Guid Actor { get; }

    public SqliteTestDatabase Database { get; }

    public DeBlissDbContext Context { get; }

    public ClosedXmlImportFiles Files { get; }

    public ICustomerService Customers { get; }

    public IInventoryService Inventory { get; }

    public ICustomerImportService CustomerImports { get; }

    public IInventoryImportService InventoryImports { get; }

    /// <summary>A separate context, so assertions read what was actually written.</summary>
    public DeBlissDbContext Reopen() => Database.CreateContext();

    public Stream Stream(byte[] content) => new MemoryStream(content);

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

    public async Task<Guid> SeedProductAsync(
        string name,
        string sku,
        InventoryType inventoryType = InventoryType.Retail,
        decimal? costPrice = null,
        decimal? sellingPrice = null,
        decimal currentStock = 0m,
        decimal minimumStock = 0m,
        string? brand = null,
        string? category = null,
        string unitOfMeasure = "pcs",
        bool isActive = true)
    {
        await using var context = Reopen();
        var id = await InventorySeed.ProductAsync(
            context, name, sku, inventoryType, brand, category,
            costPrice, sellingPrice, currentStock, minimumStock, unitOfMeasure, isActive);
        Context.ChangeTracker.Clear();

        return id;
    }

    public async Task<int> CustomerCountAsync()
    {
        await using var context = Reopen();

        return await context.Customers.CountAsync();
    }

    public async Task<int> ProductCountAsync()
    {
        await using var context = Reopen();

        return await context.InventoryProducts.CountAsync();
    }

    public async Task<int> MovementCountAsync()
    {
        await using var context = Reopen();

        return await context.StockMovements.CountAsync();
    }

    public async Task<int> NoteCountAsync()
    {
        await using var context = Reopen();

        return await context.CustomerNotes.CountAsync();
    }

    public void Dispose()
    {
        Context.Dispose();
        Database.Dispose();
    }
}

/// <summary>
/// Opens the real serializable transaction, then fires the caller's token — a
/// deterministic stand-in for a database failure between the first write and
/// the commit, which is the one path the import's catch-all rollback exists for.
/// </summary>
internal sealed class FailingAfterBeginUnitOfWork(DeBlissDbContext context, CancellationTokenSource source) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await context.BeginTransactionAsync(CancellationToken.None);

        await source.CancelAsync();

        return transaction;
    }
}
