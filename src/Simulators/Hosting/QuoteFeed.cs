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
    private int _connectionSeq; // gives each connection a distinct RNG seed

    /// <summary>Number of connections this feed has accepted — a reconnect after a drop increments it.</summary>
    public long ConnectionsAccepted => Interlocked.Read(ref _connectionsAccepted);

    /// <summary>Runs the emit loop for one connection until it drops, the app stops, or the client leaves.</summary>
    public async Task RunAsync(WebSocket socket, CancellationToken appStopping)
    {
        Interlocked.Increment(ref _connectionsAccepted);
        var generator = new QuoteGenerator(options.Tickers, timeProvider, NextSeed());

        // Capture the current drop signal for THIS connection: a later drop cancels the token
        // captured here (closing this connection) while the next connection captures a fresh one.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(appStopping, faults.DropToken);
        var token = linked.Token;
        var plan = EmitPlan.For(options.Rate);
        using var timer = new PeriodicTimer(plan.Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                for (var i = 0; i < plan.QuotesPerTick; i++)
                {
                    var frame = formatter.Serialize(generator.Next());
                    await SendAsync(socket, frame, token);
                    if (faults.DuplicateEnabled)
                        await SendAsync(socket, frame, token); // spec fault: re-send exercises downstream dedup
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A drop fault or app shutdown — fall through to end the connection.
        }
        catch (WebSocketException ex)
        {
            // The client vanished without a close handshake; a real exchange sees this too.
            logger.LogDebug(ex, "client connection lost on format {Format} feed", options.Format);
        }
        finally
        {
            await EndConnectionAsync(socket, appStopping);
        }
    }

    private int NextSeed() => unchecked(options.Seed + Interlocked.Increment(ref _connectionSeq));

    private static Task SendAsync(WebSocket socket, string frame, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(frame);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private async Task EndConnectionAsync(WebSocket socket, CancellationToken appStopping)
    {
        if (appStopping.IsCancellationRequested)
        {
            // Graceful shutdown: attempt a clean close handshake, best-effort.
            try
            {
                if (socket.State is WebSocketState.Open)
                    await socket.CloseAsync(
                        WebSocketCloseStatus.EndpointUnavailable, "simulator stopping", CancellationToken.None);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                logger.LogDebug(ex, "clean close failed during shutdown (client already gone)");
            }
        }
        else
        {
            // Drop fault (or the client already left): abort so the aggregator sees an abrupt drop
            // and exercises its reconnect path. Abort() is synchronous and never throws.
            socket.Abort();
        }
    }
}
