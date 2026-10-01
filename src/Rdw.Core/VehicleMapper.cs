namespace Rdw.Core;

public static class VehicleMapper
{
    public static Vehicle ToVehicle(RdwVehicleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.Kenteken))
        {
            throw new ArgumentException("The RDW record has no license plate.", nameof(record));
        }

        return new Vehicle
        {
            LicensePlate = record.Kenteken,
            Make = record.Merk,
            TradeName = record.Handelsbenaming,
            VehicleType = record.Voertuigsoort,
            PrimaryColor = record.EersteKleur,
            EmptyMassKg = RdwValueParser.ParseInt(record.MassaLedigVoertuig),
            FirstAdmissionDate = RdwValueParser.ParseDate(record.DatumEersteToelating),
            ApkExpiryDate = RdwValueParser.ParseDate(record.VervaldatumApk),
        };
    }
}