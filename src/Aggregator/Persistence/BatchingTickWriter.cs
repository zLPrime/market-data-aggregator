using System.Threading.Channels;
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

    public Task RunAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
