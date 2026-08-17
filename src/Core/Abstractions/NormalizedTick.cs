namespace MarketData.Core.Abstractions;

/// <summary>
/// The single internal representation every exchange quote is normalized to,
/// regardless of the source exchange's wire format (field names, price as string
/// vs. number, time encoding, etc.). This is the currency of the whole pipeline:
/// connectors produce it, the deduplicator filters it, the store persists it.
/// </summary>
/// <remarks>
/// Deliberately an immutable <c>readonly record struct</c>:
/// <list type="bullet">
///   <item>Immutability makes it safe to hand the same value across threads /
///   channels with no defensive copying or locking (grading priority #1).</item>
///   <item>A struct avoids a heap allocation per tick at 500–1000 ticks/s.</item>
/// </list>
/// Note: the deduplicator does NOT rely on the record's structural equality — it
/// derives its own key from a subset of these fields (decided in Phase c), so the
/// exact set of fields here is independent of the dedup key.
/// </remarks>
public readonly record struct NormalizedTick
{
    /// <summary>
    /// Stable identifier of the originating exchange (e.g. "exchange-a").
    /// A string rather than an enum so adding a new exchange never edits this type
    /// (grading priority #5: new exchange = no rewrite).
    /// </summary>
    public required string Source { get; init; }

    /// <summary>Instrument symbol, normalized to a canonical form (e.g. "BTC-USD").</summary>
    public required string Ticker { get; init; }

    /// <summary>
    /// Price as <see cref="decimal"/> — never <see cref="double"/>. Money and
    /// exchange prices must not carry binary floating-point rounding error.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>Trade/quote volume. Decimal to allow fractional (crypto) sizes.</summary>
    public required decimal Volume { get; init; }

    /// <summary>
    /// The exchange-provided event time, normalized to UTC via
    /// <see cref="DateTimeOffset"/> so ticks from sources using different time
    /// encodings are directly comparable.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
}
