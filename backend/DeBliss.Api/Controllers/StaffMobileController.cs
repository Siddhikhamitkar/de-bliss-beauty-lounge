using DeBliss.Api.Security;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The self-scoped surface a staff member uses from the future mobile app: their
/// own profile, bookings, shift pattern and clock-in/clock-out. Every route reads
/// the staff id from the signed token, so no request can name somebody else's
/// record — the id is not a parameter anywhere on this controller.
/// </summary>
[ApiController]
[Route("api/staff/me")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.StaffSelf)]
public sealed class StaffMobileController(
    IStaffMobileService mobile,
    IStaffAttendanceService attendance) : ControllerBase
{
    private Guid StaffId => StaffSelfIdentity.Resolve(User);

    [HttpGet]
    [ProducesResponseType(typeof(StaffMobileProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffMobileProfileDto>> GetProfile(CancellationToken cancellationToken)
    {
        return Ok(await mobile.GetProfileAsync(StaffId, cancellationToken));
    }

    /// <remarks>Defaults to <c>Today</c>; <c>Upcoming</c> covers the next month, <c>History</c> the last quarter.</remarks>
    [HttpGet("appointments")]
    [ProducesResponseType(typeof(IReadOnlyList<StaffAppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StaffAppointmentDto>>> GetAppointments(
        [FromQuery] StaffAppointmentScope scope,
        CancellationToken cancellationToken)
    {
        return Ok(await mobile.GetAppointmentsAsync(StaffId, scope, cancellationToken));
    }

    /// <remarks>
    /// Today plus the following six days, derived from the recurring weekly
    /// working pattern the owner maintains and the dated time off against it.
    /// </remarks>
    [HttpGet("schedule")]
    [ProducesResponseType(typeof(StaffScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffScheduleDto>> GetSchedule(CancellationToken cancellationToken)
    {
        return Ok(await mobile.GetScheduleAsync(StaffId, cancellationToken));
    }

    /// <remarks>Null when the staff member has not clocked in today.</remarks>
    [HttpGet("attendance/today")]
    [ProducesResponseType(typeof(StaffAttendanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffAttendanceDto?>> GetTodayAttendance(CancellationToken cancellationToken)
    {
        return Ok(await attendance.GetTodayAsync(StaffId, cancellationToken));
    }

    /// <remarks>Defaults to the last 90 days ending today.</remarks>
    [HttpGet("attendance")]
    [ProducesResponseType(typeof(IReadOnlyList<StaffAttendanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StaffAttendanceDto>>> GetAttendance(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        return Ok(await attendance.GetHistoryAsync(StaffId, new AttendanceQuery(from, to), cancellationToken));
    }

    /// <remarks>
    /// The server clock stamps the check-in; a client-supplied time is not
    /// accepted. Returns the day's record so the caller can render it at once.
    /// </remarks>
    [HttpPost("attendance/check-in")]
    [ProducesResponseType(typeof(StaffAttendanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffAttendanceDto>> CheckIn(CancellationToken cancellationToken)
    {
        return Ok(await attendance.CheckInAsync(StaffId, cancellationToken));
    }

    /// <remarks>Closes today's record; refused when there is no open check-in or it is already closed.</remarks>
    [HttpPost("attendance/check-out")]
    [ProducesResponseType(typeof(StaffAttendanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffAttendanceDto>> CheckOut(CancellationToken cancellationToken)
    {
        return Ok(await attendance.CheckOutAsync(StaffId, cancellationToken));
    }
}
