namespace Rdw.Core;

public sealed record VehicleLookupResult
{
    public LookupStatus Status { get; }
    public Vehicle? Vehicle { get; }
    public string? LicensePlate { get; }
    public string? Message { get; }

    private VehicleLookupResult(
        LookupStatus status,
        Vehicle? vehicle,
        string? licensePlate,
        string? message)
    {
        Status = status;
        Vehicle = vehicle;
        LicensePlate = licensePlate;
        Message = message;
    }

    public static VehicleLookupResult Found(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        return new VehicleLookupResult(LookupStatus.Found, vehicle, vehicle.LicensePlate, null);
    }

    public static VehicleLookupResult NotFound(string licensePlate)
    {
        return new VehicleLookupResult(LookupStatus.NotFound, null, licensePlate, null);
    }

    public static VehicleLookupResult InvalidInput()
    {
        return new VehicleLookupResult(
            LookupStatus.InvalidInput,
            null,
            null,
            "The input can never be a license plate (a plate has exactly six letters or digits, separators not counted).");
    }

    public static VehicleLookupResult ServiceUnavailable(string message)
    {
        return new VehicleLookupResult(LookupStatus.ServiceUnavailable, null, null, message);
    }
}