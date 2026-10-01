using System.Text.Json;

namespace Rdw.Core;

public sealed class RdwClient : IRdwClient
{
    private const string DatasetUrl = "https://opendata.rdw.nl/resource/m9d7-ebf2.json";

    private readonly HttpClient _httpClient;

    public RdwClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    public async Task<VehicleLookupResult> GetVehicleAsync(string? licensePlate)
    {
        if (!LicensePlateNormalizer.TryNormalize(licensePlate, out var plate))
        {
            return VehicleLookupResult.InvalidInput();
        }

        var uri = new Uri($"{DatasetUrl}?kenteken={Uri.EscapeDataString(plate)}");

        try
        {
            using var response = await _httpClient.GetAsync(uri);

            if (!response.IsSuccessStatusCode)
            {
                return VehicleLookupResult.ServiceUnavailable(
                    $"The RDW answered with status {(int)response.StatusCode} ({response.StatusCode}).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var records = await JsonSerializer.DeserializeAsync<List<RdwVehicleRecord>>(stream);

            if (records is null || records.Count == 0)
            {
                return VehicleLookupResult.NotFound(plate);
            }

            return VehicleLookupResult.Found(VehicleMapper.ToVehicle(records[0]));
        }
        catch (HttpRequestException ex)
        {
            return VehicleLookupResult.ServiceUnavailable($"The RDW could not be reached: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return VehicleLookupResult.ServiceUnavailable("The request to the RDW timed out.");
        }
        catch (JsonException)
        {
            return VehicleLookupResult.ServiceUnavailable("The RDW returned data in an unexpected format.");
        }
    }
}
