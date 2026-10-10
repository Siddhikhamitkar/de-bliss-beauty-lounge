using DeBliss.Application.Billing;
using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Domain.Inventory;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.Billing;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Inventory;

/// <summary>
/// The retail-sale seam between inventory and billing: product lines on a
/// draft, stock deducted at checkout in the same save as the payment, no
/// overselling, and no double deduction however the checkout is retried.
/// </summary>
public class RetailSaleTests : IDisposable
{
    // One database shared by both harnesses: the product rows the sale is
    // deducted from must be the same ones the invoice service reads.
    private readonly SqliteTestDatabase _database = new();
    private readonly BillingHarness _billing;
    private readonly InventoryHarness _inventory;

    public RetailSaleTests()
    {
        _billing = new BillingHarness(_database);
        _inventory = new InventoryHarness(_database);
    }

    private Guid SeedRetailProduct(string name, string sku, decimal cost, decimal price, decimal stock)
    {
        var productId = _inventory.SeedProductAsync(
            name, sku, InventoryType.Retail, costPrice: cost, sellingPrice: price, currentStock: stock)
            .GetAwaiter().GetResult();
        return productId;
    }

    private async Task<(Guid InvoiceId, IInvoiceService Service)> StartRetailDraftAsync()
    {
        var invoices = new InvoiceService(
            new InvoiceRepository(_billing.Context),
            new CouponRepository(_billing.Context),
            new AppointmentRepository(_billing.Context),
            new CustomerRepository(_billing.Context),
            new InventoryRepository(_billing.Context),
            _billing.Context,
            _billing.Database.Clock);

        var result = await invoices.StartFromAppointmentAsync(
            new CreateInvoiceRequest(AppointmentId: null, CustomerId: null, WalkInCustomerName: "Walk-in retail"));

        return (result.Invoice.Id, invoices);
    }

    [Fact]
    public async Task A_retail_sale_checkout_deducts_stock_and_writes_the_sale_movement()
    {
        var productId = SeedRetailProduct("Argan Oil", "AO-1", 40m, 85m, stock: 5m);
        var (invoiceId, invoices) = await StartRetailDraftAsync();

        await invoices.AddLineAsync(invoiceId, new AddInvoiceLineRequest(productId, 2m));
        var receipt = await invoices.CheckoutAsync(
            invoiceId,
            new CheckoutRequest(null, null, null, "Cash", 170m, null),
            actorUserId: null);

        await using var context = _inventory.Reopen();
        var product = await context.InventoryProducts.SingleAsync(p => p.Id == productId);
        var movement = await context.StockMovements
            .SingleAsync(m => m.ProductId == productId && m.InvoiceId == invoiceId);

        Assert.Equal(3m, product.CurrentStock);
        Assert.Equal(StockMovementType.RetailSale, movement.MovementType);
        Assert.Equal(-2m, movement.QuantityChange);
        Assert.Equal(5m, movement.StockBefore);
        Assert.Equal(3m, movement.StockAfter);
        Assert.Equal(170m, receipt.TotalAmount);
    }

    [Fact]
    public async Task Overselling_at_checkout_is_rejected_and_nothing_is_written()
    {
        var productId = SeedRetailProduct("Serum", "SR-1", 20m, 45m, stock: 2m);
        var (invoiceId, invoices) = await StartRetailDraftAsync();

        // The line was added when two were on the shelf; stock has since fallen.
        await invoices.AddLineAsync(invoiceId, new AddInvoiceLineRequest(productId, 2m));

        await using (var context = _inventory.Reopen())
        {
            var product = await context.InventoryProducts.SingleAsync(p => p.Id == productId);
            product.CurrentStock = 1m;
            product.StockVersion = Guid.NewGuid();
            await context.SaveChangesAsync();
        }

        // The checkout reads fresh figures, the way a separate request would.
        _billing.Context.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            invoices.CheckoutAsync(invoiceId, new CheckoutRequest(null, null, null, "Cash", 90m, null)));

