using DeBliss.Application.Scheduling;
using DeBliss.Application.Scheduling.Dtos;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The calendar read model: staff columns, bookings and non-bookable markers
/// for one range. Composed by <see cref="ICalendarService"/> from the staffing,
/// catalogue and scheduling tables; the grid never stitches this together client-side.
/// </summary>
[ApiController]
[Route("api/calendar")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class CalendarController(ICalendarService calendar) : ControllerBase
{
    /// <summary>
    /// Everything the grid renders for [from, to). The range is capped at
    /// <see cref="CalendarQuery.MaximumRangeDays"/> so a view can never ask
    /// for an unbounded slice of history.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Scheduling.ViewCalendar</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(CalendarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CalendarDto>> Get(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        return Ok(await calendar.GetAsync(new CalendarQuery(from, to), cancellationToken));
    }
}
