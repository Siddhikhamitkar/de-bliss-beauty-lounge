using DeBliss.Domain.Settings;

namespace DeBliss.Application.Settings.Ports;

/// <summary>
/// Persistence for the salon's singleton settings row. Reads return null when
/// the row has never been written — the caller supplies the same defaults the
/// migration seeds — so a fresh database behaves like a configured one.
/// </summary>
public interface ISalonSettingsRepository
{
    /// <summary>The settings row, untracked, or null when it does not exist yet.</summary>
    Task<SalonSettings?> GetAsync(CancellationToken ct = default);

    /// <summary>The settings row for editing, or null when it does not exist yet.</summary>
    Task<SalonSettings?> GetTrackedAsync(CancellationToken ct = default);

    void Add(SalonSettings settings);
}
