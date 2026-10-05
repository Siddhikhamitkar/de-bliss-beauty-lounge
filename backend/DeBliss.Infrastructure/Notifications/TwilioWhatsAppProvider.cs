using System.Text.Json;
using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Ports;
using DeBliss.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Twilio.Clients;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace DeBliss.Infrastructure.Notifications;

/// <summary>
/// The Twilio WhatsApp implementation of the delivery port. This is the only
/// place in the solution that references the Twilio SDK; the reminder engine
/// stays provider-agnostic. Messages go out as approved WhatsApp templates
/// (ContentSid + ContentVariables); in Sandbox mode a missing template falls
/// back to a free-form message so the sandbox can be exercised end to end.
/// Provider-side failures are returned as results, never thrown, and no
/// credential or phone number ever reaches a log line.
/// </summary>
public sealed class TwilioWhatsAppProvider : INotificationProvider
{
    /// <summary>Sentinel template id marking 'compose a free-form message'; used only in Sandbox mode.</summary>
    public const string SandboxFreeformTemplateId = "sandbox:freeform";

    private const int MaxFailureReasonLength = 300;

    // Twilio error codes that a retry cannot fix: invalid or unreachable
    // recipient, sender not on WhatsApp, missing sandbox join, expired session
    // window without a template, bad credentials at the account level.
    private static readonly HashSet<int> PermanentErrorCodes =
    [
        20003, 21211, 21214, 21408, 21606, 21610, 21612, 21614, 63007, 63016, 63024
    ];

    // Throttling and Twilio-side internal errors are worth another attempt.
    private static readonly HashSet<int> TransientErrorCodes = [20429, 20500, 20501, 20502, 20503];

    private static readonly Dictionary<int, string> ErrorCodeDescriptions = new()
    {
        [20003] = "Twilio rejected the account credentials.",
        [21211] = "The recipient phone number is not a valid mobile number.",
        [21214] = "The recipient number is not verified for this Twilio trial account.",
        [21408] = "This Twilio account is not allowed to send to the recipient's region.",
        [21606] = "The configured sender is not a WhatsApp-enabled number.",
        [21610] = "The recipient has opted out of messages at Twilio.",
        [21612] = "The recipient number cannot receive WhatsApp messages.",
        [21614] = "The recipient number is not a valid mobile number.",
        [63007] = "Twilio's WhatsApp sandbox does not recognise this sender and recipient pair. Join the sandbox from the recipient's phone first.",
        [63016] = "The 24-hour WhatsApp session has expired; an approved template is required for this message.",
        [63024] = "The recipient has not accepted the WhatsApp opt-in.",
        [20429] = "Twilio is throttling requests; the message will be retried.",
    };

    private readonly TwilioWhatsAppOptions options;
    private readonly ILogger<TwilioWhatsAppProvider> logger;
    private readonly TwilioRestClient? client;

    public TwilioWhatsAppProvider(TwilioWhatsAppOptions options, ILogger<TwilioWhatsAppProvider> logger)
    {
        this.options = options;
        this.logger = logger;

        if (IsConfigured)
        {
            client = new TwilioRestClient(options.AccountSid!, options.AuthToken!);
        }
    }

    public bool IsConfigured =>
        options.Enabled && options.MissingSettings().Count == 0;

    public string Description
    {
        get
        {
            if (!options.Enabled)
            {
                return "WhatsApp reminders are turned off. Set Twilio:Enabled to true to turn them on.";
            }

            var missing = options.MissingSettings();

            if (missing.Count > 0)
            {
                return $"Twilio WhatsApp is not configured. Missing: {string.Join(", ", missing)}. " +
                       "Configure them via environment variables (Twilio__AccountSid, Twilio__AuthToken, Twilio__WhatsAppFrom).";
            }

            return options.IsSandbox
                ? "Twilio WhatsApp sandbox is connected."
                : "Twilio WhatsApp is connected (production sender).";
        }
    }

