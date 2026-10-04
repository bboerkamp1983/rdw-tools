using Rdw.Core;

namespace Rdw.Api.Tests;

/// <summary>Waits until the lookup is cancelled, to observe cancellation from the API.</summary>
public sealed class WaitingRdwClient : IRdwClient
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => _started.Task;

    public Task Cancelled => _cancelled.Task;

    public async Task<VehicleLookupResult> GetVehicleAsync(
        string? licensePlate,
        CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() => _cancelled.TrySetResult());
        _started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
    }
}
