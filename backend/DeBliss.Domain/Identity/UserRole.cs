using DeBliss.Domain.Common;

namespace DeBliss.Domain.Identity;

/// <summary>
/// User-to-role assignment. Carries its own surrogate key so that every table in
/// the system has a single Guid primary key, with uniqueness of the pair enforced
/// by an index instead.
/// </summary>
public class UserRole : Entity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public Guid RoleId { get; set; }

    public Role Role { get; set; } = null!;

    public DateTimeOffset AssignedAt { get; set; }
}
