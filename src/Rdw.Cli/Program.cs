using System.Globalization;
using Rdw.Core;

if (args.Length != 2 || !string.Equals(args[0], "kenteken", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: rdw kenteken <license plate>");
    return ExitCodes.UsageError;
}

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
IRdwClient client = new RdwClient(httpClient);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Stop the lookup instead of killing the process, so it can exit with its own code.
    e.Cancel = true;
    cancellation.Cancel();
};

VehicleLookupResult result;
try
{
    result = await client.GetVehicleAsync(args[1], cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return ExitCodes.Cancelled;
}

switch (result.Status)
{
    case LookupStatus.Found:
        PrintVehicle(result.Vehicle!);
        return ExitCodes.Success;

    case LookupStatus.NotFound:
        Console.Error.WriteLine($"No vehicle found for {result.LicensePlate} in the RDW open data.");
        return ExitCodes.NotFound;

    case LookupStatus.InvalidInput:
        Console.Error.WriteLine(result.Message);
        return ExitCodes.InvalidInput;

    default:
        Console.Error.WriteLine($"The RDW could not be queried: {result.Message}");
        return ExitCodes.ServiceUnavailable;
}

static void PrintVehicle(Vehicle vehicle)
{
    Console.WriteLine($"License plate : {vehicle.LicensePlate}");
    Console.WriteLine($"Make          : {ShowText(vehicle.Make)}");
    Console.WriteLine($"Trade name    : {ShowText(vehicle.TradeName)}");
    Console.WriteLine($"Vehicle type  : {ShowText(vehicle.VehicleType)}");
    Console.WriteLine($"Color         : {ShowText(vehicle.PrimaryColor)}");
    Console.WriteLine($"Body type     : {ShowText(vehicle.BodyType)}");
    Console.WriteLine($"Empty mass    : {ShowMass(vehicle.EmptyMassKg)}");
    Console.WriteLine($"Max mass      : {ShowMass(vehicle.MaxPermittedMassKg)}");
    Console.WriteLine($"First admitted: {ShowDate(vehicle.FirstAdmissionDate)}");
    Console.WriteLine($"APK expires   : {ShowDate(vehicle.ApkExpiryDate)}");
    Console.WriteLine($"Exported      : {ShowYesNo(vehicle.IsExported)}");
}

static string ShowText(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

static string ShowMass(int? value) => value is null ? "-" : $"{value} kg";

static string ShowYesNo(bool? value) => value switch
{
    true => "yes",
    false => "no",
    null => "-",
};

static string ShowDate(DateOnly? value) =>
    value is null ? "-" : value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

internal static class ExitCodes
{
    public const int Success = 0;
    public const int NotFound = 1;
    public const int InvalidInput = 2;
    public const int ServiceUnavailable = 3;
    public const int UsageError = 64;
    public const int Cancelled = 130;
}
