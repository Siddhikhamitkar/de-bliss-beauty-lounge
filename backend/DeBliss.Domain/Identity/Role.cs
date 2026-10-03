using DeBliss.Domain.Common;

namespace DeBliss.Domain.Identity;

public class Role : AuditableEntity
{
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
