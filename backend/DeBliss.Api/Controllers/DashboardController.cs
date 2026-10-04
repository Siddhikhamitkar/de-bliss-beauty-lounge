using DeBliss.Application.Common.Security;
using DeBliss.Application.Dashboard;
using DeBliss.Application.Dashboard.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Produces("application/json")]
public sealed class DashboardController(IReceptionistDashboardService dashboard) : ControllerBase
{
    /// <summary>The receptionist's read-only snapshot of today.</summary>
    /// <remarks>
    /// <para>Will require <c>Permissions.Dashboard.ViewReceptionistDashboard</c> once authorisation exists.</para>
    /// <para>
    /// Appointment counts and operational lists only — no revenue or owner-level
    /// fields — so the front-desk role can land here without exposing business
    /// financials. The permission is enforced at this endpoint (not just in the
    /// UI) when Roles &amp; Permissions lands.
    /// </para>
    /// </remarks>
    [HttpGet("receptionist")]
    [ProducesResponseType(typeof(ReceptionistDashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReceptionistDashboardDto>> GetReceptionist(CancellationToken cancellationToken)
    {
        return Ok(await dashboard.GetAsync(cancellationToken));
    }
}
