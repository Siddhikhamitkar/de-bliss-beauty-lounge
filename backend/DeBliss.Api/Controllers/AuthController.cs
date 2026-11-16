using DeBliss.Application.Identity;
using DeBliss.Application.Identity.Dtos;
using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAuthService auth,
    JwtTokenIssuer tokens,
    TokenRevocation revocations) : ControllerBase
{
    /// <summary>Signs in with an email (or, for customers, a mobile number) and password.</summary>
    /// <remarks>
    /// <para>
    /// Send exactly one of <c>email</c> or <c>phoneNumber</c>. Team accounts and
    /// existing email customers use email; a customer whose account is active and
    /// whose mobile number is verified may use the number in any accepted format.
    /// </para>
    /// <para>
    /// Deliberately anonymous. Every failure — unknown email or number, wrong
    /// password, deactivated or not-yet-activated account — returns the same
    /// generic 401 so callers cannot probe which accounts exist.
    /// </para>
    /// <para>
    /// On success the response sets an HttpOnly, SameSite=Strict, Secure cookie
    /// (<c>debliss_auth</c>) holding the short-lived JWT (60 minutes) and returns
    /// the safe user view (never the password hash). The cookie is the SPA's
    /// transport; non-browser clients keep using the Authorization header.
    /// </para>
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimitPolicies.SignIn)]
    [ProducesResponseType(typeof(AuthUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthUserDto>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
        var hasPhone = !string.IsNullOrWhiteSpace(request.PhoneNumber);

        if (hasEmail == hasPhone)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["login"] = ["Sign in with either an email address or a mobile number."]
            }));
        }

        var user = hasPhone
            ? await auth.AuthenticateByPhoneAsync(request.PhoneNumber!.Trim(), request.Password, cancellationToken)
            : await auth.AuthenticateAsync(request.Email!.Trim(), request.Password, cancellationToken);

        if (user is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Login failed",
                Detail = hasPhone
                    ? "Mobile number or password is incorrect."
                    : "Email or password is incorrect.",
                Status = StatusCodes.Status401Unauthorized,
            });
        }

        var token = tokens.Issue(user, await auth.GetSecurityStampAsync(user.Id, cancellationToken));

        AuthCookie.Append(Response, token);

        return Ok(user);
    }

    /// <summary>The current signed-in user, or 401 when there is none.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(AuthUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirst("sub")!.Value);
        var user = await auth.GetUserAsync(userId, cancellationToken);

        return user is null ? Unauthorized() : Ok(user);
    }

    /// <summary>
    /// Signs the presented token out: revokes it so replaying it fails with 401, and
    /// drops the session cookie. Anonymous on purpose — it must also work when the
    /// token just expired.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        if (TryGetPresentedToken(out var token))
        {
            var tokenId = TokenRevocation.ExtractTokenId(token);
            if (!string.IsNullOrEmpty(tokenId))
            {
                revocations.Revoke(tokenId);
            }
        }

        Response.Cookies.Delete(AuthToken.CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });

        return NoContent();
    }

    private bool TryGetPresentedToken(out string token)
    {
        token = string.Empty;
        var header = Request.Headers.Authorization.ToString();

        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            header.Length > "Bearer ".Length)
        {
            token = header["Bearer ".Length..].Trim();
            return true;
        }

        return Request.Cookies.TryGetValue(AuthToken.CookieName, out token!);
    }
}
