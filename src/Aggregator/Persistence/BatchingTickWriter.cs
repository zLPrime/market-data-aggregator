using System.Threading.Channels;
using Aggregator.Connectors;
using Microsoft.Extensions.Logging;
using Trading.Core.Abstractions;

namespace Aggregator.Persistence;

/// <summary>
/// Drains the outbound tick stream, groups ticks into batches (size or time trigger), and
/// persists each batch via an <see cref="ITickStore"/>. Owns the write-failure policy so the
/// low-level store can stay a dumb "persist one batch, throw on failure" adapter (SOLID).
/// </summary>
public sealed class BatchingTickWriter
{
    private readonly ChannelReader<NormalizedTick> _reader;
    private readonly ITickStore _store;
    private readonly BatchingWriterOptions _options;
    private readonly ILogger<BatchingTickWriter> _logger;

    private long _written;
    private long _dropped;

    public BatchingTickWriter(
        ChannelReader<NormalizedTick> reader,
        ITickStore store,
        BatchingWriterOptions options,
        ILogger<BatchingTickWriter> logger)
    {
        _reader = reader;
        _store = store;
        _options = options;
        _logger = logger;
    }

    /// <summary>Ticks durably written to the store.</summary>
    public long Written => Interlocked.Read(ref _written);

    /// <summary>Ticks dropped after write retries were exhausted (never silently — always counted).</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Drains the reader until the channel completes or cancellation, flushing a batch whenever
    /// it fills (<see cref="BatchingWriterOptions.BatchSize"/>) or ages past
    /// <see cref="BatchingWriterOptions.MaxBatchLatency"/>. Single consumer of the reader, so the
    /// batch buffer needs no synchronization.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "batching writer starting (batchSize={BatchSize}, maxLatency={MaxLatencyMs} ms)",
            _options.BatchSize, (int)_options.MaxBatchLatency.TotalMilliseconds);

        var batch = new List<NormalizedTick>(_options.BatchSize);
        try
        {
            // Outer wait has no time trigger: with an empty batch there is nothing to flush, so
            // we block until the first tick of the next batch arrives (or the channel completes).
            while (await _reader.WaitToReadAsync(cancellationToken))
            {
                await FillBatchAsync(batch, cancellationToken);
                if (batch.Count > 0)
                {
                    await FlushAsync(batch, cancellationToken);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Hard stop: the in-flight partial batch is abandoned by design. The graceful drain
            // (Phase f) instead completes the channel, so the loop above flushes it and exits here.
        }
        finally
        {
            _logger.LogInformation(
                "batching writer stopped. written={Written} dropped={Dropped}", Written, Dropped);
        }
    }

    /// <summary>
    /// Accumulates ticks into <paramref name="batch"/> until it reaches the size trigger or the
    /// time trigger fires. The time trigger is a per-batch linked CTS armed once the first tick is
    /// buffered — the same armed/disarmed pattern the connector uses for idle detection.
    /// </summary>
    private async Task FillBatchAsync(List<NormalizedTick> batch, CancellationToken cancellationToken)
    {
        using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var armed = false;

        try
        {
            while (batch.Count < _options.BatchSize && await _reader.WaitToReadAsync(flushCts.Token))
            {
                while (batch.Count < _options.BatchSize && _reader.TryRead(out var tick))
                    batch.Add(tick);

                if (!armed && batch.Count > 0)
                {
                    flushCts.CancelAfter(_options.MaxBatchLatency); // start the clock on this batch
                    armed = true;
                }
            }
        }
        catch (OperationCanceledException) when (flushCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Time trigger fired: flush whatever is buffered so far.
        }
    }

    /// <summary>
    /// Persists one batch, retrying a failed write with capped exponential backoff. Transient
    /// failures cost only latency (and backpressure, since we stop draining while retrying); a
    /// sustained outage exhausts the attempts and the batch is counted as dropped — a deliberate
    /// bounded-loss decision (spec 2.4), never a swallowed exception.
    /// </summary>
    private async Task FlushAsync(IReadOnlyList<NormalizedTick> batch, CancellationToken cancellationToken)
    {
        ExponentialBackoff? backoff = null;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _store.WriteBatchAsync(batch, cancellationToken);
                Interlocked.Add(ref _written, batch.Count);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // shutdown — let RunAsync unwind.
            }
            catch (Exception ex) when (attempt < _options.MaxWriteAttempts)
            {
                backoff ??= new ExponentialBackoff(
                    _options.RetryBackoffInitial, _options.RetryBackoffMax, _options.RetryBackoffFactor);
                _logger.LogWarning(ex,
                    "batch write failed (attempt {Attempt}/{Max}), retrying", attempt, _options.MaxWriteAttempts);
                await Task.Delay(backoff.NextDelay(), cancellationToken);
            }
            catch (Exception ex)
            {
                var dropped = Interlocked.Add(ref _dropped, batch.Count);
                _logger.LogError(ex,
                    "batch write failed after {Max} attempts; dropping {Count} ticks (dropped total {Dropped})",
                    _options.MaxWriteAttempts, batch.Count, dropped);
                return;
            }
        }
    }
}
