using System.Globalization;

namespace Aggregator.Monitoring;

/// <summary>
/// Formats the once-per-interval console stats line from two consecutive snapshots. Pure and static so
/// the rate maths (recv/s from the delta over elapsed time) and the rendering are unit-testable
/// without a timer.
/// </summary>
public static class StatsLine
{
    public static string Format(PipelineMetrics previous, PipelineMetrics current)
    {
        var elapsedSeconds = (current.Timestamp - previous.Timestamp).TotalSeconds;
        var receivedPerSecond = elapsedSeconds > 0
            ? (current.Received - previous.Received) / elapsedSeconds
            : 0;

        return string.Format(
            CultureInfo.InvariantCulture,
            "stats recv/s={0:F0} recv={1} parse={2} dedup={3} written={4} dropped={5} fill={6:F0}% keys={7} conns={8}/{9}",
            receivedPerSecond,
            current.Received,
            current.ParseErrors,
            current.Deduplicated,
            current.Written,
            current.Dropped,
            current.OutboundFill * 100,
            current.TrackedKeys,
            current.ConnectionsUp,
            current.SourceCount);
    }
}
