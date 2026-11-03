using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Development;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed class DemoSmokeIntegrationTests
{
    // A legal preview with two shifts for one employee makes the reported assignment
    // unambiguous without relying on the solver's tie-breaking or execution time.
    private sealed class MultipleAssignmentOptimizer : IScheduleOptimizer
    {
        public CandidateCell Earlier { get; private set; } = null!;
        public CandidateCell Target { get; private set; } = null!;
        public Guid EmployeeId { get; private set; }
        public OptimizationResult Solve(SchedulingProblem problem, double seconds, CancellationToken ct)
        {
            Earlier = problem.Cells.First(c => c.Semantic == SchedulingRules.SixNine && c.Required > 0);
            Target = problem.Cells.First(c => c.Semantic == "ORDINARY" && c.Required > 0 && c.Date >= Earlier.Date.AddDays(2) && !SchedulingRules.Weekend(c.Date));
            var employee = problem.Employees.First(e => problem.Eligible(e, Earlier) && problem.Eligible(e, Target));
            EmployeeId = employee.Id;
            return new("FEASIBLE", 0, problem.Cells.Select(c => new GeneratedCell(c.DefinitionId, c.Date, c.Start, c.End, c.Required,
                c == Earlier || c == Target ? [new ScheduledEmployee(employee.Id, employee.Name, false, false)] : [])).ToArray());
        }
    }

    [PostgresFact]
    public async Task Smoke_corrects_reported_assignment_when_employee_has_an_earlier_shift()
    {
        await using var database = new TestDatabase();
        await database.StartAsync();
        var seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var name = DemoCli.DatabaseName(new(2026, 11, 1), seed);
        await using var admin = new NpgsqlConnection(database.GetConnectionString());
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin)) await create.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(database.GetConnectionString()) { Database = name, Host = "127.0.0.1", Pooling = false };
        var output = Path.Combine(Directory.GetCurrentDirectory(), ".demo", name);
        try
        {
            var optimizer = new MultipleAssignmentOptimizer();
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing")
                .UseSetting("ConnectionStrings:Database", connection.ConnectionString).ConfigureServices(services =>
                {
                    var worker = services.FirstOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(GenerationWorker));
                    if (worker is not null) services.Remove(worker);
                    services.RemoveAll<IScheduleOptimizer>();
                    services.AddSingleton<IScheduleOptimizer>(optimizer);
                }));
            await DemoCli.Run(factory.Services, "Testing", connection.ConnectionString, ["seed-demo", "--month", "2026-11", "--seed", seed.ToString()]);
            await DemoCli.Run(factory.Services, "Testing", connection.ConnectionString, ["smoke-demo", "--month", "2026-11", "--seed", seed.ToString()]);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var publications = await db.Set<SchedulePublication>().OrderBy(p => p.VersionNumber).ToArrayAsync();
            Assert.Equal(2, publications.Length);
            var preliminary = JsonSerializer.Deserialize<ScheduleSnapshot>(publications[0].Snapshot)!;
            var final = JsonSerializer.Deserialize<ScheduleSnapshot>(publications[1].Snapshot)!;
            var earlier = preliminary.Cells.Single(c => c.DefinitionId == optimizer.Earlier.DefinitionId && c.Date == optimizer.Earlier.Date).Employees.Single();
            var target = preliminary.Cells.Single(c => c.DefinitionId == optimizer.Target.DefinitionId && c.Date == optimizer.Target.Date).Employees.Single();
            Assert.Equal(optimizer.EmployeeId, earlier.UserId);
            Assert.Equal(earlier.UserId, target.UserId);
            Assert.Equal(earlier.AssignmentId, preliminary.Cells.SelectMany(c => c.Employees).First(e => e.UserId == target.UserId).AssignmentId);
            var issue = await db.Set<ScheduleIssue>().SingleAsync();
            Assert.Equal(target.AssignmentId, issue.AssignmentId);
            Assert.Equal("RESOLVED", issue.Status);
            Assert.Equal(earlier, final.Cells.Single(c => c.DefinitionId == optimizer.Earlier.DefinitionId && c.Date == optimizer.Earlier.Date).Employees.Single());
            Assert.True(await db.Set<ShiftAssignment>().AnyAsync(a => a.Id == earlier.AssignmentId));
            var corrected = final.Cells.Single(c => c.DefinitionId == optimizer.Target.DefinitionId && c.Date == optimizer.Target.Date).Employees.Single();
            Assert.Equal(target.UserId, corrected.UserId);
            Assert.NotEqual(target.AssignmentId, corrected.AssignmentId);
            Assert.Equal("הערת הדגמה — להגיע חצי שעה מוקדם", target.Note);
            Assert.Equal("הערה שפורסמה בגרסה הסופית", corrected.Note);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }
}
