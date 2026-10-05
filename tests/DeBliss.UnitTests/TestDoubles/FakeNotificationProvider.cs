using DeBliss.Application.Notifications.Ports;
using DeBliss.Domain.Notifications;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// Scriptable stand-in for a real notification provider, so reminder tests
/// never touch a live channel. Records every message it is handed and answers
/// with queued results, a result function, or a deterministic success.
/// </summary>
internal sealed class FakeNotificationProvider : INotificationProvider
{
    public bool IsConfigured { get; set; } = true;

    public string Description { get; set; } = "Fake WhatsApp provider for tests.";

    /// <summary>Per-type template ids; a missing or null entry means "no template configured".</summary>
    public Dictionary<ReminderType, string?> Templates { get; } = new()
    {
        [ReminderType.Confirmation] = "HXconfirmation",
        [ReminderType.Reminder] = "HXreminder",
        [ReminderType.Cancellation] = "HXcancellation",
        [ReminderType.Rescheduled] = "HXrescheduled"
    };

    /// <summary>Results answered in order; when empty the factory (or a success) decides.</summary>
    public Queue<NotificationSendResult> QueuedResults { get; } = new();

    public Func<NotificationMessage, NotificationSendResult>? ResultFactory { get; set; }

    /// <summary>When set, the send throws — proving the engine survives a misbehaving provider.</summary>
    public bool ThrowOnSend { get; set; }

    public List<NotificationMessage> SentMessages { get; } = [];

    public int SendCount => SentMessages.Count;

    public string? ResolveTemplateId(ReminderType reminderType) =>
        Templates.TryGetValue(reminderType, out var templateId) ? templateId : null;

    public Task<NotificationSendResult> SendNotificationAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);

        if (ThrowOnSend)
        {
            throw new InvalidOperationException("The provider is unreachable.");
        }

        var result = QueuedResults.Count > 0
            ? QueuedResults.Dequeue()
            : ResultFactory?.Invoke(message)
                ?? NotificationSendResult.Sent($"SM{SentMessages.Count:D4}");

        return Task.FromResult(result);
    }
}
