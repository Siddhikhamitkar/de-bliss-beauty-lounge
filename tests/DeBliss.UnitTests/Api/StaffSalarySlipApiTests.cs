using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Identity;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Domain.Staffing;
using DeBliss.UnitTests.Staffing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace DeBliss.UnitTests.Api;

public sealed class StaffSalarySlipApiTests
{
    private const string Route = "/api/staff/me/salary-slips";

    [Theory]
    [InlineData("other")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Staff_history_has_only_own_metadata_and_ignores_spoofed_staffId(string spoof)
    {
        var fixture = new SalarySlipTestFixture();
        var september = await SaveAsync(fixture, 9);
        var october = await SaveAsync(fixture);
        var other = new Staff { Id = Guid.NewGuid(), FirstName = "Other", LastName = "Employee", IsActive = true };
        fixture.Staff.Items.Add(other);
        await SaveAsync(fixture, 11, other.Id);
        await using var host = await StartAsync(fixture);
        SetIdentity(host.Client, RoleNames.Staff, fixture.Member.Id);
        host.Client.DefaultRequestHeaders.Add("X-Test-Subject", other.Id.ToString());
        var queryId = spoof == "other" ? other.Id.ToString() : spoof;

        using var response = await host.Client.GetAsync($"{Route}?page=1&pageSize=1&staffId={queryId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(new[] { "hasNextPage", "hasPreviousPage", "items", "page", "pageSize", "totalCount", "totalPages" },
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(2, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, root.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(1, root.GetProperty("pageSize").GetInt32());
        Assert.True(root.GetProperty("hasNextPage").GetBoolean());
        Assert.False(root.GetProperty("hasPreviousPage").GetBoolean());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        AssertMetadata(item, october);

        using var secondPage = await host.Client.GetAsync($"{Route}?page=2&pageSize=1&staffId={queryId}");
        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);
        var second = (await secondPage.Content.ReadFromJsonAsync<PagedResult<StaffSalarySlipSummaryDto>>())!;
        Assert.Equal(2, second.TotalCount);
        Assert.Equal(september.Id, Assert.Single(second.Items).Id);
        Assert.True(second.HasPreviousPage);
        Assert.False(second.HasNextPage);

        using var beyondPage = await host.Client.GetAsync($"{Route}?page=3&pageSize=1&staffId={queryId}");
        Assert.Equal(HttpStatusCode.OK, beyondPage.StatusCode);
        var beyond = (await beyondPage.Content.ReadFromJsonAsync<PagedResult<StaffSalarySlipSummaryDto>>())!;
        Assert.Empty(beyond.Items);
        Assert.Equal(2, beyond.TotalCount);
    }

    [Theory]
    [InlineData("", 1, 50)]
    [InlineData("?page=-1&pageSize=-1", 1, 50)]
    [InlineData("?page=0&pageSize=10000", 1, 100)]
    [InlineData("?page=2147483647&pageSize=100", int.MaxValue / 100, 100)]
    public async Task Empty_history_is_200_with_normalized_pagination(string query, int page, int pageSize)
    {
        var fixture = new SalarySlipTestFixture();
        await using var host = await StartAsync(fixture);
        SetIdentity(host.Client, RoleNames.Staff, fixture.Member.Id);

        using var response = await host.Client.GetAsync(Route + query);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<PagedResult<StaffSalarySlipSummaryDto>>())!;
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Equal(page, result.Page);
        Assert.Equal(pageSize, result.PageSize);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public async Task Pdf_is_an_attachment_with_exact_saved_historical_bytes_and_no_store()
    {
        var fixture = new SalarySlipTestFixture();
        var saved = await SaveAsync(fixture, 9);
        var stored = Assert.Single(fixture.Slips.Items);
        stored.Pdf = "%PDF-1.4\nSaved historical document\n%%EOF"u8.ToArray();
        var expectedBytes = stored.Pdf.ToArray();
        fixture.Member.FirstName = "Changed after saving";
        fixture.Member.BasicSalary = 9999;
        fixture.Member.TotalSalary = 99999;
        fixture.Member.BankName = "New bank";
        fixture.Member.BankAccountNumber = "New account";
        fixture.Member.IbanNumber = "New IBAN";
        fixture.Clock.Now = fixture.Clock.Now.AddYears(1);
        await using var host = await StartAsync(fixture);
        SetIdentity(host.Client, RoleNames.Staff, fixture.Member.Id);

        using var response = await host.Client.GetAsync($"{Route}/{saved.Id}/pdf?staffId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var filename = $"salary-slip-2026-09-{saved.Id}.pdf";
        Assert.Equal(filename, response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(filename, response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(expectedBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, fixture.Slips.SaveCalls);
    }

    [Fact]
    public async Task Other_staff_and_missing_pdfs_have_identical_sanitized_404_details_even_with_spoofed_query()
    {
        var fixture = new SalarySlipTestFixture();
        var ownerOfSlip = new Staff { Id = Guid.NewGuid(), FirstName = "Private", LastName = "Employee", IsActive = true };
        fixture.Staff.Items.Add(ownerOfSlip);
        var otherSlip = await SaveAsync(fixture, staffId: ownerOfSlip.Id);
        await using var host = await StartAsync(fixture);
        SetIdentity(host.Client, RoleNames.Staff, fixture.Member.Id);

        using var denied = await host.Client.GetAsync($"{Route}/{otherSlip.Id}/pdf?staffId={ownerOfSlip.Id}");
        using var missing = await host.Client.GetAsync($"{Route}/{Guid.NewGuid()}/pdf?staffId={ownerOfSlip.Id}");

        foreach (var response in new[] { denied, missing })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(response.Content.Headers.ContentDisposition);
            var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
            Assert.Equal("Not found.", problem.Title);
            Assert.Equal("Salary slip was not found.", problem.Detail);
            Assert.Equal(404, problem.Status);
            Assert.Empty(problem.Extensions);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Private", json);
            Assert.DoesNotContain("bank", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("netSalary", json);
            Assert.DoesNotContain("pdfBase64", json);
            Assert.DoesNotContain(ownerOfSlip.Id.ToString(), json);
        }
    }

    [Fact]
    public async Task Owner_workflow_stays_available_and_staff_cannot_see_preview_until_save()
    {
        var fixture = new SalarySlipTestFixture();
        await using var host = await StartAsync(fixture);
        // The owner having a staff link must not change owner endpoint access.
        SetIdentity(host.Client, RoleNames.Owner, fixture.Member.Id);
        using var finance = await host.Client.GetAsync($"/api/staff/{fixture.Member.Id}/salary");
        Assert.Equal(HttpStatusCode.OK, finance.StatusCode);
        Assert.Equal(fixture.Member.BankAccountNumber, (await finance.Content.ReadFromJsonAsync<StaffSalaryDetails>())!.BankAccountNumber);
        using var previewResponse = await host.Client.PostAsJsonAsync("/api/salary-slips/preview", fixture.Request());
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<SalarySlipPreview>())!;

        SetIdentity(host.Client, RoleNames.Staff, fixture.Member.Id);
        using var before = await host.Client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Empty((await before.Content.ReadFromJsonAsync<PagedResult<StaffSalarySlipSummaryDto>>())!.Items);
        using var unsavedPdf = await host.Client.GetAsync($"{Route}/{preview.Slip.Id}/pdf");
        Assert.Equal(HttpStatusCode.NotFound, unsavedPdf.StatusCode);
        Assert.Empty(fixture.Slips.Items);

        SetIdentity(host.Client, RoleNames.Owner, fixture.Member.Id);
        using var saveResponse = await host.Client.PostAsJsonAsync("/api/salary-slips", new SaveSalarySlipRequest(preview.PreviewToken));
        Assert.Equal(HttpStatusCode.Created, saveResponse.StatusCode);
        var saved = (await saveResponse.Content.ReadFromJsonAsync<SalarySlipDto>())!;
        using var ownerHistory = await host.Client.GetAsync("/api/salary-slips");
        Assert.Equal(HttpStatusCode.OK, ownerHistory.StatusCode);
        Assert.Equal(saved.NetSalary, Assert.Single((await ownerHistory.Content.ReadFromJsonAsync<PagedResult<SalarySlipSummaryDto>>())!.Items).NetSalary);
        using var ownerDetail = await host.Client.GetAsync($"/api/salary-slips/{saved.Id}");
        Assert.Equal(HttpStatusCode.OK, ownerDetail.StatusCode);
        Assert.Equal(saved, await ownerDetail.Content.ReadFromJsonAsync<SalarySlipDto>());
        using var ownerPdf = await host.Client.GetAsync($"/api/salary-slips/{saved.Id}/pdf");
        Assert.Equal(HttpStatusCode.OK, ownerPdf.StatusCode);
        Assert.Equal(Convert.FromBase64String(preview.PdfBase64), await ownerPdf.Content.ReadAsByteArrayAsync());

        fixture.Member.TotalSalary = 99999;
        fixture.Member.BankAccountNumber = "Updated after save";
        SetIdentity(host.Client, RoleNames.Staff, fixture.Member.Id);
        using var after = await host.Client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var json = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        AssertMetadata(Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()), saved);
        using var staffPdf = await host.Client.GetAsync($"{Route}/{saved.Id}/pdf");
        Assert.Equal(HttpStatusCode.OK, staffPdf.StatusCode);
        Assert.Equal(Convert.FromBase64String(preview.PdfBase64), await staffPdf.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, fixture.Slips.SaveCalls);
    }

    [Fact]
    public async Task Nonexistent_linked_staff_cannot_read_saved_records()
    {
        var fixture = new SalarySlipTestFixture();
        var saved = await SaveAsync(fixture);
        fixture.Staff.Items.Clear();
        await using var host = await StartAsync(fixture);
        SetIdentity(host.Client, RoleNames.Staff, fixture.Member.Id);

        using var history = await host.Client.GetAsync(Route);
        using var pdf = await host.Client.GetAsync($"{Route}/{saved.Id}/pdf");

        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pdf.StatusCode);
        Assert.Single(fixture.Slips.Items);
    }

    private static Task<SalaryAuthorizationHost> StartAsync(SalarySlipTestFixture fixture) =>
        SalaryAuthorizationHost.StartAsync(services => services.AddSingleton<ISalarySlipService>(fixture.Service));

    private static void SetIdentity(HttpClient client, string role, Guid staffId)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Remove("X-Test-Staff-Id");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Staff-Id", staffId.ToString());
    }

    private static async Task<SalarySlipDto> SaveAsync(SalarySlipTestFixture fixture, int month = 10, Guid? staffId = null)
    {
        var preview = await fixture.Service.PreviewAsync(fixture.Request() with
        {
            StaffId = staffId ?? fixture.Member.Id,
            SalaryMonth = month
        }, fixture.Owner);
        return await fixture.Service.SaveAsync(new(preview.PreviewToken), fixture.Owner);
    }

    private static void AssertMetadata(JsonElement item, SalarySlipDto saved)
    {
        Assert.Equal(new[] { "createdAt", "id", "payDate", "salaryMonth", "salaryYear" },
            item.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(saved.Id, item.GetProperty("id").GetGuid());
        Assert.Equal(saved.SalaryMonth, item.GetProperty("salaryMonth").GetInt32());
        Assert.Equal(saved.SalaryYear, item.GetProperty("salaryYear").GetInt32());
        Assert.Equal(saved.PayDate, item.GetProperty("payDate").Deserialize<DateOnly>());
        Assert.Equal(saved.CreatedAt, item.GetProperty("createdAt").GetDateTimeOffset());
    }
}
