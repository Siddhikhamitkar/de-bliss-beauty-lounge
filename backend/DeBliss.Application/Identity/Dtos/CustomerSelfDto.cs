namespace DeBliss.Application.Identity.Dtos;

/// <summary>
/// The signed-in customer's own profile, as the customer website renders it.
/// Only the fields the customer already knows about themselves — never consent
/// state, walk-in flags or operational bookkeeping.
/// </summary>
public sealed record CustomerSelfDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber);
