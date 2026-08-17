using Aggregator.Parsing;

namespace MarketData.Tests.Parsing;

public sealed class FormatCParserTests
{
    private readonly FormatCParser _parser = new();

    [Fact]
    public void Parses_wellformed_tick()
    {
        var ok = _parser.TryParse("BTC-USD|42123.5|0.10|1730000000", source: "exchange-c", out var tick);

        Assert.True(ok);
        Assert.Equal("exchange-c", tick.Source);
        Assert.Equal("BTC-USD", tick.Ticker);
        Assert.Equal(42123.5m, tick.Price);
        Assert.Equal(0.10m, tick.Volume);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_730_000_000), tick.Timestamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ignores_blank_frame_without_throwing(string raw)
    {
        var ok = _parser.TryParse(raw, "exchange-c", out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData("BTC-USD|42123.5|0.10")]           // too few fields
    [InlineData("BTC-USD|42123.5|0.10|1|extra")]   // too many fields
    [InlineData("BTC-USD|oops|0.10|1730000000")]   // price not numeric
    [InlineData("BTC-USD|42123.5|0.10|notanumber")] // timestamp not an integer
    public void Throws_FormatException_on_malformed_tick(string raw)
    {
        Assert.Throws<FormatException>(() => _parser.TryParse(raw, "exchange-c", out _));
    }
}
