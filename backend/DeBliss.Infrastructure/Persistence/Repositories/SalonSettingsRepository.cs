using DeBliss.Application.Settings.Ports;
using DeBliss.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// The salon's singleton settings row. Reads are untracked because a read is
/// never the start of an edit; the update path loads tracked instead.
/// </summary>
public sealed class SalonSettingsRepository(DeBlissDbContext context) : ISalonSettingsRepository
{
    public Task<SalonSettings?> GetAsync(CancellationToken ct = default)
    {
        return context.SalonSettings.AsNoTracking().FirstOrDefaultAsync(ct);
    }

    public Task<SalonSettings?> GetTrackedAsync(CancellationToken ct = default)
    {
        return context.SalonSettings.FirstOrDefaultAsync(ct);
    }

    public void Add(SalonSettings settings) => context.SalonSettings.Add(settings);
}
