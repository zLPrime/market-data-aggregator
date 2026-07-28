namespace Trading.Core.Abstractions;

/// <summary>
/// Filters out duplicate ticks arriving concurrently from multiple sources
/// (e.g. a simulator re-sending the same quote after a reconnect, per spec 2.3).
/// Implementations MUST be safe for concurrent callers — several connectors are
/// deduplicated on different threads at once (grading priority #4).
/// </summary>
public interface IDeduplicator
{
    /// <summary>
    /// Atomically decides whether <paramref name="tick"/> is new and, if so, records
    /// it as seen — check-and-record is a single operation.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the tick is new (not seen within the dedup window) and should be
    /// forwarded downstream; <c>false</c> if it is a duplicate and must be dropped.
    /// </returns>
    /// <remarks>
    /// The method combines the "have I seen this key?" test and the "remember this key"
    /// mutation into one atomic call <em>by design</em>: exposing them separately would
    /// invite a check-then-act race where two threads both see a key as new. The
    /// interface shape therefore forces correct concurrent usage. The composition of the
    /// dedup key and the window policy are implementation details fixed in Phase c.
    /// <paramref name="tick"/> is passed by <c>in</c> to avoid copying the struct.
    /// </remarks>
    bool TryAccept(in NormalizedTick tick);
}
