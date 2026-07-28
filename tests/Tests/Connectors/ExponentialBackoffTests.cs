using Aggregator.Connectors;

namespace Trading.Tests.Connectors;

public sealed class ExponentialBackoffTests
{
    private static ExponentialBackoff Backoff(double jitter) => new(
        initial: TimeSpan.FromMilliseconds(500),
        max: TimeSpan.FromSeconds(30),
        factor: 2.0,
        jitter: () => jitter);

    [Fact]
    public void Ceiling_grows_exponentially_then_caps()
    {
        // jitter = 1.0 => NextDelay returns exactly the ceiling.
        var backoff = Backoff(jitter: 1.0);

        var delays = Enumerable.Range(0, 8).Select(_ => backoff.NextDelay().TotalMilliseconds).ToArray();

        Assert.Equal(new double[] { 500, 1000, 2000, 4000, 8000, 16000, 30000, 30000 }, delays);
    }

    [Fact]
    public void Full_jitter_keeps_delay_within_zero_and_ceiling()
    {
        var random = new Random(12345);
        var backoff = new ExponentialBackoff(
            TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30), 2.0, random.NextDouble);

        double expectedCeiling = 500;
        for (var i = 0; i < 10; i++)
        {
            var ceiling = Math.Min(expectedCeiling, 30000);
            var delay = backoff.NextDelay().TotalMilliseconds;

            Assert.InRange(delay, 0, ceiling);
            expectedCeiling *= 2;
        }
    }

    [Fact]
    public void Reset_returns_to_initial_ceiling()
    {
        var backoff = Backoff(jitter: 1.0);

        backoff.NextDelay(); // 500
        backoff.NextDelay(); // 1000
        backoff.NextDelay(); // 2000
        backoff.Reset();

        Assert.Equal(500, backoff.NextDelay().TotalMilliseconds);
    }

    [Theory]
    [InlineData(0, 30, 2.0)]      // initial must be > 0
    [InlineData(500, 100, 2.0)]   // max must be >= initial (ms here)
    [InlineData(500, 30000, 0.5)] // factor must be >= 1
    public void Invalid_options_throw(double initialMs, double maxMs, double factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialBackoff(
            TimeSpan.FromMilliseconds(initialMs), TimeSpan.FromMilliseconds(maxMs), factor));
    }
}
