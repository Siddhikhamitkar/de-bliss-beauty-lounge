using ClosedXML.Excel;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Imports;
using DeBliss.Application.Imports.Dtos;
using DeBliss.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DeBliss.UnitTests.Imports;

public class CustomerImportServiceTests
{
    [Fact]
    public async Task The_template_carries_headers_only_and_an_example_sheet_that_is_never_imported()
    {
        using var harness = new ImportHarness();

        var template = harness.CustomerImports.CreateTemplate();

        using var workbook = new XLWorkbook(new MemoryStream(template));
        Assert.True(workbook.TryGetWorksheet("Customers", out var data));
        Assert.Equal(9, data!.LastColumnUsed().ColumnNumber());
        Assert.Equal(1, data.LastRowUsed().RowNumber());

        Assert.True(workbook.TryGetWorksheet("Example", out var example));
        Assert.Equal("Amelia", example!.Cell(5, 1).GetString());

        var refused = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.CustomerImports.PreviewAsync(harness.Stream(template), "customer-import-template.xlsx"));

        Assert.Contains("no data rows", refused.Errors["file"][0]);
    }

    [Fact]
    public async Task A_valid_file_creates_customers_and_notes_through_the_customer_service()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210", "amelia@example.com", new DateTime(1992, 4, 18), "Female", "12 Rose Lane", "Prefers evening appointments", "yes" },
            new object?[] { "Rahul", "Mehta", "9812345678", null, null, null, null, null, null });

        var result = await harness.CustomerImports.ExecuteAsync(harness.Stream(file), "customers.xlsx", harness.Actor, importDuplicates: false);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.SkippedExisting);
        Assert.Equal(0, result.SkippedInvalid);
        Assert.Equal(0, result.OpeningStockMovements);

        await using var context = harness.Reopen();
        var amelia = await context.Customers.SingleAsync(entry => entry.Email == "amelia@example.com");
        Assert.Equal("Amelia", amelia.FirstName);
        Assert.Equal("Fernandes", amelia.LastName);
        Assert.Equal(new DateOnly(1992, 4, 18), amelia.DateOfBirth);
        Assert.Equal("Female", amelia.Gender);
        Assert.Equal("12 Rose Lane", amelia.Address);
        Assert.True(amelia.IsActive);

        var rahul = await context.Customers.SingleAsync(entry => entry.LastName == "Mehta");
        Assert.True(rahul.IsActive);

        var note = await context.CustomerNotes.SingleAsync(entry => entry.CustomerId == amelia.Id);
        Assert.Equal("Prefers evening appointments", note.Note);
    }

    [Fact]
    public async Task Preview_reports_every_row_with_field_errors_for_the_invalid_ones()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210", "amelia@example.com" },
            new object?[] { null, null, "+91 90000 00001" },
            new object?[] { "Typo", "Email", "+91 90000 00002", "not-an-email" },
            new object?[] { "Unborn", "Child", "+91 90000 00003", null, "2027-01-01" },
            new object?[] { "Short", "Number", "12" });

        var preview = await harness.CustomerImports.PreviewAsync(harness.Stream(file), "customers.xlsx");

        Assert.Equal("Customers", preview.Type);
        Assert.Equal(5, preview.TotalRows);
        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(4, preview.InvalidRows);
        Assert.Equal(0, preview.ExistingRows);
        Assert.Equal("Valid", preview.Rows[0].Status);

        Assert.Equal("First name is required.", Single(preview, "First name").Message);
        Assert.Equal("Last name is required.", Single(preview, "Last name").Message);
        Assert.Equal("Enter a valid email address.", Single(preview, "Email").Message);
        Assert.Equal("Date of birth cannot be in the future.", Single(preview, "Date of birth").Message);
        Assert.Equal("Enter a valid mobile number.", Single(preview, "Mobile").Message);
    }

    [Fact]
    public async Task Field_limits_match_the_customer_form_exactly()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[]
            {
                new string('A', 101), new string('B', 101), new string('9', 31),
                $"{new string('e', 250)}@example.com", null, new string('G', 21),
                new string('X', 501), new string('N', 2001), null
            });

        var preview = await harness.CustomerImports.PreviewAsync(harness.Stream(file), "customers.xlsx");

        Assert.Equal(1, preview.InvalidRows);
        Assert.Equal("First name must be 100 characters or fewer.", Single(preview, "First name").Message);
        Assert.Equal("Last name must be 100 characters or fewer.", Single(preview, "Last name").Message);
        Assert.Equal("Mobile number must be 30 characters or fewer.", Single(preview, "Mobile").Message);
        Assert.Equal("Email must be 256 characters or fewer.", Single(preview, "Email").Message);
        Assert.Equal("Gender must be 20 characters or fewer.", Single(preview, "Gender").Message);
        Assert.Equal("Address must be 500 characters or fewer.", Single(preview, "Address").Message);
        Assert.Equal("Note must be 2000 characters or fewer.", Single(preview, "Notes").Message);
    }

    [Fact]
    public async Task A_mobile_number_already_in_use_is_flagged_and_never_imported()
    {
        using var harness = new ImportHarness();
        await harness.SeedCustomerAsync("Priya", "Sharma", "+91 98765 43210", "priya@example.com");

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210", "amelia@example.com" });

        var preview = await harness.CustomerImports.PreviewAsync(harness.Stream(file), "customers.xlsx");

        Assert.Equal(1, preview.ExistingRows);
        Assert.Equal(0, preview.ValidRows);
        Assert.Contains("Priya Sharma", Single(preview, "Mobile").Message);
        Assert.Contains("never overwrites", Single(preview, "Mobile").Message);

        var result = await harness.CustomerImports.ExecuteAsync(harness.Stream(file), "customers.xlsx", harness.Actor, importDuplicates: false);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.SkippedExisting);

        await using var context = harness.Reopen();
        var priya = await context.Customers.SingleAsync();
        Assert.Equal("Priya", priya.FirstName);
        Assert.Equal("priya@example.com", priya.Email);
    }

    [Fact]
    public async Task The_same_mobile_twice_in_one_file_is_a_duplicate_and_only_one_is_imported()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210" },
            new object?[] { "Rahul", "Mehta", "+91 98765 43210" });

        var preview = await harness.CustomerImports.PreviewAsync(harness.Stream(file), "customers.xlsx");

        Assert.Equal(1, preview.DuplicateRows);
        Assert.Equal("Valid", preview.Rows[0].Status);
        Assert.Equal("DuplicateInFile", preview.Rows[1].Status);
        Assert.Contains("Same mobile number as row 2.", Single(preview, "Mobile").Message);

        var result = await harness.CustomerImports.ExecuteAsync(harness.Stream(file), "customers.xlsx", harness.Actor, importDuplicates: false);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.SkippedExisting);
        Assert.Equal(1, await harness.CustomerCountAsync());
    }

    [Fact]
    public async Task Importing_duplicates_requires_the_explicit_opt_in()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210" },
            new object?[] { "Rahul", "Mehta", "+91 98765 43210" });

        var result = await harness.CustomerImports.ExecuteAsync(harness.Stream(file), "customers.xlsx", harness.Actor, importDuplicates: true);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Created);
        Assert.Equal(2, await harness.CustomerCountAsync());
    }

    [Fact]
    public async Task Retrying_the_same_file_creates_nothing_new()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210", null, null, null, null, "Prefers evenings" },
            new object?[] { "Rahul", "Mehta", "+91 98123 45678" });

        var first = await harness.CustomerImports.ExecuteAsync(harness.Stream(file), "customers.xlsx", harness.Actor, importDuplicates: false);
        Assert.Equal(2, first.Created);

        var retry = await harness.CustomerImports.ExecuteAsync(harness.Stream(file), "customers.xlsx", harness.Actor, importDuplicates: false);

        Assert.True(retry.Succeeded);
        Assert.Equal(0, retry.Created);
        Assert.Equal(2, retry.SkippedExisting);
        Assert.Equal(2, await harness.CustomerCountAsync());
        Assert.Equal(1, await harness.NoteCountAsync());
    }

    [Fact]
    public async Task Previewing_and_cancelling_changes_nothing()
    {
        using var harness = new ImportHarness();
        await harness.SeedCustomerAsync("Priya", "Sharma", "+91 98765 43210", "priya@example.com");

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210" },
            new object?[] { null, null, "junk" });

        var preview = await harness.CustomerImports.PreviewAsync(harness.Stream(file), "customers.xlsx");

        Assert.Equal(1, preview.ExistingRows);
        Assert.Equal(1, preview.InvalidRows);
        Assert.Equal(1, await harness.CustomerCountAsync());
        Assert.Equal(0, await harness.NoteCountAsync());
    }

    [Fact]
    public async Task A_formula_anywhere_in_a_row_refuses_that_row()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210", new Formula("1+1") });

        var preview = await harness.CustomerImports.PreviewAsync(harness.Stream(file), "customers.xlsx");

        Assert.Equal(1, preview.InvalidRows);
        Assert.Equal("Formulas are not allowed. Enter a plain value.", Single(preview, "Email").Message);

        var result = await harness.CustomerImports.ExecuteAsync(harness.Stream(file), "customers.xlsx", harness.Actor, importDuplicates: false);

        Assert.Equal(0, result.Created);
        Assert.Equal(0, await harness.CustomerCountAsync());
    }

    [Fact]
    public async Task File_level_problems_are_refused_before_any_row_is_read()
    {
        using var harness = new ImportHarness();

        await RefuseAsync(harness, ImportTestExcel.For(ImportType.Customers), "customers.xlsx", "no data rows");
        await RefuseAsync(harness, new byte[] { 1, 2, 3, 4 }, "customers.xlsx", "not an .xlsx workbook");
        await RefuseAsync(harness, ImportTestExcel.For(ImportType.Customers, new object?[] { "A", "B", "99999" }), "customers.xls", "Import an .xlsx workbook");
        await RefuseAsync(harness, ImportTestExcel.EmptyWorkbook("Sheet1"), "customers.xlsx", "no sheet named 'Customers'");
        await RefuseAsync(harness, ImportTestExcel.Workbook("Customers", ["First name", "Something else"], []), "customers.xlsx", "must have exactly these columns");

        var tooManyRows = ImportTestExcel.For(
            ImportType.Customers,
            [.. Enumerable.Range(0, 1001).Select(index => new object?[] { $"First{index}", "Last", $"+91 9000{index:0000}" })]);

        await RefuseAsync(harness, tooManyRows, "customers.xlsx", "the limit is 1,000");
    }

    [Fact]
    public async Task The_error_report_names_every_problem_and_neutralises_formula_lookalikes()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210" },
            new object?[] { null, null, "+91 90000 00001" },
            new object?[] { "Bad", "Number", "+oops" });

        var report = await harness.CustomerImports.CreateErrorReportAsync(harness.Stream(file), "customers.xlsx");

        using var workbook = new XLWorkbook(new MemoryStream(report));
        var sheet = workbook.Worksheet("Errors");

        var rows = ReportRows(sheet).ToList();

        Assert.Contains(rows, cells => cells[0] == "3" && cells[1] == "First name" && cells[2] == "First name is required.");
        Assert.Contains(rows, cells => cells[0] == "4" && cells[1] == "Mobile" && cells[2] == "Enter a valid mobile number.");
        Assert.DoesNotContain(rows, cells => cells[0] == "2");

        // The formula-lookalike value lands as inert text: never a live formula,
        // and the leading apostrophe (whether stored as content or as a quote
        // prefix) keeps Excel from ever interpreting it.
        var lookalike = rows.Single(cells => cells[1] == "Mobile" && cells[3].EndsWith("+oops"));
        var lookalikeRowNumber = int.Parse(lookalike[0]);
        var valueCell = sheet.Cell(lookalikeRowNumber, 4);

        Assert.False(valueCell.HasFormula);
        Assert.Equal("+oops", valueCell.GetString().TrimStart('\''));
    }

    [Fact]
    public async Task A_file_level_problem_produces_a_downloadable_error_report_too()
    {
        using var harness = new ImportHarness();

        var file = ImportTestExcel.EmptyWorkbook("Sheet1");

        var report = await harness.CustomerImports.CreateErrorReportAsync(harness.Stream(file), "upload.xlsx");

        using var workbook = new XLWorkbook(new MemoryStream(report));
        var rows = ReportRows(workbook.Worksheet("Errors")).ToList();

        Assert.Contains(rows, cells => cells[0] == "-" && cells[1] == "File" && cells[2].Contains("no sheet named 'Customers'"));
    }

    [Fact]
    public async Task An_unexpected_failure_mid_file_saves_nothing()
    {
        using var harness = new ImportHarness();
        var source = new CancellationTokenSource();

        var imports = new CustomerImportService(
            harness.Files,
            new CustomerRepository(harness.Context),
            harness.Customers,
            new FailingAfterBeginUnitOfWork(harness.Context, source),
            harness.Database.Clock);

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210" },
            new object?[] { "Rahul", "Mehta", "+91 98123 45678" });

        // With the duplicates opt-in the rows go straight to the write, so the
        // fault lands inside the first row's SaveChangesAsync — after its
        // attempted++ but before any second row is attempted.
        var result = await imports.ExecuteAsync(
            harness.Stream(file), "customers.xlsx", harness.Actor, importDuplicates: true, source.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Failed);
        Assert.Contains("nothing was saved", result.Message);
        Assert.Equal(0, await harness.CustomerCountAsync());
    }

    private static ImportFieldError Single(ImportPreview preview, string field) =>
        preview.Errors.Single(error => error.Field == field);

    /// <summary>Asserts the problem arrived as a single "file" validation error carrying the given text.</summary>
    private static async Task RefuseAsync(ImportHarness harness, byte[] content, string fileName, string expectedText)
    {
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.CustomerImports.PreviewAsync(harness.Stream(content), fileName));

        Assert.Equal("file", Assert.Single(exception.Errors.Keys));
        Assert.Contains(expectedText, Assert.Single(Assert.Single(exception.Errors.Values)));
    }

    private static IEnumerable<string[]> ReportRows(IXLWorksheet sheet)
    {
        for (var row = 2; row <= sheet.LastRowUsed().RowNumber(); row++)
        {
            yield return
            [
                sheet.Cell(row, 1).GetString(),
                sheet.Cell(row, 2).GetString(),
                sheet.Cell(row, 3).GetString(),
                sheet.Cell(row, 4).GetString()
            ];
        }
    }
}
