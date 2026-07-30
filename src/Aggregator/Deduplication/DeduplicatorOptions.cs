namespace Aggregator.Deduplication;

/// <summary>
/// Tuning knobs for <see cref="Deduplicator"/>. Immutable.
/// </summary>
public sealed class DeduplicatorOptions
{
    /// <summary>
    /// Minimum time a tick is remembered so an exact re-send is caught; generational eviction
    /// means actual retention is 1x–2x this. Sized to exceed how long a reconnect takes, since
    /// the duplicates to catch are quotes re-sent right after a reconnect (spec 2.3). Larger =
    /// catches later re-sends at the cost of memory.
    /// </summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(2);
}
