using DeBliss.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace DeBliss.Infrastructure.Persistence;

/// <summary>
/// EF transaction handle behind the unit-of-work abstraction. Disposing before
/// <see cref="CommitAsync"/> rolls back — the use case only has to remember to
/// commit on success.
/// </summary>
internal sealed class EntityFrameworkUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
{
    public async Task CommitAsync(CancellationToken cancellationToken = default) =>
        await transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync() =>
        await transaction.DisposeAsync();
}
