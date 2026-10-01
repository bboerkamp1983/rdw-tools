using System.Text.Json;
using Rdw.Core;

namespace Rdw.Core.Tests;

public class VehicleMapperTests
{
    private const string KiaJson = """
        {
          "kenteken": "X998ZG",
          "voertuigsoort": "Personenauto",
          "merk": "KIA",
          "handelsbenaming": "NIRO",
          "vervaldatum_apk": "20280320",
          "eerste_kleur": "GRIJS",
          "massa_ledig_voertuig": "1657",
          "datum_eerste_toelating": "20240320",
          "wacht_op_keuren": "Geen verstrekking in Open Data"
        }
        """;

    [Fact]
    public void ToVehicle_FromRdwJson_MapsAllFields()
    {
        var record = JsonSerializer.Deserialize<RdwVehicleRecord>(KiaJson);
        Assert.NotNull(record);

        var vehicle = VehicleMapper.ToVehicle(record);

        Assert.Equal("X998ZG", vehicle.LicensePlate);
        Assert.Equal("KIA", vehicle.Make);
        Assert.Equal("NIRO", vehicle.TradeName);
        Assert.Equal("Personenauto", vehicle.VehicleType);
        Assert.Equal("GRIJS", vehicle.PrimaryColor);
        Assert.Equal(1657, vehicle.EmptyMassKg);
        Assert.Equal(new DateOnly(2024, 3, 20), vehicle.FirstAdmissionDate);
        Assert.Equal(new DateOnly(2028, 3, 20), vehicle.ApkExpiryDate);
    }

    [Fact]
    public void ToVehicle_MissingOptionalValues_GivesNulls()
    {
        var record = new RdwVehicleRecord { Kenteken = "AB123C" };

        var vehicle = VehicleMapper.ToVehicle(record);

        Assert.Equal("AB123C", vehicle.LicensePlate);
        Assert.Null(vehicle.Make);
        Assert.Null(vehicle.EmptyMassKg);
        Assert.Null(vehicle.FirstAdmissionDate);
        Assert.Null(vehicle.ApkExpiryDate);
    }

    [Fact]
    public void ToVehicle_MissingLicensePlate_Throws()
    {
        Assert.Throws<ArgumentException>(() => VehicleMapper.ToVehicle(new RdwVehicleRecord()));
    }

    [Fact]
    public void ToVehicle_NullRecord_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VehicleMapper.ToVehicle(null!));
    }
}