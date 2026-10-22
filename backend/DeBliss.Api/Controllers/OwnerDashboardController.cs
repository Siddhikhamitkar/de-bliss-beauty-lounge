using DeBliss.Api.Security;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Dashboard;
using DeBliss.Application.Dashboard.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The owner's financial and operational dashboard. The aggregation lives in
/// <see cref="OwnerDashboardService"/>; this controller only binds the range
/// parameters and enforces the owner-only boundary — receptionists and staff
/// never see revenue, VAT, tips or outstanding money through this API.
/// </summary>
[ApiController]
[Route("api/dashboard/owner")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.OwnerOnly)]
public sealed class OwnerDashboardController(IOwnerDashboardService dashboard) : ControllerBase
{
    /// <summary>Owner dashboard for a reporting range. Defaults to today.</summary>
    /// <param name="range" example="ThisMonth">Today, Yesterday, Last7Days, ThisMonth, LastMonth or Custom.</param>
    /// <param name="from">Custom-range start date (salon-local, inclusive). Required when range is Custom.</param>
    /// <param name="to">Custom-range end date (salon-local, inclusive). Required when range is Custom.</param>
    [HttpGet("")]
    [ProducesResponseType(typeof(OwnerDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OwnerDashboardDto>> Get(
        [FromQuery] string? range,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var request = new OwnerDashboardRangeRequest(ParseRange(range), from, to);
        request.Validate();

        return Ok(await dashboard.GetAsync(request, cancellationToken));
    }

    private static OwnerDashboardRange ParseRange(string? range)
    {
        if (string.IsNullOrWhiteSpace(range))
        {
            return OwnerDashboardRange.Today;
        }

        if (Enum.TryParse<OwnerDashboardRange>(range.Trim(), ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ValidationFailedException(new Dictionary<string, string[]>
        {
            ["range"] =
            [
                "Unknown range. Use Today, Yesterday, Last7Days, ThisMonth, LastMonth or Custom.",
            ],
        });
    }
}
