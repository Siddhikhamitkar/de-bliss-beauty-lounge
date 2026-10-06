using DeBliss.Api.Security;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// One staff member's attendance history for the owner's Staff Management view.
/// Owner-only rather than operational: a clock record is private staff data, and
/// the front desk keeps the availability information it needs for booking without
/// it. Read-only by design — a historical shift is the record of what happened.
/// </summary>
[ApiController]
[Route("api/staff/{staffId:guid}/attendance")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.OwnerOnly)]
public sealed class StaffAttendanceController(IStaffAttendanceService attendance) : ControllerBase
{
    /// <remarks>Defaults to the last 90 days ending today; a wider range is refused rather than truncated.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StaffAttendanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StaffAttendanceDto>>> Get(
        Guid staffId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        return Ok(await attendance.GetHistoryAsync(staffId, new AttendanceQuery(from, to), cancellationToken));
    }
}
