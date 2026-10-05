namespace DeBliss.Application.Identity.Dtos;

/// <summary>
/// The safe, client-facing view of the signed-in user. Never carries
/// <c>PasswordHash</c> or any other sensitive User column — the API and the tests
/// assert against exactly this shape.
/// </summary>
public sealed record AuthUserDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    IReadOnlyList<string> Roles,
    Guid? StaffId);
