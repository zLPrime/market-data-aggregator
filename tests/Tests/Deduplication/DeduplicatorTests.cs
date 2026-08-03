using Aggregator.Deduplication;
using MarketData.Core.Abstractions;
using MarketData.Tests.Fakes;

namespace MarketData.Tests.Deduplication;

/// <summary>
/// Single-threaded correctness of the dedup key and the time window. The concurrency
/// guarantees are covered separately in <see cref="DeduplicatorConcurrencyTests"/>.
/// </summary>
public sealed class DeduplicatorTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static NormalizedTick Tick(
        string source = "exchange-a",
        string ticker = "BTC-USD",
        decimal price = 100m,
        decimal volume = 1m,
        long tsMillis = 1_730_000_000_000) => new()
    {
        Source = source,
        Ticker = ticker,
        Price = price,
        Volume = volume,
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsMillis),
    };

    private static Deduplicator NewDeduplicator(TimeSpan? window = null, TimeProvider? clock = null) =>
        new(new DeduplicatorOptions { Window = window ?? TimeSpan.FromMinutes(2) },
            clock ?? new MutableTimeProvider(Epoch));

    [Fact]
    public void First_sighting_is_accepted()
    {
        var dedup = NewDeduplicator();

        Assert.True(dedup.TryAccept(Tick()));
    }

    [Fact]
    public void Exact_resend_within_window_is_rejected()
    {
        var dedup = NewDeduplicator();
        var tick = Tick();

        Assert.True(dedup.TryAccept(tick));
        Assert.False(dedup.TryAccept(tick));
        Assert.False(dedup.TryAccept(tick));
    }

    [Theory]
    [InlineData("exchange-b", "BTC-USD", 100, 1, 1_730_000_000_000)] // different source
    [InlineData("exchange-a", "ETH-USD", 100, 1, 1_730_000_000_000)] // different ticker
    [InlineData("exchange-a", "BTC-USD", 101, 1, 1_730_000_000_000)] // different price
    [InlineData("exchange-a", "BTC-USD", 100, 2, 1_730_000_000_000)] // different volume
    [InlineData("exchange-a", "BTC-USD", 100, 1, 1_730_000_000_001)] // different timestamp
    public void A_tick_differing_in_any_identity_field_is_not_a_duplicate(
        string source, string ticker, decimal price, decimal volume, long tsMillis)
    {
        var dedup = NewDeduplicator();
        Assert.True(dedup.TryAccept(Tick()));

        Assert.True(dedup.TryAccept(Tick(source, ticker, price, volume, tsMillis)));
    }

    [Fact]
    public void Same_quote_from_two_sources_is_not_a_duplicate()
    {
        // Two exchanges independently reporting the same symbol/price/time are distinct
        // observations, not a duplicate — Source is part of the dedup key.
        var dedup = NewDeduplicator();

        Assert.True(dedup.TryAccept(Tick(source: "exchange-a")));
        Assert.True(dedup.TryAccept(Tick(source: "exchange-b")));
    }

    [Fact]
    public void Duplicate_is_still_caught_after_partial_window_advance()
    {
        var clock = new MutableTimeProvider(Epoch);
        var dedup = NewDeduplicator(window: TimeSpan.FromMinutes(2), clock: clock);
        var tick = Tick();

        Assert.True(dedup.TryAccept(tick));
        clock.Advance(TimeSpan.FromMinutes(3)); // one rotation: original moves to the previous generation
        Assert.False(dedup.TryAccept(tick));
    }

    [Fact]
    public void Key_is_forgotten_once_fully_outside_the_window()
    {
        var clock = new MutableTimeProvider(Epoch);
        var dedup = NewDeduplicator(window: TimeSpan.FromMinutes(2), clock: clock);
        var tick = Tick();

        Assert.True(dedup.TryAccept(tick));
        clock.Advance(TimeSpan.FromMinutes(5)); // beyond 2x window: both generations rotated out
        Assert.True(dedup.TryAccept(tick));     // treated as new again (documented window trade-off)
    }

    [Fact]
    public void Tracked_key_count_stays_bounded_across_many_windows()
    {
        // Spec load scenario: memory must not grow without bound. Feed a fresh batch of unique
        // keys every window for many windows; only two generations are ever retained, so the
        // tracked set is capped at ~2 windows' worth no matter how many ticks flow through.
        var window = TimeSpan.FromMinutes(1);
        var clock = new MutableTimeProvider(Epoch);
        var dedup = NewDeduplicator(window: window, clock: clock);
        const int perWindow = 1_000;
        const int windows = 20;
        var uid = 0;

        for (var w = 0; w < windows; w++)
        {
            for (var i = 0; i < perWindow; i++)
                Assert.True(dedup.TryAccept(Tick(ticker: "SYM-" + uid++)));

            Assert.True(dedup.TrackedKeys <= 2 * perWindow,
                $"tracked keys {dedup.TrackedKeys} exceeded the two-generation bound after window {w}");

            clock.Advance(window);
        }
    }
}
