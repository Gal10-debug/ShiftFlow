using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ShiftManager.Operations;

public sealed class DatabaseReadiness(IConfiguration configuration, IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Database"));
            await connection.OpenAsync(timeout.Token);
            var pilot = services.GetService<PilotConfiguration>();
            await using var command = new NpgsqlCommand(pilot is null ? "SELECT 1" : """
                SELECT current_database() = @database AND session_user = @role
                  AND (SELECT deployment_id = @id FROM shiftflow_ops.identity)
                  AND to_regclass('public."AspNetUsers"') IS NOT NULL
                """, connection) { CommandTimeout = 2 };
            if (pilot is not null)
            {
                command.Parameters.AddWithValue("database", pilot.DatabaseName);
                command.Parameters.AddWithValue("role", pilot.Role("runtime"));
                command.Parameters.AddWithValue("id", pilot.DeploymentId.ToString("N"));
            }
            var result = await command.ExecuteScalarAsync(timeout.Token);
            return pilot is null ? HealthCheckResult.Healthy() : result is true ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch { return HealthCheckResult.Unhealthy(); }
    }
}

public static class OperationalHealth
{
    public static bool IsHealth(PathString path) => path == "/health/live" || path == "/health/ready";
    public static async Task<int> Probe(string name)
    {
        if (name is not ("live" or "ready")) return 1;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:8080/health/{name}");
            if (Uri.TryCreate(Environment.GetEnvironmentVariable("PILOT_PUBLIC_ORIGIN"), UriKind.Absolute, out var origin)) request.Headers.Host = origin.Authority;
            return (await client.SendAsync(request)).IsSuccessStatusCode ? 0 : 1;
        }
        catch { return 1; }
    }
    public static void UsePilotObservability(this WebApplication app, PilotConfiguration? pilot)
    {
        if (pilot is null) return;
        app.Use(async (context, next) =>
        {
            var watch = Stopwatch.StartNew();
            context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
            using var scope = app.Logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = context.TraceIdentifier });
            await next(context);
            if (!IsHealth(context.Request.Path))
                app.Logger.LogInformation("Request completed {Method} {StatusCode} {ElapsedMs} {RequestId}",
                    context.Request.Method, context.Response.StatusCode, watch.Elapsed.TotalMilliseconds, context.TraceIdentifier);
        });
    }
}
