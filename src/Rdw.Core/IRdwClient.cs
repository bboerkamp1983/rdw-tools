namespace Rdw.Core;

public interface IRdwClient
{
    /// <summary>Looks up a vehicle by license plate.</summary>
    /// <exception cref="OperationCanceledException">
    /// The caller cancelled <paramref name="cancellationToken"/>. A timeout is not a cancellation:
    /// it returns <see cref="LookupStatus.ServiceUnavailable"/>.
    /// </exception>
    Task<VehicleLookupResult> GetVehicleAsync(
        string? licensePlate,
        CancellationToken cancellationToken = default);
}
