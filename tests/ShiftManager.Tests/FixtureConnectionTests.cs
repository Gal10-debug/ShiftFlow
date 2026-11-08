using System.Diagnostics;
using Npgsql;

namespace ShiftManager.Tests;

public partial class PostgresTests
{
    [PostgresFact]
    public async Task Fixture_connections_reuse_one_backend_and_cleanup_does_not_clear_another_database_pool()
    {
        var backends = new HashSet<int>();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            await using var connection = new NpgsqlConnection(database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", connection);
            backends.Add((int)(await command.ExecuteScalarAsync())!);
        }
        TestTiming.Record("fixture-50-connection-cycles", watch.Elapsed.TotalSeconds);
        Assert.Single(backends);
        await using (var other = new TestDatabase())
        {
            await other.StartAsync();
            Assert.NotEqual(new NpgsqlConnectionStringBuilder(database.GetConnectionString()).Database,
                new NpgsqlConnectionStringBuilder(other.GetConnectionString()).Database);
            await using var connection = new NpgsqlConnection(other.GetConnectionString()); await connection.OpenAsync();
            Assert.DoesNotContain((int)(await new NpgsqlCommand("SELECT pg_backend_pid()", connection).ExecuteScalarAsync())!, backends);
        }
        // Disposing the other database must clear only its pool, never all test pools.
        await using var retained = new NpgsqlConnection(database.GetConnectionString()); await retained.OpenAsync();
        Assert.Contains((int)(await new NpgsqlCommand("SELECT pg_backend_pid()", retained).ExecuteScalarAsync())!, backends);
    }
}
