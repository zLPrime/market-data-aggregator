using Npgsql;
using NpgsqlTypes;
using MarketData.Core.Abstractions;

namespace Aggregator.Persistence;

/// <summary>
/// PostgreSQL <see cref="ITickStore"/> that persists a batch with a single binary COPY — the
/// fast bulk-load path, far cheaper than per-row inserts (spec 2.4). A thin adapter by design:
/// it persists one batch and <b>throws on any failure</b>, leaving the batching / retry / drop
/// policy to <see cref="BatchingTickWriter"/> (SOLID: "how to persist" vs "when/whether to").
/// </summary>
public sealed class NpgsqlTickStore : ITickStore
{
    private const string CopyCommand =
        "COPY ticks (source, ticker, price, volume, ts) FROM STDIN (FORMAT BINARY)";

    private readonly NpgsqlDataSource _dataSource;

    /// <param name="dataSource">
    /// A pooled data source owned by the composition root (it configures and disposes it), so this
    /// adapter stays a stateless writer.
    /// </param>
    public NpgsqlTickStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task WriteBatchAsync(IReadOnlyList<NormalizedTick> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
            return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var writer = await connection.BeginBinaryImportAsync(CopyCommand, cancellationToken);

        foreach (var tick in batch)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(tick.Source, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(tick.Ticker, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(tick.Price, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(tick.Volume, NpgsqlDbType.Numeric, cancellationToken);
            await writer.WriteAsync(tick.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
        }

        // Flushes the whole batch as one transaction; throws if the server rejects any row.
        await writer.CompleteAsync(cancellationToken);
    }
}
