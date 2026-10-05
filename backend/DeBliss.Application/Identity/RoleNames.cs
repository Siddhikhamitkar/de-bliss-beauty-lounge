namespace DeBliss.Application.Identity;

/// <summary>
/// The stable role identifiers the platform seeds and authorises against. Stored
/// as <c>Role.Name</c> values, emitted as role claims on the auth token, and used
/// by the authorization policies — renaming one is a breaking change across all
/// three, so they are defined once here.
/// </summary>
public static class RoleNames
{
    public const string Owner = "OWNER";
    public const string Receptionist = "RECEPTIONIST";
    public const string Staff = "STAFF";
}
