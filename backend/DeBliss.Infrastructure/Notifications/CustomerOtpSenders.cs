using DeBliss.Application.Identity.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace DeBliss.Infrastructure.Notifications;

/// <summary>
/// Which <see cref="ICustomerOtpSender"/> to use, read from <c>CustomerOtp:Provider</c>:
/// <list type="bullet">
/// <item><c>TwilioSms</c> — real SMS through Twilio. Reuses <c>Twilio:AccountSid</c> /
/// <c>Twilio:AuthToken</c>; the sender is <c>CustomerOtp:Sms:MessagingServiceSid</c> or
/// <c>CustomerOtp:Sms:From</c> (an SMS-capable number).</item>
/// <item><c>Log</c> — writes the message to the API log. Development only: the
/// shipped <c>appsettings.Development.json</c> selects it.</item>
/// <item>anything else / unset — <c>Disabled</c>: nothing is sent and an error is
/// logged (without the code), so a misconfigured server fails safe.</item>
/// </list>
/// </summary>
public sealed class CustomerOtpSenderOptions
{
    public const string SectionName = "CustomerOtp";

    public string Provider { get; init; } = "Disabled";

    public string? From { get; init; }

    public string? MessagingServiceSid { get; init; }

    public string? AccountSid { get; init; }

    public string? AuthToken { get; init; }

    public static CustomerOtpSenderOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var twilio = configuration.GetSection(TwilioWhatsAppOptions.SectionName);

        return new CustomerOtpSenderOptions
        {
            Provider = section["Provider"]?.Trim() is { Length: > 0 } provider ? provider : "Disabled",
            From = section["Sms:From"],
            MessagingServiceSid = section["Sms:MessagingServiceSid"],
            AccountSid = twilio["AccountSid"],
            AuthToken = twilio["AuthToken"],
        };
    }

    public ICustomerOtpSender CreateSender(ILoggerFactory loggers) =>
        Provider.ToLowerInvariant() switch
        {
            "twiliosms" => new TwilioSmsOtpSender(this, loggers.CreateLogger<TwilioSmsOtpSender>()),
            "log" => new LoggingOtpSender(loggers.CreateLogger<LoggingOtpSender>()),
            _ => new DisabledOtpSender(loggers.CreateLogger<DisabledOtpSender>())
        };
}

/// <summary>Development stand-in: the code appears in the API console instead of on a phone.</summary>
public sealed class LoggingOtpSender(ILogger<LoggingOtpSender> logger) : ICustomerOtpSender
{
    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        logger.LogWarning("[DEV OTP] To {PhoneNumber}: {Message}", phoneNumber, message);
        return Task.CompletedTask;
    }
}

public sealed class DisabledOtpSender(ILogger<DisabledOtpSender> logger) : ICustomerOtpSender
{
    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        logger.LogError(
            "A customer verification code could not be delivered: no OTP provider is configured. " +
            "Set CustomerOtp:Provider to 'TwilioSms' (production) or 'Log' (development).");
        return Task.CompletedTask;
    }
}

public sealed class TwilioSmsOtpSender(CustomerOtpSenderOptions options, ILogger<TwilioSmsOtpSender> logger) : ICustomerOtpSender
{
    public async Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.AccountSid) || string.IsNullOrWhiteSpace(options.AuthToken)
            || (string.IsNullOrWhiteSpace(options.MessagingServiceSid) && string.IsNullOrWhiteSpace(options.From)))
        {
            throw new InvalidOperationException(
                "Twilio SMS for customer codes needs Twilio:AccountSid, Twilio:AuthToken and " +
                "CustomerOtp:Sms:MessagingServiceSid or CustomerOtp:Sms:From.");
        }

        var client = new TwilioRestClient(options.AccountSid, options.AuthToken);

        var create = new CreateMessageOptions(new PhoneNumber(phoneNumber)) { Body = message };

        if (!string.IsNullOrWhiteSpace(options.MessagingServiceSid))
        {
            create.MessagingServiceSid = options.MessagingServiceSid;
        }
        else
        {
            create.From = new PhoneNumber(options.From);
        }

        var result = await MessageResource.CreateAsync(create, client);

        logger.LogInformation("Customer code sent by SMS ({Sid}, status {Status}).", result.Sid, result.Status);
    }
}
