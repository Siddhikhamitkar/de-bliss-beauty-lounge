using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Security;

/// <summary>
/// Grants the customer self-service surface to a signed-in user whose token
/// carries a usable customer link. A login with no Customer record is refused
/// rather than served an empty view, and customers never reach staff or owner
/// data because those policies demand roles the customer role does not hold.
/// </summary>
public sealed class CustomerLinkRequirement : IAuthorizationRequirement;

public sealed class CustomerLinkHandler : AuthorizationHandler<CustomerLinkRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CustomerLinkRequirement requirement)
    {
        if (CustomerSelfIdentity.TryResolve(context.User, out _))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// The one place a customer self-service endpoint learns which customer is
/// asking. The id comes from the signed token only, so a client cannot name
/// somebody else's record in a route, a query string or a body.
/// </summary>
public static class CustomerSelfIdentity
{
    public static bool TryResolve(ClaimsPrincipal user, out Guid customerId) =>
        Guid.TryParse(user.FindFirstValue(AuthClaimNames.CustomerId), out customerId);

    /// <summary>For use behind <see cref="AuthPolicies.CustomerSelf"/>, which has already proved the claim exists.</summary>
    public static Guid Resolve(ClaimsPrincipal user)
    {
        TryResolve(user, out var customerId);

        return customerId;
    }
}
