using Trading.Core.Abstractions;

namespace Aggregator.Deduplication;

/// <summary>
/// Thread-safe deduplicator that drops exact re-sends of a quote (spec 2.3) while remembering
/// only a bounded, recent window of keys. Safe for many concurrent callers — several
/// connectors are deduplicated on different threads at once (grading priority #4).
/// </summary>
/// <remarks>
/// Not yet implemented — this stub establishes the API the Phase-c tests drive. The tests are
/// red against it on purpose; the real implementation lands in the next commit.
/// </remarks>
public sealed class Deduplicator : IDeduplicator
{
    public Deduplicator(DeduplicatorOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
    }

    public bool TryAccept(in NormalizedTick tick) => throw new NotImplementedException();
}
