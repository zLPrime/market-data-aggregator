namespace MarketData.Tests.Fakes;

/// <summary>
/// A hand-controlled <see cref="TimeProvider"/> for deterministic tests of time-based
/// behaviour (the deduplicator's window / eviction). Avoids taking a dependency on the
/// external <c>Microsoft.Extensions.Time.Testing</c> package for a single overridden method.
/// </summary>
/// <remarks>
/// <see cref="Advance"/> and <see cref="GetUtcNow"/> are safe to call from multiple threads:
/// the clock is a single <see cref="long"/> mutated with <see cref="Interlocked"/>, so the
/// stress tests can advance time under concurrency without tearing.
/// </remarks>
public sealed class MutableTimeProvider : TimeProvider
{
    private long _utcTicks;

    public MutableTimeProvider(DateTimeOffset start) => _utcTicks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() =>
        new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);
}
