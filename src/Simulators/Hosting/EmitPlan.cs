namespace Simulators.Hosting;

/// <summary>
/// How the feed paces itself for a target rate: how long to wait between ticks and how many quotes
/// to emit each tick. Emitting a small batch per tick decouples throughput from the OS timer
/// granularity (~15 ms on Windows), which a one-quote-per-tick loop would cap well below the spec's
/// 500–1000 ticks/s load scenario.
/// </summary>
public readonly record struct EmitPlan(TimeSpan Interval, int QuotesPerTick)
{
    // Below ~10ms the OS timer can't tick reliably, so we stop shortening the interval and start
    // widening the batch instead.
    private const double MinIntervalSeconds = 0.01;

    /// <summary>Builds the pacing plan for <paramref name="ratePerSecond"/> quotes/second.</summary>
    public static EmitPlan For(int ratePerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratePerSecond);

        var intervalSeconds = Math.Max(1.0 / ratePerSecond, MinIntervalSeconds);
        var perTick = Math.Max(1, (int)Math.Round(ratePerSecond * intervalSeconds, MidpointRounding.AwayFromZero));
        return new EmitPlan(TimeSpan.FromSeconds(intervalSeconds), perTick);
    }
}
