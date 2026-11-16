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
    ICustomerAccountService accounts,
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

    /// <summary>Updates the customer's name and optional email. The mobile number changes only through <c>me/phone</c>.</summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(CustomerSelfDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerSelfDto>> UpdateMe(
        [FromBody] UpdateCustomerProfileRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = CustomerSelfIdentity.Resolve(User);

        return Ok(await accounts.UpdateProfileAsync(customerId, request, cancellationToken));
    }

    /// <summary>Sends a one-time code to a new mobile number the customer wants to switch to.</summary>
    [HttpPost("me/phone/otp")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimitPolicies.CustomerCodes)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestPhoneChange(
        [FromBody] RequestMobileChangeRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = CustomerSelfIdentity.Resolve(User);

        await accounts.RequestMobileChangeAsync(customerId, request, cancellationToken);

        return Accepted();
    }

    /// <summary>Confirms the new mobile number with the code sent to it, and switches to it.</summary>
    [HttpPost("me/phone/verify")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimitPolicies.CustomerCodes)]
    [ProducesResponseType(typeof(CustomerSelfDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerSelfDto>> ConfirmPhoneChange(
        [FromBody] ConfirmMobileChangeRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = CustomerSelfIdentity.Resolve(User);

        return Ok(await accounts.ConfirmMobileChangeAsync(customerId, request, cancellationToken));
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
