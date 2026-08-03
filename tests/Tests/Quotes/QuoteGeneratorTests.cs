using Simulators.Quotes;
using MarketData.Tests.Fakes;

namespace MarketData.Tests.Quotes;

public sealed class QuoteGeneratorTests
{
    private static readonly string[] Tickers = ["AAA", "BBB", "CCC"];

    [Fact]
    public void Cycles_through_the_configured_tickers_in_order()
    {
        var generator = new QuoteGenerator(Tickers, TimeProvider.System, seed: 1);

        var seen = new[] { generator.Next(), generator.Next(), generator.Next(), generator.Next() }
            .Select(q => q.Ticker)
            .ToArray();

        Assert.Equal(["AAA", "BBB", "CCC", "AAA"], seen); // round-robin, wraps around
    }

    [Fact]
    public void Stamps_each_quote_with_the_providers_current_time()
    {
        var now = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
        var generator = new QuoteGenerator(Tickers, new MutableTimeProvider(now), seed: 1);

        Assert.Equal(now, generator.Next().Timestamp);
    }

    [Fact]
    public void Is_deterministic_for_a_given_seed()
    {
        // A fixed clock so the timestamp field can't diverge between the two runs — the point here
        // is that the same seed reproduces the same price/volume walk.
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-31T00:00:00Z"));
        var a = new QuoteGenerator(Tickers, clock, seed: 42);
        var b = new QuoteGenerator(Tickers, clock, seed: 42);

        for (var i = 0; i < 50; i++)
            Assert.Equal(a.Next(), b.Next()); // identical sequence -> reproducible tests/repro
    }

    [Fact]
    public void Produces_positive_price_and_volume()
    {
        var generator = new QuoteGenerator(Tickers, TimeProvider.System, seed: 7);

        for (var i = 0; i < 500; i++)
        {
            var quote = generator.Next();
            Assert.True(quote.Price > 0m, $"price was {quote.Price}");
            Assert.True(quote.Volume > 0m, $"volume was {quote.Volume}");
        }
    }

    [Fact]
    public void Requires_at_least_one_ticker()
    {
        Assert.Throws<ArgumentException>(() => new QuoteGenerator([], TimeProvider.System, seed: 1));
    }
}
