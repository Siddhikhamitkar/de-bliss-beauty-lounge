using DeBliss.Application.Scheduling;
using DeBliss.Application.Scheduling.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The customer website's availability lookup. Anonymous by design — a day's
/// open slots contain no customer-specific information — and deliberately
/// narrow: one service, one salon-local day, bookable starts only.
/// </summary>
[ApiController]
[Route("api/public/availability")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class PublicAvailabilityController(IPublicAvailabilityService availability) : ControllerBase
{
    /// <summary>
    /// Bookable start times for one service on one salon-local day (IANA zone
    /// from <c>Scheduling:SalonTimeZone</c>), each with the staff who can take
    /// it. Past moments and the 90-day horizon are refused; inactive or not
    /// online-bookable services are refused.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PublicAvailabilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicAvailabilityDto>> GetDay(
        [FromQuery] Guid serviceId,
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken)
    {
        return Ok(await availability.GetDayAsync(serviceId, date, cancellationToken));
    }
}
