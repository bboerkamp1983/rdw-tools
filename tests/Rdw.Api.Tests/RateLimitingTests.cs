using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rdw.Core;

namespace Rdw.Api.Tests;

/// <summary>
/// ADR-005, Consequences > Rate limiting. Limits are tiny and windows are an hour long, so no
/// window can expire during a test: the tests never sleep or depend on timing.
/// </summary>
public class RateLimitingTests : IClassFixture<ApiTestFactory>
{
    private const string VehicleUrl = "/api/v1/vehicles/X998ZG";
    private const string UserA = "100000000000000000001";
    private const string UserB = "100000000000000000002";
    private const string UserC = "100000000000000000003";

    private static readonly Vehicle Kia = new() { LicensePlate = "X998ZG", Make = "KIA" };

    private readonly ApiTestFactory _factory;
    private readonly FakeRdwClient _rdw = new(VehicleLookupResult.Found(Kia));

    public RateLimitingTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UserOverLimit_Gets429WithRetryAfter_WhileOtherUserGets200()
    {
        var client = CreateClient(perUser: 2, global: 100);

        Assert.Equal(HttpStatusCode.OK, (await Send(client, UserA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, UserA)).StatusCode);
        var rejected = await Send(client, UserA);
        var otherUser = await Send(client, UserB);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        AssertRetryAfter(rejected);
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);
        Assert.Equal(3, _rdw.RequestedPlates.Count);
    }

    [Fact]
    public async Task GlobalLimit_Returns429AcrossUsers()
    {
        var client = CreateClient(perUser: 100, global: 2);

        Assert.Equal(HttpStatusCode.OK, (await Send(client, UserA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, UserB)).StatusCode);
        var rejected = await Send(client, UserC);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        AssertRetryAfter(rejected);
        Assert.Equal(2, _rdw.RequestedPlates.Count);
    }

    [Fact]
    public async Task UnauthenticatedRequests_Return401AndUseNoQuota()
    {
        var client = CreateClient(perUser: 1, global: 1);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(VehicleUrl)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await Send(client, UserA)).StatusCode);
    }

    [Fact]
    public async Task ForbiddenRequests_UseNoQuota()
    {
        var client = CreateClient(perUser: 1, global: 1);
        var notAllowed = TestTokens.Create(TestTokens.PersonalClaims("999999999999999999999", "x@gmail.com"));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, token: notAllowed)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await Send(client, UserA)).StatusCode);
    }

    [Fact]
    public async Task RateLimitResponse_IsDistinctFromRdwUnavailable()
    {
        var unavailable = new FakeRdwClient(VehicleLookupResult.ServiceUnavailable("The RDW could not be reached."));
        var client = CreateClient(perUser: 1, global: 100, rdwClient: unavailable);

        var rdwDown = await Send(client, UserA);
        var limited = await Send(client, UserA);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, rdwDown.StatusCode);
        Assert.False(rdwDown.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        AssertRetryAfter(limited);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        using var rdwDownJson = JsonDocument.Parse(await rdwDown.Content.ReadAsStringAsync());
        using var limitedJson = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.Equal(429, limitedJson.RootElement.GetProperty("status").GetInt32());
        Assert.NotEqual(
            rdwDownJson.RootElement.GetProperty("title").GetString(),
            limitedJson.RootElement.GetProperty("title").GetString());
        Assert.Single(unavailable.RequestedPlates);
    }

    [Fact]
    public async Task Health_IsNotRateLimited()
    {
        var client = CreateClient(perUser: 1, global: 1);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

            using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(UserA));
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        }

        // /health did not use the user's or the global quota.
        Assert.Equal(HttpStatusCode.OK, (await Send(client, UserA)).StatusCode);
    }

    [Fact]
    public async Task PerUserLimit_IsPartitionedOnSubNotEmail()
    {
        var client = CreateClient(perUser: 1, global: 100);
        var subA = TestTokens.Create(TestTokens.PersonalClaims(UserA, "first@gmail.com"));
        var subAOtherEmail = TestTokens.Create(TestTokens.PersonalClaims(UserA, "second@gmail.com"));
        var subBSameEmail = TestTokens.Create(TestTokens.PersonalClaims(UserB, "first@gmail.com"));

        Assert.Equal(HttpStatusCode.OK, (await Send(client, token: subA)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(client, token: subAOtherEmail)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, token: subBSameEmail)).StatusCode);
    }

    [Fact]
    public async Task TwoAppInstances_DoNotShareLimits()
    {
        // The limiters live in the memory of one app instance: a second instance has its own counters.
        var first = CreateClient(perUser: 1, global: 1);
        var second = CreateClient(perUser: 1, global: 1);

        Assert.Equal(HttpStatusCode.OK, (await Send(first, UserA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(second, UserA)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(first, UserA)).StatusCode);
    }

    [Fact]
    public void Defaults_AreTheDocumentedConservativeValues()
    {
        var factory = _factory.WithTestAuth(_rdw);

        var options = factory.Services.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        Assert.Equal(10, options.PerUser.PermitLimit);
        Assert.Equal(60, options.PerUser.WindowSeconds);
        Assert.Equal(60, options.Global.PermitLimit);
        Assert.Equal(60, options.Global.WindowSeconds);
    }

    [Theory]
    [InlineData("RateLimiting:PerUser:PermitLimit")]
    [InlineData("RateLimiting:PerUser:WindowSeconds")]
    [InlineData("RateLimiting:Global:PermitLimit")]
    [InlineData("RateLimiting:Global:WindowSeconds")]
    public void ZeroOrNegativeValue_StopsTheAppFromStarting(string key)
    {
        var factory = _factory.WithTestAuth(_rdw, extraSettings: new Dictionary<string, string?> { [key] = "0" });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    private HttpClient CreateClient(int perUser, int global, IRdwClient? rdwClient = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RateLimiting:PerUser:PermitLimit"] = perUser.ToString(),
            ["RateLimiting:PerUser:WindowSeconds"] = "3600",
            ["RateLimiting:Global:PermitLimit"] = global.ToString(),
            ["RateLimiting:Global:WindowSeconds"] = "3600",
        };

        return _factory
            .WithTestAuth(rdwClient ?? _rdw, allowedSubjects: [UserA, UserB, UserC], extraSettings: settings)
            .CreateClient();
    }

    private static string TokenFor(string subject) =>
        TestTokens.Create(TestTokens.PersonalClaims(subject, $"{subject}@gmail.com"));

    private static async Task<HttpResponseMessage> Send(HttpClient client, string? subject = null, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, VehicleUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? TokenFor(subject!));
        return await client.SendAsync(request);
    }

    private static void AssertRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        Assert.NotNull(retryAfter);
        Assert.NotNull(retryAfter.Delta);
        Assert.True(retryAfter.Delta > TimeSpan.Zero, $"Retry-After should be positive, was {retryAfter.Delta}.");
    }
}
