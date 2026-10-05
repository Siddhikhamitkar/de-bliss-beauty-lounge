using DeBliss.Api.Security;
using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The reminder rows attached to one appointment, for the appointment details
/// panel: what is pending, what went out and what failed. Read-only — the
/// engine owns the rows' lifecycle.
/// </summary>
[ApiController]
[Route("api/appointments/{appointmentId:guid}/reminders")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class AppointmentRemindersController(IReminderHistoryService history) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.Notifications.ViewReminders</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentReminderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AppointmentReminderDto>>> List(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        return Ok(await history.ListForAppointmentAsync(appointmentId, cancellationToken));
    }
}
