using System.Net;
using System.Net.Http.Headers;
using DeBliss.Application.Identity;
using DeBliss.Application.Imports;
using DeBliss.UnitTests.Imports;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// The import endpoints over the real pipeline: template downloads, previews
/// and the permission split — customers follow the front-desk policy, inventory
/// is owner-only, staff and anonymous callers get nothing, and every refusal
/// the framework makes is a 403 before any controller code runs. All of this
/// runs against the factory's throwaway SQLite database.
/// </summary>
public class ImportsApiTests : ApiTestBase
{
    private const string ReceptionistEmail = "receptionist.imports@test.local";
    private const string ReceptionistPassword = "Test#Front123";
    private const string StaffEmail = "staff.imports@test.local";
    private const string StaffPassword = "Test#Staff123";

    [Fact]
    public async Task Templates_download_as_real_workbooks_for_every_flavour()
    {
        foreach (var (route, fileName) in new[]
        {
            ("/api/imports/customers/template", "customer-import-template.xlsx"),
            ("/api/imports/inventory/retail/template", "retail-products-import-template.xlsx"),
            ("/api/imports/inventory/salon-use/template", "salon-use-products-import-template.xlsx")
        })
        {
            var response = await Client.GetAsync(route);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                response.Content.Headers.ContentType?.MediaType);

            var disposition = response.Content.Headers.ContentDisposition?.FileName
                ?? response.Content.Headers.ContentDisposition?.FileNameStar
                ?? string.Empty;
            Assert.Contains(fileName, disposition);

            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B);
        }
    }

    [Fact]
    public async Task An_owner_previews_imports_and_retries_customers_end_to_end()
    {
        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210", "amelia@example.com" });

        var preview = await Client.PostAsync("/api/imports/customers/preview", Upload(file, "customers.xlsx"));

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewBody = await ReadAsync<PreviewResponse>(preview);
        Assert.Equal("Customers", previewBody.Type);
        Assert.Equal(1, previewBody.TotalRows);
        Assert.Equal(1, previewBody.ValidRows);

        var import = await Client.PostAsync("/api/imports/customers", Upload(file, "customers.xlsx", "false"));

        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var importBody = await ReadAsync<ImportResponse>(import);
        Assert.True(importBody.Succeeded);
        Assert.Equal(1, importBody.Created);

        var retry = await Client.PostAsync("/api/imports/customers", Upload(file, "customers.xlsx", "false"));

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retryBody = await ReadAsync<ImportResponse>(retry);
        Assert.Equal(0, retryBody.Created);
        Assert.Equal(1, retryBody.SkippedExisting);

        await using var context = Factory.Database.CreateContext();
        Assert.Equal(1, await context.Customers.CountAsync());
    }

    [Fact]
    public async Task An_owner_previews_and_imports_inventory_end_to_end()
    {
        var file = ImportTestExcel.For(ImportType.RetailProducts,
            new object?[] { "Keratin Shampoo", "Lumière", "LUM-KER-250", "Hair Care", "320.50", "549", "5", "24", "yes" });

        var preview = await Client.PostAsync("/api/imports/inventory/retail/preview", Upload(file, "retail.xlsx"));

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewBody = await ReadAsync<PreviewResponse>(preview);
        Assert.Equal("RetailProducts", previewBody.Type);
        Assert.Equal(1, previewBody.OpeningStockRows);

        var import = await Client.PostAsync("/api/imports/inventory/retail", Upload(file, "retail.xlsx"));

        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var importBody = await ReadAsync<ImportResponse>(import);
        Assert.True(importBody.Succeeded);
        Assert.Equal(1, importBody.Created);
        Assert.Equal(1, importBody.OpeningStockMovements);

        await using var context = Factory.Database.CreateContext();
        Assert.Equal(1, await context.InventoryProducts.CountAsync());
        Assert.Equal(1, await context.StockMovements.CountAsync());
    }

    [Fact]
    public async Task A_file_that_is_not_xlsx_is_refused_with_a_field_error()
    {
        var response = await Client.PostAsync("/api/imports/customers/preview", Upload(new byte[] { 1, 2, 3, 4 }, "customers.xlsx"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<ValidationProblem>(response);
        Assert.True(problem.Errors!.TryGetValue("file", out var messages));
        Assert.Contains("not an .xlsx", messages[0]);
    }

    [Fact]
    public async Task A_request_without_a_file_is_refused()
    {
        // A well-formed multipart form with no file part — an empty multipart
        // body would be rejected by the form reader before the action runs.
        var response = await Client.PostAsync(
            "/api/imports/customers/preview",
            new MultipartFormDataContent { { new StringContent("anything"), "notTheFile" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<ValidationProblem>(response);
        Assert.Contains("Choose an .xlsx file", problem.Errors["file"][0]);
    }

    [Fact]
    public async Task An_error_report_downloads_for_a_faulty_file()
    {
        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { null, null, "+91 90000 00001" });

        var response = await Client.PostAsync("/api/imports/customers/error-report", Upload(file, "customers.xlsx"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_receptionist_may_use_the_customer_import_end_to_end()
    {
        await Factory.SeedUserAsync(ReceptionistEmail, ReceptionistPassword, RoleNames.Receptionist);

        using var receptionist = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await AuthenticateAsync(receptionist, ReceptionistEmail, ReceptionistPassword);

        var file = ImportTestExcel.For(ImportType.Customers,
            new object?[] { "Amelia", "Fernandes", "+91 98765 43210" });

        var preview = await receptionist.PostAsync("/api/imports/customers/preview", Upload(file, "customers.xlsx"));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

        var import = await receptionist.PostAsync("/api/imports/customers", Upload(file, "customers.xlsx", "false"));
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        Assert.True((await ReadAsync<ImportResponse>(import)).Succeeded);
    }

    [Fact]
    public async Task A_receptionist_gets_403_on_every_inventory_import_route()
    {
        await Factory.SeedUserAsync(ReceptionistEmail, ReceptionistPassword, RoleNames.Receptionist);

        using var receptionist = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await AuthenticateAsync(receptionist, ReceptionistEmail, ReceptionistPassword);

        var retail = RetailFile();
        var salon = ImportTestExcel.For(ImportType.SalonUseProducts,
            new object?[] { "Hydrogen Peroxide 3%", "Salon Basics", "SB-H2O2-1L", "Colour", "ml", "500", "2000", null });

        foreach (var attempt in new Func<Task<HttpResponseMessage>>[]
        {
            () => receptionist.GetAsync("/api/imports/inventory/retail/template"),
            () => receptionist.PostAsync("/api/imports/inventory/retail/preview", Upload(retail, "retail.xlsx")),
            () => receptionist.PostAsync("/api/imports/inventory/retail", Upload(retail, "retail.xlsx")),
            () => receptionist.PostAsync("/api/imports/inventory/retail/error-report", Upload(retail, "retail.xlsx")),
            () => receptionist.GetAsync("/api/imports/inventory/salon-use/template"),
            () => receptionist.PostAsync("/api/imports/inventory/salon-use/preview", Upload(salon, "salon.xlsx")),
            () => receptionist.PostAsync("/api/imports/inventory/salon-use", Upload(salon, "salon.xlsx"))
        })
        {
            var response = await attempt();

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_staff_account_gets_403_on_customers_too()
    {
        await Factory.SeedUserAsync(StaffEmail, StaffPassword, RoleNames.Staff);

        using var staff = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await AuthenticateAsync(staff, StaffEmail, StaffPassword);

        var templateResponse = await staff.GetAsync("/api/imports/customers/template");
        Assert.Equal(HttpStatusCode.Forbidden, templateResponse.StatusCode);

        var previewResponse = await staff.PostAsync("/api/imports/customers/preview", Upload(CustomerFile(), "customers.xlsx"));
        Assert.Equal(HttpStatusCode.Forbidden, previewResponse.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_get_401()
    {
        var templateResponse = await AnonymousClient.GetAsync("/api/imports/customers/template");
        Assert.Equal(HttpStatusCode.Unauthorized, templateResponse.StatusCode);

        var previewResponse = await AnonymousClient.PostAsync("/api/imports/customers/preview", Upload(CustomerFile(), "customers.xlsx"));
        Assert.Equal(HttpStatusCode.Unauthorized, previewResponse.StatusCode);
    }

    [Fact]
    public async Task An_unknown_inventory_kind_is_not_a_route()
    {
        var response = await Client.GetAsync("/api/imports/inventory/beverages/template");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static byte[] CustomerFile() => ImportTestExcel.For(ImportType.Customers,
        new object?[] { "Amelia", "Fernandes", "+91 98765 43210" });

    private static byte[] RetailFile() => ImportTestExcel.For(ImportType.RetailProducts,
        new object?[] { "Keratin Shampoo", "Lumière", "LUM-KER-250", "Hair Care", "320.50", "549", "5", "24", "yes" });

    private static MultipartFormDataContent Upload(byte[] content, string fileName, string? importDuplicates = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", fileName);

        if (importDuplicates is not null)
        {
            form.Add(new StringContent(importDuplicates), "importDuplicates");
        }

        return form;
    }

    private sealed record PreviewResponse(
        string Type,
        string FileName,
        int TotalRows,
        int ValidRows,
        int InvalidRows,
        int DuplicateRows,
        int ExistingRows,
        int OpeningStockRows);

    private sealed record ImportResponse(
        bool Succeeded,
        int Created,
        int SkippedInvalid,
        int SkippedExisting,
        int Failed,
        int OpeningStockMovements);
}
