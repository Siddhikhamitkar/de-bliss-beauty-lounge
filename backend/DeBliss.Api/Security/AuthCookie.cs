namespace DeBliss.Api.Security;

/// <summary>Writes and clears the session cookie the same way on every sign-in path.</summary>
public static class AuthCookie
{
    public static void Append(HttpResponse response, string token)
    {
        response.Cookies.Append(AuthToken.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = AuthToken.Lifetime,
        });
    }
}
