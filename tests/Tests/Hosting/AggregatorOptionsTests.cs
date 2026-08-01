using Aggregator.Hosting;
using Microsoft.Extensions.Configuration;

namespace Trading.Tests.Hosting;

/// <summary>
/// Pins the config surface Phase f exposes: sources + database bind from the <c>Aggregator</c>
/// section, the <c>TRADING_DB</c> env var overrides the connection string (per the runbook), and
/// malformed configuration fails loudly at start-up rather than producing a silently broken host.
/// </summary>
public sealed class AggregatorOptionsTests
{
    [Fact]
    public void Loads_sources_and_database_from_configuration()
    {
        var configuration = Config(new()
        {
            ["Aggregator:Database:ConnectionString"] = "Host=db;Database=ticks",
            ["Aggregator:DrainTimeout"] = "00:00:05",
            ["Aggregator:Sources:0:Name"] = "exchange-a",
            ["Aggregator:Sources:0:Uri"] = "ws://localhost:9001/",
            ["Aggregator:Sources:0:Format"] = "A",
            ["Aggregator:Sources:1:Name"] = "exchange-b",
            ["Aggregator:Sources:1:Uri"] = "ws://localhost:9002/",
            ["Aggregator:Sources:1:Format"] = "B",
        });

        var options = AggregatorOptions.Load(configuration);

        Assert.Equal("Host=db;Database=ticks", options.Database.ConnectionString);
        Assert.Equal(TimeSpan.FromSeconds(5), options.DrainTimeout);
        Assert.Equal(2, options.Sources.Count);
        Assert.Equal(new Uri("ws://localhost:9002/"), options.Sources[1].Uri);
        Assert.Equal("B", options.Sources[1].Format);
    }

    [Fact]
    public void TRADING_DB_overrides_the_appsettings_connection_string()
    {
        var configuration = Config(new()
        {
            ["Aggregator:Database:ConnectionString"] = "Host=appsettings",
            ["Aggregator:Sources:0:Name"] = "exchange-a",
            ["Aggregator:Sources:0:Uri"] = "ws://localhost:9001/",
            ["Aggregator:Sources:0:Format"] = "A",
            ["TRADING_DB"] = "Host=env-override",
        });

        var options = AggregatorOptions.Load(configuration);

        Assert.Equal("Host=env-override", options.Database.ConnectionString);
    }

    [Fact]
    public void Rejects_configuration_with_no_sources()
    {
        var configuration = Config(new()
        {
            ["Aggregator:Database:ConnectionString"] = "Host=db",
        });

        Assert.Throws<InvalidOperationException>(() => AggregatorOptions.Load(configuration));
    }

    [Fact]
    public void Rejects_an_empty_connection_string()
    {
        var configuration = Config(new()
        {
            ["Aggregator:Sources:0:Name"] = "exchange-a",
            ["Aggregator:Sources:0:Uri"] = "ws://localhost:9001/",
            ["Aggregator:Sources:0:Format"] = "A",
        });

        Assert.Throws<InvalidOperationException>(() => AggregatorOptions.Load(configuration));
    }

    [Fact]
    public void Rejects_a_source_missing_its_uri()
    {
        var configuration = Config(new()
        {
            ["Aggregator:Database:ConnectionString"] = "Host=db",
            ["Aggregator:Sources:0:Name"] = "exchange-a",
            ["Aggregator:Sources:0:Format"] = "A",
        });

        Assert.Throws<InvalidOperationException>(() => AggregatorOptions.Load(configuration));
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
