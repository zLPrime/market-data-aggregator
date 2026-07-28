namespace Trading.Core.Abstractions;

/// <summary>
/// Creates a fresh <see cref="IWebSocketConnection"/> per connection attempt.
/// The factory seam keeps the connector free of transport construction details and
/// lets tests inject scripted fake connections to drive reconnect scenarios.
/// </summary>
public interface IWebSocketConnectionFactory
{
    /// <summary>Creates a new, not-yet-connected connection.</summary>
    IWebSocketConnection Create();
}
