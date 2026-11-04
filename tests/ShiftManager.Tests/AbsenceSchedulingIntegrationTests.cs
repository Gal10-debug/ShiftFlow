using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;
public sealed partial class SchedulingTests
{
    [PostgresFact]
    public async Task Absence_draft_removal_requires_fresh_explicit_confirmation_and_preserves_unrelated_work()
    {
        var (admin, d, r) = await ManualSetup(); using (admin)
        {
            r = await Edit(admin, r, "ASSIGN", ids["employee"], d, 1);
            r = await Edit(admin, r, "ASSIGN", ids["employee"], d, 4);
            using var employee = await Login("employee"); var request = await SubmitAbsence(employee);
            var pending = await Review(admin);
            Assert.Single(pending.PendingAbsenceWarnings!); Assert.DoesNotContain(pending.Conflicts, c => c.Code == "APPROVED_ABSENCE");
            Assert.Equal("READY", (await Generate(admin)).State);
            var report = (await admin.GetFromJsonAsync<AbsenceConflictReport>($"/api/absences/{request.Id}/conflicts"))!;
            Assert.Single(report.Items); Assert.True(report.Items[0].Removable);
            var response = await Send(admin, $"/api/absences/{request.Id}/decision", new AbsenceDecisionRequest(request.Version, "APPROVED", null, report.Token), "POST");
            Assert.Equal("ABSENCE_DRAFT_CONFIRMATION", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
            r = await Edit(admin, await Review(admin), "ASSIGN", ids["employee"], d, 2);
            response = await Send(admin, $"/api/absences/{request.Id}/decision", new AbsenceDecisionRequest(request.Version, "APPROVED", null, report.Token, true), "POST");
            Assert.Equal("STALE_ABSENCE_CONFLICTS", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
            await DecideAbsence(admin, request, remove: true);
            r = await Review(admin); Assert.Single(r.Draft.Cells.SelectMany(c => c.Employees));
            Assert.Equal(new DateOnly(2026, 10, 4), r.Draft.Cells.Single(c => c.Employees.Length > 0).Date);
            Assert.Empty(r.PendingAbsenceWarnings!);
            response = await Send(admin, ReviewPath + "/assignments", new ManualAssignmentRequest(r.Token, "ASSIGN", null, ids["employee"], d, new(2026, 10, 1)), "POST");
            Assert.Equal("APPROVED_ABSENCE", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
            var run = await Generate(admin); Assert.Equal("READY", run.State);
            Assert.DoesNotContain(run.Cells.Where(c => c.Date <= new DateOnly(2026, 10, 3)).SelectMany(c => c.Employees), e => e.UserId == ids["employee"]);
            Assert.Contains((await admin.GetFromJsonAsync<ScheduleEditAudit[]>(ReviewPath + "/audit"))!, a => a.Operation == "ABSENCE_REMOVE");
        }
    }
    [PostgresFact]
    public async Task Approved_absence_blocks_replace_move_and_manual_only_assignment_with_atomic_rollback()
    {
        var (admin, d, r) = await ManualSetup(); using (admin)
        {
            await SetupTeam(admin); r = await Review(admin);
            r = await Edit(admin, r, "ASSIGN", ids["employee"], d, 4);
            r = await Edit(admin, r, "ASSIGN", ids["coordinator"], d, 1);
            using var employee = await Login("employee"); await DecideAbsence(admin, await SubmitAbsence(employee));
            r = await Review(admin);
            var source = r.Draft.Cells.SelectMany(c => c.Employees).Single(e => e.UserId == ids["employee"]);
            var target = r.Draft.Cells.SelectMany(c => c.Employees).Single(e => e.UserId == ids["coordinator"]);
            foreach (var command in new[] {
                new ManualAssignmentRequest(r.Token, "REPLACE", target.AssignmentId, ids["employee"], d, new(2026, 10, 1)),
                new ManualAssignmentRequest(r.Token, "MOVE", source.AssignmentId, null, d, new(2026, 10, 1)) })
            {
                var response = await Send(admin, ReviewPath + "/assignments", command, "POST");
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            }
            Assert.Equal(2, (await Review(admin)).Draft.Cells.SelectMany(c => c.Employees).Count());
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                (await db.Set<ShiftTypeDefinition>().SingleAsync(x => x.Slot == 12)).RequiredEmployees = 1; await db.SaveChangesAsync();
            }
            r = await Review(admin);
            var denied = await Send(admin, ReviewPath + "/assignments", new ManualAssignmentRequest(r.Token, "ASSIGN", null, ids["employee"], BoardDefinition(12), new(2026, 10, 1)), "POST");
            Assert.Equal("APPROVED_ABSENCE", (await denied.Content.ReadFromJsonAsync<ApiError>())!.Code);
            var candidates = (await admin.GetFromJsonAsync<CandidateResponse[]>($"{ReviewPath}/candidates?definitionId={d}&date=2026-10-03"))!;
            Assert.Contains("APPROVED_ABSENCE", candidates.Single(c => c.UserId == ids["employee"]).Reasons);
        }
    }
    [PostgresFact]
    public async Task Approved_absence_preserves_final_snapshot_and_conflict_resolves_only_after_new_publication()
    {
        var (admin, d, r) = await ManualSetup(); using (admin)
        {
            r = await Edit(admin, r, "ASSIGN", ids["employee"], d, 1); r = await Ack(admin, r);
            await Publish(admin, r, "PRELIMINARY"); var final = await Publish(admin, await Review(admin), "FINAL");
            using var employee = await Login("employee"); var approved = await DecideAbsence(admin, await SubmitAbsence(employee));
            Assert.Single(approved.UnresolvedConflicts); Assert.Equal(final.Id, approved.UnresolvedConflicts[0].PublicationId);
            r = await Review(admin); Assert.Contains(r.Conflicts, c => c.Code == "APPROVED_ABSENCE");
            Assert.Equal(final.Id, (await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2026/10"))!.Id);
            var assignment = r.Draft.Cells.SelectMany(c => c.Employees).Single();
            r = await Edit(admin, r, "REMOVE", null, null, 1, assignment.AssignmentId);
            Assert.Single((await admin.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/manage"))!.Single().UnresolvedConflicts);
            r = await Ack(admin, r); var replacement = await Publish(admin, r, "FINAL");
            Assert.Empty((await admin.GetFromJsonAsync<AbsenceResponse[]>("/api/absences/manage"))!.Single().UnresolvedConflicts);
            var historical = (await employee.GetFromJsonAsync<PublicationResponse>($"/api/my-schedule/2026/10?version={final.VersionNumber}"))!;
            Assert.Single(historical.Snapshot!.Cells.SelectMany(c => c.Employees));
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(replacement.Id, (await db.Set<AbsenceConflict>().SingleAsync()).ResolvedByPublicationId);
            Assert.NotNull((await db.Set<AbsenceConflict>().SingleAsync()).ResolvedAt);
        }
    }
    [PostgresFact]
    public async Task Absence_cross_month_on_call_removals_include_both_days_atomically()
    {
        var (admin, d, _) = await PairSetup(); using (admin)
        {
            const string may = "/api/admin/scheduling/periods/2027/5";
            var r = (await admin.GetFromJsonAsync<ReviewResponse>(may))!;
            var response = await Send(admin, may + "/assignments", new ManualAssignmentRequest(r.Token, "ASSIGN", null, ids["employee"], d, new(2027, 5, 1)), "POST");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var employee = await Login("employee");
            var request = await SubmitAbsence(employee, Absence() with { StartDate = new(2027, 5, 1), EndDate = new(2027, 5, 1) });
            var report = (await admin.GetFromJsonAsync<AbsenceConflictReport>($"/api/absences/{request.Id}/conflicts"))!;
            Assert.Equal(2, report.Items.Count(i => i.Removable));
            Assert.Contains(report.Items, i => i.Date == new DateOnly(2027, 4, 30));
            await DecideAbsence(admin, request, remove: true);
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.Set<ShiftAssignment>().ToArrayAsync());
            Assert.Equal(2, await db.Set<ScheduleEditAudit>().CountAsync(a => a.Operation == "ABSENCE_REMOVE"));
        }
    }
    [PostgresFact]
    public async Task Weekly_partial_absence_modification_and_cancellation_change_future_eligibility_only_after_approval()
    {
        var (admin, d, _) = await ManualSetup(); using (admin)
        {
            using var employee = await Login("employee");
            var p = Absence(true, [2]) with { FullDay = false, StartTime = new(8, 0), EndTime = new(16, 0) };
            var request = await DecideAbsence(admin, await SubmitAbsence(employee, p));
            async Task<bool> Eligible(string date, string period = ReviewPath) =>
                (await admin.GetFromJsonAsync<CandidateResponse[]>($"{period}/candidates?definitionId={d}&date={date}"))!.Single(c => c.UserId == ids["employee"]).Eligible;
            Assert.False(await Eligible("2026-10-06")); Assert.True(await Eligible("2026-10-07"));
            var response = await Send(employee, $"/api/absences/{request.Id}/changes", new AbsenceChange(request.Version, "MODIFY", p with { Weekdays = [3] }), "POST");
            var proposal = (await response.Content.ReadFromJsonAsync<AbsenceResponse>())!;
            Assert.False(await Eligible("2026-10-06")); Assert.True(await Eligible("2026-10-07"));
            var updated = await DecideAbsence(admin, proposal);
            Assert.True(await Eligible("2026-10-06")); Assert.False(await Eligible("2026-10-07"));
            Assert.False(await Eligible("2026-11-04", "/api/admin/scheduling/periods/2026/11"));
            response = await Send(employee, $"/api/absences/{updated.Id}/changes", new AbsenceChange(updated.Version, "CANCEL", null), "POST");
            var cancellation = (await response.Content.ReadFromJsonAsync<AbsenceResponse>())!;
            Assert.False(await Eligible("2026-11-04", "/api/admin/scheduling/periods/2026/11"));
            await DecideAbsence(admin, cancellation);
            Assert.True(await Eligible("2026-11-04", "/api/admin/scheduling/periods/2026/11"));
        }
    }
    [PostgresFact]
    public async Task Absence_approval_uses_period_before_account_locks_to_avoid_availability_deadlocks()
    {
        var (admin, d, r) = await ManualSetup(); using (admin)
        {
            r = await Edit(admin, r, "ASSIGN", ids["employee"], d, 1);
            using var employee = await Login("employee"); var request = await SubmitAbsence(employee);
            var report = (await admin.GetFromJsonAsync<AbsenceConflictReport>($"/api/absences/{request.Id}/conflicts"))!;
            await using var connection = new Npgsql.NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await new Npgsql.NpgsqlCommand("SELECT pg_advisory_xact_lock(71003, 24322)", connection, transaction).ExecuteNonQueryAsync();
            var approval = Send(admin, $"/api/absences/{request.Id}/decision", new AbsenceDecisionRequest(request.Version, "APPROVED", null, report.Token, true), "POST");
            var waiting = false;
            for (var n = 0; n < 100 && !waiting; n++)
            {
                waiting = (bool)(await new Npgsql.NpgsqlCommand("SELECT EXISTS (SELECT FROM pg_locks WHERE locktype='advisory' AND classid=71003 AND objid=24322 AND NOT granted)", connection, transaction).ExecuteScalarAsync())!;
                if (!waiting) await Task.Delay(10);
            }
            Assert.True(waiting);
            Assert.True((bool)(await new Npgsql.NpgsqlCommand("SELECT pg_try_advisory_xact_lock(4218072)", connection, transaction).ExecuteScalarAsync())!);
            await transaction.CommitAsync();
            Assert.Equal(HttpStatusCode.OK, (await approval).StatusCode);
        }
    }
}
