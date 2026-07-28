using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace Trading.Tests.Fakes;

/// <summary>
/// A minimal real WebSocket "exchange" over loopback (built-in <see cref="HttpListener"/>,
/// no extra dependencies). Each accepted connection is sent a fixed number of Format A
/// frames and then closed — exercising the real transport plus server-initiated drops so
/// the connector's reconnect path runs end-to-end. Clients are handled one at a time, which
/// matches the connector's one-connection-at-a-time behaviour and keeps the helper free of
/// untracked background tasks.
/// </summary>
public sealed class FakeExchangeServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly int _messagesPerConnection;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private int _connectionCount;

    public FakeExchangeServer(int messagesPerConnection)
    {
        _messagesPerConnection = messagesPerConnection;
        var port = GetFreeLoopbackPort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        Uri = new Uri($"ws://localhost:{port}/");
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>The <c>ws://</c> endpoint the connector should dial.</summary>
    public Uri Uri { get; }

    /// <summary>Number of client connections accepted so far (i.e. reconnects + 1).</summary>
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                // Dispose() calls _listener.Stop(), which unblocks this await by throwing —
                // caught below. Avoids leaving an orphaned GetContextAsync with an
                // unobserved exception.
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }

            await HandleClientAsync(context, cancellationToken);
        }
    }

    private async Task HandleClientAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
        Interlocked.Increment(ref _connectionCount);
        var socket = wsContext.WebSocket;
        try
        {
            for (var i = 0; i < _messagesPerConnection; i++)
            {
                var json = FormatAMessages.Tick("BTC-USD", price: 100 + i, size: 1, tsMillis: i);
                var bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }

            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Server is shutting down mid-send; nothing to do.
        }
        finally
        {
            socket.Dispose();
        }
    }

    private static int GetFreeLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Surface unexpected accept-loop failures to the test rather than hide them.
            throw;
        }
        finally
        {
            _listener.Close();
            _cts.Dispose();
        }
    }
}
