using System.Globalization;
using MarketData.Core.Abstractions;

namespace Aggregator.Parsing;

/// <summary>
/// Parser for exchange "Format C": a pipe-delimited, positional (non-JSON) line
/// <c>ticker|price|volume|unixSeconds</c>, e.g. <c>BTC-USD|42123.5|0.10|1730000000</c>.
/// A blank frame is treated as an ignorable keep-alive; anything else must parse or it is a
/// counted parse error. The Format C counterpart of <see cref="FormatAParser"/>.
/// </summary>
public sealed class FormatCParser : IMessageParser
{
    private const int FieldCount = 4;

    public string Format => "C";

    public bool TryParse(string rawMessage, string source, out NormalizedTick tick)
    {
        tick = default;

        // Blank frame -> ignorable keep-alive, not a tick and not an error.
        if (string.IsNullOrWhiteSpace(rawMessage))
            return false;

        var parts = rawMessage.Split('|');
        if (parts.Length != FieldCount)
            throw new FormatException(
                $"Invalid Format C message (expected {FieldCount} fields): {rawMessage}");

        try
        {
            tick = new NormalizedTick
            {
                Source = source,
                Ticker = parts[0],
                Price = decimal.Parse(parts[1], CultureInfo.InvariantCulture),
                Volume = decimal.Parse(parts[2], CultureInfo.InvariantCulture),
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[3], CultureInfo.InvariantCulture)),
            };
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            // A field was present but unparseable: surface as a parse error to be counted.
            throw new FormatException($"Invalid Format C message: {rawMessage}", ex);
        }
    }
}
