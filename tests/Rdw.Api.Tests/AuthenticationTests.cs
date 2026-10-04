using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Rdw.Core;

namespace Rdw.Api.Tests;

/// <summary>The test table of ADR-005, "Proving the lock works", plus a few extra cases.</summary>
public class AuthenticationTests : IClassFixture<ApiTestFactory>
{
    private const string VehicleUrl = "/api/v1/vehicles/X998ZG";

    private static readonly Vehicle Kia = new() { LicensePlate = "X998ZG", Make = "KIA" };

    private readonly ApiTestFactory _factory;
    private readonly FakeRdwClient _rdw = new(VehicleLookupResult.Found(Kia));

    public AuthenticationTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NoToken_Returns401AndDoesNotCallRdw()
    {
        var response = await Send(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task WrongAudience_Returns401()
    {
        var token = TestTokens.Create(TestTokens.EuromasterClaims(), audience: "someone-elses-client-id");

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task WrongIssuer_Returns401()
    {
        var token = TestTokens.Create(TestTokens.EuromasterClaims(), issuer: "https://evil.example.com");

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        // Older than the default five minutes of allowed clock skew.
        var token = TestTokens.Create(TestTokens.EuromasterClaims(), expiresUtc: DateTime.UtcNow.AddMinutes(-10));

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task TokenSignedWithUnknownKey_Returns401()
    {
        var token = TestTokens.Create(TestTokens.EuromasterClaims(), signingKey: TestTokens.CreateOtherKey());

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task TokenWithoutExpiry_Returns401()
    {
        var token = TestTokens.Create(TestTokens.EuromasterClaims(), includeExpiry: false);

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task UnsignedToken_Returns401()
    {
        var token = TestTokens.Create(TestTokens.EuromasterClaims(), signed: false);

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Theory]
    [InlineData("https://accounts.google.com")]
    [InlineData("accounts.google.com")]
    public async Task AllowedHostedDomain_Returns200ForBothGoogleIssuers(string issuer)
    {
        var token = TestTokens.Create(TestTokens.EuromasterClaims(), issuer: issuer);

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["X998ZG"], _rdw.RequestedPlates);
    }

    [Fact]
    public async Task EuromasterEmailWithoutHdClaim_Returns403()
    {
        // A personal Google account created with a work address: never trust the email domain.
        var claims = TestTokens.EuromasterClaims();
        claims.Remove("hd");
        var token = TestTokens.Create(claims);

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task OtherHostedDomain_Returns403()
    {
        var claims = TestTokens.EuromasterClaims();
        claims["hd"] = "other-domain.com";
        claims["email"] = "piet@other-domain.com";
        var token = TestTokens.Create(claims);

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task SubjectInAllowList_Returns200()
    {
        var token = TestTokens.Create(new Dictionary<string, object>
        {
            ["sub"] = TestTokens.AllowedSubject,
            ["email"] = "owner@gmail.com",
        });

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["X998ZG"], _rdw.RequestedPlates);
    }

    [Fact]
    public async Task SubjectNotInAllowList_Returns403()
    {
        var token = TestTokens.Create(new Dictionary<string, object>
        {
            ["sub"] = "300000000000000000003",
            ["email"] = "someone.else@gmail.com",
        });

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task EmailOfAllowedUserButDifferentSubject_Returns403()
    {
        // Individual users are identified by sub, never by email.
        var token = TestTokens.Create(new Dictionary<string, object>
        {
            ["sub"] = "300000000000000000003",
            ["email"] = "owner@gmail.com",
        });

        var response = await Send(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task EmptyAllowLists_DenyEveryone()
    {
        var client = _factory.WithTestAuth(_rdw).CreateClient();

        var response = await Send(client, TestTokens.Create(TestTokens.EuromasterClaims()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoConfigurationAtAll_RejectsEveryRequest(bool withValidToken)
    {
        // No client ID and no allow-lists: the state of a fresh deployment.
        var client = _factory.WithTestAuth(_rdw, clientId: null).CreateClient();
        var token = withValidToken ? TestTokens.Create(TestTokens.EuromasterClaims()) : null;

        var response = await Send(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task ClientIdNotConfigured_Returns401()
    {
        var client = _factory
            .WithTestAuth(_rdw, [TestTokens.HostedDomain], [TestTokens.AllowedSubject], clientId: null)
            .CreateClient();

        var response = await Send(client, TestTokens.Create(TestTokens.EuromasterClaims()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_rdw.RequestedPlates);
    }

    [Fact]
    public async Task Health_WithoutToken_Returns200()
    {
        var client = _factory
            .WithTestAuth(_rdw, [TestTokens.HostedDomain], [TestTokens.AllowedSubject])
            .CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> Send(string? token)
    {
        var client = _factory
            .WithTestAuth(_rdw, [TestTokens.HostedDomain], [TestTokens.AllowedSubject])
            .CreateClient();

        return Send(client, token);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, VehicleUrl);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }
}
