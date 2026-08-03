using System.Threading.Channels;
using Aggregator.Connectors;
using Aggregator.Parsing;
using Microsoft.Extensions.Logging.Abstractions;
using MarketData.Core.Abstractions;
using MarketData.Tests.Fakes;

namespace MarketData.Tests.Connectors;

public sealed class WebSocketExchangeConnectorTests
{
    private static WebSocketExchangeConnector CreateConnector(
        IWebSocketConnectionFactory factory, TimeSpan? idleTimeout = null)
    {
        var options = new ConnectorOptions
        {
            Uri = new Uri("ws://fake/"),
            IdleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30),
            ChannelCapacity = 1_000,
            BackoffInitial = TimeSpan.FromMilliseconds(1),
            BackoffMax = TimeSpan.FromMilliseconds(5),
            BackoffFactor = 2.0,
        };

        return new WebSocketExchangeConnector(
            "exchange-a", options, factory, new FormatAParser(),
            NullLogger<WebSocketExchangeConnector>.Instance);
    }

    private static async Task<NormalizedTick> ReadOneAsync(ChannelReader<NormalizedTick> reader, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await foreach (var tick in reader.ReadAllAsync(cts.Token))
            return tick;
        throw new InvalidOperationException("channel completed before yielding a tick");
    }

    [Fact]
    public async Task Reconnects_repeatedly_after_drops_and_keeps_publishing()
    {
        // Every attempt delivers one tick then drops -> the only way to collect 3 ticks is
        // for the connector to reconnect at least 3 times.
        var factory = new ScriptedWebSocketFactory(attempt => new FakeWebSocketConnection(
            [FormatAMessages.Tick("BTC-USD", price: 100 + attempt, size: 1, tsMillis: attempt)],
            FakeTermination.Drop));
        var connector = CreateConnector(factory);

        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = connector.RunAsync(guard.Token);

        var ticks = new List<NormalizedTick>();
        await foreach (var tick in connector.Ticks.ReadAllAsync(guard.Token))
        {
            ticks.Add(tick);
            if (ticks.Count == 3) break;
        }

        await guard.CancelAsync();
        await run;

        Assert.Equal(3, ticks.Count);
        Assert.True(factory.Attempts >= 3, $"expected >= 3 reconnect attempts, got {factory.Attempts}");
        Assert.All(ticks, t => Assert.Equal("exchange-a", t.Source));
    }

    [Fact]
    public async Task Idle_timeout_forces_reconnect_on_hung_socket()
    {
        // Each attempt delivers one tick then STALLS (socket alive, no data). Collecting more
        // than one tick can only happen if the idle timeout tears the hung socket down.
        var factory = new ScriptedWebSocketFactory(attempt => new FakeWebSocketConnection(
            [FormatAMessages.Tick("BTC-USD", price: 100, size: 1, tsMillis: attempt)],
            FakeTermination.Stall));
        var connector = CreateConnector(factory, idleTimeout: TimeSpan.FromMilliseconds(150));

        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = connector.RunAsync(guard.Token);

        var ticks = new List<NormalizedTick>();
        await foreach (var tick in connector.Ticks.ReadAllAsync(guard.Token))
        {
            ticks.Add(tick);
            if (ticks.Count == 3) break;
        }

        await guard.CancelAsync();
        await run;

        Assert.True(factory.Attempts >= 3, $"expected idle-driven reconnects, got {factory.Attempts} attempts");
    }

    [Fact]
    public async Task Malformed_frames_are_counted_and_do_not_kill_the_stream()
    {
        // One connection: a garbage frame, then a good tick, then stall (no reconnect needed).
        var factory = new ScriptedWebSocketFactory(_ => new FakeWebSocketConnection(
            ["garbage-not-json", FormatAMessages.Tick("BTC-USD", price: 100, size: 1, tsMillis: 0)],
            FakeTermination.Stall));
        var connector = CreateConnector(factory);

        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = connector.RunAsync(guard.Token);

        var tick = await ReadOneAsync(connector.Ticks, TimeSpan.FromSeconds(5));

        Assert.Equal("BTC-USD", tick.Ticker);
        Assert.Equal(1, connector.ParseErrors);
        Assert.Equal(1, connector.Received);

        await guard.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Cancellation_completes_the_channel()
    {
        var factory = new ScriptedWebSocketFactory(_ => new FakeWebSocketConnection(
            [FormatAMessages.Tick("BTC-USD", price: 100, size: 1, tsMillis: 0)],
            FakeTermination.Stall));
        var connector = CreateConnector(factory);

        using var cts = new CancellationTokenSource();
        var run = connector.RunAsync(cts.Token);

        // Ensure it is up and producing before we cancel.
        _ = await ReadOneAsync(connector.Ticks, TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        await run;

        Assert.True(connector.Ticks.Completion.IsCompleted);
        await connector.Ticks.Completion; // completes without faulting
    }
}
