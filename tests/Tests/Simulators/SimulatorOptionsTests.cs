using Simulators;

namespace Trading.Tests.Simulators;

public sealed class SimulatorOptionsTests
{
    [Fact]
    public void Parses_required_and_optional_arguments()
    {
        var options = SimulatorOptions.Parse(["--port", "9002", "--format", "b", "--rate", "500"]);

        Assert.Equal(9002, options.Port);
        Assert.Equal("B", options.Format); // normalized upper-case
        Assert.Equal(500, options.Rate);
    }

    [Fact]
    public void Defaults_the_rate_when_omitted()
    {
        var options = SimulatorOptions.Parse(["--port", "9001", "--format", "A"]);

        Assert.True(options.Rate > 0);
    }

    [Theory]
    [InlineData("--format", "A")]                                    // missing port
    [InlineData("--port", "9001")]                                   // missing format
    [InlineData("--port", "9001", "--format", "Z")]                  // unknown format
    [InlineData("--port", "9001", "--format", "A", "--rate", "0")]   // non-positive rate
    [InlineData("--port", "9001", "--format", "A", "--bogus", "x")]  // unknown argument
    public void Rejects_invalid_arguments(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => SimulatorOptions.Parse(args));
    }
}
