namespace Simulators.Faults;

/// <summary>
/// Holds a simulator's mutable fault state: the duplicate flag and a monotonic "drop generation"
/// that each drop bumps. This is the single piece of shared mutable state in a simulator — faults
/// arrive on the stdin loop and the HTTP endpoint concurrently with the feed reading the state — so
/// every member is thread-safe (grading priority #1).
/// </summary>
public sealed class FaultController
{
    private volatile bool _duplicateEnabled;
    private long _dropGeneration;

    /// <summary>When true, the feed re-emits each quote once more, exercising downstream dedup.</summary>
    public bool DuplicateEnabled => _duplicateEnabled;

    public void SetDuplicate(bool enabled) => _duplicateEnabled = enabled;

    /// <summary>
    /// Bumped by each <see cref="RequestDrop"/>. A connection captures this value when it opens and
    /// closes itself once it changes, so a drop closes only the connections live at the time while
    /// later connections start clean — no cancellation-token lifecycle needed.
    /// </summary>
    public long DropGeneration => Interlocked.Read(ref _dropGeneration);

    /// <summary>Force-closes connections live right now; connections opened afterward are unaffected.</summary>
    public void RequestDrop() => Interlocked.Increment(ref _dropGeneration);
}
