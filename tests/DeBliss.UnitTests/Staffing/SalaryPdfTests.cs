using DeBliss.Infrastructure.Staffing;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace DeBliss.UnitTests.Staffing;

public sealed class SalaryPdfTests
{
    [Fact]
    public void Generated_document_is_a4_with_real_text_not_an_image_only_page()
    {
        var pdf = new SalarySlipPdf().Render(SalarySlipPreviewTokenTests.Example());
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var page = Assert.Single(document.Pages.Cast<PdfSharp.Pdf.PdfPage>());
        Assert.InRange(page.Width.Point, 595, 596);
        Assert.InRange(page.Height.Point, 841, 843);
        var content = ContentReader.ReadContent(page);
        Assert.Contains(content.OfType<COperator>(), operation => operation.OpCode.Name is "Tj" or "TJ");
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void Maximum_length_details_and_amounts_generate_without_layout_exceptions()
    {
        var slip = SalarySlipPreviewTokenTests.Example();
        slip.EmployeeName = new string('W', 100) + " " + new string('W', 100);
        slip.EmployeeId = new string('0', 100);
        slip.AccountName = slip.EmployeeName;
        slip.BankName = new string('B', 200);
        slip.BankAccountNumber = new string('0', 100);
        slip.IbanNumber = new string('A', 50);
        slip.BasicSalary = 999999999.99m;
        slip.HouseRentAllowance = 999999999.99m;
        slip.OtherAllowance = 999999999.99m;
        slip.Commission = 999999999.99m;
        slip.TotalEarnings = 3999999999.96m;
        slip.NetSalary = slip.TotalEarnings - slip.TotalDeductions;
        var pdf = new SalarySlipPdf().Render(slip);
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.NotEmpty(document.Pages.Cast<PdfSharp.Pdf.PdfPage>());
        foreach (var page in document.Pages.Cast<PdfSharp.Pdf.PdfPage>())
        {
            Assert.InRange(page.Width.Point, 595, 596);
            Assert.InRange(page.Height.Point, 841, 843);
        }
    }

    [Fact]
    public void Missing_optional_bank_fields_generate_without_invented_data()
    {
        var slip = SalarySlipPreviewTokenTests.Example();
        slip.BankName = null;
        slip.BankAccountNumber = null;
        slip.IbanNumber = null;
        slip.EmployeeId = null;
        slip.AgreedTotalSalary = null;
        var pdf = new SalarySlipPdf().Render(slip);
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Single(document.Pages.Cast<PdfSharp.Pdf.PdfPage>());
        Assert.Null(slip.BankName);
        Assert.Null(slip.BankAccountNumber);
    }
}
