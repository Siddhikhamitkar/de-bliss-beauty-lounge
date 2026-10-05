namespace DeBliss.Application.Notifications.Dtos;

/// <summary>One configurable reminder rule as the settings screen shows it.</summary>
public sealed record ReminderRuleDto(Guid Id, string ReminderType, int OffsetMinutes, bool IsEnabled);

/// <summary>Partial edit of one rule: any subset may be sent.</summary>
public sealed record UpdateReminderRuleRequest(bool? IsEnabled, int? OffsetMinutes);

/// <summary>Whether WhatsApp can send right now, for the settings status pill. Never contains credentials.</summary>
public sealed record ReminderProviderStatusDto(bool IsConfigured, string Description, string Channel);

/// <summary>Test-message payload. The server normalises the number to E.164.</summary>
public sealed record SendTestMessageRequest(string? PhoneNumber);

/// <summary>Outcome of a test send, surfaced to the operator who ran it.</summary>
public sealed record SendTestMessageResult(bool Success, string? ProviderMessageId, string? FailureReason, string? ProviderStatus);

/// <summary>One reminder row for history tables and the appointment panel.</summary>
public sealed record AppointmentReminderDto(
    Guid Id,
    Guid AppointmentId,
    Guid CustomerId,
    string CustomerName,
    DateTimeOffset? AppointmentStartsAt,
    string ReminderType,
    string Channel,
    string Status,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? SentAt,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    string? ProviderMessageId,
    string? FailureReason,
    DateTimeOffset CreatedAt);

/// <summary>Filters for the reminder history list. All parts optional.</summary>
public sealed record ReminderHistoryQuery(
    string? Status = null,
    string? ReminderType = null,
    Guid? AppointmentId = null,
    Guid? CustomerId = null,
    DateTimeOffset? ScheduledFrom = null,
    DateTimeOffset? ScheduledTo = null,
    int? Take = null);
