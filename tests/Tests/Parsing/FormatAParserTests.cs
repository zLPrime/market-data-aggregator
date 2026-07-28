using Aggregator.Parsing;
using Trading.Tests.Fakes;

namespace Trading.Tests.Parsing;

public sealed class FormatAParserTests
{
    private readonly FormatAParser _parser = new();

    [Fact]
    public void Parses_wellformed_tick()
    {
        var raw = FormatAMessages.Tick("BTC-USD", price: 42123.5m, size: 0.10m, tsMillis: 1_730_000_000_000);

        var ok = _parser.TryParse(raw, source: "exchange-a", out var tick);

        Assert.True(ok);
        Assert.Equal("exchange-a", tick.Source);
        Assert.Equal("BTC-USD", tick.Ticker);
        Assert.Equal(42123.5m, tick.Price);
        Assert.Equal(0.10m, tick.Volume);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_730_000_000_000), tick.Timestamp);
    }

    [Fact]
    public void Ignores_nontick_frame_without_throwing()
    {
        var ok = _parser.TryParse("""{"type":"heartbeat"}""", "exchange-a", out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData("""{"symbol":"BTC-USD","price":100,"size":1}""")]            // missing ts
    [InlineData("""{"symbol":"BTC-USD","price":"oops","size":1,"ts":0}""")]  // price wrong type
    [InlineData("not json at all")]                                          // malformed json
    public void Throws_FormatException_on_malformed_tick(string raw)
    {
        Assert.Throws<FormatException>(() => _parser.TryParse(raw, "exchange-a", out _));
    }
}
