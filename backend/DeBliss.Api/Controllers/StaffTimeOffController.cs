using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Dated time-off records (leave, sick, training, …) for one staff member. Thin
/// by design: the rules live in <see cref="IStaffScheduleService"/>. Only future
/// entries can be removed; anything that has started stays on the record.
/// </summary>
[ApiController]
[Route("api/staff/{staffId:guid}/time-off")]
[Produces("application/json")]
public sealed class StaffTimeOffController(IStaffScheduleService schedule) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.Staffing.ViewStaff</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StaffTimeOffDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StaffTimeOffDto>>> Get(Guid staffId, CancellationToken cancellationToken)
    {
        return Ok(await schedule.GetTimeOffAsync(staffId, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Staffing.ManageTimeOff</c> once authorisation exists.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(StaffTimeOffDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffTimeOffDto>> Create(
        Guid staffId,
        [FromBody] SaveStaffTimeOffRequest request,
        CancellationToken cancellationToken)
    {
        var created = await schedule.CreateTimeOffAsync(staffId, request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { staffId }, created);
    }

    /// <remarks>Will require <c>Permissions.Staffing.ManageTimeOff</c> once authorisation exists.</remarks>
    [HttpPut("{timeOffId:guid}")]
    [ProducesResponseType(typeof(StaffTimeOffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffTimeOffDto>> Update(
        Guid staffId,
        Guid timeOffId,
        [FromBody] SaveStaffTimeOffRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await schedule.UpdateTimeOffAsync(staffId, timeOffId, request, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Staffing.ManageTimeOff</c> once authorisation exists.</remarks>
    [HttpDelete("{timeOffId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid staffId, Guid timeOffId, CancellationToken cancellationToken)
    {
        await schedule.DeleteTimeOffAsync(staffId, timeOffId, cancellationToken);

        return NoContent();
    }
}
