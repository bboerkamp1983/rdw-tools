using Rdw.Core;

namespace Rdw.Api.Tests;

public sealed class FakeRdwClient : IRdwClient
{
    private readonly VehicleLookupResult _result;

    public FakeRdwClient(VehicleLookupResult result)
    {
        _result = result;
    }

    public List<string?> RequestedPlates { get; } = [];

    public Task<VehicleLookupResult> GetVehicleAsync(
        string? licensePlate,
        CancellationToken cancellationToken = default)
    {
        RequestedPlates.Add(licensePlate);
        return Task.FromResult(_result);
    }
}
