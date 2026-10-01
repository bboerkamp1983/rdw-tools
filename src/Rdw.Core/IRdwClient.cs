namespace Rdw.Core;

public interface IRdwClient
{
    Task<VehicleLookupResult> GetVehicleAsync(string? licensePlate);
}
