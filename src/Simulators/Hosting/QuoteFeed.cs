using System.Net.WebSockets;
using System.Text;
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
    private long _connectionsAccepted;

    /// <summary>Number of connections this feed has accepted — a reconnect after a drop increments it.</summary>
    public long ConnectionsAccepted => Interlocked.Read(ref _connectionsAccepted);

    /// <summary>Runs the emit loop for one connection until it drops, the app stops, or the client leaves.</summary>
    public async Task RunAsync(WebSocket socket, CancellationToken appStopping)
    {
        Interlocked.Increment(ref _connectionsAccepted);
        var generator = new QuoteGenerator(options.Tickers, timeProvider, options.Seed);

        // Capture the drop generation for THIS connection; when a drop bumps it we end this
        // connection (the aggregator then reconnects), while the next connection starts fresh.
        var dropGeneration = faults.DropGeneration;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / options.Rate));

        try
        {
            while (await timer.WaitForNextTickAsync(appStopping))
            {
                if (faults.DropGeneration != dropGeneration)
                    break; // drop fault — close and let the aggregator reconnect

                var frame = formatter.Serialize(generator.Next());
                await SendAsync(socket, frame, appStopping);
                if (faults.DuplicateEnabled)
                    await SendAsync(socket, frame, appStopping); // spec fault: re-send exercises dedup
            }
        }
        catch (OperationCanceledException) when (appStopping.IsCancellationRequested)
        {
            // App shutdown — fall through to close the connection.
        }
        catch (WebSocketException ex)
        {
            // The client vanished without a close handshake; a real exchange sees this too.
            logger.LogDebug(ex, "client connection lost on format {Format} feed", options.Format);
        }
        finally
        {
            await CloseQuietlyAsync(socket);
        }
    }

    private static Task SendAsync(WebSocket socket, string frame, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(frame);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    // Best-effort close on any exit (drop, shutdown, or client-gone). A clean close is enough for
    // the aggregator to reconnect after a drop — proven by the Phase b connector — so there is no
    // need to distinguish an abrupt abort from a graceful close here.
    private async Task CloseQuietlyAsync(WebSocket socket)
    {
        try
        {
            if (socket.State is WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            logger.LogDebug(ex, "close handshake failed (client already gone)");
        }
    }
}
