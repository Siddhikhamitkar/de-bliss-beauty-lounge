using ClosedXML.Excel;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Imports;
using DeBliss.Application.Imports.Dtos;
using DeBliss.Domain.Inventory;
using DeBliss.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DeBliss.UnitTests.Imports;

public class InventoryImportServiceTests
{
    [Fact]
    public void Both_inventory_templates_carry_headers_only_and_never_an_example_row()
    {
        using var harness = new ImportHarness();

        foreach (var type in new[] { ImportType.RetailProducts, ImportType.SalonUseProducts })
        {
            var template = harness.InventoryImports.CreateTemplate(type);

            var refused = Assert.ThrowsAsync<ValidationFailedException>(() =>
                harness.InventoryImports.PreviewAsync(harness.Stream(template), "template.xlsx", type));

            Assert.Contains("no data rows", refused.GetAwaiter().GetResult().Errors["file"][0]);
        }

        var retailTemplate = harness.InventoryImports.CreateTemplate(ImportType.RetailProducts);

        using var workbook = new XLWorkbook(new MemoryStream(retailTemplate));
        Assert.True(workbook.TryGetWorksheet("Retail Products", out var data));
        Assert.Equal(9, data!.LastColumnUsed().ColumnNumber());
        Assert.Equal(1, data.LastRowUsed().RowNumber());
        Assert.True(workbook.TryGetWorksheet("Example", out var example));
        Assert.Equal("LUM-KER-250", example!.Cell(5, 3).GetString());
    }

