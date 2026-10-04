using Rdw.Api;
using Rdw.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
// Liveness only: no checks are registered, so /health never calls the RDW.
builder.Services.AddHealthChecks();
builder.Services.AddGoogleAuthentication();
builder.Services.AddApiRateLimiting();
builder.Services.AddHttpClient<IRdwClient, RdwClient>(client => client.Timeout = TimeSpan.FromSeconds(15));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
// After authentication and authorization: requests answered with 401 or 403 use no quota.
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/api/v1/vehicles/{licensePlate}", VehicleEndpoint.GetVehicleAsync)
    .WithName("GetVehicle");

// The only anonymous endpoint (ADR-005): platform probes call it without a token, and it returns no data.
// Not rate limited, so probes never use quota and are never answered with 429.
app.MapHealthChecks("/health").AllowAnonymous().DisableRateLimiting();

app.Run();

public partial class Program;
