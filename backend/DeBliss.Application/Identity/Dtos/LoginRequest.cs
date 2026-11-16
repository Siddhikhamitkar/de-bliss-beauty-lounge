using System.ComponentModel.DataAnnotations;

namespace DeBliss.Application.Identity.Dtos;

/// <summary>
/// Sign-in with exactly one identifier: <see cref="Email"/> (team accounts and
/// existing email customers) or <see cref="PhoneNumber"/> (customers with a
/// verified mobile number).
/// </summary>
public sealed record LoginRequest
{
    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; init; }

    [MaxLength(30)]
    public string? PhoneNumber { get; init; }

    [Required]
    [MaxLength(200)]
    public required string Password { get; init; }
}
