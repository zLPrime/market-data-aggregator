using System.Text.Json;
using Trading.Core.Abstractions;

namespace Aggregator.Parsing;

/// <summary>
/// Parser for exchange "Format A": a JSON object with numeric price/size and a Unix-millis
/// timestamp, e.g. <c>{"symbol":"BTC-USD","price":42123.5,"size":0.10,"ts":1730000000000}</c>.
/// </summary>
public sealed class FormatAParser : IMessageParser
{
    public string Format => "A";

    public bool TryParse(string rawMessage, string source, out NormalizedTick tick)
    {
        tick = default;
        try
        {
            using var document = JsonDocument.Parse(rawMessage);
            var root = document.RootElement;

            // Not a tick object (e.g. a heartbeat / control frame) -> ignore, don't count.
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("symbol", out _))
                return false;

            var ticker = root.GetProperty("symbol").GetString()
                         ?? throw new FormatException("Format A: 'symbol' was null");
            var price = root.GetProperty("price").GetDecimal();
            var volume = root.GetProperty("size").GetDecimal();
            var unixMillis = root.GetProperty("ts").GetInt64();

            tick = new NormalizedTick
            {
                Source = source,
                Ticker = ticker,
                Price = price,
                Volume = volume,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMillis),
            };
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                   or InvalidOperationException or FormatException or OverflowException)
        {
            // Looked like a tick but was malformed: surface as a parse error to be counted.
            throw new FormatException($"Invalid Format A message: {rawMessage}", ex);
        }
    }
}
