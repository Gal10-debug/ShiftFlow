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
        adminConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnection))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                throw new InvalidOperationException("CI requires TEST_POSTGRES_CONNECTION_STRING.");
            container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await container.StartAsync();
            connectionString = container.GetConnectionString();
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
        settings.Pooling = false;
        connectionString = settings.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
        else if (adminConnection is not null && connectionString.Length > 0)
        {
            await using var connection = new NpgsqlConnection(adminConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
