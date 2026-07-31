namespace Simulators.Quotes;

/// <summary>
/// Emits exchange "Format C": a pipe-delimited, positional (non-JSON) line with a Unix-<em>seconds</em>
/// timestamp, e.g. <c>BTC-USD|42123.5|0.10|1730000000</c>. Being not-JSON at all makes it the
/// sharpest departure from Formats A/B. Consumed by <c>Aggregator.Parsing.FormatCParser</c>.
/// </summary>
public sealed class FormatCFormatter : IQuoteFormatter
{
    public string Format => "C";

    public string Serialize(Quote quote) => throw new NotImplementedException();
}
