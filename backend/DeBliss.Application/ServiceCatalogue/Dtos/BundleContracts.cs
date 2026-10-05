namespace DeBliss.Application.ServiceCatalogue.Dtos;

/// <summary>
/// The full bundle record, including its components in delivery sequence.
/// Duration and standard total are derived from the current component
/// catalogue values; <see cref="SavingsAmount"/> is the difference against
/// <see cref="Price"/>.
/// </summary>
public sealed record BundleDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    bool IsCategoryActive,
    int DurationMinutes,
    decimal Price,
    decimal StandardTotalPrice,
    decimal SavingsAmount,
    bool IsActive,
    bool IsBookable,
    int SortOrder,
    IReadOnlyList<BundleItemDto> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>One component of a bundle, in delivery sequence.</summary>
public sealed record BundleItemDto(
    Guid ServiceId,
    string Name,
    int DurationMinutes,
    decimal Price,
    int DisplayOrder,
    bool IsActive);

/// <summary>
/// Payload for both creating and editing a bundle. <see cref="Items"/> is the
/// whole component set in delivery order — the list position becomes the
/// sequence, so reordering is just resubmitting the list in the new order.
/// </summary>
public sealed record SaveBundleRequest(
    string? Name,
    Guid? CategoryId,
    string? Description,
    decimal Price,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<Guid>? Items);
