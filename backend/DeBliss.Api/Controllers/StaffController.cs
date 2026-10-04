using DeBliss.Application.Common.Paging;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Staff profiles: who works at the salon and whether they are active. Thin by
/// design: every rule lives in <see cref="IStaffService"/> and
/// <see cref="IStaffAvailabilityService"/>.
/// </summary>
[ApiController]
[Route("api/staff")]
[Produces("application/json")]
public sealed class StaffController(IStaffService staff, IStaffAvailabilityService availability) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.Staffing.ViewStaff</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<StaffSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StaffSummaryDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] StaffStatusFilter status,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new StaffQuery(search, status, page, pageSize);

        return Ok(await staff.SearchAsync(query, cancellationToken));
    }

    /// <summary>
    /// Answers "can this staff member take this slot?" for the future appointment
    /// calendar. The window must be within one day and end after it starts.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Staffing.CheckAvailability</c> once authorisation exists.</remarks>
    [HttpGet("availability")]
    [ProducesResponseType(typeof(StaffAvailabilityCheckDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffAvailabilityCheckDto>> CheckAvailability(
        [FromQuery] Guid staffId,
        [FromQuery] DateTimeOffset start,
        [FromQuery] DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        return Ok(await availability.CheckAsync(staffId, start, end, cancellationToken: cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Staffing.CreateStaff</c> once authorisation exists.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(StaffDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StaffDto>> Create(
        [FromBody] SaveStaffRequest request,
        CancellationToken cancellationToken)
    {
        var created = await staff.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <remarks>Will require <c>Permissions.Staffing.ViewStaff</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StaffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await staff.GetAsync(id, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Staffing.EditStaff</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(StaffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffDto>> Update(
        Guid id,
        [FromBody] SaveStaffRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await staff.UpdateAsync(id, request, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Staffing.ChangeStaffStatus</c> once authorisation exists.</remarks>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(StaffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffDto>> SetStatus(
        Guid id,
        [FromBody] SetStaffStatusRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await staff.SetStatusAsync(id, request.IsActive, cancellationToken));
    }
}
