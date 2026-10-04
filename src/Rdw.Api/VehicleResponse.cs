using Rdw.Core;

namespace Rdw.Api;

// Public contract of the API (ADR-004). Kept separate from Core.Vehicle so
// that renaming inside Core does not silently change the API.
public sealed record VehicleResponse(
    string LicensePlate,
    string? Make,
    string? TradeName,
    string? VehicleType,
    string? PrimaryColor,
    string? BodyType,
    int? EmptyMassKg,
    int? MaxPermittedMassKg,
    DateOnly? FirstAdmissionDate,
    DateOnly? ApkExpiryDate,
    bool? IsExported)
{
    public static VehicleResponse From(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        return new VehicleResponse(
            vehicle.LicensePlate,
            vehicle.Make,
            vehicle.TradeName,
            vehicle.VehicleType,
            vehicle.PrimaryColor,
            vehicle.BodyType,
            vehicle.EmptyMassKg,
            vehicle.MaxPermittedMassKg,
            vehicle.FirstAdmissionDate,
            vehicle.ApkExpiryDate,
            vehicle.IsExported);
    }
}
