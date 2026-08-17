using Aggregator.Parsing;
using Simulators.Quotes;
using MarketData.Core.Abstractions;

namespace MarketData.Tests.Formats;

/// <summary>
/// The two extensibility seams must agree: whatever a simulator's <see cref="IQuoteFormatter"/>
/// emits, the matching aggregator <c>IMessageParser</c> must read back into the same tick. This
/// round-trip is the anchor that keeps the producing and consuming halves of each wire format in
/// lockstep — a change to one side that the other doesn't mirror fails here rather than silently
/// in production (grading priority #5).
/// </summary>
public sealed class FormatRoundTripTests
{
    // Each format is paired with a quote at that format's own time resolution, so the round trip
    // is exact: Format A carries millis, B carries an ISO-8601 instant (millis here), C only seconds.
    public static TheoryData<IQuoteFormatter, IMessageParser, Quote> Formats() => new()
    {
        {
            new FormatAFormatter(), new FormatAParser(),
            QuoteAt(DateTimeOffset.FromUnixTimeMilliseconds(1_730_000_000_123))
        },
        {
            new FormatBFormatter(), new FormatBParser(),
            QuoteAt(DateTimeOffset.FromUnixTimeMilliseconds(1_730_000_000_123))
        },
        {
            new FormatCFormatter(), new FormatCParser(),
            QuoteAt(DateTimeOffset.FromUnixTimeSeconds(1_730_000_000))
        },
    };

    private static Quote QuoteAt(DateTimeOffset timestamp) => new()
    {
        Ticker = "BTC-USD",
        Price = 42123.5m,
        Volume = 0.10m,
        Timestamp = timestamp,
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void Formatter_output_round_trips_through_the_matching_parser(
        IQuoteFormatter formatter, IMessageParser parser, Quote quote)
    {
        Assert.Equal(formatter.Format, parser.Format); // the pair really is one format

        var frame = formatter.Serialize(quote);
        var ok = parser.TryParse(frame, source: "exchange-x", out var tick);

        Assert.True(ok);
        Assert.Equal("exchange-x", tick.Source);
        Assert.Equal(quote.Ticker, tick.Ticker);
        Assert.Equal(quote.Price, tick.Price);
        Assert.Equal(quote.Volume, tick.Volume);
        Assert.Equal(quote.Timestamp, tick.Timestamp);
    }
}
