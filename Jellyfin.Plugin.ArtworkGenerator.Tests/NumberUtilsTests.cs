using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the number to words and Roman numeral conversions in <see cref="NumberUtils"/>.
/// </summary>
public class NumberUtilsTests
{
    [Theory]
    [InlineData(0, "ZERO")]
    [InlineData(5, "FIVE")]
    [InlineData(19, "NINETEEN")]
    [InlineData(20, "TWENTY")]
    [InlineData(21, "TWENTY ONE")]
    [InlineData(42, "FORTY TWO")]
    [InlineData(99, "NINETY NINE")]
    [InlineData(100, "100")]
    [InlineData(-5, "MINUS FIVE")]
    public void NumberToWords_Cases(int number, string expected)
    {
        Assert.Equal(expected, NumberUtils.NumberToWords(number));
    }

    [Theory]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(40, "XL")]
    [InlineData(90, "XC")]
    [InlineData(2024, "MMXXIV")]
    [InlineData(1990, "MCMXC")]
    [InlineData(3999, "MMMCMXCIX")]
    [InlineData(0, "0")]
    [InlineData(4000, "4000")]
    public void NumberToRomanNumeral_Cases(int number, string expected)
    {
        Assert.Equal(expected, NumberUtils.NumberToRomanNumeral(number));
    }
}
