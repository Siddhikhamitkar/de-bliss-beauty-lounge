using System.Text.Json;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Domain.Staffing;

namespace DeBliss.UnitTests.Staffing;

public sealed class StaffSalarySlipServiceTests
{
    [Fact]
    public async Task History_contains_only_own_saved_slips_and_metadata()
    {
        var fixture = new SalarySlipTestFixture();
        var own = await SaveAsync(fixture);
        var other = new Staff { Id = Guid.NewGuid(), FirstName = "Other", LastName = "Employee", IsActive = true };
        fixture.Staff.Items.Add(other);
        var otherSlip = await SaveAsync(fixture, staffId: other.Id);

        var result = await fixture.Service.GetForStaffAsync(fixture.Member.Id, 1, 20);

        Assert.Equal(1, result.TotalCount);
        var summary = Assert.Single(result.Items);
        Assert.Equal(new StaffSalarySlipSummaryDto(own.Id, own.SalaryMonth, own.SalaryYear, own.PayDate, own.CreatedAt), summary);
        Assert.DoesNotContain(result.Items, item => item.Id == otherSlip.Id);
        var json = JsonSerializer.SerializeToElement(summary, JsonSerializerOptions.Web);
        Assert.Equal(new[] { "createdAt", "id", "payDate", "salaryMonth", "salaryYear" },
            json.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    [Fact]
    public async Task Pagination_counts_and_pages_only_the_linked_staff_history()
    {
        var fixture = new SalarySlipTestFixture();
        foreach (var month in new[] { 8, 9, 10 })
            await SaveAsync(fixture, month);
        var other = new Staff { Id = Guid.NewGuid(), FirstName = "Other", LastName = "Employee", IsActive = true };
        fixture.Staff.Items.Add(other);
        await SaveAsync(fixture, 11, other.Id);

        var result = await fixture.Service.GetForStaffAsync(fixture.Member.Id, 2, 1);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(2, result.Page);
        Assert.Equal(1, result.PageSize);
        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
        Assert.Equal(9, Assert.Single(result.Items).SalaryMonth);
        var beyond = await fixture.Service.GetForStaffAsync(fixture.Member.Id, 4, 1);
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.TotalCount);
        Assert.False(beyond.HasNextPage);
    }

    [Theory]
    [InlineData(0, 0, 1, 50)]
    [InlineData(-1, -1, 1, 50)]
    [InlineData(1, 10000, 1, 100)]
    [InlineData(int.MaxValue, 10000, int.MaxValue / 100, 100)]
    public async Task Empty_history_uses_existing_pagination_normalization(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var fixture = new SalarySlipTestFixture();

        var result = await fixture.Service.GetForStaffAsync(fixture.Member.Id, page, pageSize);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Equal(expectedPage, result.Page);
        Assert.Equal(expectedPageSize, result.PageSize);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public async Task Preview_is_neither_listed_nor_downloadable_until_owner_saves_it()
    {
        var fixture = new SalarySlipTestFixture();
        var preview = await fixture.Service.PreviewAsync(fixture.Request(), fixture.Owner);

        Assert.Empty((await fixture.Service.GetForStaffAsync(fixture.Member.Id, 1, 20)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.GetPdfForStaffAsync(fixture.Member.Id, preview.Slip.Id));
        Assert.Equal(0, fixture.Slips.SaveCalls);

        var saved = await fixture.Service.SaveAsync(new(preview.PreviewToken), fixture.Owner);

        Assert.Equal(saved.Id, Assert.Single((await fixture.Service.GetForStaffAsync(fixture.Member.Id, 1, 20)).Items).Id);
        var (metadata, bytes) = await fixture.Service.GetPdfForStaffAsync(fixture.Member.Id, saved.Id);
        Assert.Equal(saved.Id, metadata.Id);
        Assert.Equal(Convert.FromBase64String(preview.PdfBase64), bytes);
        Assert.Equal(1, fixture.Slips.SaveCalls);
    }

    [Fact]
    public async Task Download_returns_exact_historical_stored_bytes_not_a_regenerated_document()
    {
        var fixture = new SalarySlipTestFixture();
        var saved = await SaveAsync(fixture);
        var stored = Assert.Single(fixture.Slips.Items);
        // A historical renderer's bytes differ from today's fixture renderer.
        stored.Pdf = "%PDF-1.4\nHistorical salary snapshot\n%%EOF"u8.ToArray();
        fixture.Member.FirstName = "Changed";
        fixture.Member.BasicSalary = 9999;
        fixture.Member.TotalSalary = 99999;
        fixture.Member.BankName = "Changed bank";
        fixture.Member.BankAccountNumber = "Changed account";
        fixture.Member.IbanNumber = "Changed IBAN";
        fixture.Member.IsActive = false;
        fixture.Clock.Now = fixture.Clock.Now.AddYears(1);

        var (metadata, bytes) = await fixture.Service.GetPdfForStaffAsync(fixture.Member.Id, saved.Id);

        Assert.Equal(new StaffSalarySlipSummaryDto(saved.Id, saved.SalaryMonth, saved.SalaryYear, saved.PayDate, saved.CreatedAt), metadata);
        Assert.Same(stored.Pdf, bytes);
        Assert.Equal("%PDF-1.4\nHistorical salary snapshot\n%%EOF"u8.ToArray(), bytes);
        Assert.Equal(1, fixture.Slips.SaveCalls);
    }

    [Fact]
    public async Task Other_staff_and_missing_slips_have_the_same_sanitized_not_found()
    {
        var fixture = new SalarySlipTestFixture();
        var saved = await SaveAsync(fixture);
        var other = new Staff { Id = Guid.NewGuid(), FirstName = "Other", LastName = "Employee", IsActive = true };
        fixture.Staff.Items.Add(other);

        var denied = await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.GetPdfForStaffAsync(other.Id, saved.Id));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.GetPdfForStaffAsync(other.Id, Guid.NewGuid()));
        var emptyId = await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.GetPdfForStaffAsync(other.Id, Guid.Empty));

        Assert.Equal("Salary slip was not found.", denied.Message);
        Assert.Equal(denied.Message, missing.Message);
        Assert.Equal(denied.Message, emptyId.Message);
        Assert.Empty((await fixture.Service.GetForStaffAsync(other.Id, 1, 20)).Items);
        Assert.Single(fixture.Slips.Items);
    }

    [Fact]
    public async Task Empty_staff_identity_is_rejected_by_both_service_methods()
    {
        var fixture = new SalarySlipTestFixture();
        var saved = await SaveAsync(fixture);

        var list = await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Service.GetForStaffAsync(Guid.Empty, 1, 20));
        var pdf = await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Service.GetPdfForStaffAsync(Guid.Empty, saved.Id));

        Assert.Contains("staffId", list.Errors.Keys);
        Assert.Contains("staffId", pdf.Errors.Keys);
    }

    [Fact]
    public async Task Stale_staff_link_cannot_list_or_download_even_a_matching_stored_slip()
    {
        var fixture = new SalarySlipTestFixture();
        var saved = await SaveAsync(fixture);
        fixture.Staff.Items.Clear();

        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.GetForStaffAsync(fixture.Member.Id, 1, 20));
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.GetPdfForStaffAsync(fixture.Member.Id, saved.Id));
        Assert.Single(fixture.Slips.Items);
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
}
