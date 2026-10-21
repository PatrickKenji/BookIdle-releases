using IdleDeck.Core.Market;

namespace IdleDeck.Core.Tests;

public class NumberParseTests
{
    [Theory]
    [InlineData("250000", 250000)]
    [InlineData("1.000.000", 1000000)]
    [InlineData("1,000", 1000)]
    [InlineData("1 000", 1000)]
    [InlineData("1.500,50", 1500)]
    [InlineData("1.000.000,00", 1000000)]
    [InlineData("1,5", 1)]
    [InlineData("0,5", 0)]
    [InlineData("10.5", 10)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void Coins_reads_integers_with_or_without_separators_and_drops_decimals(string text, long expected)
    {
        Assert.Equal(expected, NumberParse.Coins(text));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.0000")]
    [InlineData("12.34.56")]
    [InlineData("1e6")]
    [InlineData("99999999999999999999")]
    public void Coins_refuses_anything_ambiguous_or_invalid(string text)
    {
        Assert.Null(NumberParse.Coins(text));
    }

    [Fact]
    public void A_null_text_is_zero()
    {
        Assert.Equal(0, NumberParse.Coins(null));
    }
}
