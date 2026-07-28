using System.Net.WebSockets;

namespace Aggregator.Connectors;

/// <summary>
/// Thrown by <see cref="ClientWebSocketConnection"/> when the peer sends a Close frame,
/// so the connector treats it as a drop and reconnects.
/// </summary>
public sealed class WebSocketClosedException : Exception
{
    public WebSocketCloseStatus? CloseStatus { get; }

    public WebSocketClosedException(WebSocketCloseStatus? closeStatus, string? description)
        : base($"WebSocket closed by peer: {closeStatus} {description}".TrimEnd())
        => CloseStatus = closeStatus;
}
