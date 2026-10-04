using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rdw.Core;

namespace Rdw.Api.Tests;

public class VehicleEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Vehicle Kia = new()
    {
        LicensePlate = "X998ZG",
        Make = "KIA",
        TradeName = "NIRO",
        VehicleType = "Personenauto",
        PrimaryColor = "GRIJS",
        BodyType = "stationwagen",
        EmptyMassKg = 1657,
        MaxPermittedMassKg = 2200,
        FirstAdmissionDate = new DateOnly(2024, 3, 20),
        ApkExpiryDate = new DateOnly(2028, 3, 20),
        IsExported = false,
    };

    private readonly WebApplicationFactory<Program> _factory;

    public VehicleEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Found_Returns200WithVehicle()
    {
        var fake = new FakeRdwClient(VehicleLookupResult.Found(Kia));

        var response = await CreateClient(fake).GetAsync("/api/v1/vehicles/x-998-zg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = await ReadJson(response);
        var root = json.RootElement;
        Assert.Equal("X998ZG", root.GetProperty("licensePlate").GetString());
        Assert.Equal("KIA", root.GetProperty("make").GetString());
        Assert.Equal("NIRO", root.GetProperty("tradeName").GetString());
        Assert.Equal("Personenauto", root.GetProperty("vehicleType").GetString());
        Assert.Equal("GRIJS", root.GetProperty("primaryColor").GetString());
        Assert.Equal("stationwagen", root.GetProperty("bodyType").GetString());
        Assert.Equal(1657, root.GetProperty("emptyMassKg").GetInt32());
        Assert.Equal(2200, root.GetProperty("maxPermittedMassKg").GetInt32());
        Assert.Equal("2024-03-20", root.GetProperty("firstAdmissionDate").GetString());
        Assert.Equal("2028-03-20", root.GetProperty("apkExpiryDate").GetString());
        Assert.False(root.GetProperty("isExported").GetBoolean());
    }

    [Fact]
    public async Task Found_HasExactlyTheFieldsFromAdr004()
    {
        var fake = new FakeRdwClient(VehicleLookupResult.Found(Kia));

        var response = await CreateClient(fake).GetAsync("/api/v1/vehicles/X998ZG");

        using var json = await ReadJson(response);
        var names = json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        string[] expected =
        [
            "apkExpiryDate", "bodyType", "emptyMassKg", "firstAdmissionDate", "isExported",
            "licensePlate", "make", "maxPermittedMassKg", "primaryColor", "tradeName", "vehicleType",
        ];
        Assert.Equal(expected, names);
    }

    [Fact]
    public async Task Found_UnknownValuesAreNullNotOmitted()
    {
        var bare = new Vehicle { LicensePlate = "AB123C" };
        var fake = new FakeRdwClient(VehicleLookupResult.Found(bare));

        var response = await CreateClient(fake).GetAsync("/api/v1/vehicles/AB123C");

        using var json = await ReadJson(response);
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("make").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("maxPermittedMassKg").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("apkExpiryDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("isExported").ValueKind);
    }

    [Fact]
    public async Task PassesPlateAsTypedToCore()
    {
        var fake = new FakeRdwClient(VehicleLookupResult.Found(Kia));

        await CreateClient(fake).GetAsync("/api/v1/vehicles/x-998-zg");

        Assert.Equal(["x-998-zg"], fake.RequestedPlates);
    }

    [Fact]
    public async Task NotFound_Returns404ProblemWithPlate()
    {
        var fake = new FakeRdwClient(VehicleLookupResult.NotFound("ZZ999Z"));

        var response = await CreateClient(fake).GetAsync("/api/v1/vehicles/ZZ-999-Z");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = await ReadJson(response);
        var root = json.RootElement;
        Assert.Equal(404, root.GetProperty("status").GetInt32());
        Assert.Equal("Vehicle not found", root.GetProperty("title").GetString());
        Assert.Equal("ZZ999Z", root.GetProperty("licensePlate").GetString());
        Assert.Contains("ZZ999Z", root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task InvalidInput_Returns400ProblemWithMessage()
    {
        var result = VehicleLookupResult.InvalidInput();
        var fake = new FakeRdwClient(result);

        var response = await CreateClient(fake).GetAsync("/api/v1/vehicles/AB12");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = await ReadJson(response);
        var root = json.RootElement;
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("Invalid license plate", root.GetProperty("title").GetString());
        Assert.Equal(result.Message, root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ServiceUnavailable_Returns503ProblemWithMessage()
    {
        var fake = new FakeRdwClient(VehicleLookupResult.ServiceUnavailable("Timeout while calling the RDW."));

        var response = await CreateClient(fake).GetAsync("/api/v1/vehicles/X998ZG");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = await ReadJson(response);
        var root = json.RootElement;
        Assert.Equal(503, root.GetProperty("status").GetInt32());
        Assert.Equal("Timeout while calling the RDW.", root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ClientDisconnect_CancelsTheLookup()
    {
        var waiting = new WaitingRdwClient();
        var httpClient = _factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<IRdwClient>(waiting)))
            .CreateClient();
        using var cts = new CancellationTokenSource();

        var request = httpClient.GetAsync("/api/v1/vehicles/X998ZG", cts.Token);
        await waiting.Started.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        await waiting.Cancelled.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public void RealClient_IsRegistered()
    {
        using var scope = _factory.Services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IRdwClient>();

        Assert.IsType<RdwClient>(client);
    }

    private HttpClient CreateClient(FakeRdwClient fake)
    {
        return _factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<IRdwClient>(fake)))
            .CreateClient();
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
