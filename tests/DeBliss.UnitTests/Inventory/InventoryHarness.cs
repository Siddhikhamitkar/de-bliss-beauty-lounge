using DeBliss.Application.Inventory;
using DeBliss.Domain.Inventory;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.Inventory;

/// <summary>
/// Wires the real inventory service to the real repository over a throwaway
/// SQLite database, mirroring one web request where DI hands the repository and
/// the unit of work the same scoped DbContext.
/// </summary>
internal sealed class InventoryHarness : IDisposable
{
    public InventoryHarness(SqliteTestDatabase? database = null)
    {
        Database = database ?? new SqliteTestDatabase();
        Context = Database.CreateContext();

        Inventory = new InventoryService(
            new InventoryRepository(Context),
            Context,
            Database.Clock);
    }

    public SqliteTestDatabase Database { get; }

    public DeBlissDbContext Context { get; }

    public IInventoryService Inventory { get; }

    /// <summary>A separate context, so assertions read what was actually written.</summary>
    public DeBlissDbContext Reopen() => Database.CreateContext();

    public async Task<Guid> SeedProductAsync(
        string name,
        string sku,
        InventoryType inventoryType = InventoryType.Retail,
        string? brand = null,
        string? category = null,
        decimal? costPrice = null,
        decimal? sellingPrice = null,
        decimal currentStock = 0m,
        decimal minimumStock = 0m,
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

    public async Task<Guid> SeedMovementAsync(
        Guid productId,
        StockMovementType movementType,
        decimal quantityChange,
        decimal stockBefore,
        DateTimeOffset occurredAt,
        string? reference = null,
        string? notes = null,
        Guid? invoiceId = null,
        Guid? performedByUserId = null,
        Guid? reversalOfMovementId = null)
    {
        await using var context = Reopen();
        var id = await InventorySeed.MovementAsync(
            context, productId, movementType, quantityChange, stockBefore, occurredAt,
            reference, notes, invoiceId, performedByUserId, reversalOfMovementId);
        Context.ChangeTracker.Clear();
        return id;
    }

    public void Dispose()
    {
        Context.Dispose();
        Database.Dispose();
    }
}
