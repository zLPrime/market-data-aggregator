using Aggregator.Deduplication;
using Aggregator.Persistence;
using Aggregator.Pipeline;

namespace Aggregator.Monitoring;

/// <summary>
/// Reads every stage's live counters into one <see cref="PipelineMetrics"/> snapshot. Holds the
/// connectors polymorphically (via <see cref="IConnectorMetrics"/>) and the single fan-in /
/// deduplicator / writer concretely — there is exactly one of each, so no interface earns its keep.
/// </summary>
public sealed class PipelineMetricsSource : IMetricsSource
{
    private readonly IReadOnlyList<IConnectorMetrics> _connectors;
    private readonly FanIn _fanIn;
    private readonly Deduplicator _deduplicator;
    private readonly BatchingTickWriter _writer;
    private readonly TimeProvider _timeProvider;

    public PipelineMetricsSource(
        IReadOnlyList<IConnectorMetrics> connectors,
        FanIn fanIn,
        Deduplicator deduplicator,
        BatchingTickWriter writer,
        TimeProvider timeProvider)
    {
        _connectors = connectors;
        _fanIn = fanIn;
        _deduplicator = deduplicator;
        _writer = writer;
        _timeProvider = timeProvider;
    }

    public PipelineMetrics Capture() => throw new NotImplementedException();
}
