using Aggregator.Monitoring;

namespace Trading.Tests.Monitoring;

/// <summary>
/// The stats line is the live dashboard, so its maths must be right: recv/s is a rate derived from the
/// delta between two snapshots, and the fill gauge is derived from outbound occupancy. Pure formatter,
/// so these need no timer.
/// </summary>
public sealed class StatsLineTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Recv_per_second_is_the_received_delta_over_elapsed_time()
    {
        var previous = Snapshot(Start, received: 100);
        var current = Snapshot(Start.AddSeconds(2), received: 900); // +800 over 2s == 400/s

        Assert.Contains("recv/s=400", StatsLine.Format(previous, current));
    }

    [Fact]
    public void Zero_elapsed_reports_zero_rate_without_dividing_by_zero()
    {
        var line = StatsLine.Format(Snapshot(Start, received: 100), Snapshot(Start, received: 500));

        Assert.Contains("recv/s=0", line);
    }

    [Fact]
    public void Fill_percentage_is_derived_from_outbound_occupancy()
    {
        var current = Snapshot(Start.AddSeconds(1), received: 0) with { OutboundCount = 300, OutboundCapacity = 10_000 };

        Assert.Contains("fill=3%", StatsLine.Format(Snapshot(Start, received: 0), current));
    }

    [Fact]
    public void Line_carries_every_gauge()
    {
        var current = new PipelineMetrics
        {
            Timestamp = Start.AddSeconds(1),
            Received = 15320,
            ParseErrors = 4,
            Deduplicated = 210,
            Written = 14980,
            Dropped = 2,
            TrackedKeys = 1483,
            OutboundCount = 0,
            OutboundCapacity = 10_000,
            ConnectionsUp = 2,
            SourceCount = 3,
        };

        var line = StatsLine.Format(Snapshot(Start, received: 0), current);

        Assert.Contains("recv=15320", line);
        Assert.Contains("parse=4", line);
        Assert.Contains("dedup=210", line);
        Assert.Contains("written=14980", line);
        Assert.Contains("dropped=2", line);
        Assert.Contains("keys=1483", line);
        Assert.Contains("conns=2/3", line);
    }

    private static PipelineMetrics Snapshot(DateTimeOffset timestamp, long received) => new()
    {
        Timestamp = timestamp,
        Received = received,
        ParseErrors = 0,
        Deduplicated = 0,
        Written = 0,
        Dropped = 0,
        TrackedKeys = 0,
        OutboundCount = 0,
        OutboundCapacity = 10_000,
        ConnectionsUp = 0,
        SourceCount = 0,
    };
}
