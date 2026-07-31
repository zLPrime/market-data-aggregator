namespace Simulators.Quotes;

/// <summary>
/// Emits exchange "Format B": a JSON object with short field names, price/volume as <em>strings</em>,
/// and an ISO-8601 timestamp, e.g.
/// <c>{"s":"BTC-USD","p":"42123.5","v":"0.10","t":"2026-07-31T12:00:00.123+00:00"}</c>.
/// Deliberately differs from Format A in field names, value types and time encoding.
/// Consumed by <c>Aggregator.Parsing.FormatBParser</c>.
/// </summary>
public sealed class FormatBFormatter : IQuoteFormatter
{
    public string Format => "B";

    public string Serialize(Quote quote) => throw new NotImplementedException();
}
