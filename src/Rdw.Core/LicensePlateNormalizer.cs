namespace Rdw.Core;

public static class LicensePlateNormalizer
{
    // The only place that defines the plate length. Every plate in dataset
    // m9d7-ebf2 has exactly this many characters. If the RDW changes its plate
    // format, change it here and follow ADR-003 ("When the RDW changes its
    // plate format").
    public const int PlateLength = 6;

    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var lettersAndDigits = input.Where(char.IsLetterOrDigit).ToArray();
        return new string(lettersAndDigits).ToUpperInvariant();
    }

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;

        if (input is null)
        {
            return false;
        }

        var candidate = Normalize(input);

        if (candidate.Length != PlateLength)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}