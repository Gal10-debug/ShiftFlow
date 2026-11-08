using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ShiftManager.E2EHost;

// Test executable only. Never returns SQL, parameter values, Identity data or cookies.
internal sealed class PerformanceProbe : DbCommandInterceptor
{
    private sealed record Sample(string Sql, NpgsqlParameter[] Parameters);
    private readonly ConcurrentDictionary<string, Sample> samples = new();
    private readonly ConcurrentDictionary<string, (long Count, double Milliseconds)> timings = new();
    private volatile bool enabled;
    public void Clear() { samples.Clear(); timings.Clear(); enabled = true; }
    private void Record(DbCommand command, CommandExecutedEventData data)
    {
        if (!enabled) return;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.CommandText)));
        timings.AddOrUpdate(hash, (1, data.Duration.TotalMilliseconds), (_, old) => (old.Count + 1, old.Milliseconds + data.Duration.TotalMilliseconds));
        // Only SELECTs over business tables are explained, never writes or account lookups.
        if (samples.Count < 50 && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
            && new[] { "SchedulingCell", "SchedulePublication", "AvailabilitySubmission", "AbsenceRequest", "ShiftExchange", "TeamMembership" }.Any(command.CommandText.Contains))
            samples.TryAdd(hash, new(command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(p => (NpgsqlParameter)p.Clone()).ToArray()));
    }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default)
    { Record(command, data); return ValueTask.FromResult(result); }

    public object Snapshot()
    {
        using var process = Process.GetCurrentProcess();
        return new { ReaderCommands = timings.Sum(t => t.Value.Count), CpuSeconds = process.TotalProcessorTime.TotalSeconds,
            WorkingSetBytes = process.WorkingSet64, PeakWorkingSetBytes = process.PeakWorkingSet64 > 0 ? process.PeakWorkingSet64 : (long?)null };
    }

    public async Task<object> Report(string connection)
    {
        var plans = new List<object>();
        await using var c = new NpgsqlConnection(connection); await c.OpenAsync();
        await using (var analyze = new NpgsqlCommand("ANALYZE", c)) await analyze.ExecuteNonQueryAsync();
        foreach (var (hash, sample) in samples.OrderBy(s => s.Key))
        {
            await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sample.Sql, c) { CommandTimeout = 5 };
            command.Parameters.AddRange(sample.Parameters.Select(p => (NpgsqlParameter)p.Clone()).ToArray());
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            var root = document.RootElement[0];
            var nodes = new List<object>();
            void Visit(JsonElement node)
            {
                nodes.Add(new { Type = node.GetProperty("Node Type").GetString(),
                    Relation = node.TryGetProperty("Relation Name", out var relation) ? relation.GetString() : null,
                    Rows = node.GetProperty("Actual Rows").GetDouble(), Loops = node.GetProperty("Actual Loops").GetDouble(),
                    SharedHitBlocks = node.GetProperty("Shared Hit Blocks").GetInt64(),
                    SharedReadBlocks = node.GetProperty("Shared Read Blocks").GetInt64() });
                if (node.TryGetProperty("Plans", out var children)) foreach (var child in children.EnumerateArray()) Visit(child);
            }
            Visit(root.GetProperty("Plan"));
            plans.Add(new { Fingerprint = hash, PlanningMs = root.GetProperty("Planning Time").GetDouble(),
                ExecutionMs = root.GetProperty("Execution Time").GetDouble(), Nodes = nodes });
        }
        await using var connections = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()", c);
        using var process = Process.GetCurrentProcess();
        return new { Queries = timings.OrderByDescending(t => t.Value.Milliseconds).Select(t => new { Fingerprint = t.Key, t.Value.Count, t.Value.Milliseconds }),
            Plans = plans, DatabaseConnections = (long)(await connections.ExecuteScalarAsync())!,
            WorkingSetBytes = process.WorkingSet64, PeakWorkingSetBytes = process.PeakWorkingSet64 > 0 ? process.PeakWorkingSet64 : (long?)null, CpuSeconds = process.TotalProcessorTime.TotalSeconds };
    }
}
