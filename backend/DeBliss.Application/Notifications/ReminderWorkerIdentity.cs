namespace DeBliss.Application.Notifications;

/// <summary>
/// Identifies one running dispatch worker (machine, process, run). Claims are
/// taken with this id so two workers — or two runs of the same process — can
/// never hold the same reminder row.
/// </summary>
public sealed class ReminderWorkerIdentity
{
    private const int MaxLength = 100;

    public ReminderWorkerIdentity(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public static ReminderWorkerIdentity Create()
    {
        var raw = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

        return new ReminderWorkerIdentity(raw.Length <= MaxLength ? raw : raw[..MaxLength]);
    }
}
