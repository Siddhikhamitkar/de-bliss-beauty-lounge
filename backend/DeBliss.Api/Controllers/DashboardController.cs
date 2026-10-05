using DeBliss.Application.Common.Security;
using DeBliss.Application.Dashboard;
using DeBliss.Application.Dashboard.Dtos;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class DashboardController(IReceptionistDashboardService dashboard) : ControllerBase
{
    /// <summary>The receptionist's read-only snapshot of today.</summary>
    /// <remarks>
    /// <para>
    /// Enforced by the <c>Operational</c> policy (owner + receptionist) — the
    /// staff role is turned away with 403. The finer-grained
    /// <c>Permissions.Dashboard.ViewReceptionistDashboard</c> constant remains the
    /// seam for the future per-permission authorization layer.
    /// </para>
    /// <para>
    /// Appointment counts and operational lists only — no revenue or owner-level
    /// fields — so the front-desk role can land here without exposing business
    /// financials. The restriction is enforced at this endpoint, not just in the
    /// UI.
    /// </para>
    /// </remarks>
    [HttpGet("receptionist")]
    [ProducesResponseType(typeof(ReceptionistDashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReceptionistDashboardDto>> GetReceptionist(CancellationToken cancellationToken)
    {
        return Ok(await dashboard.GetAsync(cancellationToken));
    }
}
