using Rdw.Core;

namespace Rdw.Core.Tests;

public class LicensePlateNormalizerTests
{
    [Theory]
    [InlineData("AB-123-C", "AB123C")]
    [InlineData("ab-123-c", "AB123C")]
    [InlineData(" x 998 zg ", "X998ZG")]
    [InlineData("X998ZG", "X998ZG")]
    public void Normalize_RemovesSeparatorsAndUppercases(string input, string expected)
    {
        var result = LicensePlateNormalizer.Normalize(input);

        Assert.Equal(expected, result);
    }
}