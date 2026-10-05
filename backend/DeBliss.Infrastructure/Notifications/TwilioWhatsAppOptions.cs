using Microsoft.Extensions.Configuration;

namespace DeBliss.Infrastructure.Notifications;

/// <summary>
/// The Twilio WhatsApp settings, read raw from the 'Twilio' configuration
/// section (matching how the rest of the host reads configuration). The
/// AccountSid, AuthToken and sender values come from environment variables or
/// user-secrets — never from a committed file. The object is never logged
/// wholesale; the provider exposes only a secret-free description.
/// </summary>
public sealed class TwilioWhatsAppOptions
{
    public const string SectionName = "Twilio";
    public const string SandboxMode = "Sandbox";
    public const string ProductionMode = "Production";

    /// <summary>Master switch. Off by default so a missing configuration never sends anything.</summary>
    public bool Enabled { get; init; }

    /// <summary>'Sandbox' or 'Production'. Production is the fail-safe default.</summary>
    public string Mode { get; init; } = ProductionMode;

    public string? AccountSid { get; init; }

    public string? AuthToken { get; init; }

    /// <summary>The sender, as 'whatsapp:+1...' — the sandbox number in Sandbox mode, the business sender in Production.</summary>
    public string? WhatsAppFrom { get; init; }

    /// <summary>Optional Messaging Service SID; when set it takes precedence over the from number.</summary>
    public string? MessagingServiceSid { get; init; }

    public string? AppointmentConfirmationTemplateSid { get; init; }

    public string? AppointmentReminderTemplateSid { get; init; }

    public string? AppointmentCancellationTemplateSid { get; init; }

    public string? AppointmentRescheduledTemplateSid { get; init; }

    public bool IsSandbox => string.Equals(Mode.Trim(), SandboxMode, StringComparison.OrdinalIgnoreCase);

    /// <summary>Which of the required settings are absent. Used for the status description, never for logging secrets' values.</summary>
    public IReadOnlyList<string> MissingSettings()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(AccountSid))
        {
            missing.Add("AccountSid");
        }

        if (string.IsNullOrWhiteSpace(AuthToken))
        {
            missing.Add("AuthToken");
        }

        if (string.IsNullOrWhiteSpace(WhatsAppFrom) && string.IsNullOrWhiteSpace(MessagingServiceSid))
        {
            missing.Add("WhatsAppFrom");
        }

        return missing;
    }

    public static TwilioWhatsAppOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var mode = section["Mode"];

        return new TwilioWhatsAppOptions
        {
            Enabled = bool.TryParse(section["Enabled"], out var enabled) && enabled,
            Mode = string.IsNullOrWhiteSpace(mode) ? ProductionMode : mode.Trim(),
            AccountSid = section["AccountSid"],
            AuthToken = section["AuthToken"],
            WhatsAppFrom = section["WhatsAppFrom"],
            MessagingServiceSid = section["MessagingServiceSid"],
            AppointmentConfirmationTemplateSid = section["AppointmentConfirmationTemplateSid"],
            AppointmentReminderTemplateSid = section["AppointmentReminderTemplateSid"],
            AppointmentCancellationTemplateSid = section["AppointmentCancellationTemplateSid"],
            AppointmentRescheduledTemplateSid = section["AppointmentRescheduledTemplateSid"]
        };
    }
}
