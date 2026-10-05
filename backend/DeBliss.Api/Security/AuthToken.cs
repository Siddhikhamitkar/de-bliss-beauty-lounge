namespace DeBliss.Api.Security;

/// <summary>
/// Shared constants for the cookie half of token delivery. The SPA is served
/// same-origin through the Vite proxy, so the browser session rides an
/// HttpOnly, SameSite=Strict cookie; the Authorization header stays supported
/// for Swagger and non-browser clients.
/// </summary>
public static class AuthToken
{
    public const string CookieName = "debliss_auth";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(60);

    public const string SigningKeyConfigPath = "Authentication:Jwt:SigningKey";
}
