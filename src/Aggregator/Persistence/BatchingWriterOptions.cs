namespace Aggregator.Persistence;

/// <summary>
/// Tuning knobs for a <see cref="BatchingTickWriter"/>. Immutable.
/// </summary>
public sealed class BatchingWriterOptions
{
    /// <summary>Flush when this many ticks have accumulated (size trigger, spec 2.4).</summary>
    public int BatchSize { get; init; } = 500;

    /// <summary>
    /// Flush a partial batch after this long since its first tick (time trigger), so ticks
    /// are not held indefinitely under light load.
    /// </summary>
    public TimeSpan MaxBatchLatency { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How many times a failing batch write is attempted before the batch is counted as
    /// dropped (spec 2.4: a conscious strategy, not a swallowed exception).
    /// </summary>
    public int MaxWriteAttempts { get; init; } = 5;

    /// <summary>First retry delay after a failed write; grows exponentially from here.</summary>
    public TimeSpan RetryBackoffInitial { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Upper bound on the retry delay.</summary>
    public TimeSpan RetryBackoffMax { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Multiplier applied to the retry-delay ceiling after each failed attempt.</summary>
    public double RetryBackoffFactor { get; init; } = 2.0;
}
