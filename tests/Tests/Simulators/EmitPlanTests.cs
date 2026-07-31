using Simulators.Hosting;

namespace Trading.Tests.Simulators;

public sealed class EmitPlanTests
{
    [Theory]
    [InlineData(50, 20, 1)]    // low rate: one quote per (stretched) tick
    [InlineData(100, 10, 1)]   // at the 10ms floor
    [InlineData(250, 10, 3)]   // above the floor: batch to keep up
    [InlineData(1000, 10, 10)]
    public void Computes_interval_and_batch_size_for_a_rate(int rate, int expectedIntervalMs, int expectedPerTick)
    {
        var plan = EmitPlan.For(rate);

        Assert.Equal(expectedIntervalMs, plan.Interval.TotalMilliseconds, precision: 3);
        Assert.Equal(expectedPerTick, plan.QuotesPerTick);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(250)]
    [InlineData(1000)]
    public void Achieved_rate_is_close_to_the_requested_rate(int rate)
    {
        var plan = EmitPlan.For(rate);

        var achieved = plan.QuotesPerTick / plan.Interval.TotalSeconds;
        Assert.InRange(achieved, rate * 0.5, rate * 1.5);
    }

    [Fact]
    public void Rejects_a_nonpositive_rate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EmitPlan.For(0));
    }
}
