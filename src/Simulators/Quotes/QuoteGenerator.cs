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
    public QuoteGenerator(
        IReadOnlyList<string> tickers, TimeProvider timeProvider, int seed, decimal startPrice = 100m) =>
        throw new NotImplementedException();

    /// <summary>Advances the walk for the next ticker (round-robin) and returns its new quote.</summary>
    public Quote Next() => throw new NotImplementedException();
}
