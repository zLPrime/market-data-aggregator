using Aggregator.Parsing;

namespace MarketData.Tests.Parsing;

public sealed class FormatBParserTests
{
    private readonly FormatBParser _parser = new();

    [Fact]
    public void Parses_wellformed_tick()
    {
        var raw = """{"s":"BTC-USD","p":"42123.5","v":"0.10","t":"2026-07-31T12:00:00.123+00:00"}""";

        var ok = _parser.TryParse(raw, source: "exchange-b", out var tick);

        Assert.True(ok);
        Assert.Equal("exchange-b", tick.Source);
        Assert.Equal("BTC-USD", tick.Ticker);
        Assert.Equal(42123.5m, tick.Price);
        Assert.Equal(0.10m, tick.Volume);
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 12, 0, 0, 123, TimeSpan.Zero), tick.Timestamp);
    }

    [Fact]
    public void Ignores_nontick_frame_without_throwing()
    {
        var ok = _parser.TryParse("""{"type":"heartbeat"}""", "exchange-b", out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData("""{"s":"BTC-USD","p":"42123.5","v":"0.10"}""")]                        // missing t
    [InlineData("""{"s":"BTC-USD","p":100,"v":"0.10","t":"2026-07-31T12:00:00Z"}""")]   // price not a string
    [InlineData("""{"s":"BTC-USD","p":"oops","v":"0.10","t":"2026-07-31T12:00:00Z"}""")] // price not numeric
    [InlineData("""{"s":"BTC-USD","p":"1","v":"0.10","t":"not-a-time"}""")]              // bad timestamp
    [InlineData("not json at all")]                                                      // malformed json
    public void Throws_FormatException_on_malformed_tick(string raw)
    {
        Assert.Throws<FormatException>(() => _parser.TryParse(raw, "exchange-b", out _));
    }
}
