using Aggregator.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;
using Trading.Core.Abstractions;

namespace Trading.Tests.Persistence;

/// <summary>
/// Verifies the one part of the persistence path a fake can't cover: the real binary-COPY write
/// against PostgreSQL. Testcontainers starts a throwaway postgres:17 and tears it down, so the
/// test is self-contained — and skips (rather than fails) when Docker isn't available, keeping
/// <c>dotnet test</c> green on machines without it.
/// </summary>
public sealed class NpgsqlTickStoreIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private NpgsqlDataSource? _dataSource;
    private string? _dockerUnavailable;

    public async Task InitializeAsync()
    {
        try
        {
            // Building the container validates the Docker endpoint, so it must sit inside the
            // guard too — otherwise a missing daemon throws before the test can skip.
            _postgres = new PostgreSqlBuilder("postgres:17").Build();
            await _postgres.StartAsync();
        }
        catch (Exception ex)
        {
            _dockerUnavailable = ex.Message; // no Docker daemon reachable -> skip the test
            return;
        }

        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = _dataSource.CreateCommand(schema);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [SkippableFact]
    public async Task Persists_a_batch_via_binary_copy_and_rows_round_trip()
    {
        Skip.If(_dockerUnavailable is not null, $"Docker not available: {_dockerUnavailable}");

        var store = new NpgsqlTickStore(_dataSource!);
        var batch = new[]
        {
            Tick("exchange-a", "BTC-USD", 65000.50m, 1.25m),
            Tick("exchange-b", "ETH-USD", 3200.00m, 10m),
        };

        await store.WriteBatchAsync(batch, CancellationToken.None);

        var rows = await ReadAllTicksAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r is { Source: "exchange-a", Ticker: "BTC-USD" } && r.Price == 65000.50m && r.Volume == 1.25m);
        Assert.Contains(rows, r => r is { Source: "exchange-b", Ticker: "ETH-USD" } && r.Price == 3200.00m && r.Volume == 10m);
    }

    private static NormalizedTick Tick(string source, string ticker, decimal price, decimal volume) => new()
    {
        Source = source,
        Ticker = ticker,
        Price = price,
        Volume = volume,
        Timestamp = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero),
    };

    private async Task<List<NormalizedTick>> ReadAllTicksAsync()
    {
        var rows = new List<NormalizedTick>();
        await using var command = _dataSource!.CreateCommand("SELECT source, ticker, price, volume, ts FROM ticks");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new NormalizedTick
            {
                Source = reader.GetString(0),
                Ticker = reader.GetString(1),
                Price = reader.GetDecimal(2),
                Volume = reader.GetDecimal(3),
                Timestamp = reader.GetFieldValue<DateTimeOffset>(4),
            });
        }
        return rows;
    }
}
