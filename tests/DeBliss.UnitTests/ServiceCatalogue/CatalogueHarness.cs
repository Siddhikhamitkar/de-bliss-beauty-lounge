using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.ServiceCatalogue;

/// <summary>
/// Wires the real application services to the real repositories over a throwaway
/// SQLite database. One context for the whole harness mirrors one web request,
/// where DI hands the repositories and the unit of work the same scoped DbContext.
/// </summary>
internal sealed class CatalogueHarness : IDisposable
{
    public CatalogueHarness()
    {
        Database = new SqliteTestDatabase();
        Context = Database.CreateContext();

        var categoryRepository = new ServiceCategoryRepository(Context);

        Categories = new ServiceCategoryService(categoryRepository, Context);
        Catalogue = new ServiceCatalogueService(
            new ServiceRepository(Context),
            new ServiceBundleRepository(Context),
            categoryRepository,
            new StaffDirectory(Context),
            Context);
        Bundles = new ServiceBundleService(
            new ServiceBundleRepository(Context),
            new ServiceRepository(Context),
            categoryRepository,
            Context);
    }

    public SqliteTestDatabase Database { get; }

    public DeBlissDbContext Context { get; }

    public IServiceCategoryService Categories { get; }

    public IServiceCatalogueService Catalogue { get; }

    public IServiceBundleService Bundles { get; }

    public IUnitOfWork UnitOfWork => Context;

    /// <summary>A separate context, so assertions read what was actually written.</summary>
    public DeBlissDbContext Reopen() => Database.CreateContext();

    public async Task<Guid> SeedStaffAsync(string firstName, string lastName, bool isActive = true)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.StaffAsync(context, firstName, lastName, isActive);
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
        decimal price,
        int durationMinutes = 30,
        bool isActive = true,
        string? description = null,
        IReadOnlyList<Guid>? staffIds = null,
        int sortOrder = 0,
        bool isOnlineBookable = false)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.ServiceAsync(
            context, categoryId, name, price, durationMinutes, isActive, description, staffIds, sortOrder, isOnlineBookable);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedBundleAsync(
        Guid categoryId,
        string name,
        decimal price,
        IReadOnlyList<Guid> serviceIds,
        bool isActive = true,
        string? description = null,
        int sortOrder = 0)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.BundleAsync(
            context, categoryId, name, price, serviceIds, isActive, description, sortOrder);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedBookedServiceAsync(
        Guid serviceId,
        string snapshotName,
        decimal unitPrice,
        int durationMinutes)
    {
        await using var context = Reopen();
        var id = await CatalogueSeed.BookedServiceAsync(context, serviceId, snapshotName, unitPrice, durationMinutes);
        Context.ChangeTracker.Clear();
        return id;
    }

    public void Dispose()
    {
        Context.Dispose();
        Database.Dispose();
    }
}
