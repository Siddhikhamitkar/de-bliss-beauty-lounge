namespace DeBliss.Application.Common.Interfaces;

/// <summary>
/// Commits the current unit of work. Implemented by the Infrastructure DbContext so
/// that the Application layer can persist changes without referencing EF Core.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a serializable transaction on the underlying connection, so a use
    /// case that must re-check and then write (booking) cannot interleave with a
    /// concurrent one. Commit explicitly; disposing without committing rolls back.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// One serializable unit-of-work transaction. Disposing without
/// <see cref="CommitAsync"/> rolls everything in it back.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
