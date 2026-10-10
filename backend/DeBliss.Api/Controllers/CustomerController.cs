using DeBliss.Application.Identity;
using DeBliss.Application.Identity.Dtos;
using DeBliss.Application.Scheduling;
using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The signed-in customer's own surface. Everything here resolves identity
/// from the token's customer link, so a route, query string or body field can
/// never name another customer's record.
/// </summary>
[ApiController]
[Route("api/customer")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.CustomerSelf)]
public sealed class CustomerController(
    ICustomerAccessService customerAccess,
    ICustomerBookingService bookings) : ControllerBase
{
    /// <summary>The signed-in customer's own profile.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(CustomerSelfDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CustomerSelfDto>> Me(CancellationToken cancellationToken)
    {
        var customerId = CustomerSelfIdentity.Resolve(User);

        return Ok(await customerAccess.GetProfileAsync(customerId, cancellationToken));
    }

    /// <summary>
    /// Books one service at a chosen time. The customer comes from the token;
    /// the server revalidates service eligibility, staff eligibility and
    /// availability immediately before booking and prices from the catalogue.
    /// The <c>Idempotency-Key</c> header is required: a repeated request with
    /// the same key and payload returns the original booking instead of a
    /// second one, and the same key with a different payload is refused.
    /// </summary>
    [HttpPost("appointments")]
    [ProducesResponseType(typeof(CustomerBookingConfirmationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerBookingConfirmationDto>> Book(
        [FromBody] CustomerBookingRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var customerId = CustomerSelfIdentity.Resolve(User);
        var confirmation = await bookings.BookAsync(customerId, request, idempotencyKey, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, confirmation);
    }

    /// <summary>The signed-in customer's own appointments, newest first.</summary>
    [HttpGet("appointments")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerAppointmentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CustomerAppointmentDto>>> History(
        CancellationToken cancellationToken)
    {
        var customerId = CustomerSelfIdentity.Resolve(User);

        return Ok(await bookings.GetMyAppointmentsAsync(customerId, cancellationToken));
    }
}
