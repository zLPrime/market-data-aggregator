namespace Simulators.Faults;

/// <summary>
/// Holds a simulator's mutable fault state and the signal a "drop" fault uses to force-close live
/// connections. This is the single piece of shared mutable state in a simulator: faults arrive on
/// the stdin loop and the HTTP endpoint concurrently with the WebSocket feed reading the state, so
/// every member is thread-safe (grading priority #1).
/// </summary>
public sealed class FaultController
{
    /// <summary>When true, the feed re-emits each quote once more, exercising downstream dedup.</summary>
    public bool DuplicateEnabled => throw new NotImplementedException();

    public void SetDuplicate(bool enabled) => throw new NotImplementedException();

    /// <summary>Token a connection captures when it opens; fires when a drop is requested.</summary>
    public CancellationToken DropToken => throw new NotImplementedException();

    /// <summary>Force-closes connections live right now; connections opened afterward are unaffected.</summary>
    public void RequestDrop() => throw new NotImplementedException();
}
