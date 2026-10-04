using Rdw.Core;

namespace Rdw.Api;

public static class VehicleEndpoint
{
    public static async Task<IResult> GetVehicleAsync(
        string licensePlate,
        IRdwClient rdwClient,
        CancellationToken cancellationToken)
    {
        // Minimal APIs bind CancellationToken to HttpContext.RequestAborted: a client disconnect cancels the lookup.
        var result = await rdwClient.GetVehicleAsync(licensePlate, cancellationToken);

        return result.Status switch
        {
            LookupStatus.Found => TypedResults.Ok(VehicleResponse.From(result.Vehicle!)),

            LookupStatus.NotFound => TypedResults.Problem(
                title: "Vehicle not found",
                detail: $"No vehicle found for {result.LicensePlate} in the RDW open data.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["licensePlate"] = result.LicensePlate }),

            LookupStatus.InvalidInput => TypedResults.Problem(
                title: "Invalid license plate",
                detail: result.Message,
                statusCode: StatusCodes.Status400BadRequest),

            _ => TypedResults.Problem(
                title: "RDW unavailable",
                detail: result.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }
}
