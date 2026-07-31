using System.Collections.Concurrent;
using Trading.Core.Abstractions;

namespace Trading.Tests.Fakes;

/// <summary>
/// A hand-controlled <see cref="ITickStore"/> for testing the batching writer's flush and
/// write-failure behaviour without a real database. The next N calls can be scripted to throw
/// (simulating a DB outage / write error, spec 2.4); successful calls record their batch.
/// </summary>
/// <remarks>
/// Thread-safe: the writer under test drives this from a single loop today, but the counters
/// and recorded batches are concurrency-safe so the fake can't itself hide a race.
/// </remarks>
public sealed class FakeTickStore : ITickStore
{
    private readonly ConcurrentQueue<IReadOnlyList<NormalizedTick>> _writtenBatches = new();
    private int _failuresRemaining;
    private long _writeCalls;

    /// <summary>Every batch this store successfully persisted, in call order.</summary>
    public IReadOnlyList<IReadOnlyList<NormalizedTick>> WrittenBatches => _writtenBatches.ToArray();

    /// <summary>All successfully persisted ticks, flattened.</summary>
    public IReadOnlyList<NormalizedTick> WrittenTicks => _writtenBatches.SelectMany(b => b).ToArray();

    /// <summary>Total number of <see cref="WriteBatchAsync"/> invocations (including failed ones).</summary>
    public long WriteCalls => Interlocked.Read(ref _writeCalls);

    /// <summary>Fail the next <paramref name="count"/> write attempts, then succeed. Use <see cref="int.MaxValue"/> for a permanent outage.</summary>
    public void FailNext(int count) => Interlocked.Exchange(ref _failuresRemaining, count);

    public Task WriteBatchAsync(IReadOnlyList<NormalizedTick> batch, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _writeCalls);
        cancellationToken.ThrowIfCancellationRequested();

        // Atomically claim a failure slot if any remain.
        int remaining;
        do
        {
            remaining = Volatile.Read(ref _failuresRemaining);
            if (remaining <= 0)
                break;
        }
        while (Interlocked.CompareExchange(ref _failuresRemaining, remaining - 1, remaining) != remaining);

        if (remaining > 0)
            return Task.FromException(new InvalidOperationException("simulated write failure"));

        _writtenBatches.Enqueue(batch.ToArray());
        return Task.CompletedTask;
    }
}
