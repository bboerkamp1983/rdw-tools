using System.Text.Json.Serialization;

namespace Rdw.Core;

public sealed class RdwVehicleRecord
{
    [JsonPropertyName("kenteken")]
    public string? Kenteken { get; init; }

    [JsonPropertyName("merk")]
    public string? Merk { get; init; }

    [JsonPropertyName("handelsbenaming")]
    public string? Handelsbenaming { get; init; }

    [JsonPropertyName("voertuigsoort")]
    public string? Voertuigsoort { get; init; }

    [JsonPropertyName("eerste_kleur")]
    public string? EersteKleur { get; init; }

    [JsonPropertyName("massa_ledig_voertuig")]
    public string? MassaLedigVoertuig { get; init; }

    [JsonPropertyName("datum_eerste_toelating")]
    public string? DatumEersteToelating { get; init; }

    [JsonPropertyName("vervaldatum_apk")]
    public string? VervaldatumApk { get; init; }
}