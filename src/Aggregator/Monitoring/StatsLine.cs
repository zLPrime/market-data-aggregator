namespace Aggregator.Monitoring;

/// <summary>
/// Formats the once-per-interval console stats line from two consecutive snapshots. Pure and static so
/// the rate maths (recv/s from the delta over elapsed time) and the rendering are unit-testable
/// without a timer.
/// </summary>
public static class StatsLine
{
    public static string Format(PipelineMetrics previous, PipelineMetrics current) =>
        throw new NotImplementedException();
}
