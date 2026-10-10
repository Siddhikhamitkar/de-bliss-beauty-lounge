using DeBliss.Application.Identity;
using DeBliss.Application.Identity.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Anonymous self-service registration for the customer website. Creating an
/// account here always assigns the CUSTOMER role — role selection and any path
/// toward staff or owner privilege do not exist on this surface.
/// </summary>
[ApiController]
[Route("api/public/customers")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class PublicCustomersController(ICustomerAccessService customerAccess) : ControllerBase
{
    /// <summary>
    /// Registers a new customer account: validates and normalises the input,
    /// hashes the password with the platform hasher (never stored or logged in
    /// plaintext), links an existing front-desk profile by exact email match
    /// when unambiguous, and creates the User + Customer rows in one save.
    /// </summary>
    /// <remarks>
    /// Known limitation: email addresses are not verified (no confirmation
    /// mail yet), so an account may be registered with an address the caller
    /// does not own. Password reset is likewise a follow-up. On success the
    /// response is the safe user view — no password hash, no token, and no
    /// session cookie; the customer signs in via <c>POST /api/auth/login</c>.
    /// </remarks>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthUserDto>> Register(
        [FromBody] CustomerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var user = await customerAccess.RegisterAsync(request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, user);
    }
}
