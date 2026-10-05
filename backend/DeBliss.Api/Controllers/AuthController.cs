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
    /// <summary>Signs in with an email and password.</summary>
    /// <remarks>
    /// <para>
    /// Deliberately anonymous. Every failure — unknown email, wrong password,
    /// deactivated account — returns the same generic 401 so callers cannot
    /// probe which addresses exist.
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
    [ProducesResponseType(typeof(AuthUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthUserDto>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await auth.AuthenticateAsync(
            request.Email.Trim(),
            request.Password,
            cancellationToken);

        if (user is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Login failed",
                Detail = "Email or password is incorrect.",
                Status = StatusCodes.Status401Unauthorized,
            });
        }

        var token = tokens.Issue(user);

        Response.Cookies.Append(AuthToken.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = AuthToken.Lifetime,
        });

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
