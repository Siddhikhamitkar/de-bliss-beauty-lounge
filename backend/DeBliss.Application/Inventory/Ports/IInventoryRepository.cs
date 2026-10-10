using DeBliss.Application.Inventory.Dtos;
using DeBliss.Domain.Inventory;

namespace DeBliss.Application.Inventory.Ports;

/// <summary>
/// Inventory persistence. GetByIdAsync returns a tracked product because the
/// application service mutates what it loads — and every stock change it makes
/// goes through <see cref="InventoryProduct.ApplyStockChange"/>, which bumps the
/// concurrency token that keeps two users from selling the same units twice.
/// Nothing here deletes: products are retired by deactivation and movements are
/// append-only.
/// </summary>
public interface IInventoryRepository
{
    /// <summary>Tracked product or null.</summary>
    Task<InventoryProduct?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked products for the given ids, so checkout can deduct them in one save. Missing ids are simply absent.</summary>
    Task<IReadOnlyList<InventoryProduct>> GetTrackedByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>True when another product already uses the SKU; excludeId skips the product being edited.</summary>
    Task<bool> SkuExistsAsync(string normalizedSku, Guid? excludeId, CancellationToken ct = default);

    Task<(IReadOnlyList<InventoryProduct> Items, int TotalCount)> SearchAsync(InventoryQuery query, CancellationToken ct = default);

    /// <summary>The active retail products the checkout screen can pick from, ordered by name.</summary>
    Task<IReadOnlyList<InventoryProduct>> ListSellableAsync(string? search, CancellationToken ct = default);

    /// <summary>
    /// For each product given, the most recent movement that added stock (a
    /// delivery or an opening count) — the pair the inventory list shows as
    /// "last added". Products that never received stock are absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, StockMovement>> GetLatestStockAdditionsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken ct = default);

    Task<(IReadOnlyList<StockMovement> Items, int TotalCount)> SearchMovementsAsync(InventoryMovementQuery query, CancellationToken ct = default);

    /// <summary>One movement of one product, read-only, with its invoice and performer included.</summary>
    Task<StockMovement?> GetMovementAsync(Guid productId, Guid movementId, CancellationToken ct = default);

    /// <summary>True when some reversal movement already points back at the given movement.</summary>
    Task<bool> IsReversedAsync(Guid movementId, CancellationToken ct = default);

    void Add(InventoryProduct product);

    void AddMovement(StockMovement movement);
}
