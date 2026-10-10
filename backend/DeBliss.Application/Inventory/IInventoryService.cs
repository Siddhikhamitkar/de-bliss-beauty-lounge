using DeBliss.Application.Common.Paging;
using DeBliss.Application.Inventory.Dtos;

namespace DeBliss.Application.Inventory;

public interface IInventoryService
{
    Task<PagedResult<InventoryProductDto>> SearchAsync(InventoryQuery query, CancellationToken cancellationToken = default);

    Task<InventoryProductDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryProductOptionDto>> GetSellableAsync(string? search, CancellationToken cancellationToken = default);

    Task<InventoryProductDto> CreateAsync(SaveProductRequest request, CancellationToken cancellationToken = default);

    Task<InventoryProductDto> UpdateAsync(Guid id, SaveProductRequest request, CancellationToken cancellationToken = default);

    Task<InventoryProductDto> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records stock physically arriving (or an explicit opening count). Each
    /// call is its own append-only movement — history is never overwritten, and
    /// the receipt that added stock stays the product's "last added" even after
    /// later reductions.
    /// </summary>
    Task<StockMovementDto> ReceiveStockAsync(
        Guid productId,
        ReceiveStockRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Records salon-use stock consumed in service or lost to wastage, with the reason the history needs.</summary>
    Task<StockMovementDto> RecordUsageAsync(
        Guid productId,
        RecordUsageRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Applies a signed correction, such as reconciling to a physical count.</summary>
    Task<StockMovementDto> AdjustStockAsync(
        Guid productId,
        AdjustStockRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a mistaken movement with an audited reversal. The original row is
    /// never edited; each movement can be reversed once, ever.
    /// </summary>
    Task<StockMovementDto> ReverseMovementAsync(
        Guid productId,
        Guid movementId,
        ReverseStockMovementRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<StockMovementDto>> GetMovementsAsync(
        Guid productId,
        InventoryMovementQuery query,
        CancellationToken cancellationToken = default);
}
