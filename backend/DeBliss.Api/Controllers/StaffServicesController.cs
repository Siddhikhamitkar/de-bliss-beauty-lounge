using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Which services a staff member can perform. Thin by design: the rules live in
/// <see cref="IStaffService"/>.
/// </summary>
[ApiController]
[Route("api/staff/{staffId:guid}/services")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class StaffServicesController(IStaffService staff) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.Staffing.ViewStaff</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StaffServiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StaffServiceDto>>> Get(Guid staffId, CancellationToken cancellationToken)
    {
        return Ok(await staff.GetServicesAsync(staffId, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Staffing.ManageStaffServices</c> once authorisation exists.</remarks>
    [HttpPut]
    [ProducesResponseType(typeof(IReadOnlyList<StaffServiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StaffServiceDto>>> Set(
        Guid staffId,
        [FromBody] SetStaffServicesRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await staff.SetServicesAsync(staffId, request, cancellationToken));
    }
}
