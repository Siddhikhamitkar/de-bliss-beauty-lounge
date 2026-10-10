using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DeBliss.Application.Identity.Dtos;
using Microsoft.IdentityModel.Tokens;

namespace DeBliss.Api.Security;

/// <summary>
/// Signs short-lived access tokens (60 minutes) carrying the user's id, name,
/// email, roles and optional staff link as claims. A per-token identifier (jti)
/// lets the logout flow revoke the exact token that was presented. No refresh
/// token yet — that is a documented follow-up; sessions simply expire and the
/// SPA returns to the login page.
/// </summary>
public sealed class JwtTokenIssuer(IConfiguration configuration, TimeProvider clock)
{
    public string Issue(AuthUserDto user)
    {
        var now = clock.GetUtcNow();
        var expires = now.Add(AuthToken.Lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new(JwtRegisteredClaimNames.FamilyName, user.LastName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        claims.AddRange(user.Roles.Select(role => new Claim(AuthClaimNames.Role, role)));

        if (user.StaffId is { } staffId)
        {
            claims.Add(new Claim(AuthClaimNames.StaffId, staffId.ToString()));
        }

        if (user.CustomerId is { } customerId)
        {
            claims.Add(new Claim(AuthClaimNames.CustomerId, customerId.ToString()));
        }

        var credentials = new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public SymmetricSecurityKey SigningKey()
    {
        var secret = configuration[AuthToken.SigningKeyConfigPath];

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                $"JWT signing key is not configured. Set '{AuthToken.SigningKeyConfigPath}' " +
                "via configuration or an environment variable — never in source code. " +
                "A development-only key ships in appsettings.Development.json; other " +
                "environments must provide their own.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }
}
