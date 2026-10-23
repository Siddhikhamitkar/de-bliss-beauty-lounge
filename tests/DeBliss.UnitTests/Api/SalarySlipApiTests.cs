using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeBliss.Application.Identity;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.UnitTests.Staffing;
using Microsoft.Extensions.DependencyInjection;

namespace DeBliss.UnitTests.Api;

public sealed class SalarySlipApiTests
{
    [Fact]
    public async Task Owner_preview_save_history_and_download_preserve_exact_reviewed_pdf()
    {
        var fixture = new SalarySlipTestFixture(realPdf: true);
        await using var host = await SalaryAuthorizationHost.StartAsync(services => services.AddSingleton<ISalarySlipService>(fixture.Service));
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", RoleNames.Owner);
        using var finance = await host.Client.GetAsync($"/api/staff/{fixture.Member.Id}/salary");
        Assert.Equal(HttpStatusCode.OK, finance.StatusCode);
        Assert.True(finance.Headers.CacheControl?.NoStore);
        var values = await finance.Content.ReadFromJsonAsync<StaffSalaryDetails>();
        Assert.Equal(fixture.Member.BasicSalary, values?.BasicSalary);
        using var previewResponse = await host.Client.PostAsJsonAsync("/api/salary-slips/preview", fixture.Request());
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.True(previewResponse.Headers.CacheControl?.NoStore);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<SalarySlipPreview>())!;
        Assert.Empty(fixture.Slips.Items);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(preview.PdfBase64)));
        fixture.Member.FirstName = "Changed after preview";
        fixture.Member.BankAccountNumber = "changed";
        using var saveResponse = await host.Client.PostAsJsonAsync("/api/salary-slips", new SaveSalarySlipRequest(preview.PreviewToken));
        Assert.Equal(HttpStatusCode.Created, saveResponse.StatusCode);
        Assert.NotNull(saveResponse.Headers.Location);
        Assert.True(saveResponse.Headers.CacheControl?.NoStore);
        var saved = (await saveResponse.Content.ReadFromJsonAsync<SalarySlipDto>())!;
        Assert.Equal(preview.Slip.EmployeeName, saved.EmployeeName);
        Assert.Equal(preview.Slip.BankAccountNumber, saved.BankAccountNumber);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), saved.CreatedBy);
        Assert.Single(fixture.Slips.Items);
        using var download = await host.Client.GetAsync($"/api/salary-slips/{saved.Id}/pdf");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(download.Headers.CacheControl?.NoStore);
        Assert.Equal(Convert.FromBase64String(preview.PdfBase64), await download.Content.ReadAsByteArrayAsync());
        using var detail = await host.Client.GetAsync($"/api/salary-slips/{saved.Id}");
        Assert.Equal(saved, await detail.Content.ReadFromJsonAsync<SalarySlipDto>());
        using var history = await host.Client.GetAsync("/api/salary-slips?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var json = await history.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bank", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iban", json, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        using var duplicate = await host.Client.PostAsJsonAsync("/api/salary-slips", new SaveSalarySlipRequest(preview.PreviewToken));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Single(fixture.Slips.Items);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Preview_requires_a_valid_authenticated_creator(string subject)
    {
        var fixture = new SalarySlipTestFixture();
        await using var host = await SalaryAuthorizationHost.StartAsync(services => services.AddSingleton<ISalarySlipService>(fixture.Service));
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", RoleNames.Owner);
        host.Client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        using var response = await host.Client.PostAsJsonAsync("/api/salary-slips/preview", fixture.Request());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(fixture.Slips.Items);
    }

    [Fact]
    public async Task Other_owner_cannot_save_a_preview_they_did_not_review()
    {
        var fixture = new SalarySlipTestFixture();
        await using var host = await SalaryAuthorizationHost.StartAsync(services => services.AddSingleton<ISalarySlipService>(fixture.Service));
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", RoleNames.Owner);
        using var response = await host.Client.PostAsJsonAsync("/api/salary-slips/preview", fixture.Request());
        var preview = (await response.Content.ReadFromJsonAsync<SalarySlipPreview>())!;
        host.Client.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        using var save = await host.Client.PostAsJsonAsync("/api/salary-slips", new SaveSalarySlipRequest(preview.PreviewToken));
        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        Assert.Empty(fixture.Slips.Items);
    }

    [Fact]
    public async Task Invalid_period_and_negative_net_produce_field_validation_responses()
    {
        var fixture = new SalarySlipTestFixture();
        await using var host = await SalaryAuthorizationHost.StartAsync(services => services.AddSingleton<ISalarySlipService>(fixture.Service));
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", RoleNames.Owner);
        using var period = await host.Client.PostAsJsonAsync("/api/salary-slips/preview", fixture.Request() with { SalaryMonth = 13 });
        Assert.Equal(HttpStatusCode.BadRequest, period.StatusCode);
        Assert.Contains("salaryMonth", await period.Content.ReadAsStringAsync());
        using var net = await host.Client.PostAsJsonAsync("/api/salary-slips/preview", fixture.Request() with { OtherDeductions = 99999 });
        Assert.Equal(HttpStatusCode.BadRequest, net.StatusCode);
        Assert.Contains("netSalary", await net.Content.ReadAsStringAsync());
        Assert.Empty(fixture.Slips.Items);
    }

    [Fact]
    public async Task Unknown_slip_is_404_and_invalid_preview_does_not_persist()
    {
        var fixture = new SalarySlipTestFixture();
        await using var host = await SalaryAuthorizationHost.StartAsync(services => services.AddSingleton<ISalarySlipService>(fixture.Service));
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", RoleNames.Owner);
        using var detail = await host.Client.GetAsync($"/api/salary-slips/{Guid.NewGuid()}");
        using var pdf = await host.Client.GetAsync($"/api/salary-slips/{Guid.NewGuid()}/pdf");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pdf.StatusCode);
        using var save = await host.Client.PostAsJsonAsync("/api/salary-slips", new SaveSalarySlipRequest("invalid"));
        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        Assert.Empty(fixture.Slips.Items);
    }
}
