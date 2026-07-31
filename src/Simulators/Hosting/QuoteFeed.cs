using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Simulators.Faults;
using Simulators.Quotes;

namespace Simulators.Hosting;

/// <summary>
/// Drives one accepted WebSocket connection: generates quotes at the configured rate, serializes
/// them with the connection's <see cref="IQuoteFormatter"/>, and applies the live fault state
/// (duplicate re-send, drop). Stateless across connections apart from a connection counter, so a
/// single instance safely serves every connection concurrently.
/// </summary>
public sealed class QuoteFeed(
    IQuoteFormatter formatter,
    FaultController faults,
    SimulatorOptions options,
    TimeProvider timeProvider,
    ILogger<QuoteFeed> logger)
{
    /// <summary>Number of connections this feed has accepted — a reconnect after a drop increments it.</summary>
    public long ConnectionsAccepted => throw new NotImplementedException();

    /// <summary>Runs the emit loop for one connection until it drops, the app stops, or the client leaves.</summary>
    public Task RunAsync(WebSocket socket, CancellationToken appStopping)
    {
        _ = (formatter, faults, options, timeProvider, logger, socket, appStopping);
        throw new NotImplementedException();
    }
}
