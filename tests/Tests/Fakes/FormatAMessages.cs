using System.Globalization;

namespace MarketData.Tests.Fakes;

/// <summary>Builds Format A JSON frames for tests.</summary>
public static class FormatAMessages
{
    public static string Tick(string symbol, decimal price, decimal size, long tsMillis) =>
        string.Create(CultureInfo.InvariantCulture,
            $$"""{"symbol":"{{symbol}}","price":{{price}},"size":{{size}},"ts":{{tsMillis}}}""");
}
