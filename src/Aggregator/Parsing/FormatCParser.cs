using Trading.Core.Abstractions;

namespace Aggregator.Parsing;

/// <summary>
/// Parser for exchange "Format C": a pipe-delimited, positional (non-JSON) line
/// <c>ticker|price|volume|unixSeconds</c>, e.g. <c>BTC-USD|42123.5|0.10|1730000000</c>.
/// A blank frame is treated as an ignorable keep-alive; anything else must parse or it is a
/// counted parse error. The Format C counterpart of <see cref="FormatAParser"/>.
/// </summary>
public sealed class FormatCParser : IMessageParser
{
    public string Format => "C";

    public bool TryParse(string rawMessage, string source, out NormalizedTick tick) =>
        throw new NotImplementedException();
}
