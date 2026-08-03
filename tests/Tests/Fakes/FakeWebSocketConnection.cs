using MarketData.Core.Abstractions;

namespace MarketData.Tests.Fakes;

/// <summary>How a fake connection behaves once its scripted messages are exhausted.</summary>
public enum FakeTermination
{
    /// <summary>Throw, simulating a dropped connection.</summary>
    Drop,

    /// <summary>Block until cancelled, simulating a hung socket (triggers idle-timeout).</summary>
    Stall,
}

/// <summary>
/// A scripted <see cref="IWebSocketConnection"/>: yields a fixed list of messages, then
/// either drops or stalls. Lets the connector's reconnect/idle logic be driven
/// deterministically with no real network.
/// </summary>
public sealed class FakeWebSocketConnection : IWebSocketConnection
{
    private readonly Queue<string> _messages;
    private readonly FakeTermination _termination;

    public FakeWebSocketConnection(IEnumerable<string> messages, FakeTermination termination)
    {
        _messages = new Queue<string>(messages);
        _termination = termination;
    }

    public bool Disposed { get; private set; }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async ValueTask<string> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_messages.Count > 0)
            return _messages.Dequeue();

        return _termination switch
        {
            FakeTermination.Drop => throw new IOException("simulated drop"),
            FakeTermination.Stall => await StallForeverAsync(cancellationToken),
            _ => throw new InvalidOperationException(),
        };
    }

    private static async ValueTask<string> StallForeverAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("unreachable");
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
