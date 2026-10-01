using Rdw.Core;

namespace Rdw.Core.Tests;

public class RdwValueParserTests
{
    [Theory]
    [InlineData("1657", 1657)]
    [InlineData("0", 0)]
    public void ParseInt_ValidNumber_ReturnsNumber(string input, int expected)
    {
        var result = RdwValueParser.ParseInt(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Geen verstrekking in Open Data")]
    public void ParseInt_NoNumber_ReturnsNull(string? input)
    {
        var result = RdwValueParser.ParseInt(input);

        Assert.Null(result);
    }

    [Fact]
    public void ParseDate_ValidDate_ReturnsDate()
    {
        var result = RdwValueParser.ParseDate("20240320");

        Assert.Equal(new DateOnly(2024, 3, 20), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Geen verstrekking in Open Data")]
    [InlineData("20241341")]
    [InlineData("2024032")]
    public void ParseDate_NoValidDate_ReturnsNull(string? input)
    {
        var result = RdwValueParser.ParseDate(input);

        Assert.Null(result);
    }
}