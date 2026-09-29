namespace Rdw.Core;

public static class LicensePlateNormalizer
{
    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var lettersAndDigits = input.Where(char.IsLetterOrDigit).ToArray();
        return new string(lettersAndDigits).ToUpperInvariant();
    }
}