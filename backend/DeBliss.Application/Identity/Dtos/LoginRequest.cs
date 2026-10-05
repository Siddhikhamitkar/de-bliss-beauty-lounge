using System.ComponentModel.DataAnnotations;

namespace DeBliss.Application.Identity.Dtos;

public sealed record LoginRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public required string Email { get; init; }

    [Required]
    [MaxLength(200)]
    public required string Password { get; init; }
}
