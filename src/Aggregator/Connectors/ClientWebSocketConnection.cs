using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Trading.Core.Abstractions;

namespace Aggregator.Connectors;

/// <summary>
/// Real <see cref="IWebSocketConnection"/> over <see cref="ClientWebSocket"/>. Reassembles
/// multi-frame messages and decodes them as UTF-8 text. One instance per connect attempt.
/// </summary>
public sealed class ClientWebSocketConnection : IWebSocketConnection
{
    private const int ReceiveBufferSize = 8 * 1024;

    private readonly ClientWebSocket _socket = new();
    private readonly ILogger _logger;

    public ClientWebSocketConnection(ILogger logger) => _logger = logger;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        => _socket.ConnectAsync(uri, cancellationToken);

    public async ValueTask<string> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            var result = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            ThrowIfClosed(result);

            // Fast path: whole message arrived in one frame — no MemoryStream allocation.
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(buffer, 0, result.Count);

            // Slow path: reassemble a multi-frame message.
            using var assembled = new MemoryStream(ReceiveBufferSize * 2);
            assembled.Write(buffer, 0, result.Count);
            do
            {
                result = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                ThrowIfClosed(result);
                assembled.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(assembled.GetBuffer(), 0, (int)assembled.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ThrowIfClosed(ValueWebSocketReceiveResult result)
    {
        if (result.MessageType == WebSocketMessageType.Close)
            throw new WebSocketClosedException(_socket.CloseStatus, _socket.CloseStatusDescription);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", closeTimeout.Token);
            }
        }
        catch (Exception ex)
        {
            // Closing an already-dead/faulted socket legitimately throws; there is nothing
            // to recover, but we log (never silently swallow) per the repo hard rules.
            _logger.LogDebug(ex, "Ignoring error while closing WebSocket during dispose");
        }
        finally
        {
            _socket.Dispose();
        }
    }
}
