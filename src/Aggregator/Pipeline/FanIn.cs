using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Trading.Core.Abstractions;

namespace Aggregator.Pipeline;

/// <summary>
/// Merges the N per-connector inbound streams into one outbound stream, running every tick
/// through the shared <see cref="IDeduplicator"/> so only first-seen ticks survive. Owns the
/// bounded outbound channel and exposes only its read side (the same channel-ownership shape the
/// connectors use), so the DB writer downstream is decoupled from how many sources exist.
/// </summary>
public sealed class FanIn
{
    private readonly IReadOnlyList<ChannelReader<NormalizedTick>> _inputs;
    private readonly IDeduplicator _deduplicator;
    private readonly ILogger<FanIn> _logger;
    private readonly Channel<NormalizedTick> _output;
    private readonly int _outputCapacity;

    // Counters. Written by the pump tasks (many), read by external monitoring (Phase g), so via
    // Interlocked for cross-thread visibility and atomic increment under concurrent pumps.
    private long _accepted;
    private long _deduplicated;

    public FanIn(
        IReadOnlyList<ChannelReader<NormalizedTick>> inputs,
        IDeduplicator deduplicator,
        FanInOptions options,
        ILogger<FanIn> logger)
    {
        _inputs = inputs;
        _deduplicator = deduplicator;
        _logger = logger;
        _outputCapacity = options.OutputCapacity;

        _output = Channel.CreateBounded<NormalizedTick>(new BoundedChannelOptions(options.OutputCapacity)
        {
            SingleWriter = false, // one pump task per input writes concurrently
            SingleReader = true,  // the batching DB writer is the sole consumer
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    /// <summary>The merged, deduplicated outbound stream. Completes once all inputs have drained.</summary>
    public ChannelReader<NormalizedTick> Output => _output.Reader;

    /// <summary>Unique ticks forwarded downstream.</summary>
    public long Accepted => Interlocked.Read(ref _accepted);

    /// <summary>Ticks dropped as exact re-sends by the deduplicator.</summary>
    public long Deduplicated => Interlocked.Read(ref _deduplicated);

    /// <summary>Items currently buffered on the outbound belt — the live backpressure signal.</summary>
    public int OutboundCount => _output.Reader.Count;

    /// <summary>Capacity of the outbound belt (its bound).</summary>
    public int OutboundCapacity => _outputCapacity;

    /// <summary>
    /// Runs one pump task per input, each draining its reader through the deduplicator into the
    /// shared output. Returns when every input has completed (clean drain) or on cancellation
    /// (hard stop). The output is completed exactly once, on the way out, so downstream always
    /// sees a definite end-of-stream (the Phase f drain cascade relies on this).
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("fan-in starting over {InputCount} source(s)", _inputs.Count);
        try
        {
            var pumps = new Task[_inputs.Count];
            for (var i = 0; i < _inputs.Count; i++)
                pumps[i] = PumpAsync(_inputs[i], cancellationToken);

            await Task.WhenAll(pumps);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown (hard stop).
        }
        finally
        {
            _output.Writer.Complete();
            _logger.LogInformation("fan-in stopped");
        }
    }

    /// <summary>
    /// Drains one input, forwarding only ticks the deduplicator accepts. The dedup call is the
    /// single point of shared state and is thread-safe, so pumps run concurrently without locking.
    /// </summary>
    private async Task PumpAsync(ChannelReader<NormalizedTick> input, CancellationToken cancellationToken)
    {
        await foreach (var tick in input.ReadAllAsync(cancellationToken))
        {
            if (_deduplicator.TryAccept(tick))
            {
                Interlocked.Increment(ref _accepted);
                await _output.Writer.WriteAsync(tick, cancellationToken);
            }
            else
            {
                Interlocked.Increment(ref _deduplicated);
            }
        }
    }
}
