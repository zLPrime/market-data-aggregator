using Aggregator.Connectors;
using Aggregator.Parsing;
using Microsoft.Extensions.Logging.Abstractions;
using MarketData.Core.Abstractions;
using MarketData.Tests.Fakes;

namespace MarketData.Tests.Connectors;

/// <summary>
/// End-to-end validation against a real loopback WebSocket "exchange" — the Phase b
/// acceptance criterion (a single fake exchange) exercising the actual
/// <see cref="ClientWebSocketConnection"/> transport, not a mocked seam.
/// </summary>
public sealed class WebSocketExchangeConnectorIntegrationTests
{
    [Fact]
    public async Task Connects_normalizes_and_reconnects_over_real_transport()
    {
        // Server sends 3 frames per connection then closes -> a server-initiated drop.
        await using var server = new FakeExchangeServer(messagesPerConnection: 3);

        var options = new ConnectorOptions
        {
            Uri = server.Uri,
            IdleTimeout = TimeSpan.FromSeconds(5),
            ChannelCapacity = 1_000,
            BackoffInitial = TimeSpan.FromMilliseconds(10),
            BackoffMax = TimeSpan.FromMilliseconds(50),
        };
        var connector = new WebSocketExchangeConnector(
            "exchange-a",
            options,
            new ClientWebSocketConnectionFactory(NullLogger<ClientWebSocketConnection>.Instance),
            new FormatAParser(),
            NullLogger<WebSocketExchangeConnector>.Instance);

        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = connector.RunAsync(guard.Token);

        var ticks = new List<NormalizedTick>();
        await foreach (var tick in connector.Ticks.ReadAllAsync(guard.Token))
        {
            ticks.Add(tick);
            if (ticks.Count == 6) break; // 3 per connection => requires at least one reconnect
        }

        await guard.CancelAsync();
        await run;

        Assert.True(ticks.Count >= 6, $"expected >= 6 ticks across reconnects, got {ticks.Count}");
        Assert.True(server.ConnectionCount >= 2, $"expected >= 2 connections, got {server.ConnectionCount}");
        Assert.All(ticks, t =>
        {
            Assert.Equal("exchange-a", t.Source);
            Assert.Equal("BTC-USD", t.Ticker);
        });
    }
}
