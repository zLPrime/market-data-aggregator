using Aggregator.Persistence;
using Aggregator.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trading.Core.Abstractions;

namespace Aggregator.Hosting;

/// <summary>
/// Runs and shuts down the assembled pipeline (connectors → fan-in → batched writer) as a hosted
/// service. Owns the lifecycle only — the concurrency lives inside the stages it drives. Shutdown is
/// a bounded, two-phase drain: stop intake so connectors complete their inbound channels, then let
/// the fan-in and writer drain those channels to the database before the deadline forces a stop.
/// </summary>
public sealed class AggregatorPipeline : IHostedService, IDisposable
{
    private readonly IReadOnlyList<IExchangeConnector> _connectors;
    private readonly FanIn _fanIn;
    private readonly BatchingTickWriter _writer;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<AggregatorPipeline> _logger;

    public AggregatorPipeline(
        IReadOnlyList<IExchangeConnector> connectors,
        FanIn fanIn,
        BatchingTickWriter writer,
        IHostApplicationLifetime lifetime,
        ILogger<AggregatorPipeline> logger)
    {
        _connectors = connectors;
        _fanIn = fanIn;
        _writer = writer;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task StopAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

    public void Dispose() { }
}
