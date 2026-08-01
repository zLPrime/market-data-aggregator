namespace Aggregator.Monitoring;

/// <summary>
/// Captures a coherent snapshot of the pipeline's live counters on demand. The seam the stats
/// reporter depends on, so its cadence and rate maths can be tested against scripted snapshots
/// without spinning up real stages.
/// </summary>
public interface IMetricsSource
{
    PipelineMetrics Capture();
}
