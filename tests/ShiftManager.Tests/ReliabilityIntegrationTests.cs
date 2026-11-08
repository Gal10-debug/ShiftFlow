using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    [PostgresFact]
    public async Task Database_lock_timeout_rolls_back_correction_and_allows_explicit_retry()
    {
        var (admin, definition, review) = await ManualSetup(); using (admin)
        {
            // Limit only this new test host. Production timeout and retry policy are unchanged.
            var settings = new NpgsqlConnectionStringBuilder(database.GetConnectionString()) { CommandTimeout = 1 };
            await using var bounded = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
                .WithIsolatedTestKeys().UseSetting("ConnectionStrings:Database", settings.ConnectionString));
            using var client = bounded.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
            Assert.Equal(HttpStatusCode.OK, (await Send(client, "/api/auth/login", new { username = "admin", password = Password }, "POST")).StatusCode);
            await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await new NpgsqlCommand("SELECT pg_advisory_xact_lock(71004)", connection, tx).ExecuteNonQueryAsync();
            var body = new ManualAssignmentRequest(review.Token, "ASSIGN", null, ids["employee"], definition, new(2026, 10, 1));
            var response = await Send(client, ReviewPath + "/assignments", body, "POST");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("INTERNAL_ERROR", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
            await tx.RollbackAsync();
            var after = await Review(admin); Assert.Equal(review.Token, after.Token); Assert.Empty(after.Draft.Cells.SelectMany(c => c.Employees));
            Assert.Empty((await admin.GetFromJsonAsync<ScheduleEditAudit[]>(ReviewPath + "/audit"))!);
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, ReviewPath + "/assignments", body, "POST")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, ReviewPath + "/assignments", body, "POST")).StatusCode);
            Assert.Single((await Review(admin)).Draft.Cells.SelectMany(c => c.Employees));
            Assert.Single((await admin.GetFromJsonAsync<ScheduleEditAudit[]>(ReviewPath + "/audit"))!);
        }
    }

    [PostgresFact]
    public async Task Interrupted_database_write_rolls_back_assignment_and_audit_then_recovers_after_host_restart()
    {
        var (admin, definition, review) = await ManualSetup(); using (admin)
        {
            await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync();
            // Interrupt after an INSERT, inside the real transaction. The trigger exists only in this test's fresh DB.
            await new NpgsqlCommand("""
                CREATE FUNCTION phase7d_pause() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN PERFORM pg_sleep(10); RETURN NEW; END $$;
                CREATE TRIGGER phase7d_pause AFTER INSERT ON "ScheduleEditAudit" FOR EACH ROW EXECUTE FUNCTION phase7d_pause();
                """, connection).ExecuteNonQueryAsync();
            var body = new ManualAssignmentRequest(review.Token, "ASSIGN", null, ids["employee"], definition, new(2026, 10, 1));
            var pending = Send(admin, ReviewPath + "/assignments", body, "POST");
            var watch = Stopwatch.StartNew(); var cancelled = false;
            while (watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                await using var cancel = new NpgsqlCommand("""
                    SELECT pg_cancel_backend(pid) FROM pg_stat_activity
                    WHERE datname = current_database() AND pid <> pg_backend_pid()
                    AND wait_event = 'PgSleep' AND query LIKE '%ScheduleEditAudit%'
                    """, connection);
                if ((bool?)await cancel.ExecuteScalarAsync() == true) { cancelled = true; break; }
                await Task.Delay(25);
            }
            Assert.True(cancelled, "The writer must reach the injected post-insert failure point.");
            Assert.Equal(HttpStatusCode.InternalServerError, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).StatusCode);
            await new NpgsqlCommand("DROP TRIGGER phase7d_pause ON \"ScheduleEditAudit\"; DROP FUNCTION phase7d_pause()", connection).ExecuteNonQueryAsync();
            var after = await Review(admin); Assert.Equal(review.Token, after.Token);
            Assert.Empty(after.Draft.Cells.SelectMany(c => c.Employees));
            Assert.Empty((await admin.GetFromJsonAsync<ScheduleEditAudit[]>(ReviewPath + "/audit"))!);
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, ReviewPath + "/assignments", body, "POST")).StatusCode);
            var assignment = (await Review(admin)).Draft.Cells.SelectMany(c => c.Employees).Single().AssignmentId;
            await factory.DisposeAsync();
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").WithIsolatedTestKeys()
                .UseSetting("ConnectionStrings:Database", database.GetConnectionString()));
            using var restarted = await Login();
            Assert.Equal(assignment, (await Review(restarted)).Draft.Cells.SelectMany(c => c.Employees).Single().AssignmentId);
            Assert.Single((await restarted.GetFromJsonAsync<ScheduleEditAudit[]>(ReviewPath + "/audit"))!);
        }
    }
}
