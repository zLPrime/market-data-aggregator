using Npgsql;
using Testcontainers.PostgreSql;

namespace Trading.Tests.EndToEnd;

/// <summary>
/// One throwaway <c>postgres:17</c> shared by every end-to-end test in the class (xUnit runs a
/// class's tests sequentially, so a single container with a <see cref="ResetAsync"/> between tests
/// is safe and keeps the suite to one Docker start). Provisioned from the same <c>db/schema.sql</c>
/// production uses. When no Docker daemon is reachable it records the reason instead of throwing, so
/// the tests can <see cref="Xunit.Skip"/> rather than fail on machines without it (as Phase d does).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public NpgsqlDataSource? DataSource { get; private set; }
    public string? DockerUnavailable { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            // Building the container validates the Docker endpoint, so it must sit inside the guard
            // too — otherwise a missing daemon throws before the tests can skip.
            _postgres = new PostgreSqlBuilder("postgres:17").Build();
            await _postgres.StartAsync();
        }
        catch (Exception ex)
        {
            DockerUnavailable = ex.Message; // no Docker daemon reachable -> the tests skip
            return;
        }

        DataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = DataSource.CreateCommand(schema);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Clears the <c>ticks</c> table so each scenario starts from an empty database.</summary>
    public async Task ResetAsync()
    {
        await using var command = DataSource!.CreateCommand("TRUNCATE ticks");
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null)
            await DataSource.DisposeAsync();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }
}
