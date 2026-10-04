using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Rdw.Core;

namespace Rdw.Api.Tests;

/// <summary>
/// Guards that the API test host ignores the machine's configuration, so tests give the same
/// result locally as in CI, whatever environment variables or user secrets a developer has set.
/// </summary>
public class ConfigurationIsolationTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public ConfigurationIsolationTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void TestHost_HasNoEnvironmentVariablesOrJsonFiles()
    {
        var providers = Providers(_factory);

        Assert.DoesNotContain(providers, p => p is EnvironmentVariablesConfigurationProvider);
        // User secrets and appsettings files are both JSON file providers.
        Assert.DoesNotContain(providers, p => p is JsonConfigurationProvider);
    }

    [Fact]
    public void TestHost_ContainsOnlyTheSettingsTheTestSets()
    {
        var factory = _factory.WithTestAuth(
            new FakeRdwClient(VehicleLookupResult.NotFound("X998ZG")),
            allowedSubjects: [TestTokens.AllowedSubject]);

        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        Assert.Equal(TestTokens.ClientId, configuration[GoogleAuthentication.ClientIdKey]);
        Assert.Equal(TestTokens.AllowedSubject, configuration["Authorization:AllowedSubjects:0"]);
        Assert.Empty(configuration.GetSection("Authorization:AllowedHostedDomains").GetChildren());
    }

    private static IEnumerable<IConfigurationProvider> Providers(ApiTestFactory factory)
    {
        var configuration = (IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>();
        return configuration.Providers;
    }
}
