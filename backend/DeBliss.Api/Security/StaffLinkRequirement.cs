using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Security;

/// <summary>
/// The claim names the token issuer writes and the API reads back. Claims keep
/// their short JWT names because inbound claim mapping is off, so these literals
/// are the contract on both sides.
/// </summary>
public static class AuthClaimNames
{
    public const string Role = "role";

    /// <summary>Present only when the login is linked to an existing Staff record.</summary>
    public const string StaffId = "staff_id";
}

/// <summary>
/// Grants the "my …" surface to a signed-in user whose token carries a usable
/// staff link. Any role may hold one — a receptionist who also performs services
/// sees exactly the same self-scoped data as a stylist — and a login with no
/// Staff record is refused rather than served an empty view.
/// </summary>
public sealed class StaffLinkRequirement : IAuthorizationRequirement;

public sealed class StaffLinkHandler : AuthorizationHandler<StaffLinkRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StaffLinkRequirement requirement)
    {
        if (StaffSelfIdentity.TryResolve(context.User, out _))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// The one place a "my …" endpoint learns which staff member is asking. The id
/// comes from the signed token only, so a client cannot name somebody else's
/// record in a route, a query string or a body.
/// </summary>
public static class StaffSelfIdentity
{
    public static bool TryResolve(ClaimsPrincipal user, out Guid staffId) =>
        Guid.TryParse(user.FindFirstValue(AuthClaimNames.StaffId), out staffId);

    /// <summary>For use behind <see cref="AuthPolicies.StaffSelf"/>, which has already proved the claim exists.</summary>
    public static Guid Resolve(ClaimsPrincipal user)
    {
        TryResolve(user, out var staffId);

        return staffId;
    }
}
