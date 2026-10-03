namespace DeBliss.Application.ServiceCatalogue.Dtos;

/// <summary>A service menu category as the UI needs it.</summary>
public sealed record ServiceCategoryDto(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    int ServiceCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Payload for both creating and editing a category, so the same form and the
/// same validation rules serve both operations.
/// </summary>
public sealed record SaveServiceCategoryRequest(
    string? Name,
    string? Description,
    int SortOrder,
    bool IsActive);
