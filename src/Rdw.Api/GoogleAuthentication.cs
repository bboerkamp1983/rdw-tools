using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Rdw.Api;

/// <summary>
/// Google ID token authentication and access rules (ADR-005). Every endpoint requires an allowed
/// Google account unless it is explicitly marked anonymous.
/// </summary>
public static class GoogleAuthentication
{
    public const string Authority = "https://accounts.google.com";

    public const string ClientIdKey = "Authentication:Google:ClientId";

    // Google uses either value in the iss claim of an ID token.
    public static readonly string[] ValidIssuers = ["https://accounts.google.com", "accounts.google.com"];

    public static IServiceCollection AddGoogleAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Read configuration when the options are built, not at startup, so a missing client ID
        // rejects every token (audience validation fails) instead of opening the API.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.Authority = Authority;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = ValidIssuers,
                    ValidateAudience = true,
                    ValidAudience = configuration[ClientIdKey],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                };
            });

        services.AddOptions<GoogleAccessOptions>().BindConfiguration(GoogleAccessOptions.SectionName);
        services.AddSingleton<IAuthorizationHandler, GoogleAccountHandler>();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new GoogleAccountRequirement())
                .Build());

        return services;
    }
}

/// <summary>Who may use the API. Both lists are empty by default, which denies everyone.</summary>
public sealed class GoogleAccessOptions
{
    public const string SectionName = "Authorization";

    /// <summary>Allowed values of the <c>hd</c> claim (Google Workspace domains).</summary>
    public string[] AllowedHostedDomains { get; set; } = [];

    /// <summary>Allowed values of the <c>sub</c> claim (individual Google accounts).</summary>
    public string[] AllowedSubjects { get; set; } = [];
}

public sealed class GoogleAccountRequirement : IAuthorizationRequirement;

/// <summary>
/// Allows a user whose <c>hd</c> claim is an allowed domain, or whose <c>sub</c> is in the allow-list.
/// The domain of the <c>email</c> claim is never used: a personal Google account can have a work address.
/// </summary>
public sealed class GoogleAccountHandler : AuthorizationHandler<GoogleAccountRequirement>
{
    private readonly IOptionsMonitor<GoogleAccessOptions> _options;

    public GoogleAccountHandler(IOptionsMonitor<GoogleAccessOptions> options)
    {
        _options = options;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        GoogleAccountRequirement requirement)
    {
        var options = _options.CurrentValue;
        var hostedDomain = context.User.FindFirst("hd")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;

        var domainAllowed = !string.IsNullOrEmpty(hostedDomain)
            && options.AllowedHostedDomains.Contains(hostedDomain, StringComparer.OrdinalIgnoreCase);
        var subjectAllowed = !string.IsNullOrEmpty(subject)
            && options.AllowedSubjects.Contains(subject, StringComparer.Ordinal);

        if (domainAllowed || subjectAllowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
