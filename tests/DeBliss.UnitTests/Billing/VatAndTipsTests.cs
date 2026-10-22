using DeBliss.Application.Billing;
using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Inventory;
using DeBliss.UnitTests.Inventory;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Billing;

/// <summary>
/// The VAT and tip layer on top of the checkout: VAT is charged on the
/// post-discount amount at the salon's configured rate and mode, and the tip
/// is a gratuity on top that never joins the taxable amount. Both are
/// snapshotted onto the paid invoice, so a later settings change never
/// rewrites a finished bill, and the payment records the whole payable — VAT
/// and tip included — exactly once. Staff attribution follows the approved
/// rule: one performer for the whole visit attributes the tip, anything
/// shared or unassigned stays unallocated.
/// </summary>
public class VatAndTipsTests : IDisposable
{
    private static readonly DateTimeOffset VisitTime =
        new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    // One database shared by both harnesses: a product on a bill is deducted
    // from the same rows the invoice service reads, the way one request would.
    private readonly SqliteTestDatabase _database = new();
    private readonly BillingHarness _billing;
    private readonly InventoryHarness _inventory;

    public VatAndTipsTests()
    {
        _billing = new BillingHarness(_database);
        _inventory = new InventoryHarness(_database);
    }

    private static CheckoutRequest Checkout(
        decimal amount,
        string? couponCode = null,
        string? manualDiscountType = null,
        decimal? manualDiscountValue = null,
        string? paymentMethod = "Cash",
        string? reference = null,
        decimal? tipPercentage = null,
        decimal? tipAmount = null) =>
        new(couponCode, manualDiscountType, manualDiscountValue, paymentMethod, amount, reference, tipPercentage, tipAmount);

    /// <summary>
    /// A completed visit of one or two services at the booked prices. The
    /// catalogue prices are irrelevant to the bill: the snapshot is what
    /// charges, and one number keeps the arithmetic readable.
    /// </summary>
    private async Task<Guid> VisitAsync(
        decimal firstPrice,
        decimal? secondPrice = null,
        IReadOnlyList<Guid?>? lineStaffIds = null,
        string firstName = "Haircut",
        string secondName = "Beard Trim")
    {
        var customerId = await _billing.SeedCustomerAsync();
        var haircutId = await _billing.SeedServiceAsync(firstName, firstPrice);
        var lines = new List<(Guid ServiceId, string SnapshotName, decimal UnitPrice, int SortOrder)>
        {
            (haircutId, firstName, firstPrice, 0)
        };

        if (secondPrice is { } second)
        {
            var beardTrimId = await _billing.SeedServiceAsync(secondName, second);
            lines.Add((beardTrimId, secondName, second, 1));
        }

        return await _billing.SeedAppointmentAsync(customerId, VisitTime, lines: lines, lineStaffIds: lineStaffIds);
    }

