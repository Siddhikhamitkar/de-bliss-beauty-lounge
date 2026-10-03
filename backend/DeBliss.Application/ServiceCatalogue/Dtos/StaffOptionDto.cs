namespace DeBliss.Application.ServiceCatalogue.Dtos;

/// <summary>A staff member as offered by the eligible-staff selector.</summary>
public sealed record StaffOptionDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? JobTitle,
    bool IsActive)
{
    public string DisplayName => $"{FirstName} {LastName}".Trim();
}
