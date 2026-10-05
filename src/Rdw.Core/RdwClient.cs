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

    public async Task<VehicleLookupResult> GetVehicleAsync(
        string? licensePlate,
        CancellationToken cancellationToken = default)
    {
        if (!LicensePlateNormalizer.TryNormalize(licensePlate, out var plate))
        {
            return VehicleLookupResult.InvalidInput();
        }

        var uri = new Uri($"{DatasetUrl}?kenteken={Uri.EscapeDataString(plate)}");

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return VehicleLookupResult.ServiceUnavailable(
                    $"The vehicle data service answered with status {(int)response.StatusCode} ({response.StatusCode}).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var records = await JsonSerializer.DeserializeAsync<List<RdwVehicleRecord>>(stream, cancellationToken: cancellationToken);

            if (records is null || records.Count == 0)
            {
                return VehicleLookupResult.NotFound(plate);
            }

            return VehicleLookupResult.Found(VehicleMapper.ToVehicle(records[0]));
        }
        catch (HttpRequestException ex)
        {
            return VehicleLookupResult.ServiceUnavailable($"The vehicle data service could not be reached: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled without the caller asking for it: the HttpClient timeout expired.
            // A cancellation by the caller is not caught and reaches the caller.
            return VehicleLookupResult.ServiceUnavailable("The request to the vehicle data service timed out.");
        }
        catch (JsonException)
        {
            return VehicleLookupResult.ServiceUnavailable("The vehicle data service returned data in an unexpected format.");
        }
    }
}
