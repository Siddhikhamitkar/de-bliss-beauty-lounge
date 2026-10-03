using DeBliss.Domain.Common;
using DeBliss.Domain.Staffing;

namespace DeBliss.Domain.Identity;

/// <summary>
/// A person who can sign in to the salon platform. Authentication is not
/// implemented yet, so <see cref="PasswordHash"/> is optional and currently unused.
/// </summary>
public class User : AuditableEntity
{
    public string Email { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string? PasswordHash { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    /// <summary>Set when this login belongs to a bookable staff member.</summary>
    public Staff? StaffProfile { get; set; }
}
