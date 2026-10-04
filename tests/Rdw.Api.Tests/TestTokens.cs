using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
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

    public static string Create(
        IDictionary<string, object> claims,
        string issuer = GoogleIssuer,
        string audience = ClientId,
        DateTime? expiresUtc = null,
        SecurityKey? signingKey = null,
        bool signed = true)
    {
        var expires = expiresUtc ?? DateTime.UtcNow.AddMinutes(30);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = expires.AddHours(-1),
            NotBefore = expires.AddHours(-1),
            Expires = expires,
            SigningCredentials = signed
                ? new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256)
                : null,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>
    /// Configures the factory with test settings, a fake RDW client and the local signing key.
    /// Pass null for <paramref name="clientId"/> to leave the client ID unconfigured.
    /// </summary>
    public static WebApplicationFactory<Program> WithTestAuth(
        this WebApplicationFactory<Program> factory,
        IRdwClient rdwClient,
        string[]? allowedHostedDomains = null,
        string[]? allowedSubjects = null,
        string? clientId = ClientId)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            if (clientId is not null)
            {
                builder.UseSetting("Authentication:Google:ClientId", clientId);
            }

            UseList(builder, "Authorization:AllowedHostedDomains", allowedHostedDomains);
            UseList(builder, "Authorization:AllowedSubjects", allowedSubjects);

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

    private static void UseList(IWebHostBuilder builder, string key, string[]? values)
    {
        for (var i = 0; i < (values?.Length ?? 0); i++)
        {
            builder.UseSetting($"{key}:{i}", values![i]);
        }
    }
}
