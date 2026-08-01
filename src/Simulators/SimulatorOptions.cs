namespace Simulators;

/// <summary>
/// A simulator's start-up configuration, parsed from the command line
/// (<c>--port &lt;n&gt; --format &lt;A|B|C&gt; [--rate &lt;n&gt;] [--seed &lt;n&gt;]</c>). The emit rate is
/// deliberately start-up config rather than a runtime fault: three simulators at the default rate
/// sum to the spec's 500–1000 ticks/s load scenario.
/// </summary>
public sealed record SimulatorOptions
{
    /// <summary>Default emit rate (quotes/second); three simulators at this rate sum to ~500–1000/s.</summary>
    public const int DefaultRate = 250;

    /// <summary>Default instruments each simulator streams; kept small and shared across formats.</summary>
    public static readonly IReadOnlyList<string> DefaultTickers = ["BTC-USD", "ETH-USD", "SOL-USD"];

    /// <summary>TCP port Kestrel binds (the WebSocket feed and <c>/fault</c> share it). 0 = ephemeral.</summary>
    public required int Port { get; init; }

    /// <summary>Wire format identifier, normalized upper-case: "A", "B" or "C".</summary>
    public required string Format { get; init; }

    /// <summary>Quotes emitted per second per connection.</summary>
    public int Rate { get; init; } = DefaultRate;

    public IReadOnlyList<string> Tickers { get; init; } = DefaultTickers;

    /// <summary>RNG seed for the price walk; defaults to a per-process value so simulators differ.</summary>
    public int Seed { get; init; } = Environment.TickCount;

    /// <summary>Parses CLI arguments, throwing <see cref="ArgumentException"/> on anything invalid.</summary>
    public static SimulatorOptions Parse(string[] args)
    {
        int? port = null;
        string? format = null;
        var rate = DefaultRate;
        int? seed = null;

        if (args.Length % 2 != 0)
            throw new ArgumentException("arguments must be --key value pairs");

        for (var i = 0; i < args.Length; i += 2)
        {
            var key = args[i];
            var value = args[i + 1];
            switch (key)
            {
                case "--port": port = ParseInt(key, value); break;
                case "--format": format = value.ToUpperInvariant(); break;
                case "--rate": rate = ParseInt(key, value); break;
                case "--seed": seed = ParseInt(key, value); break;
                default: throw new ArgumentException($"unknown argument '{key}'");
            }
        }

        if (port is null)
            throw new ArgumentException("--port is required");
        if (format is null)
            throw new ArgumentException("--format is required");
        if (format is not ("A" or "B" or "C"))
            throw new ArgumentException($"--format must be A, B or C (got '{format}')");
        if (rate <= 0)
            throw new ArgumentException("--rate must be a positive number of ticks/second");

        var options = new SimulatorOptions { Port = port.Value, Format = format, Rate = rate };
        return seed is null ? options : options with { Seed = seed.Value };
    }

    private static int ParseInt(string key, string value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"{key} must be an integer (got '{value}')");
}
