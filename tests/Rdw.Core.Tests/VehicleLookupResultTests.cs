using Rdw.Core;

namespace Rdw.Core.Tests;

public class VehicleLookupResultTests
{
    [Fact]
    public void Found_ContainsVehicleAndPlate()
    {
        var vehicle = new Vehicle { LicensePlate = "X998ZG" };

        var result = VehicleLookupResult.Found(vehicle);

        Assert.Equal(LookupStatus.Found, result.Status);
        Assert.Equal(vehicle, result.Vehicle);
        Assert.Equal("X998ZG", result.LicensePlate);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Found_NullVehicle_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VehicleLookupResult.Found(null!));
    }

    [Fact]
    public void NotFound_HasNoVehicleButKeepsPlate()
    {
        var result = VehicleLookupResult.NotFound("AB123C");

        Assert.Equal(LookupStatus.NotFound, result.Status);
        Assert.Null(result.Vehicle);
        Assert.Equal("AB123C", result.LicensePlate);
    }

    [Fact]
    public void InvalidInput_HasNoVehicleAndExplains()
    {
        var result = VehicleLookupResult.InvalidInput();

        Assert.Equal(LookupStatus.InvalidInput, result.Status);
        Assert.Null(result.Vehicle);
        Assert.Null(result.LicensePlate);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void ServiceUnavailable_KeepsMessage()
    {
        var result = VehicleLookupResult.ServiceUnavailable("Timeout while calling the RDW.");

        Assert.Equal(LookupStatus.ServiceUnavailable, result.Status);
        Assert.Null(result.Vehicle);
        Assert.Equal("Timeout while calling the RDW.", result.Message);
    }
}