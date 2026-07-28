namespace Trading.Core.Abstractions;

/// <summary>
/// Persistence port for normalized ticks. This is the low-level DB adapter: it
/// persists one batch per call and nothing more. The batching policy (size/time
/// triggers) and the write-failure policy (retry / bounded buffer / dropped-counter)
/// live in a separate pipeline stage that drives this port — keeping "how to persist"
/// and "when/whether to persist" as distinct responsibilities (SOLID).
/// </summary>
public interface ITickStore
{
    /// <summary>
    /// Persists <paramref name="batch"/> as a single transactional write
    /// (spec 2.4: batched inserts, per-tick inserts are undesirable).
    /// </summary>
    /// <remarks>
    /// <b>Throws on write failure — it does not swallow.</b> Surfacing the failure is
    /// deliberate: the caller owns the no-silent-loss strategy (retry / buffer / count),
    /// which it cannot apply if this method hides the error (hard rule: no swallowed
    /// exceptions). Implementations must also honour <paramref name="cancellationToken"/>
    /// so a shutdown drain can bound how long a write is awaited.
    /// </remarks>
    Task WriteBatchAsync(IReadOnlyList<NormalizedTick> batch, CancellationToken cancellationToken);
}
