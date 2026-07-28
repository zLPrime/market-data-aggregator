namespace Aggregator.Connectors;

/// <summary>
/// Exponential backoff with "full jitter" (delay = random in <c>[0, ceiling]</c>, where
/// the ceiling grows <c>initial × factor^attempt</c> capped at <c>max</c>). Full jitter
/// spreads reconnect attempts out to avoid a thundering herd when many connectors drop
/// at once.
/// </summary>
/// <remarks>
/// <b>Not thread-safe by design:</b> a single connector's <c>RunAsync</c> loop is the
/// only caller, so no synchronization is needed. The jitter source is injectable to make
/// the policy deterministically testable.
/// </remarks>
public sealed class ExponentialBackoff
{
    private readonly double _initialMs;
    private readonly double _maxMs;
    private readonly double _factor;
    private readonly Func<double> _nextUnitInterval;
    private int _attempt;

    /// <param name="jitter">
    /// Returns a value in <c>[0,1)</c>. Defaults to <see cref="Random.Shared"/>
    /// (thread-safe). Injected in tests for determinism.
    /// </param>
    public ExponentialBackoff(TimeSpan initial, TimeSpan max, double factor, Func<double>? jitter = null)
    {
        if (initial <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(initial));
        if (max < initial) throw new ArgumentOutOfRangeException(nameof(max));
        if (factor < 1.0) throw new ArgumentOutOfRangeException(nameof(factor));

        _initialMs = initial.TotalMilliseconds;
        _maxMs = max.TotalMilliseconds;
        _factor = factor;
        _nextUnitInterval = jitter ?? Random.Shared.NextDouble;
    }

    /// <summary>The current (pre-jitter) ceiling — exposed for logging/testing.</summary>
    public TimeSpan CurrentCeiling => TimeSpan.FromMilliseconds(CeilingMs());

    /// <summary>Returns the next jittered delay and advances the attempt counter.</summary>
    public TimeSpan NextDelay()
    {
        var delay = _nextUnitInterval() * CeilingMs();
        _attempt++;
        return TimeSpan.FromMilliseconds(delay);
    }

    /// <summary>Resets to the initial delay after a connection proves productive.</summary>
    public void Reset() => _attempt = 0;

    private double CeilingMs() => Math.Min(_initialMs * Math.Pow(_factor, _attempt), _maxMs);
}
