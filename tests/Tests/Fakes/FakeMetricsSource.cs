using Aggregator.Monitoring;

namespace Trading.Tests.Fakes;

/// <summary>
/// An <see cref="IMetricsSource"/> that returns snapshots on demand, so the stats reporter's cadence
/// and rate maths can be driven deterministically. Each <see cref="Capture"/> stamps the snapshot with
/// the supplied <see cref="TimeProvider"/>'s current time and a running <c>received</c> total that
/// climbs by a fixed step per call — so a known recv/s can be asserted.
/// </summary>
public sealed class FakeMetricsSource : IMetricsSource
{
    private readonly TimeProvider _timeProvider;
    private readonly long _receivedStep;
    private long _received;

    public FakeMetricsSource(TimeProvider timeProvider, long receivedStep)
    {
        _timeProvider = timeProvider;
        _receivedStep = receivedStep;
    }

    /// <summary>Number of times <see cref="Capture"/> has been called.</summary>
    public int CaptureCount { get; private set; }

    public PipelineMetrics Capture()
    {
        CaptureCount++;
        var received = _received;
        _received += _receivedStep;

        return new PipelineMetrics
        {
            Timestamp = _timeProvider.GetUtcNow(),
            Received = received,
            ParseErrors = 0,
            Deduplicated = 0,
            Written = received,
            Dropped = 0,
            TrackedKeys = 0,
            OutboundCount = 0,
            OutboundCapacity = 10_000,
            ConnectionsUp = 1,
            SourceCount = 1,
        };
    }
}
