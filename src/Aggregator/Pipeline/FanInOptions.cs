namespace Aggregator.Pipeline;

/// <summary>
/// Tuning knobs for the <see cref="FanIn"/> merge stage. Immutable.
/// </summary>
public sealed class FanInOptions
{
    /// <summary>
    /// Capacity of the shared outbound channel. Bounded so a slow DB writer backpressures the
    /// whole pipeline (grading #2) rather than growing memory without limit; its fill level is
    /// the backpressure signal surfaced in Phase g.
    /// </summary>
    public int OutputCapacity { get; init; } = 10_000;
}
