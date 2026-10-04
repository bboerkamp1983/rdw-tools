using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Rdw.Api.Tests;

/// <summary>
/// Test host for the API whose configuration contains only what a test sets explicitly.
/// Environment variables, user secrets, appsettings files and command-line arguments are removed,
/// so a developer's local configuration (README) cannot change test results.
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.Sources.Clear());
    }
}
