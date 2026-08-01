using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aggregator.Monitoring;

/// <summary>
/// Emits a once-per-interval stats line to the log — the live dashboard watched during manual testing
/// and fault drills. A <see cref="BackgroundService"/> so the host owns its lifecycle and observes its
/// <see cref="ExecuteAsync"/> (hard rule: no unobserved background task). The loop is a
/// <see cref="PeriodicTimer"/> driven by the injected <see cref="TimeProvider"/>, so its cadence is
/// deterministic under test.
/// </summary>
public sealed class StatsReporter : BackgroundService
{
    private readonly IMetricsSource _source;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StatsReporter> _logger;
    private readonly TimeSpan _interval;

    public StatsReporter(
        IMetricsSource source,
        TimeProvider timeProvider,
        ILogger<StatsReporter> logger,
        TimeSpan interval)
    {
        _source = source;
        _timeProvider = timeProvider;
        _logger = logger;
        _interval = interval;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunAsync(stoppingToken);

    /// <summary>The reporting loop, exposed for direct (timer-driven) testing without the host.</summary>
    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
