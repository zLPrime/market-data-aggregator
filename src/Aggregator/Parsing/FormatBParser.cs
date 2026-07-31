using Trading.Core.Abstractions;

namespace Aggregator.Parsing;

/// <summary>
/// Parser for exchange "Format B": a JSON object with short field names, price/volume as strings
/// and an ISO-8601 timestamp, e.g.
/// <c>{"s":"BTC-USD","p":"42123.5","v":"0.10","t":"2026-07-31T12:00:00.123+00:00"}</c>.
/// The Format B counterpart of <see cref="FormatAParser"/>; adding it required no connector change.
/// </summary>
public sealed class FormatBParser : IMessageParser
{
    public string Format => "B";

    public bool TryParse(string rawMessage, string source, out NormalizedTick tick) =>
        throw new NotImplementedException();
}
