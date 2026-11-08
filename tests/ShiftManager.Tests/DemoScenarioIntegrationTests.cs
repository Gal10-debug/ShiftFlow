using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Development;
using ShiftManager.Domain;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed class DemoScenarioIntegrationTests
{
    [PostgresFact]
    public async Task Preview_approval_workflows_idempotency_restart_and_preservation_use_real_services()
    {
        await using var database = new TestDatabase();
        await database.StartAsync();
        var seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var name = DemoCli.DatabaseName(new(2026, 11, 1), seed);
        await using var admin = new NpgsqlConnection(database.GetConnectionString());
        await admin.OpenAsync();
        await using (var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin)) await cmd.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(database.GetConnectionString()) { Database = name, Host = "127.0.0.1", Pooling = false }.ConnectionString;
        WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").WithIsolatedTestKeys()
            .UseSetting("ConnectionStrings:Database", connection).ConfigureServices(services =>
            {
                var worker = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(GenerationWorker));
                if (worker is not null) services.Remove(worker);
            }));
        var output = Path.Combine(Directory.GetCurrentDirectory(), ".demo", name);
        string[] Args(string verb, string? token = null) => new[] { verb, "--month", "2026-12", "--demo-month", "2026-11", "--seed", seed.ToString() }
            .Concat(token is null ? [] : new[] { "--approve", token }).ToArray();
        try
        {
            await using var factory = Factory();
            await DemoCli.Run(factory.Services, "Testing", connection, ["seed-demo", "--month", "2026-11", "--seed", seed.ToString()]);
            var initial = await DemoScenario.Run(factory.Services, "Testing", connection, Args("preview-demo-scenario"));
            Assert.Equal(35, initial.PlannedRequests);
            Assert.Equal(27, initial.UnaffectedAccounts.Length);
            await Assert.ThrowsAsync<InvalidOperationException>(() => DemoScenario.Run(factory.Services, "Testing", connection, Args("apply-demo-scenario")));
            // Force an authorized-workflow failure after Submit has inserted a request.
            // The caller-owned batch must roll that insert back as well.
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var approvers = await db.Set<OrganizationalTeam>().Select(t => new { t.LeaderUserId, t.DeputyUserId }).ToArrayAsync();
                var ids = approvers.SelectMany(t => new[] { t.LeaderUserId!.Value, t.DeputyUserId!.Value }).ToArray();
                await db.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(x => x.SetProperty(u => u.MustChangePassword, true));
                var unsafePreview = await DemoScenario.Run(factory.Services, "Testing", connection, Args("preview-demo-scenario"));
                await Assert.ThrowsAsync<ApiException>(() => DemoScenario.Run(factory.Services, "Testing", connection, Args("apply-demo-scenario", unsafePreview.Token)));
                Assert.Empty(await db.Set<AbsenceRequest>().ToArrayAsync());
                await db.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(x => x.SetProperty(u => u.MustChangePassword, false));
            }
            Guid manualId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Empty(await db.Set<AbsenceRequest>().ToArrayAsync());
                var login = initial.Items.First(i => i.Workflow == "APPROVED").Login;
                var user = await db.Users.SingleAsync(u => u.UserName == login);
                var request = await scope.ServiceProvider.GetRequiredService<AbsenceService>().Submit(
                    new("OTHER", new(2026, 11, 1), new(2026, 12, 31), false, [], true, null, null, "manual: preserve"), DemoCli.Principal(user, "EMPLOYEE"));
                manualId = request.Id;
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => DemoScenario.Run(factory.Services, "Testing", connection, Args("apply-demo-scenario", initial.Token)));
            var preview = await DemoScenario.Run(factory.Services, "Testing", connection, Args("preview-demo-scenario"));
            Assert.Contains(preview.Items, i => i.Action == "SKIP_EXISTING_ABSENCE");
            await DemoScenario.Run(factory.Services, "Testing", connection, Args("apply-demo-scenario", preview.Token));
            var applied = await DemoScenario.Run(factory.Services, "Testing", connection, Args("preview-demo-scenario"));
            Assert.Equal(0, applied.PlannedRequests);
            await DemoScenario.Run(factory.Services, "Testing", connection, Args("apply-demo-scenario", applied.Token));
            // A second host/context verifies persistence, rather than relying on tracked objects.
            await using var restarted = Factory();
            using var verifyScope = restarted.Services.CreateScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(61, await verify.Users.CountAsync());
            Assert.Equal(60, await verify.Set<TeamMembership>().CountAsync());
            var requests = await verify.Set<AbsenceRequest>().Include(r => r.Weekdays).ToArrayAsync();
            Assert.Equal("manual: preserve", requests.Single(r => r.Id == manualId).Explanation);
            Assert.Equal("PENDING", requests.Single(r => r.Id == manualId).Status);
            Assert.Equal(preview.PlannedRequests + 1, requests.Length);
            Assert.Equal(preview.PlannedDecisions, await verify.Set<AbsenceDecision>().CountAsync());
            Assert.Equal(preview.Items.Sum(i => i.PlannedWeekdayRecords), await verify.Set<AbsenceWeekday>().CountAsync());
            Assert.Equal(preview.Items.Sum(i => i.PlannedConflictRecords), await verify.Set<AbsenceConflict>().CountAsync());
            Assert.Single(requests, r => r.Kind == "MODIFY" && r.Status == "APPROVED");
            Assert.Single(requests, r => r.Kind == "CANCEL" && r.Status == "APPROVED");
            Assert.All(requests.GroupBy(r => new { r.Explanation, r.Kind }), g => Assert.Single(g));
            var problem = await verifyScope.ServiceProvider.GetRequiredService<SchedulingInputs>().Read(2026, 12);
            foreach (var r in requests)
            {
                if (r.Status == "APPROVED" && r.Kind != "CANCEL") Assert.Contains(problem.ApprovedAbsences!, a => a.Id == r.Id);
                else Assert.DoesNotContain(problem.ApprovedAbsences!, a => a.Id == r.Id);
                if (r.Status == "PENDING") Assert.Contains(problem.PendingAbsences!, a => a.Id == r.Id);
            }
            Assert.All(await verify.Set<AbsenceDecision>().Where(d => d.Decision == "REJECTED").ToArrayAsync(), d => Assert.False(string.IsNullOrWhiteSpace(d.RejectionReason)));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }
}
