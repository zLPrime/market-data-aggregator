namespace Simulators.Quotes;

/// <summary>
/// Emits exchange "Format A": a JSON object with numeric price/size and a Unix-millis timestamp,
/// e.g. <c>{"symbol":"BTC-USD","price":42123.5,"size":0.10,"ts":1730000000000}</c>.
/// Consumed by <c>Aggregator.Parsing.FormatAParser</c>.
/// </summary>
public sealed class FormatAFormatter : IQuoteFormatter
{
    public string Format => "A";

    public string Serialize(Quote quote) => throw new NotImplementedException();
}
