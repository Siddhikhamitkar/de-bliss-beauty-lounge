using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

[ApiController]
[Route("api/public/customers")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class PublicCustomersController : ControllerBase
{
    /// <summary>Retired: email sign-up without proof of the mobile number.</summary>
    /// <remarks>
    /// Accounts are now opened by proving the mobile number with a one-time code:
    /// <c>POST /api/public/customer-auth/otp</c> (purpose <c>activation</c>) →
    /// <c>otp/verify</c> → <c>activate</c>. Existing accounts are unaffected and
    /// keep signing in as before.
    /// </remarks>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public IActionResult Register()
    {
        return Problem(
            title: "Registration has moved.",
            detail: "Create your account by verifying your mobile number: request a code at " +
                    "/api/public/customer-auth/otp with purpose 'activation'.",
            statusCode: StatusCodes.Status410Gone);
    }
}
