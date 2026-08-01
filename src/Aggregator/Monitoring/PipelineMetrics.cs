namespace Aggregator.Monitoring;

/// <summary>
/// An immutable, point-in-time snapshot of the whole pipeline's counters. Rates such as recv/s are
/// derived by comparing two snapshots, so the capture <see cref="Timestamp"/> travels with the data.
/// Each field is read from a stage's thread-safe counter, so a snapshot is eventually consistent
/// across stages — adequate for a human-facing dashboard line, and cheap (no locking on the hot path).
/// </summary>
public sealed record PipelineMetrics
{
    /// <summary>When this snapshot was captured (from the monitor's <see cref="TimeProvider"/>).</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Ticks accepted across all connectors.</summary>
    public required long Received { get; init; }

    /// <summary>Malformed frames dropped across all connectors.</summary>
    public required long ParseErrors { get; init; }

    /// <summary>Ticks dropped by the deduplicator as exact re-sends.</summary>
    public required long Deduplicated { get; init; }

    /// <summary>Ticks durably written to the store.</summary>
    public required long Written { get; init; }

    /// <summary>Ticks dropped after write retries were exhausted (counted, never silent).</summary>
    public required long Dropped { get; init; }

    /// <summary>Keys currently retained in the dedup window (bounded-memory gauge).</summary>
    public required int TrackedKeys { get; init; }

    /// <summary>Items currently buffered on the outbound belt.</summary>
    public required int OutboundCount { get; init; }

    /// <summary>Capacity of the outbound belt (its bound).</summary>
    public required int OutboundCapacity { get; init; }

    /// <summary>Connectors currently connected.</summary>
    public required int ConnectionsUp { get; init; }

    /// <summary>Total configured sources.</summary>
    public required int SourceCount { get; init; }

    /// <summary>Outbound-belt fill as a fraction 0..1 — the live backpressure indicator.</summary>
    public double OutboundFill => OutboundCapacity == 0 ? 0 : (double)OutboundCount / OutboundCapacity;
}
