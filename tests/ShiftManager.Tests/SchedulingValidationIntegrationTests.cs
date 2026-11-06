using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    private async Task<SchedulingProblem> ValidationInputs()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SchedulingInputs>().Read(2026, 10);
    }

    [PostgresFact]
    public async Task Validation_inputs_filter_inactive_accounts_administrators_and_leaders_but_keep_deputy()
    {
        using var admin = await Login(); await SetupTeam(admin); await ConfigureAutomatic(1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(ids["admin"].ToString()))!, "EMPLOYEE")).Succeeded);
            var inactive = await db.Users.SingleAsync(u => u.Id == ids["employee"]);
            inactive.IsActive = false; await db.SaveChangesAsync();
        }
        var p = await ValidationInputs();
        Assert.DoesNotContain(p.Employees, e => e.Id == ids["employee"] || e.Id == ids["admin"] || e.Id == ids["leader"]);
        Assert.Contains(p.Employees, e => e.Id == ids["coordinator"]);
        var result = new ScheduleOptimizer().Solve(p, 10, default);
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
        Assert.NotEmpty(result.Cells.SelectMany(c => c.Employees));
        Assert.All(result.Cells.SelectMany(c => c.Employees), e => Assert.Equal(ids["coordinator"], e.UserId));
    }

    [PostgresFact]
    public async Task Validation_absence_status_and_approved_replacement_are_reflected_in_real_solver_inputs()
    {
        await ConfigureAutomatic(1);
        using var admin = await Login(); using var employee = await Login("employee");
        var request = await SubmitAbsence(employee);
        Assert.Empty((await ValidationInputs()).ApprovedAbsences!);
        Assert.Single((await ValidationInputs()).PendingAbsences!);
        await DecideAbsence(admin, request, "REJECTED");
        Assert.Empty((await ValidationInputs()).ApprovedAbsences!);
        var cancelled = await SubmitAbsence(employee);
        Assert.Equal(HttpStatusCode.OK, (await Send(employee, $"/api/absences/{cancelled.Id}/cancel-pending", new AbsenceVersion(cancelled.Version), "POST")).StatusCode);
        Assert.Empty((await ValidationInputs()).PendingAbsences!);
        var original = await DecideAbsence(admin, await SubmitAbsence(employee));
        var pattern = Absence() with { StartDate = new(2026, 10, 5), EndDate = new(2026, 10, 6), FullDay = false, StartTime = new(12, 0), EndTime = new(13, 0) };
        var response = await Send(employee, $"/api/absences/{original.Id}/changes", new AbsenceChange(original.Version, "MODIFY", pattern), "POST");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var proposal = (await response.Content.ReadFromJsonAsync<AbsenceResponse>())!;
        Assert.Equal(original.Id, (await ValidationInputs()).ApprovedAbsences!.Single().Id);
        var replacement = await DecideAbsence(admin, proposal);
        var p = await ValidationInputs();
        Assert.Equal(replacement.Id, p.ApprovedAbsences!.Single().Id);
        Assert.Equal(pattern.StartDate, p.ApprovedAbsences![0].StartDate);
        var result = new ScheduleOptimizer().Solve(p, 10, default);
        Assert.Empty(IndependentScheduleValidator.Check(p, result.Cells));
        Assert.DoesNotContain(result.Cells.Where(c => c.Date.Day is 5 or 6).SelectMany(c => c.Employees), e => e.UserId == ids["employee"]);
        Assert.Contains(result.Cells.Where(c => c.Date.Day == 1).SelectMany(c => c.Employees), e => e.UserId == ids["employee"]);
        response = await Send(employee, $"/api/absences/{replacement.Id}/changes", new AbsenceChange(replacement.Version, "CANCEL", null), "POST");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cancellation = (await response.Content.ReadFromJsonAsync<AbsenceResponse>())!;
        Assert.Single((await ValidationInputs()).ApprovedAbsences!);
        await DecideAbsence(admin, cancellation);
        Assert.Empty((await ValidationInputs()).ApprovedAbsences!);
        result = new ScheduleOptimizer().Solve(await ValidationInputs(), 10, default);
        Assert.Contains(result.Cells.Where(c => c.Date.Day == 5).SelectMany(c => c.Employees), e => e.UserId == ids["employee"]);
    }

    [PostgresFact]
    public async Task Validation_latest_staffing_and_stable_id_priorities_survive_display_reorder_and_rename()
    {
        await ConfigureAutomatic(1); using var admin = await Login();
        var before = await ValidationInputs();
        var rows = await Types(admin);
        var updates = Request(rows).ShiftTypes.Select(r => r with { DisplayOrder = 52 - r.DisplayOrder, Name = $"Renamed {r.DisplayOrder}" }).ToArray();
        var index = Array.FindIndex(updates, r => r.Id == BoardDefinition(2));
        updates[index] = updates[index] with { RequiredEmployees = 2 };
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, TypesPath, new UpdateShiftTypesRequest(updates))).StatusCode);
        var after = await ValidationInputs();
        Assert.NotEqual(before.Fingerprint, after.Fingerprint);
        var changedCells = after.Cells.Where(c => c.DefinitionId == BoardDefinition(2)).ToArray();
        Assert.NotEmpty(changedCells);
        Assert.All(changedCells, c => Assert.Equal(2, c.Required));
        Assert.DoesNotContain(after.Cells, c => new[] { 12, 13, 14 }.Any(slot => c.DefinitionId == BoardDefinition(slot)));
        Assert.All(after.StaffingCells!.Where(c => Enumerable.Range(1, 7).Any(slot => c.DefinitionId == BoardDefinition(slot))), c => Assert.Equal(SchedulingPriority.Critical, c.SchedulingPriority));
        Assert.All(after.StaffingCells!.Where(c => c.DefinitionId == BoardDefinition(26)), c => Assert.Equal(SchedulingPriority.Normal, c.SchedulingPriority));
        var result = new ScheduleOptimizer().Solve(after, 10, default);
        Assert.Empty(IndependentScheduleValidator.Check(after, result.Cells));
        Assert.All(result.Cells, c => Assert.True(c.Employees.Length <= c.RequiredEmployees));
    }

    [PostgresFact]
    public async Task Validation_repeated_generation_preserves_noted_and_task_linked_generated_assignments()
    {
        var definitions = await ConfigureAutomatic(1);
        Guid notedId; Guid taskMemberId; Guid taskAssignmentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var period = new SchedulePeriod { Year = 2026, Month = 10 };
            var oldRun = new SchedulingRun { Id = Guid.NewGuid(), Year = 2026, Month = 10, SavedByUserId = ids["admin"], SavedAt = clock.GetUtcNow().UtcDateTime };
            db.Add(oldRun);
            ShiftAssignment Member(int day, string? note)
            {
                var date = new DateOnly(2026, 10, day); var time = ShiftClock.Boundaries(date, ShiftType.DAY);
                return new ShiftAssignment { UserId = ids["employee"], GeneratedByRunId = oldRun.Id, Note = note,
                    Cell = new SchedulingCell { ShiftTypeDefinitionId = definitions[0], SchedulePeriod = period, SchedulingRunId = oldRun.Id,
                        Date = date, StartsAt = time.StartsAt, EndsAt = time.EndsAt, RequiredEmployees = 1, ConfigurationVersion = 1 },
                    Shift = new Shift { SchedulePeriod = period, ShiftDate = date, StartsAt = time.StartsAt, EndsAt = time.EndsAt } };
            }
            var noted = Member(1, "Keep the operational note"); var tasked = Member(4, null);
            var task = new TaskAssignment { ShiftAssignment = tasked,
                ShiftTask = new ShiftTask { Shift = tasked.Shift, CreatedByUserId = ids["admin"], Title = "Protected linked task" },
                StartsAt = tasked.Shift.StartsAt.AddHours(1), EndsAt = tasked.Shift.StartsAt.AddHours(2) };
            db.AddRange(noted, task); await db.SaveChangesAsync();
            notedId = noted.Id; taskMemberId = tasked.Id; taskAssignmentId = task.Id;
        }
        using var admin = await Login();
        for (var i = 0; i < 2; i++)
        {
            var p = await ValidationInputs();
            Assert.All(p.Work.Where(w => w.AssignmentId == notedId || w.AssignmentId == taskMemberId), w => Assert.False(w.Replaceable));
            var run = await Generate(admin); Assert.Equal("READY", run.State);
            Assert.Empty(IndependentScheduleValidator.Check(p, run.Cells));
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        }
        using var final = factory.Services.CreateScope(); var context = final.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("Keep the operational note", (await context.Set<ShiftAssignment>().SingleAsync(a => a.Id == notedId)).Note);
        Assert.True(await context.Set<ShiftAssignment>().AnyAsync(a => a.Id == taskMemberId));
        Assert.Equal(taskMemberId, (await context.Set<TaskAssignment>().SingleAsync(a => a.Id == taskAssignmentId)).ShiftAssignmentId);
    }
}
