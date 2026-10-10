using DeBliss.Application.Inventory.Dtos;
using DeBliss.Application.Inventory.Ports;
using DeBliss.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Inventory persistence. GetByIdAsync and GetTrackedByIdAsync return tracked
/// products because the application service mutates stock through
/// <see cref="InventoryProduct.ApplyStockChange"/>, which is also what bumps the
/// concurrency token EF turns into an optimistic-concurrency guard. The list and
/// movement queries do not track. Nothing deletes: products are retired by
/// deactivation and movements are append-only.
/// </summary>
public sealed class InventoryRepository(DeBlissDbContext context) : IInventoryRepository
{
    public Task<InventoryProduct?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return context.InventoryProducts.FirstOrDefaultAsync(product => product.Id == id, ct);
    }

    public async Task<IReadOnlyList<InventoryProduct>> GetTrackedByIdAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default)
    {
        return await context.InventoryProducts
            .Where(product => ids.Contains(product.Id))
            .ToListAsync(ct);
    }

    public async Task<bool> SkuExistsAsync(string normalizedSku, Guid? excludeId, CancellationToken ct = default)
    {
        var matches = context.InventoryProducts
            .AsNoTracking()
            .Where(product => product.Sku == normalizedSku);

        if (excludeId is { } id)
        {
            matches = matches.Where(product => product.Id != id);
        }

        return await matches.AnyAsync(ct);
    }

    public async Task<(IReadOnlyList<InventoryProduct> Items, int TotalCount)> SearchAsync(
        InventoryQuery query,
        CancellationToken ct = default)
    {
        var filtered = ApplyFilters(context.InventoryProducts.AsNoTracking(), query);

        var totalCount = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Sku)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<InventoryProduct>> ListSellableAsync(string? search, CancellationToken ct = default)
    {
        var query = context.InventoryProducts
            .AsNoTracking()
            .Where(product => product.IsActive && product.InventoryType == InventoryType.Retail);

        if (search is { Length: > 0 })
        {
            var lowered = search.ToLowerInvariant();

            query = query.Where(product =>
                product.Name.ToLower().Contains(lowered)
                || (product.Brand != null && product.Brand.ToLower().Contains(lowered))
                || product.Sku.ToLower().Contains(lowered));
        }

        return await query
            .OrderBy(product => product.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, StockMovement>> GetLatestStockAdditionsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken ct = default)
    {
        // Only deliveries and opening counts are "stock added": reductions and
        // corrections are different movement types, so they can never displace
        // the receipt the list is meant to show.
        var additions = await context.StockMovements
            .AsNoTracking()
            .Where(movement => productIds.Contains(movement.ProductId)
                && (movement.MovementType == StockMovementType.StockReceived
                    || movement.MovementType == StockMovementType.OpeningStock))
            .ToListAsync(ct);

        // SQLite cannot order DateTimeOffset columns; one page of products
        // carries few receipts, so latest-per-product is chosen in memory.
        return additions
            .GroupBy(movement => movement.ProductId)
            .ToDictionary(
                grouping => grouping.Key,
                grouping => grouping
                    .OrderByDescending(movement => movement.OccurredAt)
                    .ThenByDescending(movement => movement.CreatedAt)
                    .First());
    }

    public async Task<(IReadOnlyList<StockMovement> Items, int TotalCount)> SearchMovementsAsync(
        InventoryMovementQuery query,
        CancellationToken ct = default)
    {
        var filtered = context.StockMovements
            .AsNoTracking()
            .Where(movement => movement.ProductId == query.ProductId);

        if (query.Type != StockMovementTypeFilter.All)
        {
            var movementType = (StockMovementType)(int)query.Type;

            filtered = filtered.Where(movement => movement.MovementType == movementType);
        }

        var totalCount = await filtered.CountAsync(ct);

        // SQLite cannot order DateTimeOffset columns; a product's history is
        // small, so newest-first and paging are applied in memory.
        var items = await filtered
            .Include(movement => movement.Invoice)
            .Include(movement => movement.PerformedByUser)
            .ToListAsync(ct);

        var page = items
            .OrderByDescending(movement => movement.OccurredAt)
            .ThenByDescending(movement => movement.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return (page, totalCount);
    }

    public Task<StockMovement?> GetMovementAsync(Guid productId, Guid movementId, CancellationToken ct = default)
    {
        return context.StockMovements
            .AsNoTracking()
            .Include(movement => movement.Invoice)
            .Include(movement => movement.PerformedByUser)
            .FirstOrDefaultAsync(movement => movement.Id == movementId && movement.ProductId == productId, ct);
    }

    public Task<bool> IsReversedAsync(Guid movementId, CancellationToken ct = default)
    {
        return context.StockMovements
            .AsNoTracking()
            .AnyAsync(movement => movement.ReversalOfMovementId == movementId, ct);
    }

    public void Add(InventoryProduct product) => context.InventoryProducts.Add(product);

    public void AddMovement(StockMovement movement) => context.StockMovements.Add(movement);

    private static IQueryable<InventoryProduct> ApplyFilters(IQueryable<InventoryProduct> query, InventoryQuery normalized)
    {
        if (normalized.Search is { Length: > 0 } term)
        {
            var lowered = term.ToLowerInvariant();

            query = query.Where(product =>
                product.Name.ToLower().Contains(lowered)
                || (product.Brand != null && product.Brand.ToLower().Contains(lowered))
                || product.Sku.ToLower().Contains(lowered));
        }

        if (normalized.Category is { Length: > 0 } category)
        {
            var lowered = category.ToLowerInvariant();

            query = query.Where(product => product.Category != null && product.Category.ToLower() == lowered);
        }

        query = normalized.Type switch
        {
            InventoryTypeFilter.Retail => query.Where(product => product.InventoryType == InventoryType.Retail),
            InventoryTypeFilter.SalonUse => query.Where(product => product.InventoryType == InventoryType.SalonUse),
            _ => query
        };

        query = normalized.Status switch
        {
            InventoryProductStatusFilter.Active => query.Where(product => product.IsActive),
            InventoryProductStatusFilter.Inactive => query.Where(product => !product.IsActive),
            _ => query
        };

        // The three states mirror the DTO's StockStatus exactly, so what the
        // filter selects is what the pill shows.
        return normalized.StockStatus switch
        {
            InventoryStockStatusFilter.InStock => query.Where(product => product.CurrentStock > product.MinimumStock),
            InventoryStockStatusFilter.LowStock => query.Where(product =>
                product.CurrentStock > 0m && product.CurrentStock <= product.MinimumStock),
            InventoryStockStatusFilter.OutOfStock => query.Where(product => product.CurrentStock <= 0m),
            _ => query
        };
    }
}
