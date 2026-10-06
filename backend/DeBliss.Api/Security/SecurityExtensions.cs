using DeBliss.Application.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace DeBliss.Api.Security;

/// <summary>Policy names shared by the authorization registration and the controllers.</summary>
public static class AuthPolicies
{
    /// <summary>Owner-only surface: the future owner dashboard, reports, settings, user administration.</summary>
    public const string OwnerOnly = nameof(OwnerOnly);

    /// <summary>Day-to-day operations the front desk runs: calendar, customers, services, staff, billing.</summary>
    public const string Operational = nameof(Operational);

    /// <summary>
    /// A signed-in user reading or writing only their own staff record — the
    /// surface the future staff mobile app consumes.
    /// </summary>
    public const string StaffSelf = nameof(StaffSelf);
}

public static class SecurityExtensions
{
    public static IServiceCollection AddDeblissSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var issuer = new JwtTokenIssuer(configuration, TimeProvider.System);
        var revocations = new TokenRevocation();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Claims keep their short JWT names ("sub", "role") instead of the
                // mapped ClaimTypes URIs, so controllers read the same names the
                // issuer wrote.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = issuer.SigningKey(),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };
                // The SPA sends the same token in an HttpOnly cookie (set at login);
                // Swagger and script clients keep using the Authorization header.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrEmpty(context.Token) &&
                            context.Request.Cookies.TryGetValue(AuthToken.CookieName, out var cookie))
                        {
                            context.Token = cookie;
                        }

                        return Task.CompletedTask;
                    },
                    // A token presented after logout is rejected even though its
                    // signature and lifetime are still valid.
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.FindFirst("jti")?.Value is { } tokenId &&
                            revocations.IsRevoked(tokenId))
                        {
                            context.Fail("The presented token has been revoked.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthPolicies.OwnerOnly,
                policy => policy.RequireRole(RoleNames.Owner));
            options.AddPolicy(
                AuthPolicies.Operational,
                policy => policy.RequireRole(RoleNames.Owner, RoleNames.Receptionist));
            options.AddPolicy(
                AuthPolicies.StaffSelf,
                policy => policy.RequireAuthenticatedUser().AddRequirements(new StaffLinkRequirement()));
        });

        services.AddSingleton<IAuthorizationHandler, StaffLinkHandler>();

        services.AddSingleton(issuer);
        services.AddSingleton(revocations);

        return services;
    }

    /// <summary>Advertises the bearer scheme on the OpenAPI document so Swagger's Authorize button works.</summary>
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddDeblissOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                var scheme = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description =
                        "Paste a token obtained from POST /api/auth/login, or sign in via the login endpoint " +
                        $"and use the {AuthToken.CookieName} cookie the response sets.",
                };

                var components = document.Components ??= new OpenApiComponents();
                components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                components.SecuritySchemes["Bearer"] = scheme;
                var security = document.Security ??= [];
                security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
                });

                return Task.CompletedTask;
            });
        });

        return services;
    }
}
