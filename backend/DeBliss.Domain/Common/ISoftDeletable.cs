namespace DeBliss.Domain.Common;

/// <summary>
/// Marks records that must never be physically deleted, only retired.
/// Implemented by appointment and financial records so that history and
/// reporting remain intact.
/// </summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}
