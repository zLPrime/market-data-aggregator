namespace Simulators.Quotes;

/// <summary>
/// A quote as the simulator knows it, before any wire encoding: instrument, price, volume and
/// event time. There is deliberately no "source" field — the simulator <em>is</em> the origin,
/// and the aggregator assigns the source per connector when it parses the frame.
/// </summary>
/// <remarks>
/// An immutable <c>readonly record struct</c> for the same reasons as
/// <see cref="Trading.Core.Abstractions.NormalizedTick"/>: cheap to pass around at 500–1000/s
/// and safe to share without copying. It mirrors that type's fields minus <c>Source</c>, so a
/// formatter here and a parser on the aggregator side round-trip cleanly.
/// </remarks>
public readonly record struct Quote
{
    public required string Ticker { get; init; }
    public required decimal Price { get; init; }
    public required decimal Volume { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}
