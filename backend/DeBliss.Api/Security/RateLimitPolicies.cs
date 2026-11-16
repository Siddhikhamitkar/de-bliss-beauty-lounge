using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DeBliss.Api.Security;

/// <summary>
/// Per-client-IP limits on the anonymous endpoints that guess at secrets: sign-in
/// and the customer one-time-code flow. They sit on top of the per-number limits
/// the account service enforces (resend cooldown, hourly cap, attempts per code),
/// so neither a single number nor a single client can be hammered.
/// </summary>
/// <remarks>
/// Behind a reverse proxy, configure forwarded headers so the client IP is the
/// real caller's — otherwise every request shares the proxy's partition.
/// </remarks>
public static class RateLimitPolicies
{
    public const string SignIn = nameof(SignIn);

    public const string CustomerCodes = nameof(CustomerCodes);

    public static IServiceCollection AddDeblissRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Title = "Too many requests.",
                        Detail = "Too many attempts from this device. Please wait a minute and try again.",
                        Status = StatusCodes.Status429TooManyRequests,
                        Instance = context.HttpContext.Request.Path,
                    },
                    cancellationToken);
            };

            options.AddPolicy(SignIn, context => PerClientIp(context, permitsPerMinute: 20));
            options.AddPolicy(CustomerCodes, context => PerClientIp(context, permitsPerMinute: 10));
        });

        return services;
    }

    private static RateLimitPartition<string> PerClientIp(HttpContext context, int permitsPerMinute)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();

        // No address only happens in-process (integration tests): nothing to key on.
        if (ip is null)
        {
            return RateLimitPartition.GetNoLimiter("in-process");
        }

        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }
}
