using DeBliss.Api.Security;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Domain.Staffing;
using Microsoft.AspNetCore.DataProtection;

namespace DeBliss.UnitTests.Staffing;

public sealed class SalarySlipPreviewTokenTests
{
    [Fact]
    public void Roundtrip_preserves_snapshot_and_exact_pdf_without_plaintext_banking()
    {
        var clock = new SalaryTestClock();
        var tokens = new SalarySlipPreviewTokens(new EphemeralDataProtectionProvider(), clock);
        var slip = Example();
        var token = tokens.Protect(slip);
        Assert.DoesNotContain(slip.BankAccountNumber!, token);
        Assert.DoesNotContain(slip.EmployeeName, token);
        var copy = tokens.Unprotect(token);
        Assert.Equal(slip.Id, copy.Id);
        Assert.Equal(slip.CreatedBy, copy.CreatedBy);
        Assert.Equal(slip.BankAccountNumber, copy.BankAccountNumber);
        Assert.Equal(slip.Pdf, copy.Pdf);
        Assert.Equal(slip.NetSalary, copy.NetSalary);
    }

    [Fact]
    public void Preview_expires_after_thirty_minutes_without_persistent_state()
    {
        var clock = new SalaryTestClock();
        var tokens = new SalarySlipPreviewTokens(new EphemeralDataProtectionProvider(), clock);
        var token = tokens.Protect(Example());
        clock.Now = clock.Now.AddMinutes(29);
        Assert.NotNull(tokens.Unprotect(token));
        clock.Now = clock.Now.AddMinutes(1);
        var error = Assert.Throws<ValidationFailedException>(() => tokens.Unprotect(token));
        Assert.Contains("previewToken", error.Errors.Keys);
    }

    [Fact]
    public void Tampering_and_different_protection_keys_are_rejected()
    {
        var clock = new SalaryTestClock();
        var tokens = new SalarySlipPreviewTokens(new EphemeralDataProtectionProvider(), clock);
        var token = tokens.Protect(Example());
        var modified = token[..20] + (token[20] == 'A' ? 'B' : 'A') + token[21..];
        Assert.Throws<ValidationFailedException>(() => tokens.Unprotect(modified));
        var other = new SalarySlipPreviewTokens(new EphemeralDataProtectionProvider(), clock);
        Assert.Throws<ValidationFailedException>(() => other.Unprotect(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-token")]
    [InlineData("eyJhZG1pbiI6dHJ1ZX0=")]
    public void Malformed_tokens_produce_sanitized_field_errors(string token)
    {
        var tokens = new SalarySlipPreviewTokens(new EphemeralDataProtectionProvider(), new SalaryTestClock());
        var error = Assert.Throws<ValidationFailedException>(() => tokens.Unprotect(token));
        Assert.Single(error.Errors);
        Assert.Contains("previewToken", error.Errors.Keys);
    }

    [Fact]
    public void Oversized_token_is_rejected_before_decryption()
    {
        var tokens = new SalarySlipPreviewTokens(new EphemeralDataProtectionProvider(), new SalaryTestClock());
        Assert.Throws<ValidationFailedException>(() => tokens.Unprotect(new string('A', 2_000_001)));
    }

    internal static SalarySlip Example() => new()
    {
        Id = Guid.NewGuid(), StaffId = Guid.NewGuid(), EmployeeName = "Test Employee",
        EmployeeId = "EMP-001", SalaryMonth = 10, SalaryYear = 2026, PayDate = new DateOnly(2026, 11, 1),
        WorkingDays = 26, TotalLeave = 2, LeaveWithoutPay = 1, LateMarks = 2,
        AgreedTotalSalary = 3100, BasicSalary = 2000.10m, HouseRentAllowance = 500.20m,
        OtherAllowance = 400.30m, Commission = 200.40m, LeaveDeduction = 100.10m,
        OtherDeductions = 0.20m, TotalEarnings = 3101m, TotalDeductions = 100.30m, NetSalary = 3000.70m,
        BankName = "Test Bank", AccountName = "Test Employee", BankAccountNumber = "0012345678",
        IbanNumber = "AE00 0012 3456", CreatedBy = Guid.NewGuid(),
        CreatedAt = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero),
        Pdf = "%PDF-1.7\nSnapshot test bytes"u8.ToArray(), TemplateVersion = "1"
    };
}

internal sealed class SalaryTestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
