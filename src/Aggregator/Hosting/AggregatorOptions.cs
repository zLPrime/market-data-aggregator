using Microsoft.Extensions.Configuration;

namespace Aggregator.Hosting;

/// <summary>
/// The aggregator's start-up configuration, bound from the <c>Aggregator</c> section of
/// <c>appsettings.json</c>. Deliberately narrow: it carries only what an operator actually varies
/// per deployment (the sources, the database, the shutdown budget). Every other tuning knob keeps
/// its spec-tuned code default rather than becoming speculative config (YAGNI).
/// </summary>
public sealed record AggregatorOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "Aggregator";

    /// <summary>Database connection settings (connection string, overridable by <c>TRADING_DB</c>).</summary>
    public DatabaseOptions Database { get; init; } = new();

    /// <summary>
    /// Upper bound on the graceful-shutdown drain. Wired to the host's shutdown timeout: past it,
    /// the drain is forced to stop and any still-buffered ticks are counted as dropped, never lost
    /// silently.
    /// </summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The exchange sources to connect to; at least one is required.</summary>
    public IReadOnlyList<SourceOptions> Sources { get; init; } = [];

    /// <summary>
    /// Binds and validates the options from <paramref name="configuration"/>. The connection string
    /// is taken from the <c>TRADING_DB</c> environment variable when set, otherwise from
    /// <c>Aggregator:Database:ConnectionString</c> (per the project runbook).
    /// </summary>
    /// <exception cref="InvalidOperationException">The configuration is missing or malformed.</exception>
    public static AggregatorOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(SectionName).Get<AggregatorOptions>() ?? new AggregatorOptions();

        // The TRADING_DB env var (a flat key) takes precedence over the appsettings value, so the
        // connection string can be supplied at deploy time without editing the file (runbook).
        var envConnectionString = configuration["TRADING_DB"];
        var connectionString = !string.IsNullOrWhiteSpace(envConnectionString)
            ? envConnectionString
            : options.Database.ConnectionString;

        options = options with { Database = new DatabaseOptions { ConnectionString = connectionString } };
        Validate(options);
        return options;
    }

    /// <summary>Fails loudly on anything that would produce a silently broken host.</summary>
    private static void Validate(AggregatorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Database.ConnectionString))
            throw new InvalidOperationException(
                "No database connection string configured. Set 'Aggregator:Database:ConnectionString' " +
                "or the TRADING_DB environment variable.");

        if (options.Sources.Count == 0)
            throw new InvalidOperationException("No sources configured under 'Aggregator:Sources'.");

        for (var i = 0; i < options.Sources.Count; i++)
        {
            var source = options.Sources[i];
            if (string.IsNullOrWhiteSpace(source.Name))
                throw new InvalidOperationException($"Source at index {i} is missing 'Name'.");
            if (source.Uri is null)
                throw new InvalidOperationException($"Source '{source.Name}' is missing 'Uri'.");
            if (string.IsNullOrWhiteSpace(source.Format))
                throw new InvalidOperationException($"Source '{source.Name}' is missing 'Format'.");
        }
    }
}

/// <summary>Database connection settings.</summary>
public sealed record DatabaseOptions
{
    /// <summary>Npgsql connection string for the tick store.</summary>
    public string ConnectionString { get; init; } = "";
}

/// <summary>One exchange source: its stable name, WebSocket endpoint, and wire format.</summary>
public sealed record SourceOptions
{
    /// <summary>Stable identifier stamped onto every tick from this source (e.g. "exchange-a").</summary>
    public string Name { get; init; } = "";

    /// <summary>WebSocket endpoint of the exchange feed.</summary>
    public Uri? Uri { get; init; }

    /// <summary>Wire-format identifier selecting the parser ("A", "B" or "C").</summary>
    public string Format { get; init; } = "";
}
