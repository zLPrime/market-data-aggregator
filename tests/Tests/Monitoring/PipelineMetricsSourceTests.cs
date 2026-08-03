using Aggregator.Deduplication;
using Aggregator.Monitoring;
using Aggregator.Persistence;
using Aggregator.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Trading.Core.Abstractions;
using Trading.Tests.Fakes;

namespace Trading.Tests.Monitoring;

/// <summary>
/// The metrics source is the read model behind the stats line. These pin the two things that have real
/// logic: it sums / counts the polymorphic connector counters, and it faithfully reflects the running
/// stages' counters (deduplicated, written) — which also proves the fan-in actually counts duplicates.
/// </summary>
public sealed class PipelineMetricsSourceTests
{
    [Fact]
    public void Capture_sums_connector_counters_and_counts_live_connections()
    {
        var connectors = new IConnectorMetrics[]
        {
            new FakeConnectorMetrics("exchange-a", received: 100, parseErrors: 2, isConnected: true),
            new FakeConnectorMetrics("exchange-b", received: 50, parseErrors: 1, isConnected: false),
            new FakeConnectorMetrics("exchange-c", received: 25, parseErrors: 0, isConnected: true),
        };
        var deduplicator = new Deduplicator(new DeduplicatorOptions(), TimeProvider.System);
        var fanIn = new FanIn(
            connectors.Select(_ => Channel()).ToArray(),
            deduplicator,
            new FanInOptions(),
            NullLogger<FanIn>.Instance);
        var writer = new BatchingTickWriter(
            fanIn.Output, new FakeTickStore(), new BatchingWriterOptions(), NullLogger<BatchingTickWriter>.Instance);

        var source = new PipelineMetricsSource(connectors, fanIn, deduplicator, writer, TimeProvider.System);

        var metrics = source.Capture();

        Assert.Equal(175, metrics.Received);
        Assert.Equal(3, metrics.ParseErrors);
        Assert.Equal(2, metrics.ConnectionsUp);
        Assert.Equal(3, metrics.SourceCount);
        Assert.Equal(0, metrics.Deduplicated);   // nothing has flowed yet
        Assert.Equal(0, metrics.Written);
        Assert.Equal(10_000, metrics.OutboundCapacity); // FanInOptions default
    }

    [Fact]
    public async Task Capture_reflects_deduplicated_and_written_counts()
    {
        var a = new FakeExchangeConnector("exchange-a");
        var deduplicator = new Deduplicator(new DeduplicatorOptions(), TimeProvider.System);
        var fanIn = new FanIn(
            new[] { a.Ticks }, deduplicator, new FanInOptions(), NullLogger<FanIn>.Instance);
        var store = new FakeTickStore();
        var writer = new BatchingTickWriter(
            fanIn.Output,
            store,
            new BatchingWriterOptions { BatchSize = 500, MaxBatchLatency = TimeSpan.FromMilliseconds(50) },
            NullLogger<BatchingTickWriter>.Instance);

        var source = new PipelineMetricsSource(
            new IConnectorMetrics[] { new FakeConnectorMetrics("exchange-a", received: 3, isConnected: true) },
            fanIn, deduplicator, writer, TimeProvider.System);

        using var cts = new CancellationTokenSource();
        var writerTask = writer.RunAsync(cts.Token);
        var fanInTask = fanIn.RunAsync(cts.Token);

        var first = Tick("exchange-a", "BTC-USD", 100);
        await a.EmitAsync(first);
        await a.EmitAsync(first); // exact re-send -> deduplicated
        await a.EmitAsync(Tick("exchange-a", "BTC-USD", 101));

        await WaitUntilAsync(() => store.WrittenTicks.Count == 2, TimeSpan.FromSeconds(5));

        var metrics = source.Capture();
        Assert.Equal(2, metrics.Written);
        Assert.Equal(1, metrics.Deduplicated);

        await cts.CancelAsync();
        await Task.WhenAll(writerTask, fanInTask);
    }

    private static System.Threading.Channels.ChannelReader<NormalizedTick> Channel() =>
        System.Threading.Channels.Channel.CreateUnbounded<NormalizedTick>().Reader;

    private static NormalizedTick Tick(string source, string ticker, decimal price) => new()
    {
        Source = source,
        Ticker = ticker,
        Price = price,
        Volume = 1m,
        Timestamp = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
    };

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
            await Task.Delay(10, cts.Token); // throws on timeout -> the test fails visibly
    }
}
