using Aggregator.Persistence;
using Aggregator.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trading.Core.Abstractions;

namespace Aggregator.Hosting;

/// <summary>
/// Runs and shuts down the assembled pipeline (connectors → fan-in → batched writer) as a hosted
/// service. Owns the lifecycle only — the concurrency lives inside the stages it drives.
/// </summary>
/// <remarks>
/// Shutdown is a bounded, two-phase drain governed by two separate cancellation sources:
/// <list type="number">
///   <item><b>Stop intake</b> — cancel <c>_intakeCts</c>. Each connector completes its inbound
///   channel on run-exit (never on a socket drop), so this is a clean "no more input" signal that
///   leaves already-buffered ticks readable.</item>
///   <item><b>Drain</b> — <c>_drainCts</c> stays live, so the fan-in drains the completed inbound
///   channels, completes the outbound belt, and the writer flushes its final partial batch. The
///   host cancels the shutdown token after the configured drain timeout; only then is
///   <c>_drainCts</c> cancelled to force a hard stop.</item>
/// </list>
/// A single supervisor task awaits every stage: on the expected cancellation path it just completes,
/// but an unexpected fault is logged and turned into a <see cref="IHostApplicationLifetime.StopApplication"/>
/// call so a dead stage tears the whole pipeline down. That supervisor is stored and awaited on stop —
/// no background task is left unobserved (hard rule).
/// </remarks>
public sealed class AggregatorPipeline : IHostedService, IDisposable
{
    private readonly IReadOnlyList<IExchangeConnector> _connectors;
    private readonly FanIn _fanIn;
    private readonly BatchingTickWriter _writer;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<AggregatorPipeline> _logger;

    private readonly CancellationTokenSource _intakeCts = new();
    private readonly CancellationTokenSource _drainCts = new();

    private Task? _supervisor;

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

    /// <summary>
    /// Starts every stage and the supervisor, then returns — the pipeline runs on its own tasks, not
    /// on the caller's. Stages start downstream-first (writer, then fan-in, then connectors) so a
    /// consumer is always ready before a producer, though the bounded channels make the order safe
    /// either way.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The writer and fan-in run under the drain token (they must keep going through phase 1 of
        // shutdown); the connectors run under the intake token (phase 1 cancels only them).
        var writerTask = _writer.RunAsync(_drainCts.Token);
        var fanInTask = _fanIn.RunAsync(_drainCts.Token);

        var stageTasks = new List<Task>(_connectors.Count + 2) { writerTask, fanInTask };
        foreach (var connector in _connectors)
            stageTasks.Add(connector.RunAsync(_intakeCts.Token));

        _supervisor = SuperviseAsync(stageTasks);

        _logger.LogInformation("aggregator pipeline started over {SourceCount} source(s)", _connectors.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Two-phase drain: stop intake, then await the drain cascade. The host cancels
    /// <paramref name="cancellationToken"/> after its shutdown timeout; if that fires before the
    /// cascade finishes, the drain is forced to stop.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_supervisor is null)
            return; // never started

        _logger.LogInformation("shutdown: stopping intake and draining buffered ticks to the database");

        // Phase 1: stop intake. Connectors complete their inbound channels; buffered ticks remain.
        await _intakeCts.CancelAsync();

        // If the host's shutdown deadline elapses, force the drain to stop (bounded shutdown).
        await using var registration = cancellationToken.Register(
            static state => ((CancellationTokenSource)state!).Cancel(), _drainCts);

        // Phase 2: await the drain cascade (via the supervisor, which also logs any fault).
        await _supervisor;

        _logger.LogInformation("aggregator pipeline stopped");
    }

    /// <summary>
    /// Awaits every stage. Cancellation (from the drain deadline) is the expected way stages end
    /// during a forced stop, so it is swallowed; any other fault means a stage died unexpectedly and
    /// the whole host is asked to stop rather than limp along with an unobserved exception.
    /// </summary>
    private async Task SuperviseAsync(IReadOnlyList<Task> stageTasks)
    {
        try
        {
            await Task.WhenAll(stageTasks);
        }
        catch (OperationCanceledException) when (_drainCts.IsCancellationRequested)
        {
            // Forced hard stop after the drain deadline — expected.
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "aggregator pipeline stage faulted; requesting application stop");
            _lifetime.StopApplication();
        }
    }

    public void Dispose()
    {
        _intakeCts.Dispose();
        _drainCts.Dispose();
    }
}
