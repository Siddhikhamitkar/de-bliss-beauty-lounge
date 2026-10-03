namespace DeBliss.Application.Common.Interfaces;

/// <summary>
/// Commits the current unit of work. Implemented by the Infrastructure DbContext so
/// that the Application layer can persist changes without referencing EF Core.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