        Assert.Contains("items", error.Errors.Keys);

        await using var verify = _inventory.Reopen();
        var stillOne = await verify.InventoryProducts.SingleAsync(p => p.Id == productId);
        var invoice = await verify.Invoices.Include(i => i.Payments).SingleAsync(i => i.Id == invoiceId);

        Assert.Equal(1m, stillOne.CurrentStock);
        Assert.Empty(invoice.Payments);
        Assert.Equal(DeBliss.Domain.Billing.InvoiceStatus.Draft, invoice.Status);
    }

    [Fact]
    public async Task A_retried_checkout_of_a_paid_invoice_is_conflict_not_a_second_deduction()
    {
        var productId = SeedRetailProduct("Mask", "MK-1", 10m, 25m, stock: 4m);
        var (invoiceId, invoices) = await StartRetailDraftAsync();

        await invoices.AddLineAsync(invoiceId, new AddInvoiceLineRequest(productId, 1m));
        await invoices.CheckoutAsync(invoiceId, new CheckoutRequest(null, null, null, "Cash", 25m, null));

        // The browser retries the POST: the paid guard fires before any stock
        // is touched a second time.
        await Assert.ThrowsAsync<ConflictException>(() =>
            invoices.CheckoutAsync(invoiceId, new CheckoutRequest(null, null, null, "Cash", 25m, null)));

        await using var verify = _inventory.Reopen();
        var product = await verify.InventoryProducts.SingleAsync(p => p.Id == productId);
        var saleMovements = await verify.StockMovements
            .CountAsync(m => m.ProductId == productId && m.InvoiceId == invoiceId);

        Assert.Equal(3m, product.CurrentStock);
        Assert.Equal(1, saleMovements);
    }

    [Fact]
    public async Task Concurrent_checkouts_of_the_last_unit_one_wins_the_other_conflicts()
    {
        var productId = SeedRetailProduct("Last Bottle", "LB-1", 30m, 60m, stock: 1m);

        // Two independent requests, each its own context over the same database.
        var first = await BuildIndependentInvoiceAsync();
        var second = await BuildIndependentInvoiceAsync();

        await first.invoices.AddLineAsync(first.invoiceId, new AddInvoiceLineRequest(productId, 1m));
        await second.invoices.AddLineAsync(second.invoiceId, new AddInvoiceLineRequest(productId, 1m));

        var firstCheckout = Task.Run(() => first.invoices.CheckoutAsync(
            first.invoiceId, new CheckoutRequest(null, null, null, "Cash", 60m, null)));
        var secondCheckout = Task.Run(() => second.invoices.CheckoutAsync(
            second.invoiceId, new CheckoutRequest(null, null, null, "Cash", 60m, null)));

        var firstResult = await firstCheckout.ContinueWith(
            task => task.IsCompletedSuccessfully ? (object?)task.Result : (object?)task.Exception?.InnerException);
        var secondResult = await secondCheckout.ContinueWith(
            task => task.IsCompletedSuccessfully ? (object?)task.Result : (object?)task.Exception?.InnerException);

        // One checkout walked away with a receipt; the other lost the race to
        // the unique (invoice, product, type) index or the stock token.
        var results = new[] { firstResult, secondResult };

        Assert.Single(results.OfType<ReceiptDto>());
        Assert.Single(results.OfType<Exception>());

        await using var verify = _inventory.Reopen();
        var product = await verify.InventoryProducts.SingleAsync(p => p.Id == productId);
        Assert.Equal(0m, product.CurrentStock);

        // Only the winning checkout wrote a sale movement for the last unit.
        var sales = await verify.StockMovements
            .Where(m => m.ProductId == productId && m.MovementType == StockMovementType.RetailSale)
            .ToListAsync();
        Assert.Single(sales);
    }

    [Fact]
    public async Task A_salon_use_product_cannot_be_added_to_a_bill()
    {
        var productId = await _inventory.SeedProductAsync("Backwash Shampoo", "BS-1", InventoryType.SalonUse);
        var (invoiceId, invoices) = await StartRetailDraftAsync();

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            invoices.AddLineAsync(invoiceId, new AddInvoiceLineRequest(productId, 1m)));

        Assert.Contains("productId", error.Errors.Keys);
    }

    [Fact]
    public async Task An_empty_retail_invoice_cannot_be_checked_out()
    {
        var (invoiceId, invoices) = await StartRetailDraftAsync();

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            invoices.CheckoutAsync(invoiceId, new CheckoutRequest(null, null, null, "Cash", 0m, null)));

        Assert.Contains("items", error.Errors.Keys);
    }

    [Fact]
    public async Task Adding_the_same_product_twice_merges_into_one_line()
    {
        var productId = SeedRetailProduct("Shampoo", "SH-1", 10m, 25m, stock: 10m);
        var (invoiceId, invoices) = await StartRetailDraftAsync();

        await invoices.AddLineAsync(invoiceId, new AddInvoiceLineRequest(productId, 1m));
        var invoice = await invoices.AddLineAsync(invoiceId, new AddInvoiceLineRequest(productId, 2m));

        var line = Assert.Single(invoice.Items);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal(75m, invoice.TotalAmount);
    }

    [Fact]
    public async Task Service_lines_come_from_the_booking_and_cannot_be_edited_by_hand()
    {
        var serviceId = await _billing.SeedServiceAsync("Balayage", 300m);
        var appointmentId = await _billing.SeedAppointmentAsync(
            null, new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero),
            lines: [(serviceId, "Balayage", 300m, 1)]);

        var invoices = new InvoiceService(
            new InvoiceRepository(_billing.Context),
            new CouponRepository(_billing.Context),
            new AppointmentRepository(_billing.Context),
            new CustomerRepository(_billing.Context),
            new InventoryRepository(_billing.Context),
            _billing.Context,
            _billing.Database.Clock);

        var result = await invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId));
        var line = Assert.Single(result.Invoice.Items);

        await Assert.ThrowsAsync<ConflictException>(() =>
            invoices.UpdateLineAsync(result.Invoice.Id, line.Id, new UpdateInvoiceLineRequest(5m)));
        await Assert.ThrowsAsync<ConflictException>(() =>
            invoices.RemoveLineAsync(result.Invoice.Id, line.Id));
    }

    [Fact]
    public async Task Product_lines_can_be_re_quantified_and_removed_while_draft()
    {
        var productId = SeedRetailProduct("Serum", "SR-2", 20m, 45m, stock: 10m);
        var (invoiceId, invoices) = await StartRetailDraftAsync();

        var added = await invoices.AddLineAsync(invoiceId, new AddInvoiceLineRequest(productId, 2m));
        var line = Assert.Single(added.Items);

        var updated = await invoices.UpdateLineAsync(invoiceId, line.Id, new UpdateInvoiceLineRequest(1m));
        Assert.Equal(45m, updated.TotalAmount);

        var removed = await invoices.RemoveLineAsync(invoiceId, line.Id);
        Assert.Empty(removed.Items);
        Assert.Equal(0m, removed.TotalAmount);
    }

    private async Task<(Guid invoiceId, IInvoiceService invoices)> BuildIndependentInvoiceAsync()
    {
        var context = _inventory.Database.CreateContext();
        var invoices = new InvoiceService(
            new InvoiceRepository(context),
            new CouponRepository(context),
            new AppointmentRepository(context),
            new CustomerRepository(context),
            new InventoryRepository(context),
            context,
            _inventory.Database.Clock);

        var result = await invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(null));

        return (result.Invoice.Id, invoices);
    }

    public void Dispose()
    {
        _billing.Dispose();
        _inventory.Dispose();
    }
}
