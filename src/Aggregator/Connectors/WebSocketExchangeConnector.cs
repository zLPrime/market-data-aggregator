using System.Threading.Channels;
using Aggregator.Monitoring;
using Microsoft.Extensions.Logging;
using MarketData.Core.Abstractions;

namespace Aggregator.Connectors;

/// <summary>
/// A live connection to one exchange. Owns a bounded inbound channel and runs a
/// supervise → connect → read → backoff → reconnect loop until cancelled. Transport- and
/// format-agnostic: the socket comes from an <see cref="IWebSocketConnectionFactory"/> and
/// normalization from an <see cref="IMessageParser"/>, so a new exchange needs neither
/// this class changed nor a new loop.
/// </summary>
public sealed class WebSocketExchangeConnector : IExchangeConnector, IConnectorMetrics
{
    private readonly ConnectorOptions _options;
    private readonly IWebSocketConnectionFactory _connectionFactory;
    private readonly IMessageParser _parser;
    private readonly ILogger<WebSocketExchangeConnector> _logger;
    private readonly Channel<NormalizedTick> _channel;
    private readonly ExponentialBackoff _backoff;

    // Counters. Written only by the single read-loop thread, but read by external
    // monitoring (Phase g), so mutated/read via Interlocked for cross-thread visibility.
    private long _received;
    private long _parseErrors;

    // Connection gauge for monitoring. Single writer (the read-loop thread) toggles it around each
    // connection's lifetime; the monitor only reads it, so volatile is sufficient — no lock needed.
    private volatile bool _connected;

    public WebSocketExchangeConnector(
        string source,
        ConnectorOptions options,
        IWebSocketConnectionFactory connectionFactory,
        IMessageParser parser,
        ILogger<WebSocketExchangeConnector> logger)
    {
        Source = source;
        _options = options;
        _connectionFactory = connectionFactory;
        _parser = parser;
        _logger = logger;
        _backoff = new ExponentialBackoff(options.BackoffInitial, options.BackoffMax, options.BackoffFactor);

        // SingleWriter: only this connector's read loop writes.
        // SingleReader: exactly one downstream consumer drains it (the Phase-c fan-in).
        // FullMode.Wait: a full channel backpressures the read loop (and thus the socket)
        // rather than dropping ticks or growing unboundedly.
        _channel = Channel.CreateBounded<NormalizedTick>(new BoundedChannelOptions(options.ChannelCapacity)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public string Source { get; }

    public ChannelReader<NormalizedTick> Ticks => _channel.Reader;

    /// <summary>Ticks successfully parsed and enqueued.</summary>
    public long Received => Interlocked.Read(ref _received);

    /// <summary>Malformed frames that could not be parsed.</summary>
    public long ParseErrors => Interlocked.Read(ref _parseErrors);

    /// <summary>Whether a socket is currently connected (between a successful connect and its drop).</summary>
    public bool IsConnected => _connected;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{Source}] connector starting for {Uri}", Source, _options.Uri);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await RunSingleConnectionAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    break;

                var delay = _backoff.NextDelay();
                _logger.LogInformation("[{Source}] reconnecting in {DelayMs} ms", Source, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            // Completed exactly once, on loop exit only — never on a mid-stream drop.
            // This is what lets a reconnect NOT look like end-of-stream, and lets the
            // downstream drain cleanly on shutdown (Phase f).
            _channel.Writer.Complete();
            _logger.LogInformation(
                "[{Source}] connector stopped. received={Received} parseErrors={ParseErrors}",
                Source, Received, ParseErrors);
        }
    }

    /// <summary>
    /// One connection lifecycle: connect, read until the socket drops or goes idle, then
    /// return so the caller can back off and reconnect. Only cancellation propagates out;
    /// every other failure is logged and swallowed <i>into a reconnect</i>.
    /// </summary>
    private async Task RunSingleConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = _connectionFactory.Create();
        try
        {
            _logger.LogInformation("[{Source}] connecting", Source);
            await connection.ConnectAsync(_options.Uri, cancellationToken);
            _connected = true;
            _logger.LogInformation("[{Source}] connected", Source);

            await ReadLoopAsync(connection, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutdown — let RunAsync exit the loop.
        }
        catch (OperationCanceledException)
        {
            // The idle-timeout-linked token (not the shutdown token) fired: hung socket.
            _logger.LogWarning("[{Source}] idle {IdleMs} ms with no data, reconnecting",
                Source, (int)_options.IdleTimeout.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            // Drop / handshake failure / protocol error — counted-as-logged, then reconnect.
            _logger.LogWarning(ex, "[{Source}] connection error, reconnecting", Source);
        }
        finally
        {
            _connected = false;
            await connection.DisposeAsync();
        }
    }

    private async Task ReadLoopAsync(IWebSocketConnection connection, CancellationToken cancellationToken)
    {
        // One linked CTS per connection. Armed just before each receive and disarmed during
        // processing, so idle is measured strictly as "time waiting for a frame" — downstream
        // backpressure while writing to the channel never counts as idle.
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var backoffReset = false;

        while (true)
        {
            idleCts.CancelAfter(_options.IdleTimeout);                 // arm
            var message = await connection.ReceiveMessageAsync(idleCts.Token);
            idleCts.CancelAfter(Timeout.InfiniteTimeSpan);            // disarm before processing

            // A connection that actually delivers data resets the backoff, so a socket that
            // connects then instantly drops can't fool us into resetting.
            if (!backoffReset)
            {
                _backoff.Reset();
                backoffReset = true;
            }

            NormalizedTick tick;
            try
            {
                if (!_parser.TryParse(message, Source, out tick))
                    continue; // recognized non-tick frame (e.g. heartbeat)
            }
            catch (FormatException ex)
            {
                Interlocked.Increment(ref _parseErrors);
                _logger.LogWarning(ex, "[{Source}] dropping unparseable frame", Source);
                continue;
            }

            // Backpressure point: waits (honouring the shutdown token) if the channel is full.
            await _channel.Writer.WriteAsync(tick, cancellationToken);
            Interlocked.Increment(ref _received);
        }
    }
}
