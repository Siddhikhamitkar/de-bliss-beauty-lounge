namespace DeBliss.Domain.Common;

/// <summary>
/// Base type for every persisted entity. All primary keys in the system are
/// <see cref="Guid"/> so that identifiers are not guessable or enumerable once
/// they are exposed through public-facing (Phase 2 customer website) APIs.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; }
}
