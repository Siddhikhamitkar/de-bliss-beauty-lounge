namespace DeBliss.Application.Notifications.Ports;

/// <summary>How one dispatch run went, for logging and tests.</summary>
public sealed record ReminderDispatchResult(int Sent, int Failed, int Skipped, int Cancelled, int Deferred)
{
    public static ReminderDispatchResult Empty { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>
/// Executes due reminder rows. The hosted worker only calls this on a timer;
/// all claim / send / settle logic lives behind the port so tests can drive it
/// directly.
/// </summary>
public interface IReminderDispatchService
{
    /// <summary>
    /// Processes one batch of due Pending rows and returns what happened to
    /// each. Safe to call concurrently: rows are claimed atomically, so a row
    /// is only ever handled by one caller.
    /// </summary>
    Task<ReminderDispatchResult> ProcessDueAsync(CancellationToken cancellationToken = default);
}