    public string? ResolveTemplateId(ReminderType reminderType)
    {
        var configured = reminderType switch
        {
            ReminderType.Confirmation => options.AppointmentConfirmationTemplateSid,
            ReminderType.Reminder => options.AppointmentReminderTemplateSid,
            ReminderType.Cancellation => options.AppointmentCancellationTemplateSid,
            ReminderType.Rescheduled => options.AppointmentRescheduledTemplateSid,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        // Sandbox does not require pre-approved templates; fall back to a
        // free-form message so the sandbox flow can be tested. Production
        // refuses rather than violating the template requirement.
        return options.IsSandbox ? SandboxFreeformTemplateId : null;
    }

    public async Task<NotificationSendResult> SendNotificationAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return NotificationSendResult.Permanent("WhatsApp sending is not configured.");
        }

        if (message.Channel != NotificationChannel.WhatsApp)
        {
            return NotificationSendResult.Permanent($"{message.Channel} is not a supported channel.");
        }

        try
        {
            var result = await MessageResource.CreateAsync(BuildMessage(message), client);

            return NotificationSendResult.Sent(result.Sid, result.Status?.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RestException exception)
        {
            return FromRestException(exception);
        }
        catch (ApiConnectionException)
        {
            return NotificationSendResult.Transient("Twilio could not be reached; the message will be retried.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The Twilio send attempt failed unexpectedly.");

            return NotificationSendResult.Transient("The WhatsApp provider call failed unexpectedly.");
        }
    }

    private CreateMessageOptions BuildMessage(NotificationMessage message)
    {
        var options = new CreateMessageOptions(new PhoneNumber($"whatsapp:{message.RecipientE164}"));

        if (!string.IsNullOrWhiteSpace(this.options.MessagingServiceSid))
        {
            options.MessagingServiceSid = this.options.MessagingServiceSid.Trim();
        }
        else
        {
            options.From = new PhoneNumber(NormalizeWhatsAppAddress(this.options.WhatsAppFrom!));
        }

        if (message.TemplateId == SandboxFreeformTemplateId)
        {
            options.Body = ComposeFreeformBody(message.Variables);
        }
        else
        {
            options.ContentSid = message.TemplateId;
            options.ContentVariables = JsonSerializer.Serialize(message.Variables);
        }

        return options;
    }

    private NotificationSendResult FromRestException(RestException exception)
    {
        var reason = ErrorCodeDescriptions.TryGetValue(exception.Code, out var description)
            ? description
            : $"Twilio rejected the message (code {exception.Code}).";

        if (reason.Length > MaxFailureReasonLength)
        {
            reason = reason[..MaxFailureReasonLength];
        }

        var providerStatus = exception.Status.ToString();

        if (PermanentErrorCodes.Contains(exception.Code))
        {
            return NotificationSendResult.Permanent(reason, providerStatus);
        }

        if (TransientErrorCodes.Contains(exception.Code))
        {
            return NotificationSendResult.Transient(reason, providerStatus);
        }

        return exception.Status == 429 || exception.Status >= 500
            ? NotificationSendResult.Transient(reason, providerStatus)
            : NotificationSendResult.Permanent(reason, providerStatus);
    }

    private static string NormalizeWhatsAppAddress(string address)
    {
        var trimmed = address.Trim();

        return trimmed.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"whatsapp:{trimmed}";
    }

    /// <summary>
    /// Renders the template variables as a plain message. Only reached in
    /// Sandbox mode, where free-form sends are allowed; production always
    /// sends the configured approved template.
    /// </summary>
    private static string ComposeFreeformBody(IReadOnlyDictionary<string, string> variables)
    {
        string Get(string key) => variables.TryGetValue(key, out var value) ? value : string.Empty;

        return $"{Get(ReminderTemplateVariables.CustomerName)}, this is a message from " +
               $"{Get(ReminderTemplateVariables.SalonName)}. " +
               $"{Get(ReminderTemplateVariables.ServiceName)} on " +
               $"{Get(ReminderTemplateVariables.AppointmentDate)} at " +
               $"{Get(ReminderTemplateVariables.AppointmentTime)} with " +
               $"{Get(ReminderTemplateVariables.StaffName)}.";
    }
}
