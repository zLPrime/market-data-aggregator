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

    public FanIn(
        IReadOnlyList<ChannelReader<NormalizedTick>> inputs,
        IDeduplicator deduplicator,
        FanInOptions options,
        ILogger<FanIn> logger)
    {
        _inputs = inputs;
        _deduplicator = deduplicator;
        _logger = logger;

        _output = Channel.CreateBounded<NormalizedTick>(new BoundedChannelOptions(options.OutputCapacity)
        {
            SingleWriter = false, // one pump task per input writes concurrently
            SingleReader = true,  // the batching DB writer is the sole consumer
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    /// <summary>The merged, deduplicated outbound stream. Completes once all inputs have drained.</summary>
    public ChannelReader<NormalizedTick> Output => _output.Reader;

    public Task RunAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
