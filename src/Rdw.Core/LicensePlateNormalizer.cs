namespace Rdw.Core;

public static class LicensePlateNormalizer
{
    private const int MaxPlateLength = 6;

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

        if (candidate.Length == 0 || candidate.Length > MaxPlateLength)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}