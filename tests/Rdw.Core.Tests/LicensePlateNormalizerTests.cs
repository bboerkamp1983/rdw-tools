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

    [Theory]
    [InlineData("AB-123-C", "AB123C")]
    [InlineData(" x 998 zg ", "X998ZG")]
    [InlineData("ab@12#3c", "AB123C")]
    [InlineData("RM-04-80", "RM0480")]
    public void TryNormalize_AcceptsPossiblePlates(string input, string expected)
    {
        var success = LicensePlateNormalizer.TryNormalize(input, out var normalized);

        Assert.True(success);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void TryNormalize_FollowsConfiguredPlateLength()
    {
        var exact = new string('A', LicensePlateNormalizer.PlateLength);
        var shorter = new string('A', LicensePlateNormalizer.PlateLength - 1);
        var longer = new string('A', LicensePlateNormalizer.PlateLength + 1);

        Assert.True(LicensePlateNormalizer.TryNormalize(exact, out _));
        Assert.False(LicensePlateNormalizer.TryNormalize(shorter, out _));
        Assert.False(LicensePlateNormalizer.TryNormalize(longer, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--- ---")]
    [InlineData("A")]
    [InlineData("AB1")]
    [InlineData("AB12")]
    [InlineData("AB@123")]
    [InlineData("AB-123-CD")]
    [InlineData("ABCDEFG")]
    public void TryNormalize_RejectsImpossibleInput(string? input)
    {
        var success = LicensePlateNormalizer.TryNormalize(input, out var normalized);

        Assert.False(success);
        Assert.Equal(string.Empty, normalized);
    }
}