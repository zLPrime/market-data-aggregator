using MarketData.Core.Abstractions;

namespace MarketData.Tests.Fakes;

/// <summary>
/// Wraps a real <see cref="ITickStore"/> and, while <see cref="Faulted"/> is set, throws on every
/// write as a downed database would — then delegates to the inner store once cleared. Lets an
/// end-to-end test inject a DB outage at the store seam (deterministic, in-process) rather than
/// stopping a container, whose ephemeral port would move on restart; the healthy and recovery
/// writes still hit the real store. The flag is a single <c>volatile bool</c>: the test thread
/// writes it and the writer's single loop thread reads it, so no stronger synchronization is needed.
/// </summary>
public sealed class ToggleableFaultStore : ITickStore
{
    private readonly ITickStore _inner;
    private volatile bool _faulted;

    public ToggleableFaultStore(ITickStore inner) => _inner = inner;

    /// <summary>When true, every <see cref="WriteBatchAsync"/> throws instead of persisting.</summary>
    public bool Faulted
    {
        get => _faulted;
        set => _faulted = value;
    }

    public Task WriteBatchAsync(IReadOnlyList<NormalizedTick> batch, CancellationToken cancellationToken) =>
        _faulted
            ? throw new InvalidOperationException("simulated database outage")
            : _inner.WriteBatchAsync(batch, cancellationToken);
}
