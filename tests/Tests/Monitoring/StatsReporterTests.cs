using Aggregator.Monitoring;
using Microsoft.Extensions.Time.Testing;
using Trading.Tests.Fakes;

namespace Trading.Tests.Monitoring;

/// <summary>
/// The reporter is a background loop, so it must tick on its interval and stop on cancellation. Driven
/// by a <see cref="FakeTimeProvider"/> so the cadence is deterministic (no real waiting).
/// </summary>
public sealed class StatsReporterTests
{
    [Fact]
    public async Task Emits_a_stats_line_each_interval_with_the_measured_rate()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        var source = new FakeMetricsSource(time, receivedStep: 100); // +100 received per capture
        var logger = new RecordingLogger<StatsReporter>();
        var interval = TimeSpan.FromSeconds(1);
        var reporter = new StatsReporter(source, time, logger, interval);

        using var cts = new CancellationTokenSource();
        var run = reporter.RunAsync(cts.Token);

        // Wait until the loop has taken its baseline snapshot (and therefore armed its timer).
        await WaitUntilAsync(() => source.CaptureCount >= 1, TimeSpan.FromSeconds(5));

        for (var i = 1; i <= 3; i++)
        {
            time.Advance(interval);
            await WaitUntilAsync(() => logger.Messages.Count >= i, TimeSpan.FromSeconds(5));
        }

        await cts.CancelAsync();
        await run;

        Assert.True(logger.Messages.Count >= 3);
        // 100 received per 1s interval -> 100/s on every line.
        Assert.All(logger.Messages, message => Assert.Contains("recv/s=100", message));
    }

    [Fact]
    public async Task Stops_when_cancelled()
    {
        var time = new FakeTimeProvider();
        var reporter = new StatsReporter(
            new FakeMetricsSource(time, receivedStep: 0),
            time,
            new RecordingLogger<StatsReporter>(),
            TimeSpan.FromSeconds(1));

        using var cts = new CancellationTokenSource();
        var run = reporter.RunAsync(cts.Token);

        await cts.CancelAsync();
        await run; // completes without throwing
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
            await Task.Delay(10, cts.Token); // throws on timeout -> the test fails visibly
    }
}
