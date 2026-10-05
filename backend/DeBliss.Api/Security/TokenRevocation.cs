using Microsoft.IdentityModel.JsonWebTokens;

namespace DeBliss.Api.Security;

/// <summary>
/// In-memory set of revoked token identifiers. Logout records the presented token's
/// <c>jti</c> so the (otherwise stateless) JWT cannot be replayed until it expires.
/// Valid for the current single-server deployment; a shared store is required if the
/// API is ever scaled out.
/// </summary>
public sealed class TokenRevocation
{
    private readonly HashSet<string> _revoked = new(StringComparer.Ordinal);

    public void Revoke(string tokenId)
    {
        lock (_revoked)
        {
            _revoked.Add(tokenId);
        }
    }

    public bool IsRevoked(string tokenId)
    {
        lock (_revoked)
        {
            return _revoked.Contains(tokenId);
        }
    }

    public static string? ExtractTokenId(string token)
    {
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token);
        return jwt.Id;
    }
}
