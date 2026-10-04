using System.Net;
using System.Text;
using Rdw.Core;

namespace Rdw.Core.Tests;

public class RdwClientTests
{
    private const string KiaJsonArray = """
        [
          {
            "kenteken": "X998ZG",
            "voertuigsoort": "Personenauto",
            "merk": "KIA",
            "handelsbenaming": "NIRO",
            "eerste_kleur": "GRIJS",
            "massa_ledig_voertuig": "1657",
            "datum_eerste_toelating": "20240320",
            "vervaldatum_apk": "20280320"
          }
        ]
        """;

    [Fact]
    public async Task GetVehicleAsync_KnownPlate_ReturnsFound()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(KiaJsonArray));
        var client = new RdwClient(new HttpClient(handler));

        var result = await client.GetVehicleAsync("x-998-zg");

        Assert.Equal(LookupStatus.Found, result.Status);
        Assert.Equal("KIA", result.Vehicle?.Make);
        var requestedUri = Assert.Single(handler.RequestedUris);
        Assert.Contains("kenteken=X998ZG", requestedUri.Query);
    }

    [Fact]
    public async Task GetVehicleAsync_UnknownPlate_ReturnsNotFound()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));
        var client = new RdwClient(new HttpClient(handler));

        var result = await client.GetVehicleAsync("ZZ-999-Z");

        Assert.Equal(LookupStatus.NotFound, result.Status);
        Assert.Null(result.Vehicle);
        Assert.Equal("ZZ999Z", result.LicensePlate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AB12")]
    [InlineData("AB-123")]
    [InlineData("ABCDEFG")]
    public async Task GetVehicleAsync_ImpossibleInput_ReturnsInvalidInputWithoutCallingRdw(string? input)
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));
        var client = new RdwClient(new HttpClient(handler));

        var result = await client.GetVehicleAsync(input);

        Assert.Equal(LookupStatus.InvalidInput, result.Status);
        Assert.Empty(handler.RequestedUris);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GetVehicleAsync_HttpError_ReturnsServiceUnavailable(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode));
        var client = new RdwClient(new HttpClient(handler));

        var result = await client.GetVehicleAsync("X998ZG");

        Assert.Equal(LookupStatus.ServiceUnavailable, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task GetVehicleAsync_NetworkFailure_ReturnsServiceUnavailable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("Network down."));
        var client = new RdwClient(new HttpClient(handler));

        var result = await client.GetVehicleAsync("X998ZG");

        Assert.Equal(LookupStatus.ServiceUnavailable, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
