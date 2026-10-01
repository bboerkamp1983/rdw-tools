namespace Rdw.Core;

public sealed record Vehicle
{
    public required string LicensePlate { get; init; }
    public string? Make { get; init; }
    public string? TradeName { get; init; }
    public string? VehicleType { get; init; }
    public string? PrimaryColor { get; init; }
    public int? EmptyMassKg { get; init; }
    public DateOnly? FirstAdmissionDate { get; init; }
    public DateOnly? ApkExpiryDate { get; init; }
}