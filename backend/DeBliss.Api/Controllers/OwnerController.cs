using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Owner landing surface. The real Owner Dashboard (revenue, reports, marketing,
/// settings, user administration) is a later module — this endpoint exists now
/// so the owner-only authorization boundary is enforced and testable before any
/// of those screens are built.
/// </summary>
[ApiController]
[Route("api/owner")]
[Authorize(Policy = AuthPolicies.OwnerOnly)]
[Produces("application/json")]
public sealed class OwnerController : ControllerBase
{
    [HttpGet("workspace")]
    [ProducesResponseType(typeof(OwnerWorkspaceProbe), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public ActionResult<OwnerWorkspaceProbe> GetWorkspace()
    {
        return Ok(new OwnerWorkspaceProbe(
            "Owner workspace is under construction",
            DateTimeOffset.UtcNow));
    }
}

public sealed record OwnerWorkspaceProbe(string Message, DateTimeOffset GeneratedAt);