    private async Task<Guid> StartAsync(Guid appointmentId) =>
        (await _billing.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId))).Invoice.Id;

    private async Task<Guid> RetailDraftAsync()
    {
        var result = await _billing.Invoices.StartFromAppointmentAsync(
            new CreateInvoiceRequest(AppointmentId: null, CustomerId: null, WalkInCustomerName: "Retail buyer"));

        return result.Invoice.Id;
    }

    private async Task<Guid> SeedRetailProductAsync(string name, string sku, decimal price, decimal stock) =>
        await _inventory.SeedProductAsync(
            name, sku, InventoryType.Retail, costPrice: price / 2m, sellingPrice: price, currentStock: stock);

    [Fact]
    public async Task A_tax_exclusive_checkout_adds_five_percent_to_the_post_discount_amount()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var invoiceId = await StartAsync(await VisitAsync(100m));

        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(105m));

        Assert.Equal(5m, receipt.TaxRate);
        Assert.Equal(5m, receipt.TaxTotal);
        Assert.Equal(0m, receipt.TipAmount);
        Assert.Equal(105m, receipt.TotalAmount);

        await using var verify = _billing.Reopen();
        var stored = await verify.Invoices.Include(invoice => invoice.Payments).SingleAsync();

        Assert.Equal(5m, stored.TaxRate);
        Assert.False(stored.PricesIncludeTax);
        Assert.Equal(5m, stored.TaxTotal);
        Assert.Equal(105m, stored.TotalAmount);

        var payment = Assert.Single(stored.Payments);

        Assert.Equal(105m, payment.Amount);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
    }

    [Fact]
    public async Task A_retail_product_is_taxed_the_same_as_a_service()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var productId = await SeedRetailProductAsync("Argan Oil", "AO-VAT", 100m, stock: 5m);
        var invoiceId = await RetailDraftAsync();

        await _billing.Invoices.AddLineAsync(invoiceId, new AddInvoiceLineRequest(productId, 1m));

        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(105m));

        Assert.Equal(5m, receipt.TaxTotal);
        Assert.Equal(105m, receipt.TotalAmount);
    }

    [Fact]
    public async Task Tax_inclusive_prices_extract_the_vat_without_charging_it_twice()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: true);
        var invoiceId = await StartAsync(await VisitAsync(105m));

        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(105m));

        // 105 already contains its VAT — 105 × 5/105 = 5 — so nothing is added.
        Assert.Equal(5m, receipt.TaxTotal);
        Assert.Equal(105m, receipt.TotalAmount);

        await using var verify = _billing.Reopen();

        Assert.True((await verify.Invoices.SingleAsync()).PricesIncludeTax);
    }

    [Fact]
    public async Task Discounts_reduce_the_amount_the_vat_is_charged_on()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        await _billing.SeedCouponAsync("SAVE20", DiscountType.Fixed, 20m);
        var invoiceId = await StartAsync(await VisitAsync(100m, 60m));

        var receipt = await _billing.Invoices.CheckoutAsync(
            invoiceId,
            Checkout(136.50m, couponCode: "SAVE20", manualDiscountType: "Fixed", manualDiscountValue: 10m));

        // 160 − 20 − 10 = 130 owed for the visit, and VAT is charged on that.
        Assert.Equal(130m, receipt.SubTotal - receipt.CouponDiscount - receipt.ManualDiscount);
        Assert.Equal(6.50m, receipt.TaxTotal);
        Assert.Equal(136.50m, receipt.TotalAmount);
    }

    [Fact]
    public async Task Vat_rounds_to_fils_and_the_same_figure_reaches_every_surface()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var invoiceId = await StartAsync(await VisitAsync(99.99m));

        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(104.99m));

        // 5% of 99.99 is 4.9995, rounded away from zero to 5.00.
        Assert.Equal(5m, receipt.TaxTotal);
        Assert.Equal(104.99m, receipt.TotalAmount);

        await using var verify = _billing.Reopen();
        var stored = await verify.Invoices.Include(invoice => invoice.Payments).SingleAsync();
        var reprint = await _billing.Invoices.GetReceiptAsync(invoiceId);

        Assert.Equal(receipt.TaxTotal, stored.TaxTotal);
        Assert.Equal(receipt.TotalAmount, stored.TotalAmount);
        Assert.Equal(receipt.TotalAmount, stored.Payments.Single().Amount);
        Assert.Equal(receipt.TotalAmount, reprint.TotalAmount);
        Assert.Equal(receipt.TaxTotal, reprint.TaxTotal);
    }

    [Fact]
    public async Task A_paid_invoice_keeps_its_tax_snapshot_when_the_settings_change()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var invoiceId = await StartAsync(await VisitAsync(100m));

        await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(105m));

        await _billing.SetTaxAsync(20m, pricesIncludeTax: true);

        await using (var verify = _billing.Reopen())
        {
            var stored = await verify.Invoices.SingleAsync();

            Assert.Equal(5m, stored.TaxRate);
            Assert.False(stored.PricesIncludeTax);
            Assert.Equal(5m, stored.TaxTotal);
            Assert.Equal(105m, stored.TotalAmount);
        }

        // The next bill is raised under the new configuration.
        var newInvoiceId = await StartAsync(await VisitAsync(100m, firstName: "Blow Dry"));
        var receipt = await _billing.Invoices.CheckoutAsync(newInvoiceId, Checkout(100m));

        Assert.Equal(20m, receipt.TaxRate);
        Assert.Equal(16.67m, receipt.TaxTotal); // 100 × 20/120
        Assert.Equal(100m, receipt.TotalAmount);
    }

    [Fact]
    public async Task A_percentage_tip_charges_the_post_tax_total_and_never_taxes_itself()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var invoiceId = await StartAsync(await VisitAsync(100m));

        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(115.50m, tipPercentage: 10m));

        // The base is the 105 the customer owes before the tip, never 115.50.
        Assert.Equal(10.50m, receipt.TipAmount);
        Assert.Equal(5m, receipt.TaxTotal);
        Assert.Equal(115.50m, receipt.TotalAmount);

        var stored = await _billing.Invoices.GetAsync(invoiceId);

        Assert.Equal(10m, stored.TipPercentage);
        Assert.Equal(10.50m, stored.TipAmount);
    }

    [Fact]
    public async Task An_inclusive_percentage_tip_uses_the_total_which_already_contains_the_vat()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: true);
        var invoiceId = await StartAsync(await VisitAsync(100m));

        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(110m, tipPercentage: 10m));

        // 100 already contains its VAT; the tip is 10% of the 100, and the
        // extracted VAT does not move.
        Assert.Equal(4.76m, receipt.TaxTotal); // 100 × 5/105 = 4.7619
        Assert.Equal(10m, receipt.TipAmount);
        Assert.Equal(110m, receipt.TotalAmount);
    }

    [Fact]
    public async Task A_custom_tip_amount_is_charged_as_keyed()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var invoiceId = await StartAsync(await VisitAsync(100m));

        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(120m, tipAmount: 15m));

        Assert.Equal(15m, receipt.TipAmount);
        Assert.Equal(5m, receipt.TaxTotal);
        Assert.Equal(120m, receipt.TotalAmount);

        var stored = await _billing.Invoices.GetAsync(invoiceId);

        Assert.Null(stored.TipPercentage);
        Assert.Equal(15m, stored.TipAmount);
    }

    [Fact]
    public async Task No_tip_leaves_zero_tip_and_no_unnecessary_records()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var invoiceId = await StartAsync(await VisitAsync(100m));

        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(105m));

        Assert.Equal(0m, receipt.TipAmount);

        var stored = await _billing.Invoices.GetAsync(invoiceId);

        Assert.Equal(0m, stored.TipAmount);
        Assert.Null(stored.TipPercentage);
        Assert.Null(stored.TipStaffId);
        Assert.Single(stored.Payments);
    }

    [Fact]
    public async Task A_tip_on_a_visit_performed_by_one_staff_member_is_attributed_to_that_performer()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var staffId = await _billing.SeedStaffAsync("Aisha", "Stylist");
        var invoiceId = await StartAsync(await VisitAsync(100m, 60m, lineStaffIds: [staffId, staffId]));

        await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(171m, tipAmount: 3m));

        var stored = await _billing.Invoices.GetAsync(invoiceId);

        Assert.Equal(staffId, stored.TipStaffId);
        Assert.Equal(3m, stored.TipAmount);
    }

    [Fact]
    public async Task A_tip_on_a_visit_performed_by_two_staff_members_is_recorded_unallocated()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var first = await _billing.SeedStaffAsync("Aisha", "Stylist");
        var second = await _billing.SeedStaffAsync("Omar", "Barber");
        var invoiceId = await StartAsync(await VisitAsync(100m, 60m, lineStaffIds: [first, second]));

        await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(171m, tipAmount: 3m));

        var stored = await _billing.Invoices.GetAsync(invoiceId);

        // No split is invented: the gratuity is kept whole and unallocated
        // until the owner approves a distribution rule.
        Assert.Equal(3m, stored.TipAmount);
        Assert.Null(stored.TipStaffId);
    }

    [Fact]
    public async Task A_retail_tip_carries_no_staff_link_and_changes_nothing_about_the_stock_math()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var productId = await SeedRetailProductAsync("Serum", "SR-VAT", 45m, stock: 10m);
        var invoiceId = await RetailDraftAsync();

        await _billing.Invoices.AddLineAsync(invoiceId, new AddInvoiceLineRequest(productId, 2m));

        // 90 for the products, 4.50 VAT, 5 gratuity.
        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(99.50m, tipAmount: 5m));

        Assert.Equal(4.50m, receipt.TaxTotal);
        Assert.Equal(5m, receipt.TipAmount);
        Assert.Equal(99.50m, receipt.TotalAmount);

        await using var verify = _billing.Reopen();
        var product = await verify.InventoryProducts.SingleAsync(p => p.Id == productId);
        var stored = await verify.Invoices.SingleAsync();

        Assert.Equal(8m, product.CurrentStock);
        Assert.Null(stored.TipStaffId);
    }

    [Fact]
    public async Task A_mixed_service_and_product_bill_charges_vat_on_the_whole_amount()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var productId = await SeedRetailProductAsync("Mask", "MK-VAT", 45m, stock: 10m);
        var appointmentId = await VisitAsync(100m);
        var invoiceId = await StartAsync(appointmentId);

        await _billing.Invoices.AddLineAsync(invoiceId, new AddInvoiceLineRequest(productId, 1m));

        // 100 + 45 = 145; VAT 7.25 on the whole bill.
        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(152.25m));

        Assert.Equal(7.25m, receipt.TaxTotal);
        Assert.Equal(152.25m, receipt.TotalAmount);
    }

    [Fact]
    public async Task A_tip_saved_on_the_draft_is_restored_when_the_checkout_is_resumed()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var appointmentId = await VisitAsync(100m);
        var invoiceId = await StartAsync(appointmentId);

        var saved = await _billing.Invoices.SetTipAsync(invoiceId, new SetTipRequest(TipPercentage: 10m, TipAmount: null));

        Assert.Equal(10m, saved.TipPercentage);

        // The interrupted checkout resumes on the same bill, tip still picked.
        var resumed = await _billing.Invoices.StartFromAppointmentAsync(new CreateInvoiceRequest(appointmentId));

        Assert.False(resumed.WasCreated);
        Assert.Equal(invoiceId, resumed.Invoice.Id);
        Assert.Equal(10m, resumed.Invoice.TipPercentage);

        // The charged amount is computed at checkout from the final numbers,
        // never from anything the client could have keyed as a total.
        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(115.50m, tipPercentage: 10m));

        Assert.Equal(10.50m, receipt.TipAmount);
        Assert.Equal(115.50m, receipt.TotalAmount);
    }

    [Fact]
    public async Task A_saved_tip_can_be_cleared_before_the_payment()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var invoiceId = await StartAsync(await VisitAsync(100m));

        await _billing.Invoices.SetTipAsync(invoiceId, new SetTipRequest(TipPercentage: 10m, TipAmount: null));
        var cleared = await _billing.Invoices.SetTipAsync(invoiceId, new SetTipRequest(TipPercentage: null, TipAmount: null));

        Assert.Null(cleared.TipPercentage);
        Assert.Equal(0m, cleared.TipAmount);

        var receipt = await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(105m));

        Assert.Equal(0m, receipt.TipAmount);
        Assert.Equal(105m, receipt.TotalAmount);
    }

    [Fact]
    public async Task A_retried_checkout_cannot_charge_the_vat_or_the_tip_twice()
    {
        await _billing.SetTaxAsync(5m, pricesIncludeTax: false);
        var invoiceId = await StartAsync(await VisitAsync(100m));

        await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(115.50m, tipPercentage: 10m));

        await Assert.ThrowsAsync<ConflictException>(
            () => _billing.Invoices.CheckoutAsync(invoiceId, Checkout(115.50m, tipPercentage: 10m)));

        await using var verify = _billing.Reopen();
        var stored = await verify.Invoices.Include(invoice => invoice.Payments).SingleAsync();

        var payment = Assert.Single(stored.Payments);

        Assert.Equal(115.50m, payment.Amount);
        Assert.Equal(10.50m, stored.TipAmount);
        Assert.Equal(5m, stored.TaxTotal);
    }

    /// <summary>
    /// The same invalid choices, wherever they are keyed: on the draft through
    /// <see cref="IInvoiceService.SetTipAsync"/> or in the checkout request
    /// itself. Field name first, then the value that is wrong with it.
    /// </summary>
    public static TheoryData<decimal?, decimal?, string> InvalidTipChoices => new()
    {
        { 10m, 5m, "tipAmount" },
        { 0m, null, "tipPercentage" },
        { -1m, null, "tipPercentage" },
        { 101m, null, "tipPercentage" },
        { null, 0m, "tipAmount" },
        { null, -5m, "tipAmount" }
    };

    [Theory]
    [MemberData(nameof(InvalidTipChoices))]
    public async Task An_invalid_tip_choice_on_the_draft_is_a_field_error(decimal? percentage, decimal? amount, string field)
    {
        var invoiceId = await StartAsync(await VisitAsync(100m));

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => _billing.Invoices.SetTipAsync(invoiceId, new SetTipRequest(percentage, amount)));

        Assert.Contains(field, failure.Errors.Keys);
    }

    [Theory]
    [MemberData(nameof(InvalidTipChoices))]
    public async Task An_invalid_tip_choice_at_checkout_is_rejected_and_nothing_is_charged(
        decimal? percentage, decimal? amount, string field)
    {
        var invoiceId = await StartAsync(await VisitAsync(100m));

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => _billing.Invoices.CheckoutAsync(
                invoiceId, Checkout(105m, tipPercentage: percentage, tipAmount: amount)));

        Assert.Contains(field, failure.Errors.Keys);

        await using var verify = _billing.Reopen();
        var stored = await verify.Invoices.Include(invoice => invoice.Payments).SingleAsync();

        Assert.Equal(InvoiceStatus.Draft, stored.Status);
        Assert.Empty(stored.Payments);
    }

    [Fact]
    public async Task A_tip_cannot_be_saved_on_an_invoice_that_is_already_paid()
    {
        var invoiceId = await StartAsync(await VisitAsync(100m));

        await _billing.Invoices.CheckoutAsync(invoiceId, Checkout(100m));

        await Assert.ThrowsAsync<ConflictException>(
            () => _billing.Invoices.SetTipAsync(invoiceId, new SetTipRequest(TipPercentage: 10m, TipAmount: null)));
    }

    public void Dispose()
    {
        _billing.Dispose();
        _inventory.Dispose();
    }
}
