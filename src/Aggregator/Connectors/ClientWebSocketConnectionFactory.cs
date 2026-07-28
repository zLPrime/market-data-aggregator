using Microsoft.Extensions.Logging;
using Trading.Core.Abstractions;

namespace Aggregator.Connectors;

/// <summary>
/// Produces real <see cref="ClientWebSocketConnection"/> instances.
/// </summary>
public sealed class ClientWebSocketConnectionFactory : IWebSocketConnectionFactory
{
    private readonly ILogger<ClientWebSocketConnection> _logger;

    public ClientWebSocketConnectionFactory(ILogger<ClientWebSocketConnection> logger) => _logger = logger;

    public IWebSocketConnection Create() => new ClientWebSocketConnection(_logger);
}
