using Rdw.Core;

namespace Rdw.Core.Tests;

public class LicensePlateNormalizerTests
{
    [Theory]
    [InlineData("AB-123-C", "AB123C")]
    [InlineData(" x 998 zg ", "X998ZG")]
    [InlineData("AB@123", "AB123")]
    [InlineData("AB1", "AB1")]
    public void TryNormalize_AcceptsPossiblePlates(string input, string expected)
    {
        var success = LicensePlateNormalizer.TryNormalize(input, out var normalized);

        Assert.True(success);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--- ---")]
    [InlineData("AB-123-CD")]
    [InlineData("ABCDEFG")]
    public void TryNormalize_RejectsImpossibleInput(string? input)
    {
        var success = LicensePlateNormalizer.TryNormalize(input, out var normalized);

        Assert.False(success);
        Assert.Equal(string.Empty, normalized);
    }
}