using Npgsql;
using Testcontainers.PostgreSql;

namespace ShiftManager.Tests;

// Each test owns a fresh database, including tests that roll migrations back to zero.
internal sealed class TestDatabase : IAsyncDisposable
{
    private PostgreSqlContainer? container;
    private string? adminConnection;
    private string connectionString = "";
    private readonly string databaseName = $"shiftflow_test_{Guid.NewGuid():N}";

    public string GetConnectionString() => connectionString;

    public async Task StartAsync()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        adminConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnection))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                throw new InvalidOperationException("CI requires TEST_POSTGRES_CONNECTION_STRING.");
            container = new PostgreSqlBuilder("postgres:17.11-alpine3.24@sha256:3540c0362c809df084ffac0ae69fe794515f9de92692f02178bcdce0ef0302ac").Build();
            await container.StartAsync();
            connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Pooling = true, MaxPoolSize = 8 }.ConnectionString;
            TestTiming.Record("database-container-start", watch.Elapsed.TotalSeconds);
            return;
        }

        var settings = new NpgsqlConnectionStringBuilder(adminConnection);
        if (settings.Host is not ("localhost" or "127.0.0.1") || settings.Database != "shiftflow_ci")
            throw new InvalidOperationException("Test service must use a loopback host and the disposable shiftflow_ci database.");

        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        settings.Database = databaseName;
        // Reuse connections only within this test's unique database. No data/host
        // fixture is shared, and migrations may still roll all the way back.
        settings.Pooling = true;
        settings.MaxPoolSize = 8;
        connectionString = settings.ConnectionString;
        TestTiming.Record("database-create", watch.Elapsed.TotalSeconds);
    }

    public async ValueTask DisposeAsync()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (connectionString.Length > 0)
        {
            // Clear this database's pool only, after its host/contexts have closed.
            // Clearing all pools would interfere with parallel independent tests.
            using var owned = new NpgsqlConnection(connectionString);
            NpgsqlConnection.ClearPool(owned);
        }
        if (container is not null)
            await container.DisposeAsync();
        else if (adminConnection is not null && connectionString.Length > 0)
        {
            await using var connection = new NpgsqlConnection(adminConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
        TestTiming.Record("database-dispose", watch.Elapsed.TotalSeconds);
    }
}
