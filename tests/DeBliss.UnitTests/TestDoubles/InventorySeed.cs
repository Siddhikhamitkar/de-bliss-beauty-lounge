using DeBliss.Domain.Inventory;
using DeBliss.Infrastructure.Persistence;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// Writes inventory rows straight to the database. Product writes never create
/// stock — only an explicit movement does, mirroring the rule that the system
/// never invents an opening quantity.
/// </summary>
internal static class InventorySeed
{
    public static async Task<Guid> ProductAsync(
        DeBlissDbContext context,
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
        var product = new InventoryProduct
        {
            Id = Guid.NewGuid(),
            Name = name,
            Sku = sku,
            InventoryType = inventoryType,
            Brand = brand,
            Category = category,
            CostPrice = costPrice,
            SellingPrice = sellingPrice,
            CurrentStock = currentStock,
            MinimumStock = minimumStock,
            UnitOfMeasure = unitOfMeasure,
            IsActive = isActive,
            StockVersion = Guid.NewGuid()
        };

        context.InventoryProducts.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    public static async Task<Guid> MovementAsync(
        DeBlissDbContext context,
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
        var movement = new StockMovement
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            MovementType = movementType,
            QuantityChange = quantityChange,
            StockBefore = stockBefore,
            StockAfter = stockBefore + quantityChange,
            OccurredAt = occurredAt,
            Reference = reference,
            Notes = notes,
            InvoiceId = invoiceId,
            PerformedByUserId = performedByUserId,
            ReversalOfMovementId = reversalOfMovementId
        };

        context.StockMovements.Add(movement);
        await context.SaveChangesAsync();

        return movement.Id;
    }
}
