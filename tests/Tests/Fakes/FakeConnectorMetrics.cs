using Aggregator.Monitoring;

namespace MarketData.Tests.Fakes;

/// <summary>
/// A hand-set <see cref="IConnectorMetrics"/> for monitoring tests — lets a test declare a source's
/// counters and connection state directly, without running a real connector.
/// </summary>
public sealed class FakeConnectorMetrics : IConnectorMetrics
{
    public FakeConnectorMetrics(string source, long received = 0, long parseErrors = 0, bool isConnected = false)
    {
        Source = source;
        Received = received;
        ParseErrors = parseErrors;
        IsConnected = isConnected;
    }

    public string Source { get; }
    public long Received { get; }
    public long ParseErrors { get; }
    public bool IsConnected { get; }
}
