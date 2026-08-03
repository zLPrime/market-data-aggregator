using Aggregator.Persistence;
using MarketData.Core.Abstractions;
using MarketData.Tests.Fixtures;

namespace MarketData.Tests.Persistence;

/// <summary>
/// Verifies the one part of the persistence path a fake can't cover: the real binary-COPY write
/// against PostgreSQL. Uses the shared <see cref="PostgresFixture"/> to stand up a throwaway
/// postgres:17, so the test is self-contained — and skips (rather than fails) when Docker isn't
/// available, keeping <c>dotnet test</c> green on machines without it.
/// </summary>
public sealed class NpgsqlTickStoreIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _db;

    public NpgsqlTickStoreIntegrationTests(PostgresFixture db) => _db = db;

    [SkippableFact]
    public async Task Persists_a_batch_via_binary_copy_and_rows_round_trip()
    {
        Skip.If(_db.DockerUnavailable is not null, $"Docker not available: {_db.DockerUnavailable}");

        var store = new NpgsqlTickStore(_db.DataSource!);
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
        await using var command = _db.DataSource!.CreateCommand("SELECT source, ticker, price, volume, ts FROM ticks");
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
