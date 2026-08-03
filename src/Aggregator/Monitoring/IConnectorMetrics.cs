namespace Aggregator.Monitoring;

/// <summary>
/// Read-only monitoring view of one connector's live counters. Deliberately separate from
/// <see cref="MarketData.Core.Abstractions.IExchangeConnector"/> (ISP): the pipeline depends on the
/// connector's <i>behaviour</i>, the monitor on its <i>counters</i>. Because there are many connectors
/// held polymorphically — and a new exchange must stay monitorable without its transport leaking into
/// the monitoring code (grading #5) — this view earns its own interface, unlike the single-instance
/// fan-in / deduplicator / writer, which are read concretely.
/// </summary>
public interface IConnectorMetrics
{
    /// <summary>Stable source identifier stamped on this connector's ticks (e.g. "exchange-a").</summary>
    string Source { get; }

    /// <summary>Ticks successfully parsed and enqueued since start.</summary>
    long Received { get; }

    /// <summary>Malformed frames dropped since start.</summary>
    long ParseErrors { get; }

    /// <summary>Whether the underlying socket is currently connected.</summary>
    bool IsConnected { get; }
}
