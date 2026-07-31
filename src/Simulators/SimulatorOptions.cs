namespace Simulators;

/// <summary>
/// A simulator's start-up configuration, parsed from the command line
/// (<c>--port &lt;n&gt; --format &lt;A|B|C&gt; [--rate &lt;n&gt;] [--seed &lt;n&gt;]</c>). The emit rate is
/// deliberately start-up config rather than a runtime fault: three simulators at the default rate
/// sum to the spec's 500–1000 ticks/s load scenario.
/// </summary>
public sealed record SimulatorOptions
{
    /// <summary>Default instruments each simulator streams; kept small and shared across formats.</summary>
    public static readonly IReadOnlyList<string> DefaultTickers = ["BTC-USD", "ETH-USD", "SOL-USD"];

    /// <summary>TCP port Kestrel binds (the WebSocket feed and <c>/fault</c> share it). 0 = ephemeral.</summary>
    public required int Port { get; init; }

    /// <summary>Wire format identifier, normalized upper-case: "A", "B" or "C".</summary>
    public required string Format { get; init; }

    /// <summary>Quotes emitted per second per connection.</summary>
    public int Rate { get; init; } = 250;

    public IReadOnlyList<string> Tickers { get; init; } = DefaultTickers;

    /// <summary>RNG seed for the price walk; defaults to a per-process value so simulators differ.</summary>
    public int Seed { get; init; } = Environment.TickCount;

    /// <summary>Parses CLI arguments, throwing <see cref="ArgumentException"/> on anything invalid.</summary>
    public static SimulatorOptions Parse(string[] args) => throw new NotImplementedException();
}
