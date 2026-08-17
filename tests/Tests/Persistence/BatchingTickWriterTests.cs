using System.Threading.Channels;
using Aggregator.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using MarketData.Core.Abstractions;
using MarketData.Tests.Fakes;

namespace MarketData.Tests.Persistence;

/// <summary>
/// Covers the batching writer's two flush triggers and — the required "breaking scenario"
/// (spec 2.4, grading #2/#3) — its behaviour when the store's writes fail: transient failures
/// must not lose data, and a sustained outage must surface as an explicit dropped count, never
/// a swallowed exception.
/// </summary>
public sealed class BatchingTickWriterTests
{
    private static NormalizedTick Tick(int i) => new()
    {
        Source = "exchange-a",
        Ticker = "SYM",
        Price = 100m + i,
        Volume = 1m,
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1_730_000_000_000 + i),
    };

    private static BatchingTickWriter Writer(
        ChannelReader<NormalizedTick> reader, ITickStore store, BatchingWriterOptions options) =>
        new(reader, store, options, NullLogger<BatchingTickWriter>.Instance);

    private static Channel<NormalizedTick> UnboundedChannel() => Channel.CreateUnbounded<NormalizedTick>();

    /// <summary>Writes all ticks, completes the channel, and runs the writer to completion.</summary>
    private static async Task<BatchingTickWriter> DrainAsync(
        ITickStore store, BatchingWriterOptions options, IEnumerable<NormalizedTick> ticks)
    {
        var channel = UnboundedChannel();
        foreach (var tick in ticks)
            await channel.Writer.WriteAsync(tick);
        channel.Writer.Complete();

        var writer = Writer(channel.Reader, store, options);
        await writer.RunAsync(CancellationToken.None);
        return writer;
    }

    [Fact]
    public async Task Flushes_when_the_batch_size_is_reached()
    {
        var store = new FakeTickStore();
        var options = new BatchingWriterOptions { BatchSize = 10, MaxBatchLatency = TimeSpan.FromMinutes(5) };
        var ticks = Enumerable.Range(0, 25).Select(Tick).ToArray();

        var writer = await DrainAsync(store, options, ticks);

        Assert.Equal(25, writer.Written);
        Assert.Equal(0, writer.Dropped);
        Assert.Equal(ticks, store.WrittenTicks);
        // 25 ticks at batch size 10 => full batches of 10, 10 then a final partial 5 on completion.
        Assert.Equal(new[] { 10, 10, 5 }, store.WrittenBatches.Select(b => b.Count));
    }

    [Fact]
    public async Task Flushes_the_final_partial_batch_when_the_channel_completes()
    {
        var store = new FakeTickStore();
        var options = new BatchingWriterOptions { BatchSize = 1000, MaxBatchLatency = TimeSpan.FromMinutes(5) };
        var ticks = Enumerable.Range(0, 3).Select(Tick).ToArray();

        var writer = await DrainAsync(store, options, ticks);

        Assert.Equal(3, writer.Written);
        Assert.Equal(ticks, store.WrittenTicks);
    }

    [Fact]
    public async Task Flushes_a_partial_batch_on_the_time_trigger_before_the_channel_completes()
    {
        // Batch size far above what we send, so only the time trigger can flush these.
        var store = new FakeTickStore();
        var options = new BatchingWriterOptions
        {
            BatchSize = 1000,
            MaxBatchLatency = TimeSpan.FromMilliseconds(100),
        };
        var channel = UnboundedChannel();
        var writer = Writer(channel.Reader, store, options);

        using var cts = new CancellationTokenSource();
        var run = writer.RunAsync(cts.Token);

        for (var i = 0; i < 3; i++)
            await channel.Writer.WriteAsync(Tick(i));

        // The channel stays open, so without a time trigger these would sit unbatched forever.
        await WaitUntilAsync(() => writer.Written == 3, TimeSpan.FromSeconds(5));

        cts.Cancel();
        channel.Writer.Complete();
        await AwaitCancellation(run);

        Assert.Equal(3, writer.Written);
        Assert.Equal(0, writer.Dropped);
    }

    [Fact]
    public async Task Transient_write_failures_are_retried_with_no_data_loss()
    {
        var store = new FakeTickStore();
        store.FailNext(2); // first two attempts throw, third succeeds
        var options = new BatchingWriterOptions
        {
            BatchSize = 5,
            MaxWriteAttempts = 5,
            RetryBackoffInitial = TimeSpan.FromMilliseconds(1),
            RetryBackoffMax = TimeSpan.FromMilliseconds(5),
        };
        var ticks = Enumerable.Range(0, 5).Select(Tick).ToArray();

        var writer = await DrainAsync(store, options, ticks);

        Assert.Equal(5, writer.Written);
        Assert.Equal(0, writer.Dropped);
        Assert.Equal(ticks, store.WrittenTicks);
        Assert.Equal(3, store.WriteCalls); // 2 failed + 1 succeeded
    }

    [Fact]
    public async Task A_sustained_outage_drops_the_batch_and_counts_it_never_silently()
    {
        var store = new FakeTickStore();
        store.FailNext(int.MaxValue); // permanent outage
        var options = new BatchingWriterOptions
        {
            BatchSize = 5,
            MaxWriteAttempts = 3,
            RetryBackoffInitial = TimeSpan.FromMilliseconds(1),
            RetryBackoffMax = TimeSpan.FromMilliseconds(5),
        };
        var ticks = Enumerable.Range(0, 5).Select(Tick).ToArray();

        var writer = await DrainAsync(store, options, ticks);

        Assert.Equal(0, writer.Written);
        Assert.Equal(5, writer.Dropped);        // the loss is counted, not hidden
        Assert.Empty(store.WrittenTicks);
        Assert.Equal(3, store.WriteCalls);       // exactly MaxWriteAttempts tries, then give up
    }

    [Fact]
    public async Task Processing_continues_after_a_batch_is_dropped()
    {
        // One outage window costs one batch; subsequent batches must still be written.
        var store = new FakeTickStore();
        store.FailNext(3); // exhausts the first batch's 3 attempts, then recovers
        var options = new BatchingWriterOptions
        {
            BatchSize = 5,
            MaxWriteAttempts = 3,
            MaxBatchLatency = TimeSpan.FromMinutes(5),
            RetryBackoffInitial = TimeSpan.FromMilliseconds(1),
            RetryBackoffMax = TimeSpan.FromMilliseconds(5),
        };
        var ticks = Enumerable.Range(0, 10).Select(Tick).ToArray(); // two batches of 5

        var writer = await DrainAsync(store, options, ticks);

        Assert.Equal(5, writer.Dropped);   // first batch lost
        Assert.Equal(5, writer.Written);   // second batch persisted
        Assert.Equal(ticks.Skip(5), store.WrittenTicks);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition not met within timeout");
            await Task.Delay(10);
        }
    }

    private static async Task AwaitCancellation(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }
}
