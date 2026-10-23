using System.Security.Cryptography;
using System.Text.Json;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;
using Microsoft.AspNetCore.DataProtection;

namespace DeBliss.Api.Security;

public sealed class SalarySlipPreviewTokens(IDataProtectionProvider provider, TimeProvider clock)
    : ISalarySlipPreviewTokens
{
    private readonly IDataProtector protector = provider.CreateProtector("DeBliss.SalarySlip.Preview.v1");

    public string Protect(SalarySlip slip) => protector.Protect(JsonSerializer.Serialize(
        new PreviewEnvelope(clock.GetUtcNow().AddMinutes(30), slip)));

    public SalarySlip Unprotect(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2_000_000)
            throw InvalidPreview();

        try
        {
            var envelope = JsonSerializer.Deserialize<PreviewEnvelope>(protector.Unprotect(token));
            if (envelope is null || envelope.ExpiresAt <= clock.GetUtcNow() || envelope.Slip is null)
                throw InvalidPreview();
            return envelope.Slip;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        {
            throw InvalidPreview();
        }
    }

    private static ValidationFailedException InvalidPreview() => new(new Dictionary<string, string[]>
    {
        ["previewToken"] = ["This preview has expired or is invalid. Generate the salary slip again."]
    });

    private sealed record PreviewEnvelope(DateTimeOffset ExpiresAt, SalarySlip Slip);
}
