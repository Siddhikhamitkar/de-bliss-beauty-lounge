using DeBliss.Domain.Staffing;

namespace DeBliss.Application.Staffing.Ports;

public interface ISalarySlipPdf
{
    byte[] Render(SalarySlip slip);
}

public interface ISalarySlipPreviewTokens
{
    string Protect(SalarySlip slip);

    SalarySlip Unprotect(string token);
}
