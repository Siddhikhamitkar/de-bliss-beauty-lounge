using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Notifications.Dtos;
using DeBliss.Application.Notifications.Ports;
using DeBliss.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace DeBliss.Application.Notifications;

/// <summary>
/// Owns the reminder rules the owner configures and the WhatsApp connection
/// status the settings screen displays. Timed reminders can move within a
/// 30-day window; the event-based notices (confirmation, cancellation,
/// reschedule) fire immediately and have no timing to edit. The test message
/// goes through the exact same provider port as real reminders, so a success
/// here proves the production path, not a side door.
/// </summary>
public sealed class ReminderSettingsService(
    IReminderRuleRepository rules,
    INotificationProvider provider,
    ReminderMessageComposer composer,
    IUnitOfWork unitOfWork,
    ILogger<ReminderSettingsService> logger) : IReminderSettingsService
{
    /// <summary>Nothing useful can be scheduled more than 30 days ahead.</summary>
    public const int MaxOffsetMinutes = 30 * 24 * 60;

    public async Task<IReadOnlyList<ReminderRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var allRules = await rules.ListAsync(cancellationToken);

        return allRules
            .OrderBy(rule => rule.ReminderType)
            .ThenByDescending(rule => rule.OffsetMinutes)
            .Select(Map)
            .ToList();
    }

    public async Task<ReminderRuleDto> UpdateRuleAsync(
        Guid id,
        UpdateReminderRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var rule = await rules.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Reminder rule '{id}' was not found.");

        if (request.OffsetMinutes is { } offsetMinutes)
        {
            if (rule.ReminderType != ReminderType.Reminder)
            {
                throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["offsetMinutes"] =
                    [
                        "This notice is sent as soon as the change happens and has no timing to configure."
                    ]
                });
            }

            if (offsetMinutes < 1 || offsetMinutes > MaxOffsetMinutes)
            {
                throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["offsetMinutes"] =
                    [
                        $"Timing must be between 1 minute and {MaxOffsetMinutes} minutes (30 days)."
                    ]
                });
            }

            var duplicate = (await rules.ListAsync(cancellationToken))
                .Any(other =>
                    other.Id != rule.Id
                    && other.ReminderType == rule.ReminderType
                    && other.OffsetMinutes == offsetMinutes);

            if (duplicate)
            {
                throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["offsetMinutes"] =
                    [
                        "A reminder with the same timing already exists for this notification type."
                    ]
                });
            }

            rule.OffsetMinutes = offsetMinutes;
        }

        if (request.IsEnabled is { } isEnabled)
        {
            rule.IsEnabled = isEnabled;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(rule);
    }

    public Task<ReminderProviderStatusDto> GetProviderStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ReminderProviderStatusDto(provider.IsConfigured, provider.Description, "WhatsApp"));
    }

    public async Task<SendTestMessageResult> SendTestMessageAsync(
        SendTestMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!provider.IsConfigured)
        {
            return new SendTestMessageResult(false, null, ReminderFailureReasons.ProviderNotConfigured, null);
        }

        var phone = PhoneNumbers.TryNormalize(request.PhoneNumber);

        if (phone is null)
        {
            return new SendTestMessageResult(
                false,
                null,
                "Enter a valid international phone number in E.164 format, for example +14155552671.",
                null);
        }

        var templateId = provider.ResolveTemplateId(ReminderType.Confirmation);

        if (templateId is null)
        {
            return new SendTestMessageResult(false, null, ReminderFailureReasons.TemplateNotConfigured, null);
        }

        logger.LogInformation(
            "Sending a WhatsApp test message to {MaskedPhone}.",
            PhoneNumbers.Mask(phone));

        NotificationSendResult result;

        try
        {
            result = await provider.SendNotificationAsync(
                new NotificationMessage(
                    NotificationChannel.WhatsApp,
                    phone,
                    templateId,
                    composer.ComposeTest()),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            result = NotificationSendResult.Transient($"The provider call failed: {exception.Message}");
        }

        logger.LogInformation(
            "WhatsApp test message to {MaskedPhone} finished with success={Success}.",
            PhoneNumbers.Mask(phone),
            result.Success);

        return new SendTestMessageResult(
            result.Success,
            result.ProviderMessageId,
            result.FailureReason,
            result.ProviderStatus);
    }

    private static ReminderRuleDto Map(ReminderRule rule) =>
        new(rule.Id, rule.ReminderType.ToString(), rule.OffsetMinutes, rule.IsEnabled);
}
