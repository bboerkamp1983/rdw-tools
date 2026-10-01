using System.Globalization;

namespace Rdw.Core;

public static class RdwValueParser
{
    private const string DateFormat = "yyyyMMdd";

    public static int? ParseInt(string? input)
    {
        if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }

    public static DateOnly? ParseDate(string? input)
    {
        if (DateOnly.TryParseExact(input, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return value;
        }

        return null;
    }
}