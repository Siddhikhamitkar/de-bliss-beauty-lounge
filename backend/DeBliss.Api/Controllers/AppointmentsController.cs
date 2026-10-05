using DeBliss.Application.Scheduling;
using DeBliss.Application.Scheduling.Dtos;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Bookings: creation, full edit, the drag-and-drop schedule patch and the
/// status pipeline. Thin by design — every rule (snapshot capture, availability
/// gate, transition matrix) lives in <see cref="IAppointmentService"/>.
/// </summary>
[ApiController]
[Route("api/appointments")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class AppointmentsController(IAppointmentService appointments) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.Scheduling.ViewAppointments</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AppointmentDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await appointments.GetAsync(id, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Scheduling.CreateAppointment</c> once authorisation exists.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentDto>> Create(
        [FromBody] SaveAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        var created = await appointments.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <remarks>Will require <c>Permissions.Scheduling.EditAppointment</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentDto>> Update(
        Guid id,
        [FromBody] SaveAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await appointments.UpdateAsync(id, request, cancellationToken));
    }

    /// <summary>
    /// One calendar drag or resize: any subset of new start (vertical drag),
    /// staff (horizontal drag) or total duration (resize). The server
    /// re-validates the resulting window before committing; a rejection here
    /// is what tells the UI to revert the drag.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Scheduling.RescheduleAppointment</c> once authorisation exists.</remarks>
    [HttpPatch("{id:guid}/schedule")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentDto>> UpdateSchedule(
        Guid id,
        [FromBody] UpdateScheduleRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await appointments.UpdateScheduleAsync(id, request, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Scheduling.ChangeAppointmentStatus</c> once authorisation exists.</remarks>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentDto>> SetStatus(
        Guid id,
        [FromBody] SetAppointmentStatusRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await appointments.SetStatusAsync(id, request, cancellationToken));
    }
}
