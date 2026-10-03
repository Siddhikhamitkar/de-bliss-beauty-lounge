namespace DeBliss.Application.ServiceCatalogue.Dtos;

/// <summary>A row in the service list: enough to recognise and triage a service.</summary>
public sealed record ServiceSummaryDto(
    Guid Id,
    string Name,
    Guid? CategoryId,
    string? CategoryName,
    int DurationMinutes,
    decimal Price,
    bool IsActive,
    bool IsOnlineBookable,
    bool IsBookable,
    int EligibleStaffCount,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>The full record behind one service, including who may perform it.</summary>
public sealed record ServiceDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    bool IsCategoryActive,
    int DurationMinutes,
    decimal Price,
    bool IsActive,
    bool IsOnlineBookable,
    bool IsBookable,
    int SortOrder,
    IReadOnlyList<EligibleStaffDto> EligibleStaff,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A staff member already attached to a service.</summary>
public sealed record EligibleStaffDto(
    Guid StaffId,
    string FirstName,
    string LastName,
    string? JobTitle,
    bool IsActive);

/// <summary>
/// Payload for both creating and editing a service. <see cref="EligibleStaffIds"/>
/// replaces the whole assignment set, which is what the form submits.
/// </summary>
public sealed record SaveServiceRequest(
    string? Name,
    Guid? CategoryId,
    string? Description,
    int DurationMinutes,
    decimal Price,
    bool IsActive,
    bool IsOnlineBookable,
    int SortOrder,
    IReadOnlyList<Guid>? EligibleStaffIds);

/// <summary>Body of the status toggle endpoint.</summary>
public sealed record SetStatusRequest(bool IsActive);
