using System.ComponentModel.DataAnnotations;

using DeBliss.Application.Customers;

namespace DeBliss.Application.Identity.Dtos;

/// <summary>
/// The minimum a public registration accepts: a name, an email, a password and
/// an optional mobile number. Deliberately flat and small — roles, consent flags
/// and identifiers are never collected at the public door.
/// </summary>
public sealed record CustomerRegistrationRequest
{
    [Required]
    [MaxLength(CustomerLimits.NameMaxLength)]
    public required string FirstName { get; init; }

    [Required]
    [MaxLength(CustomerLimits.NameMaxLength)]
    public required string LastName { get; init; }

    [Required]
    [EmailAddress]
    [MaxLength(CustomerLimits.EmailMaxLength)]
    public required string Email { get; init; }

    [Required]
    [MinLength(8)]
    [MaxLength(200)]
    public required string Password { get; init; }

    [MaxLength(CustomerLimits.PhoneMaxLength)]
    public string? PhoneNumber { get; init; }
}
