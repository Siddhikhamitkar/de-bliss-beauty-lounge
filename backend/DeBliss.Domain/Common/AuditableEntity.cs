namespace DeBliss.Domain.Common;

/// <summary>
/// Entity carrying audit timestamps. <see cref="UpdatedAt"/> stays null until the
/// record is modified for the first time. Values are populated automatically by
/// the Infrastructure audit interceptor.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
