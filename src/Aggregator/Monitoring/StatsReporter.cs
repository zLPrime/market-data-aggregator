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
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Create the timer before the baseline capture so the timer is armed the moment the first
        // snapshot is taken (the deterministic test advances time only once it observes that capture).
        using var timer = new PeriodicTimer(_interval, _timeProvider);
        var previous = _source.Capture();

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var current = _source.Capture();
                _logger.LogInformation("{Stats}", StatsLine.Format(previous, current));
                previous = current;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
