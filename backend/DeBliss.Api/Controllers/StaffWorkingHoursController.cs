using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The recurring weekly working pattern of one staff member. Thin by design:
/// the rules live in <see cref="IStaffScheduleService"/>.
/// </summary>
[ApiController]
[Route("api/staff/{staffId:guid}/working-hours")]
[Produces("application/json")]
public sealed class StaffWorkingHoursController(IStaffScheduleService schedule) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.Staffing.ViewStaff</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(WeekScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WeekScheduleDto>> Get(Guid staffId, CancellationToken cancellationToken)
    {
        return Ok(await schedule.GetWeekScheduleAsync(staffId, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Staffing.ManageWorkingHours</c> once authorisation exists.</remarks>
    [HttpPut]
    [ProducesResponseType(typeof(WeekScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WeekScheduleDto>> Set(
        Guid staffId,
        [FromBody] SaveWeekScheduleRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await schedule.SaveWeekScheduleAsync(staffId, request, cancellationToken));
    }
}
