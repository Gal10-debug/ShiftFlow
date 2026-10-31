using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;

namespace ShiftManager.Development;

public static class DemoSmoke
{
    public static async Task Run(IServiceProvider services, DateOnly month, string output)
    {
        var steps = new List<string>();
        try
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var directory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "accounts.json")));
            var credential = directory.RootElement[0];
            var adminUser = await users.FindByNameAsync(credential.GetProperty("Login").GetString()!);
            if (adminUser is null || !await users.CheckPasswordAsync(adminUser, credential.GetProperty("Password").GetString()!))
                throw new InvalidOperationException("Local demo credentials could not be verified.");
            var admin = DemoCli.Principal(adminUser, "ADMIN", "EMPLOYEE");
            var reviews = services.GetRequiredService<ScheduleReviewService>();
            var r = await reviews.Review(month.Year, month.Month, admin);
            if (r.State != "DRAFT" || r.Draft.Cells.Any(c => c.Employees.Length > 0)) throw new InvalidOperationException("Smoke test requires a fresh seeded draft.");
            var problem = await services.GetRequiredService<SchedulingInputs>().Read(month.Year, month.Month);
            if (problem.Employees.Length != 56 || await db.Users.CountAsync() != 60 || await db.Set<OrganizationalTeam>().CountAsync() != 4)
                throw new InvalidOperationException("Demo population verification failed.");
            steps.Add("60 accounts, four teams, 56 eligible employees and submitted availability verified");
            var result = services.GetRequiredService<IScheduleOptimizer>().Solve(problem, 30, CancellationToken.None);
            if (result.Status is not ("OPTIMAL" or "FEASIBLE")) throw new InvalidOperationException($"Solver returned {result.Status}");
            GeneratedDraftService.Validate(problem, result.Cells);
            var queue = services.GetRequiredService<GenerationQueue>();
            var started = queue.Start(month.Year, month.Month);
            var job = queue.Get(started.Id);
            job.Problem = problem;
            job.Response = job.Response with { State = "READY", SolverStatus = result.Status, ProvenObjectives = result.ProvenObjectives,
                Cells = result.Cells, Summary = StaffingSummary.From(result.Cells) };
            await services.GetRequiredService<GeneratedDraftService>().Save(job.Id, admin, CancellationToken.None);
            steps.Add("Automatic generation and validated draft save");
            r = await reviews.Review(month.Year, month.Month, admin);
            var cell = r.Draft.Cells.First(c => c.Semantic == "ORDINARY" && c.Employees.Length > 0);
            var original = cell.Employees[0];
            // Remove and restore through the same validated, audited manual workflow used by the board.
            r = await reviews.Manual(month.Year, month.Month, new(r.Token, "REMOVE", original.AssignmentId, null, null, cell.Date), admin);
            r = await reviews.Manual(month.Year, month.Month, new(r.Token, "ASSIGN", null, original.UserId, cell.DefinitionId, cell.Date), admin);
            r = await reviews.Acknowledge(month.Year, month.Month, new(r.Token), admin);
            steps.Add("Valid manual correction and review");
            var preliminary = await reviews.Publish(month.Year, month.Month, "PRELIMINARY", new(r.Token, true, true, "Synthetic demo smoke"), admin);
            var employeeUser = (await users.FindByIdAsync(original.UserId.ToString()))!;
            var employeeCredential = directory.RootElement.EnumerateArray().Single(x => x.GetProperty("Login").GetString() == employeeUser.UserName);
            if (!await users.CheckPasswordAsync(employeeUser, employeeCredential.GetProperty("Password").GetString()!)) throw new InvalidOperationException("Employee login verification failed.");
            var employee = DemoCli.Principal(employeeUser, "EMPLOYEE");
            var visible = await reviews.Mine(month.Year, month.Month, employee);
            if (visible.Id != preliminary.Id) throw new InvalidOperationException("Employee publication mismatch.");
            var assigned = visible.Snapshot!.Cells.SelectMany(c => c.Employees).First(e => e.UserId == employeeUser.Id);
            var issue = await reviews.Report(month.Year, month.Month, new(preliminary.Id, assigned.AssignmentId, "Synthetic shift correction request"), employee);
            var issues = await reviews.Issues(month.Year, month.Month, admin, true);
            if (!issues.Any(i => i.Id == issue.Id)) throw new InvalidOperationException("Administrator cannot review employee issue.");
            r = await reviews.Review(month.Year, month.Month, admin);
            r = await reviews.Correction(month.Year, month.Month, new(r.Token), admin);
            r = await reviews.Manual(month.Year, month.Month, new(r.Token, "REMOVE", assigned.AssignmentId, null, null, cell.Date), admin);
            r = await reviews.Manual(month.Year, month.Month, new(r.Token, "ASSIGN", null, original.UserId, cell.DefinitionId, cell.Date), admin);
            await reviews.UpdateIssue(month.Year, month.Month, issue.Id, new(issue.Version, "RESOLVED", "Reviewed synthetic correction"), admin);
            r = await reviews.Review(month.Year, month.Month, admin);
            r = await reviews.Acknowledge(month.Year, month.Month, new(r.Token), admin);
            var final = await reviews.Publish(month.Year, month.Month, "FINAL", new(r.Token, true, false, "Synthetic final"), admin);
            var history = await reviews.History(month.Year, month.Month, admin);
            if (final.VersionNumber != 2 || history.Length != 2 || history.Single(h => h.Id == preliminary.Id).Snapshot!.Cells.SelectMany(c => c.Employees).All(e => e.AssignmentId != assigned.AssignmentId))
                throw new InvalidOperationException("Publication history verification failed.");
            if ((await reviews.Mine(month.Year, month.Month, employee)).Id != final.Id) throw new InvalidOperationException("Final employee publication mismatch.");
            steps.Add("Preliminary publication, employee access, issue reporting, administrator review, correction, final publication and immutable history");
            var current = await services.GetRequiredService<SchedulingInputs>().Read(month.Year, month.Month, review: true);
            var violations = SchedulingRules.Violations(current.Work, month.Year, month.Month);
            var roster = await reviews.Review(month.Year, month.Month, admin);
            if (violations.Length > 0 || roster.Conflicts.Length > 0) throw new InvalidOperationException("Saved roster constraint violations detected.");
            steps.Add("All saved roster hard constraints verified");
            var report = new
            {
                Success = true, Steps = steps, Accounts = await db.Users.CountAsync(), Eligible = current.Employees.Length,
                Teams = await db.Set<TeamMembership>().GroupBy(m => m.TeamId).Select(g => new { Team = g.Key, Count = g.Count() }).ToArrayAsync(),
                SubmittedAvailability = await db.Set<AvailabilitySubmission>().CountAsync(), Assignments = current.Work.Length,
                Shortages = roster.Draft.Cells.Where(c => c.RequiredEmployees > c.Employees.Length).Select(c => new { c.Date, c.Name, Missing = c.RequiredEmployees - c.Employees.Length }),
                WeekendAllocations = current.Work.Where(w => SchedulingRules.Weekend(w.Date)).GroupBy(w => new { w.UserId, Month = SchedulingRules.AllocationMonth(w) })
                    .Select(g => new { g.Key.UserId, g.Key.Month, Count = SchedulingRules.Allocations(g) }),
                WeeklyNights = current.Work.Where(w => w.AnchorType == ShiftType.NIGHT).GroupBy(w => new { w.UserId, Week = SchedulingRules.Week(w.Date) }).Select(g => new { g.Key.UserId, g.Key.Week, Count = g.Count() }),
                WeeklySixNine = current.Work.Where(w => w.Semantic == SchedulingRules.SixNine).GroupBy(w => new { w.UserId, Week = SchedulingRules.Week(w.Date) }).Select(g => new { g.Key.UserId, g.Key.Week, Count = g.Count() }),
                WeekendAllocationDistribution = current.Employees.Select(e => new { e.Id, Allocations = SchedulingRules.Allocations(current.Work.Where(w => w.UserId == e.Id && SchedulingRules.Weekend(w.Date) && SchedulingRules.AllocationMonth(w) == month)) }),
                OnCallPairing = "Validated", Violations = violations, RosterConflicts = roster.Conflicts,
                HardBlockViolations = roster.Conflicts.Where(c => c.Code == "HARD_BLOCK_CONFLICT"),
                OverlapOrConsecutiveViolations = roster.Conflicts.Where(c => c.Code == "WORK_OVERLAP_OR_CONSECUTIVE"),
                Versions = history.Select(h => new { h.Id, h.VersionNumber, h.PublicationType }), Solver = result.Status
            };
            await File.WriteAllTextAsync(Path.Combine(output, "verification-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(output, "verification-report.json"), JsonSerializer.Serialize(new { Success = false, Steps = steps, FailedWith = error.GetType().Name }));
            throw;
        }
    }
}
