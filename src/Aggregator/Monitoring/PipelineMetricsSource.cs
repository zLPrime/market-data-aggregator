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

    public PipelineMetrics Capture()
    {
        long received = 0;
        long parseErrors = 0;
        var connectionsUp = 0;
        foreach (var connector in _connectors)
        {
            received += connector.Received;
            parseErrors += connector.ParseErrors;
            if (connector.IsConnected)
                connectionsUp++;
        }

        return new PipelineMetrics
        {
            Timestamp = _timeProvider.GetUtcNow(),
            Received = received,
            ParseErrors = parseErrors,
            Deduplicated = _fanIn.Deduplicated,
            Written = _writer.Written,
            Dropped = _writer.Dropped,
            TrackedKeys = _deduplicator.TrackedKeys,
            OutboundCount = _fanIn.OutboundCount,
            OutboundCapacity = _fanIn.OutboundCapacity,
            ConnectionsUp = connectionsUp,
            SourceCount = _connectors.Count,
        };
    }
}
