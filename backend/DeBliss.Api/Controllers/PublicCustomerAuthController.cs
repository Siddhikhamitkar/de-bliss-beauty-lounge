using DeBliss.Application.Identity;
using DeBliss.Application.Identity.Dtos;
using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Anonymous customer-account flows built on proving a mobile number with a
/// one-time code: first-time activation (which is also how a new customer signs
/// up), and forgotten passwords.
/// </summary>
/// <remarks>
/// Flow: <c>POST otp</c> → <c>POST otp/verify</c> (returns a single-use
/// verification token) → <c>POST activate</c> or <c>POST password/reset</c>.
/// No response here says whether a number belongs to an existing customer
/// before the code has been proven.
/// </remarks>
[ApiController]
[Route("api/public/customer-auth")]
[Produces("application/json")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.CustomerCodes)]
public sealed class PublicCustomerAuthController(
    ICustomerAccountService accounts,
    IAuthService auth,
    JwtTokenIssuer tokens) : ControllerBase
{
    /// <summary>Requests a one-time code by SMS.</summary>
    /// <remarks>
    /// <c>purpose</c> is <c>activation</c> or <c>password-reset</c>. Always answers
    /// 202 for a well-formed request, whether or not a code was due for that
    /// number; 429 (with Retry-After) inside the resend cooldown or past the
    /// hourly cap.
    /// </remarks>
    [HttpPost("otp")]
    [ProducesResponseType(typeof(CodeRequestAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CodeRequestAcceptedDto>> RequestCode(
        [FromBody] RequestCustomerCodeRequest request,
        CancellationToken cancellationToken)
    {
        await accounts.RequestCodeAsync(request, cancellationToken);

        return Accepted(new CodeRequestAcceptedDto(
            "If this number can receive a code for this request, we have sent one. It expires in a few minutes."));
    }

    /// <summary>Proves a code and returns a short-lived single-use verification token.</summary>
    [HttpPost("otp/verify")]
    [ProducesResponseType(typeof(CustomerCodeVerifiedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerCodeVerifiedDto>> VerifyCode(
        [FromBody] VerifyCustomerCodeRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await accounts.VerifyCodeAsync(request, cancellationToken));
    }

    /// <summary>Sets the first password and activates the account; signs the customer in.</summary>
    /// <remarks>
    /// Spends an <c>activation</c> verification token. When the number had no
    /// customer record (<c>needsProfile</c> from verify), <c>firstName</c> and
    /// <c>lastName</c> are required and the record is created. Email is always
    /// optional. 409 when the account is already active.
    /// </remarks>
    [HttpPost("activate")]
    [ProducesResponseType(typeof(AuthUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthUserDto>> Activate(
        [FromBody] ActivateCustomerAccountRequest request,
        CancellationToken cancellationToken)
    {
        var user = await accounts.ActivateAsync(request, cancellationToken);
        var token = tokens.Issue(user, await auth.GetSecurityStampAsync(user.Id, cancellationToken));

        AuthCookie.Append(Response, token);

        return Ok(user);
    }

    /// <summary>Sets a new password with a <c>password-reset</c> verification token.</summary>
    /// <remarks>Every session issued before the reset stops working.</remarks>
    [HttpPost("password/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetCustomerPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await accounts.ResetPasswordAsync(request, cancellationToken);

        return NoContent();
    }
}

public sealed record CodeRequestAcceptedDto(string Message);
