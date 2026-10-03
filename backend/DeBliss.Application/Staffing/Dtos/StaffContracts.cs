namespace DeBliss.Application.Staffing.Dtos;

/// <summary>How a staff list is narrowed by record state. Sent by name, as the API binds it.</summary>
public enum StaffStatusFilter
{
    All = 0,
    Active = 1,
    Inactive = 2
}

/// <summary>
/// Everything the staff list can be narrowed by. Page and page size are
/// normalised before they reach the database so a caller cannot ask for page
/// zero or an unbounded page.
/// </summary>
public sealed record StaffQuery(string? Search, StaffStatusFilter Status, int Page, int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public StaffQuery Normalized()
    {
        var pageSize = PageSize <= 0 ? DefaultPageSize : Math.Min(PageSize, MaximumPageSize);

        return this with
        {
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            Page = Math.Max(Page, 1),
            PageSize = pageSize
        };
    }
}

/// <summary>A row in the staff list: enough to recognise and triage a team member.</summary>
public sealed record StaffSummaryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? JobTitle,
    string? CalendarColor,
    bool IsActive,
    int ServiceCount,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A service a staff member can perform, as shown on the profile.</summary>
public sealed record StaffServiceDto(
    Guid ServiceId,
    string Name,
    string? CategoryName,
    bool IsActive);

/// <summary>The full staff profile, including which services the member can perform.</summary>
public sealed record StaffDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    string? JobTitle,
    string? CalendarColor,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<StaffServiceDto> Services,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Payload for creating and editing a staff member.</summary>
public sealed record SaveStaffRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber,
    string? JobTitle,
    string? CalendarColor,
    bool IsActive,
    int SortOrder);

/// <summary>Body of the staff status toggle endpoint.</summary>
public sealed record SetStaffStatusRequest(bool IsActive);

/// <summary>
/// Payload for replacing the services a staff member can perform. ServiceIds
/// replaces the whole assignment set, mirroring how the service form submits.
/// </summary>
public sealed record SetStaffServicesRequest(IReadOnlyList<Guid>? ServiceIds);
