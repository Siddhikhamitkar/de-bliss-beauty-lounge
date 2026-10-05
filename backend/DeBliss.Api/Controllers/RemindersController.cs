using DeBliss.Api.Security;
using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The WhatsApp reminder surface: the operational history the front desk
/// monitors (any logged-in operations user) and the owner-only settings —
/// rule timing, connection status and the operator test send. Credentials are
/// configuration, never data: no endpoint here reads or returns them.
/// </summary>
[ApiController]
[Route("api/reminders")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class RemindersController(
    IReminderSettingsService settings,
    IReminderHistoryService history) : ControllerBase
{
    /// <summary>
    /// The reminder history table: pending, sent, failed, cancelled and
    /// skipped rows, newest scheduled first, with optional filters. The
    /// receptionist watches failed rows here for operational follow-up.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Notifications.ViewReminders</c> once authorisation exists.</remarks>
    [HttpGet("history")]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentReminderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<AppointmentReminderDto>>> GetHistory(
        [FromQuery] string? status,
        [FromQuery] string? reminderType,
        [FromQuery] Guid? appointmentId,
        [FromQuery] Guid? customerId,
        [FromQuery] DateTimeOffset? scheduledFrom,
        [FromQuery] DateTimeOffset? scheduledTo,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var query = new ReminderHistoryQuery(
            status, reminderType, appointmentId, customerId, scheduledFrom, scheduledTo, take);

        return Ok(await history.QueryAsync(query, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Notifications.ManageReminderSettings</c> once authorisation exists.</remarks>
    [HttpGet("rules")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(IReadOnlyList<ReminderRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ReminderRuleDto>>> GetRules(CancellationToken cancellationToken)
    {
        return Ok(await settings.GetRulesAsync(cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Notifications.ManageReminderSettings</c> once authorisation exists.</remarks>
    [HttpPut("rules/{id:guid}")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(ReminderRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReminderRuleDto>> UpdateRule(
        Guid id,
        [FromBody] UpdateReminderRuleRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await settings.UpdateRuleAsync(id, request, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Notifications.ManageReminderSettings</c> once authorisation exists.</remarks>
    [HttpGet("provider")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(ReminderProviderStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReminderProviderStatusDto>> GetProviderStatus(CancellationToken cancellationToken)
    {
        return Ok(await settings.GetProviderStatusAsync(cancellationToken));
    }

    /// <summary>
    /// The owner-only operator test send. It exercises the configured provider
    /// against an operator-supplied number, is clearly marked as a test
    /// message, and never routes through a customer's reminder rows or their
    /// opt-in state.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Notifications.SendTestReminder</c> once authorisation exists.</remarks>
    [HttpPost("test")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(SendTestMessageResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SendTestMessageResult>> SendTest(
        [FromBody] SendTestMessageRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await settings.SendTestMessageAsync(request, cancellationToken));
    }
}
