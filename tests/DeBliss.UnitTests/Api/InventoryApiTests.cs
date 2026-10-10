using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.Inventory.Dtos;
using DeBliss.Domain.Inventory;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// The inventory surface's role split, exercised exactly as a browser or a
/// rogue script would: the owner can manage everything, the receptionist can
/// read everything and write nothing. Every write endpoint must answer 403 for
/// a receptionist — hiding the button is not the enforcement.
/// </summary>
public class InventoryApiTests : ApiTestBase
{
    private const string ReceptionistEmail = "receptionist@debliss.test";
    private const string ReceptionistPassword = "Test#Front123";

    private async Task<HttpClient> SignInReceptionistAsync()
    {
        await Factory.SeedUserAsync(
            ReceptionistEmail, ReceptionistPassword, DeBliss.Application.Identity.RoleNames.Receptionist);

        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await AuthenticateAsync(client, ReceptionistEmail, ReceptionistPassword);

        return client;
    }

    private Task<Guid> SeedProductAsync(
        string name = "L'Oréal Colour 1.0",
        string sku = "LC-100",
        InventoryType inventoryType = InventoryType.SalonUse,
        decimal currentStock = 5m) =>
        WithDatabaseAsync(context =>
            TestDoubles.InventorySeed.ProductAsync(
                context, name, sku, inventoryType,
                costPrice: inventoryType == InventoryType.Retail ? 10m : null,
                sellingPrice: inventoryType == InventoryType.Retail ? 25m : null,
                currentStock: currentStock));

    private async Task<Guid> WithDatabaseAsync(Func<DeBliss.Infrastructure.Persistence.DeBlissDbContext, Task<Guid>> seed)
    {
        await using var context = Factory.Database.CreateContext();
        return await seed(context);
    }

    [Fact]
    public async Task The_owner_creates_a_product_and_receives_stock()
    {
        var created = await Client.PostAsJsonAsync("/api/inventory", new
        {
            name = "Argan Oil 100ml",
            brand = "Moroccanoil",
            sku = "MO-AO-100",
            category = "Hair Care",
            inventoryType = "Retail",
            unitOfMeasure = "pcs",
            costPrice = 40m,
            sellingPrice = 85m,
            minimumStock = 3m,
            isActive = true
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var product = await ReadAsync<InventoryProductDto>(created);
        Assert.Equal(0m, product.CurrentStock);

        var received = await Client.PostAsJsonAsync($"/api/inventory/{product.Id}/stock", new
        {
            quantity = 6m,
            reference = "PO-2026-10",
            notes = "First delivery",
            kind = "StockReceived"
        });

        Assert.Equal(HttpStatusCode.Created, received.StatusCode);

        var movement = await ReadAsync<StockMovementDto>(received);
        Assert.Equal(6m, movement.StockAfter);

        var detail = await ReadAsync<InventoryProductDto>(
            await Client.GetAsync($"/api/inventory/{product.Id}"));

        Assert.Equal(6m, detail.CurrentStock);
        Assert.Equal(6m, detail.LastAddedQuantity);
        Assert.NotNull(detail.LastStockAddedAt);
    }

    [Fact]
    public async Task A_receptionist_reads_the_inventory_but_writes_nothing()
    {
        var productId = await SeedProductAsync();
        await SeedProductAsync("Serum", "SR-1", InventoryType.Retail, currentStock: 4m);

        using var receptionist = await SignInReceptionistAsync();

        // Reads succeed — the front desk needs the stock list to sell.
        var list = await receptionist.GetAsync("/api/inventory");
        var detail = await receptionist.GetAsync($"/api/inventory/{productId}");
        var movements = await receptionist.GetAsync($"/api/inventory/{productId}/movements");
        var sellable = await receptionist.GetAsync("/api/inventory/sellable");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, movements.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sellable.StatusCode);

        // Every write is forbidden, called directly the way a script would.
        var create = await receptionist.PostAsJsonAsync("/api/inventory", new
        {
            name = "New Product", sku = "NP-1", inventoryType = "Retail", costPrice = 1m, sellingPrice = 2m
        });
        var update = await receptionist.PutAsJsonAsync($"/api/inventory/{productId}", new
        {
            name = "Renamed", sku = "LC-100", inventoryType = "SalonUse", minimumStock = 1m
        });
        var status = await receptionist.PatchAsJsonAsync($"/api/inventory/{productId}/status", new { isActive = false });
        var stock = await receptionist.PostAsJsonAsync($"/api/inventory/{productId}/stock", new { quantity = 5m });
        var usage = await receptionist.PostAsJsonAsync($"/api/inventory/{productId}/usage", new
        {
            quantity = 1m, usageType = "Consumption", notes = "reason"
        });
        var adjustment = await receptionist.PostAsJsonAsync($"/api/inventory/{productId}/adjustments", new
        {
            quantityChange = -1m, notes = "count"
        });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, stock.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, usage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, adjustment.StatusCode);
    }

    [Fact]
    public async Task A_receptionist_cannot_reverse_a_movement_even_one_the_owner_just_made()
    {
        var productId = await SeedProductAsync();

        var received = await Client.PostAsJsonAsync($"/api/inventory/{productId}/stock", new
        {
            quantity = 5m, notes = "Owner received", kind = "StockReceived"
        });

        var movement = await ReadAsync<StockMovementDto>(received);

        using var receptionist = await SignInReceptionistAsync();

        var reversal = await receptionist.PostAsJsonAsync(
            $"/api/inventory/{productId}/movements/{movement.Id}/reversal",
            new { notes = "Should not work" });

        Assert.Equal(HttpStatusCode.Forbidden, reversal.StatusCode);
    }

    [Fact]
    public async Task A_retail_sale_through_the_api_deducts_stock_and_lands_in_the_history()
    {
        var productId = await SeedProductAsync("Argan Oil", "AO-API-1", InventoryType.Retail, currentStock: 5m);

        var started = await Client.PostAsJsonAsync("/api/invoices", new { walkInCustomerName = "Retail buyer" });
        var invoice = await ReadAsync<InvoiceApiContract>(started);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        var added = await Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/lines", new
        {
            productId, quantity = 2m
        });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        var checkedOut = await Client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/checkout", new
        {
            paymentMethod = "Card", amount = 50m
        });
        Assert.Equal(HttpStatusCode.OK, checkedOut.StatusCode);

        var detail = await ReadAsync<InventoryProductDto>(
            await Client.GetAsync($"/api/inventory/{productId}"));

        Assert.Equal(3m, detail.CurrentStock);
        // The sale never overwrote the receipt: there was none, so still none —
        // and the movement history shows exactly one RetailSale for this invoice.
        var movements = await ReadAsync<MovementPage>(
            await Client.GetAsync($"/api/inventory/{productId}/movements"));

        var sale = Assert.Single(movements.Items, movement => movement.MovementType == "RetailSale");
        Assert.Equal(-2m, sale.QuantityChange);
        Assert.Equal(invoice.Id, sale.InvoiceId);
    }

    private sealed record InvoiceApiContract(Guid Id, string InvoiceNumber);

    private sealed record MovementPage(int TotalCount, IReadOnlyList<StockMovementDto> Items);
}
