using DeBliss.Domain.Common;
using DeBliss.Domain.Staffing;

namespace DeBliss.Domain.Identity;

/// <summary>
/// A person who can sign in to the salon platform. <see cref="PasswordHash"/> holds
/// the ASP.NET Core Identity PBKDF2 hash; it stays nullable only because legacy rows
/// may predate the first login being issued.
/// </summary>
public class User : AuditableEntity
{
    /// <summary>
    /// Sign-in email. Required for team accounts; optional for customers, who
    /// sign in with their verified mobile number instead.
    /// </summary>
    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string? PasswordHash { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Changes whenever the password is reset, and is carried in every token
    /// issued to the user: a token whose stamp no longer matches is refused, so a
    /// reset signs out every existing session. Null for rows that predate it.
    /// </summary>
    public string? SecurityStamp { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    /// <summary>Set when this login belongs to a bookable staff member.</summary>
    public Staff? StaffProfile { get; set; }
}
