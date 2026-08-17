using MarketData.Core.Abstractions;

namespace MarketData.Tests.Fakes;

/// <summary>
/// Builds one <see cref="IWebSocketConnection"/> per connect attempt from a caller-supplied
/// factory function (given the zero-based attempt index), and records how many attempts the
/// connector has made — the primary signal that reconnection actually happened.
/// </summary>
public sealed class ScriptedWebSocketFactory : IWebSocketConnectionFactory
{
    private readonly Func<int, IWebSocketConnection> _create;
    private int _attempts;

    public ScriptedWebSocketFactory(Func<int, IWebSocketConnection> create) => _create = create;

    public int Attempts => Volatile.Read(ref _attempts);

    public IWebSocketConnection Create()
    {
        var index = Interlocked.Increment(ref _attempts) - 1;
        return _create(index);
    }
}
