using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Rdw.Core;

namespace Rdw.Api.Tests;

/// <summary>
/// Creates locally signed tokens with the claims shape of Google ID tokens, and configures the API
/// to trust the local signing key instead of Google's. Issuer, audience and lifetime are still
/// validated by the production settings. Tests never contact Google.
/// </summary>
public static class TestTokens
{
    public const string ClientId = "test-client-id.apps.googleusercontent.com";
    public const string GoogleIssuer = "https://accounts.google.com";
    public const string HostedDomain = "euromaster.com";
    public const string AllowedSubject = "100000000000000000001";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-signing-key" };

    public static RsaSecurityKey CreateOtherKey() => new(RSA.Create(2048)) { KeyId = "other-key" };

    public static Dictionary<string, object> EuromasterClaims() => new()
    {
        ["sub"] = "200000000000000000002",
        ["email"] = "jan@euromaster.com",
        ["email_verified"] = true,
        ["hd"] = HostedDomain,
    };

    /// <summary>Claims of an individual (non-Workspace) Google account.</summary>
    public static Dictionary<string, object> PersonalClaims(string subject, string email) => new()
    {
        ["sub"] = subject,
        ["email"] = email,
        ["email_verified"] = true,
    };

    public static string Create(
        IDictionary<string, object> claims,
        string issuer = GoogleIssuer,
        string audience = ClientId,
        DateTime? expiresUtc = null,
        SecurityKey? signingKey = null,
        bool signed = true,
        bool includeExpiry = true)
    {
        var expires = expiresUtc ?? DateTime.UtcNow.AddMinutes(30);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = expires.AddHours(-1),
            NotBefore = expires.AddHours(-1),
            Expires = includeExpiry ? expires : null,
            SigningCredentials = signed
                ? new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256)
                : null,
        };

        // Without this the handler adds a default exp claim.
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = includeExpiry };
        return handler.CreateToken(descriptor);
    }

    /// <summary>
    /// Configures the isolated test host with test settings, a fake RDW client and the local signing key.
    /// The settings passed here are the only configuration the host has (see <see cref="ApiTestFactory"/>).
    /// Pass null for <paramref name="clientId"/> to leave the client ID unconfigured.
    /// <paramref name="extraSettings"/> adds other settings, such as rate limits.
    /// </summary>
    public static WebApplicationFactory<Program> WithTestAuth(
        this ApiTestFactory factory,
        IRdwClient rdwClient,
        string[]? allowedHostedDomains = null,
        string[]? allowedSubjects = null,
        string? clientId = ClientId,
        IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        var settings = new Dictionary<string, string?>(extraSettings ?? new Dictionary<string, string?>());
        if (clientId is not null)
        {
            settings[GoogleAuthentication.ClientIdKey] = clientId;
        }

        AddList(settings, "Authorization:AllowedHostedDomains", allowedHostedDomains);
        AddList(settings, "Authorization:AllowedSubjects", allowedSubjects);

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(rdwClient);
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    // Replace only the source of the signing keys (Google's discovery document).
                    var configuration = new OpenIdConnectConfiguration { Issuer = GoogleIssuer };
                    configuration.SigningKeys.Add(SigningKey);
                    options.Configuration = configuration;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
            });
        });
    }

    private static void AddList(Dictionary<string, string?> settings, string key, string[]? values)
    {
        for (var i = 0; i < (values?.Length ?? 0); i++)
        {
            settings[$"{key}:{i}"] = values![i];
        }
    }
}
