namespace DeBliss.Application.Customers.Dtos;

/// <summary>How a customer list is narrowed by record state. Sent by name, as the API binds it.</summary>
public enum CustomerStatusFilter
{
    All = 0,
    Active = 1,
    Inactive = 2
}

/// <summary>
/// Everything the customer list can be narrowed by. Page and page size are
/// normalised before they reach the database so a caller cannot ask for page
/// zero or an unbounded page.
/// </summary>
public sealed record CustomerQuery(string? Search, CustomerStatusFilter Status, int Page, int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public CustomerQuery Normalized()
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

/// <summary>A row in the customer list: enough to recognise and triage a customer.</summary>
public sealed record CustomerSummaryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Email,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Appointment counts for one customer, read straight from the scheduling
/// tables. Every count is zero until the appointment module starts writing
/// bookings, at which point the same query simply starts returning real data.
/// </summary>
public sealed record CustomerAppointmentSummaryDto(
    int TotalAppointments,
    int CompletedAppointments,
    int CancelledAppointments,
    DateTimeOffset? LastVisitAt);

/// <summary>The full customer profile as the profile page renders it.</summary>
public sealed record CustomerDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Address,
    bool IsActive,
    CustomerAppointmentSummaryDto AppointmentSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>One note on a customer profile, newest first.</summary>
public sealed record CustomerNoteDto(
    Guid Id,
    Guid CustomerId,
    string Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// One past or upcoming appointment on the customer profile. Service names and
/// staff come from the booking-time snapshots so the history never changes when
/// the catalogue or staff list is edited later.
/// </summary>
public sealed record CustomerAppointmentDto(
    Guid Id,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    IReadOnlyList<string> ServiceNames,
    IReadOnlyList<string> StaffNames);

/// <summary>
/// Payload for creating and editing a customer. <see cref="IsActive"/> is
/// nullable so the compact "+ New Customer" form the future appointment screen
/// will post can omit it: null means "active" on create and "unchanged" on
/// edit. <see cref="AllowDuplicatePhone"/> is the second step of the duplicate
/// warning: the first save without it comes back 409 naming the customer that
/// already holds the number, and the client asks the user before retrying.
/// </summary>
public sealed record SaveCustomerRequest(
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? Email,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Address,
    bool? IsActive,
    bool AllowDuplicatePhone);

/// <summary>Body of the customer status toggle endpoint.</summary>
public sealed record SetCustomerStatusRequest(bool IsActive);

/// <summary>Payload for adding to or editing a customer note. Notes are never deleted.</summary>
public sealed record SaveCustomerNoteRequest(string? Note);