    [Fact]
    public async Task A_valid_retail_file_creates_the_product_and_an_opening_stock_movement()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.RetailProducts,
            new object?[] { "Keratin Shampoo 250ml", "Lumière", "lum-ker-250", "Hair Care", "320.50", "549", "5", "24", "yes" });

        var result = await harness.InventoryImports.ExecuteAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts, harness.Actor);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.OpeningStockMovements);

        await using var context = harness.Reopen();
        var product = await context.InventoryProducts.SingleAsync(entry => entry.Sku == "LUM-KER-250");
        Assert.Equal("Keratin Shampoo 250ml", product.Name);
        Assert.Equal(InventoryType.Retail, product.InventoryType);
        Assert.Equal(320.50m, product.CostPrice);
        Assert.Equal(549m, product.SellingPrice);
        Assert.Equal(5m, product.MinimumStock);
        Assert.Equal(24m, product.CurrentStock);
        Assert.True(product.IsActive);

        var movement = await context.StockMovements.SingleAsync(entry => entry.ProductId == product.Id);
        Assert.Equal(StockMovementType.OpeningStock, movement.MovementType);
        Assert.Equal(24m, movement.QuantityChange);
        Assert.Equal(0m, movement.StockBefore);
        Assert.Equal(24m, movement.StockAfter);
        Assert.Equal("Excel import", movement.Reference);
        Assert.Contains("Opening stock imported from 'retail.xlsx'", movement.Notes);
        Assert.Equal(harness.Actor, movement.PerformedByUserId);
        Assert.Equal(harness.Database.Clock.GetUtcNow(), movement.OccurredAt);
    }

    [Fact]
    public async Task A_valid_salon_use_file_creates_unpriced_products_with_opening_movements()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.SalonUseProducts,
            new object?[] { "Hydrogen Peroxide 3%", "Salon Basics", "SB-H2O2-1L", "Colour", "ml", "500", "2000", "no" });

        var result = await harness.InventoryImports.ExecuteAsync(harness.Stream(file), "salon.xlsx", ImportType.SalonUseProducts, harness.Actor);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.OpeningStockMovements);

        await using var context = harness.Reopen();
        var product = await context.InventoryProducts.SingleAsync(entry => entry.Sku == "SB-H2O2-1L");
        Assert.Equal(InventoryType.SalonUse, product.InventoryType);
        Assert.Null(product.CostPrice);
        Assert.Null(product.SellingPrice);
        Assert.Equal("ml", product.UnitOfMeasure);
        Assert.Equal(500m, product.MinimumStock);
        Assert.Equal(2000m, product.CurrentStock);
        Assert.False(product.IsActive);

        var movement = await context.StockMovements.SingleAsync(entry => entry.ProductId == product.Id);
        Assert.Equal(2000m, movement.QuantityChange);
        Assert.Equal(harness.Actor, movement.PerformedByUserId);
    }

    [Fact]
    public async Task Retail_price_and_stock_rules_match_the_product_form_exactly()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.RetailProducts,
            new object?[] { "No Prices", null, "NP-1", null, null, null, null, null, null },
            new object?[] { "Negative Cost", null, "NC-1", null, "-5", "100", null, null, null },
            new object?[] { "Zero Selling", null, "ZS-1", null, "10", "0", null, null, null },
            new object?[] { "Three Decimals", null, "TD-1", null, "10.123", "20", null, null, null },
            new object?[] { "Bad Opening", null, "BO-1", null, "10", "20", null, "-1", null },
            new object?[] { "Fine Opening", null, "FO-1", null, "10", "20", "0.0001", null, null });

        var preview = await harness.InventoryImports.PreviewAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts);

        Assert.Equal(6, preview.TotalRows);
        Assert.Equal(0, preview.ValidRows);
        Assert.Equal(6, preview.InvalidRows);
        Assert.Equal(0, preview.OpeningStockRows);
        AssertHas(preview, "Cost price", "Retail products need a cost price.");
        AssertHas(preview, "Selling price", "Retail products need a selling price.");
        AssertHas(preview, "Cost price", "Cost price cannot be negative.");
        AssertHas(preview, "Selling price", "Selling price must be greater than zero.");
        AssertHas(preview, "Cost price", "Cost price can have at most 2 decimal places.");
        AssertHas(preview, "Opening stock", "Opening stock cannot be negative.");
        AssertHas(preview, "Minimum stock", "Minimum stock can have at most 3 decimal places.");
    }

    [Fact]
    public async Task Numbers_may_use_thousands_grouping_but_never_ambiguous_separators()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.RetailProducts,
            new object?[] { "Grouped", "B", "G-1", null, "1,234", "2,000", null, null, null },
            new object?[] { "Ambiguous", "B", "A-1", null, "1.234,50", "20", null, null, null },
            new object?[] { "Typed", "B", "N-1", null, 320.5, 549, null, null, null });

        var preview = await harness.InventoryImports.PreviewAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts);

        Assert.Equal(2, preview.ValidRows);
        Assert.Equal(1, preview.InvalidRows);
        Assert.Equal("'1.234,50' is ambiguous. Use plain digits with a dot for decimals.", Single(preview, "Cost price").Message);

        var result = await harness.InventoryImports.ExecuteAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts, harness.Actor);

        Assert.Equal(2, result.Created);

        await using var context = harness.Reopen();
        var grouped = await context.InventoryProducts.SingleAsync(entry => entry.Sku == "G-1");
        Assert.Equal(1234m, grouped.CostPrice);
        Assert.Equal(2000m, grouped.SellingPrice);

        var typed = await context.InventoryProducts.SingleAsync(entry => entry.Sku == "N-1");
        Assert.Equal(320.5m, typed.CostPrice);
    }

    [Fact]
    public async Task An_existing_sku_is_flagged_and_skipped_with_nothing_overwritten()
    {
        using var harness = new ImportHarness();
        await harness.SeedProductAsync("Existing Shampoo", "LUM-KER-250", InventoryType.Retail, 100m, 200m, currentStock: 5m);

        var file = ImportTestExcel.For(ImportType.RetailProducts,
            new object?[] { "Keratin Shampoo", "Lumière", "lum-ker-250", "Hair Care", "320.50", "549", "5", "99", "yes" });

        var preview = await harness.InventoryImports.PreviewAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts);

        Assert.Equal(1, preview.ExistingRows);
        Assert.Contains("already exists", Single(preview, "SKU").Message);
        Assert.Contains("skips it", Single(preview, "SKU").Message);

        var result = await harness.InventoryImports.ExecuteAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts, harness.Actor);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.SkippedExisting);
        Assert.Equal(0, result.OpeningStockMovements);

        await using var context = harness.Reopen();
        var existing = await context.InventoryProducts.SingleAsync(entry => entry.Sku == "LUM-KER-250");
        Assert.Equal("Existing Shampoo", existing.Name);
        Assert.Equal(100m, existing.CostPrice);
        Assert.Equal(5m, existing.CurrentStock);
        Assert.Equal(0, await context.StockMovements.CountAsync());
    }

    [Fact]
    public async Task The_same_sku_twice_in_one_file_is_a_duplicate_and_only_one_is_created()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.RetailProducts,
            new object?[] { "First Mask", "Lumière", "MK-1", null, "100", "200", null, null, null },
            new object?[] { "Second Mask", "Lumière", "mk-1", null, "150", "250", null, null, null });

        var preview = await harness.InventoryImports.PreviewAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts);

        Assert.Equal(1, preview.DuplicateRows);
        Assert.Equal("DuplicateInFile", preview.Rows[1].Status);
        Assert.Contains("Same SKU as row 2.", Single(preview, "SKU").Message);

        var result = await harness.InventoryImports.ExecuteAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts, harness.Actor);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.SkippedExisting);

        await using var context = harness.Reopen();
        var product = await context.InventoryProducts.SingleAsync(entry => entry.Sku == "MK-1");
        Assert.Equal("First Mask", product.Name);
    }

    [Fact]
    public async Task Retrying_the_same_file_creates_no_new_products_or_movements()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.RetailProducts,
            new object?[] { "Keratin Shampoo", "Lumière", "LUM-KER-250", "Hair Care", "320.50", "549", "5", "24", "yes" });

        var first = await harness.InventoryImports.ExecuteAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts, harness.Actor);
        Assert.Equal(1, first.Created);

        var retry = await harness.InventoryImports.ExecuteAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts, harness.Actor);

        Assert.True(retry.Succeeded);
        Assert.Equal(0, retry.Created);
        Assert.Equal(1, retry.SkippedExisting);
        Assert.Equal(0, retry.OpeningStockMovements);
        Assert.Equal(1, await harness.ProductCountAsync());
        Assert.Equal(1, await harness.MovementCountAsync());

        await using var context = harness.Reopen();
        var product = await context.InventoryProducts.SingleAsync(entry => entry.Sku == "LUM-KER-250");
        Assert.Equal(24m, product.CurrentStock);
    }

    [Fact]
    public async Task Opening_stock_is_only_recorded_when_it_is_a_positive_quantity()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.RetailProducts,
            new object?[] { "Blank Opening", null, "BL-1", null, "10", "20", null, null, null },
            new object?[] { "Zero Opening", null, "ZO-1", null, "10", "20", null, "0", null });

        var preview = await harness.InventoryImports.PreviewAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts);

        Assert.Equal(2, preview.ValidRows);
        Assert.Equal(0, preview.OpeningStockRows);

        var result = await harness.InventoryImports.ExecuteAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts, harness.Actor);

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.OpeningStockMovements);
        Assert.Equal(0, await harness.MovementCountAsync());
    }

    [Fact]
    public async Task A_formula_anywhere_in_a_row_refuses_that_row()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.RetailProducts,
            new object?[] { "Keratin Shampoo", "Lumière", "K-1", null, new Formula("1+1"), "549", null, null, null });

        var preview = await harness.InventoryImports.PreviewAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts);

        Assert.Equal(1, preview.InvalidRows);
        Assert.Equal("Formulas are not allowed. Enter a plain value.", Single(preview, "Cost price").Message);

        var result = await harness.InventoryImports.ExecuteAsync(harness.Stream(file), "retail.xlsx", ImportType.RetailProducts, harness.Actor);

        Assert.Equal(0, result.Created);
        Assert.Equal(0, await harness.ProductCountAsync());
    }

    [Fact]
    public async Task Field_limits_match_the_product_form_exactly()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.SalonUseProducts,
            new object?[]
            {
                new string('N', 201), new string('B', 101), new string('S', 51), new string('C', 101),
                new string('u', 21), null, null, null
            });

        var preview = await harness.InventoryImports.PreviewAsync(harness.Stream(file), "salon.xlsx", ImportType.SalonUseProducts);

        Assert.Equal(1, preview.InvalidRows);
        Assert.Equal("Product name must be 200 characters or fewer.", Single(preview, "Product name").Message);
        Assert.Equal("Brand must be 100 characters or fewer.", Single(preview, "Brand").Message);
        Assert.Equal("SKU must be 50 characters or fewer.", Single(preview, "SKU").Message);
        Assert.Equal("Category must be 100 characters or fewer.", Single(preview, "Category").Message);
        Assert.Equal("Unit must be 20 characters or fewer.", Single(preview, "Unit of measure").Message);
    }

    [Fact]
    public async Task File_level_problems_are_refused_before_any_row_is_read()
    {
        using var harness = new ImportHarness();

        await RefuseAsync(harness, ImportTestExcel.For(ImportType.RetailProducts), "retail.xlsx", ImportType.RetailProducts, "no data rows");
        await RefuseAsync(harness, new byte[] { 1, 2, 3, 4 }, "retail.xlsx", ImportType.RetailProducts, "not an .xlsx workbook");
        await RefuseAsync(harness, ImportTestExcel.For(ImportType.RetailProducts, new object?[] { "A", "B", "S-1", null, "1", "2", null, null, null }), "retail.xlsx", ImportType.SalonUseProducts, "no sheet named 'Salon-Use Products'");
        await RefuseAsync(harness, ImportTestExcel.Workbook("Retail Products", ["Product name", "Extra"], []), "retail.xlsx", ImportType.RetailProducts, "must have exactly these columns");
    }

    [Fact]
    public async Task An_unexpected_failure_mid_file_saves_no_products_or_movements()
    {
        using var harness = new ImportHarness();
        var source = new CancellationTokenSource();

        var imports = new InventoryImportService(
            harness.Files,
            new InventoryRepository(harness.Context),
            harness.Inventory,
            new FailingAfterBeginUnitOfWork(harness.Context, source));

        var file = ImportTestExcel.For(ImportType.RetailProducts,
            new object?[] { "Keratin Shampoo", "Lumière", "K-1", null, "10", "20", null, "24", null });

        var result = await imports.ExecuteAsync(
            harness.Stream(file), "retail.xlsx", ImportType.RetailProducts, harness.Actor, source.Token);

        // Failed counts rows that reached the write; the fault fires during the
        // pre-write SKU re-check, so the file-level summary reports zero.
        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.OpeningStockMovements);
        Assert.Contains("nothing was saved", result.Message);
        Assert.Equal(0, await harness.ProductCountAsync());
        Assert.Equal(0, await harness.MovementCountAsync());
    }

    private static ImportFieldError Single(ImportPreview preview, string field) =>
        preview.Errors.Single(error => error.Field == field);

    private static void AssertHas(ImportPreview preview, string field, string message) =>
        Assert.Contains(preview.Errors, error => error.Field == field && error.Message == message);

    private static async Task RefuseAsync(
        ImportHarness harness,
        byte[] content,
        string fileName,
        ImportType type,
        string expectedText)
    {
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.InventoryImports.PreviewAsync(harness.Stream(content), fileName, type));

        Assert.Equal("file", Assert.Single(exception.Errors.Keys));
        Assert.Contains(expectedText, Assert.Single(Assert.Single(exception.Errors.Values)));
    }
}
