using System.Globalization;
using System.Text.Json;
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

    public bool TryParse(string rawMessage, string source, out NormalizedTick tick)
    {
        tick = default;
        try
        {
            using var document = JsonDocument.Parse(rawMessage);
            var root = document.RootElement;

            // Not a tick object (e.g. a heartbeat / control frame) -> ignore, don't count.
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("s", out _))
                return false;

            var ticker = RequireString(root, "s");
            var price = decimal.Parse(RequireString(root, "p"), CultureInfo.InvariantCulture);
            var volume = decimal.Parse(RequireString(root, "v"), CultureInfo.InvariantCulture);
            var timestamp = DateTimeOffset.Parse(
                RequireString(root, "t"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            tick = new NormalizedTick
            {
                Source = source,
                Ticker = ticker,
                Price = price,
                Volume = volume,
                Timestamp = timestamp,
            };
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                   or InvalidOperationException or FormatException or OverflowException)
        {
            // Looked like a tick but was malformed: surface as a parse error to be counted.
            throw new FormatException($"Invalid Format B message: {rawMessage}", ex);
        }
    }

    /// <summary>Reads a required string field, treating a missing key or non-string value as malformed.</summary>
    private static string RequireString(JsonElement root, string name) =>
        root.GetProperty(name).GetString()
        ?? throw new FormatException($"Format B: '{name}' was null");
}
