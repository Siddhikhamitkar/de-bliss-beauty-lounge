using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Inventory;
using DeBliss.Application.Inventory.Dtos;
using DeBliss.Domain.Inventory;
using DeBliss.UnitTests.Inventory;

namespace DeBliss.UnitTests.Inventory;

public class InventoryServiceTests
{
    private static readonly DateTimeOffset ClockNow = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_new_product_starts_with_zero_stock_and_no_received_history()
    {
        using var harness = new InventoryHarness();

        var product = await harness.Inventory.CreateAsync(new SaveProductRequest(
            Name: "L'Oréal Colour 1.0",
            Brand: "L'Oréal",
            Sku: " lc-100 ",
            Category: "Hair Colour",
            InventoryType: "SalonUse",
            UnitOfMeasure: null,
            CostPrice: null,
            SellingPrice: null,
            MinimumStock: 2m,
            IsActive: null));

        Assert.Equal(0m, product.CurrentStock);
        Assert.Equal("LC-100", product.Sku);
        Assert.Equal("pcs", product.UnitOfMeasure);
        Assert.Null(product.LastStockAddedAt);
        Assert.Null(product.LastAddedQuantity);
        Assert.Equal("OutOfStock", product.StockStatus);
    }

    [Fact]
    public async Task A_retail_product_requires_cost_and_selling_price()
    {
        using var harness = new InventoryHarness();

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Inventory.CreateAsync(new SaveProductRequest(
                "Shampoo", null, "SH-1", null, "Retail", null, null, null, null, null)));

