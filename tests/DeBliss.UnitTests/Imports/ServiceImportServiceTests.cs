using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using DeBliss.Infrastructure.Imports;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Imports;
using DeBliss.Application.Imports.Dtos;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;

namespace DeBliss.UnitTests.Imports;

public sealed class ServiceImportServiceTests
{
    private static readonly string[] Headers =
        ["Service name", "Category name", "Price (AED)", "Duration (minutes)", "Description", "Active", "Eligible staff"];

    [Fact]
    public async Task Template_has_exact_seven_headers_and_an_example_that_is_never_imported()
    {
        var fixture = new ServiceImportFixture();
        var bytes = fixture.Imports.CreateTemplate();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Services");
        Assert.Equal(3, (int)ImportType.Services);
        Assert.Contains(ImportType.Services, ImportSheets.All());
        Assert.Equal(Headers, sheet.Row(1).CellsUsed().Select(cell => cell.GetString()).ToArray());
        Assert.Equal(1, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(7, sheet.LastColumnUsed()!.ColumnNumber());
        Assert.False(workbook.Worksheet("Example").Cell(5, 1).IsEmpty());
        await Refuse(fixture, bytes, "services.xlsx", "no data rows");
        fixture.AssertNoWrites();
    }

    [Fact]
    public async Task Valid_rows_normalize_names_description_numbers_staff_and_creation_defaults()
    {
        var fixture = new ServiceImportFixture();
        var bytes = Excel(
            ["  Consultation  ", " hAiR ", "0", 1, "  A consultation  ", null, " ada lovelace ; GRACE HOPPER ; Ada Lovelace "],
            [new string('N', 150), "Hair", 100000m, 1440, new string('D', 1000), "yes", "Ada Lovelace"],
            ["Inactive service", "Hair", "1,234", "45", "   ", "no", "Inactive Stylist"],
            ["Unassigned draft", "Hair", "25.75", "30", null, false, null]);

        using var previewStream = new MemoryStream(bytes);
        var preview = await fixture.Imports.PreviewAsync(previewStream, "services.XLSX");
        Assert.Equal("Services", preview.Type);
        Assert.Equal("services.XLSX", preview.FileName);
        Assert.Equal(4, preview.TotalRows);
        Assert.Equal(4, preview.ValidRows);
        Assert.Equal(0, preview.OpeningStockRows);
        Assert.Empty(preview.Errors);
        Assert.All(preview.Rows, row => Assert.Equal("Valid", row.Status));
        fixture.AssertNoWrites();

        using var executeStream = new MemoryStream(bytes);
        var result = await fixture.Imports.ExecuteAsync(executeStream, "services.XLSX");
        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Created);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.OpeningStockMovements);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(0, fixture.Categories.AddCalls);
        Assert.Equal(3, fixture.Staff.Items.Count);
        Assert.All(fixture.Services.Items, service =>
        {
            Assert.Equal(fixture.Hair.Id, service.ServiceCategoryId);
            Assert.False(service.IsOnlineBookable);
            Assert.Equal(0, service.SortOrder);
        });
        var first = fixture.Services.Items[0];
        Assert.Equal("Consultation", first.Name);
        Assert.Equal("A consultation", first.Description);
        Assert.Equal(0m, first.Price);
        Assert.Equal(1, first.DurationMinutes);
        Assert.True(first.IsActive);
        Assert.Equal(new[] { fixture.Ada.Id, fixture.Grace.Id }.Order(), first.StaffServices.Select(item => item.StaffId).Order());
        Assert.Equal(100000m, fixture.Services.Items[1].Price);
        Assert.Equal(1440, fixture.Services.Items[1].DurationMinutes);
        Assert.Equal(1234m, fixture.Services.Items[2].Price);
        Assert.Null(fixture.Services.Items[2].Description);
        Assert.False(fixture.Services.Items[2].IsActive);
        Assert.Equal(fixture.Inactive.Id, Assert.Single(fixture.Services.Items[2].StaffServices).StaffId);
        Assert.Empty(fixture.Services.Items[3].StaffServices);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("active", true)]
    [InlineData("1", true)]
    [InlineData("no", false)]
    [InlineData(" FALSE ", false)]
    [InlineData("inactive", false)]
    [InlineData("0", false)]
    public async Task Active_accepts_the_shared_boolean_spellings(string active, bool expected)
    {
        var fixture = new ServiceImportFixture();
        var row = Row();
        row[5] = active;
        using var stream = new MemoryStream(Excel(row));
        Assert.Equal(1, (await fixture.Imports.ExecuteAsync(stream, "services.xlsx")).Created);
        Assert.Equal(expected, Assert.Single(fixture.Services.Items).IsActive);
    }

    public static IEnumerable<object?[]> InvalidCells()
    {
        foreach (var column in new[] { 0, 1, 2, 3 })
        {
            yield return [column, null];
            yield return [column, "   "];
        }
        yield return [0, new string('N', 151)];
        yield return [4, new string('D', 1001)];
        foreach (var price in new[] { "-0.01", "100000.01", "1.001", "abc", "1.234,50", "NaN" })
            yield return [2, price];
        foreach (var duration in new[] { "0", "-1", "1441", "1.5", "2147483648", "abc" })
            yield return [3, duration];
        yield return [5, "maybe"];
        yield return [6, "Ada"];
        yield return [6, "Ada  Lovelace"];
        yield return [6, "Ada Lovelace, Grace Hopper"];
    }

    [Theory]
    [MemberData(nameof(InvalidCells))]
    public async Task Invalid_fields_are_reported_at_the_excel_row_and_never_written(int column, object? value)
    {
        var fixture = new ServiceImportFixture();
        var row = Row();
        row[column] = value;
        var bytes = Excel(row);
        using var previewStream = new MemoryStream(bytes);
        var preview = await fixture.Imports.PreviewAsync(previewStream, "services.xlsx");
        Assert.Equal(1, preview.InvalidRows);
        Assert.Equal(0, preview.ValidRows);
        Assert.Equal("Invalid", Assert.Single(preview.Rows).Status);
        Assert.Contains(preview.Errors, error => error.RowNumber == 2 && error.Field == Headers[column]);
        fixture.AssertNoWrites();

        using var executeStream = new MemoryStream(bytes);
        var result = await fixture.Imports.ExecuteAsync(executeStream, "services.xlsx");
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.SkippedInvalid);
        Assert.Empty(fixture.Services.Items);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(0, fixture.Categories.AddCalls);
    }

    [Fact]
    public async Task Missing_and_ambiguous_categories_are_invalid_but_an_existing_inactive_category_is_allowed()
    {
        var fixture = new ServiceImportFixture();
        fixture.Hair.IsActive = false;
        fixture.Categories.Items.AddRange([
            new ServiceCategory { Id = Guid.NewGuid(), Name = "Nails" },
            new ServiceCategory { Id = Guid.NewGuid(), Name = " nails " }]);
        var bytes = Excel(Row("Allowed"), Row("Missing", "Unknown"), Row("Ambiguous", "NAILS"));
        using var previewStream = new MemoryStream(bytes);
        var preview = await fixture.Imports.PreviewAsync(previewStream, "services.xlsx");
        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(2, preview.InvalidRows);
        Assert.All(preview.Errors, error => Assert.Equal("Category name", error.Field));
        fixture.AssertNoWrites();

        using var executeStream = new MemoryStream(bytes);
        var result = await fixture.Imports.ExecuteAsync(executeStream, "services.xlsx");
        Assert.Equal(1, result.Created);
        Assert.Equal(2, result.SkippedInvalid);
        var service = Assert.Single(fixture.Services.Items);
        Assert.True(service.IsActive);
        Assert.False(service.CanBeBooked());
        Assert.Equal(fixture.Hair.Id, service.ServiceCategoryId);
        Assert.Equal(3, fixture.Categories.Items.Count);
        Assert.Equal(0, fixture.Categories.AddCalls);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("yes", "Inactive Stylist", false)]
    [InlineData("no", null, true)]
    [InlineData("no", "Inactive Stylist", true)]
    [InlineData("yes", "Ada Lovelace; Inactive Stylist", true)]
    [InlineData("yes", "Ada Lovelace; Unknown Person", false)]
    [InlineData("no", "Unknown Person", false)]
    public async Task Active_services_need_active_staff_and_unknown_names_fail_even_for_inactive_services(
        string? active, string? staff, bool valid)
    {
        var fixture = new ServiceImportFixture();
        var row = Row();
        row[5] = active;
        row[6] = staff;
        var bytes = Excel(row);
        using var previewStream = new MemoryStream(bytes);
        var preview = await fixture.Imports.PreviewAsync(previewStream, "services.xlsx");
        Assert.Equal(valid ? 1 : 0, preview.ValidRows);
        if (!valid)
            Assert.Contains(preview.Errors, error => error.Field == "Eligible staff");
        fixture.AssertNoWrites();
        using var executeStream = new MemoryStream(bytes);
        var result = await fixture.Imports.ExecuteAsync(executeStream, "services.xlsx");
        Assert.True(result.Succeeded);
        Assert.Equal(valid ? 1 : 0, result.Created);
        Assert.Equal(valid ? 0 : 1, result.SkippedInvalid);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    public async Task Ambiguous_staff_names_include_inactive_matches_and_never_pick_an_arbitrary_person(string active)
    {
        var fixture = new ServiceImportFixture();
        fixture.Staff.Items.Add(new Staff { Id = Guid.NewGuid(), FirstName = "ada", LastName = "lovelace", IsActive = false });
        var row = Row();
        row[5] = active;
        using var stream = new MemoryStream(Excel(row));
        var preview = await fixture.Imports.PreviewAsync(stream, "services.xlsx");
        Assert.Equal(1, preview.InvalidRows);
        Assert.Contains(preview.Errors, error => error.Field == "Eligible staff");
        fixture.AssertNoWrites();
        stream.Position = 0;
        var result = await fixture.Imports.ExecuteAsync(stream, "services.xlsx");
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.SkippedInvalid);
        Assert.Empty(fixture.Services.Items);
    }

    [Fact]
    public async Task Real_catalogue_validation_is_read_only_and_uses_the_same_domain_constraints()
    {
        var fixture = new ServiceImportFixture();
        var request = new SaveServiceRequest("Cut", fixture.Hair.Id, null, 30, 150m, true, false, 0, [fixture.Ada.Id]);
        await fixture.Catalogue.ValidateCreateAsync(request);
        var invalid = await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Catalogue.ValidateCreateAsync(request with
        {
            Name = new string('N', 151), Description = new string('D', 1001), CategoryId = Guid.NewGuid(),
            DurationMinutes = 1441, Price = 100000.01m, SortOrder = -1, EligibleStaffIds = [Guid.NewGuid()]
        }));
        foreach (var field in new[] { "name", "description", "categoryId", "durationMinutes", "price", "sortOrder", "eligibleStaffIds" })
            Assert.Contains(field, invalid.Errors.Keys);
        var existing = fixture.Existing();
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Catalogue.ValidateCreateAsync(request with { Name = " CUT " }));
        // Validation errors take precedence over an existing identity.
        await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Catalogue.ValidateCreateAsync(request with { Price = -1m }));
        Assert.Same(existing, Assert.Single(fixture.Services.Items));
        fixture.AssertNoWrites();
    }

    [Fact]
    public async Task Identity_is_trimmed_case_insensitive_name_plus_category_and_retry_never_overwrites()
    {
        var fixture = new ServiceImportFixture();
        var existing = fixture.Existing("Existing");
        var nails = new ServiceCategory { Id = Guid.NewGuid(), Name = "Nails" };
        fixture.Categories.Items.Add(nails);
        var invalid = Row("Bad");
        invalid[2] = "-1";
        var bytes = Excel(Row("Cut"), Row(" cUT ", " hair "), Row("Cut", "Nails"), Row(" EXISTING "), invalid);
        using var previewStream = new MemoryStream(bytes);
        var preview = await fixture.Imports.PreviewAsync(previewStream, "services.xlsx");
        Assert.Equal(2, preview.ValidRows);
        Assert.Equal(1, preview.DuplicateRows);
        Assert.Equal(1, preview.ExistingRows);
        Assert.Equal(1, preview.InvalidRows);
        Assert.Equal("DuplicateInFile", preview.Rows[1].Status);
        Assert.Equal("AlreadyExists", preview.Rows[3].Status);
        fixture.AssertNoWrites();

        using var executeStream = new MemoryStream(bytes);
        var result = await fixture.Imports.ExecuteAsync(executeStream, "services.xlsx");
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Created);
        Assert.Equal(2, result.SkippedExisting);
        Assert.Equal(1, result.SkippedInvalid);
        Assert.Equal(3, fixture.Services.Items.Count);
        Assert.Equal(2, fixture.Services.Items.Count(service => service.Name == "Cut"));
        AssertExistingUnchanged(fixture, existing, "Existing");

        using var retryStream = new MemoryStream(bytes);
        var retry = await fixture.Imports.ExecuteAsync(retryStream, "services.xlsx");
        Assert.True(retry.Succeeded);
        Assert.Equal(0, retry.Created);
        Assert.Equal(4, retry.SkippedExisting);
        Assert.Equal(1, retry.SkippedInvalid);
        Assert.Equal(2, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(3, fixture.Services.Items.Count);
        AssertExistingUnchanged(fixture, existing, "Existing");
    }

    [Fact]
    public async Task Execute_reads_the_submitted_workbook_again_and_revalidates_current_staff()
    {
        var fixture = new ServiceImportFixture();
        using var previewStream = new MemoryStream(Excel(Row("Preview only")));
        Assert.Equal(1, (await fixture.Imports.PreviewAsync(previewStream, "services.xlsx")).ValidRows);
        fixture.AssertNoWrites();
        fixture.Ada.IsActive = false;
        var draft = Row("Submitted draft");
        draft[5] = "no";
        using var executeStream = new MemoryStream(Excel(Row("Now invalid"), draft));
        var result = await fixture.Imports.ExecuteAsync(executeStream, "services.xlsx");
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.SkippedInvalid);
        Assert.Equal("Submitted draft", Assert.Single(fixture.Services.Items).Name);
    }

    [Fact]
    public async Task Execute_reads_staff_only_inside_the_transaction_and_sees_new_deactivations()
    {
        var fixture = new ServiceImportFixture();
        var transactionBegan = false;
        fixture.UnitOfWork.OnBegin = () =>
        {
            transactionBegan = true;
            fixture.Ada.IsActive = false;
        };
        fixture.Staff.BeforeRead = () => Assert.True(transactionBegan);
        using var stream = new MemoryStream(Excel(Row()));
        var result = await fixture.Imports.ExecuteAsync(stream, "services.xlsx");
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.SkippedInvalid);
        Assert.Contains(result.Errors, error => error.Field == "Eligible staff");
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
        Assert.Empty(fixture.Services.Items);
    }

    [Fact]
    public async Task Concurrent_creation_at_transaction_begin_is_skipped_without_overwriting()
    {
        var fixture = new ServiceImportFixture();
        Service? concurrent = null;
        fixture.UnitOfWork.OnBegin = () => concurrent = fixture.Existing("Cut");
        using var stream = new MemoryStream(Excel(Row(" CUT "), Row("New service")));
        var result = await fixture.Imports.ExecuteAsync(stream, "services.xlsx");
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.SkippedExisting);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(2, fixture.Services.Items.Count);
        AssertExistingUnchanged(fixture, Assert.IsType<Service>(concurrent), "Cut");
    }

    [Fact]
    public async Task Failure_on_second_save_rolls_back_the_first_save_and_pending_assignments()
    {
        var fixture = new ServiceImportFixture();
        var existing = fixture.Existing("Untouched");
        fixture.UnitOfWork.FailOnSave = 2;
        using var stream = new MemoryStream(Excel(Row("First"), Row("Second")));
        var result = await fixture.Imports.ExecuteAsync(stream, "services.xlsx");
        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.OpeningStockMovements);
        Assert.Equal(2, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(1, fixture.UnitOfWork.SuccessfulSaves);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
        Assert.Empty(fixture.Services.Pending);
        Assert.Same(existing, Assert.Single(fixture.Services.Items));
        Assert.Contains("nothing was saved", result.Message);
        Assert.DoesNotContain(ListUnitOfWork.PrivateFailure, result.Message!);
        AssertExistingUnchanged(fixture, existing, "Untouched");
    }

    [Fact]
    public async Task Cancelling_after_preview_or_before_any_operation_does_not_write()
    {
        var fixture = new ServiceImportFixture();
        var bytes = Excel(Row());
        using var previewStream = new MemoryStream(bytes);
        await fixture.Imports.PreviewAsync(previewStream, "services.xlsx");
        fixture.AssertNoWrites(); // Dismissing the confirmation dialog calls no execute endpoint.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var cancelledPreview = new MemoryStream(bytes);
        using var cancelledExecute = new MemoryStream(bytes);
        using var cancelledReport = new MemoryStream(bytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Imports.PreviewAsync(cancelledPreview, "services.xlsx", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Imports.ExecuteAsync(cancelledExecute, "services.xlsx", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Imports.CreateErrorReportAsync(cancelledReport, "services.xlsx", cancellation.Token));
        fixture.AssertNoWrites();
        Assert.Empty(fixture.Services.Items);
    }

    [Fact]
    public async Task Cancellation_after_first_save_cannot_leave_a_partial_import()
    {
        var fixture = new ServiceImportFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.UnitOfWork.AfterSave = cancellation.Cancel;
        using var stream = new MemoryStream(Excel(Row("First"), Row("Second")));
        // Implementations may propagate cancellation or report a rolled-back import.
        try
        {
            var result = await fixture.Imports.ExecuteAsync(stream, "services.xlsx", cancellation.Token);
            Assert.False(result.Succeeded);
            Assert.Equal(0, result.Created);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Assert.Equal(1, fixture.UnitOfWork.SuccessfulSaves);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
        Assert.Empty(fixture.Services.Items);
        Assert.Empty(fixture.Services.Pending);
    }

    [Theory]
    [InlineData(0, "1+1")]
    [InlineData(1, "1+1")]
    [InlineData(2, "1+1")]
    [InlineData(3, "1+1")]
    [InlineData(4, "1+1")]
    [InlineData(5, "1+1")]
    [InlineData(6, "1+1")]
    [InlineData(2, "1/0")]
    [InlineData(4, "SUM(")]
    [InlineData(6, "NONEXISTENTFUNCTION(1)")]
    public async Task Formulas_in_every_column_including_invalid_formulas_are_rejected_without_evaluation(int column, string formula)
    {
        var fixture = new ServiceImportFixture();
        var row = Row();
        row[column] = new Formula(formula);
        var bytes = Excel(row);
        using var previewStream = new MemoryStream(bytes);
        var preview = await fixture.Imports.PreviewAsync(previewStream, "services.xlsx");
        Assert.Equal(1, preview.InvalidRows);
        Assert.Contains(preview.Errors, error => error.RowNumber == 2 && error.Field == Headers[column]
            && error.Message.Contains("formula", StringComparison.OrdinalIgnoreCase));
        using var executeStream = new MemoryStream(bytes);
        Assert.Equal(0, (await fixture.Imports.ExecuteAsync(executeStream, "services.xlsx")).Created);
        Assert.Empty(fixture.Services.Items);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Malformed_empty_wrong_sheet_header_extension_and_oversized_files_fail_before_writes()
    {
        var fixture = new ServiceImportFixture();
        await Refuse(fixture, [], "services.xlsx", "empty");
        await Refuse(fixture, [1, 2, 3, 4], "services.xlsx", "not an .xlsx");
        await Refuse(fixture, [0x50, 0x4b, 0x03, 0x04, 0, 0, 0, 0], "services.xlsx", "could not be opened");
        await Refuse(fixture, Excel(Row()), "services.xls", ".xlsx");
        await Refuse(fixture, Excel(Row()), "services.csv", ".xlsx");
        await Refuse(fixture, ImportTestExcel.Workbook("Other", Headers, Row()), "services.xlsx", "no sheet named 'Services'");
        await Refuse(fixture, ImportTestExcel.EmptyWorkbook("Services"), "services.xlsx", "exactly these columns");
        await Refuse(fixture, Excel(), "services.xlsx", "no data rows");
        await Refuse(fixture, ImportTestExcel.Workbook("Services", Headers[..6], Row()[..6]), "services.xlsx", "exactly these columns");
        await Refuse(fixture, ImportTestExcel.Workbook("Services", [.. Headers, "Extra"], Row()), "services.xlsx", "exactly these columns");
        var reordered = Headers.ToArray();
        (reordered[0], reordered[1]) = (reordered[1], reordered[0]);
        await Refuse(fixture, ImportTestExcel.Workbook("Services", reordered, Row()), "services.xlsx", "exactly these columns");
        await Refuse(fixture, new byte[checked((int)ImportRules.MaxFileBytes + 1)], "services.xlsx", "limit");
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(ImportType.Customers)]
    [InlineData(ImportType.RetailProducts)]
    [InlineData(ImportType.SalonUseProducts)]
    [InlineData(ImportType.Services)]
    public void Shared_reader_accepts_its_own_templates_for_every_import_type(ImportType type)
    {
        var files = new ClosedXmlImportFiles();
        using var stream = new MemoryStream(files.CreateTemplate(ImportSheets.Template(type)));
        var read = files.Read(stream, ImportSheets.Describe(type).SheetName);
        Assert.Equal(ImportReadStatus.Ok, read.Status);
        Assert.Equal(ImportSheets.Describe(type).Headers, read.Sheet!.Headers);
        Assert.Empty(read.Sheet.Rows);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Workbook_content_type_uses_override_before_default_and_refuses_renamed_macro_files(bool useOverride, bool macro)
    {
        using var stream = new MemoryStream();
        stream.Write(Excel(Row()));
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("[Content_Types].xml")!;
            XDocument types;
            using (var input = entry.Open())
                types = XDocument.Load(input);
            XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
            var contentType = macro
                ? "application/vnd.ms-excel.sheet.macroEnabled.main+xml"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
            types.Root!.Elements(ns + "Override").Where(element => (string?)element.Attribute("PartName") == "/xl/workbook.xml").Remove();
            types.Root.Elements(ns + "Default").Single(element => (string?)element.Attribute("Extension") == "xml")
                .SetAttributeValue("ContentType", useOverride
                    ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" : contentType);
            if (useOverride)
                types.Root.Add(new XElement(ns + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", contentType)));
            entry.Delete();
            using var output = archive.CreateEntry("[Content_Types].xml").Open();
            types.Save(output);
        }
        stream.Position = 0;
        var read = new ClosedXmlImportFiles().Read(stream, "Services");
        Assert.Equal(macro ? ImportReadStatus.NotXlsx : ImportReadStatus.Ok, read.Status);
    }

    [Fact]
    public void Macro_workbook_selected_by_relationship_is_rejected_even_with_a_normal_workbook_decoy()
    {
        using var stream = new MemoryStream();
        stream.Write(Excel(Row()));
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var workbook = archive.GetEntry("xl/workbook.xml")!;
            using (var input = workbook.Open())
            using (var output = archive.CreateEntry("xl/actual.xml").Open())
                input.CopyTo(output);
            var relationships = archive.GetEntry("_rels/.rels")!;
            XDocument rels;
            using (var input = relationships.Open())
                rels = XDocument.Load(input);
            rels.Root!.Elements().Single(element => ((string?)element.Attribute("Type"))?.EndsWith("/officeDocument") == true)
                .SetAttributeValue("Target", "/xl/actual.xml");
            relationships.Delete();
            using (var output = archive.CreateEntry("_rels/.rels").Open())
                rels.Save(output);
            var entry = archive.GetEntry("[Content_Types].xml")!;
            XDocument types;
            using (var input = entry.Open())
                types = XDocument.Load(input);
            types.Root!.Add(new XElement(types.Root.Name.Namespace + "Override",
                new XAttribute("PartName", "/xl/actual.xml"),
                new XAttribute("ContentType", "application/vnd.ms-excel.sheet.macroEnabled.main+xml")));
            entry.Delete();
            using (var output = archive.CreateEntry("[Content_Types].xml").Open())
                types.Save(output);
        }
        stream.Position = 0;
        Assert.Equal(ImportReadStatus.NotXlsx, new ClosedXmlImportFiles().Read(stream, "Services").Status);
    }

    [Fact]
    public async Task Unnamed_extra_data_columns_and_header_formulas_are_rejected()
    {
        var fixture = new ServiceImportFixture();
        await Refuse(fixture, ImportTestExcel.Workbook("Services", Headers, [.. Row(), "Unexpected"]), "services.xlsx", "Column 8 (no header)");
        using var workbook = new XLWorkbook(new MemoryStream(Excel(Row())));
        workbook.Worksheet("Services").Cell(1, 1).FormulaA1 = "1/0";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        await Refuse(fixture, stream.ToArray(), "services.xlsx", "could not be opened");
        fixture.AssertNoWrites();
    }

    [Fact]
    public async Task Exactly_1000_rows_are_allowed_but_1001_are_rejected_and_preview_is_bounded()
    {
        var fixture = new ServiceImportFixture();
        var rows = Enumerable.Range(1, 1001).Select(index => Row($"Service {index}")).ToArray();
        using var stream = new MemoryStream(Excel(rows[..1000]));
        var preview = await fixture.Imports.PreviewAsync(stream, "services.xlsx");
        Assert.Equal(1000, preview.TotalRows);
        Assert.Equal(1000, preview.ValidRows);
        Assert.Equal(ImportRules.PreviewRowCount, preview.Rows.Count);
        await Refuse(fixture, Excel(rows), "services.xlsx", "limit");
        fixture.AssertNoWrites();
    }

    [Fact]
    public async Task Blank_rows_are_ignored_without_losing_original_excel_row_numbers()
    {
        var fixture = new ServiceImportFixture();
        using var stream = new MemoryStream(Excel(new object?[7], Row("Valid"), ["Invalid", "Hair", "bad", 30, null, "no", null]));
        var preview = await fixture.Imports.PreviewAsync(stream, "services.xlsx");
        Assert.Equal(2, preview.TotalRows);
        Assert.Equal(new[] { 3, 4 }, preview.Rows.Select(row => row.RowNumber));
        Assert.Equal(4, Assert.Single(preview.Errors).RowNumber);
        fixture.AssertNoWrites();
    }

    [Fact]
    public async Task Error_reports_include_row_and_file_errors_as_safe_text_without_writes()
    {
        var fixture = new ServiceImportFixture();
        fixture.Existing("Existing");
        var bytes = Excel(Row("First", "=1+1"), Row("Second", "+SUM(1)"), Row("Third", "-1"), Row("Fourth", "@name"), Row("Existing"), Row("Cut"), Row(" cut "));
        using var previewStream = new MemoryStream(bytes);
        var preview = await fixture.Imports.PreviewAsync(previewStream, "services.xlsx");
        using var reportStream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(new MemoryStream(await fixture.Imports.CreateErrorReportAsync(reportStream, "services.xlsx")));
        var sheet = workbook.Worksheet("Errors");
        Assert.Equal(new[] { "Row", "Field", "Problem", "Cell value" }, sheet.Row(1).CellsUsed().Select(cell => cell.GetString()));
        Assert.Equal(preview.Errors.Count + 1, sheet.LastRowUsed()!.RowNumber());
        for (var index = 0; index < preview.Errors.Count; index++)
        {
            var error = preview.Errors[index];
            Assert.Equal(error.RowNumber.ToString(), sheet.Cell(index + 2, 1).GetString());
            Assert.Equal(error.Field, sheet.Cell(index + 2, 2).GetString());
            Assert.Equal(error.Message, sheet.Cell(index + 2, 3).GetString());
        }
        Assert.Equal("=1+1", sheet.Cell(2, 4).GetString());
        Assert.Equal("+SUM(1)", sheet.Cell(3, 4).GetString());
        Assert.Equal("-1", sheet.Cell(4, 4).GetString());
        Assert.Equal("@name", sheet.Cell(5, 4).GetString());
        foreach (var row in Enumerable.Range(2, 4))
        {
            Assert.Equal(XLDataType.Text, sheet.Cell(row, 4).DataType);
            Assert.True(sheet.Cell(row, 4).Style.IncludeQuotePrefix);
        }
        Assert.All(sheet.CellsUsed(), cell => Assert.False(cell.HasFormula));
        using var invalidStream = new MemoryStream([1, 2, 3]);
        using var fileReport = new XLWorkbook(new MemoryStream(await fixture.Imports.CreateErrorReportAsync(invalidStream, "services.xlsx")));
        Assert.Equal("File", fileReport.Worksheet("Errors").Cell(2, 2).GetString());
        Assert.Contains("not an .xlsx", fileReport.Worksheet("Errors").Cell(2, 3).GetString());
        fixture.AssertNoWrites();
    }

    private static object?[] Row(string name = "Cut", string category = "Hair") =>
        [name, category, "150.50", "45", null, null, "Ada Lovelace"];

    private static byte[] Excel(params object?[][] rows) => ImportTestExcel.Workbook("Services", Headers, rows);

    private static async Task Refuse(ServiceImportFixture fixture, byte[] bytes, string fileName, string message)
    {
        using var previewStream = new MemoryStream(bytes);
        var previewError = await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Imports.PreviewAsync(previewStream, fileName));
        Assert.Equal("file", Assert.Single(previewError.Errors.Keys));
        Assert.Contains(previewError.Errors["file"], text => text.Contains(message, StringComparison.OrdinalIgnoreCase));
        using var executeStream = new MemoryStream(bytes);
        var executeError = await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Imports.ExecuteAsync(executeStream, fileName));
        Assert.Equal("file", Assert.Single(executeError.Errors.Keys));
        Assert.Contains(executeError.Errors["file"], text => text.Contains(message, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertExistingUnchanged(ServiceImportFixture fixture, Service existing, string name)
    {
        Assert.Contains(existing, fixture.Services.Items);
        Assert.Equal(name, existing.Name);
        Assert.Equal(fixture.Hair.Id, existing.ServiceCategoryId);
        Assert.Equal(77m, existing.Price);
        Assert.Equal(25, existing.DurationMinutes);
        Assert.Equal("Keep this description", existing.Description);
        Assert.False(existing.IsActive);
        Assert.True(existing.IsOnlineBookable);
        Assert.Equal(9, existing.SortOrder);
        Assert.Equal(fixture.Grace.Id, Assert.Single(existing.StaffServices).StaffId);
    }
}
