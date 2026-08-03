using MarketData.Core.Abstractions;

namespace Aggregator.Deduplication;

/// <summary>
/// Quote identity for deduplication: two ticks with an equal key are the same quote re-sent.
/// A distinct type (not <see cref="NormalizedTick"/>'s own equality) so the tick can gain fields
/// without changing what counts as a duplicate, and so the key policy has one home. All five
/// identity fields are used: <c>Source</c> (the same quote from another exchange is a separate
/// observation, not a duplicate) and <c>Price</c>/<c>Volume</c> (two genuine quotes can share a
/// millisecond — dropping one would be silent loss).
/// </summary>
internal readonly record struct TickKey(
    string Source,
    string Ticker,
    decimal Price,
    decimal Volume,
    DateTimeOffset Timestamp)
{
    public static TickKey From(in NormalizedTick tick) =>
        new(tick.Source, tick.Ticker, tick.Price, tick.Volume, tick.Timestamp);
}
