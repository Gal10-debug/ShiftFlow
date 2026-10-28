using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;
public sealed partial class SchedulingTests
{
    private const string TeamsPath = "/api/admin/teams";
    private static string Delegated(Guid user, int month = 10) => $"/api/team-availability/employees/{user}/2026/{month}";
    private static async Task<TeamResponse[]> Teams(HttpClient c) => (await c.GetFromJsonAsync<TeamResponse[]>(TeamsPath))!;
    private async Task<TeamResponse> SetupTeam(HttpClient admin)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var deputy = (await manager.FindByIdAsync(ids["coordinator"].ToString()))!;
            if (!await manager.IsInRoleAsync(deputy, "EMPLOYEE")) Assert.True((await manager.AddToRoleAsync(deputy, "EMPLOYEE")).Succeeded);
        }
        var team = (await Teams(admin))[0];
        foreach (var name in new[] { "admin", "coordinator", "employee" })
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{TeamsPath}/members/{ids[name]}", new MembershipRequest(team.Id))).StatusCode);
        team = (await Teams(admin))[0];
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{TeamsPath}/{team.Id}", new UpdateTeamRequest(team.Name, ids["admin"], ids["coordinator"], team.Version))).StatusCode);
        return (await Teams(admin))[0];
    }
    private async Task ConfigureWindow(HttpClient admin, int month = 10, bool open = false)
    {
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"/api/admin/availability/2026/{month}", new WindowRequest(clock.Now.AddDays(-1), clock.Now.AddDays(10)))).StatusCode);
        if (open) Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"/api/admin/availability/2026/{month}/state", new WindowStateRequest(true))).StatusCode);
    }
    private static SubmitAvailabilityRequest Choices(long version = 0, params int[] days) => new(version, days.Select(d => new SelectionRequest(new(2026, 10, d), "DAY", "BLOCKED")).ToArray());
    [PostgresFact]
    public async Task Four_teams_seed_idempotently_rename_and_membership_transfers_preserve_constraints()
    {
        using var admin = await Login(); var teams = await Teams(admin);
        Assert.Equal(new[] { "צוות א׳", "צוות ב׳", "צוות ג׳", "צוות ד׳" }, teams.Select(t => t.Name));
        Assert.Equal(4, teams.Select(t => t.Id).Distinct().Count());
        var t = await SetupTeam(admin);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{TeamsPath}/{t.Id}", new UpdateTeamRequest("צוות חדש", t.LeaderUserId, t.DeputyUserId, t.Version))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.MigrateAsync();
            Assert.Equal(4, await db.Set<OrganizationalTeam>().CountAsync());
            Assert.Equal("צוות חדש", (await db.Set<OrganizationalTeam>().SingleAsync(x => x.Id == t.Id)).Name);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{TeamsPath}/members/{ids["coordinator"]}", new MembershipRequest(teams[1].Id))).StatusCode);
        teams = await Teams(admin); Assert.Null(teams[0].DeputyUserId);
        Assert.Contains(teams[1].Members, m => m.Id == ids["coordinator"]);
        Assert.DoesNotContain(teams[0].Members, m => m.Id == ids["coordinator"]);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{TeamsPath}/members/{ids["admin"]}", new MembershipRequest(null))).StatusCode);
        Assert.Null((await Teams(admin))[0].LeaderUserId);
    }
    [PostgresFact]
    public async Task Leadership_requires_distinct_members_and_stale_updates_are_rejected()
    {
        using var admin = await Login(); var t = (await Teams(admin))[0];
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, $"{TeamsPath}/{t.Id}", new UpdateTeamRequest(t.Name, ids["employee"], null, t.Version))).StatusCode);
        t = await SetupTeam(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, $"{TeamsPath}/{t.Id}", new UpdateTeamRequest(t.Name, ids["admin"], ids["admin"], t.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, $"{TeamsPath}/{t.Id}", new UpdateTeamRequest(t.Name, null, null, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, $"{TeamsPath}/{t.Id}", new UpdateTeamRequest(" ", null, null, t.Version))).StatusCode);
    }
    [PostgresFact]
    public async Task PostgreSql_membership_and_leadership_constraints_are_enforced_without_service()
    {
        using var admin = await Login(); var t = await SetupTeam(admin); var other = (await Teams(admin))[1];
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var distinct = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OrganizationalTeam\" SET \"DeputyUserId\" = {ids["admin"]} WHERE \"Id\" = {t.Id}"));
        Assert.Equal("23514", distinct.SqlState);
        var member = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OrganizationalTeam\" SET \"LeaderUserId\" = {ids["admin"]} WHERE \"Id\" = {other.Id}"));
        Assert.Equal("23503", member.SqlState);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TeamMembership\" (\"UserId\", \"TeamId\") VALUES ({ids["employee"]}, {other.Id})"));
        Assert.Equal("23505", duplicate.SqlState);
    }
    [PostgresFact]
    public async Task Delegated_access_is_team_scoped_and_does_not_grant_administration()
    {
        using var admin = await Login(); await SetupTeam(admin); await ConfigureWindow(admin);
        using var deputy = await Login("coordinator"); using var employee = await Login("employee");
        Assert.Single((await deputy.GetFromJsonAsync<TeamResponse[]>("/api/team-availability/members"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/team-availability/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deputy.GetAsync(Delegated(ids["firstlogin"]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(deputy, Delegated(ids["firstlogin"]), Choices(), "POST")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deputy.GetAsync(TeamsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deputy.GetAsync($"{TeamsPath}/audit")).StatusCode);
        var demotion = await Send(admin, $"/api/users/{ids["coordinator"]}/roles", new RolesRequest(["COORDINATOR"]));
        Assert.Equal(HttpStatusCode.BadRequest, demotion.StatusCode);
        Assert.Equal("DEPUTY_REQUIRES_EMPLOYEE", (await demotion.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await deputy.GetAsync(TypesPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(deputy, GenerationPath, new { year = 2026, month = 10 }, "POST")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(deputy, Delegated(ids["employee"]), Choices(), "POST", false)).StatusCode);
        var other = (await Teams(admin))[1];
        await Send(admin, $"{TeamsPath}/members/{ids["employee"]}", new MembershipRequest(other.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await deputy.GetAsync(Delegated(ids["employee"]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deputy.GetAsync($"/api/team-availability/employees/{ids["employee"]}/periods")).StatusCode);
    }
    [PostgresFact]
    public async Task Delegated_open_and_closed_window_edits_preserve_versions_timestamps_and_atomic_audit()
    {
        using var admin = await Login(); var t = await SetupTeam(admin); await ConfigureWindow(admin, open: true);
        using var deputy = await Login("coordinator");
        var first = await Send(deputy, Delegated(ids["employee"]), Choices(0, 1), "POST"); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var saved = (await first.Content.ReadFromJsonAsync<AvailabilityResponse>())!; Assert.Equal(1, saved.Version); Assert.Equal(clock.Now.UtcDateTime, saved.LastSubmittedAt);
        await Send(admin, "/api/admin/availability/2026/10/state", new WindowStateRequest(false));
        clock.Now = clock.Now.AddMinutes(5);
        Assert.Equal(HttpStatusCode.OK, (await Send(deputy, Delegated(ids["employee"]), Choices(1, 2))).StatusCode);
        using var employee = await Login("employee");
        Assert.Equal(HttpStatusCode.Conflict, (await Send(employee, "/api/availability/2026/10", Choices(2))).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await db.Set<DelegatedAvailabilityAudit>().OrderBy(a => a.OccurredAt).ToArrayAsync(); Assert.Equal(2, audit.Length);
        Assert.True(audit[0].InitialSubmission); Assert.False(audit[1].InitialSubmission);
        Assert.Equal(ids["coordinator"], audit[1].ActorUserId); Assert.Equal(ids["employee"], audit[1].TargetUserId); Assert.Equal(t.Id, audit[1].TeamId);
        Assert.Contains("2026-10-01", audit[1].PreviousValues); Assert.Contains("2026-10-02", audit[1].NewValues);
        Assert.Equal(clock.Now.UtcDateTime, audit[1].OccurredAt);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(deputy, Delegated(ids["employee"]), Choices(1))).StatusCode);
        Assert.Equal(2, await db.Set<DelegatedAvailabilityAudit>().CountAsync());
        var history = await admin.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"{TeamsPath}/audit");
        Assert.Equal(2, history!.Length);
        Assert.Equal(2026, history[0].GetProperty("year").GetInt32());
        Assert.Equal(10, history[0].GetProperty("month").GetInt32());
    }
    [PostgresFact]
    public async Task Delegated_cross_month_three_block_limit_rolls_back_audit_and_preferences()
    {
        using var admin = await Login(); await SetupTeam(admin); await ConfigureWindow(admin, 9); await ConfigureWindow(admin);
        using var deputy = await Login("coordinator");
        var september = new SubmitAvailabilityRequest(0, [new(new(2026, 9, 29), "DAY", "BLOCKED"), new(new(2026, 9, 30), "DAY", "BLOCKED")]);
        Assert.Equal(HttpStatusCode.Created, (await Send(deputy, Delegated(ids["employee"], 9), september, "POST")).StatusCode);
        var bad = await Send(deputy, Delegated(ids["employee"]), Choices(0, 1, 2), "POST");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); Assert.Equal("WEEKLY_BLOCK_LIMIT", (await bad.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Equal(HttpStatusCode.Created, (await Send(deputy, Delegated(ids["employee"]), Choices(0, 1), "POST")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.Set<DelegatedAvailabilityAudit>().CountAsync()); Assert.Equal(3, await db.Set<ShiftPreference>().CountAsync());
    }
    [PostgresFact]
    public async Task Failure_after_audit_insert_rolls_back_entire_delegated_update()
    {
        using var admin = await Login(); await SetupTeam(admin); await ConfigureWindow(admin);
        using var deputy = await Login("coordinator");
        Assert.Equal(HttpStatusCode.Created, (await Send(deputy, Delegated(ids["employee"]), Choices(0, 1), "POST")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_submission_for_test() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'forced test failure'; END $$;
            CREATE TRIGGER fail_submission_for_test BEFORE UPDATE ON "AvailabilitySubmission"
            FOR EACH ROW EXECUTE FUNCTION fail_submission_for_test();
            """);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Send(deputy, Delegated(ids["employee"]), Choices(1, 2))).StatusCode);
        Assert.Equal(1, await db.Set<DelegatedAvailabilityAudit>().CountAsync());
        Assert.Equal(1, (await db.Set<AvailabilitySubmission>().SingleAsync()).Version);
        Assert.Equal(new DateOnly(2026, 10, 1), (await db.Set<ShiftPreference>().Select(p => p.Shift.ShiftDate).SingleAsync()));
    }
    [PostgresFact]
    public async Task Concurrent_delegated_updates_have_one_winner_and_one_audit()
    {
        using var admin = await Login(); await SetupTeam(admin); await ConfigureWindow(admin);
        using var deputy = await Login("coordinator");
        await Send(deputy, Delegated(ids["employee"]), Choices(), "POST");
        using var second = await Login("coordinator");
        var writes = await Task.WhenAll(Send(deputy, Delegated(ids["employee"]), Choices(1, 1)), Send(second, Delegated(ids["employee"]), Choices(1, 2)));
        Assert.Single(writes, w => w.StatusCode == HttpStatusCode.OK); Assert.Single(writes, w => w.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.Set<DelegatedAvailabilityAudit>().CountAsync()); Assert.Equal(2, (await db.Set<AvailabilitySubmission>().SingleAsync()).Version);
    }
    [PostgresFact]
    public async Task Delegated_block_preserves_draft_surfaces_conflict_rejects_stale_preview_and_allows_regeneration()
    {
        using var admin = await Login(); await SetupTeam(admin); await ConfigureWindow(admin); await ConfigureAutomatic(2);
        var run = await Generate(admin); Assert.Equal("READY", run.State);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{run.Id}/save", new { }, "POST")).StatusCode);
        var preview = await Generate(admin);
        using var deputy = await Login("coordinator");
        Assert.Equal(HttpStatusCode.Created, (await Send(deputy, Delegated(ids["employee"]), Choices(0, 1), "POST")).StatusCode);
        var stale = await Send(admin, $"{GenerationPath}/{preview.Id}/save", new { }, "POST");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal("STALE_PREVIEW", (await stale.Content.ReadFromJsonAsync<ApiError>())!.Code);
        var conflicts = (await admin.GetFromJsonAsync<RosterConflict[]>($"{TeamsPath}/conflicts"))!; Assert.Contains(conflicts, c => c.Code == "HARD_BLOCK_CONFLICT");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); Assert.Equal(46, await db.Set<ShiftAssignment>().CountAsync());
            var validator = scope.ServiceProvider.GetRequiredService<RosterValidationService>();
            await Assert.ThrowsAsync<ApiException>(() => validator.ValidateForPublication(conflicts[0].SchedulePeriodId));
        }
        var next = await Generate(admin); Assert.Equal("READY", next.State);
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, $"{GenerationPath}/{next.Id}/save", new { }, "POST")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<RosterConflict[]>($"{TeamsPath}/conflicts"))!);
    }
    [PostgresFact]
    public async Task Leader_excluded_even_when_qualified_and_deputy_retains_personal_workflow_and_scheduling()
    {
        using var admin = await Login(); var t = await SetupTeam(admin); await ConfigureWindow(admin, open: true); await ConfigureAutomatic(2);
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var name in new[] { "admin", "coordinator" }) { var u = (await manager.FindByIdAsync(ids[name].ToString()))!; await manager.AddToRoleAsync(u, "EMPLOYEE"); }
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); (await db.Users.SingleAsync(u => u.Id == ids["admin"])).IsShiftLeadQualified = true; await db.SaveChangesAsync();
        }
        using var leader = await Login(); using var deputy = await Login("coordinator");
        Assert.Equal(HttpStatusCode.Conflict, (await Send(leader, "/api/availability/2026/10", Choices(), "POST")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(deputy, "/api/availability/2026/10", Choices(0, 1), "POST")).StatusCode);
        using var readScope = factory.Services.CreateScope(); var p = await readScope.ServiceProvider.GetRequiredService<SchedulingInputs>().Read(2026, 10);
        Assert.DoesNotContain(p.Employees, e => e.Id == ids["admin"]); Assert.Contains(p.Employees, e => e.Id == ids["coordinator"] && !e.IsShiftLeadQualified);
        var run = await Generate(leader); Assert.Equal("READY", run.State);
        Assert.DoesNotContain(run.Cells.SelectMany(c => c.Employees), e => e.UserId == ids["admin"]);
        Assert.DoesNotContain(run.Cells.Where(c => c.Date == new DateOnly(2026, 10, 1)).SelectMany(c => c.Employees), e => e.UserId == ids["coordinator"]);
        Assert.Contains(run.Cells.SelectMany(c => c.Employees), e => e.UserId == ids["coordinator"]);
    }
    [PostgresFact]
    public async Task Appointment_reports_existing_assignments_without_deletion_and_blocks_generation()
    {
        using var admin = await Login(); var t = await SetupTeam(admin); await ConfigureAutomatic(1);
        await ConfigureWindow(admin);
        using (var setup = factory.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var shift = await db.Set<Shift>().SingleAsync(s => s.ShiftDate == new DateOnly(2026, 10, 1) && s.ShiftType == ShiftType.DAY);
            db.Add(new ShiftAssignment { ShiftId = shift.Id, UserId = ids["employee"] }); await db.SaveChangesAsync();
        }
        var result = await Send(admin, $"{TeamsPath}/{t.Id}", new UpdateTeamRequest(t.Name, ids["employee"], ids["coordinator"], t.Version));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("TEAM_LEADER_ASSIGNED", await result.Content.ReadAsStringAsync());
        var blocked = await Generate(admin); Assert.NotEqual("READY", blocked.State);
        using var scope = factory.Services.CreateScope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<ShiftAssignment>().CountAsync());
    }
}
