using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Dashboard.Dtos;

namespace DeBliss.Application.Dashboard;

/// <summary>The window the owner is looking at. Today is the default view.</summary>
public enum OwnerDashboardRange
{
    Today = 0,
    Yesterday = 1,
    Last7Days = 2,
    ThisMonth = 3,
    LastMonth = 4,
    Custom = 5
}

/// <summary>
/// The selected window. Custom requires both dates in the salon's business
/// zone and accepts at most a year — a dashboard, not a data warehouse export.
/// </summary>
public sealed record OwnerDashboardRangeRequest(
    OwnerDashboardRange Range,
    DateOnly? From,
    DateOnly? To)
{
    public const int MaxCustomDays = 366;

    public void Validate()
    {
        if (Range != OwnerDashboardRange.Custom)
        {
            return;
        }

        var errors = new ValidationErrorBuilder();

        if (From is null || To is null)
        {
            errors.Add("from", "Choose both a start and an end date.");
        }
        else if (From.Value > To.Value)
        {
            errors.Add("from", "The start date must be on or before the end date.");
        }
        else if (To.Value.DayNumber - From.Value.DayNumber + 1 > MaxCustomDays)
        {
            errors.Add("to", $"Choose a range of {MaxCustomDays} days or fewer.");
        }

        errors.ThrowIfInvalid();
    }
}

/// <summary>
/// The owner's read-only business snapshot: money, floor operations, trends,
/// stock alerts and the attention list, composed in one round trip from the
/// billing, scheduling, staffing and inventory data the modules already own.
/// Nothing here mutates, and every figure is defined on the DTO contracts.
/// </summary>
public interface IOwnerDashboardService
{
    Task<OwnerDashboardDto> GetAsync(
        OwnerDashboardRangeRequest request,
        CancellationToken cancellationToken = default);
}
