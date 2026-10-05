using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Read-only staff lookups that feed the eligible-staff selector on the service
/// form. This module does not manage staff records — that belongs to a later one.
/// </summary>
[ApiController]
[Route("api/staff-members")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class StaffMembersController(IServiceCatalogueService catalogue) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.ServiceCatalogue.AssignStaffToServices</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StaffOptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffOptionDto>>> Get(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        return Ok(await catalogue.GetSelectableStaffAsync(includeInactive, cancellationToken));
    }
}
