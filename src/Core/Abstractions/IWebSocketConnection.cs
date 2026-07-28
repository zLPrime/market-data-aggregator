namespace Trading.Core.Abstractions;

/// <summary>
/// A single, one-shot WebSocket connection abstraction over the raw transport.
/// Exists so the connector's reconnect / backoff / idle logic can be unit-tested
/// deterministically against a fake, with the real <see cref="System.Net.WebSockets.ClientWebSocket"/>
/// isolated behind this port (keep infrastructure isolated).
/// </summary>
/// <remarks>
/// A connection is <b>not reusable</b>: it represents one connect→read→teardown cycle.
/// The connector creates a fresh instance per attempt via
/// <see cref="IWebSocketConnectionFactory"/> and disposes it before reconnecting.
/// </remarks>
public interface IWebSocketConnection : IAsyncDisposable
{
    /// <summary>Opens the connection to <paramref name="uri"/>. Throws on failure.</summary>
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    /// <summary>
    /// Receives the next complete text message, reassembling multi-frame messages.
    /// </summary>
    /// <remarks>
    /// Throws on a closed or faulted socket (e.g. a server Close frame) so the connector
    /// treats it as a drop and reconnects. Honours <paramref name="cancellationToken"/>:
    /// the connector passes an idle-timeout-linked token so a hung socket surfaces as an
    /// <see cref="OperationCanceledException"/>.
    /// </remarks>
    ValueTask<string> ReceiveMessageAsync(CancellationToken cancellationToken);
}
