namespace Simulators.Quotes;

/// <summary>
/// Produces a deterministic stream of <see cref="Quote"/>s: an independent random walk per ticker,
/// round-robining across the configured instruments and stamping each with the current time.
/// </summary>
/// <remarks>
/// Pure and allocation-lean by design — the timing (emit rate) and transport (WebSocket) live in
/// the feed loop, so this stays unit-testable with a seeded RNG and a fake clock. Not thread-safe:
/// each connection's feed loop owns its own instance, so there is no shared mutable state to guard.
/// </remarks>
public sealed class QuoteGenerator
{
    private readonly string[] _tickers;
    private readonly decimal[] _prices; // current price per ticker — the walk state
    private readonly Random _random;
    private readonly TimeProvider _timeProvider;
    private int _next; // round-robin cursor into _tickers

    public QuoteGenerator(
        IReadOnlyList<string> tickers, TimeProvider timeProvider, int seed, decimal startPrice = 100m)
    {
        if (tickers.Count == 0)
            throw new ArgumentException("at least one ticker is required", nameof(tickers));

        _tickers = [.. tickers];
        _prices = new decimal[_tickers.Length];
        Array.Fill(_prices, startPrice);
        _random = new Random(seed);
        _timeProvider = timeProvider;
    }

    /// <summary>Advances the walk for the next ticker (round-robin) and returns its new quote.</summary>
    public Quote Next()
    {
        var i = _next;
        _next = (_next + 1) % _tickers.Length;

        // Small proportional step (+/-0.5%), clamped so the price can never reach zero.
        var step = (decimal)((_random.NextDouble() - 0.5) * 0.01);
        var price = Math.Max(0.01m, Math.Round(_prices[i] * (1m + step), 2));
        _prices[i] = price;

        // Strictly positive volume in (0, 5].
        var volume = Math.Round((decimal)_random.NextDouble() * 5m, 3) + 0.001m;

        return new Quote
        {
            Ticker = _tickers[i],
            Price = price,
            Volume = volume,
            Timestamp = _timeProvider.GetUtcNow(),
        };
    }
}