        Assert.Contains("costPrice", error.Errors.Keys);
        Assert.Contains("sellingPrice", error.Errors.Keys);
    }

    [Fact]
    public async Task A_salon_use_product_cannot_carry_prices()
    {
        using var harness = new InventoryHarness();

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Inventory.CreateAsync(new SaveProductRequest(
                "Toner", null, "TN-1", null, "SalonUse", null, 10m, 25m, null, null)));

        Assert.Contains("sellingPrice", error.Errors.Keys);
    }

    [Fact]
    public async Task A_sku_exists_only_once()
    {
        using var harness = new InventoryHarness();
        await harness.SeedProductAsync("Mask", "MK-1");

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            harness.Inventory.CreateAsync(new SaveProductRequest(
                "Mask Two", null, "mk-1", null, "Retail", null, 10m, 25m, null, null)));

        Assert.Contains("already exists", conflict.Message);
    }

    [Fact]
    public async Task Receiving_stock_sets_current_stock_and_the_last_added_pair()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("L'Oréal Colour 1.0", "LC-100", InventoryType.SalonUse);

        await harness.Inventory.ReceiveStockAsync(
            productId,
            new ReceiveStockRequest(Quantity: 2m, ReceivedAt: null, Reference: "PO-1", Notes: null, Kind: null),
            actorUserId: null);

        var product = await harness.Inventory.GetAsync(productId);

        Assert.Equal(2m, product.CurrentStock);
        Assert.NotNull(product.LastStockAddedAt);
        Assert.Equal(2m, product.LastAddedQuantity);
    }

    [Fact]
    public async Task A_zero_or_negative_receipt_is_rejected()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Mask", "MK-1", InventoryType.SalonUse);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Inventory.ReceiveStockAsync(
                productId, new ReceiveStockRequest(0m, null, null, null, null), null));

        Assert.Contains("quantity", error.Errors.Keys);
    }

    [Fact]
    public async Task A_future_receipt_date_is_rejected()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Mask", "MK-1", InventoryType.SalonUse);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Inventory.ReceiveStockAsync(
                productId,
                new ReceiveStockRequest(1m, ClockNow.AddDays(1), null, null, null),
                null));

        Assert.Contains("receivedAt", error.Errors.Keys);
    }

    [Fact]
    public async Task Multiple_receipts_each_keep_their_own_movement_and_the_list_shows_the_latest()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("L'Oréal Colour 1.0", "LC-100", InventoryType.SalonUse);

        await harness.Inventory.ReceiveStockAsync(
            productId, new ReceiveStockRequest(3m, ClockNow.AddDays(-2), "PO-1", null, null), null);
        await harness.Inventory.ReceiveStockAsync(
            productId, new ReceiveStockRequest(2m, ClockNow.AddDays(-1), "PO-2", null, null), null);

        var product = await harness.Inventory.GetAsync(productId);
        var movements = await harness.Inventory.GetMovementsAsync(
            productId, new InventoryMovementQuery(productId, StockMovementTypeFilter.All, 1, 50));

        Assert.Equal(5m, product.CurrentStock);
        Assert.Equal(2m, product.LastAddedQuantity);
        Assert.Equal(ClockNow.AddDays(-1), product.LastStockAddedAt);
        Assert.Equal(2, movements.TotalCount);
    }

    [Fact]
    public async Task Reductions_never_overwrite_the_last_receipt()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("L'Oréal Colour 1.0", "LC-100", InventoryType.SalonUse, currentStock: 5m);

        await harness.SeedMovementAsync(
            productId, StockMovementType.StockReceived, 5m, 0m, ClockNow.AddDays(-3));
        await harness.Inventory.RecordUsageAsync(
            productId, new RecordUsageRequest(2m, "Consumption", null, "Balayage service"), null);

        var product = await harness.Inventory.GetAsync(productId);

        // Stock fell, but "last added" still describes the receipt, not the usage.
        Assert.Equal(3m, product.CurrentStock);
        Assert.Equal(5m, product.LastAddedQuantity);
    }

    [Fact]
    public async Task A_product_with_no_receipt_shows_no_stock_received_yet()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Mask", "MK-1", InventoryType.SalonUse, currentStock: 4m);

        // Only a reduction exists, never a receipt.
        await harness.SeedMovementAsync(
            productId, StockMovementType.Consumption, -1m, 5m, ClockNow);

        var product = await harness.Inventory.GetAsync(productId);

        Assert.Null(product.LastStockAddedAt);
        Assert.Null(product.LastAddedQuantity);
    }

    [Fact]
    public async Task Usage_records_the_reason_and_reduces_stock()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Toner", "TN-1", InventoryType.SalonUse, currentStock: 10m);

        var movement = await harness.Inventory.RecordUsageAsync(
            productId, new RecordUsageRequest(2.5m, "Wastage", null, "Spilled bottle"), null);

        Assert.Equal(StockMovementType.Wastage.ToString(), movement.MovementType);
        Assert.Equal(-2.5m, movement.QuantityChange);
        Assert.Equal(7.5m, movement.StockAfter);
    }

    [Fact]
    public async Task Usage_without_a_reason_is_rejected()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Toner", "TN-1", InventoryType.SalonUse, currentStock: 10m);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Inventory.RecordUsageAsync(
                productId, new RecordUsageRequest(1m, "Consumption", null, " "), null));

        Assert.Contains("notes", error.Errors.Keys);
    }

    [Fact]
    public async Task Usage_cannot_take_stock_below_zero()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Toner", "TN-1", InventoryType.SalonUse, currentStock: 1m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Inventory.RecordUsageAsync(
                productId, new RecordUsageRequest(2m, "Consumption", null, "Too much"), null));
    }

    [Fact]
    public async Task Retail_products_leave_stock_through_sales_not_usage()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Shampoo", "SH-1", InventoryType.Retail, costPrice: 10m, sellingPrice: 25m);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Inventory.RecordUsageAsync(
                productId, new RecordUsageRequest(1m, "Consumption", null, "reason"), null));

        Assert.Contains("usageType", error.Errors.Keys);
    }

    [Fact]
    public async Task An_adjustment_can_go_either_way_but_not_below_zero()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Towel", "TW-1", InventoryType.SalonUse, currentStock: 10m);

        var down = await harness.Inventory.AdjustStockAsync(
            productId, new AdjustStockRequest(-3m, null, "Physical count"), null);

        Assert.Equal(7m, down.StockAfter);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Inventory.AdjustStockAsync(
                productId, new AdjustStockRequest(-8m, null, "Too far"), null));

        Assert.Contains("quantityChange", error.Errors.Keys);
    }

    [Fact]
    public async Task A_zero_adjustment_is_rejected()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Towel", "TW-1", InventoryType.SalonUse, currentStock: 10m);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.Inventory.AdjustStockAsync(
                productId, new AdjustStockRequest(0m, null, "Nothing"), null));

        Assert.Contains("quantityChange", error.Errors.Keys);
    }

    [Fact]
    public async Task A_reversal_undoes_a_movement_without_touching_it()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Toner", "TN-1", InventoryType.SalonUse, currentStock: 8m);

        var mistaken = await harness.Inventory.ReceiveStockAsync(
            productId, new ReceiveStockRequest(3m, null, "PO-X", null, null), null);

        var reversal = await harness.Inventory.ReverseMovementAsync(
            productId, mistaken.Id, new ReverseStockMovementRequest("Delivery was never ours"), null);

        var product = await harness.Inventory.GetAsync(productId);

        Assert.Equal(8m, product.CurrentStock);
        Assert.Equal(-3m, reversal.QuantityChange);
        Assert.Equal(mistaken.Id, reversal.ReversalOfMovementId);
        // The original movement is still there, unchanged.
        var movements = await harness.Inventory.GetMovementsAsync(
            productId, new InventoryMovementQuery(productId, StockMovementTypeFilter.All, 1, 50));
        Assert.Contains(movements.Items, movement => movement.Id == mistaken.Id && movement.QuantityChange == 3m);
    }

    [Fact]
    public async Task A_movement_can_be_reversed_only_once()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Toner", "TN-1", InventoryType.SalonUse, currentStock: 8m);

        var mistaken = await harness.Inventory.ReceiveStockAsync(
            productId, new ReceiveStockRequest(3m, null, "PO-X", null, null), null);

        await harness.Inventory.ReverseMovementAsync(
            productId, mistaken.Id, new ReverseStockMovementRequest("Once"), null);

        await Assert.ThrowsAsync<ConflictException>(() =>
            harness.Inventory.ReverseMovementAsync(
                productId, mistaken.Id, new ReverseStockMovementRequest("Twice"), null));
    }

    [Fact]
    public async Task A_reversal_cannot_be_reversed_itself()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Toner", "TN-1", InventoryType.SalonUse, currentStock: 8m);

        var mistaken = await harness.Inventory.ReceiveStockAsync(
            productId, new ReceiveStockRequest(3m, null, "PO-X", null, null), null);

        var reversal = await harness.Inventory.ReverseMovementAsync(
            productId, mistaken.Id, new ReverseStockMovementRequest("Undo"), null);

        await Assert.ThrowsAsync<ConflictException>(() =>
            harness.Inventory.ReverseMovementAsync(
                productId, reversal.Id, new ReverseStockMovementRequest("Undo the undo"), null));
    }

    [Fact]
    public async Task The_stock_list_reports_low_and_out_of_stock()
    {
        using var harness = new InventoryHarness();
        var ok = await harness.SeedProductAsync("A Mask", "MK-A", InventoryType.SalonUse, currentStock: 10m, minimumStock: 2m);
        var low = await harness.SeedProductAsync("B Mask", "MK-B", InventoryType.SalonUse, currentStock: 2m, minimumStock: 2m);
        var out_ = await harness.SeedProductAsync("C Mask", "MK-C", InventoryType.SalonUse, currentStock: 0m, minimumStock: 2m);

        var okProduct = await harness.Inventory.GetAsync(ok);
        var lowProduct = await harness.Inventory.GetAsync(low);
        var outProduct = await harness.Inventory.GetAsync(out_);

        Assert.Equal("InStock", okProduct.StockStatus);
        Assert.Equal("LowStock", lowProduct.StockStatus);
        Assert.Equal("OutOfStock", outProduct.StockStatus);
    }

    [Fact]
    public async Task Search_finds_products_by_name_brand_and_sku()
    {
        using var harness = new InventoryHarness();
        await harness.SeedProductAsync("Colour Cream", "CC-1", InventoryType.SalonUse, brand: "Loreal Paris");
        await harness.SeedProductAsync("Shampoo", "LO-2", InventoryType.Retail, brand: "Loreal Paris", costPrice: 10m, sellingPrice: 25m);
        await harness.SeedProductAsync("Conditioner", "CD-1", InventoryType.SalonUse, brand: "Kerastase");

        var byBrand = await harness.Inventory.SearchAsync(
            new InventoryQuery("loreal", InventoryTypeFilter.All, InventoryProductStatusFilter.All, InventoryStockStatusFilter.All, null, 1, 50));
        var bySku = await harness.Inventory.SearchAsync(
            new InventoryQuery("cd-1", InventoryTypeFilter.All, InventoryProductStatusFilter.All, InventoryStockStatusFilter.All, null, 1, 50));

        Assert.Equal(2, byBrand.TotalCount);
        Assert.Single(bySku.Items);
    }

    [Fact]
    public async Task The_sellable_list_is_active_retail_only()
    {
        using var harness = new InventoryHarness();
        var retail = await harness.SeedProductAsync("Shampoo", "SH-1", InventoryType.Retail, costPrice: 10m, sellingPrice: 25m);
        await harness.SeedProductAsync("Toner", "TN-1", InventoryType.SalonUse);
        await harness.SeedProductAsync("Old Stock", "OS-1", InventoryType.Retail, costPrice: 10m, sellingPrice: 25m, isActive: false);

        var sellable = await harness.Inventory.GetSellableAsync(null);

        var option = Assert.Single(sellable);
        Assert.Equal(retail, option.Id);
        Assert.Equal(25m, option.SellingPrice);
    }

    [Fact]
    public async Task An_edit_cannot_restate_current_stock()
    {
        using var harness = new InventoryHarness();
        var productId = await harness.SeedProductAsync("Shampoo", "SH-1", InventoryType.Retail, costPrice: 10m, sellingPrice: 25m, currentStock: 7m);

        await harness.Inventory.UpdateAsync(
            productId,
            new SaveProductRequest("Shampoo", "Brand", "SH-1", "Hair", "Retail", "pcs", 10m, 30m, 3m, null));

        var product = await harness.Inventory.GetAsync(productId);

        Assert.Equal(30m, product.SellingPrice);
        Assert.Equal(7m, product.CurrentStock);
    }
}
