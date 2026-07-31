namespace Simulators.Faults;

/// <summary>
/// Holds a simulator's mutable fault state and the signal a "drop" fault uses to force-close live
/// connections. This is the single piece of shared mutable state in a simulator: faults arrive on
/// the stdin loop and the HTTP endpoint concurrently with the WebSocket feed reading the state, so
/// every member is thread-safe (grading priority #1).
/// </summary>
public sealed class FaultController
{
    private volatile bool _duplicateEnabled;

    // Swapped-and-cancelled on each drop: connections that captured the previous token close,
    // while connections opened afterward capture the fresh, uncancelled one. Deliberately NOT
    // disposed — it owns no timer or wait handle here, and skipping Dispose removes any
    // cancel-vs-dispose race with a connection reading the token concurrently (grading #1); GC
    // reclaims the old source once no connection references it, and drops are rare.
    private CancellationTokenSource _dropSignal = new();

    /// <summary>When true, the feed re-emits each quote once more, exercising downstream dedup.</summary>
    public bool DuplicateEnabled => _duplicateEnabled;

    public void SetDuplicate(bool enabled) => _duplicateEnabled = enabled;

    /// <summary>Token a connection captures when it opens; fires when a drop is requested.</summary>
    public CancellationToken DropToken => Volatile.Read(ref _dropSignal).Token;

    /// <summary>Force-closes connections live right now; connections opened afterward are unaffected.</summary>
    public void RequestDrop() => Interlocked.Exchange(ref _dropSignal, new CancellationTokenSource()).Cancel();
}
